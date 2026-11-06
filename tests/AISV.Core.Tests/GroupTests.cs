using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using Xunit;

namespace AISV.Core.Tests;

public class GroupTests
{
	private static readonly string[] Speakers = { "Sam", "Abigail", "Sebastian" };

	private static GroupRequest Request(GroupSetting setting = GroupSetting.FaceToFace, string? player = null, bool among = false, int count = 3) => new GroupRequest
	{
		PlayerName = "阿星",
		Members = Speakers.Take(count).Select(n => new GroupMember(n, n + " 的性格", 4, new[] { "春季 3 日：农夫说「你好」" }, "开心", "", "听说镇上要下雨", "")).ToList(),
		Setting = setting,
		Location = "Town",
		Time = 1330,
		Season = "spring",
		Day = 5,
		Year = 1,
		Weather = "晴",
		Relations = GroupPrompts.Relations(Speakers.Take(count).ToList()),
		Transcript = new[] { new GroupLine("Sam", "今天好热。"), new GroupLine("阿星", "是啊") },
		PlayerText = player,
		AmongThemselves = among,
		FarmerDistance = 5
	};

	[Fact]
	public void ParserReadsLinesFriendshipAndDisplayNames()
	{
		string raw = "```json\n{\"lines\":[{\"speaker\":\"山姆\",\"text\":\"Sam: 嘿 *挥手* 你来啦$h\",\"emotion\":\"HAPPY\"},{\"name\":\"Abigail\",\"dialogue\":\"\",\"emotion\":\"sad\"},{\"speaker\":\"Nobody\",\"text\":\"谁\"},{\"npc\":\"abigail\",\"line\":\"一起去矿洞吗？\"}],\"friendship\":{\"Sam\":\"+55\",\"Abigail\":-3,\"Nobody\":9}}\n```";
		GroupTurn? turn = GroupReply.Parse(raw, Speakers, n => n == "Sam" ? "山姆" : null);
		Assert.NotNull(turn);
		Assert.Equal(2, turn!.Lines.Count);
		Assert.Equal(new GroupLine("Sam", "嘿 你来啦", "happy"), turn.Lines[0]);
		Assert.Equal("Abigail", turn.Lines[1].Speaker);
		Assert.Equal(40, turn.Friendship["Sam"]);
		Assert.Equal(-3, turn.Friendship["Abigail"]);
		Assert.False(turn.Friendship.ContainsKey("Nobody"));
	}

	[Fact]
	public void ParserAcceptsArraysAndCapsLines()
	{
		string raw = "[" + string.Join(",", Enumerable.Range(0, 9).Select(i => $"{{\"speaker\":\"{Speakers[i % 3]}\",\"text\":\"第{i}句\"}}")) + "]";
		GroupTurn? turn = GroupReply.Parse(raw, Speakers);
		Assert.Equal(GroupReply.MaxLines, turn!.Lines.Count);
		Assert.Empty(turn.Friendship);
		Assert.Null(GroupReply.Parse("没有 JSON", Speakers));
		Assert.Null(GroupReply.Parse("{\"lines\":[]}", Speakers));
	}

	[Fact]
	public void LongLinesAreCutWithEllipsis()
	{
		string text = GroupReply.Clean(new string('长', 300), "Sam");
		Assert.Equal(GroupReply.MaxLineChars, text.Length);
		Assert.EndsWith("…", text);
		Assert.Equal("你好", GroupReply.Clean("山姆：你好", "Sam", "山姆"));
	}

	[Fact]
	public void OpeningPromptHandsTheTalkToTheFarmer()
	{
		GroupRequest r = Request();
		string system = GroupPrompts.System(r, "中文");
		Assert.StartsWith("这次只说中文。", system);
		Assert.Contains("Sam, Abigail, Sebastian", system);
		Assert.Contains("把话头递给农夫", system);
		Assert.Contains("写 3～4 句", system);
		Assert.Contains("SAM_PERSONALITY_BEGIN", system);
		Assert.Contains("Sam 和 Sebastian：", system);
		Assert.DoesNotContain("friendship", system);
		Assert.Contains("每句最多 180 个字", system);
		Assert.Equal("农夫走过来了，写 Sam、Abigail 和 Sebastian 怎么招呼他、把他带进话题。", GroupPrompts.User(r));
	}

	[Fact]
	public void ReplyPromptAsksForFriendshipAndAddressedSpeaker()
	{
		GroupRequest r = Request(player: "Abigail 你去过矿洞吗") with { Addressed = new[] { "Abigail" }, Final = true };
		string system = GroupPrompts.System(r, "中文");
		Assert.Contains("\"friendship\":{\"Sam\":0,\"Abigail\":0,\"Sebastian\":0}", system);
		Assert.Contains("就该散了", system);
		Assert.Contains("写 2～5 句", system);
		string user = GroupPrompts.User(r);
		Assert.Contains("农夫：是啊", user);
		Assert.Contains("Sam：今天好热。", user);
		Assert.Contains("PLAYER_RESPONSE_BEGIN", user);
		Assert.DoesNotContain("叫到了", user);
		Assert.Contains("第一句得由这位来说", GroupPrompts.User(r with { Setting = GroupSetting.OnMap }));
	}

	[Fact]
	public void MapPromptKeepsLinesShortAndDescribesDistance()
	{
		GroupRequest r = Request(GroupSetting.OnMap, among: true) with { Transcript = Array.Empty<GroupLine>(), JustJoined = new[] { "Sebastian" } };
		string system = GroupPrompts.System(r, "中文");
		Assert.Contains("每句最多 60 个字", system);
		Assert.Contains("头顶的小气泡", system);
		Assert.Contains("在大约 5 步外，听得见", system);
		Assert.Contains("Sebastian 刚走过来加入", system);
		Assert.Contains("先不要结束对话", system);
		Assert.Equal("Sam、Abigail 和 Sebastian 正站在一块儿，写他们怎么聊开的。", GroupPrompts.User(r));
		GroupRequest more = r with { Transcript = new[] { new GroupLine("Sam", "嗨") }, FarmerJoined = true, FarmerDistance = 2 };
		Assert.Contains("已经插过话", GroupPrompts.System(more, "中文"));
		Assert.Contains("就站在他们身边", GroupPrompts.System(more, "中文"));
		Assert.EndsWith("Sam、Abigail 和 Sebastian 接着往下聊，写下一段。", GroupPrompts.User(more));
	}

	[Fact]
	public void PhonePromptHasNoAwarenessAndShortMessages()
	{
		GroupRequest r = Request(GroupSetting.Phone, player: "大家好");
		string system = GroupPrompts.System(r, "中文");
		Assert.Contains("居民群", system);
		Assert.Contains("1～3 条", system);
		Assert.Contains("最多 80 个字", system);
		Assert.DoesNotContain("friendship", system);
		Assert.Contains("写出 1～3 位村民的回复。", GroupPrompts.User(r));
		GroupRequest villagers = r with { PlayerText = null, Transcript = Array.Empty<GroupLine>() };
		Assert.Contains("Sam 先发", GroupPrompts.System(villagers, "中文"));
		Assert.Equal("群里最近的消息：\n\n群里还冷冷清清的。下面由 Sam 挑起一个新的小话题，写出接下来的几条。", GroupPrompts.User(villagers));
	}

	[Fact]
	public void PairPickerPrefersCloseGroupsNearTheFarmer()
	{
		List<GroupSpot> spots = new List<GroupSpot> { new("A", 0, 0), new("B", 3, 0), new("C", 30, 30), new("D", 31, 30), new("E", 10, 0) };
		(GroupSpot Lead, GroupSpot Other)? pair = GroupRules.PickPair(spots, 4, 0);
		Assert.Equal(("B", "A"), (pair!.Value.Lead.Name, pair.Value.Other.Name));
		Assert.Null(GroupRules.PickPair(new List<GroupSpot> { new("A", 0, 0), new("B", 7, 0) }, 0, 0));
		Assert.Null(GroupRules.PickPair(new List<GroupSpot> { new("A", 0, 0), new("B", 1, 0) }, 30, 0));
	}

	[Fact]
	public void RulesHelpers()
	{
		Assert.Equal(new List<string> { "Abigail" }, GroupRules.Addressed("阿比盖尔你好", new[] { ("Sam", "山姆"), ("Abigail", "阿比盖尔") }));
		Assert.Equal("", GroupRules.DescribeTalk(new ConversationEntry { FarmerReply = "[听了]" }));
		Assert.Equal("春 3 日，农夫：「嗨」 你：「你好」", GroupRules.DescribeTalk(new ConversationEntry { Season = "spring", Day = 3, FarmerReply = "嗨", Comeback = "你好" }));
		Assert.Equal(2200, GroupRules.ReadingMs("短", true));
		Assert.Equal(6500, GroupRules.ReadingMs(new string('长', 60), true));
		Assert.Equal(TimeSpan.FromMilliseconds(1600), GroupRules.TypingDelay(1));
		List<string> parts = GroupRules.SplitBubbles("今天天气很好，我们去海边走走吧。顺便看看有没有贝壳可以捡回家。", 20);
		Assert.True(parts.All(p => p.Length <= 20));
		Assert.Equal("今天天气很好，我们去海边走走吧。", parts[0]);
		Assert.Equal(6, GroupRules.MaxMembers(9));
		Assert.Equal(1, GroupRules.AmbientRounds(0));
	}

	[Fact]
	public void ResidentsChecksAndCounts()
	{
		ResidentsState s = new ResidentsState();
		Assert.Equal(ResidentsBlock.AiOff, ResidentsGroup.Check("hi", false, false, 3, 5));
		Assert.Equal(ResidentsBlock.Empty, ResidentsGroup.Check("  ", true, false, 3, 5));
		Assert.Equal(ResidentsBlock.TooLong, ResidentsGroup.Check(new string('a', 201), true, false, 3, 5));
		Assert.Equal(ResidentsBlock.Busy, ResidentsGroup.Check("hi", true, true, 3, 5));
		Assert.Equal(ResidentsBlock.NoPostsLeft, ResidentsGroup.Check("hi", true, false, 0, 5));
		Assert.Equal(ResidentsBlock.TooFewMembers, ResidentsGroup.Check("hi", true, false, 3, 1));
		Assert.Equal(ResidentsBlock.None, ResidentsGroup.Check("hi", true, false, 3, 2));
		ResidentsGroup.CountPost(s, 10);
		ResidentsGroup.CountPost(s, 10);
		Assert.Equal(4, ResidentsGroup.PostsLeft(s, 10, 6));
		Assert.Equal(6, ResidentsGroup.PostsLeft(s, 11, 6));
		ResidentsGroup.CountPost(s, 11);
		Assert.Equal(1, s.PostsToday);
		for (int i = 0; i < 305; i++)
		{
			ResidentsGroup.Append(s, new ResidentsMessage { Text = i.ToString() });
		}
		Assert.Equal(ResidentsGroup.MaxStored, s.Messages.Count);
		Assert.Equal("5", s.Messages[0].Text);
		Assert.False(ResidentsGroup.HasPhone("Linus"));
		Assert.True(ResidentsGroup.HasPhone("Sam"));
	}

	[Fact]
	public void ResidentsCandidatesStartWithMentionsAndRecentSpeakers()
	{
		var members = new List<(string, string, int)> { ("Sam", "山姆", 2), ("Abigail", "阿比盖尔", 8), ("Pierre", "皮埃尔", 0), ("Emily", "艾米丽", 5), ("Leah", "莉亚", 1), ("Gus", "格斯", 3), ("Haley", "海莉", 0) };
		List<ResidentsMessage> history = new List<ResidentsMessage>
		{
			new() { Speaker = "Gus" }, new() { Speaker = "Leah" }, new() { Speaker = "阿星", IsPlayer = true }, new() { Speaker = "Gus" }
		};
		List<string> pool = ResidentsGroup.Candidates("皮埃尔，店里有种子吗", members, history, new Random(1));
		Assert.Equal(ResidentsGroup.CandidatePool, pool.Count);
		Assert.Equal(new[] { "Pierre", "Gus", "Leah" }, pool.Take(3));
		Assert.Equal(pool.Count, pool.Distinct().Count());
	}

	[Fact]
	public void ResidentsPlanSpacesTimesAnHourApart()
	{
		List<int> times = ResidentsGroup.PlanTimes(4, 900, new Random(3));
		Assert.InRange(times.Count, 1, 4);
		Assert.All(times, t => Assert.InRange(t, 910, 2100));
		Assert.All(times, t => Assert.Equal(0, t % 10));
		for (int i = 1; i < times.Count; i++)
		{
			Assert.True(ResidentsGroup.Minutes(times[i]) - ResidentsGroup.Minutes(times[i - 1]) >= 60);
		}
		Assert.Empty(ResidentsGroup.PlanTimes(0, 600, new Random(1)));
		List<string> pool = ResidentsGroup.VillagerPool(new[] { "Sam", "Abigail", "Sebastian", "Pierre", "Gus", "Emily" }, (a, b) => GroupPrompts.Relation(a, b).Length > 0, new Random(2));
		Assert.Equal(4, pool.Count);
		Assert.Equal(4, pool.Distinct().Count());
	}

	[Fact]
	public void FarmerTopicIsOccasionalAndCoolsDown()
	{
		FarmerTopics topics = new FarmerTopics();
		(string, string)[] fresh = { ("e1", "送礼被嫌弃"), ("e2", "喝醉了") };
		Assert.Equal("", topics.Pick("phone", 10, fresh, roll: 0.9));
		Assert.Equal("送礼被嫌弃", topics.Pick("phone", 10, fresh, roll: 0.1));
		Assert.Equal("", topics.Pick("phone", 10, fresh, roll: 0.1));
		Assert.Equal("送礼被嫌弃", topics.Pick("map", 10, fresh, roll: 0.1));
		Assert.Equal("喝醉了", topics.Pick("phone", 11, fresh, roll: 0.1));
		Assert.Equal("", topics.Pick("phone", 12, fresh, roll: 0.1));

		FarmerTopics always = new FarmerTopics { Always = true };
		Assert.Equal("送礼被嫌弃", always.Pick("scene", 10, fresh, roll: 0.9));
		Assert.Equal("喝醉了", always.Pick("scene", 10, fresh, roll: 0.9));
		Assert.Equal("", always.Pick("scene", 10, fresh, roll: 0.1));
	}

	[Fact]
	public void FarmerTopicSteersTheFirstPoster()
	{
		GroupRequest r = new GroupRequest { PlayerName = "牛哥", Setting = GroupSetting.Phone, Members = new[] { new GroupMember("Haley", "", 0, Array.Empty<string>(), "", "", "", "") }, FarmerTopic = "听说牛哥送了海莉一块五彩碎片" };
		string prompt = GroupPrompts.System(r, "中文");
		Assert.Contains("说起农夫最近的这件事", prompt);
		Assert.Contains("五彩碎片", prompt);
		Assert.DoesNotContain("说说自己今天的一件具体小事", prompt);
	}

	[Fact]
	public void PromptTellsHowToCallMembers()
	{
		GroupMember gus = new GroupMember("Gus", "", 0, Array.Empty<string>());
		GroupMember pam = new GroupMember("Pam", "", 0, Array.Empty<string>());
		GroupRequest r = new GroupRequest { PlayerName = "牛哥", Setting = GroupSetting.Phone, Members = new[] { gus, pam }, Display = new Dictionary<string, string> { ["Gus"] = "格斯", ["Pam"] = "潘姆" } };
		Assert.Contains("Gus 叫格斯，Pam 叫潘姆", GroupPrompts.System(r, "中文"));
		Assert.Contains("Gus 叫格斯", GroupPrompts.System(r with { Setting = GroupSetting.FaceToFace }, "中文"));
		Assert.DoesNotContain("称呼", GroupPrompts.System(r with { Display = new Dictionary<string, string> { ["Gus"] = "Gus" } }, "English"));
	}
}
