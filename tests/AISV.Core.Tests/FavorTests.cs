using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Favors;
using AISV.Core.Scenes;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class FavorTests
{
	private static readonly GameDate Spring3 = new GameDate(3, "spring", 1);

	private static string SceneDir()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "AISV.sln")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		return Path.Combine(dir!, "src", "AISV.Mod", "assets", "item_request_scenes");
	}

	private static Func<DocumentStore?> Store()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		return () => store;
	}

	// ---- 物品匹配 ----

	[Theory]
	[InlineData("Parsnip", "Parsnip", null, 3)]
	[InlineData("parsnips", "Parsnip", null, 3)]
	[InlineData("fresh Parsnip please", "Parsnip", null, 2)]
	[InlineData("Wine", "Cherry Wine Bottle", null, 1)]
	[InlineData("防风草", "防风草", null, 3)]
	[InlineData("一些防风草", "防风草", null, 2)]
	[InlineData("Egg", "Eggplant", null, 0)]
	[InlineData("", "Parsnip", null, 0)]
	[InlineData("Café", null, "Cafe", 3)]
	public void MatchScores(string request, string? display, string? internalName, int expected)
	{
		Assert.Equal(expected, ItemMatcher.Score(request, display, internalName));
	}

	[Fact]
	public void ExactIdNeedsOneUniqueItem()
	{
		var items = new List<(string, string?, string?)> { ("(O)24", "Parsnip", "Parsnip"), ("(O)20", "Leek", "Leek"), ("(O)900", "Leek", "Leek") };
		Assert.Equal("(O)24", ItemMatcher.ExactId("parsnips", items));
		Assert.Null(ItemMatcher.ExactId("Leek", items));
		Assert.Null(ItemMatcher.ExactId("Carrot", items));
	}

	// ---- 托付 ----

	[Fact]
	public void OnlyItemFavorsAreRegisteredOnePerVillager()
	{
		FavorBook book = new FavorBook(Store());
		Assert.NotNull(book.Register("Abigail", "companionship", "陪我走走", "", "", Spring3));
		Assert.NotNull(book.Register("Abigail", "bring", "帮我带", "", "", Spring3));
		Assert.Null(book.Register("Abigail", "traer item", "帮我带一块紫水晶", "Amethyst", "(O)66", Spring3));
		Assert.Equal("bring_item", book.All.Single().Kind);
		Assert.NotNull(book.Register("Abigail", "bring_item", "再带个石英", "Quartz", "", Spring3));
		Assert.Null(book.Register("Sam", "bring_item", "帮我带啤酒", "Beer", "", Spring3));
	}

	[Fact]
	public void GiftCompletesByIdOrByName()
	{
		FavorBook book = new FavorBook(Store());
		book.Register("Abigail", "bring_item", "紫水晶", "Amethyst", "(O)66", Spring3);
		book.Register("Sam", "bring_item", "比萨", "Pizza", "", Spring3);
		Assert.Null(book.CompleteByGift("Abigail", "(O)80", "Quartz", "Quartz", Spring3.TotalDays));
		Assert.NotNull(book.CompleteByGift("Abigail", "(O)66", "紫水晶", "Amethyst", Spring3.TotalDays));
		Assert.NotNull(book.CompleteByGift("Sam", "(O)206", "Pizza", "Pizza", Spring3.TotalDays));
		Assert.All(book.All, f => Assert.Equal("fulfilled", f.Phase));
	}

	[Theory]
	[InlineData(0, 150)]
	[InlineData(80, 70)]
	[InlineData(150, 0)]
	[InlineData(200, 0)]
	public void GiftBonusFillsUpTo150(int gain, int bonus) => Assert.Equal(bonus, FavorBook.GiftBonus(gain));

	[Fact]
	public void FavorsExpireAfterFiveDaysAndAreForgottenLater()
	{
		FavorBook book = new FavorBook(Store());
		book.Register("Abigail", "bring_item", "紫水晶", "Amethyst", "", Spring3);
		int day = Spring3.TotalDays;
		Assert.Empty(book.Expire(day + 5));
		Assert.Single(book.Expire(day + 6));
		Assert.Contains("一直没办", book.PromptBlock("Abigail", day + 7));
		Assert.Contains("很早以前", book.PromptBlock("Abigail", day + 15));
		Assert.Equal("", book.PromptBlock("Abigail", day + 30));
		book.Expire(day + 5 + 31);
		Assert.Empty(book.All);
	}

	[Fact]
	public void PromptShowsPendingAndRecentThanks()
	{
		FavorBook book = new FavorBook(Store());
		book.Register("Abigail", "bring_item", "帮我带紫水晶", "Amethyst", "", Spring3);
		string pending = book.PromptBlock("Abigail", Spring3.TotalDays);
		Assert.Contains("【你托农夫办的事】", pending);
		Assert.Contains("还没办", pending);
		book.CompleteByGift("Abigail", "(O)66", "Amethyst", "Amethyst", Spring3.TotalDays + 1);
		Assert.Contains("昨天，把Amethyst带来了", book.PromptBlock("Abigail", Spring3.TotalDays + 2));
		Assert.Equal("", book.PromptBlock("Abigail", Spring3.TotalDays + 9));
	}

	// ---- 物品请求 ----

	[Fact]
	public void BundledScenesLoadAndCoverEveryHint()
	{
		ItemRequestScenes scenes = new ItemRequestScenes(SceneDir());
		Assert.Equal(10, scenes.All.Count);
		Assert.All(ItemRequestScenes.SceneTypes, t => Assert.Equal(2, scenes.All.Count(s => s.SceneType == t)));
		HashSet<string> hints = scenes.All.SelectMany(s => s.ItemHints).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (string hint in ItemHints.All)
		{
			Assert.Contains(hint, hints);
		}
		Assert.All(scenes.All, s =>
		{
			Assert.True(EventPlacement.TryRead(s.ScriptTemplate, out _, out List<EventPlacement.Actor> npcs));
			Assert.Single(npcs);
			Assert.Contains(ItemRequestBook.SceneComplete + " {{FAVOR_ID}}", s.ScriptTemplate);
			Assert.EndsWith("/end", s.ScriptTemplate);
		});
	}

	[Fact]
	public void SceneGatesFollowHeartsPartnerAndChild()
	{
		ItemRequestScenes scenes = new ItemRequestScenes(SceneDir());
		Assert.DoesNotContain(scenes.Eligible(0, false, false), s => s.SceneType is "date" or "cozy" or "friendship");
		Assert.Contains(scenes.Eligible(1, false, false), s => s.SceneType == "friendship");
		Assert.DoesNotContain(scenes.Eligible(1, false, false), s => s.SceneType == "cozy");
		Assert.Contains(scenes.Eligible(8, true, false), s => s.SceneType == "date");
		Assert.DoesNotContain(scenes.Eligible(10, true, true), s => s.BlockedForChildCodedNpcs);
	}

	[Fact]
	public void RequestLifecycle()
	{
		ItemRequestBook book = new ItemRequestBook(Store(), () => 7);
		ItemRequestScenes scenes = new ItemRequestScenes(SceneDir(), new Random(1));
		int day = Spring3.TotalDays;
		ItemRequestPlan plan = new ItemRequestPlan { StageRef = "funny/general_store_mixup", ItemName = "Spring Onion", PayKind = "monetary", PayGold = 900 };
		ItemRequestCandidate? c = book.Candidate(scenes, "Sam", 3, false, false, plan, "Farmer_123", day, (_, hint) => hint);
		Assert.NotNull(c);
		Assert.Equal("(O)399", c!.WantedItemId);
		Assert.Equal(500, c.PayGold);
		Assert.StartsWith("Aisv.ItemRequest.Farmer_123.Sam." + day + ".", c.JournalId);
		ItemRequestFavor? f = book.Activate(c, "能帮我找根小葱吗？", "就是这个/对", "", day);
		Assert.NotNull(f);
		Assert.False(book.CanAsk("Sam", day));
		Assert.Contains("就是这个／对", f!.StageScript);
		Assert.DoesNotContain("{{", f.StageScript);
		Assert.Equal($"ItemDelivery/Sam的托付/Sam想要一份Spring Onion。/把Spring Onion交给Sam/Sam (O)399/-1/500/500 金/true/真的帮我带来Spring Onion了！谢谢你。", ItemRequestBook.QuestData(f));
		ItemRequestFavor? said = new ItemRequestBook(Store(), () => 7).Activate(c, "能帮我找根小葱吗？", "", "", day, "哼，算你/记性好。");
		Assert.EndsWith("/true/哼，算你 记性好。", ItemRequestBook.QuestData(said!));
		Assert.True(ItemRequestBook.Matches(f, "(O)399", "399"));
		Assert.False(ItemRequestBook.Matches(f, "(O)24", "24"));
		Assert.Contains("【你托农夫找的东西】", book.PromptBlock("Sam", day));
		Assert.NotNull(book.Complete(x => x.Asker == "Sam", day + 1));
		Assert.True(f.SceneWaiting);
		Assert.Contains("还没一起过", book.PromptBlock("Sam", day + 1));
		Assert.True(book.InCooldown("Sam", day + 7));
		Assert.False(book.InCooldown("Sam", day + 8));
		book.MarkScenePlayed(f.Id);
		Assert.False(f.SceneWaiting);
		Assert.Equal($"{ItemRequestBook.EventId(f.Id)}/{ItemRequestBook.SceneArmed} {f.Id}/{ItemRequestBook.SceneTime} 900 1700", ItemRequestBook.EventKey(f));
		Assert.InRange(int.Parse(ItemRequestBook.EventId(f.Id)), 9708000, 9717999);
	}

	[Fact]
	public void PlanAndLinesParse()
	{
		ItemRequestPlan? plan = ItemRequestAi.ParsePlan("```json\n{\"scene_kind\":\"cozy\",\"scene_id\":\"cozy/saloon_fireside\",\"item\":\"Pale Ale\",\"gold\":10}\n```");
		Assert.Equal("cozy/saloon_fireside", plan!.StageRef);
		Assert.Equal(50, plan.PayGold);
		Assert.Equal(("你来啦。", "坐吧。"), ItemRequestAi.ParseLines("{\"beat_1\":\"“你来啦。”\",\"beat_2\":\"坐吧。\"}"));
		Assert.Null(ItemRequestAi.ParseLines("{\"beat_1\":\"只有一句\"}"));
		Assert.Equal("哼，算你记性好。", ItemRequestAi.ParseHandover("{\"thanks\":\"哼，算你记性好。\",\"beat_1\":\"a\",\"beat_2\":\"b\"}"));
		Assert.Equal("", ItemRequestAi.ParseHandover("{\"beat_1\":\"a\",\"beat_2\":\"b\"}"));
	}

	// ---- 表白求婚 ----

	private static ProposalSituation Ready(int hearts = 8, string status = "none") => new ProposalSituation { Hearts = hearts, Status = status, Datable = true, HouseUpgradeLevel = 1 };

	[Fact]
	public void DatingAndMarriageGates()
	{
		ModConfig c = new ModConfig();
		Assert.Equal(new ProposalOptions(true, false, ""), ProposalRules.Evaluate(Ready(), c));
		Assert.False(ProposalRules.Evaluate(Ready(7), c).Any);
		Assert.Equal(new ProposalOptions(false, true, ""), ProposalRules.Evaluate(Ready(10, "dating"), c));
		Assert.False(ProposalRules.Evaluate(Ready(10, "dating") with { HouseUpgradeLevel = 0 }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready(10, "dating") with { FreeToMarry = false }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { OtherCommitment = true }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { InCooldown = true }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { IsChild = true }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { Datable = false }, c).Any);
		Assert.True(ProposalRules.Evaluate(Ready() with { Datable = false, HasPersona = true }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { Datable = false, HasPersona = true, Narrative = "canonical_married" }, c).Any);
		Assert.True(ProposalRules.Evaluate(Ready() with { Datable = false, Narrative = "story_single" }, c).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { Narrative = "story_single" }, new ModConfig { CourtStorySingles = false }).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { IsFarmhand = true }, c).Any);
		Assert.True(ProposalRules.Evaluate(Ready() with { IsFarmhand = true }, new ModConfig { HostOnlyCourting = false }).Any);
		Assert.False(ProposalRules.Evaluate(Ready(), new ModConfig { CourtWithoutTokens = false }).Any);
		Assert.False(ProposalRules.Evaluate(Ready() with { EventActive = true }, c).Any);
	}

	[Fact]
	public void DecisionParsing()
	{
		Assert.Null(ProposalParser.Parse("{\"agrees\":\"yes\",\"says\":\"好\"}", 300));
		ProposalDecision yes = ProposalParser.Parse("{\"agrees\":true,\"says\":\"\",\"warmth\":180,\"tone\":\"SHY\",\"back_off_days\":9}", 300)!;
		Assert.Equal(100, yes.Points);
		Assert.Equal("shy", yes.Tone);
		Assert.Equal(0, yes.WaitDays);
		Assert.False(string.IsNullOrWhiteSpace(yes.Line));
		Assert.Equal(7, ProposalParser.Parse("{\"agrees\":false,\"says\":\"不了\",\"back_off_days\":9}", 300)!.WaitDays);
		Assert.Equal(28, ProposalParser.Parse("{\"agrees\":false,\"says\":\"不了\",\"line_drawn\":\"我们只是朋友\"}", 300)!.WaitDays);
		Assert.Equal(7, ProposalParser.Parse("{\"agrees\":false,\"says\":\"不了\"}", 300)!.WaitDays);
	}

	[Fact]
	public void ProposalRecordsKeepTwentyAndCooldown()
	{
		RelationshipStates states = new RelationshipStates(Store());
		for (int i = 0; i < 25; i++)
		{
			states.RecordProposal("Abigail", ProposalKind.Dating, new ProposalDecision { SaidYes = false, Line = "不", WaitDays = 7 }, "我喜欢你", 100 + i, "Friendly", "");
		}
		NpcRomanceState s = states.State("Abigail")!;
		Assert.Equal(20, s.PastAsks.Count);
		Assert.Equal(124, s.PastAsks[0].OnDay);
		Assert.True(states.InProposalCooldown("Abigail", 130));
		Assert.False(states.InProposalCooldown("Abigail", 131));
		states.RecordProposal("Abigail", ProposalKind.Dating, new ProposalDecision { SaidYes = true, Line = "好" }, "我喜欢你", 140, "Dating", "");
		Assert.Equal("dating", states.State("Abigail")!.StandingWithFarmer);
		Assert.False(states.InProposalCooldown("Abigail", 140));
	}

	[Theory]
	[InlineData(ProposalKind.Dating, true, null, "dating_started_by_dialogue")]
	[InlineData(ProposalKind.Marriage, true, null, "marriage_accepted_by_dialogue")]
	[InlineData(ProposalKind.Dating, false, "只是朋友", "romantic_boundary_set")]
	[InlineData(ProposalKind.Marriage, false, null, "proposal_rejected")]
	public void Milestones(ProposalKind kind, bool accepted, string? boundary, string expected)
	{
		Assert.Equal(expected, ProposalRules.Milestone(kind, new ProposalDecision { SaidYes = accepted, Limit = boundary }));
		Assert.Equal(kind == ProposalKind.Marriage ? 20 : 32, ProposalRules.AcceptEmote(kind));
	}

	// ---- 站位 ----

	[Fact]
	public void PlacementMovesBlockedActorsAndViewport()
	{
		string script = "continue/10 10/farmer 9 10 1 Abigail 11 10 3/skippable/end";
		HashSet<(int, int)> blocked = new HashSet<(int, int)> { (9, 10), (11, 10) };
		Assert.True(EventPlacement.TrySafe(script, (x, y) => !blocked.Contains((x, y)), out string safe));
		Assert.True(EventPlacement.TryRead(safe, out EventPlacement.Actor farmer, out List<EventPlacement.Actor> npcs));
		Assert.DoesNotContain((farmer.X, farmer.Y), blocked);
		Assert.DoesNotContain((npcs[0].X, npcs[0].Y), blocked);
		Assert.NotEqual((farmer.X, farmer.Y), (npcs[0].X, npcs[0].Y));
		Assert.Equal($"{10 + farmer.X - 9} {10 + farmer.Y - 10}", safe.Split('/')[1]);
		Assert.Equal(script, EventPlacement.TrySafe(script, (_, _) => true, out string same) ? same : "");
		Assert.False(EventPlacement.TrySafe(script, (_, _) => false, out _));
	}
}
