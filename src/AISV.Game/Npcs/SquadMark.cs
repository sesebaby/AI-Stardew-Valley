using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Npcs;

/// <summary>Squad 给跟着你（或原地等你）的村民写的标记；不靠它的 API 也能判断。走过来、陪钓、群聊、场景都避开这些人。</summary>
public static class SquadMark
{
	public const string RecruiterIdKey = "TheStardewSquad/RecruiterId";

	public static bool IsControlled(NPC? npc) => npc != null && npc.modData.ContainsKey(RecruiterIdKey);

	public static bool IsControlled(string? npcName)
	{
		if (string.IsNullOrWhiteSpace(npcName) || !Context.IsWorldReady)
		{
			return false;
		}
		try
		{
			NPC? here = Game1.currentLocation?.characters.FirstOrDefault(c => string.Equals(c.Name, npcName, System.StringComparison.OrdinalIgnoreCase));
			return IsControlled(here ?? Game1.getCharacterFromName(npcName));
		}
		catch
		{
			return false;
		}
	}
}
