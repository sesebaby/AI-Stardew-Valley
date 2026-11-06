using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>NPC.receiveGift 的后缀：村民收下礼物后记下（礼物记录、负面记忆、生日感谢信）。只在送礼时调用，不是每帧。</summary>
public static class GiftPatch
{
	public static Action<NPC, StardewValley.Object>? Handler { get; set; }

	/// <summary>这次送礼实际加的好感点数（收礼前后的差），处理器里读。</summary>
	public static int LastGain { get; private set; }

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, ILog log)
	{
		_log = log;
		harmony.Patch(AccessTools.Method(typeof(NPC), nameof(NPC.receiveGift)), prefix: new HarmonyMethod(typeof(GiftPatch), nameof(Prefix)), postfix: new HarmonyMethod(typeof(GiftPatch), nameof(Postfix)));
	}

	private static void Prefix(NPC __instance, Farmer __1, out int __state)
	{
		__state = __instance?.Name != null && __1 != null && __1.friendshipData.ContainsKey(__instance.Name) ? Points(__instance, __1) : -1;
	}

	private static void Postfix(NPC __instance, StardewValley.Object o, Farmer __1, int __state)
	{
		try
		{
			if (__instance != null && o != null)
			{
				LastGain = __state < 0 ? 0 : Points(__instance, __1) - __state;
				Handler?.Invoke(__instance, o);
			}
		}
		catch (Exception ex)
		{
			_log.Warn("记礼物出错：" + ex.Message);
		}
	}

	private static int Points(NPC? npc, Farmer? giver) => npc?.Name != null && giver != null && giver.friendshipData.TryGetValue(npc.Name, out Friendship? f) ? f.Points : 0;
}
