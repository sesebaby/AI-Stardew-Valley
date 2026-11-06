using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AISV.Core.Compat;

/// <summary>
/// 社区的"AI 排除名单"（stardewmodding wiki 上的 AI_Mod_Exclusion.json）：NPC 作者声明不希望自己的角色被 AI 驱动。
/// 每条规则是一个 mod ID 通配符（* 和 ?，不分大小写），可以只点名其中几位 NPC；没点名就是这个 mod 加的所有 NPC。
/// </summary>
public sealed class OptOutRules
{
	private const int MaxRules = 5000;

	private const int MaxPatternsPerRule = 1000;

	private const int MaxPatternLength = 256;

	private readonly Rule[] _rules;

	private readonly IReadOnlyDictionary<string, string> _discoveredOwners;

	private OptOutRules(Rule[] rules, IReadOnlyDictionary<string, string>? discoveredOwners = null)
	{
		_rules = rules;
		_discoveredOwners = discoveredOwners ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public int RuleCount => _rules.Length;

	public int DiscoveredNpcCount => _discoveredOwners.Count;

	/// <summary>解析名单；格式不对抛 JsonException（调用方按"名单不可用"处理，不排除任何人）。</summary>
	public static OptOutRules Parse(string json)
	{
		static JsonException Bad(string why) => new JsonException(why);
		using JsonDocument doc = !string.IsNullOrWhiteSpace(json)
			? JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 })
			: throw Bad("AI 排除名单是空的。");
		JsonElement root = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : throw Bad("AI 排除名单的根必须是对象。");
		Dictionary<string, HashSet<string>> npcsByMod = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty row in root.EnumerateObject().Where(r => !r.Name.Equals("INSTRUCTIONS", StringComparison.OrdinalIgnoreCase)))
		{
			JsonElement body = row.Value.ValueKind == JsonValueKind.Object ? row.Value : throw Bad($"条目 '{row.Name}' 必须是对象。");
			JsonElement? idField = Property(body, "ModId");
			string mod = Normalize(idField?.ValueKind == JsonValueKind.String ? idField.Value.GetString() : null);
			if (mod.Length == 0)
			{
				throw Bad($"条目 '{row.Name}' 没有有效的 ModId。");
			}
			if (!npcsByMod.ContainsKey(mod) && npcsByMod.Count >= MaxRules)
			{
				throw Bad($"规则超过 {MaxRules} 条。");
			}
			HashSet<string> patterns = npcsByMod.TryGetValue(mod, out HashSet<string>? known) ? known : npcsByMod[mod] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string field in new[] { "NPC", "NPCs" })
			{
				AddNpcPatterns(body, field, patterns);
			}
		}
		return npcsByMod.Count > 0
			? new OptOutRules(npcsByMod.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new Rule(p.Key, p.Value.ToArray())).ToArray())
			: throw Bad("AI 排除名单里没有有效规则。");
	}

	/// <summary>这位 NPC 被排除了吗？返回命中的规则（mod ID 通配符或归属 mod），没命中返回 null。</summary>
	public string? MatchNpc(string npcName, IEnumerable<string> sourceModIds, IReadOnlyCollection<string> loadedModIds)
	{
		if (_discoveredOwners.TryGetValue(npcName, out string? owner))
		{
			return owner;
		}
		string[] sources = sourceModIds.Select(id => id?.Trim() ?? "").Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		foreach (Rule rule in _rules)
		{
			if (sources.Any(rule.MatchesModId) || rule.MatchesNamespacedNpc(npcName)
				|| (rule.NpcPatterns.Length > 0 && loadedModIds.Any(rule.MatchesModId) && rule.MatchesExplicitNpc(npcName)))
			{
				return rule.ModIdPattern;
			}
		}
		return null;
	}

	public string? MatchModId(string modId) => string.IsNullOrWhiteSpace(modId) ? null : _rules.FirstOrDefault(r => r.MatchesModId(modId))?.ModIdPattern;

	/// <summary>最具体的命中规则（通配符最少、最长）。</summary>
	public string? MostSpecificPattern(string modId) => _rules.Where(r => r.MatchesModId(modId))
		.OrderBy(r => r.ModIdPattern.Count(c => c is '*' or '?'))
		.ThenByDescending(r => r.ModIdPattern.Length)
		.Select(r => r.ModIdPattern)
		.FirstOrDefault();

	/// <summary>这个 mod 整个被排除（没点名 NPC），要扫它的文件才知道它加了哪些 NPC。</summary>
	public bool NeedsOwnershipScan(string modId) => _rules.Any(r => r.MatchesModId(modId) && r.NpcPatterns.Length == 0);

	public OptOutRules WithDiscoveredOwners(IReadOnlyDictionary<string, string> owners) => new OptOutRules(_rules, new Dictionary<string, string>(owners, StringComparer.OrdinalIgnoreCase));

	/// <summary>通配符匹配：* 任意多个字符，? 一个字符，不分大小写。</summary>
	public static bool Glob(string pattern, string value)
	{
		if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		int p = 0, v = 0, star = -1, mark = -1;
		while (v < value.Length)
		{
			if (p < pattern.Length && (pattern[p] == '?' || (pattern[p] != '*' && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v]))))
			{
				p++;
				v++;
			}
			else if (p < pattern.Length && pattern[p] == '*')
			{
				star = p++;
				mark = v;
			}
			else if (star >= 0)
			{
				p = star + 1;
				v = ++mark;
			}
			else
			{
				return false;
			}
		}
		while (p < pattern.Length && pattern[p] == '*')
		{
			p++;
		}
		return p == pattern.Length;
	}

	private static void AddNpcPatterns(JsonElement entry, string name, ISet<string> target)
	{
		if (Property(entry, name) is not JsonElement value)
		{
			return;
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			AddPattern(value.GetString(), target);
			return;
		}
		if (value.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException($"'{name}' 必须是字符串或字符串数组。");
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				throw new JsonException($"'{name}' 里只能有字符串。");
			}
			AddPattern(item.GetString(), target);
		}
	}

	private static void AddPattern(string? raw, ISet<string> target)
	{
		if (target.Count >= MaxPatternsPerRule)
		{
			throw new JsonException($"一条规则的 NPC 超过 {MaxPatternsPerRule} 个。");
		}
		string p = Normalize(raw);
		if (p.Length == 0)
		{
			throw new JsonException("名单里有无效的 NPC 名。");
		}
		target.Add(p);
	}

	private static string Normalize(string? raw)
	{
		string t = raw?.Trim() ?? "";
		return t.Length == 0 || t.Length > MaxPatternLength || t.Any(char.IsControl) ? "" : t;
	}

	private static JsonElement? Property(JsonElement obj, string name)
	{
		foreach (JsonProperty p in obj.EnumerateObject())
		{
			if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return p.Value;
			}
		}
		return null;
	}

	private sealed record Rule(string ModIdPattern, string[] NpcPatterns)
	{
		public bool MatchesModId(string modId) => Glob(ModIdPattern, modId);

		/// <summary>NPC 内部名是 "模组ID_名字" 的写法：任何一个下划线前的部分匹配就算。</summary>
		public bool MatchesNamespacedNpc(string npcName)
		{
			for (int i = npcName.IndexOf('_'); i > 0; i = npcName.IndexOf('_', i + 1))
			{
				if (Glob(ModIdPattern, npcName.Substring(0, i)))
				{
					return true;
				}
			}
			return false;
		}

		public bool MatchesExplicitNpc(string npcName) => NpcPatterns.Any(p => Glob(p, npcName));
	}
}
