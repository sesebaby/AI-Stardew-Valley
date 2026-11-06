using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.Game.Group;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>
/// 居民群：顶栏是群名、人数、今天还能发几条和几位成员头像；中间是聊天记录（村民在左、你在右，换天插一条日期，有人在输入时底下提示）；
/// 底部是输入框和发送（回车发送，Shift+回车换行）。
/// </summary>
internal sealed class ResidentsPage : ShellPage
{
	private const int AvatarSize = 44;

	private readonly TextArea _box = new TextArea(ResidentsGroup.MaxPostChars) { Placeholder = Tr.Get("residents.placeholder", "在群里说点什么……回车发送") };

	private readonly Scroller _scroll = new Scroller();

	private IReadOnlyList<NPC> _members = Array.Empty<NPC>();

	private int _seenVersion = -1;

	private bool _stick = true;

	private string _status = "";

	private bool _backspaceHeld;

	private double _backspaceMs;

	private int _offerVersion = -1;

	public ResidentsPage(ShellContext ctx) : base(ctx)
	{
	}

	private ResidentsService? Service => Ctx.Residents;

	private static int _drawnTick = -100;

	public static bool Viewing => Game1.ticks - _drawnTick <= 2 && Game1.activeClickableMenu is ShellMenu;

	public override bool CapturesKeys => _box.Selected;

	public override void Reload()
	{
		_members = Service?.Members() ?? Array.Empty<NPC>();
	}

	public override void Closed()
	{
		_box.Blur();
	}

	public override bool KeyPress(Keys key)
	{
		if (!_box.Selected)
		{
			return _scroll.PageKeys(key, 300);
		}
		switch (key)
		{
		case Keys.Enter:
		{
			KeyboardState s = Keyboard.GetState();
			if (s.IsKeyDown(Keys.LeftShift) || s.IsKeyDown(Keys.RightShift))
			{
				_box.NewLine();
			}
			else
			{
				Send();
			}
			return true;
		}
		case Keys.Back:
			_box.DeleteChar();
			_backspaceHeld = true;
			_backspaceMs = 0;
			return true;
		case Keys.Escape:
			_box.Blur();
			return true;
		default:
			return false;
		}
	}

	public override void Scroll(int direction)
	{
		_scroll.Wheel(direction);
		_stick = _scroll.Offset >= _scroll.Max;
	}

	private void Send()
	{
		ResidentsService? service = Service;
		if (service == null)
		{
			return;
		}
		ResidentsBlock block = service.Send(_box.Text);
		if (block == ResidentsBlock.None)
		{
			_box.Text = "";
			_stick = true;
			_status = "";
			Game1.playSound("coin");
			return;
		}
		_status = BlockText(block, service);
		Game1.playSound("cancel");
	}

	private static string BlockText(ResidentsBlock block, ResidentsService service) => block switch
	{
		ResidentsBlock.Empty => Tr.Get("residents.empty", "先写点什么"),
		ResidentsBlock.TooLong => Tr.Get("residents.tooLong", "太长了，最多 {{max}} 个字", ("max", ResidentsGroup.MaxPostChars)),
		ResidentsBlock.Busy => Tr.Get("residents.busy", "等大家回完再发"),
		ResidentsBlock.NoPostsLeft => Tr.Get("residents.noPosts", "今天发了 {{count}} 条，明天再聊", ("count", service.DailyLimit)),
		ResidentsBlock.AiOff => Tr.Get("residents.aiOff", "AI 现在用不了"),
		ResidentsBlock.TooFewMembers => Tr.Get("residents.fewMembers", "群里的人还太少，多认识几位村民吧"),
		_ => ""
	};

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_drawnTick = Game1.ticks;
		Tick(Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16.7);
		ResidentsService? service = Service;
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 72);
		DrawHeader(b, header, service);
		int composeH = 28 + AisvTheme.LineHeight() * 2 + 44;
		Rectangle compose = new Rectangle(area.X, area.Bottom - composeH, area.Width, composeH);
		Rectangle list = new Rectangle(area.X + 12, header.Bottom + 4, area.Width - 24, compose.Y - header.Bottom - 8);
		DrawMessages(b, list, service, hits);
		DrawCompose(b, compose, hits, service);
	}

	private void Tick(double ms)
	{
		if (_box.Selected && Game1.keyboardDispatcher.Subscriber == null)
		{
			_box.Focus();
		}
		if (_backspaceHeld && Keyboard.GetState().IsKeyDown(Keys.Back) && _box.Selected)
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
		if (Service is ResidentsService s && s.Version != _seenVersion)
		{
			_seenVersion = s.Version;
			_stick = true;
			if (s.LastPostFailed)
			{
				s.LastPostFailed = false;
				_status = Tr.Get("residents.noReply", "这次没人回，过会儿再试试");
			}
		}
	}

	private static readonly Color InviteColor = new Color(232, 58, 36);

	private void DrawHeader(SpriteBatch b, Rectangle r, ResidentsService? service)
	{
		AisvTheme.Fill(b, r, AisvTheme.PageDeep);
		AisvTheme.HLine(b, r.X, r.Bottom - 2, r.Width, AisvTheme.Divider);
		string title = Tr.Get("residents.title", "居民群");
		AisvTheme.Bold(b, title, new Vector2(r.X + 20, r.Y + 12), AisvTheme.Text, 1.05f);
		int titleW = (int)AisvTheme.Width(title, 1.05f);
		string invite = Tr.Get("residents.invite", "本群群号：1464326733，欢迎鹈鹕镇居民加入");
		Vector2 inviteSize = invite.Length == 0 ? Vector2.Zero : Game1.smallFont.MeasureString(invite) * 0.9f;
		int inviteW = invite.Length == 0 ? 0 : (int)inviteSize.X + 24;
		if (inviteW > 0)
		{
			Rectangle chip = new Rectangle(r.X + 20 + titleW + 14, r.Y + 8, inviteW, (int)inviteSize.Y + 8);
			AisvTheme.FillRounded(b, chip, InviteColor, chip.Height / 2);
			AisvTheme.Bold(b, invite, new Vector2(chip.X + 12, chip.Y + 4), Color.White, 0.9f);
		}
		string sub = service == null ? "" : Tr.Get("residents.sub", "{{count}} 位居民 · 今天还能发 {{left}} 条", ("count", _members.Count), ("left", service.PostsLeft));
		AisvTheme.DrawText(b, sub, new Vector2(r.X + 20, r.Y + 44), AisvTheme.TextFaint, 0.72f);
		int slots = Math.Max(0, (r.Width - titleW - (inviteW > 0 ? inviteW + 14 : 0) - 120) / (AvatarSize + 6));
		slots = Math.Min(slots, 10);
		int x = r.Right - 16 - AvatarSize;
		int shown = Math.Min(_members.Count, slots);
		for (int i = 0; i < shown; i++)
		{
			Rectangle a = new Rectangle(x - i * (AvatarSize + 6), r.Y + 14, AvatarSize, AvatarSize);
			bool more = i == slots - 1 && _members.Count > slots;
			if (more)
			{
				AisvTheme.Glyph(b, a, AisvTheme.AccentSoft, "+" + (_members.Count - slots + 1));
			}
			else
			{
				NPC npc = _members[i];
				AisvTheme.Avatar(b, a, npc, Ctx.Display(npc.Name));
			}
		}
	}

	private void DrawMessages(SpriteBatch b, Rectangle view, ResidentsService? service, Hits hits)
	{
		IReadOnlyList<ResidentsMessage> messages = service?.Messages ?? Array.Empty<ResidentsMessage>();
		int lh = AisvTheme.LineHeight();
		int bubbleMax = Math.Max(240, (int)(view.Width * 0.62f));
		AisvTheme.BeginClip(b, view);
		int y = view.Y + 12 - _scroll.Offset;
		if (messages.Count == 0)
		{
			AisvTheme.TextCentered(b, Tr.Get("residents.emptyThread", "群里还没人说话，打个招呼吧"), new Rectangle(view.X, view.Y + 40, view.Width, 40), AisvTheme.TextFaint, 0.85f);
		}
		string lastDay = "";
		foreach (ResidentsMessage m in messages)
		{
			string day = $"{m.Year}-{m.Season}-{m.Day}";
			if (day != lastDay)
			{
				lastDay = day;
				string label = Tr.Get("residents.date", "第 {{year}} 年{{season}} {{day}} 日", ("year", m.Year), ("season", HistoryFormatter.SeasonName(m.Season)), ("day", m.Day));
				int cw = AisvTheme.ChipWidth(label, 0.7f);
				AisvTheme.Chip(b, new Vector2(view.Center.X - cw / 2, y), label, AisvTheme.Divider, AisvTheme.TextMuted, 0.7f);
				y += 40;
			}
			string wrapped = AisvTheme.Wrap(m.Text, bubbleMax - 32);
			Vector2 size = Game1.smallFont.MeasureString(wrapped);
			int w = Math.Max(72, (int)size.X + 32);
			int h = wrapped.Split('\n').Length * lh + 22;
			if (m.IsPlayer)
			{
				Rectangle r = new Rectangle(view.Right - 20 - w, y, w, h);
				AisvTheme.Bordered(b, r, new Color(214, 236, 190), new Color(139, 186, 102), 12);
				AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 11), AisvTheme.Text);
				y += h + 14;
			}
			else
			{
				NPC? npc = Game1.getCharacterFromName(m.Speaker);
				Rectangle avatar = new Rectangle(view.X + 6, y, AvatarSize, AvatarSize);
				AisvTheme.Avatar(b, avatar, npc, Ctx.Display(m.Speaker));
				AisvTheme.DrawText(b, Ctx.Display(m.Speaker) + "  " + Clock(m.Time), new Vector2(avatar.Right + 12, y - 2), AisvTheme.TextFaint, 0.68f);
				Rectangle r = new Rectangle(avatar.Right + 10, y + 22, w, h);
				AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 12);
				AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 11), AisvTheme.Text);
				y += h + 22 + 14;
			}
		}
		if (service?.TypingSpeaker == null)
		{
			int offerVersion = Ctx.Offers?.Version ?? 0;
			if (offerVersion != _offerVersion)
			{
				_offerVersion = offerVersion;
				_stick = true;
			}
			int cardBottom = OfferCard.Draw(b, Ctx, OfferDesk.GroupChannel, view.X + AvatarSize + 16, y, bubbleMax, hits, view, said => _status = said);
			if (cardBottom > y)
			{
				y = cardBottom + 14;
			}
		}
		if (service?.TypingSpeaker is string typing)
		{
			string text = typing.Length == 0 ? Tr.Get("residents.thinking", "大家在看消息……") : Tr.Get("residents.typing", "{{name}}正在输入……", ("name", Ctx.Display(typing)));
			AisvTheme.DrawText(b, text, new Vector2(view.X + 16, y), AisvTheme.TextFaint, 0.75f);
			y += 32;
		}
		AisvTheme.EndClip(b);
		int bottom = y + 8;
		_scroll.Finish(b, view, bottom);
		if (_stick)
		{
			_scroll.By(int.MaxValue / 2);
		}
	}

	private void DrawCompose(SpriteBatch b, Rectangle r, Hits hits, ResidentsService? service)
	{
		AisvTheme.HLine(b, r.X, r.Y, r.Width, AisvTheme.Divider);
		if (_status.Length > 0)
		{
			AisvTheme.DrawText(b, _status, new Vector2(r.X + 20, r.Y + 8), AisvTheme.Warn, 0.72f);
		}
		const int bw = 112;
		Rectangle send = new Rectangle(r.Right - 20 - bw, r.Bottom - 16 - 48, bw, 48);
		_box.Bounds = new Rectangle(r.X + 20, r.Y + 34, send.X - 12 - (r.X + 20), r.Bottom - 16 - (r.Y + 34));
		_box.Draw(b);
		string count = _box.Text.Length + "/" + ResidentsGroup.MaxPostChars;
		Vector2 cs = Game1.smallFont.MeasureString(count) * 0.7f;
		AisvTheme.DrawText(b, count, new Vector2(_box.Bounds.Right - cs.X - 12, _box.Bounds.Bottom - cs.Y - 6), AisvTheme.TextFaint, 0.7f);
		bool canSend = service != null && ResidentsGroup.Check(_box.Text, service.IsAiAvailable, service.IsBusy, service.PostsLeft, _members.Count) == ResidentsBlock.None;
		AisvTheme.Button(b, send, Tr.Get("reply.send", "发送"), primary: true, hover: AisvTheme.Hover(send), disabled: !canSend);
		hits.Add(_box.Bounds, () => _box.Focus());
		hits.Add(send, Send);
	}

	private static string Clock(int time) => time <= 0 ? "" : $"{time / 100 % 24:D2}:{time % 100:D2}";
}
