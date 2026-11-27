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
		api.AddBoolOption(manifest, () => get().ShareUsageStats, v => get().ShareUsageStats = v, () => Tr.Get("shell.settings.shareStats", "帮助改进功能"),
			() => Tr.Get("shell.settings.shareStatsNote", "用服务器时上报游戏时长和各功能用了几次，不含聊天内容；关掉后不再收集"));

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
		api.AddBoolOption(manifest, () => get().PlayerStartsTalk, v => get().PlayerStartsTalk = v, () => Tr.Get("shell.settings.playerStarts", "我先开口"),
			() => Tr.Get("shell.settings.playerStartsNote", "点村民时不等 TA 开场，直接打开回复框由你起话题；TA 有非说不可的话时还是 TA 先说。回复框里也能切换"));
		api.AddTextOption(manifest, () => get().VanillaInteractKey, v => get().VanillaInteractKey = v.Trim(), () => Tr.Get("shell.settings.vanillaKey", "按住点村民 = 游戏原来的互动"),
			() => Tr.Get("config.vanillaKey.tip", "按住这个键点村民时不走 AI，用游戏原来的互动（亲吻、拥抱、别的 mod 加的动作）。SMAPI 按键写法，逗号隔开表示任一个，比如 LeftAlt, LeftShoulder。"));
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

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.life", "村民的人生"));
		api.AddBoolOption(manifest, () => get().LifeOn, v => get().LifeOn = v, () => Tr.Get("shell.settings.lifeOn", "村民有自己的心愿"), () => Tr.Get("shell.settings.lifeOnNote", "村民慢慢做成自己想做的事，走到某一步会托你帮忙，达成时请你去看；暗生好感的村民可能交往"));
		api.AddNumberOption(manifest, () => get().LifeGoalsAtOnce, v => get().LifeGoalsAtOnce = v, () => Tr.Get("shell.settings.lifeGoals", "同时几位在忙"), () => Tr.Get("shell.settings.lifeGoalsNote", "每周看一次，有空位时再有一位村民开始新的心愿"), min: 1, max: 6);
		api.AddBoolOption(manifest, () => get().LifeWeddings, v => get().LifeWeddings = v, () => Tr.Get("shell.settings.lifeWeddings", "村民之间的婚礼"), () => Tr.Get("shell.settings.lifeWeddingsNote", "交往久了可能订婚，一周后在镇广场办婚礼；关掉就只交往。还要开着“村民之间可能产生好感”"));
		api.AddBoolOption(manifest, () => get().LifeJobs, v => get().LifeJobs = v, () => Tr.Get("shell.settings.lifeJobs", "心愿达成后换工作"), () => Tr.Get("shell.settings.lifeJobsNote", "有的村民做成以后换了工作，账本和聊天里跟着变"));

		api.AddSectionTitle(manifest, () => Tr.Get("config.social", "镇上的生活"));
		api.AddTextOption(manifest, () => get().KeyPhone, v => get().KeyPhone = v.Trim(), () => Tr.Get("config.phoneKey", "打开手机"), () => Tr.Get("config.phoneKeyTip", "手机里是同样的内容，排成竖屏"));
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

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.home", "家里"));
		api.AddBoolOption(manifest, () => get().KissChoiceMenu, v => get().KissChoiceMenu = v, () => Tr.Get("shell.settings.kissMenu", "点配偶先选聊天还是亲吻"),
			() => Tr.Get("shell.settings.kissMenuNote", "今天还能亲（抱）的时候，配偶头顶弹出小圆盘；关掉就直接聊天，按住跳过键点配偶照样能亲"));
		api.AddTextOption(manifest, () => get().ChildTalkMode, v => get().ChildTalkMode = v, () => Tr.Get("shell.settings.childTalk", "孩子说话"),
			() => Tr.Get("shell.settings.childTalkNote", "会走路以后：不变（游戏原来的样子）、每天一句、能来回聊几句；孩子的性格可以写在村民角色卡里"),
			allowedValues: new[] { "Off", "Line", "Chat" },
			formatAllowedValue: v => v switch
			{
				"Off" => Tr.Get("shell.settings.childOff", "不变"),
				"Line" => Tr.Get("shell.settings.childLine", "每天一句"),
				_ => Tr.Get("shell.settings.childChat", "能聊几句")
			});
		api.AddBoolOption(manifest, () => get().ChildRunaway, v => get().ChildRunaway = v, () => Tr.Get("shell.settings.runaway", "孩子离家出走"),
			() => Tr.Get("shell.settings.runawayNote", "孩子偶尔跑到农场上躲起来玩，要你去找；靠近会听到笑声"));
		api.AddNumberOption(manifest, () => get().ChildRunawayPercent, v => get().ChildRunawayPercent = v, () => Tr.Get("shell.settings.runawayChance", "每天跑出去的机会"), min: 0, max: 100);
		api.AddBoolOption(manifest, () => get().KidsGrowUp, v => get().KidsGrowUp = v, () => Tr.Get("shell.settings.kidsGrow", "孩子会长大"),
			() => Tr.Get("shell.settings.kidsGrowNote", "会走路以后还会长到上学的年纪、再成少年：换样子，说话跟着长大；你陪得多少会养成不同的性子"));
		api.AddNumberOption(manifest, () => get().KidSchoolDays, v => get().KidSchoolDays = v, () => Tr.Get("shell.settings.kidSchoolDays", "多久到上学的年纪"),
			() => Tr.Get("shell.settings.kidSchoolDaysNote", "从学会走路算起"), min: 7, max: 336);
		api.AddNumberOption(manifest, () => get().KidTeenDays, v => get().KidTeenDays = v, () => Tr.Get("shell.settings.kidTeenDays", "多久成少年"),
			() => Tr.Get("shell.settings.kidTeenDaysNote", "从到上学的年纪算起"), min: 7, max: 336);
		api.AddBoolOption(manifest, () => get().KidOutings, v => get().KidOutings = v, () => Tr.Get("shell.settings.kidOutings", "孩子出门"),
			() => Tr.Get("shell.settings.kidOutingsNote", "上学的孩子在佩妮上课的日子去教室（可以去看）；少年周末去镇上逛"));
		api.AddBoolOption(manifest, () => get().PetThoughts, v => get().PetThoughts = v, () => Tr.Get("shell.settings.petThoughts", "宠物的心里话"),
			() => Tr.Get("shell.settings.petThoughtsNote", "你在宠物旁边时，它偶尔冒一句心里话（灰色泡泡）；暂时不能和宠物对话"));
		api.AddNumberOption(manifest, () => get().PetThoughtsDaily, v => get().PetThoughtsDaily = v, () => Tr.Get("shell.settings.petDaily", "每只每天最多几句"), min: 1, max: 10);
		api.AddNumberOption(manifest, () => get().PetThoughtsPercent, v => get().PetThoughtsPercent = v, () => Tr.Get("shell.settings.petChance", "多常冒一句"),
			() => Tr.Get("shell.settings.petChanceNote", "你在旁边时每 10 分钟的机会"), min: 0, max: 100);
		api.AddBoolOption(manifest, () => get().SpousePlanner, v => get().SpousePlanner = v, () => Tr.Get("shell.settings.spousePlan", "配偶的日常安排"),
			() => Tr.Get("shell.settings.spousePlanNote", "婚后配偶每天自己安排：帮忙干农活、去镇上、看店、带孩子；手机“家务安排”里可以改"));
		api.AddTextOption(manifest, () => get().SpouseHelpLevel, v => get().SpouseHelpLevel = v, () => Tr.Get("shell.settings.spouseHelp", "帮忙勤快程度"), allowedValues: new[] { "Light", "Normal", "Busy" },
			formatAllowedValue: v => v switch
			{
				"Light" => Tr.Get("shell.settings.helpLight", "轻松"),
				"Busy" => Tr.Get("shell.settings.helpBusy", "勤快"),
				_ => Tr.Get("shell.settings.helpNormal", "一般")
			});
		api.AddTextOption(manifest, () => get().SpouseHarvestTo, v => get().SpouseHarvestTo = v, () => Tr.Get("shell.settings.spouseHarvestTo", "收获的东西放哪"),
			() => Tr.Get("shell.settings.spouseHarvestToNote", "收获箱在手机“家务安排”里领取；放出货箱就当晚卖掉"), allowedValues: new[] { "Chest", "Bin" },
			formatAllowedValue: v => v == "Bin" ? Tr.Get("shell.settings.toBin", "出货箱") : Tr.Get("shell.settings.toChest", "收获箱"));
		api.AddBoolOption(manifest, () => get().SpouseWaters, v => get().SpouseWaters = v, () => Tr.Get("shell.settings.spouseWater", "浇水"));
		api.AddBoolOption(manifest, () => get().SpouseHarvests, v => get().SpouseHarvests = v, () => Tr.Get("shell.settings.spouseHarvest", "收获"));
		api.AddBoolOption(manifest, () => get().SpouseTendsAnimals, v => get().SpouseTendsAnimals = v, () => Tr.Get("shell.settings.spouseAnimals", "照顾动物"), () => Tr.Get("shell.settings.spouseAnimalsNote", "补干草、抚摸、捡蛋"));
		api.AddBoolOption(manifest, () => get().SpouseClears, v => get().SpouseClears = v, () => Tr.Get("shell.settings.spouseClear", "清理农场"), () => Tr.Get("shell.settings.spouseClearNote", "只清杂草、石头、树枝，不砍树，不动你放的东西"));
		api.AddBoolOption(manifest, () => get().SpouseGreenhouse, v => get().SpouseGreenhouse = v, () => Tr.Get("shell.settings.spouseGreenhouse", "温室"));
		api.AddBoolOption(manifest, () => get().SpouseFishPonds, v => get().SpouseFishPonds = v, () => Tr.Get("shell.settings.spousePonds", "鱼塘"), () => Tr.Get("shell.settings.spousePondsNote", "收鱼塘的产出"));
		api.AddBoolOption(manifest, () => get().SpouseMachines, v => get().SpouseMachines = v, () => Tr.Get("shell.settings.spouseMachines", "收加工机器"), () => Tr.Get("shell.settings.spouseMachinesNote", "把做好的东西收进收获箱"));
		api.AddBoolOption(manifest, () => get().SpouseGoesOut, v => get().SpouseGoesOut = v, () => Tr.Get("shell.settings.spouseOut", "去镇上"), () => Tr.Get("shell.settings.spouseOutNote", "找人聊天、买东西"));
		api.AddNumberOption(manifest, () => get().SpouseSpendDaily, v => get().SpouseSpendDaily = v, () => Tr.Get("shell.settings.spouseSpend", "每天最多花多少"), () => Tr.Get("shell.settings.spouseSpendNote", "花的是配偶自己的钱"), min: 0, max: 5000, interval: 50);
		api.AddBoolOption(manifest, () => get().SpouseMindsShop, v => get().SpouseMindsShop = v, () => Tr.Get("shell.settings.spouseShop", "去小超市看店"), () => Tr.Get("shell.settings.spouseShopNote", "建了农夫小超市时，配偶会去当收银员"));
		api.AddBoolOption(manifest, () => get().SpousePlaysWithKids, v => get().SpousePlaysWithKids = v, () => Tr.Get("shell.settings.spouseKids", "带孩子玩"));
		api.AddBoolOption(manifest, () => get().RoommatePlans, v => get().RoommatePlans = v, () => Tr.Get("shell.settings.roommatePlan", "室友也这样安排"));

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.mart", "小超市"));
		api.AddBoolOption(manifest, () => get().FarmerMart, v => get().FarmerMart = v, () => Tr.Get("shell.settings.martOn", "农夫小超市"), () => Tr.Get("shell.settings.martOnNote", "去 Robin 的木匠铺建一间，放货架上架，村民会来买"));
		api.AddNumberOption(manifest, () => get().FarmerMartOpen, v => get().FarmerMartOpen = v, () => Tr.Get("shell.settings.martOpen", "几点开门"), min: 600, max: 1400, interval: 100);
		api.AddNumberOption(manifest, () => get().FarmerMartClose, v => get().FarmerMartClose = v, () => Tr.Get("shell.settings.martClose", "几点打烊"), min: 1200, max: 2400, interval: 100);
		api.AddBoolOption(manifest, () => get().MartCheckoutGame, v => get().MartCheckoutGame = v, () => Tr.Get("shell.settings.martCheckout", "收银小游戏"), () => Tr.Get("shell.settings.martCheckoutNote", "你在店里时顾客到柜台排队，点柜台收银找零，又快又对有小费；关掉就自动结账"));
		api.AddBoolOption(manifest, () => get().MartCompetition, v => get().MartCompetition = v, () => Tr.Get("shell.settings.martCompete", "和 Pierre、Joja 抢生意"),
			() => Tr.Get("shell.settings.martCompeteNote", "抢得多了他们会吃紧，甚至停业整顿；只在 AI星露谷 的经济里，不改原版剧情，种子一定买得到"));
		api.AddBoolOption(manifest, () => get().AllowBrawl, v => get().AllowBrawl = v, () => Tr.Get("shell.settings.brawl", "上门理论时可以动手"), () => Tr.Get("shell.settings.brawlNote", "像原版过场那样一拳，不伤人；会被罚款、上周报"));
		api.AddBoolOption(manifest, () => get().StolenGoodsNoticed, v => get().StolenGoodsNoticed = v, () => Tr.Get("shell.settings.stolenNoticed", "赃物可能被认出来"), () => Tr.Get("shell.settings.stolenNoticedNote", "偷来的东西放在店里卖，失主和熟人可能认出来"));
		api.AddBoolOption(manifest, () => get().FarmersStoreLink, v => get().FarmersStoreLink = v, () => Tr.Get("shell.settings.cosLink", "和 Farmer's Store 对接"),
			() => Tr.Get("shell.settings.cosLinkNote", "装了 Farmer's Store 时：两边不抢村民，它那边的成交也记进村民账本；两家店可以同时开"));

		api.AddSectionTitle(manifest, () => Tr.Get("shell.settings.consequences", "后果"));
		api.AddBoolOption(manifest, () => get().ConsequencesOn, v => get().ConsequencesOn = v, () => Tr.Get("shell.settings.consequencesOn", "做错事有后果"), () => Tr.Get("shell.settings.consequencesOnNote", "在原有的后果之外，同一季犯得越多越重：来信、罚款、记仇、传开、冷落一阵子；换季清零"));
		api.AddBoolOption(manifest, () => get().FenceArrest, v => get().FenceArrest = v, () => Tr.Get("shell.settings.arrest", "屡犯会被带走"), () => Tr.Get("shell.settings.arrestNote", "同一季第三次卖赃物被认出、第三次在店里动手：被带走（装了 Law and Order 关进牢里），当天结束；卖赃物的还会没收店里的赃物。关掉只罚款、停业"));
		api.AddBoolOption(manifest, () => get().BrawlConsequences, v => get().BrawlConsequences = v, () => Tr.Get("shell.settings.csqBrawl", "屡次动手"), () => Tr.Get("shell.settings.csqBrawlNote", "上门理论时动手：第二回罚款加倍，第三回被带走"));
		api.AddBoolOption(manifest, () => get().MeetupConsequences, v => get().MeetupConsequences = v, () => Tr.Get("shell.settings.csqMeetup", "放人鸽子"), () => Tr.Get("shell.settings.csqMeetupNote", "约好了没去：记成说话不算话；第二回传开，第三回这一周不肯再和你约"));
		api.AddBoolOption(manifest, () => get().FavorConsequences, v => get().FavorConsequences = v, () => Tr.Get("shell.settings.csqFavor", "答应的事没办"), () => Tr.Get("shell.settings.csqFavorNote", "托付、请求到期没办：记成说话不算话，好感掉一点；第二回传开"));
		api.AddBoolOption(manifest, () => get().DebtConsequences, v => get().DebtConsequences = v, () => Tr.Get("shell.settings.csqDebt", "欠钱不还"), () => Tr.Get("shell.settings.csqDebtNote", "借的钱、赊的账到期没还：镇长来信；第三回起两周内借不到钱、赊不了账"));
		api.AddBoolOption(manifest, () => get().TheftConsequences, v => get().TheftConsequences = v, () => Tr.Get("shell.settings.csqTheft", "偷东西被抓"), () => Tr.Get("shell.settings.csqTheftNote", "装了 Stardew Thief 时：屡次当场被抓会传开、镇长来信（罚款和牢房仍由它管）"));
		api.AddBoolOption(manifest, () => get().TrashConsequences, v => get().TrashConsequences = v, () => Tr.Get("shell.settings.csqTrash", "翻垃圾桶"), () => Tr.Get("shell.settings.csqTrashNote", "同一季屡次翻别人家门口的垃圾桶：主人来信，被起外号"));
		api.AddBoolOption(manifest, () => get().FlirtConsequences, v => get().FlirtConsequences = v, () => Tr.Get("shell.settings.csqFlirt", "当着恋人调情"), () => Tr.Get("shell.settings.csqFlirtNote", "当着恋人、配偶的面和别人调情：对方伤心好几天，好感和心情都掉；配偶这几天不帮你干农活"));

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
