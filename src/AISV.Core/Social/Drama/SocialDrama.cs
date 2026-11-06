using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;

namespace AISV.Core.Social.Drama;

/// <summary>
/// 社交剧情（bonds/drama.json）：
/// 八卦和农夫的话改变村民之间的看法、生出传闻；每天看法慢慢平复、传闻传开；对话时把相关的几条写进提示词。只在主线程用。
/// </summary>
public sealed class SocialDrama
{
	public const string Path = "bonds/drama.json";

	public const int MaxOpinions = 200;
	public const int MaxBeats = 60;
	public const int MaxInfluences = 60;
	public const int MaxRumors = 80;
	public const int MaxAdult = 40;

	private static readonly DocumentType<DramaState> Doc = new DocumentType<DramaState>("社交剧情", 1, () => new DramaState());

	private readonly Func<DocumentStore?> _store;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, bool> _npcEnabled;

	private readonly Func<string, bool> _modEnabled;

	private readonly Func<string, string> _display;

	private readonly ILog _log;

	private DocumentStore? _normalizedFor;

	public SocialDrama(Func<DocumentStore?> store, Func<ModConfig> config, Func<string, bool> npcEnabled, Func<string, bool> modEnabled, Func<string, string> display, ILog log)
	{
		_store = store;
		_config = config;
		_npcEnabled = npcEnabled;
		_modEnabled = modEnabled;
		_display = display;
		_log = log;
	}

	private bool Enabled => _config().DramaOn;

	private string Intensity => DramaRules.Intensity(_config().DramaHeat);

	private AdultRules.Gates Gates
	{
		get
		{
			ModConfig c = _config();
			return new AdultRules.Gates(c.AdultDramaOn(), c.InfidelityOn(), c.SafeguardGameCouples, c.DramaHeat);
		}
	}

	public DramaState Data
	{
		get
		{
			DocumentStore? store = _store();
			if (store == null)
			{
				return new DramaState();
			}
			DramaState d = store.Get(Path, Doc);
			if (!ReferenceEquals(_normalizedFor, store))
			{
				_normalizedFor = store;
				Normalize(d);
			}
			return d;
		}
	}

	public void Reset() => _normalizedFor = null;

	// ---------------- 天亮 ----------------

	/// <summary>看法慢慢平复、传闻传开，再按上限修剪各清单。</summary>
	public void OnDayStarted(int today)
	{
		if (!Enabled)
		{
			return;
		}
		DramaState d = Data;
		bool changed = Decay(d);
		foreach (Rumor r in d.Rumors)
		{
			changed |= RumorRules.SpreadForDay(r, today, Intensity, BuiltInPersonas.VanillaNames);
		}
		int beats = d.Beats.Count, rumors = d.Rumors.Count, adult = d.Adult.Count;
		d.Beats = d.Beats.Where(b => b.OnDay >= today - 21).Take(MaxBeats).ToList();
		d.Rumors = d.Rumors.Where(r => r.DiesDay >= today).OrderByDescending(r => r.Heat + r.Harm).ThenByDescending(r => r.BornDay).Take(MaxRumors).ToList();
		d.Adult = d.Adult.Where(s => s.DiesDay >= today).OrderByDescending(s => s.Force + s.Pull).ThenByDescending(s => s.MovedDay).Take(MaxAdult).ToList();
		if (changed || beats != d.Beats.Count || rumors != d.Rumors.Count || adult != d.Adult.Count)
		{
			Dirty();
		}
	}

	// ---------------- 新八卦 ----------------

	/// <summary>晚上整理出一条新八卦：建一条传闻，听到的人对被说到的人改看法。返回改了几条看法。</summary>
	public int ApplyGossip(GossipEntry e)
	{
		if (!Enabled || !GossipUsable(e))
		{
			return 0;
		}
		int day = e.BornOn > 0 ? e.BornOn : new GameDate(e.Day, e.Season, e.Year).TotalDays;
		return ApplyGossip(e, day);
	}

	internal int ApplyGossip(GossipEntry e, int day)
	{
		DramaState d = Data;
		int sentiment = DramaText.Sentiment(e.Fact);
		List<string> targets = DramaText.Mentioned(e.Fact, _display).ToList();
		if (targets.Count == 0 && !string.IsNullOrWhiteSpace(e.StartedBy))
		{
			targets.Add(e.StartedBy);
		}
		targets = targets.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
		if (targets.Count == 0)
		{
			return 0;
		}
		bool rumorChanged = Upsert(d, RumorRules.FromGossip(e.Fact, e.Season, e.Day, e.Year, e.StartedBy, e.ToldBy, e.KnownTo, e.FromMods, sentiment, targets, day, Intensity));
		ModConfig c = _config();
		if (!c.RumorsMoveOpinions || sentiment == 0)
		{
			if (rumorChanged)
			{
				Dirty();
			}
			return 0;
		}
		IEnumerable<string> listeners = e.KnownTo == null || e.KnownTo.Count == 0 ? targets : e.KnownTo;
		int changed = 0;
		foreach (string holder in listeners.Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
		{
			if (string.IsNullOrWhiteSpace(holder))
			{
				continue;
			}
			foreach (string target in targets.Where(t => !DramaRules.Same(t, holder)))
			{
				Opinion o = GetOrCreate(d, holder, target);
				string before = OpinionRules.Snapshot(o);
				o.Origins = DramaRules.Merge(o.Origins, e.FromMods);
				ApplyGossipTo(o, e, sentiment, day);
				if (OpinionRules.Snapshot(o) == before)
				{
					continue;
				}
				changed++;
				AdultRules.Apply(d.Adult, o, DramaSources.Gossip, day, Gates, e.FromMods, Name);
				AddGossipBeat(d, o, e, sentiment, day);
			}
		}
		if (changed > 0 || rumorChanged)
		{
			Dirty();
			_log.Debug($"社交剧情：这条八卦改了 {changed} 条看法：{DramaRules.Trim(e.Fact, 80)}");
		}
		return changed;
	}

	// ---------------- 农夫的话 ----------------

	/// <summary>农夫在 <paramref name="speaker"/> 面前说起别人：记下影响、可能生出传闻、改说话人的看法。返回改了几条看法。</summary>
	public int RecordInfluence(string speaker, string playerText, int hearts, NpcReaction? reaction, int today)
	{
		ModConfig c = _config();
		if (!Enabled || !c.FarmerCanSway || string.IsNullOrWhiteSpace(playerText) || !_npcEnabled(speaker))
		{
			return 0;
		}
		int sentiment = DramaText.Sentiment(playerText);
		if (sentiment == 0)
		{
			return 0;
		}
		List<string> targets = DramaText.Mentioned(playerText, _display).Where(n => !DramaRules.Same(n, speaker)).Where(_npcEnabled).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
		if (targets.Count == 0)
		{
			return 0;
		}
		DramaState d = Data;
		string action = DramaText.Action(playerText, sentiment);
		float acceptance = InfluenceRules.Acceptance(hearts, reaction?.FriendshipPoints, reaction?.Emotion);
		string result = InfluenceRules.Result(acceptance);
		int changed = 0;
		foreach (string target in targets)
		{
			Influence a = new Influence
			{
				Id = Guid.NewGuid().ToString("N"),
				Move = action,
				Talker = speaker,
				Aim = target,
				Subject = DramaRules.Trim(playerText),
				Slant = sentiment,
				Uptake = acceptance,
				Upshot = result,
				OnDay = today
			};
			d.Influences.Insert(0, a);
			if (result == InfluenceRules.Rejected)
			{
				continue;
			}
			if (a.Slant < 0)
			{
				Upsert(d, RumorRules.FromInfluence(a, Intensity));
			}
			Opinion o = GetOrCreate(d, speaker, target);
			string before = OpinionRules.Snapshot(o);
			ApplyInfluenceTo(o, a);
			if (OpinionRules.Snapshot(o) == before)
			{
				continue;
			}
			changed++;
			AdultRules.Apply(d.Adult, o, DramaSources.Player, a.OnDay, Gates, null, Name);
			AddInfluenceBeat(d, o, a);
		}
		d.Influences = d.Influences.Take(MaxInfluences).ToList();
		Dirty();
		_log.Debug($"社交剧情：农夫对 {speaker} 说起 {string.Join("、", targets)}（{action}，{result}，接受度 {acceptance:0.00}），改了 {changed} 条看法。");
		return changed;
	}

	// ---------------- 读 ----------------

	/// <summary>对话提示词里的"村民之间的事"一块；没有可说的就是空。</summary>
	public string PromptBlock(string npc, int maxOpinions = 3)
	{
		if (!Enabled || string.IsNullOrWhiteSpace(npc) || !_npcEnabled(npc))
		{
			return "";
		}
		DramaState d = Data;
		List<Opinion> opinions = d.Opinions.Where(o => DramaRules.Same(o.Who, npc) && Referable(o.About) && ModsUsable(o.Origins))
			.Where(OpinionRules.Notable).OrderByDescending(OpinionRules.Weight).ThenByDescending(o => o.MovedOn).Take(maxOpinions).ToList();
		List<Influence> influences = d.Influences.Where(a => DramaRules.Same(a.Talker, npc) && Referable(a.Aim) && ModsUsable(a.Origins) && a.Upshot != InfluenceRules.Rejected)
			.OrderByDescending(a => a.OnDay).Take(2).ToList();
		List<Rumor> rumors = d.Rumors.Where(r => RumorRules.KnownBy(r, npc)).Where(RumorUsable)
			.OrderByDescending(r => r.Heat + r.Harm).ThenByDescending(r => r.BornDay).Take(3).ToList();
		List<AdultStrain> adult = d.Adult.Where(s => DramaRules.Same(s.Villager, npc)).Where(AdultUsable)
			.OrderByDescending(s => s.Force + s.Pull).ThenByDescending(s => s.MovedDay).Take(2).ToList();
		if (opinions.Count == 0 && influences.Count == 0 && rumors.Count == 0 && adult.Count == 0)
		{
			return "";
		}
		return DramaPrompt.Block(opinions, influences, rumors, adult, npc, Intensity == DramaRules.Dramatic, Name);
	}

	/// <summary>全村对每个人的敌意：持有人看法的平均（至少 <paramref name="minHolders"/> 个持有人），从高到低。</summary>
	public IReadOnlyList<(string Npc, float Hostility)> Hostility(int minHolders = 3) =>
		Data.Opinions.Where(o => !string.IsNullOrWhiteSpace(o.About)).GroupBy(o => o.About.Trim(), StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() >= minHolders)
			.Select(g => (Npc: g.Key, Hostility: g.Average(o => DramaRules.Clamp01((o.Annoyance + o.Doubt + (1f - o.Esteem) + (1f - o.Faith)) / 4f))))
			.OrderByDescending(x => x.Hostility).ToList();

	public IReadOnlyList<Opinion> OpinionsHeldBy(string npc) =>
		!Enabled || string.IsNullOrWhiteSpace(npc) || !_npcEnabled(npc) ? Array.Empty<Opinion>()
		: Data.Opinions.Where(o => DramaRules.Same(o.Who, npc) && Referable(o.About) && ModsUsable(o.Origins)).ToList();

	public IReadOnlyList<Rumor> RumorsKnownBy(string npc, int today) =>
		!Enabled || string.IsNullOrWhiteSpace(npc) || !_npcEnabled(npc) ? Array.Empty<Rumor>()
		: Data.Rumors.Where(r => RumorRules.KnownBy(r, npc) && (r.DiesDay <= 0 || r.DiesDay >= today)).Where(RumorUsable).ToList();

	// ---------------- 内部 ----------------

	private string Name(string npc) => DramaRules.Same(npc, RumorRules.Farmer) ? "农夫" : _display(npc);

	private void Dirty() => _store()?.MarkDirty(Path);

	private bool Referable(string npc) => DramaRules.Same(npc, RumorRules.Farmer) || _npcEnabled(npc);

	private bool ModsUsable(IEnumerable<string>? mods) => (mods ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).All(_modEnabled);

	private bool GossipUsable(GossipEntry e) =>
		ModsUsable(e.FromMods) && new[] { e.StartedBy, e.ToldBy }.Concat(e.KnownTo ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).All(Referable);

	private bool RumorUsable(Rumor r) =>
		ModsUsable(r.Origins) && new[] { r.Source, r.Carrier }.Concat(r.About).Concat(r.Heard).Concat(r.Convinced).Concat(r.Doubters).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).All(Referable);

	private bool AdultUsable(AdultStrain s) =>
		ModsUsable(s.Origins) && new[] { s.Villager, s.Spouse, s.Third }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).All(Referable);

	private static Opinion GetOrCreate(DramaState d, string holder, string target)
	{
		Opinion? o = d.Opinions.FirstOrDefault(x => DramaRules.Same(x.Who, holder) && DramaRules.Same(x.About, target));
		if (o == null)
		{
			o = OpinionRules.Fresh(holder, target);
			d.Opinions.Add(o);
		}
		return o;
	}

	private void ApplyGossipTo(Opinion o, GossipEntry e, int sentiment, int day)
	{
		float step = DramaRules.Step(Intensity);
		float d = DramaRules.Close(o.Who, o.About) && !DramaRules.Same(e.StartedBy, o.Who) ? step * 0.35f : step;
		if (sentiment < 0)
		{
			o.Annoyance = DramaRules.Clamp01(o.Annoyance + d);
			o.Doubt = DramaRules.Clamp01(o.Doubt + d * 0.8f);
			o.Faith = DramaRules.Clamp01(o.Faith - d * 0.45f);
			o.Esteem = DramaRules.Clamp01(o.Esteem - d * 0.35f);
			o.Labels = DramaRules.Tag(o.Labels, "gossip_influenced", "negative_gossip");
		}
		else
		{
			o.Annoyance = DramaRules.Clamp01(o.Annoyance - d * 0.6f);
			o.Doubt = DramaRules.Clamp01(o.Doubt - d * 0.5f);
			o.Faith = DramaRules.Clamp01(o.Faith + d * 0.45f);
			o.Esteem = DramaRules.Clamp01(o.Esteem + d * 0.5f);
			o.Labels = DramaRules.Tag(o.Labels, "gossip_influenced", "positive_gossip");
		}
		o.Attitude = OpinionRules.Classify(o);
		o.Why = DramaPrompt.GossipReason(e, Name);
		o.MovedOn = day;
	}

	private void ApplyInfluenceTo(Opinion o, Influence a)
	{
		float d = DramaRules.Step(Intensity) * (0.35f + a.Uptake);
		if (DramaRules.Close(o.Who, o.About) && a.Slant < 0)
		{
			d *= 0.45f;
		}
		if (a.Slant < 0)
		{
			o.Annoyance = DramaRules.Clamp01(o.Annoyance + d);
			o.Doubt = DramaRules.Clamp01(o.Doubt + d * (a.Move == DramaText.Leak ? 1.05f : 0.75f));
			o.Faith = DramaRules.Clamp01(o.Faith - d * 0.35f);
			o.Esteem = DramaRules.Clamp01(o.Esteem - d * 0.25f);
			o.Labels = DramaRules.Tag(o.Labels, "player_influenced", a.Move);
		}
		else
		{
			o.Annoyance = DramaRules.Clamp01(o.Annoyance - d * 0.8f);
			o.Doubt = DramaRules.Clamp01(o.Doubt - d * 0.7f);
			o.Faith = DramaRules.Clamp01(o.Faith + d * 0.4f);
			o.Esteem = DramaRules.Clamp01(o.Esteem + d * 0.45f);
			o.Labels = DramaRules.Tag(o.Labels, "player_influenced", "defended");
		}
		o.Attitude = OpinionRules.Classify(o);
		o.Why = DramaPrompt.InfluenceSentence(a, Name) + "原话：" + DramaRules.Trim(a.Subject);
		o.MovedOn = a.OnDay;
	}

	private void AddGossipBeat(DramaState d, Opinion o, GossipEntry e, int sentiment, int day)
	{
		if (sentiment >= 0 || !_config().BackbitingOn || BeatsFull(d, day) || (o.Annoyance < 0.18f && o.Doubt < 0.18f))
		{
			return;
		}
		bool sour = o.Attitude is Stances.Cold or Stances.Badmouthing or Stances.Betrayed;
		d.Beats.Insert(0, new DramaBeat
		{
			Id = Guid.NewGuid().ToString("N"),
			OnDay = day,
			Kind = sour ? "private_vent" : "sharp_comment",
			Doer = o.Who,
			Receiver = o.About,
			Gist = $"{Name(o.Who)}听了八卦，对{Name(o.About)}{DramaPrompt.Stance(o.Attitude)}。",
			Force = Math.Max(o.Annoyance, o.Doubt),
			Labels = DramaRules.Names(new[] { "gossip", e.StartedBy }).ToList()
		});
		d.Beats = d.Beats.Take(MaxBeats).ToList();
	}

	private void AddInfluenceBeat(DramaState d, Opinion o, Influence a)
	{
		if (BeatsFull(d, a.OnDay))
		{
			return;
		}
		d.Beats.Insert(0, new DramaBeat
		{
			Id = Guid.NewGuid().ToString("N"),
			OnDay = a.OnDay,
			Kind = a.Slant >= 0 ? "defense" : a.Move == DramaText.Leak ? "leaked_claim" : "instigated_suspicion",
			Doer = o.Who,
			Receiver = o.About,
			Gist = DramaPrompt.InfluenceSummary(a, Name),
			Force = Math.Max(o.Annoyance, o.Doubt),
			Labels = new List<string> { "player_influence", a.Move }
		});
		d.Beats = d.Beats.Take(MaxBeats).ToList();
	}

	private bool BeatsFull(DramaState d, int day)
	{
		return d.Beats.Count(b => b.OnDay == day) >= _config().DramaBeatsDaily;
	}

	private static bool Upsert(DramaState d, Rumor r)
	{
		RumorRules.Normalize(r);
		Rumor? current = d.Rumors.FirstOrDefault(x => (!string.IsNullOrWhiteSpace(r.ComesFrom) && DramaRules.Same(x.ComesFrom, r.ComesFrom)) || DramaRules.Same(x.Id, r.Id));
		if (current == null)
		{
			d.Rumors.Insert(0, r);
			d.Rumors = d.Rumors.Take(MaxRumors).ToList();
			return true;
		}
		string before = RumorRules.Snapshot(current);
		RumorRules.MergeInto(current, r);
		return RumorRules.Snapshot(current) != before;
	}

	private static bool Decay(DramaState d)
	{
		bool changed = false;
		foreach (Opinion o in d.Opinions)
		{
			string before = OpinionRules.Snapshot(o);
			bool close = DramaRules.Close(o.Who, o.About);
			o.Annoyance = DramaRules.Toward(o.Annoyance, 0f, 0.015f);
			o.Doubt = DramaRules.Toward(o.Doubt, 0f, 0.012f);
			o.Envy = DramaRules.Toward(o.Envy, 0f, 0.01f);
			o.Faith = DramaRules.Toward(o.Faith, close ? 0.6f : 0.5f, 0.004f);
			o.Esteem = DramaRules.Toward(o.Esteem, close ? 0.55f : 0.5f, 0.004f);
			o.Attitude = OpinionRules.Classify(o);
			changed |= OpinionRules.Snapshot(o) != before;
		}
		d.Opinions = d.Opinions.Where(o => OpinionRules.Notable(o) || Math.Abs(o.Faith - 0.5f) > 0.08f || Math.Abs(o.Esteem - 0.5f) > 0.08f || DramaRules.Close(o.Who, o.About)).Take(MaxOpinions).ToList();
		return changed;
	}

	private static void Normalize(DramaState d)
	{
		d.Opinions ??= new List<Opinion>();
		d.Beats ??= new List<DramaBeat>();
		d.Influences ??= new List<Influence>();
		d.Rumors ??= new List<Rumor>();
		d.Adult ??= new List<AdultStrain>();
		foreach (Opinion o in d.Opinions)
		{
			OpinionRules.Normalize(o);
		}
		foreach (DramaBeat b in d.Beats)
		{
			b.Labels ??= new List<string>();
		}
		foreach (Influence a in d.Influences)
		{
			a.Origins = DramaRules.Names(a.Origins);
		}
		foreach (Rumor r in d.Rumors)
		{
			RumorRules.Normalize(r);
		}
		foreach (AdultStrain s in d.Adult)
		{
			AdultRules.Normalize(s);
		}
	}
}
