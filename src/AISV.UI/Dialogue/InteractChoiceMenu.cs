using System;
using System.Collections.Generic;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Dialogue;

/// <summary>
/// 点配偶时头顶弹出的小圆盘（样子像原版的表情圆盘）：几个圆钮横排在头顶，点一个就关掉并执行。
/// ESC、右键或点到外面就关掉什么也不做。手柄左右选、A 确认；刚弹出来的几帧不收按键，免得开窗那一下就点上了。
/// </summary>
public sealed class InteractChoiceMenu : IClickableMenu
{
	public enum Icon
	{
		Talk,
		Heart,
		Hug
	}

	public sealed record Choice(string Label, Icon Icon, Action Pick);

	private const int Size = 72;

	private const int Gap = 20;

	private const int InputCooldownTicks = 10;

	private readonly NPC _npc;

	private readonly List<Choice> _choices;

	private readonly string? _hint;

	private readonly List<Rectangle> _buttons = new List<Rectangle>();

	private int _selected;

	private int _age;

	private bool _done;

	public InteractChoiceMenu(NPC npc, List<Choice> choices, string? hint = null)
	{
		_npc = npc;
		_choices = choices;
		_hint = hint;
		Layout();
		Game1.playSound("shwip");
	}

	private void Layout()
	{
		_buttons.Clear();
		Vector2 head = Game1.GlobalToLocal(Game1.viewport, _npc.Position + new Vector2(32f, -_npc.Sprite.SpriteHeight * 4f + 8f));
		head = Utility.ModifyCoordinatesForUIScale(head);
		int total = _choices.Count * Size + (_choices.Count - 1) * Gap;
		int x = (int)head.X - total / 2;
		int y = Math.Max(8, (int)head.Y - Size - 40);
		x = Math.Clamp(x, 8, Math.Max(8, Game1.uiViewport.Width - total - 8));
		for (int i = 0; i < _choices.Count; i++)
		{
			_buttons.Add(new Rectangle(x + i * (Size + Gap), y, Size, Size));
		}
		xPositionOnScreen = x - 16;
		yPositionOnScreen = y - 16;
		width = total + 32;
		height = Size + 80;
	}

	public override void update(GameTime time)
	{
		base.update(time);
		_age++;
		Layout();
		for (int i = 0; i < _buttons.Count; i++)
		{
			if (_buttons[i].Contains(Game1.getMouseX(), Game1.getMouseY()))
			{
				_selected = i;
			}
		}
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_age < InputCooldownTicks)
		{
			return;
		}
		for (int i = 0; i < _buttons.Count; i++)
		{
			if (_buttons[i].Contains(x, y))
			{
				Pick(i);
				return;
			}
		}
		Close();
	}

	public override void receiveRightClick(int x, int y, bool playSound = true) => Close();

	public override void receiveKeyPress(Keys key)
	{
		if (_age < InputCooldownTicks)
		{
			return;
		}
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			Close();
		}
		else if (key is Keys.Left or Keys.A)
		{
			Move(-1);
		}
		else if (key is Keys.Right or Keys.D)
		{
			Move(1);
		}
		else if (key is Keys.Enter or Keys.Space)
		{
			Pick(_selected);
		}
		else if (key >= Keys.D1 && key <= Keys.D9 && key - Keys.D1 < _choices.Count)
		{
			Pick(key - Keys.D1);
		}
	}

	public override void receiveGamePadButton(Buttons button)
	{
		if (_age < InputCooldownTicks)
		{
			return;
		}
		switch (button)
		{
			case Buttons.DPadLeft:
			case Buttons.LeftThumbstickLeft:
				Move(-1);
				break;
			case Buttons.DPadRight:
			case Buttons.LeftThumbstickRight:
				Move(1);
				break;
			case Buttons.A:
				Pick(_selected);
				break;
			case Buttons.B:
				Close();
				break;
		}
	}

	private void Move(int by)
	{
		_selected = (_selected + by + _choices.Count) % _choices.Count;
		Game1.playSound("shiny4");
	}

	private void Pick(int index)
	{
		if (_done || index < 0 || index >= _choices.Count)
		{
			return;
		}
		_done = true;
		Game1.playSound("smallSelect");
		exitThisMenu(playSound: false);
		_choices[index].Pick();
	}

	private void Close()
	{
		if (_done)
		{
			return;
		}
		_done = true;
		exitThisMenu();
	}

	public override void draw(SpriteBatch b)
	{
		for (int i = 0; i < _buttons.Count; i++)
		{
			Rectangle r = _buttons[i];
			bool on = i == _selected;
			Hearth.CastShadow(b, r, Size / 2);
			Hearth.Bordered(b, r, on ? Hearth.Honey : Hearth.Slip, on ? Hearth.Amber : Hearth.SlipEdge, Size / 2, on ? 3 : 2);
			Rectangle icon = new Rectangle(r.Center.X - 18, r.Center.Y - 18, 36, 36);
			switch (_choices[i].Icon)
			{
				case Icon.Heart:
					Hearth.Heart(b, new Rectangle(icon.X + 2, icon.Y + 4, 32, 28));
					break;
				case Icon.Hug:
					Hearth.Heart(b, new Rectangle(icon.X - 2, icon.Y + 8, 22, 20));
					Hearth.Heart(b, new Rectangle(icon.X + 16, icon.Y + 8, 22, 20));
					break;
				default:
					Hearth.Glyph(b, icon, Hearth.LeafPale, "…");
					break;
			}
			string label = (i + 1) + " " + _choices[i].Label;
			float w = Hearth.Width(label, 0.8f);
			Rectangle tag = new Rectangle(r.Center.X - (int)w / 2 - 8, r.Bottom + 6, (int)w + 16, 30);
			Hearth.Bordered(b, tag, Hearth.Slip, on ? Hearth.Amber : Hearth.SlipEdge, 10);
			Hearth.InkMiddle(b, label, tag, on ? Hearth.Quill : Hearth.QuillSoft, 0.8f);
		}
		if (!string.IsNullOrWhiteSpace(_hint) && _buttons.Count > 0)
		{
			float w = Hearth.Width(_hint!, 0.7f);
			int cx = (_buttons[0].X + _buttons[_buttons.Count - 1].Right) / 2;
			Rectangle r = new Rectangle(cx - (int)w / 2 - 10, _buttons[0].Bottom + 44, (int)w + 20, 28);
			Hearth.Bordered(b, r, Hearth.LeafPale, Hearth.Leaf, 10);
			Hearth.InkMiddle(b, _hint!, r, Hearth.LeafDeep, 0.7f);
		}
		drawMouse(b);
	}
}
