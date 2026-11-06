using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;

namespace AISV.Game.Dialogue;

/// <summary>
/// NPC.checkAction 的前缀：玩家点村民时先问对话流程要不要接管。只打这一个补丁（不碰每帧都会调用的方法）。
/// </summary>
public static class TalkPatch
{
	/// <summary>返回 null = 交给游戏原来的 checkAction；否则是 checkAction 的返回值。</summary>
	public static Func<NPC, Farmer?, GameLocation, bool?>? Handler { get; set; }

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, ILog log)
	{
		_log = log;
		harmony.Patch(AccessTools.Method(typeof(NPC), nameof(NPC.checkAction)), prefix: new HarmonyMethod(typeof(TalkPatch), nameof(Prefix)));
	}

	private static bool Prefix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
	{
		try
		{
			bool? handled = Handler?.Invoke(__instance, who, l);
			if (handled == null)
			{
				return true;
			}
			__result = handled.Value;
			return false;
		}
		catch (Exception ex)
		{
			_log.Error("点村民的补丁出错，交给游戏处理：" + ex.Message);
			return true;
		}
	}
}
