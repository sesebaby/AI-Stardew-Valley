using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AISV.Core.Config;

namespace AISV.Core.Dialogue;

public enum ProposalKind
{
	Dating,
	Marriage
}

/// <summary>村民对表白/求婚的决定。</summary>
public sealed class ProposalDecision
{
	[JsonPropertyName("feeling")]
	public string Face { get; set; } = "neutral";

	[JsonPropertyName("to_remember")]
	public string Keepsake { get; set; } = "";

	[JsonPropertyName("line_drawn")]
	public string? Limit { get; set; }

	[JsonPropertyName("says")]
	public string Line { get; set; } = "";

	[JsonPropertyName("warmth")]
	public int Points { get; set; }

	[JsonPropertyName("agrees")]
	public bool SaidYes { get; set; }

	[JsonPropertyName("tone")]
	public string Tone { get; set; } = "tender";

	[JsonPropertyName("back_off_days")]
	public int WaitDays { get; set; }

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

public static class ProposalParser
{
	private static readonly string[] Tones = { "tender", "shy", "playful", "surprised", "conflicted", "awkward" };

	private static readonly int[] Cooldowns = { 0, 3, 7, 14, 28 };

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

	/// <summary>没有布尔 agrees 就不算数。</summary>
	public static ProposalDecision? Parse(string raw, int maxChars)
	{
		try
		{
			string json = JsonCleaner.Clean(raw);
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("agrees", out JsonElement accepted) || accepted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			{
				return null;
			}
			ProposalDecision? d = JsonSerializer.Deserialize<ProposalDecision>(json, Json);
			if (d == null)
			{
				return null;
			}
			d.Line = TextTools.Truncate(d.Line, maxChars);
			d.Why = Cut(d.Why, 280);
			d.Keepsake = Cut(d.Keepsake, 280);
			d.Limit = string.IsNullOrWhiteSpace(d.Limit) ? null : Cut(d.Limit, 180);
			d.Points = Math.Clamp(d.Points, 0, 100);
			d.Face = Emotion.Normalize(d.Face);
			d.Tone = Tones.FirstOrDefault(t => string.Equals(t, d.Tone, StringComparison.OrdinalIgnoreCase)) ?? "tender";
			d.WaitDays = d.SaidYes ? 0 : d.WaitDays > 0 ? Cooldowns.OrderBy(c => Math.Abs(c - d.WaitDays)).First() : d.Limit != null ? 28 : 7;
			if (string.IsNullOrWhiteSpace(d.Line))
			{
				d.Line = d.SaidYes ? "我……嗯，我也想。" : "让我再想想。";
			}
			return d;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static string Cut(string? text, int max)
	{
		string t = text?.Trim() ?? "";
		return t.Length <= max ? t : t[..max].TrimEnd();
	}
}

/// <summary>判断能不能表白、求婚要的游戏状态。</summary>
public sealed record ProposalSituation
{
	public int Hearts { get; init; }

	/// <summary>none / dating / engaged / married / divorced。</summary>
	public string Status { get; init; } = "none";

	/// <summary>canonical_single / canonical_married / story_single。</summary>
	public string Narrative { get; init; } = "canonical_single";

	public bool AiAvailable { get; init; } = true;

	public bool IsFarmhand { get; init; }

	public bool EventActive { get; init; }

	public bool CanSocialize { get; init; } = true;

	public bool NpcEnabled { get; init; } = true;

	public bool IsChild { get; init; }

	public bool Datable { get; init; }

	public bool HasPersona { get; init; }

	public bool InCooldown { get; init; }

	/// <summary>农夫已经和别人结婚或订婚。</summary>
	public bool OtherCommitment { get; init; }

	/// <summary>农夫没有配偶、婚约、室友。</summary>
	public bool FreeToMarry { get; init; } = true;

	public int HouseUpgradeLevel { get; init; }
}

public sealed record ProposalOptions(bool Dating, bool Marriage, string BlockReason)
{
	public static readonly ProposalOptions None = new ProposalOptions(false, false, "");

	public bool Any => Dating || Marriage;
}

public static class ProposalRules
{
	public const int DatingHearts = 8;

	public const int MarriageHearts = 10;

	public static ProposalOptions Evaluate(ProposalSituation s, ModConfig config)
	{
		string? block =
			!config.CourtingOn ? "关了对话里的表白求婚" :
			!config.CourtWithoutTokens ? "要用游戏道具才能表白求婚" :
			!s.AiAvailable ? "AI 不可用" :
			config.HostOnlyCourting && s.IsFarmhand ? "联机的农场工不能" :
			s.EventActive ? "正在过场" :
			!s.CanSocialize ? "这位不能社交" :
			!s.NpcEnabled ? "这位没开 AI" :
			!RomanceSafe(s) ? "这位不能谈恋爱" :
			s.InCooldown ? "被拒后还在冷却" :
			s.Narrative is not ("canonical_single" or "story_single" or "separated" or "divorced") || s.Narrative == "story_single" && !config.CourtStorySingles ? "故事里不单身" :
			null;
		if (block != null)
		{
			return new ProposalOptions(false, false, block);
		}
		bool dating = config.AskToDateOn && s.Hearts >= DatingHearts && s.Status is "none" or "divorced" && !s.OtherCommitment;
		bool marriage = config.AskToMarryOn && s.Hearts >= MarriageHearts && s.Status == "dating" && !s.OtherCommitment && s.FreeToMarry
			&& (!config.MarryNeedsHouseUpgrade || s.HouseUpgradeLevel >= 1);
		return new ProposalOptions(dating, marriage, dating || marriage ? "" : "条件不够");
	}

	private static bool RomanceSafe(ProposalSituation s) => !s.IsChild && (s.Datable || s.Narrative == "story_single" || s.HasPersona && s.Narrative != "canonical_married");

	public static int AcceptEmote(ProposalKind kind) => kind == ProposalKind.Marriage ? 20 : 32;

	public static string Milestone(ProposalKind kind, ProposalDecision d) =>
		d.SaidYes ? kind == ProposalKind.Dating ? "dating_started_by_dialogue" : "marriage_accepted_by_dialogue"
		: !string.IsNullOrWhiteSpace(d.Limit) ? "romantic_boundary_set" : "proposal_rejected";
}
