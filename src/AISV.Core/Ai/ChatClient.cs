using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;

namespace AISV.Core.Ai;

/// <summary>要发的一段对话。Name 是请求名（决定槽位、日志里认得出来）。</summary>
public sealed record ChatRequest(string Name, string System, string User, int MaxOutputTokens, double Temperature);

/// <summary>发到哪：地址、key、模型、思考档位（null = 按模型名猜，"" = 不传）。</summary>
public sealed record ChatEndpoint(string BaseUrl, string ApiKey, string Model, string? ReasoningEffort, bool SupportsSystemPrompt, bool IsServer, IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// OpenAI 兼容的 /chat/completions 客户端：发一次（参数不被支持时调整后最多再试 3 次），把回复变成 <see cref="AiResult"/>。
/// 换备用模型、截断重试、每日上限在 <see cref="AiService"/> 里。
/// </summary>
public sealed class ChatClient
{
	private const int MaxParameterRetries = 3;

	private const string EmptyResponse = "empty response";

	private const int MaxLogBody = 4000;

	private static readonly Regex ThinkBlock = new Regex("<think>.*?(</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private readonly HttpClient _http;

	private readonly Func<ModConfig> _config;

	private readonly ILog _log;

	private readonly Func<string?, string> _mask;

	private readonly ConcurrentDictionary<string, RequestQuirks> _quirks = new ConcurrentDictionary<string, RequestQuirks>();

	public ChatClient(HttpClient http, Func<ModConfig> config, ILog log, Func<string?, string>? mask = null)
	{
		_http = http;
		_config = config;
		_log = log;
		_mask = mask ?? (s => s ?? "");
	}

	/// <summary>服务器回的每个响应（成功时 errorBody 为 null）：账号据此更新余额、发现登录失效。</summary>
	public event Action<HttpResponseMessage, string?>? ServerResponse;

	/// <summary>用量：输入、输出、合计 token，是不是截断后的重试。</summary>
	public event Action<int, int, int, bool>? Usage;

	public void ForgetLearnedQuirks() => _quirks.Clear();

	public async Task<AiResult> SendAsync(ChatEndpoint endpoint, ChatRequest request, int timeoutSeconds, bool isRetry, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
		{
			_log.Error("AI 网关地址是空的。");
			return AiResult.Fail(FailureKind.BadRequest, "base URL is empty");
		}
		if (string.IsNullOrWhiteSpace(endpoint.Model))
		{
			_log.Error("没有设置 AI 模型。");
			return AiResult.Fail(FailureKind.ModelNotFound, "model is empty");
		}
		ModConfig config = _config();
		string model = endpoint.Model.Trim();
		try
		{
			Uri url = ChatUrl(endpoint.BaseUrl);
			string quirksKey = (url.AbsoluteUri + "|" + model + "|" + (endpoint.ReasoningEffort ?? "auto")).ToLowerInvariant();
			RequestQuirks quirks = _quirks.GetOrAdd(quirksKey, _ =>
			{
				RequestQuirks guess = RequestQuirks.Guess(model);
				return endpoint.ReasoningEffort == null ? guess : guess with { ReasoningEffort = endpoint.ReasoningEffort.Length == 0 ? null : endpoint.ReasoningEffort };
			});
			bool askedAgain = false;
			for (int attempt = 0; ; attempt++)
			{
				string body = BuildBody(model, endpoint.SupportsSystemPrompt, request, quirks);
				using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
				if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
				{
					message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey.Trim());
				}
				if (endpoint.Headers != null)
				{
					foreach (KeyValuePair<string, string> h in endpoint.Headers)
					{
						message.Headers.TryAddWithoutValidation(h.Key, h.Value);
					}
				}
				// 提示词里有存档里的对话和八卦：只有打开 LogFullAiTraffic 才整段写进日志。
				if (config.LogAiTraffic)
				{
					_log.Trace("AI 请求：" + ForLog(body));
				}
				else if (config.Verbose)
				{
					_log.Debug($"AI 请求：{request.Name} 模型={model} 思考={quirks.ReasoningEffort ?? "-"} 正文 {body.Length} 字");
				}
				using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 30 : timeoutSeconds, 5, 500)));
				using HttpResponseMessage response = await _http.SendAsync(message, cts.Token).ConfigureAwait(false);
				string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				if (endpoint.IsServer)
				{
					ServerResponse?.Invoke(response, response.IsSuccessStatusCode ? null : text);
				}
				if (response.IsSuccessStatusCode)
				{
					AiResult result = ReadSuccess(text, request, isRetry, config);
					// 有的模型偶尔把话全写进思考里、正文空着：再问一次。
					if (!askedAgain && result.Failure == FailureKind.Other && result.Message == EmptyResponse)
					{
						askedAgain = true;
						_log.Info($"{request.Name} 回了空内容，再问一次。");
						continue;
					}
					return result;
				}
				int status = (int)response.StatusCode;
				if (attempt < MaxParameterRetries && status is 400 or 422
					&& ProviderErrors.UnsupportedParameter(text) is string param && quirks.TryAdjust(param, out RequestQuirks adjusted))
				{
					_log.Info($"服务商不接受参数 '{param}'（模型 {model}），调整后重试。");
					quirks = adjusted;
					_quirks[quirksKey] = adjusted;
					continue;
				}
				_log.Error($"AI 请求出错（{status}）：{_mask(ForLog(text))}");
				FailureKind failure = ProviderErrors.Classify(response.StatusCode, text);
				return AiResult.Fail(failure, _mask(ProviderErrors.Message(text)), retryAfter: failure == FailureKind.RateLimit ? RetryAfter(response) : null);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// 调用方取消（玩家关了菜单、看门狗）：不是超时，不换备用模型。
			return AiResult.Fail(FailureKind.Cancelled, "cancelled");
		}
		catch (OperationCanceledException)
		{
			_log.Warn($"AI 请求超时（{request.Name}，模型 {model}）。");
			return AiResult.Fail(FailureKind.Timeout);
		}
		catch (HttpRequestException ex)
		{
			_log.Warn("AI 网络错误：" + _mask(ex.Message));
			return AiResult.Fail(FailureKind.Network, ProviderErrors.Shorten(_mask(ex.Message)));
		}
		catch (Exception ex)
		{
			// 兜底：调用方永远拿到结果，不会收到异常。
			_log.Error("AI 请求失败：" + _mask(ex.Message));
			return AiResult.Fail(FailureKind.Other, ProviderErrors.Shorten(_mask(ex.Message)));
		}
	}

	private AiResult ReadSuccess(string text, ChatRequest request, bool isRetry, ModConfig config)
	{
		if (config.LogAiTraffic)
		{
			_log.Trace("AI 回复：" + ForLog(text));
		}
		using JsonDocument doc = JsonDocument.Parse(text);
		JsonElement root = doc.RootElement;
		JsonElement? choice = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 ? choices[0] : null;
		JsonElement? msg = choice is JsonElement c && c.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.Object ? m : null;
		string? content = Str(msg, "content") ?? Str(choice, "text");
		string? finish = Str(choice, "finish_reason");
		int? completionTokens = null;
		bool reasoningTokens = false;
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
		{
			int prompt = Int(usage, "prompt_tokens");
			int completion = Int(usage, "completion_tokens");
			int total = Int(usage, "total_tokens");
			completionTokens = completion;
			Usage?.Invoke(prompt, completion, total > 0 ? total : prompt + completion, isRetry);
			reasoningTokens = usage.TryGetProperty("completion_tokens_details", out JsonElement details) && details.ValueKind == JsonValueKind.Object && Int(details, "reasoning_tokens") > 0;
			if (completion > 0 && request.MaxOutputTokens > 0 && completion >= request.MaxOutputTokens * 0.9)
			{
				_log.Debug($"{request.Name} 用了 {completion}/{request.MaxOutputTokens} 个输出 token，快到上限了。");
			}
		}
		string? answer = StripThinking(content);
		// 看的是去掉思考块之后的正文：模型只是输出了 <think> 标签不算"用了思考"。
		bool usedReasoning = reasoningTokens || !string.IsNullOrWhiteSpace(Str(msg, "reasoning_content")) || !string.IsNullOrWhiteSpace(Str(msg, "reasoning"))
			|| ProviderErrors.Has(answer, "<think>");
		bool cut = ProviderErrors.StoppedAtOutputLimit(finish, completionTokens, request.MaxOutputTokens);
		if (string.IsNullOrWhiteSpace(answer))
		{
			if (ProviderErrors.Has(text, "content_filter") || ProviderErrors.Has(text, "\"safety\""))
			{
				_log.Warn("AI 服务商的内容过滤拦下了这次请求。");
				return AiResult.Fail(FailureKind.ContentFiltered, "content filter", usedReasoning);
			}
			if (cut)
			{
				_log.Warn($"{request.Name} 还没写出正文就到了输出上限（{request.MaxOutputTokens}）。");
				return AiResult.Truncated(null, "finish_reason=" + (finish ?? "?"), usedReasoning);
			}
			_log.Warn("AI 回了空内容。");
			return AiResult.Fail(FailureKind.Other, EmptyResponse, usedReasoning);
		}
		if (cut)
		{
			_log.Warn($"{request.Name} 的回复在输出上限处被截断（finish_reason={finish ?? "?"}，{completionTokens ?? 0}/{request.MaxOutputTokens}）。");
			return AiResult.Truncated(answer.Trim(), "finish_reason=" + (finish ?? "?"), usedReasoning);
		}
		return AiResult.Success(answer.Trim(), usedReasoning);
	}

	internal static string BuildBody(string model, bool supportsSystemPrompt, ChatRequest request, RequestQuirks quirks)
	{
		JsonArray messages = supportsSystemPrompt
			? new JsonArray(Message("system", request.System), Message("user", request.User))
			: new JsonArray(Message("user", request.System + "\n\nUSER REQUEST:\n" + request.User));
		JsonObject body = new JsonObject { ["model"] = model, ["messages"] = messages };
		if (!quirks.OmitTemperature)
		{
			body["temperature"] = request.Temperature;
		}
		body[quirks.UseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = request.MaxOutputTokens;
		if (quirks.ReasoningEffort != null)
		{
			body["reasoning_effort"] = quirks.ReasoningEffort;
		}
		body["stream"] = false;
		return body.ToJsonString();
	}

	/// <summary>地址可以写到 /v1、写全 /chat/completions，或只写主机。</summary>
	internal static Uri ChatUrl(string baseUrl)
	{
		string s = baseUrl.Trim().TrimEnd('/');
		if (s.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
		{
			return new Uri(s);
		}
		return new Uri(s.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? s + "/chat/completions" : s + "/v1/chat/completions");
	}

	/// <summary>去掉正文里的 &lt;think&gt;…&lt;/think&gt;（没闭合的也去掉）。</summary>
	internal static string? StripThinking(string? text)
	{
		if (string.IsNullOrEmpty(text) || text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text;
		}
		return ThinkBlock.Replace(text, "").Trim();
	}

	private static TimeSpan? RetryAfter(HttpResponseMessage response)
	{
		RetryConditionHeaderValue? r = response.Headers.RetryAfter;
		if (r?.Delta is TimeSpan d && d > TimeSpan.Zero)
		{
			return d;
		}
		if (r?.Date is DateTimeOffset at && at - DateTimeOffset.UtcNow is TimeSpan left && left > TimeSpan.Zero)
		{
			return left;
		}
		return null;
	}

	private static JsonObject Message(string role, string content) => new JsonObject { ["role"] = role, ["content"] = content };

	private static string? Str(JsonElement? obj, string name) => obj is JsonElement o && o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	private static int Int(JsonElement obj, string name) => obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

	private static string ForLog(string? text) => string.IsNullOrEmpty(text) ? "" : text.Length <= MaxLogBody ? text : text.Substring(0, MaxLogBody) + $"…（共 {text.Length} 字）";
}
