using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.Core.World.Ledger;
using AISV.Game.World;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>世界 · 账本：全镇的钱、谁最有钱、本周花销和生意、镇上的钱事、你欠谁的钱。</summary>
internal sealed class TownLedgerPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private Rectangle _view;

	public TownLedgerPage(ShellContext ctx) : base(ctx)
	{
	}

	private LedgerService? Service => Ctx.Ledger;

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		Kit.Header(b, header, Tr.Get("townLedger.title", "小镇账本"), "", null, hits, null);
		_view = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		if (Service is not LedgerService service || !service.Enabled)
		{
			Kit.Empty(b, _view, Tr.Get("townLedger.off", "村民账本没开。"), Tr.Get("townLedger.offHint", "在设置 → 世界里打开。"));
			return;
		}
		IReadOnlyList<NpcWallet> wallets = service.Wallets();
		if (wallets.Count == 0)
		{
			Kit.Empty(b, _view, Tr.Get("townLedger.empty", "账本还是空的。"), Tr.Get("townLedger.emptyHint", "明天早上会给每位村民记上第一笔。"));
			return;
		}
		bool narrow = area.Width < 980;
		int pad = narrow ? 16 : 28;
		AisvTheme.BeginClip(b, _view);
		int x = _view.X + pad, w = _view.Width - pad * 2 - 12;
		int y = _view.Y + 16 - _scroll.Offset;
		y = DrawTotals(b, service, wallets, x, y, w, narrow) + 20;
		if (narrow)
		{
			y = DrawRich(b, service, wallets, x, y, w, hits) + 20;
			y = DrawWeek(b, service, x, y, w) + 20;
			y = DrawEvents(b, service, x, y, w) + 20;
			y = DrawHustlers(b, service, x, y, w, hits) + 20;
			y = DrawDebts(b, service, x, y, w, hits);
		}
		else
		{
			int lw = (w - 24) / 2;
			int left = DrawRich(b, service, wallets, x, y, lw, hits) + 20;
			left = DrawDebts(b, service, x, left, lw, hits);
			int right = DrawWeek(b, service, x + lw + 24, y, lw) + 20;
			right = DrawHustlers(b, service, x + lw + 24, right, lw, hits) + 20;
			right = DrawEvents(b, service, x + lw + 24, right, lw);
			y = Math.Max(left, right);
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y + 16);
	}

	private static int DrawTotals(SpriteBatch b, LedgerService service, IReadOnlyList<NpcWallet> wallets, int x, int y, int w, bool narrow)
	{
		var (spenders, shops) = service.Week();
		(string Label, string Value, Color Color)[] tiles =
		{
			(Tr.Get("townLedger.total", "全镇的钱"), $"{wallets.Sum(v => (long)v.Total):N0}g", AisvTheme.Gold),
			(Tr.Get("townLedger.cash", "大家身上的"), $"{wallets.Sum(v => (long)v.Gold):N0}g", AisvTheme.Text),
			(Tr.Get("townLedger.spent", "本周花出去"), $"{spenders.Sum(s => s.Spent):N0}g", AisvTheme.Danger),
			(Tr.Get("townLedger.sales", "本周店里进账"), $"{shops.Sum(s => s.Sales):N0}g", AisvTheme.Accent)
		};
		int cols = narrow ? 2 : 4, gap = 12;
		int tw = (w - gap * (cols - 1)) / cols;
		for (int i = 0; i < tiles.Length; i++)
		{
			Rectangle r = new Rectangle(x + i % cols * (tw + gap), y + i / cols * (88 + gap), tw, 88);
			AisvTheme.DrawCard(b, r);
			AisvTheme.DrawText(b, tiles[i].Label, new Vector2(r.X + 18, r.Y + 14), AisvTheme.TextMuted, 0.72f);
			AisvTheme.Bold(b, AisvTheme.Truncate(tiles[i].Value, r.Width - 36, 1.05f), new Vector2(r.X + 18, r.Y + 44), tiles[i].Color, 1.05f);
		}
		return y + (tiles.Length + cols - 1) / cols * (88 + gap) - gap;
	}

	private int DrawRich(SpriteBatch b, LedgerService service, IReadOnlyList<NpcWallet> wallets, int x, int y, int w, Hits hits)
	{
		Kit.SectionTitle(b, Tr.Get("townLedger.rich", "谁最有钱"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		List<NpcWallet> top = wallets.OrderByDescending(v => v.Total).Take(10).ToList();
		long max = Math.Max(1, top.FirstOrDefault()?.Total ?? 1);
		for (int i = 0; i < top.Count; i++)
		{
			NpcWallet v = top[i];
			Rectangle row = new Rectangle(x, y, w, 56);
			bool hover = AisvTheme.Hover(row, _view);
			AisvTheme.Bordered(b, row, hover ? AisvTheme.Card : AisvTheme.Page, AisvTheme.Divider, 10);
			AisvTheme.Bold(b, (i + 1).ToString(), new Vector2(row.X + 14, row.Y + 16), i < 3 ? AisvTheme.Gold : AisvTheme.TextFaint, 0.8f);
			Kit.Avatar(b, new Rectangle(row.X + 40, row.Y + 8, 40, 40), Ctx, v.Npc);
			string money = $"{v.Total:N0}g";
			float mw = AisvTheme.Width(money, 0.8f);
			AisvTheme.Bold(b, money, new Vector2(row.Right - 14 - mw, row.Y + 8), AisvTheme.Text, 0.8f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(service.Display(v.Npc), row.Right - 110 - mw - 92, 0.8f), new Vector2(row.X + 92, row.Y + 8), AisvTheme.Text, 0.8f);
			int barX = row.X + 92, barW = row.Right - 14 - barX;
			AisvTheme.Progress(b, new Rectangle(barX, row.Y + 38, barW, 6), v.Total / (float)max, AisvTheme.Gold);
			string npc = v.Npc;
			hits.Add(row, () => Host.Push(new LedgerPage(Ctx, npc)), _view);
			y = row.Bottom + 6;
		}
		return y;
	}

	private static int DrawWeek(SpriteBatch b, LedgerService service, int x, int y, int w)
	{
		var (spenders, shops) = service.Week();
		Kit.SectionTitle(b, Tr.Get("townLedger.week", "这一周"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		Rectangle card = new Rectangle(x, y, w, 40 + (Math.Clamp(spenders.Count, 1, 3) + Math.Clamp(shops.Count, 1, 3)) * 34 + 50);
		AisvTheme.DrawCard(b, card);
		int ty = card.Y + 16;
		AisvTheme.Bold(b, Tr.Get("townLedger.spenders", "花得最多"), new Vector2(card.X + 20, ty), AisvTheme.TextMuted, 0.74f);
		ty += 30;
		foreach ((string npc, int spent) in spenders.Take(3))
		{
			ty = Line(b, service.Display(npc), $"-{spent:N0}g", card.X + 20, ty, w - 40, AisvTheme.Danger);
		}
		if (spenders.Count == 0)
		{
			ty = Line(b, Tr.Get("townLedger.noSpend", "还没人花钱"), "", card.X + 20, ty, w - 40, AisvTheme.TextFaint);
		}
		ty += 8;
		AisvTheme.Bold(b, Tr.Get("townLedger.busiest", "生意最好"), new Vector2(card.X + 20, ty), AisvTheme.TextMuted, 0.74f);
		ty += 30;
		foreach ((string owner, int sales) in shops.Take(3))
		{
			ty = Line(b, service.Display(owner), $"+{sales:N0}g", card.X + 20, ty, w - 40, AisvTheme.Accent);
		}
		if (shops.Count == 0)
		{
			ty = Line(b, Tr.Get("townLedger.noSales", "店里还没进账"), "", card.X + 20, ty, w - 40, AisvTheme.TextFaint);
		}
		return Math.Max(card.Bottom, ty + 10);
	}

	private static int Line(SpriteBatch b, string left, string right, int x, int y, int w, Color color)
	{
		float rw = AisvTheme.Width(right, 0.78f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(left, w - rw - 12, 0.78f), new Vector2(x, y), AisvTheme.Text, 0.78f);
		AisvTheme.Bold(b, right, new Vector2(x + w - rw, y), color, 0.78f);
		return y + 34;
	}

	private int DrawEvents(SpriteBatch b, LedgerService service, int x, int y, int w)
	{
		Kit.SectionTitle(b, Tr.Get("townLedger.events", "镇上的钱事"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		IReadOnlyList<TownEvent> events = service.Events();
		if (events.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("townLedger.noEvents", "最近没什么大事。"), new Vector2(x, y), AisvTheme.TextFaint, 0.78f);
			return y + 30;
		}
		int today = (Game1.Date?.TotalDays ?? 0) + 1;
		foreach (TownEvent e in events.Take(12))
		{
			List<string> lines = AisvTheme.WrapLines(ShellText.Clean(e.Text), w - 90, 0.76f);
			Rectangle row = new Rectangle(x, y, w, 20 + lines.Count * AisvTheme.LineHeight(0.76f));
			if (row.Bottom >= _view.Y && row.Y <= _view.Bottom)
			{
				string when = today - e.Day <= 0 ? Tr.Get("townLedger.today", "今天") : Tr.Get("townLedger.ago", "{{n}} 天前", ("n", today - e.Day));
				AisvTheme.DrawText(b, when, new Vector2(row.X, row.Y + 10), AisvTheme.TextFaint, 0.66f);
				int ty = row.Y + 10;
				foreach (string line in lines)
				{
					AisvTheme.DrawText(b, line, new Vector2(row.X + 80, ty), AisvTheme.Text, 0.76f);
					ty += AisvTheme.LineHeight(0.76f);
				}
				AisvTheme.HLine(b, row.X, row.Bottom - 1, row.Width, AisvTheme.Divider, 1);
			}
			y = row.Bottom + 4;
		}
		return y;
	}

	private int DrawDebts(SpriteBatch b, LedgerService service, int x, int y, int w, Hits hits)
	{
		List<Debt> open = service.Debts().Where(d => d.State == DebtStates.Open).ToList();
		Kit.SectionTitle(b, Tr.Get("townLedger.debts", "你欠的钱"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		if (open.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("townLedger.noDebts", "谁的钱也不欠。"), new Vector2(x, y), AisvTheme.TextFaint, 0.78f);
			return y + 30;
		}
		int today = (Game1.Date?.TotalDays ?? 0) + 1;
		foreach (Debt d in open.OrderBy(d => d.DueDay))
		{
			Rectangle row = new Rectangle(x, y, w, 60);
			bool late = today > d.DueDay;
			AisvTheme.Bordered(b, row, late ? new Color(250, 226, 222) : AisvTheme.Card, late ? AisvTheme.Danger * 0.6f : AisvTheme.Divider, 10);
			Kit.Avatar(b, new Rectangle(row.X + 12, row.Y + 10, 40, 40), Ctx, d.Npc);
			AisvTheme.Bold(b, $"{service.Display(d.Npc)} · {d.Owed:N0}g", new Vector2(row.X + 64, row.Y + 8), AisvTheme.Text, 0.8f);
			string due = late ? Tr.Get("townLedger.late", "过期 {{n}} 天", ("n", today - d.DueDay)) : d.DueDay == today ? Tr.Get("townLedger.dueToday", "今天到期") : Tr.Get("townLedger.due", "{{n}} 天后到期", ("n", d.DueDay - today));
			AisvTheme.DrawText(b, due, new Vector2(row.X + 64, row.Y + 34), late ? AisvTheme.Danger : AisvTheme.TextMuted, 0.66f);
			string npc = d.Npc;
			hits.Add(row, () => Host.Push(new LedgerPage(Ctx, npc, LedgerPage.View.Loan)), _view);
			y = row.Bottom + 6;
		}
		return y;
	}

	/// <summary>手头紧的人在想什么办法，村民之间谁欠谁。</summary>
	private int DrawHustlers(SpriteBatch b, LedgerService service, int x, int y, int w, Hits hits)
	{
		Kit.SectionTitle(b, Tr.Get("townLedger.hustlers", "手头紧的人"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		IReadOnlyList<NpcWallet> hustlers = service.Hustlers();
		int today = (Game1.Date?.TotalDays ?? 0) + 1;
		if (hustlers.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("townLedger.noHustlers", "最近没人缺钱。"), new Vector2(x, y), AisvTheme.TextFaint, 0.78f);
			y += 30;
		}
		foreach (NpcWallet v in hustlers.Take(8))
		{
			Hustle h = v.Hustle!;
			string text = h.Done ? h.Note : Tr.Get("townLedger.planning", "今天打算{{plan}}", ("plan", h.Plan));
			List<string> lines = AisvTheme.WrapLines(ShellText.Clean(text), w - 80, 0.72f).Take(2).ToList();
			Rectangle row = new Rectangle(x, y, w, Math.Max(60, 40 + lines.Count * AisvTheme.LineHeight(0.72f)));
			AisvTheme.Bordered(b, row, AisvTheme.Card, AisvTheme.Divider, 10);
			Kit.Avatar(b, new Rectangle(row.X + 12, row.Y + 10, 40, 40), Ctx, v.Npc);
			string when = today - h.Day <= 0 ? Tr.Get("townLedger.today", "今天") : Tr.Get("townLedger.ago", "{{n}} 天前", ("n", today - h.Day));
			AisvTheme.Bold(b, service.Display(v.Npc), new Vector2(row.X + 64, row.Y + 8), AisvTheme.Text, 0.8f);
			float ww = AisvTheme.Width(when, 0.66f);
			AisvTheme.DrawText(b, when, new Vector2(row.Right - 14 - ww, row.Y + 10), AisvTheme.TextFaint, 0.66f);
			int ty = row.Y + 34;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(row.X + 64, ty), AisvTheme.TextMuted, 0.72f);
				ty += AisvTheme.LineHeight(0.72f);
			}
			string npc = v.Npc;
			hits.Add(row, () => Host.Push(new LedgerPage(Ctx, npc)), _view);
			y = row.Bottom + 6;
		}
		List<NpcDebt> debts = service.NpcDebts().Where(d => d.State == NpcDebtStates.Open).OrderBy(d => d.DueDay).ToList();
		if (debts.Count == 0)
		{
			return y;
		}
		y += 14;
		Kit.SectionTitle(b, Tr.Get("townLedger.npcDebts", "村民之间的债"), x, y);
		y += AisvTheme.LineHeight(0.85f) + 10;
		foreach (NpcDebt d in debts.Take(8))
		{
			bool late = today > d.DueDay;
			string line = Tr.Get("townLedger.npcDebt", "{{b}}欠{{a}} {{gold}}g", ("a", service.Display(d.Lender)), ("b", service.Display(d.Borrower)), ("gold", d.Amount));
			string due = late ? Tr.Get("townLedger.late", "过期 {{n}} 天", ("n", today - d.DueDay)) : d.DueDay == today ? Tr.Get("townLedger.dueToday", "今天到期") : Tr.Get("townLedger.due", "{{n}} 天后到期", ("n", d.DueDay - today));
			float dw = AisvTheme.Width(due, 0.7f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(line, w - dw - 16, 0.76f), new Vector2(x, y), AisvTheme.Text, 0.76f);
			AisvTheme.DrawText(b, due, new Vector2(x + w - dw, y + 2), late ? AisvTheme.Danger : AisvTheme.TextMuted, 0.7f);
			y += AisvTheme.LineHeight(0.76f) + 8;
		}
		return y;
	}
}
