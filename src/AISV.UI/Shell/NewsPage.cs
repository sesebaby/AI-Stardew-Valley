using System;
using System.Collections.Generic;
using AISV.Core.News;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Social;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace AISV.UI.Shell;

/// <summary>世界 · 周报：一张报纸。报头、左边镇上新闻（头条放大，"山谷之外"是框起来的边栏）、右边本周行情和新委托，底下翻往期。</summary>
internal sealed class NewsPage : ShellPage
{
	private static readonly Color Paper = new Color(250, 244, 228);

	private static readonly Color PaperDeep = new Color(242, 233, 210);

	private static readonly Color Ink = new Color(52, 34, 28);

	private static readonly Color InkSoft = new Color(120, 98, 80);

	private static readonly Color Rule = new Color(214, 196, 162);

	private static readonly Color Red = new Color(190, 58, 48);

	private static readonly Color Up = new Color(214, 69, 65);

	private static readonly Color Down = new Color(46, 150, 80);

	private static readonly Color UpSoft = new Color(252, 228, 224);

	private const float BodyScale = 0.82f;

	private const float LeadScale = 0.95f;

	private readonly Scroller _scroll = new Scroller();

	private IReadOnlyList<WeeklyNews> _editions = Array.Empty<WeeklyNews>();

	private int _index;

	private int _version = -1;

	private Rectangle _body;

	public NewsPage(ShellContext ctx) : base(ctx)
	{
	}

	private WeeklyNewsService? News => Ctx.News;

	public override void Reload()
	{
		WeeklyNewsService? news = News;
		if (news == null)
		{
			_editions = Array.Empty<WeeklyNews>();
			return;
		}
		if (news.Version == _version && _editions.Count > 0)
		{
			return;
		}
		string? shown = _index < _editions.Count ? Key(_editions[_index]) : null;
		_version = news.Version;
		_editions = news.Editions();
		_index = 0;
		for (int i = 0; shown != null && i < _editions.Count; i++)
		{
			if (Key(_editions[i]) == shown)
			{
				_index = i;
			}
		}
	}

	private static string Key(WeeklyNews n) => n.Year + "." + n.Season + "." + n.Day;

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (key == Keys.Left && _index + 1 < _editions.Count)
		{
			Go(_index + 1);
			return true;
		}
		if (key == Keys.Right && _index > 0)
		{
			Go(_index - 1);
			return true;
		}
		return _scroll.PageKeys(key, _body.Height - 80);
	}

	private void Go(int index)
	{
		Game1.playSound("shwip");
		_index = Math.Clamp(index, 0, Math.Max(0, _editions.Count - 1));
		_scroll.Reset();
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		AisvTheme.Fill(b, area, Paper);
		if (_editions.Count == 0)
		{
			Rectangle mast0 = new Rectangle(area.X, area.Y, area.Width, 140);
			DrawMasthead(b, mast0, null);
			Rectangle rest = new Rectangle(area.X + 60, mast0.Bottom, area.Width - 120, area.Bottom - mast0.Bottom);
			List<string> lines = AisvTheme.WrapLines(Tr.Get("news.none", "第一期《鹈鹕镇周报》会在本月 7 日晚上出刊，第二天早上送到你的信箱。"), rest.Width, 0.85f);
			int y = rest.Center.Y - lines.Count * AisvTheme.LineHeight(0.85f) / 2 - 20;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(rest.Center.X - AisvTheme.Width(line, 0.85f) / 2, y), InkSoft, 0.85f);
				y += AisvTheme.LineHeight(0.85f);
			}
			return;
		}
		WeeklyNews n = _editions[Math.Clamp(_index, 0, _editions.Count - 1)];
		Rectangle mast = new Rectangle(area.X, area.Y, area.Width, 140);
		Rectangle nav = new Rectangle(area.X, area.Bottom - 58, area.Width, 58);
		_body = new Rectangle(area.X, mast.Bottom, area.Width, nav.Y - mast.Bottom);
		DrawMasthead(b, mast, n);
		DrawBody(b, n);
		DrawNav(b, nav, hits);
	}

	/// <summary>报名用游戏的大字；粗细两道线下面是期号、日期、出刊日，再一道细线收住。</summary>
	private static void DrawMasthead(SpriteBatch b, Rectangle r, WeeklyNews? n)
	{
		const int pad = 32;
		string title = Tr.Get("news.paperTitle", "鹈鹕镇周报");
		SpriteFont font = Game1.dialogueFont;
		float ts = Math.Min(1.6f, 72f / Math.Max(1f, font.MeasureString("鹈").Y));
		Vector2 size = font.MeasureString(title) * ts;
		Vector2 tp = new Vector2(r.Center.X - size.X / 2, r.Y + 8);
		b.DrawString(font, title, tp + new Vector2(2f, 2f), Rule, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);
		b.DrawString(font, title, tp, Ink, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);
		int wing = (int)Math.Min(160, (r.Width - size.X) / 2 - pad - 24);
		if (wing > 40)
		{
			int wy = (int)(tp.Y + size.Y / 2);
			AisvTheme.HLine(b, (int)tp.X - 20 - wing, wy - 3, wing, Ink, 2);
			AisvTheme.HLine(b, (int)tp.X - 20 - wing, wy + 2, wing, Ink, 1);
			AisvTheme.HLine(b, (int)(tp.X + size.X) + 20, wy - 3, wing, Ink, 2);
			AisvTheme.HLine(b, (int)(tp.X + size.X) + 20, wy + 2, wing, Ink, 1);
		}
		int ruleY = r.Bottom - 46;
		AisvTheme.HLine(b, r.X + pad, ruleY, r.Width - pad * 2, Ink, 3);
		AisvTheme.HLine(b, r.X + pad, ruleY + 6, r.Width - pad * 2, Ink, 1);
		AisvTheme.HLine(b, r.X + pad, r.Bottom - 6, r.Width - pad * 2, Ink, 1);
		const float s = 0.7f;
		int ty = ruleY + 13;
		if (n != null)
		{
			AisvTheme.Bold(b, Tr.Get("news.issue", "第{{issue}}期", ("issue", n.Edition)), new Vector2(r.X + pad + 4, ty), Ink, s);
			string date = Tr.Get("news.date", "第{{year}}年{{season}}{{day}}日", ("year", n.Year), ("season", ShellText.Season(n.Season)), ("day", n.Day));
			AisvTheme.DrawText(b, date, new Vector2(r.Center.X - AisvTheme.Width(date, s) / 2, ty), Ink, s);
		}
		string weekly = Tr.Get("news.weekly", "每周日出刊");
		AisvTheme.DrawText(b, weekly, new Vector2(r.Right - pad - 4 - AisvTheme.Width(weekly, s), ty), InkSoft, s);
	}

	private void DrawBody(SpriteBatch b, WeeklyNews n)
	{
		const int pad = 32, gap = 36;
		int inner = _body.Width - pad * 2;
		int leftW = (inner - gap) * 56 / 100;
		int rightW = inner - gap - leftW;
		int leftX = _body.X + pad, rightX = leftX + leftW + gap;
		int top = _body.Y + 16 - _scroll.Offset;
		AisvTheme.BeginClip(b, _body);
		int leftBottom = DrawNewsColumn(b, n, leftX, top, leftW);
		int rightBottom = DrawMarket(b, n, rightX, top, rightW);
		rightBottom = DrawCommissions(b, n, rightX, rightBottom + 22, rightW);
		if (n.Purse.Count > 0)
		{
			rightBottom = Column(b, Tr.Get("news.money", "本周钱事"), rightX, rightBottom + 22, rightW);
			foreach (string line in n.Purse)
			{
				rightBottom = Paragraph(b, "· " + ShellText.Clean(line), rightX, rightBottom, rightW, Ink);
			}
		}
		int bottom = Math.Max(leftBottom, rightBottom);
		AisvTheme.VLine(b, leftX + leftW + gap / 2, top + 4, bottom - top - 8, Rule, 1);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _body, bottom + 16, 10);
	}

	private int DrawNewsColumn(SpriteBatch b, WeeklyNews n, int x, int y, int w)
	{
		y = Column(b, Tr.Get("news.town", "镇上新闻"), x, y, w);
		if (n.Stories.Count == 0)
		{
			y = Paragraph(b, Tr.Get("news.quiet", "这周镇上风平浪静，没什么新闻。"), x, y, w, InkSoft);
		}
		for (int i = 0; i < n.Stories.Count; i++)
		{
			if (i > 0)
			{
				Ornament(b, x + w / 2, y + 2);
				y += 18;
			}
			string text = ShellText.Clean(n.Stories[i].Text);
			y = i == 0 ? Paragraph(b, text, x, y, w, Ink, LeadScale, bold: true) : Paragraph(b, text, x, y, w, Ink);
		}
		if (News?.BeyondShown == true && n.FarBody.Trim().Length > 0)
		{
			y += 18;
			string title = n.FarTitle.Trim().Length > 0 ? n.FarTitle : Tr.Get("news.beyond", "山谷之外");
			List<string> lines = AisvTheme.WrapLines(ShellText.Clean(n.FarBody), w - 40, BodyScale);
			int h = 18 + AisvTheme.LineHeight(0.9f) + 10 + lines.Count * AisvTheme.LineHeight(BodyScale) + 18;
			Rectangle box = new Rectangle(x, y, w, h);
			AisvTheme.Fill(b, box, PaperDeep);
			AisvTheme.HLine(b, box.X, box.Y, box.Width, Ink, 2);
			AisvTheme.HLine(b, box.X, box.Bottom - 1, box.Width, Ink, 1);
			AisvTheme.VLine(b, box.X, box.Y, box.Height, Rule, 1);
			AisvTheme.VLine(b, box.Right - 1, box.Y, box.Height, Rule, 1);
			int ty = box.Y + 18;
			AisvTheme.Bold(b, title, new Vector2(box.X + 20, ty), Red, 0.9f);
			ty += AisvTheme.LineHeight(0.9f) + 10;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(box.X + 20, ty), Ink, BodyScale);
				ty += AisvTheme.LineHeight(BodyScale);
			}
			y = box.Bottom + 6;
		}
		return y;
	}

	private int DrawMarket(SpriteBatch b, WeeklyNews n, int x, int y, int w)
	{
		y = Column(b, Tr.Get("news.market", "本周行情"), x, y, w);
		if (n.Prices.Count == 0)
		{
			return Paragraph(b, Tr.Get("news.noMarket", "这周没有登行情。"), x, y, w, InkSoft);
		}
		y = Paragraph(b, Tr.Get("news.marketNote", "出货箱结算价，出刊那天的；涨跌和一周前比。"), x, y, w, InkSoft, 0.66f);
		foreach (WeeklyMarketLine line in n.Prices)
		{
			Rectangle row = new Rectangle(x, y, w, 46);
			Rectangle icon = new Rectangle(row.X, row.Y + 5, 36, 36);
			try
			{
				ParsedItemData? data = ItemRegistry.GetData(line.ItemId);
				if (data != null)
				{
					b.Draw(data.GetTexture(), icon, data.GetSourceRect(), Color.White);
				}
			}
			catch
			{
			}
			int textY = row.Y + 12;
			string change = WeeklyNewsRules.ShortChange(line.WeekChange);
			Color tone = line.WeekChange > 0.05f ? Up : line.WeekChange < -0.05f ? Down : InkSoft;
			float changeW = Math.Max(AisvTheme.Width(change, 0.7f), AisvTheme.Width("+00.0%", 0.7f));
			AisvTheme.DrawText(b, change, new Vector2(row.Right - AisvTheme.Width(change, 0.7f), textY + 1), tone, 0.7f);
			string price = line.Price + "g";
			float priceW = AisvTheme.Width(price, 0.82f);
			float priceX = row.Right - changeW - 14 - priceW;
			AisvTheme.Bold(b, price, new Vector2(priceX, textY), Ink, 0.82f);
			int nameX = icon.Right + 10;
			string hotLabel = Tr.Get("market.tag.hot", "热门");
			float nameMax = priceX - 24 - nameX - (line.Hot ? AisvTheme.ChipWidth(hotLabel, 0.56f) + 8 : 0);
			string name = AisvTheme.Truncate(line.Name, nameMax, 0.8f);
			AisvTheme.Bold(b, name, new Vector2(nameX, textY), Ink, 0.8f);
			float after = nameX + AisvTheme.Width(name, 0.8f) + 8;
			if (line.Hot)
			{
				after = AisvTheme.Chip(b, new Vector2(after, textY + 1), hotLabel, UpSoft, Up, 0.56f).Right + 8;
			}
			Leader(b, (int)after, (int)priceX - 8, textY + (int)(AisvTheme.LineHeight(0.8f) * 0.7f));
			y = row.Bottom;
		}
		return y + 4;
	}

	private int DrawCommissions(SpriteBatch b, WeeklyNews n, int x, int y, int w)
	{
		y = Column(b, Tr.Get("news.commissions", "新委托"), x, y, w);
		if (n.Jobs.Count == 0)
		{
			return Paragraph(b, Tr.Get("news.noCommissions", "出刊时委托板上没有等人接的委托。"), x, y, w, InkSoft);
		}
		int today = Game1.Date.TotalDays;
		for (int k = 0; k < n.Jobs.Count; k++)
		{
			WeeklyCommissionLine c = n.Jobs[k];
			if (k > 0)
			{
				AisvTheme.HLine(b, x, y, w, Rule, 1);
				y += 10;
			}
			Rectangle face = new Rectangle(x, y + 2, 52, 52);
			Mugshot(b, face, c.Poster);
			int tx = face.Right + 14, tw = w - (tx - x);
			string who = c.PosterName.Length > 0 ? c.PosterName : Ctx.Display(c.Poster);
			float hw = c.Headline.Length > 0 ? AisvTheme.Width(c.Headline + "  ", 0.78f) : 0;
			if (hw > 0)
			{
				AisvTheme.Bold(b, c.Headline, new Vector2(tx, y), Red, 0.78f);
			}
			AisvTheme.DrawText(b, AisvTheme.Truncate(who, tw - hw, 0.74f), new Vector2(tx + hw, y + 1), InkSoft, 0.74f);
			int ty = y + AisvTheme.LineHeight(0.78f) + 2;
			foreach (string line in AisvTheme.WrapLines(ShellText.Clean(c.Task), tw, 0.72f))
			{
				AisvTheme.DrawText(b, line, new Vector2(tx, ty), Ink, 0.72f);
				ty += AisvTheme.LineHeight(0.72f);
			}
			int left = c.LastDay - today;
			string due = left > 0 ? Tr.Get("news.daysLeft", "{{days}} 天内有效", ("days", left)) : left == 0 ? Tr.Get("news.dueToday", "今天截止") : Tr.Get("news.expired", "已截止");
			AisvTheme.DrawText(b, Tr.Get("news.reward", "酬劳 {{gold}}g", ("gold", c.RewardGold)) + " · " + due, new Vector2(tx, ty + 2), left <= 0 ? Up : AisvTheme.Gold, 0.68f);
			ty += 2 + AisvTheme.LineHeight(0.68f);
			y = Math.Max(face.Bottom, ty) + 12;
		}
		return Paragraph(b, Tr.Get("news.commissionsNote", "接委托请到外壳“世界”里的“委托”。"), x, y + 4, w, InkSoft, 0.66f);
	}

	private void DrawNav(SpriteBatch b, Rectangle r, Hits hits)
	{
		AisvTheme.HLine(b, r.X + 32, r.Y, r.Width - 64, Ink, 1);
		bool hasPrev = _index + 1 < _editions.Count, hasNext = _index > 0;
		Rectangle prev = new Rectangle(r.X + 28, r.Y + 8, 150, 42);
		Rectangle next = new Rectangle(r.Right - 178, r.Y + 8, 150, 42);
		NavLink(b, prev, Tr.Get("news.prev", "‹ 上一期"), hasPrev, false);
		NavLink(b, next, Tr.Get("news.next", "下一期 ›"), hasNext, true);
		if (hasPrev)
		{
			hits.Add(prev, () => Go(_index + 1));
		}
		if (hasNext)
		{
			hits.Add(next, () => Go(_index - 1));
		}
		AisvTheme.TextCentered(b, Tr.Get("news.page", "往期 {{n}} / {{total}}", ("n", _editions.Count - _index), ("total", _editions.Count)), r, InkSoft, 0.72f);
	}

	private static void NavLink(SpriteBatch b, Rectangle r, string label, bool enabled, bool right)
	{
		bool hover = enabled && AisvTheme.Hover(r);
		if (hover)
		{
			AisvTheme.FillRounded(b, r, PaperDeep, 8);
		}
		Color c = enabled ? hover ? Red : Ink : Rule;
		float w = AisvTheme.Width(label, 0.82f);
		float x = right ? r.Right - 12 - w : r.X + 12;
		AisvTheme.Bold(b, label, new Vector2(x, r.Center.Y - AisvTheme.LineHeight(0.82f) / 2), c, 0.82f);
	}

	/// <summary>栏目标题：红色小方块、标题，下面一道墨线。</summary>
	private static int Column(SpriteBatch b, string title, int x, int y, int w)
	{
		int lh = AisvTheme.LineHeight(0.95f);
		AisvTheme.Fill(b, new Rectangle(x, y + lh / 2 - 5, 10, 10), Red);
		AisvTheme.Bold(b, title, new Vector2(x + 18, y), Ink, 0.95f);
		int ly = y + lh + 6;
		AisvTheme.HLine(b, x, ly, w, Ink, 2);
		return ly + 14;
	}

	private static int Paragraph(SpriteBatch b, string text, int x, int y, int w, Color color, float scale = BodyScale, bool bold = false)
	{
		foreach (string line in AisvTheme.WrapLines(text, w, scale))
		{
			if (bold)
			{
				AisvTheme.Bold(b, line, new Vector2(x, y), color, scale);
			}
			else
			{
				AisvTheme.DrawText(b, line, new Vector2(x, y), color, scale);
			}
			y += AisvTheme.LineHeight(scale);
		}
		return y + 8;
	}

	/// <summary>两条新闻之间：短线中间一个小菱形。</summary>
	private static void Ornament(SpriteBatch b, int cx, int y)
	{
		AisvTheme.HLine(b, cx - 54, y + 4, 44, Rule, 1);
		AisvTheme.HLine(b, cx + 10, y + 4, 44, Rule, 1);
		for (int i = 0; i < 4; i++)
		{
			AisvTheme.Fill(b, new Rectangle(cx - i, y + i, i * 2 + 1, 1), Rule);
			AisvTheme.Fill(b, new Rectangle(cx - i, y + 8 - i, i * 2 + 1, 1), Rule);
		}
	}

	/// <summary>菜单式的点线，把品名引到价钱。</summary>
	private static void Leader(SpriteBatch b, int from, int to, int y)
	{
		for (int x = from + 2; x + 2 <= to; x += 7)
		{
			AisvTheme.Fill(b, new Rectangle(x, y, 2, 2), Rule);
		}
	}

	/// <summary>村民的小头像（游戏里的半身小图），没有时用名字的第一个字。</summary>
	private void Mugshot(SpriteBatch b, Rectangle r, string npcName)
	{
		AisvTheme.Fill(b, r, PaperDeep);
		AisvTheme.HLine(b, r.X, r.Y, r.Width, Rule, 1);
		AisvTheme.HLine(b, r.X, r.Bottom - 1, r.Width, Rule, 1);
		AisvTheme.VLine(b, r.X, r.Y, r.Height, Rule, 1);
		AisvTheme.VLine(b, r.Right - 1, r.Y, r.Height, Rule, 1);
		NPC? npc = ShellText.Npc(npcName);
		if (npc?.Sprite?.Texture is Texture2D tex && !tex.IsDisposed)
		{
			Rectangle src = npc.getMugShotSourceRect();
			float k = Math.Min((r.Width - 8f) / src.Width, (r.Height - 8f) / src.Height);
			int w = (int)(src.Width * k), h = (int)(src.Height * k);
			b.Draw(tex, new Rectangle(r.Center.X - w / 2, r.Bottom - 4 - h, w, h), src, Color.White);
			return;
		}
		string shown = Ctx.Display(npcName);
		AisvTheme.TextCentered(b, shown.Length > 0 ? shown.Substring(0, 1) : "?", r, InkSoft, 0.9f);
	}
}
