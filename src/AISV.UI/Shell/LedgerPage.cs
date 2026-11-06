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
using StardewValley.ItemTypeDefinitions;

namespace AISV.UI.Shell;

/// <summary>一位村民的家当：随身、家里、账目、借钱。</summary>
internal sealed class LedgerPage : ShellPage
{
	public enum View
	{
		Carried,
		Home,
		Log,
		Loan
	}

	private readonly string _npc;

	private readonly Scroller _scroll = new Scroller();

	private View _view;

	private Rectangle _body;

	private (Rectangle Bounds, string Text)? _tip;

	private int _amount;

	private int _days = 7;

	private string _answer = "";

	private bool _waiting;

	public LedgerPage(ShellContext ctx, string npc, View view = View.Carried) : base(ctx)
	{
		_npc = npc;
		_view = view;
	}

	private LedgerService? Service => Ctx.Ledger;

	public override string Title => Tr.Get("ledgerPage.title", "{{name}}的家当", ("name", Ctx.Display(_npc)));

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _body.Height - 80);

	private static string Label(View v) => v switch
	{
		View.Carried => Tr.Get("ledgerPage.carried", "随身"),
		View.Home => Tr.Get("ledgerPage.home", "家里"),
		View.Log => Tr.Get("ledgerPage.log", "账目"),
		_ => Tr.Get("ledgerPage.loan", "借钱")
	};

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_tip = null;
		if (Service is not LedgerService service || !service.Enabled || service.Wallet(_npc) is not NpcWallet w)
		{
			Kit.Empty(b, area, Tr.Get("ledgerPage.none", "看不到这位村民的家当。"), Tr.Get("ledgerPage.noneHint", "村民账本没开，或者联机时你不是房主。"));
			return;
		}
		bool narrow = area.Width < 900;
		int pad = narrow ? 16 : 28;
		Rectangle head = new Rectangle(area.X + pad, area.Y + 16, area.Width - pad * 2, 104);
		DrawHead(b, service, w, head);
		int tx = head.X;
		int ty = head.Bottom + 14;
		foreach (View v in Enum.GetValues<View>())
		{
			string label = Label(v);
			int tw = (int)AisvTheme.Width(label, 0.85f) + 40;
			Rectangle r = new Rectangle(tx, ty, tw, 42);
			bool sel = v == _view;
			AisvTheme.Bordered(b, r, sel ? AisvTheme.AccentSoft : AisvTheme.Hover(r) ? AisvTheme.Card : AisvTheme.Page, sel ? AisvTheme.Accent : AisvTheme.CardBorder, 21);
			AisvTheme.TextCentered(b, label, r, sel ? AisvTheme.AccentDark : AisvTheme.TextMuted, 0.85f);
			hits.Add(r, () =>
			{
				if (_view != v)
				{
					Game1.playSound("smallSelect");
					_view = v;
					_scroll.Reset();
				}
			});
			tx = r.Right + 8;
		}
		_body = new Rectangle(area.X, ty + 56, area.Width, area.Bottom - ty - 56);
		AisvTheme.BeginClip(b, _body);
		int x = _body.X + pad, cw = _body.Width - pad * 2 - 12;
		int y = _body.Y + 8 - _scroll.Offset;
		y = _view switch
		{
			View.Carried => Grid(b, w.Carried, LedgerRules.MaxCarried, x, y, cw),
			View.Home => Grid(b, w.Home, LedgerRules.MaxHome, x, y, cw),
			View.Log => DrawLog(b, w, x, y, cw),
			_ => DrawLoan(b, service, w, x, y, cw, hits)
		};
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _body, y + 16);
		if (_tip is var (bounds, text))
		{
			Tooltip(b, bounds, text);
		}
	}

	private void DrawHead(SpriteBatch b, LedgerService service, NpcWallet w, Rectangle r)
	{
		AisvTheme.DrawCard(b, r);
		Rectangle face = new Rectangle(r.X + 20, r.Y + 16, 72, 72);
		Kit.Avatar(b, face, Ctx, _npc);
		int tx = face.Right + 18;
		Occupation job = service.Job(_npc);
		int right = r.Right - 24;
		right = Money(b, Tr.Get("ledgerPage.savings", "存款"), w.Savings, right, r.Y + 22, AisvTheme.TextMuted) - 28;
		right = Money(b, Tr.Get("ledgerPage.gold", "身上"), w.Gold, right, r.Y + 22, AisvTheme.Gold);
		int nameW = Math.Max(40, right - 16 - tx);
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(_npc), nameW, 1f), new Vector2(tx, r.Y + 18), AisvTheme.Text, 1f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(job.Job + (job.Wage > 0 ? Tr.Get("ledgerPage.wage", " · 周薪 {{n}}g", ("n", job.Wage)) : ""), nameW, 0.72f), new Vector2(tx, r.Y + 54), AisvTheme.TextMuted, 0.72f);
	}

	/// <summary>右对齐画一组"标题 + 金额"，返回左边的 x。</summary>
	private static int Money(SpriteBatch b, string label, int amount, int right, int y, Color color)
	{
		string value = $"{amount:N0}g";
		float w = Math.Max(AisvTheme.Width(value, 1f), AisvTheme.Width(label, 0.7f));
		AisvTheme.DrawText(b, label, new Vector2(right - AisvTheme.Width(label, 0.7f), y), AisvTheme.TextFaint, 0.7f);
		AisvTheme.Bold(b, value, new Vector2(right - AisvTheme.Width(value, 1f), y + 26), color, 1f);
		return right - (int)w;
	}

	private int Grid(SpriteBatch b, List<LedgerItem> items, int slots, int x, int y, int w)
	{
		const int cell = 72, gap = 10;
		int cols = Math.Max(1, (w + gap) / (cell + gap));
		string count = Tr.Get("ledgerPage.slots", "{{n}}/{{max}} 格", ("n", items.Count), ("max", slots));
		AisvTheme.DrawText(b, count, new Vector2(x, y), AisvTheme.TextFaint, 0.7f);
		y += AisvTheme.LineHeight(0.7f) + 10;
		for (int i = 0; i < slots; i++)
		{
			Rectangle r = new Rectangle(x + i % cols * (cell + gap), y + i / cols * (cell + gap), cell, cell);
			if (r.Bottom < _body.Y || r.Y > _body.Bottom)
			{
				continue;
			}
			LedgerItem? item = i < items.Count ? items[i] : null;
			bool hover = item != null && AisvTheme.Hover(r, _body);
			AisvTheme.Bordered(b, r, item == null ? AisvTheme.Page : hover ? AisvTheme.Card : AisvTheme.PageDeep, hover ? AisvTheme.Gold : AisvTheme.CardBorder, 10);
			if (item == null)
			{
				continue;
			}
			ParsedItemData? data = ItemRegistry.GetData(item.Id);
			if (data != null && !data.IsErrorItem)
			{
				b.Draw(data.GetTexture(), AisvTheme.Inflate(r, -12), data.GetSourceRect(), Color.White);
			}
			if (item.Quality > 0)
			{
				Color star = item.Quality >= 4 ? new Color(180, 90, 200) : item.Quality == 2 ? AisvTheme.Gold : new Color(170, 170, 180);
				AisvTheme.FillRounded(b, new Rectangle(r.X + 6, r.Bottom - 16, 10, 10), star, 5);
			}
			if (item.Count > 1)
			{
				string n = item.Count.ToString();
				AisvTheme.Bold(b, n, new Vector2(r.Right - 8 - AisvTheme.Width(n, 0.62f), r.Bottom - 6 - AisvTheme.LineHeight(0.62f)), AisvTheme.Text, 0.62f);
			}
			if (hover)
			{
				Item made = ItemRegistry.Create(item.Id, 1, item.Quality);
				_tip = (r, $"{LedgerService.ItemName(item.Id)}{(item.Count > 1 ? " ×" + item.Count : "")} · {Math.Max(0, made.sellToStorePrice())}g");
			}
		}
		return y + (slots + cols - 1) / cols * (cell + gap);
	}

	private int DrawLog(SpriteBatch b, NpcWallet w, int x, int y, int width)
	{
		if (w.Log.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("ledgerPage.noLog", "还没有记过账。"), new Vector2(x, y), AisvTheme.TextMuted, 0.8f);
			return y + 30;
		}
		int lastDay = int.MinValue;
		int rowH = 50;
		foreach (LedgerEntry e in w.Log)
		{
			if (e.Day != lastDay)
			{
				lastDay = e.Day;
				y += 6;
				AisvTheme.Bold(b, DayLabel(e.Day), new Vector2(x, y), AisvTheme.AccentDark, 0.75f);
				y += AisvTheme.LineHeight(0.75f) + 6;
			}
			Rectangle row = new Rectangle(x, y, width, rowH);
			if (row.Bottom >= _body.Y && row.Y <= _body.Bottom)
			{
				AisvTheme.Bordered(b, row, AisvTheme.Card, AisvTheme.Divider, 10);
				string kind = LedgerRules.KindLabel(e.Kind);
				Rectangle chip = AisvTheme.Chip(b, new Vector2(row.X + 14, row.Y + 13), kind, e.Amount >= 0 ? AisvTheme.AccentSoft : AisvTheme.Divider, e.Amount >= 0 ? AisvTheme.AccentDark : AisvTheme.TextMuted, 0.62f);
				string amount = e.Amount == 0 ? "" : $"{e.Amount:+#,0;-#,0}g";
				float aw = AisvTheme.Width(amount, 0.85f);
				AisvTheme.Bold(b, amount, new Vector2(row.Right - 16 - aw, row.Y + 12), e.Amount >= 0 ? AisvTheme.Accent : AisvTheme.Danger, 0.85f);
				AisvTheme.DrawText(b, AisvTheme.Truncate(ShellText.Clean(e.Note), row.Right - 32 - aw - chip.Right, 0.75f), new Vector2(chip.Right + 12, row.Y + 13), AisvTheme.Text, 0.75f);
			}
			y += rowH + 6;
		}
		return y;
	}

	private static string DayLabel(int day)
	{
		int today = (Game1.Date?.TotalDays ?? 0) + 1;
		return (today - day) switch
		{
			0 => Tr.Get("ledgerPage.today", "今天"),
			1 => Tr.Get("ledgerPage.yesterday", "昨天"),
			int n => Tr.Get("ledgerPage.daysAgo", "{{n}} 天前", ("n", n))
		};
	}

	private int DrawLoan(SpriteBatch b, LedgerService service, NpcWallet w, int x, int y, int width, Hits hits)
	{
		int cardW = Math.Min(width, 760);
		Debt? debt = service.OpenDebt(_npc);
		if (debt != null)
		{
			int today = (Game1.Date?.TotalDays ?? 0) + 1;
			int left = debt.DueDay - today;
			Rectangle card = new Rectangle(x, y, cardW, 190);
			AisvTheme.DrawCard(b, card, highlight: left < 0);
			AisvTheme.Bold(b, Tr.Get("ledgerPage.owe", "你欠{{name}} {{gold}}g", ("name", Ctx.Display(_npc)), ("gold", debt.Owed)), new Vector2(card.X + 24, card.Y + 20), left < 0 ? AisvTheme.Danger : AisvTheme.Text, 1f);
			string due = left > 0 ? Tr.Get("ledgerPage.dueIn", "{{n}} 天后到期", ("n", left)) : left == 0 ? Tr.Get("ledgerPage.dueToday", "今天到期") : Tr.Get("ledgerPage.overdue", "已经过期 {{n}} 天", ("n", -left));
			string owedLine = debt.Tab
				? Tr.Get("ledgerPage.tab", "赊了 {{gold}}g 的账，已还 {{paid}}g · {{due}}", ("gold", debt.Amount), ("paid", debt.Repaid), ("due", due))
				: Tr.Get("ledgerPage.borrowed", "借了 {{gold}}g，已还 {{paid}}g · {{due}}", ("gold", debt.Amount), ("paid", debt.Repaid), ("due", due));
			AisvTheme.DrawText(b, owedLine, new Vector2(card.X + 24, card.Y + 60), AisvTheme.TextMuted, 0.78f);
			AisvTheme.DrawText(b, StyleNote(debt.Style), new Vector2(card.X + 24, card.Y + 92), AisvTheme.Warn, 0.7f);
			Rectangle all = new Rectangle(card.X + 24, card.Bottom - 20 - 48, 200, 48);
			Rectangle part = new Rectangle(all.Right + 12, all.Y, 160, 48);
			bool canAll = Game1.player.Money >= debt.Owed;
			AisvTheme.Button(b, all, Tr.Get("ledgerPage.repayAll", "全部还清"), primary: true, hover: AisvTheme.Hover(all, _body), disabled: !canAll, scale: 0.8f);
			if (canAll)
			{
				hits.Add(all, () => Repay(service, debt.Owed), _body);
			}
			int half = Math.Min(debt.Owed, Math.Max(10, debt.Owed / 2 / 10 * 10));
			bool canPart = Game1.player.Money >= half && half < debt.Owed;
			AisvTheme.Button(b, part, Tr.Get("ledgerPage.repayPart", "先还 {{n}}g", ("n", half)), hover: AisvTheme.Hover(part, _body), disabled: !canPart, scale: 0.8f);
			if (canPart)
			{
				hits.Add(part, () => Repay(service, half), _body);
			}
			y = card.Bottom + 16;
		}
		else
		{
			int limit = service.LoanLimit(_npc);
			_amount = Math.Clamp(_amount <= 0 ? Math.Min(limit, 500) / 10 * 10 : _amount, 10, Math.Max(10, limit));
			string daysLabel = Tr.Get("ledgerPage.days", "几天还");
			int pillsW = LedgerRules.LoanDays.Sum(d => (int)AisvTheme.Width(Tr.Get("ledgerPage.dayN", "{{n}} 天", ("n", d)), 0.75f) + 28 + 6);
			int stepperEnd = x + 130 + 40 + 4 + 130 + 4 + 40;
			bool wrap = stepperEnd + 30 + (int)AisvTheme.Width(daysLabel, 0.8f) + 14 + pillsW > x + cardW - 24;
			Rectangle card = new Rectangle(x, y, cardW, wrap ? 310 : 250);
			AisvTheme.DrawCard(b, card);
			AisvTheme.Bold(b, Tr.Get("ledgerPage.askLoan", "向{{name}}借钱", ("name", Ctx.Display(_npc))), new Vector2(card.X + 24, card.Y + 20), AisvTheme.Text, 1f);
			string? problem = service.LoanProblem(_npc, _amount);
			string hint = limit >= 10 ? Tr.Get("ledgerPage.limit", "看交情和他身上的钱，最多能借 {{n}}g。", ("n", limit)) : problem ?? "";
			AisvTheme.DrawText(b, hint, new Vector2(card.X + 24, card.Y + 58), AisvTheme.TextMuted, 0.74f);
			int cy = card.Y + 120;
			AisvTheme.DrawText(b, Tr.Get("ledgerPage.amount", "借多少"), new Vector2(card.X + 24, cy - 12), AisvTheme.TextMuted, 0.8f);
			int end = Stepper(b, card.X + 130, cy, $"{_amount}g", 130, hits, () => StepAmount(-1, limit), () => StepAmount(1, limit));
			int lx = wrap ? card.X + 24 : end + 30;
			if (wrap)
			{
				cy += 60;
			}
			AisvTheme.DrawText(b, daysLabel, new Vector2(lx, cy - 12), AisvTheme.TextMuted, 0.8f);
			int dx = wrap ? card.X + 130 : lx + (int)AisvTheme.Width(daysLabel, 0.8f) + 14;
			foreach (int d in LedgerRules.LoanDays)
			{
				string label = Tr.Get("ledgerPage.dayN", "{{n}} 天", ("n", d));
				Rectangle r = new Rectangle(dx, cy - 19, (int)AisvTheme.Width(label, 0.75f) + 28, 38);
				bool sel = d == _days;
				AisvTheme.Bordered(b, r, sel ? AisvTheme.GoldSoft : AisvTheme.Page, sel ? AisvTheme.Gold : AisvTheme.CardBorder, 19);
				AisvTheme.TextCentered(b, label, r, sel ? AisvTheme.Gold : AisvTheme.TextMuted, 0.75f);
				int days = d;
				hits.Add(r, () =>
				{
					if (!_waiting)
					{
						_days = days;
					}
				}, _body);
				dx = r.Right + 6;
			}
			Rectangle ask = new Rectangle(card.X + 24, card.Bottom - 20 - 50, 200, 50);
			bool can = problem == null && !_waiting;
			AisvTheme.Button(b, ask, _waiting ? Tr.Get("ledgerPage.asking", "等他回话…") : Tr.Get("ledgerPage.ask", "开口借"), primary: true, hover: AisvTheme.Hover(ask, _body), disabled: !can, scale: 0.85f);
			if (can)
			{
				hits.Add(ask, () => Ask(service), _body);
			}
			if (problem != null && limit >= 10)
			{
				AisvTheme.DrawText(b, AisvTheme.Truncate(problem, card.Right - 24 - ask.Right - 16, 0.7f), new Vector2(ask.Right + 16, ask.Y + 14), AisvTheme.Warn, 0.7f);
			}
			y = card.Bottom + 16;
		}
		if (_answer.Length > 0)
		{
			List<string> lines = AisvTheme.WrapLines("“" + ShellText.Clean(_answer) + "”", cardW - 48, 0.85f);
			Rectangle bubble = new Rectangle(x, y, cardW, 28 + lines.Count * AisvTheme.LineHeight(0.85f));
			AisvTheme.Bordered(b, bubble, AisvTheme.GoldSoft, AisvTheme.Gold * 0.5f);
			int ly = bubble.Y + 14;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(bubble.X + 24, ly), AisvTheme.Text, 0.85f);
				ly += AisvTheme.LineHeight(0.85f);
			}
			y = bubble.Bottom + 16;
		}
		foreach (Debt d in service.Debts().Where(d => d.Npc == _npc && d.State != DebtStates.Open).OrderByDescending(d => d.ClosedDay).Take(5))
		{
			string state = d.State switch
			{
				DebtStates.Paid => Tr.Get("ledgerPage.paid", "还清了"),
				DebtStates.Collected => Tr.Get("ledgerPage.collected", "被他自己拿回去了"),
				_ => Tr.Get("ledgerPage.forgiven", "不了了之")
			};
			AisvTheme.DrawText(b, Tr.Get("ledgerPage.past", "{{when}}借过 {{gold}}g，{{state}}", ("when", DayLabel(d.Day)), ("gold", d.Amount), ("state", state)), new Vector2(x + 4, y), AisvTheme.TextFaint, 0.72f);
			y += AisvTheme.LineHeight(0.72f) + 6;
		}
		return y;
	}

	private static string StyleNote(string style) => style switch
	{
		DebtStyles.Demand => Tr.Get("ledgerPage.style.demand", "过期不还他会自己来拿——先拿钱，不够拿你包里的东西。"),
		DebtStyles.Quiet => Tr.Get("ledgerPage.style.quiet", "过期了他不会开口，但心里会越来越不舒服。"),
		_ => Tr.Get("ledgerPage.style.remind", "过期了他会来信、发消息提醒你。")
	};

	private void StepAmount(int delta, int limit)
	{
		if (_waiting)
		{
			return;
		}
		int step = Game1.input.GetKeyboardState().IsKeyDown(Keys.LeftShift) ? 100 : 10;
		int next = Math.Clamp(_amount + delta * step, 10, Math.Max(10, limit));
		Game1.playSound(next == _amount ? "cancel" : "shwip");
		_amount = next;
	}

	private void Ask(LedgerService service)
	{
		_waiting = true;
		_answer = "";
		Game1.playSound("bigSelect");
		int amount = _amount;
		service.RequestLoan(_npc, amount, _days, (ok, line) =>
		{
			_waiting = false;
			_answer = line;
			Game1.playSound(ok ? "purchase" : "cancel");
			if (ok)
			{
				Host.Notice(Tr.Get("ledgerPage.got", "从{{name}}那儿借到了 {{gold}}g", ("name", Ctx.Display(_npc)), ("gold", amount)));
			}
		});
	}

	private void Repay(LedgerService service, int amount)
	{
		string? problem = service.Repay(_npc, amount);
		Game1.playSound(problem == null ? "purchase" : "cancel");
		Host.Notice(problem ?? Tr.Get("ledgerPage.repaid", "还了 {{n}}g", ("n", amount)));
	}

	private int Stepper(SpriteBatch b, int x, int cy, string label, int w, Hits hits, Action prev, Action next)
	{
		Rectangle left = new Rectangle(x, cy - 20, 40, 40);
		Rectangle mid = new Rectangle(left.Right + 4, cy - 22, w, 44);
		Rectangle right = new Rectangle(mid.Right + 4, cy - 20, 40, 40);
		AisvTheme.FillRounded(b, left, AisvTheme.Hover(left, _body) ? AisvTheme.AccentSoft : AisvTheme.PageDeep, 20);
		AisvTheme.FillRounded(b, right, AisvTheme.Hover(right, _body) ? AisvTheme.AccentSoft : AisvTheme.PageDeep, 20);
		AisvTheme.Chevron(b, new Vector2(left.Center.X + 2, cy), left: true, AisvTheme.AccentDark);
		AisvTheme.Chevron(b, new Vector2(right.Center.X - 2, cy), left: false, AisvTheme.AccentDark);
		AisvTheme.Bordered(b, mid, AisvTheme.Card, AisvTheme.CardBorder, 10);
		AisvTheme.TextCentered(b, label, mid, AisvTheme.Text, 0.85f);
		hits.Add(left, prev, _body);
		hits.Add(right, next, _body);
		return right.Right;
	}

	internal static void Tooltip(SpriteBatch b, Rectangle anchor, string text)
	{
		int w = (int)AisvTheme.Width(text, 0.7f) + 24;
		Rectangle r = new Rectangle(Math.Min(anchor.X, Game1.uiViewport.Width - w - 8), anchor.Bottom + 6, w, 34);
		if (r.Bottom > Game1.uiViewport.Height - 8)
		{
			r.Y = anchor.Y - 40;
		}
		AisvTheme.DrawShadow(b, r, 8, 3);
		AisvTheme.Bordered(b, r, AisvTheme.Text, AisvTheme.Text, 8);
		AisvTheme.TextCentered(b, text, r, Color.White, 0.7f);
	}
}
