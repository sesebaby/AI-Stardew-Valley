using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;

namespace AISV.Core.Festivals;

public sealed class FestivalDetection
{
	public string Season { get; set; } = "";

	public int Day { get; set; }

	public bool MatchAnyDate { get; set; }

	public List<string> Locations { get; set; } = new List<string>();

	public Dictionary<string, string> FestivalDataMarkers { get; set; } = new Dictionary<string, string>();

	/// <summary>日期要对；有数据标记时标记全对；再看地点（没写地点时只靠标记）。</summary>
	public bool Matches(string? season, int day, string locationName, string rawLocationName, IReadOnlyDictionary<string, string>? festivalData)
	{
		if (!MatchAnyDate && (!string.Equals(season, Season, StringComparison.OrdinalIgnoreCase) || day != Day))
		{
			return false;
		}
		if (FestivalDataMarkers.Count > 0 && (festivalData == null || FestivalDataMarkers.Any(m => !festivalData.TryGetValue(m.Key, out string? v) || !string.Equals(v, m.Value, StringComparison.Ordinal))))
		{
			return false;
		}
		if (Locations.Count > 0)
		{
			return Locations.Any(l => string.Equals(l, locationName, StringComparison.OrdinalIgnoreCase) || string.Equals(l, rawLocationName, StringComparison.OrdinalIgnoreCase));
		}
		return FestivalDataMarkers.Count > 0;
	}
}

public sealed class FestivalGossip
{
	public string Summary { get; set; } = "";

	public string Chat { get; set; } = "";

	public int PromptDays { get; set; } = 2;
}

public sealed class FestivalAction
{
	public const string Dance = "dance";

	public const string Invitation = "invitation";

	public string Id { get; set; } = "";

	public string Kind { get; set; } = "";

	public string Label { get; set; } = "";

	public string Tip { get; set; } = "";

	[JsonIgnore]
	public string Prompt { get; set; } = "";
}

/// <summary>一个节日内容包：assets/festivals/&lt;id&gt;/ 下的 festival.json、prompt.txt，还有动作用的 action.txt。</summary>
public sealed class FestivalPack
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public int Priority { get; set; }

	public FestivalDetection Detection { get; set; } = new FestivalDetection();

	public FestivalGossip Gossip { get; set; } = new FestivalGossip();

	public FestivalAction? Action { get; set; }

	[JsonIgnore]
	public string Direction { get; set; } = "";
}

public static class FestivalPacks
{
	private const int MaxPromptChars = 24000;

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	/// <summary>按优先级从高到低；坏的包跳过并记一条日志。</summary>
	public static IReadOnlyList<FestivalPack> Load(string directory, ILog log)
	{
		List<FestivalPack> packs = new List<FestivalPack>();
		if (!Directory.Exists(directory))
		{
			log.Warn("找不到节日内容包文件夹，节日上村民说游戏原来的话：" + directory);
			return packs;
		}
		foreach (string folder in Directory.GetDirectories(directory))
		{
			try
			{
				if (Read(folder, log) is FestivalPack pack)
				{
					packs.Add(pack);
				}
			}
			catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
			{
				log.Warn($"节日内容包 {Path.GetFileName(folder)} 读不了：{ex.Message}");
			}
		}
		return packs.OrderByDescending(p => p.Priority).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
	}

	private static FestivalPack? Read(string folder, ILog log)
	{
		string manifest = Path.Combine(folder, "festival.json");
		if (!File.Exists(manifest))
		{
			return null;
		}
		FestivalPack? pack = JsonSerializer.Deserialize<FestivalPack>(File.ReadAllText(manifest), Options);
		if (pack == null || !string.Equals(pack.Id, Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase) || !Detectable(pack.Detection))
		{
			log.Warn($"节日内容包 {Path.GetFileName(folder)} 的 festival.json 不完整。");
			return null;
		}
		string prompt = ReadPrompt(Path.Combine(folder, "prompt.txt"));
		if (prompt.Length == 0)
		{
			log.Warn($"节日内容包 {pack.Id} 没有可用的 prompt.txt。");
			return null;
		}
		pack.Direction = prompt;
		if (pack.Action != null)
		{
			pack.Action.Prompt = ReadPrompt(Path.Combine(folder, "action.txt"));
			if (pack.Action.Prompt.Length == 0 || pack.Action.Kind is not (FestivalAction.Dance or FestivalAction.Invitation))
			{
				log.Warn($"节日内容包 {pack.Id} 的动作不完整，不提供这个动作。");
				pack.Action = null;
			}
		}
		return pack;
	}

	private static string ReadPrompt(string path)
	{
		if (!File.Exists(path))
		{
			return "";
		}
		string text = File.ReadAllText(path).Trim();
		return text.Length > MaxPromptChars ? "" : text;
	}

	private static bool Detectable(FestivalDetection d) =>
		d.MatchAnyDate ? d.FestivalDataMarkers.Count > 0 : !string.IsNullOrWhiteSpace(d.Season) && d.Day > 0 && (d.Locations.Count > 0 || d.FestivalDataMarkers.Count > 0);
}

/// <summary>两种邀请各自的答复格式：跳舞按游戏的交情门槛，组队按内容包写的门槛。</summary>
public sealed record InviteProtocol(string Kind, IReadOnlyList<string> Reasons, string GateMet, string TooLow, string GateField, int ExamplePoints)
{
	public static readonly InviteProtocol Dance = new InviteProtocol(FestivalAction.Dance,
		new[] { "close_enough", "good_memories", "shared_story", "suits_me", "sparks", "too_distant", "bad_blood", "promised_elsewhere", "not_ready", "bad_moment", "not_allowed", "unclear" },
		"close_enough", "too_distant", "exception", 20);

	public static readonly InviteProtocol Invitation = new InviteProtocol(FestivalAction.Invitation,
		new[] { "close_enough", "good_memories", "shared_story", "suits_me", "our_bond", "too_distant", "bad_blood", "promised_elsewhere", "not_ready", "bad_moment", "not_allowed", "unclear" },
		"close_enough", "too_distant", "exception", 15);

	public static InviteProtocol For(string? kind) => kind == FestivalAction.Invitation ? Invitation : Dance;
}

/// <summary>对节日邀请的答复。</summary>
public sealed class FestivalInviteDecision
{
	public bool Accepted { get; set; }

	public string ReasonCode { get; set; } = "";

	public string ReasonSummary { get; set; } = "";

	public string Reaction { get; set; } = "";

	public string Emotion { get; set; } = Dialogue.Emotion.Neutral;

	public int FriendshipPoints { get; set; }

	public bool BelowGateOverride { get; set; }

	public string MemorySummary { get; set; } = "";

	/// <summary>读不出来、没有布尔的 accepted 时返回 null。</summary>
	public static FestivalInviteDecision? Parse(string raw, int maxChars, ILog log, InviteProtocol? protocol = null)
	{
		InviteProtocol p = protocol ?? InviteProtocol.Dance;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !TryGet(root, "agrees", out JsonElement accepted) || accepted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			{
				log.Warn("节日邀请的答复里没有 agrees（true/false）。");
				return null;
			}
			FestivalInviteDecision d = new FestivalInviteDecision
			{
				Accepted = accepted.GetBoolean(),
				ReasonSummary = Clip(Text(root, "why"), 280),
				MemorySummary = Clip(Text(root, "to_remember"), 280),
				Reaction = TextTools.Truncate(Text(root, "says").Trim(), Math.Max(0, maxChars)),
				Emotion = Dialogue.Emotion.Normalize(Text(root, "feeling")),
				FriendshipPoints = Math.Clamp(Number(root, "warmth"), -10, 40),
				BelowGateOverride = TryGet(root, p.GateField, out JsonElement gate) && gate.ValueKind == JsonValueKind.True
			};
			string code = Text(root, "code").Trim().ToLowerInvariant();
			d.ReasonCode = p.Reasons.Contains(code) ? code : d.Accepted ? "unclear" : "not_ready";
			if (d.Accepted)
			{
				d.ReasonCode = d.ReasonCode == p.TooLow ? "unclear" : d.ReasonCode;
				d.FriendshipPoints = Math.Max(0, d.FriendshipPoints);
			}
			else
			{
				d.BelowGateOverride = false;
			}
			if (d.Reaction.Length == 0)
			{
				d.Reaction = d.Accepted ? "好啊，就这么说定了。" : "这次就算了吧，不过谢谢你来问我。";
			}
			return d;
		}
		catch (JsonException ex)
		{
			log.Warn("节日邀请的答复读不出来：" + ex.Message);
			return null;
		}
	}

	private static bool TryGet(JsonElement root, string name, out JsonElement value)
	{
		foreach (JsonProperty prop in root.EnumerateObject())
		{
			if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = prop.Value;
				return true;
			}
		}
		value = default;
		return false;
	}

	private static string Text(JsonElement root, string name) => TryGet(root, name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static int Number(JsonElement root, string name)
	{
		if (!TryGet(root, name, out JsonElement v))
		{
			return 0;
		}
		if (v.ValueKind == JsonValueKind.Number)
		{
			return v.TryGetInt32(out int i) ? i : (int)Math.Round(v.GetDouble());
		}
		return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out int s) ? s : 0;
	}

	private static string Clip(string? s, int max)
	{
		s = s?.Trim() ?? "";
		return s.Length > max ? s.Substring(0, max).TrimEnd() : s;
	}
}

/// <summary>节日台词的来源：游戏这一刻要说的话是不是一句单纯的闲聊。</summary>
public static class FestivalLine
{
	private const int MaxChars = 260;

	public static bool IsPlainChat(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}
		string t = raw.Trim();
		return !t.Contains("$q", StringComparison.OrdinalIgnoreCase) && !t.Contains("$r", StringComparison.OrdinalIgnoreCase)
			&& !t.Contains("answerDialogue", StringComparison.OrdinalIgnoreCase) && !t.Contains("quickQuestion", StringComparison.OrdinalIgnoreCase);
	}

	public static string Normalize(string raw)
	{
		string t = raw.Replace("\r", " ").Replace("\n", " ").Trim();
		return t.Length > MaxChars ? t.Substring(0, MaxChars) : t;
	}
}
