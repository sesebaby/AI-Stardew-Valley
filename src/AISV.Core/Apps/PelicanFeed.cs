using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AISV.Core.Apps;

public enum PostSource
{
	Gossip,
	Diary,
	Promo
}

/// <summary>可以发成帖子的一段内容。<see cref="Npc"/> 为空时由海莉来发。</summary>
public sealed record PostMemory(int TotalDay, int Day, string Season, int Year, string Text, string Npc = "", string Image = "");

public sealed record PostComment(string Npc, string Text);

public sealed record PelicanPost(PostSource Source, string Npc, int Expression, int Day, string Season, int Year, string Caption, string Hashtag, int Likes, IReadOnlyList<PostComment> Comments, string Image = "");

/// <summary>鹈鹕图：把八卦、农夫日记和推广海报排成帖子，配上点赞数、标签和两条评论（都按内容固定）。</summary>
public static class PelicanFeed
{
	public const int MaxPosts = 24;

	public const int CaptionLength = 180;

	public const int CommentsPerPost = 2;

	public const int CommentLines = 8;

	public const int Hashtags = 6;

	public const int DiaryIntros = 3;

	public const string Haley = "Haley";

	public static readonly IReadOnlyList<string> Commenters = new[] { "Haley", "Sebastian", "Sam", "Abigail", "Alex", "Emily", "Leah", "Maru" };

	/// <param name="text">按键取文字：(键, 序号) → 那一句（键是 diary / tag / comment / promo.comment / promo.tag）。</param>
	public static IReadOnlyList<PelicanPost> Build(IEnumerable<PostMemory> gossip, IEnumerable<PostMemory> diary, IEnumerable<PostMemory> promo, Func<string, int, string> text)
	{
		var all = gossip.Where(Usable).Select(m => (Memory: m, Source: PostSource.Gossip))
			.Concat(diary.Where(Usable).Select(m => (Memory: m, Source: PostSource.Diary)))
			.Concat(promo.Where(Usable).Select(m => (Memory: m, Source: PostSource.Promo)))
			.OrderByDescending(p => p.Memory.TotalDay).ThenBy(p => p.Source == PostSource.Promo ? 0 : 1).ThenBy(p => p.Source)
			.Take(MaxPosts);
		List<PelicanPost> posts = new List<PelicanPost>();
		foreach ((PostMemory m, PostSource source) in all)
		{
			uint seed = Seed(m);
			string poster = source == PostSource.Diary || string.IsNullOrWhiteSpace(m.Npc) ? Haley : m.Npc.Trim();
			string caption = Excerpt(m.Text, CaptionLength);
			if (source == PostSource.Promo)
			{
				List<PostComment> promoComments = Commenters.Where(n => !Same(n, poster)).Skip((int)(seed % 5u)).Take(CommentsPerPost)
					.Select((n, k) => new PostComment(n, text("promo.comment", k))).ToList();
				posts.Add(new PelicanPost(source, poster, 0, m.Day, m.Season, m.Year, caption, text("promo.tag", 0), (int)(20 + seed % 60), promoComments, m.Image));
				continue;
			}
			if (source == PostSource.Diary)
			{
				caption = text("diary", (int)((seed >> 3) % DiaryIntros)) + caption;
			}
			posts.Add(new PelicanPost(source, poster, (int)(seed % 2), m.Day, m.Season, m.Year, caption, text("tag", (int)((seed >> 6) % Hashtags)), (int)(3 + seed % 140), Comments(poster, seed, text)));
		}
		return posts;
	}

	/// <summary>两条评论：评论的人不是发帖人，也不重复。</summary>
	public static IReadOnlyList<PostComment> Comments(string poster, uint seed, Func<string, int, string> text)
	{
		List<string> pool = Commenters.Where(n => !Same(n, poster)).ToList();
		List<PostComment> list = new List<PostComment>();
		int start = (int)((seed >> 9) % CommentLines);
		for (int k = 0; k < CommentsPerPost && pool.Count > 0; k++)
		{
			string who = pool[(int)((seed >> (12 + k * 5)) % (uint)pool.Count)];
			pool.Remove(who);
			list.Add(new PostComment(who, text("comment", (start + k * 3) % CommentLines)));
		}
		return list;
	}

	public static string Excerpt(string? text, int max)
	{
		string clean = Clean(text);
		if (clean.Length <= max)
		{
			return clean;
		}
		string cut = clean.Substring(0, Math.Max(1, max - 2));
		int space = cut.LastIndexOf(' ');
		if (space > max / 2)
		{
			cut = cut.Substring(0, space);
		}
		return cut.TrimEnd(' ', ',', ';', ':', '.', '-', '，', '；', '：', '。') + "……";
	}

	/// <summary>去掉表情符号，空白合成一个空格。</summary>
	public static string Clean(string? text)
	{
		static bool Decoration(char c) => char.IsSurrogate(c) || c == '\uFE0F' || c == '\u200D' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol;
		string kept = new string((text ?? "").Where(c => !Decoration(c)).ToArray());
		return string.Join(' ', kept.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
	}

	public static uint Seed(PostMemory m)
	{
		uint h = 2166136261u;
		foreach (char c in Clean(m.Text) + "|" + m.TotalDay.ToString(CultureInfo.InvariantCulture))
		{
			h ^= c;
			h *= 16777619;
		}
		return h;
	}

	/// <summary>翻页：循环到头尾。</summary>
	public static int Step(int index, int delta, int count) => count > 0 ? ((index + delta) % count + count) % count : 0;

	private static bool Usable(PostMemory? m) => m != null && Clean(m.Text).Length > 0;

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
