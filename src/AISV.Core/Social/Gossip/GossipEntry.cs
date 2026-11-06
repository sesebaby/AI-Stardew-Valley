using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AISV.Core.Social.Gossip;

/// <summary>一条八卦（town/rumors_known.json）。</summary>
public sealed class GossipEntry
{
	[JsonPropertyName("born_on")]
	public int BornOn { get; set; }

	[JsonPropertyName("day")]
	public int Day { get; set; }

	/// <summary>给村民记住的事实（进提示词）。</summary>
	[JsonPropertyName("fact")]
	public string Fact { get; set; } = "";

	[JsonPropertyName("fades_on")]
	public int FadesOn { get; set; }

	[JsonPropertyName("from_mods")]
	public List<string> FromMods { get; set; } = new List<string>();

	/// <summary>节日八卦还能再传几轮（-1 = 不限）。</summary>
	[JsonPropertyName("hops_left")]
	public int HopsLeft { get; set; } = -1;

	/// <summary>八卦涉及的村民（内部名）：有村民被关掉 AI 时，带着他的八卦也不再给 AI。</summary>
	[JsonPropertyName("involved")]
	public List<string> Involved { get; set; } = new List<string>();

	[JsonPropertyName("key")]
	public string Key { get; set; } = "";

	/// <summary>festival / scene_event / invite_out 等；普通八卦为空。</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	[JsonPropertyName("kind_ref")]
	public string KindRef { get; set; } = "";

	/// <summary>知道这件事的村民（内部名，按字母排序）。</summary>
	[JsonPropertyName("known_to")]
	public List<string> KnownTo { get; set; } = new List<string>();

	[JsonPropertyName("last_spread_on")]
	public int LastSpreadOn { get; set; }

	[JsonPropertyName("leaves_prompt_on")]
	public int LeavesPromptOn { get; set; }

	/// <summary>给玩家看的那句（聊天框、八卦动态）。</summary>
	[JsonPropertyName("rumor")]
	public string Rumor { get; set; } = "";

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("source_kind")]
	public string SourceKind { get; set; } = "";

	[JsonPropertyName("source_ref")]
	public string SourceRef { get; set; } = "";

	[JsonPropertyName("started_by")]
	public string StartedBy { get; set; } = "";

	[JsonPropertyName("told_by")]
	public string ToldBy { get; set; } = "";

	[JsonPropertyName("topic")]
	public string Topic { get; set; } = "";

	/// <summary>说不清来自哪些村民：不给 AI。</summary>
	[JsonPropertyName("unsourced")]
	public bool Unsourced { get; set; }

	[JsonPropertyName("year")]
	public int Year { get; set; }

	[JsonIgnore]
	public int TotalDay => Dates.TotalDays(Day, Season, Year);

	[JsonIgnore]
	public bool IsFestival => string.Equals(Kind, "festival", StringComparison.OrdinalIgnoreCase) || string.Equals(SourceKind, "festival", StringComparison.OrdinalIgnoreCase);

	public bool HasKey(string key) => string.Equals(Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(Topic, key, StringComparison.OrdinalIgnoreCase);

	/// <summary>过了提示词的有效期（没设有效期的不过期）。</summary>
	public bool ExpiredForPrompt(int today)
	{
		int until = LeavesPromptOn > 0 ? LeavesPromptOn : FadesOn;
		return until > 0 && today > until;
	}
}

/// <summary>今天农夫和某位村民的一轮对话（晚上整理八卦和日记用；不存盘）。</summary>
public sealed class DailyInteraction
{
	public string Comeback { get; set; } = "";

	public string Face { get; set; } = "neutral";

	public string Farmer { get; set; } = "";

	public string Npc { get; set; } = "";

	public string Opener { get; set; } = "";

	public string Reply { get; set; } = "";
}

/// <summary>今天新看过的一个心事件（晚上可能变成八卦）。</summary>
public sealed record HeartEventCandidate(string NpcName, int Hearts, int EventId, [property: JsonPropertyName("GossipCandidateText")] string Story, string PlayerName);

/// <summary>其他功能（场景、看到听到、其他 mod）交来的八卦素材，和来源 mod。</summary>
public sealed record ExternalNotes(IReadOnlyList<string> Notes, IReadOnlyList<string> SourceModIds)
{
	public static readonly ExternalNotes None = new ExternalNotes(Array.Empty<string>(), Array.Empty<string>());
}
