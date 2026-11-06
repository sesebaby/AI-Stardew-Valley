using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;

namespace AISV.Core.Apps;

/// <summary>一件东西：分类号和价钱（挑"宜送 / 忌送"用）。</summary>
public sealed record TasteItem(int Category, int Price);

/// <summary>一个人今天的星座运势。</summary>
public sealed record Fortune(string Npc, int Sign, int Stars, string Good, string Bad, string Line, int LuckyIndex);

public static class Horoscope
{
	public static readonly string[] Signs =
	{
		"春芽座", "春花座", "春雨座", "夏阳座", "夏浪座", "夏萤座",
		"秋叶座", "秋实座", "秋月座", "冬雪座", "冬炉座", "冬星座"
	};

	private static readonly string[] SeasonKeys = { "spring", "summer", "fall", "winter" };

	/// <summary>生日在哪一季的哪一旬：1–9、10–18、19–28。</summary>
	public static int SignOf(string? season, int day)
	{
		int s = Array.FindIndex(SeasonKeys, k => k.Equals((season ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		if (s < 0 || day < 1 || day > 28)
		{
			return -1;
		}
		return s * 3 + (day <= 9 ? 0 : day <= 18 ? 1 : 2);
	}

	/// <summary>分类号 → 一类东西的叫法；不认识的为空。</summary>
	public static string Kind(int category) => category switch
	{
		-80 => "花",
		-2 => "宝石",
		-12 => "矿石",
		-4 => "鱼",
		-7 => "菜肴",
		-79 => "水果",
		-75 => "蔬菜",
		-26 => "手工酿制的东西",
		-5 => "蛋",
		-6 => "奶",
		-81 => "野菜",
		-27 => "糖浆",
		-18 => "动物产品",
		-28 => "怪物掉落物",
		-15 => "金属锭",
		-16 => "建筑材料",
		-20 => "垃圾",
		-23 => "贝壳",
		_ => ""
	};

	/// <summary>每类东西的分量：最爱（最讨厌）的算 2，喜欢（不喜欢）的算 1。</summary>
	public static Dictionary<string, double> Weights(IEnumerable<TasteItem> strong, IEnumerable<TasteItem> mild)
	{
		Dictionary<string, double> w = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach ((TasteItem item, double weight) in strong.Select(i => (i, 2.0)).Concat(mild.Select(i => (i, 1.0))))
		{
			string kind = Kind(item.Category);
			if (kind.Length > 0)
			{
				w[kind] = w.GetValueOrDefault(kind) + weight;
			}
		}
		return w;
	}

	/// <summary>全村平均每类的分量。</summary>
	public static Dictionary<string, double> Average(IReadOnlyCollection<Dictionary<string, double>> all) => all.Count == 0
		? new Dictionary<string, double>()
		: all.SelectMany(d => d).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value) / all.Count, StringComparer.Ordinal);

	/// <summary>挑出这个人和全村比最特别的一类：最该送的、最不该送的（两样不重复）。</summary>
	public static (string Good, string Bad) Picks(Dictionary<string, double> loved, Dictionary<string, double> hated, Dictionary<string, double> villageLoved, Dictionary<string, double> villageHated)
	{
		static string Top(Dictionary<string, double> own, Dictionary<string, double> village, string except) => own
			.Where(kv => kv.Value > 0 && kv.Key != except)
			.OrderByDescending(kv => kv.Value - village.GetValueOrDefault(kv.Key)).ThenByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
			.Select(kv => kv.Key).FirstOrDefault() ?? "";
		string good = Top(loved, villageLoved, "");
		return (good, Top(hated, villageHated, good));
	}

	/// <summary>同一个存档、同一天、同一个人，结果都一样。</summary>
	public static int Roll(ulong gameId, int day, string npc, int salt, int range)
	{
		unchecked
		{
			ulong h = gameId * 0x9E3779B97F4A7C15UL ^ (ulong)(day * 7919 + salt * 104729);
			foreach (char c in npc.ToLowerInvariant())
			{
				h = (h ^ c) * 0x100000001B3UL;
			}
			h ^= h >> 31;
			return (int)(h % (ulong)Math.Max(1, range));
		}
	}

	/// <summary>星数：心情占一半，运气占一半（1～5）。</summary>
	public static int Stars(int moodScore, int roll5) => Math.Clamp((int)Math.Round((Math.Clamp(moodScore, -100, 100) + 100) / 200.0 * 2.0 + roll5 * 0.6) + 1, 1, 5);

	public static Fortune For(string npc, int sign, int moodScore, string good, string bad, ulong gameId, int day)
	{
		int stars = Stars(moodScore, Roll(gameId, day, npc, 1, 5));
		string line = Line(stars, Roll(gameId, day, npc, 2, 3));
		return new Fortune(npc, sign, stars, good, bad, line, Roll(gameId, day, npc, 3, 1000));
	}

	public static string Line(int stars, int variant) => (stars, variant) switch
	{
		(>= 5, 0) => Tr.Get("horo.line.5a", "今天运气顶好，说什么都顺耳。"),
		(>= 5, 1) => Tr.Get("horo.line.5b", "心情明亮，适合当面说句好听的。"),
		(>= 5, _) => Tr.Get("horo.line.5c", "星星都站在这边，主动一点没错。"),
		(4, 0) => Tr.Get("horo.line.4a", "整体不错，小惊喜会被记很久。"),
		(4, 1) => Tr.Get("horo.line.4b", "心情偏好，适合约个时间见面。"),
		(4, _) => Tr.Get("horo.line.4c", "顺风顺水，别把话说得太满。"),
		(3, 0) => Tr.Get("horo.line.3a", "平平常常的一天，照常就好。"),
		(3, 1) => Tr.Get("horo.line.3b", "有点心不在焉，聊天挑轻松的话题。"),
		(3, _) => Tr.Get("horo.line.3c", "不好不坏，送对东西能加分。"),
		(2, 0) => Tr.Get("horo.line.2a", "有点烦，别去碰旧话题。"),
		(2, 1) => Tr.Get("horo.line.2b", "容易计较小事，给点空间。"),
		(2, _) => Tr.Get("horo.line.2c", "心里不太痛快，少说多听。"),
		(_, 0) => Tr.Get("horo.line.1a", "诸事不宜，今天最好别去惹。"),
		(_, 1) => Tr.Get("horo.line.1b", "火气大，说错一句会记很久。"),
		_ => Tr.Get("horo.line.1c", "乌云压顶，明天再说吧。")
	};

	/// <summary>"宜送花，忌送宝石"。</summary>
	public static string Advice(Fortune f)
	{
		List<string> parts = new List<string>();
		if (f.Good.Length > 0)
		{
			parts.Add(Tr.Get("horo.good", "宜送{{kind}}", ("kind", f.Good)));
		}
		if (f.Bad.Length > 0)
		{
			parts.Add(Tr.Get("horo.bad", "忌送{{kind}}", ("kind", f.Bad)));
		}
		return string.Join(Tr.Get("horo.sep", "，"), parts);
	}
}
