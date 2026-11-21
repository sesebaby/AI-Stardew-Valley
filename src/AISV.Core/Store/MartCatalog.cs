using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Store;

/// <summary>货的大类：货架能放什么、顾客想买什么、多久会坏、该放冷柜还是保温柜，都按这个分。</summary>
public static class Goods
{
	public const string Vegetable = "vegetable";

	public const string Fruit = "fruit";

	public const string Flower = "flower";

	public const string Fish = "fish";

	public const string Animal = "animal";

	public const string Artisan = "artisan";

	public const string Food = "food";

	public const string Forage = "forage";

	public const string Mineral = "mineral";

	public const string Artifact = "artifact";

	public const string General = "general";

	public static readonly string[] All = { Vegetable, Fruit, Flower, Fish, Animal, Artisan, Food, Forage, Mineral, Artifact, General };

	/// <summary>游戏物品分类 → 大类（文物要调用方告诉，游戏里文物的分类是 0）。</summary>
	public static string Of(int category, bool artifact = false)
	{
		if (artifact)
		{
			return Artifact;
		}
		return category switch
		{
			-75 => Vegetable,
			-79 => Fruit,
			-80 => Flower,
			-4 => Fish,
			-5 or -6 or -18 => Animal,
			-26 or -27 => Artisan,
			-7 => Food,
			-81 or -23 => Forage,
			-2 or -12 => Mineral,
			_ => General
		};
	}

	/// <summary>常温下放几天开始过期（0 = 不会坏）。</summary>
	public static int LifeDays(string group) => group switch
	{
		Vegetable => 6,
		Fruit => 6,
		Flower => 4,
		Fish => 2,
		Animal => 4,
		Food => 2,
		Forage => 5,
		_ => 0
	};

	/// <summary>最合适的存放温度。</summary>
	public static string Prefers(string group) => group switch
	{
		Fish or Animal => Temps.Chilled,
		Food => Temps.Warm,
		_ => Temps.Ambient
	};
}

public static class Temps
{
	public const string Ambient = "ambient";

	public const string Chilled = "chilled";

	public const string Warm = "warm";
}

/// <summary>货架的种类：几个格子、每格放多少、温度、能放哪些货、多少钱。贴图在游戏层从原版图块拼。</summary>
public sealed record ShelfKind(string Id, int Slots, int PerSlot, string Temp, string[] Groups, int Price, double Attention = 1.0)
{
	public bool Allows(string group) => Groups.Contains(group);
}

public static class ShelfKinds
{
	/// <summary>皮埃尔店那种白货架（三格宽）：什么都能放。</summary>
	public static readonly ShelfKind Shelf = new ShelfKind("shelf", 6, 20, Temps.Ambient, Goods.All, 600);

	/// <summary>窄货架（两格宽）。</summary>
	public static readonly ShelfKind Small = new ShelfKind("shelf_small", 4, 20, Temps.Ambient, Goods.All, 400);

	/// <summary>Joja 那种玻璃冷柜：鱼、蛋奶放这里不容易坏。</summary>
	public static readonly ShelfKind Cold = new ShelfKind("cold_case", 6, 20, Temps.Chilled, new[] { Goods.Fish, Goods.Animal, Goods.Fruit, Goods.Vegetable, Goods.Flower, Goods.Artisan, Goods.Food }, 2500);

	/// <summary>保温柜：只放熟食。</summary>
	public static readonly ShelfKind Warm = new ShelfKind("warm_case", 4, 10, Temps.Warm, new[] { Goods.Food }, 2000);

	/// <summary>促销桶：一样货堆成一堆，进门的人一眼就看到。</summary>
	public static readonly ShelfKind Bin = new ShelfKind("promo_bin", 1, 40, Temps.Ambient, new[] { Goods.Vegetable, Goods.Fruit, Goods.Forage, Goods.Mineral, Goods.Flower }, 300, 1.35);

	/// <summary>木柜：放酒、果酱、矿石、文物这些精品。</summary>
	public static readonly ShelfKind Cabinet = new ShelfKind("cabinet", 6, 10, Temps.Ambient, new[] { Goods.Artisan, Goods.Mineral, Goods.Artifact, Goods.Flower, Goods.General }, 1200, 1.1);

	public static readonly ShelfKind[] All = { Shelf, Small, Cold, Warm, Bin, Cabinet };

	public static ShelfKind? Get(string id) => All.FirstOrDefault(k => k.Id == id);
}

/// <summary>新鲜度：当日上架 / 新鲜 / 正常 / 临期 / 过期。</summary>
public static class Freshness
{
	public const string SameDay = "same_day";

	public const string Fresh = "fresh";

	public const string Normal = "normal";

	public const string Near = "near";

	public const string Expired = "expired";

	/// <summary>这种货在这个温度下能放几天（0 = 不会坏）：放对地方翻倍，该冷藏的放常温减半。</summary>
	public static int Life(string group, string temp, bool longerChilled = false)
	{
		int life = Goods.LifeDays(group);
		if (life == 0)
		{
			return 0;
		}
		string want = Goods.Prefers(group);
		if (temp == want && want != Temps.Ambient || temp == Temps.Chilled && group is Goods.Vegetable or Goods.Fruit or Goods.Flower)
		{
			life *= 2;
			if (longerChilled && group == Goods.Fish)
			{
				life += life / 2;
			}
		}
		else if (want != Temps.Ambient && temp != want)
		{
			life = Math.Max(1, life / 2);
		}
		return life;
	}

	public static string Stage(int life, int days)
	{
		if (days <= 0)
		{
			return SameDay;
		}
		if (life == 0)
		{
			return Normal;
		}
		if (days > life)
		{
			return Expired;
		}
		if (days * 3 <= life)
		{
			return Fresh;
		}
		return days * 3 < life * 2 ? Normal : Near;
	}

	/// <summary>顾客想不想买（乘在挑货分数上）。</summary>
	public static double Appeal(string stage) => stage switch
	{
		SameDay => 1.15,
		Fresh => 1.05,
		Near => 0.7,
		Expired => 0.12,
		_ => 1.0
	};

	/// <summary>评价时加减几分（-3～+2）。</summary>
	public static double ReviewDelta(string stage) => stage switch
	{
		SameDay => 0.8,
		Fresh => 0.4,
		Near => -0.6,
		Expired => -2.0,
		_ => 0
	};
}

/// <summary>货的来路：自家农场种的、配偶做的、从别人那里进的货。</summary>
public static class Origins
{
	public const string Farm = "farm";

	public const string Spouse = "spouse";

	public const string Supplier = "supplier";

	/// <summary>自家种的菜、果、蛋奶、手工品，顾客愿意多出一点钱（能接受的价格上浮）。</summary>
	public static double Premium(string origin, string group) => origin is Farm or Spouse && group is Goods.Vegetable or Goods.Fruit or Goods.Animal or Goods.Artisan or Goods.Flower ? 0.1 : 0;
}

/// <summary>今天的行情：每类货的需求系数，一类紧俏、一类滞销。同一天算出来都一样。</summary>
public sealed record MarketDay(IReadOnlyDictionary<string, double> Demand, string Hot, string Cold)
{
	public double Of(string group) => Demand.TryGetValue(group, out double d) ? d : 1.0;
}

public static class Market
{
	private static readonly string[] Movers = { Goods.Vegetable, Goods.Fruit, Goods.Flower, Goods.Fish, Goods.Animal, Goods.Artisan, Goods.Food, Goods.Forage, Goods.Mineral };

	/// <summary>season：0 春 1 夏 2 秋 3 冬。冬天菜和水果更紧俏，下雨天鱼好卖、花难卖。</summary>
	public static MarketDay For(int day, int season, bool rain)
	{
		Random r = new Random(day * 7919 + 17);
		Dictionary<string, double> demand = Movers.ToDictionary(g => g, _ => 0.88 + r.NextDouble() * 0.24);
		demand[Goods.General] = 1.0;
		demand[Goods.Artifact] = 1.0;
		List<string> pool = Movers.ToList();
		if (season == 3)
		{
			pool.AddRange(new[] { Goods.Vegetable, Goods.Fruit, Goods.Vegetable });
		}
		string hot = pool[r.Next(pool.Count)];
		string cold = Movers.Where(g => g != hot).ElementAt(r.Next(Movers.Length - 1));
		demand[hot] = 1.35 + r.NextDouble() * 0.15;
		demand[cold] = 0.65 + r.NextDouble() * 0.1;
		if (rain)
		{
			demand[Goods.Fish] *= 1.15;
			demand[Goods.Flower] *= 0.85;
		}
		return new MarketDay(demand, hot, cold);
	}
}

/// <summary>店型：主打哪几类货。主打的货更好卖、顾客能接受的价格更高，等级越高越明显，5 级和 10 级各有一样本事。</summary>
public sealed record ShopType(string Id, string[] Groups, string[] Archetypes);

public static class ShopTypes
{
	public static readonly ShopType General = new ShopType("general", Goods.All, new[] { Archetypes.Browser });

	public static readonly ShopType Greengrocer = new ShopType("greengrocer", new[] { Goods.Vegetable, Goods.Fruit, Goods.Forage }, new[] { Archetypes.Cook, Archetypes.FruitLover });

	public static readonly ShopType Fishmonger = new ShopType("fishmonger", new[] { Goods.Fish }, new[] { Archetypes.FishBuyer });

	public static readonly ShopType Florist = new ShopType("florist", new[] { Goods.Flower }, new[] { Archetypes.Gifter });

	public static readonly ShopType Deli = new ShopType("deli", new[] { Goods.Food }, new[] { Archetypes.Hungry });

	public static readonly ShopType Workshop = new ShopType("workshop", new[] { Goods.Artisan }, new[] { Archetypes.Connoisseur });

	public static readonly ShopType Curio = new ShopType("curio", new[] { Goods.Mineral, Goods.Artifact }, new[] { Archetypes.Collector });

	public static readonly ShopType[] All = { General, Greengrocer, Fishmonger, Florist, Deli, Workshop, Curio };

	public static ShopType Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? General;

	/// <summary>店的等级（1～10），按累计营业额。</summary>
	public static int Level(int xp)
	{
		int[] steps = { 0, 800, 2500, 5000, 9000, 15000, 23000, 33000, 45000, 60000 };
		int level = 1;
		for (int i = 1; i < steps.Length; i++)
		{
			if (xp >= steps[i])
			{
				level = i + 1;
			}
		}
		return level;
	}

	public static int NextLevelXp(int level)
	{
		int[] steps = { 0, 800, 2500, 5000, 9000, 15000, 23000, 33000, 45000, 60000 };
		return level >= 10 ? steps[9] : steps[level];
	}

	/// <summary>主打的货：需求 ×1.2（每级再 +2%）；杂货店什么都不偏。不主打的货在专门店里 ×0.85。</summary>
	public static double Demand(ShopType t, string group, int level)
	{
		if (t == General)
		{
			return 1.0;
		}
		return t.Groups.Contains(group) ? 1.2 + level * 0.02 : 0.85;
	}

	/// <summary>主打的货顾客能多接受几成价（5% + 每级 1%）。</summary>
	public static double Tolerance(ShopType t, string group, int level) => t != General && t.Groups.Contains(group) ? 0.05 + level * 0.01 : 0;
}

/// <summary>顾客类型：想买哪类、最多几样、什么时候来得多。</summary>
public sealed record Archetype(string Id, string[] Groups, int MaxItems, (int From, int To)[] Peaks);

public static class Archetypes
{
	public const string Browser = "browser";

	public const string Cook = "cook";

	public const string FruitLover = "fruit";

	public const string FishBuyer = "fish";

	public const string Gifter = "gift";

	public const string Hungry = "hungry";

	public const string Connoisseur = "connoisseur";

	public const string Collector = "collector";

	public static readonly Archetype[] All =
	{
		new Archetype(Browser, Goods.All, 2, Array.Empty<(int, int)>()),
		new Archetype(Cook, new[] { Goods.Vegetable, Goods.Animal, Goods.Forage }, 3, new[] { (900, 1200), (1500, 1800) }),
		new Archetype(FruitLover, new[] { Goods.Fruit }, 2, new[] { (1300, 1700) }),
		new Archetype(FishBuyer, new[] { Goods.Fish }, 2, new[] { (900, 1300) }),
		new Archetype(Gifter, new[] { Goods.Flower, Goods.Artisan }, 1, new[] { (1400, 1900) }),
		new Archetype(Hungry, new[] { Goods.Food }, 2, new[] { (1100, 1300), (1700, 1900) }),
		new Archetype(Connoisseur, new[] { Goods.Artisan }, 1, Array.Empty<(int, int)>()),
		new Archetype(Collector, new[] { Goods.Mineral, Goods.Artifact }, 1, Array.Empty<(int, int)>())
	};

	public static Archetype Get(string id) => All.FirstOrDefault(a => a.Id == id) ?? All[0];

	/// <summary>有些人一看就是哪类顾客（Gus 买菜、Evelyn 买花……）。</summary>
	public static readonly IReadOnlyDictionary<string, string> Habits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Gus"] = Cook,
		["Evelyn"] = Gifter,
		["Caroline"] = Cook,
		["Jodi"] = Cook,
		["Marnie"] = Cook,
		["Pam"] = Hungry,
		["Shane"] = Hungry,
		["Clint"] = Collector,
		["Emily"] = Collector,
		["Elliott"] = Connoisseur,
		["Harvey"] = Connoisseur,
		["Leah"] = Connoisseur,
		["Willy"] = FishBuyer,
		["Linus"] = FishBuyer,
		["Haley"] = Gifter,
		["Alex"] = FruitLover,
		["Maru"] = Collector,
		["George"] = Hungry,
		["Lewis"] = Browser
	};

	/// <summary>
	/// 这位顾客是哪类：店型主打的类型 ×3，正赶上这类人来得多的时候 ×1.6，本人习惯 ×4；只看店里有货的类型（逛逛的人总在）。
	/// roll 是 [0,1) 的随机数。
	/// </summary>
	public static Archetype Pick(string npc, ShopType shop, int time, ISet<string> stockedGroups, double roll)
	{
		List<(Archetype A, double W)> weights = new List<(Archetype, double)>();
		foreach (Archetype a in All)
		{
			if (a.Id != Browser && !a.Groups.Any(stockedGroups.Contains))
			{
				continue;
			}
			double w = a.Id == Browser ? 1.2 : 0.8;
			if (shop.Archetypes.Contains(a.Id))
			{
				w *= 3;
			}
			if (a.Peaks.Any(p => time >= p.From && time < p.To))
			{
				w *= 1.6;
			}
			if (Habits.TryGetValue(npc, out string? habit) && habit == a.Id)
			{
				w *= 4;
			}
			weights.Add((a, w));
		}
		double total = weights.Sum(x => x.W);
		double at = roll * total;
		foreach ((Archetype a, double w) in weights)
		{
			at -= w;
			if (at <= 0)
			{
				return a;
			}
		}
		return All[0];
	}
}

/// <summary>货架上的一样货（顾客挑货时看的）。Boost 是新鲜度、行情、店型、促销、温度合在一起的吸引力；Tolerance 是能多接受几成价。</summary>
public readonly record struct Offer(string ItemId, int Price, int BasePrice, int Quality, int Stock, int Taste, bool Needed, string Group = Goods.General, double Boost = 1.0, double Tolerance = 0);

/// <summary>顾客挑货：喜欢的、用得着的、便宜的、品质好的、新鲜的更想买；买不起的不买。</summary>
public static class CustomerDemand
{
	/// <summary>游戏的送礼喜好：0 最爱、2 喜欢、4 不喜欢、6 讨厌、8 一般。</summary>
	public static double TasteWeight(int taste) => taste switch
	{
		0 => 3.0,
		2 => 1.8,
		4 => 0.3,
		6 => 0.05,
		_ => 1.0
	};

	/// <summary>价格吸引力：比原价便宜越多越想买，贵一倍基本不买（tolerance：能多接受几成）。</summary>
	public static double PriceWeight(int price, int basePrice, double tolerance = 0)
	{
		if (basePrice <= 0)
		{
			return 1;
		}
		double ratio = price / (double)basePrice / (1 + tolerance);
		return Math.Clamp(1.6 - ratio * 0.8, 0.05, 1.4);
	}

	public static double Score(Offer o, int budget)
	{
		if (o.Stock <= 0 || o.Price <= 0 || o.Price > budget)
		{
			return 0;
		}
		return TasteWeight(o.Taste) * PriceWeight(o.Price, o.BasePrice, o.Tolerance) * (1 + o.Quality * 0.15) * (o.Needed ? 1.6 : 1) * o.Boost;
	}

	/// <summary>挑一样买（roll 是 [0,1) 的随机数）；什么都不想买返回 -1。买几件：预算够时最多 3 件。</summary>
	public static (int Index, int Qty) Pick(IReadOnlyList<Offer> offers, int budget, double roll)
	{
		double[] scores = offers.Select(o => Score(o, budget)).ToArray();
		double total = scores.Sum();
		if (total < 0.3)
		{
			return (-1, 0);
		}
		double at = roll * total;
		for (int i = 0; i < scores.Length; i++)
		{
			at -= scores[i];
			if (at <= 0 && scores[i] > 0)
			{
				Offer o = offers[i];
				int qty = Math.Max(1, Math.Min(Math.Min(o.Stock, 3), budget / Math.Max(1, o.Price)));
				return (i, o.Taste == 0 ? qty : 1);
			}
		}
		return (-1, 0);
	}

	/// <summary>
	/// 按顾客类型挑一篮子：只看想要的那几类（逛逛的人什么都看），每次按分数抽一样，花不超过预算，最多 maxItems 样。
	/// 返回 (第几样, 几件)；空的就是没买到想要的。
	/// </summary>
	public static List<(int Index, int Qty)> Basket(IReadOnlyList<Offer> offers, Archetype a, int budget, Random random)
	{
		List<(int, int)> basket = new List<(int, int)>();
		HashSet<int> taken = new HashSet<int>();
		int left = budget;
		int want = 1 + random.Next(a.MaxItems);
		for (int n = 0; n < want; n++)
		{
			List<int> pool = Enumerable.Range(0, offers.Count).Where(i => !taken.Contains(i) && a.Groups.Contains(offers[i].Group)).ToList();
			double[] scores = pool.Select(i => Score(offers[i], left)).ToArray();
			double total = scores.Sum();
			if (total < 0.3)
			{
				break;
			}
			double at = random.NextDouble() * total;
			for (int k = 0; k < pool.Count; k++)
			{
				at -= scores[k];
				if (at > 0 || scores[k] <= 0)
				{
					continue;
				}
				Offer o = offers[pool[k]];
				int qty = o.Taste == 0 || a.Id == Archetypes.Cook ? Math.Max(1, Math.Min(Math.Min(o.Stock, 3), left / Math.Max(1, o.Price))) : 1;
				basket.Add((pool[k], qty));
				taken.Add(pool[k]);
				left -= qty * o.Price;
				break;
			}
		}
		return basket;
	}

	/// <summary>这半小时来几位顾客：货越多样、价格越公道来得越多（0～3）。</summary>
	public static int Arrivals(int kinds, double avgPriceRatio, double share, double roll)
	{
		if (kinds <= 0)
		{
			return 0;
		}
		double expected = Math.Min(2.5, 0.6 + kinds * 0.15) * Math.Clamp(1.6 - avgPriceRatio * 0.6, 0.3, 1.3) * Math.Clamp(share * 2.2, 0.6, 1.4);
		int n = (int)expected;
		return Math.Min(3, n + (roll < expected - n ? 1 : 0));
	}
}

/// <summary>客流：时段、周末、天气、口碑（星级）、宣传都会影响来多少人。</summary>
public static class Traffic
{
	public static double TimeFactor(int time) => time switch
	{
		< 1000 => 0.6,
		< 1200 => 0.9,
		< 1400 => 1.2,
		< 1600 => 0.9,
		< 1800 => 1.3,
		_ => 0.7
	};

	/// <summary>平均几星 → 客流系数（没有评价按 3 星）。</summary>
	public static double StarFactor(double stars) => 0.6 + (Math.Clamp(stars, 1, 5) - 1) / 4 * 0.8;

	/// <summary>
	/// 这半小时预计来几位：基础按货的种类和价格（照旧），再乘时段、周末 ×1.3、雨天 ×0.75、节日前一天 ×1.5、星级、宣传（promo 是额外几成）。
	/// </summary>
	public static double Expected(int kinds, double priceRatio, double share, int time, bool weekend, bool rain, bool festivalEve, double stars, double promo)
	{
		if (kinds <= 0)
		{
			return 0;
		}
		double baseRate = Math.Min(2.5, 0.6 + kinds * 0.15) * Math.Clamp(1.6 - priceRatio * 0.6, 0.3, 1.3) * Math.Clamp(share * 2.2, 0.6, 1.4);
		return baseRate * TimeFactor(time) * (weekend ? 1.3 : 1) * (rain ? 0.75 : 1) * (festivalEve ? 1.5 : 1) * StarFactor(stars) * (1 + promo);
	}

	public static int Arrivals(double expected, double roll)
	{
		int n = (int)expected;
		return Math.Min(4, n + (roll < expected - n ? 1 : 0));
	}
}

/// <summary>一条顾客评价。</summary>
public sealed class Review
{
	public int Day { get; set; }

	public int Time { get; set; }

	public string Npc { get; set; } = "";

	public int Stars { get; set; }

	/// <summary>最主要的原因（cheap / pricey / fresh / stale / quality / temp / fast / slow / cozy / bare / friendly / notfound / mistake / ok）。</summary>
	public string Reason { get; set; } = "";

	/// <summary>一句话（模板或 AI 写的）。</summary>
	public string Text { get; set; } = "";

	public string Items { get; set; } = "";
}

/// <summary>评价要看的东西。</summary>
public sealed record ReviewInput(bool Found, double PriceRatio, double FreshDelta, double AvgQuality, bool WrongTemp, int WaitSeconds, int Decor, bool Greeter, int Checkout);

public static class ReviewRules
{
	/// <summary>结账小游戏：没玩 0，找零又快又对 1，找错了 -1。</summary>
	public const int CheckoutPerfect = 1;

	public const int CheckoutWrong = -1;

	/// <summary>从 3 星起算，每项加减；返回星级（1～5）和影响最大的原因。</summary>
	public static (int Stars, string Reason) Score(ReviewInput i)
	{
		if (!i.Found)
		{
			return (i.WaitSeconds > 30 ? 1 : 2, "notfound");
		}
		List<(string Reason, double Delta)> parts = new List<(string, double)>
		{
			(i.PriceRatio <= 0.9 ? "cheap" : "pricey", i.PriceRatio <= 0.9 ? 0.8 : i.PriceRatio <= 1.1 ? 0.3 : i.PriceRatio >= 1.8 ? -2 : i.PriceRatio >= 1.4 ? -1 : 0),
			(i.FreshDelta >= 0 ? "fresh" : "stale", i.FreshDelta),
			("quality", Math.Min(1.0, i.AvgQuality * 0.3)),
			("temp", i.WrongTemp ? -1 : 0),
			(i.WaitSeconds <= 8 ? "fast" : "slow", i.WaitSeconds <= 8 ? 0.4 : i.WaitSeconds > 40 ? -2 : i.WaitSeconds > 20 ? -1 : 0),
			(i.Decor >= 10 ? "cozy" : "bare", i.Decor >= 10 ? 0.5 : i.Decor < 4 ? -0.5 : 0),
			("friendly", i.Greeter ? 0.5 : 0),
			(i.Checkout >= 0 ? "fast" : "mistake", i.Checkout == CheckoutPerfect ? 0.5 : i.Checkout == CheckoutWrong ? -1 : 0)
		};
		double score = 3 + parts.Sum(p => p.Delta);
		int stars = (int)Math.Round(Math.Clamp(score, 1, 5), MidpointRounding.AwayFromZero);
		(string reason, double delta) = stars >= 3 ? parts.OrderByDescending(p => p.Delta).First() : parts.OrderBy(p => p.Delta).First();
		return (stars, Math.Abs(delta) < 0.25 ? "ok" : reason);
	}

	/// <summary>最近 20 条的平均星级（没有评价时 3）。</summary>
	public static double Average(IEnumerable<Review> reviews)
	{
		List<Review> last = reviews.TakeLast(20).ToList();
		return last.Count == 0 ? 3 : last.Average(r => r.Stars);
	}

	/// <summary>平均星级 → 口碑（-100～100）。</summary>
	public static int Reputation(double stars) => (int)Math.Clamp((stars - 3) * 50, -100, 100);
}

/// <summary>宣传：海报（一周，主推一类货）、橱窗（主推一样货）、喇叭（一天一次，马上招来几位客人）。</summary>
public sealed class Promotion
{
	public const string Poster = "poster";

	public const string Window = "window";

	public string Kind { get; set; } = Poster;

	/// <summary>海报：主推的大类；橱窗：物品 ID。</summary>
	public string Target { get; set; } = "";

	public int From { get; set; }

	public int Until { get; set; }
}

public static class Promotions
{
	public const int PosterCost = 600;

	public const int PosterDays = 7;

	public const int MegaphoneCost = 250;

	public const int MegaphoneCustomers = 3;

	/// <summary>这类货有海报：需求 ×1.4。</summary>
	public static double PosterBoost(IEnumerable<Promotion> promos, string group, int day) => promos.Any(p => p.Kind == Promotion.Poster && p.Target == group && day >= p.From && day <= p.Until) ? 1.4 : 1.0;

	/// <summary>橱窗里的那样货：需求 ×1.8。</summary>
	public static double WindowBoost(IEnumerable<Promotion> promos, string itemId, int day) => promos.Any(p => p.Kind == Promotion.Window && p.Target == itemId && day >= p.From && day <= p.Until) ? 1.8 : 1.0;

	/// <summary>海报让客流多几成（每张 15%，最多 45%）。</summary>
	public static double TrafficBonus(IEnumerable<Promotion> promos, int day) => Math.Min(0.45, promos.Count(p => p.Kind == Promotion.Poster && day >= p.From && day <= p.Until) * 0.15);
}

/// <summary>收银小游戏：顾客给钱、你找零。</summary>
public static class Checkout
{
	public static readonly int[] Coins = { 500, 100, 50, 10, 5, 1 };

	/// <summary>顾客递过来多少钱：凑个整（到 10 / 50 / 100 / 500 / 1000），偶尔刚好给够。</summary>
	public static int Paid(int total, double roll)
	{
		if (roll < 0.12)
		{
			return total;
		}
		int[] steps = total < 50 ? new[] { 10, 50, 100 } : total < 300 ? new[] { 50, 100, 500 } : total < 1500 ? new[] { 100, 500, 1000 } : new[] { 500, 1000, 5000 };
		int step = steps[(int)(roll * 10) % steps.Length];
		int paid = (total / step + 1) * step;
		return paid == total ? paid + step : paid;
	}

	/// <summary>找得对不对、给多少小费：又对又快（10 秒内）小费一成，20 秒内半成。</summary>
	public static (bool Ok, int Tip) Result(int total, int paid, int given, double seconds)
	{
		bool ok = given == paid - total;
		if (!ok)
		{
			return (false, 0);
		}
		int tip = seconds <= 10 ? Math.Max(1, total / 10) : seconds <= 20 ? Math.Max(1, total / 20) : 0;
		return (true, tip);
	}
}

/// <summary>进货：几位村民按季节供货，比卖价贵四成（自己种的才最赚），第二天早上送到库房。</summary>
public sealed record Supplier(string Npc, string Group, string[][] Seasons)
{
	public string[] Items(int season) => Seasons[Math.Clamp(season, 0, 3)];
}

public static class Suppliers
{
	public const double Markup = 1.4;

	public static readonly Supplier[] All =
	{
		new Supplier("Pierre", Goods.Vegetable, new[]
		{
			new[] { "(O)24", "(O)188", "(O)190", "(O)192", "(O)400", "(O)250" },
			new[] { "(O)256", "(O)260", "(O)254", "(O)258", "(O)270", "(O)264" },
			new[] { "(O)272", "(O)276", "(O)280", "(O)282", "(O)278", "(O)300" },
			new[] { "(O)412", "(O)416", "(O)414", "(O)418" }
		}),
		new Supplier("Marnie", Goods.Animal, new[]
		{
			new[] { "(O)176", "(O)180", "(O)184", "(O)436", "(O)440" },
			new[] { "(O)176", "(O)180", "(O)184", "(O)436", "(O)440" },
			new[] { "(O)176", "(O)180", "(O)184", "(O)436", "(O)440" },
			new[] { "(O)176", "(O)180", "(O)184", "(O)436", "(O)440" }
		}),
		new Supplier("Willy", Goods.Fish, new[]
		{
			new[] { "(O)129", "(O)131", "(O)137", "(O)145", "(O)147" },
			new[] { "(O)130", "(O)138", "(O)146", "(O)701", "(O)145" },
			new[] { "(O)131", "(O)139", "(O)142", "(O)150", "(O)701" },
			new[] { "(O)130", "(O)141", "(O)147", "(O)151", "(O)131" }
		}),
		new Supplier("Gus", Goods.Food, new[]
		{
			new[] { "(O)194", "(O)196", "(O)210", "(O)211", "(O)206" },
			new[] { "(O)194", "(O)198", "(O)200", "(O)206", "(O)214" },
			new[] { "(O)194", "(O)200", "(O)204", "(O)206", "(O)211" },
			new[] { "(O)194", "(O)210", "(O)211", "(O)206", "(O)218" }
		}),
		new Supplier("Clint", Goods.Mineral, new[]
		{
			new[] { "(O)80", "(O)86", "(O)84", "(O)82", "(O)68", "(O)66" },
			new[] { "(O)80", "(O)86", "(O)84", "(O)82", "(O)68", "(O)66" },
			new[] { "(O)80", "(O)86", "(O)84", "(O)82", "(O)68", "(O)66" },
			new[] { "(O)80", "(O)86", "(O)84", "(O)82", "(O)68", "(O)66" }
		}),
		new Supplier("Evelyn", Goods.Flower, new[]
		{
			new[] { "(O)591", "(O)597" },
			new[] { "(O)593", "(O)376", "(O)421" },
			new[] { "(O)595", "(O)421" },
			new[] { "(O)591" }
		})
	};

	public static Supplier? Get(string npc) => All.FirstOrDefault(s => s.Npc.Equals(npc, StringComparison.OrdinalIgnoreCase));

	/// <summary>进货单价：出货价 × 1.4（至少 1g）。</summary>
	public static int Cost(int sellPrice) => Math.Max(1, (int)Math.Ceiling(sellPrice * Markup));
}

/// <summary>进货单：今天下单，明早送到库房。</summary>
public sealed class Order
{
	public string Supplier { get; set; } = "";

	public string ItemId { get; set; } = "";

	public int Qty { get; set; }

	public int Cost { get; set; }

	public int Day { get; set; }
}

/// <summary>店员的岗位和合同。</summary>
public static class Staff
{
	public const string Cashier = "cashier";

	public const string Stocker = "stocker";

	public const string Greeter = "greeter";

	public static readonly string[] Roles = { Cashier, Stocker, Greeter };

	public const int MaxStaff = 3;

	/// <summary>基础日薪：收银 200、理货 180、迎宾 150。</summary>
	public static int BaseWage(string role) => role switch
	{
		Cashier => 200,
		Stocker => 180,
		_ => 150
	};

	/// <summary>开价：4 心以上每多一心便宜 10g；Joja 不行了时 Sam 和 Shane 便宜三成；签四周的每天便宜 15%。最低 80g。</summary>
	public static int Wage(string role, int hearts, bool jojaStruggling, string npc, int days)
	{
		double wage = BaseWage(role) - Math.Max(0, hearts - 4) * 10;
		if (jojaStruggling && npc is "Sam" or "Shane")
		{
			wage *= 0.7;
		}
		if (days >= 28)
		{
			wage *= 0.85;
		}
		return Math.Max(80, (int)Math.Round(wage / 5) * 5);
	}
}
