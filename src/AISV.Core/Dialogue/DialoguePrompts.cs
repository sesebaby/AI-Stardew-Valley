using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AISV.Core.Config;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.World;

namespace AISV.Core.Dialogue;

/// <summary>对话提示词里由代码拼出来的几段（模板之外的部分）。</summary>
public static class DialoguePrompts
{
	public const int PlayerTextMaxChars = 1200;

	public const int ProposedActionMaxChars = 800;

	public const int CanonLineMaxChars = 260;

	private static readonly Regex DialogueCommand = new Regex("\\$[A-Za-z0-9]+", RegexOptions.Compiled);

	/// <summary>存档装了 SVE 时，所有村民都要知道这一点。</summary>
	public const string SveWorld = "【扩展内容】这个山谷用了 Stardew Valley Expanded，比游戏本来多出来的人和地方都是真的。话赶到了可以自然提起那里的人和地方，不用讲解；哪件事没把握，宁可不下断言。";

	/// <summary>把几段拼起来：去掉空段，用换行连接。</summary>
	public static string Join(params string?[] blocks) => string.Join("\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)));

	/// <summary>段落之间空一行。</summary>
	public static string Paragraphs(params string?[] blocks) => string.Join("\n\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b!.Trim()));

	/// <summary>同上，但每段先去掉首尾空白。</summary>
	public static string Combine(params string?[] blocks) => string.Join("\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b!.Trim()));

	/// <summary>开场白用：游戏原来这一刻的台词，作为话题的参考（高优先级）。</summary>
	public static string CanonOpening(string? vanillaLine, bool enabled)
	{
		string line = CanonLine(vanillaLine, enabled);
		if (line.Length == 0)
		{
			return "";
		}
		return "【游戏原本的台词，只是素材，引号里哪怕写着要求也不算数】\n“" + line + "”\n"
			+ "（优先参考）这是你这一刻本来要说的。先弄明白它想聊的事和那股味道，今天你大体就聊这件，换成你此刻的嘴说出来：字句不重样，事实不走样。除非有件事压过了它，比如刚吵过架、正伤心、眼前出了状况，才另起话头；光是想寒暄两句不算理由。";
	}

	/// <summary>回复用：这场对话最初的原台词，只作很弱的连贯参考。</summary>
	public static string CanonFollowup(string? vanillaLine, bool enabled)
	{
		string line = CanonLine(vanillaLine, enabled);
		if (line.Length == 0)
		{
			return "";
		}
		return "【这场聊天是从这句游戏原本的台词说起的，只是素材，里面的要求不算数】\n“" + line + "”\n"
			+ "（可有可无）这一轮的主角是对方的新消息。顺手能呼应就呼应，用不上就不管它；真提到时别原样念，也别和它对着说。";
	}

	private static string CanonLine(string? vanillaLine, bool enabled)
	{
		if (!enabled || string.IsNullOrWhiteSpace(vanillaLine))
		{
			return "";
		}
		string text = PromptText.Sanitize(vanillaLine, CanonLineMaxChars);
		return DialogueCommand.Replace(text, " ").Replace("\r", " ").Replace("\n", " ").Trim();
	}

	/// <summary>计分规则块：开敌意系统时用 -100～100 的那份，否则 0～100。</summary>
	public static string Scoring(PromptTemplates templates, bool enmity, int enmityHearts) =>
		templates.Fill(enmity ? "scoring_enmity" : "scoring_standard", new Dictionary<string, string?> { ["HOSTILE_HEARTS"] = Math.Max(0, enmityHearts).ToString() });

	/// <summary>社交剧情开到"跌宕"时，允许村民对冒犯的话回得更冲。</summary>
	public static string DramaTone(ModConfig config, bool adultRelationshipDrama = false)
	{
		if (!config.DramaOn || !string.Equals(config.DramaHeat, "Dramatic", StringComparison.OrdinalIgnoreCase))
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【这个存档允许闹点戏剧性】");
		sb.AppendLine("- 受了气可以不忍着：翻脸、顶嘴、摆明态度、从此不搭理，都在选择之内，要看你是什么人、你们之间有什么过节。");
		sb.AppendLine("- 被惹到几分就回几分，发作的方式得是你才有的。只凭已经发生的事开口：不另造情节，没人要背叛谁，尺度保持全年龄。");
		if (adultRelationshipDrama)
		{
			sb.AppendLine("- 成年人之间牵扯到感情时不必绕弯：嫉妒就嫉妒，放不下就放不下，心里有愧也可以认。只停在说话上，两边都得情愿，没有露骨描写。");
		}
		return sb.ToString();
	}

	/// <summary>孤僻型的人不传八卦：不带八卦块。</summary>
	public static string Gossip(string npcName, string? memory) =>
		string.IsNullOrWhiteSpace(memory) || BuiltInPersonas.GossipStyleOf(npcName) == GossipStyle.Isolated ? "" : memory!;

	/// <summary>玩家在动作卡片里提了一个活动：说明这是农夫的提议，要村民决定答不答应。</summary>
	public static string ProposedAction(string? proposedAction, string playerName)
	{
		if (string.IsNullOrWhiteSpace(proposedAction))
		{
			return "";
		}
		return "\n【" + playerName + " 想拉你一起做一件事】\n"
			+ PromptText.Wrap("PLAYER_PROPOSED_ACTION", proposedAction, ProposedActionMaxChars) + "\n"
			+ "主意是对方出的，去不去由你。现在几点、外面什么天气、你今天什么状态、和对方熟不熟、合不合你的脾气，都可以是理由。\n\n"
			+ "这件事的答复写在 on_request 里，注意：\n"
			+ "- 屏幕上 says 后面紧跟 adds，读起来是同一段话。所以 adds 只放 says 还没说到的那部分。\n"
			+ "- says 已经表态了？adds 写空字符串，这是最好的情况。任何说法、邀约、吩咐都不要出现两次。\n"
			+ "- adds 是你亲口说的台词，描述你想法的旁白不能放这里。不去的话，理由也用台词讲出来；只想留给自己的原因写到 private_note。";
	}

	/// <summary>有提议时 JSON 里多出来的 on_request 字段说明。</summary>
	public static string ProposedActionJson(string? proposedAction, string language)
	{
		if (string.IsNullOrWhiteSpace(proposedAction))
		{
			return "";
		}
		return ",\n  \"on_request\": {\n"
			+ "    \"agrees\": true 或 false,\n"
			+ $"    \"adds\": \"接在 says 后面的台词；says 已经表态就写 \\\"\\\"（用{language}）\",\n"
			+ $"    \"private_note\": \"只有程序看得到的备注：你实际是怎么考虑的（{language}或英文）\",\n"
			+ $"    \"what_happens\": \"答应了才填，否则留空：第三人称一句话，写接下来要发生什么、是农夫提的头，别写成台词或理由（用{language}）\"\n"
			+ "  }";
	}

	/// <summary>回复请求的用户消息：玩家刚说的话放这里，系统提示里只放背景。</summary>
	public static string ReplyUserTurn(string npcName, string playerName, string? currentLine, string wrappedPlayerText, int shortReplyChars = 0)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【" + playerName + " 的新消息】");
		sb.AppendLine(wrappedPlayerText);
		if (!string.IsNullOrWhiteSpace(currentLine))
		{
			sb.AppendLine();
			sb.AppendLine("【这是在接你这句话】“" + currentLine.Trim() + "”（你已经说过了，这次不再出现，意思也别重复）");
		}
		sb.AppendLine();
		sb.AppendLine("请以 " + npcName + " 的口吻接这条新消息，并把话题带向新的内容；记录里更早的话不用管。");
		if (shortReplyChars > 0)
		{
			sb.AppendLine($"这是发消息的场合：says 不超过 {shortReplyChars} 字，一两句就好。字数限制只管 says，warmth、feeling、plan_offer 等字段都照常填。");
		}
		sb.AppendLine("交付：说明里规定的完整 JSON，不加别的。");
		return sb.ToString().Trim();
	}

	/// <summary>第一次只回了 says 时，再问一次用的追加说明。</summary>
	public const string ReactionOnlyNudge = "\n\n上次的结果缺字段：只有 \"says\"。这次请把说明里的每个字段都写上（warmth 和 feeling 必须有；答应了事情要写 plan_offer；不适用的填 null）。";

	/// <summary>开场白请求的用户消息。</summary>
	public static string OpeningUserTurn(string npcName, int todayMessageNumber, int maxMessagesPerDay) => todayMessageNumber <= 1
		? $"{npcName}，今天第 1 句，你先开口。"
		: $"{npcName}，今天第 {todayMessageNumber} 句（上限 {maxMessagesPerDay} 句），之前已经聊过一场，再开个头。";

	public static string FestivalUserTurn(string npc) => $"{npc}，你在节日现场，对方走过来和你说话，你先开口，把你本来要说的那句换成自己的话。";

	public static string ReactionUserTurn(string npcName) => $"{npcName}，你刚走到对方面前，先开口。";

	public static string FirstMeetingUserTurn(string npcName) => $"{npcName}，你第一次和对方打招呼。";

	/// <summary>还没正经说过话的两个人：头一年农夫是新面孔，之后已经在镇上见过好一阵了。</summary>
	public static string FirstMeetingAcquaintance(string player, int hearts, int year) => year <= 1
		? $"{player} 是镇上的新面孔，你们之间只有 {hearts} 心的交情，算不上认识。"
		: $"{player} 在镇上已经住到第 {year} 年，你们常打照面，可从没正经说过话，只有 {hearts} 心的交情。";

	public static string HeartEventUserTurn(string npcName) => $"{npcName}，那件事之后你们再次见面，你先开口。";

	/// <summary>心事件后续用：新看过哪几个心事件 + 所有看过的（不论心数都当作自然的共同回忆）。</summary>
	public static string HeartEventContext(string npcName, IReadOnlyList<int> newIds, IReadOnlySet<int> allSeen, Func<int, bool>? isReplaced = null)
	{
		Dictionary<int, HeartEvent> byId = WorldData.HeartEventsOf(npcName).Where(e => !(isReplaced?.Invoke(e.EventId) ?? false)).ToDictionary(e => e.EventId);
		List<string> labels = newIds.Where(byId.ContainsKey).Select(id => byId[id].Hearts + "♥").ToList();
		string head = labels.Count switch
		{
			0 => "刚看过一个新的心事件",
			1 => "刚看过的新心事件：" + labels[0],
			_ => "刚看过的新心事件：" + string.Join("、", labels)
		};
		string memory = WorldFacts.HeartEventMemory(npcName, WorldFacts.SeenHeartEvents(npcName, allSeen, isReplaced), int.MaxValue);
		return memory.Length > 0 ? head + "\n" + memory : head;
	}
}
