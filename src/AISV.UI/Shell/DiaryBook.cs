using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Diary;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>日记本：皮面、两页横线纸，一页页往后翻。打开时停在最新的一页。</summary>
internal sealed class DiaryBook : ShellPage
{
	private enum Kind
	{
		Text,
		Date,
		Note
	}

	private static readonly Color Cover = new Color(120, 66, 42);

	private static readonly Color Paper = new Color(250, 242, 222);

	private static readonly Color PaperEdge = new Color(205, 182, 150);

	private static readonly Color Rule = new Color(150, 178, 210) * 0.45f;

	private static readonly Color Margin = new Color(220, 110, 110) * 0.55f;

	private static readonly Color Ink = new Color(52, 46, 78);

	private static readonly Color DateInk = new Color(150, 78, 32);

	private static readonly Color Faded = new Color(120, 110, 120);

	private static readonly Color Ribbon = new Color(178, 42, 48);

	private static readonly Rectangle FrameSource = new Rectangle(403, 373, 9, 9);

	private static readonly Rectangle LeftArrow = new Rectangle(352, 495, 12, 11);

	private static readonly Rectangle RightArrow = new Rectangle(365, 495, 12, 11);

	private List<DiaryEntry> _entries = new List<DiaryEntry>();

	private List<List<(string Text, Kind Kind)>> _pages = new List<List<(string, Kind)>>();

	private Rectangle _builtFor;

	private int _spread = int.MaxValue;

	private int _lineH;

	private bool _album;

	private List<AISV.Core.Messenger.PhotoRecord> _photos = new List<AISV.Core.Messenger.PhotoRecord>();

	private const int PhotosPerPage = 2;

	public DiaryBook(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("shell.book.title", "日记本");

	private int PageCount => _album ? Math.Max(1, (_photos.Count + PhotosPerPage - 1) / PhotosPerPage) : _pages.Count;

	private int LastSpread => Math.Max(0, (PageCount + 1) / 2 - 1);

	public override void Reload()
	{
		_photos = Ctx.Messenger?.Photos.All().ToList() ?? new List<AISV.Core.Messenger.PhotoRecord>();
		List<DiaryEntry> entries = Ctx.Social.Diary.All();
		if (entries.Count != _entries.Count || !entries.Select(e => e.Text).SequenceEqual(_entries.Select(e => e.Text)))
		{
			_entries = entries;
			_builtFor = Rectangle.Empty;
		}
	}

	private void Paginate(Rectangle text)
	{
		int rows = Math.Max(3, text.Height / _lineH);
		List<(string, Kind)> flow = new List<(string, Kind)>();
		foreach (DiaryEntry e in _entries.AsEnumerable().Reverse())
		{
			string weather = ShellText.DiaryWeather(e.Sky);
			flow.Add((ShellText.Date(e.Day, e.Season, e.Year) + (weather.Length > 0 ? "（" + weather + "）" : ""), Kind.Date));
			flow.AddRange(AisvTheme.WrapLines(ShellText.Clean(e.Text), text.Width, 1f, paragraphGap: true).Select(l => (l, Kind.Text)));
			flow.Add(("", Kind.Text));
		}
		if (flow.Count == 0)
		{
			flow.Add((Tr.Get("shell.book.empty", "还没有写下任何一天。"), Kind.Note));
		}
		_pages = new List<List<(string, Kind)>> { new List<(string, Kind)>() };
		foreach ((string line, Kind kind) in flow)
		{
			List<(string, Kind)> page = _pages[^1];
			bool full = page.Count >= rows || kind == Kind.Date && page.Count >= rows - 1;
			if (full)
			{
				page = new List<(string, Kind)>();
				_pages.Add(page);
			}
			if (page.Count == 0 && line.Length == 0)
			{
				continue;
			}
			page.Add((line, kind));
		}
		_spread = Math.Clamp(_spread, 0, LastSpread);
	}

	private void Turn(int delta)
	{
		int next = Math.Clamp(_spread + delta, 0, LastSpread);
		if (next != _spread)
		{
			_spread = next;
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
		case Keys.Home:
			Turn(-_spread);
			return true;
		case Keys.End:
			Turn(LastSpread - _spread);
			return true;
		default:
			return false;
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_lineH = AisvTheme.LineHeight() + 6;
		int w = Math.Min(area.Width - 80, 1180), h = Math.Min(area.Height - 76, 760);
		Rectangle cover = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2 + 18, w, h);
		int pageW = (cover.Width - 36 - 28) / 2;
		Rectangle left = new Rectangle(cover.X + 18, cover.Y + 18, pageW, cover.Height - 36);
		Rectangle right = new Rectangle(left.Right + 28, left.Y, pageW, left.Height);
		Rectangle leftText = TextArea(left), rightText = TextArea(right);
		if (_builtFor != cover)
		{
			_builtFor = cover;
			Paginate(leftText);
		}
		Bookmarks(b, cover, hits);
		IClickableMenu.drawTextureBox(b, Game1.mouseCursors, FrameSource, cover.X, cover.Y, cover.Width, cover.Height, Cover, 4f);
		if (_album)
		{
			DrawAlbumPage(b, left, _spread * 2, isLeft: true);
			DrawAlbumPage(b, right, _spread * 2 + 1, isLeft: false);
		}
		else
		{
			DrawPage(b, left, leftText, _spread * 2, isLeft: true);
			DrawPage(b, right, rightText, _spread * 2 + 1, isLeft: false);
		}
		Rectangle spine = new Rectangle(left.Right, cover.Y + 14, 28, cover.Height - 28);
		AisvTheme.Fill(b, spine, Cover * 0.9f);
		AisvTheme.Fill(b, new Rectangle(spine.Center.X - 1, spine.Y, 2, spine.Height), Color.Black * 0.35f);
		DrawRibbon(b, right.X + 34, cover.Y + 4, Math.Min(130, right.Height / 4));
		if (_spread > 0)
		{
			Arrow(b, new Rectangle(left.X + 16, left.Bottom - 50, 48, 44), LeftArrow, hits, () => Turn(-1));
		}
		if (_spread < LastSpread)
		{
			Arrow(b, new Rectangle(right.Right - 64, right.Bottom - 50, 48, 44), RightArrow, hits, () => Turn(1));
		}
	}

	/// <summary>封面顶上的两张书签：日记 / 相册。</summary>
	private void Bookmarks(SpriteBatch b, Rectangle cover, Hits hits)
	{
		(string Label, bool Album)[] tabs = { (Tr.Get("shell.book.diaryTab", "日记"), false), (Tr.Get("shell.book.albumTab", "相册"), true) };
		int x = cover.X + 60;
		foreach ((string label, bool album) in tabs)
		{
			int w = (int)AisvTheme.Width(label, 0.8f) + 36;
			bool on = _album == album;
			Rectangle tab = new Rectangle(x, cover.Y - (on ? 40 : 32), w, 48);
			AisvTheme.FillRounded(b, tab, on ? Paper : PaperEdge, 8);
			AisvTheme.TextCentered(b, label, new Rectangle(tab.X, tab.Y + 2, tab.Width, 36), on ? DateInk : Faded, 0.8f);
			if (!on)
			{
				hits.Add(tab, () =>
				{
					_album = album;
					_spread = LastSpread;
					Game1.playSound("shwip");
				});
			}
			x = tab.Right + 8;
		}
	}

	/// <summary>相册的一页：两张贴着胶带的照片，下面手写是谁拍的、在哪、哪天。</summary>
	private void DrawAlbumPage(SpriteBatch b, Rectangle page, int index, bool isLeft)
	{
		AisvTheme.Fill(b, page, Paper);
		AisvTheme.HLine(b, page.X, page.Y, page.Width, PaperEdge);
		AisvTheme.HLine(b, page.X, page.Bottom - 2, page.Width, PaperEdge);
		AisvTheme.VLine(b, page.X, page.Y, page.Height, PaperEdge);
		AisvTheme.VLine(b, page.Right - 2, page.Y, page.Height, PaperEdge);
		for (int i = 0; i < 6; i++)
		{
			int sx = isLeft ? page.Right - (i + 1) * 4 : page.X + i * 4;
			AisvTheme.Fill(b, new Rectangle(sx, page.Y, 4, page.Height), Color.Black * (0.07f - i * 0.011f));
		}
		if (_photos.Count == 0)
		{
			if (index == 0)
			{
				AisvTheme.DrawText(b, Tr.Get("shell.book.albumEmpty", "还没有照片。在信使里找人要一张吧。"), new Vector2(page.X + 40, page.Y + 60), Faded);
			}
			return;
		}
		int slotH = (page.Height - 80) / PhotosPerPage;
		for (int k = 0; k < PhotosPerPage; k++)
		{
			int i = index * PhotosPerPage + k;
			if (i >= _photos.Count)
			{
				break;
			}
			AISV.Core.Messenger.PhotoRecord r = _photos[i];
			Texture2D? image = Ctx.Messenger?.Photos.Image(r.Id);
			int maxW = page.Width - 120, maxH = slotH - 70;
			int w = maxW, h = w * 9 / 16;
			if (image != null)
			{
				h = (int)(w * (image.Height / (float)Math.Max(1, image.Width)));
			}
			if (h > maxH)
			{
				w = (int)(w * maxH / (float)h);
				h = maxH;
			}
			int px = page.Center.X - w / 2 + (k % 2 == 0 ? -10 : 10), py = page.Y + 36 + k * slotH;
			Rectangle frame = new Rectangle(px - 10, py - 10, w + 20, h + 20);
			AisvTheme.Fill(b, new Rectangle(frame.X + 4, frame.Y + 5, frame.Width, frame.Height), Color.Black * 0.12f);
			AisvTheme.Fill(b, frame, Color.White);
			if (image != null)
			{
				b.Draw(image, new Rectangle(px, py, w, h), Color.White);
			}
			else
			{
				AisvTheme.Fill(b, new Rectangle(px, py, w, h), PaperEdge);
			}
			AisvTheme.Fill(b, new Rectangle(frame.X - 12, frame.Y - 6, 44, 16), new Color(240, 226, 170) * 0.85f);
			AisvTheme.Fill(b, new Rectangle(frame.Right - 32, frame.Y - 6, 44, 16), new Color(240, 226, 170) * 0.85f);
			string who = Ctx.Display(r.Sender);
			string caption = ShellText.Date(r.Day, r.Season, r.Year) + " · " + (r.LocationDisplay.Length > 0 ? r.LocationDisplay : r.LocationName) + " · "
				+ (string.Equals(r.Subject, r.Sender, StringComparison.OrdinalIgnoreCase) ? Tr.Get("shell.book.selfie", "{{name}}的自拍", ("name", who)) : Tr.Get("shell.book.shotBy", "{{name}}拍的", ("name", who)));
			AisvTheme.DrawText(b, AisvTheme.Truncate(caption, page.Width - 80, 0.82f), new Vector2(page.Center.X - Math.Min(page.Width - 80, AisvTheme.Width(caption, 0.82f)) / 2f, frame.Bottom + 10), Ink, 0.82f);
		}
		string number = (index + 1).ToString();
		AisvTheme.DrawText(b, number, new Vector2(page.Center.X - AisvTheme.Width(number) / 2f, page.Bottom - 40), Faded);
	}

	private Rectangle TextArea(Rectangle page)
	{
		Rectangle r = new Rectangle(page.X + 58, page.Y + 40, page.Width - 58 - 26, page.Height - 40 - 56);
		return new Rectangle(r.X, r.Y, r.Width, Math.Max(_lineH, r.Height / _lineH * _lineH));
	}

	private void DrawPage(SpriteBatch b, Rectangle page, Rectangle text, int index, bool isLeft)
	{
		AisvTheme.Fill(b, page, Paper);
		AisvTheme.HLine(b, page.X, page.Y, page.Width, PaperEdge);
		AisvTheme.HLine(b, page.X, page.Bottom - 2, page.Width, PaperEdge);
		AisvTheme.VLine(b, page.X, page.Y, page.Height, PaperEdge);
		AisvTheme.VLine(b, page.Right - 2, page.Y, page.Height, PaperEdge);
		for (int i = 0; i < 6; i++)
		{
			int x = isLeft ? page.Right - (i + 1) * 4 : page.X + i * 4;
			AisvTheme.Fill(b, new Rectangle(x, page.Y, 4, page.Height), Color.Black * (0.07f - i * 0.011f));
		}
		for (int y = text.Y + _lineH - 4; y <= text.Bottom; y += _lineH)
		{
			AisvTheme.Fill(b, new Rectangle(page.X + 10, y, page.Width - 20, 2), Rule);
		}
		AisvTheme.Fill(b, new Rectangle(text.X - 16, page.Y + 6, 2, page.Height - 12), Margin);
		if (index >= _pages.Count)
		{
			return;
		}
		int row = 0;
		foreach ((string line, Kind kind) in _pages[index])
		{
			int y = text.Y + row * _lineH + 2;
			Color color = kind == Kind.Date ? DateInk : kind == Kind.Note ? Faded : Ink;
			AisvTheme.DrawText(b, line, new Vector2(text.X, y), color);
			if (kind == Kind.Date)
			{
				AisvTheme.Fill(b, new Rectangle(text.X, text.Y + row * _lineH + _lineH - 8, Math.Min(text.Width, (int)AisvTheme.Width(line)), 2), DateInk * 0.6f);
			}
			row++;
		}
		string number = (index + 1).ToString();
		AisvTheme.DrawText(b, number, new Vector2(page.Center.X - AisvTheme.Width(number) / 2f, page.Bottom - 40), Faded);
	}

	private static void DrawRibbon(SpriteBatch b, int x, int y, int h)
	{
		AisvTheme.Fill(b, new Rectangle(x, y, 16, h), Ribbon);
		AisvTheme.Fill(b, new Rectangle(x + 12, y, 4, h), Color.Black * 0.2f);
		for (int i = 0; i < 8; i++)
		{
			AisvTheme.Fill(b, new Rectangle(x, y + h + i, 8 - i, 1), Ribbon);
			AisvTheme.Fill(b, new Rectangle(x + 8 + i, y + h + i, 8 - i, 1), Ribbon);
		}
	}

	private static void Arrow(SpriteBatch b, Rectangle r, Rectangle source, Hits hits, Action onClick)
	{
		Rectangle dest = AisvTheme.Hover(r) ? AisvTheme.Inflate(r, 2) : r;
		b.Draw(Game1.mouseCursors, dest, source, Color.White);
		hits.Add(r, onClick);
	}
}
