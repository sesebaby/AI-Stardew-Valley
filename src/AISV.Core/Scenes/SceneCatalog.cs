using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using AISV.Core.Diagnostics;

namespace AISV.Core.Scenes;

/// <summary>内置场景目录：读、校验、按地点找、拼过场脚本、决定怎么演。</summary>
public sealed class SceneCatalog
{
	public const string ArmedCondition = "AisvSceneArmed";

	public const string TimeCondition = "AisvSceneTime";

	public const string DoneCommand = "AisvSceneDone";

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	private readonly List<SceneDefinition> _all;

	private SceneCatalog(List<SceneDefinition> all)
	{
		_all = all;
	}

	public IReadOnlyList<SceneDefinition> All => _all;

	public int Count => _all.Count;

	public SceneDefinition? Find(string? id) => string.IsNullOrWhiteSpace(id) ? null : _all.FirstOrDefault(d => Same(d.Id, id.Trim()));

	public IReadOnlyList<SceneDefinition> At(string? location) => _all.Where(d => Same(d.Place, location)).ToList();

	public IReadOnlyList<string> Locations => _all.Select(d => d.Place).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	public static SceneCatalog Load(string? json, ILog? log = null)
	{
		List<SceneDefinition> list = new List<SceneDefinition>();
		if (string.IsNullOrWhiteSpace(json))
		{
			log?.Warn("没有找到场景目录。");
			return new SceneCatalog(list);
		}
		List<SceneDefinition>? raw;
		try
		{
			raw = JsonSerializer.Deserialize<List<SceneDefinition>>(json, Options);
		}
		catch (JsonException ex)
		{
			log?.Warn($"场景目录读不了：{ex.Message}");
			return new SceneCatalog(list);
		}
		HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (SceneDefinition d in raw ?? new List<SceneDefinition>())
		{
			string? why = Normalize(d);
			if (why != null)
			{
				log?.Warn($"跳过场景 {(d.Id.Length > 0 ? d.Id : "（没有 id）")}：{why}。");
				continue;
			}
			if (!ids.Add(d.Id))
			{
				log?.Warn($"跳过场景 {d.Id}：id 重复。");
				continue;
			}
			list.Add(d);
		}
		return new SceneCatalog(list);
	}

	/// <summary>整理一条场景；不能用时返回原因。</summary>
	private static string? Normalize(SceneDefinition d)
	{
		d.Id = d.Id?.Trim() ?? "";
		d.Place = d.Place?.Trim() ?? "";
		d.EventCode = d.EventCode?.Trim() ?? "";
		d.Name = d.Name?.Trim() ?? "";
		d.Odds = Math.Max(1, d.Odds);
		d.RestDays = Math.Max(0, d.RestDays);
		d.Repeats = Math.Max(0, d.Repeats);
		d.Needs ??= new SceneConditions();
		d.Needs.InSeasons ??= new List<string>();
		d.Needs.InWeather ??= new List<string>();
		d.Needs.HeartsAtLeast = new Dictionary<string, int>(d.Needs.HeartsAtLeast ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
		d.Needs.HeartsAtMost = new Dictionary<string, int>(d.Needs.HeartsAtMost ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
		d.Needs.Luck = Math.Clamp(d.Needs.Luck, 0, 1);
		if (d.Reach.HasValue)
		{
			d.Reach = Math.Clamp(d.Reach.Value, 2, 30);
		}
		d.Actors = (d.Players ?? new List<List<JsonElement>>()).Where(c => c.Count == 4 && c[0].ValueKind == JsonValueKind.String && c.Skip(1).All(e => e.ValueKind == JsonValueKind.Number))
			.Select(c => new SceneActor(c[0].GetString()!.Trim(), c[1].GetInt32(), c[2].GetInt32(), c[3].GetInt32())).ToList();
		d.Lines = (d.ScriptRows ?? new List<List<string>>()).Where(l => l.Count == 2 && !string.IsNullOrWhiteSpace(l[0]) && !string.IsNullOrWhiteSpace(l[1]))
			.Select(l => new SceneLine(l[0].Trim(), l[1].Trim())).ToList();
		d.EmoteCue = d.Gesture is { Count: 2 } e && e[0].ValueKind == JsonValueKind.String && e[1].ValueKind == JsonValueKind.Number ? (e[0].GetString()!.Trim(), e[1].GetInt32()) : null;
		d.Keepsake = string.IsNullOrWhiteSpace(d.Keepsake) ? null : Cut(d.Keepsake.Trim(), SceneMemory.MaxChars);
		if (d.Id.Length == 0)
		{
			return "没有 id";
		}
		if (d.Place.Length == 0)
		{
			return "没有地点";
		}
		if (d.EventCode.Length == 0)
		{
			return "没有事件编号";
		}
		if (d.Camera is not { Length: 2 } || d.Actors.Count == 0)
		{
			return "没有镜头或站位";
		}
		if (d.Lines.Count == 0)
		{
			return "没有台词";
		}
		if (d.Lines.Any(l => !d.Actors.Any(a => Same(a.Name, l.Speaker))))
		{
			return "有台词的人不在站位里";
		}
		return null;
	}

	/// <summary>
	/// 过场脚本：镜头、站位、可跳过、标记演完，然后台词；第二句之后放表情。
	/// 进事件表时用 <see cref="EventKey"/> 作键。
	/// </summary>
	public static string Script(SceneDefinition d)
	{
		StringBuilder s = new StringBuilder("continue/");
		s.Append(d.Camera[0]).Append(' ').Append(d.Camera[1]).Append('/');
		s.Append(string.Join(" ", d.Actors.Select(a => $"{a.Name} {a.X} {a.Y} {a.Facing}")));
		s.Append("/skippable/").Append(DoneCommand).Append(' ').Append(d.Id);
		for (int i = 0; i < d.Lines.Count; i++)
		{
			s.Append("/speak ").Append(d.Lines[i].Speaker).Append(" \"").Append(Escape(d.Lines[i].Text)).Append('"');
			if (i == Math.Min(1, d.Lines.Count - 1) && d.EmoteCue is { } emote)
			{
				s.Append("/emote ").Append(emote.Who).Append(' ').Append(emote.Id.ToString(CultureInfo.InvariantCulture));
			}
		}
		return s.Append("/end").ToString();
	}

	/// <summary>事件表里的键：编号、今天挑中、时间窗口。</summary>
	public static string EventKey(SceneDefinition d) => d.Hours is { Length: 2 }
		? $"{d.EventCode}/{ArmedCondition} {d.Id}/{TimeCondition} {d.WindowMin} {d.WindowMax}"
		: $"{d.EventCode}/{ArmedCondition} {d.Id}";

	/// <summary>台词里会打断脚本的字符换掉；$h 这类表情码留着。</summary>
	public static string Escape(string text) => text.Replace("\\", "＼").Replace("\"", "”").Replace("/", "／").Replace("\r", "").Replace("\n", " ");

	/// <summary>
	/// 怎么演：过场风格下都是过场；否则一个人说话的走过来、几个人说话的旁听。
	/// </summary>
	public static SceneDelivery Delivery(SceneDefinition d, string style) =>
		SceneStyle.Normalize(style) == SceneStyle.Cinematic ? SceneDelivery.Cinematic : d.Speakers.Count < 2 ? SceneDelivery.WalkUp : SceneDelivery.Overheard;

	public static string DeliveryName(SceneDelivery delivery) => delivery switch
	{
		SceneDelivery.WalkUp => "走过来",
		SceneDelivery.Overheard => "旁听",
		_ => "过场"
	};

	/// <summary>写好的台词连成一句心里想的事，给 AI 当资料（400 字）。</summary>
	public static string Premise(IEnumerable<SceneLine> lines)
	{
		string joined = string.Join(" / ", lines.Select(l => (l.Speaker, Text: SceneBubbles.Clean(l.Text))).Where(p => p.Text.Length > 0).Select(p => p.Speaker.Length > 0 ? $"{p.Speaker}: {p.Text}" : p.Text));
		joined = string.Join(" ", joined.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (joined.Length <= MomentRules.MaxPremiseChars)
		{
			return joined;
		}
		string cut = Slice(joined, MomentRules.MaxPremiseChars - 1);
		int space = cut.LastIndexOf(' ');
		if (space >= MomentRules.MaxPremiseChars / 2)
		{
			cut = cut[..space];
		}
		return cut.TrimEnd() + "…";
	}

	/// <summary>截到 <paramref name="max"/> 字，超出时补省略号。</summary>
	internal static string Cut(string text, int max) => text.Length <= max ? text : Slice(text, max).TrimEnd() + "…";

	/// <summary>截到 <paramref name="max"/> 字，不拆开代理对。</summary>
	internal static string Slice(string text, int max) => text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
