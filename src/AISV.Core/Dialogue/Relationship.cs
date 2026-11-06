using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AISV.Core.Storage;

namespace AISV.Core.Dialogue;

/// <summary>
/// 好感计分规则。普通模式：每次互动 0～上限分，只加好感。敌意模式（默认）：-上限～上限，负分记成敌意，
/// 正分照常加好感并抵消一半（向上取整）的敌意；有效心数 = 好感心数 − 敌意心数。每 250 点一颗心。
/// </summary>
public sealed class RelationshipRules
{
	public const int PointsPerHeart = 250;

	public const int MaxEnmityPoints = 2500;

	public RelationshipRules(bool enmity)
	{
		UsesEnmity = enmity;
	}

	public bool UsesEnmity { get; }

	public int EffectiveHearts(int friendshipPoints, int enmityPoints) => UsesEnmity
		? Math.Max(0, Math.Max(0, friendshipPoints) / PointsPerHeart - Math.Max(0, enmityPoints) / PointsPerHeart)
		: Math.Max(0, friendshipPoints) / PointsPerHeart;

	public int ClampInteraction(int points, int configuredMaximum)
	{
		int max = Math.Max(0, configuredMaximum);
		return Math.Clamp(points, UsesEnmity ? -max : 0, max);
	}

	/// <summary>正分怎么分：加多少好感、抵消多少敌意。</summary>
	public (int Friendship, int EnmityReduced) AllocatePositive(int awarded, int currentEnmity)
	{
		int points = Math.Max(0, awarded);
		return UsesEnmity ? (points, Math.Min((int)Math.Ceiling(points / 2.0), Math.Max(0, currentEnmity))) : (points, 0);
	}
}

/// <summary>给 AI 的关系语气提示（满分 14 心）。</summary>
public static class FriendshipTone
{
	/// <summary>交情深浅的说法，不带数字（给只需要分寸、不该让 AI 把心数说出口的提示词）。</summary>
	public static string Level(int hearts) => hearts switch
	{
		<= 2 => "点头之交，彼此保持礼貌的距离",
		<= 5 => "碰面能开玩笑的程度，不用端着",
		<= 8 => "把对方当朋友，话里带着关心",
		<= 10 => "无话不谈，心事也能讲",
		_ => "老交情了，心事随便讲，旧事随口就来"
	};

	public static string Describe(int hearts, int enmityHearts = 0)
	{
		if (enmityHearts <= 0)
		{
			return $"交情 {hearts}/14 心：{Level(hearts)}。";
		}
		string grudge = enmityHearts switch
		{
			<= 2 => "看对方有点不顺眼，话能少就少",
			<= 5 => "还记着仇，防备着，连客气都懒得装",
			_ => "恨不得绕着走，没有一句好话"
		};
		return $"交情 {hearts}/14 心，可又结了 {enmityHearts} 心的梁子：{grudge}。";
	}
}

/// <summary>和一位村民的恋爱/敌意状态。存在 bonds/romance.json（求爱提议在第 5 期用）。</summary>
public sealed class NpcRomanceState
{
	[JsonPropertyName("game_status_seen")]
	public string GameStatusSeen { get; set; } = "";

	[JsonPropertyName("hostility")]
	public int Hostility { get; set; }

	[JsonPropertyName("latest_answer_note")]
	public string LatestAnswerNote { get; set; } = "";

	[JsonPropertyName("latest_ask_kind")]
	public string LatestAskKind { get; set; } = "";

	[JsonPropertyName("latest_ask_on")]
	public int LatestAskOn { get; set; }

	[JsonPropertyName("no_ask_until")]
	public int NoAskUntil { get; set; }

	[JsonPropertyName("npc")]
	public string Npc { get; set; } = "";

	[JsonPropertyName("past_asks")]
	public List<RelationshipProposalRecord> PastAsks { get; set; } = new List<RelationshipProposalRecord>();

	[JsonPropertyName("standing_with_farmer")]
	public string StandingWithFarmer { get; set; } = "none";

	[JsonPropertyName("story_freedom")]
	public string StoryFreedom { get; set; } = "unknown";

	[JsonPropertyName("wedding_on")]
	public string WeddingOn { get; set; } = "";
}

public sealed class RelationshipProposalRecord
{
	[JsonPropertyName("answer")]
	public string Answer { get; set; } = "";

	[JsonPropertyName("farmer_words")]
	public string FarmerWords { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("keepsake")]
	public string Keepsake { get; set; } = "";

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	[JsonPropertyName("on_day")]
	public int OnDay { get; set; }

	[JsonPropertyName("said_yes")]
	public bool SaidYes { get; set; }

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

public sealed class NpcRomanceSaveData
{
	[JsonPropertyName("npcs")]
	public List<NpcRomanceState> Npcs { get; set; } = new List<NpcRomanceState>();
}

/// <summary>敌意点数等关系状态（存档数据/relationship_proposals.json）。</summary>
public sealed class RelationshipStates
{
	private static readonly DocumentType<NpcRomanceSaveData> Doc = new DocumentType<NpcRomanceSaveData>("关系状态", 1, () => new NpcRomanceSaveData());

	private const string Path = "bonds/romance.json";

	private readonly Func<DocumentStore?> _store;

	private readonly object _sync = new object();

	public RelationshipStates(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public int EnmityPoints(string npcName)
	{
		lock (_sync)
		{
			return Find(npcName)?.Hostility ?? 0;
		}
	}

	public int EnmityHearts(string npcName) => EnmityPoints(npcName) / RelationshipRules.PointsPerHeart;

	public void AddEnmity(string npcName, int points) => Change(npcName, s => s.Hostility = Math.Clamp(s.Hostility + Math.Max(0, points), 0, RelationshipRules.MaxEnmityPoints));

	public void ReduceEnmity(string npcName, int points) => Change(npcName, s => s.Hostility = Math.Clamp(s.Hostility - Math.Max(0, points), 0, RelationshipRules.MaxEnmityPoints));

	public void SetEnmity(string npcName, int points) => Change(npcName, s => s.Hostility = Math.Clamp(points, 0, RelationshipRules.MaxEnmityPoints));

	public NpcRomanceState? State(string npcName)
	{
		lock (_sync)
		{
			return Find(npcName);
		}
	}

	public bool InProposalCooldown(string npcName, int today) => (State(npcName)?.NoAskUntil ?? 0) > today;

	public void SetNarrativeAvailability(string npcName, string availability)
	{
		if (!string.IsNullOrWhiteSpace(availability) && State(npcName)?.StoryFreedom != availability)
		{
			Change(npcName, s => s.StoryFreedom = availability);
		}
	}

	/// <summary>记一次表白/求婚：答应了清掉冷却，拒绝了按天数冷却，有边界就记成划了界线；历史留 20 条。</summary>
	public void RecordProposal(string npcName, ProposalKind kind, ProposalDecision d, string playerMessage, int today, string vanillaStatus, string weddingDate) => Change(npcName, s =>
	{
		string type = kind == ProposalKind.Dating ? "dating" : "marriage";
		s.LatestAskKind = type;
		s.LatestAskOn = today;
		s.LatestAnswerNote = d.Why;
		s.GameStatusSeen = vanillaStatus;
		s.WeddingOn = weddingDate;
		if (d.SaidYes)
		{
			s.StandingWithFarmer = kind == ProposalKind.Dating ? "dating" : "engaged";
			s.NoAskUntil = 0;
		}
		else
		{
			s.NoAskUntil = Math.Max(s.NoAskUntil, today + Math.Max(0, d.WaitDays));
			if (!string.IsNullOrWhiteSpace(d.Limit))
			{
				s.StandingWithFarmer = "boundary_set";
			}
		}
		s.PastAsks.Insert(0, new RelationshipProposalRecord
		{
			Id = $"{npcName}_{type}_{today}_{Guid.NewGuid():N}".ToLowerInvariant().Replace(' ', '_'),
			OnDay = today,
			Kind = type,
			SaidYes = d.SaidYes,
			FarmerWords = ProposalParser.Cut(playerMessage, 500),
			Answer = ProposalParser.Cut(d.Line, 500),
			Why = ProposalParser.Cut(d.Why, 280),
			Keepsake = ProposalParser.Cut(d.Keepsake, 280)
		});
		if (s.PastAsks.Count > 20)
		{
			s.PastAsks.RemoveRange(20, s.PastAsks.Count - 20);
		}
	});

	private NpcRomanceState? Find(string npcName)
	{
		NpcRomanceSaveData? data = _store()?.Get(Path, Doc);
		return data?.Npcs.FirstOrDefault(s => string.Equals(s.Npc, npcName, StringComparison.OrdinalIgnoreCase));
	}

	private void Change(string npcName, Action<NpcRomanceState> change)
	{
		lock (_sync)
		{
			DocumentStore? store = _store();
			if (store == null || string.IsNullOrWhiteSpace(npcName))
			{
				return;
			}
			NpcRomanceSaveData data = store.Get(Path, Doc);
			NpcRomanceState? state = data.Npcs.FirstOrDefault(s => string.Equals(s.Npc, npcName, StringComparison.OrdinalIgnoreCase));
			if (state == null)
			{
				state = new NpcRomanceState { Npc = npcName.Trim() };
				data.Npcs.Add(state);
			}
			change(state);
			store.MarkDirty(Path);
		}
	}
}
