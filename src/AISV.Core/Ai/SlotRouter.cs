using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;

namespace AISV.Core.Ai;

public enum AiSlot
{
	/// <summary>玩家正在等结果的请求（对话、回复、邀约……）。</summary>
	Dialogue,

	/// <summary>后台任务（八卦、记忆整理、信件、周报、故事弧……）。</summary>
	Background
}

/// <summary>
/// 按请求名把 AI 请求分到两个槽，给出每个槽的模型（含备用）、思考档位和超时。
/// 默认归对话槽：只有明确列出的请求走后台（不认识的请求更可能是玩家在等）。
/// </summary>
public static class SlotRouter
{
	public const string ServerDialogueAlias = "aisv-dialogue";

	public const string ServerBackgroundAlias = "aisv-background";

	public static readonly string[] ReasoningEfforts = { "auto", "off", "low", "medium", "high", "max" };

	private static readonly HashSet<string> BackgroundRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"gossip_analysis", "gossip_compaction", "emotional_profile", "letter_generation", "story_arc_detection", "arc_conclusion",
		"npc_social_moment", "npc_interaction", "weekly_news", "messenger_villager_text", "gift_naming", "market_reason", "quest_text", "spouse_story"
	};

	// 超过每天后台上限也照样发：少了它们记忆和周报会断档。
	private static readonly HashSet<string> EssentialRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"gossip_analysis", "weekly_news", "letter_generation", "arc_conclusion", "spouse_story"
	};

	public static AiSlot Classify(string? requestName) => requestName != null && BackgroundRequests.Contains(requestName) ? AiSlot.Background : AiSlot.Dialogue;

	public static bool IsEssential(string? requestName) => requestName != null && EssentialRequests.Contains(requestName);

	/// <summary>用服务器时只发别名（服务器自己路由和换备用）；用自己的网关时主模型在前、备用在后，去重，最多 3 个。</summary>
	public static IReadOnlyList<string> Models(ModConfig config, AiSlot slot, bool useServer)
	{
		if (useServer)
		{
			return new[] { slot == AiSlot.Background ? ServerBackgroundAlias : ServerDialogueAlias };
		}
		string primary = slot == AiSlot.Background && !string.IsNullOrWhiteSpace(config.NightlyModel) ? config.NightlyModel : config.CustomLlmModel;
		string fallbacks = slot == AiSlot.Background ? config.NightlyBackupModels : config.ChatBackupModels;
		return new[] { primary ?? "" }.Concat(SplitList(fallbacks))
			.Select(m => m.Trim())
			.Where(m => m.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.ToList();
	}

	/// <summary>null = 按模型名猜；"" = 不传 reasoning_effort；其他原样传。</summary>
	public static string? ReasoningEffort(ModConfig config, AiSlot slot)
	{
		return NormalizeEffort(slot == AiSlot.Background ? config.NightlyThinking : config.ChatThinking) switch
		{
			"auto" => null,
			"off" => "",
			string v => v
		};
	}

	/// <summary>单次尝试的超时：没填用 30（对话）/ 90（后台），限制在 5～300 秒。</summary>
	public static int TimeoutSeconds(ModConfig config, AiSlot slot)
	{
		int value = slot == AiSlot.Background ? config.NightlyWaitSeconds : config.ChatWaitSeconds;
		return Math.Clamp(value <= 0 ? (slot == AiSlot.Background ? 90 : 30) : value, 5, 300);
	}

	/// <summary>一次请求（含换备用模型）的总时长：对话槽要赶在界面看门狗之前结束。</summary>
	public static int TotalBudgetSeconds(ModConfig config, AiSlot slot)
	{
		int timeout = TimeoutSeconds(config, slot);
		return slot == AiSlot.Background ? timeout * 2 : timeout + 10;
	}

	/// <summary>界面等对话结果的看门狗：对话槽总时长再多 5 秒。</summary>
	public static int DialogueWatchdogSeconds(ModConfig config) => TotalBudgetSeconds(config, AiSlot.Dialogue) + 5;

	/// <summary>这种失败换个模型可能就好了。账号、key、余额、请求本身的问题换模型也没用。</summary>
	public static bool ShouldTryFallback(FailureKind failure) => failure is FailureKind.ModelNotFound or FailureKind.ModelRetired or FailureKind.ContentFiltered
		or FailureKind.RateLimit or FailureKind.HighDemand or FailureKind.ServerError or FailureKind.ServiceUnavailable
		or FailureKind.Timeout or FailureKind.Network or FailureKind.Other or FailureKind.OutputTruncated;

	public static string NormalizeEffort(string? value)
	{
		string v = (value ?? "").Trim().ToLowerInvariant();
		if (v == "none")
		{
			v = "off";
		}
		return ReasoningEfforts.Contains(v) ? v : "low";
	}

	public static IEnumerable<string> SplitList(string? csv) => (csv ?? "").Split(new[] { ',', '，', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
