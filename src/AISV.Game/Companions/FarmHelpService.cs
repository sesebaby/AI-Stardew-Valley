using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Approach;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Approach;
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
/// 由房主排和跑；联机的农场帮手当面答应的帮工交给房主排，来他家门前、钱和提示在他自己那边（<see cref="CompanionNet"/>）。
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

	public const string ApproachSource = "farmhelp";

	/// <summary>到了先走过来打个招呼（房主自己请的；你在农场上时）。</summary>
	public ApproachCoordinator? Approaches { get; set; }

	/// <summary>(村民, 要说什么) → AI 写那一句。</summary>
	public Func<NPC, string, Func<CancellationToken, Task<string?>>>? Opening { get; set; }

	/// <summary>打过招呼（或不用打）的帮工：可以开工了。</summary>
	private readonly HashSet<string> _greeted = new HashSet<string>();

	/// <summary>正走过来打招呼的帮工。</summary>
	private readonly HashSet<string> _greeting = new HashSet<string>();

	/// <summary>提示过"在门口"的帮工。</summary>
	private readonly HashSet<string> _atDoor = new HashSet<string>();

	public Action<string>? ResolveOffer { get; set; }

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

	private FarmHelpLog? Log => _store()?.Get(FarmHelpLog.Path, FarmHelpLog.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	/// <summary>这边的农夫约的（联机时别的农夫的不算）。</summary>
	public IReadOnlyList<FarmHelpJob> All() => Every().Where(Mine).ToList();

	/// <summary>所有农夫约的。</summary>
	private IReadOnlyList<FarmHelpJob> Every() => Log?.Items.ToList() ?? new List<FarmHelpJob>();

	private static bool Mine(FarmHelpJob j) => j.Farmer == CompanionNet.Me;

	public FarmHelpJob? Find(string id) => Log?.Items.FirstOrDefault(j => j.Id == id);

	public IReadOnlyList<FarmHelpJob> Open() => All().Where(j => FarmHelpStates.IsOpen(j.State)).OrderBy(j => j.Day).ThenBy(j => j.Start).ToList();

	// ---------------- 生命周期 ----------------

	public void OnSaveLoaded() => Version++;

	/// <summary>早上：过去没结的收尾；下雨不用来浇水；提醒今天谁来。日程改写先跑。</summary>
	public void OnDayStarted()
	{
		_greeted.Clear();
		_greeting.Clear();
		_atDoor.Clear();
		if (Log is not FarmHelpLog log)
		{
			return;
		}
		if (!Context.IsMainPlayer)
		{
			// 农场帮手：收尾在房主那边；这边只提醒今天谁来。
			Remind();
			return;
		}
		foreach (FarmHelpJob j in Every().Where(j => FarmHelpStates.IsOpen(j.State) && j.Day < Today))
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
		foreach (FarmHelpJob j in Every().Where(j => j.State == FarmHelpStates.Scheduled && j.Day == Today).OrderBy(j => j.Start))
		{
			if (j.Task == "water" && (Game1.isRaining || Game1.IsRainingHere(Game1.getFarm())))
			{
				Close(j, FarmHelpStates.CalledOff, PlanReasons.Rain, j.Price, Today);
				_schedules.Cancel(j.OverlayId, PlanReasons.Rain);
				Tell(j, Tr.Get("farmhelp.rainOff", "下雨了，地都湿了，{{npc}}今天不用来浇水了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Price));
			}
		}
		Remind();
	}

	private void Remind()
	{
		foreach (FarmHelpJob j in All().Where(j => j.State == FarmHelpStates.Scheduled && j.Day == Today).OrderBy(j => j.Start))
		{
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
		foreach (FarmHelpJob j in Every().Where(j => j.State == FarmHelpStates.Working))
		{
			Finish(j, PlanReasons.DayEnded);
		}
	}

	// ---------------- 当面的提议 ----------------

	/// <summary>Squad 问提议之前先问这里：是约好时间上门的就由这里问，返回 true。</summary>
	public bool TryHandle(PendingOffer p, NPC npc)
	{
		if (Log == null || !Enabled || !Context.IsMainPlayer && !CompanionNet.Remote || !FarmHelpRules.IsFarmVisit(p.Type, p.Task, p.When, p.Time, Game1.timeOfDay, out int offset, out int time))
		{
			return false;
		}
		if (CompanionNet.Remote)
		{
			CompanionNet.Net!.Ask<JobAsk, Answer>(CheckOp, new JobAsk(npc.Name, p.Task, Today + offset, time, p.Price, p.Summary), a => Ask(p, npc, Today + offset, time, a == null ? FarmHelpRules.NoHostReason : a.Ok ? null : a.Reason));
			return true;
		}
		Ask(p, npc, Today + offset, time, Check(npc, p.Task, Today + offset, time));
		return true;
	}

	private void Ask(PendingOffer p, NPC npc, int day, int time, string? why)
	{
		string name = MeetupService.NpcName(npc.Name);
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
			if (answer != "yes")
			{
				return;
			}
			if (!CompanionNet.Remote)
			{
				Hud(PayAndArrange(who, npc.Name, p.Task, day, time, price, p.Summary, out _));
				return;
			}
			if (price > 0 && who.Money < price)
			{
				Hud(Tr.Get("farmhelp.notEnoughMoney", "钱不够：要 {{price}}g。", ("price", price)));
				return;
			}
			CompanionNet.Net!.Ask<JobAsk, Answer>(ArrangeOp, new JobAsk(npc.Name, p.Task, day, time, price, p.Summary), a =>
			{
				if (a?.Ok != true)
				{
					Hud(CantArrangeText(name, a?.Reason ?? FarmHelpRules.NoHostReason));
					return;
				}
				if (price > 0)
				{
					Game1.player.Money -= price;
					Game1.playSound("purchaseClick");
				}
				Hud(Tr.Get("farmhelp.arranged", "说好了：{{when}} {{time}}，{{npc}}来农场帮你{{task}}。", ("when", MeetupService.WhenText(day)), ("time", Clock.Text(time)), ("npc", name), ("task", FarmHelpRules.TaskLabel(p.Task))));
			});
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

	public string? Check(NPC npc, string task, int day, int time, long farmer = 0)
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
		List<FarmHelpJob> open = Every().Where(j => FarmHelpStates.IsOpen(j.State)).ToList();
		if (open.Any(j => j.Day == day && string.Equals(j.Npc, npc.Name, StringComparison.OrdinalIgnoreCase)))
		{
			return FarmHelpRules.ComingReason;
		}
		if (open.Count(j => j.Day == day && j.Farmer == farmer) >= FarmHelpRules.MaxPerDay)
		{
			return FarmHelpRules.FullReason;
		}
		int end = FarmHelpRules.EndOf(task, time);
		string? shop = FarmHelpRules.ShopConflict(npc.Name, day, time, end);
		if (shop != null)
		{
			return shop;
		}
		if (!TryPorch(out Point porch, farmer))
		{
			return FarmHelpRules.NoHouseReason;
		}
		_schedules.Plan(npc.Name, FarmHelpRules.Owner, day, time, end, "Farm", porch.X, porch.Y, 2, out string reason, dryRun: true);
		return reason.Length > 0 ? reason : null;
	}

	public FarmHelpJob? Arrange(string npcName, string task, int day, int time, int price, string summary, out string reason, long farmer = 0)
	{
		reason = "";
		NPC? npc = Game1.getCharacterFromName(npcName);
		if (npc == null || Log is not FarmHelpLog log)
		{
			reason = PlanReasons.NoVillager;
			return null;
		}
		string? why = Check(npc, task, day, time, farmer);
		if (why != null)
		{
			reason = why;
			return null;
		}
		TryPorch(out Point porch, farmer);
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
			CreatedDay = Today,
			Farmer = farmer
		};
		log.Items.Add(j);
		_log.Info($"帮工 {j.Id}：{npc.Name} 第 {day} 天 {time}～{end} 来{task}（{j.Price}g，{o.Id} → 农场 {o.X},{o.Y}）。");
		Version++;
		MarkDirty();
		return j;
	}

	/// <summary>这位农夫家门前旁边一格（不挡门）。</summary>
	private static bool TryPorch(out Point porch, long farmer = 0)
	{
		porch = Point.Zero;
		Farmer? who = farmer == 0 ? Game1.player : CompanionNet.FarmerOf(farmer);
		FarmHouse? house = who == null ? null : Utility.getHomeOfFarmer(who);
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
		foreach (FarmHelpJob j in Every().Where(j => FarmHelpStates.IsOpen(j.State) && j.Day == Today))
		{
			try
			{
				if (j.State == FarmHelpStates.Scheduled)
				{
					if (_schedules.Find(j.OverlayId) is ScheduleOverlay o && o.State == OverlayStates.Arrived && Greeted(j, o))
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
		foreach (FarmHelpJob j in Every().Where(j => j.State == FarmHelpStates.Working))
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
		List<string> targets = FindTargets(j.Task, farm, j.Farmer, out Point center, out int radius);
		if (targets.Count == 0)
		{
			Close(j, FarmHelpStates.NothingToDo, PlanReasons.NothingToDo, j.Price, Today);
			Say(j, npc, Tr.Get("farmhelp.nothingLines", "嗯？看起来没什么要我干的嘛。|地里都弄好了啊，那我先回去了。"));
			Tell(j, Tr.Get("farmhelp.nothing", "{{npc}}来了一看，没什么要干的，就回去了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Price));
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
		// 小队招人是招到房主名下：给农场帮手干的活按进度替他干。
		j.UsedSquad = j.Farmer == 0 && (Crew?.StartSiteWork(npc, j.Task, "Farm", center, radius) ?? false);
		_log.Info($"帮工 {j.Id}：{j.Npc} {Game1.timeOfDay} 开始{j.Task}，{j.Target} 处，中心 ({center.X},{center.Y}) 半径 {radius}，{(j.UsedSquad ? "Squad 当场干" : "按进度算")}。");
		if (!_greeting.Contains(j.Id))
		{
			Say(j, npc, Tr.Get("farmhelp.arriveLines", "我来啦，这就开始干。|早啊，交给我吧。|来了来了，从哪儿开始？"));
		}
		Version++;
		MarkDirty();
		return true;
	}

	/// <summary>
	/// 开工前先打招呼：你在农场上就走过来说一句（AI 写，说今天来干什么）；你在屋里就提示在门口等，等一会儿还没出来先干活；
	/// 你不在农场、联机的农场帮手请的、或者一小时还没碰上，直接开工。
	/// </summary>
	private bool Greeted(FarmHelpJob j, ScheduleOverlay o)
	{
		if (_greeted.Contains(j.Id))
		{
			return true;
		}
		NPC? npc = Game1.getCharacterFromName(j.Npc);
		bool late = o.ArrivedAt >= 0 && Clock.Minutes(Game1.timeOfDay) - Clock.Minutes(o.ArrivedAt) >= CompanyRules.HelperGreetMinutes;
		if (_greeting.Contains(j.Id))
		{
			if (late && Approaches?.IsNpcInvolved(j.Npc) != true)
			{
				_greeted.Add(j.Id);
			}
			return _greeted.Contains(j.Id);
		}
		if (j.Farmer != 0 || Approaches is not ApproachCoordinator ap || npc?.currentLocation is not Farm farm || late)
		{
			_greeted.Add(j.Id);
			return true;
		}
		Farmer me = Game1.player;
		if (me.currentLocation is FarmHouse && me.currentLocation == Utility.getHomeOfFarmer(me))
		{
			if (_atDoor.Add(j.Id))
			{
				Tell(j, Tr.Get("farmhelp.atDoor", "{{npc}}到了，在门口等你。", ("npc", MeetupService.NpcName(j.Npc))));
			}
			return false;
		}
		if (me.currentLocation != farm || !Context.IsPlayerFree)
		{
			_greeted.Add(j.Id);
			return true;
		}
		string id = $"farmhelp:{j.Id}";
		string task = FarmHelpRules.TaskLabel(j.Task);
		EnqueueResult r = ap.Enqueue(new ApproachIntent(ApproachSource, id, j.Npc, id, "Farm", Game1.timeOfDay)
		{
			DistanceTiles = Vector2.Distance(npc.Tile, me.Tile),
			Priority = 40,
			TtlTicks = 3600,
			StartWhilePreparing = true,
			PreparationTimeoutTicks = 600,
			PrepareDialogue = Opening?.Invoke(npc, $"你答应来农夫的农场帮忙干活（{task}），刚到。先走过去跟农夫打个招呼，说说今天打算怎么干，然后就去干活。"),
			FallbackDialogue = Tr.Get("farmhelp.greetLine", "我来啦！今天帮你{{task}}，这就开始。", ("task", task)),
			OnFinished = _ => _greeted.Add(j.Id)
		});
		if (r is EnqueueResult.Accepted or EnqueueResult.Replaced)
		{
			_greeting.Add(j.Id);
			return false;
		}
		_greeted.Add(j.Id);
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
				Say(j, npc, done >= j.Target ? Tr.Get("farmhelp.doneLines", "都弄好了，我回去啦。|活干完了，有空再叫我。|好了！今天就到这儿。") : Tr.Get("farmhelp.partLines", "没全弄完，剩下的不多了。|今天先到这儿吧，我回去了。"));
			}
		}
		if (reason != PlanReasons.DayEnded || done > 0)
		{
			Tell(j, Tr.Get("farmhelp.finished", "{{npc}}帮你{{task}}：做完 {{done}}/{{target}}。", ("npc", MeetupService.NpcName(j.Npc)), ("task", FarmHelpRules.TaskLabel(j.Task)), ("done", done), ("target", j.Target)) + FarmHelpRules.RefundText(j.Refund));
		}
		if (j.Farmer == 0)
		{
			Remember(j);
		}
		_log.Info($"帮工 {j.Id}：{j.Npc} 结束（{reason}），{Game1.timeOfDay}，做完 {done}/{j.Target}（替他干了 {j.RemoteDone}），退 {j.Refund}g / {j.Price}g。");
	}

	/// <summary>你说不用来了：全额退钱；当天才说好感 -10。</summary>
	public bool CancelByPlayer(string id)
	{
		FarmHelpJob? j = Find(id);
		if (j == null || j.State != FarmHelpStates.Scheduled || !Mine(j))
		{
			return false;
		}
		if (CompanionNet.Remote)
		{
			// 房主那边取消、退钱；这边先划掉，等下一次同步。
			CompanionNet.Net!.ToHost(CancelOp, new MeetupService.IdAsk(id));
			j.State = FarmHelpStates.CancelledByPlayer;
			Version++;
			return true;
		}
		CancelFor(j);
		return true;
	}

	private void CancelFor(FarmHelpJob j)
	{
		Close(j, FarmHelpStates.CancelledByPlayer, PlanReasons.FarmerCancelled, j.Price, Today);
		_schedules.Cancel(j.OverlayId, PlanReasons.FarmerCancelled);
		if (j.Day == Today)
		{
			CompanionNet.Send(j.Farmer, new CompanionEffect(j.Npc) { Friendship = FarmHelpRules.SameDayCancelFriendship });
		}
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
		foreach (FarmHelpJob j in Every().Where(j => j.State == FarmHelpStates.Working))
		{
			Finish(j, PlanReasons.Cleared);
		}
		foreach (FarmHelpJob j in Every().Where(j => j.State == FarmHelpStates.Scheduled))
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
		FarmHelpJob? j = Every().FirstOrDefault(x => x.OverlayId == o.Id);
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
			Tell(j, Tr.Get("farmhelp.calledOff", "{{npc}}今天来不了农场了。", ("npc", MeetupService.NpcName(j.Npc))) + FarmHelpRules.RefundText(j.Refund));
		}
	}

	private void Close(FarmHelpJob j, string state, string reason, int refund, int day)
	{
		j.State = state;
		j.Reason = reason;
		j.ClosedDay = Math.Max(day, j.Day);
		j.Refund = Math.Max(0, Math.Min(j.Price, refund));
		if (j.Refund > 0)
		{
			CompanionNet.Send(j.Farmer, new CompanionEffect(j.Npc) { Money = j.Refund });
		}
		_log.Info($"帮工 {j.Id}：{j.Npc} {state}（{reason}），退 {j.Refund}g。");
		Version++;
		MarkDirty();
	}

	// ---------------- 活和进度 ----------------

	private static List<string> FindTargets(string task, Farm farm, long farmer, out Point center, out int radius)
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
				TryPorch(out center, farmer);
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
				if (pair.Value is HoeDirt dirt && (task == "water" ? FarmChoreExecutor.NeedsWater(dirt) : dirt.readyForHarvest()))
				{
					tiles.Add(((int)pair.Key.X, (int)pair.Key.Y));
				}
			}
		}
		List<(int X, int Y)> near = FarmHelpRules.Densest(tiles, radius, out (int X, int Y) c);
		center = new Point(c.X, c.Y);
		return near.Select(t => t.X + "," + t.Y).ToList();
	}

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

	/// <summary>那块地没有农夫就按速度全干；有农夫在农场时只替谁都看不到的地方干，速度减半。</summary>
	private void RemoteWork(FarmHelpJob j)
	{
		Farm farm = Game1.getFarm();
		List<Farmer> onFarm = Game1.getOnlineFarmers().Where(f => f.currentLocation == farm).ToList();
		int budget = FarmHelpRules.RateOf(j.Task);
		if (onFarm.Count > 0 && j.Task != "animals")
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
				FarmChoreExecutor.Pet(a);
				did++;
				continue;
			}
			if (!TryTile(target, out Vector2 tile) || onFarm.Any(f => InView(f, tile)))
			{
				continue;
			}
			if (DoTile(j, farm, tile))
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

	/// <summary>那位农夫的屏幕大概看得到这格（本机用真实视口，别人按一屏 20×13 格估）。</summary>
	private static bool InView(Farmer f, Vector2 tile) => f.IsLocalPlayer ? Utility.isOnScreen(tile * 64f, 64) : Math.Abs(f.Tile.X - tile.X) <= 11 && Math.Abs(f.Tile.Y - tile.Y) <= 8;

	private bool DoTile(FarmHelpJob j, Farm farm, Vector2 tile) => j.Task switch
	{
		"water" => FarmChoreExecutor.WaterTile(farm, tile),
		"harvest" => FarmChoreExecutor.HarvestTile(farm, tile, item => Store(item, j.Farmer)),
		"clear" => FarmChoreExecutor.ClearTile(farm, tile, item => Store(item, j.Farmer)),
		_ => false
	};

	/// <summary>放进小队箱子；满了放背包，再不行放门前。</summary>
	private void Store(Item item, long farmer)
	{
		Farmer player = CompanionNet.FarmerOf(farmer) ?? Game1.player;
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
		// 别的农夫的背包只能他自己那边放：放不进箱子就放在他家门前。
		if (player == Game1.player && player.addItemToInventoryBool(item))
		{
			return;
		}
		TryPorch(out Point porch, farmer);
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

	/// <summary>帮工冒一句（只画在雇他的那位农夫那边，人在旁边才看得见）。</summary>
	private static void Say(FarmHelpJob j, NPC npc, string lines)
	{
		string[] options = lines.Split('|');
		CompanionNet.Send(j.Farmer, new CompanionEffect(npc.Name) { Line = TextTools.CleanForGame(options[Game1.random.Next(options.Length)]) });
	}

	/// <summary>告诉雇他的那位农夫（房主自己的直接弹提示）。</summary>
	private static void Tell(FarmHelpJob j, string text) => CompanionNet.Send(j.Farmer, new CompanionEffect(j.Npc) { Hud = text });

	// ---------------- 联机 ----------------

	public const string CheckOp = "farmhelp.check";

	public const string ArrangeOp = "farmhelp.arrange";

	public const string CancelOp = "farmhelp.cancel";

	public sealed record JobAsk(string Npc, string Task, int Day, int Time, int Price, string Summary);

	public sealed record Answer(bool Ok, string Reason);

	/// <summary>房主替农场帮手查、排、取消（钱在他自己那边付）。</summary>
	public void WireNet(AISV.Game.Net.NetSync net)
	{
		net.On<JobAsk>(CheckOp, (from, a) =>
		{
			string? why = !Context.IsMainPlayer || Game1.getCharacterFromName(a.Npc) is not NPC npc ? PlanReasons.NoVillager : Check(npc, a.Task, a.Day, a.Time, from);
			return new Answer(why == null, why ?? "");
		});
		net.On<JobAsk>(ArrangeOp, (from, a) =>
		{
			if (!Context.IsMainPlayer)
			{
				return new Answer(false, FarmHelpRules.OffReason);
			}
			FarmHelpJob? j = Arrange(a.Npc, a.Task, a.Day, a.Time, a.Price, a.Summary, out string reason, from);
			return new Answer(j != null, reason);
		});
		net.On<MeetupService.IdAsk>(CancelOp, (from, a) =>
		{
			if (Context.IsMainPlayer && Find(a.Id) is FarmHelpJob j && j.Farmer == from && j.State == FarmHelpStates.Scheduled)
			{
				CancelFor(j);
			}
		});
	}

	private void MarkDirty() => _store()?.MarkDirty(FarmHelpLog.Path);
}
