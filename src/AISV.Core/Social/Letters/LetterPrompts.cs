using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Dialogue;
using AISV.Core.Social.Gossip;

namespace AISV.Core.Social.Letters;

/// <summary>写信提示词里的几块。</summary>
public static class LetterPrompts
{
	public const string Warm = "这封信写得温暖、动情一些。";

	public const string Everyday = "用平常、自然的口气写。";

	public const string Playful = "合你性格的话，可以带点轻松的幽默和玩笑。";

	/// <summary>
	/// 口气：心数里程碑的信要温暖；"想你了"在信任 ≥ 0.4、亲密 ≥ 0.3 时一半概率俏皮；其他信在信任 ≥ 0.6、亲密 ≥ 0.5 时俏皮；否则平常。
	/// </summary>
	public static string StyleHint(string trigger, EmotionalProfile? p, Func<int, int> roll)
	{
		if (trigger.StartsWith("hearts_", StringComparison.Ordinal))
		{
			return Warm;
		}
		float trust = p?.FaithInFarmer ?? 0f, warmth = p?.Affection ?? 0f;
		if (trigger.StartsWith("miss_you", StringComparison.Ordinal))
		{
			return trust >= 0.4f && warmth >= 0.3f && roll(2) == 0 ? Playful : Everyday;
		}
		return trust >= 0.6f && warmth >= 0.5f ? Playful : Everyday;
	}

	/// <summary>有关键记忆或最近大事时：现在的信任程度 + 一件可以轻轻提到的事。</summary>
	public static string Feelings(EmotionalProfile? p)
	{
		if (p == null || p.Recollections.Count == 0 && string.IsNullOrWhiteSpace(p.LatestMoment))
		{
			return "";
		}
		string trust = p.FaithInFarmer >= 0.8f ? "很深" : p.FaithInFarmer >= 0.6f ? "很牢靠" : p.FaithInFarmer >= 0.4f ? "一般" : p.FaithInFarmer >= 0.2f ? "还有点戒备" : "几乎没有";
		string memory = p.Recollections.Count > 0 ? p.Recollections[0] : p.LatestMoment;
		return $"你现在对农夫的信任：{trust}。\n可以不着痕迹地提一下这件事：“{memory}”";
	}

	/// <summary>信任 ≥ 0.5、亲密 ≥ 0.3、口气允许玩笑时：可以拿来打趣的共同记忆和你说过的话。</summary>
	public static string Teasing(EmotionalProfile? p, IReadOnlyList<ConversationEntry> recent, string styleHint)
	{
		if (p == null || p.FaithInFarmer < 0.5f || p.Affection < 0.3f || styleHint != Playful)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("觉得自然的话，可以提提你们共同的回忆，或者开个小玩笑：");
		foreach (string m in p.Recollections.Take(2))
		{
			sb.AppendLine("- " + m);
		}
		foreach (ConversationEntry e in recent.Take(3).Where(e => !string.IsNullOrWhiteSpace(e.Opener)))
		{
			sb.AppendLine($"- 你说过：“{e.Opener}”");
		}
		return sb.ToString().TrimEnd();
	}

	/// <summary>这位村民知道的最近两条八卦。</summary>
	public static string Gossip(IReadOnlyList<GossipEntry> known)
	{
		if (known.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("镇上最近的事，你可以按自己的性格说说看法：");
		foreach (GossipEntry e in known)
		{
			sb.AppendLine("- " + e.Fact);
		}
		return sb.ToString().TrimEnd();
	}

	/// <summary>最近和这位农夫的几轮对话（村民说的、农夫回的）。</summary>
	public static string History(IReadOnlyList<ConversationEntry> recent)
	{
		StringBuilder sb = new StringBuilder();
		foreach (ConversationEntry e in recent)
		{
			sb.AppendLine("  村民：" + e.Opener);
			if (!string.IsNullOrEmpty(e.FarmerReply))
			{
				sb.AppendLine("  农夫：" + SkippedReply.ForPrompt(e.FarmerReply));
			}
		}
		return sb.ToString();
	}
}
