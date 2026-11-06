using System;
using System.Collections.Generic;
using AISV.Core.Social.Diary;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AISV.UI.Shell;

/// <summary>日记：每天一张卡片，左边是日期签；按季分组，新的在前。可以翻开日记本看。</summary>
internal sealed class DiaryPage : ShellPage
{
	private sealed class Card
	{
		public DiaryEntry Entry = null!;

		public List<string> Lines = new List<string>();

		public string Weather = "";

		public int Height;
	}

	private const float TextScale = 0.88f;

	private readonly Scroller _scroll = new Scroller();

	private List<DiaryEntry> _entries = new List<DiaryEntry>();

	private List<Card> _cards = new List<Card>();

	private int _builtWidth = -1;

	private Rectangle _feed;

	public DiaryPage(ShellContext ctx) : base(ctx)
	{
	}

	public override void Reload()
	{
		_entries = Ctx.Social.Diary.All();
		_builtWidth = -1;
	}

	private void Build(int width)
	{
		_builtWidth = width;
		int textW = Math.Max(160, width - 24 - 96 - 24 - 24);
		_cards = new List<Card>();
		foreach (DiaryEntry e in _entries)
		{
			Card c = new Card { Entry = e, Weather = ShellText.DiaryWeather(e.Sky), Lines = AisvTheme.WrapLines(ShellText.Clean(e.Text), textW, TextScale, paragraphGap: true) };
			c.Height = 24 + Math.Max(96, c.Lines.Count * AisvTheme.LineHeight(TextScale)) + 24;
			_cards.Add(c);
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _feed.Height - 60);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		_feed = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		int colW = Math.Min(_feed.Width - 80, 860);
		if (_builtWidth != colW)
		{
			Build(colW);
		}
		Kit.Header(b, header, Tr.Get("shell.diary.title", "日记"), _entries.Count > 0 ? Tr.Get("shell.diary.count", "共 {{n}} 篇", ("n", _entries.Count)) : "", Tr.Get("shell.diary.openBook", "翻开日记本"), hits, () => Host.Push(new DiaryBook(Ctx)));
		AisvTheme.BeginClip(b, _feed);
		int x = _feed.X + (_feed.Width - colW) / 2, w = colW;
		int y = _feed.Y + 14 - _scroll.Offset;
		y = Kit.RetryCard(b, Ctx, gossip: false, x, y, w, _feed, hits, Host);
		if (_cards.Count == 0)
		{
			Kit.Empty(b, new Rectangle(x, y + 30, w, 80), Tr.Get("shell.diary.none", "日记本还是空的。"), Tr.Get("shell.diary.noneHint", "过完一天，睡觉时会把今天的事写下来。"));
			y += 140;
		}
		string group = "";
		foreach (Card c in _cards)
		{
			string g = c.Entry.Year + "-" + c.Entry.Season;
			if (g != group)
			{
				group = g;
				AisvTheme.DrawText(b, ShellText.SeasonGroup(c.Entry.Year, c.Entry.Season), new Vector2(x + 6, y + 4), AisvTheme.TextFaint, 0.75f);
				y += 36;
			}
			Rectangle r = new Rectangle(x, y, w, c.Height);
			if (r.Bottom >= _feed.Y && r.Y <= _feed.Bottom)
			{
				DrawCard(b, c, r);
			}
			y = r.Bottom + 14;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _feed, y + 10);
	}

	private static void DrawCard(SpriteBatch b, Card c, Rectangle r)
	{
		AisvTheme.DrawCard(b, r);
		Rectangle tag = new Rectangle(r.X + 24, r.Y + 24, 96, 96);
		bool today = ShellText.IsToday(c.Entry.Day, c.Entry.Season, c.Entry.Year);
		AisvTheme.FillRounded(b, tag, today ? AisvTheme.AccentSoft : AisvTheme.PageDeep, 12);
		AisvTheme.TextCentered(b, c.Entry.Day.ToString(), new Rectangle(tag.X, tag.Y + 8, tag.Width, 46), today ? AisvTheme.AccentDark : AisvTheme.Text, 1.5f);
		AisvTheme.TextCentered(b, ShellText.Season(c.Entry.Season), new Rectangle(tag.X, tag.Y + 54, tag.Width, 20), AisvTheme.TextMuted, 0.68f);
		if (c.Weather.Length > 0)
		{
			AisvTheme.TextCentered(b, c.Weather, new Rectangle(tag.X, tag.Y + 74, tag.Width, 18), AisvTheme.TextFaint, 0.6f);
		}
		int tx = tag.Right + 24, ty = r.Y + 22;
		foreach (string line in c.Lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.Text, TextScale);
			ty += AisvTheme.LineHeight(TextScale);
		}
	}
}
