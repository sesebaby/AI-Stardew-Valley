using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Prompts;

namespace AISV.Core.Group;

public readonly record struct GroupSpot(string Name, float X, float Y);

public static class GroupRules
{
	public const int EntryDelayTicks = 90;

	public const float PairTiles = 6f;

	public const float LeadTiles = 20f;

	public const float OtherStaysTiles = 14f;

	public const int RecentTalks = 3;

	public const int GossipPerMember = 2;

	public const int AmbientCooldownMinutes = 120;

	public const float TogetherTiles = 3f;

	public const float JoinTiles = 2.5f;

	public const float HearTiles = 14f;

	public const float HintTiles = 8f;

	public const float KeyStartTiles = 4f;

	public const float ClickTiles = 3f;

	public const int NextRoundTicks = 60;

	public const int NextRoundAfterFarmerTicks = 420;

	public const string ListenedOnly = "[听了大家聊天，没有插话]";

	public static int MaxMembers(int configured) => Math.Clamp(configured, 2, 6);

	public static int Replies(int configured) => Math.Clamp(configured, 1, 50);

	public static int AmbientRounds(int configured) => Math.Clamp(configured, 1, 8);

	/// <summary>进门时挑一对：两人相距 6 格内、离农夫较近的人 20 格内；相距 + 0.5×离农夫距离 最小，离农夫近的那位带头。</summary>
	public static (GroupSpot Lead, GroupSpot Other)? PickPair(IReadOnlyList<GroupSpot> spots, float px, float py)
	{
		(GroupSpot, GroupSpot)? best = null;
		float bestScore = float.MaxValue;
		for (int i = 0; i < spots.Count; i++)
		{
			for (int j = i + 1; j < spots.Count; j++)
			{
				GroupSpot a = spots[i], b = spots[j];
				if (string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				float apart = Distance(a.X, a.Y, b.X, b.Y);
				float da = Distance(a.X, a.Y, px, py), db = Distance(b.X, b.Y, px, py);
				float near = Math.Min(da, db);
				if (apart > PairTiles || near > LeadTiles)
				{
					continue;
				}
				float score = apart + near * 0.5f;
				if (score < bestScore)
				{
					bestScore = score;
					best = da <= db ? (a, b) : (b, a);
				}
			}
		}
		return best;
	}

	/// <summary>农夫的话里点到的人（内部名或显示名）。</summary>
	public static List<string> Addressed(string text, IEnumerable<(string Name, string Display)> members) => members
		.Where(m => text.Contains(m.Name, StringComparison.OrdinalIgnoreCase) || (m.Display.Length > 0 && text.Contains(m.Display, StringComparison.OrdinalIgnoreCase)))
		.Select(m => m.Name)
		.ToList();

	/// <summary>"最近聊过的"一行；农夫没说话或是标记（[…]）的不算。</summary>
	public static string DescribeTalk(ConversationEntry e)
	{
		string said = Snippet(e.FarmerReply);
		if (said.Length == 0 || said.StartsWith('['))
		{
			return "";
		}
		string reply = Snippet(e.Comeback);
		string when = $"{HistoryFormatter.SeasonName(e.Season)} {e.Day} 日";
		return reply.Length == 0 ? $"{when}，农夫：「{said}」" : $"{when}，农夫：「{said}」 你：「{reply}」";
	}

	/// <summary>一段气泡停留多久：中文每字约 0.14 秒、其他每字约 0.055 秒，2.2～6.5 秒。</summary>
	public static double ReadingMs(string text, bool cjk) => Math.Clamp(1200.0 + text.Length * (cjk ? 140.0 : 55.0), 2200.0, 6500.0);

	/// <summary>居民群里一条消息"打字"多久。</summary>
	public static TimeSpan TypingDelay(int chars) => TimeSpan.FromMilliseconds(Math.Clamp(1400 + chars * 55, 1600, 5000));

	/// <summary>按长度切成几段气泡，尽量在标点处断开。</summary>
	public static List<string> SplitBubbles(string text, int max)
	{
		List<string> parts = new List<string>();
		string rest = text.Trim();
		while (rest.Length > max)
		{
			int cut = -1;
			for (int i = Math.Min(max, rest.Length - 1); i >= max / 2; i--)
			{
				if ("。！？!?，,；;… ".IndexOf(rest[i]) >= 0)
				{
					cut = i + 1;
					break;
				}
			}
			if (cut <= 0)
			{
				cut = max;
			}
			parts.Add(rest.Substring(0, cut).Trim());
			rest = rest.Substring(cut).Trim();
		}
		if (rest.Length > 0)
		{
			parts.Add(rest);
		}
		return parts;
	}

	private static string Snippet(string? text)
	{
		string t = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		return t.Length <= 110 ? t : PromptText.Truncate(t, 110) + "…";
	}

	private static float Distance(float x1, float y1, float x2, float y2) => MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}

/// <summary>
/// 村民自己闲聊时，偶尔从农夫最近的事聊起：只挑这两天的新鲜事，四成机会；
/// 同一件事在同一个渠道只聊一次，每个渠道一天最多一次。
/// </summary>
public sealed class FarmerTopics
{
	public const int FreshDays = 2;

	public const double Chance = 0.4;

	private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

	private readonly Dictionary<string, int> _lastDay = new Dictionary<string, int>(StringComparer.Ordinal);

	/// <summary>调试用：有新鲜事就一定聊，不看机会和每天一次的冷却（同一件事仍不重复）。</summary>
	public bool Always { get; set; }

	public string Pick(string channel, int today, IEnumerable<(string Id, string Text)> candidates, double roll)
	{
		if (!Always && _lastDay.TryGetValue(channel, out int day) && day == today)
		{
			return "";
		}
		(string Id, string Text) pick = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Text) && !_used.Contains(channel + "|" + c.Id));
		if (pick.Id == null || (!Always && roll >= Chance))
		{
			return "";
		}
		_used.Add(channel + "|" + pick.Id);
		_lastDay[channel] = today;
		return pick.Text;
	}

	public void Clear()
	{
		_used.Clear();
		_lastDay.Clear();
	}
}
