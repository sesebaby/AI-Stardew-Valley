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
			_open[path] = new Entry(value, type.Version, false);
			return value;
		}
	}

	/// <summary>整个替换并标脏。</summary>
	public void Put<T>(string path, DocumentType<T> type, T value) where T : class
	{
		lock (_lock)
		{
			_open[path] = new Entry(value, type.Version, true);
		}
	}

	/// <summary>标记这个文档改过了（必须先 Get 过）。</summary>
	public void MarkDirty(string path)
	{
		lock (_lock)
		{
			if (!_open.TryGetValue(path, out Entry? entry))
			{
				throw new InvalidOperationException($"文档 {path} 还没打开，不能标脏。");
			}
			entry.Dirty = true;
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
				JsonObject envelope = new JsonObject
				{
					["schema"] = entry.Version,
					["data"] = JsonSerializer.SerializeToNode(entry.Value, entry.Value.GetType(), Json)
				};
				pending.Add((path, entry, envelope.ToJsonString(Json)));
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

	private T Load<T>(string path, DocumentType<T> type) where T : class
	{
		string? text;
		try
		{
			text = _backend.Read(path);
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
	}
}
