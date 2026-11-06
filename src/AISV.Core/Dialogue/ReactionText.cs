using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AISV.Core.Dialogue;

/// <summary>把 says 和对提议的答复（adds）合成一段要显示、要记下的话：后一段重复前一段时只留一份。</summary>
public static class ReactionText
{
	/// <summary>记进对话记录和防重复列表用（两段之间换行）。</summary>
	public static string ForHistory(NpcReaction reaction) => TextTools.CollapseRepeats(Merge(reaction.Reaction.Trim(), ActionReply(reaction.ActionResponse), "\n"));

	/// <summary>回复框里显示用（两段之间空格）。</summary>
	public static string ForDisplay(NpcReaction reaction) => reaction.ActionResponse == null
		? TextTools.CollapseRepeats(reaction.Reaction.Trim())
		: TextTools.CollapseRepeats(Merge(reaction.Reaction.Trim(), ActionReply(reaction.ActionResponse), " "));

	/// <summary>对提议的答复：拒绝时只用 adds；答应时 adds 为空可以退到 why（看起来像内部理由的不用）。</summary>
	public static string ActionReply(ActionResponse? action)
	{
		if (action == null)
		{
			return "";
		}
		string reply = First(action.NpcReply);
		if (!action.Accepted || reply.Length > 0)
		{
			return reply;
		}
		string reason = First(action.Reason);
		return reason.Length > 0 && !LooksInternal(reason) ? reason : "";
	}

	/// <summary>日志和记忆里写的理由。</summary>
	public static string ActionLogReason(ActionResponse action)
	{
		string text = First(action.InternalReason, action.Reason, action.NpcReply);
		return text.Length > 0 ? text : "no reason provided";
	}

	/// <summary>答应时的活动概括（像内部理由的不用）。</summary>
	public static string AcceptedEvent(ActionResponse action)
	{
		if (!action.Accepted)
		{
			return "";
		}
		string text = First(action.EventDescription);
		return LooksInternal(text) ? "" : text;
	}

	internal static string Merge(string first, string second, string separator)
	{
		if (string.IsNullOrWhiteSpace(first))
		{
			return second.Trim();
		}
		if (string.IsNullOrWhiteSpace(second) || Covers(first, second))
		{
			return first.Trim();
		}
		// 后一段把前一段整个说了一遍再接着说（"好啊。" + "好啊，下午两点见。"）：只留后一段。
		if (Covers(second, first))
		{
			return second.Trim();
		}
		return first.Trim() + separator + second.Trim();
	}

	private static bool Covers(string container, string value)
	{
		if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string outerWords = Words(container), innerWords = Words(value), innerKey = TextTools.Key(value);
		return container.Contains(value, StringComparison.OrdinalIgnoreCase)
			|| (outerWords.Length > 0 && innerWords.Length > 0 && (outerWords.Contains(innerWords, StringComparison.Ordinal)
				|| (innerKey.Length >= 4 && TextTools.Key(container).Contains(innerKey, StringComparison.Ordinal)) // 中文没有空格：只比文字和数字。
				|| MostlyRepeated(outerWords, innerWords)));
	}

	private static string Words(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		foreach (char c in text.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
			}
		}
		return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
	}

	/// <summary>后一段 3～25 个长词里有八成都在前一段出现过：算复述。</summary>
	private static bool MostlyRepeated(string container, string value)
	{
		List<string> words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToList();
		if (words.Count < 3 || words.Count > 25)
		{
			return false;
		}
		HashSet<string> have = container.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
		return words.Count(have.Contains) >= (int)Math.Ceiling(words.Count * 0.8);
	}

	private static readonly string[] InternalStarts = { "reason:", "motivo:", "because ", "porque ", "原因：", "原因:", "理由：", "理由:" };

	private static readonly string[] InternalPhrases = { " because ", " porque ", "aceitou porque", "aceita porque", "accepted because", "rejected because", "recusou porque", "recusa porque" };

	private static bool LooksInternal(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string t = text.Trim().ToLowerInvariant();
		return InternalStarts.Any(t.StartsWith) || InternalPhrases.Any(t.Contains);
	}

	private static string First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
