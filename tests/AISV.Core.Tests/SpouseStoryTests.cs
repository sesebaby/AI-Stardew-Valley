using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using Xunit;

namespace AISV.Core.Tests;

public class SpouseStoryTests
{
	private static StoryCandidates Candidates()
	{
		StoryCandidates c = new StoryCandidates();
		c.Deliver.Add(new StoryCandidate("(O)24", "防风草", "农场这季能收"));
		c.Deliver.Add(new StoryCandidate("(O)72", "钻石", "你最爱的"));
		c.Fish.Add(new StoryCandidate("(O)145", "太阳鱼", "好钓"));
		c.Slay.Add(new StoryCandidate("Bat", "蝙蝠", "矿洞第 31 层起"));
		c.Places.AddRange(MeetupPlaces.All.Where(p => p.Key != MeetupPlaces.Home));
		return c;
	}

	private static int Price(string id) => id == "(O)72" ? 750 : 35;

	private const string EventJson = "{\"kind\":\"event\",\"place\":\"beach\",\"time\":\"16:30\",\"invite\":\"来海滩找我\",\"lines\":["
		+ "{\"type\":\"say\",\"text\":\"你来啦。\",\"mood\":\"happy\"},{\"type\":\"narrate\",\"text\":\"海风吹过。\"},{\"type\":\"emote\",\"who\":\"farmer\",\"emote\":\"爱心\"},"
		+ "{\"type\":\"choice\",\"text\":\"喜欢这里吗？\",\"options\":[{\"text\":\"喜欢\",\"reply\":\"我也是。\",\"effect\":\"warm\"},{\"text\":\"一般\",\"reply\":\"哦……\",\"effect\":\"cold\"}]},"
		+ "{\"type\":\"choice\",\"text\":\"再待会？\",\"options\":[{\"text\":\"好\",\"reply\":\"嗯。\",\"effect\":\"neutral\"},{\"text\":\"走吧\",\"reply\":\"好吧。\",\"effect\":\"neutral\"}]},"
		+ "{\"type\":\"choice\",\"text\":\"第三个\",\"options\":[{\"text\":\"a\",\"reply\":\"b\"},{\"text\":\"c\",\"reply\":\"d\"}]},"
		+ "{\"type\":\"say\",\"text\":\"回家吧/走\",\"mood\":\"love\"}]}";

	[Fact]
	public void CompositionsMatchTheFiveStructures()
	{
		Assert.Equal(5, SpouseStoryRules.Compositions.Count);
		Assert.Equal("信 → 任务 → 见面", SpouseStoryRules.Describe(0));
		Assert.Equal(new[] { "event", "talk", "quest", "talk" }, SpouseStoryRules.Compositions[2].Steps);
		Random rng = new Random(3);
		for (int i = 0; i < 50; i++)
		{
			Assert.NotEqual(2, SpouseStoryRules.PickComposition(2, rng));
		}
	}

	[Fact]
	public void PartnersTakeTurns()
	{
		string[] partners = { "Abigail", "Leah", "Maru" };
		Assert.Equal(new[] { "Maru", "Abigail", "Leah" }, SpouseStoryRules.RotationFrom(partners, "leah").ToArray());
		Assert.Equal(partners, SpouseStoryRules.RotationFrom(partners, "").ToArray());
	}

	[Fact]
	public void ParsesLetterQuestEvent()
	{
		string json = "```json\n{\"title\":\"海边的约定\",\"summary\":\"我想和你去看海。\",\"steps\":["
			+ "{\"kind\":\"letter\",\"text\":\"亲爱的，\\n我想请你帮个忙。\",\"gift\":\"true\"},"
			+ "{\"kind\":\"quest\",\"quest_kind\":\"fish\",\"target\":\"(O)24\",\"count\":20,\"title\":\"防风草\",\"description\":\"帮我带 99 个回来\",\"done_line\":\"太好了！\",\"done_mood\":\"开心\"},"
			+ EventJson + "]}\n```";
		StoryPackage? pkg = SpouseStoryRules.Parse(json, 0, Candidates(), Price, out string error);
		Assert.True(pkg != null, error);
		Assert.Equal("海边的约定", pkg!.Title);
		Assert.Equal(new[] { 0, 1, 2 }, pkg.Steps.Select(s => s.Index));
		Assert.Equal("亲爱的，\n我想请你帮个忙。", pkg.Steps[0].LetterText);
		Assert.True(pkg.Steps[0].Gift);
		StoryQuest q = pkg.Steps[1].Quest!;
		Assert.Equal(StoryQuestKinds.Deliver, q.Kind);
		Assert.Equal(10, q.Count);
		Assert.Equal("帮我带 10 个回来 （防风草 ×10）", q.Description);
		Assert.Equal("happy", q.DoneMood);
		StoryStep ev = pkg.Steps[2];
		Assert.Equal("beach", ev.Place);
		Assert.Equal(1630, ev.Time);
		Assert.Equal(2, ev.Lines.Count(l => l.Type == "choice"));
		Assert.Equal("heart", ev.Lines[2].Emote);
		Assert.Equal("回家吧／走", ev.Lines.Last().Text);
	}

	[Fact]
	public void PricierThingsAskForFewer()
	{
		Assert.Equal(1, SpouseStoryRules.DeliverCountLimit(750));
		Assert.Equal(10, SpouseStoryRules.DeliverCountLimit(35));
		Assert.Equal(6, SpouseStoryRules.DeliverCountLimit(100));
	}

	[Fact]
	public void RejectsWhatDoesNotFit()
	{
		Assert.Null(SpouseStoryRules.Parse("", 1, Candidates(), Price, out string e1));
		Assert.Equal("空回复", e1);
		Assert.Null(SpouseStoryRules.Parse("{\"title\":\"x\",\"steps\":[" + EventJson + "]}", 1, Candidates(), Price, out string e2));
		Assert.Contains("要 2 步", e2);
		string badQuest = "{\"title\":\"x\",\"steps\":[" + EventJson + ",{\"kind\":\"quest\",\"target\":\"(O)999\"},{\"kind\":\"letter\",\"text\":\"谢谢你，真的谢谢你。\"}]}";
		Assert.Null(SpouseStoryRules.Parse(badQuest, 3, Candidates(), Price, out string e3));
		Assert.Contains("不在清单里", e3);
		string shortEvent = "{\"title\":\"x\",\"steps\":[{\"kind\":\"event\",\"lines\":[\"嗨\",\"嗯\"]},{\"kind\":\"talk\",\"lines\":[\"a\"]}]}";
		Assert.Null(SpouseStoryRules.Parse(shortEvent, 1, Candidates(), Price, out string e4));
		Assert.Contains("只有 2 行", e4);
	}

	[Fact]
	public void WrongQuestKindIsFoundInTheOtherLists()
	{
		string json = "{\"title\":\"x\",\"steps\":[" + EventJson + ",{\"kind\":\"quest\",\"quest_kind\":\"deliver\",\"target\":\"蝙蝠\",\"count\":1},{\"kind\":\"letter\",\"text\":\"谢谢你，真的谢谢你。\"}]}";
		StoryPackage? pkg = SpouseStoryRules.Parse(json, 3, Candidates(), Price, out string error);
		Assert.True(pkg != null, error);
		StoryQuest q = pkg!.Steps[1].Quest!;
		Assert.Equal(StoryQuestKinds.Slay, q.Kind);
		Assert.Equal("Bat", q.Target);
		Assert.Equal(3, q.Count);
		Assert.Equal("谢谢你。", q.DoneLine);
	}

	[Fact]
	public void UnknownPlaceFallsBackToTownSquareAndTimeIsClamped()
	{
		string json = "{\"title\":\"x\",\"steps\":[{\"kind\":\"event\",\"place\":\"月球\",\"time\":\"23:00\",\"lines\":[\"一\",\"二\",\"三\",\"四\"]},{\"kind\":\"talk\",\"lines\":[{\"text\":\"昨晚的梦好奇怪\"}]}]}";
		StoryPackage? pkg = SpouseStoryRules.Parse(json, 1, Candidates(), Price, out string error);
		Assert.True(pkg != null, error);
		Assert.Equal(MeetupPlaces.TownSquare, pkg!.Steps[0].Place);
		Assert.Equal(SpouseStoryRules.EventLatest, pkg.Steps[0].Time);
		Assert.Equal("昨晚的梦好奇怪", pkg.Steps[1].Topic);
	}

	[Fact]
	public void EventScriptRecordsChoices()
	{
		StoryPackage pkg = SpouseStoryRules.Parse("{\"title\":\"x\",\"steps\":[" + EventJson + ",{\"kind\":\"talk\",\"topic\":\"海\",\"lines\":[\"嗯\"]}]}", 1, Candidates(), Price, out _)!;
		string script = SpouseStoryRules.EventScript("s3", 0, "Leah", (10, 5), (9, 5), pkg.Steps[0].Lines, "阿农", "你想了想……");
		Assert.StartsWith("none/10 5/farmer 9 5 1 Leah 10 5 3/skippable/pause 400/speak Leah \"你来啦。$h\"", script);
		Assert.Contains("emote farmer 20", script);
		Assert.Contains("quickQuestion 喜欢这里吗？#喜欢#一般(break)speak Leah \"我也是。\"\\AISV_StoryChoice s3 0 0 0(break)speak Leah \"哦……\"\\AISV_StoryChoice s3 0 0 1", script);
		Assert.Contains("AISV_StoryChoice s3 0 1 1", script);
		Assert.EndsWith("pause 500/end", script);
	}

	[Fact]
	public void SceneOutcomeCountsWarmAndCold()
	{
		StoryStep step = SpouseStoryRules.Parse("{\"title\":\"x\",\"steps\":[" + EventJson + ",{\"kind\":\"talk\",\"lines\":[\"嗯\"]}]}", 1, Candidates(), Price, out _)!.Steps[0];
		step.Choices = new List<int> { 0, 1 };
		(int points, List<string> said, string feeling) = SpouseStoryRules.SceneOutcome(step);
		Assert.Equal(SpouseStoryRules.EventFriendship + SpouseStoryRules.WarmBonus, points);
		Assert.Equal(new[] { "“喜欢”", "“走吧”" }, said);
		Assert.Equal("这对你意义很大。", feeling);
		step.Choices = new List<int> { 1 };
		Assert.Equal(SpouseStoryRules.EventFriendship + SpouseStoryRules.ColdPenalty, SpouseStoryRules.SceneOutcome(step).Points);
		step.Choices.Clear();
		Assert.Equal("很愉快。", SpouseStoryRules.SceneOutcome(step).Feeling);
	}

	[Fact]
	public void PromptBlockDoesNotSpoil()
	{
		StoryPackage active = new StoryPackage { Title = "海边", Summary = "我想去海边。" };
		StoryStep talk = new StoryStep { Kind = StepKinds.Talk, Topic = "昨晚的梦" };
		StoryPackage recent = new StoryPackage { Title = "早饭", Summary = "一起做了早饭。", State = StoryStates.Failed, Outcome = "农夫没来" };
		string block = SpouseStoryRules.PromptBlock(active, SpouseStoryRules.StepPromptLine(talk, false, false), recent);
		Assert.StartsWith("【和农夫之间的小故事】", block);
		Assert.Contains("别剧透", block);
		Assert.Contains("等农夫问起再说", block);
		Assert.Contains("《早饭》：一起做了早饭。（没成：农夫没来）", block);
		Assert.Equal("", SpouseStoryRules.PromptBlock(null, "", null));
		Assert.Contains("先别提，这是个惊喜", SpouseStoryRules.StepPromptLine(new StoryStep { Kind = StepKinds.Letter }, false, false));
	}

	[Fact]
	public void ContextListsActivityAndWish()
	{
		string text = SpouseStoryRules.Context(new StoryContext
		{
			FarmerName = "阿农",
			Hearts = 10,
			DaysMarried = 12,
			OtherPartners = new[] { "Abigail" },
			Today = "第 1 年春 5 日（周五），天气不错",
			TodayNumber = 4,
			Activity = new[] { new StoryActivityDay { Day = 3, Fish = 2, Places = new List<string> { "海滩" }, GiftsTo = new List<string> { "Haley" }, AwayAtNight = true }, new StoryActivityDay { Day = 4 } },
			Theme = "看日落",
			Display = n => n == "Haley" ? "海莉" : n == "Abigail" ? "阿比盖尔" : n
		});
		Assert.Contains("农夫是 阿农（她）。你们结婚了，已经 12 天。好感：10 心。", text);
		Assert.Contains("农夫还有别的伴侣：阿比盖尔。", text);
		Assert.Contains("- 今天：平静的一天", text);
		Assert.Contains("- 昨天：钓到鱼 2；去了海滩；给海莉送了礼；那晚没回家", text);
		Assert.Contains("很久了或从没送过", text);
		Assert.Contains("（这是第一段）", text);
		Assert.Contains("“看日落”", text);
	}

	[Fact]
	public void CleanStripsScriptCharacters()
	{
		Assert.Equal("a／b＃c“d", SpouseStoryRules.Clean("a/b#c\"d\"$^@", 30));
		Assert.Equal("一二三。", SpouseStoryRules.Clean("一二三。四五六七八", 6));
		Assert.Equal("第一段\n\n第二段", SpouseStoryRules.CleanLetter("第一段\\n\\n\\n\\n第二段", 100));
	}

	[Fact]
	public void OldThemesThatDifferOnlyInCaseDoNotBreakLoading()
	{
		SpouseStoryLog log = System.Text.Json.JsonSerializer.Deserialize<SpouseStoryLog>("{\"Themes\":{\"Abigail\":\"看海\",\"abigail\":\"看星星\"},\"Packages\":[{\"Id\":\"s1\",\"Steps\":[{\"Kind\":\"talk\",\"Lines\":null}]}]}")!;
		log.Normalize();
		Assert.Single(log.Themes);
		Assert.Equal("看海", log.Themes["ABIGAIL"]);
		Assert.Empty(log.Packages[0].Steps[0].Lines);
	}

	[Fact]
	public void SlayExpiryMemorySaysDealWith()
	{
		StoryPackage p = new StoryPackage { Title = "蝙蝠" };
		Assert.Contains("收拾 5 只蝙蝠", SpouseStoryRules.QuestExpiredMemory(p, new StoryQuest { Kind = StoryQuestKinds.Slay, Count = 5, TargetName = "蝙蝠" }));
		Assert.Contains("带 2 个防风草", SpouseStoryRules.QuestExpiredMemory(p, new StoryQuest { Count = 2, TargetName = "防风草" }));
	}

	[Theory]
	[InlineData("浪漫", "romantic")]
	[InlineData("FUNNY", "funny")]
	[InlineData("whatever", "mixed")]
	public void StyleNormalizes(string input, string expected)
	{
		ModConfig c = new ModConfig { SpouseChapterFlavor = input, SpouseChapterGap = 99 }.Normalize();
		Assert.Equal(expected, c.SpouseChapterFlavor);
		Assert.Equal(28, c.SpouseChapterGap);
	}
}
