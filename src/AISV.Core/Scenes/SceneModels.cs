using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AISV.Core.Scenes;

/// <summary>一个内置场景（assets/scenes/scenes.json 里的一条）。</summary>
public sealed class SceneDefinition
{
	[JsonIgnore]
	public IReadOnlyList<SceneActor> Actors { get; set; } = Array.Empty<SceneActor>();

	[JsonPropertyName("camera")]
	public int[] Camera { get; set; } = Array.Empty<int>();

	/// <summary>事件编号：游戏记"看过的事件"用。</summary>
	[JsonPropertyName("event_code")]
	public string EventCode { get; set; } = "";

	[JsonPropertyName("gesture")]
	public List<JsonElement>? Gesture { get; set; }

	[JsonPropertyName("hours")]
	public int[]? Hours { get; set; }

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	/// <summary>演完要记的记忆；没有就不记。</summary>
	[JsonPropertyName("keepsake")]
	public string? Keepsake { get; set; }

	[JsonIgnore]
	public IReadOnlyList<SceneLine> Lines { get; set; } = Array.Empty<SceneLine>();

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("needs")]
	public SceneConditions Needs { get; set; } = new SceneConditions();

	[JsonPropertyName("odds")]
	public int Odds { get; set; } = 100;

	[JsonPropertyName("place")]
	public string Place { get; set; } = "";

	/// <summary>站位：[名字, x, y, 朝向]，农夫写作 farmer。</summary>
	[JsonPropertyName("players")]
	public List<List<JsonElement>> Players { get; set; } = new List<List<JsonElement>>();

	[JsonPropertyName("reach")]
	public int? Reach { get; set; }

	/// <summary>每个存档最多演几次（0 = 不限）。</summary>
	[JsonPropertyName("repeats")]
	public int Repeats { get; set; } = 1;

	[JsonPropertyName("rest_days")]
	public int RestDays { get; set; }

	[JsonPropertyName("script_rows")]
	public List<List<string>> ScriptRows { get; set; } = new List<List<string>>();

	[JsonIgnore]
	public (string Who, int Id)? EmoteCue { get; set; }

	public int WindowMin => Hours is { Length: 2 } ? Hours[0] : 600;

	public int WindowMax => Hours is { Length: 2 } ? Hours[1] : 2600;

	public int MaxDistanceTiles => Reach ?? 12;

	public bool RecordsMemory => !string.IsNullOrWhiteSpace(Keepsake);

	/// <summary>说话的人，按第一次开口的先后；第一个领头。</summary>
	public IReadOnlyList<string> Speakers => Lines.Select(l => l.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	public string Lead => Speakers.FirstOrDefault() ?? "";
}

public sealed class SceneConditions
{
	[JsonPropertyName("after_days")]
	public int? AfterDays { get; set; }

	[JsonPropertyName("from_year")]
	public int? FromYear { get; set; }

	[JsonPropertyName("hearts_at_least")]
	public Dictionary<string, int> HeartsAtLeast { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("hearts_at_most")]
	public Dictionary<string, int> HeartsAtMost { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("in_seasons")]
	public List<string> InSeasons { get; set; } = new List<string>();

	/// <summary>Sunny / Rainy / Stormy / Snowy。</summary>
	[JsonPropertyName("in_weather")]
	public List<string> InWeather { get; set; } = new List<string>();

	[JsonPropertyName("luck")]
	public double Luck { get; set; } = 1.0;

	[JsonPropertyName("until_year")]
	public int? UntilYear { get; set; }
}

public sealed record SceneLine(string Speaker, string Text);

public sealed record SceneActor(string Name, int X, int Y, int Facing);

/// <summary>场景今天的记录（town/scene_log.json）。</summary>
public sealed class SceneState
{
	[JsonPropertyName("day_key")]
	public string DayKey { get; set; } = "";

	[JsonPropertyName("loaded")]
	public List<string> Loaded { get; set; } = new List<string>();

	[JsonPropertyName("played_today")]
	public List<string> PlayedToday { get; set; } = new List<string>();

	[JsonPropertyName("resting_until")]
	public Dictionary<string, int> RestingUntil { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("times_played")]
	public Dictionary<string, int> TimesPlayed { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public enum SceneDelivery
{
	Cinematic,
	WalkUp,
	Overheard
}

/// <summary>演法风格。</summary>
public static class SceneStyle
{
	public const string Natural = "Natural";

	public const string AmbientOnly = "AmbientOnly";

	public const string Cinematic = "Cinematic";

	public static readonly string[] All = { Natural, AmbientOnly, Cinematic };

	public static string Normalize(string? value) => All.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Natural;
}

/// <summary>当天挑场景要用的事实。</summary>
public sealed record SceneDay(int Year, string Season, int Day, int TotalDays, string Weather, string GameId, string SaveFolder, IReadOnlyDictionary<string, int> Hearts)
{
	public string Key => $"Y{Year}-{Season}-{Day}";

	public int HeartsOf(string npc) => Hearts.TryGetValue(npc, out int h) ? h : 0;
}

public enum MomentPlanKind
{
	Wait,
	Skip,
	WalkUp,
	Overheard
}

public sealed record MomentPlan(MomentPlanKind Kind, string Lead, IReadOnlyList<string> Speakers, string Reason);

public sealed record MomentPresence(string Npc, bool Nearby, bool CanApproach);

public enum MomentBlock
{
	None,
	WorldBusy,
	PlayerBusy,
	OutsideWindow,
	JustArrived,
	OutOfTries,
	TooSoon,
	SomethingElseRunning
}

public sealed record MomentReadiness(int Tick, int EnteredTick, int Time, int WindowMin, int WindowMax, bool WorldBusy, bool PlayerBusy, int Tries, int LastTryTick, bool OtherRunning);

/// <summary>日程里的一站：几点去哪。</summary>
public sealed record ScheduleStop(int Time, string Location);

/// <summary>旁听的一段气泡：谁、说什么、第几 tick 开始、显示多久。</summary>
public sealed record OverheardCue(string Speaker, string Text, int StartTick, int DurationTicks);
