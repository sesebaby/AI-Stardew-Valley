using System;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.World;

/// <summary>拿着委托（或伴侣拜托）要的东西对他按动作键：东西够就交付，不够提示还差几个；其他照常（送礼）。</summary>
public static class CommissionPatches
{
	private static CommissionService? _service;

	private static ILog? _log;

	/// <summary>这次按键是委托交付（物品请求别再算一次）。</summary>
	public static bool Delivered { get; private set; }

	/// <summary>伴侣剧情的交付：村民、农夫、只是探测 → 交了返回 true。</summary>
	public static Func<NPC, Farmer, bool, bool>? PartnerHandIn { get; set; }

	public static void Apply(Harmony harmony, CommissionService service, ILog log)
	{
		_service = service;
		_log = log;
		try
		{
			harmony.Patch(AccessTools.Method(typeof(NPC), nameof(NPC.tryToReceiveActiveObject), new[] { typeof(Farmer), typeof(bool) }), prefix: new HarmonyMethod(typeof(CommissionPatches), nameof(Prefix)));
		}
		catch (Exception ex)
		{
			log.Warn("委托交付的入口没接上，只能等以后的版本：" + ex.Message);
		}
	}

	private static bool Prefix(NPC __instance, Farmer who, bool probe, ref bool __result)
	{
		Delivered = false;
		try
		{
			if (who != null && Context.IsMainPlayer && PartnerHandIn?.Invoke(__instance, who, probe) == true)
			{
				__result = true;
				Delivered = !probe;
				return false;
			}
			if (_service == null || !Context.IsMainPlayer || _service.WaitingFor(__instance, who?.ActiveObject) is not Commission c)
			{
				return true;
			}
			int have = CommissionService.CountInInventory(who!, c.ItemId);
			if (have < c.Count)
			{
				if (!probe)
				{
					Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(Tr.Get("commission.deliverMissing", "手上才 {{have}} 个，要 {{need}} 个才够", ("have", have), ("need", c.Count - have))), HUDMessage.error_type));
				}
				return true;
			}
			__result = true;
			if (!probe)
			{
				Delivered = _service.Deliver(c, who!, __instance);
			}
			return false;
		}
		catch (Exception ex)
		{
			_log?.Warn("委托交付出错：" + ex.Message);
			return true;
		}
	}
}
