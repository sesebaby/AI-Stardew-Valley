using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Social.Diary;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>把日记印成 PDF：封面、一页页的文字、夹在当天后面的画，最后是相册。每一页先画到一张图上。</summary>
internal sealed class DiaryPrinter
{
	private const int PageW = 900;

	private const int PageH = 1270;

	private const int Inset = 84;

	private readonly ShellContext _ctx;

	private readonly List<Action<SpriteBatch>> _pages = new List<Action<SpriteBatch>>();

	private DiaryPrinter(ShellContext ctx)
	{
		_ctx = ctx;
	}

	public static string PrintedFolder() => Path.Combine(StardewModdingAPI.Constants.DataPath, "AIStardewValley-Diary");

	/// <summary>印一本，返回文件路径。要在游戏主线程、不在绘制过程中调用。</summary>
	public static string Export(ShellContext ctx)
	{
		DiaryPrinter printer = new DiaryPrinter(ctx);
		printer.Compose();
		PdfBook book = printer.Render();
		string safeName = string.Concat((Game1.player?.Name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
		string folder = PrintedFolder();
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, $"{Tr.Get("shell.print.file", "日记")}-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
		string draft = path + ".part";
		try
		{
			using (FileStream output = File.Create(draft))
			{
				book.Save(output, Tr.Get("shell.print.title", "{{name}}的日记", ("name", Game1.player?.Name ?? "")));
			}
			File.Move(draft, path, overwrite: true);
		}
		finally
		{
			File.Delete(draft);
		}
		return path;
	}

	private void Compose()
	{
		List<DiaryEntry> entries = _ctx.Social.Diary.All().AsEnumerable().Reverse().ToList();
		_pages.Add(CoverPage);
		List<PinnedSketch> pins = DiarySketches.Pin(entries, _ctx.Display);
		List<(string Line, bool IsDate)> lines = new List<(string, bool)>();
		int rows = (PageH - 2 * Inset) / RowHeight;
		void Flush()
		{
			if (lines.Count > 0)
			{
				List<(string, bool)> page = lines;
				lines = new List<(string, bool)>();
				_pages.Add(b => TextPage(b, page));
			}
		}
		foreach (DiaryEntry entry in entries)
		{
			if (lines.Count >= rows - 1)
			{
				Flush();
			}
			string weather = ShellText.DiaryWeather(entry.Sky);
			lines.Add((ShellText.Date(entry.Day, entry.Season, entry.Year) + (weather.Length > 0 ? "（" + weather + "）" : ""), true));
			foreach (string line in Hearth.WrapLines(ShellText.Clean(entry.Text), PageW - 2 * Inset, 1f, paragraphGap: true))
			{
				if (lines.Count >= rows)
				{
					Flush();
				}
				if (line.Length > 0 || lines.Count > 0)
				{
					lines.Add((line, false));
				}
			}
			if (lines.Count < rows)
			{
				lines.Add(("", false));
			}
			PinnedSketch? pin = pins.FirstOrDefault(p => ReferenceEquals(p.Entry, entry));
			if (pin != null)
			{
				Flush();
				_pages.Add(b => PicturePage(b, DiaryPictures.Sketch(_ctx, pin.Sketch.File), DiaryPictures.SketchCaption(_ctx, pin)));
			}
		}
		Flush();
		foreach (AISV.Core.Messenger.PhotoRecord photo in _ctx.Messenger?.Photos.All() ?? Array.Empty<AISV.Core.Messenger.PhotoRecord>())
		{
			_pages.Add(b => PicturePage(b, _ctx.Messenger?.Photos.Image(photo.Id), DiaryPictures.PhotoCaption(_ctx, photo)));
		}
	}

	private static int RowHeight => Hearth.LineHeight() + 8;

	private PdfBook Render()
	{
		GraphicsDevice device = Game1.graphics.GraphicsDevice;
		RenderTargetBinding[] previous = device.GetRenderTargets();
		PdfBook book = new PdfBook();
		Color[] pixels = new Color[PageW * PageH];
		byte[] rgb = new byte[PageW * PageH * 3];
		using RenderTarget2D target = new RenderTarget2D(device, PageW, PageH);
		using SpriteBatch batch = new SpriteBatch(device);
		try
		{
			foreach (Action<SpriteBatch> page in _pages)
			{
				device.SetRenderTarget(target);
				device.Clear(DiaryBook.Paper);
				batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
				page(batch);
				batch.End();
				device.SetRenderTarget(null);
				target.GetData(pixels);
				for (int i = 0; i < pixels.Length; i++)
				{
					rgb[i * 3] = pixels[i].R;
					rgb[i * 3 + 1] = pixels[i].G;
					rgb[i * 3 + 2] = pixels[i].B;
				}
				book.AddPage(rgb, PageW, PageH);
			}
		}
		finally
		{
			device.SetRenderTargets(previous);
		}
		return book;
	}

	private void CoverPage(SpriteBatch b)
	{
		Hearth.Fill(b, new Rectangle(0, 0, PageW, PageH), DiaryBook.Cover);
		Hearth.Fill(b, new Rectangle(36, 36, PageW - 72, PageH - 72), DiaryBook.Paper);
		Texture2D? art = DiaryPictures.Sketch(_ctx, DiarySketches.CoverFile);
		const int side = 680;
		Rectangle frame = new Rectangle((PageW - side) / 2, 110, side, side);
		if (art != null)
		{
			b.Draw(art, frame, Color.White);
		}
		string title = Tr.Get("shell.print.title", "{{name}}的日记", ("name", ShellText.Clean(Game1.player?.Name ?? "")));
		float scale = 1.6f;
		Hearth.InkBold(b, title, new Vector2((PageW - Hearth.Width(title, scale)) / 2f, frame.Bottom + 70), DiaryBook.DateInk, scale);
		string farm = Tr.Get("shell.print.farm", "{{name}}农场", ("name", ShellText.Clean(Game1.player?.farmName.Value ?? "")));
		Hearth.Ink(b, farm, new Vector2((PageW - Hearth.Width(farm)) / 2f, frame.Bottom + 150), DiaryBook.Faded);
	}

	private static void TextPage(SpriteBatch b, List<(string Line, bool IsDate)> lines)
	{
		int y = Inset;
		for (int rule = y + RowHeight - 6; rule < PageH - Inset; rule += RowHeight)
		{
			Hearth.Fill(b, new Rectangle(40, rule, PageW - 80, 2), DiaryBook.Rule);
		}
		Hearth.Fill(b, new Rectangle(Inset - 18, 0, 2, PageH), DiaryBook.Margin);
		foreach ((string line, bool isDate) in lines)
		{
			Hearth.Ink(b, line, new Vector2(Inset, y), isDate ? DiaryBook.DateInk : DiaryBook.Ink);
			y += RowHeight;
		}
	}

	private static void PicturePage(SpriteBatch b, Texture2D? image, string caption)
	{
		int maxW = PageW - 2 * Inset, maxH = PageH - 2 * Inset - 80;
		int w = maxW, h = maxW * 3 / 4;
		if (image != null)
		{
			float fit = Math.Min(maxW / (float)image.Width, maxH / (float)image.Height);
			w = (int)(image.Width * fit);
			h = (int)(image.Height * fit);
		}
		Rectangle photo = new Rectangle((PageW - w) / 2, Inset, w, h);
		Rectangle frame = Hearth.Inflate(photo, 12);
		Hearth.Fill(b, new Rectangle(frame.X + 5, frame.Y + 6, frame.Width, frame.Height), Color.Black * 0.14f);
		Hearth.Fill(b, frame, Color.White);
		if (image != null)
		{
			b.Draw(image, photo, Color.White);
		}
		else
		{
			Hearth.Fill(b, photo, DiaryBook.PaperEdge);
		}
		string shown = Hearth.Truncate(caption, maxW, 1f);
		Hearth.Ink(b, shown, new Vector2((PageW - Hearth.Width(shown)) / 2f, frame.Bottom + 24), DiaryBook.Ink);
	}
}

/// <summary>日记本相册和 PDF 共用的图片与说明文字。</summary>
internal static class DiaryPictures
{
	private static readonly Dictionary<string, Texture2D?> Loaded = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

	public static Texture2D? Sketch(ShellContext ctx, string file)
	{
		if (Loaded.TryGetValue(file, out Texture2D? known) && known is not { IsDisposed: true })
		{
			return known;
		}
		Texture2D? image = null;
		try
		{
			using FileStream stream = File.OpenRead(Path.Combine(ctx.ModFolder, "assets", "diary", file));
			image = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
		}
		catch (Exception ex)
		{
			ctx.Log.Warn($"日记的画 {file} 读不出来：{ex.Message}");
		}
		Loaded[file] = image;
		return image;
	}

	public static string SketchCaption(ShellContext ctx, PinnedSketch pin) => Tr.Get("shell.book.sketch", "{{date}} · 这天写到了{{name}}", ("date", ShellText.Date(pin.Entry.Day, pin.Entry.Season, pin.Entry.Year)), ("name", ctx.Display(pin.Sketch.Npc)));

	public static string PhotoCaption(ShellContext ctx, AISV.Core.Messenger.PhotoRecord r)
	{
		string who = ctx.Display(r.Sender);
		return ShellText.Date(r.Day, r.Season, r.Year) + " · " + (r.LocationDisplay.Length > 0 ? r.LocationDisplay : r.LocationName) + " · "
			+ (string.Equals(r.Subject, r.Sender, StringComparison.OrdinalIgnoreCase) ? Tr.Get("shell.book.selfie", "{{name}}的自拍", ("name", who)) : Tr.Get("shell.book.shotBy", "{{name}}拍的", ("name", who)));
	}
}
