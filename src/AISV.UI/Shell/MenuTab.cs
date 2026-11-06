using System;
using System.IO;
using System.Runtime.CompilerServices;
using AISV.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>游戏菜单页签左边多一个小页签，点它打开外壳（地图页不显示）。只用 SMAPI 事件，不改游戏方法。</summary>
public sealed class MenuTab
{
	private const int Size = 64;

	private const int Shift = 30;

	private static readonly Rectangle Background = new Rectangle(16, 368, 16, 16);

	private readonly ConditionalWeakTable<GameMenu, StrongBox<int>> _shifted = new ConditionalWeakTable<GameMenu, StrongBox<int>>();

	private readonly string _iconPath;

	private readonly Action _open;

	private Texture2D? _icon;

	private bool _iconFailed;

	public MenuTab(string modDirectory, Action open)
	{
		_iconPath = Path.Combine(modDirectory, "assets", "ui", "menu_tab.png");
		_open = open;
	}

	private IInputHelper? _input;

	public void Attach(IModHelper helper)
	{
		_input = helper.Input;
		helper.Events.Display.RenderedActiveMenu += OnRendered;
		helper.Events.Input.ButtonPressed += OnButtonPressed;
	}

	private Rectangle? Bounds(out GameMenu? menu)
	{
		menu = Game1.activeClickableMenu as GameMenu;
		if (menu == null || menu.tabs.Count == 0 || menu.currentTab == GameMenu.mapTab || menu.invisible)
		{
			return null;
		}
		ClickableComponent first = menu.tabs[0];
		StrongBox<int> box = _shifted.GetValue(menu, _ => new StrongBox<int>(int.MinValue));
		if (first.bounds.X != box.Value)
		{
			foreach (ClickableComponent tab in menu.tabs)
			{
				tab.bounds.X += Shift;
			}
			box.Value = first.bounds.X;
		}
		return new Rectangle(first.bounds.X - Size - 4, first.bounds.Y, Size, Size);
	}

	private void OnRendered(object? sender, RenderedActiveMenuEventArgs e)
	{
		if (Bounds(out GameMenu? menu) is not Rectangle r || menu == null)
		{
			return;
		}
		SpriteBatch b = e.SpriteBatch;
		b.Draw(Game1.mouseCursors, new Vector2(r.X, r.Y), Background, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.0001f);
		if (Icon() is Texture2D icon)
		{
			b.Draw(icon, new Vector2(r.X, r.Y), null, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.0001f);
		}
		if (r.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			IClickableMenu.drawHoverText(b, Tr.Get("shell.menuTab", "AI星露谷"), Game1.smallFont);
		}
		menu.drawMouse(b);
	}

	private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
	{
		if (e.Button != SButton.MouseLeft || Bounds(out GameMenu? menu) is not Rectangle r || menu == null)
		{
			return;
		}
		Vector2 p = e.Cursor.GetScaledScreenPixels();
		if (!r.Contains((int)p.X, (int)p.Y))
		{
			return;
		}
		_input?.Suppress(e.Button);
		menu.exitThisMenu(playSound: false);
		Game1.playSound("smallSelect");
		_open();
	}

	private Texture2D? Icon()
	{
		if (_icon is { IsDisposed: false } || _iconFailed)
		{
			return _icon;
		}
		try
		{
			using FileStream stream = File.OpenRead(_iconPath);
			_icon = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
		}
		catch
		{
			_iconFailed = true;
		}
		return _icon;
	}
}
