using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Awareness;

public sealed record AwarenessMemoryItem(Sighting Event, Witness Witness, double Relevance, int AgeInDays);

/// <summary>
/// 村民看到听到的事（bonds/witnessed.json）：记录、遗忘、按村民取记忆、挑当天的谈资。
/// 有半衰期的事最多记 56 天，没有的 7 天，新近度低于 0.05 就忘；超过 400 条时按"分量×新近度"删最轻的。
/// </summary>
public sealed class AwarenessMemory
{
	public const string Path = "bonds/witnessed.json";

	public const int MaxEvents = 400;

	public const int MemoryDays = 7;

	public const int MaxMemoryDays = 56;

	public const int MaxContextMemories = 8;

	public const double MinimumConfidence = 0.2;

	public const double DailyDecay = 0.72;

	public const double ForgetBelowRecency = 0.05;

	private static readonly DocumentType<AwarenessData> Doc = new DocumentType<AwarenessData>("看到听到", 1, () => new AwarenessData());

	private readonly Func<DocumentStore?> _store;

	private readonly object _gate = new object();

	private AwarenessData? _loaded;

	private DocumentStore? _loadedFrom;

	public AwarenessMemory(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<Sighting> Events
	{
		get
		{
			lock (_gate)
			{
				return Data()?.Events.ToArray() ?? Array.Empty<Sighting>();
			}
		}
	}

	public IReadOnlyList<GrudgeData> Grudges
	{
		get
		{
			lock (_gate)
			{
				return Data()?.Grudges.ToArray() ?? Array.Empty<GrudgeData>();
			}
		}
	}

	public void SetGrudges(IEnumerable<FeelingGrudge> grudges)
	{
		lock (_gate)
		{
			AwarenessData? d = Data();
			if (d == null)
			{
				return;
			}
			d.Grudges = grudges.Select(g => new GrudgeData { NpcName = g.Npc, MeterId = g.MeterId, Amount = g.Amount }).ToList();
			Dirty();
		}
	}

	/// <summary>记一件事：去掉确信度太低的、同名取最高的；没人看见或已经记过就不记。</summary>
	public bool Record(Sighting e, IEnumerable<Witness> witnesses)
	{
		lock (_gate)
		{
			AwarenessData? d = Data();
			if (d == null || string.IsNullOrWhiteSpace(e.EventId) || d.Events.Any(x => x.EventId == e.EventId))
			{
				return false;
			}
			List<Witness> kept = witnesses
				.Where(w => !string.IsNullOrWhiteSpace(w.Who) && w.Confidence >= MinimumConfidence)
				.GroupBy(w => w.Who, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderByDescending(w => w.Confidence).First())
				.ToList();
			if (kept.Count == 0)
			{
				return false;
			}
			e.SeenBy = kept;
			d.Events.Add(e);
			d.Events.RemoveAll(x => !Retained(x, e.OnDay));
			Trim(d, e.OnDay);
			Dirty();
			return true;
		}
	}

	/// <summary>改一件已经记下的事（比如补上停留时间、对话轮数）。</summary>
	public Sighting? Update(string eventId, Action<Sighting> change)
	{
		lock (_gate)
		{
			Sighting? e = Data()?.Events.FirstOrDefault(x => x.EventId == eventId);
			if (e == null)
			{
				return null;
			}
			change(e);
			Dirty();
			return e;
		}
	}

	public void OnDayStarted(int day) => Forget(day);

	public void OnDayEnding(int day) => Forget(day);

	/// <summary>这位村民还记得的事，按相关度取前 8 条。</summary>
	public IReadOnlyList<AwarenessMemoryItem> MemoriesOf(string npc, int day, int limit = MaxContextMemories)
	{
		lock (_gate)
		{
			AwarenessData? d = Data();
			if (d == null)
			{
				return Array.Empty<AwarenessMemoryItem>();
			}
			return d.Events
				.Where(e => Retained(e, day))
				.Select(e => (Event: e, Witness: e.WitnessNamed(npc)))
				.Where(x => x.Witness != null)
				.Select(x => new AwarenessMemoryItem(x.Event, x.Witness!, Relevance(x.Event, x.Witness!, day), Math.Max(0, day - x.Event.OnDay)))
				.Where(m => m.Relevance > 0)
				.OrderByDescending(m => m.Relevance)
				.ThenByDescending(m => m.Event.OnDay)
				.ThenByDescending(m => m.Event.Clock)
				.Take(limit)
				.ToList();
		}
	}

	public static double Recency(Sighting e, int day)
	{
		int age = Math.Max(0, day - e.OnDay);
		double half = e.DetailNumber("halfLifeDays");
		return half > 0 ? Math.Pow(0.5, age / half) : Math.Pow(DailyDecay, age);
	}

	public static double Relevance(Sighting e, Witness w, int day) => Math.Clamp(e.Weight, 0, 1) * Recency(e, day) * w.Confidence;

	public static double GossipScore(Sighting e, int day)
	{
		double best = e.SeenBy.Select(w => Relevance(e, w, day)).DefaultIfEmpty(0).Max();
		return Math.Clamp(e.Weight, 0, 1) * 0.65 + best * 0.35;
	}

	/// <summary>当天最值得传的几件事：先每类取最好的一件，不够再按分数补。</summary>
	public IReadOnlyList<Sighting> GossipOfTheDay(int day, int count, IReadOnlyCollection<string> notGossip)
	{
		List<(Sighting Event, double Score)> all = Events
			.Where(e => e.OnDay == day && e.SeenBy.Count > 0 && !notGossip.Contains(e.Type))
			.Select(e => (Event: e, Score: GossipScore(e, day)))
			.OrderByDescending(x => x.Score)
			.ToList();
		List<(Sighting Event, double Score)> top = all
			.GroupBy(x => x.Event.Type)
			.Select(g => g.First())
			.OrderByDescending(x => x.Score)
			.Take(count)
			.ToList();
		top.AddRange(all.Where(x => !top.Contains(x)).Take(Math.Max(0, count - top.Count)));
		return top.Select(x => x.Event).ToList();
	}

	/// <summary>换存档或回标题时丢掉缓存。</summary>
	public void Reset()
	{
		lock (_gate)
		{
			_loaded = null;
			_loadedFrom = null;
		}
	}

	private static bool Retained(Sighting e, int day)
	{
		int age = Math.Max(0, day - e.OnDay);
		int maxDays = e.DetailNumber("halfLifeDays") > 0 ? MaxMemoryDays : MemoryDays;
		return age <= maxDays && Recency(e, day) >= ForgetBelowRecency;
	}

	private void Forget(int day)
	{
		lock (_gate)
		{
			AwarenessData? d = Data();
			if (d == null)
			{
				return;
			}
			int before = d.Events.Count;
			d.Events.RemoveAll(e => !Retained(e, day));
			Trim(d, day);
			if (d.Events.Count != before)
			{
				Dirty();
			}
		}
	}

	private static void Trim(AwarenessData d, int day)
	{
		if (d.Events.Count <= MaxEvents)
		{
			return;
		}
		HashSet<Sighting> drop = d.Events
			.Select((e, i) => (Event: e, Index: i))
			.OrderBy(x => x.Event.Weight * Recency(x.Event, day))
			.ThenBy(x => x.Index)
			.Take(d.Events.Count - MaxEvents)
			.Select(x => x.Event)
			.ToHashSet();
		d.Events.RemoveAll(drop.Contains);
	}

	private AwarenessData? Data()
	{
		DocumentStore? s = _store();
		if (s == null)
		{
			return null;
		}
		if (_loaded == null || !ReferenceEquals(_loadedFrom, s))
		{
			_loaded = s.Get(Path, Doc);
			_loadedFrom = s;
			Normalize(_loaded);
		}
		return _loaded;
	}

	private static void Normalize(AwarenessData d)
	{
		d.Events ??= new List<Sighting>();
		d.Grudges ??= new List<GrudgeData>();
		d.Events.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.EventId));
		foreach (Sighting e in d.Events)
		{
			e.Facts = new Dictionary<string, string>(e.Facts ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
			e.SeenBy = (e.SeenBy ?? new List<Witness>()).Where(w => w != null).ToList();
			foreach (Witness w in e.SeenBy)
			{
				w.Gauges = new Dictionary<string, double>(w.Gauges ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);
				w.FiredRules ??= new List<string>();
				w.Part ??= "";
				w.Repeats = Math.Max(1, w.Repeats);
				w.Sureness = Math.Clamp(w.Sureness, 0, 5);
			}
		}
	}

	private void Dirty() => _store()?.MarkDirty(Path);
}
