using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.Core.World.Market;
using AISV.Game.Dialogue;
using AISV.Game.World;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace AISV.UI.Shell;

/// <summary>世界 · 市场：今天出货箱的行情（红涨绿跌），背包里有的、昨天卖过的、热门的排前面。</summary>
internal sealed class MarketPage : ShellPage
{
	private static readonly Color Up = new Color(214, 69, 65);

	private static readonly Color UpSoft = new Color(252, 228, 224);

	private static readonly Color Down = new Color(46, 150, 80);

	private static readonly Color DownSoft = new Color(222, 241, 226);

	private const int RowHeight = 84;

	private readonly Scroller _scroll = new Scroller();

	private IReadOnlyList<MarketRow> _rows = Array.Empty<MarketRow>();

	private IReadOnlyList<string> _hot = Array.Empty<string>();

	private string _footer = "";

	private Rectangle _list;

	public MarketPage(ShellContext ctx) : base(ctx)
	{
	}

	private MarketService? Market => Ctx.Market;

	public override void Reload()
	{
		MarketService? m = Market;
		if (m?.Enabled != true || m.State is not MarketState s)
		{
			_rows = Array.Empty<MarketRow>();
			_hot = Array.Empty<string>();
			_footer = "";
			return;
		}
		_rows = m.BoardRows();
		_hot = m.HotNames();
		List<string> parts = new List<string>();
		int gold = m.LastNightGold(s);
		if (gold != 0)
		{
			parts.Add(gold > 0
				? Tr.Get("market.lastNightMore", "昨晚出货按行情比平常多拿了 {{gold}}g。", ("gold", gold))
				: Tr.Get("market.lastNightLess", "昨晚出货按行情比平常少拿了 {{gold}}g。", ("gold", -gold)));
		}
		IReadOnlyList<string> sales = m.NpcSalesLines();
		if (sales.Count > 0)
		{
			parts.Add(Tr.Get("market.npcSales", "昨天镇上也有人出货：") + string.Join("、", sales));
		}
		if (parts.Count == 0)
		{
			parts.Add(Tr.Get("market.footerNote", "价格只影响出货箱结算；直接卖给商店还是原价。"));
		}
		_footer = string.Join("\n", parts);
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _list.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		const float footScale = 0.72f;
		List<string> footLines = AisvTheme.WrapLines(_footer, area.Width - 48, footScale);
		int footH = _footer.Length == 0 ? 0 : 20 + footLines.Count * AisvTheme.LineHeight(footScale);
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, _hot.Count > 0 ? 96 : 72);
		Rectangle footer = new Rectangle(area.X, area.Bottom - footH, area.Width, footH);
		_list = new Rectangle(area.X, header.Bottom, area.Width, footer.Y - header.Bottom);
		DrawHeader(b, header);
		DrawList(b);
		if (footH == 0)
		{
			return;
		}
		AisvTheme.HLine(b, footer.X, footer.Y, footer.Width, AisvTheme.Divider);
		int fy = footer.Y + 10;
		foreach (string line in footLines)
		{
			AisvTheme.DrawText(b, line, new Vector2(footer.X + 24, fy), AisvTheme.TextMuted, footScale);
			fy += AisvTheme.LineHeight(footScale);
		}
	}

	private void DrawHeader(SpriteBatch b, Rectangle r)
	{
		AisvTheme.Fill(b, r, AisvTheme.PageDeep);
		AisvTheme.HLine(b, r.X, r.Bottom - 2, r.Width, AisvTheme.Divider);
		AisvTheme.Bold(b, Tr.Get("market.title", "市场行情"), new Vector2(r.X + 20, r.Y + 12), AisvTheme.Text, 1.15f);
		AisvTheme.DrawText(b, Tr.Get("market.subtitle", "出货箱结算价 · 每天早上更新 · 涨跌和昨天比"), new Vector2(r.X + 20, r.Y + 46), AisvTheme.TextFaint, 0.7f);
		if (_hot.Count > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate(Tr.Get("market.hotThisWeek", "本周镇上想要：") + string.Join("、", _hot), r.Width - 40, 0.72f), new Vector2(r.X + 20, r.Y + 68), AisvTheme.Gold, 0.72f);
		}
	}

	private void DrawList(SpriteBatch b)
	{
		if (_rows.Count == 0)
		{
			string empty = Market?.Enabled == true ? Tr.Get("market.empty", "今天还没有行情。睡一觉，明天早上就有了。") : Tr.Get("market.disabled", "市场已关闭，出货按原价结算。可以在设置的“世界”里打开。");
			AisvTheme.TextCentered(b, empty, _list, AisvTheme.TextFaint, 0.85f);
			return;
		}
		AisvTheme.BeginClip(b, _list);
		int x = _list.X + 24, w = _list.Width - 48;
		int y = _list.Y + 14 - _scroll.Offset;
		foreach (MarketRow row in _rows)
		{
			if (y + RowHeight >= _list.Y && y <= _list.Bottom)
			{
				DrawRow(b, row, new Rectangle(x, y, w, RowHeight));
			}
			y += RowHeight + 10;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _list, y + 4);
	}

	private static void DrawRow(SpriteBatch b, MarketRow row, Rectangle r)
	{
		AisvTheme.DrawCard(b, r);
		Rectangle icon = new Rectangle(r.X + 14, r.Center.Y - 28, 56, 56);
		try
		{
			ParsedItemData? data = ItemRegistry.GetData(row.ItemId);
			if (data != null)
			{
				b.Draw(data.GetTexture(), icon, data.GetSourceRect(), Color.White);
			}
		}
		catch
		{
		}
		bool up = row.ChangePercent > 0.05f, down = row.ChangePercent < -0.05f;
		Color tone = up ? Up : down ? Down : AisvTheme.TextFaint;
		string change = up ? "+" + row.ChangePercent.ToString("0.0") + "%" : down ? "-" + (-row.ChangePercent).ToString("0.0") + "%" : Tr.Get("market.flat", "持平");
		Rectangle pill = new Rectangle(r.Right - 14 - 112, r.Center.Y - 18, 112, 36);
		AisvTheme.FillRounded(b, pill, up ? UpSoft : down ? DownSoft : AisvTheme.Divider, 10);
		AisvTheme.TextCentered(b, change, pill, tone, 0.72f);
		string price = row.TodayPrice + "g";
		string basis = Tr.Get("market.base", "原价 {{gold}}g", ("gold", row.BasePrice));
		float pw = AisvTheme.Width(price), bw = AisvTheme.Width(basis, 0.62f);
		int priceRight = pill.X - 14;
		AisvTheme.Bold(b, price, new Vector2(priceRight - pw, r.Y + 12), up || down ? tone : AisvTheme.Text);
		AisvTheme.DrawText(b, basis, new Vector2(priceRight - bw, r.Bottom - 30), AisvTheme.TextFaint, 0.62f);
		int sparkW = Math.Min(140, r.Width / 6);
		int sparkRight = priceRight - (int)Math.Max(pw, bw) - 18;
		if (sparkW >= 60)
		{
			Sparkline(b, new Rectangle(sparkRight - sparkW, r.Y + 16, sparkW, r.Height - 32), row.History, tone);
		}
		int tx = icon.Right + 14;
		AisvTheme.Bold(b, AisvTheme.Truncate(row.DisplayName, sparkRight - sparkW - tx - 16, 0.92f), new Vector2(tx, r.Y + 12), AisvTheme.Text, 0.92f);
		float cx = tx;
		int chipRight = sparkW >= 60 ? sparkRight - sparkW - 12 : sparkRight - 12;
		void Chip(string text, Color fill, Color ink)
		{
			if (cx + AisvTheme.ChipWidth(text, 0.62f) <= chipRight)
			{
				cx = AisvTheme.Chip(b, new Vector2(cx, r.Bottom - 34), text, fill, ink, 0.62f).Right + 6;
			}
		}
		if (row.Hot)
		{
			Chip(Tr.Get("market.tag.hot", "热门"), UpSoft, Up);
		}
		if (row.OutOfSeason)
		{
			Chip(Tr.Get("market.tag.offSeason", "反季"), AisvTheme.GoldSoft, AisvTheme.Gold);
		}
		if (row.Oversupplied)
		{
			Chip(Tr.Get("market.tag.glut", "供应多"), DownSoft, Down);
		}
		if (row.PlayerHas)
		{
			Chip(Tr.Get("market.tag.youHave", "背包里有"), AisvTheme.AccentSoft, AisvTheme.AccentDark);
		}
		if (row.PlayerSold > 0)
		{
			Chip(Tr.Get("market.tag.youSold", "你昨天卖了 {{count}}", ("count", row.PlayerSold)), AisvTheme.Divider, AisvTheme.TextMuted);
		}
	}

	/// <summary>7 天走势；中间那条细线是原价。</summary>
	private static void Sparkline(SpriteBatch b, Rectangle r, IReadOnlyList<float> history, Color tone)
	{
		float min = Math.Min(0.95f, history.Count > 0 ? history.Min() : 1f);
		float max = Math.Max(1.05f, history.Count > 0 ? history.Max() : 1f);
		float Y(float v) => r.Bottom - (v - min) / (max - min) * r.Height;
		AisvTheme.HLine(b, r.X, (int)Y(1f), r.Width, AisvTheme.Divider);
		if (history.Count == 0)
		{
			return;
		}
		float step = r.Width / (float)(MarketRules.HistoryDays - 1);
		float start = r.Right - step * (history.Count - 1);
		for (int i = 1; i < history.Count; i++)
		{
			AisvTheme.Line(b, new Vector2(start + step * (i - 1), Y(history[i - 1])), new Vector2(start + step * i, Y(history[i])), tone, 3f);
		}
		AisvTheme.Fill(b, new Rectangle((int)(start + step * (history.Count - 1)) - 3, (int)Y(history[^1]) - 3, 7, 7), tone);
	}
}
