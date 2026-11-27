using System;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Net;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Companions;

/// <summary>约见、帮工落在某位农夫身上的结果：好感、退钱、提示、村民冒的一句、算不算放鸽子。</summary>
public sealed record CompanionEffect(string Npc)
{
	public int Friendship { get; init; }

	public int Money { get; init; }

	public string Hud { get; init; } = "";

	public string Line { get; init; } = "";

	public bool StoodUp { get; init; }
}

/// <summary>
/// 联机时约见和帮工都由房主排（村民在房主那边走动），记录里写着是和哪位农夫（0 = 房主）。
/// 结果在那位农夫自己那边生效：房主自己的直接做，农场帮手的发过去。
/// </summary>
public static class CompanionNet
{
	public const string EffectOp = "companion.effect";

	public static NetSync? Net { get; set; }

	/// <summary>放鸽子算进做错事的后果（在被放鸽子的那位农夫那边算）。</summary>
	public static Action<string>? StoodUp { get; set; }

	/// <summary>这边的农夫在记录里的编号：房主是 0。</summary>
	public static long Me => Context.IsMainPlayer ? 0 : Game1.player?.UniqueMultiplayerID ?? 0;

	/// <summary>农场帮手连着房主：约见、帮工交给房主排。</summary>
	public static bool Remote => Context.IsWorldReady && !Context.IsMainPlayer && Net?.Linked == true;

	/// <summary>记录里的农夫（不在线时为 null）。</summary>
	public static Farmer? FarmerOf(long id) => id == 0 ? Game1.MasterPlayer : Game1.GetPlayer(id, onlyOnline: true);

	private static ILog _log = new NullLog();

	public static void Wire(NetSync net, ILog log)
	{
		Net = net;
		_log = log;
		net.On<CompanionEffect>(EffectOp, (_, e) => Apply(e));
	}

	public static void Send(long farmer, CompanionEffect e)
	{
		if (farmer == 0 || Net == null)
		{
			Apply(e);
		}
		else
		{
			Net.ToFarmer(farmer, EffectOp, e);
		}
	}

	private static void Apply(CompanionEffect e)
	{
		NPC? npc = Game1.getCharacterFromName(e.Npc);
		if (Game1.player != null && e.Friendship != 0)
		{
			int before = Relationships.FriendshipPoints(e.Npc);
			Relationships.ChangeFriendship(Game1.player, e.Npc, e.Friendship);
			_log.Debug($"{e.Npc} 的好感 {e.Friendship:+#;-#}：{before} → {Relationships.FriendshipPoints(e.Npc)}（{(npc == null ? "村民不在这边" : "村民在 " + npc.currentLocation?.NameOrUniqueName)}）。");
		}
		if (e.Money > 0 && Game1.player != null)
		{
			Game1.player.Money += e.Money;
		}
		if (e.Line.Length > 0 && npc != null && npc.currentLocation == Game1.currentLocation)
		{
			NpcSpeech.Say(npc, e.Line);
		}
		if (e.Hud.Length > 0)
		{
			Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(e.Hud), HUDMessage.newQuest_type));
		}
		if (e.StoodUp)
		{
			StoodUp?.Invoke(e.Npc);
		}
	}
}
