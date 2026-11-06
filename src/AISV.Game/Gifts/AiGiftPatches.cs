using System;
using AISV.Core.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace AISV.Game.Gifts;

/// <summary>礼物物品显示自己的描述、不和别的东西叠放；信里的礼物像游戏自带附件一样放在信底下。</summary>
public static class AiGiftPatches
{
	private static AiGiftService? _service;

	private static ILog? _log;

	public static void Apply(Harmony harmony, AiGiftService service, ILog log)
	{
		_service = service;
		_log = log;
		try
		{
			harmony.Patch(AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.getDescription), Type.EmptyTypes), postfix: new HarmonyMethod(typeof(AiGiftPatches), nameof(Description)));
			harmony.Patch(AccessTools.Method(typeof(Item), nameof(Item.canStackWith), new[] { typeof(ISalable) }), postfix: new HarmonyMethod(typeof(AiGiftPatches), nameof(CanStack)));
			harmony.Patch(AccessTools.Constructor(typeof(LetterViewerMenu), new[] { typeof(string), typeof(string), typeof(bool) }), postfix: new HarmonyMethod(typeof(AiGiftPatches), nameof(Letter)));
		}
		catch (Exception ex)
		{
			log.Error("AI 礼物的补丁没打上：" + ex.Message);
		}
	}

	private static void Description(StardewValley.Object __instance, ref string __result)
	{
		try
		{
			if (AiGiftService.DescriptionOf(__instance) is string text)
			{
				__result = Game1.parseText(text, Game1.smallFont, Traverse.Create(__instance).Method("getDescriptionWidth").GetValue<int>());
			}
		}
		catch
		{
		}
	}

	private static void CanStack(Item __instance, ISalable other, ref bool __result)
	{
		if (!__result || other is not Item item)
		{
			return;
		}
		string? a = AiGiftService.GiftIdOf(__instance), b = AiGiftService.GiftIdOf(item);
		if ((a != null || b != null) && !string.Equals(a, b, StringComparison.Ordinal))
		{
			__result = false;
		}
	}

	private static void Letter(LetterViewerMenu __instance, string mailTitle, bool fromCollection)
	{
		if (fromCollection || string.IsNullOrWhiteSpace(mailTitle) || _service == null)
		{
			return;
		}
		try
		{
			if (_service.TakeLetterGift(mailTitle) is not Item gift)
			{
				return;
			}
			__instance.itemsToGrab.Add(new ClickableComponent(new Rectangle(__instance.xPositionOnScreen + __instance.width / 2 - 48, __instance.yPositionOnScreen + __instance.height - 32 - 96, 96, 96), gift)
			{
				myID = 104,
				leftNeighborID = 101,
				rightNeighborID = 102
			});
			__instance.backButton.rightNeighborID = 104;
			__instance.forwardButton.leftNeighborID = 104;
		}
		catch (Exception ex)
		{
			_log?.Warn($"礼物没放进信 {mailTitle}：{ex.Message}");
		}
	}
}
