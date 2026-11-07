using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>应用：七个读 AI 数据的小应用排成图标格子，每个下面一行实时的摘要。</summary>
internal sealed class AppsPage : ShellPage
{
	internal sealed record App(string Id, PixelArt Icon, Color Tint, string Name, Func<string> Summary, Action Open);

	private readonly Scroller _scroll = new Scroller();

	private readonly List<App> _apps;

	private readonly Dictionary<string, string> _summaries = new Dictionary<string, string>();

	private Rectangle _view;

	private DateTime _refreshed = DateTime.MinValue;

	public AppsPage(ShellContext ctx) : base(ctx)
	{
		_apps = new List<App>
		{
			new App("messenger", AppIcons.Messenger, new Color(226, 240, 214), Tr.Get("apps.messenger", "信使"), MessengerSummary, () => Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Messenger)),
			new App("where", AppIcons.Where, new Color(226, 238, 246), Tr.Get("apps.where", "大家都在哪"), WhereSummary, () => Host.Push(new WhereaboutsPage(Ctx))),
			new App("mood", AppIcons.Mood, new Color(224, 236, 250), Tr.Get("apps.mood", "心情预报"), MoodSummary, () => Host.Push(new MoodForecastPage(Ctx))),
			new App("pelican", AppIcons.Pelican, new Color(238, 230, 248), Tr.Get("apps.pelican", "鹈鹕图"), PelicanSummary, () => Host.Push(new PelicanPage(Ctx))),
			new App("gossip", AppIcons.Gossip, new Color(222, 240, 234), Tr.Get("apps.gossip", "八卦树"), GossipSummary, () => Host.Push(new GossipTreePage(Ctx))),
			new App("web", AppIcons.Web, new Color(252, 230, 238), Tr.Get("apps.web", "村庄关系网"), WebSummary, () => Host.Push(new VillageMapPage(Ctx))),
			new App("family", AppIcons.Family, new Color(234, 242, 218), Tr.Get("apps.family", "家谱"), FamilySummary, () => Host.Push(new KinshipPage(Ctx))),
			new App("reviews", AppIcons.Reviews, new Color(255, 242, 212), Tr.Get("apps.reviews", "评价"), ReviewsSummary, () => Host.Push(new ReviewsPage(Ctx))),
			new App("year", AppIcons.Year, new Color(252, 232, 222), Tr.Get("apps.year", "年度回顾"), YearSummaryLine, () => Host.Push(new YearReviewPage(Ctx))),
			new App("agenda", AppIcons.Agenda, new Color(226, 236, 248), Tr.Get("apps.agenda", "日程"), AgendaSummary, () => Host.Push(new AgendaPage(Ctx))),
			new App("horoscope", AppIcons.Horoscope, new Color(234, 228, 250), Tr.Get("apps.horoscope", "星座"), HoroscopeSummary, () => Host.Push(new HoroscopePage(Ctx))),
			new App("sky", AppIcons.Telescope, new Color(222, 226, 246), Tr.Get("apps.sky", "天文台"), SkySummary, () => Host.Push(new ObservatoryPage(Ctx))),
			new App("ads", AppIcons.Board, new Color(246, 234, 214), Tr.Get("apps.ads", "分类广告"), AdsSummary, () => Host.Push(new ClassifiedsPage(Ctx))),
			new App("whosaid", AppIcons.Quote, new Color(240, 240, 236), Tr.Get("apps.whosaid", "谁说的"), WhoSummary, () => Host.Push(new WhoSaidItPage(Ctx))),
			new App("junimo", AppIcons.Junimo, new Color(228, 246, 220), Tr.Get("apps.junimo", "Junimo 宠物"), JunimoSummary, () => Host.Push(new JunimoPage(Ctx))),
			new App("perfection", AppIcons.Perfection, new Color(255, 236, 170), Tr.Get("tools.perfection", "完美度"), () => PerfectionPage.Score().ToString("0.0") + "%", () => Host.Push(new PerfectionPage(Ctx))),
			new App("todo", AppIcons.Todo, new Color(240, 234, 214), Tr.Get("tools.todo", "待办"), TodoSummary, () => Host.Push(new TodoPage(Ctx))),
			new App("focus", AppIcons.Focus, new Color(250, 222, 210), Tr.Get("tools.focus", "番茄钟"), () => Tr.Get("tools.focus.line", "专心 25 分钟，歇 5 分钟"), () => Host.Push(new FocusPage(Ctx))),
			new App("birthdays", AppIcons.Birthdays, new Color(252, 226, 236), Tr.Get("tools.birthdays", "生日"), () => Tr.Get("tools.birthdays.line", "这一季谁过生日，一看就知道"), () => Host.Push(new BirthdaysPage(Ctx))),
			new App("aquarium", AppIcons.Aquarium, new Color(212, 234, 248), Tr.Get("tools.aquarium", "鱼缸"), () => Tr.Get("tools.aquarium.line", "钓到过的鱼都在这儿游"), () => Host.Push(new CollectionPage(Ctx, Tr.Get("tools.aquarium", "鱼缸"), Tr.Get("tools.aquarium.lead", "你钓到过的鱼。"), CollectionPage.Fish, tank: true))),
			new App("cookbook", AppIcons.Cookbook, new Color(248, 232, 206), Tr.Get("tools.cookbook", "食谱"), () => Tr.Get("tools.cookbook.line", "学会了哪些菜，做过哪些"), () => Host.Push(new CollectionPage(Ctx, Tr.Get("tools.cookbook", "食谱"), Tr.Get("tools.cookbook.lead", "彩色的是做过的。"), CollectionPage.Recipes))),
			new App("museum", AppIcons.Museum, new Color(232, 228, 216), Tr.Get("tools.museum", "博物馆"), () => Tr.Get("tools.museum.line", "捐了什么，还差什么"), () => Host.Push(new CollectionPage(Ctx, Tr.Get("tools.museum", "博物馆"), Tr.Get("tools.museum.lead", "彩色的是已经捐出去的。"), CollectionPage.Museum))),
			new App("thanks", AppIcons.Thanks, new Color(252, 232, 238), Tr.Get("apps.thanks", "致谢"), () => Tr.Get("apps.thanks.line", "谢谢每一位帮过这个 mod 的老乡"), () => Host.Push(new ThanksPage(Ctx)))
		};
		_apps = _apps.Select(a => a with { Open = () => { AISV.Core.Ai.UsageReporter.Note("app." + a.Id); a.Open(); } }).ToList();
	}

	public IReadOnlyList<App> All => _apps;

	private List<App> Visible()
	{
		var config = Ctx.Config();
		return _apps.Where(a => !AppShelf.IsHidden(config, a.Id)).ToList();
	}

	/// <summary>调试用：按名字打开一个应用。</summary>
	public bool OpenById(string id)
	{
		App? app = _apps.FirstOrDefault(a => a.Id == id);
		app?.Open();
		return app != null;
	}

	/// <summary>摘要要读不少存档数据，半分钟最多刷新一次。</summary>
	public override void Reload()
	{
		bool stale = (DateTime.UtcNow - _refreshed).TotalSeconds >= 30;
		if (stale)
		{
			_refreshed = DateTime.UtcNow;
		}
		foreach (App app in Visible())
		{
			if (!stale && _summaries.ContainsKey(app.Id))
			{
				continue;
			}
			try
			{
				_summaries[app.Id] = app.Summary();
			}
			catch (Exception ex)
			{
				_summaries[app.Id] = "";
				Ctx.Log.Warn($"应用 {app.Id} 的摘要没读出来：{ex.Message}");
			}
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		const int pad = 32;
		int x = area.X + pad, w = area.Width - pad * 2;
		Hearth.BeginClip(b, area);
		int y = area.Y + 24 - _scroll.Offset;
		Hearth.InkBold(b, Tr.Get("apps.title", "应用"), new Vector2(x, y), Hearth.Quill, 1.25f);
		string manageLabel = Tr.Get("apps.manage", "管理应用");
		int mw = (int)Hearth.Width(manageLabel, 0.76f) + 36;
		Rectangle manage = new Rectangle(x + w - mw, y, mw, 42);
		Hearth.PressPlate(b, manage, manageLabel, hover: Hearth.Hover(manage, area), scale: 0.76f);
		hits.Add(manage, () => Host.Push(new AppShelfPage(Ctx)), area);
		Hearth.Ink(b, Tr.Get("apps.sub", "从别的角度看看山谷：心情、日程、星象、八卦、关系、口碑和这一年。"), new Vector2(x + 2, y + 46), Hearth.QuillSoft, 0.78f);
		y += 96;
		int cols = w >= 1180 ? 3 : w >= 720 ? 2 : 1;
		const int gap = 22, h = 150;
		int tw = (w - gap * (cols - 1)) / cols;
		List<App> apps = Visible();
		for (int i = 0; i < apps.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % cols * (tw + gap), y + i / cols * (h + gap), tw, h);
			if (r.Bottom >= area.Y && r.Y <= area.Bottom)
			{
				App app = apps[i];
				Tile(b, r, app, Hearth.Hover(r, area));
				hits.Add(r, () =>
				{
					Game1.playSound("smallSelect");
					app.Open();
				}, area);
			}
		}
		int bottom = y + (apps.Count + cols - 1) / cols * (h + gap) + 8;
		Hearth.EndClip(b);
		_scroll.Finish(b, area, bottom, 12);
	}

	private void Tile(SpriteBatch b, Rectangle r, App app, bool hover)
	{
		Rectangle card = hover ? new Rectangle(r.X, r.Y - 3, r.Width, r.Height) : r;
		Hearth.CardSlip(b, card, hover);
		Rectangle well = new Rectangle(card.X + 22, card.Center.Y - 50, 100, 100);
		Hearth.Bordered(b, well, app.Tint, Hearth.Bark, 8, 4);
		Hearth.Fill(b, new Rectangle(well.X + 8, well.Y + 8, well.Width - 16, 4), Color.White * 0.5f);
		app.Icon.Draw(b, well.Center.X - 32, well.Center.Y - 32, 4);
		int tx = well.Right + 22, tr = card.Right - 44;
		Hearth.InkBold(b, app.Name, new Vector2(tx, card.Y + 30), Hearth.Quill, 1.0f);
		string summary = _summaries.TryGetValue(app.Id, out string? s) ? s : "";
		if (summary.Length == 0)
		{
			summary = Tr.Get("apps.nothing", "还没什么可看的");
		}
		int ly = card.Y + 72;
		foreach (string line in Hearth.WrapLines(summary, tr - tx, 0.74f).Take(2))
		{
			Hearth.Ink(b, line, new Vector2(tx, ly), Hearth.QuillSoft, 0.74f);
			ly += Hearth.LineHeight(0.74f);
		}
		Hearth.Chevron(b, new Vector2(card.Right - 26, card.Center.Y), left: false, hover ? Hearth.LeafDeep : Hearth.QuillPale);
	}

	private string MessengerSummary()
	{
		var m = Ctx.Messenger;
		if (m == null)
		{
			return "";
		}
		int unread = m.Unread.Count;
		return unread > 0 ? Tr.Get("apps.messenger.unread", "{{count}} 位村民有新消息", ("count", unread)) : Tr.Get("apps.messenger.none", "和村民发发消息，看看他们在忙什么");
	}

	private string WhereSummary()
	{
		List<WhereInput> inputs = AppData.WhereInputs(Ctx);
		int home = inputs.Count(w => !w.Hidden && w.Place != null && string.Equals(w.Place, w.Home, StringComparison.OrdinalIgnoreCase));
		return Tr.Get("apps.where.line", "{{out}} 位在外面，{{home}} 位在家", ("out", inputs.Count(w => !w.Hidden) - home), ("home", home));
	}

	private string MoodSummary()
	{
		List<MoodEntry> moods = AppData.Moods(Ctx);
		if (moods.Count == 0)
		{
			return Tr.Get("apps.mood.none", "天亮后才有今天的预报");
		}
		MoodSky village = MoodForecast.Village(moods.Select(m => m.Mood.Level));
		int bright = moods.Count(m => MoodForecast.IsBright(m.Sky));
		return Tr.Get("apps.mood.line", "全村今天{{sky}}，{{count}} 人心情不错", ("sky", MoodText.SkyName(village)), ("count", bright));
	}

	private string PelicanSummary()
	{
		IReadOnlyList<PelicanPost> posts = AppData.Posts(Ctx);
		if (posts.Count == 0)
		{
			return "";
		}
		PelicanPost top = posts[0];
		return Tr.Get("apps.pelican.line", "{{count}} 条帖子，最新一条来自{{name}}", ("count", posts.Count), ("name", Ctx.Display(top.Npc)));
	}

	private string GossipSummary()
	{
		int count = Ctx.Social.Gossip.Feed().Count;
		return count == 0 ? "" : Tr.Get("apps.gossip.line", "{{count}} 条八卦，看看是谁传给谁的", ("count", Math.Min(count, AppData.MaxStories)));
	}

	private string WebSummary()
	{
		MapModel map = AppData.Map(Ctx);
		if (map.IsEmpty)
		{
			return "";
		}
		Tie? best = map.TopBonds.FirstOrDefault();
		return best == null
			? Tr.Get("apps.web.count", "牵着 {{count}} 个人", ("count", map.Ties.Count))
			: Tr.Get("apps.web.line", "{{count}} 段关系，最要好的是{{a}}和{{b}}", ("count", map.Ties.Count), ("a", Ctx.Display(best.A)), ("b", Ctx.Display(best.B)));
	}

	private string TodoSummary()
	{
		int open = new TodoPad(Ctx.Store).InOrder().Count(n => !n.Ticked);
		return open == 0 ? Tr.Get("tools.todo.line", "把要做的事记下来") : Tr.Get("tools.todo.open", "还有 {{n}} 件没做", ("n", open));
	}

	private string FamilySummary()
	{
		return Tr.Get("apps.family.line", "镇上有 {{count}} 家人", ("count", AppData.Kin(Ctx).Trees.Count(t => !t.IsFarmers)));
	}

	private string ReviewsSummary()
	{
		List<ReviewEntry> reviews = AppData.Reviews(Ctx);
		if (reviews.Count == 0)
		{
			return "";
		}
		return Tr.Get("apps.reviews.line", "平均 {{avg}} 星，共 {{count}} 条", ("avg", FarmerReviews.Average(reviews.Select(r => r.Review.Stars)).ToString("0.0")), ("count", reviews.Count));
	}

	private string YearSummaryLine()
	{
		YearFacts facts = AppData.Year(Ctx);
		int year = Math.Max(1, Game1.year);
		YearSummary s = YearReview.Summarize(facts, year);
		return Tr.Get("apps.year.line", "第 {{year}} 年：聊了 {{talks}} 次，送了 {{gifts}} 份礼", ("year", year), ("talks", s.TalkCount), ("gifts", s.GiftCount));
	}

	private string AgendaSummary()
	{
		Dictionary<string, List<AgendaStop>> stops = new Dictionary<string, List<AgendaStop>>(StringComparer.OrdinalIgnoreCase);
		var people = AppData.Agenda(Ctx, stops);
		string? npc = AppData.Closest(Ctx, people.Select(p => p.Npc));
		return npc == null ? "" : Agenda.Next(Ctx.Display(npc), stops.TryGetValue(npc, out List<AgendaStop>? s) ? s : new List<AgendaStop>(), Game1.timeOfDay);
	}

	private string HoroscopeSummary()
	{
		List<Fortune> all = AppData.Fortunes(Ctx);
		string? npc = AppData.Closest(Ctx, all.Select(f => f.Npc));
		Fortune? f = all.FirstOrDefault(x => x.Npc == npc);
		return f == null ? "" : Tr.Get("apps.horoscope.line", "{{name}}：{{advice}}", ("name", Ctx.Display(f.Npc)), ("advice", Horoscope.Advice(f) is { Length: > 0 } a ? a : f.Line));
	}

	private string SkySummary() => Ctx.Apps?.Tonight().Summary ?? "";

	private string AdsSummary()
	{
		int today = AppData.Today;
		int open = Ctx.Ledger?.Ads().Count(a => AISV.Core.World.Ledger.ClassifiedRules.Open(a, today)) ?? 0;
		return open == 0 ? "" : Tr.Get("apps.ads.line", "公告栏上有 {{count}} 条广告", ("count", open));
	}

	private string WhoSummary()
	{
		int n = AppData.Quotes(Ctx).Count;
		return n < 3 ? "" : Tr.Get("apps.whosaid.line", "攒了 {{count}} 句话，猜猜是谁说的", ("count", n));
	}

	private string JunimoSummary()
	{
		var s = Ctx.Apps?.Junimo;
		return s is not { Adopted: true } ? Tr.Get("apps.junimo.none", "家里还没有 Junimo") : Tr.Get("apps.junimo.line", "{{name}}{{mood}}", ("name", s.Name), ("mood", JunimoRules.MoodLabel(JunimoRules.Mood(s))));
	}
}
