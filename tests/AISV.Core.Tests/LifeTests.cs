using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Dialogue;
using AISV.Core.Life;
using AISV.Core.Social.Diary;
using AISV.Core.Social.Personality;
using AISV.Core.Stories;
using Xunit;

namespace AISV.Core.Tests;

public class LifeTests
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

	private static LifeCatalog Bundled() => LifeCatalog.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src/AISV.Mod/assets/life/goals.json")));

	private static LifeGoalDef Def(string npc = "Haley", int steps = 4, int helpStep = 2) => new LifeGoalDef
	{
		Id = npc.ToLowerInvariant() + "_goal",
		Npc = npc,
		Goal = "办展",
		Steps = Enumerable.Range(1, steps).Select(i => "第" + i + "步").ToList(),
		Help = helpStep > 0 ? new LifeHelp { Step = helpStep, Item = "(O)388", Amount = 5 } : null,
		Ending = new LifeEnding { Cast = new List<string> { npc } }
	};

	[Fact]
	public void BundledGoalsLoadCleanlyAndHaveArtAndProps()
	{
		LifeCatalog c = Bundled();
		Assert.Empty(c.Problems);
		Assert.Equal(12, c.Goals.Count);
		Assert.Equal(c.Goals.Count, c.Goals.Select(g => g.Npc).Distinct(StringComparer.OrdinalIgnoreCase).Count());
		string root = RepoRoot();
		foreach (LifeGoalDef g in c.Goals)
		{
			Assert.True(LifeRules.Lines(g.Ending.Lines, g.Ending.Cast).Count >= 3, g.Id);
			Assert.True(File.Exists(Path.Combine(root, "src/AISV.Mod/assets/diary/life", g.Ending.Art + ".png")), "缺日记画 " + g.Id);
			Assert.True(g.Ending.Prop.Length == 0 || LifeRules.VanillaProp(g.Ending.Prop) != null || File.Exists(Path.Combine(root, "src/AISV.Mod/assets/life/props", g.Ending.Prop + ".png")), "缺道具 " + g.Id);
		}
	}

	[Fact]
	public void CatalogReportsMistakes()
	{
		LifeCatalog c = LifeCatalog.Parse("{\"goals\":[{\"id\":\"x\",\"npc\":\"Sam\",\"steps\":[\"一\"],\"help\":{\"step\":1,\"item\":\"\"},\"ending\":{\"cast\":[\"Abigail\"],\"lines\":[\"只有一段\"]}},{\"id\":\"x\",\"npc\":\"Sam\",\"steps\":[\"一\",\"二\"],\"ending\":{\"cast\":[\"Sam\"]}}]}");
		Assert.Contains(c.Problems, p => p.Contains("两步"));
		Assert.Contains(c.Problems, p => p.Contains("帮忙"));
		Assert.Contains(c.Problems, p => p.Contains("第一个到场"));
		Assert.Contains(c.Problems, p => p.Contains("台词"));
		Assert.Contains(c.Problems, p => p.Contains("重复"));
	}

	[Fact]
	public void OnlyStartsGoalsForFreeVillagersOnceAndFromTheRightYear()
	{
		LifeCatalog c = new LifeCatalog { Goals = { Def("Haley"), Def("Sam"), Def("Leah") } };
		c.Goals[2].FromYear = 2;
		LifeBook b = new LifeBook();
		b.Goals.Add(new LifeGoal { Id = "other", Npc = "Haley" });
		b.Goals.Add(new LifeGoal { Id = "sam_goal", Npc = "Sam", Outcome = "done" });
		Assert.Empty(LifeRules.Startable(c, b, _ => true, 1));
		Assert.Equal(new[] { "Leah" }, LifeRules.Startable(c, b, _ => true, 2).Select(g => g.Npc));
		Assert.Empty(LifeRules.Startable(c, b, n => n != "Leah", 2));
	}

	[Fact]
	public void HelpMoodAndFriendsMakeStepsLikelierAndWaitingSlowsThem()
	{
		LifeGoal g = new LifeGoal();
		double plain = LifeRules.StepChance(g, 0, 0, false);
		g.Helped = 1;
		Assert.True(LifeRules.StepChance(g, 0, 0, false) > plain);
		Assert.True(LifeRules.StepChance(g, 0, 0, true) < LifeRules.StepChance(g, 0, 0, false));
		Assert.True(LifeRules.StepChance(g, 80, 3, false) > LifeRules.StepChance(g, -80, 0, false));
		Assert.InRange(LifeRules.StepChance(new LifeGoal { Helped = 9 }, 100, 9, false), 0.15, 0.95);
		Assert.InRange(LifeRules.StepChance(new LifeGoal(), -100, 0, true), 0.15, 0.95);
	}

	[Fact]
	public void StuckGoalsMoveAfterThreeWeeks()
	{
		LifeGoal g = new LifeGoal { LastStepDay = 10 };
		Assert.False(LifeRules.Steps(g, 0.2, 0.9, 10 + 20));
		Assert.True(LifeRules.Steps(g, 0.2, 0.9, 10 + LifeRules.StuckWeeks * LifeRules.TickDays));
		Assert.True(LifeRules.Steps(g, 0.2, 0.1, 11));
	}

	[Fact]
	public void AdvancingAsksForHelpOnceAndFinishesOnTheLastStep()
	{
		LifeGoalDef def = Def(steps: 3, helpStep: 1);
		LifeGoal g = new LifeGoal { Id = def.Id, Npc = def.Npc };
		Assert.False(LifeRules.WantsHelp(g, def));
		Assert.False(LifeRules.Advance(g, def, 7));
		Assert.True(LifeRules.WantsHelp(g, def));
		g.HelpRequest = "rq";
		Assert.False(LifeRules.WantsHelp(g, def));
		Assert.False(LifeRules.Advance(g, def, 14));
		Assert.True(LifeRules.Advance(g, def, 21));
		Assert.Equal("done", g.Outcome);
		Assert.Equal(21, g.EndedDay);
		Assert.False(LifeRules.Advance(g, def, 28));
		Assert.Equal(3, g.Stage);
	}

	[Fact]
	public void TicksOncePerWeek()
	{
		LifeBook b = new LifeBook();
		Assert.True(LifeRules.Due(b, 1));
		b.LastTick = 1;
		Assert.False(LifeRules.Due(b, 1));
		Assert.False(LifeRules.Due(b, 7));
		Assert.True(LifeRules.Due(b, 8));
	}

	[Fact]
	public void OnePartnerAtATimeAndNoGettingBackTogether()
	{
		LifeBook b = new LifeBook();
		b.Couples.Add(new Couple { A = "Alex", B = "Haley", Phase = CouplePhases.Dating });
		b.Couples.Add(new Couple { A = "Sam", B = "Penny", Phase = CouplePhases.BrokenUp });
		bool Any(string _, string __) => true;
		bool Free(string _) => false;
		Assert.False(LifeRules.CanPair(b, "Haley", "Elliott", Any, Free));
		Assert.False(LifeRules.CanPair(b, "Sam", "Penny", Any, Free));
		Assert.True(LifeRules.CanPair(b, "Sam", "Leah", Any, Free));
		Assert.False(LifeRules.CanPair(b, "Sam", "Leah", Any, n => n == "Leah"));
		Assert.False(LifeRules.CanPair(b, "Sam", "Leah", (_, _) => false, Free));
		Assert.False(LifeRules.CanPair(b, "Sam", "sam", Any, Free));
	}

	[Fact]
	public void NewCouplesComeFromTheStrongestCrushAndAreCapped()
	{
		LifeBook b = new LifeBook();
		PairState[] crushes =
		{
			new PairState("Sam", "Leah", 0.5f, 0.35f, 0.1f),
			new PairState("Elliott", "Leah", 0.6f, 0.7f, 0.1f),
			new PairState("Shane", "Emily", 0.9f, 0.2f, 0.0f),
			new PairState("Harvey", "Maru", 0.9f, 0.9f, 0.5f)
		};
		PairState? p = LifeRules.NewCouple(crushes, b, (_, _) => true, _ => false);
		Assert.Equal(("Elliott", "Leah"), (p!.A, p.B));
		for (int i = 0; i < LifeRules.MaxNewCouples; i++)
		{
			b.Couples.Add(new Couple { A = "a" + i, B = "b" + i });
		}
		Assert.Null(LifeRules.NewCouple(crushes, b, (_, _) => true, _ => false));
	}

	[Fact]
	public void CouplesGetEngagedMarryOnTheDayOrBreakUp()
	{
		Couple c = new Couple { A = "Sam", B = "Leah", Since = 0 };
		PairState warm = new PairState("Sam", "Leah", 0.6f, 0.5f, 0.1f);
		Assert.Null(LifeRules.NextPhase(c, warm, 14, 0.0));
		Assert.Equal(CouplePhases.Engaged, LifeRules.NextPhase(c, warm, 21, 0.1));
		Assert.Null(LifeRules.NextPhase(c, warm, 21, 0.9));
		Assert.Equal(CouplePhases.BrokenUp, LifeRules.NextPhase(c, warm with { Strain = 0.6f }, 5, 0.1));
		c.Phase = CouplePhases.Engaged;
		c.WeddingDay = 30;
		Assert.Null(LifeRules.NextPhase(c, warm, 29, 0.9));
		Assert.Equal(CouplePhases.Married, LifeRules.NextPhase(c, warm, 30, 0.9));
		c.Phase = CouplePhases.Married;
		Assert.Null(LifeRules.NextPhase(c, warm with { Strain = 1f }, 99, 0.0));
		(float fondness, float spark) = LifeRules.Grow(warm);
		Assert.Equal(0.64f, fondness, 3);
		Assert.Equal(0.52f, spark, 3);
	}

	[Fact]
	public void BookAnswersWhoIsWithWhom()
	{
		LifeBook b = new LifeBook();
		b.Couples.Add(new Couple { A = "Sam", B = "Leah", Phase = CouplePhases.Married });
		b.Couples.Add(new Couple { A = "Alex", B = "Haley", Phase = CouplePhases.BrokenUp });
		b.Jobs.Add(new JobChange { Npc = "Leah", Job = "卖画" });
		Assert.True(b.IsWed("leah"));
		Assert.True(b.IsCouple("Leah", "Sam"));
		Assert.False(b.IsCouple("Alex", "Haley"));
		Assert.Null(b.CoupleOf("Haley"));
		Assert.Equal("Leah", b.CoupleOf("Sam")!.Other("Sam"));
		Assert.Equal("卖画", b.JobOf("LEAH")!.Job);
	}

	[Fact]
	public void EndingsPlayOnlyInTheirPlaceHoursAndWeek()
	{
		LifeGoalDef def = Def();
		def.Ending.Location = "Saloon";
		LifeGoal g = new LifeGoal { Outcome = "done", EndedDay = 10 };
		Assert.True(LifeRules.EndingDue(g, def, "Saloon", 12, 1900));
		Assert.False(LifeRules.EndingDue(g, def, "Saloon", 12, 1000));
		Assert.False(LifeRules.EndingDue(g, def, "Town", 12, 1900));
		Assert.False(LifeRules.EndingDue(g, def, "Saloon", 10 + LifeRules.EndingDays + 1, 1900));
		g.EndingShown = true;
		Assert.False(LifeRules.EndingDue(g, def, "Saloon", 12, 1900));
		def.Ending.Location = "Town";
		Assert.Equal((900, 1800), LifeRules.EndingHours("Town"));
	}

	[Fact]
	public void LinesKeepOnlyTheCast()
	{
		List<ConclusionLine> lines = LifeRules.Lines(new[] { "haley|你好|happy", "Gus|我不在场", "Emily|  ", "Emily|嗯" }, new[] { "Haley", "Emily" });
		Assert.Equal(new[] { "Haley", "Emily" }, lines.Select(l => l.Who));
		Assert.Equal("happy", lines[0].Feeling);
		Assert.Equal("neutral", lines[1].Feeling);
	}

	[Fact]
	public void PropCommandIsOneEventCommand()
	{
		string cmd = LifeRules.PropCommand("Mods/AIStardewValley.Core/Life/photo_board", 0, 0, 48, 40, 50, 58);
		Assert.StartsWith("/temporaryAnimatedSprite Mods\\AIStardewValley.Core\\Life\\photo_board 0 0 48 40 ", cmd);
		string[] args = cmd.TrimStart('/').Split(' ');
		Assert.Equal(19, args.Length);
		Assert.Equal("50", args[9]);
		Assert.Equal("58", args[10]);
		Assert.Equal("60", args[13]);
		Assert.DoesNotContain("/", cmd[1..]);
		Assert.Equal(("TileSheets/Craftables", 64, 448, 16, 32), LifeRules.VanillaProp("TileSheets/Craftables@64,448,16,32"));
		Assert.Null(LifeRules.VanillaProp("photo_board"));
		Assert.Null(LifeRules.VanillaProp("TileSheets/Craftables@64,448,16"));
	}

	[Fact]
	public void EndingsPutTheHeroInTheMiddleWhoeverSpeaksFirst()
	{
		ConclusionScript.Layout l = LifeRules.EndingLayout(30, 64, new[] { "Lewis", "Linus", "Marnie", "Gus" }, "Linus");
		Assert.Equal(new ConclusionScript.Spot(30, 64, 2), l.Npc(1));
		Assert.Equal(new ConclusionScript.Spot(28, 65, 1), l.Npc(0));
		Assert.Equal(new ConclusionScript.Spot(32, 65, 3), l.Npc(2));
		Assert.Equal(new ConclusionScript.Spot(30, 67, 0), l.Farmer);
		Assert.Equal(5, Enumerable.Range(0, 4).Select(i => (l.Npc(i).X, l.Npc(i).Y)).Append((l.Farmer.X, l.Farmer.Y)).Distinct().Count());
		// 98 像素宽的拱门（7 格）正中对着主角，底边在主角身后一排。
		Assert.Equal((27, 60), LifeRules.PropAt(30, 64, 0, -1, 98, 54));
		Assert.Equal((31, 63), LifeRules.PropAt(30, 64, 1, 0, 16, 32));
		Assert.Equal((30, 64), LifeRules.Stage("Town"));
	}

	[Fact]
	public void WeddingStandsLikeTheGameOwnWedding()
	{
		ConclusionScript.Layout l = LifeRules.WeddingLayout(27, 63, new[] { "Lewis", "Sam", "Leah", "Gus", "Pam" }, "Sam", "Leah", "Lewis");
		Assert.Equal(new ConclusionScript.Spot(27, 64, 0), l.Npc(0));
		Assert.Equal(new ConclusionScript.Spot(27, 63, 2), l.Npc(1));
		Assert.Equal(new ConclusionScript.Spot(28, 63, 2), l.Npc(2));
		Assert.Equal(6, Enumerable.Range(0, 5).Select(i => (l.Npc(i).X, l.Npc(i).Y)).Append((l.Farmer.X, l.Farmer.Y)).Distinct().Count());
		Assert.Equal(0, l.Npc(3).Facing);
		string[] decor = LifeRules.WeddingDecor().Split('/', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(6, decor.Length);
		Assert.All(decor, d => Assert.Equal(19, d.Split(' ').Length));
	}

	[Fact]
	public void LifeSketchesWaitUntilTheGoalIsDone()
	{
		List<DiaryEntry> entries = new List<DiaryEntry>
		{
			new DiaryEntry { Day = 1, Season = "spring", Year = 1, Text = "和 Haley 聊了几句" },
			new DiaryEntry { Day = 20, Season = "spring", Year = 1, Text = "去看了 Haley 的摄影展" }
		};
		DiarySketch life = new DiarySketch("life/haley_exhibit.png", "Haley", new GameDate(15, "spring", 1).TotalDays);
		List<PinnedSketch> pins = DiarySketches.Pin(entries, n => n, new[] { life, new DiarySketch("haley.png", "Haley") });
		Assert.Equal(2, pins.Count);
		Assert.Equal("haley.png", pins[0].Sketch.File);
		Assert.Equal(life, pins[1].Sketch);
	}

	[Fact]
	public void ModCouplesCountAsCouplesAndShowInTheFamilyTree()
	{
		Func<string, string, bool>? oldMarriage = VillageRelations.ExtraMarriage;
		Func<string, bool>? oldMarried = VillageRelations.ExtraMarried;
		Func<string, string, bool>? oldCouple = VillageRelations.ExtraCouple;
		try
		{
			// 只认这两个测试用的名字，不影响同时在跑的别的测试。
			VillageRelations.ExtraMarriage = (a, b) => new[] { a, b }.OrderBy(x => x).SequenceEqual(new[] { "LifeTestA", "LifeTestB" });
			VillageRelations.ExtraMarried = n => n is "LifeTestA" or "LifeTestB";
			VillageRelations.ExtraCouple = (a, b) => new[] { a, b }.OrderBy(x => x).SequenceEqual(new[] { "LifeTestC", "LifeTestD" });
			Assert.True(VillageRelations.IsMarriage("LifeTestB", "LifeTestA"));
			Assert.True(VillageRelations.IsMarriedNpc("LifeTestA"));
			Assert.True(VillageRelations.IsCouple("LifeTestD", "LifeTestC"));
			Assert.False(VillageRelations.IsCouple("LifeTestA", "LifeTestC"));
		}
		finally
		{
			VillageRelations.ExtraMarriage = oldMarriage;
			VillageRelations.ExtraMarried = oldMarried;
			VillageRelations.ExtraCouple = oldCouple;
		}
		Assert.Equal(PairKind.Dating, Kinship.ReadTitle("Sam", "Leah", "partner")!.Pair);
		Assert.Equal(PairKind.Engaged, Kinship.ReadTitle("Sam", "Leah", "fiance")!.Pair);
		Assert.Equal(PairKind.Married, Kinship.ReadTitle("Sam", "Leah", "spouse")!.Pair);
		Assert.Equal("在交往", RelationshipGraph.StatusWord("dating"));
	}
}
