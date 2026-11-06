using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AISV.Core.Social.Personality;

/// <summary>一位村民在这个存档里慢慢变化的性格（bonds/temperament.json）。</summary>
public sealed class NpcPersonalityState
{
	[JsonPropertyName("npc")]
	public string Npc { get; set; } = "";

	[JsonPropertyName("updated_on")]
	public int UpdatedOn { get; set; }

	[JsonPropertyName("self_portrait")]
	public string SelfPortrait { get; set; } = "";

	[JsonPropertyName("inner_conflict")]
	public string InnerConflict { get; set; } = "";

	/// <summary>openness / kindness / resentment / honesty / social_confidence / vanity / romantic_openness。</summary>
	[JsonPropertyName("traits")]
	public Dictionary<string, PersonalityTraitState> Traits { get; set; } = new Dictionary<string, PersonalityTraitState>(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("pressures")]
	public List<PersonalityModifier> Pressures { get; set; } = new List<PersonalityModifier>();

	[JsonPropertyName("turning_points")]
	public List<PersonalityMilestone> TurningPoints { get; set; } = new List<PersonalityMilestone>();

	[JsonPropertyName("identity")]
	public NpcIdentityProfile Identity { get; set; } = new NpcIdentityProfile();
}

public sealed class PersonalityTraitState
{
	/// <summary>-1～1。</summary>
	[JsonPropertyName("level")]
	public float Level { get; set; }

	[JsonPropertyName("certainty")]
	public float Certainty { get; set; }

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";

	[JsonPropertyName("changed_on")]
	public int ChangedOn { get; set; }
}

/// <summary>性格上的一个转折点。</summary>
public sealed class PersonalityMilestone
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("on_day")]
	public int OnDay { get; set; }

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("private")]
	public bool Private { get; set; } = true;
}

/// <summary>一段时间内的情绪压力（受伤后更戒备、被理解后多一分信任）。</summary>
public sealed class PersonalityModifier
{
	[JsonPropertyName("cause_id")]
	public string CauseId { get; set; } = "";

	[JsonPropertyName("cause_kind")]
	public string CauseKind { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("private")]
	public bool Private { get; set; } = true;

	[JsonPropertyName("since_day")]
	public int SinceDay { get; set; }

	[JsonPropertyName("strength")]
	public float Strength { get; set; }

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";

	[JsonPropertyName("until_day")]
	public int? UntilDay { get; set; }
}

/// <summary>私下的感情取向探索（只做连续性，绝不公开）。</summary>
public sealed class NpcIdentityProfile
{
	[JsonPropertyName("confided_in")]
	public HashSet<string> ConfidedIn { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("drawn_to_non_binary")]
	public float DrawnToNonBinary { get; set; }

	[JsonPropertyName("drawn_to_other")]
	public float DrawnToOther { get; set; }

	[JsonPropertyName("drawn_to_same")]
	public float DrawnToSame { get; set; }

	[JsonPropertyName("label_certainty")]
	public float LabelCertainty { get; set; }

	[JsonPropertyName("openly_out")]
	public bool OpenlyOut { get; set; }

	[JsonPropertyName("quiet_moments")]
	public List<string> QuietMoments { get; set; } = new List<string>();

	[JsonPropertyName("romantic_label")]
	public string RomanticLabel { get; set; } = "unknown";

	[JsonPropertyName("romantic_signals")]
	public int RomanticSignals { get; set; }

	[JsonPropertyName("sexual_label")]
	public string SexualLabel { get; set; } = "unknown";

	/// <summary>unexplored / questioning / private_acceptance。</summary>
	[JsonPropertyName("stage")]
	public string Stage { get; set; } = "unexplored";
}

/// <summary>两个人（村民或农夫）之间的关系。A、B 按名字排好（农夫写作 Farmer）。</summary>
public sealed class RelationshipEdgeState
{
	[JsonPropertyName("a")]
	public string A { get; set; } = "";

	[JsonPropertyName("b")]
	public string B { get; set; } = "";

	[JsonPropertyName("changed_on")]
	public int ChangedOn { get; set; }

	[JsonPropertyName("devotion")]
	public float Devotion { get; set; }

	[JsonPropertyName("fondness")]
	public float Fondness { get; set; }

	[JsonPropertyName("grudge")]
	public float Grudge { get; set; }

	[JsonPropertyName("interest")]
	public float Interest { get; set; }

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "acquaintance";

	/// <summary>stable / warming / crush / questioning_attraction / boundary_set / affair_contemplation / strained / married。</summary>
	[JsonPropertyName("phase")]
	public string Phase { get; set; } = "stable";

	[JsonPropertyName("recent_shifts")]
	public List<string> RecentShifts { get; set; } = new List<string>();

	[JsonPropertyName("reserve")]
	public float Reserve { get; set; } = 0.7f;

	[JsonPropertyName("secret")]
	public bool Secret { get; set; }

	[JsonPropertyName("spark")]
	public float Spark { get; set; }

	[JsonPropertyName("strain")]
	public float Strain { get; set; }

	[JsonPropertyName("trust")]
	public float Trust { get; set; }
}

public sealed class PersonalityData
{
	[JsonPropertyName("villagers")]
	public List<NpcPersonalityState> Villagers { get; set; } = new List<NpcPersonalityState>();

	[JsonPropertyName("ties")]
	public List<RelationshipEdgeState> Ties { get; set; } = new List<RelationshipEdgeState>();
}

/// <summary>从一轮对话里读出的性格信号。</summary>
public sealed class InteractionSignal
{
	public bool CrossedLine { get; set; }

	public bool Flirty { get; set; }

	public bool FlirtyPolitely { get; set; }

	public List<string> Labels { get; } = new List<string>();

	public bool MakesGuarded { get; set; }

	public bool MakesSofter { get; set; }

	public string Npc { get; set; } = "";

	public Dictionary<string, float> Nudges { get; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

	public int OnDay { get; set; }

	public bool TemptsCheating { get; set; }

	public string Why { get; set; } = "";
}

/// <summary>村民之间发生的一件事对关系的影响（社交网给出）。</summary>
public sealed record SocialMomentEffect(string Npc1, string Npc2, int TotalDay, float Affinity, float Trust, float Tension, float Playfulness, string Tone, IReadOnlyList<string> Tags, string Archetype);
