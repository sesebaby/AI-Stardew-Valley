using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;

namespace AISV.Core.Persona;

/// <summary>
/// 玩家自己写的角色卡（mod 文件夹/Data/character-cards/&lt;村民内部名&gt;.json，全局，不分存档）。
/// 写了 Personality 就整个替换默认人设；只写外貌、背景、标签、说话方式时是在默认人设上补充。
/// </summary>
public sealed class CharacterCard
{
	public string Personality { get; set; } = "";

	public string Appearance { get; set; } = "";

	public string Lore { get; set; } = "";

	public string SocialTags { get; set; } = "";

	/// <summary>说话方式（口头禅、语气）。</summary>
	public string Speech { get; set; } = "";

	/// <summary>改名：AI 用这个名字称呼这位村民。</summary>
	public string DisplayName { get; set; } = "";

	[JsonIgnore]
	public bool Replaces => !string.IsNullOrWhiteSpace(Personality);

	/// <summary>手写或别人分享的卡里可能有 null，统一换成空串。</summary>
	public CharacterCard Normalized()
	{
		Personality ??= "";
		Appearance ??= "";
		Lore ??= "";
		SocialTags ??= "";
		Speech ??= "";
		DisplayName ??= "";
		return this;
	}

	public static string ToJson(CharacterCard card) => JsonSerializer.Serialize(card, CharacterCardStore.Json);

	/// <summary>从文字读一张卡（导入用）；读不懂返回 null。</summary>
	public static CharacterCard? FromJson(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<CharacterCard>(text.Trim(), CharacterCardStore.Json)?.Normalized();
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return null;
		}
	}

	[JsonIgnore]
	public bool IsEmpty => string.IsNullOrWhiteSpace(Personality) && string.IsNullOrWhiteSpace(Appearance) && string.IsNullOrWhiteSpace(Lore)
		&& string.IsNullOrWhiteSpace(SocialTags) && string.IsNullOrWhiteSpace(Speech) && string.IsNullOrWhiteSpace(DisplayName);
}

/// <summary>角色卡的读写：进存档时在后台预读一遍（<see cref="Preload"/>），之后只查内存；坏文件当没有，并记一条警告。</summary>
public sealed class CharacterCardStore
{
	internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private readonly IStorageBackend _backend;

	private readonly ILog _log;

	private readonly Dictionary<string, CharacterCard?> _cache = new Dictionary<string, CharacterCard?>(StringComparer.OrdinalIgnoreCase);

	public CharacterCardStore(IStorageBackend backend, ILog log)
	{
		_backend = backend;
		_log = log;
	}

	public CharacterCard? Get(string npcName)
	{
		lock (_cache)
		{
			if (_cache.TryGetValue(npcName, out CharacterCard? cached))
			{
				return cached;
			}
			CharacterCard? card = null;
			string? text;
			try
			{
				text = _backend.Read(PathOf(npcName));
			}
			catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
			{
				_log.Warn($"角色卡 {npcName}.json 读不了：{ex.Message}");
				text = null;
			}
			if (text != null)
			{
				try
				{
					card = JsonSerializer.Deserialize<CharacterCard>(text, Json)?.Normalized();
					if (card != null && card.IsEmpty)
					{
						card = null;
					}
				}
				catch (JsonException ex)
				{
					_log.Warn($"角色卡 {npcName}.json 格式不对，先不用它：{ex.Message}");
				}
			}
			_cache[npcName] = card;
			return card;
		}
	}

	/// <summary>只查已经读进内存的（主线程用，不读盘）。</summary>
	public CharacterCard? Cached(string npcName)
	{
		lock (_cache)
		{
			return _cache.TryGetValue(npcName, out CharacterCard? card) ? card : null;
		}
	}

	/// <summary>把文件夹里所有角色卡读进内存（后台线程调用），之后主线程上取人设不用读盘。</summary>
	public void Preload()
	{
		IReadOnlyList<string> files;
		try
		{
			files = _backend.List("");
		}
		catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
		{
			_log.Warn("角色卡文件夹读不了：" + ex.Message);
			return;
		}
		foreach (string file in files)
		{
			if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				Get(System.IO.Path.GetFileNameWithoutExtension(file));
			}
		}
	}

	/// <summary>保存；卡是空的就删掉文件（恢复默认）。</summary>
	public void Save(string npcName, CharacterCard card)
	{
		lock (_cache)
		{
			if (card.IsEmpty)
			{
				_backend.Delete(PathOf(npcName));
				_cache[npcName] = null;
				return;
			}
			_backend.Write(PathOf(npcName), JsonSerializer.Serialize(card, Json));
			_cache[npcName] = card;
		}
	}

	private static string PathOf(string npcName)
	{
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
		{
			npcName = npcName.Replace(c, '_');
		}
		return npcName + ".json";
	}
}
