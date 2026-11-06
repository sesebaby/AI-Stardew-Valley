using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Awareness;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Favors;
using AISV.Core.Festivals;
using AISV.Core.Outings;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Scenes;
using AISV.Core.World;

namespace AISV.Core.Dialogue;

/// <summary>一位村民今天的对话状态（只在内存里，新的一天清空）。</summary>
public sealed class TodayDialogue
{
	/// <summary>生成这句开场白时参考的游戏原台词（回复时作为弱参考）。</summary>
	public string GameLineHint { get; set; } = "";

	public string? HeldFace { get; set; }

	/// <summary>配偶对话中途关掉时留着的上一句反应和剩余轮数，再点配偶时接着聊。</summary>
	public string? HeldReply { get; set; }

	public int HeldTurns { get; set; }

	public string Line { get; set; } = "";

	/// <summary>配偶的这场对话已经算进每天的场数。</summary>
	public bool Logged { get; set; }

	public string Npc { get; init; } = "";

	/// <summary>配偶在开场白里主动约了出去，等农夫回答（回答前不开回复框）。</summary>
	public OutingDecision? OpenOffer { get; set; }

	public bool Spent { get; set; }

	/// <summary>这场对话已经有过回复（或被跳过）。</summary>
	public bool SpokeToday { get; set; }

	public bool WithSpouse { get; set; }

	public void PreservePendingSpouseResponse(string? dialogue, int remainingTurns, string? emotion = null)
	{
		if (WithSpouse && SpokeToday && !Logged && remainingTurns > 0 && !string.IsNullOrWhiteSpace(dialogue))
		{
			HeldReply = dialogue;
			HeldFace = emotion;
			HeldTurns = remainingTurns;
		}
	}

	public void ClearPendingResponse()
	{
		HeldReply = null;
		HeldFace = null;
		HeldTurns = 0;
	}
}

/// <summary>
/// 一次对话要用的全部材料。游戏层在主线程上取好游戏状态，各子系统交出自己的提示词块；没有的块留空。
/// </summary>
public sealed class DialogueMaterial
{
	public string NpcName { get; init; } = "";

	/// <summary>游戏语言里的名字。提示词用它：内部名是英文，中文对话里会被模型原样说出来。</summary>
	public string DisplayName { get; init; } = "";

	public string PromptName => string.IsNullOrWhiteSpace(DisplayName) ? NpcName : DisplayName;

	/// <summary>玩家改写过这位村民的角色卡。</summary>
	public bool CardRewritten { get; init; }

	public string PlayerName { get; init; } = "";

	/// <summary>有效心数（启用敌意时扣掉敌意心数）。</summary>
	public int Hearts { get; init; }

	/// <summary>游戏里的好感心数（回复评估里告诉 AI 的心数）。</summary>
	public int RawHearts { get; init; }

	public int EnmityHearts { get; init; }

	public string Season { get; init; } = "spring";

	public int Day { get; init; } = 1;

	public int Year { get; init; } = 1;

	public string Weather { get; init; } = "";

	public string Location { get; init; } = "";

	public bool IsSpouse { get; init; }

	public bool IsAdultRomanticPartner { get; init; }

	public string Personality { get; init; } = "";

	/// <summary>孩子或写得像孩子（游戏层按名单、年龄和人设算好）。</summary>
	public bool IsChildCoded { get; init; }

	public string PersonalityEvolution { get; init; } = "";

	/// <summary>开场白用的"关于农夫"块（亲疏按有效心数算）。</summary>
	public string PlayerProfile { get; init; } = "";

	/// <summary>回复评估用的"关于农夫"块（亲疏按游戏好感心数算）。</summary>
	public string ReplyPlayerProfile { get; init; } = "";

	public string GossipMemory { get; init; } = "";

	/// <summary>"你亲眼见过的事"（可能带着想说的心事）。</summary>
	public string Awareness { get; init; } = "";

	public string TodayInteractions { get; init; } = "";

	/// <summary>世界背景 + 几天没聊 + 各功能的一句话提示。</summary>
	public string WorldContext { get; init; } = "";

	public string ModContext { get; init; } = "";

	public string LocationContext { get; init; } = "";

	public string Mood { get; init; } = "";

	public bool LowMood { get; init; }

	public bool HighMood { get; init; }

	public string EmotionalProfile { get; init; } = "";

	public string Gift { get; init; } = "";

	public string Events { get; init; } = "";

	public string Milestones { get; init; } = "";

	public string NpcInteractions { get; init; } = "";

	public string Promises { get; init; } = "";

	public string Favors { get; init; } = "";

	public string ItemRequest { get; init; } = "";

	public string Arc { get; init; } = "";

	public string SocialDrama { get; init; } = "";

	public string VillageState { get; init; } = "";

	public string CanonicalLore { get; init; } = "";

	/// <summary>游戏原来这一刻要说的台词（成人包改过的村民不给）。</summary>
	public string VanillaLine { get; init; } = "";

	/// <summary>调用方指定的开场方向（例如从别的功能跳过来）；有它时不走心事件后续和故事弧后续台词。</summary>
	public string OpeningFocus { get; init; } = "";

	/// <summary>回复 JSON 里提议层、AI 礼物要加的字段说明。</summary>
	public string ProposalJson { get; init; } = "";

	public bool AdultRelationshipDrama { get; init; }

	public IReadOnlySet<int> EventsSeen { get; init; } = new HashSet<int>();

	public Func<int, bool>? IsReplacedHeartEvent { get; init; }

	/// <summary>回复字数另有上限（如信使）时填；0 = 用默认的回复字数。</summary>
	public int MaxReplyChars { get; init; }

	/// <summary>农夫生日当天：回复时额外的说明（放进回复提示词）。平时为空。</summary>
	public string ReplyBirthday { get; init; } = "";

	/// <summary>记进对话记录时给这一轮加的说明（比如"那天是农夫生日"）。</summary>
	public string? ReplyHistoryNote { get; init; }

	/// <summary>节日上的对话：是哪个节日。平时为 null。</summary>
	public FestivalPack? Festival { get; init; }

	/// <summary>信使上的文字聊天（不是当面）。</summary>
	public bool Messenger { get; init; }

	/// <summary>回复提示词里附加的场合说明（信使的照片、深夜、生日等）。</summary>
	public string ChannelSection { get; init; } = "";

	public string ContextType => Festival != null ? "festival" : Messenger ? AISV.Core.Messenger.MessengerRules.ContextType : "daily";

	public string ContextId => Festival?.Id ?? (Messenger ? AISV.Core.Messenger.MessengerRules.ContextId : "");

	public string ContextName => Festival?.Name ?? (Messenger ? "信使" : "");
}

public enum OpeningKind
{
	Cached,
	PostResolution,
	FirstMeeting,
	HeartEventFollowup,
	Regular
}

public sealed record OpeningResult(string Dialogue, OpeningKind Kind, OpeningTopic Topic);

public sealed record ReplyResult(NpcReaction Reaction, string CurrentLine, string DisplayText);

/// <summary>
/// 日常对话：生成开场白、评估玩家的回复、记住今天每位村民聊到哪了。
/// 生成和评估在后台线程跑；改记录（<see cref="CommitReply"/> 等）在主线程调。
/// </summary>
public sealed class DialogueEngine
{
	public const string OpeningRequest = "dialogue_generation";

	public const string FirstMeetingRequest = "dialogue_minimal";

	public const string HeartEventRequest = "heart_event_followup";

	public const string ReactionTemplate = "seen_and_heard";

	public const string ReplyRequest = "evaluate_response";

	public const string ItemRequestPlanRequest = "item_request_plan";

	public const string ItemRequestLinesRequest = "item_request_lines";

	public const string ProposalRequest = "relationship_proposal";

	public const string OutingRequest = "invite_out_proposal";

	public const string OutingLinesRequest = "invite_out_scene_lines";

	public const string OverheardRequest = "scene_overheard_exchange";

	public const int OpeningTokens = 3072;

	public const int ReplyTokens = 4096;

	public const double Temperature = 0.9;

	private readonly Func<ModConfig> _config;

	private readonly IAiSender _ai;

	private readonly PromptTemplates _templates;

	private readonly ConversationLog _log;

	private readonly RecentLines _recent;

	private readonly ILog _diag;

	private readonly Func<string> _language;

	private readonly OpeningTopicPicker _picker;

	private readonly object _sync = new object();

	private readonly Dictionary<string, TodayDialogue> _daily = new Dictionary<string, TodayDialogue>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, TodayDialogue> _festival = new Dictionary<string, TodayDialogue>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, TodayDialogue> _today;

	private readonly HashSet<string> _generating = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public DialogueEngine(Func<ModConfig> config, IAiSender ai, PromptTemplates templates, ConversationLog log, RecentLines recent, ILog diag, Func<string> language, OpeningTopicPicker? picker = null)
	{
		_config = config;
		_ai = ai;
		_templates = templates;
		_log = log;
		_recent = recent;
		_diag = diag;
		_language = language;
		_picker = picker ?? new OpeningTopicPicker();
		_today = _daily;
	}

	/// <summary>故事弧结局后要说的台词（第 5 期接入）：有就直接用它当今天的开场白。</summary>
	public Func<string, string?>? PostResolutionLine { get; set; }

	/// <summary>物品请求：能不能托、方案选项、定下来、开口后登记。</summary>
	public IItemRequestDesk? ItemRequests { get; set; }

	public IOutingDesk? Outings { get; set; }

	public bool IsGenerating
	{
		get
		{
			lock (_sync)
			{
				return _generating.Count > 0;
			}
		}
	}

	public bool IsGeneratingFor(string npc)
	{
		lock (_sync)
		{
			return _generating.Contains(npc);
		}
	}

	public TodayDialogue? State(string npc)
	{
		lock (_sync)
		{
			return _today.TryGetValue(npc, out TodayDialogue? s) ? s : null;
		}
	}

	public void StartNewDay()
	{
		lock (_sync)
		{
			_daily.Clear();
			_festival.Clear();
			_today = _daily;
		}
	}

	/// <summary>回标题：当天状态和防重复记忆都丢掉，没保存的对话不能带进下次读档。</summary>
	public void Reset()
	{
		StartNewDay();
		_recent.Clear();
	}

	/// <summary>节日上的对话单独算：进节日换一套状态，出来接着用当天的日常状态。</summary>
	public void EnterFestival()
	{
		lock (_sync)
		{
			_festival.Clear();
			_today = _festival;
		}
	}

	public void LeaveFestival()
	{
		lock (_sync)
		{
			_festival.Clear();
			_today = _daily;
		}
	}

	private int MaxSpouseConversations => Math.Clamp(_config().SpouseTalksDaily, 1, 10);

	/// <summary>今天还能不能开始一场新的日常对话：没聊过可以；聊过的只有配偶在场数内可以再来。</summary>
	public bool CanStartDaily(string npc, bool isSpouse, GameDate today)
	{
		TodayDialogue? s = State(npc);
		if (s == null || !s.SpokeToday)
		{
			return true;
		}
		return isSpouse && s.Logged && _log.CompletedSpouseConversations(npc, today) < MaxSpouseConversations;
	}

	/// <summary>今天和这位村民的对话已经有状态（聊完了、或有生成好的开场白）。</summary>
	public bool HasDailyState(string npc, bool isSpouse, GameDate today)
	{
		TodayDialogue? s = State(npc);
		if (s == null)
		{
			return false;
		}
		if (s.SpokeToday)
		{
			return !CanStartDaily(npc, isSpouse, today);
		}
		return s.Spent || !string.IsNullOrEmpty(s.Line);
	}

	/// <summary>配偶对话中途被关掉、还有剩余轮数：可以接着聊。</summary>
	public bool CanResume(string npc, bool isSpouse)
	{
		TodayDialogue? s = State(npc);
		return isSpouse && s != null && s.WithSpouse && s.SpokeToday && !s.Logged && !string.IsNullOrWhiteSpace(s.HeldReply) && s.HeldTurns > 0;
	}

	/// <summary>生成好但没显示出来（玩家走开了）的开场白：丢掉，下次重新生成。</summary>
	public bool DiscardUnshownOpening(string npc, string dialogue)
	{
		if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(dialogue))
		{
			return false;
		}
		lock (_sync)
		{
			if (!_today.TryGetValue(npc, out TodayDialogue? s) || s.SpokeToday || !string.Equals(s.Line?.Trim(), dialogue.Trim(), StringComparison.Ordinal))
			{
				return false;
			}
			_today.Remove(npc);
		}
		_log.ClearCachedDialogue(npc);
		return true;
	}

	public void Invalidate(string npc)
	{
		lock (_sync)
		{
			_today.Remove(npc);
		}
		_log.ClearCachedDialogue(npc);
	}

	/// <summary>
	/// 今天的开场白。已经生成过的直接给；今天聊完了（配偶没到场数上限除外）返回 null；同一位村民同时只生成一个。
	/// 顺序：故事弧后续台词 → 新心事件后续（≥4 心）→ 初次见面（没有记录且 ≤2 心）→ 按方向生成。
	/// </summary>
	public async Task<OpeningResult?> OpeningAsync(DialogueMaterial m, GameDate today, CancellationToken ct = default)
	{
		if (ct.IsCancellationRequested)
		{
			return null;
		}
		string npc = m.NpcName;
		bool hasFocus = !string.IsNullOrWhiteSpace(m.OpeningFocus);
		lock (_sync)
		{
			if (_generating.Contains(npc))
			{
				_diag.Debug($"{npc} 的开场白正在生成，这次不重复发。");
				return null;
			}
			if (_today.TryGetValue(npc, out TodayDialogue? state))
			{
				if (state.SpokeToday)
				{
					if (!m.IsSpouse || !state.Logged || _log.CompletedSpouseConversations(npc, today) >= MaxSpouseConversations)
					{
						return null;
					}
					_today.Remove(npc);
					_log.ClearCachedDialogue(npc);
					state = null;
				}
				if (state != null && !string.IsNullOrEmpty(state.Line))
				{
					state.Spent = true;
					return new OpeningResult(state.Line, OpeningKind.Cached, OpeningTopic.General);
				}
			}
			string? post = hasFocus ? null : PostResolutionLine?.Invoke(npc);
			if (!string.IsNullOrEmpty(post))
			{
				_today[npc] = new TodayDialogue { Npc = npc, Line = post, Spent = true, WithSpouse = m.IsSpouse };
				return new OpeningResult(post, OpeningKind.PostResolution, OpeningTopic.General);
			}
			_generating.Add(npc);
		}
		try
		{
			(string? text, OpeningKind kind, OpeningTopic topic, ItemRequestCandidate? request, string line1, string line2, OutingDecision? offer, string handover) = await GenerateOpeningAsync(m, today, hasFocus, ct).ConfigureAwait(false);
			if (ct.IsCancellationRequested)
			{
				_diag.Debug($"{npc} 的开场白回来晚了（超时或被取消），丢掉。");
				return null;
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				_diag.Warn($"{npc} 的开场白是空的。");
				return null;
			}
			text = Tidy(text);
			_recent.RecordOpening(npc, text);
			lock (_sync)
			{
				_today[npc] = new TodayDialogue { Npc = npc, Line = text, GameLineHint = m.VanillaLine, Spent = true, WithSpouse = m.IsSpouse, OpenOffer = offer };
			}
			_log.SetCachedDialogue(npc, text);
			if (request != null)
			{
				ItemRequests?.Activate(request, text, line1, line2, handover);
			}
			_diag.Debug($"{npc} 的开场白（{kind}/{topic}）：{text}");
			return new OpeningResult(text, kind, topic);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_diag.Error($"生成 {npc} 的开场白时出错：{ex.Message}");
			return null;
		}
		finally
		{
			lock (_sync)
			{
				_generating.Remove(npc);
			}
		}
	}

	private sealed record Generated(string? Text, OpeningKind Kind, OpeningTopic Topic, ItemRequestCandidate? Request = null, string Line1 = "", string Line2 = "", OutingDecision? Offer = null, string Handover = "");

	private async Task<Generated> GenerateOpeningAsync(DialogueMaterial m, GameDate today, bool hasFocus, CancellationToken ct)
	{
		string npc = m.NpcName;
		ModConfig config = _config();
		if (_templates.Raw("dialogue_opening") == null || _templates.Raw("dialogue_first_meeting") == null || _templates.Raw(HeartEventRequest) == null)
		{
			_diag.Error("缺少对话提示词模板，这次不发请求。");
			return new Generated(null, OpeningKind.Regular, OpeningTopic.General);
		}
		IReadOnlyList<ConversationEntry> history = _log.Recent(npc, 10);
		int todayNumber = _log.Recent(npc, 20).Count(e => e.IsOn(today) && string.Equals(e.ContextOrDaily, "daily", StringComparison.OrdinalIgnoreCase)) + 1;
		string historyText = HistoryText(npc, history, today, m.PlayerName);
		string canon = DialoguePrompts.CanonOpening(m.VanillaLine, config.FeedGameLineToAi);
		string focus = hasFocus ? m.OpeningFocus.Trim() : "";
		string personality = PersonaPrompt.PersonalityBlock(npc, m.Personality);
		string usage = PersonaPrompt.UsageRules(npc, m.CardRewritten);
		string replyChars = _ai.ReplyChars.ToString();

		// 新看过的心事件（和上次聊天时比），够 4 心就先聊它。
		List<int> seenNow = WorldFacts.SeenHeartEvents(npc, m.EventsSeen, m.IsReplacedHeartEvent).Select(e => e.EventId).ToList();
		IReadOnlyList<int> seenBefore = _log.HeartEventsSeen(npc);
		List<int> fresh = seenNow.Where(id => !seenBefore.Contains(id)).ToList();
		if (!hasFocus && fresh.Count > 0 && m.Hearts >= 4)
		{
			string context = DialoguePrompts.HeartEventContext(npc, fresh, seenNow.ToHashSet(), m.IsReplacedHeartEvent);
			_diag.Info($"{npc} 有 {fresh.Count} 个新的心事件：{context.Split('\n')[0]}");
			string system = _templates.Fill(HeartEventRequest, new Dictionary<string, string?>
			{
				["VILLAGER"] = m.PromptName,
				["NATURE"] = personality,
				["FARMER_NAME"] = m.PlayerName,
				["HEART_COUNT"] = m.Hearts.ToString(),
				["SEASON_NOW"] = HistoryFormatter.SeasonName(m.Season),
				["DAY_NUM"] = m.Day.ToString(),
				["YEAR_NUM"] = m.Year.ToString(),
				["HEART_SCENE"] = context,
				["LOG_TEXT"] = historyText,
				["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.ModContext, m.LocationContext, m.CanonicalLore, m.SocialDrama, m.VillageState, m.TodayInteractions, canon, focus),
				["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
				["PLAY_THE_ROLE"] = usage,
				["MOOD_NOW"] = m.Mood,
				["HOW_THEY_FEEL"] = m.EmotionalProfile,
				["SPEAK_IN"] = _language(),
				["LINE_LIMIT"] = replyChars
			});
			AiResult r = await _ai.SendAsync(new ChatRequest(HeartEventRequest, system, DialoguePrompts.HeartEventUserTurn(m.PromptName), OpeningTokens, Temperature), ct).ConfigureAwait(false);
			_log.SetHeartEventsSeen(npc, seenNow);
			return new Generated(r.Ok ? r.Text : null, OpeningKind.HeartEventFollowup, OpeningTopic.General);
		}

		// 配偶有时会先开口约农夫出去：邀请的话就是开场白。
		if (!hasFocus && Outings?.SpouseOffer(m) is OutingOffer o)
		{
			OutingDecision? offer = await OutingAsync(m, o.Context, "", null, today, ct).ConfigureAwait(false);
			if (offer is { Agrees: true, Instead: null } && string.Equals(offer.OutingId, o.Plan.Id, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(offer.Says))
			{
				offer.Tone = o.Plan.Type;
				_diag.Info($"{npc} 主动约农夫去 {o.Plan.Id}。");
				return new Generated(offer.Says, OpeningKind.Regular, OpeningTopic.General, Offer: offer);
			}
			_diag.Debug($"{npc} 这次没有主动邀约，照常开场。");
		}

		string worldBase = DialoguePrompts.Join(m.WorldContext, m.ModContext, m.LocationContext, m.Events, m.Milestones, m.NpcInteractions, m.Promises, m.Favors, m.ItemRequest, m.Arc, m.SocialDrama, m.VillageState, m.TodayInteractions);
		string world = DialoguePrompts.Join(worldBase, canon, focus);
		string backstory = m.PlayerProfile;
		// 第一次聊天不带八卦。
		string memory = _log.Count(npc) == 0 ? "" : m.GossipMemory;
		string gossip = DialoguePrompts.Paragraphs(DialoguePrompts.Gossip(npc, memory), m.Awareness);

		if (history.Count == 0 && m.Hearts <= 2)
		{
			string system = _templates.Fill("dialogue_first_meeting", new Dictionary<string, string?>
			{
				["VILLAGER"] = m.PromptName,
				["NATURE"] = personality,
				["FARMER_NAME"] = m.PlayerName,
				["ACQUAINTANCE"] = DialoguePrompts.FirstMeetingAcquaintance(m.PlayerName, m.Hearts, m.Year),
				["SEASON_NOW"] = HistoryFormatter.SeasonName(m.Season),
				["DAY_NUM"] = m.Day.ToString(),
				["WORLD_BRIEF"] = world,
				["TOWN_FACTS"] = m.CanonicalLore,
				["PAST_LIFE"] = backstory,
				["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
				["PLAY_THE_ROLE"] = usage,
				["DONT_REPEAT"] = _recent.OpeningBlock(npc),
				["LINE_LIMIT"] = replyChars,
				["SPEAK_IN"] = _language()
			});
			AiResult r = await _ai.SendAsync(new ChatRequest(FirstMeetingRequest, system, DialoguePrompts.FirstMeetingUserTurn(m.PromptName), OpeningTokens, Temperature), ct).ConfigureAwait(false);
			return new Generated(r.Ok ? r.Text : null, OpeningKind.FirstMeeting, OpeningTopic.General);
		}

		bool child = m.IsChildCoded || ChildSafety.IsChildCoded(npc, null, m.Personality);
		bool canAsk = !hasFocus && todayNumber <= 1 && ItemRequests?.CanAsk(npc, m.Hearts, m.IsAdultRomanticPartner, child) == true;
		OpeningTopic topic = hasFocus ? OpeningTopic.General : _picker.Pick(new OpeningTopicInput
		{
			Location = m.Location,
			LowMood = m.LowMood,
			HighMood = m.HighMood,
			HasGossip = !string.IsNullOrWhiteSpace(memory) || !string.IsNullOrWhiteSpace(m.SocialDrama) || !string.IsNullOrWhiteSpace(m.NpcInteractions),
			HasGift = !string.IsNullOrWhiteSpace(m.Gift),
			HasStory = !string.IsNullOrWhiteSpace(m.Events) || !string.IsNullOrWhiteSpace(m.Milestones) || !string.IsNullOrWhiteSpace(m.Arc) || !string.IsNullOrWhiteSpace(m.CanonicalLore) || seenNow.Count > 0 && m.Hearts >= 4,
			IsAdultRomanticPartner = m.IsAdultRomanticPartner,
			IsOpeningLine = todayNumber <= 1,
			CanRequestItem = canAsk,
			ItemRequestChancePercent = config.WishOddsPercent,
			RecentHistory = history
		});
		ItemRequestCandidate? request = null;
		if (topic == OpeningTopic.ItemRequest)
		{
			request = await PlanItemRequestAsync(m, child, historyText, worldBase, ct).ConfigureAwait(false);
			if (request == null)
			{
				_diag.Debug($"{npc} 的物品请求没定下来，改成普通开场。");
				topic = OpeningTopic.General;
			}
			else
			{
				world = DialoguePrompts.Join(world, request.PromptBlock());
			}
		}
		string? focusFile = OpeningTopicPicker.FocusTemplate(topic);
		string topicBlock = focusFile == null ? "" : _templates.Raw(focusFile)?.Trim() ?? "";
		_diag.Debug($"{npc} 的开场方向：{topic}。");
		string prompt = _templates.Fill("dialogue_opening", new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["NATURE"] = personality,
			["FARMER_NAME"] = m.PlayerName,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["SEASON_NOW"] = HistoryFormatter.SeasonName(m.Season),
			["DAY_NUM"] = m.Day.ToString(),
			["YEAR_NUM"] = m.Year.ToString(),
			["SKY"] = m.Weather,
			["PAST_LIFE"] = backstory,
			["TOWN_FACTS"] = m.CanonicalLore,
			["RUMORS_HEARD"] = gossip,
			["WORLD_BRIEF"] = world,
			["MOOD_NOW"] = m.Mood,
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["PLAY_THE_ROLE"] = usage,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["JUST_GIVEN"] = m.Gift,
			["LOG_TEXT"] = historyText,
			["DONT_REPEAT"] = _recent.OpeningBlock(npc),
			["BOND_LEVEL"] = FriendshipTone.Describe(m.Hearts, m.EnmityHearts),
			["FOCUS_BLOCK"] = topicBlock,
			["LINE_LIMIT"] = replyChars,
			["SPEAK_IN"] = _language()
		});
		AiResult result = await _ai.SendAsync(new ChatRequest(OpeningRequest, prompt, DialoguePrompts.OpeningUserTurn(m.PromptName, todayNumber, config.RepliesPerTalk), OpeningTokens, Temperature), ct).ConfigureAwait(false);
		if (!result.Ok || request == null || ct.IsCancellationRequested)
		{
			return new Generated(result.Ok ? result.Text : null, OpeningKind.Regular, topic);
		}
		(string line1, string line2, string handover) = await ItemRequestLinesAsync(m, request, result.Text!, historyText, worldBase, ct).ConfigureAwait(false);
		return new Generated(result.Text, OpeningKind.Regular, topic, request, line1, line2, Handover: handover);
	}

	/// <summary>让 AI 挑一个物品请求的方案，再按方案定下要什么。</summary>
	private async Task<ItemRequestCandidate?> PlanItemRequestAsync(DialogueMaterial m, bool child, string historyText, string world, CancellationToken ct)
	{
		IItemRequestDesk? desk = ItemRequests;
		string options = desk?.PlanningOptions(m.Hearts, m.IsAdultRomanticPartner, child) ?? "";
		if (desk == null || options.Length == 0 || _templates.Raw(ItemRequestPlanRequest) == null)
		{
			return null;
		}
		string system = _templates.Fill(ItemRequestPlanRequest, new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["NATURE"] = PersonaPrompt.PersonalityBlock(m.NpcName, m.Personality),
			["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(m.NpcName, m.CardRewritten),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["PLANNING_OPTIONS"] = options,
			["WORLD_BRIEF"] = world,
			["MOOD_NOW"] = m.Mood,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["LOG_TEXT"] = historyText,
			["SPEAK_IN"] = _language()
		});
		AiResult r = await _ai.SendAsync(new ChatRequest(ItemRequestPlanRequest, system, $"替 {m.PromptName} 挑一个物品请求的方案。", ReplyTokens, 0.4), ct).ConfigureAwait(false);
		ItemRequestPlan? plan = r.Ok ? ItemRequestAi.ParsePlan(r.Text!) : null;
		return plan == null ? null : desk.Candidate(m.NpcName, m.Hearts, m.IsAdultRomanticPartner, child, plan);
	}

	/// <summary>交东西时的一句道谢和完成场景里的两句台词；没写成就用内置的。</summary>
	private async Task<(string, string, string)> ItemRequestLinesAsync(DialogueMaterial m, ItemRequestCandidate c, string requestText, string historyText, string world, CancellationToken ct)
	{
		if (_templates.Raw(ItemRequestLinesRequest) == null)
		{
			return ("", "", "");
		}
		string system = _templates.Fill(ItemRequestLinesRequest, new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["NATURE"] = PersonaPrompt.PersonalityBlock(m.NpcName, m.Personality),
			["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(m.NpcName, m.CardRewritten),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["ITEM_NAME"] = c.WantedItemName,
			["REQUEST_TEXT"] = requestText,
			["SCENE_TYPE"] = ItemRequestCandidate.SceneLabel(c.Scene.SceneType),
			["SCENE_ID"] = c.Scene.TemplateId,
			["SCENE_GUIDANCE"] = c.Scene.LlmLineInstructions,
			["WORLD_BRIEF"] = world,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["LOG_TEXT"] = historyText,
			["SPEAK_IN"] = _language()
		});
		AiResult r = await _ai.SendAsync(new ChatRequest(ItemRequestLinesRequest, system, $"写 {m.PromptName} 交东西时的道谢和完成场景里的两句话。", OpeningTokens, Temperature), ct).ConfigureAwait(false);
		if (!r.Ok || ItemRequestAi.ParseLines(r.Text!) is not (string a, string b))
		{
			return ("", "", "");
		}
		return (a, b, ItemRequestAi.ParseHandover(r.Text!));
	}

	/// <summary>
	/// 村民看到一件事后走过来时说的第一句：不管今天聊没聊过，都换成这句当今天的开场白，后面照常可以回复。
	/// </summary>
	public async Task<string?> ReactionOpeningAsync(DialogueMaterial m, WitnessReaction reaction, GameDate today, CancellationToken ct = default)
	{
		string npc = m.NpcName;
		if (ct.IsCancellationRequested || _templates.Raw(ReactionTemplate) == null)
		{
			return null;
		}
		lock (_sync)
		{
			if (_generating.Contains(npc))
			{
				return null;
			}
			_today.Remove(npc);
			_generating.Add(npc);
		}
		_log.ClearCachedDialogue(npc);
		try
		{
			IReadOnlyList<ConversationEntry> history = _log.Recent(npc, 10);
			string system = _templates.Fill(ReactionTemplate, new Dictionary<string, string?>
			{
				["VILLAGER"] = m.PromptName,
				["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
				["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(npc, m.CardRewritten),
				["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
				["FARMER_NAME"] = m.PlayerName,
				["HEART_COUNT"] = m.Hearts.ToString(),
				["SEASON_NOW"] = HistoryFormatter.SeasonName(m.Season),
				["DAY_NUM"] = m.Day.ToString(),
				["YEAR_NUM"] = m.Year.ToString(),
				["SKY"] = m.Weather,
				["PAST_LIFE"] = m.PlayerProfile,
				["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.ModContext, m.LocationContext),
				["MOOD_NOW"] = m.Mood,
				["HOW_THEY_FEEL"] = m.EmotionalProfile,
				["LOG_TEXT"] = HistoryText(npc, history, today, m.PlayerName),
				["NOTICED_THINGS"] = m.Awareness,
				["REACTION_TITLE"] = reaction.Title,
				["REACTION_GUIDE"] = string.Join("\n", reaction.Guide.Select(g => g.StartsWith(" ", StringComparison.Ordinal) ? g : "· " + g)),
				["WHAT_WAS_SEEN"] = reaction.Observation,
				["HOW_SURE"] = reaction.Perception,
				["FEELS"] = reaction.Feeling,
				["DONT_REPEAT"] = _recent.OpeningBlock(npc),
				["LINE_LIMIT"] = _ai.ReplyChars.ToString(),
				["SPEAK_IN"] = _language()
			});
			AiResult r = await _ai.SendAsync(new ChatRequest(OpeningRequest, system, DialoguePrompts.ReactionUserTurn(m.PromptName), OpeningTokens, Temperature), ct).ConfigureAwait(false);
			if (!r.Ok || string.IsNullOrWhiteSpace(r.Text) || ct.IsCancellationRequested)
			{
				return null;
			}
			string text = Tidy(r.Text);
			_recent.RecordOpening(npc, text);
			lock (_sync)
			{
				_today[npc] = new TodayDialogue { Npc = npc, Line = text, Spent = true, WithSpouse = m.IsSpouse };
			}
			_log.SetCachedDialogue(npc, text);
			_diag.Debug($"{npc} 走过来时说（{reaction.Type}）：{text}");
			return text;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_diag.Error($"生成 {npc} 走过来时的话出错：{ex.Message}");
			return null;
		}
		finally
		{
			lock (_sync)
			{
				_generating.Remove(npc);
			}
		}
	}

	/// <summary>
	/// 评估玩家的回复，得到村民的反应。只发请求、解析，不改记录；结果在主线程交给 <see cref="CommitReply"/>。
	/// <paramref name="currentLine"/> 是玩家正在回应的那句（多轮时是上一句反应）；不给时用今天的开场白。
	/// </summary>
	public async Task<ReplyResult?> ReplyAsync(DialogueMaterial m, string playerText, GameDate today, string? currentLine = null, string? proposedAction = null, CancellationToken ct = default, bool remember = true)
	{
		string npc = m.NpcName;
		string? line = string.IsNullOrWhiteSpace(currentLine) ? TodayLine(npc) : currentLine;
		if (line == null || _templates.Raw("dialogue_reply") == null)
		{
			return null;
		}
		ModConfig config = _config();
		int replyLimit = m.MaxReplyChars > 0 ? Math.Min(m.MaxReplyChars, _ai.ReplyChars) : _ai.ReplyChars;
		int window = HistoryFormatter.Window(config.RecentTurnsInPrompt, config.RepliesPerTalk, _ai.UsesServer, _ai.ServerMemoryCap);
		IReadOnlyList<ConversationEntry> history = _log.Recent(npc, window);
		string wrapped = PromptText.Wrap("PLAYER_RESPONSE", playerText, DialoguePrompts.PlayerTextMaxChars);
		string language = _language();
		TodayDialogue? state = State(npc);
		bool festival = m.Festival != null;
		proposedAction = festival ? null : proposedAction;
		string system = FestivalReplyPrefix(m) + _templates.Fill("dialogue_reply", new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
			["HEART_COUNT"] = m.RawHearts.ToString(),
			["FARMER_NAME"] = m.PlayerName,
			["HOSTILE_HEARTS"] = config.HostilityOn ? Math.Max(0, m.EnmityHearts).ToString() : "0",
			["POINT_RULES"] = DialoguePrompts.Scoring(_templates, config.HostilityOn, m.EnmityHearts),
			["PAST_LIFE"] = m.ReplyPlayerProfile,
			["RUMORS_HEARD"] = DialoguePrompts.Paragraphs(DialoguePrompts.Gossip(npc, m.GossipMemory + m.TodayInteractions), m.Awareness),
			// 村子状态、心情、拜托、物品请求、故事弧、社交剧情连在一起放在心情的位置。
			["MOOD_NOW"] = festival ? m.VillageState + m.Mood + m.SocialDrama : m.VillageState + m.Mood + m.Favors + m.ItemRequest + m.Arc + m.SocialDrama,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(npc, m.CardRewritten),
			["JUST_GIVEN"] = m.Gift,
			["FROM_MODS"] = DialoguePrompts.Combine(m.ModContext, DialoguePrompts.DramaTone(config, m.AdultRelationshipDrama && !m.IsChildCoded && !ChildSafety.IsChildCoded(npc, null, m.Personality))),
			["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.CanonicalLore, DialoguePrompts.CanonFollowup(state?.GameLineHint, config.FeedGameLineToAi)),
			["NPC_DIALOGUE"] = line,
			["PLAYER_RESPONSE"] = wrapped,
			["CAN_DO"] = DialoguePrompts.ProposedAction(proposedAction, m.PlayerName),
			["CAN_DO_JSON"] = DialoguePrompts.ProposedActionJson(proposedAction, language),
			["PROPOSAL_JSON_BLOCK"] = m.ProposalJson,
			["LOG_TEXT"] = HistoryText(npc, history, today, m.PlayerName, window, m.ContextType, m.ContextId),
			["DONT_REPEAT_REPLY"] = _recent.ReactionBlock(npc),
			["BIRTHDAY_BLOCK"] = m.ReplyBirthday,
			["CHANNEL_BLOCK"] = m.ChannelSection,
			["LINE_LIMIT"] = replyLimit.ToString(),
			["SPEAK_IN"] = language
		});
		string user = DialoguePrompts.ReplyUserTurn(m.PromptName, m.PlayerName, line, wrapped, m.MaxReplyChars > 0 ? replyLimit : 0);
		AiResult first = await _ai.SendAsync(new ChatRequest(ReplyRequest, system, user, ReplyTokens, Temperature), ct).ConfigureAwait(false);
		if (!first.Ok)
		{
			return null;
		}
		string raw = first.Text!;
		// 只回了 says（好感、情绪、提议都丢了）：提醒一下再问一次，第二次也不完整就用第一次的。
		if (ReactionParser.IsReactionOnly(raw))
		{
			_diag.Info($"{npc} 的回复只有 says，再要一次完整的 JSON。");
			AiResult again = await _ai.SendAsync(new ChatRequest(ReplyRequest, system, user + DialoguePrompts.ReactionOnlyNudge, ReplyTokens, Temperature), ct).ConfigureAwait(false);
			if (again.Ok && !ReactionParser.IsReactionOnly(again.Text!) && ReactionParser.Parse(again.Text!, replyLimit, config.HostilityOn, _diag) != null)
			{
				raw = again.Text!;
			}
			else
			{
				_diag.Info($"{npc} 第二次也不完整，用第一次的。");
			}
		}
		NpcReaction? reaction = ReactionParser.Parse(raw, replyLimit, config.HostilityOn, _diag);
		if (reaction == null)
		{
			return null;
		}
		if (festival)
		{
			reaction.ActionResponse = null;
			reaction.RequestedFavor = null;
			reaction.ArcObjectiveCompleted = null;
			reaction.ArcObjectiveApproach = null;
		}
		// 只回了 adds、says 为空也算有回应（两段本来就连着显示）。
		if (remember && !string.IsNullOrWhiteSpace(reaction.Reaction))
		{
			_recent.RecordReaction(npc, ReactionText.ForHistory(reaction));
		}
		return new ReplyResult(reaction, line, ReactionText.ForDisplay(reaction));
	}

	/// <summary>主线程：把这一轮记进对话记录，标记今天聊过。</summary>
	public ConversationEntry CommitReply(DialogueMaterial m, string playerText, ReplyResult reply, GameDate today, string? historyNote = null)
	{
		string history = ReactionText.ForHistory(reply.Reaction);
		ConversationEntry entry = new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Farmer = string.IsNullOrWhiteSpace(m.PlayerName) ? "Farmer" : m.PlayerName,
			Setting = m.ContextType,
			SettingRef = m.ContextId,
			SettingName = m.ContextName,
			Opener = string.IsNullOrWhiteSpace(historyNote) ? reply.CurrentLine : (reply.CurrentLine + " " + historyNote).Trim(),
			FarmerReply = playerText,
			Comeback = string.IsNullOrWhiteSpace(history) ? reply.Reaction.Reaction : history,
			Face = reply.Reaction.Emotion,
			HeartPoints = reply.Reaction.FriendshipPoints
		};
		_log.Add(m.NpcName, entry);
		if (m.Festival == null)
		{
			_log.MarkTalkedToday(m.NpcName, today);
		}
		if (!m.Messenger)
		{
			lock (_sync)
			{
				if (_today.TryGetValue(m.NpcName, out TodayDialogue? s))
				{
					s.SpokeToday = true;
				}
			}
		}
		return entry;
	}

	/// <summary>
	/// 主线程：玩家按了"不回了"。这场对话算聊过并结束；有开场白时记一条"跳过"（不写得分，也不写进存档里的"今天聊过"）。
	/// 加好感由调用方做（<see cref="ModConfig.WalkAwayPoints"/>）。返回记下的那条，没有开场白时为 null。
	/// </summary>
	public ConversationEntry? CommitSkip(string npc, string playerName, GameDate today, AISV.Core.Festivals.FestivalPack? festival = null)
	{
		TodayDialogue? s = State(npc);
		if (s == null)
		{
			CompleteDaily(npc, today);
			return null;
		}
		lock (_sync)
		{
			s.SpokeToday = true;
		}
		CompleteDaily(npc, today);
		string contextType = festival != null ? "festival" : "daily";
		string contextId = festival?.Id ?? "";
		// 来回聊过几轮再走开时，农夫没接的是对方最后那句，不是开场白。
		ConversationEntry? last = _log.Recent(npc, 1).LastOrDefault();
		bool replied = last != null && last.IsOn(today) && HistoryFormatter.IsSameContext(last, contextType, contextId)
			&& !SkippedReply.Is(last.FarmerReply) && !string.IsNullOrWhiteSpace(last.Comeback);
		ConversationEntry entry = new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Farmer = playerName,
			Setting = contextType,
			SettingRef = contextId,
			SettingName = festival?.Name ?? "",
			Opener = replied ? last!.Comeback : s.Line,
			FarmerReply = SkippedReply.Marker,
			Comeback = "",
			Face = Emotion.Neutral
		};
		_log.Add(npc, entry);
		return entry;
	}

	/// <summary>这场对话结束：配偶的算进今天的场数。</summary>
	public void CompleteDaily(string npc, GameDate today)
	{
		TodayDialogue? s = State(npc);
		if (s != null && s.WithSpouse && !s.Logged)
		{
			s.ClearPendingResponse();
			s.Logged = true;
			_log.RecordCompletedSpouseConversation(npc, today);
		}
	}

	private string? TodayLine(string npc)
	{
		lock (_sync)
		{
			if (_today.TryGetValue(npc, out TodayDialogue? s) && !string.IsNullOrEmpty(s.Line))
			{
				s.Spent = true;
				return s.Line;
			}
			return null;
		}
	}

	private string HistoryText(string npc, IReadOnlyList<ConversationEntry> history, GameDate today, string player, int? window = null, string contextType = "daily", string contextId = "")
	{
		ModConfig c = _config();
		int w = window ?? HistoryFormatter.Window(c.RecentTurnsInPrompt, c.RepliesPerTalk, _ai.UsesServer, _ai.ServerMemoryCap);
		return HistoryFormatter.Format(npc, history, w, c.RepliesPerTalk, today, player, contextType, contextId);
	}

	// ---- 节日 ----

	private string FestivalReplyPrefix(DialogueMaterial m) => m.Festival == null ? "" : _templates.Fill("festival_reply_prefix", new Dictionary<string, string?>
	{
		["FEST"] = m.Festival.Name,
		["FESTIVAL_RULES"] = _templates.Raw("festival_rules") ?? "",
		["FESTIVAL_DIRECTION"] = FestivalDirection(m, m.Festival.Direction),
		["VILLAGER"] = m.PromptName
	});

	private static readonly System.Text.RegularExpressions.Regex LeftoverPlaceholder = new System.Text.RegularExpressions.Regex(@"\{\{[A-Z0-9_]+\}\}");

	private string FestivalDirection(DialogueMaterial m, string text) =>
		LeftoverPlaceholder.Replace(text.Replace("{{VILLAGER}}", m.PromptName).Replace("{{FARMER_NAME}}", m.PlayerName).Replace("{{HEART_COUNT}}", m.Hearts.ToString()), "");

	/// <summary>节日上的开场白：把游戏这一刻要说的那句换成村民自己的话。同一场节日里生成过就不再发。</summary>
	public async Task<string?> FestivalOpeningAsync(DialogueMaterial m, GameDate today, CancellationToken ct = default)
	{
		string npc = m.NpcName;
		if (m.Festival == null || ct.IsCancellationRequested || _templates.Raw("festival_opening") == null)
		{
			return null;
		}
		lock (_sync)
		{
			if (_generating.Contains(npc) || (_today.TryGetValue(npc, out TodayDialogue? known) && known.SpokeToday))
			{
				return null;
			}
			if (known != null && !string.IsNullOrEmpty(known.Line))
			{
				known.Spent = true;
				return known.Line;
			}
			_generating.Add(npc);
		}
		try
		{
			string vanilla = PromptText.Wrap("VANILLA_FESTIVAL_DIALOGUE", m.VanillaLine, 800);
			string anchor = string.IsNullOrWhiteSpace(m.VanillaLine) ? "" : (_config().FeedGameLineToAi ? DialoguePrompts.CanonOpening(m.VanillaLine, true) : "【游戏原本的台词，只是素材】\n" + vanilla)
				+ "\n上面是游戏在这一刻安排给你的话。拿它当底子：里面提到的事留着，换成你自己的嘴说出来。现场有更值得说的可以拐过去，只是别为了客套把它整个丢掉，也不能和节日此刻的进度对不上。";
			IReadOnlyList<ConversationEntry> history = _log.Recent(npc, 10);
			string system = _templates.Fill("festival_opening", new Dictionary<string, string?>
			{
				["FEST"] = m.Festival.Name,
				["FESTIVAL_RULES"] = _templates.Raw("festival_rules") ?? "",
				["FESTIVAL_DIRECTION"] = FestivalDirection(m, m.Festival.Direction),
				["VANILLA_BLOCK"] = anchor,
				["VILLAGER"] = m.PromptName,
				["FARMER_NAME"] = m.PlayerName,
				["HEART_COUNT"] = m.Hearts.ToString(),
				["SEASON_NOW"] = HistoryFormatter.SeasonName(m.Season),
				["DAY_NUM"] = m.Day.ToString(),
				["YEAR_NUM"] = m.Year.ToString(),
				["SKY"] = m.Weather,
				["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
				["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(npc, m.CardRewritten),
				["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
				["HOW_THEY_FEEL"] = m.EmotionalProfile,
				["JUST_GIVEN"] = m.Gift,
				["TOWN_FACTS"] = m.CanonicalLore,
				["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.ModContext, m.LocationContext),
				["MOOD_NOW"] = m.Mood,
				["RUMORS_HEARD"] = DialoguePrompts.Paragraphs(DialoguePrompts.Gossip(npc, m.GossipMemory), m.Awareness),
				["PAST_LIFE"] = m.PlayerProfile,
				["LOG_TEXT"] = HistoryText(npc, history, today, m.PlayerName, null, m.ContextType, m.ContextId),
				["LINE_LIMIT"] = _ai.ReplyChars.ToString(),
				["SPEAK_IN"] = _language()
			});
			AiResult r = await _ai.SendAsync(new ChatRequest(OpeningRequest, system, DialoguePrompts.FestivalUserTurn(m.PromptName), OpeningTokens, Temperature), ct).ConfigureAwait(false);
			if (!r.Ok || string.IsNullOrWhiteSpace(r.Text) || ct.IsCancellationRequested)
			{
				return null;
			}
			string text = Tidy(r.Text);
			lock (_sync)
			{
				_today[npc] = new TodayDialogue { Npc = npc, Line = text, GameLineHint = m.VanillaLine, Spent = true };
			}
			_recent.RecordOpening(npc, text);
			_diag.Debug($"{npc} 在 {m.Festival.Id} 上说：{text}");
			return text;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_diag.Error($"生成 {npc} 的节日台词时出错：{ex.Message}");
			return null;
		}
		finally
		{
			lock (_sync)
			{
				_generating.Remove(npc);
			}
		}
	}

	/// <summary>表白或求婚：交给村民自己决定。status / narrative / vanilla 是调用方从游戏里读的关系状态。</summary>
	public async Task<ProposalDecision?> ProposalAsync(DialogueMaterial m, ProposalKind kind, string playerText, string? currentLine, string status, string narrative, string vanilla, GameDate today, CancellationToken ct = default)
	{
		string npc = m.NpcName;
		if (_templates.Raw(ProposalRequest) == null)
		{
			return null;
		}
		string system = _templates.Fill(ProposalRequest, new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["PROPOSAL_ACT"] = kind == ProposalKind.Dating ? "表白，问能不能和你交往" : "求婚",
			["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["BOND_STATUS"] = status,
			["FREE_TO_COURT"] = narrative,
			["GAME_BOND"] = vanilla,
			["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.PlayerProfile),
			["FROM_MODS"] = m.ModContext,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["BIG_MOMENTS"] = m.Milestones,
			["STORIES_NOW"] = m.Arc,
			["SETTLED_STORIES"] = m.CanonicalLore,
			["DRAMA_NOW"] = m.SocialDrama,
			["LOG_TEXT"] = HistoryText(npc, _log.Recent(npc, 10), today, m.PlayerName),
			["LAST_LINE"] = string.IsNullOrWhiteSpace(currentLine) ? TodayLine(npc) ?? "" : currentLine,
			["FARMER_SAID"] = PromptText.Wrap("PLAYER_MESSAGE", playerText, DialoguePrompts.PlayerTextMaxChars),
			["SPEAK_IN"] = _language()
		});
		AiResult r = await _ai.SendAsync(new ChatRequest(ProposalRequest, system, $"{m.PromptName} 怎么回应这次{(kind == ProposalKind.Dating ? "表白" : "求婚")}？", ReplyTokens, Temperature), ct).ConfigureAwait(false);
		if (!r.Ok)
		{
			return null;
		}
		ProposalDecision? d = ProposalParser.Parse(r.Text!, _ai.ReplyChars);
		if (d == null)
		{
			_diag.Warn($"{npc} 对{(kind == ProposalKind.Dating ? "表白" : "求婚")}的回答解析不了。");
		}
		return d;
	}

	/// <summary>约出去 / 约会 / 配偶主动邀约：交给村民自己决定。</summary>
	public async Task<OutingDecision?> OutingAsync(DialogueMaterial m, OutingContext c, string playerText, string? currentLine, GameDate today, CancellationToken ct = default)
	{
		string npc = m.NpcName;
		if (_templates.Raw(OutingRequest) == null || string.IsNullOrWhiteSpace(c.Options))
		{
			return null;
		}
		bool spouse = c.Kind != OutingKind.Invite;
		bool offer = c.Kind == OutingKind.SpouseOffer;
		string system = _templates.Fill(OutingRequest, new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["BOND_STATUS"] = c.Status,
			["FREE_TO_COURT"] = c.Narrative,
			["GAME_BOND"] = c.Vanilla,
			["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["BIG_MOMENTS"] = m.Milestones,
			["STORIES_NOW"] = m.Arc,
			["SETTLED_STORIES"] = m.CanonicalLore,
			["DRAMA_NOW"] = m.SocialDrama,
			["FROM_MODS"] = m.ModContext,
			["LOG_TEXT"] = HistoryText(npc, _log.Recent(npc, 10), today, m.PlayerName),
			["LAST_LINE"] = offer ? "" : PromptText.Wrap("CURRENT_NPC_DIALOGUE", string.IsNullOrWhiteSpace(currentLine) ? TodayLine(npc) ?? "" : currentLine, 800),
			["FARMER_SAID"] = offer ? "（农夫还没开口，这次是你先提。）" : PromptText.Wrap("PLAYER_INVITE", playerText, DialoguePrompts.PlayerTextMaxChars),
			["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.PlayerProfile),
			["OUTING_CHOICES"] = c.Options,
			["OUTING_STATE"] = c.State,
			["TEXT_KINDS"] = OutingPrompt.KindRules(c.Kind, c.Hot),
			["TONE_CHOICES"] = OutingPrompt.ToneValues(spouse, c.Hot),
			["SAMPLE_TONE"] = OutingPrompt.ToneExample(spouse),
			["SAMPLE_OUTING"] = OutingPrompt.TemplateExample(spouse),
			["SPEAK_IN"] = _language()
		});
		string user = offer ? $"这回由 {m.PromptName} 先开口，邀配偶 {m.PlayerName} 去上面列的那个地方；{m.PlayerName} 还没表态。" : $"{m.PromptName} 怎么回应这次{(spouse ? "约会" : "邀约")}？";
		AiResult r = await _ai.SendAsync(new ChatRequest(OutingRequest, system, user, ReplyTokens, Temperature), ct).ConfigureAwait(false);
		if (!r.Ok)
		{
			return null;
		}
		OutingDecision? d = OutingAi.Parse(r.Text!, _ai.ReplyChars);
		if (d == null)
		{
			_diag.Warn($"{npc} 对邀约的回答解析不了。");
		}
		else
		{
			_diag.Info($"{npc} 对邀约：{(d.Agrees ? "答应" : "没答应")}，语气 {d.Tone}，情绪 {d.Feeling}，好感 {d.Warmth}{(d.Instead != null ? "，改提 " + d.Instead.OutingId : "")}。");
		}
		return d;
	}

	/// <summary>
	/// 旁听：写几位村民当着农夫的面说的几句（一个人时是自言自语）。<paramref name="speakers"/> 是 (名字, 人设)；<paramref name="premise"/> 是心里想的事，可以为空；
	/// <paramref name="farmerTopic"/> 不空时，他们正背着农夫聊他最近的这件事。
	/// </summary>
	public async Task<IReadOnlyList<SceneLine>?> OverheardAsync(IReadOnlyList<(string Name, string Persona)> speakers, string premise, string location, int time, string season, CancellationToken ct = default, string farmerTopic = "")
	{
		List<string> names = speakers.Select(s => s.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (names.Count == 0 || ct.IsCancellationRequested || _templates.Raw(OverheardRequest) == null)
		{
			return null;
		}
		bool alone = names.Count == 1;
		string joined = OverheardAi.JoinNames(names);
		string system = _templates.Fill(OverheardRequest, new Dictionary<string, string?>
		{
			["VOICES"] = string.Join(", ", names),
			["WHATS_UP"] = alone ? $"只有{names[0]}一个人在，正自己跟自己念叨。" : $"{joined}凑在一块儿说着话。",
			["WHO_THEY_ARE"] = OverheardAi.Personas(speakers.Where(s => names.Contains(s.Name.Trim(), StringComparer.OrdinalIgnoreCase))),
			["STAGE_NOTES"] = OverheardAi.Setting(location, time, season),
			["SETUP"] = farmerTopic.Length > 0 ? OverheardAi.FarmerTopicBlock(farmerTopic)
				: string.IsNullOrWhiteSpace(premise) ? "（没有特别惦记的事——就从这个地方、这个钟点自然会聊起的事里挑一件。）" : PromptText.Wrap("SCENE_PREMISE", premise, MomentRules.MaxPremiseChars),
			["FARMER_RULE"] = farmerTopic.Length > 0 ? OverheardAi.BehindBackRule : OverheardAi.NotAboutFarmerRule,
			["HOW_MANY_LINES"] = alone ? $"1～{MomentRules.MaxMonologueLines}" : $"2～{MomentRules.MaxOverheardLines}",
			["LINE_CHAR_CAP"] = OverheardAi.MaxLineChars.ToString(),
			["SPEAK_IN"] = _language()
		});
		string user = alone ? $"{names[0]}的这几句念叨，按格式交回来。" : $"{joined}这一来一回，按格式交回来。";
		AiResult r = await _ai.SendAsync(new ChatRequest(OverheardRequest, system, user, OpeningTokens, Temperature), ct).ConfigureAwait(false);
		if (!r.Ok || ct.IsCancellationRequested)
		{
			return null;
		}
		IReadOnlyList<SceneLine>? lines = OverheardAi.Parse(r.Text, names, OverheardAi.MaxLines(names.Count));
		if (lines == null)
		{
			_diag.Debug($"{string.Join("、", names)} 的旁听台词没有能用的。");
		}
		return lines;
	}

	/// <summary>答应以后，写出游场景里村民说的话（4～6 句）。<paramref name="scenery"/> 是布景的说明，可以为空。</summary>
	public async Task<IReadOnlyList<string>?> OutingLinesAsync(DialogueMaterial m, OutingPlan plan, string scenery, OutingRecord record, GameDate today, CancellationToken ct = default)
	{
		if (_templates.Raw(OutingLinesRequest) == null)
		{
			return null;
		}
		int count = Math.Clamp(plan.Lines, 4, 6);
		string guide = string.IsNullOrWhiteSpace(scenery) ? plan.Hint : $"{plan.Hint} 地方够的话，旁边摆着：{scenery}。";
		string system = _templates.Fill(OutingLinesRequest, new Dictionary<string, string?>
		{
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["SCENE_TITLE"] = plan.Title,
			["STAGE_PLACE"] = OutingCatalog.PlaceName(plan.Location),
			["SCENE_TONE"] = OutingAi.ToneName(plan.Type),
			["SCENE_GUIDANCE"] = guide,
			["SCENE_RULES"] = OutingPrompt.SceneRules(plan.Type),
			["HOW_MANY_LINES"] = count.ToString(),
			["FARMER_SAID"] = PromptText.Wrap("PLAYER_INVITE", record.Asked, 600),
			["ACCEPTANCE_BLOCK"] = PromptText.Wrap("ACCEPTANCE_LINE", record.Said, 600),
			["MEMORY_SUMMARY"] = record.ToRemember,
			["NATURE"] = PersonaPrompt.PersonalityBlock(m.NpcName, m.Personality),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["BOND_LEVEL"] = FriendshipTone.Describe(m.Hearts, m.EnmityHearts),
			["LOG_TEXT"] = HistoryText(m.NpcName, _log.Recent(m.NpcName, 10), today, m.PlayerName),
			["LINE_KEYS"] = OutingPrompt.LineKeys(count),
			["SPEAK_IN"] = _language()
		});
		AiResult r = await _ai.SendAsync(new ChatRequest(OutingLinesRequest, system, $"写 {m.PromptName} 在这段出游里说的 {count} 句话。", OpeningTokens, Temperature), ct).ConfigureAwait(false);
		IReadOnlyList<string>? lines = r.Ok ? OutingAi.ParseLines(r.Text!, count) : null;
		if (r.Ok && lines == null)
		{
			_diag.Warn($"{m.NpcName} 的出游台词缺了前四句里的某句，用内置的。");
		}
		return lines;
	}

	/// <summary>邀约这一轮记进对话记录，今天算聊过了。</summary>
	public void CommitOuting(DialogueMaterial m, OutingKind kind, string playerText, string? currentLine, OutingDecision d, GameDate today)
	{
		_log.Add(m.NpcName, new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Farmer = m.PlayerName,
			Opener = string.IsNullOrWhiteSpace(currentLine) ? State(m.NpcName)?.Line ?? "" : currentLine,
			FarmerReply = $"[{(kind == OutingKind.Invite ? "约出去" : "约会")}] {playerText}",
			Comeback = d.Says,
			Face = d.Feeling,
			HeartPoints = d.Warmth
		});
		_log.MarkTalkedToday(m.NpcName, today);
		lock (_sync)
		{
			if (_today.TryGetValue(m.NpcName, out TodayDialogue? s))
			{
				s.SpokeToday = true;
				s.OpenOffer = null;
			}
		}
	}

	/// <summary>节日上的邀请（跳舞、组队）：让 AI 判断答不答应。</summary>
	public async Task<FestivalInviteDecision?> FestivalInviteAsync(DialogueMaterial m, string playerText, string? currentLine, GameDate today, CancellationToken ct = default)
	{
		string npc = m.NpcName;
		FestivalAction? action = m.Festival?.Action;
		string? line = string.IsNullOrWhiteSpace(currentLine) ? TodayLine(npc) : currentLine;
		if (action == null || line == null || _templates.Raw("festival_invite") == null)
		{
			return null;
		}
		const int window = 10;
		bool dance = action.Kind == FestivalAction.Dance;
		InviteProtocol protocol = InviteProtocol.For(action.Kind);
		string system = _templates.Fill("festival_invite", new Dictionary<string, string?>
		{
			["FEST"] = m.Festival!.Name,
			["FESTIVAL_RULES"] = _templates.Raw("festival_rules") ?? "",
			["ACTION_LABEL"] = action.Label,
			["FESTIVAL_DIRECTION"] = FestivalDirection(m, m.Festival.Direction),
			["ACTION_PROMPT"] = FestivalDirection(m, action.Prompt),
			["GATE_NOTE"] = dance ? "交情只有 0～1 心时基本不答应，除非资料里有很特别、又和恋爱无关的理由。" : "门槛以上面这个邀请里写的为准。",
			["ELIGIBILITY"] = dance ? "" : "不符合邀请写明的条件时，直接回绝。",
			["REASON_CODES"] = string.Join(", ", protocol.Reasons),
			["FIRST_CODE"] = protocol.GateMet,
			["GATE_FIELD"] = protocol.GateField,
			["EXAMPLE_POINTS"] = protocol.ExamplePoints.ToString(),
			["REACTION_LENGTH"] = dance ? $"一两句，最多三句，{_ai.ReplyChars} 字以内" : "240 字以内",
			["VILLAGER"] = m.PromptName,
			["FARMER_NAME"] = m.PlayerName,
			["HEART_COUNT"] = m.Hearts.ToString(),
			["PAST_LIFE"] = m.PlayerProfile,
			["NATURE"] = PersonaPrompt.PersonalityBlock(npc, m.Personality),
			["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(npc, m.CardRewritten),
			["HOW_THEY_CHANGED"] = m.PersonalityEvolution,
			["NPC_DIALOGUE"] = line,
			["LOG_TEXT"] = HistoryText(npc, _log.Recent(npc, window), today, m.PlayerName, window, m.ContextType, m.ContextId),
			["WORLD_BRIEF"] = DialoguePrompts.Join(m.WorldContext, m.VillageState),
			["FROM_MODS"] = m.ModContext,
			["RUMORS_HEARD"] = DialoguePrompts.Paragraphs(DialoguePrompts.Gossip(npc, m.GossipMemory + m.TodayInteractions), m.Awareness),
			["MOOD_NOW"] = m.Mood,
			["HOW_THEY_FEEL"] = m.EmotionalProfile,
			["MILESTONES"] = m.Milestones,
			["ARC"] = m.Arc,
			["TOWN_FACTS"] = m.CanonicalLore,
			["SOCIAL_DRAMA"] = m.SocialDrama,
			["PLAYER_RESPONSE"] = PromptText.Wrap(dance ? "PLAYER_DANCE_INVITE" : "PLAYER_FESTIVAL_INVITATION", playerText, DialoguePrompts.PlayerTextMaxChars),
			["SPEAK_IN"] = _language()
		});
		AiResult r = await _ai.SendAsync(new ChatRequest(ReplyRequest, system, $"判断 {m.PromptName} 对「{action.Label}」的答复。", ReplyTokens, Temperature), ct).ConfigureAwait(false);
		return r.Ok ? FestivalInviteDecision.Parse(r.Text!, _ai.ReplyChars, _diag, protocol) : null;
	}

	/// <summary>主线程：把这次邀请和结果记进对话记录，这场对话算聊完。</summary>
	public void CommitInvite(DialogueMaterial m, string playerText, string? currentLine, FestivalInviteDecision d, GameDate today)
	{
		FestivalPack festival = m.Festival!;
		_log.Add(m.NpcName, new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Farmer = m.PlayerName,
			Setting = m.ContextType,
			SettingRef = m.ContextId,
			SettingName = m.ContextName,
			Opener = string.IsNullOrWhiteSpace(currentLine) ? State(m.NpcName)?.Line ?? "" : currentLine,
			FarmerReply = $"[节日邀请：{festival.Action?.Label}] {playerText}",
			Comeback = FestivalInviteText(festival, d, withReason: true),
			Face = d.Emotion,
			HeartPoints = d.FriendshipPoints
		});
		_log.MarkTalkedToday(m.NpcName, today);
		lock (_sync)
		{
			if (_today.TryGetValue(m.NpcName, out TodayDialogue? s))
			{
				s.SpokeToday = true;
			}
		}
	}

	/// <summary>表白/求婚这一轮写进对话记录，今天算聊过。</summary>
	public void CommitProposal(DialogueMaterial m, ProposalKind kind, string playerText, string? currentLine, ProposalDecision d, GameDate today)
	{
		_log.Add(m.NpcName, new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Farmer = m.PlayerName,
			Opener = string.IsNullOrWhiteSpace(currentLine) ? State(m.NpcName)?.Line ?? "" : currentLine,
			FarmerReply = $"[{(kind == ProposalKind.Dating ? "表白" : "求婚")}] {playerText}",
			Comeback = d.Line,
			Face = d.Face,
			HeartPoints = d.Points
		});
		_log.MarkTalkedToday(m.NpcName, today);
		lock (_sync)
		{
			if (_today.TryGetValue(m.NpcName, out TodayDialogue? s))
			{
				s.SpokeToday = true;
			}
		}
	}

	public static string FestivalInviteText(FestivalPack festival, FestivalInviteDecision d, bool withReason = false)
	{
		string outcome = $"（{festival.Action?.Label}：{(d.Accepted ? "答应了" : "没答应")}{(withReason ? "，" + d.ReasonCode : "")}。）";
		return string.Join(" ", new[] { d.Reaction, outcome, d.ReasonSummary }.Where(p => !string.IsNullOrWhiteSpace(p)));
	}

	/// <summary>去掉重复的句子，超过回复字数就截断。</summary>
	private string Tidy(string text)
	{
		text = TextTools.CollapseRepeats(text);
		int limit = _ai.ReplyChars;
		return text.Length > limit ? TextTools.Truncate(text, limit) : text;
	}
}
