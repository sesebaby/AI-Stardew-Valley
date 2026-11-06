using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Approach;
using AISV.Core.Awareness;
using AISV.Core.Social;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Approach;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace AISV.Game.Awareness;

/// <summary>
/// 看到听到：传感器报来的事 → 找目击者、定分量 → 记进记忆 → 按情绪计量决定谁走过来说、谁放在心里、谁冒一句。
/// 提供对话里"你亲眼见过的事"和心事引子；每天结束挑三件交给八卦。只在主机的主线程上跑。
/// </summary>
public sealed class AwarenessSystem
{
	public const string Source = "seen-and-heard";

	private const int TalkMergeMinutes = 60;

	private const int BubbleMilliseconds = 3000;

	private const double MinApproachRelevance = 0.18;

	public static readonly IReadOnlyCollection<string> NotGossip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "map", "place", "npc_move", "talk", "phone_text", "stare", "block_path", "swim", "use_appliance" };

	private sealed class PendingFeeling
	{
		public PendingFeeling(string requestId, string npc, string meterId, int tier, IReadOnlyList<string> eventIds, string reason, string? rewardItem, int rewardCount, string textSituation = "")
		{
			TextSituation = textSituation;
			RequestId = requestId;
			Npc = npc;
			MeterId = meterId;
			Tier = tier;
			EventIds = eventIds;
			Reason = reason;
			RewardItem = rewardItem;
			RewardCount = rewardCount;
		}

		public string RequestId { get; }

		public string Npc { get; }

		public string MeterId { get; }

		public int Tier { get; }

		public IReadOnlyList<string> EventIds { get; }

		public string Reason { get; }

		/// <summary>走不过来改发消息时给 AI 的情况。</summary>
		public string TextSituation { get; }

		public string? RewardItem { get; }

		public int RewardCount { get; }

		public bool Said { get; set; }

		public bool Withdrawn { get; set; }
	}

	private sealed record ColdShoulder(int Minute, bool Used);

	private readonly Func<ModConfig> _config;

	private readonly NpcDirectory _npcs;

	private readonly ApproachCoordinator _approaches;

	private readonly MaterialFactory _materials;

	private readonly DialogueEngine _dialogue;

	private readonly ILog _log;

	private readonly string _catalogPath;

	private readonly Witnesses _witnesses;

	private readonly Dictionary<string, PendingFeeling> _pending = new Dictionary<string, PendingFeeling>(StringComparer.Ordinal);

	private readonly BystanderHooks _hooks = new BystanderHooks();

	private readonly Queue<(string Npc, string MeterId, int Tier)> _delivered = new Queue<(string, string, int)>();

	private readonly Dictionary<string, ColdShoulder> _coldShoulders = new Dictionary<string, ColdShoulder>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, long> _throttle = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

	private readonly Random _random = new Random();

	private SightingCatalog? _catalog;

	private bool _resetting;

	public AwarenessSystem(Func<ModConfig> config, Func<DocumentStore?> store, NpcDirectory npcs, ApproachCoordinator approaches, MaterialFactory materials, DialogueEngine dialogue, string modDirectory, ILog log)
	{
		_config = config;
		_npcs = npcs;
		_approaches = approaches;
		_materials = materials;
		_dialogue = dialogue;
		_log = log;
		_catalogPath = Path.Combine(modDirectory, "assets", "awareness", "catalog.json");
		Memory = new AwarenessMemory(store);
		Feelings = new Feelings(() => Catalog);
		Writer = new SightingWriter(() => Catalog);
		Bubbles = new Bubbles();
		_witnesses = new Witnesses(() => Catalog, npcs.IsEnabled, () => Memory.Events);
	}

	public AwarenessMemory Memory { get; }

	public Feelings Feelings { get; }

	public SightingWriter Writer { get; }

	public Bubbles Bubbles { get; }

	/// <summary>心情加减（社交模拟接上）。</summary>
	public Action<string, int>? AdjustMood { get; set; }

	/// <summary>把今天最值得传的事交给八卦。</summary>
	public Action<string>? RecordGossip { get; set; }

	/// <summary>没能走过来时改发消息：(村民, 情况说明, 发出去后) → 发没发。</summary>
	public Func<string, string, Action, bool>? SendText { get; set; }

	/// <summary>村民正忙别的（比如陪钓）。</summary>
	public Func<string, bool>? IsBusyElsewhere { get; set; }

	/// <summary>跟在农夫身边的人：不走过来，原地头顶说一句。</summary>
	public Func<string, bool>? IsFollowing { get; set; }

	internal FishingCompanion? Fishing { get; set; }

	public bool Enabled => _config().NoticingOn && _config().AiOn;

	public SightingCatalog Catalog
	{
		get
		{
			if (_catalog == null)
			{
				try
				{
					_catalog = SightingCatalog.Parse(File.ReadAllText(_catalogPath));
					foreach (string p in _catalog.Problems)
					{
						_log.Warn("看到听到的目录：" + p);
					}
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					_log.Warn("读不了看到听到的目录，先不记任何事：" + ex.Message);
					_catalog = SightingCatalog.Empty;
				}
			}
			return _catalog;
		}
	}

	public static int Today => Math.Max(1, GameState.Today.TotalDays);

	public static int NowMinute => Today * 1440 + SightingText.ToMinutes(Game1.timeOfDay);

	public bool IsNpcInApproach(string npc) => _approaches.IsNpcBusy(npc);

	/// <summary>玩家正在和他说话或刚说过。</summary>
	public Func<string, bool>? IsTalking { get; set; }

	public bool IsNpcOccupied(string npc) => (IsTalking?.Invoke(npc) ?? false) || _approaches.IsNpcBusy(npc) || (IsBusyElsewhere?.Invoke(npc) ?? false);

	// ---- 生命周期 ----

	public void OnSaveLoaded()
	{
		ResetRuntime("换了存档");
		Memory.Reset();
		Feelings.Load(Memory.Grudges.Select(g => new FeelingGrudge(g.NpcName, g.MeterId, g.Amount)));
		Memory.OnDayStarted(Today);
	}

	public void OnDayStarted()
	{
		ResetRuntime("新的一天");
		Memory.OnDayStarted(Today);
		Feelings.OnDayStarted(NowMinute, Today.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>天黑：放回所有人，把火气记下，挑三件事交给八卦。要在社交模拟整理之前调。</summary>
	public void OnDayEnding()
	{
		ResetRuntime("天黑了");
		int day = Today;
		Memory.OnDayEnding(day);
		Memory.SetGrudges(Feelings.Grudges());
		if (!Enabled || RecordGossip == null)
		{
			return;
		}
		foreach (Sighting e in Memory.GossipOfTheDay(day, 3, NotGossip))
		{
			RecordGossip(Writer.GossipLine(e));
		}
	}

	public void OnSaving()
	{
		_approaches.AbortAll("存档");
		Memory.SetGrudges(Feelings.Grudges());
	}

	public void OnReturnedToTitle()
	{
		ResetRuntime("回到标题");
		_coldShoulders.Clear();
		Feelings.Clear();
		Memory.Reset();
	}

	private void ResetRuntime(string why)
	{
		_resetting = true;
		try
		{
			_approaches.AbortAll(why);
		}
		finally
		{
			_resetting = false;
		}
		_pending.Clear();
		_hooks.Clear();
		_delivered.Clear();
		_coldShoulders.Clear();
		_throttle.Clear();
		Bubbles.Reset();
	}

	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || Context.ScreenId > 0)
		{
			return;
		}
		foreach (PendingFeeling p in _pending.Values.Where(p => p.Said && !p.Withdrawn).ToList())
		{
			p.Withdrawn = true;
			_approaches.Withdraw(p.RequestId);
		}
		while (_delivered.Count > 0)
		{
			(string npc, string meter, int tier) = _delivered.Dequeue();
			if (!Enabled)
			{
				continue;
			}
			if (_config().NoticingMovesHearts)
			{
				ApplyFriendship(npc, meter, tier);
			}
			int tiers = Catalog.TryGetMeter(meter, out MeterDefinition m) ? m.TierCount : 3;
			if (FeelingConsequences.EarnsColdShoulder(meter, tier, tiers) && (!_coldShoulders.TryGetValue(npc, out ColdShoulder? cs) || !cs.Used))
			{
				_coldShoulders[npc] = new ColdShoulder(NowMinute, false);
			}
		}
	}

	// ---- 记一件事 ----

	/// <summary>节流：同一个键在冷却内只过一次（<paramref name="cooldown"/> 为 long.MaxValue 时到重置为止）。</summary>
	public bool Pass(string key, long now, long cooldown)
	{
		if (_throttle.TryGetValue(key, out long last) && (cooldown == long.MaxValue || now - last < cooldown))
		{
			return false;
		}
		_throttle[key] = now;
		return true;
	}

	public void Mark(string key, long now) => _throttle[key] = now;

	/// <summary>农场工也在自己这边记（走过来只在房主排，排不上的放在心里）。</summary>
	public Sighting? Record(Observation o, List<string>? report = null)
	{
		if (!Enabled || !Context.IsWorldReady || Context.ScreenId > 0)
		{
			return null;
		}
		try
		{
			return RecordCore(o, report);
		}
		catch (Exception ex)
		{
			_log.Warn($"看到听到：记录 {o.Type} 时出错：{ex.Message}");
			return null;
		}
	}

	private Sighting? RecordCore(Observation o, List<string>? report)
	{
		GameLocation? location = ResolveLocation(o.Location);
		if (location == null)
		{
			report?.Add("找不到地图 " + o.Location + "。");
			return null;
		}
		string type = SightingCatalog.NormalizeType(o.Type);
		o = o with { Type = type };
		bool fishing = type == "fishing" && Fishing != null;
		if (fishing && Fishing!.SessionFinished(o.Detail("fishingSession")))
		{
			report?.Add("这次抛竿已经有人来陪了。");
			return null;
		}
		EventDefinition def = Catalog.Get(type);
		Dictionary<string, string> details = o.Details == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(o.Details, StringComparer.OrdinalIgnoreCase);
		Witnesses.Enrich(type, o, details, location);
		o = o with { Details = details };
		Vector2 tile = o.Tile ?? Game1.player?.Tile ?? Vector2.Zero;
		details["summary"] = o.Summary;
		details["actionId"] = type;
		if (!string.IsNullOrWhiteSpace(o.Subject))
		{
			details["subject"] = o.Subject;
		}
		if (!details.ContainsKey("locationDisplay"))
		{
			details["locationDisplay"] = PlaceName(location);
		}
		List<Witness> witnesses = _witnesses.Resolve(location, tile, o, def, details);
		if (witnesses.Count == 0)
		{
			report?.Add(DescribeNoWitnesses(location, tile, o, def));
			return null;
		}
		double? significance = Witnesses.Number(o, "sh.significance", 0.05, 1.0);
		Dictionary<string, RuleReward> rewards = new Dictionary<string, RuleReward>(StringComparer.OrdinalIgnoreCase);
		int day = Today;
		if (def.IsKnown)
		{
			double grade = Aggravators.Grade(def, details, Game1.timeOfDay);
			details["grade"] = Invariant(grade);
			details["halfLifeDays"] = Invariant(def.FadeDays);
			significance ??= Aggravators.Significance(grade);
			witnesses = _witnesses.Shape(def, witnesses, o, details, location, tile, day, rewards);
		}
		details["displayNames"] = DisplayNames(o, details, witnesses);
		Sighting e = new Sighting
		{
			EventId = $"{day}:{Game1.timeOfDay}:{type}:{Guid.NewGuid():N}",
			Act = Sighting.ActionFor(type),
			Doer = o.Actor,
			Place = location.NameOrUniqueName,
			X = (int)tile.X,
			Y = (int)tile.Y,
			OnDay = day,
			Clock = Game1.timeOfDay,
			Weight = significance ?? Aggravators.Significance(def.Severity),
			FirstHand = witnesses.Any(w => w.FirstHand),
			Facts = details
		};
		if (fishing)
		{
			return RecordFishing(e, witnesses, report);
		}
		if (!Memory.Record(e, witnesses.Select(w => w.Copy())))
		{
			report?.Add("目击者看得都太模糊，没记下。");
			return null;
		}
		report?.Add($"记下了 {type}：{string.Join("、", e.SeenBy.Select(w => $"{w.Who}（{w.Part}，确定度 {w.Sureness}，{string.Join("/", w.Gauges.Select(m => $"{m.Key} {m.Value:0.##}"))}）"))}");
		if (UsesExplicitReaction(def, details))
		{
			if (IsMemoryOnly(o))
			{
				TryBubble(def, e, o, report);
			}
			else
			{
				report?.Add(EnqueueReaction(e, e.SeenBy.Where(w => !(IsBusyElsewhere?.Invoke(w.Who) ?? false) && !SkipsSubject(o, w.Who)).ToList()));
			}
		}
		else if (IsMemoryOnly(o))
		{
			TryBubble(def, e, o, report);
		}
		else if (!React(e, rewards, npc => CanWalkOver(o, npc), report))
		{
			TryBubble(def, e, o, report);
		}
		return e;
	}

	/// <summary>抛竿期间每隔半秒报一次：新看到的人记一笔，再看看有没有人来陪钓。</summary>
	private Sighting? RecordFishing(Sighting e, List<Witness> witnesses, List<string>? report)
	{
		List<Witness> fresh = witnesses.Where(w => Fishing!.Unseen(w.Who)).Select(w => w.Copy()).ToList();
		bool recorded = fresh.Count > 0 && Memory.Record(e, fresh);
		if (recorded)
		{
			fresh.ForEach(w => Fishing!.MarkSeen(w.Who));
		}
		bool joined = Fishing!.TryInvite(e, witnesses);
		report?.Add(joined ? "有人来陪钓了。" : $"这回没人来陪钓：{Fishing.LastRefusal}。");
		return recorded ? e : null;
	}

	/// <summary>喂情绪计量：一件事最多一个人走过来，其余升了档的放在心里。</summary>
	private bool React(Sighting e, IReadOnlyDictionary<string, RuleReward> rewards, Func<string, bool> canWalkOver, List<string>? report)
	{
		if (!Enabled)
		{
			return false;
		}
		int now = NowMinute;
		string type = e.Type;
		EventDefinition def = Catalog.Get(type);
		int walkFrom = def.Callout?.EffectiveWalkUpFromLevel ?? 1;
		double grade = double.TryParse(e.Detail("grade"), NumberStyles.Float, CultureInfo.InvariantCulture, out double g) ? g : def.Severity;
		List<(FeelingSignal Signal, Witness Witness)> signals = new List<(FeelingSignal, Witness)>();
		foreach (Witness w in e.SeenBy.Where(w => w.Gauges.Count > 0))
		{
			double score = MeterScore(grade, w, type, e);
			IReadOnlyList<FeelingSignal> got = Feelings.Feel(new FeelingInput(w.Who, e.EventId, type, w.Gauges, score, Certainty.Level(w), w.Quiet, rewards.GetValueOrDefault(w.Who)), now);
			foreach (FeelingSignal s in got)
			{
				signals.Add((s, w));
				OnTierReached(s);
				report?.Add($"{s.Npc} 的 {s.MeterId} 到了第 {s.Tier} 档（{s.Reading:0.#}）。");
			}
		}
		if (signals.Count == 0)
		{
			return false;
		}
		bool walking = false;
		foreach ((FeelingSignal s, Witness w) in signals.OrderByDescending(x => Math.Clamp(1.0 - x.Witness.TilesAway / 12.0, 0.15, 1.0) * x.Signal.Intensity * x.Signal.Priority))
		{
			if (s.Tier >= walkFrom && !walking && (IsFollowing?.Invoke(s.Npc) ?? false) && SayInPlace(e, s, w))
			{
				report?.Add($"{s.Npc} 跟在身边，原地说一句。");
				_hooks.MarkRaised(s.Npc, e.EventId);
				walking = true;
				continue;
			}
			if (s.Tier >= walkFrom && !walking && canWalkOver(s.Npc))
			{
				(bool ok, string detail) = EnqueueFeeling(e, s, w);
				report?.Add(ok ? $"{s.Npc} 会走过来：{detail}" : $"{s.Npc} 没走过来：{detail}");
				if (ok)
				{
					_hooks.MarkRaised(s.Npc, e.EventId);
					walking = true;
					continue;
				}
			}
			_hooks.MarkRaised(s.Npc, e.EventId);
			Feelings.KeepAsAside(s, now);
			report?.Add($"{s.Npc} 把这事放在心里。");
		}
		return walking;
	}

	private static double MeterScore(double grade, Witness w, string type, Sighting e)
	{
		double personally = w.Part switch { "target" => 1.4, "relative" => 1.2, "vendor" => 0.6, _ => 1.0 };
		bool ownBin = type == "trash" && SightingText.SplitList(e.Detail("owners")).Contains(w.Who, StringComparer.OrdinalIgnoreCase);
		bool habitualDrinker = type == "consume" && e.Detail("kind") == "alcohol" && w.Repeats >= 3;
		return grade * personally * Math.Clamp(w.Confidence, 0, 1) * (ownBin ? 1.6 : 1.0) * (habitualDrinker ? 1.8 : 1.0);
	}

	private bool SayInPlace(Sighting e, FeelingSignal s, Witness w)
	{
		if (!Catalog.TryGetMeter(s.MeterId, out MeterDefinition meter) || ApproachCoordinator.FindNpc(s.Npc) is not NPC npc)
		{
			return false;
		}
		string farmer = PlayerName;
		string feeling = Core.Awareness.Feelings.ReactionFeeling(meter, s.Tier, farmer, MemoryLines(s.Npc, s.EventIds.Where(id => id != e.EventId).ToList(), Today, Game1.timeOfDay));
		WitnessReaction reaction = AwarenessPrompts.Reaction(Catalog, e, w, farmer, Season, Writer.Sentence(e, w), feeling);
		DialogueMaterial material = _materials.Build(npc, e.Place, awarenessFeelings: false);
		GameDateCapture date = new GameDateCapture(GameState.Today);
		int? emote = Emotes.Resolve(meter.Emote);
		Feelings.MarkPending(s);
		Task.Run(async () =>
		{
			string? text = await _dialogue.ReactionOpeningAsync(material, reaction, date.Date, CancellationToken.None).ConfigureAwait(false);
			MainThread.Run(() =>
			{
				if (string.IsNullOrWhiteSpace(text) || npc.currentLocation == null)
				{
					Feelings.ReleasePending(s.Npc, meter.Id, s.Tier, NowMinute);
					return;
				}
				if (emote.HasValue)
				{
					npc.doEmote(emote.Value);
				}
				NpcSpeech.Say(npc, TextTools.CleanForGame(text));
				Feelings.MarkSpoken(s.Npc, meter.Id, s.Tier, NowMinute);
				_delivered.Enqueue((s.Npc, meter.Id, s.Tier));
			});
		});
		return true;
	}

	private (bool Ok, string Detail) EnqueueFeeling(Sighting e, FeelingSignal s, Witness w)
	{
		if (!Catalog.TryGetMeter(s.MeterId, out MeterDefinition meter))
		{
			return (false, "没有这个计量");
		}
		NPC? npc = ApproachCoordinator.FindNpc(s.Npc);
		if (npc == null)
		{
			return (false, "找不到人");
		}
		int day = Today;
		int time = Game1.timeOfDay;
		string farmer = PlayerName;
		List<string> earlier = MemoryLines(s.Npc, s.EventIds.Where(id => id != e.EventId).ToList(), day, time);
		string feeling = Core.Awareness.Feelings.ReactionFeeling(meter, s.Tier, farmer, earlier);
		int count = s.Reward?.CountFor(s.Tier) ?? 0;
		ParsedItemData? item = count > 0 ? ItemRegistry.GetData(s.Reward!.Item) : null;
		if (item != null)
		{
			feeling += AwarenessPrompts.HandOverLine(count, item.DisplayName);
		}
		WitnessReaction reaction = AwarenessPrompts.Reaction(Catalog, e, w, farmer, Season, Writer.Sentence(e, w), feeling);
		string requestId = $"{e.EventId}:{s.Npc}:{meter.Id}:{s.Tier}";
		PendingFeeling pending = new PendingFeeling(requestId, s.Npc, meter.Id, s.Tier, s.EventIds, Core.Awareness.Feelings.Reason(meter, s.Tier, farmer, MemoryLines(s.Npc, s.EventIds, day, time), approached: true), item == null ? null : s.Reward!.Item, count,
			"你看到的事：" + Writer.Sentence(e, w) + "\n" + feeling);
		if (!_pending.TryAdd(requestId, pending))
		{
			return (false, "同样的已经在排队");
		}
		DialogueMaterial material = _materials.Build(npc, e.Place, awarenessFeelings: false);
		GameDateCapture date = new GameDateCapture(GameState.Today);
		EnqueueResult r = _approaches.Enqueue(new ApproachIntent(Source, requestId, s.Npc, $"{day}:{s.Npc}:feeling:{meter.Id}", e.Place, time)
		{
			Relevance = Math.Clamp(s.Intensity * s.Priority / 2.0, MinApproachRelevance, 1.0),
			DistanceTiles = w.TilesAway,
			Priority = s.Tier * 100 + (int)Math.Round(Math.Min(99.0, s.Intensity * s.Priority * 10.0)),
			TtlTicks = 7200,
			PrepareDialogue = ct => _dialogue.ReactionOpeningAsync(material, reaction, date.Date, ct),
			FallbackDialogue = NullIfEmpty(e.Detail("fallbackDialogue")),
			PreparationTimeoutTicks = TimeoutTicks(e.Detail("preparationTimeoutSeconds")),
			StartWhilePreparing = true,
			UrgentApproach = true,
			ShowAttentionMarker = true,
			AnnounceEmote = Emotes.Resolve(meter.Emote),
			CanStartNow = () => !pending.Said,
			CanPresent = _ => !pending.Said,
			OnFinished = outcome => FinishFeeling(requestId, outcome)
		});
		if (r is EnqueueResult.Accepted or EnqueueResult.Replaced)
		{
			Feelings.MarkPending(s);
			return (true, $"{meter.Label}，强度 {s.Intensity:0.00}");
		}
		_pending.Remove(requestId);
		return (false, r switch
		{
			EnqueueResult.RejectedLowerPriority => "已经有更要紧的在排队",
			EnqueueResult.RejectedFull => "队伍满了",
			_ => "排不上（" + r + "）"
		});
	}

	private void FinishFeeling(string requestId, ApproachOutcome outcome)
	{
		if (!_pending.Remove(requestId, out PendingFeeling? p))
		{
			return;
		}
		int now = NowMinute;
		if (outcome == ApproachOutcome.Spoken || p.Said)
		{
			Feelings.MarkSpoken(p.Npc, p.MeterId, p.Tier, now);
			_delivered.Enqueue((p.Npc, p.MeterId, p.Tier));
			if (outcome == ApproachOutcome.Spoken && p.RewardItem != null)
			{
				HandOver(p.RewardItem, p.RewardCount);
			}
		}
		else if (outcome != ApproachOutcome.Rejected && !_pending.Values.Any(o => Same(o.Npc, p.Npc) && Same(o.MeterId, p.MeterId)))
		{
			if (TryTextInstead(p))
			{
				Feelings.MarkSpoken(p.Npc, p.MeterId, p.Tier, now);
			}
			else
			{
				Feelings.ReleasePending(p.Npc, p.MeterId, p.Tier, now);
			}
		}
	}

	private bool TryTextInstead(PendingFeeling p)
	{
		if (_resetting || SendText == null || !Enabled || !_config().NoticeByText || !Context.IsWorldReady || string.IsNullOrWhiteSpace(p.TextSituation))
		{
			return false;
		}
		try
		{
			return SendText(p.Npc, p.TextSituation, () => _delivered.Enqueue((p.Npc, p.MeterId, p.Tier)));
		}
		catch (Exception ex)
		{
			_log.Warn($"{p.Npc} 改发消息失败：{ex.Message}");
			return false;
		}
	}

	/// <summary>跟踪、受伤这类不经情绪计量、直接走过来的事：挑分量×确信度最高的人。</summary>
	private string EnqueueReaction(Sighting e, IReadOnlyList<Witness> witnesses)
	{
		Witness? w = witnesses.Where(x => !string.IsNullOrWhiteSpace(x.Who)).OrderByDescending(x => e.Weight * x.Confidence).ThenBy(x => x.TilesAway).FirstOrDefault();
		if (w == null)
		{
			return "没人能走过来。";
		}
		double relevance = e.Weight * w.Confidence;
		if (relevance < MinApproachRelevance)
		{
			return $"{w.Who} 的相关度 {relevance:0.00} 不到 {MinApproachRelevance}，不走过来。";
		}
		NPC? npc = ApproachCoordinator.FindNpc(w.Who);
		if (npc == null)
		{
			return "找不到 " + w.Who + "。";
		}
		WitnessReaction reaction = AwarenessPrompts.Reaction(Catalog, e, w, PlayerName, Season);
		DialogueMaterial material = _materials.Build(npc, e.Place, awarenessFeelings: false);
		GameDateCapture date = new GameDateCapture(GameState.Today);
		EnqueueResult r = _approaches.Enqueue(new ApproachIntent(Source, e.EventId + ":" + w.Who, w.Who, $"{e.OnDay}:{w.Who}:{e.Act}", e.Place, e.Clock)
		{
			Relevance = relevance,
			DistanceTiles = w.TilesAway,
			Priority = Math.Max(1, (int)Math.Round(e.Weight * 100.0)),
			TtlTicks = 7200,
			PrepareDialogue = ct => _dialogue.ReactionOpeningAsync(material, reaction, date.Date, ct),
			FallbackDialogue = NullIfEmpty(e.Detail("fallbackDialogue")),
			PreparationTimeoutTicks = TimeoutTicks(e.Detail("preparationTimeoutSeconds")),
			StartWhilePreparing = true,
			UrgentApproach = true,
			ShowAttentionMarker = true,
			OnFinished = outcome => FinishReaction(e, outcome == ApproachOutcome.Spoken)
		});
		if (r is EnqueueResult.Accepted or EnqueueResult.Replaced)
		{
			_hooks.MarkRaised(w.Who, e.EventId);
		}
		return r switch
		{
			EnqueueResult.Accepted => $"{w.Who} 会走过来（相关度 {relevance:0.00}）。",
			EnqueueResult.Replaced => $"{w.Who} 会走过来，替换了之前排的（相关度 {relevance:0.00}）。",
			EnqueueResult.RejectedLowerPriority => $"{w.Who} 已经有更要紧的在排队。",
			EnqueueResult.RejectedFull => "队伍满了。",
			_ => $"排不上（{r}）。"
		};
	}

	/// <summary>显式反应结束：受伤的事要告诉传感器；说出口了就把要给的东西交出去。</summary>
	public event Action<Sighting, bool>? ReactionFinished;

	private void FinishReaction(Sighting e, bool spoken)
	{
		ReactionFinished?.Invoke(e, spoken);
		string item = e.Detail("rewardItem");
		if (spoken && item.Length > 0)
		{
			HandOver(item, 1);
		}
	}

	private void TryBubble(EventDefinition def, Sighting e, Observation o, List<string>? report)
	{
		if (!Enabled || !_config().NoticeRemarks || e.SeenBy.Count == 0)
		{
			return;
		}
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (player == null || here == null)
		{
			return;
		}
		string packed = e.Detail("sh.bubbleText").Trim();
		List<(BubbleCandidate Candidate, NPC Npc, BubbleDefinition Bubble)> list = new List<(BubbleCandidate, NPC, BubbleDefinition)>();
		foreach (Witness w in e.SeenBy)
		{
			if (SkipsSubject(o, w.Who))
			{
				continue;
			}
			BubbleDefinition? bubble = packed.Length > 0 ? BubbleDefinition.Single(packed, e.Detail("subject").Length > 0) : def.BubbleFor(w.FiredRules);
			if (bubble == null || here.getCharacterFromName(w.Who) is not NPC npc || !ApproachCoordinator.IsVillager(npc))
			{
				continue;
			}
			bool busy = npc.isSleeping.Value || IsNpcOccupied(npc.Name) || _approaches.IsNpcInvolved(npc.Name);
			list.Add((new BubbleCandidate(w.Who, w.Part, Certainty.Level(w, def.MinCertainty), w.TilesAway, Bucket(npc, player), busy), npc, bubble));
		}
		BubbleChoice? choice = null;
		foreach (IGrouping<BubbleDefinition, (BubbleCandidate Candidate, NPC Npc, BubbleDefinition Bubble)> group in list.GroupBy(x => x.Bubble))
		{
			choice = Bubbles.Choose(e.Type, group.Key, group.Select(x => x.Candidate).ToList(), Game1.ticks, key => Tr.Get(key, key), (speaker, token) => BubbleToken(e, speaker, token));
			if (choice != null)
			{
				break;
			}
		}
		if (choice == null)
		{
			return;
		}
		NPC speaker = list.First(x => Same(x.Candidate.Npc, choice.Npc)).Npc;
		speaker.faceTowardFarmerForPeriod(BubbleMilliseconds, 12, false, player);
		if (choice.Emote is int emote)
		{
			speaker.doEmote(emote);
		}
		NpcSpeech.Say(speaker, choice.Text);
		report?.Add($"{speaker.Name} 冒了一句：{choice.Text}");
	}

	private string? BubbleToken(Sighting e, string speaker, string token)
	{
		Dictionary<string, string> names = SightingWriter.Names(e);
		string? Detail(string key)
		{
			string v = SightingText.Clip(SightingText.FirstLine(e.Detail(key)), 60);
			return v.Length > 0 ? v : null;
		}
		return token.ToLowerInvariant() switch
		{
			"farmer" or "actor" => PlayerName,
			"npc" => SightingWriter.Display(names, speaker),
			"target" => Detail("subject") is string t ? SightingWriter.Display(names, t) : null,
			"item" => Detail("item") ?? Detail("items"),
			"location" => SightingWriter.LocationName(e),
			_ => Detail(token)
		};
	}

	private static string Bucket(NPC npc, Farmer player)
	{
		if (ChildSafety.IsChild(npc.Name, npc.Age))
		{
			return "child";
		}
		if (player.friendshipData.TryGetValue(npc.Name, out Friendship? f) && !f.IsRoommate())
		{
			if (f.IsMarried())
			{
				return "spouse";
			}
			if (f.IsDating() || f.IsEngaged())
			{
				return "partner";
			}
		}
		return player.getFriendshipHeartLevelForNPC(npc.Name) < 4 ? "default" : "friend";
	}

	// ---- 说出口之后 ----

	private void OnTierReached(FeelingSignal s)
	{
		if (AdjustMood == null || !_config().NoticingMovesMood)
		{
			return;
		}
		int delta = FeelingConsequences.MoodDelta(s.MeterId, s.Tier, ChildSafety.IsChild(s.Npc, null));
		if (delta != 0)
		{
			AdjustMood(s.Npc, delta);
		}
	}

	private void ApplyFriendship(string npcName, string meter, int tier)
	{
		int delta = FeelingConsequences.FriendshipDelta(meter, tier, ChildSafety.IsChild(npcName, null));
		Farmer player = Game1.player;
		if (delta == 0 || player == null || Game1.getCharacterFromName(npcName) is not NPC npc)
		{
			return;
		}
		if (!player.friendshipData.ContainsKey(npcName))
		{
			if (delta < 0)
			{
				return;
			}
			player.friendshipData.Add(npcName, new Friendship(0));
		}
		player.changeFriendship(delta, npc);
		_log.Debug($"{npcName} 说出了心里话（{meter} 第 {tier} 档），好感 {delta:+#;-#}。");
	}

	/// <summary>发完大火后一小时内第一次找他说话：他转身不理，说一句冷淡的话（返回 null 表示照常聊）。</summary>
	public string? TryColdShoulder(NPC npc)
	{
		if (!Enabled || !_config().SilentTreatment || !_coldShoulders.TryGetValue(npc.Name, out ColdShoulder? cs))
		{
			return null;
		}
		int now = NowMinute;
		if (!FeelingConsequences.ShouldColdShoulder(cs.Minute, now, cs.Used))
		{
			if (cs.Used || now - cs.Minute > FeelingConsequences.ColdShoulderWindowMinutes)
			{
				_coldShoulders.Remove(npc.Name);
			}
			return null;
		}
		_coldShoulders[npc.Name] = cs with { Used = true };
		Farmer player = Game1.player;
		if (player != null)
		{
			float dx = player.Tile.X - npc.Tile.X;
			float dy = player.Tile.Y - npc.Tile.Y;
			int toward = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
			npc.faceDirection((toward + 2) % 4);
		}
		npc.doEmote(Emotes.Resolve("angry") ?? 12);
		return FeelingConsequences.ColdShoulderLine(_random.NextDouble()) switch
		{
			1 => Tr.Get("awareness.coldShoulder.1", "……我现在不想跟你说话。"),
			2 => Tr.Get("awareness.coldShoulder.2", "别来烦我，让我自己待会儿。"),
			_ => Tr.Get("awareness.coldShoulder.3", "哼。")
		};
	}

	private void HandOver(string itemId, int count)
	{
		Farmer player = Game1.player;
		if (player == null || count <= 0 || ItemRegistry.GetData(itemId) == null || ItemRegistry.Create(itemId, count) is not Item item)
		{
			return;
		}
		Item? left = player.addItemToInventory(item);
		if (left != null)
		{
			Game1.createItemDebris(left, player.Position, player.FacingDirection, player.currentLocation);
		}
		else
		{
			Game1.addHUDMessage(HUDMessage.ForItemGained(item, item.Stack));
		}
	}

	// ---- 对话里的块 ----

	private static readonly HashSet<string> SmallTalkTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "map", "place", "npc_move", "talk", "stare", "purchase", "consume", "fishing", "phone_text", "room", "room_locked", "room_unlocked" };

	/// <summary>进卧室只有待得久才算值得聊。</summary>
	private static bool SmallTalk(Sighting e) => SmallTalkTypes.Contains(e.Type)
		&& !(e.Type == "room" && SightingText.SplitList(e.Detail("aggravators")).Contains("roomLongStay", StringComparer.OrdinalIgnoreCase));

	/// <summary>这位村民最近几天亲眼看见农夫做的、值得一聊的事，分量重的在前（给闲聊挑话题）。</summary>
	public IReadOnlyList<(string Id, string Text)> FreshFarmerSightings(string npc, int days)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(npc) || !Context.IsWorldReady)
		{
			return Array.Empty<(string, string)>();
		}
		int day = Today;
		string farmer = PlayerName;
		return Memory.Events
			.Where(e => day - e.OnDay <= days && Same(e.Doer, farmer) && !SmallTalk(e) && e.WitnessNamed(npc) != null)
			.OrderByDescending(e => e.Weight)
			.Select(e => (e.EventId, Writer.Sentence(e, e.WitnessNamed(npc)!)))
			.Where(x => x.Item2.Length > 0)
			.ToList();
	}

	/// <summary>村民日记用：今天的心事、留着下回说的那件、最近看到的值得一提的事。只读，不算说过。</summary>
	public NoticingView DiaryView(string npc)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(npc) || !Context.IsWorldReady)
		{
			return NoticingView.Off;
		}
		int day = Today;
		int time = Game1.timeOfDay;
		string farmer = PlayerName;
		string you = Core.Text.Tr.Get("vdiary.you", "你");
		List<FeelingLine> feelings = Feelings.Readings(npc, NowMinute)
			.Select(r => new FeelingLine(r.MeterId, r.Tier, r.Thresholds.Length, r.ActedTier >= r.Tier && r.ActedTier > 0, r.WalkingOver, r.CarriedIntoTomorrow, RewardName(r.RewardItem))).ToList();
		List<string> sights = Memory.MemoriesOf(npc, day, AwarenessMemory.MaxContextMemories).Where(m => !SmallTalk(m.Event))
			.Select(m => SightingText.Relative(m.Event.OnDay, m.Event.Clock, day, time) + "：" + SightingText.Terminate(Writer.Past(m.Event).Replace(farmer, you))).Distinct().ToList();
		return new NoticingView(true, feelings, Feelings.PeekAside(npc, NowMinute)?.MeterId, sights);
	}

	private static string RewardName(string? item) => string.IsNullOrWhiteSpace(item) ? "" : ItemRegistry.GetDataOrErrorItem(item).DisplayName;

	/// <summary>
	/// "你亲眼见过的事"。带心事时：有待说的（没在走过来的路上）就当引子用掉，否则取放在心里的那条，再没有就看刚才旁观的事。
	/// </summary>
	public string PromptBlock(string npc, bool includeFeelings = true)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(npc) || !Context.IsWorldReady)
		{
			return "";
		}
		try
		{
			int day = Today;
			int time = Game1.timeOfDay;
			string lead = "";
			IReadOnlyCollection<string>? leadIds = null;
			if (includeFeelings)
			{
				PendingFeeling? p = _approaches.IsNpcBusy(npc) ? null : _pending.Values.Where(x => !x.Said && Same(x.Npc, npc)).OrderByDescending(x => x.Tier).FirstOrDefault();
				if (p != null)
				{
					foreach (PendingFeeling other in _pending.Values.Where(x => Same(x.Npc, npc)))
					{
						other.Said = true;
					}
					lead = p.Reason;
					leadIds = p.EventIds;
				}
				else if (Feelings.TakeAside(npc, NowMinute, out bool first) is FeelingAside aside && Catalog.TryGetMeter(aside.MeterId, out MeterDefinition meter))
				{
					lead = Core.Awareness.Feelings.AsideBlock(meter, aside.Tier, MemoryLines(npc, aside.EventIds, day, time));
					leadIds = aside.EventIds;
					if (first)
					{
						_delivered.Enqueue((npc, aside.MeterId, aside.Tier));
					}
				}
				else if (!_approaches.IsNpcBusy(npc) && _hooks.Pick(npc, Memory.MemoriesOf(npc, day), PlayerName, day, time, SmallTalk) is AwarenessMemoryItem hook)
				{
					lead = BystanderHooks.LeadBlock(Writer.MemoryLine(hook.Event, hook.Witness, day, time));
					leadIds = new[] { hook.Event.EventId };
				}
			}
			return AwarenessPrompts.MemoryBlock(Memory.MemoriesOf(npc, day, AwarenessMemory.MaxContextMemories * 3), Writer, day, time, PlayerName, lead, leadIds);
		}
		catch (Exception ex)
		{
			_log.Warn($"拼 {npc} 见过的事时出错：{ex.Message}");
			return "";
		}
	}

	private List<string> MemoryLines(string npc, IReadOnlyCollection<string> ids, int day, int time)
	{
		if (ids.Count == 0)
		{
			return new List<string>();
		}
		return Memory.Events
			.Where(e => ids.Contains(e.EventId))
			.Select(e => (Event: e, Witness: e.WitnessNamed(npc)))
			.Where(x => x.Witness != null)
			.OrderBy(x => x.Event.OnDay).ThenBy(x => x.Event.Clock)
			.Select(x => Writer.MemoryLine(x.Event, x.Witness!, day, time))
			.ToList();
	}

	// ---- 聊天带出来的事 ----

	/// <summary>玩家和村民聊了一轮：记一次"在聊天"（一小时内算同一场），再看有没有吵架、当着恋人调情。</summary>
	public void OnTalk(NPC npc, string playerLine, string npcLine, bool keepSecret, NpcReaction? reaction)
	{
		if (!Enabled || !Context.IsWorldReady || !Context.IsMainPlayer)
		{
			return;
		}
		try
		{
			GameLocation? location = npc.currentLocation ?? Game1.currentLocation;
			if (location == null)
			{
				return;
			}
			NoticeTone(npc, location, playerLine, keepSecret, reaction);
			int day = Today;
			int now = Game1.timeOfDay;
			Sighting? last = Memory.Events.LastOrDefault(e => e.Type == "talk" && Same(e.Detail("subject"), npc.Name) && e.OnDay == day && SightingText.ToMinutes(now) - SightingText.ToMinutes(LastTime(e)) <= TalkMergeMinutes);
			if (last != null)
			{
				Memory.Update(last.EventId, e =>
				{
					e.Facts["turns"] = ((int.TryParse(e.Detail("turns"), out int t) ? t : 1) + 1).ToString(CultureInfo.InvariantCulture);
					e.Facts["lastTime"] = now.ToString(CultureInfo.InvariantCulture);
					SetTalkLines(e.Facts, playerLine, npcLine, keepSecret || e.Detail("secret") == "true");
				});
				return;
			}
			Dictionary<string, string> d = Observation.Map(("turns", "1"));
			SetTalkLines(d, playerLine, npcLine, keepSecret);
			string farmer = PlayerName;
			Record(new Observation("talk", farmer, npc.Name, location.NameOrUniqueName, $"{farmer}和{npc.Name}聊了几句。", Game1.player?.Tile, d, Exclude: new[] { npc.Name }));
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到：记录聊天时出错：" + ex.Message);
		}
	}

	private void NoticeTone(NPC npc, GameLocation location, string playerLine, bool keepSecret, NpcReaction? reaction)
	{
		if (keepSecret || string.IsNullOrWhiteSpace(playerLine) || Game1.player is not Farmer player)
		{
			return;
		}
		string emotion = reaction?.Emotion?.Trim().ToLowerInvariant() ?? "";
		bool insult = Keywords.Any(playerLine, Keywords.SignalInsult);
		bool upset = emotion is "angry" or "annoyed" or "upset" or "frustrated" or "hurt" or "disappointed";
		string farmer = PlayerName;
		long now = NowMinute;
		if ((insult || (upset && reaction != null && reaction.FriendshipPoints <= 10)) && Pass("argue:" + npc.Name, now, 60))
		{
			Record(new Observation("argue", farmer, npc.Name, location.NameOrUniqueName, $"{farmer}当着大家的面和{npc.Name}吵了起来。", player.Tile,
				Observation.Map(("with", npc.Name), ("said", SightingText.Clip(playerLine, 80)), ("insult", insult ? "true" : "false"), ("emotion", emotion)), Exclude: new[] { npc.Name }));
		}
		if (!ChildSafety.IsChild(npc.Name, npc.Age) && Keywords.Any(playerLine, Keywords.SignalFlirt) && PartnerNearby(player, location, npc.Name, 10f) is string partner && Pass("flirt:" + npc.Name, now, 60))
		{
			Record(new Observation("flirt", farmer, npc.Name, location.NameOrUniqueName, $"{farmer}和{npc.Name}调情，而{partner}就在旁边。", player.Tile,
				Observation.Map(("with", npc.Name), ("partner", partner), ("owners", partner), ("aggravators", "flirtPartnerNearby")), Exclude: new[] { npc.Name }));
		}
	}

	private static string? PartnerNearby(Farmer player, GameLocation location, string except, float tiles)
	{
		foreach ((string name, Friendship f) in player.friendshipData.Pairs)
		{
			if (!Same(name, except) && Witnesses.IsPartner(f) && location.getCharacterFromName(name) is NPC n && Vector2.Distance(n.Tile, player.Tile) <= tiles)
			{
				return name;
			}
		}
		return null;
	}

	private static void SetTalkLines(Dictionary<string, string> d, string playerLine, string npcLine, bool secret)
	{
		if (secret)
		{
			d["secret"] = "true";
			d.Remove("said");
			d.Remove("replied");
			d.Remove("topic");
			return;
		}
		d["said"] = SightingText.Clip(playerLine, 80);
		d["replied"] = SightingText.Clip(npcLine, 80);
		if (!d.ContainsKey("topic"))
		{
			d["topic"] = SightingText.Clip(playerLine, 30);
		}
	}

	private static int LastTime(Sighting e) => int.TryParse(e.Detail("lastTime"), out int t) ? t : e.Clock;

	/// <summary>离开卧室：补上停留时间；待了 30 分钟以上分量加重，重新喂一次情绪。</summary>
	public void OnRoomLeft(string eventId, int minutes)
	{
		bool grew = false;
		Sighting? updated = Memory.Update(eventId, e =>
		{
			e.Facts["stayMinutes"] = minutes.ToString(CultureInfo.InvariantCulture);
			List<string> agg = SightingText.SplitList(e.Detail("aggravators"));
			if (minutes >= 30 && !agg.Contains("roomLongStay", StringComparer.OrdinalIgnoreCase) && double.TryParse(e.Detail("grade"), NumberStyles.Float, CultureInfo.InvariantCulture, out double grade))
			{
				grade *= Aggravators.Factors["roomLongStay"];
				agg.Add("roomLongStay");
				e.Facts["aggravators"] = string.Join(",", agg);
				e.Facts["grade"] = Invariant(grade);
				e.Weight = Aggravators.Significance(grade);
				grew = true;
			}
		});
		if (grew && updated != null && Enabled)
		{
			React(updated, new Dictionary<string, RuleReward>(), IsFreeToWalkOver, null);
		}
	}

	// ---- 调试 ----

	public List<string> DebugFire(Observation o)
	{
		List<string> report = new List<string>();
		if (!Enabled)
		{
			report.Add("看到听到在设置里关着，什么都没记。");
			return report;
		}
		if (!Context.IsWorldReady || !Context.IsMainPlayer)
		{
			report.Add("要在主机的存档里用。");
			return report;
		}
		Record(o, report);
		return report;
	}

	public string DescribeMemories(string npc)
	{
		IReadOnlyList<AwarenessMemoryItem> items = Memory.MemoriesOf(npc, Today);
		if (items.Count == 0)
		{
			return npc + " 还没记住什么事。";
		}
		StringBuilder sb = new StringBuilder(npc + " 记得：");
		foreach (AwarenessMemoryItem m in items)
		{
			sb.Append($"\n  [{m.Relevance:0.00}] ").Append(Writer.MemoryLine(m.Event, m.Witness, Today, Game1.timeOfDay));
		}
		return sb.ToString();
	}

	// ---- 小工具 ----

	private bool CanWalkOver(Observation o, string npc) => !IsMemoryOnly(o) && !SkipsSubject(o, npc) && IsFreeToWalkOver(npc);

	private bool IsFreeToWalkOver(string npc) => !(IsBusyElsewhere?.Invoke(npc) ?? false) && !_approaches.IsNpcInvolved(npc) && !_pending.Values.Any(p => Same(p.Npc, npc));

	private static bool UsesExplicitReaction(EventDefinition def, IReadOnlyDictionary<string, string> d)
	{
		if (!def.IsKnown || string.Equals(d.GetValueOrDefault("sh.explicitReaction"), "true", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return d.ContainsKey("trigger") && (!string.IsNullOrWhiteSpace(d.GetValueOrDefault("reactionDirective")) || !string.IsNullOrWhiteSpace(d.GetValueOrDefault("rewardItem")));
	}

	private static bool IsMemoryOnly(Observation o) => o.Type is "purchase" or "sleep_outside" || o.Flag("sh.memoryOnly");

	private static bool SkipsSubject(Observation o, string npc) => o.Type is "dating_proposal" or "marriage_proposal" && Same(o.Subject, npc);

	private string DisplayNames(Observation o, IReadOnlyDictionary<string, string> d, IEnumerable<Witness> witnesses)
	{
		IEnumerable<string> names = witnesses.Select(w => w.Who)
			.Concat(SightingText.SplitList(d.GetValueOrDefault("owners")))
			.Append(o.Subject).Append(d.GetValueOrDefault("partner")).Append(d.GetValueOrDefault("vendor")).Append(o.Actor)
			.Where(n => !string.IsNullOrWhiteSpace(n) && (Game1.characterData?.ContainsKey(n!) ?? false))
			.Select(n => n!)
			.Distinct(StringComparer.OrdinalIgnoreCase);
		return SightingText.EncodeNames(names.Select(n => new KeyValuePair<string, string>(n, _npcs.Names.Display(n))).Where(p => p.Key != p.Value));
	}

	private string DescribeNoWitnesses(GameLocation location, Vector2 tile, Observation o, EventDefinition def)
	{
		double range = Witnesses.Number(o, "sh.range", 1, 30) ?? (def.ReachTiles < 0 ? double.MaxValue : def.ReachTiles);
		string[] here = location.characters.Where(n => !string.IsNullOrWhiteSpace(n.Name)).Select(n => $"{n.Name} 在 {Vector2.Distance(n.Tile, tile):0.#} 格外{(_npcs.IsEnabled(n.Name) ? "" : "（没启用）")}").ToArray();
		string within = range == double.MaxValue ? "整张图" : $"{range:0.#} 格";
		return here.Length > 0 ? $"{within}内没有目击者。这里的人：{string.Join("；", here)}。" : $"没有目击者：{location.NameOrUniqueName} 里没有村民。";
	}

	private static GameLocation? ResolveLocation(string name)
	{
		GameLocation? here = Game1.currentLocation;
		if (here != null && (string.IsNullOrWhiteSpace(name) || Same(here.NameOrUniqueName, name) || Same(here.Name, name)))
		{
			return here;
		}
		return string.IsNullOrWhiteSpace(name) ? here : Game1.getLocationFromName(name);
	}

	public static string PlaceName(GameLocation location)
	{
		try
		{
			string display = location.DisplayName;
			return string.IsNullOrWhiteSpace(display) ? location.NameOrUniqueName : display;
		}
		catch (Exception)
		{
			return location.NameOrUniqueName;
		}
	}

	public static string PlayerName => string.IsNullOrWhiteSpace(Game1.player?.Name) ? "农夫" : Game1.player.Name;

	private static string Season => Core.Dialogue.HistoryFormatter.SeasonName(Game1.currentSeason ?? "spring");

	private static string Invariant(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

	private static int TimeoutTicks(string raw) => int.TryParse(raw, out int s) && s > 0 ? Math.Clamp(s, 1, 60) * 60 : 0;

	private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>把排队时的日期带进后台任务。</summary>
	private sealed record GameDateCapture(GameDate Date);
}
