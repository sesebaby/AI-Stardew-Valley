using System;
using System.Collections.Generic;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Group;

/// <summary>地图群聊底部的输入栏：上面是最近几句，下面是"离开 · 输入框 · 发送"。回车发送，Shift+回车换行，ESC 离开。</summary>
internal sealed class GroupInputBar : IClickableMenu
{
	private const int MaxChars = 300;

	private const int LogLines = 4;

	private readonly MapGroupChat _chat;

	private readonly Action<string> _send;

	private readonly Action _leave;

	private readonly TextArea _box = new TextArea(MaxChars) { Placeholder = Tr.Get("group.placeholder", "说点什么……回车发送") };

	private Rectangle _panel;

	private Rectangle _logRect;

	private Rectangle _leaveButton;

	private Rectangle _sendButton;

	private bool _resolved;

	private bool _backspaceHeld;

	private double _backspaceMs;

	public GroupInputBar(MapGroupChat chat, Action<string> send, Action leave) : base(0, 0, 0, 0, showUpperRightCloseButton: false)
	{
		_chat = chat;
		_send = send;
		_leave = leave;
		Layout();
		_box.Focus();
	}

	private void Layout()
	{
		int lh = Hearth.LineHeight();
		width = Math.Min(980, Game1.uiViewport.Width - 48);
		int logH = 44 + LogLines * (lh + 2);
		int inputH = 28 + lh * 2;
		height = logH + inputH + 32;
		xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
		yPositionOnScreen = Game1.uiViewport.Height - height - 16;
		_panel = new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height);
		_logRect = new Rectangle(_panel.X + 20, _panel.Y + 48, _panel.Width - 40, logH - 56);
		int rowY = _panel.Bottom - 16 - inputH;
		const int bw = 112;
		_leaveButton = new Rectangle(_panel.X + 16, rowY + (inputH - 48) / 2, bw, 48);
		_sendButton = new Rectangle(_panel.Right - 16 - bw, rowY + (inputH - 48) / 2, bw, 48);
		_box.Bounds = new Rectangle(_leaveButton.Right + 12, rowY, _sendButton.X - 12 - (_leaveButton.Right + 12), inputH);
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
	{
		base.gameWindowSizeChanged(oldBounds, newBounds);
		Layout();
	}

	private void Submit()
	{
		string text = _box.Text.Trim();
		if (_resolved || text.Length == 0)
		{
			return;
		}
		_resolved = true;
		_box.Text = "";
		exitThisMenu(playSound: false);
		_send(text);
	}

	private void Leave()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		exitThisMenu(playSound: false);
		_leave();
	}

	/// <summary>群聊自己散了时关掉，不再回调。</summary>
	public void CloseFromOwner()
	{
		_resolved = true;
		if (Game1.activeClickableMenu == this)
		{
			exitThisMenu(playSound: false);
		}
	}

	protected override void cleanupBeforeExit()
	{
		_box.Blur();
		base.cleanupBeforeExit();
		if (!_resolved)
		{
			_resolved = true;
			_leave();
		}
	}

	public override void receiveKeyPress(Keys key)
	{
		switch (key)
		{
		case Keys.Escape:
			Leave();
			return;
		case Keys.Enter:
		{
			KeyboardState state = Keyboard.GetState();
			if (state.IsKeyDown(Keys.LeftShift) || state.IsKeyDown(Keys.RightShift))
			{
				_box.NewLine();
			}
			else
			{
				Submit();
			}
			return;
		}
		case Keys.Back:
			_box.DeleteChar();
			_backspaceHeld = true;
			_backspaceMs = 0;
			return;
		}
	}

	public override void receiveGamePadButton(Buttons button)
	{
		if (button == Buttons.B)
		{
			Leave();
		}
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_sendButton.Contains(x, y))
		{
			Submit();
		}
		else if (_leaveButton.Contains(x, y))
		{
			Game1.playSound("smallSelect");
			Leave();
		}
		else if (_box.Bounds.Contains(x, y))
		{
			_box.Focus();
		}
	}

	public override void receiveRightClick(int x, int y, bool playSound = true)
	{
	}

	public override void update(GameTime time)
	{
		base.update(time);
		if (Game1.keyboardDispatcher.Subscriber == null)
		{
			_box.Focus();
		}
		if (_backspaceHeld && Keyboard.GetState().IsKeyDown(Keys.Back))
		{
			_backspaceMs += time.ElapsedGameTime.TotalMilliseconds;
			if (_backspaceMs >= 400)
			{
				_backspaceMs -= 50;
				_box.DeleteChar();
			}
		}
		else
		{
			_backspaceHeld = false;
		}
	}

	public override void draw(SpriteBatch b)
	{
		_chat.Draw(b);
		Hearth.CastShadow(b, _panel, 14, 6);
		Hearth.Bordered(b, _panel, Hearth.Parchment * 0.97f, Hearth.SlipEdge, 14);
		DrawTitle(b);
		DrawLog(b);
		_box.Draw(b);
		string count = _box.Text.Length + "/" + MaxChars;
		Vector2 cs = Hearth.Measure(count, 0.7f);
		Hearth.Ink(b, count, new Vector2(_box.Bounds.Right - cs.X - 12, _box.Bounds.Bottom - cs.Y - 6), _box.Text.Length >= MaxChars ? Hearth.Berry : Hearth.QuillPale, 0.7f);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		Hearth.PressPlate(b, _leaveButton, Tr.Get("group.leave", "离开"), hover: _leaveButton.Contains(mx, my));
		Hearth.PressPlate(b, _sendButton, Tr.Get("reply.send", "发送"), primary: true, hover: _sendButton.Contains(mx, my), disabled: _box.Text.Trim().Length == 0);
		drawMouse(b);
	}

	private void DrawTitle(SpriteBatch b)
	{
		Hearth.InkBold(b, Hearth.Truncate(_chat.MemberNames(), _panel.Width / 2f, 0.85f), new Vector2(_panel.X + 20, _panel.Y + 14), Hearth.Quill, 0.85f);
		string right = Tr.Get("group.repliesLeft", "还能说 {{count}} 句", ("count", _chat.RepliesLeft)) + "  ·  " + Tr.Get("group.inputHint", "回车发送 · ESC 离开");
		float rw = Hearth.Width(right, 0.7f);
		Hearth.Ink(b, right, new Vector2(_panel.Right - 20 - rw, _panel.Y + 18), Hearth.QuillPale, 0.7f);
		Hearth.HLine(b, _panel.X + 16, _panel.Y + 42, _panel.Width - 32, Hearth.Seam);
	}

	private void DrawLog(SpriteBatch b)
	{
		IReadOnlyList<MapGroupChat.LogLine> log = _chat.Log;
		int lh = Hearth.LineHeight() + 2;
		int y = _logRect.Bottom;
		string you = Tr.Get("group.you", "你");
		Hearth.BeginClip(b, _logRect);
		for (int i = log.Count - 1; i >= 0 && y > _logRect.Y; i--)
		{
			MapGroupChat.LogLine e = log[i];
			string name = (e.Mine ? you : e.Name) + (MapGroupChat.Cjk ? "：" : ": ");
			float nameW = Hearth.Width(name, 0.85f);
			string[] lines = Hearth.Wrap(e.Text.Replace('\n', ' '), Math.Max(120, _logRect.Width - (int)nameW - 8)).Split('\n');
			y -= lines.Length * lh;
			Hearth.Ink(b, name, new Vector2(_logRect.X, y + 2), e.Mine ? Hearth.LeafDeep : Hearth.QuillSoft, 0.85f);
			for (int j = 0; j < lines.Length; j++)
			{
				Hearth.Ink(b, lines[j], new Vector2(_logRect.X + nameW + 4, y + j * lh), Hearth.Quill);
			}
		}
		Hearth.EndClip(b);
	}
}
