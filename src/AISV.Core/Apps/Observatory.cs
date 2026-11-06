using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;

namespace AISV.Core.Apps;

/// <summary>今晚的天空。</summary>
public sealed record NightSky(int Phase, string Moon, bool Clear, bool Meteors, int Stars, IReadOnlyList<string> Spots, string Summary);

public static class Observatory
{
	public const int MeteorNightsPerSeason = 2;

	private static readonly string[] Moons = { "新月", "娥眉月", "上弦月", "盈凸月", "满月", "亏凸月", "下弦月", "残月" };

	private static readonly string[] SeasonKeys = { "spring", "summer", "fall", "winter" };

	/// <summary>一季 28 天正好一个月相周期：第 1 天新月，第 15 天满月；0～7。</summary>
	public static int Phase(int day) => (Math.Clamp(day, 1, 28) - 1 + 2) / 4 % 8;

	public static string MoonName(int phase) => Moons[Math.Clamp(phase, 0, 7)];

	/// <summary>月亮亮的比例（0 新月 ～ 1 满月）。</summary>
	public static float Light(int phase) => (float)((1 - Math.Cos(Math.Clamp(phase, 0, 7) / 8.0 * 2 * Math.PI)) / 2);

	/// <summary>这一季哪两晚有流星雨（每个存档不一样，避开节日前后不管）。</summary>
	public static IReadOnlyList<int> MeteorNights(ulong gameId, string? season, int year)
	{
		int s = Math.Max(0, Array.FindIndex(SeasonKeys, k => k.Equals((season ?? "").Trim(), StringComparison.OrdinalIgnoreCase)));
		List<int> days = new List<int>();
		for (int salt = 0; days.Count < MeteorNightsPerSeason && salt < 20; salt++)
		{
			int d = Horoscope.Roll(gameId, year * 4 + s, "meteor", salt, 26) + 2;
			if (!days.Any(x => Math.Abs(x - d) < 5))
			{
				days.Add(d);
			}
		}
		days.Sort();
		return days;
	}

	/// <summary>今晚：下雨下雪看不到星星；晴天按月亮亮度和流星算能看到多少颗。</summary>
	public static NightSky Tonight(ulong gameId, string? season, int day, int year, bool raining, bool snowing, bool forcedMeteors, bool summitOpen)
	{
		int phase = Phase(day);
		bool clear = !raining && !snowing;
		bool meteors = clear && (forcedMeteors || MeteorNights(gameId, season, year).Contains(day));
		int stars = clear ? (int)Math.Round(120 * (1 - Light(phase) * 0.6)) : 0;
		List<string> spots = new List<string>();
		if (clear)
		{
			if (summitOpen)
			{
				spots.Add(Tr.Get("sky.spot.summit", "山顶"));
			}
			spots.Add(Tr.Get("sky.spot.beach", "海滩的码头"));
			spots.Add(Tr.Get("sky.spot.railroad", "铁路边"));
			spots.Add(Tr.Get("sky.spot.lake", "山上的湖边"));
		}
		string summary = !clear
			? (snowing ? Tr.Get("sky.sum.snow", "下着雪，今晚看不到星星。") : Tr.Get("sky.sum.rain", "阴雨，今晚看不到星星。"))
			: meteors ? Tr.Get("sky.sum.meteor", "今晚有流星雨，{{moon}}。", ("moon", MoonName(phase)))
			: phase == 4 ? Tr.Get("sky.sum.full", "满月，亮得能看清脚下的路。")
			: phase == 0 ? Tr.Get("sky.sum.new", "新月，星星最多的一晚。")
			: Tr.Get("sky.sum.clear", "晴朗，{{moon}}。", ("moon", MoonName(phase)));
		return new NightSky(phase, MoonName(phase), clear, meteors, stars, spots, summary);
	}

	/// <summary>给村民的一句（只说值得一提的天象）。</summary>
	public static string? PromptLine(NightSky sky) => sky.Meteors ? "今晚天晴，有流星雨，适合找个开阔的地方看星星。"
		: sky.Clear && sky.Phase == 4 ? "今晚是满月。"
		: null;

	/// <summary>下一场流星雨在哪天（这一季没有了就是 -1）。</summary>
	public static int NextMeteor(ulong gameId, string? season, int year, int today) => MeteorNights(gameId, season, year).Where(d => d >= today).DefaultIfEmpty(-1).First();
}
