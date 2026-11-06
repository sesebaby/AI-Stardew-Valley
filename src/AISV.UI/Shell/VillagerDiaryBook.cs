using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Apps;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>村民日记：左页是这位村民和目录，右页是一章内容，长了往后翻。</summary>
internal sealed class VillagerDiaryBook : ShellPage
{
	private static readonly VillagerChapter[] Chapters = Enum.GetValues<VillagerChapter>();

	private static readonly Color WarmInk = new Color(58, 104, 32);

	private readonly string _npc;

	private VillagerFacts? _facts;

	private VillagerChapter _chapter = VillagerChapter.Heart;

	private List<DiaryLine> _lines = new List<DiaryLine>();

	private List<List<(string Text, DiaryInk Ink)>> _pages = new List<List<(string, DiaryInk)>>();

	private Rectangle _builtFor;

	private int _page;

	private int _lineH;

	public VillagerDiaryBook(ShellContext ctx, string npc) : base(ctx)
	{
		_npc = npc;
	}

	public override string Title => Tr.Get("vdiary.title", "{{name}}的日记", ("name", Ctx.Display(_npc)));

	public override void Reload()
	{
		_facts = AppData.VillagerFacts(Ctx, _npc);
		List<DiaryLine> lines = VillagerDiary.Write(_chapter, _facts);
		if (!lines.SequenceEqual(_lines))
		{
			_lines = lines;
			_builtFor = Rectangle.Empty;
		}
	}

	private void Choose(VillagerChapter chapter)
	{
		if (chapter == _chapter)
		{
			return;
		}
		_chapter = chapter;
		_page = 0;
		Game1.playSound("shwip");
		Reload();
	}

	private void Turn(int delta)
	{
		int next = Math.Clamp(_page + delta, 0, Math.Max(0, _pages.Count - 1));
		if (next != _page)
		{
			_page = next;
			Game1.playSound("shwip");
		}
	}

	public override void Scroll(int direction) => Turn(direction > 0 ? -1 : 1);

	public override bool KeyPress(Keys key)
	{
		switch (key)
		{
		case Keys.Left:
		case Keys.PageUp:
			Turn(-1);
			return true;
		case Keys.Right:
		case Keys.PageDown:
			Turn(1);
			return true;
		case Keys.Up:
			Choose(Chapters[Math.Max(0, Array.IndexOf(Chapters, _chapter) - 1)]);
			return true;
		case Keys.Down:
			Choose(Chapters[Math.Min(Chapters.Length - 1, Array.IndexOf(Chapters, _chapter) + 1)]);
			return true;
		default:
			return false;
		}
	}

	private void Paginate(Rectangle text)
	{
		int rows = Math.Max(3, text.Height / _lineH);
		_pages = new List<List<(string, DiaryInk)>> { new List<(string, DiaryInk)>() };
		foreach (DiaryLine line in _lines)
		{
			List<string> wrapped = AisvTheme.WrapLines(ShellText.Clean(line.Text), text.Width);
			if (line.Ink == DiaryInk.Heading && _pages[^1].Count + 2 > rows)
			{
				_pages.Add(new List<(string, DiaryInk)>());
			}
			foreach (string row in wrapped)
			{
				if (_pages[^1].Count >= rows)
				{
					_pages.Add(new List<(string, DiaryInk)>());
				}
				_pages[^1].Add((row, line.Ink));
			}
		}
		_page = Math.Clamp(_page, 0, _pages.Count - 1);
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_lineH = AisvTheme.LineHeight() + 6;
		(Rectangle cover, Rectangle left, Rectangle right) = DiaryBook.Layout(area);
		Rectangle text = new Rectangle(right.X + 58, right.Y + 40 + _lineH, right.Width - 58 - 26, Math.Max(_lineH, (right.Height - 96 - _lineH) / _lineH * _lineH));
		if (_builtFor != cover)
		{
			_builtFor = cover;
			Paginate(text);
		}
		DiaryBook.DrawCover(b, cover);
		DiaryBook.Sheet(b, left, isLeft: true);
		DrawContents(b, left, hits);
		DrawChapter(b, right, text, hits);
		DiaryBook.DrawSpine(b, cover, left);
	}

	private void DrawContents(SpriteBatch b, Rectangle page, Hits hits)
	{
		NPC? npc = ShellText.Npc(_npc);
		Rectangle face = new Rectangle(page.X + 40, page.Y + 28, 96, 96);
		AisvTheme.Avatar(b, face, npc);
		int tx = face.Right + 20, width = page.Right - 24 - tx;
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(_npc), width, 1.1f), new Vector2(tx, face.Y + 2), DiaryBook.Ink, 1.1f);
		int hearts = Ctx.Relationships.EffectiveHearts(_npc);
		AisvTheme.DrawText(b, AisvTheme.Truncate(Relation(hearts), width, 0.78f), new Vector2(tx, face.Y + 40), DiaryBook.DateInk, 0.78f);
		string mood = Ctx.Social.Moods.Get(_npc) is { } m ? Tr.Get("vdiary.mood", "今天心情：{{word}}", ("word", MoodBook.MoodWord(m.Temper))) : "";
		if (npc?.isBirthday() == true)
		{
			mood = Tr.Get("vdiary.birthday", "今天过生日！");
		}
		AisvTheme.DrawText(b, AisvTheme.Truncate(mood, width, 0.78f), new Vector2(tx, face.Y + 66), DiaryBook.Faded, 0.78f);
		int y = face.Bottom + 18;
		AisvTheme.Fill(b, new Rectangle(page.X + 40, y, page.Width - 80, 2), DiaryBook.PaperEdge);
		y += 12;
		int rowH = Math.Min(48, (page.Bottom - 20 - y) / Chapters.Length);
		foreach (VillagerChapter chapter in Chapters)
		{
			Rectangle row = new Rectangle(page.X + 28, y, page.Width - 56, rowH - 2);
			bool on = chapter == _chapter;
			if (on || AisvTheme.Hover(row))
			{
				AisvTheme.FillRounded(b, row, (on ? DiaryBook.DateInk : DiaryBook.Faded) * 0.14f, 8);
			}
			AisvTheme.DrawText(b, VillagerDiary.ChapterTitle(chapter), new Vector2(row.X + 14, row.Center.Y - AisvTheme.LineHeight(0.92f) / 2), on ? DiaryBook.DateInk : DiaryBook.Ink, 0.92f);
			hits.Add(row, () => Choose(chapter));
			y += rowH;
		}
	}

	private string Relation(int hearts)
	{
		Friendship? f = Game1.player != null && Game1.player.friendshipData.TryGetValue(_npc, out Friendship? got) ? got : null;
		string state = f == null ? Tr.Get("vdiary.status.stranger", "还没见过面")
			: f.IsRoommate() ? Tr.Get("shell.villagers.roommate", "室友")
			: f.IsMarried() ? Tr.Get("shell.villagers.spouse", "配偶")
			: f.IsEngaged() ? Tr.Get("shell.villagers.engaged", "订婚了")
			: f.IsDivorced() ? Tr.Get("vdiary.status.divorced", "离过婚")
			: f.IsDating() ? Tr.Get("shell.villagers.dating", "在约会")
			: Tr.Get("shell.villagers.friend", "朋友");
		return state + " · " + Tr.Get("shell.villagers.hearts", "{{n}} 心", ("n", hearts));
	}

	private void DrawChapter(SpriteBatch b, Rectangle page, Rectangle text, Hits hits)
	{
		DiaryBook.Sheet(b, page, isLeft: false);
		for (int y = text.Y + _lineH - 4; y <= text.Bottom; y += _lineH)
		{
			AisvTheme.Fill(b, new Rectangle(page.X + 10, y, page.Width - 20, 2), DiaryBook.Rule);
		}
		AisvTheme.Fill(b, new Rectangle(text.X - 16, page.Y + 6, 2, page.Height - 12), DiaryBook.Margin);
		string title = VillagerDiary.ChapterTitle(_chapter);
		AisvTheme.Bold(b, title, new Vector2(text.X, page.Y + 28), DiaryBook.DateInk, 1.05f);
		int row = 0;
		if (_page < _pages.Count)
		{
			foreach ((string line, DiaryInk ink) in _pages[_page])
			{
				AisvTheme.DrawText(b, line, new Vector2(text.X, text.Y + row * _lineH + 2), ink switch
				{
					DiaryInk.Heading => DiaryBook.DateInk,
					DiaryInk.Faint => DiaryBook.Faded,
					DiaryInk.Warm => WarmInk,
					DiaryInk.Sore => DiaryBook.Ribbon,
					_ => DiaryBook.Ink
				});
				row++;
			}
		}
		if (_pages.Count > 1)
		{
			string number = $"{_page + 1} / {_pages.Count}";
			AisvTheme.DrawText(b, number, new Vector2(page.Center.X - AisvTheme.Width(number) / 2f, page.Bottom - 44), DiaryBook.Faded);
		}
		if (_page > 0)
		{
			Arrow(b, new Rectangle(page.X + 24, page.Bottom - 50, 48, 44), DiaryBook.LeftArrow, hits, () => Turn(-1));
		}
		if (_page < _pages.Count - 1)
		{
			Arrow(b, new Rectangle(page.Right - 64, page.Bottom - 50, 48, 44), DiaryBook.RightArrow, hits, () => Turn(1));
		}
	}

	private static void Arrow(SpriteBatch b, Rectangle r, Rectangle source, Hits hits, Action onClick)
	{
		b.Draw(Game1.mouseCursors, AisvTheme.Hover(r) ? AisvTheme.Inflate(r, 2) : r, source, Color.White);
		hits.Add(r, onClick);
	}
}
