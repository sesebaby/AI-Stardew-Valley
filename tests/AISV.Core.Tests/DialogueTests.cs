using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;
using AISV.Core.World;
using Xunit;

namespace AISV.Core.Tests;

/// <summary>假的 AI：按顺序回排好的文字，记下每个请求。</summary>
internal sealed class FakeSender : IAiSender
{
	private readonly Queue<Func<Task<AiResult>>> _replies = new Queue<Func<Task<AiResult>>>();

	public List<ChatRequest> Requests { get; } = new List<ChatRequest>();

	public bool UsesServer { get; set; }

	public int ReplyChars { get; set; } = 275;

	public int ServerMemoryCap { get; set; } = 40;

	public FakeSender Reply(string text)
	{
		_replies.Enqueue(() => Task.FromResult(AiResult.Success(text)));
		return this;
	}

	public FakeSender Fail(FailureKind kind)
	{
		_replies.Enqueue(() => Task.FromResult(AiResult.Fail(kind)));
		return this;
	}

	public FakeSender Wait(Task<AiResult> pending)
	{
		_replies.Enqueue(() => pending);
		return this;
	}

	public Task<AiResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
	{
		Requests.Add(request);
		if (_replies.Count == 0)
		{
			throw new InvalidOperationException("没有排好的回复了");
		}
		return _replies.Dequeue()();
	}
}

public class DialogueEngineTests
{
	private static readonly GameDate Today = new GameDate(13, "spring", 1);

	private readonly ModConfig _config = new ModConfig().Normalize();

	private readonly FakeSender _ai = new FakeSender();

	private readonly ConversationLog _log;

	private readonly DialogueEngine _engine;

	public DialogueEngineTests()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		_log = new ConversationLog(() => store, () => 100);
		PromptTemplates templates = new PromptTemplates(new DirectoryBackend(PromptDir()), NullLog.Instance);
		// 方向固定从 0 开始掷，结果可预测。
		_engine = new DialogueEngine(() => _config, _ai, templates, _log, new RecentLines(), NullLog.Instance, () => "简体中文", new OpeningTopicPicker(new Random(1)));
	}

	internal static string PromptDir()
	{
		string? dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "AISV.sln")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		return Path.Combine(dir!, "src", "AISV.Mod", "assets", "prompts");
	}

	private static DialogueMaterial M(string npc = "Abigail", int hearts = 4, bool spouse = false, IReadOnlySet<int>? events = null) => new DialogueMaterial
	{
		NpcName = npc,
		PlayerName = "小麦",
		Hearts = hearts,
		RawHearts = hearts,
		Season = "spring",
		Day = 13,
		Year = 1,
		IsSpouse = spouse,
		Personality = "测试人设",
		EventsSeen = events ?? new HashSet<int>()
	};

	private void Talked(string npc, int day = 10) => _log.Add(npc, new ConversationEntry { Day = day, Season = "spring", Year = 1, Farmer = "小麦", Opener = "以前的话", FarmerReply = "嗯", Comeback = "好" });

	[Fact]
	public async Task FirstMeetingWhenNoHistoryAndFewHearts()
	{
		_ai.Reply("你好，新来的？");
		OpeningResult? r = await _engine.OpeningAsync(M(hearts: 2), Today);
		Assert.Equal(OpeningKind.FirstMeeting, r!.Kind);
		Assert.Equal(DialogueEngine.FirstMeetingRequest, _ai.Requests[0].Name);
		Assert.Equal(3072, _ai.Requests[0].MaxOutputTokens);
		Assert.Equal(0.9, _ai.Requests[0].Temperature);
	}

	[Fact]
	public async Task FirstMeetingInALaterYearIsNotANewFace()
	{
		_ai.Reply("好久不见了吧。");
		await _engine.OpeningAsync(new DialogueMaterial { NpcName = "Pam", DisplayName = "潘姆", PlayerName = "小麦", Hearts = 0, Year = 2 }, Today);
		Assert.DoesNotContain("新面孔", _ai.Requests[0].System);
		Assert.Contains("已经住到第 2 年", _ai.Requests[0].System);
		_ai.Reply("新来的？");
		await _engine.OpeningAsync(M(npc: "Leah", hearts: 0), Today);
		Assert.Contains("新面孔", _ai.Requests[1].System);
	}

	[Fact]
	public async Task RewrittenCardOutranksOldImpressions()
	{
		_ai.Reply("早安。");
		await _engine.OpeningAsync(M(npc: "Haley", hearts: 5), Today);
		Assert.DoesNotContain("玩家改写过你的设定", _ai.Requests[0].System);
		_ai.Reply("早安，亲爱的。");
		await _engine.OpeningAsync(new DialogueMaterial { NpcName = "Leah", DisplayName = "莉亚", PlayerName = "小麦", Hearts = 5, CardRewritten = true }, Today);
		Assert.Contains("玩家改写过你的设定", _ai.Requests[1].System);
	}

	[Fact]
	public void SilenceBeforeTodayLastsThroughTheConversation()
	{
		_log.MarkTalkedToday("Haley", new GameDate(3, "spring", 1));
		_log.MarkTalkedToday("Haley", new GameDate(13, "spring", 1));
		Assert.Equal(10, _log.DaysSinceLastTalk("Haley", new GameDate(13, "spring", 1)));
		Assert.Equal(2, _log.DaysSinceLastTalk("Haley", new GameDate(15, "spring", 1)));
	}

	[Fact]
	public async Task RegularOpeningWithHistoryAndCachedAfterwards()
	{
		Talked("Abigail");
		_ai.Reply("又见面啦。");
		OpeningResult? first = await _engine.OpeningAsync(M(hearts: 1), Today);
		Assert.Equal(OpeningKind.Regular, first!.Kind);
		Assert.Equal(DialogueEngine.OpeningRequest, _ai.Requests[0].Name);
		Assert.Contains("今天第 1 句", _ai.Requests[0].User);
		OpeningResult? again = await _engine.OpeningAsync(M(hearts: 1), Today);
		Assert.Equal(OpeningKind.Cached, again!.Kind);
		Assert.Equal("又见面啦。", again.Dialogue);
		Assert.Single(_ai.Requests);
	}

	[Fact]
	public async Task SecondConversationTodaySaysSo()
	{
		Talked("Abigail", day: 13);
		_ai.Reply("还有事？");
		await _engine.OpeningAsync(M(), Today);
		Assert.Contains("今天第 2 句", _ai.Requests[0].User);
	}

	[Fact]
	public async Task HeartEventFollowupOnlyFromFourHeartsAndOnlyOnce()
	{
		Talked("Abigail");
		_ai.Reply("那天的事……谢谢你。").Reply("又来啦。");
		OpeningResult? r = await _engine.OpeningAsync(M(hearts: 4, events: new HashSet<int> { 1, 2 }), Today);
		Assert.Equal(OpeningKind.HeartEventFollowup, r!.Kind);
		Assert.Contains("2♥、4♥", _ai.Requests[0].System);
		Assert.Equal(new[] { 1, 2 }, _log.HeartEventsSeen("Abigail"));
		_engine.StartNewDay();
		OpeningResult? next = await _engine.OpeningAsync(M(hearts: 4, events: new HashSet<int> { 1, 2 }), new GameDate(14, "spring", 1));
		Assert.Equal(OpeningKind.Regular, next!.Kind);
	}

	[Fact]
	public async Task NoHeartEventFollowupBelowFourHearts()
	{
		Talked("Abigail");
		_ai.Reply("嗨。");
		OpeningResult? r = await _engine.OpeningAsync(M(hearts: 3, events: new HashSet<int> { 1, 2 }), Today);
		Assert.Equal(OpeningKind.Regular, r!.Kind);
		Assert.Empty(_log.HeartEventsSeen("Abigail"));
	}

	[Fact]
	public async Task FinishedConversationEndsTheDayExceptForSpouseUnderLimit()
	{
		Talked("Abigail");
		_ai.Reply("开场");
		await _engine.OpeningAsync(M(), Today);
		_engine.CommitSkip("Abigail", "小麦", Today);
		Assert.Null(await _engine.OpeningAsync(M(), Today));
		Assert.False(_engine.CanStartDaily("Abigail", false, Today));

		_config.SpouseTalksDaily = 2;
		Talked("Sebastian");
		_ai.Reply("老婆早").Reply("又来了").Reply("不该再生成");
		await _engine.OpeningAsync(M("Sebastian", spouse: true), Today);
		_engine.CommitSkip("Sebastian", "小麦", Today);
		Assert.True(_engine.CanStartDaily("Sebastian", true, Today));
		Assert.Equal("又来了", (await _engine.OpeningAsync(M("Sebastian", spouse: true), Today))!.Dialogue);
		_engine.CommitSkip("Sebastian", "小麦", Today);
		Assert.Equal(2, _log.CompletedSpouseConversations("Sebastian", Today));
		Assert.Null(await _engine.OpeningAsync(M("Sebastian", spouse: true), Today));
	}

	[Fact]
	public async Task SkipAfterAReplyRemembersTheLastLine()
	{
		Talked("Abigail");
		_ai.Reply("开场");
		await _engine.OpeningAsync(M(), Today);
		_log.Add("Abigail", new ConversationEntry { Day = Today.Day, Season = Today.Season, Year = Today.Year, Farmer = "小麦", Opener = "开场", FarmerReply = "你好", Comeback = "后来那句" });
		Assert.Equal("后来那句", _engine.CommitSkip("Abigail", "小麦", Today)!.Opener);

		Talked("Sebastian");
		_ai.Reply("嗯");
		await _engine.OpeningAsync(M("Sebastian"), Today);
		Assert.Equal("嗯", _engine.CommitSkip("Sebastian", "小麦", Today)!.Opener);
	}

	[Fact]
	public async Task OnlyOneGenerationPerNpcAtATime()
	{
		Talked("Abigail");
		TaskCompletionSource<AiResult> pending = new TaskCompletionSource<AiResult>();
		_ai.Wait(pending.Task);
		Task<OpeningResult?> first = _engine.OpeningAsync(M(), Today);
		Assert.True(_engine.IsGeneratingFor("Abigail"));
		Assert.Null(await _engine.OpeningAsync(M(), Today));
		pending.SetResult(AiResult.Success("嗨"));
		Assert.Equal("嗨", (await first)!.Dialogue);
		Assert.False(_engine.IsGenerating);
	}

	[Fact]
	public async Task LongOpeningIsTruncatedAndRepeatsRemoved()
	{
		Talked("Abigail");
		_ai.ReplyChars = 50;
		_ai.Reply("今天天气真好。今天天气真好。" + new string('啊', 80));
		OpeningResult? r = await _engine.OpeningAsync(M(), Today);
		// 硬截时补一个省略号。
		Assert.True(r!.Dialogue.Length <= 51);
		Assert.Equal(1, CountOf(r.Dialogue, "今天天气真好"));
	}

	private static int CountOf(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;

	[Fact]
	public async Task DramaBlockAndAdultToneReachThePrompts()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"哼\",\"warmth\":0,\"feeling\":\"angry\"}").Reply("{\"says\":\"嗯\",\"warmth\":0,\"feeling\":\"neutral\"}");
		DialogueMaterial Drama(bool adult) => new DialogueMaterial { NpcName = "Abigail", PlayerName = "小麦", Hearts = 4, RawHearts = 4, Season = "spring", Day = 13, Year = 1, Personality = "测试人设", EventsSeen = new HashSet<int>(), SocialDrama = "\n【村民之间的事】\n- 你对山姆：有点烦。\n", AdultRelationshipDrama = adult };
		DialogueMaterial m = Drama(true);
		_config.DramaHeat = "Dramatic";
		_config.AdultDramaAllowed = true;
		await _engine.OpeningAsync(m, Today);
		Assert.Contains("【村民之间的事】", _ai.Requests[0].System);
		await _engine.ReplyAsync(m, "你好", Today);
		Assert.Contains("【村民之间的事】", _ai.Requests[1].System);
		Assert.Contains("成年人之间牵扯到感情", _ai.Requests[1].System);
		await _engine.ReplyAsync(Drama(false), "再说一句", Today);
		Assert.Contains("【这个存档允许闹点戏剧性】", _ai.Requests[2].System);
		Assert.DoesNotContain("成年人之间牵扯到感情", _ai.Requests[2].System);
	}

	[Fact]
	public async Task ReplyAsksAgainWhenOnlyReactionCameBack()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"好呀\"}").Reply("{\"says\":\"好呀！\",\"warmth\":30,\"feeling\":\"happy\"}");
		await _engine.OpeningAsync(M(), Today);
		ReplyResult? r = await _engine.ReplyAsync(M(), "一起去吗", Today);
		Assert.Equal(30, r!.Reaction.FriendshipPoints);
		Assert.Equal(3, _ai.Requests.Count);
		Assert.Equal(DialogueEngine.ReplyRequest, _ai.Requests[1].Name);
		Assert.Equal(4096, _ai.Requests[1].MaxOutputTokens);
		Assert.Contains("一起去吗", _ai.Requests[1].User);
		Assert.Contains("开场", _ai.Requests[1].User);
		Assert.Contains("只有 \"says\"", _ai.Requests[2].User);
	}

	[Fact]
	public async Task ReplyKeepsFirstAnswerWhenSecondIsAlsoIncomplete()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"好呀\"}").Reply("{\"says\":\"还是好呀\"}");
		await _engine.OpeningAsync(M(), Today);
		ReplyResult? r = await _engine.ReplyAsync(M(), "一起去吗", Today);
		Assert.Equal("好呀", r!.Reaction.Reaction);
	}

	[Fact]
	public async Task CommitReplyRecordsTheTurnAndMarksTalked()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"真的？\",\"warmth\":15,\"feeling\":\"unique\"}");
		await _engine.OpeningAsync(M(), Today);
		ReplyResult? r = await _engine.ReplyAsync(M(), "我挖到钻石了", Today);
		ConversationEntry e = _engine.CommitReply(M(), "我挖到钻石了", r!, Today);
		Assert.Equal("开场", e.Opener);
		Assert.Equal("真的？", e.Comeback);
		Assert.Equal(15, e.HeartPoints);
		Assert.True(_log.TalkedToday("Abigail", Today));
		Assert.True(_engine.State("Abigail")!.SpokeToday);
	}

	[Fact]
	public async Task SkipRecordsMarkerWithoutPointsAndWithoutSavedTalkedFlag()
	{
		Talked("Abigail");
		_ai.Reply("开场");
		await _engine.OpeningAsync(M(), Today);
		ConversationEntry? e = _engine.CommitSkip("Abigail", "小麦", Today);
		Assert.Equal(SkippedReply.Marker, e!.FarmerReply);
		Assert.Equal(0, e.HeartPoints);
		Assert.False(_log.TalkedToday("Abigail", Today));
		Assert.True(_engine.State("Abigail")!.SpokeToday);
	}

	[Fact]
	public async Task ReplyUsesRawHeartsAndItsOwnProfileBlock()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"嗯\",\"warmth\":5,\"feeling\":\"neutral\"}");
		DialogueMaterial m = new DialogueMaterial { NpcName = "Abigail", PlayerName = "小麦", Hearts = 3, RawHearts = 8, EnmityHearts = 5, Personality = "测试", PlayerProfile = "有效心数的资料", ReplyPlayerProfile = "原始心数的资料" };
		await _engine.OpeningAsync(m, Today);
		Assert.Contains("有效心数的资料", _ai.Requests[0].System);
		await _engine.ReplyAsync(m, "你好", Today);
		string system = _ai.Requests[1].System;
		Assert.Contains("好感 8 心", system);
		Assert.Contains("原始心数的资料", system);
		Assert.DoesNotContain("有效心数的资料", system);
		Assert.Contains("记恨对方 5 心", system);
	}

	[Fact]
	public async Task OnlyNpcReplyStillCountsAsAnAnswer()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"\",\"warmth\":10,\"feeling\":\"happy\",\"on_request\":{\"agrees\":true,\"adds\":\"走吧。\"}}");
		await _engine.OpeningAsync(M(), Today);
		ReplyResult? r = await _engine.ReplyAsync(M(), "去海边吗", Today, proposedAction: "去海边");
		Assert.Equal("走吧。", r!.DisplayText);
	}

	[Fact]
	public async Task OpeningWorldBlocksKeepOriginalOrder()
	{
		Talked("Abigail");
		_ai.Reply("嗨");
		await _engine.OpeningAsync(new DialogueMaterial { NpcName = "Abigail", PlayerName = "小麦", Hearts = 5, RawHearts = 5, Personality = "测试", WorldContext = "块世界", ModContext = "块来历", LocationContext = "块地点", Events = "块事件", Milestones = "块里程碑", NpcInteractions = "块互动", Promises = "块承诺", Favors = "块拜托", ItemRequest = "块物品", Arc = "块故事", SocialDrama = "块剧情", VillageState = "块村子", TodayInteractions = "块今日" }, Today);
		string system = _ai.Requests[0].System;
		int[] at = new[] { "块世界", "块来历", "块地点", "块事件", "块里程碑", "块互动", "块承诺", "块拜托", "块物品", "块故事", "块剧情", "块村子", "块今日" }.Select(t => system.IndexOf(t, StringComparison.Ordinal)).ToArray();
		Assert.DoesNotContain(-1, at);
		Assert.Equal(at.OrderBy(i => i), at);
	}

	[Fact]
	public async Task ReplyMoodSlotKeepsOriginalOrder()
	{
		Talked("Abigail");
		_ai.Reply("开场").Reply("{\"says\":\"嗯\",\"warmth\":5,\"feeling\":\"neutral\"}");
		DialogueMaterial m = new DialogueMaterial { NpcName = "Abigail", PlayerName = "小麦", Hearts = 5, RawHearts = 5, Personality = "测试", VillageState = "村子", Mood = "心情", Favors = "拜托", ItemRequest = "物品", Arc = "故事", SocialDrama = "剧情", GossipMemory = "记忆", TodayInteractions = "今日" };
		await _engine.OpeningAsync(m, Today);
		await _engine.ReplyAsync(m, "你好", Today);
		Assert.Contains("村子心情拜托物品故事剧情", _ai.Requests[1].System);
		Assert.Contains("记忆今日", _ai.Requests[1].System);
	}

	[Fact]
	public async Task ReplyWithoutOpeningReturnsNull()
	{
		Assert.Null(await _engine.ReplyAsync(M(), "你好", Today));
		Assert.Empty(_ai.Requests);
	}

	[Fact]
	public async Task FailedOpeningLeavesNoState()
	{
		Talked("Abigail");
		_ai.Fail(FailureKind.Network);
		Assert.Null(await _engine.OpeningAsync(M(), Today));
		Assert.Null(_engine.State("Abigail"));
		Assert.False(_engine.IsGenerating);
	}

	[Fact]
	public async Task DiscardUnshownOpeningAllowsRegeneration()
	{
		Talked("Abigail");
		_ai.Reply("第一句").Reply("第二句");
		await _engine.OpeningAsync(M(), Today);
		Assert.True(_engine.DiscardUnshownOpening("Abigail", "第一句"));
		Assert.Equal("第二句", (await _engine.OpeningAsync(M(), Today))!.Dialogue);
	}

	[Fact]
	public async Task SpousePendingTurnCanResume()
	{
		Talked("Sebastian");
		_ai.Reply("开场");
		await _engine.OpeningAsync(M("Sebastian", spouse: true), Today);
		TodayDialogue s = _engine.State("Sebastian")!;
		s.SpokeToday = true;
		s.PreservePendingSpouseResponse("刚才说到哪了", 2, "happy");
		Assert.True(_engine.CanResume("Sebastian", true));
		_engine.CompleteDaily("Sebastian", Today);
		Assert.False(_engine.CanResume("Sebastian", true));
	}
}

public class OpeningTopicTests
{
	private static ConversationEntry Said(string line) => new ConversationEntry { Opener = line };

	private static int WeightOf(OpeningTopicInput input, OpeningTopic topic) => OpeningTopicPicker.Weights(input).Where(w => w.Topic == topic).Select(w => w.Weight).DefaultIfEmpty(0).Sum();

	[Fact]
	public void BaseWeightsMatchOriginal()
	{
		OpeningTopicInput input = new OpeningTopicInput();
		Assert.Equal(10, WeightOf(input, OpeningTopic.General));
		Assert.Equal(12, WeightOf(input, OpeningTopic.Personal));
		Assert.Equal(6, WeightOf(input, OpeningTopic.NoQuestion));
		Assert.Equal(4, WeightOf(input, OpeningTopic.Memory));
		Assert.Equal(10, WeightOf(input, OpeningTopic.Routine));
		Assert.Equal(0, WeightOf(input, OpeningTopic.Beach));
	}

	[Fact]
	public void ConditionalWeights()
	{
		OpeningTopicInput input = new OpeningTopicInput
		{
			Location = "beach",
			LowMood = true,
			HighMood = true,
			HasGossip = true,
			HasGift = true,
			HasStory = true,
			IsAdultRomanticPartner = true,
			RecentHistory = new[] { Said("你好？"), Said("在吗"), Said("吃了吗？"), Said("嗯") }
		};
		Assert.Equal(28, WeightOf(input, OpeningTopic.NoQuestion));
		Assert.Equal(14, WeightOf(input, OpeningTopic.Memory));
		Assert.Equal(30, WeightOf(input, OpeningTopic.Beach));
		Assert.Equal(30, WeightOf(input, OpeningTopic.LowMood));
		Assert.Equal(24, WeightOf(input, OpeningTopic.HighMood));
		Assert.Equal(28, WeightOf(input, OpeningTopic.Gossip));
		Assert.Equal(24, WeightOf(input, OpeningTopic.Gift));
		Assert.Equal(26, WeightOf(input, OpeningTopic.Story));
		Assert.Equal(30, WeightOf(input, OpeningTopic.Romance));
	}

	[Fact]
	public void ItemRequestOnlyOnFirstLineWhenAllowed()
	{
		OpeningTopicPicker always = new OpeningTopicPicker(new Random(3));
		Assert.Equal(OpeningTopic.ItemRequest, always.Pick(new OpeningTopicInput { CanRequestItem = true, IsOpeningLine = true, ItemRequestChancePercent = 100 }));
		Assert.NotEqual(OpeningTopic.ItemRequest, always.Pick(new OpeningTopicInput { CanRequestItem = true, IsOpeningLine = false, ItemRequestChancePercent = 100 }));
		Assert.NotEqual(OpeningTopic.ItemRequest, always.Pick(new OpeningTopicInput { CanRequestItem = false, IsOpeningLine = true, ItemRequestChancePercent = 100 }));
	}

	[Fact]
	public void EveryTopicExceptGeneralHasAFocusFile()
	{
		foreach (OpeningTopic t in Enum.GetValues<OpeningTopic>())
		{
			string? file = OpeningTopicPicker.FocusTemplate(t);
			if (t == OpeningTopic.General)
			{
				Assert.Null(file);
			}
			else
			{
				Assert.True(File.Exists(Path.Combine(DialogueEngineTests.PromptDir(), file + ".txt")), file);
			}
		}
	}
}

public class ReactionRuleTests
{
	[Theory]
	[InlineData("love", 10, 8, true, false, false, 20)]
	[InlineData("love", 10, 7, false, false, false, 20)]
	[InlineData("happy", 80, 2, true, false, false, 20)]
	[InlineData("happy", 79, 2, false, false, false, 20)]
	[InlineData("angry", -20, 3, false, true, true, 12)]
	[InlineData("angry", -20, 4, false, false, false, 12)]
	[InlineData("sad", 39, 5, false, true, false, 28)]
	[InlineData("sad", 40, 5, false, false, false, 28)]
	[InlineData("unique", 50, 5, false, true, false, 16)]
	public void EmotePlans(string emotion, int points, int hearts, bool jump, bool halt, bool turn, int emote)
	{
		Assert.Equal(new EmotePlan(jump, halt, turn, emote), ReactionEffects.Emote(emotion, points, hearts));
	}

	[Fact]
	public void NeutralDoesNothing() => Assert.Null(ReactionEffects.Emote("neutral", 50, 5));

	[Theory]
	[InlineData("happy", 1, 5)]
	[InlineData("happy", 10, 20)]
	[InlineData("angry", -20, -40)]
	[InlineData("angry", 10, -5)]
	[InlineData("sad", 10, -10)]
	[InlineData("sad", 1, -3)]
	[InlineData("neutral", 30, 0)]
	[InlineData("love", 30, 30)]
	public void MoodChanges(string emotion, int points, int expected) => Assert.Equal(expected, ReactionEffects.MoodChange(emotion, points));

	[Fact]
	public void FriendshipCaps()
	{
		Assert.Equal(3749, ReactionEffects.MaxFriendshipPoints(true, true, true));
		Assert.Equal(2749, ReactionEffects.MaxFriendshipPoints(true, true, false));
		Assert.Equal(2249, ReactionEffects.MaxFriendshipPoints(true, false, false));
		Assert.Equal(2749, ReactionEffects.MaxFriendshipPoints(false, false, false));
		Assert.Equal(0, ReactionEffects.ApplyFriendshipChange(10, -50, 2249));
		Assert.Equal(2249, ReactionEffects.ApplyFriendshipChange(2240, 50, 2249));
	}

	[Fact]
	public void ReactionAndActionReplyMergeWithoutRepeating()
	{
		NpcReaction r = new NpcReaction { Reaction = "好啊。", ActionResponse = new ActionResponse { Accepted = true, NpcReply = "好啊，下午两点见。" } };
		Assert.Equal("好啊，下午两点见。", ReactionText.ForDisplay(r));
		r.ActionResponse.NpcReply = "";
		r.ActionResponse.Reason = "Accepted because she likes the farmer";
		Assert.Equal("好啊。", ReactionText.ForHistory(r));
		r.ActionResponse.Reason = "正好我也想去";
		Assert.Equal("好啊。\n正好我也想去", ReactionText.ForHistory(r));
	}
}

public class DialoguePromptTests
{
	[Fact]
	public void CanonReferenceRespectsSettingAndStripsCommands()
	{
		Assert.Equal("", DialoguePrompts.CanonOpening("你好$h", enabled: false));
		string block = DialoguePrompts.CanonOpening("你好$h\n再见", enabled: true);
		Assert.Contains("“你好  再见”", block);
		Assert.DoesNotContain("$h", block);
		Assert.Equal("", DialoguePrompts.CanonFollowup("  ", enabled: true));
	}

	[Fact]
	public void IsolatedVillagersGetNoGossip()
	{
		Assert.Equal("", DialoguePrompts.Gossip("Linus", "听说……"));
		Assert.Equal("听说……", DialoguePrompts.Gossip("Haley", "听说……"));
	}

	[Fact]
	public void UserTurnMentionsShortReplyOnlyWhenAsked()
	{
		Assert.DoesNotContain("不超过", DialoguePrompts.ReplyUserTurn("Abigail", "小麦", "嗨", "[x]"));
		Assert.Contains("不超过 80 字", DialoguePrompts.ReplyUserTurn("Abigail", "小麦", "嗨", "[x]", 80));
	}
}

public class GameTextTests
{
	[Fact]
	public void DisplayKeepsPageBreaksAndNeutralizesCommands()
	{
		Assert.Equal("你好小麦，真的？#$b#嗯", GameDialogueText.ForDisplay("你好@，真的？$h#$b#嗯", "小麦"));
		Assert.Equal("100％好＾的＃", GameDialogueText.ForDisplay("100%好^的#", "小麦"));
	}

	[Fact]
	public void VanillaHintRemovesCommandsAndFillsName()
	{
		Assert.Equal("早啊小麦 今天不错", GameDialogueText.VanillaHint("早啊@$h#$e#今天不错", "小麦"));
		Assert.Equal(260, GameDialogueText.VanillaHint(new string('啊', 400), "x").Length);
	}

	[Fact]
	public void PierreHasHisOwnAngryPortrait()
	{
		Assert.Equal("Pierre_Annoyed", Emotion.PortraitOverride("Pierre", "angry"));
		Assert.Null(Emotion.PortraitOverride("Pierre", "happy"));
		Assert.Null(Emotion.PortraitOverride("Abigail", "angry"));
	}


	[Theory]
	[InlineData("Pierre", "angry", 6, "", 0)]
	[InlineData("Abigail", "angry", 6, "$a", 5)]
	[InlineData("Abigail", "angry", 4, "$u", 3)]
	[InlineData("Abigail", "angry", 3, "$s", 2)]
	[InlineData("Abigail", "angry", 2, "", 0)]
	[InlineData("Jodi", "angry", 6, "$u", 3)]
	[InlineData("Pierre", "happy", 6, "$h", 1)]
	public void PortraitFallbacks(string npc, string emotion, int tiles, string command, int index)
	{
		Assert.Equal(command, Emotion.PortraitCommandFor(npc, emotion, tiles));
		Assert.Equal(index, Emotion.PortraitIndex(command));
	}

	[Theory]
	[InlineData(2, 0, "点头之交")]
	[InlineData(3, 0, "开玩笑")]
	[InlineData(8, 0, "当朋友")]
	[InlineData(10, 0, "无话不谈")]
	[InlineData(11, 0, "老交情")]
	[InlineData(5, 2, "不顺眼")]
	[InlineData(5, 5, "记着仇")]
	[InlineData(5, 6, "绕着走")]
	public void FriendshipTiers(int hearts, int enmity, string expected) => Assert.Contains(expected, FriendshipTone.Describe(hearts, enmity));
}

public class WorldDataTests
{
	[Fact]
	public void FixedTablesAreComplete()
	{
		Assert.Equal(8, WorldData.Festivals.Count);
		Assert.Equal(33, WorldData.Birthdays.Count);
		Assert.Equal(9, WorldData.ShopCounters.Count);
	}

	[Fact]
	public void LocationBlockSaysWhoElseIsThere()
	{
		Assert.Contains("这里只有你们两个", LocationFacts.Build("Town", 1030, Array.Empty<string>()));
		Assert.Contains("旁边还有：Sam、Alex", LocationFacts.Build("Town", 1030, new[] { "Sam", "Alex" }));
		Assert.DoesNotContain("旁边", LocationFacts.Build("Town", 1030, null));
		Assert.Contains("10:30（上午）", LocationFacts.Build("Town", 1030, null));
	}
}

public class WorldFactsTests
{
	private static string Facts(WorldSnapshot w, string npc = "Abigail", int hearts = 0) => WorldFacts.Build(w, npc.Length == 0 ? null : new NpcWorldState { Name = npc, Hearts = hearts });

	[Theory]
	[InlineData(39, null)]
	[InlineData(40, "最深到过第 40 层")]
	[InlineData(80, "老练的矿工")]
	[InlineData(120, "最底层")]
	public void MineTiers(int level, string? expected)
	{
		string facts = Facts(new WorldSnapshot { DeepestMineLevel = level });
		if (expected == null)
		{
			Assert.DoesNotContain("矿井", facts);
		}
		else
		{
			Assert.Contains(expected, facts);
		}
	}

	[Fact]
	public void ColdMarriageIsSaid()
	{
		WorldSnapshot w = new WorldSnapshot { Spouse = "Haley" };
		Assert.Contains("婚姻已经凉了", Facts(w, "Haley", 2));
		Assert.DoesNotContain("凉了", Facts(w, "Haley", 10));
		Assert.DoesNotContain("凉了", Facts(w, "Abigail", 2));
	}

	[Fact]
	public void SpouseIsNotAStranger()
	{
		PlayerProfile profile = new PlayerProfile { Intro = "种地的" };
		Assert.Contains("还不熟", PlayerProfileBlock.Build("Haley", "牛哥", 0, profile));
		string spouse = PlayerProfileBlock.Build("Haley", "牛哥", 0, profile, spouse: true);
		Assert.DoesNotContain("还不熟", spouse);
		Assert.Contains("另一半", spouse);
		string ex = PlayerProfileBlock.Build("Haley", "牛哥", 0, profile, ex: true);
		Assert.DoesNotContain("还不熟", ex);
		Assert.Contains("前任", ex);
	}

	[Fact]
	public void ExWifeKnowsTheyDivorced()
	{
		WorldSnapshot w = new WorldSnapshot();
		Assert.Contains("离了", WorldFacts.Build(w, new NpcWorldState { Name = "Haley", Divorced = true }));
		Assert.DoesNotContain("离了", WorldFacts.Build(w, new NpcWorldState { Name = "Haley" }));
	}

	[Theory]
	[InlineData("relationship_proposal.txt")]
	[InlineData("invite_out_proposal.txt")]
	public void ProposalRepliesAreSpokenOnly(string file)
	{
		string says = File.ReadAllLines(Path.Combine(DialogueEngineTests.PromptDir(), file)).Single(l => l.TrimStart().StartsWith("\"says\":"));
		Assert.Contains("不带旁白、动作和神态", says);
	}

	[Fact]
	public void FestivalFactKnowsWhenItIsOver()
	{
		Assert.Contains("今天过节：花舞节", Facts(new WorldSnapshot { Season = "spring", Day = 24, TimeOfDay = 1000 }));
		Assert.Contains("今天的花舞节已经办完了", Facts(new WorldSnapshot { Season = "spring", Day = 24, TimeOfDay = 2200 }));
		Assert.Contains("今晚过节：月光水母起舞（22 点开始）", Facts(new WorldSnapshot { Season = "summer", Day = 28, TimeOfDay = 1500 }));
		Assert.Contains("今天过节：月光水母起舞", Facts(new WorldSnapshot { Season = "summer", Day = 28, TimeOfDay = 2230 }));
	}

	[Fact]
	public void FestivalTomorrowRollsOverTheSeason()
	{
		Assert.Contains("明天过节：冰雪节", Facts(new WorldSnapshot { Season = "winter", Day = 7 }));
		Assert.Contains("今天过节：复活节（彩蛋节）", Facts(new WorldSnapshot { Season = "spring", Day = 13 }));
		Assert.DoesNotContain("明天过节", Facts(new WorldSnapshot { Season = "fall", Day = 28 }));
	}

	[Fact]
	public void KentsBirthdayOnlyAfterHeReturns()
	{
		Assert.DoesNotContain("生日", Facts(new WorldSnapshot { Season = "spring", Day = 4, Year = 1 }));
		Assert.Contains("今天是Kent的生日", Facts(new WorldSnapshot { Season = "spring", Day = 4, Year = 2 }));
		Assert.Contains("和你很亲", Facts(new WorldSnapshot { Season = "spring", Day = 4, Year = 2 }, "Sam"));
		Assert.Contains("你自己的生日", Facts(new WorldSnapshot { Season = "fall", Day = 13 }));
	}

	[Fact]
	public void KentNotesByYear()
	{
		Assert.Contains("还在打仗", Facts(new WorldSnapshot { Year = 1 }, "Jodi"));
		Assert.DoesNotContain("肯特", Facts(new WorldSnapshot { Year = 1 }, "Kent"));
		Assert.Contains("刚从战场回来", Facts(new WorldSnapshot { Year = 2 }, "Kent"));
		Assert.DoesNotContain("肯特", Facts(new WorldSnapshot { Year = 2 }, "Abigail"));
	}

	[Fact]
	public void HeartEventMemoryNeedsFourHearts()
	{
		WorldSnapshot w = new WorldSnapshot { EventsSeen = new HashSet<int> { 1, 2 } };
		Assert.DoesNotContain("心事件", Facts(w, hearts: 3));
		Assert.Contains("2♥、4♥", Facts(w, hearts: 4));
		Assert.Contains("偶尔", Facts(w, hearts: 7));
		Assert.Contains("很自然地提起", Facts(w, hearts: 8));
		Assert.DoesNotContain("心事件", WorldFacts.Build(w, new NpcWorldState { Name = "Abigail", Hearts = 8 }, isReplacedHeartEvent: id => true));
	}

	[Fact]
	public void SpouseAndChildren()
	{
		WorldSnapshot w = new WorldSnapshot { Spouse = "Abigail", Children = new[] { new ChildInfo("小葵", 0, false) } };
		string own = Facts(w);
		Assert.Contains("你的另一半", own);
		Assert.Contains("你和农夫有个孩子叫小葵", own);
		Assert.Contains("和你很亲", Facts(w, "Pierre"));
		Assert.DoesNotContain("结婚", WorldFacts.Build(w, new NpcWorldState { Name = "Haley" }, isNpcEnabled: n => n != "Abigail"));
	}

	[Theory]
	[InlineData("SeedShop", 900, false, true)]
	[InlineData("SeedShop", 1700, false, false)]
	[InlineData("SeedShop", 1000, true, false)]
	[InlineData("Saloon", 1000, false, false)]
	public void ShopCounterHours(string location, int time, bool moving, bool expected)
	{
		Assert.Equal(expected, WorldFacts.ShopCounter("Pierre", location, time, moving).Length > 0);
	}

	[Fact]
	public void ActivityPrefersSleepThenRouteBehavior()
	{
		Assert.Contains("睡觉", WorldFacts.Activity("Pierre", new NpcActivitySnapshot(true, true, "pierre_read", "SeedShop", false), 1000));
		Assert.Contains("看书", WorldFacts.Activity("Pierre", new NpcActivitySnapshot(false, true, "pierre_read", "SeedShop", false), 1000));
		Assert.Contains("柜台", WorldFacts.Activity("Pierre", new NpcActivitySnapshot(false, true, "pierre_unknown", "SeedShop", false), 1000));
	}

	[Fact]
	public void SecretOnlyWhenCloseEnough()
	{
		Assert.DoesNotContain("别人不知道的事", Facts(new WorldSnapshot(), "Kent", 7));
		Assert.Contains("别人不知道的事", Facts(new WorldSnapshot(), "Kent", 8));
	}

	[Fact]
	public void DayOfWeekAlwaysPresent()
	{
		Assert.Contains("今天星期一", Facts(new WorldSnapshot { Day = 1 }, ""));
		Assert.Contains("今天星期日", Facts(new WorldSnapshot { Day = 28 }, ""));
	}
}

public class LenientReactionFieldTests
{
	[Fact]
	public void PlainProseBecomesTheReaction()
	{
		NpcReaction? r = ReactionParser.Parse("哦——委托板的事啊。那倒是像她。", 300, allowNegative: false, NullLog.Instance);
		Assert.Equal("哦——委托板的事啊。那倒是像她。", r!.Reaction);
		Assert.Equal(30, r.FriendshipPoints);
		Assert.Equal("neutral", r.Emotion);
		Assert.Null(ReactionParser.Parse("{\"feeling\": \"happy\"", 300, allowNegative: false, NullLog.Instance));
	}
}

public class RecentLinesTests
{
	[Fact]
	public void OpeningBlockKeepsLastThreeCutAt60()
	{
		RecentLines r = new RecentLines();
		foreach (string s in new[] { "第一句", "第二句", "第三句", new string('长', 70) })
		{
			r.RecordOpening("Abigail", s);
		}
		string block = r.OpeningBlock("Abigail");
		Assert.DoesNotContain("第一句", block);
		Assert.Contains("第二句", block);
		Assert.Contains(new string('长', 60) + "…", block);
		Assert.DoesNotContain(new string('长', 61), block);
		Assert.Equal("", r.OpeningBlock("Sam"));
	}

	[Fact]
	public void ReactionBlockCutsAt80()
	{
		RecentLines r = new RecentLines();
		r.RecordReaction("Sam", new string('话', 90));
		string block = r.ReactionBlock("Sam");
		Assert.Contains(new string('话', 80) + "…", block);
		Assert.DoesNotContain(new string('话', 81), block);
	}

	[Fact]
	public void NewOpeningStartsAFreshReactionList()
	{
		RecentLines r = new RecentLines();
		r.RecordOpening("Haley", "又是你。");
		r.RecordReaction("Haley", "三点就三点，我记下了。");
		r.RecordOpening("Haley", "今天沙子烫脚。");
		Assert.Equal("", r.ReactionBlock("Haley"));
		Assert.Contains("又是你", r.OpeningBlock("Haley"));
	}

	[Fact]
	public void TopicsDedupeAndReactionsCountToo()
	{
		RecentLines r = new RecentLines();
		r.RecordOpening("Sam", "今天又下雨，还打雷");
		r.RecordReaction("Sam", "飘雪的时候也差不多");
		r.RecordReaction("Sam", "据说有条小道消息");
		string block = r.OpeningBlock("Sam");
		string topics = block.Substring(block.IndexOf("话题类别", StringComparison.Ordinal));
		Assert.Single(System.Text.RegularExpressions.Regex.Matches(topics, "天色"));
		Assert.Contains("闲话", topics);
	}

	[Fact]
	public void ClearForgetsEverything()
	{
		RecentLines r = new RecentLines();
		r.RecordOpening("Sam", "你好");
		r.Clear();
		Assert.Equal("", r.OpeningBlock("Sam"));
	}
}

public class HistoryFormatterTests
{
	private static readonly GameDate Today = new GameDate(13, "spring", 1);

	private static ConversationEntry E(int day, string npc = "你的话", string player = "农夫的话", string reaction = "你的反应", string who = "小麦") =>
		new ConversationEntry { Day = day, Season = "spring", Year = 1, Opener = npc, FarmerReply = player, Comeback = reaction, Farmer = who };

	private static string Format(int maxTurns, params ConversationEntry[] entries) => HistoryFormatter.Format("Abigail", entries, 10, maxTurns, Today, "小麦");

	[Fact]
	public void EmptyHistorySaysSo() => Assert.Contains("没聊过", Format(3));

	[Fact]
	public void OngoingLastReactionIsLeftOut()
	{
		string text = Format(3, E(13, reaction: "反应一"), E(13, reaction: "反应二"));
		Assert.Contains("[今天·这场对话]", text);
		Assert.Contains("反应一", text);
		Assert.DoesNotContain("反应二", text);
	}

	[Fact]
	public void FullTurnsBecomeFinished()
	{
		string text = Format(2, E(13, reaction: "反应一"), E(13, reaction: "反应二"));
		Assert.DoesNotContain("[今天·这场对话]", text);
		Assert.Contains("今天早些时候·已经聊完", text);
		Assert.Contains("反应二", text);
	}

	[Fact]
	public void OlderThanFourIsOneLine()
	{
		string text = Format(3, E(1, npc: "最旧开场"), E(2), E(3), E(4), E(5, npc: "较新开场"));
		Assert.DoesNotContain("最旧开场", text);
		Assert.Contains("较新开场", text);
		Assert.Contains("春 1 日", text);
	}

	[Fact]
	public void OtherFarmerAndSkipAreMarked()
	{
		string text = Format(3, E(5, who: "小豆"), E(6, player: SkippedReply.Marker));
		Assert.Contains("另一位农夫：小豆", text);
		Assert.Contains("没有接话", text);
		Assert.DoesNotContain(SkippedReply.Marker, text);
	}

	[Theory]
	[InlineData(40, 3, false, 40, 10)]
	[InlineData(40, 20, false, 40, 22)]
	[InlineData(40, 3, true, 40, 40)]
	[InlineData(5, 20, true, 40, 7)]
	[InlineData(100, 3, true, 40, 40)]
	public void WindowFollowsTheRules(int memory, int turns, bool server, int cap, int expected) => Assert.Equal(expected, HistoryFormatter.Window(memory, turns, server, cap));
}

public class PromptFillTests
{
	private readonly ModConfig _config = new ModConfig().Normalize();

	private readonly FakeSender _ai = new FakeSender();

	private readonly DialogueEngine _engine;

	private readonly ConversationLog _log;

	private readonly PromptTemplates _templates = new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance);

	private static readonly GameDate Today = new GameDate(13, "spring", 1);

	public PromptFillTests()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		_log = new ConversationLog(() => store, () => 100);
		_engine = new DialogueEngine(() => _config, _ai, _templates, _log, new RecentLines(), NullLog.Instance, () => "简体中文", new OpeningTopicPicker(new Random(1)));
	}

	private static DialogueMaterial M(int hearts, string? vanilla = null, string? action = null) => new DialogueMaterial
	{
		NpcName = "Abigail",
		DisplayName = "阿比盖尔",
		PlayerName = "小麦",
		Hearts = hearts,
		RawHearts = hearts,
		Season = "spring",
		Day = 13,
		Year = 1,
		Personality = "测试人设",
		VanillaLine = vanilla ?? "",
		EventsSeen = new HashSet<int>()
	};

	private void Talked() => _log.Add("Abigail", new ConversationEntry { Day = 10, Season = "spring", Year = 1, Farmer = "小麦", Opener = "以前的话", FarmerReply = "嗯", Comeback = "好" });

	private static void AssertFilled(ChatRequest r)
	{
		Assert.DoesNotContain("{{", r.System);
		Assert.DoesNotContain("}}", r.System);
		Assert.Contains("阿比盖尔", r.System);
		Assert.DoesNotContain("Abigail", r.System + r.User);
	}

	[Fact]
	public async Task FirstMeetingAndOpeningAreFullyFilled()
	{
		_ai.Reply("你好。");
		await _engine.OpeningAsync(M(1, vanilla: "原作的话"), Today);
		AssertFilled(_ai.Requests[0]);
		Assert.Contains("原作的话", _ai.Requests[0].System);

		_engine.StartNewDay();
		Talked();
		_ai.Reply("又见面了。");
		await _engine.OpeningAsync(M(4, vanilla: "原作的话"), Today);
		AssertFilled(_ai.Requests[1]);
		Assert.Contains("原作的话", _ai.Requests[1].System);
	}

	[Fact]
	public async Task CanonBlockFollowsTheSetting()
	{
		Talked();
		_ai.Reply("又见面了。");
		await _engine.OpeningAsync(M(4), Today);
		Assert.DoesNotContain("剧本素材", _ai.Requests[0].System);
	}

	[Fact]
	public void ScoringTablesKeepTheirBands()
	{
		string enmity = DialoguePrompts.Scoring(_templates, enmity: true, enmityHearts: 3);
		foreach (string band in new[] { "-100～-50", "-49～-15", "5～30", "30～60", "60～100" })
		{
			Assert.Contains(band, enmity);
		}
		Assert.Contains("3 心", enmity);
		string standard = DialoguePrompts.Scoring(_templates, enmity: false, enmityHearts: 0);
		foreach (string band in new[] { "0～10", "10～30", "30～60", "60～100" })
		{
			Assert.Contains(band, standard);
		}
		Assert.DoesNotContain("-100", standard);
	}

	[Fact]
	public void ProposalBlockOnlyWithAProposal()
	{
		Assert.Equal("", DialoguePrompts.ProposedAction(null, "小麦"));
		Assert.Equal("", DialoguePrompts.ProposedActionJson("", "中文"));
		Assert.Contains("去海边", DialoguePrompts.ProposedAction("去海边", "小麦"));
		string json = DialoguePrompts.ProposedActionJson("去海边", "中文");
		foreach (string field in new[] { "agrees", "adds", "private_note", "what_happens" })
		{
			Assert.Contains(field, json);
		}
	}
}

public class DramaToneTests
{
	[Fact]
	public void OnlyOnDramaticAndAdultLineOnlyWhenAsked()
	{
		ModConfig off = new ModConfig { DramaOn = true, DramaHeat = "Cozy" };
		Assert.Equal("", DialoguePrompts.DramaTone(off));
		ModConfig on = new ModConfig { DramaOn = true, DramaHeat = "Dramatic" };
		Assert.DoesNotContain("成年人", DialoguePrompts.DramaTone(on));
		Assert.Contains("成年人", DialoguePrompts.DramaTone(on, adultRelationshipDrama: true));
	}
}

public class WorldFactNameTests
{
	private static string Build(WorldSnapshot w) => WorldFacts.Build(w, new NpcWorldState { Name = "Sam" }, display: n => n == "Abigail" ? "阿比盖尔" : n == "Haley" ? "海莉" : n);

	[Fact]
	public void SpouseIsWrittenWithDisplayName()
	{
		string text = Build(new WorldSnapshot { Day = 1, Season = "spring", Year = 2, Spouse = "Abigail" });
		Assert.Contains("阿比盖尔", text);
		Assert.DoesNotContain("Abigail", text);
	}

	[Fact]
	public void BirthdayIsWrittenWithDisplayName()
	{
		string text = Build(new WorldSnapshot { Day = 14, Season = "spring", Year = 2 });
		Assert.Contains("海莉", text);
		Assert.DoesNotContain("Haley", text);
	}

	[Fact]
	public void ForDisplayKeepsMoneyAndBothPageBreaks()
	{
		Assert.Equal("给你＄100", GameDialogueText.ForDisplay("给你$100", "小麦"));
		Assert.Equal("好的", GameDialogueText.ForDisplay("好的$h", "小麦"));
		Assert.Equal("一#$b#二", GameDialogueText.ForDisplay("一#$e#二", "小麦"));
	}
}
