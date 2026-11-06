using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Social.Web;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>日程：左边村民（现在在哪），右边选中那位今天的一站站，现在这一站亮着，和你的约见另外标出。</summary>
internal sealed class AgendaPage : ShellPage
{
	private readonly Scroller _list = new Scroller();

	private readonly Scroller _scroll = new Scroller();

	private List<(string Npc, string Name, string Now)> _people = new List<(string, string, string)>();

	private readonly Dictionary<string, List<AgendaStop>> _stops = new Dictionary<string, List<AgendaStop>>(StringComparer.OrdinalIgnoreCase);

	private string _selected = "";

	private Rectangle _listView, _view;

	public AgendaPage(ShellContext ctx, string? npc = null) : base(ctx)
	{
		_selected = npc ?? "";
	}

	public override string Title => Tr.Get("agenda.title", "日程");

	public override void Reload()
	{
		_people = AppData.Agenda(Ctx, _stops);
		if (_people.Count > 0 && !_people.Any(p => p.Npc.Equals(_selected, StringComparison.OrdinalIgnoreCase)))
		{
			_selected = AppData.Closest(Ctx, _people.Select(p => p.Npc)) ?? _people[0].Npc;
		}
	}

	public override void Scroll(int direction)
	{
		if (AisvTheme.Hover(_listView))
		{
			_list.Wheel(direction);
		}
		else
		{
			_scroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_people.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("agenda.empty", "今天镇上没人可看。"), "");
			return;
		}
		bool narrow = area.Width < 900;
		int listW = narrow ? 0 : 300;
		if (!narrow)
		{
			_listView = new Rectangle(area.X + 16, area.Y + 16, listW, area.Height - 32);
			People(b, hits);
		}
		_view = new Rectangle(area.X + (narrow ? 8 : listW + 36), area.Y + 8, area.Width - (narrow ? 16 : listW + 44), area.Height - 16);
		Timeline(b, hits, narrow);
	}

	private void People(SpriteBatch b, Hits hits)
	{
		AisvTheme.DrawCard(b, _listView);
		Rectangle inner = new Rectangle(_listView.X + 6, _listView.Y + 8, _listView.Width - 12, _listView.Height - 16);
		AisvTheme.BeginClip(b, inner);
		int y = inner.Y - _list.Offset;
		foreach ((string npc, string name, string now) in _people)
		{
			Rectangle row = new Rectangle(inner.X, y, inner.Width - 10, 58);
			if (row.Bottom >= inner.Y && row.Y <= inner.Bottom)
			{
				bool on = npc.Equals(_selected, StringComparison.OrdinalIgnoreCase);
				if (on || AisvTheme.Hover(row, inner))
				{
					AisvTheme.FillRounded(b, row, on ? AisvTheme.SelectedRow : AisvTheme.PageDeep, 10);
				}
				Kit.Avatar(b, new Rectangle(row.X + 8, row.Y + 9, 40, 40), Ctx, npc);
				AisvTheme.Bold(b, AisvTheme.Truncate(name, row.Width - 70, 0.78f), new Vector2(row.X + 58, row.Y + 7), on ? AisvTheme.AccentDark : AisvTheme.Text, 0.78f);
				AisvTheme.DrawText(b, AisvTheme.Truncate(now, row.Width - 70, 0.64f), new Vector2(row.X + 58, row.Y + 33), AisvTheme.TextMuted, 0.64f);
				string pick = npc;
				hits.Add(row, () =>
				{
					Game1.playSound("smallSelect");
					_selected = pick;
					_scroll.Reset();
				}, inner);
			}
			y += 62;
		}
		AisvTheme.EndClip(b);
		_list.Finish(b, _listView, y + 16, 6);
	}

	private void Timeline(SpriteBatch b, Hits hits, bool narrow)
	{
		int x = _view.X + 16, w = _view.Width - 44;
		AisvTheme.BeginClip(b, _view);
		int y = _view.Y + 12 - _scroll.Offset;
		string name = Ctx.Display(_selected);
		Rectangle head = new Rectangle(x, y, w, 96);
		AisvTheme.DrawCard(b, head);
		Kit.Avatar(b, new Rectangle(head.X + 18, head.Y + 16, 64, 64), Ctx, _selected);
		AisvTheme.Bold(b, name, new Vector2(head.X + 98, head.Y + 18), AisvTheme.Text, 1.0f);
		string now = Tr.Get("agenda.now", "现在 {{time}}", ("time", Agenda.Clock(Game1.timeOfDay)));
		AisvTheme.DrawText(b, now, new Vector2(head.X + 98, head.Y + 56), AisvTheme.TextMuted, 0.72f);
		if (narrow)
		{
			int i = _people.FindIndex(p => p.Npc.Equals(_selected, StringComparison.OrdinalIgnoreCase));
			Rectangle prev = new Rectangle(head.Right - 108, head.Y + 26, 44, 44), next = new Rectangle(head.Right - 56, head.Y + 26, 44, 44);
			AisvTheme.Button(b, prev, "‹", hover: AisvTheme.Hover(prev, _view));
			AisvTheme.Button(b, next, "›", hover: AisvTheme.Hover(next, _view));
			hits.Add(prev, () => _selected = _people[(i - 1 + _people.Count) % _people.Count].Npc, _view);
			hits.Add(next, () => _selected = _people[(i + 1) % _people.Count].Npc, _view);
		}
		y = head.Bottom + 20;
		List<AgendaStop> stops = _stops.TryGetValue(_selected, out List<AgendaStop>? s) ? s : new List<AgendaStop>();
		if (stops.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("agenda.none", "今天没有安排，大概一直待在家里。"), new Vector2(x + 8, y), AisvTheme.TextMuted, 0.8f);
			y += 40;
		}
		int current = Agenda.Current(stops.Where(t => !t.WithYou).ToList(), Game1.timeOfDay);
		int plain = -1;
		int lineX = x + 112;
		for (int i = 0; i < stops.Count; i++)
		{
			AgendaStop stop = stops[i];
			if (!stop.WithYou)
			{
				plain++;
			}
			bool here = !stop.WithYou && plain == current;
			bool past = !stop.WithYou && plain < current;
			Rectangle row = new Rectangle(lineX + 26, y, w - (lineX + 26 - x), stop.Note.Length > 0 ? 82 : 62);
			if (i < stops.Count - 1)
			{
				AisvTheme.VLine(b, lineX - 1, y + 30, row.Height + 12, AisvTheme.Divider, 3);
			}
			string clock = Agenda.Clock(stop.Time);
			AisvTheme.Bold(b, clock, new Vector2(lineX - 24 - AisvTheme.Width(clock, 0.8f), y + 18), here ? AisvTheme.AccentDark : past ? AisvTheme.TextFaint : AisvTheme.Text, 0.8f);
			Color dot = stop.WithYou ? AisvTheme.Pink : here ? AisvTheme.Accent : past ? AisvTheme.Divider : AisvTheme.CardBorder;
			AisvTheme.FillRounded(b, new Rectangle(lineX - 10, y + 20, 20, 20), dot, 10);
			if (here)
			{
				AisvTheme.FillRounded(b, new Rectangle(lineX - 5, y + 25, 10, 10), Color.White, 5);
			}
			AisvTheme.Bordered(b, row, stop.WithYou ? AisvTheme.PinkSoft : here ? AisvTheme.SelectedRow : AisvTheme.Card, stop.WithYou ? AisvTheme.Pink * 0.6f : here ? AisvTheme.Accent * 0.5f : AisvTheme.Divider, 12);
			AisvTheme.Bold(b, AisvTheme.Truncate(stop.Place, row.Width - 40, 0.84f), new Vector2(row.X + 18, row.Y + 9), past ? AisvTheme.TextMuted : AisvTheme.Text, 0.84f);
			string doing = here ? (stop.Doing.Length > 0 ? Tr.Get("agenda.doingNow", "{{doing}} · 正在这儿", ("doing", stop.Doing)) : Tr.Get("agenda.hereNow", "正在这儿")) : stop.Doing;
			AisvTheme.DrawText(b, AisvTheme.Truncate(doing, row.Width - 40, 0.7f), new Vector2(row.X + 18, row.Y + 36), stop.WithYou ? AisvTheme.Pink : AisvTheme.TextMuted, 0.7f);
			if (stop.Note.Length > 0)
			{
				AisvTheme.DrawText(b, AisvTheme.Truncate(ShellText.Clean(stop.Note), row.Width - 40, 0.66f), new Vector2(row.X + 18, row.Y + 58), AisvTheme.TextFaint, 0.66f);
			}
			y = row.Bottom + 12;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y + 8);
	}
}
