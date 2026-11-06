using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Adult;
using AISV.Core.Gifts;
using AISV.Core.World.Commissions;
using Xunit;

namespace AISV.Core.Tests;

public class CommissionGiftTests
{
	[Theory]
	[InlineData(35, 9, 500)]
	[InlineData(200, 3, 950)]
	[InlineData(2, 12, 150)]
	public void DeliverCountAndReward(int price, int count, int gold)
	{
		Assert.Equal(count, CommissionRules.DeliverCount(price));
		Assert.Equal(gold, CommissionRules.DeliverReward(price));
	}

	[Fact]
	public void RewardRoundsTo25AndClamps()
	{
		Assert.Equal(200, CommissionRules.MineReward(25, 0, 3));
		Assert.Equal(1125, CommissionRules.MineReward(25, 0, 30));
		Assert.Equal(CommissionRules.MaxGold, CommissionRules.Reward(100000, 2, 100));
		Assert.Equal(120, CommissionRules.GatherReward(1, 5));
	}

	[Fact]
	public void MaterialsRoundToFive()
	{
		Assert.Equal(45, CommissionRules.RoundMaterial(43, 30));
		Assert.Equal(7, CommissionRules.RoundMaterial(7, 4));
	}

	[Fact]
	public void KindWeightsFollow35_25_20_20()
	{
		Dictionary<string, int> counts = CommissionKinds.All.ToDictionary(k => k, _ => 0);
		Random rng = new Random(7);
		for (int i = 0; i < 20000; i++)
		{
			counts[CommissionRules.PickKind(rng)]++;
		}
		Assert.InRange(counts[CommissionKinds.Deliver], 6600, 7400);
		Assert.InRange(counts[CommissionKinds.Gather], 4600, 5400);
		Assert.InRange(counts[CommissionKinds.Mine], 3600, 4400);
		Assert.InRange(counts[CommissionKinds.Visit], 3600, 4400);
	}

	[Fact]
	public void VisitRewardIs100To200()
	{
		Random rng = new Random(1);
		HashSet<int> seen = Enumerable.Range(0, 500).Select(_ => CommissionRules.VisitReward(rng)).ToHashSet();
		Assert.Equal(new HashSet<int> { 100, 125, 150, 175, 200 }, seen);
	}

	[Theory]
	[InlineData(1, 5, false, 4, true)]
	[InlineData(1, 4, false, 4, false)]
	[InlineData(1, 20, true, 28, false)]
	[InlineData(2, 1, true, 28, true)]
	public void FirstYearOnlyAskForCropsThatCouldBeReady(int year, int day, bool fruitTree, int grow, bool ok) =>
		Assert.Equal(ok, CommissionRules.RealisticCrop(year, day, fruitTree, grow));

	[Fact]
	public void NoteTextIsCheckedForLengthAndNumbers()
	{
		Assert.Equal("想要些防风草，炖汤用。", CommissionRules.ValidateText("“想要些防风草，炖汤用。”", 5));
		Assert.NotNull(CommissionRules.ValidateText("需要 5 个防风草，炖汤用。", 5));
		Assert.Null(CommissionRules.ValidateText("需要 5 个防风草，给你 300g。", 5));
		Assert.Null(CommissionRules.ValidateText("短", 1));
		Assert.Null(CommissionRules.ValidateText(new string('长', 71), 1));
	}

	[Fact]
	public void NoteAndThanksComeBackTogether()
	{
		(string? note, string thanks) = CommissionRules.ParseNote("{\"note\": \"想要4个向日葵，拍照缺点亮色。\", \"thanks\": \"……还挺好看的。谢了。\"}", 4);
		Assert.Equal("想要4个向日葵，拍照缺点亮色。", note);
		Assert.Equal("……还挺好看的。谢了。", thanks);
		Assert.Equal(("想要些防风草，炖汤用。", ""), CommissionRules.ParseNote("想要些防风草，炖汤用。", 5));
		Assert.Equal("", CommissionRules.ParseNote("{\"note\": \"想要些防风草，炖汤用。\", \"thanks\": \"拿好你的 300g。\"}", 5).Thanks);
		Assert.Null(CommissionRules.ParseNote("{\"thanks\": \"谢了\"}", 5).Note);
	}

	[Fact]
	public void PromptShowsOwnRecentCommissions()
	{
		Commission open = new Commission { Kind = CommissionKinds.Deliver, Count = 5, RewardGold = 300, State = CommissionStates.Open, ItemId = "(O)24" };
		Commission old = new Commission { Kind = CommissionKinds.Visit, State = CommissionStates.Done, ClosedDay = 1 };
		Assert.True(CommissionRules.InPrompt(open, 30));
		Assert.False(CommissionRules.InPrompt(old, 30));
		string block = CommissionRules.PromptBlock(new[] { open }, 10, _ => "防风草");
		Assert.Contains("5 个防风草", block);
		Assert.Contains("还没接", block);
	}

	// ---------------- AI 礼物 ----------------

	[Fact]
	public void GiftNameIsCleanedAndCut()
	{
		Assert.Equal("矿洞深处的紫晶", AiGiftRules.CleanName("「矿洞深处的紫晶」"));
		Assert.Equal("一二三四五六七八九十一二", AiGiftRules.CleanName("一二三四五六七八九十一二三四"));
		Assert.Equal("ab", AiGiftRules.CleanName("[a]%b^#$"));
		Assert.Equal("", AiGiftRules.CleanName("  "));
		Assert.Equal("阿比盖尔送的紫水晶", AiGiftRules.FallbackName("阿比盖尔", "紫水晶"));
	}

	[Fact]
	public void GiftDescriptionGetsSignature()
	{
		Assert.Equal("矿洞里挖的。\n——阿比盖尔", AiGiftRules.CleanDescription("矿洞里挖的。", "阿比盖尔", "x"));
		Assert.Equal("说明\n别弄丢了——阿比盖尔", AiGiftRules.CleanDescription("说明\n别弄丢了——阿比盖尔", "阿比盖尔", "x"));
		Assert.Equal("一种紫色的石头。\n——阿比盖尔", AiGiftRules.CleanDescription("", "阿比盖尔", "一种紫色的石头。"));
		Assert.Equal("a b\n——x", AiGiftRules.CleanDescription("a^b", "x", ""));
	}

	[Fact]
	public void CandidatesPutRecentLastAndCapAtEight()
	{
		List<AiGiftRecord> gifts = new List<AiGiftRecord> { new AiGiftRecord { Npc = "Abigail", TemplateId = "(O)66", CreatedDay = 5 } };
		List<GiftCandidate> raw = new List<GiftCandidate> { new GiftCandidate("(O)66", "紫水晶", "n"), new GiftCandidate("(O)66", "紫水晶", "n") };
		raw.AddRange(Enumerable.Range(1, 10).Select(i => new GiftCandidate("(O)" + (100 + i), "x" + i, "n")));
		List<GiftCandidate> list = AiGiftRules.Order(raw, gifts, "abigail");
		Assert.Equal(AiGiftRules.MaxCandidates, list.Count);
		Assert.DoesNotContain(list, c => c.Id == "(O)66");
		Assert.Equal("(O)66", AiGiftRules.Order(raw.Take(3), gifts, "Abigail").Last().Id);
	}

	[Fact]
	public void MatchAcceptsIdUnqualifiedOrName()
	{
		List<GiftCandidate> offered = new List<GiftCandidate> { new GiftCandidate("(O)66", "紫水晶", "n") };
		Func<string, string?> qualify = s => s.StartsWith("(") ? s : "(O)" + s;
		Assert.NotNull(AiGiftRules.Match(offered, "(O)66", qualify));
		Assert.NotNull(AiGiftRules.Match(offered, "66", qualify));
		Assert.NotNull(AiGiftRules.Match(offered, "紫水晶", qualify));
		Assert.Null(AiGiftRules.Match(offered, "(O)67", qualify));
		Assert.Null(AiGiftRules.Match(offered, " ", qualify));
	}

	[Fact]
	public void DialogueGiftNeedsHeartsCooldownAndDailyRoom()
	{
		Assert.Equal("", AiGiftRules.DialogueBlock(true, true, 3, 3, false, 7, 1, 2, false));
		Assert.NotEqual("", AiGiftRules.DialogueBlock(false, true, 10, 3, false, 7, 0, 2, false));
		Assert.NotEqual("", AiGiftRules.DialogueBlock(true, true, 2, 3, false, 7, 0, 2, false));
		Assert.NotEqual("", AiGiftRules.DialogueBlock(true, true, 5, 3, true, 7, 0, 2, false));
		Assert.NotEqual("", AiGiftRules.DialogueBlock(true, true, 5, 3, false, 7, 2, 2, false));
		Assert.NotEqual("", AiGiftRules.DialogueBlock(true, true, 5, 3, false, 7, 0, 2, true));
		List<AiGiftRecord> gifts = new List<AiGiftRecord> { new AiGiftRecord { Npc = "Leah", CreatedDay = 10 } };
		Assert.True(AiGiftRules.OnCooldown(gifts, "leah", 16, 7));
		Assert.False(AiGiftRules.OnCooldown(gifts, "Leah", 17, 7));
	}

	[Fact]
	public void NamingReplyIsParsed()
	{
		NamedGift? g = AiGiftRules.ParseNaming("```json\n{\"item_id\": \"(O)66\", \"name\": \"紫晶\", \"description\": \"一块石头\"}\n```");
		Assert.NotNull(g);
		Assert.Equal("(O)66", g!.ItemId);
		Assert.Equal("紫晶", g.Name);
		Assert.Null(AiGiftRules.ParseNaming("不是 JSON"));
	}

	[Fact]
	public void GiftPromptListsLastThreeAndToday()
	{
		List<AiGiftRecord> gifts = Enumerable.Range(1, 4).Select(i => new AiGiftRecord { Npc = "Leah", Name = "礼物" + i, TemplateId = "(O)1", Delivered = true, DeliveredDay = 10 + i, Source = i == 4 ? AiGiftSources.Letter : AiGiftSources.Dialogue }).ToList();
		string block = AiGiftRules.PromptBlock(gifts, "Leah", 14, _ => "石头");
		Assert.Contains("礼物4", block);
		Assert.DoesNotContain("礼物1", block);
		Assert.Contains("随信寄的", block);
		Assert.Contains("别再送", block);
		Assert.Equal("", AiGiftRules.PromptBlock(gifts, "Abigail", 14, _ => ""));
	}

	// ---------------- 成人事件 ----------------

	private const string Catalog = "{\"packs\":[{\"id\":\"A.Pack\",\"dialogueNpcs\":[\"Caroline\"],\"events\":[{\"id\":\"54\",\"location\":\"Forest\",\"partners\":[\"Leah\"],\"replacesVanilla\":true},{\"id\":\"900\",\"location\":\"Farm\",\"partners\":[\"Abigail\"]}]},{\"id\":\"B.Pack\",\"events\":[{\"id\":\"58\",\"location\":\"Town\",\"partners\":[\"Haley\"],\"replacesVanilla\":true}]}]}";

	[Fact]
	public void OnlyInstalledPacksCount()
	{
		AdultCatalog c = new AdultCatalog();
		c.Load(Catalog);
		Assert.False(c.AnyActive);
		Assert.False(c.IsReplacedHeartEvent(54));
		c.Activate(id => id == "A.Pack");
		Assert.True(c.IsReplacedHeartEvent(54));
		Assert.False(c.IsReplacedHeartEvent(58));
		Assert.True(c.Touches("caroline"));
		Assert.True(c.Touches("Leah"));
		Assert.False(c.Touches("Haley"));
		Assert.Equal("A.Pack", c.Find("900", "farm")?.Pack);
		Assert.Null(c.Find("900", "Town"));
	}

	[Fact]
	public void BundledCatalogLoads()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "AISV.sln")))
		{
			dir = System.IO.Path.GetDirectoryName(dir);
		}
		string path = System.IO.Path.Combine(dir!, "src", "AISV.Mod", "assets", "adult", "adult_events.json");
		AdultCatalog c = new AdultCatalog();
		c.Load(System.IO.File.ReadAllText(path));
		Assert.Equal(4, c.Known.Count);
		c.Activate(_ => true);
		Assert.True(c.ActiveEvents.Count > 50);
	}

	[Fact]
	public void MemoryNeverDescribesTheScene()
	{
		AdultMemory r = new AdultMemory { Location = "Forest", Partners = new List<string> { "Leah", "Elliott" } };
		string text = AdultMemoryRules.Neutral("Leah", r, "第 1 年春 5 日", n => n == "Elliott" ? "艾利欧特" : n);
		Assert.Contains("和艾利欧特一起", text);
		Assert.Contains("在森林里", text);
		Assert.Contains("绝不描述", text);
		r.WithChildren = true;
		string kids = AdultMemoryRules.Neutral("Leah", r, "第 1 年春 5 日", n => n);
		Assert.DoesNotContain("亲密", kids);
		Assert.Contains("更亲近", kids);
		Assert.Equal("", AdultMemoryRules.PromptBlock(Array.Empty<string>()));
	}
}
