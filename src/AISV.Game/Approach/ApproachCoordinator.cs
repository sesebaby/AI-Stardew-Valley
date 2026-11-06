using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Approach;
using AISV.Core.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;
using StardewValley.Pathfinding;
using AISV.Game.Npcs;

namespace AISV.Game.Approach;

/// <summary>一次"村民走过来找你"的请求。</summary>
public sealed class ApproachIntent
{
	public ApproachIntent(string source, string requestId, string npcName, string dedupeKey, string locationName, int timeOfDay)
	{
		Source = source;
		RequestId = requestId;
		NpcName = npcName;
		DedupeKey = dedupeKey;
		LocationName = locationName;
		TimeOfDay = timeOfDay;
	}

	public string Source { get; }

	public string RequestId { get; }

	public string NpcName { get; }

	public string DedupeKey { get; }

	public string LocationName { get; }

	public int TimeOfDay { get; }

	public double Relevance { get; init; } = 1.0;

	public double DistanceTiles { get; init; }

	public int Priority { get; init; } = 1;

	public int TtlTicks { get; init; } = 7200;

	/// <summary>后台生成要说的话。</summary>
	public Func<CancellationToken, Task<string?>>? PrepareDialogue { get; init; }

	public string? FallbackDialogue { get; init; }

	/// <summary>准备超过这么多 tick 就改用备用台词（0 = 不限）。</summary>
	public int PreparationTimeoutTicks { get; init; }

	/// <summary>边生成边走。</summary>
	public bool StartWhilePreparing { get; init; }

	/// <summary>走得快一点。</summary>
	public bool UrgentApproach { get; init; }

	/// <summary>走过来时头顶显示"!"。</summary>
	public bool ShowAttentionMarker { get; init; }

	public int? AnnounceEmote { get; init; }

	public string? AnnounceText { get; init; }

	public string? AnnounceTextNpcName { get; init; }

	public Func<bool>? CanStartNow { get; init; }

	/// <summary>(用的是不是备用台词) → 还要不要说。</summary>
	public Func<bool, bool>? CanPresent { get; init; }

	/// <summary>自己打开对话（不给就用默认的 AI 对话框）。</summary>
	public Func<bool>? Present { get; init; }

	public Action<ApproachOutcome>? OnFinished { get; init; }
}

/// <summary>
/// 村民走过来找玩家：一次只准备一句、一次只走一个人。走到玩家旁边的空格、面向玩家、打开对话；
/// 对话结束后走回原处，恢复原来的行走路线。存档、换天、过场、换图、回标题时立刻放回原位。只在主机上跑。
/// </summary>
public sealed class ApproachCoordinator
{
	private const int SpeechOpenTimeoutTicks = 900;

	private const int UrgentSpeed = 6;

	/// <summary>找不到路时，离农夫这么近就地开口（帐篷这种小地方走不开）。</summary>
	private const int NoPathTalkTiles = 4;

	private const int ReturnStallTicks = 120;

	private const int MaxLeaseTicks = 36000;

	private const int ReturnTimeoutTicks = 1200;

	private const int StartCooldownTicks = 300;

	private static readonly TimeSpan MaxPreparation = TimeSpan.FromSeconds(120);

	private sealed record Prepared(long Version, string RequestId, string? Dialogue);

	private sealed class Lease
	{
		public Lease(NPC npc, int tick)
		{
			Npc = npc;
			LocationName = npc.currentLocation?.NameOrUniqueName ?? "";
			Position = npc.Position;
			Facing = npc.FacingDirection;
			Speed = npc.speed;
			IgnoreSchedule = npc.ignoreScheduleToday;
			OriginalController = npc.controller;
			StartedTick = tick;
		}

		public NPC Npc { get; }

		public string LocationName { get; }

		public Vector2 Position { get; }

		public int Facing { get; }

		public int Speed { get; }

		public bool IgnoreSchedule { get; }

		public PathFindController? OriginalController { get; }

		public int StartedTick { get; }
	}

	private sealed class Active
	{
		public Active(ApproachIntent intent, Lease lease, int tick)
		{
			Intent = intent;
			Lease = lease;
			StateTick = tick;
		}

		public ApproachIntent Intent { get; }

		public Lease Lease { get; }

		public NPC Npc => Lease.Npc;

		public string? Dialogue { get; set; }

		public bool UsingFallback { get; set; }

		public bool PreparationTimedOut { get; set; }

		public ApproachNavigation Navigation { get; } = new ApproachNavigation();

		public (int X, int Y)? ArrivedAt { get; set; }

		public bool WaitingForDialogue { get; set; }

		public bool SpeedApplied { get; set; }

		public int IdleTicks { get; set; }

		public int StateTick { get; set; }

		public int ReturnStartedTick { get; set; }

		public Vector2 ReturnLastPosition { get; set; }

		public int ReturnLastProgressTick { get; set; }
	}

	/// <summary>我们自己的寻路，用来和别的控制器区分开。</summary>
	private sealed class Walk : PathFindController
	{
		public Walk(Character c, GameLocation location, Point end, int facing) : base(c, location, end, facing, clearMarriageDialogues: false)
		{
		}

		public override bool update(GameTime time)
		{
			if (pathToEndPoint == null || pathToEndPoint.Count == 0)
			{
				return true;
			}
			return location.farmers.Any() && base.update(time);
		}
	}

	private readonly Func<string, bool> _isSourceEnabled;

	private readonly Func<string, bool> _isNpcGenerating;

	private readonly Func<bool> _isDialogueBusy;

	private readonly Func<bool> _isDayTransition;

	private readonly Func<NPC, string, bool> _presentDefault;

	private readonly ILog _log;

	private readonly ApproachQueue _queue = new ApproachQueue(6, StartCooldownTicks);

	private readonly Dictionary<string, ApproachIntent> _intents = new Dictionary<string, ApproachIntent>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentQueue<Prepared> _results = new ConcurrentQueue<Prepared>();

	private readonly HashSet<string> _fallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _timedOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _conversationEnded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly List<(ApproachIntent Intent, ApproachOutcome Outcome)> _rejected = new List<(ApproachIntent, ApproachOutcome)>();

	private CancellationTokenSource? _prepCts;

	private string? _preparingId;

	private long _prepVersion;

	private int _prepStarted;

	private Active? _active;

	private int _tick;

	public ApproachCoordinator(Func<string, bool> isSourceEnabled, Func<string, bool> isNpcGenerating, Func<bool> isDialogueBusy, Func<bool> isDayTransition, Func<NPC, string, bool> presentDefault, ILog log)
	{
		_isSourceEnabled = isSourceEnabled;
		_isNpcGenerating = isNpcGenerating;
		_isDialogueBusy = isDialogueBusy;
		_isDayTransition = isDayTransition;
		_presentDefault = presentDefault;
		_log = log;
	}

	public string Describe()
	{
		string entries = string.Join("；", _queue.Tracked.Select(e => $"{e.Request.NpcName} {e.State}"));
		return $"tick {_tick}，正在走 {_active?.Intent.NpcName ?? "无"}，准备中 {_preparingId ?? "无"}，队伍：{(entries.Length > 0 ? entries : "空")}，冒感叹号 {AttentionNpc()?.Name ?? "无"}";
	}

	/// <summary>队伍之外也想冒感叹号的村民（比如发现被人跟着）。</summary>
	public Func<string?>? ExtraAttention { get; set; }

	public int Tick => _tick;

	public bool IsBusy => _active != null || _preparingId != null;

	/// <summary>这位村民正在走过来或正在说。</summary>
	public bool IsNpcBusy(string npc) => _active != null && Same(_active.Intent.NpcName, npc);

	/// <summary>这位村民在走、在说或在排队。</summary>
	public bool IsNpcInvolved(string npc) => IsNpcBusy(npc) || _intents.Values.Any(i => Same(i.NpcName, npc));

	public bool HadConversationSince(string npc, int tick) => _conversationEnded.TryGetValue(npc, out int t) && t > tick;

	public EnqueueResult Enqueue(ApproachIntent intent)
	{
		if (!_isSourceEnabled(intent.Source) || !Context.IsMainPlayer || !Context.IsWorldReady)
		{
			_rejected.Add((intent, ApproachOutcome.Rejected));
			Deliver();
			return EnqueueResult.RejectedInvalid;
		}
		EnqueueResult r = _queue.Enqueue(new ApproachRequest(intent.RequestId, intent.NpcName, intent.DedupeKey, intent.Relevance, intent.DistanceTiles, intent.Priority, _tick, intent.TtlTicks), _tick);
		if (r is EnqueueResult.Accepted or EnqueueResult.Replaced)
		{
			_intents[intent.RequestId] = intent;
			ReleaseOrphan();
			StartNextPreparation();
		}
		else
		{
			_rejected.Add((intent, ApproachOutcome.Rejected));
		}
		Deliver();
		return r;
	}

	public void OnUpdateTicked()
	{
		if (Context.IsWorldReady && !Context.IsMainPlayer)
		{
			return;
		}
		_tick++;
		DrainResults();
		TimeoutPreparation();
		AbortDisabledSources();
		if (_active == null && _preparingId == null && !_queue.HasWork)
		{
			Deliver();
			return;
		}
		if (!Context.IsWorldReady)
		{
			Deliver();
			return;
		}
		_queue.Expire(_tick);
		ReleaseOrphan();
		StartNextPreparation();
		if (_active == null)
		{
			TryStart();
		}
		if (_active != null)
		{
			TickActive();
		}
		Deliver();
	}

	/// <summary>玩家和这位村民的对话结束了：走过来的那位可以回去了。</summary>
	public void OnConversationEnded(string npc)
	{
		if (!string.IsNullOrWhiteSpace(npc))
		{
			_conversationEnded[npc] = _tick;
		}
		if (_active != null && Same(_active.Intent.NpcName, npc) && (_queue.Get(_active.Intent.RequestId)?.SpeechPresented ?? false))
		{
			BeginReturn();
			Deliver();
		}
	}

	/// <summary>全部取消，正在走的立刻放回原位。</summary>
	public void AbortAll(string reason)
	{
		CancelPreparation();
		if (_active != null)
		{
			RestoreNow(_active, reason);
		}
		_queue.Clear(reason);
		_fallback.Clear();
		_timedOut.Clear();
		_conversationEnded.Clear();
		while (_results.TryDequeue(out _))
		{
		}
		Deliver();
	}

	/// <summary>撤回一个请求：排着的取消，正在走的转身回去。</summary>
	public void Withdraw(string requestId)
	{
		if (!_intents.ContainsKey(requestId))
		{
			return;
		}
		if (_active != null && Same(_active.Intent.RequestId, requestId))
		{
			if (_preparingId == requestId)
			{
				CancelPreparation();
			}
			AbortActive("撤回");
		}
		else
		{
			if (_preparingId == requestId)
			{
				CancelPreparation();
			}
			_queue.Abort(requestId, "撤回", _tick);
		}
		Deliver();
	}

	/// <summary>走过来时头顶的"!"。</summary>
	public void Draw(SpriteBatch b)
	{
		NPC? npc = AttentionNpc();
		if (npc == null || npc.IsInvisible || npc.currentLocation != Game1.currentLocation)
		{
			return;
		}
		Vector2 local = npc.getLocalPosition(Game1.viewport);
		float scale = Math.Max(0.2f, npc.Scale);
		float x = local.X + npc.GetSpriteWidthForPositioning() * 2f;
		float y = local.Y + npc.GetBoundingBox().Height / 2f - npc.Sprite.SpriteHeight * 3f * scale;
		Rectangle src = new Rectangle(16, 64, 16, 16);
		b.Draw(Game1.emoteSpriteSheet, new Vector2(x - src.Width * 2f, y - src.Height * 4f - 8f), src, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
	}

	public NPC? AttentionNpc()
	{
		if (_active != null && !(_queue.Get(_active.Intent.RequestId)?.SpeechPresented ?? true))
		{
			return _active.Intent.ShowAttentionMarker ? _active.Npc : null;
		}
		string? id = _preparingId ?? _queue.Queued.FirstOrDefault()?.Request.RequestId;
		if (id != null && _intents.TryGetValue(id, out ApproachIntent? intent) && intent.ShowAttentionMarker)
		{
			return FindNpc(intent.NpcName);
		}
		return ExtraAttention?.Invoke() is string name ? FindNpc(name) : null;
	}

	// ---- 准备台词 ----

	private void StartNextPreparation()
	{
		if (_active != null || _isDayTransition())
		{
			return;
		}
		IReadOnlyList<ApproachEntry> queued = _queue.Queued;
		ApproachEntry? next = queued.FirstOrDefault(e => e.State == ApproachState.PendingDialogue && e.Request.RequestId != _preparingId && !SafeGenerating(e.Request.NpcName));
		if (_preparingId != null)
		{
			if (next == null || !Outranks(next, _preparingId))
			{
				return;
			}
			CancelPreparation();
		}
		ApproachEntry? ready = queued.FirstOrDefault(e => e.State == ApproachState.ReadyToApproach && CanStartNow(e.Request.RequestId));
		if (next == null || (ready != null && !Outranks(next, ready.Request.RequestId)))
		{
			return;
		}
		string id = next.Request.RequestId;
		if (!_intents.TryGetValue(id, out ApproachIntent? intent))
		{
			_queue.Abort(id, "请求丢失", _tick);
			return;
		}
		NPC? npc = FindNpc(intent.NpcName);
		if (npc?.currentLocation == null || (!string.IsNullOrWhiteSpace(intent.LocationName) && !Same(npc.currentLocation.NameOrUniqueName, intent.LocationName)))
		{
			_queue.Abort(id, "村民不在那张图上了", _tick);
			return;
		}
		if (intent.StartWhilePreparing && LeaseRefusal(npc) is string why)
		{
			_log.Debug($"{intent.NpcName} 现在不能走过来：{why}");
			_queue.Abort(id, why, _tick);
			return;
		}
		if (intent.PrepareDialogue == null)
		{
			string? line = Choose(null, intent.FallbackDialogue);
			if (line == null || !_queue.MarkDialogueReady(id, line, _tick))
			{
				_queue.Abort(id, "没有台词", _tick);
			}
			else
			{
				_fallback.Add(id);
			}
			return;
		}
		_preparingId = id;
		_prepStarted = _tick;
		_prepCts = new CancellationTokenSource();
		CancellationToken token = _prepCts.Token;
		long version = ++_prepVersion;
		Func<CancellationToken, Task<string?>> prepare = intent.PrepareDialogue;
		Task.Run(async () =>
		{
			string? dialogue = null;
			try
			{
				using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token);
				limit.CancelAfter(MaxPreparation);
				dialogue = await prepare(limit.Token).WaitAsync(MaxPreparation, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (TimeoutException)
			{
			}
			catch (Exception ex)
			{
				_log.Warn($"准备 {intent.NpcName} 走过来要说的话时出错：{ex.Message}");
			}
			_results.Enqueue(new Prepared(version, id, dialogue));
		}, token);
	}

	private void DrainResults()
	{
		while (_results.TryDequeue(out Prepared? r))
		{
			if (r.Version != _prepVersion || _preparingId != r.RequestId)
			{
				_timedOut.Remove(r.RequestId);
				continue;
			}
			_prepCts?.Dispose();
			_prepCts = null;
			_preparingId = null;
			if (!_intents.TryGetValue(r.RequestId, out ApproachIntent? intent))
			{
				continue;
			}
			string? line = Choose(r.Dialogue, intent.FallbackDialogue);
			if (line == null)
			{
				Fail(r.RequestId, "生成失败");
				continue;
			}
			MarkPrepared(r.RequestId, line, string.IsNullOrWhiteSpace(r.Dialogue));
		}
	}

	private void TimeoutPreparation()
	{
		string? id = _preparingId;
		if (id == null || !_intents.TryGetValue(id, out ApproachIntent? intent) || intent.PreparationTimeoutTicks <= 0 || _tick - _prepStarted < intent.PreparationTimeoutTicks)
		{
			return;
		}
		_timedOut.Add(id);
		CancelPreparation();
		string? line = Choose(null, intent.FallbackDialogue);
		if (line == null)
		{
			Fail(id, "准备超时");
			return;
		}
		if (MarkPrepared(id, line, true) && _active != null && Same(_active.Intent.RequestId, id))
		{
			_active.PreparationTimedOut = true;
		}
	}

	private bool MarkPrepared(string id, string line, bool fallback)
	{
		if (!_queue.MarkDialogueReady(id, line, _tick))
		{
			return false;
		}
		if (fallback)
		{
			_fallback.Add(id);
		}
		else
		{
			_fallback.Remove(id);
		}
		if (_active != null && Same(_active.Intent.RequestId, id))
		{
			_active.Dialogue = line.Trim();
			_active.UsingFallback = fallback;
		}
		return true;
	}

	private void Fail(string id, string why)
	{
		if (_active != null && Same(_active.Intent.RequestId, id))
		{
			AbortActive(why);
		}
		else
		{
			_queue.Abort(id, why, _tick);
		}
	}

	private void CancelPreparation()
	{
		_prepCts?.Cancel();
		_prepCts?.Dispose();
		_prepCts = null;
		_preparingId = null;
		_prepStarted = 0;
	}

	private void ReleaseOrphan()
	{
		if (_preparingId != null && (_queue.Get(_preparingId) is not ApproachEntry e || e.IsTerminal || e.AbortRequested))
		{
			CancelPreparation();
		}
	}

	private bool Outranks(ApproachEntry pending, string otherId)
	{
		if (!_intents.TryGetValue(pending.Request.RequestId, out ApproachIntent? a) || !a.StartWhilePreparing || !_intents.TryGetValue(otherId, out ApproachIntent? b) || b.StartWhilePreparing || Same(a.NpcName, b.NpcName))
		{
			return false;
		}
		return _queue.Get(otherId) is ApproachEntry other && pending.Request.Score > other.Request.Score;
	}

	// ---- 出发、走路、说话、回去 ----

	private void TryStart()
	{
		if (Game1.eventUp || Game1.currentLocation?.currentEvent != null || Game1.activeClickableMenu != null || Game1.dialogueUp)
		{
			return;
		}
		string? id = SelectStart();
		if (id == null || (_isDialogueBusy() && !_timedOut.Contains(id)))
		{
			return;
		}
		ApproachEntry? entry = _queue.TryBegin(_tick, id);
		if (entry == null || !_intents.TryGetValue(id, out ApproachIntent? intent))
		{
			return;
		}
		NPC? npc = FindNpc(intent.NpcName);
		if (npc?.currentLocation == null || npc.currentLocation != Game1.currentLocation || LeaseRefusal(npc) != null)
		{
			_log.Debug($"{intent.NpcName} 没能出发：{(npc?.currentLocation != Game1.currentLocation ? "不在玩家所在的地图" : LeaseRefusal(npc!))}");
			if (_preparingId == id)
			{
				CancelPreparation();
			}
			_queue.Abort(id, "没能出发", _tick);
			FinishWithoutWalking(id);
			return;
		}
		Lease lease = new Lease(npc, _tick);
		npc.ignoreScheduleToday = true;
		npc.Halt();
		npc.controller = null;
		_active = new Active(intent, lease, _tick) { Dialogue = entry.Dialogue, PreparationTimedOut = _timedOut.Contains(id), UsingFallback = _fallback.Contains(id) };
		Announce(npc, intent);
		_log.Debug($"{intent.NpcName} 朝玩家走过来（{intent.Source}）。");
	}

	private string? SelectStart()
	{
		if (_preparingId != null && _intents.TryGetValue(_preparingId, out ApproachIntent? preparing) && preparing.StartWhilePreparing && CanStartNow(_preparingId))
		{
			return _preparingId;
		}
		foreach (ApproachEntry e in _queue.Queued.Where(e => e.State == ApproachState.ReadyToApproach))
		{
			string id = e.Request.RequestId;
			if (!_intents.TryGetValue(id, out ApproachIntent? intent))
			{
				return id;
			}
			if (!SafeCanPresent(intent, _fallback.Contains(id)))
			{
				_queue.Abort(id, "不需要说了", _tick);
				_fallback.Remove(id);
			}
			else if (SafeCanStart(intent))
			{
				return id;
			}
		}
		return null;
	}

	private void Announce(NPC npc, ApproachIntent intent)
	{
		if (intent.AnnounceEmote is int emote && emote > 0)
		{
			npc.doEmote(emote);
		}
		if (!string.IsNullOrWhiteSpace(intent.AnnounceText))
		{
			NPC speaker = string.IsNullOrWhiteSpace(intent.AnnounceTextNpcName) ? npc
				: npc.currentLocation?.characters.FirstOrDefault(c => !c.IsInvisible && Same(c.Name, intent.AnnounceTextNpcName)) ?? npc;
			NpcSpeech.Say(speaker, intent.AnnounceText.Trim());
		}
	}

	private void TickActive()
	{
		Active a = _active!;
		if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
		{
			RestoreNow(a, "开始了过场");
			return;
		}
		ApproachEntry? entry = _queue.Get(a.Intent.RequestId);
		if (entry == null || entry.IsTerminal)
		{
			RestoreNow(a, "请求丢了");
			return;
		}
		if (_tick - a.Lease.StartedTick > MaxLeaseTicks)
		{
			RestoreNow(a, "借用太久");
			return;
		}
		switch (entry.State)
		{
		case ApproachState.Returning:
			TickReturn(a);
			break;
		case ApproachState.Speaking:
			TickSpeaking(a, entry);
			break;
		default:
			TickApproach(a, entry);
			break;
		}
	}

	private void TickSpeaking(Active a, ApproachEntry entry)
	{
		if (!entry.SpeechPresented)
		{
			if (_tick - a.StateTick >= SpeechOpenTimeoutTicks)
			{
				AbortActive("对话框一直打不开");
			}
			else if (!SafeCanPresent(a.Intent, a.UsingFallback))
			{
				AbortActive("不需要说了");
			}
			else if (!string.IsNullOrWhiteSpace(a.Dialogue) && (a.Intent.Present?.Invoke() ?? _presentDefault(a.Npc, a.Dialogue!)))
			{
				_queue.MarkPresented(a.Intent.RequestId);
			}
			return;
		}
		if (Game1.activeClickableMenu == null && !Game1.dialogueUp && (!_isDialogueBusy() || a.PreparationTimedOut))
		{
			if (++a.IdleTicks >= 60)
			{
				BeginReturn();
			}
		}
		else
		{
			a.IdleTicks = 0;
		}
	}

	private void TickApproach(Active a, ApproachEntry entry)
	{
		Farmer player = Game1.player;
		GameLocation? location = a.Npc.currentLocation;
		if (player == null || location == null || player.currentLocation != location)
		{
			AbortActive("玩家换了地图");
			return;
		}
		if (a.WaitingForDialogue && string.IsNullOrWhiteSpace(entry.Dialogue))
		{
			StopWalking(a);
			a.Npc.Halt();
			a.Npc.faceTowardFarmerForPeriod(300, 3, false, player);
			return;
		}
		a.WaitingForDialogue = false;
		(int X, int Y) npcTile = ((int)a.Npc.Tile.X, (int)a.Npc.Tile.Y);
		(int X, int Y) playerTile = ((int)player.Tile.X, (int)player.Tile.Y);
		(int X, int Y)? target = ApproachNavigation.AdjacentTile(playerTile, npcTile, t => SafeTile(location, t, a.Npc));
		if (target == null)
		{
			AbortActive("玩家身边没有空位");
			return;
		}
		if (a.ArrivedAt == target)
		{
			Arrive(a, player, entry);
			return;
		}
		a.ArrivedAt = null;
		Walk? walk = a.Npc.controller as Walk;
		(int X, int Y)? walkTarget = walk == null ? null : (walk.endPoint.X, walk.endPoint.Y);
		if (npcTile != target.Value && a.Intent.UrgentApproach && !a.SpeedApplied)
		{
			a.Npc.speed = Math.Max(Math.Max(1, a.Lease.Speed), UrgentSpeed);
			a.SpeedApplied = true;
		}
		switch (a.Navigation.Evaluate(_tick, npcTile, target.Value, walk != null, walkTarget))
		{
		case ApproachNavigation.Decision.Arrived:
			Arrive(a, player, entry);
			break;
		case ApproachNavigation.Decision.StartPath:
		case ApproachNavigation.Decision.Repath:
			StopWalking(a);
			int started = StartWalk(a.Npc, location, target.Value);
			if (started < 0)
			{
				if (Math.Abs(npcTile.X - playerTile.X) + Math.Abs(npcTile.Y - playerTile.Y) <= NoPathTalkTiles)
				{
					a.ArrivedAt = target;
					Arrive(a, player, entry);
				}
				else
				{
					AbortActive("找不到路");
				}
			}
			else if (started == 0)
			{
				a.ArrivedAt = target;
				Arrive(a, player, entry);
			}
			break;
		case ApproachNavigation.Decision.Failed:
			if (Math.Abs(npcTile.X - playerTile.X) + Math.Abs(npcTile.Y - playerTile.Y) <= 2)
			{
				StopWalking(a);
				a.ArrivedAt = target;
				Arrive(a, player, entry);
			}
			else
			{
				AbortActive("走不过来：" + a.Navigation.FailReason);
			}
			break;
		}
	}

	private void Arrive(Active a, Farmer player, ApproachEntry entry)
	{
		StopWalking(a);
		RestoreSpeed(a);
		a.Npc.Halt();
		a.Npc.faceTowardFarmerForPeriod(600, 3, false, player);
		if (!string.IsNullOrWhiteSpace(entry.Dialogue) && Game1.activeClickableMenu == null && !Game1.dialogueUp && (!_isDialogueBusy() || a.PreparationTimedOut) && _queue.MarkArrived(a.Intent.RequestId))
		{
			a.Dialogue = entry.Dialogue;
			a.StateTick = _tick;
			a.WaitingForDialogue = false;
		}
		else
		{
			a.WaitingForDialogue = true;
		}
	}

	private void BeginReturn()
	{
		Active? a = _active;
		if (a == null)
		{
			return;
		}
		StopWalking(a);
		RestoreSpeed(a);
		_queue.MarkSpeechDone(a.Intent.RequestId);
		StartReturn(a);
	}

	private void AbortActive(string why)
	{
		Active? a = _active;
		if (a == null)
		{
			return;
		}
		if (_preparingId == a.Intent.RequestId)
		{
			CancelPreparation();
		}
		_log.Debug($"{a.Intent.NpcName} 不走过来了：{why}");
		_queue.Abort(a.Intent.RequestId, why, _tick);
		StopWalking(a);
		RestoreSpeed(a);
		StartReturn(a);
	}

	private void StartReturn(Active a)
	{
		a.ReturnStartedTick = _tick;
		a.ReturnLastPosition = a.Npc.Position;
		a.ReturnLastProgressTick = _tick;
		GameLocation? location = a.Npc.currentLocation;
		if (location == null || !Same(location.NameOrUniqueName, a.Lease.LocationName))
		{
			Finish(a);
			return;
		}
		(int X, int Y) home = ((int)MathF.Round(a.Lease.Position.X / 64f), (int)MathF.Round(a.Lease.Position.Y / 64f));
		if (StartWalk(a.Npc, location, home, a.Lease.Facing) <= 0)
		{
			Finish(a);
		}
	}

	private void TickReturn(Active a)
	{
		if (a.Npc.controller is not Walk || Vector2.Distance(a.Npc.Position, a.Lease.Position) <= 12f)
		{
			Finish(a);
			return;
		}
		if (Vector2.DistanceSquared(a.Npc.Position, a.ReturnLastPosition) >= 16f)
		{
			a.ReturnLastPosition = a.Npc.Position;
			a.ReturnLastProgressTick = _tick;
		}
		else if (_tick - a.ReturnLastProgressTick >= ReturnStallTicks || _tick - a.ReturnStartedTick >= ReturnTimeoutTicks)
		{
			Finish(a);
		}
	}

	/// <summary>放回原位：回到记下的位置和朝向，恢复速度和原来的路线（或重新对一遍日程）。</summary>
	private void Finish(Active a)
	{
		Restore(a.Lease);
		_queue.MarkReturned(a.Intent.RequestId);
		_queue.MarkRestored(a.Intent.RequestId);
		_fallback.Remove(a.Intent.RequestId);
		_timedOut.Remove(a.Intent.RequestId);
		_active = null;
	}

	private void RestoreNow(Active a, string why)
	{
		if (_preparingId == a.Intent.RequestId)
		{
			CancelPreparation();
		}
		_log.Debug($"{a.Intent.NpcName} 立刻放回原位：{why}");
		Restore(a.Lease);
		_queue.ForceFinishActive(why);
		_fallback.Remove(a.Intent.RequestId);
		_timedOut.Remove(a.Intent.RequestId);
		_active = null;
	}

	private static void Restore(Lease lease)
	{
		NPC npc = lease.Npc;
		try
		{
			if (npc.controller is Walk)
			{
				npc.controller = null;
			}
			npc.Halt();
			npc.speed = lease.Speed;
			bool exact = false;
			if (npc.currentLocation != null && Same(npc.currentLocation.NameOrUniqueName, lease.LocationName))
			{
				npc.Position = lease.Position;
				npc.faceDirection(lease.Facing);
				exact = true;
			}
			npc.ignoreScheduleToday = lease.IgnoreSchedule;
			if (exact && lease.OriginalController != null)
			{
				npc.controller = lease.OriginalController;
			}
			else if (exact)
			{
				npc.checkSchedule(Game1.timeOfDay);
			}
		}
		catch (Exception)
		{
			npc.ignoreScheduleToday = lease.IgnoreSchedule;
		}
	}

	private void FinishWithoutWalking(string id)
	{
		_queue.MarkReturned(id);
		_queue.MarkRestored(id);
		_fallback.Remove(id);
	}

	// ---- 工具 ----

	/// <returns>1 = 开始走；0 = 已经在那儿；-1 = 找不到路。</returns>
	private static int StartWalk(NPC npc, GameLocation location, (int X, int Y) target, int facing = -1)
	{
		if ((int)(npc.Position.X / 64f) == target.X && (int)(npc.Position.Y / 64f) == target.Y)
		{
			return 0;
		}
		try
		{
			Walk walk = new Walk(npc, location, new Point(target.X, target.Y), facing);
			if (walk.pathToEndPoint == null)
			{
				return -1;
			}
			if (walk.pathToEndPoint.Count == 0)
			{
				return 0;
			}
			npc.controller = walk;
			return 1;
		}
		catch (Exception)
		{
			return -1;
		}
	}

	private static void StopWalking(Active a)
	{
		if (a.Npc.controller is Walk)
		{
			a.Npc.controller = null;
		}
	}

	private static void RestoreSpeed(Active a)
	{
		if (a.SpeedApplied)
		{
			a.Npc.speed = a.Lease.Speed;
			a.SpeedApplied = false;
		}
	}

	/// <summary>能不能借这位村民：别的 mod 的控制器在带着他走、或者已经被我们借走时不行。</summary>
	private string? LeaseRefusal(NPC npc)
	{
		if (_active != null && _active.Npc == npc)
		{
			return "已经在走了";
		}
		if (SquadMark.IsControlled(npc))
		{
			return "跟着农夫";
		}
		PathFindController? c = npc.controller;
		if (c != null && c is not Walk && c.GetType().Assembly != typeof(PathFindController).Assembly)
		{
			return "被别的 mod 带着走";
		}
		return null;
	}

	private static bool SafeTile(GameLocation location, (int X, int Y) t, NPC moving)
	{
		Vector2 v = new Vector2(t.X, t.Y);
		try
		{
			return t.X >= 0 && t.Y >= 0 && location.isTileOnMap(v) && location.isTilePassable(v) && !location.characters.Any(c => c != moving && c.Tile == v);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private void AbortDisabledSources()
	{
		foreach (ApproachIntent intent in _intents.Values.Where(i => !_isSourceEnabled(i.Source)).ToList())
		{
			if (_active != null && Same(_active.Intent.RequestId, intent.RequestId))
			{
				RestoreNow(_active, "功能关掉了");
			}
			else
			{
				if (_preparingId == intent.RequestId)
				{
					CancelPreparation();
				}
				_queue.Abort(intent.RequestId, "功能关掉了", _tick);
			}
		}
	}

	private void Deliver()
	{
		List<(ApproachIntent Intent, ApproachOutcome Outcome)> list = _rejected.ToList();
		_rejected.Clear();
		foreach ((ApproachEntry e, ApproachOutcome outcome) in _queue.TakeFinished())
		{
			if (_intents.Remove(e.Request.RequestId, out ApproachIntent? intent))
			{
				list.Add((intent, outcome));
			}
		}
		foreach ((ApproachIntent intent, ApproachOutcome outcome) in list)
		{
			try
			{
				intent.OnFinished?.Invoke(outcome);
			}
			catch (Exception ex)
			{
				_log.Warn($"{intent.Source} 处理走过来的结果时出错：{ex.Message}");
			}
		}
	}

	private bool CanStartNow(string id) => !_intents.TryGetValue(id, out ApproachIntent? i) || SafeCanStart(i);

	private static bool SafeCanStart(ApproachIntent i)
	{
		try
		{
			return i.CanStartNow?.Invoke() ?? true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool SafeCanPresent(ApproachIntent i, bool fallback)
	{
		try
		{
			return i.CanPresent?.Invoke(fallback) ?? true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private bool SafeGenerating(string npc)
	{
		try
		{
			return _isNpcGenerating(npc);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string? Choose(string? prepared, string? fallback) =>
		!string.IsNullOrWhiteSpace(prepared) ? prepared.Trim() : !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : null;

	public static NPC? FindNpc(string name)
	{
		if (!Context.IsWorldReady || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		// 矿井这类临时地点不在 locations 里，先看农夫所在的地方。
		if (Game1.currentLocation?.characters.FirstOrDefault(c => Same(c.Name, name)) is NPC here)
		{
			return here;
		}
		foreach (GameLocation l in Game1.locations)
		{
			foreach (NPC c in l.characters)
			{
				if (Same(c.Name, name))
				{
					return c;
				}
			}
		}
		return null;
	}

	public static bool IsVillager(NPC? npc) => npc != null && npc.IsVillager && !npc.IsInvisible && npc is not (Monster or Pet or Horse or Child) && !string.IsNullOrWhiteSpace(npc.Name);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
