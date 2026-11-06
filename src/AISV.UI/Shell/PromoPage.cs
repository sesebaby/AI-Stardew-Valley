using System;
using System.Collections.Generic;
using AISV.Core.Promo;
using AISV.Core.Text;
using AISV.Game.Promo;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>推荐：服务器后台的公告和推荐（文字、图、按钮），打开链接前先问一句。</summary>
internal sealed class PromoPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private PromoItem? _confirm;

	private Rectangle _view;

	public PromoPage(ShellContext ctx) : base(ctx)
	{
		ctx.Promo?.Content.Refresh();
	}

	public override string Title => Tr.Get("promo.title", "推荐");

	private ServerContentService? Content => Ctx.Promo?.Content;

	public override void Scroll(int direction)
	{
		if (_confirm == null)
		{
			_scroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		if (_confirm != null)
		{
			if (key == Keys.Escape)
			{
				_confirm = null;
			}
			else if (key == Keys.Enter)
			{
				Open();
			}
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		ServerContent content = Content?.Content ?? ServerContent.Empty;
		AisvTheme.BeginClip(b, area);
		const int pad = 28;
		int x = area.X + pad, w = area.Width - pad * 2;
		int y = area.Y + 18 - _scroll.Offset;
		AisvTheme.DrawText(b, Tr.Get("promo.sub", "mod 作者的消息和推荐"), new Vector2(x + 6, y), AisvTheme.TextMuted, 0.8f);
		y += AisvTheme.LineHeight(0.8f) + 18;
		if (content.Announcement.Length > 0)
		{
			y = Announcement(b, ShellText.Clean(content.Announcement), x, y, w) + 16;
		}
		if (content.Promo.Count == 0)
		{
			Rectangle r = new Rectangle(x, y, w, 110);
			AisvTheme.DrawCard(b, r);
			string empty = Content?.Available == true ? Tr.Get("promo.empty", "暂时没有推荐，过几天再来看看。") : Tr.Get("promo.noServer", "这个版本没有内置服务器，没有推荐内容。");
			AisvTheme.TextCentered(b, empty, r, AisvTheme.TextFaint, 0.85f);
			y = r.Bottom + 16;
		}
		foreach (PromoItem item in content.Promo)
		{
			y = Item(b, item, x, y, w, area, hits) + 16;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, area, y, 12);
		if (_confirm != null)
		{
			Kit.ConfirmLink(b, area, hits, _confirm.LinkUrl, "", Open, () =>
			{
				Game1.playSound("bigDeSelect");
				_confirm = null;
			});
		}
	}

	/// <summary>后台公告（金色横条）。</summary>
	public static int Announcement(SpriteBatch b, string text, int x, int y, int w)
	{
		int textW = w - 96;
		List<string> lines = AisvTheme.WrapLines(text, textW, 0.8f);
		int h = Math.Max(58, lines.Count * AisvTheme.LineHeight(0.8f) + 28);
		Rectangle r = new Rectangle(x, y, w, h);
		AisvTheme.Bordered(b, r, AisvTheme.GoldSoft, AisvTheme.Gold, 12);
		AisvTheme.Glyph(b, new Rectangle(x + 14, y + (h - 38) / 2, 38, 38), AisvTheme.Gold, "告");
		int ty = y + 14;
		foreach (string line in lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(x + 66, ty), AisvTheme.Text, 0.8f);
			ty += AisvTheme.LineHeight(0.8f);
		}
		return r.Bottom;
	}

	private int Item(SpriteBatch b, PromoItem item, int x, int y, int w, Rectangle clip, Hits hits)
	{
		Texture2D? tex = item.Image.Length > 0 ? Content?.Texture(item.Image) : null;
		bool side = tex != null && w >= 760;
		int imgW = 0, imgH = 0;
		if (tex != null)
		{
			int maxW = side ? Math.Min(360, w * 2 / 5) : w - 40;
			int maxH = side ? 240 : 260;
			float s = Math.Min((float)maxW / tex.Width, (float)maxH / tex.Height);
			imgW = Math.Max(1, (int)(tex.Width * s));
			imgH = Math.Max(1, (int)(tex.Height * s));
		}
		int textX = side ? x + 20 + imgW + 24 : x + 20;
		int textW = side ? w - (textX - x) - 20 : w - 40;
		int titleH = AisvTheme.LineHeight(1f);
		List<string> body = AisvTheme.WrapLines(ShellText.Clean(item.Body), textW, 0.82f);
		bool link = item.LinkUrl.Length > 0;
		int textBlock = titleH + 8 + body.Count * AisvTheme.LineHeight(0.82f) + (link ? 20 + 52 : 0);
		int h = side ? Math.Max(imgH, textBlock) + 40 : (tex != null ? imgH + 16 : 0) + textBlock + 40;
		Rectangle card = new Rectangle(x, y, w, h);
		AisvTheme.DrawCard(b, card);
		int ty = y + 20;
		if (tex != null)
		{
			Rectangle img = side ? new Rectangle(x + 20, y + 20, imgW, imgH) : new Rectangle(x + (w - imgW) / 2, y + 20, imgW, imgH);
			b.Draw(tex, img, Color.White);
			if (!side)
			{
				ty = img.Bottom + 16;
			}
		}
		AisvTheme.Bold(b, AisvTheme.Truncate(ShellText.Clean(item.Title), textW), new Vector2(textX, ty), AisvTheme.Text);
		ty += titleH + 8;
		foreach (string line in body)
		{
			AisvTheme.DrawText(b, line, new Vector2(textX, ty), AisvTheme.Text, 0.82f);
			ty += AisvTheme.LineHeight(0.82f);
		}
		if (link)
		{
			string label = item.LinkText.Length > 0 ? item.LinkText : Tr.Get("promo.go", "去看看");
			int bw = Math.Min(textW, (int)AisvTheme.Width(label, 0.85f) + 64);
			Rectangle btn = new Rectangle(textX, ty + 20, Math.Max(160, bw), 52);
			AisvTheme.Button(b, btn, label, primary: true, hover: AisvTheme.Hover(btn, clip), scale: 0.85f);
			if (_confirm == null)
			{
				hits.Add(btn, () =>
				{
					Game1.playSound("bigSelect");
					_confirm = item;
				}, clip);
			}
		}
		return card.Bottom;
	}

	private void Open()
	{
		PromoItem? item = _confirm;
		_confirm = null;
		if (item == null)
		{
			return;
		}
		LinkResult r = ExternalLink.Open(item.LinkUrl, Ctx.Log);
		Game1.playSound("newArtifact");
		Host.Notice(LinkNotice(r));
	}

	public static string LinkNotice(LinkResult r) => r switch
	{
		LinkResult.Opened => Tr.Get("promo.opened", "已在浏览器里打开。"),
		LinkResult.Copied => Tr.Get("promo.copied", "打不开浏览器，链接已复制到剪贴板。"),
		_ => Tr.Get("promo.failed", "打不开这个链接。")
	};
}
