using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;
using AISV.Core.Storage;

namespace AISV.Core.Social;

/// <summary>日期的几种写法。</summary>
public static class Dates
{
	/// <summary>记录里的"最后更新"：第 1 年春 5 日。</summary>
	public static string Label(GameDate d) => $"第 {d.Year} 年{HistoryFormatter.SeasonName(d.Season)} {d.Day} 日";

	public static int TotalDays(int day, string? season, int year) => (Math.Max(1, year) - 1) * 112 + GameDate.SeasonIndex(season) * 28 + day;
}

/// <summary>
/// 每位村民对农夫的情感档案：关键记忆、负面记忆、10 心以上的关系动态。
/// 存在一个文件里（bonds/feelings.json）。只在主线程用。
/// </summary>
public sealed class EmotionalMemory
{
	public const string Path = "bonds/feelings.json";

	public const int MaxKeyMemories = 5;

	private static readonly DocumentType<List<EmotionalProfile>> Doc = new DocumentType<List<EmotionalProfile>>("情感档案", 1, () => new List<EmotionalProfile>());

	private readonly Func<DocumentStore?> _store;

	private DocumentStore? _loadedFrom;

	private Dictionary<string, EmotionalProfile> _byName = new Dictionary<string, EmotionalProfile>(StringComparer.OrdinalIgnoreCase);

	public EmotionalMemory(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyCollection<EmotionalProfile> All => Profiles().Values;

	public EmotionalProfile? Find(string npc) => Profiles().TryGetValue(npc, out EmotionalProfile? p) ? p : null;

	public EmotionalProfile GetOrCreate(string npc)
	{
		Dictionary<string, EmotionalProfile> all = Profiles();
		if (!all.TryGetValue(npc, out EmotionalProfile? p))
		{
			p = new EmotionalProfile { Npc = npc };
			all[npc] = p;
			_store()?.Get(Path, Doc).Add(p);
		}
		return p;
	}

	/// <summary>记一条关键记忆（礼物、约会、场景、生日祝福等）：放最前，最多 5 条，同时记成"最近一件大事"。</summary>
	public void AddKeyMemory(string npc, string text, GameDate today)
	{
		EmotionalProfile p = GetOrCreate(npc);
		p.Recollections.Insert(0, text);
		while (p.Recollections.Count > MaxKeyMemories)
		{
			p.Recollections.RemoveAt(p.Recollections.Count - 1);
		}
		p.LatestMoment = text;
		p.TouchedOn = Dates.Label(today);
		Touch();
	}

	/// <summary>聊完一轮：10 心以上更新关系动态，再按这次的口气记或淡化负面记忆。</summary>
	public void AfterExchange(string npc, int hearts, NpcReaction reaction, string playerText, GameDate today)
	{
		EmotionalProfile p = GetOrCreate(npc);
		string label = Dates.Label(today);
		if (hearts >= 10)
		{
			p.Climate = Dynamics.AfterExchange(p.Climate, hearts, reaction, label);
		}
		NegativeMemories.AfterExchange(p.Sores, reaction, playerText, today.TotalDays);
		p.TouchedOn = label;
		Touch();
	}

	public void GiftReaction(string npc, string itemName, string taste, bool isBirthday, GameDate today)
	{
		if (!string.Equals(taste, "hated", StringComparison.OrdinalIgnoreCase) && !string.Equals(taste, "disliked", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		EmotionalProfile p = GetOrCreate(npc);
		NegativeMemories.Gift(p.Sores, itemName, taste, isBirthday, today.TotalDays);
		p.TouchedOn = Dates.Label(today);
		Touch();
	}

	public void BrokenPromise(string npc, string promise, GameDate today)
	{
		EmotionalProfile p = GetOrCreate(npc);
		NegativeMemories.BrokenPromise(p.Sores, promise, today.TotalDays);
		p.TouchedOn = Dates.Label(today);
		Touch();
	}

	/// <summary>每周 AI 评估的结果合进档案：信任、亲密、印象、关键记忆、最近大事整体替换；关系动态和负面记忆只在 AI 给了时替换。</summary>
	public void ApplyAnalysis(string npc, EmotionalProfile analyzed, GameDate today)
	{
		EmotionalProfile p = GetOrCreate(npc);
		p.FaithInFarmer = analyzed.FaithInFarmer;
		p.Affection = analyzed.Affection;
		p.Impression = analyzed.Impression ?? "";
		p.Recollections = analyzed.Recollections ?? new List<string>();
		p.LatestMoment = analyzed.LatestMoment ?? "";
		p.TouchedOn = Dates.Label(today);
		if (analyzed.Climate != null)
		{
			p.Climate = analyzed.Climate;
		}
		if (analyzed.Sores is { Count: > 0 })
		{
			p.Sores = analyzed.Sores;
		}
		p.Normalize();
		Touch();
	}

	/// <summary>对话提示词里的情感块：印象、信任、亲密、关键记忆（过滤掉只是在说村民爱好的）、关系动态、负面记忆。</summary>
	public string PromptBlock(string npc, string displayName, int hearts, int today)
	{
		EmotionalProfile? p = Find(npc);
		string dynamics = Dynamics.PromptBlock(hearts, p?.Climate);
		if (p == null)
		{
			return dynamics;
		}
		// 负面记忆会顺手清掉过期的：真的改了才标脏。
		int before = p.Sores.Count;
		string negative = NegativeMemories.PromptBlock(p.Sores, today);
		if (p.Sores.Count != before)
		{
			Touch();
		}
		string impression = PersonaPrompt.ProfileLine(npc, p.Impression, 160);
		List<string> memories = PersonaPrompt.FilterMemories(npc, p.Recollections, 3);
		string last = PersonaPrompt.ProfileLine(npc, p.LatestMoment, 220);
		StringBuilder sb = new StringBuilder();
		if (impression.Length > 0 || memories.Count > 0 || last.Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine($"【{displayName}对农夫的心思】这是你们之间的底色，拿捏亲疏、放不放心时参考，别当成话题抛出来。");
			sb.AppendLine("- 有多放心：" + TrustWord(p.FaithInFarmer));
			sb.AppendLine("- 有多亲近：" + WarmthWord(p.Affection));
			if (impression.Length > 0)
			{
				sb.AppendLine("- 心里给农夫的评语：" + impression);
			}
			if (memories.Count > 0)
			{
				sb.AppendLine("- 忘不了的事：");
				foreach (string m in memories)
				{
					sb.AppendLine("  - " + m);
				}
			}
			if (last.Length > 0)
			{
				sb.AppendLine("- 新近的一件事：" + last);
			}
		}
		sb.Append(dynamics);
		sb.Append(negative);
		return sb.ToString();
	}

	/// <summary>每周评估时给 AI 的上次档案（只当关系证据）。没有可用内容时为空。</summary>
	public string PreviousForAnalysis(string npc)
	{
		EmotionalProfile? p = Find(npc);
		if (p == null)
		{
			return "";
		}
		string impression = PersonaPrompt.ProfileLine(npc, p.Impression, 120);
		List<string> memories = PersonaPrompt.FilterMemories(npc, p.Recollections, 5);
		if (impression.Length == 0 && memories.Count == 0)
		{
			return "";
		}
		return $"\n之前一次的看法，细枝末节已经去掉，只拿来对照：信任 {p.FaithInFarmer:F2}，亲近 {p.Affection:F2}；那时眼里的农夫：{impression}；那时记得：{string.Join("；", memories)}\n";
	}

	public void Reset()
	{
		_loadedFrom = null;
		_byName = new Dictionary<string, EmotionalProfile>(StringComparer.OrdinalIgnoreCase);
	}

	private static string TrustWord(float t) => t >= 0.8f ? "打心底信得过" : t >= 0.6f ? "挺信任，也喜欢" : t >= 0.4f ? "还在慢慢熟" : t >= 0.2f ? "不大放心" : "处处提防";

	private static string WarmthWord(float w) => w >= 0.6f ? "喜欢得不得了" : w >= 0.3f ? "挺喜欢" : w >= -0.3f ? "说不上冷热" : w >= -0.6f ? "有点冷淡" : "很冷，不待见";

	private Dictionary<string, EmotionalProfile> Profiles()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return _byName;
		}
		if (!ReferenceEquals(store, _loadedFrom))
		{
			_loadedFrom = store;
			_byName = new Dictionary<string, EmotionalProfile>(StringComparer.OrdinalIgnoreCase);
			foreach (EmotionalProfile p in store.Get(Path, Doc))
			{
				if (!string.IsNullOrWhiteSpace(p.Npc))
				{
					p.Normalize();
					_byName[p.Npc] = p;
				}
			}
		}
		return _byName;
	}

	private void Touch()
	{
		DocumentStore? store = _store();
		if (store != null)
		{
			store.Get(Path, Doc);
			store.MarkDirty(Path);
		}
	}
}

/// <summary>一个关系里程碑。</summary>
public sealed class RelationshipMilestone
{
	[JsonPropertyName("day")]
	public int Day { get; set; }

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	[JsonPropertyName("note")]
	public string Note { get; set; } = "";

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("year")]
	public int Year { get; set; }
}

/// <summary>
/// 关系里程碑：第 1 / 10 / 50 次聊天、100 次以上、第一次送最爱的礼物、3 / 6 / 8 / 10 心、
/// 第 2 年起且聊过 5 次以上、第一次吵架（单次得分低于 10）。每种只记一次。提示词里放最近 6 条。
/// 存在 bonds/moments.json。
/// </summary>
public sealed class MilestoneBook
{
	public const string Path = "bonds/moments.json";

	public const int InPrompt = 6;

	private static readonly DocumentType<Dictionary<string, List<RelationshipMilestone>>> Doc = new DocumentType<Dictionary<string, List<RelationshipMilestone>>>("关系里程碑", 1, () => new Dictionary<string, List<RelationshipMilestone>>(StringComparer.OrdinalIgnoreCase));

	private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
	{
		["first_conversation"] = "头一回和农夫说上话",
		["10_conversations"] = "和农夫聊了 10 回，渐渐熟了",
		["50_conversations"] = "和农夫聊了 50 回，成了老相识",
		["100_conversations"] = "和农夫聊了不下百回，交情深厚",
		["first_loved_gift"] = "第一次收到农夫送的心头好",
		["3_hearts"] = "到 3 心，成了朋友",
		["6_hearts"] = "到 6 心，是很好的朋友了",
		["8_hearts"] = "到 8 心，彼此都不一般",
		["10_hearts"] = "到 10 心，亲密无间",
		["1_year"] = "认识满一年，农夫已经是山谷的一份子",
		["first_fight"] = "和农夫头一回闹别扭"
	};

	private readonly Func<DocumentStore?> _store;

	public MilestoneBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>所有村民的全部里程碑。</summary>
	public IEnumerable<(string Npc, RelationshipMilestone Milestone)> Everything() => All()?.Where(p => p.Value != null).SelectMany(p => p.Value.Where(m => m != null).Select(m => (p.Key, m))) ?? Enumerable.Empty<(string, RelationshipMilestone)>();

	public IReadOnlyList<RelationshipMilestone> For(string npc) => Find(npc) ?? (IReadOnlyList<RelationshipMilestone>)Array.Empty<RelationshipMilestone>();

	/// <summary>聊完一轮后检查（得分低于 10 时还会记"第一次吵架"）。</summary>
	public void AfterExchange(string npc, int conversationCount, int hearts, bool everGaveLovedGift, int rawPoints, GameDate today)
	{
		if (conversationCount == 1)
		{
			Add(npc, "first_conversation", today);
		}
		if (conversationCount == 10)
		{
			Add(npc, "10_conversations", today);
		}
		if (conversationCount == 50)
		{
			Add(npc, "50_conversations", today);
		}
		if (conversationCount >= 100)
		{
			Add(npc, "100_conversations", today);
		}
		if (everGaveLovedGift)
		{
			Add(npc, "first_loved_gift", today);
		}
		foreach (int h in new[] { 3, 6, 8, 10 })
		{
			if (hearts >= h)
			{
				Add(npc, $"{h}_hearts", today);
			}
		}
		if (today.Year >= 2 && conversationCount >= 5)
		{
			Add(npc, "1_year", today);
		}
		if (rawPoints < 10)
		{
			Add(npc, "first_fight", today);
		}
	}

	/// <summary>其他系统的里程碑（比如出游）：同一种只记一次。</summary>
	public void AddCustom(string npc, string type, string description, GameDate today)
	{
		if (!string.IsNullOrWhiteSpace(npc) && !string.IsNullOrWhiteSpace(type))
		{
			Add(npc, type.Trim(), today, string.IsNullOrWhiteSpace(description) ? type.Trim() : description.Trim());
		}
	}

	public string PromptBlock(string npc)
	{
		List<RelationshipMilestone>? list = Find(npc);
		if (list == null || list.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("\n【你们俩的几个日子】");
		foreach (RelationshipMilestone m in list.OrderBy(m => Dates.TotalDays(m.Day, m.Season, m.Year)).TakeLast(InPrompt))
		{
			sb.AppendLine($"- {Describe(m)}（第 {m.Year} 年{HistoryFormatter.SeasonName(m.Season)} {m.Day} 日）");
		}
		sb.AppendLine("话头碰上了提一句就好。");
		return sb.ToString();
	}

	/// <summary>内置种类用自己的说法，其他种类用记下的描述。</summary>
	public static string Describe(RelationshipMilestone m) => Descriptions.TryGetValue(m.Kind, out string? text) ? text : m.Note;

	private List<RelationshipMilestone>? Find(string npc)
	{
		Dictionary<string, List<RelationshipMilestone>>? all = All();
		return all == null ? null : Lookup(all, npc);
	}

	private Dictionary<string, List<RelationshipMilestone>>? All() => _store()?.Get(Path, Doc);

	// 从 JSON 读出来的字典区分大小写，村民名按不区分大小写找。
	private static List<RelationshipMilestone>? Lookup(Dictionary<string, List<RelationshipMilestone>> all, string npc)
	{
		if (all.TryGetValue(npc, out List<RelationshipMilestone>? exact))
		{
			return exact;
		}
		foreach ((string key, List<RelationshipMilestone> value) in all)
		{
			if (string.Equals(key, npc, StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}
		return null;
	}

	private void Add(string npc, string type, GameDate today, string? description = null)
	{
		Dictionary<string, List<RelationshipMilestone>>? all = All();
		if (all == null)
		{
			return;
		}
		List<RelationshipMilestone>? list = Lookup(all, npc);
		if (list == null)
		{
			list = new List<RelationshipMilestone>();
			all[npc] = list;
		}
		if (list.Any(m => m.Kind == type))
		{
			return;
		}
		list.Add(new RelationshipMilestone { Kind = type, Note = description ?? (Descriptions.TryGetValue(type, out string? d) ? d : type), Day = today.Day, Season = today.Season, Year = today.Year });
		_store()?.MarkDirty(Path);
	}
}
