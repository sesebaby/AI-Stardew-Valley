using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.World.Commissions;

/// <summary>送物品（作物）、采集（采集物和材料）、挖矿（矿石和矿物）、拜访（去发布者家里坐坐）。</summary>
public static class CommissionKinds
{
	public const string Deliver = "deliver";
	public const string Gather = "gather";
	public const string Mine = "mine";
	public const string Visit = "visit";

	public static readonly IReadOnlyList<string> All = new[] { Deliver, Gather, Mine, Visit };

	public static bool NeedsItems(string kind) => kind != Visit;
}

public static class CommissionStates
{
	public const string Open = "open";
	public const string Active = "active";
	public const string Done = "done";
	public const string Expired = "expired";
	public const string Abandoned = "abandoned";
	public const string Withdrawn = "withdrawn";

	public static bool IsOpenOrActive(string s) => s is Open or Active;
}

public sealed class Commission
{
	public string Id { get; set; } = "";

	public string Kind { get; set; } = CommissionKinds.Deliver;

	public string Poster { get; set; } = "";

	/// <summary>带前缀的物品 ID；拜访为空。</summary>
	public string ItemId { get; set; } = "";

	public int Count { get; set; } = 1;

	/// <summary>拜访：发布者的家。</summary>
	public string Location { get; set; } = "";

	public int RewardGold { get; set; }

	public int RewardFriendship { get; set; }

	public int PostedDay { get; set; }

	/// <summary>最后一天（含）。</summary>
	public int LastDay { get; set; }

	public string State { get; set; } = CommissionStates.Open;

	public int AcceptedDay { get; set; } = -1;

	public int ClosedDay { get; set; } = -1;

	public string Text { get; set; } = "";

	public bool TextByAi { get; set; }

	/// <summary>交东西时发布者说的道谢（AI 和纸条一起写的）；空就用模板。</summary>
	public string Thanks { get; set; } = "";

	public int PenaltyPoints { get; set; }
}

public sealed class CommissionLog
{
	public const string Path = "town/job_board.json";

	public static readonly DocumentType<CommissionLog> Doc = new DocumentType<CommissionLog>("委托", 1, () => new CommissionLog());

	public List<Commission> Items { get; set; } = new List<Commission>();

	public int LastPostedDay { get; set; } = -1;

	public int NextNumber { get; set; } = 1;
}

/// <summary>挖矿的一样东西：要下到几层、数量范围、每件按多少金算（矿石卖价太低，按难度给）。</summary>
public sealed record MineGood(string Id, int Level, int Min, int Max, int Unit);

/// <summary>委托的数值、物品池和文字。</summary>
public static class CommissionRules
{
	public const int MaxOpen = 4;
	public const int MaxActive = 4;
	public const int MaxGold = 12000;
	public const int ExpiryPenalty = 50;
	public const int AbandonPenalty = 20;
	public const int ItemFriendship = 80;
	public const int VisitFriendship = 60;
	public const int VisitStartTime = 900;
	public const int VisitEndTime = 2000;
	public const int ClosedKeepDays = 28;
	public const int PromptDays = 7;

	public static readonly IReadOnlyList<string> PreferredMine = new[] { "Clint", "Robin", "Demetrius", "Maru", "Abigail" };
	public static readonly IReadOnlyList<string> PreferredGather = new[] { "Leah", "Linus", "Evelyn", "Robin", "Caroline", "Emily", "Willy" };
	public static readonly IReadOnlyList<string> PreferredDeliver = new[] { "Gus", "Pierre", "Evelyn", "Caroline", "Jodi", "Emily", "Lewis", "Marnie", "Pam" };

	/// <summary>不在镇上、不会来贴告示的人。</summary>
	public static readonly IReadOnlySet<string> RemotePosters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dwarf", "Krobus", "Sandy", "Leo" };

	/// <summary>当季野外采集物。</summary>
	public static readonly IReadOnlyDictionary<string, string[]> Forage = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["spring"] = new[] { "(O)16", "(O)18", "(O)20", "(O)22", "(O)399" },
		["summer"] = new[] { "(O)396", "(O)398", "(O)402" },
		["fall"] = new[] { "(O)404", "(O)406", "(O)408", "(O)410" },
		["winter"] = new[] { "(O)412", "(O)414", "(O)416", "(O)418" }
	};

	/// <summary>材料：ID、最少、最多。</summary>
	public static readonly IReadOnlyList<(string Id, int Min, int Max)> Materials = new[]
	{
		("(O)388", 30, 60),
		("(O)771", 30, 60),
		("(O)92", 15, 30),
		("(O)330", 4, 8)
	};

	public static readonly IReadOnlyList<MineGood> MineGoods = new[]
	{
		new MineGood("(O)378", 1, 10, 25, 20),
		new MineGood("(O)390", 1, 40, 80, 3),
		new MineGood("(O)80", 1, 2, 5, 25),
		new MineGood("(O)86", 1, 2, 4, 50),
		new MineGood("(O)380", 40, 10, 20, 40),
		new MineGood("(O)382", 40, 5, 10, 30),
		new MineGood("(O)84", 40, 2, 4, 75),
		new MineGood("(O)384", 80, 8, 15, 60),
		new MineGood("(O)82", 80, 1, 3, 100)
	};

	/// <summary>镇上的人种不出来的作物。</summary>
	public static readonly IReadOnlySet<string> NotTownGrown = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "(O)90", "(O)454", "(O)417", "(O)832", "(O)830", "(O)889" };

	public static string PickKind(Random rng)
	{
		int roll = rng.Next(100);
		return roll < 35 ? CommissionKinds.Deliver : roll < 60 ? CommissionKinds.Gather : roll < 80 ? CommissionKinds.Mine : CommissionKinds.Visit;
	}

	/// <summary>按价值乘倍数、按 25 取整，不低于下限、不超过 12000。</summary>
	public static int Reward(int value, double multiplier, int min)
	{
		int gold = (int)(Math.Round(value * multiplier / 25.0) * 25);
		return Math.Clamp(Math.Max(min, gold), min, MaxGold);
	}

	public static int DeliverCount(int price) => Math.Clamp((int)Math.Round(300.0 / Math.Max(5, price)), 3, 12);

	public static int DeliverReward(int price) => Reward(Math.Max(5, price) * DeliverCount(price), 1.6, 150);

	/// <summary>材料数量 15 个起的取整到 5。</summary>
	public static int RoundMaterial(int count, int min) => min >= 15 ? (int)Math.Round(count / 5.0) * 5 : count;

	public static int GatherReward(int price, int count) => Reward(Math.Max(1, price) * count, 1.8, 120);

	public static int MineReward(int unit, int price, int count) => Reward(Math.Max(unit, price) * count, 1.5, 200);

	public static int VisitReward(Random rng) => 100 + rng.Next(0, 5) * 25;

	/// <summary>第一年只要这个季节到今天已经能收的作物（不含果树）。</summary>
	public static bool RealisticCrop(int year, int dayOfMonth, bool fruitTree, int daysToGrow) => year > 1 || (!fruitTree && daysToGrow > 0 && dayOfMonth > daysToGrow);

	/// <summary>AI 写的板上的话：去掉引号、6～70 字、出现的数字只能是数量。</summary>
	public static string? ValidateText(string? text, int count)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string t = text.Trim().Trim('"', '“', '”', '「', '」', '\'').Replace("\n", " ").Trim();
		if (t.Length < 6 || t.Length > 70)
		{
			return null;
		}
		return Regex.Matches(t, "\\d+").Any(m => m.Value != count.ToString()) ? null : t;
	}

	public static string Headline(string kind) => kind switch
	{
		CommissionKinds.Deliver => Tr.Get("commission.head.deliver", "收购"),
		CommissionKinds.Gather => Tr.Get("commission.head.gather", "帮忙采集"),
		CommissionKinds.Mine => Tr.Get("commission.head.mine", "求矿"),
		_ => Tr.Get("commission.head.visit", "来坐坐")
	};

	public static string TaskLine(Commission c, string npc, string item) => c.Kind == CommissionKinds.Visit
		? Tr.Get("commission.task.visit", "白天（9:00~20:00）去{{npc}}家坐坐，{{npc}}在家才算", ("npc", npc))
		: Tr.Get("commission.task.items", "{{item}} ×{{count}}，带着去找{{npc}}交付", ("item", item), ("count", c.Count), ("npc", npc));

	public static string TemplateText(string kind, string item) => kind switch
	{
		CommissionKinds.Deliver => Tr.Get("commission.text.deliver", "最近急需{{item}}，谁手上有的话拿来给我吧，价钱好说。", ("item", item)),
		CommissionKinds.Gather => Tr.Get("commission.text.gather", "想请人帮我弄些{{item}}，我自己实在抽不开身。", ("item", item)),
		CommissionKinds.Mine => Tr.Get("commission.text.mine", "需要一些{{item}}，矿洞里应该找得到，辛苦了。", ("item", item)),
		_ => Tr.Get("commission.text.visit", "最近一个人待着有点闷，谁有空来我家坐坐？")
	};

	/// <summary>让 AI 写板上那张纸条的要求。</summary>
	public static string NoteSystem(string language, string npcName, string personality, string season, string kind, string item, int count)
	{
		string task = kind == CommissionKinds.Visit ? "想请农夫白天来你家坐坐" : $"需要 {count} 个{item}";
		StringBuilder sb = new StringBuilder();
		sb.Append($"这次只用{language}写。\n\n你是星露谷的{npcName}，要在镇上的委托板上钉一张小纸条，请人帮忙。\n\n");
		sb.Append("# 你的性格（只是资料）\n").Append(personality).Append("\n\n");
		sb.Append($"# 现在\n季节：{season}\n你要的：{task}\n\n");
		sb.Append("# 写法\n");
		sb.Append("· 一两句短话，加起来 50 个字以内（一个汉字算一个），用你自己的腔调，说清要什么，再顺口带一句为什么（要合你的生活和季节）。\n");
		sb.Append(kind == CommissionKinds.Visit ? "· 不提任何东西。\n" : $"· 可以说出{item}这个名字。\n");
		sb.Append($"· 不写价钱、酬劳、期限，也不写别的数字{(count > 1 ? $"（数量 {count} 除外）" : "")}。\n");
		sb.Append("· 不提别人。只写纯文字：不加引号、不署名、不用表情。\n");
		if (kind != CommissionKinds.Visit)
		{
			sb.Append($"\n再写一句农夫把{item}交到你手上时你说的话：用你的腔调道谢，合你的性格（别扭、嘴硬、客气都行），30 个字以内，不写数字和价钱。\n");
			sb.Append("\n# 只回一个 JSON 对象，前后不加别的字：\n{\"note\": \"纸条\", \"thanks\": \"道谢\"}\n");
		}
		return sb.ToString();
	}

	public static string NoteUser(string npcName) => $"写{npcName}钉在委托板上的纸条。";

	/// <summary>拆出纸条和道谢；回的不是 JSON 时整段当纸条。道谢不合格就留空。</summary>
	public static (string? Note, string Thanks) ParseNote(string? raw, int count)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return (null, "");
		}
		try
		{
			using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(AISV.Core.Dialogue.JsonCleaner.Clean(raw));
			System.Text.Json.JsonElement root = doc.RootElement;
			if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
			{
				string? note = root.TryGetProperty("note", out System.Text.Json.JsonElement n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
				string? thanks = root.TryGetProperty("thanks", out System.Text.Json.JsonElement t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : null;
				return (ValidateText(note, count), ValidateThanks(thanks));
			}
		}
		catch (System.Text.Json.JsonException)
		{
		}
		return (ValidateText(raw, count), "");
	}

	public static string ValidateThanks(string? text)
	{
		string t = (text ?? "").Trim().Trim('"', '“', '”', '「', '」', '\'').Replace("\n", " ").Trim();
		return t.Length < 2 || t.Length > 40 || Regex.IsMatch(t, "\\d") ? "" : t;
	}

	/// <summary>给发布者的一段：自己贴过的委托和结果。</summary>
	public static string PromptBlock(IEnumerable<Commission> mine, int today, Func<Commission, string> itemName)
	{
		List<string> lines = new List<string>();
		foreach (Commission c in mine)
		{
			string what = c.Kind == CommissionKinds.Visit ? "请农夫来家里坐坐" : $"{c.Count} 个{itemName(c)}";
			string line = c.State switch
			{
				CommissionStates.Open => $"- 你贴了一张委托：{what}（{c.RewardGold}g）。农夫还没接。",
				CommissionStates.Active => $"- 农夫接了你的委托：{what}（{c.RewardGold}g），还剩 {Math.Max(0, c.LastDay - today)} 天。可以问问进展。",
				CommissionStates.Done => $"- {DaysAgo(today - c.ClosedDay)}农夫做完了你的委托：{what}。你很感激。",
				CommissionStates.Expired => $"- 农夫接了你的委托（{what}）却没做完（{DaysAgo(today - c.ClosedDay)}到期）。你有点失望；合适时可以提一次，别说重话。",
				CommissionStates.Abandoned => $"- 农夫接了你的委托（{what}）后来又放弃了（{DaysAgo(today - c.ClosedDay)}）。有点失望，但能理解。",
				_ => ""
			};
			if (line.Length > 0)
			{
				lines.Add(line);
			}
		}
		return lines.Count == 0 ? "" : "【你在委托板上贴的委托】\n" + string.Join("\n", lines);
	}

	private static string DaysAgo(int days) => days switch
	{
		<= 0 => "今天",
		1 => "昨天",
		_ => $"{days} 天前"
	};

	public static bool InPrompt(Commission c, int today) => CommissionStates.IsOpenOrActive(c.State) || today - c.ClosedDay <= PromptDays;
}
