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

/// <summary>年度回顾：一年一本小册子，一页一个主题（开头、聊天、礼物、八卦、故事、里程碑、来信、名声），可以换年份。</summary>
internal sealed class YearReviewPage : ShellPage
{
	private YearFacts _facts = YearFacts.Empty;

	private IReadOnlyList<int> _years = new[] { 1 };

	private int _year = 1;

	private YearSummary? _summary;

	private int _page;

	private bool _loaded;

	public YearReviewPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("year.title", "年度回顾");

	public override void Reload()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		_facts = AppData.Year(Ctx);
		int current = Math.Max(1, Game1.year);
		_years = YearReview.Years(_facts, current);
		SetYear(YearReview.DefaultYear(_facts, current));
	}

	private void SetYear(int year)
	{
		_year = year;
		_summary = YearReview.Summarize(_facts, year);
		_page = 0;
	}

	private void Flip(int delta)
	{
		if (_summary == null)
		{
			return;
		}
		int next = Math.Clamp(_page + delta, 0, _summary.Pages.Count - 1);
		if (next != _page)
		{
			_page = next;
			Game1.playSound("shwip");
		}
	}

	private void ChangeYear(int delta)
	{
		int i = _years.ToList().IndexOf(_year) + delta;
		if (i >= 0 && i < _years.Count)
		{
			SetYear(_years[i]);
			Game1.playSound("newArtifact");
		}
	}

	public override void Scroll(int direction) => Flip(direction > 0 ? -1 : 1);

	public override bool KeyPress(Keys key)
	{
		switch (key)
		{
		case Keys.Left:
			Flip(-1);
			return true;
		case Keys.Right:
		case Keys.Space:
			Flip(1);
			return true;
		case Keys.PageUp:
			ChangeYear(-1);
			return true;
		case Keys.PageDown:
			ChangeYear(1);
			return true;
		default:
			return false;
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_summary == null)
		{
			return;
		}
		YearPage page = _summary.Pages[_page];
		Color theme = ThemeOf(page);
		int cw = Math.Min(1040, area.Width - 80);
		Rectangle card = new Rectangle(area.Center.X - cw / 2, area.Y + 18, cw, area.Height - 18 - 84);
		AisvTheme.DrawShadow(b, card, 18, 6);
		AisvTheme.Bordered(b, card, AisvTheme.Card, Color.Lerp(theme, AisvTheme.CardBorder, 0.5f), 18);
		Rectangle band = new Rectangle(card.X + 2, card.Y + 2, card.Width - 4, 82);
		AisvTheme.FillRounded(b, band, Color.Lerp(theme, Color.White, 0.82f), 16);
		AisvTheme.Fill(b, new Rectangle(band.X, band.Bottom - 16, band.Width, 16), Color.Lerp(theme, Color.White, 0.82f));
		AisvTheme.Fill(b, new Rectangle(band.X, band.Bottom, band.Width, 3), theme);
		// 右上角换年份。
		string yearText = Tr.Get("year.banner", "第 {{year}} 年", ("year", _year));
		float yw = AisvTheme.Width(yearText, 0.9f);
		bool older = _years.Count > 0 && _years[0] < _year, newer = _years.Count > 0 && _years[^1] > _year;
		Rectangle nextYear = new Rectangle(band.Right - 24 - 40, band.Center.Y - 20, 40, 40);
		Vector2 yp = new Vector2(nextYear.X - 8 - yw, band.Center.Y - 13);
		Rectangle prevYear = new Rectangle((int)yp.X - 48, band.Center.Y - 20, 40, 40);
		Confetti(b, new Rectangle(band.X, band.Y, prevYear.X - band.X - 20, band.Height), theme);
		AisvTheme.Bold(b, yearText, yp, Color.Lerp(theme, AisvTheme.Text, 0.55f), 0.9f);
		YearArrow(b, prevYear, true, older, hits, () => ChangeYear(-1));
		YearArrow(b, nextYear, false, newer, hits, () => ChangeYear(1));
		AisvTheme.Bold(b, Heading(page), new Vector2(card.X + 40, card.Y + 24), Color.Lerp(theme, AisvTheme.Text, 0.55f), 1.25f);
		Rectangle body = new Rectangle(card.X + 40, band.Bottom + 24, card.Width - 80, card.Bottom - band.Bottom - 40);
		AisvTheme.BeginClip(b, new Rectangle(card.X, band.Bottom + 4, card.Width, card.Bottom - band.Bottom - 8));
		switch (page)
		{
		case YearPage.Intro:
			Intro(b, body, theme);
			break;
		case YearPage.Talks:
			Talks(b, body, theme);
			break;
		case YearPage.Gifts:
			Gifts(b, body, theme);
			break;
		case YearPage.Gossip:
			Gossip(b, body, theme);
			break;
		case YearPage.Arcs:
			Arcs(b, body, theme);
			break;
		case YearPage.Milestones:
			Milestones(b, body, theme);
			break;
		case YearPage.Letters:
			Letters(b, body, theme);
			break;
		default:
			Reputation(b, body, theme);
			break;
		}
		AisvTheme.EndClip(b);
		Footer(b, new Rectangle(card.X, card.Bottom + 18, card.Width, 50), theme, hits);
	}

	private static void YearArrow(SpriteBatch b, Rectangle r, bool left, bool enabled, Hits hits, Action go)
	{
		if (!enabled)
		{
			return;
		}
		AisvTheme.FillRounded(b, r, AisvTheme.Hover(r) ? Color.White : Color.White * 0.6f, 20);
		AisvTheme.Chevron(b, new Vector2(r.Center.X + (left ? 2 : -2), r.Center.Y), left, AisvTheme.AccentDark);
		hits.Add(r, go);
	}

	private void Footer(SpriteBatch b, Rectangle r, Color theme, Hits hits)
	{
		int n = _summary!.Pages.Count;
		const int dot = 12, gap = 12;
		int dw = n * dot + (n - 1) * gap;
		int dx = r.Center.X - dw / 2;
		for (int i = 0; i < n; i++)
		{
			Rectangle d = new Rectangle(dx + i * (dot + gap), r.Center.Y - dot / 2, dot, dot);
			AisvTheme.FillRounded(b, i == _page ? new Rectangle(d.X - 3, d.Y - 3, dot + 6, dot + 6) : d, i == _page ? theme : AisvTheme.Divider, (i == _page ? dot + 6 : dot) / 2);
			int k = i;
			hits.Add(AisvTheme.Inflate(d, 6), () =>
			{
				_page = k;
				Game1.playSound("shwip");
			});
		}
		Rectangle back = new Rectangle(r.X, r.Y, 150, r.Height);
		Rectangle next = new Rectangle(r.Right - 150, r.Y, 150, r.Height);
		if (_page > 0)
		{
			AisvTheme.Button(b, back, Tr.Get("year.back", "上一页"), hover: AisvTheme.Hover(back), scale: 0.85f);
			hits.Add(back, () => Flip(-1));
		}
		bool last = _page == n - 1;
		AisvTheme.Button(b, next, last ? Tr.Get("year.again", "从头看") : Tr.Get("year.next", "下一页"), primary: true, hover: AisvTheme.Hover(next), scale: 0.85f);
		hits.Add(next, () =>
		{
			if (last)
			{
				_page = 0;
				Game1.playSound("shwip");
			}
			else
			{
				Flip(1);
			}
		});
	}

	/// <summary>页头右边几片彩纸。</summary>
	private static void Confetti(SpriteBatch b, Rectangle band, Color theme)
	{
		Color[] colors = { theme, AisvTheme.Gold, AisvTheme.Pink, new Color(110, 160, 230) };
		for (int i = 0; i < 14; i++)
		{
			int x = band.Right - 40 - (int)(FarmerReviews.StableHash("c" + i) % 300);
			int y = band.Y + 14 + (int)(FarmerReviews.StableHash("y" + i) % (uint)(band.Height - 34));
			int s = 6 + i % 3 * 2;
			AisvTheme.FillRounded(b, new Rectangle(x, y, s, s), colors[i % colors.Length] * 0.55f, i % 2 == 0 ? s / 2 : 1);
		}
	}

	private static Color ThemeOf(YearPage p) => p switch
	{
		YearPage.Talks => new Color(84, 140, 214),
		YearPage.Gifts => new Color(214, 104, 140),
		YearPage.Gossip => new Color(146, 100, 200),
		YearPage.Arcs => new Color(54, 150, 140),
		YearPage.Milestones => AisvTheme.Gold,
		YearPage.Letters => new Color(222, 128, 60),
		_ => AisvTheme.Accent
	};

	private string Heading(YearPage p) => p switch
	{
		YearPage.Intro => Tr.Get("year.intro.heading", "山谷里的第 {{year}} 年", ("year", _year)),
		YearPage.Talks => Tr.Get("year.talks.heading", "最常一起聊天的人"),
		YearPage.Gifts => Tr.Get("year.gifts.heading", "年度礼物"),
		YearPage.Gossip => Tr.Get("year.gossip.heading", "年度八卦"),
		YearPage.Arcs => Tr.Get("year.arcs.heading", "看到了结局的故事"),
		YearPage.Milestones => Tr.Get("year.milestones.heading", "难忘的时刻"),
		YearPage.Letters => Tr.Get("year.letters.heading", "信箱里的这一年"),
		_ => Tr.Get("year.rep.heading", "你在山谷的名声")
	};

	private void Intro(SpriteBatch b, Rectangle r, Color theme)
	{
		YearSummary s = _summary!;
		(int Value, string Label, Color Tint)[] stats =
		{
			(s.TalkCount, Tr.Get("year.intro.talks", "次聊天"), new Color(84, 140, 214)),
			(s.GiftCount, Tr.Get("year.intro.gifts", "份礼物"), new Color(214, 104, 140)),
			(s.LetterCount, Tr.Get("year.intro.letters", "封来信"), new Color(222, 128, 60)),
			(s.Arcs.Count, Tr.Get("year.intro.arcs", "个故事看到结局"), new Color(54, 150, 140))
		};
		const int gap = 18;
		int tw = (r.Width - gap * 3) / 4;
		for (int i = 0; i < stats.Length; i++)
		{
			Rectangle t = new Rectangle(r.X + i * (tw + gap), r.Y, tw, 150);
			AisvTheme.FillRounded(b, t, Color.Lerp(stats[i].Tint, Color.White, 0.88f), 14);
			AisvTheme.FillRounded(b, new Rectangle(t.X, t.Y, 6, t.Height), stats[i].Tint, 3);
			string v = stats[i].Value.ToString();
			b.DrawString(Game1.dialogueFont, v, new Vector2(t.X + 26, t.Y + 22), Color.Lerp(stats[i].Tint, AisvTheme.Text, 0.35f), 0f, Vector2.Zero, 1.3f, SpriteEffects.None, 1f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(stats[i].Label, t.Width - 40, 0.78f), new Vector2(t.X + 26, t.Bottom - 44), AisvTheme.TextMuted, 0.78f);
		}
		int y = r.Y + 190;
		if (s.TalkCount == 0 && s.GiftCount == 0 && s.LetterCount == 0 && s.Arcs.Count == 0)
		{
			Paragraph(b, Tr.Get("year.intro.empty", "这一年还没留下什么。多去和村民聊聊、送点东西，过些日子再来翻翻。"), r.X, ref y, r.Width, AisvTheme.TextMuted, 0.85f);
			return;
		}
		if (s.People > 0)
		{
			Paragraph(b, Tr.Get("year.intro.people", "这一年你和 {{count}} 位村民说过话。", ("count", s.People)), r.X, ref y, r.Width, AisvTheme.Text, 0.9f);
		}
		y += 10;
		Paragraph(b, Tr.Get("year.intro.hint", "往后翻，看看这一年的高光。"), r.X, ref y, r.Width, AisvTheme.TextFaint, 0.75f);
	}

	private void Talks(SpriteBatch b, Rectangle r, Color theme)
	{
		YearSummary s = _summary!;
		int y = r.Y;
		Paragraph(b, Tr.Get("year.talks.total", "这一年一共聊了 {{count}} 次。", ("count", s.TalkCount)), r.X, ref y, r.Width, AisvTheme.TextMuted, 0.76f, center: true);
		// 领奖台：第一名在中间、最高；尺寸跟着可用高度走。
		int room = r.Bottom - y - 8;
		int[] order = s.TopTalkers.Count switch { 1 => new[] { 0 }, 2 => new[] { 1, 0 }, _ => new[] { 1, 0, 2 } };
		const int colW = 220;
		int x0 = r.Center.X - order.Length * colW / 2;
		for (int k = 0; k < order.Length; k++)
		{
			int rank = order[k];
			Ranked who = s.TopTalkers[rank];
			int stand = (int)(room * (rank == 0 ? 0.26f : rank == 1 ? 0.18f : 0.12f));
			int av = (int)Math.Min(rank == 0 ? 112 : 92, room * (rank == 0 ? 0.36f : 0.3f));
			int cx = x0 + k * colW + colW / 2;
			Rectangle block = new Rectangle(cx - 86, r.Bottom - stand, 172, stand);
			AisvTheme.FillRounded(b, block, Color.Lerp(theme, Color.White, rank == 0 ? 0.55f : 0.75f), 10);
			string place = (rank + 1).ToString();
			Vector2 ps = Game1.dialogueFont.MeasureString(place);
			b.DrawString(Game1.dialogueFont, place, new Vector2(cx - ps.X / 2f, block.Y + Math.Max(2, (stand - ps.Y) / 2f)), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);
			Rectangle avatar = new Rectangle(cx - av / 2, block.Y - av - 66, av, av);
			if (rank == 0)
			{
				AisvTheme.FillRounded(b, AisvTheme.Inflate(avatar, 5), AisvTheme.Gold, 12);
			}
			Kit.Avatar(b, avatar, Ctx, who.Name);
			string name = AisvTheme.Truncate(Ctx.Display(who.Name), colW - 16, 0.88f);
			AisvTheme.Bold(b, name, new Vector2(cx - AisvTheme.Width(name, 0.88f) / 2f, avatar.Bottom + 10), AisvTheme.Text, 0.88f);
			string count = Tr.Get("year.talks.count", "{{count}} 次", ("count", who.Count));
			AisvTheme.DrawText(b, count, new Vector2(cx - AisvTheme.Width(count, 0.7f) / 2f, avatar.Bottom + 38), AisvTheme.TextMuted, 0.7f);
		}
	}

	private void Gifts(SpriteBatch b, Rectangle r, Color theme)
	{
		YearSummary s = _summary!;
		TopGift g = s.TopGift!;
		int side = Math.Min(170, r.Height - 20);
		Rectangle box = new Rectangle(r.X, r.Y, side, side);
		AisvTheme.FillRounded(b, box, Color.Lerp(theme, Color.White, 0.86f), 20);
		int gs = side * 2 / 3;
		GiftBox(b, new Rectangle(box.Center.X - gs / 2, box.Center.Y - gs / 2, gs, gs), theme);
		int tx = box.Right + 36, w = r.Right - tx;
		int y = r.Y;
		AisvTheme.DrawText(b, Tr.Get("year.gifts.most", "送得最多的是"), new Vector2(tx, y), AisvTheme.TextMuted, 0.76f);
		y += 28;
		b.DrawString(Game1.dialogueFont, AisvTheme.Truncate(g.Item, w / 1.1f, 1f), new Vector2(tx, y), Color.Lerp(theme, AisvTheme.Text, 0.5f), 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 1f);
		y += 64;
		string times = g.Count == 1 ? Tr.Get("year.gifts.once", "只送过一回，给了{{name}}。", ("name", Ctx.Display(g.Recipient)))
			: Tr.Get("year.gifts.times", "一共送了 {{count}} 回，{{name}}收到的最多。", ("count", g.Count), ("name", Ctx.Display(g.Recipient)));
		Paragraph(b, times, tx, ref y, w, AisvTheme.Text, 0.8f);
		y += 14;
		if (s.TopRecipient != null)
		{
			Rectangle av = new Rectangle(tx, y, 44, 44);
			Kit.Avatar(b, av, Ctx, s.TopRecipient.Name);
			AisvTheme.DrawText(b, AisvTheme.Truncate(Tr.Get("year.gifts.fan", "收礼最多的人：{{name}}，{{count}} 份。", ("name", Ctx.Display(s.TopRecipient.Name)), ("count", s.TopRecipient.Count)), w - 60, 0.78f), new Vector2(av.Right + 14, y + 10), AisvTheme.Text, 0.78f);
			y = av.Bottom + 18;
		}
		if (s.GiftCount > 0)
		{
			Paragraph(b, Tr.Get("year.gifts.loved", "{{total}} 份礼物里，有 {{loved}} 份正好送到心坎上。", ("total", s.GiftCount), ("loved", s.LovedGifts)), tx, ref y, w, AisvTheme.TextMuted, 0.74f);
			Rectangle bar = new Rectangle(tx, y + 6, Math.Min(w, 420), 14);
			AisvTheme.FillRounded(b, bar, AisvTheme.Divider, 7);
			AisvTheme.FillRounded(b, new Rectangle(bar.X, bar.Y, Math.Max(14, bar.Width * s.LovedGifts / s.GiftCount), bar.Height), theme, 7);
		}
	}

	private static void GiftBox(SpriteBatch b, Rectangle r, Color theme)
	{
		Rectangle body = new Rectangle(r.X + 8, r.Y + 38, r.Width - 16, r.Height - 40);
		Rectangle lid = new Rectangle(r.X, r.Y + 22, r.Width, 22);
		AisvTheme.FillRounded(b, body, theme, 6);
		AisvTheme.FillRounded(b, lid, Color.Lerp(theme, Color.Black, 0.12f), 6);
		AisvTheme.Fill(b, new Rectangle(r.Center.X - 7, lid.Y, 14, body.Bottom - lid.Y), AisvTheme.Gold);
		AisvTheme.FillRounded(b, new Rectangle(r.Center.X - 30, r.Y + 2, 26, 22), AisvTheme.Gold, 11);
		AisvTheme.FillRounded(b, new Rectangle(r.Center.X + 4, r.Y + 2, 26, 22), AisvTheme.Gold, 11);
	}

	private void Gossip(SpriteBatch b, Rectangle r, Color theme)
	{
		GossipFact g = _summary!.TopGossip!;
		List<string> lines = AisvTheme.WrapLines(g.Text, r.Width - 120, 0.95f).Take(6).ToList();
		Rectangle quote = new Rectangle(r.X, r.Y, r.Width, lines.Count * AisvTheme.LineHeight(0.95f) + 60);
		AisvTheme.FillRounded(b, quote, Color.Lerp(theme, Color.White, 0.88f), 16);
		b.DrawString(Game1.dialogueFont, "“", new Vector2(quote.X + 16, quote.Y - 6), theme * 0.6f, 0f, Vector2.Zero, 2f, SpriteEffects.None, 1f);
		int y = quote.Y + 30;
		foreach (string line in lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(quote.X + 80, y), AisvTheme.Text, 0.95f);
			y += AisvTheme.LineHeight(0.95f);
		}
		y = quote.Bottom + 30;
		if (!string.IsNullOrWhiteSpace(g.Origin))
		{
			Rectangle av = new Rectangle(r.X, y, 64, 64);
			Kit.Avatar(b, av, Ctx, g.Origin);
			AisvTheme.DrawText(b, Tr.Get("year.gossip.origin", "最早是从{{name}}那儿传出来的。", ("name", Ctx.Display(g.Origin))), new Vector2(av.Right + 16, y + 18), AisvTheme.Text, 0.82f);
			y = av.Bottom + 24;
		}
		AisvTheme.DrawText(b, Tr.Get("year.gossip.spread", "传到了 {{count}} 位村民耳朵里。", ("count", g.Aware)), new Vector2(r.X, y), AisvTheme.TextMuted, 0.8f);
		y += 40;
		for (int i = 0; i < Math.Min(g.Aware, 40); i++)
		{
			AisvTheme.FillRounded(b, new Rectangle(r.X + i % 20 * 26, y + i / 20 * 26, 18, 18), Color.Lerp(theme, Color.White, 0.25f + i % 3 * 0.15f), 9);
		}
	}

	private void Arcs(SpriteBatch b, Rectangle r, Color theme)
	{
		IReadOnlyList<ArcFact> arcs = _summary!.Arcs;
		int y = r.Y;
		foreach (ArcFact a in arcs.Take(YearReview.ListShown))
		{
			Rectangle row = new Rectangle(r.X, y, r.Width, 76);
			AisvTheme.FillRounded(b, row, Color.Lerp(theme, Color.White, 0.9f), 12);
			AisvTheme.FillRounded(b, new Rectangle(row.X, row.Y, 6, row.Height), theme, 3);
			int ax = row.Right - 16;
			foreach (string npc in a.Npcs.Where(n => !string.IsNullOrWhiteSpace(n)).Take(4).Reverse())
			{
				ax -= 52;
				Kit.Avatar(b, new Rectangle(ax, row.Y + 14, 48, 48), Ctx, npc);
			}
			AisvTheme.Bold(b, AisvTheme.Truncate(a.Title, ax - row.X - 48, 0.9f), new Vector2(row.X + 26, row.Center.Y - 14), AisvTheme.Text, 0.9f);
			y = row.Bottom + 12;
		}
		More(b, arcs.Count, r.X, y);
	}

	private void Milestones(SpriteBatch b, Rectangle r, Color theme)
	{
		IReadOnlyList<MilestoneFact> list = _summary!.Milestones;
		int shown = Math.Min(YearReview.ListShown, list.Count);
		bool more = list.Count > YearReview.ListShown;
		int step = Math.Min(84, (r.Height - (more ? 30 : 0)) / Math.Max(1, shown));
		int av = Math.Min(60, step - 12);
		int y = r.Y;
		int lineX = r.X + 30;
		if (shown > 1)
		{
			AisvTheme.Fill(b, new Rectangle(lineX - 1, y + av / 2, 3, (shown - 1) * step), Color.Lerp(theme, Color.White, 0.5f));
		}
		foreach (MilestoneFact m in list.Take(shown))
		{
			int cy = y + av / 2;
			AisvTheme.FillRounded(b, new Rectangle(lineX - 12, cy - 12, 24, 24), theme, 12);
			AisvTheme.FillRounded(b, new Rectangle(lineX - 5, cy - 5, 10, 10), Color.White, 5);
			Rectangle a = new Rectangle(lineX + 34, y, av, av);
			Kit.Avatar(b, a, Ctx, m.Npc);
			AisvTheme.Bold(b, Ctx.Display(m.Npc), new Vector2(a.Right + 16, cy - 26), AisvTheme.Text, 0.82f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(MilestoneName(m.Type, m.Description), r.Right - a.Right - 160, 0.76f), new Vector2(a.Right + 16, cy + 2), Color.Lerp(theme, AisvTheme.Text, 0.5f), 0.76f);
			string date = DateOf(m.TotalDay);
			AisvTheme.DrawText(b, date, new Vector2(r.Right - AisvTheme.Width(date, 0.68f), cy - 10), AisvTheme.TextFaint, 0.68f);
			y += step;
		}
		More(b, list.Count, r.X, y - 4);
	}

	private void Letters(SpriteBatch b, Rectangle r, Color theme)
	{
		YearSummary s = _summary!;
		Rectangle env = new Rectangle(r.X, r.Y + 10, 180, 120);
		AisvTheme.Bordered(b, env, Color.Lerp(theme, Color.White, 0.85f), theme, 10, 3);
		AisvTheme.Line(b, new Vector2(env.X + 8, env.Y + 10), new Vector2(env.Center.X, env.Center.Y + 8), theme, 3f);
		AisvTheme.Line(b, new Vector2(env.Right - 8, env.Y + 10), new Vector2(env.Center.X, env.Center.Y + 8), theme, 3f);
		AisvTheme.FillRounded(b, new Rectangle(env.Center.X - 14, env.Center.Y - 4, 28, 28), AisvTheme.Badge, 14);
		int tx = env.Right + 36;
		string count = s.LetterCount.ToString();
		b.DrawString(Game1.dialogueFont, count, new Vector2(tx, r.Y), Color.Lerp(theme, AisvTheme.Text, 0.4f), 0f, Vector2.Zero, 1.6f, SpriteEffects.None, 1f);
		AisvTheme.DrawText(b, Tr.Get("year.letters.count", "封信寄到了农场"), new Vector2(tx + Game1.dialogueFont.MeasureString(count).X * 1.6f + 14, r.Y + 40), AisvTheme.TextMuted, 0.85f);
		if (s.TopSender != null)
		{
			int y = r.Y + 92;
			Rectangle av = new Rectangle(tx, y, 56, 56);
			Kit.Avatar(b, av, Ctx, s.TopSender.Name);
			AisvTheme.DrawText(b, Tr.Get("year.letters.top", "写得最勤的是{{name}}，{{count}} 封。", ("name", Ctx.Display(s.TopSender.Name)), ("count", s.TopSender.Count)), new Vector2(av.Right + 16, y + 16), AisvTheme.Text, 0.82f);
		}
	}

	private void Reputation(SpriteBatch b, Rectangle r, Color theme)
	{
		YearSummary s = _summary!;
		// 左边称号徽章，右边四项声望。
		int bw = Math.Min(340, r.Width * 2 / 5);
		Rectangle badge = new Rectangle(r.X, r.Y, bw, Math.Min(r.Height - 30, 230));
		AisvTheme.Bordered(b, badge, AisvTheme.GoldSoft, AisvTheme.Gold, 16, 3);
		Rectangle medal = new Rectangle(badge.Center.X - 40, badge.Y + 22, 80, 80);
		AisvTheme.FillRounded(b, medal, AisvTheme.Gold, 40);
		AisvTheme.FillRounded(b, AisvTheme.Inflate(medal, -10), new Color(255, 226, 120), 30);
		AppIcons.Reviews.Draw(b, medal.Center.X - 16, medal.Center.Y - 16, 2);
		string title = TitleName(s.Title);
		AisvTheme.Bold(b, title, new Vector2(badge.Center.X - AisvTheme.Width(title, 1.05f) / 2f, medal.Bottom + 14), AisvTheme.Text, 1.05f);
		int ty = medal.Bottom + 56;
		Paragraph(b, TitleText(s.Title), badge.X + 20, ref ty, badge.Width - 40, AisvTheme.TextMuted, 0.72f, center: true);
		int x = badge.Right + 40, w = r.Right - x;
		(string Label, float Score, Color Tint)[] bars =
		{
			(Tr.Get("year.rep.generosity", "大方"), s.Reputation.Generosity, new Color(214, 104, 140)),
			(Tr.Get("year.rep.reliability", "靠得住"), s.Reputation.Reliability, new Color(54, 150, 140)),
			(Tr.Get("year.rep.charisma", "讨人喜欢"), s.Reputation.Charisma, new Color(84, 140, 214)),
			(Tr.Get("year.rep.mystery", "神秘"), s.Reputation.Mystery, new Color(146, 100, 200))
		};
		int y = r.Y + 6;
		foreach ((string label, float score, Color tint) in bars)
		{
			AisvTheme.DrawText(b, label, new Vector2(x, y), AisvTheme.Text, 0.8f);
			string tier = TierWord(YearReview.Tier(score));
			AisvTheme.DrawText(b, tier, new Vector2(x + w - AisvTheme.Width(tier, 0.72f), y + 2), AisvTheme.TextMuted, 0.72f);
			Rectangle track = new Rectangle(x, y + 32, w, 14);
			AisvTheme.FillRounded(b, track, AisvTheme.Divider, 7);
			AisvTheme.FillRounded(b, new Rectangle(track.X, track.Y, Math.Max(14, (int)(track.Width * score / 100f)), track.Height), tint, 7);
			y += 58;
		}
		AisvTheme.DrawText(b, Tr.Get("year.rep.note", "按这一年的聊天和送礼算出来的。"), new Vector2(x, y + 2), AisvTheme.TextFaint, 0.66f);
	}

	private static void More(SpriteBatch b, int count, int x, int y)
	{
		if (count > YearReview.ListShown)
		{
			AisvTheme.DrawText(b, Tr.Get("year.more", "还有 {{count}} 个没列出来。", ("count", count - YearReview.ListShown)), new Vector2(x, y + 4), AisvTheme.TextFaint, 0.72f);
		}
	}

	private static void Paragraph(SpriteBatch b, string text, int x, ref int y, int w, Color color, float scale, bool center = false)
	{
		foreach (string line in AisvTheme.WrapLines(text, w, scale))
		{
			float lx = center ? x + (w - AisvTheme.Width(line, scale)) / 2f : x;
			AisvTheme.DrawText(b, line, new Vector2(lx, y), color, scale);
			y += AisvTheme.LineHeight(scale);
		}
	}

	private static string DateOf(int totalDay)
	{
		if (totalDay <= 0)
		{
			return "";
		}
		int d = totalDay - 1;
		return ShellText.Date(d % 28 + 1, (d / 28 % 4) switch { 0 => "spring", 1 => "summer", 2 => "fall", _ => "winter" }, d / 112 + 1);
	}

	private static string TierWord(int tier) => tier switch
	{
		4 => Tr.Get("year.tier.4", "很高"),
		3 => Tr.Get("year.tier.3", "较高"),
		2 => Tr.Get("year.tier.2", "一般"),
		1 => Tr.Get("year.tier.1", "偏低"),
		_ => Tr.Get("year.tier.0", "很低")
	};

	private static string MilestoneName(string type, string description) => type.Trim().ToLowerInvariant() switch
	{
		"first_conversation" => Tr.Get("year.ms.first_conversation", "头一回说上话"),
		"10_conversations" => Tr.Get("year.ms.10_conversations", "聊满 10 次"),
		"50_conversations" => Tr.Get("year.ms.50_conversations", "聊满 50 次"),
		"100_conversations" => Tr.Get("year.ms.100_conversations", "聊满 100 次"),
		"3_hearts" => Tr.Get("year.ms.3_hearts", "到了 3 颗心"),
		"6_hearts" => Tr.Get("year.ms.6_hearts", "到了 6 颗心"),
		"8_hearts" => Tr.Get("year.ms.8_hearts", "到了 8 颗心"),
		"10_hearts" => Tr.Get("year.ms.10_hearts", "到了 10 颗心"),
		"first_loved_gift" => Tr.Get("year.ms.first_loved_gift", "第一次送到心坎上"),
		"first_fight" => Tr.Get("year.ms.first_fight", "第一次闹别扭"),
		"1_year" => Tr.Get("year.ms.1_year", "认识满一年"),
		_ => description.Length > 0 ? description : type
	};

		private static readonly Dictionary<YearTitle, (Func<string> Name, Func<string> Blurb)> Titles = new Dictionary<YearTitle, (Func<string>, Func<string>)>
	{
		[YearTitle.FarmHermit] = (() => Tr.Get("year.title.hermit", "田里的独行侠"), () => Tr.Get("year.titleText.hermit", "这一年，陪你最多的是庄稼。")),
		[YearTitle.ValleySweetheart] = (() => Tr.Get("year.title.sweetheart", "山谷宝贝"), () => Tr.Get("year.titleText.sweetheart", "大方、常来、又会聊天，谁都喜欢你。")),
		[YearTitle.GiftShower] = (() => Tr.Get("year.title.giftShower", "送礼达人"), () => Tr.Get("year.titleText.giftShower", "从你手里送出去的礼物，几乎没有落空的。")),
		[YearTitle.GenerousSoul] = (() => Tr.Get("year.title.generous", "大方的人"), () => Tr.Get("year.titleText.generous", "你送的东西大多挺合人心意。")),
		[YearTitle.TownCharmer] = (() => Tr.Get("year.title.charmer", "小镇人气王"), () => Tr.Get("year.titleText.charmer", "跟你聊天，大家都觉得是种享受。")),
		[YearTitle.GoodCompany] = (() => Tr.Get("year.title.company", "好聊伴"), () => Tr.Get("year.titleText.company", "和你说话总是挺愉快。")),
		[YearTitle.PillarOfTheValley] = (() => Tr.Get("year.title.pillar", "山谷顶梁柱"), () => Tr.Get("year.titleText.pillar", "谁有事，你都在。")),
		[YearTitle.FriendlyNeighbor] = (() => Tr.Get("year.title.neighbor", "热心邻居"), () => Tr.Get("year.titleText.neighbor", "街坊四邻都和你常有来往。")),
		[YearTitle.Enigma] = (() => Tr.Get("year.title.enigma", "神秘农夫"), () => Tr.Get("year.titleText.enigma", "那个农夫到底是什么人？谁也说不清。"))
	};

	private static string TitleName(YearTitle t) => Titles.TryGetValue(t, out var title) ? title.Name() : Tr.Get("year.title.quiet", "安静的农夫");

	private static string TitleText(YearTitle t) => Titles.TryGetValue(t, out var title) ? title.Blurb() : Tr.Get("year.titleText.quiet", "你不太爱凑热闹，这样也挺好。");
}
