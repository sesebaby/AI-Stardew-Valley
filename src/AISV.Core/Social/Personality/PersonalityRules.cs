using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Text;

namespace AISV.Core.Social.Personality;

/// <summary>村民之间的游戏里的亲友关系：哪些是夫妻、哪些是家人（不会发展恋情）。</summary>
public static class VillageRelations
{
	private static readonly (string, string)[] Married = { ("Robin", "Demetrius"), ("Pierre", "Caroline"), ("Evelyn", "George"), ("Kent", "Jodi") };

	private static readonly (string, string)[] Family = { ("Shane", "Marnie"), ("Shane", "Jas"), ("Penny", "Pam"), ("Robin", "Sebastian"), ("Maru", "Demetrius"), ("Alex", "Evelyn"), ("Emily", "Haley"), ("Sam", "Vincent") };

	public static bool IsMarriage(string a, string b) => Contains(Married, a, b);

	public static bool IsFamily(string a, string b) => Contains(Family, a, b);

	/// <summary>游戏资料里有这两人的关系（亲友、邻居、恋情……）。</summary>
	public static bool IsKnownPair(string a, string b) => BuiltInPersonas.Relationships.Any(r => Same(r.A, r.B, a, b));

	public static bool IsMarriedNpc(string npc) => Married.Any(p => string.Equals(p.Item1, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Item2, npc, StringComparison.OrdinalIgnoreCase));

	/// <summary>游戏里的三个孩子。</summary>
	public static bool IsKnownChild(string npc) => npc is "Vincent" or "Jas" or "Leo";

	private static bool Contains((string, string)[] list, string a, string b) => list.Any(p => Same(p.Item1, p.Item2, a, b));

	private static bool Same(string x, string y, string a, string b) =>
		string.Equals(x, a, StringComparison.OrdinalIgnoreCase) && string.Equals(y, b, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(x, b, StringComparison.OrdinalIgnoreCase) && string.Equals(y, a, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 从一轮对话读出性格信号：玩家的话、提议、村民刚说的那句合在一起，去掉重音后按关键词判断
/// 示好、道歉、辱骂、调情、越界、暗示出轨；海莉另外看是不是只夸外表。
/// </summary>
public static class InteractionSignals
{
	public static InteractionSignal Extract(string npc, string playerText, string? proposedAction, string npcLine, NpcReaction reaction, int hearts, int totalDay)
	{
		string text = Keywords.StripAccents($"{playerText} {proposedAction ?? ""} {npcLine}").ToLowerInvariant();
		InteractionSignal s = new InteractionSignal { Npc = npc, OnDay = totalDay };
		string emotion = reaction.Emotion?.ToLowerInvariant() ?? "neutral";
		bool positive = ExchangeTone.Good(reaction);
		bool strongPositive = reaction.FriendshipPoints >= 75 || emotion == "love";
		bool negative = ExchangeTone.Bad(reaction);
		bool insult = Has(text, Keywords.SignalInsult);
		bool apology = Has(text, Keywords.SignalApology);
		bool supportive = Has(text, Keywords.SignalSupportive);
		bool flirt = Has(text, Keywords.SignalFlirt);
		bool boundary = Has(text, Keywords.SignalBoundary) || flirt && insult;
		s.Flirty = flirt;
		s.CrossedLine = boundary;
		s.FlirtyPolitely = flirt && !boundary && !insult && !negative;
		s.TemptsCheating = flirt && Has(text, Keywords.SignalInfidelity);
		if (flirt)
		{
			s.Labels.Add(s.FlirtyPolitely ? "respectful_flirt" : "boundary_violation");
			Add(s, "romantic_openness", s.FlirtyPolitely && positive ? 0.035f : -0.025f);
			s.Why = s.FlirtyPolitely ? "农夫的示好留了余地，让对方自己选。" : "农夫的示好越了界，让气氛变得不自在。";
		}
		if (supportive || positive)
		{
			s.Labels.Add("supportive");
			Add(s, "openness", strongPositive ? 0.04f : 0.025f);
			Add(s, "kindness", 0.02f);
			Add(s, "resentment", -0.02f);
			s.MakesSofter = strongPositive || supportive;
			s.Why = "农夫的回应让人觉得被支持、可以放心。";
		}
		if (apology)
		{
			s.Labels.Add("apology");
			Add(s, "honesty", 0.03f);
			Add(s, "openness", 0.02f);
			Add(s, "resentment", -0.03f);
			s.Why = "农夫道了歉，想把关系补回来。";
		}
		if (negative || insult)
		{
			s.Labels.Add(insult ? "insult" : "negative_exchange");
			Add(s, "openness", insult ? -0.07f : -0.04f);
			Add(s, "kindness", -0.025f);
			Add(s, "resentment", insult ? 0.07f : 0.04f);
			Add(s, "social_confidence", -0.02f);
			s.MakesGuarded = true;
			s.Why = insult ? "农夫说话伤人或带着嘲弄，让对方更戒备了。" : "这次说话没说到一块去，留下了疙瘩。";
		}
		if (string.Equals(npc, "Haley", StringComparison.OrdinalIgnoreCase))
		{
			Haley(s, text, supportive, positive, insult, hearts);
		}
		if (s.Nudges.Count == 0 && reaction.FriendshipPoints > 20)
		{
			s.Labels.Add("small_warmth");
			Add(s, "openness", 0.01f);
			s.Why = "这次聊得还算愉快，对方稍微放松了一点。";
		}
		return s;
	}

	private static void Haley(InteractionSignal s, string text, bool supportive, bool positive, bool insult, int hearts)
	{
		bool photo = Has(text, Keywords.SignalPhotography);
		bool looksOnly = Has(text, Keywords.SignalAppearance) && !photo;
		if ((supportive || positive) && photo)
		{
			s.Labels.Add("photography_respect");
			Add(s, "openness", hearts >= 4 ? 0.05f : 0.035f);
			Add(s, "kindness", 0.03f);
			Add(s, "vanity", -0.03f);
			Add(s, "resentment", -0.02f);
			s.MakesSofter = true;
			s.Why = "农夫看重的是海莉的摄影和她这个人，不只是长相。";
		}
		else if (looksOnly && !positive)
		{
			s.Labels.Add("appearance_focus");
			Add(s, "vanity", 0.02f);
			Add(s, "openness", -0.01f);
			s.Why = "这次又让海莉觉得别人只看她的外表和身份。";
		}
		if (insult && Has(text, Keywords.SignalAppearance))
		{
			s.Labels.Add("mock_appearance");
			Add(s, "openness", -0.04f);
			Add(s, "resentment", 0.04f);
			s.MakesGuarded = true;
			s.Why = "海莉觉得自己的外表被评头论足、被取笑，于是竖起了刺。";
		}
	}

	private static void Add(InteractionSignal s, string trait, float delta)
	{
		s.Nudges.TryGetValue(trait, out float v);
		s.Nudges[trait] = Math.Clamp(v + delta, -0.08f, 0.08f);
	}

	private static bool Has(string text, IReadOnlyList<string> words) => words.Any(w => text.Contains(Keywords.StripAccents(w), StringComparison.OrdinalIgnoreCase));
}

/// <summary>性格怎么随对话慢慢变：每项每次最多变 0.08，限制在 -1～1；情绪压力最多 5 条，转折点留 8 条。</summary>
public static class PersonalityEvolution
{
	public const string GuardedId = "guarded_after_hurt";

	public const string SofteningId = "softening_after_support";

	public static bool Apply(NpcPersonalityState state, InteractionSignal signal)
	{
		if (signal.Nudges.Count == 0 && !signal.MakesGuarded && !signal.MakesSofter)
		{
			return false;
		}
		bool changed = false;
		foreach ((string trait, float raw) in signal.Nudges)
		{
			if (Math.Abs(raw) < 0.001f)
			{
				continue;
			}
			PersonalityTraitState t = Trait(state, trait);
			float delta = Math.Clamp(raw, -0.08f, 0.08f);
			t.Level = Math.Clamp(t.Level + delta, -1f, 1f);
			t.Certainty = Math.Clamp(t.Certainty + Math.Abs(delta) * 1.75f, 0f, 1f);
			t.Why = signal.Why;
			t.ChangedOn = signal.OnDay;
			changed = true;
		}
		if (signal.MakesGuarded)
		{
			Modifier(state, GuardedId, "最近一次不愉快的来往之后，变得更有戒心。", signal.OnDay, 7, 0.65f);
			changed = true;
		}
		if (signal.MakesSofter)
		{
			Modifier(state, SofteningId, "觉得被理解了，心里多了一分信任。", signal.OnDay, 5, 0.45f);
			changed = true;
		}
		if (changed)
		{
			state.UpdatedOn = signal.OnDay;
			Milestones(state, signal.OnDay);
			UpdateSummary(state);
		}
		return changed;
	}

	/// <summary>过一天：去掉过期的情绪压力（留最强的 5 条），转折点留最近 8 个。有变化返回 true。</summary>
	public static bool AdvanceDay(NpcPersonalityState state, int today)
	{
		int before = state.Pressures.Count;
		state.Pressures = state.Pressures.Where(m => !m.UntilDay.HasValue || m.UntilDay >= today)
			.OrderByDescending(m => m.Strength).ThenByDescending(m => m.SinceDay).Take(5).ToList();
		state.TurningPoints = state.TurningPoints.OrderByDescending(m => m.OnDay).Take(8).ToList();
		if (state.Pressures.Count == before)
		{
			return false;
		}
		UpdateSummary(state);
		return true;
	}

	/// <summary>一句话总结：变化最明显的 3 项、最近的转折点、最强的情绪压力。</summary>
	public static void UpdateSummary(NpcPersonalityState state)
	{
		List<string> traits = state.Traits.Where(p => Math.Abs(p.Value.Level) >= 0.08f || p.Value.Certainty >= 0.2f)
			.OrderByDescending(p => Math.Abs(p.Value.Level)).Take(3).Select(p => TraitWord(p.Key, p.Value.Level)).Where(t => t.Length > 0).ToList();
		if (traits.Count == 0 && state.TurningPoints.Count == 0 && state.Pressures.Count == 0)
		{
			state.SelfPortrait = "";
			return;
		}
		List<string> parts = new List<string>();
		if (traits.Count > 0)
		{
			parts.Add("最近" + string.Join("、", traits) + "。");
		}
		PersonalityMilestone? turn = state.TurningPoints.OrderByDescending(m => m.OnDay).FirstOrDefault();
		if (turn != null)
		{
			parts.Add("转折：" + turn.Text);
		}
		PersonalityModifier? pressure = state.Pressures.OrderByDescending(m => m.Strength).FirstOrDefault();
		if (pressure != null)
		{
			parts.Add("眼下：" + pressure.Text);
		}
		state.SelfPortrait = string.Join(" ", parts);
	}

	public static string TraitWord(string trait, float value) => trait.ToLowerInvariant() switch
	{
		"kindness" => value >= 0 ? "待人更和善" : "说话更带刺",
		"openness" => value >= 0 ? "更愿意敞开心扉" : "更爱自我防备",
		"vanity" => value >= 0 ? "更在意面子和身份" : "没那么看重身份了",
		"resentment" => value >= 0 ? "心里积了更多怨气" : "怨气消了些",
		"honesty" => value >= 0 ? "说话更坦率" : "更爱回避",
		"social_confidence" => value >= 0 ? "和人打交道更有底气" : "和人打交道没那么有底气了",
		"romantic_openness" => value >= 0 ? "对感情更放得开" : "对感情更谨慎",
		_ => (value >= 0 ? "更多" : "更少") + trait
	};

	public static string TraitName(string trait) => trait.ToLowerInvariant() switch
	{
		"kindness" => "和善",
		"openness" => "敞开心扉",
		"vanity" => "在意面子",
		"resentment" => "怨气",
		"honesty" => "坦率",
		"social_confidence" => "社交底气",
		"romantic_openness" => "对感情放得开",
		_ => trait
	};

	private static void Milestones(NpcPersonalityState state, int day)
	{
		if (string.Equals(state.Npc, "Haley", StringComparison.OrdinalIgnoreCase) && At(state, "openness", 0.3f))
		{
			AddMilestone(state, "haley_seen_beyond_beauty", "海莉开始相信，农夫看到的不只是她的漂亮和身份。", day);
		}
		if (At(state, "resentment", 0.35f))
		{
			AddMilestone(state, "guarded_by_resentment", "几次闹僵之后，心里的怨气已经藏不住了。", day);
		}
		if (At(state, "openness", 0.4f))
		{
			AddMilestone(state, "opening_up", "明显变得更坦诚、更愿意交心了。", day);
		}
	}

	private static void AddMilestone(NpcPersonalityState state, string id, string summary, int day)
	{
		if (!state.TurningPoints.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
		{
			state.TurningPoints.Insert(0, new PersonalityMilestone { Id = id, Text = summary, OnDay = day, Private = true });
		}
	}

	private static bool At(NpcPersonalityState state, string trait, float min) => state.Traits.TryGetValue(trait, out PersonalityTraitState? t) && t.Level >= min;

	private static PersonalityTraitState Trait(NpcPersonalityState state, string trait)
	{
		if (!state.Traits.TryGetValue(trait, out PersonalityTraitState? t))
		{
			t = new PersonalityTraitState();
			state.Traits[trait] = t;
		}
		return t;
	}

	private static void Modifier(NpcPersonalityState state, string id, string summary, int day, int days, float intensity)
	{
		PersonalityModifier? m = state.Pressures.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
		if (m == null)
		{
			state.Pressures.Insert(0, new PersonalityModifier
			{
				Id = id,
				CauseKind = "player_interaction",
				CauseId = $"{state.Npc}-{day}-{id}",
				Text = summary,
				SinceDay = day,
				UntilDay = day + days,
				Strength = intensity,
				Private = true
			});
		}
		else
		{
			m.SinceDay = day;
			m.UntilDay = day + days;
			m.Strength = Math.Clamp(Math.Max(m.Strength, intensity), 0f, 1f);
			m.Text = summary;
		}
		state.Pressures = state.Pressures.OrderByDescending(x => x.Strength).ThenByDescending(x => x.SinceDay).Take(5).ToList();
	}
}

/// <summary>关系图（村民和村民、村民和农夫）的规则。</summary>
public static class RelationshipGraph
{
	public const string Farmer = "Farmer";

	public static RelationshipEdgeState GetOrCreate(List<RelationshipEdgeState> edges, string a, string b)
	{
		(string left, string right) = Sort(a, b);
		RelationshipEdgeState? found = edges.FirstOrDefault(e => string.Equals(e.A, left, StringComparison.OrdinalIgnoreCase) && string.Equals(e.B, right, StringComparison.OrdinalIgnoreCase));
		if (found != null)
		{
			return found;
		}
		bool married = VillageRelations.IsMarriage(left, right);
		RelationshipEdgeState edge = new RelationshipEdgeState
		{
			A = left,
			B = right,
			Kind = married ? "marriage" : "acquaintance",
			Devotion = married ? 0.9f : 0f,
			Trust = VillageRelations.IsKnownPair(left, right) ? 0.55f : 0.35f,
			Reserve = 0.7f,
			Phase = married ? "married" : "stable"
		};
		edges.Add(edge);
		return edge;
	}

	public static IEnumerable<RelationshipEdgeState> EdgesFor(IEnumerable<RelationshipEdgeState> edges, string npc, int max = 3) =>
		edges.Where(e => string.Equals(e.A, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(e.B, npc, StringComparison.OrdinalIgnoreCase))
			.Where(PromptWorthy).OrderByDescending(Weight).ThenByDescending(e => e.ChangedOn).Take(max);

	/// <summary>村民之间发生了一件事：按它的影响改关系，甜蜜的事可能让两人萌生好感，紧张多了会变僵。有变化返回 true。</summary>
	public static bool ApplyMoment(List<RelationshipEdgeState> edges, SocialMomentEffect m, ModConfig config, Func<string, string> display)
	{
		if (string.IsNullOrWhiteSpace(m.Npc1) || string.IsNullOrWhiteSpace(m.Npc2))
		{
			return false;
		}
		RelationshipEdgeState e = GetOrCreate(edges, m.Npc1, m.Npc2);
		string before = Snapshot(e);
		e.Fondness = Clamp01(e.Fondness + Math.Clamp(m.Affinity, -0.12f, 0.12f));
		e.Trust = Clamp01(e.Trust + Math.Clamp(m.Trust, -0.1f, 0.1f));
		e.Strain = Clamp01(e.Strain + Math.Clamp(m.Tension, -0.12f, 0.12f));
		e.Interest = Clamp01(e.Interest + Math.Clamp(m.Playfulness, -0.08f, 0.1f));
		if (m.Tone is "sweet" or "funny")
		{
			e.Fondness = Clamp01(e.Fondness + 0.03f);
		}
		if (m.Tone is "tense" or "awkward")
		{
			e.Strain = Clamp01(e.Strain + 0.04f);
		}
		bool romantic = m.Tone == "sweet" || m.Tags.Any(t => t.Contains("romance", StringComparison.OrdinalIgnoreCase) || t.Contains("crush", StringComparison.OrdinalIgnoreCase)) || m.Archetype.Contains("bond", StringComparison.OrdinalIgnoreCase);
		if (config.VillagerCouplesOn && RomanceAllowed(e.A, e.B, config) && romantic)
		{
			e.Spark = Clamp01(e.Spark + 0.05f);
			e.Interest = Clamp01(e.Interest + 0.04f);
			if (e.Spark >= 0.3f && e.Phase is "stable" or "warming")
			{
				SetStatus(e, "crush", m.TotalDay, $"{display(m.Npc1)}和{display(m.Npc2)}几次相处下来都很温馨，悄悄有了一点火花。");
			}
		}
		if (e.Phase == "stable" && e.Fondness >= 0.25f)
		{
			e.Phase = "warming";
		}
		if (e.Strain >= 0.45f && e.Phase is "stable" or "warming")
		{
			e.Phase = "strained";
		}
		e.ChangedOn = m.TotalDay;
		return Snapshot(e) != before;
	}

	public static string PromptBlock(IEnumerable<RelationshipEdgeState> edges, string npc, Func<string, bool> enabled, Func<string, string> display)
	{
		List<RelationshipEdgeState> list = EdgesFor(edges, npc).Where(e => Eligible(e, enabled)).ToList();
		if (list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("这个存档里一直延续着的几段关系：");
		foreach (RelationshipEdgeState e in list)
		{
			string other = string.Equals(e.A, npc, StringComparison.OrdinalIgnoreCase) ? e.B : e.A;
			sb.Append("- 和").Append(other == Farmer ? "农夫" : display(other)).Append("：").Append(Describe(e));
			if (e.RecentShifts.Count > 0)
			{
				sb.Append(" 最近的转折：").Append(e.RecentShifts[0]);
			}
			sb.AppendLine();
		}
		sb.AppendLine("这些只是私下的连续性。尊重分寸，别一下子跳到轰轰烈烈的恋爱或背叛。");
		return sb.ToString().Trim();
	}

	public static bool Eligible(RelationshipEdgeState e, Func<string, bool> enabled) => EntityOk(e.A, enabled) && EntityOk(e.B, enabled);

	public static bool RomanceAllowed(string a, string b, ModConfig config)
	{
		if (VillageRelations.IsKnownChild(a) || VillageRelations.IsKnownChild(b) || VillageRelations.IsFamily(a, b))
		{
			return false;
		}
		return !(config.SafeguardGameCouples && VillageRelations.IsMarriage(a, b));
	}

	public static bool SetStatus(RelationshipEdgeState e, string status, int day, string turningPoint)
	{
		if (string.Equals(e.Phase, status, StringComparison.OrdinalIgnoreCase) && e.RecentShifts.Contains(turningPoint, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}
		e.Phase = status;
		e.ChangedOn = day;
		e.RecentShifts.Insert(0, turningPoint);
		e.RecentShifts = e.RecentShifts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
		return true;
	}

	public static void Normalize(RelationshipEdgeState e)
	{
		(e.A, e.B) = Sort(e.A ?? "", e.B ?? "");
		e.Kind ??= "acquaintance";
		e.Phase = string.IsNullOrWhiteSpace(e.Phase) ? "stable" : e.Phase;
		e.RecentShifts ??= new List<string>();
		e.Fondness = Clamp01(e.Fondness);
		e.Spark = Clamp01(e.Spark);
		e.Trust = Clamp01(e.Trust);
		e.Devotion = Clamp01(e.Devotion);
		e.Strain = Clamp01(e.Strain);
		e.Grudge = Clamp01(e.Grudge);
		e.Interest = Clamp01(e.Interest);
		e.Reserve = Clamp01(e.Reserve <= 0f ? 0.7f : e.Reserve);
	}

	public static string StatusWord(string status) => status switch
	{
		"crush" => "暗生好感",
		"questioning_attraction" => "有点心动",
		"boundary_set" => "划了界线",
		"affair_contemplation" => "危险的心动",
		"strained" => "关系紧张",
		"warming" => "越来越熟",
		"married" => "夫妻",
		_ => "平常"
	};

	private static string Describe(RelationshipEdgeState e) => e.Phase switch
	{
		"crush" => "一份私下的好感正在慢慢长出来。",
		"questioning_attraction" => "有些小心翼翼的心动，还藏在心里、拿不准。",
		"boundary_set" => "感情上划过一道界线，要尊重。",
		"affair_contemplation" => "成年人之间危险的心动，夹着愧疚和诱惑；要慢，要尊重各自的选择。",
		"strained" => "关系有点僵，能感觉到别扭。",
		"warming" => "关系在慢慢变好。",
		"married" => "两人是夫妻，这份承诺是根基。",
		_ => $"亲近 {e.Fondness:0.00}，心动 {e.Spark:0.00}，紧张 {e.Strain:0.00}。"
	};

	private static bool PromptWorthy(RelationshipEdgeState e) =>
		e.Phase is "crush" or "questioning_attraction" or "boundary_set" or "affair_contemplation" or "strained" or "warming"
		|| e.Spark >= 0.25f || e.Strain >= 0.35f || e.RecentShifts.Count > 0;

	private static float Weight(RelationshipEdgeState e) => Math.Max(e.Spark, e.Strain) + e.Interest + (e.Phase == "affair_contemplation" ? 1f : 0f);

	private static bool EntityOk(string entity, Func<string, bool> enabled) => string.Equals(entity, Farmer, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(entity) && enabled(entity);

	private static (string, string) Sort(string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0 ? (b, a) : (a, b);

	private static string Snapshot(RelationshipEdgeState e) => string.Join("|", e.Fondness.ToString("0.000"), e.Spark.ToString("0.000"), e.Trust.ToString("0.000"), e.Strain.ToString("0.000"), e.Interest.ToString("0.000"), e.Phase, e.RecentShifts.Count);

	private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}

/// <summary>农夫对成年村民示好时，村民私下的感情变化。</summary>
public static class RomanceDiscovery
{
	public static bool Apply(NpcPersonalityState state, RelationshipEdgeState farmer, InteractionSignal s, ModConfig config, int hearts, bool isAdult, string display)
	{
		if (!s.Flirty || !isAdult || VillageRelations.IsKnownChild(state.Npc))
		{
			return false;
		}
		if (s.CrossedLine)
		{
			farmer.Reserve = Clamp01(farmer.Reserve + 0.18f);
			farmer.Strain = Clamp01(farmer.Strain + 0.12f);
			bool set = RelationshipGraph.SetStatus(farmer, "boundary_set", s.OnDay, $"{display}觉得感情上的分寸被越过了，需要更多尊重。");
			PrivateNote(state, $"{display}私下给农夫划了一道感情上的界线。");
			return set;
		}
		if (!s.FlirtyPolitely || hearts < config.SelfDiscoveryHearts)
		{
			farmer.Interest = Clamp01(farmer.Interest + 0.01f);
			farmer.Reserve = Clamp01(farmer.Reserve + 0.02f);
			return true;
		}
		farmer.Fondness = Clamp01(farmer.Fondness + 0.04f);
		farmer.Spark = Clamp01(farmer.Spark + 0.06f);
		farmer.Interest = Clamp01(farmer.Interest + 0.05f);
		farmer.Reserve = Clamp01(farmer.Reserve - 0.03f);
		farmer.Secret = true;
		bool changed = true;
		if (VillageRelations.IsMarriedNpc(state.Npc))
		{
			return Married(state, farmer, s, config, display) | changed;
		}
		if (config.SelfDiscoveryOn)
		{
			changed |= Identity(state, farmer, s, config, display);
		}
		if (farmer.Phase is "stable" or "warming" && farmer.Spark >= 0.28f)
		{
			changed |= RelationshipGraph.SetStatus(farmer, "questioning_attraction", s.OnDay, $"{display}私下对农夫的示好有些好奇，但还没有任何承诺。");
		}
		farmer.ChangedOn = s.OnDay;
		return changed;
	}

	private static bool Identity(NpcPersonalityState state, RelationshipEdgeState farmer, InteractionSignal s, ModConfig config, string display)
	{
		NpcIdentityProfile id = state.Identity;
		id.RomanticSignals++;
		id.Stage = string.IsNullOrWhiteSpace(id.Stage) ? "unexplored" : id.Stage;
		id.DrawnToSame = Clamp01(id.DrawnToSame + 0.06f);
		id.LabelCertainty = Clamp01(id.LabelCertainty + 0.035f);
		if (id.DrawnToSame >= 0.18f && id.Stage == "unexplored")
		{
			id.Stage = "questioning";
			PrivateNote(state, $"{display}私下在琢磨自己被吸引的方式，不希望这件事被当成八卦传出去。");
		}
		if (id.DrawnToSame >= 0.35f && id.LabelCertainty >= 0.18f && id.RomanticSignals >= Math.Max(1, config.SelfDiscoverySignals))
		{
			id.RomanticLabel = "queer";
			id.SexualLabel = "unlabeled";
			id.Stage = "private_acceptance";
			RelationshipGraph.SetStatus(farmer, "questioning_attraction", s.OnDay, $"{display}私下接受了自己不贴标签的心动，没有对外说。");
		}
		return true;
	}

	private static bool Married(NpcPersonalityState state, RelationshipEdgeState farmer, InteractionSignal s, ModConfig config, string display)
	{
		if (!config.AdultDramaOn() || !config.InfidelityOn() || config.SafeguardGameCouples)
		{
			farmer.Reserve = Clamp01(farmer.Reserve + 0.08f);
			farmer.Strain = Clamp01(farmer.Strain + 0.04f);
			return RelationshipGraph.SetStatus(farmer, "boundary_set", s.OnDay, $"{display}察觉到了示好，但因为已有的承诺守住了分寸。");
		}
		if (!s.TemptsCheating && farmer.Spark < 0.55f)
		{
			return false;
		}
		farmer.Strain = Clamp01(farmer.Strain + 0.14f);
		farmer.Reserve = Clamp01(farmer.Reserve - 0.08f);
		farmer.Secret = true;
		PrivateNote(state, $"{display}被一份危险的心动和对家人的愧疚拉扯着。");
		return RelationshipGraph.SetStatus(farmer, "affair_contemplation", s.OnDay, $"{display}动摇了却很矛盾；这条线必须慢、要双方情愿，也要有后果。");
	}

	private static void PrivateNote(NpcPersonalityState state, string note)
	{
		if (!state.Identity.QuietMoments.Contains(note, StringComparer.OrdinalIgnoreCase))
		{
			state.Identity.QuietMoments.Insert(0, note);
			state.Identity.QuietMoments = state.Identity.QuietMoments.Take(5).ToList();
		}
	}

	private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}
