using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Consequences;

/// <summary>算这一次后果需要的外部情况。</summary>
public sealed record PlanInput(int ItemValue, int Money, int SettingPenalty, ReactionStyle Style, bool ArrestAllowed, bool OwnerIsSomeoneElse);

/// <summary>算出来的这一次后果（都是"另外再加"的部分）。</summary>
public sealed record ConsequencePlan(
	int Strike,
	int Friendship,
	int OwnerFriendship,
	int Mood,
	int Enmity,
	int Fine,
	int ClosedDays,
	bool Arrest,
	bool Letter,
	bool Gossip,
	bool Memory,
	int CooldownDays,
	bool Hud);

public static class ConsequenceRules
{
	/// <summary>年×4+季：后果次数按季清零。</summary>
	public static int SeasonKey(int year, int seasonIndex) => year * 4 + seasonIndex;

	public static ConsequencePlan Plan(MisdeedSpec spec, int strike, PlanInput input)
	{
		MisdeedTier t = spec.Tier(strike);
		int friendship = t.FriendshipFromSetting
			? (int)Math.Round(Math.Clamp(input.SettingPenalty, 0, 250) * ReactionStyles.FriendshipFactor(input.Style) * t.FriendshipScale)
			: (int)Math.Round(t.Friendship * t.FriendshipScale);
		int owner = input.OwnerIsSomeoneElse ? (int)Math.Round(friendship * t.OwnerShare) : 0;
		int mood = t.MoodByStyle ? -ReactionStyles.MoodDrop(input.Style) : t.Mood;
		int fine = t.Fine == null ? 0 : Math.Min(Math.Max(0, input.Money), Fine(t.Fine, input.ItemValue, input.Money));
		return new ConsequencePlan(strike, Math.Max(0, friendship), Math.Max(0, owner), Math.Max(0, mood), Math.Max(0, t.Enmity), fine, Math.Max(0, t.ClosedDays),
			t.Arrest && input.ArrestAllowed, t.Letter, t.Gossip, t.Memory, Math.Max(0, t.CooldownDays), t.Hud);
	}

	/// <summary>罚款（还没按身上的钱封顶）。</summary>
	public static int Fine(FineRule f, int itemValue, int money)
	{
		double baseAmount = f.Basis == "wallet" ? Math.Max(0, money) * f.Rate / 100.0 : Math.Max(0, itemValue) * f.Rate;
		int clamped = (int)Math.Clamp(Math.Round(baseAmount), f.Min, Math.Max(f.Min, f.Max));
		return (int)Math.Round(clamped * f.Multiplier);
	}
}

/// <summary>同一季犯过几次（按农夫、种类、对象分开）。</summary>
public sealed class StrikeEntry
{
	public long Farmer { get; set; }

	public string Kind { get; set; } = "";

	/// <summary>对象（村民名）；空 = 不分对象。</summary>
	public string Target { get; set; } = "";

	public int Season { get; set; }

	public int Count { get; set; }
}

/// <summary>一次后果的记录：信里写什么、八卦说什么都从这里来。</summary>
public sealed class ConsequenceRecord
{
	public string Id { get; set; } = "";

	public string Kind { get; set; } = "";

	public int Day { get; set; }

	public long Farmer { get; set; }

	public string FarmerName { get; set; } = "";

	/// <summary>当事村民（认出来的人、被放鸽子的人……）。</summary>
	public string Npc { get; set; } = "";

	/// <summary>失主（和当事村民不同时）。</summary>
	public string Owner { get; set; } = "";

	public string Item { get; set; } = "";

	public int Strike { get; set; }

	public int Fine { get; set; }

	public int ClosedDays { get; set; }

	public bool Arrest { get; set; }

	public string Style { get; set; } = "";

	/// <summary>这条记录自己的信（空 = 不寄信）。</summary>
	public string MailId { get; set; } = "";
}

/// <summary>一段时间里不肯做某事（不借钱、不约见、配偶不帮忙）。</summary>
public sealed class Cooldown
{
	public string Key { get; set; } = "";

	public int Until { get; set; }
}

/// <summary>后果的存档（town/consequences.json）。</summary>
public sealed class ConsequenceState
{
	public const string Path = "town/consequences.json";

	public static readonly DocumentType<ConsequenceState> Doc = new DocumentType<ConsequenceState>("做错事的后果", 1, () => new ConsequenceState());

	/// <summary>记录留这么多天。</summary>
	public const int KeepDays = 112;

	public int NextId { get; set; } = 1;

	public List<StrikeEntry> Strikes { get; set; } = new List<StrikeEntry>();

	public List<ConsequenceRecord> Records { get; set; } = new List<ConsequenceRecord>();

	public List<Cooldown> Cooldowns { get; set; } = new List<Cooldown>();

	/// <summary>其他 mod 的 NPC 让 AI 判断过的反应方式。</summary>
	public Dictionary<string, string> Styles { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>等空下来就被带走的农夫。</summary>
	public List<long> ArrestPending { get; set; } = new List<long>();

	/// <summary>小超市里旧的赃物次数已经搬过来了。</summary>
	public bool MigratedMart { get; set; }

	/// <summary>这一季第几次（先加一再返回）。</summary>
	public int AddStrike(long farmer, string kind, string target, int season)
	{
		StrikeEntry? e = Strikes.FirstOrDefault(s => s.Farmer == farmer && Same(s.Kind, kind) && Same(s.Target, target));
		if (e == null)
		{
			e = new StrikeEntry { Farmer = farmer, Kind = kind, Target = target, Season = season };
			Strikes.Add(e);
		}
		if (e.Season != season)
		{
			e.Season = season;
			e.Count = 0;
		}
		e.Count++;
		return e.Count;
	}

	public int StrikesOf(long farmer, string kind, string target, int season) =>
		Strikes.FirstOrDefault(s => s.Farmer == farmer && Same(s.Kind, kind) && Same(s.Target, target) && s.Season == season)?.Count ?? 0;

	public ConsequenceRecord Add(ConsequenceRecord r, bool letter)
	{
		r.Id = "k" + NextId++;
		if (letter)
		{
			r.MailId = "AIStardewValley.Core_Consequence_" + r.Id;
		}
		Records.Add(r);
		return r;
	}

	public void Block(string key, int until)
	{
		Cooldown? c = Cooldowns.FirstOrDefault(x => Same(x.Key, key));
		if (c == null)
		{
			Cooldowns.Add(new Cooldown { Key = key, Until = until });
		}
		else
		{
			c.Until = Math.Max(c.Until, until);
		}
	}

	public bool Blocked(string key, int today) => Cooldowns.Any(c => Same(c.Key, key) && c.Until > today);

	/// <summary>换天：清掉过期的冷却和太久的记录（信件 id 还在邮箱里的留着）。</summary>
	public void Trim(int today, Func<string, bool>? mailPending = null)
	{
		Cooldowns.RemoveAll(c => c.Until <= today);
		Records.RemoveAll(r => today - r.Day > KeepDays && (r.MailId.Length == 0 || !(mailPending?.Invoke(r.MailId) ?? false)));
	}

	public static string CooldownKey(string what, long farmer, string target = "") => $"{what}:{farmer}:{target}";

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
