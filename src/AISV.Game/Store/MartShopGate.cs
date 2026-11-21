using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;

namespace AISV.Game.Store;

/// <summary>对手停业整顿时，在营业时间外点他的柜台不开店（只是我们这边的经济，原版剧情不动）。</summary>
public static class MartShopGate
{
	private static MartService? _mart;

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, MartService mart, ILog log)
	{
		_mart = mart;
		_log = log;
		try
		{
			harmony.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction), new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }),
				prefix: new HarmonyMethod(typeof(MartShopGate), nameof(Prefix)));
		}
		catch (Exception ex)
		{
			_log.Warn("小超市：营业时间补丁没打上：" + ex.Message);
		}
	}

	private static bool Prefix(string[] action, ref bool __result)
	{
		try
		{
			if (_mart != null && action is { Length: > 1 } && action[0] == "OpenShop" && !_mart.AllowShop(action[1]))
			{
				__result = true;
				return false;
			}
		}
		catch (Exception ex)
		{
			_log.Warn("小超市：判断对手营业时间出错：" + ex.Message);
		}
		return true;
	}
}
