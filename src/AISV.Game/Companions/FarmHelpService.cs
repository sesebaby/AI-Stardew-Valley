using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace AISV.Game.Companions;

/// <summary>接 Squad 的那一头：上门帮工时让人在一片地里干活。</summary>
public interface ISquadCrew
{
	bool IsAvailable { get; }

	void Enqueue(PendingOffer offer);

	bool StartSiteWork(NPC npc, string task, string location, Point center, int radius);

	void EndSiteWork(NPC npc);

	/// <summary>现在跟着你的人（帮工页）。</summary>
	IReadOnlyList<(string Npc, string Task, int Price, int? JoinedAt, bool Site)> Members();

	string PromptLine(string npc);
}

/// <summary>
/// 约好时间上门帮工：接受时付钱；到点走到农舍门前，定下要干的活，你在农场时由 Squad 当场干，不在时按进度替他干；
/// 干完、到点或你让他先回去时，没干完的按比例退钱。来不了、下雨不用浇水、没活可干全额退钱。
/// 只在主机上运行。
/// </summary>
public sealed class FarmHelpService
{
	private const string SquadChestPrefix = "TheStardewSquad_SquadInventory_";

	private const int SquadChestSlots = 36;

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ScheduleOverlayService _schedules;

	private readonly MeetupService _meetups;

	private readonly Func<EmotionalMemory?> _memory;

	private readonly ILog _log;

	private int _ticks;

	public FarmHelpService(Func<ModConfig> config, Func<DocumentStore?> store, ScheduleOverlayService schedules, MeetupService meetups, Func<EmotionalMemory?> memory, ILog log)
	{
		_config = config;
		_store = store;
		_schedules = schedules;
		_meetups = meetups;
		_memory = memory;
		_log = log;
		_schedules.Closed += OnOverlayClosed;
	}

	public ISquadCrew? Crew { get; set; }

	public Action<string>? ResolveOffer { get; set; }

	public int Version { get; private set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.CompanionModuleOn;
		}
	}

	private FarmHelpLog? Log => _store()?.Get(FarmHelpLog.Path, FarmHelpLog.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public IReadOnlyList<FarmHelpJob> All() => Log?.Items.ToList() ?? new List<FarmHelpJob>();

	public FarmHelpJob? Find(string id) => Log?.Items.FirstOrDefault(j => j.Id == id);

	public IReadOnlyList<FarmHelpJob> Open() => All().Where(j => FarmHelpStates.IsOpen(j.State)).OrderBy(j => j.Day).ThenBy(j => j.Start).ToList();

	// ---------------- 生命周期 ----------------

	public void OnSaveLoaded() => Version++;

	/// <summary>早上：过去没结的收尾；下雨不用来浇水；提醒今天谁来。日程改写先跑。</summary>
	public void OnDayStarted()
	{
		if (Log is not FarmHelpLog log)
		{
			return;
		}
		foreach (FarmHelpJob j in All().Where(j => FarmHelpStates.IsOpen(j.State) && j.Day < Today))
		{
			if (j.State == FarmHelpStates.Working)
			{
				Close(j, FarmHelpStates.Done, PlanReasons.DayEnded, 0, Today - 1);
			}
			else
			{
				Close(j, FarmHelpStates.CalledOff, PlanReasons.Lost, j.Price, Today - 1);
			}
		}
		if (log.Items.RemoveAll(j => !FarmHelpStates.IsOpen(j.State) && Today - j.ClosedDay > FarmHelpRules.ClosedKeepDays) > 0)
		{
			MarkDirty();
		}
		if (!Enabled || !Context.IsMainPlayer)
		{
			return;
		}
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Scheduled && j.Day == Today).OrderBy(j => j.Start))
		{
			if (j.Task == "water" && (Game1.isRaining || Game1.IsRainingHere(Game1.getFarm())))
			{
				Close(j, FarmHelpStates.CalledOff, PlanReasons.Rain, j.Price, Today);
				_schedules.Cancel(j.OverlayId, PlanReasons.Rain);
				Hud(Tr.Get("farmhelp.rainOff", "下雨了，地都湿了，{{npc}}今天不用来浇水了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Price));
				continue;
			}
			Hud(Tr.Get("farmhelp.reminder", "今天 {{time}}，{{npc}}来农场帮你{{task}}。", ("time", Clock.Text(j.Start)), ("npc", MeetupService.NpcName(j.Npc)), ("task", FarmHelpRules.TaskLabel(j.Task))));
		}
	}

	/// <summary>入夜：还在干的按进度结算。</summary>
	public void OnDayEnding()
	{
		if (!Context.IsMainPlayer)
		{
			return;
		}
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Working))
		{
			Finish(j, PlanReasons.DayEnded);
		}
	}

	// ---------------- 当面的提议 ----------------

	/// <summary>Squad 问提议之前先问这里：是约好时间上门的就由这里问，返回 true。</summary>
	public bool TryHandle(PendingOffer p, NPC npc)
	{
		if (Log == null || !Enabled || !Context.IsMainPlayer || !FarmHelpRules.IsFarmVisit(p.Type, p.Task, p.When, p.Time, Game1.timeOfDay, out int offset, out int time))
		{
			return false;
		}
		Ask(p, npc, Today + offset, time);
		return true;
	}

	private void Ask(PendingOffer p, NPC npc, int day, int time)
	{
		string name = MeetupService.NpcName(npc.Name);
		string? why = Check(npc, p.Task, day, time);
		if (why != null)
		{
			ResolveOffer?.Invoke(p.Id);
			_log.Info($"{npc.Name} 的帮工（{p.Task}，第 {day} 天 {time}）排不上：{why}。");
			Hud(CantArrangeText(name, why));
			return;
		}
		int price = p.Price;
		Response[] answers =
		{
			new Response("yes", price > 0 ? Tr.Get("farmhelp.yesPaid", "好（{{price}}g）", ("price", price)) : Tr.Get("farmhelp.yes", "好，麻烦你了")),
			new Response("no", Tr.Get("farmhelp.no", "不用了"))
		};
		string question = Tr.Get("farmhelp.ask", "{{npc}}{{when}} {{time}} 来农场帮你{{task}}（大约干到 {{end}}）。", ("npc", name), ("when", MeetupService.WhenText(day)), ("time", Clock.Text(time)), ("task", FarmHelpRules.TaskLabel(p.Task)), ("end", Clock.Text(FarmHelpRules.EndOf(p.Task, time))));
		Game1.currentLocation.createQuestionDialogue(TextTools.CleanForGame(question), answers, (who, answer) =>
		{
			ResolveOffer?.Invoke(p.Id);
			if (answer == "yes")
			{
				Hud(PayAndArrange(who, npc.Name, p.Task, day, time, price, p.Summary, out _));
			}
		});
	}

	/// <summary>钱够就排上并付钱；返回说给玩家听的一句。</summary>
	public string PayAndArrange(Farmer who, string npc, string task, int day, int time, int price, string summary, out bool arranged)
	{
		arranged = false;
		string name = MeetupService.NpcName(npc);
		if (price > 0 && who.Money < price)
		{
			return Tr.Get("farmhelp.notEnoughMoney", "钱不够：要 {{price}}g。", ("price", price));
		}
		FarmHelpJob? job = Arrange(npc, task, day, time, price, summary, out string reason);
		if (job == null)
		{
			return CantArrangeText(name, reason);
		}
		if (price > 0)
		{
			who.Money -= price;
			Game1.playSound("purchaseClick");
		}
		arranged = true;
		return Tr.Get("farmhelp.arranged", "说好了：{{when}} {{time}}，{{npc}}来农场帮你{{task}}。", ("when", MeetupService.WhenText(day)), ("time", Clock.Text(time)), ("npc", name), ("task", FarmHelpRules.TaskLabel(task)));
	}

	private static string CantArrangeText(string name, string why) => Tr.Get("farmhelp.cantArrange", "{{npc}}想了想：{{why}}，改天再说吧。", ("npc", name), ("why", FarmHelpRules.Explain(why)));

	/// <summary>发消息或群里说"来帮我浇水"：算出哪天几点来。今天不早于走到农场的时间；有工作的人没说时间就挪到下班以后。</summary>
	public bool TryResolveChat(PendingOffer p, NPC npc, out int day, out int time, out bool shifted, out string reason)
	{
		shifted = false;
		reason = "";
		day = Today;
		time = FarmHelpRules.DefaultTime;
		if (Log == null || !Enabled || Crew?.IsAvailable != true)
		{
			reason = FarmHelpRules.OffReason;
			return false;
		}
		int offset = MeetupRules.ParseDayOffset(p.When, p.Time, Game1.timeOfDay);
		if (offset > 1)
		{
			reason = FarmHelpRules.DaysReason;
			return false;
		}
		day = Today + offset;
		int? asked = MeetupRules.ParseTime(p.Time) ?? MeetupRules.ParseTime(p.When);
		int earliest = FarmHelpRules.EarliestTime;
		if (offset == 0)
		{
			MeetupPlace? farm = MeetupPlaces.Get(MeetupPlaces.Farm);
			earliest = farm == null ? Clock.Add(Game1.timeOfDay, FarmHelpRules.MinNoticeMinutes) : _meetups.EarliestToday(npc, farm);
		}
		if (!asked.HasValue)
		{
			time = offset == 0 ? earliest : FarmHelpRules.DefaultTime;
			shifted = offset == 0;
			int after = FarmHelpRules.AfterWork(npc.Name, day, time, p.Task);
			if (after != time)
			{
				time = after;
				shifted = true;
			}
		}
		else
		{
			time = asked.Value;
			if (offset == 0 && Clock.Minutes(time) < Clock.Minutes(earliest))
			{
				time = earliest;
				shifted = true;
			}
		}
		if (offset == 0 && time > FarmHelpRules.LatestStart)
		{
			reason = FarmHelpRules.TooLateReason;
			return false;
		}
		string? why = Check(npc, p.Task, day, time);
		if (why != null)
		{
			reason = why;
			return false;
		}
		return true;
	}

	// ---------------- 排 ----------------

	public string? Check(NPC npc, string task, int day, int time)
	{
		if (Log == null || !Enabled)
		{
			return FarmHelpRules.OffReason;
		}
		if (ChildSafety.IsChild(npc.Name, npc.Age))
		{
			return FarmHelpRules.ChildReason;
		}
		if (!FarmHelpRules.Tasks.Contains(task))
		{
			return FarmHelpRules.TaskReason;
		}
		if (day < Today || day > Today + 1)
		{
			return FarmHelpRules.DaysReason;
		}
		if (time < FarmHelpRules.EarliestTime || time > FarmHelpRules.LatestStart)
		{
			return FarmHelpRules.HoursReason;
		}
		if (day == Today && Clock.Minutes(time) < Clock.Minutes(Game1.timeOfDay) + FarmHelpRules.MinNoticeMinutes)
		{
			return FarmHelpRules.SoonReason;
		}
		IReadOnlyList<FarmHelpJob> open = Open();
		if (open.Any(j => j.Day == day && string.Equals(j.Npc, npc.Name, StringComparison.OrdinalIgnoreCase)))
		{
			return FarmHelpRules.ComingReason;
		}
		if (open.Count(j => j.Day == day) >= FarmHelpRules.MaxPerDay)
		{
			return FarmHelpRules.FullReason;
		}
		int end = FarmHelpRules.EndOf(task, time);
		string? shop = FarmHelpRules.ShopConflict(npc.Name, day, time, end);
		if (shop != null)
		{
			return shop;
		}
		if (!TryPorch(out Point porch))
		{
			return FarmHelpRules.NoHouseReason;
		}
		_schedules.Plan(npc.Name, FarmHelpRules.Owner, day, time, end, "Farm", porch.X, porch.Y, 2, out string reason, dryRun: true);
		return reason.Length > 0 ? reason : null;
	}

	public FarmHelpJob? Arrange(string npcName, string task, int day, int time, int price, string summary, out string reason)
	{
		reason = "";
		NPC? npc = Game1.getCharacterFromName(npcName);
		if (npc == null || Log is not FarmHelpLog log)
		{
			reason = PlanReasons.NoVillager;
			return null;
		}
		string? why = Check(npc, task, day, time);
		if (why != null)
		{
			reason = why;
			return null;
		}
		TryPorch(out Point porch);
		int end = FarmHelpRules.EndOf(task, time);
		ScheduleOverlay? o = _schedules.Plan(npc.Name, FarmHelpRules.Owner, day, time, end, "Farm", porch.X, porch.Y, 2, out reason);
		if (o == null)
		{
			return null;
		}
		FarmHelpJob j = new FarmHelpJob
		{
			Id = "h" + log.NextNumber++,
			Npc = npc.Name,
			Task = task,
			Day = day,
			Start = time,
			End = end,
			Price = Math.Max(0, price),
			OverlayId = o.Id,
			Summary = summary ?? "",
			CreatedDay = Today
		};
		log.Items.Add(j);
		_log.Info($"帮工 {j.Id}：{npc.Name} 第 {day} 天 {time}～{end} 来{task}（{j.Price}g，{o.Id} → 农场 {o.X},{o.Y}）。");
		Version++;
		MarkDirty();
		return j;
	}

	/// <summary>农舍门前旁边一格（不挡门）。</summary>
	private static bool TryPorch(out Point porch)
	{
		porch = Point.Zero;
		FarmHouse? house = Game1.player == null ? null : Utility.getHomeOfFarmer(Game1.player);
		if (house == null || Game1.getFarm() == null)
		{
			return false;
		}
		Point door = house.getFrontDoorSpot();
		porch = new Point(door.X + 2, door.Y + 1);
		return true;
	}

	// ---------------- 跑 ----------------

	/// <summary>每 15 帧：人到门前就开工；你让他走了就结算。</summary>
	public void OnUpdateTicked()
	{
		if (Log == null || !Context.IsMainPlayer || ++_ticks % 15 != 0 || Game1.eventUp)
		{
			return;
		}
		foreach (FarmHelpJob j in All().Where(j => FarmHelpStates.IsOpen(j.State) && j.Day == Today))
		{
			try
			{
				if (j.State == FarmHelpStates.Scheduled)
				{
					if (_schedules.Find(j.OverlayId)?.State == OverlayStates.Arrived)
					{
						Begin(j);
					}
				}
				else if (j.UsedSquad && _ticks % 60 == 0 && !SquadMark.IsControlled(j.Npc))
				{
					Finish(j, FarmHelpRules.SentHome, walkBack: false);
				}
			}
			catch (Exception ex)
			{
				_log.Warn($"帮工 {j.Id} 出错：{ex}");
			}
		}
	}

	/// <summary>每 10 分钟：替他干；干完或到点结算。</summary>
	public void OnTimeChanged()
	{
		if (Log == null || !Context.IsMainPlayer || Game1.newDay)
		{
			return;
		}
		int now = Game1.timeOfDay;
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Working))
		{
			try
			{
				RemoteWork(j);
				int done = CountDone(j);
				if (done >= j.Target)
				{
					Finish(j, FarmHelpRules.AllDone);
				}
				else if (now >= j.End)
				{
					Finish(j, PlanReasons.TimeUp);
				}
				else if (done != j.Progress)
				{
					j.Progress = done;
					Version++;
					MarkDirty();
				}
			}
			catch (Exception ex)
			{
				_log.Warn($"帮工 {j.Id} 干活时出错：{ex}");
			}
		}
	}

	/// <summary>人到门前：定下要干的活，交给 Squad。没活就全额退钱、回去。</summary>
	public bool Begin(FarmHelpJob j)
	{
		NPC? npc = Game1.getCharacterFromName(j.Npc);
		Farm? farm = Game1.getFarm();
		if (npc == null || farm == null || j.State != FarmHelpStates.Scheduled)
		{
			return false;
		}
		List<string> targets = FindTargets(j.Task, farm, out Point center, out int radius);
		if (targets.Count == 0)
		{
			Close(j, FarmHelpStates.NothingToDo, PlanReasons.NothingToDo, j.Price, Today);
			Say(npc, Tr.Get("farmhelp.nothingLines", "嗯？看起来没什么要我干的嘛。|地里都弄好了啊，那我先回去了。"));
			Hud(Tr.Get("farmhelp.nothing", "{{npc}}来了一看，没什么要干的，就回去了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Price));
			_schedules.Cancel(j.OverlayId, PlanReasons.NothingToDo);
			return true;
		}
		j.Targets = targets.Take(FarmHelpRules.CapacityOf(j.Task)).ToList();
		j.Target = j.Targets.Count;
		j.CenterX = center.X;
		j.CenterY = center.Y;
		j.Radius = radius;
		j.StartedAt = Game1.timeOfDay;
		j.State = FarmHelpStates.Working;
		j.Progress = CountDone(j);
		j.UsedSquad = Crew?.StartSiteWork(npc, j.Task, "Farm", center, radius) ?? false;
		_log.Info($"帮工 {j.Id}：{j.Npc} {Game1.timeOfDay} 开始{j.Task}，{j.Target} 处，中心 ({center.X},{center.Y}) 半径 {radius}，{(j.UsedSquad ? "Squad 当场干" : "按进度算")}。");
		Say(npc, Tr.Get("farmhelp.arriveLines", "我来啦，这就开始干。|早啊，交给我吧。|来了来了，从哪儿开始？"));
		Version++;
		MarkDirty();
		return true;
	}

	/// <summary>结算：退没干完那部分的钱，送他回日程。</summary>
	public void Finish(FarmHelpJob j, string reason, bool walkBack = true)
	{
		if (j.State != FarmHelpStates.Working)
		{
			return;
		}
		NPC? npc = Game1.getCharacterFromName(j.Npc);
		int done = CountDone(j);
		j.Progress = done;
		int refund = FarmHelpRules.RefundFor(j.Price, done, j.Target);
		j.FinishedAt = Game1.timeOfDay;
		Close(j, FarmHelpStates.Done, reason, refund, Today);
		if (npc != null)
		{
			if (j.UsedSquad)
			{
				Crew?.EndSiteWork(npc);
			}
			ScheduleOverlay? o = _schedules.Find(j.OverlayId);
			if (o != null && OverlayStates.IsOpen(o.State))
			{
				_schedules.Cancel(o.Id, PlanReasons.JobFinished);
			}
			else if (walkBack && reason != PlanReasons.DayEnded)
			{
				_schedules.WalkBack(npc);
			}
			if (reason != PlanReasons.DayEnded)
			{
				Say(npc, done >= j.Target ? Tr.Get("farmhelp.doneLines", "都弄好了，我回去啦。|活干完了，有空再叫我。|好了！今天就到这儿。") : Tr.Get("farmhelp.partLines", "没全弄完，剩下的不多了。|今天先到这儿吧，我回去了。"));
			}
		}
		if (reason != PlanReasons.DayEnded || done > 0)
		{
			Hud(Tr.Get("farmhelp.finished", "{{npc}}帮你{{task}}：做完 {{done}}/{{target}}。", ("npc", MeetupService.NpcName(j.Npc)), ("task", FarmHelpRules.TaskLabel(j.Task)), ("done", done), ("target", j.Target)) + FarmHelpRules.RefundText(j.Refund));
		}
		Remember(j);
		_log.Info($"帮工 {j.Id}：{j.Npc} 结束（{reason}），{Game1.timeOfDay}，做完 {done}/{j.Target}（替他干了 {j.RemoteDone}），退 {j.Refund}g / {j.Price}g。");
	}

	/// <summary>你说不用来了：全额退钱；当天才说好感 -10。</summary>
	public bool CancelByPlayer(string id)
	{
		FarmHelpJob? j = Find(id);
		if (j == null || j.State != FarmHelpStates.Scheduled)
		{
			return false;
		}
		Close(j, FarmHelpStates.CancelledByPlayer, PlanReasons.FarmerCancelled, j.Price, Today);
		_schedules.Cancel(j.OverlayId, PlanReasons.FarmerCancelled);
		if (j.Day == Today && Game1.getCharacterFromName(j.Npc) is NPC npc)
		{
			Game1.player.changeFriendship(FarmHelpRules.SameDayCancelFriendship, npc);
		}
		return true;
	}

	/// <summary>调试：人没走到也直接开工。</summary>
	public bool DebugStart(string id)
	{
		FarmHelpJob? j = Find(id);
		NPC? npc = j == null ? null : Game1.getCharacterFromName(j.Npc);
		ScheduleOverlay? o = j == null ? null : _schedules.Find(j.OverlayId);
		if (j == null || npc == null || o == null || j.State != FarmHelpStates.Scheduled)
		{
			return false;
		}
		Farm farm = Game1.getFarm();
		if (npc.currentLocation != farm || npc.TilePoint != new Point(o.X, o.Y))
		{
			npc.controller = null;
			npc.Halt();
			Game1.warpCharacter(npc, farm, new Vector2(o.X, o.Y));
		}
		return Begin(j);
	}

	public void DebugClear()
	{
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Working))
		{
			Finish(j, PlanReasons.Cleared);
		}
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Scheduled))
		{
			Close(j, FarmHelpStates.CalledOff, PlanReasons.Cleared, 0, Today);
			_schedules.Cancel(j.OverlayId, PlanReasons.Cleared);
		}
		Log?.Items.Clear();
		Version++;
		MarkDirty();
	}

	private void OnOverlayClosed(ScheduleOverlay o)
	{
		if (o.Owner != FarmHelpRules.Owner)
		{
			return;
		}
		FarmHelpJob? j = All().FirstOrDefault(x => x.OverlayId == o.Id);
		if (j == null || !FarmHelpStates.IsOpen(j.State))
		{
			return;
		}
		if (j.State == FarmHelpStates.Working)
		{
			// Squad 接手时日程改写自己关掉，不是结束。
			if (o.CancelReason != PlanReasons.WithFarmer)
			{
				Finish(j, o.State == OverlayStates.Done ? PlanReasons.TimeUp : o.CancelReason, walkBack: false);
			}
			return;
		}
		Close(j, FarmHelpStates.CalledOff, o.CancelReason.Length > 0 ? o.CancelReason : "didn't make it", j.Price, o.Day);
		if (o.Day == Today && Context.IsWorldReady && o.CancelReason != PlanReasons.DayEnded)
		{
			Hud(Tr.Get("farmhelp.calledOff", "{{npc}}今天来不了农场了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Refund));
		}
	}

	private void Close(FarmHelpJob j, string state, string reason, int refund, int day)
	{
		j.State = state;
		j.Reason = reason;
		j.ClosedDay = Math.Max(day, j.Day);
		j.Refund = Math.Max(0, Math.Min(j.Price, refund));
		if (j.Refund > 0 && Game1.player != null)
		{
			Game1.player.Money += j.Refund;
		}
		_log.Info($"帮工 {j.Id}：{j.Npc} {state}（{reason}），退 {j.Refund}g。");
		Version++;
		MarkDirty();
	}

	// ---------------- 活和进度 ----------------

	private static List<string> FindTargets(string task, Farm farm, out Point center, out int radius)
	{
		center = Point.Zero;
		radius = 8;
		if (task == "animals")
		{
			radius = 24;
			List<FarmAnimal> animals = farm.getAllFarmAnimals().Where(a => !a.wasPet.Value).ToList();
			List<FarmAnimal> outdoors = animals.Where(a => a.currentLocation == farm).ToList();
			if (outdoors.Count > 0)
			{
				center = new Point((int)outdoors.Average(a => a.TilePoint.X), (int)outdoors.Average(a => a.TilePoint.Y));
			}
			else
			{
				TryPorch(out center);
			}
			return animals.Select(a => a.myID.Value.ToString()).ToList();
		}
		List<(int X, int Y)> tiles = new List<(int, int)>();
		if (task == "clear")
		{
			radius = 10;
			foreach (KeyValuePair<Vector2, StardewValley.Object> pair in farm.objects.Pairs)
			{
				if (pair.Value.IsTwig() || pair.Value.IsBreakableStone())
				{
					tiles.Add(((int)pair.Key.X, (int)pair.Key.Y));
				}
			}
		}
		else
		{
			foreach (KeyValuePair<Vector2, TerrainFeature> pair in farm.terrainFeatures.Pairs)
			{
				if (pair.Value is HoeDirt dirt && (task == "water" ? NeedsWater(dirt) : dirt.readyForHarvest()))
				{
					tiles.Add(((int)pair.Key.X, (int)pair.Key.Y));
				}
			}
		}
		List<(int X, int Y)> near = FarmHelpRules.Densest(tiles, radius, out (int X, int Y) c);
		center = new Point(c.X, c.Y);
		return near.Select(t => t.X + "," + t.Y).ToList();
	}

	private static bool NeedsWater(HoeDirt dirt) => dirt.crop != null && !dirt.crop.dead.Value && !dirt.isWatered() && dirt.needsWatering();

	private static bool TryTile(string target, out Vector2 tile)
	{
		tile = Vector2.Zero;
		string[] parts = target.Split(',');
		if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
		{
			return false;
		}
		tile = new Vector2(x, y);
		return true;
	}

	private static FarmAnimal? AnimalOf(string target, Farm farm) => long.TryParse(target, out long id) ? farm.getAllFarmAnimals().FirstOrDefault(x => x.myID.Value == id) : null;

	private static bool IsDone(FarmHelpJob j, string target, Farm farm)
	{
		if (j.Task == "animals")
		{
			return AnimalOf(target, farm) is not FarmAnimal a || a.wasPet.Value;
		}
		if (!TryTile(target, out Vector2 tile))
		{
			return true;
		}
		if (j.Task == "clear")
		{
			return !farm.objects.TryGetValue(tile, out StardewValley.Object obj) || !(obj.IsTwig() || obj.IsBreakableStone());
		}
		if (!farm.terrainFeatures.TryGetValue(tile, out TerrainFeature f) || f is not HoeDirt dirt || dirt.crop == null)
		{
			return true;
		}
		return j.Task == "water" ? dirt.isWatered() || !dirt.needsWatering() : !dirt.readyForHarvest();
	}

	public static int CountDone(FarmHelpJob j)
	{
		Farm? farm = Game1.getFarm();
		return farm == null ? 0 : j.Targets.Count(t => IsDone(j, t, farm));
	}

	/// <summary>那块地没有农夫就按速度全干；你在农场看着、Squad 在当场干时，只替屏幕外的干，速度减半。</summary>
	private void RemoteWork(FarmHelpJob j)
	{
		Farm farm = Game1.getFarm();
		bool farmerOnFarm = Game1.getOnlineFarmers().Any(f => f.currentLocation == farm);
		int budget = FarmHelpRules.RateOf(j.Task);
		if (farmerOnFarm && j.UsedSquad && j.Task != "animals")
		{
			budget = Math.Max(1, budget / 2);
		}
		int did = 0;
		foreach (string target in j.Targets)
		{
			if (did >= budget)
			{
				break;
			}
			if (IsDone(j, target, farm))
			{
				continue;
			}
			if (j.Task == "animals")
			{
				FarmAnimal? a = AnimalOf(target, farm);
				if (a == null || Game1.getOnlineFarmers().Any(f => f.currentLocation == a.currentLocation))
				{
					continue;
				}
				PetQuietly(a);
				did++;
				continue;
			}
			if (!TryTile(target, out Vector2 tile) || (farmerOnFarm && (!j.UsedSquad || Utility.isOnScreen(tile * 64f, 64))))
			{
				continue;
			}
			if (DoTile(j.Task, farm, tile))
			{
				did++;
			}
		}
		if (did > 0)
		{
			j.RemoteDone += did;
			MarkDirty();
		}
	}

	private bool DoTile(string task, Farm farm, Vector2 tile)
	{
		switch (task)
		{
			case "water":
				if (farm.terrainFeatures.TryGetValue(tile, out TerrainFeature f) && f is HoeDirt dirt)
				{
					dirt.state.Value = HoeDirt.watered;
					return true;
				}
				return false;
			case "harvest":
				return HarvestQuietly(farm, tile);
			case "clear":
				if (farm.objects.TryGetValue(tile, out StardewValley.Object obj))
				{
					string? drop = obj.IsTwig() ? "(O)388" : obj.IsBreakableStone() ? "(O)390" : null;
					if (drop == null)
					{
						return false;
					}
					farm.objects.Remove(tile);
					Store(ItemRegistry.Create(drop));
					return true;
				}
				return false;
		}
		return false;
	}

	/// <summary>收获掉出来的东西会落在当前地图，两边都看。</summary>
	private bool HarvestQuietly(Farm farm, Vector2 tile)
	{
		if (!farm.terrainFeatures.TryGetValue(tile, out TerrainFeature f) || f is not HoeDirt dirt || dirt.crop == null || !dirt.readyForHarvest())
		{
			return false;
		}
		GameLocation? here = Game1.currentLocation;
		HashSet<Debris> before = new HashSet<Debris>(farm.debris);
		if (here != null && here != farm)
		{
			before.UnionWith(here.debris);
		}
		dirt.crop.harvest((int)tile.X, (int)tile.Y, dirt, null, isForcedScytheHarvest: true);
		if (dirt.crop == null || !dirt.crop.RegrowsAfterHarvest())
		{
			dirt.destroyCrop(showAnimation: false);
		}
		foreach (GameLocation loc in here != null && here != farm ? new[] { farm, here } : new GameLocation[] { farm })
		{
			foreach (Debris d in loc.debris.Where(d => !before.Contains(d)).ToList())
			{
				if (d.item != null)
				{
					Store(d.item);
					loc.debris.Remove(d);
				}
			}
		}
		return true;
	}

	private static void PetQuietly(FarmAnimal a)
	{
		if (a.wasPet.Value)
		{
			return;
		}
		a.wasPet.Value = true;
		a.friendshipTowardFarmer.Value = Math.Min(1000, a.friendshipTowardFarmer.Value + (a.wasAutoPet.Value ? 7 : 15));
		int drain = a.GetAnimalData()?.HappinessDrain ?? 0;
		a.happiness.Value = (byte)Math.Min(255, a.happiness.Value + Math.Max(5, 30 + drain));
	}

	/// <summary>放进小队箱子；满了放背包，再不行放门前。</summary>
	private void Store(Item item)
	{
		Farmer player = Game1.player;
		if (Crew?.IsAvailable == true)
		{
			StardewValley.Inventories.Inventory chest = player.team.GetOrCreateGlobalInventory(SquadChestPrefix + player.UniqueMultiplayerID);
			foreach (Item? slot in chest)
			{
				if (slot != null && slot.canStackWith(item))
				{
					int left = slot.addToStack(item);
					if (left <= 0)
					{
						return;
					}
					item.Stack = left;
				}
			}
			if (chest.Count < SquadChestSlots)
			{
				chest.Add(item);
				return;
			}
		}
		if (player.addItemToInventoryBool(item))
		{
			return;
		}
		TryPorch(out Point porch);
		Game1.createItemDebris(item, new Vector2(porch.X * 64 + 32, porch.Y * 64 + 32), -1, Game1.getFarm());
	}

	// ---------------- 记忆和提示词 ----------------

	private void Remember(FarmHelpJob j)
	{
		try
		{
			string paid = j.Price > 0 ? $"农夫付了我 {j.Price}g{(j.Refund > 0 ? $"，没干完的退了 {j.Refund}g" : "")}。" : "";
			_memory()?.AddKeyMemory(j.Npc, $"{Dates.Label(GameState.Today)}，我按说好的去农夫的农场帮忙{FarmHelpRules.TaskLabel(j.Task)}，做完 {j.Progress}/{j.Target}。{paid}", GameState.Today);
		}
		catch (Exception ex)
		{
			_log.Warn($"帮工 {j.Id} 没记成记忆：{ex.Message}");
		}
	}

	public string PromptBlock(string npc) => FarmHelpRules.PromptBlock(All().Where(j => string.Equals(j.Npc, npc, StringComparison.OrdinalIgnoreCase)), Today, CountDone);

	private static void Hud(string text) => Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(text), HUDMessage.newQuest_type));

	private static void Say(NPC npc, string lines)
	{
		if (Game1.player?.currentLocation != npc.currentLocation)
		{
			return;
		}
		string[] options = lines.Split('|');
		NpcSpeech.Say(npc, TextTools.CleanForGame(options[Game1.random.Next(options.Length)]));
	}

	private void MarkDirty() => _store()?.MarkDirty(FarmHelpLog.Path);
}
