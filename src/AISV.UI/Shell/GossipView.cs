using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AISV.Core.Social.Gossip;

namespace AISV.UI.Shell;

/// <summary>八卦动态里一条怎么显示：由一位知情的村民来说，但八卦讲的是谁，就不让谁来说。</summary>
internal sealed class GossipView
{
	public string? Speaker { get; private init; }

	public string Text { get; private init; } = "";

	/// <summary>最早说起的人（正文里没提到、也不是说话人时才有）。</summary>
	public string? Origin { get; private init; }

	public static GossipView Resolve(GossipEntry e, Func<string, string> display)
	{
		string text = ShellText.Clean(string.IsNullOrWhiteSpace(e.Rumor) ? e.Fact : e.Rumor);
		bool firstHand = string.Equals(e.SourceKind, "beyond_valley", StringComparison.OrdinalIgnoreCase);
		HashSet<string> subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!firstHand)
		{
			if (!string.IsNullOrWhiteSpace(e.StartedBy))
			{
				subjects.Add(e.StartedBy);
			}
			subjects.UnionWith(e.Involved.Where(s => !string.IsNullOrWhiteSpace(s)));
		}
		List<string> candidates = new List<string>();
		if (firstHand && !string.IsNullOrWhiteSpace(e.StartedBy))
		{
			candidates.Add(e.StartedBy);
		}
		if (!string.IsNullOrWhiteSpace(e.ToldBy))
		{
			candidates.Add(e.ToldBy);
		}
		string seed = e.Key + "|" + e.BornOn + "|" + e.Fact;
		candidates.AddRange(e.KnownTo.Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => Hash(seed + n)));
		string? speaker = candidates.FirstOrDefault(n => !subjects.Contains(n) && !Mentions(text, n, display));
		if (speaker != null)
		{
			text = StripLeadingName(text, display(speaker));
		}
		string? origin = !string.IsNullOrWhiteSpace(e.StartedBy) && !string.Equals(e.StartedBy, speaker, StringComparison.OrdinalIgnoreCase) && !subjects.Contains(e.StartedBy) && !Mentions(text, e.StartedBy, display)
			? e.StartedBy
			: null;
		return new GossipView { Speaker = speaker, Text = text, Origin = origin };
	}

	private static bool Mentions(string text, string npc, Func<string, string> display)
	{
		if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string shown = display(npc);
		return shown.Length >= 2 && text.Contains(shown, StringComparison.OrdinalIgnoreCase) || npc.Length >= 3 && text.Contains(npc, StringComparison.OrdinalIgnoreCase);
	}

	private static string StripLeadingName(string text, string shown)
	{
		if (shown.Length == 0)
		{
			return text;
		}
		string stripped = Regex.Replace(text, "^\\s*" + Regex.Escape(shown) + "\\s*(说|讲|提到)?\\s*[，,：:]\\s*", "");
		return stripped.Trim().Length > 0 ? stripped : text;
	}

	private static uint Hash(string s)
	{
		uint h = 2166136261u;
		foreach (char c in s)
		{
			h = (h ^ c) * 16777619u;
		}
		return h;
	}
}
