using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Awareness;

/// <summary>走过来说话时提示词里的三块：发生了什么、怎么知道的、心里什么感受。</summary>
public sealed record WitnessReaction(string Type, string Observation, string Perception, string Feeling, string Title, IReadOnlyList<string> Guide);

public static class AwarenessPrompts
{
	public const int MaxBlockChars = 1800;

	private const string Header = "【这些事你亲眼见过】";

	private const string Hedges = "每行的说法就是你的把握：写“看见”“看到”的你很肯定，写“听说”的是别人告诉你的，带“好像”“说不准”“不太确定”的，说起来就留点余地。这几行以外的细节一概别编。";

	private const string Closing = "上面每一件都确有其事。有人问起，就照你的脾气如实说——生气、支吾都行，就是不能矢口否认；眼下聊的不是这些，就别自己扯过来。";

	/// <summary>日常对话里的"你亲眼见过的事"：引子（心事）在前，然后按相关度放记忆行，一模一样的行只写一次，条数和总长有上限。</summary>
	public static string MemoryBlock(IReadOnlyList<AwarenessMemoryItem> memories, SightingWriter writer, int nowDay, int nowTime, string playerName, string lead = "", IReadOnlyCollection<string>? leadEventIds = null, int maxChars = MaxBlockChars, int maxLines = AwarenessMemory.MaxContextMemories)
	{
		AwarenessMemoryItem[] items = memories
			.Where(m => leadEventIds == null || !leadEventIds.Contains(m.Event.EventId))
			.OrderByDescending(m => m.Relevance)
			.ToArray();
		if (items.Length == 0 && string.IsNullOrWhiteSpace(lead))
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(lead))
		{
			sb.Append(lead.Trim()).Append("\n\n");
		}
		if (items.Length == 0)
		{
			return sb.ToString().TrimEnd();
		}
		sb.Append(Header).Append('\n');
		if (!string.IsNullOrWhiteSpace(playerName))
		{
			sb.Append("此刻和你面对面的农夫，就是这几行里的 ").Append(playerName).Append("。");
		}
		sb.Append(Hedges).Append('\n');
		int budget = maxChars - sb.Length - Closing.Length - 1;
		HashSet<string> written = new HashSet<string>();
		foreach (AwarenessMemoryItem m in items)
		{
			string line = writer.MemoryLine(m.Event, m.Witness, nowDay, nowTime);
			if (written.Count < maxLines && line.Length + 1 <= budget && written.Add(line))
			{
				sb.Append(line).Append('\n');
				budget -= line.Length + 1;
			}
		}
		sb.Append(Closing);
		return sb.ToString();
	}

	/// <summary>发生了什么：句子（或摘要，加上收礼人的口味），后面接反应指令。</summary>
	public static string Observation(Sighting e, string? sentence = null)
	{
		string text = sentence ?? e.Detail("summary", e.Act).Trim();
		if (sentence == null)
		{
			string subject = e.Detail("subject");
			string taste = e.Detail("taste");
			if (subject.Length > 0 && TasteWord(taste) is string word)
			{
				text += $"{subject}{word}这份礼物。";
			}
		}
		string directive = e.Detail("reactionDirective");
		return directive.Length > 0 ? text.Trim() + "\n反应要求：" + directive.Trim() : text.Trim();
	}

	/// <summary>怎么知道的：牵涉到自己 / 只瞥见 / 隔墙听见 / 看清楚 / 就在眼前。</summary>
	public static string Perception(Witness w, bool heardThroughWalls)
	{
		int tiles = Math.Max(1, (int)Math.Round(w.TilesAway));
		if (w.FirstHand)
		{
			return "这事落在你自己身上，你当时就在场，从头到尾看得一清二楚。";
		}
		if (!w.ClearView)
		{
			return $"隔着大约 {tiles} 格，你只匆匆扫到一眼，细节说不准。";
		}
		if (heardThroughWalls && w.TilesAway > 8)
		{
			return $"大约 {tiles} 格开外，你压根没看见，只是听见动静，估摸出是谁弄的。";
		}
		return w.TilesAway <= 3 ? "就在你跟前，几步远的地方。" : $"大约 {tiles} 格远，可你看得明明白白。";
	}

	/// <summary>拼出走过来时的反应材料；标题和反应说明里的 {farmer}、{season} 换成实际的值。</summary>
	public static WitnessReaction Reaction(SightingCatalog catalog, Sighting e, Witness w, string farmer, string season, string? sentence = null, string feeling = "")
	{
		ReactionGuide guide = catalog.Reaction(e.Type);
		Func<string, string?> resolve = t => t switch { "farmer" => farmer, "season" => season, _ => null };
		return new WitnessReaction(e.Type, Observation(e, sentence), Perception(w, catalog.Get(e.Type).ThroughWalls),
			feeling, PhraseTemplate.Parse(guide.Title).Render(resolve), guide.Guide.Select(g => (g.StartsWith(" ", StringComparison.Ordinal) ? "  " : "") + PhraseTemplate.Parse(g).Render(resolve)).ToList());
	}

	/// <summary>手上要递东西时追加的一句。</summary>
	public static string HandOverLine(int count, string itemName) =>
		$"\n话说出口时，你顺手把{(count == 1 ? "一个" : count + " 个")}{itemName}交到对方手里，说的时候自然带上一句。";

	private static string? TasteWord(string taste) => taste switch
	{
		"loved" => "很喜欢",
		"liked" => "挺喜欢",
		"disliked" => "不太喜欢",
		"hated" => "很讨厌",
		_ => null
	};
}
