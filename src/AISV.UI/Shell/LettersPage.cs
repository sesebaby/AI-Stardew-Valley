using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Letters;
using AISV.Core.Text;
using AISV.Game.Social;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>信件：左边收信列表（按季分组），右边信纸。</summary>
internal sealed class LettersPage : ShellPage
{
	private sealed class Letter
	{
		public QueuedLetter Source = null!;

		public string Sender = "";

		public string Subject = "";

		public string Greeting = "";

		public List<string> Paragraphs = new List<string>();

		public int Hearts;

		public Item? Gift;

		public string Section = "";
	}

	private static readonly Color PaperColor = new Color(255, 251, 238);

	/// <summary>从别的页跳过来时要选中的信。</summary>
	public static string? Focus;

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _readScroll = new Scroller();

	private List<Letter> _letters = new List<Letter>();

	private int _selected = -1;

	private Rectangle _list;

	private Rectangle _detail;

	public LettersPage(ShellContext ctx) : base(ctx)
	{
	}

	public override void Reload()
	{
		string? keep = Focus ?? (_selected >= 0 && _selected < _letters.Count ? _letters[_selected].Source.Key : null);
		bool first = _selected < 0;
		Focus = null;
		_letters = Ctx.Social.Letters.All().Select(Parse).ToList();
		int i = keep == null ? -1 : _letters.FindIndex(l => l.Source.Key == keep);
		if (i >= 0 && i != _selected)
		{
			_selected = i;
			_readScroll.Reset();
		}
		else if (first)
		{
			_selected = Math.Max(0, _letters.FindIndex(l => l.Source.Sent));
		}
		_selected = Math.Clamp(_selected, 0, Math.Max(0, _letters.Count - 1));
	}

	private Letter Parse(QueuedLetter q)
	{
		string sender = Ctx.Display(q.From);
		List<string> paras = ShellText.Clean(q.Text).Replace("^", "\n").Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
		string greeting = "";
		if (paras.Count > 1 && paras[0].Length <= 30 && (paras[0].StartsWith("亲爱的", StringComparison.Ordinal) || paras[0].EndsWith("：", StringComparison.Ordinal) || paras[0].EndsWith(":", StringComparison.Ordinal)))
		{
			greeting = paras[0];
			paras.RemoveAt(0);
		}
		if (paras.Count > 1 && paras[^1].Length <= sender.Length + 6 && paras[^1].Contains(sender, StringComparison.Ordinal))
		{
			paras.RemoveAt(paras.Count - 1);
		}
		Item? gift = Ctx.Gifts?.Preview(q.GiftRef);
		if (gift == null && q.EnclosedId > 0)
		{
			try
			{
				gift = ItemRegistry.Create("(O)" + q.EnclosedId);
			}
			catch (Exception)
			{
				gift = null;
			}
		}
		int hearts = q.Cause.StartsWith("hearts_", StringComparison.Ordinal) && int.TryParse(q.Cause.Substring(7), out int h) ? h : 0;
		return new Letter { Source = q, Sender = sender, Subject = ShellText.Clean(q.Title), Greeting = greeting, Paragraphs = paras, Hearts = hearts, Gift = gift, Section = q.Year + "-" + q.Season };
	}

	private void Select(int i)
	{
		if (i >= 0 && i < _letters.Count && i != _selected)
		{
			Game1.playSound("smallSelect");
			_selected = i;
			_readScroll.Reset();
		}
	}

	public override void Scroll(int direction)
	{
		if (_list.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			_listScroll.Wheel(direction, 72);
		}
		else
		{
			_readScroll.Wheel(direction, 72);
		}
	}

	public override bool KeyPress(Keys key)
	{
		switch (key)
		{
		case Keys.Up:
			Select(_selected - 1);
			return true;
		case Keys.Down:
			Select(_selected + 1);
			return true;
		case Keys.PageUp:
			_readScroll.By(-200);
			return true;
		case Keys.PageDown:
			_readScroll.By(200);
			return true;
		default:
			return false;
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		int listW = Math.Clamp(area.Width * 34 / 100, 300, 420);
		_list = new Rectangle(area.X, area.Y, listW, area.Height);
		_detail = new Rectangle(_list.Right, area.Y, area.Right - _list.Right, area.Height);
		if (_letters.Count == 0)
		{
			DrawEmpty(b, area);
			return;
		}
		DrawList(b, hits);
		DrawLetter(b, _letters[_selected]);
	}

	private static void DrawEmpty(SpriteBatch b, Rectangle area)
	{
		Rectangle box = new Rectangle(area.Center.X - 220, area.Center.Y - 90, 440, 180);
		AisvTheme.DrawCard(b, box);
		Rectangle env = new Rectangle(box.Center.X - 36, box.Y + 26, 72, 48);
		AisvTheme.Bordered(b, env, PaperColor, AisvTheme.CardBorder, 6);
		AisvTheme.Line(b, new Vector2(env.X + 4, env.Y + 4), new Vector2(env.Center.X, env.Center.Y + 6), AisvTheme.CardBorder, 2f);
		AisvTheme.Line(b, new Vector2(env.Right - 4, env.Y + 4), new Vector2(env.Center.X, env.Center.Y + 6), AisvTheme.CardBorder, 2f);
		AisvTheme.TextCentered(b, Tr.Get("shell.letters.none", "还没有收到信。"), new Rectangle(box.X, env.Bottom + 12, box.Width, 32), AisvTheme.Text, 0.9f);
		AisvTheme.TextCentered(b, Tr.Get("shell.letters.noneHint", "和村民交情深了，他们会写信来。"), new Rectangle(box.X, env.Bottom + 48, box.Width, 30), AisvTheme.TextMuted, 0.75f);
	}

	private void DrawList(SpriteBatch b, Hits hits)
	{
		AisvTheme.Fill(b, _list, AisvTheme.Card);
		AisvTheme.VLine(b, _list.Right - 2, _list.Y, _list.Height, AisvTheme.Divider);
		Rectangle clip = new Rectangle(_list.X, _list.Y + 6, _list.Width - 4, _list.Height - 12);
		AisvTheme.BeginClip(b, clip);
		int y = clip.Y + 8 - _listScroll.Offset;
		string section = "";
		const int rowH = 84;
		for (int i = 0; i < _letters.Count; i++)
		{
			Letter l = _letters[i];
			if (l.Section != section)
			{
				section = l.Section;
				int n = _letters.Count(x => x.Section == l.Section);
				AisvTheme.DrawText(b, ShellText.SeasonGroup(l.Source.Year, l.Source.Season) + "  ·  " + Tr.Get("shell.letters.count", "{{n}} 封", ("n", n)), new Vector2(_list.X + 20, y + 4), AisvTheme.TextFaint, 0.72f);
				y += 34;
			}
			Rectangle row = new Rectangle(_list.X + 8, y, _list.Width - 20, rowH);
			if (row.Bottom >= clip.Y && row.Y <= clip.Bottom)
			{
				DrawRow(b, l, row, i == _selected, AisvTheme.Hover(row, clip));
				int index = i;
				hits.Add(row, () => Select(index), clip);
			}
			y += rowH + 2;
		}
		AisvTheme.EndClip(b);
		_listScroll.Finish(b, clip, y + 8, 10);
	}

	private void DrawRow(SpriteBatch b, Letter l, Rectangle row, bool selected, bool hover)
	{
		if (selected || hover)
		{
			AisvTheme.FillRounded(b, row, selected ? AisvTheme.SelectedRow : AisvTheme.Page, 10);
		}
		if (selected)
		{
			AisvTheme.FillRounded(b, new Rectangle(row.X, row.Y + 14, 4, row.Height - 28), AisvTheme.Accent, 2);
		}
		Kit.Avatar(b, new Rectangle(row.X + 14, row.Y + 14, 54, 54), Ctx, l.Source.From);
		int tx = row.X + 82;
		bool transit = !l.Source.Sent;
		string when = transit ? Tr.Get("shell.letters.onTheWay", "在路上") : ShellText.Season(l.Source.Season) + " " + l.Source.Day;
		float ww = AisvTheme.Width(when, 0.66f);
		AisvTheme.DrawText(b, when, new Vector2(row.Right - 12 - ww, row.Y + 16), transit ? AisvTheme.Gold : AisvTheme.TextFaint, 0.66f);
		AisvTheme.Bold(b, AisvTheme.Truncate(l.Sender, row.Right - 24 - ww - tx, 0.9f), new Vector2(tx, row.Y + 12), AisvTheme.Text, 0.9f);
		float cx = row.Right - 12;
		if (l.Gift != null)
		{
			cx -= 30;
			try
			{
				l.Gift.drawInMenu(b, new Vector2(cx - 19, row.Y + 46 - 19), 0.4f, 1f, 0.9f, StackDrawType.Hide, Color.White, drawShadow: false);
			}
			catch (Exception)
			{
			}
		}
		if (l.Hearts > 0)
		{
			string h = Tr.Get("shell.letters.hearts", "{{n}}心", ("n", l.Hearts));
			cx -= AisvTheme.ChipWidth(h, 0.6f) + 2;
			AisvTheme.Chip(b, new Vector2(cx, row.Y + 48), h, AisvTheme.PinkSoft, AisvTheme.Pink, 0.6f);
		}
		string preview = l.Subject.Length > 0 ? l.Subject : l.Paragraphs.FirstOrDefault() ?? "";
		AisvTheme.DrawText(b, AisvTheme.Truncate(preview, cx - 10 - tx, 0.75f), new Vector2(tx, row.Y + 48), AisvTheme.TextMuted, 0.75f);
	}

	private void DrawLetter(SpriteBatch b, Letter l)
	{
		Rectangle clip = _detail;
		int paperW = Math.Min(clip.Width - 64, 760);
		int x = clip.X + (clip.Width - paperW) / 2;
		int inner = paperW - 80;
		int lh = AisvTheme.LineHeight(0.88f);
		bool transit = !l.Source.Sent;
		List<string> subject = l.Subject.Length > 0 ? AisvTheme.WrapLines(l.Subject, inner) : new List<string>();
		List<List<string>> paras = transit
			? new List<List<string>> { AisvTheme.WrapLines(Tr.Get("shell.letters.notYet", "信还在路上，明天早上寄到后才能读。"), inner, 0.88f) }
			: l.Paragraphs.Select(p => AisvTheme.WrapLines(p, inner, 0.88f)).ToList();
		string greeting = transit ? "" : l.Greeting;
		int h = 32 + 56 + 18 + subject.Count * AisvTheme.LineHeight(1f) + (subject.Count > 0 ? 14 : 0) + (greeting.Length > 0 ? lh + 12 : 0) + paras.Sum(p => p.Count * lh + 12) + lh + 20 + (l.Gift != null && !transit ? 96 : 0) + 32;
		AisvTheme.BeginClip(b, clip);
		Rectangle paper = new Rectangle(x, clip.Y + 18 - _readScroll.Offset, paperW, h);
		AisvTheme.DrawShadow(b, paper);
		AisvTheme.Bordered(b, paper, PaperColor, AisvTheme.CardBorder, 10);
		AisvTheme.Fill(b, new Rectangle(paper.X + 2, paper.Y + 2, paper.Width - 4, 8), new Color(232, 214, 176));
		int cy = paper.Y + 32, px = paper.X + 40;
		Kit.Avatar(b, new Rectangle(px, cy, 56, 56), Ctx, l.Source.From);
		AisvTheme.Bold(b, l.Sender, new Vector2(px + 72, cy + 2), AisvTheme.Text);
		string meta = ShellText.Date(l.Source.Day, l.Source.Season, l.Source.Year) + (transit ? "  ·  " + Tr.Get("shell.letters.onTheWay", "在路上") : "");
		AisvTheme.DrawText(b, meta, new Vector2(px + 72, cy + 32), AisvTheme.TextFaint, 0.72f);
		if (l.Hearts > 0)
		{
			string when = Tr.Get("shell.letters.heartsWhen", "写在你们 {{n}} 心的时候", ("n", l.Hearts));
			AisvTheme.Chip(b, new Vector2(paper.Right - 40 - AisvTheme.ChipWidth(when, 0.66f), cy + 14), when, AisvTheme.PinkSoft, AisvTheme.Pink, 0.66f);
		}
		cy += 74;
		AisvTheme.HLine(b, px, cy - 9, inner, AisvTheme.CardBorder);
		foreach (string line in subject)
		{
			AisvTheme.Bold(b, line, new Vector2(px, cy), AisvTheme.Text);
			cy += AisvTheme.LineHeight(1f);
		}
		if (subject.Count > 0)
		{
			cy += 14;
		}
		if (greeting.Length > 0)
		{
			AisvTheme.DrawText(b, greeting, new Vector2(px, cy), AisvTheme.Text, 0.88f);
			cy += lh + 12;
		}
		foreach (List<string> para in paras)
		{
			foreach (string line in para)
			{
				AisvTheme.DrawText(b, line, new Vector2(px, cy), transit ? AisvTheme.TextMuted : AisvTheme.Text, 0.88f);
				cy += lh;
			}
			cy += 12;
		}
		if (!transit)
		{
			string sign = "—— " + l.Sender;
			AisvTheme.Bold(b, sign, new Vector2(paper.Right - 40 - AisvTheme.Width(sign, 0.95f), cy), AisvTheme.AccentDark, 0.95f);
		}
		cy += lh + 20;
		if (l.Gift != null && !transit)
		{
			Rectangle gift = new Rectangle(px, cy, inner, 80);
			AisvTheme.Bordered(b, gift, AisvTheme.GoldSoft, AisvTheme.Gold * 0.5f, 10);
			Rectangle slot = new Rectangle(gift.X + 14, gift.Y + 8, 64, 64);
			AisvTheme.FillRounded(b, slot, AisvTheme.Card, 8);
			try
			{
				l.Gift.drawInMenu(b, new Vector2(slot.X, slot.Y), 1f, 1f, 0.9f, StackDrawType.Hide, Color.White, drawShadow: false);
			}
			catch (Exception)
			{
			}
			AisvTheme.DrawText(b, Tr.Get("shell.letters.gift", "随信附上的礼物"), new Vector2(slot.Right + 16, gift.Y + 12), AisvTheme.TextMuted, 0.72f);
			AisvTheme.Bold(b, AisvTheme.Truncate(ShellText.Clean(Ctx.Social.LetterGiftName(l.Source)), gift.Right - slot.Right - 32, 0.9f), new Vector2(slot.Right + 16, gift.Y + 40), AisvTheme.Text, 0.9f);
		}
		AisvTheme.EndClip(b);
		_readScroll.Finish(b, clip, paper.Bottom + 24, 12);
	}
}
