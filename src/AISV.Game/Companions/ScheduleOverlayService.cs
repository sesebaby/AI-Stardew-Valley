using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.World;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace AISV.Game.Companions;

/// <summary>
/// 日程改写：不动村民的日程表，只在一段时间里接管他。按路程提前出发，走游戏的日程寻路到地方等着；
/// 到点恢复标志，走回日程此刻该在的地方。存档里只记"谁哪天几点去哪"，所以下雨、读档、换天都不用恢复备份。
/// 节日、生日、孩子不改写；只在主机上运行；同伴模块关掉时全部放回。
/// </summary>
public sealed class ScheduleOverlayService
{
	private const int ReturningChecks = 40;

	private sealed class Runtime
	{
		public bool IgnoreBefore;

		public PathFindController? Ours;

		public int Attempts;

		public Point LastTile = new Point(-1, -1);

		public int StillChecks;

		public int EstimatedMinutes = -1;

		public int WaitChecks;

		public int DoorTries;

		public int Hops;

		public string AttemptLocation = "";
	}

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly IReflectionHelper _reflection;

	private readonly Func<string, bool> _isBusyElsewhere;

	private readonly ILog _log;

	private readonly Dictionary<string, Runtime> _runtime = new Dictionary<string, Runtime>(StringComparer.Ordinal);

	private readonly HashSet<string> _sendHome = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _returning = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private HashSet<string> _eventsSeenAtDayStart = new HashSet<string>();

	private int _ticks;

	private int _startedDay = -1;

	public ScheduleOverlayService(Func<ModConfig> config, Func<DocumentStore?> store, IReflectionHelper reflection, Func<string, bool> isBusyElsewhere, ILog log)
	{
		_config = config;
		_store = store;
		_reflection = reflection;
		_isBusyElsewhere = isBusyElsewhere;
		_log = log;
	}

	/// <summary>一段改写结束时通知排它的功能。</summary>
	public event Action<ScheduleOverlay>? Closed;

	public int Version { get; private set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.CompanionModuleOn;
		}
	}

	private ScheduleOverlayState? State => _store()?.Get(ScheduleOverlayState.Path, ScheduleOverlayState.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public IReadOnlyList<ScheduleOverlay> All() => State?.Items.ToList() ?? new List<ScheduleOverlay>();

	public ScheduleOverlay? Find(string id) => State?.Items.FirstOrDefault(o => o.Id == id);

	/// <summary>这个村民此刻在路上或在等。</summary>
	public bool IsHolding(string npc) => State?.Items.Any(o => OverlayStates.IsHolding(o.State) && string.Equals(o.Npc, npc, StringComparison.OrdinalIgnoreCase)) ?? false;

	// ---------------- 生命周期 ----------------

	/// <summary>读档就是那天早上：过去没关的算没去成，接管中的重新排。</summary>
	public void OnSaveLoaded()
	{
		_runtime.Clear();
		_sendHome.Clear();
		_returning.Clear();
		_startedDay = -1;
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsOpen(o.State)))
		{
			if (o.Day < Today)
			{
				Close(o, OverlayStates.Cancelled, PlanReasons.Missed);
			}
			else if (OverlayStates.IsHolding(o.State))
			{
				o.State = OverlayStates.Planned;
				o.DepartedAt = -1;
				o.ArrivedAt = -1;
				o.ArrivedBy = "";
				MarkDirty();
			}
		}
		CaptureEventsSeen();
		Version++;
	}

	public void OnReturnedToTitle()
	{
		_runtime.Clear();
		_sendHome.Clear();
		_returning.Clear();
		_startedDay = -1;
	}

	public void OnDayStarted()
	{
		if (State is not ScheduleOverlayState state)
		{
			return;
		}
		_runtime.Clear();
		_sendHome.Clear();
		_returning.Clear();
		CaptureEventsSeen();
		_startedDay = Today;
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsOpen(o.State) && o.Day < Today))
		{
			Close(o, OverlayStates.Cancelled, PlanReasons.Missed);
		}
		if (state.Items.RemoveAll(o => !OverlayStates.IsOpen(o.State) && Today - o.Day > OverlayRules.ClosedKeepDays) > 0)
		{
			MarkDirty();
		}
		OnTimeChanged();
	}

	/// <summary>入夜：接管着的放手（换天会把人送回家、重算日程），没出发的算没去成。</summary>
	public void OnDayEnding()
	{
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsOpen(o.State) && o.Day <= Today))
		{
			if (OverlayStates.IsHolding(o.State))
			{
				RestoreFlags(o);
				Close(o, OverlayStates.Done, PlanReasons.DayEnded);
			}
			else
			{
				Close(o, OverlayStates.Cancelled, PlanReasons.DayEnded);
			}
		}
		_runtime.Clear();
	}

	private void CaptureEventsSeen() => _eventsSeenAtDayStart = new HashSet<string>(Game1.player?.eventsSeen ?? Enumerable.Empty<string>());

	// ---------------- 排 ----------------

	/// <summary>排一段改写；不合规返回空和原因。dryRun 只检查不排。</summary>
	public ScheduleOverlay? Plan(string npcName, string owner, int day, int start, int end, string location, int x, int y, int facing, out string reason, bool dryRun = false)
	{
		reason = "";
		if (State is not ScheduleOverlayState state || !Enabled)
		{
			reason = PlanReasons.Off;
			return null;
		}
		if (!Context.IsMainPlayer)
		{
			reason = PlanReasons.HostOnly;
			return null;
		}
		NPC? npc = Game1.getCharacterFromName(npcName);
		if (npc == null || !npc.IsVillager)
		{
			reason = PlanReasons.NoVillager;
			return null;
		}
		if (ChildSafety.IsChild(npc.Name, npc.Age))
		{
			reason = PlanReasons.Child;
			return null;
		}
		string? times = OverlayRules.CheckTimes(Today, Game1.timeOfDay, day, start, end);
		if (times != null)
		{
			reason = times;
			return null;
		}
		string? protectedDay = ProtectedDayReason(npc, day);
		if (protectedDay != null)
		{
			reason = protectedDay;
			return null;
		}
		GameLocation? loc = Game1.getLocationFromName(location);
		if (loc == null)
		{
			reason = PlanReasons.NoPlace;
			return null;
		}
		if (!TryFindReachableTile(npc, loc, new Point(x, y), out Point safe))
		{
			reason = PlanReasons.NoSpot;
			return null;
		}
		if (state.Items.Any(o => string.Equals(o.Npc, npc.Name, StringComparison.OrdinalIgnoreCase) && OverlayRules.Overlaps(o, day, start, end)))
		{
			reason = PlanReasons.Overlap;
			return null;
		}
		ScheduleOverlay overlay = new ScheduleOverlay
		{
			Id = dryRun ? "dry" : "s" + state.NextNumber++,
			Npc = npc.Name,
			Owner = owner,
			Day = day,
			Start = start,
			End = end,
			Location = loc.NameOrUniqueName,
			X = safe.X,
			Y = safe.Y,
			Facing = facing,
			CreatedDay = Today
		};
		if (dryRun)
		{
			return overlay;
		}
		state.Items.Add(overlay);
		_log.Info($"日程 {overlay.Id}：{npc.Name} → {overlay.Location}({overlay.X},{overlay.Y})，第 {day} 天 {start}～{end}（{owner}）。");
		Version++;
		MarkDirty();
		if (day == Today)
		{
			OnTimeChanged();
		}
		return overlay;
	}

	private static string? ProtectedDayReason(NPC npc, int day)
	{
		SDate date = SDate.FromDaysSinceStart(day + 1);
		if (Utility.isFestivalDay(date.Day, date.Season))
		{
			return PlanReasons.Festival;
		}
		return string.Equals(npc.Birthday_Season, date.SeasonKey, StringComparison.OrdinalIgnoreCase) && npc.Birthday_Day == date.Day ? PlanReasons.Birthday : null;
	}

	public bool SetEnd(string id, int end)
	{
		ScheduleOverlay? o = Find(id);
		if (o == null || !OverlayStates.IsOpen(o.State) || end <= o.Start)
		{
			return false;
		}
		o.End = Math.Min(OverlayRules.LatestEnd, end);
		Version++;
		MarkDirty();
		return true;
	}

	public bool Cancel(string id, string reason)
	{
		ScheduleOverlay? o = Find(id);
		if (o == null || !OverlayStates.IsOpen(o.State))
		{
			return false;
		}
		if (OverlayStates.IsHolding(o.State))
		{
			Release(o, OverlayStates.Cancelled, reason);
		}
		else
		{
			Close(o, OverlayStates.Cancelled, reason);
		}
		return true;
	}

	/// <summary>让不归这里管的村民走回日程此刻该在的地方（帮工从 Squad 原地离队以后）。</summary>
	public void WalkBack(NPC npc)
	{
		if (!Context.IsMainPlayer || npc?.currentLocation == null || IsHolding(npc.Name) || SquadMark.IsControlled(npc))
		{
			return;
		}
		try
		{
			npc.controller = null;
			npc.Halt();
			SendBackToSchedule(npc);
		}
		catch (Exception ex)
		{
			_log.Warn($"{npc.Name} 走不回日程：{ex.Message}");
		}
	}

	public void DebugClear()
	{
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsHolding(o.State)))
		{
			Release(o, OverlayStates.Cancelled, PlanReasons.Cleared);
		}
		State?.Items.Clear();
		_runtime.Clear();
		Version++;
		MarkDirty();
	}

	// ---------------- 跑 ----------------

	/// <summary>每 10 分钟：今天排好的看要不要出发，到点的放回。</summary>
	public void OnTimeChanged()
	{
		// 换天过程中日期已经加一、钟还停在前一晚，这时不算"太晚了"。
		if (State == null || !Context.IsMainPlayer || Game1.player == null || Game1.newDay || _startedDay != Today)
		{
			return;
		}
		if (!Enabled)
		{
			ReleaseAll();
			return;
		}
		int now = Game1.timeOfDay;
		foreach (ScheduleOverlay o in All().Where(o => o.Day == Today && OverlayStates.IsOpen(o.State)))
		{
			if (now >= o.End)
			{
				if (OverlayStates.IsHolding(o.State))
				{
					Release(o, OverlayStates.Done, PlanReasons.TimeUp);
				}
				else
				{
					Close(o, OverlayStates.Cancelled, PlanReasons.TooLate);
				}
				continue;
			}
			if (o.State == OverlayStates.Planned)
			{
				TryDepart(o, now);
			}
		}
	}

	private void TryDepart(ScheduleOverlay o, int now)
	{
		if (Game1.eventUp)
		{
			return;
		}
		NPC? npc = Game1.getCharacterFromName(o.Npc);
		if (npc?.currentLocation == null)
		{
			Close(o, OverlayStates.Cancelled, PlanReasons.NotAround);
			return;
		}
		Runtime rt = RuntimeOf(o);
		if (rt.EstimatedMinutes < 0)
		{
			if (Clock.Minutes(o.Start) - Clock.Minutes(now) > OverlayRules.EstimateWindowMinutes)
			{
				return;
			}
			Stack<Point>? probe = BuildRoute(npc, o);
			int extra = 0;
			if ((probe == null || probe.Count == 0) && npc.isMarried() && npc.currentLocation is FarmHouse or Farm)
			{
				probe = TryRoute(npc, "BusStop", new Point(10, 23), o.Location, new Point(o.X, o.Y), o.Facing);
				extra = 40;
			}
			rt.EstimatedMinutes = probe == null || probe.Count == 0 ? 30 + extra : OverlayRules.WalkMinutes(probe.Count) + extra;
		}
		if (Clock.Minutes(now) < Clock.Minutes(o.Start) - rt.EstimatedMinutes - 10)
		{
			return;
		}
		if (WhyNotNow(npc) is string why)
		{
			Close(o, OverlayStates.Cancelled, why);
			return;
		}
		if (_isBusyElsewhere(npc.Name))
		{
			return;
		}
		rt.IgnoreBefore = npc.ignoreScheduleToday;
		npc.ignoreScheduleToday = true;
		npc.queuedSchedulePaths.Clear();
		npc.IsWalkingInSquare = false;
		npc.controller = null;
		npc.temporaryController = null;
		npc.Halt();
		PrepareToLeave(npc);
		o.State = OverlayStates.Walking;
		o.DepartedAt = now;
		_log.Info($"日程 {o.Id}：{npc.Name} {now} 从 {npc.currentLocation.NameOrUniqueName}({npc.TilePoint.X},{npc.TilePoint.Y}) 出发去 {o.Location}({o.X},{o.Y})，约 {rt.EstimatedMinutes} 分钟。");
		Version++;
		MarkDirty();
		if (npc.temporaryController == null)
		{
			StartWalking(o, npc, rt);
		}
	}

	/// <summary>结束手上的动作（坐着、看书）。已婚的不走那条路：它在农场上会把当天日程清掉。</summary>
	private void PrepareToLeave(NPC npc)
	{
		try
		{
			if (!npc.isMarried())
			{
				_reflection.GetMethod(npc, "prepareToDisembarkOnNewSchedulePath").Invoke();
				return;
			}
			_reflection.GetMethod(npc, "finishEndOfRouteAnimation").Invoke();
			npc.doingEndOfRouteAnimation.Value = false;
			_reflection.GetField<bool>(npc, "currentlyDoingEndOfRouteAnimation").SetValue(false);
		}
		catch (Exception ex)
		{
			_log.Debug($"{npc.Name} 出发前收尾出错：{ex.Message}");
		}
	}

	private string? WhyNotNow(NPC npc)
	{
		if (!npc.IsVillager || npc.IsInvisible)
		{
			return PlanReasons.NotAround;
		}
		if (SquadMark.IsControlled(npc))
		{
			return PlanReasons.WithFarmer;
		}
		return HadHeartEventToday(npc.Name) ? PlanReasons.HeartEvent : null;
	}

	private bool HadHeartEventToday(string npc)
	{
		Farmer? player = Game1.player;
		if (player == null)
		{
			return false;
		}
		foreach (HeartEvent e in WorldData.HeartEvents.Where(e => string.Equals(e.Npc, npc, StringComparison.OrdinalIgnoreCase)))
		{
			string id = e.EventId.ToString();
			if (player.eventsSeen.Contains(id) && !_eventsSeenAtDayStart.Contains(id))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>每 15 帧：看在路上的人走到没有、卡没卡住；到点放回；没日程可回的已婚村民趁你看不见送回家。</summary>
	public void OnUpdateTicked()
	{
		if (State == null || !Context.IsMainPlayer || ++_ticks % 15 != 0 || Game1.eventUp)
		{
			return;
		}
		if (!Enabled)
		{
			if (_ticks % 60 == 0)
			{
				ReleaseAll();
			}
			return;
		}
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsHolding(o.State)))
		{
			try
			{
				Supervise(o);
			}
			catch (Exception ex)
			{
				_log.Warn($"日程 {o.Id} 出错，把 {o.Npc} 放回去：{ex}");
				Release(o, OverlayStates.Cancelled, PlanReasons.Error);
			}
		}
		if (_returning.Count > 0)
		{
			ContinueReturns();
		}
		foreach (string name in _sendHome.ToList())
		{
			NPC? npc = Game1.getCharacterFromName(name);
			if (npc?.currentLocation == null || IsHolding(name))
			{
				_sendHome.Remove(name);
			}
			else if (!PlayerCanSee(npc.currentLocation) && npc.getHome() is FarmHouse home)
			{
				npc.controller = null;
				npc.Halt();
				Game1.warpCharacter(npc, home, Utility.PointToVector2(home.getKitchenStandingSpot()));
				_sendHome.Remove(name);
			}
		}
	}

	private void Supervise(ScheduleOverlay o)
	{
		NPC? npc = Game1.getCharacterFromName(o.Npc);
		if (npc?.currentLocation == null)
		{
			Release(o, OverlayStates.Cancelled, PlanReasons.LostTrack);
			return;
		}
		if (SquadMark.IsControlled(npc))
		{
			RestoreFlags(o);
			Close(o, OverlayStates.Cancelled, PlanReasons.WithFarmer);
			return;
		}
		if (Game1.timeOfDay >= o.End)
		{
			Release(o, OverlayStates.Done, PlanReasons.TimeUp);
			return;
		}
		Runtime rt = RuntimeOf(o);
		npc.ignoreScheduleToday = true;
		if (o.State == OverlayStates.Arrived)
		{
			return;
		}
		bool there = string.Equals(npc.currentLocation.NameOrUniqueName, o.Location, StringComparison.OrdinalIgnoreCase);
		Point tile = npc.TilePoint;
		if (there && tile.X == o.X && tile.Y == o.Y && npc.controller == null)
		{
			Arrive(o, npc, "walked");
			return;
		}
		if (npc.temporaryController != null)
		{
			return;
		}
		if (npc.controller != null)
		{
			// 单人开着菜单时寻路不走，这段时间不算卡住。
			if (Game1.activeClickableMenu != null && !Game1.IsMultiplayer)
			{
				rt.StillChecks = 0;
				return;
			}
			if (tile == rt.LastTile)
			{
				if (++rt.StillChecks >= 20)
				{
					rt.StillChecks = 0;
					_log.Debug($"日程 {o.Id}：{npc.Name} 在 {npc.currentLocation.NameOrUniqueName}({tile.X},{tile.Y}) 5 秒没动，重算路线（第 {rt.Attempts} 次）。");
					npc.controller = null;
					npc.Halt();
					Unfreeze(npc);
				}
			}
			else
			{
				rt.LastTile = tile;
				rt.StillChecks = 0;
			}
			return;
		}
		if (there && Math.Abs(tile.X - o.X) + Math.Abs(tile.Y - o.Y) <= 1)
		{
			Arrive(o, npc, "walked");
			return;
		}
		if (rt.WaitChecks > 0)
		{
			rt.WaitChecks--;
			return;
		}
		StartWalking(o, npc, rt);
	}

	/// <summary>清掉会让人原地不动的标志。</summary>
	private void Unfreeze(NPC npc)
	{
		try
		{
			if (npc.doingEndOfRouteAnimation.Value)
			{
				PrepareToLeave(npc);
			}
			npc.movementPause = 0;
			_reflection.GetField<bool>(npc, "freezeMotion", required: false)?.SetValue(false);
			npc.isCharging = false;
			npc.IsWalkingInSquare = false;
			_reflection.GetField<bool>(npc, "returningToEndPoint", required: false)?.SetValue(false);
			if (npc.speed <= 0)
			{
				npc.speed = 2;
			}
		}
		catch (Exception ex)
		{
			_log.Debug($"清不掉 {npc.Name} 的停止标志：{ex.Message}");
		}
	}

	private void StartWalking(ScheduleOverlay o, NPC npc, Runtime rt)
	{
		if (LeaveFarmHouse(o, npc, rt))
		{
			return;
		}
		// 换了地图就重新数，长途几段加起来不会用完。
		string where = npc.currentLocation.NameOrUniqueName;
		if (!string.Equals(where, rt.AttemptLocation, StringComparison.OrdinalIgnoreCase))
		{
			rt.AttemptLocation = where;
			rt.Attempts = 0;
		}
		rt.Attempts++;
		Stack<Point>? route = rt.Attempts <= 4 ? BuildRoute(npc, o) : null;
		_log.Debug($"日程 {o.Id}：{npc.Name} 从 {where}({npc.TilePoint.X},{npc.TilePoint.Y}) 算路，第 {rt.Attempts} 次：{(route == null ? "没有" : route.Count + " 步")}。");
		if (route != null && route.Count > 0)
		{
			string id = o.Id;
			PathFindController controller = new PathFindController(route, npc, npc.currentLocation)
			{
				finalFacingDirection = o.Facing,
				endBehaviorFunction = (_, _) => OnRouteEnd(id)
			};
			npc.controller = controller;
			rt.Ours = controller;
			rt.LastTile = npc.TilePoint;
			rt.StillChecks = 0;
			return;
		}
		// 走不通：两头你都看不见就直接放过去。
		GameLocation? target = Game1.getLocationFromName(o.Location);
		if (target != null && !PlayerCanSee(npc.currentLocation) && !PlayerCanSee(target))
		{
			npc.controller = null;
			npc.Halt();
			Game1.warpCharacter(npc, target, new Vector2(o.X, o.Y));
			Arrive(o, npc, "placed");
			return;
		}
		if (rt.Attempts >= 2 && rt.Hops < 3 && !PlayerCanSee(npc.currentLocation) && TryHop(o, npc))
		{
			rt.Hops++;
			rt.Attempts = 0;
			rt.WaitChecks = 1;
			return;
		}
		if (rt.Attempts > 12)
		{
			Release(o, OverlayStates.Cancelled, PlanReasons.NoWay);
			return;
		}
		rt.WaitChecks = 8;
	}

	/// <summary>路线算不出来：去农场的挪到车站或农场入口，其他人从屋里挪到屋外门口。</summary>
	private bool TryHop(ScheduleOverlay o, NPC npc)
	{
		GameLocation here = npc.currentLocation;
		bool toFarm = string.Equals(o.Location, "Farm", StringComparison.OrdinalIgnoreCase);
		bool atBus = string.Equals(here.NameOrUniqueName, "BusStop", StringComparison.OrdinalIgnoreCase);
		if (toFarm && !atBus && here is not Farm)
		{
			GameLocation? bus = Game1.getLocationFromName("BusStop");
			if (bus != null && !PlayerCanSee(bus))
			{
				MoveTo(npc, bus, new Point(10, 23));
				return true;
			}
		}
		if (toFarm && atBus)
		{
			Farm? farm = Game1.getFarm();
			Point warp = here.getWarpPointTo("Farm");
			Point landing = warp == Point.Zero ? Point.Zero : here.getWarpPointTarget(warp, npc);
			if (farm != null && landing != Point.Zero)
			{
				MoveTo(npc, farm, landing);
				return true;
			}
		}
		if (!here.IsOutdoors)
		{
			foreach (Warp w in here.warps)
			{
				GameLocation? outside = Game1.getLocationFromName(w.TargetName);
				if (outside != null && outside.IsOutdoors && !PlayerCanSee(outside))
				{
					MoveTo(npc, outside, new Point(w.TargetX, w.TargetY));
					return true;
				}
			}
		}
		return false;
	}

	private void MoveTo(NPC npc, GameLocation to, Point tile)
	{
		_log.Info($"{npc.Name} 在 {npc.currentLocation.NameOrUniqueName} 算不出路，挪到 {to.NameOrUniqueName}({tile.X},{tile.Y}) 再走。");
		npc.controller = null;
		npc.Halt();
		Game1.warpCharacter(npc, to, new Vector2(tile.X, tile.Y));
	}

	/// <summary>伴侣在农舍里要去别处：寻路不从农舍出发。你看着时先走到门口，然后放到屋外门前。</summary>
	private bool LeaveFarmHouse(ScheduleOverlay o, NPC npc, Runtime rt)
	{
		if (!npc.isMarried() || npc.currentLocation is not FarmHouse house || string.Equals(o.Location, house.NameOrUniqueName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		Point entry = house.getEntryLocation();
		Point tile = npc.TilePoint;
		bool atDoor = Math.Abs(tile.X - entry.X) + Math.Abs(tile.Y - entry.Y) <= 1;
		if (PlayerCanSee(house) && !atDoor && rt.DoorTries < 3)
		{
			rt.DoorTries++;
			foreach (Point p in new[] { entry, new Point(entry.X, entry.Y - 1), new Point(entry.X - 1, entry.Y), new Point(entry.X + 1, entry.Y) })
			{
				PathFindController toDoor = new PathFindController(npc, house, p, 2);
				if (toDoor.pathToEndPoint != null && toDoor.pathToEndPoint.Count > 0)
				{
					npc.controller = toDoor;
					rt.Ours = toDoor;
					rt.LastTile = tile;
					rt.StillChecks = 0;
					return true;
				}
			}
		}
		Farm? farm = Game1.getFarm();
		if (farm == null)
		{
			return false;
		}
		Point door = house.getFrontDoorSpot();
		npc.controller = null;
		npc.temporaryController = null;
		npc.Halt();
		Game1.warpCharacter(npc, farm, new Vector2(door.X, door.Y));
		rt.WaitChecks = 1;
		return true;
	}

	private void OnRouteEnd(string id)
	{
		ScheduleOverlay? o = Find(id);
		NPC? npc = o == null ? null : Game1.getCharacterFromName(o.Npc);
		if (o == null || npc == null || o.State != OverlayStates.Walking)
		{
			return;
		}
		if (string.Equals(npc.currentLocation?.NameOrUniqueName, o.Location, StringComparison.OrdinalIgnoreCase) && Math.Abs(npc.TilePoint.X - o.X) + Math.Abs(npc.TilePoint.Y - o.Y) <= 1)
		{
			Arrive(o, npc, "walked");
		}
	}

	private void Arrive(ScheduleOverlay o, NPC npc, string how)
	{
		npc.controller = null;
		npc.Halt();
		npc.faceDirection(o.Facing);
		o.State = OverlayStates.Arrived;
		o.ArrivedAt = Game1.timeOfDay;
		o.ArrivedBy = how;
		_log.Info($"日程 {o.Id}：{npc.Name} {Game1.timeOfDay} 到了 {o.Location}({npc.TilePoint.X},{npc.TilePoint.Y})（{how}）。");
		Version++;
		MarkDirty();
	}

	private void Release(ScheduleOverlay o, string state, string reason)
	{
		NPC? npc = Game1.getCharacterFromName(o.Npc);
		RestoreFlags(o);
		if (npc?.currentLocation != null && !SquadMark.IsControlled(npc))
		{
			try
			{
				SendBackToSchedule(npc);
			}
			catch (Exception ex)
			{
				_log.Warn($"{o.Npc} 走不回日程：{ex.Message}");
			}
		}
		Close(o, state, reason);
	}

	private void ReleaseAll()
	{
		foreach (ScheduleOverlay o in All().Where(o => OverlayStates.IsHolding(o.State)))
		{
			Release(o, OverlayStates.Cancelled, PlanReasons.ModuleOff);
		}
	}

	private void RestoreFlags(ScheduleOverlay o)
	{
		NPC? npc = Game1.getCharacterFromName(o.Npc);
		_runtime.TryGetValue(o.Id, out Runtime? rt);
		if (npc == null)
		{
			return;
		}
		npc.ignoreScheduleToday = rt?.IgnoreBefore ?? false;
		if (rt?.Ours != null && npc.controller == rt.Ours)
		{
			npc.controller = null;
			npc.Halt();
		}
		if (npc.Schedule != null)
		{
			npc.followSchedule = true;
		}
	}

	/// <summary>日程此刻的位置：不晚于现在的最后一条；还没到第一条就是早上的出发点。已婚又没日程时返回空。</summary>
	private static (string Location, Point Tile, int Facing, string? Behavior, string? Message)? SchedulePositionNow(NPC npc)
	{
		int now = Game1.timeOfDay;
		if (npc.Schedule != null && npc.Schedule.Count > 0)
		{
			SchedulePathDescription? last = null;
			foreach (KeyValuePair<int, SchedulePathDescription> pair in npc.Schedule.OrderBy(p => p.Key))
			{
				if (pair.Key <= now)
				{
					last = pair.Value;
				}
			}
			if (last != null)
			{
				return (last.targetLocationName, last.targetTile, last.facingDirection, last.endOfRouteBehavior, last.endOfRouteMessage);
			}
		}
		if (npc.isMarried())
		{
			return null;
		}
		return (npc.DefaultMap, new Point((int)(npc.DefaultPosition.X / 64f), (int)(npc.DefaultPosition.Y / 64f)), npc.DefaultFacingDirection, null, null);
	}

	private void ContinueReturns()
	{
		foreach (string name in _returning.Keys.ToList())
		{
			NPC? npc = Game1.getCharacterFromName(name);
			if (npc?.currentLocation == null || IsHolding(name) || ++_returning[name] > ReturningChecks)
			{
				_returning.Remove(name);
			}
			else if (npc.controller == null && npc.temporaryController == null)
			{
				_returning.Remove(name);
				SendBackToSchedule(npc);
			}
		}
	}

	private void SendBackToSchedule(NPC npc)
	{
		var target = SchedulePositionNow(npc);
		if (target == null)
		{
			_sendHome.Add(npc.Name);
			return;
		}
		(string loc, Point tile, int facing, string? behavior, string? message) = target.Value;
		string here = npc.currentLocation.NameOrUniqueName;
		if (string.Equals(here, loc, StringComparison.OrdinalIgnoreCase) && npc.TilePoint == tile)
		{
			return;
		}
		(string legLoc, Point legTile) = NextLeg(here, loc, tile);
		if (!string.Equals(legLoc, loc, StringComparison.OrdinalIgnoreCase) || legTile != tile)
		{
			Stack<Point>? leg = TryRoute(npc, here, npc.TilePoint, legLoc, legTile, facing);
			if (leg != null)
			{
				npc.controller = new PathFindController(leg, npc, npc.currentLocation) { finalFacingDirection = facing };
				_returning[npc.Name] = 0;
				return;
			}
		}
		SchedulePathDescription back = npc.pathfindToNextScheduleLocation(npc.ScheduleKey ?? "aisv", here, npc.TilePoint.X, npc.TilePoint.Y, loc, tile.X, tile.Y, facing, behavior, message);
		if (back.route == null || back.route.Count == 0)
		{
			GameLocation? where = Game1.getLocationFromName(loc);
			if (where != null && !PlayerCanSee(npc.currentLocation) && !PlayerCanSee(where))
			{
				Game1.warpCharacter(npc, where, new Vector2(tile.X, tile.Y));
				npc.faceDirection(facing);
			}
			return;
		}
		PathFindController.endBehavior? end = null;
		try
		{
			end = _reflection.GetMethod(npc, "getRouteEndBehaviorFunction").Invoke<PathFindController.endBehavior>(behavior, message);
		}
		catch (Exception ex)
		{
			_log.Debug($"{npc.Name} 路线末尾动作取不到：{ex.Message}");
		}
		npc.controller = new PathFindController(back.route, npc, npc.currentLocation)
		{
			finalFacingDirection = facing,
			endBehaviorFunction = end
		};
	}

	private void Close(ScheduleOverlay o, string state, string reason)
	{
		o.State = state;
		o.CancelReason = state == OverlayStates.Cancelled ? reason : "";
		o.ReleasedAt = Game1.timeOfDay;
		_runtime.Remove(o.Id);
		_log.Info($"日程 {o.Id}：{o.Npc} {state}（{reason}），{Game1.timeOfDay}。");
		Version++;
		MarkDirty();
		try
		{
			Closed?.Invoke(o);
		}
		catch (Exception ex)
		{
			_log.Warn($"日程 {o.Id} 结束后的处理出错：{ex}");
		}
	}

	// ---------------- 路线 ----------------

	private Runtime RuntimeOf(ScheduleOverlay o)
	{
		if (!_runtime.TryGetValue(o.Id, out Runtime? rt))
		{
			rt = new Runtime();
			_runtime[o.Id] = rt;
		}
		return rt;
	}

	/// <summary>跨地图路线图里没有农场：进出农场分两段，先走到车站和农场之间的传送点。</summary>
	private static (string Location, Point Tile) NextLeg(string fromLoc, string toLoc, Point to)
	{
		bool fromFarm = string.Equals(fromLoc, "Farm", StringComparison.OrdinalIgnoreCase);
		bool toFarm = string.Equals(toLoc, "Farm", StringComparison.OrdinalIgnoreCase);
		if (fromFarm == toFarm)
		{
			return (toLoc, to);
		}
		if (toFarm)
		{
			Point warp = Game1.getLocationFromName("BusStop")?.getWarpPointTo("Farm") ?? Point.Zero;
			return warp == Point.Zero ? (toLoc, to) : ("BusStop", warp);
		}
		Point exit = Game1.getFarm()?.getWarpPointTo("BusStop") ?? Point.Zero;
		return exit == Point.Zero ? (toLoc, to) : ("Farm", exit);
	}

	/// <summary>现在出发走到那里大约几分钟（分两段的多加 20 分钟）。</summary>
	public int EstimateWalkMinutes(NPC npc, string location, int x, int y)
	{
		if (npc.currentLocation == null)
		{
			return 60;
		}
		ScheduleOverlay target = new ScheduleOverlay { Id = "estimate", Npc = npc.Name, Location = location, X = x, Y = y };
		Stack<Point>? probe = BuildRoute(npc, target);
		int extra = 0;
		string fromLoc = npc.currentLocation.NameOrUniqueName;
		if ((probe == null || probe.Count == 0) && npc.isMarried() && npc.currentLocation is FarmHouse or Farm)
		{
			probe = TryRoute(npc, "BusStop", new Point(10, 23), location, new Point(x, y), 2);
			fromLoc = "BusStop";
			extra = 40;
		}
		if (string.Equals(fromLoc, location, StringComparison.OrdinalIgnoreCase) && (probe == null || probe.Count == 0))
		{
			return 10 + extra;
		}
		int minutes = probe == null || probe.Count == 0 ? 60 + extra : OverlayRules.WalkMinutes(probe.Count) + extra;
		if (!string.Equals(NextLeg(fromLoc, location, new Point(x, y)).Location, location, StringComparison.OrdinalIgnoreCase))
		{
			minutes += 20;
		}
		return minutes;
	}

	/// <summary>日程寻路；路线没走到终点（最后一段算不出时会停在上一张地图）返回空。</summary>
	private static Stack<Point>? ScheduleRoute(NPC npc, string fromLoc, Point from, string toLoc, Point to, int facing)
	{
		SchedulePathDescription d = npc.pathfindToNextScheduleLocation(npc.ScheduleKey ?? "aisv", fromLoc, from.X, from.Y, toLoc, to.X, to.Y, facing, null, null);
		Point[] steps = d.route?.ToArray() ?? Array.Empty<Point>();
		return steps.Length == 0 || steps[^1] != to ? null : d.route;
	}

	private static Stack<Point>? TryRoute(NPC npc, string fromLoc, Point from, string toLoc, Point to, int facing)
	{
		try
		{
			return ScheduleRoute(npc, fromLoc, from, toLoc, to, facing);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private Stack<Point>? BuildRoute(NPC npc, ScheduleOverlay o)
	{
		string fromLoc = npc.currentLocation.NameOrUniqueName;
		if (npc.isMarried() && npc.currentLocation is FarmHouse && !string.Equals(o.Location, fromLoc, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		(string legLoc, Point legTile) = NextLeg(fromLoc, o.Location, new Point(o.X, o.Y));
		try
		{
			return ScheduleRoute(npc, fromLoc, npc.TilePoint, legLoc, legTile, o.Facing);
		}
		catch (Exception ex)
		{
			_log.Debug($"{npc.Name} 从 {fromLoc} 算不出路：{ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	private static bool PlayerCanSee(GameLocation? location) => location != null && Game1.player?.currentLocation == location;

	/// <summary>3 格内找一格：能站人，日程寻路也真的走得到（有自己的禁行格，光看碰撞不够）。最多试 12 格。</summary>
	private static bool TryFindReachableTile(NPC npc, GameLocation loc, Point tile, out Point safe)
	{
		safe = tile;
		int tried = 0;
		foreach (Point p in Spiral(tile, 3))
		{
			if (!CanStand(loc, p, npc))
			{
				continue;
			}
			if (++tried > 12)
			{
				break;
			}
			if (RouteReaches(npc, loc, p))
			{
				safe = p;
				return true;
			}
		}
		return false;
	}

	private static bool RouteReaches(NPC npc, GameLocation loc, Point p)
	{
		if (npc.currentLocation == null)
		{
			return true;
		}
		string fromLoc = npc.currentLocation.NameOrUniqueName;
		Point from = npc.TilePoint;
		if (npc.isMarried() && npc.currentLocation is FarmHouse or Farm)
		{
			fromLoc = "BusStop";
			from = new Point(10, 23);
		}
		string toLoc = loc.NameOrUniqueName;
		if (string.Equals(fromLoc, toLoc, StringComparison.OrdinalIgnoreCase) && from == p)
		{
			return true;
		}
		try
		{
			(string legLoc, Point legTile) = NextLeg(fromLoc, toLoc, p);
			if (ScheduleRoute(npc, fromLoc, from, legLoc, legTile, 2) == null)
			{
				return false;
			}
			if (string.Equals(legLoc, toLoc, StringComparison.OrdinalIgnoreCase) && legTile == p)
			{
				return true;
			}
			Point landing = Game1.getLocationFromName(legLoc)?.getWarpPointTarget(legTile, npc) ?? Point.Zero;
			return landing != Point.Zero && (landing == p || ScheduleRoute(npc, toLoc, landing, toLoc, p, 2) != null);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static IEnumerable<Point> Spiral(Point center, int radius)
	{
		for (int r = 0; r <= radius; r++)
		{
			for (int dy = -r; dy <= r; dy++)
			{
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
					{
						yield return new Point(center.X + dx, center.Y + dy);
					}
				}
			}
		}
	}

	private static bool CanStand(GameLocation loc, Point p, NPC npc)
	{
		if (!loc.isTileOnMap(p.X, p.Y) || loc.isWaterTile(p.X, p.Y))
		{
			return false;
		}
		if (loc.warps.Any(w => w.X == p.X && w.Y == p.Y) || loc.doors.ContainsKey(p))
		{
			return false;
		}
		Rectangle box = new Rectangle(p.X * 64 + 1, p.Y * 64 + 1, 62, 62);
		return !loc.isCollidingPosition(box, Game1.viewport, false, 0, false, npc, true);
	}

	private void MarkDirty() => _store()?.MarkDirty(ScheduleOverlayState.Path);
}
