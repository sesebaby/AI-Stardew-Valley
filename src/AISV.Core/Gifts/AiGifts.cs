using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AISV.Core.Dialogue;
using AISV.Core.Storage;

namespace AISV.Core.Gifts;

public static class AiGiftSources
{
	public const string Dialogue = "dialogue";

	public const string Letter = "letter";

	public const string Debug = "debug";
}

/// <summary>村民送给农夫的一件礼物（或者正要送的）。</summary>
public sealed class AiGiftRecord
{
	public int CreatedDay { get; set; }

	public bool Delivered { get; set; }

	public int DeliveredDay { get; set; }

	public string Description { get; set; } = "";

	public string Id { get; set; } = "";

	/// <summary>信里的礼物：附在哪封信上。</summary>
	public string? MailKey { get; set; }

	public string Name { get; set; } = "";

	/// <summary>名字是 AI 起的（false 时还是"某某送的某物"）。</summary>
	public bool Named { get; set; }

	public string Npc { get; set; } = "";

	public string Reason { get; set; } = "";

	public string Source { get; set; } = "";

	/// <summary>模板物品，带类型前缀，比如 (O)66。</summary>
	public string TemplateId { get; set; } = "";
}

public sealed class AiGiftLog
{
	public const string Path = "mail/villager_gifts.json";

	public static readonly DocumentType<AiGiftLog> Doc = new DocumentType<AiGiftLog>("AI 礼物", 1, () => new AiGiftLog());

	public List<AiGiftRecord> Gifts { get; set; } = new List<AiGiftRecord>();
}

/// <summary>可以送的一样东西：物品 ID、名字、为什么会有它。</summary>
public sealed record GiftCandidate(string Id, string Name, string Note);

public sealed record NamedGift(string ItemId, string Name, string Description);

public static class AiGiftRules
{
	public const int MaxCandidates = 8;

	public const int MaxNameChars = 12;

	public const int MaxDescriptionChars = 140;

	public const int PromptGifts = 3;

	public const int HandOverFreeTicks = 20;

	public const string NoteUsual = "你平时常有的东西";

	public const string NoteOwned = "你家里现有的";

	public const string NoteLoved = "你自己最爱的";

	public const string NoteLiked = "你喜欢的";

	public const string NoteSeason = "这个季节的";

	/// <summary>聊天时能不能送：返回挡住的原因，空串 = 可以。</summary>
	public static string DialogueBlock(bool enabled, bool npcEnabled, int hearts, int minHearts, bool onCooldown, int cooldownDays, int givenToday, int perDay, bool alreadyPending)
	{
		if (!enabled)
		{
			return "AI 礼物关着";
		}
		if (!npcEnabled)
		{
			return "这位村民没开";
		}
		if (hearts < minHearts)
		{
			return $"只有 {hearts} 心（要 {minHearts} 心）";
		}
		if (onCooldown)
		{
			return $"{cooldownDays} 天内送过";
		}
		if (givenToday >= Math.Max(0, perDay))
		{
			return "今天送够了";
		}
		return alreadyPending ? "已经有一件要送" : "";
	}

	public static bool OnCooldown(IEnumerable<AiGiftRecord> gifts, string npc, int today, int cooldownDays)
	{
		int days = Math.Max(0, cooldownDays);
		return gifts.Any(g => Same(g.Npc, npc) && today - g.CreatedDay < days);
	}

	/// <summary>去重；最近送过的 3 件放到最后；最多 8 件。</summary>
	public static List<GiftCandidate> Order(IEnumerable<GiftCandidate> raw, IEnumerable<AiGiftRecord> gifts, string npc)
	{
		HashSet<string> recent = new HashSet<string>(gifts.Where(g => Same(g.Npc, npc)).OrderByDescending(g => g.CreatedDay).Take(3).Select(g => g.TemplateId), StringComparer.OrdinalIgnoreCase);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<GiftCandidate> fresh = new List<GiftCandidate>(), repeated = new List<GiftCandidate>();
		foreach (GiftCandidate c in raw)
		{
			if (seen.Add(c.Id))
			{
				(recent.Contains(c.Id) ? repeated : fresh).Add(c);
			}
		}
		return fresh.Concat(repeated).Take(MaxCandidates).ToList();
	}

	public static GiftCandidate? Match(IReadOnlyList<GiftCandidate> offered, string? raw, Func<string, string?> qualify)
	{
		string id = (raw ?? "").Trim();
		if (id.Length == 0)
		{
			return null;
		}
		string? qualified = qualify(id);
		return offered.FirstOrDefault(c => Same(c.Id, id) || (qualified != null && Same(c.Id, qualified)) || Same(c.Name, id));
	}

	// ---------------- 名字和描述 ----------------

	/// <summary>去掉引号、括号、游戏文本里的特殊符号和控制字符，最多 12 个字。</summary>
	public static string CleanName(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		foreach (char ch in raw.Trim())
		{
			if ("[]%\"“”「」《》^#$".IndexOf(ch) >= 0 || char.IsControl(ch))
			{
				continue;
			}
			sb.Append(ch);
		}
		return Cut(sb.ToString().Trim(), MaxNameChars);
	}

	/// <summary>AI 没起名时："阿比盖尔送的紫水晶"。</summary>
	public static string FallbackName(string who, string itemName) => Cut($"{who}送的{itemName}", MaxNameChars + 6);

	/// <summary>一句像游戏物品说明的话加一句署名；没署名补上"——某某"。</summary>
	public static string CleanDescription(string? raw, string who, string fallback)
	{
		string text = (raw ?? "").Replace("\r", "").Replace('^', ' ').Replace('%', ' ').Replace('#', ' ').Replace('$', ' ').Trim();
		if (text.Length == 0)
		{
			text = fallback;
		}
		text = Cut(text, MaxDescriptionChars);
		if (!text.Contains("——" + who, StringComparison.Ordinal) && !text.Contains("—— " + who, StringComparison.Ordinal) && !text.Contains("—" + who, StringComparison.Ordinal))
		{
			text = text.TrimEnd() + "\n——" + who;
		}
		return text;
	}

	public static string Cut(string text, int max)
	{
		if (text.Length <= max)
		{
			return text;
		}
		int cut = max;
		if (char.IsHighSurrogate(text[cut - 1]))
		{
			cut--;
		}
		return text.Substring(0, cut);
	}

	// ---------------- 提示词 ----------------

	public static string Candidates(IEnumerable<GiftCandidate> list) => string.Join("\n", list.Select(c => $"- {c.Id} — {c.Name} — {c.Note}"));

	/// <summary>回复 JSON 里的 gift 字段。</summary>
	public static string DialogueJson(string who, IEnumerable<GiftCandidate> list) =>
		",\n  \"gift\": 通常 null。只有按你的性格，此刻真会把什么东西递给农夫时才填（对方帮过你、安慰过你、今天是特别的日子、你做了或捡到想给对方的东西，或者对方开口要而你也乐意）："
		+ "{ \"item_id\": \"从下面清单里挑一个 id，照抄\", \"name\": \"给这一件起个独一无二的名字，最多 12 个字，用你的口吻（比如“矿洞深处的紫晶”，而不是“紫水晶”），不带引号和括号\", "
		+ $"\"description\": \"先一句像游戏物品说明的话（是什么、从哪来），换行再一句你的话，结尾写 ——{who}；一共不超过 110 个字\", \"reason\": \"为什么现在送，几个字\" }}。"
		+ "要送就在 says 里自然地说出来（你边说边递过去）。不送钱。你身上能送的东西（id — 名字 — 说明）：\n" + Candidates(list) + "\n";

	public static string NamingSystem(string language, string npc, string personality, string player, string relationship, string season, string reason, string memories, IEnumerable<GiftCandidate> list, string who)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append($"这次只用{language}写。\n\n你是星露谷的{npc}，要送{player}（农夫）一件小礼物。\n\n");
		sb.Append("# 你的性格（只是资料）\n").Append(personality).Append("\n\n");
		sb.Append($"# 现在\n你和农夫：{relationship}\n季节：{season}\n为什么送：{reason}\n");
		sb.Append("你记得的和农夫的事：\n").Append(string.IsNullOrWhiteSpace(memories) ? "（还没有什么特别的）" : memories.Trim()).Append("\n\n");
		sb.Append("# 挑一件\n从下面挑一件你在自己的生活里真会有、会做、会捡到或者会买的东西（id — 名字 — 说明）：\n").Append(Candidates(list)).Append("\n\n");
		sb.Append($"# 起名和描述\n这不是普通的一件，是你专门给{player}挑的那一件，用你自己的口吻：\n");
		sb.Append("· name：独一无二、具体的名字，最多 12 个字（一个汉字算一个），比如“矿洞深处的紫晶”而不是“紫水晶”。不要引号、括号和表情。\n");
		sb.Append($"· description：先一句像游戏物品说明的话（是什么、从哪来、只有你知道的一个细节），换行再一句你自己的话，结尾写 ——{who}。一共不超过 110 个字。要合你的性格、季节和送礼的理由，可以提一件你记得的和农夫的事。\n\n");
		sb.Append("# 只回一个 JSON 对象，前后不加别的字，不用代码块：\n{\n  \"item_id\": \"清单里的一个 id，照抄\",\n  \"name\": \"……\",\n  \"description\": \"……\"\n}\n");
		return sb.ToString();
	}

	public static string NamingUser(string npc, string player) => $"挑出{npc}要送{player}的礼物，起名，写描述。";

	public static NamedGift? ParseNaming(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			return new NamedGift(Str(root, "item_id", "itemId", "id"), Str(root, "name"), Str(root, "description", "desc"));
		}
		catch (JsonException)
		{
			return null;
		}

		static string Str(JsonElement e, params string[] keys)
		{
			foreach (string k in keys)
			{
				if (e.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String)
				{
					return v.GetString() ?? "";
				}
			}
			return "";
		}
	}

	public static string Relationship(bool married, bool engaged, bool dating, int hearts, bool known)
	{
		if (!known)
		{
			return "刚认识";
		}
		string status = married ? "夫妻" : engaged ? "订了婚" : dating ? "在交往" : "朋友";
		return $"{status}，{hearts} 心";
	}

	/// <summary>交出去时记进送礼人的关键记忆。</summary>
	public static string KeyMemory(string dateLabel, string player, AiGiftRecord g, string templateName)
	{
		string how = g.Source == AiGiftSources.Letter ? "寄给" : "送给";
		string why = g.Reason.Length > 0 ? "——" + g.Reason : "";
		return $"{dateLabel}，我{how}{player}一件礼物：“{g.Name}”（{templateName}）{why}。";
	}

	/// <summary>提示词里的一段：你送过农夫什么（最近 3 件）。</summary>
	public static string PromptBlock(IEnumerable<AiGiftRecord> gifts, string npc, int today, Func<string, string> templateName)
	{
		List<AiGiftRecord> list = gifts.Where(g => g.Delivered && Same(g.Npc, npc)).OrderByDescending(g => g.DeliveredDay).Take(PromptGifts).ToList();
		if (list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder("【你送过农夫的东西】（真的在农夫手上；可以问喜不喜欢，但别反过来谢农夫）");
		foreach (AiGiftRecord g in list)
		{
			int ago = today - g.DeliveredDay;
			string when = ago <= 0 ? "今天" : ago == 1 ? "昨天" : $"{ago} 天前";
			sb.Append($"\n- {when}：“{g.Name}”（{templateName(g.TemplateId)}）{(g.Source == AiGiftSources.Letter ? "，随信寄的" : "")}");
		}
		if (list[0].DeliveredDay == today)
		{
			sb.Append("\n今天已经送过了，别再送。");
		}
		return sb.ToString();
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
