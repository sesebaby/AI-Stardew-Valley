using System;
using System.Linq;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game;
using AISV.Game.Store;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.UI.Store;

/// <summary>
/// 货架：上面是货架的格子（每格一样货，看新鲜度、温度、来路、标价），下面是你的背包——点背包里的东西上架（右键只放一个），
/// 点格子选中，改价（原价的 50%～200%）或取下。
/// </summary>
public sealed class ShelfMenu : IClickableMenu
{
	private readonly Furniture _shelf;

	private readonly ShelfKind _kind;

	private InventoryMenu _bag = null!;

	private Rectangle _panel, _minus, _plus, _take, _done, _all;

	private Rectangle[] _slots = Array.Empty<Rectangle>();

	private int _selected;

	private string _hover = "";

	public ShelfMenu(Furniture shelf)
	{
		_shelf = shelf;
		_kind = MartShelves.Kind(shelf) ?? ShelfKinds.Shelf;
		Layout();
	}

	private void Layout()
	{
		int w = Math.Min(980, Game1.uiViewport.Width - 48), h = Math.Min(720, Game1.uiViewport.Height - 32);
		_panel = new Rectangle((Game1.uiViewport.Width - w) / 2, (Game1.uiViewport.Height - h) / 2, w, h);
		xPositionOnScreen = _panel.X;
		yPositionOnScreen = _panel.Y;
		width = w;
		height = h;
		int size = 80, gap = 12;
		int rowW = _kind.Slots * (size + gap) - gap;
		int sx = _panel.X + (w - rowW) / 2;
		_slots = Enumerable.Range(0, _kind.Slots).Select(i => new Rectangle(sx + i * (size + gap), _panel.Y + 110, size, size)).ToArray();
		int row = _panel.Y + 286;
		_minus = new Rectangle(_panel.X + 40, row, 100, 48);
		_plus = new Rectangle(_panel.X + 300, row, 100, 48);
		_take = new Rectangle(_panel.X + 430, row, 150, 48);
		_all = new Rectangle(_panel.Right - 250, row, 210, 48);
		int bagW = 12 * 64;
		_bag = new InventoryMenu(_panel.X + (w - bagW) / 2, _panel.Bottom - 3 * 64 - 100, playerInventory: true, highlightMethod: CanStock);
		_done = new Rectangle(_panel.Right - 170, _panel.Bottom - 74, 130, 50);
	}

	private bool CanStock(Item item) => MartShelves.Sellable(item) && _kind.Allows(MartShelves.Group((SObject)item));

	private Inventory Slots => MartShelves.Slots(_shelf);

	private SObject? Selected => _selected < Slots.Count ? Slots[_selected] as SObject : null;

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();

	public override void receiveLeftClick(int x, int y, bool playSound = true) => Click(x, y, all: true);

	public override void receiveRightClick(int x, int y, bool playSound = true) => Click(x, y, all: false);

	private void Click(int x, int y, bool all)
	{
		for (int i = 0; i < _slots.Length; i++)
		{
			if (_slots[i].Contains(x, y))
			{
				_selected = i;
				Game1.playSound("smallSelect");
				return;
			}
		}
		if (_bag.getItemAt(x, y) is SObject item && CanStock(item))
		{
			int n = MartShelves.Add(_shelf, item, all ? item.Stack : 1, Origins.Farm);
			if (n > 0)
			{
				item.Stack -= n;
				if (item.Stack <= 0)
				{
					Game1.player.removeItemFromInventory(item);
				}
				Game1.playSound("woodyStep");
				_selected = Math.Max(0, Enumerable.Range(0, Slots.Count).LastOrDefault(i => Slots[i] is SObject s && s.canStackWith(item)));
			}
			else
			{
				Game1.playSound("cancel");
				Hints.Hud(Tr.Get("mart.shelfFull", "{{shelf}}放满了。", ("shelf", MartService.ShelfName(_shelf))));
			}
			return;
		}
		SObject? sel = Selected;
		int pct = sel == null ? 100 : MartShelves.Percent(sel);
		if (sel != null && _minus.Contains(x, y) && pct > 50)
		{
			MartShelves.SetPercent(sel, pct - 10);
			Game1.playSound("smallSelect");
		}
		else if (sel != null && _plus.Contains(x, y) && pct < 200)
		{
			MartShelves.SetPercent(sel, pct + 10);
			Game1.playSound("smallSelect");
		}
		else if (sel != null && _take.Contains(x, y))
		{
			SObject? taken = MartShelves.Take(_shelf, _selected, sel.Stack);
			if (taken != null)
			{
				MartShelves.Untag(taken);
				if (!Game1.player.addItemToInventoryBool(taken))
				{
					Game1.createItemDebris(taken, Game1.player.getStandingPosition(), -1);
				}
				Game1.playSound("pickUpItem");
			}
		}
		else if (_all.Contains(x, y))
		{
			// 整张货架一起改：照选中那样的标价。
			foreach (SObject o in MartShelves.Goods(_shelf))
			{
				MartShelves.SetPercent(o, pct);
			}
			Game1.playSound("smallSelect");
		}
		else if (_done.Contains(x, y) || !_panel.Contains(x, y))
		{
			exitThisMenu();
		}
	}

	protected override void cleanupBeforeExit()
	{
		if (Game1.currentLocation != null)
		{
			MartShelves.RefreshDisplay(Game1.currentLocation);
		}
		base.cleanupBeforeExit();
	}

	public override void receiveKeyPress(Keys key)
	{
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			exitThisMenu();
		}
		else if (key is Keys.Left or Keys.A)
		{
			Click(_minus.Center.X, _minus.Center.Y, true);
		}
		else if (key is Keys.Right or Keys.D)
		{
			Click(_plus.Center.X, _plus.Center.Y, true);
		}
	}

	public override void receiveGamePadButton(Buttons b)
	{
		if (b == Buttons.B)
		{
			exitThisMenu();
		}
		else if (b == Buttons.LeftShoulder)
		{
			_selected = (_selected + _kind.Slots - 1) % _kind.Slots;
		}
		else if (b == Buttons.RightShoulder)
		{
			_selected = (_selected + 1) % _kind.Slots;
		}
	}

	public override void performHoverAction(int x, int y)
	{
		_hover = _bag.getItemAt(x, y) is Item i ? i.DisplayName : "";
		_bag.hover(x, y, null);
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.4f);
		Hearth.WoodFrame(b, _panel);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		Hearth.InkBold(b, MartService.ShelfName(_shelf), new Vector2(_panel.X + 36, _panel.Y + 28), Hearth.Quill);
		string groups = _kind.Groups.Length == Goods.All.Length ? Tr.Get("mart.anyGoods", "什么都能放") : string.Join(Tr.Get("list.sep", "、"), _kind.Groups.Select(MartNames.Group));
		Hearth.Ink(b, Tr.Get("mart.shelfInfo", "{{temp}} · {{slots}} 格，每格 {{per}} 件 · {{groups}}", ("temp", MartNames.Temp(_kind.Temp)), ("slots", _kind.Slots), ("per", _kind.PerSlot), ("groups", groups)), new Vector2(_panel.X + 36, _panel.Y + 66), Hearth.QuillSoft, 0.75f);
		Inventory slots = Slots;
		for (int i = 0; i < _slots.Length; i++)
		{
			Rectangle r = _slots[i];
			Hearth.CardSlip(b, r, highlight: i == _selected);
			if (i < slots.Count && slots[i] is SObject o)
			{
				o.drawInMenu(b, new Vector2(r.X + 8, r.Y + 4), 1f);
				string stage = MartShelves.Stage(_shelf, o);
				Color c = stage == Freshness.Expired ? Hearth.Berry : stage == Freshness.Near ? Hearth.Rust : Hearth.LeafDeep;
				Hearth.InkMiddle(b, MartNames.Stage(stage), new Rectangle(r.X - 6, r.Bottom + 2, r.Width + 12, 26), c, 0.6f);
				Hearth.InkMiddle(b, MartShelves.UnitPrice(o) + "g", new Rectangle(r.X - 6, r.Bottom + 26, r.Width + 12, 26), Hearth.QuillSoft, 0.6f);
			}
		}
		int dy = _panel.Y + 250;
		if (Selected is SObject sel)
		{
			string stage = MartShelves.Stage(_shelf, sel);
			int days = Game1.Date.TotalDays - MartShelves.StockedDay(sel);
			string temp = MartShelves.WrongTemp(_shelf, sel) ? Tr.Get("mart.wrongTemp", " · 该放{{temp}}，坏得快、评价会差", ("temp", MartNames.Temp(Goods.Prefers(MartShelves.Group(sel))))) : "";
			Hearth.Ink(b, Tr.Get("mart.slotLine", "{{item}} ×{{n}} · {{origin}} · 上架 {{days}} 天（{{stage}}）{{temp}}", ("item", sel.DisplayName), ("n", sel.Stack), ("origin", MartNames.Origin(MartShelves.Origin(sel))), ("days", days), ("stage", MartNames.Stage(stage)), ("temp", temp)), new Vector2(_panel.X + 40, dy), temp.Length > 0 ? Hearth.Rust : Hearth.Quill, 0.8f);
			int pct = MartShelves.Percent(sel);
			Hearth.PressPlate(b, _minus, "-10%", hover: _minus.Contains(mx, my), disabled: pct <= 50, scale: 0.8f);
			Hearth.InkMiddle(b, Tr.Get("mart.pricePct", "{{price}}g（{{pct}}%）", ("price", MartShelves.UnitPrice(sel)), ("pct", pct)), new Rectangle(_minus.Right, _minus.Y, _plus.X - _minus.Right, _minus.Height), pct > 130 ? Hearth.Rust : pct < 90 ? Hearth.LeafDeep : Hearth.Quill, 0.8f);
			Hearth.PressPlate(b, _plus, "+10%", hover: _plus.Contains(mx, my), disabled: pct >= 200, scale: 0.8f);
			Hearth.PressPlate(b, _take, Tr.Get("mart.takeBack", "取下"), hover: _take.Contains(mx, my), scale: 0.8f);
			Hearth.PressPlate(b, _all, Tr.Get("mart.priceAll", "整架都按这个价"), hover: _all.Contains(mx, my), scale: 0.75f);
			string tip = pct > 130 ? Tr.Get("mart.tipHigh", "贵了：来的人少，口碑会慢慢变差") : pct < 90 ? Tr.Get("mart.tipLow", "便宜：卖得快，抢 Pierre 和 Joja 的生意更狠") : Tr.Get("mart.tipFair", "公道价");
			Hearth.Ink(b, tip, new Vector2(_panel.X + 40, _minus.Bottom + 6), Hearth.QuillPale, 0.7f);
		}
		else
		{
			Hearth.Ink(b, Tr.Get("mart.slotEmpty", "空格子。点下面背包里的东西上架（右键只放一个）。"), new Vector2(_panel.X + 40, dy), Hearth.QuillSoft, 0.8f);
		}
		Hearth.Ink(b, Tr.Get("mart.bagHint", "背包（亮着的是这个货架能放的）"), new Vector2(_bag.xPositionOnScreen, _bag.yPositionOnScreen - 54), Hearth.QuillSoft, 0.7f);
		_bag.draw(b);
		Hearth.PressPlate(b, _done, Tr.Get("mart.done", "好"), primary: true, hover: _done.Contains(mx, my));
		if (_hover.Length > 0)
		{
			drawHoverText(b, _hover, Game1.smallFont);
		}
		drawMouse(b);
	}
}
