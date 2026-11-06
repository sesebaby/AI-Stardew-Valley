using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Social.Gossip;

/// <summary>八卦的另一些状态：还没给玩家看的那句八卦。</summary>
public sealed class GossipState
{
	public string PendingChatMessage { get; set; } = "";
}

/// <summary>镇上的八卦。两份清单：新八卦同一个对象同时放进两份。今天的对话记录和外来素材只在内存里，晚上整理完就清掉。只在主线程用。</summary>
public sealed class GossipBook
{
	public const string PromptPath = "town/rumors_known.json";

	public const string FeedPath = "town/rumors_feed.json";

	public const string StatePath = "town/rumors_state.json";

	public const int MinEntriesToCompact = 5;

	public const int MaxExternalNoteChars = 1200;

	public const int MaxExperimentalNotesPerDay = 12;

	private static readonly DocumentType<List<GossipEntry>> PromptDoc = new DocumentType<List<GossipEntry>>("八卦（提示词）", 1, () => new List<GossipEntry>());

	private static readonly DocumentType<List<GossipEntry>> FeedDoc = new DocumentType<List<GossipEntry>>("八卦动态", 1, () => new List<GossipEntry>());

	private static readonly DocumentType<GossipState> StateDoc = new DocumentType<GossipState>("八卦状态", 1, () => new GossipState());

	private readonly Func<DocumentStore?> _store;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, bool> _npcEnabled;

	private readonly Func<string, bool> _modEnabled;

	private readonly Func<string, string> _display;

	private readonly ILog _log;

	private readonly List<DailyInteraction> _today = new List<DailyInteraction>();

	private readonly List<(string Text, string Owner)> _experimental = new List<(string, string)>();

	private readonly List<(string Text, string Owner)> _thirdParty = new List<(string, string)>();

	private GameDate? _lastCompaction;

	public GossipBook(Func<DocumentStore?> store, Func<ModConfig> config, Func<string, bool> npcEnabled, Func<string, bool> modEnabled, Func<string, string> display, ILog log)
	{
		_store = store;
		_config = config;
		_npcEnabled = npcEnabled;
		_modEnabled = modEnabled;
		_display = display;
		_log = log;
	}

	private bool Enabled => _config().RumorsOn;

	public int TodayInteractionCount => _today.Count;

	/// <summary>八卦动态，新的在前。</summary>
	public IReadOnlyList<GossipEntry> Feed() => Ordered(FeedList()).ToList();

	/// <summary>给 AI 用的八卦动态（周报等），去掉不能给 AI 的。</summary>
	public List<GossipEntry> FeedForAi() => Enabled ? Ordered(FeedList().Where(Eligible)).ToList() : new List<GossipEntry>();

	public IReadOnlyList<GossipEntry> PromptEntries => PromptList();

	public string PendingChatMessage => State()?.PendingChatMessage ?? "";

	// ---- 今天发生的事 ----

	public void RecordInteraction(DailyInteraction interaction) => _today.Add(interaction);

	public List<DailyInteraction> TakeTodayInteractions()
	{
		List<DailyInteraction> list = _today.ToList();
		_today.Clear();
		return list;
	}

	/// <summary>实验内容交来的素材：要有来源 mod 且该 mod 没被关掉，每天最多 12 条。</summary>
	public bool AddExperimentalNote(string text, string ownerModId)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(ownerModId) || !_modEnabled(ownerModId) || _experimental.Count >= MaxExperimentalNotesPerDay)
		{
			return false;
		}
		_experimental.Add((Cut(text, MaxExternalNoteChars), ownerModId.Trim()));
		return true;
	}

	/// <summary>其他 mod（或"看到听到"）交来的素材：设置里允许才收，每天有上限（默认 15，最多 100）。</summary>
	public bool AddThirdPartyNote(string text, string ownerModId, bool builtIn = false)
	{
		ModConfig c = _config();
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(ownerModId) || !builtIn && !c.RumorsFromMods)
		{
			return false;
		}
		int limit = builtIn ? 15 : Math.Clamp(c.ModRumorsDaily, 0, 100);
		if (_thirdParty.Count >= limit)
		{
			_log.Warn($"八卦：今天外来素材已满 {limit} 条，忽略来自 {ownerModId} 的一条。");
			return false;
		}
		_thirdParty.Add((Cut(text, MaxExternalNoteChars), ownerModId.Trim()));
		return true;
	}

	public ExternalNotes TakeExternalNotes()
	{
		List<(string Text, string Owner)> ec = _experimental.Where(n => _modEnabled(n.Owner)).ToList();
		List<(string Text, string Owner)> tp = _thirdParty.ToList();
		_experimental.Clear();
		_thirdParty.Clear();
		return new ExternalNotes(
			ec.Concat(tp).Select(n => n.Text).ToList(),
			ec.Select(n => n.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Concat(tp.Select(n => n.Owner).Distinct(StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
	}

	/// <summary>八卦关掉时，今天收集的都丢掉。</summary>
	public void DiscardToday()
	{
		_today.Clear();
		_experimental.Clear();
		_thirdParty.Clear();
	}

	// ---- 提示词块 ----

	/// <summary>同一天别的农夫和这位村民聊过什么（联机）。</summary>
	public string OtherFarmersToday(string npc, string currentPlayer)
	{
		if (!Enabled)
		{
			return "";
		}
		List<DailyInteraction> list = _today.Where(i => string.Equals(i.Npc, npc, StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Farmer, currentPlayer, StringComparison.OrdinalIgnoreCase)).ToList();
		if (list.Count == 0)
		{
			return "";
		}
		string name = _display(npc);
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"\n【今天早些时候，别的农夫也找{name}聊过】");
		foreach (DailyInteraction i in list)
		{
			string said = i.Opener.Length > 120 ? i.Opener.Substring(0, 120) + "…" : i.Opener;
			sb.AppendLine($"- {i.Farmer}（聊得{i.Face}）：{name}先说“{said}”，后来的反应是：{i.Comeback}");
		}
		return sb.ToString();
	}

	/// <summary>
	/// 这位村民知道、而且和他有关的八卦（最多 <paramref name="max"/> 条）：
	/// 提到他本人 +3、提到他的亲友 +2、提到农夫 +1、节日八卦 +1，每旧一季排的时候少算 1 分，节日八卦只放一条；
	/// 不够 2 条时补上他知道的、提到农夫的最新八卦。
	/// </summary>
	public string MemoryFor(string npc, string playerName, int today, int max = 5)
	{
		if (!Enabled)
		{
			return "";
		}
		List<GossipEntry> entries = PromptList();
		if (entries.Count == 0)
		{
			return "";
		}
		HashSet<string> circle = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { npc };
		foreach ((string a, string b, string _) in BuiltInPersonas.Relationships)
		{
			if (string.Equals(a, npc, StringComparison.OrdinalIgnoreCase))
			{
				circle.Add(b);
			}
			else if (string.Equals(b, npc, StringComparison.OrdinalIgnoreCase))
			{
				circle.Add(a);
			}
		}
		string player = playerName?.ToLowerInvariant() ?? "";
		bool AboutFarmer(GossipEntry e) => Keywords.Any(e.Fact, Keywords.Farmer) || player.Length > 0 && e.Fact.ToLowerInvariant().Contains(player);
		List<(GossipEntry Entry, int Score)> scored = new List<(GossipEntry, int)>();
		foreach (GossipEntry e in entries)
		{
			if (e.ExpiredForPrompt(today) || !Eligible(e) || !GossipSpread.KnownBy(e, npc))
			{
				continue;
			}
			int score = 0;
			if (Mentions(e.Fact, npc))
			{
				score += 3;
			}
			score += 2 * circle.Count(n => !string.Equals(n, npc, StringComparison.OrdinalIgnoreCase) && Mentions(e.Fact, n));
			if (AboutFarmer(e))
			{
				score++;
			}
			if (e.IsFestival)
			{
				score++;
			}
			if (score > 0)
			{
				scored.Add((e, score));
			}
		}
		List<GossipEntry> picked = new List<GossipEntry>();
		bool festivalTaken = false;
		foreach ((GossipEntry e, int _) in scored.OrderByDescending(s => s.Score - Math.Max(0, today - s.Entry.TotalDay) / 28).ThenByDescending(s => s.Entry.TotalDay))
		{
			if (e.IsFestival)
			{
				if (festivalTaken)
				{
					continue;
				}
				festivalTaken = true;
			}
			picked.Add(e);
			if (picked.Count >= max)
			{
				break;
			}
		}
		if (picked.Count < 2)
		{
			picked.AddRange(entries
				.Where(e => !picked.Contains(e) && Eligible(e) && !e.ExpiredForPrompt(today))
				.Where(e => !e.IsFestival || !picked.Any(p => p.IsFestival))
				.Where(e => GossipSpread.KnownBy(e, npc) && AboutFarmer(e))
				.OrderByDescending(e => e.TotalDay)
				.Take(2 - picked.Count));
		}
		if (picked.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【你知道的镇上八卦】（和你有关的几件）");
		foreach (GossipEntry e in picked)
		{
			sb.AppendLine($"- [{DateText(e)}] {GossipSpread.Lead(e, npc, _display)} {e.Fact}");
		}
		return sb.ToString();
	}

	/// <summary>两位村民当中至少一位知道的最近 4 条八卦（村民之间的互动用）。</summary>
	public string SharedBetween(string npc1, string npc2)
	{
		if (!Enabled)
		{
			return "";
		}
		List<GossipEntry> list = PromptList().Where(Eligible)
			.Where(e => GossipSpread.KnownBy(e, npc1) || GossipSpread.KnownBy(e, npc2))
			.OrderByDescending(e => e.TotalDay).Take(4).ToList();
		if (list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【这两人可能听说过的八卦】");
		foreach (GossipEntry e in list)
		{
			string who = string.Join("、", new[] { npc1, npc2 }.Where(n => GossipSpread.KnownBy(e, n)).Select(_display));
			sb.AppendLine($"- [{DateText(e)}] {who}知道：{e.Fact}");
		}
		sb.AppendLine("只有标明知道的那位才会提起这件事。");
		return sb.ToString();
	}

	/// <summary>这位村民知道的最近几条（新的在前）。</summary>
	public List<GossipEntry> RecentKnownBy(string npc, int max) =>
		Enabled ? Ordered(PromptList().Where(Eligible).Where(e => GossipSpread.KnownBy(e, npc))).Take(max).ToList() : new List<GossipEntry>();

	// ---- 加八卦 ----

	/// <summary>晚上整理出的八卦：放进两份清单，提示词清单超出上限时从最旧的非节日八卦删起，并记成"待给玩家看"。</summary>
	public GossipEntry AddDaily(GameDate date, string memory, string chat, IReadOnlyList<string> sourceNpcs, IReadOnlyList<string> sourceMods)
	{
		string shown = string.IsNullOrWhiteSpace(chat) ? memory.Trim() : chat.Trim();
		GossipEntry e = GossipSpread.Create(date.Day, date.Season, date.Year, memory, shown, sourceNpcs, _display);
		e.FromMods = sourceMods.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		e.Unsourced = e.Involved.Count == 0 && e.FromMods.Count == 0;
		AddToBoth(e);
		SetPending(shown);
		return e;
	}

	/// <summary>场景、出游等留下的小事：同一来源同一天只记一次，3 天后不再进提示词，只再传一轮。</summary>
	public bool AddLowKey(string text, IEnumerable<string> participants, string originType, string originId, GameDate date)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string id = originId?.Trim() ?? "";
		string key = $"{originType}:{id}:{date.Year}-{date.Season}-{date.Day}";
		if (PromptList().Any(e => e.HasKey(key)) || FeedList().Any(e => e.HasKey(key)))
		{
			return false;
		}
		GossipEntry e = GossipSpread.Create(date.Day, date.Season, date.Year, text.Trim(), text.Trim(), participants, _display);
		e.Key = e.Topic = key;
		e.Kind = e.SourceKind = originType;
		e.KindRef = e.SourceRef = id;
		e.FadesOn = e.LeavesPromptOn = e.BornOn + 3;
		e.HopsLeft = 1;
		AddToBoth(e);
		return true;
	}

	/// <summary>写好的一条（一位村民起头），同一来源同一天只加一次；传两跳。</summary>
	public bool AddAuthored(string memory, string chat, string speaker, string originType, string originId, int lifetimeDays, GameDate date)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(memory) || string.IsNullOrWhiteSpace(speaker))
		{
			return false;
		}
		string key = $"{originType}:{originId}:{date.Year}-{date.Season}-{date.Day}";
		if (PromptList().Any(e => e.HasKey(key)) || FeedList().Any(e => e.HasKey(key)))
		{
			return false;
		}
		GossipEntry e = GossipSpread.Create(date.Day, date.Season, date.Year, memory.Trim(), chat.Trim(), new[] { speaker }, _display);
		e.Key = e.Topic = key;
		e.Kind = e.SourceKind = originType;
		e.KindRef = e.SourceRef = originId;
		e.FadesOn = e.LeavesPromptOn = e.BornOn + Math.Max(1, lifetimeDays);
		e.HopsLeft = 2;
		AddToBoth(e);
		return true;
	}

	/// <summary>故事弧结局等：直接加，不设有效期。</summary>
	public void AddResolution(string summary, IEnumerable<string> npcs, IEnumerable<string> mods, GameDate date)
	{
		if (!Enabled)
		{
			return;
		}
		GossipEntry e = GossipSpread.Create(date.Day, date.Season, date.Year, summary, summary, npcs, _display);
		e.FromMods = mods.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		e.Unsourced = e.Involved.Count == 0 && e.FromMods.Count == 0;
		PromptList().Add(e);
		FeedList().Add(e);
		Dirty();
	}

	/// <summary>村民之间值得传的一件事。</summary>
	public bool AddVillageMoment(int day, string season, int year, string memory, string shown, IReadOnlyList<string> npcs)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(memory) || npcs.Count == 0)
		{
			return false;
		}
		AddToBoth(GossipSpread.Create(day, season, year, memory, shown, npcs, _display));
		return true;
	}

	/// <summary>节日的汇总八卦：同一个键只记一次，有效期到 <paramref name="promptDays"/> 天后，只再传一轮。</summary>
	public bool AddFestival(string festivalId, string entryKey, string summary, string chat, GameDate date, int promptDays)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(festivalId) || string.IsNullOrWhiteSpace(summary))
		{
			return false;
		}
		string key = string.IsNullOrWhiteSpace(entryKey) ? $"festival:{festivalId}:{date.Year}" : entryKey.Trim();
		if (PromptList().Any(e => e.HasKey(key)) || FeedList().Any(e => e.HasKey(key)))
		{
			return false;
		}
		GossipEntry e = GossipSpread.Create(date.Day, date.Season, date.Year, summary.Trim(), string.IsNullOrWhiteSpace(chat) ? summary.Trim() : chat.Trim(), Array.Empty<string>(), _display);
		e.Key = e.Topic = key;
		e.Kind = e.SourceKind = "festival";
		e.KindRef = e.SourceRef = festivalId.Trim();
		e.FadesOn = e.LeavesPromptOn = e.BornOn + Math.Max(0, promptDays);
		e.HopsLeft = 1;
		AddToBoth(e);
		return true;
	}

	// ---- 每天 ----

	/// <summary>天亮时让八卦传一轮，返回传开的条数。</summary>
	public int SpreadForDay(int today)
	{
		int n = 0;
		foreach (GossipEntry e in PromptList())
		{
			n += GossipSpread.SpreadForDay(e, today) ? 1 : 0;
		}
		foreach (GossipEntry e in FeedList())
		{
			n += GossipSpread.SpreadForDay(e, today) ? 1 : 0;
		}
		if (n > 0)
		{
			Dirty();
		}
		return n;
	}

	/// <summary>今天该不该压缩：至少 5 条、今天没压过；间隔天数 ≤ 0 时每月 1 日压，否则总天数整除间隔时压。</summary>
	public bool ShouldCompact(GameDate today)
	{
		if (PromptList().Count < MinEntriesToCompact || _lastCompaction == today)
		{
			return false;
		}
		int interval = _config().RumorTidyDays;
		return interval <= 0 ? today.Day == 1 : today.TotalDays % interval == 0;
	}

	/// <summary>能拿去压缩的：不是节日八卦、可以给 AI 的。不到 5 条返回空。</summary>
	public List<GossipEntry> CompactionInput()
	{
		List<GossipEntry> list = PromptList().Where(e => !e.IsFestival && Eligible(e)).ToList();
		return list.Count < MinEntriesToCompact ? new List<GossipEntry>() : list;
	}

	/// <summary>
	/// 压缩结果合回来：被压的那些换成新的几条（继承原来涉及的村民和 mod），节日八卦、不能给 AI 的、压缩期间新加的保留，按日期排好再按上限裁。
	/// </summary>
	public void ApplyCompaction(IReadOnlyList<GossipEntry> compacted, IReadOnlyList<GossipEntry> input, GameDate today)
	{
		List<GossipEntry> current = PromptList();
		HashSet<GossipEntry> inputSet = new HashSet<GossipEntry>(input, ReferenceEqualityComparer.Instance);
		List<string> npcs = input.SelectMany(GossipSpread.Provenance).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		List<string> mods = input.SelectMany(e => e.FromMods ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (GossipEntry e in compacted)
		{
			e.Involved = npcs.ToList();
			e.FromMods = mods.ToList();
			e.Unsourced = npcs.Count == 0 && mods.Count == 0;
			Normalize(e);
		}
		List<GossipEntry> kept = current.Where(e => !inputSet.Contains(e)).ToList();
		List<GossipEntry> protectedBlocked = kept.Where(e => !e.IsFestival && !Eligible(e)).ToList();
		List<GossipEntry> merged = compacted.Concat(kept).OrderBy(e => e.TotalDay).ToList();
		current.Clear();
		current.AddRange(merged);
		Trim(protectedBlocked);
		_lastCompaction = today;
		Dirty();
	}

	public string? TakePendingChatMessage()
	{
		string pending = PendingChatMessage;
		if (string.IsNullOrWhiteSpace(pending))
		{
			return null;
		}
		ClearPending(pending);
		return pending;
	}

	/// <summary>清掉待显示的八卦（只在它还是 <paramref name="expected"/> 时）。</summary>
	public void ClearPending(string? expected = null)
	{
		GossipState? s = State();
		if (s == null || !string.IsNullOrWhiteSpace(expected) && !string.Equals(s.PendingChatMessage, expected, StringComparison.Ordinal))
		{
			return;
		}
		if (s.PendingChatMessage.Length > 0)
		{
			s.PendingChatMessage = "";
			_store()?.MarkDirty(StatePath);
		}
	}

	public void Reset()
	{
		DiscardToday();
		_lastCompaction = null;
	}

	/// <summary>能不能给 AI：来路不明的（节日除外）不行；涉及被关掉的村民或 mod 的不行。</summary>
	public bool Eligible(GossipEntry e)
	{
		if (e.Unsourced && !e.IsFestival)
		{
			return false;
		}
		return GossipSpread.Provenance(e).All(_npcEnabled) && (e.FromMods ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).All(_modEnabled);
	}

	public static string DateText(GossipEntry e) => $"第 {e.Year} 年{HistoryFormatter.SeasonName(e.Season)} {e.Day} 日";

	private static IEnumerable<GossipEntry> Ordered(IEnumerable<GossipEntry> list) => list.OrderByDescending(e => e.Year).ThenByDescending(e => GameDate.SeasonIndex(e.Season)).ThenByDescending(e => e.Day);

	private bool Mentions(string text, string npc)
	{
		if (text.Contains(npc, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string shown = _display(npc);
		return shown.Length > 0 && !string.Equals(shown, npc, StringComparison.OrdinalIgnoreCase) && text.Contains(shown, StringComparison.OrdinalIgnoreCase);
	}

	private void AddToBoth(GossipEntry e)
	{
		PromptList().Add(e);
		FeedList().Add(e);
		Trim(null);
		Dirty();
		_log.Debug($"八卦：新加一条（{(string.IsNullOrEmpty(e.Kind) ? "日常" : e.Kind)}），知道的人 {e.KnownTo.Count} 位：{e.Fact}");
	}

	/// <summary>提示词清单超出上限：删最旧的非节日、非保护的；全是保护的就停。</summary>
	private void Trim(IReadOnlyCollection<GossipEntry>? alsoProtected)
	{
		List<GossipEntry> list = PromptList();
		HashSet<GossipEntry>? guard = alsoProtected == null ? null : new HashSet<GossipEntry>(alsoProtected, ReferenceEqualityComparer.Instance);
		while (list.Count > _config().RumorsInPrompt)
		{
			int i = list.FindIndex(e => !e.IsFestival && (guard == null || !guard.Contains(e)));
			if (i < 0 && guard is { Count: > 0 })
			{
				break;
			}
			list.RemoveAt(i >= 0 ? i : 0);
		}
	}

	private void SetPending(string message)
	{
		GossipState? s = State();
		if (s != null)
		{
			s.PendingChatMessage = message;
			_store()?.MarkDirty(StatePath);
		}
	}

	private GossipState? State() => _store()?.Get(StatePath, StateDoc);

	private List<GossipEntry> PromptList() => Load(PromptPath, PromptDoc);

	private List<GossipEntry> FeedList() => Load(FeedPath, FeedDoc);

	private List<GossipEntry> Load(string path, DocumentType<List<GossipEntry>> doc)
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return new List<GossipEntry>();
		}
		return store.Get(path, doc);
	}

	/// <summary>压缩回来的八卦：补齐字段，名字里的内部名换成游戏里的名字。</summary>
	private void Normalize(GossipEntry e)
	{
		GossipSpread.Normalize(e, everyoneIfUnset: true);
		List<string> names = GossipSpread.Provenance(e).ToList();
		e.Fact = ReplaceNames(e.Fact, names);
		e.Rumor = ReplaceNames(e.Rumor, names);
	}

	private string ReplaceNames(string text, IEnumerable<string> names)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		foreach (string n in names.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			string shown = _display(n);
			if (!string.Equals(n, shown, StringComparison.OrdinalIgnoreCase))
			{
				text = text.Replace(n, shown, StringComparison.OrdinalIgnoreCase);
			}
		}
		return text;
	}

	private void Dirty()
	{
		DocumentStore? store = _store();
		if (store != null)
		{
			store.MarkDirty(PromptPath);
			store.MarkDirty(FeedPath);
		}
	}

	private static string Cut(string text, int max)
	{
		string t = text.Trim();
		return t.Length <= max ? t : t.Substring(0, max);
	}
}
