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
/// 只在主机上运行；同伴模块关掉时不约新的。
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
	}

	/// <summary>玩家答复过提议时（从账本拿掉）。</summary>
	public Action<string>? ResolveOffer { get; set; }

	/// <summary>有人跟着剧情约的见到了：演事件；返回 false 时按普通约见处理。</summary>
	public Func<Meetup, NPC, bool>? StoryKept { get; set; }

	public event Action<Meetup>? MeetupClosed;

	public int Version { get; private set; }

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

	public IReadOnlyList<Meetup> Upcoming() => All().Where(m => MeetupStates.IsOpen(m.State)).OrderBy(m => m.Day).ThenBy(m => m.Time).ToList();

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
		foreach (Meetup m in All().Where(m => MeetupStates.IsOpen(m.State) && m.Day == Today).OrderBy(m => m.Time))
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
		if (m.Owner != MeetupRules.StoryOwner && npc != null && m.Time >= saloon.OpenTime && TryPlanOverlay(npc, m.Day, m.Time, saloon, out string? overlayId, out _))
		{
			m.MovedFrom = from;
			m.Place = saloon.Key;
			m.OverlayId = overlayId!;
			_log.Info($"约见 {m.Id}：下雨，从 {from} 改到酒吧。");
			Hud(Tr.Get("meetup.movedForRain", "下雨了，{{npc}}说改在{{place}}见。", ("npc", NpcName(m.Npc)), ("place", MeetupPlaces.Label(saloon.Key))));
			Version++;
			MarkDirty();
			return;
		}
		Close(m, MeetupStates.CalledOff, PlanReasons.Rain, Today);
		Hud(Tr.Get("meetup.calledOffRain", "下雨了，{{npc}}说约在{{place}}的事改天再说。", ("npc", NpcName(m.Npc)), ("place", MeetupPlaces.Label(from, npc == null ? null : NpcName(m.Npc)))));
	}

	// ---------------- 当面的提议：聊完问你 ----------------

	public void Enqueue(PendingOffer offer)
	{
		if (Enabled && Context.IsMainPlayer && !_toConfirm.Contains(offer))
		{
			_toConfirm.Enqueue(offer);
		}
	}

	public void OnUpdateTicked()
	{
		if (Log == null || !Context.IsMainPlayer)
		{
			return;
		}
		if (++_ticks % 30 == 0 && !Game1.eventUp)
		{
			CheckKept();
		}
		if (_toConfirm.Count == 0)
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
			Ask(_toConfirm.Dequeue());
		}
	}

	private void Ask(PendingOffer p)
	{
		NPC? npc = Game1.getCharacterFromName(p.Npc);
		if (npc == null || p.ExpiresDay < Today)
		{
			ResolveOffer?.Invoke(p.Id);
			return;
		}
		if (!TryResolve(p, npc, out int day, out int time, out MeetupPlace place, out bool shifted, out string why))
		{
			ResolveOffer?.Invoke(p.Id);
			_log.Info($"{npc.Name} 的约见（{p.When} {p.Time} {p.Where}）排不上：{why}。");
			NoteCantArrange(npc.Name, why);
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
	public bool TryResolve(PendingOffer p, NPC npc, out int day, out int time, out MeetupPlace place, out bool shifted, out string reason)
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
			int earliest = EarliestToday(npc, place);
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
		string? why = Check(npc, day, time, place);
		if (why != null)
		{
			reason = why;
			return false;
		}
		return true;
	}

	public int EarliestToday(NPC npc, MeetupPlace place)
	{
		int walk = 60;
		if (Resolve(place, npc, out string loc, out Point tile))
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
	public static bool Resolve(MeetupPlace p, NPC npc, out string location, out Point tile)
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
			FarmHouse? house = Game1.player == null ? null : Utility.getHomeOfFarmer(Game1.player);
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

	public string? Check(NPC npc, int day, int time, MeetupPlace place)
	{
		if (!Enabled)
		{
			return MeetupRules.OffReason;
		}
		if (ChildSafety.IsChild(npc.Name, npc.Age))
		{
			return MeetupRules.ChildReason;
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
		IReadOnlyList<Meetup> open = Upcoming();
		if (open.Any(m => string.Equals(m.Npc, npc.Name, StringComparison.OrdinalIgnoreCase)))
		{
			return MeetupRules.AlreadyReason;
		}
		if (open.Count(m => m.Day == day) >= MeetupRules.MaxPerDay)
		{
			return MeetupRules.FullReason;
		}
		if (!Resolve(place, npc, out string loc, out Point tile))
		{
			return PlanReasons.NoPlace;
		}
		_schedules.Plan(npc.Name, MeetupRules.OverlayOwner, day, time, MeetupRules.End(time, place), loc, tile.X, tile.Y, 2, out string reason, dryRun: true);
		return reason.Length > 0 ? reason : null;
	}

	public Meetup? Arrange(string npcName, int day, int time, string placeKey, string summary, out string reason, string owner = "", string ownerRef = "")
	{
		reason = "";
		NPC? npc = Game1.getCharacterFromName(npcName);
		MeetupPlace? place = MeetupPlaces.Get(placeKey);
		if (npc == null || place == null || Log is not MeetupLog log)
		{
			reason = PlanReasons.NoVillager;
			return null;
		}
		string? why = Check(npc, day, time, place);
		if (why != null)
		{
			reason = why;
			return null;
		}
		if (!TryPlanOverlay(npc, day, time, place, out string? overlayId, out reason))
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
			OwnerRef = ownerRef
		};
		log.Items.Add(m);
		_log.Info($"约见 {m.Id}：{npc.Name}，第 {day} 天 {time} 在 {place.Key}（{overlayId}）。");
		Version++;
		MarkDirty();
		return m;
	}

	private bool TryPlanOverlay(NPC npc, int day, int time, MeetupPlace place, out string? overlayId, out string reason)
	{
		overlayId = null;
		if (!Resolve(place, npc, out string loc, out Point tile))
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
		if (m == null || !MeetupStates.IsOpen(m.State))
		{
			return false;
		}
		_schedules.Cancel(m.OverlayId, PlanReasons.FarmerCancelled);
		Close(m, MeetupStates.CancelledByPlayer, PlanReasons.FarmerCancelled, Today);
		if (m.Day == Today)
		{
			ChangeFriendship(m.Npc, MeetupRules.SameDayCancelFriendship);
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
		Farmer? player = Game1.player;
		if (player?.currentLocation == null || !Enabled)
		{
			return;
		}
		foreach (Meetup m in All().Where(m => MeetupStates.IsOpen(m.State) && m.Day == Today))
		{
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
			ChangeFriendship(m.Npc, MeetupRules.KeptFriendship);
			npc.doEmote(Character.happyEmote);
			Say(npc, Tr.Get("meetup.keptLines", "你来啦！|正等你呢。|准时，不错嘛。|我也刚到。"));
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
			ChangeFriendship(m.Npc, MeetupRules.MissedFriendship);
			return;
		}
		if (o.CancelReason is PlanReasons.RainMove or PlanReasons.FarmerCancelled)
		{
			return;
		}
		Close(m, MeetupStates.CalledOff, o.CancelReason, o.Day);
		if (o.Day == Today && Context.IsWorldReady && o.CancelReason != PlanReasons.HeartEvent)
		{
			Hud(Tr.Get("meetup.calledOff", "{{npc}}今天去不了{{place}}了，改天再约吧。", ("npc", NpcName(m.Npc)), ("place", PlaceLabel(m))));
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

	private static void ChangeFriendship(string npc, int points)
	{
		NPC? n = Game1.getCharacterFromName(npc);
		if (n != null && Game1.player != null && points != 0)
		{
			Game1.player.changeFriendship(points, n);
		}
	}

	public string PromptBlock(string npc)
	{
		string display = NpcName(npc);
		return MeetupRules.PromptBlock(All().Where(m => string.Equals(m.Npc, npc, StringComparison.OrdinalIgnoreCase)), Today, key => MeetupPlaces.Label(key, display));
	}

	// ---------------- 文字 ----------------

	public static string NpcName(string npc)
	{
		string name = TextTools.CleanForGame(Game1.getCharacterFromName(npc)?.displayName ?? "");
		return name.Length > 0 ? name : npc;
	}

	public static string PlaceLabel(Meetup m) => MeetupPlaces.Label(m.Place, NpcName(m.Npc));

	public static string WhenText(int day) => MeetupRules.WhenText(day, Today);

	private static void Hud(string text) => Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(text), HUDMessage.newQuest_type));

	private static void Say(NPC npc, string lines)
	{
		string[] options = lines.Split('|');
		NpcSpeech.Say(npc, TextTools.CleanForGame(options[Game1.random.Next(options.Length)]));
	}

	private void MarkDirty() => _store()?.MarkDirty(MeetupLog.Path);

	// ---------------- 调试 ----------------

	public object DumpData() => new { today = Today, now = Game1.timeOfDay, meetups = All(), overlays = _schedules.All() };
}
