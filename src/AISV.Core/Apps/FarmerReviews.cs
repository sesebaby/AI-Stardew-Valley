using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Apps;

public enum Bound
{
	None,
	Dating,
	Engaged,
	Married,
	Divorced
}

/// <summary>写一条评价要用的、这位村民和玩家之间的情况。</summary>
public sealed record ReviewInput
{
	public string Npc { get; init; } = "";

	public int Hearts { get; init; }

	public Bound Bound { get; init; }

	public bool HasProfile { get; init; }

	public float Trust { get; init; } = 0.5f;

	public float Warmth { get; init; }

	public int Loved { get; init; }

	public int Liked { get; init; }

	public int Neutral { get; init; }

	public int Disliked { get; init; }

	public int Hated { get; init; }

	public int BirthdayGifts { get; init; }

	public string? BestGift { get; init; }

	public string? WorstGift { get; init; }

	public int Talks { get; init; }

	public IReadOnlyList<int> PositivePoints { get; init; } = Array.Empty<int>();

	public int BrokenPromises { get; init; }

	public int HurtfulTalks { get; init; }

	public string? Memory { get; init; }

	public int Gifts => Loved + Liked + Neutral + Disliked + Hated;

	public bool IsStranger => Talks == 0 && Gifts == 0 && Hearts == 0 && !HasProfile;
}

/// <summary>四项声望，没数据时为空。</summary>
public sealed record ReviewAxes(float? Generosity, float? Reliability, float? Charisma, float? Mystery);

/// <summary>一句评价：i18n 键（不带前缀）加上要填的值。</summary>
public sealed record ReviewLine(string Key, IReadOnlyDictionary<string, string> Values)
{
	public ReviewLine(string key) : this(key, new Dictionary<string, string>())
	{
	}

	public ReviewLine(string key, string name, string value) : this(key, new Dictionary<string, string> { [name] = value })
	{
	}
}

public sealed record Review(string Npc, int Stars, ReviewLine Title, IReadOnlyList<ReviewLine> Body, ReviewLine? Memory, int Helpful, ReviewAxes Axes);

/// <summary>评价：每位认识的村民给农夫打星、写几句（按好感、信任、送礼、聊天、违约、伤人话算）。</summary>
public static class FarmerReviews
{
	public const int MaxRemarks = 3;

	public const int MaxMemoryLength = 140;

	public static ReviewAxes Axes(ReviewInput i)
	{
		float? generosity = null, reliability = null, charisma = null;
		if (i.Gifts > 0)
		{
			float taste = (2f * i.Loved + i.Liked - i.Disliked - 2f * i.Hated) / i.Gifts;
			generosity = Math.Clamp(50f + taste * 25f, 0f, 100f);
		}
		List<int> warm = i.PositivePoints.Where(p => p > 0).ToList();
		if (warm.Count > 0)
		{
			charisma = Math.Clamp((float)warm.Average(), 0f, 100f);
		}
		if (i.Talks > 0 || i.BrokenPromises > 0)
		{
			reliability = Math.Clamp(Math.Min(95f, 35f + i.Talks * 2.5f) - i.BrokenPromises * 20f, 0f, 100f);
		}
		float mystery = Math.Clamp(100f - i.Talks * 1.5f, 8f, 100f);
		return new ReviewAxes(generosity, reliability, charisma, mystery);
	}

	public static int Stars(ReviewInput i) => Stars(i, Axes(i));

	private static int Stars(ReviewInput i, ReviewAxes axes)
	{
		if (i.IsStranger)
		{
			return 3;
		}
		float weighted = 0f, weights = 0f;
		void Add(float v, float w)
		{
			weighted += Math.Clamp(v, 0f, 1f) * w;
			weights += w;
		}
		Add(Math.Min(i.Hearts, 10) / 10f, 0.4f);
		if (i.HasProfile)
		{
			Add((i.Warmth + 1f) / 2f, 0.2f);
			Add(i.Trust, 0.1f);
		}
		if (axes.Generosity is float g)
		{
			Add(g / 100f, 0.15f);
		}
		if (axes.Charisma is float c)
		{
			Add(c / 100f, 0.15f);
		}
		float value = weighted / weights - i.BrokenPromises * 0.1f - i.HurtfulTalks * 0.08f;
		int stars = 1 + (int)Math.Round(Math.Clamp(value, 0f, 1f) * 4f, MidpointRounding.AwayFromZero);
		if (i.Bound == Bound.Divorced)
		{
			stars = Math.Min(stars, 2);
		}
		return Math.Clamp(stars, 1, 5);
	}

	public static Review Build(ReviewInput i)
	{
		ReviewAxes axes = Axes(i);
		int stars = Stars(i, axes);
		int helpful = HelpfulCount(i.Npc, stars);
		if (i.IsStranger)
		{
			return new Review(i.Npc, stars, new ReviewLine("stranger.title"), new[] { new ReviewLine("stranger.body") }, null, helpful, axes);
		}
		ReviewLine title = new ReviewLine($"title.{stars}{(StableHash(i.Npc) % 2 == 0 ? "a" : "b")}");
		List<ReviewLine> body = new List<ReviewLine> { new ReviewLine($"open.{stars}") };
		body.AddRange(Remarks(i, axes).Take(MaxRemarks));
		ReviewLine? memory = string.IsNullOrWhiteSpace(i.Memory) ? null : new ReviewLine("memory", "memory", ShortenMemory(i.Memory));
		return new Review(i.Npc, stars, title, body, memory, helpful, axes);
	}

	/// <summary>评语按话题分组：每组取第一条成立的，组和组之间的先后就是评语的先后。</summary>
	public static IEnumerable<ReviewLine> Remarks(ReviewInput i, ReviewAxes axes)
	{
		bool mean = i.Hated + i.Disliked > 0 && axes.Generosity < 50f && !string.IsNullOrWhiteSpace(i.WorstGift);
		bool warm = i.Loved > 0 && !string.IsNullOrWhiteSpace(i.BestGift);
		(bool Holds, Func<ReviewLine> Line)[][] topics =
		{
			new (bool, Func<ReviewLine>)[]
			{
				(i.Bound == Bound.Married, () => new ReviewLine("rel.married")),
				(i.Bound == Bound.Engaged, () => new ReviewLine("rel.engaged")),
				(i.Bound == Bound.Dating, () => new ReviewLine("rel.dating")),
				(i.Bound == Bound.Divorced, () => new ReviewLine("rel.divorced"))
			},
			new (bool, Func<ReviewLine>)[] { (i.BrokenPromises > 0, () => new ReviewLine("promise.broken")) },
			new (bool, Func<ReviewLine>)[] { (i.HurtfulTalks > 0, () => new ReviewLine("talk.hurt")) },
			new (bool, Func<ReviewLine>)[]
			{
				(mean, () => new ReviewLine("gift.hated", "item", i.WorstGift!)),
				(i.Gifts >= 10 && axes.Generosity >= 60f, () => new ReviewLine("gift.many")),
				(warm, () => new ReviewLine("gift.loved", "item", i.BestGift!)),
				(i.Gifts == 0 && i.Hearts >= 2, () => new ReviewLine("gift.none"))
			},
			new (bool, Func<ReviewLine>)[] { (i.BirthdayGifts > 0 && i.Hated == 0, () => new ReviewLine("gift.birthday")) },
			new (bool, Func<ReviewLine>)[]
			{
				(axes.Charisma >= 60f, () => new ReviewLine("talk.great")),
				(axes.Charisma < 25f, () => new ReviewLine("talk.awkward"))
			},
			new (bool, Func<ReviewLine>)[]
			{
				(i.Talks >= 15, () => new ReviewLine("talk.often", "talks", i.Talks.ToString())),
				(i.Talks <= 2 && axes.Mystery >= 80f, () => new ReviewLine("talk.rare"))
			},
			new (bool, Func<ReviewLine>)[]
			{
				(i.HasProfile && i.Trust >= 0.8f, () => new ReviewLine("trust.high")),
				(i.HasProfile && i.Trust < 0.3f, () => new ReviewLine("trust.low"))
			}
		};
		foreach ((bool Holds, Func<ReviewLine> Line)[] topic in topics)
		{
			foreach ((bool holds, Func<ReviewLine> line) in topic)
			{
				if (holds)
				{
					yield return line();
					break;
				}
			}
		}
	}

	/// <summary>"几位村民觉得有用"：按名字和星数固定的 0～27。</summary>
	public static int HelpfulCount(string npc, int stars) => (int)(StableHash(npc + ":" + stars) % 28);

	public static float Average(IEnumerable<int> stars)
	{
		List<int> list = stars.ToList();
		return list.Count == 0 ? 0f : (float)list.Average();
	}

	/// <summary>一星到五星各几条。</summary>
	public static int[] Distribution(IEnumerable<int> stars)
	{
		int[] d = new int[5];
		foreach (int s in stars)
		{
			d[Math.Clamp(s, 1, 5) - 1]++;
		}
		return d;
	}

	/// <summary>五颗星各填几半（0、1、2）。</summary>
	public static int[] StarFill(float average)
	{
		int halves = (int)Math.Round(Math.Clamp(average, 0f, 5f) * 2f, MidpointRounding.AwayFromZero);
		int[] fill = new int[5];
		for (int k = 0; k < 5; k++)
		{
			fill[k] = Math.Clamp(halves - k * 2, 0, 2);
		}
		return fill;
	}

	public static string ShortenMemory(string memory)
	{
		string text = string.Join(" ", memory.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('"', '“', '”', ' ');
		if (text.Length <= MaxMemoryLength)
		{
			return text;
		}
		int cut = text.LastIndexOf(' ', MaxMemoryLength);
		if (cut < MaxMemoryLength / 2)
		{
			cut = MaxMemoryLength;
		}
		return text.Substring(0, cut).TrimEnd(',', ';', ':', ' ', '，', '；', '：') + "……";
	}

	public static uint StableHash(string text)
	{
		uint h = 2166136261u;
		foreach (char c in (text ?? "").ToLowerInvariant())
		{
			h ^= c;
			h *= 16777619;
		}
		return h;
	}
}
