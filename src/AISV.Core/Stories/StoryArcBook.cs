using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Dialogue;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Stories;

/// <summary>判断故事线能不能给 AI 用：涉及的村民都开了 AI，来源 mod 也都还在。</summary>
public delegate bool ArcUsable(StoryArc arc);

/// <summary>小剧场（town/storylines.json）：检测结果合并、对话里推进目标、选结局、写结局、结局后的台词。</summary>
public sealed class StoryArcBook
{
	public const string Path = "town/storylines.json";

	public const int MinObjectives = 3;

	public const int MaxObjectives = 5;

	private static readonly DocumentType<List<StoryArc>> Doc = new DocumentType<List<StoryArc>>("小剧场", 1, () => new List<StoryArc>());

	private readonly Func<DocumentStore?> _store;

	private readonly object _sync = new object();

	public StoryArcBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>快照（检测在后台线程读）。</summary>
	public IReadOnlyList<StoryArc> All
	{
		get
		{
			List<StoryArc>? list = List();
			if (list == null)
			{
				return new List<StoryArc>();
			}
			lock (_sync)
			{
				return list.ToList();
			}
		}
	}

	public StoryArc? Get(string id) => All.FirstOrDefault(a => Same(a.Id, id));

	public static bool ShouldAnalyze(int dayOfMonth) => (dayOfMonth - 1) % 7 == 2;

	public int ActiveCount(ArcUsable usable) => All.Count(a => a.State == StoryArc.Active && usable(a));

	/// <summary>进存档：名字换成内部名、补来源、去掉要带东西的目标、补结局选项。返回有没有改动。</summary>
	public bool Normalize(Func<string, string> toInternal, Action<string>? log = null)
	{
		List<StoryArc>? list = List();
		if (list == null)
		{
			return false;
		}
		bool changed = false;
		foreach (StoryArc arc in list)
		{
			for (int i = 0; i < arc.Cast.Count; i++)
			{
				string real = toInternal(arc.Cast[i]);
				if (real != arc.Cast[i])
				{
					arc.Cast[i] = real;
					changed = true;
				}
			}
			foreach (ArcObjective o in arc.Goals.Where(o => !string.IsNullOrEmpty(o.Npc)))
			{
				string real = toInternal(o.Npc);
				if (real != o.Npc)
				{
					o.Npc = real;
					changed = true;
				}
			}
			if (arc.Sources.Count == 0)
			{
				int slash = arc.Id.IndexOf('/');
				if (slash > 0 && arc.Id[..slash].Contains('.'))
				{
					arc.MergeSources(new[] { arc.Id[..slash].Trim() });
					changed = true;
				}
			}
			if (DropItemObjectives(arc))
			{
				log?.Invoke($"小剧场「{arc.Title}」：去掉了要带东西的目标（现在 {arc.State}）。");
				changed = true;
			}
			if (arc.State is StoryArc.Active or StoryArc.Completable)
			{
				int before = arc.Endings?.Count ?? 0;
				string? selected = arc.ChosenEnding;
				EnsureChoices(arc);
				changed |= before != arc.Endings!.Count || !Same(selected, arc.ChosenEnding);
			}
		}
		if (changed)
		{
			Dirty();
		}
		return changed;
	}

	/// <summary>进行中的故事线里没完成的"带东西"目标去掉；去完一个不剩就不了了之，剩下的都完成了就可以收尾。</summary>
	public static bool DropItemObjectives(StoryArc arc)
	{
		if (arc.State != StoryArc.Active || arc.Goals == null || arc.Goals.RemoveAll(o => IsItem(o) && !o.Done) == 0)
		{
			return false;
		}
		if (arc.Goals.Count == 0)
		{
			arc.State = StoryArc.Fizzled;
		}
		else if (arc.AllObjectivesDone)
		{
			arc.State = StoryArc.Completable;
			EnsureChoices(arc);
		}
		return true;
	}

	/// <summary>还没收尾的故事线直接不了了之（返回改了几出）。</summary>
	public int Abandon(Func<StoryArc, bool> which)
	{
		List<StoryArc> list = All.Where(a => a.State is StoryArc.Active or StoryArc.Completable && which(a)).ToList();
		list.ForEach(a => a.State = StoryArc.Fizzled);
		if (list.Count > 0)
		{
			Dirty();
		}
		return list.Count;
	}

	/// <summary>几季没一点进展的进行中故事线标成不了了之。</summary>
	public List<StoryArc> Fizzle(GameDate today, int thresholdSeasons, ArcUsable usable)
	{
		int now = today.Year * 4 + GameDate.SeasonIndex(today.Season);
		List<StoryArc> fizzled = All.Where(a => a.State == StoryArc.Active && usable(a) && a.DoneCount == 0
			&& now - (a.BornYear * 4 + GameDate.SeasonIndex(a.BornSeason)) >= thresholdSeasons).ToList();
		fizzled.ForEach(a => a.State = StoryArc.Fizzled);
		if (fizzled.Count > 0)
		{
			Dirty();
		}
		return fizzled;
	}

	/// <summary>把检测结果并进来：同 id 的进行中故事线更新阶段，新的一次最多建一条。返回新建的。</summary>
	public StoryArc? Merge(IReadOnlyList<StoryArc> detected, GameDate today, int maxActive, IReadOnlyCollection<string> sources, ArcUsable usable, Action<string>? log = null)
	{
		List<StoryArc>? list = List();
		if (list == null)
		{
			return null;
		}
		List<StoryArc> active = list.Where(a => a.State == StoryArc.Active && usable(a)).ToList();
		StoryArc? created = null;
		foreach (StoryArc arc in detected)
		{
			if (!usable(arc))
			{
				log?.Invoke($"小剧场「{arc.Title}」里有没开 AI 的村民，不要。");
				continue;
			}
			// AI 有时换个 id 重提同一出：标题一样也算原来那出。
			StoryArc? existing = active.FirstOrDefault(a => Same(a.Id, arc.Id)) ?? active.FirstOrDefault(a => Same(a.Title?.Trim(), arc.Title?.Trim()));
			if (existing != null)
			{
				existing.Stage = arc.Stage;
				existing.MergeSources(sources);
			}
			else if (created == null && active.Count < maxActive)
			{
				arc.BornSeason = today.Season;
				arc.BornYear = today.Year;
				arc.State = StoryArc.Active;
				arc.MergeSources(sources);
				EnsureChoices(arc);
				lock (_sync)
				{
					list.Add(arc);
				}
				created = arc;
			}
			else
			{
				log?.Invoke($"小剧场「{arc.Title}」没建：{(created != null ? "一次只建一条" : "进行中的已经够多")}。");
			}
		}
		Dirty();
		return created;
	}

	/// <summary>对话里玩家完成了某个目标。返回所在的故事线（找不到时 null）；<paramref name="ready"/> 表示因此可以收尾了。</summary>
	public StoryArc? CompleteObjective(string objectiveId, string? approach, string by, GameDate today, ArcUsable usable, out ArcObjective? objective, out bool ready)
	{
		objective = null;
		ready = false;
		foreach (StoryArc arc in All.Where(a => a.State == StoryArc.Active && usable(a)))
		{
			ArcObjective? o = arc.Goals.FirstOrDefault(x => Same(x.Id, objectiveId));
			if (o == null)
			{
				continue;
			}
			o.Done = true;
			o.DoneOn = $"{today.Season} {today.Day}, Year {today.Year}";
			o.DoneHow = approach;
			o.DoneBy = by;
			objective = o;
			if (arc.AllObjectivesDone)
			{
				arc.State = StoryArc.Completable;
				EnsureChoices(arc);
				ready = true;
			}
			Dirty();
			return arc;
		}
		return null;
	}

	/// <summary>别处写好的一段（只用对话目标）：编号重了、名额满了、没有目标时不加。</summary>
	public bool AddExternal(StoryArc arc, int maxActive, ArcUsable usable)
	{
		List<StoryArc>? list = List();
		if (list == null || arc.Goals.Count == 0 || arc.Cast.Count == 0 || list.Any(a => Same(a.Id, arc.Id)) || ActiveCount(usable) >= Math.Clamp(maxActive, 1, 5))
		{
			return false;
		}
		EnsureChoices(arc);
		lock (_sync)
		{
			list.Add(arc);
		}
		Dirty();
		return true;
	}

	public bool SelectChoice(string arcId, string choiceId)
	{
		StoryArc? arc = Get(arcId);
		if (arc == null || arc.State != StoryArc.Completable)
		{
			return false;
		}
		EnsureChoices(arc);
		ArcChoiceOption? c = arc.Endings.FirstOrDefault(o => Same(o.Id, choiceId));
		if (c == null)
		{
			return false;
		}
		arc.ChosenEnding = c.Id;
		Dirty();
		return true;
	}

	public bool Delete(string arcId)
	{
		List<StoryArc>? list = List();
		if (list == null)
		{
			return false;
		}
		int removed;
		lock (_sync)
		{
			removed = list.RemoveAll(a => Same(a.Id, arcId));
		}
		if (removed > 0)
		{
			Dirty();
		}
		return removed > 0;
	}

	/// <summary>写结局：台词、总结、后果、结局后的台词、标签、舞台，状态改成已结束。</summary>
	public void Resolve(StoryArc arc, ArcConclusion c, ConclusionStage stage, string? fallbackLocation, GameDate today)
	{
		arc.FinalScene = c.Lines;
		arc.Closure = c.Summary;
		arc.Outcome = Consequence(arc);
		arc.Afterwords = c.PostLines;
		arc.Tags = c.Tags;
		arc.FinalStage = stage;
		arc.StagePlace = string.IsNullOrWhiteSpace(stage.Venue) ? fallbackLocation : stage.Venue;
		arc.EndedDay = today.Day;
		arc.EndedOn = today.TotalDays;
		arc.AfterwordsSpoken = new HashSet<string>();
		arc.State = StoryArc.Resolved;
		Dirty();
	}

	/// <summary>结局后几天内第一次见面时村民说的那句（每人一次）。只读，取用后要调 <see cref="ConsumePostResolutionLine"/>。</summary>
	public (string ArcId, string Line)? PeekPostResolutionLine(string npc, int today, int expiryDays, ArcUsable usable)
	{
		foreach (StoryArc arc in All.Where(a => a.State == StoryArc.Resolved && usable(a)))
		{
			bool used;
			lock (_sync)
			{
				used = arc.AfterwordsSpoken.Any(n => Same(n, npc));
			}
			if (arc.Afterwords != null && arc.EndedOn is int day
				&& TryGet(arc.Afterwords, npc, out string? line) && !used && today - day <= expiryDays && line != null)
			{
				return (arc.Id, line);
			}
		}
		return null;
	}

	/// <summary>记下这位村民已经说过结局后的话（主线程调）。</summary>
	public void ConsumePostResolutionLine(string arcId, string npc)
	{
		StoryArc? arc = Get(arcId);
		if (arc == null)
		{
			return;
		}
		lock (_sync)
		{
			arc.AfterwordsSpoken.Add(npc);
		}
		Dirty();
	}

	public string? TakePostResolutionLine(string npc, int today, int expiryDays, ArcUsable usable)
	{
		(string ArcId, string Line)? hit = PeekPostResolutionLine(npc, today, expiryDays, usable);
		if (hit == null)
		{
			return null;
		}
		ConsumePostResolutionLine(hit.Value.ArcId, npc);
		return hit.Value.Line;
	}

	/// <summary>和这位村民说话时告诉 AI 的小剧场：他涉及的进行中故事线和他身上还没完成的目标。</summary>
	public string PromptBlock(string npc, Func<string, string> display, ArcUsable usable)
	{
		List<StoryArc> mine = All.Where(a => a.State == StoryArc.Active && usable(a) && a.Involves(npc)).ToList();
		if (mine.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("【镇上正在上演、你也在其中的事】");
		bool judge = false;
		foreach (StoryArc arc in mine)
		{
			List<string> others = arc.Cast.Where(n => !Same(n, npc)).Select(display).ToList();
			sb.AppendLine($"「{arc.Title}」{(others.Count > 0 ? "，同在其中的还有" + string.Join("、", others) : "")}。局面：{arc.Stage}");
			foreach (ArcObjective o in arc.Goals.Where(o => o.Done))
			{
				sb.AppendLine($"  ✓ 已办成 [id:{o.Id}]：{o.Text}（经手：{(string.IsNullOrWhiteSpace(o.DoneBy) ? "农夫" : o.DoneBy)}）");
			}
			foreach (ArcObjective o in arc.Goals.Where(o => !o.Done && Same(o.Npc, npc)))
			{
				sb.AppendLine($"  ☐ 等农夫来找你办 [id:{o.Id}]：{o.Text}。要看到：{o.DoneWhen}");
				judge = true;
			}
		}
		sb.AppendLine(judge
			? "带 ☐ 的事由你把关：只有农夫这一句确实做到了\"要看到\"的那一步，才在 step_done 里填它方括号中 id: 后面的那串字，并在 step_how 用一句话说他靠什么做到的；没做到、只是沾了边，两项都留 null。"
			: "眼下没有等着你点头的事；聊到相关的话题，按你的立场自然带上一两句。");
		return sb.ToString();
	}

	// ---- 结局选项 ----

	public static void EnsureChoices(StoryArc arc)
	{
		arc.Endings ??= new List<ArcChoiceOption>();
		if (arc.Endings.Count == 0)
		{
			arc.Endings.AddRange(DefaultChoices());
		}
		if (string.IsNullOrWhiteSpace(arc.ChosenEnding) || arc.Endings.All(c => !Same(c.Id, arc.ChosenEnding)))
		{
			arc.ChosenEnding = arc.Endings[0].Id;
		}
	}

	public const string ChoiceMend = "mend";

	public const string ChoiceOpenUp = "open_up";

	public const string ChoiceAct = "act";

	/// <summary>AI 没给选项时用的三种收尾。</summary>
	public static ArcChoiceOption[] DefaultChoices() => new[]
	{
		new ArcChoiceOption { Id = ChoiceMend, Label = Tr.Get("arcs.choice.mend", "和好"), Text = Tr.Get("arcs.choice.mendDesc", "先顾人心，把这段情分补回来。"), Outcome = Tr.Get("arcs.choice.mendResult", "你选了先把人拉回来：彼此又肯坐到一张桌上，只是那些实际的麻烦还摆在那儿，得慢慢还。") },
		new ArcChoiceOption { Id = ChoiceOpenUp, Label = Tr.Get("arcs.choice.openUp", "摊牌"), Text = Tr.Get("arcs.choice.openUpDesc", "不绕弯子，是谁的事谁当面认。"), Outcome = Tr.Get("arcs.choice.openUpResult", "你让大家把话挑明：来龙去脉都清楚了，听的人未必好受，这股疙瘩也要过阵子才消。") },
		new ArcChoiceOption { Id = ChoiceAct, Label = Tr.Get("arcs.choice.act", "动手"), Text = Tr.Get("arcs.choice.actDesc", "少说多做，拿一件看得见的事来解决。"), Outcome = Tr.Get("arcs.choice.actResult", "你带着大家先把事办了：局面确实好转，心里的结却没人去碰，情分得靠日后再攒。") }
	};

	public static ArcChoiceOption? Selected(StoryArc arc)
	{
		EnsureChoices(arc);
		return arc.Endings.FirstOrDefault(c => Same(c.Id, arc.ChosenEnding));
	}

	public static string Consequence(StoryArc arc) => Selected(arc)?.Outcome ?? "";

	// ---- 目标 ----

	public static bool IsItem(ArcObjective o) => string.Equals(o.Kind, "bring_item", StringComparison.OrdinalIgnoreCase);

	public static string NormalizeObjectiveType(string? type) => (type ?? "").Trim().ToLowerInvariant() switch
	{
		"talk_to" or "talk" or "conversation" or "converse" or "hablar" or "hablar_con" or "conversar" or "conversar_com" or "falar" or "falar_com"
			or "parler" or "parler_a" or "parlare" or "parlare_con" or "sprechen" or "sprechen_mit" => "talk_to",
		"bring_item" or "bring" or "give_item" or "deliver_item" or "traer_item" or "entregar_item" or "dar_item" or "trazer_item"
			or "entregar_objeto" or "donner_objet" or "apporter_objet" or "portare_oggetto" or "gegenstand_bringen" => "bring_item",
		_ => type ?? ""
	};

	/// <summary>检查 AI 给的目标：先去掉带东西的，再看够不够 3 个、对象是不是涉及的村民、有没有指向玩家、是不是都是说话。返回为什么不要（null = 可以）。</summary>
	public static string? Validate(List<ArcObjective> objectives, IEnumerable<string> involved, string playerNames, out int droppedItems)
	{
		droppedItems = objectives.RemoveAll(IsItem);
		if (objectives.Count < MinObjectives)
		{
			return droppedItems > 0 ? $"去掉 {droppedItems} 个带东西的目标后只剩 {objectives.Count} 个" : $"只有 {objectives.Count} 个目标";
		}
		HashSet<string> who = new HashSet<string>(involved, StringComparer.OrdinalIgnoreCase);
		HashSet<string> players = new HashSet<string>(playerNames.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase);
		ArcObjective? outsider = objectives.FirstOrDefault(o => !string.IsNullOrEmpty(o.Npc) && !who.Contains(o.Npc));
		if (outsider != null)
		{
			return $"目标对象 {outsider.Npc} 不在牵扯的人里";
		}
		ArcObjective? player = objectives.FirstOrDefault(o => players.Contains(o.Npc));
		if (player != null)
		{
			return $"目标对象是玩家 {player.Npc}";
		}
		ArcObjective? other = objectives.FirstOrDefault(o => !string.Equals(o.Kind, "talk_to", StringComparison.OrdinalIgnoreCase));
		return other != null ? $"目标类型 {other.Kind} 不是说话" : null;
	}

	private static bool TryGet(Dictionary<string, string> map, string key, out string? value)
	{
		foreach (KeyValuePair<string, string> kv in map)
		{
			if (Same(kv.Key, key))
			{
				value = kv.Value;
				return true;
			}
		}
		value = null;
		return false;
	}

	private List<StoryArc>? List() => _store()?.Get(Path, Doc);

	private void Dirty() => _store()?.MarkDirty(Path);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
