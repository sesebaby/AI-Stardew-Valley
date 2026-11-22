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
using StardewValley.Menus;

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

	private static readonly int[] WindowSizes = { 70, 80, 90, 100, 110, 125, 150 };

	private static readonly int[] PetDaily = { 1, 2, 3, 5, 8 };

	private static readonly int[] PetChances = { 10, 25, 40, 60 };

	private static readonly int[] RunawayChances = { 1, 3, 5, 10 };

	private static readonly string[] ChildModes = { "Off", "Line", "Chat" };

	private static readonly string[] HelpLevels = { "Light", "Normal", "Busy" };

	private static readonly int[] SpendLimits = { 0, 200, 400, 800, 1500 };

	private static readonly int[] MartOpens = { 700, 800, 900, 1000, 1100 };

	private static readonly int[] MartCloses = { 1500, 1600, 1700, 1800, 2000, 2200 };

	private static readonly int[] DailyLimits = { 3, 5, 6, 8, 10, 15 };

	private static readonly int[] ItemRequestChances = { 0, 5, 6, 10, 20, 35, 50 };

	private static readonly int[] WeeklyTexts = { 0, 1, 3, 4, 5, 7, 10 };

	private static readonly int[] ArcLimits = { 1, 2, 3, 4, 5 };

	private static readonly int[] PostDays = { 1, 2, 3, 4, 5, 7, 14 };

	private static readonly int[] FizzleSeasons = { 1, 2, 3, 4, 8 };

	private static readonly int[] Thresholds = { 50, 60, 70, 80, 90 };

	private static readonly int[] ExpulsionChances = { 0, 10, 25, 50, 75, 100 };

	private static readonly int[] OutingHearts = { 0, 2, 3, 4, 6, 8 };

	private static readonly int[] OutingCooldowns = { 0, 1, 2, 3, 7, 14 };

	private static readonly int[] OutingFriendship = { 0, 5, 10, 12, 20, 30, 50 };

	private static readonly int[] OfferChances = { 0, 5, 10, 12, 20, 30 };

	private static readonly int[] ModRumorLimits = { 5, 12, 25, 50, 100 };

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

	private readonly TextBox _search = new TextBox(null, null, Game1.smallFont, Hearth.Quill) { textLimit = 20, limitWidth = false };

	private int _current;

	private Rectangle _detail;

	private string _lastQuery = "";

	private int _resetArmedUntil;

	public SettingsPage(ShellContext ctx) : base(ctx)
	{
		_categories = Build();
	}

	public override bool CapturesKeys => _search.Selected;

	private string Query => (_search.Text ?? "").Trim();

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
					new ToggleRow(Tr.Get("shell.settings.shareStats", "帮助改进功能"), Tr.Get("shell.settings.shareStatsNote", "用服务器时上报游戏时长和各功能用了几次，不含聊天内容；关掉后不再收集"), c => c.ShareUsageStats, (c, v) => c.ShareUsageStats = v),
					new LinkRow(Tr.Get("shell.settings.privacy", "数据说明"), Tr.Get("shell.settings.privacyNote", "服务器会收到哪些信息、拿来做什么"), ctx => new PrivacyPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.gateway", "AI 连接"), Tr.Get("shell.settings.gatewayNote", "用服务器还是自己的 AI 网关、模型、思考档位"), ctx => new GatewayPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.aiStatus", "AI 状态"), Tr.Get("shell.settings.aiStatusNote", "现在好不好用、今天用了多少、最近为什么失败"), ctx => new AiStatusPage(ctx)),
					new LinkRow(Tr.Get("shell.settings.npcCompat", "村民和 NPC mod"), Tr.Get("shell.settings.npcCompatNote", "哪些村民用 AI，为什么；关掉谁、用哪些 mod 的村民"), ctx => new NpcCompatPage(ctx)),
					new ToggleRow(Tr.Get("shell.settings.aiFeatures", "AI 功能"), Tr.Get("shell.settings.aiFeaturesNote", "关掉后村民用游戏原来的台词，晚上也不整理八卦和日记"), c => c.AiOn, (c, v) => c.AiOn = v),
					new ToggleRow(Tr.Get("shell.settings.playerStarts", "我先开口"), Tr.Get("shell.settings.playerStartsNote", "点村民时不等 TA 开场，直接打开回复框由你起话题；TA 有非说不可的话时还是 TA 先说。回复框里也能切换"), c => c.PlayerStartsTalk, (c, v) => c.PlayerStartsTalk = v),
					new ChoiceRow(Tr.Get("shell.settings.talkMinutes", "聊天花的时间"), Tr.Get("shell.settings.talkMinutesNote", "当面聊一轮、群聊说一句，钟走几分钟（只在单人游戏里；聊完关掉对话框再走，最晚到 25:50）"),
						TalkMinutes.Select(m => m == 0 ? Tr.Get("shell.settings.talkFree", "不花时间") : Tr.Get("shell.settings.minutes", "{{n}} 分钟", ("n", m))).ToArray(),
						c => Nearest(TalkMinutes, c.MinutesPerReply), (c, i) => c.MinutesPerReply = TalkMinutes[i])
				}),
				(Tr.Get("shell.settings.uiGroup", "界面"), new List<Row>
				{
					new ChoiceRow(Tr.Get("shell.settings.windowSize", "窗口大小"), Tr.Get("shell.settings.windowSizeNote", "只改变窗口，字的大小不变"), WindowSizes.Select(n => n + "%").ToArray(), c => Nearest(WindowSizes, c.WindowPercent), (c, i) => c.WindowPercent = WindowSizes[i]),
					new LinkRow(Tr.Get("apps.manage.title", "显示哪些应用"), Tr.Get("shell.settings.appsNote", "把用不上的应用从应用页收起来"), ctx => new AISV.UI.Apps.AppShelfPage(ctx))
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
					new ToggleRow(Tr.Get("options.gazette", "镇上的报纸"), Tr.Get("shell.settings.weeklyNewsNote", "每月 7、14、21、28 日晚上出一期《鹈鹕镇周报》，第二天早上送到信箱"), c => c.GazetteOn, (c, v) => c.GazetteOn = v),
					new ToggleRow(Tr.Get("shell.settings.modRumors", "接受其他 mod 的八卦"), Tr.Get("shell.settings.modRumorsNote", "其他 mod 可以交来今天发生的事，晚上一起整理"), c => c.RumorsFromMods, (c, v) => c.RumorsFromMods = v),
					new ChoiceRow(Tr.Get("shell.settings.modRumorsDaily", "每天最多收几条"), Tr.Get("shell.settings.modRumorsDailyNote", "所有 mod 加起来的数量"), ModRumorLimits.Select(n => n.ToString()).ToArray(), c => Nearest(ModRumorLimits, c.ModRumorsDaily), (c, i) => c.ModRumorsDaily = ModRumorLimits[i]) { Shown = c => c.RumorsFromMods }
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
			new Category(Tr.Get("shell.settings.home", "家里"), Tr.Get("shell.settings.homeDesc", "配偶、孩子和宠物。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.spouse", "配偶"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.kissMenu", "点配偶先选聊天还是亲吻"), Tr.Get("shell.settings.kissMenuNote", "今天还能亲（抱）的时候，配偶头顶弹出小圆盘；关掉就直接聊天，按住跳过键点配偶照样能亲"), c => c.KissChoiceMenu, (c, v) => c.KissChoiceMenu = v),
					new ToggleRow(Tr.Get("shell.settings.spousePlan", "配偶的日常安排"), Tr.Get("shell.settings.spousePlanNote", "婚后配偶每天自己安排：帮忙干农活、去镇上、看店、带孩子；手机“家务安排”里可以改"), c => c.SpousePlanner, (c, v) => c.SpousePlanner = v),
					new ChoiceRow(Tr.Get("shell.settings.spouseHelp", "帮忙勤快程度"), Tr.Get("shell.settings.spouseHelpNote", "每样活最多干多少"), new[] { Tr.Get("shell.settings.helpLight", "轻松"), Tr.Get("shell.settings.helpNormal", "一般"), Tr.Get("shell.settings.helpBusy", "勤快") },
						c => Math.Max(0, Array.IndexOf(HelpLevels, c.SpouseHelpLevel)), (c, i) => c.SpouseHelpLevel = HelpLevels[i]) { Shown = c => c.SpousePlanner },
					new ChoiceRow(Tr.Get("shell.settings.spouseHarvestTo", "收获的东西放哪"), Tr.Get("shell.settings.spouseHarvestToNote", "收获箱在手机“家务安排”里领取；放出货箱就当晚卖掉"), new[] { Tr.Get("shell.settings.toChest", "收获箱"), Tr.Get("shell.settings.toBin", "出货箱") },
						c => c.SpouseHarvestTo == "Bin" ? 1 : 0, (c, i) => c.SpouseHarvestTo = i == 1 ? "Bin" : "Chest") { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseWater", "浇水"), "", c => c.SpouseWaters, (c, v) => c.SpouseWaters = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseHarvest", "收获"), "", c => c.SpouseHarvests, (c, v) => c.SpouseHarvests = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseAnimals", "照顾动物"), Tr.Get("shell.settings.spouseAnimalsNote", "补干草、抚摸、捡蛋"), c => c.SpouseTendsAnimals, (c, v) => c.SpouseTendsAnimals = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseClear", "清理农场"), Tr.Get("shell.settings.spouseClearNote", "只清杂草、石头、树枝，不砍树，不动你放的东西"), c => c.SpouseClears, (c, v) => c.SpouseClears = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseGreenhouse", "温室"), "", c => c.SpouseGreenhouse, (c, v) => c.SpouseGreenhouse = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spousePonds", "鱼塘"), Tr.Get("shell.settings.spousePondsNote", "收鱼塘的产出"), c => c.SpouseFishPonds, (c, v) => c.SpouseFishPonds = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseMachines", "收加工机器"), Tr.Get("shell.settings.spouseMachinesNote", "把做好的东西收进收获箱"), c => c.SpouseMachines, (c, v) => c.SpouseMachines = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseOut", "去镇上"), Tr.Get("shell.settings.spouseOutNote", "找人聊天、买东西"), c => c.SpouseGoesOut, (c, v) => c.SpouseGoesOut = v) { Shown = c => c.SpousePlanner },
					new ChoiceRow(Tr.Get("shell.settings.spouseSpend", "每天最多花多少"), Tr.Get("shell.settings.spouseSpendNote", "花的是配偶自己的钱"), SpendLimits.Select(n => n + "g").ToArray(), c => Nearest(SpendLimits, c.SpouseSpendDaily), (c, i) => c.SpouseSpendDaily = SpendLimits[i]) { Shown = c => c.SpousePlanner && c.SpouseGoesOut },
					new ToggleRow(Tr.Get("shell.settings.spouseShop", "去小超市看店"), Tr.Get("shell.settings.spouseShopNote", "建了农夫小超市时，配偶会去当收银员"), c => c.SpouseMindsShop, (c, v) => c.SpouseMindsShop = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.spouseKids", "带孩子玩"), "", c => c.SpousePlaysWithKids, (c, v) => c.SpousePlaysWithKids = v) { Shown = c => c.SpousePlanner },
					new ToggleRow(Tr.Get("shell.settings.roommatePlan", "室友也这样安排"), "", c => c.RoommatePlans, (c, v) => c.RoommatePlans = v) { Shown = c => c.SpousePlanner }
				}),
				(Tr.Get("shell.settings.kids", "孩子"), new List<Row>
				{
					new ChoiceRow(Tr.Get("shell.settings.childTalk", "孩子说话"), Tr.Get("shell.settings.childTalkNote", "会走路以后：不变（游戏原来的样子）、每天一句、能来回聊几句；孩子的性格可以写在村民角色卡里"),
						new[] { Tr.Get("shell.settings.childOff", "不变"), Tr.Get("shell.settings.childLine", "每天一句"), Tr.Get("shell.settings.childChat", "能聊几句") },
						c => Math.Max(0, Array.IndexOf(ChildModes, c.ChildTalkMode)), (c, i) => c.ChildTalkMode = ChildModes[i]),
					new ToggleRow(Tr.Get("shell.settings.runaway", "孩子离家出走"), Tr.Get("shell.settings.runawayNote", "孩子偶尔跑到农场上躲起来玩，要你去找；靠近会听到笑声"), c => c.ChildRunaway, (c, v) => c.ChildRunaway = v),
					new ChoiceRow(Tr.Get("shell.settings.runawayChance", "每天跑出去的机会"), Tr.Get("shell.settings.runawayChanceNote", "上午检查一次；同一个孩子至少隔两周"), RunawayChances.Select(n => n + "%").ToArray(), c => Nearest(RunawayChances, c.ChildRunawayPercent), (c, i) => c.ChildRunawayPercent = RunawayChances[i]) { Shown = c => c.ChildRunaway }
				}),
				(Tr.Get("shell.settings.pets", "宠物"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.petThoughts", "宠物的心里话"), Tr.Get("shell.settings.petThoughtsNote", "你在宠物旁边时，它偶尔冒一句心里话（灰色泡泡）；暂时不能和宠物对话"), c => c.PetThoughts, (c, v) => c.PetThoughts = v),
					new ChoiceRow(Tr.Get("shell.settings.petDaily", "每只每天最多几句"), "", PetDaily.Select(n => n.ToString()).ToArray(), c => Nearest(PetDaily, c.PetThoughtsDaily), (c, i) => c.PetThoughtsDaily = PetDaily[i]) { Shown = c => c.PetThoughts },
					new ChoiceRow(Tr.Get("shell.settings.petChance", "多常冒一句"), Tr.Get("shell.settings.petChanceNote", "你在旁边时每 10 分钟的机会"), PetChances.Select(n => n + "%").ToArray(), c => Nearest(PetChances, c.PetThoughtsPercent), (c, i) => c.PetThoughtsPercent = PetChances[i]) { Shown = c => c.PetThoughts }
				})
			}),
			new Category(Tr.Get("shell.settings.mart", "小超市"), Tr.Get("shell.settings.martDesc", "自己的农夫小超市，和镇上的商店抢生意。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.mart", "小超市"), new List<Row>
				{
					new ToggleRow(Tr.Get("shell.settings.martOn", "农夫小超市"), Tr.Get("shell.settings.martOnNote", "去 Robin 的木匠铺建一间，放货架上架，村民会来买"), c => c.FarmerMart, (c, v) => c.FarmerMart = v),
					new ChoiceRow(Tr.Get("shell.settings.martOpen", "几点开门"), "", MartOpens.Select(Clock).ToArray(), c => Nearest(MartOpens, c.FarmerMartOpen), (c, i) => c.FarmerMartOpen = MartOpens[i]) { Shown = c => c.FarmerMart },
					new ChoiceRow(Tr.Get("shell.settings.martClose", "几点打烊"), "", MartCloses.Select(Clock).ToArray(), c => Nearest(MartCloses, c.FarmerMartClose), (c, i) => c.FarmerMartClose = MartCloses[i]) { Shown = c => c.FarmerMart },
					new ToggleRow(Tr.Get("shell.settings.martCheckout", "收银小游戏"), Tr.Get("shell.settings.martCheckoutNote", "你在店里时顾客到柜台排队，点柜台收银找零，又快又对有小费；关掉就自动结账"), c => c.MartCheckoutGame, (c, v) => c.MartCheckoutGame = v) { Shown = c => c.FarmerMart },
					new ToggleRow(Tr.Get("shell.settings.martCompete", "和 Pierre、Joja 抢生意"), Tr.Get("shell.settings.martCompeteNote", "抢得多了他们会吃紧，甚至停业整顿；只在 AI星露谷 的经济里，不改原版剧情，种子一定买得到"), c => c.MartCompetition, (c, v) => c.MartCompetition = v) { Shown = c => c.FarmerMart },
					new ToggleRow(Tr.Get("shell.settings.brawl", "上门理论时可以动手"), Tr.Get("shell.settings.brawlNote", "像原版过场那样一拳，不伤人；会被罚款、上周报"), c => c.AllowBrawl, (c, v) => c.AllowBrawl = v) { Shown = c => c.FarmerMart && c.MartCompetition },
					new ToggleRow(Tr.Get("shell.settings.stolenNoticed", "赃物可能被认出来"), Tr.Get("shell.settings.stolenNoticedNote", "偷来的东西放在店里卖，失主和熟人可能认出来"), c => c.StolenGoodsNoticed, (c, v) => c.StolenGoodsNoticed = v),
					new ToggleRow(Tr.Get("shell.settings.fenceArrest", "屡次卖赃物会被带走"), Tr.Get("shell.settings.fenceArrestNote", "同一季第三次被认出卖赃物：赃物没收、被带走（装了 Law and Order 关进牢里），当天结束。关掉只罚款、停业"), c => c.FenceArrest, (c, v) => c.FenceArrest = v),
					new ToggleRow(Tr.Get("shell.settings.cosLink", "和 Farmer's Store 对接"), Tr.Get("shell.settings.cosLinkNote", "装了 Farmer's Store 时：两边不抢村民，它那边的成交也记进村民账本；两家店可以同时开"), c => c.FarmersStoreLink, (c, v) => c.FarmersStoreLink = v)
				})
			}),
			new Category(Tr.Get("shell.settings.keys", "快捷键"), Tr.Get("shell.settings.keysDesc", "在 Generic Mod Config Menu 或 config.json 里改。"), new List<(string, List<Row>)>
			{
				(Tr.Get("shell.settings.keysGroup", "按键"), new List<Row>
				{
					new InfoRow(Tr.Get("shell.settings.shellKey", "打开这个界面"), "", c => c.KeyShell),
					new InfoRow(Tr.Get("shell.settings.phoneKey", "打开手机"), "", c => c.KeyPhone),
					new InfoRow(Tr.Get("shell.settings.profileKey", "我的设定"), "", c => c.KeyAboutMe),
					new InfoRow(Tr.Get("shell.settings.gossipKey", "直接看八卦"), "", c => c.KeyRumors),
					new InfoRow(Tr.Get("shell.settings.groupKey", "群聊插话"), "", c => c.KeyGroupTalk),
					new InfoRow(Tr.Get("shell.settings.vanillaKey", "按住点村民 = 游戏原来的互动"), "", c => c.VanillaInteractKey)
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

	private static string Clock(int time) => $"{time / 100}:{time % 100:00}";

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (_search.Selected)
		{
			if (key == Keys.Escape)
			{
				if (Query.Length > 0)
				{
					_search.Text = "";
				}
				else
				{
					_search.Selected = false;
				}
			}
			return true;
		}
		return _scroll.PageKeys(key, _detail.Height - 80);
	}

	public override void Closed() => _search.Selected = false;

	private bool _reading;

	private int _narrowTick = -10;

	public override bool StepBack()
	{
		bool was = _reading && Game1.ticks - _narrowTick <= 2;
		_reading &= !was;
		return was;
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_hoverNote = null;
		DrawPage(b, area, hits);
		if (_hoverNote != null)
		{
			IClickableMenu.drawHoverText(b, Game1.parseText(_hoverNote, Game1.smallFont, Math.Min(640, area.Width - 80)), Game1.smallFont);
		}
	}

	private string? _hoverNote;

	private void DrawPage(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (area.Width < 760)
		{
			DrawNarrow(b, area, hits);
			return;
		}
		Rectangle nav = new Rectangle(area.X, area.Y, Math.Clamp(area.Width / 4, 240, 300), area.Height);
		_detail = new Rectangle(nav.Right, area.Y, area.Right - nav.Right, area.Height);
		DrawNav(b, nav, hits);
		DrawDetail(b, hits);
	}

	/// <summary>窄屏时分类占满一屏，点进去才看设置；打字搜索时搜索框下面直接列结果。</summary>
	private void DrawNarrow(SpriteBatch b, Rectangle area, Hits hits)
	{
		_narrowTick = Game1.ticks;
		if (Query.Length > 0)
		{
			Rectangle top = new Rectangle(area.X, area.Y, area.Width, 66);
			_detail = new Rectangle(area.X, top.Bottom, area.Width, area.Bottom - top.Bottom);
			DrawNav(b, top, hits);
			DrawDetail(b, hits);
		}
		else if (_reading)
		{
			Rectangle back = new Rectangle(area.X + 10, area.Y + 8, 170, 40);
			Hearth.PressPlate(b, back, "‹ " + Tr.Get("shell.settings.allGroups", "所有分类"), hover: Hearth.Hover(back), scale: 0.78f);
			hits.Add(back, () => _reading = false);
			_detail = new Rectangle(area.X, back.Bottom + 4, area.Width, area.Bottom - back.Bottom - 4);
			DrawDetail(b, hits);
		}
		else
		{
			DrawNav(b, area, hits);
		}
	}

	private void DrawNav(SpriteBatch b, Rectangle nav, Hits hits)
	{
		Hearth.Fill(b, nav, Hearth.ParchmentDeep);
		Hearth.Fill(b, new Rectangle(nav.Right - 4, nav.Y, 4, nav.Height), Hearth.Bark * 0.5f);
		string q = Query;
		Rectangle box = new Rectangle(nav.X + 8, nav.Y + 14, nav.Width - 20, 44);
		Hearth.Field(b, box, _search.Selected);
		Hearth.Magnifier(b, new Vector2(box.X + 20, box.Center.Y));
		Vector2 tp = new Vector2(box.X + 40, box.Center.Y - Hearth.LineHeight(0.8f) / 2);
		Hearth.Ink(b, q.Length == 0 && !_search.Selected ? Tr.Get("shell.settings.search", "搜索设置") : Hearth.Truncate(q, box.Width - 52, 0.8f), tp, q.Length == 0 ? Hearth.QuillPale : Hearth.Quill, 0.8f);
		hits.Add(box, () => _search.Selected = true);
		int y = box.Bottom + 12;
		for (int i = 0; i < _categories.Count && y < nav.Bottom; i++)
		{
			Rectangle r = new Rectangle(nav.X + 8, y, nav.Width - 20, 56);
			bool sel = q.Length == 0 && i == _current;
			if (sel || Hearth.Hover(r))
			{
				Hearth.FillRounded(b, r, sel ? Hearth.Honey : Hearth.ParchmentDeep, 10);
			}
			if (sel)
			{
				Hearth.FillRounded(b, new Rectangle(r.X, r.Y + 10, 4, r.Height - 20), Hearth.Leaf, 2);
			}
			Hearth.Ink(b, _categories[i].Title, new Vector2(r.X + 18, r.Y + 13), sel ? Hearth.LeafDeep : Hearth.Quill, 0.88f);
			Hearth.Chevron(b, new Vector2(r.Right - 20, r.Center.Y), left: false, Hearth.QuillPale);
			int index = i;
			hits.Add(r, () =>
			{
				bool searching = Query.Length > 0;
				_reading = true;
				_search.Text = "";
				_search.Selected = false;
				_resetArmedUntil = 0;
				if (_current != index || searching)
				{
					Game1.playSound("smallSelect");
					_current = index;
					_scroll.Reset();
				}
			});
			y += 60;
		}
	}

	/// <summary>搜索结果按原来的分类和分组排；同一个开关在两处出现时只留第一处。</summary>
	private Category Results(string query, ModConfig config)
	{
		List<(string, List<Row>)> groups = new List<(string, List<Row>)>();
		HashSet<(string, string)> seen = new HashSet<(string, string)>();
		foreach (Category cat in _categories)
		{
			foreach ((string group, List<Row> rows) in cat.Groups)
			{
				List<Row> found = rows.Where(r => (r.Shown?.Invoke(config) ?? true) && SettingsSearch.Matches(query, r.Label, r.Note, group, cat.Title) && seen.Add((r.Label, r.Note))).ToList();
				if (found.Count > 0)
				{
					groups.Add((cat.Title == group ? group : cat.Title + " · " + group, found));
				}
			}
		}
		int count = groups.Sum(g => g.Item2.Count);
		return new Category(Tr.Get("shell.settings.searchTitle", "搜索结果"), count > 0 ? Tr.Get("shell.settings.searchFound", "找到 {{n}} 项。", ("n", count)) : Tr.Get("shell.settings.searchNone", "没有找到，换个词试试。"), groups);
	}

	private void DrawDetail(SpriteBatch b, Hits hits)
	{
		ModConfig config = Ctx.Config();
		string q = Query;
		if (q != _lastQuery)
		{
			_lastQuery = q;
			_scroll.Reset();
		}
		Category cat = q.Length > 0 ? Results(q, config) : _categories[_current];
		Hearth.BeginClip(b, _detail);
		int w = Math.Min(_detail.Width - 64, 820);
		int x = _detail.X + (_detail.Width - w) / 2;
		int y = _detail.Y + 22 - _scroll.Offset;
		Hearth.InkBold(b, cat.Title, new Vector2(x, y), Hearth.Quill, 1.15f);
		if (q.Length == 0 && cat.Groups.Any(g => g.Rows.Any(r => r is ToggleRow or ChoiceRow)))
		{
			DrawResetButton(b, cat, x + w, y - 4, hits);
		}
		y += Hearth.LineHeight(1.15f) + 4;
		Hearth.Ink(b, cat.Description, new Vector2(x, y), Hearth.QuillSoft, 0.78f);
		y += 44;
		foreach ((string group, List<Row> all) in cat.Groups)
		{
			List<Row> rows = all.Where(r => r.Shown?.Invoke(config) ?? true).ToList();
			if (rows.Count == 0)
			{
				continue;
			}
			Hearth.Ribbon(b, group, x + 12, y - 2);
			y += 44;
			Rectangle card = new Rectangle(x, y, w, rows.Count * 76 + 8);
			Hearth.CardSlip(b, card);
			for (int i = 0; i < rows.Count; i++)
			{
				Rectangle r = new Rectangle(x + 4, y + 4 + i * 76, w - 8, 76);
				if (i > 0)
				{
					Hearth.Dotted(b, r.X + 16, r.Y, r.Width - 32, leaf: true);
				}
				DrawRow(b, rows[i], r, config, hits);
			}
			y = card.Bottom + 22;
		}
		Hearth.Ink(b, Tr.Get("shell.settings.more", "更细的数值（每天几封信、八卦条数等）可以在 Generic Mod Config Menu 里调。"), new Vector2(x + 6, y), Hearth.QuillPale, 0.72f);
		y += 40;
		Hearth.EndClip(b);
		_scroll.Finish(b, _detail, y);
	}

	private void DrawRow(SpriteBatch b, Row row, Rectangle r, ModConfig config, Hits hits)
	{
		if (row is ToggleRow or LinkRow && Hearth.Hover(r, _detail))
		{
			Hearth.FillRounded(b, Hearth.Inflate(r, -2), Hearth.Honey, 10);
		}
		string note = row.Status?.Invoke(config) is { Length: > 0 } status ? status : row.Note;
		int ty = note.Length > 0 ? r.Y + 8 : r.Y + 24;
		Hearth.Ink(b, row.Label, new Vector2(r.X + 20, ty), Hearth.Quill, 0.88f);
		if (note.Length > 0)
		{
			string shown = Hearth.Truncate(note, r.Width - 300, 0.7f);
			Hearth.Ink(b, shown, new Vector2(r.X + 20, r.Y + 38), Hearth.QuillPale, 0.7f);
			// 说明一行放不下被截掉时，鼠标停在这一行上显示全文（英文、德文常常放不下）。
			if (shown != note && Hearth.Hover(r, _detail))
			{
				_hoverNote = note;
			}
		}
		switch (row)
		{
		case ToggleRow t:
		{
			bool on = t.Get(config);
			Rectangle sw = new Rectangle(r.Right - 20 - 64, r.Center.Y - 18, 64, 36);
			Hearth.Latch(b, sw, on);
			hits.Add(r, () => Change(c => t.Set(c, !on)), _detail);
			break;
		}
		case ChoiceRow ch:
		{
			int index = Math.Clamp(ch.Get(config), 0, ch.Options.Length - 1);
			string label = ch.Options[index];
			int bw = (int)Hearth.Width(label, 0.8f) + 56;
			Rectangle btn = new Rectangle(r.Right - 20 - bw, r.Center.Y - 22, bw, 44);
			Hearth.PressPlate(b, btn, label, hover: Hearth.Hover(btn, _detail), scale: 0.8f);
			Hearth.Chevron(b, new Vector2(btn.Right - 18, btn.Center.Y), left: false, Hearth.QuillPale);
			hits.Add(btn, () => Change(c => ch.Set(c, (index + 1) % ch.Options.Length)), _detail);
			break;
		}
		case LinkRow link:
		{
			Hearth.Chevron(b, new Vector2(r.Right - 30, r.Center.Y), left: false, Hearth.LeafDeep);
			hits.Add(r, () =>
			{
				_search.Selected = false;
				Host.Push(link.Page(Ctx));
			}, _detail);
			break;
		}
		case InfoRow info:
		{
			string value = info.Value(config);
			int cw = Hearth.TagWidth(value, 0.8f);
			Hearth.Tag(b, new Vector2(r.Right - 20 - cw, r.Center.Y - 16), value, Hearth.ParchmentDeep, Hearth.Quill, 0.8f);
			break;
		}
		}
	}

	/// <summary>点一次变成“再点一次确认”，几秒内再点才恢复。</summary>
	private void DrawResetButton(SpriteBatch b, Category cat, int right, int y, Hits hits)
	{
		bool armed = Game1.ticks < _resetArmedUntil;
		string label = armed ? Tr.Get("shell.settings.resetConfirm", "再点一次确认") : Tr.Get("shell.settings.reset", "恢复本页默认");
		int bw = (int)Hearth.Width(label, 0.76f) + 36;
		Rectangle btn = new Rectangle(right - bw, y, bw, 42);
		Hearth.PressPlate(b, btn, label, primary: armed, hover: Hearth.Hover(btn, _detail), scale: 0.76f);
		hits.Add(btn, () =>
		{
			if (!armed)
			{
				Game1.playSound("smallSelect");
				_resetArmedUntil = Game1.ticks + 300;
				return;
			}
			_resetArmedUntil = 0;
			Reset(cat);
		}, _detail);
	}

	/// <summary>只恢复这一页的开关和选项，包括现在没显示出来的；账号、连接这些链接进去的页面不动。</summary>
	private void Reset(Category cat)
	{
		ModConfig defaults = new ModConfig();
		Change(c =>
		{
			foreach (Row row in cat.Groups.SelectMany(g => g.Rows))
			{
				switch (row)
				{
				case ToggleRow t:
					t.Set(c, t.Get(defaults));
					break;
				case ChoiceRow ch:
					ch.Set(c, ch.Get(defaults));
					break;
				}
			}
		});
		Host.Notice(Tr.Get("shell.settings.resetDone", "这一页已恢复默认"));
	}

	private void Change(Action<ModConfig> edit)
	{
		Game1.playSound("drumkit6");
		ModConfig c = Ctx.Config();
		edit(c);
		Ctx.SaveConfig(c.Normalize());
	}
}
