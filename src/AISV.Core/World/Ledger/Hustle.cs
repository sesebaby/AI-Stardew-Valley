using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;

namespace AISV.Core.World.Ledger;

public static class HustleKinds
{
	public const string Forage = "forage";
	public const string Scavenge = "scavenge";
	public const string Busk = "busk";
	public const string OddJob = "odd_job";
	public const string Crafts = "crafts";
	public const string SellOwned = "sell";
	public const string Borrow = "borrow";
	public const string Tighten = "tighten";

	public static readonly string[] All = { Forage, Scavenge, Busk, OddJob, Crafts, SellOwned, Borrow, Tighten };
}

/// <summary>手头紧时这一天想的办法。</summary>
public sealed class Hustle
{
	public int Day { get; set; }

	public string Kind { get; set; } = "";

	/// <summary>到这个钟点见分晓。</summary>
	public int Time { get; set; }

	/// <summary>付钱或借钱给他的人（内部名，可能为空）。</summary>
	public string Other { get; set; } = "";

	/// <summary>要卖的家当。</summary>
	public string ItemId { get; set; } = "";

	public int ItemQuality { get; set; }

	public bool Done { get; set; }

	public int Earned { get; set; }

	/// <summary>打算做什么（不带钱数）。</summary>
	public string Plan { get; set; } = "";

	/// <summary>见分晓以后的一句说明，给 AI 和账本看。</summary>
	public string Note { get; set; } = "";

	public int BubbleTime { get; set; } = -1;
}

public static class NpcDebtStates
{
	public const string Open = "open";
	public const string Paid = "paid";
	public const string Forgiven = "forgiven";
	public const string Collected = "collected";
}

/// <summary>村民之间的一笔债。</summary>
public sealed class NpcDebt
{
	public string Id { get; set; } = "";

	public string Lender { get; set; } = "";

	public string Borrower { get; set; } = "";

	public int Amount { get; set; }

	public int Day { get; set; }

	public int DueDay { get; set; }

	public string State { get; set; } = NpcDebtStates.Open;

	public int ClosedDay { get; set; } = -1;

	public bool Strained { get; set; }
}

public sealed record HustleChoice(string Kind, int Time, string Other = "", string ItemId = "", int ItemQuality = 0);

/// <summary>想办法时能用的东西：谁肯借、谁雇人、家里最值钱的东西、酒吧今晚开不开。</summary>
public sealed record HustleOptions(string? Lender, string? Employer, (string Id, int Quality, int Price)? Valuable, bool SaloonOpen, bool Festival);

public static class HustleRules
{
	public const int CooldownDays = 2;
	public const double Chance = 0.7;
	public const int TightDays = 7;
	public const int LoanDays = 7;
	public const int ForgiveAfter = 14;
	public const int MinValuable = 30;

	/// <summary>每个人偏爱的办法，排在前面的更常用。</summary>
	public static readonly IReadOnlyDictionary<string, string[]> Preferred = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Linus"] = new[] { HustleKinds.Forage, HustleKinds.Scavenge },
		["Leah"] = new[] { HustleKinds.Forage, HustleKinds.Crafts },
		["Penny"] = new[] { HustleKinds.OddJob, HustleKinds.Forage, HustleKinds.Tighten },
		["Sam"] = new[] { HustleKinds.Busk, HustleKinds.OddJob, HustleKinds.Borrow },
		["Abigail"] = new[] { HustleKinds.Busk, HustleKinds.SellOwned, HustleKinds.Borrow },
		["Sebastian"] = new[] { HustleKinds.OddJob, HustleKinds.SellOwned, HustleKinds.Busk },
		["Shane"] = new[] { HustleKinds.OddJob, HustleKinds.Borrow, HustleKinds.Scavenge },
		["Haley"] = new[] { HustleKinds.Crafts, HustleKinds.SellOwned, HustleKinds.Borrow },
		["Emily"] = new[] { HustleKinds.Crafts, HustleKinds.OddJob, HustleKinds.Tighten },
		["Alex"] = new[] { HustleKinds.OddJob, HustleKinds.Borrow, HustleKinds.SellOwned },
		["Pam"] = new[] { HustleKinds.Borrow, HustleKinds.Scavenge, HustleKinds.OddJob },
		["Elliott"] = new[] { HustleKinds.Crafts, HustleKinds.Busk, HustleKinds.Tighten },
		["Maru"] = new[] { HustleKinds.OddJob, HustleKinds.Crafts },
		["Caroline"] = new[] { HustleKinds.Crafts, HustleKinds.Tighten },
		["Jodi"] = new[] { HustleKinds.Tighten, HustleKinds.Forage, HustleKinds.SellOwned },
		["Kent"] = new[] { HustleKinds.OddJob, HustleKinds.Tighten },
		["George"] = new[] { HustleKinds.Tighten, HustleKinds.SellOwned },
		["Evelyn"] = new[] { HustleKinds.Forage, HustleKinds.Crafts, HustleKinds.Tighten },
		["Leo"] = new[] { HustleKinds.Forage, HustleKinds.Scavenge }
	};

	/// <summary>某些人做同一件事时的说法。</summary>
	private static readonly IReadOnlyDictionary<string, string> Flavor = new (string Kind, (string Who, string What)[] Lines)[]
	{
		(HustleKinds.OddJob, new[] { ("Penny", "周末加了两节私教课"), ("Sebastian", "替城里人赶了个脚本"), ("Maru", "上门修好了邻居的收音机和水泵"), ("Kent", "替人上房补瓦") }),
		(HustleKinds.Busk, new[] { ("Sam", "背着吉他去酒馆卖唱"), ("Abigail", "在酒馆角落吹了一晚上笛子"), ("Sebastian", "去酒馆弹合成器换几个钱"), ("Elliott", "在酒馆念诗，靠打赏凑点稿费") }),
		(HustleKinds.Crafts, new[] { ("Haley", "冲洗了一叠风景照摆出来卖"), ("Emily", "缝了几条裙子托人代卖"), ("Leah", "刻了只小鸟放到店里卖"), ("Elliott", "把一篇散文卖给了报社"), ("Caroline", "把自家茶叶分装卖了几罐"), ("Maru", "焊了个小玩意儿摆出去卖"), ("Evelyn", "把院里的花晾干扎成束卖") }),
		(HustleKinds.Scavenge, new[] { ("Linus", "夜里又去垃圾桶里找能用的") }),
		(HustleKinds.Forage, new[] { ("Linus", "上山采了一篮野果，吃不完的换钱") })
	}.SelectMany(g => g.Lines.Select(l => (Key: l.Who + "|" + g.Kind, l.What))).ToDictionary(x => x.Key, x => x.What, StringComparer.OrdinalIgnoreCase);

	public static int Weekly(Occupation job) => Math.Max(60, LedgerRules.LivingCost(job));

	/// <summary>手头紧：身上不到半周生活费（最少 30g），存款也不够一周。</summary>
	public static bool Broke(NpcWallet w, Occupation job) => !job.Child && w.Gold < Math.Max(30, Weekly(job) / 2) && w.Savings < Weekly(job);

	public static bool Tight(NpcWallet w, int today) => w.TightUntil >= today;

	/// <summary>今天要不要想办法、想什么办法；不用时为 null。</summary>
	public static HustleChoice? Choose(string npc, NpcWallet w, Occupation job, HustleOptions o, int today, Random rng)
	{
		if (!Broke(w, job) || (w.Hustle != null && today - w.Hustle.Day < CooldownDays) || rng.NextDouble() >= Chance)
		{
			return null;
		}
		List<string> kinds = (Preferred.TryGetValue(npc, out string[]? p) ? p : Fallback(job.Style)).Where(k => Possible(k, o, w, today)).ToList();
		if (kinds.Count == 0)
		{
			kinds = Fallback(job.Style).Where(k => Possible(k, o, w, today)).ToList();
		}
		if (kinds.Count == 0)
		{
			return null;
		}
		int total = 0;
		int[] weights = kinds.Select((_, i) => Math.Max(1, 3 - i)).ToArray();
		int roll = rng.Next(weights.Sum());
		string kind = kinds[^1];
		for (int i = 0; i < kinds.Count; i++)
		{
			total += weights[i];
			if (roll < total)
			{
				kind = kinds[i];
				break;
			}
		}
		return kind switch
		{
			HustleKinds.Borrow => new HustleChoice(kind, 1000, o.Lender ?? ""),
			HustleKinds.OddJob => new HustleChoice(kind, 1700, o.Employer ?? ""),
			HustleKinds.SellOwned => new HustleChoice(kind, 1100, "", o.Valuable!.Value.Id, o.Valuable.Value.Quality),
			_ => new HustleChoice(kind, TimeOf(kind))
		};
	}

	private static bool Possible(string kind, HustleOptions o, NpcWallet w, int today) => kind switch
	{
		HustleKinds.Busk => o.SaloonOpen && !o.Festival,
		HustleKinds.Borrow => !string.IsNullOrEmpty(o.Lender),
		HustleKinds.OddJob => !string.IsNullOrEmpty(o.Employer),
		HustleKinds.SellOwned => o.Valuable is { Price: >= MinValuable },
		HustleKinds.Tighten => !Tight(w, today),
		_ => true
	};

	public static string[] Fallback(int style) => style switch
	{
		LedgerRules.Thrifty => new[] { HustleKinds.Forage, HustleKinds.Tighten, HustleKinds.OddJob },
		LedgerRules.Lavish => new[] { HustleKinds.Borrow, HustleKinds.SellOwned, HustleKinds.OddJob },
		_ => new[] { HustleKinds.OddJob, HustleKinds.SellOwned, HustleKinds.Tighten }
	};

	public static int TimeOf(string kind) => kind switch
	{
		HustleKinds.Forage => 1300,
		HustleKinds.Scavenge => 1900,
		HustleKinds.Busk => 2100,
		HustleKinds.Crafts => 1500,
		HustleKinds.Tighten => 600,
		_ => 1200
	};

	/// <summary>挣多少（卖家当和挖野菜按东西的价钱，另算）。</summary>
	public static int Pay(string kind, Random rng) => kind switch
	{
		HustleKinds.Scavenge => rng.Next(10, 61),
		HustleKinds.Busk => rng.Next(30, 151),
		HustleKinds.OddJob => rng.Next(80, 201),
		HustleKinds.Crafts => rng.Next(60, 251),
		_ => 0
	};

	/// <summary>能借多少：够一周生活费、至少 100g，不超过借给他的人身上的四分之一，按 10g 取整。</summary>
	public static int LoanAmount(Occupation job, int lenderGold) => Math.Min(Math.Max(100, Weekly(job)), lenderGold / 4) / 10 * 10;

	/// <summary>这件事怎么说（不带钱数）。</summary>
	public static string Doing(string npc, string kind, string other = "", string item = "") =>
		Flavor.TryGetValue(npc + "|" + kind, out string? f) ? f : kind switch
		{
			HustleKinds.Forage => Tr.Get("hustle.doing.forage", "去野外挖野菜、捡东西卖"),
			HustleKinds.Scavenge => Tr.Get("hustle.doing.scavenge", "去翻镇上的垃圾桶，找能卖钱的东西"),
			HustleKinds.Busk => Tr.Get("hustle.doing.busk", "晚上在酒吧卖唱挣小费"),
			HustleKinds.OddJob => other.Length > 0 ? Tr.Get("hustle.doing.oddFor", "去给{{who}}打零工", ("who", other)) : Tr.Get("hustle.doing.odd", "到处打零工"),
			HustleKinds.Crafts => Tr.Get("hustle.doing.crafts", "做点手工拿去寄卖"),
			HustleKinds.SellOwned => Tr.Get("hustle.doing.sell", "把家里的{{item}}拿去卖", ("item", item)),
			HustleKinds.Borrow => Tr.Get("hustle.doing.borrow", "找{{who}}借钱", ("who", other)),
			_ => Tr.Get("hustle.doing.tighten", "勒紧裤腰带，不去酒吧、不乱买东西")
		};

	/// <summary>手头紧的人头顶冒的一句。</summary>
	public static string Bubble(string kind) => kind switch
	{
		HustleKinds.Forage => Tr.Get("hustle.bubble.forage", "这些野菜能换几个钱……"),
		HustleKinds.Scavenge => Tr.Get("hustle.bubble.scavenge", "说不定里面有好东西。"),
		HustleKinds.Busk => Tr.Get("hustle.bubble.busk", "♪ 赏两个小钱吧 ♪"),
		HustleKinds.OddJob => Tr.Get("hustle.bubble.odd", "活儿多点才好。"),
		HustleKinds.Crafts => Tr.Get("hustle.bubble.crafts", "希望这个能卖出去。"),
		HustleKinds.SellOwned => Tr.Get("hustle.bubble.sell", "舍不得，也得卖。"),
		HustleKinds.Borrow => Tr.Get("hustle.bubble.borrow", "这个月真是……"),
		_ => Tr.Get("hustle.bubble.tighten", "省着点花吧。")
	};

	/// <summary>旁人会拿来说道的办法。</summary>
	public static bool Gossipy(string kind) => kind is HustleKinds.Scavenge or HustleKinds.Busk or HustleKinds.Borrow or HustleKinds.SellOwned;

	/// <summary>借钱的人有钱了就还：身上的钱够还完还剩半周生活费。</summary>
	public static bool CanRepay(NpcWallet borrower, Occupation job, int amount) => borrower.Gold >= amount + Weekly(job) / 2;

	/// <summary>过期的村民债：过期当天关系紧张一次；过期两周，债主不好意思再要就算了，强要的人把他身上的钱拿走能拿的。</summary>
	public static string Overdue(NpcDebt d, int today, string lenderStyle)
	{
		if (d.State != NpcDebtStates.Open || today <= d.DueDay)
		{
			return "";
		}
		if (today - d.DueDay >= ForgiveAfter)
		{
			return lenderStyle == DebtStyles.Demand ? NpcDebtStates.Collected : NpcDebtStates.Forgiven;
		}
		return d.Strained ? "" : "strain";
	}

	/// <summary>提示词里的"手头紧"一段。</summary>
	public static IEnumerable<string> PromptLines(NpcWallet w, IEnumerable<NpcDebt> debts, string npc, int today, Func<string, string> name)
	{
		if (w.Hustle is Hustle h && today - h.Day <= 3)
		{
			string when = today == h.Day ? "今天" : today - h.Day == 1 ? "昨天" : $"{today - h.Day} 天前";
			yield return h.Done || h.Kind == HustleKinds.Tighten
				? $"- 你最近手头紧，{when}{(h.Note.Length > 0 ? h.Note : h.Plan)}。"
				: $"- 你最近手头紧，今天打算{h.Plan}。";
		}
		if (Tight(w, today))
		{
			yield return "- 你这阵子在省钱，不去酒吧，也不乱买东西。";
		}
		foreach (NpcDebt d in debts.Where(d => d.State == NpcDebtStates.Open))
		{
			int left = d.DueDay - today;
			string due = left > 0 ? $"{left} 天后到期" : left == 0 ? "今天到期" : $"已经过期 {-left} 天";
			if (string.Equals(d.Borrower, npc, StringComparison.OrdinalIgnoreCase))
			{
				yield return $"- 你向{name(d.Lender)}借了 {d.Amount}g，{due}。";
			}
			else if (string.Equals(d.Lender, npc, StringComparison.OrdinalIgnoreCase))
			{
				yield return $"- {name(d.Borrower)}向你借了 {d.Amount}g，{due}。";
			}
		}
	}
}
