using System;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Text;
using StardewModdingAPI;

namespace AISV.Integrations.Gmcm;

/// <summary>
/// 在 GMCM 里登记 AI 连接相关的设置（装了 GMCM 才有）。完整的设置页在 AI星露谷 自己的外壳里，这里只放连接和调试项。
/// </summary>
public static class GmcmSettings
{
	public static void Register(IModHelper helper, IManifest manifest, Func<ModConfig> get, Action<ModConfig> replace, bool hasServer, Action<ModConfig, bool>? imeChanged = null)
	{
		IGenericModConfigMenuApi? api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
		if (api == null)
		{
			return;
		}
		api.Register(manifest, reset: () => replace(new ModConfig()), save: () => helper.WriteConfig(get().Normalize()));
		api.AddBoolOption(manifest, () => get().ModOn, v => get().ModOn = v, () => Tr.Get("config.enable", "启用 AI 村民"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.connection", "AI 连接"));
		if (hasServer)
		{
			api.AddTextOption(manifest, () => get().AiConnectionMode, v => get().AiConnectionMode = v, () => Tr.Get("config.mode", "连接方式"),
				allowedValues: new[] { "server", "own" },
				formatAllowedValue: v => v == "own" ? Tr.Get("config.mode.own", "我自己的 AI 网关") : Tr.Get("config.mode.server", "AI星露谷服务器"));
		}
		api.AddTextOption(manifest, () => get().CustomLlmBaseUrl, v => get().CustomLlmBaseUrl = v.Trim(), () => Tr.Get("config.baseUrl", "网关地址"));
		api.AddTextOption(manifest, () => get().CustomLlmApiKey, v => get().CustomLlmApiKey = v.Trim(), () => Tr.Get("config.apiKey", "网关 key"));
		api.AddTextOption(manifest, () => get().CustomLlmModel, v => get().CustomLlmModel = v.Trim(), () => Tr.Get("config.model", "对话模型"));
		api.AddTextOption(manifest, () => get().ChatBackupModels, v => get().ChatBackupModels = v, () => Tr.Get("config.fallbacks", "对话备用模型（逗号分隔）"));
		api.AddTextOption(manifest, () => get().NightlyModel, v => get().NightlyModel = v.Trim(), () => Tr.Get("config.bgModel", "后台模型（空 = 同对话模型）"));
		api.AddTextOption(manifest, () => SlotRouter.NormalizeEffort(get().ChatThinking), v => get().ChatThinking = v, () => Tr.Get("config.effort", "对话思考档位"), allowedValues: SlotRouter.ReasoningEfforts);
		api.AddNumberOption(manifest, () => get().ChatWaitSeconds, v => get().ChatWaitSeconds = v, () => Tr.Get("config.timeout", "对话超时（秒）"), min: 5, max: 300);
		api.AddNumberOption(manifest, () => get().NightlyWaitSeconds, v => get().NightlyWaitSeconds = v, () => Tr.Get("config.bgTimeout", "后台超时（秒）"), min: 5, max: 300);
		api.AddNumberOption(manifest, () => get().NightlyCallsPerDay, v => get().NightlyCallsPerDay = v, () => Tr.Get("config.bgDaily", "每天后台请求上限（自己的网关，0 = 不限）"), min: 0, max: 2000);

		api.AddSectionTitle(manifest, () => Tr.Get("config.dialogue", "对话"));
		api.AddBoolOption(manifest, () => get().AiOn, v => get().AiOn = v, () => Tr.Get("config.aiFeatures", "用 AI 说话"));
		api.AddNumberOption(manifest, () => get().RepliesPerTalk, v => get().RepliesPerTalk = v, () => Tr.Get("config.turns", "每场对话最多回几次"), min: 1, max: 50);
		api.AddNumberOption(manifest, () => get().MinutesPerReply, v => get().MinutesPerReply = v, () => Tr.Get("config.talkMinutes", "聊一轮花几分钟（0 = 不花时间）"), min: 0, max: 60, interval: 5);
		api.AddNumberOption(manifest, () => get().SpouseTalksDaily, v => get().SpouseTalksDaily = v, () => Tr.Get("config.spouseTalks", "配偶每天最多聊几场"), min: 1, max: 10);
		api.AddNumberOption(manifest, () => get().ReplyLength, v => get().ReplyLength = v, () => Tr.Get("config.replyChars", "村民每句话最多几个字"), min: 50, max: 2000, interval: 25);
		api.AddNumberOption(manifest, () => get().RecentTurnsInPrompt, v => get().RecentTurnsInPrompt = v, () => Tr.Get("config.memory", "带给 AI 的对话记录条数"), min: 1, max: 200);
		api.AddNumberOption(manifest, () => get().PointsCapPerReply, v => get().PointsCapPerReply = v, () => Tr.Get("config.maxPoints", "每次回复最多加减多少好感"), min: 0, max: 250);
		api.AddNumberOption(manifest, () => get().WalkAwayPoints, v => get().WalkAwayPoints = v, () => Tr.Get("config.skipPoints", "不回话时加多少好感"), min: 0, max: 100);
		api.AddBoolOption(manifest, () => get().HostilityOn, v => get().HostilityOn = v, () => Tr.Get("config.enmity", "说话太冲会结仇"),
			() => Tr.Get("config.enmity.tip", "冒犯的话会积累敌意，敌意会抵掉好感心数；好好说话能慢慢化解。"));
		api.AddBoolOption(manifest, () => get().FeedGameLineToAi, v => get().FeedGameLineToAi = v, () => Tr.Get("config.canon", "参考游戏原来的台词"),
			() => Tr.Get("config.canon.tip", "把村民这一刻本来要说的话交给 AI 当话题参考，聊天更贴近原作。"));
		api.AddBoolOption(manifest, () => get().GameLinesWhenAiDown, v => get().GameLinesWhenAiDown = v, () => Tr.Get("config.allowVanilla", "AI 用不了时说游戏原来的台词"));
		api.AddBoolOption(manifest, () => get().PauseClockWhileWriting, v => get().PauseClockWhileWriting = v, () => Tr.Get("config.freeze", "等 AI 想台词时暂停时间（单人）"));
		api.AddTextOption(manifest, () => get().SpeechLanguage.ToString(), v => get().SpeechLanguage = Enum.TryParse(v, out LanguageMode m) ? m : LanguageMode.GameLanguage, () => Tr.Get("config.language", "AI 说的语言"),
			allowedValues: Enum.GetNames(typeof(LanguageMode)),
			formatAllowedValue: v => v switch
			{
				"Vietnamese" => Tr.Get("config.language.vi", "越南语"),
				"Custom" => Tr.Get("config.language.custom", "自己填"),
				_ => Tr.Get("config.language.game", "跟游戏语言")
			});
		api.AddTextOption(manifest, () => get().SpeechLanguageName, v => get().SpeechLanguageName = v.Trim(), () => Tr.Get("config.customLanguage", "自己填的语言名"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.favors", "拜托和表白"));
		api.AddNumberOption(manifest, () => get().WishOddsPercent, v => get().WishOddsPercent = v, () => Tr.Get("config.itemRequestChance", "村民托你找东西的机会（%）"), min: 0, max: 100);
		api.AddNumberOption(manifest, () => get().WishRestDays, v => get().WishRestDays = v, () => Tr.Get("config.itemRequestCooldown", "同一人托完隔几天再托"), min: 0, max: 28);
		api.AddBoolOption(manifest, () => get().CourtingOn, v => get().CourtingOn = v, () => Tr.Get("config.proposals", "对话里表白、求婚"),
			() => Tr.Get("config.proposals.tip", "交情够了，回复框里会出现开关；这句话会当作表白或求婚，答不答应由村民决定。"));
		api.AddBoolOption(manifest, () => get().AskToDateOn, v => get().AskToDateOn = v, () => Tr.Get("config.proposeDating", "可以表白（8 心以上）"));
		api.AddBoolOption(manifest, () => get().AskToMarryOn, v => get().AskToMarryOn = v, () => Tr.Get("config.proposeMarriage", "可以求婚（交往中、10 心以上）"));
		api.AddBoolOption(manifest, () => get().CourtWithoutTokens, v => get().CourtWithoutTokens = v, () => Tr.Get("config.proposeNoItems", "不用花束、吊坠也行"));
		api.AddBoolOption(manifest, () => get().MarryNeedsHouseUpgrade, v => get().MarryNeedsHouseUpgrade = v, () => Tr.Get("config.proposeHouse", "求婚要先扩建房子"));
		api.AddBoolOption(manifest, () => get().CourtStorySingles, v => get().CourtStorySingles = v, () => Tr.Get("config.proposeStorySingle", "故事里单身了的已婚村民也能表白"));
		api.AddBoolOption(manifest, () => get().HostOnlyCourting, v => get().HostOnlyCourting = v, () => Tr.Get("config.proposeFarmhand", "联机时农场工不能在对话里表白"));
		api.AddNumberOption(manifest, () => get().CourtRestAfterNo, v => get().CourtRestAfterNo = v, () => Tr.Get("config.proposeCooldown", "被拒后隔几天再开口"), min: 1, max: 28);
		api.AddNumberOption(manifest, () => get().CourtRestAfterLine, v => get().CourtRestAfterLine = v, () => Tr.Get("config.proposeBoundary", "对方划了界线时隔几天"), min: 1, max: 112);

		api.AddSectionTitle(manifest, () => Tr.Get("config.outings", "约出去"));
		api.AddBoolOption(manifest, () => get().OutingsOn, v => get().OutingsOn = v, () => Tr.Get("config.inviteOut", "回复框里可以约出去"),
			() => Tr.Get("config.inviteOut.tip", "这句话会当作邀约，去不去、去哪由村民决定；答应了就在约好的地方演一小段。"));
		api.AddNumberOption(manifest, () => get().OutingHearts, v => get().OutingHearts = v, () => Tr.Get("config.inviteOutHearts", "几心起能约"), min: 0, max: 14);
		api.AddNumberOption(manifest, () => get().OutingRestAfterYes, v => get().OutingRestAfterYes = v, () => Tr.Get("config.inviteOutAccepted", "答应后隔几天再约"), min: 0, max: 28);
		api.AddNumberOption(manifest, () => get().OutingRestAfterNo, v => get().OutingRestAfterNo = v, () => Tr.Get("config.inviteOutRejected", "被拒后隔几天再约"), min: 0, max: 28);
		api.AddNumberOption(manifest, () => get().OutingPoints, v => get().OutingPoints = v, () => Tr.Get("config.inviteOutFriendship", "一起出去加的好感"), min: 0, max: 100, interval: 5);
		api.AddNumberOption(manifest, () => get().SpouseAsksOutPercent, v => get().SpouseAsksOutPercent = v, () => Tr.Get("config.spouseOffer", "配偶主动约你的机会（%，最多按 30 算）"), min: 0, max: 100);
		api.AddBoolOption(manifest, () => get().SpouseIntimacyOn, v => get().SpouseIntimacyOn = v, () => Tr.Get("config.hotSpouse", "和配偶的亲密场景（不露骨）"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.scenes", "偶遇"));
		api.AddBoolOption(manifest, () => get().EncountersOn, v => get().EncountersOn = v, () => Tr.Get("config.sceneEvents", "镇上的偶遇"),
			() => Tr.Get("config.sceneEvents.tip", "每天挑几段村民的小场景，你到了那里就会碰上。"));
		api.AddTextOption(manifest, () => AISV.Core.Scenes.SceneStyle.Normalize(get().EncounterStyle), v => get().EncounterStyle = v, () => Tr.Get("config.sceneStyle", "怎么演"),
			allowedValues: AISV.Core.Scenes.SceneStyle.All,
			formatAllowedValue: v => v switch { "AmbientOnly" => Tr.Get("config.sceneAmbient", "只旁听"), "Cinematic" => Tr.Get("config.sceneCinematic", "过场"), _ => Tr.Get("config.sceneNatural", "自然（一个人走过来，几个人旁听）") });
		api.AddNumberOption(manifest, () => get().EncountersDaily, v => get().EncountersDaily = v, () => Tr.Get("config.scenesPerDay", "每天最多几段"), min: 0, max: 5);
		api.AddBoolOption(manifest, () => get().EncountersRemembered, v => get().EncountersRemembered = v, () => Tr.Get("config.sceneMemory", "演完后记住、传成八卦"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.arcs", "小剧场"));
		api.AddNumberOption(manifest, () => get().StorylinesAtOnce, v => get().StorylinesAtOnce = v, () => Tr.Get("config.arcLimit", "同时进行几出"), min: 1, max: 5);
		api.AddBoolOption(manifest, () => get().FinaleScenesOn, v => get().FinaleScenesOn = v, () => Tr.Get("config.arcScene", "收尾时在游戏里演一段"));
		api.AddNumberOption(manifest, () => get().AfterwordDays, v => get().AfterwordDays = v, () => Tr.Get("config.arcPostDays", "结局后几天内村民先聊这件事"), min: 1, max: 14);
		api.AddNumberOption(manifest, () => get().StorylineStaleSeasons, v => get().StorylineStaleSeasons = v, () => Tr.Get("config.arcFizzle", "几季没进展算不了了之"), min: 1, max: 8);
		api.AddBoolOption(manifest, () => get().DepartureStoriesOn, v => get().DepartureStoriesOn = v, () => Tr.Get("config.leaveTown", "小剧场可能以有人离开小镇收场"),
			() => Tr.Get("config.leaveTown.tip", "Jas、Vincent、Leo 和你的配偶永远不会被逐出。"));
		api.AddNumberOption(manifest, () => get().DepartureHintPercent, v => get().DepartureHintPercent = v, () => Tr.Get("config.leaveTownChance", "离开小镇的故事出现的机会（%）"), min: 0, max: 100);
		api.AddNumberOption(manifest, () => get().DepartureGrudgePercent, v => get().DepartureGrudgePercent = v, () => Tr.Get("config.leaveTownThreshold", "全镇敌意到多少（%）算候选"), min: 0, max: 100, interval: 5);
		api.AddBoolOption(manifest, () => get().ReturnStoriesOn, v => get().ReturnStoriesOn = v, () => Tr.Get("config.leaveTownReturn", "离开的人可能回来"));
		api.AddNumberOption(manifest, () => get().ReturnHintPercent, v => get().ReturnHintPercent = v, () => Tr.Get("config.leaveTownReturnChance", "回来的故事出现的机会（%）"), min: 0, max: 100);

		api.AddSectionTitle(manifest, () => Tr.Get("config.social", "镇上的生活"));
		api.AddTextOption(manifest, () => get().KeyShell, v => get().KeyShell = v.Trim(), () => Tr.Get("config.shellKey", "打开外壳的按键"));
		api.AddTextOption(manifest, () => get().KeyRumors, v => get().KeyRumors = v.Trim(), () => Tr.Get("config.gossipKey", "打开八卦页的按键"));
		api.AddTextOption(manifest, () => get().KeyAboutMe, v => get().KeyAboutMe = v.Trim(), () => Tr.Get("config.profileKey", "打开“我的设定”的按键"));
		api.AddBoolOption(manifest, () => get().RumorsOn, v => get().RumorsOn = v, () => Tr.Get("config.gossip", "八卦和日记"));
		api.AddTextOption(manifest, () => AISV.Core.Social.GossipFocus.Normalize(get().RumorSubject), v => get().RumorSubject = v, () => Tr.Get("config.gossipFocus", "八卦说谁"),
			allowedValues: new[] { AISV.Core.Social.GossipFocus.Normal, AISV.Core.Social.GossipFocus.Balanced, AISV.Core.Social.GossipFocus.VillagersOnly },
			formatAllowedValue: v => v switch { "Normal" => Tr.Get("config.focusNormal", "多说你的事"), "VillagersOnly" => Tr.Get("config.focusVillagers", "只说村民的事"), _ => Tr.Get("config.focusBalanced", "你和村民各半") });
		api.AddNumberOption(manifest, () => get().RumorsInPrompt, v => get().RumorsInPrompt = v, () => Tr.Get("config.maxGossip", "AI 记住的八卦条数"), min: 5, max: 100);
		api.AddNumberOption(manifest, () => get().RumorTidyDays, v => get().RumorTidyDays = v, () => Tr.Get("config.compaction", "每隔几天精简一次八卦（0 = 每月 1 日）"), min: 0, max: 112);
		api.AddBoolOption(manifest, () => get().MailOn, v => get().MailOn = v, () => Tr.Get("config.letters", "村民来信"));
		api.AddNumberOption(manifest, () => get().MailDaily, v => get().MailDaily = v, () => Tr.Get("config.lettersPerDay", "每天最多几封信"), min: 1, max: 5);
		api.AddNumberOption(manifest, () => get().MailRestDays, v => get().MailRestDays = v, () => Tr.Get("config.letterCooldown", "同一位村民两封信至少隔几天"), min: 1, max: 56);
		api.AddNumberOption(manifest, () => get().MailMissingDays, v => get().MailMissingDays = v, () => Tr.Get("config.missYou", "几天没聊会寄\"想你了\""), min: 3, max: 56);
		api.AddBoolOption(manifest, () => get().MailAfterHeartScene, v => get().MailAfterHeartScene = v, () => Tr.Get("config.eventLetters", "事后来信"));
		api.AddBoolOption(manifest, () => get().FestivalTalkOn, v => get().FestivalTalkOn = v, () => Tr.Get("config.festival", "节日上用 AI 说话"));
		api.AddBoolOption(manifest, () => get().VillagerMeetupsOn, v => get().VillagerMeetupsOn = v, () => Tr.Get("config.npcInteractions", "村民之间的来往"));
		api.AddBoolOption(manifest, () => get().VillagerBondsShift, v => get().VillagerBondsShift = v, () => Tr.Get("config.friendships", "村民关系会变"));
		api.AddNumberOption(manifest, () => get().VillagerMeetupsDaily, v => get().VillagerMeetupsDaily = v, () => Tr.Get("config.interactionsPerDay", "每天村民之间几件事"), min: 1, max: 5);
		api.AddNumberOption(manifest, () => get().MeetupSpicePercent, v => get().MeetupSpicePercent = v, () => Tr.Get("config.chaos", "村民之间的事有多出人意料（0～100）"), min: 0, max: 100);
		api.AddBoolOption(manifest, () => get().MeetupsBecomeRumors, v => get().MeetupsBecomeRumors = v, () => Tr.Get("config.socialGossip", "村民的事也传八卦"));
		api.AddBoolOption(manifest, () => get().TemperamentShifts, v => get().TemperamentShifts = v, () => Tr.Get("config.personality", "性格慢慢变化"));
		api.AddBoolOption(manifest, () => get().VillagerCouplesOn, v => get().VillagerCouplesOn = v, () => Tr.Get("config.npcRomance", "村民之间可能产生好感"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.drama", "是非和传闻"));
		api.AddBoolOption(manifest, () => get().DramaOn, v => get().DramaOn = v, () => Tr.Get("config.dramaOn", "村民之间的是非"),
			() => Tr.Get("config.dramaOn.tip", "八卦和你的话会让村民对彼此有看法，传闻也会越传越广。"));
		api.AddTextOption(manifest, () => AISV.Core.Social.Drama.DramaRules.Intensity(get().DramaHeat), v => get().DramaHeat = v, () => Tr.Get("config.dramaIntensity", "戏有多浓"),
			allowedValues: new[] { AISV.Core.Social.Drama.DramaRules.Cozy, AISV.Core.Social.Drama.DramaRules.Balanced, AISV.Core.Social.Drama.DramaRules.Dramatic },
			formatAllowedValue: v => v switch { "Cozy" => Tr.Get("config.dramaCozy", "温和"), "Dramatic" => Tr.Get("config.dramaDramatic", "跌宕"), _ => Tr.Get("config.dramaBalanced", "平常") });
		api.AddBoolOption(manifest, () => get().RumorsMoveOpinions, v => get().RumorsMoveOpinions = v, () => Tr.Get("config.dramaGossip", "八卦会改变看法"));
		api.AddBoolOption(manifest, () => get().FarmerCanSway, v => get().FarmerCanSway = v, () => Tr.Get("config.dramaPlayer", "你在村民面前说的话也算数"));
		api.AddBoolOption(manifest, () => get().BackbitingOn, v => get().BackbitingOn = v, () => Tr.Get("config.dramaBadmouth", "背后说人"));
		api.AddNumberOption(manifest, () => get().DramaBeatsDaily, v => get().DramaBeatsDaily = v, () => Tr.Get("config.dramaBeats", "每天最多几个剧情片段"), min: 1, max: 5);
		api.AddBoolOption(manifest, () => get().AdultModuleOn, v => get().AdultModuleOn = v, () => Tr.Get("config.moduleAdult", "成人模块"),
			() => Tr.Get("config.moduleAdult.tip", "关闭时，所有成人向开关都不生效（各自的设置值会保留，重新开启后恢复）。"));
		api.AddBoolOption(manifest, () => get().AdultDramaAllowed, v => get().AdultDramaAllowed = v, () => Tr.Get("config.adultDrama", "成人关系戏（不露骨）"),
			() => Tr.Get("config.adultDrama.tip", "成年村民之间可以有嫉妒、愧疚、夫妻闹别扭；孩子不会有。"));
		api.AddBoolOption(manifest, () => get().WaveringSpousesOn, v => get().WaveringSpousesOn = v, () => Tr.Get("config.infidelity", "允许已婚的人动摇（要先开成人关系戏）"));
		api.AddBoolOption(manifest, () => get().SafeguardGameCouples, v => get().SafeguardGameCouples = v, () => Tr.Get("config.protectMarriages", "保护原作里的夫妻"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.world", "世界"));
		api.AddBoolOption(manifest, () => get().WorldModuleOn, v => get().WorldModuleOn = v, () => Tr.Get("config.moduleWorld", "世界模块"),
			() => Tr.Get("config.moduleWorld.tip", "市场行情、委托、生态读取、周报扩展等世界玩法。"));
		api.AddBoolOption(manifest, () => get().MarketOn, v => get().MarketOn = v, () => Tr.Get("config.market", "出货价按行情变化"),
			() => Tr.Get("config.market.tip", "作物和加工品的出货箱结算价每天按行情浮动（原价的 0.8~1.4 倍，每天最多变 10%）。直接卖给商店不受影响。关掉后立即按原价结算。"));
		api.AddBoolOption(manifest, () => get().JobBoardOn, v => get().JobBoardOn = v, () => Tr.Get("config.commissions", "村民在委托板上贴委托"),
			() => Tr.Get("config.commissions.tip", "每天早上村民在委托板上贴新委托（送物品、采集、挖矿、拜访）。在委托板上接受或放弃，带着东西找发布者交付。接了没做完、过了期限，发布者好感会下降。"));
		api.AddNumberOption(manifest, () => get().JobsPostedDaily, v => get().JobsPostedDaily = v, () => Tr.Get("config.commissionsPerDay", "每天新委托条数"),
			() => Tr.Get("config.commissionsPerDay.tip", "每天早上贴几条新委托。板上没人接的最多 4 条。"), min: 0, max: 4);
		api.AddBoolOption(manifest, () => get().ErrandsOn, v => get().ErrandsOn = v, () => Tr.Get("config.errands", "给村民发委托"),
			() => Tr.Get("config.errands.tip", "在世界 > 委托的\"我发的委托\"里出钱托村民找东西、代买、做菜或转送。报酬先交出去，没办成全额退回。"));
		api.AddBoolOption(manifest, () => get().PursesOn, v => get().PursesOn = v, () => Tr.Get("config.ledger", "村民账本"),
			() => Tr.Get("config.ledger.tip", "村民有自己的钱、随身物品和家当：领工资、逛店、付生活费、被偷，都会记账。可以向村民借钱，到期不还他们各有各的反应。"));
		api.AddBoolOption(manifest, () => get().ShoppingRemarks, v => get().ShoppingRemarks = v, () => Tr.Get("config.ledgerBubbles", "村民买东西时头顶说一句"),
			() => Tr.Get("config.ledgerBubbles.tip", "你和他在同一个地方时，他买了东西会在头顶说一句。"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.gifts", "村民送的礼物"));
		api.AddBoolOption(manifest, () => get().VillagerGiftsOn, v => get().VillagerGiftsOn = v, () => Tr.Get("config.aiGifts", "村民送的礼物有名字"),
			() => Tr.Get("config.aiGifts.tip", "村民送你的东西有自己的名字和描述，用送礼人的口吻写。心数里程碑的信里必带一件；聊天时由村民自己决定要不要当面送。关掉后信里附游戏自带的物品。"));
		api.AddNumberOption(manifest, () => get().GiftRestDays, v => get().GiftRestDays = v, () => Tr.Get("config.aiGiftCooldown", "同一个人送礼间隔（天）"),
			() => Tr.Get("config.aiGiftCooldown.tip", "同一个村民两次送礼至少隔这么多天。里程碑信件不受限制。"), min: 0, max: 56);
		api.AddNumberOption(manifest, () => get().GiftHearts, v => get().GiftHearts = v, () => Tr.Get("config.aiGiftHearts", "聊天送礼的最低心数"),
			() => Tr.Get("config.aiGiftHearts.tip", "至少这么多心，村民才会在聊天时当面送你东西。"), min: 0, max: 14);
		api.AddNumberOption(manifest, () => get().GiftsDaily, v => get().GiftsDaily = v, () => Tr.Get("config.aiGiftsPerDay", "每天聊天送礼上限"),
			() => Tr.Get("config.aiGiftsPerDay.tip", "一天里所有村民在聊天时当面送礼的总次数。"), min: 0, max: 10);

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.storiesGroup", "伴侣剧情"));
		api.AddBoolOption(manifest, () => get().SpouseChaptersOn, v => get().SpouseChaptersOn = v, () => Tr.Get("options.chapters", "伴侣剧情"),
			() => Tr.Get("options.chapters.note", "结婚以后，伴侣隔几天会和你有一段小故事：写信给你、拜托你一件事、约你在某个地方见面演一场戏、有话想跟你说。几位伴侣轮流。需要 AI。"));
		api.AddNumberOption(manifest, () => get().SpouseChapterGap, v => get().SpouseChapterGap = v, () => Tr.Get("options.chapterGap", "两段故事之间隔几天"),
			() => Tr.Get("options.chapterGap.note", "一段故事开始后，过多少天开始下一段（有几位伴侣时轮流）。"), min: 2, max: 28);
		api.AddTextOption(manifest, () => get().SpouseChapterFlavor, v => get().SpouseChapterFlavor = v, () => Tr.Get("shell.settings.storyStyle", "故事风格"),
			() => Tr.Get("shell.settings.storyStyleNote", "伴侣写的小故事偏哪种味道；随机就是每次挑最合适的。"),
			allowedValues: new[] { "mixed", "romantic", "everyday", "adventure", "funny" },
			formatAllowedValue: v => v switch
			{
				"romantic" => Tr.Get("shell.settings.storyRomantic", "浪漫"),
				"everyday" => Tr.Get("shell.settings.storyEveryday", "日常"),
				"adventure" => Tr.Get("shell.settings.storyAdventure", "小冒险"),
				"funny" => Tr.Get("shell.settings.storyFunny", "搞笑"),
				_ => Tr.Get("shell.settings.storyMixed", "随机")
			});

		api.AddSectionTitle(manifest, () => Tr.Get("config.companions", "同伴"));
		api.AddBoolOption(manifest, () => get().CompanionModuleOn, v => get().CompanionModuleOn = v, () => Tr.Get("config.moduleCompanions", "同伴模块"),
			() => Tr.Get("config.moduleCompanions.tip", "村民跟随、帮工、约见和约好时间上门帮忙（跟随和帮工需要安装 The Stardew Squad）。"));
		api.AddBoolOption(manifest, () => get().VillagersOfferHelp, v => get().VillagersOfferHelp = v, () => Tr.Get("config.aiProposals", "村民主动提议"),
			() => Tr.Get("config.aiProposals.tip", "聊天时村民可能说要跟你去、帮你干活，聊完你来决定。"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.seenAndHeard", "看到听到"));
		api.AddBoolOption(manifest, () => get().NoticingOn, v => get().NoticingOn = v, () => Tr.Get("config.sh.enable", "村民会注意到你做的事"));
		api.AddBoolOption(manifest, () => get().NoticeRemarks, v => get().NoticeRemarks = v, () => Tr.Get("config.sh.bubbles", "头上冒一句话"));
		api.AddBoolOption(manifest, () => get().NoticingMovesMood, v => get().NoticingMovesMood = v, () => Tr.Get("config.sh.mood", "看到的事影响心情"));
		api.AddBoolOption(manifest, () => get().NoticingMovesHearts, v => get().NoticingMovesHearts = v, () => Tr.Get("config.sh.friendship", "说出心里话时影响好感"));
		api.AddBoolOption(manifest, () => get().SilentTreatment, v => get().SilentTreatment = v, () => Tr.Get("config.sh.coldShoulder", "发过火后一小时内不理你"));
		api.AddBoolOption(manifest, () => get().NoticeTheft, v => get().NoticeTheft = v, () => Tr.Get("config.thief", "对偷窃有反应（Stardew Thief）"),
			() => Tr.Get("config.thief.tip", "装了 Stardew Thief 时：看见的人会记住、找你理论，失主发现少了东西，镇上会传开。"));
		api.AddNumberOption(manifest, () => get().CaughtStealingPenalty, v => get().CaughtStealingPenalty = v, () => Tr.Get("config.thiefLoss", "被当场抓住掉的好感"),
			() => Tr.Get("config.thiefLoss.tip", "抓到你的人掉这么多（250 是一颗心），入室时不在场的主人掉一半；0 = 不掉。"), min: 0, max: 250, interval: 10);
		api.AddBoolOption(manifest, () => get().NoticeByText, v => get().NoticeByText = v, () => Tr.Get("config.sh.text", "走不过来时改发消息"));
		api.AddBoolOption(manifest, () => get().NoticeVisitors, v => get().NoticeVisitors = v, () => Tr.Get("config.sh.npcMove", "记下室内谁来了"));
		api.AddNumberOption(manifest, () => get().FishTogetherPercent, v => get().FishTogetherPercent = v, () => Tr.Get("config.sh.fishing", "村民来陪钓的机会（%）"), min: 0, max: 100);

		api.AddSectionTitle(manifest, () => Tr.Get("config.group", "群聊"));
		api.AddBoolOption(manifest, () => get().WorldGroupTalkOn, v => get().WorldGroupTalkOn = v, () => Tr.Get("config.group.inWorld", "在地图上聊（头顶气泡）"),
			() => Tr.Get("config.group.inWorld.tip", "关掉后村民走过来拉你聊，在聊天窗口里进行。"));
		api.AddNumberOption(manifest, () => get().GroupTalkSize, v => get().GroupTalkSize = v, () => Tr.Get("config.group.max", "一场群聊最多几位村民"), min: 2, max: 6);
		api.AddNumberOption(manifest, () => get().PassingTalkPercent, v => get().PassingTalkPercent = v, () => Tr.Get("config.group.ambientChance", "村民自己聊起来的机会（%）"), min: 0, max: 100);
		api.AddNumberOption(manifest, () => get().PassingTalksDaily, v => get().PassingTalksDaily = v, () => Tr.Get("config.group.ambientPerDay", "每天最多自己聊几场"), min: 0, max: 50);
		api.AddNumberOption(manifest, () => get().PassingTalkRounds, v => get().PassingTalkRounds = v, () => Tr.Get("config.group.rounds", "自己聊几轮就散"), min: 1, max: 8);
		api.AddNumberOption(manifest, () => get().NoticedGroupTalkPercent, v => get().NoticedGroupTalkPercent = v, () => Tr.Get("config.group.entryChance", "进门时被拉进群聊的机会（%）"), min: 0, max: 100);
		api.AddTextOption(manifest, () => get().KeyGroupTalk, v => get().KeyGroupTalk = v, () => Tr.Get("config.group.key", "插话按键"));
		api.AddNumberOption(manifest, () => get().TownChatPostsDaily, v => get().TownChatPostsDaily = v, () => Tr.Get("config.group.posts", "居民群每天能发几条"), min: 1, max: 20);
		api.AddNumberOption(manifest, () => get().TownChatTalksDaily, v => get().TownChatTalksDaily = v, () => Tr.Get("config.group.npcChats", "村民每天在居民群里聊几次"), min: 0, max: 10);

		api.AddSectionTitle(manifest, () => Tr.Get("config.messenger", "信使"));
		api.AddNumberOption(manifest, () => get().TextsDaily, v => get().TextsDaily = v, () => Tr.Get("config.messenger.limit", "每位村民每天能发几条"), min: 1, max: 15);
		api.AddNumberOption(manifest, () => get().VillagerTextsWeekly, v => get().VillagerTextsWeekly = v, () => Tr.Get("config.messenger.perWeek", "村民每周主动发几条"), min: 0, max: 10);

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.newsGroup", "周报"));
		api.AddBoolOption(manifest, () => get().GazetteOn, v => get().GazetteOn = v, () => Tr.Get("options.gazette", "镇上的报纸"),
			() => Tr.Get("options.gazette.note", "每七天印一份镇上的报纸，要花一次 AI 请求"));
		if (OperatingSystem.IsWindows())
		{
			api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.inputGroup", "输入"));
			api.AddBoolOption(manifest, () => get().ImeCandidatesVisible, v =>
			{
				get().ImeCandidatesVisible = v;
				imeChanged?.Invoke(get(), v);
			}, () => Tr.Get("options.imeBox", "显示输入法候选框"),
				() => Tr.Get("options.imeBox.note", "打拼音时在游戏里显示输入法的候选字（还有输入法自带的语音输入条）。改了要重启游戏才生效。"));
		}

		api.AddSectionTitle(manifest, () => Tr.Get("config.debug", "调试"));
		api.AddBoolOption(manifest, () => get().Verbose, v => get().Verbose = v, () => Tr.Get("config.debugMode", "调试日志"));
		api.AddBoolOption(manifest, () => get().LogAiTraffic, v => get().LogAiTraffic = v, () => Tr.Get("config.logTraffic", "把发给 AI 的完整内容写进日志"),
			() => Tr.Get("config.logTraffic.tip", "里面有存档里的对话和八卦，只在排查问题时打开。"));
	}
}
