using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Apps;

public enum JunimoMood
{
	Happy,
	Calm,
	Lonely,
	Curled
}

public sealed class JunimoLog
{
	public int Day { get; set; }

	public string Text { get; set; } = "";
}

/// <summary>家里的 Junimo 宠物。</summary>
public sealed class JunimoState
{
	public const string Path = "farmer/junimo.json";

	public static readonly DocumentType<JunimoState> Doc = new DocumentType<JunimoState>("Junimo 宠物", 1, () => new JunimoState());

	public bool Adopted { get; set; }

	public string Name { get; set; } = "";

	public int AdoptedDay { get; set; }

	/// <summary>颜色（游戏里 Junimo 的几种之一）。</summary>
	public int Tint { get; set; }

	public int Fullness { get; set; } = 80;

	public int Joy { get; set; } = 70;

	public int FedDay { get; set; } = -1;

	public int PettedDay { get; set; } = -1;

	public int PlayedDay { get; set; } = -1;

	public int LastDay { get; set; } = -1;

	public List<JunimoLog> Log { get; set; } = new List<JunimoLog>();
}

/// <summary>每天早上家里的情况：有没有伴侣、伴侣对你怎样、离没离婚、有没有孩子。</summary>
public sealed record JunimoHome(string? Spouse, int SpouseHearts, bool Divorced, int Children, string? ExSpouse);

public static class JunimoRules
{
	public const int MaxLog = 30;
	public const int FeedGain = 35;
	public const int PetGain = 15;
	public const int PlayGain = 20;

	public static readonly string[] Names = { "啾啾", "团子", "小绿", "豆豆", "毛球", "星星", "果冻", "咕噜" };

	public static JunimoMood Mood(JunimoState s) => s.Fullness < 20 || s.Joy < 15 ? JunimoMood.Curled
		: s.Joy < 40 ? JunimoMood.Lonely
		: s.Joy >= 70 && s.Fullness >= 50 ? JunimoMood.Happy
		: JunimoMood.Calm;

	public static string MoodLabel(JunimoMood m) => m switch
	{
		JunimoMood.Happy => Tr.Get("junimo.mood.happy", "开心得蹦来蹦去"),
		JunimoMood.Calm => Tr.Get("junimo.mood.calm", "安安静静的"),
		JunimoMood.Lonely => Tr.Get("junimo.mood.lonely", "有点落寞"),
		_ => Tr.Get("junimo.mood.curled", "缩成一团，不太理人")
	};

	public static void Adopt(JunimoState s, int today, Random rng, string? name = null)
	{
		s.Adopted = true;
		s.AdoptedDay = today;
		s.Name = string.IsNullOrWhiteSpace(name) ? Names[rng.Next(Names.Length)] : name.Trim();
		s.Tint = rng.Next(6);
		s.Fullness = 80;
		s.Joy = 70;
		s.LastDay = today;
		Add(s, today, Tr.Get("junimo.log.adopt", "{{name}}搬进了家里，在门口好奇地张望。", ("name", s.Name)));
	}

	/// <summary>过一天：饿一点、没人陪就闷一点；家里的气氛会传给它。可能跨好几天（没进游戏的日子按一天算）。</summary>
	public static void NewDay(JunimoState s, int today, JunimoHome home, Func<string, string> display)
	{
		if (!s.Adopted || s.LastDay >= today)
		{
			return;
		}
		int days = Math.Clamp(today - s.LastDay, 1, 7);
		s.LastDay = today;
		bool cared = s.PettedDay >= today - days || s.PlayedDay >= today - days;
		s.Fullness = Math.Clamp(s.Fullness - 25 * days, 0, 100);
		s.Joy = Math.Clamp(s.Joy - (cared ? 5 : 12) * days, 0, 100);
		if (home.Spouse != null && home.SpouseHearts >= 8)
		{
			s.Joy = Math.Min(100, s.Joy + 12);
			s.Fullness = Math.Min(100, s.Fullness + 20);
			Add(s, today, Tr.Get("junimo.log.spouse", "{{spouse}}今天给{{name}}喂了吃的，还给它梳了毛。", ("spouse", display(home.Spouse)), ("name", s.Name)));
		}
		else if (home.Spouse != null && home.SpouseHearts < 5)
		{
			s.Joy = Math.Max(0, s.Joy - 8);
			Add(s, today, Tr.Get("junimo.log.cold", "家里很安静，{{name}}躲在角落里看着你们。", ("name", s.Name)));
		}
		else if (home.Divorced && home.ExSpouse != null)
		{
			s.Joy = Math.Max(0, s.Joy - 5);
			if (today % 7 == 0)
			{
				Add(s, today, Tr.Get("junimo.log.waiting", "{{name}}在门口等了很久，等的人没有回来。", ("name", s.Name)));
			}
		}
		if (home.Children > 0 && s.Joy < 90)
		{
			s.Joy = Math.Min(100, s.Joy + 5);
		}
		if (s.Fullness == 0)
		{
			Add(s, today, Tr.Get("junimo.log.hungry", "{{name}}饿得没力气，缩成了一团。", ("name", s.Name)));
		}
	}

	/// <summary>喂、摸、陪玩，各一天一次。返回它的反应（今天已经做过时为 null）。</summary>
	public static string? Act(JunimoState s, string act, int today)
	{
		switch (act)
		{
			case "feed" when s.FedDay != today:
				s.FedDay = today;
				s.Fullness = Math.Min(100, s.Fullness + FeedGain);
				s.Joy = Math.Min(100, s.Joy + 5);
				return Tr.Get("junimo.act.feed", "{{name}}抱着果子啃得飞快。", ("name", s.Name));
			case "pet" when s.PettedDay != today:
				s.PettedDay = today;
				s.Joy = Math.Min(100, s.Joy + PetGain);
				return Tr.Get("junimo.act.pet", "{{name}}眯起眼睛，往你手心里蹭。", ("name", s.Name));
			case "play" when s.PlayedDay != today:
				s.PlayedDay = today;
				s.Joy = Math.Min(100, s.Joy + PlayGain);
				s.Fullness = Math.Max(0, s.Fullness - 5);
				return Tr.Get("junimo.act.play", "{{name}}追着你的手指转了好几圈。", ("name", s.Name));
			default:
				return null;
		}
	}

	public static void Add(JunimoState s, int day, string text)
	{
		s.Log.Insert(0, new JunimoLog { Day = day, Text = text });
		if (s.Log.Count > MaxLog)
		{
			s.Log.RemoveRange(MaxLog, s.Log.Count - MaxLog);
		}
	}
}
