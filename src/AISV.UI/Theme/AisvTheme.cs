using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Theme;

public static class AisvTheme
{
	public static readonly Color WoodDark = new Color(90, 52, 24);

	public static readonly Color Wood = new Color(122, 74, 36);

	public static readonly Color WoodLight = new Color(160, 104, 56);

	public static readonly Color Page = new Color(251, 245, 233);

	public static readonly Color PageDeep = new Color(246, 238, 220);

	public static readonly Color Card = new Color(255, 253, 247);

	public static readonly Color CardBorder = new Color(229, 213, 181);

	public static readonly Color Divider = new Color(239, 227, 204);

	public static readonly Color Text = new Color(61, 43, 31);

	public static readonly Color TextMuted = new Color(122, 100, 80);

	public static readonly Color TextFaint = new Color(154, 134, 112);

	public static readonly Color Accent = new Color(79, 138, 45);

	public static readonly Color AccentDark = new Color(58, 104, 32);

	public static readonly Color AccentSoft = new Color(227, 238, 211);

	public static readonly Color SelectedRow = new Color(238, 245, 230);

	public static readonly Color Badge = new Color(226, 88, 62);

	public static readonly Color Gold = new Color(201, 138, 28);

	public static readonly Color GoldSoft = new Color(250, 236, 205);

	public static readonly Color Pink = new Color(214, 120, 150);

	public static readonly Color PinkSoft = new Color(252, 228, 236);

	public static readonly Color Warn = new Color(214, 120, 52);

	public static readonly Color Danger = new Color(200, 70, 60);

	public static readonly Color Shadow = new Color(60, 35, 15) * 0.18f;

	public const int Radius = 12;

	public const int WindowBorder = 10;

	private static readonly RasterizerState Scissor = new RasterizerState { ScissorTestEnable = true };

	private static readonly Rectangle HeartFull = new Rectangle(211, 428, 7, 6);

	private static readonly Rectangle HeartEmpty = new Rectangle(218, 428, 7, 6);

	public static void Fill(SpriteBatch b, Rectangle r, Color c)
	{
		if (r.Width > 0 && r.Height > 0)
		{
			b.Draw(Game1.staminaRect, r, c);
		}
	}

	public static void HLine(SpriteBatch b, int x, int y, int w, Color c, int thickness = 2) => Fill(b, new Rectangle(x, y, w, thickness), c);

	public static void VLine(SpriteBatch b, int x, int y, int h, Color c, int thickness = 2) => Fill(b, new Rectangle(x, y, thickness, h), c);

	public static void FillRounded(SpriteBatch b, Rectangle r, Color c, int radius = Radius)
	{
		if (r.Width <= 0 || r.Height <= 0)
		{
			return;
		}
		int rad = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
		if (rad == 0)
		{
			Fill(b, r, c);
			return;
		}
		Fill(b, new Rectangle(r.X, r.Y + rad, r.Width, r.Height - rad * 2), c);
		for (int i = 0; i < rad; i += 2)
		{
			float dy = rad - i - 1f;
			int inset = (int)Math.Ceiling(rad - Math.Sqrt(Math.Max(0f, rad * rad - dy * dy)));
			int h = Math.Min(2, rad - i);
			Fill(b, new Rectangle(r.X + inset, r.Y + i, r.Width - inset * 2, h), c);
			Fill(b, new Rectangle(r.X + inset, r.Bottom - i - h, r.Width - inset * 2, h), c);
		}
	}

	public static void Bordered(SpriteBatch b, Rectangle r, Color fill, Color border, int radius = Radius, int thickness = 2)
	{
		FillRounded(b, r, border, radius);
		FillRounded(b, Inflate(r, -thickness), fill, Math.Max(0, radius - thickness));
	}

	public static void DrawShadow(SpriteBatch b, Rectangle r, int radius = Radius, int offset = 4) => FillRounded(b, new Rectangle(r.X + 2, r.Y + offset, r.Width, r.Height), Shadow, radius);

	/// <summary>木框窗口，返回里面的纸面。</summary>
	public static Rectangle Window(SpriteBatch b, Rectangle outer)
	{
		DrawShadow(b, outer, 20, 8);
		FillRounded(b, outer, WoodDark, 20);
		Rectangle mid = Inflate(outer, -4);
		FillRounded(b, mid, Wood, 17);
		HLine(b, mid.X + 16, mid.Y + 2, mid.Width - 32, WoodLight);
		Rectangle page = Inflate(outer, -WindowBorder);
		FillRounded(b, page, Page, 12);
		return page;
	}

	public static void DrawCard(SpriteBatch b, Rectangle r, bool highlight = false, Color? fill = null) => Bordered(b, r, fill ?? (highlight ? SelectedRow : Card), CardBorder);

	public static void Field(SpriteBatch b, Rectangle r, bool focused) => Bordered(b, r, Card, focused ? Accent : CardBorder, Math.Min(Radius, r.Height / 2));

	public static void Button(SpriteBatch b, Rectangle r, string label, bool primary = false, bool hover = false, bool disabled = false, float scale = 1f)
	{
		Color fill = disabled ? PageDeep : primary ? (hover ? AccentDark : Accent) : (hover ? SelectedRow : Card);
		Color border = disabled ? CardBorder : primary ? AccentDark : CardBorder;
		Bordered(b, r, fill, border, Math.Min(r.Height / 2, 20));
		TextCentered(b, label, r, disabled ? TextFaint : primary ? Color.White : Text, scale);
	}

	public static void Tab(SpriteBatch b, Rectangle r, string label, bool selected, bool hover, int badge = 0)
	{
		if (selected || hover)
		{
			FillRounded(b, r, selected ? AccentSoft : PageDeep, Math.Min(r.Height / 2, 22));
		}
		TextCentered(b, label, r, selected ? AccentDark : TextMuted);
		if (badge > 0)
		{
			Vector2 size = Game1.smallFont.MeasureString(label);
			DrawBadge(b, new Vector2(r.Center.X + size.X / 2f + 10f, r.Y + 10), badge);
		}
	}

	public static void SubTab(SpriteBatch b, Rectangle r, string label, bool selected, bool hover)
	{
		TextCentered(b, label, r, selected ? AccentDark : hover ? Text : TextMuted);
		if (selected)
		{
			int w = (int)Game1.smallFont.MeasureString(label).X;
			FillRounded(b, new Rectangle(r.Center.X - w / 2, r.Bottom - 6, w, 4), Accent, 2);
		}
	}

	public static void DrawBadge(SpriteBatch b, Vector2 center, int count)
	{
		string text = count > 99 ? "99+" : count.ToString();
		const float scale = 0.7f;
		Vector2 size = Game1.smallFont.MeasureString(text) * scale;
		int w = Math.Max(26, (int)size.X + 14);
		Rectangle r = new Rectangle((int)center.X - w / 2, (int)center.Y - 13, w, 26);
		Bordered(b, r, Badge, Color.White, 13);
		DrawText(b, text, new Vector2(r.Center.X - size.X / 2f, r.Center.Y - size.Y / 2f + 1f), Color.White, scale);
	}

	public static Rectangle Chip(SpriteBatch b, Vector2 pos, string text, Color fill, Color ink, float scale = 0.75f)
	{
		Vector2 size = Game1.smallFont.MeasureString(text) * scale;
		Rectangle r = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X + 20, (int)size.Y + 8);
		FillRounded(b, r, fill, r.Height / 2);
		DrawText(b, text, new Vector2(r.X + 10, r.Y + 5), ink, scale);
		return r;
	}

	public static int ChipWidth(string text, float scale = 0.75f) => (int)(Game1.smallFont.MeasureString(text).X * scale) + 20;

	public static void Toggle(SpriteBatch b, Rectangle r, bool on, bool disabled = false)
	{
		FillRounded(b, r, disabled ? CardBorder : on ? Accent : new Color(214, 202, 180), r.Height / 2);
		int knob = r.Height - 6;
		FillRounded(b, new Rectangle(on ? r.Right - knob - 3 : r.X + 3, r.Y + 3, knob, knob), Color.White, knob / 2);
	}

	public static void Progress(SpriteBatch b, Rectangle r, float fraction, Color? fill = null)
	{
		FillRounded(b, r, Divider, r.Height / 2);
		int w = (int)(r.Width * Math.Clamp(fraction, 0f, 1f));
		if (w > 0)
		{
			FillRounded(b, new Rectangle(r.X, r.Y, Math.Max(w, r.Height), r.Height), fill ?? Accent, r.Height / 2);
		}
	}

	public static void ScrollTrack(SpriteBatch b, Rectangle track, int offset, int maxOffset)
	{
		if (maxOffset <= 0 || track.Height < 24)
		{
			return;
		}
		Rectangle rail = new Rectangle(track.Center.X - 4, track.Y, 8, track.Height);
		FillRounded(b, rail, Divider, 4);
		int thumb = Math.Clamp(track.Height * track.Height / (track.Height + maxOffset), 40, track.Height);
		int y = track.Y + (int)((track.Height - thumb) * (offset / (float)maxOffset));
		FillRounded(b, new Rectangle(rail.X, y, rail.Width, thumb), TextFaint, 4);
	}

	public static void CloseButton(SpriteBatch b, Rectangle r, bool hover)
	{
		int size = Math.Min(r.Width, r.Height);
		Rectangle k = new Rectangle(r.Center.X - size / 2, r.Center.Y - size / 2, size, size);
		Bordered(b, k, hover ? PageDeep : Card, CardBorder, size / 2);
		float arm = size * 0.22f;
		Vector2 c = new Vector2(k.Center.X, k.Center.Y);
		Color col = hover ? Text : TextMuted;
		Line(b, c - new Vector2(arm, arm), c + new Vector2(arm, arm), col, 3f);
		Line(b, c + new Vector2(-arm, arm), c + new Vector2(arm, -arm), col, 3f);
	}

	public static void Chevron(SpriteBatch b, Vector2 c, bool left, Color color)
	{
		float dx = left ? 4f : -4f;
		Line(b, new Vector2(c.X + dx, c.Y - 7), new Vector2(c.X - dx, c.Y), color, 3f);
		Line(b, new Vector2(c.X - dx, c.Y), new Vector2(c.X + dx, c.Y + 7), color, 3f);
	}

	public static void Line(SpriteBatch b, Vector2 from, Vector2 to, Color c, float thickness)
	{
		Vector2 d = to - from;
		float len = d.Length();
		if (len >= 0.5f)
		{
			b.Draw(Game1.staminaRect, from, null, c, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 1f);
		}
	}

	public static void DrawText(SpriteBatch b, string text, Vector2 pos, Color color, float scale = 1f, SpriteFont? font = null)
	{
		if (!string.IsNullOrEmpty(text))
		{
			b.DrawString(font ?? Game1.smallFont, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
		}
	}

	/// <summary>游戏字体没有粗体，错开一像素画两遍。</summary>
	public static void Bold(SpriteBatch b, string text, Vector2 pos, Color color, float scale = 1f)
	{
		DrawText(b, text, pos, color, scale);
		DrawText(b, text, pos + new Vector2(1f, 0f), color, scale);
	}

	public static void TextCentered(SpriteBatch b, string text, Rectangle r, Color color, float scale = 1f)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		Vector2 size = Game1.smallFont.MeasureString(text) * scale;
		DrawText(b, text, new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f + 2f * scale), color, scale);
	}

	public static float Width(string text, float scale = 1f) => Game1.smallFont.MeasureString(text ?? "").X * scale;

	public static string Truncate(string text, float maxWidth, float scale = 1f)
	{
		text ??= "";
		if (Width(text, scale) <= maxWidth)
		{
			return text;
		}
		float room = Math.Max(0f, maxWidth - Width("…", scale));
		int n = text.Length;
		while (n > 0 && Width(text.Substring(0, n), scale) > room)
		{
			n--;
		}
		return text.Substring(0, n) + "…";
	}

	public static string Wrap(string text, int width, SpriteFont? font = null) => Game1.parseText(text ?? "", font ?? Game1.smallFont, Math.Max(40, width));

	/// <summary>按宽度折行；段落之间可以空一行。</summary>
	public static List<string> WrapLines(string text, int width, float scale = 1f, bool paragraphGap = false)
	{
		List<string> lines = new List<string>();
		foreach (string para in (text ?? "").Replace("\r", "").Split('\n'))
		{
			string p = para.Trim();
			if (p.Length == 0)
			{
				continue;
			}
			if (paragraphGap && lines.Count > 0)
			{
				lines.Add("");
			}
			lines.AddRange(Wrap(p, (int)(width / scale)).Split('\n'));
		}
		return lines;
	}

	public static int LineHeight(SpriteFont? font = null) => (int)(font ?? Game1.smallFont).MeasureString("Ay").Y;

	public static int LineHeight(float scale) => (int)(Game1.smallFont.LineSpacing * scale);

	public static void Hearts(SpriteBatch b, Vector2 pos, int hearts, int max, int size = 14)
	{
		for (int i = 0; i < max; i++)
		{
			b.Draw(Game1.mouseCursors, new Rectangle((int)pos.X + i * (size + 4), (int)pos.Y, size, size * 6 / 7), i < hearts ? HeartFull : HeartEmpty, Color.White);
		}
	}

	public static void Heart(SpriteBatch b, Rectangle r) => b.Draw(Game1.mouseCursors, r, HeartFull, Color.White);

	public static void Avatar(SpriteBatch b, Rectangle r, NPC? npc, string fallback = "?")
	{
		FillRounded(b, r, GoldSoft, 8);
		Texture2D? tex = null;
		try
		{
			tex = npc?.Portrait;
		}
		catch
		{
		}
		if (tex != null && !tex.IsDisposed)
		{
			b.Draw(tex, Inflate(r, -2), new Rectangle(0, 0, Math.Min(64, tex.Width), Math.Min(64, tex.Height)), Color.White);
		}
		else if (npc?.Sprite?.Texture != null)
		{
			b.Draw(npc.Sprite.Texture, Inflate(r, -6), npc.getMugShotSourceRect(), Color.White);
		}
		else
		{
			Glyph(b, r, TextFaint, fallback);
		}
	}

	public static void Glyph(SpriteBatch b, Rectangle r, Color fill, string glyph)
	{
		FillRounded(b, r, fill, Math.Min(12, r.Width / 3));
		TextCentered(b, glyph, r, Color.White, Math.Max(0.7f, r.Height / (glyph.Length > 1 ? 60f : 46f)));
	}

	public static void Magnifier(SpriteBatch b, Vector2 c)
	{
		Rectangle ring = new Rectangle((int)c.X - 9, (int)c.Y - 10, 16, 16);
		Bordered(b, ring, Card, TextFaint, 8);
		Line(b, new Vector2(ring.Right - 3, ring.Bottom - 3), new Vector2(ring.Right + 4, ring.Bottom + 4), TextFaint, 3f);
	}

	public static bool Hover(Rectangle r, Rectangle? clip = null)
	{
		int x = Game1.getMouseX(), y = Game1.getMouseY();
		return r.Contains(x, y) && (clip == null || clip.Value.Contains(x, y));
	}

	public static void BeginClip(SpriteBatch b, Rectangle clip)
	{
		b.End();
		b.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(clip, b.GraphicsDevice.Viewport.Bounds);
		b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, Scissor);
	}

	public static void EndClip(SpriteBatch b)
	{
		b.End();
		b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
	}

	public static Rectangle Inflate(Rectangle r, int by) => new Rectangle(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);
}
