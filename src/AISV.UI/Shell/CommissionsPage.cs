using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Favors;
using AISV.Core.Promo;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Core.World.Errands;
using AISV.Game.Dialogue;
using AISV.Game.World;
using AISV.Game.Promo;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Quests;
using StardewValley.SpecialOrders;

namespace AISV.UI.Shell;

/// <summary>世界 · 委托：镇上的委托可以接下或放弃；物品请求、托付、杂货店告示板、特别订单一起列出来，只能看；"我发的"是托村民办的事。</summary>
internal sealed class CommissionsPage : ShellPage
{
	private enum Source
	{
		Commission,
		Request,
		Favor,
		HelpWanted,
		Order
	}

	private sealed class Entry
	{
		public Source Source;

		public string Npc = "";

		public string Who = "";

		public string ItemId = "";

		public string Headline = "";

		public string Body = "";

		public string Reward = "";

		public int? DaysLeft;

		public Commission? Commission;
	}

	private static readonly Color Orange = new Color(205, 110, 50);

	private static readonly Color Urgent = new Color(196, 72, 60);

	private static readonly Color UrgentSoft = new Color(250, 226, 222);

	private static readonly Color Purple = new Color(140, 100, 190);

	private static readonly Color PurpleSoft = new Color(236, 226, 248);

	private readonly Scroller _scroll = new Scroller();

	private List<Entry> _entries = new List<Entry>();

	private List<Errand> _mine = new List<Errand>();

	private const int MineFilter = 4;

	/// <summary>从别的页跳过来时直接看"我发的"。</summary>
	public static bool ShowMine { get; set; }

	private IReadOnlyList<SupportTask> _support = Array.Empty<SupportTask>();

	private SupportTask? _confirm;

	private int _filter;

	private int _seenVersion = -1;

	private Rectangle _view;

	private string? _confirmWithdraw;

	public CommissionsPage(ShellContext ctx) : base(ctx)
	{
	}

	private static string[] Filters => new[]
	{
		Tr.Get("commissionsPage.all", "全部"),
		Tr.Get("commissionsPage.open", "可接"),
		Tr.Get("commissionsPage.taken", "已接下"),
		Tr.Get("commissionsPage.other", "其他"),
		Tr.Get("commissionsPage.mine", "我发的")
	};

	private int Version => (Ctx.Commissions?.Version ?? 0) * 7919 + (Ctx.Errands?.Version ?? 0);

	public override void Reload()
	{
		_seenVersion = Version;
		if (ShowMine)
		{
			ShowMine = false;
			_filter = MineFilter;
			_scroll.Reset();
		}
		bool board = Ctx.Commissions?.Enabled == true, mine = Ctx.Errands?.Enabled == true;
		if (!board && mine)
		{
			_filter = MineFilter;
		}
		else if (!mine && _filter == MineFilter)
		{
			_filter = 0;
		}
		_mine = Ctx.Errands is { Enabled: true } errands
			? errands.All().OrderBy(e => ErrandStates.IsOpen(e.State) ? 0 : 1).ThenBy(e => e.State == ErrandStates.Countered ? 0 : 1).ThenByDescending(e => Math.Max(e.PostedDay, e.ClosedDay)).ToList()
			: new List<Errand>();
		if (_filter == MineFilter)
		{
			_entries = new List<Entry>();
			_support = Array.Empty<SupportTask>();
			return;
		}
		List<Entry> all = new List<Entry>();
		ReadCommissions(all);
		List<Entry> others = new List<Entry>();
		ReadItemRequests(others);
		ReadFavors(others);
		ReadHelpWanted(others);
		ReadSpecialOrders(others);
		all.AddRange(others.OrderBy(e => e.DaysLeft ?? int.MaxValue));
		_support = _filter is 0 or 1 ? Ctx.Promo?.OpenSupport() ?? Array.Empty<SupportTask>() : Array.Empty<SupportTask>();
		_entries = all.Where(e => _filter switch
		{
			1 => e.Commission?.State == CommissionStates.Open,
			2 => e.Commission?.State == CommissionStates.Active,
			3 => e.Commission == null,
			_ => true
		}).ToList();
	}

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
				SupportConfirmed();
			}
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	/// <summary>确认后用浏览器打开链接，就算完成，发谢礼。</summary>
	private void SupportConfirmed()
	{
		SupportTask? t = _confirm;
		_confirm = null;
		if (t == null || Ctx.Promo == null)
		{
			return;
		}
		LinkResult opened = ExternalLink.Open(t.Url, Ctx.Log);
		(bool ok, string message) = Ctx.Promo.CompleteSupport(t);
		if (opened == LinkResult.Copied)
		{
			message += Tr.Get("commissionsPage.supportCopied", "（打不开浏览器，链接已复制。）");
		}
		if (!ok)
		{
			Game1.playSound("cancel");
		}
		if (message.Length > 0)
		{
			Host.Notice(message);
		}
		Reload();
	}

	// ---------------- 读 ----------------

	private void ReadCommissions(List<Entry> into)
	{
		CommissionService? service = Ctx.Commissions;
		if (service == null || !service.Enabled)
		{
			return;
		}
		foreach (Commission c in service.Active().OrderBy(c => c.LastDay).Concat(service.Open().OrderBy(c => c.LastDay)))
		{
			into.Add(new Entry
			{
				Source = Source.Commission,
				Npc = c.Poster,
				ItemId = c.ItemId,
				Headline = CommissionRules.Headline(c.Kind),
				Body = ShellText.Clean(c.Text) + "\n" + ShellText.Clean(CommissionService.TaskLine(c)),
				Reward = Tr.Get("commissionsPage.reward", "酬劳 {{gold}}g · 好感 +{{points}}", ("gold", c.RewardGold), ("points", c.RewardFriendship)),
				DaysLeft = CommissionService.DaysLeft(c),
				Commission = c
			});
		}
	}

	private void ReadItemRequests(List<Entry> into)
	{
		if (Ctx.Favors == null)
		{
			return;
		}
		int today = GameState.Today.TotalDays;
		foreach (ItemRequestFavor f in Ctx.Favors.Requests.All.Where(f => f.IsActive && (f.LapsesOn <= 0 || f.LapsesOn >= today)))
		{
			string item = ItemName(f.WantedItemId, f.WantedItemName);
			int gold = string.Equals(f.PayKind, "monetary", StringComparison.OrdinalIgnoreCase) ? Math.Max(0, f.PayGold) : 0;
			into.Add(new Entry
			{
				Source = Source.Request,
				Npc = f.Asker,
				ItemId = f.WantedItemId,
				Headline = Tr.Get("commissionsPage.wants", "求购{{item}} ×{{count}}", ("item", item), ("count", Math.Max(1, f.Amount))),
				Body = ShellText.Clean(f.Plea),
				Reward = GoldLine(gold),
				DaysLeft = f.LapsesOn > 0 ? f.LapsesOn - today : null
			});
		}
	}

	private void ReadFavors(List<Entry> into)
	{
		if (Ctx.Favors == null)
		{
			return;
		}
		int today = GameState.Today.TotalDays;
		foreach (NpcFavor f in Ctx.Favors.Favors.All.Where(f => f.IsPending))
		{
			int left = f.CreatedTotalDay + f.DaysAllowed - today;
			if (left < 0)
			{
				continue;
			}
			string target = ShellText.Clean(f.Aim);
			string type = f.Kind.ToLowerInvariant();
			string itemId = type == "bring_item" ? f.ItemId ?? "" : "";
			into.Add(new Entry
			{
				Source = Source.Favor,
				Npc = f.Asker,
				ItemId = itemId,
				Headline = type switch
				{
					"bring_item" => Tr.Get("commissionsPage.favor.item", "想要{{item}}", ("item", ItemName(itemId, target))),
					"deliver_message" => Tr.Get("commissionsPage.favor.message", "帮忙带话给{{npc}}", ("npc", Ctx.Display(target))),
					"companionship" => Tr.Get("commissionsPage.favor.company", "想找人陪陪"),
					"opinion" => Tr.Get("commissionsPage.favor.opinion", "想听听你的看法"),
					_ => Tr.Get("commissionsPage.favor.other", "托你帮个忙")
				},
				Body = ShellText.Clean(f.Ask),
				DaysLeft = left
			});
		}
	}

	private static void ReadHelpWanted(List<Entry> into)
	{
		List<Quest> quests = new List<Quest>();
		Quest? daily = Game1.questOfTheDay;
		if (daily != null && !daily.accepted.Value && !daily.completed.Value)
		{
			quests.Add(daily);
		}
		if (Game1.player?.questLog != null)
		{
			quests.AddRange(Game1.player.questLog.Where(q => q != null && q.dailyQuest.Value && !q.completed.Value));
		}
		foreach (Quest q in quests.Distinct())
		{
			(string npc, string itemId) = q switch
			{
				ItemDeliveryQuest d => (d.target.Value, d.ItemId.Value),
				ResourceCollectionQuest r => (r.target.Value, r.ItemId.Value),
				FishingQuest f => (f.target.Value, f.ItemId.Value),
				SlayMonsterQuest s => (s.target.Value, ""),
				_ => ("", "")
			};
			string qualified = string.IsNullOrWhiteSpace(itemId) ? "" : ItemRegistry.QualifyItemId(itemId) ?? itemId;
			into.Add(new Entry
			{
				Source = Source.HelpWanted,
				Npc = npc ?? "",
				ItemId = qualified,
				Headline = ShellText.Clean(q.GetName()),
				Body = ShellText.Clean(q.GetDescription()),
				Reward = GoldLine(Math.Max(0, q.GetMoneyReward())),
				DaysLeft = q.accepted.Value ? q.daysLeft.Value > 0 ? q.daysLeft.Value - 1 : null : 0
			});
		}
	}

	private static void ReadSpecialOrders(List<Entry> into)
	{
		if (Game1.player?.team?.specialOrders is not { } orders)
		{
			return;
		}
		foreach (SpecialOrder o in orders)
		{
			if (o == null || o.questState.Value != SpecialOrderStatus.InProgress)
			{
				continue;
			}
			int days = o.GetDaysLeft();
			into.Add(new Entry
			{
				Source = Source.Order,
				Npc = o.requester.Value ?? "",
				Headline = ShellText.Clean(o.GetName()),
				Body = ShellText.Clean(o.GetDescription()),
				Reward = GoldLine(Math.Max(0, o.GetMoneyReward())),
				DaysLeft = days > 0 ? days - 1 : null
			});
		}
	}

	private static string GoldLine(int gold) => gold > 0 ? Tr.Get("commissionsPage.gold", "酬劳 {{gold}}g", ("gold", gold)) : "";

	private static string ItemName(string? id, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(id))
		{
			ParsedItemData? data = ItemRegistry.GetData(id);
			if (data != null && !data.IsErrorItem && !string.IsNullOrWhiteSpace(data.DisplayName))
			{
				return ShellText.Clean(data.DisplayName);
			}
		}
		return ShellText.Clean(fallback);
	}

	private static (string Text, Color Fill, Color Ink) SourceChip(Source s) => s switch
	{
		Source.Commission => (Tr.Get("commissionsPage.src.commission", "镇上的委托"), AisvTheme.AccentSoft, AisvTheme.AccentDark),
		Source.Request => (Tr.Get("commissionsPage.src.request", "想要的东西"), AisvTheme.GoldSoft, AisvTheme.Gold),
		Source.Favor => (Tr.Get("commissionsPage.src.favor", "托你帮忙"), PurpleSoft, Purple),
		Source.HelpWanted => (Tr.Get("commissionsPage.src.helpWanted", "杂货店告示板"), AisvTheme.Divider, AisvTheme.TextMuted),
		_ => (Tr.Get("commissionsPage.src.order", "特别订单"), AisvTheme.Divider, AisvTheme.TextMuted)
	};

	private static string Deadline(int? days) => days switch
	{
		null => Tr.Get("commissionsPage.deadline.none", "不急。"),
		<= 0 => Tr.Get("commissionsPage.deadline.today", "仅限今天！"),
		1 => Tr.Get("commissionsPage.deadline.tomorrow", "明天截止。"),
		_ => Tr.Get("commissionsPage.deadline.days", "{{days}}天内交货。", ("days", days.Value))
	};

	// ---------------- 接下 / 放弃 ----------------

	private void Toggle(Commission c)
	{
		CommissionService? service = Ctx.Commissions;
		if (service == null)
		{
			return;
		}
		(bool ok, string message) = c.State == CommissionStates.Active ? service.Abandon(c.Id) : service.Accept(c.Id);
		Game1.playSound(ok ? (c.State == CommissionStates.Active ? "newArtifact" : "trashcan") : "cancel");
		Host.Notice(message);
		Reload();
	}

	// ---------------- 画 ----------------

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (Version != _seenVersion)
		{
			Reload();
		}
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		DrawHeader(b, header, hits);
		Rectangle feed = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		_view = feed;
		if (_filter == MineFilter)
		{
			DrawMine(b, feed, hits);
			return;
		}
		if (_entries.Count == 0 && _support.Count == 0)
		{
			Kit.Empty(b, feed, _filter == 0 ? Tr.Get("commissionsPage.empty", "今天板上没有委托。") : Tr.Get("commissionsPage.emptyFilter", "这一栏现在是空的。"));
			return;
		}
		AisvTheme.BeginClip(b, feed);
		int w = Math.Min(feed.Width - 48, 900);
		int x = feed.X + (feed.Width - w) / 2;
		int y = feed.Y + 16 - _scroll.Offset;
		foreach (SupportTask t in _support)
		{
			y = DrawSupport(b, t, x, y, w, hits) + 14;
		}
		foreach (Entry e in _entries)
		{
			y = DrawCard(b, e, x, y, w, hits) + 14;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, feed, y);
		if (_confirm != null)
		{
			Kit.ConfirmLink(b, area, hits, _confirm.Url, Tr.Get("commissionsPage.supportNote", "打开就算完成，酬劳马上到手。"), SupportConfirmed, () =>
			{
				Game1.playSound("bigDeSelect");
				_confirm = null;
			});
		}
	}

	private void DrawHeader(SpriteBatch b, Rectangle header, Hits hits)
	{
		string title = Tr.Get("commissionsPage.title", "委托");
		Kit.Header(b, header, title, "", null, hits, null);
		int fx = header.X + 20 + (int)AisvTheme.Width(title, 1.15f) + 28;
		string[] labels = Filters;
		bool board = Ctx.Commissions?.Enabled == true, mine = Ctx.Errands?.Enabled == true;
		for (int i = 0; i < labels.Length; i++)
		{
			if (i == MineFilter ? !mine : !board)
			{
				continue;
			}
			int fw = (int)AisvTheme.Width(labels[i], 0.85f) + 32;
			Rectangle r = new Rectangle(fx, header.Y + 10, fw, 40);
			bool sel = i == _filter;
			AisvTheme.Bordered(b, r, sel ? AisvTheme.AccentSoft : AisvTheme.Hover(r) ? AisvTheme.Card : AisvTheme.Page, sel ? AisvTheme.Accent : AisvTheme.CardBorder, r.Height / 2);
			AisvTheme.TextCentered(b, labels[i], r, sel ? AisvTheme.AccentDark : AisvTheme.TextMuted, 0.85f);
			int index = i;
			hits.Add(r, () =>
			{
				if (_filter != index)
				{
					Game1.playSound("smallSelect");
					_filter = index;
					_scroll.Reset();
					Reload();
				}
			});
			fx = r.Right + 8;
		}
		if (_filter == MineFilter)
		{
			if (Ctx.Errands?.CanPost == true)
			{
				string post = Tr.Get("commissionsPage.post", "发布委托");
				int pw = (int)AisvTheme.Width(post, 0.85f) + 44;
				if (header.Right - 16 - pw <= fx + 12)
				{
					post = "+";
					pw = 44;
				}
				Rectangle pr = new Rectangle(header.Right - 16 - pw, header.Y + 10, pw, 44);
				AisvTheme.Button(b, pr, post, primary: true, hover: AisvTheme.Hover(pr), scale: 0.85f);
				hits.Add(pr, () => Host.Push(new ErrandFormPage(Ctx)));
				string help = Tr.Get("commissionsPage.farmHelp", "请人来农场帮忙");
				int hw = (int)AisvTheme.Width(help, 0.75f) + 32;
				Rectangle hr = new Rectangle(pr.X - 10 - hw, header.Y + 12, hw, 40);
				if (hr.X > fx + 12)
				{
					AisvTheme.Button(b, hr, help, hover: AisvTheme.Hover(hr), scale: 0.75f);
					hits.Add(hr, () => Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Help));
				}
			}
		}
		else if (Ctx.Commissions?.Enabled == true)
		{
			string count = Tr.Get("commissionsPage.takenCount", "接下 {{n}}/{{max}}", ("n", Ctx.Commissions.Active().Count), ("max", CommissionRules.MaxActive));
			AisvTheme.DrawText(b, count, new Vector2(header.Right - 24 - AisvTheme.Width(count, 0.75f), header.Y + 22), AisvTheme.TextMuted, 0.75f);
		}
	}

	private int DrawCard(SpriteBatch b, Entry e, int x, int y, int w, Hits hits)
	{
		bool taken = e.Commission?.State == CommissionStates.Active;
		int tx = x + 24 + 56 + 16, textW = x + w - 24 - tx;
		List<string> body = e.Body.Split('\n').Where(s => s.Trim().Length > 0).SelectMany(s => AisvTheme.WrapLines(s, textW, 0.8f)).ToList();
		int lh = AisvTheme.LineHeight(0.8f);
		Rectangle r = new Rectangle(x, y, w, 20 + 34 + 34 + body.Count * lh + 10 + 44 + 20);
		if (r.Bottom < _view.Y || r.Y > _view.Bottom)
		{
			return r.Bottom;
		}
		AisvTheme.DrawCard(b, r, highlight: taken);
		if (taken)
		{
			AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 16, 5, r.Height - 32), AisvTheme.Accent, 2);
		}
		Rectangle face = new Rectangle(r.X + 24, r.Y + 22, 56, 56);
		Kit.Avatar(b, face, Ctx, e.Npc.Length > 0 ? e.Npc : null);
		string who = e.Npc.Length > 0 ? Ctx.Display(e.Npc) : Tr.Get("commissionsPage.anonymous", "匿名");
		AisvTheme.Bold(b, who, new Vector2(tx, r.Y + 20), AisvTheme.Text, 0.9f);
		(string src, Color fill, Color ink) = SourceChip(e.Source);
		AisvTheme.Chip(b, new Vector2(tx + AisvTheme.Width(who, 0.9f) + 12, r.Y + 22), src, fill, ink, 0.62f);
		int right = r.Right - 24;
		ParsedItemData? data = string.IsNullOrWhiteSpace(e.ItemId) ? null : ItemRegistry.GetData(e.ItemId);
		if (data != null && !data.IsErrorItem)
		{
			Rectangle icon = new Rectangle(right - 48, r.Y + 20, 48, 48);
			AisvTheme.FillRounded(b, icon, AisvTheme.PageDeep, 8);
			b.Draw(data.GetTexture(), AisvTheme.Inflate(icon, -6), data.GetSourceRect(), Color.White);
			right = icon.X - 12;
		}
		int ty = r.Y + 20 + 34;
		AisvTheme.Bold(b, AisvTheme.Truncate(e.Headline, right - tx, 0.95f), new Vector2(tx, ty), Orange, 0.95f);
		ty += 34;
		foreach (string line in body)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.Text, 0.8f);
			ty += lh;
		}
		ty += 10;
		const int btnW = 140;
		int rowRight = e.Commission != null ? r.Right - 24 - btnW - 12 : r.Right - 24;
		string deadline = Deadline(e.DaysLeft);
		int deadlineW = deadline.Length > 0 ? AisvTheme.ChipWidth(deadline, 0.66f) + 8 : 0;
		int cx = tx;
		if (e.Reward.Length > 0)
		{
			Rectangle rc = AisvTheme.Chip(b, new Vector2(cx, ty + 8), AisvTheme.Truncate(e.Reward, Math.Max(60, rowRight - cx - deadlineW - 24), 0.66f), AisvTheme.GoldSoft, AisvTheme.Gold, 0.66f);
			cx = rc.Right + 8;
		}
		if (deadline.Length > 0)
		{
			bool urgent = e.DaysLeft <= 1;
			AisvTheme.Chip(b, new Vector2(cx, ty + 8), deadline, urgent ? UrgentSoft : AisvTheme.Divider, urgent ? Urgent : AisvTheme.TextMuted, 0.66f);
			cx += deadlineW;
		}
		if (e.Commission is Commission c)
		{
			Rectangle btn = new Rectangle(r.Right - 24 - btnW, ty, btnW, 44);
			AisvTheme.Button(b, btn, taken ? Tr.Get("commissionsPage.abandon", "放弃") : Tr.Get("commissionsPage.accept", "接下"), primary: !taken, hover: AisvTheme.Hover(btn, _view));
			hits.Add(btn, () => Toggle(c), _view);
			if (taken)
			{
				string status = c.Kind == CommissionKinds.Visit ? Tr.Get("commissionsPage.visitHint", "白天去他家坐坐就算完成") : Tr.Get("commissionsPage.deliverHint", "东西备齐了去找他交");
				float sw = AisvTheme.Width(status, 0.62f);
				if (btn.X - 12 - sw > cx)
				{
					AisvTheme.DrawText(b, status, new Vector2(btn.X - 12 - sw, btn.Y + 14), AisvTheme.AccentDark, 0.62f);
				}
			}
		}
		return r.Bottom;
	}

	// ---------------- 我发的 ----------------

	private void DrawMine(SpriteBatch b, Rectangle feed, Hits hits)
	{
		ErrandService? service = Ctx.Errands;
		if (service == null || !service.Enabled)
		{
			Kit.Empty(b, feed, Tr.Get("commissionsPage.errandsOff", "给村民发委托没开。"), Tr.Get("commissionsPage.errandsOffHint", "在设置 > 世界里打开。"));
			return;
		}
		if (_mine.Count == 0)
		{
			Kit.Empty(b, feed, Tr.Get("commissionsPage.mineEmpty", "还没托村民办过事。"), service.CanPost
				? Tr.Get("commissionsPage.mineHint", "点右上角\"发布委托\"，出钱托人找东西、代买、做菜或捎东西。")
				: Tr.Get("commissionsPage.mineHost", "联机时只有房主能发委托。"));
			return;
		}
		AisvTheme.BeginClip(b, feed);
		int w = Math.Min(feed.Width - 48, 900);
		int x = feed.X + (feed.Width - w) / 2;
		int y = feed.Y + 16 - _scroll.Offset;
		string open = Tr.Get("commissionsPage.mineCount", "进行中 {{n}}/{{max}}", ("n", _mine.Count(e => ErrandStates.IsOpen(e.State))), ("max", ErrandRules.MaxTotal));
		AisvTheme.DrawText(b, open, new Vector2(x + 4, y), AisvTheme.TextMuted, 0.72f);
		y += AisvTheme.LineHeight(0.72f) + 8;
		foreach (Errand e in _mine)
		{
			y = DrawErrand(b, service, e, x, y, w, hits) + 14;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, feed, y);
	}

	private static (string Text, Color Fill, Color Ink) StateChip(Errand e, bool asking) => e.State switch
	{
		ErrandStates.Waiting => (asking ? Tr.Get("errand.state.asking", "正在回话") : Tr.Get("errand.state.waiting", "等回话"), AisvTheme.Divider, AisvTheme.TextMuted),
		ErrandStates.Countered => (Tr.Get("errand.state.countered", "还价了"), AisvTheme.GoldSoft, AisvTheme.Gold),
		ErrandStates.Active => (Tr.Get("errand.state.active", "办理中"), AisvTheme.AccentSoft, AisvTheme.AccentDark),
		ErrandStates.Done => (Tr.Get("errand.state.done", "办成了"), AisvTheme.Accent, Color.White),
		ErrandStates.Failed => (Tr.Get("errand.state.failed", "没办成"), UrgentSoft, Urgent),
		ErrandStates.Declined => (Tr.Get("errand.state.declined", "推掉了"), AisvTheme.Divider, AisvTheme.TextMuted),
		_ => (Tr.Get("errand.state.withdrawn", "撤回了"), AisvTheme.Divider, AisvTheme.TextFaint)
	};

	private int DrawErrand(SpriteBatch b, ErrandService service, Errand e, int x, int y, int w, Hits hits)
	{
		bool open = ErrandStates.IsOpen(e.State);
		int tx = x + 24 + 56 + 16, textW = x + w - 24 - 48 - 12 - tx;
		List<string> quote = e.Reply.Length > 0 ? AisvTheme.WrapLines("“" + ShellText.Clean(e.Reply) + "”", textW, 0.8f) : new List<string>();
		List<string> note = e.Note.Length > 0 ? AisvTheme.WrapLines(Tr.Get("commissionsPage.myNote", "我的留言：{{note}}", ("note", ShellText.Clean(e.Note))), textW, 0.7f) : new List<string>();
		int lh = AisvTheme.LineHeight(0.8f), nh = AisvTheme.LineHeight(0.7f);
		bool progress = e.State == ErrandStates.Active;
		bool buttons = open && service.CanPost;
		string money = e.Goods > 0
			? Tr.Get("commissionsPage.errandMoneyGoods", "报酬 {{reward}}g · 货款 {{goods}}g", ("reward", e.Reward), ("goods", e.Goods))
			: Tr.Get("commissionsPage.errandMoney", "报酬 {{reward}}g", ("reward", e.Reward));
		int chipsW = (int)AisvTheme.Width(money, 0.66f) + 20 + (open ? 8 + (int)AisvTheme.Width(Deadline(ErrandService.DaysLeft(e)), 0.66f) + 20 : 0);
		int buttonsW = e.State == ErrandStates.Countered ? 290 : 120;
		bool stacked = buttons && tx + chipsW + 12 > x + w - 24 - buttonsW;
		int h = 20 + 34 + 34 + quote.Count * lh + note.Count * nh + (progress ? 28 : 0) + 10 + (buttons ? 44 : 30) + (stacked ? 44 : 0) + 20;
		Rectangle r = new Rectangle(x, y, w, h);
		if (r.Bottom < _view.Y || r.Y > _view.Bottom)
		{
			return r.Bottom;
		}
		AisvTheme.DrawCard(b, r, highlight: e.State == ErrandStates.Countered, fill: open ? null : AisvTheme.Page);
		if (e.State == ErrandStates.Countered)
		{
			AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 16, 5, r.Height - 32), AisvTheme.Gold, 2);
		}
		Kit.Avatar(b, new Rectangle(r.X + 24, r.Y + 22, 56, 56), Ctx, e.Npc);
		string who = service.Display(e.Npc);
		AisvTheme.Bold(b, who, new Vector2(tx, r.Y + 20), open ? AisvTheme.Text : AisvTheme.TextMuted, 0.9f);
		(string chip, Color fill, Color ink) = StateChip(e, service.IsAsking(e.Id));
		AisvTheme.Chip(b, new Vector2(tx + AisvTheme.Width(who, 0.9f) + 12, r.Y + 22), chip, fill, ink, 0.62f);
		ParsedItemData? data = ItemRegistry.GetData(e.ItemId);
		if (data != null && !data.IsErrorItem)
		{
			Rectangle icon = new Rectangle(r.Right - 24 - 48, r.Y + 20, 48, 48);
			AisvTheme.FillRounded(b, icon, AisvTheme.PageDeep, 8);
			b.Draw(data.GetTexture(), AisvTheme.Inflate(icon, -6), data.GetSourceRect(), Color.White * (open ? 1f : 0.6f));
		}
		int ty = r.Y + 20 + 34;
		AisvTheme.Bold(b, AisvTheme.Truncate(service.What(e), textW, 0.95f), new Vector2(tx, ty), open ? Orange : AisvTheme.TextMuted, 0.95f);
		ty += 34;
		foreach (string line in quote)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.Text, 0.8f);
			ty += lh;
		}
		foreach (string line in note)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.TextFaint, 0.7f);
			ty += nh;
		}
		if (progress)
		{
			ty += 8;
			int pw = Math.Min(textW, 360);
			AisvTheme.Progress(b, new Rectangle(tx, ty, pw, 10), (float)ErrandService.Progress(e), AisvTheme.Accent);
			string chance = ErrandRules.ChanceLabel(e.Chance);
			AisvTheme.DrawText(b, chance, new Vector2(tx + pw + 12, ty - 6), AisvTheme.TextMuted, 0.62f);
			ty += 20;
		}
		ty += 10;
		int cx = tx;
		Rectangle mc = AisvTheme.Chip(b, new Vector2(cx, ty + 8), money, AisvTheme.GoldSoft, AisvTheme.Gold, 0.66f);
		cx = mc.Right + 8;
		if (open)
		{
			int left = ErrandService.DaysLeft(e);
			AisvTheme.Chip(b, new Vector2(cx, ty + 8), Deadline(left), left <= 1 ? UrgentSoft : AisvTheme.Divider, left <= 1 ? Urgent : AisvTheme.TextMuted, 0.66f);
		}
		if (!buttons)
		{
			return r.Bottom;
		}
		int bx = r.Right - 24;
		if (stacked)
		{
			ty += 44;
		}
		if (e.State == ErrandStates.Countered)
		{
			Rectangle no = new Rectangle(bx - 110, ty, 110, 44);
			Rectangle yes = new Rectangle(no.X - 10 - 170, ty, 170, 44);
			AisvTheme.Button(b, yes, Tr.Get("commissionsPage.agree", "同意 {{gold}}g", ("gold", e.Counter)), primary: true, hover: AisvTheme.Hover(yes, _view), scale: 0.8f);
			AisvTheme.Button(b, no, Tr.Get("commissionsPage.never", "算了"), hover: AisvTheme.Hover(no, _view), scale: 0.8f);
			hits.Add(yes, () => Act(service.AcceptCounter(e.Id), "purchase"), _view);
			hits.Add(no, () => Act(service.Withdraw(e.Id), "trashcan"), _view);
		}
		else
		{
			bool confirm = _confirmWithdraw == e.Id;
			Rectangle withdraw = new Rectangle(bx - 120, ty, 120, 44);
			AisvTheme.Button(b, withdraw, confirm ? Tr.Get("commissionsPage.withdrawConfirm", "确定撤回") : Tr.Get("commissionsPage.withdraw", "撤回"), primary: confirm, hover: AisvTheme.Hover(withdraw, _view), scale: 0.8f);
			string tip = e.State == ErrandStates.Active
				? Tr.Get("commissionsPage.withdrawActive", "已经开工，撤回只退一半报酬，交情也会差一点")
				: Tr.Get("commissionsPage.withdrawWaiting", "还没回话，撤回全额退款");
			if (confirm || AisvTheme.Hover(withdraw, _view))
			{
				float tw = AisvTheme.Width(tip, 0.62f);
				Vector2 at = withdraw.X - 12 - tw >= tx ? new Vector2(withdraw.X - 12 - tw, withdraw.Y + 14) : new Vector2(withdraw.Right - tw, withdraw.Y - 22);
				AisvTheme.DrawText(b, tip, at, AisvTheme.Warn, 0.62f);
			}
			string id = e.Id;
			bool started = e.State == ErrandStates.Active;
			hits.Add(withdraw, () =>
			{
				if (started && _confirmWithdraw != id)
				{
					Game1.playSound("smallSelect");
					_confirmWithdraw = id;
					return;
				}
				_confirmWithdraw = null;
				Act(service.Withdraw(id), "trashcan");
			}, _view);
		}
		return r.Bottom;
	}

	private void Act(string? problem, string sound)
	{
		Game1.playSound(problem == null ? sound : "cancel");
		if (problem != null)
		{
			Host.Notice(problem);
		}
		Reload();
	}

	/// <summary>"支持作者"卡片：粉色边，酬劳是后台指定的物品或随机一件稀有物品，右下"去支持"。</summary>
	private int DrawSupport(SpriteBatch b, SupportTask t, int x, int y, int w, Hits hits)
	{
		int tx = x + 24 + 56 + 16, textW = x + w - 24 - tx;
		List<string> body = AisvTheme.WrapLines(ShellText.Clean(t.Text), textW, 0.8f);
		int lh = AisvTheme.LineHeight(0.8f);
		Rectangle r = new Rectangle(x, y, w, 20 + 34 + 34 + body.Count * lh + 10 + 44 + 20);
		if (r.Bottom < _view.Y || r.Y > _view.Bottom)
		{
			return r.Bottom;
		}
		AisvTheme.Bordered(b, r, AisvTheme.PinkSoft, AisvTheme.Pink);
		AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 16, 5, r.Height - 32), AisvTheme.Pink, 2);
		AisvTheme.Glyph(b, new Rectangle(r.X + 24, r.Y + 22, 56, 56), AisvTheme.Pink, "牛");
		string who = ShellText.Clean(t.Poster.Length > 0 ? t.Poster : Tr.Get("commissionsPage.supportPoster", "远古牛哥（本 mod 作者）"));
		AisvTheme.Bold(b, who, new Vector2(tx, r.Y + 20), AisvTheme.Text, 0.9f);
		AisvTheme.Chip(b, new Vector2(tx + AisvTheme.Width(who, 0.9f) + 12, r.Y + 22), Tr.Get("commissionsPage.src.support", "支持作者"), Color.White, AisvTheme.Pink, 0.62f);
		int right = r.Right - 24;
		(string Id, int Qty)? reward = PromoRules.FixedReward(t, id => ItemRegistry.GetData(id) is { IsErrorItem: false });
		ParsedItemData? data = reward.HasValue ? ItemRegistry.GetData(reward.Value.Id) : null;
		Rectangle icon = new Rectangle(right - 48, r.Y + 20, 48, 48);
		if (data != null)
		{
			AisvTheme.FillRounded(b, icon, Color.White, 8);
			b.Draw(data.GetTexture(), AisvTheme.Inflate(icon, -6), data.GetSourceRect(), Color.White);
		}
		else
		{
			AisvTheme.Glyph(b, icon, AisvTheme.Gold, "稀");
		}
		right = icon.X - 12;
		int ty = r.Y + 20 + 34;
		string title = t.Title.Length > 0 ? ShellText.Clean(t.Title) : Tr.Get("commissionsPage.supportTitle", "支持一下远古牛哥");
		AisvTheme.Bold(b, AisvTheme.Truncate(title, right - tx, 0.95f), new Vector2(tx, ty), AisvTheme.Pink, 0.95f);
		ty += 34;
		foreach (string line in body)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.Text, 0.8f);
			ty += lh;
		}
		ty += 10;
		const int btnW = 140;
		string rewardText = data != null
			? Tr.Get("commissionsPage.supportReward", "酬劳 {{item}}", ("item", data.DisplayName + (reward!.Value.Qty > 1 ? " ×" + reward.Value.Qty : "")))
			: Tr.Get("commissionsPage.supportRewardRandom", "酬劳 随机一件稀有物品");
		AisvTheme.Chip(b, new Vector2(tx, ty + 8), AisvTheme.Truncate(rewardText, Math.Max(60, r.Right - 24 - btnW - 12 - tx), 0.66f), AisvTheme.GoldSoft, AisvTheme.Gold, 0.66f);
		Rectangle btn = new Rectangle(r.Right - 24 - btnW, ty, btnW, 44);
		AisvTheme.Button(b, btn, Tr.Get("commissionsPage.supportGo", "去支持"), primary: true, hover: AisvTheme.Hover(btn, _view));
		if (_confirm == null)
		{
			hits.Add(btn, () =>
			{
				Game1.playSound("bigSelect");
				_confirm = t;
			}, _view);
		}
		return r.Bottom;
	}
}
