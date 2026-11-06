using System;
using System.Collections.Generic;
using AISV.Core.Ai;
using AISV.Core.Text;

namespace AISV.UI.Dialogue;

/// <summary>把 AI 请求失败的原因说成玩家看得懂的一句话（服务商给的说明有用时附在后面）。</summary>
public static class FailureText
{
	/// <summary>只复述状态码的说明，没有多的信息，不显示。</summary>
	private static readonly HashSet<string> Useless = new HashSet<string>(
		"error|unknown error|null|none|gone|forbidden|unauthorized|unauthorised|not found|notfound|bad request|badrequest|bad gateway|gateway timeout|request timeout|too many requests|service unavailable|internal server error|payment required|payload too large|unprocessable entity".Split('|'),
		StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<FailureKind, Func<string>> Messages = new Dictionary<FailureKind, Func<string>>
	{
		[FailureKind.MissingApiKey] = () => Tr.Get("failure.missingKey", "还没填 AI 网关的 key。"),
		[FailureKind.InvalidApiKey] = () => Tr.Get("failure.invalidKey", "AI 网关不认这个 key，检查一下设置。"),
		[FailureKind.PermissionDenied] = () => Tr.Get("failure.permission", "这个 key 没有权限用这个模型。"),
		[FailureKind.PaymentRequired] = () => Tr.Get("failure.payment", "AI 网关那边余额不够了。"),
		[FailureKind.ModelNotFound] = () => Tr.Get("failure.modelNotFound", "找不到设置里的对话模型，换一个试试。"),
		[FailureKind.ModelRetired] = () => Tr.Get("failure.modelRetired", "这个模型已经下线了，换一个吧。"),
		[FailureKind.BadRequest] = () => Tr.Get("failure.badRequest", "AI 网关说请求不对，详情在 SMAPI 日志里。"),
		[FailureKind.ContextTooLong] = () => Tr.Get("failure.tooLong", "这次要带给 AI 的内容太长了。"),
		[FailureKind.OutputTruncated] = () => Tr.Get("failure.truncated", "AI 的回答被输出上限截断了，可以在设置里调高 token 上限。"),
		[FailureKind.ContentFiltered] = () => Tr.Get("failure.filtered", "AI 拒绝回答这句（内容过滤）。"),
		[FailureKind.RateLimit] = () => Tr.Get("failure.rateLimit", "请求太频繁，歇一会儿再试。"),
		[FailureKind.HighDemand] = () => Tr.Get("failure.busy", "AI 那边现在太忙，过一会儿再试。"),
		[FailureKind.ServerError] = () => Tr.Get("failure.serverError", "AI 网关出错了，稍后再试。"),
		[FailureKind.ServiceUnavailable] = () => Tr.Get("failure.unavailable", "AI 服务暂时用不了。"),
		[FailureKind.Timeout] = () => Tr.Get("failure.timeout", "等太久没回音，超时了。"),
		[FailureKind.Network] = () => Tr.Get("failure.network", "连不上 AI 网关，看看网络或网关有没有开。"),
		[FailureKind.QuotaExhausted] = () => Tr.Get("failure.quota", "今天的 AI 额度用完了，明天会再送。"),
		[FailureKind.AccountRequired] = () => Tr.Get("failure.account", "还没登录 AI星露谷 账号。"),
		[FailureKind.AccountBanned] = () => Tr.Get("failure.banned", "AI星露谷 账号已停用。"),
		[FailureKind.ServerMaintenance] = () => Tr.Get("failure.maintenance", "AI星露谷 服务器在维护。"),
		[FailureKind.Disabled] = () => Tr.Get("failure.disabled", "AI 功能在设置里关掉了。")
	};

	public static string Describe(FailureKind kind, string? detail)
	{
		string text = Messages.TryGetValue(kind, out Func<string>? message) ? message() : Tr.Get("failure.other", "AI 没有回应，详情在 SMAPI 日志里。");
		string d = (detail ?? "").Trim().TrimEnd('.');
		return d.Length == 0 || Useless.Contains(d) || SameMeaning(text, d) ? text : text + "\n" + detail!.Trim();
	}

	/// <summary>服务器的说明和我们的提示是同一句话（只差空格、标点）时不重复。</summary>
	internal static bool SameMeaning(string a, string b)
	{
		string x = Letters(a), y = Letters(b);
		if (x.Length == 0 || y.Length == 0)
		{
			return false;
		}
		return x == y || (Math.Min(x.Length, y.Length) >= 6 && (x.Contains(y, StringComparison.OrdinalIgnoreCase) || y.Contains(x, StringComparison.OrdinalIgnoreCase)));
	}

	private static string Letters(string s)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s)
		{
			if (char.IsLetterOrDigit(c))
			{
				sb.Append(char.ToLowerInvariant(c));
			}
		}
		return sb.ToString();
	}
}
