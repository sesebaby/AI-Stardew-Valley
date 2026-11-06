using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Game.Dialogue;
using AISV.Game.World;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Quests;

namespace AISV.Game.Companions;

/// <summary>
/// 伴侣剧情：结了婚（含多位伴侣、室友），每隔几天 AI 按伴侣的性格、你们的记忆、你最近做的事写一段小故事，几步按顺序来：
/// 信（第二天早上到，可以附 AI 礼物）、拜托你的事（任务日志 7 天）、见面（约见，到了就地演一场戏）、有话想说（找他说话时听完）。
/// 结构由规则定，AI 只写内容。只在主机上运行。
/// </summary>
public sealed class SpouseStoryService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly MeetupService _meetups;

	private readonly Ecosystem _eco;

	private readonly MarketService _market;

	private readonly Func<string, bool> _isNpcEnabled;

	private readonly ILog _log;

	private readonly HashSet<string> _generating = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private (string Package, int Step, int StartTick)? _scene;

	private string? _skipBeat;

	/// <summary>选了"先说别的"：等你空下来接着照常聊（村民、最晚到哪一帧）。</summary>
	private (NPC Npc, int Until)? _resumeTalk;

	private int _session;

	private int _ticks;

	private static bool _commandRegistered;

	private static Action<string, int, int, int>? _choiceMade;

	public SpouseStoryService(Func<ModConfig> config, Func<DocumentStore?> store, MeetupService meetups, Ecosystem eco, MarketService market, Func<string, bool> isNpcEnabled, ILog log)
	{
		_config = config;
		_store = store;
		_meetups = meetups;
		_eco = eco;
		_market = market;
		_isNpcEnabled = isNpcEnabled;
		_log = log;
		_meetups.StoryKept = OnStoryKept;
		_meetups.MeetupClosed += OnMeetupClosed;
		_choiceMade = OnChoice;
		RegisterChoiceCommand();
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Personality { get; set; }

	public Func<EmotionalMemory?>? Feelings { get; set; }

	/// <summary>伴侣对农夫的感情（编剧资料用）：村民、心数 → 提示词块。</summary>
	public Func<string, int, string>? FeelingsBlock { get; set; }

	/// <summary>寄一封信：村民、触发、标题、正文、礼物理由（为空不附）→ 信的键和礼物 ID。</summary>
	public Func<string, string, string, string, string?, (string Key, string? GiftId)?>? PostLetter { get; set; }

	/// <summary>伴侣发信使消息；没发出去返回 false。</summary>
	public Func<string, string, bool>? Text { get; set; }

	/// <summary>成人模块开着。</summary>
	public Func<bool>? Adult { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	/// <summary>村民说一段不走 AI 流程的话。</summary>
	public Action<NPC, string>? Say { get; set; }

	public int Version { get; private set; }

	public string LastRaw { get; private set; } = "";

	public string LastError { get; private set; } = "";

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.SpouseChaptersOn;
		}
	}

	private static int Today => Game1.Date?.TotalDays ?? 0;

	private SpouseStoryLog? _normalized;

	private SpouseStoryLog? Log
	{
		get
		{
			SpouseStoryLog? log = _store()?.Get(SpouseStoryLog.Path, SpouseStoryLog.Doc);
			if (log != null && !ReferenceEquals(log, _normalized))
			{
				log.Normalize();
				_normalized = log;
			}
			return log;
		}
	}

	// ---------------- 查询 ----------------

	/// <summary>你的伴侣：已婚（含多位）和室友，不含孩子和关掉的村民。</summary>
	public IReadOnlyList<string> Partners()
	{
		Farmer? player = Game1.player;
		if (player == null)
		{
			return Array.Empty<string>();
		}
		List<string> names = player.friendshipData.Pairs.Where(p => p.Value != null && (p.Value.IsMarried() || p.Value.IsRoommate())).Select(p => p.Key).ToList();
		if (!string.IsNullOrWhiteSpace(player.spouse))
		{
			names.Add(player.spouse);
		}
		return names.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(n => Game1.getCharacterFromName(n) is NPC npc && !ChildSafety.IsChild(n, npc.Age) && _isNpcEnabled(n))
			.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public IReadOnlyList<StoryPackage> All() => Log?.Packages.ToList() ?? new List<StoryPackage>();

	public StoryPackage? Find(string id) => Log?.Packages.FirstOrDefault(p => p.Id == id);

	public StoryPackage? ActiveFor(string partner) => All().FirstOrDefault(p => StoryStates.IsOpen(p.State) && string.Equals(p.Partner, partner, StringComparison.OrdinalIgnoreCase));

	public IReadOnlyList<StoryPackage> HistoryFor(string partner) => All().Where(p => !StoryStates.IsOpen(p.State) && string.Equals(p.Partner, partner, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.ClosedDay).ToList();

	public bool IsGenerating(string partner) => _generating.Contains(partner);

	public string Theme(string partner) => Log?.Themes.TryGetValue(partner, out string? t) == true ? t! : "";

	public int NextStoryDay => Log?.NextStoryDay ?? 0;

	// ---------------- 生命周期 ----------------

	public void OnSaveLoaded()
	{
		_generating.Clear();
		_scene = null;
		_skipBeat = null;
		_resumeTalk = null;
		_session++;
		Version++;
	}

	public void OnReturnedToTitle()
	{
		_generating.Clear();
		_scene = null;
		_session++;
	}

	/// <summary>早上：寄信、查任务和期限、给见面那一步排约见；到日子了写下一段。要在约见之后跑。</summary>
	public void OnDayStarted()
	{
		if (Log is not SpouseStoryLog log || !Context.IsMainPlayer)
		{
			return;
		}
		TakeSnapshot(log);
		if (!Enabled)
		{
			MarkDirty();
			return;
		}
		IReadOnlyList<string> partners = Partners();
		foreach (StoryPackage p in log.Packages.Where(p => StoryStates.IsOpen(p.State)).ToList())
		{
			if (!partners.Contains(p.Partner, StringComparer.OrdinalIgnoreCase))
			{
				Abandon(p, "不再是伴侣了");
				continue;
			}
			if (p.Current is not StoryStep step)
			{
				Finish(p);
				continue;
			}
			MorningCheck(p, step);
		}
		MaybeStartDue(log, partners);
		MarkDirty();
	}

	private void MorningCheck(StoryPackage p, StoryStep step)
	{
		switch (step.Kind)
		{
			case StepKinds.Letter:
				if (!step.Delivered && step.DueDay <= Today)
				{
					DeliverLetter(p, step);
				}
				break;
			case StepKinds.Quest:
				if (step.Quest is not StoryQuest q)
				{
					Abandon(p, "任务资料丢了");
				}
				else if (FindQuest(q) == null || Today > q.LastDay)
				{
					Fail(p, step, "任务过期了", SpouseStoryRules.QuestExpiredMemory(p, q));
				}
				break;
			case StepKinds.Talk:
				if (Today - step.UnlockedDay > SpouseStoryRules.TalkDays)
				{
					Abandon(p, "农夫一直没来听");
				}
				break;
			case StepKinds.Event:
				if (step.MeetupId.Length == 0)
				{
					TryArrange(p, step);
				}
				break;
		}
	}

	public void OnDayEnding()
	{
		if (Log is not SpouseStoryLog log || !Context.IsMainPlayer)
		{
			return;
		}
		RecordActivityDay(log);
		MarkDirty();
	}

	public void OnWarped(GameLocation? location)
	{
		if (!Context.IsMainPlayer || Log is not SpouseStoryLog log || location == null || location is FarmHouse || location is Farm)
		{
			return;
		}
		string name = TextTools.CleanForGame(location.DisplayName ?? location.Name);
		if (name.Length == 0 || log.TodayPlaces.Contains(name) || log.TodayPlaces.Count >= SpouseStoryRules.MaxPlacesPerDay)
		{
			return;
		}
		log.TodayPlaces.Add(name);
		MarkDirty();
	}

	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || !Context.IsMainPlayer || _store() == null)
		{
			return;
		}
		ResumeTalk();
		if (_scene is { } scene)
		{
			if (!Game1.eventUp && Game1.CurrentEvent == null && Game1.ticks - scene.StartTick > 30)
			{
				_scene = null;
				FinishScene(scene.Package, scene.Step);
			}
			return;
		}
		if (++_ticks % 30 != 0 || !Enabled)
		{
			return;
		}
		foreach (StoryPackage p in All().Where(p => StoryStates.IsOpen(p.State)))
		{
			if (p.Current is not StoryStep step)
			{
				continue;
			}
			if (step.Kind == StepKinds.Letter && step.Delivered && Game1.player.mailReceived.Contains(step.MailKey))
			{
				step.Note = "读了";
				Complete(p, step);
			}
			else if (step.Kind == StepKinds.Quest && step.Quest?.Kind == StoryQuestKinds.Slay)
			{
				UpdateSlayProgress(p, step);
			}
		}
	}

	// ---------------- 开始一段 ----------------

	private void MaybeStartDue(SpouseStoryLog log, IReadOnlyList<string> partners)
	{
		if (partners.Count == 0)
		{
			return;
		}
		if (log.NextStoryDay == 0)
		{
			log.NextStoryDay = Today + 1;
			return;
		}
		if (Today < log.NextStoryDay || _generating.Count > 0)
		{
			return;
		}
		foreach (string partner in SpouseStoryRules.RotationFrom(partners, log.LastPartner))
		{
			if (ActiveFor(partner) != null)
			{
				continue;
			}
			if (StartStory(partner, null, out string reason))
			{
				return;
			}
			_log.Debug($"伴侣剧情：{partner} 的下一段开不了：{reason}。");
		}
	}

	/// <summary>让 AI 写这位伴侣的下一段（后台）。composition 为空时随机，不和他上一段一样。</summary>
	public bool StartStory(string partner, int? composition, out string reason)
	{
		reason = "";
		if (Log is not SpouseStoryLog || !Context.IsMainPlayer)
		{
			reason = "没进存档";
			return false;
		}
		if (!Enabled)
		{
			reason = "伴侣剧情关着";
			return false;
		}
		if (Ai is not IAiSender ai || !_config().AiOn)
		{
			reason = "AI 关着";
			return false;
		}
		string? name = Partners().FirstOrDefault(n => string.Equals(n, partner, StringComparison.OrdinalIgnoreCase));
		NPC? npc = name == null ? null : Game1.getCharacterFromName(name);
		if (name == null || npc == null)
		{
			reason = "不是你的伴侣";
			return false;
		}
		if (ActiveFor(name) != null)
		{
			reason = "已经有一段在进行";
			return false;
		}
		if (!_generating.Add(name))
		{
			reason = "正在写";
			return false;
		}
		Random rng = new Random(Guid.NewGuid().GetHashCode());
		int comp = composition ?? SpouseStoryRules.PickComposition(LastComposition(name), rng);
		StoryCandidates candidates = BuildCandidates(name, rng);
		string theme = Theme(name);
		string system = SpouseStoryRules.System(Language?.Invoke() ?? "中文", Display(name), Personality?.Invoke(name) ?? "", Brief(name, comp, candidates, theme));
		string user = SpouseStoryRules.User(Display(name), comp);
		int session = _session;
		_log.Info($"伴侣剧情：在写 {name} 的下一段（结构 {comp + 1}：{SpouseStoryRules.Describe(comp)}{(theme.Length > 0 ? "，心愿：" + theme : "")}）。");
		Version++;
		Task.Run(async () =>
		{
			string? text = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(SpouseStoryRules.RequestName, system, user, SpouseStoryRules.MaxOutputTokens, SpouseStoryRules.Temperature)).ConfigureAwait(false);
				text = r.Ok ? r.Text : null;
			}
			catch (Exception ex)
			{
				_log.Warn("伴侣剧情：AI 请求失败：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				_generating.Remove(name);
				Version++;
				if (session != _session || Log is not SpouseStoryLog log)
				{
					return;
				}
				StoryPackage? pkg = SpouseStoryRules.Parse(text, comp, candidates, CommissionService.BasePrice, out string error);
				LastRaw = text ?? "";
				LastError = error;
				if (pkg == null)
				{
					_log.Warn($"伴侣剧情：{name} 的故事写得不能用（{error}），明天再试。");
					log.NextStoryDay = Math.Max(log.NextStoryDay, Today + 1);
					MarkDirty();
					return;
				}
				Accept(pkg, name, theme);
			});
		});
		return true;
	}

	private int LastComposition(string partner) => All().Where(p => string.Equals(p.Partner, partner, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.CreatedDay).Select(p => p.Composition).DefaultIfEmpty(-1).First();

	private string Brief(string partner, int comp, StoryCandidates candidates, string theme)
	{
		ModConfig c = _config();
		return SpouseStoryRules.Brief(Display(partner), comp, SpouseStoryRules.Context(BuildContext(partner, theme)), candidates, c.SpouseChapterFlavor, Adult?.Invoke() == true, key => MeetupPlaces.Label(key));
	}

	/// <summary>收下一段写好的故事，开始第一步。</summary>
	public StoryPackage? Accept(StoryPackage pkg, string partner, string theme)
	{
		if (Log is not SpouseStoryLog log || pkg.Steps.Count == 0)
		{
			return null;
		}
		pkg.Id = "s" + log.NextNumber++;
		pkg.Partner = partner;
		pkg.Theme = theme ?? "";
		pkg.CreatedDay = Today;
		pkg.State = StoryStates.Active;
		log.Packages.Add(pkg);
		log.LastPartner = partner;
		log.NextStoryDay = Today + Math.Max(1, _config().SpouseChapterGap);
		if (!string.IsNullOrEmpty(theme))
		{
			log.Themes.Remove(partner);
		}
		List<StoryPackage> closed = log.Packages.Where(p => !StoryStates.IsOpen(p.State)).OrderBy(p => p.ClosedDay).ToList();
		foreach (StoryPackage old in closed.Take(Math.Max(0, log.Packages.Count - SpouseStoryRules.KeepPackages)))
		{
			log.Packages.Remove(old);
		}
		_log.Info($"伴侣剧情 {pkg.Id}：{partner}《{pkg.Title}》（{string.Join(" → ", pkg.Steps.Select(s => s.Kind))}）。");
		Unlock(pkg, pkg.Steps[0]);
		Version++;
		MarkDirty();
		return pkg;
	}

	public void SetTheme(string partner, string theme)
	{
		if (!Context.IsMainPlayer || Log is not SpouseStoryLog log)
		{
			return;
		}
		string t = SpouseStoryRules.Clean(theme, SpouseStoryRules.MaxThemeLength);
		if (t.Length == 0)
		{
			log.Themes.Remove(partner);
		}
		else
		{
			log.Themes[partner] = t;
		}
		Version++;
		MarkDirty();
	}

	// ---------------- 步骤 ----------------

	private void Unlock(StoryPackage p, StoryStep step)
	{
		step.State = StepStates.Ready;
		step.UnlockedDay = Today;
		switch (step.Kind)
		{
			case StepKinds.Letter:
				step.DueDay = Today + 1;
				break;
			case StepKinds.Quest:
				AddQuest(p, step);
				break;
			case StepKinds.Talk:
				Hud(Tr.Get("story.talkHint", "{{npc}}好像有话想跟你说。", ("npc", Display(p.Partner))));
				break;
			case StepKinds.Event:
				TryArrange(p, step);
				break;
		}
		_log.Info($"伴侣剧情 {p.Id}：第 {step.Index + 1}/{p.Steps.Count} 步（{step.Kind}）开始。");
	}

	private void Complete(StoryPackage p, StoryStep step)
	{
		step.State = StepStates.Done;
		step.DoneDay = Today;
		_log.Info($"伴侣剧情 {p.Id}：第 {step.Index + 1} 步（{step.Kind}）完成{(step.Note.Length > 0 ? "：" + step.Note : "")}。");
		if (p.Steps.FirstOrDefault(s => s.Index == step.Index + 1) is StoryStep next)
		{
			Unlock(p, next);
		}
		else
		{
			Finish(p);
		}
		Version++;
		MarkDirty();
	}

	private void Finish(StoryPackage p)
	{
		p.State = StoryStates.Done;
		p.ClosedDay = Today;
		p.Outcome = string.Join("；", p.Steps.Select(s => s.Note).Where(n => n.Length > 0));
		Remember(p.Partner, SpouseStoryRules.FinishedMemory(p));
		_log.Info($"伴侣剧情 {p.Id}：圆满结束。");
		Version++;
		MarkDirty();
	}

	private void Fail(StoryPackage p, StoryStep step, string reason, string memory)
	{
		step.State = StepStates.Failed;
		step.Note = reason;
		p.State = StoryStates.Failed;
		p.ClosedDay = Today;
		p.Outcome = reason;
		RemoveQuests(p);
		ReleaseMeetups(p);
		Remember(p.Partner, memory);
		_log.Info($"伴侣剧情 {p.Id}：第 {step.Index + 1} 步没成：{reason}。");
		Version++;
		MarkDirty();
	}

	private void Abandon(StoryPackage p, string reason)
	{
		p.State = StoryStates.Abandoned;
		p.ClosedDay = Today;
		p.Outcome = reason;
		RemoveQuests(p);
		ReleaseMeetups(p);
		_log.Info($"伴侣剧情 {p.Id}：不了了之：{reason}。");
		Version++;
		MarkDirty();
	}

	// ---------------- 信 ----------------

	private string LetterContent(StoryPackage p, StoryStep step) => step.LetterText.Replace("\n", "^") + "^^   — " + Display(p.Partner);

	private void DeliverLetter(StoryPackage p, StoryStep step)
	{
		string trigger = $"{AISV.Core.Social.Letters.LetterBook.StoryPrefix}{p.Id}#{step.Index}";
		string? giftReason = step.Gift ? $"随信附的小礼物（《{p.Title}》）" : null;
		(string Key, string? GiftId)? sent = PostLetter?.Invoke(p.Partner, trigger, p.Title, step.LetterText.Replace("\n", "^"), giftReason);
		if (sent == null)
		{
			_log.Warn($"伴侣剧情 {p.Id}：信没寄出去。");
			return;
		}
		step.MailKey = sent.Value.Key;
		step.GiftId = sent.Value.GiftId ?? "";
		step.Delivered = true;
		_log.Info($"伴侣剧情 {p.Id}：{p.Partner} 的信放进信箱了{(step.GiftId.Length > 0 ? "（附礼物）" : "")}。");
	}

	// ---------------- 任务 ----------------

	public static string QuestTargetName(StoryQuest q) => q.TargetName.Length > 0 ? q.TargetName : q.Target;

	private string Objective(StoryPackage p, StoryQuest q, int have)
	{
		string npc = Display(p.Partner);
		if (q.Kind == StoryQuestKinds.Slay)
		{
			return have >= q.Count
				? Tr.Get("story.objective.slayDone", "回去告诉{{npc}}吧", ("npc", npc))
				: Tr.Get("story.objective.slay", "在矿洞里打倒{{target}}：{{have}}/{{count}}", ("target", QuestTargetName(q)), ("have", have), ("count", q.Count));
		}
		return Tr.Get("story.objective.deliver", "把 {{count}} 个{{target}}交给{{npc}}", ("count", q.Count), ("target", QuestTargetName(q)), ("npc", npc));
	}

	private void AddQuest(StoryPackage p, StoryStep step)
	{
		if (step.Quest is not StoryQuest q)
		{
			return;
		}
		q.QuestId = $"{SpouseStoryRules.Prefix}{p.Id}_{step.Index}";
		q.LastDay = Today + SpouseStoryRules.QuestDays - 1;
		if (q.Kind == StoryQuestKinds.Slay)
		{
			q.Baseline = Game1.stats.getMonstersKilled(q.Target);
		}
		Quest quest = new Quest();
		quest.questType.Value = 1;
		quest.id.Value = q.QuestId;
		quest.questTitle = TextTools.CleanForGame(q.Title);
		quest.questDescription = TextTools.CleanForGame(q.Description);
		quest.currentObjective = TextTools.CleanForGame(Objective(p, q, 0));
		quest.showNew.Value = true;
		quest.accepted.Value = true;
		quest.canBeCancelled.Value = false;
		quest.daysLeft.Value = SpouseStoryRules.QuestDays;
		quest.dayQuestAccepted.Value = Today;
		Game1.player.questLog.Add(quest);
		Hud(Tr.Get("story.questAdded", "{{npc}}拜托你：{{title}}", ("npc", Display(p.Partner)), ("title", q.Title)));
	}

	private static Quest? FindQuest(StoryQuest q) => Game1.player?.questLog.FirstOrDefault(x => x.id.Value == q.QuestId);

	private void ReleaseMeetups(StoryPackage p)
	{
		foreach (StoryStep s in p.Steps.Where(s => s.MeetupId.Length > 0))
		{
			_meetups.CancelForOwner(s.MeetupId, "story ended");
		}
	}

	private static void RemoveQuests(StoryPackage p)
	{
		foreach (StoryStep s in p.Steps.Where(s => s.Quest != null))
		{
			if (FindQuest(s.Quest!) is Quest quest)
			{
				Game1.player.questLog.Remove(quest);
			}
		}
	}

	public static int SlayProgress(StoryQuest q) => Math.Max(0, Game1.stats.getMonstersKilled(q.Target) - q.Baseline);

	private void UpdateSlayProgress(StoryPackage p, StoryStep step)
	{
		StoryQuest q = step.Quest!;
		if (FindQuest(q) is not Quest quest)
		{
			return;
		}
		int have = Math.Min(q.Count, SlayProgress(q));
		string objective = TextTools.CleanForGame(Objective(p, q, have));
		if (quest.currentObjective != objective)
		{
			quest.currentObjective = objective;
			Game1.dayTimeMoneyBox.questsDirty = true;
		}
		if (have >= q.Count && !q.ReadyHinted)
		{
			q.ReadyHinted = true;
			Hud(objective);
			MarkDirty();
		}
	}

	/// <summary>伴侣现在要你拿来的东西（送东西、钓鱼）；没有为空。</summary>
	private (StoryPackage Package, StoryStep Step, StoryQuest Quest)? WantedFrom(NPC npc)
	{
		if (!Enabled || ActiveFor(npc.Name) is not StoryPackage p || p.Current is not StoryStep step || step.Kind != StepKinds.Quest || step.Quest is not StoryQuest q || q.Kind == StoryQuestKinds.Slay)
		{
			return null;
		}
		return (p, step, q);
	}

	/// <summary>拿着伴侣要的东西按动作键：够了交付返回 true；不够提示还差几个、照常送礼。</summary>
	public bool TryHandIn(NPC npc, Farmer who, bool probe)
	{
		if (who.ActiveObject is not StardewValley.Object held || WantedFrom(npc) is not { } wanted || held.QualifiedItemId != wanted.Quest.Target)
		{
			return false;
		}
		(StoryPackage p, StoryStep step, StoryQuest q) = wanted;
		int have = CommissionService.CountInInventory(who, q.Target);
		if (have < q.Count)
		{
			if (!probe)
			{
				Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(Tr.Get("commission.deliverMissing", "手上才 {{have}} 个，要 {{need}} 个才够", ("have", have), ("need", q.Count - have))), HUDMessage.error_type));
			}
			return false;
		}
		if (!probe)
		{
			CompleteQuest(p, step, npc, who);
		}
		return true;
	}

	private void CompleteQuest(StoryPackage p, StoryStep step, NPC npc, Farmer player)
	{
		StoryQuest q = step.Quest!;
		if (q.Kind != StoryQuestKinds.Slay)
		{
			int left = q.Count;
			foreach (Item item in player.Items.Where(i => i != null && i.QualifiedItemId == q.Target && i is not Tool).OrderBy(i => i.Quality).ToList())
			{
				int take = Math.Min(left, item.Stack);
				item.Stack -= take;
				left -= take;
				if (item.Stack <= 0)
				{
					player.removeItemFromInventory(item);
				}
				if (left <= 0)
				{
					break;
				}
			}
			player.completelyStopAnimatingOrDoingAction();
			npc.currentLocation?.localSound("give_gift");
		}
		if (FindQuest(q) is Quest quest)
		{
			quest.questComplete();
			Game1.player.questLog.Remove(quest);
		}
		player.changeFriendship(SpouseStoryRules.QuestFriendship, npc);
		Speak(npc, new[] { new StoryLine { Text = q.DoneLine, Mood = q.DoneMood } });
		Remember(p.Partner, SpouseStoryRules.QuestDoneMemory(p, q));
		step.Note = "办完了";
		Complete(p, step);
	}

	// ---------------- 找伴侣说话时 ----------------

	/// <summary>伴侣有话想说、或者打怪数够了：先问一句。处理了返回 true。</summary>
	public bool TryOfferBeat(NPC npc)
	{
		if (string.Equals(_skipBeat, npc.Name, StringComparison.OrdinalIgnoreCase))
		{
			_skipBeat = null;
			return false;
		}
		if (!Enabled || !Context.IsMainPlayer || ActiveFor(npc.Name) is not StoryPackage p || p.Current is not StoryStep step)
		{
			return false;
		}
		string name = Display(npc.Name);
		string question, option;
		if (step.Kind == StepKinds.Talk)
		{
			question = Tr.Get("story.talkHint", "{{npc}}好像有话想跟你说。", ("npc", name));
			option = Tr.Get("story.talkOption", "聊聊{{topic}}", ("topic", step.Topic));
		}
		else if (step.Kind == StepKinds.Quest && step.Quest is StoryQuest q && q.Kind == StoryQuestKinds.Slay && SlayProgress(q) >= q.Count)
		{
			question = Tr.Get("story.reportAsk", "{{npc}}在等你的消息。", ("npc", name));
			option = Tr.Get("story.reportOption", "告诉{{npc}}：{{title}}", ("npc", name), ("title", q.Title));
		}
		else
		{
			return false;
		}
		string id = p.Id;
		int index = step.Index;
		Response[] answers = { new Response("story", TextTools.CleanForGame(option)), new Response("later", TextTools.CleanForGame(Tr.Get("story.later", "先说别的"))) };
		npc.faceTowardFarmerForPeriod(3000, 4, faceAway: false, Game1.player);
		Game1.currentLocation.createQuestionDialogue(TextTools.CleanForGame(question), answers, (who, answer) =>
		{
			StoryPackage? live = Find(id);
			if (answer == "story" && live?.Current is StoryStep s && s.Index == index)
			{
				DelayedAction.functionAfterDelay(() => PlayBeat(live, s, npc), 50);
				return;
			}
			_resumeTalk = (npc, Game1.ticks + 120);
		}, npc);
		return true;
	}

	private void ResumeTalk()
	{
		if (_resumeTalk is not { } r)
		{
			return;
		}
		if (Game1.ticks > r.Until || r.Npc.currentLocation != Game1.currentLocation)
		{
			_resumeTalk = null;
			return;
		}
		if (!Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.dialogueUp)
		{
			return;
		}
		_resumeTalk = null;
		_skipBeat = r.Npc.Name;
		try
		{
			r.Npc.checkAction(Game1.player, Game1.currentLocation);
		}
		finally
		{
			_skipBeat = null;
		}
	}

	private void PlayBeat(StoryPackage p, StoryStep step, NPC npc)
	{
		if (step.Kind == StepKinds.Talk)
		{
			Speak(npc, step.Lines);
			Game1.player.changeFriendship(SpouseStoryRules.TalkFriendship, npc);
			step.Note = "听完了";
			Complete(p, step);
		}
		else if (step.Quest is StoryQuest q && q.Kind == StoryQuestKinds.Slay && SlayProgress(q) >= q.Count)
		{
			CompleteQuest(p, step, npc, Game1.player);
		}
	}

	private void Speak(NPC npc, IEnumerable<StoryLine> lines)
	{
		string text = TextTools.CleanForGame(SpouseStoryRules.DialogueText(lines));
		if (text.Length == 0)
		{
			return;
		}
		npc.faceTowardFarmerForPeriod(4000, 4, faceAway: false, Game1.player);
		if (Say != null)
		{
			Say(npc, text);
			return;
		}
		npc.CurrentDialogue.Push(new StardewValley.Dialogue(npc, null, text));
		Game1.drawDialogue(npc);
	}

	// ---------------- 见面 ----------------

	private void TryArrange(StoryPackage p, StoryStep step)
	{
		if (!_meetups.Enabled)
		{
			step.Attempts++;
			if (step.Attempts >= SpouseStoryRules.MaxAttempts)
			{
				Abandon(p, "约见关着");
			}
			return;
		}
		string reason = "";
		for (int offset = 1; offset <= MeetupRules.MaxDaysAhead; offset++)
		{
			Meetup? m = _meetups.Arrange(p.Partner, Today + offset, step.Time, step.Place, p.Title, out reason, MeetupRules.StoryOwner, p.Id + "#" + step.Index);
			if (m != null)
			{
				step.MeetupId = m.Id;
				step.Attempts++;
				SendInvite(p, step, m);
				MarkDirty();
				return;
			}
		}
		step.Attempts++;
		_log.Info($"伴侣剧情 {p.Id}：还约不上（{reason}），第 {step.Attempts}/{SpouseStoryRules.MaxAttempts} 次。");
		if (step.Attempts >= SpouseStoryRules.MaxAttempts)
		{
			Abandon(p, "约不上：" + MeetupRules.Explain(reason));
		}
	}

	private void SendInvite(StoryPackage p, StoryStep step, Meetup m)
	{
		string place = MeetupPlaces.Label(m.Place, Display(p.Partner));
		string tail = Tr.Get("story.inviteTail", "{{when}} {{time}}，{{place}}见。", ("when", MeetupService.WhenText(m.Day)), ("time", Clock.Text(m.Time)), ("place", place));
		string invite = step.Attempts > 1 ? Tr.Get("story.inviteAgain", "上次没见成，我们改个时间吧。") : step.Invite.Length > 0 ? step.Invite : Tr.Get("story.inviteDefault", "有空吗？想和你见一面。");
		string text = TextTools.CleanForGame(invite + " " + tail);
		if (Text?.Invoke(p.Partner, text) != true)
		{
			Hud(Display(p.Partner) + "：" + text);
		}
		_log.Info($"伴侣剧情 {p.Id}：{p.Partner} 约农夫（{m.Id}，第 {m.Day} 天 {m.Time} 在 {m.Place}）：{text}");
	}

	private (StoryPackage, StoryStep)? FindByRef(string ownerRef)
	{
		string[] parts = (ownerRef ?? "").Split('#');
		if (parts.Length != 2 || !int.TryParse(parts[1], out int index) || Find(parts[0]) is not StoryPackage p || p.Steps.FirstOrDefault(s => s.Index == index) is not StoryStep step)
		{
			return null;
		}
		return (p, step);
	}

	private bool OnStoryKept(Meetup m, NPC npc)
	{
		if (FindByRef(m.OwnerRef) is not { } found || !StoryStates.IsOpen(found.Item1.State) || found.Item2.State != StepStates.Ready || found.Item2.MeetupId != m.Id)
		{
			return false;
		}
		return PlayScene(found.Item1, found.Item2, npc);
	}

	/// <summary>在你现在的地方演这一步的戏（伴侣要在同一张地图上）。</summary>
	public bool PlayScene(StoryPackage p, StoryStep step, NPC npc)
	{
		GameLocation? loc = Game1.currentLocation;
		if (loc == null || Game1.eventUp || npc.currentLocation != loc)
		{
			return false;
		}
		Point npcTile = npc.TilePoint;
		Point farmerTile = FarmerSpot(loc, npcTile, Game1.player.TilePoint);
		string farmerName = SpouseStoryRules.Clean(Game1.player.Name, 20);
		string script = SpouseStoryRules.EventScript(p.Id, step.Index, npc.Name, (npcTile.X, npcTile.Y), (farmerTile.X, farmerTile.Y), step.Lines, farmerName.Length > 0 ? farmerName : "你", Tr.Get("story.choicePrompt", "你想了想……"));
		step.Choices.Clear();
		Event evt = new Event(script, null, $"{SpouseStoryRules.Prefix}{p.Id}_{step.Index}", Game1.player) { markEventSeen = false };
		loc.startEvent(evt);
		_scene = (p.Id, step.Index, Game1.ticks);
		_log.Info($"伴侣剧情 {p.Id}：和 {npc.Name} 的戏在 {loc.NameOrUniqueName} {npcTile} 开演。");
		_log.Debug("伴侣剧情脚本：" + script);
		return true;
	}

	/// <summary>你站哪：伴侣左右两边能站的一格优先，再按离你近的排；都不行就站原地。</summary>
	private static Point FarmerSpot(GameLocation loc, Point npcTile, Point farmerTile)
	{
		Point[] sides = { new Point(npcTile.X - 1, npcTile.Y), new Point(npcTile.X + 1, npcTile.Y), new Point(npcTile.X, npcTile.Y + 1), new Point(npcTile.X, npcTile.Y - 1) };
		foreach (Point p in sides.OrderBy(p => p.Y == npcTile.Y ? 0 : 1).ThenBy(p => Math.Abs(p.X - farmerTile.X) + Math.Abs(p.Y - farmerTile.Y)))
		{
			if (p == farmerTile || loc.CanSpawnCharacterHere(new Vector2(p.X, p.Y)))
			{
				return p;
			}
		}
		return farmerTile;
	}

	private static void RegisterChoiceCommand()
	{
		if (_commandRegistered)
		{
			return;
		}
		_commandRegistered = true;
		Event.RegisterCommand(SpouseStoryRules.ChoiceCommand, (Event e, string[] args, EventContext context) =>
		{
			try
			{
				if (args.Length >= 5 && int.TryParse(args[2], out int step) && int.TryParse(args[3], out int point) && int.TryParse(args[4], out int option))
				{
					_choiceMade?.Invoke(args[1], step, point, option);
				}
			}
			finally
			{
				e.CurrentCommand++;
			}
		});
	}

	private void OnChoice(string packageId, int stepIndex, int point, int option)
	{
		if (Find(packageId)?.Steps.FirstOrDefault(s => s.Index == stepIndex) is not StoryStep step)
		{
			return;
		}
		while (step.Choices.Count <= point)
		{
			step.Choices.Add(-1);
		}
		step.Choices[point] = option;
		_log.Debug($"伴侣剧情 {packageId}：第 {point + 1} 个选项选了第 {option + 1} 项。");
	}

	private void FinishScene(string packageId, int stepIndex)
	{
		if (Find(packageId) is not StoryPackage p || p.Steps.FirstOrDefault(s => s.Index == stepIndex) is not StoryStep step || step.State != StepStates.Ready)
		{
			return;
		}
		NPC? npc = Game1.getCharacterFromName(p.Partner);
		(int points, List<string> said, string feeling) = SpouseStoryRules.SceneOutcome(step);
		if (npc != null)
		{
			Game1.player.changeFriendship(points, npc);
		}
		string place = MeetupPlaces.Label(step.Place, Display(p.Partner));
		Remember(p.Partner, SpouseStoryRules.SceneMemory(p, place, said, feeling));
		step.Note = $"在{place}见了面" + (said.Count > 0 ? "，农夫说了" + string.Join("、", said) : "") + $"（好感 {points:+0;-0}）";
		Complete(p, step);
	}

	private void OnMeetupClosed(Meetup m)
	{
		if (m.Owner != MeetupRules.StoryOwner || FindByRef(m.OwnerRef) is not { } found || !StoryStates.IsOpen(found.Item1.State) || found.Item2.State != StepStates.Ready || found.Item2.MeetupId != m.Id)
		{
			return;
		}
		(StoryPackage p, StoryStep step) = found;
		string place = MeetupService.PlaceLabel(m);
		if (m.State == MeetupStates.Missed)
		{
			Fail(p, step, "农夫没来", SpouseStoryRules.MissedMemory(p, place));
			return;
		}
		step.MeetupId = "";
		_log.Info($"伴侣剧情 {p.Id}：{place} 的见面作罢（{m.Reason}），明天再约。");
		if (step.Attempts >= SpouseStoryRules.MaxAttempts)
		{
			Abandon(p, "见面一再泡汤");
		}
		MarkDirty();
	}

	// ---------------- 候选和资料 ----------------

	public StoryCandidates BuildCandidates(string partner, Random rng, bool full = false)
	{
		StoryCandidates c = new StoryCandidates();
		string season = Game1.currentSeason ?? "spring";
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void AddDeliver(string id, string note)
		{
			string? q = Ecosystem.Qualify(id);
			if (q == null || !seen.Add(q))
			{
				return;
			}
			string? name = ItemRegistry.GetData(q)?.DisplayName;
			if (!string.IsNullOrWhiteSpace(name))
			{
				c.Deliver.Add(new StoryCandidate(q, TextTools.CleanForGame(name), note));
			}
		}
		foreach ((string taste, string note) in new[] { ("loved", "你最爱的"), ("liked", "你喜欢的") })
		{
			foreach (ItemTemplate t in _eco.PersonalTastes(partner, taste).Where(t => _eco.IsEnabled(t) && t.Price > 0 && t.Price <= 300 && SpouseStoryRules.LikedCategories.Contains(t.Category)).OrderBy(_ => rng.Next()).Take(full ? 99 : 4))
			{
				AddDeliver(t.QualifiedId, note);
			}
		}
		foreach (CropInfo crop in _market.MarketCrops().Where(x => x.Seasons.Contains(season) && !CommissionRules.NotTownGrown.Contains(x.HarvestItemId) && CommissionRules.RealisticCrop(Game1.year, Game1.dayOfMonth, x.IsFruitTree, x.DaysToGrow)).OrderBy(_ => rng.Next()).Take(full ? 99 : 5))
		{
			AddDeliver(crop.HarvestItemId, "农场这季能收");
		}
		if (CommissionRules.Forage.TryGetValue(season, out string[]? forage))
		{
			foreach (string id in forage.OrderBy(_ => rng.Next()).Take(full ? 99 : 3))
			{
				AddDeliver(id, "这季野外能采到");
			}
		}
		try
		{
			foreach (KeyValuePair<string, string> pair in DataLoader.Fish(Game1.content).OrderBy(_ => rng.Next()))
			{
				if (c.Fish.Count >= (full ? 99 : 6))
				{
					break;
				}
				string[] f = pair.Value.Split('/');
				if (f.Length < 7 || f[1] == "trap" || SpouseStoryRules.SpecialFish.Contains(pair.Key) || !int.TryParse(pair.Key, out _) || !int.TryParse(f[1], out int difficulty) || difficulty > 70 || !f[6].Split(' ').Contains(season))
				{
					continue;
				}
				string? name = ItemRegistry.GetData("(O)" + pair.Key)?.DisplayName;
				if (!string.IsNullOrWhiteSpace(name))
				{
					c.Fish.Add(new StoryCandidate("(O)" + pair.Key, TextTools.CleanForGame(name), difficulty >= 50 ? "有点难钓" : "好钓"));
				}
			}
		}
		catch (Exception ex)
		{
			_log.Debug("伴侣剧情：读不了鱼的数据：" + ex.Message);
		}
		int depth = Game1.player?.deepestMineLevel ?? 0;
		if (depth > 0)
		{
			Dictionary<string, string>? monsters = null;
			try
			{
				monsters = DataLoader.Monsters(Game1.content);
			}
			catch (Exception)
			{
			}
			foreach ((string name, int level) in SpouseStoryRules.Monsters.Where(m => depth >= m.Level))
			{
				string display = name;
				if (monsters != null && monsters.TryGetValue(name, out string? raw) && raw.Split('/') is { Length: > 14 } f && f[14].Trim().Length > 0)
				{
					display = f[14].Trim();
				}
				c.Slay.Add(new StoryCandidate(name, TextTools.CleanForGame(display), $"矿洞第 {level} 层起"));
			}
		}
		c.Places.AddRange(MeetupPlaces.All.Where(p => p.Key != MeetupPlaces.Home));
		return c;
	}

	private StoryContext BuildContext(string partner, string theme)
	{
		Farmer player = Game1.player;
		player.friendshipData.TryGetValue(partner, out Friendship? f);
		int hearts = player.getFriendshipHeartLevelForNPC(partner);
		WorldDate? lastGift = f?.LastGiftDate;
		string weather = Game1.isRaining ? "下雨" : Game1.isSnowing ? "下雪" : "天气不错";
		GameDate today = GameState.Today;
		return new StoryContext
		{
			FarmerName = player.Name ?? "",
			FarmerMale = player.IsMale,
			Roommate = f?.IsRoommate() == true,
			DaysMarried = f?.DaysMarried,
			Hearts = hearts,
			Children = player.getChildren().Select(ch => TextTools.CleanForGame(ch.displayName)).ToList(),
			OtherPartners = Partners().Where(n => !string.Equals(n, partner, StringComparison.OrdinalIgnoreCase)).ToList(),
			Today = $"{Dates.Label(today)}（{Game1.shortDayDisplayNameFromDayOfSeason(Game1.dayOfMonth)}），{weather}",
			TodayNumber = Today,
			Activity = Log?.Activity.ToList() ?? new List<StoryActivityDay>(),
			DaysSinceGift = lastGift == null ? null : Today - lastGift.TotalDays,
			Skills = $"技能：耕种 {player.FarmingLevel}，钓鱼 {player.FishingLevel}，采集 {player.ForagingLevel}，采矿 {player.MiningLevel}，战斗 {player.CombatLevel}。矿洞最深到第 {player.deepestMineLevel} 层。",
			Feelings = FeelingsBlock?.Invoke(partner, hearts) ?? "",
			Earlier = HistoryFor(partner).Take(5).ToList(),
			Theme = theme,
			Display = Display
		};
	}

	// ---------------- 记忆和提示词 ----------------

	private void Remember(string npc, string text)
	{
		try
		{
			Feelings?.Invoke()?.AddKeyMemory(npc, text, GameState.Today);
		}
		catch (Exception ex)
		{
			_log.Debug("伴侣剧情：记忆没记上：" + ex.Message);
		}
	}

	public string PromptBlock(string npc)
	{
		if (_store() == null || !Enabled || string.IsNullOrEmpty(npc))
		{
			return "";
		}
		StoryPackage? active = ActiveFor(npc);
		string stepLine = "";
		if (active?.Current is StoryStep step)
		{
			bool read = step.MailKey.Length > 0 && Game1.player.mailReceived.Contains(step.MailKey);
			stepLine = SpouseStoryRules.StepPromptLine(step, read, step.MeetupId.Length > 0);
		}
		StoryPackage? recent = HistoryFor(npc).FirstOrDefault(x => Today - x.ClosedDay <= SpouseStoryRules.RecentDays);
		return SpouseStoryRules.PromptBlock(active, stepLine, recent);
	}

	// ---------------- 活动 ----------------

	private static Dictionary<string, long> ReadStats()
	{
		Stats s = Game1.stats;
		return new Dictionary<string, long>
		{
			["fish"] = s.FishCaught,
			["cooked"] = s.ItemsCooked,
			["crops"] = s.CropsShipped,
			["monsters"] = s.MonstersKilled,
			["foraged"] = s.ItemsForaged,
			["crafted"] = s.ItemsCrafted,
			["earned"] = (long)Game1.player.totalMoneyEarned
		};
	}

	private static void TakeSnapshot(SpouseStoryLog log)
	{
		log.Snapshot = ReadStats();
		log.TodayPlaces.Clear();
	}

	private static void RecordActivityDay(SpouseStoryLog log)
	{
		if (log.Snapshot.Count == 0)
		{
			return;
		}
		Dictionary<string, long> now = ReadStats();
		int Delta(string key) => (int)Math.Max(0, now.GetValueOrDefault(key) - log.Snapshot.GetValueOrDefault(key));
		Farmer player = Game1.player;
		StoryActivityDay day = new StoryActivityDay
		{
			Day = Today,
			Fish = Delta("fish"),
			Cooked = Delta("cooked"),
			CropsShipped = Delta("crops"),
			Monsters = Delta("monsters"),
			Foraged = Delta("foraged"),
			Crafted = Delta("crafted"),
			Earned = Delta("earned"),
			Places = log.TodayPlaces.ToList(),
			GiftsTo = player.friendshipData.Pairs.Where(kv => kv.Value != null && kv.Value.GiftsToday > 0).Select(kv => kv.Key).Take(6).ToList(),
			AwayAtNight = player.currentLocation is not FarmHouse && player.currentLocation is not IslandFarmHouse
		};
		log.Activity.RemoveAll(d => d.Day == day.Day || Today - d.Day >= SpouseStoryRules.ActivityDays);
		log.Activity.Add(day);
		log.Snapshot.Clear();
	}

	// ---------------- 界面文字 ----------------

	/// <summary>伴侣页上这一步现在的样子。</summary>
	public string StepStatus(StoryPackage p, StoryStep step)
	{
		string name = Display(p.Partner);
		switch (step.Kind)
		{
			case StepKinds.Letter:
				return step.Delivered
					? Tr.Get("story.status.letterWaiting", "信箱里有{{npc}}的信，还没读。", ("npc", name))
					: Tr.Get("story.status.letterComing", "{{npc}}在给你写信，明天早上会到。", ("npc", name));
			case StepKinds.Quest when step.Quest is StoryQuest q:
			{
				int left = Math.Max(0, q.LastDay - Today + 1);
				if (q.Kind == StoryQuestKinds.Slay)
				{
					return Tr.Get("story.status.slay", "在矿洞里打倒{{target}}：{{have}}/{{count}}，还剩 {{days}} 天。", ("target", q.TargetName), ("have", Math.Min(q.Count, SlayProgress(q))), ("count", q.Count), ("days", left));
				}
				return Tr.Get("story.status.deliver", "把 {{count}} 个{{target}}交给{{npc}}（背包里有 {{have}} 个），还剩 {{days}} 天。", ("count", q.Count), ("target", q.TargetName), ("npc", name), ("have", CommissionService.CountInInventory(Game1.player, q.Target)), ("days", left));
			}
			case StepKinds.Event:
			{
				Meetup? m = step.MeetupId.Length > 0 ? _meetups.Find(step.MeetupId) : null;
				return m != null && MeetupStates.IsOpen(m.State)
					? Tr.Get("story.status.meet", "约好了：{{when}} {{time}}，{{place}}。", ("when", MeetupService.WhenText(m.Day)), ("time", Clock.Text(m.Time)), ("place", MeetupPlaces.Label(m.Place, name)))
					: Tr.Get("story.status.meetPending", "{{npc}}想约你见面，在找合适的时间。", ("npc", name));
			}
			case StepKinds.Talk:
				return Tr.Get("story.status.talk", "{{npc}}有话想跟你说（找{{npc}}说话时选“聊聊{{topic}}”）。", ("npc", name), ("topic", step.Topic));
			default:
				return "";
		}
	}

	/// <summary>今天这一步约见的时间（今日页排序用）；没有为空。</summary>
	public Meetup? MeetupOf(StoryStep step) => step.MeetupId.Length > 0 ? _meetups.Find(step.MeetupId) : null;

	private static void Hud(string text) => Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(text), HUDMessage.newQuest_type));

	private void MarkDirty() => _store()?.MarkDirty(SpouseStoryLog.Path);

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write, string debugDir)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		int? Comp(int i) => args.Length > i && int.TryParse(args[i], out int c) ? Math.Clamp(c - 1, 0, SpouseStoryRules.Compositions.Count - 1) : null;
		string Partner(int i) => Partners().FirstOrDefault(n => string.Equals(n, args[i], StringComparison.OrdinalIgnoreCase)) ?? args[i];
		switch (sub)
		{
			case "marry" when args.Length > 1:
				return Marry(args[1], out string why) ? "成了伴侣：" + string.Join("、", Partners()) : why;
			case "start" when args.Length > 1:
				return StartStory(Partner(1), Comp(2), out string reason) ? "开始写了" : reason;
			case "inject" when args.Length > 3:
			{
				string partner = Partner(1);
				string path = Path.IsPathRooted(args[3]) ? args[3] : Path.Combine(debugDir, args[3]);
				StoryPackage? pkg = SpouseStoryRules.Parse(File.ReadAllText(path), Comp(2) ?? 0, BuildCandidates(partner, new Random(1), full: true), CommissionService.BasePrice, out string error);
				StoryPackage? accepted = pkg != null && ActiveFor(partner) == null ? Accept(pkg, partner, Theme(partner)) : null;
				return write("story-inject.json", new { ok = accepted != null, error, id = accepted?.Id, pkg });
			}
			case "prompt" when args.Length > 1:
			{
				string partner = Partner(1);
				int comp = Comp(2) ?? 0;
				return write("story-prompt.txt", Brief(partner, comp, BuildCandidates(partner, new Random(1)), Theme(partner)));
			}
			case "theme" when args.Length > 1:
				SetTheme(Partner(1), string.Join(" ", args.Skip(2)));
				return "心愿：" + Theme(Partner(1));
			case "deliver" when args.Length > 1:
			{
				if (Find(args[1]) is not StoryPackage p || p.Current is not StoryStep step || step.Kind != StepKinds.Letter)
				{
					return "这一步不是信";
				}
				DeliverLetter(p, step);
				MarkDirty();
				return "信放进信箱了：" + step.MailKey;
			}
			case "advance" when args.Length > 1:
			{
				if (Find(args[1]) is not StoryPackage p || p.Current is not StoryStep step)
				{
					return "没有进行中的这一段";
				}
				if (step.Quest != null && FindQuest(step.Quest) is Quest quest)
				{
					Game1.player.questLog.Remove(quest);
				}
				string meetup = step.MeetupId;
				step.Note = "调试跳过";
				Complete(p, step);
				if (meetup.Length > 0)
				{
					_meetups.CancelForOwner(meetup, "story step skipped");
				}
				return "跳到下一步";
			}
			case "scene" when args.Length > 1:
			{
				StoryPackage? p = Find(args[1]);
				StoryStep? step = p?.Current;
				NPC? npc = p == null ? null : Game1.getCharacterFromName(p.Partner);
				bool ok = p != null && step != null && npc != null && step.Kind == StepKinds.Event && PlayScene(p, step, npc);
				return ok ? "开演" : $"演不了（这一步 {step?.Kind}，伴侣在 {npc?.currentLocation?.NameOrUniqueName}，你在 {Game1.currentLocation?.NameOrUniqueName}）";
			}
			case "due":
				if (Log is SpouseStoryLog dueLog)
				{
					dueLog.NextStoryDay = args.Length > 1 && int.TryParse(args[1], out int day) ? day : Today;
					MarkDirty();
				}
				return "下一段在第 " + NextStoryDay + " 天";
			case "clear":
				if (Log is SpouseStoryLog log)
				{
					foreach (StoryPackage p in log.Packages.Where(p => StoryStates.IsOpen(p.State)).ToList())
					{
						p.State = StoryStates.Abandoned;
						RemoveQuests(p);
						ReleaseMeetups(p);
					}
					log.Packages.Clear();
					log.NextStoryDay = 0;
					MarkDirty();
				}
				Version++;
				return "清空了";
			default:
				return write("spouse-stories.json", new
				{
					enabled = Enabled,
					today = Today,
					partners = Partners(),
					generating = _generating.ToList(),
					prompts = Partners().ToDictionary(n => n, PromptBlock),
					meetups = _meetups.All().Where(m => m.Owner == MeetupRules.StoryOwner),
					quests = Game1.player.questLog.Where(q => q.id.Value?.Contains("_Story_") == true).Select(q => new { id = q.id.Value, title = q.questTitle, objective = q.currentObjective }),
					lastError = LastError,
					lastRaw = LastRaw,
					state = Log
				});
		}
	}

	/// <summary>测试用：直接把这位设成你的伴侣（初始小屋先扩建，不然伴侣没地方站）。</summary>
	private static bool Marry(string npcName, out string reason)
	{
		reason = "";
		if (Game1.getCharacterFromName(npcName) is not NPC npc)
		{
			reason = "没有这位村民";
			return false;
		}
		Farmer player = Game1.player;
		if (!player.friendshipData.TryGetValue(npc.Name, out Friendship? f))
		{
			f = new Friendship();
			player.friendshipData[npc.Name] = f;
		}
		f.Points = Math.Max(f.Points, 2500);
		f.Status = FriendshipStatus.Married;
		f.WeddingDate = WorldDate.ForDaysPlayed(Math.Max(0, Game1.Date.TotalDays - 10));
		if (string.IsNullOrWhiteSpace(player.spouse))
		{
			player.spouse = npc.Name;
		}
		if (player.HouseUpgradeLevel < 1 && Utility.getHomeOfFarmer(player) is FarmHouse house)
		{
			player.houseUpgradeLevel.Value = 1;
			house.setMapForUpgradeLevel(1);
		}
		return true;
	}
}
