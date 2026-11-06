using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Social.Gossip;

namespace AISV.Core.Social.Drama;

/// <summary>社交剧情写给 AI 的文字，以及存进看法和片段里的原因。<c>name</c> 把内部名换成显示名。</summary>
public static class DramaPrompt
{
	public static string Block(IReadOnlyList<Opinion> opinions, IReadOnlyList<Influence> influences, IReadOnlyList<Rumor> rumors, IReadOnlyList<AdultStrain> adult, string npc, bool dramatic, Func<string, string> name)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【村民之间的事】");
		if (dramatic)
		{
			sb.AppendLine("这一档可以闹得开：只要事出有因，就别光顾着客气地辩解，火气、委屈、醋意都能直说，话不投机也可以当面挑明。当然得是你这个人会有的反应，大小和事情相当；牵扯到感情时两边都情愿，不写露骨的东西。");
		}
		foreach (Opinion o in opinions)
		{
			sb.Append("- 你对").Append(name(o.About)).Append("：").Append(Stance(o.Attitude));
			if (!string.IsNullOrWhiteSpace(o.Why))
			{
				sb.Append("。起因：").Append(DramaRules.Trim(o.Why));
			}
			sb.AppendLine();
		}
		foreach (Influence a in influences)
		{
			sb.Append("- ").Append(InfluenceSentence(a, name)).Append("这是农夫的说法，不一定是真的。");
			if (!string.IsNullOrWhiteSpace(a.Subject))
			{
				sb.Append("原话：").Append(DramaRules.Trim(a.Subject));
			}
			sb.AppendLine();
		}
		foreach (Rumor r in rumors)
		{
			sb.Append("- 你听过一个传闻");
			if (r.About.Count > 0)
			{
				sb.Append("（说的是").Append(string.Join("、", r.About.Select(name))).Append("）");
			}
			sb.Append("：").Append(Sentence(r.Subject)).Append(Belief(r, npc)).Append("，").Append(Credibility(r.Believability)).Append("，").Append(Distortion(r.Warp)).Append("，").Append(Reach(r.Spread));
			sb.AppendLine("。终究是传闻，没有坐实。");
		}
		foreach (AdultStrain s in adult)
		{
			sb.Append("- ").AppendLine(AdultLine(s, name));
		}
		sb.AppendLine("以上是你心里有数的事，不是要背的台词：和眼下聊的事不沾边就别提；提起别人时，要么说你真实的看法或疑虑，要么略过不说。不知道的八卦不要抖出来，没发生的事不要编，也别骗农夫。真闹起来，轻重和方式都要像你。");
		return sb.ToString();
	}

	/// <summary>一段话收成一句：末尾没有句号类标点时补一个"。"。</summary>
	public static string Sentence(string? text)
	{
		string t = DramaRules.Trim(text);
		return t.Length == 0 || "。！？…!?.".Contains(t[^1]) ? t : t + "。";
	}

	public static string Stance(string stance) => stance switch
	{
		Stances.Admiring => "多了几分佩服",
		Stances.Protective => "想护着对方",
		Stances.Annoyed => "有点烦",
		Stances.Suspicious => "起了疑心",
		Stances.Cold => "冷淡下来了",
		Stances.Badmouthing => "憋着想私下数落几句",
		Stances.Betrayed => "觉得被辜负了",
		_ => "没什么特别的感觉"
	};

	/// <summary>农夫做了什么，写给被影响的那位村民看（第二人称）。</summary>
	public static string InfluenceSentence(Influence a, Func<string, string> name)
	{
		string t = name(a.Aim);
		return a.Move switch
		{
			DramaText.Defend => $"农夫最近在你面前替{t}说过好话。",
			DramaText.Leak => $"农夫最近跟你透露过一件关于{t}的事。",
			DramaText.Warn => $"农夫最近提醒你提防{t}。",
			DramaText.Instigate => $"农夫最近想让你对{t}起疑心。",
			_ => $"农夫最近想改变你对{t}的看法。"
		};
	}

	/// <summary>剧情片段里记的一句（第三人称）。</summary>
	public static string InfluenceSummary(Influence a, Func<string, string> name)
	{
		string t = name(a.Aim), s = name(a.Talker);
		return a.Move switch
		{
			DramaText.Defend => $"农夫在{s}面前替{t}说了话。",
			DramaText.Leak => $"农夫跟{s}透露了一件关于{t}的事。",
			DramaText.Warn => $"农夫提醒{s}提防{t}。",
			DramaText.Instigate => $"农夫想让{s}对{t}起疑心。",
			_ => $"农夫想改变{s}对{t}的看法。"
		};
	}

	public static string GossipReason(GossipEntry e, Func<string, string> name)
	{
		string text = DramaRules.Trim(string.IsNullOrWhiteSpace(e.Fact) ? e.Rumor : e.Fact, 180);
		return string.IsNullOrWhiteSpace(e.StartedBy) ? "听到一条八卦：" + text : $"听到一条和{name(e.StartedBy)}有关的八卦：{text}";
	}

	public static string Belief(Rumor r, string npc) =>
		RumorRules.Believes(r, npc) ? "你倾向于相信" : RumorRules.Doubts(r, npc) ? "你不太信" : "你只是听说";

	public static string Credibility(float v) => v >= 0.72f ? "听着挺可信" : v >= 0.45f ? "可信度一般" : "听着不太靠谱";

	public static string Distortion(float v) => v >= 0.65f ? "传得已经很走样" : v >= 0.35f ? "传得有些走样" : "基本没走样";

	public static string Reach(string reach) => reach switch
	{
		RumorRules.Town => "全镇都在传",
		RumorRules.Circle => "在一小圈人里传开了",
		_ => "只有少数人知道"
	};

	public static string AdultLine(AdultStrain s, Func<string, string> name)
	{
		string spouse = string.IsNullOrWhiteSpace(s.Spouse) ? "你的另一半" : name(s.Spouse);
		string outsider = string.IsNullOrWhiteSpace(s.Third) ? "别人" : name(s.Third);
		return s.Phase switch
		{
			AdultRules.Contemplating => $"感情上的事（成年人）：眼下{outsider}对你的关注让你有点动摇。你能做的：心乱可以承认，界限也可以划清；愧疚是真的，选择权在你手里，不必急着越过那一步。",
			AdultRules.Tempted => $"感情上的事（成年人）：你和{spouse}闹着别扭，{outsider}的关注就显得格外扎眼。你能做的：可以流露挣扎，也可以随时守住底线。",
			AdultRules.Strained => $"感情上的事（成年人）：你和{spouse}最近不太顺。你能做的：心里的不痛快可以照实说，但别给对方安罪名，对农夫也不说假话。",
			_ => "感情上的事（成年人）：" + s.Gist
		};
	}
}
