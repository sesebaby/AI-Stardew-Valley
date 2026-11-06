using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AISV.Core.Awareness;

/// <summary>规则求值时这位目击者所处的情况。</summary>
public sealed class RuleSituation
{
	public string Witness { get; init; } = "";

	public string Role { get; init; } = "bystander";

	public int Certainty { get; init; }

	public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();

	public string Weather { get; init; } = "";

	public string Season { get; init; } = "";

	public string Weekday { get; init; } = "";

	public string Location { get; init; } = "";

	public bool Outdoors { get; init; }

	public int Time { get; init; }

	public int Hearts { get; init; }

	public bool IsPlayerPartner { get; init; }

	/// <summary>(年龄 adult/teen/child, 格数) → 附近这个年龄的村民（不含目击者自己）。</summary>
	public Func<string, int, IReadOnlyList<string>> NearbyOfAge { get; init; } = (_, _) => Array.Empty<string>();
}

public sealed record RuleOutcome(Dictionary<string, double> Meters, List<string> MatchedRules, bool Silent, RuleReward? Reward, Dictionary<string, string> AddedDetails);

public static class SightingRules
{
	public static RuleOutcome Evaluate(EventDefinition def, IReadOnlyList<EventRule> global, RuleSituation s)
	{
		Dictionary<string, double> meters = def.Meters
			.Where(m => m.Value.Weight > 0 && m.Value.AppliesTo(s.Role) && (string.IsNullOrWhiteSpace(m.Value.RequiresPayload) || s.Details.ContainsKey(m.Value.RequiresPayload)))
			.ToDictionary(m => m.Key, m => m.Value.Weight, StringComparer.OrdinalIgnoreCase);
		void Assign(string id, double value)
		{
			if (value > 0)
			{
				meters[id] = value;
			}
			else
			{
				meters.Remove(id);
			}
		}
		Dictionary<string, string> added = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<EventRule> hits = def.Rules.Concat(global).Where(rule => Matches(rule.When, s, added)).ToList();
		foreach (EventRule rule in hits)
		{
			foreach ((string id, double value) in rule.Set)
			{
				Assign(id, value);
			}
			foreach ((string id, double factor) in rule.Multiply.Where(p => meters.ContainsKey(p.Key)))
			{
				Assign(id, meters[id] * factor);
			}
		}
		List<string> matched = hits.Select(r => r.Name).ToList();
		bool silent = hits.Any(r => r.Silent);
		RuleReward? reward = hits.FirstOrDefault(r => r.Reward != null)?.Reward;
		return new RuleOutcome(meters, matched, silent, reward, added);
	}

	private static bool Matches(RuleConditions w, RuleSituation s, Dictionary<string, string> added)
	{
		if (!In(w.Witness, s.Witness) || (w.NotWitness != null && w.NotWitness.Contains(s.Witness, StringComparer.OrdinalIgnoreCase)) || !In(w.Role, s.Role))
		{
			return false;
		}
		if (w.MinCertainty.HasValue && s.Certainty < w.MinCertainty.Value)
		{
			return false;
		}
		if (w.PlayerPartner.HasValue && w.PlayerPartner.Value != s.IsPlayerPartner)
		{
			return false;
		}
		if (w.Payload != null)
		{
			foreach ((string key, string expected) in w.Payload)
			{
				if (!s.Details.TryGetValue(key, out string? actual))
				{
					return false;
				}
				if (!string.IsNullOrEmpty(expected) && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
		}
		if (!In(w.Weather, s.Weather) || !In(w.Season, s.Season) || !In(w.Weekday, s.Weekday) || !In(w.Location, s.Location))
		{
			return false;
		}
		if (w.Outdoors.HasValue && w.Outdoors.Value != s.Outdoors)
		{
			return false;
		}
		if ((w.TimeFrom.HasValue && s.Time < w.TimeFrom.Value) || (w.TimeTo.HasValue && s.Time > w.TimeTo.Value))
		{
			return false;
		}
		if ((w.MinHearts.HasValue && s.Hearts < w.MinHearts.Value) || (w.MaxHearts.HasValue && s.Hearts > w.MaxHearts.Value))
		{
			return false;
		}
		if (w.NearbyAge != null)
		{
			string age = string.IsNullOrWhiteSpace(w.NearbyAge.Age) ? "child" : w.NearbyAge.Age.Trim().ToLowerInvariant();
			IReadOnlyList<string> near = s.NearbyOfAge(age, w.NearbyAge.Tiles <= 0 ? 8 : w.NearbyAge.Tiles);
			if ((near.Count > 0) != w.NearbyAge.Present)
			{
				return false;
			}
			if (near.Count > 0)
			{
				added["nearby" + age + "Count"] = near.Count.ToString(CultureInfo.InvariantCulture);
				added["nearby" + age + "Names"] = string.Join(",", near);
			}
		}
		return true;
	}

	private static bool In(List<string>? allowed, string value) => allowed == null || allowed.Count == 0 || allowed.Contains(value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>加重因素：乘到事件的分量上。</summary>
public static class Aggravators
{
	public static readonly IReadOnlyDictionary<string, double> Factors = new (double Factor, string Keys)[]
	{
		(1.2, "lateNight drinkOutsideSaloon"),
		(1.25, "sitOwnerPresent"),
		(1.3, "placeNpcHome roomLongStay"),
		(1.4, "giftLovedOrHated lockedRepeated"),
		(1.5, "roomAtNight giftPartnerNearby"),
		(1.7, "giftBirthday"),
		(1.8, "snoopOwnerPresent"),
		(2.0, "roomOwnerPresent flirtPartnerNearby")
	}.SelectMany(g => g.Keys.Split(' ').Select(k => (k, g.Factor))).ToDictionary(p => p.k, p => p.Factor, StringComparer.OrdinalIgnoreCase);

	public static void Add(IDictionary<string, string> details, string key)
	{
		List<string> list = SightingText.SplitList(details.TryGetValue("aggravators", out string? v) ? v : null);
		if (!list.Contains(key, StringComparer.OrdinalIgnoreCase))
		{
			list.Add(key);
		}
		details["aggravators"] = string.Join(",", list);
	}

	/// <summary>22:00 以后自动加 lateNight，再把所有加重因素乘上去。</summary>
	public static double Grade(EventDefinition def, IDictionary<string, string> details, int timeOfDay)
	{
		if (timeOfDay >= 2200)
		{
			Add(details, "lateNight");
		}
		double g = def.Severity;
		foreach (string a in SightingText.SplitList(details.TryGetValue("aggravators", out string? v) ? v : null))
		{
			if (Factors.TryGetValue(a, out double f))
			{
				g *= f;
			}
		}
		return g;
	}

	public static double Significance(double grade) => Math.Clamp(grade / 11.0, 0.1, 1.0);
}
