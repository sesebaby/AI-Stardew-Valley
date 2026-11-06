using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AISV.Core.Social.Web;

/// <summary>两位村民之间的关系在社交网里的样子（bonds/village_web.json）。</summary>
public sealed class NpcSocialEdge
{
	public string First { get; set; } = "";

	public string Second { get; set; } = "";

	/// <summary>unknown / acquaintance / friendly / close / odd_friendship / awkward / tense / rivalry。</summary>
	public string Standing { get; set; } = "unknown";

	public float Closeness { get; set; }

	public float Liking { get; set; }

	public float Trust { get; set; }

	public float Friction { get; set; }

	public float Banter { get; set; }

	public List<string> Tags { get; set; } = new List<string>();

	public int Encounters { get; set; }

	public int LastMetDay { get; set; }

	public int RestUntilDay { get; set; }

	public string HowItStarted { get; set; } = "";

	public List<NpcSocialMemory> Shared { get; set; } = new List<NpcSocialMemory>();
}

public sealed class NpcSocialMemory
{
	public int FadesAfterDay { get; set; }

	public string Id { get; set; } = "";

	public int OnDay { get; set; }

	public string Place { get; set; } = "";

	public List<string> Tags { get; set; } = new List<string>();

	public string Text { get; set; } = "";

	public string Tone { get; set; } = "";

	public int Weight { get; set; }
}

/// <summary>关系变化量（每项 -0.2～0.2）。</summary>
public sealed class NpcSocialDelta
{
	[JsonPropertyName("closer")]
	public float Closer { get; set; }

	[JsonPropertyName("liking")]
	public float Liking { get; set; }

	[JsonPropertyName("trust")]
	public float Trust { get; set; }

	[JsonPropertyName("friction")]
	public float Friction { get; set; }

	[JsonPropertyName("banter")]
	public float Banter { get; set; }

	[JsonIgnore]
	public bool IsEmpty => System.Math.Abs(Closer) < 0.001f && System.Math.Abs(Liking) < 0.001f && System.Math.Abs(Trust) < 0.001f && System.Math.Abs(Friction) < 0.001f && System.Math.Abs(Banter) < 0.001f;
}

/// <summary>村民之间发生的一件小事。</summary>
public sealed class NpcSocialMoment
{
	public string Id { get; set; } = "";

	public int OnDay { get; set; }

	public int Day { get; set; }

	public string Season { get; set; } = "";

	public int Year { get; set; }

	[JsonPropertyName("first")]
	public string First { get; set; } = "";

	[JsonPropertyName("second")]
	public string Second { get; set; } = "";

	[JsonPropertyName("place")]
	public string Place { get; set; } = "";

	[JsonPropertyName("theme")]
	public string Theme { get; set; } = "";

	public string Backdrop { get; set; } = "";

	/// <summary>mundane / sweet / funny / awkward / tense / strange。</summary>
	[JsonPropertyName("vibe")]
	public string Vibe { get; set; } = "";

	[JsonPropertyName("what_happened")]
	public string WhatHappened { get; set; } = "";

	[JsonPropertyName("line_a")]
	public string? LineA { get; set; }

	[JsonPropertyName("line_b")]
	public string? LineB { get; set; }

	[JsonPropertyName("mood_a")]
	public int MoodA { get; set; }

	[JsonPropertyName("mood_b")]
	public int MoodB { get; set; }

	[JsonPropertyName("bond_shift")]
	public NpcSocialDelta BondShift { get; set; } = new NpcSocialDelta();

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("worth_telling")]
	public bool WorthTelling { get; set; }

	[JsonPropertyName("worth_remembering")]
	public bool WorthRemembering { get; set; }
}

/// <summary>今天打算让哪两位村民碰上。</summary>
public sealed class NpcSocialCandidate
{
	public bool AlreadyLinked { get; set; }

	/// <summary>给 AI 的关系说明（游戏里的亲友关系 + 社交网里现在的状态）。</summary>
	public string Backdrop { get; set; } = "";

	public string First { get; set; } = "";

	public bool FirstHasPersona { get; set; }

	public bool FromLore { get; set; }

	public bool HasChild { get; set; }

	public bool NoAi { get; set; }

	public string Place { get; set; } = "";

	public bool Rare { get; set; }

	public List<string> RecentThemes { get; set; } = new List<string>();

	public bool SamePlace { get; set; }

	public int Score { get; set; }

	public string Second { get; set; } = "";

	public bool SecondHasPersona { get; set; }

	public string Theme { get; set; } = "mundane";

	public string WhyThem { get; set; } = "";
}

public sealed class NpcSocialWebState
{
	public List<NpcSocialEdge> Links { get; set; } = new List<NpcSocialEdge>();

	public List<NpcSocialMoment> Moments { get; set; } = new List<NpcSocialMoment>();

	public List<NpcSocialCandidate> Plan { get; set; } = new List<NpcSocialCandidate>();

	public int PlanFor { get; set; }
}
