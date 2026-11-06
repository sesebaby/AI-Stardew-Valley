using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AISV.Core.Stories;

/// <summary>结局场景的地点名：各种写法统一成游戏里的地点名。</summary>
public static class StageLocations
{
	public static readonly IReadOnlyList<string> SafeFallbacks = new[] { "Town", "Saloon" };

	public static string Normalize(string? location) => (location ?? "").Trim() switch
	{
		"Community Center" => "CommunityCenter",
		"Museum" or "Library" => "ArchaeologyHouse",
		"Cindersap Forest" or "CindersapForest" => "Forest",
		"Bus Stop" => "BusStop",
		"Haley House" => "HaleyHouse",
		"CarpenterShop" or "Science House" or "Carpenter" => "ScienceHouse",
		"Sam House" => "SamHouse",
		"Seed Shop" or "PierreShop" => "SeedShop",
		"MarnieRanch" or "Animal Shop" => "AnimalShop",
		"Leah House" => "LeahHouse",
		"Elliott House" => "ElliottHouse",
		"Manor House" => "ManorHouse",
		"WizardTower" or "Wizard House" => "WizardHouse",
		"Fish Shop" => "FishShop",
		"The Desert" or "CalicoDesert" => "Desert",
		"MineShaft" or "MineShaft0" or "Mines" => "Mine",
		string s => s
	};
}

/// <summary>挑好的地点计划。</summary>
public sealed class LocationPlan
{
	public IReadOnlyList<string> Participants { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Allowed { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Attempted { get; init; } = Array.Empty<string>();

	public IReadOnlyDictionary<string, int> Weights { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public string? Preferred { get; init; }

	/// <summary>npc_affinity / scene_location_fallback / generic_fallback。</summary>
	public string Source { get; init; } = "npc_affinity";
}

/// <summary>结局场景在哪演、用哪个舞台：按村民常去的地方加权挑地点，再给舞台打分。</summary>
public sealed class ConclusionStaging
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

	private readonly string _stageDir;

	private readonly string _affinityDir;

	private readonly Func<string, string> _toInternal;

	private List<ConclusionStage>? _stages;

	private Dictionary<string, List<LocationAffinity>>? _affinities;

	public ConclusionStaging(string stageDir, string affinityDir, Func<string, string> toInternal)
	{
		_stageDir = stageDir;
		_affinityDir = affinityDir;
		_toInternal = toInternal;
	}

	public IReadOnlyList<ConclusionStage> Stages => _stages ??= LoadStages();

	public IReadOnlyList<LocationAffinity> Affinities(string npc) => (_affinities ??= LoadAffinities()).TryGetValue(_toInternal(npc), out List<LocationAffinity>? list) ? list : Array.Empty<LocationAffinity>();

	/// <summary>挑舞台：先挑地点，再在能演的舞台里打分。</summary>
	public (ConclusionStage Stage, LocationPlan Plan) Choose(StoryArc arc, IReadOnlyList<ConclusionLine> lines, int today, string playerName)
	{
		LocationPlan plan = Plan(arc, lines, Seed(arc, lines, today, playerName), SupportedLocations(arc, lines), playerName);
		return (Resolve(arc, lines, plan), plan);
	}

	/// <summary>写结局之前定下在哪演：弧自己的地点在可选范围里就用它，否则用按参与者挑的。写台词和摆舞台都用这一个。</summary>
	public string SceneFor(StoryArc arc, int today, string playerName)
	{
		IReadOnlyList<ConclusionLine> none = Array.Empty<ConclusionLine>();
		LocationPlan plan = Plan(arc, none, Seed(arc, none, today, playerName), SupportedLocations(arc, none), playerName);
		string scene = StageLocations.Normalize(arc.StagePlace);
		return scene.Length > 0 && plan.Allowed.Contains(scene, Names) ? scene : plan.Preferred ?? (scene.Length > 0 ? scene : "Town");
	}

	public IReadOnlyList<string> SupportedLocations(StoryArc arc, IReadOnlyList<ConclusionLine> lines)
	{
		int speakers = CountSpeakers(arc, lines);
		List<string> tags = arc.Tags ?? new List<string>();
		return Playable(Stages, speakers, tags).Select(s => StageLocations.Normalize(s.Venue)).Where(l => l.Length > 0)
			.Distinct(Names).OrderBy(l => l, Names).ToList();
	}

	public ConclusionStage Resolve(StoryArc arc, IReadOnlyList<ConclusionLine> lines, LocationPlan? plan)
	{
		int speakers = CountSpeakers(arc, lines);
		List<string> tags = arc.Tags ?? new List<string>();
		List<ConclusionStage> ok = Playable(Stages, speakers, tags).ToList();
		if (plan != null && plan.Allowed.Count > 0)
		{
			HashSet<string> allowed = plan.Allowed.Select(StageLocations.Normalize).Where(l => l.Length > 0).ToHashSet(Names);
			List<ConclusionStage> there = ok.Where(s => allowed.Contains(StageLocations.Normalize(s.Venue))).ToList();
			if (there.Count > 0)
			{
				return Best(there, arc, tags, plan);
			}
		}
		if (ok.Count > 0)
		{
			return Best(ok, arc, tags, plan);
		}
		return Stages.Where(s => HasLayout(s, Math.Min(speakers, Math.Max(1, s.MostVoices))))
			.OrderByDescending(s => s.MostVoices).ThenBy(s => s.Key, Names).FirstOrDefault() ?? BuiltInStages()[0];
	}

	public LocationPlan Plan(StoryArc arc, IReadOnlyList<ConclusionLine> lines, int seed, IReadOnlyCollection<string>? available, string playerName)
	{
		List<string> participants = Participants(arc, lines, playerName);
		HashSet<string>? availableSet = available == null || available.Count == 0 ? null : available.Select(StageLocations.Normalize).Where(l => l.Length > 0).ToHashSet(Names);
		Dictionary<string, int> weights = new Dictionary<string, int>(Names);
		Dictionary<string, int> people = new Dictionary<string, int>(Names);
		Dictionary<string, HashSet<string>> kinds = new Dictionary<string, HashSet<string>>(Names);
		foreach (string npc in participants)
		{
			HashSet<string> seen = new HashSet<string>(Names);
			foreach (LocationAffinity a in Affinities(npc))
			{
				string loc = StageLocations.Normalize(a.Place);
				if (loc.Length == 0)
				{
					continue;
				}
				weights[loc] = weights.GetValueOrDefault(loc) + Math.Clamp(a.Pull <= 0 ? 1 : a.Pull, 1, 5) + (Same(a.Kind, "home") ? 2 : 0);
				if (seen.Add(loc))
				{
					people[loc] = people.GetValueOrDefault(loc) + 1;
				}
				if (!kinds.TryGetValue(loc, out HashSet<string>? k))
				{
					kinds[loc] = k = new HashSet<string>(Names);
				}
				if (!string.IsNullOrWhiteSpace(a.Kind))
				{
					k.Add(a.Kind.Trim());
				}
			}
		}
		foreach ((string loc, int count) in people)
		{
			if (count > 1)
			{
				weights[loc] += count * 4;
			}
		}
		string scene = StageLocations.Normalize(arc.StagePlace);
		if (scene.Length > 0 && weights.ContainsKey(scene))
		{
			weights[scene] += 3;
		}
		if (PrivateTone(arc))
		{
			foreach ((string loc, HashSet<string> k) in kinds)
			{
				if (k.Contains("home") || k.Contains("interior") || k.Contains("family"))
				{
					weights[loc] += 2;
				}
			}
		}
		List<string> attempted = weights.OrderByDescending(p => p.Value).ThenBy(p => p.Key, Names).Select(p => p.Key).ToList();
		List<string> allowed = attempted.Where(l => Available(l, availableSet)).ToList();
		if (allowed.Count > 0)
		{
			Dictionary<string, int> w = allowed.ToDictionary(l => l, l => weights.GetValueOrDefault(l, 1), Names);
			return new LocationPlan { Participants = participants, Attempted = attempted, Allowed = allowed, Weights = w, Preferred = Weighted(allowed, w, seed), Source = "npc_affinity" };
		}
		if (scene.Length > 0 && Available(scene, availableSet))
		{
			return new LocationPlan { Participants = participants, Attempted = attempted, Allowed = new[] { scene }, Weights = new Dictionary<string, int>(Names) { [scene] = 1 }, Preferred = scene, Source = "scene_location_fallback" };
		}
		List<string> safe = StageLocations.SafeFallbacks.Where(l => Available(l, availableSet)).ToList();
		if (safe.Count == 0 && availableSet != null)
		{
			safe = availableSet.OrderBy(l => l, Names).Take(1).ToList();
		}
		if (safe.Count == 0)
		{
			safe = StageLocations.SafeFallbacks.ToList();
		}
		Dictionary<string, int> even = safe.ToDictionary(l => l, _ => 1, Names);
		return new LocationPlan { Participants = participants, Attempted = attempted, Allowed = safe, Weights = even, Preferred = Weighted(safe, even, seed), Source = "generic_fallback" };
	}

	/// <summary>同一出、同一天、同样的人挑出来的地点总是一样（FNV 哈希）。</summary>
	public int Seed(StoryArc arc, IReadOnlyList<ConclusionLine> lines, int today, string playerName)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append(arc.Id ?? "").Append('|').Append(arc.ChosenEnding ?? "").Append('|').Append(today);
		foreach (string p in Participants(arc, lines, playerName).OrderBy(p => p, Names))
		{
			sb.Append('|').Append(p);
		}
		unchecked
		{
			int h = (int)2166136261;
			foreach (char c in sb.ToString())
			{
				h ^= char.ToUpperInvariant(c);
				h *= 16777619;
			}
			return h;
		}
	}

	public static int CountSpeakers(StoryArc arc, IReadOnlyList<ConclusionLine> lines)
	{
		int n = lines.Select(l => l.Who).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(Names).Count();
		return n > 0 ? n : Math.Max(1, arc.Cast.Count);
	}

	private List<string> Participants(StoryArc arc, IEnumerable<ConclusionLine> lines, string playerName) =>
		arc.Cast.Concat(lines.Select(l => l.Who))
			.Where(n => !string.IsNullOrWhiteSpace(n) && !Same(n.Trim(), "Farmer") && !Same(n.Trim(), "Player") && !Same(n.Trim(), playerName))
			.Select(n => _toInternal(n.Trim())).Distinct(Names).OrderBy(n => n, Names).ToList();

	private static bool PrivateTone(StoryArc arc) => arc.Tags?.Any(t => t.Contains("private", StringComparison.OrdinalIgnoreCase) || t.Contains("secret", StringComparison.OrdinalIgnoreCase)
		|| t.Contains("family", StringComparison.OrdinalIgnoreCase) || t.Contains("confession", StringComparison.OrdinalIgnoreCase)) ?? false;

	private static bool Available(string location, HashSet<string>? available) => available?.Contains(StageLocations.Normalize(location)) ?? true;

	private static string Weighted(IReadOnlyList<string> locations, IReadOnlyDictionary<string, int> weights, int seed)
	{
		if (locations.Count == 0)
		{
			return "";
		}
		int total = locations.Sum(l => Math.Max(1, weights.GetValueOrDefault(l, 1)));
		int roll = (seed == int.MinValue ? 0 : Math.Abs(seed)) % total;
		foreach (string l in locations)
		{
			roll -= Math.Max(1, weights.GetValueOrDefault(l, 1));
			if (roll < 0)
			{
				return l;
			}
		}
		return locations[0];
	}

	private static IEnumerable<ConclusionStage> Playable(IEnumerable<ConclusionStage> stages, int speakers, List<string> tags) => stages
		.Where(s => speakers >= s.FewestVoices && speakers <= s.MostVoices)
		.Where(s => HasLayout(s, speakers))
		.Where(s => !s.NotFor.Any(t => tags.Contains(t, Names)));

	private static bool HasLayout(ConclusionStage s, int speakers) => s.Blocking != null && s.Blocking.Others.Count >= Math.Max(1, speakers) && UniqueTiles(s.Blocking);

	private static bool UniqueTiles(StageLayout layout)
	{
		HashSet<(int, int)> used = new HashSet<(int, int)> { (layout.Hero.X, layout.Hero.Y) };
		return layout.Others.All(n => used.Add((n.X, n.Y)));
	}

	private static ConclusionStage Best(IReadOnlyList<ConclusionStage> candidates, StoryArc arc, IReadOnlyList<string> tags, LocationPlan? plan) =>
		candidates.OrderByDescending(s => Score(s, arc, tags, plan)).ThenBy(s => s.Key, Names).First();

	public static int Score(ConclusionStage stage, StoryArc arc, IReadOnlyList<string> tags, LocationPlan? plan)
	{
		int score = 0;
		string loc = StageLocations.Normalize(stage.Venue);
		string scene = StageLocations.Normalize(arc.StagePlace);
		if (plan != null)
		{
			if (!string.IsNullOrWhiteSpace(plan.Preferred) && Same(loc, StageLocations.Normalize(plan.Preferred)))
			{
				score += 80;
			}
			if (plan.Weights.TryGetValue(loc, out int w))
			{
				score += Math.Min(80, Math.Max(1, w) * 10);
			}
			if (scene.Length > 0 && plan.Allowed.Contains(scene, Names) && Same(loc, scene))
			{
				score += 200;
			}
		}
		else if (scene.Length > 0 && Same(loc, scene))
		{
			score += 100;
		}
		foreach (string tag in tags)
		{
			if (stage.OnlyFor.Contains(tag, Names))
			{
				score += 12;
			}
			if (stage.Moods.Contains(tag, Names))
			{
				score += 8;
			}
		}
		string choice = arc.ChosenEnding ?? "";
		if (choice.Equals(StoryArcBook.ChoiceOpenUp, StringComparison.OrdinalIgnoreCase) && stage.Moods.Contains("conflict", Names))
		{
			score += 5;
		}
		if (choice.Equals(StoryArcBook.ChoiceMend, StringComparison.OrdinalIgnoreCase) && stage.Moods.Contains("reconciliation", Names))
		{
			score += 5;
		}
		return score;
	}

	// ---- 读文件 ----

	private List<ConclusionStage> LoadStages()
	{
		List<ConclusionStage> list = new List<ConclusionStage>();
		HashSet<string> seen = new HashSet<string>(Names);
		if (Directory.Exists(_stageDir))
		{
			foreach (string file in Directory.EnumerateFiles(_stageDir, "*.json", SearchOption.AllDirectories).OrderBy(f => System.IO.Path.GetRelativePath(_stageDir, f), Names))
			{
				try
				{
					ConclusionStage? s = JsonSerializer.Deserialize<ConclusionStage>(File.ReadAllText(file), Json);
					if (s != null && Validate(s, seen))
					{
						list.Add(s);
					}
				}
				catch (Exception)
				{
				}
			}
		}
		return list.Count > 0 ? list : BuiltInStages().ToList();
	}

	public static bool Validate(ConclusionStage s, ISet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(s.Key) || string.IsNullOrWhiteSpace(s.Venue) || s.Backdrop == null || s.Spots == null || s.Spots.Count == 0)
		{
			return false;
		}
		s.Key = s.Key.Trim();
		if (!seen.Add(s.Key))
		{
			return false;
		}
		s.Name = (s.Name ?? "").Trim();
		s.Venue = s.Venue.Trim();
		s.Tune = (s.Tune ?? "").Trim();
		s.FewestVoices = Math.Max(1, s.FewestVoices);
		s.MostVoices = Math.Max(s.FewestVoices, s.MostVoices);
		s.Moods = Clean(s.Moods);
		s.OnlyFor = Clean(s.OnlyFor);
		s.NotFor = Clean(s.NotFor);
		s.Backdrop.Style = string.IsNullOrWhiteSpace(s.Backdrop.Style) ? "vanilla_event" : s.Backdrop.Style.Trim();
		s.Backdrop.Feel = string.IsNullOrWhiteSpace(s.Backdrop.Feel) ? "interior" : s.Backdrop.Feel.Trim();
		if (s.Blocking != null)
		{
			s.Blocking.Lens ??= new StagePoint();
			s.Blocking.Hero ??= new StagePoint();
			s.Blocking.Hero.Facing = Facing(s.Blocking.Hero.Facing);
			s.Blocking.Others = (s.Blocking.Others ?? new List<StageNpcPoint>()).Where(n => n != null).Take(Math.Max(1, s.MostVoices)).ToList();
			foreach (StageNpcPoint n in s.Blocking.Others)
			{
				n.Slot = (n.Slot ?? "").Trim();
				n.Facing = Facing(n.Facing);
			}
			if (!UniqueTiles(s.Blocking))
			{
				return false;
			}
		}
		s.Spots = s.Spots.Where(p => p != null).ToList();
		foreach (StagePlacement p in s.Spots)
		{
			p.Role = (p.Role ?? "").Trim();
			p.X = Math.Clamp(p.X, 0.05f, 0.95f);
			p.Y = Math.Clamp(p.Y, 0.05f, 0.95f);
			p.Size = Math.Clamp(p.Size <= 0f ? 1f : p.Size, 0.7f, 1.4f);
			p.Dir = (p.Dir ?? "").Trim();
		}
		return s.Spots.Count > 0;
	}

	private Dictionary<string, List<LocationAffinity>> LoadAffinities()
	{
		Dictionary<string, List<LocationAffinity>> map = new Dictionary<string, List<LocationAffinity>>(Names);
		if (!Directory.Exists(_affinityDir))
		{
			return map;
		}
		foreach (string file in Directory.EnumerateFiles(_affinityDir, "*.json", SearchOption.AllDirectories).OrderBy(f => System.IO.Path.GetRelativePath(_affinityDir, f), Names))
		{
			try
			{
				LocationAffinityFile? data = JsonSerializer.Deserialize<LocationAffinityFile>(File.ReadAllText(file), Json);
				foreach ((string npc, List<LocationAffinity> raw) in data?.Npcs ?? new Dictionary<string, List<LocationAffinity>>())
				{
					AddAffinities(map, _toInternal(npc.Trim()), raw);
				}
			}
			catch (Exception)
			{
			}
		}
		return map;
	}

	/// <summary>每位村民同一地点只留权重最高的一条，最多留两个地点。</summary>
	public static void AddAffinities(Dictionary<string, List<LocationAffinity>> map, string npc, IEnumerable<LocationAffinity>? raw)
	{
		if (string.IsNullOrWhiteSpace(npc) || raw == null)
		{
			return;
		}
		Dictionary<string, LocationAffinity> best = new Dictionary<string, LocationAffinity>(Names);
		foreach (LocationAffinity r in raw.Where(r => r != null))
		{
			string loc = StageLocations.Normalize(r.Place);
			if (loc.Length == 0)
			{
				continue;
			}
			LocationAffinity a = new LocationAffinity
			{
				Place = loc,
				Pull = Math.Clamp(r.Pull <= 0 ? 1 : r.Pull, 1, 5),
				Kind = string.IsNullOrWhiteSpace(r.Kind) ? "habit" : r.Kind.Trim().ToLowerInvariant(),
				Why = r.Why?.Trim() ?? ""
			};
			if (!best.TryGetValue(loc, out LocationAffinity? old) || a.Pull > old.Pull)
			{
				best[loc] = a;
			}
		}
		List<LocationAffinity> top = best.Values.OrderByDescending(a => a.Pull).ThenBy(a => a.Place, Names).Take(2).ToList();
		if (top.Count > 0)
		{
			map[npc] = top;
		}
	}

	private static List<string> Clean(IEnumerable<string>? values) =>
		values?.Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct(Names).ToList() ?? new List<string>();

	private static string Facing(string? facing)
	{
		string f = (facing ?? "").Trim().ToLowerInvariant();
		return f is "up" or "right" or "down" or "left" ? f : "down";
	}

	/// <summary>没有舞台文件时的两个：镇广场、酒吧。</summary>
	public static ConclusionStage[] BuiltInStages() => new[]
	{
		BuiltIn("fallback/town_square", "Town Square", "Town", new[] { "community", "reconciliation" }, new[] { "community", "trust", "friendship", "reconciliation" }, "spring_day",
			(52, 61), (51, 61), new[] { (53, 61, "left"), (54, 61, "left"), (52, 60, "down"), (55, 60, "left") }),
		BuiltIn("fallback/saloon_table", "Saloon Table", "Saloon", new[] { "reconciliation", "conflict" }, new[] { "trust", "apology", "conflict", "family" }, "sadpiano",
			(10, 18), (9, 18), new[] { (11, 18, "left"), (12, 18, "left"), (10, 19, "down"), (13, 19, "left") })
	};

	private static ConclusionStage BuiltIn(string id, string title, string location, string[] types, string[] tags, string music, (int X, int Y) camera, (int X, int Y) farmer, (int X, int Y, string F)[] npcs) => new ConclusionStage
	{
		Key = id,
		Name = title,
		Venue = location,
		Moods = types.ToList(),
		OnlyFor = tags.ToList(),
		FewestVoices = 1,
		MostVoices = 4,
		Tune = music,
		Blocking = new StageLayout
		{
			Lens = new StagePoint { X = camera.X, Y = camera.Y },
			Hero = new StagePoint { X = farmer.X, Y = farmer.Y, Facing = "down" },
			Others = npcs.Select((n, i) => new StageNpcPoint { Slot = $"speaker_{i + 1}", X = n.X, Y = n.Y, Facing = n.F }).ToList()
		},
		Spots = new List<StagePlacement>
		{
			new StagePlacement { Role = "speaker_1", X = 0.38f, Y = 0.54f, Size = 1.05f },
			new StagePlacement { Role = "speaker_2", X = 0.55f, Y = 0.54f, Size = 1f },
			new StagePlacement { Role = "speaker_3", X = 0.46f, Y = 0.42f, Size = 0.92f },
			new StagePlacement { Role = "speaker_4", X = 0.67f, Y = 0.43f, Size = 0.88f }
		}
	};

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>结局场景的事件脚本。</summary>
public static class ConclusionScript
{
	public const string CompleteCommand = "AisvArcConclusionComplete";

	public const int MaxSpeakers = 8;

	private static readonly HashSet<string> SupportedFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Forest", "Saloon", "Farm", "Town", "Beach", "CommunityCenter", "ArchaeologyHouse", "Mountain" };

	public readonly record struct Spot(int X, int Y, int Facing);

	public sealed record Layout(int CameraX, int CameraY, Spot Farmer, IReadOnlyList<Spot> Npcs)
	{
		/// <summary>站位不够时，多出来的人从最后一个位置往旁边排。</summary>
		public Spot Npc(int i)
		{
			if (i < Npcs.Count)
			{
				return Npcs[i];
			}
			Spot last = Npcs[^1];
			int extra = i - Npcs.Count + 1;
			return new Spot(last.X + extra, last.Y + extra % 2, last.Facing);
		}
	}

	public static List<ConclusionLine> Playable(StoryArc arc, string playerName) => arc.FinalScene?
		.Where(l => !string.IsNullOrWhiteSpace(l.Who) && !string.IsNullOrWhiteSpace(l.Line)
			&& !Same(l.Who, playerName) && !Same(l.Who, "Farmer") && !Same(l.Who, "Player"))
		.ToList() ?? new List<ConclusionLine>();

	public static string Location(StoryArc arc)
	{
		string stage = StageLocations.Normalize(arc.FinalStage?.Venue);
		if (stage.Length > 0 && (arc.FinalStage?.Blocking != null || SupportedFallbacks.Contains(stage)))
		{
			return stage;
		}
		string scene = StageLocations.Normalize(arc.StagePlace);
		return SupportedFallbacks.Contains(scene) ? scene : "Town";
	}

	public static Layout LayoutFor(string location, ConclusionStage? stage)
	{
		StageLayout? l = stage?.Blocking;
		if (l != null && l.Others.Count > 0)
		{
			List<Spot> npcs = l.Others.Select(n => new Spot(n.X, n.Y, Facing(n.Facing))).ToList();
			HashSet<(int, int)> used = new HashSet<(int, int)> { (l.Hero.X, l.Hero.Y) };
			if (npcs.All(n => used.Add((n.X, n.Y))))
			{
				return new Layout(l.Lens.X, l.Lens.Y, new Spot(l.Hero.X, l.Hero.Y, Facing(l.Hero.Facing)), npcs);
			}
		}
		return location switch
		{
			"Saloon" => Row(10, 18),
			"Beach" => Row(39, 34),
			"CommunityCenter" => Row(20, 20),
			"ArchaeologyHouse" => Row(10, 5),
			"Mountain" => Row(34, 15),
			"Forest" => Row(30, 41),
			"Farm" => new Layout(64, 15, new Spot(64, 15, 2), new[] { new Spot(66, 15, 3), new Spot(67, 15, 3), new Spot(65, 14, 2), new Spot(68, 14, 3) }),
			_ => Row(52, 61)
		};
	}

	/// <summary>镜头在 (x, y)，农夫在左边一格，村民在右边排开。</summary>
	private static Layout Row(int x, int y) => new Layout(x, y, new Spot(x - 1, y, 2), new[] { new Spot(x + 1, y, 3), new Spot(x + 2, y, 3), new Spot(x, y - 1, 2), new Spot(x + 3, y - 1, 3) });

	public static string Build(IReadOnlyList<ConclusionLine> lines, Layout layout, string eventId)
	{
		List<string> speakers = lines.Select(l => l.Who.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSpeakers).ToList();
		StringBuilder sb = new StringBuilder();
		sb.Append($"continue/{layout.CameraX} {layout.CameraY}/farmer {layout.Farmer.X} {layout.Farmer.Y} {layout.Farmer.Facing}");
		for (int i = 0; i < speakers.Count; i++)
		{
			Spot s = layout.Npc(i);
			sb.Append($" {speakers[i]} {s.X} {s.Y} {s.Facing}");
		}
		foreach (ConclusionLine l in lines.Where(l => speakers.Contains(l.Who.Trim(), StringComparer.OrdinalIgnoreCase)))
		{
			string who = l.Who.Trim();
			sb.Append($"/speak {who} \"{Escape(l.Line)}\"");
			int emote = Emote(l.Feeling);
			if (emote > 0)
			{
				sb.Append($"/emote {who} {emote}");
			}
		}
		sb.Append($"/{CompleteCommand} {eventId}/end");
		return sb.ToString();
	}

	public static int Emote(string? emotion) => (emotion ?? "").Trim().ToLowerInvariant() switch
	{
		"happy" or "love" => 32,
		"surprised" => 8,
		"sad" => 28,
		"angry" => 12,
		_ => 0
	};

	public static int Facing(string? facing) => (facing ?? "").Trim().ToLowerInvariant() switch
	{
		"up" => 0,
		"right" => 1,
		"left" => 3,
		_ => 2
	};

	public static string Escape(string text)
	{
		string t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (t.Length > 240)
		{
			t = t[..240].TrimEnd();
		}
		return t.Replace("\\", "").Replace("\"", "'").Replace("/", " ");
	}

	/// <summary>事件 ID：故事线 id 里的字母数字加一串随机码。</summary>
	public static string EventId(string arcId)
	{
		string clean = string.Concat((string.IsNullOrWhiteSpace(arcId) ? "arc" : arcId).Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
		return (clean.Length == 0 ? "arc" : clean) + "_" + Guid.NewGuid().ToString("N");
	}

	private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
