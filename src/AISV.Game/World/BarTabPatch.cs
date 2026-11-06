using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;

namespace AISV.Game.World;

/// <summary>在格斯的店里钱不够时记账：商店算钱时把还能赊的算进去，扣钱时不够的部分记到账上。</summary>
public static class BarTabPatch
{
	/// <summary>这家店现在还能赊多少（不能赊时为 0）。</summary>
	public static Func<string, int>? Room { get; set; }

	/// <summary>记账：店、金额。</summary>
	public static Action<string, int>? Charge { get; set; }

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, ILog log)
	{
		_log = log;
		harmony.Patch(AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.getPlayerCurrencyAmount)), postfix: new HarmonyMethod(typeof(BarTabPatch), nameof(Amount)));
		harmony.Patch(AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.chargePlayer)), prefix: new HarmonyMethod(typeof(BarTabPatch), nameof(Pay)));
	}

	private static string? Shop(Farmer who, int currency) => currency == 0 && who.IsLocalPlayer && Game1.activeClickableMenu is ShopMenu m ? m.ShopId : null;

	private static void Amount(Farmer __0, int __1, ref int __result)
	{
		try
		{
			if (Shop(__0, __1) is string shop && Room?.Invoke(shop) is int room and > 0)
			{
				__result += room;
			}
		}
		catch (Exception ex)
		{
			_log.Warn("酒账：算钱出错：" + ex.Message);
		}
	}

	private static bool Pay(Farmer __0, int __1, int __2)
	{
		Farmer who = __0;
		int amount = __2;
		try
		{
			if (Shop(who, __1) is not string shop || amount <= who.Money || Room?.Invoke(shop) is not int room || amount - who.Money > room)
			{
				return true;
			}
			int owed = amount - who.Money;
			Charge?.Invoke(shop, owed);
			who.Money = 0;
			return false;
		}
		catch (Exception ex)
		{
			_log.Warn("酒账：记账出错：" + ex.Message);
			return true;
		}
	}
}
