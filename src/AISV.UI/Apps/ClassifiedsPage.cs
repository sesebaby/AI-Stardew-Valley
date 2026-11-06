using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.Core.World.Ledger;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>分类广告：软木板上钉着村民登的纸条，卖掉的盖章；有实物的可以直接买下。</summary>
internal sealed class ClassifiedsPage : ShellPage
{
	private static readonly Color Cork = new Color(196, 152, 104);

	private static readonly Color CorkDark = new Color(150, 108, 70);

	private static readonly Color[] Papers = { new Color(255, 251, 236), new Color(255, 244, 214), new Color(240, 248, 236), new Color(250, 240, 246) };

	private static readonly Color Stamp = new Color(196, 52, 48);

	private readonly Scroller _scroll = new Scroller();

	private List<Ad> _ads = new List<Ad>();

	private Rectangle _view;

	private string _notice = "";

	public ClassifiedsPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("ads.page", "分类广告");

	public override void Reload()
	{
		int today = AppData.Today;
		_ads = (Ctx.Ledger?.Ads() ?? new List<Ad>()).Where(a => Ctx.Npcs.IsEnabled(a.Npc))
			.OrderBy(a => a.SoldDay >= 0 || today > a.ExpiresDay).ThenByDescending(a => a.Day).ToList();
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		Rectangle board = new Rectangle(area.X + 12, area.Y + 12, area.Width - 24, area.Height - 24);
		AisvTheme.FillRounded(b, board, CorkDark, 14);
		AisvTheme.FillRounded(b, new Rectangle(board.X + 8, board.Y + 8, board.Width - 16, board.Height - 16), Cork, 10);
		Rectangle inner = new Rectangle(board.X + 20, board.Y + 18, board.Width - 40, board.Height - 36);
		if (Ctx.Ledger is not { Enabled: true })
		{
			Kit.Empty(b, new Rectangle(inner.X, inner.Y + inner.Height / 3, inner.Width, 80), Tr.Get("townLedger.off", "村民账本没开。"), Tr.Get("townLedger.offHint", "在设置 > 世界里打开。"));
			return;
		}
		if (_ads.Count == 0)
		{
			Kit.Empty(b, new Rectangle(inner.X, inner.Y + inner.Height / 3, inner.Width, 80), Tr.Get("ads.empty", "公告栏上空空的。"), Tr.Get("ads.emptyHint", "手头紧的村民会在这里登广告。"));
			return;
		}
		AisvTheme.BeginClip(b, inner);
		int cols = inner.Width >= 1100 ? 3 : inner.Width >= 680 ? 2 : 1;
		const int gap = 18;
		int cw = (inner.Width - 12 - gap * (cols - 1)) / cols;
		int[] colY = Enumerable.Repeat(inner.Y + 6 - _scroll.Offset, cols).ToArray();
		int today = AppData.Today;
		for (int i = 0; i < _ads.Count; i++)
		{
			int c = Array.IndexOf(colY, colY.Min());
			Ad ad = _ads[i];
			List<string> lines = AisvTheme.WrapLines(ShellText.Clean(ad.Text), cw - 40, 0.74f).Take(5).ToList();
			bool buyable = ad.Kind == AdKinds.Sell && ad.ItemId.Length > 0 && ad.Price > 0 && ClassifiedRules.Open(ad, today);
			int h = 20 + 34 + (ad.Price > 0 ? 34 : 4) + lines.Count * AisvTheme.LineHeight(0.74f) + 10 + 28 + 20 + (buyable ? 56 : 0);
			Rectangle note = new Rectangle(inner.X + c * (cw + gap), colY[c], cw, h);
			colY[c] = note.Bottom + gap;
			if (note.Bottom < inner.Y || note.Y > inner.Bottom)
			{
				continue;
			}
			Note(b, note, ad, lines, buyable, today, hits, inner, i);
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, inner, colY.Max() + 8, 6);
		if (_notice.Length > 0)
		{
			Rectangle tip = new Rectangle(board.Center.X - 220, board.Bottom - 70, 440, 48);
			AisvTheme.Bordered(b, tip, AisvTheme.Card, AisvTheme.CardBorder, 12);
			AisvTheme.TextCentered(b, _notice, tip, AisvTheme.Text, 0.8f);
		}
	}

	private void Note(SpriteBatch b, Rectangle note, Ad ad, List<string> lines, bool buyable, int today, Hits hits, Rectangle clip, int index)
	{
		bool closed = ad.SoldDay >= 0 || today > ad.ExpiresDay;
		AisvTheme.Fill(b, new Rectangle(note.X + 4, note.Y + 6, note.Width, note.Height), Color.Black * 0.12f);
		AisvTheme.Fill(b, note, Papers[index % Papers.Length] * (closed ? 0.82f : 1f));
		AisvTheme.FillRounded(b, new Rectangle(note.Center.X - 9, note.Y - 6, 18, 18), index % 2 == 0 ? new Color(210, 70, 60) : new Color(70, 120, 200), 9);
		int x = note.X + 20, w = note.Width - 40;
		int y = note.Y + 20;
		if (ad.ItemId.Length > 0 && ItemRegistry.Create(ad.ItemId, 1, ad.Quality, allowNull: true) is Item item)
		{
			item.drawInMenu(b, new Vector2(note.Right - 76, note.Y + 12), 0.8f, closed ? 0.5f : 1f, 0.9f, StackDrawType.Hide);
			w -= 60;
		}
		AisvTheme.Bold(b, AisvTheme.Truncate(ClassifiedRules.Title(ad), w, 0.86f), new Vector2(x, y), closed ? AisvTheme.TextMuted : AisvTheme.Text, 0.86f);
		y += 34;
		if (ad.Price > 0)
		{
			AisvTheme.Chip(b, new Vector2(x, y), $"{ad.Price:N0}g", AisvTheme.GoldSoft, AisvTheme.Gold, 0.66f);
		}
		y += ad.Price > 0 ? 34 : 4;
		foreach (string line in lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(x, y), closed ? AisvTheme.TextMuted : AisvTheme.Text, 0.74f);
			y += AisvTheme.LineHeight(0.74f);
		}
		y += 10;
		Kit.Avatar(b, new Rectangle(x, y, 28, 28), Ctx, ad.Npc);
		string by = $"{Ctx.Display(ad.Npc)} · {(today - ad.Day <= 0 ? Tr.Get("townLedger.today", "今天") : Tr.Get("townLedger.ago", "{{n}} 天前", ("n", today - ad.Day)))}";
		AisvTheme.DrawText(b, AisvTheme.Truncate(by, note.Width - 80, 0.64f), new Vector2(x + 36, y + 4), AisvTheme.TextFaint, 0.64f);
		if (buyable)
		{
			string label = Tr.Get("ads.buy", "买下（{{price}}g）", ("price", ad.Price));
			Rectangle btn = new Rectangle(x, note.Bottom - 56, Math.Min(note.Width - 40, (int)AisvTheme.Width(label, 0.78f) + 40), 42);
			AisvTheme.Button(b, btn, label, primary: true, hover: AisvTheme.Hover(btn, clip), disabled: Game1.player.Money < ad.Price, scale: 0.78f);
			string id = ad.Id;
			hits.Add(btn, () =>
			{
				string? problem = Ctx.Ledger?.BuyAd(id);
				Game1.playSound(problem == null ? "purchaseClick" : "cancel");
				_notice = problem ?? Tr.Get("ads.bought", "买下了，东西放进了背包。");
				Reload();
			}, clip);
		}
		if (closed)
		{
			string stamp = ad.SoldDay < 0 ? Tr.Get("ads.expired", "已撤下") : ad.Kind switch
			{
				AdKinds.Work => Tr.Get("ads.hired", "找到活了"),
				AdKinds.Show => Tr.Get("ads.showDone", "演完了"),
				AdKinds.Lost => Tr.Get("ads.found", "找到了"),
				_ => ad.Buyer == ClassifiedRules.Farmer ? Tr.Get("ads.soldYou", "已卖给你") : Tr.Get("ads.sold", "已售出")
			};
			int sw = (int)AisvTheme.Width(stamp, 0.95f) + 28;
			Rectangle s = new Rectangle(note.Right - sw - 16, note.Bottom - 54, sw, 40);
			AisvTheme.Bordered(b, s, Color.Transparent, Stamp * 0.85f, 6, 3);
			AisvTheme.TextCentered(b, stamp, s, Stamp * 0.9f, 0.95f);
		}
	}
}
