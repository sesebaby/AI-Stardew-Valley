using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Machines;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace AISV.Game.Companions;

/// <summary>
/// 农活的共用执行器（帮工和配偶日程都用）：不放动画，直接改地图状态；掉出来的东西交给 store。
/// 只碰作物、动物、杂物和原版机器的成品，不砍树、不动玩家摆的东西。
/// </summary>
public static class FarmChoreExecutor
{
	public static bool NeedsWater(HoeDirt dirt) => dirt.crop != null && !dirt.crop.dead.Value && !dirt.isWatered() && dirt.needsWatering();

	public static bool IsDebris(SObject o) => o.IsTwig() || o.IsBreakableStone() || o.IsWeeds();

	/// <summary>温室（没修好时为 null）。</summary>
	public static GameLocation? Greenhouse()
	{
		Farm? farm = Game1.getFarm();
		if (farm == null || !farm.greenhouseUnlocked.Value)
		{
			return null;
		}
		return Game1.getLocationFromName("Greenhouse");
	}

	public static IEnumerable<AnimalHouse> AnimalHouses() => Game1.getFarm()?.buildings.Select(b => b.GetIndoors()).OfType<AnimalHouse>() ?? Enumerable.Empty<AnimalHouse>();

	/// <summary>每样活现在有多少可干。</summary>
	public static int Count(string task)
	{
		Farm? farm = Game1.getFarm();
		if (farm == null)
		{
			return 0;
		}
		return task switch
		{
			SpouseTasks.Water => Dirt(farm, NeedsWater).Count(),
			SpouseTasks.Harvest => Dirt(farm, d => d.readyForHarvest()).Count(),
			SpouseTasks.Greenhouse => Greenhouse() is GameLocation g ? Dirt(g, d => NeedsWater(d) || d.readyForHarvest()).Count() : 0,
			SpouseTasks.Animals => farm.getAllFarmAnimals().Count(a => !a.wasPet.Value),
			SpouseTasks.Produce => AnimalHouses().Sum(h => Produce(h).Count()) + Produce(farm).Count(),
			SpouseTasks.Hay => AnimalHouses().Sum(h => EmptyTroughs(h).Count()) is int n && n > 0 && farm.piecesOfHay.Value > 0 ? n : 0,
			SpouseTasks.Clear => farm.objects.Values.Count(IsDebris),
			SpouseTasks.Ponds => farm.buildings.OfType<FishPond>().Count(p => p.output.Value != null),
			SpouseTasks.Machines => Machines().Count(),
			_ => 0
		};
	}

	/// <summary>后台干活：最多 max 件，返回干了几件。skip 用来跳过你眼前的格子。</summary>
	public static int Do(string task, int max, Action<Item> store, Func<GameLocation, Vector2, bool>? skip = null)
	{
		Farm? farm = Game1.getFarm();
		if (farm == null || max <= 0)
		{
			return 0;
		}
		int did = 0;
		bool Skip(GameLocation l, Vector2 t) => skip?.Invoke(l, t) == true;
		switch (task)
		{
			case SpouseTasks.Water:
				foreach (Vector2 t in Dirt(farm, NeedsWater).ToList())
				{
					if (did >= max)
					{
						break;
					}
					if (!Skip(farm, t) && WaterTile(farm, t))
					{
						did++;
					}
				}
				break;
			case SpouseTasks.Harvest:
				foreach (Vector2 t in Dirt(farm, d => d.readyForHarvest()).ToList())
				{
					if (did >= max)
					{
						break;
					}
					if (!Skip(farm, t) && HarvestTile(farm, t, store))
					{
						did++;
					}
				}
				break;
			case SpouseTasks.Greenhouse:
				if (Greenhouse() is GameLocation g)
				{
					foreach (Vector2 t in Dirt(g, d => NeedsWater(d) || d.readyForHarvest()).ToList())
					{
						if (did >= max)
						{
							break;
						}
						if (Skip(g, t))
						{
							continue;
						}
						bool harvested = HarvestTile(g, t, store);
						bool watered = g.terrainFeatures.TryGetValue(t, out TerrainFeature f) && f is HoeDirt d && NeedsWater(d) && WaterTile(g, t);
						if (harvested || watered)
						{
							did++;
						}
					}
				}
				break;
			case SpouseTasks.Animals:
				foreach (FarmAnimal a in farm.getAllFarmAnimals().Where(a => !a.wasPet.Value).ToList())
				{
					if (did >= max)
					{
						break;
					}
					if (!Skip(a.currentLocation, a.Tile) && Pet(a))
					{
						did++;
					}
				}
				break;
			case SpouseTasks.Produce:
				foreach (GameLocation l in AnimalHouses().Cast<GameLocation>().Append(farm))
				{
					foreach (Vector2 t in Produce(l).ToList())
					{
						if (did >= max)
						{
							break;
						}
						if (!Skip(l, t) && l.objects.TryGetValue(t, out SObject o))
						{
							l.objects.Remove(t);
							o.IsSpawnedObject = false;
							store(o);
							did++;
						}
					}
				}
				break;
			case SpouseTasks.Hay:
				foreach (AnimalHouse h in AnimalHouses())
				{
					foreach (Vector2 t in EmptyTroughs(h).ToList())
					{
						if (did >= max)
						{
							break;
						}
						if (GameLocation.GetHayFromAnySilo(farm) is not SObject hay)
						{
							return did;
						}
						h.objects.Add(t, hay);
						did++;
					}
				}
				break;
			case SpouseTasks.Clear:
				foreach (Vector2 t in farm.objects.Pairs.Where(p => IsDebris(p.Value)).Select(p => p.Key).ToList())
				{
					if (did >= max)
					{
						break;
					}
					if (!Skip(farm, t) && ClearTile(farm, t, store))
					{
						did++;
					}
				}
				break;
			case SpouseTasks.Ponds:
				foreach (FishPond p in farm.buildings.OfType<FishPond>().Where(p => p.output.Value != null).ToList())
				{
					if (did >= max)
					{
						break;
					}
					Item item = p.output.Value;
					p.output.Value = null;
					store(item);
					did++;
				}
				break;
			case SpouseTasks.Machines:
				foreach ((GameLocation l, SObject m) in Machines().ToList())
				{
					if (did >= max)
					{
						break;
					}
					if (!Skip(l, m.TileLocation) && Collect(l, m, store))
					{
						did++;
					}
				}
				break;
		}
		return did;
	}

	public static IEnumerable<Vector2> Dirt(GameLocation l, Func<HoeDirt, bool> want)
	{
		foreach (KeyValuePair<Vector2, TerrainFeature> pair in l.terrainFeatures.Pairs)
		{
			if (pair.Value is HoeDirt dirt && want(dirt))
			{
				yield return pair.Key;
			}
		}
		// 盆栽也算（温室里常见）。
		foreach (KeyValuePair<Vector2, SObject> pair in l.objects.Pairs)
		{
			if (pair.Value is IndoorPot pot && pot.hoeDirt.Value is HoeDirt dirt && want(dirt))
			{
				yield return pair.Key;
			}
		}
	}

	private static HoeDirt? DirtAt(GameLocation l, Vector2 tile) =>
		l.terrainFeatures.TryGetValue(tile, out TerrainFeature f) && f is HoeDirt d ? d : l.objects.TryGetValue(tile, out SObject o) && o is IndoorPot pot ? pot.hoeDirt.Value : null;

	public static bool WaterTile(GameLocation l, Vector2 tile)
	{
		if (DirtAt(l, tile) is not HoeDirt dirt)
		{
			return false;
		}
		dirt.state.Value = HoeDirt.watered;
		return true;
	}

	/// <summary>收获掉出来的东西会落在那块地或你当前的地图，两边都收走。</summary>
	public static bool HarvestTile(GameLocation l, Vector2 tile, Action<Item> store)
	{
		if (DirtAt(l, tile) is not HoeDirt dirt || dirt.crop == null || !dirt.readyForHarvest())
		{
			return false;
		}
		GameLocation? here = Game1.currentLocation;
		HashSet<Debris> before = new HashSet<Debris>(l.debris);
		if (here != null && here != l)
		{
			before.UnionWith(here.debris);
		}
		dirt.crop.harvest((int)tile.X, (int)tile.Y, dirt, null, isForcedScytheHarvest: true);
		if (dirt.crop == null || !dirt.crop.RegrowsAfterHarvest())
		{
			dirt.destroyCrop(showAnimation: false);
		}
		foreach (GameLocation loc in here != null && here != l ? new[] { l, here } : new[] { l })
		{
			foreach (Debris d in loc.debris.Where(d => !before.Contains(d)).ToList())
			{
				if (d.item != null)
				{
					store(d.item);
					loc.debris.Remove(d);
				}
			}
		}
		return true;
	}

	/// <summary>树枝给木头，石头给石头，杂草偶尔给纤维。</summary>
	public static bool ClearTile(GameLocation l, Vector2 tile, Action<Item> store)
	{
		if (!l.objects.TryGetValue(tile, out SObject obj) || !IsDebris(obj))
		{
			return false;
		}
		string? drop = obj.IsTwig() ? "(O)388" : obj.IsBreakableStone() ? "(O)390" : Game1.random.NextDouble() < 0.5 ? "(O)771" : null;
		l.objects.Remove(tile);
		if (drop != null)
		{
			store(ItemRegistry.Create(drop));
		}
		return true;
	}

	public static bool Pet(FarmAnimal a)
	{
		if (a.wasPet.Value)
		{
			return false;
		}
		a.wasPet.Value = true;
		a.friendshipTowardFarmer.Value = Math.Min(1000, a.friendshipTowardFarmer.Value + (a.wasAutoPet.Value ? 7 : 15));
		int drain = a.GetAnimalData()?.HappinessDrain ?? 0;
		a.happiness.Value = (byte)Math.Min(255, a.happiness.Value + Math.Max(5, 30 + drain));
		return true;
	}

	/// <summary>地上能捡的动物产品（蛋、毛、松露这类）。</summary>
	private static IEnumerable<Vector2> Produce(GameLocation l) => l.objects.Pairs.Where(p => p.Value.IsSpawnedObject && p.Value.isAnimalProduct()).Select(p => p.Key);

	private static IEnumerable<Vector2> EmptyTroughs(AnimalHouse h)
	{
		if (h.animalsThatLiveHere.Count == 0)
		{
			yield break;
		}
		for (int x = 0; x < h.map.Layers[0].LayerWidth; x++)
		{
			for (int y = 0; y < h.map.Layers[0].LayerHeight; y++)
			{
				Vector2 t = new Vector2(x, y);
				if (h.doesTileHaveProperty(x, y, "Trough", "Back") != null && !h.objects.ContainsKey(t))
				{
					yield return t;
				}
			}
		}
	}

	/// <summary>农场、温室和各建筑里做好了的原版机器（有机器数据的才算）。</summary>
	private static IEnumerable<(GameLocation, SObject)> Machines()
	{
		Farm? farm = Game1.getFarm();
		if (farm == null)
		{
			yield break;
		}
		List<GameLocation> places = new List<GameLocation> { farm };
		places.AddRange(farm.buildings.Select(b => b.GetIndoors()).Where(l => l != null)!);
		if (Greenhouse() is GameLocation g && !places.Contains(g))
		{
			places.Add(g);
		}
		foreach (GameLocation l in places)
		{
			foreach (SObject o in l.objects.Values)
			{
				if (o.bigCraftable.Value && o.readyForHarvest.Value && o.heldObject.Value != null && o.GetMachineData() != null)
				{
					yield return (l, o);
				}
			}
		}
	}

	/// <summary>照 Automate 的做法：拿走成品、复位，再让机器按"取走成品"的规则接着做（比如水晶复制器）。</summary>
	private static bool Collect(GameLocation l, SObject m, Action<Item> store)
	{
		SObject output = m.heldObject.Value;
		MachineData? data = m.GetMachineData();
		if (output == null || data == null)
		{
			return false;
		}
		m.heldObject.Value = null;
		m.readyForHarvest.Value = false;
		m.showNextIndex.Value = false;
		m.ResetParentSheetIndex();
		if (MachineDataUtility.TryGetMachineOutputRule(m, data, MachineOutputTrigger.OutputCollected, output.getOne(), null, l, out MachineOutputRule rule, out _, out _, out _))
		{
			m.OutputMachine(data, rule, m.lastInputItem.Value, null, l, probe: false);
		}
		store(output);
		return true;
	}
}
