using System;
using StardewValley;

namespace AISV.Game.Npcs;

/// <summary>村民头顶说一句话：界面层接上自己的气泡，没接上时用游戏原来的。</summary>
public static class NpcSpeech
{
	public static Action<NPC, string, int>? Show { get; set; }

	/// <summary>心里话（宠物的内心独白）：界面层画成灰色的思考泡；没接上时用游戏原来的头顶字加括号。</summary>
	public static Action<NPC, string, int>? ShowThought { get; set; }

	public static void Think(NPC npc, string text, int durationMs = -1)
	{
		if (npc == null || string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (ShowThought != null)
		{
			ShowThought(npc, text, durationMs);
			return;
		}
		npc.showTextAboveHead("（" + text.Trim() + "）", duration: durationMs > 0 ? durationMs : 3500);
	}

	/// <param name="durationMs">显示多久；不填按字数算。</param>
	public static void Say(NPC npc, string text, int durationMs = -1)
	{
		if (npc == null || string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (Show != null)
		{
			Show(npc, text, durationMs);
			return;
		}
		npc.showTextAboveHead(text, duration: durationMs > 0 ? durationMs : 3000);
	}
}
