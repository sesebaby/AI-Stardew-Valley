using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;

namespace AISV.Core.Apps;

/// <summary>一句有出处的话：谁说的、在哪说的、哪天。</summary>
public sealed record Quote(string Text, string Speaker, string Source, int Day);

public sealed record QuizQuestion(Quote Quote, IReadOnlyList<string> Choices)
{
	public string Answer => Quote.Speaker;
}

public static class WhoSaidIt
{
	public const int Rounds = 5;
	public const int Choices = 4;
	public const int MinChars = 6;
	public const int MaxChars = 60;

	public const string Gossip = "gossip";
	public const string Chat = "chat";
	public const string Text = "text";
	public const string Talk = "talk";

	/// <summary>去掉太短、太长、重复、带说话人名字（一看就知道是谁）的句子。</summary>
	public static List<Quote> Clean(IEnumerable<Quote> raw, Func<string, string> display)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		List<Quote> list = new List<Quote>();
		foreach (Quote q in raw)
		{
			string text = (q.Text ?? "").Trim().Trim('"', '“', '”', '「', '」');
			if (text.Length < MinChars || text.Length > MaxChars || string.IsNullOrWhiteSpace(q.Speaker) || !seen.Add(text))
			{
				continue;
			}
			string name = display(q.Speaker);
			if (name.Length > 0 && text.Contains(name, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			list.Add(q with { Text = text });
		}
		return list;
	}

	/// <summary>出一轮题：最新的一句八卦打头，其余按新旧加一点随机；每题四个人里选，干扰项是同样说过话的人。</summary>
	public static List<QuizQuestion> Round(IReadOnlyList<Quote> quotes, IReadOnlyList<string> villagers, Random rng)
	{
		List<Quote> pool = quotes.OrderByDescending(q => q.Day + rng.NextDouble() * 6).ToList();
		Quote? lead = quotes.Where(q => q.Source == Gossip).OrderByDescending(q => q.Day).FirstOrDefault();
		if (lead != null)
		{
			pool.Remove(lead);
			pool.Insert(0, lead);
		}
		List<string> speakers = quotes.Select(q => q.Speaker).Concat(villagers).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		List<QuizQuestion> round = new List<QuizQuestion>();
		HashSet<string> asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Quote q in pool)
		{
			if (round.Count >= Rounds)
			{
				break;
			}
			if (round.Count > 0 && asked.Contains(q.Speaker) && pool.Count > Rounds * 2)
			{
				continue;
			}
			List<string> others = speakers.Where(s => !string.Equals(s, q.Speaker, StringComparison.OrdinalIgnoreCase)).OrderBy(_ => rng.Next()).Take(Choices - 1).ToList();
			if (others.Count < Choices - 1)
			{
				continue;
			}
			others.Insert(rng.Next(others.Count + 1), q.Speaker);
			round.Add(new QuizQuestion(q, others));
			asked.Add(q.Speaker);
		}
		return round;
	}

	public static string SourceLabel(string source) => source switch
	{
		Gossip => Tr.Get("who.src.gossip", "镇上传的话"),
		Chat => Tr.Get("who.src.chat", "居民群"),
		Text => Tr.Get("who.src.text", "信使"),
		_ => Tr.Get("who.src.talk", "当面说的")
	};

	public static string Verdict(int right, int total) => total == 0 ? "" : ((double)right / total) switch
	{
		>= 1 => Tr.Get("who.verdict.all", "全对！镇上的话你一句没漏。"),
		>= 0.6 => Tr.Get("who.verdict.good", "耳朵挺灵，大部分人的口气你都认得。"),
		>= 0.3 => Tr.Get("who.verdict.mid", "还行，再多听听大家说话。"),
		_ => Tr.Get("who.verdict.low", "你平时是不是都在地里？")
	};
}
