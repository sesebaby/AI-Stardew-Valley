using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using Xunit;

namespace AISV.Core.Tests;

public class AiTests
{
	private static ChatRequest Req(string name = "npc_dialogue", int max = 1000) => new ChatRequest(name, "SYS", "USER", max, 0.8);

	private static (AiService Service, FakeHttp Http, ServerAccount Account) Own(ModConfig? config = null)
	{
		config ??= new ModConfig { AiConnectionMode = "own", CustomLlmBaseUrl = "http://gw.test/v1", CustomLlmApiKey = "k" };
		FakeHttp fake = new FakeHttp();
		HttpClient http = new HttpClient(fake);
		ServerAccount account = new ServerAccount(ServerConnection.None, new MemoryAccountStore(), http, NullLog.Instance);
		ChatClient client = new ChatClient(http, () => config, NullLog.Instance);
		return (new AiService(() => config, client, ServerConnection.None, account, NullLog.Instance), fake, account);
	}

	private static (AiService Service, FakeHttp Http, ServerAccount Account, MemoryAccountStore Store) Server(ModConfig? config = null)
	{
		config ??= new ModConfig();
		ServerConnection connection = ServerConnection.Parse("{\"u\":\"http://srv.test:8080/v1\",\"n\":\"测试包\"}")!;
		FakeHttp fake = new FakeHttp();
		HttpClient http = new HttpClient(fake);
		MemoryAccountStore store = new MemoryAccountStore();
		ServerAccount account = new ServerAccount(connection, store, http, NullLog.Instance);
		ChatClient client = new ChatClient(http, () => config, NullLog.Instance, connection.Mask);
		return (new AiService(() => config, client, connection, account, NullLog.Instance), fake, account, store);
	}

	[Fact]
	public void ConnectionFileRoundTripsAndStripsV1()
	{
		string encoded = ServerConnection.Encode("{\"u\":\"https://a.example:9000/v1/\",\"n\":\"包\"}");
		ServerConnection c = ServerConnection.Parse(ServerConnection.Decode(encoded))!;
		Assert.Equal("https://a.example:9000", c.Root);
		Assert.Equal("https://a.example:9000/v1", c.ChatBaseUrl);
		Assert.Equal("包", c.Label);
		Assert.Equal("连不上 服务器 和 服务器", c.Mask("连不上 a.example:9000 和 a.example"));
	}

	[Fact]
	public void ConnectionFileMatchesPackScriptAlgorithm()
	{
		// 打包时的算法：异或 SHA256(种子)[i%32] 和 (byte)(i*31)。第 0 字节只异或 pad[0]。
		byte[] pad = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("AIStardewValley.Core/connection"));
		byte[] raw = Convert.FromBase64String(ServerConnection.Encode("{"));
		Assert.Equal((byte)('{' ^ pad[0]), raw[0]);
	}

	[Theory]
	[InlineData(400, "{\"error\":{\"message\":\"maximum context length is 8k\"}}", FailureKind.ContextTooLong)]
	[InlineData(402, "{\"error\":{\"code\":\"aisv_insufficient_balance\"}}", FailureKind.QuotaExhausted)]
	[InlineData(401, "{\"error\":{\"code\":\"aisv_session_invalid\"}}", FailureKind.AccountRequired)]
	[InlineData(403, "{\"error\":{\"code\":\"aisv_banned\"}}", FailureKind.AccountBanned)]
	[InlineData(503, "{\"error\":{\"code\":\"aisv_maintenance\"}}", FailureKind.ServerMaintenance)]
	[InlineData(400, "invalid api key", FailureKind.InvalidApiKey)]
	[InlineData(400, "model foo does not exist", FailureKind.ModelNotFound)]
	[InlineData(400, "bad", FailureKind.BadRequest)]
	[InlineData(403, "nope", FailureKind.PermissionDenied)]
	[InlineData(404, "", FailureKind.ModelNotFound)]
	[InlineData(410, "", FailureKind.ModelRetired)]
	[InlineData(429, "", FailureKind.RateLimit)]
	[InlineData(503, "model loading", FailureKind.HighDemand)]
	[InlineData(502, "", FailureKind.ServiceUnavailable)]
	[InlineData(501, "", FailureKind.ServerError)]
	[InlineData(418, "", FailureKind.Other)]
	public void ClassifiesErrors(int status, string body, FailureKind expected)
	{
		Assert.Equal(expected, ProviderErrors.Classify((HttpStatusCode)status, body));
	}

	[Fact]
	public void ReadsUnsupportedParameterFromJsonAndText()
	{
		Assert.Equal("temperature", ProviderErrors.UnsupportedParameter("{\"error\":{\"param\":\"temperature\",\"code\":\"unsupported_value\"}}"));
		Assert.Equal("max_tokens", ProviderErrors.UnsupportedParameter("Unsupported parameter: 'max_tokens' is not supported, use 'max_completion_tokens'"));
		Assert.Null(ProviderErrors.UnsupportedParameter("{\"error\":{\"message\":\"other\"}}"));
	}

	[Fact]
	public void GuessesQuirksByModelName()
	{
		Assert.Equal(new RequestQuirks(true, false, "none"), RequestQuirks.Guess("gpt-5.1-mini"));
		Assert.Equal(new RequestQuirks(true, false, "minimal"), RequestQuirks.Guess("GPT-5"));
		Assert.Equal(new RequestQuirks(true, false, null), RequestQuirks.Guess("o3-mini"));
		Assert.Equal(new RequestQuirks(false, false, null), RequestQuirks.Guess("o30"));
		Assert.Equal(new RequestQuirks(false, false, null), RequestQuirks.Guess("cn:deepseek-v4.1-flash"));
	}

	[Fact]
	public void BuildsRequestBodyLikeOriginal()
	{
		string body = ChatClient.BuildBody("m", true, Req(max: 300), new RequestQuirks(false, false, "low"));
		JsonObject o = JsonNode.Parse(body)!.AsObject();
		Assert.Equal("system", o["messages"]![0]!["role"]!.GetValue<string>());
		Assert.Equal(300, o["max_tokens"]!.GetValue<int>());
		Assert.Equal("low", o["reasoning_effort"]!.GetValue<string>());
		Assert.False(o["stream"]!.GetValue<bool>());
		string merged = ChatClient.BuildBody("m", false, Req(), new RequestQuirks(true, true, null));
		JsonObject p = JsonNode.Parse(merged)!.AsObject();
		Assert.Equal("SYS\n\nUSER REQUEST:\nUSER", p["messages"]![0]!["content"]!.GetValue<string>());
		Assert.Null(p["temperature"]);
		Assert.NotNull(p["max_completion_tokens"]);
		Assert.Null(p["reasoning_effort"]);
	}

	[Theory]
	[InlineData("http://h/v1", "http://h/v1/chat/completions")]
	[InlineData("http://h/v1/chat/completions/", "http://h/v1/chat/completions")]
	[InlineData("http://h", "http://h/v1/chat/completions")]
	public void BuildsChatUrl(string baseUrl, string expected) => Assert.Equal(expected, ChatClient.ChatUrl(baseUrl).ToString());

	[Fact]
	public void StripsThinkBlocks()
	{
		Assert.Equal("你好", ChatClient.StripThinking("<think>嗯……</think>你好"));
		Assert.Equal("", ChatClient.StripThinking("<THINK>没写完"));
	}

	[Fact]
	public void RoutesSlotsAndTimeouts()
	{
		ModConfig c = new ModConfig { CustomLlmModel = "a", ChatBackupModels = "b，a; c\nd", NightlyWaitSeconds = 0, ChatWaitSeconds = 999 };
		Assert.Equal(AiSlot.Background, SlotRouter.Classify("Gossip_Analysis"));
		Assert.Equal(AiSlot.Dialogue, SlotRouter.Classify("something_new"));
		Assert.Equal(new[] { "a", "b", "c" }, SlotRouter.Models(c, AiSlot.Dialogue, useServer: false));
		Assert.Equal(new[] { "aisv-background" }, SlotRouter.Models(c, AiSlot.Background, useServer: true));
		Assert.Equal(90, SlotRouter.TimeoutSeconds(c, AiSlot.Background));
		Assert.Equal(300, SlotRouter.TimeoutSeconds(c, AiSlot.Dialogue));
		Assert.Equal(180, SlotRouter.TotalBudgetSeconds(c, AiSlot.Background));
		Assert.Equal(315, SlotRouter.DialogueWatchdogSeconds(c));
		Assert.Null(SlotRouter.ReasoningEffort(new ModConfig { ChatThinking = "auto" }, AiSlot.Dialogue));
		Assert.Equal("", SlotRouter.ReasoningEffort(new ModConfig { ChatThinking = "none" }, AiSlot.Dialogue));
		Assert.Equal("low", SlotRouter.ReasoningEffort(new ModConfig { ChatThinking = "weird" }, AiSlot.Dialogue));
	}

	[Fact]
	public void TokenBudgetMatchesOriginalRules()
	{
		int roomy = TokenBudget.MaxCeiling;
		Assert.InRange(TokenBudget.ForRequest(0, roomy, false, "x"), 129, TokenBudget.DefaultCeiling);
		Assert.Equal(TokenBudget.DefaultCeiling, TokenBudget.ForRequest(TokenBudget.DefaultCeiling + 800, 0, false, "x"));
		Assert.Equal(1000, TokenBudget.ForRequest(1000, roomy, false, "x"));
		Assert.True(TokenBudget.ForRequest(1000, roomy, true, "x") > 1000);
		Assert.True(TokenBudget.ForRequest(9000, roomy, true, "x") - 9000 > TokenBudget.ForRequest(1000, roomy, true, "x") - 1000);
		Assert.Equal(TokenBudget.DefaultCeiling, TokenBudget.ForRequest(TokenBudget.DefaultCeiling - 1, TokenBudget.DefaultCeiling, true, "x"));
		Assert.Equal(100, TokenBudget.ForRequest(100, roomy, true, "x"));
		Assert.Equal(5000, TokenBudget.ForRequest(5000, roomy, true, TokenBudget.ConnectionTestRequest));
		Assert.True(TokenBudget.ForRetry(1000, roomy, "x") >= 1500);
		Assert.Equal(TokenBudget.DefaultCeiling, TokenBudget.ForRetry(TokenBudget.DefaultCeiling - 1, TokenBudget.DefaultCeiling, "x"));
		Assert.Equal(TokenBudget.DefaultCeiling, TokenBudget.ForRetry(TokenBudget.DefaultCeiling, TokenBudget.DefaultCeiling, "x"));
		Assert.Equal(0, TokenBudget.ForRetry(0, roomy, "x"));
		Assert.Equal(100, TokenBudget.ForRetry(100, roomy, "x"));
		Assert.Equal(TokenBudget.MaxCeiling, TokenBudget.Ceiling(TokenBudget.MaxCeiling + 1));
	}

	[Fact]
	public async Task FallsBackToNextModelOnServerError()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}").Reply(HttpStatusCode.OK, FakeHttp.Chat("嗨"));
		AiResult r = await ai.SendAsync(Req());
		Assert.True(r.Ok);
		Assert.Equal("嗨", r.Text);
		Assert.Contains("\"cn:deepseek-v4.1-flash\"", http.Requests[0].Body);
		Assert.Contains("\"cn:deepseek-v4-flash\"", http.Requests[1].Body);
		Assert.Equal("Bearer k", http.Requests[0].Headers["Authorization"]);
	}

	[Fact]
	public async Task AsksAgainOnceWhenContentIsEmpty()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("")).Reply(HttpStatusCode.OK, FakeHttp.Chat("嗨"));
		AiResult r = await ai.SendAsync(Req());
		Assert.Equal("嗨", r.Text);
		Assert.Contains("\"cn:deepseek-v4.1-flash\"", http.Requests[1].Body);
	}

	[Fact]
	public async Task DoesNotFallBackOnInvalidKey()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"bad key\"}}");
		AiResult r = await ai.SendAsync(Req());
		Assert.Equal(FailureKind.InvalidApiKey, r.Failure);
		Assert.Equal("bad key", r.Message);
		Assert.Single(http.Requests);
		Assert.Equal(1, ai.FailedToday);
	}

	[Fact]
	public async Task AdjustsUnsupportedParameterAndRemembersIt()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.BadRequest, "{\"error\":{\"param\":\"temperature\",\"code\":\"unsupported_parameter\"}}")
			.Reply(HttpStatusCode.OK, FakeHttp.Chat("好"))
			.Reply(HttpStatusCode.OK, FakeHttp.Chat("又好"));
		Assert.True((await ai.SendAsync(Req())).Ok);
		Assert.Contains("temperature", http.Requests[0].Body);
		Assert.DoesNotContain("temperature", http.Requests[1].Body);
		Assert.True((await ai.SendAsync(Req())).Ok);
		Assert.DoesNotContain("temperature", http.Requests[2].Body);
	}

	[Fact]
	public async Task RetriesOnceWithLargerBudgetWhenTruncated()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("半句", "length")).Reply(HttpStatusCode.OK, FakeHttp.Chat("完整的一句"));
		AiResult r = await ai.SendAsync(Req(max: 1000));
		Assert.Equal("完整的一句", r.Text);
		Assert.Contains("\"max_tokens\":1000", http.Requests[0].Body);
		int bigger = TokenBudget.ForRetry(1000, TokenBudget.DefaultCeiling, "x");
		Assert.True(bigger > 1000);
		Assert.Contains($"\"max_tokens\":{bigger}", http.Requests[1].Body);
	}

	[Fact]
	public async Task BackgroundLimitSkipsNonEssentialOnly()
	{
		(AiService ai, FakeHttp http, _) = Own(new ModConfig { AiConnectionMode = "own", CustomLlmBaseUrl = "http://gw.test", NightlyCallsPerDay = 1 });
		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("1")).Reply(HttpStatusCode.OK, FakeHttp.Chat("2"));
		Assert.True((await ai.SendAsync(Req("npc_interaction"))).Ok);
		Assert.Equal(FailureKind.SkippedDailyLimit, (await ai.SendAsync(Req("npc_interaction"))).Failure);
		Assert.True((await ai.SendAsync(Req("weekly_news"))).Ok);
		Assert.Equal((2, 1), ai.BackgroundUsage);
		ai.StartNewDay();
		Assert.Equal((0, 0), ai.BackgroundUsage);
	}

	[Fact]
	public async Task ServerModeNeedsLoginAndSendsAliasHeaders()
	{
		(AiService ai, FakeHttp http, ServerAccount account, MemoryAccountStore store) = Server(new ModConfig { ServerChatModel = "pro", ServerThinkingLevel = "HIGH" });
		Assert.Equal(FailureKind.AccountRequired, (await ai.SendAsync(Req())).Failure);
		Assert.True(account.LoginRequested);
		Assert.Empty(http.Requests);

		http.Reply(HttpStatusCode.OK, "{\"token\":\"t1\",\"created\":true,\"me\":{\"name\":\"阿哲\",\"available\":2,\"giftLeft\":2,\"limits\":{\"backgroundDaily\":150},\"models\":[{\"id\":\"pro\",\"label\":\"Pro\"}]}}");
		(bool ok, string msg) = await account.EnterAsync(" 阿哲 ", "pw");
		Assert.True(ok);
		Assert.Equal("created", msg);
		Assert.Equal("http://srv.test:8080/aisv/enter", http.Requests[0].Url);
		Assert.Contains("\"username\":", http.Requests[0].Body);
		Assert.Equal("t1", store.Saved!.Token);
		Assert.False(account.LoginRequested);
		Assert.Equal(150, ai.BackgroundDailyLimit);

		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("你好"), ("X-AISV-Available", "1.5"));
		Assert.True((await ai.SendAsync(Req())).Ok);
		FakeHttp.Seen chat = http.Requests[1];
		Assert.Equal("http://srv.test:8080/v1/chat/completions", chat.Url);
		Assert.Contains("\"aisv-dialogue\"", chat.Body);
		Assert.Equal("Bearer t1", chat.Headers["Authorization"]);
		Assert.Equal("pro", chat.Headers["X-AISV-Model"]);
		Assert.Equal("high", chat.Headers["X-AISV-Thinking"]);
		Assert.Equal(1.5, account.Available);
		Assert.Equal(0.5, account.GiftUsedToday, 3);

		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("周报"));
		Assert.True((await ai.SendAsync(Req("weekly_news"))).Ok);
		Assert.Equal("1", http.Requests[2].Headers["X-AISV-Essential"]);
		Assert.False(http.Requests[2].Headers.ContainsKey("X-AISV-Model"));
	}

	[Fact]
	public async Task SessionInvalidClearsTokenAndAsksForLogin()
	{
		(AiService ai, FakeHttp http, ServerAccount account, MemoryAccountStore store) = Server();
		http.Reply(HttpStatusCode.OK, "{\"token\":\"t1\",\"me\":{\"name\":\"a\"}}");
		await account.EnterAsync("a", "b");
		http.Reply(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"aisv_session_invalid\",\"message\":\"登录失效\"}}");
		AiResult r = await ai.SendAsync(Req());
		Assert.Equal(FailureKind.AccountRequired, r.Failure);
		Assert.False(account.IsLoggedIn);
		Assert.Equal("", store.Saved!.Token);
		Assert.True(account.LoginRequested);
	}

	[Fact]
	public async Task NetworkErrorIsMaskedAndFallsBack()
	{
		(AiService ai, FakeHttp http, ServerAccount account, _) = Server();
		http.Reply(HttpStatusCode.OK, "{\"token\":\"t1\",\"me\":{\"name\":\"a\"}}");
		await account.EnterAsync("a", "b");
		http.Throw(new HttpRequestException("connect srv.test:8080 failed"));
		AiResult r = await ai.SendAsync(Req());
		Assert.Equal(FailureKind.Network, r.Failure);
		Assert.DoesNotContain("srv.test", r.Message);
	}

	[Fact]
	public async Task AccountOfflineAndStatusText()
	{
		(_, FakeHttp http, ServerAccount account, _) = Server();
		http.Throw(new HttpRequestException("down"));
		(bool ok, string msg) = await account.EnterAsync("a", "b");
		Assert.False(ok);
		Assert.Equal("连不上服务器，检查一下网络。", msg);
		Assert.StartsWith("还没登录", account.StatusText(""));
		http.Reply(HttpStatusCode.OK, "{\"token\":\"t\",\"me\":{\"name\":\"x\",\"available\":0.25,\"giftEnabled\":true,\"giftAmount\":1,\"giftLeft\":0.25}}");
		await account.EnterAsync("x", "y");
		Assert.Equal("账号 x：可用 0.25 元\n今天远古牛哥请客 1 元，已经用了 0.75 元。", account.StatusText(""));
	}

	[Fact]
	public void UpdateRequiredComparesVersions()
	{
		(_, _, ServerAccount account, _) = Server();
		account.ApplyMe(JsonNode.Parse("{\"minClientVersion\":\"2.1\"}")!.AsObject());
		account.ClientVersion = "2.0.0-alpha.1";
		Assert.True(account.UpdateRequired);
		account.ClientVersion = "2.1.0";
		Assert.False(account.UpdateRequired);
	}

	[Fact]
	public async Task BannedAccountClearsToken()
	{
		(AiService ai, FakeHttp http, ServerAccount account, _) = Server();
		http.Reply(HttpStatusCode.OK, "{\"token\":\"t1\",\"me\":{\"name\":\"a\"}}");
		await account.EnterAsync("a", "b");
		http.Reply(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"aisv_banned\"}}");
		Assert.Equal(FailureKind.AccountBanned, (await ai.SendAsync(Req())).Failure);
		Assert.False(account.IsLoggedIn);
	}

	[Fact]
	public async Task GatewayGoesOfflineAfterThreeNetworkFailuresAndRecovers()
	{
		(AiService ai, FakeHttp http, _) = Own(new ModConfig { AiConnectionMode = "own", CustomLlmBaseUrl = "http://gw.test", ChatBackupModels = "" });
		for (int i = 0; i < 3; i++)
		{
			Assert.False(ai.IsGatewayOffline);
			http.Throw(new HttpRequestException("down"));
			await ai.SendAsync(Req());
		}
		Assert.True(ai.IsGatewayOffline);
		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("好了"));
		Assert.True((await ai.SendAsync(Req())).Ok);
		Assert.False(ai.IsGatewayOffline);
	}

	[Fact]
	public async Task ThinkTagAloneDoesNotCountAsReasoning()
	{
		(AiService ai, FakeHttp http, _) = Own();
		http.Reply(HttpStatusCode.OK, FakeHttp.Chat("<think>想想</think>你好")).Reply(HttpStatusCode.OK, FakeHttp.Chat("再见"));
		Assert.Equal("你好", (await ai.SendAsync(Req(max: 1000))).Text);
		await ai.SendAsync(Req(max: 1000));
		Assert.Contains("\"max_tokens\":1000", http.Requests[1].Body);
	}

	[Fact]
	public async Task DisabledAiSendsNothing()
	{
		(AiService ai, FakeHttp http, _) = Own(new ModConfig { AiConnectionMode = "own", AiOn = false });
		Assert.Equal(FailureKind.Disabled, (await ai.SendAsync(Req())).Failure);
		Assert.Empty(http.Requests);
	}

	[Fact]
	public void ConfigNormalizeFillsDefaults()
	{
		ModConfig c = new ModConfig { CustomLlmBaseUrl = " ", CustomLlmModel = "", NightlyCallsPerDay = 99999, ModVillagerPicks = new System.Collections.Generic.List<string> { " A ", "a", "" } }.Normalize();
		Assert.Equal(new ModConfig().CustomLlmBaseUrl, c.CustomLlmBaseUrl);
		Assert.Equal(new ModConfig().CustomLlmModel, c.CustomLlmModel);
		Assert.Equal(2000, c.NightlyCallsPerDay);
		Assert.Equal(new[] { "A" }, c.ModVillagerPicks);
	}

	[Fact]
	public void CleanerDropsTrailingCommasOutsideStrings()
	{
		string clean = AISV.Core.Dialogue.JsonCleaner.Clean("{\"a\": [1, 2,], \"b\": \"x,}\",\n}");
		using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(clean);
		Assert.Equal(2, doc.RootElement.GetProperty("a").GetArrayLength());
		Assert.Equal("x,}", doc.RootElement.GetProperty("b").GetString());
	}
}
