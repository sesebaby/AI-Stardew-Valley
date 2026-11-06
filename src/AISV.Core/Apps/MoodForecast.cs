using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Apps;

/// <summary>心情预报里的天气。</summary>
public enum MoodSky
{
	Rainbow,
	Sunny,
	PartlyCloudy,
	Cloudy,
	Foggy,
	Rainy,
	Stormy
}

public enum MoodCause
{
	LikesWeather,
	DislikesWeather,
	LovesSeason,
	DislikesSeason,
	LikesDay,
	DislikesDay,
	Birthday,
	GoodEvent,
	BadEvent,
	NormalDay,
	Other
}

/// <summary>心情原因里的一条（<see cref="Text"/> 是原话）。</summary>
public readonly record struct MoodCauseItem(MoodCause Kind, string Text);

public enum MoodAdvice
{
	BirthdayGift,
	GoodDayToTalk,
	ChatOrGift,
	GentleTalk,
	LovedGift,
	TreadLightly,
	WeatherWillPass,
	TomorrowIsBetter
}

/// <summary>把每日心情换成天气：分数、心情、生日决定天气，原因决定给玩家的建议。</summary>
public static class MoodForecast
{
	public static MoodSky Sky(string? baseMood, int score, bool birthday)
	{
		if (birthday && score >= 0 || score >= 60)
		{
			return MoodSky.Rainbow;
		}
		return (baseMood ?? "").Trim().ToLowerInvariant() switch
		{
			"excited" => MoodSky.Sunny,
			"happy" => MoodSky.PartlyCloudy,
			"neutral" => MoodSky.Cloudy,
			"anxious" => MoodSky.Foggy,
			"irritated" => MoodSky.Stormy,
			"melancholic" => MoodSky.Rainy,
			_ => SkyForScore(score)
		};
	}

	public static MoodSky SkyForScore(int score)
	{
		if (score >= 60)
		{
			return MoodSky.Rainbow;
		}
		if (score >= 30)
		{
			return MoodSky.Sunny;
		}
		if (score >= 10)
		{
			return MoodSky.PartlyCloudy;
		}
		if (score > -10)
		{
			return MoodSky.Cloudy;
		}
		return score > -30 ? MoodSky.Stormy : MoodSky.Rainy;
	}

	/// <summary>全村的天气：平均分，最高到晴（彩虹留给个人）。</summary>
	public static MoodSky Village(IEnumerable<int> scores)
	{
		List<int> list = scores.ToList();
		if (list.Count == 0)
		{
			return MoodSky.Cloudy;
		}
		int avg = (int)Math.Round(list.Average(), MidpointRounding.AwayFromZero);
		return avg >= 60 ? MoodSky.Sunny : SkyForScore(avg);
	}

	public static bool IsBright(MoodSky sky) => sky is MoodSky.Rainbow or MoodSky.Sunny or MoodSky.PartlyCloudy;

	/// <summary>拆开心情原因（用"；"连起来的几句），去掉重复（好事、不痛快的事、生日各算一条）；有别的原因时去掉"平平常常"。</summary>
	public static IReadOnlyList<MoodCauseItem> Causes(string? reason)
	{
		List<MoodCauseItem> list = new List<MoodCauseItem>();
		if (string.IsNullOrWhiteSpace(reason))
		{
			return list;
		}
		foreach (string raw in reason.Split('；', ';'))
		{
			string part = raw.Trim().TrimEnd('。');
			if (part.Length == 0)
			{
				continue;
			}
			MoodCause kind = KindOf(part);
			MoodCauseItem item = new MoodCauseItem(kind, part);
			bool merged = kind is MoodCause.GoodEvent or MoodCause.BadEvent or MoodCause.Birthday or MoodCause.NormalDay;
			if (merged ? list.All(c => c.Kind != kind) : !list.Contains(item))
			{
				list.Add(item);
			}
		}
		if (list.Count > 1)
		{
			list.RemoveAll(c => c.Kind == MoodCause.NormalDay);
		}
		return list;
	}

	private static MoodCause KindOf(string part)
	{
		if (part.Contains("生日"))
		{
			return MoodCause.Birthday;
		}
		if (part.StartsWith("爱这种") && part.EndsWith("天"))
		{
			return MoodCause.LikesWeather;
		}
		if (part.StartsWith("受不了") && part.EndsWith("天"))
		{
			return MoodCause.DislikesWeather;
		}
		if (part.StartsWith("最喜欢"))
		{
			return MoodCause.LovesSeason;
		}
		if (part.StartsWith("不太喜欢"))
		{
			return MoodCause.DislikesSeason;
		}
		if (part.EndsWith("让人有盼头"))
		{
			return MoodCause.LikesDay;
		}
		if (part.EndsWith("总提不起劲"))
		{
			return MoodCause.DislikesDay;
		}
		if (part.Contains("好事"))
		{
			return MoodCause.GoodEvent;
		}
		if (part.Contains("不痛快"))
		{
			return MoodCause.BadEvent;
		}
		return part.Contains("平平常常") ? MoodCause.NormalDay : MoodCause.Other;
	}

	public static IReadOnlyList<MoodAdvice> Advice(MoodSky sky, IReadOnlyList<MoodCauseItem> causes)
	{
		List<MoodAdvice> list = new List<MoodAdvice>();
		if (causes.Any(c => c.Kind == MoodCause.Birthday))
		{
			list.Add(MoodAdvice.BirthdayGift);
		}
		if (IsBright(sky))
		{
			if (list.Count == 0)
			{
				list.Add(MoodAdvice.GoodDayToTalk);
			}
			return list;
		}
		list.Add(sky switch
		{
			MoodSky.Cloudy => MoodAdvice.ChatOrGift,
			MoodSky.Foggy => MoodAdvice.GentleTalk,
			MoodSky.Rainy => MoodAdvice.LovedGift,
			_ => MoodAdvice.TreadLightly
		});
		if (causes.Any(c => c.Kind == MoodCause.DislikesWeather))
		{
			list.Add(MoodAdvice.WeatherWillPass);
		}
		if (causes.Any(c => c.Kind == MoodCause.DislikesDay))
		{
			list.Add(MoodAdvice.TomorrowIsBetter);
		}
		return list;
	}

	/// <summary>-100～100 换成 0～1（画分数条用）。</summary>
	public static float Fraction(int score) => (Math.Clamp(score, -100, 100) + 100) / 200f;
}
