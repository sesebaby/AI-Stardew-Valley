using System;
using System.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Outings;
using AISV.Game.Outings;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Dialogue;

/// <summary>
/// 回复框：上面是村民刚说的话（带表情立绘），下面写你的回应，可以附一个"想一起做的事"，可以勾"保密"（不进八卦）。
/// 发出去以后显示"在想……"，可以取消；失败时可以改一改或重发。
/// </summary>
public sealed class ReplyMenu : IClickableMenu
{
	private static readonly TimeSpan CancelGrace = TimeSpan.FromMilliseconds(600);

	private const int MaxReplyChars = 300;

	private const int MaxActionChars = 200;

	private readonly NPC _npc;

	private readonly string _displayName;

	private readonly bool _multiTurn;

	private readonly int _hearts;

	private readonly TextArea _reply = new TextArea(MaxReplyChars) { Placeholder = Tr.Get("reply.placeholder", "想说什么？Ctrl+回车发送") };

	private readonly TextArea _action = new TextArea(MaxActionChars) { Placeholder = Tr.Get("reply.actionPlaceholder", "比如：一起去海边走走（可以不填）") };

	private readonly Stopwatch _processingClock = new Stopwatch();

	private TextArea _active;

	private bool _backspaceHeld;

	private double _backspaceTimer;

	private int _lineScroll;

	private bool _keepSecret;

	private readonly bool _showAction;

	private readonly string? _inviteLabel;

	private readonly string? _inviteTip;

	private bool _inviting;

	private Rectangle _inviteToggle;

	private readonly ProposalKind? _proposalKind;

	private bool _proposing;

	private Rectangle _proposalToggle;

	private Rectangle _proposalHit;

	private readonly OutingOptions _outings;

	private OutingKind? _outing;

	private Rectangle _inviteOutToggle;

	private Rectangle _inviteOutHit;

	private readonly bool _errands;

	private Rectangle _errandHit;

	private readonly bool _loans;

	private Rectangle _loanHit;

	private bool _pillsAbove;

	private Rectangle _dateToggle;

	private Rectangle _dateHit;

	private bool _processing;

	private bool _cancelled;

	private readonly string _shownLine;

	private string? _failure;

	private string? _failureTitle;

	private Rectangle _panel;

	private Rectangle _lineCard;

	private Rectangle _secretToggle;

	private Rectangle _sendButton;

	private Rectangle _skipButton;

	private Rectangle _closeButton;

	private int _portraitSize = 132;

	private int _labelHeight = 34;

	private int _speakerLeft;

	/// <param name="festival">节日上没有"想一起做的事"，只有内容包提供的邀请开关（<paramref name="inviteLabel"/>，可以没有）。</param>
	/// <param name="proposals">这一轮能不能表白或求婚（同一时间最多只有一种）。</param>
	/// <param name="outings">这一轮能不能约出去、约会。</param>
	/// <param name="errands">能不能从这里托他办事。</param>
	/// <param name="loans">能不能从这里向他借钱（或还钱）。</param>
	public ReplyMenu(NPC npc, string displayName, string line, string? emotion, bool multiTurn, int hearts, bool festival = false, string? inviteLabel = null, string? inviteTip = null, ProposalOptions? proposals = null, OutingOptions? outings = null, bool errands = false, bool loans = false)
	{
		_errands = errands && !festival;
		_loans = loans && !festival;
		_showAction = !festival;
		_proposalKind = festival || proposals == null ? null : proposals.Marriage ? ProposalKind.Marriage : proposals.Dating ? ProposalKind.Dating : null;
		_outings = festival || outings == null ? OutingOptions.None : outings;
		_inviteLabel = inviteLabel;
		_inviteTip = inviteTip;
		_npc = npc;
		_displayName = displayName;
		_multiTurn = multiTurn;
		_hearts = hearts;
		DisplayedLine = line;
		// 卡片不经过游戏的对话解析，模型写的翻页和表情指令画之前整理掉；DisplayedLine 留原文（要写进记录）。
		_shownLine = GameDialogueText.ForDisplay(line, Game1.player?.Name ?? "").Replace("#$b#", " ");
		DisplayedEmotion = emotion;
		_active = _reply;
		Layout();
		_reply.Focus();
	}

	/// <summary>回复框上面显示的那句（多轮时是上一句反应）。</summary>
	public string DisplayedLine { get; }

	public string? DisplayedEmotion { get; }

	/// <summary>玩家打开了节日邀请开关。</summary>
	public bool Inviting => _inviting;

	/// <summary>玩家打开了表白/求婚开关：这句话就是表白或求婚。</summary>
	public ProposalKind? Proposal => _proposing ? _proposalKind : null;

	private string ProposalLabel => _proposalKind == ProposalKind.Marriage ? Tr.Get("reply.proposeMarriage", "求婚") : Tr.Get("reply.proposeDating", "表白");

	/// <summary>玩家打开了约出去 / 约会开关：这句话就是邀约。</summary>
	public OutingKind? Outing => _outing;

	private static string InviteOutLabel => Tr.Get("reply.inviteOut", "约出去");

	private static string DateLabel => Tr.Get("reply.spouseDate", "约会");

	/// <summary>发出回应：文字、想一起做的事（没有为 null）、保密。</summary>
	public event Action<string, string?, bool>? Submitted;

	/// <summary>关掉回复框（配偶对话会留着下次接着聊）。</summary>
	public event Action? Cancelled;

	/// <summary>不回了：这场对话结束，照样加一点好感。</summary>
	public event Action? Skipped;

	/// <summary>不等这次的回应了。</summary>
	public event Action? ProcessingCancelled;

	/// <summary>点了"托办件事"。</summary>
	public event Action? ErrandRequested;

	/// <summary>点了"借钱"。</summary>
	public event Action? LoanRequested;

	private static string LoanLabel => Tr.Get("reply.loan", "借钱");

	private static string ErrandLabel => Tr.Get("reply.errand", "托办件事");

	private bool Locked => _processing || _failure != null;

	/// <summary>挪到回复框右上角的按钮会和告白提示撞上，这时先藏起来。</summary>
	private bool PillsHidden => _pillsAbove && _proposing;

	private void Layout()
	{
		int width = Math.Min(980, Game1.uiViewport.Width - 64);
		int height = Math.Min(620, Game1.uiViewport.Height - 64);
		xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
		yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
		this.width = width;
		this.height = height;
		_panel = new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height);
		// 界面放大或窗口很矮时（比如 150% 缩放）放不下原来的间距，换紧凑排法，不让按钮压住输入框。
		bool compact = height < 560;
		int pad = compact ? 16 : 28;
		int left = _panel.X + pad;
		int inner = width - pad * 2;
		int buttonHeight = compact ? 48 : 56;
		int actionHeight = compact ? 44 : 64;
		_portraitSize = compact ? 96 : 132;
		_labelHeight = compact ? 28 : 34;
		// 爱心排宽 126：这一列至少要放得下它，立绘比它窄时居中。
		int column = Math.Max(_portraitSize, 128) + 18;
		_speakerLeft = left;
		_lineCard = new Rectangle(left + column, _panel.Y + pad + (compact ? 0 : 8), inner - column, compact ? 130 : 170);
		int y = _lineCard.Bottom + (compact ? 28 + _labelHeight : 64);
		int buttonsTop = _panel.Bottom - pad - buttonHeight;
		int replyHeight = compact ? Math.Max(52, buttonsTop - 10 - actionHeight - _labelHeight - y) : Math.Max(90, buttonsTop - 64 - actionHeight - 40 - y);
		_reply.Bounds = new Rectangle(left, y, inner, replyHeight);
		_action.Bounds = new Rectangle(left, _reply.Bounds.Bottom + (compact ? _labelHeight : 40), inner, actionHeight);
		_inviteToggle = new Rectangle(left, _action.Bounds.Y + (_action.Bounds.Height - 28) / 2, 56, 28);
		_secretToggle = new Rectangle(left, buttonsTop + (buttonHeight - 28) / 2, 56, 28);
		_sendButton = new Rectangle(_panel.Right - pad - 150, buttonsTop, 150, buttonHeight);
		_skipButton = new Rectangle(_sendButton.X - 16 - 130, buttonsTop, 130, buttonHeight);
		_closeButton = new Rectangle(_skipButton.X - 16 - 130, buttonsTop, 130, buttonHeight);
		int secretWidth = (int)Game1.smallFont.MeasureString(Tr.Get("reply.keepSecret", "保密")).X;
		_proposalToggle = new Rectangle(_secretToggle.Right + 12 + secretWidth + 32, _secretToggle.Y, 56, 28);
		_proposalHit = new Rectangle(_proposalToggle.X - 4, _proposalToggle.Y - 8, 56 + 12 + (int)Game1.smallFont.MeasureString(ProposalLabel).X + 8, 44);
		int next = _proposalKind != null ? _proposalHit.Right + 28 : _proposalToggle.X;
		(_inviteOutToggle, _inviteOutHit) = ToggleAt(_outings.Invite ? next : -1, InviteOutLabel);
		next = _outings.Invite ? _inviteOutHit.Right + 28 : next;
		(_dateToggle, _dateHit) = ToggleAt(_outings.Date ? next : -1, DateLabel);
		next = _outings.Date ? _dateHit.Right + 28 : next;
		int errandW = _errands ? (int)Game1.smallFont.MeasureString(ErrandLabel).X * 8 / 10 + 32 : 0;
		int loanW = _loans ? (int)Game1.smallFont.MeasureString(LoanLabel).X * 8 / 10 + 32 : 0;
		int pillsW = errandW + loanW + (_errands && _loans ? 10 : 0);
		// 底下一排放不下时挪到回复框右上角。
		bool below = next + pillsW < _closeButton.X - 12;
		_pillsAbove = !below;
		int px = below ? next : _panel.Right - pad - pillsW;
		int py = below ? _secretToggle.Y - 6 : _reply.Bounds.Y - 44;
		_errandHit = _errands ? new Rectangle(px, py, errandW, below ? 40 : 36) : Rectangle.Empty;
		px = _errands ? _errandHit.Right + 10 : px;
		_loanHit = _loans ? new Rectangle(px, py, loanW, below ? 40 : 36) : Rectangle.Empty;
	}

	private (Rectangle Toggle, Rectangle Hit) ToggleAt(int x, string label)
	{
		if (x < 0)
		{
			return (Rectangle.Empty, Rectangle.Empty);
		}
		Rectangle toggle = new Rectangle(x, _secretToggle.Y, 56, 28);
		return (toggle, new Rectangle(toggle.X - 4, toggle.Y - 8, 56 + 12 + (int)Game1.smallFont.MeasureString(label).X + 8, 44));
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
	{
		base.gameWindowSizeChanged(oldBounds, newBounds);
		Layout();
	}

	/// <summary>回应失败：保留写好的话，给出原因，可以重发、改一改或结束。</summary>
	public void ShowFailure(string reason, string? title = null)
	{
		_processing = false;
		_failure = reason ?? "";
		_failureTitle = title;
		_processingClock.Restart();
		_reply.Locked = true;
		_action.Locked = true;
		Game1.playSound("cancel");
	}

	public void CloseMenu() => exitThisMenu(playSound: false);

	/// <summary>游戏对话框顶掉回复框时（显示反应），把键盘还回去。</summary>
	public void ReleaseKeyboard()
	{
		_reply.Blur();
		_action.Blur();
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_failure != null)
		{
			if (_sendButton.Contains(x, y))
			{
				_failure = null;
				Submit();
			}
			else if (_skipButton.Contains(x, y))
			{
				_failure = null;
				Unlock();
				Game1.playSound("smallSelect");
			}
			else if (_closeButton.Contains(x, y))
			{
				Cancel(force: true);
			}
			return;
		}
		if (_processing)
		{
			if (_closeButton.Contains(x, y))
			{
				CancelProcessing();
			}
			return;
		}
		if (_reply.Bounds.Contains(x, y))
		{
			Activate(_reply);
		}
		else if (_showAction && _outing == null && _action.Bounds.Contains(x, y))
		{
			Activate(_action);
		}
		else if (_inviteLabel != null && !_showAction && _action.Bounds.Contains(x, y))
		{
			_inviting = !_inviting;
			Game1.playSound("drumkit6");
		}
		else if (_secretToggle.Contains(x, y))
		{
			_keepSecret = !_keepSecret;
			Game1.playSound("drumkit6");
		}
		else if (_proposalKind != null && _proposalHit.Contains(x, y))
		{
			_proposing = !_proposing;
			if (_proposing)
			{
				_outing = null;
				_action.Locked = false;
			}
			Game1.playSound(_proposing ? "give_gift" : "drumkit6");
		}
		else if (_outings.Invite && _inviteOutHit.Contains(x, y))
		{
			ToggleOuting(OutingKind.Invite);
		}
		else if (_outings.Date && _dateHit.Contains(x, y))
		{
			ToggleOuting(OutingKind.SpouseDate);
		}
		else if (_errandHit.Contains(x, y) && !Locked && !PillsHidden)
		{
			Game1.playSound("bigSelect");
			ErrandRequested?.Invoke();
		}
		else if (_loanHit.Contains(x, y) && !Locked && !PillsHidden)
		{
			Game1.playSound("bigSelect");
			LoanRequested?.Invoke();
		}
		else if (_sendButton.Contains(x, y))
		{
			Submit();
		}
		else if (_skipButton.Contains(x, y))
		{
			Skip();
		}
		else if (_closeButton.Contains(x, y))
		{
			Cancel();
		}
	}

	/// <summary>邀约开关和表白开关同时只开一个；邀约时“想一起做点什么”不用填。</summary>
	private void ToggleOuting(OutingKind kind)
	{
		_outing = _outing == kind ? null : kind;
		_action.Locked = _outing != null;
		if (_outing != null)
		{
			_proposing = false;
			_action.Blur();
			if (_active == _action)
			{
				Activate(_reply);
			}
		}
		Game1.playSound(_outing != null ? "give_gift" : "drumkit6");
	}

	private void DrawOutingToggle(SpriteBatch b, bool shown, Rectangle toggle, OutingKind kind, string label, int labelY)
	{
		if (!shown)
		{
			return;
		}
		bool on = _outing == kind;
		AisvTheme.Toggle(b, toggle, on);
		AisvTheme.DrawText(b, label, new Vector2(toggle.Right + 12, toggle.Y), on ? AisvTheme.Accent : AisvTheme.TextMuted);
		if (on)
		{
			string tip = kind == OutingKind.SpouseDate ? Tr.Get("reply.spouseDateTip", "这句话会当作约会邀请，去不去由对方决定") : Tr.Get("reply.inviteOutTip", "这句话会当作邀约，去不去、去哪由对方决定");
			Vector2 size = Game1.smallFont.MeasureString(tip) * 0.8f;
			b.DrawString(Game1.smallFont, tip, new Vector2(_reply.Bounds.Right - size.X - 4, labelY + 4), AisvTheme.Accent, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 1f);
		}
	}

	public override void receiveKeyPress(Keys key)
	{
		// 输入框接管键盘时，别让 E 之类的游戏快捷键关掉菜单。
		if (key == Keys.Enter && !_processing)
		{
			KeyboardState keys = Keyboard.GetState();
			if (_failure != null || keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
			{
				_failure = null;
				Submit();
			}
			else
			{
				_active.NewLine();
			}
			return;
		}
		if (key == Keys.Back && !Locked)
		{
			_active.DeleteChar();
			_backspaceHeld = true;
			_backspaceTimer = 0;
			return;
		}
		if (key == Keys.Tab && !Locked && _showAction && _outing == null)
		{
			Activate(_active == _reply ? _action : _reply);
			Game1.playSound("smallSelect");
			return;
		}
		if (key == Keys.Escape)
		{
			if (_processing)
			{
				CancelProcessing();
			}
			else
			{
				Cancel(force: _failure != null);
			}
		}
	}

	/// <summary>手柄：B 等于 ESC（不等了 / 先不说了）。屏幕键盘还没有。</summary>
	public override void receiveGamePadButton(Buttons button)
	{
		if (button != Buttons.B)
		{
			return;
		}
		if (_processing)
		{
			CancelProcessing();
		}
		else
		{
			Cancel(force: _failure != null);
		}
	}

	public override void receiveScrollWheelAction(int direction)
	{
		_lineScroll = Math.Max(0, _lineScroll + (direction < 0 ? 1 : -1));
	}

	public override void update(GameTime time)
	{
		base.update(time);
		if (!Locked && Game1.keyboardDispatcher.Subscriber == null)
		{
			_active.Focus();
		}
		// 长按退格：停 400 毫秒后连删。
		if (_backspaceHeld && Keyboard.GetState().IsKeyDown(Keys.Back) && !Locked)
		{
			_backspaceTimer += time.ElapsedGameTime.TotalMilliseconds;
			if (_backspaceTimer >= 400)
			{
				_backspaceTimer -= 50;
				_active.DeleteChar();
			}
		}
		else
		{
			_backspaceHeld = false;
		}
	}

	private void Activate(TextArea area)
	{
		(area == _reply ? _action : _reply).Blur();
		_active = area;
		area.Focus();
	}

	private void Submit()
	{
		if (_processing)
		{
			return;
		}
		string text = _reply.Text.Trim();
		if (text.Length == 0)
		{
			return;
		}
		Game1.playSound("coin");
		_failure = null;
		_processing = true;
		_processingClock.Restart();
		_reply.Locked = true;
		_action.Locked = true;
		string? action = !_showAction || _outing != null || string.IsNullOrWhiteSpace(_action.Text) ? null : _action.Text.Trim();
		Submitted?.Invoke(text, action, _keepSecret);
	}

	private void Unlock()
	{
		_reply.Locked = false;
		_action.Locked = _outing != null;
		_active.Focus();
	}

	private void CancelProcessing()
	{
		if (_processing && _processingClock.Elapsed >= CancelGrace)
		{
			ProcessingCancelled?.Invoke();
			ShowFailure(Tr.Get("reply.cancelled", "不等这次的回应了。"), Tr.Get("reply.cancelledTitle", "已取消"));
		}
	}

	private void Cancel(bool force = false)
	{
		if ((_processing && !force) || _cancelled)
		{
			return;
		}
		// 同一次按键可能先后走手柄和键盘两条路：刚显示"已取消"就别接着把菜单关了。
		if (force && _processingClock.IsRunning && _processingClock.Elapsed < TimeSpan.FromMilliseconds(200))
		{
			return;
		}
		_cancelled = true;
		Game1.playSound("bigDeSelect");
		Cancelled?.Invoke();
		exitThisMenu();
	}

	private void Skip()
	{
		if (_processing)
		{
			return;
		}
		Game1.playSound("coin");
		Skipped?.Invoke();
		exitThisMenu();
	}

	protected override void cleanupBeforeExit()
	{
		_reply.Blur();
		_action.Blur();
		base.cleanupBeforeExit();
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
		AisvTheme.Window(b, _panel);
		DrawSpeaker(b);
		DrawLine(b);
		int labelY = _reply.Bounds.Y - _labelHeight;
		AisvTheme.DrawText(b, Tr.Get("reply.label", "你的回应"), new Vector2(_reply.Bounds.X + 4, labelY), AisvTheme.TextMuted);
		_reply.Draw(b);
		DrawCounter(b, _reply, MaxReplyChars);
		if (_showAction)
		{
			string actionLabel = _outing != null ? Tr.Get("reply.actionDuringOuting", "约出去的时候这一栏不用填") : Tr.Get("reply.actionLabel", "想一起做点什么（可选）");
			AisvTheme.DrawText(b, actionLabel, new Vector2(_action.Bounds.X + 4, _action.Bounds.Y - _labelHeight), _outing != null ? AisvTheme.TextFaint : AisvTheme.TextMuted);
			_action.Draw(b);
			DrawCounter(b, _action, MaxActionChars);
		}
		else if (_inviteLabel != null)
		{
			AisvTheme.Toggle(b, _inviteToggle, _inviting);
			AisvTheme.DrawText(b, _inviteLabel, new Vector2(_inviteToggle.Right + 12, _inviteToggle.Y), AisvTheme.Text);
			if (!string.IsNullOrWhiteSpace(_inviteTip))
			{
				AisvTheme.DrawText(b, _inviteTip, new Vector2(_inviteToggle.Right + 12 + Game1.smallFont.MeasureString(_inviteLabel).X + 20, _inviteToggle.Y), AisvTheme.TextMuted);
			}
		}
		AisvTheme.Toggle(b, _secretToggle, _keepSecret);
		AisvTheme.DrawText(b, Tr.Get("reply.keepSecret", "保密"), new Vector2(_secretToggle.Right + 12, _secretToggle.Y), AisvTheme.TextMuted);
		if (_proposalKind != null)
		{
			AisvTheme.Toggle(b, _proposalToggle, _proposing);
			AisvTheme.DrawText(b, ProposalLabel, new Vector2(_proposalToggle.Right + 12, _proposalToggle.Y), _proposing ? AisvTheme.Accent : AisvTheme.TextMuted);
			if (_proposing)
			{
				string tip = _proposalKind == ProposalKind.Marriage ? Tr.Get("reply.proposeMarriageTip", "这句话会当作求婚说出口，答不答应由对方决定") : Tr.Get("reply.proposeDatingTip", "这句话会当作表白说出口，答不答应由对方决定");
				Vector2 size = Game1.smallFont.MeasureString(tip) * 0.8f;
				b.DrawString(Game1.smallFont, tip, new Vector2(_reply.Bounds.Right - size.X - 4, labelY + 4), AisvTheme.Accent, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 1f);
			}
		}
		DrawOutingToggle(b, _outings.Invite, _inviteOutToggle, OutingKind.Invite, InviteOutLabel, labelY);
		DrawOutingToggle(b, _outings.Date, _dateToggle, OutingKind.SpouseDate, DateLabel, labelY);
		int mx = Game1.getMouseX(), my = Game1.getMouseY();
		bool pillsHidden = PillsHidden;
		if (_errandHit != Rectangle.Empty && !Locked && !pillsHidden)
		{
			bool hover = _errandHit.Contains(mx, my);
			AisvTheme.Bordered(b, _errandHit, hover ? AisvTheme.GoldSoft : AisvTheme.Card, hover ? AisvTheme.Gold : AisvTheme.CardBorder, 20);
			AisvTheme.TextCentered(b, ErrandLabel, _errandHit, hover ? AisvTheme.Gold : AisvTheme.TextMuted, 0.8f);
		}
		if (_loanHit != Rectangle.Empty && !Locked && !pillsHidden)
		{
			bool hover = _loanHit.Contains(mx, my);
			AisvTheme.Bordered(b, _loanHit, hover ? AisvTheme.GoldSoft : AisvTheme.Card, hover ? AisvTheme.Gold : AisvTheme.CardBorder, 20);
			AisvTheme.TextCentered(b, LoanLabel, _loanHit, hover ? AisvTheme.Gold : AisvTheme.TextMuted, 0.8f);
		}
		if (_failure != null)
		{
			DrawFailure(b);
			AisvTheme.Button(b, _closeButton, Tr.Get("reply.end", "结束"), hover: _closeButton.Contains(mx, my));
			AisvTheme.Button(b, _skipButton, Tr.Get("reply.edit", "改一改"), hover: _skipButton.Contains(mx, my));
			AisvTheme.Button(b, _sendButton, Tr.Get("reply.retry", "重发"), primary: true, hover: _sendButton.Contains(mx, my));
		}
		else if (_processing)
		{
			string dots = new string('.', (int)(_processingClock.Elapsed.TotalSeconds * 2) % 4);
			AisvTheme.DrawText(b, Tr.Get("reply.thinking", "{{npc}}在想", ("npc", _displayName)) + dots, new Vector2(_closeButton.Right + 20, _sendButton.Y + 14), AisvTheme.Accent);
			AisvTheme.Button(b, _closeButton, Tr.Get("reply.stopWaiting", "不等了"), hover: _closeButton.Contains(mx, my), disabled: _processingClock.Elapsed < CancelGrace);
		}
		else
		{
			AisvTheme.Button(b, _closeButton, Tr.Get("reply.close", "先不说了"), hover: _closeButton.Contains(mx, my));
			AisvTheme.Button(b, _skipButton, _multiTurn ? Tr.Get("reply.finish", "聊到这") : Tr.Get("reply.skip", "不回了"), hover: _skipButton.Contains(mx, my));
			AisvTheme.Button(b, _sendButton, Tr.Get("reply.send", "发送"), primary: true, hover: _sendButton.Contains(mx, my), disabled: _reply.Text.Trim().Length == 0);
		}
		drawMouse(b);
	}

	/// <summary>输入框右下角的字数，写满时变红。</summary>
	private static void DrawCounter(SpriteBatch b, TextArea area, int max)
	{
		const float scale = 0.7f;
		string text = area.Text.Length + "/" + max;
		Vector2 size = Game1.smallFont.MeasureString(text) * scale;
		b.DrawString(Game1.smallFont, text, new Vector2(area.Bounds.Right - size.X - 12f, area.Bounds.Bottom - size.Y - 6f), area.Text.Length >= max ? AisvTheme.Danger : AisvTheme.TextFaint, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
	}

	private void DrawSpeaker(SpriteBatch b)
	{
		Rectangle box = new Rectangle(_speakerLeft + Math.Max(0, 128 - _portraitSize) / 2, _lineCard.Y, _portraitSize, _portraitSize);
		AisvTheme.DrawCard(b, box);
		Texture2D? portrait = PortraitOverrides.For(_npc.Name, DisplayedEmotion) ?? SafePortrait();
		if (portrait != null)
		{
			int tiles = portrait.Width / 64 * (portrait.Height / 64);
			int index = Emotion.PortraitIndex(Emotion.PortraitCommandFor(_npc.Name, DisplayedEmotion, tiles));
			index = index < tiles ? index : 0;
			b.Draw(portrait, new Rectangle(box.X + 2, box.Y + 2, box.Width - 4, box.Height - 4), Game1.getSourceRectForStandardTileSheet(portrait, index, 64, 64), Color.White);
		}
		AisvTheme.TextCentered(b, _displayName, new Rectangle(box.X - 10, box.Bottom + 2, box.Width + 20, 32), AisvTheme.Text);
		// 每排 7 颗，超过 7 心时再画一排（最多 14 心）。
		AisvTheme.Hearts(b, new Vector2(_speakerLeft + 2, box.Bottom + 34), Math.Min(_hearts, 7), 7);
		if (_hearts > 7)
		{
			AisvTheme.Hearts(b, new Vector2(_speakerLeft + 2, box.Bottom + 48), Math.Min(_hearts - 7, 7), 7);
		}
	}

	private void DrawLine(SpriteBatch b)
	{
		AisvTheme.DrawCard(b, _lineCard);
		string text = AisvTheme.Wrap(_shownLine, _lineCard.Width - 32);
		string[] lines = text.Split('\n');
		int lh = AisvTheme.LineHeight();
		int max = Math.Max(1, (_lineCard.Height - 24) / lh);
		// 放不下时用滚轮看后面的。
		_lineScroll = Math.Clamp(_lineScroll, 0, Math.Max(0, lines.Length - max));
		for (int i = 0; i < max && i + _lineScroll < lines.Length; i++)
		{
			b.DrawString(Game1.smallFont, lines[i + _lineScroll], new Vector2(_lineCard.X + 16, _lineCard.Y + 12 + i * lh), AisvTheme.Text);
		}
		if (lines.Length > max)
		{
			Rectangle track = new Rectangle(_lineCard.Right - 12, _lineCard.Y + 12, 4, _lineCard.Height - 24);
			AisvTheme.Fill(b, track, AisvTheme.Divider);
			int thumb = Math.Max(16, track.Height * max / lines.Length);
			int top = track.Y + (track.Height - thumb) * _lineScroll / Math.Max(1, lines.Length - max);
			AisvTheme.Fill(b, new Rectangle(track.X, top, track.Width, thumb), AisvTheme.TextFaint);
		}
	}

	private void DrawFailure(SpriteBatch b)
	{
		// 盖住两个输入框：原因可能有好几行（比如网络错误），只盖回复框放不下。
		Rectangle r = new Rectangle(_reply.Bounds.X, _reply.Bounds.Y, _reply.Bounds.Width, _action.Bounds.Bottom - _reply.Bounds.Y);
		AisvTheme.Bordered(b, r, new Color(255, 246, 240), AisvTheme.Warn);
		string title = _failureTitle ?? Tr.Get("reply.failedTitle", "这次没有回应");
		AisvTheme.DrawText(b, title, new Vector2(r.X + 16, r.Y + 10), AisvTheme.Danger);
		string[] lines = AisvTheme.Wrap((_failure ?? "") + "\n" + Tr.Get("reply.kept", "你写的话还在，可以重发。"), r.Width - 32).Split('\n');
		int lh = AisvTheme.LineHeight();
		int top = r.Y + 10 + lh + 4;
		int max = Math.Max(1, (r.Bottom - 8 - top) / lh);
		for (int i = 0; i < max && i < lines.Length; i++)
		{
			b.DrawString(Game1.smallFont, lines[i], new Vector2(r.X + 16, top + i * lh), AisvTheme.Text);
		}
	}

	private Texture2D? SafePortrait()
	{
		try
		{
			return _npc.Portrait is { IsDisposed: false } p ? p : null;
		}
		catch
		{
			return null;
		}
	}
}
