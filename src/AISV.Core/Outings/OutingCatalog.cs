using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AISV.Core.Diagnostics;

namespace AISV.Core.Outings;

public sealed record OutingProp(string ItemId, int X, int Y, int Width = 1, int Height = 1, bool Floor = false, float LightRadius = 0f, bool Flame = false);

/// <summary>一种出游：在哪个站位、什么时间、几心、什么语气，演出时说几句、做什么表情、摆什么布景。</summary>
public sealed class OutingPlan
{
	public string Id { get; init; } = "";

	public string Type { get; init; } = "";

	public string Title { get; init; } = "";

	public string Location { get; init; } = "";

	public int X { get; init; }

	public int Y { get; init; }

	public int FarmerFacing { get; init; } = 1;

	/// <summary>农场上的区域（门口、野餐、北/东/南入口、西角、池塘）；空 = 用固定坐标。</summary>
	public string FarmArea { get; init; } = "";

	public int TimeMin { get; init; } = 600;

	public int TimeMax { get; init; } = 2600;

	public int MinHearts { get; init; } = 3;

	public bool Romantic { get; init; }

	public bool BlocksChildren { get; init; } = true;

	public int Lines { get; init; } = 4;

	public int Emote { get; init; } = 32;

	public int Pause { get; init; } = 250;

	public string Hint { get; init; } = "";

	public IReadOnlyList<OutingProp> Props { get; init; } = Array.Empty<OutingProp>();

	public bool IsSpouseType => Type.StartsWith("spouse_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>谁在约、约的是什么关系：决定哪些出游能用。</summary>
public readonly record struct OutingAsk(int Hearts, bool Romantic, bool Child, bool Spouse = false, bool Hot = false);

/// <summary>出游计划页上一个能去的地方：地点、时间窗口、有哪些语气、几种出游。</summary>
public sealed record OutingDestination(string Location, int TimeMin, int TimeMax, IReadOnlyList<string> Types, int Count);

/// <summary>出游模板目录：assets/outings 下的 spots.json（站位）和 outings.json（出游）。</summary>
public sealed class OutingCatalog
{
	public static readonly string[] Types = { "awkward", "cozy", "date_light", "friendship", "slice_of_life", "walk", "spouse_love", "spouse_hot" };

	public static readonly string[] FarmAreas = { "doorstep", "picnic", "north_entrance", "east_entrance", "south_entrance", "west_corner", "pond" };

	public const string DoneCommand = "AisvOutingDone";

	public const string WaitingCondition = "AisvOutingWaiting";

	public const string TimeCondition = "AisvOutingTime";

	public const string SceneryCommand = "AisvOutingScenery";

	private const int BaseHearts = 3;

	private readonly List<OutingPlan> _plans;

	public OutingCatalog(IEnumerable<OutingPlan> plans)
	{
		_plans = plans.ToList();
	}

	/// <summary>门槛不高于 3 心的出游，按设置里的最低心数放宽。</summary>
	public Func<int> MinHeartsFloor { get; set; } = () => BaseHearts;

	/// <summary>这个地方现在能不能去（温泉要等铁路区开放）。</summary>
	public Func<string, bool> LocationAvailable { get; set; } = _ => true;

	public IReadOnlyList<OutingPlan> All => _plans;

	public int Count => _plans.Count;

	public IReadOnlyList<string> Locations => _plans.Select(p => OutingIntentReader.NormalizeLocation(p.Location)).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(Available).ToList();

	public OutingPlan? Find(string? id) => string.IsNullOrWhiteSpace(id) ? null : _plans.FirstOrDefault(p => Same(p.Id, id.Trim()));

	public IReadOnlyList<OutingPlan> Eligible(OutingAsk ask, OutingIntent? intent = null)
	{
		List<OutingPlan> list = _plans.Where(p => Available(OutingIntentReader.NormalizeLocation(p.Location))
			&& (!ask.Child || !p.BlocksChildren)
			&& ask.Hearts >= EffectiveHearts(p)
			&& (!p.Romantic || ask.Romantic)
			&& FitsKind(p, ask.Spouse, ask.Hot)).ToList();
		return intent is { HasConstraints: true } ? list.Where(p => Matches(p, intent)).ToList() : list;
	}

	public int EffectiveHearts(OutingPlan p)
	{
		int own = Math.Clamp(p.MinHearts <= 0 ? BaseHearts : p.MinHearts, 0, 14);
		return own > BaseHearts ? own : Math.Min(own, Math.Clamp(MinHeartsFloor(), 0, 14));
	}

	public static bool Matches(OutingPlan p, OutingIntent? intent)
	{
		if (intent is not { HasConstraints: true })
		{
			return true;
		}
		if (intent.Unresolvable || intent.HasLocation && (!intent.HasSupportedLocation || !Same(OutingIntentReader.NormalizeLocation(p.Location), intent.RequestedLocation)))
		{
			return false;
		}
		return !intent.HasTime || p.TimeMax >= intent.TimeMin!.Value && p.TimeMin <= intent.TimeMax!.Value;
	}

	/// <summary>答应以后选哪个：AI 点的那个能用就用，否则同语气随机，再否则（约会先在 spouse_love 里）随机，最后排除 spouse_hot。</summary>
	public OutingPlan? Pick(OutingDecision d, OutingAsk ask, OutingIntent? intent, Random random)
	{
		IReadOnlyList<OutingPlan> eligible = Eligible(ask, intent);
		if (eligible.Count == 0)
		{
			return null;
		}
		OutingPlan? chosen = string.IsNullOrWhiteSpace(d.OutingId) ? null : eligible.FirstOrDefault(p => Same(p.Id, d.OutingId));
		if (chosen != null)
		{
			return chosen;
		}
		string tone = NormalizeType(d.Tone);
		List<OutingPlan> sameTone = eligible.Where(p => Same(p.Type, tone)).ToList();
		if (sameTone.Count > 0)
		{
			return sameTone[random.Next(sameTone.Count)];
		}
		IReadOnlyList<OutingPlan> pool = ask.Spouse ? eligible.Where(p => p.Type == "spouse_love").ToList() : eligible;
		if (pool.Count == 0)
		{
			pool = eligible.Where(p => p.Type != "spouse_hot").ToList();
		}
		return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
	}

	/// <summary>配偶主动邀约：时间窗口还没过的配偶出游里，按概率（最多 30%）挑一个。</summary>
	public OutingPlan? PickSpouseOffer(OutingAsk ask, int timeOfDay, int chancePercent, Random random)
	{
		int chance = Math.Clamp(chancePercent, 0, 30);
		if (chance == 0)
		{
			return null;
		}
		List<OutingPlan> list = Eligible(ask with { Spouse = true, Romantic = true }).Where(p => p.TimeMax >= timeOfDay).ToList();
		if (list.Count == 0 || random.Next(100) >= chance)
		{
			return null;
		}
		return list[random.Next(list.Count)];
	}

	/// <summary>给 AI 看的可选出游。有约束时先列玩家的约束；没有能对上的就把全部列出来，只许拿来改提。</summary>
	public string OptionsBlock(OutingAsk ask, OutingIntent? intent)
	{
		IReadOnlyList<OutingPlan> eligible = Eligible(ask);
		if (eligible.Count == 0)
		{
			return "";
		}
		bool constrained = intent is { HasConstraints: true };
		List<OutingPlan> matching = constrained ? eligible.Where(p => Matches(p, intent)).ToList() : eligible.ToList();
		StringBuilder sb = new StringBuilder();
		if (constrained)
		{
			sb.AppendLine("【农夫点了地方或时间】");
			sb.AppendLine("- 想去：" + (intent!.HasLocation ? $"{intent.RequestedLocation}（{PlaceName(intent.RequestedLocation)}，原话“{intent.RawLocation}”）" : intent.Unresolvable ? "说了个地方，但认不出是哪里" : "没说"));
			sb.AppendLine("- 地方能不能去：" + intent.LocationStatus switch
			{
				OutingIntent.Supported => "supported，有这个地方的出游",
				OutingIntent.Unsupported => "unsupported，那里没有能演的出游",
				OutingIntent.Unresolved => "unresolved_from_player_text，认不出来",
				_ => "none"
			});
			sb.AppendLine("- 想几点：" + (intent.HasTime ? $"{intent.TimeMin}-{intent.TimeMax}" : "没说"));
			sb.AppendLine($"- 对得上的出游：{matching.Count} 个");
			sb.AppendLine("- 能去的地方：" + string.Join("、", eligible.Select(p => OutingIntentReader.NormalizeLocation(p.Location)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l, StringComparer.OrdinalIgnoreCase)));
			sb.AppendLine(matching.Count > 0 ? "- 要答应，只能从下面对得上的出游里挑：农夫点的地方和时间是硬条件，不能换。"
				: intent.Unresolvable ? "- 别自己编地名。可以先不去；如果还愿意出去，就在 instead 里提一个下面列出的去处。"
				: "- 农夫点的地方或时间这次去不了。可以先不去；如果还愿意出去，就在 instead 里提一个下面列出的去处。");
			sb.AppendLine();
		}
		bool counterOnly = constrained && matching.Count == 0;
		sb.AppendLine("【能演的出游】");
		sb.AppendLine("tone 只能填：" + string.Join("、", eligible.Select(p => p.Type).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)));
		sb.AppendLine(counterOnly ? "下面没有一个对得上农夫点的地方或时间，不能拿它们答应，只能拿来改提：" : "答应时 outing_id 从这里挑（id 中间那一段就是语气）：");
		sb.AppendLine("id ｜ 名字 ｜ 地点 ｜ 时间");
		foreach (OutingPlan p in (counterOnly ? eligible : matching).OrderBy(p => p.Id, StringComparer.Ordinal))
		{
			sb.AppendLine($"- {p.Id} ｜ {p.Title} ｜ {p.Location} ｜ {p.TimeMin}-{p.TimeMax}");
		}
		return sb.ToString().TrimEnd();
	}

	/// <summary>只给一个出游（配偶主动邀约）。</summary>
	public static string SingleOption(OutingPlan p) => $"【能演的出游】\n只有这一个：{p.Id} ｜ {p.Title} ｜ {p.Location} ｜ {p.TimeMin}-{p.TimeMax}";

	public IReadOnlyList<OutingDestination> Destinations(OutingAsk ask) => Eligible(ask)
		.GroupBy(p => (Location: OutingIntentReader.NormalizeLocation(p.Location), p.TimeMin, p.TimeMax))
		.Select(g => new OutingDestination(g.Key.Location, g.Key.TimeMin, g.Key.TimeMax,
			g.Select(p => NormalizeType(p.Type)).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(), g.Count()))
		.OrderBy(d => d.Location, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.TimeMin).ThenBy(d => d.TimeMax).ToList();

	/// <summary>
	/// 拼事件脚本：镜头在站位上，农夫在左、村民在右面向农夫；开头先标记完成，台词之间停一下，第二句后做表情。
	/// 第 1 句没写时用答应时说的话，其余没写时用内置的几句。
	/// </summary>
	public static string Script(OutingPlan p, string inviteId, string npc, string reaction, IReadOnlyList<string> lines)
	{
		string Line(int i) => Escape(i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) ? lines[i] : i == 0 ? reaction : DefaultLine(i));
		StringBuilder sb = new StringBuilder();
		sb.Append($"continue/{p.X} {p.Y}/farmer {p.X - 1} {p.Y} {p.FarmerFacing} {npc} {p.X + 1} {p.Y} 3/skippable/{DoneCommand} {inviteId}");
		int count = Math.Clamp(p.Lines, 4, 6);
		for (int i = 0; i < count; i++)
		{
			if (i == 2)
			{
				sb.Append($"/emote {npc} {p.Emote}");
			}
			else if (i > 0)
			{
				sb.Append($"/pause {p.Pause}");
			}
			sb.Append($"/speak {npc} \"{Line(i)}\"");
		}
		sb.Append("/end");
		return sb.ToString();
	}

	public static string DefaultLine(int index) => index switch
	{
		1 => AISV.Core.Text.Tr.Get("outing.line2", "能抽出这点时间一起待着，真好。"),
		2 => AISV.Core.Text.Tr.Get("outing.line3", "这一会儿，就当是我们俩的吧。"),
		3 => AISV.Core.Text.Tr.Get("outing.line4", "下次你也说说，在这儿看到了什么。"),
		4 => AISV.Core.Text.Tr.Get("outing.line5", "比我想的安静，这样挺好。"),
		_ => AISV.Core.Text.Tr.Get("outing.line6", "谢谢你多陪我一会儿。")
	};

	/// <summary>事件文字里会打断脚本的字符换掉，换行变空格，最多 220 字。</summary>
	public static string Escape(string? text) => OutingAi.Clean(text, 220).Replace("\\", "").Replace('"', '\'').Replace('/', '／').Replace('#', '＃').Replace('^', ' ').Replace('@', '＠').Replace('$', '＄');

	public static string NormalizeType(string? value)
	{
		string t = (value ?? "").Trim().ToLowerInvariant();
		return Types.Contains(t) ? t : "";
	}

	/// <summary>给提示词用的中文地名。</summary>
	public static string PlaceName(string location) => OutingIntentReader.NormalizeLocation(location) switch
	{
		"Town" => "镇上",
		"Beach" => "海滩",
		"Saloon" => "星之果实酒吧",
		"Mountain" => "山上",
		"CommunityCenter" => "社区中心",
		"ArchaeologyHouse" => "博物馆和图书馆",
		"BathHouse_Pool" => "温泉",
		"Farm" => "农场",
		"FarmHouse" => "农舍",
		"Forest" => "森林",
		"Sewer" => "下水道",
		string other => other
	};

	private static bool FitsKind(OutingPlan p, bool spouse, bool hot)
	{
		if (!spouse)
		{
			return !p.IsSpouseType;
		}
		if (!p.IsSpouseType)
		{
			return p.Type == "date_light";
		}
		return p.Type != "spouse_hot" || hot;
	}

	private bool Available(string location)
	{
		try
		{
			return LocationAvailable(location);
		}
		catch
		{
			return true;
		}
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	// ---------------- 读文件 ----------------

	private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	/// <summary>读站位和出游；坏掉的条目跳过并记一句。</summary>
	public static OutingCatalog Load(string? spotsJson, string? outingsJson, ILog log)
	{
		List<OutingPlan> plans = new List<OutingPlan>();
		if (string.IsNullOrWhiteSpace(spotsJson) || string.IsNullOrWhiteSpace(outingsJson))
		{
			log.Warn("出游模板文件缺失，约出去这次不可用。");
			return new OutingCatalog(plans);
		}
		try
		{
			JsonObject spots = JsonNode.Parse(spotsJson, documentOptions: ReadOptions)!.AsObject();
			foreach (JsonNode? node in JsonNode.Parse(outingsJson, documentOptions: ReadOptions)!.AsArray())
			{
				try
				{
					OutingPlan? p = ReadPlan(node!.AsObject(), spots);
					if (p == null)
					{
						log.Warn($"出游模板 {node?["id"]} 缺字段或写错了，跳过。");
					}
					else
					{
						plans.Add(p);
					}
				}
				catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is JsonException)
				{
					log.Warn($"出游模板 {node?["id"]} 读不了：{ex.Message}");
				}
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException || ex is JsonException)
		{
			log.Warn("出游模板文件读不了：" + ex.Message);
		}
		return new OutingCatalog(plans);
	}

	private static OutingPlan? ReadPlan(JsonObject o, JsonObject spots)
	{
		string id = Str(o, "id"), type = NormalizeType(Str(o, "type")), spotId = Str(o, "spot");
		if (id.Length == 0 || type.Length == 0 || spots[spotId] is not JsonObject spot)
		{
			return null;
		}
		string location = Str(spot, "location"), area = Str(spot, "farmArea");
		if (location.Length == 0 || area.Length > 0 && (location != "Farm" || !FarmAreas.Contains(area)))
		{
			return null;
		}
		JsonArray? time = o["time"] as JsonArray;
		int min = Math.Clamp(Int(time?[0], 600) is int a && a > 0 ? a : 600, 600, 2600);
		int max = Math.Clamp(Int(time?[1], 2600) is int b && b > 0 ? b : 2600, min, 2600);
		List<OutingProp> props = new List<OutingProp>();
		foreach (JsonNode? prop in o["props"] as JsonArray ?? new JsonArray())
		{
			OutingProp? p = ReadProp(prop as JsonArray);
			if (p == null)
			{
				return null;
			}
			props.Add(p);
		}
		return new OutingPlan
		{
			Id = id,
			Type = type,
			Title = Str(o, "title"),
			Location = location,
			X = Int(spot["x"], 0),
			Y = Int(spot["y"], 0),
			FarmerFacing = Int(spot["farmerFacing"], 1),
			FarmArea = area,
			TimeMin = min,
			TimeMax = max,
			MinHearts = Math.Clamp(Int(o["hearts"], BaseHearts) is int h && h > 0 ? h : BaseHearts, 0, 14),
			Romantic = o["romantic"]?.GetValue<bool>() == true,
			BlocksChildren = o["children"]?.GetValue<bool>() != true,
			Lines = Math.Clamp(Int(o["lines"], 4), 4, 6),
			Emote = Int(o["emote"], 32),
			Pause = Int(o["pause"], 250),
			Hint = Str(o, "hint"),
			Props = props
		};
	}

	/// <summary>["物品", x, y, 宽?, 高?, "floor"?, "light:半径"? / "flame:半径"?]</summary>
	private static OutingProp? ReadProp(JsonArray? a)
	{
		if (a == null || a.Count < 3 || a[0]?.GetValue<string>() is not string item || item.Trim().Length == 0)
		{
			return null;
		}
		List<int> numbers = a.Skip(1).Where(n => n is JsonValue v && v.TryGetValue(out int _)).Select(n => n!.GetValue<int>()).ToList();
		List<string> flags = a.Skip(1).Where(n => n is JsonValue v && v.TryGetValue(out string? _)).Select(n => n!.GetValue<string>()).ToList();
		if (numbers.Count < 2)
		{
			return null;
		}
		int w = numbers.Count > 2 ? numbers[2] : 1, h = numbers.Count > 3 ? numbers[3] : 1;
		float light = 0f;
		bool flame = false;
		foreach (string f in flags)
		{
			int colon = f.IndexOf(':');
			if (colon > 0 && float.TryParse(f[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
			{
				light = r;
				flame = f.StartsWith("flame", StringComparison.OrdinalIgnoreCase);
			}
		}
		if (w <= 0 || h <= 0 || !float.IsFinite(light) || light < 0f)
		{
			return null;
		}
		return new OutingProp(item.Trim(), numbers[0], numbers[1], w, h, flags.Contains("floor"), light, flame);
	}

	private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : "";

	private static int Int(JsonNode? n, int fallback) => n is JsonValue v && v.TryGetValue(out int i) ? i : fallback;
}
