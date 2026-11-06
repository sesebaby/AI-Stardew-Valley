using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Scenes;
using StardewValley;
using StardewValley.Pathfinding;

namespace AISV.Game.Scenes;

/// <summary>读村民今天的日程：各站几点去哪。</summary>
internal static class ScheduleReader
{
	/// <summary>找没找到这个人、日程读不读得出、现在在哪、各站。没有日程算读得出、没有站。</summary>
	public static (bool Found, bool Readable, string? Now, IReadOnlyList<ScheduleStop> Stops) Read(string name)
	{
		NPC? npc;
		try
		{
			npc = string.IsNullOrWhiteSpace(name) ? null : Game1.getCharacterFromName(name.Trim());
		}
		catch
		{
			npc = null;
		}
		if (npc == null || npc.IsInvisible)
		{
			return (false, false, null, Array.Empty<ScheduleStop>());
		}
		string? now = npc.currentLocation?.Name;
		try
		{
			Dictionary<int, SchedulePathDescription>? schedule = npc.Schedule;
			if (schedule == null)
			{
				return (true, true, now, Array.Empty<ScheduleStop>());
			}
			List<ScheduleStop> stops = new List<ScheduleStop>();
			foreach ((int time, SchedulePathDescription path) in schedule)
			{
				if (string.IsNullOrWhiteSpace(path?.targetLocationName))
				{
					return (true, false, now, Array.Empty<ScheduleStop>());
				}
				stops.Add(new ScheduleStop(time, path.targetLocationName.Trim()));
			}
			return (true, true, now, stops.OrderBy(s => s.Time).ToList());
		}
		catch
		{
			return (true, false, now, Array.Empty<ScheduleStop>());
		}
	}
}
