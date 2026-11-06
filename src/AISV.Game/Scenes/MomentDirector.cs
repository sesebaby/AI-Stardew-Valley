using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Approach;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Scenes;
using AISV.Core.Text;
using AISV.Game.Approach;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Scenes;

/// <summary>
/// 偶遇：每 15 tick 看一次当前地图上今天挑中的场景，条件合适时让领头的走过来找农夫，或者让几个人在旁边自己聊、农夫能听到。
/// 只在主机上跑。
/// </summary>
public sealed class MomentDirector
{
	public const string Source = "scene-events";

	private sealed record Assessment(MomentBlock Block, MomentPlan? Plan, bool Ai);

	private sealed record Generated(long Version, IReadOnlyList<SceneLine>? Lines);

	private sealed class Tries
	{
		public int Count { get; set; }

		public int LastTick { get; set; } = -1;

		public string Last { get; set; } = "";
	}

	private sealed class Active
	{
		public Active(SceneDefinition scene, SceneDelivery delivery, MomentPlanKind kind, GameLocation location, string lead, IReadOnlyList<string> speakers, IReadOnlyList<SceneLine> fallback, int tick)
		{
			Scene = scene;
			Delivery = delivery;
			Kind = kind;
			Location = location;
			Lead = lead;
			Speakers = speakers;
			Fallback = fallback;
			StartedTick = tick;
		}

		public SceneDefinition Scene { get; }

		public string Id => Scene.Id;

		public SceneDelivery Delivery { get; }

		public MomentPlanKind Kind { get; }

		public GameLocation Location { get; }

		public string Lead { get; }

		public IReadOnlyList<string> Speakers { get; }

		public IReadOnlyList<SceneLine> Fallback { get; }

		public int StartedTick { get; }

		public string RequestId { get; set; } = "";

		public WalkUpLine? Line { get; set; }

		public long Version { get; set; }

		public int Deadline { get; set; }

		public IReadOnlyList<SceneLine>? Ready { get; set; }

		public int ReadySince { get; set; }

		public bool Playing { get; set; }
	}

	/// <summary>走过来要说的那句（后台写好后记下），和第一次在主线程看到它的 tick。</summary>
	private sealed class WalkUpLine
	{
		private string? _text;

		public string? Text => Volatile.Read(ref _text);

		public int SeenTick { get; private set; } = -1;

		public void Remember(string? text)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				Volatile.Write(ref _text, text);
			}
		}

		public void Observe(int tick)
		{
			if (SeenTick < 0 && Text != null)
			{
				SeenTick = tick;
			}
		}
	}

	private readonly SceneService _scenes;

	private readonly ApproachCoordinator _approaches;

	private readonly Func<ModConfig> _config;

	private readonly ILog _log;

	private readonly OverheardPlayer _player;

	private readonly Dictionary<string, Tries> _tries = new Dictionary<string, Tries>(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentQueue<Generated> _generated = new ConcurrentQueue<Generated>();

	private Active? _active;

	private CancellationTokenSource? _generation;

	private long _version;

	private GameLocation? _location;

	private int _enteredTick;

	private string? _style;

	private int _tick;

	private int _nextCheck;

	private int _sequence;

	private int _lastFailureTick = -100000;

	private int _suppressed;

	public MomentDirector(SceneService scenes, ApproachCoordinator approaches, Func<ModConfig> config, ILog log)
	{
		_scenes = scenes;
		_approaches = approaches;
		_config = config;
		_log = log;
		_player = new OverheardPlayer(log);
	}

	public Func<string, bool> IsNpcEnabled { get; set; } = _ => true;

	/// <summary>被陪钓之类别的事占着。</summary>
	public Func<string, bool> IsNpcOccupied { get; set; } = _ => false;

	/// <summary>AI 对话现在能不能用（没被停）。</summary>
	public Func<bool> AiDialogueAvailable { get; set; } = () => false;

	public Func<string, bool> TalkedToday { get; set; } = _ => false;

	public Func<string, bool> IsGenerating { get; set; } = _ => false;

	public Func<string, string?> TodayLine { get; set; } = _ => null;

	public Func<string, string, bool> DiscardOpening { get; set; } = (_, _) => false;

	public Func<string, bool> IsTalkingTo { get; set; } = _ => false;

	public Func<bool> InDayTransition { get; set; } = () => false;

	public Func<bool> Generating { get; set; } = () => false;

	/// <summary>在主线程上准备"走过来时生成开场白"的活：(村民, 地点, 说明) → 后台跑的生成。</summary>
	public Func<NPC, string, string, Func<CancellationToken, Task<string?>>>? PrepareWalkUp { get; set; }

	/// <summary>在主线程上准备"写旁听台词"的活：(说话的人, 心里想的事, 地点, 时间) → 后台跑的生成。</summary>
	public Func<IReadOnlyList<string>, string, string, int, Func<CancellationToken, Task<IReadOnlyList<SceneLine>?>>>? WriteOverheard { get; set; }

	private static bool FarmhandScreen => Context.IsSplitScreen && !Context.IsMainPlayer;

	public void OnUpdateTicked()
	{
		if (!Context.IsMainPlayer)
		{
			return;
		}
		_tick++;
		try
		{
			if (!Context.IsWorldReady)
			{
				return;
			}
			ModConfig c = _config();
			if (!c.EncountersOn)
			{
				if (_active != null)
				{
					Cancel("偶遇关掉了");
				}
				return;
			}
			WatchStyle(c);
			TrackLocation();
			Drain();
			if (_active is { Kind: MomentPlanKind.WalkUp } walking)
			{
				walking.Line?.Observe(_approaches.Tick);
			}
			if (_active != null && CalledAway(_active))
			{
				Fail("有人被叫走了");
			}
			if (_player.Playing)
			{
				_player.Tick(_tick);
			}
			if (_tick >= _nextCheck)
			{
				_nextCheck = _tick + MomentRules.EvaluateEveryTicks;
				if (_active != null)
				{
					Advance();
				}
				else
				{
					Evaluate(c);
				}
			}
		}
		catch (Exception ex)
		{
			Report("每帧检查", ex);
		}
	}

	public void OnDayStarted() => ResetUnlessFarmhand("新的一天");

	public void OnDayEnding() => ResetUnlessFarmhand("一天结束");

	public void OnReturnedToTitle() => ResetUnlessFarmhand("回到标题");

	public void OnSaveLoaded()
	{
		if (FarmhandScreen)
		{
			return;
		}
		Reset("读了存档");
		_style = _scenes.Style;
		_scenes.OnStyleChanged();
	}

	/// <summary>设置里改了风格。</summary>
	public void OnStyleChanged()
	{
		if (!FarmhandScreen && _scenes.Style != _style)
		{
			ApplyStyle(_scenes.Style);
		}
	}

	public void ResetTries(string id) => _tries.Remove(id.Trim());

	public IReadOnlyList<string> Describe()
	{
		List<string> lines = new List<string>();
		if (!Context.IsMainPlayer)
		{
			lines.Add("偶遇：走过来和旁听只在主机上演。");
			return lines;
		}
		lines.Add(_active == null ? "偶遇：没有正在进行的。" : $"偶遇：{_active.Id} 正在进行（{Phase(_active)}）。");
		GameLocation? here = Game1.currentLocation;
		Farmer? player = Game1.player;
		if (here != null && player != null)
		{
			string style = _scenes.Style;
			bool busy = WorldBusy();
			bool playerBusy = PlayerBusy(player);
			foreach ((SceneDefinition scene, SceneDelivery delivery) in _scenes.ArmedMoments(here.Name))
			{
				Assessment a = Assess(scene, delivery, here, player, style, busy, playerBusy);
				string state = a.Block != MomentBlock.None ? $"现在不行（{MomentRules.BlockName(a.Block)}）" : $"{a.Plan!.Kind}：{a.Plan.Reason}";
				lines.Add($"- 这里：{scene.Id}（{SceneCatalog.DeliveryName(delivery)}）→ {state}。");
			}
		}
		foreach ((string id, Tries t) in _tries)
		{
			string when = t.LastTick >= 0 ? $"{(_tick - t.LastTick) / 60} 秒前" : "没试过";
			lines.Add($"- {id} 试了 {t.Count}/{MomentRules.MaxTriesPerDay} 次，上次 {when}：{t.Last}。");
		}
		return lines;
	}

	// ---------------- 看一看能不能开始 ----------------

	private void Evaluate(ModConfig c)
	{
		GameLocation? here = Game1.currentLocation;
		Farmer? player = Game1.player;
		if (here == null || player == null)
		{
			return;
		}
		IReadOnlyList<(SceneDefinition Scene, SceneDelivery Delivery)> armed = _scenes.ArmedMoments(here.Name);
		if (armed.Count == 0)
		{
			return;
		}
		string style = _scenes.Style;
		bool busy = WorldBusy();
		bool playerBusy = PlayerBusy(player);
		foreach ((SceneDefinition scene, SceneDelivery delivery) in armed)
		{
			Assessment a = Assess(scene, delivery, here, player, style, busy, playerBusy);
			if (a.Block != MomentBlock.None || a.Plan == null)
			{
				continue;
			}
			switch (a.Plan.Kind)
			{
				case MomentPlanKind.Skip:
					Failed(scene.Id, a.Plan.Reason);
					break;
				case MomentPlanKind.WalkUp:
					StartWalkUp(scene, delivery, a.Plan, here, player, a.Ai);
					return;
				case MomentPlanKind.Overheard:
					StartOverheard(scene, delivery, a.Plan, here, player, a.Ai);
					return;
			}
		}
	}

	private Assessment Assess(SceneDefinition scene, SceneDelivery delivery, GameLocation location, Farmer player, string style, bool worldBusy, bool playerBusy)
	{
		_tries.TryGetValue(scene.Id, out Tries? t);
		MomentBlock block = MomentRules.Check(new MomentReadiness(_tick, _enteredTick, Game1.timeOfDay, scene.WindowMin, scene.WindowMax, worldBusy, playerBusy, t?.Count ?? 0, t?.LastTick ?? -1, _active != null || _approaches.IsBusy));
		if (block != MomentBlock.None)
		{
			return new Assessment(block, null, false);
		}
		List<MomentPresence> presence = Presence(scene, location, player);
		bool ai = AiDialogueAvailable() && presence.Where(p => p.Nearby).All(p => IsNpcEnabled(p.Npc));
		return new Assessment(block, MomentRules.Decide(delivery, style, scene.Speakers, presence, ai, scene.Lines), ai);
	}

	private List<MomentPresence> Presence(SceneDefinition scene, GameLocation location, Farmer player)
	{
		List<MomentPresence> list = new List<MomentPresence>();
		foreach (string name in scene.Speakers)
		{
			NPC? npc = MomentWorld.Villager(location, name);
			bool near = Near(npc, player, scene.MaxDistanceTiles);
			bool canApproach = near && !TalkedToday(npc!.Name) && !IsGenerating(npc.Name) && IsNpcEnabled(npc.Name);
			list.Add(new MomentPresence(npc?.Name ?? name, near, canApproach));
		}
		return list;
	}

	// ---------------- 走过来 ----------------

	private void StartWalkUp(SceneDefinition scene, SceneDelivery delivery, MomentPlan plan, GameLocation location, Farmer player, bool ai)
	{
		NPC? npc = MomentWorld.Villager(location, plan.Lead);
		if (npc == null)
		{
			return;
		}
		string name = npc.Name;
		string locationName = location.NameOrUniqueName;
		List<string> others = scene.Speakers.Where(n => !string.Equals(n, plan.Lead, StringComparison.OrdinalIgnoreCase)).ToList();
		string focus = MomentRules.WalkUpFocus(WithFarmer(SceneCatalog.Premise(scene.Lines), player), others);
		WalkUpLine? line = null;
		Func<CancellationToken, Task<string?>>? prepare = null;
		if (ai && PrepareWalkUp != null)
		{
			WalkUpLine l = line = new WalkUpLine();
			Func<CancellationToken, Task<string?>> write = PrepareWalkUp(npc, locationName, focus);
			prepare = async ct =>
			{
				string? text = await write(ct).ConfigureAwait(false);
				l.Remember(text);
				return text;
			};
		}
		string? fallback = MomentRules.LeadDialogue(scene.Lines, plan.Lead);
		int maxTiles = scene.MaxDistanceTiles;
		string requestId = $"scene:{scene.Id}:{Game1.Date?.TotalDays ?? 0}:{++_sequence}";
		string id = scene.Id;
		int queued = _approaches.Tick;
		_active = new Active(scene, delivery, MomentPlanKind.WalkUp, location, name, plan.Speakers, Array.Empty<SceneLine>(), _tick) { RequestId = requestId, Line = line };
		EnqueueResult result;
		try
		{
			result = _approaches.Enqueue(new ApproachIntent(Source, requestId, name, "scene:" + scene.Id, locationName, Game1.timeOfDay)
			{
				DistanceTiles = Vector2.Distance(npc.Tile, player.Tile),
				Priority = 1,
				TtlTicks = MomentRules.WalkUpTtlTicks,
				PreparationTimeoutTicks = MomentRules.WalkUpPrepareTicks,
				AnnounceEmote = MomentRules.AnnounceEmote,
				AnnounceText = Tr.Get("scenes.callout", "{{farmer}}，过来一下！", ("farmer", player.Name)),
				PrepareDialogue = prepare,
				FallbackDialogue = fallback,
				CanStartNow = () => !_approaches.HadConversationSince(name, queued) && CanStartWalkUp(location, name, maxTiles),
				CanPresent = usingFallback => !_approaches.HadConversationSince(name, queued) && (!usingFallback || !TalkedToday(name)),
				OnFinished = outcome => WalkUpFinished(requestId, id, name, line, outcome)
			});
		}
		catch
		{
			Fail("走过来没能排上");
			throw;
		}
		if (result is not (EnqueueResult.Accepted or EnqueueResult.Replaced) && _active?.RequestId == requestId)
		{
			Fail("走过来没能排上（" + result + "）");
			return;
		}
		_log.Debug($"偶遇 {scene.Id}：{name} 会走过来找农夫（{plan.Reason}；{(ai ? "AI 写" : "写好的")}台词）：{result}。");
	}

	private void WalkUpFinished(string requestId, string id, string npc, WalkUpLine? line, ApproachOutcome outcome)
	{
		try
		{
			bool mine = _active != null && _active.RequestId == requestId;
			if (mine)
			{
				_active = null;
			}
			if (outcome == ApproachOutcome.Spoken)
			{
				if (Context.IsWorldReady)
				{
					Complete(id, "走过来说上了话", Played(npc, line?.Text));
				}
				return;
			}
			string? text = line?.Text;
			if (text != null)
			{
				int since = line!.SeenTick >= 0 ? line.SeenTick : _approaches.Tick;
				bool isOpening = string.Equals(TodayLine(npc)?.Trim(), text.Trim(), StringComparison.Ordinal);
				if (isOpening && _approaches.HadConversationSince(npc, since))
				{
					if (Context.IsWorldReady)
					{
						Complete(id, "农夫自己找他聊了，那句已经备好", Played(npc, text));
					}
					return;
				}
				if (isOpening && !IsTalkingTo(npc))
				{
					DiscardOpening(npc, text);
				}
			}
			if (mine)
			{
				Failed(id, outcome == ApproachOutcome.Rejected ? "走过来没被接受" : "还没走到农夫跟前就结束了");
			}
		}
		catch (Exception ex)
		{
			Report("走过来收尾", ex);
		}
	}

	private bool CanStartWalkUp(GameLocation location, string npc, int maxTiles)
	{
		Farmer? player = Game1.player;
		if (player == null || Game1.currentLocation != location || _location != location || _tick - _enteredTick < MomentRules.MinTicksInLocation || PlayerBusy(player))
		{
			return false;
		}
		NPC? n = MomentWorld.Villager(location, npc);
		return n != null && !IsNpcOccupied(n.Name) && MomentWorld.InView(n, player, maxTiles);
	}

	// ---------------- 旁听 ----------------

	private void StartOverheard(SceneDefinition scene, SceneDelivery delivery, MomentPlan plan, GameLocation location, Farmer player, bool ai)
	{
		List<SceneLine> fallback = MomentRules.OnlyLinesBy(scene.Lines, plan.Speakers) ? MomentRules.OnlyBy(scene.Lines, plan.Speakers) : new List<SceneLine>();
		Active active = _active = new Active(scene, delivery, MomentPlanKind.Overheard, location, plan.Lead, plan.Speakers, fallback, _tick);
		_log.Debug($"偶遇 {scene.Id}：{string.Join("、", plan.Speakers)} 会在旁边说话（{plan.Reason}；{(ai ? "请 AI 写" : "写好的")}台词）。");
		if (ai && WriteOverheard != null)
		{
			Generate(active, WithFarmer(SceneCatalog.Premise(scene.Lines), player), location.Name);
			return;
		}
		active.Ready = fallback;
		active.ReadySince = _tick;
		TryPlay(active);
	}

	private void Generate(Active active, string premise, string location)
	{
		long version = ++_version;
		CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(MomentRules.GenerationSeconds));
		active.Version = version;
		active.Deadline = _tick + MomentRules.GenerationTicks;
		_generation = cts;
		Task<IReadOnlyList<SceneLine>?> task;
		try
		{
			task = WriteOverheard!(active.Speakers, premise, location, Game1.timeOfDay)(cts.Token);
		}
		catch (Exception ex)
		{
			task = Task.FromException<IReadOnlyList<SceneLine>?>(ex);
		}
		task.ContinueWith(t =>
		{
			if (t.IsFaulted)
			{
				_log.Debug($"偶遇 {active.Id} 的台词没写出来：{t.Exception?.GetBaseException().Message}");
			}
			_generated.Enqueue(new Generated(version, t.Status == TaskStatus.RanToCompletion ? t.Result : null));
			cts.Dispose();
		}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
	}

	private void Drain()
	{
		while (_generated.TryDequeue(out Generated? g))
		{
			Active? a = _active;
			if (a == null || a.Version != g.Version || a.Ready != null)
			{
				continue;
			}
			_generation = null;
			List<SceneLine> lines = MomentRules.OnlyBy(g.Lines, a.Speakers);
			a.Ready = lines.Count > 0 ? lines : a.Fallback;
			a.ReadySince = _tick;
			_log.Debug($"偶遇 {a.Id}：{(lines.Count > 0 ? $"AI 写了 {lines.Count} 句" : "AI 没写出能用的，用写好的")}。");
		}
	}

	private void Advance()
	{
		Active a = _active!;
		if (_tick - a.StartedTick > MomentRules.StaleTicks)
		{
			Cancel("拖得太久");
			Failed(a.Id, "拖得太久");
			return;
		}
		if (a.Kind == MomentPlanKind.WalkUp)
		{
			if (Game1.currentLocation != a.Location && !_approaches.IsNpcBusy(a.Lead))
			{
				_approaches.Withdraw(a.RequestId);
			}
			return;
		}
		if (a.Playing)
		{
			return;
		}
		if (Game1.currentLocation != a.Location)
		{
			Fail("还没开始农夫就走了");
			return;
		}
		if (a.Ready == null)
		{
			if (_tick < a.Deadline)
			{
				return;
			}
			CancelGeneration();
			a.Ready = a.Fallback;
			a.ReadySince = _tick;
			_log.Debug($"偶遇 {a.Id}：AI 写台词超时，用写好的。");
		}
		TryPlay(a);
	}

	private void TryPlay(Active a)
	{
		if (a.Ready == null || a.Ready.Count == 0)
		{
			Fail("没有台词可说");
			return;
		}
		if (WorldBusy())
		{
			if (_tick - a.ReadySince > MomentRules.ReadyTimeoutTicks)
			{
				Fail("游戏一直在忙");
			}
			return;
		}
		List<SceneLine> lines = Playable(a);
		if (lines.Count == 0)
		{
			if (_tick - a.ReadySince > MomentRules.ReadyTimeoutTicks)
			{
				Fail("说话的人一直不在视野里");
			}
			return;
		}
		if (!_player.Start(a.Id, a.Location, lines, _tick, outcome => OverheardFinished(a, outcome)))
		{
			Fail("气泡显示不出来");
			return;
		}
		a.Playing = true;
	}

	private List<SceneLine> Playable(Active a)
	{
		Farmer? player = Game1.player;
		if (player == null || a.Ready == null || Game1.currentLocation != a.Location)
		{
			return new List<SceneLine>();
		}
		if (!a.Speakers.All(n => Near(MomentWorld.Villager(a.Location, n), player, a.Scene.MaxDistanceTiles)))
		{
			return new List<SceneLine>();
		}
		return MomentRules.OnlyBy(a.Ready, a.Speakers);
	}

	private void OverheardFinished(Active a, OverheardOutcome outcome)
	{
		if (_active != a)
		{
			return;
		}
		_active = null;
		if (outcome == OverheardOutcome.Heard)
		{
			Complete(a.Id, "农夫听到了", a.Ready ?? a.Fallback);
		}
		else
		{
			Failed(a.Id, outcome == OverheardOutcome.NotHeard ? "农夫离得太远，没听到" : "被打断了");
		}
	}

	// ---------------- 收尾 ----------------

	private void Complete(string id, string how, IReadOnlyList<SceneLine>? played)
	{
		_tries.Remove(id);
		_scenes.Complete(id, played);
		_log.Debug($"偶遇 {id} 演完了：{how}。");
	}

	private static IReadOnlyList<SceneLine>? Played(string npc, string? text) => string.IsNullOrWhiteSpace(text) ? null : new[] { new SceneLine(npc, text.Trim()) };

	private void Failed(string id, string reason)
	{
		if (!_tries.TryGetValue(id, out Tries? t))
		{
			t = _tries[id] = new Tries();
		}
		t.Count++;
		t.LastTick = _tick;
		t.Last = reason;
		_log.Debug($"偶遇 {id} 没演成（{t.Count}/{MomentRules.MaxTriesPerDay}）：{reason}。");
	}

	private void Fail(string reason)
	{
		Active? a = _active;
		if (a != null)
		{
			Cancel(reason);
			Failed(a.Id, reason);
		}
	}

	private void Cancel(string reason)
	{
		Active? a = _active;
		_active = null;
		CancelGeneration();
		_player.Abort(reason);
		if (a != null)
		{
			_log.Debug($"偶遇 {a.Id} 停了：{reason}。");
		}
	}

	private void CancelGeneration()
	{
		CancellationTokenSource? cts = _generation;
		_generation = null;
		try
		{
			cts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private void ResetUnlessFarmhand(string reason)
	{
		if (!FarmhandScreen)
		{
			Reset(reason);
		}
	}

	private void Reset(string reason)
	{
		try
		{
			Cancel(reason);
		}
		catch (Exception ex)
		{
			Report("清空", ex);
		}
		_active = null;
		_tries.Clear();
		_location = null;
		_nextCheck = 0;
		while (_generated.TryDequeue(out _))
		{
		}
	}

	private void WatchStyle(ModConfig c)
	{
		string style = SceneStyle.Normalize(c.EncounterStyle);
		if (_style == null)
		{
			_style = style;
		}
		else if (style != _style)
		{
			ApplyStyle(style);
		}
	}

	/// <summary>风格变了：重建事件表；正在进行的偶遇按新风格已经不该这么演的就撤掉。</summary>
	private void ApplyStyle(string style)
	{
		_style = style;
		_scenes.OnStyleChanged();
		_log.Debug($"偶遇风格改成 {style}，事件表重建了。");
		Active? a = _active;
		if (a == null || !Context.IsWorldReady)
		{
			return;
		}
		bool cinematic = _scenes.DeliveryOf(a.Scene) == SceneDelivery.Cinematic;
		bool canWalk = !cinematic && style != SceneStyle.AmbientOnly && (a.Delivery != SceneDelivery.Overheard || style == SceneStyle.Natural);
		bool withdraw;
		if (a.Kind == MomentPlanKind.WalkUp)
		{
			if (_approaches.IsNpcBusy(a.Lead))
			{
				return;
			}
			withdraw = !canWalk;
		}
		else
		{
			withdraw = cinematic;
		}
		if (withdraw)
		{
			Cancel("风格改成了 " + style);
			if (a.Kind == MomentPlanKind.WalkUp)
			{
				_approaches.Withdraw(a.RequestId);
			}
		}
	}

	private void TrackLocation()
	{
		GameLocation? here = Game1.currentLocation;
		if (here != _location)
		{
			_location = here;
			_enteredTick = _tick;
		}
	}

	private bool CalledAway(Active a) => a.Kind == MomentPlanKind.Overheard && a.Speakers.Any(n => _approaches.IsNpcInvolved(n) || IsNpcOccupied(n));

	private bool Near(NPC? npc, Farmer player, int maxTiles) => npc != null && !_approaches.IsNpcInvolved(npc.Name) && !IsNpcOccupied(npc.Name) && MomentWorld.InView(npc, player, maxTiles);

	private bool WorldBusy() => Game1.eventUp || Game1.currentLocation?.currentEvent != null || Game1.isFestival() || Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.isWarping || Game1.newDay || InDayTransition() || Generating();

	private static bool PlayerBusy(Farmer player) => !Context.CanPlayerMove || player.UsingTool || player.isRidingHorse();

	/// <summary>这位村民正在演偶遇（旁听或走过来），别的系统先别拉走。</summary>
	public bool IsInScene(string npc) => _active is Active a && (a.Speakers.Contains(npc, StringComparer.OrdinalIgnoreCase) || string.Equals(a.Lead, npc, StringComparison.OrdinalIgnoreCase));

	private static string WithFarmer(string text, Farmer player) => string.IsNullOrEmpty(text) ? "" : text.Replace("@", player.Name);

	private static string Phase(Active a) => a.Kind == MomentPlanKind.WalkUp ? a.Lead + " 走过来"
		: a.Playing ? string.Join("、", a.Speakers) + " 的气泡"
		: a.Ready != null ? "台词备好了，等游戏空下来、说话的人进视野"
		: "在请 AI 写旁听台词";

	private void Report(string stage, Exception ex)
	{
		if (_tick - _lastFailureTick < 3600)
		{
			_suppressed++;
			return;
		}
		string more = _suppressed > 0 ? $"（上次报告后又出了 {_suppressed} 次）" : "";
		_lastFailureTick = _tick;
		_suppressed = 0;
		_log.Warn($"偶遇{stage}出错：{ex.Message}{more}");
		_log.Debug(ex.ToString());
	}
}
