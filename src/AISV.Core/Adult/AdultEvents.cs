using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AISV.Core.Storage;

namespace AISV.Core.Adult;

public sealed class AdultEvent
{
	public string Id { get; set; } = "";

	public string Location { get; set; } = "";

	public List<string> Actors { get; set; } = new List<string>();

	/// <summary>这段戏的另一半（只记给他们）。</summary>
	public List<string> Partners { get; set; } = new List<string>();

	public bool HasChildren { get; set; }

	/// <summary>换掉了游戏自带的心事件。</summary>
	public bool ReplacesVanilla { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public string Pack { get; set; } = "";
}

public sealed class AdultPack
{
	public string Id { get; set; } = "";

	public string Folder { get; set; } = "";

	/// <summary>这个包改了谁的对话。</summary>
	public List<string> DialogueNpcs { get; set; } = new List<string>();

	public List<AdultEvent> Events { get; set; } = new List<AdultEvent>();
}

/// <summary>
/// 成人事件清单：只有识别信息（包、事件 ID、地点、演员、另一半），没有原文。
/// 只有装了的包生效，不管成人模块开没开。
/// </summary>
public sealed class AdultCatalog
{
	private sealed class Doc
	{
		public List<AdultPack> Packs { get; set; } = new List<AdultPack>();
	}

	private List<AdultPack> _all = new List<AdultPack>();

	private List<AdultEvent> _active = new List<AdultEvent>();

	private HashSet<int> _replaced = new HashSet<int>();

	private HashSet<string> _touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyList<AdultPack> Known => _all;

	public IReadOnlyList<string> ActivePacks { get; private set; } = Array.Empty<string>();

	public IReadOnlyList<AdultEvent> ActiveEvents => _active;

	public bool AnyActive => ActivePacks.Count > 0;

	public void Load(string? json)
	{
		_all = new List<AdultPack>();
		if (!string.IsNullOrWhiteSpace(json))
		{
			_all = JsonSerializer.Deserialize<Doc>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip })?.Packs ?? new List<AdultPack>();
		}
		Activate(_ => false);
	}

	public void Activate(Func<string, bool> installed)
	{
		List<AdultPack> packs = _all.Where(p => installed(p.Id)).ToList();
		ActivePacks = packs.Select(p => p.Id).ToList();
		_active = new List<AdultEvent>();
		foreach (AdultPack p in packs)
		{
			foreach (AdultEvent e in p.Events ?? new List<AdultEvent>())
			{
				e.Pack = p.Id;
				_active.Add(e);
			}
		}
		_replaced = _active.Where(e => e.ReplacesVanilla).Select(e => int.TryParse(e.Id, out int id) ? id : -1).Where(id => id >= 0).ToHashSet();
		_touched = new HashSet<string>(packs.SelectMany(p => p.DialogueNpcs ?? new List<string>()).Concat(_active.SelectMany(e => e.Partners ?? new List<string>())), StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>被装着的成人包换掉的游戏自带心事件：心事件记忆、八卦、跟进对话都不用它。</summary>
	public bool IsReplacedHeartEvent(int eventId) => _replaced.Contains(eventId);

	/// <summary>装着的成人包改过这个人的对话或和他有事件：游戏自带台词参考不进提示词。</summary>
	public bool Touches(string? npc) => !string.IsNullOrEmpty(npc) && _touched.Contains(npc);

	public AdultEvent? Find(string? eventId, string? location) =>
		string.IsNullOrEmpty(eventId) ? null : _active.FirstOrDefault(e => e.Id == eventId && string.Equals(e.Location, location ?? "", StringComparison.OrdinalIgnoreCase));
}

public sealed class AdultMemory
{
	public string EventId { get; set; } = "";

	public string Pack { get; set; } = "";

	public string Location { get; set; } = "";

	public int Day { get; set; }

	public bool WithChildren { get; set; }

	public List<string> Partners { get; set; } = new List<string>();
}

public sealed class AdultMemoryLog
{
	public const string Path = "plans/private_moments.json";

	public static readonly DocumentType<AdultMemoryLog> Doc = new DocumentType<AdultMemoryLog>("成人事件记忆", 1, () => new AdultMemoryLog());

	public List<AdultMemory> Recorded { get; set; } = new List<AdultMemory>();
}

public static class AdultMemoryRules
{
	public const int PromptLimit = 3;

	/// <summary>不含原文的中性记忆；有孩子在场的只说"更亲近了"。</summary>
	public static string Neutral(string npc, AdultMemory r, string dateLabel, Func<string, string> display)
	{
		string place = Place(r.Location);
		List<string> others = r.Partners.Where(p => !string.Equals(p, npc, StringComparison.OrdinalIgnoreCase)).Select(display).ToList();
		string with = others.Count > 0 ? "和" + string.Join("、", others) + "一起" : "";
		if (r.WithChildren)
		{
			return $"{dateLabel}，你{with}和农夫{place}待了一阵，从那以后觉得和农夫更亲近了。";
		}
		return $"{dateLabel}，你{with}和农夫{place}有过一段私密、亲密的时光。这是你们之间的事：可以温柔、含蓄地提起，但绝不描述细节。";
	}

	public static string Place(string? location) => (location ?? "").ToLowerInvariant() switch
	{
		"farmhouse" => "在农夫家里",
		"farm" => "在农场上",
		"saloon" => "在酒吧",
		"beach" => "在海边",
		"forest" or "custom_forestwest" => "在森林里",
		"woods" => "在秘密森林",
		"mountain" => "在山边的湖畔",
		"town" => "在镇上",
		"bathhouse_pool" => "在澡堂",
		"seedshop" => "在杂货店",
		"leahhouse" => "在莉亚的小屋",
		"haleyhouse" => "在海莉和艾米丽家",
		"sciencehouse" => "在木匠家",
		"mine" => "在矿洞里",
		_ => ""
	};

	public static string PromptBlock(IEnumerable<string> lines)
	{
		List<string> list = lines.ToList();
		return list.Count == 0 ? "" : "【和农夫之间的私密时刻】\n" + string.Join("\n", list.Select(l => "- " + l));
	}
}
