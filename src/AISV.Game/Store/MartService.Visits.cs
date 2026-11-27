using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Npcs;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>店里走动的顾客和店员：都是替身，真人在原地藏起来，走完放回去。顾客挑完货到柜台排队，你来收银。</summary>
public sealed partial class MartService
{
	private enum Stage
	{
		ToShelf,
		Browse,
		ToQueue,
		Waiting,
		Leave,
		Done
	}

	private sealed class Visit
	{
		public NPC Real = null!;

		public NPC Clone = null!;

		public GameLocation Shop = null!;

		public Archetype Type = Archetypes.All[0];

		public List<Pick> Picks = new List<Pick>();

		/// <summary>要去看的货架（按顺序）。</summary>
		public List<Furniture> Stops = new List<Furniture>();

		public int Stop;

		public List<Line> Lines = new List<Line>();

		public Stage Stage;

		public bool Arrived;

		public int Wait;

		public int Ticks;

		/// <summary>在柜台前排了多少帧。</summary>
		public int Queued;

		/// <summary>收银窗口开着（正在给这位结账）。</summary>
		public bool Serving;

		public bool Settled;

		public Point Spot;

		/// <summary>店里有这位想要的那类货（没买成也不算"没找到"）。</summary>
		public bool Wanted;
	}

	/// <summary>顾客最多排多久（帧）：45 秒没人收银就放下东西走人。</summary>
	private const int Patience = 45 * 60;

	private readonly List<Visit> _visits = new List<Visit>();

	private readonly List<Visit> _queue = new List<Visit>();

	private readonly Dictionary<string, NPC> _clerkClones = new Dictionary<string, NPC>(StringComparer.OrdinalIgnoreCase);

	/// <summary>进门的地方：室内回农场的那个传送点往里一格。</summary>
	public static Point Entry(GameLocation shop)
	{
		Warp? w = shop.warps.FirstOrDefault();
		return w == null ? new Point(shop.Map.Layers[0].LayerWidth / 2, shop.Map.Layers[0].LayerHeight - 3) : new Point(w.X, w.Y - 1);
	}

	/// <summary>货架前面（下方正中）能站人的一格。</summary>
	private static Point Front(Furniture f)
	{
		Rectangle box = f.GetBoundingBox();
		return new Point(box.Center.X / 64, box.Bottom / 64);
	}

	/// <summary>照真人的样子做一个替身（同一张图、同一个名字显示）。</summary>
	public static NPC Clone(NPC real, GameLocation where, Point tile, string reason)
	{
		NPC clone = new NPC(new AnimatedSprite(real.Sprite.textureName.Value, 0, real.Sprite.SpriteWidth, real.Sprite.SpriteHeight), tile.ToVector2() * 64f, where.Name, 0, "AISV_" + reason + "_" + real.Name, real is StardewValley.Characters.Child ? NpcBusy.BlankPortrait : real.Portrait, eventActor: true)
		{
			displayName = real.displayName
		};
		// 替身的名字没有对应的立绘：不让游戏换地图时按名字重新挑外观（会报找不到 Portraits/替身名）。
		clone.AllowDynamicAppearance = false;
		clone.modData[NpcBusy.StandInKey] = reason;
		clone.modData["AIStardewValley.Core/Real"] = real.Name;
		if (real is StardewValley.Characters.Child kid)
		{
			AISV.Game.Social.KidGrowthService.Dress(clone, kid);
		}
		where.characters.Add(clone);
		clone.currentLocation = where;
		return clone;
	}

	/// <summary>顾客进门：先想好买什么，按货架一个个去看；什么都不想买的也进来转一圈。</summary>
	private bool StartVisit(NPC buyer, GameLocation shop)
	{
		(Archetype a, List<Pick> picks, bool wanted) = Plan(buyer, shop);
		List<Furniture> stops = picks.Select(p => p.Shelf).Distinct().ToList();
		if (stops.Count == 0)
		{
			List<Furniture> shelves = Shelves(shop).ToList();
			if (shelves.Count == 0)
			{
				return false;
			}
			stops.Add(shelves[_random.Next(shelves.Count)]);
		}
		NPC clone = Clone(buyer, shop, Entry(shop), ShopperReason);
		Visit v = new Visit { Real = buyer, Clone = clone, Shop = shop, Type = a, Picks = picks, Stops = stops, Stage = Stage.ToShelf, Wanted = wanted };
		if (!Walk(v, Front(stops[0]), 0))
		{
			shop.characters.Remove(clone);
			return false;
		}
		buyer.IsInvisible = true;
		NpcBusy.Hold(buyer.Name, ShopperReason);
		if (_ledger.Wallet(buyer.Name) is { } w)
		{
			w.ShoppedDay = Today;
		}
		_visits.Add(v);
		Game1.playSound("doorClose");
		Greet(clone);
		if (picks.Count > 0 && _random.NextDouble() < 0.5)
		{
			NpcSpeech.Say(clone, Need(a), 3500);
		}
		return true;
	}

	/// <summary>进门时顺口说一句想买什么（按顾客类型）。</summary>
	private static string Need(Archetype a) => a.Id switch
	{
		Archetypes.Cook => Tr.Get("mart.need.cook", "买点菜回去做饭。"),
		Archetypes.FruitLover => Tr.Get("mart.need.fruit", "有新鲜水果吗？"),
		Archetypes.FishBuyer => Tr.Get("mart.need.fish", "今天的鱼新鲜吗？"),
		Archetypes.Gifter => Tr.Get("mart.need.gift", "想挑件东西送人。"),
		Archetypes.Hungry => Tr.Get("mart.need.hungry", "饿了，有现成吃的吗？"),
		Archetypes.Connoisseur => Tr.Get("mart.need.connoisseur", "看看有没有好东西。"),
		Archetypes.Collector => Tr.Get("mart.need.collector", "听说你这里有矿石？"),
		_ => Tr.Get("mart.need.browse", "随便看看。")
	};

	private static bool Walk(Visit v, Point to, int facing)
	{
		v.Arrived = false;
		v.Clone.controller = new PathFindController(v.Clone, v.Shop, to, facing, (c, l) => v.Arrived = true);
		if (v.Clone.controller.pathToEndPoint == null)
		{
			v.Clone.controller = null;
			return false;
		}
		return true;
	}

	/// <summary>每帧推进店里的顾客：看货架 → 拿货 → 排队 → 收银 → 出门。走不通就当场结算。</summary>
	public void OnUpdateTicked()
	{
		TickStocker();
		if (Game1.ticks % 30 == 0 && _config().FarmerMart && Context.IsWorldReady && IsShop(Game1.currentLocation))
		{
			MartShelves.EnsureDisplay(Game1.currentLocation);
			if (!Game1.currentLocation.temporarySprites.Any(t => t.id == PosterSpriteId))
			{
				ShowPoster(Game1.currentLocation);
			}
		}
		if (_visits.Count == 0)
		{
			return;
		}
		foreach (Visit v in _visits.ToList())
		{
			v.Ticks++;
			if (v.Ticks > 60 * 150 && v.Stage != Stage.Waiting)
			{
				Finish(v, sell: true);
				continue;
			}
			switch (v.Stage)
			{
				case Stage.ToShelf when v.Arrived:
					v.Stage = Stage.Browse;
					v.Wait = 70 + _random.Next(60);
					v.Clone.faceDirection(0);
					if (_random.NextDouble() < 0.4)
					{
						v.Clone.doEmote(_random.NextDouble() < 0.5 ? 32 : 40);
					}
					break;
				case Stage.Browse when --v.Wait <= 0:
					Furniture here = v.Stops[v.Stop];
					foreach (Pick p in v.Picks.Where(p => p.Shelf == here))
					{
						if (TakeLine(p) is Line l)
						{
							v.Lines.Add(l);
						}
					}
					MartShelves.RefreshDisplay(v.Shop);
					if (++v.Stop < v.Stops.Count && Walk(v, Front(v.Stops[v.Stop]), 0))
					{
						v.Stage = Stage.ToShelf;
					}
					else if (v.Lines.Count > 0)
					{
						JoinQueue(v);
					}
					else
					{
						v.Clone.doEmote(v.Wanted ? 8 : 28);
						if (!v.Wanted)
						{
							WriteReview(v.Real, v.Type, v.Lines, found: false, wait: 0, checkout: 0);
						}
						Leave(v);
					}
					break;
				case Stage.ToQueue:
				case Stage.Waiting:
					Queueing(v);
					break;
				case Stage.Leave when v.Arrived:
					Finish(v, sell: false);
					break;
			}
		}
	}

	private void JoinQueue(Visit v)
	{
		_queue.Add(v);
		v.Stage = Stage.ToQueue;
		v.Spot = new Point(-1, -1);
		Reposition();
	}

	/// <summary>队伍里每个人走到自己的位置（前面的人走了就往前挪）。</summary>
	private void Reposition()
	{
		for (int i = 0; i < _queue.Count; i++)
		{
			Visit v = _queue[i];
			Point spot = MartInterior.Queue[Math.Min(i, MartInterior.Queue.Length - 1)];
			if (spot != v.Spot)
			{
				v.Spot = spot;
				if (!Walk(v, spot, 0))
				{
					v.Clone.setTileLocation(spot.ToVector2());
					v.Arrived = true;
				}
			}
		}
	}

	/// <summary>排队：排到柜台前，有收银员（或配偶、或关掉了收银小游戏）就自动结账，否则等你点柜台；等太久就走。</summary>
	private void Queueing(Visit v)
	{
		// 开着菜单（收银、货架）时游戏是停的，不算顾客干等。
		if (!v.Serving && Game1.activeClickableMenu == null)
		{
			v.Queued++;
		}
		bool head = _queue.FirstOrDefault() == v;
		if (v.Stage == Stage.ToQueue && v.Arrived)
		{
			v.Stage = Stage.Waiting;
			v.Clone.faceDirection(0);
			if (head && !AutoCheckout)
			{
				v.Clone.doEmote(16);
				Hints.Tip("MartCheckout", Tr.Get("mart.tipCheckout", "柜台收银找零"));
			}
		}
		if (v.Stage != Stage.Waiting || v.Serving)
		{
			return;
		}
		if (head && AutoCheckout && v.Queued > 90)
		{
			Pay(v, checkout: OnDuty(Staff.Cashier) ? ReviewRules.CheckoutPerfect : 0, tip: 0);
			return;
		}
		if (v.Queued > Patience)
		{
			v.Clone.doEmote(12);
			NpcSpeech.Say(v.Clone, Tr.Get("mart.gaveUp", "等太久了，不买了。"), 3000);
			foreach (Line l in v.Lines)
			{
				PutBack(l);
			}
			MartShelves.RefreshDisplay(v.Shop);
			WriteReview(v.Real, v.Type, v.Lines, found: true, wait: v.Queued / 60, checkout: 0);
			v.Lines.Clear();
			_queue.Remove(v);
			Reposition();
			Leave(v);
		}
	}

	/// <summary>不用你收银：有收银员当班、配偶看店，或者设置里关掉了收银小游戏。</summary>
	private bool AutoCheckout => OnDuty(Staff.Cashier) || SpouseMinding?.Invoke() == true || !_config().MartCheckoutGame;

	/// <summary>点柜台：给排在最前面、已经站到柜台前的人结账。没人排队返回 false。</summary>
	private bool ServeNext()
	{
		Visit? v = _queue.FirstOrDefault();
		if (v == null)
		{
			return false;
		}
		if (v.Stage != Stage.Waiting || v.Serving)
		{
			Hints.Hud(Tr.Get("mart.comingToCounter", "{{name}}正走过来。", ("name", Display(v.Real.Name))), 2000);
			return true;
		}
		int total = v.Lines.Sum(l => l.Total);
		if (OpenCheckout == null)
		{
			Pay(v, checkout: 0, tip: 0);
			return true;
		}
		v.Serving = true;
		int paid = Checkout.Paid(total, _random.NextDouble());
		OpenCheckout(new CheckoutRequest
		{
			Customer = v.Clone,
			Name = Display(v.Real.Name),
			Lines = v.Lines,
			Total = total,
			Paid = paid,
			Done = (given, seconds) =>
			{
				v.Serving = false;
				(bool ok, int tip) = Checkout.Result(total, paid, given, seconds);
				if (!ok)
				{
					v.Clone.doEmote(8);
					NpcSpeech.Say(v.Clone, given < paid - total ? Tr.Get("mart.shortChange", "找的钱不对吧？少了。") : Tr.Get("mart.overChange", "你多找给我了，喏，还你。"), 3000);
				}
				else if (tip > 0)
				{
					NpcSpeech.Say(v.Clone, Tr.Get("mart.keepChange", "真快！零头不用找了。"), 3000);
				}
				Pay(v, checkout: ok ? seconds <= 20 ? ReviewRules.CheckoutPerfect : 0 : ReviewRules.CheckoutWrong, tip: tip);
			},
			Cancel = () => v.Serving = false
		});
		return true;
	}

	/// <summary>付钱、留评价、出门。</summary>
	private void Pay(Visit v, int checkout, int tip)
	{
		if (v.Settled)
		{
			return;
		}
		v.Settled = true;
		Settle(v.Real, v.Lines, live: true, speaker: v.Clone, tip: tip);
		WriteReview(v.Real, v.Type, v.Lines, found: true, wait: v.Queued / 60, checkout: checkout);
		_queue.Remove(v);
		Reposition();
		Leave(v);
	}

	private void Leave(Visit v)
	{
		v.Stage = Stage.Leave;
		if (!Walk(v, Entry(v.Shop), 2))
		{
			Finish(v, sell: false);
		}
	}

	/// <summary>收回替身。sell：还没结账的按后台结账（你走出店、换天时）。</summary>
	private void Finish(Visit v, bool sell)
	{
		if (sell && !v.Settled)
		{
			foreach (Pick p in v.Picks.Where(p => v.Lines.All(l => l.Shelf != p.Shelf) && v.Stops.IndexOf(p.Shelf) >= v.Stop))
			{
				if (TakeLine(p) is Line l)
				{
					v.Lines.Add(l);
				}
			}
			if (v.Lines.Count > 0)
			{
				v.Settled = true;
				Settle(v.Real, v.Lines, live: false, speaker: null, tip: 0);
				WriteReview(v.Real, v.Type, v.Lines, found: true, wait: v.Queued / 60, checkout: 0);
			}
		}
		v.Stage = Stage.Done;
		_queue.Remove(v);
		v.Shop.characters.Remove(v.Clone);
		v.Real.IsInvisible = false;
		NpcBusy.Release(v.Real.Name);
		_visits.Remove(v);
	}

	/// <summary>换天、存档、回标题、打烊：店里的替身全部收回（没结账的后台结掉）。</summary>
	public void CleanupVisits()
	{
		foreach (Visit v in _visits.ToList())
		{
			Finish(v, sell: Context.IsWorldReady);
		}
		_queue.Clear();
		NpcBusy.ReleaseAll(ShopperReason);
	}

	/// <summary>进店：把货画上货架、贴上海报；走出店：店里的顾客后台结账收回。</summary>
	public void OnShopWarped(WarpedEventArgs e)
	{
		if (!e.IsLocalPlayer || !_config().FarmerMart)
		{
			return;
		}
		if (IsShop(e.NewLocation))
		{
			if (Host && State is MartState s)
			{
				OpenNewShops(s);
			}
			MartShelves.RefreshDisplay(e.NewLocation);
			ShowPoster(e.NewLocation);
			Hints.Tip("MartInside", Tr.Get("mart.tipInside", "农夫小超市"));
			if (Suspended)
			{
				Hints.Hud(Tr.Get("mart.suspendedHud", "停业整顿中，还有 {{n}} 天", ("n", ClosedUntil - Today)), 5000);
			}
		}
		if (IsShop(e.OldLocation) && !IsShop(e.NewLocation))
		{
			CleanupVisits();
		}
	}

	// ---------------- 店员 ----------------

	/// <summary>这个岗位现在有人当班吗。</summary>
	private bool OnDuty(string role) => State?.Clerks.Any(c => c.Role == role && _clerkClones.ContainsKey(c.Npc)) == true;

	private static Point Post(string role, int index) => role switch
	{
		Staff.Cashier => index == 0 ? MartInterior.Cashier : MartInterior.Cashier + new Point(2, 0),
		Staff.Greeter => MartInterior.Greeter,
		_ => new Point(11, 8)
	};

	/// <summary>营业时间里店员到岗（真人藏起来，算在上班）；打烊就回去。合同到期的不来。</summary>
	private void ClerkShift(int time)
	{
		if (State is not MartState s || !HasShop)
		{
			return;
		}
		GameLocation shop = Shops().First();
		bool open = IsOpen(time);
		foreach (Clerk c in s.Clerks)
		{
			if (Game1.getCharacterFromName(c.Npc) is not NPC real)
			{
				continue;
			}
			bool working = open && (c.Until == 0 || c.Until >= Today) && c.HiredDay < Today;
			if (working && !_clerkClones.ContainsKey(c.Npc) && !real.IsInvisible && !NpcBusy.IsBySources(c.Npc))
			{
				int index = s.Clerks.Where(x => x.Role == c.Role).ToList().IndexOf(c);
				NPC clone = Clone(real, shop, Post(c.Role, index), ClerkReason);
				clone.faceDirection(2);
				real.IsInvisible = true;
				NpcBusy.Hold(c.Npc, ClerkReason);
				_clerkClones[c.Npc] = clone;
			}
			else if (!working && _clerkClones.TryGetValue(c.Npc, out NPC? clone))
			{
				clone.currentLocation?.characters.Remove(clone);
				_clerkClones.Remove(c.Npc);
				real.IsInvisible = false;
				NpcBusy.Release(c.Npc);
			}
		}
	}

	/// <summary>迎宾看到有人进门：打个招呼。</summary>
	private void Greet(NPC customer)
	{
		Clerk? g = State?.Clerks.FirstOrDefault(c => c.Role == Staff.Greeter && _clerkClones.ContainsKey(c.Npc));
		if (g != null && _clerkClones[g.Npc] is NPC clone && _random.NextDouble() < 0.6)
		{
			clone.faceGeneralDirection(customer.Position);
			NpcSpeech.Say(clone, Tr.Get("mart.welcomeIn", "欢迎光临！"), 2500);
		}
	}

	private int _stockerTick;

	/// <summary>理货员在货架之间走来走去。</summary>
	private void TickStocker()
	{
		if (++_stockerTick % 600 != 0 || State is not MartState s)
		{
			return;
		}
		foreach (Clerk c in s.Clerks.Where(c => c.Role == Staff.Stocker))
		{
			if (_clerkClones.TryGetValue(c.Npc, out NPC? clone) && clone.controller == null && clone.currentLocation is GameLocation shop)
			{
				List<Furniture> shelves = Shelves(shop).ToList();
				if (shelves.Count > 0)
				{
					clone.controller = new PathFindController(clone, shop, Front(shelves[_random.Next(shelves.Count)]), 0);
					if (clone.controller.pathToEndPoint == null)
					{
						clone.controller = null;
					}
				}
			}
		}
	}

	private NPC? _spouseClone;

	/// <summary>配偶来看店：站在柜台后面（真人藏起来）；off 时收回。</summary>
	public bool SpouseShift(NPC? real, bool on)
	{
		if (!on)
		{
			if (_spouseClone != null)
			{
				_spouseClone.currentLocation?.characters.Remove(_spouseClone);
				if (_spouseClone.modData.TryGetValue("AIStardewValley.Core/Real", out string? name) && Game1.getCharacterFromName(name) is NPC n)
				{
					n.IsInvisible = false;
					NpcBusy.Release(n.Name);
				}
				_spouseClone = null;
			}
			return true;
		}
		if (real == null || !HasShop || _spouseClone != null)
		{
			return false;
		}
		GameLocation shop = Shops().First();
		_spouseClone = Clone(real, shop, MartInterior.Cashier + new Point(OnDuty(Staff.Cashier) ? 2 : 0, 0), ClerkReason);
		_spouseClone.faceDirection(2);
		real.IsInvisible = true;
		NpcBusy.Hold(real.Name, ClerkReason);
		return true;
	}

	/// <summary>配偶把自己做的东西摆上能放的货架（放不下就不摆）。</summary>
	public bool StockSpouseWork(string npc, string itemId, int qty)
	{
		if (ItemRegistry.Create(itemId, qty, allowNull: true) is not SObject o)
		{
			return false;
		}
		o.modData[SpouseWorkKey] = npc;
		foreach ((GameLocation shop, Furniture f) in AllShelves())
		{
			if (MartShelves.Add(f, o, o.Stack, Origins.Spouse) > 0)
			{
				if (Game1.currentLocation == shop)
				{
					MartShelves.RefreshDisplay(shop);
				}
				return true;
			}
		}
		return false;
	}

	public const string SpouseWorkKey = "AIStardewValley.Core/SpouseWork";

	public void CleanupClerks()
	{
		foreach ((string name, NPC clone) in _clerkClones.ToList())
		{
			clone.currentLocation?.characters.Remove(clone);
			if (Game1.getCharacterFromName(name) is NPC real)
			{
				real.IsInvisible = false;
			}
		}
		_clerkClones.Clear();
		SpouseShift(null, false);
		NpcBusy.ReleaseAll(ClerkReason);
	}

	/// <summary>读档时：以前没收回的替身清掉（比如上次游戏崩了）。</summary>
	public static void Sweep()
	{
		foreach (GameLocation l in Game1.locations.Concat(Shops()))
		{
			l.characters.RemoveWhere(c => c != null && c.modData.TryGetValue(NpcBusy.StandInKey, out string? why) && why is ShopperReason or ClerkReason or RivalReason);
		}
	}
}
