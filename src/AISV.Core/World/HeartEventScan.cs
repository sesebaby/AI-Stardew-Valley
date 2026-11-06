using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AISV.Core.World;

/// <summary>从事件表的键和脚本里认出好感门槛剧情。</summary>
public static class HeartEventScan
{
	private const int PointsPerHeart = 250;

	private const int QuoteChars = 60;

	private static readonly Regex Token = new Regex("\"[^\"]*\"|\\S+", RegexOptions.Compiled);

	private static readonly Regex Speak = new Regex("(?:^|/)\\s*speak\\s+(\"[^\"]+\"|\\S+)\\s+\"([^\"]*)\"", RegexOptions.Compiled);

	private static readonly Regex Command = new Regex(@"\$[A-Za-z0-9]+|%[a-z]+", RegexOptions.Compiled);

	/// <summary>一条事件表记录里每个有好感门槛、又真的出场的村民各算一条；ID 不是数字的跳过。</summary>
	public static IEnumerable<HeartEvent> Parse(string key, string script)
	{
		string[] parts = (key ?? "").Split('/');
		if (parts.Length < 2 || !int.TryParse(parts[0], out int id) || string.IsNullOrEmpty(script))
		{
			yield break;
		}
		foreach ((string npc, int points) in Requirements(parts.Skip(1)))
		{
			if (points >= PointsPerHeart && script.Contains(npc, StringComparison.Ordinal))
			{
				yield return new HeartEvent(npc, points / PointsPerHeart, id, Story(npc, script));
			}
		}
	}

	/// <summary>同一个村民同一个 ID 只留一条。</summary>
	public static List<HeartEvent> Collect(IEnumerable<KeyValuePair<string, string>> events) => events
		.SelectMany(e => Parse(e.Key, e.Value))
		.GroupBy(e => (Npc: e.Npc.ToLowerInvariant(), e.EventId))
		.Select(g => g.First())
		.OrderBy(e => e.Npc, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Hearts).ThenBy(e => e.EventId)
		.ToList();

	private static IEnumerable<(string Npc, int Points)> Requirements(IEnumerable<string> preconditions)
	{
		foreach (string condition in preconditions)
		{
			List<string> tokens = Token.Matches(condition).Select(m => m.Value.Trim('"')).ToList();
			if (tokens.Count < 3 || !(tokens[0].Equals("f", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("Friendship", StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			for (int i = 1; i + 1 < tokens.Count && int.TryParse(tokens[i + 1], out int points); i += 2)
			{
				yield return (tokens[i], points);
			}
		}
	}

	/// <summary>这位村民在剧情里开头和收尾说的话，给八卦当依据。</summary>
	private static string Story(string npc, string script)
	{
		List<string> lines = Speak.Matches(script)
			.Where(m => m.Groups[1].Value.Trim('"').Equals(npc, StringComparison.OrdinalIgnoreCase))
			.Select(m => Clean(m.Groups[2].Value)).Where(t => t.Length > 0).ToList();
		return lines.Count switch
		{
			0 => "农夫和对方单独经历了一段事，细节没有外传",
			1 => $"当时说过：“{lines[0]}”",
			_ => $"当时先说：“{lines[0]}”，最后说：“{lines[^1]}”"
		};
	}

	private static string Clean(string text)
	{
		string first = text.Split('^')[0].Replace("#$b#", " ").Replace("#$e#", " ").Replace('#', ' ').Replace("@", "农夫");
		string plain = Regex.Replace(Command.Replace(first, ""), @"\s+", " ").Trim();
		return plain.Length <= QuoteChars ? plain : plain[..QuoteChars] + "…";
	}
}
