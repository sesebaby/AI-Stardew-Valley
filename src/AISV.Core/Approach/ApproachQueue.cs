using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Approach;

public enum ApproachState
{
	PendingDialogue,
	ReadyToApproach,
	Approaching,
	Speaking,
	Returning,
	Restoring,
	Completed,
	Aborted
}

public enum EnqueueResult
{
	Accepted,
	Replaced,
	RejectedLowerPriority,
	RejectedFull,
	RejectedInvalid
}

public enum ApproachOutcome
{
	Spoken,
	Aborted,
	Rejected
}

public sealed record ApproachRequest(string RequestId, string NpcName, string DedupeKey, double Relevance, double DistanceTiles, int Priority, int CreatedTick, int TtlTicks)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(RequestId) && !string.IsNullOrWhiteSpace(NpcName) && !string.IsNullOrWhiteSpace(DedupeKey)
		&& double.IsFinite(Relevance) && double.IsFinite(DistanceTiles) && TtlTicks > 0;

	/// <summary>优先级 × 相关度 / (1 + 距离)。</summary>
	public double Score => Math.Max(0, Priority) * Math.Max(0, Relevance) / (1.0 + Math.Max(0, DistanceTiles));
}

public sealed class ApproachEntry
{
	internal ApproachEntry(ApproachRequest request, long sequence)
	{
		Request = request;
		Sequence = sequence;
	}

	public ApproachRequest Request { get; internal set; }

	public ApproachState State { get; internal set; }

	public string? Dialogue { get; internal set; }

	public bool AbortRequested { get; internal set; }

	public string AbortReason { get; internal set; } = "";

	public bool SpeechPresented { get; internal set; }

	public long Sequence { get; }

	public bool IsQueued => State is ApproachState.PendingDialogue or ApproachState.ReadyToApproach;

	public bool IsTerminal => State is ApproachState.Completed or ApproachState.Aborted;
}

/// <summary>
/// 村民走过来找玩家的排队：最多 6 个在排，两次出发至少隔 300 tick，超过 TTL 作废。
/// 去重键相同只留分高的；排满时挤掉分最低的。一次只有一个在走。
/// 结束（完成或取消）的请求放进 <see cref="TakeFinished"/>，由调用方在主线程统一回调。
/// </summary>
public sealed class ApproachQueue
{
	private readonly int _maxPending;

	private readonly int _cooldownTicks;

	private readonly Dictionary<string, ApproachEntry> _entries = new Dictionary<string, ApproachEntry>(StringComparer.OrdinalIgnoreCase);

	private readonly List<(ApproachEntry Entry, ApproachOutcome Outcome)> _finished = new List<(ApproachEntry, ApproachOutcome)>();

	private long _sequence;

	private int? _lastStarted;

	public ApproachQueue(int maxPending = 6, int cooldownTicks = 300)
	{
		_maxPending = Math.Max(1, maxPending);
		_cooldownTicks = Math.Max(0, cooldownTicks);
	}

	public string? ActiveId { get; private set; }

	public int PendingCount => _entries.Values.Count(e => e.IsQueued);

	public bool IsTracking(string npc) => _entries.Values.Any(e => !e.IsTerminal && string.Equals(e.Request.NpcName, npc, StringComparison.OrdinalIgnoreCase));

	public bool HasWork => _entries.Values.Any(e => !e.IsTerminal);

	public ApproachEntry? Get(string? id) => id != null && _entries.TryGetValue(id, out ApproachEntry? e) ? e : null;

	public IReadOnlyList<ApproachEntry> Queued => _entries.Values.Where(e => e.IsQueued)
		.OrderByDescending(e => e.Request.Score).ThenBy(e => e.Request.DistanceTiles).ThenBy(e => e.Sequence).ToList();

	public IReadOnlyList<ApproachEntry> Tracked => _entries.Values.Where(e => !e.IsTerminal).ToList();

	public EnqueueResult Enqueue(ApproachRequest request, int now)
	{
		Expire(now);
		if (!request.IsValid || _entries.ContainsKey(request.RequestId))
		{
			return EnqueueResult.RejectedInvalid;
		}
		if (Get(ActiveId) is ApproachEntry active && Same(active.Request.DedupeKey, request.DedupeKey))
		{
			return EnqueueResult.RejectedLowerPriority;
		}
		ApproachEntry? dup = _entries.Values.FirstOrDefault(e => e.IsQueued && Same(e.Request.DedupeKey, request.DedupeKey));
		if (dup != null)
		{
			if (!Outranks(request, dup.Request))
			{
				return EnqueueResult.RejectedLowerPriority;
			}
			Abort(dup.Request.RequestId, "被同类的新请求替换", now);
		}
		else if (PendingCount >= _maxPending)
		{
			ApproachEntry? weakest = _entries.Values.Where(e => e.IsQueued)
				.OrderBy(e => e.Request.Score).ThenByDescending(e => e.Request.DistanceTiles).ThenBy(e => e.Sequence).FirstOrDefault();
			if (weakest == null || !Outranks(request, weakest.Request))
			{
				return EnqueueResult.RejectedFull;
			}
			Abort(weakest.Request.RequestId, "被挤出队列", now);
		}
		_entries[request.RequestId] = new ApproachEntry(request, _sequence++);
		return dup != null ? EnqueueResult.Replaced : EnqueueResult.Accepted;
	}

	public bool MarkDialogueReady(string id, string dialogue, int now)
	{
		ApproachEntry? e = Get(id);
		if (string.IsNullOrWhiteSpace(dialogue) || e == null || e.State is not (ApproachState.PendingDialogue or ApproachState.Approaching))
		{
			return false;
		}
		if (e.State == ApproachState.PendingDialogue && Expired(e.Request, now))
		{
			Finish(e, ApproachState.Aborted, "过期");
			return false;
		}
		e.Dialogue = dialogue.Trim();
		if (e.State == ApproachState.PendingDialogue)
		{
			e.State = ApproachState.ReadyToApproach;
		}
		return true;
	}

	/// <summary>开始走：冷却到了、没人在走、请求还有效。</summary>
	public ApproachEntry? TryBegin(int now, string id)
	{
		Expire(now);
		if (ActiveId != null || (_lastStarted is int last && (uint)(now - last) < (uint)_cooldownTicks))
		{
			return null;
		}
		ApproachEntry? e = Get(id);
		if (e == null || !e.IsQueued || Expired(e.Request, now))
		{
			return null;
		}
		ActiveId = e.Request.RequestId;
		_lastStarted = now;
		e.State = ApproachState.Approaching;
		return e;
	}

	public bool MarkArrived(string id) => Move(id, ApproachState.Approaching, ApproachState.Speaking, requireDialogue: true);

	public void MarkPresented(string id)
	{
		if (Get(id) is ApproachEntry e)
		{
			e.SpeechPresented = true;
		}
	}

	public bool MarkSpeechDone(string id) => Move(id, ApproachState.Speaking, ApproachState.Returning);

	public bool MarkReturned(string id) => Move(id, ApproachState.Returning, ApproachState.Restoring);

	public bool MarkRestored(string id)
	{
		ApproachEntry? e = Get(id);
		if (e == null || ActiveId != e.Request.RequestId || e.State != ApproachState.Restoring)
		{
			return false;
		}
		ActiveId = null;
		Finish(e, e.AbortRequested ? ApproachState.Aborted : ApproachState.Completed, e.AbortReason);
		return true;
	}

	/// <summary>排着的直接取消；正在走或说话的转为往回走（由调用方放回原位后 <see cref="MarkRestored"/>）。</summary>
	public bool Abort(string id, string reason, int now)
	{
		ApproachEntry? e = Get(id);
		if (e == null || e.IsTerminal)
		{
			return false;
		}
		if (e.IsQueued)
		{
			Finish(e, ApproachState.Aborted, reason);
			return true;
		}
		e.AbortRequested = true;
		e.AbortReason = reason;
		if (e.State is ApproachState.Approaching or ApproachState.Speaking)
		{
			e.State = ApproachState.Returning;
		}
		return true;
	}

	/// <summary>正在走的那个直接结束（已经放回原位）。</summary>
	public void ForceFinishActive(string reason)
	{
		if (Get(ActiveId) is ApproachEntry e)
		{
			e.AbortRequested = true;
			ActiveId = null;
			Finish(e, ApproachState.Aborted, reason);
		}
	}

	public int Expire(int now)
	{
		int n = 0;
		foreach (ApproachEntry e in _entries.Values.Where(e => e.IsQueued && Expired(e.Request, now)).ToList())
		{
			Finish(e, ApproachState.Aborted, "过期");
			n++;
		}
		return n;
	}

	public void Clear(string reason)
	{
		foreach (ApproachEntry e in _entries.Values.Where(e => !e.IsTerminal).ToList())
		{
			Finish(e, ApproachState.Aborted, reason);
		}
		_entries.Clear();
		ActiveId = null;
		_lastStarted = null;
		_sequence = 0;
	}

	/// <summary>取走已经结束的请求（同时从表里删掉）。</summary>
	public List<(ApproachEntry Entry, ApproachOutcome Outcome)> TakeFinished()
	{
		List<(ApproachEntry, ApproachOutcome)> list = _finished.ToList();
		_finished.Clear();
		foreach ((ApproachEntry e, _) in list)
		{
			_entries.Remove(e.Request.RequestId);
		}
		return list;
	}

	/// <summary>同去重键时谁留下：分高的；同分比优先级；再同比距离近的。</summary>
	public static bool Outranks(ApproachRequest a, ApproachRequest b)
	{
		double d = a.Score - b.Score;
		if (Math.Abs(d) >= 1e-6)
		{
			return d > 0;
		}
		return a.Priority != b.Priority ? a.Priority > b.Priority : a.DistanceTiles < b.DistanceTiles;
	}

	private bool Move(string id, ApproachState from, ApproachState to, bool requireDialogue = false)
	{
		ApproachEntry? e = Get(id);
		if (e == null || ActiveId != e.Request.RequestId || e.State != from || (requireDialogue && string.IsNullOrWhiteSpace(e.Dialogue)))
		{
			return false;
		}
		e.State = to;
		return true;
	}

	private void Finish(ApproachEntry e, ApproachState state, string reason)
	{
		if (e.IsTerminal)
		{
			return;
		}
		e.State = state;
		if (state == ApproachState.Aborted)
		{
			e.AbortRequested = true;
			e.AbortReason = reason;
		}
		_finished.Add((e, e.SpeechPresented ? ApproachOutcome.Spoken : ApproachOutcome.Aborted));
	}

	private static bool Expired(ApproachRequest r, int now) => (uint)(now - r.CreatedTick) >= (uint)Math.Max(0, r.TtlTicks);

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>走向玩家时的寻路判断：超时、重找路冷却、卡住重找、最多找几次。</summary>
public sealed class ApproachNavigation
{
	public const int TimeoutTicks = 600;

	public const int RepathCooldownTicks = 45;

	public const int StuckTicks = 30;

	public const int MaxPathStarts = 5;

	private int? _started;

	private int? _lastPath;

	private int _paths;

	private int _noProgress;

	private (int X, int Y)? _lastTile;

	private int? _lastDistance;

	public enum Decision
	{
		Arrived,
		Wait,
		StartPath,
		Repath,
		Failed
	}

	public string FailReason { get; private set; } = "";

	/// <param name="pathActive">我们的寻路控制器还在跑。</param>
	/// <param name="activeTarget">它正走向的格子。</param>
	public Decision Evaluate(int tick, (int X, int Y) npc, (int X, int Y) target, bool pathActive, (int X, int Y)? activeTarget)
	{
		_started ??= tick;
		int dist = Math.Abs(npc.X - target.X) + Math.Abs(npc.Y - target.Y);
		if (_lastDistance is int ld && _lastTile != null && ld - dist >= 1)
		{
			_noProgress = 0;
		}
		else if (_lastTile != null && _lastTile != npc)
		{
			_noProgress = Math.Max(0, _noProgress - 1);
		}
		else if (_lastTile != null)
		{
			_noProgress++;
		}
		_lastTile = npc;
		_lastDistance = dist;
		if (dist <= 0)
		{
			return Decision.Arrived;
		}
		if ((uint)(tick - _started.Value) >= TimeoutTicks)
		{
			FailReason = "超时";
			return Decision.Failed;
		}
		bool stuck = _noProgress >= StuckTicks;
		if (pathActive && activeTarget == target && !stuck)
		{
			return Decision.Wait;
		}
		if (_paths >= MaxPathStarts)
		{
			FailReason = "找路次数用完";
			return Decision.Failed;
		}
		if (_lastPath is int lp && (uint)(tick - lp) < RepathCooldownTicks)
		{
			FailReason = "冷却中";
			return Decision.Wait;
		}
		Decision d = !pathActive && !stuck ? Decision.StartPath : Decision.Repath;
		_lastPath = tick;
		_paths++;
		_noProgress = 0;
		return d;
	}

	/// <summary>玩家四周能站的格子：优先村民已经站着的，再按离村民近。</summary>
	public static (int X, int Y)? AdjacentTile((int X, int Y) player, (int X, int Y) npc, Func<(int X, int Y), bool> safe)
	{
		(int, int)[] offsets = { (0, -1), (1, 0), (0, 1), (-1, 0) };
		foreach ((int X, int Y) t in offsets.Select(o => (X: player.X + o.Item1, Y: player.Y + o.Item2))
			.OrderBy(t => t == npc ? 0 : 1).ThenBy(t => Math.Abs(t.X - npc.X) + Math.Abs(t.Y - npc.Y)))
		{
			if (safe(t))
			{
				return t;
			}
		}
		return null;
	}
}
