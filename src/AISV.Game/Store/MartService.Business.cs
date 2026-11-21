using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Dialogue;
using AISV.Game.Npcs;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Inventories;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>经营：店型、行情、进货、库房、宣传、评价、店员合同；开店时摆好货架；卸 mod 前收拾。</summary>
public sealed partial class MartService
{
	private const int PosterSpriteId = 77_320_000;

	private int _megaphoneLeft;

	// ---------------- 开店 ----------------

	/// <summary>新店：两张白货架、一个冷柜、一个木柜、一个促销桶，再放一个库房箱子。</summary>
	private static void Furnish(GameLocation inside)
	{
		(string Id, int X, int Y)[] layout =
		{
			(MartContent.ShelfId, 10, 4),
			(MartContent.ShelfId, 13, 4),
			(MartContent.ColdId, 12, 8),
			(MartContent.CabinetId, 9, 8),
			(MartContent.BinId, 6, 8)
		};
		foreach ((string id, int x, int y) in layout)
		{
			if (!inside.furniture.Any(f => f.TileLocation == new Vector2(x, y)))
			{
				inside.furniture.Add(Furniture.GetFurnitureInstance(id, new Vector2(x, y)));
			}
		}
		EnsureStockroom(inside);
	}

	/// <summary>库房：一个绑在共享库存上的大箱子（进的货送到这里，卸掉 mod 也还能打开）。</summary>
	private static void EnsureStockroom(GameLocation inside)
	{
		if (inside.objects.Values.OfType<Chest>().Any(c => c.GlobalInventoryId == MartShelves.StockroomId))
		{
			return;
		}
		Vector2 at = MartInterior.Stockroom.ToVector2();
		if (inside.objects.ContainsKey(at))
		{
			return;
		}
		Chest chest = new Chest(true, at, "BigChest") { GlobalInventoryId = MartShelves.StockroomId };
		inside.objects[at] = chest;
	}

	// ---------------- 店型 ----------------

	public ShopType Type => ShopTypes.Get(State?.ShopType);

	public int Level => ShopTypes.Level(State?.Xp ?? 0);

	/// <summary>换店型（一周一次；第一次随时能换）。</summary>
	public bool SetShopType(string id)
	{
		if (State is not MartState s || s.ShopType == id || Today - s.ShopTypeDay < 7 && s.ShopTypeDay >= 0)
		{
			return false;
		}
		s.ShopType = ShopTypes.Get(id).Id;
		s.ShopTypeDay = Today;
		Dirty();
		return true;
	}

	public int DaysUntilTypeChange => State is MartState s && s.ShopTypeDay >= 0 ? Math.Max(0, 7 - (Today - s.ShopTypeDay)) : 0;

	public MarketDay TodayMarket => Market.For(Today, Game1.seasonIndex, Game1.isRaining);

	// ---------------- 进货 ----------------

	/// <summary>这位供货人这季能进什么：物品、名字、进价。</summary>
	public IReadOnlyList<(string ItemId, string Name, int Cost)> SupplierItems(string npc)
	{
		if (Suppliers.Get(npc) is not Supplier sup)
		{
			return Array.Empty<(string, string, int)>();
		}
		return sup.Items(Game1.seasonIndex).Select(id => ItemRegistry.Create(id, allowNull: true) as SObject).Where(o => o != null)
			.Select(o => (o!.QualifiedItemId, o.DisplayName, Suppliers.Cost(o.Price))).ToList();
	}

	/// <summary>下单：现在付钱，明早送到库房。</summary>
	public bool PlaceOrder(string npc, string itemId, int qty)
	{
		if (State is not MartState s || ItemRegistry.Create(itemId, allowNull: true) is not SObject o || qty <= 0)
		{
			return false;
		}
		int cost = Suppliers.Cost(o.Price) * qty;
		if (Game1.player.Money < cost)
		{
			return false;
		}
		Game1.player.Money -= cost;
		Order? same = s.Orders.FirstOrDefault(x => x.Supplier == npc && x.ItemId == itemId);
		if (same != null)
		{
			same.Qty += qty;
			same.Cost += cost;
		}
		else
		{
			s.Orders.Add(new Order { Supplier = npc, ItemId = itemId, Qty = qty, Cost = cost, Day = Today });
		}
		_ledger.Pay(npc, cost, Tr.Get("mart.orderNote", "{{farmer}}的小超市进货", ("farmer", Game1.player.Name)));
		Game1.playSound("purchase");
		Dirty();
		return true;
	}

	/// <summary>早上：昨天的进货送到库房。</summary>
	private void Deliver(MartState s)
	{
		List<Order> due = s.Orders.Where(o => o.Day < Today).ToList();
		if (due.Count == 0)
		{
			return;
		}
		Inventory room = MartShelves.Stockroom();
		foreach (Order o in due)
		{
			if (ItemRegistry.Create(o.ItemId, o.Qty, allowNull: true) is SObject item)
			{
				MartShelves.Tag(item, Origins.Supplier);
				room.Add(item);
			}
			s.Orders.Remove(o);
			s.Delivered.Add(o);
		}
		s.Delivered.RemoveAll(d => Today - d.Day > 14);
		Dirty();
		Hints.Hud(Tr.Get("mart.delivered", "进的货送到小超市的库房了（{{n}} 单）。", ("n", due.Count)), 7000);
	}

	/// <summary>打开库房箱子。</summary>
	public void OpenStockroom()
	{
		if (Shops().FirstOrDefault() is GameLocation shop && shop.objects.Values.OfType<Chest>().FirstOrDefault(c => c.GlobalInventoryId == MartShelves.StockroomId) is Chest chest)
		{
			chest.ShowMenu();
		}
	}

	/// <summary>理货员：9 点、12 点、15 点把过期的撤到库房，再把库房的货摆上合适的货架（该冷藏的进冷柜）。</summary>
	private void StockerRounds(int time)
	{
		if (time is not (900 or 1200 or 1500) || !OnDuty(Staff.Stocker))
		{
			return;
		}
		int moved = Restock();
		if (moved > 0 && Shops().FirstOrDefault() is GameLocation shop && Game1.currentLocation == shop)
		{
			MartShelves.RefreshDisplay(shop);
		}
	}

	/// <summary>撤下过期的、补上库房里的货，返回补了几件。</summary>
	public int Restock()
	{
		Inventory room = MartShelves.Stockroom();
		List<Furniture> shelves = AllShelves().Select(x => x.Shelf).ToList();
		foreach (Furniture f in shelves)
		{
			Inventory slots = MartShelves.Slots(f);
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] is SObject o && MartShelves.Stage(f, o) == Freshness.Expired)
				{
					slots[i] = null;
					room.Add(o);
				}
			}
		}
		int moved = 0;
		foreach (SObject item in room.OfType<SObject>().Where(o => MartShelves.Stage(shelves.FirstOrDefault()!, o) != Freshness.Expired || shelves.Count == 0).ToList())
		{
			string want = AISV.Core.Store.Goods.Prefers(MartShelves.Group(item));
			foreach (Furniture f in shelves.OrderByDescending(f => MartShelves.Kind(f)?.Temp == want))
			{
				int n = MartShelves.Add(f, item, item.Stack, Origins.Supplier);
				if (n > 0)
				{
					item.Stack -= n;
					moved += n;
				}
				if (item.Stack <= 0)
				{
					room.Remove(item);
					break;
				}
			}
		}
		room.RemoveEmptySlots();
		return moved;
	}

	// ---------------- 宣传 ----------------

	public IEnumerable<Promotion> ActivePromotions => State?.Promotions.Where(p => p.Until >= Today) ?? Enumerable.Empty<Promotion>();

	/// <summary>贴一张海报（一周，主推一类货）。</summary>
	public bool StartPoster(string group)
	{
		if (State is not MartState s || Game1.player.Money < Promotions.PosterCost || ActivePromotions.Any(p => p.Kind == Promotion.Poster && p.Target == group))
		{
			return false;
		}
		Game1.player.Money -= Promotions.PosterCost;
		s.Promotions.Add(new Promotion { Kind = Promotion.Poster, Target = group, From = Today, Until = Today + Promotions.PosterDays - 1 });
		Dirty();
		if (Shops().FirstOrDefault() is GameLocation shop && Game1.currentLocation == shop)
		{
			ShowPoster(shop);
		}
		return true;
	}

	/// <summary>橱窗主推一样货（换一样就把原来的撤下，不花钱）。</summary>
	public void SetWindow(string itemId)
	{
		if (State is not MartState s)
		{
			return;
		}
		s.Promotions.RemoveAll(p => p.Kind == Promotion.Window);
		if (itemId.Length > 0)
		{
			s.Promotions.Add(new Promotion { Kind = Promotion.Window, Target = itemId, From = Today, Until = Today + 365 });
		}
		Dirty();
	}

	public string? WindowItem => ActivePromotions.FirstOrDefault(p => p.Kind == Promotion.Window)?.Target;

	public bool MegaphoneUsed => State?.MegaphoneDay == Today;

	/// <summary>喇叭：一天一次，接下来一小时陆续来三位客人。</summary>
	public bool Megaphone()
	{
		if (State is not MartState s || s.MegaphoneDay == Today || Game1.player.Money < Promotions.MegaphoneCost || !IsOpen(Game1.timeOfDay))
		{
			return false;
		}
		Game1.player.Money -= Promotions.MegaphoneCost;
		s.MegaphoneDay = Today;
		_megaphoneLeft = Promotions.MegaphoneCustomers;
		Dirty();
		Game1.playSound("crowing");
		return true;
	}

	private void SpawnMegaphone()
	{
		if (_megaphoneLeft > 0 && Arrive())
		{
			_megaphoneLeft--;
		}
	}

	private void ExpirePromotions(MartState s)
	{
		if (s.Promotions.RemoveAll(p => p.Until < Today) > 0)
		{
			Dirty();
		}
	}

	/// <summary>有海报时在墙上挂一条原版 Joja 的"新鲜又便宜"横幅。</summary>
	private void ShowPoster(GameLocation shop)
	{
		shop.temporarySprites.AnimatedSprites.RemoveAll(t => t.id == PosterSpriteId);
		if (!ActivePromotions.Any(p => p.Kind == Promotion.Poster))
		{
			return;
		}
		Texture2D tex = Game1.content.Load<Texture2D>("Maps/townInterior");
		shop.temporarySprites.Add(new TemporaryAnimatedSprite
		{
			texture = tex,
			sourceRect = new Rectangle(16, 63 * 16, 80, 16),
			sourceRectStartingPos = new Vector2(16, 63 * 16),
			position = MartInterior.Poster.ToVector2() * 64f,
			scale = 4f,
			interval = 999_999f,
			animationLength = 1,
			totalNumberOfLoops = 999_999,
			layerDepth = 0.0001f,
			id = PosterSpriteId
		});
	}

	// ---------------- 评价 ----------------

	/// <summary>店里摆了多少种家具和摆设（评价"布置得舒不舒服"）。</summary>
	private static int Decor(GameLocation shop) => shop.furniture.Select(f => f.QualifiedItemId).Distinct().Count() + shop.objects.Values.Select(o => o.QualifiedItemId).Distinct().Count();

	public IReadOnlyList<Review> Reviews => State?.Reviews ?? new List<Review>();

	public double Stars => State is MartState s ? ReviewRules.Average(s.Reviews) : 3;

	/// <summary>顾客走的时候留一条评价（星级按价格、新鲜度、品质、温度、排队、布置、迎宾、找零算）。</summary>
	private void WriteReview(NPC buyer, Archetype a, List<Line> lines, bool found, int wait, int checkout)
	{
		if (State is not MartState s || Shops().FirstOrDefault() is not GameLocation shop)
		{
			return;
		}
		found = found && lines.Count > 0;
		double baseTotal = lines.Sum(l => (double)BasePrice(l.Item) * l.Item.Stack);
		ReviewInput input = new ReviewInput(
			found,
			baseTotal <= 0 ? 1 : lines.Sum(l => (double)l.Total) / baseTotal,
			lines.Count == 0 ? 0 : lines.Average(l => Freshness.ReviewDelta(l.Stage)),
			lines.Count == 0 ? 0 : lines.Average(l => Math.Min(3, l.Item.Quality)),
			lines.Any(l => l.WrongTemp),
			wait,
			Decor(shop),
			OnDuty(Staff.Greeter),
			checkout);
		(int stars, string reason) = ReviewRules.Score(input);
		Review r = new Review
		{
			Day = Today,
			Time = Game1.timeOfDay,
			Npc = buyer.Name,
			Stars = stars,
			Reason = reason,
			Text = ReviewText(reason),
			Items = string.Join(Tr.Get("list.sep", "、"), lines.Select(l => l.Item.DisplayName))
		};
		s.Reviews.Add(r);
		if (s.Reviews.Count > 80)
		{
			s.Reviews.RemoveRange(0, s.Reviews.Count - 80);
		}
		s.Reputation = Math.Clamp((int)Math.Round(s.Reputation * 0.8 + ReviewRules.Reputation(ReviewRules.Average(s.Reviews)) * 0.2), -100, 100);
		Dirty();
		if (Game1.currentLocation == shop)
		{
			Hints.Hud(Tr.Get("mart.reviewHud", "{{name}}给了 {{stars}} 星：{{text}}", ("name", Display(buyer.Name)), ("stars", stars), ("text", r.Text)), 5000);
		}
		if (Ai is IAiSender ai && _config().AiOn && _random.NextDouble() < 0.3)
		{
			AiReview(ai, buyer, r, a);
		}
	}

	public static string ReviewText(string reason) => reason switch
	{
		"cheap" => Tr.Get("mart.review.cheap", "价格实在，下次还来。"),
		"pricey" => Tr.Get("mart.review.pricey", "东西不错，就是有点贵。"),
		"fresh" => Tr.Get("mart.review.fresh", "很新鲜，像是刚上架的。"),
		"stale" => Tr.Get("mart.review.stale", "有的东西不太新鲜了。"),
		"quality" => Tr.Get("mart.review.quality", "品质真好。"),
		"temp" => Tr.Get("mart.review.temp", "鱼和蛋奶该放冷柜里吧。"),
		"fast" => Tr.Get("mart.review.fast", "结账挺快的。"),
		"slow" => Tr.Get("mart.review.slow", "排队排了好久。"),
		"cozy" => Tr.Get("mart.review.cozy", "店里布置得真舒服。"),
		"bare" => Tr.Get("mart.review.bare", "店里空荡荡的，多摆点东西吧。"),
		"friendly" => Tr.Get("mart.review.friendly", "店员很热情。"),
		"notfound" => Tr.Get("mart.review.notfound", "没找到想买的。"),
		"mistake" => Tr.Get("mart.review.mistake", "找错钱了，下次注意点。"),
		_ => Tr.Get("mart.review.ok", "还行。")
	};

	/// <summary>让顾客用自己的口气写这条评价（AI 用不了就留模板那句）。</summary>
	private void AiReview(IAiSender ai, NPC buyer, Review r, Archetype a)
	{
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(buyer.Name)}。\n{Persona?.Invoke(buyer.Name) ?? ""}\n\n# 现在\n你刚在{Game1.player.Name}开的农夫小超市里{(r.Items.Length > 0 ? "买了" + r.Items : "转了一圈，没买到想要的")}，给这家店打了 {r.Stars} 星（满分 5 星），主要是因为：{ReviewText(r.Reason)}\n\n# 输出\n用你自己的口气写一句评价，30 个字以内，不加引号，不写星级。";
		Task.Run(async () =>
		{
			try
			{
				AiResult result = await ai.SendAsync(new ChatRequest("store_review", system, "写评价。", 200, 0.9)).ConfigureAwait(false);
				string? text = result.Ok ? ChildPrompts.Clean(result.Text) : null;
				if (text != null)
				{
					MainThread.Run(() =>
					{
						r.Text = text;
						Dirty();
					});
				}
			}
			catch (Exception ex)
			{
				_log.Debug("评价没生成：" + ex.Message);
			}
		});
	}

	// ---------------- 店员 ----------------

	/// <summary>能雇谁、开价多少：4 心以上的大人，不是 Pierre 和 Morris，没在店里上班。</summary>
	public IReadOnlyList<(string Npc, int Wage)> ClerkCandidates(string role, int days)
	{
		MartState? s = State;
		int joja = s?.Rivals.TryGetValue(Competition.Joja, out RivalState? r) == true ? r!.Health : 0;
		return Utility.getAllVillagers().Where(n => n.CanSocialize && !Kids.IsChild(n) && _npcs.IsEnabled(n) && n.Name != "Pierre" && n.Name != "Morris" && !FarAway.Contains(n.Name)
				&& Game1.player.getFriendshipHeartLevelForNPC(n.Name) >= 4 && s?.Clerks.All(c => c.Npc != n.Name) != false)
			.Select(n => (n.Name, Wage: Staff.Wage(role, Game1.player.getFriendshipHeartLevelForNPC(n.Name), joja >= Competition.Losing, n.Name, days)))
			.OrderBy(x => x.Wage).ThenBy(x => Display(x.Name)).ToList();
	}

	/// <summary>签合同：明天开始上班，干 days 天。</summary>
	public bool Hire(string npc, string role, int days, int wage)
	{
		if (State is not MartState s || s.Clerks.Count >= Staff.MaxStaff || s.Clerks.Any(c => c.Npc == npc))
		{
			return false;
		}
		s.Clerks.Add(new Clerk { Npc = npc, Wage = wage, HiredDay = Today, Role = role, Until = Today + days });
		Dirty();
		Gossip?.Invoke(Tr.Get("mart.hiredGossip", "{{name}}到{{farmer}}的店里帮忙了。", ("name", Display(npc)), ("farmer", Game1.player.Name)), new[] { npc });
		return true;
	}

	/// <summary>续约：从合同到期那天起再签同样长。</summary>
	public bool Renew(string npc, int days)
	{
		if (State?.Clerks.FirstOrDefault(c => c.Npc == npc) is not Clerk c)
		{
			return false;
		}
		c.Until = Math.Max(c.Until, Today) + days;
		Dirty();
		return true;
	}

	public void Fire(string npc)
	{
		if (State is MartState s && s.Clerks.RemoveAll(c => c.Npc == npc) > 0)
		{
			Dirty();
			if (_clerkClones.TryGetValue(npc, out NPC? clone))
			{
				clone.currentLocation?.characters.Remove(clone);
				_clerkClones.Remove(npc);
			}
			if (Game1.getCharacterFromName(npc) is NPC real)
			{
				real.IsInvisible = false;
			}
			NpcBusy.Release(npc);
		}
	}

	/// <summary>合同到期：说一声，人就不来了。</summary>
	private void ExpireContracts(MartState s)
	{
		foreach (Clerk c in s.Clerks.Where(c => c.Until > 0 && c.Until < Today).ToList())
		{
			s.Clerks.Remove(c);
			Hints.Hud(Tr.Get("mart.contractEnded", "{{name}}在小超市的合同到期了。想留人的话，在手机 > 小超市 > 店员里重新签。", ("name", Display(c.Npc))), 8000);
			Dirty();
		}
	}

	/// <summary>晚上发工钱（今天上了班的）：先从钱箱扣，不够从你的钱里扣，再不够就欠着（店员心情变差）。</summary>
	private void PayClerks(MartState s)
	{
		foreach (Clerk c in s.Clerks.Where(c => c.HiredDay < Today))
		{
			int fromBox = Math.Min(s.Cashbox, c.Wage);
			s.Cashbox -= fromBox;
			int fromMe = Math.Min(Game1.player.Money, c.Wage - fromBox);
			Game1.player.Money -= fromMe;
			int paid = fromBox + fromMe;
			_ledger.Pay(c.Npc, paid, Tr.Get("mart.wageNote", "在农夫小超市上班的工钱"));
			if (paid < c.Wage)
			{
				AdjustMood?.Invoke(c.Npc, -8);
			}
		}
		Dirty();
	}

	public static string RoleName(string role) => role switch
	{
		Staff.Cashier => Tr.Get("mart.role.cashier", "收银"),
		Staff.Stocker => Tr.Get("mart.role.stocker", "理货"),
		_ => Tr.Get("mart.role.greeter", "迎宾")
	};

	// ---------------- 调试和卸载 ----------------

	/// <summary>调试：导出货架贴图、列出店里的家具。</summary>
	public string DebugFixtures(string file)
	{
		Texture2D tex = Game1.content.Load<Texture2D>(MartContent.FurnitureTexture);
		using (System.IO.FileStream fs = System.IO.File.Create(file))
		{
			tex.SaveAsPng(fs, tex.Width, tex.Height);
		}
		return string.Join("\n", Shops().SelectMany(l => l.furniture).Select(f => $"{f.QualifiedItemId} {f.TileLocation} src={f.sourceRect.Value} tex={ItemRegistry.GetDataOrErrorItem(f.QualifiedItemId).TextureName} box={f.GetBoundingBox()} type={f.furniture_type.Value}"));
	}

	/// <summary>调试：像点了柜台一样。</summary>
	public string DebugCounter()
	{
		ClickCounter();
		return $"排队 {_queue.Count} 位，店里 {_visits.Count} 位。";
	}

	/// <summary>调试：打开店里第 i 个货架。</summary>
	public string DebugShelf(int i)
	{
		List<Furniture> shelves = Shops().SelectMany(Shelves).ToList();
		if (i < 0 || i >= shelves.Count)
		{
			return $"只有 {shelves.Count} 个货架。";
		}
		OpenShelf?.Invoke(shelves[i]);
		return MartService.ShelfName(shelves[i]);
	}

	/// <summary>调试：在农场 (x,y) 立刻建好一间、摆好货架、上一点货。</summary>
	public string DebugBuild(int x, int y)
	{
		Farm farm = Game1.getFarm();
		if (!farm.buildStructure(MartContent.BuildingId, new Vector2(x, y), Game1.player, out Building? b, magicalConstruction: true, skipSafetyChecks: true) || b?.GetIndoors() is not GameLocation inside)
		{
			return $"({x},{y}) 放不下。";
		}
		State?.Welcomed.Add(b.id.Value.ToString());
		Dirty();
		Furnish(inside);
		List<Furniture> shelves = Shelves(inside).ToList();
		void Stock(string kindId, string item, int qty)
		{
			if (shelves.FirstOrDefault(f => MartShelves.Kind(f)?.Id == kindId) is Furniture f && ItemRegistry.Create(item, qty, allowNull: true) is SObject o)
			{
				MartShelves.Add(f, o, qty, Origins.Farm);
			}
		}
		Stock("shelf", "(O)24", 12);
		Stock("shelf", "(O)188", 8);
		Stock("shelf", "(O)400", 6);
		Stock("cold_case", "(O)176", 10);
		Stock("cold_case", "(O)145", 4);
		Stock("cabinet", "(O)348", 3);
		Stock("promo_bin", "(O)192", 20);
		MartShelves.Stockroom().Add(ItemRegistry.Create("(O)190", 5));
		return $"在 ({x},{y}) 建好了 {inside.NameOrUniqueName}，门 {b.getPointForHumanDoor()}。";
	}

	/// <summary>卸 mod 之前：店换成原版大棚屋，货架上和库房里的货收回背包（放不下的放进箱子），拿掉我们的货架。</summary>
	public string Uninstall()
	{
		int buildings = 0, items = 0;
		Inventory box = Game1.player.team.GetOrCreateGlobalInventory("AIStardewValley.Core/Uninstalled");
		foreach (Building b in Buildings().ToList())
		{
			if (b.GetIndoors() is GameLocation l)
			{
				foreach (Furniture f in l.furniture.Where(f => MartShelves.IsShelf(f) || f.QualifiedItemId == MartContent.RegisterQualified).ToList())
				{
					if (MartShelves.IsShelf(f))
					{
						foreach (SObject o in MartShelves.Slots(f).OfType<SObject>().ToList())
						{
							MartShelves.Untag(o);
							box.Add(o);
							items++;
						}
						MartShelves.Slots(f).Clear();
					}
					if (f.heldObject.Value is SObject held)
					{
						f.heldObject.Value = null;
						box.Add(held);
						items++;
					}
					l.furniture.Remove(f);
				}
				foreach (KeyValuePair<Vector2, SObject> pair in l.objects.Pairs.Where(p => p.Value is Chest c && c.GlobalInventoryId == MartShelves.StockroomId).ToList())
				{
					l.objects.Remove(pair.Key);
				}
			}
			b.buildingType.Value = "Big Shed";
			b.ReloadBuildingData();
			buildings++;
		}
		foreach (Item i in MartShelves.Stockroom().Where(i => i != null).ToList())
		{
			MartShelves.Untag(i);
			box.Add(i);
			items++;
		}
		MartShelves.Stockroom().Clear();
		if (box.Count > 0)
		{
			Chest chest = new Chest(true);
			foreach (Item i in box.Where(i => i != null).ToList())
			{
				if (!Game1.player.addItemToInventoryBool(i))
				{
					chest.addItem(i);
				}
			}
			box.Clear();
			if (chest.Items.Count > 0)
			{
				// 背包放不下的装箱，放在农场上离你（不在农场就离家门）最近的空地，不压别的东西。
				Farm farm = Game1.getFarm();
				Vector2 from = Game1.player.currentLocation == farm ? Game1.player.Tile : Utility.PointToVector2(farm.GetMainFarmHouseEntry()) + new Vector2(0, 1);
				Vector2 spot = Enumerable.Range(1, 8).SelectMany(r => Enumerable.Range(-r, 2 * r + 1).SelectMany(dx => new[] { from + new Vector2(dx, -r), from + new Vector2(dx, r), from + new Vector2(-r, dx), from + new Vector2(r, dx) }))
					.FirstOrDefault(t => farm.CanItemBePlacedHere(t), from + new Vector2(1, 0));
				farm.objects[spot] = chest;
			}
		}
		return $"{buildings} 间小超市换成了大棚屋，收回了 {items} 样货。货架已拿掉，现在可以卸载 mod 了。";
	}
}
