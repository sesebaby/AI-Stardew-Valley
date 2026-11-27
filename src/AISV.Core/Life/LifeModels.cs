using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AISV.Core.Storage;

namespace AISV.Core.Life;

/// <summary>心愿推进到某一步时托你帮的忙：送几样东西。</summary>
public sealed class LifeHelp
{
	/// <summary>推进到第几步（从 1 开始）时开口。</summary>
	public int Step { get; set; } = 1;

	public string Item { get; set; } = "";

	public int Amount { get; set; } = 1;

	/// <summary>开口说的话。</summary>
	public string Ask { get; set; } = "";

	/// <summary>收到时说的话。</summary>
	public string Thanks { get; set; } = "";
}

/// <summary>心愿达成时演的那一场。</summary>
public sealed class LifeEnding
{
	/// <summary>Town / Saloon / Beach / Mountain / Forest / ArchaeologyHouse。</summary>
	public string Location { get; set; } = "Town";

	/// <summary>到场的人（第一个是主角）。</summary>
	public List<string> Cast { get; set; } = new List<string>();

	/// <summary>主角站在哪一格（0 = 用这个地方默认的位置，见 <see cref="LifeRules.Stage"/>）。</summary>
	public int X { get; set; }

	public int Y { get; set; }

	/// <summary>摆在场上的道具图（assets/life/props/ 下的文件名，不带扩展名，或者"原版贴图@x,y,宽,高"）；空 = 不摆。</summary>
	public string Prop { get; set; } = "";

	/// <summary>道具底边正中对着主角那一格往右 PropDx、往下 PropDy 格（默认在主角身后一排）。</summary>
	public int PropDx { get; set; }

	public int PropDy { get; set; } = -1;

	/// <summary>AI 没写成时的台词（"谁|说什么|心情"）。</summary>
	public List<string> Lines { get; set; } = new List<string>();

	/// <summary>夹进日记的画（assets/life/diary/ 下的文件名）。</summary>
	public string Art { get; set; } = "";
}

/// <summary>一位村民的一个心愿（assets/life/goals.json）。</summary>
public sealed class LifeGoalDef
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	/// <summary>心愿本身（"办一次摄影展"）。</summary>
	public string Goal { get; set; } = "";

	/// <summary>一步步要做的事，最后一步就是达成。</summary>
	public List<string> Steps { get; set; } = new List<string>();

	public LifeHelp? Help { get; set; }

	public LifeEnding Ending { get; set; } = new LifeEnding();

	/// <summary>达成后换的工作（空 = 不换）。</summary>
	public string NewJob { get; set; } = "";

	public int NewWage { get; set; }

	/// <summary>最早从第几年开始。</summary>
	public int FromYear { get; set; } = 1;
}

public sealed class LifeCatalog
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public List<LifeGoalDef> Goals { get; set; } = new List<LifeGoalDef>();

	[JsonIgnore]
	public List<string> Problems { get; } = new List<string>();

	public LifeGoalDef? Get(string id) => Goals.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

	public static LifeCatalog Empty => new LifeCatalog();

	public static LifeCatalog Parse(string json)
	{
		LifeCatalog c = JsonSerializer.Deserialize<LifeCatalog>(json, Options) ?? new LifeCatalog();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (LifeGoalDef g in c.Goals)
		{
			if (g.Id.Length == 0 || !seen.Add(g.Id))
			{
				c.Problems.Add($"心愿 id 为空或重复：{g.Id}");
			}
			if (g.Steps.Count < 2)
			{
				c.Problems.Add($"{g.Id} 至少要两步。");
			}
			if (g.Help != null && (g.Help.Step < 1 || g.Help.Step >= g.Steps.Count || g.Help.Item.Length == 0))
			{
				c.Problems.Add($"{g.Id} 的帮忙写得不对（步数要在最后一步之前，要写东西）。");
			}
			if (g.Ending.Cast.Count == 0 || !string.Equals(g.Ending.Cast[0], g.Npc, StringComparison.OrdinalIgnoreCase))
			{
				c.Problems.Add($"{g.Id} 的结局第一个到场的人要是 {g.Npc}。");
			}
			foreach (string line in g.Ending.Lines.Where(l => l.Split('|').Length < 2))
			{
				c.Problems.Add($"{g.Id} 的台词格式是 谁|说什么|心情：{line}");
			}
		}
		return c;
	}
}

/// <summary>进行中或结束了的一个心愿。</summary>
public sealed class LifeGoal
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	/// <summary>做完了几步（0 = 刚开始）。</summary>
	public int Stage { get; set; }

	public int StartedDay { get; set; }

	public int LastStepDay { get; set; }

	/// <summary>"" 进行中 / done 达成 / dropped 放弃。</summary>
	public string Outcome { get; set; } = "";

	public int EndedDay { get; set; } = -1;

	/// <summary>你帮过几次。</summary>
	public int Helped { get; set; }

	/// <summary>托你帮忙的那件请求（物品请求的 id）；空 = 还没开口。</summary>
	public string HelpRequest { get; set; } = "";

	/// <summary>结局演过了。</summary>
	public bool EndingShown { get; set; }

	/// <summary>每一步发生时 AI 写的一句（或者目录里的那步）。</summary>
	public List<string> Notes { get; set; } = new List<string>();

	/// <summary>达成时 AI 写好的结局台词（"谁|说什么|心情"）；空 = 用目录里的。</summary>
	public List<string> Scene { get; set; } = new List<string>();

	[JsonIgnore]
	public bool Active => Outcome.Length == 0;
}

/// <summary>两位村民之间的感情（只记 mod 让它往前走的那些）。</summary>
public sealed class Couple
{
	public string A { get; set; } = "";

	public string B { get; set; } = "";

	/// <summary>dating / engaged / married / broken_up。</summary>
	public string Phase { get; set; } = CouplePhases.Dating;

	public int Since { get; set; }

	/// <summary>婚礼那天（订婚后定）。</summary>
	public int WeddingDay { get; set; } = -1;

	public bool WeddingShown { get; set; }

	/// <summary>下一次约会是哪天（-1 = 没约）、在哪（<see cref="LifeRules.DatePlaces"/> 的下标）。</summary>
	public int NextDate { get; set; } = -1;

	public int DatePlace { get; set; }

	public bool Has(string npc) => string.Equals(A, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(B, npc, StringComparison.OrdinalIgnoreCase);

	public string Other(string npc) => string.Equals(A, npc, StringComparison.OrdinalIgnoreCase) ? B : A;

	public bool Together => Phase is CouplePhases.Dating or CouplePhases.Engaged or CouplePhases.Married;
}

public static class CouplePhases
{
	public const string Dating = "dating";

	public const string Engaged = "engaged";

	public const string Married = "married";

	public const string BrokenUp = "broken_up";
}

/// <summary>换了工作。</summary>
public sealed class JobChange
{
	public string Npc { get; set; } = "";

	public string Job { get; set; } = "";

	public int Wage { get; set; }

	public int Since { get; set; }
}

/// <summary>村民自己的人生（town/life.json）。</summary>
public sealed class LifeBook
{
	public const string Path = "town/life.json";

	public static readonly DocumentType<LifeBook> Doc = new DocumentType<LifeBook>("村民的人生", 1, () => new LifeBook());

	/// <summary>上一次每周推进是哪天（-1 = 还没开始）。</summary>
	public int LastTick { get; set; } = -1;

	public List<LifeGoal> Goals { get; set; } = new List<LifeGoal>();

	public List<Couple> Couples { get; set; } = new List<Couple>();

	public List<JobChange> Jobs { get; set; } = new List<JobChange>();

	public LifeGoal? Active(string npc) => Goals.FirstOrDefault(g => g.Active && string.Equals(g.Npc, npc, StringComparison.OrdinalIgnoreCase));

	public Couple? CoupleOf(string npc) => Couples.FirstOrDefault(c => c.Together && c.Has(npc));

	public bool IsCouple(string a, string b) => Couples.Any(c => c.Together && c.Has(a) && c.Has(b));

	public bool IsWed(string npc) => Couples.Any(c => c.Phase == CouplePhases.Married && c.Has(npc));

	public JobChange? JobOf(string npc) => Jobs.FirstOrDefault(j => string.Equals(j.Npc, npc, StringComparison.OrdinalIgnoreCase));
}
