using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Web;
using AISV.Core.Text;

namespace AISV.Core.Apps;

/// <summary>日程上的一站：几点、在哪、做什么；约见这类和你有关的另外标出来。</summary>
public sealed record AgendaStop(int Time, string Place, string Doing, bool WithYou = false, string Note = "");

public static class Agenda
{
	/// <summary>日程动作名里的关键词 → 在做什么。</summary>
	private static readonly (string Key, string Word)[] Behaviors =
	{
		("photo", "拍照"), ("camera", "拍照"), ("fish", "钓鱼"), ("read", "看书"), ("book", "看书"), ("paint", "画画"), ("sketch", "画画"),
		("guitar", "弹吉他"), ("flute", "吹长笛"), ("music", "玩音乐"), ("dance", "跳舞"), ("exercise", "锻炼"), ("workout", "锻炼"), ("pushup", "锻炼"),
		("jog", "跑步"), ("run", "跑步"), ("sleep", "睡觉"), ("nap", "打盹"), ("sit", "坐着歇歇"), ("bench", "坐着歇歇"), ("cook", "做饭"),
		("sew", "缝衣服"), ("sewing", "缝衣服"), ("garden", "侍弄花园"), ("water", "浇水"), ("dig", "挖东西"), ("hammer", "敲敲打打"), ("forge", "打铁"),
		("smoke", "抽烟"), ("drink", "喝一杯"), ("beer", "喝一杯"), ("telescope", "看星星"), ("tinker", "捣鼓发明"), ("computer", "对着电脑"),
		("game", "打游戏"), ("pool", "打台球"), ("sweep", "打扫"), ("clean", "打扫"), ("shop", "看店"), ("counter", "看店"), ("swim", "游泳"),
		("yoga", "做瑜伽"), ("write", "写东西"), ("type", "写东西"), ("crab", "抓螃蟹"), ("shell", "捡贝壳"), ("beach", "在海边走走")
	};

	/// <summary>到了地方做什么：动作名里有就用；没有时只给户外地点猜一个，屋里和店里就不多说。</summary>
	public static string Doing(string? behavior, string? location)
	{
		string b = (behavior ?? "").ToLowerInvariant();
		foreach ((string key, string word) in Behaviors)
		{
			if (b.Contains(key, StringComparison.Ordinal))
			{
				return word;
			}
		}
		return (location ?? "") switch
		{
			"Beach" => "在海边走走",
			"Mountain" => "在山上走走",
			"Forest" => "在森林里转转",
			"Town" => "在镇上走走",
			"Railroad" => "在铁路边",
			_ => ""
		};
	}

	/// <summary>把游戏日程读成一站站：连着去同一个地方的合成一站，最多 12 站。</summary>
	public static List<AgendaStop> Build(IEnumerable<(int Time, string Location, string? Behavior)> stops)
	{
		List<AgendaStop> list = new List<AgendaStop>();
		foreach ((int time, string location, string? behavior) in stops.OrderBy(s => s.Time))
		{
			string place = Places.Name(location);
			string doing = Doing(behavior, location);
			if (list.Count > 0 && list[^1].Place == place && list[^1].Doing == doing)
			{
				continue;
			}
			list.Add(new AgendaStop(time, place, doing));
		}
		return list.Take(12).ToList();
	}

	/// <summary>现在在哪一站（还没出门时为 -1）。</summary>
	public static int Current(IReadOnlyList<AgendaStop> stops, int now)
	{
		int at = -1;
		for (int i = 0; i < stops.Count; i++)
		{
			if (stops[i].Time <= now)
			{
				at = i;
			}
		}
		return at;
	}

	public static string Clock(int time)
	{
		int h = time / 100 % 24, m = time % 100;
		return $"{h:00}:{m:00}";
	}

	/// <summary>摘要里的一句：某人接下来去哪。</summary>
	public static string Next(string name, IReadOnlyList<AgendaStop> stops, int now)
	{
		AgendaStop? next = stops.FirstOrDefault(s => s.Time > now);
		return next == null
			? Tr.Get("agenda.nextNone", "{{name}}今天没有别的安排了", ("name", name))
			: Tr.Get("agenda.next", "{{name}} {{time}} 去{{place}}{{doing}}", ("name", name), ("time", Clock(next.Time)), ("place", next.Place), ("doing", next.Doing.Length > 0 ? "，" + next.Doing : ""));
	}
}
