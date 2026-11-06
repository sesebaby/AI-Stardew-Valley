using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Festivals;
using AISV.Core.Dialogue;
using AISV.Core.Outings;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Festivals;
using AISV.Game.Outings;
using AISV.Game.Social;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Menus;
using GameDialogue = StardewValley.Dialogue;

namespace AISV.UI.Dialogue;

/// <summary>
/// 和村民的日常对话流程：点村民 → 后台生成开场白（期间显示"在想"）→ 游戏对话框显示 → 关掉后打开回复框 →
/// 评估回复 → 加好感、做动作、显示反应 → 还有轮数就再开回复框，否则结束。
/// </summary>
public sealed partial class ConversationController
{
	/// <summary>这几位点了不说话、直接开店（游戏原台词又关掉时）。</summary>
	private static readonly HashSet<string> ShopOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Krobus", "HatMouse", "Raccoon", "Marlon", "TravelingCart" };

	private readonly Func<ModConfig> _config;

	private readonly AiService _ai;

	private readonly DialogueEngine _engine;

	private readonly MaterialFactory _materials;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly IModHelper _helper;

	private readonly ILog _log;

	// 显示过 AI 台词、还没聊完的村民；以及我们塞进 CurrentDialogue、还没被看完的那句。
	private readonly HashSet<string> _aiNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, GameDialogue> _pending = new Dictionary<string, GameDialogue>(StringComparer.OrdinalIgnoreCase);

	private NPC? _current;

	private NPC? _dialogueSpeaker;

	/// <summary>正在说的是游戏自己的一段话（交付道谢、伴侣剧情），关掉时不接回复流程。</summary>
	private string? _nativeSpeaker;

	private bool _generating;

	private bool _executingVanilla;

	private bool _dayTransition;

	// 回复框状态。
	private bool _waitingForReply;

	private NPC? _replyingTo;

	private ReplyMenu? _menu;

	private int _remainingTurns;

	private string? _lastReaction;

	private string? _lastEmotion;

	private CancellationTokenSource? _replyCts;

	private bool _loginNoticeShown;

	// 换天、回标题时加一，晚到的后台结果据此丢掉。
	private int _epoch;

	public ConversationController(Func<ModConfig> config, AiService ai, DialogueEngine engine, MaterialFactory materials, Relationships relationships, NpcDirectory npcs, IModHelper helper, ILog log)
	{
		_config = config;
		_ai = ai;
		_engine = engine;
		_materials = materials;
		_relationships = relationships;
		_npcs = npcs;
		_helper = helper;
		_log = log;
	}

	/// <summary>聊完一轮后通知（八卦、日记、心情、记忆、性格演变接在这里）。</summary>
	public event Action<Exchange>? Talked;

	/// <summary>回复显示之前审一下提议（不成会把那句换成婉拒）。</summary>
	public Action<NPC, NpcReaction, int>? ReviewProposal { get; set; }

	private readonly Dictionary<string, int> _lastTalkTick = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	/// <summary>玩家按了"不回了"、而且今天有开场白时：村民名、开场白。</summary>
	public event Action<string, string>? Skipped;

	/// <summary>一场对话结束（不管是聊完、跳过还是关掉）。</summary>
	public event Action<string>? ConversationEnded;

	/// <summary>刚发过大火的村民这次不理人时给出一句话（null 表示照常聊）。</summary>
	public Func<NPC, string?>? ColdShoulder { get; set; }

	/// <summary>伴侣剧情先问一句（有话想说、打怪回报）；接手了返回 true。</summary>
	public Func<NPC, bool>? StoryBeat { get; set; }

	/// <summary>回复框里能不能托这位村民办事。</summary>
	public Func<string, bool>? ErrandsAvailable { get; set; }

	/// <summary>从回复框打开托他办事的表单（对话先结束）。</summary>
	public Action<NPC>? OpenErrand { get; set; }

	public Func<string, bool>? LoansAvailable { get; set; }

	/// <summary>从回复框打开他的家当页借钱（对话先结束）。</summary>
	public Action<NPC>? OpenLoan { get; set; }

	public bool InDayTransition => _dayTransition;

	/// <summary>节日对话（没有就都按游戏原来的走）。</summary>
	public FestivalSystem? Festivals { get; set; }

	/// <summary>节日上聊了一轮：不走日常的聊天后续。</summary>
	public event Action<string, Exchange>? FestivalTalked;

	public event Action<FestivalInviteOutcome>? FestivalInvited;

	/// <summary>表白/求婚聊完的一轮和计入的好感。</summary>
	public event Action<Exchange, int>? Proposed;

	public ProposalService? Proposals { get; set; }

	/// <summary>节日上没回话：(节日名, 村民)。</summary>
	public event Action<string, string>? FestivalSkipped;

	/// <summary>刚说过话的宽限（约 30 秒）：这时玩家站在村民面前不算盯着看，也不该冒出别的头顶气泡。</summary>
	public const int TalkGraceTicks = 1800;

	/// <summary>玩家正在和这位村民说话、正在等他的台词，或 <paramref name="graceTicks"/> 帧内刚说过。</summary>
	public bool IsTalkingWith(string npcName, int graceTicks = 0)
	{
		bool now = string.Equals(_current?.Name, npcName, StringComparison.OrdinalIgnoreCase)
			|| (Game1.dialogueUp && string.Equals(Game1.currentSpeaker?.Name, npcName, StringComparison.OrdinalIgnoreCase))
			|| _engine.IsGeneratingFor(npcName);
		if (now)
		{
			return true;
		}
		return _lastTalkTick.TryGetValue(npcName, out int t) && Game1.ticks - t >= 0 && Game1.ticks - t < graceTicks;
	}

	// ---------------- 点村民 ----------------

	/// <summary>Harmony 前缀调用。返回 null 表示交给游戏原来的 checkAction；否则是 checkAction 的返回值。</summary>
	public bool? OnCheckAction(NPC npc, Farmer? who, GameLocation location)
	{
		try
		{
			if (who == null || _executingVanilla)
			{
				return null;
			}
			// 过场事件交给游戏；节日上另走一条路。
			if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
			{
				return Festivals?.Active is FestivalPack festival ? OnFestivalCheck(npc, who, location, festival) : null;
			}
			string name = npc.Name;
			bool spouse = GameState.IsSpouse(name);
			if (npc.IsInvisible || (npc.isMoving() && !spouse))
			{
				return null;
			}
			if (string.IsNullOrEmpty(name) || !npc.IsVillager || !_npcs.IsEnabled(npc) || _executingVanilla)
			{
				return null;
			}
			bool allowVanilla = _config().GameLinesWhenAiDown;
			// 手里拿着东西：是送礼或交任务物品，交给游戏。
			if (who.ActiveObject != null)
			{
				return null;
			}
			if (ColdShoulder?.Invoke(npc) is string refusal)
			{
				npc.CurrentDialogue.Clear();
				npc.CurrentDialogue.Push(new GameDialogue(npc, null, GameDialogueText.ForDisplay(refusal, who.Name ?? "")));
				Draw(npc);
				return false;
			}
			if (!_generating && StoryBeat?.Invoke(npc) == true)
			{
				return false;
			}
			if (_generating)
			{
				if (allowVanilla)
				{
					return null;
				}
				if (TryOpenShop(name))
				{
					return false;
				}
				_log.Debug($"另一句 AI 台词还在生成，先不让 {name} 说游戏原来的话。");
				return false;
			}
			if (HasContinuedDialogue(npc))
			{
				return null;
			}
			bool pending = HasPendingAiDialogue(npc);
			if (pending)
			{
				return null;
			}
			if (_ai.IsDialogueUnavailable())
			{
				if (allowVanilla)
				{
					return null;
				}
				if (TryOpenShop(name))
				{
					return false;
				}
				ShowNotices();
				_log.Info($"现在用不了 AI，{name} 这次不说话（设置里没开\"AI 不可用时说游戏原来的台词\"）。");
				return false;
			}
			GameDate today = GameState.Today;
			TodayDialogue? state = _engine.State(name);
			if (state != null && state.SpokeToday && !_engine.CanStartDaily(name, spouse, today) && !_engine.CanResume(name, spouse))
			{
				if (allowVanilla || pending)
				{
					return null;
				}
				if (TryOpenShop(name))
				{
					return false;
				}
				_log.Debug($"今天已经和 {name} 聊过了。");
				return false;
			}
			string? pendingLine = state?.HeldReply;
			bool resumeReaction = !string.IsNullOrWhiteSpace(pendingLine);
			string? again = resumeReaction ? pendingLine : state?.Line;
			if (((state != null && state.Spent && !state.SpokeToday) || _engine.CanResume(name, spouse)) && !string.IsNullOrEmpty(again))
			{
				// 生成过、没聊完（玩家关掉后又点）：直接再显示一次。
				Present(npc, again!, resumeReaction ? state!.HeldFace ?? Emotion.Neutral : null);
				_log.Debug($"再次显示 {name} 之前的话。");
				return false;
			}
			StartGeneration(npc, who, location, allowVanilla);
			return false;
		}
		catch (Exception ex)
		{
			_log.Error("处理点村民时出错：" + ex);
			_generating = false;
			return null;
		}
	}

	/// <summary>换天或回标题之后才到的失败，不再显示到新的一天上。</summary>
	private void ReportIfCurrent(int epoch, string reason)
	{
		if (epoch == _epoch)
		{
			GenerationStatus.ReportFailure(reason);
		}
	}

	private void StartGeneration(NPC npc, Farmer who, GameLocation location, bool allowVanilla)
	{
		string name = npc.Name;
		_generating = true;
		ClearHeadBubble(npc);
		string vanilla = _config().FeedGameLineToAi ? GameState.VanillaLine(npc) : "";
		_log.Debug(vanilla.Length > 0 ? $"拿到 {name} 本来要说的台词（{vanilla.Length} 字）。" : $"{name} 这次没有可参考的游戏台词。");
		DialogueMaterial material = _materials.Build(npc, location?.Name, vanilla);
		GameDate today = GameState.Today;
		TimeSpan budget = TimeSpan.FromSeconds(SlotRouter.DialogueWatchdogSeconds(_config()));
		DateTime started = DateTime.UtcNow;
		int epoch = _epoch;
		CancellationTokenSource cts = new CancellationTokenSource();
		GenerationStatus.Begin(name, _npcs.Names.Display(name), cts, budget);
		Task.Run(async () =>
		{
			string? line = null;
			bool watchdog = false;
			try
			{
				Task<OpeningResult?> task = _engine.OpeningAsync(material, today, cts.Token);
				if (await Task.WhenAny(task, Task.Delay(budget)).ConfigureAwait(false) == task)
				{
					line = (await task.ConfigureAwait(false))?.Dialogue;
				}
				else
				{
					watchdog = true;
					cts.Cancel();
					_log.Warn($"等 {name} 的开场白超过 {budget.TotalSeconds:0} 秒，放弃。");
					ReportIfCurrent(epoch, Tr.Get("thinking.timedOut", "等了 {{seconds}} 秒还没回音。模型可能太慢，换个快一点的模型或调短超时试试。", ("seconds", (int)budget.TotalSeconds)));
				}
			}
			catch (OperationCanceledException)
			{
				ReportIfCurrent(epoch, Tr.Get("thinking.cancelled", "不等了。马上可以再和对方说话。"));
			}
			catch (Exception ex)
			{
				_log.Error($"生成 {name} 的开场白时出错：{ex.Message}");
				ReportIfCurrent(epoch, Tr.Get("thinking.failed", "AI 请求失败，详情在 SMAPI 日志里。"));
			}
			finally
			{
				bool byPlayer = GenerationStatus.CancelledByPlayer(name);
				GenerationStatus.End(name);
				if (string.IsNullOrEmpty(line) && GenerationStatus.Failure == null && !byPlayer)
				{
					ReportIfCurrent(epoch, FailureReason(started));
				}
				cts.Dispose();
			}
			MainThread.Run(() =>
			{
				if (epoch == _epoch)
				{
					FinishGeneration(npc, who, location, line, watchdog, started);
				}
			});
		});
	}

	private void FinishGeneration(NPC npc, Farmer who, GameLocation? location, string? line, bool watchdog, DateTime started)
	{
		try
		{
			ShowNotices();
			if (!string.IsNullOrEmpty(line))
			{
				if (CanPresent(npc, location))
				{
					Present(npc, line, null);
					_log.Debug($"显示 {npc.Name} 的 AI 开场白。");
				}
				else
				{
					_log.Debug($"{npc.Name} 的开场白好了，但玩家已经走开或开了别的菜单，留到下次点他时再说。");
				}
				return;
			}
			// AI 没给出台词：总是退回游戏原来的对话，不让村民一句话都不说。
			if (_config().GameLinesWhenAiDown || watchdog || _ai.FailedSince(started))
			{
				if (CanPresent(npc, location))
				{
					_log.Info($"{npc.Name} 没有生成出台词，改说游戏原来的话。");
					_executingVanilla = true;
					try
					{
						npc.checkAction(who, location ?? npc.currentLocation);
					}
					finally
					{
						_executingVanilla = false;
					}
				}
			}
		}
		catch (Exception ex)
		{
			_log.Error($"显示 {npc.Name} 的台词时出错：{ex.Message}");
		}
		finally
		{
			_generating = false;
		}
	}

	/// <summary>把一句话放进村民的对话里并打开游戏对话框（emotion 不为 null 时附上立绘表情）。</summary>
	private void Present(NPC npc, string text, string? emotion)
	{
		_aiNpcs.Add(npc.Name);
		npc.CurrentDialogue.Clear();
		GameDialogue d = new GameDialogue(npc, null, emotion == null ? GameDialogueText.ForDisplay(text, Game1.player?.Name ?? "") : WithEmotion(npc, text, emotion));
		d.overridePortrait = PortraitOverrides.For(npc.Name, emotion);
		npc.CurrentDialogue.Push(d);
		_pending[npc.Name] = d;
		_current = npc;
		Draw(npc);
	}

	/// <summary>走过来的村民开口：显示准备好的话，现在显示不了就返回 false。</summary>
	public bool PresentPrepared(NPC npc, string line)
	{
		if (!CanPresent(npc, Game1.currentLocation))
		{
			return false;
		}
		Present(npc, line, null);
		return true;
	}

	private void Draw(NPC npc)
	{
		if (_dayTransition || Game1.newDay || !Context.IsWorldReady || InPlainEvent || npc.CurrentDialogue.Count == 0)
		{
			return;
		}
		ClearHeadBubble(npc);
		Game1.drawDialogue(npc);
	}

	/// <summary>过场事件里（节日不算）不插对话。</summary>
	private static bool InPlainEvent => (Game1.eventUp || Game1.currentLocation?.currentEvent != null) && !Game1.isFestival();

	private bool CanPresent(NPC npc, GameLocation? location)
	{
		if (_dayTransition || Game1.newDay || !Context.IsWorldReady || InPlainEvent)
		{
			return false;
		}
		if (location == null || Game1.currentLocation != location || npc.currentLocation != location)
		{
			return false;
		}
		return Game1.activeClickableMenu == null && !Game1.dialogueUp;
	}

	// ---------------- 节日 ----------------

	/// <summary>Harmony 前缀走到这里：点了节日上的村民。null = 交给游戏，false = 我们接手。</summary>
	private bool? OnFestivalCheck(NPC npc, Farmer who, GameLocation location, FestivalPack festival)
	{
		string name = npc.Name;
		bool allowVanilla = _config().GameLinesWhenAiDown;
		if (string.IsNullOrEmpty(name) || !npc.IsVillager || !_npcs.IsEnabled(npc) || who.ActiveObject != null)
		{
			return null;
		}
		if (_generating)
		{
			return allowVanilla ? null : false;
		}
		if (HasContinuedDialogue(npc) || HasPendingAiDialogue(npc))
		{
			return null;
		}
		TodayDialogue? state = _engine.State(name);
		if (state != null && state.Spent && !state.SpokeToday && !string.IsNullOrEmpty(state.Line))
		{
			Present(npc, state.Line, null);
			return false;
		}
		if (Festivals!.HasTalked(name) || state?.SpokeToday == true)
		{
			Festivals.MarkTalked(name);
			return allowVanilla ? null : false;
		}
		if (_ai.IsDialogueUnavailable())
		{
			Festivals.NotifyUnavailable("AI 现在用不了");
			return null;
		}
		if (Game1.activeClickableMenu != null)
		{
			return null;
		}
		_executingVanilla = true;
		try
		{
			npc.checkAction(who, location);
		}
		finally
		{
			_executingVanilla = false;
		}
		if (Game1.activeClickableMenu is DialogueBox && TryReadFestivalLine(npc, out GameDialogue? vanilla, out string line))
		{
			StartFestivalGeneration(npc, location, festival, vanilla, line);
		}
		return false;
	}

	/// <summary>节日上大多数村民是游戏直接开对话框：框打开后下一帧把它换成 AI 的话。</summary>
	private void ScheduleFestivalReplacement(NPC? speaker)
	{
		if (speaker == null || _generating || _executingVanilla || Festivals?.Active is not FestivalPack festival || _aiNpcs.Contains(speaker.Name) || !CanStartFestivalLine(speaker))
		{
			return;
		}
		Game1.delayedActions.Add(new DelayedAction(1, () =>
		{
			try
			{
				if (!_generating && !_executingVanilla && Festivals?.Active == festival && Game1.activeClickableMenu is DialogueBox && string.Equals(Game1.currentSpeaker?.Name, speaker.Name, StringComparison.OrdinalIgnoreCase)
					&& CanStartFestivalLine(speaker) && TryReadFestivalLine(speaker, out GameDialogue? vanilla, out string line))
				{
					StartFestivalGeneration(speaker, Game1.currentLocation, festival, vanilla, line);
				}
			}
			catch (Exception ex)
			{
				_log.Error($"替换 {speaker.Name} 的节日台词时出错：{ex.Message}");
			}
		}));
	}

	private bool CanStartFestivalLine(NPC npc)
	{
		string name = npc.Name;
		if (string.IsNullOrEmpty(name) || !npc.IsVillager || !_npcs.IsEnabled(npc) || Game1.player?.ActiveObject != null || HasPendingAiDialogue(npc))
		{
			return false;
		}
		if (Festivals!.HasTalked(name) || _engine.State(name)?.SpokeToday == true)
		{
			Festivals.MarkTalked(name);
			return false;
		}
		if (_ai.IsDialogueUnavailable())
		{
			Festivals.NotifyUnavailable("AI 现在用不了");
			return false;
		}
		return true;
	}

	/// <summary>游戏这一刻要说的话；带选项、问答的不算闲聊，不碰它。</summary>
	private static bool TryReadFestivalLine(NPC npc, out GameDialogue? dialogue, out string line)
	{
		line = "";
		dialogue = (Game1.activeClickableMenu as DialogueBox)?.characterDialogue ?? (npc.CurrentDialogue.Count > 0 ? npc.CurrentDialogue.Peek() : null);
		string raw = dialogue?.getCurrentDialogue() ?? "";
		if (!FestivalLine.IsPlainChat(raw) || (Game1.activeClickableMenu as DialogueBox)?.isQuestion == true)
		{
			return false;
		}
		line = FestivalLine.Normalize(raw);
		return line.Length > 0;
	}

	private void StartFestivalGeneration(NPC npc, GameLocation? location, FestivalPack festival, GameDialogue? vanilla, string vanillaLine)
	{
		string name = npc.Name;
		_generating = true;
		_dialogueSpeaker = null;
		_current = null;
		(Game1.activeClickableMenu as DialogueBox)?.closeDialogue();
		DialogueMaterial material = _materials.Build(npc, location?.Name, vanillaLine, awarenessFeelings: false, festival: festival);
		GameDate today = GameState.Today;
		TimeSpan budget = TimeSpan.FromSeconds(SlotRouter.DialogueWatchdogSeconds(_config()));
		int epoch = _epoch;
		GameLocation? place = Game1.currentLocation;
		Event? occasion = place?.currentEvent;
		CancellationTokenSource cts = new CancellationTokenSource();
		GenerationStatus.Begin(name, _npcs.Names.Display(name), cts, budget);
		Task.Run(async () =>
		{
			string? line = null;
			string why = "没有生成出台词";
			try
			{
				Task<string?> task = _engine.FestivalOpeningAsync(material, today, cts.Token);
				if (await Task.WhenAny(task, Task.Delay(budget)).ConfigureAwait(false) == task)
				{
					line = await task.ConfigureAwait(false);
				}
				else
				{
					cts.Cancel();
					why = $"等了 {budget.TotalSeconds:0} 秒";
				}
			}
			catch (OperationCanceledException)
			{
				why = "玩家不等了";
			}
			catch (Exception ex)
			{
				why = ex.Message;
			}
			finally
			{
				GenerationStatus.End(name);
				cts.Dispose();
			}
			MainThread.Run(() =>
			{
				_generating = false;
				if (epoch != _epoch || !Context.IsWorldReady || Game1.currentLocation != place || Game1.currentLocation?.currentEvent != occasion || Game1.activeClickableMenu != null || Game1.dialogueUp)
				{
					return;
				}
				if (!string.IsNullOrEmpty(line))
				{
					Present(npc, line, null);
					return;
				}
				Festivals?.NotifyUnavailable(why);
				npc.CurrentDialogue.Clear();
				npc.CurrentDialogue.Push(vanilla ?? new GameDialogue(npc, null, vanillaLine));
				Draw(npc);
			});
		});
	}

	private void SubmitInvite(NPC npc, ReplyMenu menu, DialogueMaterial material, string text, string? currentLine, GameDate today, DateTime started, CancellationToken token)
	{
		Task.Run(async () =>
		{
			try
			{
				FestivalInviteDecision? decision = await _engine.FestivalInviteAsync(material, text, currentLine, today, token).ConfigureAwait(false);
				MainThread.Run(() =>
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (decision == null || Festivals?.ApplyInvite(npc, decision) != true)
					{
						_log.Warn($"{npc.Name} 对节日邀请没有给出能用的答复。");
						HandleFailedReply(npc, menu, started);
						return;
					}
					ApplyInvite(npc, menu, material, text, currentLine, decision, today);
				});
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				_log.Debug($"玩家不等 {npc.Name} 的答复了。");
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					_log.Error($"处理节日邀请时出错：{ex.Message}");
					MainThread.Run(() => HandleFailedReply(npc, menu, started));
				}
			}
		});
	}

	private void ApplyInvite(NPC npc, ReplyMenu menu, DialogueMaterial material, string text, string? currentLine, FestivalInviteDecision d, GameDate today)
	{
		FestivalPack festival = material.Festival!;
		_engine.CommitInvite(material, text, currentLine, d, today);
		_log.Info($"{npc.Name} 对「{festival.Action?.Label}」的答复：{(d.Accepted ? "答应" : "拒绝")}（{d.ReasonCode}）");
		Festivals?.MarkTalked(npc.Name);
		_aiNpcs.Remove(npc.Name);
		int heartsBefore = _relationships.EffectiveHearts(npc.Name);
		_relationships.React(npc, d.Emotion, d.FriendshipPoints);
		menu.ReleaseKeyboard();
		ShowReaction(npc, d.Reaction, d.Emotion);
		int points = _relationships.ClampInteraction(d.FriendshipPoints);
		_relationships.Add(npc.Name, points);
		try
		{
			FestivalInvited?.Invoke(new FestivalInviteOutcome(festival, npc, text, DialogueEngine.FestivalInviteText(festival, d), d, points, heartsBefore, currentLine ?? ""));
		}
		catch (Exception ex)
		{
			_log.Warn($"节日邀请之后的处理出错（{npc.Name}）：{ex.Message}");
		}
		ResetReplyState();
		CloseIfActive(menu);
	}

	private void SubmitProposal(NPC npc, ReplyMenu menu, DialogueMaterial material, ProposalKind kind, string text, bool keepSecret, string? currentLine, GameDate today, DateTime started, CancellationToken token)
	{
		ProposalService proposals = Proposals!;
		string status = proposals.Status(npc.Name), narrative = proposals.Narrative(npc.Name), vanilla = proposals.VanillaBlock(npc.Name);
		Task.Run(async () =>
		{
			try
			{
				ProposalDecision? d = await _engine.ProposalAsync(material, kind, text, currentLine, status, narrative, vanilla, today, token).ConfigureAwait(false);
				MainThread.Run(() =>
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (d == null)
					{
						HandleFailedReply(npc, menu, started);
						return;
					}
					string? failure = proposals.Apply(npc, kind, d, text);
					if (failure != null)
					{
						_aiNpcs.Remove(npc.Name);
						menu.ReleaseKeyboard();
						ShowReaction(npc, failure, "sad");
						ResetReplyState();
						CloseIfActive(menu);
						return;
					}
					ApplyProposal(npc, menu, material, kind, text, keepSecret, currentLine, d, today);
				});
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				_log.Debug($"玩家不等 {npc.Name} 的答复了。");
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					_log.Error($"处理表白求婚时出错：{ex.Message}");
					MainThread.Run(() => HandleFailedReply(npc, menu, started));
				}
			}
		});
	}

	private void ApplyProposal(NPC npc, ReplyMenu menu, DialogueMaterial material, ProposalKind kind, string text, bool keepSecret, string? currentLine, ProposalDecision d, GameDate today)
	{
		string opening = _engine.State(npc.Name)?.Line ?? "";
		_engine.CommitProposal(material, kind, text, currentLine, d, today);
		_aiNpcs.Remove(npc.Name);
		_relationships.React(npc, d.Face, d.Points);
		menu.ReleaseKeyboard();
		ShowReaction(npc, d.Line, d.Face);
		int points = _relationships.ClampInteraction(d.Points);
		_relationships.Add(npc.Name, points);
		NpcReaction reaction = new NpcReaction { Reaction = d.Line, Emotion = d.Face, FriendshipPoints = d.Points };
		try
		{
			Proposed?.Invoke(new Exchange(npc, text, d.Line, keepSecret, reaction, currentLine ?? "", opening, "relationship_proposal:" + (kind == ProposalKind.Dating ? "dating" : "marriage"), material.Hearts), points);
		}
		catch (Exception ex)
		{
			_log.Warn($"表白求婚之后的处理出错（{npc.Name}）：{ex.Message}");
		}
		_engine.CompleteDaily(npc.Name, today);
		ResetReplyState();
		CloseIfActive(menu);
	}

	// ---------------- 对话框和回复框 ----------------

	/// <summary>村民说一段不走 AI 流程的话（交付道谢、伴侣剧情的台词）。</summary>
	public void SayNative(NPC npc, string text)
	{
		_nativeSpeaker = npc.Name;
		npc.CurrentDialogue.Push(new StardewValley.Dialogue(npc, null, text));
		Game1.drawDialogue(npc);
		if (!Game1.dialogueUp)
		{
			_nativeSpeaker = null;
		}
	}

	public void OnMenuChanged(object? sender, MenuChangedEventArgs e)
	{
		if (_dayTransition)
		{
			ResetReplyState();
			return;
		}
		// 过场事件里的对话框不归我们管（节日除外）。
		if (!Context.IsWorldReady || ((Game1.eventUp || Game1.currentLocation?.currentEvent != null) && !Game1.isFestival()))
		{
			_dialogueSpeaker = null;
			return;
		}
		if (e.OldMenu is ReplyMenu replaced)
		{
			replaced.ReleaseKeyboard();
			// 被别的菜单顶掉或被直接关掉（不是显示反应，也不是它自己的取消）：当作关掉，否则当天和谁都接不上流程。
			if (e.NewMenu is not DialogueBox && replaced == _menu)
			{
				OnProcessingCancelled();
				OnCancelled();
			}
		}
		// 反应框被别的菜单顶掉：这场对话接不下去了，别让之后任意一个对话框关闭时弹出旧村民的回复框。
		if (e.OldMenu is DialogueBox && e.NewMenu != null && e.NewMenu is not DialogueBox && !_waitingForReply && _remainingTurns > 0)
		{
			OnCancelled();
		}
		if (e.NewMenu is DialogueBox && e.OldMenu is not DialogueBox)
		{
			ScheduleFestivalReplacement(Game1.currentSpeaker);
			_dialogueSpeaker = Game1.currentSpeaker;
			if (_dialogueSpeaker != null && _aiNpcs.Contains(_dialogueSpeaker.Name) && _dialogueSpeaker.Name != _nativeSpeaker)
			{
				_current = _dialogueSpeaker;
			}
		}
		if (e.OldMenu is DialogueBox && e.NewMenu == null && _nativeSpeaker != null)
		{
			_nativeSpeaker = null;
			_dialogueSpeaker = null;
			_current = null;
			return;
		}
		if (e.OldMenu is DialogueBox && e.NewMenu == null && !_waitingForReply)
		{
			NPC? npc = _current ?? _dialogueSpeaker;
			_dialogueSpeaker = null;
			if (npc != null)
			{
				ClearConsumedDialogue(npc);
			}
			if (TryAskCounter(npc))
			{
				_current = null;
				return;
			}
			if (_remainingTurns > 0 && _replyingTo != null && _lastReaction != null)
			{
				string line = _lastReaction;
				string? emotion = _lastEmotion;
				_lastReaction = null;
				_lastEmotion = null;
				OpenReplyMenu(_replyingTo, line, emotion, multiTurn: true);
				return;
			}
			if (Outings?.TryStartHere() == true)
			{
				_current = null;
				return;
			}
			if (npc == null)
			{
				_current = null;
				return;
			}
			if (ShouldOpenReplyMenu(npc))
			{
				TodayDialogue? state = _engine.State(npc.Name);
				if (state == null)
				{
					EndConversation(npc.Name);
					_current = null;
					return;
				}
				bool resume = !string.IsNullOrWhiteSpace(state.HeldReply);
				if (resume)
				{
					_remainingTurns = state.HeldTurns;
				}
				if (!resume && TryAskOffer(npc))
				{
					_current = null;
					return;
				}
				_replyingTo = npc;
				OpenReplyMenu(npc, resume ? state.HeldReply! : state.Line, resume ? state.HeldFace : null, resume);
			}
			else
			{
				EndConversation(npc.Name);
				_current = null;
			}
		}
		if (e.OldMenu is ReplyMenu && e.NewMenu == null && !_waitingForReply)
		{
			_current = null;
		}
	}

	private bool ShouldOpenReplyMenu(NPC npc)
	{
		if (_current == null || !_aiNpcs.Contains(npc.Name))
		{
			return false;
		}
		TodayDialogue? state = _engine.State(npc.Name);
		if (state == null || !state.Spent || state.SpokeToday)
		{
			return _engine.CanResume(npc.Name, GameState.IsSpouse(npc.Name));
		}
		return true;
	}

	private void OpenReplyMenu(NPC npc, string line, string? emotion, bool multiTurn)
	{
		_waitingForReply = true;
		_replyingTo = npc;
		FestivalPack? festival = Festivals?.Active;
		FestivalAction? invite = festival == null ? null : Festivals!.OfferFor(npc.Name);
		ProposalOptions proposals = festival == null ? Proposals?.Options(npc) ?? ProposalOptions.None : ProposalOptions.None;
		OutingOptions outings = festival == null ? Outings?.Options(npc, multiTurn) ?? OutingOptions.None : OutingOptions.None;
		bool errands = festival == null && ErrandsAvailable?.Invoke(npc.Name) == true;
		bool loans = festival == null && LoansAvailable?.Invoke(npc.Name) == true;
		ReplyMenu menu = new ReplyMenu(npc, _npcs.Names.Display(npc.Name), line, emotion, multiTurn, _relationships.EffectiveHearts(npc.Name), festival != null, invite?.Label, invite?.Tip, proposals, outings, errands, loans);
		menu.Submitted += OnSubmitted;
		menu.ErrandRequested += () =>
		{
			OnCancelled();
			OpenErrand?.Invoke(npc);
		};
		menu.LoanRequested += () =>
		{
			OnCancelled();
			OpenLoan?.Invoke(npc);
		};
		menu.Cancelled += OnCancelled;
		menu.Skipped += OnSkipped;
		menu.ProcessingCancelled += OnProcessingCancelled;
		_menu = menu;
		Game1.activeClickableMenu = menu;
	}

	private void OnSubmitted(string text, string? proposedAction, bool keepSecret)
	{
		NPC? npc = _replyingTo;
		ReplyMenu? menu = _menu;
		if (npc == null)
		{
			return;
		}
		_engine.State(npc.Name)?.PreservePendingSpouseResponse(menu?.DisplayedLine ?? _lastReaction, _remainingTurns, menu?.DisplayedEmotion ?? _lastEmotion);
		DialogueMaterial material = _materials.Build(npc, festival: Festivals?.Active);
		GameDate today = GameState.Today;
		DateTime started = DateTime.UtcNow;
		string? currentLine = menu?.DisplayedLine;
		CancellationTokenSource cts = _replyCts = new CancellationTokenSource();
		CancellationToken token = cts.Token;
		if (menu?.Inviting == true && material.Festival?.Action != null)
		{
			SubmitInvite(npc, menu, material, text, currentLine, today, started, token);
			return;
		}
		if (menu?.Proposal is ProposalKind kind && material.Festival == null && Proposals != null)
		{
			SubmitProposal(npc, menu, material, kind, text, keepSecret, currentLine, today, started, token);
			return;
		}
		if (menu?.Outing is OutingKind outing && material.Festival == null && Outings != null)
		{
			SubmitOuting(npc, menu, material, outing, text, keepSecret, currentLine, today, started, token);
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				ReplyResult? result = await _engine.ReplyAsync(material, text, today, currentLine, proposedAction, token).ConfigureAwait(false);
				MainThread.Run(() =>
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (result != null)
					{
						ApplyReply(npc, menu, material, text, keepSecret, result, proposedAction);
					}
					else
					{
						_log.Warn($"{npc.Name} 没有给出反应。");
						HandleFailedReply(npc, menu, started);
					}
				});
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				_log.Debug($"玩家不等 {npc.Name} 的回应了。");
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					_log.Error($"处理对 {npc.Name} 的回复时出错：{ex.Message}");
					MainThread.Run(() => HandleFailedReply(npc, menu, started));
				}
			}
		});
	}

	private void ApplyReply(NPC npc, ReplyMenu? menu, DialogueMaterial material, string text, bool keepSecret, ReplyResult result, string? proposedAction)
	{
		NpcReaction reaction = result.Reaction;
		if (material.Festival == null && reaction.Proposal != null && ReviewProposal != null)
		{
			try
			{
				ReviewProposal(npc, reaction, material.Hearts);
			}
			catch (Exception ex)
			{
				_log.Warn($"{npc.Name} 的提议没审成：{ex.Message}");
				reaction.Proposal = null;
			}
			result = result with { DisplayText = ReactionText.ForDisplay(reaction) };
		}
		GameDate today = GameState.Today;
		string opening = _engine.State(npc.Name)?.Line ?? "";
		_engine.CommitReply(material, text, result, today, material.ReplyHistoryNote);
		_aiNpcs.Remove(npc.Name);
		_relationships.React(npc, reaction.Emotion, reaction.FriendshipPoints);
		// 回复框上面那句是村民刚说过的：新回复里原样重复它的句子去掉。
		string alreadySaid = menu?.DisplayedLine ?? "";
		string shown = TextTools.CollapseRepeats(result.DisplayText, alreadySaid);
		string history = TextTools.CollapseRepeats(ReactionText.ForHistory(reaction), alreadySaid);
		if (reaction.ActionResponse != null)
		{
			_log.Debug($"{npc.Name} 对提议的答复（{(reaction.ActionResponse.Accepted ? "答应" : "拒绝")}）：{ReactionText.ActionLogReason(reaction.ActionResponse)}");
			string accepted = ReactionText.AcceptedEvent(reaction.ActionResponse);
			if (accepted.Length > 0)
			{
				Game1.chatBox?.addMessage(accepted, new Color(255, 215, 0));
			}
		}
		menu?.ReleaseKeyboard();
		ShowReaction(npc, shown, reaction.Emotion);
		int points = _relationships.ClampInteraction(reaction.FriendshipPoints);
		_relationships.Add(npc.Name, points);
		try
		{
			Exchange exchange = new Exchange(npc, text, string.IsNullOrWhiteSpace(history) ? reaction.Reaction : history, keepSecret, reaction, result.CurrentLine, opening, proposedAction, material.Hearts);
			if (material.Festival != null)
			{
				Festivals?.MarkTalked(npc.Name);
				FestivalTalked?.Invoke(material.Festival.Name, exchange);
			}
			else
			{
				RaiseTalked(exchange);
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"聊天之后的处理出错（{npc.Name}）：{ex.Message}");
		}
		ContinueOrFinish(npc, menu, history, reaction.Emotion);
		if (_config().Verbose)
		{
			_log.Debug($"[回复] {npc.Name} 原始得分 {reaction.FriendshipPoints}，计入 {points}，情绪 {reaction.Emotion}，还能聊 {_remainingTurns} 轮");
		}
	}

	/// <summary>一个订阅者出错不影响后面的。</summary>
	private void RaiseTalked(Exchange exchange)
	{
		foreach (Action<Exchange> handler in Talked?.GetInvocationList().Cast<Action<Exchange>>() ?? Enumerable.Empty<Action<Exchange>>())
		{
			try
			{
				handler(exchange);
			}
			catch (Exception ex)
			{
				_log.Warn($"聊天之后的处理出错（{exchange.Npc.Name}）：{ex.Message}");
			}
		}
	}

	private void ShowReaction(NPC npc, string text, string emotion)
	{
		if (_dayTransition || Game1.newDay || InPlainEvent)
		{
			return;
		}
		GameDialogue d = new GameDialogue(npc, null, WithEmotion(npc, text, emotion));
		d.overridePortrait = PortraitOverrides.For(npc.Name, emotion);
		npc.CurrentDialogue.Push(d);
		_pending[npc.Name] = d;
		Draw(npc);
	}

	/// <summary>第一次回复后剩余轮数 = 每场轮数 − 1，之后每轮减 1；到 0 结束这场对话。</summary>
	private void ContinueOrFinish(NPC npc, ReplyMenu? menu, string reactionText, string? emotion)
	{
		_engine.State(npc.Name)?.ClearPendingResponse();
		_remainingTurns = _remainingTurns <= 0 ? _config().RepliesPerTalk - 1 : _remainingTurns - 1;
		if (_remainingTurns > 0)
		{
			_lastReaction = string.IsNullOrWhiteSpace(reactionText) ? null : reactionText;
			_lastEmotion = emotion;
			CloseIfActive(menu);
			_waitingForReply = false;
			_replyingTo = npc;
			_menu = null;
		}
		else
		{
			// 对话结束的通知等最后这句反应的对话框关掉时发（见 OnMenuChanged）。
			_engine.CompleteDaily(npc.Name, GameState.Today);
			ResetReplyState();
			CloseIfActive(menu);
		}
	}

	private void HandleFailedReply(NPC npc, ReplyMenu? menu, DateTime started)
	{
		string reason = FailureReason(started);
		ShowNotices();
		if (menu != null && Game1.activeClickableMenu == menu && _replyingTo == npc && !_dayTransition)
		{
			menu.ShowFailure(reason);
			return;
		}
		ResetReplyState();
		CloseIfActive(menu);
		EndConversation(npc.Name);
		_current = null;
	}

	private void OnCancelled()
	{
		NPC? npc = _replyingTo;
		ReplyMenu? menu = _menu;
		TodayDialogue? state = npc == null ? null : _engine.State(npc.Name);
		state?.PreservePendingSpouseResponse(menu?.DisplayedLine ?? _lastReaction, _remainingTurns, menu?.DisplayedEmotion ?? _lastEmotion);
		ResetReplyState();
		if (npc != null)
		{
			_log.Debug($"关掉了和 {npc.Name} 的回复框" + (state?.HeldTurns > 0 ? "（配偶的这场留着下次接着聊）。" : "。"));
			EndConversation(npc.Name);
		}
		_current = null;
	}

	private void OnProcessingCancelled()
	{
		CancellationTokenSource? cts = _replyCts;
		_replyCts = null;
		cts?.Cancel();
	}

	/// <summary>不回了：这场对话算聊过并结束，加 SkipFriendshipPoints 好感，记一条"跳过"。</summary>
	private void OnSkipped()
	{
		NPC? npc = _replyingTo;
		ResetReplyState();
		if (npc != null)
		{
			_aiNpcs.Remove(npc.Name);
			GameDate today = GameState.Today;
			FestivalPack? festival = Festivals?.Active;
			Festivals?.MarkTalked(npc.Name);
			ConversationEntry? skipped = _engine.CommitSkip(npc.Name, Game1.player.Name, today, festival);
			int points = _config().WalkAwayPoints;
			_relationships.Add(npc.Name, points);
			_log.Debug($"没有回 {npc.Name} 的话，好感 +{points}。");
			try
			{
				if (festival != null)
				{
					FestivalSkipped?.Invoke(festival.Name, npc.Name);
				}
				else if (skipped != null)
				{
					Skipped?.Invoke(npc.Name, skipped.Opener);
				}
			}
			catch (Exception ex)
			{
				_log.Warn($"不回话之后的处理出错（{npc.Name}）：{ex.Message}");
			}
			EndConversation(npc.Name);
		}
		_current = null;
	}

	private void ResetReplyState()
	{
		_waitingForReply = false;
		_replyingTo = null;
		_menu = null;
		_remainingTurns = 0;
		_lastReaction = null;
		_lastEmotion = null;
	}

	private void EndConversation(string npcName)
	{
		try
		{
			ConversationEnded?.Invoke(npcName);
		}
		catch (Exception ex)
		{
			_log.Warn($"对话结束后的处理出错（{npcName}）：{ex.Message}");
		}
	}

	// ---------------- 每帧、按键、换天 ----------------

	public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
	{
		MainThread.Drain();
		if (_current != null)
		{
			_lastTalkTick[_current.Name] = Game1.ticks;
		}
		else if (Game1.dialogueUp && Game1.currentSpeaker != null)
		{
			_lastTalkTick[Game1.currentSpeaker.Name] = Game1.ticks;
		}
		ModConfig config = _config();
		if (config.PauseClockWhileWriting && Context.IsWorldReady && !Context.IsMultiplayer && GenerationStatus.IsActive)
		{
			Game1.gameTimeInterval = 0;
		}
		if (e.IsMultipleOf(30))
		{
			ShowNotices();
		}
		GuardEmptyDialogue();
	}

	/// <summary>
	/// 对话框状态还开着、菜单却没了、村民的话也空了（别的 mod 动过时可能出现）：游戏绘制时会一直报错，这里收掉。
	/// 不给绘制方法打补丁，所以最多有一帧报错。
	/// </summary>
	private void GuardEmptyDialogue()
	{
		if (!Game1.dialogueUp || Game1.activeClickableMenu is DialogueBox || Game1.currentSpeaker is not { } speaker || speaker.CurrentDialogue.Count > 0)
		{
			return;
		}
		_log.Warn($"{speaker.Name} 的对话框状态还开着，但已经没有话可显示，收掉。");
		Game1.dialogueUp = false;
		Game1.currentSpeaker = null;
		_aiNpcs.Remove(speaker.Name);
		_pending.Remove(speaker.Name);
		if (string.Equals(_current?.Name, speaker.Name, StringComparison.OrdinalIgnoreCase))
		{
			_current = null;
		}
	}

	public void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
	{
		if (GenerationStatus.IsActive && (e.Button == SButton.Escape || e.Button == SButton.ControllerB) && GenerationStatus.Cancel())
		{
			_helper.Input.Suppress(e.Button);
			_log.Debug("玩家取消了正在生成的开场白。");
		}
	}

	/// <summary>睡觉换天前：关掉回复框和我们打开的对话框，清掉当天状态。</summary>
	public void BeginDayTransition()
	{
		_dayTransition = true;
		try
		{
			if (Game1.activeClickableMenu is ReplyMenu menu)
			{
				menu.CloseMenu();
			}
			NPC? speaker = Game1.currentSpeaker;
			if (Game1.dialogueUp && ((speaker != null && _aiNpcs.Contains(speaker.Name)) || (_current != null && _aiNpcs.Contains(_current.Name))))
			{
				Game1.dialogueUp = false;
				if (Game1.activeClickableMenu is DialogueBox)
				{
					try
					{
						Game1.exitActiveMenu();
					}
					catch
					{
						Game1.activeClickableMenu = null;
					}
				}
				Game1.currentSpeaker = null;
			}
		}
		catch (Exception ex)
		{
			_log.Warn("换天前收拾对话界面时出错：" + ex.Message);
		}
		ResetDaily();
	}

	public void EndDayTransition() => _dayTransition = false;

	public void ResetDaily()
	{
		_epoch++;
		GenerationStatus.Abort();
		OnProcessingCancelled();
		_aiNpcs.Clear();
		_pending.Clear();
		_current = null;
		_counter = null;
		_generating = false;
		_executingVanilla = false;
		_loginNoticeShown = false;
		ResetReplyState();
		GenerationStatus.Reset();
		MainThread.Clear();
	}

	/// <summary>把 AI 的状态提示（网关离线、额度用完……）显示到聊天框；需要登录时提示一次。</summary>
	public void ShowNotices()
	{
		foreach (AiNotice n in _ai.TakeNotices())
		{
			Color color = n.Tone == NoticeTone.Error ? Color.Red : n.Tone == NoticeTone.Warning ? new Color(255, 165, 0) : new Color(255, 215, 0);
			string text = n.TextKey switch
			{
				"status.gatewayOffline" => Tr.Get("status.gatewayOffline", "连不上 AI 网关，暂时改说游戏原来的台词。"),
				"status.serverQuota" => Tr.Get("status.serverQuota", "今天的 AI 额度用完了，村民先说游戏原来的台词，明天会再送。"),
				"status.serverBanned" => Tr.Get("status.serverBanned", "AI星露谷 账号已停用，村民先说游戏原来的台词。"),
				"status.serverMaintenance" => Tr.Get("status.serverMaintenance", "AI星露谷 服务器维护中，村民先说游戏原来的台词。"),
				_ => n.TextKey
			};
			Chat(text, color);
		}
		if (_ai.UsesServer && !_loginNoticeShown && _account?.LoginRequested == true)
		{
			_loginNoticeShown = true;
			_account.ConsumeLoginRequest();
			Chat(Tr.Get("status.loginNeeded", "还没登录 AI星露谷 账号：在 SMAPI 控制台输入 aisv_login 用户名 密码 登录后，村民就会用 AI 说话。"), new Color(255, 165, 0));
		}
	}

	private ServerAccount? _account;

	/// <summary>这次请求之后记下的失败才算数，否则是别的原因（比如回复解析不出来），用通用说法。</summary>
	private string FailureReason(DateTime since) => _ai.LastFailureUtc >= since ? FailureText.Describe(_ai.LastFailure, _ai.LastFailureMessage) : FailureText.Describe(FailureKind.Other, null);

	/// <summary>需要登录时提示用（第 7 期有登录窗口后换掉）。</summary>
	public void UseAccount(ServerAccount account) => _account = account;

	private static void Chat(string text, Color color)
	{
		if (Game1.chatBox != null)
		{
			Game1.chatBox.addMessage(text, color);
		}
		else
		{
			Game1.addHUDMessage(new HUDMessage(text, 3));
		}
	}

	// ---------------- 小工具 ----------------

	private bool TryOpenShop(string npcName)
	{
		if (!ShopOwners.Contains(npcName))
		{
			return false;
		}
		try
		{
			Dictionary<string, ShopData> shops = Game1.content.Load<Dictionary<string, ShopData>>("Data/Shops");
			string? id = shops.Where(kv => kv.Value?.Owners != null)
				.SelectMany(kv => kv.Value.Owners.Where(o => o != null && o.Type == ShopOwnerType.NamedNpc && string.Equals(o.Name, npcName, StringComparison.OrdinalIgnoreCase)).Select(_ => kv.Key))
				.FirstOrDefault();
			return !string.IsNullOrWhiteSpace(id) && Utility.TryOpenShopMenu(id, npcName);
		}
		catch (Exception ex)
		{
			_log.Debug($"给 {npcName} 开店失败：{ex.Message}");
			return false;
		}
	}

	private static bool HasContinuedDialogue(NPC npc) => npc.CurrentDialogue.Count > 0 && (npc.CurrentDialogue.Peek()?.isCurrentStringContinuedOnNextScreen ?? false);

	private bool HasPendingAiDialogue(NPC npc)
	{
		if (!_pending.TryGetValue(npc.Name, out GameDialogue? d))
		{
			return false;
		}
		if (npc.CurrentDialogue.Count > 0 && npc.CurrentDialogue.Contains(d))
		{
			return true;
		}
		_pending.Remove(npc.Name);
		return false;
	}

	/// <summary>对话框关掉后，把我们塞进去、已经看完的那句从村民的对话栈里清掉。</summary>
	private void ClearConsumedDialogue(NPC npc)
	{
		if (!_pending.TryGetValue(npc.Name, out GameDialogue? d))
		{
			return;
		}
		_pending.Remove(npc.Name);
		if (npc.CurrentDialogue.Count > 0 && npc.CurrentDialogue.Contains(d))
		{
			npc.CurrentDialogue.Clear();
		}
	}

	private void ClearHeadBubble(NPC npc)
	{
		try
		{
			_helper.Reflection.GetField<string?>(npc, "textAboveHead", required: false)?.SetValue(null);
			_helper.Reflection.GetField<int>(npc, "textAboveHeadTimer", required: false)?.SetValue(0);
			_helper.Reflection.GetField<int>(npc, "textAboveHeadPreTimer", required: false)?.SetValue(0);
		}
		catch (Exception ex)
		{
			_log.Trace($"清 {npc.Name} 头顶气泡失败：{ex.Message}");
		}
	}

	private static void CloseIfActive(ReplyMenu? menu)
	{
		if (menu != null && Game1.activeClickableMenu == menu)
		{
			menu.CloseMenu();
		}
	}

	/// <summary>反应带立绘表情（生气时的退路见 <see cref="Emotion.PortraitCommandFor"/>）。</summary>
	private static string WithEmotion(NPC npc, string text, string emotion)
	{
		int tiles = 0;
		try
		{
			tiles = npc.Portrait is { IsDisposed: false } p && p.Width >= 64 && p.Height >= 64 ? p.Width / 64 * (p.Height / 64) : 0;
		}
		catch
		{
		}
		return GameDialogueText.ForDisplay(text, Game1.player?.Name ?? "") + Emotion.PortraitCommandFor(npc.Name, emotion, tiles);
	}
}
