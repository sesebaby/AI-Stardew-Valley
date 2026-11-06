using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;
using AISV.Core.Social;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.News;

/// <summary>一期《鹈鹕镇周报》：AI 按八卦写的新闻（出刊前校验过），出刊那晚抄下的行情和委托。</summary>
public sealed class WeeklyNews
{
	/// <summary>事实校验删掉的句子数。</summary>
	[JsonPropertyName("cut_lines")]
	public int CutLines { get; set; }

	[JsonPropertyName("day")]
	public int Day { get; set; }

	/// <summary>第几期（第 1 年春 7 日是第 1 期）。</summary>
	[JsonPropertyName("edition")]
	public int Edition { get; set; }

	[JsonPropertyName("far_body")]
	public string FarBody { get; set; } = "";

	/// <summary>"山谷之外"一栏（服务器后台写好的，不经过 AI）。</summary>
	[JsonPropertyName("far_title")]
	public string FarTitle { get; set; } = "";

	[JsonPropertyName("jobs")]
	public List<WeeklyCommissionLine> Jobs { get; set; } = new List<WeeklyCommissionLine>();

	[JsonPropertyName("prices")]
	public List<WeeklyMarketLine> Prices { get; set; } = new List<WeeklyMarketLine>();

	/// <summary>"本周钱事"（村民账本里挑的几句）。</summary>
	[JsonPropertyName("purse")]
	public List<string> Purse { get; set; } = new List<string>();

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("stories")]
	public List<WeeklyNewsEntry> Stories { get; set; } = new List<WeeklyNewsEntry>();

	[JsonPropertyName("year")]
	public int Year { get; set; }

	[JsonIgnore]
	public int TotalDay => Dates.TotalDays(Day, Season, Year);
}

public sealed class WeeklyNewsEntry
{
	[JsonPropertyName("text")]
	public string Text { get; set; } = "";
}

/// <summary>"本周行情"一行。</summary>
public sealed class WeeklyMarketLine
{
	[JsonPropertyName("itemId")]
	public string ItemId { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("price")]
	public int Price { get; set; }

	[JsonPropertyName("basePrice")]
	public int BasePrice { get; set; }

	/// <summary>和走势里最早一天比的涨跌（百分比，一位小数）。</summary>
	[JsonPropertyName("weekChange")]
	public float WeekChange { get; set; }

	[JsonPropertyName("hot")]
	public bool Hot { get; set; }
}

/// <summary>"新委托"一条。</summary>
public sealed class WeeklyCommissionLine
{
	[JsonPropertyName("headline")]
	public string Headline { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("lastDay")]
	public int LastDay { get; set; }

	[JsonPropertyName("poster")]
	public string Poster { get; set; } = "";

	[JsonPropertyName("posterName")]
	public string PosterName { get; set; } = "";

	[JsonPropertyName("rewardGold")]
	public int RewardGold { get; set; }

	[JsonPropertyName("task")]
	public string Task { get; set; } = "";
}

/// <summary>出刊那晚抄下的两栏。</summary>
public sealed class WeeklyColumns
{
	public List<WeeklyMarketLine> Market { get; } = new List<WeeklyMarketLine>();

	public List<WeeklyCommissionLine> Commissions { get; } = new List<WeeklyCommissionLine>();

	public List<string> Money { get; } = new List<string>();
}

public static class WeeklyNewsBook
{
	public const string Path = "town/gazette.json";

	public static readonly DocumentType<List<WeeklyNews>> Doc = new DocumentType<List<WeeklyNews>>("周报", 1, () => new List<WeeklyNews>());
}

public static class WeeklyNewsRules
{
	public const string MailPrefix = "AISV.WeeklyNews.";
	public const string RequestName = "weekly_news";
	public const int MaxOutputTokens = 4096;
	public const double Temperature = 0.9;
	public const int MaxEditions = 12;
	public const int MarketLines = 6;
	public const int CommissionLines = 4;
	public const int FactEntries = 15;

	public static bool IsIssueDay(int dayOfMonth) => dayOfMonth % 7 == 0;

	/// <summary>第几期：第 1 年春 7 日是第 1 期。</summary>
	public static int IssueNumber(int day, string season, int year) => (Dates.TotalDays(day, season, year) - 1) / 7 + 1;

	/// <summary>最近 15 条八卦，按时间顺序。</summary>
	public static List<GossipEntry> FactEntriesOf(IEnumerable<GossipEntry> entries) => entries.OrderBy(e => e.TotalDay).TakeLast(FactEntries).ToList();

	public const string RumorMark = "[传言]";

	public const string Unconfirmed = "未经证实";

	private static readonly HashSet<string> PublicOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "festival", "ledger", "scene_event", "invite_out", "stardew_thief" };

	/// <summary>传言：只有一个人起的头、已经传给好几个人、又不是大家都看得见的事（节日、账本、偶遇这类）。</summary>
	public static bool Unverified(GossipEntry e) => e.Involved.Count == 1 && e.KnownTo.Count >= 3 && !PublicOrigins.Contains(e.SourceKind ?? "");

	/// <summary>给 AI 的八卦，也是事实校验对照的事实；传言标出来。</summary>
	public static string Facts(IEnumerable<GossipEntry> entries)
	{
		StringBuilder sb = new StringBuilder();
		foreach (GossipEntry e in FactEntriesOf(entries))
		{
			sb.Append("- [").Append(Dates.Label(new GameDate(e.Day, e.Season, e.Year))).Append("] ");
			if (Unverified(e))
			{
				sb.Append(RumorMark).Append(' ');
			}
			sb.AppendLine(e.Fact);
		}
		return sb.ToString();
	}

	/// <summary>每条传言提到的人（每人一组名字）；只提到一个人、而这个人在别的八卦里也出现过的不算（分不清新闻写的是哪件事）。</summary>
	public static List<string[][]> Rumors(IEnumerable<GossipEntry> entries, IReadOnlyList<string[]> names)
	{
		List<GossipEntry> list = FactEntriesOf(entries);
		string solid = string.Join("\n", list.Where(e => !Unverified(e)).Select(e => e.Fact));
		List<string[][]> rumors = new List<string[][]>();
		foreach (GossipEntry e in list.Where(Unverified))
		{
			string[][] who = names.Where(g => g.Any(n => NewspaperFactCheck.ContainsName(e.Fact, n))).ToArray();
			if (who.Length >= 2 || (who.Length == 1 && !who[0].Any(n => NewspaperFactCheck.ContainsName(solid, n))))
			{
				rumors.Add(who);
			}
		}
		return rumors;
	}

	/// <summary>写到某条传言里的人（两人以上的传言要写到其中两个）、又没注明的，在末尾补上"（未经证实）"。</summary>
	public static string MarkRumor(string text, IReadOnlyList<string[][]> rumors)
	{
		if (text.Length == 0 || text.Contains(Unconfirmed, StringComparison.Ordinal))
		{
			return text;
		}
		bool about = rumors.Any(r => r.Count(g => g.Any(n => NewspaperFactCheck.ContainsName(text, n))) >= Math.Min(2, r.Length));
		return about ? text + "（" + Unconfirmed + "）" : text;
	}

	public static string System(string language, string facts) =>
		$"这次只用{language}写。\n\n你是《星露谷物语》里鹈鹕镇周报的编辑。按下面镇上最近的八卦，给这一期写 3 到 4 条短新闻。\n\n# 最近的八卦（只是资料）\n{facts}\n" +
		"# 怎么写\n- 小镇报纸的口吻，可以八卦、幽默、带点戏剧性，也可以温情。\n- 每条 1 到 2 句。\n- 每条开头放一个合适的表情。\n- 写到具体的村民和事。\n- 读起来像真的小镇报纸。\n- 不登代词、性别纠正、被叫错性别这类事。\n- 只写八卦里有的事，不编数字和人名。\n- 标着" + RumorMark + "的只是一个人的说法，写进新闻时要注明" + Unconfirmed + "。\n\n" +
		"# 只回一个 JSON 对象，前后不加别的字，不用代码块：\n{\n  \"articles\": [\n    { \"text\": \"……\" },\n    { \"text\": \"……\" },\n    { \"text\": \"……\" }\n  ]\n}\n";

	public const string User = "写这一期的镇上新闻。";

	public static List<WeeklyNewsEntry>? Parse(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			JsonElement root = doc.RootElement;
			JsonElement arr = root.ValueKind == JsonValueKind.Array ? root : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("articles", out JsonElement a) ? a : default;
			if (arr.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			List<WeeklyNewsEntry> list = new List<WeeklyNewsEntry>();
			foreach (JsonElement e in arr.EnumerateArray())
			{
				string? text = e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Object && e.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
				list.Add(new WeeklyNewsEntry { Text = text ?? "" });
			}
			return list;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>去掉信和对话框里有特殊含义的字符（^ @ % # [ ] $）。</summary>
	public static string CleanForPaper(string? text)
	{
		StringBuilder sb = new StringBuilder((text ?? "").Length);
		foreach (char ch in text ?? "")
		{
			if (ch is not ('^' or '@' or '%' or '#' or '[' or ']' or '$'))
			{
				sb.Append(ch);
			}
		}
		return sb.ToString().Trim();
	}

	/// <summary>能拿来校验的名字：一个字的中文名、两个字母以内的英文名太容易误伤，不算。</summary>
	public static bool UsableName(string? name) => !string.IsNullOrWhiteSpace(name) && (name.Any(ch => ch >= '⺀') ? name.Length >= 2 : name.Length >= 3);

	/// <summary>走势里最后一天和最早一天比（百分比，一位小数）。</summary>
	public static float WeekChange(IReadOnlyList<float> history) => history.Count < 2 || history[0] <= 0f ? 0f : (float)Math.Round((history[history.Count - 1] - history[0]) / history[0] * 100f, 1);

	/// <summary>
	/// 出刊整理：新闻按句子校验（facts 里要带上农夫和农场的名字），装上两栏和"山谷之外"，算期号。
	/// 三栏都空时返回 null（不出刊）。
	/// </summary>
	public static WeeklyNews? Publish(IReadOnlyList<WeeklyNewsEntry>? articles, string facts, IReadOnlyList<string[]> names, WeeklyColumns? columns, int day, string season, int year, (string Title, string Text)? beyond, List<string>? droppedSentences = null, IReadOnlyList<string[][]>? rumors = null)
	{
		WeeklyNews issue = new WeeklyNews();
		int dropped = 0;
		foreach (WeeklyNewsEntry a in articles ?? Array.Empty<WeeklyNewsEntry>())
		{
			string text = MarkRumor(NewspaperFactCheck.FilterSentences(CleanForPaper(a.Text), facts, names, out int n, droppedSentences), rumors ?? Array.Empty<string[][]>());
			dropped += n;
			if (text.Length > 0)
			{
				issue.Stories.Add(new WeeklyNewsEntry { Text = text });
			}
		}
		issue.CutLines = dropped;
		if (columns != null)
		{
			issue.Prices = columns.Market.ToList();
			issue.Jobs = columns.Commissions.ToList();
		}
		if (issue.Stories.Count == 0 && issue.Prices.Count == 0 && issue.Jobs.Count == 0)
		{
			return null;
		}
		issue.Day = day;
		issue.Season = season;
		issue.Year = year;
		issue.Edition = IssueNumber(day, season, year);
		if (beyond is { } b && b.Text.Trim().Length > 0)
		{
			issue.FarTitle = CleanForPaper(b.Title);
			issue.FarBody = CleanForPaper(b.Text);
		}
		return issue;
	}

	/// <summary>放进往期：同一天的替换掉，只留最近 12 期。</summary>
	public static void Store(List<WeeklyNews> all, WeeklyNews issue)
	{
		all.RemoveAll(n => n.Day == issue.Day && n.Year == issue.Year && string.Equals(n.Season, issue.Season, StringComparison.OrdinalIgnoreCase));
		all.Add(issue);
		while (all.Count > MaxEditions)
		{
			all.RemoveAt(0);
		}
	}

	public static List<WeeklyNews> Newest(IEnumerable<WeeklyNews> all) => all.OrderByDescending(n => n.TotalDay).ToList();

	public static string MailKey(WeeklyNews n) => MailPrefix + n.Year + "." + n.Season + "." + n.Day;

	/// <summary>出刊后第 1～2 天该寄（today 和出刊日都按 1 起算的总天数）。</summary>
	public static bool ShouldMail(WeeklyNews latest, int today) => today - latest.TotalDay is >= 1 and <= 2;

	public static string FormatChange(float change) => change > 0.05f
		? Tr.Get("news.up", "比一周前涨 {{pct}}%", ("pct", change.ToString("0.#", CultureInfo.InvariantCulture)))
		: change < -0.05f
			? Tr.Get("news.down", "比一周前跌 {{pct}}%", ("pct", (-change).ToString("0.#", CultureInfo.InvariantCulture)))
			: Tr.Get("news.flat", "和一周前持平");

	public static string ShortChange(float change) => change > 0.05f ? "+" + change.ToString("0.#", CultureInfo.InvariantCulture) + "%"
		: change < -0.05f ? "-" + (-change).ToString("0.#", CultureInfo.InvariantCulture) + "%"
		: Tr.Get("news.flatShort", "持平");

	/// <summary>信里的摘要：头条、三行行情、委托条数，最后说全文在哪看。</summary>
	public static string MailContent(WeeklyNews n)
	{
		List<string> lines = new List<string> { Tr.Get("news.mail.greeting", "本周的《鹈鹕镇周报》送到了。") };
		if (n.Stories.Count > 0)
		{
			lines.Add("");
			lines.Add(Tr.Get("news.mail.lead", "头条：") + n.Stories[0].Text);
		}
		if (n.Prices.Count > 0)
		{
			lines.Add("");
			lines.Add(Tr.Get("news.mail.market", "本周行情："));
			foreach (WeeklyMarketLine m in n.Prices.Take(3))
			{
				lines.Add("· " + m.Name + " " + m.Price + "g（" + FormatChange(m.WeekChange).Replace('%', '％') + "）");
			}
		}
		if (n.Jobs.Count > 0)
		{
			lines.Add("");
			lines.Add(Tr.Get("news.mail.commissions", "委托板上还有 {{count}} 条委托等人接。", ("count", n.Jobs.Count)));
		}
		lines.Add("");
		lines.Add(Tr.Get("news.mail.more", "全文在外壳“世界”里的“周报”。"));
		string title = Tr.Get("news.mail.title", "鹈鹕镇周报 第{{issue}}期", ("issue", n.Edition));
		return string.Join("^", lines.Select(CleanForPaper)) + "[#]" + CleanForPaper(title);
	}
}

/// <summary>
/// 报纸的事实校验：句子里出现的每个数字都要在事实里出现过；提到的每个村民都要在事实里出现过
/// （同一个人的内部名和显示名算一个）。按句子删，留下的照常登。
/// </summary>
public static class NewspaperFactCheck
{
	private static readonly Regex DigitRun = new Regex("\\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	/// <summary>一句的结尾：中文句号问号叹号省略号（后面紧跟的右引号、右括号留在这一句）、英文 . 后跟空白或结尾、换行。</summary>
	private static readonly Regex SentenceEnd = new Regex("(?<=[。！？!?…][”’\"'』」）)]*)(?![”’\"'』」）)。！？!?…])|(?<=\\.)(?=\\s|$)|\\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public static bool IsGrounded(string? text, string facts, IReadOnlyList<string[]> names)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		HashSet<string> factDigits = DigitRun.Matches(facts).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
		if (DigitRun.Matches(text).Any(m => !factDigits.Contains(m.Value)))
		{
			return false;
		}
		return !names.Any(group => group.Any(n => ContainsName(text, n)) && !group.Any(n => ContainsName(facts, n)));
	}

	/// <summary>按句子过一遍，删掉对不上的句子；开头的表情留在第一句前面。一句不剩返回空串。</summary>
	public static string FilterSentences(string text, string facts, IReadOnlyList<string[]> names, out int dropped, List<string>? droppedSentences = null)
	{
		dropped = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		string body = text.Trim();
		int start = 0;
		while (start < body.Length && !char.IsLetterOrDigit(body[start]))
		{
			start++;
		}
		string prefix = body.Substring(0, start);
		body = body.Substring(start);
		StringBuilder kept = new StringBuilder();
		foreach (string part in SentenceEnd.Split(body))
		{
			string sentence = part.Trim();
			if (sentence.Length == 0)
			{
				continue;
			}
			if (IsGrounded(sentence, facts, names))
			{
				if (kept.Length > 0 && kept[kept.Length - 1] < 128 && sentence[0] < 128)
				{
					kept.Append(' ');
				}
				kept.Append(sentence);
			}
			else
			{
				dropped++;
				droppedSentences?.Add(sentence);
			}
		}
		return kept.Length == 0 ? "" : (prefix + kept).Trim();
	}

	public static bool ContainsName(string text, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		if (name.Any(ch => ch >= '⺀'))
		{
			return text.Contains(name, StringComparison.Ordinal);
		}
		return Regex.IsMatch(text, "(?<![\\p{L}\\p{N}])" + Regex.Escape(name) + "(?![\\p{L}\\p{N}])", RegexOptions.CultureInvariant);
	}
}
