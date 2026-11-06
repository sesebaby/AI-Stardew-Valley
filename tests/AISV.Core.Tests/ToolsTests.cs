using System;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class ToolsTests
{
	private static readonly DateTime Start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void ClockRunsOnRealTimeAndPausingFreezesIt()
	{
		FocusClock clock = new FocusClock();
		clock.StartOrPause(Start);
		Assert.Equal(TimeSpan.FromMinutes(15), clock.Remaining(Start.AddMinutes(10)));
		clock.StartOrPause(Start.AddMinutes(10));
		Assert.False(clock.Ticking);
		Assert.Equal(TimeSpan.FromMinutes(15), clock.Remaining(Start.AddHours(3)));
		Assert.False(clock.RollOver(Start.AddHours(3)));
		clock.StartOrPause(Start.AddHours(3));
		Assert.Equal(TimeSpan.FromMinutes(14), clock.Remaining(Start.AddHours(3).AddMinutes(1)));
	}

	[Fact]
	public void FinishedWorkTurnsToRestAndCountsARound()
	{
		FocusClock clock = new FocusClock();
		clock.StartOrPause(Start);
		Assert.False(clock.RollOver(Start.AddMinutes(24)));
		Assert.True(clock.RollOver(Start.AddMinutes(25)));
		Assert.True(clock.Resting);
		Assert.Equal(1, clock.RoundsDone);
		Assert.False(clock.Ticking);
		Assert.Equal(FocusClock.RestSpan, clock.Remaining(Start.AddMinutes(25)));
		clock.StartOrPause(Start.AddMinutes(26));
		Assert.True(clock.RollOver(Start.AddMinutes(31)));
		Assert.False(clock.Resting);
		Assert.Equal(1, clock.RoundsDone);
	}

	[Fact]
	public void GoalsClampAndUnfinishedComeFirst()
	{
		FarmGoal[] goals = { new FarmGoal("a", 0.5, 10), new FarmGoal("b", 2, 5), new FarmGoal("c", 0, 20) };
		Assert.Equal(new[] { 5.0, 5.0, 0.0 }, goals.Select(g => g.Marks));
		Assert.Equal(new[] { "a", "c", "b" }, FarmGoals.NextUp(goals).Select(g => g.Label));
	}

	[Fact]
	public void BirthdaysWrapAroundTheYear()
	{
		BornOn[] marks = { new BornOn("A", "spring", 2), new BornOn("B", "winter", 28), new BornOn("C", "Winter", 10), new BornOn("X", "moon", 1) };
		var next = BirthdayWheel.Soonest(marks, "winter", 20, 3);
		Assert.Equal(new[] { "B", "A", "C" }, next.Select(n => n.Who.Npc));
		Assert.Equal(new[] { 8, 10, 102 }, next.Select(n => n.InDays));
		Assert.Equal(0, BirthdayWheel.Soonest(marks, "spring", 2, 1)[0].InDays);
		Assert.Equal(2, BirthdayWheel.Soonest(marks, "winter", 20, 2).Count);
	}

	[Fact]
	public void SeasonsTurnBothWays()
	{
		Assert.Equal("spring", BirthdayWheel.Turn("winter", 1));
		Assert.Equal("winter", BirthdayWheel.Turn("spring", -1));
		Assert.True(BirthdayWheel.IsIn(new BornOn("A", "Fall", 1), "fall"));
	}

	[Fact]
	public void TodoPadKeepsNotesInTheSaveAndSortsTickedLast()
	{
		MemoryBackend backend = new MemoryBackend();
		DocumentStore store = new DocumentStore(backend);
		TodoPad pad = new TodoPad(() => store);
		Assert.Equal(JotOutcome.Blank, pad.Jot("  "));
		Assert.Equal(JotOutcome.Added, pad.Jot(" 浇水 "));
		Assert.Equal(JotOutcome.Added, pad.Jot("钓鱼"));
		pad.Tick(pad.InOrder()[0]);
		Assert.Equal(new[] { "浇水", "钓鱼" }, pad.InOrder().Select(n => n.Words));
		pad.Tear(pad.InOrder()[0]);
		pad.SweepTicked();
		Assert.Empty(pad.InOrder());
		Assert.Empty(new TodoPad(() => null).InOrder());
		Assert.Equal(JotOutcome.Added, new TodoPad(() => null).Jot("没进存档也不崩"));
	}

	[Fact]
	public void TodoPadStopsWhenFull()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		TodoPad pad = new TodoPad(() => store);
		for (int i = 0; i < 99; i++)
		{
			Assert.Equal(JotOutcome.Added, pad.Jot("第 " + i + " 件"));
		}
		Assert.Equal(JotOutcome.PadFull, pad.Jot("多一件"));
	}
}
