using AISV.Core.Companions;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using Xunit;

namespace AISV.Core.Tests;

public class CompanionTests
{
	private static NpcProposal P(string type, string task = "", string summary = "我陪你。", string when = "") => new NpcProposal { Type = type, Task = task, Summary = summary, When = when };

	[Theory]
	[InlineData("dance", "", "我陪你", 10, false, false, "不认识的类型")]
	[InlineData("follow", "", "   ", 10, false, false, "摘要")]
	[InlineData("work", "dance", "帮你", 10, false, false, "不认识的活")]
	[InlineData("work", "water", "帮你", 10, false, true, "孩子")]
	[InlineData("follow", "", "陪你", 10, false, true, "孩子")]
	[InlineData("follow", "", "陪你", 2, false, false, "要 3 心")]
	[InlineData("work", "water", "帮你", 3, false, false, "要 4 心")]
	[InlineData("meetup", "", "见面", 1, false, false, "要 2 心")]
	public void ReviewRejects(string type, string task, string summary, int hearts, bool partner, bool child, string why)
	{
		OfferLedger ledger = new OfferLedger();
		Assert.Null(ledger.Review("Sam", P(type, task, summary), 5, hearts, partner, child, out string reason));
		Assert.Contains(why, reason);
		Assert.Equal(1, ledger.RejectedCount);
		Assert.Empty(ledger.Pending(5));
	}

	[Fact]
	public void PartnerSkipsHeartsAndChildMayOnlyQuest()
	{
		OfferLedger ledger = new OfferLedger();
		PendingOffer? o = ledger.Review("Sam", P("work", "water"), 5, 0, true, false, out _);
		Assert.NotNull(o);
		Assert.Equal(0, o!.Price);
		Assert.NotNull(ledger.Review("Jas", P("quest"), 5, 0, false, true, out _));
	}

	[Fact]
	public void OnePerDayAndSameTypeCooldown()
	{
		OfferLedger ledger = new OfferLedger();
		Assert.False(ledger.ProposedToday("Sam", 10));
		Assert.NotNull(ledger.Review("Sam", P("follow"), 10, 5, false, false, out _));
		Assert.True(ledger.ProposedToday("Sam", 10));
		Assert.False(ledger.ProposedToday("Sam", 11));
		Assert.Null(ledger.Review("Sam", P("work", "water"), 10, 5, false, false, out string r1));
		Assert.Contains("今天已经提过了", r1);
		Assert.NotNull(ledger.Review("Abigail", P("follow"), 10, 5, false, false, out _));
		Assert.Null(ledger.Review("Sam", P("follow"), 12, 5, false, false, out string r2));
		Assert.Contains("冷却", r2);
		Assert.NotNull(ledger.Review("Sam", P("work", "water"), 12, 5, false, false, out _));
		Assert.NotNull(ledger.Review("Sam", P("follow"), 13, 5, false, false, out _));
	}

	[Fact]
	public void ExpiryAndResolveKeepsCooldown()
	{
		OfferLedger ledger = new OfferLedger();
		Assert.Equal(10, ledger.Review("A", P("follow"), 10, 5, false, false, out _)!.ExpiresDay);
		Assert.Equal(11, ledger.Review("B", P("work", "fish", when: "明天上午"), 10, 5, false, false, out _)!.ExpiresDay);
		Assert.Equal(11, ledger.Review("C", P("follow", when: "Tomorrow"), 10, 5, false, false, out _)!.ExpiresDay);
		PendingOffer m = ledger.Review("D", P("meetup"), 10, 5, false, false, out _)!;
		Assert.Equal(13, m.ExpiresDay);
		Assert.Equal(4, ledger.Pending(10).Count);
		Assert.Equal(3, ledger.Pending(11).Count);
		ledger.Resolve(m.Id);
		Assert.Equal(2, ledger.Pending(11).Count);
		Assert.Null(ledger.Review("D", P("meetup"), 10, 5, false, false, out _));
		ledger.Clear();
		Assert.Empty(ledger.Pending(10));
		Assert.NotNull(ledger.Review("D", P("meetup"), 10, 5, false, false, out _));
	}

	[Fact]
	public void LongSummaryIsCut()
	{
		PendingOffer o = new OfferLedger().Review("A", P("follow", summary: new string('话', 200)), 1, 5, false, false, out _)!;
		Assert.Equal(120, o.Summary.Length);
		Assert.Equal("这是一句话", new OfferLedger().Review("A", P("follow", summary: "  这是一句话 "), 1, 5, false, false, out _)!.Summary);
	}

	[Theory]
	[InlineData("work", 4, false, 600)]
	[InlineData("work", 5, false, 500)]
	[InlineData("work", 7, false, 300)]
	[InlineData("work", 8, false, 0)]
	[InlineData("work", 2, false, 600)]
	[InlineData("work", 5, true, 0)]
	[InlineData("follow", 5, false, 0)]
	public void Prices(string type, int hearts, bool partner, int price) => Assert.Equal(price, OfferLedger.PriceFor(type, hearts, partner));

	[Fact]
	public void GateOrder()
	{
		CompanionRoster r = new CompanionRoster();
		Assert.Equal(GateDecision.RefuseChild, r.Gate("Jas", true, true, 10, true, 1, out _));
		Assert.Equal(GateDecision.RefuseBusy, r.Gate("Sam", false, true, 10, true, 1, out _));
		Assert.Equal(GateDecision.Continue, r.Gate("Sam", false, false, 3, false, 1, out _));
		Assert.Equal(GateDecision.AskToPay, r.Gate("Sam", false, false, 5, false, 1, out int price));
		Assert.Equal(500, price);
		r.MarkPaid("Sam", 1);
		Assert.Equal(GateDecision.Continue, r.Gate("Sam", false, false, 5, false, 1, out _));
		Assert.Equal(GateDecision.AskToPay, r.Gate("Sam", false, false, 5, false, 2, out _));
		Assert.Equal(GateDecision.Continue, r.Gate("Sam", false, false, 8, false, 2, out _));
		Assert.Equal(GateDecision.Continue, r.Gate("Sam", false, false, 0, true, 2, out _));
		r.MarkPaid("Sam", 2);
		r.ClearPaid("Sam");
		Assert.Equal(GateDecision.AskToPay, r.Gate("Sam", false, false, 5, false, 2, out _));
		r.MarkPaid("Sam", 2);
		r.SetJob("Sam", "water", 2, 500);
		r.OnJoined("Sam", 900);
		r.Clear();
		Assert.False(r.PaidToday("Sam", 2));
		Assert.Null(r.JobTask("Sam", 2));
		Assert.Null(r.JoinedAt("Sam"));
	}

	[Fact]
	public void JobsDecideTaskPermissionAndMode()
	{
		CompanionRoster r = new CompanionRoster();
		Assert.Null(r.TaskOverride("Sam", "Watering", 1));
		r.SetJob("Sam", "water", 1, 0);
		Assert.True(r.TaskOverride("Sam", "Watering", 1));
		Assert.Null(r.TaskOverride("Sam", "Mining", 1));
		Assert.Null(r.TaskOverride("Sam", "Watering", 2));
		Assert.Equal("Autonomous", r.ModeOverride("Sam", "Watering", 1));
		Assert.Null(r.ModeOverride("Sam", "Mining", 1));
		r.SetJob("Alex", "fish", 1, 0);
		Assert.True(r.TaskOverride("Alex", "Fishing", 1));
		Assert.Null(r.ModeOverride("Alex", "Fishing", 1));
		r.SetJob("Gus", "follow", 1, 0);
		Assert.True(r.TaskOverride("Gus", "Attacking", 1));
		Assert.Equal(new[] { "Lumbering", "Mining" }, CompanionRoster.TaskTypes("clear"));
		Assert.Equal(new[] { "Petting", "Shearing", "Milking" }, CompanionRoster.TaskTypes("animals"));
		Assert.Equal(new[] { "Mining", "Attacking" }, CompanionRoster.TaskTypes("mine"));
		Assert.Empty(CompanionRoster.TaskTypes("nap"));
		r.OnLeft("Sam");
		Assert.Null(r.JobTask("Sam", 1));
	}

	[Fact]
	public void PromptLineSaysSinceAndJob()
	{
		CompanionRoster r = new CompanionRoster();
		Assert.DoesNotContain("起", r.PromptLine("Sam", 1));
		r.OnJoined("Sam", 930);
		r.SetJob("Sam", "water", 1, 500);
		string paid = r.PromptLine("Sam", 1);
		Assert.Contains("09:30", paid);
		Assert.Contains("500g", paid);
		Assert.Contains("浇水", paid);
		r.SetJob("Sam", "harvest", 1, 0);
		Assert.Contains("你自己说了今天帮忙收菜", r.PromptLine("Sam", 1));
		r.SetJob("Sam", "follow", 1, 0);
		Assert.DoesNotContain("帮忙", r.PromptLine("Sam", 1));
		r.SetJob("Sam", "water", 0, 0);
		Assert.DoesNotContain("帮忙", r.PromptLine("Sam", 1));
	}

	[Fact]
	public void CleanForGameDropsSymbolsTheFontLacks()
	{
		Assert.Equal("你好吗", TextTools.CleanForGame(" 你\u2B50好\u2600吗\U0001F600 "));
		Assert.Equal("星露谷，100g！", TextTools.CleanForGame("星露谷，100g！"));
	}

	[Fact]
	public void ProposalParsesFromReply()
	{
		NpcReaction? r = ReactionParser.Parse("{\"says\":\"走吧\",\"warmth\":10,\"feeling\":\"happy\",\"plan_offer\":{\"kind\":\" WORK \",\"task\":\"Water\",\"summary\":\"帮你浇水\"}}", 200, false, NullLog.Instance);
		Assert.Equal("work", r!.Proposal!.Type);
		Assert.Equal("water", r.Proposal.Task);
		Assert.Null(ReactionParser.Parse("{\"says\":\"嗯\",\"warmth\":0,\"feeling\":\"neutral\",\"plan_offer\":{\"kind\":\"none\"}}", 200, false, NullLog.Instance)!.Proposal);
	}
}
