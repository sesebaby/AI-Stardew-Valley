using System;
using System.Collections.Generic;
using AISV.Core.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Locations;

namespace AISV.Game.Stories;

/// <summary>商店脱离：店主离开小镇或嫁到农场后，店照样能开；罗宾不在时木匠铺柜台也照常给菜单。</summary>
public static class ShopPatches
{
	private static Func<string, bool> _detached = _ => false;

	private static ILog _log = NullLog.Instance;

	public static void Apply(Harmony harmony, Func<string, bool> isDetached, ILog log)
	{
		_detached = isDetached;
		_log = log;
		var open = AccessTools.Method(typeof(Utility), nameof(Utility.TryOpenShopMenu), new[] { typeof(string), typeof(GameLocation), typeof(Rectangle?), typeof(int?), typeof(bool), typeof(bool), typeof(Action<string>) });
		if (open != null)
		{
			harmony.Patch(open, prefix: new HarmonyMethod(typeof(ShopPatches), nameof(OpenPrefix)));
		}
		else
		{
			log.Warn("找不到开店的方法，店主不在时商店照常关门。");
		}
		var carpenters = AccessTools.Method(typeof(GameLocation), "carpenters");
		if (carpenters != null)
		{
			harmony.Patch(carpenters, prefix: new HarmonyMethod(typeof(ShopPatches), nameof(CarpentersPrefix)));
		}
	}

	private static void OpenPrefix(string shopId, ref bool forceOpen)
	{
		if (forceOpen || string.IsNullOrWhiteSpace(shopId))
		{
			return;
		}
		try
		{
			if (!DataLoader.Shops(Game1.content).TryGetValue(shopId, out ShopData? shop) || shop?.Owners == null)
			{
				return;
			}
			foreach (ShopOwnerData owner in shop.Owners)
			{
				if (owner != null && !string.IsNullOrWhiteSpace(owner.Name) && _detached(owner.Name))
				{
					forceOpen = true;
					return;
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"读商店 {shopId} 出错：{ex.Message}");
		}
	}

	private static bool CarpentersPrefix(GameLocation __instance, ref bool __result)
	{
		if (!_detached("Robin"))
		{
			return true;
		}
		try
		{
			Farmer player = Game1.player;
			int level = player.HouseUpgradeLevel;
			if (player.daysUntilHouseUpgrade.Value > 0 || Game1.IsThereABuildingUnderConstruction())
			{
				__result = Utility.TryOpenShopMenu("Carpenter", "Robin");
				return false;
			}
			List<Response> options = new List<Response> { new Response("Shop", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Shop")) };
			if (Game1.IsMasterGame)
			{
				if (level < 3)
				{
					options.Add(new Response("Upgrade", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_UpgradeHouse")));
				}
				else
				{
					Farmer master = Game1.MasterPlayer;
					if (master.mailReceived.Contains("ccIsComplete") || master.mailReceived.Contains("JojaMember") || master.hasCompletedCommunityCenter())
					{
						Town town = Game1.RequireLocation<Town>("Town");
						if (town.daysUntilCommunityUpgrade.Value <= 0 && (!master.mailReceived.Contains("pamHouseUpgrade") || !master.mailReceived.Contains("communityUpgradeShortcuts")))
						{
							options.Add(new Response("CommunityUpgrade", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_CommunityUpgrade")));
						}
					}
				}
			}
			else if (level < 3)
			{
				options.Add(new Response("Upgrade", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_UpgradeCabin")));
			}
			if (level >= 2)
			{
				options.Add(new Response("Renovate", Game1.content.LoadString(Game1.IsMasterGame ? "Strings\\Locations:ScienceHouse_CarpenterMenu_RenovateHouse" : "Strings\\Locations:ScienceHouse_CarpenterMenu_RenovateCabin")));
			}
			options.Add(new Response("Construct", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Construct")));
			options.Add(new Response("Leave", Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu_Leave")));
			__instance.createQuestionDialogue(Game1.content.LoadString("Strings\\Locations:ScienceHouse_CarpenterMenu"), options.ToArray(), "carpenter");
			__result = true;
			return false;
		}
		catch (Exception ex)
		{
			_log.Warn("木匠铺柜台出错：" + ex.Message);
			return true;
		}
	}
}
