using System.Linq;
using AISV.Core.World.Errands;
using Xunit;

namespace AISV.Core.Tests;

public class ErrandTests
{
	[Fact]
	public void QuoteAddsAFeeAndSkillHelps()
	{
		ErrandQuote q = ErrandRules.Quote(ErrandKinds.Fetch, FetchSources.Forage, 40, 5, false, 3, 4, skilled: false, 0);
		Assert.Equal(200, q.Value);
		Assert.Equal(50, q.Fee);
		Assert.Equal(250, q.DefaultReward);
		Assert.Equal(0, q.Goods);
		Assert.Equal(1, q.DaysNeeded);
		Assert.Equal(85, q.Chance);

		ErrandQuote skilled = ErrandRules.Quote(ErrandKinds.Fetch, FetchSources.Forage, 40, 5, false, 3, 4, skilled: true, 0);
		Assert.Equal(40, skilled.Fee);
		Assert.Equal(95, skilled.Chance);
		Assert.Equal(120, q.MinReward);
		Assert.Equal(750, q.MaxReward);
	}

	[Fact]
	public void BuyingPaysForTheGoodsSeparately()
	{
		ErrandQuote q = ErrandRules.Quote(ErrandKinds.Buy, "", 50, 10, false, 2, 3, false, 0);
		Assert.Equal(500, q.Goods);
		Assert.Equal(50, q.Fee);
		Assert.Equal(50, q.DefaultReward);
		Assert.Equal(550, q.Total(q.DefaultReward));
		Assert.Equal(30, ErrandRules.Quote(ErrandKinds.Relay, "", 0, 1, false, 1, 3, false, 0).DefaultReward);
	}

	[Fact]
	public void TightDeadlinesAndStrangersLowerTheChance()
	{
		Assert.Equal(3, ErrandRules.DaysNeeded(ErrandKinds.Fetch, FetchSources.Mine, 20, 30, 120, 0, false));
		Assert.Equal(4, ErrandRules.DaysNeeded(ErrandKinds.Fetch, FetchSources.Mine, 20, 30, 120, 80, false));
		Assert.Equal(3, ErrandRules.DaysNeeded(ErrandKinds.Fetch, FetchSources.Mine, 20, 30, 120, 80, true));
		Assert.Equal(35, ErrandRules.Chance(ErrandKinds.Fetch, 3, 1, 5, 30, 5, false));
		Assert.Equal(65, ErrandRules.Chance(ErrandKinds.Fetch, 1, 1, 20, 30, 0, false));
		Assert.Equal(5, ErrandRules.Chance(ErrandKinds.Fetch, 4, 1, 20, 30, 0, false));
		Assert.Equal("不太可能", ErrandRules.ChanceLabel(20));
		Assert.Equal("很有把握", ErrandRules.ChanceLabel(90));
	}

	[Fact]
	public void RuleReplyAcceptsCountersOrDeclines()
	{
		Assert.Equal(ErrandDecision.Accept, ErrandRules.RuleReply(85, 250, 250).Decision);
		ErrandReply counter = ErrandRules.RuleReply(85, 150, 250);
		Assert.Equal(ErrandDecision.Counter, counter.Decision);
		Assert.Equal(250, counter.Gold);
		Assert.Contains("250g", counter.Line);
		Assert.Equal(ErrandDecision.Decline, ErrandRules.RuleReply(30, 900, 250).Decision);
	}

	[Fact]
	public void AiReplyIsHeldToTheRules()
	{
		ErrandReply? r = ErrandRules.ParseReply("```json\n{\"decision\":\"counter\",\"gold\":999,\"line\":\"再加点吧，500g 我就去。\"}\n```", 200, 250);
		Assert.NotNull(r);
		Assert.Equal(ErrandDecision.Counter, r!.Decision);
		Assert.Equal(500, r.Gold);
		ErrandReply? low = ErrandRules.ParseReply("{\"decision\":\"counter\",\"gold\":100,\"line\":\"行吧。\"}", 200, 250);
		Assert.Equal(ErrandDecision.Accept, low!.Decision);
		Assert.Equal(0, low.Gold);
		Assert.Null(ErrandRules.ParseReply("{\"decision\":\"maybe\",\"line\":\"嗯\"}", 200, 250));
		Assert.Null(ErrandRules.ParseReply("{\"decision\":\"accept\",\"line\":\"\"}", 200, 250));
		Assert.Null(ErrandRules.ParseReply("好的", 200, 250));
		Assert.Equal(ErrandDecision.Decline, ErrandRules.ParseReply("{\"decision\":\"Decline\",\"line\":\"没空。\"}", 200, 250)!.Decision);
	}

	[Fact]
	public void ResultArrivesOnTheRightMorning()
	{
		Errand e = new Errand { State = ErrandStates.Active, PostedDay = 10, AcceptedDay = 10, LastDay = 13, DaysNeeded = 2, WillSucceed = true };
		Assert.Equal(12, ErrandRules.ResultDay(e));
		e.DaysNeeded = 6;
		Assert.Equal(14, ErrandRules.ResultDay(e));
		e.WillSucceed = false;
		Assert.Equal(14, ErrandRules.ResultDay(e));
		e.DaysNeeded = 2;
		Assert.Equal(0.5, ErrandRules.Progress(e, 11));
		Assert.Equal(0.95, ErrandRules.Progress(e, 20));
	}

	[Fact]
	public void WithdrawingAfterAcceptingKeepsHalfTheReward()
	{
		Assert.Equal(330, ErrandRules.Refund(new Errand { State = ErrandStates.Waiting, Reward = 30, Goods = 300 }));
		Assert.Equal(315, ErrandRules.Refund(new Errand { State = ErrandStates.Active, Reward = 30, Goods = 300 }));
	}

	[Fact]
	public void CountersAreCappedAndCounteredRefundsAll()
	{
		Assert.Equal(500, ErrandRules.ClampCounter(9999, 200, 250));
		Assert.Equal(610, ErrandRules.ClampCounter(9999, 600, 250));
		Assert.Equal(330, ErrandRules.Refund(new Errand { State = ErrandStates.Countered, Reward = 30, Goods = 300 }));
	}

	[Fact]
	public void SkillsComeFromTheListAndContentPacks()
	{
		Assert.Contains(ErrandSkills.Fish, ErrandSkills.Of("Willy", null));
		Assert.Empty(ErrandSkills.Of("Lewis", null));
		Assert.Equal(new[] { ErrandSkills.Cook, ErrandSkills.Mine }, ErrandSkills.Of("Custom", "cook, Mine，dance").OrderBy(s => s));
		Assert.Equal(ErrandSkills.Fish, ErrandSkills.Needed(ErrandKinds.Fetch, FetchSources.Fish));
		Assert.Null(ErrandSkills.Needed(ErrandKinds.Buy, ""));
		Assert.True(ErrandRules.ChildCan(ErrandKinds.Relay));
		Assert.False(ErrandRules.ChildCan(ErrandKinds.Cook));
	}

	[Fact]
	public void PromptBlockListsOpenAndRecentErrands()
	{
		Errand[] all =
		{
			new Errand { State = ErrandStates.Active, Reward = 200, LastDay = 12 },
			new Errand { State = ErrandStates.Done, Reward = 150, ClosedDay = 9 }
		};
		string block = ErrandRules.PromptBlock(all, 10, _ => "弄 5 个韭葱");
		Assert.StartsWith("【农夫托你办的事】", block);
		Assert.Contains("还剩 2 天", block);
		Assert.Contains("昨天你替农夫弄 5 个韭葱，办成了", block);
		Assert.True(ErrandRules.InPrompt(all[1], 16));
		Assert.False(ErrandRules.InPrompt(all[1], 17));
		Assert.Equal("", ErrandRules.PromptBlock(new Errand[0], 10, _ => ""));
	}
}
