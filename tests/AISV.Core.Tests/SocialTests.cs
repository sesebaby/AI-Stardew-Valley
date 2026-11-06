using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Social;
using AISV.Core.Social.Diary;
using AISV.Core.Social.Gossip;
using AISV.Core.Social.Letters;
using AISV.Core.Social.Web;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class MoodTests
{
	[Theory]
	[InlineData(-30, "melancholic")]
	[InlineData(-10, "irritated")]
	[InlineData(-9, "neutral")]
	[InlineData(9, "neutral")]
	[InlineData(10, "happy")]
	[InlineData(30, "excited")]
	public void LabelsFollowThresholds(int score, string label) => Assert.Equal(label, MoodBook.Label(score));

	[Fact]
	public void WeatherAndHeartsMoveTheScore()
	{
		MoodBook moods = new MoodBook();
		moods.CalculateDay("Rainy", "spring", 2, new[] { new MoodInput("Haley", false, 5), new MoodInput("Sebastian", false, 8) });
		Assert.True(moods.Get("Haley")!.Level < 0);
		Assert.True(moods.IsLow("Haley"));
		// 塞巴斯蒂安爱下雨（+30），8 心再 +10。
		Assert.True(moods.Get("Sebastian")!.Level >= 40);
		Assert.True(moods.IsHigh("Sebastian"));
	}

	[Fact]
	public void BirthdayIsExcitedAndAdjustmentsCarryToTomorrow()
	{
		MoodBook moods = new MoodBook();
		moods.CalculateDay("Sunny", "summer", 2, new[] { new MoodInput("Lewis", true, 3), new MoodInput("Pam", false, 3) });
		Assert.Equal("excited", moods.Get("Lewis")!.Temper);
		int before = moods.Get("Pam")!.Level;
		moods.Adjust("Pam", -20);
		Assert.Equal(before - 20, moods.Get("Pam")!.Level);
		moods.EndDay();
		Assert.Null(moods.Get("Pam"));
		moods.CalculateDay("Sunny", "summer", 2, new[] { new MoodInput("Pam", false, 3) });
		Assert.Equal(before - 20, moods.Get("Pam")!.Level);
		// 攒下的只算一次。
		moods.CalculateDay("Sunny", "summer", 2, new[] { new MoodInput("Pam", false, 3) });
		Assert.Equal(before, moods.Get("Pam")!.Level);
	}

	[Fact]
	public void PromptBlockIsEmptyBeforeMoodsAreCalculated()
	{
		Assert.Equal("", new MoodBook().PromptBlock("Abigail"));
	}
}

public class FeelingsTests
{
	private static NpcReaction R(int points, string emotion) => new NpcReaction { FriendshipPoints = points, Emotion = emotion, Reaction = "嗯" };

	[Fact]
	public void ToneFollowsPointsAndEmotion()
	{
		Assert.True(ExchangeTone.Bad(R(10, "neutral")));
		Assert.True(ExchangeTone.Bad(R(40, "angry")));
		Assert.False(ExchangeTone.Bad(R(11, "neutral")));
		Assert.True(ExchangeTone.Good(R(50, "neutral")));
		Assert.True(ExchangeTone.Good(R(20, "love")));
		Assert.False(ExchangeTone.Good(R(49, "neutral")));
		// 情绪和分数打架时以情绪为准，也不允许同一轮既算好又算坏。
		Assert.False(ExchangeTone.Bad(R(10, "happy")));
		Assert.True(ExchangeTone.Good(R(10, "happy")));
		Assert.True(ExchangeTone.Bad(R(80, "sad")));
		Assert.False(ExchangeTone.Good(R(80, "sad")));
	}

	[Fact]
	public void GiftTasteMatchesGameNumbers()
	{
		Assert.Equal("loved", GiftBook.Taste(0));
		Assert.Equal("liked", GiftBook.Taste(2));
		Assert.Equal("disliked", GiftBook.Taste(4));
		Assert.Equal("hated", GiftBook.Taste(6));
		Assert.Equal("neutral", GiftBook.Taste(8));
	}

	[Theory]
	[InlineData(3, 14)]
	[InlineData(2, 9)]
	[InlineData(1, 5)]
	public void NegativeMemoriesLastBySeverity(int severity, int days) => Assert.Equal(days, NegativeMemories.DaysFor(severity));

	[Fact]
	public void AtMostThreeNegativeMemories()
	{
		List<NegativeMemory> list = new List<NegativeMemory>();
		for (int i = 0; i < 6; i++)
		{
			NegativeMemories.Gift(list, "垃圾" + i, "hated", false, 10 + i);
		}
		Assert.True(list.Count <= NegativeMemories.Max);
	}

	[Fact]
	public void KeyMemoriesKeepNewestFive()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		EmotionalMemory m = new EmotionalMemory(() => store);
		GameDate d = new GameDate(5, "spring", 1);
		for (int i = 0; i < 7; i++)
		{
			m.AddKeyMemory("Abigail", "第" + i + "件事", d);
		}
		EmotionalProfile p = m.Find("Abigail")!;
		Assert.Equal(5, p.Recollections.Count);
		Assert.Equal("第6件事", p.Recollections[0]);
		Assert.Equal("第6件事", p.LatestMoment);
	}
}

public class GossipSpreadTests
{
	[Fact]
	public void FastTalkerReachesMoreOthersWhoAreNotIsolated()
	{
		GossipEntry e = GossipSpread.Create(1, "spring", 1, "皮埃尔说了件事", "皮埃尔说了件事", new[] { "Pierre" });
		int before = e.KnownTo.Count;
		Assert.True(GossipSpread.SpreadForDay(e, e.BornOn + 1));
		Assert.Equal(before + GossipSpread.FastTellsPerRound, e.KnownTo.Count);
		Assert.True(GossipSpread.FastTellsPerRound > GossipSpread.SlowTellsPerRound);
		Assert.DoesNotContain(e.KnownTo, GossipSpread.Isolated);
		Assert.Equal(e.KnownTo.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), e.KnownTo);
		// 同一天不会再传。
		Assert.False(GossipSpread.SpreadForDay(e, e.BornOn + 1));
	}

	[Fact]
	public void FestivalGossipStopsWhenHopsRunOut()
	{
		GossipEntry e = GossipSpread.Create(1, "spring", 1, "皮埃尔说了件事", "", new[] { "Pierre" });
		e.Kind = "festival";
		e.HopsLeft = 1;
		Assert.True(GossipSpread.SpreadForDay(e, e.BornOn + 1));
		Assert.Equal(0, e.HopsLeft);
		Assert.False(GossipSpread.SpreadForDay(e, e.BornOn + 2));
	}

	[Fact]
	public void IsolatedOnlyOrEmptyGossipDoesNotSpread()
	{
		GossipEntry nobody = new GossipEntry { BornOn = 1, LastSpreadOn = 1 };
		Assert.False(GossipSpread.SpreadForDay(nobody, 5));
		string loner = BuiltInLoner();
		GossipEntry lonely = new GossipEntry { BornOn = 1, LastSpreadOn = 1, KnownTo = new List<string> { loner } };
		Assert.False(GossipSpread.SpreadForDay(lonely, 5));
		Assert.Equal(1, lonely.LastSpreadOn);
		static string BuiltInLoner() => AISV.Core.Persona.BuiltInPersonas.VanillaNames.First(GossipSpread.Isolated);
	}

	[Fact]
	public void NormalizeFillsPairedFieldsBothWaysAndIsStable()
	{
		GossipEntry e = new GossipEntry { Day = 3, Season = "summer", Year = 2, Topic = "  k ", Kind = "festival", SourceRef = "x", KindRef = "   ", LeavesPromptOn = 9 };
		GossipSpread.Normalize(e, false);
		Assert.Equal("k", e.Key);
		Assert.Equal("festival", e.SourceKind);
		Assert.Equal("x", e.KindRef);
		Assert.Equal(9, e.FadesOn);
		Assert.Equal(Dates.TotalDays(3, "summer", 2), e.BornOn);
		Assert.Equal(e.BornOn, e.LastSpreadOn);
		GossipSpread.Normalize(e, false);
		Assert.Equal("k", e.Key);
		GossipEntry both = new GossipEntry { Key = "a", Topic = "b", FadesOn = 3, LeavesPromptOn = 4 };
		GossipSpread.Normalize(both, false);
		Assert.Equal(("a", "b", 3, 4), (both.Key, both.Topic, both.FadesOn, both.LeavesPromptOn));
		Assert.Equal(0, both.BornOn);
	}

	[Fact]
	public void FreshGossipBeatsOldOnesAboutTheSamePeople()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ModConfig c = new ModConfig();
		GossipBook book = new GossipBook(() => store, () => c, _ => true, _ => true, n => n, NullLog.Instance);
		book.AddDaily(new GameDate(3, "spring", 1), "Haley 和 Emily 拌嘴，牛哥在旁边劝", "", new[] { "Haley" }, Array.Empty<string>());
		GameDate now = new GameDate(3, "spring", 2);
		book.AddDaily(now, "深夜牛哥和 Emily 挨得很近地走过镇上", "", new[] { "Haley" }, Array.Empty<string>());
		string memory = book.MemoryFor("Haley", "牛哥", now.TotalDays);
		Assert.True(memory.IndexOf("深夜", StringComparison.Ordinal) < memory.IndexOf("拌嘴", StringComparison.Ordinal));
	}

	[Fact]
	public void UnknownGossipIsKnownByEveryone()
	{
		GossipEntry e = new GossipEntry { Fact = "x" };
		Assert.True(GossipSpread.KnownBy(e, "Abigail"));
	}

	[Fact]
	public void MemoryOnlyIncludesGossipTheVillagerHeard()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ModConfig c = new ModConfig();
		GossipBook book = new GossipBook(() => store, () => c, _ => true, _ => true, n => n, NullLog.Instance);
		GameDate d = new GameDate(3, "spring", 1);
		book.AddDaily(d, "Abigail 在矿洞里迷路了", "阿比盖尔迷路了", new[] { "Abigail" }, Array.Empty<string>());
		Assert.Contains("迷路", book.MemoryFor("Abigail", "阿哲", d.TotalDays));
		Assert.Equal("", book.MemoryFor("Linus", "阿哲", d.TotalDays));
		Assert.Equal("阿比盖尔迷路了", book.PendingChatMessage);
		Assert.Equal("阿比盖尔迷路了", book.TakePendingChatMessage());
		Assert.Equal("", book.PendingChatMessage);
	}
}

public class LetterTests
{
	private static (LetterBook Book, ModConfig Config) Make()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ModConfig c = new ModConfig();
		return (new LetterBook(() => store, () => c, _ => true), c);
	}

	[Fact]
	public void HeartMilestoneTriggersOncePerMilestone()
	{
		(LetterBook book, ModConfig c) = Make();
		c.MailHeartSteps = new List<int> { 3, 6, 8, 10 };
		c.MailRestDays = 7;
		GameDate d = new GameDate(10, "spring", 1);
		List<LetterTrigger> due = book.DueToday(d, new[] { new LetterCandidate("Abigail", 4, 2, null) });
		Assert.Single(due);
		Assert.Equal("hearts_3", due[0].Trigger);
		book.Add("Abigail", "hearts_3", "你好", "正文", -1, d);
		// 冷却 7 天内不再写。
		Assert.Empty(book.DueToday(new GameDate(15, "spring", 1), new[] { new LetterCandidate("Abigail", 7, 2, null) }));
		Assert.Equal("hearts_6", book.DueToday(new GameDate(17, "spring", 1), new[] { new LetterCandidate("Abigail", 7, 2, null) })[0].Trigger);
	}

	[Fact]
	public void MissYouNeedsSixHeartsFiveTalksAndAWeek()
	{
		(LetterBook book, ModConfig c) = Make();
		c.MailHeartSteps = new List<int> { 14 };
		c.MailMissingDays = 7;
		GameDate d = new GameDate(20, "summer", 1);
		Assert.Empty(book.DueToday(d, new[] { new LetterCandidate("Leah", 6, 6, d.TotalDays - 6) }));
		Assert.Empty(book.DueToday(d, new[] { new LetterCandidate("Leah", 6, 5, d.TotalDays - 7) }));
		LetterTrigger t = book.DueToday(d, new[] { new LetterCandidate("Leah", 6, 6, d.TotalDays - 7) }).Single();
		Assert.Equal("miss_you_summer_1", t.Trigger);
	}

	[Fact]
	public void DailyCapAndAlphabeticalOrder()
	{
		(LetterBook book, _) = Make();
		List<LetterTrigger> due = book.DueToday(new GameDate(1, "fall", 1), new[] { new LetterCandidate("Sam", 3, 0, null), new LetterCandidate("Alex", 3, 0, null) });
		Assert.Single(due);
		Assert.Equal("Alex", due[0].Npc);
	}

	[Fact]
	public void MilestonesFallBackToDefaults()
	{
		Assert.Equal(new List<int> { 3, 6, 8, 10 }, LetterBook.Milestones(new[] { 0, 20 }));
		Assert.Equal(new List<int> { 2, 5 }, LetterBook.Milestones(new[] { 5, 2, 5 }));
	}

	[Fact]
	public void NewLettersGetDistinctMailKeys()
	{
		(LetterBook book, _) = Make();
		GameDate d = new GameDate(1, "fall", 1);
		QueuedLetter a = book.Add("Sam", "hearts_3", "a", "b", -1, d)!;
		QueuedLetter b = book.Add("Sam", "thanks_birthday_gift_1", "a", "b", -1, d)!;
		Assert.NotEqual(a.Key, b.Key);
		Assert.StartsWith("AISV_", a.Key);
		Assert.Null(book.Add("Sam", "hearts_3", "a", "b", -1, d));
	}

	[Fact]
	public void MailTextHasSignatureAndItem()
	{
		QueuedLetter l = new QueuedLetter { From = "Sam", Text = "你好", EnclosedId = 66 };
		Assert.Equal("你好^^   — 山姆%item object 66 1 %%", LetterBook.MailText(l, "山姆"));
	}

	[Fact]
	public void MailTextPrefersQualifiedItem()
	{
		QueuedLetter l = new QueuedLetter { From = "Sam", Text = "给你", EnclosedId = 66, Enclosed = "(O)72", EnclosedCount = 3 };
		Assert.Equal("给你^^   — 山姆%item id (O)72 3 %%", LetterBook.MailText(l, "山姆"));
	}
}

public class SocialWebTests
{
	private static SocialDayInput Input(GameDate d) => new SocialDayInput
	{
		Today = d,
		UniqueGameId = "123456",
		EnabledNpcs = new[] { "Abigail", "Sebastian", "Sam", "Penny", "Maru", "Emily", "Haley", "Alex" },
		Locations = new Dictionary<string, string> { ["Abigail"] = "Town", ["Sebastian"] = "Town", ["Sam"] = "Saloon", ["Penny"] = "Town" }
	};

	[Fact]
	public void SameDaySameSaveGivesSamePlan()
	{
		ModConfig c = new ModConfig();
		GameDate d = new GameDate(8, "spring", 1);
		DocumentStore s1 = new DocumentStore(new MemoryBackend(), NullLog.Instance), s2 = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		List<NpcSocialCandidate> p1 = new SocialWeb(() => s1, () => c, n => n).PlanDay(Input(d));
		List<NpcSocialCandidate> p2 = new SocialWeb(() => s2, () => c, n => n).PlanDay(Input(d));
		Assert.NotEmpty(p1);
		Assert.True(p1.Count <= c.VillagerMeetupsDaily + 1);
		Assert.Equal(p1.Select(x => x.First + x.Second), p2.Select(x => x.First + x.Second));
	}

	[Fact]
	public void ScorePrefersCloserPairsAndHoldsOffRecentOnes()
	{
		ModConfig c = new ModConfig { MeetupSpicePercent = 50 };
		NpcSocialCandidate cand = new NpcSocialCandidate { First = "A", Second = "B", AlreadyLinked = true, FirstHasPersona = true, SecondHasPersona = true };
		NpcSocialEdge Edge(float closeness, int last = 0, int cooldown = 0, float tension = 0) => new NpcSocialEdge { Closeness = closeness, Liking = closeness, Friction = tension, LastMetDay = last, RestUntilDay = cooldown };
		Assert.True(SocialWeb.Score(cand, Edge(0.8f), 100, c) > SocialWeb.Score(cand, Edge(0.2f), 100, c));
		Assert.Equal(0, SocialWeb.Score(cand, Edge(0.8f, cooldown: 101), 100, c));
		Assert.True(SocialWeb.Score(cand, Edge(0.8f, cooldown: 100), 100, c) > 0);
		Assert.True(SocialWeb.Score(cand, Edge(0.8f, last: 99), 100, c) < SocialWeb.Score(cand, Edge(0.8f, last: 97), 100, c));
		Assert.True(SocialWeb.Score(cand, Edge(0.5f, tension: BondLines.AwkwardTension), 100, c) > SocialWeb.Score(cand, Edge(0.5f), 100, c));
		Assert.True(SocialWeb.Score(cand, Edge(0.5f, tension: BondLines.TenseTension), 100, c) < SocialWeb.Score(cand, Edge(0.5f), 100, c));
		NpcSocialCandidate stray = new NpcSocialCandidate { First = "A", Second = "B", FirstHasPersona = true, SecondHasPersona = true };
		Assert.True(SocialWeb.Score(stray, null, 100, new ModConfig { MeetupSpicePercent = 900 }) > SocialWeb.Score(stray, null, 100, new ModConfig { MeetupSpicePercent = 0 }));
		Assert.Equal(SocialWeb.Score(stray, null, 100, new ModConfig { MeetupSpicePercent = 100 }), SocialWeb.Score(stray, null, 100, new ModConfig { MeetupSpicePercent = 900 }));
		Assert.True(SocialWeb.Score(new NpcSocialCandidate { First = "A", Second = "B", FromLore = true, FirstHasPersona = true, SecondHasPersona = true }, null, 100, c) > SocialWeb.Score(stray, null, 100, c));
		Assert.True(SocialWeb.Score(new NpcSocialCandidate { First = "A", Second = "B", SamePlace = true, FirstHasPersona = true, SecondHasPersona = true }, null, 100, c) > SocialWeb.Score(stray, null, 100, c));
		Assert.True(SocialWeb.Score(new NpcSocialCandidate { First = "A", Second = "B" }, null, 100, c) < SocialWeb.Score(stray, null, 100, c));
	}

	[Fact]
	public void TonesMoveTheRightMeters()
	{
		foreach (string tone in new[] { "sweet", "funny", "awkward", "tense", "strange", "mundane", "whatever", "" })
		{
			Assert.True(SocialWeb.DeltaFor(tone).Closer > 0);
		}
		NpcSocialDelta sweet = SocialWeb.DeltaFor("  SWEET ");
		Assert.True(sweet.Liking > 0 && sweet.Trust > 0 && sweet.Friction < 0);
		NpcSocialDelta funny = SocialWeb.DeltaFor("funny");
		Assert.True(funny.Banter > funny.Liking && funny.Liking > 0 && funny.Friction < 0);
		NpcSocialDelta awkward = SocialWeb.DeltaFor("awkward");
		Assert.True(awkward.Liking == 0 && awkward.Trust == 0 && awkward.Friction > 0 && awkward.Banter > 0);
		NpcSocialDelta tense = SocialWeb.DeltaFor("tense");
		Assert.True(tense.Liking < 0 && tense.Trust < 0 && tense.Friction > awkward.Friction);
		NpcSocialDelta strange = SocialWeb.DeltaFor("strange");
		Assert.True(strange.Banter > strange.Liking && strange.Banter > strange.Friction);
		NpcSocialDelta unknown = SocialWeb.DeltaFor(null!);
		Assert.True(unknown.Liking > 0 && unknown.Friction == 0);
	}

	[Fact]
	public void WebStateAgreesWithTheVillageMap()
	{
		NpcSocialEdge Edge(float aff, float trust, float tension, float play = 0, int count = 5, float fam = 0.5f, params string[] tags) =>
			new NpcSocialEdge { First = "A", Second = "B", Liking = aff, Trust = trust, Friction = tension, Banter = play, Encounters = count, Closeness = fam, Tags = tags.ToList() };
		(NpcSocialEdge Edge, string State, AISV.Core.Apps.Bond Bond)[] cases =
		{
			(Edge(1f, 1f, 1f, count: 0), "unknown", AISV.Core.Apps.Bond.Strangers),
			(Edge(1f, 1f, BondLines.RivalTension), "rivalry", AISV.Core.Apps.Bond.Rivals),
			(Edge(1f, 1f, BondLines.RivalTension, count: BondLines.RivalEncounters - 1), "tense", AISV.Core.Apps.Bond.Tense),
			(Edge(0f, 0f, BondLines.AwkwardTension), "awkward", AISV.Core.Apps.Bond.Awkward),
			(Edge(1f, 1f, 0f, play: 1f, tags: "Odd_Friendship"), "odd_friendship", AISV.Core.Apps.Bond.OddFriends),
			(Edge(BondLines.CloseAffinity, BondLines.CloseTrust, 0f), "close", AISV.Core.Apps.Bond.BestFriends),
			(Edge(0f, BondLines.FriendlyLine, 0f), "friendly", AISV.Core.Apps.Bond.Friends),
			(Edge(0f, 0f, 0f), "acquaintance", AISV.Core.Apps.Bond.Acquaintances)
		};
		foreach ((NpcSocialEdge edge, string state, AISV.Core.Apps.Bond bond) in cases)
		{
			Assert.Equal(state, SocialWeb.Classify(edge));
			Assert.Equal(bond, AISV.Core.Apps.VillageMap.Classify(edge, AISV.Core.Apps.MapPolicy.Safe));
		}
	}

	[Fact]
	public void FallbackMomentsChangeTheEdge()
	{
		ModConfig c = new ModConfig();
		DocumentStore s = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		SocialWeb web = new SocialWeb(() => s, () => c, n => n);
		GameDate d = new GameDate(8, "spring", 1);
		NpcSocialCandidate cand = new NpcSocialCandidate { First = "Abigail", Second = "Sebastian", Place = "Town", Theme = "small_kindness" };
		NpcSocialMoment m = web.Fallback(cand, d);
		web.Apply(m);
		NpcSocialEdge e = web.Find("Sebastian", "Abigail")!;
		Assert.Equal(1, e.Encounters);
		Assert.True(e.Closeness > 0);
		Assert.Contains("Abigail和Sebastian", SocialWeb.Describe(m, n => n));
	}

	[Fact]
	public void ChildrenAreNotPairedWithDwarfOrKrobus()
	{
		Assert.True(SocialWeb.Blocked("Jas", "Dwarf", "Mine", "Mine"));
		Assert.True(SocialWeb.Blocked("Vincent", "Krobus", "Sewer", "Sewer"));
	}
}

public class DiaryAndRetryTests
{
	[Fact]
	public void SecretTalksBecomeDiaryNotesAndFallback()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		PlayerDiary diary = new PlayerDiary(() => store);
		Assert.Equal("", diary.TodayFallback());
		diary.RecordSecret("阿比盖尔", "别告诉别人", "我保证");
		Assert.Single(diary.TodayNotes());
		Assert.Contains("阿比盖尔", diary.TodayFallback());
		diary.ClearTodayNotes();
		Assert.Empty(diary.TodayNotes());
	}

	[Fact]
	public void FallbackIsReplacedByTheRetriedEntry()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		PlayerDiary diary = new PlayerDiary(() => store);
		GameDate d = new GameDate(4, "winter", 2);
		diary.Add("兜底的话", d);
		Assert.True(diary.Replace("兜底的话", "AI 写的日记", d));
		Assert.Equal("AI 写的日记", diary.Find(d)!.Text);
	}

	[Fact]
	public void RetryIsOfferedOnlyAfterThatDay()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		DailyRetry retry = new DailyRetry(() => store);
		GameDate d = new GameDate(4, "winter", 2);
		retry.Begin(new PendingDailyAnalysis { Day = 4, Season = "winter", Year = 2, WithRumors = true, PlainJournal = "x" });
		Assert.Null(retry.RetryableOn(d));
		Assert.NotNull(retry.RetryableOn(new GameDate(5, "winter", 2)));
		retry.Complete(new GameDate(3, "winter", 2));
		Assert.NotNull(retry.Pending);
		retry.Complete(d);
		Assert.Null(retry.Pending);
	}

	[Fact]
	public void FestivalKeyMatchesGameData() => Assert.Equal("spring13", BirthdayRules.FestivalKey("Spring", 13));
}

public class SocialAiTests
{
	private static SocialAi Make(FakeSender ai) => new SocialAi(ai, new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance), () => "简体中文", n => n == "Abigail" ? "阿比盖尔" : n == "Kent" ? "肯特" : n, NullLog.Instance);

	private static DayInput Day(int year = 1) => new DayInput
	{
		Year = year,
		Interactions = new List<DailyInteraction> { new DailyInteraction { Npc = "Abigail", Farmer = "阿哲", Opener = "嗨", Reply = "一起去矿洞吗", Comeback = "好啊", Face = "happy" } }
	};

	[Fact]
	public async Task EmptyDaySendsNothing()
	{
		FakeSender ai = new FakeSender();
		Assert.Null(await Make(ai).AnalyzeDayAsync(new DayInput()));
		Assert.Empty(ai.Requests);
	}

	[Fact]
	public async Task AnalysisUsesDisplayNames()
	{
		FakeSender ai = new FakeSender().Reply("{\"worth_spreading\":true,\"fact\":\"Abigail 约农夫去矿洞\",\"rumor\":\"听说 Abigail 要去矿洞\",\"diary\":\"今天和 Abigail 聊了\",\"day_mood\":\"happy\"}");
		DayAnalysis a = (await Make(ai).AnalyzeDayAsync(Day()))!;
		Assert.Equal(SocialAi.DayRequest, ai.Requests.Single().Name);
		Assert.Equal("阿比盖尔 约农夫去矿洞", a.GossipMemory);
		Assert.Contains("阿比盖尔", a.ChatMessage);
		Assert.Contains("阿比盖尔", a.DiaryEntry);
	}

	[Fact]
	public async Task EachStoryIsItsOwnGossip()
	{
		FakeSender ai = new FakeSender().Reply("{\"worth_spreading\":true,\"tales\":[{\"fact\":\"Abigail 打翻了茶\",\"rumor\":\"☕ 茶翻了\",\"source\":\"\"},{\"fact\":\"潘姆说深夜看见两人走过\",\"rumor\":\"☕ 潘姆看见了\",\"source\":\"潘姆\"},{\"fact\":\"c\"},{\"fact\":\"d\"}],\"diary\":\"\",\"day_mood\":\"\"}");
		DayAnalysis a = (await Make(ai).AnalyzeDayAsync(Day(2)))!;
		Assert.True(a.HasGossip);
		Assert.Equal(SocialAi.MaxGossip, a.Gossip!.Count);
		Assert.Equal("阿比盖尔 打翻了茶", a.GossipMemory);
		Assert.Equal("潘姆", a.Gossip[1].ToldBy);
		Assert.Contains("每件事单独写一条", ai.Requests.Single().System);
	}

	[Fact]
	public async Task FirstYearKentIsDroppedFromGossip()
	{
		FakeSender ai = new FakeSender().Reply("{\"worth_spreading\":true,\"fact\":\"肯特在酒吧喝酒\",\"rumor\":\"肯特在酒吧喝酒\",\"diary\":\"\",\"day_mood\":\"\"}");
		DayAnalysis a = (await Make(ai).AnalyzeDayAsync(Day(1)))!;
		Assert.False(a.HasGossip);
		Assert.Equal("", a.GossipMemory);
	}

	[Fact]
	public async Task FailedRequestReturnsNull()
	{
		FakeSender ai = new FakeSender().Fail(AISV.Core.Ai.FailureKind.ServerError);
		Assert.Null(await Make(ai).AnalyzeDayAsync(Day()));
	}

	[Fact]
	public void HappeningsPreferGossipWorthyAndRecent()
	{
		List<NpcSocialMoment> list = new List<NpcSocialMoment>
		{
			new NpcSocialMoment { WhatHappened = "a", OnDay = 5 },
			new NpcSocialMoment { WhatHappened = "b", OnDay = 3, WorthTelling = true },
			new NpcSocialMoment { WhatHappened = "", OnDay = 9, WorthTelling = true },
			new NpcSocialMoment { WhatHappened = "c", OnDay = 4, WorthTelling = true }
		};
		Assert.Equal(new[] { "c", "b", "a" }, GossipFocus.SelectHappenings(list).Select(m => m.WhatHappened));
		Assert.Equal("c", GossipFocus.PickFallback(list)!.WhatHappened);
		Assert.Equal("☕ 有事", GossipFocus.FallbackChat("有事"));
	}
}
