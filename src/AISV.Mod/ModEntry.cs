using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Ai;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Messenger;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Scenes;
using AISV.Core.Storage;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Game;
using AISV.Game.Approach;
using AISV.Game.Companions;
using AISV.Game.Awareness;
using AISV.Game.Dialogue;
using AISV.Game.Favors;
using AISV.Game.Festivals;
using AISV.Game.Group;
using AISV.Game.Looks;
using AISV.Game.Messenger;
using AISV.Game.Npcs;
using AISV.Game.Outings;
using AISV.Game.Scenes;
using AISV.Game.Social;
using AISV.Game.Stories;
using AISV.Game.World;
using AISV.UI;
using AISV.UI.Dialogue;
using AISV.UI.Group;
using AISV.UI.Shell;
using AISV.UI.Theme;
using HarmonyLib;
using AISV.Integrations.Gmcm;
using AISV.Integrations.Squad;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace AISV.Mod;

/// <summary>SMAPI 入口：只负责把各层组装起来、挂事件。具体功能都在各自的子系统里。</summary>
public sealed class ModEntry : StardewModdingAPI.Mod
{
	private ILog _log = NullLog.Instance;

	private ModConfig _config = new ModConfig();

	private SaveData _saveData = null!;

	private ServerConnection _connection = ServerConnection.None;

	private ServerAccount _account = null!;

	private AiService _ai = null!;

	private NpcOwnershipScanner _ownership = null!;

	private OptOutList _optOut = null!;

	private NpcDirectory _npcs = null!;

	private PromptTemplates _templates = null!;

	private DialogueEngine _dialogue = null!;

	private ConversationController _conversations = null!;

	private SocialSystem _social = null!;

	private ShellContext _shell = null!;

	private ApproachCoordinator _approaches = null!;

	private AwarenessSystem _awareness = null!;

	private AwarenessSensors _sensors = null!;

	private NpcLooks _looks = null!;

	private FavorService _favors = null!;

	private StoryArcService _arcs = null!;

	private OutingService _outings = null!;

	private SceneService _scenes = null!;

	private MomentDirector _moments = null!;

	private GroupMaterial _groupMaterial = null!;

	private FestivalSystem _festivals = null!;

	private GroupDirector _groups = null!;

	private TalkClock _talkClock = null!;

	private ProfilePrompt _profilePrompt = null!;

	private ThiefWatcher _thief = null!;

	private ResidentsService _residents = null!;

	private MessengerService _messenger = null!;

	private VillagerTextService _villagerTexts = null!;

	private Ecosystem _eco = null!;

	private MarketService _market = null!;

	private CommissionService _commissions = null!;

	private ErrandService _errands = null!;

	private LedgerService _ledger = null!;

	private AISV.Game.Social.AppsService _apps = null!;

	private AISV.Game.Gifts.AiGiftService _gifts = null!;

	private AISV.Game.Adult.AdultMemoryService _adult = null!;

	private SquadBridge _squad = null!;

	private ScheduleOverlayService _schedules = null!;

	private MeetupService _meetups = null!;

	private SpouseStoryService _stories = null!;

	private WeeklyNewsService _news = null!;

	private AISV.Game.Promo.SoftPromoService _promo = null!;

	private AISV.Game.Phone.PhoneItem _phone = null!;

	private ImeCandidateWindow _ime = null!;

	private FarmHelpService _farmHelp = null!;

	private OfferDesk _offers = null!;

	public override void Entry(IModHelper helper)
	{
		StepTimer timer = new StepTimer("AI星露谷 启动");
		_log = new SmapiLog(Monitor);
		using (timer.Step("配置和文字"))
		{
			_config = ReadConfig(helper);
			Tr.Use(key => helper.Translation.Get(key) is { } t && t.HasValue() ? t.ToString() : null);
		}
		if (!_config.ModOn)
		{
			// 总开关关掉：什么都不挂。
			_log.Info("config.json 里关掉了 AI星露谷（EnableMod = false）。");
			return;
		}
		using (timer.Step("AI 连接和账号"))
		{
			_connection = ServerConnection.Load(helper.DirectoryPath, _log);
			HttpClient accountHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
			HttpClient aiHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
			_account = new ServerAccount(_connection, new GlobalAccountStore(helper.Data), accountHttp, _log) { ClientVersion = ModManifest.Version.ToString() };
			ChatClient chat = new ChatClient(aiHttp, () => _config, _log, _connection.Mask);
			_ai = new AiService(() => _config, chat, _connection, _account, _log);
		}
		using (timer.Step("村民和人设"))
		{
			string data = Path.Combine(helper.DirectoryPath, "Data");
			DirectoryBackend cache = new DirectoryBackend(Path.Combine(data, "cache"));
			_ownership = new NpcOwnershipScanner(cache, _log, LenientJson);
			_optOut = new OptOutList(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, cache, _ownership, _log);
			NpcEligibility eligibility = new NpcEligibility(() => _config, _optOut, _log);
			_npcs = new NpcDirectory(new CharacterCardStore(new DirectoryBackend(Path.Combine(data, "character-cards")), _log), eligibility);
			_templates = new PromptTemplates(new DirectoryBackend(Path.Combine(helper.DirectoryPath, "assets", "prompts")), _log);
		}
		new ConsoleCommands(_account, _ai, _npcs, _log).Register(helper.ConsoleCommands);
		helper.ConsoleCommands.Add("aisv_shell", "打开 AI星露谷 外壳。\n用法：aisv_shell [today|social|messenger|villagers|residents|gossip|letters|diary|arcs|outings|world|market|commissions|mine|errand[:村民]|ledger|belongings:村民|loan:村民|help|news|settings|profile|cards|apps|mood|year|web|gtree|reviews|pelican|agenda|horoscope|sky|ads|whosaid|junimo]", (_, args) => OpenShell(args.Length > 0 ? args[0] : ""));
		using (timer.Step("存档数据"))
		{
			_saveData = new SaveData(helper.DirectoryPath, _log);
		}
		using (timer.Step("对话"))
		{
			ChildSafety.Configure(_config.ExtraChildren);
			Func<DocumentStore?> store = () => _saveData.Current;
			ConversationLog log = new ConversationLog(store, () => _config.TalkLogPerVillager);
			RelationshipStates states = new RelationshipStates(store);
			Relationships relationships = new Relationships(() => _config, states, _log);
			_dialogue = new DialogueEngine(() => _config, _ai, _templates, log, new RecentLines(), _log, PromptLanguage);
			MaterialFactory materials = new MaterialFactory(_npcs, relationships, log, store);
			_conversations = new ConversationController(() => _config, _ai, _dialogue, materials, relationships, _npcs, helper, _log);
			_conversations.UseAccount(_account);
			MainThread.Log = _log;
			Harmony harmony = new Harmony(ModManifest.UniqueID);
			TalkPatch.Handler = _conversations.OnCheckAction;
			TalkPatch.Apply(harmony, _log);
			_social = new SocialSystem(() => _config, store, _ai, _templates, PromptLanguage, log, relationships, _npcs, _log);
			_shell = new ShellContext(_social, _npcs, relationships, log, () => _config, SaveConfig) { Store = store };
			_profilePrompt = new ProfilePrompt(_shell);
			materials.Social = _social;
			_conversations.Talked += _social.OnTalked;
			_talkClock = new TalkClock(() => _config);
			_conversations.Talked += _ => _talkClock.Talked();
			_conversations.Skipped += _social.OnSkipped;
			GiftPatch.Handler = _social.OnGiftReceived;
			GiftPatch.Apply(harmony, _log);
			new ChildTalk(() => _config, _npcs, _log) { Ai = _ai, Language = PromptLanguage, Rewritten = n => !string.IsNullOrWhiteSpace(_npcs.Cards.Get(n)?.Personality) }.Apply(harmony);
			_approaches = new ApproachCoordinator(source => source == MomentDirector.Source ? _config.EncountersOn : _awareness.Enabled, _dialogue.IsGeneratingFor, () => GenerationStatus.IsActive, () => _conversations.InDayTransition, _conversations.PresentPrepared, _log);
			_awareness = new AwarenessSystem(() => _config, store, _npcs, _approaches, materials, _dialogue, helper.DirectoryPath, _log)
			{
				AdjustMood = _social.Moods.Adjust,
				RecordGossip = text => _social.Gossip.AddThirdPartyNote(text, AwarenessSystem.Source, builtIn: true)
			};
			_looks = new NpcLooks(helper, _log);
			PortraitOverrides.Directory = helper.DirectoryPath;
			_sensors = new AwarenessSensors(_awareness, () => _config, _npcs.IsEnabled, () => _conversations.InDayTransition, _looks.FishingPose, _log);
			_sensors.Apply(harmony, _log);
			_thief = new ThiefWatcher(() => _config, _awareness, new TheftLog(store), _log) { NoticedLoss = OnTheftNoticed };
			_thief.Attach(helper, harmony);
			helper.ConsoleCommands.Add("aisv_thief", "Stardew Thief 兼容调试。\n用法：aisv_thief dump | sim pickpocket [村民] [caught] | sim burglary [caught]", (_, args) => _log.Info(ThiefCommand(args)));
			_sensors.Exploded += _looks.OnExplosion;
			_approaches.ExtraAttention = () => _sensors.AttentionNpc;
			materials.Awareness = _awareness.PromptBlock;
			_festivals = new FestivalSystem(() => _config, helper.DirectoryPath, _dialogue, _social, helper.ModRegistry, _log);
			_conversations.Festivals = _festivals;
			_festivals.DanceAccepted += _looks.WearDanceOutfit;
			_conversations.FestivalTalked += _social.OnFestivalTalked;
			_conversations.FestivalInvited += _social.OnFestivalInvite;
			_conversations.FestivalSkipped += _social.OnFestivalSkipped;
			_conversations.ColdShoulder = _awareness.TryColdShoulder;
			_conversations.Talked += x => _awareness.OnTalk(x.Npc, x.PlayerText, x.ShownReaction, x.KeepSecret, x.Reaction);
			_conversations.ConversationEnded += _approaches.OnConversationEnded;
			GiftPatch.Handler += _sensors.OnGift;
			_favors = new FavorService(() => _config, store, helper, relationships, _log) { Changed = _dialogue.Invalidate };
			_dialogue.ItemRequests = _favors;
			materials.Favors = _favors.FavorBlock;
			materials.ItemRequest = _favors.RequestBlock;
			_social.OpenFavors = _favors.OpenFor;
			GiftPatch.Handler += (npc, item) => _favors.OnGift(npc, item, GiftPatch.LastGain);
			FavorPatches.Apply(harmony, _favors, _log);
			helper.ConsoleCommands.Add("aisv_favor", "托付和物品请求调试。\n用法：aisv_favor state | favor <村民> <东西> | request <村民> [模板] | complete <村民>", (_, args) => _log.Info(_favors.Command(args)));
			_conversations.Talked += x => _favors.OnReply(x.Npc, x.Reaction);
			_arcs = new StoryArcService(() => _config, store, helper.DirectoryPath, _social, log, relationships, _npcs, new StoryArcAi(_ai, _templates, PromptLanguage, _log), _log);
			materials.Arcs = _arcs.PromptBlock;
			materials.Lore = _arcs.LoreBlock;
			materials.AdultDramaAllowed = () => _config.AdultDramaOn();
			_arcs.Hostility = () => _social.Drama.Hostility();
			helper.ConsoleCommands.Add("aisv_gossip", "八卦调试。\n用法：aisv_gossip list | inject <起头的村民> <事实> [| 他的原话] | spread", (_, args) => _log.Info(Context.IsWorldReady ? GossipCommand(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_drama", "社交剧情调试。\n用法：aisv_drama [status] | hostility | <村民>", (_, args) => DramaCommand(args));
			_dialogue.PostResolutionLine = _arcs.PostResolutionLine;
			_npcs.Eligibility.SetExpelledCheck(_arcs.Expulsions.IsExpelled);
			ShopPatches.Apply(harmony, _arcs.Shops.IsDetached, _log);
			_shell.Arcs = _arcs;
			ArcScenes.Log = _log;
			_conversations.Talked += x => _arcs.OnReply(x.Npc, x.Reaction);
			helper.ConsoleCommands.Add("aisv_arc", "小剧场调试。\n用法：aisv_arc list | analyze | inject <文件> | make <标题> <村民,村民,村民> <说明> | complete <id> | conclude <id> | play <id> [舞台] | fizzle <id> | expel <村民> | return <村民>", (_, args) => _log.Info(_arcs.Command(args)));
			_conversations.Proposals = new ProposalService(() => _config, states, relationships, _npcs, _log) { Social = _social, StorySingle = _arcs.IsStorySingle };
			_conversations.Proposed += (x, points) =>
			{
				_social.OnProposal(x, points);
				_favors.OnReply(x.Npc, x.Reaction);
				_arcs.OnReply(x.Npc, x.Reaction);
			};
			_outings = new OutingService(() => _config, store, helper, relationships, _npcs, _log)
			{
				Social = _social,
				Conversations = log,
				Engine = _dialogue,
				Materials = materials,
				Proposals = _conversations.Proposals
			};
			_dialogue.Outings = _outings;
			_conversations.Outings = _outings;
			_social.OutingsToday = _outings.CompletedToday;
			_shell.Outings = _outings;
			_conversations.Outed += (x, turn) =>
			{
				_talkClock.Talked();
				_social.OnOuting(x, turn);
				if (turn.SettlesFavor)
				{
					_favors.OnReply(x.Npc, x.Reaction);
				}
				_arcs.OnReply(x.Npc, x.Reaction);
			};
			helper.ConsoleCommands.Add("aisv_outing", "约出去调试。\n用法：aisv_outing state | options <村民> | accept <村民> [出游 id] | offer | start | expire", (_, args) => _log.Info(_outings.Command(args)));
			_scenes = new SceneService(() => _config, store, helper, relationships, _npcs, _log)
			{
				Social = _social,
				AiAvailable = () => _config.AiOn && !_ai.IsDialogueUnavailable()
			};
			GroupMaterial groupMaterial = _groupMaterial = new GroupMaterial(_npcs, relationships, log, store) { Social = _social, Awareness = _awareness.PromptBlock, FarmerSightings = _awareness.FreshFarmerSightings };
			_moments = new MomentDirector(_scenes, _approaches, () => _config, _log)
			{
				IsNpcEnabled = _npcs.IsEnabled,
				IsNpcOccupied = n => _awareness.IsNpcOccupied(n),
				AiDialogueAvailable = () => _config.AiOn && !_ai.IsDialogueUnavailable(),
				TalkedToday = n => _dialogue.HasDailyState(n, GameState.IsSpouse(n), GameState.Today),
				IsGenerating = _dialogue.IsGeneratingFor,
				TodayLine = n => _dialogue.State(n)?.Line,
				DiscardOpening = (n, line) => _dialogue.DiscardUnshownOpening(n, line),
				IsTalkingTo = n => _conversations.IsTalkingWith(n),
				InDayTransition = () => _conversations.InDayTransition,
				Generating = () => GenerationStatus.IsActive,
				PrepareWalkUp = (npc, location, focus) =>
				{
					DialogueMaterial m = materials.Build(npc, location, openingFocus: focus);
					GameDate today = GameState.Today;
					return async ct => (await _dialogue.OpeningAsync(m, today, ct).ConfigureAwait(false))?.Dialogue;
				},
				WriteOverheard = (speakers, premise, location, time) =>
				{
					List<(string, string)> personas = speakers.Select(n => (n, _npcs.Persona(n, StardewValley.Game1.getCharacterFromName(n)).Personality)).ToList();
					string season = StardewValley.Game1.currentSeason;
					string topic = speakers.Count > 1 ? groupMaterial.FarmerTopic(speakers, "scene") : "";
					if (topic.Length > 0)
					{
						_log.Debug("旁听：从农夫最近的事聊起：" + topic);
					}
					return ct => _dialogue.OverheardAsync(personas, premise, location, time, season, ct, topic);
				}
			};
			helper.ConsoleCommands.Add("aisv_scene", "偶遇调试。\n用法：aisv_scene status | arm <场景 id> | style <Natural|AmbientOnly|Cinematic>", (_, args) => SceneCommand(args));
			GroupChatAi groupAi = new GroupChatAi(_ai, PromptLanguage, _log);
			Func<string, bool> fishing = _awareness.IsBusyElsewhere ?? (_ => false);
			_groups = new GroupDirector(() => _config, _ai, groupAi, groupMaterial, _approaches, _dialogue, helper, _log)
			{
				Social = _social,
				IsNpcOccupied = n => _conversations.IsTalkingWith(n, ConversationController.TalkGraceTicks) || _approaches.IsNpcBusy(n) || fishing(n) || SquadMark.IsControlled(n) || _schedules.IsHolding(n) || _moments.IsInScene(n),
				InDayTransition = () => _conversations.InDayTransition
			};
			_groups.PlayerSpoke += () => _talkClock.Talked();
			_awareness.IsBusyElsewhere = n => fishing(n) || _groups.IsNpcInvolved(n) || SquadMark.IsControlled(n) || _schedules.IsHolding(n);
			_awareness.IsFollowing = SquadMark.IsControlled;
			_residents = new ResidentsService(() => _config, store, _ai, groupAi, groupMaterial, _log) { IsViewing = () => ShellMenu.ViewingResidents };
			_shell.Residents = _residents;
			_messenger = new MessengerService(() => _config, _ai, _dialogue, materials, relationships, log, _npcs, new PhotoStore(helper.DirectoryPath, _log), _log)
			{
				Social = _social,
				Viewing = () => ShellMenu.ViewingMessenger,
				TextedBeside = OnTextedBeside
			};
			_shell.Messenger = _messenger;
			_villagerTexts = new VillagerTextService(() => _config, store, _messenger, new MessengerAi(_ai, _templates, PromptLanguage), _npcs, relationships, _log) { Social = _social };
			_awareness.SendText = _villagerTexts.TryRequestText;
			helper.ConsoleCommands.Add("aisv_msg", "信使调试。\n用法：aisv_msg send <村民> <文字> | text <村民> [类型] | seen <村民> [看到的事] | open <村民> | state", (_, args) => MessengerCommand(args));
			helper.ConsoleCommands.Add("aisv_group", "群聊调试。\n用法：aisv_group start | ambient | key | click <村民> | state | post <文字> | villagers | residents | farmertopic always|normal", (_, args) => GroupCommand(args));
			helper.ConsoleCommands.Add("aisv_looks", "村民临时样子调试。\n用法：aisv_looks soot <村民> | dance <村民> [帧] | clear", (_, args) => LooksCommand(args));
			_eco = new Ecosystem(_log, () => _npcs.Eligibility.LoadedModIds, _npcs.Eligibility.OwnerModId, _npcs.Eligibility.IsContentModEnabled);
			_market = new MarketService(() => _config, store, _eco, _npcs.IsEnabled, _log);
			MarketPatches.Apply(harmony, _log);
			_shell.Market = _market;
			helper.Events.Content.AssetsInvalidated += (_, e) => _eco.OnAssetsInvalidated(e.NamesWithoutLocale);
			Func<string, object, string> debugFile = DebugFiles.Writer(helper.DirectoryPath);
			helper.ConsoleCommands.Add("aisv_eco", "生态读取调试，结果写到 debug/。\n用法：aisv_eco | npc <名字> | item <ID 或 wine:(O)398>", (_, args) => _log.Info(Context.IsWorldReady ? _eco.Command(args, debugFile) : "先进存档。"));
			_commissions = new CommissionService(() => _config, store, _eco, _market, _npcs.IsEnabled, _log)
			{
				Ai = _ai,
				Language = PromptLanguage,
				Personality = n => _npcs.Persona(n, StardewValley.Game1.getCharacterFromName(n)).Personality
			};
			CommissionPatches.Apply(harmony, _commissions, _log);
			_shell.Commissions = _commissions;
			_shell.Favors = _favors;
			_gifts = new AISV.Game.Gifts.AiGiftService(() => _config, store, _eco, _npcs.IsEnabled, _log)
			{
				Ai = _ai,
				Language = PromptLanguage,
				Personality = n => _npcs.Persona(n, StardewValley.Game1.getCharacterFromName(n)).Personality,
				Feelings = () => _social.Feelings
			};
			AISV.Game.Gifts.AiGiftPatches.Apply(harmony, _gifts, _log);
			materials.GiftJson = _gifts.DialogueJson;
			_conversations.Talked += x => _gifts.OnTalked(x.Npc.Name, x.HeartsBefore, x.Reaction);
			_social.AttachGift = _gifts.PrepareLetterGift;
			_social.GiftName = _gifts.NameOf;
			_shell.Gifts = _gifts;
			helper.ConsoleCommands.Add("aisv_gift", "AI 礼物调试，dump 写到 debug/。\n用法：aisv_gift dump | candidates <村民> | json <村民> [心数] | make <村民> [物品] [ai] | letter <村民> | clear", (_, args) => GiftCommand(args, debugFile));
			_adult = new AISV.Game.Adult.AdultMemoryService(() => _config, store, _log) { Display = _npcs.Names.Display };
			materials.ReplacedHeartEvent = _adult.Catalog.IsReplacedHeartEvent;
			materials.AdultTouched = _adult.Catalog.Touches;
			_social.IsReplacedHeartEvent = _adult.Catalog.IsReplacedHeartEvent;
			helper.ConsoleCommands.Add("aisv_adult", "成人事件记忆调试。\n用法：aisv_adult dump | activate <包…|all|none> | remember <事件> | prompt <村民> | clear", (_, args) => _log.Info(Context.IsWorldReady ? _adult.Command(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_commission", "委托调试，dump 写到 debug/。\n用法：aisv_commission dump | post [种类] [村民] [物品，只限送货] | accept <id> | abandon <id> | expire <id> | clear", (_, args) => _log.Info(Context.IsWorldReady ? _commissions.Command(args, debugFile) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_market", "市场调试，结果写到 debug/。\n用法：aisv_market dump | price <id> [品质] | bin <id> <件数> [品质] | advance <天数> [id:件数,...] | set <id> <系数>", (_, args) => _log.Info(Context.IsWorldReady ? _market.Command(args, debugFile) : "先进存档。"));
			_awareness.IsTalking = n => _conversations.IsTalkingWith(n, ConversationController.TalkGraceTicks);
			// 招募键不因为约见挡人：Squad 带走时约见自己取消。
			_squad = new SquadBridge(helper, _log, () => _config, n => _approaches.IsNpcBusy(n) || fishing(n) || _groups.IsNpcInvolved(n) || _conversations.IsTalkingWith(n), Relationships.RawHearts);
			_schedules = new ScheduleOverlayService(() => _config, store, helper.Reflection, n => _conversations.IsTalkingWith(n, ConversationController.TalkGraceTicks) || _approaches.IsNpcBusy(n) || fishing(n) || _groups.IsNpcInvolved(n), _log);
			_meetups = new MeetupService(() => _config, store, _schedules, _log);
			_farmHelp = new FarmHelpService(() => _config, store, _schedules, _meetups, () => _social.Feelings, _log) { Crew = _squad };
			_offers = new OfferDesk(() => _config, _meetups, _farmHelp, relationships.EffectiveHearts, _log);
			_squad.Ledger = _offers.Ledger;
			_squad.FarmVisit = _farmHelp.TryHandle;
			_stories = new SpouseStoryService(() => _config, store, _meetups, _eco, _market, _npcs.IsEnabled, _log)
			{
				Ai = _ai,
				Language = PromptLanguage,
				Personality = n => _npcs.Persona(n, StardewValley.Game1.getCharacterFromName(n)).Personality,
				Feelings = () => _social.Feelings,
				FeelingsBlock = (n, hearts) => _social.Feelings.PromptBlock(n, _npcs.Names.Display(n), hearts, GameState.Today.TotalDays),
				PostLetter = _social.PostStoryLetter,
				Text = (n, text) =>
				{
					if (!_messenger.RecordTextedFirst(n, text, TextKind.SmallTalk))
					{
						return false;
					}
					_messenger.Arrived(n, text);
					return true;
				},
				Adult = () => _config.ModOn && _config.AdultModuleOn,
				Display = _npcs.Names.Display,
				Say = _conversations.SayNative
			};
			_commissions.Say = _conversations.SayNative;
			_errands = new ErrandService(() => _config, store, _eco, _npcs, relationships, _log)
			{
				Ai = _ai,
				Language = PromptLanguage,
				Mood = n => _social.Moods.Get(n)?.Temper ?? "",
				PostLetter = _social.PostErrandLetter,
				Remember = (n, text) => _social.Feelings.AddKeyMemory(n, text, GameState.Today),
				Text = (n, text) =>
				{
					if (!MessengerRules.HasPhone(n) || !_messenger.RecordTextedFirst(n, text, TextKind.SmallTalk))
					{
						return false;
					}
					_messenger.Arrived(n, text);
					return true;
				}
			};
			_shell.Errands = _errands;
			_ledger = new LedgerService(() => _config, store, _eco, _npcs, relationships, _log)
			{
				Ai = _ai,
				Language = PromptLanguage,
				PostLetter = _social.PostErrandLetter,
				Text = _errands.Text,
				Gossip = (text, knowers) => _social.Gossip.AddLowKey(text, knowers.ToList(), "ledger", "ledger-" + GameState.Today.TotalDays + "-" + text.GetHashCode(), GameState.Today)
			};
			_shell.Ledger = _ledger;
			_shell.Eco = _eco;
			_shell.TryChat = (npc, card, text, done) =>
			{
				if (StardewValley.Game1.getCharacterFromName(npc) is not StardewValley.NPC n)
				{
					done(null);
					return;
				}
				DialogueMaterial m = materials.Build(n, awarenessFeelings: false, personaOverride: _npcs.Preview(npc, card));
				string opening = Tr.Get("card.trialOpening", "（你们在路上碰见了。）");
				GameDate today = GameState.Today;
				Task.Run(async () =>
				{
					string? said = null;
					try
					{
						said = (await _dialogue.ReplyAsync(m, text, today, currentLine: opening, remember: false).ConfigureAwait(false))?.DisplayText;
					}
					catch (Exception ex)
					{
						_log.Warn($"试聊没成：{ex.Message}");
					}
					MainThread.Run(() => done(said));
				});
			};
			_apps = new AISV.Game.Social.AppsService(store, _npcs, _log);
			_shell.Apps = _apps;
			GameState.SkyTonight = () => AISV.Core.Apps.Observatory.PromptLine(_apps.Tonight());
			helper.ConsoleCommands.Add("aisv_apps", "应用调试。\n用法：aisv_apps meteor | sky | adopt [名字] | junimo [joy|full <数>] | junimo day", (_, args) => _log.Info(Context.IsWorldReady ? _apps.Command(args) : "先进存档。"));
			_ledger.Closeness = (a, b) =>
			{
				float seed = AISV.Core.Persona.BuiltInPersonas.Relationships.Any(r => (r.A.Equals(a, StringComparison.OrdinalIgnoreCase) && r.B.Equals(b, StringComparison.OrdinalIgnoreCase)) || (r.A.Equals(b, StringComparison.OrdinalIgnoreCase) && r.B.Equals(a, StringComparison.OrdinalIgnoreCase))) ? 0.6f : 0f;
				AISV.Core.Social.Web.NpcSocialEdge? edge = _social.Web.Find(a, b);
				return Math.Max(seed, edge == null ? 0f : (edge.Liking + edge.Trust) / 2f + 0.2f);
			};
			_ledger.Moment = _social.AddMoment;
			_social.MomentApplied = _ledger.OnMoment;
			_social.MoneyBetween = _ledger.MoneyBetween;
			_arcs.Money = _ledger.ArcMaterial;
			_gifts.Owned = _ledger.Owned;
			_gifts.GaveAway = _ledger.GaveAway;
			BarTabPatch.Room = _ledger.TabRoom;
			BarTabPatch.Charge = _ledger.ChargeTab;
			BarTabPatch.Apply(harmony, _log);
			_conversations.LoansAvailable = n => _ledger.Enabled && Context.IsMainPlayer && (_ledger.OpenDebt(n) != null || (relationships.EffectiveHearts(n) >= AISV.Core.World.Ledger.LedgerRules.LoanMinHearts && !ChildSafety.IsChild(n, StardewValley.Game1.getCharacterFromName(n)?.Age)));
			_conversations.OpenLoan = npc =>
			{
				ShellMenu menu = new ShellMenu(_shell, ShellMenu.Tab.World, null, ShellMenu.WorldTab.Ledger);
				StardewValley.Game1.activeClickableMenu = menu;
				menu.OpenLedger(npc.Name, loan: true);
			};
			_errands.Completed = _ledger.OnErrandDone;
			_errands.Kept = _ledger.OnErrandKept;
			_commissions.Paid = _ledger.OnCommissionPaid;
			_thief.Recorded = _ledger.OnTheft;
			GiftPatch.Handler += _ledger.OnGift;
			helper.ConsoleCommands.Add("aisv_ledger", "村民账本调试，dump 写到 debug/。\n" + LedgerService.Usage, (_, args) => _log.Info(Context.IsWorldReady ? _ledger.Command(args, debugFile) : "先进存档。"));
			_conversations.ErrandsAvailable = n => _errands.CanPost && _errands.Helpers().Any(h => h.Npc == n && h.Blocked == null);
			_conversations.OpenErrand = npc =>
			{
				ShellMenu menu = new ShellMenu(_shell, ShellMenu.Tab.World, null, ShellMenu.WorldTab.Commissions);
				StardewValley.Game1.activeClickableMenu = menu;
				menu.OpenErrand(npc.Name, (e, reply) =>
				{
					if (StardewValley.Game1.activeClickableMenu is not ShellMenu shown || !shown.IsAwaiting(e.Id))
					{
						return false;
					}
					StardewValley.Game1.activeClickableMenu = null;
					_conversations.SayNative(npc, reply.Line);
					if (reply.Decision == AISV.Core.World.Errands.ErrandDecision.Counter)
					{
						StardewValley.Game1.addHUDMessage(new StardewValley.HUDMessage(Tr.Get("errand.counterHint", "{{name}}还了价，到世界 → 委托 → 我发的里答复。", ("name", _npcs.Names.Display(npc.Name))), 2));
					}
					return true;
				});
			};
			helper.ConsoleCommands.Add("aisv_errand", "给村民发委托调试，list 写到 debug/。\n用法：aisv_errand list | items <类型[:来源]> | post <村民> <类型[:来源]> <物品> [数量] [天数] [收件人] [报酬] | reply <id> accept|decline|counter [金额] | counter <id> yes|no | finish <id> [fail] | withdraw <id> | prompt <村民> | clear", (_, args) => _log.Info(Context.IsWorldReady ? _errands.Command(args, debugFile) : "先进存档。"));
			CommissionPatches.PartnerHandIn = _stories.TryHandIn;
			_conversations.StoryBeat = _stories.TryOfferBeat;
			_shell.Stories = _stories;
			string debugDir = Path.Combine(helper.DirectoryPath, "debug");
			helper.ConsoleCommands.Add("aisv_story", "伴侣剧情调试，dump 写到 debug/。\n用法：aisv_story dump | marry <村民> | start <村民> [结构 1-5] | inject <村民> <结构> <文件> | prompt <村民> [结构] | theme <村民> <文字> | deliver <包> | advance <包> | scene <包> | due [天] | clear", (_, args) => _log.Info(Context.IsWorldReady ? _stories.Command(args, debugFile, debugDir) : "先进存档。"));
			materials.Dealings = n => DialoguePrompts.Join(_squad.PromptLine(n), _gifts.PromptBlock(n), _commissions.PromptBlock(n), _errands.PromptBlock(n), _ledger.PromptBlock(n), _meetups.PromptBlock(n), _stories.PromptBlock(n), _farmHelp.PromptBlock(n), _adult.PromptBlock(n));
			_news = new WeeklyNewsService(() => _config, store, _social, _market, _commissions, _log) { Ai = _ai, Language = PromptLanguage };
			_shell.News = _news;
			_news.Money = _ledger.PaperLines;
			AISV.Game.Promo.ServerContentService content = new AISV.Game.Promo.ServerContentService(_connection, helper.Data, new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, ModManifest.UniqueID, _log);
			content.LoadCached();
			_promo = new AISV.Game.Promo.SoftPromoService(() => _config, store, content, _social, _residents, _arcs, _npcs.IsEnabled, _npcs.Names.Display, ModManifest.UniqueID, _log);
			_news.Beyond = _promo.PaperColumn;
			_news.ShowBeyond = () => _promo.Enabled;
			_shell.Promo = _promo;
			_shell.Log = _log;
			_phone = new AISV.Game.Phone.PhoneItem(helper.DirectoryPath, helper.Input, _log, () => OpenShell(""));
			helper.ConsoleCommands.Add("aisv_phone", "手机物品调试。\n用法：aisv_phone dump | give | hold | lose", (_, args) => _log.Info(Context.IsWorldReady ? _phone.Command(args) : "先进存档。"));
			_ime = new ImeCandidateWindow(() => _config, SaveConfig, _log);
			_ime.Apply();
			helper.ConsoleCommands.Add("aisv_promo", "游戏内推广调试，dump 写到 debug/。\n用法：aisv_promo dump | refresh | content <文件> | due | gossip | group | pelican | arc | letter | support <id> | unsupport | capture", (_, args) => _log.Info(Context.IsWorldReady ? _promo.Command(args, debugFile) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_news", "周报调试，dump 写到 debug/。\n用法：aisv_news dump | publish | deliver | facts | clear", (_, args) => _log.Info(Context.IsWorldReady ? _news.Command(args, debugFile) : "先进存档。"));
			materials.OfferJson = _offers.Json;
			_conversations.ReviewProposal = _offers.ReviewFace;
			_messenger.ReviewProposal = _offers.ReviewText;
			_residents.MeetupRule = _offers.GroupRule;
			_residents.ReviewMeetup = _offers.ReviewGroup;
			_shell.Meetups = _meetups;
			_shell.FarmHelp = _farmHelp;
			_shell.Offers = _offers;
			CompanionCommands companionCommands = new CompanionCommands(_schedules, _meetups, _farmHelp, _offers, debugFile);
			helper.ConsoleCommands.Add("aisv_schedule", "日程改写调试。\n用法：aisv_schedule dump | plan <村民> <地图> <x> <y> [开始] [结束] [天数] | cancel <id> | clear | npc <村民>", (_, args) => _log.Info(Context.IsWorldReady ? companionCommands.Schedule(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_meetup", "约见调试。\n用法：aisv_meetup dump | arrange <村民> <地点> <时间> [天数] | offer <村民> [when] [time] [where] | chat <村民|group> <村民> [when] [time] [where] [活] | answer <村民|group> <yes|no> | cancel <id> | prompt <村民>", (_, args) => _log.Info(Context.IsWorldReady ? companionCommands.Meetup(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_farmhelp", "上门帮工调试。\n用法：aisv_farmhelp dump | arrange <村民> <活> [时间] [天数] [价钱] | offer <村民> <活> <when> [time] | start <id> | end <id> | cancel <id> | shop <村民> <时间> [天数] | prompt <村民> | clear", (_, args) => _log.Info(Context.IsWorldReady ? companionCommands.FarmHelp(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_squad", "同伴调试。\n用法：aisv_squad status | offer <村民> <follow|work> [活] [摘要] | gate <村民> | check <村民>", (_, args) => _log.Info(Context.IsWorldReady ? _squad.Command(args) : "先进存档。"));
			helper.ConsoleCommands.Add("aisv_sh", "看到听到调试。\n用法：aisv_sh fire <类型> [村民] [键=值 ...] | memories <村民> | feelings <村民> | rooms | approaches", (_, args) => AwarenessCommand(args));
		}
		using (timer.Step("挂事件"))
		{
			helper.Events.GameLoop.UpdateTicked += _conversations.OnUpdateTicked;
			helper.Events.GameLoop.UpdateTicked += (_, _) => _talkClock.OnUpdateTicked();
			helper.Events.GameLoop.UpdateTicked += (_, _) => _profilePrompt.OnUpdateTicked();
			helper.Events.GameLoop.UpdateTicked += (_, _) => Notices.Flush();
			helper.Events.GameLoop.UpdateTicked += (_, e) =>
			{
				_festivals.OnUpdateTicked();
				_approaches.OnUpdateTicked();
				_moments.OnUpdateTicked();
				_awareness.OnUpdateTicked();
				_sensors.OnUpdateTicked();
				_looks.OnUpdateTicked();
				_groups.OnUpdateTicked();
				_residents.OnUpdateTicked();
				_ime.OnUpdateTicked(e.Ticks);
				_messenger.OnUpdateTicked();
				_squad.OnUpdateTicked();
				_schedules.OnUpdateTicked();
				_meetups.OnUpdateTicked();
				_farmHelp.OnUpdateTicked();
				_gifts.OnUpdateTicked();
				_adult.OnUpdateTicked();
				_stories.OnUpdateTicked();
				if (StardewValley.Game1.ticks % 30 == 0)
				{
					_villagerTexts.OnUpdateTicked();
				}
			};
			helper.Events.Player.Warped += (_, e) =>
			{
				_sensors.OnWarped(e);
				_groups.OnWarped(e);
				if (e.IsLocalPlayer && _saveData.Current != null)
				{
					_commissions.CheckVisits();
					_stories.OnWarped(e.NewLocation);
				}
			};
			helper.Events.World.NpcListChanged += (_, e) => _sensors.OnNpcListChanged(e);
			helper.Events.Display.MenuChanged += (_, e) => _sensors.OnMenuChanged(e.OldMenu, e.NewMenu);
			helper.Events.Display.RenderedWorld += (_, e) =>
			{
				_approaches.Draw(e.SpriteBatch);
				_sensors.Draw(e.SpriteBatch);
			};
			helper.Events.Content.AssetRequested += OnAssetRequested;
			new MenuTab(helper.DirectoryPath, () => OpenShell("")).Attach(helper);
			helper.Events.Input.ButtonPressed += _conversations.OnButtonPressed;
			helper.Events.Input.ButtonPressed += OnButtonPressed;
			helper.Events.Input.ButtonPressed += (_, e) => _phone.OnButtonPressed(e);
			helper.Events.Input.ButtonPressed += (_, e) => _groups.OnButtonPressed(e);
			helper.Events.Display.MenuChanged += _conversations.OnMenuChanged;
			helper.Events.Display.MenuChanged += (_, e) => _ledger.OnMenuChanged(e.OldMenu, e.NewMenu);
			helper.Events.Display.MenuChanged += (_, e) =>
			{
				if (e.NewMenu == null && e.OldMenu is StardewValley.Menus.DialogueBox && _saveData.Current != null)
				{
					StardewValley.DelayedAction.functionAfterDelay(() => _outings.TryStartHere(), 200);
				}
			};
			helper.Events.GameLoop.TimeChanged += (_, _) =>
			{
				if (_saveData.Current != null)
				{
					_outings.OnTimeChanged();
					_schedules.OnTimeChanged();
					_farmHelp.OnTimeChanged();
					_commissions.CheckVisits();
					_ledger.OnTimeChanged();
					_promo.OnTimeChanged();
				}
			};
			NpcSpeech.Show = HeadBubbles.Show;
			HeadBubbles.Display = _npcs.Names.Display;
			helper.Events.GameLoop.UpdateTicked += (_, _) => HeadBubbles.Update(StardewValley.Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16.7);
			helper.Events.Display.RenderedHud += (_, e) =>
			{
				HeadBubbles.Draw(e.SpriteBatch);
				_groups.OnRenderedHud(e.SpriteBatch);
				ThinkingOverlay.Draw(e.SpriteBatch);
			};
			helper.Events.GameLoop.DayEnding += OnDayEnding;
			helper.Events.GameLoop.GameLaunched += OnGameLaunched;
			helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
			helper.Events.GameLoop.DayStarted += OnDayStarted;
			helper.Events.GameLoop.Saving += OnSaving;
			helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
		}
		_log.Debug(timer.Summary());
	}

	/// <summary>提示词里要求 AI 用的语言（默认跟游戏语言）。</summary>
	private string PromptLanguage() => AISV.Core.Text.PromptLanguage.Resolve(_config.SpeechLanguage, _config.SpeechLanguageName, StardewValley.LocalizedContentManager.CurrentLanguageString);

	private ModConfig ReadConfig(IModHelper helper)
	{
		try
		{
			return helper.ReadConfig<ModConfig>().Normalize();
		}
		catch (Exception ex)
		{
			_log.Warn("config.json 读不出来，先用默认设置：" + ex.Message);
			return new ModConfig();
		}
	}

	private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
	{
		GmcmSettings.Register(Helper, ModManifest, () => _config, c => { _config = c; _moments?.OnStyleChanged(); }, _connection.IsAvailable, ImeCandidateWindow.SetVariable);
		RefreshAccountInBackground();
		StartCompatibilityScan();
		_squad.OnGameLaunched();
		_promo.Content.Refresh();
		_adult.LoadCatalog(Helper.DirectoryPath, Helper.ModRegistry);
	}

	/// <summary>信要等存档里有了才能寄；其余交给礼物服务。</summary>
	private void GiftCommand(string[] args, Func<string, object, string> debugFile)
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("先进存档。");
			return;
		}
		_log.Info(args.Length > 1 && args[0].Equals("letter", StringComparison.OrdinalIgnoreCase) ? _social.QueueTestGiftLetter(args[1]) : _gifts.Command(args, debugFile));
	}

	/// <summary>
	/// 找出 mod 村民的来源、加载 AI 排除名单。都在后台做；
	/// 扫描结果有缓存，mod 没变时第二次启动几乎不花时间。扫完之前，mod 村民按"来源未知"判断。
	/// </summary>
	private void StartCompatibilityScan()
	{
		List<string> loaded = new List<string>();
		foreach (IModInfo mod in Helper.ModRegistry.GetAll())
		{
			loaded.Add(mod.Manifest.UniqueID);
		}
		_npcs.Eligibility.SetLoadedMods(loaded);
		ModFolders.Warn = _log.Warn;
		IReadOnlyList<ModFolder> packs = ModFolders.ContentPatcherPacks(Helper.ModRegistry);
		IReadOnlyList<ModFolder> all = ModFolders.All(Helper.ModRegistry);
		_npcs.Eligibility.SetModFamilies(all);
		string url = _config.OptOutListOn ? _config.OptOutListUrl : "";
		Task.Run(async () =>
		{
			try
			{
				StepTimer timer = new StepTimer("AI星露谷 兼容扫描（后台）");
				int rescanned;
				using (timer.Step("NPC 归属"))
				{
					_npcs.Eligibility.SetOwners(_ownership.Scan(packs, out rescanned));
				}
				using (timer.Step("AI 排除名单"))
				{
					await _optOut.RefreshAsync(url, all);
				}
				_log.Debug(timer.Summary() + $"（重新扫了 {rescanned}/{packs.Count} 个内容包）");
			}
			catch (Exception ex)
			{
				_log.Warn("兼容扫描失败，mod 村民按来源未知处理：" + ex.Message);
			}
		});
	}

	/// <summary>内容包的 JSON 由 SMAPI 宽松读取；标准 JSON 读不了的文件用 SMAPI 自带的 Newtonsoft 转一遍。</summary>
	/// <remarks>显式传转换器数组：只传 Formatting 会绑定到新版 Newtonsoft 才有的重载，旧版 SMAPI 会拒绝加载整个 mod。</remarks>
	private static string? LenientJson(string text) => Newtonsoft.Json.Linq.JToken.Parse(text).ToString(Newtonsoft.Json.Formatting.None, Array.Empty<Newtonsoft.Json.JsonConverter>());

	private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
	{
		AISV.Game.World.HeartEventSource.Refresh(Helper, _log);
		StepTimer timer = new StepTimer("AI星露谷 进存档");
		using (timer.Step("打开存档数据"))
		{
			_saveData.Open();
		}
		using (timer.Step("村民快照"))
		{
			_npcs.RefreshWorld();
		}
		_social.Reset();
		_awareness.OnSaveLoaded();
		_sensors.OnSaveLoaded();
		_groups.Reset();
		_residents.Reset();
		_messenger.Reset();
		_villagerTexts.Reset();
		_favors.OnSaveLoaded();
		_arcs.OnSaveLoaded();
		_moments.OnSaveLoaded();
		_eco.Invalidate();
		_market.OnSaveLoaded();
		_commissions.OnSaveLoaded();
		_errands.OnSaveLoaded();
		_ledger.OnSaveLoaded();
		_gifts.OnSaveLoaded();
		_adult.OnSaveLoaded();
		_squad.OnSaveLoaded();
		_schedules.OnSaveLoaded();
		_meetups.OnSaveLoaded();
		_stories.OnSaveLoaded();
		_news.OnSaveLoaded();
		_promo.OnSaveLoaded();
		_farmHelp.OnSaveLoaded();
		_offers.OnSaveLoaded();
		// 角色卡放到后台读，主线程上拼提示词时只查内存。
		Task.Run(() =>
		{
			_npcs.Cards.Preload();
			MainThread.Run(_npcs.RefreshNames);
		});
		_log.Debug(timer.Summary());
	}

	private void OnDayStarted(object? sender, DayStartedEventArgs e)
	{
		AISV.Game.World.HeartEventSource.Refresh(Helper, _log);
		HeadBubbles.Clear();
		_eco.Invalidate();
		_conversations.EndDayTransition();
		_talkClock.Reset();
		_profilePrompt.OnDayStarted();
		_festivals.Reset();
		_dialogue.StartNewDay();
		_ai.StartNewDay();
		_npcs.Eligibility.StartNewDay();
		_npcs.RefreshWorld();
		_social.OnDayStarted();
		_arcs.OnDayStarted();
		_awareness.OnDayStarted();
		_sensors.OnDayStarted();
		_groups.OnDayStarted();
		_villagerTexts.OnDayStarted();
		_favors.OnDayStarted();
		_outings.OnDayStarted();
		_scenes.OnDayStarted();
		_moments.OnDayStarted();
		_market.OnDayStarted();
		_commissions.OnDayStarted();
		_errands.OnDayStarted();
		_ledger.OnDayStarted();
		_apps.OnDayStarted();
		_squad.OnDayStarted();
		_schedules.OnDayStarted();
		_meetups.OnDayStarted();
		_stories.OnDayStarted();
		_news.TryDeliverMail();
		_promo.OnDayStarted();
		_phone.OnDayStarted();
		_farmHelp.OnDayStarted();
		_offers.OnDayStarted();
		RefreshAccountInBackground();
	}

	private void OnDayEnding(object? sender, DayEndingEventArgs e)
	{
		_conversations.BeginDayTransition();
		_groups.Reset();
		_festivals.OnDayEnding();
		_sensors.OnDayEnding();
		_looks.Reset();
		_awareness.OnDayEnding();
		_moments.OnDayEnding();
		_news.OnDayEnding();
		_ledger.OnDayEnding();
		_social.OnDayEnding();
		_market.OnDayEnding();
		_farmHelp.OnDayEnding();
		_stories.OnDayEnding();
		_schedules.OnDayEnding();
	}

	/// <summary>F9 打开外壳，F6 直接到八卦页（只在能自由行动、没开别的菜单时）。</summary>
	private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
	{
		if (!Context.IsPlayerFree)
		{
			return;
		}
		if (Matches(_config.KeyShell, e.Button))
		{
			OpenShell("");
		}
		else if (Matches(_config.KeyRumors, e.Button))
		{
			OpenShell("gossip");
		}
		else if (Matches(_config.KeyAboutMe, e.Button))
		{
			OpenShell("profile");
		}
	}

	private static bool Matches(string? key, SButton pressed) => !string.IsNullOrWhiteSpace(key) && Enum.TryParse(key.Trim(), true, out SButton b) && b == pressed;

	private void OpenShell(string where)
	{
		string arg = where.Contains(':') ? where[(where.IndexOf(':') + 1)..] : "";
		where = where.Contains(':') ? where[..where.IndexOf(':')] : where;
		if (!Context.IsWorldReady || _saveData.Current == null)
		{
			_log.Info("进了存档才能打开外壳。");
			return;
		}
		(ShellMenu.Tab? tab, ShellMenu.SocialTab? sub) = where.Trim().ToLowerInvariant() switch
		{
			"" => ((ShellMenu.Tab?)null, (ShellMenu.SocialTab?)null),
			"social" => (ShellMenu.Tab.Social, null),
			"contacts" or "villagers" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Contacts),
			"gossip" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Gossip),
			"residents" or "group" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Residents),
			"messenger" or "msg" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Messenger),
			"letters" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Letters),
			"diary" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Diary),
			"arcs" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Arcs),
			"outings" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Outings),
			"partners" or "spouse" => (ShellMenu.Tab.Social, ShellMenu.SocialTab.Partners),
			"world" => (ShellMenu.Tab.World, null),
			"market" or "commissions" or "help" or "farmhelp" or "mine" or "errand" or "ledger" or "belongings" or "loan" => (ShellMenu.Tab.World, null),
			"news" or "paper" => (ShellMenu.Tab.World, null),
			"settings" or "profile" or "cards" => (ShellMenu.Tab.Settings, null),
			"today" => (ShellMenu.Tab.Today, null),
			"apps" or "mood" or "year" or "web" or "gtree" or "reviews" or "pelican" or "agenda" or "horoscope" or "sky" or "ads" or "whosaid" or "junimo" => (ShellMenu.Tab.Apps, null),
			_ => (null, null)
		};
		ShellMenu.WorldTab? world = where.Trim().ToLowerInvariant() switch
		{
			"market" => ShellMenu.WorldTab.Market,
			"commissions" or "mine" or "errand" => ShellMenu.WorldTab.Commissions,
			"ledger" or "belongings" or "loan" => ShellMenu.WorldTab.Ledger,
			"help" or "farmhelp" => ShellMenu.WorldTab.Help,
			"news" or "paper" => ShellMenu.WorldTab.News,
			"world" => ShellMenu.WorldTab.Village,
			_ => null
		};
		if (where.Trim().ToLowerInvariant() == "mine")
		{
			ShellMenu.ShowMyErrands();
		}
		ShellMenu menu = new ShellMenu(_shell, tab, sub, world);
		StardewValley.Game1.activeClickableMenu = menu;
		switch (where.Trim().ToLowerInvariant())
		{
		case "errand":
			menu.OpenErrand(arg);
			return;
		case "belongings":
		case "loan":
			menu.OpenLedger(arg, where.Trim().ToLowerInvariant() == "loan");
			return;
		case "profile":
			menu.OpenProfile();
			return;
		case "cards":
			menu.OpenCards();
			return;
		}
		if (tab == ShellMenu.Tab.Apps && where.Trim().ToLowerInvariant() is string app && app != "apps")
		{
			menu.OpenApp(app == "gtree" ? "gossip" : app);
		}
	}

	private string GossipCommand(string[] args)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
		GameDate today = GameState.Today;
		switch (sub)
		{
			case "inject" when args.Length >= 3:
			{
				string speaker = _npcs.Names.Internal(args[1]);
				string rest = string.Join(" ", args.Skip(2));
				string[] parts = rest.Split('|', 2);
				string fact = parts[0].Trim(), said = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
				bool ok = _social.Gossip.AddAuthored(fact, said, speaker, "debug", Guid.NewGuid().ToString("N")[..8], 7, today);
				return ok ? $"{speaker} 起头传开了：{fact}（原话：{said}）。晚上整理时会继续传。" : "没加进去（八卦没开？）。";
			}
			case "spread":
				return $"按今天传了一轮：{_social.Gossip.SpreadForDay(today.TotalDays)} 条有人新知道了。";
			default:
				List<string> lines = _social.Gossip.Feed().Take(15).Select(e => $"[{e.Season} {e.Day}] {e.StartedBy}：{e.Fact}｜{e.Rumor}（{e.KnownTo.Count} 人知道）").ToList();
				return lines.Count == 0 ? "没有八卦。" : string.Join("\n", lines);
		}
	}

	/// <summary>失主发现少了东西，不知道是谁：心情变差，这件事在镇上传开。</summary>
	private void OnTheftNoticed(TheftRecord r, string place)
	{
		List<string> victims = r.Victims.Where(_npcs.IsEnabled).ToList();
		if (victims.Count == 0)
		{
			return;
		}
		foreach (string v in victims)
		{
			_social.Moods.Adjust(v, TheftText.MoodDrop(r.Value));
			_dialogue.Invalidate(v);
		}
		_social.Gossip.AddLowKey(TheftText.LossGossip(r, _npcs.Names.Display, place), victims, "stardew_thief", r.Id, GameState.Today);
	}

	private string ThiefCommand(string[] args)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		if (sub == "sim" && args.Length > 1)
		{
			bool caught = args.Any(a => a.Equals("caught", StringComparison.OrdinalIgnoreCase));
			string? npc = args.Length > 2 && !args[2].Equals("caught", StringComparison.OrdinalIgnoreCase) ? args[2] : null;
			return _thief.Simulate(args[1].ToLowerInvariant(), npc, caught);
		}
		IReadOnlyList<TheftRecord> all = _thief.Log.All;
		string json = System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
		Directory.CreateDirectory(Path.Combine(Helper.DirectoryPath, "debug"));
		File.WriteAllText(Path.Combine(Helper.DirectoryPath, "debug", "thefts.json"), json);
		return $"Stardew Thief {(_thief.Installed ? "已装" : "没装")}，偷窃记录 {all.Count} 条，写到 debug/thefts.json。";
	}

	private void SaveConfig(ModConfig config)
	{
		_config = config;
		Helper.WriteConfig(config);
		_moments?.OnStyleChanged();
	}

	private void DramaCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Warn("先进存档。");
			return;
		}
		AISV.Core.Social.Drama.SocialDrama drama = _social.Drama;
		string sub = args.Length > 0 ? args[0] : "status";
		if (sub.Equals("status", StringComparison.OrdinalIgnoreCase))
		{
			AISV.Core.Social.Drama.DramaState d = drama.Data;
			_log.Info($"社交剧情{(_config.DramaOn ? "开着" : "关着")}，强度 {_config.DramaHeat}：看法 {d.Opinions.Count} 条、传闻 {d.Rumors.Count} 条、片段 {d.Beats.Count} 个、农夫的影响 {d.Influences.Count} 次、成人关系 {d.Adult.Count} 份。");
			return;
		}
		if (sub.Equals("hostility", StringComparison.OrdinalIgnoreCase))
		{
			IReadOnlyList<(string Npc, float Hostility)> all = drama.Hostility(1);
			if (all.Count == 0)
			{
				_log.Info("还没有村民之间的看法。");
				return;
			}
			int threshold = Math.Clamp(_config.DepartureGrudgePercent, 0, 100);
			HashSet<string> counted = drama.Hostility().Select(x => x.Npc).ToHashSet(StringComparer.OrdinalIgnoreCase);
			List<string> lines = new List<string> { $"全村敌意排名（阈值 {threshold}%；至少 3 个人有看法才算候选）：" };
			lines.AddRange(all.Select(x => $"  {x.Npc}：{x.Hostility:P0}{(!counted.Contains(x.Npc) ? "（不到 3 人，不算）" : x.Hostility >= threshold / 100f ? "（候选）" : "")}"));
			_log.Info(string.Join("\n", lines));
			return;
		}
		string block = drama.PromptBlock(sub);
		_log.Info(block.Length == 0 ? $"{sub} 眼下没有要写进提示词的社交剧情。" : $"{sub} 的社交剧情提示：{block}");
	}

	private void SceneCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Warn("先进存档。");
			return;
		}
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
		if (sub == "arm" && args.Length > 1)
		{
			string text = _scenes.Arm(args[1], out bool ok);
			if (ok)
			{
				_moments.ResetTries(args[1]);
			}
			_log.Info(text);
			return;
		}
		if (sub == "style" && args.Length > 1)
		{
			_config.EncounterStyle = SceneStyle.Normalize(args[1]);
			SaveConfig(_config);
			_log.Info($"偶遇风格：{_config.EncounterStyle}");
			return;
		}
		if (sub != "status")
		{
			_log.Info("用法：aisv_scene status | arm <场景 id> | style <Natural|AmbientOnly|Cinematic>");
			return;
		}
		_log.Info(string.Join("\n", _scenes.Describe().Concat(_moments.Describe())));
	}

	private void LooksCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("先进存档。");
			return;
		}
		string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
		StardewValley.NPC? npc = args.Length > 1 ? StardewValley.Game1.getCharacterFromName(args[1]) : null;
		switch (verb)
		{
		case "soot" when npc?.currentLocation != null:
			_looks.OnExplosion(npc.currentLocation, npc.Tile, 1);
			_log.Info($"{npc.Name} 沾上了烟灰。");
			break;
		case "dance" when npc != null:
			_log.Info(_looks.HasDanceOutfit(npc.Name) ? $"{npc.Name} 换上舞裙。" : $"{npc.Name} 没有舞裙图（本来就有自己的花舞节样子）。");
			_looks.WearDanceOutfit(npc);
			if (args.Length > 2 && int.TryParse(args[2], out int frame))
			{
				npc.Halt();
				npc.Sprite.StopAnimation();
				npc.Sprite.CurrentFrame = frame;
			}
			break;
		case "clear":
			_looks.Reset();
			_log.Info("都换回来了。");
			break;
		default:
			_log.Info("用法：aisv_looks soot <村民> | dance <村民> [帧] | clear");
			break;
		}
	}

	/// <summary>Data/mail 里加上村民的信。</summary>
	private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
	{
		_looks.OnAssetRequested(e);
		_phone.OnAssetRequested(e);
		if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
		{
			e.Edit(asset => AISV.Game.Phone.PhoneItem.EditMail(asset.AsDictionary<string, string>().Data));
		}
		if (_saveData.Current != null)
		{
			ContentCache.ProvideMissingEventTable(e);
			_favors.OnAssetRequested(e);
			_outings.OnAssetRequested(sender, e);
			_scenes.OnAssetRequested(sender, e);
		}
		if (e.NameWithoutLocale.IsEquivalentTo("Data/mail") && _saveData.Current != null)
		{
			e.Edit(asset =>
			{
				IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
				_social.EditMail(data);
				_news.EditMail(data);
				_promo.EditMail(data);
			});
		}
	}

	private void OnSaving(object? sender, SavingEventArgs e)
	{
		_sensors.OnSaving();
		_awareness.OnSaving();
		StepTimer timer = new StepTimer("AI星露谷 保存");
		int written;
		using (timer.Step("写存档数据"))
		{
			written = _saveData.Commit();
		}
		_log.Debug(timer.Summary() + $"（写了 {written} 个文件）");
	}

	private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
	{
		_conversations.ResetDaily();
		_conversations.EndDayTransition();
		_talkClock.Reset();
		_profilePrompt.Reset();
		_dialogue.Reset();
		_festivals.Reset();
		_sensors.OnReturnedToTitle();
		_looks.Reset();
		_awareness.OnReturnedToTitle();
		_moments.OnReturnedToTitle();
		_market.OnReturnedToTitle();
		_commissions.OnReturnedToTitle();
		_errands.OnReturnedToTitle();
		_ledger.OnReturnedToTitle();
		_gifts.OnReturnedToTitle();
		_adult.OnReturnedToTitle();
		_squad.OnReturnedToTitle();
		_schedules.OnReturnedToTitle();
		_meetups.OnReturnedToTitle();
		_stories.OnReturnedToTitle();
		_news.OnReturnedToTitle();
		_offers.OnReturnedToTitle();
		_groups.Reset();
		_residents.Reset();
		_messenger.Reset();
		_villagerTexts.Reset();
		_social.Reset();
		_saveData.Close();
	}

	private void GroupCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("进存档以后再用。");
			return;
		}
		string action = args.Length > 0 ? args[0].ToLowerInvariant() : "state";
		string rest = string.Join(" ", args.Skip(1));
		switch (action)
		{
		case "start":
			_log.Info(_groups.DebugStart());
			break;
		case "ambient":
			_log.Info(_groups.DebugAmbient());
			break;
		case "farmertopic":
			_groupMaterial.Topics.Always = rest.Trim().Equals("always", StringComparison.OrdinalIgnoreCase);
			_log.Info(_groupMaterial.Topics.Always ? "闲聊和旁听：有农夫的新鲜事就一定聊。" : "闲聊和旁听：农夫的事照常按机会聊。");
			break;
		case "key":
			_groups.OnGroupKey();
			_log.Info(_groups.Describe());
			break;
		case "click":
			_log.Info(_groups.DebugClick(rest) ? "当作插话处理了。" : "没点中正在群聊的人。");
			break;
		case "post":
			_log.Info("居民群：" + _residents.Send(rest));
			break;
		case "villagers":
			_log.Info(_residents.StartVillagerChat() ? "村民自己聊起来了。" : "现在聊不起来（忙、AI 不可用或群里人太少）。");
			break;
		case "residents":
			_log.Info($"居民群：{_residents.Members().Count} 位成员，今天还能发 {_residents.PostsLeft} 条，记录 {_residents.Messages.Count} 条，{(_residents.IsBusy ? "正在回复" : "空闲")}。");
			break;
		default:
			_log.Info(_groups.Describe());
			break;
		}
	}

	private void OnTextedBeside(StardewValley.NPC npc, string device)
	{
		string farmer = AwarenessSystem.PlayerName;
		string where = npc.currentLocation?.NameOrUniqueName ?? StardewValley.Game1.currentLocation.NameOrUniqueName;
		Dictionary<string, string> details = Observation.Map(("device", device), ("nearPhrase", device == "手机" ? "手机屏幕亮着" : "隔着电脑屏幕"));
		_awareness.Record(new Observation("phone_text", farmer, npc.Name, where, $"{farmer}站在{npc.displayName}身边，却给{npc.displayName}发消息。", npc.Tile, details, Exclude: new[] { npc.Name }));
	}

	private void MessengerCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("进存档以后再用。");
			return;
		}
		string action = args.Length > 0 ? args[0].ToLowerInvariant() : "state";
		string who = args.Length > 1 ? args[1] : "";
		string rest = string.Join(" ", args.Skip(2));
		switch (action)
		{
		case "send" when who.Length > 0:
			SendBlock sent = _messenger.Send(who, rest, keepSecret: false);
			_log.Info(sent == SendBlock.None ? "信使：发出去了。" : $"信使：没发出去（{sent}）。");
			break;
		case "text" when who.Length > 0:
			_log.Info(_villagerTexts.DebugStart(who, Enum.TryParse(rest.Replace("_", ""), true, out TextKind kind) ? kind : null));
			break;
		case "seen" when who.Length > 0:
			_log.Info(_villagerTexts.TryRequestText(who, "你看到的事：" + (rest.Length > 0 ? rest : "农夫在广场上翻了垃圾桶。"), () => _log.Info("看到的事：消息发出去了。")) ? "开始写了。" : "现在发不了。");
			break;
		case "open" when who.Length > 0:
			ShellMenu.PreselectChat(who);
			OpenShell("messenger");
			break;
		default:
			_log.Info($"信使：每人每天 {_messenger.DailyLimit} 条，等回复 {_messenger.PendingCount} 条，未读 {string.Join(", ", _messenger.Unread)}。{_villagerTexts.Describe()}");
			break;
		}
	}

	private void AwarenessCommand(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("进存档以后再用。");
			return;
		}
		string action = args.Length > 0 ? args[0].ToLowerInvariant() : "";
		string who = args.Length > 1 ? args[1] : "";
		switch (action)
		{
		case "memories":
			_log.Info(_awareness.DescribeMemories(who));
			break;
		case "approaches":
			_log.Info(_approaches.Describe());
			break;
		case "rooms":
			_log.Info(_sensors.DescribeRooms());
			break;
		case "feelings":
			_log.Info(_awareness.Feelings.Describe(who, AwarenessSystem.NowMinute));
			break;
		case "fire" when who.Length > 0:
		{
			string? subject = args.Length > 2 && !args[2].Contains('=') ? args[2] : null;
			Dictionary<string, string> details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string pair in args.Skip(subject == null ? 2 : 3))
			{
				string[] kv = pair.Split('=', 2);
				if (kv.Length == 2)
				{
					details[kv[0]] = kv[1];
				}
			}
			string farmer = AwarenessSystem.PlayerName;
			string summary = subject == null ? $"{farmer}做了件事（{who}）。" : $"{farmer}对{subject}做了件事（{who}）。";
			foreach (string line in _awareness.DebugFire(new Observation(who, farmer, subject, StardewValley.Game1.currentLocation.NameOrUniqueName, summary, null, details, Exclude: subject == null ? null : new[] { subject })))
			{
				_log.Info(line);
			}
			break;
		}
		default:
			_log.Info("用法：aisv_sh fire <类型> [村民] [键=值 ...] | memories <村民> | feelings <村民> | rooms | approaches");
			break;
		}
	}

	/// <summary>启动时和每天早上查一次账号（余额、上限、可选模型），不挡主线程。</summary>
	private void RefreshAccountInBackground()
	{
		if (!_connection.IsAvailable || !_account.IsLoggedIn)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await _account.RefreshMeAsync();
			}
			catch (Exception ex)
			{
				_log.Warn("查账号失败：" + ex.Message);
			}
		});
	}
}
