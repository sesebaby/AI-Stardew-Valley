using System;

namespace AISV.Core.Dialogue;

/// <summary>村民反应时的小动作：跳一下、停下、转过身、头顶冒的表情（游戏的 emote 编号）。</summary>
public sealed record EmotePlan(bool Jump, bool Halt, bool TurnAway, int Emote);

/// <summary>反应的效果规则，游戏层照着执行。</summary>
public static class ReactionEffects
{
	public const int HeartEmote = 20;

	public const int SadEmote = 28;

	public const int AngryEmote = 12;

	public const int SurprisedEmote = 16;

	public const int QuestionEmote = 8;

	/// <summary>
	/// love：8 心起跳一下 + 爱心；happy：得分 ≥80 跳一下 + 爱心；angry：≤3 心停下、转身 + 生气；
	/// sad：得分 &lt;40 停下 + 难过；unique：停下 + 惊讶；neutral 什么也不做。
	/// </summary>
	public static EmotePlan? Emote(string? emotion, int friendshipPoints, int effectiveHearts)
	{
		return Emotion.Normalize(emotion) switch
		{
			"love" => new EmotePlan(effectiveHearts >= 8, false, false, HeartEmote),
			"happy" => new EmotePlan(friendshipPoints >= 80, false, false, HeartEmote),
			"angry" => effectiveHearts <= 3 ? new EmotePlan(false, true, true, AngryEmote) : new EmotePlan(false, false, false, AngryEmote),
			"sad" => new EmotePlan(false, friendshipPoints < 40, false, SadEmote),
			"unique" => new EmotePlan(false, true, false, SurprisedEmote),
			_ => null
		};
	}

	/// <summary>出错时退回的简单表情（只冒表情）。</summary>
	public static int? PlainEmote(string? emotion) => Emotion.Normalize(emotion) switch
	{
		"happy" or "love" => HeartEmote,
		"sad" => SadEmote,
		"angry" => AngryEmote,
		"unique" => QuestionEmote,
		_ => null
	};

	/// <summary>
	/// 这次反应让村民心情变多少（第 3 期心情系统用）：开心类 max(5, 分×2)、生气类 min(-5, 分×2)、难过类 min(-3, -分)、中性 0、其他 = 分。
	/// 用的是 AI 原样给的情绪词（没归一化）。
	/// </summary>
	public static int MoodChange(string? rawEmotion, int friendshipPoints) => (rawEmotion ?? "").ToLowerInvariant() switch
	{
		"loved" or "happy" or "excited" or "joyful" or "grateful" => Math.Max(5, friendshipPoints * 2),
		"upset" or "angry" or "annoyed" or "frustrated" => Math.Min(-5, friendshipPoints * 2),
		"sad" or "disappointed" or "hurt" => Math.Min(-3, -friendshipPoints),
		"neutral" => 0,
		_ => friendshipPoints
	};

	/// <summary>游戏的好感上限：结婚 14 心、约会 10 心、可恋爱但没约会 8 心、不可恋爱 10 心；上限点数 = (心数 + 1) × 250 − 1。</summary>
	public static int MaxFriendshipPoints(bool canBeRomanced, bool dating, bool married)
	{
		int hearts = married ? 14 : dating ? 10 : !canBeRomanced ? 10 : 8;
		return (hearts + 1) * 250 - 1;
	}

	public static int ApplyFriendshipChange(int current, int amount, int max) => Math.Max(0, Math.Min(current + amount, max));
}
