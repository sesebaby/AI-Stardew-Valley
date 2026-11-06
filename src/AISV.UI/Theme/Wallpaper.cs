using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Theme;

/// <summary>手机和电脑桌面的壁纸：用方块拼出来，会慢慢动。</summary>
public static class Wallpaper
{
	public static readonly string[] Names = { "meadow", "forest", "night" };

	private const int Px = 4;

	private static double Now => Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;

	public static void Draw(SpriteBatch b, Rectangle r, string? name)
	{
		Hearth.BeginClip(b, r);
		switch (name)
		{
			case "forest":
				Forest(b, r);
				break;
			case "night":
				Night(b, r);
				break;
			default:
				Meadow(b, r);
				break;
		}
		Hearth.EndClip(b);
	}

	private static void Sky(SpriteBatch b, Rectangle r, Color top, Color bottom, int bands = 12)
	{
		int h = r.Height / bands + 1;
		for (int i = 0; i < bands; i++)
		{
			Hearth.Fill(b, new Rectangle(r.X, r.Y + i * h, r.Width, Math.Min(h, r.Bottom - r.Y - i * h)), Color.Lerp(top, bottom, i / (float)(bands - 1)));
		}
	}

	/// <summary>一道起伏的山坡，从 baseline 往下填满。</summary>
	private static void Hill(SpriteBatch b, Rectangle r, float baseline, float swell, float wave, float phase, Color fill, Color edge)
	{
		const int step = 8;
		for (int x = r.X; x < r.Right; x += step)
		{
			int y = r.Y + (int)(r.Height * baseline + Math.Sin(x * wave + phase) * swell) / Px * Px;
			int w = Math.Min(step, r.Right - x);
			Hearth.Fill(b, new Rectangle(x, y, w, Px), edge);
			Hearth.Fill(b, new Rectangle(x, y + Px, w, r.Bottom - y - Px), fill);
		}
	}

	private static int Hash(int a, int b) => Hearth.Speckle(a, b) & 0x7fffffff;

	private static void Meadow(SpriteBatch b, Rectangle r)
	{
		Sky(b, r, new Color(112, 188, 236), new Color(214, 240, 246));
		for (int i = 0; i < 4; i++)
		{
			int x = r.X + (int)((Now * (4 + i) + i * 311) % (r.Width + 240)) - 120, y = r.Y + 34 + i * 46;
			Hearth.FillRounded(b, new Rectangle(x, y, 120 - i * 10, 24), Color.White * 0.9f, 8);
			Hearth.FillRounded(b, new Rectangle(x + 24, y - 14, 60 - i * 4, 22), Color.White * 0.9f, 8);
		}
		Hill(b, r, 0.62f, 26f, 0.009f, 1.2f, new Color(136, 196, 96), new Color(168, 216, 116));
		// 远处的小农舍
		int hx = r.X + r.Width * 2 / 3, hy = r.Y + (int)(r.Height * 0.6f) - 28;
		Hearth.Fill(b, new Rectangle(hx, hy, 56, 40), new Color(200, 92, 66));
		Hearth.Fill(b, new Rectangle(hx + 22, hy + 16, 12, 24), new Color(110, 58, 40));
		for (int i = 0; i < 4; i++)
		{
			Hearth.Fill(b, new Rectangle(hx - 8 + i * 8, hy - 8 - i * 6, 72 - i * 16, 8), new Color(120, 70, 50));
		}
		Hill(b, r, 0.76f, 18f, 0.013f, 3.4f, new Color(96, 168, 72), new Color(128, 192, 84));
		for (int i = 0; i < 44; i++)
		{
			int h = Hash(i, 7);
			int x = r.X + h % Math.Max(1, r.Width), y = r.Y + (int)(r.Height * 0.8f) + (h >> 8) % Math.Max(1, (int)(r.Height * 0.18f));
			Color petal = (h >> 3 & 3) switch { 0 => new Color(255, 236, 130), 1 => new Color(252, 170, 190), 2 => Color.White, _ => new Color(190, 170, 250) };
			Hearth.Fill(b, new Rectangle(x, y, Px, Px), petal);
			Hearth.Fill(b, new Rectangle(x, y + Px, Px, Px), new Color(62, 124, 50));
		}
	}

	private static void Pines(SpriteBatch b, Rectangle r, float baseline, int gap, int height, Color color, int seed)
	{
		for (int x = r.X - gap + seed % gap; x < r.Right; x += gap)
		{
			int tall = height + Hash(x / gap, seed) % (height / 2 + 1);
			int foot = r.Y + (int)(r.Height * baseline);
			for (int row = 0; row < tall; row += 12)
			{
				int half = (row + 12) * gap / (tall * 2) / 4 * 4 + 4;
				Rectangle slab = new Rectangle(x + gap / 2 - half, foot - tall + row, half * 2, 12);
				Hearth.Fill(b, Rectangle.Intersect(slab, r), color);
			}
			Hearth.Fill(b, new Rectangle(r.X, foot, r.Width, r.Bottom - foot), color);
		}
	}

	private static void Forest(SpriteBatch b, Rectangle r)
	{
		Sky(b, r, new Color(244, 196, 130), new Color(170, 208, 150));
		Hearth.FillRounded(b, new Rectangle(r.Right - r.Width / 3, r.Y + r.Height / 6, 70, 70), new Color(255, 236, 170), 28);
		Pines(b, r, 0.62f, 74, 150, new Color(112, 156, 110), 31);
		Pines(b, r, 0.76f, 96, 200, new Color(66, 116, 78), 13);
		Pines(b, r, 0.94f, 124, 250, new Color(34, 72, 52), 57);
		for (int i = 0; i < 18; i++)
		{
			int h = Hash(i, 3);
			float glow = 0.5f + 0.5f * (float)Math.Sin(Now * (1 + i % 3) + i);
			int x = r.X + (h + (int)(Math.Sin(Now * 0.3 + i) * 30)) % Math.Max(1, r.Width), y = r.Y + r.Height / 2 + (h >> 7) % Math.Max(1, r.Height / 2 - 8);
			Hearth.Fill(b, new Rectangle(x, y, Px, Px), new Color(250, 250, 150) * glow);
		}
	}

	private static void Night(SpriteBatch b, Rectangle r)
	{
		Sky(b, r, new Color(18, 20, 62), new Color(76, 62, 132));
		for (int i = 0; i < 90; i++)
		{
			int h = Hash(i, 11);
			float twinkle = 0.35f + 0.65f * (float)Math.Abs(Math.Sin(Now * (0.5 + i % 5 * 0.3) + i));
			int size = h % 7 == 0 ? Px : 2;
			Hearth.Fill(b, new Rectangle(r.X + h % Math.Max(1, r.Width), r.Y + (h >> 9) % Math.Max(1, r.Height * 2 / 3), size, size), Color.White * twinkle);
		}
		int mx = r.Right - r.Width / 4, my = r.Y + r.Height / 6;
		Hearth.FillRounded(b, new Rectangle(mx, my, 76, 76), new Color(250, 240, 200), 28);
		Hearth.FillRounded(b, new Rectangle(mx + 24, my - 4, 62, 66), new Color(32, 32, 84), 24);
		double shoot = Now % 13;
		if (shoot < 1)
		{
			int sx = r.X + r.Width / 4 + (int)(shoot * 260), sy = r.Y + 40 + (int)(shoot * 110);
			for (int i = 0; i < 6; i++)
			{
				Hearth.Fill(b, new Rectangle(sx - i * 10, sy - i * 4, Px, Px), Color.White * (1f - i / 6f));
			}
		}
		Hill(b, r, 0.8f, 20f, 0.01f, 0.7f, new Color(24, 30, 62), new Color(40, 46, 88));
		int hx = r.X + r.Width / 4, hy = r.Y + (int)(r.Height * 0.8f) - 24;
		Hearth.Fill(b, new Rectangle(hx, hy, 52, 36), new Color(16, 18, 44));
		for (int i = 0; i < 4; i++)
		{
			Hearth.Fill(b, new Rectangle(hx - 8 + i * 8, hy - 8 - i * 6, 68 - i * 16, 8), new Color(16, 18, 44));
		}
		Hearth.Fill(b, new Rectangle(hx + 18, hy + 10, 16, 12), new Color(255, 214, 110) * (0.8f + 0.2f * (float)Math.Sin(Now * 2)));
	}
}
