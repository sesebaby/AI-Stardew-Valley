using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Compat;

/// <summary>偷来的一样东西（只存 ID、数量、品质和单价，显示名是当时的）。</summary>
public sealed class StolenItem
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public int Stack { get; set; } = 1;

	public int Quality { get; set; }

	/// <summary>单价（游戏里的售价）。</summary>
	public int Price { get; set; }
}

/// <summary>一次偷窃：扒窃一位村民，或者溜进一户人家翻东西。</summary>
public sealed class TheftRecord
{
	public const string Pickpocket = "pickpocket";

	public const string Burglary = "burglary";

	public string Id { get; set; } = "";

	public string Kind { get; set; } = Pickpocket;

	/// <summary>1 基的总天数。</summary>
	public int Day { get; set; }

	public int Time { get; set; }

	public string Location { get; set; } = "";

	/// <summary>失主（扒窃是被摸的人，入室是这户的主人）。</summary>
	public List<string> Victims { get; set; } = new List<string>();

	public List<StolenItem> Items { get; set; } = new List<StolenItem>();

	public int Coins { get; set; }

	public bool Caught { get; set; }

	/// <summary>当场抓住的人。</summary>
	public string CaughtBy { get; set; } = "";

	public int Value => Items.Sum(i => Math.Max(0, i.Price) * Math.Max(1, i.Stack)) + Math.Max(0, Coins);
}

/// <summary>偷窃记录（存档里，最多留 200 条；以后的村民账本按这里扣钱扣东西）。</summary>
public sealed class TheftLog
{
	public const string Path = "farmer/pilfering.json";

	public const int MaxRecords = 200;

	public static readonly DocumentType<List<TheftRecord>> Doc = new DocumentType<List<TheftRecord>>("偷窃记录", 1, () => new List<TheftRecord>());

	private readonly Func<DocumentStore?> _store;

	public TheftLog(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<TheftRecord> All => (IReadOnlyList<TheftRecord>?)List() ?? Array.Empty<TheftRecord>();

	public IEnumerable<TheftRecord> On(int day) => All.Where(r => r != null && r.Day == day);

	public void Add(TheftRecord record)
	{
		DocumentStore? store = _store();
		List<TheftRecord>? list = List();
		if (store == null || list == null)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(record.Id))
		{
			record.Id = $"t{record.Day}-{record.Time}-{Guid.NewGuid().ToString("N")[..6]}";
		}
		list.Add(record);
		if (list.Count > MaxRecords)
		{
			list.RemoveRange(0, list.Count - MaxRecords);
		}
		store.MarkDirty(Path);
	}

	private List<TheftRecord>? List()
	{
		List<TheftRecord>? list = _store()?.Get(Path, Doc);
		list?.RemoveAll(r => r == null);
		return list;
	}
}

/// <summary>偷窃相关的文字和数值。</summary>
public static class TheftText
{
	/// <summary>"破旧钱包、苹果 ×3 和 120g"；什么都没有时是空串。</summary>
	public static string Loot(IReadOnlyList<StolenItem> items, int coins)
	{
		List<string> parts = items
			.GroupBy(i => i.Name)
			.Select(g => (Name: g.Key, Count: g.Sum(i => Math.Max(1, i.Stack))))
			.Where(p => !string.IsNullOrWhiteSpace(p.Name))
			.Select(p => p.Count > 1 ? $"{p.Name} ×{p.Count}" : p.Name)
			.ToList();
		if (coins > 0)
		{
			parts.Add($"{coins}g");
		}
		return parts.Count switch
		{
			0 => "",
			1 => parts[0],
			_ => string.Join("、", parts.Take(parts.Count - 1)) + "和" + parts[^1]
		};
	}

	/// <summary>失主没看见是谁时，镇上传开的那句话。</summary>
	public static string LossGossip(TheftRecord r, Func<string, string> display, string placeName)
	{
		string loot = Loot(r.Items, r.Coins);
		string who = string.Join("、", r.Victims.Select(display));
		if (r.Kind == TheftRecord.Burglary)
		{
			return $"{who}{(TheftRules.Shopkeepers.ContainsKey(r.Location) ? "店里" : "家里")}被人翻过，少了{(loot.Length > 0 ? loot : "些东西")}，没人看见是谁干的。";
		}
		return $"{who}在{placeName}一转身，身上的{(loot.Length > 0 ? loot : "东西")}就不见了，到现在也不知道是被谁摸走的。";
	}

	/// <summary>失主发现少了东西，心情往下掉多少（丢得越多越难受）。</summary>
	public static int MoodDrop(int value) => value >= 1000 ? -15 : value >= 200 ? -10 : -6;
}

/// <summary>偷窃的判定规则。</summary>
public static class TheftRules
{
	/// <summary>开店的地方：店主也是失主，排在住户前面。</summary>
	public static readonly IReadOnlyDictionary<string, string> Shopkeepers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["SeedShop"] = "Pierre",
		["Saloon"] = "Gus",
		["Hospital"] = "Harvey",
		["Blacksmith"] = "Clint",
		["FishShop"] = "Willy",
		["AnimalShop"] = "Marnie",
		["ScienceHouse"] = "Robin",
		["ArchaeologyHouse"] = "Gunther",
		["JojaMart"] = "Morris",
		["SandyHouse"] = "Sandy"
	};

	public static List<string> BurglaryVictims(string location, IEnumerable<string> residents)
	{
		List<string> list = new List<string>();
		if (Shopkeepers.TryGetValue(location, out string? keeper))
		{
			list.Add(keeper);
		}
		list.AddRange(residents.Where(r => !string.IsNullOrWhiteSpace(r) && !list.Contains(r, StringComparer.OrdinalIgnoreCase)));
		return list;
	}

	/// <summary>当场被抓时谁掉多少好感：抓到的人掉全额，入室时其他失主掉一半；没人抓到就谁也不掉。</summary>
	public static IReadOnlyList<(string Npc, int Amount)> FriendshipHits(TheftRecord r, int loss)
	{
		List<(string, int)> hits = new List<(string, int)>();
		if (!r.Caught || string.IsNullOrWhiteSpace(r.CaughtBy) || loss <= 0)
		{
			return hits;
		}
		hits.Add((r.CaughtBy, loss));
		if (r.Kind == TheftRecord.Burglary && loss / 2 > 0)
		{
			hits.AddRange(r.Victims.Where(v => !string.Equals(v, r.CaughtBy, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Select(v => (v, loss / 2)));
		}
		return hits;
	}

	/// <summary>值不值得记一笔：入室要么拿到了东西、要么被人撞见；骑马扒窃认不出失手，没拿到东西就当没发生。</summary>
	public static bool WorthRecording(TheftRecord r, bool horse)
	{
		if (r.Kind == TheftRecord.Burglary)
		{
			return r.Items.Count > 0 || r.Coins > 0 || r.Caught && !string.IsNullOrWhiteSpace(r.CaughtBy);
		}
		return !horse || r.Items.Count > 0 || r.Coins > 0;
	}

	/// <summary>失主事后会不会发现少了东西：没被人当场抓住、而且确实少了东西。</summary>
	public static bool LossNoticed(TheftRecord r) => (!r.Caught || string.IsNullOrWhiteSpace(r.CaughtBy)) && (r.Items.Count > 0 || r.Coins > 0);
}
