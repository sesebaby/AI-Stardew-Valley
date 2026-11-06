using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AISV.Core.Diagnostics;

namespace AISV.Core.Dialogue;

/// <summary>村民对玩家一句话的反应（AI 回的 JSON）。字段名就是 JSON 里的键。</summary>
public sealed class NpcReaction
{
	[JsonPropertyName("says")]
	public string Reaction { get; set; } = "";

	[JsonPropertyName("warmth")]
	public int FriendshipPoints { get; set; }

	[JsonPropertyName("feeling")]
	public string Emotion { get; set; } = "neutral";

	[JsonPropertyName("on_request")]
	public ActionResponse? ActionResponse { get; set; }

	[JsonPropertyName("favor_asked")]
	public RequestedFavor? RequestedFavor { get; set; }

	[JsonPropertyName("step_done")]
	[JsonConverter(typeof(TextOrNullConverter))]
	public string? ArcObjectiveCompleted { get; set; }

	[JsonPropertyName("step_how")]
	[JsonConverter(typeof(TextOrNullConverter))]
	public string? ArcObjectiveApproach { get; set; }

	[JsonPropertyName("plan_offer")]
	public NpcProposal? Proposal { get; set; }

	[JsonPropertyName("gift")]
	public NpcGift? Gift { get; set; }
}

/// <summary>模型偶尔在文字字段里回对象或数字：当作没有，不让整段回复解析失败。</summary>
public sealed class TextOrNullConverter : JsonConverter<string?>
{
	public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			return reader.GetString();
		}
		reader.Skip();
		return null;
	}

	public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

/// <summary>玩家提议一起做某事时，村民答不答应。</summary>
public sealed class ActionResponse
{
	[JsonPropertyName("agrees")]
	public bool Accepted { get; set; }

	[JsonPropertyName("adds")]
	public string NpcReply { get; set; } = "";

	[JsonPropertyName("private_note")]
	public string InternalReason { get; set; } = "";

	[JsonPropertyName("why")]
	public string Reason { get; set; } = "";

	[JsonPropertyName("what_happens")]
	public string EventDescription { get; set; } = "";
}

public sealed class RequestedFavor
{
	[JsonPropertyName("kind")]
	public string Type { get; set; } = "";

	[JsonPropertyName("what")]
	public string Description { get; set; } = "";

	[JsonPropertyName("item")]
	public string Target { get; set; } = "";
}

/// <summary>村民主动提议：跟你去、来帮工、约见。</summary>
public sealed class NpcProposal
{
	[JsonPropertyName("summary")]
	public string Summary { get; set; } = "";

	[JsonPropertyName("task")]
	public string Task { get; set; } = "";

	[JsonPropertyName("time")]
	public string Time { get; set; } = "";

	[JsonPropertyName("kind")]
	public string Type { get; set; } = "";

	[JsonPropertyName("when")]
	public string When { get; set; } = "";

	[JsonPropertyName("where")]
	public string Where { get; set; } = "";
}

public sealed class NpcGift
{
	[JsonPropertyName("item_id")]
	public string ItemId { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("description")]
	public string Description { get; set; } = "";

	[JsonPropertyName("reason")]
	public string Reason { get; set; } = "";
}

/// <summary>
/// 把 AI 回的文字读成 <see cref="NpcReaction"/>。模型常见的毛病都容忍：包在代码块里、JSON 前后有闲话、
/// 字符串里有没转义的换行或引号、正数写成 +30。实在读不成 JSON 时，用正则把 话、分数、情绪捞出来。
/// </summary>
public static class ReactionParser
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	/// <param name="allowNegative">敌意模式下分数可以是负的（-100～100），否则 0～100。</param>
	public static NpcReaction? Parse(string raw, int maxLength, bool allowNegative, ILog log)
	{
		try
		{
			string json = JsonCleaner.Clean(raw);
			JsonDocument? probe = null;
			try
			{
				probe = JsonDocument.Parse(json);
				if (probe.RootElement.ValueKind == JsonValueKind.Array && probe.RootElement.GetArrayLength() > 0)
				{
					json = probe.RootElement[0].GetRawText();
				}
			}
			finally
			{
				probe?.Dispose();
			}
			NpcReaction? r = JsonSerializer.Deserialize<NpcReaction>(json, Options);
			if (r == null)
			{
				return null;
			}
			Normalize(r, maxLength, allowNegative);
			if (string.IsNullOrWhiteSpace(r.Reaction) && string.IsNullOrWhiteSpace(r.ActionResponse?.NpcReply))
			{
				log.Warn("AI 回的 JSON 里没有反应文字。原文：" + raw);
				return null;
			}
			return r;
		}
		catch (JsonException ex)
		{
			log.Warn("AI 回的反应不是有效 JSON（" + ex.Message + "），尝试从文字里捞。原文：" + raw);
			return Salvage(raw, maxLength, allowNegative, log);
		}
	}

	/// <summary>只有 says、没有分数和情绪（有的模型在长提示里会偷懒）：调用方应该再问一次。</summary>
	public static bool IsReactionOnly(string raw)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			JsonElement root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}
			bool reaction = false, points = false, emotion = false;
			foreach (JsonProperty p in root.EnumerateObject())
			{
				reaction |= p.Name.Equals("says", StringComparison.OrdinalIgnoreCase);
				points |= p.Name.Equals("warmth", StringComparison.OrdinalIgnoreCase);
				emotion |= p.Name.Equals("feeling", StringComparison.OrdinalIgnoreCase);
			}
			return reaction && !points && !emotion;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	internal static void Normalize(NpcReaction r, int maxLength, bool allowNegative)
	{
		r.FriendshipPoints = Math.Clamp(r.FriendshipPoints, allowNegative ? -100 : 0, 100);
		r.Emotion = Emotion.Normalize(r.Emotion);
		r.Reaction = TextTools.Truncate(r.Reaction?.Trim() ?? "", Math.Max(0, maxLength));
		if (r.ActionResponse != null)
		{
			r.ActionResponse.NpcReply ??= "";
			r.ActionResponse.InternalReason ??= "";
			r.ActionResponse.Reason ??= "";
			r.ActionResponse.EventDescription ??= "";
		}
		if (r.Proposal != null)
		{
			NpcProposal p = r.Proposal;
			p.Type = (p.Type ?? "").Trim().ToLowerInvariant();
			p.Task = (p.Task ?? "").Trim().ToLowerInvariant();
			p.Summary = (p.Summary ?? "").Trim();
			p.When = (p.When ?? "").Trim();
			p.Time = (p.Time ?? "").Trim();
			p.Where = (p.Where ?? "").Trim();
			if (p.Type is "" or "null" or "none")
			{
				r.Proposal = null;
			}
		}
		if (r.Gift != null)
		{
			NpcGift g = r.Gift;
			g.ItemId = (g.ItemId ?? "").Trim();
			g.Name = (g.Name ?? "").Trim();
			g.Description = (g.Description ?? "").Trim();
			g.Reason = (g.Reason ?? "").Trim();
			if (g.ItemId is "" or "null" or "none")
			{
				r.Gift = null;
			}
		}
		if (r.RequestedFavor != null)
		{
			r.RequestedFavor.Type ??= "";
			r.RequestedFavor.Description ??= "";
			r.RequestedFavor.Target ??= "";
		}
	}

	/// <summary>JSON 坏了：用正则捞 says（必须有）、分数（没有按 30）、情绪、承诺；整段都是话、一点 JSON 都没有时，就把这段话当反应。</summary>
	private static NpcReaction? Salvage(string raw, int maxLength, bool allowNegative, ILog log)
	{
		Match reaction = Regex.Match(raw, "\"says\"\\s*:\\s*\"(.+?)\"\\s*(?:,\\s*\"[A-Za-z_]+\"\\s*:|\\}|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		if (!reaction.Success)
		{
			string prose = raw.Trim();
			if (prose.Length < 2 || prose.IndexOfAny(new[] { '{', '}', '[', ']' }) >= 0 || prose.Contains("\"says\"", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			NpcReaction plain = new NpcReaction { Reaction = prose, FriendshipPoints = 30, Emotion = "neutral" };
			Normalize(plain, maxLength, allowNegative);
			log.Warn($"AI 没按格式回，把整段话当反应：\"{plain.Reaction}\"（{plain.FriendshipPoints} 分）");
			return plain;
		}
		Match points = Regex.Match(raw, "\"warmth\"\\s*:\\s*\"?(-?\\d+)", RegexOptions.IgnoreCase);
		Match emotion = Regex.Match(raw, "\"feeling\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
		NpcReaction r = new NpcReaction
		{
			Reaction = reaction.Groups[1].Value.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "").Replace("\\t", " ").Replace("\\\\", "\\"),
			FriendshipPoints = points.Success && int.TryParse(points.Groups[1].Value, out int p) ? p : 30,
			Emotion = emotion.Success ? emotion.Groups[1].Value : "neutral"
		};
		Normalize(r, maxLength, allowNegative);
		log.Warn($"从坏掉的 JSON 里捞回了反应：\"{r.Reaction}\"（{r.FriendshipPoints} 分）");
		return r;
	}
}

/// <summary>把模型回的"差不多是 JSON"的文字整理成能解析的 JSON。</summary>
public static class JsonCleaner
{
	private static readonly Regex ControlChars = new Regex("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]", RegexOptions.Compiled);

	public static string Clean(string raw)
	{
		string text = raw.Trim();
		if (text.StartsWith("```", StringComparison.Ordinal))
		{
			int nl = text.IndexOf('\n');
			text = nl >= 0 ? text.Substring(nl + 1) : text.TrimStart('`');
			if (text.EndsWith("```", StringComparison.Ordinal))
			{
				text = text.Substring(0, text.Length - 3);
			}
			text = text.Trim();
		}
		text = text.Replace("﻿", "").Replace("​", "").Replace("‌", "").Replace("‍", "");
		text = ControlChars.Replace(text, "");
		text = EscapeNewlinesInStrings(text);
		text = StripPlusSigns(text);
		text = StripTrailingCommas(text);
		if (FirstParsable(text) is string payload)
		{
			return payload;
		}
		string repaired = RepairQuotes(text);
		return FirstParsable(repaired) ?? text;
	}

	/// <summary>从第一个 { 或 [ 开始，找第一段能解析的完整 JSON。</summary>
	internal static string? FirstParsable(string text)
	{
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] is not ('{' or '[') || EndOf(text, i) is not int end)
			{
				continue;
			}
			string candidate = text.Substring(i, end - i + 1);
			try
			{
				using JsonDocument _ = JsonDocument.Parse(candidate);
				return candidate;
			}
			catch (JsonException)
			{
			}
		}
		return null;
	}

	/// <summary>配对括号找到这段 JSON 的结尾（跳过字符串里的括号）。</summary>
	private static int? EndOf(string text, int start)
	{
		Stack<char> stack = new Stack<char>();
		bool inString = false, escaped = false;
		for (int i = start; i < text.Length; i++)
		{
			char c = text[i];
			if (inString)
			{
				if (escaped)
				{
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == '"')
				{
					inString = false;
				}
				continue;
			}
			switch (c)
			{
				case '"':
					inString = true;
					break;
				case '{':
				case '[':
					stack.Push(c);
					break;
				case '}':
				case ']':
					if (stack.Count == 0 || stack.Pop() != (c == '}' ? '{' : '['))
					{
						return null;
					}
					if (stack.Count == 0)
					{
						return i;
					}
					break;
			}
		}
		return null;
	}

	/// <summary>字符串里直接换行的，改成 \n。</summary>
	internal static string EscapeNewlinesInStrings(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		bool inString = false, escaped = false;
		foreach (char c in text)
		{
			if (inString && !escaped && (c == '\n' || c == '\r'))
			{
				if (c == '\n')
				{
					sb.Append("\\n");
				}
				continue;
			}
			if (inString && escaped)
			{
				escaped = false;
			}
			else if (inString && c == '\\')
			{
				escaped = true;
			}
			else if (c == '"')
			{
				inString = !inString;
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	/// <summary>
	/// 字符串值里没转义的双引号（比如 "says": "他说"你好"就走了"）：
	/// 一个引号后面紧跟的不是 , } ] :（允许空白）就不是字符串结尾，把它转义掉。
	/// </summary>
	internal static string RepairQuotes(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length + 16);
		bool inString = false, escaped = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (!inString)
			{
				if (c == '"')
				{
					inString = true;
				}
				sb.Append(c);
				continue;
			}
			if (escaped)
			{
				escaped = false;
				sb.Append(c);
				continue;
			}
			if (c == '\\')
			{
				escaped = true;
				sb.Append(c);
				continue;
			}
			if (c == '"')
			{
				int j = i + 1;
				while (j < text.Length && char.IsWhiteSpace(text[j]))
				{
					j++;
				}
				if (j >= text.Length || text[j] is ',' or '}' or ']' or ':')
				{
					inString = false;
					sb.Append(c);
				}
				else
				{
					sb.Append("\\\"");
				}
				continue;
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	/// <summary>"warmth": +30 → 30（只改字符串外面的）。</summary>
	internal static string StripPlusSigns(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		bool inString = false, escaped = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (inString)
			{
				if (escaped)
				{
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == '"')
				{
					inString = false;
				}
				sb.Append(c);
				continue;
			}
			if (c == '"')
			{
				inString = true;
			}
			else if (c == '+' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && PrevNonSpace(sb) is ':' or '[' or ',')
			{
				continue;
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	/// <summary>{"a": 1,} → {"a": 1}（只改字符串外面的）。</summary>
	internal static string StripTrailingCommas(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		bool inString = false, escaped = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (inString)
			{
				if (escaped)
				{
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == '"')
				{
					inString = false;
				}
			}
			else if (c == '"')
			{
				inString = true;
			}
			else if (c == ',')
			{
				int j = i + 1;
				while (j < text.Length && char.IsWhiteSpace(text[j]))
				{
					j++;
				}
				if (j < text.Length && text[j] is '}' or ']')
				{
					continue;
				}
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	private static char PrevNonSpace(StringBuilder sb)
	{
		for (int i = sb.Length - 1; i >= 0; i--)
		{
			if (!char.IsWhiteSpace(sb[i]))
			{
				return sb[i];
			}
		}
		return '\0';
	}
}
