using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Awareness;

public static class SightingText
{
	private static readonly char[] ClipTrailers = " ,.;:!?、，。；：！？…—-~～\"'“”‘’（）()".ToCharArray();

	public static string Clip(string? value, int max)
	{
		StringBuilder flat = new StringBuilder();
		foreach (char ch in value ?? "")
		{
			if (char.IsWhiteSpace(ch))
			{
				if (flat.Length > 0 && flat[^1] != ' ')
				{
					flat.Append(' ');
				}
			}
			else if (!char.IsControl(ch))
			{
				flat.Append(ch);
			}
		}
		string text = flat.ToString().TrimEnd();
		int limit = Math.Max(0, max);
		if (text.Length <= limit)
		{
			return text;
		}
		int space = limit > 0 ? text.LastIndexOf(' ', limit) : -1;
		int cut = space >= limit * 0.6 ? space : limit;
		if (cut > 0 && cut < text.Length && char.IsSurrogatePair(text, cut - 1))
		{
			cut--;
		}
		return text[..cut].TrimEnd(ClipTrailers) + "…";
	}

	public static string FirstLine(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		int i = value.IndexOfAny(new[] { '\r', '\n', '\t' });
		return i < 0 ? value : value.Substring(0, i);
	}

	public static List<string> SplitList(string? csv) =>
		string.IsNullOrWhiteSpace(csv) ? new List<string>() : csv.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

	public static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
	{
		0 => "",
		1 => items[0],
		_ => string.Join("、", items.Take(items.Count - 1)) + "和" + items[^1]
	};

	public static int ToMinutes(int time) => time / 100 * 60 + time % 100;

	public static string Clock(int time)
	{
		int h = time / 100 % 24;
		int m = time % 100;
		string part = h < 5 ? "凌晨" : h < 12 ? "上午" : h < 13 ? "中午" : h < 18 ? "下午" : "晚上";
		int h12 = h % 12 == 0 ? 12 : h % 12;
		return m == 0 ? $"{part}{h12}点" : $"{part}{h12}:{m:00}";
	}

	private static string PartOfDay(int time) => time < 1200 ? "上午" : time < 1700 ? "下午" : time < 2100 ? "傍晚" : "夜里";

	/// <summary>"刚才"、"大约两小时前"、"昨天下午"、"3 天前"……</summary>
	public static string Relative(int eventDay, int eventTime, int nowDay, int nowTime)
	{
		int days = nowDay - eventDay;
		if (days <= 0)
		{
			int minutes = Math.Max(0, ToMinutes(nowTime) - ToMinutes(eventTime));
			if (minutes < 20)
			{
				return "刚才";
			}
			if (minutes < 60)
			{
				return $"大约 {minutes / 10 * 10} 分钟前";
			}
			int hours = (int)Math.Round(minutes / 60.0);
			return hours > 1 ? $"大约 {hours} 小时前" : "大约一小时前";
		}
		if (days == 1)
		{
			return eventTime >= 2100 ? "昨天夜里" : "昨天" + PartOfDay(eventTime);
		}
		if (days < 7)
		{
			return $"{days} 天前";
		}
		if (days < 14)
		{
			return "上周";
		}
		if (days < 28)
		{
			return $"大约 {days / 7} 周前";
		}
		int seasons = days / 28;
		return seasons == 1 ? "大约一个季节前" : $"大约 {seasons} 个季节前";
	}

	public static string Terminate(string sentence)
	{
		string t = sentence.Trim();
		if (t.Length == 0)
		{
			return t;
		}
		return t[^1] is '!' or '.' or '?' or '…' or '。' or '！' or '？' ? t : t + "。";
	}

	public static string EncodeNames(IEnumerable<KeyValuePair<string, string>> names) =>
		string.Join("|", names.Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value)).Select(p => Strip(p.Key) + "=" + Strip(p.Value)));

	public static Dictionary<string, string> DecodeNames(string encoded)
	{
		Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string part in encoded.Split('|', StringSplitOptions.RemoveEmptyEntries))
		{
			int eq = part.IndexOf('=');
			if (eq > 0 && eq < part.Length - 1)
			{
				d[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
			}
		}
		return d;
	}

	private static string Strip(string v) => Clip(v.Replace('|', ' ').Replace('=', ' '), 60);
}

/// <summary>把一件事按某位目击者的视角写成一句话（他自己写成"你"，他家写成"你家"）。</summary>
public sealed class SightingWriter
{
	private readonly Func<SightingCatalog> _catalog;

	public SightingWriter(Func<SightingCatalog> catalog)
	{
		_catalog = catalog;
	}

	public string Sentence(Sighting e, Witness w) => Seen(e, w) + Sooted(e, w);

	/// <summary>爆炸把这位目击者自己熏黑了。</summary>
	private static string Sooted(Sighting e, Witness w) =>
		SightingText.SplitList(e.Detail("sooted")).Any(n => string.Equals(n, w.Who, StringComparison.OrdinalIgnoreCase)) ? "——这一下把你自己熏了一身黑" : "";

	private string Seen(Sighting e, Witness w)
	{
		EventDefinition def = _catalog().Get(e.Type);
		if (!def.HasPhrases || OwnSummary(e))
		{
			return Summary(e);
		}
		Tokens tokens = new Tokens(e, w, def);
		PhraseSet set = PhrasesFor(def, w, tokens);
		int level = Certainty.Level(w, def.MinCertainty);
		PhraseTemplate? exact = set.Level(level);
		if (exact != null)
		{
			return exact.Render(tokens.Resolve);
		}
		PhraseTemplate? full = set.ClearT;
		PhraseTemplate? reduced = set.PartialT ?? full;
		PhraseTemplate? glimpse = set.FaintT ?? reduced;
		return level switch
		{
			5 => "你清清楚楚地看见" + Render(full, tokens, e),
			4 => "你看见" + Render(full, tokens, e),
			3 => "你好像看见" + Render(reduced, tokens, e),
			2 => "你好像看见" + Render(reduced, tokens, e) + "，但不太确定",
			_ => "你瞥见一个像是" + (tokens.Resolve("actor") ?? "谁") + "的人" + Render(glimpse, tokens, e) + "，但不太确定"
		};
	}

	public string Past(Sighting e)
	{
		EventDefinition def = _catalog().Get(e.Type);
		if (!def.HasPhrases || OwnSummary(e))
		{
			return Summary(e).TrimEnd('.', '。');
		}
		Tokens tokens = new Tokens(e, null, def);
		PhraseTemplate? past = def.PhrasesFor(tokens.Resolve).RecalledT;
		return past != null ? past.Render(tokens.Resolve) : Summary(e).TrimEnd('.', '。');
	}

	public string MemoryLine(Sighting e, Witness w, int nowDay, int nowTime) =>
		$"- {SightingText.Relative(e.OnDay, e.Clock, nowDay, nowTime)}，在{LocationName(e)}：{SightingText.Terminate(Sentence(e, w))}";

	public string GossipLine(Sighting e)
	{
		EventDefinition def = _catalog().Get(e.Type);
		Dictionary<string, string> names = Names(e);
		string witnesses = string.Join("、", e.SeenBy
			.OrderByDescending(w => Certainty.Level(w, def.MinCertainty))
			.Select(w => Display(names, w.Who) + "（" + Certainty.Words(Certainty.Level(w, def.MinCertainty)) + "）"));
		return $"{SightingText.Clock(e.Clock)}，在{LocationName(e)}：{SightingText.Terminate(Past(e))}目击者：{witnesses}。";
	}

	public static string LocationName(Sighting e) => SightingText.Clip(SightingText.FirstLine(e.Detail("locationDisplay", e.Place)), 120);

	public static Dictionary<string, string> Names(Sighting e) => SightingText.DecodeNames(e.Detail("displayNames"));

	public static string Display(IReadOnlyDictionary<string, string> names, string npc) => names.TryGetValue(npc, out string? v) && v.Length > 0 ? v : npc;

	private static PhraseSet PhrasesFor(EventDefinition def, Witness w, Tokens tokens)
	{
		PhraseSet set = def.PhrasesFor(tokens.Resolve);
		if (w.FiredRules.Count > 0)
		{
			foreach (EventRule rule in def.Rules)
			{
				if (rule.Wording != null && w.FiredRules.Contains(rule.Name, StringComparer.Ordinal))
				{
					return rule.Wording.Over(set);
				}
			}
		}
		return set;
	}

	private static string Render(PhraseTemplate? t, Tokens tokens, Sighting e)
	{
		string text = t?.Render(tokens.Resolve) ?? "";
		return text.Length > 0 ? text : Summary(e).TrimEnd('.', '。');
	}

	private static bool OwnSummary(Sighting e) => string.Equals(e.Detail("summaryOverride"), "true", StringComparison.OrdinalIgnoreCase);

	private static string Summary(Sighting e) => SightingText.Clip(SightingText.FirstLine(e.Detail("summary", e.Act)), 240);

	private sealed class Tokens
	{
		private readonly Sighting _e;

		private readonly Witness? _w;

		private readonly EventDefinition _def;

		private readonly Dictionary<string, string> _names;

		private readonly List<string> _owners;

		private int _depth;

		public Tokens(Sighting e, Witness? w, EventDefinition def)
		{
			_e = e;
			_w = w;
			_def = def;
			_names = Names(e);
			_owners = SightingText.SplitList(e.Detail("owners"));
		}

		public string? Resolve(string name)
		{
			if (name.Length == 0)
			{
				return null;
			}
			int colon = name.IndexOf(':');
			if (colon > 0)
			{
				string key = name.Substring(colon + 1);
				string? v = Detail(key);
				return name.Substring(0, colon) switch
				{
					"p" or "a" => v,
					"n" => v == null ? null : Name(v),
					"you" => v != null && IsWitness(v) ? "你" : null,
					_ => null
				};
			}
			return name switch
			{
				"actor" => Name(_e.Doer),
				"target" => Detail("subject") is string t ? Name(t) : null,
				"targetPossessive" => Detail("subject") is string t ? Possessive(new[] { t }) : null,
				"owners" => _owners.Count > 0 ? Possessive(_owners) : null,
				"ownersPlain" => _owners.Count > 0 ? SightingText.JoinAnd(_owners.Select(Display).ToList()) : null,
				"place" => Place(),
				"location" => LocationName(_e),
				"nearPhrase" => Detail("nearPhrase"),
				"time" => SightingText.Clock(_e.Clock),
				"count" => Detail("count"),
				"seen" => _w != null && _w.Repeats > 0 ? _w.Repeats.ToString() : null,
				"attempt" => Detail("attempt"),
				"secretMode" => string.Equals(Detail("secret"), "true", StringComparison.OrdinalIgnoreCase) ? "secret" : "open",
				"witnessName" => _w == null ? null : Display(_w.Who),
				_ => Computed(name)
			};
		}

		private string? Detail(string key)
		{
			string v = SightingText.Clip(SightingText.FirstLine(_e.Detail(key)), 120);
			return v.Length > 0 ? v : null;
		}

		private string? Computed(string name)
		{
			if (!_def.Derived.TryGetValue(name, out ComputedToken? t) || _depth >= 4)
			{
				return null;
			}
			_depth++;
			try
			{
				string v = t.Render(Resolve);
				return v.Length > 0 ? v : null;
			}
			finally
			{
				_depth--;
			}
		}

		private bool IsWitness(string npc) => _w != null && string.Equals(npc, _w.Who, StringComparison.OrdinalIgnoreCase);

		private string Display(string npc) => SightingText.Clip(SightingWriter.Display(_names, npc), 60);

		private string Name(string? npc) => string.IsNullOrWhiteSpace(npc) ? "有人" : IsWitness(npc) ? "你" : Display(npc);

		private string Possessive(IReadOnlyList<string> npcs)
		{
			if (_w != null && npcs.Contains(_w.Who, StringComparer.OrdinalIgnoreCase))
			{
				return npcs.Count == 1 ? "你的" : "你们家的";
			}
			return SightingText.JoinAnd(npcs.Select(Display).ToList()) + "的";
		}

		private string Place()
		{
			if (_w != null && _owners.Contains(_w.Who, StringComparer.OrdinalIgnoreCase))
			{
				return "你家";
			}
			return Detail("placeDisplay") ?? LocationName(_e);
		}
	}
}
