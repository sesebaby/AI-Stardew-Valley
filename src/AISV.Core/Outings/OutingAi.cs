using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;

namespace AISV.Core.Outings;

/// <summary>演完以后留下的东西：一句记忆、村民说过的话、记进对话记录和当天互动的那条。</summary>
public sealed record OutingMemory(string Text, IReadOnlyList<string> Spoken, string FirstLine, string LastLine, string Place);

/// <summary>解析 AI 的邀约答复和场景台词，整理演完以后的记忆。</summary>
public static class OutingAi
{
	public const int MaxMemoryChars = 280;

	public const int MaxQuotedChars = 140;

	private static readonly int[] Cooldowns = { 0, 1, 3, 7, 14, 28 };

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

	private static readonly Regex PortraitCode = new Regex(@"[\$＄][A-Za-z0-9]+", RegexOptions.Compiled);

	private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

	/// <summary>没有布尔 agrees 就不算数。</summary>
	public static OutingDecision? Parse(string raw, int maxChars)
	{
		try
		{
			string json = JsonCleaner.Clean(raw);
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("agrees", out JsonElement accepted) || accepted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			{
				return null;
			}
			OutingDecision? d = JsonSerializer.Deserialize<OutingDecision>(json, Json);
			if (d != null)
			{
				Normalize(d, maxChars);
			}
			return d;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void Normalize(OutingDecision d, int maxChars)
	{
		d.Says = TextTools.Truncate(d.Says, maxChars);
		d.Why = Cut(d.Why, 280);
		d.OutingId = Cut(d.OutingId, 120);
		d.Beat1 = Cut(d.Beat1, 180);
		d.Beat2 = Cut(d.Beat2, 180);
		d.Beat3 = Cut(d.Beat3, 180);
		d.Beat4 = Cut(d.Beat4, 180);
		d.Beat5 = Cut(d.Beat5, 180);
		d.Beat6 = Cut(d.Beat6, 180);
		d.ToRemember = Cut(d.ToRemember, 280);
		d.LineDrawn = string.IsNullOrWhiteSpace(d.LineDrawn) ? null : Cut(d.LineDrawn, 180);
		d.Instead = d.Agrees ? null : Counter(d.Instead, maxChars);
		d.Warmth = Math.Clamp(d.Warmth, -10, 40);
		d.Feeling = Emotion.Normalize(d.Feeling);
		d.Tone = d.Agrees ? OutingCatalog.NormalizeType(d.Tone) is { Length: > 0 } t ? t : "friendship" : "";
		d.BackOffDays = d.BackOffDays == 0 ? 0 : d.BackOffDays < 0 ? d.Agrees ? 3 : d.LineDrawn != null ? 7 : 1 : Cooldowns.OrderBy(c => Math.Abs(c - d.BackOffDays)).First();
		if (!d.Agrees)
		{
			d.ClearScene();
		}
		if (string.IsNullOrWhiteSpace(d.Says))
		{
			d.Says = d.Agrees ? AISV.Core.Text.Tr.Get("outing.yes", "好啊，一起去待一会儿吧。") : AISV.Core.Text.Tr.Get("outing.no", "这次就算了，不过谢谢你约我。");
		}
	}

	private static OutingCounter? Counter(OutingCounter? c, int maxChars)
	{
		if (c == null)
		{
			return null;
		}
		string id = Cut(c.OutingId, 120);
		string place = OutingIntentReader.NormalizeLocation(Cut(c.Place, 80));
		if (id.Length == 0 || place.Length == 0)
		{
			return null;
		}
		return new OutingCounter
		{
			Place = place,
			Tone = OutingCatalog.NormalizeType(c.Tone) is { Length: > 0 } t ? t : "friendship",
			OutingId = id,
			AskText = Cut(c.AskText, 180),
			YesLabel = Cut(c.YesLabel, 80),
			NoLabel = Cut(c.NoLabel, 80),
			IfYes = TextTools.Truncate(c.IfYes, maxChars),
			IfNo = TextTools.Truncate(c.IfNo, maxChars),
			Beat1 = Cut(c.Beat1, 180),
			Beat2 = Cut(c.Beat2, 180),
			Beat3 = Cut(c.Beat3, 180),
			Beat4 = Cut(c.Beat4, 180),
			Beat5 = Cut(c.Beat5, 180),
			Beat6 = Cut(c.Beat6, 180)
		};
	}

	/// <summary>把反提议变成一次答应：分数至少 15、不冷却、可以传、记忆写成"改去某地"。</summary>
	public static OutingDecision AcceptCounter(OutingDecision original, string memory)
	{
		OutingCounter c = original.Instead!;
		return new OutingDecision
		{
			Agrees = true,
			Says = string.IsNullOrWhiteSpace(c.IfYes) ? original.Says : c.IfYes,
			Why = string.IsNullOrWhiteSpace(original.Why) ? "农夫接受了改提的去处。" : original.Why,
			Feeling = original.Feeling,
			Warmth = Math.Max(original.Warmth, 15),
			Tone = c.Tone,
			OutingId = c.OutingId,
			Beat1 = c.Beat1,
			Beat2 = c.Beat2,
			Beat3 = c.Beat3,
			Beat4 = c.Beat4,
			Beat5 = c.Beat5,
			Beat6 = c.Beat6,
			ToRemember = memory,
			LineDrawn = null,
			BackOffDays = 0,
			WorthTelling = true
		};
	}

	/// <summary>场景台词：beat_1…，要 4～6 句，前四句缺一句就不算。</summary>
	public static IReadOnlyList<string>? ParseLines(string raw, int count)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw));
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			List<string> lines = new List<string>();
			for (int i = 1; i <= Math.Clamp(count, 4, 6); i++)
			{
				string line = "";
				if (doc.RootElement.TryGetProperty($"beat_{i}", out JsonElement v) && v.ValueKind == JsonValueKind.String)
				{
					line = Cut(v.GetString()?.Replace('\r', ' ').Replace('\n', ' ').Trim('"'), 180);
				}
				lines.Add(line);
			}
			return lines.Take(4).Any(string.IsNullOrWhiteSpace) ? null : lines;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>脚本里这位村民说的每一句（去掉引号、表情代码和翻页符）。</summary>
	public static IReadOnlyList<string> SpokenLines(string? script, string? npc)
	{
		if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(npc))
		{
			return Array.Empty<string>();
		}
		List<string> lines = new List<string>();
		foreach (string command in script.Split('/'))
		{
			string c = command.Trim();
			if (!c.StartsWith("speak ", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string rest = c[6..].TrimStart();
			int space = rest.IndexOf(' ');
			if (space > 0 && string.Equals(rest[..space], npc.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				string line = CleanLine(rest[(space + 1)..]);
				if (line.Length > 0)
				{
					lines.Add(line);
				}
			}
		}
		return lines;
	}

	/// <summary>最长的那句（140 字）。</summary>
	public static string Highlight(IReadOnlyList<string>? lines) => Truncate(lines?.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).OrderByDescending(l => l.Length).FirstOrDefault() ?? "", MaxQuotedChars);

	/// <summary>
	/// 演完的记忆：有台词时写"某某和农夫一起去了哪里（语气、天气）。某某说：「最长的那句」"；
	/// 没有台词时用答应时的记忆（或默认那句）。
	/// </summary>
	public static OutingMemory Remember(OutingRecord r, string? title, IReadOnlyList<string>? spoken, string? weather, string npcDisplay, string? fallback)
	{
		List<string> lines = (spoken ?? Array.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
		string place = Place(title, r.Place);
		string text;
		if (lines.Count == 0)
		{
			text = Truncate(Clean(fallback, int.MaxValue), MaxMemoryChars);
		}
		else
		{
			List<string> notes = new List<string>();
			if (!string.IsNullOrWhiteSpace(r.Tone))
			{
				notes.Add(ToneName(r.Tone.Trim()));
			}
			if (!string.IsNullOrWhiteSpace(weather))
			{
				notes.Add(weather.Trim());
			}
			string aside = notes.Count > 0 ? "（" + string.Join("，", notes) + "）" : "";
			text = Truncate($"{npcDisplay}和农夫一起去了{place}{aside}。{npcDisplay}说：「{Highlight(lines)}」", MaxMemoryChars);
		}
		return new OutingMemory(text, lines, lines.Count > 0 ? lines[0] : text, lines.Count > 1 ? lines[^1] : "", place);
	}

	public static string ToneName(string tone) => tone switch
	{
		"friendship" => "朋友间",
		"cozy" => "温馨",
		"walk" => "散步",
		"slice_of_life" => "平常日子",
		"date_light" => "淡淡的约会",
		"awkward" => "有点尴尬",
		"spouse_love" => "夫妻间",
		"spouse_hot" => "夫妻私密",
		_ => tone
	};

	private static string Place(string? title, string? location)
	{
		string t = title?.Trim() ?? "", l = string.IsNullOrWhiteSpace(location) ? "" : OutingCatalog.PlaceName(location.Trim());
		return t.Length > 0 && l.Length > 0 ? $"{l}的「{t}」" : t.Length > 0 ? t : l.Length > 0 ? l : "山谷里的某个地方";
	}

	private static string CleanLine(string text)
	{
		string t = text.Trim();
		if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
		{
			t = t[1..^1];
		}
		t = PortraitCode.Replace(t, "").Replace("#", " ").Replace("＃", " ");
		return Spaces.Replace(t, " ").Trim();
	}

	public static string Clean(string? text, int max)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return t.Length > max ? t[..max].TrimEnd() : t;
	}

	public static string Cut(string? text, int max)
	{
		string t = text?.Trim() ?? "";
		return t.Length <= max ? t : t[..Math.Max(0, max)].TrimEnd();
	}

	private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)].TrimEnd() + "…";
}

/// <summary>邀约提示词里随类型变化的几段话。</summary>
public static class OutingPrompt
{
	public static string KindRules(OutingKind kind, bool hot)
	{
		const string spouseWhat = "这次是什么：你是农夫现在的成年配偶，这是你们俩的约会。";
		const string spouseNever = "底线：恋爱和亲密只发生在你和配偶之间；孩子、写得像孩子的角色、室友，一律不沾。";
		string rules = kind == OutingKind.Invite
			? "这次是什么：农夫想找你一起待一会儿，可以是朋友作伴、顺路走走，别先入为主当成约会。\n分寸：要带恋爱的味道，得你们的关系、以前的事、农夫这句话三样都往那边指；只要有一样不够，就按朋友来。\n底线：不写任何性方面的内容；你若是孩子或被写得像孩子，连暧昧都没有。"
			: hot
				? $"{spouseWhat}\n分寸：亲密场景是开着的，心动、靠近、成年人之间的张力都能写，前提是两人都情愿、点到为止，性行为一个细节也不写。邀请和你的回应都已经明显升温时才用 spouse_hot，其余时候在 spouse_love 和列出的 date_light 里挑。\n{spouseNever}"
				: $"{spouseWhat}\n分寸：可以甜、可以黏，但不往性上走；在 spouse_love 和列出的 date_light 里挑，这次没有 spouse_hot。\n{spouseNever}";
		if (kind == OutingKind.SpouseOffer)
		{
			rules += "\n你先开口时：这一轮其实是你在新对话一开头邀农夫出去，农夫还一个字都没说。所以 agrees=true 的意思是“我要提这个出游”，instead 留 null；outing_id 和 tone 用下面唯一列出的那个，真要演还得等农夫点头。says 就是邀请本身：带着兴致问农夫去不去，把地方和时段说清楚，别替农夫作答，也别说成是农夫约的你。此刻的气氛不适合开这个口，就写 agrees=false，照平常聊。";
		}
		return rules;
	}

	public static string ToneValues(bool spouse, bool hot) => spouse ? hot ? "date_light、spouse_love、spouse_hot" : "date_light、spouse_love" : "friendship、cozy、walk、slice_of_life、date_light、awkward";

	public static string ToneExample(bool spouse) => spouse ? "spouse_love" : "friendship";

	public static string TemplateExample(bool spouse) => spouse ? "aisv.spouse_love.door_evening" : "aisv.friendship.bench_gossip";

	/// <summary>写台词时按语气加的分寸。</summary>
	public static string SceneRules(string type) => type.ToLowerInvariant() switch
	{
		"spouse_hot" => "分寸：成年夫妻之间的约会，心动和靠近都行，前提是两人情愿、点到为止，性行为不写细节。",
		"spouse_love" => "分寸：成年夫妻之间的约会，可以甜，不往性上走。",
		"date_light" => "分寸：带点心动的相处，甜到哪一步看两人现在的关系，不往性上走。",
		_ => "分寸：先按朋友相处来写；关系真到了那一步才露一点心动，不往性上走。"
	};

	public static string LineKeys(int count) => "{" + string.Join(", ", Enumerable.Range(1, count).Select(i => $"\"beat_{i}\": \"…\"")) + "}";
}
