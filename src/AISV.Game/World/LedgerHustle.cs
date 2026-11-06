using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Dialogue;
using AISV.Core.Social.Web;
using AISV.Core.Text;
using AISV.Core.World.Ledger;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.World;

/// <summary>手头紧时想办法、分类广告、村民之间借钱、家当的进出。</summary>
public sealed partial class LedgerService
{
	private static readonly IReadOnlyDictionary<string, string[]> Forage = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["spring"] = new[] { "(O)16", "(O)18", "(O)20", "(O)22", "(O)399" },
		["summer"] = new[] { "(O)396", "(O)398", "(O)402", "(O)259" },
		["fall"] = new[] { "(O)404", "(O)406", "(O)408", "(O)410", "(O)281" },
		["winter"] = new[] { "(O)412", "(O)414", "(O)416", "(O)418", "(O)283" }
	};

	/// <summary>两人有多亲（0～1），用来挑借钱给谁。</summary>
	public Func<string, string, float>? Closeness { get; set; }

	/// <summary>记进村民之间：甲、乙、类型、语气、一句话、值不值得传。</summary>
	public Action<string, string, string, string, string, bool>? Moment { get; set; }

	public IReadOnlyList<Ad> Ads() => Book?.Ads.ToList() ?? new List<Ad>();

	public IReadOnlyList<NpcDebt> NpcDebts() => Book?.NpcDebts.ToList() ?? new List<NpcDebt>();

	/// <summary>最近在想办法挣钱的人（新的在前）。</summary>
	public IReadOnlyList<NpcWallet> Hustlers() => Wallets().Where(w => w.Hustle != null && Today - w.Hustle.Day <= 3).OrderByDescending(w => w.Hustle!.Day).ToList();

	private static int SellPrice(string id, int quality)
	{
		try
		{
			return ItemRegistry.Create(id, 1, quality, allowNull: true)?.sellToStorePrice() ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	// ---------------- 早上 ----------------

	private void MorningExtras(LedgerBook book, List<NPC> villagers)
	{
		NpcDebtsMorning(book);
		if (Game1.Date.DayOfWeek == DayOfWeek.Sunday)
		{
			Consume(villagers);
		}
		bool festival = Utility.isFestivalDay(Game1.dayOfMonth, Game1.season);
		foreach (NPC n in villagers.OrderBy(_ => _random.Next()))
		{
			PlanHustle(n, festival);
		}
		DivorceAds(book);
		ClassifiedRules.Trim(book.Ads, Today);
		WriteAds(book);
	}

	private void PlanHustle(NPC n, bool festival)
	{
		Occupation job = Job(n.Name);
		if (job.Child || Wallet(n.Name) is not NpcWallet w)
		{
			return;
		}
		HustleOptions options = new HustleOptions(Lender(n.Name), Employer(n.Name), Valuable(w), SaloonOpen: true, festival);
		if (HustleRules.Choose(n.Name, w, job, options, Today, _random) is HustleChoice c)
		{
			StartHustle(n.Name, w, c);
		}
	}

	private void StartHustle(string npc, NpcWallet w, HustleChoice c)
	{
		string item = c.ItemId.Length > 0 ? ItemName(c.ItemId) : "";
		w.Hustle = new Hustle { Day = Today, Kind = c.Kind, Time = c.Time, Other = c.Other, ItemId = c.ItemId, ItemQuality = c.ItemQuality, Plan = HustleRules.Doing(npc, c.Kind, c.Other.Length > 0 ? Display(c.Other) : "", item) };
		_log.Info($"账本：{npc} 手头紧（身上 {w.Gold}g，存款 {w.Savings}g），今天{w.Hustle.Plan}。");
		switch (c.Kind)
		{
			case HustleKinds.Tighten:
				w.TightUntil = Today + HustleRules.TightDays;
				FinishHustle(npc, w, 0, Tr.Get("hustle.note.tighten", "开始省钱，这一周不去酒吧、不乱买东西"));
				break;
			case HustleKinds.SellOwned:
				int price = ClassifiedRules.AskingPrice(SellPrice(c.ItemId, c.ItemQuality));
				PostAd(npc, AdKinds.Sell, item, c.ItemId, c.ItemQuality, price, ClassifiedRules.Days, -1, "hustle", Tr.Get("hustle.why.sell", "手头紧，只好卖家当"));
				break;
			case HustleKinds.Busk:
				PostAd(npc, AdKinds.Show, Tr.Get("ads.thing.show", "今晚的弹唱"), "", 0, 0, 0, -1, "hustle", Tr.Get("hustle.why.busk", "手头紧，去酒吧卖唱挣小费"));
				break;
			case HustleKinds.OddJob:
				PostAd(npc, AdKinds.Work, Tr.Get("ads.thing.work", "修修补补、搬搬抬抬"), "", 0, 0, 2, -1, "hustle", Tr.Get("hustle.why.work", "手头紧，想多接点活"));
				break;
		}
		Changed();
	}

	/// <summary>肯借钱的人：身上有 400g 以上、和他最亲的那位（至少要有点交情）。</summary>
	private string? Lender(string npc)
	{
		if (Closeness == null || Book is not LedgerBook book || book.NpcDebts.Any(d => d.State == NpcDebtStates.Open && string.Equals(d.Borrower, npc, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		return book.Wallets.Values
			.Where(o => !string.Equals(o.Npc, npc, StringComparison.OrdinalIgnoreCase) && o.Gold >= 400 && !Job(o.Npc).Child)
			.Select(o => (o.Npc, Close: Closeness(npc, o.Npc)))
			.Where(x => x.Close >= 0.3f)
			.OrderByDescending(x => x.Close).ThenBy(_ => _random.Next())
			.Select(x => x.Npc).FirstOrDefault();
	}

	private string? Employer(string npc) => LedgerRules.ShopTraffic.Keys
		.Where(o => !string.Equals(o, npc, StringComparison.OrdinalIgnoreCase) && Wallet(o) is NpcWallet w && w.Gold >= 300)
		.OrderBy(_ => _random.Next()).FirstOrDefault();

	private static (string, int, int)? Valuable(NpcWallet w)
	{
		var best = w.Home.Select(i => (i.Id, i.Quality, Price: SellPrice(i.Id, i.Quality))).OrderByDescending(x => x.Price).FirstOrDefault();
		return best.Id == null || best.Price < HustleRules.MinValuable ? null : best;
	}

	// ---------------- 到点结算 ----------------

	/// <summary>睡觉前：今天定下、还没到点的办法也算做完了（玩家早睡时不会落下）。</summary>
	public void OnDayEnding()
	{
		if (!Host || Book is not LedgerBook book)
		{
			return;
		}
		try
		{
			foreach (NpcWallet w in book.Wallets.Values.Where(w => w.Hustle is { Done: false } h && h.Day == Today).ToList())
			{
				ResolveHustle(w.Npc, w, w.Hustle!);
			}
			AdsTick();
		}
		catch (Exception ex)
		{
			_log.Warn("账本：晚上结算出错：" + ex.Message);
		}
	}

	private void HustleTick(NPC n, NpcWallet w, int time)
	{
		if (w.Hustle is not Hustle h || h.Day != Today)
		{
			return;
		}
		if (!h.Done && time >= h.Time)
		{
			if (h.Kind == HustleKinds.SellOwned && time < 1800)
			{
				return;
			}
			ResolveHustle(n.Name, w, h);
		}
		bool showing = h.Kind == HustleKinds.Busk ? time >= 1900 && time < 2400 && n.currentLocation?.Name == "Saloon" : !h.Done;
		if (showing && _config().ShoppingRemarks && Game1.player.currentLocation == n.currentLocation && time - h.BubbleTime >= 100
			&& Vector(n) is var d && d.LengthSquared() < 12 * 12 * 64 * 64)
		{
			h.BubbleTime = time;
			NpcSpeech.Say(n, HustleRules.Bubble(h.Kind));
		}
	}

	private static Microsoft.Xna.Framework.Vector2 Vector(NPC n) => n.Position - Game1.player.Position;

	private void ResolveHustle(string npc, NpcWallet w, Hustle h)
	{
		switch (h.Kind)
		{
			case HustleKinds.Forage:
			{
				string[] pool = Forage.TryGetValue(Game1.currentSeason ?? "spring", out string[]? p) ? p : Forage["spring"];
				List<string> found = Enumerable.Range(0, _random.Next(2, 5)).Select(_ => pool[_random.Next(pool.Length)]).ToList();
				LedgerRules.Put(w.Home, LedgerRules.MaxHome, found[0], 1, 0);
				int gold = found.Skip(1).Sum(id => SellPrice(id, 0));
				string names = string.Join("、", found.Distinct().Select(ItemName));
				FinishHustle(npc, w, gold, Tr.Get("hustle.note.forage", "挖到{{items}}，留了一样自己吃，剩下的卖了 {{gold}}g", ("items", names), ("gold", gold)));
				break;
			}
			case HustleKinds.Scavenge:
			case HustleKinds.Busk:
			case HustleKinds.Crafts:
			{
				int gold = HustleRules.Pay(h.Kind, _random);
				if (h.Kind == HustleKinds.Crafts && Wallet("Pierre") is NpcWallet pierre && !string.Equals(npc, "Pierre", StringComparison.OrdinalIgnoreCase))
				{
					gold = Math.Min(gold, Math.Max(20, pierre.Gold / 4));
					Spend(pierre, gold, EntryKinds.Shopping, Tr.Get("ledger.note.consign", "收下{{name}}寄卖的东西", ("name", Display(npc))));
				}
				if (h.Kind == HustleKinds.Busk)
				{
					CloseAds(npc, AdKinds.Show, "");
				}
				FinishHustle(npc, w, gold, Tr.Get("hustle.note.earned", "{{doing}}，挣了 {{gold}}g", ("doing", h.Plan), ("gold", gold)));
				break;
			}
			case HustleKinds.OddJob:
			{
				int gold = HustleRules.Pay(h.Kind, _random);
				if (h.Other.Length > 0 && Wallet(h.Other) is NpcWallet boss)
				{
					gold = Spend(boss, Math.Min(gold, boss.Gold / 3), EntryKinds.Hustle, Tr.Get("ledger.note.hire", "请{{name}}打零工", ("name", Display(npc))));
				}
				CloseAds(npc, AdKinds.Work, h.Other);
				FinishHustle(npc, w, gold, Tr.Get("hustle.note.earned", "{{doing}}，挣了 {{gold}}g", ("doing", h.Plan), ("gold", gold)));
				break;
			}
			case HustleKinds.SellOwned:
			{
				int price = SellPrice(h.ItemId, h.ItemQuality);
				string item = ItemName(h.ItemId);
				if (LedgerRules.Take(w.Home, h.ItemId, 1, h.ItemQuality) == 0)
				{
					FinishHustle(npc, w, 0, Tr.Get("hustle.note.sellGone", "想卖{{item}}，翻出来才发现已经没了", ("item", item)));
					break;
				}
				if (Wallet("Pierre") is NpcWallet buyer && !string.Equals(npc, "Pierre", StringComparison.OrdinalIgnoreCase))
				{
					price = Spend(buyer, price, EntryKinds.Shopping, Tr.Get("ledger.note.buyUsed", "收了{{name}}的{{item}}", ("name", Display(npc)), ("item", item)));
					LedgerRules.Put(buyer.Home, LedgerRules.MaxHome, h.ItemId, 1, h.ItemQuality);
				}
				CloseAds(npc, AdKinds.Sell, "Pierre", h.ItemId);
				FinishHustle(npc, w, price, Tr.Get("hustle.note.sold", "把家里的{{item}}卖给了{{buyer}}，得了 {{gold}}g", ("item", item), ("buyer", Display("Pierre")), ("gold", price)));
				break;
			}
			case HustleKinds.Borrow:
				Borrow(npc, w, h);
				break;
		}
		Changed();
	}

	private void FinishHustle(string npc, NpcWallet w, int gold, string note)
	{
		if (w.Hustle is not Hustle h)
		{
			return;
		}
		h.Done = true;
		h.Earned = gold;
		h.Note = note;
		if (gold > 0)
		{
			w.Gold = Math.Min(LedgerRules.MaxGold, w.Gold + gold);
		}
		if (h.Kind != HustleKinds.Borrow)
		{
			Log(w, EntryKinds.Hustle, gold, note);
		}
		string text = Tr.Get("hustle.event", "{{name}}手头紧，{{note}}。", ("name", Display(npc)), ("note", note));
		Event(npc, EntryKinds.Hustle, gold, text);
		if (HustleRules.Gossipy(h.Kind))
		{
			Gossip?.Invoke(text, new[] { npc });
		}
		_log.Info($"账本：{npc} 想办法挣钱：{note}。");
		Changed();
	}

	// ---------------- 村民之间借钱 ----------------

	private void Borrow(string npc, NpcWallet w, Hustle h)
	{
		if (Book is not LedgerBook book || Wallet(h.Other) is not NpcWallet lender)
		{
			FinishHustle(npc, w, 0, Tr.Get("hustle.note.borrowNone", "想找人借钱，没开得了口"));
			return;
		}
		int amount = HustleRules.LoanAmount(Job(npc), lender.Gold);
		if (amount < 50)
		{
			FinishHustle(npc, w, 0, Tr.Get("hustle.note.borrowNo", "找{{who}}借钱，对方手头也不宽裕", ("who", Display(h.Other))));
			return;
		}
		lender.Gold -= amount;
		NpcDebt d = new NpcDebt { Id = "n" + book.NextNpcDebt++, Lender = h.Other, Borrower = npc, Amount = amount, Day = Today, DueDay = Today + HustleRules.LoanDays };
		book.NpcDebts.Add(d);
		Log(lender, EntryKinds.NpcLoan, -amount, Tr.Get("ledger.note.lendTo", "借给{{name}}", ("name", Display(npc))));
		Log(w, EntryKinds.NpcLoan, amount, Tr.Get("ledger.note.borrowFrom", "向{{name}}借的，{{days}} 天内还", ("name", Display(h.Other)), ("days", HustleRules.LoanDays)));
		FinishHustle(npc, w, amount, Tr.Get("hustle.note.borrowed", "向{{who}}借了 {{gold}}g", ("who", Display(h.Other)), ("gold", amount)));
		Moment?.Invoke(h.Other, npc, "money_help", "sweet", Tr.Get("hustle.moment.lend", "{{b}}手头紧，{{a}}借了{{b}} {{gold}}g，说好一周内还。", ("a", Display(h.Other)), ("b", Display(npc)), ("gold", amount)), false);
	}

	private void NpcDebtsMorning(LedgerBook book)
	{
		foreach (NpcDebt d in book.NpcDebts.Where(d => d.State == NpcDebtStates.Open).ToList())
		{
			if (Wallet(d.Borrower) is not NpcWallet borrower || Wallet(d.Lender) is not NpcWallet lender)
			{
				continue;
			}
			string a = Display(d.Lender), b = Display(d.Borrower);
			if (HustleRules.CanRepay(borrower, Job(d.Borrower), d.Amount))
			{
				borrower.Gold -= d.Amount;
				lender.Gold = Math.Min(LedgerRules.MaxGold, lender.Gold + d.Amount);
				Close(d, NpcDebtStates.Paid);
				Log(borrower, EntryKinds.NpcLoan, -d.Amount, Tr.Get("ledger.note.repayTo", "还了{{name}}的钱", ("name", a)));
				Log(lender, EntryKinds.NpcLoan, d.Amount, Tr.Get("ledger.note.repaidBy", "{{name}}还的钱", ("name", b)));
				Moment?.Invoke(d.Lender, d.Borrower, "money_repaid", "sweet", Tr.Get("hustle.moment.repaid", "{{b}}把借{{a}}的 {{gold}}g 还上了，{{a}}挺高兴。", ("a", a), ("b", b), ("gold", d.Amount)), false);
				continue;
			}
			string style = LedgerRules.StyleOf(d.Lender, Field(d.Lender, "AIStardewValley/DebtStyle"));
			switch (HustleRules.Overdue(d, Today, style))
			{
				case "strain":
					d.Strained = true;
					string strain = style == DebtStyles.Demand
						? Tr.Get("hustle.moment.demand", "{{b}}借{{a}}的钱过期了还没还，{{a}}当面催了几句，两人都不太痛快。", ("a", a), ("b", b))
						: Tr.Get("hustle.moment.strain", "{{b}}借{{a}}的钱过期了还没还，{{a}}没好意思开口，心里有点疙瘩。", ("a", a), ("b", b));
					Moment?.Invoke(d.Lender, d.Borrower, "money_strain", "tense", strain, style == DebtStyles.Demand);
					Event(d.Borrower, EntryKinds.NpcLoan, d.Amount, strain);
					break;
				case NpcDebtStates.Forgiven:
					Close(d, NpcDebtStates.Forgiven);
					Log(lender, EntryKinds.NpcLoan, 0, Tr.Get("ledger.note.forgive", "借给{{name}}的钱不要了", ("name", b)));
					Moment?.Invoke(d.Lender, d.Borrower, "money_forgiven", "sweet", Tr.Get("hustle.moment.forgive", "{{a}}跟{{b}}说，借的那点钱不用还了。", ("a", a), ("b", b)), true);
					break;
				case NpcDebtStates.Collected:
					int got = Math.Min(borrower.Gold, d.Amount);
					borrower.Gold -= got;
					lender.Gold += got;
					Close(d, NpcDebtStates.Collected);
					Log(borrower, EntryKinds.NpcLoan, -got, Tr.Get("ledger.note.collectedBy", "被{{name}}要走了欠的钱", ("name", a)));
					Log(lender, EntryKinds.NpcLoan, got, Tr.Get("ledger.note.collectFrom", "从{{name}}那儿要回了钱", ("name", b)));
					Moment?.Invoke(d.Lender, d.Borrower, "money_collected", "tense", Tr.Get("hustle.moment.collect", "{{a}}找上门，把{{b}}欠的钱要走了 {{gold}}g。", ("a", a), ("b", b), ("gold", got)), true);
					break;
			}
		}
		book.NpcDebts.RemoveAll(d => d.State != NpcDebtStates.Open && Today - d.ClosedDay > 28);
	}

	private void Close(NpcDebt d, string state)
	{
		d.State = state;
		d.ClosedDay = Today;
		_log.Info($"账本：{d.Borrower} 欠 {d.Lender} 的 {d.Amount}g：{state}。");
	}

	/// <summary>两人之间钱上的事。</summary>
	public string MoneyBetween(string a, string b)
	{
		if (!Enabled || Book is not LedgerBook book)
		{
			return "";
		}
		List<string> parts = new List<string>();
		foreach (NpcDebt d in book.NpcDebts.Where(d => d.State == NpcDebtStates.Open && Pair(d, a, b)))
		{
			parts.Add(Today > d.DueDay
				? Tr.Get("hustle.between.overdue", "{{b}}借了{{a}} {{gold}}g，过期了还没还", ("a", Display(d.Lender)), ("b", Display(d.Borrower)), ("gold", d.Amount))
				: Tr.Get("hustle.between.owe", "{{b}}借了{{a}} {{gold}}g", ("a", Display(d.Lender)), ("b", Display(d.Borrower)), ("gold", d.Amount)));
		}
		foreach (string who in new[] { a, b })
		{
			if (book.Wallets.TryGetValue(who, out NpcWallet? w) && w.Hustle is Hustle h && Today - h.Day <= 3)
			{
				parts.Add(Tr.Get("hustle.between.broke", "{{name}}最近手头紧（{{plan}}）", ("name", Display(who)), ("plan", h.Plan)));
			}
		}
		return string.Join("；", parts);
	}

	private static bool Pair(NpcDebt d, string a, string b) =>
		(string.Equals(d.Lender, a, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Borrower, b, StringComparison.OrdinalIgnoreCase))
		|| (string.Equals(d.Lender, b, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Borrower, a, StringComparison.OrdinalIgnoreCase));

	/// <summary>给小剧场检测的：最近两周镇上的钱事、过期的村民债、手头紧的人。</summary>
	public string ArcMaterial()
	{
		if (!Enabled || Book is not LedgerBook book)
		{
			return "";
		}
		List<string> lines = book.Events.Where(e => Today - e.Day <= 14).Take(8).Select(e => "- " + e.Text).ToList();
		lines.AddRange(book.NpcDebts.Where(d => d.State == NpcDebtStates.Open && Today > d.DueDay)
			.Select(d => "- " + Tr.Get("hustle.between.overdue", "{{b}}借了{{a}} {{gold}}g，过期了还没还", ("a", Display(d.Lender)), ("b", Display(d.Borrower)), ("gold", d.Amount))));
		return lines.Count == 0 ? "" : "镇上的钱事：\n" + string.Join("\n", lines.Distinct());
	}

	// ---------------- 家当的进出 ----------------

	/// <summary>每周日吃掉家里一点能吃的东西。</summary>
	private void Consume(List<NPC> villagers)
	{
		foreach (NPC n in villagers)
		{
			if (Wallet(n.Name) is not NpcWallet w)
			{
				continue;
			}
			List<LedgerItem> food = w.Home.Concat(w.Carried).Where(i => ItemRegistry.Create(i.Id, 1, 0, allowNull: true) is StardewValley.Object o && o.Edibility > 0).ToList();
			foreach (LedgerItem i in food.OrderBy(_ => _random.Next()).Take(_random.Next(1, 4)))
			{
				if (LedgerRules.Take(w.Home, i.Id, 1) == 0)
				{
					LedgerRules.Take(w.Carried, i.Id, 1);
				}
			}
		}
		Changed();
	}

	/// <summary>家当里能拿来送礼的东西（随身的在前）。</summary>
	public IReadOnlyList<string> Owned(string npc) => Enabled && Book is LedgerBook book && book.Wallets.TryGetValue(npc, out NpcWallet? w)
		? w.Carried.Concat(w.Home).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
		: Array.Empty<string>();

	/// <summary>送出去一件：先拿随身的，再拿家里的。</summary>
	public void GaveAway(string npc, string id, string to)
	{
		if (!Host || Wallet(npc) is not NpcWallet w)
		{
			return;
		}
		if (LedgerRules.Take(w.Carried, id, 1) == 0 && LedgerRules.Take(w.Home, id, 1) == 0)
		{
			return;
		}
		Log(w, EntryKinds.Gift, 0, Tr.Get("ledger.note.gave", "把{{item}}送给了{{name}}", ("item", ItemName(id)), ("name", to)));
		Changed();
	}

	/// <summary>村民之间暖心的小事：四成机会把家里一件对方喜欢的东西送过去。</summary>
	public void OnMoment(NpcSocialMoment m)
	{
		if (!Host || m.Theme is not ("small_kindness" or "quiet_bond") || _random.NextDouble() >= 0.4 || Wallet(m.First) is not NpcWallet giver || Wallet(m.Second) is not NpcWallet taker)
		{
			return;
		}
		HashSet<string> likes = new HashSet<string>(Safe(() => _eco.PersonalTastes(m.Second, "loved")).Concat(Safe(() => _eco.PersonalTastes(m.Second, "liked"))).Select(t => t.QualifiedId), StringComparer.OrdinalIgnoreCase);
		LedgerItem? pick = giver.Home.Concat(giver.Carried).FirstOrDefault(i => likes.Contains(i.Id) && !Job(m.First).Items.Contains(i.Id));
		if (pick == null)
		{
			return;
		}
		string id = pick.Id;
		int quality = pick.Quality;
		bool fromHome = LedgerRules.Take(giver.Home, id, 1, quality) > 0;
		if (!fromHome && LedgerRules.Take(giver.Carried, id, 1, quality) == 0)
		{
			return;
		}
		if (!LedgerRules.Put(taker.Home, LedgerRules.MaxHome, id, 1, quality))
		{
			LedgerRules.Put(fromHome ? giver.Home : giver.Carried, fromHome ? LedgerRules.MaxHome : LedgerRules.MaxCarried, id, 1, quality);
			return;
		}
		Log(giver, EntryKinds.Gift, 0, Tr.Get("ledger.note.gave", "把{{item}}送给了{{name}}", ("item", ItemName(id)), ("name", Display(m.Second))));
		Log(taker, EntryKinds.Gift, 0, Tr.Get("ledger.note.got", "{{name}}送了{{item}}", ("item", ItemName(id)), ("name", Display(m.First))));
		_log.Info($"账本：{m.First} 把 {id} 送给了 {m.Second}。");
		Changed();
	}

	// ---------------- 分类广告 ----------------

	private Ad? PostAd(string npc, string kind, string thing, string itemId, int quality, int price, int days, int sellDay, string source, string why)
	{
		if (Book is not LedgerBook book || book.Ads.Any(a => a.Source == source && (source != "hustle" || (a.Npc == npc && a.Day == Today))))
		{
			return null;
		}
		Ad ad = new Ad { Id = "a" + book.NextAd++, Npc = npc, Kind = kind, Thing = thing, ItemId = itemId, Quality = quality, Price = price, Day = Today, ExpiresDay = Today + days, SellDay = sellDay, Source = source };
		ad.Text = ClassifiedRules.Plain(ad, Display(npc));
		book.Ads.Insert(0, ad);
		_why[ad.Id] = why;
		_log.Info($"分类广告：{npc} 登了 {ClassifiedRules.Title(ad)}。");
		Changed();
		return ad;
	}

	private readonly Dictionary<string, string> _why = new Dictionary<string, string>();

	private void CloseAds(string npc, string kind, string buyer, string itemId = "")
	{
		foreach (Ad a in Book?.Ads.Where(a => a.Npc == npc && a.Kind == kind && ClassifiedRules.Open(a, Today) && (itemId.Length == 0 || a.ItemId == itemId)) ?? Enumerable.Empty<Ad>())
		{
			a.SoldDay = Today;
			a.Buyer = buyer;
		}
	}

	/// <summary>离婚以后，对方登一条卖掉婚礼那身衣服的广告，过几天有人买走。</summary>
	private void DivorceAds(LedgerBook book)
	{
		foreach ((string npc, Friendship f) in Game1.player.friendshipData.Pairs)
		{
			if (!f.IsDivorced() || book.DivorceAdsPosted.Contains(npc) || Game1.getCharacterFromName(npc) is not NPC n)
			{
				continue;
			}
			string thing = n.Gender == Gender.Male ? Tr.Get("ads.thing.suit", "结婚礼服") : Tr.Get("ads.thing.dress", "婚纱");
			int sell = Today + _random.Next(5, 11);
			book.DivorceAdsPosted.Add(npc);
			PostAd(npc, AdKinds.Sell, thing, "", 0, 0, sell - Today + 1, sell, "divorce:" + npc, Tr.Get("ads.why.divorce", "刚和农夫离了婚，那身婚礼上穿的衣服留着也没用了"));
		}
	}

	private void AdsTick()
	{
		foreach (Ad a in Book?.Ads.Where(a => a.SellDay == Today && a.SoldDay < 0) ?? Enumerable.Empty<Ad>())
		{
			a.SoldDay = Today;
			a.Buyer = "";
			_log.Info($"分类广告：{a.Npc} 的 {a.Thing} 有人买走了。");
			Changed();
		}
	}

	/// <summary>农夫在分类广告上买下一件东西；返回问题（买成时为 null）。</summary>
	public string? BuyAd(string id)
	{
		if (!Host || Book?.Ads.FirstOrDefault(a => a.Id == id) is not Ad ad || Wallet(ad.Npc) is not NpcWallet w)
		{
			return Tr.Get("ads.buy.gone", "这条广告已经撤了");
		}
		if (!ClassifiedRules.Open(ad, Today) || ad.Kind != AdKinds.Sell || ad.ItemId.Length == 0 || ad.Price <= 0)
		{
			return Tr.Get("ads.buy.closed", "已经卖掉了");
		}
		if (Game1.player.Money < ad.Price)
		{
			return Tr.Get("ads.buy.money", "钱不够");
		}
		Item? item = ItemRegistry.Create(ad.ItemId, 1, ad.Quality, allowNull: true);
		if (item == null || !Game1.player.couldInventoryAcceptThisItem(item))
		{
			return Tr.Get("ads.buy.full", "背包满了");
		}
		if (LedgerRules.Take(w.Home, ad.ItemId, 1, ad.Quality) == 0)
		{
			ad.SoldDay = Today;
			return Tr.Get("ads.buy.gone2", "他家里已经没有这件了");
		}
		Game1.player.Money -= ad.Price;
		Game1.player.addItemToInventory(item);
		ad.SoldDay = Today;
		ad.Buyer = ClassifiedRules.Farmer;
		string item0 = ItemName(ad.ItemId);
		if (w.Hustle is Hustle h && h.Day == Today && h.Kind == HustleKinds.SellOwned && !h.Done)
		{
			FinishHustle(ad.Npc, w, ad.Price, Tr.Get("hustle.note.soldFarmer", "把家里的{{item}}卖给了农夫，得了 {{gold}}g", ("item", item0), ("gold", ad.Price)));
		}
		else
		{
			w.Gold = Math.Min(LedgerRules.MaxGold, w.Gold + ad.Price);
			Log(w, EntryKinds.Ad, ad.Price, Tr.Get("ledger.note.adSold", "分类广告：{{item}}卖给了农夫", ("item", item0)));
		}
		Changed();
		return null;
	}

	/// <summary>新登的广告让 AI 用各自的口吻重写（一次一批）。</summary>
	private void WriteAds(LedgerBook book)
	{
		List<Ad> fresh = book.Ads.Where(a => !a.Written && a.Day >= Today - 1).Take(6).ToList();
		if (fresh.Count == 0 || Ai is not IAiSender ai || !_config().AiOn)
		{
			return;
		}
		foreach (Ad a in fresh)
		{
			a.Written = true;
		}
		var items = fresh.Select(a => (a, Display(a.Npc), _npcs.Persona(a.Npc, Game1.getCharacterFromName(a.Npc)).Personality, _why.TryGetValue(a.Id, out string? why) ? why : "")).ToList();
		string system = ClassifiedRules.System(Language?.Invoke() ?? "中文", items);
		int session = _session;
		Task.Run(async () =>
		{
			Dictionary<string, string> texts = new Dictionary<string, string>();
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(ClassifiedRules.Request, system, "写这几条广告。", 900, 0.9)).ConfigureAwait(false);
				texts = r.Ok ? ClassifiedRules.Parse(r.Text) : texts;
			}
			catch (Exception ex)
			{
				_log.Debug($"分类广告：AI 没写成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (session != _session || Book is not LedgerBook b)
				{
					return;
				}
				foreach (Ad a in b.Ads)
				{
					if (texts.TryGetValue(a.Id, out string? t))
					{
						a.Text = TextTools.CleanForGame(t);
					}
				}
				_log.Info($"分类广告：AI 写好了 {texts.Count} 条。");
				Changed();
			});
		});
	}

	/// <summary>扒窃丢了东西：失主登一条寻物启事。</summary>
	private void LostAd(string npc, string thing, string theftId)
	{
		if (PostAd(npc, AdKinds.Lost, thing, "", 0, 0, 5, -1, "theft:" + theftId, Tr.Get("ads.why.lost", "身上的东西不知道什么时候丢了，其实是被人偷了，但他自己不知道")) != null && Book is LedgerBook book)
		{
			WriteAds(book);
		}
	}

	/// <summary>拍片和调试：直接登一条，sellIn 天后有人买走（0 = 不安排）。</summary>
	private Ad? DebugAd(string npc, string kind, string thing, int price, int sellIn, string why)
	{
		Ad? ad = PostAd(npc, kind, thing, "", 0, price, sellIn > 0 ? sellIn + 1 : ClassifiedRules.Days, sellIn > 0 ? Today + sellIn : -1, "debug:" + Guid.NewGuid().ToString("N"), why);
		if (ad != null && Book is LedgerBook book)
		{
			WriteAds(book);
		}
		return ad;
	}
}
