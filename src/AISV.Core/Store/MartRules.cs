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
	/// 基础：买的就是失主 60%，是失主的家人或密友 25%，其他人 5%；稀有（价值高）的 ×1.5～2；便宜得可疑（不到原价一半）×1.5；
	/// 偷来越久越难认，每过一周 ×0.8。最后限在 0～95%。
	/// </summary>
	public static double Chance(bool buyerIsVictim, bool buyerIsClose, int basePrice, int price, int daysSinceStolen)
	{
		double p = buyerIsVictim ? 0.60 : buyerIsClose ? 0.25 : 0.05;
		p *= basePrice >= 1000 ? 2.0 : basePrice >= 300 ? 1.5 : 1.0;
		if (basePrice > 0 && price < basePrice / 2)
		{
			p *= 1.5;
		}
		p *= Math.Pow(0.8, Math.Max(0, daysSinceStolen) / 7);
		return Math.Clamp(p, 0, 0.95);
	}
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
