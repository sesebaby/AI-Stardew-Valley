using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Favors;
using AISV.Core.Persona;
using AISV.Core.Social.Drama;
using AISV.Core.Social.Personality;
using AISV.Core.Social.Web;
using AISV.Core.Stories;
using AISV.Core.Text;

namespace AISV.Core.Social;

public enum VillagerChapter
{
	Heart,
	Today,
	ToSay,
	Asks,
	Stories,
	Others,
	Changes,
	Beliefs
}

public enum DiaryInk
{
	Heading,
	Text,
	Faint,
	Warm,
	Sore
}

public sealed record DiaryLine(string Text, DiaryInk Ink = DiaryInk.Text);

public sealed record FeelingLine(string Meter, int Tier, int Tiers, bool Spoken, bool WalkingOver, bool Carried, string Reward);

/// <summary>一位村民今天被什么触动了、还看到了什么。</summary>
public sealed record NoticingView(bool On, IReadOnlyList<FeelingLine> Feelings, string? Aside, IReadOnlyList<string> Sights)
{
	public static readonly NoticingView Off = new NoticingView(false, Array.Empty<FeelingLine>(), null, Array.Empty<string>());
}

public sealed class VillagerFacts
{
	public string Npc { get; init; } = "";

	public string Name { get; init; } = "";

	public int Today { get; init; }

	public int AfterwordDays { get; init; }

	public EmotionalProfile? Profile { get; init; }

	public NoticingView Noticing { get; init; } = NoticingView.Off;

	public IReadOnlyList<NpcFavor> Favors { get; init; } = Array.Empty<NpcFavor>();

	public IReadOnlyList<ItemRequestFavor> ItemRequests { get; init; } = Array.Empty<ItemRequestFavor>();

	public IReadOnlyList<StoryArc> Arcs { get; init; } = Array.Empty<StoryArc>();

	public IReadOnlyList<NpcSocialEdge> Edges { get; init; } = Array.Empty<NpcSocialEdge>();

	public NpcPersonalityState? Personality { get; init; }

	public IReadOnlyList<Rumor> Rumors { get; init; } = Array.Empty<Rumor>();

	public IReadOnlyList<Opinion> Opinions { get; init; } = Array.Empty<Opinion>();

	public Func<string, string> Display { get; init; } = n => n;
}

/// <summary>村民日记：把存档里关于一位村民的记录，写成农夫能读的几章。</summary>
public static class VillagerDiary
{
	public const int MaxOthers = 8;

	public const int MaxSharedMemories = 2;

	public const int MaxClosedAsks = 4;

	public const int MaxExpiredTopics = 2;

	public const int MaxRumors = 4;

	public const int MaxOpinions = 2;

	public const float MinTraitShift = 0.08f;

	public const float MinOpinionStrength = 0.2f;

	private static readonly string[] RomanticWords = { "恋", "暧昧", "调情", "亲吻", "接吻", "约会", "心动", "表白", "求婚", "romance", "romantic", "flirt", "kiss", "dating" };

	public static string ChapterTitle(VillagerChapter chapter) => chapter switch
	{
		VillagerChapter.Heart => Tr.Get("vdiary.chapter.heart", "对你的感觉"),
		VillagerChapter.Today => Tr.Get("vdiary.chapter.today", "今天的心事"),
		VillagerChapter.ToSay => Tr.Get("vdiary.chapter.toSay", "想跟你说的"),
		VillagerChapter.Asks => Tr.Get("vdiary.chapter.asks", "托你办的事"),
		VillagerChapter.Stories => Tr.Get("vdiary.chapter.stories", "卷进的小剧场"),
		VillagerChapter.Others => Tr.Get("vdiary.chapter.others", "和别的村民"),
		VillagerChapter.Changes => Tr.Get("vdiary.chapter.changes", "这些日子的变化"),
		_ => Tr.Get("vdiary.chapter.beliefs", "听来的和信的")
	};

	public static List<DiaryLine> Write(VillagerChapter chapter, VillagerFacts f) => chapter switch
	{
		VillagerChapter.Heart => Heart(f),
		VillagerChapter.Today => Today(f),
		VillagerChapter.ToSay => ToSay(f),
		VillagerChapter.Asks => Asks(f),
		VillagerChapter.Stories => Stories(f),
		VillagerChapter.Others => Others(f),
		VillagerChapter.Changes => Changes(f),
		_ => Beliefs(f)
	};

	private static List<DiaryLine> Heart(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		EmotionalProfile? p = f.Profile;
		List<NegativeMemory> sores = p?.Sores.Where(m => m.HealsOn <= 0 || m.HealsOn >= f.Today).OrderByDescending(m => m.Sting).ThenByDescending(m => m.SinceDay).ToList() ?? new List<NegativeMemory>();
		if (p == null || (string.IsNullOrWhiteSpace(p.Impression) && p.Recollections.Count == 0 && sores.Count == 0))
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.heart.none", "{{name}}对你还没拿定看法。多聊几回，慢慢就有了。", ("name", f.Name)), DiaryInk.Faint));
			return lines;
		}
		lines.Add(new DiaryLine(Tr.Get("vdiary.heart.trust", "信任：{{word}}", ("word", TrustWord(p.FaithInFarmer)))));
		lines.Add(new DiaryLine(Tr.Get("vdiary.heart.warmth", "亲近：{{word}}", ("word", WarmthWord(p.Affection)))));
		if (!string.IsNullOrWhiteSpace(p.Impression))
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.heart.impression", "在{{name}}眼里，你是：{{text}}", ("name", f.Name), ("text", p.Impression.Trim()))));
		}
		if (!string.IsNullOrWhiteSpace(p.LatestMoment))
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.heart.latest", "最近的一件事：{{text}}", ("text", p.LatestMoment.Trim()))));
		}
		AddSection(lines, Tr.Get("vdiary.heart.memories", "忘不掉的事"), p.Recollections.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => new DiaryLine("· " + s.Trim())));
		AddSection(lines, Tr.Get("vdiary.heart.sores", "心里的疙瘩"), sores.Select(m => new DiaryLine("· " + m.Text.Trim() + Fades(m.HealsOn, f.Today), DiaryInk.Sore)));
		AddSection(lines, Tr.Get("vdiary.heart.stories", "一起经历过的故事"), p.StoriesLived.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => new DiaryLine("· " + s.Trim())));
		return lines;
	}

	public static string TrustWord(float trust) => trust switch
	{
		>= 0.8f => Tr.Get("vdiary.trust.4", "把你当自己人"),
		>= 0.6f => Tr.Get("vdiary.trust.3", "信得过你"),
		>= 0.4f => Tr.Get("vdiary.trust.2", "慢慢习惯你了"),
		>= 0.2f => Tr.Get("vdiary.trust.1", "还留着几分戒心"),
		_ => Tr.Get("vdiary.trust.0", "不信你")
	};

	public static string WarmthWord(float warmth) => warmth switch
	{
		>= 0.5f => Tr.Get("vdiary.warmth.4", "很喜欢你"),
		>= 0.15f => Tr.Get("vdiary.warmth.3", "挺喜欢你"),
		> -0.15f => Tr.Get("vdiary.warmth.2", "不冷不热"),
		> -0.5f => Tr.Get("vdiary.warmth.1", "躲着你点"),
		_ => Tr.Get("vdiary.warmth.0", "烦你")
	};

	private static string Fades(int healsOn, int today)
	{
		if (healsOn <= 0)
		{
			return "";
		}
		int days = healsOn - today;
		return days <= 1 ? Tr.Get("vdiary.fadesSoon", "（快过去了）") : Tr.Get("vdiary.fadesIn", "（还要 {{n}} 天才放下）", ("n", days));
	}

	private static List<DiaryLine> Today(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		if (!f.Noticing.On)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.today.off", "“村民会注意到你做的事”关着，今天没有记录。"), DiaryInk.Faint));
			return lines;
		}
		if (f.Noticing.Feelings.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.today.none", "今天你做的事，还没让{{name}}放在心上。", ("name", f.Name)), DiaryInk.Faint));
			return lines;
		}
		foreach (FeelingLine feeling in f.Noticing.Feelings)
		{
			string name = MeterName(feeling.Meter);
			lines.Add(new DiaryLine(feeling.Tier > 0 ? Tr.Get("vdiary.today.tier", "{{meter}}：第 {{tier}} 档，共 {{tiers}} 档", ("meter", name), ("tier", feeling.Tier), ("tiers", feeling.Tiers)) : Tr.Get("vdiary.today.slight", "{{meter}}：有一点", ("meter", name)), DiaryInk.Heading));
			if (feeling.WalkingOver)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.today.walking", "正朝你走过来，要说这件事"), DiaryInk.Warm));
			}
			else if (feeling.Spoken)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.today.spoken", "今天已经说过了"), DiaryInk.Faint));
			}
			if (feeling.Carried)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.today.carried", "到明天还放不下"), DiaryInk.Sore));
			}
			if (feeling.Reward.Length > 0)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.today.reward", "想送你：{{item}}", ("item", feeling.Reward)), DiaryInk.Warm));
			}
		}
		return lines;
	}

	public static string MeterName(string meter) => meter.ToLowerInvariant() switch
	{
		"confront" => Tr.Get("vdiary.meter.confront", "火气"),
		"humor" => Tr.Get("vdiary.meter.humor", "好笑"),
		"tenderness" => Tr.Get("vdiary.meter.tenderness", "暖意"),
		"love" => Tr.Get("vdiary.meter.love", "心动"),
		_ => meter
	};

	private static string MeterWish(string meter) => meter.ToLowerInvariant() switch
	{
		"confront" => Tr.Get("vdiary.wish.confront", "想找你问个明白"),
		"humor" => Tr.Get("vdiary.wish.humor", "想拿这事逗逗你"),
		"tenderness" => Tr.Get("vdiary.wish.tenderness", "想对你说句贴心话"),
		"love" => Tr.Get("vdiary.wish.love", "有句心里话想告诉你"),
		_ => Tr.Get("vdiary.wish.other", "有话想说")
	};

	private static List<DiaryLine> ToSay(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		if (f.Noticing.Feelings.FirstOrDefault(x => x.WalkingOver) is FeelingLine walking)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.walking", "正要过来找你：{{what}}", ("what", MeterWish(walking.Meter))), DiaryInk.Warm));
		}
		if (f.Noticing.Aside is string aside)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.aside", "留着下回聊：{{what}}", ("what", MeterWish(aside)))));
		}
		foreach (StoryArc arc in f.Arcs.Where(a => a.State == StoryArc.Resolved && a.EndedOn is int day && f.Today - day <= f.AfterwordDays
			&& (a.Afterwords?.Keys.Any(k => Same(k, f.Npc)) ?? false) && !a.AfterwordsSpoken.Any(n => Same(n, f.Npc))))
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.afterword", "想聊聊《{{title}}》的结局", ("title", arc.Title.Trim()))));
		}
		foreach (NpcFavor favor in f.Favors.Where(x => x.Phase == "expired" && Same(x.Asker, f.Npc)).OrderByDescending(x => x.ExpiredTotalDay).Take(MaxExpiredTopics))
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.expired", "还记着你没办的事：{{what}}", ("what", favor.Ask.Trim())), DiaryInk.Sore));
		}
		foreach (string wound in f.Profile?.Climate?.OpenWounds.Where(s => !string.IsNullOrWhiteSpace(s)) ?? Enumerable.Empty<string>())
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.wound", "你们之间还有个结：{{what}}", ("what", wound.Trim())), DiaryInk.Sore));
		}
		AddSection(lines, Tr.Get("vdiary.say.sights", "看到的事，说不定会提起"), f.Noticing.Sights.Select(s => new DiaryLine("· " + s)));
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.say.none", "{{name}}眼下没攒着什么要跟你说。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	private sealed record AskRow(bool Open, int Left, int Age, string What, string Status, string Extra);

	private static List<DiaryLine> Asks(VillagerFacts f)
	{
		List<AskRow> rows = new List<AskRow>();
		foreach (NpcFavor x in f.Favors.Where(x => Same(x.Asker, f.Npc)))
		{
			string what = x.Ask.Trim().Length > 0 ? x.Ask.Trim() : Tr.Get("vdiary.ask.generic", "一件小事");
			string status = x.Kind == FavorBook.BringItem && x.Aim.Trim().Length > 0 ? AskStatus(x.Phase) + " · " + Tr.Get("vdiary.ask.bring", "要带：{{item}}", ("item", x.Aim.Trim())) : AskStatus(x.Phase);
			rows.Add(new AskRow(x.IsPending, x.IsPending ? x.ExpiredTotalDay - f.Today : int.MaxValue, f.Today - x.CreatedTotalDay, what, status, ""));
		}
		foreach (ItemRequestFavor x in f.ItemRequests.Where(x => Same(x.Asker, f.Npc)))
		{
			string what = Tr.Get("vdiary.ask.item", "想要 {{n}} 个{{item}}", ("n", Math.Max(1, x.Amount)), ("item", x.WantedItemName.Trim()));
			string extra = x.PayGold > 0 ? Tr.Get("vdiary.ask.gold", "报酬 {{gold}}g", ("gold", x.PayGold)) : "";
			rows.Add(new AskRow(x.IsActive, x.IsActive && x.LapsesOn > 0 ? x.LapsesOn - f.Today : int.MaxValue, f.Today - x.AskedOn, what, AskStatus(x.Phase), extra));
		}
		List<DiaryLine> lines = new List<DiaryLine>();
		foreach (AskRow row in rows.Where(r => r.Open).OrderBy(r => r.Left).Concat(rows.Where(r => !r.Open).OrderBy(r => r.Age).Take(MaxClosedAsks)))
		{
			lines.Add(new DiaryLine(row.What));
			List<string> notes = new List<string> { row.Status };
			if (row.Open && row.Left != int.MaxValue)
			{
				notes.Add(row.Left <= 0 ? Tr.Get("vdiary.ask.dueToday", "今天到期") : Tr.Get("vdiary.ask.left", "还剩 {{n}} 天", ("n", row.Left)));
			}
			if (row.Extra.Length > 0)
			{
				notes.Add(row.Extra);
			}
			lines.Add(new DiaryLine(string.Join(" · ", notes), row.Open ? DiaryInk.Warm : DiaryInk.Faint));
		}
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.ask.none", "{{name}}还没托你办过事。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	private static string AskStatus(string phase) => phase.Trim().ToLowerInvariant() switch
	{
		"pending" or "active" => Tr.Get("vdiary.ask.open", "还在等你"),
		"fulfilled" or "completed" => Tr.Get("vdiary.ask.done", "办成了"),
		"expired" => Tr.Get("vdiary.ask.missed", "错过了"),
		_ => Tr.Get("vdiary.ask.cancelled", "不用办了")
	};

	private static List<DiaryLine> Stories(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		IEnumerable<StoryArc> mine = f.Arcs.Where(a => a.Cast.Any(n => Same(n, f.Npc)) || a.Goals.Any(g => Same(g.Npc, f.Npc)))
			.Where(a => a.State is StoryArc.Active or StoryArc.Completable or StoryArc.Resolved or StoryArc.Fizzled)
			.OrderBy(a => a.State switch { StoryArc.Completable => 0, StoryArc.Active => 1, StoryArc.Resolved => 2, _ => 3 })
			.ThenByDescending(a => a.EndedOn ?? int.MaxValue);
		foreach (StoryArc arc in mine)
		{
			lines.Add(new DiaryLine(arc.Title.Trim(), DiaryInk.Heading));
			string state = arc.State switch
			{
				StoryArc.Completable => Tr.Get("vdiary.arc.ready", "可以收尾了"),
				StoryArc.Resolved => Tr.Get("vdiary.arc.ended", "演完了"),
				StoryArc.Fizzled => Tr.Get("vdiary.arc.fizzled", "不了了之"),
				_ => Tr.Get("vdiary.arc.running", "还在演")
			};
			lines.Add(new DiaryLine(arc.Goals.Count > 0 ? state + " · " + Tr.Get("vdiary.arc.goals", "目标 {{done}}/{{total}}", ("done", arc.Goals.Count(g => g.Done)), ("total", arc.Goals.Count)) : state, DiaryInk.Faint));
			if (!string.IsNullOrWhiteSpace(arc.Stage) && arc.State is StoryArc.Active or StoryArc.Completable)
			{
				lines.Add(new DiaryLine(arc.Stage.Trim()));
			}
			foreach (ArcObjective goal in arc.Goals.Where(g => !g.Done && Same(g.Npc, f.Npc) && !string.IsNullOrWhiteSpace(g.Text)))
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.arc.waiting", "等你去办：{{what}}", ("what", goal.Text.Trim())), DiaryInk.Warm));
			}
			List<string> others = arc.Cast.Where(n => !Same(n, f.Npc)).Select(f.Display).ToList();
			if (others.Count > 0)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.arc.with", "一起的还有：{{names}}", ("names", string.Join("、", others))), DiaryInk.Faint));
			}
			if (arc.State is StoryArc.Active or StoryArc.Completable && arc.Endings.Count > 0 && string.IsNullOrWhiteSpace(arc.ChosenEnding))
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.arc.choice", "小剧场页里有个选择等你定"), DiaryInk.Warm));
			}
			string ending = (arc.Outcome ?? arc.Closure ?? "").Trim();
			if (ending.Length > 0)
			{
				lines.Add(new DiaryLine(Tr.Get("vdiary.arc.ending", "结局：{{what}}", ("what", ending))));
			}
		}
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.arc.none", "{{name}}还没卷进哪出小剧场。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	private static List<DiaryLine> Others(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		var edges = f.Edges.Select(e => (Edge: e, Other: Same(e.First, f.Npc) ? e.Second : Same(e.Second, f.Npc) ? e.First : ""))
			.Where(x => x.Other.Length > 0 && !Same(x.Other, f.Npc) && x.Edge.Standing != "unknown")
			.OrderBy(x => x.Edge.LastMetDay > 0 ? f.Today - x.Edge.LastMetDay : int.MaxValue).ThenByDescending(x => x.Edge.Encounters).Take(MaxOthers);
		foreach ((NpcSocialEdge edge, string other) in edges)
		{
			lines.Add(new DiaryLine(f.Display(other) + "：" + SocialWeb.StateWord(edge.Standing), DiaryInk.Heading));
			string met = Tr.Get("vdiary.other.count", "打过 {{n}} 回交道", ("n", Math.Max(0, edge.Encounters)));
			if (edge.LastMetDay > 0)
			{
				int days = Math.Max(0, f.Today - edge.LastMetDay);
				met += " · " + (days == 0 ? Tr.Get("vdiary.other.today", "今天刚碰过面") : Tr.Get("vdiary.other.daysAgo", "{{n}} 天前碰过面", ("n", days)));
			}
			lines.Add(new DiaryLine(met, DiaryInk.Faint));
			bool child = ChildSafety.IsListed(f.Npc) || ChildSafety.IsListed(other);
			foreach (string memory in edge.Shared.Where(m => m.FadesAfterDay <= 0 || m.FadesAfterDay >= f.Today).OrderByDescending(m => m.OnDay).Select(m => m.Text.Trim())
				.Where(t => t.Length > 0 && !(child && IsRomantic(t))).Take(MaxSharedMemories))
			{
				lines.Add(new DiaryLine("· " + memory));
			}
		}
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.other.none", "{{name}}和别的村民之间，还没记下什么。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	private static List<DiaryLine> Changes(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		NpcPersonalityState? s = f.Personality;
		if (s != null)
		{
			AddSection(lines, Tr.Get("vdiary.change.traits", "性子上的变化"), s.Traits.Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value != null && Math.Abs(p.Value.Level) >= MinTraitShift)
				.OrderByDescending(p => Math.Abs(p.Value.Level)).Select(p => new DiaryLine("· " + PersonalityEvolution.TraitWord(p.Key.Trim(), p.Value.Level))));
			AddSection(lines, Tr.Get("vdiary.change.turns", "转折"), s.TurningPoints.Where(m => !m.Private && !string.IsNullOrWhiteSpace(m.Text)).OrderByDescending(m => m.OnDay)
				.Select(m => new DiaryLine("· " + m.Text.Trim())));
		}
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.change.none", "{{name}}还是老样子。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	private static List<DiaryLine> Beliefs(VillagerFacts f)
	{
		List<DiaryLine> lines = new List<DiaryLine>();
		bool childSelf = ChildSafety.IsListed(f.Npc);
		AddSection(lines, Tr.Get("vdiary.belief.rumors", "听过的传闻"), f.Rumors.Where(r => !string.IsNullOrWhiteSpace(r.Subject))
			.Where(r => !(childSelf || r.About.Any(ChildSafety.IsListed)) || !IsRomantic(r.Subject))
			.OrderByDescending(r => r.Heat + r.Harm).ThenByDescending(r => r.BornDay).Take(MaxRumors)
			.Select(r => new DiaryLine(RumorStance(r, f.Npc) + "：" + r.Subject.Trim())));
		AddSection(lines, Tr.Get("vdiary.belief.opinions", "对别人的看法"), f.Opinions.Where(o => !string.IsNullOrWhiteSpace(o.About) && !Same(o.About, f.Npc) && !Same(o.About, RumorRules.Farmer))
			.Select(o => (Opinion: o, Strength: OpinionStrength(o, childSelf || ChildSafety.IsListed(o.About))))
			.Where(x => x.Strength >= MinOpinionStrength).OrderByDescending(x => x.Strength).ThenByDescending(x => x.Opinion.MovedOn).Take(MaxOpinions)
			.Select(x => new DiaryLine("· " + StanceWord(x.Opinion.Attitude, f.Display(x.Opinion.About)))));
		if (lines.Count == 0)
		{
			lines.Add(new DiaryLine(Tr.Get("vdiary.belief.none", "{{name}}没听信什么闲话。", ("name", f.Name)), DiaryInk.Faint));
		}
		return lines;
	}

	public static float OpinionStrength(Opinion o, bool childInvolved)
	{
		float regard = Math.Max(Math.Abs(o.Esteem - 0.5f), Math.Abs(o.Faith - 0.5f)) * 2f;
		float sting = Math.Max(o.Annoyance, Math.Max(o.Doubt, childInvolved ? 0f : o.Envy));
		return Math.Clamp(Math.Max(regard, sting), 0f, 1f);
	}

	private static string RumorStance(Rumor r, string npc) =>
		r.Convinced.Any(n => Same(n, npc)) ? Tr.Get("vdiary.belief.believes", "信了")
		: r.Doubters.Any(n => Same(n, npc)) ? Tr.Get("vdiary.belief.doubts", "不太信")
		: Tr.Get("vdiary.belief.heard", "只是听说");

	private static string StanceWord(string? stance, string name) => (stance ?? "").Trim().ToLowerInvariant() switch
	{
		Stances.Admiring => Tr.Get("vdiary.stance.admiring", "佩服{{name}}", ("name", name)),
		Stances.Protective => Tr.Get("vdiary.stance.protective", "护着{{name}}", ("name", name)),
		Stances.Annoyed => Tr.Get("vdiary.stance.annoyed", "嫌{{name}}烦", ("name", name)),
		Stances.Suspicious => Tr.Get("vdiary.stance.suspicious", "信不过{{name}}", ("name", name)),
		Stances.Cold => Tr.Get("vdiary.stance.cold", "对{{name}}不冷不热", ("name", name)),
		Stances.Badmouthing => Tr.Get("vdiary.stance.badmouthing", "背后说{{name}}的不是", ("name", name)),
		Stances.Betrayed => Tr.Get("vdiary.stance.betrayed", "觉得被{{name}}辜负了", ("name", name)),
		_ => Tr.Get("vdiary.stance.mixed", "说不清对{{name}}是什么感觉", ("name", name))
	};

	private static void AddSection(List<DiaryLine> lines, string title, IEnumerable<DiaryLine> items)
	{
		List<DiaryLine> list = items.ToList();
		if (list.Count > 0)
		{
			lines.Add(new DiaryLine(title, DiaryInk.Heading));
			lines.AddRange(list);
		}
	}

	private static bool IsRomantic(string text) => RomanticWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

	private static bool Same(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
