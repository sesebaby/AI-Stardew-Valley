using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Web;

namespace AISV.Core.Apps;

public enum Bond
{
	Couple,
	Crush,
	BestFriends,
	OddFriends,
	Friends,
	Acquaintances,
	Awkward,
	Tense,
	Rivals,
	Strangers
}

public enum MemoryTone
{
	Warm,
	Playful,
	Tense,
	Neutral
}

public sealed record PairMemory(int TotalDay, string Tone, string Summary, int Importance);

/// <summary>两位村民之间的一条关系（A、B 按名字排好）。</summary>
public sealed class Tie
{
	public string A { get; init; } = "";

	public string B { get; init; } = "";

	public float Affinity { get; init; }

	public float Trust { get; init; }

	public float Tension { get; init; }

	public float Playfulness { get; init; }

	public int Encounters { get; init; }

	public int LastTotalDay { get; init; }

	public Bond Bond { get; init; }

	public IReadOnlyList<PairMemory> Memories { get; init; } = Array.Empty<PairMemory>();

	public float Warmth => VillageMap.Warmth(Affinity, Trust, Playfulness);

	public float Intensity => Math.Max(Warmth, Tension);

	public bool Involves(string npc) => Same(A, npc) || Same(B, npc);

	public string Other(string npc) => Same(A, npc) ? B : A;

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>哪些人是夫妻、哪些人之间能显示恋情，成人剧情开没开。</summary>
public sealed record MapPolicy(Func<string, string, bool> IsCouple, Func<string, string, bool> RomanceAllowed, bool AdultDrama)
{
	public static MapPolicy Safe { get; } = new MapPolicy((_, _) => false, (_, _) => false, false);
}

public sealed class MapModel
{
	public IReadOnlyList<Tie> Ties { get; init; } = Array.Empty<Tie>();

	public IReadOnlyList<string> People { get; init; } = Array.Empty<string>();

	public IReadOnlyList<Tie> TopBonds { get; init; } = Array.Empty<Tie>();

	public IReadOnlyList<Tie> TopFeuds { get; init; } = Array.Empty<Tie>();

	public bool IsEmpty => Ties.Count == 0;
}

/// <summary>村庄关系网：把村民之间的社交边分成情侣、挚友、不和等，挑出最亲和最僵的几对。</summary>
public static class VillageMap
{
	public const int Highlights = 3;

	public const int MemoriesShown = 3;

	private static readonly string[] RomanceStems = { "flirt", "crush", "roman", "attract", "dat", "love", "smitten", "blush", "sweetheart", "court", "kiss", "swoon" };

	public static Bond Classify(NpcSocialEdge e, MapPolicy policy)
	{
		if (BondLines.Stranger(e))
		{
			return Bond.Strangers;
		}
		string a = (e.First ?? "").Trim(), b = (e.Second ?? "").Trim();
		bool grownUps = !SocialWeb.IsChild(a) && !SocialWeb.IsChild(b);
		bool married = grownUps && policy.IsCouple(a, b);
		if (BondLines.Sworn(e))
		{
			return !married || policy.AdultDrama ? Bond.Rivals : Bond.Tense;
		}
		if (BondLines.Strained(e))
		{
			return Bond.Tense;
		}
		if (married)
		{
			return Bond.Couple;
		}
		if (BondLines.Uneasy(e))
		{
			return Bond.Awkward;
		}
		if (grownUps && e.Liking >= BondLines.CrushAffinity && HasRomance(e) && policy.RomanceAllowed(a, b))
		{
			return Bond.Crush;
		}
		if (BondLines.OddlyFond(e))
		{
			return Bond.OddFriends;
		}
		if (BondLines.Devoted(e))
		{
			return Bond.BestFriends;
		}
		return BondLines.Fond(e) ? Bond.Friends : Bond.Acquaintances;
	}

	/// <summary>恋情能不能显示：开了村民恋情、这两人允许；有一方已婚时还要成人剧情和出轨剧情都开。</summary>
	public static bool CanShowRomance(bool romancesOn, bool pairAllowed, bool eitherMarried, bool adultDrama, bool infidelity) =>
		romancesOn && pairAllowed && (!eitherMarried || adultDrama && infidelity);

	public static bool IsWarm(Bond b) => b <= Bond.Friends;

	public static bool IsFeud(Bond b) => b is Bond.Awkward or Bond.Tense or Bond.Rivals;

	public static bool IsRomantic(Bond b) => b is Bond.Couple or Bond.Crush;

	public static float Warmth(float affinity, float trust, float playfulness) => Math.Clamp(Clamp01(affinity) * 0.5f + Clamp01(trust) * 0.3f + Clamp01(playfulness) * 0.2f, 0f, 1f);

	public static MapModel Build(IEnumerable<NpcSocialEdge>? edges, int today, MapPolicy policy)
	{
		Dictionary<string, NpcSocialEdge> unique = new Dictionary<string, NpcSocialEdge>(StringComparer.OrdinalIgnoreCase);
		foreach (NpcSocialEdge e in edges ?? Enumerable.Empty<NpcSocialEdge>())
		{
			if (e == null || string.IsNullOrWhiteSpace(e.First) || string.IsNullOrWhiteSpace(e.Second) || string.Equals(e.First.Trim(), e.Second.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string key = SocialWeb.PairKey(e.First, e.Second);
			if (!unique.TryGetValue(key, out NpcSocialEdge? had) || e.Encounters > had.Encounters)
			{
				unique[key] = e;
			}
		}
		List<Tie> ties = unique.Values.Select(e => ToTie(e, today, policy)).Where(t => t.Bond != Bond.Strangers || t.Memories.Count > 0)
			.OrderBy(t => t.A, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.B, StringComparer.OrdinalIgnoreCase).ToList();
		return new MapModel
		{
			Ties = ties,
			People = ties.SelectMany(t => new[] { t.A, t.B }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
			TopBonds = ties.Where(t => IsWarm(t.Bond)).OrderByDescending(t => IsRomantic(t.Bond)).ThenByDescending(t => t.Warmth).ThenByDescending(t => t.Encounters).Take(Highlights).ToList(),
			TopFeuds = ties.Where(t => IsFeud(t.Bond)).OrderByDescending(t => t.Tension).ThenByDescending(t => t.Encounters).Take(Highlights).ToList()
		};
	}

	/// <summary>一个人的全部关系，最浓的在前。</summary>
	public static IReadOnlyList<Tie> TiesOf(MapModel m, string npc) =>
		m.Ties.Where(t => t.Involves(npc)).OrderByDescending(t => t.Intensity).ThenByDescending(t => t.LastTotalDay).ThenBy(t => t.Other(npc), StringComparer.OrdinalIgnoreCase).ToList();

	public static IReadOnlyList<PairMemory> RecentMemories(IEnumerable<NpcSocialMemory>? memories, int today, int max = MemoriesShown) =>
		(memories ?? Enumerable.Empty<NpcSocialMemory>())
			.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Text) && (m.FadesAfterDay <= 0 || m.FadesAfterDay >= today))
			.OrderByDescending(m => m.OnDay).ThenByDescending(m => m.Weight)
			.DistinctBy(m => m.Text.Trim(), StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, max))
			.Select(m => new PairMemory(m.OnDay, m.Tone ?? "", m.Text.Trim(), m.Weight)).ToList();

	public static MemoryTone ToneOf(string? tone) => (tone ?? "").Trim().ToLowerInvariant() switch
	{
		"sweet" or "kind" or "warm" or "romantic" or "tender" => MemoryTone.Warm,
		"funny" or "silly" or "playful" or "strange" => MemoryTone.Playful,
		"angry" or "tense" or "conflict" or "awkward" or "rivalry" => MemoryTone.Tense,
		_ => MemoryTone.Neutral
	};

	public static bool IsRomanceTag(string? tag) =>
		!string.IsNullOrWhiteSpace(tag) && tag.Split(new[] { '_', '-', ' ', ':', '/' }, StringSplitOptions.RemoveEmptyEntries).Any(word => RomanceStems.Any(stem => word.StartsWith(stem, StringComparison.OrdinalIgnoreCase)));

	private static bool HasRomance(NpcSocialEdge e) => (e.Tags ?? new List<string>()).Any(IsRomanceTag) || (e.Shared ?? new List<NpcSocialMemory>()).Any(m => m?.Tags?.Any(IsRomanceTag) == true);

	private static Tie ToTie(NpcSocialEdge e, int today, MapPolicy policy)
	{
		bool swap = string.Compare(e.First.Trim(), e.Second.Trim(), StringComparison.OrdinalIgnoreCase) > 0;
		IReadOnlyList<PairMemory> memories = RecentMemories(e.Shared, today);
		return new Tie
		{
			A = (swap ? e.Second : e.First).Trim(),
			B = (swap ? e.First : e.Second).Trim(),
			Affinity = Clamp01(e.Liking),
			Trust = Clamp01(e.Trust),
			Tension = Clamp01(e.Friction),
			Playfulness = Clamp01(e.Banter),
			Encounters = Math.Max(0, e.Encounters),
			LastTotalDay = Math.Max(e.LastMetDay, memories.Count > 0 ? memories.Max(m => m.TotalDay) : 0),
			Bond = Classify(e, policy),
			Memories = memories
		};
	}

	private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);
}
