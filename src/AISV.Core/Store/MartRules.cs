using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Store;

/// <summary>Pierre 和 Joja 的经营状况：正常 → 吃紧 → 亏损 → 停业整顿（只在 AI星露谷 的经济里）。</summary>
public static class Competition
{
	public const string Pierre = "Pierre";

	public const string Joja = "Joja";

	public const int Normal = 0;

	public const int Tight = 1;

	public const int Losing = 2;

	public const int Closed = 3;

	/// <summary>三家店分走镇上购物的份额：吸引力 = 品种 × 价格 × 口碑；Pierre 和 Joja 有底子。</summary>
	public static (double Pierre, double Joja, double Ours) Shares(int ourKinds, double ourPriceRatio, int reputation, bool jojaOpen, double pierreBoost = 0, double jojaBoost = 0)
	{
		double ours = ourKinds <= 0 ? 0 : Math.Min(3.0, ourKinds * 0.18) * Math.Clamp(1.7 - ourPriceRatio * 0.7, 0.2, 1.4) * Math.Clamp(1 + reputation / 100.0, 0.5, 1.5);
		double pierre = 1.4 + pierreBoost;
		double joja = jojaOpen ? 1.1 + jojaBoost : 0;
		double total = ours + pierre + joja;
		return (pierre / total, joja / total, ours / total);
	}

	/// <summary>按最近一周的份额定经营状况：份额被抢走越多越难熬；对手停业后要份额回升到一半以上才重新营业。</summary>
	public static int Health(double weekShare, double normalShare, int current)
	{
		double ratio = normalShare <= 0 ? 1 : weekShare / normalShare;
		int target = ratio >= 0.8 ? Normal : ratio >= 0.6 ? Tight : ratio >= 0.4 ? Losing : Closed;
		if (current == Closed && ratio < 0.5)
		{
			return Closed;
		}
		// 一天最多变一档，免得忽上忽下。
		return target > current ? current + 1 : target < current ? current - 1 : current;
	}

	/// <summary>停业整顿时的营业时间（开门晚、关门早）。</summary>
	public static (int Open, int Close) Hours(int open, int close, int health) => health switch
	{
		Closed => (Math.Max(open, 1100), Math.Min(close, 1500)),
		Losing => (Math.Max(open, 1000), Math.Min(close, 1600)),
		_ => (open, close)
	};

	/// <summary>能不能来上门理论：亏损以上、这一季还没来过。</summary>
	public static bool MayConfront(int health, int lastSeason, int season, double roll, int percent = 20) => health >= Losing && lastSeason != season && roll * 100 < percent;
}

/// <summary>偷来的东西放在店里卖，买的人认不认得出来。</summary>
public static class StolenRules
{
	/// <summary>
	/// 基础：买的就是失主 15%，是失主的家人或密友 6%，其他人 1%；稀有（价值高）的 ×1.5～2；便宜得可疑（不到原价一半）×1.5；
	/// 偷来越久越难认，每过一周 ×0.8。最后限在 0～35%：只是有可能被认出来，不能让卖赃物这条路玩不下去。
	/// </summary>
	public static double Chance(bool buyerIsVictim, bool buyerIsClose, int basePrice, int price, int daysSinceStolen)
	{
		double p = buyerIsVictim ? 0.15 : buyerIsClose ? 0.06 : 0.01;
		p *= basePrice >= 1000 ? 2.0 : basePrice >= 300 ? 1.5 : 1.0;
		if (basePrice > 0 && price < basePrice / 2)
		{
			p *= 1.5;
		}
		p *= Math.Pow(0.8, Math.Max(0, daysSinceStolen) / 7);
		return Math.Clamp(p, 0, 0.35);
	}
}

/// <summary>失主认出赃物时的脾气：当场骂 / 冷脸拿走 / 难过失望 / 阴阳怪气。</summary>
public enum FenceStyle
{
	Hot,
	Cold,
	Hurt,
	Snide
}

/// <summary>
/// 卖赃物被认出的后果：按这一季的"案底"次数逐级加重。第 1 次罚款；第 2 次罚款翻倍、停业整顿 3 天；第 3 次起被带走（当天结束）、没收店里的赃物、罚三倍、停业 7 天。
/// 失主按脾气反应：好感扣多少、心情掉多少、记不记仇都不一样。
/// </summary>
public static class FenceRules
{
	/// <summary>原版村民的脾气（其他 mod 的 NPC 让 AI 按人设判断，判断不了当成 Hot）。</summary>
	public static readonly IReadOnlyDictionary<string, FenceStyle> Known = new Dictionary<string, FenceStyle>(StringComparer.OrdinalIgnoreCase)
	{
		["Shane"] = FenceStyle.Hot, ["Haley"] = FenceStyle.Hot, ["Clint"] = FenceStyle.Hot, ["Marnie"] = FenceStyle.Hot, ["Pierre"] = FenceStyle.Hot,
		["Pam"] = FenceStyle.Hot, ["Kent"] = FenceStyle.Hot, ["Gus"] = FenceStyle.Hot, ["Willy"] = FenceStyle.Hot, ["Lewis"] = FenceStyle.Hot,
		["Robin"] = FenceStyle.Hot, ["Jodi"] = FenceStyle.Hot, ["Abigail"] = FenceStyle.Hot,
		["Sebastian"] = FenceStyle.Cold, ["Elliott"] = FenceStyle.Cold, ["Demetrius"] = FenceStyle.Cold, ["Harvey"] = FenceStyle.Cold,
		["Linus"] = FenceStyle.Cold, ["Wizard"] = FenceStyle.Cold, ["Krobus"] = FenceStyle.Cold, ["Morris"] = FenceStyle.Cold,
		["Penny"] = FenceStyle.Hurt, ["Leah"] = FenceStyle.Hurt, ["Evelyn"] = FenceStyle.Hurt, ["George"] = FenceStyle.Hurt,
		["Emily"] = FenceStyle.Hurt, ["Caroline"] = FenceStyle.Hurt, ["Maru"] = FenceStyle.Hurt, ["Sandy"] = FenceStyle.Hurt,
		["Alex"] = FenceStyle.Snide, ["Sam"] = FenceStyle.Snide, ["Gil"] = FenceStyle.Snide, ["Marlon"] = FenceStyle.Snide
	};

	public static FenceStyle? StyleOf(string npc) => Known.TryGetValue(npc, out FenceStyle s) ? s : null;

	public static FenceStyle Parse(string? text) => text?.Trim().ToLowerInvariant() switch
	{
		"cold" => FenceStyle.Cold,
		"hurt" => FenceStyle.Hurt,
		"snide" => FenceStyle.Snide,
		_ => FenceStyle.Hot
	};

	/// <summary>好感扣多少倍：冷脸的最记仇，难过的其次，阴阳怪气的嘴上不饶人、心里没那么在意。</summary>
	public static double FriendshipFactor(FenceStyle s) => s switch
	{
		FenceStyle.Cold => 1.3,
		FenceStyle.Hurt => 1.2,
		FenceStyle.Snide => 0.8,
		_ => 1.0
	};

	/// <summary>失主心情掉多少。</summary>
	public static int MoodDrop(FenceStyle s) => s switch
	{
		FenceStyle.Hurt => -20,
		FenceStyle.Hot => -15,
		FenceStyle.Cold => -12,
		_ => -8
	};

	/// <summary>之后主动找来吵的火气（给"看到听到"的 confront）：冷脸的不来吵，只是记着。</summary>
	public static double Grudge(FenceStyle s) => s switch
	{
		FenceStyle.Hot => 3.0,
		FenceStyle.Hurt => 2.0,
		FenceStyle.Snide => 1.6,
		_ => 0
	};

	/// <summary>"案底"按季算：换季清零。</summary>
	public static int Strike(int lastSeason, int lastStrikes, int season) => (lastSeason == season ? lastStrikes : 0) + 1;

	/// <summary>罚款：货值 ×5，限在 300～3000g；第 2 次 ×2，第 3 次起 ×3。</summary>
	public static int Fine(int value, int strike) => Math.Clamp(value * 5, 300, 3000) * (strike >= 3 ? 3 : strike == 2 ? 2 : 1);

	/// <summary>停业整顿几天：第 1 次不停，第 2 次 3 天，第 3 次起 7 天。</summary>
	public static int ClosedDays(int strike) => strike >= 3 ? 7 : strike == 2 ? 3 : 0;

	/// <summary>第 3 次起被带走（关了逮捕的话只罚款、停业）。</summary>
	public static bool Arrest(int strike, bool allowed) => allowed && strike >= 3;

	/// <summary>口碑掉多少。</summary>
	public static int ReputationDrop(int strike) => strike >= 3 ? 40 : strike == 2 ? 30 : 20;
}

/// <summary>小超市自己的状态（store/farmer_mart.json）。</summary>
public sealed class MartState
{
	public const string Path = "store/farmer_mart.json";

	public static readonly DocumentType<MartState> Doc = new DocumentType<MartState>("农夫小超市", 1, () => new MartState());

	/// <summary>没人看店时收下的钱。</summary>
	public int Cashbox { get; set; }

	public int NextTx { get; set; } = 1;

	/// <summary>口碑（-100～100）：价格公道、认出赃物、动手打人都会改。</summary>
	public int Reputation { get; set; }

	/// <summary>Pierre / Joja 的经营状况和最近的份额。</summary>
	public Dictionary<string, RivalState> Rivals { get; set; } = new Dictionary<string, RivalState>(StringComparer.OrdinalIgnoreCase);

	/// <summary>让一步：到这天之前我们的价格对顾客来说贵两成。</summary>
	public int ConcedeUntil { get; set; } = -1;

	/// <summary>雇的店员：名字、每天工钱。</summary>
	public List<Clerk> Clerks { get; set; } = new List<Clerk>();

	/// <summary>送过开店礼包（货架、收银台）的建筑。</summary>
	public List<string> Welcomed { get; set; } = new List<string>();

	/// <summary>这些天的份额（三家，按天）。</summary>
	public List<DayShare> Shares { get; set; } = new List<DayShare>();

	/// <summary>店型（general / greengrocer / fishmonger / florist / deli / workshop / curio）。</summary>
	public string ShopType { get; set; } = "general";

	/// <summary>上次换店型是哪天（一周只能换一次）。</summary>
	public int ShopTypeDay { get; set; } = -100;

	/// <summary>累计营业额（店的经验，决定等级）。</summary>
	public int Xp { get; set; }

	/// <summary>顾客评价（最多留 80 条）。</summary>
	public List<Review> Reviews { get; set; } = new List<Review>();

	/// <summary>海报和橱窗。</summary>
	public List<Promotion> Promotions { get; set; } = new List<Promotion>();

	/// <summary>还没送到的进货单。</summary>
	public List<Order> Orders { get; set; } = new List<Order>();

	/// <summary>最近送到的进货（留 40 笔）：从 Pierre 进的货会帮他一把。</summary>
	public List<Order> Delivered { get; set; } = new List<Order>();

	/// <summary>这一季卖赃物被认出几次（Year*4+季节）。</summary>
	public int FenceSeason { get; set; } = -1;

	public int FenceStrikes { get; set; }

	/// <summary>停业整顿到这天（不含）为止。</summary>
	public int ClosedUntil { get; set; } = -1;

	/// <summary>在外面被认出、等你回来时再被带走（第 3 次）。</summary>
	public bool ArrestPending { get; set; }

	/// <summary>今天用过喇叭没有。</summary>
	public int MegaphoneDay { get; set; } = -1;
}

public sealed class RivalState
{
	public int Health { get; set; }

	/// <summary>上一次来理论是哪一季（年×4+季）。</summary>
	public int ConfrontedSeason { get; set; } = -1;

	public int Brawls { get; set; }

	/// <summary>投资入股、合伙之后不再抢生意。</summary>
	public bool Partner { get; set; }
}

public sealed class Clerk
{
	public string Npc { get; set; } = "";

	public int Wage { get; set; }

	public int HiredDay { get; set; }

	/// <summary>岗位：cashier 收银 / stocker 理货 / greeter 迎宾。</summary>
	public string Role { get; set; } = Staff.Cashier;

	/// <summary>合同到哪天（含这天）；0 = 没写期限（旧存档）。</summary>
	public int Until { get; set; }
}

public sealed class DayShare
{
	public int Day { get; set; }

	public double Pierre { get; set; }

	public double Joja { get; set; }

	public double Ours { get; set; }
}
