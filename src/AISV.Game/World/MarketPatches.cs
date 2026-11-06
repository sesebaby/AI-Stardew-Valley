using System;
using System.Collections.Generic;
using AISV.Core.Diagnostics;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;

namespace AISV.Game.World;

/// <summary>
/// 行情只在出货结算里生效。夜里结算和出货清单都走 Object.sellToStorePrice，所以给它加后缀，但只在两段时间里改价：
/// 入夜到第二天开始之间（出货箱结算），以及 ShippingMenu.parseItems 执行期间（清单上的单价、合计和进账一致）。白天卖给商店、物品说明都不变。
/// </summary>
public static class MarketPatches
{
	public static bool SettlementActive { get; set; }

	public static Func<StardewValley.Object, float?>? Factor { get; set; }

	/// <summary>出货清单打开时：东西、原价、行情价。</summary>
	public static Action<IList<Item>, Func<Item, int>, Func<Item, int>>? Shipped { get; set; }

	private static int _menuDepth;

	private static bool _suppress;

	private static ILog _log = NullLog.Instance;

	public static bool InSettlement => SettlementActive || _menuDepth > 0;

	public static void Apply(Harmony harmony, ILog log)
	{
		_log = log;
		try
		{
			harmony.Patch(AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.sellToStorePrice)), postfix: new HarmonyMethod(typeof(MarketPatches), nameof(PricePostfix)));
			harmony.Patch(AccessTools.Method(typeof(ShippingMenu), nameof(ShippingMenu.parseItems)), prefix: new HarmonyMethod(typeof(MarketPatches), nameof(ParsePrefix)), finalizer: new HarmonyMethod(typeof(MarketPatches), nameof(ParseFinalizer)));
		}
		catch (Exception ex)
		{
			log.Error("市场补丁没装上，出货按原价：" + ex.Message);
		}
	}

	/// <summary>不经过行情的价格。</summary>
	public static int VanillaPrice(Item item)
	{
		bool was = _suppress;
		_suppress = true;
		try
		{
			return item.sellToStorePrice(-1L);
		}
		finally
		{
			_suppress = was;
		}
	}

	/// <summary>按行情的结算价（白天也能算，给看板和调试用）。</summary>
	public static int MarketPrice(Item item)
	{
		_menuDepth++;
		try
		{
			return item.sellToStorePrice(-1L);
		}
		finally
		{
			_menuDepth--;
		}
	}

	private static void PricePostfix(StardewValley.Object __instance, ref int __result)
	{
		if (_suppress || __result <= 0 || !InSettlement || Factor == null)
		{
			return;
		}
		try
		{
			float? factor = Factor(__instance);
			if (factor.HasValue && Math.Abs(factor.Value - 1f) > 0.0005f)
			{
				__result = Math.Max(1, (int)Math.Round(__result * factor.Value));
			}
		}
		catch
		{
		}
	}

	private static void ParsePrefix(IList<Item> items)
	{
		_menuDepth++;
		try
		{
			Shipped?.Invoke(items, VanillaPrice, MarketPrice);
		}
		catch (Exception ex)
		{
			_log.Warn("记出货出错：" + ex.Message);
		}
	}

	private static Exception? ParseFinalizer(Exception? __exception)
	{
		_menuDepth = Math.Max(0, _menuDepth - 1);
		return __exception;
	}
}
