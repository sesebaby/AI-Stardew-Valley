using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>世界 · 帮工：今天谁来农场、谁跟着你、约好的帮工和见面、最近几次；约好的可以说不用来了。</summary>
internal sealed class FarmHelpPage : ShellPage
{
	private static readonly Color Teal = new Color(70, 165, 160);

	private static readonly Color TealSoft = new Color(220, 240, 238);

	private const int RowH = 84;

	private readonly Scroller _scroll = new Scroller();

	private List<FarmHelpJob> _today = new List<FarmHelpJob>();

	private List<(string Npc, string Task, int Price, int? JoinedAt, bool Site)> _members = new List<(string, string, int, int?, bool)>();

	private List<FarmHelpJob> _later = new List<FarmHelpJob>();

	private List<Meetup> _meetups = new List<Meetup>();

	private List<FarmHelpJob> _past = new List<FarmHelpJob>();

	private string _confirm = "";

	private int _seenVersion = -1;

	private Rectangle _view;

	public FarmHelpPage(ShellContext ctx) : base(ctx)
	{
	}

	private static int Today => Game1.Date?.TotalDays ?? 0;

	private int Version => (Ctx.FarmHelp?.Version ?? 0) + (Ctx.Meetups?.Version ?? 0);

	public override void Reload()
	{
		_seenVersion = Version;
		List<FarmHelpJob> all = Ctx.FarmHelp?.All().ToList() ?? new List<FarmHelpJob>();
		int today = Today;
		_today = all.Where(j => j.Day == today).OrderBy(j => j.Start).ToList();
		_later = all.Where(j => j.Day > today && j.State == FarmHelpStates.Scheduled).OrderBy(j => j.Day).ThenBy(j => j.Start).ToList();
		_past = all.Where(j => j.Day < today && !FarmHelpStates.IsOpen(j.State)).OrderByDescending(j => j.Day).ThenByDescending(j => j.Start).Take(8).ToList();
		_meetups = Ctx.Meetups?.Upcoming().ToList() ?? new List<Meetup>();
		HashSet<string> working = new HashSet<string>(_today.Where(j => j.State == FarmHelpStates.Working).Select(j => j.Npc), StringComparer.OrdinalIgnoreCase);
		_members = (Ctx.FarmHelp?.Crew?.Members() ?? Array.Empty<(string, string, int, int?, bool)>()).Where(m => !m.Site && !working.Contains(m.Npc)).ToList();
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (key == Keys.Escape && _confirm.Length > 0)
		{
			_confirm = "";
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	// ---------------- 文字 ----------------

	private static string TaskText(string task) => task switch
	{
		"water" or "harvest" or "animals" or "clear" => FarmHelpRules.TaskLabel(task),
		"forage" => Tr.Get("farmHelpPage.task.forage", "采集"),
		"fish" => Tr.Get("farmHelpPage.task.fish", "钓鱼"),
		"mine" => Tr.Get("farmHelpPage.task.mine", "挖矿"),
		"fight" => Tr.Get("farmHelpPage.task.fight", "打怪"),
		_ => Tr.Get("farmHelpPage.task.follow", "跟着你")
	};

	private static (string Text, Color Fill, Color Ink) StateChip(FarmHelpJob j) => j.State switch
	{
		FarmHelpStates.Scheduled => (Tr.Get("farmHelpPage.state.scheduled", "还没到"), AisvTheme.GoldSoft, AisvTheme.Gold),
		FarmHelpStates.Working => (Tr.Get("farmHelpPage.state.working", "正在干"), AisvTheme.AccentSoft, AisvTheme.AccentDark),
		FarmHelpStates.Done => (Tr.Get("farmHelpPage.state.done", "干完了"), TealSoft, Teal),
		FarmHelpStates.NothingToDo => (Tr.Get("farmHelpPage.state.nothing", "没活可干"), AisvTheme.Divider, AisvTheme.TextMuted),
		FarmHelpStates.CalledOff => (Tr.Get("farmHelpPage.state.calledOff", "来不了"), AisvTheme.Divider, AisvTheme.TextMuted),
		FarmHelpStates.CancelledByPlayer => (Tr.Get("farmHelpPage.state.cancelled", "你取消了"), AisvTheme.Divider, AisvTheme.TextMuted),
		_ => (j.State, AisvTheme.Divider, AisvTheme.TextMuted)
	};

	private static string When(FarmHelpJob j)
	{
		string span = Clock.Text(j.Start) + "–" + Clock.Text(j.End);
		return j.Day == Today ? span : MeetupService.WhenText(j.Day) + " " + span;
	}

	private static string MoneyText(FarmHelpJob j)
	{
		if (j.Price <= 0)
		{
			return Tr.Get("farmHelpPage.free", "免费");
		}
		string paid = Tr.Get("farmHelpPage.paid", "付了 {{gold}}g", ("gold", j.Price));
		return j.Refund > 0 ? paid + " · " + Tr.Get("farmHelpPage.refund", "退回 {{gold}}g", ("gold", j.Refund)) : paid;
	}

	// ---------------- 取消 ----------------

	private void CancelJob(FarmHelpJob j)
	{
		if (_confirm != j.Id)
		{
			Game1.playSound("smallSelect");
			_confirm = j.Id;
			return;
		}
		_confirm = "";
		if (Ctx.FarmHelp?.CancelByPlayer(j.Id) == true)
		{
			Game1.playSound("trashcan");
			Host.Notice(Tr.Get("farmHelpPage.cancelled", "已经告诉{{npc}}不用来了，钱退给你了。", ("npc", Ctx.Display(j.Npc))));
		}
		else
		{
			Game1.playSound("cancel");
			Host.Notice(Tr.Get("farmHelpPage.cantCancel", "已经开工了，取消不了。"));
		}
		Reload();
	}

	private void CancelMeetup(Meetup m)
	{
		if (_confirm != m.Id)
		{
			Game1.playSound("smallSelect");
			_confirm = m.Id;
			return;
		}
		_confirm = "";
		bool sameDay = m.Day == Today;
		if (Ctx.Meetups?.CancelByPlayer(m.Id) == true)
		{
			Game1.playSound("trashcan");
			string reply = sameDay ? Tr.Get("meetup.cancelReplySameDay", "啊……好吧，那下次吧。") : Tr.Get("meetup.cancelReplyEarly", "好，那改天吧。谢谢你提前告诉我。");
			Host.Notice(Ctx.Display(m.Npc) + "：" + reply);
		}
		Reload();
	}

	// ---------------- 画 ----------------

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (Version != _seenVersion)
		{
			Reload();
		}
		_view = area;
		hits.Add(area, () => _confirm = "");
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 72);
		DrawHeader(b, header);
		Rectangle feed = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		_view = feed;
		AisvTheme.BeginClip(b, feed);
		int colW = Math.Min(feed.Width - 48, 900);
		int x = feed.X + (feed.Width - colW) / 2, w = colW;
		int y = feed.Y + 16 - _scroll.Offset;
		if (_today.Count > 0)
		{
			y = Section(b, Tr.Get("farmHelpPage.today", "今天来农场"), x, y);
			foreach (FarmHelpJob j in _today)
			{
				y = DrawJob(b, j, x, y, w, hits, j.State == FarmHelpStates.Scheduled) + 10;
			}
		}
		if (_members.Count > 0)
		{
			y = Section(b, Tr.Get("farmHelpPage.following", "现在跟着你"), x, y + 6);
			foreach ((string Npc, string Task, int Price, int? JoinedAt, bool Site) m in _members)
			{
				y = DrawMember(b, m, x, y, w) + 10;
			}
		}
		if (_later.Count > 0)
		{
			y = Section(b, Tr.Get("farmHelpPage.later", "约好了"), x, y + 6);
			foreach (FarmHelpJob j in _later)
			{
				y = DrawJob(b, j, x, y, w, hits, true) + 10;
			}
		}
		if (_meetups.Count > 0)
		{
			y = Section(b, Tr.Get("farmHelpPage.meetups", "约好的见面"), x, y + 6);
			foreach (Meetup m in _meetups)
			{
				y = DrawMeetup(b, m, x, y, w, hits) + 10;
			}
		}
		if (_past.Count > 0)
		{
			y = Section(b, Tr.Get("farmHelpPage.past", "最近"), x, y + 6);
			foreach (FarmHelpJob j in _past)
			{
				y = DrawJob(b, j, x, y, w, hits, false) + 10;
			}
		}
		y = DrawHowTo(b, x, y + 8, w);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, feed, y + 12);
	}

	private void DrawHeader(SpriteBatch b, Rectangle r)
	{
		AisvTheme.Fill(b, r, AisvTheme.PageDeep);
		AisvTheme.HLine(b, r.X, r.Bottom - 2, r.Width, AisvTheme.Divider);
		AisvTheme.Bold(b, Tr.Get("farmHelpPage.title", "帮工"), new Vector2(r.X + 20, r.Y + 12), AisvTheme.Text, 1.15f);
		int spent = _today.Sum(j => Math.Max(0, j.Price - j.Refund)) + _members.Sum(m => m.Price);
		int people = _today.Select(j => j.Npc).Concat(_members.Select(m => m.Npc)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
		string line = people == 0 ? Tr.Get("farmHelpPage.subNone", "今天没人来帮忙") : Tr.Get("farmHelpPage.sub", "今天 {{n}} 人帮忙 · 花了 {{gold}}g", ("n", people), ("gold", spent));
		AisvTheme.DrawText(b, line, new Vector2(r.X + 20, r.Y + 46), AisvTheme.TextMuted, 0.72f);
		if (Ctx.FarmHelp?.Crew?.IsAvailable != true)
		{
			string note = Tr.Get("farmHelpPage.noSquad", "没装 Squad：跟随不可用，上门帮工按进度结算");
			AisvTheme.DrawText(b, note, new Vector2(r.Right - 24 - AisvTheme.Width(note, 0.62f), r.Y + 28), AisvTheme.TextFaint, 0.62f);
		}
	}

	private static int Section(SpriteBatch b, string title, int x, int y)
	{
		Kit.SectionTitle(b, title, x + 4, y);
		return y + 36;
	}

	private int DrawJob(SpriteBatch b, FarmHelpJob j, int x, int y, int w, Hits hits, bool allowCancel)
	{
		bool working = j.State == FarmHelpStates.Working;
		bool progress = j.Target > 0 && (working || j.State == FarmHelpStates.Done);
		string quote = ShellText.Clean(j.Summary);
		int quoteH = quote.Length > 0 ? AisvTheme.LineHeight(0.72f) + 6 : 0;
		Rectangle r = new Rectangle(x, y, w, RowH + quoteH + (progress ? 28 : 0));
		AisvTheme.DrawCard(b, r);
		Rectangle face = new Rectangle(r.X + 14, r.Y + 14, 56, 56);
		Kit.Avatar(b, face, Ctx, j.Npc);
		int tx = face.Right + 14, right = r.Right - 14;
		(string chip, Color fill, Color ink) = StateChip(j);
		int cw = AisvTheme.ChipWidth(chip, 0.66f);
		AisvTheme.Chip(b, new Vector2(right - cw, r.Y + 14), chip, fill, ink, 0.66f);
		if (allowCancel && Ctx.FarmHelp != null)
		{
			bool confirming = _confirm == j.Id;
			string label = confirming ? (j.Day == Today ? Tr.Get("farmHelpPage.confirmToday", "确定？好感 -10") : Tr.Get("farmHelpPage.confirm", "确定不用来了")) : Tr.Get("farmHelpPage.cancel", "不用来了");
			int bw = (int)AisvTheme.Width(label, 0.7f) + 32;
			Rectangle btn = new Rectangle(right - bw, r.Y + RowH - 44, bw, 32);
			AisvTheme.Button(b, btn, label, primary: confirming, hover: AisvTheme.Hover(btn, _view), scale: 0.7f);
			hits.Add(btn, () => CancelJob(j), _view);
			right = Math.Min(right - cw - 10, btn.X - 10);
		}
		else
		{
			right -= cw + 10;
		}
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(j.Npc) + " · " + FarmHelpRules.TaskLabel(j.Task), right - tx, 0.9f), new Vector2(tx, r.Y + 12), AisvTheme.Text, 0.9f);
		string sub = When(j) + " · " + MoneyText(j);
		AisvTheme.DrawText(b, AisvTheme.Truncate(sub, right - tx, 0.7f), new Vector2(tx, r.Y + 46), AisvTheme.TextFaint, 0.7f);
		if (quote.Length > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate("“" + quote + "”", right - tx, 0.72f), new Vector2(tx, r.Y + 74), AisvTheme.TextMuted, 0.72f);
		}
		if (progress)
		{
			int done = Math.Min(j.Target, working ? FarmHelpService.CountDone(j) : j.Progress);
			Rectangle bar = new Rectangle(tx, r.Bottom - 26, r.Right - 14 - tx - 70, 10);
			AisvTheme.FillRounded(b, bar, AisvTheme.Divider, 5);
			AisvTheme.FillRounded(b, new Rectangle(bar.X, bar.Y, Math.Max(10, bar.Width * done / j.Target), bar.Height), working ? AisvTheme.Accent : Teal, 5);
			AisvTheme.DrawText(b, Tr.Get("farmHelpPage.progress", "{{done}}/{{target}}", ("done", done), ("target", j.Target)), new Vector2(bar.Right + 12, bar.Y - 9), AisvTheme.TextMuted, 0.66f);
		}
		return r.Bottom;
	}

	private int DrawMember(SpriteBatch b, (string Npc, string Task, int Price, int? JoinedAt, bool Site) m, int x, int y, int w)
	{
		Rectangle r = new Rectangle(x, y, w, RowH);
		AisvTheme.DrawCard(b, r);
		Rectangle face = new Rectangle(r.X + 14, r.Y + 14, 56, 56);
		Kit.Avatar(b, face, Ctx, m.Npc);
		int tx = face.Right + 14;
		string money = m.Price > 0 ? Tr.Get("farmHelpPage.paid", "付了 {{gold}}g", ("gold", m.Price)) : Tr.Get("farmHelpPage.free", "免费");
		int cw = AisvTheme.ChipWidth(money, 0.66f);
		AisvTheme.Chip(b, new Vector2(r.Right - 14 - cw, r.Y + 14), money, m.Price > 0 ? AisvTheme.GoldSoft : TealSoft, m.Price > 0 ? AisvTheme.Gold : Teal, 0.66f);
		string what = TaskText(m.Task) + (m.JoinedAt.HasValue ? " · " + Tr.Get("farmHelpPage.since", "{{time}} 开始", ("time", Clock.Text(m.JoinedAt.Value))) : "");
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(m.Npc) + " · " + what, r.Right - 28 - cw - tx, 0.9f), new Vector2(tx, r.Y + 12), AisvTheme.Text, 0.9f);
		AisvTheme.DrawText(b, Tr.Get("farmHelpPage.dismissHint", "找他说话可以让他等着或先回去"), new Vector2(tx, r.Y + 46), AisvTheme.TextFaint, 0.7f);
		return r.Bottom;
	}

	private int DrawMeetup(SpriteBatch b, Meetup m, int x, int y, int w, Hits hits)
	{
		Rectangle r = new Rectangle(x, y, w, RowH);
		AisvTheme.DrawCard(b, r);
		Rectangle face = new Rectangle(r.X + 14, r.Y + 14, 56, 56);
		Kit.Avatar(b, face, Ctx, m.Npc);
		int tx = face.Right + 14, right = r.Right - 14;
		bool confirming = _confirm == m.Id;
		string label = confirming ? (m.Day == Today ? Tr.Get("farmHelpPage.confirmToday", "确定？好感 -10") : Tr.Get("farmHelpPage.confirmMeetup", "确定去不了")) : Tr.Get("farmHelpPage.cancelMeetup", "去不了了");
		int bw = (int)AisvTheme.Width(label, 0.7f) + 32;
		Rectangle btn = new Rectangle(right - bw, r.Center.Y - 16, bw, 32);
		AisvTheme.Button(b, btn, label, primary: confirming, hover: AisvTheme.Hover(btn, _view), scale: 0.7f);
		hits.Add(btn, () => CancelMeetup(m), _view);
		right = btn.X - 12;
		string when = MeetupService.WhenText(m.Day) + " " + Clock.Text(m.Time);
		AisvTheme.Bold(b, AisvTheme.Truncate(Tr.Get("todayPage.meetup", "和{{npc}}在{{place}}见面", ("npc", Ctx.Display(m.Npc)), ("place", MeetupService.PlaceLabel(m))), right - tx, 0.9f), new Vector2(tx, r.Y + 12), AisvTheme.Text, 0.9f);
		string sub = when + " · " + (m.Day > Today ? Tr.Get("meetup.cancelEarly", "提前说一声就不要紧") : Tr.Get("meetup.cancelSameDay", "当天才说，会让人有点失落"));
		AisvTheme.DrawText(b, AisvTheme.Truncate(sub, right - tx, 0.7f), new Vector2(tx, r.Y + 46), AisvTheme.TextFaint, 0.7f);
		return r.Bottom;
	}

	private static int DrawHowTo(SpriteBatch b, int x, int y, int w)
	{
		string text = Tr.Get("farmHelpPage.howTo", "和村民聊天时可以请他们帮忙：跟你去矿洞、帮你浇水收菜，或者约好哪天几点来农场帮忙（浇水、收菜、照看动物、清理树枝和石头）。4~7 心的朋友要收点钱（4 心 600g，每多一心便宜 100g），8 心以上和伴侣免费。有工作的人只在下班后或休息日来。");
		List<string> lines = AisvTheme.WrapLines(text, w - 48, 0.72f);
		int lh = AisvTheme.LineHeight(0.72f);
		Rectangle r = new Rectangle(x, y, w, 56 + lines.Count * lh + 16);
		AisvTheme.DrawCard(b, r, fill: AisvTheme.PageDeep);
		AisvTheme.Bold(b, Tr.Get("farmHelpPage.howToTitle", "怎么请人帮忙"), new Vector2(x + 24, y + 16), AisvTheme.AccentDark, 0.9f);
		int ly = y + 52;
		foreach (string line in lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(x + 24, ly), AisvTheme.TextMuted, 0.72f);
			ly += lh;
		}
		return r.Bottom;
	}
}
