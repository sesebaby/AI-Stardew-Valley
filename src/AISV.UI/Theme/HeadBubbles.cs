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
	}

	private static readonly List<Said> Active = new List<Said>();

	public static Func<string, string> Display { get; set; } = name => name;

	/// <summary>同一个人再说话就换掉上一句。</summary>
	public static void Show(NPC npc, string text, int durationMs)
	{
		Active.RemoveAll(s => s.Npc == npc);
		Active.Add(new Said { Npc = npc, Text = text.Trim(), Duration = durationMs > 0 ? durationMs : Math.Clamp(1500 + text.Length * 110, 3000, 9000) });
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
			DrawBubble(b, Head(s.Npc), Display(s.Npc.Name), s.Text, mine: false, alpha);
		}
	}

	/// <summary>气泡尾巴指向的位置（UI 坐标）；npc 为空时是农夫头顶。</summary>
	public static Vector2 Head(NPC? npc)
	{
		Vector2 world;
		if (npc != null)
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

	public static Rectangle DrawBubble(SpriteBatch b, Vector2 anchor, string name, string text, bool mine, float alpha)
	{
		if (alpha <= 0.01f || string.IsNullOrWhiteSpace(text))
		{
			return Rectangle.Empty;
		}
		string wrapped = AisvTheme.Wrap(text, Width);
		Vector2 size = Game1.smallFont.MeasureString(wrapped);
		int nameH = name.Length > 0 ? 24 : 0;
		float nameW = name.Length > 0 ? AisvTheme.Width(name, 0.7f) : 0;
		int w = (int)Math.Max(size.X, nameW) + 32;
		int h = (int)size.Y + 20 + nameH;
		int x = Math.Clamp((int)anchor.X - w / 2, 8, Math.Max(8, Game1.uiViewport.Width - w - 8));
		int y = Math.Max(8, (int)anchor.Y - h - 14);
		Rectangle r = new Rectangle(x, y, w, h);
		Color fill = mine ? new Color(214, 236, 190) : AisvTheme.Card;
		Color border = mine ? new Color(139, 186, 102) : AisvTheme.CardBorder;
		AisvTheme.DrawShadow(b, r, 12, 4);
		AisvTheme.Bordered(b, r, fill * alpha, border * alpha, 12);
		int tx = Math.Clamp((int)anchor.X, r.X + 16, r.Right - 16);
		for (int i = 0; i < 4; i++)
		{
			int half = 8 - i * 2;
			AisvTheme.Fill(b, new Rectangle(tx - half - 2, r.Bottom - 2 + i * 3, (half + 2) * 2, 3), border * alpha);
			AisvTheme.Fill(b, new Rectangle(tx - half, r.Bottom - 2 + i * 3, half * 2, 3), fill * alpha);
		}
		if (name.Length > 0)
		{
			AisvTheme.DrawText(b, name, new Vector2(r.X + 16, r.Y + 8), AisvTheme.AccentDark * alpha, 0.7f);
		}
		AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 10 + nameH), AisvTheme.Text * alpha);
		return r;
	}
}
