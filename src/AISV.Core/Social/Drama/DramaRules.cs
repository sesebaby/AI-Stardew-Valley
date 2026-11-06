using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AISV.Core.Persona;
using AISV.Core.Social.Personality;
using AISV.Core.Text;

namespace AISV.Core.Social.Drama;

/// <summary>剧情强度和通用的数值、名单小工具。</summary>
public static class DramaRules
{
	public const string Cozy = "Cozy";
	public const string Balanced = "Balanced";
	public const string Dramatic = "Dramatic";

	public static string Intensity(string? value) =>
		string.Equals(value, Cozy, StringComparison.OrdinalIgnoreCase) ? Cozy
		: string.Equals(value, Dramatic, StringComparison.OrdinalIgnoreCase) ? Dramatic
		: Balanced;

	/// <summary>每一步改动看法的幅度。</summary>
	public static float Step(string? intensity) => Intensity(intensity) switch
	{
		Cozy => 0.05f,
		Dramatic => 0.14f,
		_ => 0.09f
	};

	/// <summary>两人在关系表里有任何关系（亲友、邻居、夫妻……）。</summary>
	public static bool Close(string a, string b) => VillageRelations.IsKnownPair(a, b);

	/// <summary>原作里的配偶，没有就是空。</summary>
	public static string CanonicalSpouse(string npc)
	{
		foreach ((string a, string b, _) in BuiltInPersonas.Relationships)
		{
			if (!VillageRelations.IsMarriage(a, b))
			{
				continue;
			}
			if (string.Equals(a, npc, StringComparison.OrdinalIgnoreCase))
			{
				return b;
			}
			if (string.Equals(b, npc, StringComparison.OrdinalIgnoreCase))
			{
				return a;
			}
		}
		return "";
	}

	public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

	public static float Toward(float value, float target, float step) =>
		Math.Abs(value - target) <= step ? target : value < target ? value + step : value - step;

	/// <summary>去空、去重、按名字排序。</summary>
	public static List<string> Names(IEnumerable<string>? names) =>
		(names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

	public static List<string> Merge(IEnumerable<string>? current, IEnumerable<string>? more) => Names((current ?? Array.Empty<string>()).Concat(more ?? Array.Empty<string>()));

	public static List<string> Tag(IEnumerable<string>? tags, params string[] more) => Merge(tags, more);

	public static string Trim(string? text, int max = 220)
	{
		string t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
		return t.Length > max ? t[..max] : t;
	}

	/// <summary>由几段文字算出的固定 id（20 位十六进制）。</summary>
	public static string StableId(params string[] parts)
	{
		string s = string.Join("|", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..20].ToLowerInvariant();
	}

	public static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

	public static bool Has(IEnumerable<string> names, string name) => names.Any(n => Same(n, name));
}

/// <summary>看法的立场：从各项数值推出来。</summary>
public static class OpinionRules
{
	public static string Classify(Opinion o)
	{
		if (o.Annoyance >= 0.72f && o.Faith <= 0.25f)
		{
			return Stances.Betrayed;
		}
		if (o.Annoyance >= 0.5f)
		{
			return Stances.Badmouthing;
		}
		if (o.Faith <= 0.28f || o.Annoyance >= 0.36f)
		{
			return Stances.Cold;
		}
		if (o.Doubt >= 0.32f)
		{
			return Stances.Suspicious;
		}
		if (o.Annoyance >= 0.18f)
		{
			return Stances.Annoyed;
		}
		if (o.Fealty >= 0.65f && o.Faith >= 0.52f)
		{
			return Stances.Protective;
		}
		if (o.Esteem >= 0.64f && o.Faith >= 0.58f)
		{
			return Stances.Admiring;
		}
		return Stances.Neutral;
	}

	/// <summary>值得写进提示词的立场。</summary>
	public static bool Notable(Opinion o) => o.Attitude is Stances.Annoyed or Stances.Suspicious or Stances.Cold or Stances.Badmouthing or Stances.Betrayed or Stances.Protective;

	public static float Weight(Opinion o) => Math.Max(o.Annoyance, o.Doubt) + (o.Attitude == Stances.Protective ? 0.05f : 0f);

	/// <summary>两人一开始的看法：有关系的人信任和忠诚高一些。</summary>
	public static Opinion Fresh(string holder, string target)
	{
		bool close = DramaRules.Close(holder, target);
		return new Opinion
		{
			Who = holder,
			About = target,
			Faith = close ? 0.6f : 0.5f,
			Esteem = close ? 0.55f : 0.5f,
			Fealty = close ? 0.7f : 0.45f,
			Attitude = close ? Stances.Protective : Stances.Neutral
		};
	}

	public static void Normalize(Opinion o)
	{
		o.Labels ??= new List<string>();
		o.Origins = DramaRules.Names(o.Origins);
		o.Why ??= "";
		o.Esteem = DramaRules.Clamp01(o.Esteem);
		o.Faith = DramaRules.Clamp01(o.Faith);
		o.Annoyance = DramaRules.Clamp01(o.Annoyance);
		o.Doubt = DramaRules.Clamp01(o.Doubt);
		o.Envy = DramaRules.Clamp01(o.Envy);
		o.Fealty = DramaRules.Clamp01(o.Fealty);
		o.Attitude = Classify(o);
	}

	/// <summary>比较前后有没有变（数值保留三位）。</summary>
	public static string Snapshot(Opinion o) => string.Join("|", o.Esteem.ToString("0.000"), o.Faith.ToString("0.000"), o.Annoyance.ToString("0.000"), o.Doubt.ToString("0.000"), o.Envy.ToString("0.000"), o.Attitude, o.Why, string.Join(",", o.Origins.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)));
}

/// <summary>从一段话里看出提到了谁、是褒是贬、农夫想干什么。</summary>
public static class DramaText
{
	public const string Defend = "defend";
	public const string Leak = "leak";
	public const string Warn = "warn";
	public const string Instigate = "instigate";

	/// <summary>显示名是常用词（矮人、法师）的村民只认内部名，免得普通句子被当成提到了他们。</summary>
	private static readonly HashSet<string> CommonWordNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dwarf", "Wizard" };

	/// <summary>
	/// 提到的村民（内部名）：按内部名（前后不是英文字母）或显示名找；去重、按名字排序。
	/// </summary>
	public static IReadOnlyList<string> Mentioned(string? text, Func<string, string>? display = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<string>();
		}
		return BuiltInPersonas.VanillaNames.Where(n => Contains(text, n) || (display != null && !CommonWordNames.Contains(n) && ContainsDisplay(text, display(n), n)))
			.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static int Sentiment(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return 0;
		}
		string t = " " + text.ToLowerInvariant() + " ";
		int bad = Keywords.DramaNegative.Count(k => t.Contains(k, StringComparison.OrdinalIgnoreCase));
		int good = Keywords.DramaPositive.Count(k => t.Contains(k, StringComparison.OrdinalIgnoreCase));
		return bad > good ? -1 : good > bad ? 1 : 0;
	}

	public static string Action(string text, int sentiment)
	{
		if (sentiment > 0)
		{
			return Defend;
		}
		if (sentiment == 0)
		{
			return "";
		}
		string t = " " + text.ToLowerInvariant() + " ";
		if (Keywords.DramaLeak.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)))
		{
			return Leak;
		}
		return Keywords.DramaAttack.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)) ? Warn : Instigate;
	}

	private static bool Contains(string text, string name) =>
		!string.IsNullOrWhiteSpace(name) && Regex.IsMatch(text, "(?<![A-Za-z])" + Regex.Escape(name) + "(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static bool ContainsDisplay(string text, string? shown, string name)
	{
		if (string.IsNullOrWhiteSpace(shown) || DramaRules.Same(shown, name))
		{
			return false;
		}
		string s = shown.Trim();
		return s.Any(c => c > 0x2E80) ? text.Contains(s, StringComparison.Ordinal) : Contains(text, s);
	}
}

/// <summary>传闻的规则：从八卦或农夫的话生成、合并、每天传开、信不信。</summary>
public static class RumorRules
{
	public const string Private = "private";
	public const string Circle = "circle";
	public const string Town = "town";

	public const string Farmer = "Farmer";

	public static Rumor FromGossip(string text, string season, int day, int year, string origin, string heardFrom, IEnumerable<string>? aware, IEnumerable<string>? mods, int sentiment, IReadOnlyList<string> targets, int totalDay, string intensity)
	{
		bool dramatic = DramaRules.Intensity(intensity) == DramaRules.Dramatic;
		List<string> knows = DramaRules.Names(aware);
		if (!string.IsNullOrWhiteSpace(origin))
		{
			knows.Insert(0, origin.Trim());
		}
		knows = knows.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
		float charge = (sentiment == 0 ? 0.25f : 0.55f) + (dramatic ? 0.12f : 0f);
		float damage = (sentiment < 0 ? 0.45f : sentiment > 0 ? 0.1f : 0.2f) + (dramatic ? (sentiment < 0 ? 0.12f : 0.03f) : 0f);
		Rumor r = new Rumor
		{
			Id = DramaRules.StableId("gossip", text, season, day.ToString(), year.ToString()),
			ComesFrom = DramaRules.StableId("gossip-entry", text, season, day.ToString(), year.ToString()),
			Subject = DramaRules.Trim(text),
			About = DramaRules.Names(targets).Take(4).ToList(),
			Source = origin ?? "",
			Carrier = heardFrom ?? "",
			Believability = DramaRules.Clamp01(0.68f + (string.IsNullOrWhiteSpace(origin) ? -0.08f : 0.08f)),
			Warp = InitialDistortion(sentiment, intensity),
			Heat = DramaRules.Clamp01(charge),
			Harm = DramaRules.Clamp01(damage),
			Veracity = sentiment == 0 ? "unknown" : "partial",
			BornDay = totalDay,
			MovedDay = totalDay,
			DiesDay = totalDay + Lifetime(sentiment, intensity),
			Heard = knows,
			Origins = DramaRules.Names(mods),
			Labels = new List<string> { "gossip", sentiment < 0 ? "negative" : sentiment > 0 ? "positive" : "neutral" }
		};
		Refresh(r);
		return r;
	}

	public static Rumor FromInfluence(Influence a, string intensity)
	{
		float distortion = (a.Move == DramaText.Leak ? 0.38f : 0.28f) + (a.Upshot == InfluenceRules.Suspected ? 0.12f : 0f);
		Rumor r = new Rumor
		{
			Id = DramaRules.StableId("player-influence", a.Id),
			ComesFrom = a.Id,
			Subject = DramaRules.Trim(a.Subject),
			About = DramaRules.Names(new[] { a.Aim }),
			Source = Farmer,
			Carrier = a.Talker,
			Believability = DramaRules.Clamp01(0.25f + a.Uptake * 0.65f),
			Warp = DramaRules.Clamp01(distortion),
			Heat = DramaRules.Clamp01(0.45f + a.Uptake * 0.25f),
			Harm = DramaRules.Clamp01(a.Slant < 0 ? 0.52f : 0.12f),
			Veracity = a.Move == DramaText.Leak ? "unknown" : "partial",
			Spread = Private,
			Heard = DramaRules.Names(new[] { a.Talker }),
			BornDay = a.OnDay,
			MovedDay = a.OnDay,
			DiesDay = a.OnDay + Lifetime(a.Slant, intensity),
			Origins = DramaRules.Names(a.Origins),
			Labels = new List<string> { "player_influence", a.Move }
		};
		Refresh(r);
		return r;
	}

	public static void Normalize(Rumor r)
	{
		r.About = DramaRules.Names(r.About);
		r.Heard = DramaRules.Names(r.Heard);
		r.Convinced = DramaRules.Names(r.Convinced);
		r.Doubters = DramaRules.Names(r.Doubters);
		r.Labels = DramaRules.Names(r.Labels);
		r.Origins = DramaRules.Names(r.Origins);
		r.Believability = DramaRules.Clamp01(r.Believability);
		r.Warp = DramaRules.Clamp01(r.Warp);
		r.Heat = DramaRules.Clamp01(r.Heat);
		r.Harm = DramaRules.Clamp01(r.Harm);
		r.Subject = DramaRules.Trim(r.Subject);
		r.Source ??= "";
		r.Carrier ??= "";
		r.Spread = ReachOf(r);
		if (r.DiesDay <= 0)
		{
			r.DiesDay = r.BornDay + Lifetime(0, DramaRules.Balanced);
		}
		Beliefs(r);
	}

	/// <summary>这一天传给更多人；有新人知道了返回 true。</summary>
	public static bool SpreadForDay(Rumor r, int today, string intensity, IEnumerable<string> everyone)
	{
		Normalize(r);
		if (today <= r.MovedDay || today > r.DiesDay || r.Heard.Count == 0)
		{
			return false;
		}
		HashSet<string> aware = new HashSet<string>(r.Heard, StringComparer.OrdinalIgnoreCase);
		List<string> fresh = Candidates(aware, everyone).Where(n => !aware.Contains(n)).Take(SpreadCount(r, intensity)).ToList();
		bool added = false;
		foreach (string n in fresh)
		{
			added |= aware.Add(n);
		}
		r.MovedDay = today;
		if (!added)
		{
			return false;
		}
		r.Heard = aware.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		r.Carrier = SpreaderOf(r);
		r.Warp = DramaRules.Clamp01(r.Warp + DistortionGrowth(r, intensity));
		Refresh(r);
		return true;
	}

	/// <summary>把新的一条并进已有的：名单合并，可信度、情绪、伤害、过期日取大，失真取平均。</summary>
	public static void MergeInto(Rumor current, Rumor add)
	{
		if (string.IsNullOrWhiteSpace(current.Subject))
		{
			current.Subject = add.Subject;
		}
		current.About = DramaRules.Merge(current.About, add.About);
		current.Heard = DramaRules.Merge(current.Heard, add.Heard);
		current.Origins = DramaRules.Merge(current.Origins, add.Origins);
		current.Labels = DramaRules.Merge(current.Labels, add.Labels);
		current.Believability = Math.Max(current.Believability, add.Believability);
		current.Warp = Math.Clamp((current.Warp + add.Warp) / 2f, 0f, 1f);
		current.Heat = Math.Max(current.Heat, add.Heat);
		current.Harm = Math.Max(current.Harm, add.Harm);
		current.DiesDay = Math.Max(current.DiesDay, add.DiesDay);
		if (!string.IsNullOrWhiteSpace(add.Carrier))
		{
			current.Carrier = add.Carrier;
		}
		if (!string.IsNullOrWhiteSpace(add.Source))
		{
			current.Source = add.Source;
		}
		Normalize(current);
	}

	public static bool KnownBy(Rumor r, string npc) => DramaRules.Has(r.Heard, npc);

	public static bool Believes(Rumor r, string npc) => DramaRules.Has(r.Convinced, npc);

	public static bool Doubts(Rumor r, string npc) => DramaRules.Has(r.Doubters, npc);

	public static string Snapshot(Rumor r)
	{
		static string L(IEnumerable<string> l) => string.Join(",", l.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
		return string.Join("|", r.Subject, L(r.About), L(r.Heard), L(r.Convinced), L(r.Doubters), L(r.Origins), r.Believability.ToString("0.000"), r.Warp.ToString("0.000"), r.Heat.ToString("0.000"), r.Harm.ToString("0.000"), r.Spread, r.DiesDay.ToString());
	}

	private static void Refresh(Rumor r)
	{
		r.Spread = ReachOf(r);
		Beliefs(r);
	}

	private static void Beliefs(Rumor r)
	{
		bool believed = DramaRules.Clamp01(r.Believability - r.Warp * 0.45f) >= 0.52f;
		r.Convinced = believed ? DramaRules.Names(r.Heard) : new List<string>();
		r.Doubters = believed ? new List<string>() : DramaRules.Names(r.Heard);
	}

	private static string ReachOf(Rumor r) => r.Heard.Count >= 10 ? Town : r.Heard.Count >= 4 ? Circle : Private;

	private static IEnumerable<string> Candidates(HashSet<string> aware, IEnumerable<string> everyone)
	{
		HashSet<string> linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string a, string b, _) in BuiltInPersonas.Relationships)
		{
			if (aware.Contains(a))
			{
				linked.Add(b);
			}
			if (aware.Contains(b))
			{
				linked.Add(a);
			}
		}
		return linked.Concat(everyone).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
	}

	private static int SpreadCount(Rumor r, string intensity)
	{
		int n = DramaRules.Intensity(intensity) switch { DramaRules.Cozy => 1, DramaRules.Dramatic => 3, _ => 2 };
		return r.Heat >= 0.65f || r.Harm >= 0.55f ? n + 1 : n;
	}

	private static float InitialDistortion(int sentiment, string intensity)
	{
		float d = sentiment < 0 ? 0.22f : 0.12f;
		string i = DramaRules.Intensity(intensity);
		d += i == DramaRules.Dramatic ? 0.12f : i == DramaRules.Cozy ? -0.05f : 0f;
		return DramaRules.Clamp01(d);
	}

	private static float DistortionGrowth(Rumor r, string intensity)
	{
		float g = DramaRules.Intensity(intensity) switch { DramaRules.Cozy => 0.015f, DramaRules.Dramatic => 0.055f, _ => 0.035f };
		return r.Carrier is "Haley" or "Pierre" or "Caroline" ? g * 1.35f : g;
	}

	private static int Lifetime(int sentiment, string intensity)
	{
		int days = sentiment < 0 ? 18 : 10;
		return DramaRules.Intensity(intensity) switch
		{
			DramaRules.Cozy => Math.Max(5, days - 4),
			DramaRules.Dramatic => days + 8,
			_ => days
		};
	}

	private static string SpreaderOf(Rumor r)
	{
		if (!string.IsNullOrWhiteSpace(r.Carrier))
		{
			return r.Carrier;
		}
		if (!string.IsNullOrWhiteSpace(r.Source) && r.Source != Farmer)
		{
			return r.Source;
		}
		return r.Heard.FirstOrDefault() ?? "";
	}
}

/// <summary>农夫的话有多大分量。</summary>
public static class InfluenceRules
{
	public const string Rejected = "rejected";
	public const string Suspected = "suspected";
	public const string Accepted = "accepted";

	public static float Acceptance(int hearts, int? points, string? emotion)
	{
		float bond = 0.15f + Math.Clamp(hearts, 0, 10) * 0.075f;
		if (points is not int said)
		{
			return bond;
		}
		float belief = bond * 0.75f + Math.Clamp(said, -100, 100) / 100f * 0.25f;
		belief *= (emotion ?? "").Trim().ToLowerInvariant() switch
		{
			"angry" or "sad" => 0.55f,
			"happy" or "love" => 1.15f,
			_ => 1f
		};
		return Math.Clamp(belief, 0f, 1f);
	}

	public static string Result(float acceptance) => acceptance < 0.25f ? Rejected : acceptance < 0.45f ? Suspected : Accepted;
}

/// <summary>成年村民和原作配偶之间的紧张。</summary>
public static class AdultRules
{
	public const string Strained = "strained_marriage";
	public const string Tempted = "tempted";
	public const string Contemplating = "affair_contemplation";

	public sealed record Gates(bool Enabled, bool AllowInfidelity, bool ProtectMarriages, string Intensity);

	/// <summary>看法变了以后检查：对象正好是原作配偶、够紧张时记一份；有变化返回 true。</summary>
	public static bool Apply(List<AdultStrain> list, Opinion o, string source, int day, Gates gates, IEnumerable<string>? mods = null, Func<string, string>? name = null)
	{
		name ??= n => n;
		if (!gates.Enabled || VillageRelations.IsKnownChild(o.Who) || VillageRelations.IsKnownChild(o.About))
		{
			return false;
		}
		string spouse = DramaRules.CanonicalSpouse(o.Who);
		if (spouse.Length == 0 || !DramaRules.Same(spouse, o.About))
		{
			return false;
		}
		float level = Math.Max(o.Annoyance, o.Doubt);
		if (level < (DramaRules.Intensity(gates.Intensity) == DramaRules.Dramatic ? 0.12f : 0.18f))
		{
			return false;
		}
		AdultStrain s = list.FirstOrDefault(x => DramaRules.Same(x.Villager, o.Who) && DramaRules.Same(x.Spouse, spouse)) ?? Add(list, o.Who, spouse, day);
		string before = Snapshot(s);
		s.Origins = DramaRules.Merge(s.Origins, mods);
		s.Force = DramaRules.Clamp01(Math.Max(s.Force, level));
		s.Remorse = DramaRules.Clamp01(Math.Max(s.Remorse, 0.45f + level * 0.35f));
		s.Why = o.Why;
		s.MovedDay = day;
		s.DiesDay = Math.Max(s.DiesDay, day + 21);
		s.Labels = DramaRules.Tag(s.Labels, "adult_drama", "marital_strain", source);
		if (gates.AllowInfidelity && !gates.ProtectMarriages && source == DramaSources.Player)
		{
			s.Third = RumorRules.Farmer;
			s.Pull = DramaRules.Clamp01(Math.Max(s.Pull, level * 0.9f));
			s.Limit = DramaRules.Clamp01(Math.Min(s.Limit, 0.75f - level * 0.35f));
			s.Phase = s.Pull >= 0.42f ? Contemplating : Tempted;
			s.Gist = $"{name(o.Who)}为婚姻里的别扭和农夫那份危险的关注暗自挣扎。";
			s.Labels = DramaRules.Tag(s.Labels, "temptation", "farmer");
		}
		else
		{
			s.Phase = Strained;
			s.Limit = DramaRules.Clamp01(Math.Max(s.Limit, 0.72f));
			s.Gist = $"{name(o.Who)}和{name(spouse)}之间有成年人的别扭；除非开关允许，不往出轨方向走。";
		}
		return Snapshot(s) != before;
	}

	public static void Normalize(AdultStrain s)
	{
		s.Labels ??= new List<string>();
		s.Force = DramaRules.Clamp01(s.Force);
		s.Pull = DramaRules.Clamp01(s.Pull);
		s.Remorse = DramaRules.Clamp01(s.Remorse);
		s.Limit = DramaRules.Clamp01(s.Limit <= 0f ? 0.75f : s.Limit);
		s.Phase = string.IsNullOrWhiteSpace(s.Phase) ? "none" : s.Phase;
		s.Gist ??= "";
		s.Why ??= "";
		s.Origins = DramaRules.Names(s.Origins);
	}

	private static AdultStrain Add(List<AdultStrain> list, string npc, string spouse, int day)
	{
		AdultStrain s = new AdultStrain
		{
			Id = (npc + "_" + spouse + "_adult_drama").ToLowerInvariant().Replace(' ', '_'),
			Villager = npc,
			Spouse = spouse,
			BornDay = day,
			MovedDay = day,
			DiesDay = day + 21,
			Phase = Strained
		};
		list.Add(s);
		return s;
	}

	private static string Snapshot(AdultStrain s) => string.Join("|", s.Phase, s.Force.ToString("0.000"), s.Pull.ToString("0.000"), s.Remorse.ToString("0.000"), s.Limit.ToString("0.000"), s.Gist, s.Why, string.Join(",", s.Origins.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)), s.DiesDay.ToString());
}

public static class DramaSources
{
	public const string Gossip = "gossip";
	public const string Player = "player_influence";
}
