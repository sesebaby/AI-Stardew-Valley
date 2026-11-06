using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Dialogue;

/// <summary>开场白的方向。General 不加方向提示，其余各对应 assets/prompts/focus_*.txt。</summary>
public enum OpeningTopic
{
	General,
	Personal,
	NoQuestion,
	Memory,
	Beach,
	LowMood,
	HighMood,
	Gossip,
	Gift,
	Routine,
	Story,
	Romance,
	ItemRequest
}

/// <summary>选方向要看的情况（游戏层和各子系统算好交过来）。</summary>
public sealed class OpeningTopicInput
{
	public string Location { get; init; } = "";

	public bool LowMood { get; init; }

	public bool HighMood { get; init; }

	/// <summary>有八卦记忆、社交剧情或村民之间的互动可聊。</summary>
	public bool HasGossip { get; init; }

	public bool HasGift { get; init; }

	/// <summary>有事件、里程碑、故事弧、已定传说，或看过这位村民的心事件。</summary>
	public bool HasStory { get; init; }

	public bool IsAdultRomanticPartner { get; init; }

	public bool IsOpeningLine { get; init; }

	public bool CanRequestItem { get; init; }

	public int ItemRequestChancePercent { get; init; }

	public IReadOnlyList<ConversationEntry> RecentHistory { get; init; } = Array.Empty<ConversationEntry>();
}

/// <summary>
/// 按权重随机挑开场白的方向，权重
/// 一般 10、聊自己 12、不发问（最近 4 句里有 2 句以上是问句时 28，否则 6）、旧事（有记录 14，否则 4）、日常 10；
/// 海边 30、心情低 30、心情高 24、八卦 28、心意 24、后续 26、恋人 30（满足条件才有）。
/// 托你带东西：只在今天第一句、条件满足时，先按概率单独掷一次。
/// </summary>
public sealed class OpeningTopicPicker
{
	private readonly Random _random;

	public OpeningTopicPicker(Random? random = null)
	{
		_random = random ?? new Random();
	}

	public OpeningTopic Pick(OpeningTopicInput input)
	{
		if (CanRequestItem(input) && _random.NextDouble() < Math.Clamp(input.ItemRequestChancePercent, 0, 100) / 100.0)
		{
			return OpeningTopic.ItemRequest;
		}
		List<(OpeningTopic Topic, int Weight)> weights = Weights(input).Where(w => w.Topic != OpeningTopic.ItemRequest).ToList();
		int total = weights.Sum(w => w.Weight);
		if (total <= 0)
		{
			return OpeningTopic.General;
		}
		int roll = _random.Next(total);
		foreach ((OpeningTopic topic, int weight) in weights)
		{
			roll -= weight;
			if (roll < 0)
			{
				return topic;
			}
		}
		return OpeningTopic.General;
	}

	public static IReadOnlyList<(OpeningTopic Topic, int Weight)> Weights(OpeningTopicInput input)
	{
		List<(OpeningTopic, int)> list = new List<(OpeningTopic, int)>
		{
			(OpeningTopic.General, 10),
			(OpeningTopic.Personal, 12),
			(OpeningTopic.NoQuestion, AskedTooMuch(input.RecentHistory) ? 28 : 6),
			(OpeningTopic.Memory, input.RecentHistory.Count > 0 ? 14 : 4),
			(OpeningTopic.Routine, 10)
		};
		if (string.Equals(input.Location, "Beach", StringComparison.OrdinalIgnoreCase))
		{
			list.Add((OpeningTopic.Beach, 30));
		}
		if (input.LowMood)
		{
			list.Add((OpeningTopic.LowMood, 30));
		}
		if (input.HighMood)
		{
			list.Add((OpeningTopic.HighMood, 24));
		}
		if (input.HasGossip)
		{
			list.Add((OpeningTopic.Gossip, 28));
		}
		if (input.HasGift)
		{
			list.Add((OpeningTopic.Gift, 24));
		}
		if (input.HasStory)
		{
			list.Add((OpeningTopic.Story, 26));
		}
		if (input.IsAdultRomanticPartner)
		{
			list.Add((OpeningTopic.Romance, 30));
		}
		if (CanRequestItem(input))
		{
			list.Add((OpeningTopic.ItemRequest, Math.Clamp(input.ItemRequestChancePercent, 0, 100)));
		}
		return list;
	}

	/// <summary>assets/prompts 里的方向提示文件名；General 没有。</summary>
	public static string? FocusTemplate(OpeningTopic topic) => topic switch
	{
		OpeningTopic.Personal => "focus_personal",
		OpeningTopic.NoQuestion => "focus_no_question",
		OpeningTopic.Memory => "focus_memory",
		OpeningTopic.Beach => "focus_beach",
		OpeningTopic.LowMood => "focus_low_mood",
		OpeningTopic.HighMood => "focus_high_mood",
		OpeningTopic.Gossip => "focus_gossip",
		OpeningTopic.Gift => "focus_gift",
		OpeningTopic.Routine => "focus_routine",
		OpeningTopic.Story => "focus_story",
		OpeningTopic.Romance => "focus_romance",
		OpeningTopic.ItemRequest => "focus_item_request",
		_ => null
	};

	private static bool CanRequestItem(OpeningTopicInput input) => input.CanRequestItem && input.IsOpeningLine && input.ItemRequestChancePercent > 0;

	/// <summary>最近 4 句开场白里至少 2 句带问号（中英文问号都算）。</summary>
	private static bool AskedTooMuch(IReadOnlyList<ConversationEntry> history) =>
		history.Skip(Math.Max(0, history.Count - 4)).Count(e => (e.Opener ?? "").IndexOfAny(new[] { '?', '？' }) >= 0) >= 2;
}
