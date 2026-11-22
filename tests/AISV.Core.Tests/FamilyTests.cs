using System.Linq;
using System.Collections.Generic;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using Xunit;

namespace AISV.Core.Tests;

public class FamilyTests
{
	private static KissRules.Situation Ready() => new KissRules.Situation(true, 10, true, 1200, false, false, false, false, false, 64, 0);

	[Fact]
	public void KissMenuFollowsVanillaKissRules()
	{
		Assert.True(KissRules.CanOffer(Ready()));
		Assert.False(KissRules.CanOffer(Ready() with { Hearts = 9 }));
		Assert.False(KissRules.CanOffer(Ready() with { SleptInBed = false }));
		Assert.False(KissRules.CanOffer(Ready() with { TimeOfDay = 2200 }));
		Assert.False(KissRules.CanOffer(Ready() with { Moving = true }));
		Assert.False(KissRules.CanOffer(Ready() with { KissedToday = true }));
		Assert.False(KissRules.CanOffer(Ready() with { HoldingItem = true }));
		Assert.False(KissRules.CanOffer(Ready() with { MarriedOrRoommate = false }));
		// 站在上下方原版不亲。
		Assert.False(KissRules.CanOffer(Ready() with { DeltaX = 10, DeltaY = 64 }));
		Assert.True(KissRules.CanOffer(Ready() with { DeltaX = -64, DeltaY = 20 }));
	}

	[Fact]
	public void PetThoughtsKeepTheirDistance()
	{
		Assert.True(PetThoughtRules.CanThink(0, 3, -1, 900));
		Assert.False(PetThoughtRules.CanThink(3, 3, 900, 1200));
		Assert.False(PetThoughtRules.CanThink(1, 3, 900, 950));
		Assert.True(PetThoughtRules.CanThink(1, 3, 950, 1050));
		Assert.Equal(24, PetThoughtRules.Emote("sleepy"));
		Assert.True(PetThoughtRules.Rolls(0.1, 25));
		Assert.False(PetThoughtRules.Rolls(0.3, 25));
	}

	[Fact]
	public void PetThoughtParsesJsonOrPlainText()
	{
		Assert.Equal(("hungry", "饭盆是空的……"), PetThoughtRules.Parse("好的 {\"mood\":\"hungry\",\"line\":\"饭盆是空的……\"}"));
		Assert.Equal(("happy", "主人回来啦"), PetThoughtRules.Parse("“主人回来啦”"));
		Assert.Null(PetThoughtRules.Parse("{\"mood\":\"happy\"}"));
		Assert.Null(PetThoughtRules.Parse(""));
		Assert.True(PetThoughtRules.Parse("{\"line\":\"" + new string('喵', 60) + "\"}")!.Value.Line.Length <= PetThoughtRules.MaxChars);
	}

	[Fact]
	public void OwnChildrenAreChildrenWhateverTheirGrowthStage()
	{
		try
		{
			ChildSafety.SetFamily(new[] { "Pip" });
			// 会走路的孩子 Age 是 3、新生儿是 0：都按孩子对待。
			Assert.True(ChildSafety.IsChild("Pip", 3));
			Assert.True(ChildSafety.IsChild("Pip", 0));
			Assert.False(ChildSafety.AgeAllowsRomance("Pip", 0, false));
			Assert.False(ChildSafety.IsChild("Abigail", 1));
		}
		finally
		{
			ChildSafety.SetFamily(new string[0]);
		}
	}

	[Fact]
	public void ChildPromptKeepsItChildlike()
	{
		ChildContext c = new ChildContext { Key = "1_Pip", Name = "Pip", Boy = true, Stage = "刚学会走路", ParentName = "小麦", ParentRole = "妈妈", Home = new[] { "家里只有你和妈妈。" }, Day = 5 };
		string system = ChildPrompts.System(c, new List<ChildTurn> { new ChildTurn { Day = 5, Who = "note", Text = "今天跑到农场上躲猫猫，被妈妈找到了" } }, opening: true);
		Assert.Contains("不谈恋爱", system);
		Assert.Contains("躲猫猫", system);
		Assert.Contains("Pip是妈妈小麦的孩子", system);
		ChildContext carded = new ChildContext { Name = "Pip", ParentRole = "妈妈", Card = new CharacterCard { Personality = "爱吃胡萝卜的小捣蛋", DisplayName = "皮皮" } };
		Assert.Contains("爱吃胡萝卜", ChildPrompts.Persona(carded));
		Assert.Equal("皮皮", ChildPrompts.Called(carded));
		Assert.Null(ChildPrompts.Clean("  \"\" "));
	}

	[Fact]
	public void RunawayOnlyOnQuietMornings()
	{
		RunawayRules.Day ok = new RunawayRules.Day(false, false, true, true, 20, 930);
		Assert.True(RunawayRules.MayRun(ok, true, 3, 0.01));
		Assert.False(RunawayRules.MayRun(ok, true, 3, 0.05));
		Assert.False(RunawayRules.MayRun(ok, false, 100, 0));
		Assert.False(RunawayRules.MayRun(ok with { Raining = true }, true, 100, 0));
		Assert.False(RunawayRules.MayRun(ok with { DaysSinceLast = 5 }, true, 100, 0));
		Assert.False(RunawayRules.MayRun(ok with { Time = 1300 }, true, 100, 0));
		Assert.False(RunawayRules.MayRun(ok with { PlayerHome = false }, true, 100, 0));
	}

	[Fact]
	public void RunawayHidesBehindThingsAndHintsTheWay()
	{
		Assert.Equal(0, RunawayRules.HideScore(10, 3));
		Assert.Equal(0, RunawayRules.HideScore(30, 0));
		Assert.True(RunawayRules.HideScore(30, 3) > RunawayRules.HideScore(30, 1));
		Assert.Equal("北", RunawayRules.Direction(0, -10));
		Assert.Equal("东南", RunawayRules.Direction(10, 10));
		Assert.Equal("西", RunawayRules.Direction(-10, 0));
		Assert.Equal(60, RunawayRules.FoundPoints(900, 1050));
		Assert.Equal(40, RunawayRules.FoundPoints(900, 1230));
		Assert.Equal(20, RunawayRules.FoundPoints(900, 1600));
	}

	[Fact]
	public void SalesAreRecordedOnceAndSummed()
	{
		AISV.Core.Storage.DocumentStore store = new AISV.Core.Storage.DocumentStore(new AISV.Core.Storage.MemoryBackend());
		AISV.Core.Store.StoreSaleBook book = new AISV.Core.Store.StoreSaleBook(() => store);
		int heard = 0;
		book.Recorded += _ => heard++;
		AISV.Core.Store.StoreSale sale = new AISV.Core.Store.StoreSale { TxId = "5-1", Buyer = "Emily", ItemName = "甜瓜", Qty = 2, UnitPrice = 300, Day = 5 };
		Assert.True(book.Record(sale));
		Assert.False(book.Record(new AISV.Core.Store.StoreSale { TxId = "5-1", Buyer = "Emily", Day = 5 }));
		Assert.True(book.Record(new AISV.Core.Store.StoreSale { Source = AISV.Core.Store.StoreSale.Cos, TxId = "5-1", Buyer = "Gus", ItemName = "鸡蛋", UnitPrice = 50, Day = 5 }));
		Assert.Equal(2, heard);
		Assert.Equal(600, book.Revenue(5, 7)[6]);
		Assert.Contains("甜瓜×2", AISV.Core.Store.StoreSaleBook.PromptBlock(book.By("Emily", 5, 7), "小麦"));
	}

	[Fact]
	public void CustomersPreferWhatTheyLoveAtFairPrices()
	{
		var offers = new List<AISV.Core.Store.Offer>
		{
			new AISV.Core.Store.Offer("(O)1", 100, 100, 0, 5, 8, false),
			new AISV.Core.Store.Offer("(O)2", 100, 100, 0, 5, 0, false),
			new AISV.Core.Store.Offer("(O)3", 900, 100, 0, 5, 0, false)
		};
		Assert.True(AISV.Core.Store.CustomerDemand.Score(offers[1], 500) > AISV.Core.Store.CustomerDemand.Score(offers[0], 500));
		Assert.Equal(0, AISV.Core.Store.CustomerDemand.Score(offers[2], 500));
		Assert.Equal(1, AISV.Core.Store.CustomerDemand.Pick(offers, 500, 0.9).Index);
		Assert.Equal(0, AISV.Core.Store.CustomerDemand.Arrivals(0, 1, 0.3, 0));
		Assert.True(AISV.Core.Store.CustomerDemand.Arrivals(8, 0.8, 0.4, 0) >= 1);
	}

	[Fact]
	public void RivalsSlideOneStepAtATime()
	{
		(double p0, double j0, double o0) = AISV.Core.Store.Competition.Shares(0, 1, 0, true);
		Assert.Equal(0, o0);
		(double p1, _, double o1) = AISV.Core.Store.Competition.Shares(12, 0.8, 30, true);
		Assert.True(o1 > 0.3 && p1 < p0);
		Assert.Equal(AISV.Core.Store.Competition.Tight, AISV.Core.Store.Competition.Health(0.1, 1, AISV.Core.Store.Competition.Normal));
		Assert.Equal(AISV.Core.Store.Competition.Closed, AISV.Core.Store.Competition.Health(0.45, 1, AISV.Core.Store.Competition.Closed));
		Assert.Equal(AISV.Core.Store.Competition.Losing, AISV.Core.Store.Competition.Health(0.7, 1, AISV.Core.Store.Competition.Closed));
		Assert.Equal((1100, 1500), AISV.Core.Store.Competition.Hours(900, 1700, AISV.Core.Store.Competition.Closed));
		Assert.False(AISV.Core.Store.Competition.MayConfront(AISV.Core.Store.Competition.Tight, -1, 5, 0));
		Assert.False(AISV.Core.Store.Competition.MayConfront(AISV.Core.Store.Competition.Losing, 5, 5, 0));
		Assert.True(AISV.Core.Store.Competition.MayConfront(AISV.Core.Store.Competition.Losing, 4, 5, 0.1));
	}

	[Fact]
	public void StolenGoodsAreRecognisedMostlyByTheirOwners()
	{
		Assert.Equal(0.15, AISV.Core.Store.StolenRules.Chance(true, false, 100, 100, 0), 3);
		Assert.Equal(0.01, AISV.Core.Store.StolenRules.Chance(false, false, 100, 100, 0), 3);
		Assert.True(AISV.Core.Store.StolenRules.Chance(false, true, 2000, 100, 0) > 0.15);
		Assert.True(AISV.Core.Store.StolenRules.Chance(true, false, 2000, 100, 0) <= 0.35);
		Assert.True(AISV.Core.Store.StolenRules.Chance(true, false, 100, 100, 28) < 0.1);
	}

	private static AISV.Core.Companions.SpousePlanInput Home(int hearts = 10, bool rain = false, bool festival = false, bool patio = false, int level = 1, params string[] requested) => new AISV.Core.Companions.SpousePlanInput
	{
		Hearts = hearts,
		Rain = rain,
		Festival = festival,
		PatioDay = patio,
		Level = level,
		Work = new Dictionary<string, int> { ["water"] = 30, ["animals"] = 4, ["clear"] = 0, ["harvest"] = 5 },
		Allowed = new HashSet<string>(AISV.Core.Companions.SpouseTasks.All),
		HasKids = true,
		HasShop = true,
		Gold = 500,
		SpendCap = 300,
		Requested = requested
	};

	[Fact]
	public void SpousePlansAroundWeatherAndMood()
	{
		var plan = AISV.Core.Companions.SpouseDayPlan.Build(Home());
		Assert.Equal(3, plan.Count);
		Assert.Equal("water", plan[0].Task);
		Assert.Equal(700, plan[0].Time);
		Assert.True(plan.Select(i => i.Time).SequenceEqual(plan.Select(i => i.Time).OrderBy(t => t)));
		Assert.DoesNotContain(AISV.Core.Companions.SpouseDayPlan.Build(Home(rain: true)), i => i.Task == "water");
		Assert.DoesNotContain(AISV.Core.Companions.SpouseDayPlan.Build(Home()), i => i.Task == "clear");
		Assert.Empty(AISV.Core.Companions.SpouseDayPlan.Build(Home(festival: true)));
		Assert.Empty(AISV.Core.Companions.SpouseDayPlan.Build(Home(hearts: 3)));
		Assert.All(AISV.Core.Companions.SpouseDayPlan.Build(Home(patio: true)), i => Assert.False(AISV.Core.Companions.SpouseTasks.IsChore(i.Task)));
		Assert.Equal(5, AISV.Core.Companions.SpouseDayPlan.Build(Home(level: 2)).Count);
		Assert.True(AISV.Core.Companions.SpouseDayPlan.Build(Home(level: 2)).Count(i => AISV.Core.Companions.SpouseDayPlan.IsOuting(i.Task)) <= 1);
	}

	[Fact]
	public void PlayerRequestsAlwaysGetPlannedAndAiOnlyReorders()
	{
		var plan = AISV.Core.Companions.SpouseDayPlan.Build(Home(hearts: 10, level: 0, requested: "hay"));
		Assert.Contains(plan, i => i.Task == "hay" && i.Requested);
		var cands = AISV.Core.Companions.SpouseDayPlan.Candidates(Home()).Select(c => c.Task).ToList();
		var ai = AISV.Core.Companions.SpouseDayPlan.ParseAi("好的 {\"order\":[\"kids\",\"fly\",\"water\"],\"say\":\"今天我带孩子玩\"}", cands);
		Assert.NotNull(ai);
		Assert.Equal(new[] { "kids", "water" }, ai!.Value.Order);
		Assert.Equal("今天我带孩子玩", ai.Value.Say);
		Assert.Null(AISV.Core.Companions.SpouseDayPlan.ParseAi("不知道", cands));
		var ordered = AISV.Core.Companions.SpouseDayPlan.Build(Home(level: 0), ai.Value.Order);
		Assert.Contains(ordered, i => i.Task == "kids");
	}

	[Fact]
	public void SpouseDaySummaryFeedsChat()
	{
		var log = new AISV.Core.Companions.SpouseDayLog { Spouse = "Leah" };
		log.Items.Add(new AISV.Core.Companions.SpousePlanItem { Task = "water", Time = 700, State = "done", Did = 12 });
		log.Items.Add(new AISV.Core.Companions.SpousePlanItem { Task = "visit", Time = 1300, State = "done", Note = "和Emily聊了会儿" });
		Assert.Equal("浇水（12）；去镇上找人聊天：和Emily聊了会儿", log.Summary());
		Assert.Contains("你今天的安排", log.PromptBlock("Leah", "小麦"));
		Assert.Equal("", log.PromptBlock("Abigail", "小麦"));
	}
}
