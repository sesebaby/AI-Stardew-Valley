using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;

namespace AISV.Core.Compat;

/// <summary>一个已加载的 mod 和它的文件夹。</summary>
public sealed record ModFolder(string Id, string Directory);

/// <summary>
/// 找出每个 mod 往 Data/Characters 里加了哪些 NPC（内容包里 EditData 的条目）。
/// 只扫给定的 mod 文件夹，放在后台跑，
/// 结果按"文件数 + 总大小 + 最新修改时间"记在缓存里，mod 没变就不重读。
/// </summary>
public sealed class NpcOwnershipScanner
{
	private const int MaxJsonFilesPerMod = 5000;

	private const long MaxJsonFileBytes = 5_000_000;

	private const long MaxBytesPerMod = 250_000_000;

	private const string CachePath = "npc-owners.json";

	private readonly IStorageBackend _cache;

	private readonly ILog _log;

	private readonly Func<string, string?>? _lenientJson;

	/// <param name="lenientJson">标准 JSON 读不了时的宽松读法（游戏层用 SMAPI 自带的 Newtonsoft 转成标准 JSON）。</param>
	public NpcOwnershipScanner(IStorageBackend cache, ILog log, Func<string, string?>? lenientJson = null)
	{
		_cache = cache;
		_log = log;
		_lenientJson = lenientJson;
	}

	public sealed class CacheEntry
	{
		public string Directory { get; set; } = "";

		public string Fingerprint { get; set; } = "";

		public List<string> Npcs { get; set; } = new List<string>();
	}

	/// <summary>NPC 内部名 → 加它的 mod ID（同一个 NPC 被几个 mod 加时取 ID 排序最前的）。</summary>
	public IReadOnlyDictionary<string, string> Scan(IEnumerable<ModFolder> mods, out int rescanned)
	{
		Dictionary<string, CacheEntry> cache = LoadCache();
		// 合并写回：这次没扫到的 mod（另一组调用扫的）条目保留，不然两条路径会互相把缓存冲掉。
		Dictionary<string, CacheEntry> next = new Dictionary<string, CacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
		int before = cache.Count;
		Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		rescanned = 0;
		foreach (ModFolder mod in mods.Where(m => !string.IsNullOrWhiteSpace(m.Id) && System.IO.Directory.Exists(m.Directory)).OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				List<FileInfo> files = JsonFiles(mod.Directory);
				string fingerprint = Fingerprint(files);
				if (!cache.TryGetValue(mod.Id, out CacheEntry? entry) || entry.Fingerprint != fingerprint || !string.Equals(entry.Directory, mod.Directory, StringComparison.OrdinalIgnoreCase))
				{
					entry = new CacheEntry { Directory = mod.Directory, Fingerprint = fingerprint, Npcs = ReadNpcs(mod.Directory, files).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() };
					rescanned++;
				}
				next[mod.Id] = entry;
				foreach (string npc in entry.Npcs)
				{
					owners.TryAdd(npc, mod.Id);
				}
			}
			catch (InvalidDataException ex)
			{
				_log.Warn($"跳过 {mod.Id} 的 NPC 归属扫描：{ex.Message}");
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_log.Warn($"跳过 {mod.Id} 的 NPC 归属扫描：{ex.Message}");
			}
		}
		if (rescanned > 0 || next.Count != before)
		{
			SaveCache(next);
		}
		return owners;
	}

	internal IEnumerable<string> ReadNpcs(string directory, List<FileInfo> files)
	{
		HashSet<string> npcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		long total = 0;
		foreach (FileInfo file in files)
		{
			total += file.Length;
			if (total > MaxBytesPerMod)
			{
				throw new InvalidDataException($"文件夹 {directory} 超过扫描的总大小上限。");
			}
			string text;
			try
			{
				if (file.Length > MaxJsonFileBytes)
				{
					continue;
				}
				text = File.ReadAllText(file.FullName);
			}
			catch (IOException)
			{
				continue;
			}
			// 先做便宜的文字查找，提到 Data/Characters 的文件才解析。
			if (text.IndexOf("Data/Characters", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			using JsonDocument? doc = Parse(text);
			if (doc != null)
			{
				FindCharacterEntries(doc.RootElement, npcs);
			}
		}
		return npcs;
	}

	private JsonDocument? Parse(string text)
	{
		JsonDocumentOptions options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 128 };
		try
		{
			return JsonDocument.Parse(text, options);
		}
		catch (JsonException)
		{
			// 内容包由 SMAPI 宽松读取（SVE 有些文件标准 JSON 读不了），这里也用宽松读法再试一次；还不行就跳过这个文件。
			try
			{
				string? normalized = _lenientJson?.Invoke(text);
				return normalized == null ? null : JsonDocument.Parse(normalized, options);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}

	/// <summary>{ "Action": "EditData", "Target": "Data/Characters", "Entries": { "名字": { 像新 NPC 的定义 } } }</summary>
	internal static void FindCharacterEntries(JsonElement element, ISet<string> npcs)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			// 带 TargetField(s) 的补丁改的是已有条目的某个字段，不算新增 NPC。
			if (Str(element, "Action") is string action && action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
				&& Prop(element, "TargetField") == null && Prop(element, "TargetFields") == null
				&& Str(element, "Target") is string target && target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(t => t.Equals("Data/Characters", StringComparison.OrdinalIgnoreCase))
				&& Prop(element, "Entries") is { ValueKind: JsonValueKind.Object } entries)
			{
				foreach (JsonProperty e in entries.EnumerateObject())
				{
					string name = e.Name.Trim();
					if (name.Length > 0 && name.Length <= 256 && !name.Contains("{{", StringComparison.Ordinal) && !name.Any(char.IsControl) && LooksLikeNewNpc(e.Value))
					{
						npcs.Add(name);
					}
				}
			}
			foreach (JsonProperty p in element.EnumerateObject())
			{
				FindCharacterEntries(p.Value, npcs);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				FindCharacterEntries(item, npcs);
			}
		}
	}

	/// <summary>新 NPC 的定义：SpawnIfMissing，或有住所，或有显示名加生日/性别/年龄。只改已有 NPC 几个字段的补丁不算。</summary>
	private static bool LooksLikeNewNpc(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		if (Prop(value, "SpawnIfMissing") is JsonElement spawn)
		{
			if (spawn.ValueKind == JsonValueKind.True)
			{
				return true;
			}
			if (spawn.ValueKind == JsonValueKind.String && spawn.GetString()?.Trim() is string s && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Contains("{{", StringComparison.Ordinal)))
			{
				return true;
			}
		}
		return Prop(value, "Home") != null || (Prop(value, "DisplayName") != null && (Prop(value, "BirthSeason") != null || Prop(value, "Gender") != null || Prop(value, "Age") != null));
	}

	private static List<FileInfo> JsonFiles(string directory)
	{
		List<FileInfo> files = new DirectoryInfo(directory).EnumerateFiles("*.json", SearchOption.AllDirectories).Take(MaxJsonFilesPerMod + 1).ToList();
		if (files.Count > MaxJsonFilesPerMod)
		{
			throw new InvalidDataException($"JSON 文件超过 {MaxJsonFilesPerMod} 个。");
		}
		return files;
	}

	private static string Fingerprint(List<FileInfo> files) => $"{files.Count}:{files.Sum(f => f.Length)}:{(files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks))}";

	private Dictionary<string, CacheEntry> LoadCache()
	{
		try
		{
			string? text = _cache.Read(CachePath);
			if (text != null && JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(text) is { } saved)
			{
				return new Dictionary<string, CacheEntry>(saved, StringComparer.OrdinalIgnoreCase);
			}
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_log.Debug("NPC 归属缓存读不了，重新扫描：" + ex.Message);
		}
		return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
	}

	private void SaveCache(Dictionary<string, CacheEntry> entries)
	{
		try
		{
			_cache.Write(CachePath, JsonSerializer.Serialize(entries));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Debug("NPC 归属缓存写不进去：" + ex.Message);
		}
	}

	private static string? Str(JsonElement obj, string name) => Prop(obj, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

	private static JsonElement? Prop(JsonElement obj, string name)
	{
		if (obj.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty p in obj.EnumerateObject())
			{
				if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return p.Value;
				}
			}
		}
		return null;
	}
}
