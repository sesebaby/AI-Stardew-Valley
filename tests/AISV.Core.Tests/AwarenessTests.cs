using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Approach;
using AISV.Core.Awareness;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class AwarenessTests
{
	private static string RepoRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AISV.sln")))
		{
			dir = dir.Parent;
		}
		return dir!.FullName;
	}

	private static readonly Lazy<SightingCatalog> Catalog = new Lazy<SightingCatalog>(() =>
		SightingCatalog.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src/AISV.Mod/assets/awareness/catalog.json"))));

	private static Sighting Event(string type, string actor = "Farmer", string? subject = null, int day = 10, int time = 1200, params (string, string)[] details)
	{
		Sighting e = new Sighting
		{
			EventId = $"{day}:{time}:{type}:{Guid.NewGuid():N}",
			Act = Sighting.ActionFor(type),
			Doer = actor,
			Place = "Town",
			OnDay = day,
			Clock = time,
			Weight = Aggravators.Significance(Catalog.Value.Get(type).Severity)
		};
		e.Facts["actionId"] = type;
		e.Facts["locationDisplay"] = "镇上";
		e.Facts["halfLifeDays"] = Catalog.Value.Get(type).FadeDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (subject != null)
		{
			e.Facts["subject"] = subject;
		}
		foreach ((string k, string v) in details)
		{
			e.Facts[k] = v;
		}
		return e;
	}

	[Fact]
	public void BundledCatalogLoadsCleanly()
	{
		SightingCatalog c = Catalog.Value;
		Assert.Empty(c.Problems);
		Assert.Equal(35, c.Types.Count());
		Assert.Equal(4, c.Meters.Count());
		Assert.Equal(6, c.Get("bomb").Severity);
		Assert.Equal(-1, c.Get("explosion").ReachTiles);
		Assert.True(c.Get("bomb").ThroughWalls);
		Assert.False(c.Get("hurt").IsKnown);
		Assert.True(c.TryGetMeter("confront", out MeterDefinition m));
		Assert.Equal(new[] { 12.0, 26, 45 }, m.Steps);
		Assert.Equal(3, m.Flavors.Count);
		Assert.Contains("{farmer}", c.Reaction("hurt").Title);
		Assert.Equal(c.Reaction("default").Title, c.Reaction("stare").Title);
	}

	[Fact]
	public void PhraseTemplateHandlesGroupsCountsAndEscapes()
	{
		Dictionary<string, string> v = new Dictionary<string, string> { ["a"] = "甲", ["n"] = "3" };
		Func<string, string?> r = k => v.TryGetValue(k, out string? x) ? x : null;
		Assert.Equal("甲送了 3 份", PhraseTemplate.Parse("{a}送了[ {n?一|#} 份]").Render(r));
		Assert.Equal("甲", PhraseTemplate.Parse("{a}[，{missing}]").Render(r));
		Assert.Equal("甲备选", PhraseTemplate.Parse("{a}[{missing}|备选]").Render(r));
		Assert.Equal("[甲]", PhraseTemplate.Parse("[[{a}]]").Render(r));
		v["n"] = "1";
		Assert.Equal("甲", PhraseTemplate.Parse("{a}[{n?|#}]").Render(r));
		Assert.Equal("a, b", PhraseTemplate.Parse("a  ,  b").Render(r));
	}

	[Fact]
	public void SentencesTakeTheWitnessPointOfView()
	{
		SightingWriter writer = new SightingWriter(() => Catalog.Value);
		Sighting gift = Event("gift", subject: "Haley", details: new[] { ("item", "向日葵"), ("taste", "loved") });
		Witness haley = new Witness { Who = "Haley", FirstHand = true };
		Assert.Equal("你清清楚楚地看见Farmer把向日葵递给你，正中下怀", writer.Sentence(gift, haley));
		Witness far = new Witness { Who = "Alex", SightShare = 0.45, TilesAway = 11 };
		Assert.Equal("你好像看见Farmer把个什么东西递到Haley手里", writer.Sentence(gift, far));
		Witness glimpse = new Witness { Who = "Alex", SightShare = 0.25, ClearView = false };
		Assert.StartsWith("你瞥见一个像是Farmer的人递了样东西给Haley", writer.Sentence(gift, glimpse));
		Assert.Equal("Farmer送过Haley向日葵，正中下怀", writer.Past(gift));

		Sighting bomb = Event("bomb", details: ("count", "2"));
		Assert.Equal("隔着老远听见 2 声闷响，八成又是Farmer", writer.Sentence(bomb, new Witness { Who = "Lewis", SightShare = 0.5 }));
		Sighting sooty = Event("bomb", subject: "Haley", details: new[] { ("count", "1"), ("sooted", "Haley") });
		Assert.EndsWith("——这一下把你自己熏了一身黑", writer.Sentence(sooty, haley));
		Assert.DoesNotContain("熏", writer.Sentence(sooty, new Witness { Who = "Lewis", FirstHand = true }));

		Sighting room = Event("room", details: new[] { ("owners", "Haley,Emily"), ("stayMinutes", "40") });
		Assert.Equal("你清清楚楚地看见Farmer钻进了你们家的卧室，一待就是 40 分钟", writer.Sentence(room, new Witness { Who = "Emily", FirstHand = true }));
		Assert.Contains("Haley和Emily的卧室", writer.Sentence(room, new Witness { Who = "Lewis", SightShare = 1 }));

		Sighting talk = Event("talk", subject: "Abigail", details: new[] { ("secret", "true"), ("turns", "3") });
		Assert.Equal("你清清楚楚地看见Farmer和Abigail压着嗓子说悄悄话，好一阵都没散", writer.Sentence(talk, new Witness { Who = "Sam", SightShare = 1 }));
	}

	[Fact]
	public void TheftSentencesDependOnWhetherTheThiefWasCaught()
	{
		SightingWriter writer = new SightingWriter(() => Catalog.Value);
		Sighting quiet = Event("pickpocket", subject: "Pierre", details: new[] { ("caught", "false"), ("loot", "破旧钱包和 120g") });
		Assert.Equal("你清清楚楚地看见Farmer悄悄把手伸进Pierre的兜，摸走了破旧钱包和 120g", writer.Sentence(quiet, new Witness { Who = "Abigail", SightShare = 1 }));
		Sighting caught = Event("pickpocket", subject: "Pierre", details: new[] { ("caught", "true"), ("loot", "") });
		Assert.Equal("你清清楚楚地看见Farmer刚把手探向你的兜，手腕就被逮住了", writer.Sentence(caught, new Witness { Who = "Pierre", FirstHand = true }));
		Assert.Equal("Farmer摸Pierre的兜，被逮了个现行", writer.Past(caught));

		Sighting burglary = Event("burglary", details: new[] { ("caught", "false"), ("owners", "Pierre,Caroline"), ("loot", "钻石") });
		Assert.Equal("Farmer摸进Pierre和Caroline家偷过钻石", writer.Past(burglary));
		Sighting spotted = Event("burglary", subject: "Abigail", details: new[] { ("caught", "true"), ("owners", "Pierre,Caroline,Abigail") });
		Assert.Contains("被你撞破了", writer.Sentence(spotted, new Witness { Who = "Abigail", FirstHand = true }));
	}

	[Fact]
	public void MemoryAndGossipLinesReadNaturally()
	{
		SightingWriter writer = new SightingWriter(() => Catalog.Value);
		Sighting trash = Event("trash", day: 10, time: 900, details: ("owners", "Lewis"));
		trash.SeenBy.Add(new Witness { Who = "Lewis", FirstHand = true });
		trash.SeenBy.Add(new Witness { Who = "Pam", SightShare = 0.45 });
		Assert.Equal("- 昨天上午，在镇上：你清清楚楚地看见Farmer弯腰在你的垃圾桶里扒拉。", writer.MemoryLine(trash, trash.SeenBy[0], 11, 1000));
		Assert.Equal("上午9点，在镇上：Farmer扒过Lewis的垃圾桶。目击者：Lewis（看得清清楚楚）、Pam（觉得看见了）。", writer.GossipLine(trash));
		Assert.Equal("刚才", SightingText.Relative(5, 1200, 5, 1210));
		Assert.Equal("大约 30 分钟前", SightingText.Relative(5, 1200, 5, 1235));
		Assert.Equal("昨天夜里", SightingText.Relative(4, 2300, 5, 900));
		Assert.Equal("上周", SightingText.Relative(1, 900, 10, 900));
		Assert.Equal("凌晨2点", SightingText.Clock(2600));
	}

	[Fact]
	public void CertaintyFollowsConfidenceBands()
	{
		Assert.Equal(5, Certainty.Level(new Witness { FirstHand = true, SightShare = 0.1 }));
		Assert.Equal(4, Certainty.Level(new Witness { SightShare = 0.65 }));
		Assert.Equal(1, Certainty.Level(new Witness { SightShare = 0.65, ClearView = false }));
		Assert.Equal(3, Certainty.Level(new Witness { SightShare = 0.25 }, floor: 3));
		Assert.Equal(0.45, Certainty.VisibilityAt(12));
	}

	[Fact]
	public void RulesSetMultiplyAndReward()
	{
		EventDefinition consume = Catalog.Value.Get("consume");
		Dictionary<string, string> details = new Dictionary<string, string> { ["kind"] = "alcohol", ["alcohol"] = "true" };
		RuleOutcome gus = SightingRules.Evaluate(consume, Catalog.Value.GlobalRules, new RuleSituation { Witness = "Gus", Role = "vendor", Details = details });
		Assert.False(gus.Meters.ContainsKey("confront"));
		Assert.Equal(3, gus.Meters["humor"]);
		Assert.Equal("(O)395", gus.Reward!.Item);
		Assert.Equal(2, gus.Reward.CountFor(9));

		RuleOutcome penny = SightingRules.Evaluate(consume, Catalog.Value.GlobalRules, new RuleSituation { Witness = "Penny", Details = details, NearbyOfAge = (age, tiles) => age == "child" && tiles == 6 ? new[] { "Jas", "Vincent" } : Array.Empty<string>() });
		Assert.Equal(6, penny.Meters["confront"]);
		Assert.False(penny.Meters.ContainsKey("humor"));
		Assert.Equal("2", penny.AddedDetails["nearbychildCount"]);

		RuleOutcome food = SightingRules.Evaluate(consume, Catalog.Value.GlobalRules, new RuleSituation { Witness = "Alex", Details = new Dictionary<string, string> { ["kind"] = "food" } });
		Assert.Empty(food.Meters);

		RuleOutcome rejected = SightingRules.Evaluate(Catalog.Value.Get("dating_proposal"), Catalog.Value.GlobalRules, new RuleSituation { Witness = "Haley", Role = "target", Details = new Dictionary<string, string> { ["result"] = "rejected" } });
		Assert.True(rejected.Silent);

		Dictionary<string, string> agg = new Dictionary<string, string> { ["aggravators"] = "giftPartnerNearby" };
		EventDefinition gift = Catalog.Value.Get("gift");
		Assert.Equal(gift.Severity * Aggravators.Factors["giftPartnerNearby"] * Aggravators.Factors["lateNight"], Aggravators.Grade(gift, agg, 2300), 6);
		Assert.True(Aggravators.Grade(gift, agg, 2300) > gift.Severity);
		Assert.All(Aggravators.Factors.Values, f => Assert.True(f > 1));
		Assert.Contains("lateNight", agg["aggravators"]);
	}

	[Fact]
	public void FeelingsClimbTiersLockAndCarryGrudges()
	{
		Feelings f = new Feelings(() => Catalog.Value);
		Dictionary<string, double> w = new Dictionary<string, double> { ["confront"] = 1, ["humor"] = 1 };
		IReadOnlyList<FeelingSignal> s1 = f.Feel(new FeelingInput("Lewis", "e1", "consume", w, 16, 5, false, null), 1000);
		FeelingSignal confront = Assert.Single(s1);
		Assert.Equal(1, confront.Tier);
		// 第一个升档的计量锁定了：同类事不再喂 humor。
		f.Feel(new FeelingInput("Lewis", "e2", "consume", w, 20, 5, false, null), 1000);
		Assert.Equal(0, f.ReadingOf("Lewis", "humor", 1000), 6);
		// 同一件事只算增量。
		Assert.Empty(f.Feel(new FeelingInput("Lewis", "e2", "consume", w, 20, 5, false, null), 1000));
		// 衰减：4 小时半衰期。
		Assert.Equal(36 * Math.Pow(0.5, 0.75), f.ReadingOf("Lewis", "confront", 1180), 6);

		f.MarkSpoken("Lewis", "confront", 3, 1000);
		Assert.Single(f.Grudges());
		f.OnDayStarted(2440, "2");
		Assert.True(f.ReadingOf("Lewis", "confront", 2440) > 5);
		Assert.Empty(f.Readings("Lewis", 2440).Where(r => r.ActedTier > 0));
	}

	[Fact]
	public void LowCertaintyBecomesAnAsideThatExpires()
	{
		Feelings f = new Feelings(() => Catalog.Value);
		IReadOnlyList<FeelingSignal> s = f.Feel(new FeelingInput("Pam", "e1", "trash", new Dictionary<string, double> { ["confront"] = 1 }, 20, 2, false, null), 500);
		Assert.Empty(s);
		FeelingAside? aside = f.TakeAside("Pam", 600, out bool first);
		Assert.NotNull(aside);
		Assert.True(first);
		Assert.NotNull(f.TakeAside("Pam", 650, out first));
		Assert.False(first);
		Assert.Null(f.TakeAside("Pam", 700, out _));
	}

	[Fact]
	public void MemoryKeepsTrustedWitnessesAndForgets()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		AwarenessMemory memory = new AwarenessMemory(() => store);
		Sighting e = Event("gift", subject: "Haley", day: 10);
		Assert.True(memory.Record(e, new[] { new Witness { Who = "Haley", FirstHand = true }, new Witness { Who = "Lewis", SightShare = 0.1 } }));
		Assert.False(memory.Record(e, new[] { new Witness { Who = "Haley", FirstHand = true } }));
		Assert.Single(memory.Events[0].SeenBy);
		Assert.Single(memory.MemoriesOf("haley", 10));
		Assert.Empty(memory.MemoriesOf("Lewis", 10));
		memory.OnDayStarted(70);
		Assert.Empty(memory.Events);

		Sighting map = Event("talk", day: 20);
		map.Facts.Remove("halfLifeDays");
		memory.Record(map, new[] { new Witness { Who = "Sam", SightShare = 1 } });
		memory.OnDayStarted(28);
		Assert.Empty(memory.Events);
	}

	[Fact]
	public void SavedFileReadsBackIgnoringCase()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write(AwarenessMemory.Path, "{\"schema\":1,\"data\":{\"Events\":[{\"EventId\":\"9:1200:bomb:x\",\"Act\":\"Explosion\",\"Doer\":\"Farmer\",\"Place\":\"Town\",\"OnDay\":9,\"Clock\":1200,\"Weight\":0.7,\"FirstHand\":true,\"Facts\":{\"actionId\":\"bomb\",\"halfLifeDays\":\"14\"},\"SeenBy\":[{\"Who\":\"Lewis\",\"SightShare\":1,\"TilesAway\":3,\"FirstHand\":false,\"ClearView\":true,\"Focus\":1,\"Repeats\":1,\"Sureness\":4,\"Part\":\"bystander\",\"Gauges\":{\"confront\":2.5},\"FiredRules\":[],\"Quiet\":false}]}],\"Grudges\":[{\"Who\":\"Lewis\",\"MeterId\":\"confront\",\"Amount\":3}]}}");
		AwarenessMemory memory = new AwarenessMemory(() => new DocumentStore(backend));
		Sighting e = Assert.Single(memory.Events);
		Assert.Equal("bomb", e.Type);
		Assert.Equal(4, e.SeenBy[0].Sureness);
		Assert.Equal(2.5, e.SeenBy[0].Gauges["CONFRONT"]);
		Assert.Equal("bomb", e.Detail("ACTIONID"));
		Assert.Single(memory.Grudges);
	}

	[Fact]
	public void GossipOfTheDayPrefersVariety()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		AwarenessMemory memory = new AwarenessMemory(() => store);
		foreach (string t in new[] { "bomb", "bomb", "trash", "talk" })
		{
			memory.Record(Event(t, day: 5), new[] { new Witness { Who = "Lewis", SightShare = 1 } });
		}
		IReadOnlyList<Sighting> top = memory.GossipOfTheDay(5, 3, new HashSet<string> { "talk" });
		Assert.Equal(new[] { "bomb", "trash", "bomb" }, top.Select(e => e.Type));
	}

	[Fact]
	public void PromptBlockFitsTheBudget()
	{
		SightingWriter writer = new SightingWriter(() => Catalog.Value);
		List<AwarenessMemoryItem> items = Enumerable.Range(0, 40).Select(i =>
		{
			Sighting e = Event("bomb", day: 10, time: 600 + i * 10);
			Witness w = new Witness { Who = "Lewis", SightShare = 1 };
			return new AwarenessMemoryItem(e, w, 1 - i / 100.0, 0);
		}).ToList();
		string block = AwarenessPrompts.MemoryBlock(items, writer, 10, 1200, "Farmer", "【心事】");
		Assert.True(block.Length <= AwarenessPrompts.MaxBlockChars + 10);
		Assert.StartsWith("【心事】", block);
		Assert.EndsWith("就别自己扯过来。", block);
		Assert.Equal("", AwarenessPrompts.MemoryBlock(Array.Empty<AwarenessMemoryItem>(), writer, 10, 1200, "Farmer"));
	}

	[Fact]
	public void TheSameSightingIsWrittenOnce()
	{
		SightingWriter writer = new SightingWriter(() => Catalog.Value);
		Witness w = new Witness { Who = "Haley", SightShare = 1 };
		List<AwarenessMemoryItem> items = Enumerable.Range(0, 6).Select(i => new AwarenessMemoryItem(Event("stare", day: 8, time: 2300), w, 0.9 - i / 100.0, 2)).ToList();
		items.Add(new AwarenessMemoryItem(Event("bomb", day: 8, time: 1000), w, 0.5, 2));
		string block = AwarenessPrompts.MemoryBlock(items, writer, 10, 1200, "Farmer");
		string[] lines = block.Split('\n').Where(l => l.StartsWith("- ")).ToArray();
		Assert.Equal(2, lines.Length);
		Assert.Equal(lines.Length, lines.Distinct().Count());
		Assert.Equal(AwarenessMemory.MaxContextMemories, AwarenessPrompts.MemoryBlock(Enumerable.Range(0, 20).Select(i => new AwarenessMemoryItem(Event("bomb", day: 10 - i), w, 1 - i / 100.0, i)).ToList(), writer, 10, 1200, "Farmer").Split('\n').Count(l => l.StartsWith("- ")));
	}

	[Fact]
	public void ConsequencesMatchTheTable()
	{
		Assert.Equal(-40, FeelingConsequences.FriendshipDelta("confront", 3, false));
		Assert.Equal(0, FeelingConsequences.FriendshipDelta("love", 3, true));
		Assert.Equal(8, FeelingConsequences.FriendshipDelta("humor", 3, false));
		Assert.Equal(-12, FeelingConsequences.MoodDelta("confront", 2, false));
		Assert.True(FeelingConsequences.EarnsColdShoulder("confront", 3, 3));
		Assert.True(FeelingConsequences.ShouldColdShoulder(100, 160, false));
		Assert.False(FeelingConsequences.ShouldColdShoulder(100, 161, false));
		Assert.Equal(3, FeelingConsequences.ColdShoulderLine(0.99));
	}

	[Fact]
	public void BubblesPickTheTargetAndRespectCooldowns()
	{
		BubbleDefinition b = Catalog.Value.Get("water_npc").Callout!;
		Bubbles bubbles = new Bubbles(new Random(1));
		List<BubbleCandidate> c = new List<BubbleCandidate>
		{
			new BubbleCandidate("Alex", "bystander", 5, 1, "friend", false),
			new BubbleCandidate("Haley", "target", 5, 3, "spouse", false)
		};
		BubbleChoice? first = bubbles.Choose("water_npc", b, c, 0, k => k, (_, t) => t == "farmer" ? "阿哲" : null);
		Assert.Equal("Haley", first!.Npc);
		Assert.Contains(first.Text, b.LinesFor("spouse").Select(l => l.Replace("{farmer}", "阿哲")));
		Assert.Null(bubbles.Choose("water_npc", b, c, 100, k => k, (_, _) => null));
		Assert.NotNull(bubbles.Choose("water_npc", b, c, 900, k => k, (_, _) => null));
	}

	[Fact]
	public void ApproachQueueDedupesEvictsAndCoolsDown()
	{
		ApproachQueue q = new ApproachQueue(maxPending: 2, cooldownTicks: 300);
		Assert.Equal(EnqueueResult.Accepted, q.Enqueue(new ApproachRequest("a", "Lewis", "k1", 0.5, 2, 100, 0, 7200), 0));
		Assert.Equal(EnqueueResult.RejectedLowerPriority, q.Enqueue(new ApproachRequest("b", "Lewis", "k1", 0.4, 2, 100, 0, 7200), 0));
		Assert.Equal(EnqueueResult.Replaced, q.Enqueue(new ApproachRequest("c", "Lewis", "k1", 0.9, 2, 100, 0, 7200), 0));
		Assert.Equal(EnqueueResult.Accepted, q.Enqueue(new ApproachRequest("d", "Pam", "k2", 0.5, 2, 100, 0, 7200), 0));
		Assert.Equal(EnqueueResult.RejectedFull, q.Enqueue(new ApproachRequest("e", "Gus", "k3", 0.1, 2, 100, 0, 7200), 0));
		(ApproachEntry replaced, ApproachOutcome outcome) = Assert.Single(q.TakeFinished());
		Assert.Equal("a", replaced.Request.RequestId);
		Assert.Equal(ApproachOutcome.Aborted, outcome);

		Assert.True(q.MarkDialogueReady("c", "嘿！", 1));
		Assert.NotNull(q.TryBegin(1, "c"));
		Assert.True(q.MarkArrived("c"));
		q.MarkPresented("c");
		Assert.True(q.MarkSpeechDone("c"));
		Assert.True(q.MarkReturned("c"));
		Assert.True(q.MarkRestored("c"));
		Assert.Equal(ApproachOutcome.Spoken, Assert.Single(q.TakeFinished()).Outcome);
		Assert.True(q.MarkDialogueReady("d", "哎", 2));
		Assert.Null(q.TryBegin(100, "d"));
		Assert.NotNull(q.TryBegin(301, "d"));
		Assert.Equal(0, q.Expire(99999));
	}

	[Fact]
	public void NavigationRepathsAndTimesOut()
	{
		ApproachNavigation nav = new ApproachNavigation();
		Assert.Equal(ApproachNavigation.Decision.StartPath, nav.Evaluate(0, (0, 0), (5, 0), false, null));
		Assert.Equal(ApproachNavigation.Decision.Wait, nav.Evaluate(10, (1, 0), (5, 0), true, (5, 0)));
		Assert.Equal(ApproachNavigation.Decision.Arrived, nav.Evaluate(20, (5, 0), (5, 0), true, (5, 0)));
		ApproachNavigation slow = new ApproachNavigation();
		slow.Evaluate(0, (0, 0), (5, 0), false, null);
		Assert.Equal(ApproachNavigation.Decision.Failed, slow.Evaluate(ApproachNavigation.TimeoutTicks, (0, 0), (5, 0), true, (5, 0)));
		Assert.Equal((1, 0), ApproachNavigation.AdjacentTile((0, 0), (3, 0), _ => true));
		Assert.Null(ApproachNavigation.AdjacentTile((0, 0), (3, 0), _ => false));
	}

	[Fact]
	public void SightlinesMatchFacingAndBehind()
	{
		Assert.True(Sightlines.IsFacing(5, 5, 1, 7, 5, 2.2f));
		Assert.False(Sightlines.IsFacing(5, 5, 3, 7, 5, 2.2f));
		Assert.False(Sightlines.IsFacing(5, 5, 1, 8, 5, 2.2f));
		Assert.False(Sightlines.IsFacing(5, 5, 1, 7, 7, 2.2f));
		Assert.True(Sightlines.IsBehind(5, 5, 1, 3, 5));
		Assert.False(Sightlines.IsBehind(5, 5, 1, 7, 5));
		Assert.False(Sightlines.IsBehind(5, 5, 1, 5, 5));
		Assert.False(Sightlines.IsBehind(5, 5, 1, -2, 5));
	}

	[Fact]
	public void FollowTrackerWarnsThenConfrontsAndForgivesShortGaps()
	{
		FollowTracker t = new FollowTracker();
		FollowSignal last = FollowSignal.None;
		for (int i = 0; i < 180; i++)
		{
			last = t.Observe("Sam", true);
		}
		Assert.Equal(FollowSignal.Warning, last);
		Assert.True(t.HasWarned);
		for (int i = 0; i < 90; i++)
		{
			Assert.Equal(FollowSignal.None, t.Observe("Sam", false));
		}
		Assert.True(t.IsTracking);
		for (int i = 0; i < 180; i++)
		{
			last = t.Observe("Sam", true);
		}
		Assert.Equal(FollowSignal.Confront, last);
		Assert.False(t.IsTracking);
		t.Observe("Sam", true);
		for (int i = 0; i < 91; i++)
		{
			t.Observe("Sam", false);
		}
		Assert.False(t.IsTracking);
	}

	[Fact]
	public void ClipFlattensAndCutsAtSensiblePlaces()
	{
		Assert.Equal("", SightingText.Clip(null, 10));
		Assert.Equal("", SightingText.Clip(" \n\t ", 10));
		Assert.Equal("a b c", SightingText.Clip(" a\t\nb\u0001  c ", 10));
		Assert.Equal("hello big…", SightingText.Clip("hello big wide world", 12));
		Assert.Equal("averylongwo…", SightingText.Clip("a verylongword indeed", 12).Replace("a very", "avery", StringComparison.Ordinal));
		Assert.Equal("今天天气真好…", SightingText.Clip("今天天气真好，我们一起去海边走走吧", 7));
		Assert.Equal("今天天气真好", SightingText.Clip("今天天气真好", 6));
	}

	[Fact]
	public void HurtPolicyDependsOnHeartsAndConditions()
	{
		Assert.Equal(HurtCondition.None, HurtPolicy.ConditionOf(100, 100, 270, 270));
		Assert.Equal(HurtCondition.None, HurtPolicy.ConditionOf(70, 100, 190, 270));
		Assert.Equal(HurtCondition.Hurt, HurtPolicy.ConditionOf(5, 100, 270, 270));
		Assert.Equal(HurtCondition.Exhausted, HurtPolicy.ConditionOf(100, 100, 10, 270));
		Assert.Equal(HurtCondition.Hurt, HurtPolicy.ConditionOf(5, 100, 10, 270));
		Assert.Equal(HurtCondition.Exhausted, HurtPolicy.ConditionOf(0, 100, 10, 270));
		Assert.Equal(HurtCondition.None, HurtPolicy.ConditionOf(0, 100, 270, 270));
		Assert.Equal(HurtCondition.None, HurtPolicy.ConditionOf(5, 0, 10, 0));
		int edge = 1;
		while (HurtPolicy.ConditionOf(edge + 1, 100, 270, 270) == HurtCondition.Hurt)
		{
			edge++;
		}
		Assert.Equal(HurtCondition.Exhausted, HurtPolicy.ConditionOf(100, 100, edge, 100));
		Assert.Equal(HurtCondition.None, HurtPolicy.ConditionOf(100, 100, edge + 1, 100));
		Assert.Equal(HurtResponse.Help, HurtPolicy.ResponseOf(4, false, true));
		Assert.Equal(HurtResponse.Comment, HurtPolicy.ResponseOf(2, false, false));
		Assert.Equal(HurtResponse.None, HurtPolicy.ResponseOf(3, false, true));
		Assert.Equal(HurtResponse.None, HurtPolicy.ResponseOf(10, true, false));
		Assert.Equal(HurtPolicy.ItemFor("Abigail"), HurtPolicy.ItemFor("abigail"));
		Assert.NotEqual(HurtPolicy.DefaultItem, HurtPolicy.ItemFor("Abigail"));
		Assert.StartsWith("(O)", HurtPolicy.ItemFor("Sam"));
		Assert.StartsWith("(O)", HurtPolicy.DefaultItem);
		Assert.Equal(HurtPolicy.DefaultItem, HurtPolicy.ItemFor(null));
		Assert.All(AISV.Core.Persona.BuiltInPersonas.VanillaNames, n => Assert.NotEqual(HurtPolicy.DefaultItem, HurtPolicy.ItemFor(n)));
		Assert.Equal(HurtPolicy.DefaultItem, HurtPolicy.ItemFor("Nobody"));
	}

	[Fact]
	public void FishingInvitationsRollOncePerSession()
	{
		FishingInvitations f = new FishingInvitations();
		int rolls = 0;
		Assert.False(f.CanTry("1", "Sam", 0, 50, () => { rolls++; return 99; }));
		Assert.False(f.CanTry("1", "Sam", 500, 50, () => { rolls++; return 0; }));
		Assert.Equal(1, rolls);
		Assert.True(f.CanTry("2", "Sam", 0, 50, () => 0));
		Assert.False(f.CanTry("2", "Sam", 30, 50, () => 0));
		Assert.True(f.CanTry("2", "Sam", 60, 50, () => 0));
		Assert.False(f.CanTry("3", "Sam", 0, 0, () => 0));
		Assert.True(f.CanTry("4", "Sam", 0, 100, () => 99));
	}

	[Fact]
	public void WaterBodyFindsShoreAndDeepestCast()
	{
		bool Water(int x, int y) => x >= 3 && x <= 9 && y >= 3 && y <= 9;
		WaterBody? body = WaterBody.Discover((5, 5), 13, 13, Water, 1000);
		Assert.NotNull(body);
		Assert.Contains((2, 5), body!.Shore);
		Assert.DoesNotContain((5, 5), body.Shore);
		Assert.True(body.TryCast((2, 6), 4, out (int X, int Y) cast, out int depth));
		Assert.Equal((6, 6), cast);
		Assert.Equal(4, depth);
		Assert.False(body.TryCast((0, 0), 4, out _, out _));
		Assert.Null(WaterBody.Discover((5, 5), 13, 13, Water, 10));
		Assert.Null(WaterBody.Discover((0, 0), 13, 13, Water, 1000));
	}

	[Fact]
	public void BystanderRaisesAFreshSightingOnce()
	{
		BystanderHooks hooks = new BystanderHooks();
		Witness haley = new Witness { Who = "Haley" };
		Sighting gift = Event("gift", actor: "牛哥", subject: "Vincent", day: 10, time: 1600);
		Sighting small = Event("talk", actor: "牛哥", day: 10, time: 1610);
		Sighting old = Event("gift", actor: "牛哥", subject: "Jas", day: 10, time: 1000);
		AwarenessMemoryItem[] seen =
		{
			new AwarenessMemoryItem(small, haley, 0.9, 0),
			new AwarenessMemoryItem(old, haley, 0.9, 0),
			new AwarenessMemoryItem(gift, haley, 0.31, 0)
		};
		Func<Sighting, bool> smallTalk = e => e.Type == "talk";

		Assert.Same(gift, hooks.Pick("Haley", seen, "牛哥", 10, 1640, smallTalk)!.Event);
		Assert.Null(hooks.Pick("Haley", seen, "牛哥", 10, 1650, smallTalk));

		BystanderHooks walked = new BystanderHooks();
		walked.MarkRaised("Haley", gift.EventId);
		Assert.Null(walked.Pick("Haley", seen, "牛哥", 10, 1640, smallTalk));
		Assert.Null(new BystanderHooks().Pick("Haley", seen, "牛哥", 11, 1640, smallTalk));
		Assert.Null(new BystanderHooks().Pick("Haley", new[] { new AwarenessMemoryItem(gift, haley, 0.1, 0) }, "牛哥", 10, 1640, smallTalk));
		Assert.Contains("头一回跟农夫当面说上话", BystanderHooks.LeadBlock("- 刚才：你看见牛哥送了文森特一份糖。"));
	}
}

public class SightLineTests
{
	[Fact]
	public void WallsAndRangeBlockTheView()
	{
		Assert.True(SightLine.Clear(0, 0, 5, 0, 12, (_, _) => true));
		Assert.False(SightLine.Clear(0, 0, 5, 0, 12, (x, y) => x != 3));
		Assert.False(SightLine.Clear(0, 0, 20, 0, 12, (_, _) => true));
		Assert.True(SightLine.Clear(0, 0, 3, 0, 12, (x, y) => !(x == 0 && y == 0) && !(x == 3 && y == 0)));
		Assert.False(SightLine.Clear(0, 0, 1, 1, 12, (x, y) => !(x == 1 && y == 0)));
	}
}
