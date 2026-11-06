using AISV.Core.Outings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Gifts;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Social;
using AISV.Core.Social.Diary;
using AISV.Core.Social.Drama;
using AISV.Core.Social.Gossip;
using AISV.Core.Social.Letters;
using AISV.Core.Social.Personality;
using AISV.Core.Social.Web;
using AISV.Core.Storage;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Core.World;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>
/// 社交模拟的总调度：天亮、天黑、聊天、送礼时依次叫各个子系统。
/// 子系统的数据都只在主线程读写；要调 AI 的活在主线程拿好副本，后台一件一件排队做，结果回主线程、确认还是同一个存档再写回。
/// </summary>
public sealed class SocialSystem
{
	/// <summary>村民之间的事在提示词里最多留 10 件。</summary>
	private const int MaxRecentInteractions = 10;

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ConversationLog _conversations;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	private readonly SemaphoreSlim _jobGate = new SemaphoreSlim(1, 1);

	private readonly List<NpcSocialMoment> _recentInteractions = new List<NpcSocialMoment>();

	private readonly Random _random = new Random();

	private readonly List<(string Npc, string Text, int Time)> _birthdayTexts = new List<(string, string, int)>();

	private readonly HashSet<string> _textedToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private int _birthdayTextDay = -1;

	private int _textedDay = -1;

	private HashSet<int>? _heartEventsAtDayStart;

	public SocialSystem(Func<ModConfig> config, Func<DocumentStore?> store, IAiSender ai, PromptTemplates templates, Func<string> language, ConversationLog conversations, Relationships relationships, NpcDirectory npcs, ILog log)
	{
		_config = config;
		_store = store;
		_conversations = conversations;
		_relationships = relationships;
		_npcs = npcs;
		_log = log;
		Func<string, string> display = npcs.Names.Display;
		Ai = new SocialAi(ai, templates, language, display, log);
		Gossip = new GossipBook(store, config, npcs.IsEnabled, _ => true, display, log);
		Feelings = new EmotionalMemory(store);
		Milestones = new MilestoneBook(store);
		Gifts = new GiftBook(store);
		Diary = new PlayerDiary(store);
		Letters = new LetterBook(store, config, npcs.IsEnabled);
		Personality = new PersonalityBook(store, config, npcs.IsEnabled, display);
		Web = new SocialWeb(store, config, display);
		Drama = new SocialDrama(store, config, npcs.IsEnabled, _ => true, display, log);
		Retries = new DailyRetry(store);
		Gifts.Recorded += OnGiftRecorded;
	}

	public SocialAi Ai { get; }

	public MoodBook Moods { get; } = new MoodBook();

	public GossipBook Gossip { get; }

	public EmotionalMemory Feelings { get; }

	public MilestoneBook Milestones { get; }

	public GiftBook Gifts { get; }

	public PlayerDiary Diary { get; }

	public LetterBook Letters { get; }

	public PersonalityBook Personality { get; }

	public SocialWeb Web { get; }

	public SocialDrama Drama { get; }

	/// <summary>信里提的还没办完的托付：村民 → 几条。</summary>
	public Func<string, List<string>>? OpenFavors { get; set; }

	/// <summary>给信配 AI 礼物：(村民, 邮件键, 理由) → 礼物 ID；没备上返回 null。</summary>
	public Func<string, string, string, string?>? AttachGift { get; set; }

	public Func<string?, string?>? GiftName { get; set; }

	/// <summary>被成人内容包换掉的心事件不进八卦。</summary>
	public Func<int, bool>? IsReplacedHeartEvent { get; set; }

	/// <summary>晚上的八卦整理有了结果（或者今天不用整理）：那天的日子。没开 AI 或八卦时不触发。</summary>
	public event Action<GameDate>? NightSettled;

	private void Settle(GameDate day)
	{
		try
		{
			NightSettled?.Invoke(day);
		}
		catch (Exception ex)
		{
			_log.Warn("晚上整理之后的处理出错：" + ex.Message);
		}
	}

	/// <summary>最近村民之间的事（今天天亮时从社交网取最近 2 天的，晚上新发生的再加进来）。</summary>
	public IReadOnlyList<NpcSocialMoment> RecentInteractions => _recentInteractions;

	/// <summary>晚上整理的重试（存盘，第二天还能点）。</summary>
	public DailyRetry Retries { get; }

	private static string Player => Game1.player?.Name ?? "";

	private string Display(string npc) => _npcs.Names.Display(npc);

	// ================= 天亮 =================

	public void OnDayStarted()
	{
		if (_store() == null || Game1.player == null)
		{
			return;
		}
		ModConfig c = _config();
		GameDate today = GameState.Today;
		string weather = DayWeather();
		Safe("性格演变的日常变化", () => Personality.AdvanceDay(today.TotalDays));
		CaptureHeartEventBaseline();
		Safe("算今天的心情", () => Moods.CalculateDay(weather, today.Season, today.Day, BuiltInPersonas.VanillaNames.Select(n => new MoodInput(n, IsNpcBirthday(n, today), _relationships.EffectiveHearts(n)))));
		_recentInteractions.Clear();
		foreach (NpcSocialMoment m in Web.Recent(today.TotalDays, 2, _npcs.IsEnabled))
		{
			RememberInteraction(m);
		}
		Safe("农夫生日", () => PlayerBirthday(today));
		if (c.MailOn)
		{
			Safe("投信", () => Letters.Deliver(today.TotalDays, InMailOrRead, PutInMailbox, _log.Debug));
		}
		ShowPendingGossip();
		if (c.RumorsOn)
		{
			Safe("八卦传播", () => Gossip.SpreadForDay(today.TotalDays));
		}
		Safe("社交剧情", () => Drama.OnDayStarted(today.TotalDays));
		if (!c.AiOn || !Context.IsMainPlayer)
		{
			return;
		}
		QueueMorningJobs(today);
	}

	private void QueueMorningJobs(GameDate today)
	{
		ModConfig c = _config();
		// 压缩八卦
		if (c.RumorsOn && Gossip.ShouldCompact(today))
		{
			List<GossipEntry> input = Gossip.CompactionInput();
			if (input.Count > 0)
			{
				Enqueue("压缩八卦", async ct =>
				{
					List<GossipEntry>? result = await Ai.CompactAsync(input, today, ct).ConfigureAwait(false);
					return result == null ? null : () => Gossip.ApplyCompaction(result, input, today);
				});
			}
		}
		// 每周日：重新评估情感档案
		if ((today.Day - 1) % 7 == 6)
		{
			QueueProfiles(today);
		}
		// 写信
		if (c.MailOn)
		{
			List<LetterCandidate> candidates = LetterCandidates();
			foreach (LetterTrigger t in Letters.DueToday(today, candidates))
			{
				QueueLetter(t, today);
			}
			if (c.Verbose)
			{
				_log.Debug($"[信件] 今天检查了 {candidates.Count} 位村民。");
			}
		}
	}

	private void QueueProfiles(GameDate today)
	{
		ModConfig c = _config();
		int turns = Math.Clamp(c.RepliesPerTalk, 1, 50);
		int window = Math.Max(10, turns + 2);
		foreach (string npc in _conversations.Tracked().Where(_npcs.IsEnabled))
		{
			List<ConversationEntry> history = _conversations.Recent(npc, 20).Where(e => !SkippedReply.Is(e.FarmerReply)).ToList();
			if (history.Count < 3)
			{
				continue;
			}
			string previous = Feelings.PreviousForAnalysis(npc);
			string text = HistoryFormatter.Format(Display(npc), history, window, c.RepliesPerTalk, today, Player);
			Enqueue($"{npc} 的情感档案", async ct =>
			{
				EmotionalProfile? p = await Ai.ProfileAsync(npc, history, previous, text, ct).ConfigureAwait(false);
				return p == null ? null : () =>
				{
					Feelings.ApplyAnalysis(npc, p, today);
					_log.Debug($"{npc} 的情感档案：信任 {p.FaithInFarmer:F2}，感情 {p.Affection:F2}");
				};
			});
		}
	}

	private List<LetterCandidate> LetterCandidates()
	{
		string player = Player;
		IEnumerable<string> names = _conversations.Tracked().Concat(Game1.player.friendshipData.Keys);
		return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Select(n =>
		{
			ConversationEntry? last = _conversations.Recent(n, int.MaxValue).LastOrDefault(e => string.Equals(e.Farmer, player, StringComparison.OrdinalIgnoreCase));
			int? lastDay = last == null ? null : Dates.TotalDays(last.Day, last.Season, last.Year);
			int hearts = Game1.player.friendshipData.ContainsKey(n) ? _relationships.EffectiveHearts(n) : 0;
			return new LetterCandidate(n, hearts, _conversations.Count(n), lastDay);
		}).ToList();
	}

	/// <summary>主线程拼好写信要的材料，后台让 AI 写，写好了回主线程放进待寄。</summary>
	private void QueueLetter(LetterTrigger t, GameDate today)
	{
		LetterInput input = LetterMaterial(t);
		Enqueue($"{t.Npc} 的信", async ct =>
		{
			(string Subject, string Body)? letter = await Ai.LetterAsync(input, ct).ConfigureAwait(false);
			if (letter == null)
			{
				return null;
			}
			return () =>
			{
				bool hearts = t.Trigger.StartsWith("hearts_", StringComparison.Ordinal);
				int gift = hearts ? LetterBook.UsualGift(t.Npc, id => LetterGifts.Allowed(id, false, _config())) : -1;
				QueuedLetter? queued = Letters.Add(t.Npc, t.Trigger, letter.Value.Subject, letter.Value.Body, gift, today);
				if (queued != null)
				{
					if (hearts)
					{
						AttachAiGift(queued, $"你和农夫的友情到了 {t.Trigger.Substring(7)} 心；这封信说的是：{letter.Value.Subject}");
					}
					_log.Info($"{t.Npc} 写了一封信（{t.Trigger}），明天早上寄到。");
				}
			};
		});
	}

	private LetterInput LetterMaterial(LetterTrigger t)
	{
		string npc = t.Npc;
		string player = Player;
		NPC? character = GameState.FindNpc(npc);
		List<ConversationEntry> recent = _conversations.Recent(npc, int.MaxValue).Where(e => string.Equals(e.Farmer, player, StringComparison.OrdinalIgnoreCase)).TakeLast(5).ToList();
		EmotionalProfile profile = Feelings.GetOrCreate(npc);
		string style = LetterPrompts.StyleHint(t.Trigger, profile, _random.Next);
		PlayerProfile? sheet = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		string who = string.IsNullOrWhiteSpace(sheet?.Intro) ? "农夫" : sheet!.Intro;
		return new LetterInput(
			npc,
			PersonaPrompt.PersonalityBlock(npc, _npcs.Persona(npc, character).Personality),
			t.Context,
			LetterPrompts.History(recent),
			player,
			PlayerProfileBlock.Build(npc, who, 0, sheet),
			LetterPrompts.Feelings(profile),
			style,
			LetterPrompts.Teasing(profile, recent, style),
			LetterPrompts.Gossip(Gossip.RecentKnownBy(npc, 2)),
			LetterWorld(npc));
	}

	/// <summary>信里的"现在的情况"：日期天气、今天的心情、性格演变、和别的村民最近的事。</summary>
	private string LetterWorld(string npc)
	{
		StringBuilder sb = new StringBuilder();
		string weather = Game1.isSnowing ? "下雪" : Game1.isRaining ? "下雨" : Game1.isDebrisWeather ? "刮风" : "晴天";
		GameDate today = GameState.Today;
		sb.AppendLine($"今天是{Dates.Label(today)}，{weather}。");
		NpcMood? mood = Moods.Get(npc);
		if (mood != null && mood.Temper != "neutral")
		{
			sb.AppendLine($"你今天的心情：{MoodBook.MoodWord(mood.Temper)}" + (string.IsNullOrWhiteSpace(mood.Because) ? "。" : $"（{mood.Because.Trim()}）。"));
		}
		string evolution = Personality.PromptBlock(npc);
		if (evolution.Length > 0)
		{
			sb.AppendLine(evolution.Trim());
		}
		string others = InteractionsBlock(npc);
		if (others.Length > 0)
		{
			sb.AppendLine(others.Trim());
		}
		List<string> open = OpenFavors?.Invoke(npc) ?? new List<string>();
		if (open.Count > 0)
		{
			sb.AppendLine("你托农夫的事还没办完（信里想提可以提一句，别催）：" + string.Join("；", open));
		}
		return sb.ToString().Trim();
	}

	// ================= 天黑 =================

	public void OnDayEnding()
	{
		if (_store() == null || Game1.player == null)
		{
			return;
		}
		GameDate today = GameState.Today;
		string weather = DayWeather();
		List<string> secretNotes = Diary.TodayNotes();
		string fallback = Diary.TodayFallback();
		string facts = Safe("当天事实", () => CaptureDayFacts(today, weather).ToPrompt(), "");
		IReadOnlyList<HeartEventCandidate> heartEvents = TakeNewHeartEvents();
		Retries.Discard();
		if (!Context.IsMainPlayer)
		{
			QueueDiaryOnly(today, weather, secretNotes, fallback, facts);
		}
		else
		{
			if (!QueueNightAnalysis(today, weather, secretNotes, fallback, heartEvents, facts) && AppendFallback(fallback, today, weather))
			{
				Notices.Queue(Tr.Get("notice.diaryUpdated", "日记更新了"));
			}
			QueueSocialMoments(today, weather);
		}
		Diary.ClearTodayNotes();
		Gifts.EndDay();
		Moods.EndDay();
	}

	/// <summary>晚上整理八卦和日记。排上了返回 true；没排（没素材、关了功能）时返回"今天没有要兜底的日记"。</summary>
	private bool QueueNightAnalysis(GameDate today, string weather, List<string> secretNotes, string fallback, IReadOnlyList<HeartEventCandidate> heartEvents, string facts)
	{
		ModConfig c = _config();
		if (!c.AiOn || !c.RumorsOn)
		{
			Gossip.DiscardToday();
			return string.IsNullOrWhiteSpace(fallback);
		}
		List<NpcSocialMoment> happenings = VillageHappenings(c, today.TotalDays);
		NpcSocialMoment? fallbackMoment = GossipFocus.PickFallback(happenings);
		string fallbackChat = fallbackMoment == null ? "" : GossipFocus.FallbackChat(fallbackMoment.WhatHappened.Trim());
		PendingDailyAnalysis p = NightMaterial(today, weather, secretNotes, heartEvents, facts, fallback, true, happenings.Select(m => SocialWeb.Describe(m, Display)).ToList());
		DayInput input = Input(p, c.RumorSubject);
		if (input.IsEmpty)
		{
			if (fallbackChat.Length > 0)
			{
				Notices.Queue(Tr.Get("notice.gossipNew", "镇上有新八卦了"));
				ShowGossipLine(fallbackChat);
			}
			Settle(today);
			return string.IsNullOrWhiteSpace(fallback);
		}
		Retries.Begin(p);
		_nightRunning = true;
		Enqueue("晚上整理八卦和日记", async ct =>
		{
			DayAnalysis? a = await Ai.AnalyzeDayAsync(input, ct).ConfigureAwait(false);
			return () =>
			{
				_nightRunning = false;
				ApplyNightAnalysis(a, p, fallbackChat);
				Settle(today);
			};
		}, onFailed: () =>
		{
			_nightRunning = false;
			AfterNightFailure(p);
			Settle(today);
		});
		return true;
	}

	private void ApplyNightAnalysis(DayAnalysis? a, PendingDailyAnalysis p, string fallbackChat)
	{
		GameDate day = p.Date;
		if (a == null)
		{
			Notices.Queue(NightFailedText(p.WithRumors), Notices.Warning);
		}
		else
		{
			Retries.Complete(day);
		}
		if ((a != null && ApplyDiary(a, day, p.Weather)) || AppendFallback(p.PlainJournal, day, p.Weather))
		{
			Notices.Queue(Tr.Get("notice.diaryUpdated", "日记更新了"));
		}
		if (!p.WithRumors)
		{
			return;
		}
		if (a != null && a.HasGossip && !string.IsNullOrWhiteSpace(a.GossipMemory))
		{
			AddNightGossip(a, p);
		}
		else if (a != null && fallbackChat.Length > 0)
		{
			Notices.Queue(Tr.Get("notice.gossipNew", "镇上有新八卦了"));
			ShowGossipLine(fallbackChat);
		}
	}

	private void AddNightGossip(DayAnalysis a, PendingDailyAnalysis p)
	{
		List<string> mods = p.OutsideNotes.SourceModIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		string first = "";
		foreach (DayGossip g in a.Gossip ?? new List<DayGossip>())
		{
			GossipEntry e = Gossip.AddDaily(p.Date, g.Memory!, g.Chat ?? "", ToldBy(g) ?? Sources(p), mods);
			Safe("社交剧情（新八卦）", () => Drama.ApplyGossip(e));
			first = first.Length > 0 ? first : e.Rumor;
		}
		Notices.Queue(Tr.Get("notice.gossipNew", "镇上有新八卦了"));
		ShowGossipLine(first);
	}

	private bool ApplyDiary(DayAnalysis a, GameDate day, string weather)
	{
		if (string.IsNullOrWhiteSpace(a.DiaryEntry))
		{
			return false;
		}
		Diary.Write(a.DiaryEntry!, day);
		Diary.SetDetails(day, weather, DayFacts.NormalizeMood(a.Mood));
		return true;
	}

	/// <summary>整理的活本身出错（超时、断网）：提示可以重试，先把兜底的日记写上。</summary>
	private void AfterNightFailure(PendingDailyAnalysis p)
	{
		if (AppendFallback(p.PlainJournal, p.Date, p.Weather))
		{
			Notices.Queue(Tr.Get("notice.diaryUpdated", "日记更新了"));
		}
		Notices.Queue(NightFailedText(p.WithRumors), Notices.Warning);
	}

	private static string NightFailedText(bool includesGossip) => includesGossip
		? Tr.Get("notice.nightFailed", "今天的八卦和日记没整理出来（AI 忙），可以在八卦或日记页点“重试”")
		: Tr.Get("notice.nightDiaryFailed", "今天的日记没写出来（AI 忙），可以在日记页点“重试”");

	/// <summary>联机的农场工：只写自己的日记，不产生全村的八卦。</summary>
	private void QueueDiaryOnly(GameDate today, string weather, List<string> secretNotes, string fallback, string facts)
	{
		ModConfig c = _config();
		if (!c.AiOn || !c.RumorsOn)
		{
			Gossip.DiscardToday();
			AppendFallback(fallback, today, weather);
			return;
		}
		PendingDailyAnalysis p = NightMaterial(today, weather, secretNotes, Array.Empty<HeartEventCandidate>(), facts, fallback, false, Array.Empty<string>());
		DayInput input = Input(p, GossipFocus.Normal);
		if (input.IsEmpty)
		{
			AppendFallback(fallback, today, weather);
			return;
		}
		Retries.Begin(p);
		_nightRunning = true;
		Enqueue("写今天的日记", async ct =>
		{
			DayAnalysis? a = await Ai.AnalyzeDayAsync(input, ct).ConfigureAwait(false);
			return () =>
			{
				_nightRunning = false;
				ApplyNightAnalysis(a, p, "");
			};
		}, onFailed: () =>
		{
			_nightRunning = false;
			AfterNightFailure(p);
		});
	}

	// ---- 重试 ----

	private bool _retryRunning;

	private bool _nightRunning;

	/// <summary>上一次重试的结果（给八卦页、日记页显示）。</summary>
	public string LastRetryMessage { get; private set; } = "";

	/// <summary>八卦页（gossip = true）或日记页现在能不能点重试。</summary>
	public bool IsAnalysisRunning => _retryRunning || _nightRunning;

	public bool CanRetry(bool gossip)
	{
		PendingDailyAnalysis? p = _store() == null ? null : Retries.RetryableOn(GameState.Today);
		return p != null && (!gossip || p.WithRumors) && !IsAnalysisRunning;
	}

	/// <summary>玩家点了重试：用记下的素材再整理一次。返回给玩家看的一句话。</summary>
	public string Retry(bool gossip)
	{
		if (IsAnalysisRunning)
		{
			return Tr.Get("retry.running", "正在整理，稍等一会儿……");
		}
		if (gossip && !Context.IsMainPlayer)
		{
			return Tr.Get("retry.hostOnly", "只有房主能重新整理全村的八卦。");
		}
		PendingDailyAnalysis? p = _store() == null ? null : Retries.RetryableOn(GameState.Today);
		if (p == null || gossip && !p.WithRumors)
		{
			return gossip ? Tr.Get("retry.gossipDone", "那天的闲话已经归拢过了。") : Tr.Get("retry.diaryDone", "那天的日记没落下，补好了。");
		}
		ModConfig c = _config();
		if (!c.AiOn || !c.RumorsOn)
		{
			return Tr.Get("retry.aiOff", "要先在设置里开着 AI 功能和八卦，才能重新整理。");
		}
		DayInput input = Input(p, c.RumorSubject);
		_retryRunning = true;
		LastRetryMessage = Tr.Get("retry.failed", "AI 还在忙（用的人多），过几分钟再试试。");
		Enqueue("重试晚上整理", async ct =>
		{
			DayAnalysis? a = await Ai.AnalyzeDayAsync(input, ct).ConfigureAwait(false);
			return () =>
			{
				_retryRunning = false;
				ApplyRetry(a, p, gossip);
			};
		}, onFailed: () =>
		{
			_retryRunning = false;
			RetryFailed();
		});
		_log.Info($"重新整理 {Dates.Label(p.Date)} 的八卦和日记。");
		return Tr.Get("retry.started", "开始重新整理……");
	}

	private void ApplyRetry(DayAnalysis? a, PendingDailyAnalysis p, bool gossip)
	{
		PendingDailyAnalysis? still = Retries.Pending;
		if (still == null || !p.Date.SameDay(still.Day, still.Season, still.Year))
		{
			LastRetryMessage = gossip ? Tr.Get("retry.gossipDone", "那天的闲话已经归拢过了。") : Tr.Get("retry.diaryDone", "那天的日记没落下，补好了。");
			return;
		}
		if (a == null)
		{
			RetryFailed();
			return;
		}
		bool hasGossip = p.WithRumors && a.HasGossip && !string.IsNullOrWhiteSpace(a.GossipMemory);
		if (hasGossip)
		{
			AddNightGossip(a, p);
		}
		bool diary = ApplyRetriedDiary(p, a);
		if (diary)
		{
			Diary.SetDetails(p.Date, p.Weather, DayFacts.NormalizeMood(a.Mood));
			Notices.Queue(Tr.Get("notice.diaryUpdated", "日记更新了"));
		}
		Retries.Complete(p.Date);
		LastRetryMessage = gossip
			? hasGossip ? Tr.Get("retry.gossipOk", "八卦整理出来了！") : Tr.Get("retry.noGossip", "重新整理了一遍，那天没什么值得传的。")
			: diary ? Tr.Get("retry.diaryOk", "日记写好了！") : Tr.Get("retry.noDiary", "重新整理了一遍，日记没有新东西可写。");
	}

	/// <summary>重试写出的日记：替换掉之前的兜底内容；没写出时，兜底内容还没写进去就补上。</summary>
	private bool ApplyRetriedDiary(PendingDailyAnalysis p, DayAnalysis a)
	{
		string fallback = p.PlainJournal?.Trim() ?? "";
		GameDate day = p.Date;
		if (string.IsNullOrWhiteSpace(a.DiaryEntry))
		{
			return !Diary.Contains(fallback, day) && AppendFallback(fallback, day, null);
		}
		if (fallback.Length == 0 || !Diary.Replace(fallback, a.DiaryEntry!, day))
		{
			if (Diary.Has(day))
			{
				Diary.Append(a.DiaryEntry!, day);
			}
			else
			{
				Diary.Add(a.DiaryEntry!, day);
			}
		}
		return true;
	}

	private void RetryFailed()
	{
		LastRetryMessage = Tr.Get("retry.failed", "AI 还在忙（用的人多），过几分钟再试试。");
		Notices.Queue(LastRetryMessage, Notices.Warning);
	}

	/// <summary>
	/// 晚上整理的素材：今天的互动（关掉 AI 的、第一年还不在镇上的村民去掉）、外来素材、只给日记的备注（去重）、
	/// 心事件（关掉 AI 的去掉）、当天事实。
	/// </summary>
	private PendingDailyAnalysis NightMaterial(GameDate today, string weather, IEnumerable<string> secretNotes, IReadOnlyList<HeartEventCandidate> heartEvents, string facts, string fallback, bool includesGossip, IReadOnlyList<string> happenings)
	{
		ExternalNotes external = Gossip.TakeExternalNotes();
		return new PendingDailyAnalysis
		{
			Ticket = $"night-{today.Year}-{today.Season}-{today.Day}",
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			WithRumors = includesGossip,
			Chats = Gossip.TakeTodayInteractions().Where(i => _npcs.IsEnabled(i.Npc) && SocialWeb.PresentInTown(i.Npc, today.Year)).ToList(),
			OutsideNotes = external,
			JournalNotes = secretNotes.Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
			HeartScenes = heartEvents.Where(h => _npcs.IsEnabled(h.NpcName)).ToList(),
			PlainJournal = fallback ?? "",
			TownEvents = happenings.ToList(),
			Facts = facts ?? "",
			Weather = weather
		};
	}

	private DayInput Input(PendingDailyAnalysis p, string? focus) => new DayInput
	{
		Interactions = p.Chats.ToList(),
		Year = p.Year,
		ExternalNotes = p.OutsideNotes.Notes,
		DiaryNotes = p.JournalNotes,
		HeartEvents = p.HeartScenes,
		VillageHappenings = p.TownEvents,
		Focus = p.WithRumors ? GossipFocus.Normalize(focus) : GossipFocus.Normal,
		DayFacts = p.Facts,
		LocalPlayerName = Player
	};

	/// <summary>八卦记在谁身上：互动和心事件涉及的村民。</summary>
	/// <summary>AI 说这件事是哪位村民看见或说出来的：认得出来、又开着 AI 的才算。</summary>
	private List<string>? ToldBy(DayGossip g)
	{
		string who = string.IsNullOrWhiteSpace(g.ToldBy) ? "" : _npcs.Names.Internal(g.ToldBy!.Trim());
		return who.Length > 0 && BuiltInPersonas.VanillaNames.Contains(who, StringComparer.OrdinalIgnoreCase) && _npcs.IsEnabled(who) ? new List<string> { who } : null;
	}

	private static List<string> Sources(PendingDailyAnalysis p) =>
		p.Chats.Select(i => i.Npc).Where(n => !string.IsNullOrWhiteSpace(n)).Concat(p.HeartScenes.Select(h => h.NpcName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	private List<NpcSocialMoment> VillageHappenings(ModConfig c, int today)
	{
		if (!GossipFocus.UsesVillageHappenings(c.RumorSubject) || !c.MeetupsBecomeRumors)
		{
			return new List<NpcSocialMoment>();
		}
		return GossipFocus.SelectHappenings(Web.ForFeed(today, 1).Where(m => m.OnDay < today && _npcs.IsEnabled(m.First) && _npcs.IsEnabled(m.Second)));
	}

	/// <summary>村民之间今天碰上的几对：按社交网的计划，让 AI 写（或直接用现成写法），晚上回来改关系、可能变成八卦。</summary>
	private void QueueSocialMoments(GameDate today, string weather)
	{
		ModConfig c = _config();
		if (!c.AiOn || !c.VillagerMeetupsOn || !c.VillagerBondsShift)
		{
			return;
		}
		List<string> enabled = new List<string>();
		Dictionary<string, string> locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (GameLocation loc in Game1.locations)
		{
			foreach (NPC n in loc.characters)
			{
				if (n != null && !string.IsNullOrWhiteSpace(n.Name))
				{
					locations[n.Name] = loc.Name;
					if (n.IsVillager && _npcs.IsEnabled(n))
					{
						enabled.Add(n.Name);
					}
				}
			}
		}
		List<NpcSocialCandidate> plan = Web.PlanDay(new SocialDayInput
		{
			Today = today,
			UniqueGameId = Game1.uniqueIDForThisGame.ToString(),
			EnabledNpcs = enabled.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
			Locations = locations,
			HasKnownPersonality = n => BuiltInPersonas.HasVanilla(n) || BuiltInPersonas.IsKnownSve(n),
			Money = MoneyBetween
		});
		if (plan.Count == 0)
		{
			return;
		}
		List<(NpcSocialCandidate Candidate, MomentInput? Input)> work = plan.Select(cand => (cand, cand.NoAi ? null : new MomentInput(
			cand,
			_npcs.Persona(cand.First, GameState.FindNpc(cand.First)).Personality,
			_npcs.Persona(cand.Second, GameState.FindNpc(cand.Second)).Personality,
			today.Season,
			today.Day,
			weather,
			c.MeetupSpicePercent,
			Web.MemoryForPair(cand.First, cand.Second, today.TotalDays),
			Gossip.SharedBetween(cand.First, cand.Second)))).ToList();
		Enqueue("村民之间的事", async ct =>
		{
			List<(NpcSocialCandidate Candidate, NpcSocialMoment? Moment)> results = new List<(NpcSocialCandidate, NpcSocialMoment?)>();
			foreach ((NpcSocialCandidate cand, MomentInput? input) in work)
			{
				NpcSocialMoment? m = null;
				try
				{
					m = input == null ? null : await Ai.MomentAsync(input, ct).ConfigureAwait(false);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					_log.Warn($"{cand.First} 和 {cand.Second} 的事没写出来：{ex.Message}");
				}
				results.Add((cand, m));
			}
			return () =>
			{
				ModConfig cfg = _config();
				foreach ((NpcSocialCandidate cand, NpcSocialMoment? written) in results)
				{
					NpcSocialMoment m;
					if (written == null || string.IsNullOrWhiteSpace(written.WhatHappened))
					{
						m = Web.Fallback(cand, today);
					}
					else
					{
						m = written;
						SocialWeb.Complete(m, cand, today, cfg);
					}
					ApplyMoment(m, cfg);
				}
			};
		});
	}

	/// <summary>一件事记完以后（账本拿来让人送东西）。</summary>
	public Action<NpcSocialMoment>? MomentApplied { get; set; }

	/// <summary>两人之间钱上的事，给村民之间的 AI 看。</summary>
	public Func<string, string, string>? MoneyBetween { get; set; }

	/// <summary>账本那边的事直接记进村民之间。</summary>
	public void AddMoment(string a, string b, string archetype, string tone, string summary, bool gossip)
	{
		ModConfig c = _config();
		if (!c.VillagerBondsShift || string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || !_npcs.IsEnabled(a) || !_npcs.IsEnabled(b))
		{
			return;
		}
		GameDate today = GameState.Today;
		if (Web.RecentMoments.Any(m => m.OnDay == today.TotalDays && m.Theme == archetype && m.WhatHappened == summary))
		{
			return;
		}
		string where = GameState.FindNpc(a)?.currentLocation?.Name ?? "Town";
		ApplyMoment(SocialWeb.Direct(a, b, where, archetype, tone, summary, gossip && c.MeetupsBecomeRumors, today), c);
	}

	private void ApplyMoment(NpcSocialMoment m, ModConfig c)
	{
		Web.Apply(m);
		MomentApplied?.Invoke(m);
		Personality.AfterMoment(new SocialMomentEffect(m.First, m.Second, m.OnDay, m.BondShift.Liking, m.BondShift.Trust, m.BondShift.Friction, m.BondShift.Banter, m.Vibe, m.Labels, m.Theme));
		RememberInteraction(m);
		if (c.MeetupsBecomeRumors && m.WorthTelling && c.RumorsOn)
		{
			List<string> who = new[] { m.First, m.Second }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			Gossip.AddVillageMoment(m.Day, m.Season, m.Year, SocialWeb.Describe(m, Display), m.WhatHappened.Trim(), who);
		}
		_log.Debug($"村民之间：{m.First} 和 {m.Second} 在 {m.Place}：{m.WhatHappened}");
	}

	private void RememberInteraction(NpcSocialMoment m)
	{
		if (!_npcs.IsEnabled(m.First) || !_npcs.IsEnabled(m.Second))
		{
			return;
		}
		_recentInteractions.Add(m);
		while (_recentInteractions.Count > MaxRecentInteractions)
		{
			_recentInteractions.RemoveAt(0);
		}
	}

	private bool AppendFallback(string fallback, GameDate today, string? weather)
	{
		if (string.IsNullOrWhiteSpace(fallback))
		{
			return false;
		}
		if (Diary.Has(today))
		{
			Diary.Append(fallback.Trim(), today);
		}
		else
		{
			Diary.Add(fallback.Trim(), today);
		}
		Diary.SetDetails(today, weather, null);
		return true;
	}

	private DayFacts CaptureDayFacts(GameDate today, string weather)
	{
		Func<string, bool> on = _npcs.IsEnabled;
		List<string> birthdays = Utility.getAllVillagers().Where(n => n != null && n.isBirthday() && on(n.Name)).Select(n => Display(n.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		List<string> letters = Letters.DeliveredOn(today.TotalDays).Where(l => on(l.From)).Select(l => string.IsNullOrWhiteSpace(l.Title) ? $"收到{Display(l.From)}的信" : $"收到{Display(l.From)}的信：《{l.Title.Trim()}》").ToList();
		List<string> gifts = Gifts.TodayForDiary(Display, on).ToList();
		PlayerProfile? sheet = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		bool birthday = sheet != null && sheet.HasBirthday && BirthdayRules.IsBirthday(sheet.BornSeason, sheet.BornDay, today.Season, today.Day);
		return new DayFacts(today.Season, today.Day, today.Year, weather, FestivalName(today), birthday, birthdays, letters, gifts, Safe("今天的出游", () => OutingsToday?.Invoke() ?? Array.Empty<string>(), Array.Empty<string>()));
	}

	private static string FestivalName(GameDate today)
	{
		try
		{
			Dictionary<string, string>? dates = Game1.content.Load<Dictionary<string, string>>("Data\\Festivals\\FestivalDates");
			return dates != null && dates.TryGetValue(BirthdayRules.FestivalKey(today.Season, today.Day), out string? name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	// ================= 心事件 =================

	private void CaptureHeartEventBaseline()
	{
		if (Game1.player != null)
		{
			_heartEventsAtDayStart = WorldData.HeartEvents.Where(h => Game1.player.eventsSeen.Contains(h.EventId.ToString())).Select(h => h.EventId).ToHashSet();
		}
	}

	/// <summary>今天新看过的心事件（最多 12 个），取完重新记基线。</summary>
	private IReadOnlyList<HeartEventCandidate> TakeNewHeartEvents()
	{
		if (_heartEventsAtDayStart == null || Game1.player == null)
		{
			return Array.Empty<HeartEventCandidate>();
		}
		HashSet<int> known = WorldData.HeartEvents.Select(h => h.EventId).Where(id => IsReplacedHeartEvent?.Invoke(id) != true).ToHashSet();
		List<int> fresh = Game1.player.eventsSeen.Select(v => int.TryParse(v, out int id) ? id : -1).Where(id => id >= 0 && known.Contains(id) && !_heartEventsAtDayStart.Contains(id)).Distinct().OrderBy(id => id).Take(12).ToList();
		CaptureHeartEventBaseline();
		string player = string.IsNullOrWhiteSpace(Player) ? "Farmer" : Player;
		return fresh.Select(id => WorldData.HeartEvents.First(h => h.EventId == id)).Select(h => new HeartEventCandidate(h.Npc, h.Hearts, h.EventId, h.Story, player)).ToList();
	}

	// ================= 聊天、送礼 =================

	/// <summary>聊完一轮（主线程，好感已经加过）。</summary>
	public void OnTalked(Exchange x) => AfterTalk(x, MoodChange(x.Reaction));

	/// <summary>表白/求婚这一轮：和普通聊天一样记，心情按计入的好感至少 +3。</summary>
	public void OnProposal(Exchange x, int points) => AfterTalk(x, Math.Max(3, points * 2));

	/// <summary>邀约这一轮：和普通聊天一样记，只是没答应、也不值得传的不进八卦（保密的照样写进日记）。</summary>
	public void OnOuting(Exchange x, OutingTurn turn) => AfterTalk(x, turn.Mood, turn.Spread, turn.CountsMilestones);

	/// <summary>今天一起出去过的人（写日记的当天事实）。</summary>
	public Func<IReadOnlyList<string>>? OutingsToday { get; set; }

	private void AfterTalk(Exchange x, int mood, bool spread = true, bool milestones = true)
	{
		if (_store() == null)
		{
			return;
		}
		string npc = x.Npc.Name;
		GameDate today = GameState.Today;
		NpcReaction r = x.Reaction;
		Feelings.AfterExchange(npc, x.HeartsBefore, r, x.PlayerText, today);
		Personality.AfterExchange(npc, x.PlayerText, x.ProposedAction, x.AnsweredLine, r, x.HeartsBefore, ChildSafety.AgeAllowsRomance(npc, x.Npc.Age, x.Npc.datable.Value), today.TotalDays);
		Drama.RecordInfluence(npc, x.Words, x.HeartsBefore, r, today.TotalDays);
		if (milestones)
		{
			Milestones.AfterExchange(npc, _conversations.Count(npc), _relationships.EffectiveHearts(npc), Gifts.EverGaveLoved(npc), r.FriendshipPoints, today);
		}
		if (mood != 0)
		{
			Moods.Adjust(npc, mood);
		}
		if (x.KeepSecret)
		{
			Diary.RecordSecret(Display(npc), x.PlayerText, x.ShownReaction);
		}
		else if (spread)
		{
			Gossip.RecordInteraction(new DailyInteraction
			{
				Npc = npc,
				Farmer = Player,
				Opener = x.OpeningLine,
				Reply = x.PlayerText,
				Comeback = x.ShownReaction,
				Face = r.Emotion
			});
		}
	}

	/// <summary>信使上聊完一轮：记心里的感受、性格变化，交给八卦（保密的写进日记）。不加好感、不动心情。</summary>
	public void OnTexted(NPC npc, string playerText, string lastLine, NpcReaction reaction, string shownReaction, bool keepSecret, int heartsBefore)
	{
		if (_store() == null)
		{
			return;
		}
		string name = npc.Name;
		GameDate today = GameState.Today;
		Feelings.AfterExchange(name, heartsBefore, reaction, playerText, today);
		Personality.AfterExchange(name, playerText, null, lastLine, reaction, heartsBefore, ChildSafety.AgeAllowsRomance(name, npc.Age, npc.datable.Value), today.TotalDays);
		string message = AISV.Core.Messenger.MessengerRules.DiaryMessage(playerText);
		if (keepSecret)
		{
			Diary.RecordSecret(Display(name), message, shownReaction);
			return;
		}
		Gossip.RecordInteraction(new DailyInteraction
		{
			Npc = name,
			Farmer = Player,
			Opener = lastLine,
			Reply = message,
			Comeback = shownReaction,
			Face = reaction.Emotion
		});
	}

	/// <summary>节日上的聊天：只进日记（全镇的事由节日总结传），记心里的感受和心情。</summary>
	public void OnFestivalTalked(string festival, Exchange x)
	{
		if (_store() == null)
		{
			return;
		}
		string npc = x.Npc.Name;
		GameDate today = GameState.Today;
		Feelings.AfterExchange(npc, x.HeartsBefore, x.Reaction, x.PlayerText, today);
		Milestones.AfterExchange(npc, _conversations.Count(npc), _relationships.EffectiveHearts(npc), Gifts.EverGaveLoved(npc), x.Reaction.FriendshipPoints, today);
		int mood = MoodChange(x.Reaction);
		if (mood != 0)
		{
			Moods.Adjust(npc, mood);
		}
		Diary.RecordFestival(festival, Display(npc), x.PlayerText, x.ShownReaction);
	}

	/// <summary>节日上选了邀请：记心里的感受、性格变化、里程碑、日记和那一句回忆，心情好一点。</summary>
	public void OnFestivalInvite(FestivalInviteOutcome o)
	{
		if (_store() == null)
		{
			return;
		}
		string npc = o.Npc.Name;
		GameDate today = GameState.Today;
		NpcReaction r = new NpcReaction { Reaction = o.Decision.Reaction, Emotion = o.Decision.Emotion, FriendshipPoints = o.Decision.FriendshipPoints };
		Feelings.AfterExchange(npc, o.HeartsBefore, r, o.PlayerText, today);
		Personality.AfterExchange(npc, o.PlayerText, $"festival:{o.Festival.Id}:{o.Festival.Action?.Id}", o.AnsweredLine, r, o.HeartsBefore, ChildSafety.AgeAllowsRomance(npc, o.Npc.Age, o.Npc.datable.Value), today.TotalDays);
		Drama.RecordInfluence(npc, o.PlayerText, o.HeartsBefore, r, today.TotalDays);
		Milestones.AfterExchange(npc, _conversations.Count(npc), _relationships.EffectiveHearts(npc), Gifts.EverGaveLoved(npc), o.Decision.FriendshipPoints, today);
		Moods.Adjust(npc, Math.Max(3, o.Points * 2));
		Diary.RecordFestival(o.Festival.Name, Display(npc), o.PlayerText, o.Shown);
		if (!string.IsNullOrWhiteSpace(o.Decision.MemorySummary))
		{
			Feelings.AddKeyMemory(npc, o.Decision.MemorySummary, today);
		}
	}

	/// <summary>节日上没回话：只记进节日日记。</summary>
	public void OnFestivalSkipped(string festival, string npc)
	{
		if (_store() != null)
		{
			Diary.RecordFestival(festival, Display(npc), SkippedReply.Marker, "");
		}
	}

	/// <summary>节日结束：传一条全镇的总结，写进日记；聊过的人里交情最深的过两天来信。</summary>
	public void OnFestivalEnded(AISV.Core.Festivals.FestivalPack festival, IReadOnlyCollection<string> talkedTo)
	{
		GameDate today = GameState.Today;
		Gossip.AddFestival(festival.Id, $"festival:{festival.Id}:{today.Year}", festival.Gossip.Summary, festival.Gossip.Chat, today, festival.Gossip.PromptDays);
		Diary.AddFestivalSummary(festival.Name, festival.Gossip.Summary, today);
		string? writer = talkedTo.Where(n => _npcs.IsEnabled(n) && (Game1.player?.getFriendshipLevelForNPC(n) ?? 0) >= 500).OrderByDescending(n => Game1.player.getFriendshipLevelForNPC(n)).FirstOrDefault();
		if (writer != null)
		{
			RequestEventLetter(writer, $"after_festival_{festival.Id}_{today.Year}", $"{festival.Name}那天你在会场上和农夫说过话。过了这几天，那天的哪个画面还时不时冒出来？就写那个。", 2);
		}
	}

	/// <summary>没回话就走了：当成一次互动记下（晚上整理时知道"农夫听完就走了"）。</summary>
	public void OnSkipped(string npc, string opening)
	{
		if (_store() == null)
		{
			return;
		}
		Gossip.RecordInteraction(new DailyInteraction
		{
			Npc = npc,
			Farmer = Player,
			Opener = opening,
			Reply = SkippedReply.Marker,
			Comeback = "",
			Face = Emotion.Neutral
		});
	}

	/// <summary>聊完心情怎么变：开心类 ≥ +5、生气类 ≤ −5、难过类 ≤ −3、平静不变，其他按得分。</summary>
	internal static int MoodChange(NpcReaction r) => (r.Emotion ?? "").ToLowerInvariant() switch
	{
		"loved" or "happy" or "excited" or "joyful" or "grateful" => Math.Max(5, r.FriendshipPoints * 2),
		"upset" or "angry" or "annoyed" or "frustrated" => Math.Min(-5, r.FriendshipPoints * 2),
		"sad" or "disappointed" or "hurt" => Math.Min(-3, -r.FriendshipPoints),
		"neutral" => 0,
		_ => r.FriendshipPoints
	};

	/// <summary>村民收下礼物（receiveGift 之后）。</summary>
	public void OnGiftReceived(NPC npc, StardewValley.Object item)
	{
		if (_store() == null || string.IsNullOrEmpty(npc.Name))
		{
			return;
		}
		string name = npc.Name;
		GameDate today = GameState.Today;
		string itemName = item.DisplayName ?? item.Name ?? "某样东西";
		int taste;
		try
		{
			taste = npc.getGiftTasteForThisItem(item);
		}
		catch (Exception)
		{
			taste = NPC.gift_taste_neutral;
		}
		string tasteWord = GiftBook.Taste(taste);
		bool birthday = npc.isBirthday();
		Gifts.Record(name, itemName, tasteWord, birthday, today);
		Feelings.GiftReaction(name, itemName, tasteWord, birthday, today);
		string? memory = Gifts.SignificantMemory(name);
		if (memory != null)
		{
			Feelings.AddKeyMemory(name, memory, today);
		}
	}

	private void OnGiftRecorded(GiftRecord gift)
	{
		if (gift.OnBirthday && string.Equals(gift.Liking, "loved", StringComparison.OrdinalIgnoreCase))
		{
			RequestEventLetter(gift.Recipient, $"thanks_birthday_gift_{gift.Year}", $"你过生日那天，农夫送了你{gift.What}，正是你最喜欢的东西。用你自己的方式写信谢谢对方。");
		}
	}

	/// <summary>事件信：能马上写就排进今天的后台活，否则等到期那天早上一起写。</summary>
	public void RequestEventLetter(string npc, string trigger, string context, int delayDays = 0)
	{
		GameDate today = GameState.Today;
		if (Letters.RequestEvent(npc, trigger, context, today, delayDays) && _config().AiOn)
		{
			QueueLetter(new LetterTrigger(npc, trigger, context), today);
		}
	}

	// ================= 农夫生日 =================

	private void PlayerBirthday(GameDate today)
	{
		PlayerProfile? sheet = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		if (sheet == null || !sheet.HasBirthday || !BirthdayRules.IsBirthday(sheet.BornSeason, sheet.BornDay, today.Season, today.Day))
		{
			return;
		}
		List<BirthdayCandidate> candidates = BirthdayCandidates();
		if (candidates.Count == 0)
		{
			return;
		}
		List<string> letterSenders = BirthdayLetterSenders(candidates, today);
		PlanBirthdayTexts(candidates, letterSenders, today);
	}

	private List<string> BirthdayLetterSenders(List<BirthdayCandidate> candidates, GameDate today)
	{
		string trigger = BirthdayRules.LetterTrigger(today.Year);
		List<string> sent = Letters.Queued.Where(l => l.Cause == trigger).Select(l => l.From).ToList();
		if (sent.Count > 0 || !_config().MailOn)
		{
			return sent;
		}
		int count = BirthdayRules.SenderCount(_random.NextDouble());
		IReadOnlyList<BirthdayCandidate> senders = BirthdayRules.PickLetterSenders(candidates, count, Enumerable.Range(0, 3).Select(_ => _random.NextDouble()).ToList());
		string player = Player;
		foreach (BirthdayCandidate s in senders)
		{
			BirthdayTone tone = BirthdayRules.ToneOf(s.Spouse, s.Partner);
			string body = BirthdayLetters.Body(tone, BirthdayRules.VariantIndex(_random.NextDouble()), player);
			if (body.Length == 0 || !_npcs.IsEnabled(s.NpcName))
			{
				continue;
			}
			NPC? npc = GameState.FindNpc(s.NpcName);
			if (npc == null)
			{
				continue;
			}
			int gift = Safe("挑生日礼物", () => LetterGifts.Pick(npc, Letters.Queued, _config()), -1);
			QueuedLetter? letter = Letters.Add(s.NpcName, trigger, BirthdayLetters.Subject(player), body, gift, today);
			if (letter == null)
			{
				continue;
			}
			AttachAiGift(letter, $"{trigger}：{letter.Title}");
			string itemName = LetterGiftName(letter);
			Feelings.AddKeyMemory(s.NpcName, itemName.Length > 0 ? $"{Dates.Label(today)}，我给{player}寄了生日信，还附上了{itemName}。" : $"{Dates.Label(today)}，我给{player}寄了生日信。", today);
			_log.Info($"今天是 {player} 的生日：{s.NpcName} 寄来生日信。");
			sent.Add(s.NpcName);
		}
		return sent;
	}

	/// <summary>生日当天上午要发的祝福消息（信使取走后按时间发）。</summary>
	private void PlanBirthdayTexts(List<BirthdayCandidate> candidates, IEnumerable<string> letterSenders, GameDate today)
	{
		if (_birthdayTextDay == today.TotalDays)
		{
			return;
		}
		_birthdayTextDay = today.TotalDays;
		_birthdayTexts.Clear();
		IReadOnlyList<BirthdayCandidate> senders = BirthdayRules.PickTextSenders(candidates, letterSenders, BirthdayRules.SenderCount(_random.NextDouble()), Enumerable.Range(0, 3).Select(_ => _random.NextDouble()).ToList());
		IReadOnlyList<int> times = BirthdayRules.PlanTextTimes(senders.Count, senders.Select(_ => _random.NextDouble()).ToList());
		for (int i = 0; i < senders.Count; i++)
		{
			string text = BirthdayLetters.Text(BirthdayRules.ToneOf(senders[i].Spouse, senders[i].Partner), BirthdayRules.VariantIndex(_random.NextDouble()), Player);
			if (text.Length > 0)
			{
				_birthdayTexts.Add((senders[i].NpcName, text, times[i]));
			}
		}
		if (_birthdayTexts.Count > 0)
		{
			_log.Debug("生日祝福消息安排好了：" + string.Join("、", _birthdayTexts.Select(t => $"{t.Npc} {t.Time}")));
		}
	}

	/// <summary>取走今天的生日祝福消息安排（取过就清空）。</summary>
	public IReadOnlyList<(string Npc, string Text, int Time)>? TakeBirthdayTexts()
	{
		if (_birthdayTextDay != GameState.Today.TotalDays || _birthdayTexts.Count == 0)
		{
			return null;
		}
		List<(string, string, int)> plan = _birthdayTexts.ToList();
		_birthdayTexts.Clear();
		return plan;
	}

	public void RecordBirthdayTexted(string npc)
	{
		GameDate today = GameState.Today;
		if (_textedDay != today.TotalDays)
		{
			_textedDay = today.TotalDays;
			_textedToday.Clear();
		}
		_textedToday.Add(npc);
		Feelings.AddKeyMemory(npc, $"{Dates.Label(today)}，我用信使给{Player}发了生日祝福。", today);
	}

	private bool TextedToday(string npc) => _textedDay == GameState.Today.TotalDays && _textedToday.Contains(npc);

	private List<BirthdayCandidate> BirthdayCandidates()
	{
		Farmer player = Game1.player;
		Dictionary<string, BirthdayCandidate> found = new Dictionary<string, BirthdayCandidate>(StringComparer.OrdinalIgnoreCase);
		foreach (NPC n in Utility.getAllCharacters())
		{
			if (n == null || !n.IsVillager || string.IsNullOrWhiteSpace(n.Name) || found.ContainsKey(n.Name) || !player.friendshipData.TryGetValue(n.Name, out Friendship? f) || f == null || !_npcs.IsEnabled(n.Name))
			{
				continue;
			}
			bool spouse = f.IsMarried() && !f.IsRoommate() && string.Equals(player.spouse, n.Name, StringComparison.OrdinalIgnoreCase);
			bool partner = !spouse && (f.IsDating() || f.IsEngaged());
			found[n.Name] = new BirthdayCandidate(n.Name, player.getFriendshipHeartLevelForNPC(n.Name), AISV.Core.Messenger.MessengerRules.HasPhone(n.Name), partner, spouse);
		}
		return found.Values.ToList();
	}

	/// <summary>农夫生日当天，这位村民的开场白提示（寄过的信、附的礼物）。不是生日时为空。</summary>
	public string BirthdayOpening(string npc)
	{
		(bool Today, QueuedLetter? Letter) b = BirthdayState(npc);
		return b.Today ? BirthdayRules.OpeningBlock(Display(npc), Player, LetterGiftName(b.Letter), b.Letter != null, TextedToday(npc)) : "";
	}

	/// <summary>农夫生日当天，回复时额外的要求。不是生日时为空。</summary>
	public string BirthdayReply(string npc)
	{
		(bool Today, QueuedLetter? Letter) b = BirthdayState(npc);
		return b.Today ? BirthdayRules.ReplyBlock(Display(npc), Player, LetterGiftName(b.Letter), b.Letter != null, TextedToday(npc)) : "";
	}

	public bool IsPlayerBirthday()
	{
		PlayerProfile? sheet = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		GameDate today = GameState.Today;
		return sheet != null && sheet.HasBirthday && BirthdayRules.IsBirthday(sheet.BornSeason, sheet.BornDay, today.Season, today.Day);
	}

	private (bool Today, QueuedLetter? Letter) BirthdayState(string npc)
	{
		if (string.IsNullOrWhiteSpace(npc) || !IsPlayerBirthday())
		{
			return (false, null);
		}
		return (true, Letters.Find(npc, BirthdayRules.LetterTrigger(Game1.year)));
	}

	// ================= 提示词块 =================

	/// <summary>"最近和别的村民之间的事"块（农夫不在场的小事）。</summary>
	public string InteractionsBlock(string npc)
	{
		List<NpcSocialMoment> list = _recentInteractions.Where(m => (string.Equals(m.First, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Second, npc, StringComparison.OrdinalIgnoreCase)) && _npcs.IsEnabled(m.First) && _npcs.IsEnabled(m.Second)).ToList();
		if (list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("\n【最近和其他村民之间的事】");
		foreach (NpcSocialMoment m in list)
		{
			string other = string.Equals(m.First, npc, StringComparison.OrdinalIgnoreCase) ? m.Second : m.First;
			sb.AppendLine($"- 和{Display(other)}（{Places.Name(m.Place)}）：{m.WhatHappened}");
		}
		sb.AppendLine("合适的话可以自然提起这些农夫不在场时发生的事，但别说得像农夫当时也在。");
		return sb.ToString();
	}

	// ================= 存档切换 =================

	public void Reset()
	{
		Moods.Reset();
		Gossip.Reset();
		Feelings.Reset();
		Gifts.Reset();
		Diary.Reset();
		Personality.Reset();
		Web.Reset();
		Drama.Reset();
		_recentInteractions.Clear();
		_heartEventsAtDayStart = null;
		_retryRunning = false;
		_nightRunning = false;
		LastRetryMessage = "";
		_birthdayTexts.Clear();
		_birthdayTextDay = -1;
		_textedToday.Clear();
		_textedDay = -1;
		Notices.Clear();
	}

	// ================= 信箱 =================

	/// <summary>信里换成一件 AI 礼物：成功时去掉游戏的物品附件；AI 礼物关着或没东西可送时保留。</summary>
	private void AttachAiGift(QueuedLetter letter, string reason)
	{
		string? id = Safe("备信里的礼物", () => AttachGift?.Invoke(letter.From, letter.Key, reason), null);
		if (id != null)
		{
			letter.GiftRef = id;
			letter.EnclosedId = -1;
			Letters.Changed();
		}
	}

	/// <summary>信里附的东西叫什么：AI 礼物的名字或游戏物品名；没附东西为空串。</summary>
	public string LetterGiftName(QueuedLetter? letter)
	{
		if (letter == null)
		{
			return "";
		}
		string? ai = GiftName?.Invoke(letter.GiftRef);
		if (!string.IsNullOrWhiteSpace(ai))
		{
			return ai;
		}
		if (!string.IsNullOrWhiteSpace(letter.Enclosed))
		{
			string name = ItemRegistry.GetData(letter.Enclosed)?.DisplayName ?? letter.Enclosed;
			return letter.EnclosedCount > 1 ? $"{name} ×{letter.EnclosedCount}" : name;
		}
		return letter.EnclosedId > 0 ? LetterGifts.Name(letter.EnclosedId) : "";
	}

	/// <summary>写好的一封信：排进信件本，第二天早上进信箱；同一位同一个缘由只寄一次。</summary>
	public bool QueueReadyLetter(string npc, string trigger, string subject, string body)
	{
		if (!_config().MailOn || string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(body) || !_npcs.IsEnabled(npc) || Game1.getCharacterFromName(npc) == null)
		{
			return false;
		}
		return Letters.Add(npc, trigger, subject, body, -1, GameState.Today) != null;
	}

	/// <summary>伴侣剧情的信：今天就放进信箱，不占普通信件的冷却；有礼物理由时附一件 AI 礼物。</summary>
	public (string Key, string? GiftId)? PostStoryLetter(string npc, string trigger, string subject, string body, string? giftReason)
	{
		GameDate today = GameState.Today;
		QueuedLetter? letter = Letters.Find(npc, trigger) ?? Letters.Add(npc, trigger, subject, body, -1, today, cooldown: false);
		if (letter == null)
		{
			return null;
		}
		if (giftReason != null && letter.GiftRef == null)
		{
			AttachAiGift(letter, giftReason);
		}
		letter.Sent = true;
		letter.SentOn = today.TotalDays;
		Letters.Changed();
		PutInMailbox(letter);
		return (letter.Key, letter.GiftRef);
	}

	/// <summary>委托的信：今天就放进信箱，不占普通信件的冷却，可以附一样东西。</summary>
	public string? PostErrandLetter(string npc, string trigger, string subject, string body, string? item, int count)
	{
		GameDate today = GameState.Today;
		QueuedLetter? letter = Letters.Find(npc, trigger) ?? Letters.Add(npc, trigger, subject, body, -1, today, cooldown: false);
		if (letter == null)
		{
			return null;
		}
		letter.Enclosed = item;
		letter.EnclosedCount = Math.Max(1, count);
		letter.Sent = true;
		letter.SentOn = today.TotalDays;
		Letters.Changed();
		PutInMailbox(letter);
		return letter.Key;
	}

	/// <summary>调试：马上寄一封带礼物的信放进信箱。</summary>
	public string QueueTestGiftLetter(string npc)
	{
		GameDate today = GameState.Today;
		QueuedLetter? letter = Letters.Add(npc, "debug_gift_" + today.TotalDays + "_" + Letters.Queued.Count(), "一点小心意", "路过的时候想起你，就顺手寄了点东西。", -1, today);
		if (letter == null)
		{
			return "没写成";
		}
		AttachAiGift(letter, "路过时想起了农夫");
		letter.Sent = true;
		letter.SentOn = today.TotalDays;
		Letters.Changed();
		PutInMailbox(letter);
		return letter.GiftRef == null ? "信放进信箱了（没备上礼物）" : $"信放进信箱了，附礼物 {letter.GiftRef}";
	}

	/// <summary>Data/mail 里加上所有信的内容（署名用显示名）。</summary>
	public void EditMail(IDictionary<string, string> data)
	{
		if (_store() == null)
		{
			return;
		}
		foreach (QueuedLetter l in Letters.Queued)
		{
			data[l.Key] = LetterBook.MailText(l, Display(l.From));
		}
	}

	private static bool InMailOrRead(string key) => Game1.player == null || Game1.player.mailReceived.Contains(key) || Game1.player.mailbox.Contains(key);

	private void PutInMailbox(QueuedLetter l)
	{
		string key = l.Key;
		if (Game1.player.mailReceived.Contains(key))
		{
			return;
		}
		if (!Game1.player.mailbox.Contains(key))
		{
			Game1.player.mailbox.Add(key);
		}
		Dictionary<string, string> mail = Game1.content.Load<Dictionary<string, string>>("Data\\mail");
		if (!mail.ContainsKey(key))
		{
			mail[key] = LetterBook.MailText(l, Display(l.From));
		}
	}

	// ================= 小工具 =================

	private void ShowPendingGossip()
	{
		string? pending = Gossip.TakePendingChatMessage();
		if (string.IsNullOrEmpty(pending))
		{
			return;
		}
		Game1.delayedActions.Add(new DelayedAction(2000, () => Notices.ShowNow(pending)));
	}

	/// <summary>八卦正文：马上显示一行，并清掉"待显示"。</summary>
	private void ShowGossipLine(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return;
		}
		Notices.ShowNow(line);
		Gossip.ClearPending(line);
	}

	private static string DayWeather() => Game1.isLightning ? "Stormy" : Game1.isRaining ? "Rainy" : Game1.isSnowing ? "Snowy" : "Sunny";

	private static bool IsNpcBirthday(string name, GameDate today)
	{
		NPC? npc = Game1.getCharacterFromName(name);
		return npc != null && npc.Birthday_Day == today.Day && string.Equals(npc.Birthday_Season, today.Season, StringComparison.OrdinalIgnoreCase);
	}

	private TimeSpan JobTimeout => TimeSpan.FromSeconds(Math.Max(90, _config().NightlyWaitSeconds * 2 + 30));

	/// <summary>全村共用的数据：联机时只有房主写。</summary>
	public static readonly string[] VillageDocuments = { GossipBook.PromptPath, GossipBook.FeedPath, GossipBook.StatePath, LetterBook.Path, SocialWeb.Path, PersonalityBook.Path, StoryArcBook.Path, ResolvedLore.Path, ExpulsionBook.Path, ShopDetachBook.Path };

	/// <summary>
	/// 晚上和早上的后台结果会写到的文件。结果常在游戏存档之后才回来（属于已经存下的那一天），
	/// 写回后马上写盘，不等下一次存档——否则第二天没睡觉就退出，昨晚的八卦和日记就丢了。
	/// </summary>
	private static readonly string[] BackgroundDocuments =
	{
		GossipBook.PromptPath, GossipBook.FeedPath, GossipBook.StatePath, PlayerDiary.Path, DailyRetry.Path,
		LetterBook.Path, AiGiftLog.Path, SocialWeb.Path, PersonalityBook.Path, SocialDrama.Path, EmotionalMemory.Path, StoryArcBook.Path, ResolvedLore.Path, ExpulsionBook.Path, ShopDetachBook.Path
	};

	/// <summary>后台活一件一件做；做完回主线程写回并写盘（存档换了就丢掉）。</summary>
	internal void Enqueue(string name, Func<CancellationToken, Task<Action?>> work, Action? onFailed = null)
	{
		DocumentStore? store = _store();
		TimeSpan timeout = JobTimeout;
		Task.Run(async () =>
		{
			await _jobGate.WaitAsync().ConfigureAwait(false);
			try
			{
				using CancellationTokenSource cts = new CancellationTokenSource(timeout);
				if (!ReferenceEquals(_store(), store))
				{
					return;
				}
				Action? apply = await work(cts.Token).ConfigureAwait(false);
				if (apply != null)
				{
					MainThread.Run(() =>
					{
						if (!ReferenceEquals(_store(), store))
						{
							_log.Warn($"{name}：存档已经换了，结果丢掉。");
							return;
						}
						apply();
						Persist(store);
					});
				}
			}
			catch (Exception ex)
			{
				_log.Warn($"{name}出错：{ex.Message}");
				if (onFailed != null)
				{
					MainThread.Run(() =>
					{
						if (ReferenceEquals(_store(), store))
						{
							onFailed();
							Persist(store);
						}
					});
				}
			}
			finally
			{
				_jobGate.Release();
			}
		});
	}

	/// <summary>后台回来的结果马上写盘（主线程上调用）。</summary>
	internal void SaveBackgroundResults() => Persist(_store());

	private void Persist(DocumentStore? store)
	{
		try
		{
			store?.Commit(BackgroundDocuments, Context.IsMainPlayer ? null : VillageDocuments);
		}
		catch (Exception ex)
		{
			_log.Warn($"后台结果写盘出错（下次存档再写）：{ex.Message}");
		}
	}

	private void Safe(string what, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			_log.Error($"{what}出错：{ex.Message}");
		}
	}

	private T Safe<T>(string what, Func<T> action, T fallback)
	{
		try
		{
			return action();
		}
		catch (Exception ex)
		{
			_log.Debug($"{what}出错：{ex.Message}");
			return fallback;
		}
	}
}
