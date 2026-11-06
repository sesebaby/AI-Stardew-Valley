using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Social;

/// <summary>生日信、生日短信用哪种口吻。</summary>
public enum BirthdayTone
{
	Friend,
	Romance,
	Spouse
}

/// <summary>可能在农夫生日时来信或发消息的一位村民。</summary>
public sealed record BirthdayCandidate(string NpcName, int Hearts, bool HasPhone, bool Partner, bool Spouse);

/// <summary>农夫生日。</summary>
public static class BirthdayRules
{
	public const int DaysPerSeason = 28;

	public const int TextMinHearts = 4;

	public const int LetterMinHearts = 6;

	public const int PoolSize = 5;

	public const int Variants = 4;

	private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

	public static int SeasonIndex(string? season) => string.IsNullOrWhiteSpace(season) ? -1 : Array.FindIndex(Seasons, s => s.Equals(season.Trim(), StringComparison.OrdinalIgnoreCase));

	public static bool IsValidDate(string? season, int day) => SeasonIndex(season) >= 0 && day >= 1 && day <= DaysPerSeason;

	public static bool IsBirthday(string? birthdaySeason, int birthdayDay, string? season, int day) =>
		IsValidDate(birthdaySeason, birthdayDay) && day == birthdayDay && SeasonIndex(birthdaySeason) == SeasonIndex(season);

	/// <summary>Data/Festivals/FestivalDates 里的键（如 spring13）。</summary>
	public static string FestivalKey(string season, int day) => $"{season.Trim().ToLowerInvariant()}{day}";

	/// <summary>在 0（不设）和 1～28 之间往前或往后挪一天，跳过节日。</summary>
	public static int StepDay(int day, int direction, Func<int, bool> isFestival)
	{
		direction = direction >= 0 ? 1 : -1;
		int d = Math.Clamp(day, 0, DaysPerSeason);
		for (int i = 0; i <= DaysPerSeason; i++)
		{
			d = (d + direction + DaysPerSeason + 1) % (DaysPerSeason + 1);
			if (d == 0 || !isFestival(d))
			{
				return d;
			}
		}
		return 0;
	}

	/// <summary>设的那天是节日就顺延到下一个能用的日子。</summary>
	public static int SettleDay(int day, Func<int, bool> isFestival)
	{
		if (day <= 0 || day > DaysPerSeason)
		{
			return 0;
		}
		return isFestival(day) ? StepDay(day, 1, isFestival) : day;
	}

	public static BirthdayTone ToneOf(bool spouse, bool partner) => spouse ? BirthdayTone.Spouse : partner ? BirthdayTone.Romance : BirthdayTone.Friend;

	public static string ToneKey(BirthdayTone tone) => tone switch
	{
		BirthdayTone.Spouse => "spouse",
		BirthdayTone.Romance => "romance",
		_ => "friend"
	};

	public static int VariantIndex(double roll) => Math.Clamp((int)Math.Floor(Math.Clamp(roll, 0.0, 0.999999) * Variants), 0, Variants - 1);

	/// <summary>一半概率 1 位，一半 2 位。</summary>
	public static int SenderCount(double roll) => roll < 0.5 ? 1 : 2;

	public static string LetterTrigger(int year) => $"player_birthday_{year}";

	public static bool IsLetterTrigger(string? trigger) => trigger?.StartsWith("player_birthday_", StringComparison.OrdinalIgnoreCase) ?? false;

	public static IReadOnlyList<BirthdayCandidate> PickLetterSenders(IEnumerable<BirthdayCandidate> candidates, int count, IReadOnlyList<double> rolls)
	{
		List<BirthdayCandidate> all = candidates.Where(c => !string.IsNullOrWhiteSpace(c.NpcName)).ToList();
		List<BirthdayCandidate> chosen = all.Where(c => c.Spouse).Take(1).ToList();
		List<BirthdayCandidate> pool = all.Where(c => !c.Spouse && c.Hearts >= LetterMinHearts)
			.OrderByDescending(c => c.Hearts).ThenBy(c => c.NpcName, StringComparer.OrdinalIgnoreCase).Take(PoolSize).ToList();
		Draw(chosen, pool, Math.Max(count, chosen.Count), rolls);
		return chosen;
	}

	public static IReadOnlyList<BirthdayCandidate> PickTextSenders(IEnumerable<BirthdayCandidate> candidates, IEnumerable<string> exclude, int count, IReadOnlyList<double> rolls)
	{
		HashSet<string> excluded = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
		List<BirthdayCandidate> all = candidates.Where(c => !string.IsNullOrWhiteSpace(c.NpcName) && c.HasPhone && c.Hearts >= TextMinHearts)
			.OrderByDescending(c => c.Hearts).ThenBy(c => c.NpcName, StringComparer.OrdinalIgnoreCase).ToList();
		List<BirthdayCandidate> pool = all.Where(c => !excluded.Contains(c.NpcName)).Take(PoolSize).ToList();
		if (pool.Count == 0)
		{
			pool = all.Take(PoolSize).ToList();
		}
		List<BirthdayCandidate> chosen = new List<BirthdayCandidate>();
		Draw(chosen, pool, count, rolls);
		return chosen;
	}

	/// <summary>在 9:00～12:00 之间排时间（10 分钟一档），从早到晚。</summary>
	public static IReadOnlyList<int> PlanTextTimes(int count, IReadOnlyList<double> rolls)
	{
		const int from = 540, to = 720;
		List<int> times = new List<int>();
		for (int i = 0; i < count; i++)
		{
			double roll = i < rolls.Count ? rolls[i] : 0.0;
			int minutes = from + (int)Math.Round(Math.Clamp(roll, 0.0, 1.0) * (to - from) / 10.0) * 10;
			times.Add(minutes / 60 * 100 + minutes % 60);
		}
		times.Sort();
		return times;
	}

	/// <summary>生日当天的开场白提示：今天是农夫生日，加上自己早上寄过的信、发过的消息。</summary>
	public static string OpeningBlock(string npcDisplay, string player, string sentLetterItem, bool sentLetter, bool texted)
	{
		List<string> parts = new List<string>
		{
			$"今天{player}过生日。这句开场里带上这件事，用你自己的方式：熟络的也许热热闹闹地道喜，不太熟的也许客气一句，嘴硬的可以拐个弯说。今天不是普通的日子。"
		};
		string gift = GiftLetterBlock(npcDisplay, player, sentLetterItem, sentLetter);
		if (gift.Length > 0)
		{
			parts.Add(gift);
		}
		if (texted)
		{
			parts.Add(TextedBlock(player));
		}
		return string.Join("\n", parts);
	}

	/// <summary>生日当天回复时额外的要求。</summary>
	public static string ReplyBlock(string npcDisplay, string player, string sentLetterItem, bool sentLetter, bool texted)
	{
		List<string> parts = new List<string>
		{
			$"今天是{player}的生日，这会儿{player}正在跟你说话。\n" +
			"先接住对方刚才那句话，生日只是让回答多一点温度：交情深的放在心上，交情浅的点到为止，害羞、打趣、别扭都行，看你是什么性子。记录里能看到你今天已经道过喜的话，就别再像头一回那样祝一遍。\n" +
			"别凭空说起派对、蛋糕或者别人送的礼，只说你自己做了什么、想了什么，也别许下其实不会兑现的礼物。打分照平常的规则，生日本身不是加分的理由；今天不提请求，favor_asked 写 null。"
		};
		string gift = GiftLetterBlock(npcDisplay, player, sentLetterItem, sentLetter);
		if (gift.Length > 0)
		{
			parts.Add(gift);
		}
		if (texted)
		{
			parts.Add(TextedBlock(player));
		}
		return string.Join("\n\n", parts);
	}

	/// <summary>对话记录里给这一轮加的说明。</summary>
	public static string HistoryNote(string player) => $"[那天是{player}的生日。]";

	private static string GiftLetterBlock(string npcDisplay, string player, string item, bool sentLetter)
	{
		if (!sentLetter)
		{
			return "";
		}
		return item.Length > 0
			? $"早上那封生日信是你（{npcDisplay}）寄的，里面夹着你送给{player}的{item}。礼是你送出去的，{player}什么也没送你，所以轮不到你道谢。信寄没寄到你心里有数，用不着问对方收没收到；想知道喜不喜欢可以问，也可以等对方提。人家道谢就照你的性格应着，人家没提也不打紧，说不定信还没拆。今天别再另外送东西。"
			: $"早上你（{npcDisplay}）给{player}寄过一封生日信，没附东西；{player}那边没寄什么给你。可以顺口带一句，也可以等对方说起，但别像忘了似的问对方收到没有。";
	}

	private static string TextedBlock(string player) => $"今早你已经在手机上给{player}发过生日祝福（记录里有），这回不必再从头祝一遍，提一下那条消息就好。";

	private static void Draw(List<BirthdayCandidate> chosen, List<BirthdayCandidate> pool, int count, IReadOnlyList<double> rolls)
	{
		int i = 0;
		while (chosen.Count < count && pool.Count > 0)
		{
			double roll = i < rolls.Count ? rolls[i] : 0.0;
			i++;
			int index = Math.Clamp((int)Math.Floor(Math.Clamp(roll, 0.0, 0.999999) * pool.Count), 0, pool.Count - 1);
			chosen.Add(pool[index]);
			pool.RemoveAt(index);
		}
	}
}
