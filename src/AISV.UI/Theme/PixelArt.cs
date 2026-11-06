using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AISV.UI.Theme;

/// <summary>写在代码里的小图：每个字符一个像素，'.' 透明。第一次画的时候烘成一张贴图。</summary>
public sealed class PixelArt
{
	private readonly Color[] _pixels;

	private Texture2D? _sheet;

	private PixelArt(int width, int height, Color[] pixels)
	{
		Width = width;
		Height = height;
		_pixels = pixels;
	}

	public int Width { get; }

	public int Height { get; }

	public static PixelArt Of(IReadOnlyDictionary<char, Color> inks, params string[] rows)
	{
		int width = rows.Length == 0 ? 0 : rows.Max(r => r.Length);
		Color[] pixels = new Color[width * rows.Length];
		for (int i = 0; i < pixels.Length; i++)
		{
			string row = rows[i / width];
			int column = i % width;
			pixels[i] = column < row.Length && inks.TryGetValue(row[column], out Color ink) ? ink : Color.Transparent;
		}
		return new PixelArt(width, rows.Length, pixels);
	}

	private Texture2D Sheet(GraphicsDevice device)
	{
		if (_sheet == null || _sheet.IsDisposed)
		{
			_sheet = new Texture2D(device, Width, Height);
			_sheet.SetData(_pixels);
		}
		return _sheet;
	}

	/// <param name="columns">只画左边这么多列（画半颗星用）。</param>
	public void Draw(SpriteBatch b, int x, int y, int scale, float alpha = 1f, int columns = int.MaxValue, bool flipX = false, bool flipY = false)
	{
		int shown = Math.Min(columns, Width);
		if (shown <= 0 || Height == 0)
		{
			return;
		}
		SpriteEffects mirror = (flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None) | (flipY ? SpriteEffects.FlipVertically : SpriteEffects.None);
		Rectangle target = new Rectangle(x + (flipX ? Width - shown : 0) * scale, y, shown * scale, Height * scale);
		b.Draw(Sheet(b.GraphicsDevice), target, new Rectangle(0, 0, shown, Height), Color.White * alpha, 0f, Vector2.Zero, mirror, 0f);
	}

	/// <summary>放大到能放进这个框的最大整数倍，居中画。</summary>
	public void DrawIn(SpriteBatch b, Rectangle box, float alpha = 1f)
	{
		int scale = Math.Max(1, Math.Min(box.Width / Math.Max(1, Width), box.Height / Math.Max(1, Height)));
		Draw(b, box.Center.X - Width * scale / 2, box.Center.Y - Height * scale / 2, scale, alpha);
	}

	/// <summary>常用的颜色，再加上每张图自己的。</summary>
	public static Dictionary<char, Color> Inks(params (char Key, Color Color)[] extra)
	{
		Dictionary<char, Color> inks = new Dictionary<char, Color>
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
			inks[key] = color;
		}
		return inks;
	}
}
