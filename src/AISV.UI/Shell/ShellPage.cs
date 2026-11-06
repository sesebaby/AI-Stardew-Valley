using System;
using System.Collections.Generic;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

internal interface IShellHost
{
	void Push(ShellPage page);

	void Back();

	void Notice(string text);

	void Open(ShellMenu.Tab tab, ShellMenu.SocialTab social = ShellMenu.SocialTab.Letters, ShellMenu.WorldTab? world = null);
}

internal abstract class ShellPage
{
	private int _ticks;

	protected ShellPage(ShellContext ctx)
	{
		Ctx = ctx;
	}

	protected ShellContext Ctx { get; }

	public IShellHost Host { get; set; } = null!;

	/// <summary>推进来的页面在页头显示的标题。</summary>
	public virtual string Title => "";

	/// <summary>正在打字（Esc、菜单键不关外壳）。</summary>
	public virtual bool CapturesKeys => false;

	public abstract void Draw(SpriteBatch b, Rectangle area, Hits hits);

	public virtual void Scroll(int direction)
	{
	}

	public virtual bool KeyPress(Keys key) => false;

	/// <summary>要离开这一页（返回、换页签、关外壳）时问一声；有没保存的改动可以先拦一次。</summary>
	public virtual bool CanLeave() => true;

	public virtual void Reload()
	{
	}

	public virtual void Closed()
	{
	}

	public void Tick()
	{
		if (++_ticks >= 120)
		{
			_ticks = 0;
			Reload();
		}
	}
}

internal sealed class Hits
{
	private readonly List<(Rectangle Bounds, Rectangle? Clip, Action Action)> _list = new List<(Rectangle, Rectangle?, Action)>();

	public void Clear() => _list.Clear();

	public void Add(Rectangle bounds, Action action, Rectangle? clip = null) => _list.Add((bounds, clip, action));

	public bool Click(int x, int y)
	{
		for (int i = _list.Count - 1; i >= 0; i--)
		{
			(Rectangle bounds, Rectangle? clip, Action action) = _list[i];
			if (bounds.Contains(x, y) && (clip == null || clip.Value.Contains(x, y)))
			{
				action();
				return true;
			}
		}
		return false;
	}
}

internal sealed class Scroller
{
	public int Offset { get; private set; }

	public int Max { get; private set; }

	public void Wheel(int direction, int step = 80) => Offset = Math.Clamp(Offset - Math.Sign(direction) * step, 0, Max);

	public void By(int delta) => Offset = Math.Clamp(Offset + delta, 0, Max);

	public void Reset() => Offset = 0;

	public void Finish(SpriteBatch b, Rectangle view, int contentBottom, int trackInset = 14)
	{
		Max = Math.Max(0, contentBottom + Offset - view.Bottom);
		Offset = Math.Clamp(Offset, 0, Max);
		AisvTheme.ScrollTrack(b, new Rectangle(view.Right - trackInset, view.Y + 8, 8, view.Height - 16), Offset, Max);
	}

	public bool PageKeys(Keys key, int page)
	{
		switch (key)
		{
		case Keys.Up:
			By(-80);
			return true;
		case Keys.Down:
			By(80);
			return true;
		case Keys.PageUp:
			By(-page);
			return true;
		case Keys.PageDown:
			By(page);
			return true;
		default:
			return false;
		}
	}
}

internal static class Kit
{
	public static void SectionTitle(SpriteBatch b, string text, int x, int y) => AisvTheme.Bold(b, text, new Vector2(x, y), AisvTheme.AccentDark, 0.85f);

	/// <summary>页面顶上那一条：标题、数量、右边按钮。返回按钮位置（没有按钮时为空）。</summary>
	public static Rectangle Header(SpriteBatch b, Rectangle header, string title, string count, string? button, Hits hits, Action? onButton)
	{
		AisvTheme.Fill(b, header, AisvTheme.PageDeep);
		AisvTheme.HLine(b, header.X, header.Bottom - 2, header.Width, AisvTheme.Divider);
		AisvTheme.Bold(b, title, new Vector2(header.X + 20, header.Y + 16), AisvTheme.Text, 1.15f);
		if (count.Length > 0)
		{
			AisvTheme.DrawText(b, count, new Vector2(header.X + 36 + AisvTheme.Width(title, 1.15f), header.Y + 24), AisvTheme.TextFaint, 0.75f);
		}
		if (button == null || onButton == null)
		{
			return Rectangle.Empty;
		}
		int w = (int)AisvTheme.Width(button, 0.85f) + 40;
		Rectangle r = new Rectangle(header.Right - 16 - w, header.Y + 10, w, 44);
		AisvTheme.Button(b, r, button, hover: AisvTheme.Hover(r), scale: 0.85f);
		hits.Add(r, onButton);
		return r;
	}

	/// <summary>晚上整理没成功时的提示卡（金色），带一个重试按钮。返回卡片下面的 y。</summary>
	public static int RetryCard(SpriteBatch b, ShellContext ctx, bool gossip, int x, int y, int w, Rectangle clip, Hits hits, IShellHost host)
	{
		bool can = ctx.Social.CanRetry(gossip);
		bool running = ctx.Social.IsAnalysisRunning;
		string status = running ? "" : ctx.Social.LastRetryMessage;
		if (!can && !running && status.Length == 0)
		{
			return y;
		}
		int h = 84 + (status.Length > 0 ? 30 : 0);
		Rectangle card = new Rectangle(x, y, w, h);
		AisvTheme.Bordered(b, card, AisvTheme.GoldSoft, AisvTheme.Gold * 0.5f);
		string title = running ? gossip ? Tr.Get("shell.retry.gossipBusy", "正在整理昨晚的八卦") : Tr.Get("shell.retry.diaryBusy", "正在写昨晚的日记")
			: can ? gossip ? Tr.Get("shell.retry.gossipTitle", "昨晚的八卦没整理出来") : Tr.Get("shell.retry.diaryTitle", "昨晚的日记没写成")
			: Tr.Get("shell.retry.doneTitle", "重新整理");
		string hint = running ? Tr.Get("shell.retry.busyHint", "AI 回话后会自动补上，不用一直等着。") : Tr.Get("shell.retry.hint", "多半是那会儿 AI 没回话。可以再整理一次。");
		AisvTheme.Bold(b, title, new Vector2(card.X + 24, card.Y + 18), AisvTheme.Text, 0.9f);
		AisvTheme.DrawText(b, hint, new Vector2(card.X + 24, card.Y + 50), AisvTheme.TextMuted, 0.72f);
		Rectangle btn = new Rectangle(card.Right - 24 - 150, card.Y + 20, 150, 44);
		AisvTheme.Button(b, btn, Tr.Get("shell.retry.button", "重试"), can, AisvTheme.Hover(btn, clip), !can);
		if (can)
		{
			hits.Add(btn, () =>
			{
				Game1.playSound("smallSelect");
				host.Notice(ctx.Social.Retry(gossip));
			}, clip);
		}
		if (status.Length > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate(status, w - 48, 0.72f), new Vector2(card.X + 24, card.Y + 84), AisvTheme.TextFaint, 0.72f);
		}
		return card.Bottom + 16;
	}

	public static void Avatar(SpriteBatch b, Rectangle r, ShellContext ctx, string? npc)
	{
		string shown = string.IsNullOrWhiteSpace(npc) ? "" : ctx.Display(npc);
		AisvTheme.Avatar(b, r, ShellText.Npc(npc), shown.Length > 0 ? shown.Substring(0, 1) : "?");
	}

	/// <summary>打开链接前问一句：链接、一句说明、打开 / 取消。</summary>
	public static void ConfirmLink(SpriteBatch b, Rectangle area, Hits hits, string url, string note, Action open, Action cancel)
	{
		b.Draw(Game1.fadeToBlackRect, area, Color.Black * 0.35f);
		hits.Add(area, cancel);
		int w = Math.Min(620, area.Width - 48);
		List<string> urlLines = AisvTheme.WrapLines(url, w - 56, 0.72f);
		int h = 28 + 40 + urlLines.Count * AisvTheme.LineHeight(0.72f) + (note.Length > 0 ? 8 + AisvTheme.LineHeight(0.78f) : 0) + 24 + 50 + 28;
		Rectangle card = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
		hits.Add(card, () => { });
		AisvTheme.DrawShadow(b, card);
		AisvTheme.DrawCard(b, card);
		AisvTheme.Bold(b, Tr.Get("promo.confirm", "要用浏览器打开这个链接吗？"), new Vector2(card.X + 28, card.Y + 28), AisvTheme.Text, 0.95f);
		int y = card.Y + 68;
		foreach (string line in urlLines)
		{
			AisvTheme.DrawText(b, line, new Vector2(card.X + 28, y), AisvTheme.TextMuted, 0.72f);
			y += AisvTheme.LineHeight(0.72f);
		}
		if (note.Length > 0)
		{
			AisvTheme.DrawText(b, note, new Vector2(card.X + 28, y + 8), AisvTheme.Pink, 0.78f);
			y += 8 + AisvTheme.LineHeight(0.78f);
		}
		Rectangle yes = new Rectangle(card.X + 28, y + 24, 160, 50);
		Rectangle no = new Rectangle(yes.Right + 16, y + 24, 160, 50);
		AisvTheme.Button(b, yes, Tr.Get("promo.open", "打开"), primary: true, hover: AisvTheme.Hover(yes));
		AisvTheme.Button(b, no, Tr.Get("promo.cancel", "取消"), hover: AisvTheme.Hover(no));
		hits.Add(yes, open);
		hits.Add(no, cancel);
	}

	public static void Empty(SpriteBatch b, Rectangle r, string text, string hint = "")
	{
		AisvTheme.TextCentered(b, text, new Rectangle(r.X, r.Y, r.Width, 40), AisvTheme.TextMuted, 0.9f);
		if (hint.Length > 0)
		{
			AisvTheme.TextCentered(b, hint, new Rectangle(r.X, r.Y + 40, r.Width, 32), AisvTheme.TextFaint, 0.75f);
		}
	}
}
