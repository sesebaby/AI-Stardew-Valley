using System.Collections.Generic;
using AISV.Core.Dialogue;
using AISV.Core.Family;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class GrowthTests
{
	[Fact]
	public void StagesFollowDaysSinceWalking()
	{
		Assert.Equal(KidStages.Toddler, GrowthRules.StageFor(55, 56, 112));
		Assert.Equal(KidStages.Toddler, GrowthRules.StageFor(110, 56, 112));
		Assert.Equal(KidStages.School, GrowthRules.StageFor(111, 56, 112));
		Assert.Equal(KidStages.School, GrowthRules.StageFor(222, 56, 112));
		Assert.Equal(KidStages.Teen, GrowthRules.StageFor(223, 56, 112));
		// 天数设成 0 也至少隔一天
		Assert.Equal(KidStages.School, GrowthRules.StageFor(56, 0, 0));
		Assert.Equal(KidStages.Teen, GrowthRules.StageFor(57, 0, 0));
	}

	[Fact]
	public void GrowsOneStageADayAndNeverBack()
	{
		Assert.Equal(KidStages.School, GrowthRules.Step(KidStages.Toddler, KidStages.Teen));
		Assert.Equal(KidStages.Teen, GrowthRules.Step(KidStages.School, KidStages.Teen));
		Assert.Equal(KidStages.Teen, GrowthRules.Step(KidStages.Teen, KidStages.Toddler));
		Assert.Equal(KidStages.Toddler, GrowthRules.Step(KidStages.Toddler, KidStages.Toddler));
		Assert.Equal(KidStages.Toddler, KidStages.Parse("???"));
		Assert.Equal(KidStages.Teen, KidStages.Parse(" Teen "));
	}

	[Fact]
	public void UpbringingShapesTraits()
	{
		// 几乎没人陪：文静
		Assert.Equal("quiet", GrowthRules.Traits(new KidGrowth())[0]);
		// 天天陪着玩、聊：活泼
		Assert.Equal("lively", GrowthRules.Traits(new KidGrowth { Plays = 10, Chats = 15 })[0]);
		// 老跑出去、找了很久：调皮和独立
		List<string> wild = GrowthRules.Traits(new KidGrowth { Runaways = 4, FoundSlow = 3 });
		Assert.Contains("mischievous", wild);
		Assert.Contains("independent", wild);
		// 常去看上课、爱聊：好奇
		Assert.Equal("curious", GrowthRules.Traits(new KidGrowth { Chats = 6, ClassesWatched = 5, Classes = 10 })[0]);
		// 每次都很快找到、常送东西：黏人
		Assert.Equal("clingy", GrowthRules.Traits(new KidGrowth { FoundQuick = 4, Gifts = 3, Chats = 3 })[0]);
		Assert.True(GrowthRules.Traits(new KidGrowth { Plays = 3, Chats = 3 }).Count <= 2);
	}

	[Fact]
	public void SchoolNeedsTeacherAndClassmatesInTheRoom()
	{
		List<SchoolRules.Stop> penny = new() { new(830, "Trailer"), new(900, "ArchaeologyHouse"), new(1400, "Town") };
		List<SchoolRules.Stop> jas = new() { new(900, "ArchaeologyHouse"), new(1400, "Town") };
		List<SchoolRules.Stop> vincent = new() { new(900, "SamHouse") };
		SchoolRules.Lesson? lesson = SchoolRules.Today(penny, new[] { jas, vincent });
		Assert.NotNull(lesson);
		Assert.Equal("ArchaeologyHouse", lesson!.Value.Location);
		Assert.Equal(900, lesson.Value.Start);
		Assert.Equal(1400, lesson.Value.End);
		// 同学不在：不上课
		Assert.Null(SchoolRules.Today(penny, new[] { vincent }));
		// 老师下午才去：不算上课
		Assert.Null(SchoolRules.Today(new List<SchoolRules.Stop> { new(1300, "ArchaeologyHouse") }, new[] { new List<SchoolRules.Stop> { new(1300, "ArchaeologyHouse") } }));
		// 别的 mod 挪到图书馆
		Assert.NotNull(SchoolRules.Today(new List<SchoolRules.Stop> { new(930, "Custom_Library") }, new[] { new List<SchoolRules.Stop> { new(900, "Custom_Library") } }));
	}

	[Fact]
	public void OlderKidsTalkMoreButStayKids()
	{
		ChildContext teen = new ChildContext { Key = "1_Pip", Name = "Pip", Boy = true, StageId = KidStages.Teen, Stage = GrowthRules.StageText(KidStages.Teen), ParentName = "小麦", ParentRole = "妈妈", Traits = new[] { "quiet" }, Day = 5 };
		string system = ChildPrompts.System(teen, new List<ChildTurn>(), opening: true);
		Assert.Contains("120 个字以内", system);
		Assert.Contains("十三四岁", system);
		Assert.Contains("不谈恋爱", system);
		Assert.Contains("文静", system);
		Assert.DoesNotContain("奶声奶气", system);
		ChildContext toddler = new ChildContext { Name = "Pip", ParentRole = "妈妈", Stage = "刚学会走路" };
		Assert.Contains("40 个字以内", ChildPrompts.System(toddler, new List<ChildTurn>(), opening: true));
		string longLine = new string('啊', 100);
		Assert.True(ChildPrompts.Clean(longLine)!.Length <= 41);
		Assert.True(ChildPrompts.Clean(longLine, KidStages.School)!.Length > 70);
		// 长大那天写下的样子进提示词；角色卡写了性格就用角色卡
		ChildContext grown = new ChildContext { Name = "Pip", ParentRole = "妈妈", ParentName = "小麦", StageId = KidStages.School, Stage = "上学", Grown = "爱画画的小画家" };
		Assert.Contains("爱画画的小画家", ChildPrompts.Persona(grown));
	}

	[Fact]
	public void GrowthBookCountsChatsOncePerDay()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		GrowthBook book = new GrowthBook(() => store);
		book.Note("1_Pip", "chat", 3);
		book.Note("1_Pip", "chat", 3);
		book.Note("1_Pip", "chat", 4);
		book.Note("1_Pip", "quick", 4);
		Assert.Equal(2, book.Find("1_Pip")!.Chats);
		Assert.Equal(1, book.Find("1_Pip")!.FoundQuick);
		book.Forget(new[] { "2_Other" });
		Assert.Null(book.Find("1_Pip"));
	}
}
