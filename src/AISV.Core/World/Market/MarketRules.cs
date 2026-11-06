using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.World.Market;

/// <summary>
/// 行情的算法：每天早上销量衰减、加上昨天卖出的，按反季、热门、供应压力和小波动定目标系数，再限住每天的涨跌和总范围。
/// 随机都用跟存档绑定的哈希，同一存档同一天结果一样。
/// </summary>
public static class MarketRules
{
	public const float MinFactor = 0.8f;
	public const float MaxFactor = 1.4f;
	public const float DailyCap = 0.10f;
	public const float Decay = 0.86f;
	public const float OutOfSeasonBonus = 0.10f;
	public const float HotBonus = 0.12f;
	public const float SupplyPerItem = 0.0005f;
	public const float MaxSupplyDrop = 0.20f;
	public const float Noise = 0.03f;
	public const int HistoryDays = 7;
	public const int HotPerWeek = 2;
	public const int MinBasePrice = 5;

	/// <summary>镇上种不出来的：照样有行情，但不当热门、村民不卖（仙人掌果、上古水果、宝石甜莓、菠萝、芋头等）。</summary>
	public static readonly IReadOnlySet<string> NotTownGrown = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "(O)90", "(O)454", "(O)417", "(O)832", "(O)830", "(O)889" };

	/// <summary>会在镇上卖自家菜的村民。</summary>
	public static readonly IReadOnlyList<string> Sellers = new[] { "Lewis", "Evelyn", "Caroline", "Leah", "Linus" };

	/// <summary>
	/// 算一天的价。<paramref name="sellerPresent"/> 判断卖菜的村民今天在不在、开没开 AI。返回村民卖出的件数（按作物）。
	/// </summary>
	public static Dictionary<string, int> PriceDay(MarketState s, IReadOnlyList<MarketCrop> crops, string season, int today, ulong gameId, Func<string, bool> sellerPresent)
	{
		bool first = s.LastPricedDay < 0;
		int elapsed = first ? 0 : Math.Clamp(today - s.LastPricedDay, 1, 28);
		foreach (MarketCrop crop in crops)
		{
			if (!s.Items.ContainsKey(crop.ItemId))
			{
				s.Items[crop.ItemId] = new MarketEntry { ItemId = crop.ItemId };
			}
		}
		int week = today / 7;
		if (s.HotWeek != week || s.HotItems.Count == 0)
		{
			s.HotItems = crops.Where(c => c.Seasons.Contains(season) && !NotTownGrown.Contains(c.ItemId)).OrderBy(c => Hash(c.ItemId, week, 11, gameId)).Take(HotPerWeek).Select(c => c.ItemId).ToList();
			s.HotWeek = week;
		}
		Dictionary<string, int> npc = first ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : NpcSales(s, crops, season, today, gameId, sellerPresent);
		foreach (MarketEntry e in s.Items.Values)
		{
			e.Volume = (float)(e.Volume * Math.Pow(Decay, elapsed));
			e.PlayerSold = s.PendingPlayerSales.TryGetValue(e.ItemId, out int p) ? p : 0;
			e.NpcSold = npc.TryGetValue(e.ItemId, out int n) ? n : 0;
			e.Volume += e.PlayerSold + e.NpcSold;
		}
		s.PendingPlayerSales.Clear();
		Dictionary<string, MarketCrop> byId = crops.ToDictionary(c => c.ItemId, StringComparer.OrdinalIgnoreCase);
		foreach (MarketEntry e in s.Items.Values)
		{
			if (!byId.TryGetValue(e.ItemId, out MarketCrop? crop))
			{
				continue;
			}
			float target = Target(e, crop, season, today, s.HotItems, gameId);
			e.Yesterday = first ? 1f : e.Factor;
			(float low, float high) = Bounds(e.Yesterday);
			e.Factor = Math.Clamp((float)Math.Round(target, 3), low, Math.Max(low, high));
			e.History.Add(e.Factor);
			while (e.History.Count > HistoryDays)
			{
				e.History.RemoveAt(0);
			}
		}
		s.LastPricedDay = today;
		return npc;
	}

	/// <summary>今天允许的范围：昨天上下 10%，并在 0.8～1.4 之内（三位小数，向内取整）。</summary>
	public static (float Low, float High) Bounds(float yesterday)
	{
		float low = (float)(Math.Ceiling(Math.Max(MinFactor, yesterday * (1f - DailyCap)) * 1000.0 - 1e-4) / 1000.0);
		float high = (float)(Math.Floor(Math.Min(MaxFactor, yesterday * (1f + DailyCap)) * 1000.0 + 1e-4) / 1000.0);
		return (low, high);
	}

	public static float Target(MarketEntry e, MarketCrop crop, string season, int today, IReadOnlyCollection<string> hot, ulong gameId)
	{
		float demand = 1f;
		if (crop.Seasons.Count > 0 && !crop.Seasons.Contains(season))
		{
			demand += OutOfSeasonBonus;
		}
		if (hot.Contains(e.ItemId, StringComparer.OrdinalIgnoreCase))
		{
			demand += HotBonus;
		}
		float supply = 1f - Math.Min(MaxSupplyDrop, e.Volume * SupplyPerItem);
		float noise = (Hash(e.ItemId, today, 7, gameId) % 2001 / 1000f - 1f) * Noise;
		return demand * supply * (1f + noise);
	}

	/// <summary>卖菜的村民每人每天卖 1～2 样当季、镇上种的作物（便宜的更常见，按 1/√价格），每样 5～20 件。</summary>
	public static Dictionary<string, int> NpcSales(MarketState s, IReadOnlyList<MarketCrop> crops, string season, int today, ulong gameId, Func<string, bool> sellerPresent)
	{
		Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		s.LastNpcSales = new List<MarketSale>();
		List<MarketCrop> pool = crops.Where(c => c.Seasons.Contains(season) && c.BasePrice > 0 && !NotTownGrown.Contains(c.ItemId)).ToList();
		if (pool.Count == 0)
		{
			return totals;
		}
		foreach (string seller in Sellers)
		{
			if (!sellerPresent(seller))
			{
				continue;
			}
			Random rng = new Random(Hash(seller, today, 3, gameId));
			int picks = rng.Next(1, 3);
			for (int i = 0; i < picks; i++)
			{
				double roll = rng.NextDouble() * pool.Sum(c => 1.0 / Math.Sqrt(c.BasePrice));
				MarketCrop pick = pool[^1];
				foreach (MarketCrop c in pool)
				{
					roll -= 1.0 / Math.Sqrt(c.BasePrice);
					if (roll <= 0)
					{
						pick = c;
						break;
					}
				}
				int count = rng.Next(5, 21);
				totals[pick.ItemId] = totals.GetValueOrDefault(pick.ItemId) + count;
				s.LastNpcSales.Add(new MarketSale { Seller = seller, ItemId = pick.ItemId, Count = count });
			}
		}
		return totals;
	}

	/// <summary>记一晚的出货（一晚只记一次）：卖了哪些作物（加工品不算销量）、按行情比原价多拿多少。这一晚记过时返回 false。</summary>
	public static bool RecordShipment(MarketState s, int day, IEnumerable<ShippedItem> items)
	{
		if (s.PendingDay == day && s.LastNightDay == day)
		{
			return false;
		}
		s.PendingDay = day;
		s.PendingPlayerSales.Clear();
		int difference = 0;
		foreach (ShippedItem item in items)
		{
			difference += (item.MarketPrice - item.BasePrice) * item.Stack;
			if (!item.Artisan)
			{
				s.PendingPlayerSales[item.Key] = s.PendingPlayerSales.GetValueOrDefault(item.Key) + item.Stack;
			}
		}
		s.LastNightDifference = difference;
		s.LastNightDay = day;
		return true;
	}

	/// <summary>昨晚出货按行情多拿（负数是少拿）的金币；day 和记账时用同一种天数，不是昨晚出的货返回 0。</summary>
	public static int LastNightGold(MarketState s, int day) => s.LastNightDay == day ? s.LastNightDifference : 0;

	/// <summary>一行看板。</summary>
	public static MarketRow Row(MarketCrop crop, MarketEntry e, string season, IReadOnlyCollection<string> hot, bool held)
	{
		float change = e.Yesterday > 0f ? (e.Factor - e.Yesterday) / e.Yesterday * 100f : 0f;
		return new MarketRow(crop.ItemId, crop.DisplayName, crop.BasePrice, Math.Max(1, (int)Math.Round(crop.BasePrice * e.Factor)), e.Factor, (float)Math.Round(change, 1), e.History.ToList(),
			hot.Contains(crop.ItemId, StringComparer.OrdinalIgnoreCase), crop.Seasons.Count > 0 && !crop.Seasons.Contains(season), e.Volume * SupplyPerItem >= 0.05f, e.PlayerSold, e.NpcSold, held);
	}

	/// <summary>看板排序：背包里有、昨天卖过、热门、当季、涨跌大、偏离原价多的靠前。</summary>
	public static float Relevance(MarketRow r)
	{
		float score = 0f;
		if (r.PlayerHas)
		{
			score += 4f;
		}
		if (r.PlayerSold > 0)
		{
			score += 3f;
		}
		if (r.Hot)
		{
			score += 2.5f;
		}
		if (!r.OutOfSeason)
		{
			score += 3f;
		}
		score += Math.Min(2f, Math.Abs(r.ChangePercent) / 3f);
		score += Math.Min(1f, Math.Abs(r.Factor - 1f) * 5f);
		return score;
	}

	public static IReadOnlyList<MarketRow> Board(IEnumerable<MarketRow> rows, int count) => rows.OrderByDescending(Relevance).ThenBy(r => r.DisplayName).Take(count).ToList();

	/// <summary>跟存档绑定的确定性哈希（FNV：文字、存档 ID 高低 32 位、天数、盐）。</summary>
	public static int Hash(string text, int day, int salt, ulong gameId)
	{
		unchecked
		{
			uint h = 2166136261u;
			foreach (char ch in text)
			{
				h = (h ^ ch) * 16777619u;
			}
			h = (h ^ (uint)gameId) * 16777619u;
			h = (h ^ (uint)(gameId >> 32)) * 16777619u;
			h = (h ^ (uint)day) * 16777619u;
			h = (h ^ (uint)salt) * 16777619u;
			return (int)(h & 0x7FFFFFFF);
		}
	}

	/// <summary>读档后补齐空集合。</summary>
	public static void Tidy(MarketState s)
	{
		s.Items ??= new Dictionary<string, MarketEntry>();
		s.PendingPlayerSales ??= new Dictionary<string, int>();
		s.LastNpcSales ??= new List<MarketSale>();
		s.HotItems ??= new List<string>();
		foreach (MarketEntry e in s.Items.Values)
		{
			e.History ??= new List<float>();
		}
	}
}
