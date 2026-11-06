using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.World;

namespace AISV.Core.Scenes;

/// <summary>旁听：请 AI 写村民之间的几句（或一个人的自言自语），读回来的台词。</summary>
public static class OverheardAi
{
	public const int MaxLineChars = 90;

	public const int PersonaChars = 320;

	private static readonly string[] ListKeys = { "lines", "dialogue", "dialogues", "exchange", "conversation" };

	private static readonly string[] SpeakerKeys = { "speaker", "name", "npc" };

	private static readonly string[] TextKeys = { "text", "dialogue", "line" };

	private static readonly Regex Emote = new Regex(@"\*[^*]{0,40}\*", RegexOptions.CultureInvariant);

	private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

	private static readonly JsonDocumentOptions DocOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

	public const string NotAboutFarmerRule = "他们是在自己说自己的，跟农夫无关：不朝农夫说话，也不提起农夫。";

	public const string BehindBackRule = "他们没发现农夫就在旁边，正背着他聊上面那件事：不朝农夫说话；照各自的脾气，可以打趣、挑刺，也可以替他说句好话；亲眼看见的照实说，听来的说是听说，别编资料里没有的细节。";

	/// <summary>背后聊农夫时放进“惦记的事”的那一段。</summary>
	public static string FarmerTopicBlock(string topic) => PromptText.Wrap("FARMER_NEWS", "正好聊起农夫最近的事——" + topic, 240);

	/// <summary>一个人 1～2 句，几个人 2～4 句。</summary>
	public static int MaxLines(int speakers) => speakers <= 1 ? MomentRules.MaxMonologueLines : MomentRules.MaxOverheardLines;

	/// <summary>
	/// 读回复：认 {lines:[…]} 及几种别名、直接的数组、单条对象、"名字: 话" 的字符串；只留名单里的人说的，最多 <paramref name="maxLines"/> 句。
	/// </summary>
	public static IReadOnlyList<SceneLine>? Parse(string? raw, IReadOnlyList<string> speakers, int maxLines)
	{
		if (string.IsNullOrWhiteSpace(raw) || maxLines <= 0)
		{
			return null;
		}
		List<string> names = speakers.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (names.Count == 0)
		{
			return null;
		}
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(JsonCleaner.Clean(raw), DocOptions);
		}
		catch (Exception ex) when (ex is JsonException or ArgumentException)
		{
			return null;
		}
		using (doc)
		{
			List<SceneLine> lines = new List<SceneLine>();
			foreach (JsonElement item in Items(doc.RootElement))
			{
				SceneLine? line = Read(item, names);
				if (line == null)
				{
					continue;
				}
				lines.Add(line);
				if (lines.Count >= maxLines)
				{
					break;
				}
			}
			return lines.Count > 0 ? lines : null;
		}
	}

	private static IEnumerable<JsonElement> Items(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.EnumerateArray().ToList();
		}
		if (root.ValueKind != JsonValueKind.Object)
		{
			return Array.Empty<JsonElement>();
		}
		foreach (string key in ListKeys)
		{
			if (Property(root, key, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
			{
				return list.EnumerateArray().ToList();
			}
		}
		if (StringOf(root, TextKeys) != null)
		{
			return new[] { root };
		}
		foreach (JsonProperty p in root.EnumerateObject())
		{
			if (p.Value.ValueKind == JsonValueKind.Array)
			{
				return p.Value.EnumerateArray().ToList();
			}
		}
		return Array.Empty<JsonElement>();
	}

	private static SceneLine? Read(JsonElement item, IReadOnlyList<string> names)
	{
		string? who;
		string? text;
		if (item.ValueKind == JsonValueKind.Object)
		{
			who = StringOf(item, SpeakerKeys);
			text = StringOf(item, TextKeys);
		}
		else if (item.ValueKind == JsonValueKind.String)
		{
			text = item.GetString();
			who = Prefix(text, names, out string rest);
			if (who != null)
			{
				text = rest;
			}
		}
		else
		{
			return null;
		}
		string? speaker = string.IsNullOrWhiteSpace(who) ? names.Count == 1 ? names[0] : null : names.FirstOrDefault(n => Same(n, who.Trim()));
		if (speaker == null)
		{
			return null;
		}
		string clean = CleanText(text, speaker);
		return clean.Length > 0 ? new SceneLine(speaker, clean) : null;
	}

	private static string? Prefix(string? text, IReadOnlyList<string> names, out string rest)
	{
		rest = text ?? "";
		int colon = rest.IndexOf(':');
		if (colon <= 0)
		{
			return null;
		}
		string head = rest[..colon].Trim();
		string? name = names.FirstOrDefault(n => Same(n, head));
		if (name != null)
		{
			rest = rest[(colon + 1)..];
		}
		return name;
	}

	private static string CleanText(string? text, string speaker)
	{
		string t = SceneBubbles.Clean(text);
		t = Spaces.Replace(Emote.Replace(t, " "), " ").Trim();
		if (t.StartsWith(speaker + ":", StringComparison.OrdinalIgnoreCase))
		{
			t = t[(speaker.Length + 1)..];
		}
		return Shorten(SceneBubbles.Clean(t.Replace("$", "")), MaxLineChars);
	}

	/// <summary>超长时尽量在句末断开（后半段里找），找不到再按空格截并补省略号。</summary>
	public static string Shorten(string text, int max)
	{
		if (text.Length <= max)
		{
			return text;
		}
		string head = SceneCatalog.Slice(text, max);
		for (int i = head.Length - 1; i >= max / 2; i--)
		{
			char c = head[i];
			if ("。！？".Contains(c) || ".!?…".Contains(c) && char.IsWhiteSpace(text[i + 1]))
			{
				return head[..(i + 1)];
			}
		}
		string cut = SceneCatalog.Slice(text, max - 1);
		int space = cut.LastIndexOf(' ');
		if (space >= max / 2)
		{
			cut = cut[..space];
		}
		return cut.TrimEnd(' ', '.', ',', ';', ':', '-', '—', '…') + "…";
	}

	/// <summary>每个说话的人一行人设（320 字），包成资料块。</summary>
	public static string Personas(IEnumerable<(string Name, string Persona)> speakers)
	{
		List<string> rows = speakers
			.Select(s => (s.Name, Text: string.Join(" ", (s.Persona ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))))
			.Where(s => s.Text.Length > 0)
			.Select(s => $"- {s.Name}：{PromptText.Truncate(s.Text, PersonaChars)}").ToList();
		return rows.Count == 0 ? "" : PromptText.Wrap("SPEAKER_PERSONALITIES", string.Join("\n", rows));
	}

	/// <summary>地点、几点（早上/下午/傍晚/深夜）、季节。</summary>
	public static string Setting(string location, int time, string? season)
	{
		string place = string.Join(" ", PromptText.Sanitize(location, 80).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		string part = time < 1200 ? "早上" : time < 1800 ? "下午" : time < 2100 ? "傍晚" : "深夜";
		StringBuilder s = new StringBuilder("地点：").Append(place.Length > 0 ? LocationFacts.Describe(place) : "镇上某处").Append('\n');
		s.Append($"时间：{time / 100 % 24:D2}:{time % 100:D2}（{part}）");
		if (!string.IsNullOrWhiteSpace(season))
		{
			s.Append("\n季节：").Append(HistoryFormatter.SeasonName(season));
		}
		return s.ToString();
	}

	/// <summary>"A、B 和 C"。</summary>
	public static string JoinNames(IReadOnlyList<string> names) => names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join("、", names.Take(names.Count - 1)) + "和" + names[^1];

	private static string? StringOf(JsonElement e, IEnumerable<string> keys)
	{
		foreach (string key in keys)
		{
			if (Property(e, key, out JsonElement v) && v.ValueKind == JsonValueKind.String)
			{
				return v.GetString();
			}
		}
		return null;
	}

	private static bool Property(JsonElement e, string key, out JsonElement value)
	{
		foreach (JsonProperty p in e.EnumerateObject())
		{
			if (Same(p.Name, key))
			{
				value = p.Value;
				return true;
			}
		}
		value = default;
		return false;
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
