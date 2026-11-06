using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AISV.Core.Stories;

/// <summary>一出小剧场（故事线）：几位村民之间正在展开的事，玩家靠和他们说话推进，最后选个结局收尾。</summary>
public sealed class StoryArc
{
	public const string Active = "active";

	public const string Completable = "completable";

	public const string Resolved = "resolved";

	public const string Fizzled = "fizzled";

	[JsonPropertyName("afterwords")]
	public Dictionary<string, string>? Afterwords { get; set; }

	[JsonPropertyName("afterwords_spoken")]
	public HashSet<string> AfterwordsSpoken { get; set; } = new HashSet<string>();

	[JsonPropertyName("born_season")]
	public string BornSeason { get; set; } = "";

	[JsonPropertyName("born_year")]
	public int BornYear { get; set; }

	[JsonPropertyName("cast")]
	public List<string> Cast { get; set; } = new List<string>();

	[JsonPropertyName("chosen_ending")]
	public string? ChosenEnding { get; set; }

	[JsonPropertyName("closure")]
	public string? Closure { get; set; }

	[JsonPropertyName("ended_day")]
	public int? EndedDay { get; set; }

	[JsonPropertyName("ended_on")]
	public int? EndedOn { get; set; }

	[JsonPropertyName("endings")]
	public List<ArcChoiceOption> Endings { get; set; } = new List<ArcChoiceOption>();

	[JsonPropertyName("final_scene")]
	public List<ConclusionLine>? FinalScene { get; set; }

	[JsonPropertyName("final_stage")]
	public ConclusionStage? FinalStage { get; set; }

	[JsonPropertyName("goals")]
	public List<ArcObjective> Goals { get; set; } = new List<ArcObjective>();

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "standard";

	[JsonPropertyName("leaving_how")]
	public string? LeavingHow { get; set; }

	[JsonPropertyName("leaving_news")]
	public string? LeavingNews { get; set; }

	[JsonPropertyName("leaving_npc")]
	public string? LeavingNpc { get; set; }

	[JsonPropertyName("leaving_why")]
	public string? LeavingWhy { get; set; }

	[JsonPropertyName("main_mod")]
	public string MainMod { get; set; } = "";

	[JsonPropertyName("mods")]
	public List<string> Mods { get; set; } = new List<string>();

	[JsonPropertyName("outcome")]
	public string? Outcome { get; set; }

	[JsonPropertyName("premise")]
	public string Premise { get; set; } = "";

	[JsonPropertyName("stage")]
	public string Stage { get; set; } = "";

	[JsonPropertyName("stage_place")]
	public string? StagePlace { get; set; }

	[JsonPropertyName("state")]
	public string State { get; set; } = Active;

	[JsonPropertyName("tags")]
	public List<string>? Tags { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; } = "";

	[JsonIgnore]
	public bool IsExpulsion => string.Equals(Kind, "expulsion", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool IsReturn => string.Equals(Kind, "expulsion_return", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool AllObjectivesDone => State == Active && Goals.Count > 0 && Goals.All(o => o.Done);

	[JsonIgnore]
	public int DoneCount => Goals.Count(o => o.Done);

	/// <summary>来源 mod（合并两个字段，去重排序）。</summary>
	[JsonIgnore]
	public IReadOnlyList<string> Sources => (Mods ?? new List<string>()).Append(MainMod)
		.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
		.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();

	public void MergeSources(IEnumerable<string> more)
	{
		Mods = Sources.Concat(more).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
		MainMod = Mods.FirstOrDefault() ?? "";
	}

	public bool Involves(string npc) => Cast.Any(n => string.Equals(n, npc, StringComparison.OrdinalIgnoreCase));
}

public sealed class ArcObjective
{
	[JsonPropertyName("done")]
	public bool Done { get; set; }

	[JsonPropertyName("done_by")]
	public string? DoneBy { get; set; }

	[JsonPropertyName("done_how")]
	public string? DoneHow { get; set; }

	[JsonPropertyName("done_on")]
	public string? DoneOn { get; set; }

	[JsonPropertyName("done_when")]
	public string DoneWhen { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("item")]
	public string? Item { get; set; }

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	[JsonPropertyName("npc")]
	public string Npc { get; set; } = "";

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("topic")]
	public string? Topic { get; set; }
}

public sealed class ArcChoiceOption
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("label")]
	public string Label { get; set; } = "";

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("outcome")]
	public string Outcome { get; set; } = "";
}

public sealed class ConclusionLine
{
	[JsonPropertyName("who")]
	public string Who { get; set; } = "";

	[JsonPropertyName("line")]
	public string Line { get; set; } = "";

	[JsonPropertyName("feeling")]
	public string Feeling { get; set; } = "neutral";
}

/// <summary>结局场景的舞台：在哪演、适合什么故事、几个人、站位。</summary>
public sealed class ConclusionStage
{
	[JsonPropertyName("backdrop")]
	public StageBackground Backdrop { get; set; } = new StageBackground();

	[JsonPropertyName("blocking")]
	public StageLayout? Blocking { get; set; }

	[JsonPropertyName("fewest_voices")]
	public int FewestVoices { get; set; } = 1;

	[JsonPropertyName("key")]
	public string Key { get; set; } = "";

	[JsonPropertyName("moods")]
	public List<string> Moods { get; set; } = new List<string>();

	[JsonPropertyName("most_voices")]
	public int MostVoices { get; set; } = 3;

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("not_for")]
	public List<string> NotFor { get; set; } = new List<string>();

	[JsonPropertyName("only_for")]
	public List<string> OnlyFor { get; set; } = new List<string>();

	[JsonPropertyName("spots")]
	public List<StagePlacement> Spots { get; set; } = new List<StagePlacement>();

	[JsonPropertyName("tune")]
	public string Tune { get; set; } = "";

	[JsonPropertyName("venue")]
	public string Venue { get; set; } = "";
}

public sealed class StageBackground
{
	[JsonPropertyName("accent")]
	public string Accent { get; set; } = "c89248";

	[JsonPropertyName("base")]
	public string Base { get; set; } = "283342";

	[JsonPropertyName("feel")]
	public string Feel { get; set; } = "interior";

	[JsonPropertyName("ground")]
	public string Ground { get; set; } = "3d4654";

	[JsonPropertyName("second")]
	public string Second { get; set; } = "18202b";

	[JsonPropertyName("style")]
	public string Style { get; set; } = "vanilla_event";

	[JsonPropertyName("trim")]
	public string Trim { get; set; } = "d8c58a";
}

public sealed class StagePlacement
{
	[JsonPropertyName("dir")]
	public string Dir { get; set; } = "down";

	[JsonPropertyName("role")]
	public string Role { get; set; } = "";

	[JsonPropertyName("size")]
	public float Size { get; set; } = 1f;

	[JsonPropertyName("x")]
	public float X { get; set; } = 0.5f;

	[JsonPropertyName("y")]
	public float Y { get; set; } = 0.5f;
}

public sealed class StageLayout
{
	[JsonPropertyName("hero")]
	public StagePoint Hero { get; set; } = new StagePoint();

	[JsonPropertyName("lens")]
	public StagePoint Lens { get; set; } = new StagePoint();

	[JsonPropertyName("others")]
	public List<StageNpcPoint> Others { get; set; } = new List<StageNpcPoint>();
}

public class StagePoint
{
	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("facing")]
	public string Facing { get; set; } = "down";
}

public sealed class StageNpcPoint : StagePoint
{
	[JsonPropertyName("slot")]
	public string Slot { get; set; } = "";
}

/// <summary>村民常去的地方（结局场景挑地点用）。</summary>
public sealed class LocationAffinity
{
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "habit";

	[JsonPropertyName("place")]
	public string Place { get; set; } = "";

	[JsonPropertyName("pull")]
	public int Pull { get; set; } = 1;

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

public sealed class LocationAffinityFile
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = 1;

	[JsonPropertyName("npcs")]
	public Dictionary<string, List<LocationAffinity>> Npcs { get; set; } = new Dictionary<string, List<LocationAffinity>>(StringComparer.OrdinalIgnoreCase);
}
