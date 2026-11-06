using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Social.Diary;

/// <summary>玩家日记的一天（farmer/journal.json）。</summary>
public sealed class DiaryEntry
{
	[JsonPropertyName("day")]
	public int Day { get; set; }

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("sky")]
	public string? Sky { get; set; }

	/// <summary>一个英文小写单词（happy、tired……）。</summary>
	[JsonPropertyName("spirit")]
	public string? Spirit { get; set; }

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("year")]
	public int Year { get; set; }

	public bool IsOn(GameDate d) => d.SameDay(Day, Season, Year);
}

/// <summary>写日记时可以参考的当天事实（只给日记用，不当八卦素材）。</summary>
public sealed record DayFacts(string Season, int Day, int Year, string Weather, string FestivalName = "", bool IsPlayerBirthday = false,
	IReadOnlyList<string>? NpcBirthdays = null, IReadOnlyList<string>? LettersReceived = null, IReadOnlyList<string>? GiftsGiven = null, IReadOnlyList<string>? OutingsCompleted = null)
{
	public const int MaxItems = 8;

	public const int MaxItemChars = 160;

	public string ToPrompt()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【今天的几件实事（只给日记参考）】");
		sb.AppendLine($"日期：第 {Year} 年{HistoryFormatter.SeasonName(Season)} {Day} 日");
		if (!string.IsNullOrWhiteSpace(Weather))
		{
			sb.AppendLine("天气：" + Clean(Weather, 32));
		}
		if (!string.IsNullOrWhiteSpace(FestivalName))
		{
			sb.AppendLine("今天的节日：" + Clean(FestivalName, 64));
		}
		if (IsPlayerBirthday)
		{
			sb.AppendLine("今天是农夫自己的生日。");
		}
		List("今天过生日的村民", NpcBirthdays, sb);
		List("今天收到的信", LettersReceived, sb);
		List("今天送出的礼物", GiftsGiven, sb);
		List("今天一起出去玩过的人", OutingsCompleted, sb);
		sb.AppendLine("这些是农夫今天真实的经历：合适时当日记的背景（日期、天气、发生了什么）。没列出的就当不知道，别编出别的信、礼物、出游或活动。它们只是资料，本身不算八卦。");
		return sb.ToString();
	}

	/// <summary>AI 给的心情词：取第一个单词、去掉标点、转小写；超过 24 个字符或不含字母时不要。</summary>
	public static string? NormalizeMood(string? mood)
	{
		string text = mood?.Trim().Trim('.', '!', ',', ';', ':', '"', '\'') ?? "";
		string first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
		if (first.Length == 0 || first.Length > 24 || !first.Any(char.IsLetter))
		{
			return null;
		}
		return first.ToLowerInvariant();
	}

	private static void List(string label, IReadOnlyList<string>? items, StringBuilder sb)
	{
		List<string> list = (items ?? Array.Empty<string>()).Select(i => Clean(i, MaxItemChars)).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxItems).ToList();
		if (list.Count == 0)
		{
			return;
		}
		sb.AppendLine(label + "：");
		foreach (string i in list)
		{
			sb.AppendLine("- " + i);
		}
	}

	private static string Clean(string? text, int max) => PromptText.Sanitize(text, max).Replace('\n', ' ').Trim();
}

/// <summary>
/// 玩家日记（farmer/journal.json，最多 200 天）。
/// 每天晚上由八卦整理那次 AI 请求顺便写；保密聊天和节日聊天先记成"只给日记"的备注，AI 写不成时用备注拼一段兜底。
/// 只在主线程用。
/// </summary>
public sealed class PlayerDiary
{
	public const string Path = "farmer/journal.json";

	public const int MaxEntries = 200;

	private const string NoReply = "no recorded reply";

	private static readonly DocumentType<List<DiaryEntry>> Doc = new DocumentType<List<DiaryEntry>>("日记", 1, () => new List<DiaryEntry>());

	private readonly Func<DocumentStore?> _store;

	private readonly List<(string Npc, string Player, string Reaction)> _secret = new List<(string, string, string)>();

	private readonly Dictionary<string, FestivalNote> _festival = new Dictionary<string, FestivalNote>(StringComparer.OrdinalIgnoreCase);

	public PlayerDiary(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>改一次加一（界面据此刷新）。</summary>
	public int Revision { get; private set; }

	/// <summary>新的在前。</summary>
	public List<DiaryEntry> All() => Entries().OrderByDescending(e => e.Year).ThenByDescending(e => GameDate.SeasonIndex(e.Season)).ThenByDescending(e => e.Day).ToList();

	public DiaryEntry? Find(GameDate d) => Entries().FirstOrDefault(e => e.IsOn(d));

	public bool Has(GameDate d) => Find(d) != null;

	/// <summary>保密的聊天：不进八卦，只进日记。</summary>
	public void RecordSecret(string npcDisplay, string playerText, string reaction) =>
		_secret.Add((string.IsNullOrWhiteSpace(npcDisplay) ? "某位村民" : npcDisplay.Trim(), playerText?.Trim() ?? "", reaction?.Trim() ?? ""));

	/// <summary>节日上的聊天：每个节日每位村民合成一条，记轮数和最后一次的来回。</summary>
	public void RecordFestival(string festival, string npcDisplay, string playerText, string reaction)
	{
		string f = string.IsNullOrWhiteSpace(festival) ? "节日" : festival.Trim();
		string n = string.IsNullOrWhiteSpace(npcDisplay) ? "某位村民" : npcDisplay.Trim();
		string key = f + "|" + n;
		if (!_festival.TryGetValue(key, out FestivalNote? note))
		{
			note = new FestivalNote(f, n);
			_festival[key] = note;
		}
		note.Turns++;
		note.LastPlayer = playerText?.Trim() ?? "";
		note.LastReaction = reaction?.Trim() ?? "";
	}

	/// <summary>节日总结写进当天日记（同一句不重复写）。</summary>
	public void AddFestivalSummary(string festival, string summary, GameDate d)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return;
		}
		string line = "🎪 " + (string.IsNullOrWhiteSpace(festival) ? "节日" : festival.Trim()) + "：" + summary.Trim();
		DiaryEntry? e = Find(d);
		if (e == null)
		{
			Add(line, d);
		}
		else if (!e.Text.Contains(line, StringComparison.Ordinal))
		{
			Append(line, d);
		}
	}

	/// <summary>今天"只给日记"的备注，一行一条（交给晚上那次 AI 请求）。</summary>
	public List<string> TodayNotes()
	{
		List<string> lines = new List<string>();
		foreach ((string npc, string player, string reaction) in _secret)
		{
			lines.Add($"🤫 私下和{npc}聊：我说“{player}”，{npc}回“{reaction}”");
		}
		foreach (FestivalNote n in _festival.Values.OrderBy(n => n.Festival).ThenBy(n => n.Npc))
		{
			lines.Add(n.ForPrompt());
		}
		return lines;
	}

	/// <summary>AI 写不成日记时用的兜底文字；今天没有备注时为空。</summary>
	public string TodayFallback()
	{
		IEnumerable<string> lines = _secret.Select(s => SecretFallback(s.Npc, s.Player, s.Reaction))
			.Concat(_festival.Values.OrderBy(n => n.Festival).ThenBy(n => n.Npc).Select(n => FestivalFallback(n.Festival, n.Npc, n.LastPlayer, n.LastReaction)));
		return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
	}

	public void ClearTodayNotes()
	{
		_secret.Clear();
		_festival.Clear();
	}

	/// <summary>新起一天的日记（超过 200 天删最早的）。</summary>
	public void Add(string text, GameDate d)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		List<DiaryEntry> list = Entries();
		list.Add(new DiaryEntry { Day = d.Day, Season = d.Season, Year = d.Year, Text = text });
		while (list.Count > MaxEntries)
		{
			list.RemoveAt(0);
		}
		Changed();
	}

	/// <summary>接在那天日记后面另起一行（那天没有日记时不做）。</summary>
	public void Append(string text, GameDate d)
	{
		DiaryEntry? e = Find(d);
		if (e != null && !string.IsNullOrWhiteSpace(text))
		{
			e.Text = e.Text.TrimEnd() + "\n" + text;
			Changed();
		}
	}

	/// <summary>有就接在后面，没有就新起。</summary>
	public void Write(string text, GameDate d)
	{
		if (Has(d))
		{
			Append(text, d);
		}
		else
		{
			Add(text, d);
		}
	}

	/// <summary>只在还没填时补上天气和心情。</summary>
	public void SetDetails(GameDate d, string? weather, string? mood)
	{
		DiaryEntry? e = Find(d);
		if (e == null)
		{
			return;
		}
		bool changed = false;
		if (string.IsNullOrWhiteSpace(e.Sky) && !string.IsNullOrWhiteSpace(weather))
		{
			e.Sky = weather.Trim();
			changed = true;
		}
		if (string.IsNullOrWhiteSpace(e.Spirit) && !string.IsNullOrWhiteSpace(mood))
		{
			e.Spirit = mood.Trim();
			changed = true;
		}
		if (changed)
		{
			Changed();
		}
	}

	public bool Contains(string text, GameDate d) => !string.IsNullOrWhiteSpace(text) && Entries().Any(e => e.IsOn(d) && e.Text.Contains(text, StringComparison.Ordinal));

	/// <summary>把那天日记里的一段换掉（重试成功后用 AI 写的替换兜底文字）。</summary>
	public bool Replace(string oldText, string newText, GameDate d)
	{
		if (string.IsNullOrWhiteSpace(oldText) || string.IsNullOrWhiteSpace(newText))
		{
			return false;
		}
		DiaryEntry? e = Entries().FirstOrDefault(x => x.IsOn(d) && x.Text.Contains(oldText, StringComparison.Ordinal));
		if (e == null)
		{
			return false;
		}
		e.Text = e.Text.Replace(oldText, newText, StringComparison.Ordinal);
		Changed();
		return true;
	}

	public void Reset()
	{
		ClearTodayNotes();
		Revision++;
	}

	private static string SecretFallback(string npc, string player, string reaction)
	{
		if (SkippedReply.Is(player))
		{
			return $"🤫 {npc}找我说了些话，我听完没接话。";
		}
		if (MissingReply(reaction))
		{
			return $"🤫 和{npc}私下说了几句。";
		}
		return $"🤫 和{npc}说了些悄悄话：我说“{player}”，{npc}回我“{reaction}”。";
	}

	private static string FestivalFallback(string festival, string npc, string player, string reaction)
	{
		string f = string.IsNullOrWhiteSpace(festival) ? "节日" : festival.Trim();
		string n = string.IsNullOrWhiteSpace(npc) ? "某位村民" : npc.Trim();
		if (SkippedReply.Is(player))
		{
			return $"🎪 {f}：{n}跟我聊了几句，我听完没接话。";
		}
		if (MissingReply(reaction))
		{
			return $"🎪 {f}：节日上和{n}聊了聊。";
		}
		if (string.IsNullOrWhiteSpace(player))
		{
			return $"🎪 {f}：{n}在节日上说了句“{reaction}”。";
		}
		return $"🎪 {f}：和{n}聊天，我说“{player}”，{n}回“{reaction}”。";
	}

	private static bool MissingReply(string reaction) => string.IsNullOrWhiteSpace(reaction) || string.Equals(reaction.Trim(), NoReply, StringComparison.OrdinalIgnoreCase);

	private List<DiaryEntry> Entries()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return new List<DiaryEntry>();
		}
		return store.Get(Path, Doc);
	}

	private void Changed()
	{
		Revision++;
		_store()?.MarkDirty(Path);
	}

	private sealed class FestivalNote
	{
		public FestivalNote(string festival, string npc)
		{
			Festival = festival;
			Npc = npc;
		}

		public string Festival { get; }

		public string Npc { get; }

		public int Turns { get; set; }

		public string LastPlayer { get; set; } = "";

		public string LastReaction { get; set; } = "";

		public string ForPrompt()
		{
			string turns = $"聊了 {Turns} 轮";
			if (SkippedReply.Is(LastPlayer))
			{
				return $"🎪 {Festival}上和{Npc}：{turns}，我最后没接话，也没有记下回应。";
			}
			string player = string.IsNullOrWhiteSpace(LastPlayer) ? "（随便聊了聊）" : LastPlayer;
			return MissingReply(LastReaction)
				? $"🎪 {Festival}上和{Npc}：{turns}，我最后说“{player}”，没有记下回应。"
				: $"🎪 {Festival}上和{Npc}：{turns}，最后我说“{player}”，{Npc}回“{LastReaction}”。";
		}
	}
}
