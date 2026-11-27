using System;
using System.Linq;
using AISV.Core.Awareness;
using AISV.Core.Consequences;
using AISV.Core.Text;
using AISV.Game.Awareness;
using StardewValley;

namespace AISV.Game.Consequences;

/// <summary>各处发现的事交到这里：约见、托付、欠款、偷窃、垃圾桶、调情。</summary>
public static class ConsequenceSources
{
	private static Misdeed Base(string kind, string npc, string target = "") => new Misdeed
	{
		Kind = kind,
		Farmer = Game1.player.UniqueMultiplayerID,
		FarmerName = Game1.player.Name,
		Npc = npc,
		Target = target
	};

	/// <summary>约好了没去（伴侣剧情的约见不算，它有自己的后果）。</summary>
	public static void MissedMeetup(this ConsequenceService c, string npc) => c.Report(Base(Misdeeds.MissedMeetup, npc, npc));

	/// <summary>托付、请求到期没办。</summary>
	public static void FavorExpired(this ConsequenceService c, string npc) => c.Report(Base(Misdeeds.FavorExpired, npc, npc));

	/// <summary>一笔债到期没还（每笔只算一次）。</summary>
	public static void DebtOverdue(this ConsequenceService c, string npc) => c.Report(Base(Misdeeds.DebtOverdue, npc));

	/// <summary>偷东西当场被抓。</summary>
	public static void TheftCaught(this ConsequenceService c, string npc) => c.Report(Base(Misdeeds.TheftCaught, npc));

	/// <summary>"看到听到"记下了一件有人看见的事：翻垃圾桶、当着恋人的面调情。</summary>
	public static void OnSighting(this ConsequenceService c, Observation o, Sighting e)
	{
		if (o.Actor != Game1.player?.Name)
		{
			return;
		}
		switch (e.Type)
		{
			case "trash" when CanOwner(o.Detail("can")) is (string owner, string place):
				c.Report(Base(Misdeeds.Trash, owner, owner) with { Place = place });
				break;
			case "flirt" when o.Detail("partner") is { Length: > 0 } partner && o.Detail("with") is { Length: > 0 } other:
				// 当面质问由"看到听到"管（恋人、配偶走过来说），这里只加额外的轻重和冷淡期。
				c.Report(Base(Misdeeds.Flirt, partner, partner) with { Owner = other });
				break;
		}
	}

	/// <summary>垃圾桶是谁家门口的（原版八个桶）。</summary>
	private static (string Owner, string Place)? CanOwner(string can) => can switch
	{
		"JodiAndKent" => ("Jodi", Tr.Get("consequence.can.jodi", "乔迪和肯特家")),
		"EmilyAndHaley" => ("Emily", Tr.Get("consequence.can.emily", "艾米丽和海莉家")),
		"Mayor" => ("Lewis", Tr.Get("consequence.can.mayor", "刘易斯镇长家")),
		"Museum" => ("Gunther", Tr.Get("consequence.can.museum", "博物馆")),
		"Blacksmith" => ("Clint", Tr.Get("consequence.can.blacksmith", "克林特的铁匠铺")),
		"Saloon" => ("Gus", Tr.Get("consequence.can.saloon", "星之果实餐吧")),
		"Evelyn" => ("Evelyn", Tr.Get("consequence.can.evelyn", "艾芙琳和乔治家")),
		"JojaMart" => ("Morris", Tr.Get("consequence.can.joja", "Joja 超市")),
		_ => null
	};
}
