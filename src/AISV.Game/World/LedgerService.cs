using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Core.World.Errands;
using AISV.Core.World.Ledger;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Menus;

namespace AISV.Game.World;

/// <summary>一家店：店主、商店数据 ID、营业时间。</summary>
public sealed record ShopPlace(string Owner, string ShopId, int Open, int Close);

/// <summary>
/// 村民账本：每位村民的钱、随身物品、家当和账目。钱随着工资、逛店、酒吧、生活费、被偷、帮农夫办事流动；
/// 农夫可以向村民借钱，到期不还时按各自的性子催。只在房主（或单人）上算。
/// </summary>
public sealed partial class LedgerService
{
	public const string LoanRequest = "loan_reply";

	public static readonly IReadOnlyDictionary<string, ShopPlace> Places = new Dictionary<string, ShopPlace>(StringComparer.OrdinalIgnoreCase)
	{
		["SeedShop"] = new ShopPlace("Pierre", "SeedShop", 900, 1700),
		["Saloon"] = new ShopPlace("Gus", "Saloon", 1200, 2400),
		["Blacksmith"] = new ShopPlace("Clint", "Blacksmith", 900, 1600),
		["FishShop"] = new ShopPlace("Willy", "FishShop", 900, 1700),
		["AnimalShop"] = new ShopPlace("Marnie", "AnimalShop", 900, 1600),
		["ScienceHouse"] = new ShopPlace("Robin", "Carpenter", 900, 1700),
		["Hospital"] = new ShopPlace("Harvey", "Hospital", 900, 1500),
		["JojaMart"] = new ShopPlace("Morris", "Joja", 900, 2300),
		["SandyHouse"] = new ShopPlace("Sandy", "Sandy", 900, 2350)
	};

	/// <summary>在店里上班的人（不算逛店）。</summary>
	private static readonly IReadOnlyDictionary<string, string[]> Staff = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Saloon"] = new[] { "Emily" },
		["Hospital"] = new[] { "Maru" },
		["AnimalShop"] = new[] { "Shane" },
		["JojaMart"] = new[] { "Shane", "Sam" }
	};

	/// <summary>游戏商店菜单的 ID 对应的店主。</summary>
	private static readonly IReadOnlyDictionary<string, string> MenuOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["SeedShop"] = "Pierre",
		["Saloon"] = "Gus",
		["Blacksmith"] = "Clint",
		["FishShop"] = "Willy",
		["AnimalShop"] = "Marnie",
		["Carpenter"] = "Robin",
		["Hospital"] = "Harvey",
		["Joja"] = "Morris",
		["Sandy"] = "Sandy",
		["ShadowShop"] = "Krobus",
		["Dwarf"] = "Dwarf"
	};

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly Ecosystem _eco;

	private readonly NpcDirectory _npcs;

	private readonly Relationships _relationships;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private (string Owner, int Money)? _shopOpen;

	private bool _asking;

	private int _session;

	public LedgerService(Func<ModConfig> config, Func<DocumentStore?> store, Ecosystem eco, NpcDirectory npcs, Relationships relationships, ILog log)
	{
		_config = config;
		_store = store;
		_eco = eco;
		_npcs = npcs;
		_relationships = relationships;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string, string, string, string?, int, string?>? PostLetter { get; set; }

	public Func<string, string, bool>? Text { get; set; }

	/// <summary>镇上传一条（文字、知道的人）。</summary>
	public Action<string, IReadOnlyList<string>>? Gossip { get; set; }

	public int Version { get; private set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.WorldModuleOn && c.PursesOn;
		}
	}

	private bool Host => Enabled && Context.IsWorldReady && Context.IsMainPlayer;

	private LedgerBook? Book => _store()?.Get(LedgerBook.Path, LedgerBook.Doc);

	private static int Today => (Game1.Date?.TotalDays ?? 0) + 1;

	public string Display(string npc) => _npcs.Names.Display(npc);

	public static string ItemName(string id) => TextTools.CleanForGame(ItemRegistry.GetData(id)?.DisplayName ?? id);

	// ---------------- 钱包 ----------------

	public IReadOnlyList<NpcWallet> Wallets() => Book?.Wallets.Values.ToList() ?? new List<NpcWallet>();

	public IReadOnlyList<Debt> Debts() => Book?.Debts.ToList() ?? new List<Debt>();

	public IReadOnlyList<TownEvent> Events() => Book?.Events.ToList() ?? new List<TownEvent>();

	public Debt? OpenDebt(string npc) => Book?.Debts.FirstOrDefault(d => d.State == DebtStates.Open && string.Equals(d.Npc, npc, StringComparison.OrdinalIgnoreCase));

	/// <summary>这位村民的钱包；还没有就按职业生成（不是村民时为 null）。</summary>
	public NpcWallet? Wallet(string npc)
	{
		if (Book is not LedgerBook book || string.IsNullOrWhiteSpace(npc))
		{
			return null;
		}
		if (book.Wallets.TryGetValue(npc, out NpcWallet? w))
		{
			return w;
		}
		NPC? n = Game1.getCharacterFromName(npc);
		if (n == null || !n.IsVillager || !Context.IsMainPlayer)
		{
			return null;
		}
		w = Create(n);
		book.Wallets[npc] = w;
		Changed();
		return w;
	}

	public Occupation Job(string npc)
	{
		NPC? n = Game1.getCharacterFromName(npc);
		Occupation job = LedgerRules.JobOf(npc, ChildSafety.IsChild(npc, n?.Age));
		return LedgerRules.Override(job, Field(npc, "AIStardewValley/Wallet"));
	}

	private static string? Field(string npc, string key) =>
		Game1.characterData != null && Game1.characterData.TryGetValue(npc, out var data) && data?.CustomFields != null && data.CustomFields.TryGetValue(key, out string? v) ? v : null;

	private NpcWallet Create(NPC n)
	{
		Occupation job = Job(n.Name);
		Random rng = LedgerRules.Seeded(Game1.uniqueIDForThisGame, n.Name);
		NpcWallet w = new NpcWallet
		{
			Npc = n.Name,
			Gold = job.Child ? rng.Next(20, 101) : LedgerRules.Vary(job.Gold, rng),
			Savings = LedgerRules.Vary(job.Savings, rng),
			CreatedDay = Today
		};
		List<ItemTemplate> loved = Safe(() => _eco.PersonalTastes(n.Name, "loved"));
		List<ItemTemplate> liked = Safe(() => _eco.PersonalTastes(n.Name, "liked"));
		List<string> cheap = loved.Concat(liked).Where(t => t.Price > 0 && t.Price <= 300).Select(t => t.QualifiedId).Distinct().OrderBy(_ => rng.Next()).ToList();
		if (job.Child)
		{
			foreach (string id in cheap.Take(5))
			{
				LedgerRules.Put(w.Home, LedgerRules.MaxHome, id, 1, 0);
			}
			foreach (string id in cheap.Skip(5).Take(2))
			{
				LedgerRules.Put(w.Carried, LedgerRules.MaxCarried, id, 1, 0);
			}
			return w;
		}
		List<string> work = job.Items.Where(id => ItemRegistry.GetData(id) != null).ToList();
		foreach (string id in work.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).Concat(cheap.Take(rng.Next(1, 3))))
		{
			LedgerRules.Put(w.Carried, LedgerRules.MaxCarried, id, rng.Next(1, 4), 0);
		}
		foreach (string id in work)
		{
			LedgerRules.Put(w.Home, LedgerRules.MaxHome, id, rng.Next(2, 10), 0);
		}
		foreach (string id in cheap.Skip(2).Take(rng.Next(2, 5)))
		{
			LedgerRules.Put(w.Home, LedgerRules.MaxHome, id, rng.Next(1, 4), 0);
		}
		foreach (ItemTemplate t in loved.Where(t => t.Price > 300).OrderByDescending(t => t.Price).Take(rng.Next(1, 3)))
		{
			LedgerRules.Put(w.Home, LedgerRules.MaxHome, t.QualifiedId, 1, rng.Next(0, 3) == 0 ? 2 : 0);
		}
		return w;
	}

	private List<ItemTemplate> Safe(Func<IReadOnlyList<ItemTemplate>> get)
	{
		try
		{
			return get().ToList();
		}
		catch (Exception ex)
		{
			_log.Debug("账本：读喜好出错：" + ex.Message);
			return new List<ItemTemplate>();
		}
	}

	private static IEnumerable<NPC> Villagers() => Utility.getAllVillagers().Where(n => n != null && n.IsVillager && n.CanSocialize);

	private void Log(NpcWallet w, string kind, int amount, string note)
	{
		LedgerRules.AddLog(w, new LedgerEntry { Day = Today, Time = Game1.timeOfDay, Kind = kind, Amount = amount, Note = note });
		if (amount <= -LedgerRules.BigSpend && kind is EntryKinds.Shopping or EntryKinds.Festival or EntryKinds.Saloon)
		{
			string text = Tr.Get("ledger.event.spend", "{{name}}一下子花了 {{gold}}g：{{note}}。", ("name", Display(w.Npc)), ("gold", -amount), ("note", note));
			Event(w.Npc, kind, -amount, text);
			Gossip?.Invoke(text, new[] { w.Npc });
		}
	}

	private void Event(string npc, string kind, int amount, string text)
	{
		if (Book is not LedgerBook book)
		{
			return;
		}
		book.Events.Insert(0, new TownEvent { Day = Today, Npc = npc, Kind = kind, Amount = amount, Text = text });
		if (book.Events.Count > LedgerRules.MaxEvents)
		{
			book.Events.RemoveRange(LedgerRules.MaxEvents, book.Events.Count - LedgerRules.MaxEvents);
		}
	}

	private void Earn(string npc, int amount, string kind, string note)
	{
		if (amount <= 0 || Wallet(npc) is not NpcWallet w)
		{
			return;
		}
		w.Gold = Math.Min(LedgerRules.MaxGold, w.Gold + amount);
		Log(w, kind, amount, note);
		Changed();
	}

	private int Spend(NpcWallet w, int amount, string kind, string note)
	{
		int paid = LedgerRules.Pay(w, amount);
		if (paid > 0)
		{
			Log(w, kind, -paid, note);
			Changed();
		}
		return paid;
	}

	// ---------------- 每天 ----------------

	public void OnSaveLoaded()
	{
		_session++;
		_shopOpen = null;
		_asking = false;
		Version++;
	}

	public void OnReturnedToTitle()
	{
		_session++;
		_asking = false;
	}

	public void OnDayStarted()
	{
		if (!Host || Book is not LedgerBook book || book.LastDailyDay == Today)
		{
			return;
		}
		try
		{
			book.LastDailyDay = Today;
			List<NPC> villagers = Villagers().ToList();
			foreach (NPC n in villagers)
			{
				Wallet(n.Name);
			}
			DayOfWeek day = Game1.Date.DayOfWeek;
			if (day == DayOfWeek.Monday)
			{
				PayWages(villagers);
			}
			foreach ((string owner, (int min, int max)) in LedgerRules.ShopTraffic)
			{
				Earn(owner, _random.Next(min, max + 1), EntryKinds.Sales, Tr.Get("ledger.note.traffic", "店里的客人"));
			}
			if (Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
			{
				FestivalSpending(villagers);
			}
			if (day == DayOfWeek.Sunday)
			{
				LivingCosts(villagers);
			}
			foreach (Debt d in book.Debts.Where(d => d.State == DebtStates.Open).ToList())
			{
				Overdue(d);
			}
			book.Debts.RemoveAll(d => d.State != DebtStates.Open && Today - d.ClosedDay > 56);
			MorningExtras(book, villagers);
			foreach (NpcWallet w in book.Wallets.Values)
			{
				LedgerRules.Clamp(w);
			}
			Changed();
		}
		catch (Exception ex)
		{
			_log.Warn("账本：早上结算出错：" + ex);
		}
	}

	private void PayWages(List<NPC> villagers)
	{
		foreach (NPC n in villagers)
		{
			Occupation job = Job(n.Name);
			if (job.Wage > 0)
			{
				Earn(n.Name, job.Wage, EntryKinds.Wage, Tr.Get("ledger.note.wage", "{{job}}的工钱", ("job", job.Job)));
			}
		}
		foreach ((string kid, string parent) in LedgerRules.Parents)
		{
			if (Wallet(kid) is not NpcWallet k || Wallet(parent) is not NpcWallet p || p.Gold < LedgerRules.Allowance * 5)
			{
				continue;
			}
			p.Gold -= LedgerRules.Allowance;
			Log(p, EntryKinds.Allowance, -LedgerRules.Allowance, Tr.Get("ledger.note.allowanceGive", "给{{kid}}的零花钱", ("kid", Display(kid))));
			Earn(kid, LedgerRules.Allowance, EntryKinds.Allowance, Tr.Get("ledger.note.allowance", "{{parent}}给的零花钱", ("parent", Display(parent))));
		}
	}

	private void FestivalSpending(List<NPC> villagers)
	{
		foreach (NPC n in villagers.Where(n => !Job(n.Name).Child && n.Name != "Pierre" && _random.NextDouble() < 0.5))
		{
			if (Wallet(n.Name) is NpcWallet w)
			{
				int paid = Spend(w, Math.Min(w.Gold, LedgerRules.FestivalSpend(_random)), EntryKinds.Festival, Tr.Get("ledger.note.festival", "节日上买东西"));
				Earn("Pierre", paid, EntryKinds.Sales, Tr.Get("ledger.note.festivalSales", "节日摊位"));
			}
		}
	}

	private void LivingCosts(List<NPC> villagers)
	{
		foreach (NPC n in villagers)
		{
			Occupation job = Job(n.Name);
			int cost = LedgerRules.LivingCost(job);
			if (cost <= 0 || Wallet(n.Name) is not NpcWallet w)
			{
				continue;
			}
			if (HustleRules.Tight(w, Today))
			{
				cost /= 2;
			}
			Spend(w, cost, EntryKinds.Living, Tr.Get("ledger.note.living", "这一周的吃穿用度"));
			if (job.Shop != null && LedgerRules.Restock(w.Log.Where(e => e.Kind == EntryKinds.Sales && Today - e.Day < 7).Sum(e => e.Amount)) is int restock and > 0)
			{
				Spend(w, restock, EntryKinds.Living, Tr.Get("ledger.note.restock", "这一周进货"));
			}
			int moved = LedgerRules.Rebalance(w, cost);
			if (moved != 0)
			{
				LedgerRules.AddLog(w, new LedgerEntry { Day = Today, Time = Game1.timeOfDay, Kind = EntryKinds.Savings, Amount = -moved, Note = moved > 0 ? Tr.Get("ledger.note.save", "存了 {{n}}g", ("n", moved)) : Tr.Get("ledger.note.withdraw", "从存款取了 {{n}}g", ("n", -moved)) });
			}
		}
	}

	/// <summary>每 10 分钟：在别人店里的人有机会买一样东西；晚上 10 点还在酒吧的人花一笔；孩子偶尔买零嘴。</summary>
	public void OnTimeChanged()
	{
		if (!Host || Book == null)
		{
			return;
		}
		try
		{
			int time = Game1.timeOfDay;
			AdsTick();
			foreach (NPC n in Villagers().ToList())
			{
				if (n.currentLocation?.Name is not string here || Wallet(n.Name) is not NpcWallet w)
				{
					continue;
				}
				HustleTick(n, w, time);
				bool child = Job(n.Name).Child;
				if (HustleRules.Tight(w, Today))
				{
					continue;
				}
				if (time == LedgerRules.SaloonTime && here == "Saloon" && n.Name != "Gus" && !child)
				{
					int paid = Spend(w, Math.Min(w.Gold, LedgerRules.SaloonSpend(_random, Job(n.Name).Style)), EntryKinds.Saloon, Tr.Get("ledger.note.saloon", "在酒吧吃喝"));
					Earn("Gus", paid, EntryKinds.Sales, Tr.Get("ledger.note.saloonSales", "{{name}}在酒吧的花销", ("name", Display(n.Name))));
					continue;
				}
				if (child)
				{
					if (w.ShoppedDay != Today && (here == "Town" || here == "SeedShop") && w.Gold >= 15 && _random.NextDouble() < 0.02)
					{
						w.ShoppedDay = Today;
						Spend(w, _random.Next(5, 16), EntryKinds.Snack, Tr.Get("ledger.note.snack", "买了点零嘴"));
					}
					continue;
				}
				bool insider = string.Equals(n.DefaultMap, here, StringComparison.OrdinalIgnoreCase) || (Staff.TryGetValue(here, out string[]? staff) && staff.Contains(n.Name));
				if (!insider && Places.TryGetValue(here, out ShopPlace? shop) && LedgerRules.WillShop(_random.NextDouble(), w.ShoppedDay == Today, shop.Owner == n.Name, time >= shop.Open && time < shop.Close))
				{
					Shop(n, w, shop);
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("账本：逛店出错：" + ex.Message);
		}
	}

	/// <summary>在一家店里买一样买得起的东西；你在旁边时他头顶说一句。返回说明（没买成时为 null）。</summary>
	private string? Shop(NPC n, NpcWallet w, ShopPlace shop)
	{
		w.ShoppedDay = Today;
		int budget = LedgerRules.ShopBudget(w.Gold);
		List<(string Id, int Price)> stock = Stock(shop.ShopId).Where(s => s.Price > 0 && s.Price <= budget).ToList();
		if (stock.Count == 0)
		{
			return null;
		}
		(string id, int price) = stock[_random.Next(stock.Count)];
		int count = Math.Clamp(budget / Math.Max(1, price) / 3, 1, 5);
		int cost = price * count;
		string name = ItemName(id);
		int paid = Spend(w, cost, EntryKinds.Shopping, Tr.Get("ledger.note.bought", "在{{shop}}买了{{item}} ×{{count}}", ("shop", Display(shop.Owner)), ("item", name), ("count", count)));
		if (!LedgerRules.Put(w.Carried, LedgerRules.MaxCarried, id, count, 0))
		{
			LedgerRules.Put(w.Home, LedgerRules.MaxHome, id, count, 0);
		}
		Earn(shop.Owner, paid, EntryKinds.Sales, Tr.Get("ledger.note.sold", "{{name}}来买{{item}}", ("name", Display(n.Name)), ("item", name)));
		if (_config().ShoppingRemarks && Game1.player.currentLocation == n.currentLocation)
		{
			NpcSpeech.Say(n, Tr.Get("ledger.bubble", "买了{{item}}！", ("item", name)));
		}
		return $"{n.Name} 在 {shop.Owner} 的店里花 {paid}g 买了 {name}×{count}";
	}

	private static IEnumerable<(string Id, int Price)> Stock(string shopId)
	{
		if (!DataLoader.Shops(Game1.content).TryGetValue(shopId, out ShopData? shop) || shop?.Items == null)
		{
			yield break;
		}
		foreach (ShopItemData entry in shop.Items)
		{
			string? id = entry.ItemId;
			if (string.IsNullOrWhiteSpace(id) || id.Contains(' ') || !id.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) || !GameStateQuery.CheckConditions(entry.Condition))
			{
				continue;
			}
			Item? item = ItemRegistry.Create(id, 1, 0, allowNull: true);
			if (item != null)
			{
				yield return (item.QualifiedItemId, entry.Price > 0 ? entry.Price : item.salePrice());
			}
		}
	}

	// ---------------- 农夫的钱流进流出 ----------------

	/// <summary>打开商店时记下钱；关掉时差多少就是店主进了多少（卖东西给店里时店主付钱）。</summary>
	public void OnMenuChanged(IClickableMenu? oldMenu, IClickableMenu? newMenu)
	{
		if (!Host)
		{
			return;
		}
		if (newMenu is ShopMenu shop && shop.ShopId is string id && MenuOwners.TryGetValue(id, out string? owner))
		{
			_shopOpen = (owner, Game1.player.Money);
			return;
		}
		if (oldMenu is ShopMenu && _shopOpen is var (who, before))
		{
			_shopOpen = null;
			int diff = before - Game1.player.Money;
			if (diff > 0)
			{
				Earn(who, diff, EntryKinds.Sales, Tr.Get("ledger.note.playerBought", "农夫来买东西"));
			}
			else if (diff < 0 && Wallet(who) is NpcWallet w)
			{
				Spend(w, -diff, EntryKinds.Shopping, Tr.Get("ledger.note.playerSold", "收了农夫卖的东西"));
			}
		}
	}

	public void OnErrandDone(Errand e)
	{
		if (!Host)
		{
			return;
		}
		Earn(e.Npc, e.Reward, EntryKinds.Errand, Tr.Get("ledger.note.errand", "替农夫{{what}}", ("what", ErrandRules.What(e, ItemName(e.ItemId), e.Recipient.Length > 0 ? Display(e.Recipient) : ""))));
		if (e.Goods > 0 && Places.Values.FirstOrDefault(p => string.Equals(p.ShopId, e.Shop, StringComparison.OrdinalIgnoreCase)) is ShopPlace shop)
		{
			Earn(shop.Owner, e.Goods, EntryKinds.Sales, Tr.Get("ledger.note.errandGoods", "{{name}}替农夫来买东西", ("name", Display(e.Npc))));
		}
	}

	public void OnErrandKept(Errand e, int gold)
	{
		if (Host)
		{
			Earn(e.Npc, gold, EntryKinds.Errand, Tr.Get("ledger.note.errandKept", "农夫中途撤回委托，留下的辛苦费"));
		}
	}

	public void OnCommissionPaid(Commission c)
	{
		if (!Host || Wallet(c.Poster) is not NpcWallet w)
		{
			return;
		}
		Spend(w, c.RewardGold, EntryKinds.Commission, Tr.Get("ledger.note.commission", "付给农夫的委托酬劳"));
	}

	public void OnGift(NPC npc, StardewValley.Object item)
	{
		if (!Host || item.Edibility > -300 || Wallet(npc.Name) is not NpcWallet w)
		{
			return;
		}
		if (LedgerRules.Put(w.Home, LedgerRules.MaxHome, item.QualifiedItemId, 1, item.Quality))
		{
			Changed();
		}
	}

	/// <summary>偷窃：被偷的钱从身上扣；东西随身或家里有就拿掉，没有就按价值从身上的钱里扣。</summary>
	public void OnTheft(TheftRecord r)
	{
		if (!Host || Book is not LedgerBook book || book.TheftsApplied.Contains(r.Id) || r.Victims.Count == 0 || Wallet(r.Victims[0]) is not NpcWallet w)
		{
			return;
		}
		book.TheftsApplied.Add(r.Id);
		if (book.TheftsApplied.Count > 300)
		{
			book.TheftsApplied.RemoveRange(0, book.TheftsApplied.Count - 300);
		}
		int lost = Math.Min(w.Gold, Math.Max(0, r.Coins));
		w.Gold -= lost;
		foreach (StolenItem s in r.Items)
		{
			int need = Math.Max(1, s.Stack);
			int taken = r.Kind == TheftRecord.Burglary ? LedgerRules.Take(w.Home, s.Id, need) : 0;
			taken += LedgerRules.Take(w.Carried, s.Id, need - taken);
			taken += r.Kind == TheftRecord.Pickpocket ? LedgerRules.Take(w.Home, s.Id, need - taken) : 0;
			int value = Math.Min(w.Gold, Math.Max(0, s.Price) * (need - taken));
			w.Gold -= value;
			lost += value;
		}
		if (r.Kind == TheftRecord.Pickpocket && r.Items.Count > 0)
		{
			LostAd(w.Npc, ItemName(r.Items[0].Id), r.Id);
		}
		if (lost > 0 || r.Items.Count > 0)
		{
			Log(w, EntryKinds.Theft, -lost, TheftText.Loot(r.Items, r.Coins) is { Length: > 0 } loot ? Tr.Get("ledger.note.theft", "被偷了{{loot}}", ("loot", loot)) : Tr.Get("ledger.note.theftPlain", "被偷了"));
			if (r.Value >= LedgerRules.BigSpend)
			{
				Event(w.Npc, EntryKinds.Theft, r.Value, Tr.Get("ledger.event.theft", "{{name}}被偷了价值 {{gold}}g 的东西。", ("name", Display(w.Npc)), ("gold", r.Value)));
			}
		}
		Changed();
	}

	// ---------------- 酒账 ----------------

	/// <summary>在这家店还能赊多少（只有格斯的店能赊）。</summary>
	public int TabRoom(string shop)
	{
		if (!Host || shop != LedgerRules.TabShop || ChildSafety.IsChild(LedgerRules.TabOwner, null) || Wallet(LedgerRules.TabOwner) == null)
		{
			return 0;
		}
		Debt? open = OpenDebt(LedgerRules.TabOwner);
		if (open != null && (!open.Tab || Today > open.DueDay))
		{
			return 0;
		}
		return LedgerRules.TabRoom(_relationships.EffectiveHearts(LedgerRules.TabOwner), open?.Owed ?? 0);
	}

	/// <summary>记到格斯的账上：没有账就开一笔，七天后到期；已经有账就加上去。</summary>
	public void ChargeTab(string shop, int amount)
	{
		if (!Host || amount <= 0 || Book is not LedgerBook book || Wallet(LedgerRules.TabOwner) is not NpcWallet w)
		{
			return;
		}
		Debt? d = OpenDebt(LedgerRules.TabOwner);
		if (d == null)
		{
			d = new Debt { Id = "d" + book.NextDebt++, Npc = LedgerRules.TabOwner, Day = Today, DueDay = Today + LedgerRules.TabDays, Tab = true, Style = LedgerRules.StyleOf(LedgerRules.TabOwner, Field(LedgerRules.TabOwner, "AIStardewValley/DebtStyle")) };
			book.Debts.Add(d);
			Event(LedgerRules.TabOwner, EntryKinds.Loan, amount, Tr.Get("ledger.event.tab", "农夫在{{name}}那里挂了账。", ("name", Display(LedgerRules.TabOwner))));
		}
		d.Amount += amount;
		Log(w, EntryKinds.Loan, 0, Tr.Get("ledger.note.tab", "农夫赊了 {{n}}g 的账", ("n", amount)));
		Changed();
		Game1.addHUDMessage(new HUDMessage(Tr.Get("ledger.tab.hud", "记在{{name}}的账上：{{n}}g（一共欠 {{owed}}g，{{days}} 天内还）", ("name", Display(LedgerRules.TabOwner)), ("n", amount), ("owed", d.Owed), ("days", Math.Max(0, d.DueDay - Today))), 2));
		_log.Info($"酒账：农夫在 {LedgerRules.TabOwner} 那里赊了 {amount}g，一共欠 {d.Owed}g。");
	}

	// ---------------- 借钱 ----------------

	public int LoanLimit(string npc) => Wallet(npc) is NpcWallet w ? LedgerRules.LoanLimit(_relationships.EffectiveHearts(npc), w.Gold) : 0;

	/// <summary>借不了的原因；能借时为 null。</summary>
	public string? LoanProblem(string npc, int amount)
	{
		if (!Host)
		{
			return Context.IsMainPlayer ? Tr.Get("ledger.loan.off", "村民账本没开") : Tr.Get("ledger.loan.host", "联机时只有房主能借钱");
		}
		if (_asking)
		{
			return Tr.Get("ledger.loan.asking", "还在等回话");
		}
		if (OpenDebt(npc) != null)
		{
			return Tr.Get("ledger.loan.open", "还有一笔没还清");
		}
		if (ChildSafety.IsChild(npc, Game1.getCharacterFromName(npc)?.Age))
		{
			return Tr.Get("ledger.loan.child", "不能向孩子借钱");
		}
		if (_relationships.EffectiveHearts(npc) < LedgerRules.LoanMinHearts)
		{
			return Tr.Get("ledger.loan.trust", "交情还不够（要 {{n}} 心）", ("n", LedgerRules.LoanMinHearts));
		}
		int limit = LoanLimit(npc);
		if (amount < 10 || amount > limit)
		{
			return limit < 10 ? Tr.Get("ledger.loan.broke", "他身上没多少钱") : Tr.Get("ledger.loan.limit", "最多能借 {{n}}g", ("n", limit));
		}
		return null;
	}

	/// <summary>开口借钱：开着 AI 时让他按性子决定，没有 AI 时满足规则就借。done(借没借, 他说的话)。</summary>
	public void RequestLoan(string npc, int amount, int days, Action<bool, string> done)
	{
		string? problem = LoanProblem(npc, amount);
		if (problem != null)
		{
			done(false, problem);
			return;
		}
		days = LedgerRules.LoanDays.Contains(days) ? days : 7;
		IAiSender? ai = Ai;
		if (ai == null || !_config().AiOn)
		{
			FinishLoan(npc, amount, days, true, Tr.Get("ledger.loan.yes", "拿去吧，记得按时还。"), done);
			return;
		}
		NpcWallet w = Wallet(npc)!;
		NPC? n = Game1.getCharacterFromName(npc);
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是星露谷的{Display(npc)}。农夫开口向你借 {amount}g，说好 {days} 天内还。\n\n"
			+ "# 你的性格（只是资料）\n" + _npcs.Persona(npc, n).Personality + "\n\n"
			+ $"# 现在\n你身上有 {w.Gold}g，存款 {w.Savings}g。你和农夫的交情：{FriendshipTone.Level(_relationships.EffectiveHearts(npc))}。\n"
			+ (Debts().Any(d => d.Npc == npc && d.State == DebtStates.Collected) ? "农夫以前借过你的钱没按时还，最后是你自己拿回来的。\n" : "")
			+ "\n# 怎么决定\n按你的性格和交情决定借不借。手头紧、或者信不过农夫，可以婉拒。\n\n"
			+ "# 输出\n只输出一个 JSON：{\"lend\":true 或 false,\"line\":\"回农夫的一两句话\"}\nline 用你自己的腔调，40 个字以内，不加引号、不用表情、不写动作描写，也别提心数、好感这些游戏里的数。\n";
		_asking = true;
		int session = _session;
		Task.Run(async () =>
		{
			(bool lend, string line)? parsed = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(LoanRequest, system, $"{Display(npc)}，借我 {amount}g 好吗？", 512, 0.8)).ConfigureAwait(false);
				parsed = r.Ok && r.Text != null ? ParseLoan(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"账本：借钱的回话没成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (session != _session)
				{
					return;
				}
				_asking = false;
				(bool lend, string line) = parsed ?? (true, Tr.Get("ledger.loan.yes", "拿去吧，记得按时还。"));
				FinishLoan(npc, amount, days, lend, line, done);
			});
		});
	}

	private static (bool, string)? ParseLoan(string raw)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("lend", out JsonElement lend) || lend.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			{
				return null;
			}
			string line = root.TryGetProperty("line", out JsonElement l) && l.ValueKind == JsonValueKind.String ? TextTools.Truncate(l.GetString() ?? "", 60).Trim() : "";
			return line.Length == 0 ? null : (lend.GetBoolean(), line);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private void FinishLoan(string npc, int amount, int days, bool lend, string line, Action<bool, string> done)
	{
		if (lend && LoanProblem(npc, amount) == null && Wallet(npc) is NpcWallet w && Book is LedgerBook book)
		{
			w.Gold -= amount;
			Game1.player.Money += amount;
			Debt d = new Debt { Id = "d" + book.NextDebt++, Npc = npc, Amount = amount, Day = Today, DueDay = Today + days, Style = LedgerRules.StyleOf(npc, Field(npc, "AIStardewValley/DebtStyle")) };
			book.Debts.Add(d);
			Log(w, EntryKinds.Loan, -amount, Tr.Get("ledger.note.loan", "借给农夫，{{days}} 天内还", ("days", days)));
			Event(npc, EntryKinds.Loan, amount, Tr.Get("ledger.event.loan", "农夫向{{name}}借了 {{gold}}g。", ("name", Display(npc)), ("gold", amount)));
			Changed();
			_log.Info($"账本：{npc} 借给农夫 {amount}g，{days} 天内还（{d.Style}）。");
			done(true, line);
			return;
		}
		done(false, line);
	}

	/// <summary>还钱（可以只还一部分）。</summary>
	public string? Repay(string npc, int amount)
	{
		if (OpenDebt(npc) is not Debt d || Wallet(npc) is not NpcWallet w)
		{
			return Tr.Get("ledger.repay.none", "没有要还的");
		}
		amount = Math.Min(amount, d.Owed);
		if (amount <= 0 || Game1.player.Money < amount)
		{
			return Tr.Get("ledger.repay.money", "钱不够");
		}
		Game1.player.Money -= amount;
		w.Gold += amount;
		d.Repaid += amount;
		Log(w, EntryKinds.Repay, amount, Tr.Get("ledger.note.repay", "农夫还了 {{n}}g", ("n", amount)));
		if (d.Owed == 0)
		{
			d.State = DebtStates.Paid;
			d.ClosedDay = Today;
			Text?.Invoke(npc, Tr.Get("ledger.text.paid", "钱收到了，谢谢你说话算话。"));
		}
		Changed();
		return null;
	}

	private void Overdue(Debt d)
	{
		DebtAction action = LedgerRules.Overdue(d, Today);
		if (action != DebtAction.None)
		{
			_log.Info($"账本：农夫欠 {d.Npc} 的 {d.Owed}g 过期了（{d.Style}）：{action}。");
		}
		NPC? npc = Game1.getCharacterFromName(d.Npc);
		switch (action)
		{
			case DebtAction.Warn:
				d.WarnedDay = Today;
				Notify(d, Tr.Get("ledger.debt.warnSubject", "借的钱"), d.Tab
					? Tr.Get("ledger.debt.tabWarn", "在我这儿赊的 {{gold}}g 到期了。三天之内还不上，我就自己去拿。", ("gold", d.Owed))
					: Tr.Get("ledger.debt.warn", "借你的 {{gold}}g 已经到期了。三天之内还不上，我就自己去拿。", ("gold", d.Owed)),
					$"已经到期，你这次是最后通牒：三天之内还不上，你就自己上门去拿（先拿钱，不够拿他背包里的东西抵）。");
				break;
			case DebtAction.Collect:
				Collect(d);
				break;
			case DebtAction.Remind:
				int penalty = LedgerRules.RemindPenalty(d);
				d.Reminders++;
				d.LastReminderDay = Today;
				Notify(d, Tr.Get("ledger.debt.remindSubject", "那笔钱"), d.Tab
					? Tr.Get("ledger.debt.tabRemind", "在我这儿赊的 {{gold}}g 到期了，方便的时候来结一下。", ("gold", d.Owed))
					: d.Reminders == 1
					? Tr.Get("ledger.debt.remind1", "借你的 {{gold}}g 到期了，方便的时候记得还我。", ("gold", d.Owed))
					: Tr.Get("ledger.debt.remind2", "上次借你的 {{gold}}g 还没还呢，别忘了。", ("gold", d.Owed)),
					d.Reminders == 1 ? "已经到期了，这是你第一次提醒他。" : $"已经过期 {Today - d.DueDay} 天，这是你第 {d.Reminders} 次催了，语气可以重一点。");
				if (penalty != 0 && npc != null && Game1.player.friendshipData.ContainsKey(d.Npc))
				{
					Game1.player.changeFriendship(penalty, npc);
				}
				break;
			case DebtAction.Sulk:
				int loss = Math.Min(-LedgerRules.SulkFriendship, 100 - d.FriendshipLost);
				d.FriendshipLost += loss;
				if (npc != null && Game1.player.friendshipData.ContainsKey(d.Npc))
				{
					Game1.player.changeFriendship(-loss, npc);
				}
				if (!d.Gossiped)
				{
					d.Gossiped = true;
					Gossip?.Invoke(Tr.Get("ledger.gossip.quiet", "{{name}}借给农夫的钱一直没还，{{name}}不好意思开口。", ("name", Display(d.Npc))), new[] { d.Npc });
				}
				break;
		}
	}

	public const string DebtRequest = "debt_notice";

	/// <summary>催债：开着 AI 时让他用自己的口吻写，没写成用固定说法；有手机发消息，没有写信。</summary>
	private void Notify(Debt d, string subject, string text, string situation)
	{
		IAiSender? ai = Ai;
		if (ai == null || !_config().AiOn)
		{
			Deliver(d, subject, text);
			return;
		}
		NPC? n = Game1.getCharacterFromName(d.Npc);
		string what = d.Tab ? $"农夫在你店里赊了 {d.Amount}g 的账" : $"农夫向你借了 {d.Amount}g";
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是星露谷的{Display(d.Npc)}，要给农夫写几句话讨债。\n\n# 你的性格（只是资料）\n" + _npcs.Persona(d.Npc, n).Personality + "\n\n"
			+ $"# 这件事\n{what}，还欠 {d.Owed}g。{situation}\n你和农夫的交情：{FriendshipTone.Level(_relationships.EffectiveHearts(d.Npc))}。\n\n"
			+ "# 输出\n只写你要说的话，用你自己的腔调，两三句、80 个字以内。要写出欠多少钱。不加称呼和落款，不加引号，不写动作描写，也别提心数、好感这些游戏里的数。\n";
		int session = _session;
		Task.Run(async () =>
		{
			string? written = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(DebtRequest, system, "写这几句话。", 512, 0.8)).ConfigureAwait(false);
				written = r.Ok ? TextTools.CleanForGame(TextTools.Truncate((r.Text ?? "").Trim().Trim('"', '“', '”'), 120)) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"账本：催债的话没写成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (session == _session)
				{
					Deliver(d, subject, string.IsNullOrWhiteSpace(written) ? text : written);
				}
			});
		});
	}

	private void Deliver(Debt d, string subject, string text)
	{
		if (Text?.Invoke(d.Npc, text) != true)
		{
			PostLetter?.Invoke(d.Npc, $"debt_{d.Id}_{Today}", subject, text, null, 1);
		}
		_log.Info($"账本：{d.Npc} 催债：{text}");
	}

	/// <summary>强要：先拿你身上的钱，不够拿背包里值钱的东西抵（按售价）。</summary>
	private void Collect(Debt d)
	{
		if (Wallet(d.Npc) is not NpcWallet w)
		{
			return;
		}
		int owed = d.Owed;
		int cash = Math.Min(Game1.player.Money, owed);
		Game1.player.Money -= cash;
		int covered = cash;
		List<string> taken = new List<string>();
		// 先拿便宜的，免得为一点欠款拿走贵重东西。
		List<int> slots = Enumerable.Range(0, Game1.player.Items.Count)
			.Where(i => Game1.player.Items[i] is StardewValley.Object o && !o.questItem.Value && o.sellToStorePrice() > 0)
			.OrderBy(i => ((StardewValley.Object)Game1.player.Items[i]).sellToStorePrice())
			.ToList();
		foreach (int i in slots)
		{
			if (covered >= owed || Game1.player.Items[i] is not StardewValley.Object o)
			{
				break;
			}
			int unit = o.sellToStorePrice();
			int n = Math.Min(o.Stack, (owed - covered + unit - 1) / unit);
			covered += unit * n;
			taken.Add($"{TextTools.CleanForGame(o.DisplayName)} ×{n}");
			LedgerRules.Put(w.Home, LedgerRules.MaxHome, o.QualifiedItemId, n, o.Quality);
			o.Stack -= n;
			if (o.Stack <= 0)
			{
				Game1.player.Items[i] = null;
			}
		}
		d.WarnedDay = Today;
		if (covered == 0)
		{
			Game1.addHUDMessage(new HUDMessage(Tr.Get("ledger.debt.empty", "{{name}}来收债，你身上什么也没有，他说过几天再来。", ("name", Display(d.Npc))), HUDMessage.error_type));
			_log.Info($"账本：{d.Npc} 来收债，什么也没拿到，过几天再来。");
			return;
		}
		w.Gold += cash;
		d.Repaid = Math.Min(d.Amount, d.Repaid + covered);
		string what = (cash > 0 ? $"{cash}g" : "") + (taken.Count > 0 ? (cash > 0 ? "、" : "") + string.Join("、", taken) : "");
		Game1.addHUDMessage(new HUDMessage(Tr.Get("ledger.debt.collected", "{{name}}拿走了{{what}}抵债。", ("name", Display(d.Npc)), ("what", what)), HUDMessage.error_type));
		Log(w, EntryKinds.Repay, cash, Tr.Get("ledger.note.collect", "自己去农夫那儿拿回欠款"));
		if (d.Owed == 0)
		{
			d.State = DebtStates.Collected;
			d.ClosedDay = Today;
		}
		Event(d.Npc, EntryKinds.Repay, covered, Tr.Get("ledger.event.collect", "{{name}}从农夫那儿拿回了欠款。", ("name", Display(d.Npc))));
		_log.Info($"账本：{d.Npc} 自己收债，抵了 {Math.Min(covered, owed)}g（{what}）。");
	}

	// ---------------- 给 AI 和界面 ----------------

	public string PromptBlock(string npc)
	{
		if (!Enabled || Book is not LedgerBook book || !book.Wallets.TryGetValue(npc, out NpcWallet? w))
		{
			return "";
		}
		IEnumerable<string> carried = w.Carried.Select(i => i.Count > 1 ? $"{ItemName(i.Id)} ×{i.Count}" : ItemName(i.Id));
		string block = LedgerRules.PromptBlock(w, Job(npc), carried, book.Debts.Where(d => string.Equals(d.Npc, npc, StringComparison.OrdinalIgnoreCase)), Today);
		List<string> more = HustleRules.PromptLines(w, book.NpcDebts, npc, Today, Display).ToList();
		more.AddRange(book.Ads.Where(a => string.Equals(a.Npc, npc, StringComparison.OrdinalIgnoreCase) && Today - a.Day <= 7)
			.Select(a => a.SoldDay >= 0 ? $"- 你在分类广告上登的“{ClassifiedRules.Title(a)}”已经卖掉了。" : $"- 你在分类广告上登了“{ClassifiedRules.Title(a)}”。"));
		return more.Count == 0 ? block : block + "\n" + string.Join("\n", more);
	}

	/// <summary>这一周（周一以来）每位村民花出去的钱、每家店进的钱。</summary>
	public (IReadOnlyList<(string Npc, int Spent)> Spenders, IReadOnlyList<(string Owner, int Sales)> Shops) Week()
	{
		int start = Today - ((int)Game1.Date.DayOfWeek + 6) % 7;
		List<(string, int)> spenders = Wallets().Select(w => (w.Npc, -w.Log.Where(e => e.Day >= start && e.Amount < 0 && e.Kind is EntryKinds.Shopping or EntryKinds.Saloon or EntryKinds.Festival or EntryKinds.Snack).Sum(e => e.Amount)))
			.Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
		List<(string, int)> shops = Wallets().Where(w => Places.Values.Any(p => p.Owner == w.Npc)).Select(w => (w.Npc, w.Log.Where(e => e.Day >= start && e.Kind == EntryKinds.Sales).Sum(e => e.Amount)))
			.Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
		return (spenders, shops);
	}

	/// <summary>周报"本周钱事"：没什么可写时为空。</summary>
	public IReadOnlyList<string> PaperLines()
	{
		if (!Enabled || Book == null)
		{
			return Array.Empty<string>();
		}
		var (spenders, shops) = Week();
		List<string> lines = new List<string>();
		if (spenders.Count > 0)
		{
			lines.Add(Tr.Get("ledger.paper.spender", "本周花钱最多的是{{name}}，一共 {{gold}}g。", ("name", Display(spenders[0].Npc)), ("gold", spenders[0].Spent)));
		}
		if (shops.Count > 0)
		{
			lines.Add(Tr.Get("ledger.paper.shop", "{{name}}的店生意最好，进账 {{gold}}g。", ("name", Display(shops[0].Owner)), ("gold", shops[0].Sales)));
		}
		lines.AddRange(Events().Where(e => Today - e.Day <= 7).Take(2).Select(e => e.Text));
		return lines;
	}

	private void Changed()
	{
		Version++;
		_store()?.MarkDirty(LedgerBook.Path);
	}

	// ---------------- 调试 ----------------

	public const string Usage = "用法：aisv_ledger dump | npc <村民> | day [天数] | shop <村民> | lend <村民> <金额> [天数] | repay <村民> <金额> | due <村民> | set <村民> gold|savings <数> | broke <村民> | give <村民> <物品> [数量] [home|carried] | take <村民> <物品> [数量] | log <村民> <金额> <说明> | event <村民> <一句话> | hustle <村民> [办法] [对象] [now] | npcloan <债主> <借的人> <金额> [天数] | ad <村民> sell|show|work|lost <东西> [价钱] [几天后卖掉] [背后的事] | adsell <id> [买家] | ads | reset";

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		string npc = args.Length > 1 ? args[1] : "";
		switch (sub)
		{
			case "dump":
				return write("ledger.json", Book ?? new LedgerBook());
			case "npc" when Wallet(npc) is NpcWallet w:
				return $"{npc}：身上 {w.Gold}g，存款 {w.Savings}g，随身 {string.Join("、", w.Carried.Select(i => ItemName(i.Id) + "×" + i.Count))}；家里 {w.Home.Count} 样；最近：{string.Join("；", w.Log.Take(5).Select(e => $"{e.Amount:+#;-#;0} {e.Note}"))}\n{PromptBlock(npc)}";
			case "day":
			{
				int days = args.Length > 1 && int.TryParse(args[1], out int n) ? Math.Clamp(n, 1, 28) : 1;
				for (int i = 0; i < days; i++)
				{
					if (Book is LedgerBook b)
					{
						b.LastDailyDay = -1;
					}
					OnDayStarted();
				}
				return $"按今天的日子结算了 {days} 次。";
			}
			case "shop" when Wallet(npc) is NpcWallet w && Game1.getCharacterFromName(npc) is NPC n:
			{
				ShopPlace place = n.currentLocation != null && Places.TryGetValue(n.currentLocation.Name, out ShopPlace? p) ? p : Places["SeedShop"];
				return Shop(n, w, place) ?? "没买成（钱不够或店里没东西）。";
			}
			case "lend" when args.Length >= 3 && int.TryParse(args[2], out int amount):
			{
				int days = args.Length > 3 && int.TryParse(args[3], out int d) ? d : 7;
				string result = "等回话……";
				RequestLoan(npc, amount, days, (ok, line) => _log.Info($"账本：{npc} {(ok ? "借了" : "没借")}：{line}"));
				return result;
			}
			case "repay" when args.Length >= 3 && int.TryParse(args[2], out int amount):
				return Repay(npc, amount) ?? "还了。";
			case "due" when OpenDebt(npc) is Debt debt:
				debt.DueDay = Math.Min(debt.DueDay, Today - 1);
				if (debt.Style == DebtStyles.Demand && debt.WarnedDay >= 0)
				{
					Collect(debt);
				}
				else
				{
					debt.LastReminderDay = -1;
					Overdue(debt);
				}
				Changed();
				return $"{debt.Id}：{debt.State}，还欠 {debt.Owed}g，催过 {debt.Reminders} 次。";
			case "set" when args.Length >= 4 && Wallet(npc) is NpcWallet w && int.TryParse(args[3], out int value):
				if (args[2].Equals("savings", StringComparison.OrdinalIgnoreCase))
				{
					w.Savings = Math.Clamp(value, 0, LedgerRules.MaxGold);
				}
				else
				{
					w.Gold = Math.Clamp(value, 0, LedgerRules.MaxGold);
				}
				Changed();
				return $"{npc}：身上 {w.Gold}g，存款 {w.Savings}g。";
			case "broke" when Wallet(npc) is NpcWallet w:
				w.Gold = 0;
				w.Savings = 0;
				w.Hustle = null;
				Changed();
				return $"{npc} 身上和存款都清零了，明早（或 hustle 命令）会想办法。";
			case "give" when args.Length >= 3 && Wallet(npc) is NpcWallet w:
			{
				string id = Ecosystem.Qualify(args[2]) ?? args[2];
				int count = args.Length > 3 && int.TryParse(args[3], out int c) ? Math.Max(1, c) : 1;
				bool home = args.Length <= 4 || !args[4].Equals("carried", StringComparison.OrdinalIgnoreCase);
				bool ok = LedgerRules.Put(home ? w.Home : w.Carried, home ? LedgerRules.MaxHome : LedgerRules.MaxCarried, id, count, 0);
				Changed();
				return ok ? $"{npc} {(home ? "家里" : "身上")}多了 {ItemName(id)}×{count}。" : "放不下了。";
			}
			case "take" when args.Length >= 3 && Wallet(npc) is NpcWallet w:
			{
				string id = Ecosystem.Qualify(args[2]) ?? args[2];
				int count = args.Length > 3 && int.TryParse(args[3], out int c) ? Math.Max(1, c) : 1;
				int taken = LedgerRules.Take(w.Carried, id, count);
				taken += LedgerRules.Take(w.Home, id, count - taken);
				Changed();
				return $"拿掉了 {ItemName(id)}×{taken}。";
			}
			case "log" when args.Length >= 4 && Wallet(npc) is NpcWallet w && int.TryParse(args[2], out int amount):
				w.Gold = Math.Clamp(w.Gold + amount, 0, LedgerRules.MaxGold);
				Log(w, amount >= 0 ? EntryKinds.Sales : EntryKinds.Shopping, amount, string.Join(" ", args.Skip(3)));
				Changed();
				return $"记了一笔：{amount:+#;-#;0}g {string.Join(" ", args.Skip(3))}；身上 {w.Gold}g。";
			case "event" when args.Length >= 3 && Wallet(npc) is NpcWallet:
			{
				string text = string.Join(" ", args.Skip(2));
				Event(npc, EntryKinds.Hustle, 0, text);
				Gossip?.Invoke(text, new[] { npc });
				Changed();
				return "记进了镇上的钱事，也传成了八卦：" + text;
			}
			case "hustle" when Wallet(npc) is NpcWallet w && Game1.getCharacterFromName(npc) is NPC hn:
			{
				string kind = args.Length > 2 ? args[2].ToLowerInvariant() : "";
				if (kind.Length == 0)
				{
					w.Hustle = null;
					PlanHustle(hn, Utility.isFestivalDay(Game1.dayOfMonth, Game1.season));
					return w.Hustle is Hustle planned ? $"{npc} 今天{planned.Plan}（{planned.Time} 见分晓）。" : $"{npc} 不缺钱或这次没想办法（身上 {w.Gold}g，存款 {w.Savings}g）。";
				}
				if (!HustleKinds.All.Contains(kind))
				{
					return "办法：" + string.Join(" / ", HustleKinds.All);
				}
				string? named = args.Length > 3 && !args[3].Equals("now", StringComparison.OrdinalIgnoreCase) ? args[3] : null;
				string other = kind switch { HustleKinds.Borrow => named ?? Lender(npc) ?? "", HustleKinds.OddJob => named ?? Employer(npc) ?? "", _ => "" };
				(string Id, int Quality, int Price)? valuable = Valuable(w);
				if (kind == HustleKinds.SellOwned && valuable == null)
				{
					return "家里没有值钱的东西可卖。";
				}
				StartHustle(npc, w, new HustleChoice(kind, args.Contains("now") ? Game1.timeOfDay : HustleRules.TimeOf(kind), other, valuable?.Id ?? "", valuable?.Quality ?? 0));
				if (args.Contains("now") && !w.Hustle!.Done)
				{
					ResolveHustle(npc, w, w.Hustle);
				}
				return $"{npc}：{w.Hustle!.Plan}{(w.Hustle.Done ? "；" + w.Hustle.Note : "")}";
			}
			case "npcloan" when args.Length >= 4 && Book is LedgerBook lb && Wallet(npc) is NpcWallet lender && Wallet(args[2]) is NpcWallet borrower && int.TryParse(args[3], out int loan):
			{
				int days = args.Length > 4 && int.TryParse(args[4], out int d) ? d : HustleRules.LoanDays;
				lender.Gold = Math.Max(0, lender.Gold - loan);
				borrower.Gold += loan;
				lb.NpcDebts.Add(new NpcDebt { Id = "n" + lb.NextNpcDebt++, Lender = npc, Borrower = args[2], Amount = loan, Day = Today, DueDay = Today + days });
				Changed();
				return $"{args[2]} 向 {npc} 借了 {loan}g，{days} 天后到期。";
			}
			case "ad" when args.Length >= 4 && Wallet(npc) is NpcWallet:
			{
				int price = args.Length > 4 && int.TryParse(args[4], out int p) ? p : 0;
				int sellIn = args.Length > 5 && int.TryParse(args[5], out int si) ? si : 0;
				string why = args.Length > 6 ? string.Join(" ", args.Skip(6)) : "";
				Ad? ad = DebugAd(npc, args[2].ToLowerInvariant(), args[3], price, sellIn, why);
				return ad == null ? "没登上。" : $"{ad.Id}：{ClassifiedRules.Title(ad)}（{ad.Text}）";
			}
			case "adsell" when Book?.Ads.FirstOrDefault(a => a.Id == npc) is Ad sold:
				sold.SoldDay = Today;
				sold.Buyer = args.Length > 2 ? args[2] : "";
				Changed();
				return $"{sold.Id} 标成已售出。";
			case "ads":
				return string.Join("\n", Ads().Select(a => $"{a.Id} {a.Npc} {ClassifiedRules.Title(a)} {(a.SoldDay >= 0 ? "已售出" : "")} {a.Text}"));
			case "reset":
				if (Book is LedgerBook book)
				{
					book.Wallets.Clear();
					book.Debts.Clear();
					book.Events.Clear();
					book.LastDailyDay = -1;
					Changed();
				}
				return "清空了，下次用到时重新生成。";
			default:
				return Usage;
		}
	}
}
