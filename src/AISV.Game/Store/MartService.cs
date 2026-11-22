using System;
using System.Collections.Generic;
using System.Linq;
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
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>
/// 农夫小超市：照皮埃尔杂货店的样子开一间店。几种货架（白货架、冷柜、保温柜、促销桶、木柜）各放各的货，
/// 货有新鲜度、存放温度、来路，每天有行情；选店型、进货、贴海报、雇店员；营业时间里村民按自己的类型来买东西，
/// 你在店里时他们真的走进来挑货、到柜台排队，你来收银找零；不在时后台结算。买完会留评价，评价决定口碑和客流。
/// 还会和 Pierre / Joja 抢生意、对手吃不消时上门理论、偷来的东西可能被认出来。
/// 顾客是替身：真人在原地藏起来，替身在店里走，走完放回去，不打乱他们的日程。只在单人或主机上。
/// </summary>
public sealed partial class MartService
{
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

	/// <summary>界面层：打开货架（上架、定价、取下）。</summary>
	public Action<Furniture>? OpenShelf { get; set; }

	/// <summary>界面层：打开小超市应用。</summary>
	public Action? OpenApp { get; set; }

	/// <summary>界面层：收银小游戏。</summary>
	public Action<CheckoutRequest>? OpenCheckout { get; set; }

	/// <summary>界面层：上门理论时"讲道理"——显示对方的话，让玩家打一段回应。</summary>
	public Action<NPC, string, Action<string?>>? AskArgument { get; set; }

	/// <summary>配偶现在在不在店里看店（配偶日程接上）。</summary>
	public Func<bool>? SpouseMinding { get; set; }

	public MartState? State => _store()?.Get(MartState.Path, MartState.Doc);

	private void Dirty() => _store()?.MarkDirty(MartState.Path);

	private bool Host => _config().FarmerMart && Context.IsWorldReady && Context.IsMainPlayer;

	private static int Today => Game1.Date.TotalDays;

	// ---------------- 店在哪、货在哪 ----------------

	public static IEnumerable<Building> Buildings() => Game1.getFarm()?.buildings.Where(b => b.buildingType.Value == MartContent.BuildingId) ?? Enumerable.Empty<Building>();

	public static IEnumerable<GameLocation> Shops() => Buildings().Where(b => !b.isUnderConstruction()).Select(b => b.GetIndoors()).Where(l => l != null)!;

	public static bool IsShop(GameLocation? l) => l != null && Shops().Contains(l);

	public static IEnumerable<Furniture> Shelves(GameLocation l) => l.furniture.Where(MartShelves.IsShelf);

	public static IEnumerable<(GameLocation Shop, Furniture Shelf)> AllShelves() => Shops().SelectMany(l => Shelves(l).Select(f => (l, f)));

	public bool HasShop => Shops().Any();

	public bool IsOpen(int time) => !Suspended && time >= OpenAt && time < CloseAt;

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

	/// <summary>店里一共有几样货、平均贵几成（给竞争和来客用）。</summary>
	public (int Kinds, double Ratio) Offerings()
	{
		List<SObject> stocked = AllShelves().SelectMany(s => MartShelves.Goods(s.Shelf)).ToList();
		if (stocked.Count == 0)
		{
			return (0, 1);
		}
		double ratio = stocked.Average(o => MartShelves.Percent(o) / 100.0);
		if (State is MartState s && s.ConcedeUntil >= Today)
		{
			ratio *= 1.2;
		}
		return (stocked.Select(o => o.QualifiedItemId).Distinct().Count(), ratio);
	}

	// ---------------- 点货架、柜台、库房 ----------------

	public void OnButtonPressed(ButtonPressedEventArgs e, IInputHelper input)
	{
		if (!_config().FarmerMart || !Context.IsPlayerFree || !e.Button.IsActionButton() || !IsShop(Game1.currentLocation))
		{
			return;
		}
		Vector2 tile = e.Cursor.GrabTile;
		if (Vector2.Distance(Game1.player.Tile, tile) > 2.5f)
		{
			return;
		}
		if (MartInterior.IsCounter((int)tile.X, (int)tile.Y))
		{
			input.Suppress(e.Button);
			ClickCounter();
			return;
		}
		Furniture? f = Game1.currentLocation.furniture.FirstOrDefault(x => (MartShelves.IsShelf(x) || x.QualifiedItemId == MartContent.RegisterQualified)
			&& x.GetBoundingBox().Contains((int)tile.X * 64 + 32, (int)tile.Y * 64 + 32));
		if (f == null)
		{
			return;
		}
		input.Suppress(e.Button);
		if (f.QualifiedItemId == MartContent.RegisterQualified)
		{
			ClickCounter();
		}
		else
		{
			ClickShelf(f);
		}
	}

	/// <summary>点柜台：有人排队就收银；没人排队时取出钱箱里的钱，再没有就打开小超市应用。</summary>
	private void ClickCounter()
	{
		if (ServeNext())
		{
			return;
		}
		MartState? s = State;
		if (s != null && s.Cashbox > 0)
		{
			int cash = s.Cashbox;
			s.Cashbox = 0;
			Dirty();
			Game1.player.Money += cash;
			Game1.playSound("purchaseRepeat");
			Hints.Hud(Tr.Get("mart.cashOut", "从钱箱里取出 {{gold}}g。", ("gold", cash)));
			return;
		}
		OpenApp?.Invoke();
	}

	/// <summary>拿着能卖的东西点货架：放上去（这个货架不收这类货就说一声）；空手点：打开货架。</summary>
	private void ClickShelf(Furniture f)
	{
		if (Game1.player.ActiveItem is SObject held && MartShelves.Sellable(held))
		{
			int n = MartShelves.Add(f, held, held.Stack, Origins.Farm);
			if (n > 0)
			{
				held.Stack -= n;
				if (held.Stack <= 0)
				{
					Game1.player.removeItemFromInventory(held);
				}
				Game1.playSound("woodyStep");
				MartShelves.RefreshDisplay(Game1.currentLocation);
				Hints.Tip("ShelfHint", Tr.Get("mart.tipShelf", "货架上架和定价"));
				return;
			}
			if (n < 0)
			{
				Hints.Hud(Tr.Get("mart.wrongShelf", "{{shelf}}不放{{item}}这类东西。", ("shelf", ShelfName(f)), ("item", held.DisplayName)));
				return;
			}
			Hints.Hud(Tr.Get("mart.shelfFull", "{{shelf}}放满了。", ("shelf", ShelfName(f))));
			return;
		}
		OpenShelf?.Invoke(f);
	}

	public static string ShelfName(Furniture f) => MartShelves.Kind(f) is ShelfKind k && MartContent.FixtureOf(k) is MartContent.Fixture x ? x.Name() : f.DisplayName;

	// ---------------- 每天 ----------------

	public void OnDayStarted()
	{
		if (!Host || State is not MartState s)
		{
			return;
		}
		OpenNewShops(s);
		Deliver(s);
		ExpirePromotions(s);
		ExpireContracts(s);
		UpdateCompetition(s);
		_rivalNoticeDay = -1;
	}

	/// <summary>刚建好：店里已经摆好货架，弹一页说明。</summary>
	private void Welcome()
	{
		Hints.Hud(Tr.Get("mart.welcome", "农夫小超市建好了！货架、冷柜和柜台都摆好了。"), 8000);
		Game1.player.mailReceived.Add("AISV_MartBuilt");
		_log.Info("农夫小超市建好了，摆好了开店的货架。");
	}

	/// <summary>刚建好的店摆上货架和库房。平时早上做；当天建好（比如作弊菜单的立刻建造）时，进门那一刻做。</summary>
	private void OpenNewShops(MartState s)
	{
		foreach (Building b in Buildings().Where(b => !b.isUnderConstruction()))
		{
			string id = b.id.Value.ToString();
			if (b.GetIndoors() is GameLocation inside)
			{
				if (!s.Welcomed.Contains(id))
				{
					s.Welcomed.Add(id);
					Dirty();
					Furnish(inside);
					Welcome();
				}
				EnsureStockroom(inside);
			}
		}
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
		StockerRounds(time);
		SpawnMegaphone();
		if (time % 100 != 0 && time % 100 != 30)
		{
			return;
		}
		int arrivals = Traffic.Arrivals(ExpectedCustomers(time), _random.NextDouble());
		for (int i = 0; i < arrivals; i++)
		{
			Arrive();
		}
	}

	/// <summary>来一位顾客：你在店里就真的进门（店里最多 4 位），不在就后台逛一圈。</summary>
	private bool Arrive()
	{
		if (Customer() is not NPC buyer)
		{
			return false;
		}
		GameLocation shop = Shops().First();
		if (Game1.currentLocation == shop && _visits.Count < 4 && StartVisit(buyer, shop))
		{
			return true;
		}
		Background(buyer, shop);
		return true;
	}

	/// <summary>这半小时预计来几位（客流规则 + 店型本事 + 迎宾）。</summary>
	private double ExpectedCustomers(int time)
	{
		MartState? s = State;
		(int kinds, double ratio) = Offerings();
		double share = s?.Shares.LastOrDefault()?.Ours ?? 0.2;
		bool weekend = Game1.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
		bool eve = Utility.isFestivalDay(Game1.dayOfMonth + 1, Game1.season);
		double promo = s == null ? 0 : Promotions.TrafficBonus(s.Promotions, Today);
		if (OnDuty(Staff.Greeter))
		{
			promo += 0.1;
		}
		ShopType type = ShopTypes.Get(s?.ShopType);
		int level = ShopTypes.Level(s?.Xp ?? 0);
		if (level >= 5 && (type == ShopTypes.Florist && weekend || type == ShopTypes.Deli && (time is >= 1100 and < 1300 or >= 1700 and < 1900)))
		{
			promo += 0.2;
		}
		return Traffic.Expected(kinds, ratio, Math.Max(0.2, share), time, weekend, Game1.isRaining, eve, s == null ? 3 : ReviewRules.Average(s.Reviews), promo);
	}

	/// <summary>调试：马上叫一位顾客（你在店里就真的进门，不在就后台结算）。</summary>
	public string DebugCustomer(string? name = null)
	{
		NPC? pick = name != null ? Game1.getCharacterFromName(name) : Customer();
		if (!HasShop || pick is not NPC buyer)
		{
			return "没店，或者没有能来的村民。";
		}
		GameLocation shop = Shops().First();
		if (Game1.currentLocation == shop && StartVisit(buyer, shop))
		{
			return $"{buyer.Name} 进店了。";
		}
		Background(buyer, shop);
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

	/// <summary>店里所有在卖的货（货架、第几格、货）。</summary>
	private static List<(Furniture Shelf, int Slot, SObject Item)> Stock(GameLocation shop)
	{
		List<(Furniture, int, SObject)> list = new List<(Furniture, int, SObject)>();
		foreach (Furniture f in Shelves(shop))
		{
			StardewValley.Inventories.Inventory slots = MartShelves.Slots(f);
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] is SObject o && o.Stack > 0)
				{
					list.Add((f, i, o));
				}
			}
		}
		return list;
	}

	/// <summary>
	/// 这位顾客想买什么：先定是哪类顾客，再照喜好、价格、品质、新鲜度、温度、行情、店型、促销给每样货打分，挑一篮子（还没拿下货架）。
	/// </summary>
	private (Archetype Type, List<Pick> Picks, bool Wanted) Plan(NPC buyer, GameLocation shop)
	{
		MartState? s = State;
		List<(Furniture Shelf, int Slot, SObject Item)> stock = Stock(shop);
		ShopType type = ShopTypes.Get(s?.ShopType);
		int level = ShopTypes.Level(s?.Xp ?? 0);
		HashSet<string> groups = stock.Select(x => MartShelves.Group(x.Item)).ToHashSet();
		Archetype a = Archetypes.Pick(buyer.Name, type, Game1.timeOfDay, groups, _random.NextDouble());
		if (stock.Count == 0 || _ledger.Wallet(buyer.Name) is not { } w)
		{
			return (a, new List<Pick>(), false);
		}
		MarketDay market = Market.For(Today, Game1.seasonIndex, Game1.isRaining);
		List<Promotion> promos = s?.Promotions ?? new List<Promotion>();
		List<Offer> offers = stock.Select(x =>
		{
			SObject o = x.Item;
			string group = MartShelves.Group(o);
			int taste = Safe(() => buyer.getGiftTasteForThisItem(o), 8);
			bool needed = buyer.Name == "Gus" && group is AISV.Core.Store.Goods.Vegetable or AISV.Core.Store.Goods.Fruit or AISV.Core.Store.Goods.Animal;
			string stage = MartShelves.Stage(x.Shelf, o, longerChilled: type == ShopTypes.Fishmonger && level >= 5);
			double boost = Freshness.Appeal(stage) * market.Of(group) * ShopTypes.Demand(type, group, level) * Promotions.PosterBoost(promos, group, Today)
				* Promotions.WindowBoost(promos, o.QualifiedItemId, Today) * (MartShelves.Kind(x.Shelf)?.Attention ?? 1) * (MartShelves.WrongTemp(x.Shelf, o) ? 0.75 : 1);
			double tolerance = Origins.Premium(MartShelves.Origin(o), group) + ShopTypes.Tolerance(type, group, level) + Perk(type, level, o, group);
			return new Offer(o.QualifiedItemId, MartShelves.UnitPrice(o), BasePrice(o), o.Quality, o.Stack, taste, needed, group, boost, tolerance);
		}).ToList();
		int budget = Math.Max(80, w.Gold / 2);
		List<Pick> picks = CustomerDemand.Basket(offers, a, budget, _random).Select(p => new Pick { Shelf = stock[p.Index].Shelf, Slot = stock[p.Index].Slot, ItemId = stock[p.Index].Item.QualifiedItemId, Qty = p.Qty }).ToList();
		return (a, picks, offers.Any(o => a.Groups.Contains(o.Group)));
	}

	/// <summary>店型 5 级的本事里和价格有关的：蔬果店自家种的、工坊店金星以上、珍宝店的矿石文物，顾客多接受一成价。</summary>
	private static double Perk(ShopType type, int level, SObject o, string group)
	{
		if (level < 5)
		{
			return 0;
		}
		if (type == ShopTypes.Greengrocer && MartShelves.Origin(o) != Origins.Supplier && type.Groups.Contains(group))
		{
			return 0.1;
		}
		if (type == ShopTypes.Workshop && o.Quality >= 2 && group == AISV.Core.Store.Goods.Artisan)
		{
			return 0.1;
		}
		return type == ShopTypes.Curio && type.Groups.Contains(group) ? 0.1 : 0;
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

	/// <summary>从货架上拿下来放进篮子（货被别人买走了就拿不到）。</summary>
	private static Line? TakeLine(Pick p)
	{
		StardewValley.Inventories.Inventory slots = MartShelves.Slots(p.Shelf);
		if (p.Slot >= slots.Count || slots[p.Slot] is not SObject o || o.QualifiedItemId != p.ItemId)
		{
			return null;
		}
		string stage = MartShelves.Stage(p.Shelf, o);
		bool wrong = MartShelves.WrongTemp(p.Shelf, o);
		SObject? taken = MartShelves.Take(p.Shelf, p.Slot, p.Qty);
		return taken == null ? null : new Line { Shelf = p.Shelf, Item = taken, Price = MartShelves.UnitPrice(taken), Stage = stage, WrongTemp = wrong };
	}

	/// <summary>没买成：货放回货架（放不回去就进库房）。</summary>
	private static void PutBack(Line l)
	{
		int n = MartShelves.Add(l.Shelf, l.Item, l.Item.Stack, MartShelves.Origin(l.Item));
		if (n < l.Item.Stack)
		{
			l.Item.Stack -= Math.Max(0, n);
			MartShelves.Stockroom().Add(l.Item);
		}
	}

	/// <summary>你不在店里：按同样的规则挑货、结账、留评价。</summary>
	private void Background(NPC buyer, GameLocation shop)
	{
		(Archetype a, List<Pick> picks, bool wanted) = Plan(buyer, shop);
		List<Line> lines = picks.Select(TakeLine).Where(l => l != null).ToList()!;
		if (_ledger.Wallet(buyer.Name) is { } w)
		{
			w.ShoppedDay = Today;
		}
		if (lines.Count == 0)
		{
			// 店里根本没有想要的那类货才算"没找到"；有但嫌贵、钱不够的就不留评价。
			if (!wanted && _random.NextDouble() < 0.3)
			{
				WriteReview(buyer, a, lines, found: false, wait: 0, checkout: 0);
			}
			return;
		}
		Settle(buyer, lines, live: false, speaker: null, tip: 0);
		WriteReview(buyer, a, lines, found: true, wait: _random.Next(2, 9), checkout: 0);
	}

	/// <summary>
	/// 结账：每一样记一笔成交（偷来的可能当场被认出、拿走不付钱）；钱进你身上（你在店里、有收银员或配偶看店）或者钱箱。返回实收多少。
	/// </summary>
	private int Settle(NPC buyer, List<Line> lines, bool live, NPC? speaker, int tip)
	{
		if (State is not MartState s)
		{
			return 0;
		}
		GameLocation? shop = Shops().FirstOrDefault();
		int total = 0;
		foreach (Line l in lines)
		{
			SObject sold = l.Item;
			StoreSale sale = new StoreSale
			{
				Source = StoreSale.Ours,
				TxId = $"{Today}-{s.NextTx++}",
				Buyer = buyer.Name,
				ItemId = sold.QualifiedItemId,
				ItemName = sold.DisplayName,
				Qty = sold.Stack,
				Quality = sold.Quality,
				UnitPrice = l.Price,
				MarketPrice = BasePrice(sold),
				Stolen = sold.modData.TryGetValue("StardewThief/IsStolen", out string? st) && st == "true",
				StolenFrom = sold.modData.TryGetValue("StardewThief/StolenFrom", out string? from) ? from : "",
				StolenDay = sold.modData.TryGetValue("StardewThief/StolenDay", out string? sd) && int.TryParse(sd, out int d) ? d : 0,
				Day = Today,
				Time = Game1.timeOfDay
			};
			bool caught = sale.Stolen && _config().StolenGoodsNoticed && CheckStolen(sale, buyer);
			sale.Reclaimed = caught;
			Sales.Record(sale);
			if (caught)
			{
				Caught(sale, buyer, speaker);
				continue;
			}
			total += sale.Total;
		}
		total += tip;
		s.Xp += total;
		bool attended = live || Game1.currentLocation == shop || OnDuty(Staff.Cashier) || SpouseMinding?.Invoke() == true;
		if (attended)
		{
			Game1.player.Money += total;
		}
		else
		{
			s.Cashbox += total;
		}
		Dirty();
		if (Game1.currentLocation == shop && total > 0)
		{
			Game1.playSound("purchase");
			string items = string.Join(Tr.Get("list.sep", "、"), lines.Select(l => $"{l.Item.DisplayName}×{l.Item.Stack}"));
			Hints.Hud(Tr.Get("mart.sold", "{{name}}买了{{item}}，+{{gold}}g", ("name", Display(buyer.Name)), ("item", items), ("gold", total)), 4000);
			if (speaker != null && lines.Count > 0)
			{
				Comment(speaker, buyer, lines[0], total);
			}
		}
		if (shop != null && Game1.currentLocation == shop)
		{
			MartShelves.RefreshDisplay(shop);
		}
		return total;
	}

	/// <summary>偶尔说一句对店的看法（AI 用不了就不说）。</summary>
	private void Comment(NPC speaker, NPC buyer, Line line, int total)
	{
		if (Ai is not IAiSender ai || !_config().AiOn || _random.NextDouble() > 0.35)
		{
			return;
		}
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(buyer.Name)}。\n{Persona?.Invoke(buyer.Name) ?? ""}\n\n# 现在\n你在{Game1.player.Name}开的农夫小超市里，刚付了 {total}g，买了{line.Item.DisplayName}等东西（原价大约 {BasePrice(line.Item)}g 一个）。\n\n# 输出\n顺口说一句对这家店或这样东西的看法，20 个字以内，不加引号。";
		Task.Run(async () =>
		{
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("store_comment", system, "说一句。", 200, 0.9)).ConfigureAwait(false);
				string? said = r.Ok ? ChildPrompts.Clean(r.Text) : null;
				if (said != null)
				{
					MainThread.Run(() => NpcSpeech.Say(speaker, said));
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

	/// <summary>COS 那边的成交：认赃物只能事后算后果（它那边的货已经卖出去了）。</summary>
	public void OnSale(StoreSale sale)
	{
		_ledger.OnStorePurchase(sale);
	}
}

/// <summary>顾客想买的一样（还在货架上）。</summary>
public sealed class Pick
{
	public Furniture Shelf = null!;

	public int Slot;

	public string ItemId = "";

	public int Qty;
}

/// <summary>篮子里的一样（已经从货架上拿下来）。</summary>
public sealed class Line
{
	public Furniture Shelf = null!;

	public SObject Item = null!;

	public int Price;

	public string Stage = Freshness.Normal;

	public bool WrongTemp;

	public int Total => Price * Item.Stack;
}

/// <summary>给收银小游戏的：谁、买了什么、总价、递过来多少钱；Done(找了多少, 用了几秒)，Cancel = 先不收。</summary>
public sealed class CheckoutRequest
{
	public NPC Customer = null!;

	public string Name = "";

	public List<Line> Lines = new List<Line>();

	public int Total;

	public int Paid;

	public Action<int, double> Done = (_, _) => { };

	public Action Cancel = () => { };
}
