using System.Collections.Generic;

namespace AISV.Core.Config;

/// <summary>
/// config.json。
/// 只放数据，取值时的范围修正在各自用到的地方（比如 <see cref="Ai.SlotRouter"/>）。
/// </summary>
public sealed class ModConfig
{
	/// <summary>"server" = AI星露谷服务器（默认）；"own" = 我自己的 AI 网关。</summary>
	public string AiConnectionMode { get; set; } = "server";

	/// <summary>关掉后不发任何 AI 请求（村民用游戏原来的对话）。</summary>
	public bool AiOn { get; set; } = true;

	public bool ModOn { get; set; } = true;

	// ---- 自己的网关（AiConnectionMode = own 时用） ----

	public string CustomLlmApiKey { get; set; } = "";

	public string CustomLlmBaseUrl { get; set; } = "http://127.0.0.1:7863/v1";

	public bool CustomLlmSupportsSystemPrompt { get; set; } = true;

	// ---- 对话槽：玩家在等结果的请求 ----

	public string ChatBackupModels { get; set; } = "cn:deepseek-v4-flash";

	public string ChatThinking { get; set; } = "low";

	public int ChatWaitSeconds { get; set; } = 30;

	public string CustomLlmModel { get; set; } = "cn:deepseek-v4.1-flash";

	// ---- 后台槽：八卦、信件、周报…… ----

	public string NightlyBackupModels { get; set; } = "cn:deepseek-v4-flash";

	/// <summary>每天后台请求上限（只在用自己的网关时生效；用服务器时按服务器给的数）。0 = 不限。</summary>
	public int NightlyCallsPerDay { get; set; } = 80;

	/// <summary>空 = 和对话槽用同一个模型。</summary>
	public string NightlyModel { get; set; } = "";

	public string NightlyThinking { get; set; } = "low";

	public int NightlyWaitSeconds { get; set; } = 90;

	/// <summary>回复的输出 token 上限（4096～20000）。</summary>
	public int ReplyTokenCap { get; set; } = 12000;

	// ---- 用服务器时自己挑的模型和思考档位（空 = 服务器默认） ----

	public string ServerChatModel { get; set; } = "";

	public string ServerThinkingLevel { get; set; } = "";

	// ---- 语言 ----

	/// <summary>AI 说什么语言：跟游戏、固定越南语，或自己填（CustomLanguage）。</summary>
	public AISV.Core.Text.LanguageMode SpeechLanguage { get; set; }

	public string SpeechLanguageName { get; set; } = "";

	// ---- 对话 ----

	public const int DefaultReplyCharLimit = 240;

	public int RecentTurnsInPrompt { get; set; } = 10;

	public int ReplyLength { get; set; } = DefaultReplyCharLimit;

	/// <summary>每位村民最多存多少条对话记录。</summary>
	public int TalkLogPerVillager { get; set; } = 60;

	// ---- 对话 ----

	/// <summary>小剧场结束后几天内，村民见面先说结局后的那句话。</summary>
	public int AfterwordDays { get; set; } = 4;

	public bool AskToDateOn { get; set; } = true;

	public bool AskToMarryOn { get; set; } = true;

	/// <summary>听了坏话的村民会在背后说人（记成剧情片段）。</summary>
	public bool BackbitingOn { get; set; } = true;

	/// <summary>回复框里可以表白、求婚，由村民自己决定答不答应。</summary>
	public bool CourtingOn { get; set; } = true;

	/// <summary>村民明确划了界线时隔几天（村民没说时用）。</summary>
	public int CourtRestAfterLine { get; set; } = 21;

	/// <summary>被拒后隔几天才能再开口（村民没说时用）。</summary>
	public int CourtRestAfterNo { get; set; } = 8;

	/// <summary>原作已婚、但故事里已经单身的村民也能表白。</summary>
	public bool CourtStorySingles { get; set; } = true;

	/// <summary>不用花束、美人鱼吊坠也能在对话里表白求婚。</summary>
	public bool CourtWithoutTokens { get; set; } = true;

	/// <summary>全村对某人的敌意到多少（%）算逐出的候选。</summary>
	public int DepartureGrudgePercent { get; set; } = 80;

	/// <summary>每次检测时提示 AI 可以写逐出故事线的概率。</summary>
	public int DepartureHintPercent { get; set; } = 10;

	/// <summary>小剧场可以走到有村民离开小镇。</summary>
	public bool DepartureStoriesOn { get; set; }

	/// <summary>每天最多几个剧情片段（1～5）。</summary>
	public int DramaBeatsDaily { get; set; } = 2;

	/// <summary>社交剧情的强度：Cozy（温和）/ Balanced / Dramatic（跌宕）。</summary>
	public string DramaHeat { get; set; } = "Balanced";

	/// <summary>村民之间的社交剧情。</summary>
	public bool DramaOn { get; set; } = true;

	/// <summary>每天最多挑几个偶遇。</summary>
	public int EncountersDaily { get; set; } = 1;

	/// <summary>偶遇：村民之间的小场景，每天挑几个，到了那里就会碰上。</summary>
	public bool EncountersOn { get; set; } = true;

	/// <summary>偶遇演完后，说话的人记住这件事，也会传成八卦。</summary>
	public bool EncountersRemembered { get; set; } = true;

	/// <summary>偶遇怎么演：Natural（一个人走过来、几个人旁听）、AmbientOnly（只旁听）、Cinematic（都按过场演）。</summary>
	public string EncounterStyle { get; set; } = "Natural";

	/// <summary>另外按孩子对待的村民（逗号分隔）：不会有恋爱内容。</summary>
	public string ExtraChildren { get; set; } = "";

	/// <summary>农夫在村民面前说别人好话坏话，会影响他的看法。</summary>
	public bool FarmerCanSway { get; set; } = true;

	/// <summary>把游戏原来这一刻的台词交给 AI 当话题参考。</summary>
	public bool FeedGameLineToAi { get; set; } = true;

	/// <summary>收尾时在游戏里演一段结局场景（关掉就只在后台写结局）。</summary>
	public bool FinaleScenesOn { get; set; } = true;

	/// <summary>AI 暂时不可用时，AI 村民改说游戏原来的台词（关着时什么也不说，只弹提示）。</summary>
	public bool GameLinesWhenAiDown { get; set; }

	/// <summary>说话太冲会积累敌意，敌意会抵掉好感心数。</summary>
	public bool HostilityOn { get; set; } = true;

	public bool HostOnlyCourting { get; set; } = true;

	/// <summary>对话里求婚要求房子至少升级过一次（和游戏一样）。</summary>
	public bool MarryNeedsHouseUpgrade { get; set; } = true;

	/// <summary>当面聊一轮、群聊说一句花几分钟游戏时间（0 = 不花）；只在单人游戏里算。</summary>
	public int MinutesPerReply { get; set; } = 10;

	/// <summary>多少心起可以约出去（门槛不高于 3 心的出游也按它放宽）。</summary>
	public int OutingHearts { get; set; } = 3;

	/// <summary>一起出去演完加的好感。</summary>
	public int OutingPoints { get; set; } = 12;

	/// <summary>被拒以后隔几天才能再约（村民没说时用）。</summary>
	public int OutingRestAfterNo { get; set; } = 1;

	/// <summary>答应以后隔几天才能再约（村民没说时用）。</summary>
	public int OutingRestAfterYes { get; set; } = 2;

	/// <summary>回复框里可以约村民出去，答应了就在约好的地方演一小段。</summary>
	public bool OutingsOn { get; set; } = true;

	/// <summary>等 AI 想台词的时候游戏时间暂停（只在单人游戏）。</summary>
	public bool PauseClockWhileWriting { get; set; } = true;

	/// <summary>每次回复最多加（或扣）多少好感。</summary>
	public int PointsCapPerReply { get; set; } = 20;

	/// <summary>一场对话里玩家最多回几次。</summary>
	public int RepliesPerTalk { get; set; } = 3;

	public int ReturnHintPercent { get; set; } = 50;

	public bool ReturnStoriesOn { get; set; } = true;

	/// <summary>八卦会改变村民对被说到的人的看法。</summary>
	public bool RumorsMoveOpinions { get; set; } = true;

	/// <summary>配偶开口第一句主动约你出去的机会（%，最多按 30 算）。</summary>
	public int SpouseAsksOutPercent { get; set; } = 12;

	/// <summary>和配偶约会可以有成年人的亲密场景（不露骨）。</summary>
	public bool SpouseIntimacyOn { get; set; }

	/// <summary>配偶每天最多聊几场完整的对话。</summary>
	public int SpouseTalksDaily { get; set; } = 3;

	/// <summary>同时进行的小剧场最多几出（1～5）。</summary>
	public int StorylinesAtOnce { get; set; } = 3;

	/// <summary>小剧场几季一点进展都没有就算不了了之。</summary>
	public int StorylineStaleSeasons { get; set; } = 3;

	/// <summary>关掉回复框不接话时加的好感。</summary>
	public int WalkAwayPoints { get; set; } = 15;

	/// <summary>今天第一句话变成"托你带样东西"的概率（%）。</summary>
	public int WishOddsPercent { get; set; } = 6;

	/// <summary>同一位村民的物品请求完成后，隔几天才会再托（0 不限）。</summary>
	public int WishRestDays { get; set; } = 6;

	// ---- 世界 ----

	/// <summary>玩家可以出钱托村民办事（找东西、代买、做菜、转送）。</summary>
	public bool ErrandsOn { get; set; } = true;

	/// <summary>村民每天在委托板上贴委托。</summary>
	public bool JobBoardOn { get; set; } = true;

	/// <summary>每天早上贴几条委托（0～4）。</summary>
	public int JobsPostedDaily { get; set; } = 2;

	/// <summary>作物和加工品的出货价跟着行情变；只影响出货箱结算，直接卖给商店还是原价。</summary>
	public bool MarketOn { get; set; } = true;

	/// <summary>村民有自己的钱、随身物品和家当，钱会流动；可以向村民借钱。</summary>
	public bool PursesOn { get; set; } = true;

	/// <summary>村民买了东西时，你在旁边就头顶说一句。</summary>
	public bool ShoppingRemarks { get; set; } = true;

	/// <summary>世界模块：市场、委托、报纸、帮工这些"镇上的事"。</summary>
	public bool WorldModuleOn { get; set; } = true;

	// ---- AI 礼物 ----

	/// <summary>至少几心才会在聊天时送礼。</summary>
	public int GiftHearts { get; set; } = 3;

	/// <summary>同一位村民两次送礼至少隔几天。</summary>
	public int GiftRestDays { get; set; } = 7;

	/// <summary>每天聊天时最多收到几件礼物。</summary>
	public int GiftsDaily { get; set; } = 2;

	/// <summary>村民聊天时、心数信和生日信里送你 AI 起名的礼物。</summary>
	public bool VillagerGiftsOn { get; set; } = true;

	// ---- 周报 ----

	/// <summary>每月 7、14、21、28 日晚上出一期《鹈鹕镇周报》，第二天早上寄到信箱。</summary>
	public bool GazetteOn { get; set; } = true;

	// ---- 输入 ----

	/// <summary>在游戏里显示输入法的候选框（写用户环境变量 SDL_IME_SHOW_UI=1，重启游戏后生效，只在 Windows 上）。</summary>
	public bool ImeCandidatesVisible { get; set; } = true;

	/// <summary>SDL_IME_SHOW_UI 是不是本 mod 写的（关掉时只删自己写的）。</summary>
	public bool ImeVarIsOurs { get; set; }

	// ---- 伴侣剧情 ----

	/// <summary>故事风格：mixed / romantic / everyday / adventure / funny。</summary>
	public string SpouseChapterFlavor { get; set; } = "mixed";

	/// <summary>两段故事之间隔几天（2～28），几位伴侣轮流。</summary>
	public int SpouseChapterGap { get; set; } = 7;

	/// <summary>结婚以后，伴侣每隔几天和你有一段小故事（信、拜托的事、见面、有话说）。</summary>
	public bool SpouseChaptersOn { get; set; } = true;

	// ---- 同伴 ----

	/// <summary>同伴模块：村民跟随、帮工（要装 The Stardew Squad）。</summary>
	public bool CompanionModuleOn { get; set; } = true;

	/// <summary>村民可以主动提议跟你去、帮你干活。</summary>
	public bool VillagersOfferHelp { get; set; } = true;

	// ---- 按键 ----

	/// <summary>打开"我的设定"。</summary>
	public string KeyAboutMe { get; set; } = "F7";

	/// <summary>直接打开外壳的八卦页。</summary>
	public string KeyRumors { get; set; } = "F6";

	/// <summary>打开 AI星露谷 外壳（今日 / 社交 / 世界 / 设置）。</summary>
	public string KeyShell { get; set; } = "F9";

	// ---- 八卦 ----

	public int ModRumorsDaily { get; set; } = 12;

	/// <summary>接受其他 mod 交来的八卦素材。</summary>
	public bool RumorsFromMods { get; set; } = true;

	/// <summary>提示词里最多留几条八卦（5～100）。</summary>
	public int RumorsInPrompt { get; set; } = 36;

	public bool RumorsOn { get; set; } = true;

	/// <summary>八卦偏向谁：Normal（多说农夫）/ Balanced（先说村民）/ VillagersOnly（只说村民）。</summary>
	public string RumorSubject { get; set; } = "Balanced";

	/// <summary>每隔几天让 AI 压缩一次八卦（5～15；≤0 时每月 1 日压）。</summary>
	public int RumorTidyDays { get; set; } = 12;

	// ---- 村民之间 ----

	/// <summary>不调 AI、直接用现成写法的概率（0～100%）。</summary>
	public int MeetupNoAiPercent { get; set; } = 20;

	/// <summary>少见的大事件概率（0～25%）。</summary>
	public int MeetupRarePercent { get; set; } = 5;

	/// <summary>村民之间的事可以变成八卦。</summary>
	public bool MeetupsBecomeRumors { get; set; } = true;

	/// <summary>互动有多戏剧化（0～100）。</summary>
	public int MeetupSpicePercent { get; set; } = 40;

	/// <summary>村民之间的关系会变（社交网）。关掉时退回简单的每日互动。</summary>
	public bool VillagerBondsShift { get; set; } = true;

	/// <summary>每天几对村民有互动（1～5）。</summary>
	public int VillagerMeetupsDaily { get; set; } = 2;

	/// <summary>村民之间每天有小互动。</summary>
	public bool VillagerMeetupsOn { get; set; } = true;

	// ---- 性格演变 ----

	/// <summary>成人内容模块：成人感情戏、已婚的人动摇、配偶亲密场景、成人事件记忆都要先开它。</summary>
	public bool AdultModuleOn { get; set; }

	/// <summary>村民可能慢慢发现自己的感情取向。</summary>
	public bool SelfDiscoveryOn { get; set; } = true;

	public bool TemperamentShifts { get; set; } = true;

	/// <summary>村民之间可以产生恋情。</summary>
	public bool VillagerCouplesOn { get; set; } = true;

	public bool HotSpouseScenesOn() => AdultModuleOn && SpouseIntimacyOn;

	public bool AdultDramaOn() => AdultModuleOn && AdultDramaAllowed;

	public bool InfidelityOn() => AdultModuleOn && WaveringSpousesOn;

	/// <summary>成人的感情戏（暧昧、三角、出轨暗示等）。</summary>
	public bool AdultDramaAllowed { get; set; }

	/// <summary>保护游戏里本来的夫妻：不会动摇，也不会和别人萌生恋情。</summary>
	public bool SafeguardGameCouples { get; set; }

	public int SelfDiscoveryHearts { get; set; } = 5;

	public int SelfDiscoverySignals { get; set; } = 4;

	/// <summary>允许已婚村民动摇（还要打开成人感情戏）。</summary>
	public bool WaveringSpousesOn { get; set; }

	// ---- 信件 ----

	public bool FestivalTalkOn { get; set; } = true;

	/// <summary>看过心事件后寄信。</summary>
	public bool MailAfterHeartScene { get; set; } = true;

	public int MailDaily { get; set; } = 1;

	/// <summary>不会被附在信里的物品（带类型前缀的 ID）。</summary>
	public List<string> MailGiftBanned { get; set; } = new List<string> { "(O)458", "(O)460", "(O)74", "(O)279", "(O)852", "(O)928" };

	/// <summary>信里附的礼物最高卖价。</summary>
	public int MailGiftPriceCap { get; set; } = 450;

	public bool MailGiftsFromModVillagers { get; set; } = true;

	/// <summary>到这些心数时寄信。</summary>
	public List<int> MailHeartSteps { get; set; } = new List<int> { 2, 5, 8, 10 };

	/// <summary>附最爱的东西时的最高卖价。</summary>
	public int MailLovedGiftPriceCap { get; set; } = 800;

	/// <summary>多少天没聊会寄"想你了"的信（1～56）。</summary>
	public int MailMissingDays { get; set; } = 9;

	public bool MailOn { get; set; } = true;

	/// <summary>同一位村民两封信至少隔几天。</summary>
	public int MailRestDays { get; set; } = 6;

	// ---- 看到听到 ----

	/// <summary>偷东西被当场抓住时，抓到你的人掉多少好感（入室时不在场的主人掉一半；0 = 不掉）。</summary>
	public int CaughtStealingPenalty { get; set; } = 60;

	/// <summary>你钓鱼时旁边的村民过来一起钓的概率（0～100）。</summary>
	public int FishTogetherPercent { get; set; } = 12;

	/// <summary>村民没能走过来时改发消息。</summary>
	public bool NoticeByText { get; set; }

	/// <summary>村民看到的事引出一场群聊的概率（0～100）。</summary>
	public int NoticedGroupTalkPercent { get; set; } = 18;

	/// <summary>小事只在头顶冒一句。</summary>
	public bool NoticeRemarks { get; set; } = true;

	/// <summary>装了 Stardew Thief 时，村民会对偷窃有反应（目击、找你理论、失主发现少了东西、镇上传开）。</summary>
	public bool NoticeTheft { get; set; } = true;

	/// <summary>室内的村民也会记下谁来了。</summary>
	public bool NoticeVisitors { get; set; }

	/// <summary>村民把话说出口后好感随之变化。</summary>
	public bool NoticingMovesHearts { get; set; }

	public bool NoticingMovesMood { get; set; } = true;

	/// <summary>村民会注意到你做的事，记在心里，可能走过来找你说。</summary>
	public bool NoticingOn { get; set; } = true;

	/// <summary>村民发完大火后一小时内不理你。</summary>
	public bool SilentTreatment { get; set; }

	// ---- 信使 ----

	/// <summary>每位村民每天能发几条消息（1～15）。</summary>
	public int TextsDaily { get; set; } = 6;

	/// <summary>村民每周主动发几条消息（0～10）。</summary>
	public int VillagerTextsWeekly { get; set; } = 4;

	// ---- 群聊 ----

	/// <summary>一场群聊最多几位村民（2～6）。</summary>
	public int GroupTalkSize { get; set; } = 4;

	/// <summary>插话 / 和身边的人聊起来的按键。</summary>
	public string KeyGroupTalk { get; set; } = "T";

	/// <summary>附近站在一起的村民自己聊起来的概率（每十分钟一次，0～100）。</summary>
	public int PassingTalkPercent { get; set; } = 30;

	/// <summary>村民自己聊几轮就散（1～8）。</summary>
	public int PassingTalkRounds { get; set; } = 3;

	public int PassingTalksDaily { get; set; } = 10;

	/// <summary>居民群每天能发几条（1～20）。</summary>
	public int TownChatPostsDaily { get; set; } = 6;

	/// <summary>村民每天自己在居民群里聊几次（0～10）。</summary>
	public int TownChatTalksDaily { get; set; } = 3;

	/// <summary>群聊在地图上进行（头顶气泡、底部输入栏）；关掉时用聊天窗口。</summary>
	public bool WorldGroupTalkOn { get; set; } = true;

	// ---- 哪些村民由 AI 驱动 ----

	public List<string> ModVillagerExtras { get; set; } = new List<string>();

	public List<string> ModVillagerPicks { get; set; } = new List<string>();

	public Compat.NpcModActivationMode ModVillagerScope { get; set; } = Compat.NpcModActivationMode.AllNpcMods;

	/// <summary>玩家自己不想让 AI 驱动的村民（内部名，逗号或换行分隔）。</summary>
	public string OptOutExtras { get; set; } = "";

	/// <summary>遵守社区的 AI 排除名单（NPC 作者不希望被 AI 驱动的角色）。</summary>
	public bool OptOutListOn { get; set; } = true;

	public string OptOutListUrl { get; set; } = "https://stardewmodding.wiki.gg/wiki/AI_Mod_Exclusion.json?action=raw";

	/// <summary>"选定的 mod"模式下，查不到来源的 NPC 也启用。</summary>
	public bool UnknownVillagersOn { get; set; }

	/// <summary>设置里关掉的村民（内部名，逗号分隔）。</summary>
	public string VillagersOff { get; set; } = "";

	// ---- 调试 ----

	/// <summary>把发给 AI 的完整内容写进日志（Trace）。里面有存档里的对话和八卦，默认关。</summary>
	public bool LogAiTraffic { get; set; }

	public bool Verbose { get; set; }

	/// <summary>读完 config.json 后修正一遍：空的地址和模型回到默认值，上限限在合理范围，mod ID 列表去空白去重。</summary>
	public ModConfig Normalize()
	{
		ModConfig d = new ModConfig();
		if (string.IsNullOrWhiteSpace(CustomLlmBaseUrl))
		{
			CustomLlmBaseUrl = d.CustomLlmBaseUrl;
		}
		if (string.IsNullOrWhiteSpace(CustomLlmModel))
		{
			CustomLlmModel = d.CustomLlmModel;
		}
		NightlyCallsPerDay = System.Math.Clamp(NightlyCallsPerDay, 0, 2000);
		CustomLlmApiKey = CustomLlmApiKey?.Trim() ?? "";
		ReplyLength = System.Math.Clamp(ReplyLength <= 0 ? DefaultReplyCharLimit : ReplyLength, 50, 2000);
		RepliesPerTalk = System.Math.Clamp(RepliesPerTalk <= 0 ? 3 : RepliesPerTalk, 1, 50);
		MinutesPerReply = System.Math.Clamp(MinutesPerReply, 0, 60);
		CaughtStealingPenalty = System.Math.Clamp(CaughtStealingPenalty, 0, 250);
		SpouseTalksDaily = System.Math.Clamp(SpouseTalksDaily <= 0 ? 3 : SpouseTalksDaily, 1, 10);
		DramaHeat = (DramaHeat ?? "").Trim().ToLowerInvariant() switch
		{
			"cozy" => "Cozy",
			"dramatic" => "Dramatic",
			_ => "Balanced"
		};
		RumorSubject = Social.GossipFocus.Normalize(RumorSubject);
		RumorsInPrompt = System.Math.Clamp(RumorsInPrompt <= 0 ? 30 : RumorsInPrompt, 5, 100);
		JobsPostedDaily = System.Math.Clamp(JobsPostedDaily, 0, 4);
		GiftRestDays = System.Math.Max(0, GiftRestDays);
		GiftHearts = System.Math.Clamp(GiftHearts, 0, 14);
		GiftsDaily = System.Math.Max(0, GiftsDaily);
		SpouseChapterGap = System.Math.Clamp(SpouseChapterGap, 2, 28);
		SpouseChapterFlavor = (SpouseChapterFlavor ?? "").Trim().ToLowerInvariant() switch
		{
			"romantic" or "浪漫" => "romantic",
			"everyday" or "日常" => "everyday",
			"adventure" or "冒险" => "adventure",
			"funny" or "搞笑" => "funny",
			_ => "mixed"
		};
		FishTogetherPercent = System.Math.Clamp(FishTogetherPercent, 0, 100);
		NoticedGroupTalkPercent = System.Math.Clamp(NoticedGroupTalkPercent, 0, 100);
		TextsDaily = System.Math.Clamp(TextsDaily, 1, 15);
		StorylinesAtOnce = System.Math.Clamp(StorylinesAtOnce, 1, 5);
		OutingHearts = System.Math.Clamp(OutingHearts, 0, 14);
		OutingRestAfterYes = System.Math.Clamp(OutingRestAfterYes, 0, 28);
		OutingRestAfterNo = System.Math.Clamp(OutingRestAfterNo, 0, 28);
		OutingPoints = System.Math.Clamp(OutingPoints, 0, 100);
		SpouseAsksOutPercent = System.Math.Clamp(SpouseAsksOutPercent, 0, 100);
		EncounterStyle = Scenes.SceneStyle.Normalize(EncounterStyle);
		EncountersDaily = System.Math.Clamp(EncountersDaily, 0, 5);
		StorylineStaleSeasons = System.Math.Clamp(StorylineStaleSeasons, 1, 8);
		AfterwordDays = System.Math.Clamp(AfterwordDays, 1, 14);
		DepartureHintPercent = System.Math.Clamp(DepartureHintPercent, 0, 100);
		ReturnHintPercent = System.Math.Clamp(ReturnHintPercent, 0, 100);
		DepartureGrudgePercent = System.Math.Clamp(DepartureGrudgePercent, 0, 100);
		DramaBeatsDaily = System.Math.Clamp(DramaBeatsDaily <= 0 ? 2 : DramaBeatsDaily, 1, 5);
		VillagerTextsWeekly = System.Math.Clamp(VillagerTextsWeekly, 0, 10);
		GroupTalkSize = System.Math.Clamp(GroupTalkSize, 2, 6);
		PassingTalkPercent = System.Math.Clamp(PassingTalkPercent, 0, 100);
		PassingTalksDaily = System.Math.Clamp(PassingTalksDaily, 0, 50);
		PassingTalkRounds = System.Math.Clamp(PassingTalkRounds, 1, 8);
		KeyGroupTalk = (KeyGroupTalk ?? "").Trim();
		TownChatPostsDaily = System.Math.Clamp(TownChatPostsDaily, 1, 20);
		TownChatTalksDaily = System.Math.Clamp(TownChatTalksDaily, 0, 10);
		ModRumorsDaily = System.Math.Clamp(ModRumorsDaily, 0, 100);
		VillagerMeetupsDaily = System.Math.Clamp(VillagerMeetupsDaily, 1, 5);
		MeetupSpicePercent = System.Math.Clamp(MeetupSpicePercent, 0, 100);
		MeetupRarePercent = System.Math.Clamp(MeetupRarePercent, 0, 25);
		MeetupNoAiPercent = System.Math.Clamp(MeetupNoAiPercent, 0, 100);
		MailMissingDays = System.Math.Clamp(MailMissingDays <= 0 ? 7 : MailMissingDays, 1, 56);
		MailHeartSteps ??= new List<int> { 3, 6, 8, 10 };
		MailGiftBanned ??= new List<string>();
		ModVillagerPicks = CleanIds(ModVillagerPicks);
		ModVillagerExtras = CleanIds(ModVillagerExtras);
		return this;
	}

	private static List<string> CleanIds(List<string>? ids)
	{
		List<string> result = new List<string>();
		HashSet<string> seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (string id in ids ?? new List<string>())
		{
			string t = id?.Trim() ?? "";
			if (t.Length > 0 && seen.Add(t))
			{
				result.Add(t);
			}
		}
		return result;
	}
}
