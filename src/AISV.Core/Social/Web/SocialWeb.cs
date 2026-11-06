using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;

namespace AISV.Core.Social.Web;

/// <summary>排今天的村民互动时需要的世界情况。</summary>
public sealed class SocialDayInput
{
	public GameDate Today { get; init; }

	public string UniqueGameId { get; init; } = "";

	/// <summary>开着 AI 的村民（内部名）；空时用内置的游戏村民。</summary>
	public IReadOnlyList<string> EnabledNpcs { get; init; } = Array.Empty<string>();

	/// <summary>村民此刻在哪（内部名 → 地点名）。</summary>
	public IReadOnlyDictionary<string, string> Locations { get; init; } = new Dictionary<string, string>();

	/// <summary>认识的 SVE 村民也算有人设。</summary>
	public Func<string, bool> HasKnownPersonality { get; init; } = BuiltInPersonas.HasVanilla;

	/// <summary>两人之间钱上的事（借钱、手头紧），没有时为空。</summary>
	public Func<string, string, string>? Money { get; init; }
}

/// <summary>村民之间的社交网。存在 bonds/village_web.json。只在主线程用。</summary>
public sealed class SocialWeb
{
	public const string Path = "bonds/village_web.json";

	private static readonly DocumentType<NpcSocialWebState> Doc = new DocumentType<NpcSocialWebState>("村民社交网", 1, () => new NpcSocialWebState());

	private static readonly string[] Common = { "small_kindness", "shared_hobby", "failed_favor", "quiet_bond", "playful_argument", "accidental_teamwork", "town_rumor_ping" };

	private static readonly string[] Rare = { "awkward_misunderstanding", "odd_friendship_seed", "soft_conflict" };

	private static readonly string[] ChildSafe = { "small_kindness", "shared_hobby", "quiet_bond", "accidental_teamwork" };

	private static readonly HashSet<string> Children = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jas", "Vincent", "Leo" };

	private static readonly HashSet<string> KrobusFriends = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dwarf", "Linus", "Wizard" };

	private static readonly Dictionary<string, (float, float, float, float, float)> ToneShifts = new Dictionary<string, (float, float, float, float, float)>(StringComparer.OrdinalIgnoreCase)
	{
		["sweet"] = (0.05f, 0.05f, 0.04f, -0.02f, 0.01f),
		["funny"] = (0.05f, 0.03f, 0.01f, -0.01f, 0.06f),
		["awkward"] = (0.04f, 0f, 0f, 0.03f, 0.01f),
		["tense"] = (0.04f, -0.03f, -0.03f, 0.06f, 0f),
		["strange"] = (0.04f, 0.01f, 0f, 0.01f, 0.03f)
	};

	private const int RecentContactDays = 7;

	private const int DrawPoolSize = 24;

	private const int OddSeedPercent = 15;

	private readonly Func<DocumentStore?> _store;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, string> _display;

	private DocumentStore? _trimmedFor;

	public SocialWeb(Func<DocumentStore?> store, Func<ModConfig> config, Func<string, string> display)
	{
		_store = store;
		_config = config;
		_display = display;
	}

	public IReadOnlyList<NpcSocialEdge> Edges => State().Links;

	public IReadOnlyList<NpcSocialMoment> RecentMoments => State().Moments;

	public static string PairKey(string a, string b)
	{
		(string x, string y) = Sort(a, b);
		return x + "|" + y;
	}

	public static bool IsChild(string npc) => Children.Contains(npc);

	// ---- 排今天的互动 ----

	public List<NpcSocialCandidate> PlanDay(SocialDayInput input)
	{
		ModConfig c = _config();
		NpcSocialWebState s = State();
		List<NpcSocialCandidate> pool = Candidates(input, s, c).ToList();
		List<NpcSocialCandidate> plan = new List<NpcSocialCandidate>();
		if (pool.Count > 0)
		{
			Random random = new Random(Seed(input.UniqueGameId, input.Today.TotalDays, c.MeetupSpicePercent));
			int bonus = c.RumorsOn && GossipFocus.Normalize(c.RumorSubject) == GossipFocus.VillagersOnly ? 1 : 0;
			int count = Math.Clamp(c.VillagerMeetupsDaily + bonus, 1, 5);
			bool rareTaken = false;
			while (plan.Count < count && pool.Count > 0)
			{
				NpcSocialCandidate? pick = PickWeighted(pool, random);
				if (pick == null)
				{
					break;
				}
				bool rare = !rareTaken && !pick.FromLore && !pick.AlreadyLinked && !pick.HasChild && random.Next(100) < Math.Clamp(c.MeetupRarePercent, 0, 25);
				pick.Rare = rare;
				pick.Theme = Archetype(pick, random, rare);
				pick.NoAi = TemplateOnly(pick, random, c);
				plan.Add(pick);
				rareTaken |= rare;
				string key = PairKey(pick.First, pick.Second);
				pool.RemoveAll(x => PairKey(x.First, x.Second) == key);
			}
		}
		s.PlanFor = input.Today.TotalDays;
		s.Plan = plan;
		Dirty();
		return plan;
	}

	/// <summary>分数（0 = 不考虑）。</summary>
	public static int Score(NpcSocialCandidate cand, NpcSocialEdge? edge, int today, ModConfig c)
	{
		if (edge != null && today < edge.RestUntilDay)
		{
			return 0;
		}
		float surprise = Math.Clamp(c.MeetupSpicePercent, 0, 100) / 100f;
		float points = 12f;
		if (cand.FromLore)
		{
			points += 30f;
		}
		if (cand.SamePlace)
		{
			points += 25f;
		}
		if (edge != null)
		{
			points += edge.Closeness * 20f + edge.Liking * 25f;
			if (edge.Friction >= BondLines.TenseTension)
			{
				points -= 15f;
			}
			else if (edge.Friction >= BondLines.AwkwardTension)
			{
				points += 6f;
			}
			int gap = today - edge.LastMetDay;
			if (edge.LastMetDay > 0 && gap is >= 0 and < RecentContactDays)
			{
				points -= (RecentContactDays - gap) * 4f;
			}
		}
		else if (!cand.FromLore)
		{
			points += surprise * 25f;
			if (!cand.SamePlace)
			{
				points -= (1f - surprise) * 10f;
			}
		}
		points -= (cand.FirstHasPersona ? 0f : 3f) + (cand.SecondHasPersona ? 0f : 3f);
		return Math.Max(0, (int)Math.Round(points));
	}

	/// <summary>这一对不能安排：同一个人、孩子配矮人或科罗布斯、矮人不在矿洞、科罗布斯不在下水道也不是和熟人在僻静处。</summary>
	public static bool Blocked(string a, string b, string? locA, string? locB)
	{
		if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		bool dwarf(string n) => string.Equals(n, "Dwarf", StringComparison.OrdinalIgnoreCase);
		bool krobus(string n) => string.Equals(n, "Krobus", StringComparison.OrdinalIgnoreCase);
		if ((IsChild(a) || IsChild(b)) && (dwarf(a) || krobus(a) || dwarf(b) || krobus(b)))
		{
			return true;
		}
		if (dwarf(a) || dwarf(b))
		{
			return !(Matches(locA, "mine|volcano|dwarf") || Matches(locB, "mine|volcano|dwarf"));
		}
		if (krobus(a) || krobus(b))
		{
			if (Matches(locA, "sewer|witch|mutant|krobus") || Matches(locB, "sewer|witch|mutant|krobus"))
			{
				return false;
			}
			string other = krobus(a) ? b : a;
			return !(KrobusFriends.Contains(other) && (Matches(locA, "forest|mountain|backwoods|railroad|wizard") || Matches(locB, "forest|mountain|backwoods|railroad|wizard")));
		}
		return false;
	}

	// ---- 发生的事 ----

	/// <summary>不调 AI 时的现成写法。</summary>
	public NpcSocialMoment Fallback(NpcSocialCandidate cand, GameDate today)
	{
		string tone = cand.Theme switch
		{
			"small_kindness" or "quiet_bond" => "sweet",
			"awkward_misunderstanding" or "failed_favor" => "awkward",
			"soft_conflict" => "tense",
			"playful_argument" or "odd_friendship_seed" => "funny",
			_ => "mundane"
		};
		string a = _display(cand.First), b = _display(cand.Second), at = Places.Name(cand.Place);
		string summary = tone switch
		{
			"sweet" => $"{a}在{at}顺手帮了{b}一个小忙，两人都挺暖心。",
			"awkward" => $"{a}和{b}在{at}闹了个无伤大雅的小误会。",
			"tense" => $"{a}和{b}在{at}意见不合，拌了几句嘴。",
			"funny" => $"{a}和{b}说起镇上一件怪事，笑得停不下来。",
			_ => $"{a}和{b}在{at}碰了个面。"
		};
		List<string> tags = new List<string> { tone, cand.Theme };
		if (cand.NoAi)
		{
			tags.Add("template_only");
		}
		if (!cand.FromLore)
		{
			tags.Add("odd_friendship");
		}
		return new NpcSocialMoment
		{
			Id = Guid.NewGuid().ToString("N"),
			First = cand.First,
			Second = cand.Second,
			Place = cand.Place,
			Theme = cand.Theme,
			Vibe = tone,
			WhatHappened = summary,
			OnDay = today.TotalDays,
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Backdrop = cand.Backdrop,
			Labels = tags,
			BondShift = DeltaFor(tone)
		};
	}

	/// <summary>账本那边发生的事（借钱、还钱、送东西），直接记成一件事。</summary>
	public static NpcSocialMoment Direct(string a, string b, string location, string archetype, string tone, string summary, bool gossip, GameDate today) => new NpcSocialMoment
	{
		Id = Guid.NewGuid().ToString("N"),
		First = a,
		Second = b,
		Place = location,
		Theme = archetype,
		Vibe = tone,
		WhatHappened = summary,
		WorthTelling = gossip,
		OnDay = today.TotalDays,
		Day = today.Day,
		Season = today.Season,
		Year = today.Year,
		Labels = new List<string> { tone, archetype },
		BondShift = DeltaFor(tone)
	};

	public static NpcSocialDelta DeltaFor(string tone)
 {
		string key = (tone ?? "").Trim();
		(float familiar, float liking, float faith, float strain, float play) = ToneShifts.TryGetValue(key, out var known) ? known : (0.04f, 0.015f, 0.01f, 0f, 0f);
		return new NpcSocialDelta { Closer = familiar, Liking = liking, Trust = faith, Friction = strain, Banter = play };
	}

	/// <summary>AI 写的那件事补齐本地信息（ID、两人、地点、日期）；设置不让村民的事变八卦时，去掉"值得传"。</summary>
	public static void Complete(NpcSocialMoment m, NpcSocialCandidate cand, GameDate today, ModConfig c)
	{
		m.Id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N") : m.Id;
		m.First = cand.First;
		m.Second = cand.Second;
		m.Place = cand.Place;
		m.Theme = string.IsNullOrWhiteSpace(m.Theme) ? cand.Theme : m.Theme;
		m.Backdrop = cand.Backdrop;
		m.OnDay = today.TotalDays;
		m.Day = today.Day;
		m.Season = today.Season;
		m.Year = today.Year;
		if (!c.MeetupsBecomeRumors)
		{
			m.WorthTelling = false;
		}
	}

	/// <summary>记下这件事：改关系、加记忆、放进最近的事。</summary>
	public void Apply(NpcSocialMoment m)
	{
		if (string.IsNullOrWhiteSpace(m.First) || string.IsNullOrWhiteSpace(m.Second))
		{
			return;
		}
		NpcSocialWebState s = State();
		NpcSocialEdge e = Find(m.First, m.Second) ?? NewEdge(s, m.First, m.Second);
		if (string.IsNullOrWhiteSpace(e.HowItStarted))
		{
			e.HowItStarted = m.Backdrop;
		}
		e.Closeness = Clamp01(e.Closeness + Math.Max(0.03f, ClampDelta(m.BondShift.Closer)));
		e.Liking = Clamp01(e.Liking + ClampDelta(m.BondShift.Liking));
		e.Trust = Clamp01(e.Trust + ClampDelta(m.BondShift.Trust));
		e.Friction = Clamp01(e.Friction + ClampDelta(m.BondShift.Friction));
		e.Banter = Clamp01(e.Banter + ClampDelta(m.BondShift.Banter));
		e.Encounters++;
		e.LastMetDay = m.OnDay;
		int cooldown = m.WorthRemembering ? 3 : m.Vibe is "tense" or "sweet" ? 2 : 1;
		e.RestUntilDay = Math.Max(e.RestUntilDay, m.OnDay + cooldown);
		foreach (string tag in m.Labels.Where(t => !string.IsNullOrWhiteSpace(t)))
		{
			if (!e.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
			{
				e.Tags.Add(tag);
			}
		}
		while (e.Tags.Count > 12)
		{
			e.Tags.RemoveAt(0);
		}
		if (!string.IsNullOrWhiteSpace(m.WhatHappened))
		{
			int importance = m.WorthRemembering ? 70 : m.WorthTelling ? 50 : 25;
			e.Shared.Insert(0, new NpcSocialMemory
			{
				Id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N") : m.Id,
				OnDay = m.OnDay,
				Place = m.Place,
				Tone = m.Vibe,
				Text = m.WhatHappened,
				Tags = m.Labels.Take(6).ToList(),
				Weight = importance,
				FadesAfterDay = m.OnDay + (importance >= 60 ? 56 : 28)
			});
			e.Shared = e.Shared.Where(x => x.FadesAfterDay >= m.OnDay).OrderByDescending(x => x.Weight).ThenByDescending(x => x.OnDay).Take(5).ToList();
		}
		e.Standing = Classify(e);
		s.Moments.Insert(0, m);
		s.Moments = s.Moments.Where(x => x.OnDay >= m.OnDay - 30).Take(30).ToList();
		foreach (NpcSocialEdge edge in s.Links)
		{
			edge.Shared = edge.Shared.Where(x => x.FadesAfterDay >= m.OnDay).Take(5).ToList();
		}
		Dirty();
	}

	public static string Classify(NpcSocialEdge e)
	{
		if (BondLines.Stranger(e))
		{
			return "unknown";
		}
		if (BondLines.Sworn(e))
		{
			return "rivalry";
		}
		if (BondLines.Strained(e))
		{
			return "tense";
		}
		if (BondLines.Uneasy(e))
		{
			return "awkward";
		}
		if (BondLines.OddlyFond(e))
		{
			return "odd_friendship";
		}
		if (BondLines.Devoted(e))
		{
			return "close";
		}
		return BondLines.Fond(e) ? "friendly" : "acquaintance";
	}

	public static string StateWord(string state) => state switch
	{
		"rivalry" => "死对头",
		"tense" => "关系紧张",
		"awkward" => "有点别扭",
		"odd_friendship" => "意想不到的朋友",
		"close" => "很要好",
		"friendly" => "处得不错",
		"acquaintance" => "点头之交",
		_ => "还不熟"
	};

	// ---- 查询 ----

	/// <summary>最近几天的事（两人都开着 AI 时），从早到晚。</summary>
	public List<NpcSocialMoment> Recent(int today, int maxAgeDays, Func<string, bool> enabled) =>
		State().Moments.Where(m => m.OnDay >= today - maxAgeDays && enabled(m.First) && enabled(m.Second)).OrderBy(m => m.OnDay).ToList();

	/// <summary>值得写成小剧场的村民互动：最近 <paramref name="maxAgeDays"/> 天里里程碑级的，或者带着别扭、较劲、和好这类标签的。</summary>
	public List<NpcSocialMoment> ArcCandidates(int today, Func<string, bool> enabled, int maxAgeDays = 21, int limit = 10) =>
		State().Moments.Where(m => m.OnDay >= today - maxAgeDays && IsArcCandidate(m))
			.OrderByDescending(m => m.WorthRemembering).ThenByDescending(m => m.OnDay).Take(Math.Max(1, limit))
			.Where(m => enabled(m.First) && enabled(m.Second)).ToList();

	public static bool IsArcCandidate(NpcSocialMoment m) => m.WorthRemembering
		|| m.Labels.Any(t => new[] { "odd_friendship", "rivalry", "soft_conflict", "tense", "repairing" }.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)));

	/// <summary>当八卦素材或八卦正文用的一句：谁和谁（在哪）：发生了什么。</summary>
	public static string Describe(NpcSocialMoment m, Func<string, string> display)
	{
		List<string> who = new[] { m.First, m.Second }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Select(display).ToList();
		string pair = who.Count >= 2 ? who[0] + "和" + who[1] : who.FirstOrDefault() ?? "";
		return $"{pair}（{Places.Name(m.Place)}）：{m.WhatHappened.Trim()}";
	}

	/// <summary>给八卦动态、外壳看的：最近的事，新的在前。</summary>
	public List<NpcSocialMoment> ForFeed(int today, int maxAgeDays = 30, int limit = 30) =>
		State().Moments.Where(m => m.OnDay >= today - maxAgeDays).OrderByDescending(m => m.OnDay).ThenBy(m => m.First, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, limit)).ToList();

	/// <summary>这两人最近 3 条还没过期的记忆（给 AI 写下一件事用）。</summary>
	public string MemoryForPair(string a, string b, int today)
	{
		NpcSocialEdge? e = Find(a, b);
		if (e == null || e.Shared.Count == 0)
		{
			return "";
		}
		return string.Join("\n", e.Shared.Where(m => m.FadesAfterDay >= today).OrderByDescending(m => m.OnDay).Take(3).Select(m => "- " + m.Text));
	}

	public NpcSocialEdge? Find(string a, string b)
	{
		string key = PairKey(a, b);
		return State().Links.FirstOrDefault(e => PairKey(e.First, e.Second) == key);
	}

	/// <summary>给 AI 的关系说明：游戏里的亲友关系（没有就说只是镇上的熟面孔），加上社交网里现在的状态。</summary>
	public static string RelationshipContext(string seed, NpcSocialEdge? edge)
	{
		string text = string.IsNullOrWhiteSpace(seed) ? "没有既定的关系，大概只是在镇上见过、偶尔打个招呼" : seed;
		if (edge == null)
		{
			return text;
		}
		return $"{text}。眼下的关系：{StateWord(edge.Standing)}；熟悉{Level(edge.Closeness)}，好感{Level(edge.Liking)}，信任{Level(edge.Trust)}，紧张{Level(edge.Friction)}，玩闹{Level(edge.Banter)}。";
	}

	public void Reset() => _trimmedFor = null;

	private IEnumerable<NpcSocialCandidate> Candidates(SocialDayInput input, NpcSocialWebState s, ModConfig c)
	{
		List<string> names = (input.EnabledNpcs.Count > 0 ? input.EnabledNpcs : BuiltInPersonas.VanillaNames)
			.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		Dictionary<string, string> seeds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string a, string b, string rel) in BuiltInPersonas.Relationships)
		{
			seeds[PairKey(a, b)] = rel;
		}
		Dictionary<string, NpcSocialEdge> edges = new Dictionary<string, NpcSocialEdge>(StringComparer.OrdinalIgnoreCase);
		foreach (NpcSocialEdge e in s.Links)
		{
			string key = PairKey(e.First, e.Second);
			if (key != "|")
			{
				edges[key] = e;
			}
		}
		for (int i = 0; i < names.Count; i++)
		{
			for (int j = i + 1; j < names.Count; j++)
			{
				string a = names[i], b = names[j];
				if (!PresentInTown(a, input.Today.Year) || !PresentInTown(b, input.Today.Year))
				{
					continue;
				}
				string key = PairKey(a, b);
				edges.TryGetValue(key, out NpcSocialEdge? edge);
				seeds.TryGetValue(key, out string? seed);
				input.Locations.TryGetValue(a, out string? locA);
				input.Locations.TryGetValue(b, out string? locB);
				if (Blocked(a, b, locA, locB))
				{
					continue;
				}
				bool same = !string.IsNullOrWhiteSpace(locA) && string.Equals(locA, locB, StringComparison.OrdinalIgnoreCase);
				NpcSocialCandidate cand = new NpcSocialCandidate
				{
					First = a,
					Second = b,
					Place = same ? locA! : locA ?? locB ?? "Town",
					WhyThem = seed ?? "",
					FromLore = !string.IsNullOrWhiteSpace(seed),
					SamePlace = same,
					AlreadyLinked = edge != null,
					HasChild = IsChild(a) || IsChild(b),
					FirstHasPersona = input.HasKnownPersonality(a),
					SecondHasPersona = input.HasKnownPersonality(b),
					RecentThemes = edge?.Shared.SelectMany(m => m.Tags).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? new List<string>()
				};
				cand.Backdrop = RelationshipContext(cand.WhyThem, edge);
				cand.Score = Score(cand, edge, input.Today.TotalDays, c);
				string money = input.Money?.Invoke(a, b) ?? "";
				if (money.Length > 0)
				{
					cand.Backdrop += (cand.Backdrop.Length > 0 ? "\n" : "") + "钱上的事：" + money;
					cand.Score += cand.Score > 0 ? 15 : 0;
				}
				if (cand.Score > 0)
				{
					yield return cand;
				}
			}
		}
	}

	/// <summary>第一年肯特还没回来。</summary>
	public static bool PresentInTown(string npc, int year) => !string.Equals(npc, "Kent", StringComparison.OrdinalIgnoreCase) || year >= 2;

	private static NpcSocialCandidate? PickWeighted(List<NpcSocialCandidate> pool, Random random)
	{
		List<NpcSocialCandidate> finalists = pool.Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(DrawPoolSize).ToList();
		int tickets = finalists.Sum(x => x.Score);
		if (tickets <= 0)
		{
			return null;
		}
		int ticket = random.Next(tickets);
		foreach (NpcSocialCandidate one in finalists)
		{
			ticket -= one.Score;
			if (ticket < 0)
			{
				return one;
			}
		}
		return finalists[^1];
	}

	private static string Archetype(NpcSocialCandidate cand, Random random, bool rare)
	{
		if (cand.HasChild)
		{
			return Pick(ChildSafe, cand.RecentThemes, random);
		}
		if (rare)
		{
			return Pick(Rare, cand.RecentThemes, random);
		}
		bool freshPair = !cand.AlreadyLinked && !cand.FromLore;
		bool seededLately = cand.RecentThemes.Contains("odd_friendship_seed", StringComparer.OrdinalIgnoreCase);
		if (freshPair && !seededLately && random.Next(100) < OddSeedPercent)
		{
			return "odd_friendship_seed";
		}
		return Pick(Common, cand.RecentThemes, random);
	}

	private static string Pick(IReadOnlyList<string> list, IReadOnlyCollection<string> recent, Random random)
	{
		List<string> unused = list.Where(x => !recent.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
		IReadOnlyList<string> bag = unused.Count > 0 ? unused : list;
		return bag[random.Next(bag.Count)];
	}

	private static bool TemplateOnly(NpcSocialCandidate cand, Random random, ModConfig c)
	{
		if (cand.Rare || cand.Theme is "town_rumor_ping" or "soft_conflict")
		{
			return false;
		}
		int chance = Math.Clamp(c.MeetupNoAiPercent, 0, 100);
		return chance > 0 && random.Next(100) < chance;
	}

	private static int Seed(string gameId, int totalDays, int chaos)
	{
		uint hash = 2166136261u;
		foreach (char ch in $"{gameId}|{totalDays}|{chaos}|npc-social")
		{
			hash ^= ch;
			hash *= 16777619;
		}
		return (int)(hash & 0x7FFFFFFF);
	}

	private static string Level(float v) => v >= 0.75f ? "高" : v >= 0.4f ? "中" : v >= 0.15f ? "低" : "很低";

	private static bool Matches(string? location, string pattern) => !string.IsNullOrWhiteSpace(location) && Regex.IsMatch(location, pattern, RegexOptions.IgnoreCase);

	private static NpcSocialEdge NewEdge(NpcSocialWebState s, string a, string b)
	{
		(string x, string y) = Sort(a, b);
		NpcSocialEdge e = new NpcSocialEdge { First = x, Second = y };
		s.Links.Add(e);
		return e;
	}

	private static (string, string) Sort(string a, string b)
	{
		string x = (a ?? "").Trim(), y = (b ?? "").Trim();
		return string.Compare(x, y, StringComparison.OrdinalIgnoreCase) > 0 ? (y, x) : (x, y);
	}

	private static float ClampDelta(float v) => Math.Clamp(v, -0.2f, 0.2f);

	private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

	private NpcSocialWebState State()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return new NpcSocialWebState();
		}
		NpcSocialWebState s = store.Get(Path, Doc);
		if (!ReferenceEquals(_trimmedFor, store))
		{
			_trimmedFor = store;
			foreach (NpcSocialEdge e in s.Links)
			{
				e.Shared = (e.Shared ?? new List<NpcSocialMemory>()).Take(5).ToList();
				e.Tags ??= new List<string>();
			}
		}
		return s;
	}

	private void Dirty() => _store()?.MarkDirty(Path);
}

/// <summary>关系词的判定线。社交网和村庄关系图用同一套，同一对人在两处说法一致。</summary>
public static class BondLines
{
	public const float StrangerFamiliarity = 0.05f;

	public const float RivalTension = 0.8f;

	public const int RivalEncounters = 3;

	public const float TenseTension = 0.55f;

	public const float AwkwardTension = 0.25f;

	public const float AwkwardAffinity = 0.3f;

	public const float CrushAffinity = 0.3f;

	public const float OddAffinity = 0.25f;

	public const float OddPlayfulness = 0.25f;

	public const float CloseAffinity = 0.7f;

	public const float CloseTrust = 0.6f;

	public const float FriendlyLine = 0.35f;

	public const string OddFriendshipTag = "odd_friendship";

	public static bool Stranger(NpcSocialEdge e) => e.Encounters <= 0 || e.Closeness < StrangerFamiliarity;

	public static bool Sworn(NpcSocialEdge e) => e.Friction >= RivalTension && e.Encounters >= RivalEncounters;

	public static bool Strained(NpcSocialEdge e) => e.Friction >= TenseTension;

	public static bool Uneasy(NpcSocialEdge e) => e.Friction >= AwkwardTension && e.Liking < AwkwardAffinity;

	public static bool OddlyFond(NpcSocialEdge e) => e.Liking >= OddAffinity && e.Banter >= OddPlayfulness && (e.Tags ?? new List<string>()).Contains(OddFriendshipTag, StringComparer.OrdinalIgnoreCase);

	public static bool Devoted(NpcSocialEdge e) => e.Liking >= CloseAffinity && e.Trust >= CloseTrust;

	public static bool Fond(NpcSocialEdge e) => e.Liking >= FriendlyLine || e.Trust >= FriendlyLine;
}

/// <summary>地点内部名 → 玩家看得懂的名字（常见地点）。</summary>
public static class Places
{
	/// <summary>说法 → 用这个说法的地点。</summary>
	private static readonly Dictionary<string, string> Names = new[]
	{
		"镇上:Town", "沙滩:Beach", "山路上:Mountain", "煤矿森林:Forest", "巴士站:BusStop", "酒馆:Saloon", "皮埃尔的铺子:SeedShop", "诊所:Hospital Hospital_Office",
		"铁匠铺:Blacksmith", "木匠铺:ScienceHouse", "玛妮家牧场:AnimalShop", "博物馆:ArchaeologyHouse", "社区中心:CommunityCenter", "Joja 超市:JojaMart",
		"铁路边:Railroad", "后山小路:Backwoods", "矿洞里:Mine", "下水道:Sewer", "法师塔:WizardHouse", "沙漠:Desert", "镇长家:ManorHouse", "拖车:Trailer Trailer_Big",
		"海莉姐妹家:HaleyHouse", "山姆家:SamHouse", "亚历克斯家:JoshHouse", "艾利欧特的木屋:ElliottHouse", "莉亚家:LeahHouse", "威利的鱼店:FishShop", "莱纳斯的帐篷:Tent",
		"林中空地:Woods Woods_Station", "姜岛南岸:IslandSouth", "农场:Farm", "农夫家:FarmHouse", "温室:Greenhouse", "地窖:Cellar", "山巅:Summit", "冒险家公会:AdventureGuild",
		"卡洛琳的茶室:Sunroom Sunroom_Caroline", "温泉:BathHouse_Pool BathHouse_Entry", "更衣室:BathHouse_MensLocker BathHouse_WomensLocker", "地下赌场:Club",
		"塞巴斯蒂安屋里:SebastianRoom", "哈维屋里:HarveyRoom", "桑迪的店:SandyHouse", "工具棚:Shed", "牲口棚:Barn", "鸡棚:Coop", "电影院:MovieTheater", "冬日夜市:BeachNightMarket",
		"姜岛北面:IslandNorth", "姜岛西面:IslandWest", "姜岛东面:IslandEast", "岛上的屋子:IslandFarmHouse", "姜岛东南角:IslandSouthEast", "核桃屋:QiNutRoom", "火山口:Caldera"
	}.Select(row => row.Split(':')).SelectMany(c => c[1].Split(' ').Select(place => (place, c[0]))).ToDictionary(p => p.place, p => p.Item2, StringComparer.OrdinalIgnoreCase);

	public static bool IsKnown(string? location) => !string.IsNullOrWhiteSpace(location) && Names.ContainsKey(location);

	public static string Name(string? location) => string.IsNullOrWhiteSpace(location) ? "镇上" : Names.TryGetValue(location, out string? n) ? n : location;
}
