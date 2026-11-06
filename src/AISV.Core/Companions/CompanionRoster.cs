using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Companions;

/// <summary>招募键按下时怎么办。</summary>
public enum GateDecision
{
	/// <summary>交给 Squad 照常走。</summary>
	Continue,
	RefuseChild,
	RefuseBusy,
	AskToPay
}

/// <summary>今天跟着农夫的人：请来干什么、付没付钱、几点加入的。每天开始清空。</summary>
public sealed class CompanionRoster
{
	private sealed record Job(string Task, int Day, int Price);

	private readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _paidDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _joinedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _sites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public void Clear()
	{
		_jobs.Clear();
		_paidDay.Clear();
		_joinedAt.Clear();
		_sites.Clear();
	}

	/// <summary>上门帮工的人：不跟着农夫，只在工作地点干约好的活。</summary>
	public bool IsSiteWorker(string npc) => _sites.Contains(npc);

	public void AddSite(string npc) => _sites.Add(npc);

	public void RemoveSite(string npc) => _sites.Remove(npc);

	public int? JobPrice(string npc, int today) => _jobs.TryGetValue(npc, out Job? j) && j.Day == today ? j.Price : null;

	public void SetJob(string npc, string task, int today, int price) => _jobs[npc] = new Job(task, today, price);

	public void RemoveJob(string npc) => _jobs.Remove(npc);

	public string? JobTask(string npc, int today) => _jobs.TryGetValue(npc, out Job? j) && j.Day == today ? j.Task : null;

	public void MarkPaid(string npc, int today) => _paidDay[npc] = today;

	public void ClearPaid(string npc) => _paidDay.Remove(npc);

	public bool PaidToday(string npc, int today) => _paidDay.TryGetValue(npc, out int day) && day == today;

	public void OnJoined(string npc, int time) => _joinedAt[npc] = time;

	public void OnLeft(string npc)
	{
		_jobs.Remove(npc);
		_joinedAt.Remove(npc);
	}

	public int? JoinedAt(string npc) => _joinedAt.TryGetValue(npc, out int t) ? t : null;

	/// <summary>招募键：孩子拒绝，有事拒绝，不到 4 心交给 Squad 说"还不熟"，要收钱又没付过的问一声，其他放行。</summary>
	public GateDecision Gate(string npc, bool child, bool busy, int hearts, bool partner, int today, out int price)
	{
		price = 0;
		if (child)
		{
			return GateDecision.RefuseChild;
		}
		if (busy)
		{
			return GateDecision.RefuseBusy;
		}
		if (!partner && hearts < OfferLedger.MinHearts(OfferLedger.Work))
		{
			return GateDecision.Continue;
		}
		price = OfferLedger.PriceFor(OfferLedger.Work, hearts, partner);
		return price <= 0 || PaidToday(npc, today) ? GateDecision.Continue : GateDecision.AskToPay;
	}

	/// <summary>约好的活 → Squad 的任务类型。"跟你去"就是一路帮着打怪。</summary>
	public static IReadOnlyList<string> TaskTypes(string task) => task switch
	{
		"water" => new[] { "Watering" },
		"harvest" => new[] { "Harvesting" },
		"forage" => new[] { "Foraging" },
		"clear" => new[] { "Lumbering", "Mining" },
		"animals" => new[] { "Petting", "Shearing", "Milking" },
		"fish" => new[] { "Fishing" },
		"mine" => new[] { "Mining", "Attacking" },
		"fight" => new[] { "Attacking" },
		OfferLedger.Follow => new[] { "Attacking" },
		_ => Array.Empty<string>()
	};

	/// <summary>今天约好的活一定能做（不受 Squad 的村民配置限制）；其他照 Squad 的规则。</summary>
	public bool? TaskOverride(string npc, string taskType, int today) => HasTask(npc, taskType, today) ? true : IsSiteWorker(npc) ? false : null;

	/// <summary>约好的活自己主动干；钓鱼和坐椅子只能跟着学。</summary>
	public string? ModeOverride(string npc, string taskType, int today) =>
		taskType is not ("Fishing" or "Sitting") && HasTask(npc, taskType, today) ? "Autonomous" : null;

	private bool HasTask(string npc, string taskType, int today) => JobTask(npc, today) is string task && TaskTypes(task).Contains(taskType);

	/// <summary>给 AI 的一行：这位村民正跟着农夫。</summary>
	public string PromptLine(string npc, int today)
	{
		if (IsSiteWorker(npc))
		{
			return "";
		}
		string since = JoinedAt(npc) is int t && t > 0 ? $"（{t / 100 % 24:D2}:{t % 100:D2} 起）" : "";
		_jobs.TryGetValue(npc, out Job? job);
		string what = job == null || job.Day != today ? "" : job.Task switch
		{
			"water" => "浇水",
			"harvest" => "收菜",
			"forage" => "采集",
			"clear" => "清理木头和石头",
			"animals" => "照看动物",
			"fish" => "钓鱼",
			"mine" => "挖矿",
			"fight" => "打怪",
			_ => ""
		};
		string hired = what.Length == 0 ? "" : job!.Price > 0 ? $"，农夫花 {job.Price}g 请你今天帮忙{what}" : $"，你自己说了今天帮忙{what}";
		return $"- 你这会儿正和农夫一块儿在外头{since}{hired}。今天他们走到哪你陪到哪，说话要像一路同行的人，不像刚碰上。";
	}
}
