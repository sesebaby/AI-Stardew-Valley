using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Npcs;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.Pathfinding;

namespace AISV.Game.Store;

/// <summary>店里走动的顾客和店员：都是替身，真人在原地藏起来，走完放回去。</summary>
public sealed partial class MartService
{
	private enum Stage
	{
		ToShelf,
		Browse,
		ToRegister,
		Pay,
		Leave,
		Done
	}

	private sealed class Visit
	{
		public NPC Real = null!;

		public NPC Clone = null!;

		public GameLocation Shop = null!;

		public Furniture Shelf = null!;

		public int Qty;

		public Stage Stage;

		public bool Arrived;

		public int Wait;

		public int Ticks;
	}

	private readonly List<Visit> _visits = new List<Visit>();

	private readonly Dictionary<string, NPC> _clerkClones = new Dictionary<string, NPC>(StringComparer.OrdinalIgnoreCase);

	/// <summary>进门的地方：室内回农场的那个传送点往里一格。</summary>
	public static Point Entry(GameLocation shop)
	{
		Warp? w = shop.warps.FirstOrDefault();
		return w == null ? new Point(shop.Map.Layers[0].LayerWidth / 2, shop.Map.Layers[0].LayerHeight - 3) : new Point(w.X, w.Y - 1);
	}

	/// <summary>家具前面（下方）能站人的一格。</summary>
	private static Point Front(Furniture f) => new Point((int)f.TileLocation.X, (int)f.TileLocation.Y + Math.Max(1, f.getTilesHigh()));

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
		where.characters.Add(clone);
		clone.currentLocation = where;
		return clone;
	}

	private bool StartVisit(NPC buyer, GameLocation shop)
	{
		if (Choose(buyer, shop) is not (Furniture shelf, int qty))
		{
			return false;
		}
		Point entry = Entry(shop);
		NPC clone = Clone(buyer, shop, entry, ShopperReason);
		Visit v = new Visit { Real = buyer, Clone = clone, Shop = shop, Shelf = shelf, Qty = qty, Stage = Stage.ToShelf };
		if (!Walk(v, Front(shelf), 0))
		{
			shop.characters.Remove(clone);
			return false;
		}
		buyer.IsInvisible = true;
		NpcBusy.Hold(buyer.Name, ShopperReason);
		_visits.Add(v);
		Game1.playSound("doorClose");
		return true;
	}

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

	/// <summary>每帧推进店里的顾客：到货架前看一会儿 → 去收银台 → 结账 → 出门。走不通就当场结算。</summary>
	public void OnUpdateTicked()
	{
		if (_visits.Count == 0)
		{
			return;
		}
		foreach (Visit v in _visits.ToList())
		{
			v.Ticks++;
			if (v.Ticks > 60 * 60)
			{
				Finish(v, sell: v.Stage < Stage.Pay);
				continue;
			}
			switch (v.Stage)
			{
				case Stage.ToShelf when v.Arrived:
					v.Stage = Stage.Browse;
					v.Wait = 90;
					v.Clone.faceDirection(0);
					v.Clone.doEmote(_random.NextDouble() < 0.5 ? 32 : 40);
					break;
				case Stage.Browse when --v.Wait <= 0:
					if (Register(v.Shop) is Furniture reg && Walk(v, Front(reg), 0))
					{
						v.Stage = Stage.ToRegister;
					}
					else
					{
						v.Stage = Stage.Pay;
					}
					break;
				case Stage.ToRegister when v.Arrived:
					v.Stage = Stage.Pay;
					break;
				case Stage.Pay:
					SellTo(v.Real, v.Shop, live: true, v.Clone, (v.Shelf, v.Qty));
					v.Stage = Stage.Leave;
					if (!Walk(v, Entry(v.Shop), 2))
					{
						Finish(v, sell: false);
					}
					break;
				case Stage.Leave when v.Arrived:
					Finish(v, sell: false);
					break;
			}
		}
	}

	private void Finish(Visit v, bool sell)
	{
		if (sell)
		{
			SellTo(v.Real, v.Shop, live: false, choice: (v.Shelf, v.Qty));
		}
		v.Stage = Stage.Done;
		v.Shop.characters.Remove(v.Clone);
		v.Real.IsInvisible = false;
		NpcBusy.Release(v.Real.Name);
		_visits.Remove(v);
	}

	/// <summary>换天、存档、回标题：店里的替身全部收回。</summary>
	public void CleanupVisits()
	{
		foreach (Visit v in _visits.ToList())
		{
			Finish(v, sell: false);
		}
		NpcBusy.ReleaseAll(ShopperReason);
	}

	// ---------------- 店员 ----------------

	/// <summary>营业时间里店员在收银台后面站着（真人藏起来，算在上班）；打烊就回去。</summary>
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
			if (open && !_clerkClones.ContainsKey(c.Npc) && !real.IsInvisible && !NpcBusy.IsBySources(c.Npc))
			{
				Point at = Register(shop) is Furniture reg ? new Point((int)reg.TileLocation.X, (int)reg.TileLocation.Y - 1) : Entry(shop);
				NPC clone = Clone(real, shop, at, ClerkReason);
				clone.faceDirection(2);
				real.IsInvisible = true;
				NpcBusy.Hold(c.Npc, ClerkReason);
				_clerkClones[c.Npc] = clone;
			}
			else if (!open && _clerkClones.TryGetValue(c.Npc, out NPC? clone))
			{
				clone.currentLocation?.characters.Remove(clone);
				_clerkClones.Remove(c.Npc);
				real.IsInvisible = false;
				NpcBusy.Release(c.Npc);
			}
		}
	}

	private NPC? _spouseClone;

	/// <summary>配偶来看店：站在收银台后面（真人藏起来）；off 时收回。</summary>
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
		Point at = Register(shop) is Furniture reg ? new Point((int)reg.TileLocation.X + 1, (int)reg.TileLocation.Y - 1) : Entry(shop);
		_spouseClone = Clone(real, shop, at, ClerkReason);
		_spouseClone.faceDirection(2);
		real.IsInvisible = true;
		NpcBusy.Hold(real.Name, ClerkReason);
		return true;
	}

	/// <summary>配偶把自己做的东西摆到一张空陈列台上（没有空台就不摆）。</summary>
	public bool StockSpouseWork(string npc, string itemId, int qty)
	{
		Furniture? empty = AllShelves().Select(s => s.Shelf).FirstOrDefault(f => f.heldObject.Value == null);
		if (empty == null || ItemRegistry.Create(itemId, qty, allowNull: true) is not StardewValley.Object o)
		{
			return false;
		}
		o.modData[SpouseWorkKey] = npc;
		empty.heldObject.Value = o;
		return true;
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

	/// <summary>能雇谁：4 心以上的大人，不是 Pierre 和 Morris；Joja 不行了时 Sam 和 Shane 更愿意来（工钱少一点）。</summary>
	public IReadOnlyList<(string Npc, int Wage)> ClerkCandidates()
	{
		MartState? s = State;
		int joja = s?.Rivals.TryGetValue(Competition.Joja, out RivalState? r) == true ? r!.Health : 0;
		return Utility.getAllVillagers().Where(n => n.CanSocialize && !Kids.IsChild(n) && _npcs.IsEnabled(n) && n.Name != "Pierre" && n.Name != "Morris"
				&& Game1.player.getFriendshipHeartLevelForNPC(n.Name) >= 4 && s?.Clerks.All(c => c.Npc != n.Name) != false)
			.Select(n => (n.Name, Wage: (n.Name is "Sam" or "Shane") && joja >= Competition.Losing ? 150 : 250))
			.OrderBy(x => x.Wage).ThenBy(x => Display(x.Name)).ToList();
	}

	public bool Hire(string npc, int wage)
	{
		if (State is not MartState s || s.Clerks.Count >= 2 || s.Clerks.Any(c => c.Npc == npc))
		{
			return false;
		}
		s.Clerks.Add(new Clerk { Npc = npc, Wage = wage, HiredDay = Today });
		_store()!.MarkDirty(MartState.Path);
		Gossip?.Invoke(Tr.Get("mart.hiredGossip", "{{name}}到{{farmer}}的店里帮忙了。", ("name", Display(npc)), ("farmer", Game1.player.Name)), new[] { npc });
		return true;
	}

	public void Fire(string npc)
	{
		if (State is MartState s && s.Clerks.RemoveAll(c => c.Npc == npc) > 0)
		{
			_store()!.MarkDirty(MartState.Path);
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

	/// <summary>晚上发工钱：先从钱箱扣，不够从你的钱里扣，再不够就欠着不发（店员心情变差）。</summary>
	private void PayClerks(MartState s)
	{
		foreach (Clerk c in s.Clerks)
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
		_store()!.MarkDirty(MartState.Path);
	}

	/// <summary>读档时：以前没收回的替身清掉（比如上次游戏崩了）。</summary>
	public static void Sweep()
	{
		foreach (GameLocation l in Game1.locations.Concat(Shops()))
		{
			l.characters.RemoveWhere(c => c != null && c.modData.TryGetValue(NpcBusy.StandInKey, out string? why) && why is ShopperReason or ClerkReason or RivalReason);
		}
	}

	/// <summary>卸 mod 之前：店换成原版大棚屋，陈列台上的货收回背包（放不下的放进箱子）。</summary>
	/// <summary>调试：在农场 (x,y) 立刻建好一间、摆好两张陈列台和收银台、上一点货。</summary>
	public string DebugBuild(int x, int y)
	{
		Farm farm = Game1.getFarm();
		if (!farm.buildStructure(MartContent.BuildingId, new Vector2(x, y), Game1.player, out Building? b, magicalConstruction: true, skipSafetyChecks: true) || b?.GetIndoors() is not GameLocation inside)
		{
			return $"({x},{y}) 放不下。";
		}
		State?.Welcomed.Add(b.id.Value.ToString());
		_store()?.MarkDirty(MartState.Path);
		Furniture reg = Furniture.GetFurnitureInstance(MartContent.RegisterId, new Vector2(8, 6));
		Furniture a = Furniture.GetFurnitureInstance(MartContent.ShelfId, new Vector2(4, 9));
		Furniture c = Furniture.GetFurnitureInstance(MartContent.ShelfId, new Vector2(12, 9));
		a.heldObject.Value = ItemRegistry.Create<StardewValley.Object>("(O)24", 10);
		c.heldObject.Value = ItemRegistry.Create<StardewValley.Object>("(O)176", 6);
		inside.furniture.Add(reg);
		inside.furniture.Add(a);
		inside.furniture.Add(c);
		return $"在 ({x},{y}) 建好了 {inside.NameOrUniqueName}，门 {b.getPointForHumanDoor()}。";
	}

	public string Uninstall()
	{
		int buildings = 0, items = 0;
		foreach (Building b in Buildings().ToList())
		{
			if (b.GetIndoors() is GameLocation l)
			{
				foreach (Furniture f in l.furniture.Where(f => f.QualifiedItemId is MartContent.ShelfQualified or MartContent.RegisterQualified).ToList())
				{
					if (f.heldObject.Value is StardewValley.Object o)
					{
						f.heldObject.Value = null;
						Game1.player.team.GetOrCreateGlobalInventory("AIStardewValley.Core/Uninstalled").Add(o);
						items++;
					}
					l.furniture.Remove(f);
				}
			}
			b.buildingType.Value = "Big Shed";
			buildings++;
		}
		if (items > 0)
		{
			StardewValley.Inventories.Inventory box = Game1.player.team.GetOrCreateGlobalInventory("AIStardewValley.Core/Uninstalled");
			StardewValley.Objects.Chest chest = new StardewValley.Objects.Chest(true);
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
				Game1.getFarm().objects[Game1.player.Tile + new Vector2(1, 0)] = chest;
			}
		}
		return $"{buildings} 间小超市换成了大棚屋，收回了 {items} 样货。陈列台和收银台已拿掉，现在可以卸载 mod 了。";
	}
}
