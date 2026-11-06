using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Social;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>设置：左边分类，右边一张张卡片。改了马上生效并写进 config.json；更细的数值在 GMCM 里。</summary>
internal sealed class SettingsPage : ShellPage
{
	private abstract record Row(string Label, string Note)
	{
		/// <summary>只在满足时显示（比如总开关开着）。</summary>
		public Func<ModConfig, bool>? Shown { get; init; }

		/// <summary>按当前设置算出来的说明（不空时代替 Note）。</summary>
		public Func<ModConfig, string>? Status { get; init; }
	}

	private sealed record ToggleRow(string Label, string Note, Func<ModConfig, bool> Get, Action<ModConfig, bool> Set) : Row(Label, Note);

	private sealed record ChoiceRow(string Label, string Note, string[] Options, Func<ModConfig, int> Get, Action<ModConfig, int> Set) : Row(Label, Note);

	private sealed record InfoRow(string Label, string Note, Func<ModConfig, string> Value) : Row(Label, Note);

	/// <summary>点了推进一页。</summary>
	private sealed record LinkRow(string Label, string Note, Func<ShellContext, ShellPage> Page) : Row(Label, Note);

	private static readonly int[] TalkMinutes = AISV.Core.Dialogue.TalkTime.Choices;

	private static readonly int[] DailyLimits = { 3, 5, 8, 10, 15 };

	private static readonly int[] ItemRequestChances = { 0, 5, 10, 20, 35, 50 };

	private static readonly int[] WeeklyTexts = { 0, 1, 3, 5, 7, 10 };

	private static readonly int[] ArcLimits = { 1, 2, 3, 4, 5 };

	private static readonly int[] PostDays = { 1, 2, 3, 5, 7, 14 };

	private static readonly int[] FizzleSeasons = { 1, 2, 3, 4, 8 };

	private static readonly int[] Thresholds = { 50, 60, 70, 80, 90 };

	private static readonly int[] ExpulsionChances = { 0, 10, 25, 50, 75, 100 };

	private static readonly int[] OutingHearts = { 0, 2, 3, 4, 6, 8 };

	private static readonly int[] OutingCooldowns = { 0, 1, 3, 7, 14 };

	private static readonly int[] OutingFriendship = { 0, 5, 10, 20, 30, 50 };

	private static readonly int[] OfferChances = { 0, 5, 10, 20, 30 };

	private static readonly int[] ScenesPerDay = { 0, 1, 2, 3, 4, 5 };

	private static readonly int[] CommissionCounts = { 0, 1, 2, 3, 4 };

	private static readonly int[] GiftCooldowns = { 1, 3, 7, 14, 28 };

	private static readonly int[] GiftHearts = { 0, 2, 3, 4, 6, 8 };

	private static readonly int[] GiftsPerDay = { 0, 1, 2, 3, 5 };

	private static readonly int[] StoryIntervals = { 2, 3, 5, 7, 10, 14, 21, 28 };

	private static readonly string[] StoryStyles = { "mixed", "romantic", "everyday", "adventure", "funny" };

	private static readonly string[] DramaLevels = { AISV.Core.Social.Drama.DramaRules.Cozy, AISV.Core.Social.Drama.DramaRules.Balanced, AISV.Core.Social.Drama.DramaRules.Dramatic };

	private static readonly string[] SceneStyles = { AISV.Core.Scenes.SceneStyle.Natural, AISV.Core.Scenes.SceneStyle.AmbientOnly, AISV.Core.Scenes.SceneStyle.Cinematic };

	private static int Nearest(int[] options, int value)
	{
		int best = 0;
		for (int i = 1; i < options.Length; i++)
		{
			if (Math.Abs(options[i] - value) < Math.Abs(options[best] - value))
			{
				best = i;
			}
		}
		return best;
	}

	private sealed record Category(string Title, string Description, List<(string Group, List<Row> Rows)> Groups);

	private readonly Scroller _scroll = new Scroller();

	private readonly List<Category> _categories;

	private int _current;

	private Rectangle _detail;

	public SettingsPage(ShellContext ctx) : base(ctx)
	{
		_categories = Build();
	}

	private static List<Category> Build()
	{
		string[] focus = { Tr.Get("shell.settings.focusNormal", "多说你的事"), Tr.Get("shell.settings.focusBalanced", "你和村民各半"), Tr.Get("shell.settings.focusVillagers", "只说村民的事") };
		string[] focusKeys = { GossipFocus.Normal, GossipFocus.Balanced, GossipFocus.VillagersOnly };
		List<Category> list = new List<Category>
		{
			new Category(Tr.Get("shell.settings.common", "常用"), Tr.Get("shell.settings.commonDesc", "最常改的几个开关。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.ai", "AI"), new List<Row>
				{
					new LinkRow(Tr.Get("shell.settings.account", "账号"), Tr.Get("shell.settings.accountNote", "注册、登录、改密码，看今天还能用多少"), ctx => new AccountPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.gateway", "AI 连接"), Tr.Get("shell.settings.gatewayNote", "用服务器还是自己的 AI 网关、模型、思考档位"), ctx => new GatewayPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.aiStatus", "AI 状态"), Tr.Get("shell.settings.aiStatusNote", "现在好不好用、今天用了多少、最近为什么失败"), ctx => new AiStatusPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.npcCompat", "村民和 NPC mod"), Tr.Get("shell.settings.npcCompatNote", "哪些村民用 AI，为什么；关掉谁、用哪些 mod 的村民"), ctx => new NpcCompatPage(ctx)),
					new ToggleRow(Tr.Get("shell.settings.aiFeatures", "AI 功能"), Tr.Get("shell.settings.aiFeaturesNote", "关掉后村民用游戏原来的台词，晚上也不整理八卦和日记"), c => c.AiOn, (c, v) => c.AiOn = v),
					new ChoiceRow(Tr.Get("shell.settings.talkMinutes", "聊天花的时间"), Tr.Get("shell.settings.talkMinutesNote", "当面聊一轮、群聊说一句，钟走几分钟（只在单人游戏里；聊完关掉对话框再走，最晚到 25:50）"),
						TalkMinutes.Select(m => m == 0 ? Tr.Get("shell.settings.talkFree", "不花时间") : Tr.Get("shell.settings.minutes", "{{n}} 分钟", ("n", m))).ToArray(),
						c => Nearest(TalkMinutes, c.MinutesPerReply), (c, i) => c.MinutesPerReply = TalkMinutes[i])
				}),
				(Tr.Get("shell.settings.profiles", "设定"), new List<Row>
				{
					new LinkRow(Tr.Get("shell.settings.myProfile", "我的设定"), Tr.Get("shell.settings.myProfileNote", "你是谁、为什么搬来、生日：村民会照着这些认识你"), ctx => new ProfilePage(ctx)),
					new LinkRow(Tr.Get("shell.settings.cards", "村民角色卡"), Tr.Get("shell.settings.cardsNote", "改写或补充某位村民的人设，所有存档通用"), ctx => new CardsPage(ctx))
				}),
				(Tr.Get("shell.settings.social", "镇上的生活"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.gossip", "八卦和日记"), Tr.Get("shell.settings.gossipNote", "晚上把今天的事整理成八卦，顺便写日记"), c => c.RumorsOn, (c, v) => c.RumorsOn = v),
					new ToggleRow(Tr.Get("shell.settings.letters", "村民来信"), Tr.Get("shell.settings.lettersNote", "交情到了、很久没见时，村民会写信"), c => c.MailOn, (c, v) => c.MailOn = v)
				})
			}),
			new Category(Tr.Get("shell.settings.seenAndHeard", "看到听到"), Tr.Get("shell.settings.seenAndHeardDesc", "村民会记住你当着他们的面做的事。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.seenAndHeard", "看到听到"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.shEnable", "村民会注意到你做的事"), Tr.Get("shell.settings.shEnableNote", "看到了会记住，气不过会走过来找你说"), c => c.NoticingOn, (c, v) => c.NoticingOn = v),
					new ToggleRow(Tr.Get("shell.settings.shBubbles", "头上冒一句话"), Tr.Get("shell.settings.shBubblesNote", "没走过来的人可能随口说一句"), c => c.NoticeRemarks, (c, v) => c.NoticeRemarks = v),
					new ToggleRow(Tr.Get("shell.settings.shText", "走不过来就发消息"), Tr.Get("shell.settings.shTextNote", "想找你说却走不过来时，改用信使发一条"), c => c.NoticeByText, (c, v) => c.NoticeByText = v),
					new ToggleRow(Tr.Get("shell.settings.thief", "对偷窃有反应"), Tr.Get("shell.settings.thiefNote", "装了 Stardew Thief 时：看见的人会找你理论，失主发现少了东西，镇上会传开"), c => c.NoticeTheft, (c, v) => c.NoticeTheft = v)
				}),
				(Tr.Get("shell.settings.group", "群聊"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.groupInWorld", "在地图上聊"), Tr.Get("shell.settings.groupInWorldNote", "站在一起的村民会自己聊起来，按插话键加入"), c => c.WorldGroupTalkOn, (c, v) => c.WorldGroupTalkOn = v)
				}),
				(Tr.Get("shell.settings.favors", "拜托和表白"), new List<Row>
				{
					new ChoiceRow(Tr.Get("shell.settings.itemRequest", "村民托你找东西的机会"), Tr.Get("shell.settings.itemRequestNote", "每天第一次聊天时，村民有这个机会托你带一样东西"), ItemRequestChances.Select(n => n + "%").ToArray(), c => Nearest(ItemRequestChances, c.WishOddsPercent), (c, i) => c.WishOddsPercent = ItemRequestChances[i]),
					new ToggleRow(Tr.Get("shell.settings.proposals", "对话里表白、求婚"), Tr.Get("shell.settings.proposalsNote", "交情够了，回复框里会出现开关，答不答应由村民决定"), c => c.CourtingOn, (c, v) => c.CourtingOn = v)
				}),
				(Tr.Get("shell.settings.outings", "约出去"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.inviteOut", "回复框里可以约出去"), Tr.Get("shell.settings.inviteOutNote", "去不去、去哪由村民决定；答应了就在约好的地方演一小段"), c => c.OutingsOn, (c, v) => c.OutingsOn = v),
					new ChoiceRow(Tr.Get("shell.settings.inviteOutHearts", "几心起能约"), Tr.Get("shell.settings.inviteOutHeartsNote", "交情到了这个心数，回复框里才会出现开关"), OutingHearts.Select(n => Tr.Get("shell.settings.hearts", "{{n}} 心", ("n", n))).ToArray(), c => Nearest(OutingHearts, c.OutingHearts), (c, i) => c.OutingHearts = OutingHearts[i]),
					new ChoiceRow(Tr.Get("shell.settings.inviteOutAccepted", "答应后隔几天再约"), Tr.Get("shell.settings.inviteOutAcceptedNote", "村民自己没说时用这个"), OutingCooldowns.Select(n => Tr.Get("shell.settings.days", "{{n}} 天", ("n", n))).ToArray(), c => Nearest(OutingCooldowns, c.OutingRestAfterYes), (c, i) => c.OutingRestAfterYes = OutingCooldowns[i]),
					new ChoiceRow(Tr.Get("shell.settings.inviteOutRejected", "被拒后隔几天再约"), Tr.Get("shell.settings.inviteOutRejectedNote", "村民自己没说时用这个"), OutingCooldowns.Select(n => Tr.Get("shell.settings.days", "{{n}} 天", ("n", n))).ToArray(), c => Nearest(OutingCooldowns, c.OutingRestAfterNo), (c, i) => c.OutingRestAfterNo = OutingCooldowns[i]),
					new ChoiceRow(Tr.Get("shell.settings.inviteOutFriendship", "一起出去加的好感"), Tr.Get("shell.settings.inviteOutFriendshipNote", "演完那一段时加"), OutingFriendship.Select(n => n.ToString()).ToArray(), c => Nearest(OutingFriendship, c.OutingPoints), (c, i) => c.OutingPoints = OutingFriendship[i]),
					new ChoiceRow(Tr.Get("shell.settings.spouseOffer", "配偶主动约你的机会"), Tr.Get("shell.settings.spouseOfferNote", "配偶开口第一句有这个机会约你出去"), OfferChances.Select(n => n + "%").ToArray(), c => Nearest(OfferChances, c.SpouseAsksOutPercent), (c, i) => c.SpouseAsksOutPercent = OfferChances[i]),
					new ToggleRow(Tr.Get("shell.settings.hotSpouse", "和配偶的亲密场景"), Tr.Get("shell.settings.hotSpouseNote", "约会可以有成年人之间的亲密气氛，不露骨"), c => c.SpouseIntimacyOn, (c, v) => c.SpouseIntimacyOn = v) { Shown = c => c.AdultModuleOn }
				}),
				(Tr.Get("shell.settings.scenes", "偶遇"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.sceneEvents", "镇上的偶遇"), Tr.Get("shell.settings.sceneEventsNote", "每天挑几段村民的小场景，你到了那里就会碰上"), c => c.EncountersOn, (c, v) => c.EncountersOn = v),
					new ChoiceRow(Tr.Get("shell.settings.sceneStyle", "怎么演"), Tr.Get("shell.settings.sceneStyleNote", "自然：一个人走过来找你，几个人在旁边聊；也可以只旁听或都演成过场"),
						new[] { Tr.Get("shell.settings.sceneNatural", "自然"), Tr.Get("shell.settings.sceneAmbient", "只旁听"), Tr.Get("shell.settings.sceneCinematic", "过场") },
						c => Math.Max(0, Array.IndexOf(SceneStyles, AISV.Core.Scenes.SceneStyle.Normalize(c.EncounterStyle))), (c, i) => c.EncounterStyle = SceneStyles[i]) { Shown = c => c.EncountersOn },
					new ChoiceRow(Tr.Get("shell.settings.scenesPerDay", "每天最多几段"), Tr.Get("shell.settings.scenesPerDayNote", "同一个地方一天只挑一段"), ScenesPerDay.Select(n => n.ToString()).ToArray(), c => Nearest(ScenesPerDay, c.EncountersDaily), (c, i) => c.EncountersDaily = ScenesPerDay[i]) { Shown = c => c.EncountersOn },
					new ToggleRow(Tr.Get("shell.settings.sceneMemory", "记住看到的"), Tr.Get("shell.settings.sceneMemoryNote", "演完后说话的人会记得这件事，也会传成八卦"), c => c.EncountersRemembered, (c, v) => c.EncountersRemembered = v) { Shown = c => c.EncountersOn }
				}),
				(Tr.Get("shell.settings.festival", "节日"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.festivalAi", "节日上用 AI 说话"), Tr.Get("shell.settings.festivalAiNote", "把节日上的闲聊换成村民自己的话，还能邀请跳舞"), c => c.FestivalTalkOn, (c, v) => c.FestivalTalkOn = v)
				}),
				(Tr.Get("shell.settings.shEffects", "后果"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.shMood", "影响心情"), Tr.Get("shell.settings.shMoodNote", "看到的事会让村民当天心情变好或变差"), c => c.NoticingMovesMood, (c, v) => c.NoticingMovesMood = v),
					new ToggleRow(Tr.Get("shell.settings.shFriendship", "影响好感"), Tr.Get("shell.settings.shFriendshipNote", "村民说出心里话时，好感随之增减"), c => c.NoticingMovesHearts, (c, v) => c.NoticingMovesHearts = v),
					new ToggleRow(Tr.Get("shell.settings.shCold", "发火后不理人"), Tr.Get("shell.settings.shColdNote", "发过大火的村民一小时内第一次不跟你聊"), c => c.SilentTreatment, (c, v) => c.SilentTreatment = v)
				})
			}),
			new Category(Tr.Get("shell.settings.social", "镇上的生活"), Tr.Get("shell.settings.socialDesc", "八卦、信件、小剧场，还有村民之间的来往。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.gossipGroup", "八卦"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.gossip", "八卦和日记"), Tr.Get("shell.settings.gossipNote", "晚上把今天的事整理成八卦，顺便写日记"), c => c.RumorsOn, (c, v) => c.RumorsOn = v),
					new ChoiceRow(Tr.Get("shell.settings.focus", "八卦说谁"), Tr.Get("shell.settings.focusNote", "镇上的人更爱聊你，还是聊他们自己"), focus, c => Math.Max(0, Array.IndexOf(focusKeys, GossipFocus.Normalize(c.RumorSubject))), (c, i) => c.RumorSubject = focusKeys[i]),
					new ToggleRow(Tr.Get("shell.settings.socialGossip", "村民的事也传八卦"), Tr.Get("shell.settings.socialGossipNote", "村民之间的小事也会传开"), c => c.MeetupsBecomeRumors, (c, v) => c.MeetupsBecomeRumors = v),
					new ToggleRow(Tr.Get("options.gazette", "镇上的报纸"), Tr.Get("shell.settings.weeklyNewsNote", "每月 7、14、21、28 日晚上出一期《鹈鹕镇周报》，第二天早上送到信箱"), c => c.GazetteOn, (c, v) => c.GazetteOn = v)
				}),
				(Tr.Get("shell.settings.lettersGroup", "信件"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.letters", "村民来信"), Tr.Get("shell.settings.lettersNote", "交情到了、很久没见时，村民会写信"), c => c.MailOn, (c, v) => c.MailOn = v),
					new ToggleRow(Tr.Get("shell.settings.eventLetters", "事后来信"), Tr.Get("shell.settings.eventLettersNote", "节日、出游、生日收到心爱的礼物之后寄来的信"), c => c.MailAfterHeartScene, (c, v) => c.MailAfterHeartScene = v)
				}),
				(Tr.Get("shell.settings.giftsGroup", "村民送的礼物"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.aiGifts", "村民送的礼物有名字"), Tr.Get("shell.settings.aiGiftsNote", "村民送你的东西有自己的名字和描述，用送礼人的口吻写。心数里程碑的信里必带一件；聊天时由村民自己决定要不要当面送。关掉后信里附游戏自带的物品。"), c => c.VillagerGiftsOn, (c, v) => c.VillagerGiftsOn = v),
					new ChoiceRow(Tr.Get("shell.settings.aiGiftCooldown", "同一个人送礼间隔"), Tr.Get("shell.settings.aiGiftCooldownNote", "同一个村民两次送礼至少隔这么多天。里程碑信件不受限制。"), GiftCooldowns.Select(n => Tr.Get("shell.settings.days", "{{n}} 天", ("n", n))).ToArray(), c => Nearest(GiftCooldowns, c.GiftRestDays), (c, i) => c.GiftRestDays = GiftCooldowns[i]) { Shown = c => c.VillagerGiftsOn },
					new ChoiceRow(Tr.Get("shell.settings.aiGiftHearts", "聊天送礼的最低心数"), Tr.Get("shell.settings.aiGiftHeartsNote", "至少这么多心，村民才会在聊天时当面送你东西。"), GiftHearts.Select(n => Tr.Get("shell.settings.hearts", "{{n}} 心", ("n", n))).ToArray(), c => Nearest(GiftHearts, c.GiftHearts), (c, i) => c.GiftHearts = GiftHearts[i]) { Shown = c => c.VillagerGiftsOn },
					new ChoiceRow(Tr.Get("shell.settings.aiGiftsPerDay", "每天聊天送礼上限"), Tr.Get("shell.settings.aiGiftsPerDayNote", "一天里所有村民在聊天时当面送礼的总次数。"), GiftsPerDay.Select(n => n.ToString()).ToArray(), c => Nearest(GiftsPerDay, c.GiftsDaily), (c, i) => c.GiftsDaily = GiftsPerDay[i]) { Shown = c => c.VillagerGiftsOn }
				}),
				(Tr.Get("shell.settings.storiesGroup", "伴侣剧情"), new List<Row>
				{
					new ToggleRow(Tr.Get("options.chapters", "伴侣剧情"), Tr.Get("options.chapters.note", "结婚以后，伴侣隔几天会和你有一段小故事：写信给你、拜托你一件事、约你在某个地方见面演一场戏、有话想跟你说。几位伴侣轮流。需要 AI。"), c => c.SpouseChaptersOn, (c, v) => c.SpouseChaptersOn = v),
					new ChoiceRow(Tr.Get("options.chapterGap", "两段故事之间隔几天"), Tr.Get("options.chapterGap.note", "一段故事开始后，过多少天开始下一段（有几位伴侣时轮流）。"), StoryIntervals.Select(n => Tr.Get("shell.settings.days", "{{n}} 天", ("n", n))).ToArray(), c => Nearest(StoryIntervals, c.SpouseChapterGap), (c, i) => c.SpouseChapterGap = StoryIntervals[i]) { Shown = c => c.SpouseChaptersOn },
					new ChoiceRow(Tr.Get("shell.settings.storyStyle", "故事风格"), Tr.Get("shell.settings.storyStyleNote", "伴侣写的小故事偏哪种味道；随机就是每次挑最合适的。"),
						new[] { Tr.Get("shell.settings.storyMixed", "随机"), Tr.Get("shell.settings.storyRomantic", "浪漫"), Tr.Get("shell.settings.storyEveryday", "日常"), Tr.Get("shell.settings.storyAdventure", "小冒险"), Tr.Get("shell.settings.storyFunny", "搞笑") },
						c => Math.Max(0, Array.IndexOf(StoryStyles, c.SpouseChapterFlavor)), (c, i) => c.SpouseChapterFlavor = StoryStyles[i]) { Shown = c => c.SpouseChaptersOn }
				}),
				(Tr.Get("shell.settings.messenger", "信使"), new List<Row>
				{
					new ChoiceRow(Tr.Get("shell.settings.msgLimit", "每人每天能发几条"), Tr.Get("shell.settings.msgLimitNote", "给每位村民分别计数"), DailyLimits.Select(n => n.ToString()).ToArray(), c => Nearest(DailyLimits, c.TextsDaily), (c, i) => c.TextsDaily = DailyLimits[i]),
					new ChoiceRow(Tr.Get("shell.settings.villagerTexts", "村民每周主动发几条"), Tr.Get("shell.settings.villagerTextsNote", "闲聊、八卦、自拍……0 就是不主动发"), WeeklyTexts.Select(n => n.ToString()).ToArray(), c => Nearest(WeeklyTexts, c.VillagerTextsWeekly), (c, i) => c.VillagerTextsWeekly = WeeklyTexts[i])
				}),
				(Tr.Get("shell.settings.villageGroup", "村民之间"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.npcInteractions", "村民之间的来往"), Tr.Get("shell.settings.npcInteractionsNote", "每天晚上村民之间发生一两件小事"), c => c.VillagerMeetupsOn, (c, v) => c.VillagerMeetupsOn = v),
					new ToggleRow(Tr.Get("shell.settings.friendships", "村民关系会变"), Tr.Get("shell.settings.friendshipsNote", "这些小事会慢慢改变他们之间的关系"), c => c.VillagerBondsShift, (c, v) => c.VillagerBondsShift = v),
					new ToggleRow(Tr.Get("shell.settings.personality", "性格慢慢变化"), Tr.Get("shell.settings.personalityNote", "你怎么待人，村民会慢慢变得不一样"), c => c.TemperamentShifts, (c, v) => c.TemperamentShifts = v)
				}),
				(Tr.Get("shell.settings.dramaGroup", "是非和传闻"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.drama", "村民之间的是非"), Tr.Get("shell.settings.dramaNote", "八卦和你的话会让村民对彼此有看法，传闻也会越传越广"), c => c.DramaOn, (c, v) => c.DramaOn = v),
					new ChoiceRow(Tr.Get("shell.settings.dramaIntensity", "戏有多浓"), Tr.Get("shell.settings.dramaIntensityNote", "越浓，看法变得越快，传闻传得越远，村民说话也越冲"),
						new[] { Tr.Get("shell.settings.dramaCozy", "温和"), Tr.Get("shell.settings.dramaBalanced", "平常"), Tr.Get("shell.settings.dramaDramatic", "跌宕") },
						c => Math.Max(0, Array.IndexOf(DramaLevels, AISV.Core.Social.Drama.DramaRules.Intensity(c.DramaHeat))), (c, i) => c.DramaHeat = DramaLevels[i]) { Shown = c => c.DramaOn },
					new ToggleRow(Tr.Get("shell.settings.dramaGossip", "八卦会改变看法"), Tr.Get("shell.settings.dramaGossipNote", "听到别人的坏话或好话，村民对那人的态度会变"), c => c.RumorsMoveOpinions, (c, v) => c.RumorsMoveOpinions = v) { Shown = c => c.DramaOn },
					new ToggleRow(Tr.Get("shell.settings.dramaPlayer", "你的话也算数"), Tr.Get("shell.settings.dramaPlayerNote", "在村民面前替人说话或说人坏话，他会听进去几分"), c => c.FarmerCanSway, (c, v) => c.FarmerCanSway = v) { Shown = c => c.DramaOn },
					new ToggleRow(Tr.Get("shell.settings.dramaBadmouth", "背后说人"), Tr.Get("shell.settings.dramaBadmouthNote", "听了坏话的村民可能私下抱怨几句"), c => c.BackbitingOn, (c, v) => c.BackbitingOn = v) { Shown = c => c.DramaOn }
				}),
				(Tr.Get("shell.settings.adultGroup", "成年人的感情"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.moduleAdult", "成人模块"), Tr.Get("shell.settings.moduleAdultNote", "关闭时，所有成人向开关都不生效（各自的设置值会保留，重新开启后恢复）。"), c => c.AdultModuleOn, (c, v) => c.AdultModuleOn = v),
					new ToggleRow(Tr.Get("shell.settings.adultDrama", "成人关系戏"), Tr.Get("shell.settings.adultDramaNote", "成年村民之间可以有嫉妒、愧疚、夫妻闹别扭，不露骨；孩子不会有"), c => c.AdultDramaAllowed, (c, v) => c.AdultDramaAllowed = v) { Shown = c => c.AdultModuleOn },
					new ToggleRow(Tr.Get("shell.settings.infidelity", "允许已婚的人动摇"), Tr.Get("shell.settings.infidelityNote", "你在夫妻之间挑事时，已婚村民可能被你吸引而心里挣扎"), c => c.WaveringSpousesOn, (c, v) => c.WaveringSpousesOn = v) { Shown = c => c.AdultModuleOn && c.AdultDramaAllowed },
					new ToggleRow(Tr.Get("shell.settings.protectMarriages", "保护原作里的夫妻"), Tr.Get("shell.settings.protectMarriagesNote", "游戏本来的四对夫妻不会动摇，也不会和别人萌生恋情"), c => c.SafeguardGameCouples, (c, v) => c.SafeguardGameCouples = v)
				}),
				(Tr.Get("shell.settings.worldGroup", "世界"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.moduleWorld", "世界模块"), Tr.Get("shell.settings.moduleWorldNote", "市场行情、委托、生态读取、周报扩展等世界玩法"), c => c.WorldModuleOn, (c, v) => c.WorldModuleOn = v),
					new ToggleRow(Tr.Get("shell.settings.market", "市场"), Tr.Get("shell.settings.marketNote", "作物和加工品的出货价每天按行情浮动；直接卖给商店不受影响"), c => c.MarketOn, (c, v) => c.MarketOn = v) { Shown = c => c.WorldModuleOn },
					new ToggleRow(Tr.Get("shell.settings.commissions", "村民在委托板上贴委托"), Tr.Get("shell.settings.commissionsNote", "每天早上村民在委托板上贴新委托（送物品、采集、挖矿、拜访）。在委托板上接受或放弃，带着东西找发布者交付。接了没做完、过了期限，发布者好感会下降。"), c => c.JobBoardOn, (c, v) => c.JobBoardOn = v) { Shown = c => c.WorldModuleOn },
					new ChoiceRow(Tr.Get("shell.settings.commissionsPerDay", "每天新委托条数"), Tr.Get("shell.settings.commissionsPerDayNote", "每天早上贴几条新委托。板上没人接的最多 4 条。"), CommissionCounts.Select(n => n.ToString()).ToArray(), c => Nearest(CommissionCounts, c.JobsPostedDaily), (c, i) => c.JobsPostedDaily = CommissionCounts[i]) { Shown = c => c.WorldModuleOn && c.JobBoardOn },
					new ToggleRow(Tr.Get("shell.settings.errands", "给村民发委托"), Tr.Get("shell.settings.errandsNote", "出钱托村民找东西、代买、做菜或转送。报酬先交出去，没办成全额退回。"), c => c.ErrandsOn, (c, v) => c.ErrandsOn = v) { Shown = c => c.WorldModuleOn },
					new ToggleRow(Tr.Get("shell.settings.ledger", "村民账本"), Tr.Get("shell.settings.ledgerNote", "村民有自己的钱、随身物品和家当，可以向他们借钱"), c => c.PursesOn, (c, v) => c.PursesOn = v) { Shown = c => c.WorldModuleOn },
					new ToggleRow(Tr.Get("shell.settings.ledgerBubbles", "村民买东西时头顶说一句"), Tr.Get("shell.settings.ledgerBubblesNote", "你在旁边时才会看到"), c => c.ShoppingRemarks, (c, v) => c.ShoppingRemarks = v) { Shown = c => c.WorldModuleOn && c.PursesOn }
				}),
				(Tr.Get("shell.settings.companionsGroup", "同伴"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.moduleCompanions", "同伴模块"), Tr.Get("shell.settings.moduleCompanionsNote", "村民跟随、帮工、约好时间上门帮忙（跟随和帮工需要安装 The Stardew Squad）"), c => c.CompanionModuleOn, (c, v) => c.CompanionModuleOn = v),
					new ToggleRow(Tr.Get("shell.settings.aiProposals", "村民主动提议"), Tr.Get("shell.settings.aiProposalsNote", "聊天时村民可能说要跟你去、帮你干活、约你见面，聊完你来决定"), c => c.VillagersOfferHelp, (c, v) => c.VillagersOfferHelp = v) { Shown = c => c.CompanionModuleOn }
				}),
				(Tr.Get("shell.settings.arcs", "小剧场"), new List<Row>
				{
					new ChoiceRow(Tr.Get("shell.settings.arcLimit", "同时进行几出"), Tr.Get("shell.settings.arcLimitNote", "每周三看看镇上有没有新故事，进行中的够数了就不再开新的"), ArcLimits.Select(n => n.ToString()).ToArray(), c => Nearest(ArcLimits, c.StorylinesAtOnce), (c, i) => c.StorylinesAtOnce = ArcLimits[i]),
					new ToggleRow(Tr.Get("shell.settings.arcScene", "收尾时演一段"), Tr.Get("shell.settings.arcSceneNote", "结束小剧场时，在游戏里把结局演出来"), c => c.FinaleScenesOn, (c, v) => c.FinaleScenesOn = v),
					new ToggleRow(Tr.Get("shell.settings.leaveTown", "可能有人离开小镇"), Tr.Get("shell.settings.leaveTownNote", "全镇都容不下某人时，小剧场可能以他离开收场（Jas、Vincent、Leo 和你的配偶不会）"), c => c.DepartureStoriesOn, (c, v) => c.DepartureStoriesOn = v),
					new ChoiceRow(Tr.Get("shell.settings.leaveTownChance", "离开小镇的故事多常见"), Tr.Get("shell.settings.leaveTownChanceNote", "每次看新故事时，允许写这种故事的机会"), ExpulsionChances.Select(n => n + "%").ToArray(), c => Nearest(ExpulsionChances, c.DepartureHintPercent), (c, i) => c.DepartureHintPercent = ExpulsionChances[i]),
					new ChoiceRow(Tr.Get("shell.settings.leaveTownThreshold", "积怨多深才会被盯上"), Tr.Get("shell.settings.leaveTownThresholdNote", "全镇对一个人的敌意超过这个比例，他才会出现在离开小镇的人选里"), Thresholds.Select(n => n + "%").ToArray(), c => Nearest(Thresholds, c.DepartureGrudgePercent), (c, i) => c.DepartureGrudgePercent = Thresholds[i]),
					new ToggleRow(Tr.Get("shell.settings.leaveTownReturn", "离开的人可能回来"), Tr.Get("shell.settings.leaveTownReturnNote", "镇上可能演一出要不要接他回来的戏"), c => c.ReturnStoriesOn, (c, v) => c.ReturnStoriesOn = v),
					new ChoiceRow(Tr.Get("shell.settings.leaveTownReturnChance", "回来的故事多常见"), Tr.Get("shell.settings.leaveTownReturnChanceNote", "有人离开过时，每次看新故事允许写这种故事的机会"), ExpulsionChances.Select(n => n + "%").ToArray(), c => Nearest(ExpulsionChances, c.ReturnHintPercent), (c, i) => c.ReturnHintPercent = ExpulsionChances[i]),
					new ChoiceRow(Tr.Get("shell.settings.arcPostDays", "散场后几天还在聊"), Tr.Get("shell.settings.arcPostDaysNote", "结局后这几天里第一次见面，村民会先说起这件事"), PostDays.Select(n => Tr.Get("shell.settings.days", "{{n}} 天", ("n", n))).ToArray(), c => Nearest(PostDays, c.AfterwordDays), (c, i) => c.AfterwordDays = PostDays[i]),
					new ChoiceRow(Tr.Get("shell.settings.arcFizzle", "几季没进展就作罢"), Tr.Get("shell.settings.arcFizzleNote", "一个目标都没办成的小剧场，过了这么多季就不了了之"), FizzleSeasons.Select(n => Tr.Get("shell.settings.seasons", "{{n}} 季", ("n", n))).ToArray(), c => Nearest(FizzleSeasons, c.StorylineStaleSeasons), (c, i) => c.StorylineStaleSeasons = FizzleSeasons[i])
				})
			}),
			new Category(Tr.Get("shell.settings.keys", "快捷键"), Tr.Get("shell.settings.keysDesc", "在 Generic Mod Config Menu 或 config.json 里改。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.keysGroup", "按键"), new List<Row>
				{
					new InfoRow(Tr.Get("shell.settings.shellKey", "打开这个界面"), "", c => c.KeyShell),
					new InfoRow(Tr.Get("shell.settings.profileKey", "我的设定"), "", c => c.KeyAboutMe),
					new InfoRow(Tr.Get("shell.settings.gossipKey", "直接看八卦"), "", c => c.KeyRumors),
					new InfoRow(Tr.Get("shell.settings.groupKey", "群聊插话"), "", c => c.KeyGroupTalk)
				})
			})
		};
		if (OperatingSystem.IsWindows())
		{
			list.Insert(list.Count - 1, new Category(Tr.Get("shell.settings.other", "其他"), Tr.Get("shell.settings.otherDesc", "打字时的输入法。"), new List<(string, List<Row>)>
				{
					(Tr.Get("shell.settings.inputGroup", "输入"), new List<Row>
					{
						new ToggleRow(Tr.Get("options.imeBox", "显示输入法候选框"), Tr.Get("shell.settings.imeNote", "打拼音时在游戏里显示候选字；改了要重启游戏才生效"), c => c.ImeCandidatesVisible, (c, v) =>
						{
							c.ImeCandidatesVisible = v;
							ImeCandidateWindow.SetVariable(c, v);
						}) { Status = ImeCandidateWindow.StatusText }
					})
				}));
		}
		return list;
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _detail.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle nav = new Rectangle(area.X, area.Y, Math.Clamp(area.Width / 4, 240, 300), area.Height);
		_detail = new Rectangle(nav.Right, area.Y, area.Right - nav.Right, area.Height);
		DrawNav(b, nav, hits);
		DrawDetail(b, hits);
	}

	private void DrawNav(SpriteBatch b, Rectangle nav, Hits hits)
	{
		AisvTheme.Fill(b, nav, AisvTheme.Card);
		AisvTheme.VLine(b, nav.Right - 2, nav.Y, nav.Height, AisvTheme.Divider);
		int y = nav.Y + 16;
		for (int i = 0; i < _categories.Count; i++)
		{
			Rectangle r = new Rectangle(nav.X + 8, y, nav.Width - 20, 56);
			bool sel = i == _current;
			if (sel || AisvTheme.Hover(r))
			{
				AisvTheme.FillRounded(b, r, sel ? AisvTheme.SelectedRow : AisvTheme.PageDeep, 10);
			}
			if (sel)
			{
				AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 10, 4, r.Height - 20), AisvTheme.Accent, 2);
			}
			AisvTheme.DrawText(b, _categories[i].Title, new Vector2(r.X + 18, r.Y + 13), sel ? AisvTheme.AccentDark : AisvTheme.Text, 0.88f);
			AisvTheme.Chevron(b, new Vector2(r.Right - 20, r.Center.Y), left: false, AisvTheme.TextFaint);
			int index = i;
			hits.Add(r, () =>
			{
				if (_current != index)
				{
					Game1.playSound("smallSelect");
					_current = index;
					_scroll.Reset();
				}
			});
			y += 60;
		}
	}

	private void DrawDetail(SpriteBatch b, Hits hits)
	{
		Category cat = _categories[_current];
		ModConfig config = Ctx.Config();
		AisvTheme.BeginClip(b, _detail);
		int w = Math.Min(_detail.Width - 64, 820);
		int x = _detail.X + (_detail.Width - w) / 2;
		int y = _detail.Y + 22 - _scroll.Offset;
		AisvTheme.Bold(b, cat.Title, new Vector2(x, y), AisvTheme.Text, 1.15f);
		y += AisvTheme.LineHeight(1.15f) + 4;
		AisvTheme.DrawText(b, cat.Description, new Vector2(x, y), AisvTheme.TextMuted, 0.78f);
		y += 44;
		foreach ((string group, List<Row> all) in cat.Groups)
		{
			List<Row> rows = all.Where(r => r.Shown?.Invoke(config) ?? true).ToList();
			if (rows.Count == 0)
			{
				continue;
			}
			AisvTheme.DrawText(b, group, new Vector2(x + 6, y), AisvTheme.TextFaint, 0.75f);
			y += 32;
			Rectangle card = new Rectangle(x, y, w, rows.Count * 76 + 8);
			AisvTheme.DrawCard(b, card);
			for (int i = 0; i < rows.Count; i++)
			{
				Rectangle r = new Rectangle(x + 4, y + 4 + i * 76, w - 8, 76);
				if (i > 0)
				{
					AisvTheme.HLine(b, r.X + 16, r.Y, r.Width - 32, AisvTheme.Divider);
				}
				DrawRow(b, rows[i], r, config, hits);
			}
			y = card.Bottom + 22;
		}
		AisvTheme.DrawText(b, Tr.Get("shell.settings.more", "更细的数值（每天几封信、八卦条数等）可以在 Generic Mod Config Menu 里调。"), new Vector2(x + 6, y), AisvTheme.TextFaint, 0.72f);
		y += 40;
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _detail, y);
	}

	private void DrawRow(SpriteBatch b, Row row, Rectangle r, ModConfig config, Hits hits)
	{
		if (row is ToggleRow or LinkRow && AisvTheme.Hover(r, _detail))
		{
			AisvTheme.FillRounded(b, AisvTheme.Inflate(r, -2), AisvTheme.SelectedRow, 10);
		}
		string note = row.Status?.Invoke(config) is { Length: > 0 } status ? status : row.Note;
		int ty = note.Length > 0 ? r.Y + 12 : r.Y + 24;
		AisvTheme.DrawText(b, row.Label, new Vector2(r.X + 20, ty), AisvTheme.Text, 0.88f);
		if (note.Length > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate(note, r.Width - 300, 0.7f), new Vector2(r.X + 20, r.Y + 42), AisvTheme.TextFaint, 0.7f);
		}
		switch (row)
		{
		case ToggleRow t:
		{
			bool on = t.Get(config);
			Rectangle sw = new Rectangle(r.Right - 20 - 64, r.Center.Y - 18, 64, 36);
			AisvTheme.Toggle(b, sw, on);
			hits.Add(r, () => Change(c => t.Set(c, !on)), _detail);
			break;
		}
		case ChoiceRow ch:
		{
			int index = Math.Clamp(ch.Get(config), 0, ch.Options.Length - 1);
			string label = ch.Options[index];
			int bw = (int)AisvTheme.Width(label, 0.8f) + 56;
			Rectangle btn = new Rectangle(r.Right - 20 - bw, r.Center.Y - 22, bw, 44);
			AisvTheme.Button(b, btn, label, hover: AisvTheme.Hover(btn, _detail), scale: 0.8f);
			AisvTheme.Chevron(b, new Vector2(btn.Right - 18, btn.Center.Y), left: false, AisvTheme.TextFaint);
			hits.Add(btn, () => Change(c => ch.Set(c, (index + 1) % ch.Options.Length)), _detail);
			break;
		}
		case LinkRow link:
		{
			AisvTheme.Chevron(b, new Vector2(r.Right - 30, r.Center.Y), left: false, AisvTheme.AccentDark);
			hits.Add(r, () => Host.Push(link.Page(Ctx)), _detail);
			break;
		}
		case InfoRow info:
		{
			string value = info.Value(config);
			int cw = AisvTheme.ChipWidth(value, 0.8f);
			AisvTheme.Chip(b, new Vector2(r.Right - 20 - cw, r.Center.Y - 16), value, AisvTheme.PageDeep, AisvTheme.Text, 0.8f);
			break;
		}
		}
	}

	private void Change(Action<ModConfig> edit)
	{
		Game1.playSound("drumkit6");
		ModConfig c = Ctx.Config();
		edit(c);
		Ctx.SaveConfig(c.Normalize());
	}
}
