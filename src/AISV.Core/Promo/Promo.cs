using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AISV.Core.Storage;

namespace AISV.Core.Promo;

/// <summary>推荐页的一条。</summary>
public sealed class PromoItem
{
	public long Id { get; init; }

	public string Title { get; init; } = "";

	public string Body { get; init; } = "";

	public string Image { get; init; } = "";

	public string LinkText { get; init; } = "";

	public string LinkUrl { get; init; } = "";
}

/// <summary>"支持作者"任务：点链接就算完成。</summary>
public sealed class SupportTask
{
	public long Id { get; init; }

	public string Title { get; init; } = "";

	public string Platform { get; init; } = "";

	public string Url { get; init; } = "";

	public string Text { get; init; } = "";

	public string Poster { get; init; } = "";

	public string RewardItem { get; init; } = "";

	public int RewardQty { get; init; } = 1;

	public string Created { get; init; } = "";
}

/// <summary>推广活动：后台填了作品名才推。</summary>
public sealed class PromoCampaign
{
	public const string DefaultAuthor = "远古牛哥";

	public const string DefaultPaperTitle = "山谷之外";

	public string AuthorName { get; init; } = DefaultAuthor;

	public string GameName { get; init; } = "";

	public string Tagline { get; init; } = "";

	public string Url { get; init; } = "";

	public string Image { get; init; } = "";

	public bool PaperEnabled { get; init; } = true;

	public int PaperEveryIssues { get; init; } = 4;

	public string PaperTitle { get; init; } = DefaultPaperTitle;

	public string PaperText { get; init; } = "";

	public bool GossipEnabled { get; init; } = true;

	public int GossipEveryDays { get; init; } = 7;

	public bool GroupEnabled { get; init; } = true;

	public int GroupEveryDays { get; init; } = 7;

	public bool PelicanEnabled { get; init; } = true;

	public int PelicanEveryDays { get; init; } = 14;

	public bool ArcEnabled { get; init; } = true;

	public int ArcEverySeasons { get; init; } = 2;

	public bool LetterEnabled { get; init; } = true;

	public int LetterEveryDays { get; init; } = 84;

	public string LetterText { get; init; } = "";

	public bool HasGame => GameName.Trim().Length > 0;

	public string Game => GameName.Trim().Trim('《', '》');

	public string Author => AuthorName.Trim().Length > 0 ? AuthorName.Trim() : DefaultAuthor;

	public string Line => Tagline.Trim().TrimEnd('。', '.', '！', '!');
}

/// <summary>服务器后台编辑的内容：公告、推荐、推广活动、支持任务。</summary>
public sealed class ServerContent
{
	public static readonly ServerContent Empty = new ServerContent();

	public const int MaxImageBytes = 3 * 1024 * 1024;

	public string Version { get; init; } = "";

	public string Announcement { get; init; } = "";

	public IReadOnlyList<PromoItem> Promo { get; init; } = Array.Empty<PromoItem>();

	public IReadOnlyList<SupportTask> Support { get; init; } = Array.Empty<SupportTask>();

	public PromoCampaign Campaign { get; init; } = new PromoCampaign();

	public static ServerContent Parse(JsonObject o)
	{
		JsonObject c = o["campaign"] as JsonObject ?? new JsonObject();
		return new ServerContent
		{
			Version = o["version"]?.ToString() ?? "",
			Announcement = (o["announcement"]?.ToString() ?? "").Trim(),
			Promo = Objects(o["promo"]).Select(p => new PromoItem
			{
				Id = Long(p, "id"),
				Title = Str(p, "title"),
				Body = Str(p, "body"),
				Image = Str(p, "image"),
				LinkText = Str(p, "linkText"),
				LinkUrl = SafeUrl(Str(p, "linkUrl"))
			}).ToList(),
			Support = Objects(o["support"]).Select(s => new SupportTask
			{
				Id = Long(s, "id"),
				Title = Str(s, "title"),
				Platform = Str(s, "platform"),
				Url = SafeUrl(Str(s, "url")),
				Text = Str(s, "text"),
				Poster = Str(s, "poster"),
				RewardItem = Str(s, "rewardItem"),
				RewardQty = (int)Math.Clamp(Long(s, "rewardQty"), 1, 999),
				Created = Str(s, "created")
			}).Where(s => s.Url.Length > 0).ToList(),
			Campaign = new PromoCampaign
			{
				AuthorName = Str(c, "authorName", PromoCampaign.DefaultAuthor),
				GameName = Str(c, "gameName"),
				Tagline = Str(c, "tagline"),
				Url = SafeUrl(Str(c, "url")),
				Image = Str(c, "image"),
				PaperEnabled = Bool(c, "paperEnabled", true),
				PaperEveryIssues = (int)Math.Clamp(Long(c, "paperEveryIssues", 4), 1, 52),
				PaperTitle = Str(c, "paperTitle", PromoCampaign.DefaultPaperTitle),
				PaperText = Str(c, "paperText"),
				GossipEnabled = Bool(c, "gossipEnabled", true),
				GossipEveryDays = (int)Math.Clamp(Long(c, "gossipEveryDays", 7), 1, 112),
				GroupEnabled = Bool(c, "groupEnabled", true),
				GroupEveryDays = (int)Math.Clamp(Long(c, "groupEveryDays", 7), 1, 112),
				PelicanEnabled = Bool(c, "pelicanEnabled", true),
				PelicanEveryDays = (int)Math.Clamp(Long(c, "pelicanEveryDays", 14), 1, 112),
				ArcEnabled = Bool(c, "arcEnabled", true),
				ArcEverySeasons = (int)Math.Clamp(Long(c, "arcEverySeasons", 2), 1, 8),
				LetterEnabled = Bool(c, "letterEnabled", true),
				LetterEveryDays = (int)Math.Clamp(Long(c, "letterEveryDays", 84), 28, 336),
				LetterText = Str(c, "letterText")
			}
		};
	}

	/// <summary>要下载的图（推荐、推广活动）。</summary>
	public static IReadOnlyList<string> Images(JsonObject o)
	{
		List<string> images = Objects(o["promo"]).Select(p => Str(p, "image")).ToList();
		if (o["campaign"] is JsonObject c)
		{
			images.Add(Str(c, "image"));
		}
		return images.Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToList();
	}

	/// <summary>只认 http/https。</summary>
	public static string SafeUrl(string? url) =>
		Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out Uri? u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : "";

	/// <summary>后台上传的图（/media/… 或只有文件名）走服务器，别的只认 http/https。</summary>
	public static string? ImageUrl(string? image, string root)
	{
		image = (image ?? "").Trim();
		if (image.Length == 0)
		{
			return null;
		}
		if (image.StartsWith("/media/", StringComparison.Ordinal))
		{
			return root.Length > 0 ? root + image : null;
		}
		if (Regex.IsMatch(image, "^[a-f0-9]{16}\\.(png|jpg|gif)$"))
		{
			return root.Length > 0 ? root + "/media/" + image : null;
		}
		string safe = SafeUrl(image);
		return safe.Length > 0 ? safe : null;
	}

	/// <summary>图在本地缓存里的文件名：链接的哈希前 16 位 + 扩展名。</summary>
	public static string CacheName(string url)
	{
		string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
		if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif"))
		{
			ext = ".png";
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant() + ext;
	}

	private static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray ?? new JsonArray()).OfType<JsonObject>();

	private static string Str(JsonObject o, string key, string fallback = "")
	{
		string s = (o[key]?.ToString() ?? "").Trim();
		return s.Length > 0 ? s : fallback;
	}

	private static long Long(JsonObject o, string key, long fallback = 0)
	{
		if (o[key] is not JsonValue v)
		{
			return fallback;
		}
		if (v.TryGetValue(out long l))
		{
			return l;
		}
		return v.TryGetValue(out double d) ? (long)d : fallback;
	}

	private static bool Bool(JsonObject o, string key, bool fallback) => o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
}

/// <summary>推广在这个存档里的进度。</summary>
public sealed class PromoState
{
	public int FirstDay { get; set; } = -1;

	public int LastGossipDay { get; set; } = -1;

	public int LastGroupDay { get; set; } = -1;

	public List<long> SupportDone { get; set; } = new List<long>();

	/// <summary>背包满时寄出的谢礼信：信键 → 信的内容。</summary>
	public Dictionary<string, string> Mail { get; set; } = new Dictionary<string, string>();

	public int LastPelicanDay { get; set; } = -1;

	public List<PromoPost> Posts { get; set; } = new List<PromoPost>();

	/// <summary>上次开推广小剧场的季（年 × 4 + 季）。</summary>
	public int LastArcSeason { get; set; } = -1;

	public int LastLetterDay { get; set; } = -1;

	public List<string> LetterGames { get; set; } = new List<string>();

	/// <summary>同一天最多出一种。</summary>
	public int LastAnyDay { get; set; } = -1;
}

/// <summary>鹈鹕图上的一张推广海报。</summary>
public sealed class PromoPost
{
	public int TotalDay { get; set; }

	public int Day { get; set; }

	public string Season { get; set; } = "";

	public int Year { get; set; }

	public string Npc { get; set; } = "";

	public string Text { get; set; } = "";

	public string Image { get; set; } = "";
}

public static class PromoBook
{
	public const string Path = "mail/promo.json";

	public static readonly DocumentType<PromoState> Doc = new DocumentType<PromoState>("推广", 1, () => new PromoState());
}

/// <summary>推广的时机、文字、谢礼。日子都是游戏的 0 起总天数。</summary>
public static class PromoRules
{
	public const string GossipOrigin = "beyond_valley";

	public const int GossipQuietDays = 2;

	public const int GroupQuietDays = 4;

	public const int PelicanQuietDays = 5;

	public const int ArcQuietDays = 14;

	public const int LetterQuietDays = 21;

	public const int MinLetterGapDays = 28;

	public const int GossipLifetimeDays = 7;

	public const int MaxPosts = 6;

	public const int GroupFrom = 1100;

	public const int GroupUntil = 2100;

	public const string SupportMailPrefix = "AIStardewValley.Support.";

	public static readonly string[] Preferred = { "Abigail", "Sam", "Sebastian" };

	public static readonly string[] Fallback = { "Haley", "Alex", "Emily", "Leah", "Elliott", "Maru", "Penny", "Harvey" };

	public static readonly string[] ArcPair = { "Sam", "Sebastian" };

	/// <summary>后台没指定谢礼时从这里随机挑。</summary>
	public static readonly (string Id, int Qty)[] RarePool =
	{
		("(O)74", 1), ("(O)499", 1), ("(O)347", 5), ("(O)279", 1), ("(O)797", 1), ("(O)166", 1), ("(O)386", 10)
	};

	public static int SeasonIndex(int year, int seasonNumber) => year * 4 + Math.Max(0, seasonNumber);

	/// <summary>开新档后过了安静期、离上次够久。</summary>
	public static bool Due(PromoState s, int last, int every, int quietDays, int today) =>
		today - s.FirstDay >= quietDays && (last < 0 || today - last >= Math.Max(1, every));

	public static bool ArcDue(PromoState s, PromoCampaign c, int today, int seasonIndex) =>
		today - s.FirstDay >= ArcQuietDays && (s.LastArcSeason < 0 || seasonIndex - s.LastArcSeason >= Math.Max(1, c.ArcEverySeasons));

	public static bool LetterDue(PromoState s, PromoCampaign c, int today) =>
		!s.LetterGames.Contains(c.Game, StringComparer.OrdinalIgnoreCase) && today - s.FirstDay >= LetterQuietDays
		&& (s.LastLetterDay < 0 || today - s.LastLetterDay >= Math.Max(MinLetterGapDays, c.LetterEveryDays));

	/// <summary>小剧场至少留出两个空名额。</summary>
	public static bool ArcRoom(int active, int maxActive) => active <= Math.Clamp(maxActive, 1, 5) - 2;

	/// <summary>这期周报登不登"山谷之外"：轮到的期才登。</summary>
	public static (string Title, string Text)? PaperColumn(PromoCampaign c, int issue)
	{
		if (!c.HasGame || !c.PaperEnabled || issue <= 0 || issue % Math.Max(1, c.PaperEveryIssues) != 0)
		{
			return null;
		}
		string text = c.PaperText.Trim();
		if (text.Length == 0)
		{
			text = "远方的祖祖城最近传来一个消息：一位叫" + c.Author + "的人做了一款叫《" + c.Game + "》的游戏" + Tail(c, "，") + "。听说城里的年轻人都在讨论，镇上也有人打听哪里能玩到。";
		}
		return (c.PaperTitle.Trim().Length > 0 ? c.PaperTitle.Trim() : PromoCampaign.DefaultPaperTitle, text);
	}

	/// <summary>八卦：给 AI 看的那句和八卦页上显示的那句。</summary>
	public static (string Memory, string Chat) Gossip(PromoCampaign c, string speakerDisplay) => (
		speakerDisplay + "听人说，祖祖城有位叫" + c.Author + "的人做了一款新游戏《" + c.Game + "》" + (c.Line.Length > 0 ? "（" + c.Line + "）" : "") + "。" + speakerDisplay + "觉得挺新鲜，聊天时可能会当成城里的新鲜事跟农夫提一句。",
		"听说祖祖城有个叫" + c.Author + "的人做了个新游戏《" + c.Game + "》" + Tail(c, "，") + "，挺想去看看的。");

	/// <summary>居民群：一位起头，有第二位就再接一句。</summary>
	public static List<(string Text, string Emotion)> GroupLines(PromoCampaign c, int speakers)
	{
		List<(string, string)> lines = new List<(string, string)> { ("你们听说没？祖祖城有个叫" + c.Author + "的人做了个新游戏，叫《" + c.Game + "》。", "happy") };
		if (speakers > 1)
		{
			lines.Add((c.Line.Length > 0 ? "我也看到了，" + c.Line + "。有空想去看看。" : "我也看到了，看着挺有意思的。", "neutral"));
		}
		return lines;
	}

	public static string PelicanText(PromoCampaign c) =>
		"在祖祖城看到这张海报：一个叫" + c.Author + "的人做的游戏《" + c.Game + "》" + Tail(c, "，") + "。有人想一起玩吗？";

	public static void AddPost(PromoState s, PromoPost post)
	{
		s.Posts.Add(post);
		while (s.Posts.Count > MaxPosts)
		{
			s.Posts.RemoveAt(0);
		}
	}

	/// <summary>山姆和塞巴斯蒂安的游戏之夜。</summary>
	public static AISV.Core.Stories.StoryArc Arc(PromoCampaign c, string modId, int year, string season)
	{
		string key = "y" + year + "-" + season;
		string game = c.Game;
		return new AISV.Core.Stories.StoryArc
		{
			Id = modId + "/promo-" + key,
			Title = "山姆和塞巴斯蒂安的游戏之夜",
			Premise = "祖祖城有个叫" + c.Author + "的人做了个新游戏《" + game + "》" + (c.Line.Length > 0 ? "（" + c.Line + "）" : "") + "。山姆和塞巴斯蒂安约好找个晚上一起玩，好像还想拉上你。",
			MainMod = modId,
			Mods = new List<string> { modId },
			Cast = ArcPair.ToList(),
			Stage = "山姆和塞巴斯蒂安在商量挑个晚上一起玩《" + game + "》，就差你点头了。",
			BornSeason = season,
			BornYear = year,
			State = AISV.Core.Stories.StoryArc.Active,
			Goals = new List<AISV.Core.Stories.ArcObjective>
			{
				new AISV.Core.Stories.ArcObjective
				{
					Id = "promo_" + key + "_sam",
					Kind = "talk_to",
					Npc = "Sam",
					Text = "问问山姆那个新游戏",
					DoneWhen = "农夫和山姆聊起祖祖城那款新游戏《" + game + "》，或者说想参加游戏之夜。"
				},
				new AISV.Core.Stories.ArcObjective
				{
					Id = "promo_" + key + "_sebastian",
					Kind = "talk_to",
					Npc = "Sebastian",
					Text = "和塞巴斯蒂安约好游戏之夜",
					DoneWhen = "农夫和塞巴斯蒂安说好找个晚上一起玩《" + game + "》。"
				}
			}
		};
	}

	public const string LetterSubject = "祖祖城的海报";

	public static string LetterTrigger(PromoCampaign c) => "promo_" + c.Game;

	/// <summary>来信正文（游戏信件格式，换行是 ^）。</summary>
	public static string LetterBody(PromoCampaign c)
	{
		string body = c.LetterText.Trim();
		if (body.Length == 0)
		{
			body = "@：\n前几天我去了一趟祖祖城，在街边看到一张海报，是一个叫" + c.Author + "的人做的游戏《" + c.Game + "》" + Tail(c, "，") + "。\n不知道为什么，我第一个就想到了你。哪天你也去看看，回来跟我说说怎么样。";
		}
		return Clean(body).Replace("\r", "").Replace("\n", "^");
	}

	/// <summary>后台指定的谢礼（编号补上 (O)）；没指定为空。</summary>
	public static (string Id, int Qty)? FixedReward(SupportTask t, Func<string, bool> valid)
	{
		string id = t.RewardItem.Trim();
		if (id.Length == 0)
		{
			return null;
		}
		if (!id.StartsWith("(", StringComparison.Ordinal))
		{
			id = "(O)" + id;
		}
		return valid(id) ? (id, Math.Clamp(t.RewardQty, 1, 999)) : null;
	}

	public static (string Id, int Qty) PickReward(SupportTask t, Func<string, bool> valid, Random random)
	{
		if (FixedReward(t, valid) is { } fixedReward)
		{
			return fixedReward;
		}
		List<(string Id, int Qty)> pool = RarePool.Where(p => valid(p.Id)).ToList();
		return pool.Count > 0 ? pool[random.Next(pool.Count)] : ("(O)74", 1);
	}

	/// <summary>委托板上的支持任务：这个存档还没做过的最新两条。</summary>
	public static List<SupportTask> OpenSupport(IEnumerable<SupportTask> all, PromoState s) => all.Where(t => !s.SupportDone.Contains(t.Id)).Take(2).ToList();

	public static string SupportMailKey(long id) => SupportMailPrefix + id;

	public static string SupportMail(string author, string itemId, int qty) =>
		Clean("@，^^谢谢你花时间支持我！你的背包满了，谢礼放在信里。^^   -" + author) + "%item id " + itemId + " " + qty + " %%[#]" + Clean(author + "的谢礼");

	/// <summary>信件格式里的保留字符换掉。</summary>
	public static string Clean(string s) => s.Replace("%", "％").Replace("[#]", "").Replace("#", "");

	private static string Tail(PromoCampaign c, string sep) => c.Line.Length > 0 ? sep + c.Line : "";
}
