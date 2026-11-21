using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Theme;

/// <summary>头顶气泡：白底卡片，上面一行名字，长句自动换行。群聊和村民随口说的话都用这一种。</summary>
public static class HeadBubbles
{
	public const int Width = 360;

	private sealed class Said
	{
		public NPC Npc = null!;

		public string Text = "";

		public double Elapsed;

		public double Duration;

		/// <summary>心里话：灰色、带括号、思考泡的小圆点尾巴。</summary>
		public bool Thought;
	}

	private static readonly List<Said> Active = new List<Said>();

	public static Func<string, string> Display { get; set; } = name => name;

	/// <summary>替身（店里的顾客、店员、离家出走的孩子）显示真人的名字。</summary>
	private static string Who(StardewValley.NPC npc) => npc.modData.TryGetValue("AIStardewValley.Core/Real", out string? real) ? real : npc.Name;

	/// <summary>同一个人再说话就换掉上一句。</summary>
	public static void Show(NPC npc, string text, int durationMs)
	{
		Active.RemoveAll(s => s.Npc == npc);
		Active.Add(new Said { Npc = npc, Text = text.Trim(), Duration = durationMs > 0 ? durationMs : Math.Clamp(1500 + text.Length * 110, 3000, 9000) });
	}

	/// <summary>心里话（宠物的内心独白）。</summary>
	public static void ShowThought(NPC npc, string text, int durationMs)
	{
		Show(npc, text, durationMs);
		Active[Active.Count - 1].Thought = true;
	}

	public static void Clear() => Active.Clear();

	public static void Update(double elapsedMs)
	{
		if (Game1.paused || Game1.activeClickableMenu != null)
		{
			return;
		}
		foreach (Said s in Active)
		{
			s.Elapsed += elapsedMs;
		}
		Active.RemoveAll(s => s.Elapsed >= s.Duration || s.Npc.currentLocation == null);
	}

	public static void Draw(SpriteBatch b)
	{
		foreach (Said s in Active)
		{
			if (s.Npc.currentLocation != Game1.currentLocation || s.Npc.IsInvisible)
			{
				continue;
			}
			float alpha = (float)Math.Clamp(Math.Min(s.Elapsed / 150.0, (s.Duration - s.Elapsed) / 250.0), 0, 1);
			if (s.Thought)
			{
				DrawThought(b, Head(s.Npc), Display(Who(s.Npc)), s.Text, alpha);
				continue;
			}
			DrawBubble(b, Head(s.Npc), Display(Who(s.Npc)), s.Text, mine: false, alpha);
		}
	}

	/// <summary>气泡尾巴指向的位置（UI 坐标）；npc 为空时是农夫头顶。</summary>
	public static Vector2 Head(NPC? npc)
	{
		Vector2 world;
		if (npc is StardewValley.Characters.Pet)
		{
			// 宠物贴图 32 高但身子矮：气泡挨着头顶，不飘在半空。
			Point p = npc.StandingPixel;
			world = new Vector2(p.X, p.Y - 76);
		}
		else if (npc != null)
		{
			Point p = npc.StandingPixel;
			world = new Vector2(p.X, p.Y - (npc.Sprite?.SpriteHeight ?? 32) * 4 + 20);
		}
		else
		{
			Point p = Game1.player.StandingPixel;
			world = new Vector2(p.X, p.Y - 112);
		}
		return Utility.ModifyCoordinatesForUIScale(Game1.GlobalToLocal(Game1.viewport, world));
	}

	/// <summary>思考泡：浅灰底、括号里的字、两三个小圆点当尾巴。</summary>
	public static Rectangle DrawThought(SpriteBatch b, Vector2 anchor, string name, string text, float alpha)
	{
		if (alpha <= 0.01f || string.IsNullOrWhiteSpace(text))
		{
			return Rectangle.Empty;
		}
		string wrapped = Hearth.Wrap("（" + text.Trim() + "）", Width - 40);
		Vector2 size = Game1.smallFont.MeasureString(wrapped) * 0.9f;
		int nameH = name.Length > 0 ? 22 : 0;
		int w = (int)Math.Max(size.X, name.Length > 0 ? Hearth.Width(name, 0.65f) : 0) + 32;
		int h = (int)size.Y + 18 + nameH;
		int x = Math.Clamp((int)anchor.X - w / 2, 8, Math.Max(8, Game1.uiViewport.Width - w - 8));
		int y = Math.Max(8, (int)anchor.Y - h - 34);
		Rectangle r = new Rectangle(x, y, w, h);
		Color fill = new Color(238, 236, 232);
		Color border = new Color(150, 146, 140);
		Hearth.Bordered(b, r, fill * alpha, border * alpha, 18);
		int cx = Math.Clamp((int)anchor.X, r.X + 16, r.Right - 16);
		Hearth.Bordered(b, new Rectangle(cx - 7, r.Bottom + 4, 14, 12), fill * alpha, border * alpha, 6);
		Hearth.Bordered(b, new Rectangle(cx - 4, r.Bottom + 20, 8, 8), fill * alpha, border * alpha, 4);
		if (name.Length > 0)
		{
			Hearth.Ink(b, name, new Vector2(r.X + 16, r.Y + 7), new Color(110, 106, 100) * alpha, 0.65f);
		}
		b.DrawString(Game1.smallFont, wrapped, new Vector2(r.X + 16, r.Y + 9 + nameH), new Color(92, 86, 80) * alpha, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 1f);
		return r;
	}

	public static Rectangle DrawBubble(SpriteBatch b, Vector2 anchor, string name, string text, bool mine, float alpha)
	{
		if (alpha <= 0.01f || string.IsNullOrWhiteSpace(text))
		{
			return Rectangle.Empty;
		}
		string wrapped = Hearth.Wrap(text, Width);
		Vector2 size = Game1.smallFont.MeasureString(wrapped);
		int nameH = name.Length > 0 ? 24 : 0;
		float nameW = name.Length > 0 ? Hearth.Width(name, 0.7f) : 0;
		int w = (int)Math.Max(size.X, nameW) + 32;
		int h = (int)size.Y + 20 + nameH;
		int x = Math.Clamp((int)anchor.X - w / 2, 8, Math.Max(8, Game1.uiViewport.Width - w - 8));
		int y = Math.Max(8, (int)anchor.Y - h - 14);
		Rectangle r = new Rectangle(x, y, w, h);
		Color fill = mine ? new Color(214, 236, 190) : Hearth.Slip;
		Color border = mine ? new Color(139, 186, 102) : Hearth.SlipEdge;
		Hearth.CastShadow(b, r, 12, 4);
		Hearth.Bordered(b, r, fill * alpha, border * alpha, 12);
		int tx = Math.Clamp((int)anchor.X, r.X + 16, r.Right - 16);
		for (int i = 0; i < 4; i++)
		{
			int half = 8 - i * 2;
			Hearth.Fill(b, new Rectangle(tx - half - 2, r.Bottom - 2 + i * 3, (half + 2) * 2, 3), border * alpha);
			Hearth.Fill(b, new Rectangle(tx - half, r.Bottom - 2 + i * 3, half * 2, 3), fill * alpha);
		}
		if (name.Length > 0)
		{
			Hearth.Ink(b, name, new Vector2(r.X + 16, r.Y + 8), Hearth.LeafDeep * alpha, 0.7f);
		}
		Hearth.Ink(b, wrapped, new Vector2(r.X + 16, r.Y + 10 + nameH), Hearth.Quill * alpha);
		return r;
	}
}
