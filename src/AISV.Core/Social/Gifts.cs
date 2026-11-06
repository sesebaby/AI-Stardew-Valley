using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Storage;

namespace AISV.Core.Social;

/// <summary>农夫送出的一件礼物（farmer/gifts_given.json）。</summary>
public sealed class GiftRecord
{
	[JsonPropertyName("day")]
	public int Day { get; set; }

	/// <summary>loved / liked / neutral / disliked / hated。</summary>
	[JsonPropertyName("liking")]
	public string Liking { get; set; } = "neutral";

	[JsonPropertyName("on_birthday")]
	public bool OnBirthday { get; set; }

	[JsonPropertyName("recipient")]
	public string Recipient { get; set; } = "";

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("what")]
	public string What { get; set; } = "";

	[JsonPropertyName("year")]
	public int Year { get; set; }
}

/// <summary>
/// 送礼记录：每位村民最多留 50 件；今天送的单独记着（一天结束清空）。
/// 对话提示词里放"刚收到的礼物"或"最近收到的 5 件"，同一样东西送了 3 次以上会被注意到。
/// </summary>
public sealed class GiftBook
{
	public const string Path = "farmer/gifts_given.json";

	public const int MaxPerNpc = 50;

	private static readonly DocumentType<Dictionary<string, List<GiftRecord>>> Doc = new DocumentType<Dictionary<string, List<GiftRecord>>>("送礼记录", 1, () => new Dictionary<string, List<GiftRecord>>(StringComparer.OrdinalIgnoreCase));

	private readonly Func<DocumentStore?> _store;

	private readonly Dictionary<string, GiftRecord> _today = new Dictionary<string, GiftRecord>(StringComparer.OrdinalIgnoreCase);

	public GiftBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>记下一件礼物后通知（生日送了最爱的东西会寄感谢信等）。</summary>
	public event Action<GiftRecord>? Recorded;

	public IReadOnlyDictionary<string, GiftRecord> Today => _today;

	public static string Taste(int gameTaste) => gameTaste switch
	{
		0 => "loved",
		2 => "liked",
		4 => "disliked",
		6 => "hated",
		_ => "neutral"
	};

	public GiftRecord Record(string npc, string itemName, string taste, bool isBirthday, GameDate today)
	{
		GiftRecord r = new GiftRecord { Recipient = npc, What = itemName, Liking = taste, OnBirthday = isBirthday, Day = today.Day, Season = today.Season, Year = today.Year };
		List<GiftRecord> list = History(npc, create: true)!;
		list.Add(r);
		while (list.Count > MaxPerNpc)
		{
			list.RemoveAt(0);
		}
		_today[npc] = r;
		_store()?.MarkDirty(Path);
		Recorded?.Invoke(r);
		return r;
	}

	public IReadOnlyList<GiftRecord> For(string npc) => History(npc, create: false) ?? (IReadOnlyList<GiftRecord>)Array.Empty<GiftRecord>();

	/// <summary>所有村民收到的全部礼物。</summary>
	public IEnumerable<GiftRecord> Everything() => _store()?.Get(Path, Doc).Where(p => p.Value != null).SelectMany(p => p.Value.Where(g => g != null).Select(g => string.IsNullOrWhiteSpace(g.Recipient) ? new GiftRecord { Recipient = p.Key, What = g.What, Liking = g.Liking, OnBirthday = g.OnBirthday, Day = g.Day, Season = g.Season, Year = g.Year } : g)) ?? Enumerable.Empty<GiftRecord>();

	public bool EverGaveLoved(string npc) => History(npc, create: false)?.Any(g => g.Liking == "loved") ?? false;

	/// <summary>今天这件礼物值得记成关键记忆吗：生日收到喜欢/最爱的、收到最爱的、收到讨厌的。</summary>
	public string? SignificantMemory(string npc)
	{
		if (!_today.TryGetValue(npc, out GiftRecord? g))
		{
			return null;
		}
		if (g.OnBirthday && g.Liking is "loved" or "liked")
		{
			return $"农夫记得我的生日，还送了{g.What}——我特别喜欢！";
		}
		return g.Liking switch
		{
			"loved" => $"农夫送了我{g.What}，正是我最爱的东西！",
			"hated" => $"农夫送了我{g.What}……我真受不了这个。",
			_ => null
		};
	}

	public string PromptBlock(string npc)
	{
		StringBuilder sb = new StringBuilder();
		if (_today.TryGetValue(npc, out GiftRecord? now))
		{
			sb.AppendLine();
			sb.AppendLine($"【手上这份礼】农夫刚送了你{now.What}（{TasteWord(now.Liking)}）。");
			if (now.OnBirthday)
			{
				sb.AppendLine("今天正好是你的生日，农夫记得，还带了礼物来。");
			}
			sb.AppendLine("就是刚才的事，这句话得提到它。");
		}
		List<GiftRecord>? list = History(npc, create: false);
		if (list is { Count: > 0 })
		{
			if (!_today.ContainsKey(npc))
			{
				sb.AppendLine();
				sb.AppendLine("【农夫这些日子送过的】");
				foreach (GiftRecord g in list.TakeLast(5).Reverse())
				{
					string birthday = g.OnBirthday ? "，生日那天送的" : "";
					sb.AppendLine($"- {g.What}（{TasteWord(g.Liking)}{birthday}；第 {g.Year} 年{HistoryFormatter.SeasonName(g.Season)} {g.Day} 日）");
				}
				sb.AppendLine("话头碰上了提一句就好。");
			}
			foreach (IGrouping<string, GiftRecord> same in list.GroupBy(g => g.What).Where(g => g.Count() >= 3).OrderByDescending(g => g.Count()).Take(3))
			{
				sb.AppendLine($"{same.Key}农夫已经送了 {same.Count()} 回，你都记着。");
			}
		}
		return sb.ToString();
	}

	/// <summary>日记"今天送出的礼物"那一栏。</summary>
	public IEnumerable<string> TodayForDiary(Func<string, string> display, Func<string, bool> enabled) =>
		_today.Values.Where(g => enabled(g.Recipient)).Select(g => $"把{g.What}送给了{display(g.Recipient)}（{TasteWord(g.Liking)}{(g.OnBirthday ? "，正好是对方生日" : "")}）");

	public void EndDay() => _today.Clear();

	public void Reset() => _today.Clear();

	public static string TasteWord(string taste) => taste switch
	{
		"loved" => "最爱的",
		"liked" => "喜欢的",
		"neutral" => "还行",
		"disliked" => "不喜欢的",
		"hated" => "讨厌的",
		_ => "一件礼物"
	};

	private List<GiftRecord>? History(string npc, bool create)
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return create ? new List<GiftRecord>() : null;
		}
		Dictionary<string, List<GiftRecord>> all = store.Get(Path, Doc);
		foreach ((string key, List<GiftRecord> value) in all)
		{
			if (string.Equals(key, npc, StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}
		if (!create)
		{
			return null;
		}
		List<GiftRecord> list = new List<GiftRecord>();
		all[npc] = list;
		return list;
	}
}
