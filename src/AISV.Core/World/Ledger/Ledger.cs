using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.World.Ledger;

public sealed class LedgerItem
{
	public string Id { get; set; } = "";

	public int Count { get; set; } = 1;

	public int Quality { get; set; }
}

/// <summary>一笔账：正数是进账，负数是花出去。</summary>
public sealed class LedgerEntry
{
	public int Day { get; set; }

	public int Time { get; set; }

	public string Kind { get; set; } = "";

	public int Amount { get; set; }

	public string Note { get; set; } = "";
}

public static class EntryKinds
{
	public const string Wage = "wage";
	public const string Sales = "sales";
	public const string Shopping = "shopping";
	public const string Saloon = "saloon";
	public const string Snack = "snack";
	public const string Festival = "festival";
	public const string Allowance = "allowance";
	public const string Living = "living";
	public const string Theft = "theft";
	public const string Errand = "errand";
	public const string Commission = "commission";
	public const string Loan = "loan";
	public const string Repay = "repay";
	public const string Savings = "savings";
	public const string Hustle = "hustle";
	public const string NpcLoan = "npc_loan";
	public const string Gift = "gift";
	public const string Ad = "ad";
}

public sealed class NpcWallet
{
	public string Npc { get; set; } = "";

	public int Gold { get; set; }

	public int Savings { get; set; }

	public List<LedgerItem> Carried { get; set; } = new List<LedgerItem>();

	public List<LedgerItem> Home { get; set; } = new List<LedgerItem>();

	public List<LedgerEntry> Log { get; set; } = new List<LedgerEntry>();

	public int CreatedDay { get; set; }

	/// <summary>今天逛店买过东西的日子。</summary>
	public int ShoppedDay { get; set; } = -1;

	/// <summary>手头紧时最近一次想的办法。</summary>
	public Hustle? Hustle { get; set; }

	/// <summary>省钱省到哪天。</summary>
	public int TightUntil { get; set; } = -1;

	public int Total => Gold + Savings;
}

public static class DebtStates
{
	public const string Open = "open";
	public const string Paid = "paid";
	public const string Collected = "collected";
	public const string Forgiven = "forgiven";
}

public static class DebtStyles
{
	public const string Demand = "demand";
	public const string Remind = "remind";
	public const string Quiet = "quiet";
}

/// <summary>农夫向村民借的钱。</summary>
public sealed class Debt
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	public int Amount { get; set; }

	public int Repaid { get; set; }

	public int Day { get; set; }

	public int DueDay { get; set; }

	public string State { get; set; } = DebtStates.Open;

	public string Style { get; set; } = DebtStyles.Remind;

	public int ClosedDay { get; set; } = -1;

	/// <summary>催了几次。</summary>
	public int Reminders { get; set; }

	public int LastReminderDay { get; set; } = -1;

	/// <summary>强要的人说过"三天内不还就自己拿"的日子。</summary>
	public int WarnedDay { get; set; } = -1;

	/// <summary>忍气吞声时已经扣掉的好感。</summary>
	public int FriendshipLost { get; set; }

	public bool Gossiped { get; set; }

	/// <summary>在店里赊的账（不是开口借的钱）。</summary>
	public bool Tab { get; set; }

	public int Owed => Math.Max(0, Amount - Repaid);
}

/// <summary>镇上的一件钱事。</summary>
public sealed class TownEvent
{
	public int Day { get; set; }

	public string Npc { get; set; } = "";

	public string Kind { get; set; } = "";

	public int Amount { get; set; }

	public string Text { get; set; } = "";
}

public sealed class LedgerBook
{
	public const string Path = "town/purse_book.json";

	public static readonly DocumentType<LedgerBook> Doc = new DocumentType<LedgerBook>("村民账本", 1, () => new LedgerBook());

	public Dictionary<string, NpcWallet> Wallets { get; set; } = new Dictionary<string, NpcWallet>(StringComparer.OrdinalIgnoreCase);

	public List<Debt> Debts { get; set; } = new List<Debt>();

	public List<TownEvent> Events { get; set; } = new List<TownEvent>();

	public int NextDebt { get; set; } = 1;

	public int LastDailyDay { get; set; } = -1;

	/// <summary>已经扣过的偷窃记录。</summary>
	public List<string> TheftsApplied { get; set; } = new List<string>();

	public List<NpcDebt> NpcDebts { get; set; } = new List<NpcDebt>();

	public List<Ad> Ads { get; set; } = new List<Ad>();

	public int NextAd { get; set; } = 1;

	/// <summary>离婚后已经登过卖婚礼衣服广告的人。</summary>
	public HashSet<string> DivorceAdsPosted { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public int NextNpcDebt { get; set; } = 1;
}

/// <summary>职业档案：周薪（0 = 没有）、开的店、身上大概多少、存款、花钱习惯、职业物品。</summary>
public sealed record Occupation(string Job, int Wage, string? Shop, int Gold, int Savings, int Style, string[] Items, bool Child = false);

public static class LedgerRules
{
	public const int MaxCarried = 12;
	public const int MaxHome = 36;
	public const int MaxLog = 120;
	public const int MaxEvents = 40;
	public const int MaxGold = 999_999;
	public const int BigSpend = 500;
	public const int LoanMinHearts = 4;
	public const int LoanPerHeart = 500;
	public const int SaloonTime = 2200;
	public const int TabMinHearts = 2;
	public const int TabDays = 7;
	public const string TabShop = "Saloon";
	public const string TabOwner = "Gus";
	public static readonly int[] LoanDays = { 3, 7, 14 };

	/// <summary>节省 / 一般 / 大方。</summary>
	public const int Thrifty = 0, Normal = 1, Lavish = 2;

	public static readonly IReadOnlyDictionary<string, Occupation> Jobs = new Dictionary<string, Occupation>(StringComparer.OrdinalIgnoreCase)
	{
		["Pierre"] = new Occupation("开杂货店", 0, "SeedShop", 1800, 12000, Thrifty, new[] { "(O)472", "(O)473", "(O)474", "(O)475" }),
		["Caroline"] = new Occupation("操持家务、照看杂货店", 0, null, 400, 0, Normal, new[] { "(O)614", "(O)18" }),
		["Abigail"] = new Occupation("没有固定工作", 100, null, 250, 300, Lavish, new[] { "(O)66", "(O)86", "(O)80" }),
		["Gus"] = new Occupation("开星之果实餐吧", 0, "Saloon", 900, 6000, Normal, new[] { "(O)346", "(O)195", "(O)206" }),
		["Emily"] = new Occupation("在酒吧打工、做衣服", 350, null, 300, 800, Normal, new[] { "(O)428", "(O)62", "(O)72" }),
		["Haley"] = new Occupation("拍照", 150, null, 500, 600, Lavish, new[] { "(O)221", "(O)421" }),
		["Clint"] = new Occupation("开铁匠铺", 0, "Blacksmith", 700, 5000, Thrifty, new[] { "(O)334", "(O)335", "(O)382" }),
		["Willy"] = new Occupation("开鱼店", 0, "FishShop", 500, 4000, Thrifty, new[] { "(O)685", "(O)145", "(O)131" }),
		["Marnie"] = new Occupation("开牧场", 0, "AnimalShop", 600, 5000, Normal, new[] { "(O)178", "(O)176", "(O)184" }),
		["Robin"] = new Occupation("开木匠铺", 0, "ScienceHouse", 900, 7000, Normal, new[] { "(O)388", "(O)709" }),
		["Harvey"] = new Occupation("诊所医生", 0, "Hospital", 1200, 15000, Thrifty, new[] { "(O)349", "(O)351", "(O)395" }),
		["Morris"] = new Occupation("Joja 超市经理", 1200, null, 2500, 30000, Lavish, new[] { "(O)167" }),
		["Lewis"] = new Occupation("镇长", 1000, null, 1500, 20000, Thrifty, new[] { "(O)200", "(O)348" }),
		["Demetrius"] = new Occupation("研究员", 900, null, 1000, 9000, Thrifty, new[] { "(O)330", "(O)404", "(O)420" }),
		["Maru"] = new Occupation("诊所护士、做发明", 450, null, 500, 2000, Normal, new[] { "(O)334", "(O)787", "(O)337" }),
		["Sebastian"] = new Occupation("在家写程序", 500, null, 400, 1500, Normal, new[] { "(O)84", "(O)227" }),
		["Sam"] = new Occupation("在 Joja 打零工", 300, null, 200, 300, Lavish, new[] { "(O)206", "(O)167" }),
		["Jodi"] = new Occupation("操持家务", 0, null, 400, 1500, Thrifty, new[] { "(O)211", "(O)200" }),
		["Kent"] = new Occupation("退伍军人", 700, null, 600, 4000, Thrifty, new[] { "(O)607", "(O)649" }),
		["Penny"] = new Occupation("给孩子上课", 250, null, 150, 200, Thrifty, new[] { "(O)102", "(O)376" }),
		["Pam"] = new Occupation("开巴士", 400, null, 150, 100, Lavish, new[] { "(O)346", "(O)303" }),
		["Alex"] = new Occupation("夏天卖冰淇淋", 200, null, 300, 400, Lavish, new[] { "(O)233", "(O)243" }),
		["George"] = new Occupation("领退休金", 300, null, 300, 5000, Thrifty, new[] { "(O)20" }),
		["Evelyn"] = new Occupation("领退休金、照看花园", 250, null, 300, 4000, Normal, new[] { "(O)591", "(O)597", "(O)220" }),
		["Elliott"] = new Occupation("写小说", 300, null, 250, 1200, Normal, new[] { "(O)128", "(O)597", "(O)718" }),
		["Leah"] = new Occupation("雕刻和画画", 300, null, 200, 800, Thrifty, new[] { "(O)392", "(O)404", "(O)388" }),
		["Linus"] = new Occupation("靠采集过活", 50, null, 30, 0, Thrifty, new[] { "(O)16", "(O)22", "(O)404" }),
		["Shane"] = new Occupation("在 Joja 上班", 350, null, 150, 200, Lavish, new[] { "(O)346", "(O)206" }),
		["Wizard"] = new Occupation("研究魔法", 0, null, 3000, 80000, Thrifty, new[] { "(O)768", "(O)769", "(O)422" }),
		["Sandy"] = new Occupation("开绿洲服装店", 0, "SandyHouse", 900, 6000, Lavish, new[] { "(O)88", "(O)90" }),
		["Krobus"] = new Occupation("在下水道开店", 0, null, 1500, 9000, Thrifty, new[] { "(O)305", "(O)769" }),
		["Dwarf"] = new Occupation("在矿洞里做买卖", 0, null, 1200, 8000, Thrifty, new[] { "(O)382", "(O)384" }),
		["Gunther"] = new Occupation("博物馆馆长", 400, null, 500, 5000, Thrifty, new[] { "(O)101", "(O)114" }),
		["Leo"] = new Occupation("住在姜岛", 0, null, 20, 0, Normal, new[] { "(O)88" }),
		["Jas"] = new Occupation("上学", 0, null, 40, 0, Normal, Array.Empty<string>(), Child: true),
		["Vincent"] = new Occupation("上学", 0, null, 40, 0, Lavish, Array.Empty<string>(), Child: true)
	};

	/// <summary>孩子的零花钱从谁那里来。</summary>
	public static readonly IReadOnlyDictionary<string, string> Parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Jas"] = "Marnie",
		["Vincent"] = "Jodi"
	};

	public const int Allowance = 20;

	/// <summary>店每天外来客人带来的收入（下限、上限）。</summary>
	public static readonly IReadOnlyDictionary<string, (int Min, int Max)> ShopTraffic = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
	{
		["Pierre"] = (80, 220),
		["Gus"] = (60, 160),
		["Clint"] = (40, 130),
		["Willy"] = (30, 90),
		["Marnie"] = (50, 150),
		["Robin"] = (60, 200),
		["Harvey"] = (60, 180),
		["Sandy"] = (40, 140)
	};

	public static readonly IReadOnlyDictionary<string, string> DebtStyleOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Pierre"] = DebtStyles.Demand,
		["Morris"] = DebtStyles.Demand,
		["Clint"] = DebtStyles.Demand,
		["Shane"] = DebtStyles.Demand,
		["Haley"] = DebtStyles.Demand,
		["George"] = DebtStyles.Demand,
		["Penny"] = DebtStyles.Quiet,
		["Leah"] = DebtStyles.Quiet,
		["Emily"] = DebtStyles.Quiet,
		["Linus"] = DebtStyles.Quiet,
		["Caroline"] = DebtStyles.Quiet,
		["Evelyn"] = DebtStyles.Quiet,
		["Harvey"] = DebtStyles.Quiet,
		["Elliott"] = DebtStyles.Quiet,
		["Sam"] = DebtStyles.Quiet,
		["Jodi"] = DebtStyles.Quiet,
		["Maru"] = DebtStyles.Quiet
	};

	public static Occupation JobOf(string npc, bool child) => Jobs.TryGetValue(npc, out Occupation? job) ? job
		: child ? new Occupation("上学", 0, null, 40, 0, Normal, Array.Empty<string>(), Child: true)
		: new Occupation("过自己的日子", 300, null, 300, 1500, Normal, Array.Empty<string>());

	/// <summary>内容包写的"身上,存款,周薪"。</summary>
	public static Occupation Override(Occupation job, string? field)
	{
		if (string.IsNullOrWhiteSpace(field))
		{
			return job;
		}
		string[] parts = field.Split(new[] { ',', '，' }, StringSplitOptions.TrimEntries);
		int Part(int i, int fallback) => parts.Length > i && int.TryParse(parts[i], out int v) && v >= 0 ? Math.Min(v, MaxGold) : fallback;
		return job with { Gold = Part(0, job.Gold), Savings = Part(1, job.Savings), Wage = Part(2, job.Wage) };
	}

	public static string StyleOf(string npc, string? field) => field?.Trim().ToLowerInvariant() is DebtStyles.Demand or DebtStyles.Remind or DebtStyles.Quiet
		? field.Trim().ToLowerInvariant()
		: DebtStyleOf.TryGetValue(npc, out string? s) ? s : DebtStyles.Remind;

	/// <summary>同一个存档、同一个人每次都一样的随机数。</summary>
	public static Random Seeded(ulong saveSeed, string npc)
	{
		int h = 17;
		foreach (char c in npc.ToLowerInvariant())
		{
			h = unchecked(h * 31 + c);
		}
		return new Random(unchecked((int)saveSeed ^ h));
	}

	public static int Vary(int value, Random rng) => value <= 0 ? 0 : (int)Math.Round(value * (0.8 + rng.NextDouble() * 0.4));

	/// <summary>一周的生活费：节省 80、一般 160、大方 300；拿工资的人最多花掉周薪的六成，没收入的人减半。</summary>
	public static int LivingCost(Occupation job)
	{
		if (job.Child)
		{
			return 0;
		}
		int cost = job.Style switch { Thrifty => 80, Lavish => 300, _ => 160 };
		if (job.Wage > 0)
		{
			return Math.Min(cost, job.Wage * 3 / 5);
		}
		return job.Shop == null ? cost / 2 : cost;
	}

	/// <summary>开店的人每周进货花掉这周进账的四成。</summary>
	public static int Restock(int weekSales) => Math.Max(0, weekSales) * 2 / 5;

	/// <summary>付完生活费以后存钱或取钱：多于两周生活费，多出来的一半存起来；少于半周，从存款里补到一周。返回存进去的数（负数是取出来）。</summary>
	public static int Rebalance(NpcWallet w, int weekly)
	{
		if (weekly <= 0)
		{
			return 0;
		}
		if (w.Gold > weekly * 2)
		{
			int put = (w.Gold - weekly * 2) / 2;
			w.Gold -= put;
			w.Savings += put;
			return put;
		}
		if (w.Gold < weekly / 2 && w.Savings > 0)
		{
			int take = Math.Min(w.Savings, weekly - w.Gold);
			w.Gold += take;
			w.Savings -= take;
			return -take;
		}
		return 0;
	}

	/// <summary>付钱：先用身上的，不够从存款里取；返回实际付出去的数（最多付到 0）。</summary>
	public static int Pay(NpcWallet w, int amount)
	{
		amount = Math.Max(0, amount);
		int fromGold = Math.Min(w.Gold, amount);
		w.Gold -= fromGold;
		int fromSavings = Math.Min(w.Savings, amount - fromGold);
		w.Savings -= fromSavings;
		return fromGold + fromSavings;
	}

	public static void Clamp(NpcWallet w)
	{
		w.Gold = Math.Clamp(w.Gold, 0, MaxGold);
		w.Savings = Math.Clamp(w.Savings, 0, MaxGold);
	}

	/// <summary>记一笔；和上一笔同一天、同类、同说明、同方向时并成一笔。</summary>
	public static void AddLog(NpcWallet w, LedgerEntry e)
	{
		if (w.Log.Count > 0 && w.Log[0] is LedgerEntry last && last.Day == e.Day && last.Kind == e.Kind && last.Note == e.Note && Math.Sign(last.Amount) == Math.Sign(e.Amount))
		{
			last.Amount += e.Amount;
			last.Time = e.Time;
			return;
		}
		w.Log.Insert(0, e);
		if (w.Log.Count > MaxLog)
		{
			w.Log.RemoveRange(MaxLog, w.Log.Count - MaxLog);
		}
	}

	/// <summary>放进一个物品栏：同 ID 同品质叠在一起，满了放不下就返回 false。</summary>
	public static bool Put(List<LedgerItem> slots, int max, string id, int count, int quality)
	{
		LedgerItem? same = slots.FirstOrDefault(i => i.Id == id && i.Quality == quality);
		if (same != null)
		{
			same.Count = Math.Min(999, same.Count + count);
			return true;
		}
		if (slots.Count >= max)
		{
			return false;
		}
		slots.Add(new LedgerItem { Id = id, Count = Math.Max(1, count), Quality = quality });
		return true;
	}

	/// <summary>拿掉最多 count 个；返回实际拿掉的数。</summary>
	public static int Take(List<LedgerItem> slots, string id, int count, int quality = -1)
	{
		int taken = 0;
		foreach (LedgerItem i in slots.Where(i => i.Id == id && (quality < 0 || i.Quality == quality)).ToList())
		{
			int n = Math.Min(i.Count, count - taken);
			i.Count -= n;
			taken += n;
			if (i.Count <= 0)
			{
				slots.Remove(i);
			}
			if (taken >= count)
			{
				break;
			}
		}
		return taken;
	}

	/// <summary>最多能借多少：交情不到 4 心不借；不超过他身上钱的一半，也不超过心数 × 500g；按 10g 取整。</summary>
	public static int LoanLimit(int hearts, int gold) => hearts < LoanMinHearts ? 0 : Math.Min(gold / 2, hearts * LoanPerHeart) / 10 * 10;

	/// <summary>酒吧能赊多少：交情 2 心起，100g 加每心 100g，最多 1000g，减去已经赊着的。</summary>
	public static int TabRoom(int hearts, int owed) => hearts < TabMinHearts ? 0 : Math.Max(0, Math.Min(1000, 100 + hearts * 100) - Math.Max(0, owed));

	/// <summary>逛店：今天还没买过、店开着、不是自己的店，35% 的机会。</summary>
	public static bool WillShop(double roll, bool shoppedToday, bool ownShop, bool open) => !shoppedToday && !ownShop && open && roll < 0.35;

	/// <summary>逛店最多花身上钱的四分之一。</summary>
	public static int ShopBudget(int gold) => gold / 4;

	public static int SaloonSpend(Random rng, int style) => rng.Next(20, 50) + style * 15;

	public static int FestivalSpend(Random rng) => rng.Next(20, 121);

	/// <summary>到期不还：强要的人到期第二天警告，再过三天自己拿；催收的到期第二天和之后每三天催一次，第二次以后每次好感 -5；忍气吞声的每天 -2（最多 100）。</summary>
	public static DebtAction Overdue(Debt d, int today)
	{
		if (d.State != DebtStates.Open || today <= d.DueDay)
		{
			return DebtAction.None;
		}
		switch (d.Style)
		{
			case DebtStyles.Demand:
				if (d.WarnedDay < 0)
				{
					return DebtAction.Warn;
				}
				return today - d.WarnedDay >= 3 ? DebtAction.Collect : DebtAction.None;
			case DebtStyles.Quiet:
				return d.FriendshipLost < 100 ? DebtAction.Sulk : DebtAction.None;
			default:
				return d.LastReminderDay < 0 || today - d.LastReminderDay >= 3 ? DebtAction.Remind : DebtAction.None;
		}
	}

	public const int RemindFriendship = -5;
	public const int SulkFriendship = -2;

	public static int RemindPenalty(Debt d) => d.Reminders >= 2 ? RemindFriendship : 0;

	private static readonly Dictionary<string, Func<string>> KindLabels = new Dictionary<string, Func<string>>
	{
		[EntryKinds.Wage] = () => Tr.Get("ledger.kind.wage", "工资"),
		[EntryKinds.Sales] = () => Tr.Get("ledger.kind.sales", "店里收入"),
		[EntryKinds.Shopping] = () => Tr.Get("ledger.kind.shopping", "买东西"),
		[EntryKinds.Saloon] = () => Tr.Get("ledger.kind.saloon", "酒吧"),
		[EntryKinds.Snack] = () => Tr.Get("ledger.kind.snack", "零嘴"),
		[EntryKinds.Festival] = () => Tr.Get("ledger.kind.festival", "节日"),
		[EntryKinds.Allowance] = () => Tr.Get("ledger.kind.allowance", "零花钱"),
		[EntryKinds.Living] = () => Tr.Get("ledger.kind.living", "生活费"),
		[EntryKinds.Theft] = () => Tr.Get("ledger.kind.theft", "被偷"),
		[EntryKinds.Errand] = () => Tr.Get("ledger.kind.errand", "帮农夫办事"),
		[EntryKinds.Commission] = () => Tr.Get("ledger.kind.commission", "委托酬劳"),
		[EntryKinds.Loan] = () => Tr.Get("ledger.kind.loan", "借给农夫"),
		[EntryKinds.Repay] = () => Tr.Get("ledger.kind.repay", "农夫还钱"),
		[EntryKinds.Hustle] = () => Tr.Get("ledger.kind.hustle", "想办法挣钱"),
		[EntryKinds.NpcLoan] = () => Tr.Get("ledger.kind.npcLoan", "村民之间借钱"),
		[EntryKinds.Gift] = () => Tr.Get("ledger.kind.gift", "送东西"),
		[EntryKinds.Ad] = () => Tr.Get("ledger.kind.ad", "分类广告")
	};

	public static string KindLabel(string kind) => KindLabels.TryGetValue(kind, out Func<string>? label) ? label() : Tr.Get("ledger.kind.savings", "存取");

	/// <summary>给这位村民的一段：你的钱。</summary>
	public static string PromptBlock(NpcWallet w, Occupation job, IEnumerable<string> carried, IEnumerable<Debt> debts, int today)
	{
		StringBuilder sb = new StringBuilder("【你的钱】\n");
		sb.Append($"- 你{job.Job}。身上有 {w.Gold}g，存款 {w.Savings}g。\n");
		List<string> things = carried.Take(5).ToList();
		if (things.Count > 0)
		{
			sb.Append("- 你身上带着：").Append(string.Join("、", things)).Append("。\n");
		}
		foreach (LedgerEntry e in w.Log.Where(e => today - e.Day <= 3 && e.Kind is not (EntryKinds.Savings or EntryKinds.Sales)).Take(4))
		{
			sb.Append($"- {(today - e.Day <= 0 ? "今天" : today - e.Day == 1 ? "昨天" : $"{today - e.Day} 天前")}{(e.Amount >= 0 ? "进了" : "花了")} {Math.Abs(e.Amount)}g（{e.Note}）。\n");
		}
		foreach (Debt d in debts.Where(d => d.State == DebtStates.Open))
		{
			int left = d.DueDay - today;
			string what = d.Tab ? $"农夫在你店里赊了 {d.Amount}g 的账" : $"农夫向你借了 {d.Amount}g";
			sb.Append(left >= 0
				? $"- {what}，还欠 {d.Owed}g，{(left == 0 ? "今天" : $"{left} 天后")}到期。\n"
				: $"- {what}，还欠 {d.Owed}g，已经过期 {-left} 天了。{StyleHint(d.Style)}\n");
		}
		foreach (Debt d in debts.Where(d => d.State != DebtStates.Open && today - d.ClosedDay <= 7))
		{
			sb.Append(d.State switch
			{
				DebtStates.Paid => $"- 农夫借你的 {d.Amount}g 已经还清了。\n",
				DebtStates.Collected => $"- 农夫借的钱一直不还，你自己从他那儿拿回来了。\n",
				_ => ""
			});
		}
		return sb.ToString().TrimEnd();
	}

	private static string StyleHint(string style) => style switch
	{
		DebtStyles.Demand => "你会直接开口要。",
		DebtStyles.Quiet => "你不好意思开口，但心里有疙瘩。",
		_ => "合适的时候提醒一下。"
	};
}

public enum DebtAction
{
	None,
	Warn,
	Collect,
	Remind,
	Sulk
}
