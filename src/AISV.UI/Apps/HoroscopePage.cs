using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>星座：最上面一张大卡给最在意的那位（点下面的卡片可以换），下面是全村今天的运势。</summary>
internal sealed class HoroscopePage : ShellPage
{
	private static readonly Color Night = new Color(46, 50, 96);

	private static readonly Color NightSoft = new Color(232, 230, 250);

	private readonly Scroller _scroll = new Scroller();

	private List<Fortune> _all = new List<Fortune>();

	private string _focus = "";

	private Rectangle _view;

	public HoroscopePage(ShellContext ctx, string? npc = null) : base(ctx)
	{
		_focus = npc ?? "";
	}

	public override string Title => Tr.Get("horo.title", "星座");

	public override void Reload()
	{
		_all = AppData.Fortunes(Ctx).OrderByDescending(f => Ctx.Relationships.EffectiveHearts(f.Npc)).ThenBy(f => Ctx.Display(f.Npc), StringComparer.CurrentCulture).ToList();
		if (_all.Count > 0 && !_all.Any(f => f.Npc.Equals(_focus, StringComparison.OrdinalIgnoreCase)))
		{
			_focus = AppData.Closest(Ctx, _all.Select(f => f.Npc)) ?? _all[0].Npc;
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		if (_all.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("horo.empty", "今天的星象还没排出来。"), "");
			return;
		}
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		AisvTheme.BeginClip(b, area);
		int y = area.Y + 20 - _scroll.Offset;
		Fortune focus = _all.First(f => f.Npc.Equals(_focus, StringComparison.OrdinalIgnoreCase));
		y = Big(b, focus, x, y, w) + 26;
		Kit.SectionTitle(b, Tr.Get("horo.everyone", "全村今天"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 12;
		int cols = w >= 1100 ? 3 : w >= 700 ? 2 : 1;
		const int gap = 16, h = 132;
		int cw = (w - gap * (cols - 1)) / cols;
		for (int i = 0; i < _all.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % cols * (cw + gap), y + i / cols * (h + gap), cw, h);
			if (r.Bottom >= area.Y && r.Y <= area.Bottom)
			{
				Small(b, _all[i], r, AisvTheme.Hover(r, area), _all[i].Npc == focus.Npc);
				string npc = _all[i].Npc;
				hits.Add(r, () =>
				{
					Game1.playSound("smallSelect");
					_focus = npc;
					_scroll.Reset();
				}, area);
			}
		}
		y += (_all.Count + cols - 1) / cols * (h + gap);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, area, y + 8);
	}

	private int Big(SpriteBatch b, Fortune f, int x, int y, int w)
	{
		Rectangle card = new Rectangle(x, y, w, 210);
		AisvTheme.DrawShadow(b, card, 16, 5);
		AisvTheme.FillRounded(b, card, Night, 16);
		for (int i = 0; i < 28; i++)
		{
			int sx = card.X + 20 + Horoscope.Roll(7, i, f.Npc, 11, card.Width - 40);
			int sy = card.Y + 12 + Horoscope.Roll(7, i, f.Npc, 12, card.Height - 24);
			int size = 2 + Horoscope.Roll(7, i, f.Npc, 13, 3);
			AisvTheme.Fill(b, new Rectangle(sx, sy, size, size), Color.White * (0.35f + 0.15f * size));
		}
		Kit.Avatar(b, new Rectangle(card.X + 28, card.Y + 28, 96, 96), Ctx, f.Npc);
		int tx = card.X + 148;
		AisvTheme.Bold(b, Ctx.Display(f.Npc), new Vector2(tx, card.Y + 26), Color.White, 1.1f);
		AisvTheme.Chip(b, new Vector2(tx, card.Y + 72), Horoscope.Signs[f.Sign], NightSoft, Night, 0.72f);
		Stars(b, tx + AisvTheme.ChipWidth(Horoscope.Signs[f.Sign], 0.72f) + 18, card.Y + 70, f.Stars, 2);
		string advice = Horoscope.Advice(f);
		if (advice.Length > 0)
		{
			AisvTheme.Bold(b, AisvTheme.Truncate(advice, card.Right - tx - 24, 0.92f), new Vector2(tx, card.Y + 116), new Color(255, 224, 140), 0.92f);
		}
		AisvTheme.DrawText(b, AisvTheme.Truncate(f.Line, card.Right - tx - 24, 0.78f), new Vector2(tx, card.Y + 156), new Color(220, 222, 245), 0.78f);
		string lucky = Tr.Get("horo.lucky", "幸运时刻 {{time}}", ("time", Agenda.Clock(900 + f.LuckyIndex % 12 * 100)));
		AisvTheme.DrawText(b, lucky, new Vector2(card.X + 28, card.Y + 150), new Color(200, 204, 240), 0.66f);
		return card.Bottom;
	}

	private void Small(SpriteBatch b, Fortune f, Rectangle r, bool hover, bool focus)
	{
		AisvTheme.Bordered(b, r, focus ? NightSoft : hover ? AisvTheme.PageDeep : AisvTheme.Card, focus ? Night * 0.5f : AisvTheme.CardBorder, 12);
		Kit.Avatar(b, new Rectangle(r.X + 14, r.Y + 14, 48, 48), Ctx, f.Npc);
		int tx = r.X + 74;
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(f.Npc), r.Right - tx - 110, 0.8f), new Vector2(tx, r.Y + 12), AisvTheme.Text, 0.8f);
		Stars(b, r.Right - 100, r.Y + 16, f.Stars, 1);
		AisvTheme.DrawText(b, Horoscope.Signs[f.Sign], new Vector2(tx, r.Y + 40), AisvTheme.TextFaint, 0.64f);
		string advice = Horoscope.Advice(f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(advice.Length > 0 ? advice : f.Line, r.Width - 28, 0.7f), new Vector2(r.X + 14, r.Y + 72), Night, 0.7f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(f.Line, r.Width - 28, 0.64f), new Vector2(r.X + 14, r.Y + 100), AisvTheme.TextMuted, 0.64f);
	}

	private static void Stars(SpriteBatch b, int x, int y, int stars, int scale)
	{
		int step = 18 * scale;
		for (int i = 0; i < 5; i++)
		{
			(i < stars ? AppIcons.Reviews : AppIcons.StarEmpty).Draw(b, x + i * step, y, scale);
		}
	}
}
