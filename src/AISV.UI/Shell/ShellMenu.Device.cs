using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.UI.Apps;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>外壳的另外两种样子：手机和农场电脑。里面是同样的页面，入口换成桌面上的图标。</summary>
public sealed partial class ShellMenu
{
	public enum Device
	{
		Window,
		Phone,
		Computer
	}

	private const int StatusH = 40;

	private const int NavH = 56;

	private const double FirstBootSeconds = 2.6;

	private const double QuickBootSeconds = 0.5;

	private static int _bootedOnDay = -1;

	private readonly Device _device;

	private Rectangle _home;

	private double _bootLeft;

	private double _bootTotal = 1;

	private bool OnDevice => _device != Device.Window;

	private bool OnDesk => _tab == Tab.Desk && !Pushed;

	private bool CanGoBack => Pushed || OnDevice && _tab != Tab.Desk;

	private void StartBoot()
	{
		if (_device != Device.Computer)
		{
			return;
		}
		int today = Game1.Date?.TotalDays ?? 0;
		_bootLeft = _bootTotal = _bootedOnDay == today ? QuickBootSeconds : FirstBootSeconds;
		_bootedOnDay = today;
		Game1.playSound("select");
	}

	private void LayoutDevice(int vw, int vh)
	{
		_signs.Clear();
		if (_device == Device.Phone)
		{
			int w = Math.Min(vw - 32, 540), h = Math.Min(vh - 24, 900);
			_frame = new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
			_paper = new Rectangle(_frame.X + 16, _frame.Y + 30, w - 32, h - 60);
			_crossBox = new Rectangle(_paper.Right - 40, _paper.Y + 2, 36, 36);
			_strip = new Rectangle(_paper.X, _paper.Y + StatusH, _paper.Width, 48);
		}
		else
		{
			int w = Math.Min(vw - 48, 1320), h = Math.Min(vh - 72, 860);
			_frame = new Rectangle((vw - w) / 2, (vh - h) / 2 - 12, w, h);
			_paper = new Rectangle(_frame.X + 26, _frame.Y + 24, w - 52, h - 70);
			_crossBox = new Rectangle(_paper.Right - 46, _paper.Bottom - NavH + 8, 40, 40);
			_strip = new Rectangle(_paper.X, _paper.Y, _paper.Width, 48);
		}
		_home = _device == Device.Phone
			? new Rectangle(_paper.Center.X - 30, _paper.Bottom - NavH + 8, 60, 40)
			: new Rectangle(_paper.X + 8, _paper.Bottom - NavH + 8, 104, 40);
	}

	private Rectangle DeviceContent
	{
		get
		{
			int top = OnDesk ? (_device == Device.Phone ? _paper.Y + StatusH : _paper.Y) : _strip.Bottom;
			return new Rectangle(_paper.X, top, _paper.Width, _paper.Bottom - NavH - top);
		}
	}

	private void DrawDevice(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.5f);
		FrameDecor.DrawDrift(b, Game1.uiViewport.Width, Game1.uiViewport.Height);
		if (_device == Device.Phone)
		{
			DrawPhoneBody(b);
		}
		else
		{
			DrawMonitor(b);
		}
		_hits.Clear();
		if (_bootLeft > 0)
		{
			DrawBoot(b);
			drawMouse(b);
			return;
		}
		Rectangle content = DeviceContent;
		if (OnDesk)
		{
			Wallpaper.Draw(b, _paper, _ctx.Config().DeskWallpaper);
		}
		else
		{
			Hearth.Paper(b, _paper);
			DrawPageHeader(b);
			FrameDecor.PaperMarks(b, content);
		}
		Page.Draw(b, content, _hits);
		if (_device == Device.Phone)
		{
			DrawStatusBar(b);
		}
		DrawNavBar(b);
		DrawNotice(b, content);
		drawMouse(b);
	}

	private void DrawPhoneBody(SpriteBatch b)
	{
		Hearth.FillRounded(b, new Rectangle(_frame.X + 6, _frame.Y + 8, _frame.Width, _frame.Height), Color.Black * 0.35f, 24);
		Hearth.Bordered(b, _frame, new Color(66, 56, 78), new Color(34, 28, 42), 24, 4);
		Hearth.Fill(b, new Rectangle(_frame.X + 8, _frame.Y + 12, 4, _frame.Height - 24), new Color(104, 92, 120));
		Hearth.FillRounded(b, new Rectangle(_frame.Center.X - 34, _frame.Y + 12, 68, 8), new Color(30, 26, 38), 4);
		Hearth.Fill(b, new Rectangle(_frame.Center.X + 46, _frame.Y + 12, 8, 8), new Color(70, 110, 150));
		Hearth.Fill(b, new Rectangle(_paper.X - 2, _paper.Y - 2, _paper.Width + 4, _paper.Height + 4), new Color(24, 20, 30));
		// 机身边上挂一颗星之果实，晃一晃。
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		int swing = (int)(Math.Sin(t * 1.3) * 5);
		Hearth.Fill(b, new Rectangle(_frame.Right, _frame.Y + 70, 22, 4), new Color(150, 110, 80));
		Hearth.Fill(b, new Rectangle(_frame.Right + 18, _frame.Y + 70, 4, 40), new Color(150, 110, 80));
		FrameDecor.Item(b, 434, _frame.Right + 2 + swing, _frame.Y + 108, 40);
	}

	private void DrawMonitor(SpriteBatch b)
	{
		Hearth.FillRounded(b, new Rectangle(_frame.Center.X - 110, _frame.Bottom, 220, 18), Hearth.Bark, 4);
		Hearth.FillRounded(b, new Rectangle(_frame.Center.X - 170, _frame.Bottom + 14, 340, 14), Hearth.Timber, 4);
		Hearth.Bordered(b, _frame, Hearth.Timber, Hearth.Bark, 16, 4);
		Hearth.Fill(b, new Rectangle(_paper.X - 4, _paper.Y - 4, _paper.Width + 8, _paper.Height + 8), new Color(24, 18, 14));
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		Hearth.FillRounded(b, new Rectangle(_frame.Right - 50, _frame.Bottom - 34, 20, 20), new Color(46, 30, 20), 8);
		Hearth.FillRounded(b, new Rectangle(_frame.Right - 46, _frame.Bottom - 30, 12, 12), new Color(250, 196, 84) * (0.8f + 0.2f * (float)Math.Sin(t * 2)), 4);
		Hearth.Ink(b, Tr.Get("desk.brand", "鹈鹕牌 · 农场电脑"), new Vector2(_frame.X + 34, _frame.Bottom - 37), Hearth.Bark, 0.68f);
		FrameDecor.DrawMonitorDecor(b, _frame);
	}

	private void DrawStatusBar(SpriteBatch b)
	{
		Rectangle bar = new Rectangle(_paper.X, _paper.Y, _paper.Width, StatusH);
		Hearth.Fill(b, bar, new Color(30, 24, 38) * 0.82f);
		string clock = Game1.getTimeOfDayString(Game1.timeOfDay);
		Hearth.Ink(b, clock, new Vector2(bar.X + 14, bar.Y + 8), Color.White, 0.74f);
		for (int i = 0; i < 4; i++)
		{
			int h = 6 + i * 4;
			Hearth.Fill(b, new Rectangle(bar.Center.X - 16 + i * 8, bar.Bottom - 10 - h, 6, h), i < 3 ? Color.White : Color.White * 0.35f);
		}
		string money = Utility.getNumberWithCommas(Game1.player?.Money ?? 0);
		float mw = Hearth.Width(money, 0.74f);
		Hearth.Ink(b, money, new Vector2(_crossBox.X - 12 - mw, bar.Y + 8), new Color(255, 226, 130), 0.74f);
		FrameDecor.Coin(b, (int)(_crossBox.X - 12 - mw - 26), bar.Y + 10, 2);
		Hearth.CrossPlate(b, _crossBox, Hearth.Hover(_crossBox));
	}

	private void DrawNavBar(SpriteBatch b)
	{
		Rectangle bar = new Rectangle(_paper.X, _paper.Bottom - NavH, _paper.Width, NavH);
		Hearth.Fill(b, bar, new Color(30, 24, 38) * 0.86f);
		if (_device == Device.Phone)
		{
			bool hover = Hearth.Hover(_home);
			Hearth.FillRounded(b, _home, Color.White * (hover ? 0.3f : 0.16f), 12);
			Hearth.FillRounded(b, new Rectangle(_home.Center.X - 10, _home.Center.Y - 10, 20, 20), Color.White, 8);
			return;
		}
		Hearth.Fill(b, new Rectangle(bar.X, bar.Y, bar.Width, 2), Color.White * 0.25f);
		Hearth.PressPlate(b, _home, Tr.Get("desk.start", "主屏"), primary: true, hover: Hearth.Hover(_home), scale: 0.8f);
		string now = ShellText.TopLine();
		float w = Hearth.Width(now, 0.74f);
		Hearth.Ink(b, now, new Vector2(_crossBox.X - 20 - w, bar.Y + 16), Color.White, 0.74f);
		string money = Utility.getNumberWithCommas(Game1.player?.Money ?? 0);
		float mw = Hearth.Width(money, 0.74f);
		Hearth.Ink(b, money, new Vector2(_crossBox.X - 44 - w - mw, bar.Y + 16), new Color(255, 226, 130), 0.74f);
		FrameDecor.Coin(b, (int)(_crossBox.X - 72 - w - mw), bar.Y + 18, 2);
		if (!OnDesk)
		{
			Hearth.Ink(b, Hearth.Truncate(TitleNow, 300, 0.8f), new Vector2(_home.Right + 16, bar.Y + 15), Color.White, 0.8f);
		}
		Hearth.CrossPlate(b, _crossBox, Hearth.Hover(_crossBox));
	}

	/// <summary>开机：几行自检，一条进度，祝尼魔跳两下。点一下可以跳过。</summary>
	private void DrawBoot(SpriteBatch b)
	{
		Hearth.Fill(b, _paper, new Color(16, 14, 22));
		double done = 1 - _bootLeft / _bootTotal;
		string[] lines =
		{
			Tr.Get("desk.boot.1", "鹈鹕牌农场电脑"),
			Tr.Get("desk.boot.2", "检查土壤湿度 …… 好"),
			Tr.Get("desk.boot.3", "接通镇上的电话线 …… 好"),
			Tr.Get("desk.boot.4", "叫醒祝尼魔 …… 好")
		};
		int x = _paper.X + 40, y = _paper.Y + 36, lh = Hearth.LineHeight(0.8f) + 6;
		for (int i = 0; i < lines.Length; i++)
		{
			if (done >= i * 0.2)
			{
				Hearth.Ink(b, lines[i], new Vector2(x, y + i * lh), i == 0 ? new Color(255, 226, 130) : new Color(150, 230, 130), 0.8f);
			}
		}
		int hop = (int)(Math.Abs(Math.Sin(done * Math.PI * 4)) * 26);
		AppIcons.Junimo.Draw(b, _paper.Center.X - 48, _paper.Center.Y - 80 - hop, 6);
		Rectangle track = new Rectangle(_paper.Center.X - 180, _paper.Center.Y + 40, 360, 20);
		Hearth.Fill(b, track, new Color(60, 56, 70));
		Hearth.Fill(b, new Rectangle(track.X + 4, track.Y + 4, (int)((track.Width - 8) * Math.Clamp(done, 0, 1)), track.Height - 8), new Color(124, 200, 90));
	}

	private string TitleNow => Pushed && Page.Title.Length > 0 ? Page.Title : _tab switch
	{
		Tab.Social => SubLabel(_social),
		Tab.World => WorldLabel(_world),
		Tab.Desk => Tr.Get("desk.start", "主屏"),
		_ => TabLabel(_tab)
	};

	/// <summary>桌面上的图标：页签和子页签拆开一个个放。</summary>
	private List<(string Label, PixelArt Icon, Color Tint, Action Open)> DeskSections()
	{
		List<(string, PixelArt, Color, Action)> items = new List<(string, PixelArt, Color, Action)>
		{
			(TabLabel(Tab.Today), DeskIcons.Today, new Color(255, 236, 170), () => Switch(Tab.Today, _social, _world))
		};
		foreach (SocialTab t in SocialTabsNow())
		{
			items.Add((SubLabel(t), DeskIcons.Of(t), DeskIcons.TintOf(t), () => Switch(Tab.Social, t, _world)));
		}
		foreach (WorldTab t in WorldTabs())
		{
			items.Add((WorldLabel(t), DeskIcons.Of(t), DeskIcons.TintOf(t), () => Switch(Tab.World, _social, t)));
		}
		items.Add((TabLabel(Tab.Settings), DeskIcons.Settings, new Color(226, 226, 232), () => Switch(Tab.Settings, _social, _world)));
		return items;
	}
}
