using System;

namespace AISV.Core.Ai;

/// <summary>一次请求的结果：要么有正文，要么有失败原因；被输出上限截断时可能带半截正文。</summary>
public sealed class AiResult
{
	private AiResult(string? text, FailureKind failure, string message, string? partialText, bool usedReasoning, TimeSpan? retryAfter)
	{
		Text = text;
		Failure = failure;
		Message = message;
		PartialText = partialText;
		UsedReasoning = usedReasoning;
		RetryAfter = retryAfter;
	}

	public string? Text { get; }

	public FailureKind Failure { get; }

	/// <summary>失败时服务商给的说明（已截短、已隐去服务器地址）。</summary>
	public string Message { get; }

	public string? PartialText { get; }

	/// <summary>模型这次有没有"思考"（之后的请求给输出上限多留余量）。</summary>
	public bool UsedReasoning { get; }

	public TimeSpan? RetryAfter { get; }

	public bool Ok => Failure == FailureKind.None && !string.IsNullOrWhiteSpace(Text);

	public bool WasTruncated => Failure == FailureKind.OutputTruncated;

	public static AiResult Success(string text, bool usedReasoning = false) => new AiResult(text, FailureKind.None, "", null, usedReasoning, null);

	public static AiResult Fail(FailureKind failure, string? message = null, bool usedReasoning = false, TimeSpan? retryAfter = null) => new AiResult(null, failure, message ?? "", null, usedReasoning, retryAfter);

	public static AiResult Truncated(string? partialText, string? message = null, bool usedReasoning = false) => new AiResult(null, FailureKind.OutputTruncated, message ?? "", partialText, usedReasoning, null);
}
