using System.Collections.Generic;
using AISV.Core.Storage;

namespace AISV.Core.Companions;

/// <summary>时钟换算：1430 ↔ 870 分钟。</summary>
public static class Clock
{
	public static int Minutes(int time) => time / 100 * 60 + time % 100;

	public static int FromMinutes(int minutes) => minutes / 60 * 100 + minutes % 60;

	public static int Add(int time, int minutes) => FromMinutes(Minutes(time) + minutes);

	public static string Text(int time) => $"{time / 100}:{time % 100:00}";
}

public static class OverlayStates
{
	public const string Planned = "planned";
	public const string Walking = "walking";
	public const string Arrived = "arrived";
	public const string Done = "done";
	public const string Cancelled = "cancelled";

	public static bool IsHolding(string state) => state is Walking or Arrived;

	public static bool IsOpen(string state) => state is Planned or Walking or Arrived;
}

/// <summary>排不上或结束的原因。写进存档，也用来决定怎么跟玩家说。</summary>
public static class PlanReasons
{
	public const string Off = "schedule rewriting is off";
	public const string HostOnly = "only the host can rewrite schedules";
	public const string NoVillager = "no such villager";
	public const string Child = "children keep their own schedule";
	public const string DayRange = "day must be today to 7 days ahead";
	public const string TimeRange = "time must be 700~2400 and end after start";
	public const string Passed = "that time has already passed today";
	public const string Festival = "festival day";
	public const string Birthday = "their birthday";
	public const string NoPlace = "no such place";
	public const string NoSpot = "no spot there they can walk to";
	public const string Overlap = "already has plans then";

	public const string Missed = "missed";
	public const string DayEnded = "day ended";
	public const string TooLate = "too late to go";
	public const string NotAround = "not around today";
	public const string WithFarmer = "out with the farmer";
	public const string HeartEvent = "heart event today";
	public const string TimeUp = "time's up";
	public const string ModuleOff = "module off";
	public const string LostTrack = "lost track of them";
	public const string NoWay = "couldn't find the way";
	public const string Error = "error";
	public const string Cleared = "cleared";

	public const string FarmerCancelled = "the farmer called it off";
	public const string RainMove = "moved indoors for rain";
	public const string Rain = "rain";
	public const string JobFinished = "job finished";
	public const string NothingToDo = "nothing to do";
	public const string Lost = "lost";
}

/// <summary>一段日程改写：谁、哪天、几点到几点、去哪。</summary>
public sealed class ScheduleOverlay
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	/// <summary>谁排的：meetup、farmhelp、spouse、debug。</summary>
	public string Owner { get; set; } = "";

	public int Day { get; set; }

	public int Start { get; set; }

	public int End { get; set; }

	public string Location { get; set; } = "";

	public int X { get; set; }

	public int Y { get; set; }

	public int Facing { get; set; } = 2;

	public string State { get; set; } = OverlayStates.Planned;

	public string CancelReason { get; set; } = "";

	public int DepartedAt { get; set; } = -1;

	public int ArrivedAt { get; set; } = -1;

	public int ReleasedAt { get; set; } = -1;

	/// <summary>walked 或 placed。</summary>
	public string ArrivedBy { get; set; } = "";

	public int CreatedDay { get; set; }
}

public sealed class ScheduleOverlayState
{
	public const string Path = "plans/schedule_changes.json";

	public static readonly DocumentType<ScheduleOverlayState> Doc = new DocumentType<ScheduleOverlayState>("日程改写", 1, () => new ScheduleOverlayState());

	public int NextNumber { get; set; } = 1;

	public List<ScheduleOverlay> Items { get; set; } = new List<ScheduleOverlay>();
}

/// <summary>日程改写的数值。</summary>
public static class OverlayRules
{
	public const int EarliestStart = 700;

	public const int LatestEnd = 2400;

	public const int MaxDaysAhead = 7;

	public const int ClosedKeepDays = 14;

	/// <summary>村民走 10 分钟大约 13 格。</summary>
	public const double TilesPerTenMinutes = 13.0;

	/// <summary>开始前多久估路程。</summary>
	public const int EstimateWindowMinutes = 240;

	public static string? CheckTimes(int today, int now, int day, int start, int end)
	{
		if (day < today || day > today + MaxDaysAhead)
		{
			return PlanReasons.DayRange;
		}
		if (start < EarliestStart || end > LatestEnd || end <= start || start % 100 >= 60 || end % 100 >= 60)
		{
			return PlanReasons.TimeRange;
		}
		return day == today && end <= now ? PlanReasons.Passed : null;
	}

	public static bool Overlaps(ScheduleOverlay o, int day, int start, int end) => OverlayStates.IsOpen(o.State) && o.Day == day && o.Start < end && start < o.End;

	/// <summary>按格数估路程（分钟）。</summary>
	public static int WalkMinutes(int tiles) => (int)System.Math.Ceiling(tiles / TilesPerTenMinutes) * 10;
}
