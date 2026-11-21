using System;
using System.Text.Json;

namespace AISV.Core.Social;

/// <summary>宠物心里话的规则：多久冒一句、心情对哪个表情、怎么读模型的回答。</summary>
public static class PetThoughtRules
{
	/// <summary>两句心里话之间至少隔多少游戏分钟。</summary>
	public const int GapMinutes = 60;

	/// <summary>每句最多多少字。</summary>
	public const int MaxChars = 24;

	public static int Minutes(int time) => time / 100 * 60 + time % 100;

	/// <summary>今天还能不能再冒一句：没到上限、离上一句够久（lastTime &lt; 0 表示今天还没说过）。</summary>
	public static bool CanThink(int countToday, int daily, int lastTime, int now)
	{
		return countToday < Math.Max(1, daily) && (lastTime < 0 || Minutes(now) - Minutes(lastTime) >= GapMinutes);
	}

	/// <summary>掷骰：roll 是 [0,1) 的随机数。</summary>
	public static bool Rolls(double roll, int percent) => roll * 100 < Math.Clamp(percent, 0, 100);

	public static readonly string[] Moods = { "happy", "bored", "hungry", "sleepy", "curious", "sad" };

	/// <summary>心情对应的游戏表情编号（开心、无聊、饿、困、好奇、委屈）。</summary>
	public static int Emote(string? mood) => (mood ?? "").Trim().ToLowerInvariant() switch
	{
		"happy" or "开心" => 32,
		"bored" or "无聊" => 40,
		"hungry" or "饿" => 36,
		"sleepy" or "困" => 24,
		"curious" or "好奇" => 8,
		"sad" or "委屈" => 28,
		_ => 32
	};

	/// <summary>读 {"mood":"…","line":"…"}；不是 JSON 时把整段当成那句话（心情按开心）。读不出来返回 null。</summary>
	public static (string Mood, string Line)? Parse(string? text)
	{
		string raw = (text ?? "").Trim();
		if (raw.Length == 0)
		{
			return null;
		}
		int start = raw.IndexOf('{');
		int end = raw.LastIndexOf('}');
		if (start >= 0 && end > start)
		{
			try
			{
				using JsonDocument doc = JsonDocument.Parse(raw.Substring(start, end - start + 1));
				JsonElement root = doc.RootElement;
				string mood = root.TryGetProperty("mood", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
				string line = root.TryGetProperty("line", out JsonElement l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
				line = Clean(line);
				return line.Length == 0 ? null : (mood.Trim().ToLowerInvariant(), line);
			}
			catch (JsonException)
			{
				return null;
			}
		}
		string only = Clean(raw);
		return only.Length == 0 ? null : ("happy", only);
	}

	private static string Clean(string line)
	{
		string t = line.Trim().Trim('"', '“', '”', '（', '）', '(', ')').Trim();
		return t.Length > MaxChars ? t.Substring(0, MaxChars) : t;
	}
}
