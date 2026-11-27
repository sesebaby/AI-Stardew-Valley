using System;
using System.IO;
using System.Linq;
using AISV.Core.Consequences;
using Xunit;

namespace AISV.Core.Tests;

public class ConsequenceTests
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

	private static MisdeedCatalog Bundled() => MisdeedCatalog.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src/AISV.Mod/assets/consequences/catalog.json")));

	private static PlanInput Input(int value = 35, int money = 10000, ReactionStyle style = ReactionStyle.Hot, bool arrest = true, bool other = false) =>
		new PlanInput(value, money, 60, style, arrest, other);

	[Fact]
	public void BundledCatalogCoversEveryKind()
	{
		MisdeedCatalog c = Bundled();
		Assert.Empty(c.Problems);
		foreach (string kind in Misdeeds.All)
		{
			Assert.NotNull(c.Get(kind));
			Assert.NotEmpty(c.Get(kind)!.Tiers);
		}
	}

	[Fact]
	public void StrikesClimbWithinASeasonAndResetWhenItChanges()
	{
		ConsequenceState s = new ConsequenceState();
		Assert.Equal(1, s.AddStrike(7, Misdeeds.MissedMeetup, "Abigail", 10));
		Assert.Equal(2, s.AddStrike(7, Misdeeds.MissedMeetup, "abigail", 10));
		Assert.Equal(1, s.AddStrike(7, Misdeeds.MissedMeetup, "Leah", 10));
		Assert.Equal(1, s.AddStrike(8, Misdeeds.MissedMeetup, "Abigail", 10));
		Assert.Equal(1, s.AddStrike(7, Misdeeds.MissedMeetup, "Abigail", 11));
		Assert.Equal(1, s.StrikesOf(7, Misdeeds.MissedMeetup, "Abigail", 11));
		Assert.Equal(0, s.StrikesOf(7, Misdeeds.MissedMeetup, "Abigail", 10));
	}

	[Fact]
	public void FenceMatchesTheOldLadder()
	{
		MisdeedSpec fence = Bundled().Get(Misdeeds.Fence)!;
		ConsequencePlan first = ConsequenceRules.Plan(fence, 1, Input());
		Assert.Equal(300, first.Fine);
		Assert.Equal(0, first.ClosedDays);
		Assert.False(first.Arrest);
		Assert.Equal(60, first.Friendship);
		Assert.Equal(15, first.Mood);
		Assert.True(first.Letter);

		ConsequencePlan second = ConsequenceRules.Plan(fence, 2, Input(style: ReactionStyle.Cold));
		Assert.Equal(600, second.Fine);
		Assert.Equal(3, second.ClosedDays);
		Assert.Equal((int)Math.Round(60 * 1.3 * 1.5), second.Friendship);

		ConsequencePlan third = ConsequenceRules.Plan(fence, 5, Input(value: 2000));
		Assert.Equal(9000, third.Fine);
		Assert.Equal(7, third.ClosedDays);
		Assert.True(third.Arrest);
		Assert.False(ConsequenceRules.Plan(fence, 3, Input(arrest: false)).Arrest);
	}

	[Fact]
	public void OwnerLosesHalfOnlyWhenItIsSomeoneElse()
	{
		MisdeedSpec fence = Bundled().Get(Misdeeds.Fence)!;
		Assert.Equal(0, ConsequenceRules.Plan(fence, 1, Input()).OwnerFriendship);
		Assert.Equal(30, ConsequenceRules.Plan(fence, 1, Input(other: true)).OwnerFriendship);
	}

	[Fact]
	public void FinesNeverExceedWhatYouCarry()
	{
		MisdeedSpec fence = Bundled().Get(Misdeeds.Fence)!;
		Assert.Equal(120, ConsequenceRules.Plan(fence, 1, Input(money: 120)).Fine);
		Assert.Equal(0, ConsequenceRules.Plan(fence, 1, Input(money: -5)).Fine);
	}

	[Fact]
	public void WalletFinesFollowWhatYouCarry()
	{
		FineRule wallet = new FineRule { Basis = "wallet", Rate = 10, Min = 200, Max = 1000, Multiplier = 2 };
		Assert.Equal(400, ConsequenceRules.Fine(wallet, 0, 500));
		Assert.Equal(1000, ConsequenceRules.Fine(wallet, 0, 5000));
		Assert.Equal(2000, ConsequenceRules.Fine(wallet, 0, 50000));
	}

	[Fact]
	public void OnlyAddsWhatTheEventDidNotAlreadyCost()
	{
		MisdeedCatalog c = Bundled();
		// 动手打人第一回、失约、垃圾桶：原来就扣了好感，这里不再扣。
		Assert.Equal(0, ConsequenceRules.Plan(c.Get(Misdeeds.Brawl)!, 1, Input()).Friendship);
		Assert.Equal(0, ConsequenceRules.Plan(c.Get(Misdeeds.Brawl)!, 1, Input()).Fine);
		Assert.Equal(0, ConsequenceRules.Plan(c.Get(Misdeeds.MissedMeetup)!, 3, Input()).Friendship);
		Assert.Equal(0, ConsequenceRules.Plan(c.Get(Misdeeds.Trash)!, 3, Input()).Friendship);
		Assert.Equal(0, ConsequenceRules.Plan(c.Get(Misdeeds.TheftCaught)!, 3, Input()).Fine);
		Assert.All(c.Kinds.Values, spec => Assert.NotEmpty(spec.Keeps));
	}

	[Fact]
	public void BrawlsGetHeavierThenEndTheDay()
	{
		MisdeedSpec brawl = Bundled().Get(Misdeeds.Brawl)!;
		ConsequencePlan second = ConsequenceRules.Plan(brawl, 2, Input(money: 3000));
		Assert.Equal(300, second.Fine);
		Assert.False(second.Arrest);
		ConsequencePlan third = ConsequenceRules.Plan(brawl, 3, Input(money: 3000));
		Assert.Equal(600, third.Fine);
		Assert.True(third.Arrest);
	}

	[Fact]
	public void EveryRecordGetsItsOwnLetter()
	{
		ConsequenceState s = new ConsequenceState();
		ConsequenceRecord a = s.Add(new ConsequenceRecord { Kind = Misdeeds.Fence, Day = 3 }, letter: true);
		ConsequenceRecord b = s.Add(new ConsequenceRecord { Kind = Misdeeds.DebtOverdue, Day = 3 }, letter: true);
		ConsequenceRecord c = s.Add(new ConsequenceRecord { Kind = Misdeeds.MissedMeetup, Day = 3 }, letter: false);
		Assert.NotEqual(a.MailId, b.MailId);
		Assert.Equal("", c.MailId);
		Assert.Equal(3, s.Records.Select(r => r.Id).Distinct().Count());
	}

	[Fact]
	public void CooldownsExpireAndOldRecordsAreTrimmedUnlessTheLetterIsUnread()
	{
		ConsequenceState s = new ConsequenceState();
		string key = ConsequenceState.CooldownKey("no_meetups", 7, "Abigail");
		s.Block(key, 20);
		Assert.True(s.Blocked(key, 19));
		Assert.False(s.Blocked(key, 20));
		Assert.False(s.Blocked(ConsequenceState.CooldownKey("no_meetups", 7, "Leah"), 19));
		ConsequenceRecord unread = s.Add(new ConsequenceRecord { Day = 1 }, letter: true);
		s.Add(new ConsequenceRecord { Day = 1 }, letter: true);
		s.Add(new ConsequenceRecord { Day = 200 }, letter: false);
		s.Trim(200, id => id == unread.MailId);
		Assert.Empty(s.Cooldowns);
		Assert.Equal(2, s.Records.Count);
		Assert.Contains(s.Records, r => r.Id == unread.Id);
	}

	[Fact]
	public void UnknownStylesFallBackToHot()
	{
		Assert.Equal(ReactionStyle.Hot, ReactionStyles.Parse("furious"));
		Assert.Equal("snide", ReactionStyles.Id(ReactionStyle.Snide));
		Assert.Null(ReactionStyles.StyleOf("Ripley"));
	}

	[Fact]
	public void CatalogReportsMistakes()
	{
		MisdeedCatalog c = MisdeedCatalog.Parse("{\"kinds\":{\"brawl\":{\"tiers\":[{\"fine\":{\"basis\":\"moon\"}}]},\"nonsense\":{\"tiers\":[]}}}");
		Assert.Contains(c.Problems, p => p.Contains("nonsense"));
		Assert.Contains(c.Problems, p => p.Contains("basis") || p.Contains("依据"));
		Assert.Contains(c.Problems, p => p.Contains(Misdeeds.Fence));
	}
}
