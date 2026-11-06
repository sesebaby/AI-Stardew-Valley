using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Apps;

public sealed record TalkFact(string Npc, int Year, int Points);

public sealed record GiftFact(string Npc, string Item, string Taste, int Year);

public sealed record GossipFact(string Text, string Origin, int Aware, int Year, int TotalDay);

public sealed record ArcFact(string Title, IReadOnlyList<string> Npcs, int Year, int TotalDay);

public sealed record MilestoneFact(string Npc, string Type, int Year, int TotalDay, string Description = "");

public sealed record LetterFact(string Npc, int Year);

/// <summary>年度回顾要用的全部事实（每种都可以是空的）。</summary>
public sealed record YearFacts(IReadOnlyList<TalkFact> Talks, IReadOnlyList<GiftFact> Gifts, IReadOnlyList<GossipFact> Gossip, IReadOnlyList<ArcFact> Arcs, IReadOnlyList<MilestoneFact> Milestones, IReadOnlyList<LetterFact> Letters)
{
	public static YearFacts Empty { get; } = new YearFacts(Array.Empty<TalkFact>(), Array.Empty<GiftFact>(), Array.Empty<GossipFact>(), Array.Empty<ArcFact>(), Array.Empty<MilestoneFact>(), Array.Empty<LetterFact>());
}

public sealed record Ranked(string Name, int Count);

public sealed record TopGift(string Item, int Count, string Recipient, int RecipientCount);

/// <summary>四项声望，0～100。</summary>
public sealed record Reputation(float Generosity, float Reliability, float Charisma, float Mystery);

public enum YearPage
{
	Intro,
	Talks,
	Gifts,
	Gossip,
	Arcs,
	Milestones,
	Letters,
	Reputation
}

public enum YearTitle
{
	FarmHermit,
	ValleySweetheart,
	GiftShower,
	GenerousSoul,
	TownCharmer,
	GoodCompany,
	PillarOfTheValley,
	FriendlyNeighbor,
	Enigma,
	QuietFarmer
}

public sealed record YearSummary(int Year, int TalkCount, int People, IReadOnlyList<Ranked> TopTalkers, int GiftCount, int LovedGifts, TopGift? TopGift, Ranked? TopRecipient,
	GossipFact? TopGossip, IReadOnlyList<ArcFact> Arcs, IReadOnlyList<MilestoneFact> Milestones, int LetterCount, Ranked? TopSender, Reputation Reputation, YearTitle Title, IReadOnlyList<YearPage> Pages);

/// <summary>年度回顾：按年份汇总聊天、礼物、八卦、故事、里程碑、来信，算声望和称号。</summary>
public static class YearReview
{
	public const int DaysPerYear = 112;

	public const int TopTalkers = 3;

	public const int ListShown = 4;

	public static readonly IReadOnlyList<string> KnownMilestones = new[]
	{
		"first_conversation", "10_conversations", "50_conversations", "100_conversations", "3_hearts", "6_hearts", "8_hearts", "10_hearts", "first_loved_gift", "first_fight", "1_year"
	};

	public static int YearOf(int totalDay) => totalDay > 0 ? (totalDay - 1) / DaysPerYear + 1 : 0;

	/// <summary>有内容的年份加上今年，从早到晚。</summary>
	public static IReadOnlyList<int> Years(YearFacts f, int currentYear)
	{
		SortedSet<int> years = new SortedSet<int> { Math.Max(1, currentYear) };
		IEnumerable<int> all = f.Talks.Select(x => x.Year).Concat(f.Gifts.Select(x => x.Year)).Concat(f.Gossip.Select(x => x.Year)).Concat(f.Arcs.Select(x => x.Year)).Concat(f.Milestones.Select(x => x.Year)).Concat(f.Letters.Select(x => x.Year));
		foreach (int y in all)
		{
			if (y >= 1 && y <= currentYear)
			{
				years.Add(y);
			}
		}
		return years.ToList();
	}

	public static YearSummary Summarize(YearFacts f, int year)
	{
		List<TalkFact> talks = f.Talks.Where(x => x.Year == year && Named(x.Npc)).ToList();
		List<GiftFact> gifts = f.Gifts.Where(x => x.Year == year && Named(x.Npc)).ToList();
		List<Ranked> talkers = Rank(talks.Select(x => x.Npc)).ToList();
		TopGift? topGift = gifts.Where(g => !string.IsNullOrWhiteSpace(g.Item)).GroupBy(g => g.Item.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(g =>
			{
				Ranked to = Rank(g.Select(x => x.Npc)).First();
				return new TopGift(g.First().Item.Trim(), g.Count(), to.Name, to.Count);
			})
			.OrderByDescending(g => g.Count).ThenBy(g => g.Item, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		GossipFact? gossip = f.Gossip.Where(x => x.Year == year && !string.IsNullOrWhiteSpace(x.Text) && x.Aware > 0).OrderByDescending(x => x.Aware).ThenByDescending(x => x.TotalDay).FirstOrDefault();
		List<ArcFact> arcs = f.Arcs.Where(x => x.Year == year && !string.IsNullOrWhiteSpace(x.Title)).OrderByDescending(x => x.TotalDay).ToList();
		List<MilestoneFact> milestones = f.Milestones.Where(x => x.Year == year && Named(x.Npc) && !string.IsNullOrWhiteSpace(x.Type)).OrderByDescending(x => x.TotalDay).ToList();
		List<LetterFact> letters = f.Letters.Where(x => x.Year == year && Named(x.Npc)).ToList();
		Reputation rep = Score(talks, gifts);
		(YearPage Page, bool Filled)[] contents =
		{
			(YearPage.Intro, true),
			(YearPage.Talks, talks.Count > 0),
			(YearPage.Gifts, gifts.Count > 0),
			(YearPage.Gossip, gossip != null),
			(YearPage.Arcs, arcs.Count > 0),
			(YearPage.Milestones, milestones.Count > 0),
			(YearPage.Letters, letters.Count > 0),
			(YearPage.Reputation, true)
		};
		List<YearPage> pages = contents.Where(c => c.Filled).Select(c => c.Page).ToList();
		int loved = gifts.Count(g => string.Equals(g.Taste, "loved", StringComparison.OrdinalIgnoreCase));
		return new YearSummary(year, talks.Count, talkers.Count, talkers.Take(TopTalkers).ToList(), gifts.Count, loved, topGift,
			Rank(gifts.Select(g => g.Npc)).FirstOrDefault(), gossip, arcs, milestones, letters.Count, Rank(letters.Select(l => l.Npc)).FirstOrDefault(), rep, PickTitle(rep, talks.Count, gifts.Count), pages);
	}

	public static Reputation Score(IReadOnlyCollection<TalkFact> talks, IReadOnlyCollection<GiftFact> gifts)
	{
		float generosity = gifts.Count == 0 ? 50f : Math.Clamp((gifts.Average(g => TasteScore(g.Taste)) + 20f) / 40f * 100f, 0f, 100f);
		List<TalkFact> warm = talks.Where(t => t.Points > 0).ToList();
		float charisma = warm.Count == 0 ? 50f : Math.Clamp((float)warm.Average(t => t.Points), 0f, 100f);
		List<IGrouping<string, TalkFact>> byNpc = talks.GroupBy(t => t.Npc, StringComparer.OrdinalIgnoreCase).ToList();
		float reliability = byNpc.Count == 0 ? 50f : Math.Clamp(byNpc.Count(g => g.Count() >= 3) * 100f / byNpc.Count, 0f, 100f);
		float mystery = byNpc.Count == 0 ? 80f : Math.Clamp(Math.Max(10f, 100f - talks.Count * 0.45f), 0f, 100f);
		return new Reputation(generosity, reliability, charisma, mystery);
	}

	public static float TasteScore(string? taste) => (taste ?? "").ToLowerInvariant() switch
	{
		"loved" => 20f,
		"liked" => 10f,
		"disliked" => -10f,
		"hated" => -20f,
		_ => 0f
	};

	public static YearTitle PickTitle(Reputation r, int talkCount, int giftCount)
	{
		if (talkCount == 0 && giftCount == 0)
		{
			return YearTitle.FarmHermit;
		}
		if (Math.Min(r.Generosity, Math.Min(r.Reliability, r.Charisma)) >= 65f)
		{
			return YearTitle.ValleySweetheart;
		}
		(float Score, YearTitle High, YearTitle Low)[] options =
		{
			(giftCount > 0 ? r.Generosity : 0f, YearTitle.GiftShower, YearTitle.GenerousSoul),
			(talkCount > 0 ? r.Charisma : 0f, YearTitle.TownCharmer, YearTitle.GoodCompany),
			(talkCount > 0 ? r.Reliability : 0f, YearTitle.PillarOfTheValley, YearTitle.FriendlyNeighbor),
			(r.Mystery, YearTitle.Enigma, YearTitle.QuietFarmer)
		};
		var best = options[0];
		foreach (var o in options.Skip(1))
		{
			if (o.Score > best.Score)
			{
				best = o;
			}
		}
		return best.Score >= 75f ? best.High : best.Low;
	}

	/// <summary>0～4 档（20、40、60、80 分界）。</summary>
	public static int Tier(float score) => score >= 80f ? 4 : score >= 60f ? 3 : score >= 40f ? 2 : score >= 20f ? 1 : 0;

	public static bool IsKnownMilestone(string? type) => KnownMilestones.Any(k => Same(k, (type ?? "").Trim()));

	/// <summary>默认打开最近一个有看头的年份（除了开头和声望还有别的页）。</summary>
	public static int DefaultYear(YearFacts f, int currentYear)
	{
		IReadOnlyList<int> years = Years(f, currentYear);
		for (int i = years.Count - 1; i >= 0; i--)
		{
			if (Summarize(f, years[i]).Pages.Count > 2)
			{
				return years[i];
			}
		}
		return years[^1];
	}

	private static IEnumerable<Ranked> Rank(IEnumerable<string> names) =>
		names.Where(Named).GroupBy(n => n.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => new Ranked(g.First().Trim(), g.Count()))
			.OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

	private static bool Named(string? name) => !string.IsNullOrWhiteSpace(name);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
