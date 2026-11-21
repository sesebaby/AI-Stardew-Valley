using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Store;

/// <summary>
/// 一笔成交。我们自己的农夫小超市和 Farmer's Store（COS）都往这里报，后面的功能（村民账本、聊天话题、八卦、
/// 市场竞争、认出赃物）只认这条记录、不管是哪家店卖的。以后用自己的店慢慢替代 COS 时，这些功能不用改。
/// </summary>
public sealed class StoreSale
{
	public const string Ours = "aisv";

	public const string Cos = "cos";

	/// <summary>哪家店：aisv / cos。</summary>
	public string Source { get; set; } = Ours;

	/// <summary>同一笔不会记两次（读档后也一样）：我们的店用"天数-序号"，COS 用它自己的结账事件 id。</summary>
	public string TxId { get; set; } = "";

	public string Buyer { get; set; } = "";

	public string ItemId { get; set; } = "";

	public string ItemName { get; set; } = "";

	public int Qty { get; set; } = 1;

	public int Quality { get; set; }

	public int UnitPrice { get; set; }

	/// <summary>这件东西原本值多少（单价，商店收购价的两倍左右，用来算"便宜得可疑"）。</summary>
	public int MarketPrice { get; set; }

	public bool Stolen { get; set; }

	public string StolenFrom { get; set; } = "";

	/// <summary>偷来的那天（1 起的总天数；不知道为 0）。</summary>
	public int StolenDay { get; set; }

	public int Day { get; set; }

	public int Time { get; set; }

	/// <summary>认出是赃物、当场被拿回去了（不算营业额）。</summary>
	public bool Reclaimed { get; set; }

	public int Total => Reclaimed ? 0 : UnitPrice * Math.Max(1, Qty);
}

/// <summary>成交记录（store/sales.json，最多留 600 笔），按 TxId 去重。</summary>
public sealed class StoreSaleBook
{
	public const string Path = "store/sales.json";

	public const int MaxSales = 600;

	public static readonly DocumentType<List<StoreSale>> Doc = new DocumentType<List<StoreSale>>("店里的成交", 1, () => new List<StoreSale>());

	private readonly Func<DocumentStore?> _store;

	public StoreSaleBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>新记下一笔时通知（重复的不通知）。</summary>
	public event Action<StoreSale>? Recorded;

	public IReadOnlyList<StoreSale> All => (IReadOnlyList<StoreSale>?)_store()?.Get(Path, Doc) ?? Array.Empty<StoreSale>();

	public bool Record(StoreSale sale)
	{
		DocumentStore? store = _store();
		List<StoreSale>? list = store?.Get(Path, Doc);
		if (store == null || list == null || string.IsNullOrWhiteSpace(sale.TxId) || list.Any(s => s.TxId == sale.TxId && s.Source == sale.Source))
		{
			return false;
		}
		list.Add(sale);
		if (list.Count > MaxSales)
		{
			list.RemoveRange(0, list.Count - MaxSales);
		}
		store.MarkDirty(Path);
		Recorded?.Invoke(sale);
		return true;
	}

	/// <summary>改了某一笔（比如认出赃物后标记拿回去了）。</summary>
	public void Touch() => _store()?.MarkDirty(Path);

	public IEnumerable<StoreSale> On(int day, string? source = null) => All.Where(s => s.Day == day && (source == null || s.Source == source));

	public IEnumerable<StoreSale> By(string buyer, int today, int days) => All.Where(s => string.Equals(s.Buyer, buyer, StringComparison.OrdinalIgnoreCase) && today - s.Day < days);

	/// <summary>最近几天每天的营业额（下标 0 是最早那天）。</summary>
	public int[] Revenue(int today, int days, string source = StoreSale.Ours)
	{
		int[] result = new int[days];
		foreach (StoreSale s in All.Where(s => s.Source == source && today - s.Day < days && s.Day <= today))
		{
			result[days - 1 - (today - s.Day)] += s.Total;
		}
		return result;
	}

	/// <summary>聊天素材：这位村民最近在农夫的店里买过什么（没有为空）。</summary>
	public static string PromptBlock(IEnumerable<StoreSale> recent, string farmer)
	{
		List<string> items = recent.Where(s => !s.Reclaimed).GroupBy(s => s.ItemName).Select(g => g.Key + (g.Sum(s => s.Qty) > 1 ? "×" + g.Sum(s => s.Qty) : "")).Take(5).ToList();
		return items.Count == 0 ? "" : $"最近你在{farmer}开的店里买过：{string.Join("、", items)}。";
	}
}
