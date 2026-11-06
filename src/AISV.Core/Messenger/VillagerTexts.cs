using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Messenger;

public enum TextKind
{
	SmallTalk,
	RandomQuestion,
	Gossip,
	Flirt,
	Callback,
	CheckIn,
	Selfie,
	Birthday,
	SeenAndHeard
}

public sealed record TextCandidate(string NpcName, int Hearts, int? LastTextedFirstDay, bool Nearby, bool Busy);

public sealed class PlannedBirthdayText
{
	public string NpcName { get; set; } = "";

	public string Text { get; set; } = "";

	public int Time { get; set; }
}

public sealed class VillagerTextState
{
	public int BirthdayPlannedOn { get; set; } = -1;

	public List<PlannedBirthdayText> BirthdaySlots { get; set; } = new List<PlannedBirthdayText>();

	public Dictionary<string, int> LatestOpener { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public int PlannedOn { get; set; } = -1;

	public int SentCount { get; set; }

	public List<int> Slots { get; set; } = new List<int>();

	public int Week { get; set; } = -1;
}

/// <summary>村民主动发消息的规则：每周几条、今天几点、谁发、发什么。</summary>
public static class VillagerTexts
{
	public const string Path = "mail/villager_texts.json";

	public const int DefaultPerWeek = 3;

	public const int MinHearts = 2;

	public const int CooldownDays = 3;

	public const int FlirtHearts = 7;

	public const int FirstTime = 900;

	public const int LastTime = 2000;

	public const int MaxChars = 200;

	public const float NearbyTiles = 12f;

	public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(6);

	public static readonly DocumentType<VillagerTextState> Doc = new DocumentType<VillagerTextState>("村民主动发的消息", 1, () => new VillagerTextState());

	public static int ClampPerWeek(int perWeek) => Math.Clamp(perWeek, 0, 10);

	public static int WeekOf(int totalDays) => (Math.Max(1, totalDays) - 1) / 7;

	public static int DaysLeftInWeek(int totalDays) => 7 - (Math.Max(1, totalDays) - 1) % 7;

	/// <summary>这周还剩的条数平均摊到剩下的天，余数按概率落到今天。</summary>
	public static int PlanToday(int perWeek, int sentThisWeek, int daysLeftInWeek, double roll)
	{
		int left = ClampPerWeek(perWeek) - Math.Max(0, sentThisWeek);
		if (left <= 0)
		{
			return 0;
		}
		int days = Math.Max(1, daysLeftInWeek);
		return left / days + (roll < (double)(left % days) / days ? 1 : 0);
	}

	/// <summary>9:00～20:00 平均分成几段，每段里随机一个时间（取整到 10 分钟）。</summary>
	public static IReadOnlyList<int> PlanTimes(int count, IReadOnlyList<double> rolls)
	{
		if (count <= 0)
		{
			return Array.Empty<int>();
		}
		int start = Minutes(FirstTime);
		double slot = (double)(Minutes(LastTime) - start) / count;
		List<int> times = new List<int>(count);
		for (int i = 0; i < count; i++)
		{
			double roll = i < rolls.Count ? Math.Clamp(rolls[i], 0.0, 0.999) : 0.5;
			int m = start + (int)(slot * i + slot * roll);
			times.Add(FromMinutes(m / 10 * 10));
		}
		return times;
	}

	public static int Minutes(int time) => time / 100 * 60 + time % 100;

	public static int FromMinutes(int minutes) => minutes / 60 * 100 + minutes % 60;

	/// <summary>2 心以上、不在农夫身边、不忙、3 天内没主动发过的人里，按心数加权挑一位。</summary>
	public static TextCandidate? PickSender(IEnumerable<TextCandidate> candidates, int today, double roll)
	{
		List<TextCandidate> list = candidates
			.Where(c => !string.IsNullOrWhiteSpace(c.NpcName) && c.Hearts >= MinHearts && !c.Nearby && !c.Busy)
			.Where(c => c.LastTextedFirstDay is not int last || today - last >= CooldownDays)
			.OrderBy(c => c.NpcName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return list.Count == 0 ? null : list[PickWeighted(list.Select(c => (double)c.Hearts).ToList(), roll)];
	}

	public static bool CanFlirt(bool partner, bool datable, int hearts, bool farmerMarriedElsewhere) => partner || (datable && hearts >= FlirtHearts && !farmerMarriedElsewhere);

	public static IReadOnlyList<(TextKind Kind, int Weight)> Kinds(bool hasGossip, bool canFlirt, bool hasHistory, bool canSelfie = false)
	{
		List<(TextKind, int)> kinds = new List<(TextKind, int)> { (TextKind.SmallTalk, 40), (TextKind.RandomQuestion, 22) };
		void AddWhen(bool ready, TextKind kind, int weight)
		{
			if (ready)
			{
				kinds.Add((kind, weight));
			}
		}
		AddWhen(hasGossip, TextKind.Gossip, 28);
		AddWhen(canFlirt, TextKind.Flirt, 24);
		AddWhen(hasHistory, TextKind.Callback, 10);
		AddWhen(canSelfie, TextKind.Selfie, 8);
		kinds.Add((TextKind.CheckIn, 12));
		return kinds;
	}

	public static TextKind PickKind(IReadOnlyList<(TextKind Kind, int Weight)> kinds, double roll)
 {
		return kinds.Count == 0 ? TextKind.SmallTalk : kinds[PickWeighted(kinds.Select(k => (double)k.Weight).ToList(), roll)].Kind;
	}

	public static string KindKey(TextKind kind) => kind switch
	{
		TextKind.SmallTalk => "small_talk",
		TextKind.RandomQuestion => "random_question",
		TextKind.Gossip => "gossip",
		TextKind.Flirt => "flirt",
		TextKind.Callback => "callback",
		TextKind.Selfie => "selfie",
		TextKind.Birthday => "birthday",
		TextKind.SeenAndHeard => "seen_and_heard",
		_ => "check_in"
	};

	/// <summary>记进对话记录、放在"你说的话"位置的标记。</summary>
	public static string TextedFirstLine(TextKind kind, int time) => $"[你在信使上先开了口：{KindLabel(kind)}，{time / 100:00}:{time % 100:00}。]";

	private static string KindLabel(TextKind kind) => kind switch
	{
		TextKind.SmallTalk => "闲扯几句",
		TextKind.RandomQuestion => "冷不丁问了个问题",
		TextKind.Gossip => "传了条听来的事",
		TextKind.Flirt => "撩了对方一下",
		TextKind.Callback => "提起上回聊过的话",
		TextKind.Selfie => "发了张自拍",
		TextKind.Birthday => "那天是农夫生日，发了句祝福",
		TextKind.SeenAndHeard => "没找着农夫，就把看到的事发消息说了",
		_ => "问候一声"
	};

	public static string Relationship(int hearts, bool married, bool roommate, bool engaged, bool dating)
	{
		int h = Math.Max(0, hearts);
		if (married)
		{
			return $"你们是夫妻（{h} 心），住在同一个屋檐下。";
		}
		if (roommate)
		{
			return $"你是农夫的室友（{h} 心），像老朋友一样同住在农场。";
		}
		if (engaged)
		{
			return $"你们已经订婚（{h} 心），离婚礼不远了。";
		}
		if (dating)
		{
			return $"你们正在交往（{h} 心）。";
		}
		string level = h <= 2 ? "才认识没多久" : h <= 4 ? "见面会打个招呼" : h <= 6 ? "算得上朋友" : h <= 8 ? "很要好" : "几乎无话不谈";
		return $"交情 {h} 心（满分 10），{level}。";
	}

	/// <summary>最近几条信使记录（最旧的在前）。</summary>
	public static string FormatChat(IEnumerable<TextEntry> entries, string? player, int max = 6)
	{
		List<string> lines = new List<string>();
		foreach (TextEntry e in entries.Where(e => MessengerRules.IsOwn(e, player)).TakeLast(Math.Max(0, max)))
		{
			string said = MessengerRules.Normalize(e.PlayerMessage);
			string reply = MessengerRules.Normalize(MessengerRules.CleanReply(e.NpcReply));
			string when = $"[第 {e.Year} 年{HistoryFormatter.SeasonName(e.Season)} {e.Day} 日]";
			if (said.Length > 0)
			{
				lines.Add($"{when} 农夫：{MessengerRules.Preview(said, 200)}");
			}
			if (reply.Length > 0)
			{
				lines.Add($"{when} 你{(said.Length == 0 ? "（主动发的）" : "")}：{MessengerRules.Preview(reply, 200)}");
			}
		}
		return string.Join("\n", lines);
	}

	/// <summary>以前（不是今天）农夫说过、你也回过的最后一条。</summary>
	public static TextEntry? PickCallback(IEnumerable<TextEntry> entries, string? player, int day, string season, int year) =>
		entries.LastOrDefault(e => IsThisFarmers(e, player) && MessengerRules.Normalize(e.PlayerMessage).Length > 0 && MessengerRules.CleanReply(e.NpcReply).Length > 0 && !e.SameDay(day, season, year));

	public static string DescribeCallback(TextEntry e)
	{
		string how = string.Equals(e.ContextType, MessengerRules.ContextType, StringComparison.OrdinalIgnoreCase) ? "在信使上" : "当面";
		return $"第 {e.Year} 年{HistoryFormatter.SeasonName(e.Season)} {e.Day} 日那次，农夫{how}跟你说「{MessengerRules.Preview(e.PlayerMessage, 200)}」，你回的是「{MessengerRules.Preview(MessengerRules.CleanReply(e.NpcReply), 200)}」";
	}

	public static string CleanText(string? text, string? npc = null, string? display = null)
	{
		string t = StripQuotes(MessengerRules.CleanReply(text));
		foreach (string name in new[] { npc, display }.Where(n => !string.IsNullOrWhiteSpace(n)))
		{
			foreach (string sep in new[] { ":", "：" })
			{
				string prefix = name!.Trim() + sep;
				if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					t = t.Substring(prefix.Length).TrimStart();
				}
			}
		}
		t = StripQuotes(t);
		if (t.Length <= MaxChars)
		{
			return t;
		}
		string cut = PromptText.Truncate(t, MaxChars);
		int space = cut.LastIndexOf(' ');
		if (space > MaxChars / 2)
		{
			cut = cut.Substring(0, space);
		}
		return cut.TrimEnd(' ', ',', ';', ':', '，') + "…";
	}

	private static bool IsThisFarmers(TextEntry e, string? player)
	{
		if (string.IsNullOrWhiteSpace(e.PlayerName))
		{
			return !e.IsRemote;
		}
		return string.IsNullOrWhiteSpace(player) || string.Equals(e.PlayerName, player, StringComparison.OrdinalIgnoreCase);
	}

	private static string StripQuotes(string text)
	{
		string t = text.Trim();
		while (t.Length >= 2 && IsQuote(t[0]) && IsQuote(t[^1]))
		{
			t = t.Substring(1, t.Length - 2).Trim();
		}
		return t;
	}

	private static bool IsQuote(char c) => c is '"' or '\'' or '«' or '»' or '“' or '”' or '「' or '」';

	private static int PickWeighted(IReadOnlyList<double> weights, double roll)
	{
		double total = weights.Sum(w => Math.Max(0, w));
		if (total <= 0)
		{
			return 0;
		}
		double r = Math.Clamp(roll, 0.0, 0.999999) * total;
		for (int i = 0; i < weights.Count; i++)
		{
			r -= Math.Max(0, weights[i]);
			if (r < 0)
			{
				return i;
			}
		}
		return weights.Count - 1;
	}
}
