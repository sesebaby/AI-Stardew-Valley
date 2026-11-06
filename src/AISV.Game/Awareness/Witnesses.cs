using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AISV.Core.Awareness;
using AISV.Core.Persona;
using AISV.Game.Approach;
using Microsoft.Xna.Framework;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace AISV.Game.Awareness;

/// <summary>谁看见了、看清了几分、和这件事是什么关系。</summary>
internal sealed class Witnesses
{
	private const double InvolvedRangeTiles = 15.0;

	private static readonly IReadOnlyDictionary<string, string[]> TrashOwners = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["JodiAndKent"] = new[] { "Jodi", "Kent", "Sam", "Vincent" },
		["EmilyAndHaley"] = new[] { "Emily", "Haley" },
		["Mayor"] = new[] { "Lewis" },
		["Museum"] = new[] { "Gunther" },
		["Blacksmith"] = new[] { "Clint" },
		["Saloon"] = new[] { "Gus" },
		["Evelyn"] = new[] { "Evelyn", "George", "Alex" },
		["JojaMart"] = new[] { "Morris" }
	};

	private static readonly HashSet<string> Alcohol = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "346", "348", "303", "459", "873" };

	private readonly Func<SightingCatalog> _catalog;

	private readonly Func<string, bool> _isNpcEnabled;

	private readonly Func<IReadOnlyList<Sighting>> _events;

	public Witnesses(Func<SightingCatalog> catalog, Func<string, bool> isNpcEnabled, Func<IReadOnlyList<Sighting>> events)
	{
		_catalog = catalog;
		_isNpcEnabled = isNpcEnabled;
		_events = events;
	}

	public static bool AreRelated(string a, string b) => BuiltInPersonas.Relationships.Any(p =>
		(Same(p.A, a) && Same(p.B, b)) || (Same(p.A, b) && Same(p.B, a)));

	/// <summary>按事件类型补上 owners、kind、taste 等细节和加重因素。</summary>
	public static void Enrich(string type, Observation o, Dictionary<string, string> d, GameLocation location)
	{
		switch (type)
		{
		case "consume":
		{
			string id = d.GetValueOrDefault("itemId") ?? "";
			if (d.ContainsKey("kind") || id.Length == 0)
			{
				break;
			}
			string bare = id.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) ? id.Substring(3) : id;
			bool drink = Game1.objectData != null && Game1.objectData.TryGetValue(bare, out var data) && data.IsDrink;
			bool alcohol = Alcohol.Contains(bare);
			d["kind"] = alcohol ? "alcohol" : drink ? "drink" : "food";
			d["drink"] = drink || alcohol ? "true" : "false";
			if (alcohol)
			{
				d["alcohol"] = "true";
				if (!Same(location.Name, "Saloon"))
				{
					Aggravators.Add(d, "drinkOutsideSaloon");
				}
			}
			break;
		}
		case "trash":
			if (!d.ContainsKey("owners") && TrashOwners.TryGetValue(d.GetValueOrDefault("can") ?? "", out string[]? owners))
			{
				d["owners"] = string.Join(",", owners);
			}
			break;
		case "room_locked":
		case "room_unlocked":
			if (!d.ContainsKey("owners") && (d.GetValueOrDefault("owner") ?? o.Subject) is string owner && !string.IsNullOrWhiteSpace(owner))
			{
				d["owners"] = owner;
			}
			break;
		case "purchase":
			if (!string.IsNullOrWhiteSpace(o.Subject) && !d.ContainsKey("vendor"))
			{
				d["vendor"] = o.Subject;
			}
			break;
		case "gift":
			EnrichGift(o, d, location);
			break;
		}
	}

	private static void EnrichGift(Observation o, Dictionary<string, string> d, GameLocation location)
	{
		Farmer player = Game1.player;
		if (string.IsNullOrWhiteSpace(o.Subject) || player == null)
		{
			return;
		}
		if (d.GetValueOrDefault("taste") is "loved" or "hated")
		{
			Aggravators.Add(d, "giftLovedOrHated");
		}
		if (location.getCharacterFromName(o.Subject)?.isBirthday() == true)
		{
			d["birthday"] = "true";
			Aggravators.Add(d, "giftBirthday");
		}
		if (d.ContainsKey("partner"))
		{
			return;
		}
		foreach ((string name, Friendship f) in player.friendshipData.Pairs)
		{
			if (!Same(name, o.Subject) && IsPartner(f) && location.getCharacterFromName(name) is NPC partner && Vector2.Distance(partner.Tile, player.Tile) <= 5f)
			{
				d["partner"] = name;
				Aggravators.Add(d, "giftPartnerNearby");
				break;
			}
		}
	}

	/// <summary>在场的人：牵涉者 15 格内总能看到；其他人在范围内；睡着的只被能惊醒人的事吵醒。</summary>
	public List<Witness> Resolve(GameLocation location, Vector2 tile, Observation o, EventDefinition def, IReadOnlyDictionary<string, string> details)
	{
		List<Witness> list = Candidates(location, tile, o, def, details).ToList();
		if (!string.IsNullOrWhiteSpace(o.AlsoLocation) && Game1.getLocationFromName(o.AlsoLocation) is GameLocation other && other != location)
		{
			list.AddRange(Candidates(other, o.AlsoTile ?? tile, o, def, details));
		}
		return list
			.GroupBy(w => w.Who, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.OrderByDescending(w => w.Confidence).ThenBy(w => w.TilesAway).First())
			.ToList();
	}

	private IEnumerable<Witness> Candidates(GameLocation location, Vector2 tile, Observation o, EventDefinition def, IReadOnlyDictionary<string, string> details)
	{
		bool fishing = o.Type == "fishing";
		bool directOnly = o.Type == "hurt";
		double range = Number(o, "sh.range", 1, 30) ?? (def.ReachTiles < 0 ? double.MaxValue : def.ReachTiles);
		HashSet<string> involved = new HashSet<string>(SightingText.SplitList(details.GetValueOrDefault("owners")), StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(o.Subject))
		{
			involved.Add(o.Subject);
		}
		HashSet<string> excluded = new HashSet<string>(o.Exclude ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		foreach (NPC npc in location.characters.ToList())
		{
			if (!ApproachCoordinator.IsVillager(npc) || excluded.Contains(npc.Name) || !_isNpcEnabled(npc.Name))
			{
				continue;
			}
			double dist = Vector2.Distance(npc.Tile, tile);
			bool direct = involved.Contains(npc.Name) && (directOnly || dist <= InvolvedRangeTiles);
			if (!direct && (directOnly || dist > range))
			{
				continue;
			}
			if (!def.Senses.WakesSleepers && npc.isSleeping.Value)
			{
				continue;
			}
			if (fishing && !SightLine.Clear((int)npc.Tile.X, (int)npc.Tile.Y, (int)tile.X, (int)tile.Y, range, (x, y) => FishingSightOpen(location, x, y)))
			{
				continue;
			}
			yield return new Witness
			{
				Who = npc.Name,
				SightShare = direct ? 1.0 : Math.Max(Certainty.VisibilityAt(dist), def.Senses.HeardFloor),
				TilesAway = dist,
				FirstHand = direct,
				ClearView = direct || def.ThroughWalls || fishing || dist <= 8.0
			};
		}
	}

	private static bool FishingSightOpen(GameLocation location, int x, int y)
	{
		Vector2 t = new Vector2(x, y);
		if (!location.isTileOnMap(t) || (!location.isTilePassable(t) && !location.isWaterTile(x, y)))
		{
			return false;
		}
		CollisionMask ignore = ~(CollisionMask.Characters | CollisionMask.Farmers);
		return !location.IsTileOccupiedBy(t, ignore, ignore);
	}

	/// <summary>给每位目击者定角色、确定度，跑规则得出计量分量。规则补的细节写回 <paramref name="details"/>，奖励写进 <paramref name="rewards"/>。</summary>
	public List<Witness> Shape(EventDefinition def, List<Witness> witnesses, Observation o, Dictionary<string, string> details, GameLocation location, Vector2 tile, int day, Dictionary<string, RuleReward> rewards)
	{
		List<string> owners = SightingText.SplitList(details.GetValueOrDefault("owners"));
		Dictionary<string, int> seen = TimesSeenToday(def.Id, details, day);
		string weather = location.GetWeather()?.Weather ?? "";
		string weekday = SDate.Now().DayOfWeek.ToString();
		Farmer player = Game1.player;
		foreach (Witness w in witnesses)
		{
			w.Part = RoleOf(w.Who, o.Subject, owners, details);
			w.Sureness = 0;
			int certainty = Certainty.Level(w, def.MinCertainty);
			RuleOutcome outcome = SightingRules.Evaluate(def, _catalog().GlobalRules, new RuleSituation
			{
				Witness = w.Who,
				Role = w.Part,
				Certainty = certainty,
				Details = details,
				Weather = weather,
				Season = Game1.currentSeason ?? "",
				Weekday = weekday,
				Location = location.Name ?? "",
				Outdoors = location.IsOutdoors,
				Time = Game1.timeOfDay,
				Hearts = player?.getFriendshipHeartLevelForNPC(w.Who) ?? 0,
				IsPlayerPartner = player != null && player.friendshipData.TryGetValue(w.Who, out Friendship? f) && IsPartner(f),
				NearbyOfAge = (age, tiles) => NearbyOfAge(location, tile, w.Who, age, tiles)
			});
			foreach ((string k, string v) in outcome.AddedDetails)
			{
				details[k] = v;
			}
			if (outcome.Reward != null)
			{
				rewards[w.Who] = outcome.Reward;
			}
			w.Sureness = certainty;
			w.Gauges = outcome.Meters;
			w.FiredRules = outcome.MatchedRules;
			w.Quiet = outcome.Silent;
			w.Repeats = 1 + seen.GetValueOrDefault(w.Who);
		}
		return witnesses;
	}

	private Dictionary<string, int> TimesSeenToday(string type, IReadOnlyDictionary<string, string> details, int day)
	{
		string kind = details.GetValueOrDefault("kind") ?? "";
		string owners = details.GetValueOrDefault("owners") ?? "";
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (Sighting e in _events())
		{
			if (e.OnDay != day || e.Type != type || (type == "consume" && e.Detail("kind") != kind) || (type is "room_locked" or "room" && !Same(e.Detail("owners"), owners)))
			{
				continue;
			}
			foreach (Witness w in e.SeenBy)
			{
				counts[w.Who] = counts.GetValueOrDefault(w.Who) + 1;
			}
		}
		return counts;
	}

	private static string RoleOf(string npc, string? subject, IReadOnlyList<string> owners, IReadOnlyDictionary<string, string> details)
	{
		if (Same(details.GetValueOrDefault("vendor"), npc))
		{
			return "vendor";
		}
		if (Same(subject, npc) || owners.Contains(npc, StringComparer.OrdinalIgnoreCase))
		{
			return "target";
		}
		if ((!string.IsNullOrWhiteSpace(subject) && AreRelated(npc, subject)) || owners.Any(owner => AreRelated(npc, owner)))
		{
			return "relative";
		}
		return "bystander";
	}

	private static IReadOnlyList<string> NearbyOfAge(GameLocation location, Vector2 origin, string witness, string age, int tiles)
	{
		int value = age switch { "adult" => 0, "teen" => 1, "child" => 2, _ => -1 };
		if (value < 0)
		{
			return Array.Empty<string>();
		}
		return location.characters.Where(n => ApproachCoordinator.IsVillager(n) && n.Age == value && !Same(n.Name, witness) && Vector2.Distance(n.Tile, origin) <= tiles).Select(n => n.Name).ToArray();
	}

	public static double? Number(Observation o, string key, double min, double max) =>
		double.TryParse(o.Detail(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? Math.Clamp(v, min, max) : null;

	public static bool IsPartner(Friendship f) => !f.IsRoommate() && (f.IsDating() || f.IsEngaged() || f.IsMarried());

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
