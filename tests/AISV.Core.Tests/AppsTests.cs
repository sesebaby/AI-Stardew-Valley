using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Core.Social.Web;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class AppsTests
{
	// ---- 信使搜索 ----

	[Fact]
	public void SearchSplitsOnBothSpacesAndMatchesEveryToken()
	{
		IReadOnlyList<string> tokens = MessengerRules.SearchTokens("  阿比　Cake ");
		Assert.Equal(2, tokens.Count);
		Assert.True(MessengerRules.MatchesSearch(tokens, "阿比盖尔", "I baked a cake"));
		Assert.False(MessengerRules.MatchesSearch(tokens, "阿比盖尔", "no dessert"));
		Assert.True(MessengerRules.MatchesSearch(Array.Empty<string>(), "anything"));
	}

	[Fact]
	public void SearchSpansMergeOverlaps()
	{
		IReadOnlyList<string> tokens = MessengerRules.SearchTokens("ab bc");
		List<(int Start, int Length)> spans = MessengerRules.SearchSpans("xabcx", tokens).ToList();
		Assert.Single(spans);
		Assert.Equal((1, 3), spans[0]);
	}

	// ---- 心情预报 ----

	[Theory]
	[InlineData("excited", 35, false, MoodSky.Sunny)]
	[InlineData("happy", 12, false, MoodSky.PartlyCloudy)]
	[InlineData("neutral", 0, false, MoodSky.Cloudy)]
	[InlineData("irritated", -15, false, MoodSky.Stormy)]
	[InlineData("melancholic", -40, false, MoodSky.Rainy)]
	[InlineData("neutral", 60, false, MoodSky.Rainbow)]
	[InlineData("excited", 0, true, MoodSky.Rainbow)]
	[InlineData("melancholic", -5, true, MoodSky.Rainy)]
	[InlineData("odd", -20, false, MoodSky.Stormy)]
	public void SkyFollowsMoodScoreAndBirthday(string mood, int score, bool birthday, MoodSky expected)
	{
		Assert.Equal(expected, MoodForecast.Sky(mood, score, birthday));
	}

	[Fact]
	public void VillageSkyCapsAtSunny()
	{
		Assert.Equal(MoodSky.Sunny, MoodForecast.Village(new[] { 80, 70 }));
		Assert.Equal(MoodSky.Cloudy, MoodForecast.Village(Array.Empty<int>()));
		Assert.Equal(MoodSky.Rainy, MoodForecast.Village(new[] { -40, -30 }));
	}

	[Fact]
	public void CausesReadTheDailyMoodPhrases()
	{
		IReadOnlyList<MoodCauseItem> causes = MoodForecast.Causes("受不了下雨天；不太喜欢冬天；星期一总提不起劲；今天过生日；最近碰上了好事；刚碰上了好事；刚碰上了好事");
		Assert.Equal(new[] { MoodCause.DislikesWeather, MoodCause.DislikesSeason, MoodCause.DislikesDay, MoodCause.Birthday, MoodCause.GoodEvent }, causes.Select(c => c.Kind));
		Assert.Equal(MoodCause.NormalDay, MoodForecast.Causes("平平常常的一天").Single().Kind);
		Assert.Equal(MoodCause.LikesWeather, MoodForecast.Causes("平平常常的一天；爱这种大晴天").Single().Kind);
	}

	[Fact]
	public void AdviceMatchesSkyAndCauses()
	{
		IReadOnlyList<MoodCauseItem> causes = MoodForecast.Causes("受不了下雨天；星期一总提不起劲");
		Assert.Equal(new[] { MoodAdvice.LovedGift, MoodAdvice.WeatherWillPass, MoodAdvice.TomorrowIsBetter }, MoodForecast.Advice(MoodSky.Rainy, causes));
		Assert.Equal(new[] { MoodAdvice.GoodDayToTalk }, MoodForecast.Advice(MoodSky.Sunny, causes));
		Assert.Equal(new[] { MoodAdvice.BirthdayGift }, MoodForecast.Advice(MoodSky.Rainbow, MoodForecast.Causes("今天过生日")));
		Assert.Equal(new[] { MoodAdvice.TreadLightly }, MoodForecast.Advice(MoodSky.Stormy, Array.Empty<MoodCauseItem>()));
		Assert.Equal(0.5f, MoodForecast.Fraction(0));
	}

	// ---- 年度回顾 ----

	private static YearFacts Facts() => new YearFacts(
		new[]
		{
			new TalkFact("Abigail", 1, 40), new TalkFact("Abigail", 1, 20), new TalkFact("Abigail", 1, 0),
			new TalkFact("Sam", 1, 10), new TalkFact("Sam", 2, 30)
		},
		new[] { new GiftFact("Abigail", "紫水晶", "loved", 1), new GiftFact("Sam", "紫水晶", "neutral", 1), new GiftFact("Abigail", "紫水晶", "loved", 1), new GiftFact("Sam", "粘土", "hated", 1) },
		new[] { new GossipFact("八卦一", "Pierre", 3, 1, 10), new GossipFact("八卦二", "Haley", 5, 1, 5), new GossipFact("没人知道", "Lewis", 0, 1, 20) },
		new[] { new ArcFact("故事", new[] { "Leah" }, 1, 30) },
		new[] { new MilestoneFact("Abigail", "first_conversation", 1, 2) },
		new[] { new LetterFact("Abigail", 1), new LetterFact("Sam", 1), new LetterFact("Abigail", 1) });

	[Fact]
	public void TrimmedTalksStillCountInTheYear()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ConversationLog log = new ConversationLog(() => store, () => 5);
		for (int i = 0; i < 12; i++)
		{
			log.Add("Haley", new ConversationEntry { Day = i + 1, Season = "spring", Year = 1, Farmer = "牛哥", HeartPoints = i % 2 == 0 ? 10 : 0 });
		}
		log.Add("Haley", new ConversationEntry { Day = 1, Season = "spring", Year = 2, Farmer = "牛哥", HeartPoints = 6 });
		Assert.Equal(5, log.Recent("Haley", int.MaxValue).Count);
		Assert.Equal(13, log.Count("Haley"));
		IReadOnlyList<(int Year, string PlayerName, int Points)> all = log.AllTalks("Haley");
		Assert.Equal(12, all.Count(t => t.Year == 1));
		Assert.Equal(60, all.Where(t => t.Year == 1).Sum(t => t.Points));
		YearFacts f = YearFacts.Empty with { Talks = all.Select(t => new TalkFact("Haley", t.Year, t.Points)).ToList() };
		Assert.Equal(12, YearReview.Summarize(f, 1).TalkCount);
	}

	[Fact]
	public void SummaryPicksTopsAndPages()
	{
		YearSummary s = YearReview.Summarize(Facts(), 1);
		Assert.Equal(4, s.TalkCount);
		Assert.Equal(2, s.People);
		Assert.Equal("Abigail", s.TopTalkers[0].Name);
		Assert.Equal(new TopGift("紫水晶", 3, "Abigail", 2), s.TopGift);
		Assert.Equal("八卦二", s.TopGossip!.Text);
		Assert.Equal(("Abigail", 2), (s.TopSender!.Name, s.TopSender.Count));
		Assert.Equal(2, s.LovedGifts);
		Assert.Equal(new[] { YearPage.Intro, YearPage.Talks, YearPage.Gifts, YearPage.Gossip, YearPage.Arcs, YearPage.Milestones, YearPage.Letters, YearPage.Reputation }, s.Pages);
	}

	[Fact]
	public void ReputationAndTitle()
	{
		YearSummary s = YearReview.Summarize(Facts(), 1);
		// 礼物分 (20+0+20-20)/4 = 5 → (5+20)/40 = 62.5；正分聊天 (40+20+10)/3；聊过 3 次以上的 1/2 人；神秘 100-4*0.45。
		Assert.Equal(62.5f, s.Reputation.Generosity, 3);
		Assert.Equal(70f / 3f, s.Reputation.Charisma, 3);
		Assert.Equal(50f, s.Reputation.Reliability, 3);
		Assert.Equal(98.2f, s.Reputation.Mystery, 3);
		Assert.Equal(YearTitle.Enigma, s.Title);
		Assert.Equal(YearTitle.FarmHermit, YearReview.PickTitle(new Reputation(50, 50, 50, 80), 0, 0));
		Assert.Equal(YearTitle.ValleySweetheart, YearReview.PickTitle(new Reputation(70, 70, 70, 10), 5, 5));
		Assert.Equal(YearTitle.QuietFarmer, YearReview.PickTitle(new Reputation(50, 50, 50, 60), 5, 5));
	}

	[Fact]
	public void YearsAndDefaultYear()
	{
		Assert.Equal(new[] { 1, 2, 3 }, YearReview.Years(Facts(), 3));
		Assert.Equal(2, YearReview.DefaultYear(Facts(), 3));
		Assert.Equal(1, YearReview.DefaultYear(Facts() with { Talks = Facts().Talks.Where(t => t.Year == 1).ToList() }, 3));
		Assert.Equal(1, YearReview.YearOf(112));
		Assert.Equal(2, YearReview.YearOf(113));
		Assert.Equal(new[] { 4, 3, 2, 1, 0 }, new[] { 80f, 60f, 40f, 20f, 19.9f }.Select(YearReview.Tier));
	}

	// ---- 村庄关系网 ----

	private static NpcSocialEdge Edge(string a, string b, float aff, float trust, float tension, int count = 5, float fam = 0.5f, params string[] tags) =>
		new NpcSocialEdge { First = a, Second = b, Liking = aff, Trust = trust, Friction = tension, Encounters = count, Closeness = fam, Tags = tags.ToList() };

	[Fact]
	public void BondsFollowTheThresholds()
	{
		MapPolicy p = new MapPolicy((a, b) => a == "Pierre" && b == "Caroline", (_, _) => true, false);
		float above(float line) => line + 0.05f;
		float below(float line) => line - 0.05f;
		Assert.Equal(Bond.Strangers, VillageMap.Classify(Edge("A", "B", 0.9f, 0.9f, 0, fam: below(BondLines.StrangerFamiliarity)), p));
		Assert.Equal(Bond.Strangers, VillageMap.Classify(Edge("A", "B", 0.9f, 0.9f, 1f, count: 0), p));
		Assert.Equal(Bond.Rivals, VillageMap.Classify(Edge("A", "B", 0.9f, 0.9f, above(BondLines.RivalTension)), p));
		Assert.Equal(Bond.Tense, VillageMap.Classify(Edge("A", "B", 0.1f, 0.1f, above(BondLines.RivalTension), count: BondLines.RivalEncounters - 1), p));
		Assert.Equal(Bond.Tense, VillageMap.Classify(Edge("Pierre", "Caroline", 0.1f, 0.1f, above(BondLines.RivalTension)), p));
		MapPolicy adult = p with { AdultDrama = true };
		Assert.Equal(Bond.Rivals, VillageMap.Classify(Edge("Pierre", "Caroline", 0.1f, 0.1f, above(BondLines.RivalTension)), adult));
		Assert.Equal(Bond.Couple, VillageMap.Classify(Edge("Pierre", "Caroline", 0.5f, 0.5f, below(BondLines.TenseTension)), p));
		Assert.Equal(Bond.Awkward, VillageMap.Classify(Edge("A", "B", below(BondLines.AwkwardAffinity), 0.5f, above(BondLines.AwkwardTension)), p));
		Assert.Equal(Bond.Crush, VillageMap.Classify(Edge("A", "B", above(BondLines.CrushAffinity), 0.1f, 0, tags: "secret_crush"), p));
		Assert.Equal(Bond.Friends, VillageMap.Classify(Edge("Jas", "B", above(BondLines.FriendlyLine), 0.1f, 0, tags: "secret_crush"), p));
		Assert.Equal(Bond.Friends, VillageMap.Classify(Edge("A", "B", above(BondLines.FriendlyLine), 0.1f, 0, tags: "secret_crush"), MapPolicy.Safe));
		NpcSocialEdge odd = Edge("A", "B", above(BondLines.CloseAffinity), above(BondLines.CloseTrust), 0, tags: "ODD_friendship");
		odd.Banter = above(BondLines.OddPlayfulness);
		Assert.Equal(Bond.OddFriends, VillageMap.Classify(odd, p));
		Assert.Equal(Bond.BestFriends, VillageMap.Classify(Edge("A", "B", above(BondLines.CloseAffinity), above(BondLines.CloseTrust), 0), p));
		Assert.Equal(Bond.Friends, VillageMap.Classify(Edge("A", "B", above(BondLines.CloseAffinity), below(BondLines.CloseTrust), 0), p));
		Assert.Equal(Bond.Acquaintances, VillageMap.Classify(Edge("A", "B", below(BondLines.FriendlyLine), below(BondLines.FriendlyLine), 0), p));
	}

	[Fact]
	public void RomanceNeedsAdultSwitchesForMarriedPeople()
	{
		Assert.True(VillageMap.CanShowRomance(true, true, false, false, false));
		Assert.False(VillageMap.CanShowRomance(true, true, true, true, false));
		Assert.True(VillageMap.CanShowRomance(true, true, true, true, true));
		Assert.False(VillageMap.CanShowRomance(false, true, false, true, true));
	}

	[Fact]
	public void BuildDedupesPairsAndPicksHighlights()
	{
		NpcSocialEdge[] edges =
		{
			Edge("Sam", "Abigail", 0.8f, 0.7f, 0, count: 3),
			Edge("Abigail", "Sam", 0.8f, 0.7f, 0, count: 9),
			Edge("Shane", "Pierre", 0.1f, 0.1f, 0.9f),
			Edge("Lewis", "Gus", 0.1f, 0.1f, 0, fam: 0.01f)
		};
		MapModel m = VillageMap.Build(edges, 100, MapPolicy.Safe);
		Assert.Equal(2, m.Ties.Count);
		Tie best = m.TopBonds.Single();
		Assert.Equal(("Abigail", "Sam", 9), (best.A, best.B, best.Encounters));
		Assert.Equal(Bond.Rivals, m.TopFeuds.Single().Bond);
		Assert.Equal(new[] { "Abigail", "Pierre", "Sam", "Shane" }, m.People);
		Assert.Equal("Sam", VillageMap.TiesOf(m, "Abigail").Single().Other("Abigail"));
	}

	[Fact]
	public void MemoriesSkipExpiredAndDuplicates()
	{
		List<NpcSocialMemory> list = new List<NpcSocialMemory>
		{
			new NpcSocialMemory { Text = "一起钓鱼", OnDay = 10, FadesAfterDay = 200 },
			new NpcSocialMemory { Text = "一起钓鱼", OnDay = 8 },
			new NpcSocialMemory { Text = "吵了一架", OnDay = 12, FadesAfterDay = 50 },
			new NpcSocialMemory { Text = "  ", OnDay = 15 }
		};
		IReadOnlyList<PairMemory> shown = VillageMap.RecentMemories(list, 100);
		Assert.Equal(new[] { "一起钓鱼" }, shown.Select(m => m.Summary));
		Assert.Equal(MemoryTone.Tense, VillageMap.ToneOf("Rivalry"));
		Assert.True(VillageMap.IsRomanceTag("first-date"));
		Assert.False(VillageMap.IsRomanceTag("updated"));
	}

	// ---- 八卦树 ----

	private static GossipWorld World() => new GossipWorld(
		new[] { "Abigail", "Caroline", "Pierre", "Sam", "Linus", "Lewis", "Robin" },
		new[] { ("Pierre", "Caroline"), ("Caroline", "Abigail") },
		n => n == "Linus",
		GossipTrail.DefaultFastSpreaders,
		n => n == "Abigail" ? "阿比盖尔" : n);

	[Fact]
	public void FirstCircleHasWitnessesMentionsAndFamily()
	{
		var first = GossipTrail.FirstCircle(new[] { "Pierre" }, "看见阿比盖尔和 Linus 在一起", World());
		Assert.Equal(new[] { ("Abigail", HeardHow.Mentioned), ("Caroline", HeardHow.Close), ("Pierre", HeardHow.Witness) }, first);
		var fallback = GossipTrail.FirstCircle(Array.Empty<string>(), "", World());
		Assert.All(fallback, f => Assert.Equal(HeardHow.Unclear, f.How));
	}

	[Fact]
	public void TrailSpreadsThroughFastTellersAndDrifts()
	{
		GossipStory story = new GossipStory("Pierre", new[] { "Pierre" }, "皮埃尔看见了什么", new[] { "Pierre", "Caroline", "Sam", "Lewis", "Linus" }, "Caroline", 10, 11, false);
		Trail t = GossipTrail.Build(story, World(), "Dramatic");
		Assert.Equal("Pierre", t.Root.Npc);
		Assert.Equal(5, t.KnownBy);
		TrailNode caroline = t.Find("Caroline")!;
		Assert.Equal(HeardHow.Close, caroline.How);
		Assert.Equal(0.24f, caroline.Drift, 3);
		TrailNode sam = t.Find("Sam")!;
		Assert.Equal(HeardHow.Spread, sam.How);
		Assert.Equal("Caroline", sam.Teller!.Npc);
		Assert.Equal(1, t.Steps);
		// Linus 不传八卦，只能挂在最后告诉别人的人下面。
		TrailNode linus = t.Find("Linus")!;
		Assert.Equal(HeardHow.Unclear, linus.How);
		Assert.Equal("Caroline", linus.Teller!.Npc);
		Assert.True(t.Approximate);
		Assert.Equal(new[] { "Pierre", "Caroline", "Sam" }, sam.PathFromRoot().Select(n => n.Npc));
		Assert.Equal(Fidelity.Faithful, GossipTrail.FidelityOf(0.2f));
		Assert.Equal(Fidelity.Stretched, GossipTrail.FidelityOf(0.35f));
		Assert.Equal(Fidelity.Distorted, GossipTrail.FidelityOf(0.65f));
	}

	[Fact]
	public void WitnessesStayFaithful()
	{
		GossipStory story = new GossipStory("Pierre", new[] { "Pierre", "Sam" }, "", new[] { "Pierre", "Sam" }, "", 10, 10, false);
		Trail t = GossipTrail.Build(story, World(), "Cozy");
		Assert.Equal(0f, t.Find("Sam")!.Drift);
		Assert.False(t.Approximate);
	}

	// ---- 评价 ----

	[Fact]
	public void StrangersGetThreeStars()
	{
		Review r = FarmerReviews.Build(new ReviewInput { Npc = "Krobus" });
		Assert.Equal(3, r.Stars);
		Assert.Equal("stranger.title", r.Title.Key);
		Assert.Null(r.Memory);
	}

	[Fact]
	public void StarsWeighHeartsGiftsAndHurts()
	{
		ReviewInput good = new ReviewInput { Npc = "Abigail", Hearts = 10, HasProfile = true, Trust = 0.9f, Warmth = 0.8f, Loved = 5, Talks = 20, PositivePoints = new[] { 80, 70 } };
		Assert.Equal(5, FarmerReviews.Stars(good));
		Assert.Equal(2, FarmerReviews.Stars(good with { Bound = Bound.Divorced }));
		ReviewInput bad = new ReviewInput { Npc = "Shane", Hearts = 1, Hated = 2, Talks = 3, BrokenPromises = 2, HurtfulTalks = 1 };
		Assert.Equal(1, FarmerReviews.Stars(bad));
	}

	[Fact]
	public void RemarksFollowTheirOrderAndLimit()
	{
		ReviewInput i = new ReviewInput { Npc = "Haley", Hearts = 6, Bound = Bound.Dating, HasProfile = true, Trust = 0.9f, Loved = 1, BestGift = "向日葵", BirthdayGifts = 1, Talks = 16, PositivePoints = new[] { 70 } };
		Review r = FarmerReviews.Build(i);
		Assert.Equal(new[] { "open." + r.Stars, "rel.dating", "gift.loved", "gift.birthday" }, r.Body.Select(l => l.Key));
		Assert.Equal("向日葵", r.Body[2].Values["item"]);
		ReviewAxes axes = FarmerReviews.Axes(i);
		Assert.Equal(new[] { "talk.great", "talk.often", "trust.high" }, FarmerReviews.Remarks(i, axes).Select(l => l.Key).Skip(3));
	}

	[Fact]
	public void AxesAreEmptyWithoutData()
	{
		ReviewAxes a = FarmerReviews.Axes(new ReviewInput { Npc = "X" });
		Assert.Null(a.Generosity);
		Assert.Null(a.Charisma);
		Assert.Null(a.Reliability);
		Assert.Equal(100f, a.Mystery);
		Assert.Equal(new[] { 2, 2, 2, 1, 0 }, FarmerReviews.StarFill(3.6f));
		Assert.Equal(new[] { 1, 0, 0, 1, 2 }, FarmerReviews.Distribution(new[] { 1, 4, 5, 5 }));
		Assert.InRange(FarmerReviews.HelpfulCount("Abigail", 4), 0, 27);
		Assert.Equal(FarmerReviews.HelpfulCount("Abigail", 4), FarmerReviews.HelpfulCount("abigail", 4));
	}

	[Fact]
	public void LongMemoriesAreShortened()
	{
		string text = new string('字', 200);
		string s = FarmerReviews.ShortenMemory("“" + text + "”");
		Assert.EndsWith("……", s);
		Assert.Equal(FarmerReviews.MaxMemoryLength + 2, s.Length);
	}

	[Fact]
	public void TrailFallsBackToSeedsAndTellsOneAtATimeWithoutFastSpreaders()
	{
		GossipWorld slow = new GossipWorld(new[] { "Lewis", "Robin", "Gus", "Pam", "Sam" }, Array.Empty<(string, string)>(), _ => false, Array.Empty<string>());
		GossipStory story = new GossipStory("", Array.Empty<string>(), "没人记得", new[] { "Lewis", "Robin", "Gus", "Pam", "Sam" }, "", 10, 10, false);
		Trail t = GossipTrail.Build(story, slow, "Balanced");
		Assert.Equal(HeardHow.Unclear, t.Find("Lewis")!.How);
		Assert.Equal(HeardHow.Unclear, t.Find("Robin")!.How);
		Assert.Equal(3, t.Steps);
		Assert.All(new[] { "Gus", "Pam", "Sam" }, n => Assert.Equal(HeardHow.Spread, t.Find(n)!.How));
		Assert.True(t.Approximate);
	}

	[Fact]
	public void OtherRemarks()
	{
		ReviewInput stingy = new ReviewInput { Npc = "Pam", Hearts = 3, Hated = 1, WorstGift = "粘土", Talks = 1, PositivePoints = new[] { 10 }, HasProfile = true, Trust = 0.2f };
		Assert.Equal(new[] { "gift.hated", "talk.awkward", "talk.rare", "trust.low" }, FarmerReviews.Remarks(stingy, FarmerReviews.Axes(stingy)).Select(l => l.Key));
		ReviewInput generous = new ReviewInput { Npc = "Gus", Hearts = 5, Loved = 6, Liked = 6, Talks = 5 };
		Assert.Equal(new[] { "gift.many" }, FarmerReviews.Remarks(generous, FarmerReviews.Axes(generous)).Select(l => l.Key));
		ReviewInput none = new ReviewInput { Npc = "Lewis", Hearts = 2, Talks = 5 };
		Assert.Equal(new[] { "gift.none" }, FarmerReviews.Remarks(none, FarmerReviews.Axes(none)).Select(l => l.Key));
	}

	// ---- 鹈鹕图 ----

	private static string Text(string key, int k) => key + k;

	[Fact]
	public void FeedSortsNewestFirstAndPromoLeadsTheDay()
	{
		PostMemory[] gossip = { new PostMemory(10, 10, "spring", 1, "八卦A", "Sam"), new PostMemory(12, 12, "spring", 1, "八卦B") };
		PostMemory[] diary = { new PostMemory(12, 12, "spring", 1, "日记") };
		PostMemory[] promo = { new PostMemory(12, 12, "spring", 1, "海报", "Emily", "a.png") };
		IReadOnlyList<PelicanPost> posts = PelicanFeed.Build(gossip, diary, promo, Text);
		Assert.Equal(new[] { PostSource.Promo, PostSource.Gossip, PostSource.Diary, PostSource.Gossip }, posts.Select(p => p.Source));
		Assert.Equal("Haley", posts[1].Npc);
		Assert.Equal("Haley", posts[2].Npc);
		Assert.StartsWith("diary", posts[2].Caption);
		Assert.Equal("promo.tag0", posts[0].Hashtag);
		Assert.Equal("a.png", posts[0].Image);
		Assert.All(posts, p => Assert.DoesNotContain(p.Comments, c => c.Npc == p.Npc));
		Assert.All(posts, p => Assert.Equal(2, p.Comments.Select(c => c.Npc).Distinct().Count()));
	}

	[Fact]
	public void FeedIsStableAndCleansText()
	{
		PostMemory[] gossip = { new PostMemory(5, 5, "fall", 1, "  有  空格\n和表情😀 ", "Sam") };
		PelicanPost a = PelicanFeed.Build(gossip, Array.Empty<PostMemory>(), Array.Empty<PostMemory>(), Text)[0];
		PelicanPost b = PelicanFeed.Build(gossip, Array.Empty<PostMemory>(), Array.Empty<PostMemory>(), Text)[0];
		Assert.Equal("有 空格 和表情", a.Caption);
		Assert.Equal((a.Likes, a.Hashtag, a.Expression), (b.Likes, b.Hashtag, b.Expression));
		Assert.InRange(a.Likes, 3, 142);
		Assert.Empty(PelicanFeed.Build(new[] { new PostMemory(1, 1, "spring", 1, "😀") }, Array.Empty<PostMemory>(), Array.Empty<PostMemory>(), Text));
		Assert.Equal(2, PelicanFeed.Step(0, -1, 3));
		Assert.Equal(PelicanFeed.CaptionLength, PelicanFeed.Excerpt(new string('长', 300), PelicanFeed.CaptionLength).Length);
	}
}
