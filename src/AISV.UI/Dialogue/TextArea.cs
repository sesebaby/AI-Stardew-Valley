using System;
using System.Linq;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Dialogue;

/// <summary>
/// 多行输入框：接游戏的键盘分发（含输入法上屏的整段文字和粘贴），光标固定在末尾，超出高度时显示最后几行。
/// </summary>
public sealed class TextArea : IKeyboardSubscriber
{
	private string _text = "";

	public TextArea(int maxChars)
	{
		MaxChars = maxChars;
	}

	public int MaxChars { get; }

	public Rectangle Bounds { get; set; }

	public string Placeholder { get; set; } = "";

	public bool Locked { get; set; }

	/// <summary>单行：不换行，贴进来的换行变成空格。</summary>
	public bool SingleLine { get; set; }

	public string Text
	{
		get => _text;
		set => _text = Clip(value ?? "");
	}

	public bool Selected { get; set; }

	public void Focus()
	{
		Selected = true;
		Game1.keyboardDispatcher.Subscriber = this;
	}

	public void Blur()
	{
		Selected = false;
		if (Game1.keyboardDispatcher.Subscriber == this)
		{
			Game1.keyboardDispatcher.Subscriber = null;
		}
	}

	public void RecieveTextInput(char inputChar)
	{
		if (!Locked && !char.IsControl(inputChar))
		{
			_text = Clip(_text + inputChar);
		}
	}

	public void RecieveTextInput(string text)
	{
		if (!Locked && !string.IsNullOrEmpty(text))
		{
			if (SingleLine)
			{
				text = text.Replace('\n', ' ');
			}
			_text = Clip(_text + new string(text.Where(c => c == '\n' || !char.IsControl(c)).ToArray()));
		}
	}

	/// <summary>退格、回车由菜单的 receiveKeyPress 转过来（见 <see cref="ReplyMenu"/>），这里不处理，免得算两次。</summary>
	public void RecieveCommandInput(char command)
	{
	}

	/// <summary>回车换行。</summary>
	public void NewLine()
	{
		if (!Locked && !SingleLine)
		{
			_text = Clip(_text + "\n");
		}
	}

	public void RecieveSpecialInput(Keys key)
	{
	}

	/// <summary>删掉最后一个字（代理对一起删）。</summary>
	public void DeleteChar()
	{
		if (Locked || _text.Length == 0)
		{
			return;
		}
		int cut = _text.Length >= 2 && char.IsLowSurrogate(_text[^1]) && char.IsHighSurrogate(_text[^2]) ? 2 : 1;
		_text = _text.Substring(0, _text.Length - cut);
	}

	public void Draw(SpriteBatch b)
	{
		AisvTheme.Field(b, Bounds, Selected && !Locked);
		Rectangle inner = new Rectangle(Bounds.X + 14, Bounds.Y + 10, Bounds.Width - 28, Bounds.Height - 20);
		int lineHeight = AisvTheme.LineHeight();
		if (_text.Length == 0)
		{
			b.DrawString(Game1.smallFont, AisvTheme.Wrap(Placeholder, inner.Width), new Vector2(inner.X, inner.Y), AisvTheme.TextFaint);
			if (Selected && !Locked && Caret())
			{
				AisvTheme.Fill(b, new Rectangle(inner.X, inner.Y + 4, 2, lineHeight - 8), AisvTheme.Text);
			}
			return;
		}
		string[] lines = AisvTheme.Wrap(_text, inner.Width).Split('\n');
		int visible = Math.Max(1, inner.Height / lineHeight);
		int first = Math.Max(0, lines.Length - visible);
		for (int i = first; i < lines.Length; i++)
		{
			b.DrawString(Game1.smallFont, lines[i], new Vector2(inner.X, inner.Y + (i - first) * lineHeight), Locked ? AisvTheme.TextMuted : AisvTheme.Text);
		}
		if (Selected && !Locked && Caret())
		{
			string last = lines[^1];
			int x = inner.X + (int)Game1.smallFont.MeasureString(last).X + 1;
			int y = inner.Y + (lines.Length - 1 - first) * lineHeight;
			AisvTheme.Fill(b, new Rectangle(Math.Min(x, inner.Right - 2), y + 4, 2, lineHeight - 8), AisvTheme.Text);
		}
	}

	private static bool Caret() => DateTime.UtcNow.Millisecond < 600;

	private string Clip(string text) => text.Length <= MaxChars ? text : text.Substring(0, MaxChars);
}
