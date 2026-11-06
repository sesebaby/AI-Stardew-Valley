using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Scenes;
using AISV.Core.Stories;
using AISV.Game.Dialogue;
using Microsoft.Xna.Framework;
using StardewValley;

namespace AISV.Game.Stories;

/// <summary>把小剧场的结局在游戏里演出来：必要时先把农夫传过去，演完再传回原地。</summary>
public static class ConclusionScenePlayer
{
	private const int MaxAttempts = 24;

	private sealed record Origin(string Location, int X, int Y, int Facing, int Day);

	private sealed class Pending
	{
		public Action? Done;

		public Origin? From;

		public bool Warped;

		public bool Started;
	}

	private static readonly Dictionary<string, Pending> Waiting = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);

	private static bool _registered;

	private static ILog _log = NullLog.Instance;

	/// <summary>开演。没有能演的台词时返回 false（<paramref name="done"/> 不会被调用）。</summary>
	public static bool TryStart(StoryArc arc, Action? done, ILog log)
	{
		_log = log;
		List<ConclusionLine> lines = ConclusionScript.Playable(arc, Game1.player?.Name ?? "Farmer");
		if (lines.Count == 0)
		{
			return false;
		}
		Register();
		string location = ConclusionScript.Location(arc);
		string id = ConclusionScript.EventId(arc.Id);
		Waiting[id] = new Pending { Done = done, From = Snapshot() };
		string script = ConclusionScript.Build(lines, ConclusionScript.LayoutFor(location, arc.FinalStage), id);
		DelayedAction.functionAfterDelay(() => Attempt(location, script, id, false, 0), 100);
		return true;
	}

	private static void Attempt(string locationName, string script, string id, bool warped, int attempts)
	{
		if (!Waiting.TryGetValue(id, out Pending? pending) || pending.Started)
		{
			return;
		}
		if (attempts >= MaxAttempts)
		{
			_log.Warn("结局场景一直等不到开演的时机，跳过。");
			Finish(id);
			return;
		}
		GameLocation? target = Game1.getLocationFromName(locationName) ?? (string.Equals(Game1.currentLocation?.NameOrUniqueName, locationName, StringComparison.OrdinalIgnoreCase) ? Game1.currentLocation : null);
		if (target == null || !EventPlacement.TrySafe(script, (x, y) => Walkable(target, x, y), out string safe))
		{
			_log.Warn($"结局场景在 {locationName} 找不到站的地方，跳过。");
			Finish(id);
			return;
		}
		if (Game1.activeClickableMenu != null || Game1.eventUp || Game1.currentLocation?.currentEvent != null)
		{
			DelayedAction.functionAfterDelay(() => Attempt(locationName, safe, id, warped, attempts + 1), 250);
			return;
		}
		if (!string.Equals(Game1.currentLocation?.NameOrUniqueName, target.NameOrUniqueName, StringComparison.OrdinalIgnoreCase))
		{
			if (!warped)
			{
				pending.Warped = true;
				EventPlacement.TryRead(safe, out EventPlacement.Actor farmer, out _);
				// 一到就开演，免得进门时游戏自己的心事件抢先。
				LocationRequest request = Game1.getLocationRequest(target.NameOrUniqueName);
				request.OnLoad += () =>
				{
					if (!pending.Started && !Game1.eventUp && target.currentEvent == null)
					{
						Start(target, safe, id);
					}
				};
				Game1.warpFarmer(request, farmer.X, farmer.Y, farmer.Facing);
			}
			DelayedAction.functionAfterDelay(() => Attempt(target.NameOrUniqueName, safe, id, true, attempts + 1), warped ? 500 : 900);
			return;
		}
		Start(target, safe, id);
	}

	private static void Start(GameLocation location, string script, string id)
	{
		if (!Waiting.TryGetValue(id, out Pending? p))
		{
			return;
		}
		try
		{
			location.startEvent(new Event(script, Game1.player));
			p.Started = true;
		}
		catch (Exception ex)
		{
			_log.Warn("结局场景放不了：" + ex.Message);
			Finish(id);
		}
	}

	private static void Register()
	{
		if (_registered)
		{
			return;
		}
		_registered = true;
		Event.RegisterCommand(ConclusionScript.CompleteCommand, (Event ev, string[] args, EventContext _) =>
		{
			int skip = args.Length > 0 && string.Equals(args[0], ConclusionScript.CompleteCommand, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
			string id = skip < args.Length ? args[skip] : "";
			ev.CurrentCommand++;
			if (Waiting.ContainsKey(id))
			{
				DelayedAction.functionAfterDelay(() => Finish(id), 500);
			}
		});
	}

	private static void Finish(string id)
	{
		if (!Waiting.Remove(id, out Pending? p))
		{
			return;
		}
		Origin? o = p.From;
		if (p.Warped && o != null && o.Day == GameState.Today.TotalDays)
		{
			ReturnWhenFree(o, p.Done, 0);
		}
		else
		{
			DelayedAction.functionAfterDelay(() => p.Done?.Invoke(), 100);
		}
	}

	/// <summary>等事件自己退场（它会把农夫放回开演前的位置）以后，再传回原地。</summary>
	private static void ReturnWhenFree(Origin o, Action? done, int attempts)
	{
		if ((Game1.eventUp || Game1.currentLocation?.currentEvent != null || Game1.isWarping || Game1.fadeToBlack || Game1.globalFade) && attempts < MaxAttempts)
		{
			DelayedAction.functionAfterDelay(() => ReturnWhenFree(o, done, attempts + 1), 250);
			return;
		}
		GameLocation? back = Game1.getLocationFromName(o.Location);
		if (back != null && o.Day == GameState.Today.TotalDays && EventPlacement.TryTile((x, y) => Walkable(back, x, y), o.X, o.Y, new HashSet<(int, int)>(), out (int X, int Y) t))
		{
			Game1.warpFarmer(back.NameOrUniqueName, t.X, t.Y, o.Facing);
			DelayedAction.functionAfterDelay(() => done?.Invoke(), 900);
			return;
		}
		DelayedAction.functionAfterDelay(() => done?.Invoke(), 100);
	}

	private static Origin? Snapshot()
	{
		GameLocation? here = Game1.currentLocation;
		Farmer? player = Game1.player;
		if (here == null || player == null)
		{
			return null;
		}
		Point tile = player.TilePoint;
		return new Origin(here.NameOrUniqueName, tile.X, tile.Y, player.FacingDirection, GameState.Today.TotalDays);
	}

	private static bool Walkable(GameLocation l, int x, int y)
	{
		Vector2 t = new Vector2(x, y);
		try
		{
			return x >= 0 && y >= 0 && l.isTileOnMap(t) && l.isTilePassable(t);
		}
		catch
		{
			return false;
		}
	}
}
