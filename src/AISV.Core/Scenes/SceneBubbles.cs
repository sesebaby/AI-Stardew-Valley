using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AISV.Core.Scenes;

/// <summary>旁听的头顶气泡：去掉对话码、按句子切段、排时间。</summary>
public static class SceneBubbles
{
	public const int MaxChars = 60;

	public const int LeadInTicks = 30;

	public const int GapTicks = 24;

	private static readonly Regex GenderBlock = new Regex(@"\$\{(.*?)\}\$", RegexOptions.CultureInvariant);

	private static readonly Regex PageBreak = new Regex(@"#\$[be]#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex Code = new Regex(@"\$(\d+|[A-Za-z](?![A-Za-z]))", RegexOptions.CultureInvariant);

	private static readonly Regex Token = new Regex(@"(?<!\d)%", RegexOptions.CultureInvariant);

	private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

	private const string OpenQuotes = "\"“„«「『";

	private const string CloseQuotes = "\"”“»」』";

	private const string Ends = ".!?…。！？";

	private const string WideEnds = "。！？…";

	private const string Closers = "\"'”’»」』）)";

	/// <summary>只留说出口的字：性别分支取第一种、分页并成一行、去掉 $h 这类码和 #、%，外层引号去掉。</summary>
	public static string Clean(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string t = GenderBlock.Replace(text, m => FirstVariant(m.Groups[1].Value, '^', '¦'));
		t = string.Join(" ", PageBreak.Split(t).Select(page => FirstVariant(page, '^')));
		t = Code.Replace(t, "");
		t = t.Replace('#', ' ');
		t = Token.Replace(t, "");
		t = Spaces.Replace(t, " ").Trim();
		while (t.Length >= 2 && OpenQuotes.Contains(t[0]) && CloseQuotes.Contains(t[^1]))
		{
			t = t[1..^1].Trim();
		}
		return t;
	}

	/// <summary>按句子拼成每段不超过 <paramref name="max"/> 字；一句太长时按空格（没有空格就硬切）分。</summary>
	public static IReadOnlyList<string> Split(string? text, int max = MaxChars)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<string>();
		}
		string t = Spaces.Replace(text, " ").Trim();
		max = Math.Max(1, max);
		if (t.Length <= max)
		{
			return new[] { t };
		}
		List<string> parts = new List<string>();
		StringBuilder current = new StringBuilder();
		foreach ((string sentence, bool spaced) in Sentences(t))
		{
			if (sentence.Length > max)
			{
				Flush(current, parts);
				parts.AddRange(SplitLong(sentence, max));
				continue;
			}
			int gap = current.Length > 0 && spaced ? 1 : 0;
			if (current.Length > 0 && current.Length + gap + sentence.Length > max)
			{
				Flush(current, parts);
				gap = 0;
			}
			if (gap > 0)
			{
				current.Append(' ');
			}
			current.Append(sentence);
		}
		Flush(current, parts);
		return parts;
	}

	/// <summary>排气泡：开头空 30 tick，每段显示 90 + 3×字数（150～420）tick，段间空 24 tick。</summary>
	public static IReadOnlyList<OverheardCue> Timeline(IReadOnlyList<SceneLine> lines, int startTick)
	{
		List<OverheardCue> cues = new List<OverheardCue>();
		int at = startTick + LeadInTicks;
		foreach (SceneLine line in lines.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Speaker)))
		{
			foreach (string part in Split(Clean(line.Text)))
			{
				int duration = Math.Clamp(90 + 3 * part.Length, 150, 420);
				cues.Add(new OverheardCue(line.Speaker.Trim(), part, at, duration));
				at += duration + GapTicks;
			}
		}
		return cues;
	}

	private static string FirstVariant(string text, params char[] separators) =>
		text.IndexOfAny(separators) < 0 ? text : text.Split(separators).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";

	/// <summary>切句子：句末标点（连着的算一个）加后面的引号括号；后面是空格就吃掉空格，全角句号后面直接断。</summary>
	private static IEnumerable<(string Sentence, bool Spaced)> Sentences(string text)
	{
		int start = 0;
		bool spaced = false;
		int i = 0;
		while (i < text.Length)
		{
			if (!Ends.Contains(text[i]))
			{
				i++;
				continue;
			}
			bool wide = false;
			for (; i < text.Length && Ends.Contains(text[i]); i++)
			{
				wide = WideEnds.Contains(text[i]);
			}
			for (; i < text.Length && Closers.Contains(text[i]); i++)
			{
			}
			if (i >= text.Length)
			{
				break;
			}
			if (text[i] == ' ')
			{
				yield return (text[start..i], spaced);
				i++;
				start = i;
				spaced = true;
			}
			else if (wide)
			{
				yield return (text[start..i], spaced);
				start = i;
				spaced = false;
			}
		}
		if (start < text.Length)
		{
			yield return (text[start..], spaced);
		}
	}

	/// <summary>一句太长：先按满长度装词，再试着把每段长度摊匀（段数不增加）。</summary>
	private static IEnumerable<string> SplitLong(string sentence, int max)
	{
		string[] words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		List<string> widest = Pack(words, max, max);
		if (widest.Count <= 1)
		{
			return widest;
		}
		int even = (int)Math.Ceiling(sentence.Length / (double)widest.Count);
		for (int limit = Math.Max(1, even); limit < max; limit++)
		{
			List<string> attempt = Pack(words, limit, max);
			if (attempt.Count <= widest.Count)
			{
				return attempt;
			}
		}
		return widest;
	}

	private static List<string> Pack(IReadOnlyList<string> words, int limit, int max)
	{
		List<string> parts = new List<string>();
		StringBuilder line = new StringBuilder();
		foreach (string word in words)
		{
			string rest = word;
			if (word.Length > max)
			{
				Flush(line, parts);
				while (rest.Length > limit)
				{
					int take = char.IsSurrogatePair(rest, limit - 1) ? Math.Max(limit - 1, 2) : limit;
					parts.Add(rest[..take]);
					rest = rest[take..];
				}
			}
			if (line.Length > 0 && line.Length + 1 + rest.Length > limit)
			{
				Flush(line, parts);
			}
			if (line.Length > 0)
			{
				line.Append(' ');
			}
			line.Append(rest);
		}
		Flush(line, parts);
		return parts;
	}

	private static void Flush(StringBuilder current, List<string> parts)
	{
		if (current.Length > 0)
		{
			parts.Add(current.ToString());
			current.Clear();
		}
	}
}
