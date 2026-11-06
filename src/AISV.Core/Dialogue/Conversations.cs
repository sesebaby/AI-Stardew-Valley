using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Dialogue;

/// <summary>游戏日期（季节用英文小写，和游戏一样）。</summary>
public readonly record struct GameDate(int Day, string Season, int Year)
{
	/// <summary>从第 1 年春 1 日算起的第几天（每季 28 天）。</summary>
	public int TotalDays => (Math.Max(1, Year) - 1) * 112 + SeasonIndex(Season) * 28 + Math.Clamp(Day, 1, 28);

	public bool SameDay(int day, string season, int year) => day == Day && year == Year && string.Equals(season, Season, StringComparison.OrdinalIgnoreCase);

	public static int SeasonIndex(string? season) => (season ?? "").Trim().ToLowerInvariant() switch
	{
		"summer" => 1,
		"fall" => 2,
		"winter" => 3,
		_ => 0
	};
}

/// <summary>一轮对话：村民说的、玩家回的、村民的反应。</summary>
public sealed class ConversationEntry
{
	public string Comeback { get; set; } = "";

	public List<string> Company { get; set; } = new List<string>();

	public string CompanySaid { get; set; } = "";

	public int Day { get; set; }

	public string Face { get; set; } = "neutral";

	public string Farmer { get; set; } = "";

	public string FarmerReply { get; set; } = "";

	public int HeartPoints { get; set; }

	public bool OffSite { get; set; }

	public string Opener { get; set; } = "";

	public string Season { get; set; } = "";

	/// <summary>daily / festival / messenger / village-group …</summary>
	public string Setting { get; set; } = "daily";

	public string SettingName { get; set; } = "";

	public string SettingRef { get; set; } = "";

	public int Year { get; set; }

	public bool IsOn(GameDate date) => date.SameDay(Day, Season, Year);

	public string ContextOrDaily => string.IsNullOrWhiteSpace(Setting) ? "daily" : Setting;

	/// <summary>群聊时同场的其他村民："A、B 和 C"。</summary>
	public string GroupCompany()
	{
		List<string> names = Company.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
		return names.Count switch
		{
			0 => "",
			1 => names[0],
			_ => string.Join("、", names.Take(names.Count - 1)) + " 和 " + names[^1]
		};
	}
}

/// <summary>和一位村民的全部对话记录。</summary>
public sealed class NpcConversationData
{
	/// <summary>今天头一回聊上时，离再上一次隔了几天；从没聊过是 -1。</summary>
	public int DaysSincePrior { get; set; } = -1;

	public List<int> HeartScenesSeen { get; set; } = new List<int>();

	public int LatestDay { get; set; }

	public string LatestSeason { get; set; } = "";

	public int LatestYear { get; set; }

	public string Npc { get; set; } = "";

	public string SavedOpener { get; set; } = "";

	/// <summary>存下来的"今天聊过"；只有 LastTalked 是今天时才算数（新的一天不用逐个清）。</summary>
	public bool SpokeToday { get; set; }

	/// <summary>上面那个计数是哪天的（TotalDays）；不是今天就当 0。</summary>
	public int SpouseChatsDay { get; set; }

	public int SpouseChatsToday { get; set; }

	/// <summary>剪掉的旧对话按年份和玩家记的总数，年度回顾照样数得到。</summary>
	public List<TalkTally> TrimmedTalks { get; set; } = new List<TalkTally>();

	public List<ConversationEntry> Turns { get; set; } = new List<ConversationEntry>();
}

public sealed class TalkTally
{
	public int Year { get; set; }

	public string PlayerName { get; set; } = "";

	public int Count { get; set; }

	/// <summary>其中加了好感的次数和点数。</summary>
	public int Warm { get; set; }

	public int WarmPoints { get; set; }
}

/// <summary>
/// 对话记录：每位村民一个文件（存档数据/talks/&lt;村民&gt;.json），用到才读，游戏保存时写。
/// </summary>
public sealed class ConversationLog
{
	private static readonly DocumentType<NpcConversationData> Doc = new DocumentType<NpcConversationData>("对话记录", 1, () => new NpcConversationData());

	private readonly Func<DocumentStore?> _store;

	private readonly Func<int> _maxHistory;

	// 生成在后台线程读、主线程写：所有读写都在这把锁里，读出去的是副本。
	private readonly object _sync = new object();

	public ConversationLog(Func<DocumentStore?> store, Func<int> maxHistoryPerNpc)
	{
		_store = store;
		_maxHistory = maxHistoryPerNpc;
	}

	/// <summary>原始数据（只在持有锁或主线程独占时用）。</summary>
	internal NpcConversationData Get(string npcName)
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return new NpcConversationData { Npc = npcName };
		}
		NpcConversationData data = store.Get(PathOf(npcName), Doc);
		if (string.IsNullOrEmpty(data.Npc))
		{
			data.Npc = npcName;
		}
		return data;
	}

	public IReadOnlyList<ConversationEntry> Recent(string npcName, int count = 10)
	{
		lock (_sync)
		{
			return Get(npcName).Turns.TakeLast(Math.Max(0, count)).ToList();
		}
	}

	/// <summary>有对话记录的村民（内部名）。</summary>
	public IReadOnlyList<string> Tracked()
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return Array.Empty<string>();
		}
		lock (_sync)
		{
			return store.List("talks").Select(path => Get(System.IO.Path.GetFileNameWithoutExtension(path)).Npc).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}
	}

	/// <summary>每一次聊天的年份、玩家和加的好感，包括已经剪掉的（按当年的平均好感补回）。</summary>
	public IReadOnlyList<(int Year, string PlayerName, int Points)> AllTalks(string npcName)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			List<(int, string, int)> list = new List<(int, string, int)>();
			foreach (TalkTally t in data.TrimmedTalks)
			{
				int avg = t.Warm > 0 ? (int)Math.Round((double)t.WarmPoints / t.Warm) : 0;
				list.AddRange(Enumerable.Repeat((t.Year, t.PlayerName, avg), t.Warm));
				list.AddRange(Enumerable.Repeat((t.Year, t.PlayerName, 0), Math.Max(0, t.Count - t.Warm)));
			}
			list.AddRange(data.Turns.Select(e => (e.Year, e.Farmer, e.HeartPoints)));
			return list;
		}
	}

	/// <summary>一共聊过几次，包括已经剪掉的。</summary>
	public int Count(string npcName)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			return data.Turns.Count + data.TrimmedTalks.Sum(t => t.Count);
		}
	}

	public IReadOnlyList<int> HeartEventsSeen(string npcName)
	{
		lock (_sync)
		{
			return Get(npcName).HeartScenesSeen.ToList();
		}
	}

	public void Add(string npcName, ConversationEntry entry)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			data.Turns.Add(entry);
			int max = Math.Max(1, _maxHistory());
			if (data.Turns.Count > max)
			{
				int cut = data.Turns.Count - max;
				foreach (ConversationEntry e in data.Turns.Take(cut))
				{
					TalkTally? t = data.TrimmedTalks.FirstOrDefault(x => x.Year == e.Year && string.Equals(x.PlayerName, e.Farmer, StringComparison.OrdinalIgnoreCase));
					if (t == null)
					{
						t = new TalkTally { Year = e.Year, PlayerName = e.Farmer };
						data.TrimmedTalks.Add(t);
					}
					t.Count++;
					if (e.HeartPoints > 0)
					{
						t.Warm++;
						t.WarmPoints += e.HeartPoints;
					}
				}
				data.Turns.RemoveRange(0, cut);
			}
			Touch(npcName);
		}
	}

	public void MarkTalkedToday(string npcName, GameDate today)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			if (!today.SameDay(data.LatestDay, data.LatestSeason, data.LatestYear))
			{
				data.DaysSincePrior = string.IsNullOrEmpty(data.LatestSeason) ? -1 : today.TotalDays - new GameDate(data.LatestDay, data.LatestSeason, data.LatestYear).TotalDays;
			}
			data.SpokeToday = true;
			data.LatestDay = today.Day;
			data.LatestSeason = today.Season;
			data.LatestYear = today.Year;
			Touch(npcName);
		}
	}

	public bool TalkedToday(string npcName, GameDate today)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			return data.SpokeToday && today.SameDay(data.LatestDay, data.LatestSeason, data.LatestYear);
		}
	}

	/// <summary>距上次聊天几天，今天已经聊上了就算今天之前那次；从没聊过返回 -1。</summary>
	public int DaysSinceLastTalk(string npcName, GameDate today)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			if (string.IsNullOrEmpty(data.LatestSeason))
			{
				return -1;
			}
			return today.SameDay(data.LatestDay, data.LatestSeason, data.LatestYear) ? data.DaysSincePrior : today.TotalDays - new GameDate(data.LatestDay, data.LatestSeason, data.LatestYear).TotalDays;
		}
	}

	public void SetCachedDialogue(string npcName, string dialogue)
	{
		lock (_sync)
		{
			Get(npcName).SavedOpener = dialogue;
			Touch(npcName);
		}
	}

	public void ClearCachedDialogue(string npcName)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			if (data.SavedOpener.Length > 0)
			{
				data.SavedOpener = "";
				Touch(npcName);
			}
		}
	}

	public int CompletedSpouseConversations(string npcName, GameDate today)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			return data.SpouseChatsDay == today.TotalDays ? Math.Max(0, data.SpouseChatsToday) : 0;
		}
	}

	public void RecordCompletedSpouseConversation(string npcName, GameDate today)
	{
		lock (_sync)
		{
			NpcConversationData data = Get(npcName);
			data.SpouseChatsToday = CompletedSpouseConversations(npcName, today) + 1;
			data.SpouseChatsDay = today.TotalDays;
			Touch(npcName);
		}
	}

	public void SetHeartEventsSeen(string npcName, IEnumerable<int> ids)
	{
		lock (_sync)
		{
			Get(npcName).HeartScenesSeen = ids.ToList();
			Touch(npcName);
		}
	}

	private void Touch(string npcName) => _store()?.MarkDirty(PathOf(npcName));

	internal static string PathOf(string npcName)
	{
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
		{
			npcName = npcName.Replace(c, '_');
		}
		return "talks/" + npcName + ".json";
	}
}
