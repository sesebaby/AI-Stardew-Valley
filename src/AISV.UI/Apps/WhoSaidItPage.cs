using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>谁说的：给一句镇上真说过的话，从四个人里猜是谁；一轮五题，最新的八卦打头。</summary>
internal sealed class WhoSaidItPage : ShellPage
{
	private readonly Random _random = new Random();

	private List<Quote> _quotes = new List<Quote>();

	private List<QuizQuestion> _round = new List<QuizQuestion>();

	private int _index;

	private string? _picked;

	private int _right;

	private bool _started;

	public WhoSaidItPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("who.title", "谁说的");

	public override void Reload()
	{
		if (!_started)
		{
			_quotes = AppData.Quotes(Ctx);
		}
	}

	private void Start()
	{
		List<string> villagers = Utility.getAllVillagers().Where(n => n != null && n.IsVillager && n.CanSocialize && Ctx.Npcs.IsEnabled(n.Name)).Select(n => n.Name).ToList();
		_round = WhoSaidIt.Round(_quotes, villagers, _random);
		_index = 0;
		_right = 0;
		_picked = null;
		_started = _round.Count > 0;
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		int w = Math.Min(860, area.Width - 48);
		int x = area.Center.X - w / 2;
		int y = area.Y + 28;
		if (!_started)
		{
			Intro(b, x, y, w, hits);
			return;
		}
		if (_index >= _round.Count)
		{
			Finish(b, x, y, w, hits);
			return;
		}
		QuizQuestion q = _round[_index];
		AisvTheme.DrawText(b, Tr.Get("who.progress", "第 {{n}} / {{total}} 题 · 答对 {{right}}", ("n", _index + 1), ("total", _round.Count), ("right", _right)), new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
		y += 40;
		List<string> lines = AisvTheme.WrapLines("“" + q.Quote.Text + "”", w - 80, 1.05f).Take(4).ToList();
		int lh = AisvTheme.LineHeight(1.05f);
		Rectangle card = new Rectangle(x, y, w, 90 + 4 * lh);
		AisvTheme.DrawShadow(b, card, 16, 5);
		AisvTheme.Bordered(b, card, AisvTheme.Card, AisvTheme.CardBorder, 16);
		int ty = card.Y + 28 + (4 - lines.Count) * lh / 2;
		foreach (string line in lines)
		{
			AisvTheme.TextCentered(b, line, new Rectangle(card.X, ty, card.Width, AisvTheme.LineHeight(1.05f)), AisvTheme.Text, 1.05f);
			ty += AisvTheme.LineHeight(1.05f);
		}
		int ago = AppData.Today - q.Quote.Day;
		string where = WhoSaidIt.SourceLabel(q.Quote.Source) + " · " + (ago <= 0 ? Tr.Get("townLedger.today", "今天") : Tr.Get("townLedger.ago", "{{n}} 天前", ("n", ago)));
		AisvTheme.TextCentered(b, where, new Rectangle(card.X, card.Bottom - 44, card.Width, 30), AisvTheme.TextFaint, 0.68f);
		y = card.Bottom + 28;
		int cols = w >= 600 ? 2 : 1;
		int cw = (w - 16 * (cols - 1)) / cols;
		for (int i = 0; i < q.Choices.Count; i++)
		{
			string npc = q.Choices[i];
			Rectangle r = new Rectangle(x + i % cols * (cw + 16), y + i / cols * 86, cw, 72);
			bool answer = npc.Equals(q.Answer, StringComparison.OrdinalIgnoreCase);
			bool chosen = npc.Equals(_picked, StringComparison.OrdinalIgnoreCase);
			Color fill = _picked == null ? (AisvTheme.Hover(r) ? AisvTheme.PageDeep : AisvTheme.Card)
				: answer ? AisvTheme.AccentSoft : chosen ? new Color(250, 226, 222) : AisvTheme.Card;
			Color border = _picked == null ? AisvTheme.CardBorder : answer ? AisvTheme.Accent : chosen ? AisvTheme.Danger : AisvTheme.Divider;
			AisvTheme.Bordered(b, r, fill, border, 14);
			Kit.Avatar(b, new Rectangle(r.X + 14, r.Y + 12, 48, 48), Ctx, npc);
			AisvTheme.Bold(b, Ctx.Display(npc), new Vector2(r.X + 76, r.Y + 22), AisvTheme.Text, 0.9f);
			if (_picked != null && (answer || chosen))
			{
				string mark = answer ? Tr.Get("who.right", "对") : Tr.Get("who.wrong", "错");
				AisvTheme.Bold(b, mark, new Vector2(r.Right - 40, r.Y + 20), answer ? AisvTheme.Accent : AisvTheme.Danger, 1.0f);
			}
			if (_picked == null)
			{
				hits.Add(r, () =>
				{
					_picked = npc;
					if (answer)
					{
						_right++;
					}
					Game1.playSound(answer ? "reward" : "cancel");
				});
			}
		}
		y += (q.Choices.Count + cols - 1) / cols * 86 + 12;
		if (_picked != null)
		{
			string label = _index + 1 < _round.Count ? Tr.Get("who.next", "下一题") : Tr.Get("who.result", "看结果");
			Rectangle btn = new Rectangle(x + w - 200, y, 200, 50);
			AisvTheme.Button(b, btn, label, primary: true, hover: AisvTheme.Hover(btn));
			hits.Add(btn, () =>
			{
				_index++;
				_picked = null;
				Game1.playSound("smallSelect");
			});
		}
	}

	private void Intro(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		Rectangle card = new Rectangle(x, y + 30, w, 300);
		AisvTheme.DrawCard(b, card);
		AisvTheme.TextCentered(b, Tr.Get("who.headline", "这句话是谁说的？"), new Rectangle(card.X, card.Y + 40, card.Width, 50), AisvTheme.Text, 1.2f);
		AisvTheme.TextCentered(b, Tr.Get("who.intro", "题目全是镇上的人真说过的话：传开的八卦、居民群、信使，还有当面说的。"), new Rectangle(card.X + 30, card.Y + 110, card.Width - 60, 40), AisvTheme.TextMuted, 0.74f);
		bool enough = _quotes.Select(q => q.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2 && _quotes.Count >= 3;
		AisvTheme.TextCentered(b, enough ? Tr.Get("who.pool", "攒了 {{n}} 句话", ("n", _quotes.Count)) : Tr.Get("who.few", "镇上的人说过的话还不够出题，多和大家聊聊吧。"), new Rectangle(card.X, card.Y + 160, card.Width, 30), AisvTheme.TextFaint, 0.72f);
		if (enough)
		{
			Rectangle btn = new Rectangle(card.Center.X - 110, card.Bottom - 84, 220, 54);
			AisvTheme.Button(b, btn, Tr.Get("who.start", "开始一轮"), primary: true, hover: AisvTheme.Hover(btn));
			hits.Add(btn, () =>
			{
				Start();
				Game1.playSound("bigSelect");
			});
		}
	}

	private void Finish(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		Rectangle card = new Rectangle(x, y + 30, w, 280);
		AisvTheme.DrawCard(b, card);
		AisvTheme.TextCentered(b, Tr.Get("who.score", "答对 {{right}} / {{total}}", ("right", _right), ("total", _round.Count)), new Rectangle(card.X, card.Y + 50, card.Width, 50), AisvTheme.Text, 1.3f);
		AisvTheme.TextCentered(b, WhoSaidIt.Verdict(_right, _round.Count), new Rectangle(card.X + 30, card.Y + 120, card.Width - 60, 40), AisvTheme.TextMuted, 0.8f);
		Rectangle btn = new Rectangle(card.Center.X - 110, card.Bottom - 84, 220, 54);
		AisvTheme.Button(b, btn, Tr.Get("who.again", "再来一轮"), primary: true, hover: AisvTheme.Hover(btn));
		hits.Add(btn, () =>
		{
			_quotes = AppData.Quotes(Ctx);
			Start();
		});
	}
}
