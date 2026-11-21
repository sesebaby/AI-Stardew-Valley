using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Store;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Store;

/// <summary>
/// 收银小游戏：先把顾客篮子里的东西一样样扫进袋子，算出总价；顾客递钱过来，你用硬币凑出找零，点"找零"。
/// 又对又快有小费，找错了顾客会不高兴（评价扣分）。Esc 先不收，顾客接着排队。
/// </summary>
public sealed class CheckoutMenu : IClickableMenu
{
	private readonly CheckoutRequest _req;

	private readonly HashSet<int> _scanned = new HashSet<int>();

	private readonly List<int> _given = new List<int>();

	private readonly DateTime _start = DateTime.UtcNow;

	private Rectangle _panel, _confirm, _undo;

	private Rectangle[] _items = Array.Empty<Rectangle>();

	private Rectangle[] _coins = Array.Empty<Rectangle>();

	private bool _finished;

	public CheckoutMenu(CheckoutRequest req)
	{
		_req = req;
		Layout();
		Game1.playSound("bigSelect");
	}

	private bool AllScanned => _scanned.Count >= _req.Lines.Count;

	private int Change => _req.Paid - _req.Total;

	private int Given => _given.Sum();

	private void Layout()
	{
		int w = Math.Min(920, Game1.uiViewport.Width - 48), h = Math.Min(640, Game1.uiViewport.Height - 32);
		_panel = new Rectangle((Game1.uiViewport.Width - w) / 2, (Game1.uiViewport.Height - h) / 2, w, h);
		xPositionOnScreen = _panel.X;
		yPositionOnScreen = _panel.Y;
		width = w;
		height = h;
		int cw = 150, ch = 120, gap = 14;
		int perRow = Math.Max(1, (w - 80 + gap) / (cw + gap));
		_items = _req.Lines.Select((_, i) => new Rectangle(_panel.X + 40 + i % perRow * (cw + gap), _panel.Y + 110 + i / perRow * (ch + gap), cw, ch)).ToArray();
		int coinY = _panel.Bottom - 190;
		int coinW = Math.Min(118, (w - 80) / Checkout.Coins.Length - 10);
		_coins = Checkout.Coins.Select((_, i) => new Rectangle(_panel.X + 40 + i * (coinW + 10), coinY, coinW, 56)).ToArray();
		_undo = new Rectangle(_panel.X + 40, _panel.Bottom - 100, 160, 52);
		_confirm = new Rectangle(_panel.Right - 240, _panel.Bottom - 100, 200, 52);
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_finished)
		{
			return;
		}
		for (int i = 0; i < _items.Length; i++)
		{
			if (_items[i].Contains(x, y) && _scanned.Add(i))
			{
				Game1.playSound("coin");
				if (AllScanned)
				{
					Game1.playSound("purchaseRepeat");
				}
				return;
			}
		}
		if (!AllScanned)
		{
			return;
		}
		for (int i = 0; i < _coins.Length; i++)
		{
			if (_coins[i].Contains(x, y))
			{
				_given.Add(Checkout.Coins[i]);
				Game1.playSound("money");
				return;
			}
		}
		if (_undo.Contains(x, y) && _given.Count > 0)
		{
			_given.RemoveAt(_given.Count - 1);
			Game1.playSound("smallSelect");
		}
		else if (_confirm.Contains(x, y))
		{
			Finish();
		}
	}

	/// <summary>不管怎么关掉的（Esc、手柄 B、别的窗口顶掉），没收完就算先不收，顾客接着排队。</summary>
	protected override void cleanupBeforeExit()
	{
		if (!_finished)
		{
			_finished = true;
			_req.Cancel();
		}
		base.cleanupBeforeExit();
	}

	public override void receiveRightClick(int x, int y, bool playSound = true)
	{
		if (AllScanned && _given.Count > 0)
		{
			_given.RemoveAt(_given.Count - 1);
		}
	}

	private void Finish()
	{
		_finished = true;
		double seconds = (DateTime.UtcNow - _start).TotalSeconds;
		bool ok = Given == Change;
		Game1.playSound(ok ? "reward" : "cancel");
		exitThisMenu(playSound: false);
		_req.Done(Given, seconds);
	}

	public override void receiveKeyPress(Keys key)
	{
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			exitThisMenu();
		}
		else if (key == Keys.Space && !AllScanned)
		{
			// 空格：扫下一样。
			int next = Enumerable.Range(0, _items.Length).FirstOrDefault(i => !_scanned.Contains(i), -1);
			if (next >= 0)
			{
				receiveLeftClick(_items[next].Center.X, _items[next].Center.Y);
			}
		}
		else if (key == Keys.Enter && AllScanned)
		{
			Finish();
		}
	}

	public override void receiveGamePadButton(Buttons b)
	{
		if (b == Buttons.B)
		{
			receiveKeyPress(Keys.Escape);
		}
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
		Hearth.WoodFrame(b, _panel);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		Hearth.InkBold(b, Tr.Get("mart.checkoutTitle", "收银：{{name}}", ("name", _req.Name)), new Vector2(_panel.X + 36, _panel.Y + 28), Hearth.Quill);
		int secs = (int)(DateTime.UtcNow - _start).TotalSeconds;
		Hearth.Ink(b, Tr.Get("mart.checkoutTimer", "{{s}} 秒", ("s", secs)), new Vector2(_panel.Right - 140, _panel.Y + 32), secs <= 10 ? Hearth.LeafDeep : secs <= 20 ? Hearth.QuillSoft : Hearth.Berry, 0.85f);
		Hearth.Ink(b, AllScanned ? Tr.Get("mart.checkoutStep2", "扫好了。顾客递来钱，用硬币凑出找零，点“找零”。") : Tr.Get("mart.checkoutStep1", "点篮子里的东西，一样样扫进袋子（空格也行）。"), new Vector2(_panel.X + 36, _panel.Y + 68), Hearth.QuillSoft, 0.75f);
		for (int i = 0; i < _items.Length; i++)
		{
			Line l = _req.Lines[i];
			Rectangle r = _items[i];
			bool done = _scanned.Contains(i);
			Hearth.CardSlip(b, r, highlight: !done && r.Contains(mx, my), fill: done ? Hearth.LeafPale : null);
			l.Item.drawInMenu(b, new Vector2(r.X + (r.Width - 64) / 2, r.Y + 6), 1f, done ? 0.6f : 1f, 0.9f);
			Hearth.InkMiddle(b, Hearth.Truncate(l.Item.DisplayName, r.Width - 12, 0.6f), new Rectangle(r.X, r.Y + 72, r.Width, 22), Hearth.Quill, 0.6f);
			Hearth.InkMiddle(b, done ? $"{l.Total}g" : "?", new Rectangle(r.X, r.Y + 94, r.Width, 22), done ? Hearth.LeafDeep : Hearth.QuillPale, 0.65f);
		}
		int sumY = _coins[0].Y - 56;
		if (AllScanned)
		{
			Hearth.InkBold(b, Tr.Get("mart.checkoutSum", "合计 {{total}}g · 收到 {{paid}}g · 该找 ？", ("total", _req.Total), ("paid", _req.Paid)), new Vector2(_panel.X + 40, sumY), Hearth.Quill, 0.85f);
			for (int i = 0; i < _coins.Length; i++)
			{
				Hearth.PressPlate(b, _coins[i], Checkout.Coins[i] + "g", hover: _coins[i].Contains(mx, my), scale: 0.8f);
			}
			Hearth.Ink(b, Tr.Get("mart.checkoutGiven", "找零：{{n}}g", ("n", Given)), new Vector2(_undo.Right + 30, _undo.Y + 12), Hearth.Quill, 0.9f);
			Hearth.PressPlate(b, _undo, Tr.Get("mart.checkoutUndo", "退一枚"), hover: _undo.Contains(mx, my), disabled: _given.Count == 0, scale: 0.8f);
			Hearth.PressPlate(b, _confirm, Change == 0 && Given == 0 ? Tr.Get("mart.checkoutExact", "刚好，收下") : Tr.Get("mart.checkoutConfirm", "找零"), primary: true, hover: _confirm.Contains(mx, my));
		}
		else
		{
			Hearth.Ink(b, Tr.Get("mart.checkoutScanned", "已扫 {{n}}/{{all}}", ("n", _scanned.Count), ("all", _req.Lines.Count)), new Vector2(_panel.X + 40, sumY), Hearth.QuillSoft, 0.85f);
		}
		drawMouse(b);
	}
}
