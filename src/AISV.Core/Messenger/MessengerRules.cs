using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;

namespace AISV.Core.Messenger;

/// <summary>信使记录里的一条（从对话记录读出来）。</summary>
public sealed record TextEntry(int Day, string Season, int Year, string ContextType, string PlayerName, bool IsRemote, string PlayerMessage, string NpcReply)
{
	public static TextEntry From(ConversationEntry e) => new TextEntry(e.Day, e.Season ?? "", e.Year, e.Setting ?? "", e.Farmer ?? "", e.OffSite, e.FarmerReply ?? "", e.Comeback ?? "");

	public bool SameDay(int day, string season, int year) => Day == day && Year == year && string.Equals(Season, season, StringComparison.OrdinalIgnoreCase);
}

public enum ChatItemKind
{
	Date,
	Player,
	Npc,
	Photo
}

public sealed record ChatItem(ChatItemKind Kind, string Text, int Day, string Season, int Year);

public enum SendBlock
{
	None,
	NoContact,
	Empty,
	TooLong,
	DailyLimitReached,
	Busy,
	AiOff
}

public static class MessengerRules
{
	public const string ContextType = "messenger";

	public const string ContextId = "farm-computer";

	public const int DefaultDailyLimit = 5;

	public const int MaxMessageChars = 200;

	public const int MaxReplyChars = 150;

	public const int TranscriptMessages = 30;

	public const float BesideTiles = 5f;

	public const int LateNightFrom = 2500;

	public const double RevealCharsPerSecond = 45.0;

	/// <summary>等回复的上限：对话那边整个预算（含备用模型）再多等 30 秒。</summary>
	public static TimeSpan ReplyTimeout(int dialogueBudgetSeconds) => TimeSpan.FromSeconds(Math.Max(0, dialogueBudgetSeconds) + 30);

	private static readonly Regex Space = new Regex("\\s+", RegexOptions.Compiled);

	private static readonly Regex PageBreaks = new Regex("#\\$b#", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex Commands = new Regex("#\\$[a-z]\\b#?|\\$(?:[hsluae]|\\d+)\\b|\\{\\{[^}]*\\}\\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>没有手机的村民。</summary>
	public static readonly IReadOnlySet<string> NoPhone = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Linus", "Krobus", "Wizard", "Leo", "Birdie", "Vincent", "Jas" };

	public static bool HasPhone(string? npc) => string.IsNullOrWhiteSpace(npc) || !NoPhone.Contains(npc);

	public static int ClampDailyLimit(int limit) => Math.Clamp(limit, 1, 15);

	public static string Normalize(string? text) => Space.Replace(text ?? "", " ").Trim();

	/// <summary>这位农夫自己的信使记录（没写农夫名字的老记录算本机的）。</summary>
	public static bool IsOwn(TextEntry e, string? player)
	{
		if (!string.Equals(e.ContextType, ContextType, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(e.PlayerName))
		{
			return !e.IsRemote;
		}
		return string.IsNullOrWhiteSpace(player) || string.Equals(e.PlayerName, player, StringComparison.OrdinalIgnoreCase);
	}

	public static int SentToday(IEnumerable<TextEntry> entries, string? player, GameDate today) =>
		entries.Count(e => IsOwn(e, player) && e.SameDay(today.Day, today.Season, today.Year) && Normalize(e.PlayerMessage).Length > 0);

	public static int Remaining(int limit, int sentToday, bool pending) => Math.Max(0, ClampDailyLimit(limit) - Math.Max(0, sentToday) - (pending ? 1 : 0));

	public static SendBlock Check(bool hasContact, string? text, int remaining, bool busy, bool aiAvailable)
	{
		if (!hasContact)
		{
			return SendBlock.NoContact;
		}
		if (!aiAvailable)
		{
			return SendBlock.AiOff;
		}
		string clean = Normalize(text);
		if (clean.Length == 0)
		{
			return SendBlock.Empty;
		}
		if (clean.Length > MaxMessageChars)
		{
			return SendBlock.TooLong;
		}
		if (busy)
		{
			return SendBlock.Busy;
		}
		return remaining <= 0 ? SendBlock.DailyLimitReached : SendBlock.None;
	}

	/// <summary>放在"你刚说的话"位置的一行：说明这是信使上的文字聊天，带上上一条回复。</summary>
	public static string NpcLine(string? lastReply, bool onPhone)
	{
		string device = onPhone ? "农夫的手机" : "农场的电脑";
		string last = Normalize(lastReply);
		return last.Length == 0 ? $"[信使消息，从{device}发来，你们没有见面。]" : $"[信使消息，从{device}发来，你们没有见面。你上一条回的是：] {last}";
	}

	public static bool IsBeside(bool samePlace, float distanceTiles) => samePlace && distanceTiles >= 0 && distanceTiles <= BesideTiles;

	public static string BesideHint(string? farmer)
	{
		string who = string.IsNullOrWhiteSpace(farmer) ? "农夫" : farmer.Trim();
		return $"[{who}人就在你几步开外，却一声不吭地给你发消息。你可能觉得好笑、摸不着头脑、想逗逗对方，或者干脆抬起头当面回。]";
	}

	public static string DiaryMessage(string? text)
	{
		string t = Normalize(text);
		return t.Length == 0 ? "" : "（用信使发的）" + t;
	}

	/// <summary>去掉对话代码，保留换行。</summary>
	public static string CleanReply(string? reply)
	{
		string text = Commands.Replace(PageBreaks.Replace(reply ?? "", "\n"), " ");
		return string.Join("\n", text.Replace("\r", "").Split('\n').Select(l => Space.Replace(l, " ").Trim()).Where(l => l.Length > 0));
	}

	public static string LastReply(IEnumerable<TextEntry> entries, string? player) =>
		entries.LastOrDefault(e => IsOwn(e, player) && CleanReply(e.NpcReply).Length > 0) is TextEntry e ? CleanReply(e.NpcReply) : "";

	public static bool TextedFirst(TextEntry e) => Normalize(e.PlayerMessage).Length == 0 && CleanReply(e.NpcReply).Length > 0;

	/// <summary>聊天页的内容：换天插日期，农夫的话、村民的回复，回复后面跟照片。</summary>
	public static IReadOnlyList<ChatItem> Transcript(IEnumerable<TextEntry> entries, string? player, IEnumerable<PhotoRecord>? photos = null, int max = TranscriptMessages)
	{
		List<TextEntry> own = entries.Where(e => IsOwn(e, player)).TakeLast(Math.Max(0, max)).ToList();
		Dictionary<TextEntry, PhotoRecord> matched = photos == null ? new Dictionary<TextEntry, PhotoRecord>() : PhotoRules.Match(own, photos);
		List<ChatItem> items = new List<ChatItem>();
		TextEntry? last = null;
		foreach (TextEntry e in own)
		{
			if (last == null || !e.SameDay(last.Day, last.Season, last.Year))
			{
				items.Add(new ChatItem(ChatItemKind.Date, "", e.Day, e.Season, e.Year));
			}
			string said = Normalize(e.PlayerMessage);
			if (said.Length > 0)
			{
				items.Add(new ChatItem(ChatItemKind.Player, said, e.Day, e.Season, e.Year));
			}
			string reply = CleanReply(e.NpcReply);
			if (reply.Length > 0)
			{
				items.Add(new ChatItem(ChatItemKind.Npc, reply, e.Day, e.Season, e.Year));
				if (matched.TryGetValue(e, out PhotoRecord? photo))
				{
					items.Add(new ChatItem(ChatItemKind.Photo, photo.Id, e.Day, e.Season, e.Year));
				}
			}
			last = e;
		}
		return items;
	}

	/// <summary>回复到了以后"正在输入"多久：0.5 秒 + 每字 20 毫秒，最多 3.5 秒。</summary>
	public static TimeSpan TypingDelay(int chars) => TimeSpan.FromMilliseconds(Math.Clamp(500 + Math.Max(0, chars) * 20, 500, 3500));

	public static int RevealedChars(double elapsedMs, int total) => Math.Clamp((int)(Math.Max(0, elapsedMs) * RevealCharsPerSecond / 1000.0) + 1, 0, Math.Max(0, total));

	/// <summary>回复已经记进对话记录、但还在"正在输入"时，先把那条藏起来。</summary>
	public static IReadOnlyList<TextEntry> WithoutHeld(IReadOnlyList<TextEntry> entries, string? player, string held)
	{
		string text = Normalize(held);
		for (int i = entries.Count - 1; i >= 0; i--)
		{
			if (IsOwn(entries[i], player) && Normalize(entries[i].PlayerMessage) == text)
			{
				int skip = i;
				return entries.Where((_, index) => index != skip).ToList();
			}
		}
		return entries;
	}

	public static bool IsLateNight(int time) => time >= LateNightFrom;

	public static string ClockText(int time)
	{
		int hour = time / 100 % 24;
		return $"{(time >= 2400 ? "凌晨" : "")}{hour}:{time % 100:00}";
	}

	public static string Preview(string? text, int max)
	{
		string t = Normalize(text);
		if (max <= 0)
		{
			return "";
		}
		return t.Length <= max ? t : t.Substring(0, Math.Max(0, max - 1)).TrimEnd() + "…";
	}

	/// <summary>联系人排序：生日的人在前；"聊天"标签只留聊过的人，按最近聊天时间排。</summary>
	public static List<int> TabMembers(IReadOnlyList<int?> lastChatDays, bool chatsOnly, IReadOnlyList<bool>? birthdays = null)
	{
		bool Birthday(int i) => birthdays != null && i < birthdays.Count && birthdays[i];
		IEnumerable<int> all = Enumerable.Range(0, lastChatDays.Count);
		if (!chatsOnly)
		{
			return all.OrderByDescending(Birthday).ThenBy(i => i).ToList();
		}
		return all.Where(i => lastChatDays[i].HasValue || Birthday(i)).OrderByDescending(Birthday).ThenByDescending(i => lastChatDays[i] ?? int.MinValue).ThenBy(i => i).ToList();
	}

	/// <summary>搜索词按空格拆开，去掉大小写和重音；每个词都要在名字、内部名或最后一句里出现。</summary>
	public static IReadOnlyList<string> SearchTokens(string? query) =>
		(query ?? "").Split(new[] { ' ', '\u3000' }, StringSplitOptions.RemoveEmptyEntries).Select(PhotoRules.Fold).Distinct().ToList();

	public static bool MatchesSearch(IReadOnlyList<string> tokens, params string?[] fields)
	{
		if (tokens.Count == 0)
		{
			return true;
		}
		List<string> folded = fields.Select(f => PhotoRules.Fold(f ?? "")).ToList();
		return tokens.All(t => folded.Any(f => f.Contains(t, StringComparison.Ordinal)));
	}

	/// <summary>一行字里搜索词出现的位置（给高亮用），按起点排、不重叠。</summary>
	public static IReadOnlyList<(int Start, int Length)> SearchSpans(string? text, IReadOnlyList<string> tokens)
	{
		string folded = PhotoRules.Fold(text ?? "");
		List<(int Start, int Length)> spans = new List<(int, int)>();
		if (folded.Length != (text ?? "").Length)
		{
			return spans;
		}
		foreach (string t in tokens)
		{
			for (int i = folded.IndexOf(t, StringComparison.Ordinal); i >= 0 && t.Length > 0; i = folded.IndexOf(t, i + t.Length, StringComparison.Ordinal))
			{
				spans.Add((i, t.Length));
			}
		}
		List<(int Start, int Length)> merged = new List<(int, int)>();
		foreach ((int start, int length) in spans.OrderBy(s => s.Start))
		{
			if (merged.Count > 0 && start <= merged[^1].Start + merged[^1].Length)
			{
				(int ps, int pl) = merged[^1];
				merged[^1] = (ps, Math.Max(pl, start + length - ps));
			}
			else
			{
				merged.Add((start, length));
			}
		}
		return merged;
	}
}
