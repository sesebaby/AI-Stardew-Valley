using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.World;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Quests;

namespace AISV.Game.Dialogue;

/// <summary>主线程上从游戏里取对话要用的状态，交给 Core 拼提示词。</summary>
public static class GameState
{
	public static GameDate Today => new GameDate(Game1.dayOfMonth, Game1.currentSeason ?? "spring", Game1.year);

	public static NPC? FindNpc(string name)
	{
		foreach (GameLocation location in Game1.locations)
		{
			foreach (NPC npc in location.characters)
			{
				if (npc != null && string.Equals(npc.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return npc;
				}
			}
		}
		return null;
	}

	public static bool IsDivorced(string npcName) => Game1.player?.friendshipData.TryGetValue(npcName, out Friendship f) == true && f.IsDivorced();

	public static bool IsSpouse(string npcName) => !string.IsNullOrWhiteSpace(Game1.player?.spouse) && string.Equals(Game1.player.spouse, npcName, StringComparison.OrdinalIgnoreCase);

	/// <summary>今晚值得一提的天象（天文台给的）。</summary>
	public static Func<string?>? SkyTonight { get; set; }

	public static WorldSnapshot World()
	{
		Farmer player = Game1.player;
		return new WorldSnapshot
		{
			Season = Game1.currentSeason ?? "spring",
			Day = Game1.dayOfMonth,
			Year = Game1.year,
			TimeOfDay = Game1.timeOfDay,
			Spouse = player.spouse,
			Children = Children(player),
			DivorceTonight = player.divorceTonight.Value,
			CommunityCenterDone = Safe(() => Game1.getLocationFromName("CommunityCenter") is CommunityCenter cc && cc.areAllAreasComplete()),
			JojaMember = player.mailReceived.Contains("JojaMember"),
			DeepestMineLevel = player.deepestMineLevel,
			BusRepaired = player.mailReceived.Contains("ccVault"),
			FarmingLevel = player.FarmingLevel,
			HouseLevel = player.HouseUpgradeLevel,
			PassedOut = player.mailReceived.Contains("passedOut"),
			ReturnScepter = player.mailReceived.Contains("ReturnScepter"),
			GalaxySword = player.mailReceived.Contains("galaxySword"),
			WeatherTomorrow = Game1.weatherForTomorrow,
			SkyTonight = Safe(() => SkyTonight?.Invoke()),
			EventsSeen = EventsSeen(player)
		};
	}

	public static HashSet<int> EventsSeen(Farmer player)
	{
		HashSet<int> ids = new HashSet<int>();
		foreach (string id in player.eventsSeen)
		{
			if (int.TryParse(id, out int n))
			{
				ids.Add(n);
			}
		}
		return ids;
	}

	public static NpcWorldState NpcState(NPC? npc, string name, int hearts)
	{
		(bool has, string? quest) = OpenQuest(name, Game1.player);
		return new NpcWorldState
		{
			Name = name,
			Hearts = hearts,
			HasOpenQuest = has,
			OpenQuestName = quest,
			Divorced = IsDivorced(name),
			Activity = npc == null ? null : Safe(() => new NpcActivitySnapshot(npc.isSleeping.Value, npc.doingEndOfRouteAnimation.Value, npc.endOfRouteBehaviorName.Value, npc.currentLocation?.Name, npc.isMoving()))
		};
	}

	/// <summary>天气说法：在屋里时说"在屋里，外面……"。</summary>
	public static string Weather(GameLocation? location) => LocationFacts.Weather(Game1.isLightning, Game1.isRaining, Game1.isSnowing, location != null && Safe(() => !location.IsOutdoors));

	/// <summary>同一地点（玩家所在）的其他 AI 村民。</summary>
	public static IReadOnlyList<string>? Others(string npcName, Func<string, bool> isEnabled)
	{
		GameLocation? here = Game1.currentLocation;
		if (here == null)
		{
			return null;
		}
		return here.characters.Where(c => c != null && !string.IsNullOrEmpty(c.Name) && !string.Equals(c.Name, npcName, StringComparison.OrdinalIgnoreCase) && c.IsVillager && isEnabled(c.Name)).Select(c => c.Name).ToList();
	}

	/// <summary>村民本来这一刻要说的游戏台词（去掉表情指令，最多 260 字）。</summary>
	public static string VanillaLine(NPC npc)
	{
		try
		{
			if (npc.CurrentDialogue.Count == 0)
			{
				return "";
			}
			return GameDialogueText.VanillaHint(npc.CurrentDialogue.Peek()?.getCurrentDialogue(), Game1.player?.Name ?? "");
		}
		catch
		{
			return "";
		}
	}

	private static List<ChildInfo> Children(Farmer player)
	{
		try
		{
			return player.getChildren().Select(c => new ChildInfo(c.Name ?? "", c.Age, c.Gender == Gender.Male)).ToList();
		}
		catch
		{
			return new List<ChildInfo>();
		}
	}

	private static (bool, string?) OpenQuest(string npcName, Farmer? player)
	{
		try
		{
			if (player?.questLog == null)
			{
				return (false, null);
			}
			foreach (Quest quest in player.questLog.ToArray())
			{
				if (quest == null || quest.completed.Value || quest.destroy.Value)
				{
					continue;
				}
				string? target = quest switch
				{
					ItemDeliveryQuest q => q.target.Value,
					LostItemQuest q => q.npcName.Value,
					SecretLostItemQuest q => q.npcName.Value,
					ResourceCollectionQuest q => q.target.Value,
					SlayMonsterQuest q => q.target.Value,
					FishingQuest q => q.target.Value,
					_ => null
				};
				if (string.Equals(target, npcName, StringComparison.OrdinalIgnoreCase))
				{
					return (true, quest.GetName());
				}
			}
		}
		catch
		{
		}
		return (false, null);
	}

	private static T Safe<T>(Func<T> get)
	{
		try
		{
			return get();
		}
		catch
		{
			return default!;
		}
	}
}
