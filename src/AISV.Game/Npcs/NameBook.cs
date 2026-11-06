using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.TokenizableStrings;

namespace AISV.Game.Npcs;

/// <summary>
/// 内部名和显示名互换（提示词里用显示名，AI 回的名字要换回内部名）。缓存每天早上重建一次，只在主线程上建。
/// "作者.模组_名字" 这种内部名，显示时去掉前面的模组 ID。
/// </summary>
public sealed class NameBook
{
	private static readonly Regex NamespacedId = new Regex(@"\b(?:[A-Za-z0-9-]+\.)+[A-Za-z0-9-]+_([A-Za-z][A-Za-z0-9]*)\b", RegexOptions.Compiled);

	private Dictionary<string, string> _toDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, string> _toInternal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private Dictionary<string, string> _renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>在主线程上调用（进存档、每天早上、改了角色卡）。<paramref name="rename"/> 给出角色卡里改的名字。</summary>
	public void Rebuild(Func<string, string?>? rename = null)
	{
		Dictionary<string, string> toDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> toInternal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Utility.ForEachCharacter(npc =>
		{
			string name = npc.Name?.Trim() ?? "";
			string original = npc.displayName?.Trim() ?? "";
			if (name.Length == 0)
			{
				return true;
			}
			if (rename?.Invoke(name)?.Trim() is { Length: > 0 } custom)
			{
				renamed.TryAdd(name, custom);
				toInternal.TryAdd(custom, name);
			}
			if (original.Length > 0)
			{
				toDisplay.TryAdd(name, original);
				// 改了名也认原来的名字：别人的人设和 AI 的回复里还会用到。
				if (!string.Equals(name, original, StringComparison.OrdinalIgnoreCase))
				{
					toInternal.TryAdd(original, name);
				}
			}
			return true;
		});
		_toDisplay = toDisplay;
		_toInternal = toInternal;
		_renamed = renamed;
	}

	/// <summary>这个名字是不是已经有别的村民在用（显示名或改过的名字）。</summary>
	public bool TakenByOther(string name, string npc)
	{
		string n = name.Trim();
		bool other(string owner) => !string.Equals(owner, npc, StringComparison.OrdinalIgnoreCase);
		return n.Length > 0 && (_toInternal.TryGetValue(n, out string? owner) && other(owner) || _toDisplay.ContainsKey(n) && other(n));
	}

	public string Display(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return name;
		}
		string n = name.Trim();
		if (_renamed.TryGetValue(n, out string? custom))
		{
			return custom;
		}
		string stripped = StripNamespace(n);
		if (!string.Equals(stripped, n, StringComparison.Ordinal))
		{
			return stripped;
		}
		if (_toDisplay.TryGetValue(n, out string? display))
		{
			return display;
		}
		return DataDisplayName(n) is string fromData && fromData.Length > 0 ? fromData : n;
	}

	public string Internal(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return name;
		}
		string trimmed = name.Trim();
		string canonical = AISV.Core.Persona.BuiltInPersonas.Canonical(trimmed);
		if (!string.Equals(canonical, trimmed, StringComparison.OrdinalIgnoreCase) || AISV.Core.Persona.BuiltInPersonas.HasVanilla(trimmed))
		{
			// 别名或本来就是游戏村民的内部名：优先认它（别的 NPC 显示名碰巧叫这个也不换）。
			return canonical;
		}
		return _toInternal.TryGetValue(trimmed, out string? real) ? real : trimmed;
	}

	/// <summary>把文字里的内部名换成显示名，并去掉模组 ID 前缀。</summary>
	public string ReplaceInternalNames(string text, IEnumerable<string> names)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		foreach (string name in names)
		{
			string display = Display(name);
			if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, display, StringComparison.OrdinalIgnoreCase))
			{
				text = text.Replace(name, display, StringComparison.OrdinalIgnoreCase);
			}
		}
		return NamespacedId.Replace(text, "$1");
	}

	internal static string StripNamespace(string name)
	{
		int i = name.LastIndexOf('_');
		if (i <= 0 || i >= name.Length - 1)
		{
			return name;
		}
		string prefix = name.Substring(0, i);
		bool looksLikeId = prefix.Contains('.') && !prefix.Contains(' ') && !prefix.StartsWith("http", StringComparison.OrdinalIgnoreCase);
		return looksLikeId ? name.Substring(i + 1) : name;
	}

	private static string? DataDisplayName(string name)
	{
		if (Game1.characterData == null || !Game1.characterData.TryGetValue(name, out CharacterData? data) || string.IsNullOrWhiteSpace(data.DisplayName))
		{
			return null;
		}
		string raw = data.DisplayName.Trim();
		if (!raw.Contains('[') && !raw.Contains('\\') && !raw.Contains('/'))
		{
			return raw;
		}
		string parsed = TokenParser.ParseText(raw);
		return string.IsNullOrWhiteSpace(parsed) ? raw : parsed.Trim();
	}
}
