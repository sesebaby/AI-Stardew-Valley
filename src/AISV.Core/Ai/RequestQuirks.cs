using System;

namespace AISV.Core.Ai;

/// <summary>
/// 不同模型对参数的要求不一样（gpt-5 / o 系列要 max_completion_tokens、有的不收 temperature）。
/// 先按模型名猜，服务商回"不支持这个参数"时改一项再试，学到的结果按"地址 + 模型 + 思考档位"记住。
/// </summary>
public readonly record struct RequestQuirks(bool UseMaxCompletionTokens, bool OmitTemperature, string? ReasoningEffort)
{
	public static RequestQuirks Guess(string model)
	{
		string m = model.Trim().ToLowerInvariant();
		bool completionTokens = m.StartsWith("gpt-5", StringComparison.Ordinal) || IsSeries(m, "o1") || IsSeries(m, "o3") || IsSeries(m, "o4");
		string? effort = m.StartsWith("gpt-5.", StringComparison.Ordinal) ? "none" : m.StartsWith("gpt-5", StringComparison.Ordinal) ? "minimal" : null;
		return new RequestQuirks(completionTokens, false, effort);
	}

	/// <summary>按被拒的参数调整一项；这项已经调过（再调也没用）时返回 false。</summary>
	public bool TryAdjust(string parameter, out RequestQuirks adjusted)
	{
		adjusted = this;
		switch (parameter.Trim().ToLowerInvariant())
		{
			case "max_tokens" when !UseMaxCompletionTokens:
				adjusted = this with { UseMaxCompletionTokens = true };
				return true;
			case "max_completion_tokens" when UseMaxCompletionTokens:
				adjusted = this with { UseMaxCompletionTokens = false };
				return true;
			case "reasoning_effort" when ReasoningEffort != null:
				adjusted = this with { ReasoningEffort = null };
				return true;
			case "temperature" when !OmitTemperature:
				adjusted = this with { OmitTemperature = true };
				return true;
			default:
				return false;
		}
	}

	private static bool IsSeries(string name, string prefix) => name == prefix || name.StartsWith(prefix + "-", StringComparison.Ordinal);
}
