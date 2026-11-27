using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using AISV.Core.Text;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.Game.Net;

/// <summary>
/// 联机：房主是全村数据的唯一来源。
/// <list type="bullet">
/// <item>房主把全村共用的文档（八卦、关系网、村民人生、周报、账本……）和今天的心情、聊天发给装了同一版本本 mod 的农场帮手：对方进来时发一份全的，之后约一秒发一次改过的。</item>
/// <item>农场帮手做的、关系到全村的事（聊天、心情、八卦素材、借钱、接委托）发给房主处理。</item>
/// <item>落在某位农夫身上的事（扣钱、好感、来信）发给那位农夫，在他自己那边做。</item>
/// </list>
/// 版本不一致时，农场帮手这边只在本机运行，不收也不发。同一台电脑上分屏不走这里（两个画面本来就用同一份数据）。
/// </summary>
public sealed class NetSync
{
	private const string DocsType = "docs";

	private const string OpType = "op";

	private const string ReplyType = "reply";

	private const string HelloType = "hello";

	private const string MismatchType = "mismatch";

	private const int SyncTicks = 60;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

	private readonly IModHelper _helper;

	private readonly string _modId;

	private readonly string _version;

	private readonly Func<DocumentStore?> _store;

	private readonly Func<IReadOnlyCollection<string>> _shared;

	private readonly ILog _log;

	private readonly Dictionary<string, Func<long, string, string?>> _handlers = new Dictionary<string, Func<long, string, string?>>(StringComparer.Ordinal);

	private readonly Dictionary<string, (Func<string> Export, Action<string> Import)> _channels = new Dictionary<string, (Func<string>, Action<string>)>(StringComparer.Ordinal);

	/// <summary>房主：装了同一版本、已经打过招呼的农场帮手。</summary>
	private readonly HashSet<long> _linked = new HashSet<long>();

	private readonly Dictionary<string, long> _sentRevision = new Dictionary<string, long>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _sentChannel = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Dictionary<string, (Action<string?> Reply, int Deadline)> _waiting = new Dictionary<string, (Action<string?>, int)>(StringComparer.Ordinal);

	private int _ticks;

	private int _nextId;

	public NetSync(IModHelper helper, IManifest manifest, Func<DocumentStore?> store, Func<IReadOnlyCollection<string>> shared, ILog log)
	{
		_helper = helper;
		_modId = manifest.UniqueID;
		_version = manifest.Version.ToString();
		_store = store;
		_shared = shared;
		_log = log;
	}

	/// <summary>这边是另一台电脑上的农场帮手。</summary>
	public static bool Remote => Context.IsWorldReady && !Context.IsOnHostComputer;

	/// <summary>农场帮手：房主装着同一版本，已经连上，全村的事交给房主。</summary>
	public bool Linked { get; private set; }

	/// <summary>农场帮手：房主没装本 mod 或者版本不一样，这边的功能只在本机。</summary>
	public string? LocalOnlyReason { get; private set; }

	/// <summary>收到了一份房主的数据（界面可以刷新）。</summary>
	public event Action? Synced;

	/// <summary>房主：现在连着的、能同步的农场帮手。</summary>
	public IReadOnlyCollection<long> Peers => _linked;

	/// <summary>收到 <paramref name="op"/> 时怎么处理（两边都可以注册：房主处理农场帮手的请求，农场帮手处理落在自己身上的事）。返回值不为 null 时回给对方。</summary>
	public void On<T>(string op, Func<long, T, object?> handler)
	{
		_handlers[op] = (from, json) =>
		{
			T? payload = JsonSerializer.Deserialize<T>(json, Json);
			if (payload == null)
			{
				return null;
			}
			object? result = handler(from, payload);
			return result == null ? null : JsonSerializer.Serialize(result, result.GetType(), Json);
		};
	}

	public void On<T>(string op, Action<long, T> handler) => On<T>(op, (from, p) =>
	{
		handler(from, p);
		return null;
	});

	/// <summary>除了文档以外，房主还要同步的一份状态（今天的心情、聊天）。</summary>
	public void Channel(string key, Func<string> export, Action<string> import) => _channels[key] = (export, import);

	/// <summary>农场帮手：交给房主处理。</summary>
	public void ToHost<T>(string op, T payload)
	{
		if (Linked)
		{
			Send(new Envelope { Op = op, Json = JsonSerializer.Serialize(payload, Json) }, OpType, Game1.MasterPlayer.UniqueMultiplayerID);
		}
	}

	/// <summary>农场帮手：问房主，回话（超时或没连上时为 default）在主线程回调。</summary>
	public void Ask<T, TReply>(string op, T payload, Action<TReply?> reply, int seconds = 20)
	{
		if (!Linked)
		{
			reply(default);
			return;
		}
		string id = $"{Game1.player.UniqueMultiplayerID}-{++_nextId}";
		_waiting[id] = (json => reply(json == null ? default : JsonSerializer.Deserialize<TReply>(json, Json)), Game1.ticks + seconds * 60);
		Send(new Envelope { Op = op, Json = JsonSerializer.Serialize(payload, Json), Id = id }, OpType, Game1.MasterPlayer.UniqueMultiplayerID);
	}

	/// <summary>落在某位农夫身上的事：是这边的农夫就直接做，是连着的农场帮手就发过去在他那边做。返回有没有人处理。</summary>
	public bool ToFarmer<T>(long farmer, string op, T payload)
	{
		string json = JsonSerializer.Serialize(payload, Json);
		if (Game1.player != null && farmer == Game1.player.UniqueMultiplayerID)
		{
			return Handle(farmer, op, json, out _);
		}
		if (Context.IsMainPlayer && _linked.Contains(farmer))
		{
			Send(new Envelope { Op = op, Json = json }, OpType, farmer);
			return true;
		}
		return false;
	}

	/// <summary>房主：这位农夫在另一台电脑上、能收到同步的消息。</summary>
	public bool IsLinkedFarmhand(long farmer) => Context.IsMainPlayer && _linked.Contains(farmer);

	// ---------------- 生命周期 ----------------

	/// <summary>进了存档：农场帮手看房主装没装同一版本，装了就打招呼要一份全的。</summary>
	public void OnSaveLoaded()
	{
		Reset();
		if (!Remote)
		{
			return;
		}
		IMultiplayerPeer? host = _helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID);
		IMultiplayerPeerMod? mod = host?.GetMod(_modId);
		if (mod == null)
		{
			LocalOnly(Tr.Get("net.noMod", "房主没有装 AI 星露谷，这边的功能只在你自己这里。"));
			return;
		}
		if (mod.Version.ToString() != _version)
		{
			LocalOnly(Tr.Get("net.version", "房主的 AI 星露谷是 {{host}} 版，你的是 {{mine}} 版，版本不一样：这边的功能只在你自己这里。请装成同一版本。", ("host", mod.Version.ToString()), ("mine", _version)));
			return;
		}
		Linked = true;
		Send(new Envelope { Op = HelloType, Json = _version }, HelloType, Game1.MasterPlayer.UniqueMultiplayerID);
		_log.Info($"联机：房主也装着 {_version} 版，全村的事交给房主。");
	}

	public void OnReturnedToTitle() => Reset();

	public void OnPeerDisconnected(PeerDisconnectedEventArgs e) => _linked.Remove(e.Peer.PlayerID);

	/// <summary>房主：约一秒把改过的发出去；两边：等回话超时。</summary>
	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		if (_waiting.Count > 0)
		{
			foreach (string id in _waiting.Where(w => Game1.ticks > w.Value.Deadline).Select(w => w.Key).ToList())
			{
				Action<string?> reply = _waiting[id].Reply;
				_waiting.Remove(id);
				reply(null);
			}
		}
		if (!Context.IsMainPlayer || _linked.Count == 0 || ++_ticks % SyncTicks != 0 || Game1.newDay)
		{
			return;
		}
		Snapshot changed = Build(full: false);
		if (changed.Docs.Count > 0 || changed.Channels.Count > 0)
		{
			Send(changed, DocsType, _linked.ToArray());
		}
	}

	public void OnMessageReceived(ModMessageReceivedEventArgs e)
	{
		if (e.FromModID != _modId || !Context.IsWorldReady)
		{
			return;
		}
		try
		{
			switch (e.Type)
			{
				case HelloType when Context.IsMainPlayer:
					Greet(e.FromPlayerID, e.ReadAs<Envelope>().Json);
					break;
				case MismatchType when Remote:
					Linked = false;
					LocalOnly(e.ReadAs<Envelope>().Json);
					break;
				case DocsType when Remote && Linked:
					Apply(e.ReadAs<Snapshot>());
					break;
				case OpType when Context.IsMainPlayer ? _linked.Contains(e.FromPlayerID) : Linked:
					Envelope op = e.ReadAs<Envelope>();
					if (Handle(e.FromPlayerID, op.Op, op.Json, out string? reply) && op.Id.Length > 0)
					{
						Send(new Envelope { Op = op.Op, Json = reply ?? "null", Id = op.Id }, ReplyType, e.FromPlayerID);
					}
					break;
				case ReplyType:
					Envelope r = e.ReadAs<Envelope>();
					if (_waiting.Remove(r.Id, out var w))
					{
						w.Reply(r.Json == "null" ? null : r.Json);
					}
					break;
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"联机：处理 {e.Type} 时出错：{ex.Message}");
		}
	}

	/// <summary>调试：aisv_net [push]。</summary>
	public string Command(string[] args, Func<IEnumerable<string>> extra)
	{
		if (args.Length > 0 && args[0] == "push" && Context.IsMainPlayer)
		{
			_sentRevision.Clear();
			_sentChannel.Clear();
			Send(Build(full: true), DocsType, _linked.ToArray());
			return $"发给了 {_linked.Count} 位农场帮手。";
		}
		List<string> lines = new List<string>
		{
			Context.IsMainPlayer ? $"房主，连着 {_linked.Count} 位：{string.Join("、", _linked.Select(id => Game1.GetPlayer(id)?.Name ?? id.ToString()))}"
				: Remote ? (Linked ? "农场帮手，房主是同一版本，全村的事交给房主。" : "农场帮手，只在本机：" + LocalOnlyReason) : "同一台电脑上的分屏",
			"等回话：" + _waiting.Count
		};
		if (_store() is DocumentStore store)
		{
			lines.AddRange(_shared().Select(p => $"  {p}：{Hash(store.Export(p))}，第 {store.Revision(p)} 版{(_sentRevision.TryGetValue(p, out long r) ? $"（发过第 {r} 版）" : "")}"));
		}
		lines.AddRange(extra());
		return string.Join("\n", lines);
	}

	// ---------------- 内部 ----------------

	private static string Hash(string? text) => text == null ? "（没有）" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text))).Substring(0, 8);

	private void Reset()
	{
		Linked = false;
		LocalOnlyReason = null;
		_linked.Clear();
		_sentRevision.Clear();
		_sentChannel.Clear();
		_waiting.Clear();
	}

	private void LocalOnly(string reason)
	{
		LocalOnlyReason = reason;
		_log.Warn("联机：" + reason);
		Game1.addHUDMessage(new HUDMessage(reason, HUDMessage.error_type) { timeLeft = 9000 });
	}

	private void Greet(long farmer, string version)
	{
		if (version != _version)
		{
			_log.Warn($"联机：农场帮手 {Game1.GetPlayer(farmer)?.Name} 装的是 {version} 版，房主是 {_version} 版，不同步。");
			Send(new Envelope { Op = MismatchType, Json = Tr.Get("net.version", "房主的 AI 星露谷是 {{host}} 版，你的是 {{mine}} 版，版本不一样：这边的功能只在你自己这里。请装成同一版本。", ("host", _version), ("mine", version)) }, MismatchType, farmer);
			return;
		}
		_linked.Add(farmer);
		Snapshot full = Build(full: true);
		Send(full, DocsType, farmer);
		_log.Info($"联机：{Game1.GetPlayer(farmer)?.Name} 连上了，发了 {full.Docs.Count} 份全村数据。");
	}

	private Snapshot Build(bool full)
	{
		Snapshot s = new Snapshot { Full = full };
		if (_store() is DocumentStore store)
		{
			foreach (string path in _shared())
			{
				long rev = store.Revision(path);
				if (!full && _sentRevision.TryGetValue(path, out long sent) && sent == rev)
				{
					continue;
				}
				if (store.Export(path) is string text)
				{
					s.Docs[path] = text;
				}
				if (!full || !_sentRevision.ContainsKey(path))
				{
					_sentRevision[path] = rev;
				}
			}
		}
		foreach ((string key, (Func<string> export, _)) in _channels)
		{
			string value = export();
			if (full || !_sentChannel.TryGetValue(key, out string? last) || last != value)
			{
				s.Channels[key] = value;
				if (!full || !_sentChannel.ContainsKey(key))
				{
					_sentChannel[key] = value;
				}
			}
		}
		return s;
	}

	private void Apply(Snapshot s)
	{
		if (_store() is DocumentStore store)
		{
			foreach ((string path, string text) in s.Docs)
			{
				store.Import(path, text);
			}
		}
		foreach ((string key, string value) in s.Channels)
		{
			if (_channels.TryGetValue(key, out var c))
			{
				c.Import(value);
			}
		}
		if (s.Full)
		{
			_log.Info($"联机：收到房主的全村数据（{s.Docs.Count} 份）。");
		}
		Synced?.Invoke();
	}

	private bool Handle(long from, string op, string json, out string? reply)
	{
		reply = null;
		if (!_handlers.TryGetValue(op, out Func<long, string, string?>? handler))
		{
			_log.Debug($"联机：没人处理 {op}。");
			return false;
		}
		reply = handler(from, json);
		return true;
	}

	private void Send<T>(T message, string type, params long[] to) => _helper.Multiplayer.SendMessage(message, type, new[] { _modId }, to);

	public sealed class Envelope
	{
		public string Op { get; set; } = "";

		public string Json { get; set; } = "";

		public string Id { get; set; } = "";
	}

	public sealed class Snapshot
	{
		public bool Full { get; set; }

		public Dictionary<string, string> Docs { get; set; } = new Dictionary<string, string>();

		public Dictionary<string, string> Channels { get; set; } = new Dictionary<string, string>();
	}
}
