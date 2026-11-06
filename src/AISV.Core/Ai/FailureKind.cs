namespace AISV.Core.Ai;

/// <summary>一次 AI 请求为什么没拿到正文。决定要不要换备用模型、给玩家看哪句话。</summary>
public enum FailureKind
{
	None,
	MissingApiKey,
	InvalidApiKey,
	PermissionDenied,
	PaymentRequired,
	ModelNotFound,
	ModelRetired,
	BadRequest,
	ContextTooLong,
	OutputTruncated,
	ContentFiltered,
	RateLimit,
	HighDemand,
	ServerError,
	ServiceUnavailable,
	Timeout,
	Network,
	Other,
	Cancelled,

	/// <summary>服务器：余额（含当天赠送）不够，或全站当天的上限用完了。</summary>
	QuotaExhausted,

	/// <summary>服务器：没登录或登录失效。</summary>
	AccountRequired,

	/// <summary>服务器：账号被停用。</summary>
	AccountBanned,

	/// <summary>服务器：维护中。</summary>
	ServerMaintenance,

	/// <summary>设置里关掉了 AI（EnableAiFeatures = false）。</summary>
	Disabled,

	/// <summary>本机的每日后台上限到了，这个请求没发（不是服务器额度用完）。</summary>
	SkippedDailyLimit
}
