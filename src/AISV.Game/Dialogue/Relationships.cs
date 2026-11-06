using System;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using StardewValley;

namespace AISV.Game.Dialogue;

/// <summary>好感和敌意：读心数、按回复加减、让村民做出反应（跳一下、转身、冒表情）。</summary>
public sealed class Relationships
{
	private readonly Func<ModConfig> _config;

	private readonly RelationshipStates _states;

	private readonly ILog _log;

	public Relationships(Func<ModConfig> config, RelationshipStates states, ILog log)
	{
		_config = config;
		_states = states;
		_log = log;
	}

	private RelationshipRules Rules => new RelationshipRules(_config().HostilityOn);

	public static int FriendshipPoints(string npcName) => Game1.player != null && Game1.player.friendshipData.TryGetValue(npcName, out Friendship? f) ? f.Points : 0;

	/// <summary>游戏里的好感心数。</summary>
	public static int RawHearts(string npcName) => Math.Max(0, FriendshipPoints(npcName)) / RelationshipRules.PointsPerHeart;

	/// <summary>扣掉敌意后的心数（没开敌意系统时就是好感心数）。</summary>
	public int EffectiveHearts(string npcName) => Rules.EffectiveHearts(FriendshipPoints(npcName), _states.EnmityPoints(npcName));

	public int EnmityHearts(string npcName) => Rules.UsesEnmity ? _states.EnmityHearts(npcName) : 0;

	public int ClampInteraction(int points) => Rules.ClampInteraction(points, _config().PointsCapPerReply);

	/// <summary>
	/// 加减好感：负分记成敌意（开敌意系统时），正分先抵掉一部分敌意再加好感。
	/// 村民在世界里时走游戏自己的 changeFriendship（遵守上限、发花束信等）；不在时直接改并照游戏的上限和信件规则。
	/// </summary>
	public void Add(string npcName, int points)
	{
		if (points == 0)
		{
			return;
		}
		RelationshipRules rules = Rules;
		if (points < 0)
		{
			if (rules.UsesEnmity)
			{
				_states.AddEnmity(npcName, Math.Abs(points));
				_log.Debug($"{npcName} 的敌意 +{Math.Abs(points)}。");
			}
			return;
		}
		(int friendship, int reduced) = rules.AllocatePositive(points, _states.EnmityPoints(npcName));
		if (reduced > 0)
		{
			_states.ReduceEnmity(npcName, reduced);
			_log.Debug($"{npcName} 的敌意 -{reduced}。");
		}
		if (friendship <= 0)
		{
			return;
		}
		Farmer player = Game1.player;
		if (!player.friendshipData.ContainsKey(npcName))
		{
			player.friendshipData.Add(npcName, new Friendship(0));
		}
		Friendship f = player.friendshipData[npcName];
		int before = f.Points;
		NPC? npc = Game1.getCharacterFromName(npcName);
		if (npc != null)
		{
			player.changeFriendship(friendship, npc);
		}
		else
		{
			AddWithoutNpc(player, npcName, f, friendship);
		}
		_log.Debug($"{npcName} 的好感 +{friendship}：{before} → {f.Points}。");
	}

	private static void AddWithoutNpc(Farmer player, string npcName, Friendship f, int amount)
	{
		if (f.IsDivorced())
		{
			return;
		}
		bool romanceable = Game1.characterData.TryGetValue(npcName, out var data) && data.CanBeRomanced;
		int max = ReactionEffects.MaxFriendshipPoints(romanceable, f.IsDating(), f.IsMarried());
		f.Points = ReactionEffects.ApplyFriendshipChange(f.Points, amount, max);
		if (romanceable && f.Points >= 2000 && !player.hasOrWillReceiveMail("Bouquet"))
		{
			Game1.addMailForTomorrow("Bouquet");
		}
		if (romanceable && f.Points >= 2500 && !player.hasOrWillReceiveMail("SeaAmulet"))
		{
			Game1.addMailForTomorrow("SeaAmulet");
		}
		Game1.stats.checkForFriendshipAchievements();
	}

	/// <summary>村民对这次回复的小动作。</summary>
	public void React(NPC npc, string emotion, int friendshipPoints)
	{
		try
		{
			EmotePlan? plan = ReactionEffects.Emote(emotion, friendshipPoints, EffectiveHearts(npc.Name));
			if (plan == null)
			{
				return;
			}
			if (plan.Halt)
			{
				npc.Halt();
			}
			if (plan.TurnAway)
			{
				npc.faceDirection((npc.FacingDirection + 2) % 4);
			}
			if (plan.Jump)
			{
				npc.jump();
			}
			npc.doEmote(plan.Emote);
		}
		catch (Exception ex)
		{
			_log.Warn($"{npc.Name} 做反应动作时出错：{ex.Message}");
			if (ReactionEffects.PlainEmote(emotion) is int emote)
			{
				npc.doEmote(emote);
			}
		}
	}
}
