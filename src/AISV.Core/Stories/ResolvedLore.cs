using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AISV.Core.Storage;

namespace AISV.Core.Stories;

public sealed class ResolvedLoreEntry
{
	[JsonPropertyName("how_farmer_did_it")]
	public string HowFarmerDidIt { get; set; } = "";

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("origins")]
	public List<string> Origins { get; set; } = new List<string>();

	[JsonPropertyName("settled")]
	public string Settled { get; set; } = "";

	[JsonPropertyName("story_id")]
	public string StoryId { get; set; } = "";

	[JsonPropertyName("story_name")]
	public string StoryName { get; set; } = "";

	[JsonPropertyName("touched")]
	public List<string> Touched { get; set; } = new List<string>();

	[JsonPropertyName("written_on")]
	public string WrittenOn { get; set; } = "";
}

/// <summary>已定传说（town/settled_lore.json）：小剧场的结局成了镇上的定论，以后谁说话都不能和它矛盾。</summary>
public sealed class ResolvedLore
{
	public const string Path = "town/settled_lore.json";

	public const int NpcLimit = 5;

	private static readonly DocumentType<List<ResolvedLoreEntry>> Doc = new DocumentType<List<ResolvedLoreEntry>>("已定传说", 1, () => new List<ResolvedLoreEntry>());

	/// <summary>带这些标签的传说说明原本已婚的村民在故事里单身了。</summary>
	public static readonly HashSet<string> SingleTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "relationship_breakup", "divorced", "separated", "single_after_arc", "story_single" };

	private static readonly HashSet<string> GlobalTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "global", "world", "village", "town" };

	private readonly Func<DocumentStore?> _store;

	private readonly Func<string, bool> _npcEnabled;

	private readonly Func<string, bool> _modEnabled;

	public ResolvedLore(Func<DocumentStore?> store, Func<string, bool> npcEnabled, Func<string, bool> modEnabled)
	{
		_store = store;
		_npcEnabled = npcEnabled;
		_modEnabled = modEnabled;
	}

	/// <summary>逐出记录的提示块（接在传说后面）。</summary>
	public Func<string>? Expulsions { get; set; }

	public IReadOnlyList<ResolvedLoreEntry> All => List() ?? new List<ResolvedLoreEntry>();

	public void Add(StoryArc arc, GameDateText date)
	{
		List<ResolvedLoreEntry>? list = List();
		if (list == null)
		{
			return;
		}
		list.Add(new ResolvedLoreEntry
		{
			StoryId = arc.Id,
			StoryName = arc.Title,
			Settled = arc.Closure ?? "",
			Touched = new List<string>(arc.Cast),
			Origins = arc.Sources.ToList(),
			Labels = arc.Tags != null ? new List<string>(arc.Tags) : new List<string>(),
			WrittenOn = date.Value,
			HowFarmerDidIt = string.Join("; ", arc.Goals.Where(o => !string.IsNullOrEmpty(o.DoneHow)).Select(o => o.Text + ": " + o.DoneHow))
		});
		Dirty();
	}

	/// <summary>和这位村民有关、能给 AI 用的传说。</summary>
	public List<ResolvedLoreEntry> RelevantTo(string npc) => All.Where(Usable).Where(e => Relevant(e, npc)).ToList();

	public bool IsStorySingle(string npc) => RelevantTo(npc).Any(e => (e.Labels ?? new List<string>()).Any(SingleTags.Contains));

	/// <summary>给村民的提示块：和他有关的最近几条，再接上逐出记录。</summary>
	public string PromptBlock(string npc, Func<string, string> display, int max = NpcLimit)
	{
		List<ResolvedLoreEntry> entries = RelevantTo(npc).Where(e => !string.IsNullOrWhiteSpace(e.Settled)).TakeLast(Math.Clamp(max, 0, 20)).ToList();
		string lore = entries.Count == 0 ? "" : Format(entries, display);
		string expelled = Expulsions?.Invoke() ?? "";
		return string.IsNullOrWhiteSpace(expelled) ? lore : string.IsNullOrWhiteSpace(lore) ? expelled : lore + "\n\n" + expelled;
	}

	public static string Format(IEnumerable<ResolvedLoreEntry> entries, Func<string, string> display)
	{
		List<string> lines = new List<string> { "【已成定局的往事】", "下面每件都已经发生、全镇公认。记忆或八卦里要是冒出和它打架的说法，那是过时的消息。", "" };
		foreach (ResolvedLoreEntry e in entries)
		{
			string who = e.Touched == null || e.Touched.All(string.IsNullOrWhiteSpace) ? "全镇" : string.Join("、", e.Touched.Where(n => !string.IsNullOrWhiteSpace(n)).Select(display));
			string title = string.IsNullOrWhiteSpace(e.StoryName) ? "无题" : Trim(e.StoryName, 80);
			lines.Add($"• {Trim(e.Settled, 280)}（出自「{title}」，牵扯：{who}）");
		}
		return string.Join("\n", lines);
	}

	public static bool Relevant(ResolvedLoreEntry e, string npc)
	{
		List<string> affected = e.Touched ?? new List<string>();
		return affected.Count == 0
			|| affected.Any(a => string.Equals(a?.Trim(), npc.Trim(), StringComparison.OrdinalIgnoreCase))
			|| (e.Labels ?? new List<string>()).Any(GlobalTags.Contains);
	}

	private bool Usable(ResolvedLoreEntry e)
	{
		List<string> affected = e.Touched ?? new List<string>();
		return (affected.Count == 0 || affected.Where(n => !string.IsNullOrWhiteSpace(n)).All(_npcEnabled))
			&& (e.Origins ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).All(_modEnabled);
	}

	private static string Trim(string text, int max)
	{
		string t = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		return t.Length <= max ? t : t[..Math.Max(0, max - 3)].TrimEnd() + "...";
	}

	private List<ResolvedLoreEntry>? List() => _store()?.Get(Path, Doc);

	private void Dirty() => _store()?.MarkDirty(Path);
}

/// <summary>存进数据里的日期写法（"spring 3, Year 1"）。</summary>
public readonly record struct GameDateText(string Value)
{
	public static GameDateText Of(AISV.Core.Dialogue.GameDate d) => new GameDateText($"{d.Season} {d.Day}, Year {d.Year}");
}
