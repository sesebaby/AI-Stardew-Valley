using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Persona;

namespace AISV.Core.Social.Gossip;

/// <summary>八卦在镇上怎么传开。</summary>
public static class GossipSpread
{
	private static readonly string[] FastSpreaders = { "Caroline", "Pierre", "Haley" };

	public const int FastTellsPerRound = 2;

	public const int SlowTellsPerRound = 1;

	private static readonly string[] Seeds = { "Lewis", "Robin", "Caroline" };

	/// <param name="display">内部名 → 游戏里的名字：八卦文字是中文名时也能认出提到了谁。</param>
	public static GossipEntry Create(int day, string season, int year, string text, string chat, IEnumerable<string> sources, Func<string, string>? display = null)
	{
		int total = Dates.TotalDays(day, season, year);
		List<string> src = Clean(sources).ToList();
		List<string> aware = InitialAwareness(src, text, display);
		return new GossipEntry
		{
			Day = day,
			Season = season,
			Year = year,
			Fact = text,
			Rumor = chat,
			StartedBy = src.FirstOrDefault() ?? aware.FirstOrDefault() ?? "",
			BornOn = total,
			LastSpreadOn = total,
			KnownTo = aware,
			Involved = src,
			Unsourced = src.Count == 0
		};
	}

	/// <summary>补齐缺的字段。<paramref name="everyoneIfUnset"/>：没写谁知道时当作全镇都知道。</summary>
	public static void Normalize(GossipEntry e, bool everyoneIfUnset)
	{
		e.KnownTo = Clean(e.KnownTo ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		e.Involved = Clean(e.Involved ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		e.FromMods = Clean(e.FromMods ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		(e.Key, e.Topic) = Fill(e.Key, e.Topic);
		(e.Kind, e.SourceKind) = Fill(e.Kind, e.SourceKind);
		(e.KindRef, e.SourceRef) = Fill(e.KindRef, e.SourceRef);
		if (e.FadesOn <= 0 || e.LeavesPromptOn <= 0)
		{
			int either = Math.Max(e.FadesOn, e.LeavesPromptOn);
			if (either > 0)
			{
				e.FadesOn = e.FadesOn > 0 ? e.FadesOn : either;
				e.LeavesPromptOn = e.LeavesPromptOn > 0 ? e.LeavesPromptOn : either;
			}
		}
		if (e.BornOn <= 0 && e.Day > 0 && e.Year > 0 && !string.IsNullOrWhiteSpace(e.Season))
		{
			e.BornOn = Dates.TotalDays(e.Day, e.Season, e.Year);
		}
		if (e.LastSpreadOn <= 0)
		{
			e.LastSpreadOn = e.BornOn;
		}
		if (e.KnownTo.Count == 0 && everyoneIfUnset)
		{
			e.KnownTo = BuiltInPersonas.VanillaNames.Where(n => !Isolated(n)).ToList();
		}
		if (string.IsNullOrWhiteSpace(e.StartedBy))
		{
			e.StartedBy = e.KnownTo.FirstOrDefault() ?? "";
		}
	}

	/// <summary>这位村民知不知道（没有知情名单的当作人人知道）。</summary>
	public static bool KnownBy(GossipEntry e, string npc)
	{
		if (string.IsNullOrWhiteSpace(npc))
		{
			return false;
		}
		if (e.KnownTo == null || e.KnownTo.Count == 0)
		{
			return true;
		}
		string wanted = npc.Trim();
		return e.KnownTo.Exists(n => string.Equals(n?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>过一天传一次。传了返回 true。</summary>
	public static bool SpreadForDay(GossipEntry e, int today)
	{
		Normalize(e, false);
		if (today <= e.LastSpreadOn || e.KnownTo.Count == 0)
		{
			return false;
		}
		bool festival = e.Kind.Trim().Equals("festival", StringComparison.OrdinalIgnoreCase);
		if (festival && e.HopsLeft == 0)
		{
			return false;
		}
		HashSet<string> aware = new HashSet<string>(e.KnownTo.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
		HashSet<string> talkers = new HashSet<string>(aware.Where(n => !Isolated(n)), StringComparer.OrdinalIgnoreCase);
		if (talkers.Count == 0)
		{
			return false;
		}
		int reach = talkers.Any(IsFastSpreader) ? FastTellsPerRound : SlowTellsPerRound;
		List<string> newcomers = Candidates(aware).Take(reach).ToList();
		if (newcomers.Count == 0)
		{
			return false;
		}
		e.ToldBy = Teller(e, talkers);
		aware.UnionWith(newcomers);
		e.KnownTo = aware.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		e.LastSpreadOn = today;
		if (festival && e.HopsLeft > 0)
		{
			e.HopsLeft--;
		}
		return true;
	}

	/// <summary>提示词里这条八卦的开头：亲历、听谁说的、听说了谁的事、镇上传的。</summary>
	public static string Lead(GossipEntry e, string npc, Func<string, string> display)
	{
		if (!string.IsNullOrWhiteSpace(e.StartedBy) && string.Equals(e.StartedBy, npc, StringComparison.OrdinalIgnoreCase))
		{
			return "（你是当事人）";
		}
		if (!string.IsNullOrWhiteSpace(e.ToldBy) && !string.Equals(e.ToldBy, npc, StringComparison.OrdinalIgnoreCase))
		{
			return $"（{display(e.ToldBy)}告诉你的）";
		}
		if (!string.IsNullOrWhiteSpace(e.StartedBy))
		{
			return $"（听说的，关于{display(e.StartedBy)}）";
		}
		return "（镇上都在传）";
	}

	/// <summary>八卦带着哪些村民的痕迹：当事人、传话人、知情人、涉及的人。</summary>
	public static IEnumerable<string> Provenance(GossipEntry e) =>
		new[] { e.StartedBy, e.ToldBy }.Concat(e.KnownTo ?? new List<string>()).Concat(e.Involved ?? new List<string>())
			.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);

	public static bool Isolated(string npc) => BuiltInPersonas.GossipStyleOf(npc) == GossipStyle.Isolated;

	private static (string, string) Fill(string? a, string? b)
	{
		bool hasA = !string.IsNullOrWhiteSpace(a), hasB = !string.IsNullOrWhiteSpace(b);
		if (hasA == hasB)
		{
			return (a ?? "", b ?? "");
		}
		string value = (hasA ? a : b)!.Trim();
		return hasA ? (a!, value) : (value, b!);
	}

	private static bool IsFastSpreader(string npc) => FastSpreaders.Any(n => string.Equals(n, npc, StringComparison.OrdinalIgnoreCase));

	private static List<string> InitialAwareness(IReadOnlyList<string> sources, string text, Func<string, string>? display)
	{
		HashSet<string> aware = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(text))
		{
			aware.UnionWith(BuiltInPersonas.VanillaNames.Where(n => Mentions(text, n, display)));
		}
		foreach ((string a, string b, string _) in BuiltInPersonas.Relationships)
		{
			if (sources.Contains(a, StringComparer.OrdinalIgnoreCase))
			{
				aware.Add(b);
			}
			if (sources.Contains(b, StringComparer.OrdinalIgnoreCase))
			{
				aware.Add(a);
			}
		}
		if (aware.Count == 0)
		{
			aware.UnionWith(Seeds);
		}
		HashSet<string> sourceSet = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
		return aware.Where(n => !Isolated(n) || sourceSet.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static IEnumerable<string> Candidates(HashSet<string> aware)
	{
		HashSet<string> related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string a, string b, string _) in BuiltInPersonas.Relationships)
		{
			if (aware.Contains(a))
			{
				related.Add(b);
			}
			if (aware.Contains(b))
			{
				related.Add(a);
			}
		}
		return BuiltInPersonas.VanillaNames
			.Where(n => !aware.Contains(n) && !Isolated(n))
			.OrderBy(n => related.Contains(n) ? 0 : IsFastSpreader(n) ? 1 : 2)
			.ThenBy(n => n, StringComparer.OrdinalIgnoreCase);
	}

	private static string Teller(GossipEntry e, HashSet<string> aware)
	{
		string? fast = aware.FirstOrDefault(IsFastSpreader);
		if (!string.IsNullOrWhiteSpace(fast))
		{
			return fast;
		}
		if (!string.IsNullOrWhiteSpace(e.StartedBy) && aware.Contains(e.StartedBy))
		{
			return e.StartedBy;
		}
		return aware.FirstOrDefault() ?? "";
	}

	private static bool Mentions(string text, string npc, Func<string, string>? display)
	{
		if (text.Contains(npc, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string shown = display?.Invoke(npc) ?? "";
		return shown.Length > 0 && !string.Equals(shown, npc, StringComparison.OrdinalIgnoreCase) && text.Contains(shown, StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> Clean(IEnumerable<string> names) => names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim());
}
