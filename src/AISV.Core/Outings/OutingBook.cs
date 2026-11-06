using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Dialogue;
using AISV.Core.Storage;

namespace AISV.Core.Outings;

/// <summary>
/// 邀约记录（plans/outings.json）：谁答应了去哪、冷却到哪天、场景播了没有。
/// 提示词在后台线程读，其余在主线程改；都在锁里。
/// </summary>
public sealed class OutingBook
{
	public const string Path = "plans/outings.json";

	/// <summary>答应以后 7 天还没去就算过期。</summary>
	public const int ExpiryDays = 7;

	private static readonly DocumentType<List<OutingRecord>> Doc = new DocumentType<List<OutingRecord>>("邀约外出", 1, () => new List<OutingRecord>());

	private readonly Func<DocumentStore?> _store;

	private readonly object _sync = new object();

	public OutingBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<OutingRecord> All
	{
		get
		{
			lock (_sync)
			{
				return List()?.Select(r => r.Copy()).ToList() ?? new List<OutingRecord>();
			}
		}
	}

	public IReadOnlyList<OutingRecord> Waiting => All.Where(r => r.Waiting).OrderBy(r => r.AskedOn).ThenBy(r => r.Npc, StringComparer.OrdinalIgnoreCase).ToList();

	public IReadOnlyList<OutingRecord> CompletedOn(int today) => All.Where(r => Same(r.State, OutingState.Completed) && r.PlayedOn == today)
		.OrderBy(r => r.AskedOn).ThenBy(r => r.Npc, StringComparer.OrdinalIgnoreCase).ToList();

	public OutingRecord? Find(string? id) => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(r => Same(r.Id, id));

	public bool HasWaiting(string npc) => All.Any(r => r.Waiting && Same(r.Npc, npc));

	public bool OnCooldown(string npc, int today) => All.Any(r => Same(r.Npc, npc) && r.RestUntil > today);

	/// <summary>
	/// 记下 AI 的答复。答应时按答复选出游、拼脚本；选不出来就改成拒绝（同时改掉 <paramref name="d"/>）。
	/// <paramref name="cooldownDays"/>：AI 没给冷却时用的设置值。
	/// </summary>
	public OutingRecord Record(string npc, string playerMessage, OutingDecision d, OutingAsk ask, OutingIntent intent, OutingCatalog catalog, int cooldownDays, int today, Random random)
	{
		int cooldown = Math.Clamp(d.BackOffDays <= 0 ? Math.Clamp(cooldownDays, 0, 28) : d.BackOffDays, 0, 28);
		OutingRecord r = new OutingRecord
		{
			Id = NewId(npc, today),
			Npc = npc,
			Asked = OutingAi.Clean(playerMessage, 240),
			Agreed = d.Agrees,
			Said = OutingAi.Clean(d.Says, 280),
			Why = OutingAi.Clean(d.Why, 280),
			Feeling = Emotion.Normalize(d.Feeling),
			Warmth = d.Warmth,
			Tone = d.Tone,
			WorthTelling = d.WorthTelling,
			ToRemember = OutingAi.Clean(d.ToRemember, 280),
			LineDrawn = string.IsNullOrWhiteSpace(d.LineDrawn) ? null : OutingAi.Clean(d.LineDrawn, 180),
			WantedPlace = intent.HasLocation ? intent.RequestedLocation : "",
			WantedFrom = intent.TimeMin,
			WantedTo = intent.TimeMax,
			MatchedWish = intent.HasConstraints,
			AskedOn = today,
			RestUntil = cooldown <= 0 ? today : today + cooldown,
			State = d.Agrees ? OutingState.Pending : OutingState.Declined
		};
		if (d.Agrees)
		{
			OutingPlan? plan = catalog.Pick(d, ask, intent, random);
			if (plan == null)
			{
				Decline(r, d);
			}
			else
			{
				r.Tone = plan.Type;
				r.OutingId = plan.Id;
				r.Place = plan.Location;
				r.FromTime = plan.TimeMin;
				r.ToTime = plan.TimeMax;
				r.Script = OutingCatalog.Script(plan, r.Id, npc, r.Said, d.Lines);
			}
		}
		lock (_sync)
		{
			List()?.Add(r);
			Dirty();
		}
		return r.Copy();
	}

	/// <summary>想去但没有对得上的出游：改成拒绝，分数最多 5。</summary>
	private static void Decline(OutingRecord r, OutingDecision d)
	{
		r.Agreed = false;
		r.State = OutingState.Declined;
		if (string.IsNullOrWhiteSpace(r.Why))
		{
			r.Why = "没有对得上地点和时间的出游。";
		}
		r.Tone = r.OutingId = r.Place = r.Script = "";
		r.FromTime = r.ToTime = 0;
		d.Agrees = false;
		d.ClearScene();
		d.Why = r.Why;
		d.Warmth = Math.Min(d.Warmth, 5);
		d.Says = AISV.Core.Text.Tr.Get("outing.noScene", "我是想去的，可那个地方、那个时间今天不太行。");
	}

	/// <summary>后台写好的台词回来了：重拼脚本（台词为空时保留原来的）。返回还在等的那条。</summary>
	public OutingRecord? ApplyLines(string id, IReadOnlyList<string>? lines, OutingCatalog catalog)
	{
		lock (_sync)
		{
			OutingRecord? r = List()?.FirstOrDefault(x => Same(x.Id, id));
			if (r == null || !r.Waiting)
			{
				return null;
			}
			OutingPlan? plan = catalog.Find(r.OutingId);
			if (lines != null && plan != null)
			{
				r.Script = OutingCatalog.Script(plan, r.Id, r.Npc, r.Said, lines);
				Dirty();
			}
			return r.Copy();
		}
	}

	/// <summary>开演：标记完成。返回这条（找不到为 null）。</summary>
	public OutingRecord? Complete(string id, int today)
	{
		lock (_sync)
		{
			OutingRecord? r = List()?.FirstOrDefault(x => Same(x.Id, id));
			if (r == null)
			{
				return null;
			}
			r.Played = true;
			r.PlayedOn = today;
			r.State = OutingState.Completed;
			Dirty();
			return r.Copy();
		}
	}

	public void SetMemory(string id, string memory)
	{
		lock (_sync)
		{
			OutingRecord? r = List()?.FirstOrDefault(x => Same(x.Id, id));
			if (r != null)
			{
				r.ToRemember = memory;
				Dirty();
			}
		}
	}

	/// <summary>场景放不下：取消这次出游。</summary>
	public OutingRecord? Cancel(string id)
	{
		lock (_sync)
		{
			OutingRecord? r = List()?.FirstOrDefault(x => Same(x.Id, id));
			if (r == null)
			{
				return null;
			}
			r.Agreed = false;
			r.State = OutingState.Cancelled;
			r.Script = "";
				if (string.IsNullOrWhiteSpace(r.Why))
			{
				r.Why = "场景找不到安全的站位。";
			}
			Dirty();
			return r.Copy();
		}
	}

	/// <summary>每天开始：答应超过 7 天还没去的算过期。返回过期的。</summary>
	public IReadOnlyList<OutingRecord> Expire(int today)
	{
		lock (_sync)
		{
			List<OutingRecord> expired = List()?.Where(r => r.Waiting && today > r.AskedOn + ExpiryDays).ToList() ?? new List<OutingRecord>();
			foreach (OutingRecord r in expired)
			{
				r.State = OutingState.Expired;
			}
			if (expired.Count > 0)
			{
				Dirty();
			}
			return expired.Select(r => r.Copy()).ToList();
		}
	}

	/// <summary>给邀约判定看的：等着去的那次，和最近 14 天最多 3 条。</summary>
	public string PromptBlock(string npc, int today)
	{
		if (string.IsNullOrWhiteSpace(npc))
		{
			return "";
		}
		IReadOnlyList<OutingRecord> mine = All.Where(r => Same(r.Npc, npc)).ToList();
		OutingRecord? waiting = mine.FirstOrDefault(r => r.Waiting);
		List<OutingRecord> recent = mine.Where(r => today - r.AskedOn <= 14).OrderByDescending(r => r.AskedOn).Take(3).ToList();
		if (waiting == null && recent.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder("【你和农夫之前的邀约】\n");
		if (waiting != null)
		{
			sb.AppendLine($"已经答应、还没去的一次：{waiting.OutingId}，在 {waiting.Place}，{waiting.FromTime}-{waiting.ToTime}。这次没去成之前，别再答应别的出游。");
		}
		foreach (OutingRecord r in recent)
		{
			string answer = r.Agreed ? "答应了" : "没答应";
			string memory = string.IsNullOrWhiteSpace(r.ToRemember) ? "" : $"；记得：{r.ToRemember}";
			sb.AppendLine($"- 第 {r.AskedOn} 天：{answer}（{StateName(r.State)}）{(r.Tone.Length > 0 ? "，" + OutingAi.ToneName(r.Tone) : "")}{memory}；冷却到第 {r.RestUntil} 天。");
		}
		return sb.ToString().TrimEnd();
	}

	public static string StateName(string state) => state switch
	{
		OutingState.Pending => "等着去",
		OutingState.Completed => "去过了",
		OutingState.Expired => "没去成，过期了",
		OutingState.Cancelled => "取消了",
		_ => "拒绝了"
	};

	public static string NewId(string npc, int today)
	{
		string safe = new string((npc ?? "").Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray()).Trim('_');
		if (safe.Length == 0)
		{
			safe = "unknown";
		}
		return $"aiout-{(safe.Length > 18 ? safe[..18] : safe)}-{today}-{Guid.NewGuid().ToString("N")[..8]}";
	}

	private List<OutingRecord>? List()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return null;
		}
		return store.Get(Path, Doc);
	}

	private void Dirty() => _store()?.MarkDirty(Path);

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
