using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Favors;

/// <summary>村民开口要的一样东西，带一条任务和一段完成后的小场景（asks/item_wishes.json）。</summary>
public sealed class ItemRequestFavor
{
	public int Amount { get; set; } = 1;

	public int AskedOn { get; set; }

	public string Asker { get; set; } = "";

	public string Id { get; set; } = "";

	public string JournalId { get; set; } = "";

	/// <summary>0 表示不过期。</summary>
	public int LapsesOn { get; set; }

	public string Mood { get; set; } = "";

	public int PayGold { get; set; }

	/// <summary>none / monetary。</summary>
	public string PayKind { get; set; } = "none";

	/// <summary>active / completed / expired / cancelled。</summary>
	public string Phase { get; set; } = "active";

	public string Plea { get; set; } = "";

	public int SettledOn { get; set; }

	public int StageFrom { get; set; } = 600;

	public string StagePlace { get; set; } = "";

	public bool StagePlayed { get; set; }

	public string StageRef { get; set; } = "";

	public string StageScript { get; set; } = "";

	public int StageTo { get; set; } = 2600;

	/// <summary>当面接过东西时那句道谢（AI 和过场台词一起写的）；空的时候用内置的。</summary>
	public string ThankYou { get; set; } = "";

	public string WantedItemId { get; set; } = "";

	public string WantedItemName { get; set; } = "";

	[JsonIgnore]
	public bool IsActive => string.Equals(Phase, "active", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool IsCompleted => string.Equals(Phase, "completed", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool SceneWaiting => IsCompleted && !StagePlayed && !string.IsNullOrWhiteSpace(StageScript);
}

public sealed class SceneTimeWindow
{
	public int Min { get; set; } = 600;

	public int Max { get; set; } = 2600;
}

/// <summary>完成小场景的模板（assets/item_request_scenes/）。</summary>
public sealed class ItemRequestScene
{
	public string TemplateId { get; set; } = "";

	/// <summary>cozy / date / friendship / funny / slice_of_life。</summary>
	public string SceneType { get; set; } = "";

	public string Title { get; set; } = "";

	public string Location { get; set; } = "";

	public SceneTimeWindow TimeWindow { get; set; } = new SceneTimeWindow();

	public List<string> ItemHints { get; set; } = new List<string>();

	public bool BlockedForChildCodedNpcs { get; set; }

	public string LlmLineInstructions { get; set; } = "";

	/// <summary>事件脚本，占位符：{{FAVOR_ID}} {{NPC}} {{FARMER}} {{ITEM_NAME}} {{NPC_LINE_1}} {{NPC_LINE_2}} {{MEMORY_REFERENCE}}。</summary>
	public string ScriptTemplate { get; set; } = "";
}

/// <summary>AI 挑的方案。</summary>
public sealed class ItemRequestPlan
{
	public string ItemName { get; set; } = "";

	public string Mood { get; set; } = "";

	public int PayGold { get; set; }

	public string PayKind { get; set; } = "none";

	public string Rationale { get; set; } = "";

	public string StageRef { get; set; } = "";
}

/// <summary>定下来、还没开口的请求。</summary>
public sealed class ItemRequestCandidate
{
	public int Amount { get; set; } = 1;

	public string Asker { get; set; } = "";

	public string Id { get; set; } = "";

	public string JournalId { get; set; } = "";

	public int PayGold { get; set; }

	public string PayKind { get; set; } = "none";

	public ItemRequestScene Scene { get; set; } = new ItemRequestScene();

	public string WantedItemId { get; set; } = "";

	public string WantedItemName { get; set; } = "";

	public string PromptBlock()
	{
		string reward = PayKind == "monetary" ? $"给 {PayGold} 金当谢礼" : "没有报酬";
		return "\n【物品请求】\n"
			+ $"今天想开口请农夫带：{WantedItemName} × {Math.Max(1, Amount)}。只提这一样。\n"
			+ $"东西送到以后会有一小段场景：{SceneLabel(Scene.SceneType)}，「{Scene.Title}」，在{Scene.Location}，{Clock(Scene.TimeWindow.Min)}～{Clock(Scene.TimeWindow.Max)}。\n"
			+ $"报酬：{reward}。\n"
			+ (string.IsNullOrWhiteSpace(Scene.LlmLineInstructions) ? "" : $"场景的感觉：{Scene.LlmLineInstructions}\n")
			+ "语气轻松些，像顺嘴一提，用不着郑重其事。\n";
	}

	public static string SceneLabel(string type) => type switch
	{
		"cozy" => "温馨",
		"date" => "约会",
		"friendship" => "朋友之间",
		"funny" => "有点好笑",
		"slice_of_life" => "日常",
		_ => type
	};

	public static string Clock(int time) => $"{time / 100 % 24:00}:{time % 100:00}";
}

/// <summary>场景模板目录：按需读一次。</summary>
public sealed class ItemRequestScenes
{
	public static readonly string[] SceneTypes = { "cozy", "date", "friendship", "funny", "slice_of_life" };

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	private readonly string _directory;

	private readonly Random _random;

	private List<ItemRequestScene>? _scenes;

	public ItemRequestScenes(string directory, Random? random = null)
	{
		_directory = directory;
		_random = random ?? new Random();
	}

	public IReadOnlyList<ItemRequestScene> All => _scenes ??= Load();

	public List<ItemRequestScene> Eligible(int hearts, bool adultPartner, bool child) => All
		.Where(s => !child || !s.BlockedForChildCodedNpcs)
		.Where(s => s.SceneType != "date" || adultPartner)
		.Where(s => s.SceneType != "cozy" || hearts >= 2)
		.Where(s => s.SceneType != "friendship" || hearts >= 1)
		.Where(s => s.ItemHints.Any(ItemHints.Known))
		.ToList();

	public ItemRequestScene? Pick(ItemRequestPlan? plan, int hearts, bool adultPartner, bool child)
	{
		List<ItemRequestScene> eligible = Eligible(hearts, adultPartner, child);
		if (eligible.Count == 0)
		{
			return null;
		}
		if (plan != null)
		{
			ItemRequestScene? exact = eligible.FirstOrDefault(s => !string.IsNullOrWhiteSpace(plan.StageRef) && string.Equals(s.TemplateId, plan.StageRef, StringComparison.OrdinalIgnoreCase));
			if (exact != null)
			{
				return exact;
			}
			string type = NormalizeType(plan.Mood);
			List<ItemRequestScene> sameType = eligible.Where(s => s.SceneType == type).ToList();
			if (sameType.Count > 0)
			{
				return sameType[_random.Next(sameType.Count)];
			}
		}
		return eligible[_random.Next(eligible.Count)];
	}

	public string PickHint(ItemRequestScene scene, string? wanted)
	{
		List<string> hints = scene.ItemHints.Where(ItemHints.Known).ToList();
		if (hints.Count == 0)
		{
			return "";
		}
		string? match = hints.FirstOrDefault(h => string.Equals(h, wanted, StringComparison.OrdinalIgnoreCase));
		return match ?? hints[_random.Next(hints.Count)];
	}

	/// <summary>给 AI 挑方案用的选项：能用的模板和每个模板能要的东西。</summary>
	public string PlanningOptions(int hearts, bool adultPartner, bool child)
	{
		List<ItemRequestScene> eligible = Eligible(hearts, adultPartner, child);
		if (eligible.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"scene_kind 只能是：{string.Join("、", SceneTypes)}。");
		sb.AppendLine("能选的方案（scene_id，以及这个方案能要的 item）：");
		foreach (ItemRequestScene s in eligible.OrderBy(s => s.TemplateId, StringComparer.OrdinalIgnoreCase))
		{
			IEnumerable<string> hints = s.ItemHints.Where(ItemHints.Known).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.OrdinalIgnoreCase);
			sb.AppendLine($"- {s.TemplateId}（{s.SceneType}，「{s.Title}」，在 {s.Location}）：{string.Join("、", hints)}");
		}
		sb.AppendLine("gold 写 0 表示不给钱；要给就在 50 到 500 之间。");
		return sb.ToString();
	}

	public static string NormalizeType(string? value)
	{
		string t = (value ?? "").Trim().ToLowerInvariant();
		return SceneTypes.Contains(t) ? t : "";
	}

	private List<ItemRequestScene> Load()
	{
		List<ItemRequestScene> list = new List<ItemRequestScene>();
		if (!Directory.Exists(_directory))
		{
			return list;
		}
		foreach (string file in Directory.EnumerateFiles(_directory, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				ItemRequestScene? s = JsonSerializer.Deserialize<ItemRequestScene>(File.ReadAllText(file), Json);
				if (s != null && Validate(s))
				{
					list.Add(s);
				}
			}
			catch (Exception)
			{
			}
		}
		return list;
	}

	private static bool Validate(ItemRequestScene s)
	{
		s.TemplateId = s.TemplateId.Trim();
		s.SceneType = NormalizeType(s.SceneType);
		s.Location = s.Location.Trim();
		s.Title = s.Title.Trim();
		s.LlmLineInstructions = s.LlmLineInstructions.Trim();
		s.ItemHints = s.ItemHints.Select(h => h?.Trim() ?? "").Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		s.TimeWindow.Min = Math.Clamp(s.TimeWindow.Min, 600, 2600);
		s.TimeWindow.Max = Math.Clamp(s.TimeWindow.Max, s.TimeWindow.Min, 2600);
		return s.TemplateId.Length > 0 && s.SceneType.Length > 0 && s.Location.Length > 0 && !string.IsNullOrWhiteSpace(s.ScriptTemplate) && s.ItemHints.Count > 0;
	}
}

/// <summary>物品请求能要的东西：提示词里用的英文名 → 物品 ID。</summary>
public static class ItemHints
{
	private static readonly Dictionary<string, string> Ids = new (string Item, int Id)[]
	{
		("Topaz", 68), ("Earth Crystal", 86), ("Iron Ore", 380), ("Coal", 382), ("Clay", 330), ("Hardwood", 709), ("Seaweed", 152),
		("Sweet Pea", 402), ("Blue Jazz", 597), ("Dandelion", 22), ("Spring Onion", 399), ("Radish", 264), ("Potato", 192), ("Cactus Fruit", 90), ("Brown Egg", 180),
		("Goat Cheese", 426), ("Pancakes", 211), ("Hashbrowns", 210), ("Coleslaw", 648), ("Fried Mushroom", 205), ("Field Snack", 403),
		("Maple Bar", 731), ("Pink Cake", 221), ("Green Tea", 614), ("Juice", 350), ("Pale Ale", 303)
	}.ToDictionary(p => p.Item, p => p.Id.ToString(), StringComparer.OrdinalIgnoreCase);

	public static IEnumerable<string> All => Ids.Keys;

	public static bool Known(string hint) => Ids.ContainsKey(hint);

	public static string? QualifiedId(string hint) => Ids.TryGetValue(hint, out string? id) ? "(O)" + id : null;
}

/// <summary>
/// 物品请求的账：一人同时一件，完成后冷却几天才会再开口；不过期。
/// 完成以后的小场景在场景地点、时间窗口里播一次。
/// </summary>
public sealed class ItemRequestBook
{
	public const string Path = "asks/item_wishes.json";

	public const string SceneArmed = "AisvItemRequestSceneArmed";

	public const string SceneTime = "AisvItemRequestSceneTime";

	public const string SceneComplete = "AisvItemRequestSceneComplete";

	private static readonly DocumentType<List<ItemRequestFavor>> Doc = new DocumentType<List<ItemRequestFavor>>("物品请求", 1, () => new List<ItemRequestFavor>());

	private readonly Func<DocumentStore?> _store;

	private readonly Func<int> _cooldownDays;

	private readonly object _sync = new object();

	public ItemRequestBook(Func<DocumentStore?> store, Func<int> cooldownDays)
	{
		_store = store;
		_cooldownDays = cooldownDays;
	}

	/// <summary>快照：开场白在后台线程读，登记在主线程写。</summary>
	public IReadOnlyList<ItemRequestFavor> All
	{
		get
		{
			List<ItemRequestFavor>? list = List();
			if (list == null)
			{
				return new List<ItemRequestFavor>();
			}
			lock (_sync)
			{
				return list.ToList();
			}
		}
	}

	public IEnumerable<ItemRequestFavor> For(string npc) => All.Where(f => Same(f.Asker, npc) && !string.Equals(f.Phase, "candidate", StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.AskedOn);

	public bool HasActive(string npc) => All.Any(f => f.IsActive && Same(f.Asker, npc));

	public bool InCooldown(string npc, int today)
	{
		int days = _cooldownDays();
		return days > 0 && All.Any(f => f.IsCompleted && Same(f.Asker, npc) && today - f.SettledOn < days);
	}

	public bool CanAsk(string npc, int today) => !string.IsNullOrWhiteSpace(npc) && !HasActive(npc) && !InCooldown(npc, today);

	public ItemRequestCandidate? Candidate(ItemRequestScenes scenes, string npc, int hearts, bool adultPartner, bool child, ItemRequestPlan? plan, string saveId, int today, Func<string, string, string> displayName)
	{
		if (!CanAsk(npc, today))
		{
			return null;
		}
		ItemRequestScene? scene = scenes.Pick(plan, hearts, adultPartner, child);
		if (scene == null)
		{
			return null;
		}
		string hint = scenes.PickHint(scene, plan?.ItemName);
		string? itemId = ItemHints.QualifiedId(hint);
		if (itemId == null)
		{
			return null;
		}
		string id = $"airq-{Truncate(SafeId(npc), 18)}-{today}-{Guid.NewGuid().ToString("N")[..8]}";
		bool monetary = string.Equals(plan?.PayKind, "monetary", StringComparison.OrdinalIgnoreCase);
		return new ItemRequestCandidate
		{
			Id = id,
			JournalId = $"Aisv.ItemRequest.{SafeId(saveId)}.{SafeId(npc)}.{today}.{id[^6..]}",
			Asker = npc,
			WantedItemId = itemId,
			WantedItemName = displayName(itemId, hint),
			Scene = scene,
			PayKind = monetary ? "monetary" : "none",
			PayGold = monetary ? Math.Clamp(plan!.PayGold, 50, 500) : 0
		};
	}

	public ItemRequestFavor? Activate(ItemRequestCandidate c, string requestText, string line1, string line2, int today, string handover = "")
	{
		List<ItemRequestFavor>? list = List();
		if (list == null || HasActive(c.Asker))
		{
			return null;
		}
		ItemRequestFavor f = new ItemRequestFavor
		{
			Id = c.Id,
			JournalId = c.JournalId,
			Asker = c.Asker,
			WantedItemId = c.WantedItemId,
			WantedItemName = c.WantedItemName,
			Amount = Math.Max(1, c.Amount),
			Mood = c.Scene.SceneType,
			StageRef = c.Scene.TemplateId,
			StagePlace = c.Scene.Location,
			StageFrom = c.Scene.TimeWindow.Min,
			StageTo = c.Scene.TimeWindow.Max,
			Plea = Clean(requestText, 240),
			ThankYou = Clean(handover, 60),
			PayKind = c.PayKind,
			PayGold = c.PayGold,
			StageScript = Script(c, requestText, line1, line2),
			AskedOn = today,
			Phase = "active"
		};
		lock (_sync)
		{
			list.Add(f);
		}
		Dirty();
		return f;
	}

	public ItemRequestFavor? Complete(Func<ItemRequestFavor, bool> match, int today)
	{
		ItemRequestFavor? f = All.FirstOrDefault(x => x.IsActive && match(x));
		if (f != null)
		{
			f.Phase = "completed";
			f.SettledOn = today;
			Dirty();
		}
		return f;
	}

	public static bool Matches(ItemRequestFavor f, string qualifiedId, string itemId)
	{
		if (string.IsNullOrWhiteSpace(f.WantedItemId))
		{
			return false;
		}
		return string.Equals(qualifiedId, f.WantedItemId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(itemId, f.WantedItemId.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) ? f.WantedItemId[3..] : f.WantedItemId, StringComparison.OrdinalIgnoreCase);
	}

	public void MarkScenePlayed(string id)
	{
		ItemRequestFavor? f = All.FirstOrDefault(x => Same(x.Id, id));
		if (f != null && !f.StagePlayed)
		{
			f.StagePlayed = true;
			Dirty();
		}
	}

	public List<ItemRequestFavor> Expire(int today)
	{
		List<ItemRequestFavor> expired = All.Where(f => f.IsActive && f.LapsesOn > 0 && today > f.LapsesOn).ToList();
		expired.ForEach(f => f.Phase = "expired");
		if (expired.Count > 0)
		{
			Dirty();
		}
		return expired;
	}

	public string PromptBlock(string npc, int today)
	{
		StringBuilder sb = new StringBuilder();
		ItemRequestFavor? active = All.FirstOrDefault(f => f.IsActive && Same(f.Asker, npc));
		if (active != null)
		{
			string reward = active.PayKind == "monetary" && active.PayGold > 0 ? $"说好给 {active.PayGold} 金" : "没说报酬";
			sb.AppendLine();
			sb.AppendLine("【你托农夫找的东西】");
			sb.AppendLine($"{active.WantedItemName} × {active.Amount}，农夫还在找（{reward}）。等它送到再说别的，眼下不另外开口要东西。");
		}
		int window = Math.Max(14, _cooldownDays());
		List<ItemRequestFavor> done = All.Where(f => f.IsCompleted && Same(f.Asker, npc) && (!f.StagePlayed || today - f.SettledOn <= window))
			.OrderByDescending(f => f.SettledOn).Take(3).ToList();
		if (done.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("【农夫已经送来的】");
			foreach (ItemRequestFavor f in done)
			{
				string ago = (today - f.SettledOn) switch { 0 => "今天", 1 => "昨天", int d => $"{d} 天前" };
				sb.AppendLine($"- {f.WantedItemName} × {f.Amount}，{ago}送到的{(f.StagePlayed ? "" : "（说好的那段场景还没一起过）")}。");
			}
			sb.AppendLine("这些都已经到手了，在你心里是办妥的事：不会再念叨少了什么，也不会重新托一回。聊到了谢一声就好，没聊到就说别的。");
		}
		return sb.ToString();
	}

	/// <summary>Data/Quests 里的一条：ItemDelivery/标题/描述/目标/村民 物品 [数量]/-1/金币/报酬说明/可取消/交付反应。</summary>
	public static string QuestData(ItemRequestFavor f, string? npcDisplay = null)
	{
		string who = string.IsNullOrWhiteSpace(npcDisplay) ? f.Asker : npcDisplay;
		string title = Field(Tr.Get("itemRequest.questTitle", "{{npc}}的托付", ("npc", who)));
		string description = Field(Tr.Get("itemRequest.questDescription", "{{npc}}想要一份{{item}}。", ("npc", who), ("item", f.WantedItemName)));
		string objective = Field(Tr.Get("itemRequest.questObjective", "把{{item}}交给{{npc}}", ("npc", who), ("item", f.WantedItemName)));
		string reaction = Field(string.IsNullOrWhiteSpace(f.ThankYou) ? Tr.Get("itemRequest.questReaction", "真的帮我带来{{item}}了！谢谢你。", ("item", f.WantedItemName)) : f.ThankYou);
		string target = f.Asker + " " + f.WantedItemId + (f.Amount > 1 ? $" {f.Amount}" : "");
		int gold = f.PayKind == "monetary" && f.PayGold > 0 ? Math.Clamp(f.PayGold, 50, 500) : 0;
		string rewardText = gold > 0 ? Field(Tr.Get("itemRequest.questGold", "{{gold}} 金", ("gold", gold.ToString()))) : "-1";
		return $"ItemDelivery/{title}/{description}/{objective}/{target}/-1/{gold}/{rewardText}/true/{reaction}";
	}

	/// <summary>Data/Events 里的键：事件 ID/这条请求的场景还没播/时间在窗口里。</summary>
	public static string EventKey(ItemRequestFavor f) => $"{EventId(f.Id)}/{SceneArmed} {f.Id}/{SceneTime} {f.StageFrom} {f.StageTo}";

	public static string EventId(string favorId)
	{
		int h = 17;
		foreach (char c in favorId.ToLowerInvariant())
		{
			h = unchecked(h * 31 + c);
		}
		return (9708000 + Math.Abs(h % 10000)).ToString();
	}

	public static string Script(ItemRequestCandidate c, string requestText, string line1, string line2)
	{
		string first = string.IsNullOrWhiteSpace(line1) ? Tr.Get("itemRequest.sceneLine1", "你真的把{{item}}带来了！", ("item", c.WantedItemName)) : line1;
		string second = string.IsNullOrWhiteSpace(line2)
			? Tr.Get("itemRequest.sceneLine2", "我一直惦记着这件事，谢谢你记得。")
			: line2;
		return c.Scene.ScriptTemplate
			.Replace("{{FAVOR_ID}}", c.Id)
			.Replace("{{NPC}}", c.Asker)
			.Replace("{{FARMER}}", "farmer")
			.Replace("{{ITEM_NAME}}", EventText(c.WantedItemName))
			.Replace("{{NPC_LINE_1}}", EventText(first))
			.Replace("{{NPC_LINE_2}}", EventText(second))
			.Replace("{{MEMORY_REFERENCE}}", EventText(Clean(requestText, 120)))
			.Trim();
	}

	/// <summary>事件台词里不能出现的字符换掉。</summary>
	public static string EventText(string text) => Clean(text, 240).Replace('"', '\'').Replace('/', '／').Replace('#', '＃').Replace('^', ' ').Replace('@', '＠').Replace('$', '＄');

	private static string Field(string text) => Clean(text, 500).Replace('/', ' ');

	public static string Clean(string? text, int max)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return t.Length > max ? t[..max] : t;
	}

	public static string SafeId(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "unknown";
		}
		return new string(value.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray()).Trim('_');
	}

	private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

	private List<ItemRequestFavor>? List() => _store()?.Get(Path, Doc);

	private void Dirty() => _store()?.MarkDirty(Path);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
