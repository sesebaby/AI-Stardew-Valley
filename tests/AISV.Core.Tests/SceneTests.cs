using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Scenes;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class SceneTests
{
	private static string Root()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "AISV.sln")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		return dir!;
	}

	private static string ScenesFile() => Path.Combine(Root(), "src", "AISV.Mod", "assets", "scenes", "scenes.json");

	private static SceneCatalog Catalog() => SceneCatalog.Load(File.ReadAllText(ScenesFile()), NullLog.Instance);

	private static SceneDefinition One(string json) => SceneCatalog.Load("[" + json + "]").All.Single();

	private const string Pair = "{\"id\":\"t.pair\",\"name\":\"两个人\",\"place\":\"Town\",\"event_code\":\"900001\",\"camera\":[10,10],\"players\":[[\"farmer\",9,10,2],[\"Alex\",11,10,3],[\"Haley\",12,10,3]],\"script_rows\":[[\"Alex\",\"第一句$h\"],[\"Haley\",\"第二/句\"],[\"Alex\",\"第三\\\"句\"]],\"gesture\":[\"Haley\",32],\"hours\":[900,1800],\"keepsake\":\"记住了\"}";

	private static SceneDay Day(string season = "spring", int day = 5, int year = 1, string weather = "Sunny", Dictionary<string, int>? hearts = null) =>
		new SceneDay(year, season, day, new GameDate(day, season, year).TotalDays, weather, "123", "Farm_123", hearts ?? new Dictionary<string, int>());

	// ---- 目录 ----

	[Fact]
	public void BuiltInCatalogLoadsEveryScene()
	{
		SceneCatalog c = Catalog();
		Assert.Equal(117, c.Count);
		Assert.Equal(c.Count, c.All.Select(d => d.EventCode).Distinct().Count());
		Assert.All(c.All, d =>
		{
			Assert.Matches(@"^aisv\.[a-z_]+\.[a-z0-9_]+$", d.Id);
			Assert.InRange(int.Parse(d.EventCode), 9760001, 9760999);
			Assert.NotEmpty(d.Name);
			Assert.InRange(d.Lines.Count, 2, 4);
			Assert.InRange(d.Speakers.Count, 1, 2);
			Assert.Equal("farmer", d.Actors[0].Name);
			Assert.Equal(d.Lead, d.Actors[1].Name);
			Assert.NotNull(d.EmoteCue);
			Assert.DoesNotContain("{{", SceneCatalog.Script(d));
		});
		Assert.Equal(58, c.All.Count(d => d.RecordsMemory));
		Assert.Equal(56, c.All.Count(d => d.Speakers.Count == 1));
		SceneDefinition pot = c.Find("AISV.wizardhouse.restless_cauldron")!;
		Assert.Equal(("WizardHouse", "9760097", 70, 5), (pot.Place, pot.EventCode, pot.Odds, pot.Needs.AfterDays!.Value));
		Assert.Equal(new[] { "Rainy" }, pot.Needs.InWeather);
		Assert.Equal(new[] { "Wizard" }, pot.Speakers);
		Assert.Equal((1000, 1600, 12), (pot.WindowMin, pot.WindowMax, pot.MaxDistanceTiles));
		Assert.Equal(17, c.At("town").Count);
	}

	[Fact]
	public void ScriptPutsEmoteAfterSecondLineAndEscapesText()
	{
		SceneDefinition d = One(Pair);
		Assert.Equal("continue/10 10/farmer 9 10 2 Alex 11 10 3 Haley 12 10 3/skippable/AisvSceneDone t.pair/speak Alex \"第一句$h\"/speak Haley \"第二／句\"/emote Haley 32/speak Alex \"第三”句\"/end", SceneCatalog.Script(d));
		Assert.Equal("900001/AisvSceneArmed t.pair/AisvSceneTime 900 1800", SceneCatalog.EventKey(d));
		Assert.Equal("900002/AisvSceneArmed t.one", SceneCatalog.EventKey(One("{\"id\":\"t.one\",\"place\":\"Town\",\"event_code\":\"900002\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"]]}")));
	}

	[Fact]
	public void BadEntriesAreSkippedAndValuesClamped()
	{
		List<string> warnings = new List<string>();
		string ok = "\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"]]";
		SceneCatalog c = SceneCatalog.Load("[" + string.Join(",",
			"{\"place\":\"Town\",\"event_code\":\"1\"," + ok + "}",
			"{\"id\":\"a\",\"event_code\":\"1\"," + ok + "}",
			"{\"id\":\"b\",\"place\":\"Town\"," + ok + "}",
			"{\"id\":\"c\",\"place\":\"Town\",\"event_code\":\"1\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[]}",
			"{\"id\":\"d\",\"place\":\"Town\",\"event_code\":\"1\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Sam\",\"嗨\"]]}",
			"{\"id\":\"e\",\"place\":\"Town\",\"event_code\":\"1\",\"odds\":-5,\"rest_days\":-2,\"reach\":99,\"needs\":{\"luck\":3},\"hours\":[1800,900]," + ok + "}",
			"{\"id\":\"E\",\"place\":\"Town\",\"event_code\":\"2\"," + ok + "}") + "]", new ListLog(warnings));
		SceneDefinition e = c.All.Single();
		Assert.Equal((1, 0, 30, 1.0, 1800, 900), (e.Odds, e.RestDays, e.MaxDistanceTiles, e.Needs.Luck, e.WindowMin, e.WindowMax));
		Assert.Equal(6, warnings.Count);
		Assert.Empty(SceneCatalog.Load("not json").All);
		Assert.Empty(SceneCatalog.Load(null).All);
	}

	[Fact]
	public void DeliveryFollowsStyleAndSpeakers()
	{
		SceneDefinition pair = One(Pair);
		SceneDefinition solo = One("{\"id\":\"s\",\"place\":\"Town\",\"event_code\":\"3\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2],[\"Haley\",2,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"],[\"Alex\",\"再见\"]]}");
		Assert.Equal(SceneDelivery.Overheard, SceneCatalog.Delivery(pair, "natural"));
		Assert.Equal(SceneDelivery.WalkUp, SceneCatalog.Delivery(solo, "AmbientOnly"));
		Assert.Equal(SceneDelivery.Cinematic, SceneCatalog.Delivery(solo, "Cinematic"));
		Assert.Equal(SceneDelivery.Overheard, SceneCatalog.Delivery(pair, "什么"));
		Assert.Equal(new[] { "Alex" }, solo.Speakers);
	}

	// ---- 每天挑场景 ----

	[Fact]
	public void OldSceneIdsInTheSaveAreDropped()
	{
		SceneCatalog c = SceneCatalog.Load("[{\"id\":\"aisv.town.new\",\"place\":\"Town\",\"event_code\":\"9760001\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"]],\"repeats\":1}]");
		SceneState state = new SceneState
		{
			DayKey = Day().Key,
			Loaded = new List<string> { "alive.old_one", "aisv.town.new" },
			PlayedToday = new List<string> { "alive.old_one" },
			TimesPlayed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["alive.old_one"] = 1, ["aisv.town.new"] = 0 },
			RestingUntil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["alive.old_two"] = 99 }
		};
		Assert.Equal(new[] { "aisv.town.new" }, ScenePlanner.Plan(c.All, state, Day(), 5));
		Assert.Empty(state.PlayedToday);
		Assert.Equal(new[] { "aisv.town.new" }, state.TimesPlayed.Keys);
		Assert.Empty(state.RestingUntil);

		SceneState untouched = new SceneState { Loaded = new List<string> { "alive.old_one" } };
		ScenePlanner.Forget(untouched, Array.Empty<SceneDefinition>());
		Assert.Single(untouched.Loaded);
	}

	[Fact]
	public void PlannerChecksConditionsAndPicksOnePerLocation()
	{
		string Scene(string id, string loc, string extra = "") => "{\"id\":\"" + id + "\",\"place\":\"" + loc + "\",\"event_code\":\"" + id.GetHashCode() + "\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"]]" + extra + "}";
		SceneCatalog c = SceneCatalog.Load("[" + string.Join(",",
			Scene("town1", "Town"), Scene("town2", "Town"), Scene("beach", "Beach"),
			Scene("summer", "Forest", ",\"needs\":{\"in_seasons\":[\"summer\"]}"),
			Scene("rain", "Mine", ",\"needs\":{\"in_weather\":[\"Rainy\"]}"),
			Scene("hearts", "Saloon", ",\"needs\":{\"hearts_at_least\":{\"Alex\":4}}"),
			Scene("late", "Desert", ",\"needs\":{\"after_days\":30}"),
			Scene("never", "Hospital", ",\"needs\":{\"luck\":0}")) + "]");
		SceneState state = new SceneState();
		IReadOnlyList<string> armed = ScenePlanner.Plan(c.All, state, Day(hearts: new Dictionary<string, int> { ["Alex"] = 3 }), 5);
		Assert.Equal(2, armed.Count);
		Assert.Single(armed, id => id.StartsWith("town"));
		Assert.Contains("beach", armed);

		Assert.Equal(armed, ScenePlanner.Plan(c.All, state, Day(hearts: new Dictionary<string, int> { ["Alex"] = 3 }), 5));
		SceneState again = new SceneState();
		Assert.Equal(armed, ScenePlanner.Plan(c.All, again, Day(hearts: new Dictionary<string, int> { ["Alex"] = 3 }), 5));

		SceneState summer = new SceneState();
		IReadOnlyList<string> all = ScenePlanner.Plan(c.All, summer, Day("summer", 10, 1, "Rainy", new Dictionary<string, int> { ["Alex"] = 4 }), 10);
		Assert.Contains("summer", all);
		Assert.Contains("rain", all);
		Assert.Contains("hearts", all);
		Assert.Contains("late", all);
		Assert.DoesNotContain("never", all);

		Assert.Empty(ScenePlanner.Plan(c.All, new SceneState(), Day(), 0));
		Assert.Empty(ScenePlanner.Plan(c.All, new SceneState(), Day(), 5, _ => false));
	}

	[Fact]
	public void PlannerRespectsCountsCooldownsAndNewDay()
	{
		SceneCatalog c = SceneCatalog.Load("[" + Pair + "]");
		SceneDefinition d = c.All[0];
		SceneState state = new SceneState { TimesPlayed = { ["t.pair"] = 1 } };
		Assert.Empty(ScenePlanner.Plan(c.All, state, Day(), 1));
		state = new SceneState { RestingUntil = { ["t.pair"] = Day().TotalDays } };
		Assert.Empty(ScenePlanner.Plan(c.All, state, Day(), 1));
		state = new SceneState { RestingUntil = { ["t.pair"] = Day().TotalDays - 1 }, PlayedToday = { "x" } };
		Assert.Equal(new[] { "t.pair" }, ScenePlanner.Plan(c.All, state, Day(), 1));
		Assert.Empty(state.PlayedToday);
		Assert.Equal("Y1-spring-5", state.DayKey);
		d.Repeats = 0;
		Assert.Single(ScenePlanner.Plan(c.All, new SceneState { TimesPlayed = { ["t.pair"] = 9 } }, Day(), 1));
		Assert.NotEqual(ScenePlanner.Seed(Day()), ScenePlanner.Seed(Day(day: 6)));
		Assert.Equal(1281391461, ScenePlanner.Seed(Day()));
	}

	[Fact]
	public void PlannerSkipsUndeliverableBeforeRollingChance()
	{
		string Scene(string id, string loc, double chance) => "{\"id\":\"" + id + "\",\"place\":\"" + loc + "\",\"event_code\":\"" + (id.Length * 1000 + loc.Length) + "\",\"camera\":[1,1],\"players\":[[\"Alex\",1,1,2]],\"script_rows\":[[\"Alex\",\"嗨\"]],\"needs\":{\"luck\":" + chance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";
		SceneCatalog c = SceneCatalog.Load("[" + Scene("away", "Beach", 0.5) + "," + Scene("here", "Town", 0.5) + "]");
		Random rng = new Random(ScenePlanner.Seed(Day()));
		bool expectHere = rng.NextDouble() <= 0.5;
		IReadOnlyList<string> armed = ScenePlanner.Plan(c.All, new SceneState(), Day(), 5, d => d.Id != "away");
		Assert.Equal(expectHere, armed.Contains("here"));
		Assert.DoesNotContain("away", armed);
	}

	[Fact]
	public void BookCompletesArmsAndClears()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		SceneBook book = new SceneBook(() => store);
		SceneDefinition d = One(Pair);
		d.RestDays = 4;
		book.Complete(d, 20);
		book.Complete(d, 20);
		Assert.Equal((2, 24), (book.State.TimesPlayed["t.pair"], book.State.RestingUntil["t.pair"]));
		Assert.True(book.PlayedToday("T.PAIR"));
		book.Arm("t.pair");
		Assert.False(book.PlayedToday("t.pair"));
		Assert.Equal(new[] { "t.pair" }, book.ClearDay("Y1-spring-6"));
		Assert.Empty(book.State.Loaded);
		Assert.Empty(book.ClearDay("Y1-spring-6"));
		Assert.Empty(new SceneBook(() => null).State.Loaded);
	}

	[Fact]
	public void LoadedStateIsTidied()
	{
		MemoryBackend main = new MemoryBackend();
		main.Write(SceneBook.Path, "{\"schema\":1,\"data\":{\"day_key\":\"Y1-spring-3\",\"loaded\":[\"a\",\" a \",\"\"],\"played_today\":[\"a\"],\"times_played\":{\"a\":2,\"b\":-1},\"resting_until\":{\"a\":10}}}");
		SceneState s = new SceneBook(() => new DocumentStore(main, NullLog.Instance)).State;
		Assert.Equal(("Y1-spring-3", 1, 2, 0, 10), (s.DayKey, s.Loaded.Count, s.TimesPlayed["a"], s.TimesPlayed["b"], s.RestingUntil["a"]));
	}

	// ---- 偶遇规则 ----

	[Fact]
	public void ReadinessChecksInOrder()
	{
		MomentReadiness ok = new MomentReadiness(5000, 0, 1000, 900, 1800, false, false, 0, -1, false);
		Assert.Equal(MomentBlock.None, MomentRules.Check(ok));
		Assert.Equal(MomentBlock.WorldBusy, MomentRules.Check(ok with { WorldBusy = true, PlayerBusy = true }));
		Assert.Equal(MomentBlock.PlayerBusy, MomentRules.Check(ok with { PlayerBusy = true, Time = 2000 }));
		Assert.Equal(MomentBlock.OutsideWindow, MomentRules.Check(ok with { Time = 1810 }));
		Assert.Equal(MomentBlock.JustArrived, MomentRules.Check(ok with { EnteredTick = 4800 }));
		Assert.Equal(MomentBlock.OutOfTries, MomentRules.Check(ok with { Tries = 3 }));
		Assert.Equal(MomentBlock.TooSoon, MomentRules.Check(ok with { LastTryTick = 3300 }));
		Assert.Equal(MomentBlock.None, MomentRules.Check(ok with { LastTryTick = 3200 }));
		Assert.Equal(MomentBlock.SomethingElseRunning, MomentRules.Check(ok with { OtherRunning = true }));
	}

	[Fact]
	public void DecideChoosesWalkUpOverheardOrWait()
	{
		SceneLine[] pairLines = { new SceneLine("Alex", "嗨"), new SceneLine("Haley", "你好") };
		SceneLine[] soloLines = { new SceneLine("Alex", "嗨") };
		string[] pair = { "Alex", "Haley" };
		MomentPresence[] both = { new MomentPresence("Alex", true, true), new MomentPresence("Haley", true, true) };
		MomentPresence[] leadOnly = { new MomentPresence("Alex", true, true), new MomentPresence("Haley", false, false) };

		Assert.Equal(MomentPlanKind.Wait, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, new[] { new MomentPresence("Alex", false, false) }, true, pairLines).Kind);
		Assert.Equal(MomentPlanKind.Skip, MomentRules.Decide(SceneDelivery.Cinematic, "Natural", pair, both, true, pairLines).Kind);
		MomentPlan overheard = MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, both, true, pairLines);
		Assert.Equal((MomentPlanKind.Overheard, 2), (overheard.Kind, overheard.Speakers.Count));
		Assert.Equal(MomentPlanKind.WalkUp, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, leadOnly, true, pairLines).Kind);
		MomentPlan monologue = MomentRules.Decide(SceneDelivery.Overheard, "AmbientOnly", pair, leadOnly, true, pairLines);
		Assert.Equal((MomentPlanKind.Overheard, 1), (monologue.Kind, monologue.Speakers.Count));
		Assert.Equal(MomentPlanKind.WalkUp, MomentRules.Decide(SceneDelivery.WalkUp, "Natural", new[] { "Alex" }, leadOnly, true, soloLines).Kind);
		Assert.Equal(MomentPlanKind.Overheard, MomentRules.Decide(SceneDelivery.WalkUp, "AmbientOnly", new[] { "Alex" }, leadOnly, true, soloLines).Kind);
		Assert.Equal(MomentPlanKind.Overheard, MomentRules.Decide(SceneDelivery.WalkUp, "Natural", new[] { "Alex" }, new[] { new MomentPresence("Alex", true, false) }, true, soloLines).Kind);

		Assert.Equal(MomentPlanKind.Wait, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, leadOnly, false, pairLines).Kind);
		Assert.Equal(MomentPlanKind.Overheard, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, both, false, pairLines).Kind);
		Assert.Equal(MomentPlanKind.WalkUp, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, leadOnly, false, soloLines).Kind);
		Assert.Equal(MomentPlanKind.Skip, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, both, false, Array.Empty<SceneLine>()).Kind);
		Assert.Equal(MomentPlanKind.Skip, MomentRules.Decide(SceneDelivery.Overheard, "Natural", pair, both, false, new[] { new SceneLine("Alex", "嗨"), new SceneLine("Sam", "?") }).Kind);
	}

	[Fact]
	public void ForecastScopeAndSchedule()
	{
		SceneLine[] lines = { new SceneLine("Alex", "嗨"), new SceneLine("Haley", "你好") };
		Assert.Equal(new[] { "Alex" }, MomentRules.ForecastScope(new[] { "Alex", "Haley" }, lines, true).Speakers);
		Assert.Equal(new[] { "Alex", "Haley" }, MomentRules.ForecastScope(new[] { "Alex", "Haley" }, lines, false).Speakers);
		Assert.Empty(MomentRules.ForecastScope(new[] { "Alex" }, Array.Empty<SceneLine>(), false).Speakers);
		Assert.Empty(MomentRules.ForecastScope(Array.Empty<string>(), lines, true).Speakers);

		ScheduleStop[] stops = { new ScheduleStop(900, "Town"), new ScheduleStop(1300, "Saloon") };
		Assert.True(MomentRules.ExpectedAt("SamHouse", stops, "Town", 1000, 1200));
		Assert.False(MomentRules.ExpectedAt("SamHouse", stops, "Town", 1300, 1500));
		Assert.True(MomentRules.ExpectedAt("SamHouse", stops, "Saloon", 2500, 2600));
		Assert.True(MomentRules.ExpectedAt("Town", stops, "Town", 600, 700));
		Assert.False(MomentRules.ExpectedAt("Town", new[] { new ScheduleStop(900, "Beach") }, "Town", 900, 1000));
		Assert.True(MomentRules.ExpectedAt("Town", new[] { new ScheduleStop(900, "Beach") }, "Town", 850, 900));
		Assert.True(MomentRules.ExpectedAt("Town", Array.Empty<ScheduleStop>(), "town", 2000, 1000));
		Assert.False(MomentRules.ExpectedAt("Beach", Array.Empty<ScheduleStop>(), "Town", 600, 2600));
		Assert.False(MomentRules.ExpectedAt("Town", stops, "", 600, 2600));
	}

	[Fact]
	public void WalkUpFocusAndLeadDialogue()
	{
		string focus = MomentRules.WalkUpFocus("Alex: 嗨 / Haley: \"你好\"", new[] { "Haley", "haley" });
		Assert.Contains("名字已经喊过了", focus);
		Assert.Contains("Alex: 嗨 / Haley: '你好'", focus);
		Assert.Contains("说到Haley时带一句", focus);
		Assert.Contains("别照念", focus);
		Assert.Contains("别故弄玄虚", focus);
		string bare = MomentRules.WalkUpFocus("", Array.Empty<string>());
		Assert.DoesNotContain("说到", bare);
		Assert.DoesNotContain("别照念", bare);
		SceneLine[] solo = { new SceneLine("Alex", "一"), new SceneLine("Alex", "二") };
		Assert.Equal("一#$b#二", MomentRules.LeadDialogue(solo, "Alex"));
		Assert.Null(MomentRules.LeadDialogue(solo.Append(new SceneLine("Sam", "三")).ToList(), "Alex"));
		Assert.Equal("Alex: 第一句 / Haley: 第二/句 / Alex: 第三\"句", SceneCatalog.Premise(One(Pair).Lines));
		Assert.EndsWith("…", SceneCatalog.Premise(new[] { new SceneLine("Alex", new string('长', 500)) }));
		Assert.True(SceneCatalog.Premise(new[] { new SceneLine("Alex", new string('长', 500)) }).Length <= MomentRules.MaxPremiseChars);
	}

	// ---- 气泡 ----

	[Fact]
	public void BubblesCleanSplitAndTime()
	{
		Assert.Equal("你好 再见", SceneBubbles.Clean("“你好$h#$b#再见”"));
		Assert.Equal("他说哈", SceneBubbles.Clean("${他^她}$说%哈"));
		Assert.Equal("Hi there", SceneBubbles.Clean("\"Hi there$7\""));
		Assert.Single(SceneBubbles.Split("短短一句。"));
		string longZh = string.Concat(Enumerable.Repeat("今天天气真不错。", 10));
		IReadOnlyList<string> parts = SceneBubbles.Split(longZh);
		Assert.All(parts, p => Assert.True(p.Length <= SceneBubbles.MaxChars));
		Assert.Equal(longZh, string.Concat(parts));
		IReadOnlyList<string> en = SceneBubbles.Split("One two three. Four five six seven. Eight.", 20);
		Assert.Equal(new[] { "One two three.", "Four five six seven.", "Eight." }, en);
		IReadOnlyList<string> word = SceneBubbles.Split(new string('啊', 130), 60);
		Assert.Equal(3, word.Count);
		Assert.All(word, w => Assert.InRange(w.Length, 40, 60));

		IReadOnlyList<OverheardCue> cues = SceneBubbles.Timeline(new[] { new SceneLine("Alex", "嗨"), new SceneLine("Haley", new string('长', 200)), new SceneLine("", "无人") }, 100);
		Assert.Equal(("Alex", 130, 150), (cues[0].Speaker, cues[0].StartTick, cues[0].DurationTicks));
		Assert.Equal(130 + 150 + 24, cues[1].StartTick);
		Assert.Equal(Math.Clamp(90 + 3 * cues[1].Text.Length, 150, 420), cues[1].DurationTicks);
		Assert.Equal(5, cues.Count);
		Assert.All(cues, c => Assert.InRange(c.DurationTicks, 150, 420));
	}

	// ---- 旁听的 AI ----

	[Fact]
	public void OverheardParseAcceptsSeveralShapes()
	{
		string[] pair = { "Alex", "Haley" };
		IReadOnlyList<SceneLine>? lines = OverheardAi.Parse("```json\n{\"script_rows\":[{\"speaker\":\"alex\",\"text\":\"*笑* 嗨$h\"},{\"name\":\"Sam\",\"text\":\"不在\"},{\"npc\":\"Haley\",\"line\":\"Haley: 你好\"}]}\n```", pair, 4);
		Assert.Equal(new[] { new SceneLine("Alex", "嗨"), new SceneLine("Haley", "你好") }, lines);
		Assert.Equal("Alex", OverheardAi.Parse("[\"Alex: 嗨\", \"Haley:你好\", \"没名字\"]", pair, 4)![0].Speaker);
		Assert.Equal(2, OverheardAi.Parse("[\"Alex: 嗨\", \"Haley:你好\", \"没名字\"]", pair, 4)!.Count);
		Assert.Equal("自己说", OverheardAi.Parse("{\"text\":\"自己说\"}", new[] { "Alex" }, 2)![0].Text);
		Assert.Single(OverheardAi.Parse("{\"talk\":[\"一\",\"二\",\"三\"]}", new[] { "Alex" }, 1)!);
		Assert.Null(OverheardAi.Parse("{\"text\":\"谁说的\"}", pair, 2));
		Assert.Null(OverheardAi.Parse("不是 JSON", pair, 2));
		Assert.Null(OverheardAi.Parse("{\"script_rows\":[]}", pair, 2));
		Assert.Equal((2, 4), (OverheardAi.MaxLines(1), OverheardAi.MaxLines(3)));
	}

	[Fact]
	public void ShortenPrefersSentenceEnds()
	{
		string text = new string('一', 60) + "。" + new string('二', 60);
		Assert.Equal(new string('一', 60) + "。", OverheardAi.Shorten(text, 90));
		string words = string.Join(" ", Enumerable.Repeat("word", 30));
		string cut = OverheardAi.Shorten(words, 90);
		Assert.EndsWith("…", cut);
		Assert.True(cut.Length <= 90);
		Assert.Equal("短", OverheardAi.Shorten("短", 90));
	}

	[Fact]
	public void SettingDescribesPlaceTimeAndSeason()
	{
		Assert.Contains("时间：08:30（早上）", OverheardAi.Setting("Town", 830, "spring"));
		Assert.Contains("（下午）", OverheardAi.Setting("Town", 1200, null));
		Assert.Contains("（傍晚）", OverheardAi.Setting("Town", 1800, null));
		Assert.Contains("01:00（深夜）", OverheardAi.Setting("Town", 2500, null));
		Assert.Contains("季节：春", OverheardAi.Setting("", 900, "spring"));
		Assert.Contains("镇上某处", OverheardAi.Setting("", 900, null));
		Assert.Equal("A、B和C", OverheardAi.JoinNames(new[] { "A", "B", "C" }));
		Assert.Contains("- Alex：爱运动", OverheardAi.Personas(new[] { ("Alex", "爱运动"), ("Haley", " ") }));
		Assert.DoesNotContain("Haley", OverheardAi.Personas(new[] { ("Alex", "爱运动"), ("Haley", " ") }));
	}

	[Fact]
	public void MemoryUsesWrittenTextOrFirstLine()
	{
		SceneDefinition d = One(Pair);
		SceneMemory m = SceneMemory.Build(d, null, n => n == "Alex" ? "亚历克斯" : "海莉")!;
		Assert.Equal("记住了", m.Text);
		Assert.Equal(new[] { "Alex", "Haley" }, m.Participants);
		Assert.Equal("[农夫看到亚历克斯、海莉之间的一幕：两个人] 记住了", m.Interaction);
		d.Keepsake = null;
		SceneMemory played = SceneMemory.Build(d, new[] { new SceneLine("Haley", "AI 写的\n一句") }, n => n == "Alex" ? "亚历克斯" : "海莉")!;
		Assert.Equal("海莉说：「AI 写的 一句」", played.Text);
		Assert.Null(SceneMemory.Build(d, Array.Empty<SceneLine>(), n => n));

		d.Keepsake = "{Alex}和{Haley}，{Unknown}在场：{不换}";
		Assert.Equal("亚历克斯和海莉，Unknown在场：{不换}", SceneMemory.Build(d, null, n => n switch { "Alex" => "亚历克斯", "Haley" => "海莉", _ => n })!.Text);
		DailyInteraction seen = m.ToInteraction("小农");
		Assert.True(SkippedReply.Is(seen.Reply));
		Assert.Equal(("Alex", "小农", m.Interaction), (seen.Npc, seen.Farmer, seen.Opener));
	}

	[Fact]
	public void BuiltInMemoriesNameTheirSpeakers()
	{
		Assert.All(Catalog().All.Where(d => d.RecordsMemory), d =>
		{
			SceneMemory m = SceneMemory.Build(d, null, n => "〈" + n + "〉")!;
			Assert.Contains(d.Speakers, n => m.Text.Contains("〈" + n + "〉"));
			Assert.DoesNotContain("{", m.Text);
		});
	}

	[Fact]
	public async Task EngineWritesOverheardLines()
	{
		FakeSender ai = new FakeSender();
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ModConfig config = new ModConfig().Normalize();
		DialogueEngine engine = new DialogueEngine(() => config, ai, new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance), new ConversationLog(() => store, () => 100), new RecentLines(), NullLog.Instance, () => "简体中文");
		ai.Reply("{\"script_rows\":[{\"speaker\":\"Alex\",\"text\":\"今天风好大。\"},{\"speaker\":\"Haley\",\"text\":\"我的头发全乱了。\"}]}");
		IReadOnlyList<SceneLine>? lines = await engine.OverheardAsync(new[] { ("Alex", "爱运动"), ("Haley", "爱拍照") }, "两人在聊海报", "Town", 1400, "summer");
		Assert.Equal(2, lines!.Count);
		ChatRequest req = ai.Requests[0];
		Assert.Equal(DialogueEngine.OverheardRequest, req.Name);
		Assert.DoesNotContain("{{", req.System);
		Assert.Contains("Alex, Haley", req.System);
		Assert.Contains("共 2～4 条", req.System);
		Assert.Contains("最多 90 字", req.System);
		Assert.Contains("不朝农夫说话", req.System);
		Assert.Contains("一字不改", req.System);
		Assert.Contains("#$b#", req.System);
		Assert.Contains("两人在聊海报", req.System);
		Assert.Contains("（下午）", req.System);
		Assert.Contains("Alex和Haley", req.User);

		ai.Reply("{\"script_rows\":[{\"text\":\"嗯……\"}]}");
		IReadOnlyList<SceneLine>? alone = await engine.OverheardAsync(new[] { ("Alex", "") }, "", "Beach", 900, "spring");
		Assert.Equal("嗯……", alone![0].Text);
		Assert.Contains("共 1～2 条", ai.Requests[1].System);
		Assert.Contains("没有特别惦记的事", ai.Requests[1].System);
		Assert.Contains("只有Alex一个人在", ai.Requests[1].System);
		Assert.Null(await engine.OverheardAsync(Array.Empty<(string, string)>(), "", "Beach", 900, "spring"));
		Assert.Contains("也不提起农夫", ai.Requests[0].System);

		ai.Reply("{\"script_rows\":[{\"speaker\":\"Emily\",\"text\":\"他其实挺好的。\"},{\"speaker\":\"Haley\",\"text\":\"哼。\"}]}");
		await engine.OverheardAsync(new[] { ("Emily", ""), ("Haley", "") }, "两人在叠衣服", "HaleyHouse", 1000, "summer", default, "听说牛哥在艾米丽屋里待了两个钟头");
		string behind = ai.Requests[^1].System;
		Assert.DoesNotContain("{{", behind);
		Assert.Contains("听说牛哥在艾米丽屋里待了两个钟头", behind);
		Assert.Contains("背着他聊", behind);
		Assert.DoesNotContain("也不提起农夫", behind);
		Assert.DoesNotContain("两人在叠衣服", behind);
	}

	private sealed class ListLog : ILog
	{
		private readonly List<string> _warnings;

		public ListLog(List<string> warnings)
		{
			_warnings = warnings;
		}

		public void Trace(string message)
		{
		}

		public void Debug(string message)
		{
		}

		public void Info(string message)
		{
		}

		public void Warn(string message) => _warnings.Add(message);

		public void Error(string message) => _warnings.Add(message);
	}
}
