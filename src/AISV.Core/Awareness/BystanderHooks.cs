using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Awareness;

/// <summary>
/// 旁观者的开场引子：一件事只有一个人走过来，其余亲眼看见、分量够的人，当天两小时内头一回和农夫说话时就从这件事说起，只说一次。
/// </summary>
public sealed class BystanderHooks
{
	public const int FreshMinutes = 120;

	public const double MinRelevance = 0.25;

	private readonly HashSet<string> _raised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>这件事这个人已经当面说过（走过来了、或已经当引子用过）。</summary>
	public void MarkRaised(string npc, string eventId) => _raised.Add(npc + "|" + eventId);

	public bool WasRaised(string npc, string eventId) => _raised.Contains(npc + "|" + eventId);

	public void Clear() => _raised.Clear();

	/// <param name="smallTalk">鸡毛蒜皮、不值得当开场说的事。</param>
	public AwarenessMemoryItem? Pick(string npc, IEnumerable<AwarenessMemoryItem> memories, string farmer, int day, int time, Func<Sighting, bool> smallTalk)
	{
		int now = SightingText.ToMinutes(time);
		AwarenessMemoryItem? pick = memories
			.Where(m => m.Event.OnDay == day && now - SightingText.ToMinutes(m.Event.Clock) is >= 0 and <= FreshMinutes)
			.Where(m => string.Equals(m.Event.Doer, farmer, StringComparison.OrdinalIgnoreCase) && m.Relevance >= MinRelevance)
			.Where(m => !smallTalk(m.Event) && !WasRaised(npc, m.Event.EventId))
			.OrderByDescending(m => m.Relevance)
			.ThenByDescending(m => m.Event.Clock)
			.FirstOrDefault();
		if (pick != null)
		{
			MarkRaised(npc, pick.Event.EventId);
		}
		return pick;
	}

	public static string LeadBlock(string memoryLine) =>
		"【刚才亲眼看见的事】\n" + memoryLine.Trim() + "\n"
		+ "这事过去没多久，这之后你还是头一回跟农夫当面说上话。开口就顺着它说一句——夸、打趣、挑刺、装作不在意都行，照你的脾气和你眼下对他的态度来；只说你看见的，别添细节。";
}
