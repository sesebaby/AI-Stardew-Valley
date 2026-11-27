using System;
using System.Collections.Generic;
using AISV.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Theme;

public static class Hearth
{
	public static Color Timber = new Color(142, 84, 40);
	public static Color Bark = new Color(82, 42, 20);
	public static Color Sapling = new Color(196, 134, 72);

	public static Color Slip = new Color(255, 241, 206);
	public static Color SlipEdge = new Color(150, 92, 48);
	public static Color Parchment = new Color(250, 226, 176);
	public static Color ParchmentDeep = new Color(240, 204, 146);
	public static Color Seam = new Color(214, 168, 110);

	public static Color Quill = new Color(86, 34, 14);
	public static Color QuillSoft = new Color(124, 74, 40);
	public static Color QuillPale = new Color(158, 108, 66);
	public static readonly Color Dusk = new Color(52, 30, 16) * 0.2f;

	public static Color Leaf = new Color(92, 148, 44);
	public static Color LeafDeep = new Color(56, 104, 28);
	public static Color LeafPale = new Color(218, 230, 158);
	public static Color Honey = new Color(255, 226, 150);
	public static readonly Color Amber = new Color(214, 142, 20);
	public static readonly Color AmberPale = new Color(255, 226, 140);
	public static readonly Color Rose = new Color(214, 120, 150);
	public static readonly Color RosePale = new Color(252, 228, 236);
	public static readonly Color Ember = new Color(219, 84, 54);
	public static readonly Color Rust = new Color(214, 120, 52);
	public static readonly Color Berry = new Color(200, 70, 60);

	/// <summary>窗框的染色，配合外观。</summary>
	public static Color FrameTint = Color.White;

	private static string _skin = "cottage";

	/// <summary>换外观：cottage 木屋、forest 森林、starry 星夜。</summary>
	public static void UseSkin(string? skin)
	{
		string name = skin is "forest" or "starry" ? skin : "cottage";
		if (name == _skin)
		{
			return;
		}
		_skin = name;
		if (name == "forest")
		{
			Bark = new Color(38, 58, 30);
			Timber = new Color(72, 104, 52);
			Sapling = new Color(124, 156, 84);
			Parchment = new Color(232, 234, 190);
			ParchmentDeep = new Color(210, 218, 158);
			Slip = new Color(246, 248, 218);
			SlipEdge = new Color(84, 112, 58);
			Seam = new Color(170, 186, 120);
			Quill = new Color(38, 58, 28);
			QuillSoft = new Color(70, 94, 54);
			QuillPale = new Color(108, 130, 84);
			Leaf = new Color(176, 120, 40);
			LeafDeep = new Color(122, 78, 20);
			LeafPale = new Color(240, 222, 160);
			Honey = new Color(222, 232, 150);
			Plank = new Color(176, 200, 116);
			PlankLit = new Color(198, 220, 134);
			PlankLeaf = new Color(226, 170, 76);
			FrameTint = new Color(176, 226, 150);
		}
		else if (name == "starry")
		{
			Bark = new Color(30, 28, 74);
			Timber = new Color(62, 58, 128);
			Sapling = new Color(110, 104, 186);
			Parchment = new Color(228, 226, 248);
			ParchmentDeep = new Color(206, 202, 238);
			Slip = new Color(244, 243, 255);
			SlipEdge = new Color(78, 72, 150);
			Seam = new Color(170, 166, 220);
			Quill = new Color(38, 32, 88);
			QuillSoft = new Color(76, 70, 130);
			QuillPale = new Color(116, 110, 170);
			Leaf = new Color(150, 92, 196);
			LeafDeep = new Color(104, 60, 150);
			LeafPale = new Color(232, 216, 246);
			Honey = new Color(252, 232, 160);
			Plank = new Color(176, 172, 232);
			PlankLit = new Color(196, 192, 246);
			PlankLeaf = new Color(244, 196, 96);
			FrameTint = new Color(150, 150, 255);
		}
		else
		{
			Bark = new Color(82, 42, 20);
			Timber = new Color(142, 84, 40);
			Sapling = new Color(196, 134, 72);
			Parchment = new Color(250, 226, 176);
			ParchmentDeep = new Color(240, 204, 146);
			Slip = new Color(255, 241, 206);
			SlipEdge = new Color(150, 92, 48);
			Seam = new Color(214, 168, 110);
			Quill = new Color(86, 34, 14);
			QuillSoft = new Color(124, 74, 40);
			QuillPale = new Color(158, 108, 66);
			Leaf = new Color(92, 148, 44);
			LeafDeep = new Color(56, 104, 28);
			LeafPale = new Color(218, 230, 158);
			Honey = new Color(255, 226, 150);
			Plank = new Color(236, 192, 124);
			PlankLit = new Color(250, 216, 150);
			PlankLeaf = new Color(136, 190, 74);
			FrameTint = Color.White;
		}
	}

	public const int Round = 12;

	public const int WindowBorder = 18;

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

	/// <summary>像素风的圆角：每级 4 像素的台阶。</summary>
	public static void FillRounded(SpriteBatch b, Rectangle r, Color c, int radius = Round)
	{
		if (r.Width <= 0 || r.Height <= 0)
		{
			return;
		}
		int steps = Math.Min(Math.Clamp(radius / 4, 0, 3), Math.Min(r.Width, r.Height) / 8);
		const int px = 4;
		Fill(b, new Rectangle(r.X, r.Y + steps * px, r.Width, r.Height - steps * px * 2), c);
		for (int i = 0; i < steps; i++)
		{
			int inset = (steps - i) * px;
			Fill(b, new Rectangle(r.X + inset, r.Y + i * px, r.Width - inset * 2, px), c);
			Fill(b, new Rectangle(r.X + inset, r.Bottom - (i + 1) * px, r.Width - inset * 2, px), c);
		}
	}

	public static void Bordered(SpriteBatch b, Rectangle r, Color fill, Color border, int radius = Round, int thickness = 2)
	{
		FillRounded(b, r, border, radius);
		FillRounded(b, Inflate(r, -thickness), fill, Math.Max(0, radius - thickness));
	}

	public static void CastShadow(SpriteBatch b, Rectangle r, int radius = Round, int offset = 4) => FillRounded(b, new Rectangle(r.X + 2, r.Y + offset, r.Width, r.Height), Dusk, radius);

	/// <summary>游戏自己的菜单木框，里面铺羊皮纸，四角钉铜片。返回纸面。</summary>
	public static Rectangle WoodFrame(SpriteBatch b, Rectangle outer)
	{
		IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), outer.X, outer.Y, outer.Width, outer.Height, FrameTint, 1f, drawShadow: true);
		Rectangle page = Inflate(outer, -WindowBorder);
		Paper(b, page);
		InnerShadow(b, page);
		return page;
	}

	/// <summary>同样的两个数总得到同一个看着随机的数，用来撒斑点和星星。</summary>
	public static int Speckle(int a, int b)
	{
		uint mixed = unchecked((uint)a * 2654435761u ^ (uint)b * 40503u);
		mixed ^= mixed >> 13;
		return unchecked((int)(mixed * 1274126177u));
	}

	/// <summary>带细小纤维斑点的羊皮纸。</summary>
	public static void Paper(SpriteBatch b, Rectangle r)
	{
		Fill(b, r, Parchment);
		const int tile = 8;
		for (int y = r.Y; y < r.Bottom; y += tile)
		{
			for (int x = r.X; x < r.Right; x += tile)
			{
				int h = Speckle(x / tile, y / tile);
				if ((h & 15) == 0 && x + 4 <= r.Right && y + 4 <= r.Bottom)
				{
					Fill(b, new Rectangle(x, y, (h & 64) == 0 ? 4 : 8, 4), (h & 32) == 0 ? ParchmentDeep * 0.5f : Slip * 0.7f);
				}
			}
		}
	}

	private static void InnerShadow(SpriteBatch b, Rectangle r)
	{
		for (int i = 0; i < 3; i++)
		{
			Color c = Bark * (0.22f - i * 0.07f);
			Fill(b, new Rectangle(r.X, r.Y + i * 4, r.Width, 4), c);
			Fill(b, new Rectangle(r.X + i * 4, r.Y, 4, r.Height), c);
			Fill(b, new Rectangle(r.Right - (i + 1) * 4, r.Y, 4, r.Height), c * 0.6f);
		}
	}

	/// <summary>横幅式小标题：两头剪成燕尾的布条。</summary>
	public static void Ribbon(SpriteBatch b, string text, int x, int y, float scale = 0.76f)
	{
		int w = (int)Width(text, scale) + 40, h = LineHeight(scale) + 10;
		Rectangle band = new Rectangle(x, y, w, h);
		Fill(b, new Rectangle(band.X - 12, band.Y + 6, 16, h - 2), new Color(150, 62, 38));
		Fill(b, new Rectangle(band.Right - 4, band.Y + 6, 16, h - 2), new Color(150, 62, 38));
		Fill(b, new Rectangle(band.X - 12, band.Center.Y + 2, 6, 4), Parchment);
		Fill(b, new Rectangle(band.Right + 6, band.Center.Y + 2, 6, 4), Parchment);
		Fill(b, band, new Color(196, 84, 52));
		Fill(b, new Rectangle(band.X, band.Y, w, 4), new Color(232, 128, 84));
		Fill(b, new Rectangle(band.X, band.Bottom - 4, w, 4), new Color(150, 62, 38));
		Ink(b, text, new Vector2(band.X + 20, band.Y + 6), new Color(255, 240, 206), scale);
	}

	/// <summary>手画感的点线分隔，中间一枚小叶子。</summary>
	public static void Dotted(SpriteBatch b, int x, int y, int w, bool leaf = false)
	{
		for (int i = 0; i + 4 <= w; i += 12)
		{
			Fill(b, new Rectangle(x + i, y, 6, 2), Seam);
		}
		if (leaf)
		{
			Fill(b, new Rectangle(x + w / 2 - 8, y - 4, 16, 10), Parchment);
			Fill(b, new Rectangle(x + w / 2 - 4, y - 2, 8, 2), Leaf);
			Fill(b, new Rectangle(x + w / 2 - 6, y, 12, 2), LeafDeep);
			Fill(b, new Rectangle(x + w / 2 - 2, y + 2, 4, 2), Leaf);
		}
	}

	/// <summary>嵌在纸面上的小纸片：深色描边、上亮下暗。</summary>
	public static void CardSlip(SpriteBatch b, Rectangle r, bool highlight = false, Color? fill = null)
	{
		Fill(b, new Rectangle(r.X + 4, r.Bottom, r.Width - 4, 4), Bark * 0.25f);
		Fill(b, new Rectangle(r.Right, r.Y + 4, 4, r.Height), Bark * 0.25f);
		Bordered(b, r, fill ?? (highlight ? Honey : Slip), Bark, 8, 4);
		Fill(b, new Rectangle(r.X + 8, r.Y + 4, r.Width - 16, 4), Color.White * 0.55f);
		Fill(b, new Rectangle(r.X + 8, r.Bottom - 8, r.Width - 16, 4), SlipEdge * 0.35f);
	}

	public static void Field(SpriteBatch b, Rectangle r, bool focused)
	{
		Bordered(b, r, new Color(255, 248, 224), focused ? Amber : Bark, 8, 4);
		Fill(b, new Rectangle(r.X + 8, r.Y + 4, r.Width - 16, 4), SlipEdge * 0.45f);
	}

	/// <summary>一块上亮下暗、深色描边的小木牌。</summary>
	public static void Plate(SpriteBatch b, Rectangle r, Color wood)
	{
		Fill(b, new Rectangle(r.X + 4, r.Bottom, r.Width - 4, 4), Bark * 0.35f);
		Bordered(b, r, wood, Bark, 8, 4);
		Fill(b, new Rectangle(r.X + 8, r.Y + 4, r.Width - 16, 4), Color.Lerp(wood, Color.White, 0.42f));
		Fill(b, new Rectangle(r.X + 8, r.Bottom - 8, r.Width - 16, 4), Color.Lerp(wood, Bark, 0.32f));
	}

	public static Color Plank = new Color(236, 192, 124);

	public static Color PlankLit = new Color(250, 216, 150);

	public static Color PlankLeaf = new Color(136, 190, 74);

	public static void PressPlate(SpriteBatch b, Rectangle r, string label, bool primary = false, bool hover = false, bool disabled = false, float scale = 1f)
	{
		Rectangle plate = hover && !disabled ? new Rectangle(r.X, r.Y - 2, r.Width, r.Height) : r;
		Plate(b, plate, disabled ? new Color(214, 200, 176) : primary ? hover ? new Color(158, 208, 92) : PlankLeaf : hover ? PlankLit : Plank);
		InkMiddle(b, label, plate, disabled ? QuillPale : primary ? new Color(32, 56, 14) : Quill, scale);
	}

	/// <summary>顶上的大页签：挂在链子上的木牌，选中的放下来一点、亮一点。</summary>
	/// <summary>挂在木板下面的页签：两条链子吊一块牌子，选中的沉下去一点。</summary>
	public static void HangingSign(SpriteBatch b, Rectangle r, string label, bool selected, bool hover, PixelArt? icon = null)
	{
		Rectangle plate = selected ? new Rectangle(r.X, r.Y + 4, r.Width, r.Height) : hover ? new Rectangle(r.X, r.Y + 2, r.Width, r.Height) : r;
		foreach (int x in new[] { plate.X + 14, plate.Right - 18 })
		{
			for (int y = r.Y - 14; y < plate.Y + 2; y += 8)
			{
				Fill(b, new Rectangle(x, y, 4, 4), new Color(206, 186, 140));
				Fill(b, new Rectangle(x, y + 4, 4, 4), new Color(86, 62, 40));
			}
		}
		Plate(b, plate, selected ? new Color(255, 224, 130) : hover ? PlankLit : Plank);
		Rectangle words = plate;
		if (icon != null)
		{
			icon.Draw(b, plate.X + 6, plate.Center.Y - icon.Height, 2);
			words = new Rectangle(plate.X + 38, plate.Y, plate.Width - 44, plate.Height);
		}
		InkMiddle(b, label, words, selected ? new Color(120, 44, 10) : Quill);
	}

	public static void PaperTab(SpriteBatch b, Rectangle r, string label, bool selected, bool hover, float scale = 1f)
	{
		if (selected)
		{
			Bordered(b, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height + 2), Slip, Bark, 8, 4);
			Fill(b, new Rectangle(r.X + 12, r.Bottom - 6, r.Width - 24, 4), new Color(196, 84, 52));
		}
		else if (hover)
		{
			FillRounded(b, new Rectangle(r.X + 4, r.Y + 6, r.Width - 8, r.Height - 8), Honey * 0.8f, 8);
		}
		InkMiddle(b, label, r, selected ? new Color(120, 44, 10) : QuillSoft, scale);
	}

	/// <summary>一小块贴纸：放在 at，返回占了多大。</summary>
	public static Rectangle Tag(SpriteBatch b, Vector2 at, string text, Color fill, Color ink, float scale = 0.75f)
	{
		Rectangle tag = new Rectangle((int)at.X, (int)at.Y, TagWidth(text, scale), LineHeight(scale) + 10);
		FillRounded(b, tag, fill, 8);
		InkMiddle(b, text, tag, ink, scale);
		return tag;
	}

	public static int TagWidth(string text, float scale = 0.75f) => (int)Width(text, scale) + 18;

	/// <summary>小木闸开关：打开时是嫩绿的槽，滑块是一块木片。</summary>
	public static void Latch(SpriteBatch b, Rectangle r, bool on, bool disabled = false)
	{
		Bordered(b, r, disabled ? new Color(210, 190, 160) : on ? new Color(124, 180, 62) : new Color(176, 136, 92), Bark, 8, 4);
		Fill(b, new Rectangle(r.X + 8, r.Y + 4, r.Width - 16, 4), Color.Black * 0.14f);
		int size = r.Height - 8;
		Rectangle knob = new Rectangle(on ? r.Right - size - 4 : r.X + 4, r.Y + 4, size, size);
		Bordered(b, knob, new Color(238, 196, 128), Bark, 4, 4);
		Fill(b, new Rectangle(knob.X + 8, knob.Y + 8, 4, knob.Height - 16), new Color(176, 116, 60));
		Fill(b, new Rectangle(knob.Right - 12, knob.Y + 8, 4, knob.Height - 16), new Color(176, 116, 60));
	}

	/// <summary>木槽里的进度条，上沿一道高光。</summary>
	public static void Gauge(SpriteBatch b, Rectangle bar, float amount, Color? ink = null)
	{
		Rectangle groove = bar.Height >= 8 ? Inflate(bar, -2) : bar;
		Fill(b, bar, Bark * 0.6f);
		Fill(b, groove, ParchmentDeep);
		Rectangle poured = new Rectangle(groove.X, groove.Y, (int)(groove.Width * Math.Clamp(amount, 0f, 1f)), groove.Height);
		Fill(b, poured, ink ?? Leaf);
		if (groove.Height >= 6)
		{
			Fill(b, new Rectangle(poured.X, poured.Y, poured.Width, 2), Color.White * 0.3f);
		}
	}

	public static void Rail(SpriteBatch b, Rectangle track, int offset, int maxOffset)
	{
		const int thumb = 40;
		if (maxOffset <= 0 || track.Height < thumb)
		{
			return;
		}
		Rectangle groove = new Rectangle(track.Center.X - 10, track.Y, 24, track.Height);
		IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), groove.X, groove.Y, groove.Width, groove.Height, Color.White, 4f, drawShadow: false);
		float left = 1f - offset / (float)maxOffset;
		Rectangle knob = new Rectangle(groove.X, track.Bottom - thumb - (int)(left * (track.Height - thumb)), groove.Width, thumb);
		b.Draw(Game1.mouseCursors, knob, new Rectangle(435, 463, 6, 10), Color.White);
	}

	public static void CrossPlate(SpriteBatch b, Rectangle r, bool hover)
	{
		int size = Math.Min(r.Width, r.Height);
		b.Draw(Game1.mouseCursors, new Rectangle(r.Center.X - size / 2, r.Center.Y - size / 2 - (hover ? 2 : 0), size, size), new Rectangle(337, 494, 12, 12), hover ? new Color(255, 230, 200) : Color.White);
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

	/// <summary>原版字体只有整数倍、整像素位置画才清楚，缩到 0.8 这类小数倍时笔画会粘在一起：小于 1.5 一律按 1 倍，再大取整。</summary>
	public static float Crisp(float scale) => scale < 1.5f ? 1f : MathF.Round(scale);

	public static void Ink(SpriteBatch b, string text, Vector2 pos, Color color, float scale = 1f, SpriteFont? font = null)
	{
		if (!string.IsNullOrEmpty(text))
		{
			b.DrawString(font ?? Game1.smallFont, Glyphs.Fix(text), new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), color, 0f, Vector2.Zero, Crisp(scale), SpriteEffects.None, 1f);
		}
	}

	public static Vector2 Measure(string text, float scale = 1f, SpriteFont? font = null) => (font ?? Game1.smallFont).MeasureString(Glyphs.Fix(text ?? "")) * Crisp(scale);

	/// <summary>游戏字体没有粗体，错开一像素画两遍。</summary>
	public static void InkBold(SpriteBatch b, string text, Vector2 pos, Color color, float scale = 1f)
	{
		Ink(b, text, pos, color, scale);
		Ink(b, text, pos + new Vector2(1f, 0f), color, scale);
	}

	public static void InkMiddle(SpriteBatch b, string text, Rectangle r, Color color, float scale = 1f)
	{
		if (!string.IsNullOrEmpty(text))
		{
			Vector2 size = Measure(text, scale);
			Ink(b, text, new Vector2((int)(r.Center.X - size.X / 2f), (int)(r.Center.Y - size.Y / 2f) + 2), color, scale);
		}
	}

	public static float Width(string text, float scale = 1f) => Measure(text, scale).X;

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

	public static string Wrap(string text, int width, SpriteFont? font = null) => Game1.parseText(Glyphs.Fix(text ?? ""), font ?? Game1.smallFont, Math.Max(40, width));

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
			lines.AddRange(Wrap(p, (int)(width / Crisp(scale))).Split('\n'));
		}
		return lines;
	}

	public static int LineHeight(SpriteFont? font = null) => (int)(font ?? Game1.smallFont).MeasureString("Ay").Y;

	public static int LineHeight(float scale) => (int)(Game1.smallFont.LineSpacing * Crisp(scale));

	public static void Hearts(SpriteBatch b, Vector2 pos, int hearts, int max, int size = 14)
	{
		for (int i = 0; i < max; i++)
		{
			b.Draw(Game1.mouseCursors, new Rectangle((int)pos.X + i * (size + 4), (int)pos.Y, size, size * 6 / 7), i < hearts ? HeartFull : HeartEmpty, Color.White);
		}
	}

	public static void Heart(SpriteBatch b, Rectangle r) => b.Draw(Game1.mouseCursors, r, HeartFull, Color.White);

	/// <summary>木框头像：优先用立绘，没有就用行走图的脸，再没有就写一个字。</summary>
	public static void Cameo(SpriteBatch b, Rectangle r, NPC? npc, string fallback = "?")
	{
		Bordered(b, Inflate(r, 2), new Color(246, 214, 150), Bark, 8, 4);
		if (PortraitOf(npc) is Texture2D sheet)
		{
			b.Draw(sheet, Inflate(r, -2), Rectangle.Intersect(new Rectangle(0, 0, 64, 64), sheet.Bounds), Color.White);
		}
		else if (npc?.Sprite?.Texture is Texture2D walking)
		{
			b.Draw(walking, Inflate(r, -6), npc.getMugShotSourceRect(), Color.White);
		}
		else
		{
			Glyph(b, r, QuillPale, fallback);
		}
	}

	/// <summary>有些 mod 村民没有立绘，读的时候会抛异常。</summary>
	private static Texture2D? PortraitOf(NPC? npc)
	{
		try
		{
			return npc?.Portrait is { IsDisposed: false } sheet ? sheet : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>色块上写一两个字，字号按框的大小算。</summary>
	public static void Glyph(SpriteBatch b, Rectangle r, Color fill, string glyph)
	{
		FillRounded(b, r, fill, Math.Min(12, r.Width / 3));
		float fit = Math.Min(r.Width * 0.7f / Math.Max(1f, Width(glyph)), r.Height * 0.6f / Math.Max(1, LineHeight(1f)));
		InkMiddle(b, glyph, r, Color.White, Math.Clamp(fit, 0.6f, 2f));
	}

	public static void Magnifier(SpriteBatch b, Vector2 c)
	{
		int x = (int)c.X - 10, y = (int)c.Y - 10;
		Fill(b, new Rectangle(x + 2, y, 8, 2), QuillSoft);
		Fill(b, new Rectangle(x + 2, y + 10, 8, 2), QuillSoft);
		Fill(b, new Rectangle(x, y + 2, 2, 8), QuillSoft);
		Fill(b, new Rectangle(x + 10, y + 2, 2, 8), QuillSoft);
		Fill(b, new Rectangle(x + 2, y + 2, 8, 8), new Color(190, 226, 240));
		Line(b, new Vector2(x + 11, y + 11), new Vector2(x + 18, y + 18), Bark, 3f);
	}

	public static bool Hover(Rectangle r, Rectangle? clip = null)
	{
		int x = Game1.getMouseX(), y = Game1.getMouseY();
		return r.Contains(x, y) && (clip == null || clip.Value.Contains(x, y));
	}

	private static readonly Stack<Rectangle> Clips = new Stack<Rectangle>();

	private static int _clipTick;

	/// <summary>只画在这个框里。可以嵌套，里层不会画出外层。</summary>
	public static void BeginClip(SpriteBatch b, Rectangle clip)
	{
		// 上一帧画到一半出错时，留下的框不算数。
		if (_clipTick != Game1.ticks)
		{
			Clips.Clear();
			_clipTick = Game1.ticks;
		}
		Rectangle allowed = Clips.Count > 0 ? Clips.Peek() : b.GraphicsDevice.Viewport.Bounds;
		Clips.Push(Rectangle.Intersect(clip, allowed));
		RestartClipped(b);
	}

	public static void EndClip(SpriteBatch b)
	{
		Clips.TryPop(out _);
		RestartClipped(b);
	}

	private static void RestartClipped(SpriteBatch b)
	{
		b.End();
		if (Clips.Count == 0)
		{
			b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
			return;
		}
		b.GraphicsDevice.ScissorRectangle = Clips.Peek();
		b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, Scissor);
	}

	public static Rectangle Inflate(Rectangle r, int by) => new Rectangle(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);
}
