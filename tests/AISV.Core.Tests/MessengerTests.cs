using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Core.Prompts;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class MessengerRulesTests
{
	private static TextEntry Msg(int day, string said, string reply, string player = "小芳", string type = MessengerRules.ContextType, bool remote = false) =>
		new TextEntry(day, "spring", 1, type, player, remote, said, reply);

	[Fact]
	public void CheckOrder()
	{
		Assert.Equal(SendBlock.NoContact, MessengerRules.Check(false, "", 0, true, false));
		Assert.Equal(SendBlock.AiOff, MessengerRules.Check(true, "", 0, true, false));
		Assert.Equal(SendBlock.Empty, MessengerRules.Check(true, "   ", 0, true, true));
		Assert.Equal(SendBlock.TooLong, MessengerRules.Check(true, new string('啊', 201), 5, false, true));
		Assert.Equal(SendBlock.Busy, MessengerRules.Check(true, "在吗", 0, true, true));
		Assert.Equal(SendBlock.DailyLimitReached, MessengerRules.Check(true, "在吗", 0, false, true));
		Assert.Equal(SendBlock.None, MessengerRules.Check(true, new string('啊', 200), 1, false, true));
	}

	[Fact]
	public void CountsOnlyOwnMessagesSentToday()
	{
		List<TextEntry> entries = new List<TextEntry>
		{
			Msg(3, "早", "早呀"),
			Msg(3, "", "主动发的"),
			Msg(3, "别人的", "嗯", player: "阿强"),
			Msg(3, "当面说的", "嗯", type: "daily"),
			Msg(2, "昨天的", "嗯"),
			Msg(3, "老记录", "嗯", player: "")
		};
		Assert.Equal(2, MessengerRules.SentToday(entries, "小芳", new GameDate(3, "spring", 1)));
		Assert.Equal(2, MessengerRules.Remaining(5, 2, pending: true));
		Assert.Equal(0, MessengerRules.Remaining(1, 3, pending: false));
		Assert.Equal(15, MessengerRules.ClampDailyLimit(99));
		Assert.False(MessengerRules.IsOwn(Msg(3, "a", "b", player: "", remote: true), "小芳"));
	}

	[Fact]
	public void NoPhoneVillagers()
	{
		Assert.False(MessengerRules.HasPhone("Krobus"));
		Assert.False(MessengerRules.HasPhone("jas"));
		Assert.True(MessengerRules.HasPhone("Abigail"));
	}

	[Fact]
	public void TranscriptAddsDatesAndPhotos()
	{
		List<TextEntry> entries = new List<TextEntry> { Msg(2, "你好", "你好呀"), Msg(3, "给我看看你", "拍好了"), Msg(3, "", "在忙吗？") };
		PhotoRecord photo = new PhotoRecord("p1", "Abigail", "Abigail", "Town", "鹈鹕镇", 3, "spring", 1, 1300, "给我看看你");
		IReadOnlyList<ChatItem> items = MessengerRules.Transcript(entries, "小芳", new[] { photo });
		Assert.Equal(new[] { ChatItemKind.Date, ChatItemKind.Player, ChatItemKind.Npc, ChatItemKind.Date, ChatItemKind.Player, ChatItemKind.Npc, ChatItemKind.Photo, ChatItemKind.Npc }, items.Select(i => i.Kind));
		Assert.Equal("p1", items[6].Text);
	}

	[Fact]
	public void TypingAndReveal()
	{
		Assert.Equal(TimeSpan.FromMilliseconds(500), MessengerRules.TypingDelay(0));
		Assert.Equal(TimeSpan.FromMilliseconds(1500), MessengerRules.TypingDelay(50));
		Assert.Equal(TimeSpan.FromMilliseconds(3500), MessengerRules.TypingDelay(1000));
		Assert.Equal(1, MessengerRules.RevealedChars(0, 30));
		Assert.Equal(30, MessengerRules.RevealedChars(10000, 30));
	}

	[Fact]
	public void HeldReplyIsHidden()
	{
		List<TextEntry> entries = new List<TextEntry> { Msg(3, "在吗", "在"), Msg(3, "在吗", "又在") };
		IReadOnlyList<TextEntry> shown = MessengerRules.WithoutHeld(entries, "小芳", "在吗");
		Assert.Single(shown);
		Assert.Equal("在", shown[0].NpcReply);
	}

	[Fact]
	public void TabsPutBirthdaysFirst()
	{
		List<int?> last = new List<int?> { 5, null, 9, null };
		List<bool> birthdays = new List<bool> { false, false, false, true };
		Assert.Equal(new[] { 3, 2, 0 }, MessengerRules.TabMembers(last, true, birthdays));
		Assert.Equal(new[] { 3, 0, 1, 2 }, MessengerRules.TabMembers(last, false, birthdays));
	}

	[Fact]
	public void CleanReplyDropsCodesKeepsLines()
	{
		Assert.Equal("你好\n今天天气不错", MessengerRules.CleanReply("你好$h#$b#今天天气不错"));
		Assert.Equal("（用信使发的）早", MessengerRules.DiaryMessage(" 早 "));
		Assert.Equal("凌晨1:10", MessengerRules.ClockText(2510));
		Assert.Equal("一二三四…", MessengerRules.Preview("一二三四五六", 5));
	}
}

public class PhotoRulesTests
{
	private static readonly (string, string)[] People = { ("Abigail", "阿比盖尔"), ("Sam", "山姆"), ("Sebastian", "塞巴斯蒂安") };

	private static PhotoRequest Plan(string message, Func<string, bool>? with = null, Func<string, bool>? today = null) =>
		PhotoRules.Plan(message, "Abigail", People, with ?? (_ => false), today ?? (_ => false));

	[Fact]
	public void AskingForAPhotoGetsASelfie()
	{
		Assert.Equal(PhotoKind.Selfie, Plan("发张照片给我看看").Kind);
		Assert.Equal(PhotoKind.Selfie, Plan("send me a selfie").Kind);
		Assert.Equal(PhotoKind.None, Plan("今天天气真好").Kind);
	}

	[Fact]
	public void NamedVillagerNearbyIsPhotographed()
	{
		PhotoRequest r = Plan("山姆在干嘛", with: n => n == "Sam");
		Assert.Equal(PhotoKind.Subject, r.Kind);
		Assert.Equal("Sam", r.Subject);
		Assert.Equal(PhotoKind.None, Plan("山姆在干嘛", with: n => n == "Sam", today: _ => true).Kind);
		Assert.Equal(PhotoKind.Subject, Plan("拍张山姆的照片", with: n => n == "Sam", today: _ => true).Kind);
	}

	[Fact]
	public void NamedVillagerAwayCannotBePhotographed()
	{
		Assert.Equal(PhotoKind.Absent, Plan("塞巴斯蒂安在哪").Kind);
		Assert.Equal(PhotoKind.None, Plan("塞巴斯蒂安人不错").Kind);
	}

	[Fact]
	public void LatinDisplayNamesNeedWholeWords()
	{
		(string, string)[] english = { ("Sam", "Sam"), ("Gus", "Gus") };
		Assert.Empty(PhotoRules.Mentioned("same old august", english, "Abigail"));
		Assert.Equal(new[] { "Sam" }, PhotoRules.Mentioned("is Sam there?", english, "Abigail"));
	}

	[Fact]
	public void FramesTellTheModelWhatHappened()
	{
		ReplyFrame? frame = PhotoRules.Frame(new PhotoRequest(PhotoKind.Absent, "Sam"), "山姆", "酒吧", "小芳");
		Assert.NotNull(frame);
		Assert.Contains("没有照片", frame!.Section);
		Assert.Null(PhotoRules.Frame(PhotoRequest.None, "", "", "小芳"));
	}
}

public class VillagerTextsTests
{
	[Fact]
	public void WeeklyQuotaIsSpreadOverRemainingDays()
	{
		Assert.Equal(0, VillagerTexts.PlanToday(3, 3, 4, 0.0));
		Assert.Equal(1, VillagerTexts.PlanToday(7, 0, 7, 0.99));
		Assert.Equal(1, VillagerTexts.PlanToday(3, 0, 7, 0.1));
		Assert.Equal(0, VillagerTexts.PlanToday(3, 0, 7, 0.9));
		Assert.Equal(3, VillagerTexts.PlanToday(3, 0, 1, 0.9));
		Assert.Equal(7, VillagerTexts.DaysLeftInWeek(1));
		Assert.Equal(1, VillagerTexts.DaysLeftInWeek(7));
		Assert.Equal(1, VillagerTexts.WeekOf(8));
	}

	[Fact]
	public void PlannedTimesStayInDaytime()
	{
		IReadOnlyList<int> times = VillagerTexts.PlanTimes(3, new[] { 0.0, 0.5, 0.999 });
		Assert.Equal(3, times.Count);
		Assert.All(times, t => Assert.InRange(t, 900, 2000));
		Assert.All(times, t => Assert.Equal(0, t % 10));
		Assert.True(times.SequenceEqual(times.OrderBy(t => t)));
	}

	[Fact]
	public void SenderNeedsHeartsAndCooldown()
	{
		List<TextCandidate> list = new List<TextCandidate>
		{
			new TextCandidate("Abigail", 1, null, false, false),
			new TextCandidate("Sam", 6, 9, false, false),
			new TextCandidate("Leah", 4, null, true, false),
			new TextCandidate("Emily", 4, null, false, true),
			new TextCandidate("Haley", 3, 5, false, false)
		};
		Assert.Equal("Haley", VillagerTexts.PickSender(list, 10, 0.5)?.NpcName);
		Assert.Null(VillagerTexts.PickSender(list.Take(4), 10, 0.5));
	}

	[Fact]
	public void KindsDependOnWhatIsAvailable()
	{
		Assert.Equal(3, VillagerTexts.Kinds(false, false, false).Count);
		Assert.Equal(7, VillagerTexts.Kinds(true, true, true, true).Count);
		Assert.Equal(TextKind.SmallTalk, VillagerTexts.PickKind(VillagerTexts.Kinds(false, false, false), 0.0));
		Assert.Equal(TextKind.CheckIn, VillagerTexts.PickKind(VillagerTexts.Kinds(false, false, false), 0.99));
		Assert.False(VillagerTexts.CanFlirt(false, true, 8, farmerMarriedElsewhere: true));
		Assert.True(VillagerTexts.CanFlirt(true, false, 0, farmerMarriedElsewhere: false));
	}

	[Fact]
	public void CleanTextStripsNameAndQuotes()
	{
		Assert.Equal("在忙吗？", VillagerTexts.CleanText("阿比盖尔：「在忙吗？」", "Abigail", "阿比盖尔"));
		Assert.Equal("hey", VillagerTexts.CleanText("\"Abigail: hey\"", "Abigail", "阿比盖尔"));
		Assert.Equal("", VillagerTexts.CleanText(null));
		Assert.True(VillagerTexts.CleanText(new string('哈', 400)).Length <= VillagerTexts.MaxChars + 1);
	}

	[Fact]
	public void CallbackSkipsToday()
	{
		List<TextEntry> entries = new List<TextEntry>
		{
			new TextEntry(2, "spring", 1, "daily", "小芳", false, "你喜欢紫色吗", "最喜欢了"),
			new TextEntry(3, "spring", 1, MessengerRules.ContextType, "小芳", false, "今天呢", "还行")
		};
		TextEntry? pick = VillagerTexts.PickCallback(entries, "小芳", 3, "spring", 1);
		Assert.Equal("你喜欢紫色吗", pick?.PlayerMessage);
		Assert.Contains("当面", VillagerTexts.DescribeCallback(pick!));
	}

	[Fact]
	public void PromptCarriesKindRules()
	{
		FakeSender ai = new FakeSender().Reply("在干嘛呢");
		MessengerAi messenger = new MessengerAi(ai, new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance), () => "简体中文");
		TextRequest request = new TextRequest(TextKind.Gossip, "Abigail", "爱冒险", "小芳", VillagerTexts.Relationship(5, false, false, false, false), "晴天", "", "你听说的八卦：山姆组了乐队", "");
		string? system = messenger.System(request);
		Assert.NotNull(system);
		Assert.Contains("山姆组了乐队", system);
		Assert.DoesNotContain("{{", system);
		Assert.Contains("算得上朋友", system);
		string? text = messenger.VillagerTextAsync(request).GetAwaiter().GetResult();
		Assert.Equal("在干嘛呢", text);
		Assert.Equal(MessengerAi.RequestName, ai.Requests.Single().Name);
	}

	[Fact]
	public void EveryKindHasRules()
	{
		string raw = new PromptTemplates(new DirectoryBackend(DialogueEngineTests.PromptDir()), NullLog.Instance).Raw("messenger_text_kinds")!;
		foreach (TextKind kind in Enum.GetValues<TextKind>())
		{
			Assert.NotEqual("", MessengerAi.KindSection(raw, VillagerTexts.KindKey(kind)));
		}
	}
}
