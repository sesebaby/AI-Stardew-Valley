using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Storage;
using AISV.Core.Stories;
using Xunit;

namespace AISV.Core.Tests;

public class StoryArcTests
{
	private static readonly GameDate Spring3 = new GameDate(3, "spring", 1);

	private static readonly ArcUsable All = _ => true;

	private static string Assets(string name)
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "AISV.sln")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		return Path.Combine(dir!, "src", "AISV.Mod", "assets", name);
	}

	private static Func<DocumentStore?> Store(MemoryBackend? backend = null)
	{
		DocumentStore store = new DocumentStore(backend ?? new MemoryBackend(), NullLog.Instance);
		return () => store;
	}

	private static StoryArc Arc(string id, params string[] npcs) => new StoryArc
	{
		Id = id,
		Title = id,
		Cast = npcs.ToList(),
		Goals = new List<ArcObjective>
		{
			new ArcObjective { Id = id + "_1", Kind = "talk_to", Npc = npcs[0], DoneWhen = "c1" },
			new ArcObjective { Id = id + "_2", Kind = "talk_to", Npc = npcs[^1], DoneWhen = "c2" },
			new ArcObjective { Id = id + "_3", Kind = "talk_to", Npc = npcs[0], DoneWhen = "c3" }
		}
	};

	// ---- 检测和合并 ----

	[Theory]
	[InlineData(3, true)]
	[InlineData(10, true)]
	[InlineData(17, true)]
	[InlineData(24, true)]
	[InlineData(1, false)]
	[InlineData(4, false)]
	[InlineData(28, false)]
	public void AnalyzesOnThirdDayOfEachWeek(int day, bool expected)
	{
		Assert.Equal(expected, StoryArcBook.ShouldAnalyze(day));
	}

	[Fact]
	public void MergeCreatesAtMostOneAndUpdatesExisting()
	{
		StoryArcBook book = new StoryArcBook(Store());
		StoryArc first = book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, new[] { "a.b" }, All)!;
		Assert.Equal(StoryArc.Active, first.State);
		Assert.Equal("spring", first.BornSeason);
		Assert.Equal(StoryArcBook.ChoiceMend, first.ChosenEnding);
		Assert.Equal(3, first.Endings.Count);

		StoryArc update = Arc("tent", "Linus", "Gus");
		update.Stage = "后来";
		StoryArc? created = book.Merge(new[] { update, Arc("x", "Abigail", "Sam"), Arc("y", "Leah", "Elliott") }, Spring3, 3, Array.Empty<string>(), All);
		Assert.Equal("x", created!.Id);
		Assert.Equal(2, book.All.Count);
		Assert.Equal("后来", book.Get("tent")!.Stage);
		Assert.Contains("a.b", book.Get("tent")!.Sources);
	}

	[Fact]
	public void SameTitleUnderANewIdIsTheSameArc()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("the_unanswered_fourth", "Haley", "Emily") }, Spring3, 3, Array.Empty<string>(), All);
		StoryArc again = Arc("the_fourth_unanswered", "Haley", "Emily");
		again.Title = "the_unanswered_fourth";
		again.Stage = "后来";
		Assert.Null(book.Merge(new[] { again }, Spring3, 3, Array.Empty<string>(), All));
		Assert.Single(book.All);
		Assert.Equal("后来", book.Get("the_unanswered_fourth")!.Stage);
	}

	[Fact]
	public void MergeRespectsActiveLimitAndUsable()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("a", "Linus", "Gus") }, Spring3, 1, Array.Empty<string>(), All);
		Assert.Null(book.Merge(new[] { Arc("b", "Abigail", "Sam") }, Spring3, 1, Array.Empty<string>(), All));
		Assert.Null(book.Merge(new[] { Arc("c", "Abigail", "Sam") }, Spring3, 3, Array.Empty<string>(), a => !a.Involves("Sam")));
		Assert.Single(book.All);
	}

	[Fact]
	public void FizzlesAfterSeasonsWithoutProgress()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("idle", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All);
		book.Merge(new[] { Arc("moving", "Abigail", "Sam") }, Spring3, 3, Array.Empty<string>(), All);
		book.CompleteObjective("moving_1", "问了", "Farmer", Spring3, All, out _, out _);
		Assert.Empty(book.Fizzle(new GameDate(1, "summer", 1), 2, All));
		List<StoryArc> gone = book.Fizzle(new GameDate(1, "fall", 1), 2, All);
		Assert.Equal("idle", gone.Single().Id);
		Assert.Equal(StoryArc.Fizzled, book.Get("idle")!.State);
		Assert.Equal(StoryArc.Active, book.Get("moving")!.State);
	}

	[Fact]
	public void CompletingLastObjectiveMakesArcReady()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All);
		book.CompleteObjective("tent_1", "耐心听", "Farmer", Spring3, All, out ArcObjective? o, out bool ready);
		Assert.False(ready);
		Assert.Equal("spring 3, Year 1", o!.DoneOn);
		Assert.Equal("耐心听", o.DoneHow);
		book.CompleteObjective("TENT_2", null, "Farmer", Spring3, All, out _, out _);
		StoryArc? arc = book.CompleteObjective("tent_3", null, "Farmer", Spring3, All, out _, out ready);
		Assert.True(ready);
		Assert.Equal(StoryArc.Completable, arc!.State);
		Assert.Null(book.CompleteObjective("tent_3", null, "Farmer", Spring3, All, out _, out _));
	}

	[Fact]
	public void ChoicesOnlyChangeWhenCompletable()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All);
		Assert.False(book.SelectChoice("tent", StoryArcBook.ChoiceOpenUp));
		foreach (string id in new[] { "tent_1", "tent_2", "tent_3" })
		{
			book.CompleteObjective(id, null, "Farmer", Spring3, All, out _, out _);
		}
		Assert.False(book.SelectChoice("tent", "nope"));
		Assert.True(book.SelectChoice("tent", StoryArcBook.ChoiceOpenUp));
		Assert.Contains("挑明", StoryArcBook.Consequence(book.Get("tent")!));
		Assert.Equal("摊牌", StoryArcBook.Selected(book.Get("tent")!)!.Label);
		Assert.True(book.Delete("tent"));
		Assert.Empty(book.All);
	}

	[Fact]
	public void ValidateDropsItemsAndRejectsBadTargets()
	{
		List<ArcObjective> objs = Arc("a", "Linus", "Gus").Goals;
		objs.Add(new ArcObjective { Id = "a_4", Kind = "bring_item", Npc = "Gus" });
		Assert.Null(StoryArcBook.Validate(objs, new[] { "Linus", "Gus" }, "Farmer", out int dropped));
		Assert.Equal(1, dropped);
		Assert.Equal(3, objs.Count);

		List<ArcObjective> few = Arc("a", "Linus", "Gus").Goals;
		few[0].Kind = "bring_item";
		Assert.Contains("只剩 2", StoryArcBook.Validate(few, new[] { "Linus", "Gus" }, "", out _));

		List<ArcObjective> outsider = Arc("a", "Linus", "Gus").Goals;
		outsider[1].Npc = "Pam";
		Assert.Contains("Pam", StoryArcBook.Validate(outsider, new[] { "Linus", "Gus" }, "", out _));

		List<ArcObjective> player = Arc("a", "Linus", "Ann").Goals;
		Assert.Contains("玩家", StoryArcBook.Validate(player, new[] { "Linus", "Ann" }, "Bob, Ann", out _));
	}

	[Theory]
	[InlineData("hablar_con", "talk_to")]
	[InlineData("Conversation", "talk_to")]
	[InlineData("deliver_item", "bring_item")]
	[InlineData("dance", "dance")]
	public void ObjectiveTypesAreNormalized(string raw, string expected)
	{
		Assert.Equal(expected, StoryArcBook.NormalizeObjectiveType(raw));
	}

	[Fact]
	public void OldItemObjectivesAreDroppedOnLoad()
	{
		StoryArc arc = Arc("a", "Linus", "Gus");
		arc.Goals[0].Done = true;
		arc.Goals[1].Done = true;
		arc.Goals[2].Kind = "bring_item";
		Assert.True(StoryArcBook.DropItemObjectives(arc));
		Assert.Equal(StoryArc.Completable, arc.State);
		Assert.NotNull(arc.ChosenEnding);

		StoryArc onlyItems = Arc("b", "Linus", "Gus");
		onlyItems.Goals.ForEach(o => o.Kind = "bring_item");
		StoryArcBook.DropItemObjectives(onlyItems);
		Assert.Equal(StoryArc.Fizzled, onlyItems.State);
	}

	[Fact]
	public void ParseDetectionReadsArcsAndRejectsBadOnes()
	{
		string raw = "```json\n{\"storylines\":[" +
			"{\"key\":\"tent\",\"name\":\"帐篷\",\"premise\":\"d\",\"cast\":[\"莱纳斯\",\"Gus\"],\"right_now\":\"s\",\"state\":\"activo\",\"place\":\"Saloon\"," +
			"\"steps\":[{\"key\":\"t1\",\"kind\":\"hablar\",\"person\":\"莱纳斯\",\"done_when\":\"c\"},{\"key\":\"t2\",\"kind\":\"talk_to\",\"person\":\"Gus\",\"done_when\":\"c\"},{\"key\":\"t3\",\"kind\":\"talk_to\",\"person\":\"Gus\",\"done_when\":\"c\"},{\"key\":\"t4\",\"kind\":\"bring_item\",\"person\":\"Gus\",\"item\":\"Beer\"}]}," +
			"{\"key\":\"bad\",\"name\":\"坏\",\"cast\":[\"Gus\"],\"steps\":[{\"key\":\"b1\",\"kind\":\"talk_to\",\"person\":\"Gus\"}]}," +
			"{\"key\":\"out\",\"name\":\"走\",\"shape\":\"departure\",\"mover\":{\"who\":\"Gus\",\"how\":\"exiled\",\"why\":\"m\"},\"cast\":[\"Gus\",\"Pam\"],\"steps\":[{\"key\":\"o1\",\"kind\":\"talk_to\",\"person\":\"Gus\"},{\"key\":\"o2\",\"kind\":\"talk_to\",\"person\":\"Pam\"},{\"key\":\"o3\",\"kind\":\"talk_to\",\"person\":\"Pam\"}]}" +
			"]}\n```";
		List<StoryArc> arcs = StoryArcAi.ParseDetection(raw, n => n == "莱纳斯" ? "Linus" : n, "Farmer", NullLog.Instance)!;
		Assert.Equal(2, arcs.Count);
		StoryArc tent = arcs[0];
		Assert.Equal(new[] { "Linus", "Gus" }, tent.Cast);
		Assert.Equal("Linus", tent.Goals[0].Npc);
		Assert.Equal("talk_to", tent.Goals[0].Kind);
		Assert.Equal(3, tent.Goals.Count);
		Assert.Equal(StoryArc.Active, tent.State);
		Assert.False(tent.IsExpulsion);
		Assert.True(arcs[1].IsExpulsion);
		Assert.Equal("Gus", arcs[1].LeavingNpc);
		Assert.Null(StoryArcAi.ParseDetection("not json", n => n, "", NullLog.Instance));
		Assert.Empty(StoryArcAi.ParseDetection("{\"storylines\":[]}", n => n, "", NullLog.Instance)!);
	}

	[Fact]
	public void ParseConclusionSkipsFarmerLinesAndReadsOutcome()
	{
		StoryArc arc = Arc("out", "Gus", "Pam");
		arc.Kind = "expulsion";
		arc.LeavingNpc = "Gus";
		string raw = "{\"scene_lines\":[{\"who\":\"Ann\",\"says\":\"我\"},{\"who\":\"Farmer\",\"says\":\"x\"},{\"who\":\"潘姆\",\"text\":\"走吧\",\"feeling\":\"sad\"},{\"who\":\"Gus\",\"says\":\"再见\"}]," +
			"\"how_it_ended\":\"格斯走了\",\"afterwords\":{\"潘姆\":\"唉\",\"Lewis\":\"听说了\"},\"themes\":[\"exile\"]," +
			"\"departure\":{\"how\":\"left_village\",\"town_story\":\"格斯关了酒吧离开\"}}";
		ArcConclusion c = StoryArcAi.ParseConclusion(raw, arc, "Ann", n => n == "潘姆" ? "Pam" : n)!;
		Assert.Equal(new[] { "Pam", "Gus" }, c.Lines.Select(l => l.Who));
		Assert.Equal("走吧", c.Lines[0].Line);
		Assert.Equal("唉", c.PostLines["pam"]);
		Assert.Equal("left_village", c.OutcomeKind);
		Assert.Equal("格斯关了酒吧离开", c.Headline);
		Assert.Equal("格斯走了", c.Summary);
	}

	[Fact]
	public void ResolveAndPostResolutionLinesExpire()
	{
		StoryArcBook book = new StoryArcBook(Store());
		StoryArc arc = book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All)!;
		ArcConclusion c = new ArcConclusion { Summary = "说开了", PostLines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Linus"] = "谢谢你", ["Pam"] = "听说了" } };
		book.Resolve(arc, c, ConclusionStaging.BuiltInStages()[0], "Town", Spring3);
		Assert.Equal(StoryArc.Resolved, arc.State);
		Assert.Equal(Spring3.TotalDays, arc.EndedOn);
		Assert.Contains("拉回来", arc.Outcome);
		Assert.Equal("谢谢你", book.TakePostResolutionLine("linus", Spring3.TotalDays + 1, 3, All));
		Assert.Null(book.TakePostResolutionLine("Linus", Spring3.TotalDays + 1, 3, All));
		Assert.Null(book.TakePostResolutionLine("Pam", Spring3.TotalDays + 4, 3, All));
		Assert.Equal("听说了", book.TakePostResolutionLine("Pam", Spring3.TotalDays + 3, 3, All));
	}

	[Fact]
	public void PromptBlockShowsPendingObjectivesOnlyForTarget()
	{
		StoryArcBook book = new StoryArcBook(Store());
		book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All);
		book.CompleteObjective("tent_1", "x", "Ann", Spring3, All, out _, out _);
		string forGus = book.PromptBlock("Gus", n => n, All);
		Assert.Contains("[id:tent_2]", forGus);
		Assert.Contains("已办成 [id:tent_1]", forGus);
		Assert.Contains("经手：Ann", forGus);
		Assert.DoesNotContain("[id:tent_3]", forGus);
		Assert.Contains("step_done", forGus);
		Assert.Contains("☐ 等农夫来找你办 [id:tent_3]", book.PromptBlock("Linus", n => n, All));
		Assert.Equal("", book.PromptBlock("Pam", n => n, All));
	}

	[Fact]
	public void NormalizeFillsSourcesAndDropsItemObjectives()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write(StoryArcBook.Path, "{\"schema\":1,\"data\":[{\"id\":\"mod.x/old\",\"title\":\"旧\",\"cast\":[\"Linus\",\"Gus\"],\"state\":\"active\",\"born_season\":\"spring\",\"born_year\":1," +
			"\"goals\":[{\"id\":\"o1\",\"kind\":\"talk_to\",\"npc\":\"Linus\",\"done\":true},{\"id\":\"o2\",\"kind\":\"bring_item\",\"npc\":\"Gus\",\"item\":\"Beer\"},{\"id\":\"o3\",\"kind\":\"talk_to\",\"npc\":\"Gus\"}]}]}");
		Func<DocumentStore?> store = Store(backend);
		StoryArcBook book = new StoryArcBook(store);
		Assert.True(book.Normalize(n => n));
		StoryArc arc = book.All.Single();
		Assert.Equal(new[] { "mod.x" }, arc.Sources);
		Assert.Equal(2, arc.Goals.Count);
		Assert.Equal(StoryArcBook.ChoiceMend, arc.ChosenEnding);
	}

	// ---- 舞台 ----

	[Fact]
	public void StagesLoadFromAssetsWithUniqueIds()
	{
		ConclusionStaging staging = new ConclusionStaging(Assets("story_arc_conclusion_stages"), Assets("story_arc_conclusion_location_affinities"), n => n);
		Assert.Equal(22, staging.Stages.Count);
		Assert.Equal(22, staging.Stages.Select(s => s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
		Assert.NotEmpty(staging.Affinities("Abigail"));
		Assert.True(staging.Affinities("Abigail").Count <= 2);
	}

	[Fact]
	public void BuiltInStagesUsedWhenFolderMissing()
	{
		ConclusionStaging staging = new ConclusionStaging("/nonexistent", "/nonexistent", n => n);
		Assert.Equal(ConclusionStaging.BuiltInStages().Length, staging.Stages.Count);
		StoryArc arc = Arc("a", "Linus", "Gus");
		(ConclusionStage stage, LocationPlan plan) = staging.Choose(arc, new List<ConclusionLine>(), 10, "Ann");
		Assert.NotNull(stage);
		Assert.Equal("generic_fallback", plan.Source);
	}

	[Fact]
	public void PlanPrefersSharedPlacesAndIsDeterministic()
	{
		ConclusionStaging staging = new ConclusionStaging(Assets("story_arc_conclusion_stages"), Assets("story_arc_conclusion_location_affinities"), n => n);
		StoryArc arc = Arc("family", "Abigail", "Caroline", "Pierre");
		LocationPlan plan = staging.Plan(arc, new List<ConclusionLine>(), 7, null, "Ann");
		Assert.Equal("SeedShop", plan.Attempted[0]);
		Assert.Equal("npc_affinity", plan.Source);
		Assert.Equal(new[] { "Abigail", "Caroline", "Pierre" }, plan.Participants);
		Assert.Equal(staging.Seed(arc, new List<ConclusionLine>(), 30, "Ann"), staging.Seed(arc, new List<ConclusionLine>(), 30, "Ann"));
		Assert.NotEqual(staging.Seed(arc, new List<ConclusionLine>(), 30, "Ann"), staging.Seed(arc, new List<ConclusionLine>(), 31, "Ann"));
		var (stage, _) = staging.Choose(arc, new List<ConclusionLine>(), 30, "Ann");
		Assert.Equal(stage.Key, staging.Choose(arc, new List<ConclusionLine>(), 30, "Ann").Stage.Key);
	}

	[Fact]
	public void ScoreFavorsPreferredLocationAndTags()
	{
		ConclusionStage town = new ConclusionStage { Key = "t", Venue = "Town", OnlyFor = new List<string> { "community" }, Moods = new List<string> { "reconciliation" } };
		ConclusionStage saloon = new ConclusionStage { Key = "s", Venue = "Saloon" };
		StoryArc arc = Arc("a", "Gus", "Pam");
		arc.ChosenEnding = StoryArcBook.ChoiceMend;
		LocationPlan plan = new LocationPlan { Preferred = "Saloon", Allowed = new[] { "Saloon", "Town" }, Weights = new Dictionary<string, int> { ["Saloon"] = 5, ["Town"] = 1 } };
		Assert.Equal(130, ConclusionStaging.Score(saloon, arc, Array.Empty<string>(), plan));
		Assert.Equal(10 + 12 + 5, ConclusionStaging.Score(town, arc, new[] { "community" }, plan));
		arc.StagePlace = "Town";
		Assert.Equal(100 + 5, ConclusionStaging.Score(town, arc, Array.Empty<string>(), null));
	}

	[Fact]
	public void SceneIsSettledBeforeWritingAndStageMatchesIt()
	{
		ConclusionStaging staging = new ConclusionStaging(Assets("story_arc_conclusion_stages"), Assets("story_arc_conclusion_location_affinities"), n => n);
		StoryArc arc = Arc("family", "Abigail", "Caroline", "Pierre");
		LocationPlan plan = staging.Plan(arc, new List<ConclusionLine>(), staging.Seed(arc, new List<ConclusionLine>(), 30, "Ann"), null, "Ann");
		string other = plan.Allowed.First(l => !l.Equals(plan.Preferred, StringComparison.OrdinalIgnoreCase));
		arc.StagePlace = other;
		Assert.Equal(other, staging.SceneFor(arc, 30, "Ann"));
		Assert.Equal(other, staging.Choose(arc, new List<ConclusionLine>(), 30, "Ann").Stage.Venue);
		arc.StagePlace = "Hospital";
		Assert.Equal(plan.Preferred, staging.SceneFor(arc, 30, "Ann"));
	}

	[Fact]
	public void ScriptBuildsEventWithEmotesAndEscapes()
	{
		List<ConclusionLine> lines = new List<ConclusionLine>
		{
			new ConclusionLine { Who = "Gus", Line = "那晚/\"其实\"是我", Feeling = "sad" },
			new ConclusionLine { Who = "Linus", Line = "没事了", Feeling = "happy" },
			new ConclusionLine { Who = "Gus", Line = "谢谢", Feeling = "neutral" }
		};
		ConclusionScript.Layout layout = ConclusionScript.LayoutFor("Saloon", null);
		string script = ConclusionScript.Build(lines, layout, "tent_1");
		Assert.StartsWith("continue/10 18/farmer 9 18 2 Gus 11 18 3 Linus 12 18 3/", script);
		Assert.Contains("/speak Gus \"那晚 '其实'是我\"/emote Gus 28", script);
		Assert.Contains("/emote Linus 32", script);
		Assert.EndsWith("/AisvArcConclusionComplete tent_1/end", script);
		Assert.Equal(new ConclusionScript.Spot(13, 19, 3), new ConclusionScript.Layout(0, 0, layout.Farmer, new[] { new ConclusionScript.Spot(12, 18, 3) }).Npc(1));
	}

	[Fact]
	public void ScriptLocationFallsBackToTown()
	{
		StoryArc arc = Arc("a", "Gus", "Pam");
		arc.StagePlace = "Hospital";
		Assert.Equal("Town", ConclusionScript.Location(arc));
		arc.StagePlace = "Beach";
		Assert.Equal("Beach", ConclusionScript.Location(arc));
		arc.FinalStage = new ConclusionStage { Venue = "Hospital", Blocking = new StageLayout() };
		Assert.Equal("Hospital", ConclusionScript.Location(arc));
		arc.FinalScene = new List<ConclusionLine> { new ConclusionLine { Who = "Ann", Line = "x" }, new ConclusionLine { Who = "Gus", Line = "" }, new ConclusionLine { Who = "Pam", Line = "y" } };
		Assert.Equal("Pam", ConclusionScript.Playable(arc, "Ann").Single().Who);
		Assert.StartsWith("a_", ConclusionScript.EventId("a"));
	}

	// ---- 逐出和传说 ----

	[Fact]
	public void ExpelDetachesShopAndReturnRestoresIt()
	{
		Func<DocumentStore?> store = Store();
		ShopDetachBook shops = new ShopDetachBook(store);
		ExpulsionBook book = new ExpulsionBook(store, shops);
		StoryArc arc = Arc("out", "Pierre", "Lewis");
		arc.Kind = "expulsion";
		arc.LeavingNpc = "Pierre";
		arc.LeavingHow = "jail";
		arc.Closure = "皮埃尔被带走了";
		Assert.Null(book.Expel(arc, 50, null, n => n == "Pierre"));
		Assert.True(book.IsExpelled("pierre"));
		Assert.True(shops.IsDetached("Pierre"));
		ExpulsionRecord r = book.Get("Pierre")!;
		Assert.Equal(ExpulsionRecord.Arrested, r.How);
		Assert.Equal("皮埃尔被带走了", r.Why);
		Assert.Contains("expelled:Pierre", arc.Tags!);
		Assert.Contains("被带走，起因是皮埃尔被带走了", book.PromptBlock(n => n));

		StoryArc back = Arc("back", "Pierre", "Lewis");
		back.Kind = "expulsion_return";
		back.LeavingNpc = "Pierre";
		back.LeavingNews = "皮埃尔道歉后回来了";
		Assert.Null(book.Return(back, 80));
		Assert.False(book.IsExpelled("Pierre"));
		Assert.False(shops.IsDetached("Pierre"));
		Assert.Contains("如今已经回来了（皮埃尔道歉后回来了）", book.PromptBlock(n => n));
		Assert.NotNull(book.Return(back, 81));
	}

	[Fact]
	public void ProtectedVillagersCannotBeExpelled()
	{
		ExpulsionBook book = new ExpulsionBook(Store(), new ShopDetachBook(Store()));
		StoryArc arc = Arc("out", "Jas", "Marnie");
		arc.LeavingNpc = "Jas";
		Assert.NotNull(book.Expel(arc, 1, null, _ => false));
		arc.LeavingNpc = "Haley";
		Assert.NotNull(book.Expel(arc, 1, "haley", _ => false));
		Assert.Null(book.Expel(arc, 1, "Alex", _ => false));
		Assert.Equal(ExpulsionRecord.LeftVillage, book.Get("Haley")!.How);
	}

	[Theory]
	[InlineData("arrest", "arrested")]
	[InlineData("banished", "exiled")]
	[InlineData("whatever", "left_village")]
	public void ExpulsionKindsAreNormalized(string raw, string expected)
	{
		Assert.Equal(expected, ExpulsionBook.NormalizeKind(raw));
	}

	[Fact]
	public void LoreIsRelevantToAffectedAndGlobal()
	{
		Func<DocumentStore?> store = Store();
		ResolvedLore lore = new ResolvedLore(store, n => n != "Krobus", _ => true);
		StoryArc arc = Arc("break", "Haley", "Alex");
		arc.Closure = "海莉和亚历克斯分手了";
		arc.Tags = new List<string> { "relationship_breakup" };
		arc.Goals[0].DoneHow = "劝";
		lore.Add(arc, GameDateText.Of(Spring3));
		StoryArc town = Arc("fest", "Lewis", "Pam");
		town.Closure = "镇上办了新集市";
		town.Tags = new List<string> { "village" };
		lore.Add(town, GameDateText.Of(Spring3));
		StoryArc hidden = Arc("k", "Krobus", "Haley");
		hidden.Closure = "x";
		lore.Add(hidden, GameDateText.Of(Spring3));

		Assert.Equal("spring 3, Year 1", lore.All[0].WrittenOn);
		Assert.Contains("劝", lore.All[0].HowFarmerDidIt);
		Assert.Equal(2, lore.RelevantTo("Haley").Count);
		Assert.Single(lore.RelevantTo("Gus"));
		Assert.True(lore.IsStorySingle("Alex"));
		Assert.False(lore.IsStorySingle("Lewis"));
		lore.Expulsions = () => "【镇上人尽皆知的去留】";
		string block = lore.PromptBlock("Haley", n => n);
		Assert.Contains("分手了", block);
		Assert.Contains("人尽皆知的去留", block);
	}

	[Fact]
	public void ParseConclusionAcceptsOtherKeyCasesAndAliases()
	{
		StoryArc arc = Arc("out", "Gus", "Pam");
		arc.Kind = "expulsion";
		arc.LeavingNpc = "Gus";
		string raw = "{\"scene_lines\":[{\"Who\":\"Gus\",\"Says\":\"再见\",\"Feeling\":\"sad\"},{\"who\":\"Pam\",\"line\":\"走好\"}],\"departure\":{\"How\":\"jail\",\"Town_Story\":\"H\"},\"afterwords\":[]}";
		ArcConclusion c = StoryArcAi.ParseConclusion(raw, arc, "Ann", n => n)!;
		Assert.Equal("Gus", c.Lines[0].Who);
		Assert.Equal("再见", c.Lines[0].Line);
		Assert.Equal("sad", c.Lines[0].Feeling);
		Assert.Equal("走好", c.Lines[1].Line);
		Assert.Equal("jail", c.OutcomeKind);
		Assert.Equal("H", c.Headline);
		Assert.Empty(c.PostLines);
	}

	[Fact]
	public void ReturnKeepsShopsThatWereDetachedForOtherReasons()
	{
		Func<DocumentStore?> store = Store();
		ShopDetachBook shops = new ShopDetachBook(store);
		ExpulsionBook book = new ExpulsionBook(store, shops);
		shops.Set("Pierre", true);
		StoryArc arc = Arc("out", "Pierre", "Lewis");
		arc.LeavingNpc = "Pierre";
		book.Expel(arc, 10, null, _ => true);
		Assert.False(book.Get("Pierre")!.ClosedShop);
		book.Return(arc, 20);
		Assert.True(shops.IsDetached("Pierre"));
	}

	[Theory]
	[InlineData("Beach", 39, 34)]
	[InlineData("Mountain", 34, 15)]
	[InlineData("Forest", 30, 41)]
	[InlineData("CommunityCenter", 20, 20)]
	[InlineData("ArchaeologyHouse", 10, 5)]
	[InlineData("Town", 52, 61)]
	public void FallbackLayoutsPutFarmerLeftOfCamera(string location, int x, int y)
	{
		ConclusionScript.Layout l = ConclusionScript.LayoutFor(location, null);
		Assert.Equal((x, y), (l.CameraX, l.CameraY));
		Assert.Equal(new ConclusionScript.Spot(x - 1, y, 2), l.Farmer);
		Assert.Equal(new ConclusionScript.Spot(x + 1, y, 3), l.Npcs[0]);
		Assert.Equal(52, ConclusionScript.LayoutFor("Town", new ConclusionStage { Key = "any/stage" }).CameraX);
	}

	[Fact]
	public void PlanFallsBackToSceneThenSafePlaces()
	{
		ConclusionStaging staging = new ConclusionStaging("/nonexistent", "/nonexistent", n => n);
		StoryArc arc = Arc("a", "Nobody1", "Nobody2");
		arc.StagePlace = "Beach";
		LocationPlan plan = staging.Plan(arc, new List<ConclusionLine>(), 3, new[] { "Beach", "Town" }, "Ann");
		Assert.Equal("scene_location_fallback", plan.Source);
		Assert.Equal("Beach", plan.Preferred);
		plan = staging.Plan(arc, new List<ConclusionLine>(), 3, new[] { "Saloon" }, "Ann");
		Assert.Equal("generic_fallback", plan.Source);
		Assert.Equal(new[] { "Saloon" }, plan.Allowed);
	}

	[Fact]
	public void PeekDoesNotConsumePostResolutionLine()
	{
		StoryArcBook book = new StoryArcBook(Store());
		StoryArc arc = book.Merge(new[] { Arc("tent", "Linus", "Gus") }, Spring3, 3, Array.Empty<string>(), All)!;
		book.Resolve(arc, new ArcConclusion { PostLines = new Dictionary<string, string> { ["Linus"] = "谢谢" } }, ConclusionStaging.BuiltInStages()[0], null, Spring3);
		Assert.Equal("谢谢", book.PeekPostResolutionLine("Linus", Spring3.TotalDays, 3, All)!.Value.Line);
		Assert.NotNull(book.PeekPostResolutionLine("Linus", Spring3.TotalDays, 3, All));
		book.ConsumePostResolutionLine("tent", "Linus");
		Assert.Null(book.PeekPostResolutionLine("Linus", Spring3.TotalDays, 3, All));
	}
}
