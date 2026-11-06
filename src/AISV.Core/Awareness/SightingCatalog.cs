using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AISV.Core.Awareness;

/// <summary>一组写法：看清楚 / 没看全 / 瞥见 / 事后复述，可以按确定度 1～5 单独给。</summary>
public sealed class PhraseSet
{
	public Dictionary<string, string> ByTier { get; set; } = new Dictionary<string, string>();

	public string? Clear { get; set; }

	[JsonIgnore]
	public PhraseTemplate? ClearT { get; private set; }

	public string? Faint { get; set; }

	[JsonIgnore]
	public PhraseTemplate? FaintT { get; private set; }

	public string? Partial { get; set; }

	[JsonIgnore]
	public PhraseTemplate? PartialT { get; private set; }

	public string? Recalled { get; set; }

	[JsonIgnore]
	public PhraseTemplate? RecalledT { get; private set; }

	private Dictionary<int, PhraseTemplate> _levels = new Dictionary<int, PhraseTemplate>();

	[JsonIgnore]
	public bool IsEmpty => Clear == null && Partial == null && Faint == null && Recalled == null && (ByTier?.Count ?? 0) == 0;

	public PhraseTemplate? Level(int level) => _levels.TryGetValue(level, out PhraseTemplate? t) ? t : null;

	internal void Compile()
	{
		ClearT = Clear == null ? null : PhraseTemplate.Parse(Clear);
		PartialT = Partial == null ? null : PhraseTemplate.Parse(Partial);
		FaintT = Faint == null ? null : PhraseTemplate.Parse(Faint);
		RecalledT = Recalled == null ? null : PhraseTemplate.Parse(Recalled);
		_levels = new Dictionary<int, PhraseTemplate>();
		foreach ((string key, string text) in ByTier ?? new Dictionary<string, string>())
		{
			if (int.TryParse(key, out int n) && n >= 1 && n <= 5)
			{
				_levels[n] = PhraseTemplate.Parse(text);
			}
		}
	}

	public PhraseSet Over(PhraseSet fallback)
	{
		if (fallback.IsEmpty)
		{
			return this;
		}
		return new PhraseSet
		{
			Clear = Clear ?? fallback.Clear,
			Partial = Partial ?? fallback.Partial,
			Faint = Faint ?? fallback.Faint,
			Recalled = Recalled ?? fallback.Recalled,
			ClearT = ClearT ?? fallback.ClearT,
			PartialT = PartialT ?? fallback.PartialT,
			FaintT = FaintT ?? fallback.FaintT,
			RecalledT = RecalledT ?? fallback.RecalledT,
			ByTier = (ByTier?.Count ?? 0) > 0 ? ByTier! : fallback.ByTier,
			_levels = _levels.Count > 0 ? _levels : fallback._levels
		};
	}
}

/// <summary>按某个值选一段文字（比如按口味选"很喜欢 / 不喜欢"）。</summary>
public sealed class ComputedToken
{
	public string From { get; set; } = "";

	public Dictionary<string, string> Map { get; set; } = new Dictionary<string, string>();

	public string Default { get; set; } = "";

	private Dictionary<string, PhraseTemplate> _map = new Dictionary<string, PhraseTemplate>(StringComparer.OrdinalIgnoreCase);

	private PhraseTemplate _default = PhraseTemplate.Empty;

	internal void Compile()
	{
		_map = (Map ?? new Dictionary<string, string>()).ToDictionary(p => p.Key, p => PhraseTemplate.Parse(p.Value), StringComparer.OrdinalIgnoreCase);
		_default = PhraseTemplate.Parse(Default);
	}

	public string Render(Func<string, string?> resolve)
	{
		string key = resolve(From) ?? (From.Contains(':') ? null : resolve("p:" + From)) ?? "";
		return (_map.TryGetValue(key, out PhraseTemplate? t) ? t : _default).Render(resolve);
	}
}

/// <summary>头顶冒字（不叫 AI）。</summary>
public sealed class BubbleDefinition
{
	public static readonly string[] Buckets = { "default", "friend", "partner", "spouse", "child" };

	public int? ApproachTier { get; set; }

	public Dictionary<string, List<string>> ByBucket { get; set; } = new Dictionary<string, List<string>>();

	public int? DailyCap { get; set; }

	public string? Emote { get; set; }

	public int? MinCertainty { get; set; }

	public double? Odds { get; set; }

	public List<string> Roles { get; set; } = new List<string>();

	private Dictionary<string, List<string>> _lines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	[JsonIgnore]
	public double EffectiveChance => Math.Clamp(Odds ?? 1.0, 0, 1);

	[JsonIgnore]
	public int EffectiveMinCertainty => Math.Clamp(MinCertainty ?? 3, 1, 5);

	[JsonIgnore]
	public int EffectiveMaxPerNpcPerDay => Math.Max(0, DailyCap ?? 3);

	[JsonIgnore]
	public int EffectiveWalkUpFromLevel => Math.Max(1, ApproachTier ?? 1);

	[JsonIgnore]
	public int? EmoteIndex => Emotes.Resolve(Emote);

	[JsonIgnore]
	public bool HasLines => _lines.Values.Any(l => l.Count > 0);

	public bool AllowsRole(string role) => Roles.Count == 0 || Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

	internal void Compile()
	{
		Roles ??= new List<string>();
		_lines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach ((string bucket, List<string> lines) in ByBucket ?? new Dictionary<string, List<string>>())
		{
			if (Buckets.Contains(bucket, StringComparer.OrdinalIgnoreCase))
			{
				_lines[bucket] = (lines ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
			}
		}
	}

	public IReadOnlyList<string> LinesFor(string bucket)
	{
		foreach (string b in Chain(bucket))
		{
			if (_lines.TryGetValue(b, out List<string>? l) && l.Count > 0)
			{
				return l;
			}
		}
		return Array.Empty<string>();
	}

	public static string[] Chain(string bucket) => bucket.ToLowerInvariant() switch
	{
		"spouse" => new[] { "spouse", "partner", "friend", "default" },
		"partner" => new[] { "partner", "friend", "default" },
		"friend" => new[] { "friend", "default" },
		"child" => new[] { "child", "default" },
		_ => new[] { "default" }
	};

	public BubbleDefinition Over(BubbleDefinition? fallback)
	{
		if (fallback == null)
		{
			return this;
		}
		BubbleDefinition b = new BubbleDefinition
		{
			Odds = Odds ?? fallback.Odds,
			Emote = Emote ?? fallback.Emote,
			MinCertainty = MinCertainty ?? fallback.MinCertainty,
			DailyCap = DailyCap ?? fallback.DailyCap,
			ApproachTier = ApproachTier ?? fallback.ApproachTier,
			Roles = Roles.Count > 0 ? Roles : fallback.Roles,
			ByBucket = new Dictionary<string, List<string>>(fallback.ByBucket, StringComparer.OrdinalIgnoreCase)
		};
		foreach ((string k, List<string> v) in ByBucket)
		{
			b.ByBucket[k] = v;
		}
		b.Compile();
		return b;
	}

	/// <summary>一次性的气泡：只有一句默认台词。</summary>
	public static BubbleDefinition Single(string text, bool targetOnly)
	{
		BubbleDefinition b = new BubbleDefinition
		{
			MinCertainty = 1,
			DailyCap = 20,
			ByBucket = new Dictionary<string, List<string>> { ["default"] = new List<string> { text } },
			Roles = targetOnly ? new List<string> { "target" } : new List<string>()
		};
		b.Compile();
		return b;
	}
}

public sealed class PerceptionRules
{
	public bool ThroughWalls { get; set; }

	public bool WakesSleepers { get; set; }

	public double HeardFloor { get; set; }
}

public sealed class MeterContribution
{
	public double Weight { get; set; }

	public List<string> Roles { get; set; } = new List<string>();

	public string? RequiresPayload { get; set; }

	public bool AppliesTo(string role) => Roles == null || Roles.Count == 0 || Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public sealed class NearbyAgeCondition
{
	public string Age { get; set; } = "child";

	public int Tiles { get; set; } = 8;

	public bool Present { get; set; } = true;
}

public sealed class RuleConditions
{
	public List<string>? Witness { get; set; }

	public List<string>? NotWitness { get; set; }

	public List<string>? Role { get; set; }

	public int? MinCertainty { get; set; }

	public bool? PlayerPartner { get; set; }

	public Dictionary<string, string>? Payload { get; set; }

	public List<string>? Weather { get; set; }

	public List<string>? Season { get; set; }

	public List<string>? Weekday { get; set; }

	public List<string>? Location { get; set; }

	public bool? Outdoors { get; set; }

	public int? TimeFrom { get; set; }

	public int? TimeTo { get; set; }

	public int? MinHearts { get; set; }

	public int? MaxHearts { get; set; }

	public NearbyAgeCondition? NearbyAge { get; set; }
}

public sealed class RuleReward
{
	public string Item { get; set; } = "";

	public List<int> CountByTier { get; set; } = new List<int> { 1 };

	public int CountFor(int tier)
	{
		if (CountByTier == null || CountByTier.Count == 0)
		{
			return 1;
		}
		return CountByTier[Math.Clamp(tier, 1, CountByTier.Count) - 1];
	}
}

/// <summary>按目击者、角色、天气、心数等条件改计量分量。</summary>
public sealed class EventRule
{
	public BubbleDefinition? Callout { get; set; }

	public Dictionary<string, double> Multiply { get; set; } = new Dictionary<string, double>();

	public string Name { get; set; } = "";

	public RuleReward? Reward { get; set; }

	public Dictionary<string, double> Set { get; set; } = new Dictionary<string, double>();

	public bool Silent { get; set; }

	public RuleConditions When { get; set; } = new RuleConditions();

	public PhraseSet? Wording { get; set; }

	internal void Compile(int index)
	{
		if (string.IsNullOrWhiteSpace(Name))
		{
			Name = "rule " + (index + 1);
		}
		When ??= new RuleConditions();
		Set = new Dictionary<string, double>(Set ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);
		Multiply = new Dictionary<string, double>(Multiply ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);
		Wording?.Compile();
		Callout?.Compile();
	}
}

public sealed class EventDefinition
{
	public BubbleDefinition? Callout { get; set; }

	public Dictionary<string, ComputedToken> Derived { get; set; } = new Dictionary<string, ComputedToken>();

	public double FadeDays { get; set; } = 3.0;

	public Dictionary<string, PhraseSet> Forms { get; set; } = new Dictionary<string, PhraseSet>();

	public string Id { get; set; } = "";

	[JsonIgnore]
	public bool IsKnown { get; private set; }

	public Dictionary<string, MeterContribution> Meters { get; set; } = new Dictionary<string, MeterContribution>();

	public int MinCertainty { get; set; } = 1;

	/// <summary>-1 = 整张图。</summary>
	public double ReachTiles { get; set; } = 12.0;

	public List<EventRule> Rules { get; set; } = new List<EventRule>();

	public PerceptionRules Senses { get; set; } = new PerceptionRules();

	public double Severity { get; set; } = 4.95;

	public string? SplitOn { get; set; }

	public PhraseSet Wording { get; set; } = new PhraseSet();

	[JsonIgnore]
	public bool ThroughWalls => Senses.ThroughWalls;

	[JsonIgnore]
	public bool HasPhrases => !Wording.IsEmpty || Forms.Count > 0;

	public static EventDefinition Unknown(string id) => new EventDefinition { Id = id, FadeDays = 0 };

	internal void Compile(string id)
	{
		Id = id;
		IsKnown = true;
		Senses ??= new PerceptionRules();
		Wording ??= new PhraseSet();
		Meters = new Dictionary<string, MeterContribution>(Meters ?? new Dictionary<string, MeterContribution>(), StringComparer.OrdinalIgnoreCase);
		Forms = new Dictionary<string, PhraseSet>(Forms ?? new Dictionary<string, PhraseSet>(), StringComparer.OrdinalIgnoreCase);
		Derived = new Dictionary<string, ComputedToken>(Derived ?? new Dictionary<string, ComputedToken>(), StringComparer.OrdinalIgnoreCase);
		Rules = (Rules ?? new List<EventRule>()).Where(r => r != null).ToList();
		MinCertainty = Math.Clamp(MinCertainty, 1, 5);
		Wording.Compile();
		foreach (PhraseSet v in Forms.Values)
		{
			v.Compile();
		}
		foreach (ComputedToken t in Derived.Values)
		{
			t.Compile();
		}
		for (int i = 0; i < Rules.Count; i++)
		{
			Rules[i].Compile(i);
		}
		Callout?.Compile();
	}

	public PhraseSet PhrasesFor(Func<string, string?> resolve)
	{
		if (SplitOn == null || Forms.Count == 0)
		{
			return Wording;
		}
		string key = resolve(SplitOn) ?? "";
		return Forms.TryGetValue(key, out PhraseSet? v) || Forms.TryGetValue("default", out v) ? v.Over(Wording) : Wording;
	}

	public BubbleDefinition? BubbleFor(IReadOnlyCollection<string> matchedRules)
	{
		if (matchedRules.Count > 0)
		{
			foreach (EventRule rule in Rules)
			{
				if (rule.Callout != null && matchedRules.Contains(rule.Name))
				{
					return rule.Callout.Over(Callout);
				}
			}
		}
		return Callout;
	}
}

/// <summary>走过来时提示词里"刚才发生了什么"的标题和"怎么反应"的几条。</summary>
public sealed class ReactionGuide
{
	public string Title { get; set; } = "";

	public List<string> Guide { get; set; } = new List<string>();
}

/// <summary>情绪计量：阈值、衰减、表情和给 AI 的说明。</summary>
public sealed class MeterDefinition
{
	public int DailyCap { get; set; } = 3;

	/// <summary>walkUp = 走过来说；aside = 只放在心里。</summary>
	public string Delivery { get; set; } = "walkUp";

	public string Emote { get; set; } = "";

	public double FadeHours { get; set; } = 3;

	public List<string> Flavors { get; set; } = new List<string>();

	public double GrudgeLeft { get; set; }

	public string Heading { get; set; } = "";

	public string Id { get; set; } = "";

	public string Intro { get; set; } = "";

	public string Label { get; set; } = "";

	public int MinCertainty { get; set; } = 4;

	public string OpeningWalkUp { get; set; } = "";

	public double Rank { get; set; } = 1.0;

	public string Sendoff { get; set; } = "";

	public List<double> Steps { get; set; } = new List<double>();

	[JsonIgnore]
	public bool IsAside => string.Equals(Delivery, "aside", StringComparison.OrdinalIgnoreCase);

	[JsonIgnore]
	public int TierCount => Steps.Count;

	public int TierFor(double value)
	{
		int tier = 0;
		for (int i = 0; i < Steps.Count; i++)
		{
			if (value >= Steps[i])
			{
				tier = i + 1;
			}
		}
		return tier;
	}

	public double Intensity(double value, int tier)
	{
		if (Steps.Count == 0 || tier <= 0)
		{
			return 0;
		}
		double t = Steps[Math.Clamp(tier, 1, Steps.Count) - 1];
		return t <= 0 ? 1.0 : Math.Clamp(value / t, 0, 5);
	}

	public string ToneFor(int tier) => Flavors.Count == 0 ? "" : Flavors[Math.Clamp(tier, 1, Flavors.Count) - 1];

	internal void Compile(string id)
	{
		Id = id;
		Steps = (Steps ?? new List<double>()).OrderBy(t => t).ToList();
		Flavors ??= new List<string>();
		if (string.IsNullOrWhiteSpace(OpeningWalkUp))
		{
			OpeningWalkUp = Intro;
		}
	}
}

public static class Emotes
{
	/// <summary>表情表里的第几格（每格 4 帧）。</summary>
	private static readonly string?[] Sheet = { null, "can", "question", "angry", "exclamation", "heart", "sleep", "sad", "happy", "x", "pause", null, null, "game", "note", "blush" };

	private const int FramesPerEmote = 4;

	public static int? Resolve(string? name)
	{
		int cell = name == null ? -1 : Array.FindIndex(Sheet, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
		return cell < 0 ? null : cell * FramesPerEmote;
	}
}

/// <summary>事件类型和情绪计量的目录（assets/awareness/catalog.json）。</summary>
public sealed class SightingCatalog
{
	private sealed class CatalogFile
	{
		public Dictionary<string, EventDefinition?>? Types { get; set; }

		public Dictionary<string, MeterDefinition?>? Meters { get; set; }

		public List<EventRule?>? GlobalRules { get; set; }

		public Dictionary<string, ReactionGuide?>? Reactions { get; set; }
	}

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private readonly Dictionary<string, EventDefinition> _types;

	private readonly Dictionary<string, MeterDefinition> _meters;

	private readonly Dictionary<string, ReactionGuide> _reactions;

	private SightingCatalog(Dictionary<string, EventDefinition> types, Dictionary<string, MeterDefinition> meters, List<EventRule> global, Dictionary<string, ReactionGuide> reactions, List<string> problems)
	{
		_types = types;
		_meters = meters;
		_reactions = reactions;
		GlobalRules = global;
		Problems = problems;
	}

	public static SightingCatalog Empty { get; } = Parse("{}");

	public IReadOnlyList<EventRule> GlobalRules { get; }

	public IReadOnlyList<string> Problems { get; }

	public IEnumerable<EventDefinition> Types => _types.Values;

	public IEnumerable<MeterDefinition> Meters => _meters.Values;

	public static SightingCatalog Parse(string json)
	{
		List<string> problems = new List<string>();
		Dictionary<string, EventDefinition> types = new Dictionary<string, EventDefinition>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, MeterDefinition> meters = new Dictionary<string, MeterDefinition>(StringComparer.OrdinalIgnoreCase);
		List<EventRule> global = new List<EventRule>();
		CatalogFile? file = null;
		try
		{
			file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
		}
		catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
		{
			problems.Add("目录不是有效的 JSON：" + ex.Message);
		}
		foreach ((string id, EventDefinition? def) in file?.Types ?? new Dictionary<string, EventDefinition?>())
		{
			if (def != null)
			{
				def.Compile(NormalizeType(id));
				types[def.Id] = def;
			}
		}
		foreach ((string id, MeterDefinition? meter) in file?.Meters ?? new Dictionary<string, MeterDefinition?>())
		{
			if (meter != null)
			{
				meter.Compile(id);
				meters[id] = meter;
				if (meter.TierCount == 0)
				{
					problems.Add($"计量 {id} 没有阈值。");
				}
				else if (!meter.IsAside && meter.Flavors.Count < meter.TierCount)
				{
					problems.Add($"计量 {id} 有 {meter.TierCount} 档但只有 {meter.Flavors.Count} 种语气。");
				}
			}
		}
		foreach (EventRule? rule in file?.GlobalRules ?? new List<EventRule?>())
		{
			if (rule != null)
			{
				rule.Compile(global.Count);
				global.Add(rule);
			}
		}
		foreach (EventDefinition def in types.Values)
		{
			foreach (string m in def.Meters.Keys.Concat(def.Rules.SelectMany(r => r.Set.Keys)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (meters.Count > 0 && !meters.ContainsKey(m))
				{
					problems.Add($"{def.Id} 用到了没定义的计量 {m}。");
				}
			}
			if (!def.HasPhrases)
			{
				problems.Add($"{def.Id} 没有写法。");
			}
		}
		Dictionary<string, ReactionGuide> reactions = new Dictionary<string, ReactionGuide>(StringComparer.OrdinalIgnoreCase);
		foreach ((string id, ReactionGuide? guide) in file?.Reactions ?? new Dictionary<string, ReactionGuide?>())
		{
			if (guide != null)
			{
				guide.Guide ??= new List<string>();
				reactions[NormalizeType(id)] = guide;
			}
		}
		return new SightingCatalog(types, meters, global, reactions, problems);
	}

	public EventDefinition Get(string? type)
	{
		string id = NormalizeType(type ?? "");
		return _types.TryGetValue(id, out EventDefinition? d) ? d : EventDefinition.Unknown(id);
	}

	/// <summary>这类事走过来时的反应说明；没有专门的就用 default。</summary>
	public ReactionGuide Reaction(string? type) =>
		_reactions.TryGetValue(NormalizeType(type ?? ""), out ReactionGuide? g) || _reactions.TryGetValue("default", out g) ? g : new ReactionGuide();

	public bool TryGetMeter(string? id, out MeterDefinition meter) => _meters.TryGetValue(id ?? "", out meter!);

	public static string NormalizeType(string id)
	{
		string t = id.Trim().ToLowerInvariant();
		return t switch
		{
			"explosion" => "bomb",
			"consumption" => "consume",
			"pass_out" => "sleep_outside",
			"proposal" => "dating_proposal",
			"room_access" => "room_locked",
			"warp" => "map",
			"tool_use" => "tool_near_miss",
			"slingshot" => "slingshot_near_miss",
			"npc_movement" => "npc_move",
			"passout" => "sleep_outside",
			"lockeddoor" => "room_locked",
			_ => t
		};
	}
}
