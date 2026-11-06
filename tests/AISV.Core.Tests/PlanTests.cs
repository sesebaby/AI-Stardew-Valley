using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Group;
using Xunit;

namespace AISV.Core.Tests;

public class PlanTests
{
	[Theory]
	[InlineData(1430, 870)]
	[InlineData(600, 360)]
	[InlineData(2400, 1440)]
	public void ClockRoundTrips(int time, int minutes)
	{
		Assert.Equal(minutes, Clock.Minutes(time));
		Assert.Equal(time, Clock.FromMinutes(minutes));
	}

	[Fact]
	public void ClockAddsAcrossHours()
	{
		Assert.Equal(1610, Clock.Add(1450, 80));
		Assert.Equal("9:05", Clock.Text(905));
	}

	[Theory]
	[InlineData(5, 900, 5, 1400, 1600, null)]
	[InlineData(5, 900, 4, 1400, 1600, PlanReasons.DayRange)]
	[InlineData(5, 900, 13, 1400, 1600, PlanReasons.DayRange)]
	[InlineData(5, 900, 12, 1400, 1600, null)]
	[InlineData(5, 900, 6, 600, 900, PlanReasons.TimeRange)]
	[InlineData(5, 900, 6, 1470, 1600, PlanReasons.TimeRange)]
	[InlineData(5, 900, 6, 1600, 1600, PlanReasons.TimeRange)]
	[InlineData(5, 1700, 5, 1400, 1600, PlanReasons.Passed)]
	public void OverlayTimes(int today, int now, int day, int start, int end, string? reason)
	{
		Assert.Equal(reason, OverlayRules.CheckTimes(today, now, day, start, end));
	}

	[Fact]
	public void OverlapOnlyCountsOpenSameDay()
	{
		ScheduleOverlay o = new ScheduleOverlay { Day = 3, Start = 1200, End = 1400 };
		Assert.True(OverlayRules.Overlaps(o, 3, 1300, 1500));
		Assert.False(OverlayRules.Overlaps(o, 3, 1400, 1500));
		Assert.False(OverlayRules.Overlaps(o, 4, 1300, 1500));
		o.State = OverlayStates.Done;
		Assert.False(OverlayRules.Overlaps(o, 3, 1300, 1500));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 10)]
	[InlineData(13, 10)]
	[InlineData(14, 20)]
	[InlineData(100, 80)]
	public void WalkMinutesByTiles(int tiles, int minutes) => Assert.Equal(minutes, OverlayRules.WalkMinutes(tiles));

	[Theory]
	[InlineData("14:00", 1400)]
	[InlineData("9：35", 930)]
	[InlineData("下午两点", 1400)]
	[InlineData("下午两点半", 1430)]
	[InlineData("晚上七点", 1900)]
	[InlineData("十点", 1000)]
	[InlineData("2pm", 1400)]
	[InlineData("8 o'clock", 800)]
	[InlineData("3点", 300)]
	[InlineData("", null)]
	[InlineData("whenever", null)]
	[InlineData("25:00", null)]
	public void ParsesTimes(string text, int? expected) => Assert.Equal(expected, MeetupRules.ParseTime(text));

	[Theory]
	[InlineData("tomorrow", "", 900, 1)]
	[InlineData("明天下午", "", 900, 1)]
	[InlineData("后天", "", 900, 2)]
	[InlineData("in 3 days", "", 900, 3)]
	[InlineData("大后天", "", 900, 3)]
	[InlineData("today", "", 1900, 0)]
	[InlineData("", "now", 1900, 0)]
	[InlineData("马上", "", 1900, 0)]
	[InlineData("", "15:00", 1300, 0)]
	[InlineData("", "14:00", 1330, 1)]
	[InlineData("", "", 900, 1)]
	public void ParsesDayOffsets(string when, string time, int now, int expected) => Assert.Equal(expected, MeetupRules.ParseDayOffset(when, time, now));

	[Theory]
	[InlineData(1000, 10, 1100)]
	[InlineData(1000, 50, 1110)]
	[InlineData(1005, 30, 1110)]
	[InlineData(1000, 100, 1200)]
	public void EarliestTodayRoundsUp(int now, int walk, int expected) => Assert.Equal(expected, MeetupRules.EarliestToday(now, walk));

	[Fact]
	public void MeetupEndStopsAtClosing()
	{
		MeetupPlace square = MeetupPlaces.Get("town_square")!;
		MeetupPlace home = MeetupPlaces.Get("home")!;
		Assert.Equal(1600, MeetupRules.End(1400, square));
		Assert.Equal(2200, MeetupRules.End(2100, home));
		Assert.Equal(2400, MeetupRules.End(2300, square));
	}

	[Theory]
	[InlineData("beach", "beach")]
	[InlineData("海边见", "beach")]
	[InlineData("星之果实酒吧", "saloon")]
	[InlineData("去你农场", "farm")]
	[InlineData("来我家里坐坐", "home")]
	[InlineData("community center", "community_center")]
	[InlineData("皮埃尔那儿", "pierres")]
	[InlineData("月球", null)]
	public void ParsesPlaces(string text, string? key) => Assert.Equal(key, MeetupPlaces.Parse(text)?.Key);

	[Fact]
	public void ThirteenPlacesAndSaloonOpensAtNoon()
	{
		Assert.Equal(13, MeetupPlaces.All.Count);
		MeetupPlace saloon = MeetupPlaces.Get("saloon")!;
		Assert.True(saloon.Indoors);
		Assert.Equal(1200, saloon.OpenTime);
		Assert.Equal("家里", MeetupPlaces.Label("home", "").Substring(2));
	}

	[Theory]
	[InlineData("festival day", "过节")]
	[InlineData("their birthday", "生日")]
	[InlineData("already has a plan with you", "已经约好一次")]
	[InlineData("you already have three plans that day", "够满")]
	[InlineData("the place isn't open then", "不开门")]
	[InlineData("too late to get there today", "赶不上")]
	[InlineData("meetup 还在冷却（1/3 天）", "事情有点多")]
	[InlineData("今天已经提过了", "事情有点多")]
	[InlineData("meetup 要 2 心，现在 1 心", "不算太熟")]
	[InlineData("has to mind the shop then", "看店")]
	[InlineData("two helpers that day already", "两个人")]
	[InlineData("already coming that day", "说好要去")]
	[InlineData("too soon", "这么急")]
	[InlineData("farm help is off", "帮不上")]
	[InlineData("whatever", "走不开")]
	public void DeclineLinesFollowReason(string reason, string phrase)
	{
		string line = MeetupRules.DeclineLine(reason);
		Assert.Contains(phrase, line);
		Assert.EndsWith("改天再约吧。", line);
	}

	[Fact]
	public void MeetupPromptCoversRecentStates()
	{
		List<Meetup> items = new List<Meetup>
		{
			new Meetup { Place = "beach", Day = 11, Time = 1400, State = MeetupStates.Scheduled },
			new Meetup { Place = "saloon", Day = 8, ClosedDay = 8, State = MeetupStates.Missed },
			new Meetup { Place = "river", Day = 5, ClosedDay = 5, State = MeetupStates.Kept }
		};
		string block = MeetupRules.PromptBlock(items, 10, k => k);
		Assert.Contains("明天 14:00 在beach碰面", block);
		Assert.Contains("2 天前你在saloon等农夫", block);
		Assert.DoesNotContain("river", block);
		Assert.Equal("", MeetupRules.PromptBlock(Array.Empty<Meetup>(), 10, k => k));
	}

	[Theory]
	[InlineData("water", 120, 6, 72)]
	[InlineData("harvest", 120, 4, 48)]
	[InlineData("animals", 120, 2, 24)]
	[InlineData("clear", 180, 2, 36)]
	public void FarmHelpNumbers(string task, int duration, int rate, int capacity)
	{
		Assert.Equal(duration, FarmHelpRules.DurationOf(task));
		Assert.Equal(rate, FarmHelpRules.RateOf(task));
		Assert.Equal(capacity, FarmHelpRules.CapacityOf(task));
	}

	[Fact]
	public void FarmHelpEndCapsAtMidnight()
	{
		Assert.Equal(1100, FarmHelpRules.EndOf("water", 900));
		Assert.Equal(2100, FarmHelpRules.EndOf("clear", 1800));
		Assert.Equal(2400, FarmHelpRules.EndOf("water", 2300));
	}

	[Theory]
	[InlineData(0, DayOfWeek.Monday)]
	[InlineData(6, DayOfWeek.Sunday)]
	[InlineData(9, DayOfWeek.Wednesday)]
	[InlineData(28, DayOfWeek.Monday)]
	public void WeekDays(int day, DayOfWeek expected) => Assert.Equal(expected, FarmHelpRules.WeekDay(day));

	[Theory]
	[InlineData("Pierre", 0, 1000, true)]
	[InlineData("Pierre", 2, 1000, false)]
	[InlineData("Pierre", 0, 1700, false)]
	[InlineData("Robin", 1, 1000, false)]
	[InlineData("Shane", 5, 1000, false)]
	[InlineData("Gus", 0, 1000, false)]
	[InlineData("Gus", 0, 1100, true)]
	[InlineData("Haley", 0, 1000, false)]
	public void ShopHours(string npc, int day, int start, bool busy)
	{
		Assert.Equal(busy, FarmHelpRules.ShopConflict(npc, day, start, FarmHelpRules.EndOf("water", start)) != null);
	}

	[Fact]
	public void AfterWorkMovesToClosing()
	{
		Assert.Equal(1700, FarmHelpRules.AfterWork("Pierre", 0, 900, "water"));
		Assert.Equal(900, FarmHelpRules.AfterWork("Pierre", 2, 900, "water"));
		Assert.Equal(900, FarmHelpRules.AfterWork("Gus", 0, 900, "water"));
		Assert.Equal(900, FarmHelpRules.AfterWork("Haley", 0, 900, "water"));
	}

	[Theory]
	[InlineData("work", "water", "tomorrow", "", 900, true, 1, 900)]
	[InlineData("work", "water", "明天上午", "10:00", 900, true, 1, 1000)]
	[InlineData("work", "harvest", "today", "14:00", 1200, true, 0, 1400)]
	[InlineData("work", "harvest", "today", "12:30", 1200, false, 0, 900)]
	[InlineData("work", "water", "today", "", 900, false, 0, 900)]
	[InlineData("work", "fish", "tomorrow", "", 900, false, 0, 900)]
	[InlineData("follow", "", "tomorrow", "", 900, false, 0, 900)]
	public void FarmVisits(string type, string task, string when, string time, int now, bool visit, int offset, int start)
	{
		Assert.Equal(visit, FarmHelpRules.IsFarmVisit(type, task, when, time, now, out int o, out int s));
		Assert.Equal(offset, o);
		Assert.Equal(start, s);
	}

	[Theory]
	[InlineData(600, 0, 10, 600)]
	[InlineData(600, 5, 10, 300)]
	[InlineData(600, 10, 10, 0)]
	[InlineData(600, 12, 10, 0)]
	[InlineData(500, 1, 3, 333)]
	[InlineData(0, 1, 3, 0)]
	[InlineData(600, 0, 0, 600)]
	public void Refunds(int price, int done, int target, int refund) => Assert.Equal(refund, FarmHelpRules.RefundFor(price, done, target));

	[Fact]
	public void DensestPicksTheBusyPatch()
	{
		List<(int, int)> tiles = new List<(int, int)> { (1, 1), (40, 40), (41, 40), (40, 41), (42, 42) };
		List<(int X, int Y)> near = FarmHelpRules.Densest(tiles, 2, out (int X, int Y) center);
		Assert.Equal((40, 40), center);
		Assert.Equal(4, near.Count);
		Assert.Equal((40, 40), near[0]);
		Assert.Empty(FarmHelpRules.Densest(new List<(int, int)>(), 8, out _));
	}

	[Fact]
	public void FarmHelpPromptMentionsRainAndWork()
	{
		List<FarmHelpJob> jobs = new List<FarmHelpJob>
		{
			new FarmHelpJob { Task = "water", Day = 9, ClosedDay = 9, State = FarmHelpStates.CalledOff, Reason = PlanReasons.Rain },
			new FarmHelpJob { Task = "harvest", Day = 10, Start = 900, End = 1100, StartedAt = 910, Target = 8, State = FarmHelpStates.Working }
		};
		string block = FarmHelpRules.PromptBlock(jobs, 10, _ => 3);
		Assert.Contains("下雨了", block);
		Assert.Contains("做完 3/8", block);
		Assert.StartsWith("【上门帮工】", block);
	}

	[Fact]
	public void OfferJsonDependsOnSquadAndTexting()
	{
		string face = OfferPrompts.Json(squad: true, texting: false);
		Assert.Contains("follow、work 或 meetup", face);
		Assert.Contains("约好时间来农夫的农场", face);
		string noSquad = OfferPrompts.Json(squad: false, texting: false);
		Assert.Contains("\"kind\": \"meetup\"", noSquad);
		Assert.DoesNotContain("follow", noSquad);
		string text = OfferPrompts.Json(squad: true, texting: true);
		Assert.Contains("发消息", text);
		Assert.Contains("time 写 now", text);
		Assert.DoesNotContain("forage", text);
		Assert.StartsWith(",\n  \"plan_offer\"", text);
	}

	[Fact]
	public void NoMeetupsOnAFestivalDay()
	{
		Assert.Contains("过节那天不另约", OfferPrompts.Json(squad: false, texting: false));
		Assert.Contains("过节那天不约来帮工", OfferPrompts.Json(squad: true, texting: false));
	}

	[Fact]
	public void OfferJsonOnlyListsWhatTheHeartsAllow()
	{
		string two = OfferPrompts.Json(squad: true, texting: false, hearts: 2);
		Assert.Contains("\"kind\": \"meetup\"", two);
		Assert.DoesNotContain("follow", two);
		Assert.Contains("还不会陪他出门、帮他干活", two);
		string three = OfferPrompts.Json(squad: true, texting: false, hearts: 3);
		Assert.Contains("follow 或 meetup", three);
		Assert.Contains("还不会帮他干活", three);
		string none = OfferPrompts.Json(squad: true, texting: false, hearts: 1);
		Assert.Contains("一律 null", none);
		Assert.Contains("单独跟农夫约时间地点见面、陪他出门、帮他干活", none);
		Assert.Equal(OfferPrompts.Json(squad: true, texting: false), OfferPrompts.Json(squad: true, texting: false, hearts: 0, partner: true));
		Assert.DoesNotContain("还不会", OfferPrompts.Json(squad: true, texting: false));
	}

	[Fact]
	public void GroupReplyReadsMeetup()
	{
		string raw = "{\"lines\":[{\"speaker\":\"Robin\",\"text\":\"我下午过去。\"}],\"meetup\":{\"speaker\":\"robin\",\"when\":\"today\",\"time\":\"now\",\"where\":\"farm\",\"task\":\"Water\",\"summary\":\"去帮忙浇水\"}}";
		GroupTurn? turn = GroupReply.Parse(raw, new[] { "Robin", "Pierre" });
		Assert.NotNull(turn?.Meetup);
		Assert.Equal("Robin", turn!.Meetup!.Speaker);
		Assert.Equal("water", turn.Meetup.Task);
		Assert.Equal("now", turn.Meetup.Time);
	}

	[Fact]
	public void GroupMeetupNeedsTheSpeakerToHaveSpoken()
	{
		string raw = "{\"lines\":[{\"speaker\":\"Robin\",\"text\":\"好呀\"}],\"meetup\":{\"speaker\":\"Pierre\",\"when\":\"today\"}}";
		Assert.Null(GroupReply.Parse(raw, new[] { "Robin", "Pierre" })!.Meetup);
		Assert.Null(GroupReply.Parse("{\"lines\":[{\"speaker\":\"Robin\",\"text\":\"好\"}],\"meetup\":null}", new[] { "Robin" })!.Meetup);
	}

	[Fact]
	public void GroupPromptAddsMeetupFieldOnlyWhenAllowed()
	{
		GroupRequest r = new GroupRequest
		{
			PlayerName = "阿哲",
			Setting = GroupSetting.Phone,
			Members = new[] { new GroupMember("Robin", "木匠", 5, Array.Empty<string>()) },
			PlayerText = "谁来帮我浇水？",
			MeetupRule = OfferPrompts.GroupRule(squad: true)
		};
		string with = GroupPrompts.System(r, "中文");
		Assert.Contains("\"meetup\":null", with);
		Assert.Contains("来农场帮忙还要写 task", with);
		string without = GroupPrompts.System(r with { MeetupRule = "" }, "中文");
		Assert.DoesNotContain("\"meetup\"", without);
	}

	[Fact]
	public void SiteWorkersOnlyDoTheirJob()
	{
		CompanionRoster roster = new CompanionRoster();
		roster.SetJob("Sam", "water", 5, 0);
		roster.AddSite("Sam");
		Assert.True(roster.TaskOverride("Sam", "Watering", 5));
		Assert.False(roster.TaskOverride("Sam", "Attacking", 5));
		Assert.Equal("", roster.PromptLine("Sam", 5));
		roster.RemoveSite("Sam");
		Assert.Null(roster.TaskOverride("Sam", "Attacking", 5));
		Assert.NotEqual("", roster.PromptLine("Sam", 5));
	}
}
