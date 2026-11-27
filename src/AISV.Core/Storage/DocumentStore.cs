using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using AISV.Core.Diagnostics;

namespace AISV.Core.Storage;

/// <summary>一个存档的数据文档：<c>{"schema": 版本号, "data": {...}}</c>，版本低的按 <see cref="DocumentType{T}.Upgrade"/> 升级；坏文件留一份 .corrupt 备份后换成空文档。</summary>
public sealed class DocumentStore
{
	internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private readonly IStorageBackend _backend;

	private readonly ILog _log;

	private readonly object _lock = new object();

	private readonly Dictionary<string, Entry> _open = new Dictionary<string, Entry>(StringComparer.Ordinal);

	/// <summary>联机时房主发来、还没读过的文档（原样的文件内容），下次读时用它代替盘上的。</summary>
	private readonly Dictionary<string, string> _incoming = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>换成过房主那份的文档：这边改它不算数，标脏时不必先读过。</summary>
	private readonly HashSet<string> _imported = new HashSet<string>(StringComparer.Ordinal);

	private long _revision;

	public DocumentStore(IStorageBackend backend, ILog? log = null)
	{
		_backend = backend;
		_log = log ?? NullLog.Instance;
	}

	public bool IsDirty
	{
		get
		{
			lock (_lock)
			{
				return _open.Values.Any(e => e.Dirty);
			}
		}
	}

	/// <summary>读文档（第一次读盘，之后用缓存）。文件不存在时返回新的空文档（不会自动标脏）。</summary>
	public T Get<T>(string path, DocumentType<T> type) where T : class
	{
		lock (_lock)
		{
			if (_open.TryGetValue(path, out Entry? entry))
			{
				return entry.Value as T ?? throw new InvalidOperationException($"文档 {path} 已经按另一种类型打开了。");
			}
			T value = Load(path, type);
			_open[path] = new Entry(value, type.Version, false) { Revision = ++_revision };
			return value;
		}
	}

	/// <summary>整个替换并标脏。</summary>
	public void Put<T>(string path, DocumentType<T> type, T value) where T : class
	{
		lock (_lock)
		{
			_open[path] = new Entry(value, type.Version, true) { Revision = ++_revision };
		}
	}

	/// <summary>标记这个文档改过了（必须先 Get 过）。</summary>
	public void MarkDirty(string path)
	{
		lock (_lock)
		{
			if (!_open.TryGetValue(path, out Entry? entry))
			{
				if (_imported.Contains(path))
				{
					return;
				}
				throw new InvalidOperationException($"文档 {path} 还没打开，不能标脏。");
			}
			entry.Dirty = true;
			entry.Revision = ++_revision;
		}
	}

	/// <summary>
	/// 把改过的文档写盘，返回写成功了几个。写失败的保持“改过”，下次保存再试。
	/// <paramref name="only"/> 不为空时只写这几个（游戏已经存过档之后才算出来的结果用：属于已经存下的那一天，不该等下一次存档）。
	/// </summary>
	public int Commit(IReadOnlyCollection<string>? only = null, IReadOnlyCollection<string>? skip = null)
	{
		List<(string Path, Entry Entry, string Content)> pending = new List<(string, Entry, string)>();
		lock (_lock)
		{
			foreach ((string path, Entry entry) in _open)
			{
				if (!entry.Dirty || only != null && !only.Contains(path) || skip != null && skip.Contains(path))
				{
					continue;
				}
				pending.Add((path, entry, Envelope(entry)));
				entry.Dirty = false;
			}
		}
		int written = 0;
		foreach ((string path, Entry entry, string content) in pending)
		{
			try
			{
				_backend.Write(path, content);
				written++;
			}
			catch (Exception ex)
			{
				entry.Dirty = true;
				_log.Error($"写不了 {path}：{ex.Message}；下次保存再试。");
			}
		}
		return written;
	}

	/// <summary>丢掉内存里的所有文档和没写盘的改动。</summary>
	public void Discard()
	{
		lock (_lock)
		{
			_open.Clear();
			_incoming.Clear();
			_imported.Clear();
		}
	}

	/// <summary>这个文档改到第几版了（每次读进来、标脏、替换都会变；没打开时为 0）。联机时房主据此判断要不要再发一次。</summary>
	public long Revision(string path)
	{
		lock (_lock)
		{
			return _open.TryGetValue(path, out Entry? entry) ? entry.Revision : 0;
		}
	}

	/// <summary>文档现在的完整内容（和写盘时一样）；没打开就读盘上的，都没有时为 null。</summary>
	public string? Export(string path)
	{
		lock (_lock)
		{
			if (_open.TryGetValue(path, out Entry? entry))
			{
				return Envelope(entry);
			}
			if (_incoming.TryGetValue(path, out string? pending))
			{
				return pending;
			}
		}
		try
		{
			return _backend.Read(path);
		}
		catch (Exception ex)
		{
			_log.Debug($"读不了 {path}：{ex.Message}");
			return null;
		}
	}

	/// <summary>换成别处（联机的房主）发来的内容：丢掉这边的，下次读时用它。这边以后的改动不会写盘以外的地方，也不必先读过才能标脏。</summary>
	public void Import(string path, string content)
	{
		lock (_lock)
		{
			_open.Remove(path);
			_incoming[path] = content;
			_imported.Add(path);
		}
	}

	/// <summary>目录下有哪些文档（相对路径）：盘上的加上这次新建、还没写盘的。</summary>
	public IReadOnlyList<string> List(string directory)
	{
		string prefix = directory.TrimEnd('/') + "/";
		lock (_lock)
		{
			return _backend.List(directory)
				.Concat(_open.Where(p => p.Value.Dirty && p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Key.IndexOf('/', prefix.Length) < 0).Select(p => p.Key))
				.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
		}
	}

	private static string Envelope(Entry entry) => new JsonObject
	{
		["schema"] = entry.Version,
		["data"] = JsonSerializer.SerializeToNode(entry.Value, entry.Value.GetType(), Json)
	}.ToJsonString(Json);

	private T Load<T>(string path, DocumentType<T> type) where T : class
	{
		string? text;
		try
		{
			text = _incoming.Remove(path, out string? incoming) ? incoming : _backend.Read(path);
		}
		catch (Exception ex)
		{
			_log.Warn($"读不了 {type.Name}（{path}）：{ex.Message}；这次用空的。");
			return type.CreateDefault();
		}
		if (text == null)
		{
			return type.CreateDefault();
		}
		try
		{
			JsonNode? root = JsonNode.Parse(text);
			int version = (root as JsonObject)?["schema"]?.GetValue<int>() ?? 0;
			JsonNode? data = (root as JsonObject)?["data"];
			if (data == null || version < 1)
			{
				throw new JsonException("缺少 schema 或 data。");
			}
			if (version > type.Version)
			{
				throw new JsonException($"文件是新版本（{version}）写的，当前只认到 {type.Version}。");
			}
			if (version < type.Version && type.Upgrade != null)
			{
				data = type.Upgrade(JsonNode.Parse(data!.ToJsonString())!, version);
				_log.Info($"{type.Name}（{path}）从第 {version} 版升级到第 {type.Version} 版。");
			}
			return data.Deserialize<T>(Json) ?? type.CreateDefault();
		}
		catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
		{
			string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
			try
			{
				_backend.Write(backup, text);
			}
			catch (Exception)
			{
				// 备份失败也继续，别让游戏崩。
			}
			_log.Warn($"{type.Name}（{path}）读不懂：{ex.Message}；已备份为 {backup}，这次用空的。");
			return type.CreateDefault();
		}
	}

	private sealed class Entry
	{
		public Entry(object value, int version, bool dirty)
		{
			Value = value;
			Version = version;
			Dirty = dirty;
		}

		public object Value { get; }

		public int Version { get; }

		public bool Dirty { get; set; }

		public long Revision { get; set; }
	}
}
