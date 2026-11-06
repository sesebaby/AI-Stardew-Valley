using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;

namespace AISV.Core.Stories;

/// <summary>检测要的材料（主线程准备好）。</summary>
public sealed record ArcDetectionInput(string Gossip, string Conversations, string ExistingArcs, string Season, int Year, string PlayerNames, string ExpulsionGuidance);

/// <summary>写结局要的材料（主线程准备好）。</summary>
public sealed record ArcConclusionInput(StoryArc Arc, string PlayerName, string Personalities, string EmotionalProfiles, string PlayerProfile, string AllNpcNames);

/// <summary>AI 写回来的结局。</summary>
public sealed class ArcConclusion
{
	public List<ConclusionLine> Lines { get; init; } = new List<ConclusionLine>();

	public string Summary { get; init; } = "";

	public Dictionary<string, string> PostLines { get; init; } = new Dictionary<string, string>();

	public List<string> Tags { get; init; } = new List<string>();

	/// <summary>逐出结局实际是怎么走的（没写就 null）。</summary>
	public string? OutcomeKind { get; init; }

	/// <summary>逐出或回归的一句话说明（成为全村都知道的事）。</summary>
	public string? Headline { get; init; }
}

/// <summary>小剧场的两次 AI 请求：每周看一次镇上有没有新的故事线；玩家收尾时写结局场景。</summary>
public sealed class StoryArcAi
{
	public const string DetectRequest = "story_arc_detection";

	public const string ConcludeRequest = "arc_conclusion";

	private static readonly HashSet<string> NotNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Farmer", "Player" };

	private readonly IAiSender _ai;

	private readonly PromptTemplates _templates;

	private readonly Func<string> _language;

	private readonly ILog _log;

	public StoryArcAi(IAiSender ai, PromptTemplates templates, Func<string> language, ILog log)
	{
		_ai = ai;
		_templates = templates;
		_language = language;
		_log = log;
	}

	public async Task<List<StoryArc>?> DetectAsync(ArcDetectionInput input, Func<string, string> toInternal, CancellationToken ct = default)
	{
		string system = _templates.Fill(DetectRequest, new Dictionary<string, string?>
		{
			["FARMER_NAME"] = input.PlayerNames,
			["RUMOR_LIST"] = input.Gossip,
			["TALK_SO_FAR"] = input.Conversations,
			["RUNNING_STORIES"] = input.ExistingArcs,
			["SEASON_NOW"] = input.Season,
			["YEAR_NUM"] = input.Year.ToString(),
			["STORY_CAP"] = "1",
			["SPEAK_IN"] = _language()
		});
		if (!string.IsNullOrWhiteSpace(input.ExpulsionGuidance))
		{
			system += "\n\n" + input.ExpulsionGuidance;
		}
		AiResult r = await _ai.SendAsync(new ChatRequest(DetectRequest, system, "看看镇上最近的事，有没有正在成形的故事线。", 8192, 0.7), ct).ConfigureAwait(false);
		if (!r.Ok || string.IsNullOrWhiteSpace(r.Text))
		{
			return null;
		}
		List<StoryArc>? arcs = ParseDetection(r.Text, toInternal, input.PlayerNames, _log);
		if (arcs == null)
		{
			_log.Warn("小剧场检测的回复解析不了。");
		}
		return arcs;
	}

	public static List<StoryArc>? ParseDetection(string raw, Func<string, string> toInternal, string playerNames, ILog log)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			if (!doc.RootElement.TryGetProperty("storylines", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			List<StoryArc> list = new List<StoryArc>();
			foreach (JsonElement item in arr.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				List<string> involved = item.TryGetProperty("cast", out JsonElement who) && who.ValueKind == JsonValueKind.Array
					? who.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? toInternal(n.GetString() ?? "") : "").ToList()
					: new List<string>();
				List<ArcObjective> objectives = item.TryGetProperty("steps", out JsonElement objs) && objs.ValueKind == JsonValueKind.Array ? ParseObjectives(objs) : new List<ArcObjective>();
				foreach (ArcObjective o in objectives.Where(o => !string.IsNullOrEmpty(o.Npc)))
				{
					o.Npc = toInternal(o.Npc);
				}
				string title = Str(item, "name");
				string? why = StoryArcBook.Validate(objectives, involved, playerNames, out int dropped);
				if (why != null)
				{
					log.Warn($"小剧场「{title}」不要：{why}。");
					continue;
				}
				if (dropped > 0)
				{
					log.Debug($"小剧场「{title}」去掉了 {dropped} 个带东西的目标。");
				}
				StoryArc arc = new StoryArc
				{
					Id = Str(item, "key"),
					Title = title,
					Premise = Str(item, "premise"),
					Cast = involved,
					Stage = Str(item, "right_now"),
					State = NormalizeStatus(Opt(item, "state")),
					Goals = objectives,
					StagePlace = Opt(item, "place")
				};
				string shape = Opt(item, "shape") ?? "";
				string kind = Same(shape, "departure") ? "expulsion" : Same(shape, "return") ? "expulsion_return" : "standard";
				if (kind != "standard" && item.TryGetProperty("mover", out JsonElement ex) && ex.ValueKind == JsonValueKind.Object)
				{
					string target = Str(ex, "who");
					if (!string.IsNullOrWhiteSpace(target))
					{
						arc.Kind = kind;
						arc.LeavingNpc = toInternal(target);
						arc.LeavingHow = Opt(ex, "how");
						arc.LeavingWhy = Opt(ex, "why");
					}
					else
					{
						log.Warn($"小剧场「{title}」说是 {shape}，却没写是谁，当普通故事线。");
					}
				}
				list.Add(arc);
			}
			return list;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static List<ArcObjective> ParseObjectives(JsonElement arr) => arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => new ArcObjective
	{
		Id = Str(e, "key"),
		Kind = StoryArcBook.NormalizeObjectiveType(Opt(e, "kind")),
		Text = Str(e, "task"),
		Npc = Str(e, "person"),
		Item = Opt(e, "item"),
		Topic = Opt(e, "subject"),
		DoneWhen = Str(e, "done_when")
	}).ToList();

	public static string NormalizeStatus(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
	{
		"" or "active" or "activo" or "ativo" or "attivo" or "aktiv" or "actif" => StoryArc.Active,
		_ => status!
	};

	public async Task<ArcConclusion?> ConcludeAsync(ArcConclusionInput input, Func<string, string> toInternal, CancellationToken ct = default)
	{
		StoryArc arc = input.Arc;
		string system = _templates.Fill(ConcludeRequest, new Dictionary<string, string?>
		{
			["FARMER_NAME"] = input.PlayerName,
			["STORY_NAME"] = arc.Title,
			["STORY_PREMISE"] = arc.Premise,
			["STORY_CAST"] = string.Join(", ", arc.Cast),
			["STAGE_PLACE"] = arc.StagePlace ?? "Town",
			["FARMER_CHOICE"] = ChoiceBlock(arc),
			["GOALS_AND_HOW"] = ObjectivesBlock(arc),
			["ALL_NATURES"] = input.Personalities,
			["ALL_FEELINGS"] = input.EmotionalProfiles,
			["ABOUT_FARMER"] = input.PlayerProfile,
			["EVERYONE"] = input.AllNpcNames,
			["SPEAK_IN"] = _language()
		});
		system += OutcomeBlock(arc);
		AiResult r = await _ai.SendAsync(new ChatRequest(ConcludeRequest, system, $"给「{arc.Title}」写收尾的那场戏。", 8192, 0.8), ct).ConfigureAwait(false);
		if (!r.Ok || string.IsNullOrWhiteSpace(r.Text))
		{
			return null;
		}
		ArcConclusion? c = ParseConclusion(r.Text, arc, input.PlayerName, toInternal);
		if (c == null)
		{
			_log.Warn($"小剧场「{arc.Title}」的结局解析不了。");
		}
		else
		{
			int empty = c.Lines.Count(l => string.IsNullOrWhiteSpace(l.Line));
			_log.Debug($"小剧场「{arc.Title}」的结局：{c.Lines.Count} 句台词（{empty} 句空的）、{c.PostLines.Count} 句结局后的话、{c.Tags.Count} 个标签。");
		}
		return c;
	}

	public static ArcConclusion? ParseConclusion(string raw, StoryArc arc, string playerName, Func<string, string> toInternal)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			List<ConclusionLine> lines = new List<ConclusionLine>();
			if (root.TryGetProperty("scene_lines", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement e in arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
				{
					string speaker = First(e, "who", "speaker") ?? "";
					if (Same(speaker, playerName) || NotNpcs.Contains(speaker.Trim()))
					{
						continue;
					}
					lines.Add(new ConclusionLine
					{
						Who = toInternal(speaker),
						Line = First(e, "says", "text", "line") ?? "",
						Feeling = First(e, "feeling") ?? "neutral"
					});
				}
			}
			Dictionary<string, string> post = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (root.TryGetProperty("afterwords", out JsonElement map) && map.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty p in map.EnumerateObject())
				{
					post[toInternal(p.Name)] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
				}
			}
			List<string> tags = root.TryGetProperty("themes", out JsonElement t) && t.ValueKind == JsonValueKind.Array
				? t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToList()
				: new List<string>();
			string? kind = null, headline = null;
			if (arc.IsExpulsion && root.TryGetProperty("departure", out JsonElement eo) && eo.ValueKind == JsonValueKind.Object)
			{
				kind = First(eo, "how");
				headline = First(eo, "town_story");
			}
			else if (arc.IsReturn && root.TryGetProperty("homecoming", out JsonElement ro) && ro.ValueKind == JsonValueKind.Object)
			{
				headline = First(ro, "town_story");
			}
			return new ArcConclusion
			{
				Lines = lines,
				Summary = Str(root, "how_it_ended"),
				PostLines = post,
				Tags = tags,
				OutcomeKind = string.IsNullOrWhiteSpace(kind) ? null : kind,
				Headline = string.IsNullOrWhiteSpace(headline) ? null : headline
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>玩家挑的收尾方式。</summary>
	public static string ChoiceBlock(StoryArc arc)
	{
		ArcChoiceOption? c = StoryArcBook.Selected(arc);
		if (c == null)
		{
			return "";
		}
		return $"【农夫挑的收尾】{c.Label}（{c.Text}）\n"
			+ $"往这个方向收：{c.Outcome}\n"
			+ "观众看完要能感觉到是这条路走到了头，好处和代价都摆在那里。\n";
	}

	public static string ObjectivesBlock(StoryArc arc)
	{
		StringBuilder sb = new StringBuilder();
		foreach (ArcObjective o in arc.Goals)
		{
			sb.AppendLine($"- {o.Text}（农夫的做法：{o.DoneHow ?? "没记"}；办到的日子：{o.DoneOn ?? "没记"}）");
		}
		return sb.ToString();
	}

	/// <summary>逐出、回归故事线必须的结局。</summary>
	public static string OutcomeBlock(StoryArc arc)
	{
		string who = arc.LeavingNpc ?? "";
		if (arc.IsExpulsion)
		{
			return $"\n\n【定局：{who}离开鹈鹕镇】\n"
				+ $"这出从一开始就是为{who}的离去准备的（起头时设想的走法：{arc.LeavingHow ?? "left_village"}；由头：{arc.LeavingWhy ?? "看前面的经过"}）。这场戏要交代清楚三件事：\n"
				+ $"1. 走的方式和原因要落到实处：是被带走、被大家请走，还是自己收拾东西走了，看完的人要说得出来。\n"
				+ $"2. 镇上的人各有各的滋味：庆幸的、心里过意不去的、舍不得的、背后嘀咕的，别让所有人一个反应。{who}自己可以在戏里留最后一句话，演完就从镇上消失了。\n"
				+ $"3. how_it_ended 里用一句话写定{who}的去向和缘由，这句话以后就是镇上的说法。\n"
				+ "另外在 JSON 最外层加一项：\"departure\": { \"how\": \"arrested|exiled|left_village\", \"town_story\": \"…\" }\n"
				+ "- how 按戏里最后真正发生的写，剧情转了向就跟着改。\n"
				+ "- town_story 是全镇往后口口相传的那句话：说得出是哪件事、哪桩丑闻、哪一下让人忍无可忍，像\"因为……\"\"终于……\"这样有来龙去脉，不要\"出了点事\"这种空话。";
		}
		if (arc.IsReturn)
		{
			return $"\n\n【定局：{who}回到鹈鹕镇】\n"
				+ $"这场戏要交代清楚三件事：\n"
				+ $"1. {who}是带着什么条件回来的：大家原谅了、只是容下了、还要看看表现，还是镇上为此吵成两派。\n"
				+ "2. 欢迎的和不欢迎的都要有声音，别写成一团和气。\n"
				+ $"3. how_it_ended 写明{who}回来了，以及回来的条件。\n"
				+ "另外在 JSON 最外层加一项：\"homecoming\": { \"town_story\": \"…\" }\n"
				+ $"- town_story 是镇上往后提起这件事的那句话，要具体说出{who}怎么回来的、背着什么约定。";
		}
		return "";
	}

	private static string? First(JsonElement e, params string[] names)
	{
		foreach (string n in names)
		{
			if (e.TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.String)
			{
				return v.GetString();
			}
		}
		foreach (string n in names)
		{
			foreach (JsonProperty p in e.EnumerateObject())
			{
				if (Same(p.Name, n) && p.Value.ValueKind == JsonValueKind.String)
				{
					return p.Value.GetString();
				}
			}
		}
		return null;
	}

	private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
