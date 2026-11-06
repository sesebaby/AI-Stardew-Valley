using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AISV.Core.Social;

/// <summary>一位村民今天的心情。</summary>
public sealed class NpcMood
{
	[JsonPropertyName("because")]
	public string Because { get; set; } = "";

	/// <summary>-100～100。</summary>
	[JsonPropertyName("level")]
	public int Level { get; set; }

	[JsonPropertyName("npc")]
	public string Npc { get; set; } = "";

	/// <summary>excited / happy / neutral / irritated / melancholic。</summary>
	[JsonPropertyName("temper")]
	public string Temper { get; set; } = "neutral";
}

/// <summary>算每日心情时需要的一位村民的情况。</summary>
public readonly record struct MoodInput(string Name, bool IsBirthday, int Hearts);

/// <summary>村民的每日心情。只在主线程用。</summary>
public sealed class MoodBook
{
	// 天气偏好（天气用 Sunny / Rainy / Stormy / Snowy）。
	private static readonly Dictionary<string, Dictionary<string, int>> WeatherLikes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
	{
		["Sebastian"] = new() { ["Rainy"] = 30, ["Stormy"] = 20, ["Sunny"] = -10 },
		["Penny"] = new() { ["Rainy"] = -15, ["Sunny"] = 15 },
		["Haley"] = new() { ["Rainy"] = -25, ["Sunny"] = 30 },
		["Shane"] = new() { ["Rainy"] = -20, ["Stormy"] = -30 },
		["Leah"] = new() { ["Sunny"] = 20, ["Rainy"] = 5 },
		["Abigail"] = new() { ["Rainy"] = 10, ["Stormy"] = 20 },
		["Emily"] = new() { ["Sunny"] = 15 },
		["Alex"] = new() { ["Sunny"] = 20, ["Rainy"] = -20 },
		["Sam"] = new() { ["Sunny"] = 15, ["Rainy"] = -10 },
		["Elliott"] = new() { ["Rainy"] = 15, ["Stormy"] = -10 },
		["Maru"] = new() { ["Sunny"] = 10 },
		["Harvey"] = new() { ["Stormy"] = -25 },
		["Linus"] = new() { ["Rainy"] = -15, ["Snowy"] = -20 },
		["Willy"] = new() { ["Rainy"] = 10, ["Sunny"] = 15 },
		["Clint"] = new() { ["Rainy"] = -10 },
		["Wizard"] = new() { ["Stormy"] = 20, ["Rainy"] = 10 }
	};

	private static readonly Dictionary<string, Dictionary<string, int>> SeasonLikes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
	{
		["Sebastian"] = new() { ["fall"] = 15, ["winter"] = 10, ["summer"] = -15 },
		["Haley"] = new() { ["summer"] = 20, ["spring"] = 10, ["winter"] = -20 },
		["Shane"] = new() { ["spring"] = -10, ["winter"] = -20 },
		["Penny"] = new() { ["spring"] = 15, ["summer"] = 10 },
		["Leah"] = new() { ["fall"] = 20, ["spring"] = 10 },
		["Elliott"] = new() { ["summer"] = 15, ["fall"] = 10 },
		["Alex"] = new() { ["summer"] = 20, ["winter"] = -15 },
		["Sam"] = new() { ["summer"] = 15 },
		["Emily"] = new() { ["spring"] = 15 },
		["Abigail"] = new() { ["fall"] = 20, ["winter"] = 10 },
		["Maru"] = new() { ["summer"] = 10, ["spring"] = 10 },
		["Harvey"] = new() { ["spring"] = 10 },
		["Linus"] = new() { ["spring"] = 15, ["summer"] = 10, ["winter"] = -25 },
		["Willy"] = new() { ["summer"] = 15, ["spring"] = 10 },
		["Evelyn"] = new() { ["spring"] = 20 },
		["George"] = new() { ["winter"] = -20 }
	};

	// 星期几：(日期 - 1) % 7，0 = 星期一。
	private static readonly Dictionary<string, Dictionary<int, int>> WeekdayLikes = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase)
	{
		["Shane"] = new() { [0] = -20, [4] = -15 },
		["Sam"] = new() { [0] = -15, [5] = 20 },
		["Gus"] = new() { [5] = 10, [6] = 10 },
		["Penny"] = new() { [1] = 10, [3] = 10 },
		["Pam"] = new() { [0] = -10 },
		["Lewis"] = new() { [0] = 10 },
		["Clint"] = new() { [4] = -10 }
	};

	private readonly Dictionary<string, NpcMood> _today = new Dictionary<string, NpcMood>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _carried = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public int Count => _today.Count;

	public static string Label(int score)
	{
		if (score <= -30)
		{
			return "melancholic";
		}
		if (score <= -10)
		{
			return "irritated";
		}
		if (score >= 30)
		{
			return "excited";
		}
		return score >= 10 ? "happy" : "neutral";
	}

	/// <summary>天亮时算所有内置村民今天的心情。<paramref name="weather"/> 是 Sunny / Rainy / Stormy / Snowy。</summary>
	public void CalculateDay(string weather, string season, int dayOfMonth, IEnumerable<MoodInput> villagers)
	{
		_today.Clear();
		int weekday = (dayOfMonth - 1) % 7;
		foreach (MoodInput v in villagers)
		{
			int score = 0;
			List<string> why = new List<string>();
			if (WeatherLikes.TryGetValue(v.Name, out var w) && w.TryGetValue(weather, out int byWeather))
			{
				score += byWeather;
				why.Add(byWeather > 0 ? $"爱这种{WeatherWord(weather)}天" : $"受不了{WeatherWord(weather)}天");
			}
			if (SeasonLikes.TryGetValue(v.Name, out var s) && s.TryGetValue(season, out int bySeason))
			{
				score += bySeason;
				if (bySeason > 10)
				{
					why.Add($"最喜欢{SeasonWord(season)}");
				}
				else if (bySeason < -10)
				{
					why.Add($"不太喜欢{SeasonWord(season)}");
				}
			}
			if (WeekdayLikes.TryGetValue(v.Name, out var d) && d.TryGetValue(weekday, out int byDay))
			{
				score += byDay;
				string dayWord = weekday switch { 0 => "星期一", 4 => "星期五", 5 => "星期六", 6 => "星期天", _ => "今天这个日子" };
				why.Add(byDay > 0 ? $"{dayWord}让人有盼头" : $"{dayWord}总提不起劲");
			}
			if (v.IsBirthday)
			{
				score += 40;
				why.Add("今天过生日");
			}
			if (_carried.TryGetValue(v.Name, out int carried) && carried != 0)
			{
				score += carried;
				why.Add(carried > 0 ? "最近碰上了好事" : "最近碰上了不痛快的事");
			}
			if (v.Hearts >= 8)
			{
				score += 10;
			}
			else if (v.Hearts <= 1)
			{
				score -= 5;
			}
			score = Math.Clamp(score, -100, 100);
			string label = v.IsBirthday && score >= 0 ? "excited" : Label(score);
			_today[v.Name] = new NpcMood
			{
				Npc = v.Name,
				Temper = label,
				Because = why.Count > 0 ? string.Join("；", why) : "平平常常的一天",
				Level = score
			};
		}
		_carried.Clear();
	}

	/// <summary>聊天等事让心情变了：当场改今天的分，同时记下，明天算心情时再算一次。</summary>
	public void Adjust(string npcName, int amount)
	{
		_carried[npcName] = (_carried.TryGetValue(npcName, out int sum) ? sum : 0) + amount;
		if (_today.TryGetValue(npcName, out NpcMood? mood))
		{
			mood.Level = Math.Clamp(mood.Level + amount, -100, 100);
			mood.Because += amount > 0 ? "；刚碰上了好事" : "；刚碰上了不痛快的事";
			mood.Temper = Label(mood.Level);
		}
	}

	public NpcMood? Get(string npcName) => _today.TryGetValue(npcName, out NpcMood? mood) ? mood : null;

	public IReadOnlyCollection<NpcMood> All => _today.Values;

	/// <summary>一天结束时清空（攒下的变化留到明天）。</summary>
	public void EndDay() => _today.Clear();

	/// <summary>换存档时全部清空。</summary>
	public void Reset()
	{
		_today.Clear();
		_carried.Clear();
	}

	/// <summary>联机：用房主发来的心情替换。</summary>
	public void ReplaceAll(IEnumerable<NpcMood> moods)
	{
		List<NpcMood> list = moods.Where(m => !string.IsNullOrWhiteSpace(m.Npc)).ToList();
		if (list.Count == 0)
		{
			return;
		}
		_today.Clear();
		foreach (NpcMood m in list)
		{
			_today[m.Npc] = m;
		}
	}

	/// <summary>对话提示词里的心情块；还没算心情时为空。</summary>
	public string PromptBlock(string npcName)
	{
		NpcMood? mood = Get(npcName);
		return mood == null ? "" : $"\n【今天的心情】{MoodWord(mood.Temper)}：{mood.Because}。不用把心情说出口，让它渗进口气里：不痛快时话少、慢吞吞，高兴时轻快、爱多说两句。\n";
	}

	/// <summary>心情偏低（烦躁或低落）。</summary>
	public bool IsLow(string npcName) => Get(npcName)?.Temper is "melancholic" or "irritated";

	/// <summary>心情偏高（开心或兴奋）。</summary>
	public bool IsHigh(string npcName) => Get(npcName)?.Temper is "excited" or "happy";

	public static string MoodWord(string baseMood) => baseMood switch
	{
		"excited" => "兴奋",
		"happy" => "开心",
		"irritated" => "烦躁",
		"melancholic" => "低落",
		_ => "平静"
	};

	private static string WeatherWord(string weather) => weather switch
	{
		"Rainy" => "下雨",
		"Stormy" => "暴风雨",
		"Snowy" => "下雪",
		_ => "大晴"
	};

	private static string SeasonWord(string season) => season.ToLowerInvariant() switch
	{
		"spring" => "春天",
		"summer" => "夏天",
		"fall" => "秋天",
		"winter" => "冬天",
		_ => season
	};
}
