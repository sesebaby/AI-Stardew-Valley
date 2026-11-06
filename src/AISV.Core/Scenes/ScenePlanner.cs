using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Scenes;

/// <summary>每天开始时挑今天的场景：条件、冷却、次数上限、概率，按权重抽，同一地点只抽一个。</summary>
public static class ScenePlanner
{
	/// <summary>
	/// 挑今天的场景，写进 <paramref name="state"/>。同一天已经挑过就照旧返回。
	/// <paramref name="deliverable"/>：今天演不演得出来（日程预测）。
	/// </summary>
	public static IReadOnlyList<string> Plan(IReadOnlyList<SceneDefinition> scenes, SceneState state, SceneDay day, int max, Func<SceneDefinition, bool>? deliverable = null)
	{
		Forget(state, scenes);
		if (string.Equals(state.DayKey, day.Key, StringComparison.OrdinalIgnoreCase))
		{
			return state.Loaded.ToList();
		}
		state.DayKey = day.Key;
		state.Loaded.Clear();
		state.PlayedToday.Clear();
		if (max <= 0)
		{
			return state.Loaded.ToList();
		}
		int today = Math.Max(1, day.TotalDays);
		Random random = new Random(Seed(day));
		List<SceneDefinition> eligible = scenes.Where(d => Eligible(d, state, day, today, random, deliverable)).ToList();
		foreach (SceneDefinition d in PickWeighted(eligible, max, random))
		{
			state.Loaded.Add(d.Id);
		}
		return state.Loaded.ToList();
	}

	public static bool Eligible(SceneDefinition d, SceneState state, SceneDay day, int today, Random random, Func<SceneDefinition, bool>? deliverable)
	{
		if (d.Repeats > 0 && state.TimesPlayed.TryGetValue(d.Id, out int seen) && seen >= d.Repeats)
		{
			return false;
		}
		if (state.RestingUntil.TryGetValue(d.Id, out int until) && until >= today)
		{
			return false;
		}
		SceneConditions c = d.Needs;
		if (c.InSeasons.Count > 0 && !c.InSeasons.Any(s => Same(s, day.Season)))
		{
			return false;
		}
		if (c.InWeather.Count > 0 && !c.InWeather.Any(w => Same(w, day.Weather)))
		{
			return false;
		}
		if (c.FromYear is int minYear && day.Year < minYear || c.UntilYear is int maxYear && day.Year > maxYear || c.AfterDays is int minDays && today < minDays)
		{
			return false;
		}
		if (c.HeartsAtLeast.Any(p => day.HeartsOf(p.Key) < p.Value) || c.HeartsAtMost.Any(p => p.Value > 0 && day.HeartsOf(p.Key) > p.Value))
		{
			return false;
		}
		if (deliverable != null && !deliverable(d))
		{
			return false;
		}
		return c.Luck >= 1.0 || random.NextDouble() <= c.Luck;
	}

	private static IEnumerable<SceneDefinition> PickWeighted(List<SceneDefinition> eligible, int max, Random random)
	{
		List<SceneDefinition> left = eligible.ToList();
		for (int n = 0; n < max && left.Count > 0; n++)
		{
			int total = left.Sum(d => Math.Max(1, d.Odds));
			int roll = random.Next(1, total + 1);
			int acc = 0;
			foreach (SceneDefinition d in left)
			{
				acc += Math.Max(1, d.Odds);
				if (roll <= acc)
				{
					yield return d;
					left.RemoveAll(x => Same(x.Place, d.Place));
					break;
				}
			}
		}
	}

	/// <summary>同一存档同一天的随机种子不变。</summary>
	public static int Seed(SceneDay day)
	{
		unchecked
		{
			int hash = -2128831035;
			foreach (char ch in $"{day.GameId}|{day.SaveFolder}|{day.Year}|{day.Season}|{day.Day}")
			{
				hash = (hash ^ ch) * 16777619;
			}
			return hash;
		}
	}

	/// <summary>存档里记着、目录里已经没有的场景（比如换过一批场景以后）直接丢掉。目录是空的时候不动。</summary>
	public static void Forget(SceneState state, IReadOnlyList<SceneDefinition> scenes)
	{
		if (scenes.Count == 0)
		{
			return;
		}
		HashSet<string> known = new HashSet<string>(scenes.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
		state.Loaded.RemoveAll(id => !known.Contains(id));
		state.PlayedToday.RemoveAll(id => !known.Contains(id));
		foreach (string id in state.TimesPlayed.Keys.Where(id => !known.Contains(id)).ToList())
		{
			state.TimesPlayed.Remove(id);
		}
		foreach (string id in state.RestingUntil.Keys.Where(id => !known.Contains(id)).ToList())
		{
			state.RestingUntil.Remove(id);
		}
	}

	/// <summary>读写前整理：去空、去重、负数变 0。</summary>
	public static void Tidy(SceneState state)
	{
		state.Loaded = Ids(state.Loaded);
		state.PlayedToday = Ids(state.PlayedToday);
		state.TimesPlayed = Counts(state.TimesPlayed);
		state.RestingUntil = Counts(state.RestingUntil);
	}

	private static List<string> Ids(List<string>? ids) => (ids ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	private static Dictionary<string, int> Counts(Dictionary<string, int>? counts)
	{
		Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach ((string key, int value) in counts ?? new Dictionary<string, int>())
		{
			if (!string.IsNullOrWhiteSpace(key))
			{
				result[key.Trim()] = Math.Max(0, value);
			}
		}
		return result;
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
