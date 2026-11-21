using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AISV.Core.Storage;

namespace AISV.Core.Companions;

/// <summary>配偶一天能做的事。</summary>
public static class SpouseTasks
{
	public const string Water = "water";
	public const string Harvest = "harvest";
	public const string Animals = "animals";
	public const string Produce = "produce";
	public const string Hay = "hay";
	public const string Clear = "clear";
	public const string Greenhouse = "greenhouse";
	public const string Ponds = "ponds";
	public const string Machines = "machines";
	public const string Visit = "visit";
	public const string Shop = "shop";
	public const string MindShop = "mind_shop";
	public const string Kids = "kids";

	public static readonly IReadOnlyList<string> Chores = new[] { Water, Harvest, Animals, Produce, Hay, Clear, Greenhouse, Ponds, Machines };

	public static readonly IReadOnlyList<string> All = Chores.Concat(new[] { Visit, Shop, MindShop, Kids }).ToList();

	public static bool IsChore(string t) => Chores.Contains(t);

	/// <summary>中文名（提示词和日志用；界面上走 i18n）。</summary>
	public static string Label(string t) => t switch
	{
		Water => "浇水",
		Harvest => "收菜",
		Animals => "摸摸动物",
		Produce => "捡蛋和动物产品",
		Hay => "给饲料槽添干草",
		Clear => "清理杂草石头树枝",
		Greenhouse => "打理温室",
		Ponds => "收鱼塘",
		Machines => "收加工机器",
		Visit => "去镇上找人聊天",
		Shop => "去镇上买东西",
		MindShop => "去小超市看店",
		Kids => "带孩子玩",
		_ => t
	};

	/// <summary>每样活一次最多干多少（按勤快程度 0 轻、1 中、2 勤快）。</summary>
	public static int Cap(string t, int level)
	{
		int[] caps = t switch
		{
			Water => new[] { 20, 40, 80 },
			Harvest => new[] { 15, 30, 60 },
			Animals => new[] { 6, 12, 24 },
			Produce => new[] { 8, 16, 40 },
			Hay => new[] { 6, 12, 30 },
			Clear => new[] { 8, 15, 30 },
			Greenhouse => new[] { 30, 60, 120 },
			Ponds => new[] { 3, 6, 12 },
			Machines => new[] { 8, 15, 30 },
			_ => new[] { 1, 1, 1 }
		};
		return caps[Math.Clamp(level, 0, 2)];
	}

	/// <summary>干多久（游戏分钟）。</summary>
	public static int Minutes(string t) => t switch
	{
		Visit => 120,
		Shop => 90,
		MindShop => 240,
		Kids => 90,
		Greenhouse => 60,
		_ => 40
	};
}

/// <summary>排计划时看到的家里情况（Game 层填好）。</summary>
public sealed class SpousePlanInput
{
	public int Hearts { get; init; }

	public bool Roommate { get; init; }

	/// <summary>0 轻、1 中、2 勤快。</summary>
	public int Level { get; init; } = 1;

	public bool Rain { get; init; }

	public bool Festival { get; init; }

	public bool Winter { get; init; }

	/// <summary>原版今天让配偶在露台上待着。</summary>
	public bool PatioDay { get; init; }

	/// <summary>配偶今天心情不好（吵架、冷战等）。</summary>
	public bool Upset { get; init; }

	/// <summary>每样活有多少可干（没有的不排）。</summary>
	public IReadOnlyDictionary<string, int> Work { get; init; } = new Dictionary<string, int>();

	/// <summary>设置里允许的事。</summary>
	public IReadOnlySet<string> Allowed { get; init; } = new HashSet<string>();

	public bool HasKids { get; init; }

	public bool HasShop { get; init; }

	public int Gold { get; init; }

	public int SpendCap { get; init; }

	/// <summary>玩家今天特意要的（一定排上）。</summary>
	public IReadOnlyList<string> Requested { get; init; } = Array.Empty<string>();
}

public sealed class SpousePlanItem
{
	public string Task { get; set; } = "";

	public int Time { get; set; }

	public int Amount { get; set; }

	public bool Requested { get; set; }

	/// <summary>pending / done / skipped</summary>
	public string State { get; set; } = SpousePlanStates.Pending;

	public int Did { get; set; }

	public string Note { get; set; } = "";
}

public static class SpousePlanStates
{
	public const string Pending = "pending";
	public const string Working = "working";
	public const string Done = "done";
	public const string Skipped = "skipped";
}

/// <summary>排配偶一天的事：先按规则挑出候选，AI 可以在候选里挑和排顺序，不能凭空加。</summary>
public static class SpouseDayPlan
{
	public const int FirstSlot = 700;

	public const int LastSlot = 1800;

	/// <summary>一天最多几件事（按勤快程度）；好感低时更少，不到 4 心干脆不帮。</summary>
	public static int MaxItems(int level, int hearts, bool upset)
	{
		if (hearts < 4)
		{
			return 0;
		}
		int n = new[] { 2, 3, 5 }[Math.Clamp(level, 0, 2)];
		if (hearts < 7)
		{
			n--;
		}
		if (upset)
		{
			n = Math.Min(n, 1);
		}
		return Math.Max(0, n);
	}

	/// <summary>能做的事和它有多想做（分越高越先做）。</summary>
	public static List<(string Task, double Score)> Candidates(SpousePlanInput p)
	{
		List<(string, double)> list = new List<(string, double)>();
		if (p.Festival)
		{
			return list;
		}
		foreach (string t in SpouseTasks.Chores)
		{
			if (!p.Allowed.Contains(t) || p.Work.GetValueOrDefault(t) <= 0 || p.PatioDay && t != SpouseTasks.Greenhouse)
			{
				continue;
			}
			if (t == SpouseTasks.Water && p.Rain)
			{
				continue;
			}
			double score = t switch
			{
				SpouseTasks.Water => 5,
				SpouseTasks.Animals => 4,
				SpouseTasks.Produce => 4,
				SpouseTasks.Hay => 4.5,
				SpouseTasks.Harvest => 3.5,
				SpouseTasks.Greenhouse => 3,
				SpouseTasks.Ponds => 2,
				SpouseTasks.Machines => 2,
				_ => 1.5
			};
			// 活越多越该干（封顶 +1.5）。
			score += Math.Min(1.5, p.Work[t] / 20.0);
			list.Add((t, score));
		}
		if (p.Allowed.Contains(SpouseTasks.Kids) && p.HasKids && !p.Rain)
		{
			list.Add((SpouseTasks.Kids, 3.2));
		}
		if (p.Allowed.Contains(SpouseTasks.MindShop) && p.HasShop)
		{
			list.Add((SpouseTasks.MindShop, 2.6));
		}
		if (p.Allowed.Contains(SpouseTasks.Visit) && !p.PatioDay)
		{
			list.Add((SpouseTasks.Visit, p.Rain ? 1.2 : 2.4));
		}
		if (p.Allowed.Contains(SpouseTasks.Shop) && !p.PatioDay && p.SpendCap > 0 && p.Gold >= 30)
		{
			list.Add((SpouseTasks.Shop, 1.8));
		}
		return list.OrderByDescending(x => x.Item2).ToList();
	}

	/// <summary>按规则排：玩家要的先排，再按分数挑，农活放上午，出门和看店放下午。</summary>
	public static List<SpousePlanItem> Build(SpousePlanInput p, IReadOnlyList<string>? order = null)
	{
		List<(string Task, double Score)> cands = Candidates(p);
		int max = MaxItems(p.Level, p.Hearts, p.Upset);
		List<string> picked = new List<string>();
		foreach (string t in p.Requested.Distinct())
		{
			if (SpouseTasks.All.Contains(t) && !p.Festival)
			{
				picked.Add(t);
			}
		}
		IEnumerable<string> pool = order != null
			? order.Where(t => cands.Any(c => c.Task == t)).Concat(cands.Select(c => c.Task))
			: cands.Select(c => c.Task);
		foreach (string t in pool)
		{
			if (picked.Count >= Math.Max(max, p.Requested.Count))
			{
				break;
			}
			// 出门的事一天只排一件，免得一整天不在家。
			if (!picked.Contains(t) && !(IsOuting(t) && picked.Any(IsOuting)))
			{
				picked.Add(t);
			}
		}
		return Schedule(picked, p);
	}

	public static bool IsOuting(string t) => t is SpouseTasks.Visit or SpouseTasks.Shop or SpouseTasks.MindShop;

	private static List<SpousePlanItem> Schedule(List<string> picked, SpousePlanInput p)
	{
		List<SpousePlanItem> items = new List<SpousePlanItem>();
		int morning = FirstSlot;
		int afternoon = 1300;
		foreach (string t in picked.OrderBy(t => SpouseTasks.IsChore(t) ? 0 : t == SpouseTasks.MindShop ? 1 : 2))
		{
			int time;
			if (SpouseTasks.IsChore(t))
			{
				time = morning;
				morning = Clock.Add(morning, SpouseTasks.Minutes(t));
			}
			else if (t == SpouseTasks.MindShop)
			{
				time = Math.Max(morning, 1000);
				afternoon = Math.Max(afternoon, Clock.Add(time, SpouseTasks.Minutes(t)));
			}
			else
			{
				time = Math.Max(afternoon, morning);
				afternoon = Clock.Add(time, SpouseTasks.Minutes(t));
			}
			if (time > LastSlot)
			{
				continue;
			}
			items.Add(new SpousePlanItem
			{
				Task = t,
				Time = time,
				Amount = SpouseTasks.IsChore(t) ? Math.Min(SpouseTasks.Cap(t, p.Level), Math.Max(1, p.Work.GetValueOrDefault(t, SpouseTasks.Cap(t, p.Level)))) : t == SpouseTasks.Shop ? Math.Min(p.SpendCap, p.Gold) : 1,
				Requested = p.Requested.Contains(t)
			});
		}
		return items.OrderBy(i => i.Time).ToList();
	}

	/// <summary>AI 的回答：{"order":["water","kids"],"say":"……"}；order 只认候选里的。</summary>
	public static (List<string> Order, string Say)? ParseAi(string text, IEnumerable<string> candidates)
	{
		HashSet<string> allowed = new HashSet<string>(candidates);
		int a = text.IndexOf('{');
		int b = text.LastIndexOf('}');
		if (a < 0 || b <= a)
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(text.Substring(a, b - a + 1));
			List<string> order = new List<string>();
			if (doc.RootElement.TryGetProperty("order", out JsonElement o) && o.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement e in o.EnumerateArray())
				{
					if (e.ValueKind == JsonValueKind.String && allowed.Contains(e.GetString()!) && !order.Contains(e.GetString()!))
					{
						order.Add(e.GetString()!);
					}
				}
			}
			string say = doc.RootElement.TryGetProperty("say", out JsonElement s) && s.ValueKind == JsonValueKind.String ? (s.GetString() ?? "").Trim() : "";
			if (say.Length > 60)
			{
				say = say.Substring(0, 60);
			}
			return (order, say);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>没有 AI 时说的安排（中文素材，Game 层再翻译）。</summary>
	public static string Describe(IEnumerable<SpousePlanItem> items) => string.Join("，", items.Select(i => $"{Clock.Text(i.Time)} {SpouseTasks.Label(i.Task)}"));
}

/// <summary>配偶每天的安排和结果（family/spouse_day.json）。</summary>
public sealed class SpouseDayLog
{
	public const string Path = "family/spouse_day.json";

	public static readonly DocumentType<SpouseDayLog> Doc = new DocumentType<SpouseDayLog>("配偶的日常安排", 1, () => new SpouseDayLog());

	public int Day { get; set; } = -1;

	public string Spouse { get; set; } = "";

	public string Say { get; set; } = "";

	public bool Announced { get; set; }

	public List<SpousePlanItem> Items { get; set; } = new List<SpousePlanItem>();

	/// <summary>玩家要明天（或今天还来得及时）做的事。</summary>
	public List<string> Requests { get; set; } = new List<string>();

	/// <summary>昨天的汇总（给第二天聊天用）。</summary>
	public string Yesterday { get; set; } = "";

	public bool Summarized { get; set; }

	/// <summary>一天做了什么的一句话（中文素材）。</summary>
	public string Summary()
	{
		List<string> parts = Items.Where(i => i.State == SpousePlanStates.Done).Select(i => SpouseTasks.IsChore(i.Task) && i.Did > 0 ? $"{SpouseTasks.Label(i.Task)}（{i.Did}）" : SpouseTasks.Label(i.Task) + (i.Note.Length > 0 ? "：" + i.Note : "")).ToList();
		return parts.Count == 0 ? "" : string.Join("；", parts);
	}

	public string PromptBlock(string spouse, string farmer)
	{
		if (!string.Equals(spouse, Spouse, StringComparison.OrdinalIgnoreCase))
		{
			return "";
		}
		List<string> lines = new List<string>();
		if (Items.Count > 0)
		{
			lines.Add($"你今天的安排：{SpouseDayPlan.Describe(Items)}。已经做完的：{(Summary() is { Length: > 0 } s ? s : "还没有")}。这些是你自己愿意帮{farmer}做的家务，可以自然地提起。");
		}
		if (Yesterday.Length > 0)
		{
			lines.Add($"你昨天在家里做了：{Yesterday}。");
		}
		return string.Join("\n", lines.Select(l => "- " + l));
	}
}
