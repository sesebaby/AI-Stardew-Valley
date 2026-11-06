using System.Collections.Generic;
using AISV.Core.Storage;

namespace AISV.Core.World.Market;

/// <summary>一种作物今天的行情（按收获物的带前缀 ID 记，例如 "(O)24"）。系数 1 = 原价。</summary>
public sealed class MarketEntry
{
	public string ItemId { get; set; } = "";

	/// <summary>滚动销量（件）：每天先乘 0.86 再加上昨天卖出的。</summary>
	public float Volume { get; set; }

	public float Factor { get; set; } = 1f;

	public float Yesterday { get; set; } = 1f;

	/// <summary>最近 7 天的系数，最后一个是今天。</summary>
	public List<float> History { get; set; } = new List<float>();

	/// <summary>昨天你出货的件数。</summary>
	public int PlayerSold { get; set; }

	/// <summary>昨天村民卖出的件数。</summary>
	public int NpcSold { get; set; }
}

/// <summary>村民出货的一条记录。</summary>
public sealed class MarketSale
{
	public string Seller { get; set; } = "";

	public string ItemId { get; set; } = "";

	public int Count { get; set; }
}

/// <summary>市场的存档（farmer/market_prices.json，每个存档每个玩家一份）。</summary>
public sealed class MarketState
{
	public const string Path = "farmer/market_prices.json";

	public static readonly DocumentType<MarketState> Doc = new DocumentType<MarketState>("市场", 1, () => new MarketState());

	/// <summary>最后一次算价是哪一天；读档时同一天不重复算。</summary>
	public int LastPricedDay { get; set; } = -1;

	public Dictionary<string, MarketEntry> Items { get; set; } = new Dictionary<string, MarketEntry>();

	/// <summary>夜里出货清单记下的、你卖出的作物（第二天早上计入销量）。</summary>
	public Dictionary<string, int> PendingPlayerSales { get; set; } = new Dictionary<string, int>();

	/// <summary>PendingPlayerSales 是哪一天卖的；同一天的出货清单只记一次。</summary>
	public int PendingDay { get; set; } = -1;

	public List<MarketSale> LastNpcSales { get; set; } = new List<MarketSale>();

	/// <summary>本周镇上特别想要的作物（每周换）。</summary>
	public List<string> HotItems { get; set; } = new List<string>();

	public int HotWeek { get; set; } = -1;

	/// <summary>最近一晚出货按行情比原价多（或少）拿的钱。</summary>
	public int LastNightDifference { get; set; }

	public int LastNightDay { get; set; } = -1;
}

/// <summary>参与行情的一种作物：收获物、显示名、原价、季节。</summary>
public sealed record MarketCrop(string ItemId, string DisplayName, int BasePrice, IReadOnlyList<string> Seasons);

/// <summary>出货清单上的一样东西。Key 是市场里对应的作物。</summary>
public sealed record ShippedItem(string Key, bool Artisan, int Stack, int BasePrice, int MarketPrice);

/// <summary>看板上的一行。</summary>
public sealed record MarketRow(string ItemId, string DisplayName, int BasePrice, int TodayPrice, float Factor, float ChangePercent, IReadOnlyList<float> History, bool Hot, bool OutOfSeason, bool Oversupplied, int PlayerSold, int NpcSold, bool PlayerHas);
