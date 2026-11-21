using System;
using System.Collections;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Npcs;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Compat;

/// <summary>
/// 和 Farmer's Store（Commercialization of Stardew Valley，bubuge）对接，两家店同时开：
/// 它的顾客和店员是替身（名字以 COS_NPC_ / COS_EMP_ 开头），真人在原地藏起来——藏起来的真人我们不去打扰；
/// 反过来，我们正占着的村民它也别拉去当顾客；它那边结完账的东西记进村民的账本（StoreSale.Source = cos）。
/// 它没有公开接口，这里按名字找方法，找不到就只写一句日志，什么也不做。
/// </summary>
public static class FarmersStoreWatcher
{
	public const string ModId = "bubuge.CommercializationOfStardewValley";

	private static Func<ModConfig> _config = () => new ModConfig();

	private static ILog _log = NullLog.Instance;

	private static StoreSaleBook? _sales;

	public static bool Loaded { get; private set; }

	public static void Attach(IModHelper helper, Harmony harmony, Func<ModConfig> config, StoreSaleBook sales, ILog log)
	{
		_config = config;
		_log = log;
		_sales = sales;
		Loaded = helper.ModRegistry.IsLoaded(ModId);
		if (!Loaded)
		{
			return;
		}
		NpcBusy.Register("cos", n => _config().FarmersStoreLink && Game1.getCharacterFromName(n) is NPC npc && npc.IsInvisible && !NpcBusy.IsHeld(n));
		Patch(harmony, "CommercializationOfStardewValley.Services.CustomerService:IsEligibleRealNpcCustomer", postfix: nameof(Eligible_Postfix));
		Patch(harmony, "CommercializationOfStardewValley.Services.ContractService:RecordCheckoutSettled", postfix: nameof(Checkout_Postfix));
		WarnKeys(helper);
	}

	private static void Patch(Harmony harmony, string method, string postfix)
	{
		try
		{
			if (AccessTools.Method(method) is System.Reflection.MethodInfo m)
			{
				harmony.Patch(m, postfix: new HarmonyMethod(typeof(FarmersStoreWatcher), postfix));
			}
			else
			{
				_log.Info($"Farmer's Store 里没找到 {method}（可能更新了），这部分对接先不做。");
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"和 Farmer's Store 对接 {method} 时出错：{ex.Message}");
		}
	}

	/// <summary>我们正占着的村民（顾客、店员、配偶日程、离家出走……）它别拉去当顾客。</summary>
	private static void Eligible_Postfix(NPC npc, ref string reason, ref bool __result)
	{
		if (__result && _config().FarmersStoreLink && npc != null && NpcBusy.IsHeld(npc.Name))
		{
			__result = false;
			reason = "aisv-busy";
		}
	}

	/// <summary>它那边一位顾客结完账：每样东西记一笔（按结账事件 id 去重，读档后不会重复）。</summary>
	private static void Checkout_Postfix(object customer, IEnumerable items, string eventId)
	{
		try
		{
			if (!_config().FarmersStoreLink || !Context.IsMainPlayer || _sales == null || customer == null || items == null)
			{
				return;
			}
			string buyer = Read<string>(customer, "RealNpcName") ?? "";
			if (buyer.Length == 0)
			{
				return;
			}
			int n = 0;
			foreach (object line in items)
			{
				string id = Read<string>(line, "ItemId") ?? "";
				int qty = Math.Max(1, Read<int>(line, "Quantity"));
				int price = Math.Max(0, Read<int>(line, "UnitPrice"));
				if (id.Length == 0)
				{
					continue;
				}
				Item? item = ItemRegistry.Create(id, allowNull: true);
				_sales.Record(new StoreSale
				{
					Source = StoreSale.Cos,
					TxId = eventId + "#" + n++,
					Buyer = buyer,
					ItemId = item?.QualifiedItemId ?? id,
					ItemName = item?.DisplayName ?? id,
					Qty = qty,
					UnitPrice = price,
					MarketPrice = item is StardewValley.Object o ? AISV.Game.Store.MartService.BasePrice(o) : price,
					Day = Game1.Date.TotalDays,
					Time = Game1.timeOfDay
				});
			}
		}
		catch (Exception ex)
		{
			_log.Warn("记 Farmer's Store 的成交时出错：" + ex.Message);
		}
	}

	private static T? Read<T>(object target, string name)
	{
		object? v = AccessTools.Property(target.GetType(), name)?.GetValue(target) ?? AccessTools.Field(target.GetType(), name)?.GetValue(target);
		return v is T t ? t : default;
	}

	/// <summary>它默认用 F5 / F6 / F7，和我们的按键撞了就提醒一下。</summary>
	private static void WarnKeys(IModHelper helper)
	{
		ModConfig c = _config();
		string[] theirs = { "F5", "F6", "F7" };
		foreach ((string mine, string what) in new[] { (c.KeyRumors, Tr.Get("shell.settings.gossipKey", "直接看八卦")), (c.KeyAboutMe, Tr.Get("shell.settings.profileKey", "我的设定")), (c.KeyShell, Tr.Get("shell.settings.shellKey", "打开这个界面")) })
		{
			if (Array.Exists(theirs, k => string.Equals(k, mine?.Trim(), StringComparison.OrdinalIgnoreCase)))
			{
				_log.Warn(Tr.Get("cos.keyClash", "Farmer's Store 也用 {{key}} 键（我们的“{{what}}”）。可以在 GMCM 或 config.json 里改其中一边。", ("key", mine), ("what", what)));
			}
		}
	}
}
