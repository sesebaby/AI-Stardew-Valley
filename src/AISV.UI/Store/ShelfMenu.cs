using System;
using AISV.Core.Text;
using AISV.Game.Store;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.UI.Store;

/// <summary>陈列台：看货、改价（原价的 50%～200%，每次 10%）、取下。价格贵了来的人少、口碑会掉；便宜了卖得快。</summary>
public sealed class ShelfMenu : IClickableMenu
{
	private readonly Furniture _shelf;

	private Rectangle _panel, _minus, _plus, _take, _done;

	public ShelfMenu(Furniture shelf)
	{
		_shelf = shelf;
		Layout();
	}

	private void Layout()
	{
		int w = Math.Min(560, Game1.uiViewport.Width - 48), h = 330;
		_panel = new Rectangle((Game1.uiViewport.Width - w) / 2, (Game1.uiViewport.Height - h) / 2, w, h);
		xPositionOnScreen = _panel.X;
		yPositionOnScreen = _panel.Y;
		width = w;
		height = h;
		int row = _panel.Y + 170;
		_minus = new Rectangle(_panel.X + 40, row, 90, 52);
		_plus = new Rectangle(_panel.Right - 130, row, 90, 52);
		_take = new Rectangle(_panel.X + 40, _panel.Bottom - 76, 170, 52);
		_done = new Rectangle(_panel.Right - 190, _panel.Bottom - 76, 150, 52);
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		int pct = MartService.Percent(_shelf);
		if (_minus.Contains(x, y) && pct > 50)
		{
			MartService.SetPercent(_shelf, pct - 10);
			Game1.playSound("smallSelect");
		}
		else if (_plus.Contains(x, y) && pct < 200)
		{
			MartService.SetPercent(_shelf, pct + 10);
			Game1.playSound("smallSelect");
		}
		else if (_take.Contains(x, y) && _shelf.heldObject.Value != null)
		{
			MartService.TakeBack(_shelf);
			exitThisMenu();
		}
		else if (_done.Contains(x, y) || !_panel.Contains(x, y))
		{
			exitThisMenu();
		}
	}

	public override void receiveKeyPress(Keys key)
	{
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			exitThisMenu();
		}
		else if (key is Keys.Left or Keys.A)
		{
			receiveLeftClick(_minus.Center.X, _minus.Center.Y);
		}
		else if (key is Keys.Right or Keys.D)
		{
			receiveLeftClick(_plus.Center.X, _plus.Center.Y);
		}
	}

	public override void receiveGamePadButton(Buttons b)
	{
		if (b == Buttons.B || b == Buttons.A)
		{
			exitThisMenu();
		}
		else if (b == Buttons.DPadLeft)
		{
			receiveLeftClick(_minus.Center.X, _minus.Center.Y);
		}
		else if (b == Buttons.DPadRight)
		{
			receiveLeftClick(_plus.Center.X, _plus.Center.Y);
		}
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.4f);
		Hearth.WoodFrame(b, _panel);
		Hearth.InkBold(b, Tr.Get("mart.shelfTitle", "陈列台"), new Vector2(_panel.X + 36, _panel.Y + 30), Hearth.Quill);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		if (_shelf.heldObject.Value is SObject o)
		{
			Rectangle slot = new Rectangle(_panel.X + 36, _panel.Y + 74, 72, 72);
			Hearth.CardSlip(b, slot);
			o.drawInMenu(b, new Vector2(slot.X + 4, slot.Y + 4), 1f);
			int base0 = MartService.BasePrice(o);
			int pct = MartService.Percent(_shelf);
			Hearth.Ink(b, Tr.Get("mart.shelfItem", "{{item}} ×{{n}}", ("item", o.DisplayName), ("n", o.Stack)), new Vector2(slot.Right + 18, slot.Y + 4), Hearth.Quill);
			Hearth.Ink(b, Tr.Get("mart.shelfPrice", "每个 {{price}}g（原价 {{base}}g 的 {{pct}}%）", ("price", MartService.UnitPrice(_shelf)), ("base", base0), ("pct", pct)), new Vector2(slot.Right + 18, slot.Y + 40), pct > 130 ? Hearth.Rust : pct < 90 ? Hearth.LeafDeep : Hearth.QuillSoft, 0.85f);
			Hearth.PressPlate(b, _minus, "-10%", hover: _minus.Contains(mx, my), disabled: pct <= 50);
			Hearth.PressPlate(b, _plus, "+10%", hover: _plus.Contains(mx, my), disabled: pct >= 200);
			string tip = pct > 130 ? Tr.Get("mart.tipHigh", "贵了：来的人少，口碑会慢慢变差") : pct < 90 ? Tr.Get("mart.tipLow", "便宜：卖得快，抢 Pierre 和 Joja 的生意更狠") : Tr.Get("mart.tipFair", "公道价");
			Hearth.InkMiddle(b, tip, new Rectangle(_minus.Right, _minus.Y, _plus.X - _minus.Right, _minus.Height), Hearth.QuillSoft, 0.8f);
			Hearth.PressPlate(b, _take, Tr.Get("mart.takeBack", "取下"), hover: _take.Contains(mx, my));
		}
		else
		{
			Hearth.Ink(b, Tr.Get("mart.shelfEmpty", "空着。拿着要卖的东西点陈列台就能上架。"), new Vector2(_panel.X + 36, _panel.Y + 90), Hearth.QuillSoft, 0.9f);
		}
		Hearth.PressPlate(b, _done, Tr.Get("mart.done", "好"), primary: true, hover: _done.Contains(mx, my));
		drawMouse(b);
	}
}
