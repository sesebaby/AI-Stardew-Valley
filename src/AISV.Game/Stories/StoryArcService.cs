using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Social.Gossip;
using AISV.Core.Social.Web;
using AISV.Core.Storage;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;

namespace AISV.Game.Stories;

/// <summary>小剧场在游戏里的那一半：每周检测、对话里推进、收尾写结局、逐出小镇生效、商店脱离。</summary>
public sealed class StoryArcService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly SocialSystem _social;

	private readonly ConversationLog _conversations;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly StoryArcAi _ai;

	private readonly ILog _log;

	private readonly string _modDir;

	private readonly HashSet<string> _concluding = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public StoryArcService(Func<ModConfig> config, Func<DocumentStore?> store, string modDir, SocialSystem social, ConversationLog conversations, Relationships relationships, NpcDirectory npcs, StoryArcAi ai, ILog log)
	{
		_config = config;
		_store = store;
		_social = social;
		_conversations = conversations;
		_relationships = relationships;
		_npcs = npcs;
		_ai = ai;
		_log = log;
		_modDir = modDir;
		Arcs = new StoryArcBook(store);
		Shops = new ShopDetachBook(store);
		Expulsions = new ExpulsionBook(store, Shops);
		Lore = new ResolvedLore(store, npcs.IsEnabled, npcs.Eligibility.IsModAllowed) { Expulsions = () => Expulsions.PromptBlock(Display) };
		Staging = new ConclusionStaging(Path.Combine(modDir, "assets", "story_arc_conclusion_stages"), Path.Combine(modDir, "assets", "story_arc_conclusion_location_affinities"), ToInternal);
	}

	public StoryArcBook Arcs { get; }

	public ResolvedLore Lore { get; }

	public ExpulsionBook Expulsions { get; }

	public ShopDetachBook Shops { get; }

	public ConclusionStaging Staging { get; }

	/// <summary>全村对每个人的敌意排名（社交剧情接上后提供）。</summary>
	public Func<IReadOnlyList<(string Npc, float Hostility)>>? Hostility { get; set; }

	/// <summary>镇上的钱事（账本给的），检测小剧场时一起看。</summary>
	public Func<string>? Money { get; set; }

	public bool IsConcluding(string arcId) => _concluding.Contains(arcId);

	public bool Usable(StoryArc arc)
	{
		List<string> npcs = arc.Cast.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return npcs.Count > 0 && npcs.All(_npcs.IsEnabled) && arc.Sources.All(_npcs.Eligibility.IsModAllowed);
	}

	// ---- 生命周期 ----

	public void OnSaveLoaded()
	{
		if (_store() == null)
		{
			return;
		}
		Arcs.Normalize(ToInternal, _log.Info);
		DetachMarriedShopOwners();
		Enforce();
		_log.Debug($"小剧场：{Arcs.All.Count} 出，已定传说 {Lore.All.Count} 条，离开小镇的 {Expulsions.ExpelledNames.Count} 人。");
	}

	public void OnDayStarted()
	{
		if (_store() == null || Game1.player == null)
		{
			return;
		}
		ModConfig c = _config();
		GameDate today = GameState.Today;
		foreach (StoryArc arc in Arcs.Fizzle(today, c.StorylineStaleSeasons, Usable))
		{
			_log.Info($"小剧场「{arc.Title}」好几季没有进展，不了了之。");
		}
		Enforce();
		if (c.AiOn && Context.IsMainPlayer && StoryArcBook.ShouldAnalyze(today.Day))
		{
			Analyze("每周");
		}
	}

	// ---- 检测 ----

	/// <summary>看看镇上有没有新的小剧场（排在八卦整理之后）。</summary>
	public string Analyze(string why)
	{
		ModConfig c = _config();
		int max = Math.Clamp(c.StorylinesAtOnce, 1, 5);
		int active = Arcs.ActiveCount(Usable);
		if (active >= max)
		{
			_log.Info($"小剧场已经有 {active}/{max} 出在进行，这次不检测。");
			return "进行中的小剧场已经够多";
		}
		GameDate today = GameState.Today;
		List<GossipEntry> feed = _social.Gossip.FeedForAi();
		List<GossipEntry> recent = feed.Take(10).Reverse().ToList();
		StringBuilder gossip = new StringBuilder();
		foreach (GossipEntry e in recent)
		{
			gossip.AppendLine($"[{e.Season} {e.Day}, 第 {e.Year} 年] {e.Fact}");
		}
		string web = WebBlock(today.TotalDays);
		if (web.Length > 0)
		{
			gossip.AppendLine().AppendLine(web);
		}
		string money = Money?.Invoke() ?? "";
		if (money.Length > 0)
		{
			gossip.AppendLine().AppendLine(money);
		}
		List<string> sources = recent.SelectMany(e => e.FromMods ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
		List<StoryArc> current = Arcs.All.Where(a => a.State == StoryArc.Active && Usable(a)).ToList();
		string existing = current.Count == 0 ? "" : "\n进行中的小剧场：\n" + string.Join("\n", current.Select(a => $"- {a.Title}（id：{a.Id}）：{a.Stage}"));
		List<string> farmers = Game1.getAllFarmers().Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
		string players = farmers.Count > 1 ? string.Join(", ", farmers) : Game1.player?.Name ?? "Farmer";
		string guidance = ExpulsionGuidance();
		ArcDetectionInput input = new ArcDetectionInput(gossip.ToString(), Conversations(), existing, today.Season, today.Year, players, guidance);
		_log.Info($"小剧场检测（{why}）：{feed.Count} 条八卦{(guidance.Length > 0 ? "，这次带了逐出的提示" : "")}。");
		DocumentStore? store = _store();
		_social.Enqueue("小剧场检测", async ct =>
		{
			List<StoryArc>? arcs = await _ai.DetectAsync(input, ToInternal, ct).ConfigureAwait(false);
			return () =>
			{
				if (!ReferenceEquals(store, _store()))
				{
					return;
				}
				if (arcs == null)
				{
					_log.Info("小剧场检测没成功（AI 没回或回复不对）。");
					return;
				}
				if (guidance.Length > 0 && !arcs.Any(a => a.IsExpulsion || a.IsReturn))
				{
					_log.Info("这次给了逐出的提示，AI 没有写逐出或回归的故事线。");
				}
				StoryArc? created = Arcs.Merge(arcs, GameState.Today, Math.Clamp(_config().StorylinesAtOnce, 1, 5), sources, Usable, _log.Debug);
				_log.Info(created != null ? $"新的小剧场：「{created.Title}」（{string.Join("、", created.Cast)}）。" : arcs.Count == 0 ? "镇上暂时没有成形的小剧场。" : "小剧场检测只更新了进行中的阶段。");
			};
		});
		return "已经开始检测";
	}

	private string WebBlock(int today)
	{
		List<NpcSocialMoment> moments = _social.Web.ArcCandidates(today, _npcs.IsEnabled);
		if (moments.Count == 0)
		{
			return "";
		}
		return "[值得留意的村民来往]\n" + string.Join("\n", moments.Select(m => $"- {m.First} + {m.Second}（{m.Place}，{m.Season} {m.Day}）：{m.WhatHappened} 标签：{string.Join(", ", m.Labels.Take(5))}"));
	}

	/// <summary>和农夫说话最多的 10 位村民，各带最近 3 次对话。</summary>
	private string Conversations()
	{
		StringBuilder sb = new StringBuilder();
		var top = _conversations.Tracked().Select(n => (Npc: n, Count: _conversations.Count(n))).Where(x => x.Count > 0 && _npcs.IsEnabled(x.Npc))
			.OrderByDescending(x => x.Count).ThenBy(x => x.Npc, StringComparer.OrdinalIgnoreCase).Take(10);
		foreach ((string npc, int count) in top)
		{
			sb.AppendLine($"- {npc}：聊过 {count} 次，{_relationships.EffectiveHearts(npc)} 心");
			foreach (ConversationEntry e in _conversations.Recent(npc, 3))
			{
				string said = Clean(SkippedReply.ForPrompt(e.FarmerReply), 160);
				string reply = Clean(e.Comeback, 180);
				if (said.Length == 0 && reply.Length == 0)
				{
					continue;
				}
				sb.Append("  最近");
				if (!string.IsNullOrWhiteSpace(e.Season) && e.Day > 0)
				{
					sb.Append($"（{e.Season} {e.Day}）");
				}
				sb.Append("：");
				if (said.Length > 0)
				{
					sb.Append($"{(string.IsNullOrWhiteSpace(e.Farmer) ? "农夫" : e.Farmer)}说\"{said}\"");
				}
				if (reply.Length > 0)
				{
					sb.Append(said.Length > 0 ? "；" : "").Append($"{npc}回\"{reply}\"");
				}
				if (!string.IsNullOrWhiteSpace(e.Face))
				{
					sb.Append($"（{e.Face}）");
				}
				sb.AppendLine();
			}
		}
		return sb.ToString();
	}

	private static string Clean(string? text, int max) => PromptText.Sanitize(text, max).Replace('\r', ' ').Replace('\n', ' ').Trim();

	/// <summary>逐出的提示：掷中了才给，说明可以写逐出或回归、谁绝对不行、谁是候选。</summary>
	private string ExpulsionGuidance()
	{
		ModConfig c = _config();
		if (!c.DepartureStoriesOn)
		{
			return "";
		}
		static bool Drawn(int percent) => Game1.random.Next(100) < Math.Clamp(percent, 0, 100);
		List<ExpulsionRecord> gone = Expulsions.All.Where(r => r.Phase == ExpulsionRecord.Expelled).ToList();
		bool expel = Drawn(c.DepartureHintPercent);
		bool back = c.ReturnStoriesOn && gone.Count > 0 && Drawn(c.ReturnHintPercent);
		if (!expel && !back)
		{
			return "";
		}
		IReadOnlyCollection<string> protectedNames = ExpulsionBook.ProtectedNames(Game1.player?.spouse);
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【本周特别许可：有人离开小镇，或者有人回来】");
		sb.AppendLine("这一次已经抽中了，可以放心写，不用再掂量该不该。");
		sb.AppendLine("这种故事线要在对象里多填两个字段，别的照常：");
		sb.AppendLine("  \"shape\": \"departure\"（有人要走）或 \"return\"（有人回来）,");
		sb.AppendLine("  \"mover\": { \"who\": \"名字\", \"how\": \"arrested|exiled|left_village\", \"why\": \"一句话的起因\" }");
		sb.AppendLine("故事的核心是谁走谁留，就一定填上；不填会被当成普通故事线，最后谁也不会走。");
		if (expel)
		{
			float threshold = Math.Clamp(c.DepartureGrudgePercent, 0, 100) / 100f;
			IReadOnlyList<(string Npc, float Hostility)> ranking = Hostility?.Invoke() ?? Array.Empty<(string, float)>();
			List<(string Npc, float Hostility)> candidates = ranking.Where(r => r.Hostility >= threshold && !protectedNames.Contains(r.Npc, StringComparer.OrdinalIgnoreCase)
				&& !Expulsions.IsExpelled(r.Npc) && _npcs.IsEnabled(r.Npc)).Take(3).ToList();
			_log.Info($"逐出候选：{(candidates.Count == 0 ? "没有" : string.Join("、", candidates.Select(x => $"{x.Npc}（{x.Hostility:P0}）")))}；阈值 {threshold:P0}。");
			sb.AppendLine();
			sb.AppendLine("▶ 有人要走（departure）");
			sb.AppendLine("什么时候写：资料里能找到站得住的由头。比如账本对不上、偷偷做的事被人撞见、和半个镇子结了怨、一桩丑闻越传越凶。由头小一点也行，故事里会越滚越大。只要有像样的人选，这次就先写这一种。");
			sb.AppendLine("怎么演：几个目标一步步把事情推到台面上，有人站出来指认，当事人辩白，镇上的人聚起来商量。真正离开放在结局，目标进行时人还在镇上，可以写进 cast。");
			sb.AppendLine($"谁不能碰：{string.Join("、", protectedNames)}，无论如何都不能是要走的人。");
			sb.AppendLine(candidates.Count > 0
				? "眼下镇上积怨最深的几位（可以直接考虑）：" + string.Join("、", candidates.Select(x => $"{x.Npc}（{x.Hostility:P0}）")) + "。"
				: "眼下看不出大家集中对谁不满，人选和由头就从八卦和对话里挑。");
		}
		if (back)
		{
			sb.AppendLine();
			sb.AppendLine("▶ 有人回来（return，偶尔写一次就好）");
			sb.AppendLine("以前离开的人：");
			foreach (ExpulsionRecord r in gone)
			{
				sb.AppendLine($"- {r.Who}：{(!string.IsNullOrWhiteSpace(r.News) ? r.News : r.How + "：" + r.Why)}");
			}
			sb.AppendLine("这出戏的主角是留在镇上的人：他们去打听、争论、表态，最后决定让不让他回来。离开的那位眼下不在镇上，不能写进 cast，也不能当 person。");
		}
		return sb.ToString();
	}

	// ---- 对话 ----

	public string PromptBlock(string npc) => _npcs.IsEnabled(npc) ? Arcs.PromptBlock(npc, Display, Usable) : "";

	public string LoreBlock(string npc) => Lore.PromptBlock(npc, Display);

	public bool IsStorySingle(string npc) => Lore.IsStorySingle(npc);

	/// <summary>结局后几天里第一次见面时说的话（开场白直接用它）。</summary>
	/// <summary>开场白在后台线程生成，这里只读，记"说过了"排回主线程。</summary>
	public string? PostResolutionLine(string npc)
	{
		if (!_npcs.IsEnabled(npc) || Arcs.PeekPostResolutionLine(npc, GameState.Today.TotalDays, _config().AfterwordDays, Usable) is not { } hit)
		{
			return null;
		}
		MainThread.Run(() => Arcs.ConsumePostResolutionLine(hit.ArcId, npc));
		return hit.Line;
	}

	/// <summary>回复里说某个目标办到了。</summary>
	public void OnReply(NPC npc, NpcReaction reaction)
	{
		if (string.IsNullOrEmpty(reaction.ArcObjectiveCompleted) || !_npcs.IsEnabled(npc.Name))
		{
			return;
		}
		string by = Game1.player?.Name ?? "Farmer";
		StoryArc? arc = Arcs.CompleteObjective(reaction.ArcObjectiveCompleted, reaction.ArcObjectiveApproach, by, GameState.Today, Usable, out ArcObjective? o, out bool ready);
		if (arc == null || o == null)
		{
			_log.Debug($"小剧场目标 {reaction.ArcObjectiveCompleted} 在进行中的小剧场里找不到。");
			return;
		}
		_log.Info($"小剧场「{arc.Title}」的目标 {o.Id} 由 {by} 完成。");
		Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.objectiveDone", "小剧场目标完成：{{desc}}", ("desc", o.Text)), HUDMessage.achievement_type));
		if (ready)
		{
			_log.Info($"小剧场「{arc.Title}」可以收尾了。");
			Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.ready", "「{{title}}」可以收尾了，去 AI 外壳的社交 > 小剧场看看。", ("title", arc.Title)), HUDMessage.newQuest_type));
		}
	}

	// ---- 收尾 ----

	/// <summary>写结局（后台发请求，回主线程写回）。<paramref name="done"/> 在主线程上收到结果（失败时 null）。返回为什么没开始（null = 开始了）。</summary>
	public string? Conclude(string arcId, Action<StoryArc?> done)
	{
		StoryArc? arc = Arcs.Get(arcId);
		if (arc == null || arc.State != StoryArc.Completable)
		{
			return Tr.Get("arcs.notReady", "这出小剧场还不能收尾。");
		}
		if (!Usable(arc))
		{
			return Tr.Get("arcs.unusable", "这出小剧场里有村民关掉了 AI，没法收尾。");
		}
		if (!_concluding.Add(arc.Id))
		{
			return Tr.Get("arcs.busy", "结局正在写，写好了会告诉你。");
		}
		StoryArcBook.EnsureChoices(arc);
		arc.StagePlace = Staging.SceneFor(arc, GameState.Today.TotalDays, Game1.player?.Name ?? "");
		ArcConclusionInput input = ConclusionInput(arc);
		DocumentStore? store = _store();
		Task.Run(async () =>
		{
			ArcConclusion? c = null;
			try
			{
				using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(90, _config().NightlyWaitSeconds * 2 + 30)));
				c = await _ai.ConcludeAsync(input, ToInternal, cts.Token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_log.Warn($"小剧场「{arc.Title}」的结局没写成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				_concluding.Remove(arc.Id);
				StoryArc? result = null;
				if (ReferenceEquals(store, _store()) && c != null && Arcs.Get(arc.Id) is { State: StoryArc.Completable })
				{
					try
					{
						result = ApplyConclusion(arc, c);
					}
					catch (Exception ex)
					{
						_log.Warn($"小剧场「{arc.Title}」的结局写不回去：{ex.Message}");
					}
				}
				done(result);
			});
		});
		return null;
	}

	private ArcConclusionInput ConclusionInput(StoryArc arc)
	{
		int today = GameState.Today.TotalDays;
		StringBuilder personalities = new StringBuilder();
		StringBuilder feelings = new StringBuilder();
		foreach (string npc in arc.Cast)
		{
			string p = _npcs.Persona(npc).Personality;
			if (!string.IsNullOrWhiteSpace(p))
			{
				personalities.AppendLine($"- {npc}：{p}");
			}
			string f = _social.Feelings.PromptBlock(npc, Display(npc), _relationships.EffectiveHearts(npc), today);
			feelings.AppendLine(string.IsNullOrWhiteSpace(f) ? $"- {npc}：还谈不上有什么看法。" : $"[{npc}]{f}");
		}
		PlayerProfile? sheet = _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		StringBuilder profile = new StringBuilder();
		if (sheet != null && sheet.HasContent)
		{
			profile.AppendLine("关于农夫：");
			AppendSheet(profile, "- 身世：", "CHARACTER_SHEET_WHO_AM_I", sheet.Intro);
			AppendSheet(profile, "- 为什么搬来：", "CHARACTER_SHEET_WHY_MOVED", sheet.CameBecause);
			AppendSheet(profile, "- 其他：", "CHARACTER_SHEET_EXTRA_INFO", sheet.Notes);
			AppendSheet(profile, "- 外貌：", "CHARACTER_SHEET_APPEARANCE", sheet.QuickFacts);
		}
		List<ArcObjective> done = arc.Goals.Where(o => o.Done).ToList();
		if (done.Count > 0)
		{
			profile.AppendLine().AppendLine("各个目标是谁办到的：");
			foreach (ArcObjective o in done)
			{
				profile.AppendLine($"- {o.Text} → {(string.IsNullOrWhiteSpace(o.DoneBy) ? "农夫" : o.DoneBy)}");
			}
		}
		return new ArcConclusionInput(arc, Game1.player?.Name ?? "Farmer", personalities.ToString(), feelings.ToString(), profile.ToString(), string.Join(", ", BuiltInPersonas.VanillaNames));
	}

	private static void AppendSheet(StringBuilder sb, string label, string tag, string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			sb.AppendLine(label).AppendLine(PromptText.Wrap(tag, value, 800));
		}
	}

	private StoryArc ApplyConclusion(StoryArc arc, ArcConclusion c)
	{
		GameDate today = GameState.Today;
		if (arc.IsExpulsion)
		{
			arc.LeavingHow = c.OutcomeKind ?? arc.LeavingHow;
			arc.LeavingNews = c.Headline ?? arc.LeavingNews;
		}
		else if (arc.IsReturn)
		{
			arc.LeavingNews = c.Headline ?? arc.LeavingNews;
		}
		arc.Tags = c.Tags;
		(ConclusionStage stage, LocationPlan plan) = Staging.Choose(arc, c.Lines, today.TotalDays, Game1.player?.Name ?? "");
		if (plan.Source == "generic_fallback")
		{
			_log.Warn($"小剧场「{arc.Title}」的结局场景找不到合适的地点，用了通用的（参与者：{string.Join(", ", plan.Participants)}；试过：{string.Join(", ", plan.Attempted)}）。");
		}
		Arcs.Resolve(arc, c, stage, plan.Preferred, today);
		if (arc.IsExpulsion)
		{
			string? why = Expulsions.Expel(arc, today.TotalDays, Game1.player?.spouse, IsShopOwner);
			_log.Info(why == null ? $"{arc.LeavingNpc} 离开了小镇（{ExpulsionBook.NormalizeKind(arc.LeavingHow)}），明天早上起不在了。" : $"逐出没生效：{why}。");
		}
		else if (arc.IsReturn)
		{
			string? why = Expulsions.Return(arc, today.TotalDays);
			if (why == null)
			{
				Show(arc.LeavingNpc!);
			}
			_log.Info(why == null ? $"{arc.LeavingNpc} 回到了小镇。" : $"回归没生效：{why}。");
		}
		Lore.Add(arc, GameDateText.Of(today));
		string summary = c.Summary;
		string withConsequence = string.IsNullOrWhiteSpace(arc.Outcome) ? summary : summary + " 后来：" + arc.Outcome;
		if (!string.IsNullOrEmpty(summary) && arc.Sources.Count == 0)
		{
			foreach (string npc in arc.Cast)
			{
				_social.Feelings.GetOrCreate(npc).StoriesLived.Add(withConsequence);
				_social.Feelings.AddKeyMemory(npc, withConsequence, today);
				_social.Feelings.GetOrCreate(npc).LatestMoment = summary;
			}
		}
		_social.Gossip.AddResolution(string.IsNullOrWhiteSpace(arc.Outcome) ? summary : summary + " " + arc.Outcome, arc.Cast, arc.Sources, today);
		RequestLetter(arc);
		_social.SaveBackgroundResults();
		_log.Info($"小剧场「{arc.Title}」收尾了：{summary}");
		return arc;
	}

	/// <summary>和农夫最亲的涉及村民写一封信说说这件事。</summary>
	private void RequestLetter(StoryArc arc)
	{
		string? writer = arc.Cast.Where(n => !string.IsNullOrWhiteSpace(n) && _npcs.IsEnabled(n)).OrderByDescending(Relationships.FriendshipPoints).FirstOrDefault();
		if (writer == null)
		{
			return;
		}
		List<string> others = arc.Cast.Where(n => !string.Equals(n, writer, StringComparison.OrdinalIgnoreCase)).Select(Display).ToList();
		string summary = string.IsNullOrWhiteSpace(arc.Outcome) ? arc.Closure ?? "" : arc.Closure + " " + arc.Outcome;
		if (summary.Length > 400)
		{
			summary = summary[..400];
		}
		_social.RequestEventLetter(writer, "arc_resolved_" + arc.Id, $"你{(others.Count > 0 ? "和" + string.Join("、", others) : "")}一起经历的那件事「{arc.Title}」刚刚落幕：{summary} 写信告诉农夫这件事对你意味着什么、现在大家怎么样了。");
	}

	public bool SelectChoice(string arcId, string choiceId) => Arcs.SelectChoice(arcId, choiceId);

	public bool Delete(string arcId)
	{
		StoryArc? arc = Arcs.Get(arcId);
		bool ok = !IsConcluding(arcId) && Arcs.Delete(arcId);
		if (ok)
		{
			_log.Info($"玩家删掉了小剧场「{arc?.Title}」。");
		}
		return ok;
	}

	// ---- 逐出、商店脱离 ----

	/// <summary>离开小镇的人从地图上拿掉（配偶例外）。</summary>
	public void Enforce()
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		foreach (string npc in Expulsions.ExpelledNames)
		{
			try
			{
				NPC? n = Game1.getCharacterFromName(npc);
				if (n == null)
				{
					continue;
				}
				if (string.Equals(Game1.player?.spouse, npc, StringComparison.OrdinalIgnoreCase))
				{
					_log.Warn($"{npc} 是农夫的配偶，不从地图上拿掉（记录保留）。");
					continue;
				}
				n.IsInvisible = true;
				n.currentLocation?.characters.Remove(n);
			}
			catch (Exception ex)
			{
				_log.Warn($"没能把 {npc} 从地图上拿掉：{ex.Message}");
			}
		}
	}

	private void Show(string npc)
	{
		try
		{
			NPC? n = Game1.getCharacterFromName(npc);
			if (n != null)
			{
				n.IsInvisible = false;
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"没能让 {npc} 重新出现：{ex.Message}");
		}
	}

	/// <summary>嫁到农场的店主：店改成脱离（不在店里也能开）。</summary>
	private void DetachMarriedShopOwners()
	{
		HashSet<string> owners = ShopOwners();
		foreach (Farmer f in Game1.getAllFarmers())
		{
			string? spouse = f?.spouse;
			if (!string.IsNullOrWhiteSpace(spouse) && owners.Contains(spouse) && !Shops.IsDetached(spouse) && Shops.Set(spouse, true))
			{
				_log.Info($"{spouse} 嫁到了农场，店改成店主不在也能开。");
			}
		}
	}

	public static bool IsShopOwner(string npc) => !string.IsNullOrWhiteSpace(npc) && ShopOwners().Contains(npc.Trim());

	private static HashSet<string> ShopOwners()
	{
		HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (ShopData shop in Game1.content.Load<Dictionary<string, ShopData>>("Data/Shops").Values)
			{
				foreach (ShopOwnerData owner in shop?.Owners ?? new List<ShopOwnerData>())
				{
					if (owner != null && owner.Type == ShopOwnerType.NamedNpc && !string.IsNullOrWhiteSpace(owner.Name))
					{
						names.Add(owner.Name);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return names;
	}

	// ---- 调试 ----

	/// <summary>aisv_arc list | analyze | complete id | conclude id | expel 村民 | return 村民</summary>
	public string Command(string[] args)
	{
		string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
		string target = args.Length > 1 ? args[1] : "";
		switch (verb)
		{
			case "analyze":
				return Analyze("调试");
			case "complete":
			{
				StoryArc? arc = Arcs.Get(target);
				if (arc == null)
				{
					return "找不到这出小剧场。";
				}
				foreach (ArcObjective o in arc.Goals.Where(o => !o.Done).ToList())
				{
					Arcs.CompleteObjective(o.Id, "调试命令", Game1.player?.Name ?? "Farmer", GameState.Today, _ => true, out _, out _);
				}
				return $"「{arc.Title}」现在是 {Arcs.Get(target)?.State}。";
			}
			case "conclude":
				return Conclude(target, a => _log.Info(a == null ? "调试收尾没成功。" : $"调试收尾：{a.Closure}（舞台 {a.FinalStage?.Key}）")) ?? "开始写结局。";
			case "play":
			{
				StoryArc? arc = Arcs.Get(target);
				if (arc == null)
				{
					return "找不到这出小剧场。";
				}
				if (args.Length > 2)
				{
					arc.FinalStage = Staging.Stages.FirstOrDefault(s => string.Equals(s.Key, args[2], StringComparison.OrdinalIgnoreCase));
					arc.StagePlace = arc.FinalStage?.Venue ?? arc.StagePlace;
				}
				return ConclusionScenePlayer.TryStart(arc, null, _log) ? $"开演：{arc.FinalStage?.Key}" : "这出小剧场没有能演的台词。";
			}
			case "inject":
			{
				string path = Path.IsPathRooted(target) ? target : Path.Combine(_modDir, "debug", target);
				if (!File.Exists(path))
				{
					return $"找不到文件 {path}（格式和小剧场检测的回复一样：{{\"arcs\":[...]}}）。";
				}
				return Inject(File.ReadAllText(path));
			}
			case "make" when args.Length >= 4:
			{
				string[] who = args[2].Split(',', '，').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
				string description = string.Join(" ", args.Skip(3));
				var json = new
				{
					storylines = new[]
					{
						new
						{
							key = "inject_" + Guid.NewGuid().ToString("N")[..8],
							name = target,
							premise = description,
							cast = who,
							right_now = description,
							steps = who.Select((n, i) => new { key = "o" + (i + 1), kind = "talk_to", person = n, task = $"和{Display(ToInternal(n))}聊聊这件事", done_when = "农夫和这个人聊到了这件事" }).ToArray()
						}
					}
				};
				return Inject(System.Text.Json.JsonSerializer.Serialize(json));
			}
			case "fizzle":
			{
				StoryArc? arc = Arcs.Get(target);
				if (arc == null)
				{
					return "找不到这出小剧场。";
				}
				Arcs.Abandon(a => a == arc);
				return $"「{arc.Title}」现在是 {arc.State}。";
			}
			case "expel":
			case "return":
			{
				StoryArc arc = new StoryArc { Id = "debug_" + target, Title = "调试", Kind = verb == "expel" ? "expulsion" : "expulsion_return", LeavingNpc = target, LeavingHow = "left_village", LeavingWhy = "调试命令", Closure = "调试命令" };
				string? why = verb == "expel" ? Expulsions.Expel(arc, GameState.Today.TotalDays, Game1.player?.spouse, IsShopOwner) : Expulsions.Return(arc, GameState.Today.TotalDays);
				if (why == null && verb == "expel")
				{
					Enforce();
				}
				else if (why == null)
				{
					Show(target);
				}
				return why ?? "好了。";
			}
			default:
				return string.Join("\n", Arcs.All.Select(a => $"{a.Id} [{a.State}] 「{a.Title}」 {a.DoneCount}/{a.Goals.Count} {string.Join(",", a.Cast)}{(a.IsExpulsion || a.IsReturn ? $" {a.Kind}:{a.LeavingNpc}" : "")}")
					.Concat(Expulsions.All.Select(r => $"逐出 {r.Who} [{r.Phase}] {r.How}：{r.Why}"))
					.Concat(Shops.Names.Select(n => $"商店脱离 {n}"))
					.DefaultIfEmpty("没有小剧场。"));
		}
	}

	/// <summary>改写了某位村民的角色卡：围着他的、还没收尾的小剧场不了了之（这件事被写没了）。</summary>
	public void OnCardRewritten(string npc)
	{
		int n = Arcs.Abandon(a => a.Involves(npc) && !a.IsExpulsion && !a.IsReturn);
		if (n > 0)
		{
			_log.Info($"{npc} 的角色卡改了性格，有 {n} 出牵涉到的小剧场不了了之。");
		}
	}

	/// <summary>拍片和调试：按小剧场检测的回复格式直接加进来（不走 AI）。</summary>
	private string Inject(string json)
	{
		string players = Game1.player?.Name ?? "Farmer";
		List<StoryArc>? arcs = StoryArcAi.ParseDetection(json, ToInternal, players, _log);
		if (arcs == null || arcs.Count == 0)
		{
			return "没读出小剧场（看日志里的原因）。";
		}
		StoryArc? created = Arcs.Merge(arcs, GameState.Today, 5, Array.Empty<string>(), Usable, _log.Debug);
		return created != null ? $"加进来了：{created.Id}「{created.Title}」，{created.Goals.Count} 个目标。" : "没加进来（可能已有同名的，或进行中的太多）。";
	}

	private string Display(string npc) => _npcs.Names.Display(npc);

	private string ToInternal(string name) => BuiltInPersonas.Canonical(_npcs.Names.Internal(name.Trim()));
}
