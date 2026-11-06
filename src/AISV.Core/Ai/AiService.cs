using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;

namespace AISV.Core.Ai;

public enum NoticeTone
{
	Normal,
	Warning,
	Error
}

/// <summary>要在聊天框里告诉玩家的一句状态（i18n 键）。</summary>
public sealed record AiNotice(string TextKey, NoticeTone Tone);

/// <summary>对话引擎等子系统发 AI 请求用的接口（测试里换成假的）。</summary>
public interface IAiSender
{
	bool UsesServer { get; }

	/// <summary>回复字数上限：设置值（50～2000），用服务器时不超过服务器给的上限。</summary>
	int ReplyChars { get; }

	/// <summary>用服务器时带进提示词的历史条数上限。</summary>
	int ServerMemoryCap { get; }

	Task<AiResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 所有 AI 请求的入口：选连接（服务器 / 自己的网关）、分槽、每天的后台上限、换备用模型、截断后加大上限重试一次。
/// 不在主线程上调用；结果回到调用方自己的线程。
/// </summary>
public sealed record AiFailureNote(string When, string Request, FailureKind Kind, string Message);

public sealed class AiService : IAiSender
{
	/// <summary>用服务器时的默认上限（和服务器的默认值一样）。</summary>
	public const int DefaultServerBackgroundDaily = 300;

	public const int ConnectionTestTimeoutSeconds = 30;

	private readonly Func<ModConfig> _config;

	private readonly ChatClient _client;

	private readonly ServerConnection _connection;

	private readonly ServerAccount _account;

	private readonly ILog _log;

	private readonly object _lock = new object();

	private int _backgroundToday;

	private int _backgroundSkippedToday;

	private string? _reasoningSeenFor;

	private const int MaxFailureNotes = 6;

	private readonly List<AiFailureNote> _recentFailures = new List<AiFailureNote>();

	public AiService(Func<ModConfig> config, ChatClient client, ServerConnection connection, ServerAccount account, ILog log)
	{
		_config = config;
		_client = client;
		_connection = connection;
		_account = account;
		_log = log;
		client.ServerResponse += account.ObserveAiResponse;
		client.Usage += (_, _, total, _) =>
		{
			lock (_lock)
			{
				TokensToday += total;
				TokensSession += total;
			}
		};
	}

	/// <summary>当前用的是不是 AI星露谷服务器（有 connection.dat，且设置没选"自己的网关"）。</summary>
	public bool UsesServer => _connection.IsAvailable && !string.Equals(_config().AiConnectionMode, "own", StringComparison.OrdinalIgnoreCase);

	public int ReplyChars
	{
		get
		{
			ModConfig c = _config();
			int configured = Math.Clamp(c.ReplyLength <= 0 ? ModConfig.DefaultReplyCharLimit : c.ReplyLength, 50, 2000);
			return UsesServer ? Math.Min(configured, Math.Max(0, _account.Limit("replyChars", 2000))) : configured;
		}
	}

	public int ServerMemoryCap => _account.Limit("memoryEntries", 40);

	public int SucceededToday { get; private set; }

	public int FailedToday { get; private set; }

	public long TokensToday { get; private set; }

	public long TokensSession { get; private set; }

	/// <summary>最近几次失败，新的在前。</summary>
	public IReadOnlyList<AiFailureNote> RecentFailures
	{
		get
		{
			lock (_lock)
			{
				return new List<AiFailureNote>(_recentFailures);
			}
		}
	}

	public FailureKind LastFailure { get; private set; }

	public string LastFailureMessage { get; private set; } = "";

	public (int Used, int Skipped) BackgroundUsage
	{
		get
		{
			lock (_lock)
			{
				return (_backgroundToday, _backgroundSkippedToday);
			}
		}
	}

	/// <summary>每天的后台请求上限。0 = 不限。</summary>
	public int BackgroundDailyLimit => UsesServer ? Math.Max(0, _account.Limit("backgroundDaily", DefaultServerBackgroundDaily)) : _config().NightlyCallsPerDay;

	/// <summary>游戏里新的一天：清当天计数。</summary>
	public void StartNewDay()
	{
		lock (_lock)
		{
			_backgroundToday = 0;
			_backgroundSkippedToday = 0;
			SucceededToday = 0;
			FailedToday = 0;
			TokensToday = 0;
			_noticedToday.Clear();
		}
	}

	public async Task<AiResult> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
	{
		ModConfig config = _config();
		if (!config.AiOn)
		{
			return AiResult.Fail(FailureKind.Disabled);
		}
		bool server = UsesServer;
		AiSlot slot = SlotRouter.Classify(request.Name);
		if (slot == AiSlot.Background && !TryTakeBackgroundSlot(request.Name))
		{
			return AiResult.Fail(FailureKind.SkippedDailyLimit, "background daily limit");
		}
		if (server && !_account.IsLoggedIn)
		{
			// 用服务器但没登录：不发。对话请求会请界面弹登录窗口（后台任务不弹）。
			if (slot == AiSlot.Dialogue)
			{
				_account.RequestLogin();
			}
			RecordFailure(FailureKind.AccountRequired, "", request.Name);
			return AiResult.Fail(FailureKind.AccountRequired);
		}
		IReadOnlyList<string> models = SlotRouter.Models(config, slot, server);
		if (models.Count == 0)
		{
			RecordFailure(FailureKind.ModelNotFound, "no model configured", request.Name);
			return AiResult.Fail(FailureKind.ModelNotFound, "no model configured");
		}
		string reasoningKey = server ? "server|" + SlotRouter.ServerDialogueAlias : "own|" + config.CustomLlmBaseUrl + "|" + config.CustomLlmModel;
		int maxTokens = TokenBudget.ForRequest(request.MaxOutputTokens, config.ReplyTokenCap, string.Equals(_reasoningSeenFor, reasoningKey, StringComparison.OrdinalIgnoreCase), request.Name);
		bool connectionTest = TokenBudget.IsConnectionTest(maxTokens, request.Name);
		// 连接测试固定 30 秒（不受设置影响）。
		int attemptTimeout = connectionTest ? ConnectionTestTimeoutSeconds : SlotRouter.TimeoutSeconds(config, slot);
		int totalBudget = connectionTest ? attemptTimeout : SlotRouter.TotalBudgetSeconds(config, slot);
		string? effort = SlotRouter.ReasoningEffort(config, slot);
		Stopwatch elapsed = Stopwatch.StartNew();
		AiResult? last = null;
		for (int i = 0; i < models.Count; i++)
		{
			int remaining = totalBudget - (int)elapsed.Elapsed.TotalSeconds;
			if (i > 0 && remaining < 8)
			{
				_log.Debug($"{request.Name}：剩下的时间不够再试备用模型 {models[i]}。");
				break;
			}
			ChatEndpoint endpoint = Endpoint(config, server, slot, models[i], effort, request.Name);
			int timeout = Math.Max(5, Math.Min(attemptTimeout, remaining));
			last = await SendWithTruncationRetryAsync(endpoint, request with { MaxOutputTokens = maxTokens }, timeout, config, reasoningKey, connectionTest, cancellationToken).ConfigureAwait(false);
			if (last.Ok)
			{
				if (i > 0)
				{
					_log.Info($"{request.Name} 由备用模型 {models[i]} 回答。");
				}
				lock (_lock)
				{
					SucceededToday++;
				}
				NoteGateway(FailureKind.None);
				return last;
			}
			if (cancellationToken.IsCancellationRequested || connectionTest || !SlotRouter.ShouldTryFallback(last.Failure))
			{
				break;
			}
			if (i + 1 < models.Count)
			{
				_log.Warn($"{request.Name}：模型 {models[i]} 失败（{last.Failure}），换备用模型 {models[i + 1]}。");
			}
		}
		if (last != null)
		{
			NoteGateway(last.Failure);
			// 服务器说没登录或被封号：清掉本机令牌，别再带着它发请求。
			if (server && last.Failure is FailureKind.AccountBanned or FailureKind.AccountRequired)
			{
				_account.SessionExpired();
			}
			if (server)
			{
				NoteServerFailure(last.Failure);
			}
			if (last.Failure != FailureKind.Cancelled)
			{
				RecordFailure(last.Failure, last.Message, request.Name);
			}
		}
		return last ?? AiResult.Fail(FailureKind.Other);
	}

	/// <summary>网关连续 3 次连不上后，60 秒内当作离线（对话直接用游戏原来的台词，不让玩家干等）；成功一次就恢复。</summary>
	public bool IsGatewayOffline
	{
		get
		{
			lock (_lock)
			{
				return _networkFailures >= GatewayFailureThreshold && DateTime.UtcNow < _gatewayRetryAt;
			}
		}
	}

	private const int GatewayFailureThreshold = 3;

	private static readonly TimeSpan GatewayCooldown = TimeSpan.FromSeconds(60);

	private int _networkFailures;

	private DateTime _gatewayRetryAt = DateTime.MinValue;

	/// <summary>只有成功才清零；已经离线时不重新计时。</summary>
	private void NoteGateway(FailureKind failure)
	{
		bool wentOffline = false;
		lock (_lock)
		{
			if (failure == FailureKind.Network)
			{
				_networkFailures++;
				if (_networkFailures >= GatewayFailureThreshold && DateTime.UtcNow >= _gatewayRetryAt)
				{
					_gatewayRetryAt = DateTime.UtcNow + GatewayCooldown;
					wentOffline = true;
				}
			}
			else if (failure == FailureKind.None)
			{
				_networkFailures = 0;
				_gatewayRetryAt = DateTime.MinValue;
			}
		}
		if (wentOffline)
		{
			_log.Warn("连不上 AI 网关，60 秒内村民改说游戏原来的台词。");
			Notify("gateway_offline", "status.gatewayOffline", NoticeTone.Error);
		}
	}

	// ---- 给玩家的状态提示（聊天框里一行，每种每天一次）----

	private readonly List<AiNotice> _notices = new List<AiNotice>();

	private readonly HashSet<string> _noticedToday = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>取走还没显示的提示（主线程显示）。</summary>
	public IReadOnlyList<AiNotice> TakeNotices()
	{
		lock (_lock)
		{
			AiNotice[] list = _notices.ToArray();
			_notices.Clear();
			return list;
		}
	}

	private void Notify(string onceKey, string textKey, NoticeTone tone)
	{
		lock (_lock)
		{
			if (_noticedToday.Add(onceKey))
			{
				_notices.Add(new AiNotice(textKey, tone));
			}
		}
	}

	/// <summary>服务器拒绝时的一句话：额度用完、账号停用、维护中。</summary>
	private void NoteServerFailure(FailureKind failure)
	{
		switch (failure)
		{
			case FailureKind.QuotaExhausted:
				Notify("server_quota", "status.serverQuota", NoticeTone.Warning);
				break;
			case FailureKind.AccountBanned:
				Notify("server_banned", "status.serverBanned", NoticeTone.Error);
				break;
			case FailureKind.ServerMaintenance:
				Notify("server_maintenance", "status.serverMaintenance", NoticeTone.Warning);
				break;
		}
	}

	/// <summary>
	/// 现在能不能用 AI 说话：总开关关了、自己的网关没填地址或模型、用服务器但没登录（会请界面弹登录窗口）、网关离线中，都算不能。
	/// </summary>
	public bool IsDialogueUnavailable()
	{
		ModConfig config = _config();
		if (!config.AiOn)
		{
			return true;
		}
		if (!UsesServer && (string.IsNullOrWhiteSpace(config.CustomLlmBaseUrl) || string.IsNullOrWhiteSpace(config.CustomLlmModel)))
		{
			return true;
		}
		if (UsesServer && !_account.IsLoggedIn)
		{
			_account.RequestLogin();
			return true;
		}
		return IsGatewayOffline;
	}

	/// <summary>最后一次失败的时间（UTC）。</summary>
	public DateTime LastFailureUtc { get; private set; } = DateTime.MinValue;

	/// <summary>从某个时间起有没有失败过（含网关离线）：失败时退回游戏原来的台词。</summary>
	public bool FailedSince(DateTime sinceUtc) => LastFailureUtc >= sinceUtc || IsGatewayOffline;

	private ChatEndpoint Endpoint(ModConfig config, bool server, AiSlot slot, string model, string? effort, string requestName)
	{
		if (!server)
		{
			return new ChatEndpoint(config.CustomLlmBaseUrl ?? "", config.CustomLlmApiKey ?? "", model, effort, config.CustomLlmSupportsSystemPrompt, IsServer: false);
		}
		// 告诉服务器：玩家自己挑的对话模型和思考档位（服务器会再核对）；必需的后台请求不算进每天的后台次数。
		Dictionary<string, string> headers = new Dictionary<string, string>();
		if (slot == AiSlot.Dialogue)
		{
			string pick = PickedModel(config);
			string thinking = (config.ServerThinkingLevel ?? "").Trim().ToLowerInvariant();
			if (pick.Length > 0)
			{
				headers["X-AISV-Model"] = pick;
			}
			if (thinking.Length > 0)
			{
				headers["X-AISV-Thinking"] = thinking;
			}
		}
		else if (SlotRouter.IsEssential(requestName))
		{
			headers["X-AISV-Essential"] = "1";
		}
		return new ChatEndpoint(_connection.ChatBaseUrl, _account.Token, model, effort, config.CustomLlmSupportsSystemPrompt, IsServer: true, headers);
	}

	/// <summary>玩家挑的对话模型；已经不在服务器的可选列表里时当没挑。</summary>
	private string PickedModel(ModConfig config)
	{
		string m = (config.ServerChatModel ?? "").Trim();
		if (m.Length == 0)
		{
			return "";
		}
		foreach (PlayerModel p in _account.PlayerModels)
		{
			if (string.Equals(p.Id, m, StringComparison.OrdinalIgnoreCase))
			{
				return p.Id;
			}
		}
		return "";
	}

	private async Task<AiResult> SendWithTruncationRetryAsync(ChatEndpoint endpoint, ChatRequest request, int timeout, ModConfig config, string reasoningKey, bool connectionTest, CancellationToken ct)
	{
		AiResult first = await _client.SendAsync(endpoint, request, timeout, isRetry: false, ct).ConfigureAwait(false);
		NoteReasoning(first, reasoningKey);
		if (!first.WasTruncated)
		{
			return first;
		}
		if (connectionTest)
		{
			return string.IsNullOrWhiteSpace(first.PartialText) ? first : AiResult.Success(first.PartialText);
		}
		int retryTokens = TokenBudget.ForRetry(request.MaxOutputTokens, config.ReplyTokenCap, request.Name);
		if (retryTokens <= request.MaxOutputTokens)
		{
			_log.Warn($"{request.Name} 在 {request.MaxOutputTokens} 个输出 token 处被截断，已经是上限，不再重试。经常这样的话调高 LlmResponseTokenBudget。");
			return AiResult.Fail(FailureKind.OutputTruncated, null, first.UsedReasoning);
		}
		_log.Info($"{request.Name} 在 {request.MaxOutputTokens} 个输出 token 处被截断，用 {retryTokens} 重试一次。");
		AiResult second = await _client.SendAsync(endpoint, request with { MaxOutputTokens = retryTokens }, timeout, isRetry: true, ct).ConfigureAwait(false);
		NoteReasoning(second, reasoningKey);
		if (!second.WasTruncated)
		{
			return second;
		}
		_log.Warn($"{request.Name} 在 {retryTokens} 个输出 token 处又被截断，放弃。");
		return AiResult.Fail(FailureKind.OutputTruncated, null, second.UsedReasoning);
	}

	private void NoteReasoning(AiResult result, string key)
	{
		if (result.UsedReasoning && !string.Equals(_reasoningSeenFor, key, StringComparison.OrdinalIgnoreCase))
		{
			_reasoningSeenFor = key;
			_log.Debug("模型这次有思考过程，之后的请求给输出上限多留余量。");
		}
	}

	/// <summary>后台每天上限：到了以后只放行必需的请求。</summary>
	private bool TryTakeBackgroundSlot(string requestName)
	{
		int limit = BackgroundDailyLimit;
		lock (_lock)
		{
			if (limit > 0 && _backgroundToday >= limit && !SlotRouter.IsEssential(requestName))
			{
				_backgroundSkippedToday++;
				if (_backgroundSkippedToday == 1)
				{
					_log.Info($"今天的后台 AI 请求到了上限（{limit}），剩下的时间只发必需的。");
				}
				_log.Trace("跳过后台请求 " + requestName + "（每日上限）。");
				return false;
			}
			_backgroundToday++;
			return true;
		}
	}

	private void RecordFailure(FailureKind failure, string message, string request = "")
	{
		lock (_lock)
		{
			_recentFailures.Insert(0, new AiFailureNote(DateTime.Now.ToString("HH:mm"), request, failure, message));
			if (_recentFailures.Count > MaxFailureNotes)
			{
				_recentFailures.RemoveAt(_recentFailures.Count - 1);
			}
			FailedToday++;
			LastFailureUtc = DateTime.UtcNow;
			LastFailure = failure;
			LastFailureMessage = message;
		}
	}
}
