using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AISV.Core.Dialogue;

/// <summary>村民的情绪（对应立绘：普通、开心、难过、生气、爱心、特别）。</summary>
public static class Emotion
{
	public const string Neutral = "neutral";

	private static readonly (string Face, string Spellings)[] Faces =
	{
		("neutral", "neutral calm 平静"),
		("happy", "happy glad cheerful 高兴 开心"),
		("sad", "sad upset down 伤心 难过"),
		("angry", "angry furious annoyed cross mad 生气 恼火"),
		("love", "love smitten 心动 喜欢"),
		("unique", "unique")
	};

	public static string Normalize(string? emotion)
	{
		string e = emotion?.Trim() ?? "";
		if (e.Length == 0)
		{
			return Neutral;
		}
		foreach ((string face, string spellings) in Faces)
		{
			if (spellings.Split(' ').Contains(e, StringComparer.OrdinalIgnoreCase))
			{
				return face;
			}
		}
		return Neutral;
	}

	/// <summary>有专用立绘的情绪（皮埃尔生气）：返回 assets/portraits 下的图名。</summary>
	public static string? PortraitOverride(string npc, string? emotion) =>
		Normalize(emotion) == "angry" && string.Equals(npc, "Pierre", StringComparison.OrdinalIgnoreCase) ? "Pierre_Annoyed" : null;

	/// <summary>
	/// 这位村民这个情绪实际用的指令：有专用立绘时不加指令；生气时乔迪用"特别"；立绘没有生气那一格时依次退到特别、难过、平常（tiles = 立绘格数）。
	/// </summary>
	public static string PortraitCommandFor(string npc, string? emotion, int tiles)
	{
		string e = Normalize(emotion);
		if (PortraitOverride(npc, e) != null)
		{
			return "";
		}
		if (e != "angry")
		{
			return PortraitCommand(e);
		}
		if (string.Equals(npc, "Jodi", StringComparison.OrdinalIgnoreCase))
		{
			return "$u";
		}
		return tiles > 5 ? "$a" : tiles > 3 ? "$u" : tiles > 2 ? "$s" : "";
	}

	/// <summary>立绘指令对应第几格：平常 0、$h 1、$s 2、$u 3、$l 4、$a 5。</summary>
	public static int PortraitIndex(string command) => command switch
	{
		"$h" => 1,
		"$s" => 2,
		"$u" => 3,
		"$l" => 4,
		"$a" => 5,
		_ => 0
	};

	/// <summary>游戏对话里的立绘指令：$h 开心、$s 难过、$u 特别、$l 爱心、$a 生气。</summary>
	public static string PortraitCommand(string emotion) => Normalize(emotion) switch
	{
		"happy" => "$h",
		"sad" => "$s",
		"unique" => "$u",
		"love" => "$l",
		"angry" => "$a",
		_ => ""
	};
}

/// <summary>对话文字的整理：截断到字数上限、去掉模型说了两遍的句子。</summary>
public static class GameDialogueText
{
	private const char PageMark = '\uE000';

	private static readonly Regex AnyCommand = new Regex("\\$[A-Za-z0-9]+", RegexOptions.Compiled);

	// 只认表情指令：字母或单个数字。$100 是金额，留着（$ 后面会换全角）。
	private static readonly Regex EmotionCommand = new Regex("\\$(?:[A-Za-z]+|[0-9](?![0-9]))", RegexOptions.Compiled);

	private static readonly Regex PercentToken = new Regex("%[A-Za-z]+", RegexOptions.Compiled);

	private static readonly Regex BreakToken = new Regex("#\\$[A-Za-z]#", RegexOptions.Compiled);

	/// <summary>
	/// AI 的文字放进游戏对话前整理：保留翻页 #$b#，@ 换成玩家名，去掉模型写的表情指令，其余指令符换全角，免得被游戏当成指令。
	/// </summary>
	public static string ForDisplay(string? text, string playerName)
	{
		string s = (text ?? "").Replace("#$b#", PageMark.ToString()).Replace("#$e#", PageMark.ToString());
		s = EmotionCommand.Replace(s, "");
		s = s.Replace("#", "＃").Replace("$", "＄").Replace("^", "＾").Replace("%", "％").Replace("@", "＠");
		s = s.Replace("＠", Plain(playerName));
		return s.Trim().Replace(PageMark.ToString(), "#$b#");
	}

	/// <summary>游戏原台词当参考前整理：@ 换成玩家名，去掉指令和分隔符，最多 260 字。</summary>
	public static string VanillaHint(string? raw, string playerName)
	{
		string s = BreakToken.Replace(raw ?? "", " ");
		s = AnyCommand.Replace(s, " ");
		s = PercentToken.Replace(s, " ");
		s = s.Replace("@", Plain(playerName)).Replace("#", " ").Replace("^", " ").Replace("\r", " ").Replace("\n", " ");
		s = Regex.Replace(s, " {2,}", " ").Trim();
		return s.Length > 260 ? s.Substring(0, 260) : s;
	}

	private static string Plain(string name) => (name ?? "").Replace("#", "").Replace("$", "").Replace("^", "").Replace("%", "").Replace("@", "");
}

public static class TextTools
{
	private static readonly char[] SentenceEnders = { '.', '!', '?', '…', '。', '！', '？' };

	private static readonly Regex PageBreak = new Regex("(#\\$[be]#)", RegexOptions.Compiled);

	private static readonly Regex SentenceEnd = new Regex("(?<=[。！？!?…～~”」』])(?![。！？!?…～~”」』\"'）)])|(?<=\\.)(?=\\s)", RegexOptions.Compiled);

	private static readonly Regex Command = new Regex("\\$[a-zA-Z0-9]+|%[a-zA-Z]+|@", RegexOptions.Compiled);

	private const int MinCompareLength = 4;

	private const int MinContainedLength = 6;

	/// <summary>去掉游戏字体画不了的字符（表情符号之类）。</summary>
	public static string CleanForGame(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		StringBuilder sb = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (!char.IsSurrogate(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.OtherSymbol && c != '️' && c != '‍')
			{
				sb.Append(c);
			}
		}
		return sb.ToString().Trim();
	}

	/// <summary>超长时尽量在句末断开（句末在后一半里），其次在空格处（后 30% 里），否则硬截加省略号。</summary>
	public static string Truncate(string? text, int maxLength)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		string t = text.Trim();
		int max = Math.Max(0, maxLength);
		if (t.Length <= max)
		{
			return t;
		}
		if (max == 0)
		{
			return "";
		}
		string head = t.Substring(0, max);
		int end = SentenceEnders.Max(c => head.LastIndexOf(c));
		if (end > max * 0.5)
		{
			return t.Substring(0, end + 1).TrimEnd();
		}
		int space = head.LastIndexOf(' ');
		if (space > max * 0.7)
		{
			return t.Substring(0, space).TrimEnd() + "…";
		}
		return head.TrimEnd() + "…";
	}

	private sealed class Piece
	{
		public string Text = "";

		public string Key = "";

		/// <summary>这句前面的翻页符（#$b# / #$e#），没有为空。</summary>
		public string Break = "";
	}

	/// <summary>
	/// 去掉重复的句子：按翻页符和句末标点切句，只比文字和数字。连着重复的一串只留第一串；
	/// 和前面某句一样或被前面某句包含的去掉；很短的单句不动。alreadySaid 是上一句已经显示过的话，和它重复的也去掉（全被去掉时只做普通去重）。
	/// </summary>
	public static string CollapseRepeats(string? text, string? alreadySaid = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text ?? "";
		}
		List<string> seen = string.IsNullOrWhiteSpace(alreadySaid) ? new List<string>() : Split(alreadySaid).Select(p => p.Key).Where(k => k.Length >= MinContainedLength).ToList();
		List<Piece> pieces = Split(text);
		List<Piece> kept = Dedupe(pieces, seen);
		if (seen.Count > 0 && kept.All(p => p.Key.Length == 0))
		{
			kept = Dedupe(pieces, new List<string>());
		}
		if (kept.Count == pieces.Count)
		{
			return text;
		}
		StringBuilder sb = new StringBuilder(text.Length);
		string carry = "";
		bool lastRemoved = false;
		foreach (Piece p in pieces)
		{
			if (!kept.Contains(p))
			{
				if (carry.Length == 0)
				{
					carry = p.Break;
				}
				lastRemoved = true;
				continue;
			}
			// 只剩表情指令的一截跟在被删的句子后面：接到前一句上。
			if (p.Key.Length == 0 && lastRemoved)
			{
				sb.Append(p.Text.Trim());
				carry = "";
				lastRemoved = false;
				continue;
			}
			lastRemoved = false;
			string brk = p.Break.Length > 0 ? p.Break : carry;
			if (sb.Length > 0 && brk.Length > 0)
			{
				sb.Append(brk);
			}
			carry = "";
			sb.Append(p.Text);
		}
		return sb.ToString().Trim();
	}

	private static List<Piece> Dedupe(List<Piece> pieces, List<string> seen)
	{
		List<Piece> list = pieces.ToList();
		bool removed = true;
		while (removed)
		{
			removed = false;
			for (int i = 0; i < list.Count && !removed; i++)
			{
				for (int len = 1; i + len * 2 <= list.Count && !removed; len++)
				{
					int total = 0;
					bool same = true;
					for (int k = 0; k < len && same; k++)
					{
						same = list[i + k].Key == list[i + len + k].Key;
						total += list[i + k].Key.Length;
					}
					if (same && total >= MinCompareLength)
					{
						list.RemoveRange(i + len, len);
						removed = true;
					}
				}
			}
		}
		List<string> keys = seen.ToList();
		List<Piece> result = new List<Piece>();
		foreach (Piece p in list)
		{
			if (p.Key.Length >= MinCompareLength && keys.Any(k => k == p.Key || (p.Key.Length >= MinContainedLength && k.Length > p.Key.Length && k.Contains(p.Key, StringComparison.Ordinal))))
			{
				continue;
			}
			if (p.Key.Length >= MinCompareLength)
			{
				keys.Add(p.Key);
			}
			// 前一句被这句开头原样重复后接着说：去掉前一句。
			if (result.Count > 0)
			{
				Piece prev = result[^1];
				if (prev.Key.Length >= MinContainedLength && p.Key.Length > prev.Key.Length && p.Key.StartsWith(prev.Key, StringComparison.Ordinal))
				{
					if (p.Break.Length == 0)
					{
						p.Break = prev.Break;
					}
					result.RemoveAt(result.Count - 1);
				}
			}
			result.Add(p);
		}
		return result;
	}

	private static List<Piece> Split(string text)
	{
		List<Piece> pieces = new List<Piece>();
		string pendingBreak = "";
		foreach (string part in PageBreak.Split(text))
		{
			if (PageBreak.IsMatch(part) && part.Length <= 4)
			{
				pendingBreak = part;
				continue;
			}
			foreach (string sentence in SentenceEnd.Split(part))
			{
				if (sentence.Length > 0)
				{
					pieces.Add(new Piece { Text = sentence, Key = Key(sentence), Break = pendingBreak });
					pendingBreak = "";
				}
			}
		}
		return pieces;
	}

	/// <summary>只留文字和数字、小写，用来比较两句话是否相同。</summary>
	public static string Key(string text)
	{
		string s = Command.Replace(text ?? "", "");
		StringBuilder sb = new StringBuilder(s.Length);
		foreach (char c in s.Normalize(NormalizationForm.FormKC))
		{
			if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.ToLowerInvariant(c));
			}
		}
		return sb.ToString();
	}
}
