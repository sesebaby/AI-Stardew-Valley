using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Config;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>显示哪些应用：一行一个开关。收起的应用只是不在应用页显示，数据照常保留。</summary>
internal sealed class AppShelfPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private readonly List<AppsPage.App> _apps;

	private Rectangle _view;

	public AppShelfPage(ShellContext ctx) : base(ctx)
	{
		_apps = new AppsPage(ctx).All.Where(a => AppShelf.CanHide(a.Id)).ToList();
	}

	public override string Title => Tr.Get("apps.manage.title", "显示哪些应用");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		const int rowH = 88;
		ModConfig config = Ctx.Config();
		int w = Math.Min(area.Width - 64, 820), x = area.X + (area.Width - w) / 2;
		Hearth.BeginClip(b, area);
		int y = area.Y + 22 - _scroll.Offset;
		Hearth.Ink(b, Tr.Get("apps.manage.note", "关掉的应用不在应用页显示，数据照常保留，随时可以打开。"), new Vector2(x + 6, y), Hearth.QuillSoft, 0.78f);
		y += 40;
		Rectangle card = new Rectangle(x, y, w, _apps.Count * rowH + 8);
		Hearth.CardSlip(b, card);
		for (int i = 0; i < _apps.Count; i++)
		{
			AppsPage.App app = _apps[i];
			Rectangle r = new Rectangle(x + 4, y + 4 + i * rowH, w - 8, rowH);
			if (i > 0)
			{
				Hearth.HLine(b, r.X + 16, r.Y, r.Width - 32, Hearth.Seam);
			}
			if (Hearth.Hover(r, area))
			{
				Hearth.FillRounded(b, Hearth.Inflate(r, -2), Hearth.Honey, 10);
			}
			Rectangle well = new Rectangle(r.X + 16, r.Center.Y - 32, 64, 64);
			Hearth.FillRounded(b, well, app.Tint, 14);
			app.Icon.Draw(b, well.Center.X - 24, well.Center.Y - 24, 3);
			Hearth.Ink(b, app.Name, new Vector2(well.Right + 18, r.Center.Y - Hearth.LineHeight(0.88f) / 2), Hearth.Quill, 0.88f);
			bool on = !AppShelf.IsHidden(config, app.Id);
			Hearth.Latch(b, new Rectangle(r.Right - 20 - 64, r.Center.Y - 18, 64, 36), on);
			hits.Add(r, () => Flip(app.Id, on), area);
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, card.Bottom + 24);
	}

	private void Flip(string id, bool hide)
	{
		Game1.playSound("drumkit6");
		ModConfig c = Ctx.Config();
		AppShelf.SetHidden(c, id, hide);
		Ctx.SaveConfig(c.Normalize());
	}
}
