using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Storage;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using AISV.Game.World;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>
/// 农夫小超市：陈列台上架定价、营业时间里村民进店买东西（你在店里时真的走进来挑货、到收银台结账，不在时后台结算）、
/// 和 Pierre / Joja 抢生意、对手吃不消时上门理论、偷来的东西可能被认出来、雇店员。
/// 顾客是替身：真人在原地藏起来，替身在店里走，走完放回去，不打乱他们的日程。只在单人或主机上。
/// </summary>
public sealed partial class MartService
{
	public const string PriceKey = "AIStardewValley.Core/PricePct";

	public const string ShopperReason = "mart";

	public const string ClerkReason = "mart_clerk";

	public const string RivalReason = "mart_rival";

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly NpcDirectory _npcs;

	private readonly LedgerService _ledger;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	public MartService(Func<ModConfig> config, Func<DocumentStore?> store, NpcDirectory npcs, LedgerService ledger, MartContent content, ILog log)
	{
		_config = config;
		_store = store;
		_npcs = npcs;
		_ledger = ledger;
		Content = content;
		_log = log;
		Sales = new StoreSaleBook(store);
	}

	public MartContent Content { get; }

	public StoreSaleBook Sales { get; }

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Persona { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	public Action<string, int>? AdjustMood { get; set; }

	/// <summary>传成八卦：说法、知道的人。</summary>
	public Action<string, IReadOnlyList<string>>? Gossip { get; set; }

	/// <summary>界面层：打开陈列台的定价窗口。</summary>
	public Action<Furniture>? OpenShelf { get; set; }

	/// <summary>界面层：打开小超市应用。</summary>
	public Action? OpenApp { get; set; }

	/// <summary>界面层：上门理论时"讲道理"——显示对方的话，让玩家打一段回应。</summary>
	public Action<NPC, string, Action<string?>>? AskArgument { get; set; }

	/// <summary>配偶现在在不在店里看店（配偶日程接上）。</summary>
	public Func<bool>? SpouseMinding { get; set; }

	public MartState? State => _store()?.Get(MartState.Path, MartState.Doc);

	private bool Host => _config().FarmerMart && Context.IsWorldReady && Context.IsMainPlayer;

	private static int Today => Game1.Date.TotalDays;

	// ---------------- 店在哪、货在哪 ----------------

	public static IEnumerable<Building> Buildings() => Game1.getFarm()?.buildings.Where(b => b.buildingType.Value == MartContent.BuildingId) ?? Enumerable.Empty<Building>();

	public static IEnumerable<GameLocation> Shops() => Buildings().Where(b => !b.isUnderConstruction()).Select(b => b.GetIndoors()).Where(l => l != null)!;

	public static bool IsShop(GameLocation? l) => l != null && Shops().Contains(l);

	public static IEnumerable<Furniture> Shelves(GameLocation l) => l.furniture.Where(f => f.QualifiedItemId == MartContent.ShelfQualified);

	public static IEnumerable<(GameLocation Shop, Furniture Shelf)> AllShelves() => Shops().SelectMany(l => Shelves(l).Select(f => (l, f)));

	public static Furniture? Register(GameLocation l) => l.furniture.FirstOrDefault(f => f.QualifiedItemId == MartContent.RegisterQualified);

	public bool HasShop => Shops().Any();

	public bool IsOpen(int time) => time >= OpenAt && time < CloseAt;

	public int OpenAt => _config().FarmerMartOpen;

	public int CloseAt => _config().FarmerMartClose;

	/// <summary>原价：照游戏的价值算（含品质），不用商店收购价（Stardew Thief 会把赃物的收购价压到一成）。</summary>
	public static int BasePrice(SObject o)
	{
		double q = o.Quality switch
		{
			1 => 1.25,
			2 => 1.5,
			4 => 2,
			_ => 1
		};
		return Math.Max(1, (int)(o.Price * q * 2));
	}

	public static int Percent(Furniture f) => f.modData.TryGetValue(PriceKey, out string? v) && int.TryParse(v, out int p) ? Math.Clamp(p, 50, 200) : 100;

	public static void SetPercent(Furniture f, int pct) => f.modData[PriceKey] = Math.Clamp(pct, 50, 200).ToString();

	public static int UnitPrice(Furniture f) => f.heldObject.Value is SObject o ? Math.Max(1, BasePrice(o) * Percent(f) / 100) : 0;

	/// <summary>店里一共有几样货、平均贵几成（给竞争和来客用）。</summary>
	public (int Kinds, double Ratio) Offerings()
	{
		List<Furniture> stocked = AllShelves().Select(s => s.Shelf).Where(f => f.heldObject.Value is SObject { Stack: > 0 }).ToList();
		if (stocked.Count == 0)
		{
			return (0, 1);
		}
		double ratio = stocked.Average(f => Percent(f) / 100.0);
		if (State is MartState s && s.ConcedeUntil >= Today)
		{
			ratio *= 1.2;
		}
		return (stocked.Select(f => f.heldObject.Value!.QualifiedItemId).Distinct().Count(), ratio);
	}

	// ---------------- 点陈列台、收银台 ----------------

	public void OnButtonPressed(ButtonPressedEventArgs e, IInputHelper input)
	{
		if (!_config().FarmerMart || !Context.IsPlayerFree || !e.Button.IsActionButton() || !IsShop(Game1.currentLocation))
		{
			return;
		}
		Vector2 tile = e.Cursor.GrabTile;
		Furniture? f = Game1.currentLocation.furniture.FirstOrDefault(x => (x.QualifiedItemId == MartContent.ShelfQualified || x.QualifiedItemId == MartContent.RegisterQualified)
			&& x.GetBoundingBox().Contains((int)tile.X * 64 + 32, (int)tile.Y * 64 + 32));
		if (f == null || Vector2.Distance(Game1.player.Tile, tile) > 2.5f)
		{
			return;
		}
		input.Suppress(e.Button);
		if (f.QualifiedItemId == MartContent.RegisterQualified)
		{
			ClickRegister();
		}
		else
		{
			ClickShelf(f);
		}
	}

	private void ClickRegister()
	{
		MartState? s = State;
		if (s != null && s.Cashbox > 0)
		{
			int cash = s.Cashbox;
			s.Cashbox = 0;
			_store()?.MarkDirty(MartState.Path);
			Game1.player.Money += cash;
			Game1.playSound("purchaseRepeat");
			Hints.Hud(Tr.Get("mart.cashOut", "从钱箱里取出 {{gold}}g。", ("gold", cash)));
			return;
		}
		OpenApp?.Invoke();
	}

	private void ClickShelf(Furniture f)
	{
		Item? held = Game1.player.ActiveItem;
		SObject? onShelf = f.heldObject.Value;
		if (held is SObject o && held is not Furniture && !o.bigCraftable.Value && o.canBeShipped() || held is SObject { Category: SObject.CookingCategory or SObject.artisanGoodsCategory } && held is not Furniture)
		{
			SObject item = (SObject)held;
			if (onShelf == null)
			{
				f.heldObject.Value = item;
				Game1.player.removeItemFromInventory(item);
				SetPercent(f, Percent(f));
				Game1.playSound("woodyStep");
				Hints.Once("ShelfHint", Tr.Get("mart.shelfHint", "上架了！空手点陈列台可以改价钱或取下；营业时间里村民会来买。"), 9000);
				OpenShelf?.Invoke(f);
				return;
			}
			if (onShelf.canStackWith(item))
			{
				int left = onShelf.addToStack(item);
				if (left <= 0)
				{
					Game1.player.removeItemFromInventory(item);
				}
				else
				{
					item.Stack = left;
				}
				Game1.playSound("woodyStep");
				return;
			}
		}
		OpenShelf?.Invoke(f);
	}

	/// <summary>把陈列台上的货收回背包（放不下就掉在脚边）。</summary>
	public static void TakeBack(Furniture f)
	{
		if (f.heldObject.Value is SObject o)
		{
			f.heldObject.Value = null;
			if (!Game1.player.addItemToInventoryBool(o))
			{
				Game1.createItemDebris(o, Game1.player.getStandingPosition(), -1);
			}
			Game1.playSound("pickUpItem");
		}
	}

	// ---------------- 每天 ----------------

	public void OnDayStarted()
	{
		if (!Host || State is not MartState s)
		{
			return;
		}
		foreach (Building b in Buildings().Where(b => !b.isUnderConstruction()))
		{
			string id = b.id.Value.ToString();
			if (!s.Welcomed.Contains(id))
			{
				s.Welcomed.Add(id);
				_store()!.MarkDirty(MartState.Path);
				Welcome();
			}
		}
		UpdateCompetition(s);
		_rivalNoticeDay = -1;
	}

	/// <summary>刚建好：送两张陈列台和一个收银台，弹一页说明。</summary>
	private void Welcome()
	{
		foreach (string id in new[] { MartContent.ShelfQualified, MartContent.ShelfQualified, MartContent.RegisterQualified })
		{
			Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create(id));
		}
		Hints.Hud(Tr.Get("mart.welcome", "农夫小超市建好了！送你两张陈列台和一个收银台。"), 8000);
		Game1.player.mailReceived.Add("AISV_MartBuilt");
		_log.Info("农夫小超市建好了，送了开店礼包。");
	}

	public void OnDayEnding()
	{
		CleanupVisits();
		CleanupClerks();
		if (!Host || State is not MartState s)
		{
			return;
		}
		PayClerks(s);
	}

	// ---------------- 顾客 ----------------

	public void OnTimeChanged()
	{
		if (!Host || !HasShop || Game1.isFestival() || Game1.eventUp && !IsShop(Game1.currentLocation))
		{
			return;
		}
		int time = Game1.timeOfDay;
		ClerkShift(time);
		if (!IsOpen(time))
		{
			CleanupVisits();
			return;
		}
		TryConfront(time);
		if (time % 100 != 0 && time % 100 != 30)
		{
			return;
		}
		(int kinds, double ratio) = Offerings();
		double share = State?.Shares.LastOrDefault()?.Ours ?? 0.2;
		int arrivals = CustomerDemand.Arrivals(kinds, ratio, Math.Max(0.2, share), _random.NextDouble());
		if (State?.Clerks.Count > 0)
		{
			arrivals += _random.NextDouble() < 0.3 ? 1 : 0;
		}
		for (int i = 0; i < arrivals; i++)
		{
			if (Customer() is not NPC buyer)
			{
				break;
			}
			GameLocation shop = Shops().First();
			if (Game1.currentLocation == shop && _visits.Count < 3 && StartVisit(buyer, shop))
			{
				continue;
			}
			SellTo(buyer, shop, live: false);
		}
	}

	/// <summary>调试：马上叫一位顾客（你在店里就真的进门，不在就后台结算）。</summary>
	public string DebugCustomer()
	{
		if (!HasShop || Customer() is not NPC buyer)
		{
			return "没店，或者没有能来的村民。";
		}
		GameLocation shop = Shops().First();
		if (Game1.currentLocation == shop && StartVisit(buyer, shop))
		{
			return $"{buyer.Name} 进店了。";
		}
		SellTo(buyer, shop, live: false);
		return $"{buyer.Name} 在后台逛了一圈。";
	}

	/// <summary>住得太远或不会来镇上逛店的人（矿井、下水道、沙漠、姜岛、巫师塔……）。</summary>
	private static readonly HashSet<string> FarAway = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dwarf", "Krobus", "Sandy", "Leo", "Wizard", "Bouncer", "Gunther", "Marlon", "Henchman", "Mister Qi", "Birdie", "Fizz", "Gil" };

	/// <summary>挑一位来买东西的村民：开着 AI、不被别的事占着、今天还没逛过店、有点钱。</summary>
	private NPC? Customer()
	{
		List<NPC> pool = Utility.getAllVillagers().Where(n => n.currentLocation != null && !n.IsInvisible && n.CanSocialize && !Kids.IsChild(n) && _npcs.IsEnabled(n)
			&& !NpcBusy.Is(n.Name) && _visits.All(v => v.Real != n) && n.Name != "Pierre" && n.Name != "Morris" && !FarAway.Contains(n.Name) && _ledger.Wallet(n.Name) is { } w && w.ShoppedDay != Today && w.Gold >= 30).ToList();
		return pool.Count == 0 ? null : pool[_random.Next(pool.Count)];
	}

	/// <summary>这位顾客挑一样货（没有想买的返回 null）。</summary>
	private (Furniture Shelf, int Qty)? Choose(NPC buyer, GameLocation shop)
	{
		List<Furniture> shelves = Shelves(shop).Where(f => f.heldObject.Value is SObject { Stack: > 0 }).ToList();
		if (shelves.Count == 0 || _ledger.Wallet(buyer.Name) is not { } w)
		{
			return null;
		}
		List<Offer> offers = shelves.Select(f =>
		{
			SObject o = f.heldObject.Value!;
			int taste = Safe(() => buyer.getGiftTasteForThisItem(o), 8);
			bool needed = buyer.Name == "Gus" && o.Category is SObject.VegetableCategory or SObject.FruitsCategory or SObject.EggCategory or SObject.MilkCategory;
			return new Offer(o.QualifiedItemId, UnitPrice(f), BasePrice(o), o.Quality, o.Stack, taste, needed);
		}).ToList();
		int budget = Math.Max(30, w.Gold / 3);
		(int index, int qty) = CustomerDemand.Pick(offers, budget, _random.NextDouble());
		return index < 0 ? null : (shelves[index], qty);
	}

	private static T Safe<T>(Func<T> f, T fallback)
	{
		try
		{
			return f();
		}
		catch
		{
			return fallback;
		}
	}

	/// <summary>成交（或认出赃物拿回去）。live：替身就在店里，冒一句话。</summary>
	private void SellTo(NPC buyer, GameLocation shop, bool live, NPC? speaker = null, (Furniture Shelf, int Qty)? choice = null)
	{
		if ((choice ?? Choose(buyer, shop)) is not (Furniture shelf, int qty) || shelf.heldObject.Value is not SObject stock || State is not MartState s)
		{
			if (_ledger.Wallet(buyer.Name) is { } w)
			{
				w.ShoppedDay = Today;
			}
			return;
		}
		qty = Math.Min(qty, stock.Stack);
		int price = UnitPrice(shelf);
		SObject sold = (SObject)stock.getOne();
		sold.Stack = qty;
		stock.Stack -= qty;
		if (stock.Stack <= 0)
		{
			shelf.heldObject.Value = null;
		}
		StoreSale sale = new StoreSale
		{
			Source = StoreSale.Ours,
			TxId = $"{Today}-{s.NextTx++}",
			Buyer = buyer.Name,
			ItemId = sold.QualifiedItemId,
			ItemName = sold.DisplayName,
			Qty = qty,
			Quality = sold.Quality,
			UnitPrice = price,
			MarketPrice = BasePrice(sold),
			Stolen = sold.modData.TryGetValue("StardewThief/IsStolen", out string? st) && st == "true",
			StolenFrom = sold.modData.TryGetValue("StardewThief/StolenFrom", out string? from) ? from : "",
			StolenDay = sold.modData.TryGetValue("StardewThief/StolenDay", out string? sd) && int.TryParse(sd, out int d) ? d : 0,
			Day = Today,
			Time = Game1.timeOfDay
		};
		bool caught = sale.Stolen && _config().StolenGoodsNoticed && CheckStolen(sale, buyer);
		if (caught)
		{
			sale.Reclaimed = true;
		}
		_store()!.MarkDirty(MartState.Path);
		Sales.Record(sale);
		if (caught)
		{
			Caught(sale, buyer, speaker ?? (live ? buyer : null));
			return;
		}
		bool attended = Game1.currentLocation == shop || s.Clerks.Count > 0 || SpouseMinding?.Invoke() == true;
		if (attended)
		{
			Game1.player.Money += sale.Total;
		}
		else
		{
			s.Cashbox += sale.Total;
		}
		s.Reputation = Math.Clamp(s.Reputation + (Percent(shelf) <= 100 ? 1 : Percent(shelf) >= 150 ? -1 : 0), -100, 100);
		if (Game1.currentLocation == shop)
		{
			Game1.playSound("purchase");
			Hints.Hud(Tr.Get("mart.sold", "{{name}}买了{{item}} ×{{n}}，+{{gold}}g", ("name", Display(buyer.Name)), ("item", sale.ItemName), ("n", qty), ("gold", sale.Total)), 4000);
			if (speaker != null)
			{
				Comment(speaker, buyer, sale);
			}
		}
	}

	/// <summary>偶尔说一句对店的看法（AI 用不了就不说）。</summary>
	private void Comment(NPC speaker, NPC buyer, StoreSale sale)
	{
		if (Ai is not IAiSender ai || !_config().AiOn || _random.NextDouble() > 0.35)
		{
			return;
		}
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(buyer.Name)}。\n{Persona?.Invoke(buyer.Name) ?? ""}\n\n# 现在\n你在{Game1.player.Name}开的农夫小超市里，刚花 {sale.Total}g 买了{sale.ItemName}×{sale.Qty}（原价大约 {sale.MarketPrice}g 一个）。\n\n# 输出\n顺口说一句对这家店或这样东西的看法，20 个字以内，不加引号。";
		Task.Run(async () =>
		{
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("store_comment", system, "说一句。", 200, 0.9)).ConfigureAwait(false);
				string? line = r.Ok ? ChildPrompts.Clean(r.Text) : null;
				if (line != null)
				{
					MainThread.Run(() => NpcSpeech.Say(speaker, line));
				}
			}
			catch (Exception ex)
			{
				_log.Debug("顾客的话没生成：" + ex.Message);
			}
		});
	}

	// ---------------- 赃物 ----------------

	private bool CheckStolen(StoreSale sale, NPC buyer)
	{
		bool victim = string.Equals(sale.StolenFrom, buyer.Name, StringComparison.OrdinalIgnoreCase);
		bool close = !victim && sale.StolenFrom.Length > 0 && Close(buyer.Name, sale.StolenFrom);
		double chance = StolenRules.Chance(victim, close, sale.MarketPrice, sale.UnitPrice, sale.StolenDay > 0 ? Today - sale.StolenDay : 0);
		return _random.NextDouble() < chance;
	}

	/// <summary>是不是一家人或很要好（照账本里的家人表，再加上同住一个家的人）。</summary>
	private static bool Close(string a, string b)
	{
		if (AISV.Core.World.Ledger.LedgerRules.Parents.TryGetValue(a, out string? pa) && pa.Split(',').Contains(b) || AISV.Core.World.Ledger.LedgerRules.Parents.TryGetValue(b, out string? pb) && pb.Split(',').Contains(a))
		{
			return true;
		}
		string? ha = Game1.characterData.TryGetValue(a, out var da) ? da.Home?.FirstOrDefault()?.Location : null;
		string? hb = Game1.characterData.TryGetValue(b, out var db) ? db.Home?.FirstOrDefault()?.Location : null;
		return ha != null && ha == hb;
	}

	private void Caught(StoreSale sale, NPC buyer, NPC? speaker)
	{
		string owner = sale.StolenFrom.Length > 0 ? sale.StolenFrom : buyer.Name;
		_ledger.Reclaim(owner, sale.ItemId, sale.Qty, sale.Quality);
		int penalty = _config().CaughtStealingPenalty;
		Game1.player.changeFriendship(-penalty, buyer);
		if (!string.Equals(owner, buyer.Name, StringComparison.OrdinalIgnoreCase) && Game1.getCharacterFromName(owner) is NPC victim)
		{
			Game1.player.changeFriendship(-penalty / 2, victim);
		}
		AdjustMood?.Invoke(buyer.Name, -10);
		if (State is MartState s)
		{
			s.Reputation = Math.Clamp(s.Reputation - 20, -100, 100);
		}
		string line = string.Equals(owner, buyer.Name, StringComparison.OrdinalIgnoreCase)
			? Tr.Get("mart.caughtOwn", "这不是我家丢的{{item}}吗？！怎么在你店里卖？", ("item", sale.ItemName))
			: Tr.Get("mart.caughtKin", "等等，这{{item}}是{{owner}}家丢的那个吧？", ("item", sale.ItemName), ("owner", Display(owner)));
		if (speaker != null)
		{
			speaker.doEmote(12);
			NpcSpeech.Say(speaker, line);
		}
		Hints.Hud(Tr.Get("mart.caughtHud", "{{name}}认出{{item}}是偷来的，拿走了，没付钱。店的口碑变差了。", ("name", Display(buyer.Name)), ("item", sale.ItemName)), 9000);
		Gossip?.Invoke(Tr.Get("mart.caughtGossip", "{{name}}在{{farmer}}的店里认出了{{owner}}家被偷的{{item}}。", ("name", Display(buyer.Name)), ("farmer", Game1.player.Name), ("owner", Display(owner)), ("item", sale.ItemName)), new[] { buyer.Name, owner });
		_log.Info($"{buyer.Name} 认出了赃物 {sale.ItemName}（失主 {owner}）。");
	}

	/// <summary>COS 那边的成交：认赃物只能事后算后果（它那边的货已经卖出去了）。</summary>
	public void OnSale(StoreSale sale)
	{
		_ledger.OnStorePurchase(sale);
	}
}
