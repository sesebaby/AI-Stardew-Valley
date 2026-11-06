using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AISV.Core.Outings;

/// <summary>AI 对一次邀约的答复。</summary>
public sealed class OutingDecision
{
	[JsonPropertyName("agrees")]
	public bool Agrees { get; set; }

	[JsonPropertyName("back_off_days")]
	public int BackOffDays { get; set; }

	[JsonPropertyName("beat_1")]
	public string Beat1 { get; set; } = "";

	[JsonPropertyName("beat_2")]
	public string Beat2 { get; set; } = "";

	[JsonPropertyName("beat_3")]
	public string Beat3 { get; set; } = "";

	[JsonPropertyName("beat_4")]
	public string Beat4 { get; set; } = "";

	[JsonPropertyName("beat_5")]
	public string Beat5 { get; set; } = "";

	[JsonPropertyName("beat_6")]
	public string Beat6 { get; set; } = "";

	[JsonPropertyName("feeling")]
	public string Feeling { get; set; } = "neutral";

	[JsonPropertyName("instead")]
	public OutingCounter? Instead { get; set; }

	[JsonPropertyName("line_drawn")]
	public string? LineDrawn { get; set; }

	[JsonPropertyName("outing_id")]
	public string OutingId { get; set; } = "";

	[JsonPropertyName("says")]
	public string Says { get; set; } = "";

	[JsonPropertyName("tone")]
	public string Tone { get; set; } = "";

	[JsonPropertyName("to_remember")]
	public string ToRemember { get; set; } = "";

	[JsonPropertyName("warmth")]
	public int Warmth { get; set; }

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";

	[JsonPropertyName("worth_telling")]
	public bool WorthTelling { get; set; }

	public string[] Lines => new[] { Beat1, Beat2, Beat3, Beat4, Beat5, Beat6 };

	public void ClearScene()
	{
		Tone = "";
		OutingId = "";
		Beat1 = Beat2 = Beat3 = Beat4 = Beat5 = Beat6 = "";
	}
}

/// <summary>村民愿意出去、但玩家点的地方或时间没有场景时，改提的去处。</summary>
public sealed class OutingCounter
{
	[JsonPropertyName("ask_text")]
	public string AskText { get; set; } = "";

	[JsonPropertyName("beat_1")]
	public string Beat1 { get; set; } = "";

	[JsonPropertyName("beat_2")]
	public string Beat2 { get; set; } = "";

	[JsonPropertyName("beat_3")]
	public string Beat3 { get; set; } = "";

	[JsonPropertyName("beat_4")]
	public string Beat4 { get; set; } = "";

	[JsonPropertyName("beat_5")]
	public string Beat5 { get; set; } = "";

	[JsonPropertyName("beat_6")]
	public string Beat6 { get; set; } = "";

	[JsonPropertyName("if_no")]
	public string IfNo { get; set; } = "";

	[JsonPropertyName("if_yes")]
	public string IfYes { get; set; } = "";

	[JsonPropertyName("no_label")]
	public string NoLabel { get; set; } = "";

	[JsonPropertyName("outing_id")]
	public string OutingId { get; set; } = "";

	[JsonPropertyName("place")]
	public string Place { get; set; } = "";

	[JsonPropertyName("tone")]
	public string Tone { get; set; } = "";

	[JsonPropertyName("yes_label")]
	public string YesLabel { get; set; } = "";
}

public static class OutingState
{
	public const string Declined = "declined";

	public const string Pending = "accepted_pending_scene";

	public const string Completed = "completed";

	public const string Expired = "expired";

	public const string Cancelled = "cancelled";
}

/// <summary>一次邀约的记录（plans/outings.json 里的一项）。</summary>
public sealed class OutingRecord
{
	public bool Agreed { get; set; }

	public string Asked { get; set; } = "";

	public int AskedOn { get; set; }

	public string Feeling { get; set; } = "neutral";

	public int FromTime { get; set; }

	public string Id { get; set; } = "";

	public string? LineDrawn { get; set; }

	public bool MatchedWish { get; set; }

	public string Npc { get; set; } = "";

	public string OutingId { get; set; } = "";

	public string Place { get; set; } = "";

	public bool Played { get; set; }

	public int PlayedOn { get; set; }

	public int RestUntil { get; set; }

	public string Said { get; set; } = "";

	public string Script { get; set; } = "";

	public string State { get; set; } = OutingState.Declined;

	public string Tone { get; set; } = "";

	public string ToRemember { get; set; } = "";

	public int ToTime { get; set; }

	public int? WantedFrom { get; set; }

	public string WantedPlace { get; set; } = "";

	public int? WantedTo { get; set; }

	public int Warmth { get; set; }

	public string Why { get; set; } = "";

	public bool WorthTelling { get; set; }

	/// <summary>答应了、场景还没播、脚本也有。</summary>
	[JsonIgnore]
	public bool Waiting => string.Equals(State, OutingState.Pending, StringComparison.OrdinalIgnoreCase) && !Played && !string.IsNullOrWhiteSpace(Script);

	public OutingRecord Copy() => (OutingRecord)MemberwiseClone();
}

/// <summary>邀约的类型：普通的约出去、玩家约配偶、配偶主动约玩家。</summary>
public enum OutingKind
{
	Invite,
	SpouseDate,
	SpouseOffer
}

/// <summary>玩家的话里点到的地点和时间。</summary>
public sealed class OutingIntent
{
	public const string None = "none";

	public const string Supported = "supported";

	public const string Unsupported = "unsupported";

	public const string Unresolved = "unresolved_from_player_text";

	public string RequestedLocation { get; set; } = "";

	public string RawLocation { get; set; } = "";

	public string LocationStatus { get; set; } = None;

	public int? TimeMin { get; set; }

	public int? TimeMax { get; set; }

	public bool HasLocation => !string.IsNullOrWhiteSpace(RequestedLocation) && LocationStatus != None;

	public bool HasSupportedLocation => HasLocation && LocationStatus == Supported;

	public bool HasTime => TimeMin.HasValue && TimeMax.HasValue;

	public bool Unresolvable => LocationStatus == Unresolved;

	public bool HasConstraints => HasLocation || HasTime || Unresolvable;

	public static OutingIntent At(string location) => new OutingIntent { RequestedLocation = location, RawLocation = location, LocationStatus = Supported };
}

/// <summary>一次邀约判定要的东西：类型、可选出游、之前的邀约、关系状态。</summary>
public sealed record OutingContext(OutingKind Kind, string Options, string State, string Status, string Narrative, string Vanilla, bool Hot);

/// <summary>配偶主动邀约：挑好的出游和判定要的东西。</summary>
public sealed record OutingOffer(OutingPlan Plan, OutingContext Context);

/// <summary>对话引擎要的邀约功能（游戏层实现）。</summary>
public interface IOutingDesk
{
	/// <summary>开场白之前：配偶这次要不要主动约（按概率），要就给出挑好的出游。</summary>
	OutingOffer? SpouseOffer(AISV.Core.Dialogue.DialogueMaterial m);
}

/// <summary>邀约这一轮之后要做的：心情变化、能不能传、算不算完成托付、记不记聊天里程碑。</summary>
public readonly record struct OutingTurn(int Mood, bool Spread, bool SettlesFavor, bool CountsMilestones);
