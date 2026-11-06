using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Storage;

namespace AISV.Core.Stories;

public sealed class ExpulsionRecord
{
	public const string Arrested = "arrested";

	public const string Exiled = "exiled";

	public const string LeftVillage = "left_village";

	public const string Expelled = "expelled";

	public const string Returned = "returned";

	[JsonPropertyName("back_note")]
	public string BackNote { get; set; } = "";

	[JsonPropertyName("back_on")]
	public int? BackOn { get; set; }

	[JsonPropertyName("closed_shop")]
	public bool ClosedShop { get; set; }

	[JsonPropertyName("kind")]
	public string How { get; set; } = LeftVillage;

	[JsonPropertyName("left_on")]
	public int LeftOn { get; set; }

	[JsonPropertyName("news")]
	public string News { get; set; } = "";

	[JsonPropertyName("phase")]
	public string Phase { get; set; } = Expelled;

	[JsonPropertyName("story_id")]
	public string StoryId { get; set; } = "";

	[JsonPropertyName("who")]
	public string Who { get; set; } = "";

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

public sealed class ExpulsionData
{
	public List<ExpulsionRecord> Records { get; set; } = new List<ExpulsionRecord>();
}

public sealed class ShopDetachData
{
	public string[] Names { get; set; } = Array.Empty<string>();
}

/// <summary>商店脱离（town/unattended_shops.json）：店主不在时店照样开。</summary>
public sealed class ShopDetachBook
{
	public const string Path = "town/unattended_shops.json";

	private static readonly DocumentType<ShopDetachData> Doc = new DocumentType<ShopDetachData>("商店脱离", 1, () => new ShopDetachData());

	private readonly Func<DocumentStore?> _store;

	public ShopDetachBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<string> Names => Data()?.Names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

	public bool IsDetached(string npc) => !string.IsNullOrWhiteSpace(npc) && Names.Contains(npc.Trim(), StringComparer.OrdinalIgnoreCase);

	/// <summary>设成脱离或恢复；没进存档时返回 false。</summary>
	public bool Set(string npc, bool detached)
	{
		ShopDetachData? d = Data();
		if (d == null || string.IsNullOrWhiteSpace(npc))
		{
			return false;
		}
		string name = npc.Trim();
		List<string> names = Names.ToList();
		bool changed = detached ? !names.Contains(name, StringComparer.OrdinalIgnoreCase) : names.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0;
		if (detached && changed)
		{
			names.Add(name);
		}
		if (changed)
		{
			d.Names = names.ToArray();
			_store()?.MarkDirty(Path);
		}
		return true;
	}

	private ShopDetachData? Data() => _store()?.Get(Path, Doc);
}

/// <summary>逐出小镇的记录（town/departures.json）和规则：谁不能被逐出、结局怎么落地、全村都知道的那段话。</summary>
public sealed class ExpulsionBook
{
	public const string Path = "town/departures.json";

	public static readonly IReadOnlyList<string> AlwaysProtected = new[] { "Jas", "Vincent", "Leo" };

	private static readonly DocumentType<ExpulsionData> Doc = new DocumentType<ExpulsionData>("逐出小镇", 1, () => new ExpulsionData());

	private readonly Func<DocumentStore?> _store;

	private readonly ShopDetachBook _shops;

	public ExpulsionBook(Func<DocumentStore?> store, ShopDetachBook shops)
	{
		_store = store;
		_shops = shops;
	}

	public IReadOnlyList<ExpulsionRecord> All => Data()?.Records.Where(r => !string.IsNullOrWhiteSpace(r.Who)).ToList() ?? new List<ExpulsionRecord>();

	public ExpulsionRecord? Get(string npc) => string.IsNullOrWhiteSpace(npc) ? null : All.LastOrDefault(r => Same(r.Who, npc.Trim()));

	public bool IsExpelled(string npc) => Get(npc)?.Phase == ExpulsionRecord.Expelled;

	public IReadOnlyList<string> ExpelledNames => All.Where(r => r.Phase == ExpulsionRecord.Expelled).Select(r => r.Who).ToList();

	public static IReadOnlyCollection<string> ProtectedNames(string? spouse)
	{
		HashSet<string> names = new HashSet<string>(AlwaysProtected, StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(spouse))
		{
			names.Add(spouse.Trim());
		}
		return names;
	}

	public static bool IsProtected(string npc, string? spouse) => string.IsNullOrWhiteSpace(npc) || ProtectedNames(spouse).Contains(npc.Trim());

	public static string NormalizeKind(string? kind) => (kind ?? "").Trim().ToLowerInvariant() switch
	{
		"arrested" or "arrest" or "prison" or "jail" => ExpulsionRecord.Arrested,
		"banished" or "expelled" or "exiled" or "exile" => ExpulsionRecord.Exiled,
		_ => ExpulsionRecord.LeftVillage
	};

	/// <summary>逐出故事线结束：记下这个人离开了（店主的店改成脱离）。返回为什么没做（null = 做了）。</summary>
	public string? Expel(StoryArc arc, int today, string? spouse, Func<string, bool> isShopOwner)
	{
		ExpulsionData? d = Data();
		string who = arc.LeavingNpc?.Trim() ?? "";
		if (d == null)
		{
			return "还没进存档";
		}
		if (who.Length == 0)
		{
			return "没写是谁";
		}
		if (IsProtected(who, spouse))
		{
			return $"{who} 不能被逐出";
		}
		string kind = NormalizeKind(arc.LeavingHow);
		ExpulsionRecord r = new ExpulsionRecord
		{
			Who = who,
			How = kind,
			Why = !string.IsNullOrWhiteSpace(arc.LeavingWhy) ? arc.LeavingWhy.Trim() : Cut(arc.Closure, 200),
			News = Cut(arc.LeavingNews, 300),
			LeftOn = today,
			StoryId = arc.Id,
			Phase = ExpulsionRecord.Expelled
		};
		if (isShopOwner(who) && !_shops.IsDetached(who) && _shops.Set(who, true))
		{
			r.ClosedShop = true;
		}
		d.Records.RemoveAll(x => Same(x.Who, who));
		d.Records.Add(r);
		Dirty();
		arc.Tags ??= new List<string>();
		AddTag(arc.Tags, "expulsion");
		AddTag(arc.Tags, kind);
		AddTag(arc.Tags, "expelled:" + who);
		return null;
	}

	/// <summary>回归故事线结束：这个人回来了（当初因此脱离的店恢复）。返回为什么没做（null = 做了）。</summary>
	public string? Return(StoryArc arc, int today)
	{
		string who = arc.LeavingNpc?.Trim() ?? "";
		if (who.Length == 0)
		{
			return "没写是谁";
		}
		ExpulsionRecord? r = Get(who);
		if (Data() == null || r == null || r.Phase != ExpulsionRecord.Expelled)
		{
			return $"{who} 现在没有被逐出";
		}
		r.Phase = ExpulsionRecord.Returned;
		r.BackOn = today;
		r.BackNote = !string.IsNullOrWhiteSpace(arc.LeavingNews) ? Cut(arc.LeavingNews, 300) : Cut(arc.Closure, 200);
		if (r.ClosedShop)
		{
			_shops.Set(who, false);
		}
		Dirty();
		arc.Tags ??= new List<string>();
		AddTag(arc.Tags, "expulsion_return");
		AddTag(arc.Tags, "returned:" + who);
		return null;
	}

	/// <summary>全村都知道的逐出往事。</summary>
	public string PromptBlock(Func<string, string> display)
	{
		IReadOnlyList<ExpulsionRecord> records = All;
		if (records.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【镇上人尽皆知的去留】");
		foreach (ExpulsionRecord r in records)
		{
			string name = display(r.Who);
			string how = r.How switch
			{
				ExpulsionRecord.Arrested => "被带走",
				ExpulsionRecord.Exiled => "被大家请出了镇子",
				_ => "自己走了"
			};
			string story = !string.IsNullOrWhiteSpace(r.News) ? r.News.Trim().TrimEnd('.', '。')
				: string.IsNullOrWhiteSpace(r.Why) ? $"{how}，为什么走，镇上到今天也没个一致的说法" : $"{how}，起因是{r.Why.Trim().TrimEnd('.', '。')}";
			string now = r.Phase == ExpulsionRecord.Returned
				? $"如今已经回来了{(string.IsNullOrWhiteSpace(r.BackNote) ? "" : "（" + r.BackNote.Trim().TrimEnd('.', '。') + "）")}，不过这事在大家心里还没翻篇"
				: "如今人不在镇上，找不着也见不到";
			sb.AppendLine($"- {name}：{story}；{now}。");
		}
		sb.Append("话题碰到这些事就照实说；没回来的人不会出现在镇上，别说成刚见过他。");
		return sb.ToString();
	}

	private static void AddTag(List<string> tags, string tag)
	{
		if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
		{
			tags.Add(tag);
		}
	}

	private static string Cut(string? text, int max)
	{
		string t = (text ?? "").Trim();
		return t.Length > max ? t[..max] : t;
	}

	private ExpulsionData? Data() => _store()?.Get(Path, Doc);

	private void Dirty() => _store()?.MarkDirty(Path);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
