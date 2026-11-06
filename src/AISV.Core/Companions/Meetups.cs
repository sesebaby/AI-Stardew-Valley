using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Companions;

public static class MeetupStates
{
	public const string Scheduled = "scheduled";
	public const string Kept = "kept";
	public const string Missed = "missed";
	public const string CalledOff = "called_off";
	public const string CancelledByPlayer = "cancelled";

	public static bool IsOpen(string s) => s == Scheduled;
}

public sealed class Meetup
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	public int Day { get; set; }

	public int Time { get; set; }

	public string Place { get; set; } = "";

	public string OverlayId { get; set; } = "";

	public string State { get; set; } = MeetupStates.Scheduled;

	public string Reason { get; set; } = "";

	public string Summary { get; set; } = "";

	public int CreatedDay { get; set; }

	public int KeptAt { get; set; } = -1;

	public int ClosedDay { get; set; } = -1;

	/// <summary>下雨前原来约的地方。</summary>
	public string MovedFrom { get; set; } = "";

	public string Owner { get; set; } = "";

	public string OwnerRef { get; set; } = "";
}

public sealed class MeetupLog
{
	public const string Path = "plans/meetups.json";

	public static readonly DocumentType<MeetupLog> Doc = new DocumentType<MeetupLog>("约见", 1, () => new MeetupLog());

	public int NextNumber { get; set; } = 1;

	public List<Meetup> Items { get; set; } = new List<Meetup>();
}

/// <summary>可以约见的地方。大多是门口（从地图的门找，换地图 mod 也找得到）。</summary>
public sealed record MeetupPlace(string Key, string Label, bool Indoors, int OpenTime, int CloseTime, string Location, int X, int Y, string DoorTo = "");

public static class MeetupPlaces
{
	public const string Saloon = "saloon";
	public const string Farm = "farm";
	public const string Home = "home";
	public const string TownSquare = "town_square";

	public static readonly IReadOnlyList<MeetupPlace> All = new[]
	{
		new MeetupPlace(TownSquare, "镇广场", false, 0, 2400, "Town", 52, 68),
		new MeetupPlace("river", "镇上的河边", false, 0, 2400, "Town", 68, 58),
		new MeetupPlace(Saloon, "星之果实酒吧", true, 1200, 2400, "Saloon", 16, 21),
		new MeetupPlace("beach", "海滩", false, 0, 2400, "Beach", 40, 22),
		new MeetupPlace("mountain_lake", "山里的湖边", false, 0, 2400, "Mountain", 47, 25),
		new MeetupPlace("pierres", "皮埃尔的杂货店门口", false, 0, 2400, "Town", 0, 0, "SeedShop"),
		new MeetupPlace("clinic", "诊所门口", false, 0, 2400, "Town", 0, 0, "Hospital"),
		new MeetupPlace("blacksmith", "铁匠铺门口", false, 0, 2400, "Town", 0, 0, "Blacksmith"),
		new MeetupPlace("museum", "博物馆门口", false, 0, 2400, "Town", 0, 0, "ArchaeologyHouse"),
		new MeetupPlace("community_center", "社区中心门口", false, 0, 2400, "Town", 0, 0, "CommunityCenter"),
		new MeetupPlace("ranch", "玛妮的牧场门口", false, 0, 2400, "Forest", 0, 0, "AnimalShop"),
		new MeetupPlace(Farm, "你的农舍门口", false, 0, 2400, "Farm", 0, 0),
		new MeetupPlace(Home, "对方家里", true, 0, 2200, "", 0, 0)
	};

	private static readonly (string Word, string Key)[] Words =
	{
		("广场", TownSquare), ("square", TownSquare), ("河", "river"), ("酒吧", Saloon), ("saloon", Saloon), ("海滩", "beach"), ("海边", "beach"), ("beach", "beach"),
		("湖", "mountain_lake"), ("lake", "mountain_lake"), ("杂货店", "pierres"), ("皮埃尔", "pierres"), ("诊所", "clinic"), ("铁匠", "blacksmith"), ("博物馆", "museum"),
		("社区中心", "community_center"), ("牧场", "ranch"), ("农场", Farm), ("农舍", Farm), ("farm", Farm), ("我家", Home), ("家里", Home), ("home", Home)
	};

	public static MeetupPlace? Get(string? key)
	{
		string k = (key ?? "").Trim().ToLowerInvariant();
		return All.FirstOrDefault(p => p.Key == k);
	}

	/// <summary>认 AI 写的地点：键、中文名、关键字；认不出返回空。</summary>
	public static MeetupPlace? Parse(string? text)
	{
		string t = (text ?? "").Trim();
		if (t.Length == 0)
		{
			return null;
		}
		MeetupPlace? exact = Get(t);
		if (exact != null)
		{
			return exact;
		}
		string lower = t.ToLowerInvariant();
		foreach (MeetupPlace p in All)
		{
			if (lower.Contains(p.Key.Replace('_', ' ')) || lower.Contains(p.Key) || t.Contains(Label(p.Key)))
			{
				return p;
			}
		}
		foreach ((string word, string key) in Words)
		{
			if (lower.Contains(word))
			{
				return Get(key);
			}
		}
		return null;
	}

	public static string Label(string key, string? npcDisplay = null)
	{
		MeetupPlace? p = Get(key);
		if (p == null)
		{
			return key;
		}
		return p.Key == Home && !string.IsNullOrEmpty(npcDisplay)
			? Tr.Get("meetup.place.homeOf", "{{npc}}家里", ("npc", npcDisplay))
			: Tr.Get("meetup.place." + p.Key, p.Label);
	}

	public static string PromptList() => string.Join("、", All.Select(p => p.Key + "=" + Label(p.Key)));
}

/// <summary>约见的规则和文字。</summary>
public static class MeetupRules
{
	public const int KeptFriendship = 40;

	public const int MissedFriendship = -30;

	public const int SameDayCancelFriendship = -10;

	public const int WaitMinutes = 120;

	public const int StayAfterKeptMinutes = 60;

	public const int EarliestTime = 800;

	public const int LatestTime = 2000;

	public const int MaxDaysAhead = 3;

	public const int KeptDistance = 6;

	public const int KeptEarlyMinutes = 30;

	public const int MaxPerDay = 3;

	public const int DefaultTime = 1400;

	public const int ClosedKeepDays = 7;

	public const string StoryOwner = "story";

	public const string OverlayOwner = "meetup";

	public const string OffReason = "meetups are off";
	public const string ChildReason = "children don't make plans like that";
	public const string AheadReason = "only up to 3 days ahead";
	public const string HoursReason = "too early or too late";
	public const string ClosedReason = "the place isn't open then";
	public const string AlreadyReason = "already has a plan with you";
	public const string FullReason = "you already have three plans that day";
	public const string TooLateReason = "too late to get there today";

	private static readonly Regex ClockText = new Regex("(\\d{1,2})\\s*[:：]\\s*(\\d{2})", RegexOptions.Compiled);

	private static readonly Regex HourOnly = new Regex("(\\d{1,2})\\s*(点|时|o'?clock|am|pm)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly string[] ChineseHours = { "零", "一", "两", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二" };

	/// <summary>等到几点：等够 2 小时、地方关门、当天结束，取最早。</summary>
	public static int End(int time, MeetupPlace place) => Math.Min(Clock.Add(time, WaitMinutes), Math.Min(place.CloseTime, OverlayRules.LatestEnd));

	/// <summary>"14:00"、"下午两点"、"2pm" → 1400，取整到 10 分钟；认不出返回空。</summary>
	public static int? ParseTime(string? text)
	{
		string t = (text ?? "").Trim();
		if (t.Length == 0)
		{
			return null;
		}
		int hour = -1, minute = 0;
		Match c = ClockText.Match(t);
		if (c.Success)
		{
			hour = int.Parse(c.Groups[1].Value);
			minute = int.Parse(c.Groups[2].Value);
		}
		else
		{
			Match h = HourOnly.Match(t);
			if (h.Success)
			{
				hour = int.Parse(h.Groups[1].Value);
				if (h.Groups[2].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) && hour < 12)
				{
					hour += 12;
				}
			}
			else
			{
				for (int i = ChineseHours.Length - 1; i >= 1; i--)
				{
					if (t.Contains(ChineseHours[i] + "点"))
					{
						hour = i;
						break;
					}
				}
				if (hour >= 0 && t.Contains("半"))
				{
					minute = 30;
				}
			}
			if (hour is >= 1 and < 12 && (t.Contains("下午") || t.Contains("晚上") || t.Contains("傍晚")))
			{
				hour += 12;
			}
		}
		if (hour is < 0 or > 23 || minute is < 0 or > 59)
		{
			return null;
		}
		return hour * 100 + minute / 10 * 10;
	}

	/// <summary>今天 0、明天 1、后天 2、大后天 3；认不出时一小时内赶不上的算明天。</summary>
	public static int ParseDayOffset(string? when, string? time, int now)
	{
		string w = (when ?? "").ToLowerInvariant();
		if (w.Contains("大后天") || w.Contains("in 3 days") || w.Contains("three days"))
		{
			return 3;
		}
		if (w.Contains("后天") || w.Contains("day after tomorrow") || w.Contains("in 2 days") || w.Contains("two days"))
		{
			return 2;
		}
		if (w.Contains("明") || w.Contains("tomorrow"))
		{
			return 1;
		}
		if (w.Contains("今") || w.Contains("today") || w.Contains("tonight") || w.Contains("this "))
		{
			return 0;
		}
		if (IsNow(w) || IsNow((time ?? "").ToLowerInvariant()))
		{
			return 0;
		}
		int? t = ParseTime(time) ?? ParseTime(when);
		return t.HasValue && Clock.Minutes(t.Value) >= Clock.Minutes(now) + 60 ? 0 : 1;
	}

	public static bool IsNow(string text) => text.Contains("now") || text.Contains("asap") || text.Contains("right away") || text.Contains("现在") || text.Contains("马上") || text.Contains("立刻") || text.Contains("这就") || text.Contains("赶紧");

	/// <summary>今天最早几点能见：路程 + 收拾 20 分钟，至少一小时，取整到 10 分钟。</summary>
	public static int EarliestToday(int now, int walkMinutes)
	{
		int minutes = Clock.Minutes(now) + Math.Max(60, walkMinutes + 20);
		return Clock.FromMinutes((minutes + 9) / 10 * 10);
	}

	public static string WhenText(int day, int today)
	{
		int d = day - today;
		return d switch
		{
			0 => Tr.Get("meetup.today", "今天"),
			1 => Tr.Get("meetup.tomorrow", "明天"),
			2 => Tr.Get("meetup.dayAfter", "后天"),
			_ => Tr.Get("meetup.inDays", "{{n}} 天后", ("n", d))
		};
	}

	/// <summary>村民在聊天里婉拒的一句（换掉那句答应的话）。</summary>
	public static string DeclineLine(string reason)
	{
		string why;
		if (reason.Contains("festival"))
		{
			why = Tr.Get("meetup.no.festival", "那天镇上过节，我走不开");
		}
		else if (reason.Contains("birthday"))
		{
			why = Tr.Get("meetup.no.birthday", "那天我过生日，家里有安排");
		}
		else if (reason.Contains("already has"))
		{
			why = Tr.Get("meetup.no.already", "我们不是已经约好一次了嘛，先把那次见了");
		}
		else if (reason.Contains("three plans"))
		{
			why = Tr.Get("meetup.no.full", "你那天的安排已经够满了");
		}
		else if (reason.Contains("isn't open"))
		{
			why = Tr.Get("meetup.no.closed", "那个点那边不开门");
		}
		else if (reason.Contains("too late") || reason.Contains("passed") || reason.Contains("too early"))
		{
			why = Tr.Get("meetup.no.late", "今天太晚了，走过去也赶不上");
		}
		else if (reason.Contains(OfferLedger.CooldownWord) || reason.Contains(OfferLedger.ProposedWord))
		{
			why = Tr.Get("meetup.no.busy", "这两天我手头事情有点多");
		}
		else if (reason.Contains(OfferLedger.HeartsWord))
		{
			why = Tr.Get("meetup.no.hearts", "我们还不算太熟，过阵子再说吧");
		}
		else if (reason.Contains("days ahead") || reason.Contains("today or tomorrow"))
		{
			why = Tr.Get("meetup.no.far", "那么久以后的事我现在定不下来");
		}
		else if (reason.Contains("mind the shop"))
		{
			why = Tr.Get("meetup.no.shop", "那会儿我得看店");
		}
		else if (reason.Contains("two helpers"))
		{
			why = Tr.Get("meetup.no.helpers", "那天你已经请了两个人帮忙了");
		}
		else if (reason.Contains("already coming"))
		{
			why = Tr.Get("meetup.no.coming", "那天我已经说好要去你那儿了");
		}
		else if (reason.Contains("too soon"))
		{
			why = Tr.Get("meetup.no.soon", "这么急我赶不过去");
		}
		else if (reason.Contains("farm help is off") || reason.Contains("can't do that"))
		{
			why = Tr.Get("meetup.no.help", "这活我现在帮不上");
		}
		else
		{
			why = Tr.Get("meetup.no.other", "那会儿我实在走不开");
		}
		return Tr.Get("meetup.declineLine", "{{why}}……这次就不过去了，改天再约吧。", ("why", why));
	}

	/// <summary>排不上的原因，说给玩家听。</summary>
	public static string Explain(string reason)
	{
		if (reason.Contains("festival"))
		{
			return Tr.Get("meetup.why.festival", "那天镇上过节");
		}
		if (reason.Contains("birthday"))
		{
			return Tr.Get("meetup.why.birthday", "那天过生日，家里有安排");
		}
		if (reason.Contains("already has"))
		{
			return Tr.Get("meetup.why.already", "你们已经约了一次");
		}
		if (reason.Contains("three plans"))
		{
			return Tr.Get("meetup.why.full", "你那天已经约满了");
		}
		if (reason.Contains("isn't open"))
		{
			return Tr.Get("meetup.why.closed", "那个时间那里不开门");
		}
		if (reason.Contains("too late") || reason.Contains("too early") || reason.Contains("passed"))
		{
			return Tr.Get("meetup.why.late", "今天走过去已经赶不上了");
		}
		if (reason.Contains(OfferLedger.CooldownWord) || reason.Contains(OfferLedger.ProposedWord))
		{
			return Tr.Get("meetup.why.busy", "这两天事情有点多");
		}
		if (reason.Contains(OfferLedger.HeartsWord))
		{
			return Tr.Get("meetup.why.hearts", "你们还不算太熟");
		}
		if (reason.Contains("mind the shop"))
		{
			return Tr.Get("meetup.why.shop", "那时要看店");
		}
		if (reason.Contains("two helpers"))
		{
			return Tr.Get("meetup.why.helpers", "那天已经有两个人来帮忙了");
		}
		if (reason.Contains("already coming"))
		{
			return Tr.Get("meetup.why.coming", "那天已经说好要来了");
		}
		return Tr.Get("meetup.why.other", "那天不太方便");
	}

	/// <summary>0 今天、1 明天、-1 昨天……</summary>
	public static string RelativeDay(int day, int today)
	{
		int d = day - today;
		return d switch
		{
			0 => "今天",
			1 => "明天",
			-1 => "昨天",
			> 1 => $"{d} 天后",
			_ => $"{-d} 天前"
		};
	}

	/// <summary>给 AI 的一段：和农夫约了什么、见没见到。</summary>
	public static string PromptBlock(IEnumerable<Meetup> mine, int today, Func<string, string> placeLabel)
	{
		List<string> lines = new List<string>();
		foreach (Meetup m in mine.OrderBy(m => m.Day))
		{
			string place = placeLabel(m.Place);
			switch (m.State)
			{
				case MeetupStates.Scheduled:
					lines.Add($"- 你和农夫说好{RelativeDay(m.Day, today)} {Clock.Text(m.Time)} 在{place}碰面{(m.MovedFrom.Length > 0 ? "（下雨，改到屋里了）" : "")}。要是正到这个点、两个人都在那儿，你们这会儿就是在碰面。");
					break;
				case MeetupStates.Kept when today - m.ClosedDay <= 2:
					lines.Add($"- {RelativeDay(m.ClosedDay, today)}农夫按说好的来{place}见了你，你挺高兴。");
					break;
				case MeetupStates.Missed when today - m.ClosedDay <= 3:
					lines.Add($"- {RelativeDay(m.ClosedDay, today)}你在{place}等农夫，人一直没来。心里有点难过，可以提一次，但别说重话。");
					break;
				case MeetupStates.CancelledByPlayer when today - m.ClosedDay <= 2:
					lines.Add($"- 农夫跟你说过去不了{place}了。你不往心里去。");
					break;
				case MeetupStates.CalledOff when today - m.ClosedDay <= 2:
					lines.Add($"- {RelativeDay(m.ClosedDay, today)}你没赶上和农夫在{place}的约，有点过意不去。");
					break;
			}
		}
		return lines.Count == 0 ? "" : "【和农夫的约定】\n" + string.Join("\n", lines);
	}
}
