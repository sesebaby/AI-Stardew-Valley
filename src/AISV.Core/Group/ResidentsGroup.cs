using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Group;

public sealed class ResidentsMessage
{
	public string Speaker { get; set; } = "";

	public bool IsPlayer { get; set; }

	public string Text { get; set; } = "";

	public string Emotion { get; set; } = "neutral";

	public int Day { get; set; }

	public string Season { get; set; } = "";

	public int Year { get; set; }

	public int Time { get; set; }
}

public sealed class ResidentsState
{
	public List<ResidentsMessage> Messages { get; set; } = new List<ResidentsMessage>();

	public int PostsDay { get; set; } = -1;

	public int PostsToday { get; set; }

	public int NpcPlanDay { get; set; } = -1;

	public List<int> NpcPlannedTimes { get; set; } = new List<int>();
}

public enum ResidentsBlock
{
	None,
	Empty,
	TooLong,
	Busy,
	NoPostsLeft,
	AiOff,
	TooFewMembers
}

/// <summary>居民群的规则：存档、每天的条数、挑谁回、村民自己聊的时间表。</summary>
public static class ResidentsGroup
{
	public const string Path = "town/group_chat.json";

	public const string ContextType = "village-group";

	public const int MaxPostChars = 200;

	public const int MaxStored = 300;

	public const int CandidatePool = 5;

	public const int MaxReplies = 3;

	public const int MaxVillagerLines = 4;

	public const int MinMembers = 2;

	public const int VillagerChatMinMembers = 3;

	public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

	public static readonly DocumentType<ResidentsState> Doc = new DocumentType<ResidentsState>("居民群", 1, () => new ResidentsState());

	public static bool HasPhone(string? npc) => Messenger.MessengerRules.HasPhone(npc);

	public static int DailyLimit(int configured) => Math.Clamp(configured, 1, 20);

	public static int VillagerChatsPerDay(int configured) => Math.Clamp(configured, 0, 10);

	public static string Normalize(string? text) => string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	public static int PostsLeft(ResidentsState s, int today, int limit) => Math.Max(0, limit - (s.PostsDay == today ? s.PostsToday : 0));

	public static void CountPost(ResidentsState s, int today)
	{
		if (s.PostsDay != today)
		{
			s.PostsDay = today;
			s.PostsToday = 0;
		}
		s.PostsToday++;
	}

	public static ResidentsBlock Check(string? text, bool aiAvailable, bool busy, int postsLeft, int members)
	{
		string clean = Normalize(text);
		if (!aiAvailable)
		{
			return ResidentsBlock.AiOff;
		}
		if (clean.Length == 0)
		{
			return ResidentsBlock.Empty;
		}
		if (clean.Length > MaxPostChars)
		{
			return ResidentsBlock.TooLong;
		}
		if (busy)
		{
			return ResidentsBlock.Busy;
		}
		if (postsLeft <= 0)
		{
			return ResidentsBlock.NoPostsLeft;
		}
		return members < MinMembers ? ResidentsBlock.TooFewMembers : ResidentsBlock.None;
	}

	public static void Append(ResidentsState s, ResidentsMessage m)
	{
		s.Messages.Add(m);
		if (s.Messages.Count > MaxStored)
		{
			s.Messages.RemoveRange(0, s.Messages.Count - MaxStored);
		}
	}

	/// <summary>候选：点名的人 → 最近 8 条里说过话的两位 → 按心数加权随机补到 5 位。</summary>
	public static List<string> Candidates(string post, IReadOnlyList<(string Name, string Display, int Hearts)> members, IReadOnlyList<ResidentsMessage> history, Random random)
	{
		List<string> pool = new List<string>();
		void Add(string name)
		{
			if (pool.Count < CandidatePool && !pool.Contains(name, StringComparer.OrdinalIgnoreCase))
			{
				pool.Add(name);
			}
		}
		foreach (string name in GroupRules.Addressed(post, members.Select(m => (m.Name, m.Display))))
		{
			Add(name);
		}
		IEnumerable<string> recent = history.Reverse().Take(8).Where(m => !m.IsPlayer).Select(m => m.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).Take(2);
		foreach (string name in recent)
		{
			if (members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).Name is string found)
			{
				Add(found);
			}
		}
		List<(string Name, string Display, int Hearts)> rest = members.Where(m => !pool.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
		while (pool.Count < Math.Min(CandidatePool, members.Count) && rest.Count > 0)
		{
			double roll = random.NextDouble() * rest.Sum(m => 1.0 + m.Hearts);
			(string Name, string Display, int Hearts) pick = rest[^1];
			foreach (var m in rest)
			{
				roll -= 1.0 + m.Hearts;
				if (roll <= 0)
				{
					pick = m;
					break;
				}
			}
			rest.Remove(pick);
			Add(pick.Name);
		}
		return pool;
	}

	/// <summary>今天村民自己在群里聊的时间：8:00～21:00 随机，晚于现在，互相至少隔 1 小时。</summary>
	public static List<int> PlanTimes(int count, int now, Random random)
	{
		List<int> times = new List<int>();
		for (int i = 0; i < count * 6 && times.Count < count; i++)
		{
			int minutes = 8 * 60 + random.Next(13 * 60);
			int time = minutes / 60 * 100 + minutes % 60 / 10 * 10;
			if (time > now && times.All(t => Math.Abs(Minutes(t) - Minutes(time)) >= 60))
			{
				times.Add(time);
			}
		}
		times.Sort();
		return times;
	}

	/// <summary>村民自己聊起来的几个人：随机一位起头，再加两位和他有关系的，随机补到 4 位。</summary>
	public static List<string> VillagerPool(IReadOnlyList<string> members, Func<string, string, bool> related, Random random)
	{
		string starter = members[random.Next(members.Count)];
		List<string> pool = new List<string> { starter };
		pool.AddRange(members.Where(n => n != starter && related(starter, n)).OrderBy(_ => random.Next()).Take(2));
		foreach (string n in members.Where(n => !pool.Contains(n)).OrderBy(_ => random.Next()))
		{
			if (pool.Count >= 4)
			{
				break;
			}
			pool.Add(n);
		}
		return pool;
	}

	public static int Minutes(int time) => time / 100 * 60 + time % 100;
}
