using System;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Dialogue;

/// <summary>"我的设定"：玩家写的自我介绍（每个存档一份，存档数据/character_sheet.json）。</summary>
public sealed class PlayerProfile
{
	[JsonPropertyName("born_day")]
	public int BornDay { get; set; }

	[JsonPropertyName("born_season")]
	public string BornSeason { get; set; } = "";

	[JsonPropertyName("came_because")]
	public string CameBecause { get; set; } = "";

	[JsonPropertyName("edited_on")]
	public string EditedOn { get; set; } = "";

	[JsonPropertyName("filled")]
	public bool Filled { get; set; }

	[JsonPropertyName("intro")]
	public string Intro { get; set; } = "";

	[JsonPropertyName("notes")]
	public string Notes { get; set; } = "";

	[JsonPropertyName("quick_facts")]
	public string QuickFacts { get; set; } = "";

	[JsonIgnore]
	public bool HasContent => !string.IsNullOrWhiteSpace(Intro) || !string.IsNullOrWhiteSpace(CameBecause) || !string.IsNullOrWhiteSpace(Notes) || !string.IsNullOrWhiteSpace(QuickFacts);

	[JsonIgnore]
	public bool HasBirthday => BornDay > 0 && !string.IsNullOrWhiteSpace(BornSeason);

	public static readonly DocumentType<PlayerProfile> Doc = new DocumentType<PlayerProfile>("我的设定", 1, () => new PlayerProfile());

	public const string Path = "farmer/about_me.json";
}

/// <summary>
/// 村民对"我的设定"知道多少、从哪知道的：住得远的什么都不知道；亲眼所见的、爱传话的、神秘的各有来源；
/// 普通村民按好感分三档（≤3 心只听过一耳朵，4～7 心算熟人，≥8 心很熟）。这些内容只当背景，不当八卦。
/// </summary>
public static class PlayerProfileBlock
{
	private sealed record Framing(string Source, string Guidance);

	public static string Build(string npcName, string playerName, int hearts, PlayerProfile? profile, bool spouse = false, bool ex = false)
	{
		if (profile == null || !profile.HasContent)
		{
			return "";
		}
		GossipStyle style = BuiltInPersonas.GossipStyleOf(npcName);
		if (style == GossipStyle.Isolated && !spouse && !ex)
		{
			return $"\n【关于农夫】你很少进村，没听过别人议论 {playerName}；你对 {playerName} 的了解只来自你们当面的交谈。";
		}
		Framing f = spouse ? new Framing($"{playerName} 是你的另一半，下面这些你早就知道", "用不着当新鲜事说。")
			: ex ? new Framing($"{playerName} 是你的前任，下面这些你早就知道", "用不着当新鲜事说。")
			: style switch
		{
			GossipStyle.Firsthand => new Framing($"你和 {playerName} 打过交道，下面这些是你自己知道的", "用不着说成听来的。"),
			GossipStyle.Spreader => new Framing($"关于 {playerName} 的事你打听得不少", "知道归知道，别借题发挥、四处传。"),
			GossipStyle.Mystical => new Framing($"关于 {playerName}，你靠占卜和感应知道了这些", "提起时说成是感应、预兆或幻象里看到的，不说是听人讲的。"),
			_ when hearts <= 3 => new Framing($"你和 {playerName} 还不熟，下面只是村里零星的说法", "一次最多碰一个点，轻描淡写；别当成可以嚼的舌根。"),
			_ when hearts <= 7 => new Framing($"你和 {playerName} 认识一段时间了", "可以自然地带到，不必交代消息来源。"),
			_ => new Framing($"你和 {playerName} 很熟，下面这些你早就知道", "心照不宣即可，多聊你们之间的事和最近的话题。")
		};
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【关于农夫】" + f.Source + "：");
		Add(sb, "身份", "PROFILE_WHO", profile.Intro);
		Add(sb, "搬来的原因", "PROFILE_WHY_MOVED", profile.CameBecause);
		Add(sb, "其他", "PROFILE_EXTRA", profile.Notes);
		Add(sb, "外表（眼睛看得到的，不算传闻）", "PROFILE_APPEARANCE", profile.QuickFacts);
		sb.AppendLine(f.Guidance);
		sb.AppendLine("这些只是背景：主要用来别叫错称呼、别和对方的样子对不上；不要拿来制造话题或剧情，也别反复提。");
		return sb.ToString();
	}

	/// <summary>多人场合大家共有的那份：只列设定，不分谁熟谁生。</summary>
	public static string Shared(string playerName, PlayerProfile? profile)
	{
		if (profile == null || !profile.HasContent)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"【关于农夫 {playerName}】");
		Add(sb, "身份", "PROFILE_WHO", profile.Intro);
		Add(sb, "搬来的原因", "PROFILE_WHY_MOVED", profile.CameBecause);
		Add(sb, "其他", "PROFILE_EXTRA", profile.Notes);
		Add(sb, "外表", "PROFILE_APPEARANCE", profile.QuickFacts);
		sb.AppendLine("这些只是背景，用来别叫错称呼、别和对方的样子对不上，不要拿来制造话题。");
		return sb.ToString().TrimEnd();
	}

	private static void Add(StringBuilder sb, string label, string tag, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			sb.AppendLine("· " + label + "：");
			sb.AppendLine(PromptText.Wrap(tag, value, 800));
		}
	}
}
