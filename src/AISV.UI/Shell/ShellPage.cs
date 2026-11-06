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

	/// <summary>页面自己还能退一层（窄屏的详情退回列表）就退，返回 true。</summary>
	public virtual bool StepBack() => false;

	/// <summary>每帧一次，在绘制之外。</summary>
	public virtual void Update()
	{
	}

	public void Tick()
	{
		Update();
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

	public int Count => _list.Count;

	/// <summary>从第 mark 个起的点击，做完再多做一件事。</summary>
	public void ThenFrom(int mark, Action after)
	{
		for (int i = mark; i < _list.Count; i++)
		{
			(Rectangle bounds, Rectangle? clip, Action action) = _list[i];
			_list[i] = (bounds, clip, () =>
			{
				action();
				after();
			});
		}
	}

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

/// <summary>窄屏（手机）下的列表和详情：一次只看一边，点列表翻到详情，返回键翻回来。</summary>
internal sealed class Flip
{
	private bool _reading;

	private int _drawnTick = -10;

	public static bool Applies(Rectangle area) => area.Width < 760;

	/// <summary>只有正显示着翻页时才算退一层。</summary>
	public bool StepBack()
	{
		bool was = _reading && Game1.ticks - _drawnTick <= 2;
		_reading &= !was;
		return was;
	}

	public void Draw(SpriteBatch b, Rectangle area, Hits hits, Action<Rectangle> list, Action<Rectangle> detail)
	{
		_drawnTick = Game1.ticks;
		if (!_reading)
		{
			int mark = hits.Count;
			list(area);
			hits.ThenFrom(mark, () => _reading = true);
			return;
		}
		Rectangle back = new Rectangle(area.X + 10, area.Y + 8, 170, 40);
		Hearth.PressPlate(b, back, "‹ " + Tr.Get("shell.backToList", "回到列表"), hover: Hearth.Hover(back), scale: 0.78f);
		hits.Add(back, () => _reading = false);
		detail(new Rectangle(area.X, back.Bottom + 4, area.Width, area.Bottom - back.Bottom - 4));
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
		Hearth.Rail(b, new Rectangle(view.Right - trackInset, view.Y + 8, 8, view.Height - 16), Offset, Max);
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
	public static void SectionTitle(SpriteBatch b, string text, int x, int y) => Hearth.Ribbon(b, text, x, y - 6);

	/// <summary>页面顶上那一条：标题、数量、右边按钮。返回按钮位置（没有按钮时为空）。</summary>
	public static Rectangle Header(SpriteBatch b, Rectangle header, string title, string count, string? button, Hits hits, Action? onButton)
	{
		Hearth.Fill(b, header, Hearth.ParchmentDeep);
		Hearth.Fill(b, new Rectangle(header.X, header.Bottom - 4, header.Width, 4), Hearth.Bark * 0.5f);
		Hearth.InkBold(b, title, new Vector2(header.X + 20, header.Y + 16), Hearth.Quill, 1.15f);
		if (count.Length > 0)
		{
			Hearth.Ink(b, count, new Vector2(header.X + 36 + Hearth.Width(title, 1.15f), header.Y + 24), Hearth.QuillPale, 0.75f);
		}
		if (button == null || onButton == null)
		{
			return Rectangle.Empty;
		}
		int w = (int)Hearth.Width(button, 0.85f) + 40;
		Rectangle r = new Rectangle(header.Right - 16 - w, header.Y + 10, w, 44);
		Hearth.PressPlate(b, r, button, hover: Hearth.Hover(r), scale: 0.85f);
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
		Hearth.Bordered(b, card, Hearth.AmberPale, Hearth.Amber * 0.5f);
		string title = running ? gossip ? Tr.Get("shell.retry.gossipBusy", "正在整理昨晚的八卦") : Tr.Get("shell.retry.diaryBusy", "正在写昨晚的日记")
			: can ? gossip ? Tr.Get("shell.retry.gossipTitle", "昨晚的八卦没整理出来") : Tr.Get("shell.retry.diaryTitle", "昨晚的日记没写成")
			: Tr.Get("shell.retry.doneTitle", "重新整理");
		string hint = running ? Tr.Get("shell.retry.busyHint", "AI 回话后会自动补上，不用一直等着。") : Tr.Get("shell.retry.hint", "多半是那会儿 AI 没回话。可以再整理一次。");
		Hearth.InkBold(b, title, new Vector2(card.X + 24, card.Y + 18), Hearth.Quill, 0.9f);
		Hearth.Ink(b, hint, new Vector2(card.X + 24, card.Y + 50), Hearth.QuillSoft, 0.72f);
		Rectangle btn = new Rectangle(card.Right - 24 - 150, card.Y + 20, 150, 44);
		Hearth.PressPlate(b, btn, Tr.Get("shell.retry.button", "重试"), can, Hearth.Hover(btn, clip), !can);
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
			Hearth.Ink(b, Hearth.Truncate(status, w - 48, 0.72f), new Vector2(card.X + 24, card.Y + 84), Hearth.QuillPale, 0.72f);
		}
		return card.Bottom + 16;
	}

	public static void Avatar(SpriteBatch b, Rectangle r, ShellContext ctx, string? npc)
	{
		string shown = string.IsNullOrWhiteSpace(npc) ? "" : ctx.Display(npc);
		Hearth.Cameo(b, r, ShellText.Npc(npc), shown.Length > 0 ? shown.Substring(0, 1) : "?");
	}

	/// <summary>打开链接前问一句：链接、一句说明、打开 / 取消。</summary>
	public static void ConfirmLink(SpriteBatch b, Rectangle area, Hits hits, string url, string note, Action open, Action cancel)
	{
		b.Draw(Game1.fadeToBlackRect, area, Color.Black * 0.35f);
		hits.Add(area, cancel);
		int w = Math.Min(620, area.Width - 48);
		List<string> urlLines = Hearth.WrapLines(url, w - 56, 0.72f);
		int h = 28 + 40 + urlLines.Count * Hearth.LineHeight(0.72f) + (note.Length > 0 ? 8 + Hearth.LineHeight(0.78f) : 0) + 24 + 50 + 28;
		Rectangle card = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
		hits.Add(card, () => { });
		Hearth.CastShadow(b, card);
		Hearth.CardSlip(b, card);
		Hearth.InkBold(b, Tr.Get("promo.confirm", "要用浏览器打开这个链接吗？"), new Vector2(card.X + 28, card.Y + 28), Hearth.Quill, 0.95f);
		int y = card.Y + 68;
		foreach (string line in urlLines)
		{
			Hearth.Ink(b, line, new Vector2(card.X + 28, y), Hearth.QuillSoft, 0.72f);
			y += Hearth.LineHeight(0.72f);
		}
		if (note.Length > 0)
		{
			Hearth.Ink(b, note, new Vector2(card.X + 28, y + 8), Hearth.Rose, 0.78f);
			y += 8 + Hearth.LineHeight(0.78f);
		}
		Rectangle yes = new Rectangle(card.X + 28, y + 24, 160, 50);
		Rectangle no = new Rectangle(yes.Right + 16, y + 24, 160, 50);
		Hearth.PressPlate(b, yes, Tr.Get("promo.open", "打开"), primary: true, hover: Hearth.Hover(yes));
		Hearth.PressPlate(b, no, Tr.Get("promo.cancel", "取消"), hover: Hearth.Hover(no));
		hits.Add(yes, open);
		hits.Add(no, cancel);
	}

	public static void Empty(SpriteBatch b, Rectangle r, string text, string hint = "")
	{
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		AISV.UI.Apps.AppIcons.Junimo.Draw(b, r.Center.X - 24, r.Y - 6 - (int)(Math.Abs(Math.Sin(t * 2)) * 8), 3);
		Hearth.InkMiddle(b, text, new Rectangle(r.X, r.Y + 44, r.Width, 40), Hearth.QuillSoft, 0.9f);
		if (hint.Length > 0)
		{
			Hearth.InkMiddle(b, hint, new Rectangle(r.X, r.Y + 84, r.Width, 32), Hearth.QuillPale, 0.75f);
		}
	}
}
