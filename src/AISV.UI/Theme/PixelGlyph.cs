using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AISV.UI.Theme;

/// <summary>用字符画的小像素图：每个字符一格，'.' 是透明，其余按调色板上色。</summary>
public sealed class PixelGlyph
{
	private readonly List<(int X, int Y, int W, Color Color)> _runs = new List<(int, int, int, Color)>();

	public PixelGlyph(IReadOnlyDictionary<char, Color> palette, params string[] rows)
	{
		Height = rows.Length;
		for (int y = 0; y < rows.Length; y++)
		{
			string row = rows[y];
			Width = Math.Max(Width, row.Length);
			int x = 0;
			while (x < row.Length)
			{
				char c = row[x];
				int start = x;
				while (x < row.Length && row[x] == c)
				{
					x++;
				}
				if (c != '.' && palette.TryGetValue(c, out Color color))
				{
					_runs.Add((start, y, x - start, color));
				}
			}
		}
	}

	public int Width { get; }

	public int Height { get; }

	/// <param name="columns">只画左边这么多列（画半颗星用）。</param>
	public void Draw(SpriteBatch b, int x, int y, int scale, float alpha = 1f, int columns = int.MaxValue)
	{
		foreach ((int rx, int ry, int rw, Color color) in _runs)
		{
			int w = Math.Min(rw, columns - rx);
			if (w > 0)
			{
				AisvTheme.Fill(b, new Rectangle(x + rx * scale, y + ry * scale, w * scale, scale), color * alpha);
			}
		}
	}

	/// <summary>放大到能放进这个框的最大整数倍，居中画。</summary>
	public void DrawIn(SpriteBatch b, Rectangle r, float alpha = 1f)
	{
		int scale = Math.Max(1, Math.Min(r.Width / Math.Max(1, Width), r.Height / Math.Max(1, Height)));
		Draw(b, r.Center.X - Width * scale / 2, r.Center.Y - Height * scale / 2, scale, alpha);
	}

	/// <summary>常用的几种颜色加上各图自己的。</summary>
	public static Dictionary<char, Color> Palette(params (char Key, Color Color)[] extra)
	{
		Dictionary<char, Color> p = new Dictionary<char, Color>
		{
			['k'] = new Color(60, 42, 30),
			['w'] = Color.White,
			['W'] = new Color(236, 240, 246),
			['g'] = new Color(200, 206, 216),
			['y'] = new Color(255, 210, 70),
			['Y'] = new Color(255, 236, 150),
			['o'] = new Color(240, 150, 40),
			['r'] = new Color(214, 69, 65),
			['R'] = new Color(150, 40, 36),
			['G'] = new Color(96, 168, 64),
			['D'] = new Color(58, 104, 32),
			['b'] = new Color(90, 150, 220),
			['B'] = new Color(50, 96, 170),
			['p'] = new Color(214, 120, 150),
			['P'] = new Color(150, 96, 200),
			['c'] = new Color(160, 104, 56),
			['C'] = new Color(122, 74, 36),
			['s'] = new Color(250, 236, 205),
			['n'] = new Color(120, 120, 140)
		};
		foreach ((char key, Color color) in extra)
		{
			p[key] = color;
		}
		return p;
	}
}
