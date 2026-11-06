using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Storage;

namespace AISV.Core.Favors;

/// <summary>村民在聊天里托农夫办的一件事（asks/small_asks.json）。</summary>
public sealed class NpcFavor
{
	[JsonPropertyName("aim")]
	public string Aim { get; set; } = "";

	[JsonPropertyName("ask")]
	public string Ask { get; set; } = "";

	[JsonPropertyName("asked_day")]
	public int AskedDay { get; set; }

	[JsonPropertyName("asked_season")]
	public string AskedSeason { get; set; } = "";

	[JsonPropertyName("asked_year")]
	public int AskedYear { get; set; }

	[JsonPropertyName("asker")]
	public string Asker { get; set; } = "";

	[JsonPropertyName("days_allowed")]
	public int DaysAllowed { get; set; } = FavorBook.DeadlineDays;

	[JsonPropertyName("done_on")]
	public int? DoneOn { get; set; }

	[JsonPropertyName("item_id")]
	public string ItemId { get; set; } = "";

	/// <summary>bring_item / companionship / opinion。</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	/// <summary>pending / fulfilled / expired。</summary>
	[JsonPropertyName("phase")]
	public string Phase { get; set; } = "pending";

	[JsonIgnore]
	public int CreatedTotalDay => new GameDate(AskedDay, AskedSeason, AskedYear).TotalDays;

	[JsonIgnore]
	public int ExpiredTotalDay => CreatedTotalDay + DaysAllowed;

	[JsonIgnore]
	public bool IsPending => Phase == "pending";
}

/// <summary>
/// 村民托的事：只记带一样具体东西的那种，一人同时一件，5 天没办就算过期，过期 30 天后删掉。
/// 送礼送对了就算办成，另补好感。
/// </summary>
public sealed class FavorBook
{
	public const string Path = "asks/small_asks.json";

	public const string BringItem = "bring_item";

	public const int DeadlineDays = 5;

	private const int ThankDays = 7;

	private const int LetDownDays = 3;

	private const int RememberExpiredDays = 14;

	private const int MaxExpiredInPrompt = 2;

	private const int KeepExpiredDays = 30;

	private static readonly DocumentType<List<NpcFavor>> Doc = new DocumentType<List<NpcFavor>>("托付", 1, () => new List<NpcFavor>());

	private readonly Func<DocumentStore?> _store;

	public FavorBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<NpcFavor> All => List() ?? new List<NpcFavor>();

	/// <summary>登记一件托付，返回为什么没记（null 表示记下了）。</summary>
	public string? Register(string npc, string? type, string? description, string? target, string itemId, GameDate today)
	{
		string kind = NormalizeType(type);
		if (kind != BringItem || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(description))
		{
			return "只记带具体东西的托付";
		}
		List<NpcFavor>? list = List();
		if (list == null)
		{
			return "还没进存档";
		}
		if (list.Any(f => f.IsPending && Same(f.Asker, npc)))
		{
			return "已经有一件没办完";
		}
		list.Add(new NpcFavor
		{
			Asker = npc,
			Kind = kind,
			Ask = description.Trim(),
			Aim = target.Trim(),
			ItemId = itemId,
			AskedDay = today.Day,
			AskedSeason = today.Season,
			AskedYear = today.Year
		});
		Dirty();
		return null;
	}

	/// <summary>新的一天：到期的标过期，过期太久的删掉。返回刚过期的。</summary>
	public List<NpcFavor> Expire(int today)
	{
		List<NpcFavor>? list = List();
		if (list == null)
		{
			return new List<NpcFavor>();
		}
		List<NpcFavor> expired = list.Where(f => f.IsPending && today - f.CreatedTotalDay > f.DaysAllowed).ToList();
		expired.ForEach(f => f.Phase = "expired");
		int removed = list.RemoveAll(f => f.Phase == "expired" && today - f.ExpiredTotalDay > KeepExpiredDays);
		if (expired.Count > 0 || removed > 0)
		{
			Dirty();
		}
		return expired;
	}

	/// <summary>送了一件东西，看能不能办成这位村民托的事（挑最对得上的那件）。</summary>
	public NpcFavor? CompleteByGift(string npc, string qualifiedId, string? displayName, string? internalName, int today)
	{
		NpcFavor? best = null;
		int bestScore = 0;
		foreach (NpcFavor f in All.Where(f => f.IsPending && f.Kind == BringItem && !string.IsNullOrWhiteSpace(f.Aim) && Same(f.Asker, npc)))
		{
			int score = string.IsNullOrWhiteSpace(f.ItemId)
				? ItemMatcher.Score(f.Aim, displayName, internalName)
				: string.Equals(f.ItemId, qualifiedId, StringComparison.OrdinalIgnoreCase) ? ItemMatcher.Exact : 0;
			if (score > bestScore)
			{
				bestScore = score;
				best = f;
			}
		}
		if (best != null)
		{
			Fulfil(best, today);
		}
		return best;
	}

	/// <summary>回复以后：陪伴、出主意这两种聊过就算办成。</summary>
	public NpcFavor? CompleteByTalk(string npc, int today)
	{
		NpcFavor? f = All.FirstOrDefault(x => x.IsPending && Same(x.Asker, npc));
		if (f is { Kind: "companionship" or "opinion" })
		{
			Fulfil(f, today);
			return f;
		}
		return null;
	}

	/// <summary>托的事办成了另补的好感：这次送礼加的不足 150 的部分。</summary>
	public static int GiftBonus(int giftGain) => Math.Max(0, 150 - giftGain);

	public IEnumerable<string> OpenDescriptions(string npc) => All.Where(f => f.IsPending && Same(f.Asker, npc) && !string.IsNullOrWhiteSpace(f.Ask)).Select(f => f.Ask.Trim());

	public string PromptBlock(string npc, int today) => BuildPrompt(All, npc, today);

	public static string BuildPrompt(IEnumerable<NpcFavor> favors, string npc, int today)
	{
		List<NpcFavor> mine = favors.Where(f => Same(f.Asker, npc)).ToList();
		HashSet<NpcFavor> recentExpired = mine.Where(f => f.Phase == "expired" && today - f.ExpiredTotalDay <= RememberExpiredDays)
			.OrderByDescending(f => f.ExpiredTotalDay).Take(MaxExpiredInPrompt).ToHashSet();
		List<NpcFavor> shown = mine.Where(f => f.IsPending || recentExpired.Contains(f) || RecentlyDone(f, today)).ToList();
		if (shown.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【你托农夫办的事】");
		foreach (NpcFavor f in shown)
		{
			if (f.Phase == "fulfilled")
			{
				string what = f.Kind == BringItem && !string.IsNullOrWhiteSpace(f.Aim) ? $"，把{f.Aim.Trim()}带来了" : "";
				sb.AppendLine($"- 「{f.Ask}」：农夫已经替你办好（{Ago(today - f.DoneOn!.Value)}{what}）。这件事心里是热乎的，聊到了可以谢一声；它已经了结，不会再拿出来托。");
			}
			else if (f.Phase == "expired" && today - f.ExpiredTotalDay <= LetDownDays)
			{
				sb.AppendLine($"- 「{f.Ask}」：等到期限过了，农夫一直没办。几天前的事，心里那点空落还在，就算今天别的事开心，见到农夫也提不起平常那股劲。至于是闷着、嘟囔一句还是半开玩笑地刺一下，看你的脾气；整场对话里顶多提一次，农夫先说起了或者你已经提过就放下。现在再带来也晚了，用不着再要。气不至于发到伤人，农夫要是道了歉，你就慢慢松开。");
			}
			else if (f.Phase == "expired")
			{
				sb.AppendLine($"- 「{f.Ask}」：很早以前托的，农夫没办成。这页已经翻过去了，话赶话碰上才淡淡带过，不翻旧账，也不会再要。");
			}
			else
			{
				sb.AppendLine($"- 「{f.Ask}」：{HistoryFormatter.SeasonName(f.AskedSeason)} {f.AskedDay} 日托的，还没办。想起来可以顺口问问进展。");
			}
		}
		return sb.ToString();
	}

	public static string NormalizeType(string? type)
	{
		string t = (type ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
		return t switch
		{
			"bring_item" or "bring" or "bring_an_item" or "give_item" or "deliver_item" or "item" or "traer_item" or "traer" or "entregar_item" or "dar_item" or "trazer" or "trazer_item" or "entregar_objeto" or "donner_objet" or "apporter_objet" or "portare_oggetto" or "gegenstand_bringen" => BringItem,
			"companionship" or "companion" or "company" or "spend_time" or "hang_out" or "companhia" or "compania" => "companionship",
			"opinion" or "advice" or "feedback" or "conselho" or "opiniao" => "opinion",
			_ => t
		};
	}

	private static bool RecentlyDone(NpcFavor f, int today) => f.Phase == "fulfilled" && f.DoneOn is int d && today - d is >= 0 and <= ThankDays;

	private static string Ago(int days) => days switch
	{
		0 => "今天",
		1 => "昨天",
		_ => $"{days} 天前"
	};

	private void Fulfil(NpcFavor f, int today)
	{
		f.Phase = "fulfilled";
		f.DoneOn = today;
		Dirty();
	}

	private List<NpcFavor>? List() => _store()?.Get(Path, Doc);

	private void Dirty() => _store()?.MarkDirty(Path);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
