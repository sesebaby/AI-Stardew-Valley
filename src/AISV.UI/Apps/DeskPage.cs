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

/// <summary>手机和电脑的桌面：上面一张时钟小卡，下面一排排图标。</summary>
internal sealed class DeskPage : ShellPage
{
	private const int Tile = 72;

	private const int CellW = 108;

	private const int CellH = 118;

	private readonly Func<List<(string Label, PixelArt Icon, Color Tint, Action Open)>> _sections;

	private readonly Scroller _scroll = new Scroller();

	private AppsPage? _apps;

	private Rectangle _view;

	public DeskPage(ShellContext ctx, Func<List<(string, PixelArt, Color, Action)>> sections) : base(ctx)
	{
		_sections = sections;
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	private AppsPage Apps
	{
		get
		{
			_apps ??= new AppsPage(Ctx);
			_apps.Host = Host;
			return _apps;
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		Hearth.BeginClip(b, area);
		int x = area.X + 16, w = area.Width - 44;
		int y = DrawClock(b, x, area.Y + 14 - _scroll.Offset, w);
		y = Row(b, hits, Tr.Get("desk.town", "小镇"), _sections(), x, y, w);
		var config = Ctx.Config();
		List<(string, PixelArt, Color, Action)> apps = Apps.All.Where(a => !AppShelf.IsHidden(config, a.Id)).Select(a => (a.Name, a.Icon, a.Tint, a.Open)).ToList();
		y = Row(b, hits, Tr.Get("shell.tab.apps", "应用"), apps, x, y, w);
		List<(string, PixelArt, Color, Action)> own = new List<(string, PixelArt, Color, Action)> { (Tr.Get("desk.look", "个性化"), DeskIcons.Look, new Color(250, 236, 226), () => Host.Push(new DeskLookPage(Ctx))) };
		y = Row(b, hits, Tr.Get("desk.system", "这台机器"), own, x, y, w);
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + 8);
	}

	private static int DrawClock(SpriteBatch b, int x, int y, int w)
	{
		Rectangle card = new Rectangle(x, y, w, 96);
		Hearth.FillRounded(b, card, new Color(20, 18, 34) * 0.5f, 12);
		string time = Game1.getTimeOfDayString(Game1.timeOfDay);
		Hearth.InkBold(b, time, new Vector2(card.X + 22, card.Y + 12), Color.White, 1.35f);
		Hearth.Ink(b, Hearth.Truncate(ShellText.Clean(Utility.getDateString()) + " · " + ShellText.Weather(), w - 120, 0.76f), new Vector2(card.X + 22, card.Y + 58), new Color(255, 240, 200), 0.76f);
		FrameDecor.SeasonCrop(b, card.Right - 76, card.Y + 16, 64);
		return card.Bottom + 12;
	}

	private int Row(SpriteBatch b, Hits hits, string title, List<(string Label, PixelArt Icon, Color Tint, Action Open)> items, int x, int y, int w)
	{
		Hearth.Ribbon(b, title, x + 8, y);
		y += 44;
		int cols = Math.Max(1, w / CellW), gap = (w - cols * CellW) / Math.Max(1, cols);
		for (int i = 0; i < items.Count; i++)
		{
			(string label, PixelArt icon, Color tint, Action open) = items[i];
			Rectangle cell = new Rectangle(x + i % cols * (CellW + gap), y + i / cols * CellH, CellW, CellH);
			Rectangle tile = new Rectangle(cell.Center.X - Tile / 2, cell.Y + 4, Tile, Tile);
			bool hover = Hearth.Hover(cell, _view);
			if (hover)
			{
				tile.Y -= 4;
			}
			Hearth.FillRounded(b, new Rectangle(tile.X + 3, tile.Y + 5, tile.Width, tile.Height), Color.Black * 0.3f, 12);
			Hearth.Bordered(b, tile, tint, Hearth.Bark, 12, 4);
			icon.Draw(b, tile.X + 12, tile.Y + 12, 3);
			string text = Hearth.Truncate(label, CellW - 4, 0.68f);
			Vector2 at = new Vector2(cell.Center.X - Hearth.Width(text, 0.68f) / 2f, cell.Y + Tile + 10);
			Hearth.Ink(b, text, at + new Vector2(2, 2), Color.Black * 0.7f, 0.68f);
			Hearth.Ink(b, text, at, Color.White, 0.68f);
			hits.Add(cell, () =>
			{
				open();
			}, _view);
		}
		return y + (items.Count + cols - 1) / cols * CellH + 6;
	}
}

/// <summary>个性化：换壁纸和界面外观。</summary>
internal sealed class DeskLookPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private Rectangle _view;

	public DeskLookPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("desk.look", "个性化");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	private static string WallName(string id) => id switch
	{
		"forest" => Tr.Get("desk.wall.forest", "林间黄昏"),
		"night" => Tr.Get("desk.wall.night", "星空小屋"),
		_ => Tr.Get("desk.wall.meadow", "春日草地")
	};

	private static string SkinName(string id) => id switch
	{
		"forest" => Tr.Get("desk.skin.forest", "森林"),
		"starry" => Tr.Get("desk.skin.starry", "星夜"),
		_ => Tr.Get("desk.skin.cottage", "木屋")
	};

	private void Save(Action<ModConfig> edit)
	{
		Game1.playSound("drumkit6");
		ModConfig c = Ctx.Config();
		edit(c);
		Ctx.SaveConfig(c.Normalize());
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		Hearth.BeginClip(b, area);
		int x = area.X + 32, w = area.Width - 76, y = area.Y + 22 - _scroll.Offset;
		ModConfig c = Ctx.Config();
		Hearth.Ribbon(b, Tr.Get("desk.walls", "壁纸"), x, y);
		y += 48;
		int tw = Math.Clamp((w - 32) / 3, 130, 280), th = tw * 2 / 3;
		int cols = Math.Max(1, (w + 16) / (tw + 16));
		for (int i = 0; i < Wallpaper.Names.Length; i++)
		{
			string id = Wallpaper.Names[i];
			Rectangle r = new Rectangle(x + i % cols * (tw + 16), y + i / cols * (th + 52), tw, th);
			bool on = c.DeskWallpaper == id;
			Hearth.Bordered(b, Hearth.Inflate(r, 6), on ? Hearth.Honey : Hearth.Slip, on ? Hearth.Leaf : Hearth.SlipEdge, 8, 4);
			Wallpaper.Draw(b, r, id);
			Hearth.InkMiddle(b, WallName(id), new Rectangle(r.X, r.Bottom + 8, r.Width, 30), Hearth.Quill, 0.76f);
			hits.Add(r, () => Save(m => m.DeskWallpaper = id), _view);
		}
		y += (Wallpaper.Names.Length + cols - 1) / cols * (th + 52) + 12;
		Hearth.Ribbon(b, Tr.Get("desk.skins", "界面外观"), x, y);
		y += 48;
		Hearth.Ink(b, Tr.Get("desk.skinsNote", "外壳、对话框、群聊的配色都会跟着变。"), new Vector2(x, y), Hearth.QuillSoft, 0.74f);
		y += 36;
		int bx = x;
		foreach (string id in new[] { "cottage", "forest", "starry" })
		{
			Rectangle r = new Rectangle(bx, y, 130, 52);
			Hearth.PressPlate(b, r, SkinName(id), primary: c.ShellSkin == id, hover: Hearth.Hover(r, _view), scale: 0.85f);
			hits.Add(r, () => Save(m => m.ShellSkin = id), _view);
			bx = r.Right + 12;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + 76);
	}
}
