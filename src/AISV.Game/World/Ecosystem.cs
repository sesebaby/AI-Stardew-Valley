using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Compat;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Crops;
using StardewValley.GameData.FruitTrees;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Pathfinding;

namespace AISV.Game.World;

/// <summary>一种作物或果树和它的收获物。ID 都带前缀（"(O)24"）。</summary>
public sealed record CropInfo(string Key, string SeedItemId, string HarvestItemId, string HarvestName, string HarvestDisplayName, IReadOnlyList<string> Seasons, int HarvestPrice, int HarvestCategory, int DaysToGrow, int RegrowDays, bool IsFruitTree, string? SourceModId);

/// <summary>村民住在哪。Source：home-data（Data/Characters 的 Home）、spouse（农舍）、default-map、fallback。</summary>
public sealed record NpcHomeInfo(string Name, string DisplayName, string Location, string LocationDisplayName, int X, int Y, int Facing, string Source, bool LocationExists, bool InWorld, bool IsVanilla, string? SourceModId);

/// <summary>Data/Objects 里的一种物品。</summary>
public sealed record ItemTemplate(string QualifiedId, string Name, string DisplayName, string Type, int Category, int Price, bool CanBeGiven, IReadOnlyList<string> ContextTags, string? SourceModId);

/// <summary>今天日程的一站。</summary>
public sealed record ScheduleStopInfo(int Time, string Location, int X, int Y, int Facing, string? EndBehavior);

public sealed record NpcScheduleInfo(string Name, string? ScheduleKey, string? CurrentLocation, int CurrentX, int CurrentY, IReadOnlyList<ScheduleStopInfo> Stops);

/// <summary>查物品的条件；没填的不筛。</summary>
public sealed class ItemQuery
{
	public int? Category { get; set; }

	public string? Type { get; set; }

	/// <summary>这些标签都要有。</summary>
	public IReadOnlyList<string>? ContextTags { get; set; }

	public int? MinPrice { get; set; }

	public int? MaxPrice { get; set; }

	public bool GiftableOnly { get; set; }

	/// <summary>只要作物的收获物（可以再限当季）。</summary>
	public bool CropProduceOnly { get; set; }

	public string? InSeason { get; set; }

	/// <summary>只要这位村民对它是这些喜好之一的（默认爱和喜欢）。</summary>
	public string? TasteNpc { get; set; }

	public IReadOnlyList<string>? Tastes { get; set; }

	public int Limit { get; set; } = 200;

	/// <summary>连关掉的 mod 的东西也要（只给调试用）。</summary>
	public bool IncludeDisabledMods { get; set; }
}

/// <summary>
/// 读游戏自己的数据（作物、果树、物品、角色、日程），所以内容包加的作物、物品、住所不用逐个适配。
/// 物品 ID 一律带前缀；作物和物品列表用到时才建，数据失效时清掉。只在主线程用。
/// </summary>
public sealed class Ecosystem
{
	private static readonly HashSet<string> MinorVanillaNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"???", "Bear", "Birdie", "Bouncer", "Fizz", "Gil", "Governor", "Grandpa", "Gunther", "Henchman", "Marlon", "Mister Qi", "Morris", "Old Mariner", "Welwick"
	};

	private readonly ILog _log;

	private readonly Func<IReadOnlyList<string>> _loadedMods;

	private readonly Func<string, string?> _npcOwner;

	private readonly Func<string?, bool> _modEnabled;

	private List<CropInfo>? _crops;

	private Dictionary<string, CropInfo>? _cropsByHarvest;

	private List<ItemTemplate>? _items;

	private Dictionary<string, ItemTemplate>? _itemsById;

	public Ecosystem(ILog log, Func<IReadOnlyList<string>> loadedMods, Func<string, string?> npcOwner, Func<string?, bool> modEnabled)
	{
		_log = log;
		_loadedMods = loadedMods;
		_npcOwner = npcOwner;
		_modEnabled = modEnabled;
	}

	/// <summary>这件东西能不能给功能用：跟着"其他 NPC mod"的设置走，游戏自带的都能用。不缓存，改设置立刻生效。</summary>
	public bool IsEnabled(ItemTemplate? item) => item != null && _modEnabled(item.SourceModId);

	public bool IsEnabled(CropInfo? crop) => crop != null && _modEnabled(crop.SourceModId);

	public IReadOnlyList<CropInfo> EnabledCrops() => Crops().Where(IsEnabled).ToList();

	public void Invalidate()
	{
		_crops = null;
		_cropsByHarvest = null;
		_items = null;
		_itemsById = null;
	}

	public void OnAssetsInvalidated(IEnumerable<IAssetName> names)
	{
		if (names.Any(n => n.IsEquivalentTo("Data/Crops") || n.IsEquivalentTo("Data/FruitTrees") || n.IsEquivalentTo("Data/Objects")))
		{
			Invalidate();
		}
	}

	// ---------------- 作物 ----------------

	public IReadOnlyList<CropInfo> Crops()
	{
		if (_crops != null)
		{
			return _crops;
		}
		List<CropInfo> list = new List<CropInfo>();
		try
		{
			foreach ((string key, CropData data) in Game1.cropData ?? new Dictionary<string, CropData>())
			{
				string? harvest = Qualify(data?.HarvestItemId);
				if (data == null || harvest == null)
				{
					continue;
				}
				ParsedItemData? parsed = ItemRegistry.GetData(harvest);
				list.Add(new CropInfo(key, Qualify(key) ?? "(O)" + key, harvest, parsed?.InternalName ?? harvest, parsed?.DisplayName ?? harvest, Seasons(data.Seasons), ObjectData(harvest)?.Price ?? 0, parsed?.Category ?? 0, data.DaysInPhase?.Sum() ?? 0, data.RegrowDays, false, SourceOf(key, harvest)));
			}
			foreach ((string key, FruitTreeData data) in Game1.fruitTreeData ?? new Dictionary<string, FruitTreeData>())
			{
				if (data?.Fruit == null)
				{
					continue;
				}
				foreach (string harvest in data.Fruit.Select(f => Qualify(f?.ItemId)).Where(id => id != null).Distinct()!)
				{
					ParsedItemData? parsed = ItemRegistry.GetData(harvest);
					list.Add(new CropInfo(key, Qualify(key) ?? "(O)" + key, harvest, parsed?.InternalName ?? harvest, parsed?.DisplayName ?? harvest, Seasons(data.Seasons), ObjectData(harvest)?.Price ?? 0, parsed?.Category ?? 0, 28, 1, true, SourceOf(key, harvest)));
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("读作物数据出错：" + ex.Message);
		}
		_crops = list;
		_cropsByHarvest = new Dictionary<string, CropInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (CropInfo crop in list)
		{
			_cropsByHarvest.TryAdd(crop.HarvestItemId, crop);
		}
		return _crops;
	}

	/// <summary>收获这件东西的作物或果树（"24"、"(O)24"、字符串 ID 都行）。</summary>
	public CropInfo? CropByProduce(string? itemId)
	{
		string? id = Qualify(itemId);
		if (id == null)
		{
			return null;
		}
		Crops();
		return _cropsByHarvest!.TryGetValue(id, out CropInfo? crop) ? crop : null;
	}

	/// <summary>东西背后的原料：加工品（果酱、果酒、腌菜、果汁、果干……）记着原料，其余就是自己。</summary>
	public static string? IngredientId(Item? item)
	{
		if (item == null)
		{
			return null;
		}
		if (item is StardewValley.Object obj && !string.IsNullOrWhiteSpace(obj.preservedParentSheetIndex.Value))
		{
			return Qualify(obj.preservedParentSheetIndex.Value) ?? "(O)" + obj.preservedParentSheetIndex.Value;
		}
		return item.QualifiedItemId;
	}

	public CropInfo? CropForItem(Item? item) => CropByProduce(IngredientId(item));

	// ---------------- 物品 ----------------

	public IReadOnlyList<ItemTemplate> Items()
	{
		if (_items != null)
		{
			return _items;
		}
		List<ItemTemplate> list = new List<ItemTemplate>();
		try
		{
			foreach ((string key, ObjectData data) in Game1.objectData ?? new Dictionary<string, ObjectData>())
			{
				if (data == null)
				{
					continue;
				}
				string id = "(O)" + key;
				string display;
				try
				{
					display = ItemRegistry.GetData(id)?.DisplayName ?? data.Name ?? key;
				}
				catch
				{
					display = data.Name ?? key;
				}
				list.Add(new ItemTemplate(id, data.Name ?? key, display, data.Type ?? "", data.Category, data.Price, data.CanBeGivenAsGift, (IReadOnlyList<string>?)data.ContextTags ?? Array.Empty<string>(), SourceOf(key, id)));
			}
		}
		catch (Exception ex)
		{
			_log.Warn("读物品数据出错：" + ex.Message);
		}
		_items = list;
		_itemsById = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
		foreach (ItemTemplate t in list)
		{
			_itemsById.TryAdd(t.QualifiedId, t);
		}
		return _items;
	}

	public ItemTemplate? Item(string? itemId)
	{
		string? id = Qualify(itemId);
		if (id == null)
		{
			return null;
		}
		Items();
		return _itemsById!.TryGetValue(id, out ItemTemplate? t) ? t : null;
	}

	public IReadOnlyList<ItemTemplate> FindItems(ItemQuery q)
	{
		IEnumerable<ItemTemplate> items = Items().Where(t => !IsJunk(t) && (q.IncludeDisabledMods || IsEnabled(t)));
		if (q.Category.HasValue)
		{
			items = items.Where(t => t.Category == q.Category.Value);
		}
		if (!string.IsNullOrWhiteSpace(q.Type))
		{
			items = items.Where(t => string.Equals(t.Type, q.Type, StringComparison.OrdinalIgnoreCase));
		}
		if (q.ContextTags is { Count: > 0 })
		{
			items = items.Where(t => q.ContextTags.All(tag => t.ContextTags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
		}
		if (q.MinPrice.HasValue)
		{
			items = items.Where(t => t.Price >= q.MinPrice.Value);
		}
		if (q.MaxPrice.HasValue)
		{
			items = items.Where(t => t.Price <= q.MaxPrice.Value);
		}
		if (q.GiftableOnly)
		{
			items = items.Where(t => t.CanBeGiven);
		}
		if (q.CropProduceOnly || !string.IsNullOrWhiteSpace(q.InSeason))
		{
			string? season = q.InSeason?.Trim().ToLowerInvariant();
			items = items.Where(t => CropByProduce(t.QualifiedId) is CropInfo crop && (season == null || crop.Seasons.Contains(season)));
		}
		if (!string.IsNullOrWhiteSpace(q.TasteNpc))
		{
			NPC? npc = FindNpc(q.TasteNpc);
			if (npc == null)
			{
				return Array.Empty<ItemTemplate>();
			}
			HashSet<string> tastes = new HashSet<string>(q.Tastes ?? new[] { "loved", "liked" }, StringComparer.OrdinalIgnoreCase);
			items = items.Where(t => tastes.Contains(Taste(npc, t.QualifiedId) ?? ""));
		}
		return items.Take(Math.Max(1, q.Limit)).ToList();
	}

	/// <summary>loved / liked / neutral / disliked / hated；东西造不出来时为空。</summary>
	public static string? Taste(NPC npc, string? itemId)
	{
		string? id = Qualify(itemId);
		if (npc == null || id == null)
		{
			return null;
		}
		try
		{
			if (ItemRegistry.Create(id, 1, 0, allowNull: true) is not StardewValley.Object obj)
			{
				return null;
			}
			return npc.getGiftTasteForThisItem(obj) switch
			{
				NPC.gift_taste_love or NPC.gift_taste_stardroptea => "loved",
				NPC.gift_taste_like => "liked",
				NPC.gift_taste_dislike => "disliked",
				NPC.gift_taste_hate => "hated",
				_ => "neutral"
			};
		}
		catch
		{
			return null;
		}
	}

	/// <summary>这位村民在喜好表里亲自列出的东西（不含大家共有的喜好；跳过类别和标签）。</summary>
	public IReadOnlyList<ItemTemplate> PersonalTastes(string? npcName, string taste)
	{
		int field = taste?.ToLowerInvariant() switch
		{
			"loved" => 1,
			"liked" => 3,
			"disliked" => 5,
			"hated" => 7,
			"neutral" => 9,
			_ => -1
		};
		if (string.IsNullOrWhiteSpace(npcName) || field < 0 || Game1.NPCGiftTastes == null || !Game1.NPCGiftTastes.TryGetValue(npcName.Trim(), out string? raw) || string.IsNullOrWhiteSpace(raw))
		{
			return Array.Empty<ItemTemplate>();
		}
		string[] parts = raw.Split('/');
		if (parts.Length <= field)
		{
			return Array.Empty<ItemTemplate>();
		}
		List<ItemTemplate> list = new List<ItemTemplate>();
		foreach (string token in parts[field].Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (int.TryParse(token, out int n) && n < 0)
			{
				continue;
			}
			ItemTemplate? t = Item(token);
			if (t != null && IsEnabled(t) && !list.Contains(t))
			{
				list.Add(t);
			}
		}
		return list;
	}

	// ---------------- 住所和日程 ----------------

	public NpcHomeInfo? Home(string? npcName)
	{
		if (string.IsNullOrWhiteSpace(npcName))
		{
			return null;
		}
		string name = npcName.Trim();
		CharacterData? data = null;
		try
		{
			NPC.TryGetData(name, out data);
		}
		catch
		{
		}
		NPC? npc = FindNpc(name);
		if (data == null && npc == null)
		{
			return null;
		}
		string display = DisplayName(name, npc);
		bool vanilla = BuiltInPersonas.HasVanilla(name) || MinorVanillaNpcs.Contains(name);
		string? owner = vanilla ? null : _npcOwner(name);
		if (npc != null && npc.isMarried() && npc.getSpouse() != null)
		{
			return MakeHome(name, display, "FarmHouse", 0, 0, 2, "spouse", npc, vanilla, owner);
		}
		if (data?.Home is { Count: > 0 })
		{
			try
			{
				if (NPC.ReadNpcHomeData(data, npc?.currentLocation, out string loc, out Point tile, out int dir) && !string.IsNullOrWhiteSpace(loc))
				{
					return MakeHome(name, display, loc, tile.X, tile.Y, dir, "home-data", npc, vanilla, owner);
				}
			}
			catch (Exception ex)
			{
				_log.Trace($"读 {name} 的住所出错：{ex.Message}");
			}
		}
		if (npc != null && !string.IsNullOrWhiteSpace(npc.DefaultMap))
		{
			Vector2 pos = npc.DefaultPosition / 64f;
			return MakeHome(name, display, npc.DefaultMap, (int)pos.X, (int)pos.Y, npc.DefaultFacingDirection, "default-map", npc, vanilla, owner);
		}
		return MakeHome(name, display, "Town", 29, 67, 2, "fallback", npc, vanilla, owner);
	}

	/// <summary>Data/Characters 里每个人的住所（包括还没出场的）。</summary>
	public IReadOnlyList<NpcHomeInfo> AllHomes() =>
		(Game1.characterData?.Keys ?? Enumerable.Empty<string>()).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(Home).Where(h => h != null).ToList()!;

	/// <summary>今天的日程和每站的目标格子（今天没日程时 Stops 为空）。</summary>
	public NpcScheduleInfo? TodaySchedule(string? npcName)
	{
		NPC? npc = FindNpc(npcName);
		if (npc == null)
		{
			return null;
		}
		List<ScheduleStopInfo> stops = new List<ScheduleStopInfo>();
		try
		{
			foreach ((int time, SchedulePathDescription stop) in (npc.Schedule ?? new Dictionary<int, SchedulePathDescription>()).OrderBy(p => p.Key))
			{
				if (stop != null && !string.IsNullOrWhiteSpace(stop.targetLocationName))
				{
					stops.Add(new ScheduleStopInfo(time, stop.targetLocationName, stop.targetTile.X, stop.targetTile.Y, stop.facingDirection, string.IsNullOrWhiteSpace(stop.endOfRouteBehavior) ? null : stop.endOfRouteBehavior));
				}
			}
		}
		catch (Exception ex)
		{
			_log.Trace($"读 {npc.Name} 的日程出错：{ex.Message}");
		}
		string? key = null;
		try
		{
			key = npc.ScheduleKey;
		}
		catch
		{
		}
		return new NpcScheduleInfo(npc.Name, key, npc.currentLocation?.Name, (int)npc.Tile.X, (int)npc.Tile.Y, stops);
	}

	// ---------------- 调试 ----------------

	/// <summary>aisv_eco [npc 名字 | item ID]；结果写到 debug/ 下。</summary>
	public string Command(string[] args, Func<string, object, string> write)
	{
		string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
		string arg = string.Join(" ", args.Skip(1)).Trim();
		try
		{
			return mode switch
			{
				"npc" => write("ecosystem-npc.json", DumpNpc(arg)),
				"item" => write("ecosystem-item.json", DumpItem(arg)),
				_ => $"作物 {Crops().Count} 种（可用 {EnabledCrops().Count}），物品 {Items().Count} 种（可用 {Items().Count(IsEnabled)}），住所 {AllHomes().Count} 处；" + write("ecosystem-dump.json", DumpAll())
			};
		}
		catch (Exception ex)
		{
			return "aisv_eco 出错：" + ex.Message + "\n用法：aisv_eco | npc <名字> | item <ID 或 wine:(O)398>";
		}
	}

	private object DumpAll()
	{
		IReadOnlyList<CropInfo> crops = Crops();
		IReadOnlyList<NpcHomeInfo> homes = AllHomes();
		IReadOnlyList<ItemTemplate> items = Items();
		List<NpcScheduleInfo> schedules = Utility.getAllVillagers().Where(n => !string.IsNullOrWhiteSpace(n?.Name)).Select(n => TodaySchedule(n.Name)).Where(x => x != null).OrderBy(x => x!.Name, StringComparer.OrdinalIgnoreCase).ToList()!;
		static Dictionary<string, int> By<T>(IEnumerable<T> list, Func<T, string> key) => list.GroupBy(key).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
		return new
		{
			Date = $"{Game1.currentSeason} {Game1.dayOfMonth} Y{Game1.year} {Game1.timeOfDay}",
			Counts = new
			{
				Crops = crops.Count(c => !c.IsFruitTree),
				FruitTrees = crops.Count(c => c.IsFruitTree),
				CropsBySource = By(crops, c => c.SourceModId ?? "vanilla"),
				EnabledCropsBySource = By(EnabledCrops(), c => c.SourceModId ?? "vanilla"),
				Homes = homes.Count,
				HomesBySource = By(homes, h => h.IsVanilla ? "vanilla" : h.SourceModId ?? "unknown"),
				HomesByKind = By(homes, h => h.Source),
				Items = items.Count,
				ItemsBySource = By(items, i => i.SourceModId ?? "vanilla"),
				EnabledItemsBySource = By(items.Where(IsEnabled), i => i.SourceModId ?? "vanilla"),
				SchedulesWithStops = schedules.Count(x => x.Stops.Count > 0),
				SchedulesInWorld = schedules.Count
			},
			Crops = crops,
			Homes = homes,
			Schedules = schedules,
			ModItemsSample = items.Where(i => i.SourceModId != null).GroupBy(i => i.SourceModId!).ToDictionary(g => g.Key, g => g.Take(8).Select(i => new { i.QualifiedId, i.DisplayName, i.Category, i.Price }))
		};
	}

	private object DumpNpc(string name)
	{
		static object Brief(ItemTemplate i) => new { i.QualifiedId, i.DisplayName, i.SourceModId };
		bool known = FindNpc(name) != null;
		return new
		{
			Name = name,
			Home = Home(name),
			Schedule = TodaySchedule(name),
			PersonalLoved = PersonalTastes(name, "loved").Select(Brief),
			PersonalLiked = PersonalTastes(name, "liked").Select(Brief),
			Loved = known ? FindItems(new ItemQuery { GiftableOnly = true, TasteNpc = name, Tastes = new[] { "loved" }, Limit = 15 }).Select(Brief) : null,
			Liked = known ? FindItems(new ItemQuery { GiftableOnly = true, TasteNpc = name, Tastes = new[] { "liked" }, Limit = 15 }).Select(Brief) : null
		};
	}

	private object DumpItem(string id)
	{
		Item? item = null;
		try
		{
			int colon = id.IndexOf(':');
			if (colon > 0 && Enum.TryParse(id[..colon], true, out StardewValley.Object.PreserveType preserve))
			{
				id = id[(colon + 1)..];
				if (ItemRegistry.Create(id, 1, 0, allowNull: true) is StardewValley.Object ingredient)
				{
					item = ItemRegistry.GetObjectTypeDefinition().CreateFlavoredItem(preserve, ingredient);
				}
			}
			else
			{
				item = ItemRegistry.Create(id, 1, 0, allowNull: true);
			}
		}
		catch
		{
		}
		ItemTemplate? template = Item(item?.QualifiedItemId ?? id);
		return new
		{
			Input = id,
			Created = item == null ? null : new { item.QualifiedItemId, item.DisplayName },
			Ingredient = IngredientId(item),
			Template = template,
			Enabled = IsEnabled(template),
			Crop = CropForItem(item) ?? CropByProduce(id)
		};
	}

	// ---------------- 小工具 ----------------

	/// <summary>"24"、"(O)24"、字符串 ID → 带前缀的 ID；游戏不认识时为空。</summary>
	public static string? Qualify(string? itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return null;
		}
		try
		{
			return ItemRegistry.QualifyItemId(itemId.Trim());
		}
		catch
		{
			return null;
		}
	}

	/// <summary>键或 ID（去掉前缀）以哪个已装 mod 的 UniqueID 开头、后面跟 "_" 或 "."（取最长的）；游戏自带或认不出为空。</summary>
	public string? SourceOf(params string?[] ids) => ContentOrigin.Of(_loadedMods(), ids);

	private static ObjectData? ObjectData(string qualifiedId) =>
		qualifiedId.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) && Game1.objectData != null && Game1.objectData.TryGetValue(qualifiedId[3..], out ObjectData? d) ? d : null;

	private static IReadOnlyList<string> Seasons(IEnumerable<Season>? seasons) => seasons?.Select(s => s.ToString().ToLowerInvariant()).Distinct().ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

	/// <summary>杂项内部物品不该被挑到。</summary>
	private static bool IsJunk(ItemTemplate t) =>
		(t.Category == 0 && string.Equals(t.Type, "Quest", StringComparison.OrdinalIgnoreCase))
		|| string.Equals(t.Type, "Litter", StringComparison.OrdinalIgnoreCase)
		|| (t.Name.Contains("Error", StringComparison.OrdinalIgnoreCase) && t.Name.Contains("Item", StringComparison.OrdinalIgnoreCase))
		|| t.Name == "???";

	private static NpcHomeInfo MakeHome(string name, string display, string loc, int x, int y, int dir, string source, NPC? npc, bool vanilla, string? owner)
	{
		string locDisplay = loc;
		bool exists = false;
		try
		{
			GameLocation? location = Game1.getLocationFromName(loc);
			exists = location != null;
			locDisplay = location?.DisplayName ?? loc;
		}
		catch
		{
		}
		return new NpcHomeInfo(name, display, loc, locDisplay, x, y, dir, source, exists, npc != null, vanilla, owner);
	}

	private static NPC? FindNpc(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		try
		{
			return Game1.getCharacterFromName(name.Trim(), mustBeVillager: false, includeEventActors: false);
		}
		catch
		{
			return null;
		}
	}

	private static string DisplayName(string name, NPC? npc)
	{
		try
		{
			return npc?.displayName ?? NPC.GetDisplayName(name) ?? name;
		}
		catch
		{
			return name;
		}
	}
}
