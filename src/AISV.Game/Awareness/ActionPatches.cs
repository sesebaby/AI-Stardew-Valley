using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AISV.Core.Diagnostics;
using AISV.Game.Approach;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Projectiles;
using StardewValley.Tools;

namespace AISV.Game.Awareness;

/// <summary>
/// 玩家动作的钩子：翻垃圾桶、开卧室门、挥工具和武器、浇水、打弹弓、求婚、昏倒、吃喝、爆炸、在别人家坐下、用电器、翻东西。
/// 都挂在动作发生的那一刻（不是每帧），哪个方法找不到就只关掉那一个。
/// </summary>
internal static class ActionPatches
{
	private const long HitCooldownTicks = 1800;

	private const long DoorCooldownTicks = 3600;

	private const long HomeCooldownTicks = 3600;

	private const long SnoopCooldownTicks = 1800;

	private static readonly HashSet<string> SnoopActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Message", "MessageOnce", "Notes", "NotesOnce", "Letter", "Dialogue", "MessageSpeech" };

	private static readonly MethodInfo? TilesAffected = AccessTools.Method(typeof(Tool), "tilesAffected", new[] { typeof(Vector2), typeof(int), typeof(Farmer) });

	private static readonly FieldInfo? ItemToEat = AccessTools.Field(typeof(Farmer), "itemToEat");

	private static readonly Dictionary<string, int> DoorAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private static AwarenessSystem? _system;

	private static ILog _log = NullLog.Instance;

	/// <summary>本机农夫弄出的爆炸：地点、中心格、半径。</summary>
	public static event Action<GameLocation, Vector2, int>? Exploded;

	public static void Apply(Harmony harmony, AwarenessSystem system, ILog log)
	{
		_system = system;
		_log = log;
		Patch(harmony, "翻垃圾桶", Method(typeof(GameLocation), "CheckGarbage"), nameof(Garbage_Prefix), nameof(Garbage_Postfix));
		Patch(harmony, "锁着的门", Method(typeof(GameLocation), "ShowLockedDoorMessage"), null, nameof(LockedDoor_Postfix));
		Patch(harmony, "开门", Method(typeof(GameLocation), "performAction", typeof(string[])), nameof(Action_Prefix), nameof(Action_Postfix));
		Patch(harmony, "踩门", Method(typeof(GameLocation), "performTouchAction", typeof(string[])), nameof(Door_Prefix), nameof(Door_Postfix));
		Patch(harmony, "工具", Method(typeof(Tool), "DoFunction"), nameof(Tool_Prefix), nameof(Tool_Postfix));
		Patch(harmony, "武器", Method(typeof(MeleeWeapon), "DoDamage"), null, nameof(Melee_Postfix));
		Patch(harmony, "弹弓打中", Method(typeof(BasicProjectile), "behaviorOnCollisionWithMonster"), null, nameof(ShotHit_Postfix));
		Patch(harmony, "弹弓打偏", Method(typeof(BasicProjectile), "behaviorOnCollisionWithOther"), null, nameof(ShotMiss_Postfix));
		Patch(harmony, "求婚", Method(typeof(NPC), "tryToReceiveActiveObject"), nameof(Proposal_Prefix), nameof(Proposal_Postfix));
		Patch(harmony, "昏倒", Method(typeof(Farmer), "passOutFromTired"), nameof(PassOut_Prefix), null);
		Patch(harmony, "吃喝", Method(typeof(Farmer), "doneEating"), nameof(Eat_Prefix), nameof(Eat_Postfix));
		Patch(harmony, "爆炸", Method(typeof(GameLocation), "explode"), null, nameof(Explode_Postfix));
		Patch(harmony, "坐下", Method(typeof(Farmer), "BeginSitting"), null, nameof(Sit_Postfix));
		Patch(harmony, "家具", Method(typeof(Furniture), "checkForAction", typeof(Farmer)), nameof(Furniture_Prefix), nameof(Furniture_Postfix));
		Patch(harmony, "电视", Method(typeof(TV), "checkForAction", typeof(Farmer)), nameof(Furniture_Prefix), nameof(Furniture_Postfix));
	}

	public static void Reset() => DoorAttempts.Clear();

	/// <summary>求婚结果（动作卡片里答应的那种也走这里）。</summary>
	public static void RecordProposal(NPC npc, bool marriage, bool accepted)
	{
		Farmer who = Game1.player;
		string farmer = Name(who);
		string ask = marriage ? $"向{npc.Name}求婚" : $"向{npc.Name}表白";
		string result = accepted ? $"{npc.Name}答应了" : $"{npc.Name}拒绝了";
		Record(marriage ? "marriage_proposal" : "dating_proposal", who, npc.currentLocation ?? Game1.currentLocation, $"{farmer}{ask}，{result}。", npc.Tile, npc.Name,
			Observation.Map(("result", accepted ? "accepted" : "rejected"), ("reactionDirective", $"你要是正和{farmer}谈着恋爱或者已经结了婚，这一幕会刺痛你，当面找对方问个明白；跟{farmer}没这层关系的话，就作为两边都认识的人来看待这件事。")));
	}

	// ---- 垃圾桶 ----

	private static void Garbage_Prefix(string id, Farmer who, out string? __state)
	{
		__state = null;
		Guard("翻垃圾桶", () =>
		{
			if (Local(who) && !string.IsNullOrWhiteSpace(id))
			{
				string can = CanId(id);
				if (!Game1.netWorldState.Value.CheckedGarbage.Contains(can))
				{
					return can;
				}
			}
			return null;
		}, out __state);
	}

	private static void Garbage_Postfix(GameLocation __instance, Vector2 tile, Farmer who, bool __result, string? __state)
	{
		if (!__result || __state == null || !Local(who))
		{
			return;
		}
		Guard("翻垃圾桶", () =>
		{
			string farmer = Name(who);
			string? place = CanPlace(__state);
			Record("trash", who, __instance, place == null ? $"{farmer}翻了一个垃圾桶。" : $"{farmer}翻了{place}门口的垃圾桶。", tile, null, Observation.Map(("can", __state)));
		});
	}

	private static string CanId(string id) => id switch
	{
		"0" => "JodiAndKent",
		"1" => "EmilyAndHaley",
		"2" => "Mayor",
		"3" => "Museum",
		"4" => "Blacksmith",
		"5" => "Saloon",
		"6" => "Evelyn",
		"7" => "JojaMart",
		_ => id
	};

	private static string? CanPlace(string can) => can switch
	{
		"JodiAndKent" => "乔迪和肯特家",
		"EmilyAndHaley" => "艾米丽和海莉家",
		"Mayor" => "刘易斯镇长家",
		"Museum" => "博物馆",
		"Blacksmith" => "克林特的铁匠铺",
		"Saloon" => "星之果实餐吧",
		"Evelyn" => "艾芙琳和乔治家",
		"JojaMart" => "Joja 超市",
		_ => null
	};

	// ---- 卧室门 ----

	private static void LockedDoor_Postfix(GameLocation __instance, string[] action)
	{
		Guard("锁着的门", () =>
		{
			string[] owners = DoorOwners(action);
			AwarenessSystem? s = _system;
			if (owners.Length == 0 || Game1.eventUp || s == null)
			{
				return;
			}
			string key = string.Join("+", owners);
			if (!s.Pass("door:" + key, Game1.ticks, DoorCooldownTicks))
			{
				return;
			}
			int attempt = DoorAttempts[key] = DoorAttempts.GetValueOrDefault(key) + 1;
			Dictionary<string, string> d = Observation.Map(("owners", string.Join(",", owners)), ("attempt", attempt.ToString()));
			if (attempt >= 2)
			{
				d["aggravators"] = "lockedRepeated";
			}
			Farmer who = Game1.player;
			Record("room_locked", who, __instance, $"{Name(who)}想推开{Possessive(owners)}卧室的门，发现锁着。", who?.Tile, OwnerPresent(__instance, owners), d);
		});
	}

	private static void Action_Prefix(string[] action, Farmer who, out (string[]? Door, string? Snoop) __state)
	{
		__state = (null, null);
		Guard("开门", () => (DoorCapture(action), SnoopCapture(action, who)), out __state);
	}

	private static void Action_Postfix(GameLocation __instance, Farmer who, bool __result, (string[]? Door, string? Snoop) __state)
	{
		DoorRecord(__instance, __state.Door);
		if (__state.Snoop != null && __result)
		{
			Guard("翻东西", () => SnoopRecord(__instance, who, __state.Snoop));
		}
	}

	private static void Door_Prefix(string[] action, out string[]? __state)
	{
		__state = null;
		Guard("踩门", () => DoorCapture(action), out __state);
	}

	private static void Door_Postfix(GameLocation __instance, string[]? __state) => DoorRecord(__instance, __state);

	private static string[]? DoorCapture(string[]? action)
	{
		if (action == null || action.Length < 2 || action[0] != "Door" || Game1.eventUp)
		{
			return null;
		}
		string[] locked = DoorOwners(action).Where(o => !Game1.player.mailReceived.Contains("doorUnlock" + o)).ToArray();
		return locked.Length > 0 ? locked : null;
	}

	private static void DoorRecord(GameLocation location, string[]? before)
	{
		if (before == null)
		{
			return;
		}
		Guard("开门", () =>
		{
			string[] opened = before.Where(o => Game1.player.mailReceived.Contains("doorUnlock" + o)).ToArray();
			if (opened.Length > 0)
			{
				Farmer who = Game1.player;
				Record("room_unlocked", who, location, $"{Name(who)}第一次推开{Possessive(opened)}卧室的门，走了进去。", who?.Tile, OwnerPresent(location, opened), Observation.Map(("owners", string.Join(",", opened))));
			}
		});
	}

	private static string[] DoorOwners(string[]? action) => action == null ? Array.Empty<string>() : action.Skip(1).Where(n => !string.IsNullOrWhiteSpace(n)).Take(2).ToArray();

	private static string OwnerPresent(GameLocation location, IReadOnlyList<string> owners) => owners.FirstOrDefault(o => location.getCharacterFromName(o) != null) ?? owners[0];

	// ---- 工具、武器、浇水 ----

	private static void Tool_Prefix(Tool __instance, out bool __state) => __state = __instance is WateringCan can && can.WaterLeft > 0;

	private static void Tool_Postfix(Tool __instance, GameLocation location, int x, int y, Farmer who, bool __state)
	{
		if (location == null || !Local(who) || __instance is not (Axe or Pickaxe or Hoe or WateringCan))
		{
			return;
		}
		Guard("工具", () =>
		{
			if (__instance is WateringCan)
			{
				if (__state && Watered(__instance, location, x, y, who) is NPC wet)
				{
					RecordWatered(location, who, wet);
				}
				return;
			}
			Point p = new Point(x, y);
			if (Villagers(location).FirstOrDefault(n => n.GetBoundingBox().Contains(p)) is NPC hit)
			{
				RecordHit(__instance, location, who, hit, false);
			}
			else if (__instance is Axe or Pickaxe && Nearest(location, new Vector2(x, y), 96f) is NPC near)
			{
				RecordNearMiss(__instance, location, who, near, false);
			}
		});
	}

	private static void Melee_Postfix(MeleeWeapon __instance, GameLocation location, Farmer who)
	{
		if (location == null || !Local(who))
		{
			return;
		}
		Guard("武器", () =>
		{
			Rectangle area = __instance.mostRecentArea;
			if (area.IsEmpty)
			{
				return;
			}
			bool weapon = !__instance.isScythe();
			if (Villagers(location).FirstOrDefault(n => n.GetBoundingBox().Intersects(area)) is NPC hit)
			{
				RecordHit(__instance, location, who, hit, weapon);
			}
			else if (weapon)
			{
				Rectangle reach = area;
				reach.Inflate(48, 48);
				if (Villagers(location).FirstOrDefault(n => n.GetBoundingBox().Intersects(reach)) is NPC near)
				{
					RecordNearMiss(__instance, location, who, near, weapon);
				}
			}
		});
	}

	private static void RecordHit(Tool tool, GameLocation location, Farmer who, NPC target, bool weapon)
	{
		AwarenessSystem? s = _system;
		if (s == null || !s.Pass("hit:" + target.Name, Game1.ticks, HitCooldownTicks))
		{
			return;
		}
		s.Mark("near:" + target.Name, Game1.ticks);
		string name = ItemName(tool);
		Record("tool_hit", who, location, weapon ? $"{Name(who)}挥起武器（{name}），真的打中了{target.Name}。" : $"{Name(who)}挥起{name}，真的打中了{target.Name}。", target.Tile, target.Name,
			Observation.Map(("tool", name), ("weapon", weapon ? "true" : "false")));
	}

	private static void RecordNearMiss(Tool tool, GameLocation location, Farmer who, NPC target, bool weapon)
	{
		AwarenessSystem? s = _system;
		if (s == null || !s.Pass("near:" + target.Name, Game1.ticks, long.MaxValue))
		{
			return;
		}
		string name = ItemName(tool);
		Record("tool_near_miss", who, location, $"{Name(who)}挥着{name}，离{target.Name}近得差点打到。", target.Tile, target.Name,
			Observation.Map(("tool", name), ("weapon", weapon ? "true" : "false")));
	}

	private static NPC? Watered(Tool can, GameLocation location, int x, int y, Farmer who)
	{
		Vector2 tile = new Vector2(x / 64, y / 64);
		IEnumerable<Vector2> tiles = TilesAffected?.Invoke(can, new object[] { tile, who.toolPower.Value, who }) as IEnumerable<Vector2> ?? new[] { tile };
		List<Rectangle> areas = tiles.Select(t => new Rectangle((int)t.X * 64, (int)t.Y * 64, 64, 64)).ToList();
		return Villagers(location).FirstOrDefault(n =>
		{
			Rectangle box = n.GetBoundingBox();
			return areas.Any(a => a.Intersects(box));
		});
	}

	private static void RecordWatered(GameLocation location, Farmer who, NPC target)
	{
		if (_system?.Pass("water:" + target.Name, Game1.ticks, HitCooldownTicks) == true)
		{
			Record("water_npc", who, location, $"{Name(who)}提着水壶往{target.Name}身上浇，把人浇得湿透。", target.Tile, target.Name);
		}
	}

	// ---- 弹弓 ----

	private static void ShotHit_Postfix(BasicProjectile __instance, NPC n, GameLocation location)
	{
		Guard("弹弓", () =>
		{
			AwarenessSystem? s = _system;
			if (s != null && location != null && ApproachCoordinator.IsVillager(n) && FiredByPlayer(__instance, location) && s.Pass("shot:" + n.Name, Game1.ticks, HitCooldownTicks))
			{
				s.Mark("shotNear:" + n.Name, Game1.ticks);
				Record("slingshot_hit", Game1.player, location, $"{Name(Game1.player)}用弹弓射中了{n.Name}。", n.Tile, n.Name);
			}
		});
	}

	private static void ShotMiss_Postfix(BasicProjectile __instance, GameLocation location)
	{
		Guard("弹弓", () =>
		{
			if (location != null && FiredByPlayer(__instance, location) && Nearest(location, __instance.getBoundingBox().Center.ToVector2(), 128f) is NPC near && _system?.Pass("shotNear:" + near.Name, Game1.ticks, long.MaxValue) == true)
			{
				Record("slingshot_near_miss", Game1.player, location, $"{Name(Game1.player)}打弹弓，差点射中{near.Name}。", near.Tile, near.Name);
			}
		});
	}

	private static bool FiredByPlayer(BasicProjectile p, GameLocation location) => p.damagesMonsters.Value && p.theOneWhoFiredMe.Get(location) == Game1.player;

	// ---- 求婚 ----

	private static void Proposal_Prefix(NPC __instance, Farmer who, bool probe, out (bool Marriage, bool Already)? __state)
	{
		__state = null;
		Guard("求婚", () =>
		{
			if (probe || __instance == null || !Local(who))
			{
				return ((bool, bool)?)null;
			}
			string? id = who.ActiveObject?.QualifiedItemId;
			if (id != "(O)458" && id != "(O)460")
			{
				return null;
			}
			bool marriage = id == "(O)460";
			return (marriage, Committed(who, __instance, marriage));
		}, out __state);
	}

	private static void Proposal_Postfix(NPC __instance, Farmer who, (bool Marriage, bool Already)? __state)
	{
		if (__state is { Already: false } st)
		{
			Guard("求婚", () => RecordProposal(__instance, st.Marriage, Committed(who, __instance, st.Marriage)));
		}
	}

	private static bool Committed(Farmer who, NPC npc, bool marriage) =>
		who.friendshipData.TryGetValue(npc.Name, out Friendship? f) && f != null && (marriage ? f.IsEngaged() || f.IsMarried() : f.IsDating());

	// ---- 昏倒、吃喝、爆炸 ----

	private static void PassOut_Prefix(Farmer who)
	{
		Guard("昏倒", () =>
		{
			GameLocation? l = who?.currentLocation;
			if (!Local(who) || l == null || l is FarmHouse or IslandFarmHouse || _system?.Pass("passout", Game1.ticks, long.MaxValue) != true)
			{
				return;
			}
			bool late = Game1.timeOfDay >= 2600;
			Record("sleep_outside", who, l, late ? $"{Name(who)}半夜倒在地上睡着了。" : $"{Name(who)}累得倒在了地上。", who!.Tile, null, Observation.Map(("reason", late ? "2am" : "exhausted")));
		});
	}

	private static void Eat_Prefix(Farmer __instance, out Item? __state)
	{
		__state = null;
		Guard("吃喝", () => Local(__instance) ? ReadItemToEat(__instance) : null, out __state);
	}

	private static void Eat_Postfix(Farmer __instance, Item? __state)
	{
		if (__state == null || !Local(__instance))
		{
			return;
		}
		Guard("吃喝", () =>
		{
			string item = ItemName(__state);
			Record("consume", __instance, Game1.currentLocation, $"{Name(__instance)}吃了{item}。", __instance.Tile, null, Observation.Map(("item", item), ("itemId", __state.QualifiedItemId ?? "")));
		});
	}

	private static Item? ReadItemToEat(Farmer farmer)
	{
		object? v = ItemToEat?.GetValue(farmer);
		return v as Item ?? v?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(v) as Item;
	}

	private static void Explode_Postfix(GameLocation __instance, object[] __args)
	{
		Guard("爆炸", () =>
		{
			Farmer? who = __args?.OfType<Farmer>().FirstOrDefault();
			if (!Local(who))
			{
				return;
			}
			Vector2 tile = __args?.OfType<Vector2>().FirstOrDefault() ?? who!.Tile;
			int radius = __args?.OfType<int>().FirstOrDefault() ?? 0;
			Exploded?.Invoke(__instance, tile, radius);
			List<NPC> sooted = Villagers(__instance).Where(n => Vector2.Distance(n.Tile, tile) <= Looks.NpcLooks.SootReach(radius)).OrderBy(n => Vector2.Distance(n.Tile, tile)).ToList();
			Dictionary<string, string> details = Observation.Map(("count", "1"));
			if (sooted.Count > 0)
			{
				details["sooted"] = string.Join(",", sooted.Select(n => n.Name));
			}
			Record("bomb", who, __instance, $"{Name(who)}在{AwarenessSystem.PlaceName(__instance)}弄出了一声爆炸。", tile, sooted.FirstOrDefault()?.Name, details);
		});
	}

	// ---- 别人家里 ----

	private static void Sit_Postfix(Farmer __instance, object? furniture)
	{
		Guard("坐下", () =>
		{
			GameLocation? l = __instance?.currentLocation;
			if (furniture == null || !Local(__instance) || !__instance!.IsSitting() || !Homes.IsPrivateHome(l) || Game1.eventUp || _system?.Pass("sit:" + l!.NameOrUniqueName, Game1.ticks, HomeCooldownTicks) != true)
			{
				return;
			}
			IReadOnlyList<string> owners = Homes.OwnersOf(l.Name);
			string seat = furniture is Furniture f ? ItemName(f) : "椅子";
			Dictionary<string, string> d = HomeDetails(l, owners, "sitOwnerPresent");
			d["seat"] = seat;
			Record("sit", __instance, l, $"{Name(__instance)}在{Possessive(owners)}家里，大大咧咧地坐到了{seat}上。", __instance.Tile, Homes.OwnerPresent(l, owners), d);
		});
	}

	private static void Furniture_Prefix(Furniture __instance, Farmer who, bool justCheckingForActivity, out string? __state)
	{
		__state = null;
		Guard("家具", () => !justCheckingForActivity && Local(who) && !Game1.eventUp && Homes.IsPrivateHome(who.currentLocation) ? Kind(__instance) : null, out __state);
	}

	private static void Furniture_Postfix(Furniture __instance, Farmer who, bool __result, string? __state)
	{
		if (__state == null || !__result)
		{
			return;
		}
		Guard("家具", () =>
		{
			GameLocation? l = who.currentLocation;
			if (l == null)
			{
				return;
			}
			bool dresser = __state == "dresser";
			string type = dresser ? "snoop" : "use_appliance";
			if (_system?.Pass($"{type}:{l.NameOrUniqueName}:{__state}", Game1.ticks, dresser ? SnoopCooldownTicks : HomeCooldownTicks) != true)
			{
				return;
			}
			IReadOnlyList<string> owners = Homes.OwnersOf(l.Name);
			Dictionary<string, string> d = HomeDetails(l, owners, dresser ? "snoopOwnerPresent" : null);
			d["kind"] = __state;
			d["item"] = ItemName(__instance);
			bool on = __instance.isOn.Value;
			if (!dresser)
			{
				d["state"] = on ? "打开了" : "关掉了";
			}
			string farmer = Name(who);
			string place = Possessive(owners);
			string summary = __state switch
			{
				"dresser" => $"{farmer}拉开{place}家里的柜子翻了一遍。",
				"tv" => $"{farmer}打开{place}家的电视看了起来，像在自己家一样。",
				"fireplace" => $"{farmer}摆弄了{place}家的壁炉。",
				_ => $"{farmer}把{place}家的灯{(on ? "打开了" : "关掉了")}。"
			};
			Record(type, who, l, summary, __instance.TileLocation, Homes.OwnerPresent(l, owners), d);
		});
	}

	private static string? Kind(Furniture f)
	{
		if (f is TV)
		{
			return "tv";
		}
		return f.furniture_type.Value switch
		{
			4 => "dresser",
			14 => "fireplace",
			7 or 13 or 16 or 17 => "lamp",
			_ => null
		};
	}

	private static string? SnoopCapture(string[]? action, Farmer who) =>
		action != null && action.Length > 0 && SnoopActions.Contains(action[0]) && !Game1.eventUp && Local(who) && Homes.IsPrivateHome(who.currentLocation) ? action[0] : null;

	private static void SnoopRecord(GameLocation l, Farmer who, string action)
	{
		if (_system?.Pass($"snoop:{l.NameOrUniqueName}:{who.Tile}", Game1.ticks, SnoopCooldownTicks) != true)
		{
			return;
		}
		IReadOnlyList<string> owners = Homes.OwnersOf(l.Name);
		Dictionary<string, string> d = HomeDetails(l, owners, "snoopOwnerPresent");
		d["kind"] = action.Contains("letter", StringComparison.OrdinalIgnoreCase) ? "letter" : "note";
		Record("snoop", who, l, $"{Name(who)}看了{Possessive(owners)}家里不该看的东西。", who.Tile, Homes.OwnerPresent(l, owners), d);
	}

	private static Dictionary<string, string> HomeDetails(GameLocation l, IReadOnlyList<string> owners, string? presentAggravator)
	{
		bool present = owners.Any(o => l.getCharacterFromName(o) != null);
		Dictionary<string, string> d = Observation.Map(("owners", string.Join(",", owners)), ("place", l.Name), ("ownerPresent", present ? "true" : "false"));
		if (present && presentAggravator != null)
		{
			d["aggravators"] = presentAggravator;
		}
		return d;
	}

	// ---- 工具 ----

	private static void Record(string type, Farmer? who, GameLocation? location, string summary, Vector2? tile, string? subject, Dictionary<string, string>? details = null)
	{
		_system?.Record(new Observation(type, Name(who), subject, location?.NameOrUniqueName ?? "", summary, tile, details));
	}

	private static string Possessive(IReadOnlyList<string> owners) => owners.Count switch
	{
		0 => "别人",
		1 => owners[0],
		_ => owners[0] + "和" + owners[1]
	};

	public static string Name(Farmer? f) => string.IsNullOrWhiteSpace(f?.Name) ? "农夫" : f.Name;

	public static string ItemName(Item? item) => item?.DisplayName ?? item?.Name ?? "某样东西";

	public static bool Local(Farmer? f) => f != null && f == Game1.player;

	private static IEnumerable<NPC> Villagers(GameLocation l) => l.characters.Where(ApproachCoordinator.IsVillager);

	private static NPC? Nearest(GameLocation l, Vector2 pixel, float max) => Villagers(l)
		.Select(n => (Npc: n, D: Vector2.Distance(n.GetBoundingBox().Center.ToVector2(), pixel)))
		.Where(x => x.D <= max).OrderBy(x => x.D).Select(x => x.Npc).FirstOrDefault();

	private static void Guard(string what, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			_log.Warn($"看到听到（{what}）出错：{ex.Message}");
		}
	}

	private static void Guard<T>(string what, Func<T> capture, out T result)
	{
		try
		{
			result = capture();
		}
		catch (Exception ex)
		{
			result = default!;
			_log.Warn($"看到听到（{what}）出错：{ex.Message}");
		}
	}

	private static void Patch(Harmony harmony, string what, MethodBase? method, string? prefix, string? postfix)
	{
		if (method == null)
		{
			_log.Warn($"看到听到：找不到{what}的游戏方法，这一项不记。");
			return;
		}
		try
		{
			harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(ActionPatches), prefix), postfix == null ? null : new HarmonyMethod(typeof(ActionPatches), postfix));
		}
		catch (Exception ex)
		{
			_log.Warn($"看到听到：挂不上{what}的钩子，这一项不记：{ex.Message}");
		}
	}

	private static MethodBase? Method(Type type, string name, Type? firstParameter = null) =>
		AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name == name && (firstParameter == null || m.GetParameters().FirstOrDefault()?.ParameterType == firstParameter));
}
