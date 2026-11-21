using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Store;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.UI.Apps;

/// <summary>小超市应用：今天卖了多少、钱箱、七天走势、货架上的货、Pierre 和 Joja 的经营状况、店员，还有开店说明。</summary>
internal sealed class FarmerMartPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private bool _guide;

	private Rectangle _view;

	public FarmerMartPage(ShellContext ctx, bool guide = false) : base(ctx)
	{
		_guide = guide;
	}

	public override string Title => Tr.Get("apps.mart", "小超市");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	private MartService? Mart => Ctx.Mart;

	public static string Summary(ShellContext ctx)
	{
		if (ctx.Mart is not MartService m || !m.HasShop)
		{
			return Tr.Get("mart.summaryNone", "去 Robin 那里建一间吧");
		}
		int today = m.Sales.On(Game1.Date.TotalDays, StoreSale.Ours).Sum(s => s.Total);
		return Tr.Get("mart.summary", "今天卖了 {{gold}}g", ("gold", today));
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		Hearth.BeginClip(b, area);
		int y = area.Y + 18 - _scroll.Offset;
		Rectangle guideBtn = new Rectangle(x + w - 200, y, 200, 44);
		Hearth.PressPlate(b, guideBtn, _guide ? Tr.Get("mart.hideGuide", "收起说明") : Tr.Get("mart.showGuide", "开店说明"), hover: Hearth.Hover(guideBtn, area), scale: 0.8f);
		hits.Add(guideBtn, () => _guide = !_guide, area);
		if (_guide || Mart?.HasShop != true)
		{
			y = Guide(b, x, y, w - 220) + 16;
		}
		if (Mart is MartService m && m.HasShop)
		{
			y = Today(b, hits, m, x, y + 8, w) + 18;
			y = Stock(b, m, x, y, w) + 18;
			y = Rivals(b, hits, m, x, y, w) + 18;
			y = Clerks(b, hits, m, x, y, w) + 18;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + _scroll.Offset, 12);
	}

	private static int Guide(SpriteBatch b, int x, int y, int w)
	{
		string text = Tr.Get("mart.guide", "【在哪里建】去 Robin 的木匠铺（镇子北边、山脚下），点柜台 > 建造农场建筑 > 选「农夫小超市」（15000g、木材 300、石头 200），在农场上挑个空地放下，两天后建好，从正面的门进去。和 Farmer's Store 的分店不是一回事，两家可以同时开。\n【开店】建好后会送你两张陈列台和一个收银台（不够可以在 Robin 店里买），摆进店里。拿着要卖的东西点陈列台就上架，空手点可以改价钱或取下。\n【营业】每天 9:00 - 17:00（设置里能改）村民会来买：你在店里时他们进门、挑货、到收银台结账；你不在时也照样卖，钱放进钱箱，点收银台取出。\n【竞争】卖得越好，Pierre 和 Joja 的生意越差：会吃紧、亏损，甚至停业整顿（只影响 AI星露谷 的经济，种子一定买得到）。亏损以后他们可能上门理论。\n【注意】偷来的东西放在店里卖，失主和熟人可能认出来。");
		foreach (string line in Hearth.WrapLines(text, w, 0.78f, paragraphGap: true))
		{
			Hearth.Ink(b, line, new Vector2(x, y), Hearth.Quill, 0.78f);
			y += Hearth.LineHeight(0.78f);
		}
		return y;
	}

	private int Today(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		int day = Game1.Date.TotalDays;
		int today = m.Sales.On(day, StoreSale.Ours).Sum(s => s.Total);
		int cash = m.State?.Cashbox ?? 0;
		bool open = m.IsOpen(Game1.timeOfDay);
		Hearth.InkBold(b, open ? Tr.Get("mart.openNow", "营业中") : Tr.Get("mart.closedLabel", "打烊了"), new Vector2(x, y), open ? Hearth.LeafDeep : Hearth.QuillSoft);
		Hearth.Ink(b, Tr.Get("mart.todayLine", "今天卖了 {{gold}}g · 钱箱里 {{cash}}g · 口碑 {{rep}}", ("gold", today), ("cash", cash), ("rep", m.State?.Reputation ?? 0)), new Vector2(x + 140, y + 2), Hearth.Quill, 0.85f);
		y += 44;
		int[] week = m.Sales.Revenue(day, 7);
		int max = Math.Max(1, week.Max());
		int bw = Math.Max(20, (w - 60) / 7 - 10);
		for (int i = 0; i < 7; i++)
		{
			int h = (int)(80.0 * week[i] / max);
			Rectangle bar = new Rectangle(x + i * (bw + 10), y + 80 - h, bw, Math.Max(2, h));
			Hearth.Fill(b, bar, i == 6 ? Hearth.Leaf : Hearth.Seam);
			Hearth.Ink(b, week[i].ToString(), new Vector2(bar.X, y + 84), Hearth.QuillPale, 0.6f);
		}
		return y + 110;
	}

	private int Stock(SpriteBatch b, MartService m, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("mart.stock", "货架"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 36;
		List<Furniture> shelves = MartService.AllShelves().Select(s => s.Shelf).ToList();
		if (shelves.Count == 0)
		{
			Hearth.Ink(b, Tr.Get("mart.noShelves", "店里还没有陈列台。"), new Vector2(x, y), Hearth.QuillSoft, 0.8f);
			return y + 30;
		}
		foreach (Furniture f in shelves)
		{
			string line = f.heldObject.Value is SObject o
				? Tr.Get("mart.stockLine", "{{item}} ×{{n}} · 每个 {{price}}g（{{pct}}%）", ("item", o.DisplayName), ("n", o.Stack), ("price", MartService.UnitPrice(f)), ("pct", MartService.Percent(f)))
				: Tr.Get("mart.stockEmpty", "（空着）");
			Hearth.Ink(b, "· " + line, new Vector2(x, y), Hearth.Quill, 0.8f);
			y += Hearth.LineHeight(0.8f) + 4;
		}
		return y;
	}

	private int Rivals(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		if (!Ctx.Config().MartCompetition)
		{
			return y;
		}
		Hearth.InkBold(b, Tr.Get("mart.rivals", "镇上的商店"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 36;
		foreach ((string rival, string name, int invest) in new[] { (Competition.Pierre, Tr.Get("mart.pierreShop", "皮埃尔的杂货店"), 5000), (Competition.Joja, Tr.Get("mart.jojaShop", "Joja 超市"), 10000) })
		{
			if (rival == Competition.Joja && !MartService.JojaOpen)
			{
				continue;
			}
			bool partner = m.State?.Rivals.TryGetValue(rival, out RivalState? r) == true && r!.Partner;
			int health = m.Health(rival);
			string state = partner ? Tr.Get("mart.partner", "已入股，不再抢生意") : MartService.HealthName(health);
			Hearth.Ink(b, Tr.Get("mart.rivalLine", "{{shop}}：{{state}} · 生意剩 {{pct}}%", ("shop", name), ("state", state), ("pct", (int)(m.ShareLeft(rival) * 100))), new Vector2(x, y + 8), health >= Competition.Losing ? Hearth.Rust : Hearth.Quill, 0.82f);
			if (!partner)
			{
				string label = Tr.Get("mart.invest", "投资入股 {{gold}}g", ("gold", invest));
				Rectangle btn = new Rectangle(x + w - (int)Hearth.Width(label, 0.75f) - 40, y, (int)Hearth.Width(label, 0.75f) + 40, 40);
				Hearth.PressPlate(b, btn, label, hover: Hearth.Hover(btn, _view), scale: 0.75f, disabled: Game1.player.Money < invest);
				hits.Add(btn, () =>
				{
					if (m.Invest(rival, invest))
					{
						Host.Notice(Tr.Get("mart.investDone", "入股了 {{shop}}，两家店不再抢生意。", ("shop", name)));
					}
				}, _view);
			}
			y += 50;
		}
		return y;
	}

	private int Clerks(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("mart.clerks", "店员（最多两位）"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 36;
		Hearth.Ink(b, Tr.Get("mart.clerksNote", "店员营业时间在收银台后面站着：你不在店里时也直接收钱，客人多一点。工钱每晚发，先从钱箱里扣。"), new Vector2(x, y), Hearth.QuillSoft, 0.72f);
		y += 32;
		foreach (Clerk c in m.State?.Clerks.ToList() ?? new List<Clerk>())
		{
			Hearth.Ink(b, Tr.Get("mart.clerkLine", "{{name}} · 每天 {{gold}}g", ("name", Ctx.Display(c.Npc)), ("gold", c.Wage)), new Vector2(x, y + 8), Hearth.Quill, 0.82f);
			Rectangle fire = new Rectangle(x + w - 120, y, 120, 40);
			Hearth.PressPlate(b, fire, Tr.Get("mart.fire", "辞退"), hover: Hearth.Hover(fire, _view), scale: 0.75f);
			hits.Add(fire, () => m.Fire(c.Npc), _view);
			y += 48;
		}
		if ((m.State?.Clerks.Count ?? 0) < 2)
		{
			foreach ((string npc, int wage) in m.ClerkCandidates().Take(4))
			{
				Hearth.Ink(b, Tr.Get("mart.candidateLine", "{{name}} · 每天 {{gold}}g", ("name", Ctx.Display(npc)), ("gold", wage)), new Vector2(x, y + 8), Hearth.QuillSoft, 0.82f);
				Rectangle hire = new Rectangle(x + w - 120, y, 120, 40);
				Hearth.PressPlate(b, hire, Tr.Get("mart.hire", "雇"), hover: Hearth.Hover(hire, _view), scale: 0.75f);
				hits.Add(hire, () =>
				{
					if (m.Hire(npc, wage))
					{
						Host.Notice(Tr.Get("mart.hired", "{{name}}明天开始来店里上班。", ("name", Ctx.Display(npc))));
					}
				}, _view);
				y += 48;
			}
		}
		return y;
	}
}
