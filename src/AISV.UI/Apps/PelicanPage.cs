using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>鹈鹕图：镇上的照片墙。上面一排发帖人的头像，下面一条条帖子（照片、点赞、配文、标签、两条评论）。</summary>
internal sealed class PelicanPage : ShellPage
{
	// 这次开游戏点过的赞。
	private static readonly HashSet<string> Liked = new HashSet<string>(StringComparer.Ordinal);

	private readonly Scroller _scroll = new Scroller();

	private readonly Dictionary<int, int> _postTops = new Dictionary<int, int>();

	private IReadOnlyList<PelicanPost> _posts = Array.Empty<PelicanPost>();

	private bool _loaded;

	private Rectangle _view;

	public PelicanPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("pelican.title", "鹈鹕图");

	public override void Reload()
	{
		if (!_loaded)
		{
			_loaded = true;
			_posts = AppData.Posts(Ctx);
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction, 120);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		AisvTheme.Fill(b, area, new Color(250, 248, 244));
		if (_posts.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("pelican.empty", "还没有人发帖"), Tr.Get("pelican.emptyHint", "等镇上有点新鲜事，大家就会来晒了。"));
			return;
		}
		int colW = Math.Min(660, area.Width - 60);
		int x = area.Center.X - colW / 2;
		AisvTheme.BeginClip(b, area);
		int y = area.Y + 20 - _scroll.Offset;
		y = Stories(b, x, y, colW, hits) + 20;
		for (int i = 0; i < _posts.Count; i++)
		{
			_postTops[i] = y + _scroll.Offset - area.Y;
			y = Post(b, _posts[i], x, y, colW, hits) + 24;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, area, y, 12);
	}

	/// <summary>发帖人头像一排（带彩色圈），点一下跳到这人的第一条。</summary>
	private int Stories(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		List<string> posters = _posts.Select(p => p.Npc).Distinct(StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, w / 92)).ToList();
		int cx = x;
		foreach (string npc in posters)
		{
			Rectangle ring = new Rectangle(cx, y, 76, 76);
			AisvTheme.FillRounded(b, ring, new Color(232, 96, 140), 22);
			AisvTheme.FillRounded(b, new Rectangle(ring.X, ring.Y, ring.Width, ring.Height / 2), new Color(250, 176, 60), 22);
			AisvTheme.FillRounded(b, AisvTheme.Inflate(ring, -4), Color.White, 18);
			Kit.Avatar(b, AisvTheme.Inflate(ring, -8), Ctx, npc);
			string name = AisvTheme.Truncate(Ctx.Display(npc), 88, 0.6f);
			AisvTheme.DrawText(b, name, new Vector2(ring.Center.X - AisvTheme.Width(name, 0.6f) / 2f, ring.Bottom + 6), AisvTheme.TextMuted, 0.6f);
			int first = _posts.ToList().FindIndex(p => string.Equals(p.Npc, npc, StringComparison.OrdinalIgnoreCase));
			hits.Add(ring, () =>
			{
				if (_postTops.TryGetValue(first, out int top))
				{
					_scroll.By(top - 10 - _scroll.Offset);
					Game1.playSound("shwip");
				}
			}, _view);
			cx += 92;
		}
		return y + 76 + 34;
	}

	private int Post(SpriteBatch b, PelicanPost p, int x, int y, int w, Hits hits)
	{
		const int pad = 20;
		int photoH = Math.Min(w * 3 / 4, Math.Max(300, _view.Height * 3 / 5));
		List<string> caption = AisvTheme.WrapLines(Ctx.Display(p.Npc) + "  " + p.Caption, w - pad * 2, 0.76f);
		int h = 76 + photoH + 60 + caption.Count * AisvTheme.LineHeight(0.76f) + 34 + p.Comments.Count * 32 + 20;
		Rectangle card = new Rectangle(x, y, w, h);
		if (card.Bottom < _view.Y || card.Y > _view.Bottom)
		{
			return card.Bottom;
		}
		AisvTheme.DrawShadow(b, card, 16, 3);
		AisvTheme.Bordered(b, card, Color.White, new Color(232, 226, 216), 16);
		Rectangle av = new Rectangle(x + pad, y + 16, 44, 44);
		AisvTheme.FillRounded(b, AisvTheme.Inflate(av, 3), new Color(232, 96, 140), 12);
		Kit.Avatar(b, av, Ctx, p.Npc);
		AisvTheme.Bold(b, Ctx.Display(p.Npc), new Vector2(av.Right + 14, y + 16), AisvTheme.Text, 0.8f);
		string sub = ShellText.Date(p.Day, p.Season, p.Year) + (p.Source == PostSource.Promo ? " · " + Tr.Get("pelican.sponsored", "来自祖祖城") : "");
		AisvTheme.DrawText(b, sub, new Vector2(av.Right + 14, y + 42), AisvTheme.TextFaint, 0.6f);
		Rectangle photo = new Rectangle(x, y + 76, w, photoH);
		Photo(b, photo, p);
		int ay = photo.Bottom + 14;
		string key = Key(p);
		bool liked = Liked.Contains(key);
		Rectangle heart = new Rectangle(x + pad - 4, ay - 4, 40, 40);
		HeartIcon(b, new Rectangle(heart.X + 6, heart.Y + 8, 28, 24), liked);
		hits.Add(heart, () =>
		{
			Game1.playSound(liked ? "smallSelect" : "dwop");
			if (!Liked.Remove(key))
			{
				Liked.Add(key);
			}
		}, _view);
		Bubble(b, new Rectangle(heart.Right + 14, ay + 4, 28, 24));
		string likes = Tr.Get("pelican.likes", "♥ {{count}}", ("count", p.Likes + (liked ? 1 : 0)));
		AisvTheme.Bold(b, likes, new Vector2(x + w - pad - AisvTheme.Width(likes, 0.74f), ay + 4), AisvTheme.Text, 0.74f);
		int ly = ay + 46;
		string poster = Ctx.Display(p.Npc);
		for (int i = 0; i < caption.Count; i++)
		{
			string line = caption[i];
			if (i == 0 && line.StartsWith(poster))
			{
				AisvTheme.Bold(b, poster, new Vector2(x + pad, ly), AisvTheme.Text, 0.76f);
				AisvTheme.DrawText(b, line.Substring(poster.Length), new Vector2(x + pad + AisvTheme.Width(poster, 0.76f), ly), AisvTheme.Text, 0.76f);
			}
			else
			{
				AisvTheme.DrawText(b, line, new Vector2(x + pad, ly), AisvTheme.Text, 0.76f);
			}
			ly += AisvTheme.LineHeight(0.76f);
		}
		AisvTheme.DrawText(b, p.Hashtag, new Vector2(x + pad, ly + 4), new Color(60, 110, 190), 0.72f);
		ly += 38;
		foreach (PostComment c in p.Comments)
		{
			string who = Ctx.Display(c.Npc);
			AisvTheme.Bold(b, who, new Vector2(x + pad, ly), AisvTheme.Text, 0.68f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(c.Text, w - pad * 2 - AisvTheme.Width(who, 0.68f) - 12, 0.68f), new Vector2(x + pad + AisvTheme.Width(who, 0.68f) + 12, ly), AisvTheme.TextMuted, 0.68f);
			ly += 32;
		}
		return card.Bottom;
	}

	/// <summary>照片：推广帖有图就用图；其他是按季节画的小风景，发帖人站在中间。</summary>
	private void Photo(SpriteBatch b, Rectangle r, PelicanPost p)
	{
		Texture2D? image = p.Image.Length > 0 ? Ctx.Promo?.Content.Texture(p.Image) : null;
		if (image != null)
		{
			AisvTheme.Fill(b, r, new Color(30, 30, 34));
			float scale = Math.Min(r.Width / (float)image.Width, r.Height / (float)image.Height);
			int iw = (int)(image.Width * scale), ih = (int)(image.Height * scale);
			b.Draw(image, new Rectangle(r.Center.X - iw / 2, r.Center.Y - ih / 2, iw, ih), Color.White);
			return;
		}
		(Color sky, Color skyLow, Color ground, Color speck) = SeasonColors(p.Season);
		int horizon = r.Y + r.Height * 7 / 10;
		const int bands = 12;
		for (int i = 0; i < bands; i++)
		{
			int by = r.Y + (horizon - r.Y) * i / bands;
			AisvTheme.Fill(b, new Rectangle(r.X, by, r.Width, (horizon - r.Y) / bands + 1), Color.Lerp(sky, skyLow, i / (float)bands));
		}
		AisvTheme.Fill(b, new Rectangle(r.X, horizon, r.Width, r.Bottom - horizon), ground);
		AisvTheme.Fill(b, new Rectangle(r.X, horizon, r.Width, 6), Color.Lerp(ground, Color.Black, 0.12f));
		uint seed = FarmerReviews.StableHash(p.Caption);
		AisvTheme.FillRounded(b, new Rectangle(r.Right - 120 - (int)(seed % 80), r.Y + 34, 56, 56), new Color(255, 236, 160), 28);
		for (int i = 0; i < 3; i++)
		{
			int cx = r.X + 40 + (int)((seed >> (i * 5)) % (uint)Math.Max(1, r.Width - 220));
			int cy = r.Y + 40 + i * 34 + (int)((seed >> (i * 3)) % 20);
			AisvTheme.FillRounded(b, new Rectangle(cx, cy, 110, 26), Color.White * 0.8f, 13);
			AisvTheme.FillRounded(b, new Rectangle(cx + 24, cy - 14, 56, 30), Color.White * 0.8f, 15);
		}
		for (int i = 0; i < 26; i++)
		{
			uint h = FarmerReviews.StableHash(p.Caption + i);
			int sx = r.X + (int)(h % (uint)r.Width), sy = r.Y + (int)((h >> 10) % (uint)r.Height);
			AisvTheme.FillRounded(b, new Rectangle(sx, sy, 6, 6), speck * 0.8f, 3);
		}
		NPC? npc = ShellText.Npc(p.Npc);
		Texture2D? portrait = null;
		try
		{
			portrait = npc?.Portrait;
		}
		catch
		{
		}
		if (portrait != null && !portrait.IsDisposed)
		{
			int size = Math.Min(r.Height - 40, 256);
			Rectangle frame = new Rectangle(r.Center.X - size / 2, r.Bottom - size - 10, size, size);
			int col = p.Expression % Math.Max(1, portrait.Width / 64);
			b.Draw(portrait, frame, new Rectangle(col * 64, 0, 64, 64), Color.White);
		}
		else if (npc?.Sprite?.Texture != null)
		{
			Rectangle frame = new Rectangle(r.Center.X - 64, horizon - 160, 128, 256);
			b.Draw(npc.Sprite.Texture, frame, new Rectangle(0, 0, 16, 32), Color.White);
		}
	}

	private static (Color Sky, Color SkyLow, Color Ground, Color Speck) SeasonColors(string season) => (season ?? "").Trim().ToLowerInvariant() switch
	{
		"summer" => (new Color(120, 196, 250), new Color(196, 232, 255), new Color(104, 176, 66), Color.White),
		"fall" => (new Color(240, 180, 130), new Color(252, 222, 180), new Color(186, 120, 54), new Color(214, 96, 40)),
		"winter" => (new Color(172, 196, 226), new Color(220, 230, 244), new Color(238, 242, 250), Color.White),
		_ => (new Color(160, 214, 248), new Color(214, 238, 252), new Color(132, 196, 92), new Color(250, 170, 196))
	};

	private static void HeartIcon(SpriteBatch b, Rectangle r, bool filled)
	{
		Color c = filled ? new Color(232, 70, 100) : AisvTheme.Text;
		int lobe = r.Width / 2 + 2;
		if (filled)
		{
			AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y, lobe, lobe), c, lobe / 2);
			AisvTheme.FillRounded(b, new Rectangle(r.Right - lobe, r.Y, lobe, lobe), c, lobe / 2);
			for (int i = 0; i < r.Height - lobe / 2; i++)
			{
				int inset = i * r.Width / 2 / Math.Max(1, r.Height - lobe / 2);
				AisvTheme.Fill(b, new Rectangle(r.X + inset, r.Y + lobe / 2 + i, r.Width - inset * 2, 1), c);
			}
			return;
		}
		AisvTheme.Bordered(b, new Rectangle(r.X, r.Y, lobe, lobe), Color.White, c, lobe / 2, 3);
		AisvTheme.Bordered(b, new Rectangle(r.Right - lobe, r.Y, lobe, lobe), Color.White, c, lobe / 2, 3);
		AisvTheme.Line(b, new Vector2(r.X + 1, r.Y + lobe / 2 + 2), new Vector2(r.Center.X, r.Bottom), c, 3f);
		AisvTheme.Line(b, new Vector2(r.Right - 1, r.Y + lobe / 2 + 2), new Vector2(r.Center.X, r.Bottom), c, 3f);
	}

	private static void Bubble(SpriteBatch b, Rectangle r)
	{
		AisvTheme.Bordered(b, new Rectangle(r.X, r.Y, r.Width, r.Height - 6), Color.White, AisvTheme.Text, 10, 3);
		AisvTheme.Line(b, new Vector2(r.X + 6, r.Bottom - 8), new Vector2(r.X + 2, r.Bottom), AisvTheme.Text, 3f);
	}

	private static string Key(PelicanPost p) => $"{p.Npc}|{p.Year}|{p.Season}|{p.Day}|{FarmerReviews.StableHash(p.Caption)}";
}
