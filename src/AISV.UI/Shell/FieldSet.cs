using System.Collections.Generic;
using System.Linq;
using AISV.UI.Dialogue;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>一页上的几个输入框：点哪个哪个接键盘，Tab 换下一个，退格、回车转给当前那个（Ctrl+V 由游戏的键盘分发自己贴）。</summary>
internal sealed class FieldSet
{
	private readonly List<TextArea> _fields = new List<TextArea>();

	private bool _backspaceHeld;

	private double _backspaceTimer;

	/// <param name="singleLine">单行（回车跳到下一个框，不换行）。</param>
	public TextArea Add(int maxChars, string placeholder = "", bool singleLine = false)
	{
		TextArea area = new TextArea(maxChars) { Placeholder = placeholder, SingleLine = singleLine };
		_fields.Add(area);
		return area;
	}

	public TextArea? Focused => _fields.FirstOrDefault(f => f.Selected);

	public bool Typing => Focused != null;

	public void Focus(TextArea area)
	{
		foreach (TextArea f in _fields.Where(f => f != area))
		{
			f.Blur();
		}
		area.Focus();
	}

	public void BlurAll()
	{
		foreach (TextArea f in _fields)
		{
			f.Blur();
		}
		_backspaceHeld = false;
	}

	/// <summary>每帧调一次：按住退格停 400 毫秒后连删。</summary>
	public void Update()
	{
		TextArea? f = Focused;
		if (f == null || !_backspaceHeld || !Keyboard.GetState().IsKeyDown(Keys.Back))
		{
			_backspaceHeld = false;
			return;
		}
		_backspaceTimer += Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16;
		if (_backspaceTimer >= 400)
		{
			_backspaceTimer -= 50;
			f.DeleteChar();
		}
	}

	/// <summary>当前有输入框在打字时处理按键；Esc 只是离开输入框。返回 true 表示吃掉了。</summary>
	public bool KeyPress(Keys key)
	{
		TextArea? f = Focused;
		if (f == null)
		{
			return false;
		}
		KeyboardState s = Game1.input.GetKeyboardState();
		bool ctrl = s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl);
		bool shift = s.IsKeyDown(Keys.LeftShift) || s.IsKeyDown(Keys.RightShift);
		switch (key)
		{
		case Keys.Escape:
			BlurAll();
			return true;
		case Keys.Tab:
			Move(f, shift ? -1 : 1);
			return true;
		case Keys.Back:
			f.DeleteChar();
			_backspaceHeld = true;
			_backspaceTimer = 0;
			return true;
		case Keys.Enter when !ctrl && f.SingleLine:
			Move(f, 1);
			return true;
		case Keys.Enter when !ctrl:
			f.NewLine();
			return true;
		default:
			return !(key == Keys.Enter && ctrl);
		}
	}

	private void Move(TextArea from, int step) => Focus(_fields[(_fields.IndexOf(from) + step + _fields.Count) % _fields.Count]);
}
