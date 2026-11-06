using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Prompts;

namespace AISV.Core.Scenes;

/// <summary>偶遇（走过来、旁听）的规则：现在能不能演、怎么演、今天会不会来。</summary>
public static class MomentRules
{
	public const int EvaluateEveryTicks = 15;

	public const int MinTicksInLocation = 300;

	public const int MaxTriesPerDay = 3;

	public const int RetryTicks = 1800;

	public const int HearingTiles = 14;

	public const int WalkUpTtlTicks = 7200;

	public const int WalkUpPrepareTicks = 2400;

	public const int GenerationSeconds = 45;

	public const int GenerationTicks = 2700;

	public const int ReadyTimeoutTicks = 3600;

	public const int StaleTicks = 36000;

	public const int AnnounceEmote = 16;

	public const int MaxOverheardLines = 4;

	public const int MaxMonologueLines = 2;

	public const int MaxPremiseChars = 400;

	public static MomentBlock Check(MomentReadiness r)
	{
		if (r.WorldBusy)
		{
			return MomentBlock.WorldBusy;
		}
		if (r.PlayerBusy)
		{
			return MomentBlock.PlayerBusy;
		}
		if (r.Time < r.WindowMin || r.Time > r.WindowMax)
		{
			return MomentBlock.OutsideWindow;
		}
		if (r.Tick - r.EnteredTick < MinTicksInLocation)
		{
			return MomentBlock.JustArrived;
		}
		if (r.Tries >= MaxTriesPerDay)
		{
			return MomentBlock.OutOfTries;
		}
		if (r.LastTryTick >= 0 && r.Tick - r.LastTryTick < RetryTicks)
		{
			return MomentBlock.TooSoon;
		}
		return r.OtherRunning ? MomentBlock.SomethingElseRunning : MomentBlock.None;
	}

	public static string BlockName(MomentBlock block) => block switch
	{
		MomentBlock.WorldBusy => "游戏正忙",
		MomentBlock.PlayerBusy => "农夫在忙",
		MomentBlock.OutsideWindow => "不在时间窗口里",
		MomentBlock.JustArrived => "刚进这张地图",
		MomentBlock.OutOfTries => "今天试满了",
		MomentBlock.TooSoon => "离上次试太近",
		MomentBlock.SomethingElseRunning => "别的偶遇或走过来正在进行",
		_ => "可以"
	};

	/// <summary>
	/// 现在怎么演。<paramref name="aiAvailable"/> 为 false 时只能用写好的台词。
	/// </summary>
	public static MomentPlan Decide(SceneDelivery delivery, string style, IReadOnlyList<string> participants, IReadOnlyCollection<MomentPresence> presence, bool aiAvailable, IReadOnlyList<SceneLine> lines)
	{
		List<string> cast = Clean(participants);
		string lead = cast.FirstOrDefault() ?? "";
		if (delivery == SceneDelivery.Cinematic)
		{
			return Skip(lead, "过场不算偶遇");
		}
		if (cast.Count == 0)
		{
			return Skip(lead, "没有参与者");
		}
		Dictionary<string, MomentPresence> byName = new Dictionary<string, MomentPresence>(StringComparer.OrdinalIgnoreCase);
		foreach (MomentPresence p in presence.Where(p => !string.IsNullOrWhiteSpace(p.Npc)))
		{
			byName.TryAdd(p.Npc.Trim(), p);
		}
		if (!byName.TryGetValue(lead, out MomentPresence? leadHere) || !leadHere.Nearby)
		{
			return Wait(lead, lead + " 不在附近");
		}
		style = SceneStyle.Normalize(style);
		if (!aiAvailable)
		{
			List<string> withLines = cast.Where(n => HasLine(lines, n)).ToList();
			if (withLines.Count == 0)
			{
				return Skip(lead, lead + " 没有写好的台词");
			}
			if (!OnlyLinesBy(lines, withLines))
			{
				return Skip(lead, "有台词的人不在场景里");
			}
			if (withLines.Count == 1 && Same(withLines[0], lead))
			{
				return Alone(delivery, style, lead, leadHere.CanApproach, "只有 " + lead + " 有台词");
			}
			if (!withLines.All(n => Nearby(byName, n)))
			{
				return Wait(lead, "等其他有台词的人过来");
			}
			List<string> speakers = withLines.Contains(lead, StringComparer.OrdinalIgnoreCase) ? withLines : withLines.Prepend(lead).ToList();
			return new MomentPlan(MomentPlanKind.Overheard, lead, speakers, "有台词的人都在附近");
		}
		List<string> near = cast.Where(n => Nearby(byName, n)).ToList();
		if (delivery == SceneDelivery.Overheard && near.Count >= 2)
		{
			return new MomentPlan(MomentPlanKind.Overheard, lead, near, "参与者都在附近");
		}
		return Alone(delivery, style, lead, leadHere.CanApproach, delivery == SceneDelivery.Overheard ? "只有领头的在附近" : lead + " 在附近");
	}

	private static MomentPlan Alone(SceneDelivery delivery, string style, string lead, bool canApproach, string reason)
	{
		bool styleAllows = delivery == SceneDelivery.Overheard ? style == SceneStyle.Natural : style != SceneStyle.AmbientOnly;
		if (styleAllows && canApproach)
		{
			return new MomentPlan(MomentPlanKind.WalkUp, lead, new[] { lead }, reason);
		}
		string why = styleAllows ? lead + " 走不过来" : "这种风格不走过来";
		return new MomentPlan(MomentPlanKind.Overheard, lead, new[] { lead }, $"{reason}；{why}；自言自语");
	}

	/// <summary>
	/// 今天要看谁的日程：AI 能用时只看领头的；不能用时看所有有台词的人（领头的在前）。返回空表示演不出来。
	/// </summary>
	public static (IReadOnlyList<string> Speakers, string Reason) ForecastScope(IReadOnlyList<string> participants, IReadOnlyList<SceneLine> lines, bool aiAvailable)
	{
		List<string> cast = Clean(participants);
		if (cast.Count == 0)
		{
			return (Array.Empty<string>(), "没有参与者");
		}
		string lead = cast[0];
		if (aiAvailable)
		{
			return (new[] { lead }, "AI 写台词时只要领头的在");
		}
		List<string> withLines = cast.Where(n => HasLine(lines, n)).ToList();
		if (withLines.Count == 0)
		{
			return (Array.Empty<string>(), "没有 AI 时谁都没有写好的台词");
		}
		if (!OnlyLinesBy(lines, withLines))
		{
			return (Array.Empty<string>(), "没有 AI 时有些写好的台词不属于场景里的人");
		}
		List<string> others = withLines.Where(n => !Same(n, lead)).ToList();
		return others.Count == 0
			? (new[] { lead }, "没有 AI 时只有 " + lead + " 有写好的台词")
			: (others.Prepend(lead).ToList(), "没有 AI 时有写好台词的人都要在");
	}

	/// <summary>
	/// 按当天日程看这个人会不会在时间窗口里出现在 <paramref name="location"/>：早上 6 点在 <paramref name="start"/>，之后按各站到达时间换地方。
	/// </summary>
	public static bool ExpectedAt(string? start, IReadOnlyList<ScheduleStop>? stops, string location, int min, int max)
	{
		if (string.IsNullOrWhiteSpace(location))
		{
			return false;
		}
		if (min > max)
		{
			(min, max) = (max, min);
		}
		List<ScheduleStop> list = (stops ?? Array.Empty<ScheduleStop>()).Where(s => s != null).OrderBy(s => s.Time).ToList();
		if (list.Count == 0)
		{
			return SamePlace(start, location) && Overlaps(600, 2600, true, min, max);
		}
		if (SamePlace(start, location) && Overlaps(600, list[0].Time, false, min, max))
		{
			return true;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (!SamePlace(list[i].Location, location))
			{
				continue;
			}
			bool last = i == list.Count - 1;
			int end = last ? Math.Max(2600, list[i].Time) : list[i + 1].Time;
			if (Overlaps(list[i].Time, end, last, min, max))
			{
				return true;
			}
		}
		return false;
	}

	private static bool Overlaps(int start, int end, bool endInclusive, int min, int max)
	{
		if (end < start || !endInclusive && end == start)
		{
			return false;
		}
		return start <= max && (endInclusive ? end >= min : end > min);
	}

	/// <summary>
	/// 走过来时给开场白的说明：为什么过来、惦记的事、牵涉到谁、怎么说。
	/// </summary>
	public static string WalkUpFocus(string premise, IReadOnlyList<string> others)
	{
		string topic = OneLine(PromptText.Sanitize(premise, MaxPremiseChars)).Replace('"', '\'');
		List<string> names = others.Select(n => OneLine(PromptText.Sanitize(n, 60))).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		StringBuilder s = new StringBuilder("【你走过来找农夫的缘由——开口就照这个来】\n");
		s.Append("刚才你远远看见农夫，就自己凑了过来；名字已经喊过了，这句不用再招呼，直接说事。");
		if (topic.Length > 0)
		{
			s.Append("让你想过来说两句的是这件事（当素材用，别照念）：\"").Append(topic).Append("\"。");
		}
		if (names.Count > 0)
		{
			s.Append("说到").Append(string.Join("、", names)).Append("时带一句就行。");
		}
		s.Append("\n怎么说：当成随口闲聊，用你平时的腔调，挑具体的讲、讲短点；末尾不用总结出什么道理，也别故弄玄虚，顺口问农夫一句或者邀一下就很自然。");
		return s.ToString();
	}

	public static bool OnlyLinesBy(IReadOnlyList<SceneLine> lines, IReadOnlyCollection<string> speakers)
	{
		List<string> names = speakers.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
		List<SceneLine> said = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
		return names.Count > 0 && said.Count > 0 && said.All(l => names.Contains(l.Speaker.Trim(), StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>只有领头的有台词时，几句用分页连起来当走过来要说的话。</summary>
	public static string? LeadDialogue(IReadOnlyList<SceneLine> lines, string lead)
	{
		if (string.IsNullOrWhiteSpace(lead) || !OnlyLinesBy(lines, new[] { lead }))
		{
			return null;
		}
		return string.Join("#$b#", lines.Where(l => Same(l.Speaker, lead) && !string.IsNullOrWhiteSpace(l.Text)).Select(l => l.Text.Trim()));
	}

	public static List<SceneLine> OnlyBy(IReadOnlyList<SceneLine>? lines, IReadOnlyList<string> speakers) =>
		(lines ?? Array.Empty<SceneLine>()).Where(l => !string.IsNullOrWhiteSpace(l.Text) && speakers.Contains(l.Speaker.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();

	private static bool HasLine(IReadOnlyList<SceneLine> lines, string speaker) => lines.Any(l => !string.IsNullOrWhiteSpace(l.Text) && Same(l.Speaker.Trim(), speaker));

	private static bool Nearby(IReadOnlyDictionary<string, MomentPresence> byName, string name) => byName.TryGetValue(name, out MomentPresence? p) && p.Nearby;

	private static List<string> Clean(IReadOnlyList<string> names) => names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

	private static string OneLine(string text) => text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

	private static bool SamePlace(string? a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static MomentPlan Wait(string lead, string reason) => new MomentPlan(MomentPlanKind.Wait, lead, Array.Empty<string>(), reason);

	private static MomentPlan Skip(string lead, string reason) => new MomentPlan(MomentPlanKind.Skip, lead, Array.Empty<string>(), reason);
}
