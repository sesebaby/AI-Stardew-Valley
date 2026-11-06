using System;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Dialogue;

/// <summary>左下角的小卡片：等开场白时显示"××在想……"和进度，失败时显示几秒原因。</summary>
public static class ThinkingOverlay
{
	public static void Draw(SpriteBatch b)
	{
		if (Game1.activeClickableMenu != null && GenerationStatus.Current == null)
		{
			return;
		}
		GenerationStatus.Snapshot? now = GenerationStatus.Current;
		string? failure = now == null ? GenerationStatus.Failure : null;
		if (now == null && failure == null)
		{
			return;
		}
		int width = 420;
		string title = now != null ? Tr.Get("thinking.title", "{{npc}}在想……", ("npc", now.DisplayName)) : Tr.Get("thinking.failedTitle", "这次没有回应");
		string body = now != null
			? Tr.Get("thinking.elapsed", "已经 {{seconds}} 秒 · 按 ESC 不等了", ("seconds", (int)now.Elapsed.TotalSeconds))
			: AisvTheme.Wrap(failure!, width - 32);
		int lh = AisvTheme.LineHeight();
		int lines = body.Split('\n').Length;
		int height = 24 + lh + 6 + lines * lh + (now != null ? 22 : 0) + 16;
		Rectangle r = new Rectangle(24, Game1.uiViewport.Height - height - 120, width, height);
		AisvTheme.FillRounded(b, new Rectangle(r.X + 2, r.Y + 5, r.Width, r.Height), AisvTheme.Shadow);
		AisvTheme.Bordered(b, r, AisvTheme.Page, now != null ? AisvTheme.Accent : AisvTheme.Warn);
		b.DrawString(Game1.smallFont, title, new Vector2(r.X + 16, r.Y + 12), now != null ? AisvTheme.AccentDark : AisvTheme.Danger);
		b.DrawString(Game1.smallFont, body, new Vector2(r.X + 16, r.Y + 12 + lh + 6), AisvTheme.TextMuted);
		if (now != null)
		{
			float fraction = now.Budget.TotalSeconds <= 0 ? 0 : (float)(now.Elapsed.TotalSeconds / now.Budget.TotalSeconds);
			AisvTheme.Progress(b, new Rectangle(r.X + 16, r.Bottom - 26, r.Width - 32, 10), fraction);
		}
	}
}
