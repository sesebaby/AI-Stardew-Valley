using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace AISV.Game.Companions;

/// <summary>
/// 约见：村民约你某天几点在某处见，你答应后日程改写让他走过去等。你走到身边算守约（+40，再待一小时），
/// 他等到结束你没来算失约（-30），他那边去不了不怪你；下雨约在户外的改酒吧或者作罢；可以提前取消（当天 -10）。
/// 由房主排（村民在房主那边走动）；联机的农场帮手当面答应的约见交给房主排，结果在他自己那边生效（<see cref="CompanionNet"/>）。
/// 同伴模块关掉时不约新的。
/// </summary>
public sealed class MeetupService
{
	private const int FreeTicksBeforeAsking = 20;

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ScheduleOverlayService _schedules;

	private readonly ILog _log;

	private readonly Queue<PendingOffer> _toConfirm = new Queue<PendingOffer>();

	private int _ticks;

	private int _freeTicks;

	public MeetupService(Func<ModConfig> config, Func<DocumentStore?> store, ScheduleOverlayService schedules, ILog log)
	{
		_config = config;
		_store = store;
		_schedules = schedules;
		_log = log;
		_schedules.Closed += OnOverlayClosed;
		CompanionNet.StoodUp = n => StoodUp?.Invoke(n);
	}

	/// <summary>玩家答复过提议时（从账本拿掉）。</summary>
	public Action<string>? ResolveOffer { get; set; }

	/// <summary>有人跟着剧情约的见到了：演事件；返回 false 时按普通约见处理。</summary>
	public Func<Meetup, NPC, bool>? StoryKept { get; set; }

	public event Action<Meetup>? MeetupClosed;

	/// <summary>农夫没来（伴侣剧情的约见不算）：交给做错事的后果。</summary>
	public Action<string>? StoodUp { get; set; }

	/// <summary>这个村民被放鸽子太多次，暂时不肯再约。</summary>
	public Func<string, bool>? Refuses { get; set; }

	public int Version { get; private set; }

	/// <summary>房主发来了新的记录：界面重画。</summary>
	public void Touch() => Version++;

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.CompanionModuleOn;
		}
	}

	private MeetupLog? Log => _store()?.Get(MeetupLog.Path, MeetupLog.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public IReadOnlyList<Meetup> All() => Log?.Items.ToList() ?? new List<Meetup>();

	public Meetup? Find(string id) => Log?.Items.FirstOrDefault(m => m.Id == id);

	/// <summary>这边的农夫还没到的约（联机时别的农夫的不算）。</summary>
	public IReadOnlyList<Meetup> Upcoming() => OpenAll().Where(Mine).ToList();

	/// <summary>所有农夫还没到的约（同一位村民同一时间只能去一处）。</summary>
	private IReadOnlyList<Meetup> OpenAll() => All().Where(m => MeetupStates.IsOpen(m.State)).OrderBy(m => m.Day).ThenBy(m => m.Time).ToList();

	private static bool Mine(Meetup m) => m.Farmer == CompanionNet.Me;

	// ---------------- 生命周期 ----------------

	public void OnSaveLoaded()
	{
		_toConfirm.Clear();
		Version++;
	}

	public void OnReturnedToTitle() => _toConfirm.Clear();

	/// <summary>早上：过去的收尾、下雨改地点、提醒今天的约。日程改写先跑。</summary>
	public void OnDayStarted()
	{
		if (Log is not MeetupLog log)
		{
			return;
		}
		_toConfirm.Clear();
		_ready.Clear();
		if (!Context.IsMainPlayer)
		{
			// 农场帮手：收尾、改地点都在房主那边；这边只提醒今天的约。
			Remind();
			return;
		}
		foreach (Meetup m in All().Where(m => MeetupStates.IsOpen(m.State) && m.Day < Today))
		{
			Close(m, MeetupStates.CalledOff, PlanReasons.Lost, Today - 1);
		}
		if (log.Items.RemoveAll(m => !MeetupStates.IsOpen(m.State) && Today - m.ClosedDay > MeetupRules.ClosedKeepDays) > 0)
		{
			MarkDirty();
		}
		if (!Enabled)
		{
			return;
		}
		if (Game1.isRaining)
		{
			foreach (Meetup m in All().Where(m => MeetupStates.IsOpen(m.State) && m.Day == Today))
			{
				MoveIndoorsForRain(m);
			}
		}
		Remind();
	}

	private void Remind()
	{
		if (!Enabled)
		{
			return;
		}
		foreach (Meetup m in Upcoming().Where(m => m.Day == Today))
		{
			Hud(Tr.Get("meetup.reminder", "今天 {{time}} 和{{npc}}约好在{{place}}见面。", ("time", Clock.Text(m.Time)), ("npc", NpcName(m.Npc)), ("place", PlaceLabel(m))));
		}
	}

	private void MoveIndoorsForRain(Meetup m)
	{
		MeetupPlace? p = MeetupPlaces.Get(m.Place);
		if (p == null || p.Indoors)
		{
			return;
		}
		NPC? npc = Game1.getCharacterFromName(m.Npc);
		MeetupPlace saloon = MeetupPlaces.Get(MeetupPlaces.Saloon)!;
		_schedules.Cancel(m.OverlayId, PlanReasons.RainMove);
		string from = m.Place;
		// 剧情的场景是按那个地方写的，不搬去酒吧。
		if (m.Owner != MeetupRules.StoryOwner && npc != null && m.Time >= saloon.OpenTime && TryPlanOverlay(npc, m.Day, m.Time, saloon, m.Farmer, out string? overlayId, out _))
		{
			m.MovedFrom = from;
			m.Place = saloon.Key;
			m.OverlayId = overlayId!;
			_log.Info($"约见 {m.Id}：下雨，从 {from} 改到酒吧。");
			Tell(m, Tr.Get("meetup.movedForRain", "下雨了，{{npc}}说改在{{place}}见。", ("npc", NpcName(m.Npc)), ("place", MeetupPlaces.Label(saloon.Key))));
			Version++;
			MarkDirty();
			return;
		}
		Close(m, MeetupStates.CalledOff, PlanReasons.Rain, Today);
		Tell(m, Tr.Get("meetup.calledOffRain", "下雨了，{{npc}}说约在{{place}}的事改天再说。", ("npc", NpcName(m.Npc)), ("place", MeetupPlaces.Label(from, npc == null ? null : NpcName(m.Npc)))));
	}

	// ---------------- 当面的提议：聊完问你 ----------------

	public void Enqueue(PendingOffer offer)
	{
		if (Enabled && (Context.IsMainPlayer || CompanionNet.Remote) && !_toConfirm.Contains(offer))
		{
			_toConfirm.Enqueue(offer);
		}
	}

	public void OnUpdateTicked()
	{
		if (Log == null || !Context.IsMainPlayer && !CompanionNet.Remote)
		{
			return;
		}
		if (++_ticks % 30 == 0 && !Game1.eventUp && Context.IsMainPlayer)
		{
			CheckKept();
		}
		if (_toConfirm.Count == 0 && _ready.Count == 0)
		{
			return;
		}
		if (!Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp)
		{
			_freeTicks = 0;
			return;
		}
		if (++_freeTicks >= FreeTicksBeforeAsking)
		{
			_freeTicks = 0;
			if (_ready.Count > 0)
			{
				Resolved r = _ready.Dequeue();
				Confirm(r.Offer, r.Day, r.Time, r.Place, r.Shifted);
			}
			else
			{
				Ask(_toConfirm.Dequeue());
			}
		}
	}

	/// <summary>农场帮手：房主算好了日子和地点，等你空下来再问。</summary>
	private readonly Queue<Resolved> _ready = new Queue<Resolved>();

	private void Ask(PendingOffer p)
	{
		NPC? npc = Game1.getCharacterFromName(p.Npc);
		if (npc == null || p.ExpiresDay < Today)
		{
			ResolveOffer?.Invoke(p.Id);
			return;
		}
		if (CompanionNet.Remote)
		{
			AskHost(p, npc);
			return;
		}
		if (!TryResolve(p, npc, out int day, out int time, out MeetupPlace place, out bool shifted, out string why))
		{
			ResolveOffer?.Invoke(p.Id);
			_log.Info($"{npc.Name} 的约见（{p.When} {p.Time} {p.Where}）排不上：{why}。");
			NoteCantArrange(npc.Name, why);
			return;
		}
		Confirm(p, day, time, place.Key, shifted);
	}

	/// <summary>农场帮手：请房主算日子、地点能不能排上。</summary>
	private void AskHost(PendingOffer p, NPC npc)
	{
		if (Refuses?.Invoke(npc.Name) == true)
		{
			ResolveOffer?.Invoke(p.Id);
			NoteCantArrange(npc.Name, MeetupRules.StoodUpReason);
			return;
		}
		CompanionNet.Net!.Ask<PendingOffer, Resolved>(ResolveOp, p, r =>
		{
			if (r == null || !r.Ok)
			{
				ResolveOffer?.Invoke(p.Id);
				_log.Info($"{npc.Name} 的约见（{p.When} {p.Time} {p.Where}）房主那边排不上：{r?.Reason ?? MeetupRules.NoHostReason}。");
				NoteCantArrange(npc.Name, r?.Reason ?? MeetupRules.NoHostReason);
				return;
			}
			_ready.Enqueue(r with { Offer = p });
		});
	}

	/// <summary>问你答不答应；答应了就排上（农场帮手请房主排）。</summary>
	private void Confirm(PendingOffer p, int day, int time, string placeKey, bool shifted)
	{
		NPC? npc = Game1.getCharacterFromName(p.Npc);
		MeetupPlace? place = MeetupPlaces.Get(placeKey);
		if (npc == null || place == null)
		{
			ResolveOffer?.Invoke(p.Id);
			return;
		}
		string when = MeetupRules.WhenText(day, Today) + " " + Clock.Text(time) + (shifted ? Tr.Get("meetup.earliestNote", "（走过去最快这个点到）") : "");
		string label = MeetupPlaces.Label(place.Key, NpcName(npc.Name));
		Response[] answers =
		{
			new Response("yes", YesText),
			new Response("no", NoText)
		};
		Game1.currentLocation.createQuestionDialogue(TextTools.CleanForGame(Tr.Get("meetup.ask", "{{npc}}约你{{when}}，在{{place}}见面。", ("npc", NpcName(npc.Name)), ("when", when), ("place", label))), answers, (_, answer) =>
		{
			ResolveOffer?.Invoke(p.Id);
			if (answer != "yes")
			{
				return;
			}
			if (CompanionNet.Remote)
			{
				CompanionNet.Net!.Ask<ArrangeAsk, Arranged>(ArrangeOp, new ArrangeAsk(npc.Name, day, time, place.Key, p.Summary), a =>
				{
					if (a?.Ok == true)
					{
						Hud(Tr.Get("meetup.arranged", "约好了：{{when}}，{{place}}。", ("when", when), ("place", label)));
					}
					else
					{
						NoteCantArrange(npc.Name, a?.Reason ?? MeetupRules.NoHostReason);
					}
				});
				return;
			}
			if (Arrange(npc.Name, day, time, place.Key, p.Summary, out string reason) == null)
			{
				NoteCantArrange(npc.Name, reason);
				return;
			}
			Hud(Tr.Get("meetup.arranged", "约好了：{{when}}，{{place}}。", ("when", when), ("place", label)));
		});
	}

	public void NoteCantArrange(string npc, string reason) => Hud(CantArrangeText(npc, reason));

	public string CantArrangeText(string npc, string reason) => Tr.Get("meetup.cantArrange", "{{npc}}想了想：{{why}}，改天再约吧。", ("npc", NpcName(npc)), ("why", MeetupRules.Explain(reason)));

	public static string YesText => Tr.Get("meetup.yes", "好，到时见");

	public static string NoText => Tr.Get("meetup.no", "改天吧");

	/// <summary>把提议算成具体的日子、时间、地点并检查。今天的约不早于走过去能到的时间。</summary>
	public bool TryResolve(PendingOffer p, NPC npc, out int day, out int time, out MeetupPlace place, out bool shifted, out string reason, long farmer = 0)
	{
		shifted = false;
		reason = "";
		place = MeetupPlaces.Parse(p.Where) ?? MeetupPlaces.Parse(p.Summary) ?? MeetupPlaces.Get(MeetupPlaces.TownSquare)!;
		int offset = MeetupRules.ParseDayOffset(p.When, p.Time, Game1.timeOfDay);
		int? asked = MeetupRules.ParseTime(p.Time) ?? MeetupRules.ParseTime(p.When);
		day = Today + offset;
		time = asked ?? MeetupRules.DefaultTime;
		if (offset == 0)
		{
			int earliest = EarliestToday(npc, place, farmer);
			if (!asked.HasValue || Clock.Minutes(time) < Clock.Minutes(earliest))
			{
				time = Math.Max(earliest, place.OpenTime);
				shifted = true;
			}
			if (time > MeetupRules.LatestTime || time >= place.CloseTime)
			{
				reason = MeetupRules.TooLateReason;
				return false;
			}
		}
		string? why = Check(npc, day, time, place, farmer);
		if (why != null)
		{
			reason = why;
			return false;
		}
		return true;
	}

	public int EarliestToday(NPC npc, MeetupPlace place, long farmer = 0)
	{
		int walk = 60;
		if (Resolve(place, npc, out string loc, out Point tile, farmer))
		{
			try
			{
				walk = _schedules.EstimateWalkMinutes(npc, loc, tile.X, tile.Y);
			}
			catch (Exception ex)
			{
				_log.Debug($"估不出 {npc.Name} 走到 {place.Key} 的路程：{ex.Message}");
			}
		}
		return MeetupRules.EarliestToday(Game1.timeOfDay, walk);
	}

	/// <summary>地点在地图上的位置；找不到返回 false。</summary>
	public static bool Resolve(MeetupPlace p, NPC npc, out string location, out Point tile, long farmer = 0)
	{
		location = p.Location;
		tile = new Point(p.X, p.Y);
		if (p.Key == MeetupPlaces.Home)
		{
			location = npc.DefaultMap;
			tile = new Point((int)(npc.DefaultPosition.X / 64f), (int)(npc.DefaultPosition.Y / 64f));
			GameLocation? home = Game1.getLocationFromName(location);
			if (home == null)
			{
				return false;
			}
			// 床边不合适，往门口方向挪。
			if (home.warps.Count > 0)
			{
				Warp w = home.warps[0];
				tile = new Point((tile.X * 2 + w.X) / 3, (tile.Y * 2 + w.Y) / 3);
			}
			return true;
		}
		if (p.Key == MeetupPlaces.Farm)
		{
			Farmer? who = farmer == 0 ? Game1.player : CompanionNet.FarmerOf(farmer);
			FarmHouse? house = who == null ? null : Utility.getHomeOfFarmer(who);
			if (house == null)
			{
				return false;
			}
			Point porch = house.getPorchStandingSpot();
			location = "Farm";
			tile = new Point(porch.X + 1, porch.Y + 1);
			return true;
		}
		if (p.DoorTo.Length > 0)
		{
			GameLocation? outdoor = Game1.getLocationFromName(p.Location);
			if (outdoor == null)
			{
				return false;
			}
			foreach (KeyValuePair<Point, string> pair in outdoor.doors.Pairs)
			{
				if (string.Equals(pair.Value?.Trim(), p.DoorTo, StringComparison.OrdinalIgnoreCase))
				{
					tile = new Point(pair.Key.X, pair.Key.Y + 2);
					return true;
				}
			}
			return false;
		}
		return Game1.getLocationFromName(location) != null;
	}

	// ---------------- 排 ----------------

	public string? Check(NPC npc, int day, int time, MeetupPlace place, long farmer = 0)
	{
		if (!Enabled)
		{
			return MeetupRules.OffReason;
		}
		if (ChildSafety.IsChild(npc.Name, npc.Age))
		{
			return MeetupRules.ChildReason;
		}
		// 放鸽子太多次的冷落记在各人自己那边：农场帮手问之前自己查过。
		if (farmer == 0 && Refuses?.Invoke(npc.Name) == true)
		{
			return MeetupRules.StoodUpReason;
		}
		if (day < Today || day > Today + MeetupRules.MaxDaysAhead)
		{
			return MeetupRules.AheadReason;
		}
		if (time < MeetupRules.EarliestTime || time > MeetupRules.LatestTime)
		{
			return MeetupRules.HoursReason;
		}
		if (time < place.OpenTime || time >= place.CloseTime)
		{
			return MeetupRules.ClosedReason;
		}
		IReadOnlyList<Meetup> open = OpenAll();
		if (open.Any(m => string.Equals(m.Npc, npc.Name, StringComparison.OrdinalIgnoreCase)))
		{
			return MeetupRules.AlreadyReason;
		}
		if (open.Count(m => m.Day == day && m.Farmer == farmer) >= MeetupRules.MaxPerDay)
		{
			return MeetupRules.FullReason;
		}
		if (!Resolve(place, npc, out string loc, out Point tile, farmer))
		{
			return PlanReasons.NoPlace;
		}
		_schedules.Plan(npc.Name, MeetupRules.OverlayOwner, day, time, MeetupRules.End(time, place), loc, tile.X, tile.Y, 2, out string reason, dryRun: true);
		return reason.Length > 0 ? reason : null;
	}

	public Meetup? Arrange(string npcName, int day, int time, string placeKey, string summary, out string reason, string owner = "", string ownerRef = "", long farmer = 0)
	{
		reason = "";
		NPC? npc = Game1.getCharacterFromName(npcName);
		MeetupPlace? place = MeetupPlaces.Get(placeKey);
		if (npc == null || place == null || Log is not MeetupLog log)
		{
			reason = PlanReasons.NoVillager;
			return null;
		}
		string? why = Check(npc, day, time, place, farmer);
		if (why != null)
		{
			reason = why;
			return null;
		}
		if (!TryPlanOverlay(npc, day, time, place, farmer, out string? overlayId, out reason))
		{
			return null;
		}
		Meetup m = new Meetup
		{
			Id = "m" + log.NextNumber++,
			Npc = npc.Name,
			Day = day,
			Time = time,
			Place = place.Key,
			OverlayId = overlayId!,
			Summary = summary ?? "",
			CreatedDay = Today,
			Owner = owner,
			OwnerRef = ownerRef,
			Farmer = farmer
		};
		log.Items.Add(m);
		_log.Info($"约见 {m.Id}：{npc.Name}{(farmer == 0 ? "" : "和 " + CompanionNet.FarmerOf(farmer)?.Name)}，第 {day} 天 {time} 在 {place.Key}（{overlayId}）。");
		Version++;
		MarkDirty();
		return m;
	}

	private bool TryPlanOverlay(NPC npc, int day, int time, MeetupPlace place, long farmer, out string? overlayId, out string reason)
	{
		overlayId = null;
		if (!Resolve(place, npc, out string loc, out Point tile, farmer))
		{
			reason = PlanReasons.NoPlace;
			return false;
		}
		ScheduleOverlay? o = _schedules.Plan(npc.Name, MeetupRules.OverlayOwner, day, time, MeetupRules.End(time, place), loc, tile.X, tile.Y, 2, out reason);
		overlayId = o?.Id;
		return o != null;
	}

	/// <summary>你说去不了。提前一天以上不扣好感，当天 -10。</summary>
	public bool CancelByPlayer(string id)
	{
		Meetup? m = Find(id);
		if (m == null || !MeetupStates.IsOpen(m.State) || !Mine(m))
		{
			return false;
		}
		if (CompanionNet.Remote)
		{
			// 房主那边取消；这边先划掉，等下一次同步。
			CompanionNet.Net!.ToHost(CancelOp, new IdAsk(id));
			m.State = MeetupStates.CancelledByPlayer;
			Version++;
			return true;
		}
		return CancelFor(m);
	}

	private bool CancelFor(Meetup m)
	{
		_schedules.Cancel(m.OverlayId, PlanReasons.FarmerCancelled);
		Close(m, MeetupStates.CancelledByPlayer, PlanReasons.FarmerCancelled, Today);
		if (m.Day == Today)
		{
			CompanionNet.Send(m.Farmer, new CompanionEffect(m.Npc) { Friendship = MeetupRules.SameDayCancelFriendship });
		}
		return true;
	}

	/// <summary>排约见的那一方不需要了：作罢，不扣好感、不提示。</summary>
	public bool CancelForOwner(string id, string reason)
	{
		Meetup? m = Find(id);
		if (m == null || !MeetupStates.IsOpen(m.State))
		{
			return false;
		}
		Close(m, MeetupStates.CalledOff, reason, Today);
		_schedules.Cancel(m.OverlayId, reason);
		return true;
	}

	// ---------------- 见到和没见到 ----------------

	private void CheckKept()
	{
		if (!Enabled)
		{
			return;
		}
		foreach (Meetup m in All().Where(m => MeetupStates.IsOpen(m.State) && m.Day == Today))
		{
			Farmer? player = CompanionNet.FarmerOf(m.Farmer);
			if (player?.currentLocation == null)
			{
				continue;
			}
			ScheduleOverlay? o = _schedules.Find(m.OverlayId);
			if (o == null || !OverlayStates.IsHolding(o.State) || Clock.Minutes(Game1.timeOfDay) < Clock.Minutes(m.Time) - MeetupRules.KeptEarlyMinutes)
			{
				continue;
			}
			NPC? npc = Game1.getCharacterFromName(m.Npc);
			if (npc?.currentLocation != player.currentLocation)
			{
				continue;
			}
			if (m.Place != MeetupPlaces.Home && Vector2.Distance(npc.Tile, player.Tile) > MeetupRules.KeptDistance)
			{
				continue;
			}
			// 剧情要演一段：等你空下来。
			if (m.Owner == MeetupRules.StoryOwner && (!Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.dialogueUp))
			{
				continue;
			}
			Keep(m, npc, o);
		}
	}

	private void Keep(Meetup m, NPC npc, ScheduleOverlay o)
	{
		m.State = MeetupStates.Kept;
		m.KeptAt = Game1.timeOfDay;
		m.ClosedDay = Today;
		bool story = false;
		if (m.Owner == MeetupRules.StoryOwner && StoryKept != null)
		{
			try
			{
				story = StoryKept(m, npc);
			}
			catch (Exception ex)
			{
				_log.Warn($"约见 {m.Id} 的剧情没演成：{ex.Message}");
			}
		}
		if (!story)
		{
			npc.doEmote(Character.happyEmote);
			CompanionNet.Send(m.Farmer, new CompanionEffect(m.Npc) { Friendship = MeetupRules.KeptFriendship, Line = Pick(Tr.Get("meetup.keptLines", "你来啦！|正等你呢。|准时，不错嘛。|我也刚到。")) });
		}
		_schedules.SetEnd(o.Id, Math.Min(o.End, Clock.Add(Game1.timeOfDay, MeetupRules.StayAfterKeptMinutes)));
		_log.Info($"约见 {m.Id}：{Game1.timeOfDay} 在 {m.Place} 见到了 {m.Npc}。");
		Version++;
		MarkDirty();
	}

	/// <summary>日程改写那段结束了：没见到就是失约（你没来）或作罢（他那边去不了）。</summary>
	private void OnOverlayClosed(ScheduleOverlay o)
	{
		if (o.Owner != MeetupRules.OverlayOwner)
		{
			return;
		}
		Meetup? m = All().FirstOrDefault(x => x.OverlayId == o.Id);
		if (m == null || !MeetupStates.IsOpen(m.State))
		{
			return;
		}
		if (o.State == OverlayStates.Done || o.CancelReason is PlanReasons.DayEnded or PlanReasons.Missed)
		{
			Close(m, MeetupStates.Missed, o.CancelReason.Length > 0 ? o.CancelReason : "the farmer didn't come", o.Day);
			CompanionNet.Send(m.Farmer, new CompanionEffect(m.Npc) { Friendship = MeetupRules.MissedFriendship, StoodUp = m.Owner != MeetupRules.StoryOwner });
			return;
		}
		if (o.CancelReason is PlanReasons.RainMove or PlanReasons.FarmerCancelled)
		{
			return;
		}
		Close(m, MeetupStates.CalledOff, o.CancelReason, o.Day);
		if (o.Day == Today && Context.IsWorldReady && o.CancelReason != PlanReasons.HeartEvent)
		{
			Tell(m, Tr.Get("meetup.calledOff", "{{npc}}今天去不了{{place}}了，改天再约吧。", ("npc", NpcName(m.Npc)), ("place", PlaceLabel(m))));
		}
	}

	private void Close(Meetup m, string state, string reason, int day)
	{
		m.State = state;
		m.Reason = reason;
		m.ClosedDay = Math.Max(day, m.Day);
		_log.Info($"约见 {m.Id}：{m.Npc} {state}（{reason}）。");
		Version++;
		MarkDirty();
		try
		{
			MeetupClosed?.Invoke(m);
		}
		catch (Exception ex)
		{
			_log.Warn($"约见 {m.Id} 结束后的处理出错：{ex.Message}");
		}
	}

	public string PromptBlock(string npc)
	{
		string display = NpcName(npc);
		return MeetupRules.PromptBlock(All().Where(m => Mine(m) && string.Equals(m.Npc, npc, StringComparison.OrdinalIgnoreCase)), Today, key => MeetupPlaces.Label(key, display));
	}

	// ---------------- 联机 ----------------

	public const string ResolveOp = "meetup.resolve";

	public const string ArrangeOp = "meetup.arrange";

	public const string CancelOp = "meetup.cancel";

	/// <summary>房主算好的日子和地点（<see cref="Offer"/> 只在农场帮手这边填）。</summary>
	public sealed record Resolved(bool Ok, int Day, int Time, string Place, bool Shifted, string Reason)
	{
		public PendingOffer Offer { get; init; } = new PendingOffer();
	}

	public sealed record ArrangeAsk(string Npc, int Day, int Time, string Place, string Summary);

	public sealed record Arranged(bool Ok, string Reason);

	public sealed record IdAsk(string Id);

	/// <summary>房主替农场帮手算、排、取消。</summary>
	public void WireNet(AISV.Game.Net.NetSync net)
	{
		CompanionNet.Wire(net, _log);
		net.On<PendingOffer>(ResolveOp, (from, p) =>
		{
			if (!Context.IsMainPlayer || Game1.getCharacterFromName(p.Npc) is not NPC npc)
			{
				return new Resolved(false, 0, 0, "", false, PlanReasons.NoVillager);
			}
			bool ok = TryResolve(p, npc, out int day, out int time, out MeetupPlace place, out bool shifted, out string reason, from);
			return new Resolved(ok, day, time, place.Key, shifted, reason);
		});
		net.On<ArrangeAsk>(ArrangeOp, (from, a) =>
		{
			if (!Context.IsMainPlayer)
			{
				return new Arranged(false, MeetupRules.OffReason);
			}
			Meetup? m = Arrange(a.Npc, a.Day, a.Time, a.Place, a.Summary, out string reason, farmer: from);
			return new Arranged(m != null, reason);
		});
		net.On<IdAsk>(CancelOp, (from, a) =>
		{
			if (Context.IsMainPlayer && Find(a.Id) is Meetup m && m.Farmer == from && MeetupStates.IsOpen(m.State))
			{
				CancelFor(m);
			}
		});
	}

	/// <summary>告诉约见的那位农夫（房主自己的直接弹提示）。</summary>
	private static void Tell(Meetup m, string text) => CompanionNet.Send(m.Farmer, new CompanionEffect(m.Npc) { Hud = text });

	// ---------------- 文字 ----------------

	public static string NpcName(string npc)
	{
		string name = TextTools.CleanForGame(Game1.getCharacterFromName(npc)?.displayName ?? "");
		return name.Length > 0 ? name : npc;
	}

	public static string PlaceLabel(Meetup m) => MeetupPlaces.Label(m.Place, NpcName(m.Npc));

	public static string WhenText(int day) => MeetupRules.WhenText(day, Today);

	private static void Hud(string text) => Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(text), HUDMessage.newQuest_type));

	private static string Pick(string lines)
	{
		string[] options = lines.Split('|');
		return TextTools.CleanForGame(options[Game1.random.Next(options.Length)]);
	}

	private void MarkDirty() => _store()?.MarkDirty(MeetupLog.Path);

	// ---------------- 调试 ----------------

	public object DumpData() => new { today = Today, now = Game1.timeOfDay, meetups = All(), overlays = _schedules.All() };
}
