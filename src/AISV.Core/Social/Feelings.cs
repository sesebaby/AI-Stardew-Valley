using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Text;

namespace AISV.Core.Social;

/// <summary>村民对农夫的整体感受（每周日由 AI 根据最近的对话重新评估）。</summary>
public sealed class EmotionalProfile
{
	/// <summary>-1～1。</summary>
	[JsonPropertyName("warmth")]
	public float Affection { get; set; }

	/// <summary>10 心以上才有。</summary>
	[JsonPropertyName("climate")]
	public RelationshipDynamics? Climate { get; set; }

	/// <summary>0～1。</summary>
	[JsonPropertyName("trust")]
	public float FaithInFarmer { get; set; } = 0.5f;

	[JsonPropertyName("impression")]
	public string Impression { get; set; } = "";

	[JsonPropertyName("latest_moment")]
	public string LatestMoment { get; set; } = "";

	[JsonPropertyName("npc")]
	public string Npc { get; set; } = "";

	/// <summary>最新的在前，最多 5 条。</summary>
	[JsonPropertyName("memories")]
	public List<string> Recollections { get; set; } = new List<string>();

	[JsonPropertyName("sores")]
	public List<NegativeMemory> Sores { get; set; } = new List<NegativeMemory>();

	[JsonPropertyName("stories_lived")]
	public List<string> StoriesLived { get; set; } = new List<string>();

	[JsonPropertyName("touched_on")]
	public string TouchedOn { get; set; } = "";

	internal void Normalize()
	{
		Recollections ??= new List<string>();
		StoriesLived ??= new List<string>();
		Sores ??= new List<NegativeMemory>();
		if (Climate != null)
		{
			Climate.OpenWounds ??= new List<string>();
		}
	}
}

/// <summary>一段会过期的不愉快记忆。</summary>
public sealed class NegativeMemory
{
	/// <summary>conversation / gift / promise。</summary>
	[JsonPropertyName("source")]
	public string Cause { get; set; } = "";

	[JsonPropertyName("heals_on")]
	public int HealsOn { get; set; }

	[JsonPropertyName("since_day")]
	public int SinceDay { get; set; }

	/// <summary>1～3。</summary>
	[JsonPropertyName("sting")]
	public int Sting { get; set; } = 1;

	[JsonPropertyName("text")]
	public string Text { get; set; } = "";
}

/// <summary>10 心以上的关系里，信任、亲密、紧张、玩闹这几样还在慢慢变。</summary>
public sealed class RelationshipDynamics
{
	[JsonPropertyName("warmth")]
	public float Affection { get; set; } = 0.75f;

	[JsonPropertyName("trust")]
	public float Faith { get; set; } = 0.75f;

	/// <summary>最新的在前，最多 3 条。</summary>
	[JsonPropertyName("open_wounds")]
	public List<string> OpenWounds { get; set; } = new List<string>();

	[JsonPropertyName("strain")]
	public float Strain { get; set; }

	[JsonPropertyName("teasing")]
	public float Teasing { get; set; } = 0.35f;

	[JsonPropertyName("touched_on")]
	public string TouchedOn { get; set; } = "";
}

/// <summary>聊完一轮后判断"这次是不愉快还是很愉快"用的几个条件（负面记忆和关系动态共用）。</summary>
internal static class ExchangeTone
{
	public static string Emotion(NpcReaction r) => r.Emotion?.ToLowerInvariant() ?? "neutral";

	/// <summary>情绪 angry / sad；或者情绪不明确、得分 ≤ 10。开心时得分再低也不算不愉快。</summary>
	public static bool Bad(NpcReaction r) => Emotion(r) is "angry" or "sad"
		|| Emotion(r) is not ("happy" or "love") && r.FriendshipPoints <= 10;

	/// <summary>情绪 happy / love；或者情绪不明确、得分 ≥ 50。生气时得分再高也不算愉快。</summary>
	public static bool Good(NpcReaction r) => Emotion(r) is "happy" or "love"
		|| Emotion(r) is not ("angry" or "sad") && r.FriendshipPoints >= 50;
}

/// <summary>
/// 负面记忆的规则：每位村民最多 3 条，新的在前；严重度 3 记 14 天、2 记 9 天、1 记 5 天；
/// 同来源同内容的只刷新。聊得不愉快就记一条；聊得愉快减 1 级，道歉减 2 级，减到 0 就忘了。
/// </summary>
public static class NegativeMemories
{
	public const int Max = 3;

	public const string ConversationHurt = "最近一次和农夫聊天，对方的话让人受伤或者觉得被敷衍。";

	public static void AfterExchange(List<NegativeMemory> list, NpcReaction reaction, string playerText, int today)
	{
		RemoveExpired(list, today);
		bool apology = Keywords.Any(playerText, Keywords.Apology);
		if (ExchangeTone.Bad(reaction))
		{
			int severity = ExchangeTone.Emotion(reaction) == "angry" || reaction.FriendshipPoints <= 5 ? 3 : 2;
			AddOrRefresh(list, "conversation", ConversationHurt, severity, today);
		}
		else if (ExchangeTone.Good(reaction) || apology)
		{
			Soften(list, apology ? 2 : 1);
		}
	}

	/// <summary>送了讨厌（3 级）或不喜欢（1 级）的礼物；生日送的再加 1 级（最多 3）。</summary>
	public static void Gift(List<NegativeMemory> list, string itemName, string taste, bool isBirthday, int today)
	{
		RemoveExpired(list, today);
		string t = taste.ToLowerInvariant();
		if (t != "hated" && t != "disliked")
		{
			return;
		}
		int severity = t == "hated" ? 3 : 1;
		if (isBirthday && severity < 3)
		{
			severity++;
		}
		string what = t == "hated" ? "讨厌的东西" : "不喜欢的东西";
		string when = isBirthday ? "，还是在生日那天" : "";
		AddOrRefresh(list, "gift", $"农夫送了{itemName}——是{what}{when}。", severity, today);
	}

	public static void BrokenPromise(List<NegativeMemory> list, string promise, int today)
	{
		RemoveExpired(list, today);
		string text = string.IsNullOrWhiteSpace(promise) ? "农夫答应过的事没做到。" : "农夫答应过的事没做到：" + promise;
		AddOrRefresh(list, "promise", text, 3, today);
	}

	public static string PromptBlock(List<NegativeMemory> list, int today)
	{
		RemoveExpired(list, today);
		if (list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【还没消的气】日子久了会淡");
		foreach (NegativeMemory m in list.OrderByDescending(m => m.Sting).ThenByDescending(m => m.SinceDay))
		{
			sb.AppendLine($"- {m.Text}（分量 {m.Sting}/3）");
		}
		sb.AppendLine("话题碰到这些时才露出来；农夫赔了不是，或者做了让你舒心的事，气就消一些，不必一直揪着。");
		return sb.ToString();
	}

	public static void RemoveExpired(List<NegativeMemory> list, int today)
	{
		if (today > 0)
		{
			list.RemoveAll(m => m.HealsOn > 0 && m.HealsOn < today);
		}
	}

	public static int DaysFor(int severity) => severity >= 3 ? 14 : severity == 2 ? 9 : 5;

	private static void AddOrRefresh(List<NegativeMemory> list, string source, string description, int severity, int today)
	{
		NegativeMemory? same = list.FirstOrDefault(m => string.Equals(m.Cause, source, StringComparison.OrdinalIgnoreCase) && string.Equals(m.Text, description, StringComparison.OrdinalIgnoreCase));
		int expires = today + DaysFor(severity);
		if (same != null)
		{
			same.Sting = Math.Max(same.Sting, severity);
			same.SinceDay = today;
			same.HealsOn = Math.Max(same.HealsOn, expires);
		}
		else
		{
			list.Insert(0, new NegativeMemory { Cause = source, Text = description, Sting = severity, SinceDay = today, HealsOn = expires });
		}
		while (list.Count > Max)
		{
			list.RemoveAt(list.Count - 1);
		}
	}

	private static void Soften(List<NegativeMemory> list, int amount)
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			list[i].Sting -= amount;
			if (list[i].Sting <= 0)
			{
				list.RemoveAt(i);
			}
		}
	}
}

/// <summary>
/// 10 心以上的关系动态：不愉快时信任 -0.05、亲密 -0.06、紧张 +0.14 并记一条没解开的心结；
/// 愉快时信任 +0.04、亲密 +0.05、紧张 -0.08、玩闹 +0.02（love 时 +0.04），紧张不超过 0.2 时解开最旧的一条心结；
/// 其他时候信任 +0.01、紧张 -0.02、玩闹 +0.01。
/// </summary>
public static class Dynamics
{
	public const string TenseExchangeIssue = "前阵子有一回说话闹僵了，还等着好好缓和一下。";

	public static RelationshipDynamics Default(int hearts)
	{
		float start = hearts >= 12 ? 0.85f : 0.75f;
		return new RelationshipDynamics { Faith = start, Affection = start, Strain = 0.05f, Teasing = hearts >= 12 ? 0.45f : 0.35f };
	}

	public static RelationshipDynamics AfterExchange(RelationshipDynamics? current, int hearts, NpcReaction reaction, string dateLabel)
	{
		RelationshipDynamics d = current ?? Default(hearts);
		if (hearts < 10)
		{
			return d;
		}
		if (ExchangeTone.Bad(reaction))
		{
			d.Faith = Clamp(d.Faith - 0.05f);
			d.Affection = Clamp(d.Affection - 0.06f);
			d.Strain = Clamp(d.Strain + 0.14f);
			if (!d.OpenWounds.Any(i => string.Equals(i, TenseExchangeIssue, StringComparison.OrdinalIgnoreCase)))
			{
				d.OpenWounds.Insert(0, TenseExchangeIssue);
			}
			while (d.OpenWounds.Count > 3)
			{
				d.OpenWounds.RemoveAt(d.OpenWounds.Count - 1);
			}
		}
		else if (ExchangeTone.Good(reaction))
		{
			d.Faith = Clamp(d.Faith + 0.04f);
			d.Affection = Clamp(d.Affection + 0.05f);
			d.Strain = Clamp(d.Strain - 0.08f);
			d.Teasing = Clamp(d.Teasing + (ExchangeTone.Emotion(reaction) == "love" ? 0.04f : 0.02f));
			if (d.Strain <= 0.2f && d.OpenWounds.Count > 0)
			{
				d.OpenWounds.RemoveAt(d.OpenWounds.Count - 1);
			}
		}
		else
		{
			d.Faith = Clamp(d.Faith + 0.01f);
			d.Strain = Clamp(d.Strain - 0.02f);
			d.Teasing = Clamp(d.Teasing + 0.01f);
		}
		d.TouchedOn = dateLabel;
		return d;
	}

	public static string PromptBlock(int hearts, RelationshipDynamics? d)
	{
		if (hearts < 10 || d == null)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【你们俩如今】");
		sb.AppendLine($"- 彼此的信赖：{Level(d.Faith, "深得像扎了根", "很稳", "有，但还留着心眼", "有了裂痕")}");
		sb.AppendLine($"- 亲昵：{Level(d.Affection, "亲昵到藏不住", "热乎", "温和、有保留", "隔着点距离")}");
		sb.AppendLine($"- 摩擦：{TensionWord(d.Strain)}");
		sb.AppendLine($"- 闹着玩：{PlayWord(d.Teasing)}");
		if (d.OpenWounds.Count > 0)
		{
			sb.AppendLine("- 心里的结：");
			foreach (string issue in d.OpenWounds.Take(3))
			{
				sb.AppendLine("  - " + issue);
			}
		}
		sb.AppendLine($"你们已经 {hearts} 心，感情还在往前走；这些分寸放在说话的方式里，不要说破。");
		return sb.ToString();
	}

	private static string Level(float v, string top, string high, string mid, string low) => v >= 0.85f ? top : v >= 0.65f ? high : v >= 0.45f ? mid : low;

	private static string TensionWord(float v) => v >= 0.65f ? "很重，旧伤还疼" : v >= 0.35f ? "有，但忍得住" : v >= 0.15f ? "偶尔别扭一下" : "几乎感觉不到";

	private static string PlayWord(float v) => v >= 0.75f ? "成天逗来逗去" : v >= 0.45f ? "常开小玩笑" : v >= 0.2f ? "偶尔幽默一回" : "多半正正经经";

	private static float Clamp(float v) => Math.Clamp(v, 0f, 1f);
}
