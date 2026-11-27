using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Consequences;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Awareness;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace AISV.Game.Consequences;

/// <summary>
/// 一件做错的事。<see cref="Npc"/> 是当事村民（认出来的人、被放鸽子的人……），<see cref="Owner"/> 是失主（不同时）。
/// 当场有人在（<see cref="Speaker"/>）就先演一段反应；<see cref="Immediate"/> 是这件事自己特有的处理（收回东西、停业……）。
/// </summary>
public sealed record Misdeed
{
	public string Kind { get; init; } = "";

	public long Farmer { get; init; }

	public string FarmerName { get; init; } = "";

	public string Npc { get; init; } = "";

	public string Owner { get; init; } = "";

	/// <summary>按谁分开算次数（空 = 同一种事都算在一起）。</summary>
	public string Target { get; init; } = "";

	public string Item { get; init; } = "";

	public int ItemValue { get; init; }

	/// <summary>事情发生的地方（说给人听的名字）。</summary>
	public string Place { get; init; } = "";

	/// <summary>当场发作的人（可以是替身）。</summary>
	public NPC? Speaker { get; init; }

	/// <summary>给 AI 的情况说明（空就不让 AI 写，直接用 <see cref="Fallback"/>）。</summary>
	public string Situation { get; init; } = "";

	/// <summary>AI 没写成时按反应方式说的话。</summary>
	public Func<ReactionStyle, string>? Fallback { get; init; }

	/// <summary>旁边的人（最多三个）各说一句。</summary>
	public IReadOnlyList<NPC> Bystanders { get; init; } = Array.Empty<NPC>();

	public Func<ReactionStyle, string>? BystanderLine { get; init; }

	/// <summary>反应方式定下来以后记进"看到听到"的那件事。</summary>
	public Func<ReactionStyle, Observation?>? Observe { get; init; }

	/// <summary>算好以后马上做的、这件事特有的处理。</summary>
	public Action<ConsequencePlan, ConsequenceRecord>? Immediate { get; init; }

	/// <summary>全部做完（反应方式定了）。</summary>
	public Action<ReactionStyle, ConsequencePlan, ConsequenceRecord>? Done { get; init; }
}

/// <summary>
/// 做错事的后果：同一季犯得越多越重。只在原有后果之外再加（见 assets/consequences/catalog.json）：
/// 当场反应、好感和敌意、心情、罚款和来信、冷落一段时间、被带走、记进"看到听到"、传八卦。
/// </summary>
public sealed class ConsequenceService
{
	private const string Jail = "sdvhead.LawAndOrderSV_Jail";

	private static readonly string[] Officers = { "sdvhead.LawAndOrderSV_Russell", "sdvhead.LawAndOrderSV_Shannon" };

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly IModHelper _helper;

	private readonly string _catalogPath;

	private readonly ILog _log;

	private MisdeedCatalog? _catalog;

	/// <summary>当场还在发作（AI 的话还没回来）：这时不抓人。</summary>
	private int _reacting;

	private bool _arresting;

	public ConsequenceService(Func<ModConfig> config, Func<DocumentStore?> store, IModHelper helper, ILog log)
	{
		_config = config;
		_store = store;
		_helper = helper;
		_catalogPath = Path.Combine(helper.DirectoryPath, "assets", "consequences", "catalog.json");
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Persona { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	public Action<string, int>? AdjustMood { get; set; }

	/// <summary>敌意（开了敌意系统时记）。</summary>
	public Action<string, int>? AddEnmity { get; set; }

	/// <summary>负面记忆：说话不算话。</summary>
	public Action<string, string>? BrokenPromise { get; set; }

	/// <summary>传成八卦：说法、知道的人。</summary>
	public Action<string, IReadOnlyList<string>>? Gossip { get; set; }

	public Func<Observation, object?>? Witness { get; set; }

	/// <summary>带走时没收店里的赃物（小超市接上），返回件数。</summary>
	public Func<int>? Confiscate { get; set; }

	public MisdeedCatalog Catalog
	{
		get
		{
			if (_catalog == null)
			{
				try
				{
					_catalog = MisdeedCatalog.Parse(File.ReadAllText(_catalogPath));
					foreach (string p in _catalog.Problems)
					{
						_log.Warn("后果目录：" + p);
					}
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
				{
					_log.Warn("读不了后果目录，做错事先没有额外后果：" + ex.Message);
					_catalog = MisdeedCatalog.Empty;
				}
			}
			return _catalog;
		}
	}

	public ConsequenceState? State => _store()?.Get(ConsequenceState.Path, ConsequenceState.Doc);

	private void Dirty() => _store()?.MarkDirty(ConsequenceState.Path);

	private static int Today => Game1.Date.TotalDays;

	private static int Season => ConsequenceRules.SeasonKey(Game1.year, Game1.seasonIndex);

	public bool Reacting => _reacting > 0;

	public bool On(string kind) => _config().ConsequencesOn && kind switch
	{
		Misdeeds.Fence => _config().StolenGoodsNoticed,
		Misdeeds.Brawl => _config().BrawlConsequences,
		Misdeeds.MissedMeetup => _config().MeetupConsequences,
		Misdeeds.FavorExpired => _config().FavorConsequences,
		Misdeeds.DebtOverdue => _config().DebtConsequences,
		Misdeeds.TheftCaught => _config().TheftConsequences,
		Misdeeds.Trash => _config().TrashConsequences,
		Misdeeds.Flirt => _config().FlirtConsequences,
		_ => false
	};

	/// <summary>冷落还没过：不肯和你约见（村民）、不肯借钱赊账（全镇）、配偶不帮忙干活。</summary>
	public bool Blocked(string what, long farmer, string target = "") => State?.Blocked(ConsequenceState.CooldownKey(what, farmer, target), Today) ?? false;

	public const string NoMeetups = "no_meetups";

	public const string NoCredit = "no_credit";

	public const string ColdSpouse = "cold_spouse";

	// ---------------- 生命周期 ----------------

	/// <summary>旧存档：小超市里记的这一季赃物次数、等着被带走的搬过来。</summary>
	public void OnSaveLoaded(MartState? mart)
	{
		_reacting = 0;
		_arresting = false;
		if (State is not ConsequenceState s || s.MigratedMart)
		{
			return;
		}
		s.MigratedMart = true;
		if (mart != null)
		{
			long me = Game1.player.UniqueMultiplayerID;
			if (mart.FenceStrikes > 0 && mart.FenceSeason >= 0)
			{
				s.Strikes.Add(new StrikeEntry { Farmer = me, Kind = Misdeeds.Fence, Season = mart.FenceSeason, Count = mart.FenceStrikes });
			}
			if (mart.ArrestPending && !s.ArrestPending.Contains(me))
			{
				s.ArrestPending.Add(me);
			}
			mart.ArrestPending = false;
		}
		Dirty();
	}

	public void OnDayStarted()
	{
		_arresting = false;
		if (State is ConsequenceState s)
		{
			s.Trim(Today, id => Game1.player.mailbox.Contains(id) || Game1.player.mailForTomorrow.Contains(id));
			Dirty();
		}
	}

	public void OnReturnedToTitle()
	{
		_reacting = 0;
		_arresting = false;
		_catalog = null;
	}

	// ---------------- 记一件事 ----------------

	/// <summary>算后果、马上生效的先做（罚款、信、冷落），当场有人在就先演一段，再按反应方式做剩下的。返回这次的记录（没开就是 null）。</summary>
	public ConsequenceRecord? Report(Misdeed m)
	{
		if (!On(m.Kind) || State is not ConsequenceState s || Catalog.Get(m.Kind) is not MisdeedSpec spec)
		{
			return null;
		}
		int strike = s.AddStrike(m.Farmer, m.Kind, m.Target, Season);
		ReactionStyle? known = KnownStyle(m.Npc);
		bool other = m.Owner.Length > 0 && !Same(m.Owner, m.Npc);
		ConsequencePlan plan = ConsequenceRules.Plan(spec, strike, new PlanInput(m.ItemValue, Game1.player.Money, _config().CaughtStealingPenalty, known ?? ReactionStyle.Hot, _config().FenceArrest, other));
		ConsequenceRecord record = s.Add(new ConsequenceRecord
		{
			Kind = m.Kind,
			Day = Today,
			Farmer = m.Farmer,
			FarmerName = m.FarmerName,
			Npc = m.Npc,
			Owner = m.Owner,
			Item = m.Item,
			Strike = strike,
			Fine = plan.Fine,
			ClosedDays = plan.ClosedDays,
			Arrest = plan.Arrest
		}, plan.Letter && HasLetter(m.Kind, strike));
		if (plan.Fine > 0)
		{
			Game1.player.Money -= plan.Fine;
		}
		if (record.MailId.Length > 0)
		{
			Game1.player.mailForTomorrow.Add(record.MailId);
			_helper.GameContent.InvalidateCache("Data/Mail");
		}
		if (plan.CooldownDays > 0 && CooldownKind(m.Kind) is string what)
		{
			s.Block(ConsequenceState.CooldownKey(what, m.Farmer, what == NoCredit ? "" : m.Npc), Today + plan.CooldownDays);
		}
		if (plan.Arrest && !s.ArrestPending.Contains(m.Farmer))
		{
			s.ArrestPending.Add(m.Farmer);
		}
		Dirty();
		_log.Info($"后果：{m.Kind}（{m.Npc}），这一季第 {strike} 次：好感 -{plan.Friendship}，心情 -{plan.Mood}，敌意 +{plan.Enmity}，罚 {plan.Fine}g，停业 {plan.ClosedDays} 天{(plan.Arrest ? "，要被带走" : "")}{(plan.CooldownDays > 0 ? $"，冷落 {plan.CooldownDays} 天" : "")}。");
		m.Immediate?.Invoke(plan, record);

		if (m.Speaker != null && m.Fallback != null)
		{
			_reacting++;
			React(m, m.Speaker, strike, known, style => Finish(m, plan, record, style));
			Bystanders(m);
		}
		else
		{
			Finish(m, plan, record, known ?? ReactionStyle.Hot);
		}
		return record;
	}

	/// <summary>反应方式定下来以后：好感、敌意、心情、负面记忆、"看到听到"、八卦、提示。</summary>
	private void Finish(Misdeed m, ConsequencePlan plan, ConsequenceRecord record, ReactionStyle style)
	{
		if (Catalog.Get(m.Kind) is MisdeedSpec spec && spec.Tier(plan.Strike).FriendshipFromSetting && KnownStyle(m.Npc) == null)
		{
			// 好感按反应方式算：AI 判断出来的脾气这时才知道。
			plan = ConsequenceRules.Plan(spec, plan.Strike, new PlanInput(m.ItemValue, 0, _config().CaughtStealingPenalty, style, false, m.Owner.Length > 0 && !Same(m.Owner, m.Npc))) with { Fine = plan.Fine, Arrest = plan.Arrest, ClosedDays = plan.ClosedDays };
		}
		record.Style = ReactionStyles.Id(style);
		Dirty();
		if (plan.Friendship > 0 && Game1.getCharacterFromName(m.Npc) is NPC npc && Game1.player.friendshipData.ContainsKey(m.Npc))
		{
			Game1.player.changeFriendship(-plan.Friendship, npc);
		}
		if (plan.OwnerFriendship > 0 && Game1.getCharacterFromName(m.Owner) is NPC owner && Game1.player.friendshipData.ContainsKey(m.Owner))
		{
			Game1.player.changeFriendship(-plan.OwnerFriendship, owner);
			AdjustMood?.Invoke(m.Owner, ReactionStyles.MoodDrop(ReactionStyle.Hurt));
		}
		if (plan.Enmity > 0 && _config().HostilityOn)
		{
			AddEnmity?.Invoke(m.Npc, plan.Enmity);
		}
		if (plan.Mood > 0)
		{
			AdjustMood?.Invoke(m.Npc, -plan.Mood);
		}
		if (plan.Memory)
		{
			BrokenPromise?.Invoke(m.Npc, MemoryText(record));
		}
		if (Witness != null && m.Observe?.Invoke(style) is Observation o)
		{
			Dictionary<string, string> d = new Dictionary<string, string>(o.Details ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
			{
				[AwarenessSystem.NoHeartsTag] = "true",
				[AwarenessSystem.NoMoodTag] = "true",
				["style"] = ReactionStyles.Id(style),
				["strike"] = plan.Strike.ToString()
			};
			Witness(o with { Details = d });
		}
		if (plan.Gossip && GossipText(record, m) is string g)
		{
			Gossip?.Invoke(g, new[] { m.Npc, m.Owner, "Lewis" }.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
		}
		if (plan.Hud && HudText(record) is string hud)
		{
			Hints.Hud(hud, 8000);
		}
		m.Done?.Invoke(style, plan, record);
	}

	private ReactionStyle? KnownStyle(string npc)
	{
		if (ReactionStyles.StyleOf(npc) is ReactionStyle k)
		{
			return k;
		}
		return State?.Styles.TryGetValue(npc, out string? cached) == true ? ReactionStyles.Parse(cached) : null;
	}

	private static string? CooldownKind(string kind) => kind switch
	{
		Misdeeds.MissedMeetup => NoMeetups,
		Misdeeds.DebtOverdue => NoCredit,
		Misdeeds.Flirt => ColdSpouse,
		_ => null
	};

	// ---------------- 当场发作 ----------------

	public static string StyleHint(ReactionStyle s) => s switch
	{
		ReactionStyle.Cold => Tr.Get("mart.fenceStyleCold", "你的脸一下子冷到了冰点。你不吼不叫，只盯着对方，一字一句说一句又冷又狠、让人后背发凉的话，说完就当这个人不存在了。"),
		ReactionStyle.Hurt => Tr.Get("mart.fenceStyleHurt", "你眼眶一下就红了，声音发抖，又委屈又心寒：你一直把对方当朋友，没想到会这样。说着说着快哭出来了。"),
		ReactionStyle.Snide => Tr.Get("mart.fenceStyleSnide", "你先是冷笑一声，然后阴阳怪气、往死里损对方：挖苦这家店、挖苦对方的人品，句句带刺，还故意说得让旁边的人都听见。"),
		_ => Tr.Get("mart.fenceStyleHot", "你当场气炸了：拍柜台、跳脚、大声嚷嚷，让整个店的人都回头看。骂得很冲（不说脏话），扬言要去找镇长、要让全镇都知道。")
	};

	private static string Portrait(ReactionStyle s) => s switch
	{
		ReactionStyle.Hurt => "$s",
		ReactionStyle.Snide => "$u",
		ReactionStyle.Cold => "",
		_ => "$a"
	};

	/// <summary>当场的反应：先按脾气做动作，再说一句（AI 按人设、按脾气；不认识的 NPC 让 AI 顺便判断脾气）。</summary>
	private void React(Misdeed m, NPC speaker, int strike, ReactionStyle? known, Action<ReactionStyle> done)
	{
		bool finished = false;
		void Show(ReactionStyle st, string line)
		{
			if (finished)
			{
				return;
			}
			finished = true;
			_reacting = Math.Max(0, _reacting - 1);
			Act(speaker, st);
			if (Game1.currentLocation == speaker.currentLocation && Game1.activeClickableMenu == null && !Game1.eventUp)
			{
				Game1.DrawDialogue(new StardewValley.Dialogue(speaker, null, line + Portrait(st)));
			}
			else
			{
				NpcSpeech.Say(speaker, line, 6000);
			}
			done(st);
		}
		ReactionStyle guess = known ?? ReactionStyle.Hot;
		if (Ai is not IAiSender ai || !_config().AiOn || m.Situation.Length == 0)
		{
			Show(guess, m.Fallback!(guess));
			return;
		}
		string situation = m.Situation;
		if (strike >= 2)
		{
			situation += Tr.Get("consequence.againPrompt", "而且这一季这已经是第 {{n}} 回了，全镇都在传。", ("n", strike));
		}
		string styleBlock = known is ReactionStyle k
			? Tr.Get("mart.fencePromptStyle", "# 你现在的反应\n{{hint}}", ("hint", StyleHint(k)))
			: Tr.Get("mart.fencePromptPick", "# 你现在的反应\n按你的性格，从下面四种里挑最像你的一种，照它演：\n- hot：{{hot}}\n- cold：{{cold}}\n- hurt：{{hurt}}\n- snide：{{snide}}",
				("hot", StyleHint(ReactionStyle.Hot)), ("cold", StyleHint(ReactionStyle.Cold)), ("hurt", StyleHint(ReactionStyle.Hurt)), ("snide", StyleHint(ReactionStyle.Snide)));
		string howTo = Tr.Get("mart.fencePromptOut", "# 怎么说\n这是游戏里情绪最激烈的一幕，反应一定要大、要有戏，像电视剧里的名场面：不能平淡、不能客气、不能讲道理、不能一句带过。用你自己的说话习惯说两三句（至少两句，不少于 30 个字），点明是什么东西、是谁家的，再狠狠表态（要找镇长、要让全镇知道、再也不来了、记你一辈子……），30～80 个字，可以用感叹号、问号和省略号。不写动作、不加引号。\n\n# 输出\n只输出 JSON：{\"style\":\"hot|cold|hurt|snide\",\"line\":\"你说的话\"}");
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(m.Npc)}。\n{Persona?.Invoke(m.Npc) ?? ""}\n\n# 现在\n{situation}\n\n{styleBlock}\n\n" + howTo;
		// AI 二十秒没回来就先用写好的话，免得一直不抓人。
		DelayedAction.functionAfterDelay(() => Show(guess, m.Fallback!(guess)), 20000);
		Task.Run(async () =>
		{
			ReactionStyle st = guess;
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, "说。", 700, 0.95)).ConfigureAwait(false);
				if (r.Ok && JsonSlice(r.Text ?? "") is string json)
				{
					using JsonDocument doc = JsonDocument.Parse(json);
					if (known == null && doc.RootElement.TryGetProperty("style", out JsonElement se))
					{
						st = ReactionStyles.Parse(se.GetString());
					}
					line = doc.RootElement.TryGetProperty("line", out JsonElement le) ? Said(le.GetString()) : null;
				}
			}
			catch (Exception ex)
			{
				_log.Debug("当场的话没生成：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				if (known == null && State is ConsequenceState s)
				{
					s.Styles[m.Npc] = ReactionStyles.Id(st);
					Dirty();
				}
				Show(st, string.IsNullOrWhiteSpace(line) ? m.Fallback!(st) : line!);
			});
		});
	}

	/// <summary>整理 AI 说的话（最多 120 字）。</summary>
	public static string? Said(string? text)
	{
		string t = AISV.Core.Dialogue.TextTools.CleanForGame(AISV.Core.Dialogue.TextTools.Truncate((text ?? "").Trim().Trim('"', '“', '”'), 120));
		return string.IsNullOrWhiteSpace(t) ? null : t;
	}

	private static string? JsonSlice(string text)
	{
		int a = text.IndexOf('{'), b = text.LastIndexOf('}');
		return a >= 0 && b > a ? text.Substring(a, b - a + 1) : null;
	}

	/// <summary>按脾气做动作：气得跳脚 / 冷冷地"……" / 难过 / 冷笑。</summary>
	public static void Act(NPC n, ReactionStyle s)
	{
		switch (s)
		{
			case ReactionStyle.Hot:
				n.doEmote(12);
				n.jump();
				n.shake(800);
				Game1.playSound("clubhit");
				break;
			case ReactionStyle.Cold:
				n.doEmote(40);
				Game1.playSound("breathout");
				break;
			case ReactionStyle.Hurt:
				n.doEmote(28);
				Game1.playSound("ow");
				break;
			default:
				n.doEmote(56);
				Game1.playSound("dwop");
				break;
		}
	}

	/// <summary>旁边的人的反应：爱看热闹的笑出声，正派的倒吸一口气，心软的叹气。</summary>
	private void Bystanders(Misdeed m)
	{
		if (m.BystanderLine == null)
		{
			return;
		}
		int i = 0;
		foreach (NPC c in m.Bystanders.Where(b => b != m.Speaker).Take(3))
		{
			string real = c.modData.TryGetValue("AIStardewValley.Core/Real", out string? r) ? r : c.Name;
			ReactionStyle st = ReactionStyles.StyleOf(real) ?? ReactionStyle.Hot;
			string line = m.BystanderLine(st);
			DelayedAction.functionAfterDelay(() =>
			{
				c.doEmote(st == ReactionStyle.Snide ? 56 : 16);
				NpcSpeech.Say(c, line, 4000);
			}, 1200 + 900 * i++);
		}
	}

	// ---------------- 带走 ----------------

	/// <summary>每帧：有要带走的、你又空着手（不在过场、菜单、节日里），就来了。</summary>
	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady)
		{
			return;
		}
		if (_arresting || _reacting > 0 || State is not ConsequenceState s || !s.ArrestPending.Contains(Game1.player.UniqueMultiplayerID) || !Context.IsPlayerFree || Game1.eventUp || Game1.isFestival() || Game1.timeOfDay >= 2400 || Game1.ticks % 30 != 0)
		{
			return;
		}
		s.ArrestPending.Remove(Game1.player.UniqueMultiplayerID);
		Dirty();
		ConsequenceRecord? last = s.Records.LastOrDefault(r => r.Arrest && r.Farmer == Game1.player.UniqueMultiplayerID);
		Arrest(last?.Kind ?? Misdeeds.Fence);
	}

	private void Arrest(string kind)
	{
		_arresting = true;
		NPC? officer = Officers.Select(n => Game1.getCharacterFromName(n)).FirstOrDefault(n => n != null);
		// 装了 Law and Order 就关进它的牢里；警长、副警长还没在剧情里登场时由镇长押过去。
		bool jail = Game1.getLocationFromName(Jail) != null;
		officer ??= Game1.getCharacterFromName("Lewis");
		int taken = kind == Misdeeds.Fence ? Confiscate?.Invoke() ?? 0 : 0;
		string farmer = Game1.player.Name;
		string fallback = kind == Misdeeds.Brawl
			? jail
				? Tr.Get("consequence.arrestBrawlOfficer", "{{farmer}}，这一季你已经第三回在店里动手打人了。跟我回局里，今晚就在牢里冷静冷静！", ("farmer", farmer))
				: Tr.Get("consequence.arrestBrawlLewis", "{{farmer}}！这一季第三回动手了！镇上不能再这样下去，今天你哪儿也别想去，跟我走！", ("farmer", farmer))
			: jail
				? Tr.Get("mart.arrestOfficer", "{{farmer}}，这一季你店里已经第三回被认出卖赃物了。东西我们全扣了——跟我回局里，今晚就在牢里反省吧！", ("farmer", farmer))
				: Tr.Get("mart.arrestLewis", "{{farmer}}！这一季第三回了！你把镇上的脸都丢尽了！店里的赃物全部没收，今天你哪儿也别想去，跟我走！", ("farmer", farmer));
		void Speak(string line)
		{
			if (officer == null)
			{
				Lockup(jail);
				return;
			}
			Game1.playSound("doorClose");
			Game1.DrawDialogue(new StardewValley.Dialogue(officer, null, line + "$a"));
			Game1.afterDialogues = () => Lockup(jail);
		}
		if (officer == null || Ai is not IAiSender ai || !_config().AiOn)
		{
			Speak(fallback);
			return;
		}
		string why = kind == Misdeeds.Brawl
			? Tr.Get("consequence.arrestWhyBrawl", "{{farmer}}这一季已经第三次在自己店里和人动手，全镇都在议论。", ("farmer", farmer))
			: Tr.Get("consequence.arrestWhyFence", "{{farmer}}开的农夫小超市这一季已经第三次被人认出在卖偷来的东西，全镇都在议论。店里的赃物已经全部没收，店也封了。", ("farmer", farmer));
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(officer.Name)}。\n{Persona?.Invoke(officer.Name) ?? ""}\n\n"
			+ Tr.Get("consequence.arrestPrompt", "# 现在\n{{why}}你是来把{{farmer}}带走的。\n\n# 怎么说\n这是游戏里最严重的一幕，要有压迫感和戏剧性：严厉、愤怒、失望，宣布要带走对方{{where}}。60 个字以内，不写动作，不加引号，只写说出口的话。",
				("why", why), ("farmer", farmer), ("where", jail ? Tr.Get("mart.arrestWhereJail", "关进牢里过一夜") : Tr.Get("mart.arrestWhereHome", "押回家闭门思过，今天哪儿也不许去")));
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, "说。", 300, 0.9)).ConfigureAwait(false);
				line = r.Ok ? Said(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug("带走的话没生成：" + ex.Message);
			}
			MainThread.Run(() => Speak(string.IsNullOrWhiteSpace(line) ? fallback : line!));
		});
		_log.Info($"后果：{farmer} 被带走（{kind}），没收 {taken} 件。");
	}

	/// <summary>关进牢里待一会儿（装了 Law and Order），再押回家，第二天在家醒来。传送本身会黑屏过渡，这里不再另外淡入淡出。</summary>
	private void Lockup(bool jail)
	{
		if (!jail)
		{
			SendHome();
			return;
		}
		LocationRequest req = Game1.getLocationRequest(Jail);
		req.OnWarp += () =>
		{
			Game1.player.faceDirection(0);
			Hints.Hud(Tr.Get("mart.arrestJailHud", "你被关进了牢里。"), 6000);
			DelayedAction.functionAfterDelay(SendHome, 6000);
		};
		Game1.warpFarmer(req, 10, 22, 0);
	}

	private void SendHome()
	{
		FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
		Point bed = home.GetPlayerBedSpot();
		LocationRequest req = Game1.getLocationRequest(home.NameOrUniqueName);
		req.OnWarp += () =>
		{
			Hints.Hud(Tr.Get("mart.arrestHomeHud", "你被押回了家，今天就到这儿了。"), 6000);
			_arresting = false;
			DelayedAction.functionAfterDelay(() =>
			{
				// 当天结束：直接睡下（像晕倒那样第二天在家醒来，但不扣晕倒的钱）。
				if (AccessTools.Method(typeof(GameLocation), "startSleep") is { } sleep && Game1.currentLocation is FarmHouse here)
				{
					Game1.player.isInBed.Value = true;
					sleep.Invoke(here, null);
				}
			}, 2500);
		};
		Game1.warpFarmer(req, bed.X, bed.Y, 2);
	}

	// ---------------- 文字 ----------------

	private string Name(string npc) => npc.Length == 0 ? "" : Display(npc);

	/// <summary>这种事第几次会来信（目录里写了 letter，这里有对应的信才寄）。</summary>
	private static bool HasLetter(string kind, int strike) => kind switch
	{
		Misdeeds.Fence or Misdeeds.DebtOverdue => true,
		Misdeeds.Brawl or Misdeeds.Trash => strike >= 2,
		Misdeeds.TheftCaught => strike >= 3,
		_ => false
	};

	public string? MailText(ConsequenceRecord r)
	{
		(string, object?)[] args = { ("owner", Name(r.Owner.Length > 0 ? r.Owner : r.Npc)), ("npc", Name(r.Npc)), ("item", r.Item), ("gold", r.Fine), ("days", r.ClosedDays), ("n", r.Strike), ("farmer", r.FarmerName) };
		string? text = r.Kind switch
		{
			Misdeeds.Fence => r.Strike >= 3
				? Tr.Get("mart.fenceArrestMail", "@：^这一季第三回了。{{owner}}家的{{item}}又出现在你的货架上。^店里的赃物已经全部没收，罚款 {{gold}}g，小超市封店 {{days}} 天。^我在这个镇上当了这么多年镇长，从没这么丢脸过。好好想想你要做个什么样的人。^   -刘易斯镇长", args)
				: r.Strike == 2
					? Tr.Get("mart.fenceAgainMail", "@：^又是你。{{owner}}来找我，说你店里卖的{{item}}是从他们家偷的——这一季已经是第二回了。^东西还给失主，罚款加倍：{{gold}}g，小超市停业整顿 {{days}} 天。^再有第三回，就不是罚款能了事的了。^   -刘易斯镇长", args)
					: Tr.Get("mart.fenceFineMail", "@：^{{owner}}来找我了：你店里卖的{{item}}是从{{owner}}家偷的。东西已经还给失主，按镇上的规矩，窝赃销赃罚款 {{gold}}g，已经从你的账上扣了。^再有下次，我就不只是写信了。^   -刘易斯镇长", args),
			Misdeeds.Brawl => r.Strike >= 3
				? Tr.Get("consequence.mail.brawl3", "@：^这一季第三回动手了，这次是{{npc}}。^罚款 {{gold}}g，已经从你的账上扣了。你被带走的事全镇都知道了。^我不想再写这样的信。^   -刘易斯镇长", args)
				: Tr.Get("consequence.mail.brawl2", "@：^又动手了？这一季第二回，这次是{{npc}}。^屡教不改，罚款加倍，另外再罚 {{gold}}g。^再有下次，就不是罚钱的事了。^   -刘易斯镇长", args),
			Misdeeds.DebtOverdue => r.Strike >= 3
				? Tr.Get("consequence.mail.debt3", "@：^{{npc}}又来找我了，说你借的钱到期不还。这一季已经是第三回。^镇上的人都在说这事。从今往后一阵子，大概没人愿意再借钱给你，格斯那里也不会再让你赊账了。^先把欠的还上吧。^   -刘易斯镇长", args)
				: r.Strike == 2
					? Tr.Get("consequence.mail.debt2", "@：^{{npc}}跟我提了一句，你欠的钱又到期没还了。这一季第二回了。^有借有还，再借不难。别让人寒心。^   -刘易斯镇长", args)
					: Tr.Get("consequence.mail.debt1", "@：^听说你欠{{npc}}的钱到期了。^手头紧可以理解，但记得跟人家说一声，早点还上。^   -刘易斯镇长", args),
			Misdeeds.TheftCaught => Tr.Get("consequence.mail.theft3", "@：^这一季你第三回偷东西被当场抓住了，这次是{{npc}}。^镇上的人现在出门都要锁门了。你自己想想，这是你想过的日子吗？^   -刘易斯镇长", args),
			Misdeeds.Trash => r.Strike >= 3
				? Tr.Get("consequence.mail.trash3", "@：^你又来翻我家门口的垃圾桶了，这一季都第 {{n}} 回了。^全镇都在笑话这件事。真要缺什么，可以直接来问我。^   -{{npc}}", args)
				: Tr.Get("consequence.mail.trash2", "@：^我看见了，你又翻了我家门口的垃圾桶。^里面没什么好东西，弄得一地都是。下次别这样了。^   -{{npc}}", args),
			_ => null
		};
		return text?.Replace("\n", "^");
	}

	private string MemoryText(ConsequenceRecord r) => r.Kind switch
	{
		Misdeeds.MissedMeetup => Tr.Get("consequence.memory.meetup", "{{farmer}}约好了却没来，这一季第 {{n}} 回放我鸽子", ("farmer", r.FarmerName), ("n", r.Strike)),
		_ => Tr.Get("consequence.memory.favor", "托{{farmer}}办的事到期了也没办成，答应得好好的", ("farmer", r.FarmerName))
	};

	private string? GossipText(ConsequenceRecord r, Misdeed m)
	{
		string farmer = r.FarmerName, npc = Name(r.Npc), owner = Name(r.Owner.Length > 0 ? r.Owner : r.Npc);
		return r.Kind switch
		{
			Misdeeds.Fence => r.Strike >= 3
				? Tr.Get("mart.caughtGossipArrest", "{{farmer}}又在自己店里卖赃物，这回是{{owner}}家的{{item}}，被{{name}}认了出来——这一季都第三回了，听说直接被带走了，店也封了。", ("farmer", farmer), ("owner", owner), ("item", r.Item), ("name", npc))
				: r.Strike == 2
					? Tr.Get("mart.caughtGossipAgain", "{{farmer}}的店又被认出卖赃物了，{{owner}}家的{{item}}，{{name}}当场闹了起来。镇长罚了 {{gold}}g，还让店停业整顿。", ("farmer", farmer), ("owner", owner), ("item", r.Item), ("name", npc), ("gold", r.Fine))
					: Tr.Get("mart.caughtGossip", "{{name}}在{{farmer}}的店里认出了{{owner}}家被偷的{{item}}，当场吵了起来，镇长罚了{{farmer}} {{gold}}g。", ("name", npc), ("farmer", farmer),
						("owner", Same(r.Owner, r.Npc) || r.Owner.Length == 0 ? Tr.Get("mart.ownerSelf", "自己") : owner), ("item", r.Item), ("gold", r.Fine)),
			Misdeeds.Brawl => r.Strike >= 3
				? Tr.Get("consequence.gossip.brawl3", "{{farmer}}这一季已经第三回在店里打人了，这次是{{npc}}。听说当场就被带走了。", ("farmer", farmer), ("npc", npc))
				: Tr.Get("consequence.gossip.brawl2", "{{farmer}}又在店里动手了，这回挨打的是{{npc}}。镇长罚了双倍的钱。", ("farmer", farmer), ("npc", npc)),
			Misdeeds.MissedMeetup => Tr.Get("consequence.gossip.meetup", "{{farmer}}又放了{{npc}}的鸽子，这一季都第 {{n}} 回了，{{npc}}说再也不信{{farmer}}的约了。", ("farmer", farmer), ("npc", npc), ("n", r.Strike)),
			Misdeeds.FavorExpired => Tr.Get("consequence.gossip.favor", "{{npc}}托{{farmer}}的事又没办成。{{npc}}说{{farmer}}答应得好好的，从来不当回事。", ("farmer", farmer), ("npc", npc)),
			Misdeeds.DebtOverdue => r.Strike >= 3
				? Tr.Get("consequence.gossip.debt3", "{{farmer}}欠钱不还出了名，镇长都写信去催了。听说现在谁都不肯再借钱给{{farmer}}，格斯那里也不让赊账了。", ("farmer", farmer))
				: Tr.Get("consequence.gossip.debt2", "{{farmer}}欠{{npc}}的钱又拖着没还，这一季已经第二回了。", ("farmer", farmer), ("npc", npc)),
			Misdeeds.TheftCaught => r.Strike >= 3
				? Tr.Get("consequence.gossip.theft3", "{{farmer}}这一季已经第三回偷东西被当场抓住了，镇上的人现在出门都要锁门。", ("farmer", farmer))
				: Tr.Get("consequence.gossip.theft2", "{{farmer}}又偷东西被抓了个正着，这回是{{npc}}。这一季已经是第二回了。", ("farmer", farmer), ("npc", npc)),
			Misdeeds.Trash => Tr.Get("consequence.gossip.trash", "{{farmer}}又去翻{{place}}门口的垃圾桶了，这一季都第 {{n}} 回了，大家开始管{{farmer}}叫“垃圾桶侠”。", ("farmer", farmer), ("place", m.Place), ("n", r.Strike)),
			Misdeeds.Flirt => Tr.Get("consequence.gossip.flirt", "{{farmer}}当着{{partner}}的面和{{other}}调情，{{partner}}气坏了。这一季已经不是第一回了。", ("farmer", farmer), ("partner", npc), ("other", Name(r.Owner))),
			_ => null
		};
	}

	private string? HudText(ConsequenceRecord r) => r.Kind switch
	{
		Misdeeds.Fence => Tr.Get("mart.caughtHud", "{{name}}认出了赃物：罚款 {{gold}}g", ("name", Name(r.Npc)), ("gold", r.Fine))
			+ (r.Arrest ? Tr.Get("mart.fenceTailArrest", "，要被带走") : r.ClosedDays > 0 ? Tr.Get("mart.fenceTailClosed", "，停业 {{days}} 天", ("days", r.ClosedDays)) : ""),
		Misdeeds.Brawl => r.Arrest
			? Tr.Get("consequence.hud.brawl3", "这一季第三回动手：再罚 {{gold}}g，要被带走", ("gold", r.Fine))
			: Tr.Get("consequence.hud.brawl2", "这一季第二回动手：罚款加倍，再罚 {{gold}}g", ("gold", r.Fine)),
		Misdeeds.MissedMeetup => Tr.Get("consequence.hud.meetup", "{{npc}}被你放了太多次鸽子，这几天不想再和你约了", ("npc", Name(r.Npc))),
		Misdeeds.DebtOverdue => Tr.Get("consequence.hud.debt", "欠钱不还传开了：这两周借不到钱，也赊不了账"),
		Misdeeds.TheftCaught => Tr.Get("consequence.hud.theft", "这一季第三回偷东西被抓，全镇都知道了"),
		Misdeeds.Flirt => Tr.Get("consequence.hud.flirt", "{{npc}}很伤心，这几天不想帮你干活", ("npc", Name(r.Npc))),
		_ => null
	};

	/// <summary>Data/Mail：每条要寄信的记录一封。</summary>
	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (!e.NameWithoutLocale.IsEquivalentTo("Data/Mail") || !Context.IsWorldReady || State is not ConsequenceState s)
		{
			return;
		}
		List<ConsequenceRecord> letters = s.Records.Where(r => r.MailId.Length > 0).ToList();
		if (letters.Count == 0)
		{
			return;
		}
		e.Edit(asset =>
		{
			IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
			foreach (ConsequenceRecord r in letters)
			{
				if (MailText(r) is string text)
				{
					data[r.MailId] = text;
				}
			}
		}, AssetEditPriority.Late);
	}

	// ---------------- 调试 ----------------

	public string Command(string[] args)
	{
		if (State is not ConsequenceState s)
		{
			return "先进存档。";
		}
		switch (args.FirstOrDefault())
		{
			case "strikes":
				return s.Strikes.Count == 0 ? "这一季还没有。" : string.Join("\n", s.Strikes.Select(x => $"{x.Kind} {x.Target} 农夫 {x.Farmer} 季 {x.Season}：{x.Count} 次"))
					+ (s.Cooldowns.Count > 0 ? "\n冷落：" + string.Join("，", s.Cooldowns.Select(c => $"{c.Key} 到第 {c.Until} 天")) : "")
					+ (s.ArrestPending.Count > 0 ? "\n等着被带走：" + string.Join("，", s.ArrestPending) : "");
			case "reset":
				s.Strikes.Clear();
				s.Cooldowns.Clear();
				s.ArrestPending.Clear();
				Dirty();
				return "清掉了这一季的次数、冷落和要带走的。";
			case "fire" when args.Length > 2:
				string kind = args[1], npc = args[2];
				if (Catalog.Get(kind) == null)
				{
					return "不认识的种类：" + kind + "（" + string.Join(" ", Misdeeds.All) + "）";
				}
				ConsequenceRecord? r = Report(new Misdeed
				{
					Kind = kind,
					Farmer = Game1.player.UniqueMultiplayerID,
					FarmerName = Game1.player.Name,
					Npc = npc,
					Target = kind is Misdeeds.MissedMeetup or Misdeeds.FavorExpired or Misdeeds.Trash or Misdeeds.Flirt ? npc : "",
					Owner = args.Length > 3 ? args[3] : "",
					Item = ItemRegistry.Create("(O)24").DisplayName,
					ItemValue = 35,
					Place = Name(npc)
				});
				return r == null ? "没开（看设置里的后果开关）。" : $"{kind} {npc}：这一季第 {r.Strike} 次，罚 {r.Fine}g{(r.MailId.Length > 0 ? "，明天来信" : "")}{(r.Arrest ? "，要被带走" : "")}。";
			default:
				return string.Join("\n", s.Records.TakeLast(12).Select(x => $"第 {x.Day} 天 {x.Kind} {x.Npc} 第 {x.Strike} 次 罚 {x.Fine}g {x.Style}{(x.MailId.Length > 0 ? " 信 " + x.MailId : "")}"))
					is { Length: > 0 } list ? list : "还没有记录。";
		}
	}

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
