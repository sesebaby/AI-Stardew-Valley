using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Life;

/// <summary>一次改写：这位村民在这个存档里现在是什么样。</summary>
public sealed class VillagerGrowthEntry
{
	public int Day { get; set; }

	/// <summary>现在的他（AI 写的一段）。</summary>
	public string Now { get; set; } = "";

	/// <summary>说话上的变化，没有为空。</summary>
	public string Voice { get; set; } = "";

	/// <summary>因为什么变（角色卡页显示）。</summary>
	public string Why { get; set; } = "";
}

public sealed class VillagerGrowth
{
	public string Npc { get; set; } = "";

	/// <summary>上次改写时的经历指纹（<see cref="VillagerGrowthRules.Mark"/>）。</summary>
	public string Mark { get; set; } = "";

	public List<VillagerGrowthEntry> History { get; set; } = new List<VillagerGrowthEntry>();

	public VillagerGrowthEntry? Current => History.LastOrDefault();
}

/// <summary>村民的成长（town/villager_growth.json，房主写，联机发给农场帮手）。</summary>
public sealed class VillagerGrowthBook
{
	public const string Path = "town/villager_growth.json";

	public static readonly DocumentType<VillagerGrowthBook> Doc = new DocumentType<VillagerGrowthBook>("村民的成长", 1, () => new VillagerGrowthBook());

	public List<VillagerGrowth> Villagers { get; set; } = new List<VillagerGrowth>();

	public VillagerGrowth? Find(string npc) => Villagers.FirstOrDefault(v => string.Equals(v.Npc, npc, StringComparison.OrdinalIgnoreCase));

	public VillagerGrowth Get(string npc)
	{
		if (Find(npc) is VillagerGrowth g)
		{
			return g;
		}
		VillagerGrowth fresh = new VillagerGrowth { Npc = npc };
		Villagers.Add(fresh);
		return fresh;
	}
}

/// <summary>改写一位村民要看的经历：和农夫们的交情、自己的心愿、感情、工作、性格转折。</summary>
public sealed record VillagerGrowthFacts(string Npc, int Hearts, string SpouseOf, int GoalsDone, string Couple, string Job, int TurningPoints, int Year);

/// <summary>
/// 村民什么时候、怎么"长"：经历攒到一定程度（交情上了一档、做成心愿、交往结婚、换工作、性格有了转折、过了一年）就请 AI 重写一段"现在的他"。
/// 底色不变——脾气冲的还是冲、冷的还是冷，只是在某些事上松一点或紧一点；不往老好人那边写。
/// </summary>
public static class VillagerGrowthRules
{
	/// <summary>小变化（交情、转折、年份）至少隔这么多天才再写。</summary>
	public const int MinGapDays = 14;

	/// <summary>每天最多改写几位（AI 额度）。</summary>
	public const int PerDay = 2;

	public const int KeepHistory = 4;

	public const int NowMaxChars = 340;

	public const int VoiceMaxChars = 120;

	public static int HeartTier(int hearts) => hearts >= 10 ? 4 : hearts >= 8 ? 3 : hearts >= 5 ? 2 : hearts >= 3 ? 1 : 0;

	public static string Mark(VillagerGrowthFacts f) =>
		$"h{HeartTier(f.Hearts)}|s{f.SpouseOf}|g{f.GoalsDone}|c{f.Couple}|j{f.Job}|t{Math.Min(f.TurningPoints, 6) / 2}|y{Math.Min(f.Year, 4)}";

	/// <summary>什么都还没经历（刚认识、第一年）：不用写。</summary>
	public static bool Untouched(VillagerGrowthFacts f) => HeartTier(f.Hearts) == 0 && f.SpouseOf.Length == 0 && f.GoalsDone == 0 && f.Couple.Length == 0 && f.Job.Length == 0 && f.TurningPoints < 2 && f.Year <= 1;

	public static bool Due(VillagerGrowth? g, VillagerGrowthFacts f, int today)
	{
		string mark = Mark(f);
		if (g?.Current == null)
		{
			return !Untouched(f);
		}
		if (mark == g.Mark)
		{
			return false;
		}
		return Major(g.Mark, mark) || today - g.Current.Day >= MinGapDays;
	}

	/// <summary>结婚、做成心愿、交往/分手、换工作：不用等，第二天就写。</summary>
	public static bool Major(string before, string after)
	{
		Dictionary<char, string> a = Parts(before), b = Parts(after);
		return "sgcj".Any(k => a.GetValueOrDefault(k, "") != b.GetValueOrDefault(k, ""));
	}

	private static Dictionary<char, string> Parts(string mark) => mark.Split('|', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 0).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First().Substring(1));

	public const string System = "你在帮一个农场模拟游戏更新一位村民的设定：照他原本的角色卡和在这个存档里经历的事，写他现在是什么样。\n"
		+ "规矩：\n"
		+ "- 底色不变。原本脾气冲的还是冲，嘴硬的还是嘴硬，冷淡的还是冷淡，怪的还是怪，爱抱怨的照样抱怨。经历只会让他在某几件事、某几个人面前松一点或更紧一点，不会变成另一个人，更不能写成对谁都温柔体贴的老好人。\n"
		+ "- 变化要具体、能从说话和做事里看出来，比如“提到乐队不再说随便玩玩，会认真讲排练”，不要“变得更成熟了”“更懂得珍惜”这种空话。\n"
		+ "- 变化不一定往好处走：受了打击可以更消沉、更警惕，得意了可以更傲、更爱显摆，被冷落可以更别扭。\n"
		+ "- 跟农夫熟了，是用他自己的方式表现在乎（损两句、塞个东西、嘴上嫌弃却记着），不是突然变得会说暖心话。\n"
		+ "- 只用材料里有的事，不编新的大事；孩子不写恋爱。\n"
		+ "只交一个 JSON 对象，不加别的文字：{\"now\": \"现在的他，150 到 250 字，用第二人称“你”写\", \"voice\": \"说话上有什么变化，一句，没有就空字符串\", \"why\": \"一句话说因为哪些经历变的\"}";

	public static string Ask(string display, string card, VillagerGrowthEntry? last, IEnumerable<string> story, string language)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"村民：{display}");
		sb.AppendLine("原本的角色卡：");
		sb.AppendLine(PromptText.Sanitize(card, 1500));
		if (last != null && last.Now.Length > 0)
		{
			sb.AppendLine("上一次写的“现在的他”（在这个基础上接着变，别推翻）：");
			sb.AppendLine(last.Now + (last.Voice.Length > 0 ? " 说话上：" + last.Voice : ""));
		}
		sb.AppendLine("在这个存档里经历的事：");
		foreach (string line in story.Where(l => !string.IsNullOrWhiteSpace(l)))
		{
			sb.AppendLine("- " + PromptText.Sanitize(line, 300).Replace('\n', ' '));
		}
		sb.Append($"用{language}写。");
		return sb.ToString();
	}

	public static VillagerGrowthEntry? Parse(string? text, int today)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(text));
			JsonElement root = doc.RootElement;
			string now = Str(root, "now");
			if (now.Length < 20)
			{
				return null;
			}
			return new VillagerGrowthEntry
			{
				Day = today,
				Now = TextTools.Truncate(now, NowMaxChars),
				Voice = TextTools.Truncate(Str(root, "voice"), VoiceMaxChars),
				Why = TextTools.Truncate(Str(root, "why"), 120)
			};
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException)
		{
			return null;
		}
	}

	private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

	public static void Add(VillagerGrowth g, VillagerGrowthEntry e, string mark)
	{
		g.History.Add(e);
		g.Mark = mark;
		if (g.History.Count > KeepHistory)
		{
			g.History.RemoveRange(0, g.History.Count - KeepHistory);
		}
	}

	/// <summary>接在人设后面的一段；玩家写了说话方式时不带说话上的变化。</summary>
	public static string PersonaTail(VillagerGrowthEntry? e, bool cardSpeech)
	{
		if (e == null || e.Now.Length == 0)
		{
			return "";
		}
		string voice = !cardSpeech && e.Voice.Length > 0 ? " 说话上：" + e.Voice.TrimEnd('。') + "。" : "";
		return " 这个存档里的变化（底色还是上面那个人）：" + e.Now.TrimEnd('。') + "。" + voice;
	}
}
