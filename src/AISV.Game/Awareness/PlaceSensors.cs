using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Game.Approach;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Shops;
using StardewValley.Locations;
using StardewValley.Menus;
using xTile.Layers;

namespace AISV.Game.Awareness;

/// <summary>村民的家（来自 Data/Characters 的 Home）。商店、诊所这类公共场所不算。</summary>
internal static class Homes
{
	private static readonly HashSet<string> Public = new[]
	{
		"Town Beach Mountain Forest Railroad Desert Sewer",
		"BathHouse_Entry BathHouse_Pool BathHouse_MensLocker BathHouse_WomensLocker",
		"CommunityCenter ArchaeologyHouse MovieTheater AdventureGuild WizardHouse Club",
		"SeedShop JojaMart Saloon Hospital Blacksmith ScienceHouse AnimalShop FishShop SandyHouse"
	}.SelectMany(row => row.Split(' ')).ToHashSet(StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, List<string>>? _owners;

	public static void Reset() => _owners = null;

	public static IReadOnlyList<string> OwnersOf(string? location)
	{
		if (string.IsNullOrWhiteSpace(location))
		{
			return Array.Empty<string>();
		}
		_owners ??= Build();
		return _owners.TryGetValue(location, out List<string>? list) ? list : Array.Empty<string>();
	}

	public static bool IsPrivateHome(GameLocation? l)
	{
		try
		{
			return l != null && !l.IsFarm && l is not FarmHouse && !l.IsOutdoors && !string.IsNullOrWhiteSpace(l.Name) && !Public.Contains(l.Name) && OwnersOf(l.Name).Count > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static string? OwnerPresent(GameLocation l, IReadOnlyList<string> owners) =>
		owners.Count == 0 ? null : owners.FirstOrDefault(o => l.getCharacterFromName(o) != null) ?? owners[0];

	private static Dictionary<string, List<string>> Build()
	{
		Dictionary<string, List<string>> d = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach ((string name, CharacterData data) in Game1.characterData ?? new Dictionary<string, CharacterData>())
		{
			foreach (CharacterHomeData home in data?.Home ?? new List<CharacterHomeData>())
			{
				if (string.IsNullOrWhiteSpace(home?.Location))
				{
					continue;
				}
				if (!d.TryGetValue(home.Location, out List<string>? list))
				{
					list = d[home.Location] = new List<string>();
				}
				if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(name);
				}
			}
		}
		return d;
	}
}

/// <summary>按地图上的 "Door 主人" 推出卧室范围：门后面那片走不出去的小区域。</summary>
internal sealed class RoomIndex
{
	public sealed record Room(string Location, Point Door, IReadOnlyList<string> Owners, HashSet<Point> Tiles);

	private const int MaxRoomTiles = 900;

	private const int MinRoomTiles = 4;

	private readonly Dictionary<string, List<Room>> _byLocation = new Dictionary<string, List<Room>>(StringComparer.OrdinalIgnoreCase);

	public void Clear() => _byLocation.Clear();

	public IReadOnlyList<Room> RoomsOf(GameLocation l) => For(l);

	public Room? Find(GameLocation? l, Point tile) => l == null || l.IsOutdoors ? null : For(l).FirstOrDefault(r => r.Tiles.Contains(tile));

	/// <summary>玩家一进门就站在某间"卧室"里，说明那其实是入口区域，不算卧室。</summary>
	public void ForgetRoomsWithEntrance(GameLocation l, Point arrival)
	{
		if (!l.IsOutdoors)
		{
			For(l);
			_byLocation[l.NameOrUniqueName].RemoveAll(r => r.Tiles.Contains(arrival));
		}
	}

	private List<Room> For(GameLocation l)
	{
		if (!_byLocation.TryGetValue(l.NameOrUniqueName, out List<Room>? rooms))
		{
			rooms = _byLocation[l.NameOrUniqueName] = Build(l);
		}
		return rooms;
	}

	private static List<Room> Build(GameLocation l)
	{
		List<Room> rooms = new List<Room>();
		if (l.IsOutdoors || l.Map == null || string.Equals(l.Name, "Hospital", StringComparison.OrdinalIgnoreCase))
		{
			return rooms;
		}
		Layer? back = l.Map.GetLayer("Back");
		Layer? buildings = l.Map.GetLayer("Buildings");
		if (back == null || buildings == null)
		{
			return rooms;
		}
		HashSet<Point> exits = new HashSet<Point>();
		foreach (Warp w in l.warps)
		{
			Area(exits, new Point(w.X, w.Y), 2);
		}
		foreach (Point door in l.doors.Keys)
		{
			Area(exits, door, 2);
		}
		List<(Point Tile, string[] Owners)> doors = new List<(Point, string[])>();
		for (int x = 0; x < back.LayerWidth; x++)
		{
			for (int y = 0; y < back.LayerHeight; y++)
			{
				string? action = l.doesTileHaveProperty(x, y, "Action", "Buildings");
				string? touch = l.doesTileHaveProperty(x, y, "TouchAction", "Back");
				if (IsWarp(action) || IsWarp(touch))
				{
					Area(exits, new Point(x, y), 2);
				}
				string? text = action ?? touch;
				if (text != null && text.StartsWith("Door ", StringComparison.Ordinal))
				{
					string[] owners = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
					if (owners.Length > 0)
					{
						doors.Add((new Point(x, y), owners));
					}
				}
			}
		}
		foreach ((Point door, string[] owners) in doors)
		{
			if (Behind(l, back, buildings, door, exits, owners) is Room room && !rooms.Any(r => r.Owners.SequenceEqual(room.Owners) && r.Tiles.SetEquals(room.Tiles)))
			{
				rooms.Add(room);
			}
		}
		return rooms;
	}

	private static bool IsWarp(string? a) => a != null && (a.StartsWith("Warp", StringComparison.Ordinal) || a.StartsWith("LockedDoorWarp", StringComparison.Ordinal) || a.StartsWith("MagicWarp", StringComparison.Ordinal));

	private static Room? Behind(GameLocation l, Layer back, Layer buildings, Point door, HashSet<Point> exits, string[] owners)
	{
		HashSet<Point> walked = new HashSet<Point>();
		List<(HashSet<Point> Tiles, bool Capped)> sides = new List<(HashSet<Point>, bool)>();
		foreach (Point start in Neighbors(door))
		{
			if (walked.Contains(start) || !Walkable(back, buildings, start))
			{
				continue;
			}
			HashSet<Point> side = Flood(back, buildings, start, door, out bool capped);
			walked.UnionWith(side);
			sides.Add((side, capped));
		}
		if (sides.Count < 2)
		{
			return null;
		}
		HashSet<Point>? smallest = sides.Where(s => !s.Capped && s.Tiles.Count >= MinRoomTiles && !s.Tiles.Overlaps(exits)).Select(s => s.Tiles).OrderBy(t => t.Count).FirstOrDefault();
		return smallest == null ? null : new Room(l.NameOrUniqueName, door, owners, smallest);
	}

	private static HashSet<Point> Flood(Layer back, Layer buildings, Point start, Point door, out bool capped)
	{
		capped = false;
		HashSet<Point> tiles = new HashSet<Point> { start };
		Stack<Point> pending = new Stack<Point>();
		pending.Push(start);
		while (pending.Count > 0)
		{
			foreach (Point next in Neighbors(pending.Pop()))
			{
				if (next != door && Walkable(back, buildings, next) && tiles.Add(next))
				{
					if (tiles.Count > MaxRoomTiles)
					{
						capped = true;
						return tiles;
					}
					pending.Push(next);
				}
			}
		}
		return tiles;
	}

	private static bool Walkable(Layer back, Layer buildings, Point p) =>
		p.X >= 0 && p.Y >= 0 && p.X < back.LayerWidth && p.Y < back.LayerHeight && back.Tiles[p.X, p.Y] != null && buildings.Tiles[p.X, p.Y] == null;

	private static IEnumerable<Point> Neighbors(Point p)
	{
		yield return new Point(p.X, p.Y - 1);
		yield return new Point(p.X, p.Y + 1);
		yield return new Point(p.X - 1, p.Y);
		yield return new Point(p.X + 1, p.Y);
	}

	private static void Area(HashSet<Point> set, Point c, int r)
	{
		for (int i = -r; i <= r; i++)
		{
			for (int j = -r; j <= r; j++)
			{
				set.Add(new Point(c.X + i, c.Y + j));
			}
		}
	}
}

/// <summary>进出卧室、进出建筑、换区域、逛店、室内村民到场。</summary>
internal sealed class PlaceSensors
{
	private const int RoomSampleTicks = 15;

	private const int MentionableStayMinutes = 10;

	private readonly AwarenessSystem _system;

	private readonly Func<bool> _npcArrivals;

	private readonly ILog _log;

	private readonly RoomIndex _rooms = new RoomIndex();

	private RoomIndex.Room? _room;

	private string? _roomEventId;

	private int _roomEntered;

	private int _tick;

	private string? _arrivedIn;

	private bool _skipArrival = true;

	private string? _lastLocation;

	private Vector2 _lastTile;

	private bool _eventLast;

	private string? _previousLocation;

	private Vector2 _previousTile;

	private bool _eventBeforeChange;

	private ShopMenu? _shop;

	private string? _shopOwner;

	private List<(string Name, int Count)> _bought = new List<(string, int)>();

	private HashSet<string> _boughtIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public PlaceSensors(AwarenessSystem system, Func<bool> npcArrivals, ILog log)
	{
		_system = system;
		_npcArrivals = npcArrivals;
		_log = log;
	}

	public void Reset()
	{
		_room = null;
		_roomEventId = null;
		_tick = 0;
		_arrivedIn = null;
		_skipArrival = true;
		ResetShop();
		Homes.Reset();
	}

	public void Clear()
	{
		Reset();
		_rooms.Clear();
	}

	public string DescribeRooms(GameLocation l)
	{
		IReadOnlyList<RoomIndex.Room> rooms = _rooms.RoomsOf(l);
		return rooms.Count == 0 ? l.NameOrUniqueName + " 没有卧室。" : l.NameOrUniqueName + "：" + string.Join("；", rooms.Select(r => $"{string.Join("、", r.Owners)} 的门在 {r.Door.X},{r.Door.Y}，{r.Tiles.Count} 格，比如 {r.Tiles.First().X},{r.Tiles.First().Y}"));
	}

	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || !Context.IsMainPlayer)
		{
			return;
		}
		TrackPosition();
		if (++_tick % RoomSampleTicks != 0)
		{
			return;
		}
		try
		{
			Farmer player = Game1.player;
			GameLocation? l = player?.currentLocation;
			if (l != null)
			{
				NoteArrival(l, player!);
			}
			RoomIndex.Room? room = l == null || Game1.eventUp ? null : _rooms.Find(l, player!.TilePoint);
			if (room != _room)
			{
				if (_room != null)
				{
					LeaveRoom();
				}
				_room = room;
				if (room != null)
				{
					EnterRoom(l!, room, player!);
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到（卧室）出错：" + ex.Message);
			_room = null;
			_roomEventId = null;
		}
	}

	private void NoteArrival(GameLocation l, Farmer player)
	{
		if (Game1.eventUp)
		{
			_skipArrival = true;
			return;
		}
		if (!string.Equals(l.NameOrUniqueName, _arrivedIn, StringComparison.OrdinalIgnoreCase))
		{
			_arrivedIn = l.NameOrUniqueName;
			if (!_skipArrival)
			{
				_rooms.ForgetRoomsWithEntrance(l, player.TilePoint);
			}
		}
		_skipArrival = false;
	}

	private void EnterRoom(GameLocation l, RoomIndex.Room room, Farmer player)
	{
		_roomEventId = null;
		_roomEntered = AwarenessSystem.NowMinute;
		string? present = room.Owners.FirstOrDefault(o => l.getCharacterFromName(o) != null);
		List<string> agg = new List<string>();
		if (Game1.timeOfDay >= 2000)
		{
			agg.Add("roomAtNight");
		}
		if (present != null)
		{
			agg.Add("roomOwnerPresent");
		}
		Dictionary<string, string> d = Observation.Map(("owners", string.Join(",", room.Owners)));
		if (agg.Count > 0)
		{
			d["aggravators"] = string.Join(",", agg);
		}
		string farmer = ActionPatches.Name(player);
		string owners = room.Owners.Count == 1 ? room.Owners[0] : string.Join("和", room.Owners);
		_roomEventId = _system.Record(new Observation("room", farmer, present ?? room.Owners[0], l.NameOrUniqueName, $"{farmer}走进了{owners}的卧室。", player.Tile, d))?.EventId;
	}

	private void LeaveRoom()
	{
		int minutes = Math.Max(0, AwarenessSystem.NowMinute - _roomEntered);
		if (_roomEventId != null && minutes >= MentionableStayMinutes)
		{
			_system.OnRoomLeft(_roomEventId, minutes);
		}
		_roomEventId = null;
	}

	private void TrackPosition()
	{
		Farmer player = Game1.player;
		if (player?.currentLocation == null)
		{
			return;
		}
		string here = player.currentLocation.NameOrUniqueName;
		if (_lastLocation != null && here != _lastLocation)
		{
			_previousLocation = _lastLocation;
			_previousTile = _lastTile;
			_eventBeforeChange = _eventLast;
		}
		_lastLocation = here;
		_lastTile = player.Tile;
		_eventLast = Game1.eventUp;
	}

	/// <summary>进出建筑记 place（门口两侧的人都算），换户外区域记 map。自己家和农场之间、过场里的不算。</summary>
	public void OnWarped(WarpedEventArgs e)
	{
		if (!e.IsLocalPlayer || !Context.IsMainPlayer || e.OldLocation == null || e.NewLocation == null)
		{
			return;
		}
		try
		{
			GameLocation from = e.OldLocation;
			GameLocation to = e.NewLocation;
			bool eventBefore = _lastLocation == from.NameOrUniqueName ? _eventLast : _eventBeforeChange;
			if (eventBefore || Game1.isFestival() || (PlayerOwned(from) && PlayerOwned(to)))
			{
				return;
			}
			bool enter = from.IsOutdoors && !to.IsOutdoors;
			bool leave = !from.IsOutdoors && to.IsOutdoors;
			string type = enter || leave ? "place" : "map";
			Vector2 exit = _lastLocation == from.NameOrUniqueName ? _lastTile : _previousLocation == from.NameOrUniqueName ? _previousTile : e.Player.Tile;
			Vector2 arrive = e.Player.Tile;
			GameLocation primary = leave || type == "map" ? from : to;
			Vector2 primaryTile = primary == from ? exit : arrive;
			GameLocation other = primary == from ? to : from;
			Vector2 otherTile = primary == from ? arrive : exit;
			string farmer = ActionPatches.Name(e.Player);
			Dictionary<string, string> d = Observation.Map(("action", enter ? "enter" : leave ? "leave" : "move"), ("from", from.NameOrUniqueName), ("to", to.NameOrUniqueName),
				("fromDisplay", AwarenessSystem.PlaceName(from)), ("toDisplay", AwarenessSystem.PlaceName(to)));
			string? subject = null;
			string summary;
			if (type == "place")
			{
				GameLocation building = enter ? to : from;
				d["place"] = building.NameOrUniqueName;
				d["placeDisplay"] = AwarenessSystem.PlaceName(building);
				IReadOnlyList<string> owners = Homes.OwnersOf(building.Name);
				if (owners.Count > 0)
				{
					d["owners"] = string.Join(",", owners);
					d["aggravators"] = "placeNpcHome";
					subject = owners[0];
				}
				summary = enter ? $"{farmer}走进了{d["placeDisplay"]}。" : $"{farmer}从{d["placeDisplay"]}出来。";
			}
			else
			{
				summary = $"{farmer}从{d["fromDisplay"]}去了{d["toDisplay"]}。";
			}
			_system.Record(new Observation(type, farmer, subject, primary.NameOrUniqueName, summary, primaryTile, d, other.NameOrUniqueName, otherTile));
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到（换地方）出错：" + ex.Message);
		}
	}

	private static bool PlayerOwned(GameLocation l) => l.IsFarm || l is FarmHouse or IslandFarmHouse || l.ParentBuilding != null;

	/// <summary>室内的村民记下谁进来了（设置里开了才记）。</summary>
	public void OnNpcListChanged(NpcListChangedEventArgs e)
	{
		if (!_npcArrivals() || !Context.IsWorldReady || !Context.IsMainPlayer || Game1.eventUp || Game1.timeOfDay <= 610 || e.Location == null || e.Location.IsOutdoors)
		{
			return;
		}
		try
		{
			foreach (NPC npc in e.Added.Where(ApproachCoordinator.IsVillager))
			{
				string place = AwarenessSystem.PlaceName(e.Location);
				_system.Record(new Observation("npc_move", npc.Name, null, e.Location.NameOrUniqueName, $"{npc.Name}来到了{place}。", npc.Tile,
					Observation.Map(("action", "arrive"), ("placeDisplay", place)), Exclude: new[] { npc.Name }));
			}
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到（村民到场）出错：" + ex.Message);
		}
	}

	// ---- 逛店：关店时把这次买的合成一条 ----

	public void OnMenuChanged(IClickableMenu? oldMenu, IClickableMenu? newMenu)
	{
		try
		{
			if (_shop != null && _shop != newMenu)
			{
				FlushShop();
			}
			if (newMenu is ShopMenu shop && _shop != shop)
			{
				BeginShop(shop);
			}
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到（买东西）出错：" + ex.Message);
			ResetShop();
		}
	}

	private void BeginShop(ShopMenu shop)
	{
		if (!_system.Enabled || string.IsNullOrWhiteSpace(shop.ShopId) || !DataLoader.Shops(Game1.content).TryGetValue(shop.ShopId, out ShopData? data) || data == null)
		{
			return;
		}
		GameLocation here = Game1.currentLocation;
		_shopOwner = data.Owners?.Select(o => o?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && here?.getCharacterFromName(n) != null);
		_shop = shop;
		_bought = new List<(string, int)>();
		_boughtIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<(string Name, int Count)> bought = _bought;
		HashSet<string> ids = _boughtIds;
		ShopMenu.OnPurchaseDelegate inner = shop.onPurchase;
		shop.onPurchase = (item, who, count, stock) =>
		{
			try
			{
				if ((stock == null || stock.Price > 0 || !string.IsNullOrWhiteSpace(stock.TradeItem)) && ActionPatches.Local(who) && !string.IsNullOrWhiteSpace(item?.DisplayName) && count > 0)
				{
					int i = bought.FindIndex(b => b.Name == item.DisplayName);
					if (i >= 0)
					{
						bought[i] = (item.DisplayName, bought[i].Count + count);
					}
					else
					{
						bought.Add((item.DisplayName, count));
					}
					if (!string.IsNullOrWhiteSpace(item.QualifiedItemId))
					{
						ids.Add(item.QualifiedItemId);
					}
				}
			}
			catch (Exception)
			{
			}
			return inner != null && inner(item, who, count, stock);
		};
	}

	private void FlushShop()
	{
		List<(string Name, int Count)> bought = _bought;
		HashSet<string> ids = _boughtIds;
		string? owner = _shopOwner;
		ResetShop();
		if (bought.Count == 0 || !_system.Enabled)
		{
			return;
		}
		GameLocation here = Game1.currentLocation;
		string farmer = ActionPatches.Name(Game1.player);
		string items = Describe(bought);
		string where = string.IsNullOrWhiteSpace(owner) ? "在" + AwarenessSystem.PlaceName(here) + "的店里" : "从" + owner + "那儿";
		_system.Record(new Observation("purchase", farmer, owner, here?.NameOrUniqueName ?? "", $"{farmer}{where}买了{items}。", Game1.player?.Tile,
			Observation.Map(("items", items), ("itemIds", string.Join(",", ids)))));
	}

	/// <summary>"20 个啤酒、沙拉和另外 2 样"：按数量排，最多列 4 样。</summary>
	public static string Describe(IReadOnlyList<(string Name, int Count)> bought, int max = 4)
	{
		List<string> parts = bought.Select((b, i) => (b.Name, b.Count, i)).OrderByDescending(b => b.Count).ThenBy(b => b.i)
			.Take(Math.Max(1, max)).Select(b => b.Count > 1 ? $"{b.Count} 个{b.Name}" : b.Name).ToList();
		int rest = bought.Count - parts.Count;
		if (rest > 0)
		{
			return string.Join("、", parts) + $"和另外 {rest} 样";
		}
		return parts.Count == 1 ? parts[0] : string.Join("、", parts.Take(parts.Count - 1)) + "和" + parts[^1];
	}

	private void ResetShop()
	{
		_shop = null;
		_shopOwner = null;
		_bought = new List<(string, int)>();
		_boughtIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}
}
