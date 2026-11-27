using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AISV.Core.Consequences;

/// <summary>会有后果的几种事。</summary>
public static class Misdeeds
{
	/// <summary>店里卖赃物被认出。</summary>
	public const string Fence = "fenced_goods";

	/// <summary>上门理论时动手。</summary>
	public const string Brawl = "brawl";

	/// <summary>约好了没去。</summary>
	public const string MissedMeetup = "missed_meetup";

	/// <summary>答应的托付、请求到期没办。</summary>
	public const string FavorExpired = "favor_expired";

	/// <summary>借的钱、赊的账到期没还。</summary>
	public const string DebtOverdue = "debt_overdue";

	/// <summary>偷东西当场被抓（Stardew Thief）。</summary>
	public const string TheftCaught = "theft_caught";

	/// <summary>翻别人家的垃圾桶被看见。</summary>
	public const string Trash = "trash";

	/// <summary>当着恋人、配偶的面和别人调情。</summary>
	public const string Flirt = "flirt";

	public static readonly IReadOnlyList<string> All = new[] { Fence, Brawl, MissedMeetup, FavorExpired, DebtOverdue, TheftCaught, Trash, Flirt };
}

/// <summary>罚款怎么算：value = 货值 × 倍数；wallet = 身上钱的百分比；都限在 min～max 之间，再乘这一档的倍数。</summary>
public sealed class FineRule
{
	public string Basis { get; set; } = "value";

	public double Rate { get; set; } = 5;

	public int Min { get; set; }

	public int Max { get; set; }

	public double Multiplier { get; set; } = 1;
}

/// <summary>
/// 同一季第 N 次的后果。全是"另外再加多少"：已经在别处扣过的好感、心情这里不算（见 <see cref="MisdeedSpec.Keeps"/>）。
/// </summary>
public sealed class MisdeedTier
{
	/// <summary>当事村民好感另扣多少（正数，扣掉）。</summary>
	public int Friendship { get; set; }

	/// <summary>好感按设置里"被当场抓住掉的好感"来扣（再乘反应方式的倍数）。</summary>
	public bool FriendshipFromSetting { get; set; }

	/// <summary>好感再乘多少（第 2 次起更重）。</summary>
	public double FriendshipScale { get; set; } = 1;

	/// <summary>失主（不是当场认出的人时）扣当事人的几成。</summary>
	public double OwnerShare { get; set; }

	/// <summary>当事村民心情另掉多少（正数）；moodByStyle 时按反应方式。</summary>
	public int Mood { get; set; }

	public bool MoodByStyle { get; set; }

	/// <summary>记进敌意（开了敌意系统时）。</summary>
	public int Enmity { get; set; }

	public FineRule? Fine { get; set; }

	public int ClosedDays { get; set; }

	public bool Arrest { get; set; }

	/// <summary>镇长或当事人来信。</summary>
	public bool Letter { get; set; }

	/// <summary>传成八卦。</summary>
	public bool Gossip { get; set; }

	/// <summary>记成一条负面记忆（说话不算话）。</summary>
	public bool Memory { get; set; }

	/// <summary>之后这么多天不肯再借你钱、再和你约（看是哪种事）。</summary>
	public int CooldownDays { get; set; }

	/// <summary>屏幕上提示一句。</summary>
	public bool Hud { get; set; }
}

public sealed class MisdeedSpec
{
	/// <summary>"看到听到"里对应的类型（有的话由它管之后找上门、旁人取笑）。</summary>
	public string? Awareness { get; set; }

	/// <summary>这件事原本就有的后果（说明用，不重复扣）。</summary>
	public List<string> Keeps { get; set; } = new List<string>();

	public List<MisdeedTier> Tiers { get; set; } = new List<MisdeedTier>();

	/// <summary>第 N 次（从 1 开始）用哪一档，超过就用最后一档。</summary>
	public MisdeedTier Tier(int strike) => Tiers.Count == 0 ? new MisdeedTier() : Tiers[Math.Clamp(strike, 1, Tiers.Count) - 1];
}

/// <summary>后果目录（assets/consequences/catalog.json）：每种事、每一档的轻重。文字在代码里（走多语言）。</summary>
public sealed class MisdeedCatalog
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public Dictionary<string, MisdeedSpec> Kinds { get; set; } = new Dictionary<string, MisdeedSpec>(StringComparer.OrdinalIgnoreCase);

	[JsonIgnore]
	public List<string> Problems { get; } = new List<string>();

	public static MisdeedCatalog Empty => new MisdeedCatalog();

	public MisdeedSpec? Get(string kind) => Kinds.TryGetValue(kind, out MisdeedSpec? s) ? s : null;

	public static MisdeedCatalog Parse(string json)
	{
		MisdeedCatalog c = JsonSerializer.Deserialize<MisdeedCatalog>(json, Options) ?? new MisdeedCatalog();
		c.Kinds = new Dictionary<string, MisdeedSpec>(c.Kinds, StringComparer.OrdinalIgnoreCase);
		foreach (string kind in Misdeeds.All.Where(k => !c.Kinds.ContainsKey(k)))
		{
			c.Problems.Add($"缺少 {kind}。");
		}
		foreach ((string kind, MisdeedSpec spec) in c.Kinds)
		{
			if (!Misdeeds.All.Contains(kind))
			{
				c.Problems.Add($"不认识的种类 {kind}。");
			}
			if (spec.Tiers.Count == 0)
			{
				c.Problems.Add($"{kind} 没有写任何一档。");
			}
			foreach (MisdeedTier t in spec.Tiers.Where(t => t.Fine != null && t.Fine.Basis is not ("value" or "wallet")))
			{
				c.Problems.Add($"{kind} 的罚款依据只能是 value 或 wallet。");
			}
		}
		return c;
	}
}
