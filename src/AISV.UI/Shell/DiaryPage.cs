using System;
using System.Collections.Generic;
using AISV.Core.Social.Diary;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

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

	private bool _exported;

	/// <summary>再过几帧开始印：先让按钮显示“正在印”，大本日记要印几秒。</summary>
	private int _printIn;

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
			Card c = new Card { Entry = e, Weather = ShellText.DiaryWeather(e.Sky), Lines = Hearth.WrapLines(ShellText.Clean(e.Text), textW, TextScale, paragraphGap: true) };
			c.Height = 24 + Math.Max(96, c.Lines.Count * Hearth.LineHeight(TextScale)) + 24;
			_cards.Add(c);
		}
	}

	private void Export()
	{
		Game1.playSound("smallSelect");
		_printIn = 2;
	}

	public override void Update()
	{
		if (_printIn == 0 || --_printIn > 0)
		{
			return;
		}
		try
		{
			string path = DiaryPrinter.Export(Ctx);
			_exported = true;
			Host.Notice(Tr.Get("shell.diary.exported", "已导出：{{file}}", ("file", System.IO.Path.GetFileName(path))));
		}
		catch (Exception ex)
		{
			Ctx.Log.Warn("日记导出失败：" + ex.Message);
			Host.Notice(Tr.Get("shell.diary.exportFailed", "导出失败，详情见 SMAPI 日志"));
		}
	}

	private void OpenFolder()
	{
		Game1.playSound("smallSelect");
		string folder = DiaryPrinter.PrintedFolder();
		if (AISV.Game.Promo.ExternalLink.Open(folder, Ctx.Log) != AISV.Game.Promo.LinkResult.Opened)
		{
			Host.Notice(Tr.Get("shell.diary.folderAt", "打不开文件夹，它在：{{path}}", ("path", folder)));
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
		Rectangle open = Kit.Header(b, header, Tr.Get("shell.diary.title", "日记"), _entries.Count > 0 ? Tr.Get("shell.diary.count", "共 {{n}} 篇", ("n", _entries.Count)) : "", Tr.Get("shell.diary.openBook", "翻开日记本"), hits, () => Host.Push(new DiaryBook(Ctx)));
		if (_entries.Count > 0 || Ctx.Messenger?.Photos.All().Count > 0)
		{
			string label = _printIn > 0 ? Tr.Get("shell.diary.printing", "正在印…") : Tr.Get("shell.diary.export", "导出 PDF");
			int bw = (int)Hearth.Width(label, 0.85f) + 40;
			Rectangle export = new Rectangle(open.X - 12 - bw, open.Y, bw, open.Height);
			Hearth.PressPlate(b, export, label, hover: Hearth.Hover(export), scale: 0.85f);
			hits.Add(export, Export);
			if (_exported)
			{
				string folderLabel = Tr.Get("shell.diary.openFolder", "打开文件夹");
				int fw = (int)Hearth.Width(folderLabel, 0.85f) + 40;
				Rectangle folder = new Rectangle(export.X - 12 - fw, open.Y, fw, open.Height);
				Hearth.PressPlate(b, folder, folderLabel, hover: Hearth.Hover(folder), scale: 0.85f);
				hits.Add(folder, OpenFolder);
			}
		}
		Hearth.BeginClip(b, _feed);
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
				Hearth.Ink(b, ShellText.SeasonGroup(c.Entry.Year, c.Entry.Season), new Vector2(x + 6, y + 4), Hearth.QuillPale, 0.75f);
				y += 36;
			}
			Rectangle r = new Rectangle(x, y, w, c.Height);
			if (r.Bottom >= _feed.Y && r.Y <= _feed.Bottom)
			{
				DrawCard(b, c, r);
			}
			y = r.Bottom + 14;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, _feed, y + 10);
	}

	private static void DrawCard(SpriteBatch b, Card c, Rectangle r)
	{
		Hearth.CardSlip(b, r);
		Rectangle tag = new Rectangle(r.X + 24, r.Y + 24, 96, 96);
		bool today = ShellText.IsToday(c.Entry.Day, c.Entry.Season, c.Entry.Year);
		Hearth.FillRounded(b, tag, today ? Hearth.LeafPale : Hearth.ParchmentDeep, 12);
		Hearth.InkMiddle(b, c.Entry.Day.ToString(), new Rectangle(tag.X, tag.Y + 8, tag.Width, 46), today ? Hearth.LeafDeep : Hearth.Quill, 1.5f);
		Hearth.InkMiddle(b, ShellText.Season(c.Entry.Season), new Rectangle(tag.X, tag.Y + 54, tag.Width, 20), Hearth.QuillSoft, 0.68f);
		if (c.Weather.Length > 0)
		{
			Hearth.InkMiddle(b, c.Weather, new Rectangle(tag.X, tag.Y + 74, tag.Width, 18), Hearth.QuillPale, 0.6f);
		}
		int tx = tag.Right + 24, ty = r.Y + 22;
		foreach (string line in c.Lines)
		{
			Hearth.Ink(b, line, new Vector2(tx, ty), Hearth.Quill, TextScale);
			ty += Hearth.LineHeight(TextScale);
		}
	}
}
