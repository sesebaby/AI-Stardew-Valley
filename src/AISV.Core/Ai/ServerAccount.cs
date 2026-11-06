using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AISV.Core.Diagnostics;
using AISV.Core.Text;

namespace AISV.Core.Ai;

/// <summary>
/// 存在本机的账号信息（SMAPI 全局数据 "aisv-account"，不进存档、不进 mod 文件夹）。
/// 字段名不能改：升级后不用重新登录。
/// </summary>
public sealed class AccountState
{
	public string Token { get; set; } = "";

	public string Name { get; set; } = "";

	/// <summary>服务器给的各项上限（replyChars、memoryEntries、backgroundDaily……）。</summary>
	public Dictionary<string, int> Limits { get; set; } = new Dictionary<string, int>();

	public List<PlayerModel> Models { get; set; } = new List<PlayerModel>();

	public string ThinkingDefault { get; set; } = "";

	public string ThinkingMax { get; set; } = "";
}

/// <summary>服务器上玩家可选的对话模型。Factor = 大约是默认模型的几倍价钱。</summary>
public sealed class PlayerModel
{
	public string Id { get; set; } = "";

	public string Label { get; set; } = "";

	public double? Factor { get; set; }
}

/// <summary>账号信息存在哪：游戏层用 SMAPI 全局数据，测试用内存。</summary>
public interface IAccountStore
{
	AccountState? Load();

	void Save(AccountState state);
}

public sealed class MemoryAccountStore : IAccountStore
{
	public AccountState? Saved { get; private set; }

	public AccountState? Load() => Saved;

	public void Save(AccountState state) => Saved = JsonSerializer.Deserialize<AccountState>(JsonSerializer.Serialize(state));
}

/// <summary>
/// 玩家在 AI星露谷服务器上的账号：进入（有这个名字就登录，没有就注册）、查今天的额度、改密码、退出。
/// 所有功能对所有人免费，客户端不做额度判断，只显示服务器说的数。
/// </summary>
public sealed class ServerAccount
{
	private readonly ServerConnection _connection;

	private readonly IAccountStore _store;

	private readonly HttpClient _http;

	private readonly ILog _log;

	private readonly object _lock = new object();

	private AccountState _state;

	private string _deferredDay = "";

	public ServerAccount(ServerConnection connection, IAccountStore store, HttpClient http, ILog log)
	{
		_connection = connection;
		_store = store;
		_http = http;
		_log = log;
		try
		{
			_state = store.Load() ?? new AccountState();
		}
		catch (Exception ex)
		{
			// SMAPI 读坏文件抛的是它自己的异常，这里一律接住，当作没登录，别让整个 mod 加载失败。
			log.Warn("账号数据读不出来，当作没登录：" + ex.GetType().Name);
			_state = new AccountState();
		}
	}

	public string Token => _state.Token;

	public string Name => _state.Name;

	public bool IsLoggedIn => _state.Token.Length > 0;

	public IReadOnlyList<PlayerModel> PlayerModels => _state.Models;

	public string ThinkingDefault => _state.ThinkingDefault;

	public string ThinkingMax => _state.ThinkingMax.Length > 0 ? _state.ThinkingMax : "medium";

	public double? Available { get; private set; }

	public double? GiftLeft { get; private set; }

	public bool GiftEnabled { get; private set; }

	public double GiftAmount { get; private set; }

	public double GiftUsedToday { get; private set; }

	public double GiftUsedTotal { get; private set; }

	public string Announcement { get; private set; } = "";

	public string MinClientVersion { get; private set; } = "";

	public bool Maintenance { get; private set; }

	/// <summary>有 AI 请求因为没登录没发：界面在玩家能行动时弹登录窗口。</summary>
	public bool LoginRequested { get; private set; }

	/// <summary>账号状态每变一次加一，界面据此重画。</summary>
	public int StateVersion { get; private set; }

	public string ClientVersion { get; set; } = "";

	/// <summary>服务器给的上限；没给用 fallback（和服务器的默认值一样）。</summary>
	public int Limit(string key, int fallback) => _state.Limits.TryGetValue(key, out int v) ? v : fallback;

	public bool UpdateRequired => MinClientVersion.Length > 0 && ClientVersion.Length > 0
		&& Version.TryParse(NormalizeVersion(MinClientVersion), out Version? min) && Version.TryParse(NormalizeVersion(ClientVersion), out Version? cur) && cur < min;

	// ---- 登录窗口 ----

	/// <summary>没登录时 AI 请求不发；今天没点过"稍后"就请界面弹登录窗口。</summary>
	public void RequestLogin()
	{
		if (_deferredDay != Today())
		{
			LoginRequested = true;
		}
	}

	public void ConsumeLoginRequest() => LoginRequested = false;

	/// <summary>登录窗口点了"稍后"：今天不再自动弹（设置里随时能登录）。</summary>
	public void DeferLoginToday()
	{
		_deferredDay = Today();
		LoginRequested = false;
	}

	// ---- 服务器接口 ----

	/// <summary>登录和注册合一：名字有人用就登录，没人用就注册。成功时 Message 为 "created" 表示刚注册。</summary>
	public Task<(bool Ok, string Message)> EnterAsync(string user, string password) => SignInAsync("/aisv/enter", user, password);

	public Task<(bool Ok, string Message)> LoginAsync(string user, string password) => SignInAsync("/aisv/login", user, password);

	public Task<(bool Ok, string Message)> RegisterAsync(string user, string password) => SignInAsync("/aisv/register", user, password);

	public async Task<(bool Ok, string Message)> RefreshMeAsync()
	{
		if (!IsLoggedIn)
		{
			return (false, "");
		}
		(bool ok, JsonObject? body, string message) = await SendAsync(HttpMethod.Get, "/aisv/me", null, withToken: true).ConfigureAwait(false);
		if (ok)
		{
			ApplyMe(body);
		}
		return (ok, message);
	}

	public async Task<(bool Ok, string Message)> ChangePasswordAsync(string oldPassword, string newPassword)
	{
		(bool ok, _, string message) = await SendAsync(HttpMethod.Post, "/aisv/password", new JsonObject { ["old"] = oldPassword, ["new"] = newPassword }, withToken: true).ConfigureAwait(false);
		return (ok, ok ? Tr.Get("account.passwordChanged", "密码改好了。其他电脑上的登录已退出。") : message);
	}

	public async Task LogoutAsync()
	{
		if (IsLoggedIn)
		{
			await SendAsync(HttpMethod.Post, "/aisv/logout", new JsonObject(), withToken: true).ConfigureAwait(false);
		}
		ClearLocal();
	}

	/// <summary>只清本机的登录（退出时连不上服务器也用这个）。</summary>
	public void ClearLocal()
	{
		lock (_lock)
		{
			_state = new AccountState();
			Persist();
		}
		Available = null;
		GiftLeft = null;
		StateVersion++;
	}

	/// <summary>服务器说会话失效（过期、被踢、改了密码、封禁）：清掉本机令牌，下次用 AI 时请玩家登录。</summary>
	public void SessionExpired()
	{
		lock (_lock)
		{
			if (_state.Token.Length == 0)
			{
				return;
			}
			_state.Token = "";
			Persist();
		}
		Available = null;
		StateVersion++;
		_log.Info("服务器上的登录失效了，下次用 AI 时重新登录。");
		RequestLogin();
	}

	// ---- AI 回复里的账号信息 ----

	/// <summary>看 AI 回复的响应头：还能用多少；正文里有会话失效就清令牌。</summary>
	public void ObserveAiResponse(HttpResponseMessage response, string? errorBody)
	{
		if (response.Headers.TryGetValues("X-AISV-Available", out IEnumerable<string>? values)
			&& double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double available))
		{
			NoteAvailable(available);
		}
		if (errorBody != null && errorBody.Contains("aisv_session_invalid", StringComparison.Ordinal))
		{
			SessionExpired();
		}
	}

	/// <summary>每次 AI 请求后服务器回"还能用多少"：花掉的先算今天的赠送额度（和服务器扣钱的顺序一样）。</summary>
	public void NoteAvailable(double available)
	{
		if (Available.HasValue && available < Available.Value)
		{
			double spent = Available.Value - available;
			double fromGift = Math.Min(spent, Math.Max(0, GiftLeft ?? 0));
			if (fromGift > 0)
			{
				GiftLeft = (GiftLeft ?? 0) - fromGift;
				GiftUsedToday += fromGift;
				GiftUsedTotal += fromGift;
			}
		}
		Available = available;
		StateVersion++;
	}

	/// <summary>设置页账号区的说明。</summary>
	public string StatusText(string authorName)
	{
		if (!IsLoggedIn)
		{
			return Tr.Get("account.notLoggedIn", "还没登录。登录后村民才会用 AI 说话。");
		}
		string who = Tr.Get("account.who", "账号 {{name}}", ("name", Name));
		if (Maintenance)
		{
			return who + "：" + Tr.Get("account.maintenance", "服务器维护中。");
		}
		if (!Available.HasValue)
		{
			return who + "：" + Tr.Get("account.unknown", "额度还没查到。");
		}
		string line = Tr.Get("account.balance", "可用 {{available}} 元", ("available", Money(Available.Value)));
		if (Available.Value <= 0)
		{
			line += Tr.Get("account.empty", "。今天的额度用完了，明天再来。");
		}
		string result = who + "：" + line;
		string author = authorName.Trim().Length > 0 ? authorName.Trim() : "远古牛哥";
		if (GiftEnabled && GiftAmount > 0)
		{
			result += "\n" + Tr.Get("account.giftToday", "今天{{author}}请客 {{daily}} 元，已经用了 {{used}} 元。", ("author", author), ("daily", Money(GiftAmount)), ("used", Money(Math.Min(GiftAmount, GiftUsedToday))));
		}
		if (GiftUsedTotal > 0)
		{
			result += (GiftEnabled && GiftAmount > 0 ? "" : "\n") + Tr.Get("account.giftTotal", "到现在一共请你用了 {{total}} 元的 AI。", ("author", author), ("total", Money(GiftUsedTotal)));
		}
		if (UpdateRequired)
		{
			result += "\n" + Tr.Get("account.updateRequired", "服务器提示：请更新到 {{version}} 或更新的版本。", ("version", MinClientVersion));
		}
		return result;
	}

	public static string Money(double yuan) => yuan >= 1 ? yuan.ToString("0.##", CultureInfo.InvariantCulture) : yuan.ToString("0.####", CultureInfo.InvariantCulture);

	private async Task<(bool Ok, string Message)> SignInAsync(string path, string user, string password)
	{
		(bool ok, JsonObject? body, string message) = await SendAsync(HttpMethod.Post, path, new JsonObject
		{
			["username"] = user.Trim(),
			["password"] = password,
			["device"] = DeviceName()
		}, withToken: false).ConfigureAwait(false);
		if (!ok || body == null)
		{
			return (false, message);
		}
		lock (_lock)
		{
			_state.Token = body["token"]?.ToString() ?? "";
			_state.Name = body["me"]?["name"]?.ToString() ?? user.Trim();
			Persist();
		}
		ApplyMe(body["me"] as JsonObject);
		LoginRequested = false;
		bool created = Bool(body, "created");
		_log.Info((created ? "注册并登录了" : "已登录") + " AI星露谷服务器（" + Name + "）。");
		return (true, created ? "created" : "");
	}

	internal void ApplyMe(JsonObject? me)
	{
		if (me == null)
		{
			return;
		}
		GiftLeft = Num(me, "giftLeft");
		Available = Num(me, "available");
		GiftEnabled = Bool(me, "giftEnabled");
		GiftAmount = Num(me, "giftAmount") ?? 0;
		GiftUsedToday = Num(me, "giftUsedToday") ?? Math.Max(0, GiftAmount - (GiftLeft ?? GiftAmount));
		GiftUsedTotal = Num(me, "giftUsedTotal") ?? 0;
		Announcement = me["announcement"]?.ToString() ?? "";
		MinClientVersion = me["minClientVersion"]?.ToString() ?? "";
		Maintenance = Bool(me, "maintenance");
		string name = me["name"]?.ToString() ?? "";
		lock (_lock)
		{
			if (name.Length > 0)
			{
				_state.Name = name;
			}
			if (me["limits"] is JsonObject limits)
			{
				_state.Limits = limits
					.Where(kv => kv.Value is JsonValue v && v.TryGetValue(out double _))
					.ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value!.GetValue<double>()));
			}
			if (me["models"] is JsonArray models)
			{
				_state.Models = models.OfType<JsonObject>()
					.Select(o => new PlayerModel { Id = o["id"]?.ToString() ?? "", Label = o["label"]?.ToString() ?? "", Factor = Num(o, "factor") })
					.Where(x => x.Id.Length > 0)
					.ToList();
			}
			_state.ThinkingDefault = me["thinkingDefault"]?.ToString() ?? "";
			_state.ThinkingMax = me["thinkingMax"]?.ToString() ?? "";
			Persist();
		}
		StateVersion++;
	}

	/// <summary>发给服务器；失败时带一句给玩家看的话（服务器的说明或"连不上服务器"）。</summary>
	private async Task<(bool Ok, JsonObject? Body, string Message)> SendAsync(HttpMethod method, string path, JsonObject? body, bool withToken)
	{
		if (!_connection.IsAvailable)
		{
			return (false, null, Tr.Get("account.noServer", "这个版本没有内置服务器。"));
		}
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(method, _connection.Root + path);
			if (body != null)
			{
				req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
			}
			if (withToken && IsLoggedIn)
			{
				req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
			}
			using HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false);
			string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
			JsonObject? json = null;
			try
			{
				json = JsonNode.Parse(text) as JsonObject;
			}
			catch (JsonException)
			{
			}
			if (resp.IsSuccessStatusCode)
			{
				return (true, json, "");
			}
			string code = json?["error"]?["code"]?.ToString() ?? "";
			string message = json?["error"]?["message"]?.ToString() ?? "";
			if (withToken && code is "aisv_session_invalid" or "aisv_banned")
			{
				SessionExpired();
			}
			return (false, json, message.Length > 0 ? message : Tr.Get("account.serverError", "服务器出错了（{{status}}），稍后再试。", ("status", (int)resp.StatusCode)));
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			_log.Warn("连不上 AI星露谷服务器：" + _connection.Mask(ex.Message));
			return (false, null, Tr.Get("account.offline", "连不上服务器，检查一下网络。"));
		}
	}

	private void Persist()
	{
		try
		{
			_store.Save(_state);
		}
		catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or JsonException)
		{
			_log.Warn("账号数据存不下来：" + ex.GetType().Name);
		}
	}

	private static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static string NormalizeVersion(string v)
	{
		string s = v.Trim().Split('-', '+')[0];
		return s.Contains('.') ? s : s + ".0";
	}

	private static string DeviceName() => string.IsNullOrWhiteSpace(Environment.MachineName) ? "PC" : Environment.MachineName;

	private static double? Num(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out double d) ? d : null;

	private static bool Bool(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out bool b) && b;
}
