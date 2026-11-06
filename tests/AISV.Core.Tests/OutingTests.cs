using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Outings;
using AISV.Core.Prompts;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class OutingTests
{
	private static string AssetDir()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "AISV.sln")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		return Path.Combine(dir!, "src", "AISV.Mod", "assets", "outings");
	}

	private static OutingCatalog Catalog() => OutingCatalog.Load(File.ReadAllText(Path.Combine(AssetDir(), "spots.json")), File.ReadAllText(Path.Combine(AssetDir(), "outings.json")), NullLog.Instance);

	private static Func<DocumentStore?> Store()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		return () => store;
	}

	private static OutingDecision Yes(string id = "", string tone = "friendship") => new OutingDecision { Agrees = true, Says = "好啊！", Feeling = "happy", Warmth = 20, OutingId = id, Tone = tone, ToRemember = "答应一起出去。" };

	// ---- 模板 ----

	[Fact]
	public void CatalogLoadsAllOutings()
	{
		OutingCatalog c = Catalog();
		Assert.Equal(63, c.Count);
		Assert.Equal(OutingCatalog.Types.OrderBy(t => t), c.All.Select(p => p.Type).Distinct().OrderBy(t => t));
		Assert.All(c.All, p =>
		{
			Assert.True(p.BlocksChildren);
			Assert.InRange(p.TimeMin, 600, 2600);
			Assert.InRange(p.TimeMax, p.TimeMin, 2600);
			Assert.InRange(p.Lines, 4, 6);
			Assert.NotEmpty(p.Title);
			Assert.NotEmpty(p.Hint);
			Assert.NotEmpty(p.Props);
		});
		OutingPlan pier = c.Find("aisv.date_light.shack_sunset")!;
		Assert.Equal(("Beach", 25, 36, 6, true, 6, 20), (pier.Location, pier.X, pier.Y, pier.MinHearts, pier.Romantic, pier.Lines, pier.Emote));
		OutingPlan hot = c.Find("AISV.SPOUSE_HOT.stove_low")!;
		Assert.Equal((2100, 2500, 12, 5, 320, 1), (hot.TimeMin, hot.TimeMax, hot.MinHearts, hot.Lines, hot.Pause, hot.FarmerFacing));
		Assert.Equal("pond", c.Find("aisv.walk.pond_rounds")!.FarmArea);
		OutingProp torch = c.Find("aisv.cozy.beach_ember")!.Props.First(p => p.Flame);
		Assert.True(torch.LightRadius > 0);
	}

	[Fact]
	public void BrokenEntriesAreSkipped()
	{
		string spots = "{\"a\":{\"location\":\"Town\",\"x\":1,\"y\":2},\"f\":{\"location\":\"Town\",\"x\":1,\"y\":2,\"farmArea\":\"pond\"}}";
		string outings = "[{\"id\":\"ok\",\"type\":\"walk\",\"spot\":\"a\",\"time\":[0,0]},{\"id\":\"bad\",\"type\":\"party\",\"spot\":\"a\"},{\"id\":\"farm\",\"type\":\"walk\",\"spot\":\"f\"},{\"id\":\"prop\",\"type\":\"walk\",\"spot\":\"a\",\"props\":[[\"(F)1\",0,0,0,1]]}]";
		OutingCatalog c = OutingCatalog.Load(spots, outings, NullLog.Instance);
		OutingPlan ok = Assert.Single(c.All);
		Assert.Equal((600, 2600, 3), (ok.TimeMin, ok.TimeMax, ok.MinHearts));
	}

	[Fact]
	public void EligibilityFollowsHeartsRomanceAndKind()
	{
		OutingCatalog c = Catalog();
		Assert.Empty(c.Eligible(new OutingAsk(2, false, false)));
		IReadOnlyList<OutingPlan> friends = c.Eligible(new OutingAsk(3, false, false));
		Assert.NotEmpty(friends);
		Assert.DoesNotContain(friends, p => p.Romantic || p.IsSpouseType || p.MinHearts > 3);
		Assert.Contains(c.Eligible(new OutingAsk(4, false, false)), p => p.Id == "aisv.cozy.stove_tea");
		Assert.Empty(c.Eligible(new OutingAsk(14, true, true)));
		Assert.Contains(c.Eligible(new OutingAsk(6, true, false)), p => p.Type == "date_light");

		c.MinHeartsFloor = () => 5;
		Assert.Equal(3, c.EffectiveHearts(friends[0]));
		Assert.Contains(c.Eligible(new OutingAsk(3, false, false)), p => p.Id == friends[0].Id);

		c.MinHeartsFloor = () => 0;
		Assert.Contains(c.Eligible(new OutingAsk(0, false, false)), p => p.Type == "walk");
		Assert.DoesNotContain(c.Eligible(new OutingAsk(0, false, false)), p => p.Id == "aisv.cozy.stove_tea");

		IReadOnlyList<OutingPlan> date = c.Eligible(new OutingAsk(14, true, false, Spouse: true));
		Assert.All(date, p => Assert.True(p.Type is "date_light" or "spouse_love"));
		Assert.Contains(c.Eligible(new OutingAsk(14, true, false, Spouse: true, Hot: true)), p => p.Type == "spouse_hot");
		Assert.DoesNotContain(c.Eligible(new OutingAsk(11, true, false, Spouse: true, Hot: true)), p => p.Type == "spouse_hot");

		c.LocationAvailable = l => l != "Forest";
		Assert.DoesNotContain(c.Eligible(new OutingAsk(14, true, false)), p => p.Location == "Forest");
		Assert.DoesNotContain("Forest", c.Locations);
	}

	// ---- 读玩家的话 ----

	[Theory]
	[InlineData("我们去海边走走吧", "Beach", "supported", null, null)]
	[InlineData("晚上7点去酒吧喝一杯？", "Saloon", "supported", 1900, 1900)]
	[InlineData("一起去下水道探险", "Sewer", "unsupported", null, null)]
	[InlineData("Let's go to the mountains in the morning", "Mountain", "supported", 600, 1200)]
	[InlineData("meet me at the quarry", "", "unresolved_from_player_text", null, null)]
	[InlineData("要不要一起去走走", "", "none", null, null)]
	[InlineData("how about 7pm?", "", "none", 1900, 1900)]
	[InlineData("19:30 en la playa", "Beach", "supported", 1930, 1930)]
	[InlineData("傍晚来我家坐坐", "FarmHouse", "supported", 1600, 1900)]
	[InlineData("下午3点去博物馆", "ArchaeologyHouse", "supported", 1500, 1500)]
	[InlineData("No pressure, wanna hang out this evening?", "", "none", 1800, 2400)]
	[InlineData("I am free, no rush", "", "none", null, null)]
	[InlineData("我在想，一会儿到处走走吧", "", "none", null, null)]
	[InlineData("晚上12点去酒吧", "Saloon", "supported", 2400, 2400)]
	[InlineData("中午12点去海边", "Beach", "supported", 1200, 1200)]
	[InlineData("7点去镇上", "Town", "supported", 700, 700)]
	public void ReadsPlaceAndTime(string text, string place, string status, int? min, int? max)
	{
		OutingIntent i = OutingIntentReader.Read(text, Catalog().Locations);
		Assert.Equal(place, i.RequestedLocation);
		Assert.Equal(status, i.LocationStatus);
		Assert.Equal(min, i.TimeMin);
		Assert.Equal(max, i.TimeMax);
	}

	[Fact]
	public void MissingPlaceInSaveIsUnsupported()
	{
		OutingIntent i = OutingIntentReader.Read("去森林走走", Catalog().Locations, l => l != "Forest");
		Assert.Equal(OutingIntent.Unsupported, i.LocationStatus);
	}

	[Fact]
	public void IntentNarrowsChoices()
	{
		OutingCatalog c = Catalog();
		OutingAsk ask = new OutingAsk(6, true, false);
		IReadOnlyList<OutingPlan> beachNight = c.Eligible(ask, OutingIntentReader.Read("今晚去海边", c.Locations));
		Assert.NotEmpty(beachNight);
		Assert.All(beachNight, p => Assert.True(p.Location == "Beach" && p.TimeMax >= 1800));
		Assert.Empty(c.Eligible(ask, OutingIntentReader.Read("meet me at the quarry", c.Locations)));
		Assert.Empty(c.Eligible(ask, OutingIntentReader.Read("去下水道", c.Locations)));
	}

	[Fact]
	public void OptionsBlockExplainsConstraints()
	{
		OutingCatalog c = Catalog();
		OutingAsk ask = new OutingAsk(3, false, false);
		string plain = c.OptionsBlock(ask, new OutingIntent());
		Assert.DoesNotContain("农夫点了", plain);
		Assert.Contains("aisv.friendship.bench_gossip ｜ 长椅上交换消息 ｜ Town ｜ 930-1930", plain);
		Assert.DoesNotContain("date_light", plain);

		string sewer = c.OptionsBlock(ask, OutingIntentReader.Read("去下水道", c.Locations));
		Assert.Contains("unsupported", sewer);
		Assert.Contains("对得上的出游：0 个", sewer);
		Assert.Contains("只能拿来改提", sewer);
		Assert.Contains("aisv.friendship.bench_gossip", sewer);

		string beach = c.OptionsBlock(ask, OutingIntentReader.Read("去海边", c.Locations));
		Assert.Contains("硬条件", beach);
		Assert.DoesNotContain("| Town |", beach.Replace("｜", "|"));

		Assert.Contains("别自己编地名", c.OptionsBlock(ask, OutingIntentReader.Read("meet me at the quarry", c.Locations)));
		Assert.Equal("", c.OptionsBlock(new OutingAsk(1, false, false), null));
	}

	// ---- 选出游 ----

	[Fact]
	public void PickPrefersAiChoiceThenToneThenAny()
	{
		OutingCatalog c = Catalog();
		OutingAsk ask = new OutingAsk(6, true, false);
		Random r = new Random(3);
		Assert.Equal("aisv.walk.dune_line", c.Pick(Yes("AISV.WALK.dune_line"), ask, null, r)!.Id);
		Assert.Equal("cozy", c.Pick(Yes("nope", "cozy"), ask, null, r)!.Type);
		Assert.NotNull(c.Pick(Yes("nope", "weird"), ask, null, r));
		Assert.Equal("spouse_love", c.Pick(Yes("nope", "weird"), new OutingAsk(14, true, false, Spouse: true), null, r)!.Type);
		Assert.Null(c.Pick(Yes(), ask, OutingIntentReader.Read("去下水道", c.Locations), r));
		Assert.Equal("Beach", c.Pick(Yes("aisv.friendship.bench_gossip"), ask, OutingIntentReader.Read("去海边", c.Locations), r)!.Location);
	}

	[Fact]
	public void SpouseOfferRespectsChanceAndTime()
	{
		OutingCatalog c = Catalog();
		OutingAsk ask = new OutingAsk(14, true, false);
		Assert.Null(c.PickSpouseOffer(ask, 900, 0, new Random(1)));
		int hits = Enumerable.Range(0, 400).Count(i => c.PickSpouseOffer(ask, 900, 100, new Random(i)) != null);
		Assert.InRange(hits, 80, 160);
		for (int i = 0; i < 50; i++)
		{
			OutingPlan? p = c.PickSpouseOffer(ask, 2300, 30, new Random(i));
			if (p != null)
			{
				Assert.True(p.TimeMax >= 2300);
				Assert.True(p.Type is "date_light" or "spouse_love");
			}
		}
	}

	// ---- 脚本 ----

	[Fact]
	public void ScriptUsesFallbacksAndEscapes()
	{
		OutingCatalog c = Catalog();
		OutingPlan four = c.Find("aisv.friendship.bench_gossip")!;
		string s = OutingCatalog.Script(four, "aiout-x", "Abigail", "好呀/走吧", new[] { "", "第二句 \"引号\"" });
		Assert.StartsWith("continue/44 78/farmer 43 78 1 Abigail 45 78 3/skippable/AisvOutingDone aiout-x/speak Abigail \"好呀／走吧\"/pause 250/speak Abigail \"第二句 '引号'\"/emote Abigail 32/speak Abigail \"", s);
		Assert.EndsWith("/end", s);
		Assert.Equal(4, OutingAi.SpokenLines(s, "Abigail").Count);
		Assert.Equal(OutingCatalog.DefaultLine(3), OutingAi.SpokenLines(s, "abigail")[3]);
		OutingPlan six = c.Find("aisv.date_light.shack_sunset")!;
		Assert.Equal(6, OutingAi.SpokenLines(OutingCatalog.Script(six, "i", "Sam", "嗯", Array.Empty<string>()), "Sam").Count);
		OutingPlan home = c.Find("aisv.spouse_love.door_evening")!;
		Assert.Contains("farmer 3 9 1 Sam 5 9 3", OutingCatalog.Script(home, "i", "Sam", "嗯", Array.Empty<string>()));
	}

	// ---- 解析 ----

	[Fact]
	public void ParseNormalizesDecision()
	{
		Assert.Null(OutingAi.Parse("{\"says\":\"好\"}", 100));
		Assert.Null(OutingAi.Parse("{\"agrees\":\"yes\"}", 100));
		OutingDecision d = OutingAi.Parse("```json\n{\"agrees\":true,\"says\":\"\",\"feeling\":\"开心\",\"warmth\":99,\"tone\":\"PICNIC\",\"back_off_days\":-1,\"instead\":{\"outing_id\":\"x\",\"place\":\"Beach\"}}\n```", 100)!;
		Assert.Equal((true, "happy", 40, "friendship", 3), (d.Agrees, d.Feeling, d.Warmth, d.Tone, d.BackOffDays));
		Assert.NotEmpty(d.Says);
		Assert.Null(d.Instead);

		OutingDecision no = OutingAi.Parse("{\"agrees\":false,\"says\":\"不了\",\"outing_id\":\"aisv.walk.x\",\"tone\":\"walk\",\"beat_1\":\"a\",\"warmth\":-50,\"line_drawn\":\"别再约了\",\"back_off_days\":-3,\"instead\":{\"outing_id\":\"aisv.walk.dune_line\",\"place\":\"Pelican Town\",\"tone\":\"??\"}}", 100)!;
		Assert.Equal(("", "", "", -10, 7), (no.OutingId, no.Tone, no.Beat1, no.Warmth, no.BackOffDays));
		Assert.Equal(("Town", "friendship"), (no.Instead!.Place, no.Instead.Tone));

		Assert.Equal(1, OutingAi.Parse("{\"agrees\":false,\"back_off_days\":-1}", 100)!.BackOffDays);
		Assert.Equal(0, OutingAi.Parse("{\"agrees\":false,\"back_off_days\":0}", 100)!.BackOffDays);
		Assert.Equal(14, OutingAi.Parse("{\"agrees\":true,\"back_off_days\":12}", 100)!.BackOffDays);
		Assert.Null(OutingAi.Parse("{\"agrees\":false,\"instead\":{\"outing_id\":\"\",\"place\":\"Beach\"}}", 100)!.Instead);
	}

	[Fact]
	public void LinesNeedTheFirstFour()
	{
		Assert.Equal(new[] { "一", "二", "三", "四", "" }, OutingAi.ParseLines("{\"beat_1\":\"一\",\"beat_2\":\"二\",\"beat_3\":\"三\",\"beat_4\":\"四\"}", 5));
		Assert.Null(OutingAi.ParseLines("{\"beat_1\":\"一\",\"beat_2\":\"二\",\"beat_4\":\"四\"}", 4));
		Assert.Equal(4, OutingAi.ParseLines("{\"beat_1\":\"1\",\"beat_2\":\"2\",\"beat_3\":\"3\",\"beat_4\":\"4\",\"beat_5\":\"5\"}", 2)!.Count);
		Assert.Null(OutingAi.ParseLines("not json", 4));
	}

	[Fact]
	public void AcceptedCounterBecomesAnOuting()
	{
		OutingDecision original = new OutingDecision { Agrees = false, Says = "那里不行，去海边吧？", Warmth = 4, Instead = new OutingCounter { OutingId = "aisv.walk.dune_line", Tone = "walk", Place = "Beach", IfYes = "太好了。" } };
		OutingDecision d = OutingAi.AcceptCounter(original, "改去海边。");
		Assert.Equal((true, "太好了。", 15, 0, true, "aisv.walk.dune_line"), (d.Agrees, d.Says, d.Warmth, d.BackOffDays, d.WorthTelling, d.OutingId));
	}

	// ---- 记录 ----

	[Fact]
	public void RecordAcceptedAndDeclined()
	{
		OutingCatalog c = Catalog();
		OutingBook book = new OutingBook(Store());
		OutingRecord yes = book.Record("Abigail", "去海边吧", Yes("aisv.walk.dune_line"), new OutingAsk(5, false, false), new OutingIntent(), c, 3, 40, new Random(1));
		Assert.True(yes.Waiting);
		Assert.Equal(("aisv.walk.dune_line", "Beach", 900, 2130, 43), (yes.OutingId, yes.Place, yes.FromTime, yes.ToTime, yes.RestUntil));
		Assert.Matches("^aiout-Abigail-40-[0-9a-f]{8}$", yes.Id);
		Assert.Contains("AisvOutingDone " + yes.Id, yes.Script);
		Assert.True(book.HasWaiting("abigail"));
		Assert.True(book.OnCooldown("Abigail", 42));
		Assert.False(book.OnCooldown("Abigail", 43));

		OutingDecision no = new OutingDecision { Agrees = false, Says = "不了", BackOffDays = 14, LineDrawn = "  " };
		OutingRecord declined = book.Record("Sam", "走", no, new OutingAsk(5, false, false), new OutingIntent(), c, 1, 40, new Random(1));
		Assert.Equal((OutingState.Declined, 54, (string?)null), (declined.State, declined.RestUntil, declined.LineDrawn));
		OutingRecord zero = book.Record("Leah", "走", new OutingDecision { Agrees = false, Says = "不" }, new OutingAsk(5, false, false), new OutingIntent(), c, 0, 40, new Random(1));
		Assert.Equal(40, zero.RestUntil);
	}

	[Fact]
	public void NoMatchingOutingTurnsIntoDecline()
	{
		OutingCatalog c = Catalog();
		OutingBook book = new OutingBook(Store());
		OutingDecision d = Yes();
		d.Beat1 = "x";
		OutingRecord r = book.Record("Abigail", "去下水道", d, new OutingAsk(5, false, false), OutingIntentReader.Read("去下水道", c.Locations), c, 3, 10, new Random(1));
		Assert.Equal((false, OutingState.Declined, "", ""), (r.Agreed, r.State, r.Script, r.OutingId));
		Assert.False(d.Agrees);
		Assert.Equal(5, d.Warmth);
		Assert.Equal("", d.Beat1);
		Assert.Contains("今天不太行", d.Says);
		Assert.Equal(13, r.RestUntil);
	}

	[Fact]
	public void LinesCompleteExpireAndPrompt()
	{
		OutingCatalog c = Catalog();
		OutingBook book = new OutingBook(Store());
		OutingRecord r = book.Record("Abigail", "走", Yes("aisv.friendship.bench_gossip"), new OutingAsk(5, false, false), new OutingIntent(), c, 3, 10, new Random(1));
		OutingRecord? updated = book.ApplyLines(r.Id, new[] { "这里好热闹", "二", "三", "四" }, c);
		Assert.Contains("\"这里好热闹\"", updated!.Script);
		Assert.Contains("已经答应、还没去的一次：aisv.friendship.bench_gossip，在 Town，930-1930", book.PromptBlock("Abigail", 11));
		Assert.Contains("第 10 天：答应了（等着去），朋友间；记得：答应一起出去。；冷却到第 13 天", book.PromptBlock("Abigail", 11));
		Assert.Contains("还没去的一次", book.PromptBlock("Abigail", 30));
		Assert.Empty(book.Expire(17));
		Assert.Single(book.Expire(18));
		Assert.Equal("", book.PromptBlock("Abigail", 30));
		Assert.False(book.Find(r.Id)!.Waiting);
		Assert.Null(book.ApplyLines(r.Id, new[] { "a", "b", "c", "d" }, c));

		OutingRecord again = book.Record("Abigail", "走", Yes("aisv.friendship.bench_gossip"), new OutingAsk(5, false, false), new OutingIntent(), c, 3, 20, new Random(1));
		OutingRecord done = book.Complete(again.Id, 21)!;
		Assert.Equal((true, OutingState.Completed, 21), (done.Played, done.State, done.PlayedOn));
		Assert.Single(book.CompletedOn(21));
		Assert.Equal(OutingState.Cancelled, book.Cancel(r.Id)!.State);
	}

	// ---- 演完的记忆 ----

	[Fact]
	public void MemoryQuotesTheLongestLine()
	{
		OutingRecord r = new OutingRecord { Npc = "Abigail", Place = "Beach", Tone = "walk", Asked = "走" };
		OutingMemory m = OutingAi.Remember(r, "码头上走一圈", new[] { "嗨", "海风今天特别舒服，你闻到了吗", "下次再来" }, "天气晴朗", "阿比盖尔", "fallback");
		Assert.Equal("阿比盖尔和农夫一起去了海滩的「码头上走一圈」（散步，天气晴朗）。阿比盖尔说：「海风今天特别舒服，你闻到了吗」", m.Text);
		Assert.Equal(("嗨", "下次再来"), (m.FirstLine, m.LastLine));
		OutingMemory none = OutingAi.Remember(r, null, Array.Empty<string>(), null, "阿比盖尔", "她答应了邀请。");
		Assert.Equal(("她答应了邀请。", "", "海滩"), (none.Text, none.LastLine, none.Place));
		string longLine = new string('长', 300);
		Assert.EndsWith("「" + new string('长', OutingAi.MaxQuotedChars) + "…」", OutingAi.Remember(r, null, new[] { longLine }, null, "A", "").Text);
		Assert.Equal(OutingAi.MaxMemoryChars + 1, OutingAi.Remember(r, null, Array.Empty<string>(), null, "A", longLine).Text.Length);
	}

	// ---- 站位 ----

	[Fact]
	public void FarmLayoutHelpers()
	{
		Assert.Equal((65, 18), OutingLayout.Anchor("doorstep", 80, 65, (64, 16)));
		Assert.Equal((40, 8), OutingLayout.Anchor("north_entrance", 80, 65, (0, 0)));
		Assert.Equal((71, 17), OutingLayout.Anchor("east_entrance", 80, 65, (0, 0)));
		Assert.Throws<ArgumentException>(() => OutingLayout.Anchor("roof", 80, 65, (0, 0)));
		Assert.Equal((40, 15), OutingLayout.InsideExit("north_entrance", 40, 8));
		Assert.Equal((72, 17), OutingLayout.InsideExit("east_entrance", 79, 17));
		Assert.Equal((40, 57), OutingLayout.InsideExit("south_entrance", 40, 64));
		Assert.Equal("Forest", OutingLayout.ExitFor("south_entrance"));

		string script = "continue/40 8/farmer 39 8 1 Abigail 41 8 3/skippable/AisvOutingDone x/end";
		Assert.Equal("continue/20 30/farmer 19 30 1 Abigail 21 30 3/ignoreEventTileOffset/skippable/AisvOutingDone x/end", OutingLayout.PlaceOnFarm(script, 20, 30));
		Assert.Throws<InvalidOperationException>(() => OutingLayout.PlaceOnFarm("continue/1 1/farmer 1 1 1/end", 2, 2));
		Assert.Equal("continue/40 8/farmer 39 8 1 Abigail 41 8 3/skippable/AisvOutingScenery x/AisvOutingDone x/end", OutingLayout.WithScenery(script, "x"));
		Assert.Equal(OutingLayout.WithScenery(script, "x"), OutingLayout.WithScenery(OutingLayout.WithScenery(script, "x"), "x"));
		Assert.Equal("continue/9 9/farmer 8 9 2 Sam 10 9 3/end", OutingLayout.AnchorFarmHouse("continue/7 7/farmer 6 7 2 Sam 8 7 3/end", 5, 13));
	}

	[Fact]
	public void CenterAvoidsBlockedTilesAndPrefersRoomForProps()
	{
		HashSet<(int, int)> blocked = new HashSet<(int, int)> { (10, 10), (11, 10) };
		Assert.True(OutingLayout.TryFindCenter((10, 10), 30, 30, 5, (x, y) => !blocked.Contains((x, y)), null, null, out (int X, int Y) c));
		Assert.NotEqual((10, 10), c);
		Assert.DoesNotContain(new[] { (c.X - 1, c.Y), c, (c.X + 1, c.Y) }, t => blocked.Contains(t));
		Assert.False(OutingLayout.TryFindCenter((10, 10), 30, 30, 3, (_, _) => false, null, null, out _));
		Assert.True(OutingLayout.TryFindCenter((10, 10), 30, 30, 5, (_, _) => true, null, (x, _) => x >= 13, out c));
		Assert.Equal(13, c.X);

		OutingProp[] props = { new OutingProp("(F)rug", -1, 0, 3, 2, Floor: true), new OutingProp("(F)table", -1, -2, 2, 2), new OutingProp("(F)lamp", 0, 0) };
		IReadOnlyList<OutingProp> fit = OutingLayout.FittingProps(props, 5, 5, new HashSet<(int, int)> { (5, 5) }, (_, _) => true);
		Assert.Equal(new[] { "(F)rug", "(F)table" }, fit.Select(p => p.ItemId));
	}

	// ---- 和 AI 往来 ----

	[Fact]
	public async Task EngineFillsBothPromptsAndCommits()
	{
		FakeSender ai = new FakeSender();
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ConversationLog log = new ConversationLog(() => store, () => 100);
		ModConfig config = new ModConfig().Normalize();
		DialogueEngine engine = new DialogueEngine(() => config, ai, new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance), log, new RecentLines(), NullLog.Instance, () => "简体中文");
		DialogueMaterial m = new DialogueMaterial { NpcName = "Abigail", DisplayName = "阿比盖尔", PlayerName = "小麦", Hearts = 5, Personality = "爱冒险" };
		OutingCatalog c = Catalog();
		OutingContext ctx = new OutingContext(OutingKind.Invite, c.OptionsBlock(new OutingAsk(5, false, false), null), "【你和农夫之前的邀约】", "dating", "canonical_single", "", false);
		ai.Reply("{\"agrees\":true,\"says\":\"好呀，去码头吧。\",\"feeling\":\"happy\",\"warmth\":12,\"tone\":\"walk\",\"outing_id\":\"aisv.walk.dune_line\",\"to_remember\":\"答应去码头\",\"back_off_days\":3,\"worth_telling\":true}");
		GameDate today = new GameDate(5, "spring", 1);
		OutingDecision? d = await engine.OutingAsync(m, ctx, "一起去码头走走？", "今天天气不错。", today);
		Assert.NotNull(d);
		ChatRequest req = ai.Requests[0];
		Assert.Equal(DialogueEngine.OutingRequest, req.Name);
		Assert.DoesNotContain("{{", req.System);
		Assert.Contains("aisv.walk.dune_line", req.System);
		Assert.Contains("一起去码头走走？", req.System);
		Assert.Contains("friendship、cozy、walk", req.System);

		OutingRecord r = new OutingBook(() => store).Record("Abigail", "一起去码头走走？", d!, new OutingAsk(5, false, false), new OutingIntent(), c, 3, today.TotalDays, new Random(1));
		ai.Reply("{\"beat_1\":\"风好大\",\"beat_2\":\"二\",\"beat_3\":\"三\",\"beat_4\":\"四\"}");
		IReadOnlyList<string>? lines = await engine.OutingLinesAsync(m, c.Find(r.OutingId)!, "提灯、贝壳", r, today);
		Assert.Equal("风好大", lines![0]);
		Assert.Equal(DialogueEngine.OutingLinesRequest, ai.Requests[1].Name);
		Assert.DoesNotContain("{{", ai.Requests[1].System);
		Assert.Contains("提灯、贝壳", ai.Requests[1].System);
		Assert.Contains("\"beat_4\"", ai.Requests[1].System);

		engine.CommitOuting(m, OutingKind.Invite, "一起去码头走走？", "今天天气不错。", d!, today);
		ConversationEntry e = log.Recent("Abigail", 1).Single();
		Assert.Equal(("[约出去] 一起去码头走走？", "今天天气不错。", "好呀，去码头吧。"), (e.FarmerReply, e.Opener, e.Comeback));

		OutingPlan plan = c.Find("aisv.spouse_love.door_evening")!;
		ai.Reply("{\"agrees\":true,\"says\":\"今晚在家陪陪我？\",\"outing_id\":\"aisv.spouse_love.door_evening\",\"tone\":\"spouse_love\"}");
		await engine.OutingAsync(m, ctx with { Kind = OutingKind.SpouseOffer, Options = OutingCatalog.SingleOption(plan) }, "", null, today);
		Assert.Contains("你先开口时", ai.Requests[2].System);
		Assert.Contains("只有这一个：aisv.spouse_love.door_evening", ai.Requests[2].System);
		Assert.Contains("先开口，邀配偶", ai.Requests[2].User);
	}

	[Fact]
	public async Task SpouseOfferBecomesTheOpening()
	{
		FakeSender ai = new FakeSender();
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		ConversationLog log = new ConversationLog(() => store, () => 100);
		ModConfig config = new ModConfig().Normalize();
		DialogueEngine engine = new DialogueEngine(() => config, ai, new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance), log, new RecentLines(), NullLog.Instance, () => "简体中文");
		OutingPlan plan = Catalog().Find("aisv.spouse_love.door_evening")!;
		engine.Outings = new FixedDesk(new OutingOffer(plan, new OutingContext(OutingKind.SpouseOffer, OutingCatalog.SingleOption(plan), "", "married", "canonical_single", "", false)));
		log.Add("Abigail", new ConversationEntry { Day = 1, Season = "spring", Year = 1, Opener = "早", FarmerReply = "早" });
		DialogueMaterial m = new DialogueMaterial { NpcName = "Abigail", PlayerName = "小麦", Hearts = 12, IsSpouse = true, IsAdultRomanticPartner = true, Season = "spring", Day = 5, Year = 1 };
		ai.Reply("{\"agrees\":true,\"says\":\"今晚早点回家，陪我坐坐好吗？\",\"outing_id\":\"aisv.spouse_love.door_evening\",\"tone\":\"cozy\"}");
		OpeningResult? o = await engine.OpeningAsync(m, new GameDate(5, "spring", 1));
		Assert.Equal("今晚早点回家，陪我坐坐好吗？", o!.Dialogue);
		OutingDecision offer = engine.State("Abigail")!.OpenOffer!;
		Assert.Equal("spouse_love", offer.Tone);
		Assert.Single(ai.Requests);

		engine.Outings = new FixedDesk(new OutingOffer(plan, new OutingContext(OutingKind.SpouseOffer, OutingCatalog.SingleOption(plan), "", "married", "canonical_single", "", false)));
		ai.Reply("{\"agrees\":true,\"says\":\"去别处吧\",\"outing_id\":\"aisv.walk.dune_line\"}").Reply("今天也辛苦啦。");
		OpeningResult? normal = await engine.OpeningAsync(new DialogueMaterial { NpcName = "Sam", PlayerName = "小麦", Hearts = 12, IsSpouse = true, Season = "spring", Day = 5, Year = 1 }, new GameDate(5, "spring", 1));
		Assert.Equal("今天也辛苦啦。", normal!.Dialogue);
		Assert.Null(engine.State("Sam")!.OpenOffer);
	}

	private sealed class FixedDesk : IOutingDesk
	{
		private readonly OutingOffer _offer;

		public FixedDesk(OutingOffer offer) => _offer = offer;

		public OutingOffer? SpouseOffer(DialogueMaterial m) => _offer;
	}
}
