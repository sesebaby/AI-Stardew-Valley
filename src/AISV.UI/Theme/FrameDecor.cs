using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Theme;

/// <summary>窗口上的小装饰：铜角、藤蔓、当季的花、木框上打盹的猫，还有飘落的花瓣或雪。</summary>
internal static class FrameDecor
{
	private static readonly Dictionary<char, Color> Ink = new Dictionary<char, Color>
	{
		['k'] = new Color(62, 34, 18), ['b'] = new Color(190, 136, 58), ['B'] = new Color(240, 200, 110), ['d'] = new Color(120, 78, 30),
		['g'] = new Color(92, 148, 44), ['G'] = new Color(140, 192, 66), ['D'] = new Color(52, 100, 34),
		['p'] = new Color(240, 140, 170), ['P'] = new Color(255, 196, 212), ['y'] = new Color(250, 214, 80), ['o'] = new Color(232, 132, 48), ['O'] = new Color(248, 178, 76), ['r'] = new Color(190, 72, 44),
		['w'] = new Color(244, 248, 255), ['s'] = new Color(180, 208, 236), ['c'] = new Color(226, 150, 74), ['C'] = new Color(246, 196, 120), ['n'] = new Color(240, 130, 130)
	};

	private static readonly PixelArt Corner = PixelArt.Of(Ink, "kkkkkkkkkk", "kBBBBBBBbk", "kBbbbbbbdk", "kBbkkbdkk.", "kBbkBdk...", "kBbbdk....", "kBbdk.....", "kBdk......", "kbk.......", "kk........");

	private static readonly PixelArt Vine = PixelArt.Of(Ink, "..DD............", ".DGGD....DD.....", ".DgGGD..DGGD....", "..DggGDDGggD....", "...DDgggDDD..DD.", "......gggD..DGGD", "....DDDgggDDGggD", "...DGGDDggggDDD.", "...DgGgDDgggD...", "....DDD..Dggg...", "..........ggD...", "...DD....Dgg....", "..DGGD..DggD....", "..DgGGDDggD.....", "...DDggggD......", ".....DDDD.......");

	private static readonly PixelArt Blossom = PixelArt.Of(Ink, "..pp.pp..", ".pPPpPPp.", ".pPPPPPp.", "..pPyPp..", ".pPPPPPp.", ".pPPpPPp.", "..pp.pp..");

	private static readonly PixelArt Sunflower = PixelArt.Of(Ink, "..y.y.y..", ".yyyyyyy.", "yyyddyyyy", ".yddddyy.", "yyyddyyyy", ".yyyyyyy.", "..y.y.y..");

	private static readonly PixelArt Maple = PixelArt.Of(Ink, "....r....", ".r.rOr.r.", ".rOOOOOr.", "rOOoOoOOr", ".rOOoOOr.", "..rOoOr..", "....d....", "....d....");

	private static readonly PixelArt Snow = PixelArt.Of(Ink, "....s....", ".s..w..s.", "..s.w.s..", "...sws...", "swwwwwwws", "...sws...", "..s.w.s..", ".s..w..s.", "....s....");

	private static readonly PixelArt Cat = PixelArt.Of(Ink, "k...k...........", "kCkkCk..........", "kCCCCk...kkkkk..", "kkCCkCkkkCCCCCk.", "kCCCCCCCCcCCCCCk", "kCnCCcCCCCcCCCkk", ".kkCCCcCCCCcCCkC", "...kkkkkkkkkkk.k");


	private static PixelArt Flower => Game1.currentSeason switch
	{
		"summer" => Sunflower,
		"fall" => Maple,
		"winter" => Snow,
		_ => Blossom
	};

	/// <summary>游戏物品图里的编号：星之果实、五彩碎片、防风草、钻石、向日葵、河豚。</summary>
	private static readonly int[] Keepsakes = { 434, 74, 24, 72, 421, 128 };

	/// <summary>当季作物：春防风草、夏甜瓜、秋南瓜、冬水晶果。</summary>
	public static void SeasonCrop(SpriteBatch b, int x, int y, int size) => Item(b, Game1.currentSeason switch { "summer" => 254, "fall" => 276, "winter" => 414, _ => 24 }, x, y, size);

	/// <summary>金币小图标。</summary>
	public static void Coin(SpriteBatch b, int x, int y, int scale) => b.Draw(Game1.mouseCursors, new Rectangle(x, y, 9 * scale, 10 * scale), new Rectangle(193, 373, 9, 10), Color.White);

	/// <summary>小窗口的装饰：四角铜片，左下一枝藤蔓开着当季的花。</summary>
	public static void DrawSmall(SpriteBatch b, Rectangle outer)
	{
		Corner.Draw(b, outer.X + 4, outer.Y + 4, 3);
		Corner.Draw(b, outer.Right - 34, outer.Y + 4, 3, flipX: true);
		Corner.Draw(b, outer.X + 4, outer.Bottom - 34, 3, flipY: true);
		Corner.Draw(b, outer.Right - 34, outer.Bottom - 34, 3, flipX: true, flipY: true);
		Vine.Draw(b, outer.X - 8, outer.Bottom - 50, 3, flipY: true);
		Flower.Draw(b, outer.X + 12, outer.Bottom - 54, 3);
		Item(b, Keepsakes[0], outer.Right - 26, outer.Y - 12, 32);
	}

	/// <summary>显示器边上的小摆设：猫趴在顶上，藤蔓爬上一角，底边摆几样小物件。</summary>
	public static void DrawMonitorDecor(SpriteBatch b, Rectangle outer)
	{
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		Cat.Draw(b, outer.X + 90, outer.Y - 18 + (Math.Sin(t * 1.6) > 0.3 ? 0 : 2), 4);
		Vine.Draw(b, outer.Right - 56, outer.Bottom - 120, 3, flipX: true);
		Flower.Draw(b, outer.Right - 34, outer.Bottom - 136, 3);
		for (int i = 0; i < 3; i++)
		{
			Item(b, Keepsakes[i], outer.Center.X + 60 + i * 44, outer.Bottom - 38, 32, (float)Math.Sin(t * 1.4 + i) * 2f);
		}
	}

	public static void Item(SpriteBatch b, int id, int x, int y, int size, float bob = 0f)
	{
		Rectangle source = Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, id, 16, 16);
		b.Draw(Game1.objectSpriteSheet, new Rectangle(x, y + (int)bob, size, size), source, Color.White);
	}

	/// <summary>神秘的符文圆环：两道虚线圈、八个符号、当中一颗星。很淡，只当纸上的底纹。</summary>
	public static void RuneCircle(SpriteBatch b, Vector2 c, float radius, Color ink, float spin = 0f, bool heart = true)
	{
		for (int ring = 0; ring < 2; ring++)
		{
			float r = radius * (ring == 0 ? 1f : 0.72f);
			int dots = (int)(r * 0.8f);
			for (int i = 0; i < dots; i += 2)
			{
				double a = i * Math.PI * 2 / dots + spin * (ring == 0 ? 1 : -1);
				Hearth.Fill(b, new Rectangle((int)(c.X + Math.Cos(a) * r), (int)(c.Y + Math.Sin(a) * r), 4, 4), ink);
			}
		}
		for (int i = 0; i < 8; i++)
		{
			double a = i * Math.PI / 4 + spin;
			Vector2 at = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius * 0.86f;
			Hearth.Fill(b, new Rectangle((int)at.X - 2, (int)at.Y - 8, 4, 16), ink);
			Hearth.Fill(b, new Rectangle((int)at.X - 6, (int)at.Y + (i % 2 == 0 ? -8 : 4), 12, 4), ink);
		}
		for (int i = 0; heart && i < 8; i++)
		{
			double a = i * Math.PI / 4 - spin;
			Vector2 from = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius * (i % 2 == 0 ? 0.2f : 0.28f);
			Vector2 to = c + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * radius * (i % 2 == 0 ? 0.56f : 0.42f);
			Hearth.Line(b, from, to, ink, 4f);
		}
		if (heart)
		{
			Hearth.Fill(b, new Rectangle((int)c.X - 8, (int)c.Y - 4, 16, 8), ink);
			Hearth.Fill(b, new Rectangle((int)c.X - 4, (int)c.Y - 8, 8, 16), ink);
		}
	}

	/// <summary>四角的结绳纹，用在纸面四周。</summary>
	public static void KnotCorner(SpriteBatch b, int x, int y, int dirX, int dirY, Color ink)
	{
		int[][] cells = { new[] { 0, 0, 40, 4 }, new[] { 0, 0, 4, 40 }, new[] { 8, 8, 20, 4 }, new[] { 8, 8, 4, 20 }, new[] { 16, 16, 8, 8 }, new[] { 32, 8, 4, 12 }, new[] { 8, 32, 12, 4 } };
		foreach (int[] c in cells)
		{
			int px = dirX > 0 ? x + c[0] : x - c[0] - c[2], py = dirY > 0 ? y + c[1] : y - c[1] - c[3];
			Hearth.Fill(b, new Rectangle(px, py, c[2], c[3]), ink);
		}
	}

	/// <summary>纸面底纹：右下角一圈缓缓转动的符文，四角结绳纹。画在内容下面。</summary>
	public static void PaperMarks(SpriteBatch b, Rectangle page)
	{
		Color ink = Hearth.Sapling * 0.2f;
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		RuneCircle(b, new Vector2(page.Right - 150, page.Bottom - 150), 118f, ink, (float)(t * 0.05));
		KnotCorner(b, page.X + 10, page.Y + 10, 1, 1, ink);
		KnotCorner(b, page.Right - 10, page.Y + 10, -1, 1, ink);
		KnotCorner(b, page.X + 10, page.Bottom - 10, 1, -1, ink);
		KnotCorner(b, page.Right - 10, page.Bottom - 10, -1, -1, ink);
	}

	public static void DrawFrame(SpriteBatch b, Rectangle outer)
	{
		Corner.Draw(b, outer.X + 4, outer.Y + 4, 4);
		Corner.Draw(b, outer.Right - 44, outer.Y + 4, 4, flipX: true);
		Corner.Draw(b, outer.X + 4, outer.Bottom - 44, 4, flipY: true);
		Corner.Draw(b, outer.Right - 44, outer.Bottom - 44, 4, flipX: true, flipY: true);
		Vine.Draw(b, outer.X - 10, outer.Bottom - 74, 4, flipY: true);
		Vine.Draw(b, outer.Right - 44, outer.Bottom - 120, 3, flipX: true);
		Vine.Draw(b, outer.X + 46, outer.Bottom - 40, 3);
		Flower.Draw(b, outer.X + 18, outer.Bottom - 76, 4);
		Flower.Draw(b, outer.X + 92, outer.Bottom - 34, 3);
		Flower.Draw(b, outer.Right - 34, outer.Bottom - 136, 3);
		Flower.Draw(b, outer.Right - 150, outer.Bottom - 20, 3);
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		// 下边框上摆一排小物件，慢慢上下浮动。
		int step = Math.Max(64, outer.Width / (Keepsakes.Length + 3));
		for (int i = 0; i < Keepsakes.Length; i++)
		{
			Item(b, Keepsakes[i], outer.X + step * (i + 2) - 16, outer.Bottom - 26, 32, (float)Math.Sin(t * 1.4 + i) * 2f);
		}
		Cat.Draw(b, outer.X + 150, outer.Y - 18 + (Math.Sin(t * 1.6) > 0.3 ? 0 : 2), 4);
	}

	/// <summary>木板下面挂的小灯笼，光微微闪。</summary>
	public static void Lantern(SpriteBatch b, int x, int y)
	{
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		float glow = 0.75f + (float)Math.Sin(t * 3 + x) * 0.2f;
		Hearth.Fill(b, new Rectangle(x + 8, y, 4, 8), new Color(70, 50, 34));
		Hearth.Fill(b, new Rectangle(x, y + 8, 20, 4), new Color(70, 50, 34));
		Hearth.Fill(b, new Rectangle(x, y + 12, 20, 16), new Color(70, 50, 34));
		Hearth.Fill(b, new Rectangle(x + 4, y + 12, 12, 12), new Color(255, 214, 110) * glow);
		Hearth.Fill(b, new Rectangle(x + 8, y + 12, 4, 12), new Color(255, 244, 190) * glow);
		Hearth.Fill(b, new Rectangle(x + 4, y + 28, 12, 4), new Color(70, 50, 34));
	}

	/// <summary>窗口外慢慢飘的花瓣（冬天是雪）。</summary>
	public static void DrawDrift(SpriteBatch b, int width, int height)
	{
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		Color mote = Game1.currentSeason switch
		{
			"summer" => new Color(190, 226, 110),
			"fall" => new Color(232, 134, 48),
			"winter" => Color.White,
			_ => new Color(255, 190, 210)
		};
		for (int i = 0; i < 16; i++)
		{
			double speed = 18 + i * 3 % 17;
			int y = (int)((t * speed + i * 97) % (height + 40)) - 20;
			int x = (int)((i * 173 % Math.Max(1, width)) + Math.Sin(t * 0.8 + i) * 26);
			Hearth.Fill(b, new Rectangle(x + 3, y, 6, 3), mote * 0.8f);
			Hearth.Fill(b, new Rectangle(x, y + 3, 9, 3), mote * 0.8f);
			Hearth.Fill(b, new Rectangle(x, y + 6, 6, 3), mote * 0.55f);
		}
	}
}
