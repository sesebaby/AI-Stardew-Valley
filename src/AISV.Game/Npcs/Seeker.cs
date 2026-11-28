using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace AISV.Game.Npcs;

/// <summary>
/// 村民去找农夫：同一张地图走过去；农夫在隔壁的屋里就从门口进去（那张地图没人看着时按路程等一会儿直接进门，
/// 有人看着就真的走到门口）；陪着走时跟着换地图。矿洞这类地方不进，农夫的家要够熟才进。只在房主那边动村民。
/// </summary>
public static class Seeker
{
	/// <summary>清掉村民身上让他走不动的标志（日程末尾的动作、停顿）：日程改写那边提供。</summary>
	public static Action<NPC>? Unfreeze { get; set; }

	/// <summary>from 里通往 to 的门：outside 是 from 里走到这儿就算进门的格子，inside 是进门后在 to 里站的格子。</summary>
	public static bool TryDoor(GameLocation from, GameLocation to, out Point outside, out Point inside)
	{
		outside = inside = Point.Zero;
		Point? arrive = null;
		bool found = false;
		if (to.ParentBuilding is Building b && b.GetParentLocation() == from)
		{
			outside = new Point(b.tileX.Value + b.humanDoor.X, b.tileY.Value + b.humanDoor.Y + 1);
			found = true;
		}
		if (!found)
		{
			foreach (KeyValuePair<Point, string> d in from.doors.Pairs)
			{
				if (Leads(d.Value, to))
				{
					outside = new Point(d.Key.X, d.Key.Y + 1);
					found = true;
					break;
				}
			}
		}
		if (!found)
		{
			foreach (Warp w in from.warps)
			{
				if (Leads(w.TargetName, to))
				{
					outside = new Point(Math.Clamp(w.X, 0, Math.Max(0, from.Map.Layers[0].LayerWidth - 1)), Math.Clamp(w.Y, 0, Math.Max(0, from.Map.Layers[0].LayerHeight - 1)));
					arrive = new Point(w.TargetX, w.TargetY);
					found = true;
					break;
				}
			}
		}
		if (!found)
		{
			return false;
		}
		if (arrive == null)
		{
			foreach (Warp w in to.warps)
			{
				if (Leads(w.TargetName, from))
				{
					arrive = new Point(w.X, w.Y - 1);
					break;
				}
			}
		}
		Point start = arrive ?? Point.Zero;
		inside = OpenNear(to, start, null) ?? start;
		return arrive != null;
	}

	private static bool Leads(string? target, GameLocation to) =>
		!string.IsNullOrWhiteSpace(target) && (string.Equals(target.Trim(), to.NameOrUniqueName, StringComparison.OrdinalIgnoreCase) || string.Equals(target.Trim(), to.Name, StringComparison.OrdinalIgnoreCase));

	/// <summary>村民能不能进这张地图：矿洞、赌场、更衣室这类不进；农夫的家要够熟（伴侣、恋人随便进）。</summary>
	public static bool MayEnter(GameLocation to, NPC npc)
	{
		if (CompanyRules.NoFollow(to.NameOrUniqueName) || to is MineShaft or VolcanoDungeon)
		{
			return false;
		}
		if (to is FarmHouse house && house.owner is Farmer owner)
		{
			Friendship? f = owner.friendshipData.TryGetValue(npc.Name, out Friendship? got) ? got : null;
			bool partner = owner.spouse == npc.Name || f?.IsDating() == true || f?.IsEngaged() == true || f?.IsMarried() == true;
			return CompanyRules.MayEnterHome(owner.getFriendshipHeartLevelForNPC(npc.Name), partner);
		}
		return true;
	}

	/// <summary>农夫在隔壁、村民进得去：能从村民这张地图找过去。</summary>
	public static bool CanReach(NPC npc, GameLocation? there)
	{
		GameLocation? here = npc.currentLocation;
		return here != null && there != null && (here == there || MayEnter(there, npc) && TryDoor(here, there, out _, out _));
	}

	/// <summary>center 附近 3 格内能站的一格（离 from 最近的优先）。</summary>
	public static Point? OpenNear(GameLocation loc, Point center, NPC? moving, Point? from = null, Point? avoid = null)
	{
		Point? best = null;
		float bestScore = float.MaxValue;
		for (int r = 0; r <= 3; r++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
					{
						continue;
					}
					Point t = new Point(center.X + dx, center.Y + dy);
					if (t == avoid || !Open(loc, t, moving))
					{
						continue;
					}
					float score = r * 100 + (from is Point f ? Vector2.Distance(new Vector2(t.X, t.Y), new Vector2(f.X, f.Y)) : 0);
					if (score < bestScore)
					{
						bestScore = score;
						best = t;
					}
				}
			}
			if (best != null)
			{
				return best;
			}
		}
		return null;
	}

	private static bool Open(GameLocation loc, Point t, NPC? moving)
	{
		Vector2 v = new Vector2(t.X, t.Y);
		try
		{
			return t.X >= 0 && t.Y >= 0 && loc.isTileOnMap(v) && loc.isTilePassable(v) && !loc.characters.Any(c => c != moving && c.TilePoint == t)
				&& !loc.farmers.Any(f => f.TilePoint == t);
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>把村民放到别的地图的某一格（进门那一下）。</summary>
	public static void Put(NPC npc, GameLocation to, Point tile)
	{
		npc.controller = null;
		npc.Halt();
		Game1.warpCharacter(npc, to, new Vector2(tile.X, tile.Y));
		if (Game1.currentLocation == to)
		{
			to.localSound("doorClose");
		}
	}

	/// <summary>我们自己的寻路，用来和日程、别的 mod 的区分开。</summary>
	public sealed class Step : PathFindController
	{
		public Step(Character c, GameLocation location, Point end, int facing) : base(c, location, end, facing)
		{
		}
	}

	public enum Result
	{
		/// <summary>到身边了。</summary>
		Beside,
		/// <summary>还在路上（或者正要进门）。</summary>
		Walking,
		/// <summary>刚进了农夫那张地图。</summary>
		Entered,
		/// <summary>那张地图不进（矿洞、不够熟的家）。</summary>
		Refused,
		/// <summary>找不过去（不相邻、走不通）。</summary>
		Lost
	}

	/// <summary>一位村民去找、跟着一位农夫。每 15 tick 调一次 <see cref="Tick"/>；结束时 <see cref="Release"/> 恢复速度。</summary>
	public sealed class Escort
	{
		private readonly int _speed;

		private Point _farmerAt = new Point(-1, -1);

		private int _fails;

		private int _doorAt = -1;

		private string _doorFor = "";

		private Point _lastTile = new Point(-1, -1);

		private int _still;

		public Escort(NPC npc)
		{
			Npc = npc;
			_speed = npc.speed;
		}

		public NPC Npc { get; }

		/// <summary>anywhere：农夫去了不相邻的地图（矿车、图腾、坐车）也跟过去（陪着走时）；否则只找隔壁的屋。</summary>
		public Result Tick(Farmer farmer, bool anywhere, int closeTiles = 1)
		{
			GameLocation? here = Npc.currentLocation, there = farmer.currentLocation;
			if (here == null || there == null)
			{
				return Result.Lost;
			}
			if (here != there)
			{
				return Cross(farmer, here, there, anywhere);
			}
			_doorFor = "";
			float d = Vector2.Distance(Npc.Tile, farmer.Tile);
			if (d <= closeTiles + 0.5f)
			{
				StopWalking();
				Npc.faceGeneralDirection(farmer.getStandingPosition(), 0, false, false);
				return Result.Beside;
			}
			Npc.speed = d > 5 ? 5 : 3;
			Point ft = farmer.TilePoint;
			if (Npc.controller is Step walking && Stalled(walking, d))
			{
				return d <= 4 ? Result.Beside : Result.Walking;
			}
			if (Npc.controller is Step && Math.Abs(ft.X - _farmerAt.X) + Math.Abs(ft.Y - _farmerAt.Y) <= 1)
			{
				return Result.Walking;
			}
			_farmerAt = ft;
			Point? spot = OpenNear(there, ft, Npc, Npc.TilePoint, ft);
			if (spot is Point s)
			{
				if (s == Npc.TilePoint)
				{
					StopWalking();
					return Result.Beside;
				}
				Step step = new Step(Npc, there, s, -1);
				if (step.pathToEndPoint != null && step.pathToEndPoint.Count > 0)
				{
					Unfreeze?.Invoke(Npc);
					Npc.controller = step;
					_fails = 0;
					return Result.Walking;
				}
			}
			if (++_fails >= 6)
			{
				// 走不通：离得近就算到了；远的话没人看着这一格时挪过去。
				if (d <= 4)
				{
					return Result.Beside;
				}
				if (spot is Point near && !Watched(there, Npc))
				{
					Put(Npc, there, near);
					_fails = 0;
					return Result.Walking;
				}
				return Result.Lost;
			}
			return Result.Walking;
		}

		private Result Cross(Farmer farmer, GameLocation here, GameLocation there, bool anywhere)
		{
			if (!MayEnter(there, Npc))
			{
				return Result.Refused;
			}
			if (!TryDoor(here, there, out Point outside, out Point inside))
			{
				if (!anywhere)
				{
					return Result.Lost;
				}
				if (!Waited(there, 90))
				{
					return Result.Walking;
				}
				Put(Npc, there, OpenNear(there, farmer.TilePoint, Npc, avoid: farmer.TilePoint) ?? farmer.TilePoint);
				return Result.Entered;
			}
			if (here.farmers.Any())
			{
				// 有人看着这张地图：真的走到门口再进。
				if (Math.Abs(Npc.TilePoint.X - outside.X) + Math.Abs(Npc.TilePoint.Y - outside.Y) <= 1)
				{
					Put(Npc, there, inside);
					return Result.Entered;
				}
				if (Npc.controller is not Step)
				{
					Point? door = OpenNear(here, outside, Npc, Npc.TilePoint);
					Step step = door is Point p ? new Step(Npc, here, p, -1) : null!;
					if (step?.pathToEndPoint != null && step.pathToEndPoint.Count > 0)
					{
						Unfreeze?.Invoke(Npc);
						Npc.speed = 4;
						Npc.controller = step;
					}
					else if (++_fails >= 6)
					{
						Put(Npc, there, inside);
						return Result.Entered;
					}
				}
				return Result.Walking;
			}
			// 没人看着：按走到门口的路程等一会儿，直接进门。
			int tiles = Math.Abs(Npc.TilePoint.X - outside.X) + Math.Abs(Npc.TilePoint.Y - outside.Y);
			if (!Waited(there, Math.Clamp(tiles * 12, 30, 360)))
			{
				return Result.Walking;
			}
			Put(Npc, there, inside);
			return Result.Entered;
		}

		/// <summary>
		/// 走着走着卡住（和别的村民叠在一起时谁也走不动）：45 tick 没挪窝，离得近就算到了，
		/// 不然顺着路往前挪一格。
		/// </summary>
		private bool Stalled(Step walking, float d)
		{
			Point t = Npc.TilePoint;
			if (t != _lastTile)
			{
				_lastTile = t;
				_still = 0;
				return false;
			}
			if (++_still < 3)
			{
				return false;
			}
			_still = 0;
			if (d <= 4)
			{
				StopWalking();
				return true;
			}
			if (walking.pathToEndPoint is { Count: > 0 } path)
			{
				Point next = path.Pop();
				Npc.Position = new Vector2(next.X * 64f, next.Y * 64f);
			}
			return true;
		}

		/// <summary>去 there 之前等够 ticks 了没有（换了目标重新数）。</summary>
		private bool Waited(GameLocation there, int ticks)
		{
			if (_doorFor != there.NameOrUniqueName)
			{
				_doorFor = there.NameOrUniqueName;
				_doorAt = Game1.ticks + ticks;
				StopWalking();
			}
			if (Game1.ticks < _doorAt)
			{
				return false;
			}
			_doorFor = "";
			return true;
		}

		private static bool Watched(GameLocation loc, NPC npc) => loc.farmers.Any(f => Vector2.Distance(f.Tile, npc.Tile) < 20);

		private void StopWalking()
		{
			if (Npc.controller is Step)
			{
				Npc.controller = null;
			}
			Npc.Halt();
			Npc.speed = _speed;
		}

		public void Release()
		{
			StopWalking();
			Npc.speed = _speed;
		}
	}
}
