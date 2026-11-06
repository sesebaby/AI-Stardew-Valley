using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AISV.Core.Favors;

/// <summary>
/// 托付的物品和实际给的物品对不对得上。按词比较：中日文一个字算一个词，英文复数 s / es 算同一个词。
/// 分数：3 完全一样，2 物品名整个出现在请求里，1 请求整个出现在物品名里，0 对不上。
/// </summary>
public static class ItemMatcher
{
	public const int Exact = 3;

	public static int Score(string? request, string? displayName, string? internalName)
	{
		List<string> asked = Words(request);
		return Math.Max(Score(asked, Words(displayName)), Score(asked, Words(internalName)));
	}

	/// <summary>只有一种物品和请求完全一样时才算认出来了。</summary>
	public static string? ExactId(string? request, IEnumerable<(string Id, string? DisplayName, string? InternalName)> items)
	{
		List<string> asked = Words(request);
		if (asked.Count == 0)
		{
			return null;
		}
		string? found = null;
		foreach ((string id, string? display, string? internalName) in items)
		{
			if (Score(asked, Words(display)) == Exact || Score(asked, Words(internalName)) == Exact)
			{
				if (found != null && !string.Equals(found, id, StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				found = id;
			}
		}
		return found;
	}

	private static int Score(IReadOnlyList<string> asked, IReadOnlyList<string> name)
	{
		if (asked.Count == 0 || name.Count == 0)
		{
			return 0;
		}
		if (asked.Count == name.Count && Find(asked, name) == 0)
		{
			return Exact;
		}
		if (Specific(name) && Find(asked, name) >= 0)
		{
			return 2;
		}
		return Specific(asked) && Find(name, asked) >= 0 ? 1 : 0;
	}

	public static List<string> Words(string? text)
	{
		List<string> words = new List<string>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return words;
		}
		StringBuilder current = new StringBuilder();
		void Flush()
		{
			if (current.Length > 0)
			{
				words.Add(current.ToString());
				current.Clear();
			}
		}
		foreach (char c in text.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}
			if (Unspaced(c))
			{
				Flush();
				words.Add(c.ToString());
			}
			else if (char.IsLetterOrDigit(c))
			{
				current.Append(char.ToLowerInvariant(c));
			}
			else
			{
				Flush();
			}
		}
		Flush();
		return words;
	}

	private static bool Specific(IReadOnlyList<string> words) => words.Count >= 2 || words[0].Length >= 3;

	private static int Find(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
	{
		for (int i = 0; i + needle.Count <= haystack.Count; i++)
		{
			int j = 0;
			while (j < needle.Count && SameWord(haystack[i + j], needle[j]))
			{
				j++;
			}
			if (j == needle.Count)
			{
				return i;
			}
		}
		return -1;
	}

	private static bool SameWord(string a, string b)
	{
		if (a == b)
		{
			return true;
		}
		(string longer, string shorter) = a.Length >= b.Length ? (a, b) : (b, a);
		if (shorter.Length < 3 || !longer.StartsWith(shorter, StringComparison.Ordinal))
		{
			return false;
		}
		return longer.Length == shorter.Length + 1 && longer[^1] == 's' || longer.Length == shorter.Length + 2 && longer.EndsWith("es", StringComparison.Ordinal);
	}

	private static bool Unspaced(char c) => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '豈' and <= '﫿';
}
