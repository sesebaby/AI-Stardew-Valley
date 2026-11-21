using System;
using System.Collections.Generic;
using StardewValley;

namespace AISV.Game.Npcs;

/// <summary>
/// "这位村民现在被别的事占着吗"统一问这里：各个系统登记自己的判断（群聊、日程覆盖、小超市顾客、配偶日程、
/// 别的 mod 的店员顾客……），也可以直接按名字占住一位村民。主动走过来、群聊、日程覆盖、挑顾客都先问它。
/// </summary>
public static class NpcBusy
{
	private static readonly List<(string Source, Func<string, bool> Busy)> Sources = new List<(string, Func<string, bool>)>();

	private static readonly Dictionary<string, string> Holds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>登记一个判断；同名的来源会被替换。</summary>
	public static void Register(string source, Func<string, bool> busy)
	{
		Sources.RemoveAll(s => s.Source == source);
		Sources.Add((source, busy));
	}

	/// <summary>占住一位村民（reason 只用于日志和调试）。</summary>
	public static void Hold(string npc, string reason)
	{
		if (!string.IsNullOrWhiteSpace(npc))
		{
			Holds[npc] = reason;
		}
	}

	public static void Release(string npc)
	{
		if (!string.IsNullOrWhiteSpace(npc))
		{
			Holds.Remove(npc);
		}
	}

	/// <summary>放掉某个原因占住的所有村民（换天、回标题时）。</summary>
	public static void ReleaseAll(string reason)
	{
		List<string> names = new List<string>();
		foreach (KeyValuePair<string, string> h in Holds)
		{
			if (h.Value == reason)
			{
				names.Add(h.Key);
			}
		}
		foreach (string n in names)
		{
			Holds.Remove(n);
		}
	}

	public static bool IsHeld(string npc, string? reason = null) => Holds.TryGetValue(npc, out string? r) && (reason == null || r == reason);

	/// <summary>谁占着他（没有为 null）。</summary>
	public static string? Why(string npc)
	{
		if (string.IsNullOrWhiteSpace(npc))
		{
			return null;
		}
		if (Holds.TryGetValue(npc, out string? held))
		{
			return held;
		}
		foreach ((string source, Func<string, bool> busy) in Sources)
		{
			try
			{
				if (busy(npc))
				{
					return source;
				}
			}
			catch
			{
				// 某个系统的判断出错不影响别的。
			}
		}
		return null;
	}

	public static bool Is(string npc) => Why(npc) != null;

	/// <summary>只看登记的判断、不看 Hold：日程覆盖自己带着走的人（顾客、配偶日程）用 Hold 占住，覆盖本身不该被它挡住。</summary>
	public static bool IsBySources(string npc)
	{
		foreach ((string _, Func<string, bool> busy) in Sources)
		{
			try
			{
				if (busy(npc))
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	/// <summary>别的 mod 临时放出来的替身（比如 Farmer's Store 的顾客、店员），不是真村民。</summary>
	public static bool IsStandIn(NPC? npc) => npc != null && (npc.Name.StartsWith("COS_NPC_", StringComparison.OrdinalIgnoreCase) || npc.Name.StartsWith("COS_EMP_", StringComparison.OrdinalIgnoreCase) || npc.modData.ContainsKey(StandInKey));

	/// <summary>我们自己放的替身（离家出走的孩子、在院子里玩的孩子）打这个标记。</summary>
	public const string StandInKey = "AIStardewValley.Core/StandIn";
}
