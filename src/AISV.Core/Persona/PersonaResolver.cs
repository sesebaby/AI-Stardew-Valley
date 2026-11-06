using System;
using System.Collections.Generic;
using System.Text;
using AISV.Core.Compat;

namespace AISV.Core.Persona;

/// <summary>游戏 NPC 身上的性格整数（Age、Manners、SocialAnxiety、Optimism），原样传进来。</summary>
public sealed record NpcTraits(int Age, int Manner, int SocialAnxiety, int Optimism);

/// <summary>算人设要的原料：游戏层从 Data/Characters、CustomFields、角色卡里取出来交给 Core。</summary>
public sealed class PersonaInput
{
	public string Name { get; init; } = "";

	public string? DisplayName { get; init; }

	public NpcTraits? Traits { get; init; }

	public IReadOnlyDictionary<string, string>? CustomFields { get; init; }

	public CharacterCard? Card { get; init; }

	/// <summary>这位村民来自哪个 mod（不是游戏自带的村民时）。</summary>
	public string? SourceModId { get; init; }
}

public enum PersonaSource
{
	BuiltIn,
	Sve,
	ContentPack,
	Generated,
	Card
}

/// <summary>算好的人设：给 AI 的背景身份文字、来源（编辑页显示）、改过的名字。</summary>
public sealed record NpcPersona(string Name, string Personality, PersonaSource Source, string DisplayNameOverride);

/// <summary>
/// 人设的优先级：角色卡（替换）&gt; 内容包 CustomFields（ManagedNpc）&gt; 内置人设 &gt; SVE 人设 &gt; 按游戏性格数值生成。
/// 角色卡只写补充项时，补在下面几层算出的人设后面。第一年肯特不在镇上，由对话提示词里的规则交代。
/// </summary>
public static class PersonaResolver
{
	// 内容包的键。
	public const string OwnPrefix = "AIStardewValley/";

	public static NpcPersona Resolve(PersonaInput input, int year)
	{
		string name = input.Name;
		CharacterCard? card = input.Card;
		ContentPackPersona pack = ContentPackPersona.Read(input.CustomFields);
		string text;
		PersonaSource source;
		if (card != null && card.Replaces)
		{
			text = Compose(card.Personality, card.Appearance, card.Lore, "背景", card.SocialTags, credit: "");
			source = PersonaSource.Card;
		}
		else if (card != null && pack.Managed)
		{
			// 内容包原创角色 + 只写补充项的角色卡：同名字段以卡为准，再合成（不重复追加）。
			ContentPackPersona merged = pack with
			{
				Appearance = Pick(card.Appearance, pack.Appearance),
				Lore = Pick(card.Lore, pack.Lore),
				SocialTags = Pick(card.SocialTags, pack.SocialTags)
			};
			(text, _) = Default(input, merged);
			source = PersonaSource.Card;
		}
		else
		{
			(text, source) = Default(input, pack);
			if (card != null)
			{
				text = Supplement(text, card);
				source = PersonaSource.Card;
			}
		}
		if (card != null && !string.IsNullOrWhiteSpace(card.Speech))
		{
			text += " 说话方式：" + card.Speech.Trim().TrimEnd('。') + "。";
		}
		return new NpcPersona(name, text, source, card?.DisplayName?.Trim() ?? "");
	}

	/// <summary>
	/// 告诉 AI 这位村民的来历：内容包原创角色、SVE 角色、其他 mod 角色；游戏自带的村民没有这一块。
	/// </summary>
	public static string ContextBlock(PersonaInput input, string? location)
	{
		ContentPackPersona pack = ContentPackPersona.Read(input.CustomFields);
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		string where = string.IsNullOrWhiteSpace(location) ? "" : $"（地点键：{location}）";
		if (pack.Managed)
		{
			sb.AppendLine("【来历：内容包原创角色】" + where);
			sb.AppendLine("下面这些字段是作者定下的，本存档以此为准；存档里发生的事只能往上加，不能推翻它们。");
			AppendField(sb, "性格", pack.Personality);
			AppendField(sb, "背景", pack.Lore);
			AppendField(sb, "社交标签", pack.SocialTags);
			AppendField(sb, "投稿署名", pack.Credit);
			return sb.ToString();
		}
		string? modId = input.SourceModId;
		if (BuiltInPersonas.IsKnownSve(input.Name) || SveMods.IsSve(modId ?? ""))
		{
			sb.AppendLine("【来历：Stardew Valley Expanded】" + where);
			sb.AppendLine("这套扩展设定对你来说就是现实，和游戏本来的设定一样作数。你只是在过日子，不做世界观讲解，话走到那儿才说起相关的人和地方。你已经和农夫、和镇上的人发生过的事继续作数。");
			AppendField(sb, "SVE 资料", BuiltInPersonas.SvePersonality(input.Name));
			return sb.ToString();
		}
		if (string.IsNullOrWhiteSpace(modId))
		{
			return "";
		}
		sb.AppendLine($"【来历：其他 mod（{modId}）】");
		sb.AppendLine("游戏本来没有你这个人，不过在这个存档里你就是这个世界的一员。资料之外的亲友和往事没把握就别断言，宁可含糊。每次见面都要让人认得出是你，别今天一个腔调明天一个腔调。");
		return sb.ToString();
	}

	private static void AppendField(StringBuilder sb, string label, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			sb.Append(label).Append("：").AppendLine(value.Trim());
		}
	}

	internal static (string Text, PersonaSource Source) Default(PersonaInput input, ContentPackPersona pack)
	{
		string name = input.Name;
		// 查资料用内部名；写给 AI 看的用显示名（mod 村民的内部名常带"作者.模组_"前缀）。
		string shown = string.IsNullOrWhiteSpace(input.DisplayName) ? name : input.DisplayName!;
		if (pack.Managed)
		{
			string baseline = pack.Personality.Length > 0 ? pack.Personality : $"身份：{shown}，由内容包加入山谷的原创居民。资料只有这些，其余凭常识补全，别编造重大经历。";
			return (Compose(baseline, pack.Appearance, pack.Lore, "原创角色背景", pack.SocialTags, pack.Credit), PersonaSource.ContentPack);
		}
		if (BuiltInPersonas.VanillaPersonality(name) is string vanilla)
		{
			return (vanilla, PersonaSource.BuiltIn);
		}
		if (BuiltInPersonas.SvePersonality(name) is string sve)
		{
			return (sve, PersonaSource.Sve);
		}
		return (Generated(shown, input.Traits), PersonaSource.Generated);
	}

	/// <summary>没有任何人设资料的 mod 村民：按游戏里的性格数值描一个大概。</summary>
	internal static string Generated(string name, NpcTraits? traits)
	{
		if (traits == null)
		{
			return $"身份：{name}，来自别的 mod 的居民，没有更多资料。拿不准的事别说死。";
		}
		string age = traits.Age switch { 0 => "大人", 1 => "年轻人", 2 => "小孩", _ => "居民" };
		string manner = traits.Manner switch { 0 => "说话冲、不拐弯", 1 => "有礼貌、热心", 2 => "拘谨、小心", _ => "分寸适中" };
		string social = traits.SocialAnxiety switch { 0 => "不怕生", 1 => "社交上中规中矩", 2 => "怕生", _ => "看场合" };
		string mood = traits.Optimism switch { 0 => "务实、带点怀疑", 1 => "心态平稳", 2 => "乐观", _ => "情绪细腻" };
		return $"身份：{name}，来自别的 mod 的{age}。为人（按游戏里的性格数值）：{manner}，{social}，{mood}。语气每天保持一致。";
	}

	private static string Pick(string preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

	private static string Compose(string baseline, string appearance, string lore, string loreLabel, string tags, string credit)
	{
		StringBuilder sb = new StringBuilder(baseline.Trim());
		Append(sb, "外貌", appearance);
		Append(sb, loreLabel, lore);
		Append(sb, "社交标签", tags);
		Append(sb, "作者", credit);
		return sb.ToString();
	}

	private static string Supplement(string baseline, CharacterCard card)
	{
		StringBuilder sb = new StringBuilder(baseline.Trim());
		Append(sb, "外貌", card.Appearance);
		Append(sb, "补充背景", card.Lore);
		Append(sb, "社交标签", card.SocialTags);
		return sb.ToString();
	}

	private static void Append(StringBuilder sb, string label, string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			sb.Append(' ').Append(label).Append('：').Append(value.Trim().TrimEnd('。', '.')).Append('。');
		}
	}

	/// <summary>第一年肯特不在镇上：除了他的家人，别人不会提到他。</summary>
	public static bool IsInTown(string name, int year) => !string.Equals(name, "Kent", StringComparison.OrdinalIgnoreCase) || year >= 2;

	public static bool CanMention(string speaker, string target, int year) => IsInTown(target, year)
		|| (string.Equals(target, "Kent", StringComparison.OrdinalIgnoreCase) && speaker.ToLowerInvariant() is "jodi" or "sam" or "vincent");
}

/// <summary>内容包写在 Data/Characters 的 CustomFields 里的人设。</summary>
internal sealed record ContentPackPersona(bool Managed, string Personality, string Appearance, string Lore, string SocialTags, string Credit)
{
	public static ContentPackPersona Read(IReadOnlyDictionary<string, string>? fields)
	{
		if (fields == null || fields.Count == 0)
		{
			return new ContentPackPersona(false, "", "", "", "", "");
		}
		string managed = Field(fields, "Managed");
		return new ContentPackPersona(
			managed.Equals("true", StringComparison.OrdinalIgnoreCase) || managed.Equals("yes", StringComparison.OrdinalIgnoreCase) || managed == "1",
			Field(fields, "Personality"),
			Field(fields, "Appearance"),
			Field(fields, "Lore"),
			Field(fields, "SocialTags"),
			Field(fields, "Credit"));
	}

	private static string Field(IReadOnlyDictionary<string, string> fields, string key) =>
		fields.TryGetValue(PersonaResolver.OwnPrefix + key, out string? v) ? v?.Trim() ?? "" : "";
}
