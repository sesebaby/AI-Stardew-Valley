using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Apps;

public enum WhereState
{
	Home,
	Out,
	Walking,
	Hidden
}

public enum HomeReturn
{
	Unknown,
	StaysHome,
	OnTheWay,
	At,
	EndOfDay
}

public sealed record WhereStop(int Time, string Place, string Name);

public sealed record WhereInput(string Npc, string Name, string? Place, string PlaceName, string? Home, string? Heading, string HeadingName, bool Hidden, bool HasRoute, IReadOnlyList<WhereStop> Stops);

public sealed record WherePlan(WhereInput Who, WhereState State, WhereStop? Next, HomeReturn Home, int HomeTime, int CurrentStop);

public sealed record WhereGroup(string? Place, string Name, IReadOnlyList<WherePlan> People);

/// <summary>大家都在哪：按现在的位置和今天的路线，算出每位村民的状态、下一站和几点到家。</summary>
public static class Whereabouts
{
	public const int SoonMinutes = 30;

	public static WherePlan Plan(WhereInput who, int now)
	{
		List<WhereStop> stops = who.Stops.OrderBy(s => s.Time).ToList();
		int current = stops.FindLastIndex(s => s.Time <= now);
		WhereState state = who.Hidden ? WhereState.Hidden
			: who.Heading != null && !Same(who.Heading, who.Place) ? WhereState.Walking
			: who.Place != null && Same(who.Place, who.Home) ? WhereState.Home
			: WhereState.Out;
		if (state == WhereState.Hidden || !who.HasRoute)
		{
			return new WherePlan(who, state, null, HomeReturn.Unknown, 0, current);
		}
		List<WhereStop> ahead = stops.Where(s => s.Time > now).ToList();
		(HomeReturn home, int time) = WhenHome(who, state, ahead);
		return new WherePlan(who, state, ahead.FirstOrDefault(), home, time, current);
	}

	private static (HomeReturn, int) WhenHome(WhereInput who, WhereState state, List<WhereStop> ahead)
	{
		bool headingHome = state == WhereState.Walking && Same(who.Heading, who.Home);
		if (ahead.Count == 0)
		{
			return state == WhereState.Home ? (HomeReturn.StaysHome, 0) : headingHome ? (HomeReturn.OnTheWay, 0) : (HomeReturn.EndOfDay, 0);
		}
		if (state == WhereState.Home && ahead.All(s => Same(s.Place, who.Home)))
		{
			return (HomeReturn.StaysHome, 0);
		}
		if (!Same(ahead[^1].Place, who.Home))
		{
			return (HomeReturn.EndOfDay, 0);
		}
		int first = ahead.Count - 1;
		while (first > 0 && Same(ahead[first - 1].Place, who.Home))
		{
			first--;
		}
		return first == 0 && headingHome ? (HomeReturn.OnTheWay, 0) : (HomeReturn.At, ahead[first].Time);
	}

	/// <summary>按地点分组：人多的地点在前，看不到的放最后。</summary>
	public static IReadOnlyList<WhereGroup> Group(IEnumerable<WherePlan> plans, string hiddenName)
	{
		StringComparer byName = StringComparer.CurrentCultureIgnoreCase;
		return plans.GroupBy(p => p.State == WhereState.Hidden ? "" : p.Who.Place ?? "", StringComparer.OrdinalIgnoreCase)
			.Select(g =>
			{
				List<WherePlan> people = g.OrderBy(p => p.Who.Name, byName).ToList();
				return g.Key.Length == 0 ? new WhereGroup(null, hiddenName, people) : new WhereGroup(g.Key, people[0].Who.PlaceName, people);
			})
			.OrderBy(g => g.Place == null ? 1 : 0).ThenByDescending(g => g.People.Count).ThenBy(g => g.Name, byName).ToList();
	}

	public static bool IsSoon(int now, int time)
	{
		int minutes = Minutes(time) - Minutes(now);
		return minutes > 0 && minutes <= SoonMinutes;
	}

	private static int Minutes(int time) => time / 100 * 60 + time % 100;

	private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
