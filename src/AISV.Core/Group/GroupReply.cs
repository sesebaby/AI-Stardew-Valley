using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;

namespace AISV.Core.Group;

/// <summary>读群聊回复：认几种常见的键名，说话人对回内部名，清理台词，好感 ±40。</summary>
public static class GroupReply
{
	public const int MaxLineChars = 180;

	public const int MaxLines = 6;

	public const int MaxFriendship = 40;

	private static readonly string[] LineKeys = { "lines", "dialogue", "dialogues", "conversation" };

	private static readonly string[] SpeakerKeys = { "speaker", "name", "npc" };

	private static readonly string[] TextKeys = { "text", "dialogue", "line" };

	private static readonly string[] FriendshipKeys = { "friendship", "friendship_points", "points" };

	private static readonly Regex Emote = new Regex("\\*[^*]{0,40}\\*", RegexOptions.CultureInvariant);

	private static readonly Regex Codes = new Regex("#?\\$[a-zA-Z](\\s*\\d+)?#?|#\\$b#|\\[\\d+[^\\]]*\\]", RegexOptions.CultureInvariant);

	private static readonly Regex Space = new Regex("\\s+", RegexOptions.CultureInvariant);

	/// <param name="display">内部名 → 游戏里显示的名字（模型有时写显示名）。</param>
	public static GroupTurn? Parse(string? raw, IReadOnlyList<string> speakers, Func<string, string?>? display = null)
	{
		if (string.IsNullOrWhiteSpace(raw) || speakers.Count == 0)
		{
			return null;
		}
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(JsonCleaner.Clean(raw), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
		}
		catch (Exception ex) when (ex is JsonException or ArgumentException)
		{
			return null;
		}
		using (doc)
		{
			JsonElement root = doc.RootElement;
			List<GroupLine> lines = new List<GroupLine>();
			foreach (JsonElement item in Items(root))
			{
				if (ReadLine(item, speakers, display) is GroupLine line)
				{
					lines.Add(line);
					if (lines.Count >= MaxLines)
					{
						break;
					}
				}
			}
			return lines.Count == 0 ? null : new GroupTurn(lines, ReadFriendship(root, speakers), ReadMeetup(root, speakers, lines, display));
		}
	}

	private static GroupMeetup? ReadMeetup(JsonElement root, IReadOnlyList<string> speakers, IReadOnlyList<GroupLine> lines, Func<string, string?>? display)
	{
		if (root.ValueKind != JsonValueKind.Object || !Property(root, "meetup", out JsonElement m) || m.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string? speaker = Match(String(m, SpeakerKeys)?.Trim() ?? "", speakers, display);
		if (speaker == null || !lines.Any(l => string.Equals(l.Speaker, speaker, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		string Field(string key) => (String(m, new[] { key }) ?? "").Trim();
		string summary = Field("summary");
		return new GroupMeetup(speaker, Field("when"), Field("time"), Field("where"), summary.Length > 120 ? PromptText.Truncate(summary, 120) : summary, Field("task").ToLowerInvariant());
	}

	public static string Clean(string? text, string speaker, string? displayName = null)
	{
		string t = Codes.Replace(text ?? "", " ");
		t = Emote.Replace(t, " ");
		t = Space.Replace(t, " ").Trim();
		foreach (string prefix in new[] { speaker, displayName }.Where(p => !string.IsNullOrWhiteSpace(p)))
		{
			foreach (string sep in new[] { ":", "：" })
			{
				if (t.StartsWith(prefix + sep, StringComparison.OrdinalIgnoreCase))
				{
					t = t.Substring(prefix!.Length + sep.Length).Trim();
				}
			}
		}
		t = t.Replace("$", "");
		if (t.Length <= MaxLineChars)
		{
			return t;
		}
		string cut = PromptText.Truncate(t, MaxLineChars - 1);
		int space = cut.LastIndexOf(' ');
		if (space >= MaxLineChars / 2)
		{
			cut = cut.Substring(0, space);
		}
		return cut.TrimEnd(' ', ',', ';', ':', '-', '，') + "…";
	}

	private static IEnumerable<JsonElement> Items(JsonElement root)
	{
		if (root.ValueKind == JsonValueKind.Array)
		{
			return root.EnumerateArray();
		}
		if (root.ValueKind == JsonValueKind.Object)
		{
			foreach (string key in LineKeys)
			{
				if (Property(root, key, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
				{
					return value.EnumerateArray();
				}
			}
		}
		return Array.Empty<JsonElement>();
	}

	private static GroupLine? ReadLine(JsonElement item, IReadOnlyList<string> speakers, Func<string, string?>? display)
	{
		if (item.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string raw = String(item, SpeakerKeys)?.Trim() ?? "";
		string? speaker = Match(raw, speakers, display);
		if (speaker == null)
		{
			return null;
		}
		string text = Clean(String(item, TextKeys), speaker, display?.Invoke(speaker));
		return text.Length == 0 ? null : new GroupLine(speaker, text, Emotion.Normalize(String(item, new[] { "emotion" })));
	}

	public static string? Match(string raw, IReadOnlyList<string> speakers, Func<string, string?>? display = null)
	{
		string? exact = speakers.FirstOrDefault(n => string.Equals(n, raw, StringComparison.OrdinalIgnoreCase));
		return exact ?? (display == null ? null : speakers.FirstOrDefault(n => string.Equals(display(n)?.Trim(), raw, StringComparison.OrdinalIgnoreCase)));
	}

	private static IReadOnlyDictionary<string, int> ReadFriendship(JsonElement root, IReadOnlyList<string> speakers)
	{
		Dictionary<string, int> points = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (root.ValueKind != JsonValueKind.Object)
		{
			return points;
		}
		foreach (string key in FriendshipKeys)
		{
			if (!Property(root, key, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			foreach (JsonProperty p in value.EnumerateObject())
			{
				string? name = speakers.FirstOrDefault(n => string.Equals(n, p.Name.Trim(), StringComparison.OrdinalIgnoreCase));
				if (name != null && Int(p.Value, out int amount))
				{
					points[name] = Math.Clamp(amount, -MaxFriendship, MaxFriendship);
				}
			}
			break;
		}
		return points;
	}

	private static bool Int(JsonElement v, out int amount)
	{
		amount = 0;
		if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
		{
			amount = (int)Math.Round(Math.Clamp(d, -1000, 1000));
			return true;
		}
		return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString()?.Trim().TrimStart('+'), out amount);
	}

	private static string? String(JsonElement e, IEnumerable<string> keys)
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
			if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
			{
				value = p.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
}
