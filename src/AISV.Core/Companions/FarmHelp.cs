using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Companions;

public static class FarmHelpStates
{
	public const string Scheduled = "scheduled";
	public const string Working = "working";
	public const string Done = "done";
	public const string NothingToDo = "nothing";
	public const string CalledOff = "called_off";
	public const string CancelledByPlayer = "cancelled";

	public static bool IsOpen(string s) => s is Scheduled or Working;
}

public sealed class FarmHelpJob
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	public string Task { get; set; } = "";

	public int Day { get; set; }

	public int Start { get; set; }

	public int End { get; set; }

	public int Price { get; set; }

	public string OverlayId { get; set; } = "";

	public string State { get; set; } = FarmHelpStates.Scheduled;

	public string Reason { get; set; } = "";

	public string Summary { get; set; } = "";

	public int CreatedDay { get; set; }

	public int StartedAt { get; set; } = -1;

	public int FinishedAt { get; set; } = -1;

	public int ClosedDay { get; set; } = -1;

	/// <summary>"x,y"；照看动物是动物的 ID。</summary>
	public List<string> Targets { get; set; } = new List<string>();

	public int Target { get; set; }

	public int Progress { get; set; }

	public int Refund { get; set; }

	public bool UsedSquad { get; set; }

	public int CenterX { get; set; }

	public int CenterY { get; set; }

	public int Radius { get; set; }

	public int RemoteDone { get; set; }
}

public sealed class FarmHelpLog
{
	public const string Path = "plans/farm_hands.json";

	public static readonly DocumentType<FarmHelpLog> Doc = new DocumentType<FarmHelpLog>("上门帮工", 1, () => new FarmHelpLog());

	public int NextNumber { get; set; } = 1;

	public List<FarmHelpJob> Items { get; set; } = new List<FarmHelpJob>();
}

/// <summary>上门帮工的规则和文字。</summary>
public static class FarmHelpRules
{
	public const string Owner = "farmhelp";

	public const int DefaultTime = 900;

	public const int EarliestTime = 700;

	public const int LatestStart = 1800;

	public const int MaxPerDay = 2;

	public const int SameDayCancelFriendship = -10;

	public const int ClosedKeepDays = 7;

	public const int MinNoticeMinutes = 60;

	public const string OffReason = "farm help is off";
	public const string ChildReason = "children don't do farm work";
	public const string TaskReason = "can't do that as a farm visit";
	public const string DaysReason = "only today or tomorrow";
	public const string HoursReason = "too early or too late";
	public const string SoonReason = "too soon";
	public const string ComingReason = "already coming that day";
	public const string FullReason = "two helpers that day already";
	public const string ShopReason = "has to mind the shop then";
	public const string NoHouseReason = "no farmhouse";
	public const string TooLateReason = "too late to get there today";
	public const string SentHome = "sent home";
	public const string AllDone = "all done";

	public static readonly IReadOnlyList<string> Tasks = new[] { "water", "harvest", "animals", "clear" };

	private sealed record Shop(int Open, int Close, DayOfWeek[] Off);

	private static readonly DayOfWeek[] NoDays = Array.Empty<DayOfWeek>();

	/// <summary>开店和上班的人：营业时间里不来。</summary>
	private static readonly Dictionary<string, Shop> Shops = new Dictionary<string, Shop>(StringComparer.OrdinalIgnoreCase)
	{
		["Pierre"] = new Shop(900, 1700, new[] { DayOfWeek.Wednesday }),
		["Gus"] = new Shop(1200, 2400, NoDays),
		["Clint"] = new Shop(900, 1600, NoDays),
		["Robin"] = new Shop(900, 1700, new[] { DayOfWeek.Tuesday }),
		["Marnie"] = new Shop(900, 1600, new[] { DayOfWeek.Monday, DayOfWeek.Tuesday }),
		["Willy"] = new Shop(900, 1700, new[] { DayOfWeek.Saturday }),
		["Harvey"] = new Shop(900, 1500, NoDays),
		["Emily"] = new Shop(1600, 2400, NoDays),
		["Shane"] = new Shop(900, 1700, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
	};

	public static int DurationOf(string task) => task == "clear" ? 180 : 120;

	public static int RateOf(string task) => task switch
	{
		"water" => 6,
		"harvest" => 4,
		"animals" => 2,
		"clear" => 2,
		_ => 1
	};

	public static int CapacityOf(string task) => RateOf(task) * (DurationOf(task) / 10);

	public static int EndOf(string task, int start) => Math.Min(OverlayRules.LatestEnd, Clock.Add(start, DurationOf(task)));

	/// <summary>第 0 天是春 1 日星期一。</summary>
	public static DayOfWeek WeekDay(int totalDays) => (DayOfWeek)((totalDays + 1) % 7);

	public static string? ShopConflict(string npc, int day, int start, int end)
	{
		if (!Shops.TryGetValue(npc, out Shop? shop) || shop.Off.Contains(WeekDay(day)))
		{
			return null;
		}
		return start < shop.Close && shop.Open < end ? ShopReason : null;
	}

	/// <summary>没说几点时挪到下班以后；不用挪返回原来的时间。</summary>
	public static int AfterWork(string npc, int day, int time, string task)
	{
		return Shops.TryGetValue(npc, out Shop? shop) && ShopConflict(npc, day, time, EndOf(task, time)) != null && shop.Close <= LatestStart ? Math.Max(time, shop.Close) : time;
	}

	/// <summary>一条帮工提议是不是约好时间上门：四种活之一，并且说的是明天或今天一小时以后。</summary>
	public static bool IsFarmVisit(string type, string task, string? when, string? time, int now, out int dayOffset, out int start)
	{
		dayOffset = 0;
		start = DefaultTime;
		if (type != OfferLedger.Work || !Tasks.Contains(task))
		{
			return false;
		}
		string w = (when ?? "").ToLowerInvariant();
		int? t = MeetupRules.ParseTime(time) ?? MeetupRules.ParseTime(when);
		if (w.Contains("明") || w.Contains("tomorrow"))
		{
			dayOffset = 1;
			start = t ?? DefaultTime;
			return true;
		}
		if (t.HasValue && Clock.Minutes(t.Value) >= Clock.Minutes(now) + MinNoticeMinutes)
		{
			start = t.Value;
			return true;
		}
		return false;
	}

	public static int RefundFor(int price, int done, int target)
	{
		double share = target > 0 ? Math.Min(1.0, (double)done / target) : 0.0;
		return (int)Math.Round(price * (1.0 - share));
	}

	/// <summary>一片最密的地方：中心和半径内的格子，近的在前。</summary>
	public static List<(int X, int Y)> Densest(IReadOnlyList<(int X, int Y)> tiles, int radius, out (int X, int Y) center)
	{
		center = (0, 0);
		if (tiles.Count == 0)
		{
			return new List<(int, int)>();
		}
		(int X, int Y) best = tiles[0];
		int bestCount = -1;
		foreach ((int X, int Y) c in tiles)
		{
			int count = tiles.Count(t => Math.Abs(t.X - c.X) <= radius && Math.Abs(t.Y - c.Y) <= radius);
			if (count > bestCount)
			{
				bestCount = count;
				best = c;
			}
		}
		center = best;
		(int X, int Y) mid = best;
		return tiles.Where(t => Math.Abs(t.X - mid.X) <= radius && Math.Abs(t.Y - mid.Y) <= radius).OrderBy(t => Math.Abs(t.X - mid.X) + Math.Abs(t.Y - mid.Y)).ToList();
	}

	public static string TaskLabel(string task) => task switch
	{
		"water" => Tr.Get("farmhelp.task.water", "浇水"),
		"harvest" => Tr.Get("farmhelp.task.harvest", "收菜"),
		"animals" => Tr.Get("farmhelp.task.animals", "照看动物"),
		"clear" => Tr.Get("farmhelp.task.clear", "清理树枝和石头"),
		_ => task
	};

	public static string RefundText(int refund) => refund > 0 ? Tr.Get("farmhelp.refund", "退回 {{refund}}g。", ("refund", refund)) : "";

	public static string Explain(string reason)
	{
		if (reason.Contains("shop"))
		{
			return Tr.Get("farmhelp.why.shop", "那时要看店");
		}
		if (reason.Contains("festival"))
		{
			return Tr.Get("farmhelp.why.festival", "那天镇上过节");
		}
		if (reason.Contains("birthday"))
		{
			return Tr.Get("farmhelp.why.birthday", "那天过生日，家里有安排");
		}
		if (reason.Contains("already coming"))
		{
			return Tr.Get("farmhelp.why.already", "那天已经说好要来了");
		}
		if (reason.Contains("two helpers"))
		{
			return Tr.Get("farmhelp.why.full", "那天已经有两个人来帮忙了");
		}
		if (reason.Contains("too early") || reason.Contains("too soon"))
		{
			return Tr.Get("farmhelp.why.time", "那个时间不太方便");
		}
		if (reason.Contains("already has plans"))
		{
			return Tr.Get("farmhelp.why.plans", "那时已经有别的安排");
		}
		return Tr.Get("farmhelp.why.other", "那天不太方便");
	}

	/// <summary>给 AI 的一段：约好来帮工、正在干、前几天干过。</summary>
	public static string PromptBlock(IEnumerable<FarmHelpJob> mine, int today, Func<FarmHelpJob, int> countDone)
	{
		List<string> lines = new List<string>();
		foreach (FarmHelpJob j in mine.OrderBy(j => j.Day))
		{
			string what = TaskLabel(j.Task);
			switch (j.State)
			{
				case FarmHelpStates.Scheduled:
					lines.Add($"- 你答应了{MeetupRules.RelativeDay(j.Day, today)} {Clock.Text(j.Start)} 去农夫的农场帮忙{what}，大约干到 {Clock.Text(j.End)}{(j.Price > 0 ? $"（农夫付了你 {j.Price}g）" : "")}。");
					break;
				case FarmHelpStates.Working:
					lines.Add($"- 你这会儿正在农夫的农场上{what}，是答应好的（{Clock.Text(j.StartedAt)} 开始，大约干到 {Clock.Text(j.End)}），已经做完 {countDone(j)}/{j.Target}。说话要像正干着农活的人。");
					break;
				case FarmHelpStates.Done when today - j.ClosedDay <= 2:
					lines.Add($"- {MeetupRules.RelativeDay(j.ClosedDay, today)}你去农夫的农场帮忙{what}，做完 {j.Progress}/{j.Target}。");
					break;
				case FarmHelpStates.NothingToDo when today - j.ClosedDay <= 2:
					lines.Add($"- {MeetupRules.RelativeDay(j.ClosedDay, today)}你去农夫的农场帮忙{what}，到了发现没什么要干的。");
					break;
				case FarmHelpStates.CancelledByPlayer when today - j.ClosedDay <= 2:
					lines.Add("- 农夫跟你说农场上不用帮忙了。你不往心里去。");
					break;
				case FarmHelpStates.CalledOff when today - j.ClosedDay <= 2:
					lines.Add(j.Reason == PlanReasons.Rain ? "- 下雨了，所以你没去给农夫的地浇水。" : $"- {MeetupRules.RelativeDay(j.ClosedDay, today)}你没能去农夫的农场帮忙，有点过意不去。");
					break;
			}
		}
		return lines.Count == 0 ? "" : "【上门帮工】\n" + string.Join("\n", lines);
	}
}
