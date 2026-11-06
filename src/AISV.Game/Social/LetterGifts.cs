using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Social.Letters;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>
/// 信里附的东西：不在黑名单、卖价不超过上限（最爱的东西上限高一些）。
/// 生日信：先看这位村民常送的那件，否则从可选物品里挑村民最爱的（没有就挑喜欢的），不和上一封附的重复。
/// </summary>
public static class LetterGifts
{
	private static int _poolDay = -1;

	private static readonly List<StardewValley.Object> Pool = new List<StardewValley.Object>();

	private static readonly Dictionary<(string Npc, string Item), int> Tastes = new Dictionary<(string, string), int>();

	public static bool Allowed(int itemId, bool loved, ModConfig c) => Allowed(new StardewValley.Object(itemId.ToString(), 1), loved, c);

	public static bool Allowed(StardewValley.Object item, bool loved, ModConfig c)
	{
		if (c.MailGiftBanned.Contains(item.QualifiedItemId, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		return item.sellToStorePrice(-1L) <= (loved ? c.MailLovedGiftPriceCap : c.MailGiftPriceCap);
	}

	public static int Pick(NPC npc, IEnumerable<QueuedLetter> letters, ModConfig c)
	{
		int usual = LetterBook.UsualGift(npc.Name, id => Allowed(id, false, c));
		if (usual > 0)
		{
			return usual;
		}
		BuildPool(c);
		if (Pool.Count == 0)
		{
			return -1;
		}
		int last = letters.Where(l => string.Equals(l.From, npc.Name, StringComparison.OrdinalIgnoreCase) && l.EnclosedId > 0).OrderByDescending(l => l.TotalDay).FirstOrDefault()?.EnclosedId ?? -1;
		List<StardewValley.Object> loved = new List<StardewValley.Object>(), liked = new List<StardewValley.Object>();
		foreach (StardewValley.Object item in Pool)
		{
			if (int.TryParse(item.ItemId, out int id) && id == last)
			{
				continue;
			}
			if (!Tastes.TryGetValue((npc.Name, item.ItemId), out int taste))
			{
				try
				{
					taste = npc.getGiftTasteForThisItem(item);
				}
				catch (Exception)
				{
					taste = NPC.gift_taste_neutral;
				}
				Tastes[(npc.Name, item.ItemId)] = taste;
			}
			if (taste == NPC.gift_taste_love && Allowed(item, true, c))
			{
				loved.Add(item);
			}
			else if (taste == NPC.gift_taste_like && Allowed(item, false, c))
			{
				liked.Add(item);
			}
		}
		List<StardewValley.Object> from = loved.Count > 0 ? loved : liked;
		return from.Count > 0 && int.TryParse(from[Game1.random.Next(from.Count)].ItemId, out int pick) ? pick : -1;
	}

	public static string Name(int itemId)
	{
		try
		{
			return ItemRegistry.GetDataOrErrorItem("(O)" + itemId).DisplayName ?? "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	/// <summary>当天第一次用时建一次：有价钱的普通物品里，不在黑名单、按"最爱"的价格上限没超的。</summary>
	private static void BuildPool(ModConfig c)
	{
		int today = (int)Game1.stats.DaysPlayed;
		if (_poolDay == today)
		{
			return;
		}
		Pool.Clear();
		if (Game1.objectData != null)
		{
			foreach ((string id, StardewValley.GameData.Objects.ObjectData data) in Game1.objectData)
			{
				if (data.Category == -999 || data.Price <= 0)
				{
					continue;
				}
				StardewValley.Object item = new StardewValley.Object(id, 1);
				if (Allowed(item, true, c))
				{
					Pool.Add(item);
				}
			}
		}
		_poolDay = today;
	}
}
