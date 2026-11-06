using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.News;
using AISV.Core.Social.Gossip;
using AISV.Core.World.Ledger;
using Xunit;

namespace AISV.Core.Tests;

public class NewAppsTests
{
	[Fact]
	public void AgendaMergesStopsAndReadsBehaviors()
	{
		List<AgendaStop> stops = Agenda.Build(new (int, string, string?)[] { (900, "HaleyHouse", null), (1000, "Beach", "haley_photo"), (1030, "Beach", "haley_photo"), (1600, "Town", null) });
		Assert.Equal(3, stops.Count);
		Assert.Equal("拍照", stops[1].Doing);
		Assert.Equal("", stops[0].Doing);
		Assert.Equal(1, Agenda.Current(stops, 1200));
		Assert.Equal(-1, Agenda.Current(stops, 800));
		Assert.Equal("10:00", Agenda.Clock(1000));
		Assert.Equal("01:30", Agenda.Clock(2530));
		Assert.Contains("16:00", Agenda.Next("海莉", stops, 1200));
	}

	[Fact]
	public void HoroscopePicksWhatToGiveAndAvoid()
	{
		Assert.Equal(0, Horoscope.SignOf("spring", 1));
		Assert.Equal(1, Horoscope.SignOf("spring", 14));
		Assert.Equal(11, Horoscope.SignOf("winter", 28));
		Assert.Equal(-1, Horoscope.SignOf("moon", 3));
		var haleyLoved = Horoscope.Weights(new[] { new TasteItem(-7, 450), new TasteItem(-7, 480), new TasteItem(-80, 80), new TasteItem(-79, 50) }, new[] { new TasteItem(-80, 30) });
		var haleyHated = Horoscope.Weights(new[] { new TasteItem(-2, 2000), new TasteItem(-16, 50) }, new[] { new TasteItem(-81, 50) });
		var samLoved = Horoscope.Weights(new[] { new TasteItem(-7, 300), new TasteItem(-7, 200), new TasteItem(-2, 750) }, Array.Empty<TasteItem>());
		var samHated = Horoscope.Weights(new[] { new TasteItem(-16, 10), new TasteItem(-81, 40) }, Array.Empty<TasteItem>());
		var avgLoved = Horoscope.Average(new[] { haleyLoved, samLoved });
		var avgHated = Horoscope.Average(new[] { haleyHated, samHated });
		(string good, string bad) = Horoscope.Picks(haleyLoved, haleyHated, avgLoved, avgHated);
		Assert.Equal("花", good);
		Assert.Equal("宝石", bad);
		Assert.Equal("宝石", Horoscope.Picks(samLoved, samHated, avgLoved, avgHated).Good);
		Fortune f = Horoscope.For("Haley", 5, 0, good, bad, 42, 10);
		Assert.Equal(f, Horoscope.For("Haley", 5, 0, good, bad, 42, 10));
		Assert.InRange(f.Stars, 1, 5);
		Assert.Equal("宜送花，忌送宝石", Horoscope.Advice(f));
		Assert.True(Horoscope.Stars(100, 4) >= Horoscope.Stars(-100, 0));
	}

	[Fact]
	public void ObservatoryKnowsMoonRainAndMeteors()
	{
		Assert.Equal(0, Observatory.Phase(1));
		Assert.Equal(4, Observatory.Phase(15));
		IReadOnlyList<int> nights = Observatory.MeteorNights(99, "summer", 1);
		Assert.Equal(2, nights.Count);
		Assert.Equal(nights, Observatory.MeteorNights(99, "summer", 1));
		Assert.True(Math.Abs(nights[0] - nights[1]) >= 5);
		NightSky meteor = Observatory.Tonight(99, "summer", nights[0], 1, false, false, false, false);
		Assert.True(meteor.Meteors);
		Assert.NotNull(Observatory.PromptLine(meteor));
		Assert.False(Observatory.Tonight(99, "summer", nights[0], 1, true, false, false, false).Meteors);
		Assert.True(Observatory.Tonight(99, "summer", 27, 1, false, false, true, false).Meteors);
		Assert.Empty(Observatory.Tonight(99, "winter", 3, 1, false, true, false, false).Spots);
	}

	[Fact]
	public void WhoSaidItLeadsWithNewestGossip()
	{
		List<Quote> raw = new List<Quote>
		{
			new Quote("我亲眼看见的，两个人挨得可近了。", "Pam", WhoSaidIt.Gossip, 30),
			new Quote("今天的花开得真好呀。", "Evelyn", WhoSaidIt.Talk, 31),
			new Quote("海莉说她讨厌这个。", "Haley", WhoSaidIt.Chat, 29),
			new Quote("短", "Sam", WhoSaidIt.Chat, 31),
			new Quote("今天的花开得真好呀。", "George", WhoSaidIt.Talk, 31),
			new Quote("矿洞里冷得要命，别去太深。", "Clint", WhoSaidIt.Text, 20)
		};
		List<Quote> clean = WhoSaidIt.Clean(raw, n => n == "Haley" ? "海莉" : n);
		Assert.Equal(3, clean.Count);
		List<QuizQuestion> round = WhoSaidIt.Round(clean, new[] { "Pam", "Evelyn", "Clint", "Sam", "Abigail" }, new Random(3));
		Assert.Equal("Pam", round[0].Answer);
		Assert.All(round, q => Assert.Equal(4, q.Choices.Distinct().Count()));
		Assert.All(round, q => Assert.Contains(q.Answer, q.Choices));
		Assert.Contains("全对", WhoSaidIt.Verdict(5, 5));
	}

	[Fact]
	public void JunimoFollowsCareAndHome()
	{
		JunimoState s = new JunimoState();
		JunimoRules.Adopt(s, 10, new Random(1), "团子");
		Assert.Equal(JunimoMood.Happy, JunimoRules.Mood(s));
		Assert.NotNull(JunimoRules.Act(s, "feed", 10));
		Assert.Null(JunimoRules.Act(s, "feed", 10));
		JunimoRules.NewDay(s, 11, new JunimoHome("Haley", 10, false, 0, null), n => n);
		Assert.Contains(s.Log, l => l.Text.Contains("Haley"));
		for (int d = 12; d < 18; d++)
		{
			JunimoRules.NewDay(s, d, new JunimoHome(null, 0, true, 1, "Haley"), n => n);
		}
		Assert.Equal(JunimoMood.Curled, JunimoRules.Mood(s));
		Assert.Contains(s.Log, l => l.Text.Contains("等的人没有回来"));
	}

	[Fact]
	public void NewsMarksRumorsUnconfirmed()
	{
		GossipEntry rumor = new GossipEntry { Day = 3, Season = "fall", Year = 2, Fact = "牛哥和艾米丽深夜一起走回农场。", Involved = new List<string> { "Pam" }, KnownTo = new List<string> { "Pam", "Pierre", "Gus", "Lewis" } };
		GossipEntry solid = new GossipEntry { Day = 2, Season = "fall", Year = 2, Fact = "艾米丽在酒吧打工。", Involved = new List<string> { "Gus", "Emily" }, KnownTo = new List<string> { "Gus" } };
		Assert.True(WeeklyNewsRules.Unverified(rumor));
		Assert.False(WeeklyNewsRules.Unverified(solid));
		Assert.Contains(WeeklyNewsRules.RumorMark, WeeklyNewsRules.Facts(new[] { rumor, solid }));
		List<string[]> names = new List<string[]> { new[] { "Emily", "艾米丽" }, new[] { "牛哥" } };
		List<string[][]> rumors = WeeklyNewsRules.Rumors(new[] { rumor, solid }, names);
		Assert.Single(rumors);
		Assert.EndsWith("（未经证实）", WeeklyNewsRules.MarkRumor("深夜，牛哥与艾米丽一同回了农场。", rumors));
		Assert.Equal("艾米丽今天也在酒吧忙。", WeeklyNewsRules.MarkRumor("艾米丽今天也在酒吧忙。", rumors));
		Assert.Equal("据说牛哥与艾米丽同行（未经证实）。", WeeklyNewsRules.MarkRumor("据说牛哥与艾米丽同行（未经证实）。", rumors));
	}

	[Fact]
	public void BarTabRoomGrowsWithFriendship()
	{
		Assert.Equal(0, LedgerRules.TabRoom(1, 0));
		Assert.Equal(300, LedgerRules.TabRoom(2, 0));
		Assert.Equal(1000, LedgerRules.TabRoom(10, 0));
		Assert.Equal(700, LedgerRules.TabRoom(10, 300));
	}
}
