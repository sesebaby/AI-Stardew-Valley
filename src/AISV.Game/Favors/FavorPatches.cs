using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;
using StardewValley.Quests;

namespace AISV.Game.Favors;

/// <summary>物品请求的两个交付口：任务日志里交付、直接把东西递给村民。都只在交东西时调用。</summary>
public static class FavorPatches
{
	private static FavorService? _service;

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, FavorService service, ILog log)
	{
		_service = service;
		_log = log;
		var offered = AccessTools.Method(typeof(ItemDeliveryQuest), nameof(ItemDeliveryQuest.OnItemOfferedToNpc), new[] { typeof(NPC), typeof(Item), typeof(bool) });
		if (offered != null)
		{
			harmony.Patch(offered, postfix: new HarmonyMethod(typeof(FavorPatches), nameof(OfferedPostfix)));
		}
		else
		{
			log.Warn("找不到任务交付的入口，物品请求只能靠送礼完成。");
		}
		var receive = AccessTools.Method(typeof(NPC), nameof(NPC.tryToReceiveActiveObject), new[] { typeof(Farmer), typeof(bool) });
		if (receive != null)
		{
			harmony.Patch(receive, prefix: new HarmonyMethod(typeof(FavorPatches), nameof(ReceivePrefix)), postfix: new HarmonyMethod(typeof(FavorPatches), nameof(ReceivePostfix)));
		}
	}

	private static void OfferedPostfix(ItemDeliveryQuest __instance, bool probe, bool __result)
	{
		if (!probe && __result && __instance != null)
		{
			Run(() => _service?.OnQuestDelivered(__instance));
		}
	}

	private static void ReceivePrefix(Farmer who, bool probe, out Item? __state)
	{
		__state = !probe ? who?.ActiveObject?.getOne() : null;
	}

	private static void ReceivePostfix(NPC __instance, bool probe, bool __result, Item? __state)
	{
		if (!probe && __result && __state != null && __instance != null && !AISV.Game.World.CommissionPatches.Delivered)
		{
			Run(() => _service?.OnItemOffered(__instance, __state));
		}
	}

	private static void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			_log.Warn("物品请求交付出错：" + ex.Message);
		}
	}
}
