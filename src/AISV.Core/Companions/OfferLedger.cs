using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;

namespace AISV.Core.Companions;

/// <summary>规则层收下的一条提议，等玩家确认。</summary>
public sealed class PendingOffer
{
	public string Id { get; init; } = "";

	public string Npc { get; init; } = "";

	public string Type { get; init; } = "";

	public string Task { get; init; } = "";

	public string Summary { get; init; } = "";

	public string When { get; init; } = "";

	public string Where { get; init; } = "";

	public string Time { get; init; } = "";

	public int CreatedDay { get; init; }

	public int ExpiresDay { get; init; }

	/// <summary>收费（金币），0 是免费。按好感算，不让 AI 定价。</summary>
	public int Price { get; init; }
}

/// <summary>
/// AI 只能提议，这里决定收不收：类型白名单、好感门槛、孩子保护、每人每天一条、同类冷却、过期。
/// 不改钱、物品和存档；账本只在内存里。
/// </summary>
public sealed class OfferLedger
{
	public const string Follow = "follow";
	public const string Work = "work";
	public const string Meetup = "meetup";
	public const string Quest = "quest";

	/// <summary>规则层拒绝的原因里认得出的词（决定怎么跟玩家说）。</summary>
	public const string HeartsWord = "心，现在";
	public const string CooldownWord = "冷却";
	public const string ProposedWord = "今天已经提过";

	private const int MaxSummary = 120;

	private const int SameTypeCooldownDays = 3;

	public static readonly IReadOnlyList<string> Types = new[] { Follow, Work, Meetup, Quest };

	public static readonly IReadOnlyList<string> WorkTasks = new[] { "water", "harvest", "forage", "clear", "animals", "fish", "mine", "fight" };

	private readonly List<PendingOffer> _pending = new List<PendingOffer>();

	private readonly Dictionary<string, int> _lastDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _lastTypeDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public int AcceptedCount { get; private set; }

	public int RejectedCount { get; private set; }

	public IReadOnlyList<PendingOffer> Pending(int today)
	{
		_pending.RemoveAll(p => p.ExpiresDay < today);
		return _pending.ToList();
	}

	/// <summary>玩家答复过的提议拿掉；冷却和"每天一条"的记录留着。</summary>
	public void Resolve(string id) => _pending.RemoveAll(p => p.Id == id);

	public void Clear()
	{
		_pending.Clear();
		_lastDay.Clear();
		_lastTypeDay.Clear();
		AcceptedCount = 0;
		RejectedCount = 0;
	}

	/// <summary>审一条提议：收下就返回，不合规返回空并给出原因。</summary>
	public bool ProposedToday(string npc, int today) => _lastDay.TryGetValue(npc, out int last) && last == today;

	public PendingOffer? Review(string npc, NpcProposal? proposal, int today, int hearts, bool isPartner, bool isChild, out string reason)
	{
		reason = "";
		if (proposal == null)
		{
			reason = "没有提议";
			return null;
		}
		string type = (proposal.Type ?? "").Trim().ToLowerInvariant();
		string task = (proposal.Task ?? "").Trim().ToLowerInvariant();
		string summary = (proposal.Summary ?? "").Trim();
		if (!Types.Contains(type))
		{
			return Reject($"不认识的类型 {type}", out reason);
		}
		if (summary.Length == 0)
		{
			return Reject("摘要是空的", out reason);
		}
		if (type == Work && !WorkTasks.Contains(task))
		{
			return Reject($"不认识的活 {task}", out reason);
		}
		if (isChild && type != Quest)
		{
			return Reject($"孩子不能提 {type}", out reason);
		}
		int needed = MinHearts(type);
		if (!isPartner && hearts < needed)
		{
			return Reject($"{type} 要 {needed} 心，现在 {hearts} 心", out reason);
		}
		if (_lastDay.TryGetValue(npc, out int last) && last == today)
		{
			return Reject("今天已经提过了", out reason);
		}
		if (_lastTypeDay.TryGetValue(npc + "|" + type, out int lastType) && today >= lastType && today - lastType < SameTypeCooldownDays)
		{
			return Reject($"{type} 还在冷却（{today - lastType}/{SameTypeCooldownDays} 天）", out reason);
		}
		string id = $"{npc}:{type}:{today}";
		if (_pending.Any(p => p.Id == id))
		{
			return Reject("重复了", out reason);
		}
		PendingOffer offer = new PendingOffer
		{
			Id = id,
			Npc = npc,
			Type = type,
			Task = task,
			Summary = summary.Length > MaxSummary ? summary[..(char.IsHighSurrogate(summary[MaxSummary - 1]) ? MaxSummary - 1 : MaxSummary)] : summary,
			When = (proposal.When ?? "").Trim(),
			Where = (proposal.Where ?? "").Trim(),
			Time = (proposal.Time ?? "").Trim(),
			CreatedDay = today,
			ExpiresDay = type is Follow or Work ? today + (MentionsTomorrow(proposal.When) ? 1 : 0) : today + 3,
			Price = PriceFor(type, hearts, isPartner)
		};
		_pending.Add(offer);
		_lastDay[npc] = today;
		_lastTypeDay[npc + "|" + type] = today;
		AcceptedCount++;
		return offer;
	}

	public static int MinHearts(string type) => type switch
	{
		Work => 4,
		Follow => 3,
		Meetup => 2,
		_ => 0
	};

	/// <summary>帮工收费：配偶恋人或 8 心以上免费；4 心 600g，每多 1 心便宜 100g；跟随不收费。</summary>
	public static int PriceFor(string type, int hearts, bool isPartner)
	{
		if (type != Work || isPartner || hearts >= 8)
		{
			return 0;
		}
		return Math.Max(0, 600 - (Math.Max(4, hearts) - 4) * 100);
	}

	private static bool MentionsTomorrow(string? when)
	{
		string w = (when ?? "").ToLowerInvariant();
		return w.Contains("明天") || w.Contains("明早") || w.Contains("tomorrow");
	}

	private PendingOffer? Reject(string why, out string reason)
	{
		reason = why;
		RejectedCount++;
		return null;
	}
}
