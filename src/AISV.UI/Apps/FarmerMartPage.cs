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

/// <summary>
/// 小超市应用：概况（营业额、钱箱、星级、等级、店型、今天的行情、货架、对手）、进货、宣传、店员、评价、开店说明。
/// </summary>
internal sealed class FarmerMartPage : ShellPage
{
	private enum Tab
	{
		Overview,
		Supply,
		Promo,
		Staff,
		Reviews,
		Guide
	}

	private readonly Scroller _scroll = new Scroller();

	private Tab _tab;

	private string _supplier = "Pierre";

	private string _role = AISV.Core.Store.Staff.Cashier;

	private int _days = 7;

	private int _qty = 5;

	private Rectangle _view;

	public FarmerMartPage(ShellContext ctx, bool guide = false) : base(ctx)
	{
		_tab = guide ? Tab.Guide : Tab.Overview;
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
		int y = area.Y + 14 - _scroll.Offset;
		bool has = Mart?.HasShop == true;
		Tab[] tabs = has ? (Tab[])Enum.GetValues(typeof(Tab)) : new[] { Tab.Guide };
		if (!has)
		{
			_tab = Tab.Guide;
		}
		int tx = x;
		foreach (Tab t in tabs)
		{
			string label = TabName(t);
			Rectangle r = new Rectangle(tx, y, (int)Hearth.Width(label, 0.8f) + 36, 42);
			Hearth.PaperTab(b, r, label, t == _tab, Hearth.Hover(r, area), 0.8f);
			Tab pick = t;
			hits.Add(r, () =>
			{
				_tab = pick;
				_scroll.Reset();
			}, area);
			tx = r.Right + 6;
		}
		y += 58;
		if (Mart is MartService m && has)
		{
			y = _tab switch
			{
				Tab.Overview => Overview(b, hits, m, x, y, w),
				Tab.Supply => Supply(b, hits, m, x, y, w),
				Tab.Promo => Promo(b, hits, m, x, y, w),
				Tab.Staff => Staff(b, hits, m, x, y, w),
				Tab.Reviews => Reviews(b, m, x, y, w),
				_ => Guide(b, x, y, w)
			};
		}
		else
		{
			y = Guide(b, x, y, w);
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + _scroll.Offset + 12, 12);
	}

	private static string TabName(Tab t) => t switch
	{
		Tab.Overview => Tr.Get("mart.tab.overview", "概况"),
		Tab.Supply => Tr.Get("mart.tab.supply", "进货"),
		Tab.Promo => Tr.Get("mart.tab.promo", "宣传"),
		Tab.Staff => Tr.Get("mart.tab.staff", "店员"),
		Tab.Reviews => Tr.Get("mart.tab.reviews", "评价"),
		_ => Tr.Get("mart.tab.guide", "说明")
	};

	private static int Text(SpriteBatch b, string text, int x, int y, int w, Color color, float scale = 0.78f)
	{
		foreach (string line in Hearth.WrapLines(text, w, scale, paragraphGap: true))
		{
			Hearth.Ink(b, line, new Vector2(x, y), color, scale);
			y += Hearth.LineHeight(scale);
		}
		return y;
	}

	private int Heading(SpriteBatch b, string text, int x, int y)
	{
		Hearth.InkBold(b, text, new Vector2(x, y), Hearth.Quill, 0.9f);
		return y + 38;
	}

	private bool Button(SpriteBatch b, Hits hits, Rectangle r, string label, Action act, bool disabled = false, bool primary = false)
	{
		Hearth.PressPlate(b, r, label, primary: primary, hover: Hearth.Hover(r, _view), disabled: disabled, scale: 0.75f);
		if (!disabled)
		{
			hits.Add(r, act, _view);
		}
		return true;
	}

	private static Rectangle Fit(int right, int y, string label, int h = 40) => new Rectangle(right - (int)Hearth.Width(label, 0.75f) - 40, y, (int)Hearth.Width(label, 0.75f) + 40, h);

	/// <summary>画几颗星（原版的品质星）。</summary>
	private static void Stars(SpriteBatch b, int x, int y, double stars, float scale = 3f)
	{
		for (int i = 0; i < 5; i++)
		{
			float fill = (float)Math.Clamp(stars - i, 0, 1);
			b.Draw(Game1.mouseCursors, new Vector2(x + i * 9 * scale, y), new Rectangle(346, 400, 8, 8), Color.Black * 0.25f, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
			if (fill > 0)
			{
				b.Draw(Game1.mouseCursors, new Vector2(x + i * 9 * scale, y), new Rectangle(346, 400, (int)Math.Ceiling(8 * fill), 8), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
			}
		}
	}

	// ---------------- 概况 ----------------

	private int Overview(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		MartState? s = m.State;
		int day = Game1.Date.TotalDays;
		int today = m.Sales.On(day, StoreSale.Ours).Sum(v => v.Total);
		bool open = m.IsOpen(Game1.timeOfDay);
		Hearth.InkBold(b, open ? Tr.Get("mart.openNow", "营业中") : Tr.Get("mart.closedLabel", "打烊了"), new Vector2(x, y), open ? Hearth.LeafDeep : Hearth.QuillSoft);
		Hearth.Ink(b, Tr.Get("mart.todayLine", "今天卖了 {{gold}}g · 钱箱里 {{cash}}g · 口碑 {{rep}}", ("gold", today), ("cash", s?.Cashbox ?? 0), ("rep", s?.Reputation ?? 0)), new Vector2(x + 140, y + 2), Hearth.Quill, 0.85f);
		y += 44;
		Stars(b, x, y + 2, m.Stars);
		Hearth.Ink(b, Tr.Get("mart.starsLine", "{{stars}} 星（最近 {{n}} 条评价）", ("stars", m.Stars.ToString("0.0")), ("n", Math.Min(20, m.Reviews.Count))), new Vector2(x + 150, y), Hearth.QuillSoft, 0.78f);
		y += 40;
		int level = m.Level, xp = s?.Xp ?? 0, next = ShopTypes.NextLevelXp(level);
		Hearth.Ink(b, Tr.Get("mart.levelLine", "{{type}} · {{level}} 级（累计营业额 {{xp}}g{{next}}）", ("type", MartNames.ShopType(m.Type.Id)), ("level", level), ("xp", xp), ("next", level >= 10 ? "" : Tr.Get("mart.levelNext", "，{{n}}g 升级", ("n", next)))), new Vector2(x, y), Hearth.Quill, 0.8f);
		y += 32;
		Hearth.Gauge(b, new Rectangle(x, y, Math.Min(420, w), 14), level >= 10 ? 1f : (float)Math.Clamp((double)xp / next, 0, 1));
		y += 30;
		// 七天走势。
		int[] week = m.Sales.Revenue(day, 7);
		int max = Math.Max(1, week.Max());
		int bw = Math.Max(20, (Math.Min(w, 700) - 60) / 7 - 10);
		for (int i = 0; i < 7; i++)
		{
			int h = (int)(70.0 * week[i] / max);
			Rectangle bar = new Rectangle(x + i * (bw + 10), y + 70 - h, bw, Math.Max(2, h));
			Hearth.Fill(b, bar, i == 6 ? Hearth.Leaf : Hearth.Seam);
			Hearth.Ink(b, week[i].ToString(), new Vector2(bar.X, y + 74), Hearth.QuillPale, 0.6f);
		}
		y += 108;
		y = Market(b, m, x, y, w) + 14;
		y = ShopType(b, hits, m, x, y, w) + 14;
		y = Shelves(b, hits, m, x, y, w) + 14;
		return Rivals(b, hits, m, x, y, w);
	}

	private int Market(SpriteBatch b, MartService m, int x, int y, int w)
	{
		y = Heading(b, Tr.Get("mart.market", "今天的行情"), x, y);
		MarketDay md = m.TodayMarket;
		y = Text(b, Tr.Get("mart.marketLine", "{{hot}}紧俏（需求 ×{{h}}），{{cold}}卖不动（×{{c}}）。紧俏的货多摆、价格可以高一点。", ("hot", MartNames.Group(md.Hot)), ("h", md.Of(md.Hot).ToString("0.0")), ("cold", MartNames.Group(md.Cold)), ("c", md.Of(md.Cold).ToString("0.0"))), x, y, w, Hearth.Quill, 0.78f);
		return y;
	}

	private int ShopType(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		y = Heading(b, Tr.Get("mart.shopType", "店型"), x, y);
		y = Text(b, MartNames.ShopTypeNote(m.Type.Id), x, y, w, Hearth.QuillSoft, 0.75f) + 6;
		int wait = m.DaysUntilTypeChange;
		if (wait > 0)
		{
			return Text(b, Tr.Get("mart.typeWait", "{{n}} 天后才能再换店型。", ("n", wait)), x, y, w, Hearth.QuillPale, 0.72f);
		}
		int bx = x;
		foreach (ShopType t in ShopTypes.All)
		{
			string label = MartNames.ShopType(t.Id);
			int bw = (int)Hearth.Width(label, 0.75f) + 36;
			if (bx + bw > x + w)
			{
				bx = x;
				y += 48;
			}
			Rectangle r = new Rectangle(bx, y, bw, 40);
			string id = t.Id;
			Button(b, hits, r, label, () =>
			{
				if (m.SetShopType(id))
				{
					Host.Notice(Tr.Get("mart.typeSet", "改成了{{type}}。一周后才能再换。", ("type", MartNames.ShopType(id))));
				}
			}, disabled: t == m.Type, primary: t == m.Type);
			bx += bw + 8;
		}
		return y + 50;
	}

	private int Shelves(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		y = Heading(b, Tr.Get("mart.stock", "货架"), x, y);
		List<Furniture> shelves = MartService.AllShelves().Select(s => s.Shelf).ToList();
		foreach (Furniture f in shelves)
		{
			List<SObject> goods = MartShelves.Goods(f).ToList();
			int near = goods.Count(o => MartShelves.Stage(f, o) == Freshness.Near), expired = goods.Count(o => MartShelves.Stage(f, o) == Freshness.Expired);
			string list = goods.Count == 0 ? Tr.Get("mart.stockEmpty", "（空着）") : string.Join(Tr.Get("list.sep", "、"), goods.Take(5).Select(o => $"{o.DisplayName}×{o.Stack}")) + (goods.Count > 5 ? "……" : "");
			string warn = (near > 0 ? Tr.Get("mart.nearCount", " · 临期 {{n}}", ("n", near)) : "") + (expired > 0 ? Tr.Get("mart.expiredCount", " · 过期 {{n}}", ("n", expired)) : "");
			y = Text(b, $"· {MartService.ShelfName(f)}：{list}{warn}", x, y, w, expired > 0 ? Hearth.Berry : Hearth.Quill, 0.75f) + 2;
		}
		int stock = MartShelves.Stockroom().Where(i => i != null).Sum(i => i.Stack);
		y = Text(b, Tr.Get("mart.stockroomLine", "库房里还有 {{n}} 件（进的货送到这里；店里右下角的大箱子）。", ("n", stock)), x, y + 6, w, Hearth.QuillSoft, 0.72f) + 6;
		string restock = Tr.Get("mart.restock", "理货：撤下过期的、补上库房的货");
		Rectangle r = new Rectangle(x, y, (int)Hearth.Width(restock, 0.75f) + 40, 40);
		Button(b, hits, r, restock, () => Host.Notice(Tr.Get("mart.restocked", "补上了 {{n}} 件。", ("n", m.Restock()))));
		return y + 50;
	}

	private int Rivals(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		if (!Ctx.Config().MartCompetition)
		{
			return y;
		}
		y = Heading(b, Tr.Get("mart.rivals", "镇上的商店"), x, y);
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
				Button(b, hits, Fit(x + w, y, label), label, () =>
				{
					if (m.Invest(rival, invest))
					{
						Host.Notice(Tr.Get("mart.investDone", "入股了 {{shop}}，两家店不再抢生意。", ("shop", name)));
					}
				}, disabled: Game1.player.Money < invest);
			}
			y += 50;
		}
		return Text(b, Tr.Get("mart.pierreSupplyNote", "从 Pierre 那里进货也算照顾他的生意。"), x, y, w, Hearth.QuillPale, 0.7f);
	}

	// ---------------- 进货 ----------------

	private int Supply(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		y = Text(b, Tr.Get("mart.supplyNote", "今天下单、现在付钱，明早 6 点送到店里的库房（右下角的大箱子）。进价是出货价的 1.4 倍，赚头没有自己种的多；进的货标着“进的货”，顾客不会多给钱。"), x, y, w, Hearth.QuillSoft, 0.74f) + 8;
		int bx = x;
		foreach (Supplier sup in Suppliers.All)
		{
			string label = Ctx.Display(sup.Npc) + " · " + MartNames.Group(sup.Group);
			int bw = (int)Hearth.Width(label, 0.75f) + 36;
			if (bx + bw > x + w)
			{
				bx = x;
				y += 48;
			}
			Rectangle r = new Rectangle(bx, y, bw, 40);
			Hearth.PaperTab(b, r, label, sup.Npc == _supplier, Hearth.Hover(r, _view), 0.75f);
			string npc = sup.Npc;
			hits.Add(r, () => _supplier = npc, _view);
			bx += bw + 8;
		}
		y += 56;
		Hearth.Ink(b, Tr.Get("mart.qty", "每次订"), new Vector2(x, y + 10), Hearth.QuillSoft, 0.78f);
		int qx = x + 100;
		foreach (int q in new[] { 1, 5, 10, 20 })
		{
			Rectangle r = new Rectangle(qx, y, 64, 40);
			Hearth.PaperTab(b, r, q.ToString(), q == _qty, Hearth.Hover(r, _view), 0.75f);
			int pick = q;
			hits.Add(r, () => _qty = pick, _view);
			qx += 72;
		}
		y += 56;
		IReadOnlyList<(string ItemId, string Name, int Cost)> items = m.SupplierItems(_supplier);
		if (items.Count == 0)
		{
			y = Text(b, Tr.Get("mart.noSupply", "这一季没有货。"), x, y, w, Hearth.QuillPale);
		}
		foreach ((string id, string name, int cost) in items)
		{
			if (ItemRegistry.Create(id, allowNull: true) is Item icon)
			{
				icon.drawInMenu(b, new Vector2(x - 6, y - 10), 0.7f);
			}
			Hearth.Ink(b, Tr.Get("mart.supplyLine", "{{item}} · 进价 {{cost}}g · 卖价约 {{retail}}g", ("item", name), ("cost", cost), ("retail", ItemRegistry.Create(id) is SObject o ? MartService.BasePrice(o) : 0)), new Vector2(x + 56, y + 8), Hearth.Quill, 0.78f);
			string label = Tr.Get("mart.orderBtn", "订 {{n}} 个（{{gold}}g）", ("n", _qty), ("gold", cost * _qty));
			string item = id;
			Button(b, hits, Fit(x + w, y, label), label, () =>
			{
				if (m.PlaceOrder(_supplier, item, _qty))
				{
					Host.Notice(Tr.Get("mart.ordered", "订好了，明早送到库房。"));
				}
			}, disabled: Game1.player.Money < cost * _qty);
			y += 52;
		}
		List<Order> orders = m.State?.Orders ?? new List<Order>();
		if (orders.Count > 0)
		{
			y = Heading(b, Tr.Get("mart.pending", "明早送到"), x, y + 10);
			foreach (Order o in orders)
			{
				string name = ItemRegistry.Create(o.ItemId, allowNull: true)?.DisplayName ?? o.ItemId;
				y = Text(b, $"· {Ctx.Display(o.Supplier)}：{name} ×{o.Qty}（{o.Cost}g）", x, y, w, Hearth.Quill, 0.75f);
			}
		}
		return y;
	}

	// ---------------- 宣传 ----------------

	private int Promo(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		int day = Game1.Date.TotalDays;
		y = Heading(b, Tr.Get("mart.poster", "海报（{{gold}}g，贴一周）", ("gold", Promotions.PosterCost)), x, y);
		y = Text(b, Tr.Get("mart.posterNote", "主推一类货：这类货更好卖（×1.4），来的人也多一成半。店里墙上会挂一条横幅。"), x, y, w, Hearth.QuillSoft, 0.74f) + 6;
		foreach (Promotion p in m.ActivePromotions.Where(p => p.Kind == Promotion.Poster))
		{
			y = Text(b, Tr.Get("mart.posterActive", "· 正在主推{{group}}，还剩 {{n}} 天", ("group", MartNames.Group(p.Target)), ("n", p.Until - day + 1)), x, y, w, Hearth.LeafDeep, 0.78f);
		}
		int bx = x;
		foreach (string g in Goods.All.Where(g => g != Goods.General))
		{
			string label = MartNames.Group(g);
			int bw = (int)Hearth.Width(label, 0.75f) + 36;
			if (bx + bw > x + w)
			{
				bx = x;
				y += 48;
			}
			Rectangle r = new Rectangle(bx, y + 4, bw, 40);
			string group = g;
			Button(b, hits, r, label, () =>
			{
				if (m.StartPoster(group))
				{
					Host.Notice(Tr.Get("mart.posterDone", "海报贴好了：主推{{group}}。", ("group", MartNames.Group(group))));
				}
			}, disabled: Game1.player.Money < Promotions.PosterCost || m.ActivePromotions.Any(p => p.Kind == Promotion.Poster && p.Target == g));
			bx += bw + 8;
		}
		y += 64;
		y = Heading(b, Tr.Get("mart.window", "橱窗（不要钱）"), x, y);
		y = Text(b, Tr.Get("mart.windowNote", "挑一样货放进橱窗：进门的人最先看到它，更好卖（×1.8）。一次只能放一样。"), x, y, w, Hearth.QuillSoft, 0.74f) + 6;
		List<SObject> goods = MartService.AllShelves().SelectMany(s => MartShelves.Goods(s.Shelf)).GroupBy(o => o.QualifiedItemId).Select(g => g.First()).ToList();
		string? window = m.WindowItem;
		if (goods.Count == 0)
		{
			y = Text(b, Tr.Get("mart.windowEmpty", "货架上还没有货。"), x, y, w, Hearth.QuillPale, 0.75f);
		}
		bx = x;
		foreach (SObject o in goods.Take(16))
		{
			Rectangle r = new Rectangle(bx, y, 72, 72);
			Hearth.CardSlip(b, r, highlight: o.QualifiedItemId == window);
			o.drawInMenu(b, new Vector2(r.X + 4, r.Y + 4), 1f, 1f, 0.9f, StackDrawType.Hide);
			string id = o.QualifiedItemId;
			hits.Add(r, () => m.SetWindow(id == window ? "" : id), _view);
			bx += 80;
			if (bx + 72 > x + w)
			{
				bx = x;
				y += 80;
			}
		}
		y += 92;
		y = Heading(b, Tr.Get("mart.megaphone", "喇叭（{{gold}}g，一天一次）", ("gold", Promotions.MegaphoneCost)), x, y);
		y = Text(b, Tr.Get("mart.megaphoneNote", "在门口吆喝一声：接下来半小时左右陆续来三位客人。只在营业时间能用。"), x, y, w, Hearth.QuillSoft, 0.74f) + 6;
		string shout = m.MegaphoneUsed ? Tr.Get("mart.megaphoneUsed", "今天喊过了") : Tr.Get("mart.megaphoneBtn", "吆喝");
		Button(b, hits, new Rectangle(x, y, (int)Hearth.Width(shout, 0.75f) + 40, 40), shout, () =>
		{
			if (m.Megaphone())
			{
				Host.Notice(Tr.Get("mart.megaphoneDone", "“新鲜货上架啦！”——客人马上就来。"));
			}
		}, disabled: m.MegaphoneUsed || Game1.player.Money < Promotions.MegaphoneCost || !m.IsOpen(Game1.timeOfDay));
		return y + 56;
	}

	// ---------------- 店员 ----------------

	private int Staff(SpriteBatch b, Hits hits, MartService m, int x, int y, int w)
	{
		y = Text(b, Tr.Get("mart.staffNote", "最多三位。收银：顾客自己去柜台结账，不用你找零；理货：9、12、15 点撤下过期的、把库房的货摆上去；迎宾：门口打招呼，评价更好、客人多一成。工钱每晚发，先从钱箱里扣。签四周的每天便宜 15%。"), x, y, w, Hearth.QuillSoft, 0.74f) + 8;
		int day = Game1.Date.TotalDays;
		foreach (Clerk c in m.State?.Clerks.ToList() ?? new List<Clerk>())
		{
			string until = c.Until > 0 ? Tr.Get("mart.until", "，合同还剩 {{n}} 天", ("n", Math.Max(0, c.Until - day))) : "";
			Hearth.Ink(b, Tr.Get("mart.clerkLine2", "{{name}} · {{role}} · 每天 {{gold}}g{{until}}", ("name", Ctx.Display(c.Npc)), ("role", MartService.RoleName(c.Role)), ("gold", c.Wage), ("until", until)), new Vector2(x, y + 8), Hearth.Quill, 0.8f);
			Rectangle fire = new Rectangle(x + w - 110, y, 110, 40);
			string npc = c.Npc;
			Button(b, hits, fire, Tr.Get("mart.fire", "辞退"), () => m.Fire(npc));
			if (c.Until > 0)
			{
				string renew = Tr.Get("mart.renew", "续约 {{n}} 天", ("n", _days));
				Button(b, hits, Fit(fire.X - 8, y, renew), renew, () => m.Renew(npc, _days));
			}
			y += 50;
		}
		if ((m.State?.Clerks.Count ?? 0) >= AISV.Core.Store.Staff.MaxStaff)
		{
			return y;
		}
		y = Heading(b, Tr.Get("mart.hireHeading", "招人"), x, y + 6);
		int bx = x;
		foreach (string role in AISV.Core.Store.Staff.Roles)
		{
			string label = MartService.RoleName(role);
			Rectangle r = new Rectangle(bx, y, (int)Hearth.Width(label, 0.75f) + 36, 40);
			Hearth.PaperTab(b, r, label, role == _role, Hearth.Hover(r, _view), 0.75f);
			string pick = role;
			hits.Add(r, () => _role = pick, _view);
			bx = r.Right + 8;
		}
		bx += 24;
		foreach (int d in new[] { 7, 28 })
		{
			string label = Tr.Get("mart.days", "{{n}} 天", ("n", d));
			Rectangle r = new Rectangle(bx, y, (int)Hearth.Width(label, 0.75f) + 36, 40);
			Hearth.PaperTab(b, r, label, d == _days, Hearth.Hover(r, _view), 0.75f);
			int pick = d;
			hits.Add(r, () => _days = pick, _view);
			bx = r.Right + 8;
		}
		y += 56;
		IReadOnlyList<(string Npc, int Wage)> candidates = m.ClerkCandidates(_role, _days);
		if (candidates.Count == 0)
		{
			y = Text(b, Tr.Get("mart.noCandidates", "还没人愿意来（要 4 心以上）。"), x, y, w, Hearth.QuillPale, 0.75f);
		}
		foreach ((string npc, int wage) in candidates.Take(6))
		{
			Hearth.Ink(b, Tr.Get("mart.candidateLine", "{{name}} · 每天 {{gold}}g", ("name", Ctx.Display(npc)), ("gold", wage)), new Vector2(x, y + 8), Hearth.QuillSoft, 0.8f);
			string label = Tr.Get("mart.hireBtn", "签 {{n}} 天", ("n", _days));
			Button(b, hits, Fit(x + w, y, label), label, () =>
			{
				if (m.Hire(npc, _role, _days, wage))
				{
					Host.Notice(Tr.Get("mart.hired", "{{name}}明天开始来店里上班。", ("name", Ctx.Display(npc))));
				}
			});
			y += 50;
		}
		return y;
	}

	// ---------------- 评价 ----------------

	private int Reviews(SpriteBatch b, MartService m, int x, int y, int w)
	{
		Stars(b, x, y + 2, m.Stars, 4f);
		Hearth.InkBold(b, m.Stars.ToString("0.0"), new Vector2(x + 200, y), Hearth.Quill);
		y += 48;
		y = Text(b, Tr.Get("mart.reviewNote", "顾客走的时候会打分：价格、新鲜度、品质、该冷藏的放没放冷柜、排队久不久、店里布置、店员热不热情、找零对不对。星级越高客人越多。"), x, y, w, Hearth.QuillSoft, 0.72f) + 10;
		if (m.Reviews.Count == 0)
		{
			return Text(b, Tr.Get("mart.noReviews", "还没有评价。"), x, y, w, Hearth.QuillPale);
		}
		foreach (Review r in m.Reviews.Reverse().Take(30))
		{
			Stars(b, x, y + 4, r.Stars, 2f);
			Hearth.Ink(b, $"{Ctx.Display(r.Npc)}", new Vector2(x + 100, y), Hearth.Quill, 0.78f);
			y += 28;
			y = Text(b, r.Text, x + 12, y, w - 12, Hearth.QuillSoft, 0.74f);
			if (r.Items.Length > 0)
			{
				y = Text(b, Tr.Get("mart.reviewItems", "买了：{{items}}", ("items", r.Items)), x + 12, y, w - 12, Hearth.QuillPale, 0.64f);
			}
			y += 10;
		}
		return y;
	}

	// ---------------- 说明 ----------------

	private static int Guide(SpriteBatch b, int x, int y, int w)
	{
		string text = Tr.Get("mart.guide2", "【在哪里建】去 Robin 的木匠铺（镇子北边、山脚下），点柜台 > 建造农场建筑 > 选“农夫小超市”（15000g、木材 300、石头 200），在农场上挑个空地放下，两天后建好，从正面的门进去。和 Farmer's Store 的分店不是一回事，两家可以同时开。\n【店里】照皮埃尔杂货店的样子：左上角是柜台和收银机，已经摆好两张白货架、一个玻璃冷柜、一个木柜、一个促销桶，右下角的大箱子是库房。想要更多货架（还有保温柜、窄货架），去 Robin 店里买。\n【上架】拿着东西点货架就放上去；空手点货架打开货架窗口：下面是背包，点东西上架（右键只放一个），点格子可以改价、取下。每种货架能放的东西不一样：鱼和蛋奶放冷柜坏得慢，熟食放保温柜，促销桶最显眼。\n【新鲜度】货从上架那天算：当日上架 > 新鲜 > 正常 > 临期 > 过期。过期的基本没人买，还会被差评。矿石、手工品、文物不会坏。\n【营业】默认 9:00 - 17:00。你在店里时，村民进门、挑货、到柜台排队，点柜台开始收银：一样样扫进袋子，再用硬币凑出找零。又快又对有小费。没人收银等太久，顾客会放下东西走掉。你不在时也照样卖，钱放进钱箱，点柜台取出。\n【经营】手机 > 小超市：选店型（主打哪类货）、看今天的行情（哪类紧俏）、进货（明早送到库房）、贴海报、放橱窗、吆喝、雇店员（收银 / 理货 / 迎宾，签 7 天或 28 天）、看评价。\n【竞争】卖得越好，Pierre 和 Joja 的生意越差：会吃紧、亏损，甚至停业整顿（只影响 AI星露谷 的经济，种子一定买得到）。亏损以后他们可能上门理论。\n【注意】偷来的东西放在店里卖，失主和熟人可能认出来。");
		return Text(b, text, x, y, w, Hearth.Quill, 0.78f);
	}
}
