using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using Xunit;

namespace AISV.Core.Tests;

public class UsageTests
{
	private static (ServerAccount Account, FakeHttp Http) LoggedIn()
	{
		ServerConnection connection = ServerConnection.Parse("{\"u\":\"http://srv.test:8080/v1\",\"n\":\"测试包\"}")!;
		FakeHttp fake = new FakeHttp();
		MemoryAccountStore store = new MemoryAccountStore();
		store.Save(new AccountState { Token = "t1", Name = "阿哲" });
		return (new ServerAccount(connection, store, new HttpClient(fake), NullLog.Instance), fake);
	}

	[Fact]
	public async Task CountsAndSecondsGoOutWithThePing()
	{
		(ServerAccount account, FakeHttp http) = LoggedIn();
		UsageReporter usage = new UsageReporter(account, () => true, NullLog.Instance);
		usage.Count("tab.Social.Letters");
		usage.Count("tab.social.letters");
		usage.Count("有中文");
		usage.AddSeconds(250);
		usage.AddSeconds(500);
		Assert.Equal(UsageReporter.MaxSeconds, usage.PendingSeconds);
		Assert.Equal(new Dictionary<string, int> { ["ui:tab.social.letters"] = 2 }, usage.PendingCounts);

		http.Reply(HttpStatusCode.OK, "{\"ok\":true,\"notices\":[{\"id\":7,\"amount\":0.5,\"text\":\"维护补偿\"}]}");
		await usage.FlushAsync();
		JsonObject body = (JsonObject)JsonNode.Parse(http.Requests[0].Body)!;
		Assert.Equal("http://srv.test:8080/aisv/ping", http.Requests[0].Url);
		Assert.Equal(600, (int)body["seconds"]!);
		Assert.Equal(2, (int)body["events"]!["ui:tab.social.letters"]!);
		Assert.Equal(0, usage.PendingSeconds);
		Assert.False(usage.Due(System.DateTime.UtcNow));

		http.Reply(HttpStatusCode.OK, "{\"ok\":true}");
		List<AccountNotice> notices = account.DrainNotices();
		Assert.Equal(new AccountNotice(7, 0.5, "维护补偿"), Assert.Single(notices));
		Assert.Empty(account.DrainNotices());
		Assert.Contains("维护补偿", ServerAccount.NoticeLine(notices[0]));
	}

	[Fact]
	public async Task FailedPingKeepsDataAndOldServerStopsIt()
	{
		(ServerAccount account, FakeHttp http) = LoggedIn();
		UsageReporter usage = new UsageReporter(account, () => true, NullLog.Instance);
		usage.Count("device.phone");
		usage.AddSeconds(30);
		http.Reply(HttpStatusCode.BadGateway, "{}");
		await usage.FlushAsync();
		Assert.Equal(30, usage.PendingSeconds);
		Assert.Equal(1, usage.PendingCounts["ui:device.phone"]);

		http.Reply(HttpStatusCode.NotFound, "{}");
		await usage.FlushAsync();
		Assert.True(usage.Unsupported);
		Assert.Equal(0, usage.PendingSeconds);
		await usage.FlushAsync();
		Assert.Equal(2, http.Requests.Count);
	}

	[Fact]
	public async Task SwitchedOffCollectsAndSendsNothing()
	{
		(ServerAccount account, FakeHttp http) = LoggedIn();
		bool on = true;
		UsageReporter usage = new UsageReporter(account, () => on, NullLog.Instance);
		usage.Count("app.family");
		usage.AddSeconds(60);
		on = false;
		usage.Count("app.family");
		usage.AddSeconds(60);
		await usage.FlushAsync();
		Assert.Empty(http.Requests);
		Assert.Equal(0, usage.PendingSeconds);
		Assert.Empty(usage.PendingCounts);
	}

	[Fact]
	public async Task FeatureHeaderFollowsTheSwitch()
	{
		ModConfig config = new ModConfig();
		ServerConnection connection = ServerConnection.Parse("{\"u\":\"http://srv.test:8080/v1\",\"n\":\"测试包\"}")!;
		FakeHttp fake = new FakeHttp();
		HttpClient http = new HttpClient(fake);
		MemoryAccountStore store = new MemoryAccountStore();
		store.Save(new AccountState { Token = "t1", Name = "阿哲" });
		ServerAccount account = new ServerAccount(connection, store, http, NullLog.Instance);
		AiService ai = new AiService(() => config, new ChatClient(http, () => config, NullLog.Instance, connection.Mask), connection, account, NullLog.Instance);

		fake.Reply(HttpStatusCode.OK, FakeHttp.Chat("好"));
		Assert.True((await ai.SendAsync(new ChatRequest("evaluate_response", "SYS", "USER", 100, 0.8))).Ok);
		Assert.Equal("evaluate_response", fake.Requests[0].Headers["X-AISV-Feature"]);

		config.ShareUsageStats = false;
		fake.Reply(HttpStatusCode.OK, FakeHttp.Chat("好"));
		Assert.True((await ai.SendAsync(new ChatRequest("evaluate_response", "SYS", "USER", 100, 0.8))).Ok);
		Assert.False(fake.Requests[1].Headers.ContainsKey("X-AISV-Feature"));
	}

	[Fact]
	public async Task SignInSendsOnlyTheHashedMachineId()
	{
		(ServerAccount account, FakeHttp http) = LoggedIn();
		http.Reply(HttpStatusCode.OK, "{\"token\":\"t2\",\"me\":{\"name\":\"阿哲\"}}");
		await account.EnterAsync("azhe", "secret12");
		JsonObject body = (JsonObject)JsonNode.Parse(http.Requests[0].Body)!;
		string hw = body["hw"]!.ToString();
		Assert.True(hw.Length == 0 || (hw.Length == 32 && hw.All(c => "0123456789abcdef".Contains(c))));
		Assert.Equal(MachineId.Of("ABC-123"), MachineId.Of("abc-123"));
		Assert.NotEqual(MachineId.Of("abc-123"), MachineId.Of("abc-124"));
		Assert.Equal("", MachineId.Of(""));
		Assert.DoesNotContain("abc-123", MachineId.Of("abc-123"));
	}
}
