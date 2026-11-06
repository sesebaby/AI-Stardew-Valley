using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Prompts;

namespace AISV.Core.Dialogue;

/// <summary>玩家关掉回复框没回话时，对话记录里的玩家回复写成这个标记。</summary>
public static class SkippedReply
{
	public const string Marker = "[Skipped conversation]";

	public static bool Is(string? response) => string.Equals(response?.Trim(), Marker, StringComparison.OrdinalIgnoreCase);

	/// <summary>给 AI 看的说法：没回话不等于冷淡。</summary>
	public static string ForPrompt(string? response) => Is(response) ? "（农夫听完就走了，没有接话——这很正常，不代表冷淡或无礼）" : response ?? "";
}

/// <summary>把对话记录写成给 AI 看的"最近的对话"。</summary>
public static class HistoryFormatter
{
	public static string Format(string npcName, IReadOnlyList<ConversationEntry> history, int window, int maxTurns, GameDate today, string currentPlayer, string contextType = "daily", string contextId = "")
	{
		if (history.Count == 0)
		{
			return "（以前没聊过）";
		}
		List<ConversationEntry> list = history.TakeLast(Math.Max(1, window)).ToList();
		bool SameContext(ConversationEntry e) => IsSameContext(e, contextType, contextId);
		int todaySame = list.Count(e => e.IsOn(today) && SameContext(e));
		bool ongoing = maxTurns > 1 && todaySame > 0 && todaySame < maxTurns;
		int lastOngoing = ongoing ? list.FindLastIndex(e => e.IsOn(today) && SameContext(e)) : -1;
		// 每条压缩条目距最新的位置（正在进行的不算），用来决定只写一行。
		int[] age = new int[list.Count];
		int counter = 0;
		for (int i = list.Count - 1; i >= 0; i--)
		{
			age[i] = ongoing && list[i].IsOn(today) && SameContext(list[i]) ? -1 : counter++;
		}
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < list.Count; i++)
		{
			ConversationEntry e = list[i];
			bool isToday = e.IsOn(today);
			bool same = SameContext(e);
			bool other = IsOtherFarmer(e, currentPlayer);
			if (!(isToday && same && ongoing))
			{
				Compact(sb, npcName, e, isToday, same, age[i] >= 4, other);
				continue;
			}
			sb.AppendLine(other ? $"[今天·和另一位农夫 {e.Farmer} 的单独对话]" : "[今天·这场对话]");
			sb.AppendLine("  你说：" + e.Opener);
			if (!string.IsNullOrEmpty(e.FarmerReply))
			{
				sb.AppendLine(other ? $"  {e.Farmer}（另一位农夫，不是眼前这位）说：" : "  农夫说：");
				sb.AppendLine(PromptText.Wrap("PLAYER_RESPONSE_HISTORY", SkippedReply.ForPrompt(e.FarmerReply), 800));
			}
			if (!string.IsNullOrEmpty(e.Comeback) && i != lastOngoing)
			{
				sb.AppendLine($"  你回应：{e.Comeback}（情绪：{e.Face}）");
			}
		}
		return sb.ToString();
	}

	internal static bool IsSameContext(ConversationEntry e, string contextType, string contextId)
	{
		string a = e.ContextOrDaily;
		string b = string.IsNullOrWhiteSpace(contextType) ? "daily" : contextType;
		if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return !string.Equals(a, "festival", StringComparison.OrdinalIgnoreCase) || string.Equals(e.SettingRef ?? "", contextId ?? "", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsOtherFarmer(ConversationEntry e, string currentPlayer)
	{
		if (string.IsNullOrWhiteSpace(e.Farmer))
		{
			return false;
		}
		return string.IsNullOrWhiteSpace(currentPlayer) ? e.OffSite : !string.Equals(e.Farmer, currentPlayer, StringComparison.OrdinalIgnoreCase);
	}

	private static void Compact(StringBuilder sb, string npcName, ConversationEntry e, bool isToday, bool sameContext, bool oneLine, bool other)
	{
		string when = isToday
			? (sameContext ? "今天早些时候·已经聊完" : $"今天早些时候·{Where(e)}·另一场")
			: $"{SeasonName(e.Season)} {e.Day} 日，第 {e.Year} 年·以前的{Kind(e)}";
		string mood = string.IsNullOrWhiteSpace(e.Face) ? "" : "，情绪=" + e.Face;
		string topics = Topics(npcName, e);
		string otherTag = other ? $" [另一位农夫：{e.Farmer}]" : "";
		string who = other ? e.Farmer : "对方";
		bool textedFirst = string.IsNullOrWhiteSpace(e.FarmerReply) && string.Equals(e.Setting, "messenger", StringComparison.OrdinalIgnoreCase);
		if (oneLine)
		{
			sb.AppendLine(textedFirst
				? $"[{when}]{otherTag} 你先给{who}发了消息：\"{Snippet(e.Comeback, 70)}\"{topics}"
				: $"[{when}]{otherTag} {(other ? e.Farmer : "农夫")}说：\"{Snippet(SkippedReply.ForPrompt(e.FarmerReply), 70)}\"{mood}{topics}");
			return;
		}
		sb.AppendLine($"[{when}]{otherTag}");
		if (textedFirst)
		{
			sb.AppendLine($"  你突然在信使上给{who}发了消息：\"{Snippet(e.Comeback)}\"");
			return;
		}
		if (!string.IsNullOrWhiteSpace(e.Opener))
		{
			sb.AppendLine($"  你对{who}说：\"{Snippet(e.Opener)}\"");
		}
		sb.AppendLine(other
			? $"  {e.Farmer}（另一位农夫，不是眼前这位）说：\"{Snippet(SkippedReply.ForPrompt(e.FarmerReply))}\"{mood}"
			: $"  农夫说：\"{Snippet(SkippedReply.ForPrompt(e.FarmerReply))}\"{mood}");
		if (!string.IsNullOrWhiteSpace(e.Comeback))
		{
			sb.AppendLine($"  你回应：\"{Snippet(e.Comeback)}\"");
		}
		string company = e.GroupCompany();
		if (company.Length > 0 && !string.IsNullOrWhiteSpace(e.CompanySaid))
		{
			sb.AppendLine($"  同在那场对话里的{company}说：\"{Snippet(e.CompanySaid)}\"");
		}
		if (topics.Length > 0)
		{
			sb.AppendLine("  " + topics.TrimStart('，', ' '));
		}
	}

	private static string Where(ConversationEntry e)
	{
		if (string.Equals(e.Setting, "festival", StringComparison.OrdinalIgnoreCase))
		{
			return string.IsNullOrWhiteSpace(e.SettingName) ? "节日上" : e.SettingName;
		}
		if (string.Equals(e.Setting, "messenger", StringComparison.OrdinalIgnoreCase))
		{
			return "信使上的文字聊天（手机或电脑，不是面对面）";
		}
		if (string.Equals(e.Setting, "village-group", StringComparison.OrdinalIgnoreCase))
		{
			return "手机上的鹈鹕镇居民群（很多人的文字群聊，不是面对面）";
		}
		string company = e.GroupCompany();
		return company.Length > 0 ? $"你、农夫和{company}的多人聊天" : "日常聊天";
	}

	private static string Kind(ConversationEntry e)
	{
		if (string.Equals(e.Setting, "messenger", StringComparison.OrdinalIgnoreCase))
		{
			return "信使聊天";
		}
		if (string.Equals(e.Setting, "village-group", StringComparison.OrdinalIgnoreCase))
		{
			return "居民群聊天";
		}
		string company = e.GroupCompany();
		return company.Length > 0 ? $"多人聊天（你、农夫和{company}）" : "聊天";
	}

	/// <summary>这条记录碰到了哪些招牌话题（提醒 AI 别再绕回去）。</summary>
	private static string Topics(string npcName, ConversationEntry e)
	{
		List<string> tags = PersonaPrompt.TopicTags(npcName, e.Opener).Concat(PersonaPrompt.TopicTags(npcName, e.Comeback)).Distinct().ToList();
		return tags.Count == 0 ? "" : "，你上次聊到的老话题=" + string.Join("、", tags);
	}

	internal static string Snippet(string? text, int max = 120)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "（聊天）";
		}
		string clean = PromptText.Sanitize(text, 240).Replace('\n', ' ').Trim();
		return clean.Length <= max ? clean : clean.Substring(0, max) + "…";
	}

	public static string SeasonName(string? season) => (season ?? "").Trim().ToLowerInvariant() switch
	{
		"spring" => "春",
		"summer" => "夏",
		"fall" => "秋",
		"winter" => "冬",
		_ => season ?? ""
	};

	/// <summary>
	/// 带几条历史：用服务器时 = max(记忆条数, min(轮数+2, 记忆条数+2))，记忆条数 = min(设置, 服务器上限默认 40)；
	/// 用自己的网关时 = max(10, 轮数+2)。轮数限 1～50，记忆条数设置限 1～200。
	/// </summary>
	public static int Window(int aiMemoryEntries, int maxTurns, bool usesServer, int serverMemoryCap)
	{
		int turns = Math.Clamp(maxTurns, 1, 50);
		if (!usesServer)
		{
			return Math.Max(10, turns + 2);
		}
		int memory = Math.Max(1, Math.Min(Math.Clamp(aiMemoryEntries, 1, 200), Math.Max(0, serverMemoryCap)));
		return Math.Max(memory, Math.Min(turns + 2, memory + 2));
	}
}
