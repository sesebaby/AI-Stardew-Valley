using System;
using System.Collections.Generic;

namespace AISV.Core.Consequences;

/// <summary>撞上你做的坏事时当场的反应：当场骂 / 冷脸 / 难过失望 / 阴阳怪气。</summary>
public enum ReactionStyle
{
	Hot,
	Cold,
	Hurt,
	Snide
}

/// <summary>各人的反应方式和随之不同的轻重。</summary>
public static class ReactionStyles
{
	/// <summary>原版村民的脾气（其他 mod 的 NPC 让 AI 按人设判断，判断不了当成 Hot）。</summary>
	public static readonly IReadOnlyDictionary<string, ReactionStyle> Known = new Dictionary<string, ReactionStyle>(StringComparer.OrdinalIgnoreCase)
	{
		["Shane"] = ReactionStyle.Hot, ["Haley"] = ReactionStyle.Hot, ["Clint"] = ReactionStyle.Hot, ["Marnie"] = ReactionStyle.Hot, ["Pierre"] = ReactionStyle.Hot,
		["Pam"] = ReactionStyle.Hot, ["Kent"] = ReactionStyle.Hot, ["Gus"] = ReactionStyle.Hot, ["Willy"] = ReactionStyle.Hot, ["Lewis"] = ReactionStyle.Hot,
		["Robin"] = ReactionStyle.Hot, ["Jodi"] = ReactionStyle.Hot, ["Abigail"] = ReactionStyle.Hot,
		["Sebastian"] = ReactionStyle.Cold, ["Elliott"] = ReactionStyle.Cold, ["Demetrius"] = ReactionStyle.Cold, ["Harvey"] = ReactionStyle.Cold,
		["Linus"] = ReactionStyle.Cold, ["Wizard"] = ReactionStyle.Cold, ["Krobus"] = ReactionStyle.Cold, ["Morris"] = ReactionStyle.Cold,
		["Penny"] = ReactionStyle.Hurt, ["Leah"] = ReactionStyle.Hurt, ["Evelyn"] = ReactionStyle.Hurt, ["George"] = ReactionStyle.Hurt,
		["Emily"] = ReactionStyle.Hurt, ["Caroline"] = ReactionStyle.Hurt, ["Maru"] = ReactionStyle.Hurt, ["Sandy"] = ReactionStyle.Hurt,
		["Alex"] = ReactionStyle.Snide, ["Sam"] = ReactionStyle.Snide, ["Gil"] = ReactionStyle.Snide, ["Marlon"] = ReactionStyle.Snide
	};

	public static ReactionStyle? StyleOf(string npc) => Known.TryGetValue(npc, out ReactionStyle s) ? s : null;

	public static ReactionStyle Parse(string? text) => text?.Trim().ToLowerInvariant() switch
	{
		"cold" => ReactionStyle.Cold,
		"hurt" => ReactionStyle.Hurt,
		"snide" => ReactionStyle.Snide,
		_ => ReactionStyle.Hot
	};

	/// <summary>写进存档、提示词、目录规则里的小写名字。</summary>
	public static string Id(ReactionStyle s) => s.ToString().ToLowerInvariant();

	/// <summary>好感扣多少倍：冷脸的最记仇，难过的其次，阴阳怪气的嘴上不饶人、心里没那么在意。</summary>
	public static double FriendshipFactor(ReactionStyle s) => s switch
	{
		ReactionStyle.Cold => 1.3,
		ReactionStyle.Hurt => 1.2,
		ReactionStyle.Snide => 0.8,
		_ => 1.0
	};

	/// <summary>心情掉多少。</summary>
	public static int MoodDrop(ReactionStyle s) => s switch
	{
		ReactionStyle.Hurt => -20,
		ReactionStyle.Hot => -15,
		ReactionStyle.Cold => -12,
		_ => -8
	};
}
