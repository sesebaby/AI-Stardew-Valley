using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.World;

/// <summary>一个游戏自带的心事件：村民、心数、游戏里的事件 ID，以及给八卦用的一句经过（主语是农夫或村民，不带名字前缀）。</summary>
public sealed record HeartEvent(string Npc, int Hearts, int EventId, string Story = "");

/// <summary>游戏里的固定资料：节日、生日、亲近关系、店铺营业时间，以及运行时读到的心事件。</summary>
public static class WorldData
{
	public static readonly IReadOnlyDictionary<(string Season, int Day), string> Festivals = new Dictionary<(string, int), string>
	{
		[("spring", 13)] = "复活节（彩蛋节）",
		[("spring", 24)] = "花舞节",
		[("summer", 11)] = "夏威夷宴会",
		[("summer", 28)] = "月光水母起舞",
		[("fall", 16)] = "星露谷展览会",
		[("fall", 27)] = "万灵节",
		[("winter", 8)] = "冰雪节",
		[("winter", 25)] = "冬日星盛宴"
	};

	/// <summary>节日的开始和散场时间（游戏时间）；晚上的节日散场在午夜。</summary>
	public static readonly IReadOnlyDictionary<(string Season, int Day), (int Start, int End)> FestivalHours = new (string Dates, int Start, int End)[]
	{
		("spring/13 spring/24 summer/11 winter/8 winter/25", 900, 1400),
		("fall/16", 900, 1500),
		("summer/28", 2200, 2400),
		("fall/27", 2200, 2350)
	}.SelectMany(slot => slot.Dates.Split(' ').Select(date => (Date: date.Split('/'), slot.Start, slot.End)))
		.ToDictionary(x => (x.Date[0], int.Parse(x.Date[1])), x => (x.Start, x.End));

	public static readonly IReadOnlyDictionary<(string Season, int Day), string> Birthdays = new[]
	{
		("spring", "Kent@4 Lewis@7 Vincent@10 Haley@14 Pam@18 Shane@20 Pierre@26 Emily@27"),
		("summer", "Jas@4 Gus@8 Maru@10 Alex@13 Sam@17 Demetrius@19 Dwarf@22 Willy@24"),
		("fall", "Penny@2 Elliott@5 Jodi@11 Abigail@13 Sandy@15 Marnie@18 Robin@21 George@24"),
		("winter", "Krobus@1 Linus@3 Caroline@7 Sebastian@10 Harvey@14 Wizard@17 Evelyn@20 Leah@23 Clint@26")
	}.SelectMany(row => row.Item2.Split(' ').Select(cell => (row.Item1, Cell: cell.Split('@'))))
		.ToDictionary(x => (x.Item1, int.Parse(x.Cell[1])), x => x.Cell[0]);

	/// <summary>同一家人和常凑在一起的小圈子；同圈的人彼此亲近（生日、结婚等消息里会特别说明"是你亲近的人"）。</summary>
	private static readonly string[][] Circles =
	{
		new[] { "Pierre", "Caroline", "Abigail" }, new[] { "Jodi", "Kent", "Sam", "Vincent" }, new[] { "Robin", "Demetrius", "Maru", "Sebastian" },
		new[] { "Emily", "Haley" }, new[] { "George", "Evelyn", "Alex" }, new[] { "Pam", "Penny" }, new[] { "Marnie", "Shane", "Jas" },
		new[] { "Abigail", "Sam", "Sebastian" }, new[] { "Alex", "Haley" }, new[] { "Leah", "Elliott" }, new[] { "Elliott", "Willy" }, new[] { "Pam", "Gus" },
		new[] { "Gus", "Emily" }, new[] { "Lewis", "Marnie" }, new[] { "Clint", "Emily" }, new[] { "Sandy", "Emily" }, new[] { "Shane", "Emily" },
		new[] { "Caroline", "Jodi" }, new[] { "Caroline", "Wizard" }, new[] { "Maru", "Harvey" }, new[] { "Penny", "Jas", "Vincent" }, new[] { "Penny", "Sam" }
	};

	/// <summary>店在哪个地点、谁看店、几点到几点（游戏时间）。</summary>
	public static readonly IReadOnlyDictionary<string, (string Keeper, int Opens, int Closes)> ShopCounters = new[]
	{
		"Blacksmith Clint 900 1600", "AnimalShop Marnie 900 1600", "Hospital Harvey 900 1500", "ScienceHouse Robin 900 1700", "SeedShop Pierre 900 1700",
		"FishShop Willy 900 1700", "Saloon Gus 1200 2400", "JojaMart Morris 900 2300", "SandyHouse Sandy 900 2300"
	}.Select(row => row.Split(' ')).ToDictionary(c => c[0], c => (c[1], int.Parse(c[2]), int.Parse(c[3])), StringComparer.OrdinalIgnoreCase);

	private static IReadOnlyList<HeartEvent> _heartEvents = Array.Empty<HeartEvent>();

	/// <summary>从当前游戏的事件数据推出来的心事件，进存档后才有内容。</summary>
	public static IReadOnlyList<HeartEvent> HeartEvents => System.Threading.Volatile.Read(ref _heartEvents);

	public static void UseHeartEvents(IEnumerable<HeartEvent> events) => System.Threading.Volatile.Write(ref _heartEvents, events.ToList());

	public static bool IsClose(string npc, string other) => !string.Equals(npc, other, StringComparison.OrdinalIgnoreCase)
		&& Circles.Any(c => c.Contains(npc, StringComparer.OrdinalIgnoreCase) && c.Contains(other, StringComparer.OrdinalIgnoreCase));

	public static IEnumerable<HeartEvent> HeartEventsOf(string npc) => HeartEvents.Where(e => string.Equals(e.Npc, npc, StringComparison.OrdinalIgnoreCase));

	public static string NextSeason(string season) => season.ToLowerInvariant() switch
	{
		"spring" => "summer",
		"summer" => "fall",
		"fall" => "winter",
		_ => "spring"
	};

	public static string DayOfWeek(int dayOfMonth) => ((dayOfMonth - 1) % 7) switch
	{
		0 => "星期一",
		1 => "星期二",
		2 => "星期三",
		3 => "星期四",
		4 => "星期五",
		5 => "星期六",
		6 => "星期日",
		_ => "某一天"
	};

	public static string TomorrowWeather(string? weather) => (weather ?? "").Trim().ToLowerInvariant() switch
	{
		"rain" => "明天下雨。",
		"lightning" or "storm" => "明天有暴风雨。",
		"snow" => "明天下雪。",
		"wind" or "debris" => "明天刮风。",
		"greenrain" or "green_rain" => "明天会下那种怪怪的绿雨。",
		_ => ""
	};

	/// <summary>NPC 日程终点动作名（如 abigail_sit）→ 在做什么。</summary>
	public static string Activity(string? behaviorName)
	{
		if (string.IsNullOrWhiteSpace(behaviorName))
		{
			return "";
		}
		string b = behaviorName.Trim().ToLowerInvariant();
		int us = b.LastIndexOf('_');
		if (us >= 0)
		{
			b = b.Substring(us + 1);
		}
		return b switch
		{
			"sit" or "sitting" => "坐着",
			"reading" or "read" or "book" => "看书",
			"writing" or "write" => "写东西",
			"guitar" => "弹吉他",
			"smoking" or "smoke" => "抽烟",
			"drink" or "drinking" => "喝酒",
			"eat" or "eating" => "吃东西",
			"paint" or "painting" => "画画",
			"game" or "games" or "videogames" => "打游戏",
			"jog" or "lifting" or "lift" or "exercise" or "excercise" => "锻炼",
			"photo" or "camera" => "拍照",
			"nap" or "sleep" => "打盹",
			"cooking" or "cook" => "做饭",
			"fishing" or "fish" => "钓鱼",
			"sweep" or "clean" or "sweeping" or "cleaning" => "收拾屋子",
			"tea" or "coffee" => "喝着饮料",
			"water" or "garden" or "gardening" => "侍弄花草",
			"dancing" or "dance" => "跳舞",
			"phone" => "打电话",
			"computer" or "pc" => "用电脑",
			"forge" or "hammer" => "在炉子边干活",
			_ => ""
		};
	}

	/// <summary>夫妻感情凉了：心数掉到 4 心及以下。</summary>
	public static string ColdMarriage(int hearts) => hearts <= 4 ? $"可这段婚姻已经凉了：你对农夫只剩 {hearts} 心，没了新婚时的亲热，心里有怨" : "";

	public static string DaysSinceTalk(int days) => days < 2 ? "" : $"你们已经 {days} 天没说过话了。";
}
