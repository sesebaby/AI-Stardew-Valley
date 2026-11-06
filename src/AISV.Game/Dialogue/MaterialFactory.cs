using System;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.World;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewValley;

namespace AISV.Game.Dialogue;

/// <summary>
/// 主线程：把一位村民此刻的情况整理成 <see cref="DialogueMaterial"/>。
/// 人设、"关于农夫"、世界背景、地点、几天没聊、游戏原台词，加上社交模拟的各块（心情、八卦、情感记忆、礼物、里程碑、
/// 村民之间的事、性格演变、农夫生日）。承诺、拜托、故事弧等块由后面几期的子系统填。
/// </summary>
public sealed class MaterialFactory
{
	private readonly NpcDirectory _npcs;

	private readonly Relationships _relationships;

	private readonly ConversationLog _log;

	private readonly Func<DocumentStore?> _store;

	/// <summary>社交模拟（组装时接上；没接时这些块留空）。</summary>
	public SocialSystem? Social { get; set; }

	/// <summary>"你亲眼见过的事"：(村民, 要不要带心事) → 提示块。</summary>
	public Func<string, bool, string>? Awareness { get; set; }

	/// <summary>托付和物品请求的提示块：村民 → 文字。节日上不放。</summary>
	public Func<string, string>? Favors { get; set; }

	/// <summary>和农夫之间的往来：跟着走、送过的礼物、委托、约见、帮工、私密记忆。</summary>
	public Func<string, string>? Dealings { get; set; }

	/// <summary>回复 JSON 里的提议字段；参数是"在发消息"。</summary>
	public Func<bool, string, int, string>? OfferJson { get; set; }

	/// <summary>回复 JSON 里的礼物字段：(村民, 心数) → 说明；只在当面聊天时给。</summary>
	public Func<string, int, string>? GiftJson { get; set; }

	/// <summary>被成人内容包换掉的游戏自带心事件。</summary>
	public Func<int, bool>? ReplacedHeartEvent { get; set; }

	/// <summary>成人内容包改过这个人的对话：游戏自带台词不进提示词。</summary>
	public Func<string, bool>? AdultTouched { get; set; }

	public Func<string, string>? ItemRequest { get; set; }

	/// <summary>这位村民卷进去的小剧场（只在平常的当面对话里给）。</summary>
	public Func<string, string>? Arcs { get; set; }

	/// <summary>已定传说和离开小镇的往事。</summary>
	public Func<string, string>? Lore { get; set; }

	/// <summary>设置里开了成人关系戏剧。</summary>
	public Func<bool>? AdultDramaAllowed { get; set; }

	public MaterialFactory(NpcDirectory npcs, Relationships relationships, ConversationLog log, Func<DocumentStore?> store)
	{
		_npcs = npcs;
		_relationships = relationships;
		_log = log;
		_store = store;
	}

	/// <param name="awarenessFeelings">带上村民想说的心事（会把它用掉）；走过来时的反应和指定话题时不带。</param>
	public DialogueMaterial Build(NPC npc, string? location = null, string vanillaLine = "", string openingFocus = "", bool awarenessFeelings = true, AISV.Core.Festivals.FestivalPack? festival = null, string? messengerSection = null, NpcPersona? personaOverride = null)
	{
		string name = npc.Name;
		Farmer player = Game1.player;
		int hearts = _relationships.EffectiveHearts(name);
		int raw = Relationships.RawHearts(name);
		GameDate today = GameState.Today;
		NpcPersona persona = personaOverride ?? _npcs.Persona(name, npc);
		WorldSnapshot world = GameState.World();
		string facts = WorldFacts.Build(world, GameState.NpcState(npc, name, hearts), _npcs.IsEnabled, ReplacedHeartEvent, _npcs.Names.Display);
		if (AdultTouched?.Invoke(name) == true)
		{
			vanillaLine = "";
		}
		int days = _log.DaysSinceLastTalk(name, today);
		string daysLine = WorldData.DaysSinceTalk(days);
		PlayerProfile? profile = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		string place = location ?? npc.currentLocation?.Name ?? Game1.currentLocation?.Name ?? "Town";
		SocialSystem? social = Social;
		string display = _npcs.Names.Display(name);
		string playerName = player.Name ?? "";
		bool birthday = social?.IsPlayerBirthday() ?? false;
		return new DialogueMaterial
		{
			Festival = festival,
			Messenger = messengerSection != null,
			ChannelSection = messengerSection ?? "",
			MaxReplyChars = messengerSection != null ? AISV.Core.Messenger.MessengerRules.MaxReplyChars : 0,
			NpcName = name,
			DisplayName = personaOverride is { DisplayNameOverride.Length: > 0 } ? personaOverride.DisplayNameOverride : _npcs.Names.Display(name),
			PlayerName = player.Name ?? "",
			Hearts = hearts,
			RawHearts = raw,
			EnmityHearts = _relationships.EnmityHearts(name),
			Season = world.Season,
			Day = world.Day,
			Year = world.Year,
			Weather = GameState.Weather(npc.currentLocation ?? Game1.currentLocation),
			Location = place,
			IsSpouse = GameState.IsSpouse(name),
			IsAdultRomanticPartner = IsAdultRomanticPartner(npc, persona.Personality),
			Personality = persona.Personality,
			CardRewritten = persona.Source == PersonaSource.Card,
			IsChildCoded = ChildSafety.IsChildCoded(npc.Name, npc.Age, persona.Personality),
			PlayerProfile = PlayerProfileBlock.Build(name, player.Name ?? "", hearts, profile, GameState.IsSpouse(name), GameState.IsDivorced(name)),
			ReplyPlayerProfile = PlayerProfileBlock.Build(name, player.Name ?? "", raw, profile, GameState.IsSpouse(name), GameState.IsDivorced(name)),
			WorldContext = DialoguePrompts.Join(facts, daysLine.Length == 0 ? "" : "- " + daysLine, _npcs.Eligibility.SveContentEnabled ? DialoguePrompts.SveWorld : "", social?.BirthdayOpening(name) ?? "", Dealings?.Invoke(name) ?? ""),
			ModContext = _npcs.ContextBlock(name, npc, location ?? npc.currentLocation?.NameOrUniqueName),
			LocationContext = LocationFacts.Build(place, Game1.timeOfDay, GameState.Others(name, _npcs.IsEnabled)?.Select(_npcs.Names.Display).ToList()),
			VanillaLine = vanillaLine,
			OpeningFocus = openingFocus,
			EventsSeen = world.EventsSeen,
			IsReplacedHeartEvent = ReplacedHeartEvent,
			Mood = social?.Moods.PromptBlock(name) ?? "",
			LowMood = social?.Moods.IsLow(name) ?? false,
			HighMood = social?.Moods.IsHigh(name) ?? false,
			GossipMemory = social?.Gossip.MemoryFor(name, playerName, today.TotalDays) ?? "",
			Awareness = Awareness?.Invoke(name, awarenessFeelings && string.IsNullOrWhiteSpace(openingFocus)) ?? "",
			TodayInteractions = social?.Gossip.OtherFarmersToday(name, playerName) ?? "",
			EmotionalProfile = social?.Feelings.PromptBlock(name, display, hearts, today.TotalDays) ?? "",
			PersonalityEvolution = social?.Personality.PromptBlock(name) ?? "",
			Gift = social?.Gifts.PromptBlock(name) ?? "",
			Milestones = social?.Milestones.PromptBlock(name) ?? "",
			Favors = festival == null ? Favors?.Invoke(name) ?? "" : "",
			ProposalJson = festival == null ? (OfferJson?.Invoke(messengerSection != null, name, hearts) ?? "") + (messengerSection == null ? GiftJson?.Invoke(name, hearts) ?? "" : "") : "",
			ItemRequest = festival == null ? ItemRequest?.Invoke(name) ?? "" : "",
			Arc = festival == null && messengerSection == null ? Arcs?.Invoke(name) ?? "" : "",
			CanonicalLore = Lore?.Invoke(name) ?? "",
			SocialDrama = social?.Drama.PromptBlock(name) ?? "",
			AdultRelationshipDrama = AdultDramaAllowed?.Invoke() == true && ChildSafety.AgeAllowsRomance(npc.Name, npc.Age, npc.datable.Value) && !ChildSafety.IsChildCoded(npc.Name, npc.Age, persona.Personality),
			NpcInteractions = social?.InteractionsBlock(name) ?? "",
			ReplyBirthday = social?.BirthdayReply(name) ?? "",
			ReplyHistoryNote = birthday ? BirthdayRules.HistoryNote(playerName) : null
		};
	}

	/// <summary>恋人方向只给：年龄允许、不是科罗布斯或孩子、不是室友式婚姻，且正在约会、订婚或已结婚的村民。</summary>
	internal static bool IsAdultRomanticPartner(NPC npc, string personality)
	{
		Farmer player = Game1.player;
		if (!ChildSafety.AgeAllowsRomance(npc.Name, npc.Age, npc.datable.Value) || string.Equals(npc.Name, "Krobus", StringComparison.OrdinalIgnoreCase) || ChildSafety.IsChildCoded(npc.Name, npc.Age, personality))
		{
			return false;
		}
		if (!player.friendshipData.TryGetValue(npc.Name, out Friendship? f) || f.RoommateMarriage)
		{
			return false;
		}
		bool partner = f.IsDating() || f.IsEngaged() || f.IsMarried();
		if (!partner && !GameState.IsSpouse(npc.Name))
		{
			return false;
		}
		return npc.datable.Value || partner;
	}
}
