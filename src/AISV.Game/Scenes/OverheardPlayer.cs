using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Scenes;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Scenes;

internal enum OverheardOutcome
{
	Heard,
	NotHeard,
	Aborted
}

/// <summary>
/// 放旁听的头顶气泡：按时间表一段段显示，两个人时说话时互相转身。单人模式下开着菜单就暂停；离开地图或开始过场就中断。
/// 每段说完时人在听得见的地方就算听到了。
/// </summary>
internal sealed class OverheardPlayer
{
	private sealed class Playback
	{
		public Playback(string sceneId, GameLocation location, IReadOnlyList<OverheardCue> cues, IReadOnlyList<string> speakers, Action<OverheardOutcome> done)
		{
			SceneId = sceneId;
			Location = location;
			Cues = cues;
			Speakers = speakers;
			EndTick = cues[^1].StartTick + cues[^1].DurationTicks;
			Done = done;
		}

		public string SceneId { get; }

		public GameLocation Location { get; }

		public IReadOnlyList<OverheardCue> Cues { get; }

		public IReadOnlyList<string> Speakers { get; }

		public int EndTick { get; }

		public Action<OverheardOutcome> Done { get; }

		public int Next { get; set; }

		public int Paused { get; set; }

		public NPC? Speaking { get; set; }

		public bool InRange { get; set; }

		public int SpeakingUntil { get; set; }

		public bool Heard { get; set; }
	}

	private readonly ILog _log;

	private Playback? _playback;

	public OverheardPlayer(ILog log)
	{
		_log = log;
	}

	public bool Playing => _playback != null;

	public bool Start(string sceneId, GameLocation location, IReadOnlyList<SceneLine> lines, int tick, Action<OverheardOutcome> done)
	{
		if (_playback != null)
		{
			return false;
		}
		string farmer = Game1.player?.Name ?? "";
		IReadOnlyList<OverheardCue> cues = SceneBubbles.Timeline(lines.Select(l => l with { Text = l.Text.Replace("@", farmer) }).ToList(), tick);
		if (cues.Count == 0)
		{
			return false;
		}
		_playback = new Playback(sceneId, location, cues, cues.Select(c => c.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), done);
		return true;
	}

	public void Tick(int tick)
	{
		Playback? p = _playback;
		if (p == null)
		{
			return;
		}
		Farmer? player = Game1.player;
		if (player == null || player.currentLocation != p.Location)
		{
			Finish(OverheardOutcome.Aborted, "玩家离开了这张地图");
			return;
		}
		if (Game1.eventUp || Game1.CurrentEvent != null)
		{
			Finish(OverheardOutcome.Aborted, "开始过场了");
			return;
		}
		if (Game1.activeClickableMenu != null && !Game1.IsMultiplayer)
		{
			p.Paused++;
			return;
		}
		int now = tick - p.Paused;
		if (p.Speaking != null)
		{
			p.InRange &= InHearing(p.Speaking, p.Location, player);
			if (now >= p.SpeakingUntil)
			{
				EndCue(p);
			}
		}
		for (; p.Next < p.Cues.Count && p.Cues[p.Next].StartTick <= now; p.Next++)
		{
			OverheardCue cue = p.Cues[p.Next];
			p.Speaking = Show(p, cue);
			p.InRange = p.Speaking != null && InHearing(p.Speaking, p.Location, player);
			p.SpeakingUntil = cue.StartTick + cue.DurationTicks;
		}
		if (p.Next >= p.Cues.Count && now >= p.EndTick)
		{
			EndCue(p);
			Finish(p.Heard ? OverheardOutcome.Heard : OverheardOutcome.NotHeard, "放完了");
		}
	}

	public void Abort(string reason)
	{
		if (_playback != null)
		{
			Finish(OverheardOutcome.Aborted, reason);
		}
	}

	private static void EndCue(Playback p)
	{
		if (p.Speaking != null && p.InRange)
		{
			p.Heard = true;
		}
		p.Speaking = null;
	}

	private NPC? Show(Playback p, OverheardCue cue)
	{
		NPC? npc = MomentWorld.Villager(p.Location, cue.Speaker);
		if (npc == null)
		{
			_log.Debug($"偶遇 {p.SceneId}：{cue.Speaker} 已经不在这儿，跳过这句。");
			return null;
		}
		try
		{
			NpcSpeech.Say(npc, cue.Text, (int)Math.Round(cue.DurationTicks * 1000.0 / 60.0));
			if (p.Speakers.Count == 2 && p.Speakers.FirstOrDefault(n => !string.Equals(n, npc.Name, StringComparison.OrdinalIgnoreCase)) is string other && MomentWorld.Villager(p.Location, other) is NPC listener)
			{
				Turn(npc, listener);
				Turn(listener, npc);
			}
			return npc;
		}
		catch (Exception ex)
		{
			_log.Debug($"偶遇 {p.SceneId}：{npc.Name} 的气泡没显示出来：{ex.Message}");
			return null;
		}
	}

	private static void Turn(NPC npc, NPC target)
	{
		if (npc.isMoving() || npc.Sprite?.CurrentAnimation != null)
		{
			return;
		}
		Vector2 d = target.Tile - npc.Tile;
		int facing = Math.Abs(d.X) > Math.Abs(d.Y) ? d.X > 0 ? 1 : 3 : d.Y > 0 ? 2 : 0;
		if (npc.FacingDirection != facing)
		{
			npc.faceDirection(facing);
		}
	}

	private static bool InHearing(NPC npc, GameLocation location, Farmer player) =>
		npc.currentLocation == location && Vector2.Distance(npc.Tile, player.Tile) <= MomentRules.HearingTiles && MomentWorld.OnScreen(npc);

	private void Finish(OverheardOutcome outcome, string reason)
	{
		Playback? p = _playback;
		if (p == null)
		{
			return;
		}
		_playback = null;
		_log.Debug($"旁听 {p.SceneId} 结束：{reason}。");
		try
		{
			p.Done(outcome);
		}
		catch (Exception ex)
		{
			_log.Warn($"旁听 {p.SceneId} 收尾出错：{ex.Message}");
		}
	}
}

internal static class MomentWorld
{
	public static NPC? Villager(GameLocation location, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string n = name.Trim();
		return location.characters.FirstOrDefault(c => c.IsVillager && !c.IsInvisible && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase));
	}

	public static bool OnScreen(NPC npc)
	{
		xTile.Dimensions.Rectangle v = Game1.viewport;
		Vector2 p = npc.Position;
		return p.X + 64f > v.X && p.X < v.X + v.Width && p.Y + 64f > v.Y && p.Y - 64f < v.Y + v.Height;
	}

	public static bool InView(NPC npc, Farmer player, int maxTiles) => Vector2.Distance(npc.Tile, player.Tile) <= maxTiles && OnScreen(npc);
}
