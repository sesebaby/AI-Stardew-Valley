using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Gossip;

namespace AISV.Core.Apps;

/// <summary>这个人是怎么知道的。</summary>
public enum HeardHow
{
	Origin,
	Witness,
	Mentioned,
	Close,
	Spread,
	Unclear
}

/// <summary>传到这个人那里时还剩几分真。</summary>
public enum Fidelity
{
	Faithful,
	Stretched,
	Distorted
}

/// <summary>一条八卦的已知情况：源头、亲历者、内容、谁知道、最后是谁说的、哪天开始传、最后传到哪天。</summary>
public sealed record GossipStory(string Origin, IReadOnlyList<string> Sources, string Fact, IReadOnlyList<string> Aware, string HeardFrom, int CreatedDay, int LastSpreadDay, bool Uncertain);

/// <summary>村子的情况：所有村民、亲友关系、谁不传八卦、谁传得快；<see cref="Display"/> 用来认出八卦里写的是游戏里的名字。</summary>
public sealed record GossipWorld(IReadOnlyList<string> AllNpcs, IReadOnlyList<(string A, string B)> Pairs, Func<string, bool> IsIsolated, IReadOnlyCollection<string> FastSpreaders, Func<string, string>? Display = null)
{
	public bool Mentions(string fact, string npc)
	{
		string n = npc.Trim();
		if (fact.Contains(n, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string shown = Display?.Invoke(n) ?? "";
		return shown.Length > 0 && fact.Contains(shown, StringComparison.OrdinalIgnoreCase);
	}
}

public sealed class TrailNode
{
	public TrailNode(string npc, TrailNode? teller, HeardHow how, int step)
	{
		Npc = npc;
		Teller = teller;
		How = how;
		Step = step;
		Depth = teller == null ? 0 : teller.Depth + 1;
	}

	public string Npc { get; }

	public TrailNode? Teller { get; }

	public HeardHow How { get; }

	/// <summary>第几天传到的（0 是当天）。</summary>
	public int Step { get; }

	public int Depth { get; }

	/// <summary>0～1：和事实差了多少。</summary>
	public float Drift { get; internal set; }

	public Fidelity Fidelity => GossipTrail.FidelityOf(Drift);

	public List<TrailNode> Children { get; } = new List<TrailNode>();

	public IReadOnlyList<TrailNode> PathFromRoot()
	{
		List<TrailNode> path = new List<TrailNode>();
		for (TrailNode? n = this; n != null; n = n.Teller)
		{
			path.Add(n);
		}
		path.Reverse();
		return path;
	}
}

public sealed class Trail
{
	public Trail(TrailNode root, IReadOnlyList<TrailNode> rows, int steps, bool approximate)
	{
		Root = root;
		Rows = rows;
		Steps = steps;
		Approximate = approximate;
	}

	public TrailNode Root { get; }

	/// <summary>先根后子、同层按天数和名字排好的全部节点。</summary>
	public IReadOnlyList<TrailNode> Rows { get; }

	public int Steps { get; }

	/// <summary>有一部分是推测出来的。</summary>
	public bool Approximate { get; }

	public int KnownBy => Rows.Count(r => r.Npc.Length > 0);

	public float MaxDrift => Rows.Count == 0 ? 0f : Rows.Max(r => r.Drift);

	public TrailNode? Find(string npc) => Rows.FirstOrDefault(r => r.Npc.Length > 0 && string.Equals(r.Npc, npc, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 八卦树：村子只记得"谁知道"，不记得"谁告诉谁"，所以按传播规则重新推一遍：
/// 亲历者、被提到的人、亲历者的亲友最先知道；以后每天由一个知情人（传得快的人优先）告诉一两个人，亲友优先。
/// 每转述一次就多走样一点，走样快慢跟着社交剧情强度。
/// </summary>
public static class GossipTrail
{
	public static readonly IReadOnlyCollection<string> DefaultFastSpreaders = new[] { "Caroline", "Pierre", "Haley" };

	private static readonly string[] FallbackSeeds = { "Lewis", "Robin", "Caroline" };

	private const int MaxSteps = 400;

	public const float StretchedFrom = 0.35f;

	public const float DistortedFrom = 0.65f;

	private const float FastDrift = 1.35f;

	public static Fidelity FidelityOf(float drift) => drift >= DistortedFrom ? Fidelity.Distorted : drift >= StretchedFrom ? Fidelity.Stretched : Fidelity.Faithful;

	/// <summary>第一次转述走样多少、以后每天再走样多少。</summary>
	public static (float First, float PerDay) DriftFor(string? intensity) => (intensity ?? "").Trim().ToLowerInvariant() switch
	{
		"cozy" => (0.07f, 0.015f),
		"dramatic" => (0.24f, 0.055f),
		_ => (0.12f, 0.035f)
	};

	public static Trail Build(GossipStory story, GossipWorld world, string? intensity)
	{
		StringComparer cmp = StringComparer.OrdinalIgnoreCase;
		HashSet<string> known = new HashSet<string>(Clean(story.Aware), cmp);
		List<string> sources = Clean(story.Sources).ToList();
		string origin = (story.Origin ?? "").Trim();
		if (origin.Length == 0 && sources.Count > 0)
		{
			origin = sources[0];
		}
		TrailNode root = new TrailNode(origin, null, HeardHow.Origin, 0);
		Dictionary<string, TrailNode> nodes = new Dictionary<string, TrailNode>(cmp);
		if (origin.Length > 0)
		{
			nodes[origin] = root;
		}
		void Attach(string npc, TrailNode teller, HeardHow how, int step)
		{
			TrailNode added = new TrailNode(npc, teller, how, step);
			teller.Children.Add(added);
			nodes[npc] = added;
		}
		foreach ((string npc, HeardHow how) in FirstCircle(sources, story.Fact ?? "", world))
		{
			if (known.Contains(npc) && !nodes.ContainsKey(npc))
			{
				Attach(npc, root, how, 0);
			}
		}
		HashSet<string> fast = new HashSet<string>(world.FastSpreaders, cmp);
		int steps = 0;
		while (steps < MaxSteps && known.Any(n => !nodes.ContainsKey(n)))
		{
			List<string> tellers = nodes.Keys.OrderBy(n => n, cmp).ToList();
			if (tellers.Count == 0)
			{
				break;
			}
			string teller = tellers.FirstOrDefault(fast.Contains) ?? (origin.Length > 0 && nodes.ContainsKey(origin) ? origin : tellers[0]);
			int count = tellers.Any(fast.Contains) ? GossipSpread.FastTellsPerRound : GossipSpread.SlowTellsPerRound;
			HashSet<string> related = new HashSet<string>(cmp);
			foreach ((string a, string b) in world.Pairs)
			{
				if (nodes.ContainsKey(a))
				{
					related.Add(b);
				}
				if (nodes.ContainsKey(b))
				{
					related.Add(a);
				}
			}
			List<string> next = world.AllNpcs.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(cmp)
				.Where(n => known.Contains(n) && !nodes.ContainsKey(n) && !world.IsIsolated(n))
				.OrderBy(n => related.Contains(n) ? 0 : fast.Contains(n) ? 1 : 2).ThenBy(n => n, cmp).Take(count).ToList();
			if (next.Count == 0)
			{
				break;
			}
			steps++;
			foreach (string n in next)
			{
				Attach(n, nodes[teller], HeardHow.Spread, steps);
			}
		}
		List<string> unplaced = known.Where(n => !nodes.ContainsKey(n)).OrderBy(n => n, cmp).ToList();
		TrailNode lastTeller = nodes.TryGetValue((story.HeardFrom ?? "").Trim(), out TrailNode? told) ? told : root;
		foreach (string n in unplaced)
		{
			Attach(n, lastTeller, HeardHow.Unclear, steps);
		}
		int spreadDays = Math.Max(0, story.LastSpreadDay - story.CreatedDay);
		bool guessed = story.Uncertain || unplaced.Count > 0 || steps > spreadDays;
		ApplyDrift(root, intensity, fast);
		List<TrailNode> rows = new List<TrailNode>();
		Flatten(root, rows);
		return new Trail(root, rows, steps, guessed);
	}

	public static IReadOnlyList<(string Npc, HeardHow How)> FirstCircle(IReadOnlyList<string> sources, string fact, GossipWorld world)
	{
		StringComparer cmp = StringComparer.OrdinalIgnoreCase;
		Dictionary<string, HeardHow> first = new Dictionary<string, HeardHow>(cmp);
		HashSet<string> sourceSet = new HashSet<string>(sources, cmp);
		foreach (string s in sources)
		{
			first.TryAdd(s, HeardHow.Witness);
		}
		foreach (string n in world.AllNpcs.Where(n => !string.IsNullOrWhiteSpace(n) && world.Mentions(fact, n)))
		{
			first.TryAdd(n.Trim(), HeardHow.Mentioned);
		}
		foreach ((string a, string b) in world.Pairs)
		{
			if (sourceSet.Contains(a))
			{
				first.TryAdd(b, HeardHow.Close);
			}
			if (sourceSet.Contains(b))
			{
				first.TryAdd(a, HeardHow.Close);
			}
		}
		if (first.Count == 0)
		{
			foreach (string seed in FallbackSeeds)
			{
				first.TryAdd(seed, HeardHow.Unclear);
			}
		}
		return first.Where(p => !world.IsIsolated(p.Key) || sourceSet.Contains(p.Key)).OrderBy(p => p.Key, cmp).Select(p => (p.Key, p.Value)).ToList();
	}

	private static void ApplyDrift(TrailNode node, string? intensity, HashSet<string> fast)
	{
		(float first, float perDay) = DriftFor(intensity);
		float rate = fast.Contains(node.Npc) ? perDay * FastDrift : perDay;
		foreach (TrailNode child in node.Children)
		{
			if (child.How == HeardHow.Witness)
			{
				child.Drift = 0f;
			}
			else if (node.Teller == null)
			{
				child.Drift = first + rate * Math.Max(0, child.Step - 1);
			}
			else
			{
				child.Drift = Math.Max(first, node.Drift) + rate * Math.Max(1, child.Step - node.Step);
			}
			child.Drift = Math.Clamp(child.Drift, 0f, 1f);
			ApplyDrift(child, intensity, fast);
		}
	}

	private static void Flatten(TrailNode node, List<TrailNode> rows)
	{
		rows.Add(node);
		node.Children.Sort((a, b) =>
		{
			int c = a.Step.CompareTo(b.Step);
			return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Npc, b.Npc);
		});
		foreach (TrailNode child in node.Children)
		{
			Flatten(child, rows);
		}
	}

	private static IEnumerable<string> Clean(IEnumerable<string>? names) =>
		(names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
}
