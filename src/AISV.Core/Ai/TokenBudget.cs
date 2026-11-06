using System;

namespace AISV.Core.Ai;

/// <summary>每次请求的输出 token 上限：不超过设置里的天花板；模型会"思考"时多留一截；被截断时翻倍重试一次。</summary>
public static class TokenBudget
{
	public const int MinCeiling = 4096;

	public const int DefaultCeiling = 12000;

	public const int MaxCeiling = 20000;

	private const int EverydayRequest = 6000;

	private const int ThinkingRoom = 4000;

	private const int RetryGrowth = 2;

	public const string ConnectionTestRequest = "diagnostic_connection_test";

	public static int Ceiling(int configured) => Math.Clamp(configured <= 0 ? DefaultCeiling : configured, MinCeiling, MaxCeiling);

	/// <summary>很小的上限（≤128）或专门的连接测试：原样发，不重试、不换模型。</summary>
	public static bool IsConnectionTest(int maxOutputTokens, string? requestName) => maxOutputTokens <= 128 || string.Equals(requestName, ConnectionTestRequest, StringComparison.OrdinalIgnoreCase);

	public static int ForRequest(int requested, int configuredCeiling, bool expectReasoning, string? requestName)
	{
		if (IsConnectionTest(int.MaxValue, requestName) || (requested > 0 && IsConnectionTest(requested, requestName)))
		{
			return requested;
		}
		int wanted = requested > 0 ? requested : EverydayRequest;
		if (expectReasoning)
		{
			wanted += Math.Max(ThinkingRoom, wanted / 2);
		}
		return Math.Min(wanted, Ceiling(configuredCeiling));
	}

	/// <summary>被截断后重试用的上限；已经到天花板时返回原值（表示不必重试）。</summary>
	public static int ForRetry(int used, int configuredCeiling, string? requestName)
	{
		if (used <= 0 || IsConnectionTest(used, requestName))
		{
			return used;
		}
		int ceiling = Ceiling(configuredCeiling);
		return used >= ceiling ? used : (int)Math.Min((long)used * RetryGrowth, ceiling);
	}
}
