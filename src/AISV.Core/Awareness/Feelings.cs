using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Awareness;

public sealed record FeelingInput(string Npc, string EventId, string EventType, IReadOnlyDictionary<string, double> Weights, double Score, int Certainty, bool Silent, RuleReward? Reward);

/// <summary>某个计量升了档：可以走过来说。</summary>
public sealed record FeelingSignal(string Npc, string MeterId, int Tier, double Reading, double Intensity, double Priority, IReadOnlyList<string> EventIds, RuleReward? Reward);

/// <summary>放在心里、下次聊天时可能提起的事。</summary>
public sealed record FeelingAside(string Npc, string MeterId, int Tier, IReadOnlyList<string> EventIds);

public sealed record FeelingGrudge(string Npc, string MeterId, double Amount);

public sealed record FeelingReading(string MeterId, double Reading, int Tier, double[] Thresholds, int ActedTier, bool WalkingOver, bool CarriedIntoTomorrow, string? RewardItem);

/// <summary>
/// 情绪计量：每位村民每个计量累计看到的事，按半衰期衰减；读数过阈值就升档。
/// 每档每天只说一次，每个计量每人每天有上限；最强的火气和爱意按比例带到第二天。
/// 分钟 = 总天数×1440 + 当天分钟。线程安全。
/// </summary>
public sealed class Feelings
{
	public const int AsideFollowUpMinutes = 60;

	public const double MinimumGrudge = 0.5;

	private const double MinimumDelta = 0.05;

	private const int MaxReasonEvents = 3;

	private sealed record Contribution(string EventId, double Amount, int Minute);

	private sealed class State
	{
		public readonly List<Contribution> Contributions = new List<Contribution>();

		public readonly Dictionary<string, double> Counted = new Dictionary<string, double>(StringComparer.Ordinal);

		public int ActedTier;

		public int ActedCount;

		public int PendingTier;

		public double Grudge;

		public RuleReward? Reward;
	}

	private sealed class Aside
	{
		public Aside(FeelingAside value)
		{
			Value = value;
		}

		public FeelingAside Value { get; }

		public int? FirstUsed { get; set; }
	}

	private readonly object _gate = new object();

	private readonly Func<SightingCatalog> _catalog;

	private readonly Dictionary<string, Dictionary<string, State>> _state = new Dictionary<string, Dictionary<string, State>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Dictionary<string, string>> _typeLocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Aside> _asides = new Dictionary<string, Aside>(StringComparer.OrdinalIgnoreCase);

	private readonly List<FeelingGrudge> _carried = new List<FeelingGrudge>();

	public Feelings(Func<SightingCatalog> catalog)
	{
		_catalog = catalog;
	}

	/// <summary>
	/// 喂一件事。当天这个村民对这类事第一个升到 1 档的计量会"锁定"，之后同类事只喂它。
	/// 同一件事重复喂只算增量。Silent 时只累计不出信号。
	/// </summary>
	public IReadOnlyList<FeelingSignal> Feel(FeelingInput input, int nowMinute)
	{
		List<FeelingSignal> signals = new List<FeelingSignal>();
		if (string.IsNullOrWhiteSpace(input.Npc) || input.Weights.Count == 0 || input.Score <= 0)
		{
			return signals;
		}
		lock (_gate)
		{
			string? locked = TypeLock(input.Npc, input.EventType);
			foreach ((string meterId, double weight) in input.Weights.OrderByDescending(p => p.Value))
			{
				if (weight <= 0 || !_catalog().TryGetMeter(meterId, out MeterDefinition meter) || (locked != null && !string.Equals(locked, meterId, StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}
				State st = For(input.Npc, meterId);
				double total = input.Score * weight;
				double delta = total - st.Counted.GetValueOrDefault(input.EventId);
				if (delta < MinimumDelta)
				{
					continue;
				}
				st.Counted[input.EventId] = total;
				st.Contributions.Add(new Contribution(input.EventId, delta, nowMinute));
				if (input.Reward != null)
				{
					st.Reward = input.Reward;
				}
				double reading = Reading(st, meter, nowMinute);
				if (locked == null && meter.TierFor(reading) >= 1)
				{
					locked = meterId;
					SetTypeLock(input.Npc, input.EventType, meterId);
				}
				if (!input.Silent && Consider(input.Npc, meter, st, input.Certainty, reading, nowMinute) is FeelingSignal s)
				{
					signals.Add(s);
				}
			}
			return signals;
		}
	}

	public void MarkPending(FeelingSignal signal)
	{
		lock (_gate)
		{
			State st = For(signal.Npc, signal.MeterId);
			st.PendingTier = Math.Max(st.PendingTier, signal.Tier);
		}
	}

	/// <summary>说出口了：这一档算用掉；到最高档时把读数按比例记成隔天的火气；清掉累计。</summary>
	public void MarkSpoken(string npc, string meterId, int tier, int nowMinute)
	{
		lock (_gate)
		{
			if (!_catalog().TryGetMeter(meterId, out MeterDefinition meter))
			{
				return;
			}
			State st = For(npc, meterId);
			double reading = Reading(st, meter, nowMinute);
			if (tier > st.ActedTier)
			{
				st.ActedCount++;
			}
			st.ActedTier = Math.Max(st.ActedTier, tier);
			st.PendingTier = 0;
			st.Reward = null;
			if (tier >= meter.TierCount && meter.GrudgeLeft > 0)
			{
				st.Grudge = reading * meter.GrudgeLeft;
			}
			st.Contributions.Clear();
			if (_asides.TryGetValue(npc, out Aside? a) && string.Equals(a.Value.MeterId, meterId, StringComparison.OrdinalIgnoreCase) && a.Value.Tier <= tier)
			{
				_asides.Remove(npc);
			}
		}
	}

	/// <summary>没走过去：改成放在心里。</summary>
	public FeelingAside? ReleasePending(string npc, string meterId, int tier, int nowMinute)
	{
		lock (_gate)
		{
			if (!_catalog().TryGetMeter(meterId, out MeterDefinition meter))
			{
				return null;
			}
			State st = For(npc, meterId);
			st.PendingTier = 0;
			return MakeAside(npc, meter, st, tier, nowMinute);
		}
	}

	public FeelingAside? KeepAsAside(FeelingSignal signal, int nowMinute)
	{
		lock (_gate)
		{
			return _catalog().TryGetMeter(signal.MeterId, out MeterDefinition meter) ? MakeAside(signal.Npc, meter, For(signal.Npc, signal.MeterId), signal.Tier, nowMinute) : null;
		}
	}

	/// <summary>取心事：第一次取起 60 分钟内有效，之后丢掉。<paramref name="firstUse"/> 表示这次是第一次。</summary>
	public FeelingAside? TakeAside(string npc, int nowMinute, out bool firstUse)
	{
		lock (_gate)
		{
			firstUse = false;
			if (!_asides.TryGetValue(npc, out Aside? a))
			{
				return null;
			}
			if (a.FirstUsed == null)
			{
				a.FirstUsed = nowMinute;
				firstUse = true;
			}
			if (nowMinute - a.FirstUsed.Value > AsideFollowUpMinutes)
			{
				_asides.Remove(npc);
				firstUse = false;
				return null;
			}
			return a.Value;
		}
	}

	public FeelingAside? PeekAside(string npc, int nowMinute)
	{
		lock (_gate)
		{
			if (!_asides.TryGetValue(npc, out Aside? a))
			{
				return null;
			}
			return a.FirstUsed is int used && nowMinute - used > AsideFollowUpMinutes ? null : a.Value;
		}
	}

	public IReadOnlyList<FeelingReading> Readings(string npc, int nowMinute)
	{
		lock (_gate)
		{
			if (!_state.TryGetValue(npc, out Dictionary<string, State>? meters))
			{
				return Array.Empty<FeelingReading>();
			}
			List<FeelingReading> list = new List<FeelingReading>();
			foreach ((string id, State st) in meters)
			{
				if (!_catalog().TryGetMeter(id, out MeterDefinition meter))
				{
					continue;
				}
				double r = Reading(st, meter, nowMinute);
				if (r >= MinimumDelta || st.ActedTier != 0 || st.PendingTier != 0 || st.Grudge >= MinimumGrudge)
				{
					list.Add(new FeelingReading(id, r, meter.TierFor(r), meter.Steps.ToArray(), st.ActedTier, st.PendingTier > 0, st.Grudge >= MinimumGrudge, string.IsNullOrWhiteSpace(st.Reward?.Item) ? null : st.Reward!.Item));
				}
			}
			return list.OrderByDescending(r => r.Reading).ToArray();
		}
	}

	public double ReadingOf(string npc, string meterId, int nowMinute)
	{
		lock (_gate)
		{
			return _state.TryGetValue(npc, out Dictionary<string, State>? m) && m.TryGetValue(meterId, out State? st) && _catalog().TryGetMeter(meterId, out MeterDefinition meter) ? Reading(st, meter, nowMinute) : 0;
		}
	}

	/// <summary>新的一天：火气 ≥0.5 的留下（同人同计量取最大），其余全清，再把火气放回去当作今天的起点。</summary>
	public void OnDayStarted(int nowMinute, string dayId)
	{
		lock (_gate)
		{
			List<FeelingGrudge> keep = AllGrudges()
				.Where(g => g.Amount >= MinimumGrudge)
				.GroupBy(g => (g.Npc.ToLowerInvariant(), g.MeterId.ToLowerInvariant()))
				.Select(g => g.OrderByDescending(x => x.Amount).First())
				.ToList();
			Reset();
			foreach (FeelingGrudge g in keep)
			{
				if (_catalog().TryGetMeter(g.MeterId, out _))
				{
					For(g.Npc, g.MeterId).Contributions.Add(new Contribution("grudge:" + dayId, g.Amount, nowMinute));
				}
			}
		}
	}

	public IReadOnlyList<FeelingGrudge> Grudges()
	{
		lock (_gate)
		{
			return AllGrudges().Where(g => g.Amount >= MinimumGrudge).ToArray();
		}
	}

	public void Load(IEnumerable<FeelingGrudge> grudges)
	{
		lock (_gate)
		{
			Reset();
			_carried.AddRange(grudges.Where(g => g.Amount >= MinimumGrudge && !string.IsNullOrWhiteSpace(g.Npc)));
		}
	}

	public void Clear()
	{
		lock (_gate)
		{
			Reset();
		}
	}

	/// <summary>调试用的说明。</summary>
	public string Describe(string? npc, int nowMinute)
	{
		lock (_gate)
		{
			StringBuilder sb = new StringBuilder();
			foreach ((string name, Dictionary<string, State> meters) in _state.Where(p => npc == null || string.Equals(p.Key, npc, StringComparison.OrdinalIgnoreCase)))
			{
				sb.Append(name).Append(':');
				foreach ((string id, State st) in meters)
				{
					if (_catalog().TryGetMeter(id, out MeterDefinition meter))
					{
						double r = Reading(st, meter, nowMinute);
						sb.Append($"\n  {id} {r:0.0}/{string.Join("/", meter.Steps)} 档{meter.TierFor(r)} 已说{st.ActedTier}（{st.ActedCount}次）{(st.PendingTier > 0 ? " 正走过来" : "")}{(st.Grudge >= MinimumGrudge ? $" 带到明天{st.Grudge:0.0}" : "")}");
					}
				}
				if (_typeLocks.TryGetValue(name, out Dictionary<string, string>? locks) && locks.Count > 0)
				{
					sb.Append("\n  锁定：").Append(string.Join("，", locks.Select(p => p.Key + "→" + p.Value)));
				}
				if (_asides.TryGetValue(name, out Aside? a))
				{
					sb.Append($"\n  心事：{a.Value.MeterId} 档{a.Value.Tier}");
				}
				sb.Append('\n');
			}
			return sb.Length == 0 ? "今天还没有谁对什么事有感觉。" : sb.ToString().TrimEnd();
		}
	}

	public static string Reason(MeterDefinition meter, int tier, string farmer, IEnumerable<string> memoryLines, bool approached)
	{
		Func<string, string?> r = Resolver(meter, tier, farmer);
		StringBuilder sb = new StringBuilder();
		string header = PhraseTemplate.Parse(meter.Heading).Render(r);
		if (header.Length > 0)
		{
			sb.AppendLine(header);
		}
		string opening = PhraseTemplate.Parse(approached ? meter.OpeningWalkUp : meter.Intro).Render(r);
		if (opening.Length > 0)
		{
			sb.AppendLine(opening);
		}
		foreach (string line in memoryLines)
		{
			sb.AppendLine(line);
		}
		sb.Append(PhraseTemplate.Parse(meter.Sendoff).Render(r));
		return sb.ToString().Trim();
	}

	public static string ReactionFeeling(MeterDefinition meter, int tier, string farmer, IReadOnlyList<string> earlierLines)
	{
		string tone = meter.ToneFor(tier);
		StringBuilder sb = new StringBuilder();
		if (tone.Length > 0 && !meter.Sendoff.Contains("{tone}", StringComparison.Ordinal))
		{
			sb.Append("此刻你").Append(tone).Append('。');
		}
		if (earlierLines.Count > 0)
		{
			sb.Append("\n今天你已经不止一次碰上这种事，先前那几回是：");
			foreach (string line in earlierLines)
			{
				sb.Append('\n').Append(line);
			}
		}
		string closing = PhraseTemplate.Parse(meter.Sendoff).Render(Resolver(meter, tier, farmer));
		if (closing.Length > 0)
		{
			sb.Append('\n').Append(closing);
		}
		return sb.ToString().Trim();
	}

	public static string AsideBlock(MeterDefinition meter, int tier, IEnumerable<string> memoryLines)
	{
		StringBuilder sb = new StringBuilder("【憋在心里、还没开口的事】\n");
		foreach (string line in memoryLines)
		{
			sb.AppendLine(line);
		}
		sb.Append("你现在").Append(meter.ToneFor(tier)).Append("。要是话赶话正好说到这儿，就照你的口气、按你心里有几分把握把它讲出来；话题对不上就别生拉硬扯，你这人本来就不爱提的话，憋着不说也行。");
		return sb.ToString();
	}

	private static Func<string, string?> Resolver(MeterDefinition meter, int tier, string farmer)
	{
		string tone = meter.ToneFor(tier);
		return token => token switch
		{
			"farmer" => farmer,
			"tone" => tone,
			"feeling" => meter.Label,
			_ => null
		};
	}

	private FeelingSignal? Consider(string npc, MeterDefinition meter, State st, int certainty, double reading, int nowMinute)
	{
		int tier = meter.TierFor(reading);
		bool fresh = tier > st.ActedTier && tier > st.PendingTier && st.ActedCount < meter.DailyCap;
		if (fresh && !meter.IsAside && certainty >= meter.MinCertainty)
		{
			return new FeelingSignal(npc, meter.Id, tier, reading, meter.Intensity(reading, tier), meter.Rank, Ranked(st, meter, nowMinute), st.Reward);
		}
		if (fresh)
		{
			MakeAside(npc, meter, st, tier, nowMinute);
		}
		return null;
	}

	private FeelingAside? MakeAside(string npc, MeterDefinition meter, State st, int tier, int nowMinute)
	{
		IReadOnlyList<string> reasons = Ranked(st, meter, nowMinute);
		if (!reasons.Any())
		{
			return null;
		}
		st.ActedCount += tier > st.ActedTier ? 1 : 0;
		st.ActedTier = Math.Max(st.ActedTier, tier);
		_asides[npc] = new Aside(new FeelingAside(npc, meter.Id, tier, reasons));
		return _asides[npc].Value;
	}

	private static IReadOnlyList<string> Ranked(State st, MeterDefinition meter, int nowMinute)
	{
		return st.Contributions
			.Where(c => !c.EventId.StartsWith("grudge:", StringComparison.Ordinal))
			.GroupBy(c => c.EventId)
			.Select(g => (Id: g.Key, Weight: g.Sum(c => Decay(c, meter, nowMinute))))
			.OrderByDescending(x => x.Weight)
			.Take(MaxReasonEvents)
			.Select(x => x.Id)
			.ToList();
	}

	private static double Reading(State st, MeterDefinition meter, int nowMinute) => st.Contributions.Sum(c => Decay(c, meter, nowMinute));

	private static double Decay(Contribution c, MeterDefinition meter, int nowMinute) =>
		c.Amount * Math.Pow(0.5, Math.Max(0, nowMinute - c.Minute) / (Math.Max(0.1, meter.FadeHours) * 60.0));

	private IEnumerable<FeelingGrudge> AllGrudges() => _state
		.SelectMany(n => n.Value.Select(m => new FeelingGrudge(n.Key, m.Key, m.Value.Grudge)))
		.Concat(_carried);

	private State For(string npc, string meterId)
	{
		if (!_state.TryGetValue(npc, out Dictionary<string, State>? meters))
		{
			meters = _state[npc] = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);
		}
		if (!meters.TryGetValue(meterId, out State? st))
		{
			st = meters[meterId] = new State();
		}
		return st;
	}

	private string? TypeLock(string npc, string type) => _typeLocks.TryGetValue(npc, out Dictionary<string, string>? m) && m.TryGetValue(type, out string? v) ? v : null;

	private void SetTypeLock(string npc, string type, string meterId)
	{
		if (!_typeLocks.TryGetValue(npc, out Dictionary<string, string>? m))
		{
			m = _typeLocks[npc] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
		m[type] = meterId;
	}

	private void Reset()
	{
		_state.Clear();
		_typeLocks.Clear();
		_asides.Clear();
		_carried.Clear();
	}
}

/// <summary>说出口后的好感、心情变化，和被冷落。</summary>
public static class FeelingConsequences
{
	public const int ColdShoulderWindowMinutes = 60;

	public static int FriendshipDelta(string? meterId, int tier, bool child)
	{
		if (string.IsNullOrWhiteSpace(meterId) || tier <= 0)
		{
			return 0;
		}
		string m = meterId.ToLowerInvariant();
		if (m == "confront")
		{
			return tier switch { 1 => -6, 2 => -18, _ => -40 };
		}
		if (m == "love" && child)
		{
			return 0;
		}
		if (m is "tenderness" or "love")
		{
			return tier switch { 1 => 4, 2 => 12, _ => 30 };
		}
		return m == "humor" ? tier switch { 2 => 3, >= 3 => 8, _ => 0 } : 0;
	}

	public static int MoodDelta(string? meterId, int tier, bool child)
	{
		if (string.IsNullOrWhiteSpace(meterId) || tier < 2)
		{
			return 0;
		}
		string m = meterId.ToLowerInvariant();
		if (m == "confront")
		{
			return tier == 2 ? -12 : -30;
		}
		if (m == "love" && child)
		{
			return 0;
		}
		return m is "tenderness" or "love" ? (tier == 2 ? 10 : 18) : 0;
	}

	public static bool EarnsColdShoulder(string? meterId, int tier, int tierCount) =>
		string.Equals(meterId, "confront", StringComparison.OrdinalIgnoreCase) && tier >= Math.Max(1, tierCount);

	public static bool ShouldColdShoulder(int? deliveredMinute, int nowMinute, bool used) =>
		deliveredMinute is int d && !used && nowMinute >= d && nowMinute - d <= ColdShoulderWindowMinutes;

	/// <summary>1～3。</summary>
	public static int ColdShoulderLine(double roll) => (int)Math.Floor(Math.Clamp(roll, 0, 0.999999) * 3) + 1;
}
