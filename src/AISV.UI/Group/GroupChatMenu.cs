using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Group;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Group;

/// <summary>
/// 群聊窗口：顶上是成员头像（正在说话的人亮起来），中间是聊天记录（村民在左、你在右，新消息逐字出现，点一下跳过），
/// 底下是"结束 · 输入框 · 发送"。
/// </summary>
internal sealed class GroupChatMenu : IClickableMenu, IGroupView
{
	private enum Phase
	{
		Revealing,
		Typing,
		Waiting,
		Done
	}

	private sealed class Message
	{
		public NPC? Npc;

		public string Text = "";

		public bool Mine => Npc == null;
	}

	private const int MaxChars = 300;

	private const float MsPerChar = 22f;

	private const int AvatarSize = 52;

	private readonly List<NPC> _members;

	private readonly Func<string, string> _display;

	private readonly List<Message> _messages = new List<Message>();

	private readonly TextArea _box = new TextArea(MaxChars) { Placeholder = Tr.Get("group.windowPlaceholder", "说点什么……Ctrl+回车发送") };

	private IReadOnlyList<GroupLine> _pending = Array.Empty<GroupLine>();

	private int _next;

	private float _shown;

	private double _holdMs;

	private int _repliesLeft;

	private Phase _phase;

	private double _dotMs;

	private int _dots;

	private int _scroll;

	private int _maxScroll;

	private bool _stick = true;

	private bool _backspaceHeld;

	private double _backspaceMs;

	private Rectangle _page;

	private Rectangle _header;

	private Rectangle _transcript;

	private Rectangle _endButton;

	private Rectangle _sendButton;

	private Rectangle _closeButton;

	public GroupChatMenu(IReadOnlyList<NPC> members, IReadOnlyList<GroupLine> opening, int repliesLeft, Func<string, string> display) : base(0, 0, 0, 0, showUpperRightCloseButton: false)
	{
		_members = members.ToList();
		_display = display;
		Layout();
		ShowLines(opening, repliesLeft);
	}

	public event Action<string>? Submitted;

	public bool IsOpen => Game1.activeClickableMenu == this;

	public void ShowLines(IReadOnlyList<GroupLine> lines, int repliesLeft)
	{
		_pending = lines;
		_next = 0;
		_repliesLeft = Math.Max(0, repliesLeft);
		SetPhase(Phase.Revealing);
		NextLine();
	}

	public void ShowThinking() => SetPhase(Phase.Waiting);

	public void Close()
	{
		if (IsOpen)
		{
			exitThisMenu(playSound: false);
		}
	}

	private void Layout()
	{
		width = Math.Min(1000, Game1.uiViewport.Width - 64);
		height = Math.Min(720, Game1.uiViewport.Height - 32);
		xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
		yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
		_page = new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + 16, width - 32, height - 32);
		_header = new Rectangle(_page.X, _page.Y, _page.Width, 84);
		Rectangle compose = new Rectangle(_page.X, _page.Bottom - 196, _page.Width, 196);
		_transcript = new Rectangle(_page.X + 16, _header.Bottom + 8, _page.Width - 32, compose.Y - 16 - _header.Bottom);
		const int bw = 120;
		_sendButton = new Rectangle(compose.Right - 20 - bw, compose.Bottom - 20 - 48, bw, 48);
		_endButton = new Rectangle(compose.X + 20, _sendButton.Y, bw, 48);
		_box.Bounds = new Rectangle(compose.X + 20, compose.Y + 40, compose.Width - 40, _sendButton.Y - 10 - (compose.Y + 40));
		_closeButton = new Rectangle(_page.Right - 52, _page.Y + 20, 40, 40);
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
	{
		base.gameWindowSizeChanged(oldBounds, newBounds);
		Layout();
	}

	private void SetPhase(Phase phase)
	{
		_phase = phase;
		if (phase == Phase.Typing)
		{
			_box.Focus();
		}
		else
		{
			_box.Blur();
		}
	}

	private void NextLine()
	{
		while (_next < _pending.Count)
		{
			GroupLine line = _pending[_next++];
			NPC? npc = _members.FirstOrDefault(n => string.Equals(n.Name, line.Speaker, StringComparison.OrdinalIgnoreCase));
			if (npc != null && !string.IsNullOrWhiteSpace(line.Text))
			{
				_messages.Add(new Message { Npc = npc, Text = line.Text.Trim() });
				_shown = 0;
				_holdMs = 0;
				_stick = true;
				Game1.playSound("dialogueCharacter");
				return;
			}
		}
		SetPhase(_repliesLeft > 0 ? Phase.Typing : Phase.Done);
	}

	private Message? Revealing => _phase == Phase.Revealing && _messages.Count > 0 && !_messages[^1].Mine ? _messages[^1] : null;

	private void Submit()
	{
		string text = _box.Text.Trim();
		if (_phase != Phase.Typing || text.Length == 0)
		{
			return;
		}
		_box.Text = "";
		_messages.Add(new Message { Text = text });
		_stick = true;
		SetPhase(Phase.Waiting);
		Game1.playSound("coin");
		Submitted?.Invoke(text);
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_closeButton.Contains(x, y) || (_endButton.Contains(x, y) && _phase != Phase.Waiting))
		{
			Game1.playSound("bigDeSelect");
			exitThisMenu(playSound: false);
			return;
		}
		if (_sendButton.Contains(x, y))
		{
			Submit();
			return;
		}
		if (Revealing is Message m && _transcript.Contains(x, y))
		{
			if (_shown < m.Text.Length)
			{
				_shown = m.Text.Length;
			}
			else
			{
				NextLine();
			}
			return;
		}
		if (_phase == Phase.Typing && _box.Bounds.Contains(x, y))
		{
			_box.Focus();
		}
	}

	public override void receiveKeyPress(Keys key)
	{
		if (key == Keys.Escape)
		{
			exitThisMenu();
			return;
		}
		if (_phase == Phase.Revealing && (key == Keys.Enter || key == Keys.Space))
		{
			if (Revealing is Message m && _shown < m.Text.Length)
			{
				_shown = m.Text.Length;
			}
			else
			{
				NextLine();
			}
			return;
		}
		if (_phase != Phase.Typing)
		{
			return;
		}
		if (key == Keys.Enter)
		{
			KeyboardState s = Keyboard.GetState();
			if (s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl))
			{
				Submit();
			}
			else
			{
				_box.NewLine();
			}
		}
		else if (key == Keys.Back)
		{
			_box.DeleteChar();
			_backspaceHeld = true;
			_backspaceMs = 0;
		}
	}

	public override void receiveGamePadButton(Buttons button)
	{
		if (button == Buttons.B)
		{
			exitThisMenu();
		}
	}

	public override void receiveScrollWheelAction(int direction)
	{
		_scroll = Math.Clamp(_scroll - Math.Sign(direction) * 60, 0, _maxScroll);
		_stick = _scroll >= _maxScroll;
	}

	public override void update(GameTime time)
	{
		base.update(time);
		double ms = time.ElapsedGameTime.TotalMilliseconds;
		_dotMs += ms;
		if (_dotMs >= 400)
		{
			_dotMs = 0;
			_dots = (_dots + 1) % 4;
		}
		if (Revealing is Message m)
		{
			if (_shown < m.Text.Length)
			{
				_shown = Math.Min(m.Text.Length, _shown + (float)(ms / MsPerChar));
			}
			else
			{
				_holdMs += ms;
				if (_holdMs >= Math.Clamp(900 + m.Text.Length * 30, 1200, 3500))
				{
					NextLine();
				}
			}
		}
		if (_phase == Phase.Typing && Game1.keyboardDispatcher.Subscriber == null)
		{
			_box.Focus();
		}
		if (_backspaceHeld && Keyboard.GetState().IsKeyDown(Keys.Back) && _phase == Phase.Typing)
		{
			_backspaceMs += ms;
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

	protected override void cleanupBeforeExit()
	{
		_box.Blur();
		base.cleanupBeforeExit();
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.4f);
		AisvTheme.Window(b, new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height));
		AisvTheme.Fill(b, _page, AisvTheme.Page);
		DrawHeader(b);
		DrawTranscript(b);
		DrawCompose(b);
		AisvTheme.CloseButton(b, _closeButton, AisvTheme.Hover(_closeButton));
		drawMouse(b);
	}

	private void DrawHeader(SpriteBatch b)
	{
		AisvTheme.Fill(b, _header, AisvTheme.PageDeep);
		AisvTheme.HLine(b, _header.X, _header.Bottom - 2, _header.Width, AisvTheme.Divider);
		NPC? speaking = Revealing?.Npc;
		int x = _header.X + 20;
		foreach (NPC npc in _members)
		{
			Rectangle r = new Rectangle(x, _header.Y + 16, AvatarSize, AvatarSize);
			if (npc == speaking)
			{
				AisvTheme.FillRounded(b, AisvTheme.Inflate(r, 4), AisvTheme.Accent, 10);
			}
			AisvTheme.Avatar(b, r, npc, _display(npc.Name));
			x += AvatarSize + 12;
		}
		string title = Tr.Get("group.windowTitle", "{{names}}", ("names", string.Join(MapGroupChat.Cjk ? "、" : ", ", _members.Select(n => _display(n.Name)))));
		AisvTheme.Bold(b, AisvTheme.Truncate(title, _closeButton.X - x - 40), new Vector2(x + 12, _header.Y + 18), AisvTheme.Text);
		string sub = _repliesLeft > 0 ? Tr.Get("group.repliesLeft", "还能说 {{count}} 句", ("count", _repliesLeft)) : Tr.Get("group.wrapUp", "聊得差不多了");
		AisvTheme.DrawText(b, sub, new Vector2(x + 12, _header.Y + 48), AisvTheme.TextFaint, 0.75f);
	}

	private void DrawTranscript(SpriteBatch b)
	{
		int bubbleMax = Math.Max(240, (int)(_transcript.Width * 0.62f));
		int lh = AisvTheme.LineHeight();
		List<(Message M, string Wrapped, int H)> rows = new List<(Message, string, int)>();
		int total = 0;
		foreach (Message m in _messages)
		{
			string text = m == Revealing ? m.Text.Substring(0, Math.Min(m.Text.Length, (int)_shown)) : m.Text;
			string wrapped = AisvTheme.Wrap(text.Length == 0 ? " " : text, bubbleMax - 32);
			int h = wrapped.Split('\n').Length * lh + 24 + (m.Mine ? 0 : 22);
			rows.Add((m, wrapped, h));
			total += h + 14;
		}
		if (_phase == Phase.Waiting)
		{
			total += 54;
		}
		_maxScroll = Math.Max(0, total - _transcript.Height);
		if (_stick)
		{
			_scroll = _maxScroll;
		}
		AisvTheme.BeginClip(b, _transcript);
		int y = _transcript.Y - _scroll;
		foreach ((Message m, string wrapped, int h) in rows)
		{
			Vector2 size = Game1.smallFont.MeasureString(wrapped);
			int w = Math.Max(80, (int)size.X + 32);
			if (m.Mine)
			{
				Rectangle r = new Rectangle(_transcript.Right - 16 - w, y, w, h);
				AisvTheme.Bordered(b, r, new Color(214, 236, 190), new Color(139, 186, 102), 12);
				AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 12), AisvTheme.Text);
			}
			else
			{
				Rectangle avatar = new Rectangle(_transcript.X + 4, y + 4, 44, 44);
				AisvTheme.Avatar(b, avatar, m.Npc, _display(m.Npc!.Name));
				AisvTheme.DrawText(b, _display(m.Npc.Name), new Vector2(avatar.Right + 12, y), AisvTheme.TextMuted, 0.7f);
				Rectangle r = new Rectangle(avatar.Right + 10, y + 22, w, h - 22);
				AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 12);
				AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 12), AisvTheme.Text);
			}
			y += h + 14;
		}
		if (_phase == Phase.Waiting)
		{
			Rectangle r = new Rectangle(_transcript.X + 58, y, 84, 40);
			AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 12);
			for (int i = 0; i < 3; i++)
			{
				AisvTheme.FillRounded(b, new Rectangle(r.X + 18 + i * 18, r.Center.Y - 5, 10, 10), i < _dots ? AisvTheme.Accent : AisvTheme.CardBorder, 5);
			}
		}
		AisvTheme.EndClip(b);
		if (_maxScroll > 0)
		{
			AisvTheme.ScrollTrack(b, new Rectangle(_transcript.Right - 6, _transcript.Y + 4, 6, _transcript.Height - 8), _scroll, _maxScroll);
		}
	}

	private void DrawCompose(SpriteBatch b)
	{
		AisvTheme.HLine(b, _page.X, _box.Bounds.Y - 30, _page.Width, AisvTheme.Divider);
		string hint = _phase switch
		{
			Phase.Revealing => Tr.Get("group.revealHint", "点一下或按回车看下一句"),
			Phase.Waiting => Tr.Get("group.waiting", "大家在想怎么回你……"),
			Phase.Done => Tr.Get("group.done", "聊完了，可以关掉窗口"),
			_ => Tr.Get("group.yourTurn", "轮到你说了")
		};
		AisvTheme.DrawText(b, hint, new Vector2(_box.Bounds.X, _box.Bounds.Y - 26), AisvTheme.TextFaint, 0.75f);
		_box.Locked = _phase != Phase.Typing;
		_box.Draw(b);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		AisvTheme.Button(b, _endButton, Tr.Get("group.end", "结束"), hover: _endButton.Contains(mx, my), disabled: _phase == Phase.Waiting);
		AisvTheme.Button(b, _sendButton, Tr.Get("reply.send", "发送"), primary: true, hover: _sendButton.Contains(mx, my), disabled: _phase != Phase.Typing || _box.Text.Trim().Length == 0);
	}
}
