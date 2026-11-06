using System;
using System.Net;
using System.Text.Json;

namespace AISV.Core.Ai;

/// <summary>读懂服务商的回复：错误归类、错误说明、哪个参数不被支持、是不是被输出上限截断。</summary>
public static class ProviderErrors
{
	private static readonly string[] AdjustableParameters = { "max_completion_tokens", "max_tokens", "temperature", "reasoning_effort" };

	public static FailureKind Classify(HttpStatusCode status, string? body)
	{
		// 先看正文里的错误码：同一个状态码，不同服务商的意思不一样。
		if (Has(body, "context_length_exceeded") || Has(body, "maximum context length") || Has(body, "context window") || Has(body, "too many tokens"))
		{
			return FailureKind.ContextTooLong;
		}
		if (Has(body, "aisv_insufficient_balance"))
		{
			return FailureKind.QuotaExhausted;
		}
		if (Has(body, "aisv_session_invalid"))
		{
			return FailureKind.AccountRequired;
		}
		if (Has(body, "aisv_banned"))
		{
			return FailureKind.AccountBanned;
		}
		if (Has(body, "aisv_maintenance"))
		{
			return FailureKind.ServerMaintenance;
		}
		if (Has(body, "aisv_global_cap"))
		{
			return FailureKind.QuotaExhausted;
		}
		if (Has(body, "insufficient_quota") || Has(body, "exceeded your current quota"))
		{
			return FailureKind.PaymentRequired;
		}
		if (Has(body, "content_filter") || Has(body, "content_policy"))
		{
			return FailureKind.ContentFiltered;
		}
		bool mentionsKey = Has(body, "api key") || Has(body, "api_key") || Has(body, "authentication");
		switch (status)
		{
			case HttpStatusCode.BadRequest:
			case HttpStatusCode.UnprocessableEntity:
				if (mentionsKey)
				{
					return FailureKind.InvalidApiKey;
				}
				return Has(body, "model") && (Has(body, "not found") || Has(body, "does not exist")) ? FailureKind.ModelNotFound : FailureKind.BadRequest;
			case HttpStatusCode.Unauthorized:
				return FailureKind.InvalidApiKey;
			case HttpStatusCode.PaymentRequired:
				return FailureKind.PaymentRequired;
			case HttpStatusCode.Forbidden:
				return mentionsKey ? FailureKind.InvalidApiKey : FailureKind.PermissionDenied;
			case HttpStatusCode.NotFound:
				return FailureKind.ModelNotFound;
			case HttpStatusCode.RequestTimeout:
				return FailureKind.Timeout;
			case HttpStatusCode.Gone:
				return FailureKind.ModelRetired;
			case HttpStatusCode.RequestEntityTooLarge:
				return FailureKind.ContextTooLong;
			case HttpStatusCode.TooManyRequests:
				return FailureKind.RateLimit;
			case HttpStatusCode.InternalServerError:
				return FailureKind.ServerError;
			case HttpStatusCode.BadGateway:
			case HttpStatusCode.ServiceUnavailable:
			case HttpStatusCode.GatewayTimeout:
				return Has(body, "overload") || Has(body, "loading") ? FailureKind.HighDemand : FailureKind.ServiceUnavailable;
			default:
				return (int)status >= 500 ? FailureKind.ServerError : FailureKind.Other;
		}
	}

	/// <summary>从错误回复里取一句说明（error / error.message / message / detail / title），最多 160 字。</summary>
	public static string Message(string? body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return "";
		}
		string text = body.Trim();
		if (text.StartsWith("{", StringComparison.Ordinal))
		{
			try
			{
				using JsonDocument doc = JsonDocument.Parse(text);
				JsonElement root = doc.RootElement;
				if (root.TryGetProperty("error", out JsonElement error))
				{
					if (error.ValueKind == JsonValueKind.String)
					{
						return Shorten(error.GetString());
					}
					if (error.ValueKind == JsonValueKind.Object && TryString(error, "message", out string? m))
					{
						return Shorten(m);
					}
				}
				foreach (string key in new[] { "message", "detail", "title" })
				{
					if (TryString(root, key, out string? s))
					{
						return Shorten(s);
					}
				}
			}
			catch (JsonException)
			{
			}
		}
		return Shorten(text);
	}

	/// <summary>服务商说哪个参数不支持（400/422 时用）；认不出返回 null。</summary>
	public static string? UnsupportedParameter(string? body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
				&& TryString(error, "param", out string? param) && TryString(error, "code", out string? code)
				&& code is "unsupported_parameter" or "unsupported_value")
			{
				return param;
			}
		}
		catch (JsonException)
		{
		}
		if (!Has(body, "unsupported parameter") && !Has(body, "unsupported value"))
		{
			return null;
		}
		// 只有文字说明时：取最先出现的、带引号的参数名。
		string? found = null;
		int first = int.MaxValue;
		foreach (string name in AdjustableParameters)
		{
			int at = body.IndexOf("'" + name + "'", StringComparison.OrdinalIgnoreCase);
			if (at >= 0 && at < first)
			{
				found = name;
				first = at;
			}
		}
		return found;
	}

	/// <summary>finish_reason 是 length / max_tokens；没给 finish_reason 时看用掉的输出 token 是否到了上限。</summary>
	public static bool StoppedAtOutputLimit(string? finishReason, int? completionTokens, int maxOutputTokens)
	{
		if (!string.IsNullOrWhiteSpace(finishReason))
		{
			string f = finishReason.Trim();
			return f.Equals("length", StringComparison.OrdinalIgnoreCase) || f.Equals("max_tokens", StringComparison.OrdinalIgnoreCase);
		}
		return completionTokens.HasValue && maxOutputTokens > 0 && completionTokens.Value >= maxOutputTokens;
	}

	internal static string Shorten(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string s = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return s.Length > 160 ? s.Substring(0, 160).TrimEnd() + "…" : s;
	}

	internal static bool Has(string? haystack, string needle) => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

	private static bool TryString(JsonElement obj, string name, out string? value)
	{
		value = null;
		if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
		{
			value = el.GetString();
			return true;
		}
		return false;
	}
}
