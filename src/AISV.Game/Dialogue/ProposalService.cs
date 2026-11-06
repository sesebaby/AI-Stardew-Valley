using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social.Personality;
using AISV.Core.Text;
using AISV.Game.Awareness;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;

namespace AISV.Game.Dialogue;

/// <summary>对话里的表白和求婚：能不能提、提了以后把村民的决定落到游戏里并记下来。只在主线程用。</summary>
public sealed class ProposalService
{
	private readonly Func<ModConfig> _config;

	private readonly RelationshipStates _states;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	public ProposalService(Func<ModConfig> config, RelationshipStates states, Relationships relationships, NpcDirectory npcs, ILog log)
	{
		_config = config;
		_states = states;
		_relationships = relationships;
		_npcs = npcs;
		_log = log;
	}

	public SocialSystem? Social { get; set; }

	/// <summary>故事里已经单身的已婚村民（已定传说接上后用）。</summary>
	public Func<string, bool>? StorySingle { get; set; }

	public ProposalOptions Options(NPC npc)
	{
		try
		{
			ProposalSituation s = Situation(npc);
			_states.SetNarrativeAvailability(npc.Name, s.Narrative);
			return ProposalRules.Evaluate(s, _config());
		}
		catch (Exception ex)
		{
			_log.Warn($"判断能不能向 {npc.Name} 表白时出错：{ex.Message}");
			return ProposalOptions.None;
		}
	}

	public string Status(string npc) => Kind(npc);

	public string Narrative(string npc) => !VillageRelations.IsMarriedNpc(npc) ? "canonical_single" : StorySingle?.Invoke(npc) == true ? "story_single" : "canonical_married";

	/// <summary>提示词里的"游戏里的状态"。</summary>
	public string VanillaBlock(string npc)
	{
		Farmer p = Game1.player;
		string spouse = string.IsNullOrWhiteSpace(p.spouse) || p.isEngaged() ? "没有" : _npcs.Names.Display(p.spouse);
		return string.Join("\n",
			$"- 你和农夫：{KindWord(Kind(npc))}",
			$"- 农夫的配偶或室友：{spouse}",
			$"- 农夫现在有没有婚约：{(p.isEngaged() ? "有" : "没有")}",
			$"- 农夫的孩子：{Children(p)}",
			$"- 你在原作里的婚姻：{(Narrative(npc) == "canonical_married" ? "有，而且故事里没有分开" : "没有")}",
			$"- 上次被拒后的冷静期：{Cooldown(npc)}");
	}

	/// <summary>把决定落到游戏里。答应了但条件已经变了（或改不了状态）时返回给玩家看的话，不记录。</summary>
	public string? Apply(NPC npc, ProposalKind kind, ProposalDecision d, string playerText)
	{
		int today = GameState.Today.TotalDays;
		if (d.SaidYes)
		{
			ProposalOptions now = Options(npc);
			if (kind == ProposalKind.Dating ? !now.Dating : !now.Marriage)
			{
				_log.Warn($"{npc.Name} 答应了{Word(kind)}，但条件已经不满足（{now.BlockReason}）。");
				return Failed();
			}
			string? error = kind == ProposalKind.Dating ? StartDating(npc.Name) : Engage(npc.Name);
			if (error != null)
			{
				_log.Warn($"{npc.Name} 答应了{Word(kind)}，但改不了游戏里的关系：{error}");
				return Failed();
			}
			npc.doEmote(ProposalRules.AcceptEmote(kind));
		}
		else if (d.WaitDays <= 0)
		{
			d.WaitDays = Math.Max(1, !string.IsNullOrWhiteSpace(d.Limit) ? _config().CourtRestAfterLine : _config().CourtRestAfterNo);
		}
		Record(npc, kind, d, playerText, today);
		return null;
	}

	private void Record(NPC npc, ProposalKind kind, ProposalDecision d, string playerText, int today)
	{
		string name = npc.Name;
		Game1.player.friendshipData.TryGetValue(name, out Friendship? f);
		_states.RecordProposal(name, kind, d, playerText, today, f?.Status.ToString() ?? "missing", f?.WeddingDate?.ToString() ?? "");
		_log.Debug($"{name} 对{Word(kind)}：{(d.SaidYes ? "答应" : "拒绝")}，冷却 {d.WaitDays} 天{(string.IsNullOrWhiteSpace(d.Limit) ? "" : "，划了界线")}。");
		GameDate date = GameState.Today;
		SocialSystem? social = Social;
		if (social != null)
		{
			if (!string.IsNullOrWhiteSpace(d.Keepsake))
			{
				social.Feelings.AddKeyMemory(name, d.Keepsake, date);
			}
			string milestone = ProposalRules.Milestone(kind, d);
			social.Milestones.AddCustom(name, milestone, milestone == "romantic_boundary_set" ? d.Limit ?? "" : d.Keepsake, date);
		}
		ActionPatches.RecordProposal(npc, kind == ProposalKind.Marriage, d.SaidYes);
	}

	private ProposalSituation Situation(NPC npc)
	{
		Farmer p = Game1.player;
		string name = npc.Name;
		string narrative = Narrative(name);
		return new ProposalSituation
		{
			Hearts = _relationships.EffectiveHearts(name),
			Status = Kind(name),
			Narrative = narrative,
			AiAvailable = _config().AiOn,
			IsFarmhand = Context.IsWorldReady && !Context.IsMainPlayer,
			EventActive = Game1.eventUp || Game1.currentLocation?.currentEvent != null,
			CanSocialize = !npc.IsInvisible && npc.IsVillager && npc.CanSocialize,
			NpcEnabled = _npcs.IsEnabled(npc),
			IsChild = VillageRelations.IsKnownChild(name) || ChildSafety.IsListed(name),
			Datable = npc.datable.Value,
			HasPersona = BuiltInPersonas.HasVanilla(name),
			InCooldown = _states.InProposalCooldown(name, GameState.Today.TotalDays),
			OtherCommitment = OtherCommitment(p, name),
			FreeToMarry = string.IsNullOrWhiteSpace(p.spouse) && !p.isEngaged() && !p.hasCurrentOrPendingRoommate(),
			HouseUpgradeLevel = p.HouseUpgradeLevel
		};
	}

	private static bool OtherCommitment(Farmer p, string npc)
	{
		if (!string.IsNullOrWhiteSpace(p.spouse) && !string.Equals(p.spouse, npc, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return p.isEngaged() && !(p.friendshipData.TryGetValue(npc, out Friendship? f) && f.IsEngaged());
	}

	private static string Kind(string npc)
	{
		Farmer p = Game1.player;
		if (!p.friendshipData.TryGetValue(npc, out Friendship? f))
		{
			return string.Equals(p.spouse, npc, StringComparison.OrdinalIgnoreCase) ? "married" : "none";
		}
		return f.Status switch
		{
			FriendshipStatus.Dating => "dating",
			FriendshipStatus.Engaged => "engaged",
			FriendshipStatus.Married => "married",
			FriendshipStatus.Divorced => "divorced",
			_ => "none"
		};
	}

	private string Cooldown(string npc)
	{
		int until = _states.State(npc)?.NoAskUntil ?? 0;
		int left = until - GameState.Today.TotalDays;
		return left > 0 ? $"还有 {left} 天" : "没有";
	}

	private static string KindWord(string kind) => kind switch
	{
		"dating" => "正在交往",
		"engaged" => "订了婚",
		"married" => "结了婚",
		"divorced" => "离过婚",
		_ => "还没有恋爱关系"
	};

	private static string Children(Farmer p)
	{
		List<Child> kids = p.getChildren() ?? new List<Child>();
		if (kids.Count == 0)
		{
			return "没有";
		}
		return string.Join("；", kids.Select(c => $"{(string.IsNullOrWhiteSpace(c.Name) ? "还没起名" : c.Name)}（{(c.Age switch { 0 => "婴儿", 1 => "会爬了", _ => "会走了" })}，{(c.Gender == Gender.Male ? "男孩" : "女孩")}）"));
	}

	private static string? StartDating(string npc)
	{
		Friendship f = Friendship(npc);
		f.Status = FriendshipStatus.Dating;
		f.WeddingDate = null;
		return null;
	}

	private static string? Engage(string npc)
	{
		Farmer p = Game1.player;
		if (!string.IsNullOrWhiteSpace(p.spouse))
		{
			return "农夫已经结婚了";
		}
		if (p.isEngaged())
		{
			return "农夫已经有婚约了";
		}
		Friendship f = Friendship(npc);
		f.Status = FriendshipStatus.Engaged;
		f.WeddingDate = WeddingDate(3);
		p.spouse = npc;
		p.removeDatingActiveDialogueEvents(npc);
		return null;
	}

	private static Friendship Friendship(string npc)
	{
		if (!Game1.player.friendshipData.TryGetValue(npc, out Friendship? f))
		{
			f = new Friendship(0);
			Game1.player.friendshipData[npc] = f;
		}
		return f;
	}

	/// <summary>至少 <paramref name="daysAhead"/> 天以后、第一个能办婚礼的日子（一年之内找不到就用那天）。</summary>
	private static WorldDate WeddingDate(int daysAhead)
	{
		WorldDate date = new WorldDate(Game1.year, Game1.season, Game1.dayOfMonth);
		date.TotalDays += Math.Max(1, daysAhead);
		for (int i = 0; i < 112 && !Game1.canHaveWeddingOnDay(date.DayOfMonth, date.Season); i++)
		{
			date.TotalDays++;
		}
		return date;
	}

	private static string Failed() => Tr.Get("proposal.couldNotApply", "刚要开口，气氛被打断了……这件事只好改天再说。");

	private static string Word(ProposalKind kind) => kind == ProposalKind.Dating ? "表白" : "求婚";
}
