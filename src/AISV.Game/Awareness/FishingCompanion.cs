using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Awareness;
using AISV.Core.Diagnostics;
using AISV.Core.Text;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

namespace AISV.Game.Awareness;

/// <summary>
/// 看见玩家钓鱼的人偶尔走到岸边一起钓：钓上一两条普通的鱼，走过来交给玩家，然后回去接着走日程。
/// 有握竿姿势图的村民钓鱼时换成正面握竿的样子，其他人用平常的样子；鱼竿、鱼线和浮漂用游戏的素材画上去。
/// </summary>
internal sealed class FishingCompanion
{
	private const int CatchIntervalTicks = 180;

	private const int FinishPauseTicks = 60;

	private const int RetargetTicks = 60;

	private const int RippleTicks = 120;

	private const int ReturnStallTicks = 120;

	private const int ReturnTimeoutTicks = 1200;

	private const int MaxActivityTicks = 10800;

	private const int MaxPathTiles = 4096;

	private const int CastDistance = 4;

	private const int WaterSearchRadius = 8;

	private const string FallbackFish = "(O)145";

	private static readonly HashSet<string> Children = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jas", "Leo", "Vincent" };

	private enum Stage
	{
		ToShore,
		Fishing,
		ToPlayer,
		Returning
	}

	private sealed class Outing
	{
		public Outing(NPC npc, GameLocation location, (int X, int Y) stand, (int X, int Y) cast, int depth, int target, int tick)
		{
			Npc = npc;
			Location = location;
			Stand = stand;
			Cast = cast;
			Depth = depth;
			TargetCatches = target;
			StartPosition = npc.Position;
			StartFacing = npc.FacingDirection;
			StartController = npc.controller;
			StartIgnoreSchedule = npc.ignoreScheduleToday;
			StartedTick = tick;
			NextAction = tick;
			LastPosition = npc.Position;
			LastProgressTick = tick;
		}

		public NPC Npc { get; }

		public GameLocation Location { get; }

		public (int X, int Y) Stand { get; set; }

		public (int X, int Y) Cast { get; set; }

		public int Depth { get; set; }

		public int TargetCatches { get; }

		public List<SObject> Fish { get; } = new List<SObject>();

		public Vector2 StartPosition { get; }

		public int StartFacing { get; }

		public PathFindController? StartController { get; }

		public bool StartIgnoreSchedule { get; }

		public int StartedTick { get; }

		public Stage Stage { get; set; }

		public int NextAction { get; set; }

		public int NextRipple { get; set; }

		public int LastCatchTick { get; set; } = -1000;

		public int ReturnStarted { get; set; }

		public Vector2 LastPosition { get; set; }

		public int LastProgressTick { get; set; }

		public PathFindController? Walk { get; set; }

		public (int X, int Y)? PlayerSpot { get; set; }
	}

	/// <summary>沿算好的路走；挡住了就报告，由上面重新规划。</summary>
	private sealed class ShorePath : PathFindController
	{
		private readonly NPC _npc;

		private readonly Point _target;

		private int _blockedTicks;

		public ShorePath(NPC npc, GameLocation location, Point target, Stack<Point> route)
			: base(route, npc, location)
		{
			_npc = npc;
			_target = target;
			endPoint = target;
		}

		public bool IsBlocked => _blockedTicks >= 60;

		public override bool update(GameTime time)
		{
			if (pathToEndPoint == null || pathToEndPoint.Count == 0)
			{
				return true;
			}
			Vector2 before = _npc.Position;
			bool done = base.update(time);
			_blockedTicks = _npc.Position == before ? _blockedTicks + 1 : 0;
			return done;
		}
	}

	private readonly Func<bool> _isEnabled;

	private readonly Func<int> _chancePercent;

	private readonly Func<string, bool> _isBusy;

	private readonly ILog _log;

	private readonly Func<string, Texture2D?> _pose;

	private readonly FishingInvitations _invitations = new FishingInvitations();

	private readonly Dictionary<string, int> _lastDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _seenThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private Outing? _outing;

	private string _session = "";

	private bool _sessionDone;

	private static readonly Rectangle RodCell = new Rectangle(240, 336, 48, 48);

	private Rectangle _rodSource;

	private Texture2D? _rodTexture;

	public FishingCompanion(Func<bool> isEnabled, Func<int> chancePercent, Func<string, bool> isBusy, Func<string, Texture2D?> pose, ILog log)
	{
		_pose = pose;
		_isEnabled = isEnabled;
		_chancePercent = chancePercent;
		_isBusy = isBusy;
		_log = log;
	}

	/// <summary>最近一次没人来的原因（调试看）。</summary>
	public string LastRefusal { get; private set; } = "";

	public bool IsNpcBusy(string npc) => _outing != null && Same(_outing.Npc.Name, npc);

	// ---- 这次抛竿谁看到了 ----

	/// <summary>这次抛竿已经有人来了，后面不用再记。</summary>
	public bool SessionFinished(string session)
	{
		if (_session != session)
		{
			_session = session;
			_seenThisSession.Clear();
			_sessionDone = false;
		}
		return _sessionDone;
	}

	public bool Unseen(string npc) => !_seenThisSession.Contains(npc);

	public void MarkSeen(string npc) => _seenThisSession.Add(npc);

	/// <summary>依次看目击者里有没有人愿意来；一次抛竿最多来一个。</summary>
	public bool TryInvite(Sighting e, IEnumerable<Witness> witnesses)
	{
		foreach (Witness w in witnesses.OrderByDescending(w => w.Confidence).ThenBy(w => w.TilesAway))
		{
			if (TryStart(e, w.Who))
			{
				_sessionDone = true;
				return true;
			}
		}
		return false;
	}

	private bool TryStart(Sighting e, string name)
	{
		if (_outing != null || !_isEnabled())
		{
			return false;
		}
		Farmer player = Game1.player;
		GameLocation? location = Game1.currentLocation;
		if (player == null || location == null)
		{
			return false;
		}
		int day = AwarenessSystem.Today;
		NPC? npc = location.getCharacterFromName(name);
		if (npc == null || !Eligible(npc, day))
		{
			LastRefusal = name + " 正忙或今天来过了";
			return false;
		}
		if (!_invitations.CanTry(e.Detail("fishingSession"), npc.Name, Game1.ticks, _chancePercent(), () => Game1.random.Next(100)))
		{
			LastRefusal = npc.Name + " 没掷中";
			return false;
		}
		if (!TryPlan(location, player.Tile, npc, out (int X, int Y) stand, out (int X, int Y) cast, out int depth))
		{
			LastRefusal = npc.Name + " 走不到岸边";
			return false;
		}
		Outing outing = new Outing(npc, location, stand, cast, Math.Max(1, depth), Game1.random.Next(1, 3), Game1.ticks);
		npc.ignoreScheduleToday = true;
		if (!StartPath(outing, stand))
		{
			npc.ignoreScheduleToday = outing.StartIgnoreSchedule;
			return false;
		}
		_outing = outing;
		_lastDay[npc.Name] = day;
		_log.Debug(npc.Name + " 决定到玩家旁边一起钓鱼。");
		return true;
	}

	private bool Eligible(NPC npc, int day) =>
		npc.IsVillager && !npc.IsInvisible && !npc.isSleeping.Value && !npc.ignoreScheduleToday && npc.temporaryController == null && !_isBusy(npc.Name) && !AISV.Game.Npcs.SquadMark.IsControlled(npc)
		&& (!_lastDay.TryGetValue(npc.Name, out int last) || day - last >= 1);

	public void Reset(string why)
	{
		if (_outing != null)
		{
			Abort(why);
		}
		_lastDay.Clear();
		_invitations.Reset();
		_seenThisSession.Clear();
		_session = "";
		_sessionDone = false;
		_rodTexture = null;
	}

	/// <summary>存档之类不能带着半截活动过去：立刻放回去。</summary>
	public void Abort(string why)
	{
		Outing? o = _outing;
		if (o == null)
		{
			return;
		}
		_log.Debug($"{o.Npc.Name} 不钓了：{why}。");
		FishingPose.Hide();
		Restore(o);
		_outing = null;
	}

	private static void Restore(Outing o)
	{
		NPC npc = o.Npc;
		npc.Halt();
		npc.controller = null;
		npc.ignoreScheduleToday = o.StartIgnoreSchedule;
		if (npc.currentLocation == o.Location)
		{
			npc.Position = o.StartPosition;
			npc.faceDirection(o.StartFacing);
		}
		if (o.StartController != null)
		{
			npc.controller = o.StartController;
		}
		else
		{
			npc.checkSchedule(Game1.timeOfDay);
		}
	}

	// ---- 每帧 ----

	public void OnUpdateTicked()
	{
		Outing? o = _outing;
		if (o == null)
		{
			return;
		}
		if (!_isEnabled() || !Context.IsWorldReady || !Context.IsMainPlayer || Game1.eventUp || Game1.currentLocation?.currentEvent != null || o.Npc.currentLocation != o.Location)
		{
			Abort("情况变了");
			return;
		}
		int tick = Game1.ticks;
		if (tick - o.StartedTick > MaxActivityTicks && o.Stage != Stage.Returning)
		{
			Abort("耗得太久");
			return;
		}
		switch (o.Stage)
		{
		case Stage.ToShore:
			TickToShore(o, tick);
			break;
		case Stage.Fishing:
			TickFishing(o, tick);
			break;
		case Stage.ToPlayer:
			TickToPlayer(o, tick);
			break;
		case Stage.Returning:
			TickReturning(o, tick);
			break;
		}
	}

	private void TickToShore(Outing o, int tick)
	{
		if (tick < o.NextAction)
		{
			return;
		}
		o.NextAction = tick + RetargetTicks;
		if (!IsSafe(o.Location, o.Stand, o.Npc) && !Arrived(o.Npc, o.Stand, 0.45f))
		{
			if (TryPlan(o.Location, Game1.player.Tile, o.Npc, out (int X, int Y) stand, out (int X, int Y) cast, out int depth))
			{
				o.Stand = stand;
				o.Cast = cast;
				o.Depth = depth;
				StartPath(o, stand);
			}
			return;
		}
		if (!Arrived(o.Npc, o.Stand, 0.45f))
		{
			if (o.Npc.controller == null || o.Walk is ShorePath { IsBlocked: true })
			{
				StartPath(o, o.Stand);
			}
			return;
		}
		o.Npc.Halt();
		o.Npc.controller = null;
		Face(o.Npc, o.Cast);
		o.Stage = Stage.Fishing;
		if (_pose(o.Npc.Name) is Texture2D pose)
		{
			FishingPose.Show(o.Npc, pose);
		}
		o.NextAction = tick + CatchIntervalTicks;
		AddRipple(o);
		o.NextRipple = tick + RippleTicks;
	}

	private void TickFishing(Outing o, int tick)
	{
		o.Npc.Halt();
		Face(o.Npc, o.Cast);
		if (tick >= o.NextRipple)
		{
			AddRipple(o);
			o.NextRipple = tick + RippleTicks;
		}
		if (tick < o.NextAction)
		{
			return;
		}
		if (o.Fish.Count < o.TargetCatches)
		{
			o.Fish.Add(CatchFish(o));
			o.LastCatchTick = tick;
			AddRipple(o);
			o.Npc.jumpWithoutSound(2f);
			o.NextAction = tick + CatchIntervalTicks;
		}
		else if (tick >= o.NextAction + FinishPauseTicks)
		{
			FishingPose.Hide();
			o.Stage = Stage.ToPlayer;
			o.NextAction = tick;
			RefreshPlayerPath(o, tick);
		}
	}

	private void TickToPlayer(Outing o, int tick)
	{
		Farmer player = Game1.player;
		if (player?.currentLocation != o.Location)
		{
			Abort("玩家走了");
			return;
		}
		if (Vector2.Distance(o.Npc.Tile, player.Tile) <= 1.65f && !o.Npc.GetBoundingBox().Intersects(player.GetBoundingBox()))
		{
			Deliver(o, player, tick);
		}
		else if (tick >= o.NextAction)
		{
			RefreshPlayerPath(o, tick);
		}
	}

	private void TickReturning(Outing o, int tick)
	{
		bool home = Vector2.DistanceSquared(o.Npc.Position, o.StartPosition) < 64f * 64f;
		if (home || tick - o.ReturnStarted > ReturnTimeoutTicks)
		{
			_log.Debug(o.Npc.Name + " 钓完了，回去接着走日程。");
			Restore(o);
			_outing = null;
			return;
		}
		if (Vector2.DistanceSquared(o.Npc.Position, o.LastPosition) >= 16f)
		{
			o.LastPosition = o.Npc.Position;
			o.LastProgressTick = tick;
		}
		else if (tick - o.LastProgressTick >= ReturnStallTicks && tick >= o.NextAction)
		{
			o.NextAction = tick + RetargetTicks;
			if (!StartPath(o, ((int)(o.StartPosition.X / 64f), (int)(o.StartPosition.Y / 64f))))
			{
				Restore(o);
				_outing = null;
			}
		}
	}

	private void Deliver(Outing o, Farmer player, int tick)
	{
		o.Npc.Halt();
		o.Npc.controller = null;
		o.Npc.faceTowardFarmerForPeriod(900, 3, false, player);
		foreach (SObject fish in o.Fish)
		{
			Item? left = player.addItemToInventory(fish);
			if (left != null)
			{
				Game1.createItemDebris(left, player.Position, player.FacingDirection, o.Location);
			}
		}
		_log.Debug($"{o.Npc.Name} 把 {o.Fish.Count} 条鱼交给了玩家。");
		NpcSpeech.Say(o.Npc, Tr.Get("awareness.fishing.farewell", "这些给你！下次再一起钓。"));
		o.Stage = Stage.Returning;
		o.ReturnStarted = tick;
		o.LastPosition = o.Npc.Position;
		o.LastProgressTick = tick;
		o.NextAction = tick + RetargetTicks;
		if (!StartPath(o, ((int)(o.StartPosition.X / 64f), (int)(o.StartPosition.Y / 64f))))
		{
			Restore(o);
			_outing = null;
		}
	}

	private static SObject CatchFish(Outing o)
	{
		for (int i = 0; i < 8; i++)
		{
			try
			{
				Item fish = GameLocation.GetFishFromLocationData(o.Location.Name, new Vector2(o.Cast.X, o.Cast.Y), o.Depth, Game1.player, false, false, o.Location);
				if (fish is SObject { Category: -4 } item && string.IsNullOrWhiteSpace(item.SetFlagOnPickup) && !(item.TryGetTempData("IsBossFish", out bool boss) && boss) && !item.HasContextTag("fish_legendary") && !item.HasContextTag("fish_legendary_family"))
				{
					item.Stack = 1;
					item.Quality = 0;
					return item;
				}
			}
			catch (Exception)
			{
				break;
			}
		}
		return ItemRegistry.Create(FallbackFish) as SObject ?? new SObject("145", 1);
	}

	private void RefreshPlayerPath(Outing o, int tick)
	{
		o.NextAction = tick + RetargetTicks;
		Vector2 p = Game1.player.Tile;
		(int X, int Y) me = ((int)o.Npc.Tile.X, (int)o.Npc.Tile.Y);
		foreach ((int X, int Y) t in new[] { ((int)p.X + 1, (int)p.Y), ((int)p.X - 1, (int)p.Y), ((int)p.X, (int)p.Y + 1), ((int)p.X, (int)p.Y - 1) }.OrderBy(t => Math.Abs(t.Item1 - me.X) + Math.Abs(t.Item2 - me.Y)))
		{
			if (!IsSafe(o.Location, t, o.Npc))
			{
				continue;
			}
			if (o.PlayerSpot == t && o.Npc.controller != null && o.Walk is ShorePath { IsBlocked: false })
			{
				return;
			}
			if (StartPath(o, t))
			{
				o.PlayerSpot = t;
				return;
			}
		}
	}

	// ---- 找地方、走路 ----

	private static bool StartPath(Outing o, (int X, int Y) target)
	{
		(int X, int Y) start = ((int)o.Npc.Tile.X, (int)o.Npc.Tile.Y);
		if (!TryRoute(start, target, t => IsSafe(o.Location, t, o.Npc), out Stack<Point> route))
		{
			return false;
		}
		o.Npc.Halt();
		ShorePath walk = new ShorePath(o.Npc, o.Location, new Point(target.X, target.Y), route);
		o.Npc.controller = walk;
		o.Walk = walk;
		return true;
	}

	private static bool TryPlan(GameLocation location, Vector2 playerTile, NPC npc, out (int X, int Y) stand, out (int X, int Y) cast, out int depth)
	{
		stand = default;
		cast = default;
		depth = 0;
		if (location.Map == null || location.Map.Layers.Count == 0)
		{
			return false;
		}
		int width = location.Map.Layers[0].LayerWidth;
		int height = location.Map.Layers[0].LayerHeight;
		(int X, int Y)? nearest = null;
		float best = float.MaxValue;
		for (int y = (int)playerTile.Y - WaterSearchRadius; y <= (int)playerTile.Y + WaterSearchRadius; y++)
		{
			for (int x = (int)playerTile.X - WaterSearchRadius; x <= (int)playerTile.X + WaterSearchRadius; x++)
			{
				float distance = Math.Abs(x - playerTile.X) + Math.Abs(y - playerTile.Y);
				if (x >= 0 && y >= 0 && x < width && y < height && distance < best && location.isWaterTile(x, y))
				{
					best = distance;
					nearest = (x, y);
				}
			}
		}
		if (nearest == null || WaterBody.Discover(nearest.Value, width, height, location.isWaterTile, Math.Min(65536, width * height)) is not WaterBody body)
		{
			return false;
		}
		Dictionary<(int X, int Y), bool> cache = new Dictionary<(int, int), bool>();
		bool Safe((int X, int Y) t)
		{
			if (!cache.TryGetValue(t, out bool safe))
			{
				safe = cache[t] = IsSafe(location, t, npc);
			}
			return safe;
		}
		HashSet<(int X, int Y)> reachable = Reachable(((int)npc.Tile.X, (int)npc.Tile.Y), Safe);
		foreach ((int X, int Y) t in body.Shore
			.Where(t => OnScreen(t) && Vector2.Distance(new Vector2(t.X, t.Y), playerTile) >= 2f)
			.OrderBy(t => Vector2.DistanceSquared(new Vector2(t.X, t.Y), npc.Tile)))
		{
			if (reachable.Contains(t) && Safe(t) && body.TryCast(t, CastDistance, out cast, out depth))
			{
				stand = t;
				return true;
			}
		}
		return false;
	}

	private static bool OnScreen((int X, int Y) t) =>
		t.X * 64 >= Game1.viewport.X + 32 && t.Y * 64 - 64 >= Game1.viewport.Y && (t.X + 1) * 64 <= Game1.viewport.X + Game1.viewport.Width - 32 && (t.Y + 1) * 64 <= Game1.viewport.Y + Game1.viewport.Height - 32;

	private static IEnumerable<(int X, int Y)> Around((int X, int Y) t)
	{
		yield return (t.X + 1, t.Y);
		yield return (t.X - 1, t.Y);
		yield return (t.X, t.Y + 1);
		yield return (t.X, t.Y - 1);
	}

	private static HashSet<(int X, int Y)> Reachable((int X, int Y) start, Func<(int X, int Y), bool> passable)
	{
		HashSet<(int X, int Y)> seen = new HashSet<(int, int)> { start };
		Queue<(int X, int Y)> queue = new Queue<(int, int)>();
		queue.Enqueue(start);
		while (queue.Count > 0 && seen.Count < MaxPathTiles)
		{
			foreach ((int X, int Y) n in Around(queue.Dequeue()))
			{
				if (!seen.Contains(n) && passable(n))
				{
					seen.Add(n);
					queue.Enqueue(n);
				}
			}
		}
		return seen;
	}

	private static bool TryRoute((int X, int Y) start, (int X, int Y) target, Func<(int X, int Y), bool> passable, out Stack<Point> route)
	{
		route = new Stack<Point>();
		if (!passable(target))
		{
			return false;
		}
		Dictionary<(int X, int Y), (int X, int Y)> from = new Dictionary<(int, int), (int, int)> { [start] = start };
		Queue<(int X, int Y)> queue = new Queue<(int, int)>();
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			(int X, int Y) t = queue.Dequeue();
			if (t == target)
			{
				while (t != start)
				{
					route.Push(new Point(t.X, t.Y));
					t = from[t];
				}
				return true;
			}
			foreach ((int X, int Y) n in Around(t))
			{
				if (!from.ContainsKey(n) && passable(n))
				{
					if (from.Count >= MaxPathTiles)
					{
						return false;
					}
					from[n] = t;
					queue.Enqueue(n);
				}
			}
		}
		return false;
	}

	private static readonly string[] BlockingLayers = { "Back", "Buildings", "Front" };

	private static readonly string[] BlockingProperties = { "NPCBarrier", "TemporaryBarrier", "NoPath", "NoPathing" };

	private static bool IsSafe(GameLocation location, (int X, int Y) t, NPC npc)
	{
		Vector2 tile = new Vector2(t.X, t.Y);
		if (!location.isTileOnMap(tile) || location.isWaterTile(t.X, t.Y) || !location.isTilePassable(tile) || location.warps.Any(w => w.X == t.X && w.Y == t.Y)
			|| !string.IsNullOrEmpty(location.doesTileHavePropertyNoNull(t.X, t.Y, "Passable", "Back")))
		{
			return false;
		}
		foreach (string layer in BlockingLayers)
		{
			foreach (string property in BlockingProperties)
			{
				if (!string.IsNullOrEmpty(location.doesTileHavePropertyNoNull(t.X, t.Y, property, layer)))
				{
					return false;
				}
			}
		}
		CollisionMask mask = ~(CollisionMask.Characters | CollisionMask.Farmers);
		if (location.IsTileOccupiedBy(tile, mask, mask))
		{
			return false;
		}
		Rectangle area = new Rectangle(t.X * 64, t.Y * 64, 64, 64);
		area.Inflate(4, 4);
		return !location.farmers.Any(f => f.GetBoundingBox().Intersects(area)) && !location.characters.Any(c => c != npc && c.GetBoundingBox().Intersects(area));
	}

	private static bool Arrived(NPC npc, (int X, int Y) t, float tolerance) => Vector2.Distance(npc.Tile, new Vector2(t.X, t.Y)) <= tolerance;

	private static void Face(NPC npc, (int X, int Y) t)
	{
		Vector2 d = new Vector2(t.X, t.Y) - npc.Tile;
		npc.faceDirection(Math.Abs(d.X) > Math.Abs(d.Y) ? (d.X > 0f ? 1 : 3) : (d.Y > 0f ? 2 : 0));
	}

	private static void AddRipple(Outing o) =>
		o.Location.temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 0, 64, 64), 150f, 8, 0, new Vector2(o.Cast.X * 64f, o.Cast.Y * 64f + 16f), false, false, 0.001f, 0.01f, Color.White, 0.75f, 0.003f, 0f, 0f));

	// ---- 画鱼竿 ----

	public void Draw(SpriteBatch b)
	{
		Outing? o = _outing;
		if (o == null || o.Stage != Stage.Fishing || o.Npc.IsInvisible || o.Npc.currentLocation != Game1.currentLocation)
		{
			return;
		}
		Rectangle box = o.Npc.GetBoundingBox();
		Vector2 hand = FishingPose.IsShowing(o.Npc)
			? new Vector2(box.Center.X, box.Bottom - 48f + o.Npc.yJumpOffset + (Children.Contains(o.Npc.Name) ? 8f : 0f))
			: new Vector2(box.Center.X, box.Top - 12f) + o.Npc.FacingDirection switch
			{
				1 => new Vector2(16f, 0f),
				3 => new Vector2(-16f, 0f),
				0 => new Vector2(-8f, -8f),
				_ => new Vector2(8f, 8f)
			};
		float lift = MathF.Sin(Math.Clamp((Game1.ticks - o.LastCatchTick) / 36f, 0f, 1f) * MathF.PI);
		Vector2 bobber = new Vector2(o.Cast.X * 64f + 32f, o.Cast.Y * 64f + 30f + MathF.Sin(Game1.ticks * 0.12f) * 2f);
		Vector2 toWater = bobber - hand;
		if (toWater.LengthSquared() <= 0.001f)
		{
			return;
		}
		toWater.Normalize();
		Vector2 direction = toWater * 52f + new Vector2(0f, -38f - lift * 30f);
		direction.Normalize();
		Texture2D texture = ItemRegistry.GetDataOrErrorItem("(T)BambooPole").GetTexture();
		if (texture.IsDisposed || Game1.bobbersTexture.IsDisposed)
		{
			return;
		}
		Rectangle rod = RodSource(texture);
		Vector2 from = Game1.GlobalToLocal(Game1.viewport, hand);
		Vector2 tip = Game1.GlobalToLocal(Game1.viewport, hand + direction * ((rod.Height - 1) * 4f));
		Vector2 end = Game1.GlobalToLocal(Game1.viewport, bobber);
		b.Draw(texture, from, rod, Color.Goldenrod, MathF.Atan2(direction.Y, direction.X) - MathF.PI / 2f, new Vector2(rod.Width / 2f, 0f), 4f, SpriteEffects.None, 0.99f);
		Vector2 lineEnd = end + new Vector2(0f, -10f);
		Vector2 span = lineEnd - tip;
		Vector2 c1 = tip + new Vector2(span.X / 3f, span.Y * 2f / 3f);
		Vector2 c2 = tip + new Vector2(span.X * 2f / 3f, span.Y * 6f / 5f);
		Vector2 last = tip;
		for (int i = 1; i <= 24; i++)
		{
			float t = i / 24f;
			float u = 1f - t;
			Vector2 p = u * u * u * tip + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * lineEnd;
			Line(b, last, p, Color.White * 0.5f);
			last = p;
		}
		Rectangle source = Game1.getSourceRectForStandardTileSheet(Game1.bobbersTexture, 0, 16, 32);
		source.Y += 16;
		source.Height = 16;
		b.Draw(Game1.bobbersTexture, end, source, Color.White, 0f, new Vector2(8f, 8f), 4f, SpriteEffects.None, 0.99f);
	}

	/// <summary>工具图集里竹竿那一格的实际像素范围。</summary>
	private Rectangle RodSource(Texture2D texture)
	{
		if (texture == _rodTexture)
		{
			return _rodSource;
		}
		Color[] pixels = new Color[RodCell.Width * RodCell.Height];
		texture.GetData(0, RodCell, pixels, 0, pixels.Length);
		Rectangle? found = null;
		for (int i = 0; i < pixels.Length; i++)
		{
			if (pixels[i].A > 0)
			{
				Rectangle dot = new Rectangle(RodCell.X + i % RodCell.Width, RodCell.Y + i / RodCell.Width, 1, 1);
				found = found == null ? dot : Rectangle.Union(found.Value, dot);
			}
		}
		_rodSource = found ?? RodCell;
		_rodTexture = texture;
		return _rodSource;
	}

	private static void Line(SpriteBatch b, Vector2 from, Vector2 to, Color color)
	{
		Vector2 d = to - from;
		float length = d.Length();
		if (length > 0.01f)
		{
			b.Draw(Game1.staminaRect, from, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(length, 1f), SpriteEffects.None, 0.99f);
		}
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
