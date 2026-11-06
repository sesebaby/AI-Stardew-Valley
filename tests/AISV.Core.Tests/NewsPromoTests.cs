using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AISV.Core.News;
using AISV.Core.Promo;
using AISV.Core.Social.Gossip;
using Xunit;

namespace AISV.Core.Tests;

public class NewsPromoTests
{
	private static readonly IReadOnlyList<string[]> Names = new List<string[]>
	{
		new[] { "Abigail", "阿比盖尔" },
		new[] { "Sam", "山姆" },
		new[] { "Pierre", "皮埃尔" }
	};

	[Theory]
	[InlineData(7, "spring", 1, 1)]
	[InlineData(14, "spring", 1, 2)]
	[InlineData(28, "spring", 1, 4)]
	[InlineData(7, "summer", 1, 5)]
	[InlineData(7, "spring", 2, 17)]
	public void IssueNumberCountsWeeksFromFirstSunday(int day, string season, int year, int issue)
	{
		Assert.Equal(issue, WeeklyNewsRules.IssueNumber(day, season, year));
	}

	[Fact]
	public void IssueDaysAreEverySunday()
	{
		Assert.Equal(new[] { 7, 14, 21, 28 }, Enumerable.Range(1, 28).Where(WeeklyNewsRules.IsIssueDay));
	}

	[Fact]
	public void FactsKeepNewestFifteenInOrder()
	{
		List<GossipEntry> entries = Enumerable.Range(1, 20).Select(d => new GossipEntry { Day = d, Season = "spring", Year = 1, Fact = "第" + d + "天" }).Reverse().ToList();
		List<GossipEntry> facts = WeeklyNewsRules.FactEntriesOf(entries);
		Assert.Equal(15, facts.Count);
		Assert.Equal(6, facts[0].Day);
		Assert.Equal(20, facts[^1].Day);
		Assert.Contains("] 第20天", WeeklyNewsRules.Facts(entries));
	}

	[Fact]
	public void FactCheckDropsSentencesWithUnknownNumbersOrPeople()
	{
		string facts = "- [第 1 年春 5 日] 阿比盖尔在矿洞里待了 3 个小时。";
		string text = "⛏️ 阿比盖尔在矿洞待了 3 个小时！山姆说他也去了。她挖到了 12 块铜矿。";
		string kept = NewspaperFactCheck.FilterSentences(text, facts, Names, out int dropped);
		Assert.Equal(2, dropped);
		Assert.Equal("⛏️ 阿比盖尔在矿洞待了 3 个小时！", kept);
	}

	[Fact]
	public void FactCheckTreatsInternalAndDisplayNameAsOnePerson()
	{
		Assert.True(NewspaperFactCheck.IsGrounded("Abigail went to the mines.", "阿比盖尔去了矿洞", Names));
		Assert.False(NewspaperFactCheck.IsGrounded("Pierre raised prices.", "阿比盖尔去了矿洞", Names));
		Assert.False(NewspaperFactCheck.ContainsName("Samuel", "Sam"));
	}

	[Fact]
	public void ParseAcceptsObjectOrArray()
	{
		Assert.Equal(2, WeeklyNewsRules.Parse("```json\n{\"articles\":[{\"text\":\"a\"},{\"text\":\"b\"}]}\n```")!.Count);
		Assert.Equal("x", WeeklyNewsRules.Parse("[\"x\"]")![0].Text);
		Assert.Null(WeeklyNewsRules.Parse("没写出来"));
	}

	[Fact]
	public void PublishNeedsAtLeastOneColumn()
	{
		Assert.Null(WeeklyNewsRules.Publish(null, "", Names, new WeeklyColumns(), 7, "spring", 1, null));
		WeeklyColumns columns = new WeeklyColumns();
		columns.Market.Add(new WeeklyMarketLine { ItemId = "(O)24", Name = "防风草", Price = 40, WeekChange = 12.5f });
		WeeklyNews? n = WeeklyNewsRules.Publish(new[] { new WeeklyNewsEntry { Text = "皮埃尔涨价了。" } }, "", Names, columns, 14, "spring", 1, ("山谷之外", "远方^的消息"));
		Assert.NotNull(n);
		Assert.Empty(n!.Stories);
		Assert.Equal(1, n.CutLines);
		Assert.Equal(2, n.Edition);
		Assert.Equal("远方的消息", n.FarBody);
	}

	[Fact]
	public void StoreReplacesSameDayAndKeepsTwelve()
	{
		List<WeeklyNews> all = new List<WeeklyNews>();
		for (int i = 1; i <= 14; i++)
		{
			WeeklyNewsRules.Store(all, new WeeklyNews { Day = 7, Season = "spring", Year = i, Edition = i });
		}
		WeeklyNewsRules.Store(all, new WeeklyNews { Day = 7, Season = "spring", Year = 14, Edition = 99 });
		Assert.Equal(12, all.Count);
		Assert.Equal(99, WeeklyNewsRules.Newest(all)[0].Edition);
	}

	[Fact]
	public void MailGoesOutOneOrTwoDaysAfterIssue()
	{
		WeeklyNews n = new WeeklyNews { Day = 7, Season = "spring", Year = 1 };
		Assert.False(WeeklyNewsRules.ShouldMail(n, 7));
		Assert.True(WeeklyNewsRules.ShouldMail(n, 8));
		Assert.True(WeeklyNewsRules.ShouldMail(n, 9));
		Assert.False(WeeklyNewsRules.ShouldMail(n, 10));
		Assert.Equal("AISV.WeeklyNews.1.spring.7", WeeklyNewsRules.MailKey(n));
	}

	[Fact]
	public void MailSummaryHasLeadMarketAndPointer()
	{
		WeeklyNews n = new WeeklyNews { Day = 7, Season = "spring", Year = 1, Edition = 1 };
		n.Stories.Add(new WeeklyNewsEntry { Text = "🎉 镇上开了舞会。" });
		n.Prices.Add(new WeeklyMarketLine { Name = "防风草", Price = 40, WeekChange = 12.5f });
		n.Jobs.Add(new WeeklyCommissionLine { Id = "c1" });
		string mail = WeeklyNewsRules.MailContent(n);
		Assert.Contains("头条：🎉 镇上开了舞会。", mail);
		Assert.Contains("防风草 40g（比一周前涨 12.5％）", mail);
		Assert.Contains("还有 1 条委托", mail);
		Assert.EndsWith("[#]鹈鹕镇周报 第1期", mail);
		Assert.DoesNotContain("%", mail);
	}

	[Fact]
	public void WeekChangeComparesWithFirstDay()
	{
		Assert.Equal(25f, WeeklyNewsRules.WeekChange(new[] { 0.8f, 0.9f, 1.0f }));
		Assert.Equal(0f, WeeklyNewsRules.WeekChange(new[] { 1f }));
	}

	private static JsonObject Content(string game = "星海旅人") => JsonNode.Parse($$"""
		{
		  "version": "v3",
		  "announcement": " 服务器维护 ",
		  "promo": [ { "id": 1, "title": "新作", "body": "好玩", "image": "0123456789abcdef.png", "linkUrl": "https://example.com/a" },
		             { "id": 2, "title": "坏链接", "linkUrl": "javascript:alert(1)" } ],
		  "support": [ { "id": 5, "url": "https://example.com/s", "rewardItem": "74", "rewardQty": 5000 },
		               { "id": 6, "url": "ftp://bad" } ],
		  "campaign": { "gameName": "《{{game}}》", "tagline": "一起去看星星。", "paperEveryIssues": 0, "letterEveryDays": 3 }
		}
		""")!.AsObject();

	[Fact]
	public void ServerContentParsesAndRejectsUnsafeLinks()
	{
		ServerContent c = ServerContent.Parse(Content());
		Assert.Equal("服务器维护", c.Announcement);
		Assert.Equal("", c.Promo[1].LinkUrl);
		Assert.Single(c.Support);
		Assert.Equal(999, c.Support[0].RewardQty);
		Assert.Equal("星海旅人", c.Campaign.Game);
		Assert.Equal("一起去看星星", c.Campaign.Line);
		Assert.Equal(1, c.Campaign.PaperEveryIssues);
		Assert.Equal(28, c.Campaign.LetterEveryDays);
		Assert.Equal(PromoCampaign.DefaultAuthor, c.Campaign.Author);
		Assert.Equal(new[] { "0123456789abcdef.png" }, ServerContent.Images(Content()));
	}

	[Fact]
	public void ImageUrlsGoThroughServerOrHttp()
	{
		Assert.Equal("https://s/media/0123456789abcdef.png", ServerContent.ImageUrl("0123456789abcdef.png", "https://s"));
		Assert.Equal("https://s/media/x.png", ServerContent.ImageUrl("/media/x.png", "https://s"));
		Assert.Null(ServerContent.ImageUrl("file:///etc/passwd", "https://s"));
		Assert.EndsWith(".png", ServerContent.CacheName("https://s/a.webp"));
	}

	[Fact]
	public void PromoTimingRespectsQuietDaysAndGaps()
	{
		PromoState s = new PromoState { FirstDay = 10 };
		Assert.False(PromoRules.Due(s, -1, 7, PromoRules.GossipQuietDays, 11));
		Assert.True(PromoRules.Due(s, -1, 7, PromoRules.GossipQuietDays, 12));
		Assert.False(PromoRules.Due(s, 12, 7, PromoRules.GossipQuietDays, 18));
		Assert.True(PromoRules.Due(s, 12, 7, PromoRules.GossipQuietDays, 19));
		PromoCampaign c = ServerContent.Parse(Content()).Campaign;
		Assert.False(PromoRules.LetterDue(s, c, 30));
		Assert.True(PromoRules.LetterDue(s, c, 31));
		s.LetterGames.Add("星海旅人");
		Assert.False(PromoRules.LetterDue(s, c, 100));
		Assert.True(PromoRules.ArcRoom(1, 3));
		Assert.False(PromoRules.ArcRoom(2, 3));
	}

	[Fact]
	public void PaperColumnOnlyOnItsIssues()
	{
		PromoCampaign c = new PromoCampaign { GameName = "星海旅人", PaperEveryIssues = 4 };
		Assert.Null(PromoRules.PaperColumn(c, 3));
		(string title, string text) = PromoRules.PaperColumn(c, 8)!.Value;
		Assert.Equal("山谷之外", title);
		Assert.Contains("《星海旅人》", text);
		Assert.Null(PromoRules.PaperColumn(new PromoCampaign(), 4));
	}

	[Fact]
	public void PromoTextsUseTownWording()
	{
		PromoCampaign c = new PromoCampaign { GameName = "星海旅人", Tagline = "一起去看星星" };
		Assert.Equal("听说祖祖城有个叫远古牛哥的人做了个新游戏《星海旅人》，一起去看星星，挺想去看看的。", PromoRules.Gossip(c, "山姆").Chat);
		Assert.Equal(2, PromoRules.GroupLines(c, 2).Count);
		Assert.Single(PromoRules.GroupLines(c, 1));
		Assert.DoesNotContain("\n", PromoRules.LetterBody(c));
		Assert.StartsWith("@：^", PromoRules.LetterBody(c));
		AISV.Core.Stories.StoryArc arc = PromoRules.Arc(c, "AIStardewValley.Core", 1, "summer");
		Assert.Equal("AIStardewValley.Core/promo-y1-summer", arc.Id);
		Assert.All(arc.Goals, o => Assert.Equal("talk_to", o.Kind));
	}

	[Fact]
	public void SupportRewardsAndMail()
	{
		SupportTask fixedTask = new SupportTask { Id = 5, RewardItem = "74", RewardQty = 2 };
		Assert.Equal(("(O)74", 2), PromoRules.FixedReward(fixedTask, _ => true));
		Assert.Null(PromoRules.FixedReward(fixedTask, _ => false));
		(string id, int _) = PromoRules.PickReward(new SupportTask(), _ => true, new Random(1));
		Assert.Contains(PromoRules.RarePool, p => p.Id == id);
		PromoState s = new PromoState { SupportDone = { 1 } };
		Assert.Equal(new long[] { 2, 3 }, PromoRules.OpenSupport(new[] { 1L, 2, 3, 4 }.Select(i => new SupportTask { Id = i }), s).Select(t => t.Id));
		string mail = PromoRules.SupportMail("远古%牛哥", "(O)74", 1);
		Assert.Contains("%item id (O)74 1 %%[#]", mail);
		Assert.StartsWith("@，^^", mail);
		Assert.Contains("远古％牛哥", mail);
	}
}
