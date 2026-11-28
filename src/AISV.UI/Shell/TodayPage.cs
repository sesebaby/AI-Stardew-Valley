using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.News;
using AISV.Core.Social;
using AISV.Core.Social.Gossip;
using AISV.Core.Social.Letters;
using AISV.Core.Social.Web;
using AISV.Core.Companions;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>今日：问候、今天的事、镇上的心情、镇上在传什么。</summary>
internal sealed class TodayPage : ShellPage
{
	private sealed class Row
	{
		public string? Npc;

		public string Glyph = "";

		public Color GlyphColor = Hearth.Leaf;

		public string Text = "";

		public string Sub = "";

		public string Chip = "";

		public Color ChipFill = Hearth.LeafPale;

		public Color ChipInk = Hearth.LeafDeep;

		public Action? Open;
	}

	private sealed class Card
	{
		public string Title = "";

		public string Empty = "";

		public List<Row> Rows = new List<Row>();
	}

	private const int RowH = 66;

	private readonly Scroller _scroll = new Scroller();

	private Card _today = new Card();

	private Card _moods = new Card();

	private Card _town = new Card();

	private Card _promo = new Card();

	private string _announcement = "";

	private Rectangle _view;

	public TodayPage(ShellContext ctx) : base(ctx)
	{
	}

	public override void Reload()
	{
		GameDate today = GameState.Today;
		_today = new Card { Title = Tr.Get("shell.today.today", "今天"), Empty = Tr.Get("shell.today.free", "今天没什么特别的事。") };
		if (!ProfilePage.Filled(Ctx))
		{
			_today.Rows.Add(new Row { Glyph = "我", Text = Tr.Get("shell.today.profile", "还没写我的设定"), Sub = Tr.Get("shell.today.profileSub", "村民会照着它认识你，几句话就行。"), Chip = Tr.Get("shell.today.profileChip", "去写"), Open = () => Host.Push(new ProfilePage(Ctx)) });
		}
		if (Ctx.Social.IsPlayerBirthday())
		{
			_today.Rows.Add(new Row { Glyph = "生", GlyphColor = Hearth.Rose, Text = Tr.Get("shell.today.myBirthday", "今天是你的生日"), Sub = Tr.Get("shell.today.myBirthdaySub", "去镇上转转，大家也许记得。"), Chip = Tr.Get("shell.today.birthdayChip", "生日"), ChipFill = Hearth.RosePale, ChipInk = Hearth.Rose });
		}
		foreach (NPC npc in Utility.getAllVillagers().Where(n => n.isBirthday() && n.CanSocialize && Ctx.Npcs.IsEnabled(n.Name)).OrderBy(n => Ctx.Display(n.Name), StringComparer.CurrentCulture))
		{
			string name = npc.Name;
			_today.Rows.Add(new Row
			{
				Npc = name,
				Text = Tr.Get("shell.today.npcBirthday", "今天是{{npc}}的生日", ("npc", Ctx.Display(name))),
				Sub = Tr.Get("shell.today.npcBirthdaySub", "送份礼物吧，生日的礼物分量更重。"),
				Chip = Tr.Get("shell.today.birthdayChip", "生日"),
				ChipFill = Hearth.RosePale,
				ChipInk = Hearth.Rose,
				Open = () => OpenVillager(name)
			});
		}
		foreach (QueuedLetter l in Ctx.Social.Letters.DeliveredOn(today.TotalDays).Where(l => Ctx.Npcs.IsEnabled(l.From)))
		{
			string key = l.Key;
			_today.Rows.Add(new Row
			{
				Npc = l.From,
				Text = Tr.Get("shell.today.letter", "{{npc}}寄来一封信", ("npc", Ctx.Display(l.From))),
				Sub = ShellText.Clean(l.Title),
				Chip = Tr.Get("shell.today.letterChip", "在信箱里"),
				ChipFill = Hearth.AmberPale,
				ChipInk = Hearth.Amber,
				Open = () =>
				{
					LettersPage.Focus = key;
					Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Letters);
				}
			});
		}

		AddPlans(Game1.Date?.TotalDays ?? 0);
		AddCommissions();
		AddErrands();

		_moods = new Card { Title = Tr.Get("shell.today.moods", "大家的心情"), Empty = Tr.Get("shell.today.calm", "大家今天都挺平静。") };
		foreach (NpcMood m in Ctx.Social.Moods.All.Where(m => m.Temper != "neutral" && Ctx.Npcs.IsEnabled(m.Npc)).OrderByDescending(m => Math.Abs(m.Level)).Take(6))
		{
			string name = m.Npc;
			bool low = m.Level < 0;
			_moods.Rows.Add(new Row
			{
				Npc = name,
				Text = Ctx.Display(name),
				Sub = ShellText.Clean(m.Because),
				Chip = MoodBook.MoodWord(m.Temper),
				ChipFill = low ? Hearth.ParchmentDeep : Hearth.LeafPale,
				ChipInk = low ? Hearth.QuillSoft : Hearth.LeafDeep,
				Open = () => OpenVillager(name)
			});
		}

		_town = new Card { Title = Tr.Get("shell.today.town", "镇上"), Empty = Tr.Get("shell.today.quiet", "镇上还没什么新鲜事。") };
		foreach (GossipEntry e in Ctx.Social.Gossip.Feed().Take(3))
		{
			GossipView v = GossipView.Resolve(e, Ctx.Display);
			_town.Rows.Add(new Row
			{
				Npc = v.Speaker,
				Glyph = "?",
				GlyphColor = Hearth.QuillPale,
				Text = v.Text,
				Sub = (v.Speaker == null ? Tr.Get("shell.gossip.someone", "镇上有人说") : Ctx.Display(v.Speaker)) + " · " + ShellText.DaysAgo(today.TotalDays - e.TotalDay, e.Day, e.Season),
				Chip = Tr.Get("shell.today.gossipChip", "八卦"),
				ChipFill = Hearth.RosePale,
				ChipInk = Hearth.Rose,
				Open = () => Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Gossip)
			});
		}
		foreach (NpcSocialMoment m in Ctx.Social.Web.ForFeed(today.TotalDays, 2, 4).Where(m => Ctx.Npcs.IsEnabled(m.First) && Ctx.Npcs.IsEnabled(m.Second)))
		{
			_town.Rows.Add(new Row
			{
				Npc = m.First,
				Text = ShellText.Clean(m.WhatHappened),
				Sub = Tr.Get("shell.today.pair", "{{a}}和{{b}}", ("a", Ctx.Display(m.First)), ("b", Ctx.Display(m.Second))) + " · " + Places.Name(m.Place),
				Chip = WorldPage.ToneWord(m.Vibe),
				ChipFill = Hearth.ParchmentDeep,
				ChipInk = Hearth.QuillSoft,
				Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Village)
			});
		}
		AddPaper(today.TotalDays);
		AddPromo();
		if (Ctx.Market?.Enabled == true && Ctx.Market.HotNames() is { Count: > 0 } hot)
		{
			_town.Rows.Add(new Row
			{
				Glyph = "市",
				GlyphColor = Hearth.Leaf,
				Text = Tr.Get("shell.today.hot", "这周镇上想要：{{items}}", ("items", string.Join("、", hot.Take(3)))),
				Chip = Tr.Get("shell.today.market", "看行情"),
				ChipFill = Hearth.LeafPale,
				ChipInk = Hearth.LeafDeep,
				Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Market)
			});
		}
	}

	/// <summary>后台公告和最新两条推荐。</summary>
	private void AddPromo()
	{
		_promo = new Card { Title = Tr.Get("shell.today.promo", "推荐") };
		AISV.Game.Promo.ServerContentService? content = Ctx.Promo?.Content;
		if (content?.Available != true)
		{
			_announcement = "";
			return;
		}
		_announcement = ShellText.Clean(content.Content.Announcement);
		foreach (AISV.Core.Promo.PromoItem p in content.Content.Promo.Take(2))
		{
			string body = ShellText.Clean(p.Body).Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
			_promo.Rows.Add(new Row
			{
				Glyph = "荐",
				GlyphColor = Hearth.Rose,
				Text = ShellText.Clean(p.Title),
				Sub = body,
				Chip = Tr.Get("shell.today.promoChip", "去看看"),
				ChipFill = Hearth.RosePale,
				ChipInk = Hearth.Rose,
				Open = () => Host.Push(new PromoPage(Ctx))
			});
		}
	}

	private void AddPaper(int today)
	{
		if (Ctx.News?.Enabled != true)
		{
			return;
		}
		WeeklyNews? latest = Ctx.News.Latest;
		string text;
		if (latest != null && today - latest.TotalDay is >= 0 and <= 1)
		{
			text = Tr.Get("shell.today.paperOut", "新一期《鹈鹕镇周报》出来了");
		}
		else if (Game1.dayOfMonth % 7 == 6)
		{
			text = Tr.Get("shell.today.paperTomorrow", "周报明天出刊");
		}
		else if (latest != null)
		{
			text = Tr.Get("shell.today.paperLatest", "上一期周报：{{date}}", ("date", ShellText.Date(latest.Day, latest.Season, latest.Year)));
		}
		else
		{
			return;
		}
		_town.Rows.Add(new Row
		{
			Glyph = "报",
			GlyphColor = Hearth.Amber,
			Text = text,
			Chip = latest != null ? Tr.Get("shell.today.read", "去读") : "",
			ChipFill = Hearth.AmberPale,
			ChipInk = Hearth.Amber,
			Open = latest != null ? () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.News) : null
		});
	}

	/// <summary>今天的约见和上门帮工，按时间排在"今天"里。</summary>
	private void AddPlans(int today)
	{
		List<(int Sort, Row Row)> plans = new List<(int, Row)>();
		foreach (Meetup m in Ctx.Meetups?.Upcoming().Where(m => m.Day == today && (Ctx.Stories?.Enabled != true || m.Owner != MeetupRules.StoryOwner)) ?? Enumerable.Empty<Meetup>())
		{
			plans.Add((m.Time, new Row
			{
				Npc = m.Npc,
				Text = Tr.Get("todayPage.meetup", "和{{npc}}在{{place}}见面", ("npc", Ctx.Display(m.Npc)), ("place", MeetupService.PlaceLabel(m))),
				Sub = Clock.Text(m.Time),
				Chip = Tr.Get("todayPage.meetupChip", "约见"),
				ChipFill = Hearth.RosePale,
				ChipInk = Hearth.Rose,
				Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Help)
			}));
		}
		foreach (Meetup m in Ctx.Meetups?.WithMe() ?? Array.Empty<Meetup>())
		{
			string id = m.Id;
			plans.Add((m.KeptAt, new Row
			{
				Npc = m.Npc,
				Text = Tr.Get("todayPage.company", "{{npc}}正陪着你", ("npc", Ctx.Display(m.Npc))),
				Sub = Tr.Get("todayPage.companyUntil", "到 {{time}} · 点一下让对方先回去", ("time", Clock.Text(m.CompanyUntil))),
				Chip = Tr.Get("todayPage.companyChip", "先到这儿吧"),
				ChipFill = Hearth.RosePale,
				ChipInk = Hearth.Rose,
				Open = () =>
				{
					Ctx.Meetups?.EndCompany(id);
					Game1.exitActiveMenu();
				}
			}));
		}
		if (Ctx.FarmHelp?.Enabled == true)
		{
			foreach (FarmHelpJob j in Ctx.FarmHelp.All().Where(j => j.Day == today))
			{
				bool working = j.State == FarmHelpStates.Working;
				bool open = FarmHelpStates.IsOpen(j.State);
				string progress = working && j.Target > 0 ? " · " + Tr.Get("todayPage.progress", "干到 {{done}}/{{target}}", ("done", Math.Min(j.Target, FarmHelpService.CountDone(j))), ("target", j.Target)) : "";
				plans.Add((j.Start, new Row
				{
					Npc = j.Npc,
					Text = Tr.Get("todayPage.farmHelp", "{{npc}}来农场{{task}}", ("npc", Ctx.Display(j.Npc)), ("task", FarmHelpRules.TaskLabel(j.Task))),
					Sub = Clock.Text(j.Start) + progress,
					Chip = working ? Tr.Get("todayPage.working", "正在干") : open ? Tr.Get("todayPage.helpChip", "帮工") : Tr.Get("todayPage.finished", "结束了"),
					ChipFill = working ? Hearth.LeafPale : new Color(220, 240, 238),
					ChipInk = working ? Hearth.LeafDeep : new Color(70, 165, 160),
					Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Help)
				}));
			}
		}
		AddStories(plans, today);
		_today.Rows.AddRange(plans.OrderBy(p => p.Sort).Select(p => p.Row));
	}

	/// <summary>每位伴侣进行中的那一步；今天约了见面的按时间排。</summary>
	private void AddStories(List<(int Sort, Row Row)> plans, int today)
	{
		AISV.Game.Companions.SpouseStoryService? s = Ctx.Stories;
		if (s == null || !s.Enabled)
		{
			return;
		}
		foreach (string partner in s.Partners())
		{
			if (s.ActiveFor(partner) is not StoryPackage pkg || pkg.Current is not StoryStep step || (step.Kind == StepKinds.Letter && step.Delivered))
			{
				continue;
			}
			Meetup? m = step.Kind == StepKinds.Event ? s.MeetupOf(step) : null;
			bool todayMeet = m != null && MeetupStates.IsOpen(m.State) && m.Day == today;
			plans.Add((todayMeet ? m!.Time : 2500, new Row
			{
				Npc = partner,
				Text = Ctx.Display(partner) + " " + SpouseStoryRules.TodayWhat(step.Kind),
				Sub = (todayMeet ? Clock.Text(m!.Time) + " · " + MeetupService.PlaceLabel(m) + " · " : "") + ShellText.Clean(pkg.Title),
				Chip = Tr.Get("todayPage.storyChip", "小故事"),
				ChipFill = new Color(236, 226, 248),
				ChipInk = new Color(130, 90, 180),
				Open = () => Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Partners)
			}));
		}
	}

	/// <summary>接下的委托（快到期的标红），板上还有可接的就提一句。</summary>
	private void AddCommissions()
	{
		AISV.Game.World.CommissionService? s = Ctx.Commissions;
		if (s == null || !s.Enabled)
		{
			return;
		}
		Color red = new Color(196, 72, 60), redSoft = new Color(250, 226, 222);
		foreach (AISV.Core.World.Commissions.Commission c in s.Active().OrderBy(c => c.LastDay))
		{
			int left = AISV.Game.World.CommissionService.DaysLeft(c);
			bool urgent = left <= 1;
			_today.Rows.Add(new Row
			{
				Npc = c.Poster,
				Text = ShellText.Clean(AISV.Game.World.CommissionService.TaskLine(c)),
				Sub = Tr.Get("todayPage.reward", "酬劳 {{gold}}g", ("gold", c.RewardGold)),
				Chip = left <= 0 ? Tr.Get("todayPage.dueToday", "今天截止") : Tr.Get("todayPage.daysLeft", "还剩 {{n}} 天", ("n", left)),
				ChipFill = urgent ? redSoft : Hearth.Seam,
				ChipInk = urgent ? red : Hearth.QuillSoft,
				Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Commissions)
			});
		}
		int open = s.Open().Count;
		if (open > 0)
		{
			_today.Rows.Add(new Row
			{
				Glyph = "委",
				GlyphColor = new Color(205, 110, 50),
				Text = Tr.Get("todayPage.openQuests", "板上有 {{n}} 条委托可以接", ("n", open)),
				Chip = Tr.Get("todayPage.look", "去看看"),
				ChipFill = Hearth.AmberPale,
				ChipInk = Hearth.Amber,
				Open = () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Commissions)
			});
		}
	}

	/// <summary>托村民办的事：还价了要你答复的排前面。</summary>
	private void AddErrands()
	{
		AISV.Game.World.ErrandService? s = Ctx.Errands;
		if (s == null || !s.Enabled)
		{
			return;
		}
		foreach (AISV.Core.World.Errands.Errand e in s.Open().OrderBy(e => e.State == AISV.Core.World.Errands.ErrandStates.Countered ? 0 : 1).ThenBy(e => e.LastDay))
		{
			bool counter = e.State == AISV.Core.World.Errands.ErrandStates.Countered;
			bool active = e.State == AISV.Core.World.Errands.ErrandStates.Active;
			_today.Rows.Add(new Row
			{
				Npc = e.Npc,
				Text = Tr.Get("todayPage.errand", "托{{name}}{{what}}", ("name", s.Display(e.Npc)), ("what", s.What(e))),
				Sub = counter ? Tr.Get("todayPage.errandCounter", "开价 {{gold}}g，等你答复", ("gold", e.Counter)) : active ? Tr.Get("todayPage.errandActive", "办理中，还剩 {{n}} 天", ("n", Math.Max(0, AISV.Game.World.ErrandService.DaysLeft(e)))) : Tr.Get("todayPage.errandWaiting", "等他回话"),
				Chip = counter ? Tr.Get("todayPage.errandAnswer", "去答复") : Tr.Get("todayPage.errandChip", "我发的"),
				ChipFill = counter ? Hearth.AmberPale : Hearth.Seam,
				ChipInk = counter ? Hearth.Amber : Hearth.QuillSoft,
				Open = () =>
				{
					CommissionsPage.ShowMine = true;
					Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Commissions);
				}
			});
		}
	}

	private void OpenVillager(string name)
	{
		VillagersPage.Focus = name;
		Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Contacts);
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		Hearth.BeginClip(b, area);
		const int pad = 28;
		int x = area.X + pad, w = area.Width - pad * 2;
		int y = area.Y + 18 - _scroll.Offset;
		y = DrawHero(b, x, y, w) + 18;
		y = Kit.RetryCard(b, Ctx, gossip: true, x, y, w, area, hits, Host);
		if (_announcement.Length > 0)
		{
			y = PromoPage.Announcement(b, _announcement, x, y, w) + 16;
		}
		if (w >= 1000)
		{
			const int gap = 24;
			int lw = (w - gap) * 56 / 100, rw = w - gap - lw;
			int ly = DrawCard(b, _today, x, y, lw, hits);
			ly = DrawCard(b, _moods, x, ly + 16, lw, hits);
			int ry = DrawCard(b, _town, x + lw + gap, y, rw, hits);
			if (_promo.Rows.Count > 0)
			{
				ry = DrawCard(b, _promo, x + lw + gap, ry + 16, rw, hits);
			}
			y = Math.Max(ly, ry) + 22;
		}
		else
		{
			y = DrawCard(b, _today, x, y, w, hits) + 16;
			y = DrawCard(b, _moods, x, y, w, hits) + 16;
			y = DrawCard(b, _town, x, y, w, hits) + 22;
			if (_promo.Rows.Count > 0)
			{
				y = DrawCard(b, _promo, x, y - 6, w, hits) + 22;
			}
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y, 12);
	}

	private int DrawHero(SpriteBatch b, int x, int y, int w)
	{
		string name = Game1.player?.Name ?? "";
		int count = _today.Rows.Count;
		string summary = count == 0 ? Tr.Get("shell.today.nothing", "今天没什么要赶的事。") : Tr.Get("shell.today.count", "今天有 {{n}} 件事", ("n", count));
		int sw = (int)Hearth.Width(summary, 0.85f);
		Hearth.InkBold(b, Hearth.Truncate(ShellText.Greeting(name), w - sw - 60, 1.35f), new Vector2(x + 4, y), Hearth.Quill, 1.35f);
		int lh = Hearth.LineHeight(1.35f);
		Hearth.Ink(b, ShellText.TopLine(), new Vector2(x + 6, y + lh + 4), Hearth.QuillSoft, 0.82f);
		Rectangle chip = new Rectangle(x + w - sw - 36, y + 10, sw + 32, 40);
		Hearth.FillRounded(b, chip, count == 0 ? Hearth.ParchmentDeep : Hearth.LeafPale, 20);
		Hearth.InkMiddle(b, summary, chip, count == 0 ? Hearth.QuillSoft : Hearth.LeafDeep, 0.85f);
		return y + lh + 4 + Hearth.LineHeight(0.82f);
	}

	private int DrawCard(SpriteBatch b, Card card, int x, int y, int w, Hits hits)
	{
		int rows = Math.Max(1, card.Rows.Count);
		Rectangle r = new Rectangle(x, y, w, 18 + 34 + rows * RowH + 12);
		Hearth.CardSlip(b, r);
		Kit.SectionTitle(b, card.Title, x + 20, y + 18);
		if (card.Rows.Count > 0)
		{
			string n = card.Rows.Count.ToString();
			Hearth.Ink(b, n, new Vector2(x + w - 20 - Hearth.Width(n, 0.7f), y + 22), Hearth.QuillPale, 0.7f);
		}
		int ry = y + 52;
		if (card.Rows.Count == 0)
		{
			Hearth.Ink(b, card.Empty, new Vector2(x + 20, ry + 18), Hearth.QuillPale, 0.8f);
			return r.Bottom;
		}
		for (int i = 0; i < card.Rows.Count; i++)
		{
			Row row = card.Rows[i];
			Rectangle rr = new Rectangle(x + 8, ry, w - 16, RowH);
			if (row.Open != null && Hearth.Hover(rr, _view))
			{
				Hearth.FillRounded(b, rr, Hearth.Honey, 10);
			}
			if (i > 0)
			{
				Hearth.HLine(b, rr.X + 12, rr.Y, rr.Width - 24, Hearth.Seam);
			}
			Rectangle face = new Rectangle(rr.X + 12, rr.Y + 10, 46, 46);
			if (row.Npc != null)
			{
				Kit.Avatar(b, face, Ctx, row.Npc);
			}
			else
			{
				Hearth.Glyph(b, face, row.GlyphColor, row.Glyph);
			}
			int tx = face.Right + 14, right = rr.Right - 12;
			if (row.Chip.Length > 0)
			{
				int cw = Hearth.TagWidth(row.Chip, 0.66f);
				Hearth.Tag(b, new Vector2(right - cw, rr.Y + (row.Sub.Length > 0 ? 12 : 20)), row.Chip, row.ChipFill, row.ChipInk, 0.66f);
				right -= cw + 10;
			}
			if (row.Sub.Length > 0)
			{
				Hearth.Ink(b, Hearth.Truncate(row.Text, right - tx, 0.85f), new Vector2(tx, rr.Y + 8), Hearth.Quill, 0.85f);
				Hearth.Ink(b, Hearth.Truncate(row.Sub, right - tx, 0.7f), new Vector2(tx, rr.Y + 36), Hearth.QuillPale, 0.7f);
			}
			else
			{
				Hearth.Ink(b, Hearth.Truncate(row.Text, right - tx, 0.85f), new Vector2(tx, rr.Y + 20), Hearth.Quill, 0.85f);
			}
			if (row.Open != null)
			{
				hits.Add(rr, row.Open, _view);
			}
			ry += RowH;
		}
		return r.Bottom;
	}
}
