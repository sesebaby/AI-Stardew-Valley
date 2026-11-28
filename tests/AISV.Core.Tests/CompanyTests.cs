using System.Linq;
using AISV.Core.Companions;
using Xunit;

namespace AISV.Core.Tests;

public class CompanyTests
{
	[Fact]
	public void DoesNotFollowIntoMinesCasinoOrLockerRooms()
	{
		Assert.True(CompanyRules.NoFollow("UndergroundMine12"));
		Assert.True(CompanyRules.NoFollow("SkullCave"));
		Assert.True(CompanyRules.NoFollow("VolcanoDungeon3"));
		Assert.True(CompanyRules.NoFollow("Club"));
		Assert.True(CompanyRules.NoFollow("BathHouse_WomensLocker"));
		Assert.True(CompanyRules.NoFollow("IslandSouth"));
		Assert.False(CompanyRules.NoFollow("Town"));
		Assert.False(CompanyRules.NoFollow("SeedShop"));
		Assert.False(CompanyRules.NoFollow("BathHouse_Pool"));
		Assert.False(CompanyRules.NoFollow("Mine"));
	}

	[Fact]
	public void EntersYourHomeOnlyWhenCloseEnough()
	{
		Assert.False(CompanyRules.MayEnterHome(3, partner: false));
		Assert.True(CompanyRules.MayEnterHome(CompanyRules.HomeHearts, partner: false));
		Assert.True(CompanyRules.MayEnterHome(0, partner: true));
	}

	[Fact]
	public void StaysTwoHoursButNotPastTen()
	{
		Assert.Equal(1600, CompanyRules.End(1400));
		Assert.Equal(1850, CompanyRules.End(1650));
		Assert.Equal(2200, CompanyRules.End(2100));
		Assert.Equal(2230, CompanyRules.End(2230));
	}

	[Fact]
	public void SeeksYouTwentyMinutesLate()
	{
		Assert.False(CompanyRules.TimeToSeek(1400, 1410));
		Assert.True(CompanyRules.TimeToSeek(1400, 1420));
		Assert.True(CompanyRules.TimeToSeek(1450, 1510));
	}

	[Fact]
	public void RemarksAreFewAndSpacedOut()
	{
		Assert.True(CompanyRules.MayRemark(0, -1, 1400));
		Assert.False(CompanyRules.MayRemark(1, 1400, 1420));
		Assert.True(CompanyRules.MayRemark(1, 1400, 1430));
		Assert.False(CompanyRules.MayRemark(CompanyRules.MaxRemarks, -1, 1800));
	}

	[Fact]
	public void PromptSaysYouAreWalkingTogether()
	{
		Meetup m = new Meetup { Npc = "Abigail", Day = 5, Time = 1400, Place = MeetupPlaces.TownSquare, State = MeetupStates.Kept, ClosedDay = 5, KeptAt = 1400, CompanyUntil = 1600, Summary = "一起去河边钓鱼" };
		string now = MeetupRules.PromptBlock(new[] { m }, 5, k => "镇广场", 1500);
		Assert.Contains("一起走走", now);
		Assert.Contains("一起去河边钓鱼", now);
		string later = MeetupRules.PromptBlock(new[] { m }, 5, k => "镇广场", 1700);
		Assert.DoesNotContain("一起走走", later);
		Assert.Contains("见了你", later);
	}
}
