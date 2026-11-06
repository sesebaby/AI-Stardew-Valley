using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Outings;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Outings;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>出游：约好还没去的（去哪、几点、还剩几天），能去的地方，和配偶约会能去的地方。</summary>
internal sealed class OutingsPage : ShellPage
{
	private const int PlaceCardWidth = 300;

	private readonly Scroller _scroll = new Scroller();

	private IReadOnlyList<OutingRecord> _waiting = Array.Empty<OutingRecord>();

	private IReadOnlyList<(string Location, List<OutingDestination> Rows)> _places = Array.Empty<(string, List<OutingDestination>)>();

	private IReadOnlyList<(string Location, List<OutingDestination> Rows)> _datePlaces = Array.Empty<(string, List<OutingDestination>)>();

	private Rectangle _view;

	public OutingsPage(ShellContext ctx) : base(ctx)
	{
	}

	public override void Reload()
	{
		OutingService? outings = Ctx.Outings;
		if (outings == null)
		{
			return;
		}
		_waiting = outings.Book.Waiting;
		_places = Group(outings.Destinations(spouse: false));
		_datePlaces = Group(outings.Destinations(spouse: true));
	}

	private static List<(string, List<OutingDestination>)> Group(IReadOnlyList<OutingDestination> rows) => rows.GroupBy(d => d.Location, StringComparer.OrdinalIgnoreCase)
		.Select(g => (g.Key, g.ToList())).OrderBy(g => OutingService.PlaceName(g.Key), StringComparer.Ordinal).ToList();

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		Kit.Header(b, header, Tr.Get("shell.outings.title", "出游"), _waiting.Count > 0 ? Tr.Get("shell.outings.count", "约好了 {{n}} 次", ("n", _waiting.Count)) : "", null, hits, null);
		string today = ShellText.TopLine();
		AisvTheme.DrawText(b, today, new Vector2(header.Right - 24 - AisvTheme.Width(today, 0.8f), header.Y + 22), AisvTheme.TextFaint, 0.8f);
		_view = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		int w = Math.Min(_view.Width - 80, 980);
		int x = _view.X + (_view.Width - w) / 2;
		int y = _view.Y + 16 - _scroll.Offset;
		AisvTheme.BeginClip(b, _view);
		Kit.SectionTitle(b, Tr.Get("shell.outings.waiting", "约好的"), x, y);
		y += 40;
		if (_waiting.Count == 0)
		{
			Rectangle empty = new Rectangle(x, y, w, 104);
			AisvTheme.DrawCard(b, empty);
			Kit.Empty(b, new Rectangle(x, y + 14, w, 80), Tr.Get("shell.outings.none", "还没有约好的出游。"), Tr.Get("shell.outings.noneHint", "和村民聊天时打开回复框底下的“约出去”，对方答应了就会出现在这里。"));
			y = empty.Bottom + 28;
		}
		else
		{
			foreach (OutingRecord r in _waiting)
			{
				y = DrawTicket(b, r, new Rectangle(x, y, w, 112)) + 12;
			}
			y += 16;
		}
		y = DrawPlaces(b, Tr.Get("shell.outings.places", "能去的地方"), Tr.Get("shell.outings.placesNone", "现在没有能去的地方。"), _places, x, y, w);
		y = DrawPlaces(b, Tr.Get("shell.outings.datePlaces", "和配偶约会能去的地方"), Tr.Get("shell.outings.datePlacesNone", "现在没有能约会的地方。"), _datePlaces, x, y, w);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y);
	}

	private int DrawTicket(SpriteBatch b, OutingRecord r, Rectangle card)
	{
		int today = GameState.Today.TotalDays;
		int left = Math.Max(0, r.AskedOn + OutingBook.ExpiryDays - today);
		bool late = today - r.AskedOn >= 5;
		Color accent = !late ? AisvTheme.Accent : left == 0 ? AisvTheme.Danger : AisvTheme.Warn;
		AisvTheme.DrawCard(b, card);
		AisvTheme.FillRounded(b, new Rectangle(card.X + 2, card.Y + 12, 6, card.Height - 24), accent, 3);
		Kit.Avatar(b, new Rectangle(card.X + 24, card.Y + 22, 68, 68), Ctx, r.Npc);
		int tx = card.X + 112;
		string place = OutingService.PlaceName(r.Place);
		AisvTheme.Bold(b, Tr.Get("shell.outings.who", "{{npc}} · {{location}}", ("npc", Ctx.Display(r.Npc)), ("location", place)), new Vector2(tx, card.Y + 18), AisvTheme.Text, 0.95f);
		float cx = tx;
		Rectangle chip = AisvTheme.Chip(b, new Vector2(cx, card.Y + 52), $"{OutingService.Clock(r.FromTime)}-{OutingService.Clock(r.ToTime)}", AisvTheme.AccentSoft, AisvTheme.AccentDark, 0.7f);
		cx = chip.Right + 8;
		string tone = OutingCatalog.NormalizeType(r.Tone);
		if (tone.Length > 0)
		{
			cx = AisvTheme.Chip(b, new Vector2(cx, card.Y + 52), OutingAi.ToneName(tone), AisvTheme.GoldSoft, AisvTheme.Gold, 0.7f).Right + 8;
		}
		if (Game1.timeOfDay >= r.FromTime && Game1.timeOfDay <= r.ToTime)
		{
			AisvTheme.Chip(b, new Vector2(cx, card.Y + 52), Tr.Get("shell.outings.now", "现在就能去"), AisvTheme.Accent, Color.White, 0.7f);
		}
		string remain = left == 0 ? Tr.Get("shell.outings.lastDay", "今天是最后一天") : Tr.Get("shell.outings.daysLeft", "还剩 {{n}} 天", ("n", left));
		string meta = Tr.Get("shell.outings.hint", "{{time}}之间去{{location}}，到了就会开始", ("time", $"{OutingService.Clock(r.FromTime)}-{OutingService.Clock(r.ToTime)}"), ("location", place));
		AisvTheme.DrawText(b, AisvTheme.Truncate(meta, card.Right - tx - 200, 0.72f), new Vector2(tx, card.Y + 84), AisvTheme.TextMuted, 0.72f);
		AisvTheme.Bold(b, remain, new Vector2(card.Right - 24 - AisvTheme.Width(remain, 0.8f), card.Y + 20), accent, 0.8f);
		return card.Bottom;
	}

	private int DrawPlaces(SpriteBatch b, string title, string none, IReadOnlyList<(string Location, List<OutingDestination> Rows)> places, int x, int y, int w)
	{
		Kit.SectionTitle(b, title, x, y);
		y += 40;
		if (places.Count == 0)
		{
			AisvTheme.DrawText(b, none, new Vector2(x + 4, y), AisvTheme.TextMuted, 0.8f);
			return y + 52;
		}
		int columns = Math.Max(1, (w + 12) / (PlaceCardWidth + 12));
		int cardW = (w - (columns - 1) * 12) / columns;
		int lh = AisvTheme.LineHeight(0.72f);
		int rowTop = y, rowHeight = 0;
		for (int i = 0; i < places.Count; i++)
		{
			(string location, List<OutingDestination> rows) = places[i];
			List<string> times = Merge(rows.Select(r => (r.TimeMin, r.TimeMax))).Select(t => $"{OutingService.Clock(t.Min)}-{OutingService.Clock(t.Max)}").ToList();
			List<string> tones = rows.SelectMany(r => r.Types).Distinct().Select(OutingAi.ToneName).ToList();
			List<string> toneLines = AisvTheme.WrapLines(string.Join(" · ", tones), cardW - 40, 0.72f);
			int h = 24 + 32 + 34 + toneLines.Count * lh + 20;
			if (i % columns == 0 && i > 0)
			{
				rowTop += rowHeight + 12;
				rowHeight = 0;
			}
			Rectangle card = new Rectangle(x + i % columns * (cardW + 12), rowTop, cardW, h);
			rowHeight = Math.Max(rowHeight, h);
			AisvTheme.DrawCard(b, card);
			AisvTheme.Bold(b, OutingService.PlaceName(location), new Vector2(card.X + 20, card.Y + 18), AisvTheme.Text, 0.9f);
			string count = Tr.Get("shell.outings.kinds", "{{n}} 种", ("n", rows.Sum(r => r.Count)));
			AisvTheme.DrawText(b, count, new Vector2(card.Right - 20 - AisvTheme.Width(count, 0.7f), card.Y + 22), AisvTheme.TextFaint, 0.7f);
			float cx = card.X + 20;
			foreach (string t in times)
			{
				cx = AisvTheme.Chip(b, new Vector2(cx, card.Y + 56), t, AisvTheme.AccentSoft, AisvTheme.AccentDark, 0.65f).Right + 6;
			}
			int ty = card.Y + 92;
			foreach (string line in toneLines)
			{
				AisvTheme.DrawText(b, line, new Vector2(card.X + 20, ty), AisvTheme.TextMuted, 0.72f);
				ty += lh;
			}
		}
		return rowTop + rowHeight + 32;
	}

	/// <summary>重叠的时间段合并成一段。</summary>
	private static List<(int Min, int Max)> Merge(IEnumerable<(int Min, int Max)> windows)
	{
		List<(int Min, int Max)> merged = new List<(int, int)>();
		foreach ((int min, int max) in windows.OrderBy(w => w.Min).ThenBy(w => w.Max))
		{
			if (merged.Count > 0 && min <= merged[^1].Max)
			{
				merged[^1] = (merged[^1].Min, Math.Max(merged[^1].Max, max));
			}
			else
			{
				merged.Add((min, max));
			}
		}
		return merged;
	}
}
