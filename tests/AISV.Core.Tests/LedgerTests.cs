using System.Collections.Generic;
using System.Linq;
using AISV.Core.World.Ledger;
using Xunit;

namespace AISV.Core.Tests;

public class LedgerTests
{
	[Fact]
	public void SeededValuesStayTheSameForOneSave()
	{
		int a = LedgerRules.Vary(1000, LedgerRules.Seeded(42, "Pierre"));
		int b = LedgerRules.Vary(1000, LedgerRules.Seeded(42, "pierre"));
		Assert.Equal(a, b);
		Assert.InRange(a, 800, 1200);
		Assert.Equal(0, LedgerRules.Vary(0, LedgerRules.Seeded(1, "Linus")));
	}

	[Fact]
	public void UnknownVillagersGetPlainNumbersAndPacksCanOverride()
	{
		Occupation kid = LedgerRules.JobOf("CustomKid", child: true);
		Assert.True(kid.Child);
		Occupation adult = LedgerRules.JobOf("Custom", child: false);
		Assert.Equal(300, adult.Wage);
		Occupation changed = LedgerRules.Override(adult, "5000, 100,abc");
		Assert.Equal(5000, changed.Gold);
		Assert.Equal(100, changed.Savings);
		Assert.Equal(300, changed.Wage);
		Assert.Same(adult, LedgerRules.Override(adult, " "));
	}

	[Fact]
	public void LivingCostsAndSavingsBalanceOut()
	{
		Assert.Equal(80, LedgerRules.LivingCost(LedgerRules.Jobs["Pierre"]));
		Assert.Equal(180, LedgerRules.LivingCost(LedgerRules.Jobs["Sam"]));
		Assert.Equal(60, LedgerRules.LivingCost(LedgerRules.Jobs["Abigail"]));
		Assert.Equal(400, LedgerRules.Restock(1000));
		Assert.Equal(80, LedgerRules.LivingCost(LedgerRules.Jobs["Leo"]));
		Assert.Equal(0, LedgerRules.LivingCost(LedgerRules.Jobs["Jas"]));

		NpcWallet rich = new NpcWallet { Gold = 1000, Savings = 0 };
		Assert.Equal(340, LedgerRules.Rebalance(rich, 160));
		Assert.Equal(660, rich.Gold);
		NpcWallet poor = new NpcWallet { Gold = 50, Savings = 500 };
		Assert.Equal(-110, LedgerRules.Rebalance(poor, 160));
		Assert.Equal(160, poor.Gold);
		Assert.Equal(0, LedgerRules.Rebalance(new NpcWallet { Gold = 200 }, 160));
	}

	[Fact]
	public void PayingDrawsOnSavingsButNeverGoesNegative()
	{
		NpcWallet w = new NpcWallet { Gold = 100, Savings = 50 };
		Assert.Equal(130, LedgerRules.Pay(w, 130));
		Assert.Equal(0, w.Gold);
		Assert.Equal(20, w.Savings);
		Assert.Equal(20, LedgerRules.Pay(w, 500));
		Assert.Equal(0, w.Total);
	}

	[Fact]
	public void ItemsStackAndRespectTheSlotLimit()
	{
		NpcWallet w = new NpcWallet();
		Assert.True(LedgerRules.Put(w.Carried, 2, "(O)20", 3, 0));
		Assert.True(LedgerRules.Put(w.Carried, 2, "(O)20", 2, 0));
		Assert.Single(w.Carried);
		Assert.Equal(5, w.Carried[0].Count);
		Assert.True(LedgerRules.Put(w.Carried, 2, "(O)20", 1, 2));
		Assert.False(LedgerRules.Put(w.Carried, 2, "(O)72", 1, 0));
		Assert.Equal(6, LedgerRules.Take(w.Carried, "(O)20", 10));
		Assert.Empty(w.Carried);
	}

	[Fact]
	public void LoanLimitDependsOnTrustAndPurse()
	{
		Assert.Equal(0, LedgerRules.LoanLimit(3, 10000));
		Assert.Equal(2000, LedgerRules.LoanLimit(4, 10000));
		Assert.Equal(450, LedgerRules.LoanLimit(8, 905));
	}

	[Fact]
	public void OverdueDebtsFollowEachStyle()
	{
		Debt demand = new Debt { Style = DebtStyles.Demand, DueDay = 10 };
		Assert.Equal(DebtAction.None, LedgerRules.Overdue(demand, 10));
		Assert.Equal(DebtAction.Warn, LedgerRules.Overdue(demand, 11));
		demand.WarnedDay = 11;
		Assert.Equal(DebtAction.None, LedgerRules.Overdue(demand, 13));
		Assert.Equal(DebtAction.Collect, LedgerRules.Overdue(demand, 14));

		Debt remind = new Debt { Style = DebtStyles.Remind, DueDay = 10 };
		Assert.Equal(DebtAction.Remind, LedgerRules.Overdue(remind, 11));
		remind.LastReminderDay = 11;
		remind.Reminders = 1;
		Assert.Equal(DebtAction.None, LedgerRules.Overdue(remind, 13));
		Assert.Equal(DebtAction.Remind, LedgerRules.Overdue(remind, 14));
		Assert.Equal(0, LedgerRules.RemindPenalty(remind));
		remind.Reminders = 2;
		Assert.Equal(-5, LedgerRules.RemindPenalty(remind));

		Debt quiet = new Debt { Style = DebtStyles.Quiet, DueDay = 10, FriendshipLost = 98 };
		Assert.Equal(DebtAction.Sulk, LedgerRules.Overdue(quiet, 12));
		quiet.FriendshipLost = 100;
		Assert.Equal(DebtAction.None, LedgerRules.Overdue(quiet, 12));
		Assert.Equal(DebtAction.None, LedgerRules.Overdue(new Debt { State = DebtStates.Paid, DueDay = 1 }, 20));
	}

	[Fact]
	public void DebtStyleComesFromTheListOrAPack()
	{
		Assert.Equal(DebtStyles.Demand, LedgerRules.StyleOf("Pierre", null));
		Assert.Equal(DebtStyles.Quiet, LedgerRules.StyleOf("Penny", ""));
		Assert.Equal(DebtStyles.Remind, LedgerRules.StyleOf("Lewis", null));
		Assert.Equal(DebtStyles.Quiet, LedgerRules.StyleOf("Pierre", " Quiet "));
	}

	[Fact]
	public void PromptBlockMentionsMoneyAndDebts()
	{
		NpcWallet w = new NpcWallet { Gold = 300, Savings = 1200 };
		LedgerRules.AddLog(w, new LedgerEntry { Day = 9, Kind = EntryKinds.Shopping, Amount = -60, Note = "在杂货店买了花椰菜种子" });
		LedgerRules.AddLog(w, new LedgerEntry { Day = 9, Kind = EntryKinds.Sales, Amount = 100, Note = "店里收入" });
		Debt d = new Debt { Amount = 500, Repaid = 100, DueDay = 8, Style = DebtStyles.Demand };
		string block = LedgerRules.PromptBlock(w, LedgerRules.Jobs["Pierre"], new[] { "防风草种子 ×5" }, new[] { d }, 10);
		Assert.StartsWith("【你的钱】", block);
		Assert.Contains("身上有 300g，存款 1200g", block);
		Assert.Contains("防风草种子 ×5", block);
		Assert.Contains("昨天花了 60g（在杂货店买了花椰菜种子）", block);
		Assert.DoesNotContain("店里收入", block);
		Assert.Contains("还欠 400g，已经过期 2 天了。你会直接开口要。", block);
	}

	[Fact]
	public void LogKeepsTheLatestEntries()
	{
		NpcWallet w = new NpcWallet();
		for (int i = 0; i < LedgerRules.MaxLog + 5; i++)
		{
			LedgerRules.AddLog(w, new LedgerEntry { Day = i });
		}
		Assert.Equal(LedgerRules.MaxLog, w.Log.Count);
		Assert.Equal(LedgerRules.MaxLog + 4, w.Log.First().Day);
	}

	[Fact]
	public void SameDaySameEntriesMerge()
	{
		NpcWallet w = new NpcWallet();
		LedgerRules.AddLog(w, new LedgerEntry { Day = 3, Time = 1200, Kind = EntryKinds.Sales, Amount = 40, Note = "客流" });
		LedgerRules.AddLog(w, new LedgerEntry { Day = 3, Time = 1300, Kind = EntryKinds.Sales, Amount = 25, Note = "客流" });
		LedgerRules.AddLog(w, new LedgerEntry { Day = 3, Time = 1400, Kind = EntryKinds.Sales, Amount = -5, Note = "客流" });
		LedgerRules.AddLog(w, new LedgerEntry { Day = 4, Time = 900, Kind = EntryKinds.Sales, Amount = 10, Note = "客流" });
		Assert.Equal(3, w.Log.Count);
		Assert.Equal(65, w.Log[2].Amount);
		Assert.Equal(1300, w.Log[2].Time);
	}

	[Fact]
	public void TakeSpansSlotsAndStopsWhenEmpty()
	{
		List<LedgerItem> slots = new List<LedgerItem> { new LedgerItem { Id = "(O)24", Count = 2 }, new LedgerItem { Id = "(O)24", Count = 3, Quality = 2 } };
		Assert.Equal(4, LedgerRules.Take(slots, "(O)24", 4));
		Assert.Single(slots);
		Assert.Equal(1, slots[0].Count);
		Assert.Equal(1, LedgerRules.Take(slots, "(O)24", 9));
		Assert.Empty(slots);
	}

	[Fact]
	public void RebalanceWithdrawsWhenShort()
	{
		NpcWallet w = new NpcWallet { Gold = 10, Savings = 500 };
		Assert.Equal(-150, LedgerRules.Rebalance(w, 160));
		Assert.Equal(160, w.Gold);
		Assert.Equal(350, w.Savings);
		NpcWallet broke = new NpcWallet { Gold = 30 };
		Assert.Equal(30, LedgerRules.Pay(broke, 30));
		Assert.Equal(0, broke.Gold);
	}
}
