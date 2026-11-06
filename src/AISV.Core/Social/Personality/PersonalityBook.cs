using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Social.Personality;

/// <summary>
/// 性格演变和关系图（bonds/temperament.json）。
/// 聊天后读出性格信号慢慢改性格；农夫示好时可能引出私下的心动；村民之间的事改他们的关系。只在主线程用。
/// </summary>
public sealed class PersonalityBook
{
	public const string Path = "bonds/temperament.json";

	private static readonly DocumentType<PersonalityData> Doc = new DocumentType<PersonalityData>("性格演变", 1, () => new PersonalityData());

	private readonly Func<DocumentStore?> _store;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, bool> _npcEnabled;

	private readonly Func<string, string> _display;

	private DocumentStore? _normalizedFor;

	public PersonalityBook(Func<DocumentStore?> store, Func<ModConfig> config, Func<string, bool> npcEnabled, Func<string, string> display)
	{
		_store = store;
		_config = config;
		_npcEnabled = npcEnabled;
		_display = display;
	}

	private bool Enabled => _config().TemperamentShifts;

	public NpcPersonalityState? Find(string npc) => Data().Villagers.FirstOrDefault(s => string.Equals(s.Npc, npc, StringComparison.OrdinalIgnoreCase));

	public IReadOnlyList<RelationshipEdgeState> Edges => Data().Ties;

	/// <summary>聊完一轮：读信号、改性格；示好时再看私下的心动。</summary>
	public void AfterExchange(string npc, string playerText, string? proposedAction, string npcLine, NpcReaction reaction, int hearts, bool isAdult, int today)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(npc))
		{
			return;
		}
		PersonalityData d = Data();
		InteractionSignal signal = InteractionSignals.Extract(npc, playerText, proposedAction, npcLine, reaction, hearts, today);
		NpcPersonalityState state = GetOrCreate(d, npc);
		bool changed = PersonalityEvolution.Apply(state, signal);
		RelationshipEdgeState farmer = RelationshipGraph.GetOrCreate(d.Ties, npc, RelationshipGraph.Farmer);
		if (changed | RomanceDiscovery.Apply(state, farmer, signal, _config(), hearts, isAdult, _display(npc)))
		{
			PersonalityEvolution.UpdateSummary(state);
		}
		// 关系边可能新建了：不管性格变没变都写回。
		Dirty();
	}

	/// <summary>村民之间的一件事改两人的关系（两人都开着 AI 时）。</summary>
	public bool AfterMoment(SocialMomentEffect m)
	{
		if (!Enabled || !_npcEnabled(m.Npc1) || !_npcEnabled(m.Npc2))
		{
			return false;
		}
		bool changed = RelationshipGraph.ApplyMoment(Data().Ties, m, _config(), _display);
		if (changed)
		{
			Dirty();
		}
		return changed;
	}

	public void AdvanceDay(int today)
	{
		if (!Enabled)
		{
			return;
		}
		bool changed = false;
		foreach (NpcPersonalityState s in Data().Villagers)
		{
			changed |= PersonalityEvolution.AdvanceDay(s, today);
		}
		if (changed)
		{
			Dirty();
		}
	}

	/// <summary>对话提示词里的性格演变块（包在资料标记里：里面有玩家触发的文字）。</summary>
	public string PromptBlock(string npc)
	{
		if (!Enabled)
		{
			return "";
		}
		string self = SelfBlock(Find(npc), _display(npc));
		string graph = RelationshipGraph.PromptBlock(Data().Ties, npc, _npcEnabled, _display);
		string joined = string.Join("\n", new[] { self, graph }.Where(b => b.Length > 0));
		return joined.Length == 0 ? "" : PromptText.Wrap("PERSONALITY_EVOLUTION", joined, 2400);
	}

	internal static string SelfBlock(NpcPersonalityState? s, string display)
	{
		if (s == null)
		{
			return "";
		}
		bool traits = s.Traits.Values.Any(t => Math.Abs(t.Level) >= 0.08f);
		bool identity = s.Identity.Stage is not ("" or "unexplored") || s.Identity.QuietMoments.Count > 0;
		if (!traits && s.Pressures.Count == 0 && s.TurningPoints.Count == 0 && !identity)
		{
			return "";
		}
		if (string.IsNullOrWhiteSpace(s.SelfPortrait))
		{
			PersonalityEvolution.UpdateSummary(s);
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"{display}在这个存档里慢慢起的变化（只作潜台词）：");
		if (!string.IsNullOrWhiteSpace(s.SelfPortrait))
		{
			sb.AppendLine("- 现在的情绪走向：" + s.SelfPortrait);
		}
		foreach ((string key, PersonalityTraitState t) in s.Traits.Where(p => Math.Abs(p.Value.Level) >= 0.08f).OrderByDescending(p => Math.Abs(p.Value.Level)).Take(2))
		{
			float a = Math.Abs(t.Level);
			string how = a >= 0.55f ? "大幅" : a >= 0.28f ? "明显" : "稍微";
			sb.AppendLine($"- {PersonalityEvolution.TraitName(key)}{how}{(t.Level >= 0 ? "上升" : "下降")}了。");
		}
		foreach (PersonalityModifier m in s.Pressures.OrderByDescending(m => m.Strength).Take(1))
		{
			sb.AppendLine("- 最近的情绪压力：" + m.Text);
		}
		PersonalityMilestone? turn = s.TurningPoints.OrderByDescending(m => m.OnDay).FirstOrDefault();
		if (turn != null)
		{
			sb.AppendLine("- 重要的转折：" + turn.Text);
		}
		if (identity)
		{
			sb.AppendLine($"- 私下的感情探索（只做连续性）：阶段={s.Identity.Stage}，标签={s.Identity.RomanticLabel}。");
			foreach (string note in s.Identity.QuietMoments.Take(2))
			{
				sb.AppendLine("- 私下的心事：" + note);
			}
			sb.AppendLine("- 除非上下文明确说这位村民自己选择说出来，否则不要公开、贴标签或透露这些私下的想法。");
		}
		sb.AppendLine("这些只是慢慢的人物成长：通过语气、分寸、在意什么、怎么选择表现出来。");
		sb.AppendLine("不要提到这段说明，不要引用里面的说法，也别把它们变成聊天话题。");
		return sb.ToString().Trim();
	}

	public void Reset() => _normalizedFor = null;

	private static NpcPersonalityState GetOrCreate(PersonalityData d, string npc)
	{
		NpcPersonalityState? s = d.Villagers.FirstOrDefault(x => string.Equals(x.Npc, npc, StringComparison.OrdinalIgnoreCase));
		if (s == null)
		{
			s = new NpcPersonalityState { Npc = npc };
			d.Villagers.Add(s);
		}
		return s;
	}

	private PersonalityData Data()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return new PersonalityData();
		}
		PersonalityData d = store.Get(Path, Doc);
		if (!ReferenceEquals(_normalizedFor, store))
		{
			_normalizedFor = store;
			d.Villagers = (d.Villagers ?? new List<NpcPersonalityState>()).Where(s => !string.IsNullOrWhiteSpace(s.Npc)).ToList();
			foreach (NpcPersonalityState s in d.Villagers)
			{
				Normalize(s);
			}
			d.Ties ??= new List<RelationshipEdgeState>();
			foreach (RelationshipEdgeState e in d.Ties)
			{
				RelationshipGraph.Normalize(e);
			}
		}
		return d;
	}

	private static void Normalize(NpcPersonalityState s)
	{
		s.Traits = new Dictionary<string, PersonalityTraitState>(s.Traits ?? new Dictionary<string, PersonalityTraitState>(), StringComparer.OrdinalIgnoreCase);
		s.Pressures ??= new List<PersonalityModifier>();
		s.TurningPoints ??= new List<PersonalityMilestone>();
		s.Identity ??= new NpcIdentityProfile();
		s.Identity.ConfidedIn = new HashSet<string>(s.Identity.ConfidedIn ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
		s.Identity.QuietMoments ??= new List<string>();
		s.Identity.Stage = string.IsNullOrWhiteSpace(s.Identity.Stage) ? "unexplored" : s.Identity.Stage;
		s.Identity.RomanticLabel = string.IsNullOrWhiteSpace(s.Identity.RomanticLabel) ? "unknown" : s.Identity.RomanticLabel;
		s.Identity.SexualLabel = string.IsNullOrWhiteSpace(s.Identity.SexualLabel) ? "unknown" : s.Identity.SexualLabel;
		s.Identity.RomanticSignals = Math.Max(0, s.Identity.RomanticSignals);
		s.SelfPortrait ??= "";
		s.InnerConflict ??= "";
	}

	private void Dirty() => _store()?.MarkDirty(Path);
}
