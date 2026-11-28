using AISV.Core.Life;
using Xunit;

namespace AISV.Core.Tests;

public class VillagerGrowthTests
{
	private static VillagerGrowthFacts F(int hearts = 0, string spouse = "", int goals = 0, string couple = "", string job = "", int turns = 0, int year = 1) =>
		new VillagerGrowthFacts("Shane", hearts, spouse, goals, couple, job, turns, year);

	[Fact]
	public void NothingToWriteUntilSomethingHappened()
	{
		Assert.False(VillagerGrowthRules.Due(null, F(hearts: 2), 10));
		Assert.True(VillagerGrowthRules.Due(null, F(hearts: 3), 10));
		Assert.True(VillagerGrowthRules.Due(null, F(year: 2), 10));
	}

	[Fact]
	public void SmallChangesWaitTwoWeeksBigOnesDoNot()
	{
		VillagerGrowth g = new VillagerGrowth { Npc = "Shane" };
		VillagerGrowthRules.Add(g, new VillagerGrowthEntry { Day = 100, Now = "x" }, VillagerGrowthRules.Mark(F(hearts: 3)));
		Assert.False(VillagerGrowthRules.Due(g, F(hearts: 4), 105));
		Assert.False(VillagerGrowthRules.Due(g, F(hearts: 5), 105));
		Assert.True(VillagerGrowthRules.Due(g, F(hearts: 5), 100 + VillagerGrowthRules.MinGapDays));
		Assert.True(VillagerGrowthRules.Due(g, F(hearts: 3, goals: 1), 101));
		Assert.True(VillagerGrowthRules.Due(g, F(hearts: 3, couple: "dating:Emily"), 101));
		Assert.True(VillagerGrowthRules.Due(g, F(hearts: 3, spouse: "阿禾"), 101));
	}

	[Fact]
	public void KeepsLastFewAndParsesReply()
	{
		VillagerGrowth g = new VillagerGrowth();
		for (int i = 0; i < 6; i++)
		{
			VillagerGrowthRules.Add(g, new VillagerGrowthEntry { Day = i, Now = "n" + i }, "m" + i);
		}
		Assert.Equal(VillagerGrowthRules.KeepHistory, g.History.Count);
		Assert.Equal("n5", g.Current!.Now);
		Assert.Equal("m5", g.Mark);

		VillagerGrowthEntry? e = VillagerGrowthRules.Parse("```json\n{\"now\": \"你还是那副不耐烦的样子，可现在会在酒吧给农夫留个座，嘴上照样骂他多管闲事。\", \"voice\": \"骂人前会先叹口气\", \"why\": \"农夫一直陪他\"}\n```", 30);
		Assert.NotNull(e);
		Assert.Equal(30, e!.Day);
		Assert.Equal("骂人前会先叹口气", e.Voice);
		Assert.Null(VillagerGrowthRules.Parse("不是 JSON", 30));
		Assert.Null(VillagerGrowthRules.Parse("{\"now\": \"太短\"}", 30));
	}

	[Fact]
	public void TailKeepsPlayersOwnSpeech()
	{
		VillagerGrowthEntry e = new VillagerGrowthEntry { Now = "你还是嘴硬。", Voice = "骂人前先叹气" };
		string tail = VillagerGrowthRules.PersonaTail(e, cardSpeech: false);
		Assert.Contains("底色还是上面那个人", tail);
		Assert.Contains("骂人前先叹气", tail);
		Assert.DoesNotContain("骂人前先叹气", VillagerGrowthRules.PersonaTail(e, cardSpeech: true));
		Assert.Equal("", VillagerGrowthRules.PersonaTail(null, false));
	}

	[Fact]
	public void PromptForbidsTurningIntoANicePerson()
	{
		Assert.Contains("老好人", VillagerGrowthRules.System);
		string ask = VillagerGrowthRules.Ask("谢恩", "身份：……", new VillagerGrowthEntry { Now = "上次的样子" }, new[] { "和农夫阿禾有 6 心的交情。" }, "中文");
		Assert.Contains("上次的样子", ask);
		Assert.Contains("6 心", ask);
	}
}
