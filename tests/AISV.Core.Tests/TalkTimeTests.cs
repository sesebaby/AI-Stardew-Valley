using AISV.Core.Dialogue;
using AISV.Core.Social;
using Xunit;

namespace AISV.Core.Tests;

public class TalkTimeTests
{
	[Fact]
	public void PendingMinutesAreCappedAndNeverNegative()
	{
		Assert.Equal(10, TalkTime.Accrue(0, 10));
		Assert.Equal(TalkTime.MaxPending, TalkTime.Accrue(170, 30));
		Assert.Equal(20, TalkTime.Accrue(20, -5));
		Assert.Equal(20, TalkTime.Accrue(20, 0));
	}

	[Fact]
	public void ClockStepsInTenMinutesAndStopsAtTwoFifty()
	{
		Assert.Equal(1210, TalkTime.Next(1200));
		Assert.Equal(1300, TalkTime.Next(1250));
		Assert.Equal(2400, TalkTime.Next(2350));
		Assert.Equal(2600, TalkTime.Next(2550));
		Assert.True(TalkTime.CanStep(2540));
		Assert.False(TalkTime.CanStep(2550));
		Assert.False(TalkTime.CanStep(2600));
	}

	[Fact]
	public void LeftoverUnderTenMinutesWaitsForNextChat()
	{
		int pending = TalkTime.Accrue(0, 5);
		Assert.False(TalkTime.Ready(pending));
		pending = TalkTime.Accrue(pending, 5);
		Assert.True(TalkTime.Ready(pending));
		Assert.Equal(0, TalkTime.AfterStep(pending));
		pending = TalkTime.Accrue(0, 15);
		pending = TalkTime.AfterStep(pending);
		Assert.Equal(5, pending);
		Assert.False(TalkTime.Ready(pending));
	}

	[Fact]
	public void BirthdayStepperCyclesThroughUnsetAndSkipsFestivals()
	{
		static bool Festival(int d) => d == 13 || d == 24;
		Assert.Equal(1, BirthdayRules.StepDay(0, 1, Festival));
		Assert.Equal(28, BirthdayRules.StepDay(0, -1, Festival));
		Assert.Equal(0, BirthdayRules.StepDay(28, 1, Festival));
		Assert.Equal(14, BirthdayRules.StepDay(12, 1, Festival));
		Assert.Equal(12, BirthdayRules.StepDay(14, -1, Festival));
		Assert.Equal(14, BirthdayRules.SettleDay(13, Festival));
		Assert.Equal(5, BirthdayRules.SettleDay(5, Festival));
		Assert.Equal(0, BirthdayRules.SettleDay(0, Festival));
	}
}
