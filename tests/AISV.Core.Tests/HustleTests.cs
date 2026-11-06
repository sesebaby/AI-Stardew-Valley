using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.World.Ledger;
using Xunit;

namespace AISV.Core.Tests;

public class HustleTests
{
	private static readonly HustleOptions Everything = new HustleOptions("Gus", "Robin", ("(O)72", 0, 250), SaloonOpen: true, Festival: false);

	[Fact]
	public void OnlyBrokeAdultsHustle()
	{
		Occupation sam = LedgerRules.Jobs["Sam"];
		Assert.True(HustleRules.Broke(new NpcWallet { Gold = 20, Savings = 0 }, sam));
		Assert.False(HustleRules.Broke(new NpcWallet { Gold = 20, Savings = 5000 }, sam));
		Assert.False(HustleRules.Broke(new NpcWallet { Gold = 500 }, sam));
		Assert.False(HustleRules.Broke(new NpcWallet { Gold = 0 }, LedgerRules.Jobs["Jas"]));
	}

	[Fact]
	public void ChoiceFollowsPersonalityAndWhatIsPossible()
	{
		Dictionary<string, int> seen = new Dictionary<string, int>();
		for (int seed = 0; seed < 400; seed++)
		{
			HustleChoice? c = HustleRules.Choose("Sam", new NpcWallet { Gold = 0 }, LedgerRules.Jobs["Sam"], Everything, 10, new Random(seed));
			if (c != null)
			{
				seen[c.Kind] = seen.GetValueOrDefault(c.Kind) + 1;
			}
		}
		Assert.Equal(new[] { HustleKinds.Borrow, HustleKinds.Busk, HustleKinds.OddJob }, seen.Keys.OrderBy(k => k));
		Assert.True(seen[HustleKinds.Busk] > seen[HustleKinds.Borrow]);

		HustleOptions festival = Everything with { Festival = true, Lender = null, Employer = null };
		for (int seed = 0; seed < 50; seed++)
		{
			HustleChoice? c = HustleRules.Choose("Sam", new NpcWallet { Gold = 0 }, LedgerRules.Jobs["Sam"], festival, 10, new Random(seed));
			Assert.True(c == null || c.Kind is not (HustleKinds.Busk or HustleKinds.Borrow or HustleKinds.OddJob));
		}
	}

	[Fact]
	public void BorrowAndJobsCarryTheOtherPerson()
	{
		for (int seed = 0; seed < 200; seed++)
		{
			HustleChoice? c = HustleRules.Choose("Pam", new NpcWallet { Gold = 0 }, LedgerRules.Jobs["Pam"], Everything, 10, new Random(seed));
			if (c?.Kind == HustleKinds.Borrow)
			{
				Assert.Equal("Gus", c.Other);
			}
			if (c?.Kind == HustleKinds.OddJob)
			{
				Assert.Equal("Robin", c.Other);
			}
		}
	}

	[Fact]
	public void CooldownAndTightening()
	{
		NpcWallet w = new NpcWallet { Gold = 0, Hustle = new Hustle { Day = 9, Kind = HustleKinds.Forage } };
		Assert.All(Enumerable.Range(0, 30), s => Assert.Null(HustleRules.Choose("Linus", w, LedgerRules.Jobs["Linus"], Everything, 10, new Random(s))));
		w.TightUntil = 12;
		Assert.True(HustleRules.Tight(w, 12));
		Assert.False(HustleRules.Tight(w, 13));
	}

	[Fact]
	public void VillagerLoansRepayOrSour()
	{
		Assert.Equal(180, HustleRules.LoanAmount(LedgerRules.Jobs["Sam"], 2000));
		Assert.Equal(100, HustleRules.LoanAmount(LedgerRules.Jobs["Sam"], 400));
		Assert.True(HustleRules.CanRepay(new NpcWallet { Gold = 300 }, LedgerRules.Jobs["Sam"], 180));
		Assert.False(HustleRules.CanRepay(new NpcWallet { Gold = 200 }, LedgerRules.Jobs["Sam"], 180));

		NpcDebt d = new NpcDebt { DueDay = 10 };
		Assert.Equal("", HustleRules.Overdue(d, 10, DebtStyles.Remind));
		Assert.Equal("strain", HustleRules.Overdue(d, 11, DebtStyles.Remind));
		d.Strained = true;
		Assert.Equal("", HustleRules.Overdue(d, 12, DebtStyles.Remind));
		Assert.Equal(NpcDebtStates.Forgiven, HustleRules.Overdue(d, 24, DebtStyles.Quiet));
		Assert.Equal(NpcDebtStates.Collected, HustleRules.Overdue(d, 24, DebtStyles.Demand));
	}

	[Fact]
	public void PromptMentionsPlansDebtsAndSaving()
	{
		NpcWallet w = new NpcWallet { Npc = "Sam", Gold = 5, TightUntil = 15, Hustle = new Hustle { Day = 10, Kind = HustleKinds.Busk, Plan = "晚上在酒吧弹吉他唱歌" } };
		List<NpcDebt> debts = new List<NpcDebt> { new NpcDebt { Lender = "Gus", Borrower = "Sam", Amount = 180, DueDay = 17 } };
		string text = string.Join("\n", HustleRules.PromptLines(w, debts, "Sam", 10, n => n == "Gus" ? "格斯" : n));
		Assert.Contains("今天打算晚上在酒吧弹吉他唱歌", text);
		Assert.Contains("在省钱", text);
		Assert.Contains("你向格斯借了 180g，7 天后到期", text);
		Assert.Contains("Sam向你借了", string.Join("\n", HustleRules.PromptLines(new NpcWallet(), debts, "Gus", 10, n => n)));
	}

	[Fact]
	public void ClassifiedsTrimAndParse()
	{
		List<Ad> ads = new List<Ad>
		{
			new Ad { Id = "a1", Day = 1, ExpiresDay = 8 },
			new Ad { Id = "a2", Day = 1, ExpiresDay = 8, SoldDay = 3 },
			new Ad { Id = "a3", Day = 10, ExpiresDay = 17 }
		};
		ClassifiedRules.Trim(ads, 14);
		Assert.Equal(new[] { "a3" }, ads.Select(a => a.Id));
		Assert.True(ClassifiedRules.Open(ads[0], 17));
		Assert.False(ClassifiedRules.Open(ads[0], 18));
		Assert.Equal(150, ClassifiedRules.AskingPrice(100));
		Dictionary<string, string> parsed = ClassifiedRules.Parse("```json\n{\"a3\":\"“婚纱，九成新。”\",\"a4\":5}\n```");
		Assert.Equal("婚纱，九成新。", parsed["a3"]);
		Assert.False(parsed.ContainsKey("a4"));
		Assert.Contains("出售：婚纱", ClassifiedRules.Title(new Ad { Thing = "婚纱" }));
	}
}
