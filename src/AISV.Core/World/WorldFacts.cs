using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Persona;

namespace AISV.Core.World;

public sealed record ChildInfo(string Name, int Age, bool IsBoy);

/// <summary>NPC 被搭话那一刻在干什么（游戏层在主线程上取）。</summary>
public sealed record NpcActivitySnapshot(bool Sleeping, bool DoingEndOfRoute, string? EndOfRouteBehavior, string? LocationName, bool Moving);

/// <summary>
/// 一次对话要用的游戏状态，在主线程上一次取齐，之后在后台拼提示词。
/// 字段只放原始事实，怎么说由 <see cref="WorldFacts"/> 决定。
/// </summary>
public sealed class WorldSnapshot
{
	public string Season { get; init; } = "spring";

	public int Day { get; init; } = 1;

	public int Year { get; init; } = 1;

	public int TimeOfDay { get; init; } = 600;

	public string? Spouse { get; init; }

	public IReadOnlyList<ChildInfo> Children { get; init; } = Array.Empty<ChildInfo>();

	public bool DivorceTonight { get; init; }

	public bool CommunityCenterDone { get; init; }

	public bool JojaMember { get; init; }

	public int DeepestMineLevel { get; init; }

	public bool BusRepaired { get; init; }

	public int FarmingLevel { get; init; }

	public int HouseLevel { get; init; }

	public bool PassedOut { get; init; }

	public bool ReturnScepter { get; init; }

	public bool GalaxySword { get; init; }

	public string? WeatherTomorrow { get; init; }

	/// <summary>今晚值得一提的天象（流星雨、满月）。</summary>
	public string? SkyTonight { get; init; }

	/// <summary>玩家看过的事件 ID。</summary>
	public IReadOnlySet<int> EventsSeen { get; init; } = new HashSet<int>();
}

/// <summary>和某位村民有关的额外状态（不和具体村民说话时为空）。</summary>
public sealed class NpcWorldState
{
	public string Name { get; init; } = "";

	/// <summary>算秘密和心事件记忆用的心数（启用敌意时是扣掉敌意后的心数）。</summary>
	public int Hearts { get; init; }

	/// <summary>玩家任务里和这位村民有关的一个（任务名），没有为 null。</summary>
	public string? OpenQuestName { get; init; }

	public bool HasOpenQuest { get; init; }

	/// <summary>和农夫离过婚。</summary>
	public bool Divorced { get; init; }

	public NpcActivitySnapshot? Activity { get; init; }
}

/// <summary>
/// "世界背景"块：农夫的进度、日子、节日生日、心事件记忆、村民此刻在干什么。
/// 触发条件和阈值（矿井 40/80/120、耕种 5/10、房子 1/2/3、心事件记忆 4 心起、8 心起自然提起等）。
/// </summary>
public static class WorldFacts
{
	public const string Heading = "眼下的情况：";

	/// <param name="isNpcEnabled">配偶、生日的人被关掉 AI 时不提他们的婚姻。</param>
	/// <param name="isReplacedHeartEvent">被成人内容包换掉的心事件不算游戏自带的心事件。</param>
	public static string Build(WorldSnapshot world, NpcWorldState? npc, Func<string, bool>? isNpcEnabled = null, Func<int, bool>? isReplacedHeartEvent = null, Func<string, string>? display = null)
	{
		isNpcEnabled ??= _ => true;
		// 判断亲近、是不是本人都用内部名；写进句子的才换成显示名。
		string Shown(string who) => display?.Invoke(who) ?? who;
		string name = npc?.Name ?? "";
		bool forNpc = name.Length > 0;
		List<string> facts = new List<string>();

		string? spouse = string.IsNullOrEmpty(world.Spouse) ? null : world.Spouse;
		if (spouse != null && isNpcEnabled(spouse))
		{
			if (Same(name, spouse))
			{
				facts.Add("农夫是你的另一半，你们已经结婚了");
				AddIf(facts, WorldData.ColdMarriage(npc!.Hearts));
			}
			else
			{
				facts.Add(WorldData.IsClose(name, spouse) ? $"农夫娶/嫁的是{Shown(spouse)}，那是和你很亲的人" : $"农夫已经和{Shown(spouse)}结婚");
			}
			AddIf(facts, Children(world.Children, Same(name, spouse), Shown(spouse)));
		}
		if (world.DivorceTonight)
		{
			facts.Add("农夫正在办离婚");
		}
		if (npc?.Divorced == true && !Same(name, spouse ?? ""))
		{
			facts.Add("你和农夫结过婚，后来离了");
		}
		if (world.CommunityCenterDone)
		{
			facts.Add("社区中心已经被农夫彻底修好了");
		}
		if (world.JojaMember)
		{
			facts.Add("农夫办了 Joja 超市的会员");
		}
		if (world.DeepestMineLevel >= 120)
		{
			facts.Add("农夫已经下到矿井最底层（第 120 层）");
		}
		else if (world.DeepestMineLevel >= 80)
		{
			facts.Add($"农夫是老练的矿工，下到过第 {world.DeepestMineLevel} 层");
		}
		else if (world.DeepestMineLevel >= 40)
		{
			facts.Add($"农夫在矿井里最深到过第 {world.DeepestMineLevel} 层");
		}
		if (world.BusRepaired)
		{
			facts.Add("去卡利科沙漠的巴士修好了");
		}
		if (world.FarmingLevel >= 10)
		{
			facts.Add("农夫种地已经是行家里手");
		}
		else if (world.FarmingLevel >= 5)
		{
			facts.Add("农夫种地有了些经验");
		}
		if (world.HouseLevel >= 3)
		{
			facts.Add("农夫的房子已经扩建到头（3 级），很气派");
		}
		else if (world.HouseLevel == 2)
		{
			facts.Add("农夫的房子扩建过两次（2 级），有厨房、房间也多了");
		}
		else if (world.HouseLevel == 1)
		{
			facts.Add("农夫的房子扩建过一次（1 级）");
		}
		if (spouse == null)
		{
			AddIf(facts, Children(world.Children, isSpouse: false, null));
		}
		if (world.PassedOut)
		{
			facts.Add("农夫前阵子累晕过（或者在矿里倒下过）");
		}
		if (world.ReturnScepter)
		{
			facts.Add("农夫手上有回城权杖");
		}
		if (world.GalaxySword)
		{
			facts.Add("农夫手上有银河剑");
		}
		int hearts = forNpc ? npc!.Hearts : 0;
		string? secret = forNpc ? BuiltInPersonas.SecretIfClose(name, hearts) : null;
		if (secret != null)
		{
			facts.Add("（你心里藏着、别人不知道的事）" + secret);
		}
		if (WorldData.Festivals.TryGetValue((world.Season, world.Day), out string? today))
		{
			facts.Add(FestivalToday(today, WorldData.FestivalHours[(world.Season, world.Day)], world.TimeOfDay));
		}
		(string season, int day) tomorrow = world.Day + 1 > 28 ? (WorldData.NextSeason(world.Season), 1) : (world.Season, world.Day + 1);
		if (WorldData.Festivals.TryGetValue(tomorrow, out string? next))
		{
			facts.Add("明天过节：" + next);
		}
		if (WorldData.Birthdays.TryGetValue((world.Season, world.Day), out string? birthday) && PersonaResolver.IsInTown(birthday, world.Year))
		{
			if (Same(name, birthday))
			{
				facts.Add("今天是你自己的生日！");
			}
			else
			{
				facts.Add(WorldData.IsClose(name, birthday) ? $"今天是{Shown(birthday)}的生日，那是和你很亲的人" : $"今天是{Shown(birthday)}的生日");
			}
		}
		facts.Add("今天" + WorldData.DayOfWeek(world.Day));
		if (forNpc)
		{
			AddIf(facts, HeartEventMemory(name, SeenHeartEvents(name, world.EventsSeen, isReplacedHeartEvent), hearts));
		}
		if (world.Year == 1)
		{
			facts.Add("农夫今年才搬来（第 1 年）");
		}
		else if (world.Year >= 3)
		{
			facts.Add($"农夫在镇上已经住了 {world.Year} 年");
		}
		if (world.Year == 1 && name.ToLowerInvariant() is "jodi" or "sam" or "vincent")
		{
			facts.Add("肯特还在打仗没回来，你很想他、盼着他回家");
		}
		else if (world.Year >= 2 && name.ToLowerInvariant() is "kent" or "jodi" or "sam" or "vincent")
		{
			facts.Add("肯特刚从战场回来不久");
		}
		AddIf(facts, WorldData.TomorrowWeather(world.WeatherTomorrow));
		AddIf(facts, world.SkyTonight ?? "");
		if (forNpc)
		{
			AddIf(facts, OpenQuest(npc!));
			AddIf(facts, Activity(name, npc!.Activity, world.TimeOfDay));
		}
		if (facts.Count == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine(Heading);
		foreach (string f in facts)
		{
			sb.Append("- ").AppendLine(f);
		}
		return sb.ToString();
	}

	/// <summary>这位村民的游戏自带的心事件里，玩家看过的（去掉被成人包换掉的）。</summary>
	public static List<HeartEvent> SeenHeartEvents(string npc, IReadOnlySet<int> eventsSeen, Func<int, bool>? isReplaced = null) =>
		WorldData.HeartEventsOf(npc).Where(e => eventsSeen.Contains(e.EventId) && !(isReplaced?.Invoke(e.EventId) ?? false)).ToList();

	/// <summary>4 心起才提；8 心起当作自然的共同回忆，4～7 心偶尔带一句。</summary>
	public static string HeartEventMemory(string npc, IReadOnlyList<HeartEvent> seen, int hearts)
	{
		if (seen.Count == 0 || hearts < 4)
		{
			return "";
		}
		string list = string.Join("、", seen.Select(e => e.Hearts + "♥"));
		string how = hearts >= 8 ? "聊到相关的事时可以很自然地提起这些共同经历。" : "偶尔隐约提一下就好，别每句都提。";
		return $"你和农夫一起经历过的事：\n- 看过的心事件：{list}\n- {how}";
	}

	public static string OpenQuest(NpcWorldState npc)
	{
		if (!npc.HasOpenQuest)
		{
			return "";
		}
		string title = Flatten(npc.OpenQuestName, 80);
		return title.Length > 0 ? "农夫手上有一个和你有关、还没完成的任务：" + title : "农夫手上有一个和你有关、还没完成的任务。";
	}

	public static string Activity(string npcName, NpcActivitySnapshot? activity, int timeOfDay)
	{
		if (activity == null)
		{
			return "";
		}
		if (activity.Sleeping)
		{
			return "农夫过来的时候你正在睡觉。";
		}
		if (activity.DoingEndOfRoute)
		{
			string doing = WorldData.Activity(activity.EndOfRouteBehavior);
			if (doing.Length > 0)
			{
				return $"农夫过来的时候你正在{doing}。";
			}
		}
		return ShopCounter(npcName, activity.LocationName, timeOfDay, activity.Moving);
	}

	public static string ShopCounter(string? npcName, string? locationName, int timeOfDay, bool moving)
	{
		if (moving || string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(locationName))
		{
			return "";
		}
		if (!WorldData.ShopCounters.TryGetValue(locationName, out (string Keeper, int Opens, int Closes) shop) || !Same(shop.Keeper, npcName))
		{
			return "";
		}
		return timeOfDay >= shop.Opens && timeOfDay < shop.Closes ? "农夫过来的时候你正在自己店里的柜台后面忙。" : "";
	}

	public static string Children(IReadOnlyList<ChildInfo> children, bool isSpouse, string? spouse)
	{
		if (children.Count == 0)
		{
			return "";
		}
		List<string> parts = new List<string>();
		foreach (ChildInfo c in children)
		{
			string sex = c.IsBoy ? "男孩" : "女孩";
			string stage = c.Age switch
			{
				0 => "还是个小婴儿",
				1 => "正在学爬",
				_ => "已经会走路了"
			};
			string who = string.IsNullOrWhiteSpace(c.Name) ? "一个" + sex : c.Name;
			string parents = isSpouse ? "你和农夫" : spouse != null ? $"农夫和{spouse}" : "农夫";
			parts.Add($"{parents}有个孩子叫{who}（{sex}），{stage}");
		}
		return string.Join("；", parts) + "。";
	}

	private static string Flatten(string? text, int maxChars)
	{
		string flat = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		return flat.Length <= maxChars ? flat : flat.Substring(0, maxChars).TrimEnd() + "...";
	}

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>节日是还没开始、正在办还是已经散了：散场后游戏直接跳到晚上，不说清楚 AI 会当成节日还没到。</summary>
	public static string FestivalToday(string festival, (int Start, int End) hours, int time)
	{
		if (time >= hours.End)
		{
			return $"今天的{festival}已经办完了，人都散了";
		}
		if (time < hours.Start && hours.Start >= 1800)
		{
			return $"今晚过节：{festival}（{hours.Start / 100} 点开始）";
		}
		return "今天过节：" + festival;
	}

	private static void AddIf(List<string> facts, string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			facts.Add(text);
		}
	}
}
