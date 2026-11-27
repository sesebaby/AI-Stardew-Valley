using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using AISV.Game.Store;
using AISV.Game.World;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Inventories;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

namespace AISV.Game.Companions;

/// <summary>
/// 配偶婚后每天自己的安排：早上 6:10 按家里的情况排好（AI 按人设挑和排顺序，规则兜底），
/// 到点去干：你在农场时有个替身走出门干活（进鸡舍、温室就从门口进去），不在时直接后台做完；
/// 也会出门找人聊天、买东西、去小超市看店、带孩子玩。晚上发一份汇总。只在主机上跑。
/// </summary>
public sealed class SpousePlanner
{
	public const string Request = "spouse_day_plan";

	public const string ChoreReason = "spouse_chore";

	/// <summary>替身走过去最多花这么多分钟（游戏时间），超时就后台做完。</summary>
	private const int WalkTimeoutMinutes = 60;

	public const string KidsReason = "spouse_kids";

	public const string OverlayOwner = "spouse_plan";

	public const string BoxPrefix = "AIStardewValley.Core/SpouseBox_";

	private static readonly string[] TownSpots = { "Town", "Saloon", "SeedShop", "Beach", "Mountain", "Forest", "Hospital", "ScienceHouse", "AnimalShop", "Blacksmith", "FishShop", "ArchaeologyHouse", "CommunityCenter" };

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ScheduleOverlayService _schedules;

	private readonly LedgerService _ledger;

	private readonly MartService _mart;

	private readonly Func<EmotionalMemory?> _memory;

	private readonly ILog _log;

	private bool _planning;

	private int _plannedDay = -1;

	/// <summary>正在走出去干活的替身。</summary>
	private Worker? _worker;

	private readonly List<NPC> _kidClones = new List<NPC>();

	private int _kidsUntil = -1;

	private int _mindUntil = -1;

	/// <summary>这份安排是谁家的（0 = 这边的农夫）。联机时房主替每位结了婚的农场帮手各排一份，按他们自己的设置。</summary>
	private readonly long _owner;

	private readonly string _path = SpouseDayLog.Path;

	private readonly Dictionary<long, SpousePlanner> _farmhands = new Dictionary<long, SpousePlanner>();

	/// <summary>农场帮手发来的设置（只有替农场帮手排的那几份有）。</summary>
	private ModConfig? _prefs;

	private bool _pushLog;

	private bool _prefsSent;

	private static readonly List<SpousePlanner> Planners = new List<SpousePlanner>();

	private sealed class Worker
	{
		public NPC Clone = null!;
		public NPC Real = null!;
		public SpousePlanItem Item = null!;
		public bool Arrived;
		public bool Inside;
		public bool Leaving;
		public int Rounds;
		public int Started;
	}

	public SpousePlanner(Func<ModConfig> config, Func<DocumentStore?> store, ScheduleOverlayService schedules, LedgerService ledger, MartService mart, Func<EmotionalMemory?> memory, ILog log)
	{
		_config = config;
		_store = store;
		_schedules = schedules;
		_ledger = ledger;
		_mart = mart;
		_memory = memory;
		_log = log;
		Planners.Add(this);
		NpcBusy.Register(ChoreReason, n => Planners.Any(p => p._worker?.Real.Name == n));
	}

	/// <summary>房主替一位农场帮手排的那份。</summary>
	private SpousePlanner(SpousePlanner root, long owner, ModConfig prefs) : this(root._config, root._store, root._schedules, root._ledger, root._mart, () => null, root._log)
	{
		_owner = owner;
		_path = $"family/spouse_day_{owner}.json";
		_prefs = prefs;
		Ai = root.Ai;
		Language = root.Language;
		Display = root.Display;
		Persona = root.Persona;
		Net = root.Net;
	}

	public AISV.Game.Net.NetSync? Net { get; set; }

	/// <summary>这份安排的主人（不在线时为 null）。</summary>
	private Farmer? Owner => _owner == 0 ? Game1.player : Game1.GetPlayer(_owner, onlyOnline: true);

	/// <summary>按谁的设置排：这边的，或者农场帮手发来的。</summary>
	private ModConfig Cfg => _prefs ?? _config();

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	/// <summary>配偶还在生你的气（调情被看见）：不帮忙干农活。</summary>
	public Func<string, bool>? Cold { get; set; }

	public Func<string, string>? Display { get; set; }

	public Func<string, string>? Persona { get; set; }

	public ChildBook? KidBook { get; set; }

	public int Version { get; private set; }

	public bool Minding => _mindUntil > 0;

	public SpouseDayLog? Log => _store()?.Get(_path, SpouseDayLog.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public bool Enabled => Cfg.SpousePlanner && Context.IsMainPlayer && Spouse() != null;

	/// <summary>手机上看得到今天的安排：房主自己排，或者连着房主的农场帮手（房主替他排好发过来）。</summary>
	public bool Shown => Enabled || _owner == 0 && Net?.Linked == true && _config().SpousePlanner && Spouse() != null;

	/// <summary>玩家的配偶（室友看设置）；没结婚为 null。</summary>
	public NPC? Spouse()
	{
		Farmer? p = Owner;
		if (p?.spouse == null || !p.friendshipData.TryGetValue(p.spouse, out Friendship f) || !(f.IsMarried() || f.IsRoommate()))
		{
			return null;
		}
		if (f.IsRoommate() && !Cfg.RoommatePlans)
		{
			return null;
		}
		return Game1.getCharacterFromName(p.spouse);
	}

	private string Name(NPC n) => Display?.Invoke(n.Name) ?? n.displayName;

	// ---------------- 每天 ----------------

	public void OnDayStarted()
	{
		_plannedDay = -1;
		_prefsSent = false;
		if (_owner == 0)
		{
			foreach (SpousePlanner other in Farmhands())
			{
				other.OnDayStarted();
			}
		}
		if (Log is not SpouseDayLog log)
		{
			return;
		}
		log.Yesterday = log.Day == Today - 1 ? log.Summary() : "";
		log.Day = Today;
		log.Items.Clear();
		log.Say = "";
		log.Announced = false;
		log.Summarized = false;
		log.Spouse = Spouse()?.Name ?? "";
		MarkDirty();
	}

	public void OnTimeChanged()
	{
		if (_owner == 0)
		{
			if (!_prefsSent)
			{
				SendPrefs();
			}
			foreach (SpousePlanner other in Farmhands())
			{
				other.OnTimeChanged();
			}
		}
		if (!Enabled || Log is not SpouseDayLog log || Spouse() is not NPC spouse)
		{
			return;
		}
		int time = Game1.timeOfDay;
		if (_plannedDay != Today && !_planning && time >= 610 && log.Day == Today)
		{
			Plan(spouse, log);
			return;
		}
		if (_plannedDay != Today || Game1.eventUp || Game1.isFestival())
		{
			return;
		}
		foreach (SpousePlanItem item in log.Items.ToList())
		{
			if (item.State == SpousePlanStates.Pending && item.Time <= time)
			{
				Start(spouse, item, time);
			}
			else if (item.State == SpousePlanStates.Working)
			{
				Progress(spouse, item, time);
			}
		}
		if (!log.Summarized && time >= 1900)
		{
			Summarize(spouse, log);
		}
	}

	public void OnUpdateTicked()
	{
		if (_owner == 0 && _farmhands.Count > 0 && Game1.ticks % 30 == 0)
		{
			foreach (SpousePlanner other in Farmhands())
			{
				other.OnUpdateTicked();
				if (other._pushLog && other._store()?.Export(other._path) is string json)
				{
					other._pushLog = false;
					Net?.ToFarmer(other._owner, LogOp, json);
				}
			}
		}
		if (_worker is not Worker w)
		{
			return;
		}
		if (w.Clone.controller == null && !w.Arrived && !w.Inside)
		{
			// 寻路断了就地开始干。
			w.Arrived = true;
		}
		if (w.Leaving && w.Clone.controller == null)
		{
			EndWorker();
		}
	}

	public void Cleanup()
	{
		foreach (SpousePlanner other in _farmhands.Values)
		{
			other.Cleanup();
		}
		EndWorker();
		EndKids();
		if (_mindUntil > 0)
		{
			_mart.SpouseShift(null, false);
			_mindUntil = -1;
		}
	}

	// ---------------- 排计划 ----------------

	private SpousePlanInput Input(NPC spouse, SpouseDayLog log)
	{
		ModConfig c = Cfg;
		HashSet<string> allowed = new HashSet<string>();
		void Allow(bool on, params string[] tasks)
		{
			if (on)
			{
				allowed.UnionWith(tasks);
			}
		}
		Allow(c.SpouseWaters, SpouseTasks.Water);
		Allow(c.SpouseHarvests, SpouseTasks.Harvest);
		Allow(c.SpouseTendsAnimals, SpouseTasks.Animals, SpouseTasks.Produce, SpouseTasks.Hay);
		Allow(c.SpouseClears, SpouseTasks.Clear);
		Allow(c.SpouseGreenhouse, SpouseTasks.Greenhouse);
		Allow(c.SpouseFishPonds, SpouseTasks.Ponds);
		Allow(c.SpouseMachines, SpouseTasks.Machines);
		Allow(c.SpouseGoesOut, SpouseTasks.Visit, SpouseTasks.Shop);
		// 看店、陪孩子只排给房主自己的配偶（小超市只有房主有，孩子的事在房主这边）。
		Allow(c.SpouseMindsShop && _owner == 0, SpouseTasks.MindShop);
		Allow(c.SpousePlaysWithKids && _owner == 0, SpouseTasks.Kids);
		if (Cold?.Invoke(spouse.Name) == true)
		{
			// 还在生你的气：这几天不帮忙干农活。
			allowed.ExceptWith(SpouseTasks.Chores);
		}
		Dictionary<string, int> work = SpouseTasks.Chores.ToDictionary(t => t, FarmChoreExecutor.Count);
		Farmer p = Owner!;
		return new SpousePlanInput
		{
			Hearts = p.getFriendshipHeartLevelForNPC(spouse.Name),
			Roommate = p.friendshipData.TryGetValue(spouse.Name, out Friendship f) && f.IsRoommate(),
			Level = c.SpouseHelpLevel switch { "Light" => 0, "Busy" => 2, _ => 1 },
			Rain = Game1.IsRainingHere(Game1.getFarm()),
			Festival = Utility.isFestivalDay(),
			Winter = Game1.IsWinter,
			PatioDay = spouse.shouldPlaySpousePatioAnimation.Value,
			Work = work,
			Allowed = allowed,
			HasKids = _owner == 0 && Kids.All().Any(),
			HasShop = _owner == 0 && _mart.HasShop && _config().FarmerMart,
			Gold = _ledger.Wallet(spouse.Name)?.Gold ?? 0,
			SpendCap = c.SpouseSpendDaily,
			Requested = log.Requests.ToList()
		};
	}

	private void Plan(NPC spouse, SpouseDayLog log)
	{
		SpousePlanInput input = Input(spouse, log);
		log.Requests.Clear();
		List<(string Task, double Score)> cands = SpouseDayPlan.Candidates(input);
		if (Ai == null || cands.Count <= 1 || SpouseDayPlan.MaxItems(input.Level, input.Hearts, input.Upset) == 0)
		{
			Finish(spouse, log, SpouseDayPlan.Build(input), "");
			return;
		}
		_planning = true;
		string system = Prompt(spouse, input, cands);
		IAiSender ai = Ai;
		Task.Run(async () =>
		{
			(List<string> Order, string Say)? parsed = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(Request, system, "排今天的安排。", 400, 0.8)).ConfigureAwait(false);
				parsed = r.Ok ? SpouseDayPlan.ParseAi(r.Text, cands.Select(x => x.Task)) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"配偶日程没让 AI 排：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				_planning = false;
				if (Log is SpouseDayLog now && now.Day == Today && _plannedDay != Today)
				{
					Finish(spouse, now, SpouseDayPlan.Build(input, parsed?.Order), TextTools.CleanForGame(parsed?.Say ?? ""));
				}
			});
		});
	}

	private string Prompt(NPC spouse, SpousePlanInput input, List<(string Task, double Score)> cands)
	{
		string farmer = Owner?.Name ?? Game1.player.Name;
		string persona = Persona?.Invoke(spouse.Name) ?? "";
		string role = input.Roommate ? "室友" : "配偶";
		string weather = input.Rain ? "下雨" : "晴";
		string list = string.Join("\n", cands.Select(c => $"- {c.Task}：{SpouseTasks.Label(c.Task)}{(input.Work.TryGetValue(c.Task, out int n) && n > 0 ? $"（有 {n} 处）" : "")}"));
		return $"你是星露谷的{spouse.displayName}，{farmer}的{role}。{persona}\n今天{Dates.Label(GameState.Today)}，{weather}，你对{farmer}有 {input.Hearts} 心。\n下面是今天你能做的事，按你的性格和心情挑最多 {SpouseDayPlan.MaxItems(input.Level, input.Hearts, input.Upset)} 件、按想做的先后排：\n{list}\n只输出 JSON：{{\"order\":[\"任务代号\",...],\"say\":\"早上对{farmer}说的一句话，交代今天的安排，30 字以内，用{Language?.Invoke() ?? "中文"}\"}}";
	}

	private void Finish(NPC spouse, SpouseDayLog log, List<SpousePlanItem> items, string say)
	{
		_plannedDay = Today;
		log.Items = items;
		log.Say = say;
		MarkDirty();
		Version++;
		_log.Info($"配偶日程：{spouse.Name} {SpouseDayPlan.Describe(items)}");
		if (items.Count == 0)
		{
			return;
		}
		string plan = string.Join(Tr.Get("list.sep", "、"), items.Select(i => $"{Clock.Text(i.Time)} {TaskName(i.Task)}"));
		Tell(say.Length > 0
			? Tr.Get("spouse.plan.said", "{{npc}}：“{{say}}”（{{plan}}）", ("npc", Name(spouse)), ("say", say), ("plan", plan))
			: Tr.Get("spouse.plan.hud", "{{npc}}今天的安排：{{plan}}", ("npc", Name(spouse)), ("plan", plan)), 9000);
		log.Announced = true;
		if (_owner == 0)
		{
			Hints.Tip("SpousePlanHint", Tr.Get("help.spouse.title", "配偶的日常安排"));
		}
	}

	/// <summary>玩家要配偶帮忙做一件事：今天还来得及就排进今天，不然明天一早做。</summary>
	public bool RequestTask(string task, out bool today)
	{
		today = false;
		if (!Enabled || Log is not SpouseDayLog log || !SpouseTasks.All.Contains(task))
		{
			return false;
		}
		int time = Game1.timeOfDay;
		if (_plannedDay == Today && time < SpouseDayPlan.LastSlot && !log.Items.Any(i => i.Task == task && i.State is SpousePlanStates.Pending or SpousePlanStates.Working))
		{
			log.Items.Add(new SpousePlanItem
			{
				Task = task,
				Time = Clock.Add(time - time % 10, 10),
				Amount = SpouseTasks.IsChore(task) ? SpouseTasks.Cap(task, 2) : task == SpouseTasks.Shop ? Cfg.SpouseSpendDaily : 1,
				Requested = true
			});
			today = true;
		}
		else if (!log.Requests.Contains(task))
		{
			log.Requests.Add(task);
		}
		MarkDirty();
		Version++;
		return true;
	}

	/// <summary>聊天里说好的帮忙（帮我浇水、喂动物……）：配偶不收钱，直接排进安排。</summary>
	public bool TryRoute(NPC npc, string task)
	{
		if (Spouse() is not NPC spouse || spouse.Name != npc.Name || !Shown)
		{
			return false;
		}
		string mapped = task switch
		{
			"water" => SpouseTasks.Water,
			"harvest" => SpouseTasks.Harvest,
			"animals" => SpouseTasks.Animals,
			"clear" => SpouseTasks.Clear,
			_ => ""
		};
		if (mapped.Length == 0)
		{
			return false;
		}
		if (!Enabled)
		{
			// 农场帮手：交给房主排，房主排好了再告诉你。
			Net?.ToHost(AskOp, new AskTask(mapped));
			return true;
		}
		if (!RequestTask(mapped, out bool today))
		{
			return false;
		}
		Tell(today
			? Tr.Get("spouse.plan.okToday", "{{npc}}答应今天就帮你{{task}}。", ("npc", Name(spouse)), ("task", TaskName(mapped)))
			: Tr.Get("spouse.plan.okTomorrow", "{{npc}}答应明天一早帮你{{task}}。", ("npc", Name(spouse)), ("task", TaskName(mapped))), 6000);
		return true;
	}

	public void Toggle(string task, bool on)
	{
		ModConfig c = _config();
		switch (task)
		{
			case SpouseTasks.Water: c.SpouseWaters = on; break;
			case SpouseTasks.Harvest: c.SpouseHarvests = on; break;
			case SpouseTasks.Animals: c.SpouseTendsAnimals = on; break;
			case SpouseTasks.Clear: c.SpouseClears = on; break;
			case SpouseTasks.Greenhouse: c.SpouseGreenhouse = on; break;
			case SpouseTasks.Ponds: c.SpouseFishPonds = on; break;
			case SpouseTasks.Machines: c.SpouseMachines = on; break;
			case SpouseTasks.Visit: c.SpouseGoesOut = on; break;
			case SpouseTasks.MindShop: c.SpouseMindsShop = on; break;
			case SpouseTasks.Kids: c.SpousePlaysWithKids = on; break;
		}
		if (!on && Log is SpouseDayLog log)
		{
			foreach (SpousePlanItem i in log.Items.Where(i => i.Task == task && i.State == SpousePlanStates.Pending))
			{
				i.State = SpousePlanStates.Skipped;
			}
			MarkDirty();
		}
		Version++;
		SendPrefs();
	}

	public bool IsOn(string task)
	{
		ModConfig c = _config();
		return task switch
		{
			SpouseTasks.Water => c.SpouseWaters,
			SpouseTasks.Harvest => c.SpouseHarvests,
			SpouseTasks.Animals or SpouseTasks.Produce or SpouseTasks.Hay => c.SpouseTendsAnimals,
			SpouseTasks.Clear => c.SpouseClears,
			SpouseTasks.Greenhouse => c.SpouseGreenhouse,
			SpouseTasks.Ponds => c.SpouseFishPonds,
			SpouseTasks.Machines => c.SpouseMachines,
			SpouseTasks.Visit or SpouseTasks.Shop => c.SpouseGoesOut,
			SpouseTasks.MindShop => c.SpouseMindsShop,
			SpouseTasks.Kids => c.SpousePlaysWithKids,
			_ => false
		};
	}

	public static string TaskName(string t) => t switch
	{
		SpouseTasks.Water => Tr.Get("spouse.task.water", "浇水"),
		SpouseTasks.Harvest => Tr.Get("spouse.task.harvest", "收菜"),
		SpouseTasks.Animals => Tr.Get("spouse.task.animals", "摸摸动物"),
		SpouseTasks.Produce => Tr.Get("spouse.task.produce", "捡蛋和动物产品"),
		SpouseTasks.Hay => Tr.Get("spouse.task.hay", "添干草"),
		SpouseTasks.Clear => Tr.Get("spouse.task.clear", "清理杂草石头"),
		SpouseTasks.Greenhouse => Tr.Get("spouse.task.greenhouse", "打理温室"),
		SpouseTasks.Ponds => Tr.Get("spouse.task.ponds", "收鱼塘"),
		SpouseTasks.Machines => Tr.Get("spouse.task.machines", "收加工机器"),
		SpouseTasks.Visit => Tr.Get("spouse.task.visit", "去镇上找人聊天"),
		SpouseTasks.Shop => Tr.Get("spouse.task.shop", "去镇上买东西"),
		SpouseTasks.MindShop => Tr.Get("spouse.task.mindShop", "去小超市看店"),
		SpouseTasks.Kids => Tr.Get("spouse.task.kids", "带孩子玩"),
		_ => t
	};

	// ---------------- 干活 ----------------

	private void Start(NPC spouse, SpousePlanItem item, int time)
	{
		if (NpcBusy.IsBySources(spouse.Name) || spouse.IsInvisible)
		{
			// 在忙别的（约会、剧情、理论……），过半小时再看。
			item.Time = Clock.Add(time, 30);
			if (item.Time > 2000)
			{
				item.State = SpousePlanStates.Skipped;
			}
			MarkDirty();
			return;
		}
		item.State = SpousePlanStates.Working;
		MarkDirty();
		Version++;
		switch (item.Task)
		{
			case SpouseTasks.Visit:
				StartVisit(spouse, item, time);
				return;
			case SpouseTasks.Shop:
				StartShopping(spouse, item, time);
				return;
			case SpouseTasks.MindShop:
				StartMinding(spouse, item, time);
				return;
			case SpouseTasks.Kids:
				StartKids(spouse, item, time);
				return;
		}
		if (_worker == null && CanShowWorker(spouse) && WorkSpot(item.Task) is (GameLocation where, Point spot, bool door) && where is Farm farm)
		{
			Point from = farm.GetMainFarmHouseEntry();
			NPC clone = MartService.Clone(spouse, farm, from, ChoreReason);
			clone.controller = new PathFindController(clone, farm, spot, 2, (c, l) => { if (_worker != null) _worker.Arrived = true; });
			if (clone.controller.pathToEndPoint != null)
			{
				spouse.IsInvisible = true;
				_worker = new Worker { Clone = clone, Real = spouse, Item = item, Inside = false, Started = time };
				_worker.Leaving = false;
				_doorTarget = door;
				return;
			}
			farm.characters.Remove(clone);
		}
		// 你看不到的地方：一口气做完。
		item.Did += FarmChoreExecutor.Do(item.Task, item.Amount, Store);
		Done(spouse, item);
	}

	private bool _doorTarget;

	/// <summary>只有你在农场上、配偶不在你眼前时才派替身走出去（免得配偶在你面前凭空消失）。</summary>
	private bool CanShowWorker(NPC spouse) => _owner == 0 && Game1.currentLocation is Farm && spouse.currentLocation != Game1.currentLocation && Game1.activeClickableMenu == null;

	/// <summary>去哪干：地里就走到最近一块要干的地边上；要进屋的走到那栋建筑的门口。</summary>
	private static (GameLocation, Point, bool)? WorkSpot(string task)
	{
		Farm farm = Game1.getFarm();
		switch (task)
		{
			case SpouseTasks.Water:
			case SpouseTasks.Harvest:
			{
				Func<StardewValley.TerrainFeatures.HoeDirt, bool> want = task == SpouseTasks.Water ? FarmChoreExecutor.NeedsWater : d => d.readyForHarvest();
				Vector2? t = FarmChoreExecutor.Dirt(farm, want).Cast<Vector2?>().FirstOrDefault();
				return t is Vector2 v ? (farm, new Point((int)v.X, (int)v.Y + 1), false) : null;
			}
			case SpouseTasks.Clear:
			{
				Vector2? t = farm.objects.Pairs.Where(p => FarmChoreExecutor.IsDebris(p.Value)).Select(p => (Vector2?)p.Key).FirstOrDefault();
				return t is Vector2 v ? (farm, new Point((int)v.X, (int)v.Y + 1), false) : null;
			}
			case SpouseTasks.Animals:
			case SpouseTasks.Produce:
			case SpouseTasks.Hay:
			{
				Building? b = farm.buildings.FirstOrDefault(x => x.GetIndoors() is AnimalHouse);
				return b == null ? null : (farm, b.getPointForHumanDoor() + new Point(0, 1), true);
			}
			case SpouseTasks.Greenhouse:
			{
				Building? b = farm.buildings.FirstOrDefault(x => x.buildingType.Value == "Greenhouse");
				return b == null ? null : (farm, b.getPointForHumanDoor() + new Point(0, 1), true);
			}
			case SpouseTasks.Ponds:
			{
				FishPond? p = farm.buildings.OfType<FishPond>().FirstOrDefault(x => x.output.Value != null);
				return p == null ? null : (farm, new Point(p.tileX.Value + 1, p.tileY.Value + p.tilesHigh.Value), false);
			}
		}
		return null;
	}

	private void Progress(NPC spouse, SpousePlanItem item, int time)
	{
		switch (item.Task)
		{
			case SpouseTasks.Visit:
			case SpouseTasks.Shop:
				ProgressOuting(spouse, item, time);
				return;
			case SpouseTasks.MindShop:
				if (time >= _mindUntil || !_mart.IsOpen(time))
				{
					EndMinding(spouse, item);
				}
				return;
			case SpouseTasks.Kids:
				if (time >= _kidsUntil)
				{
					EndKids();
					Done(spouse, item);
				}
				return;
		}
		if (_worker is not Worker w || w.Item != item)
		{
			// 替身被收回了（换地图、存档）：剩下的后台做完。
			item.Did += FarmChoreExecutor.Do(item.Task, Math.Max(0, item.Amount - item.Did), Store);
			Done(spouse, item);
			return;
		}
		if (!w.Arrived && !w.Leaving && Utility.CalculateMinutesBetweenTimes(w.Started, time) >= WalkTimeoutMinutes)
		{
			// 走了一小时还没到（被挡住、路断了）：收回替身，剩下的后台做完。
			_log.Debug($"配偶日程：{spouse.Name} 去干 {item.Task} 的路上耽搁太久，改成后台做完。");
			EndWorker();
			return;
		}
		if (!w.Arrived || w.Leaving)
		{
			return;
		}
		if (_doorTarget && !w.Inside)
		{
			// 进屋干活：替身从门口进去。
			w.Inside = true;
			w.Clone.IsInvisible = true;
		}
		int chunk = Math.Max(1, (item.Amount + 2) / 3);
		int did = FarmChoreExecutor.Do(item.Task, Math.Min(chunk, item.Amount - item.Did), Store, (l, t) => l == Game1.currentLocation && Utility.isOnScreen(t * 64f, 64) && Vector2.Distance(t, w.Clone.Tile) > 3);
		item.Did += did;
		w.Rounds++;
		if (!w.Inside && did > 0 && Game1.random.NextDouble() < 0.4)
		{
			w.Clone.doEmote(Game1.random.NextDouble() < 0.5 ? 32 : 20);
		}
		MarkDirty();
		if (item.Did >= item.Amount || did == 0 || w.Rounds >= 4)
		{
			// 干完了，走回屋里。
			w.Clone.IsInvisible = false;
			w.Inside = false;
			w.Leaving = true;
			Farm farm = Game1.getFarm();
			w.Clone.controller = new PathFindController(w.Clone, farm, farm.GetMainFarmHouseEntry(), 0, (c, l) => { });
			if (w.Clone.controller.pathToEndPoint == null)
			{
				w.Clone.controller = null;
			}
			Done(spouse, item);
		}
		else if (!w.Inside && WorkSpot(item.Task) is (GameLocation _, Point next, bool _) && Vector2.Distance(next.ToVector2(), w.Clone.Tile) > 2)
		{
			w.Arrived = false;
			w.Clone.controller = new PathFindController(w.Clone, w.Clone.currentLocation, next, 2, (c, l) => { if (_worker != null) _worker.Arrived = true; });
		}
	}

	private void EndWorker()
	{
		if (_worker is not Worker w)
		{
			return;
		}
		w.Clone.currentLocation?.characters.Remove(w.Clone);
		w.Real.IsInvisible = false;
		_worker = null;
		if (w.Item.State == SpousePlanStates.Working)
		{
			w.Item.Did += FarmChoreExecutor.Do(w.Item.Task, Math.Max(0, w.Item.Amount - w.Item.Did), Store);
			if (Spouse() is NPC s)
			{
				Done(s, w.Item);
			}
		}
	}

	private void Done(NPC spouse, SpousePlanItem item, string note = "")
	{
		item.State = SpousePlanStates.Done;
		if (note.Length > 0)
		{
			item.Note = note;
		}
		MarkDirty();
		Version++;
		if (SpouseTasks.IsChore(item.Task) && item.Did > 0 && Owner?.currentLocation == spouse.currentLocation && !spouse.IsInvisible)
		{
			Say(spouse, Tr.Get("spouse.plan.bubbleDone", "{{task}}弄好啦～", ("task", TaskName(item.Task))));
		}
		_log.Debug($"配偶日程：{spouse.Name} {item.Task} 做完（{item.Did}/{item.Amount}）{note}");
	}

	// ---------------- 出门 ----------------

	private void StartVisit(NPC spouse, SpousePlanItem item, int time)
	{
		List<NPC> friends = Utility.getAllCharacters()
			.Where(n => n.IsVillager && n.Name != spouse.Name && !Kids.IsChild(n) && !n.IsInvisible && !NpcBusy.Is(n.Name) && n.currentLocation != null && TownSpots.Contains(n.currentLocation.Name))
			.ToList();
		if (friends.Count == 0)
		{
			Done(spouse, item, Tr.Get("spouse.plan.noFriend", "镇上没碰到人，逛了一圈就回来了"));
			return;
		}
		NPC friend = friends[Game1.random.Next(friends.Count)];
		item.Note = friend.Name;
		Point at = friend.TilePoint + new Point(1, 0);
		ScheduleOverlay? o = _schedules.Plan(spouse.Name, OverlayOwner, Today, Clock.Add(time, 10), Clock.Add(time, SpouseTasks.Minutes(item.Task)), friend.currentLocation.Name, at.X, at.Y, 3, out string reason);
		if (o == null)
		{
			_log.Debug($"配偶日程：{spouse.Name} 去找 {friend.Name} 没排上（{reason}），后台算聊过了。");
			Chatted(spouse, friend, item);
			return;
		}
		_overlay = o.Id;
	}

	private string _overlay = "";

	private void StartShopping(NPC spouse, SpousePlanItem item, int time)
	{
		ScheduleOverlay? o = _mart.AllowShop("SeedShop") && Game1.Date.DayOfWeek != DayOfWeek.Wednesday
			? _schedules.Plan(spouse.Name, OverlayOwner, Today, Clock.Add(time, 10), Clock.Add(time, SpouseTasks.Minutes(item.Task)), "Town", 43, 59, 0, out _)
			: null;
		if (o == null)
		{
			Bought(spouse, item);
			return;
		}
		_overlay = o.Id;
	}

	private void ProgressOuting(NPC spouse, SpousePlanItem item, int time)
	{
		ScheduleOverlay? o = _overlay.Length > 0 ? _schedules.Find(_overlay) : null;
		bool arrived = o != null && o.State == OverlayStates.Arrived;
		bool over = o == null || !OverlayStates.IsOpen(o.State) || time >= Clock.Add(item.Time, SpouseTasks.Minutes(item.Task) + 60);
		if (!arrived && !over)
		{
			return;
		}
		_overlay = "";
		if (item.Task == SpouseTasks.Visit)
		{
			Chatted(spouse, Game1.getCharacterFromName(item.Note), item);
		}
		else
		{
			Bought(spouse, item);
		}
	}

	private void Chatted(NPC spouse, NPC? friend, SpousePlanItem item)
	{
		string who = friend != null ? Name(friend) : item.Note;
		if (friend != null && Owner?.currentLocation == spouse.currentLocation && spouse.currentLocation == friend.currentLocation)
		{
			spouse.doEmote(32);
			friend.doEmote(20);
			Say(spouse, Tr.Get("spouse.plan.chatBubble", "{{friend}}，最近怎么样？", ("friend", who)));
		}
		try
		{
			_memory()?.AddKeyMemory(spouse.Name, $"{Dates.Label(GameState.Today)}，我去镇上找{who}聊了会儿天。", GameState.Today);
			if (friend != null)
			{
				_memory()?.AddKeyMemory(friend.Name, $"{Dates.Label(GameState.Today)}，{spouse.displayName}来找我聊了会儿天。", GameState.Today);
			}
		}
		catch (Exception ex)
		{
			_log.Debug($"配偶聊天没记成记忆：{ex.Message}");
		}
		Done(spouse, item, Tr.Get("spouse.plan.chatted", "和{{friend}}聊了会儿", ("friend", who)));
	}

	private void Bought(NPC spouse, SpousePlanItem item)
	{
		string? what = _ledger.ShopFor(spouse, "SeedShop", Math.Max(30, item.Amount));
		Done(spouse, item, what ?? Tr.Get("spouse.plan.boughtNothing", "逛了逛，没买东西"));
	}

	// ---------------- 看店 ----------------

	private void StartMinding(NPC spouse, SpousePlanItem item, int time)
	{
		if (!_mart.HasShop || !_mart.IsOpen(Math.Max(time, _mart.OpenAt)) || !_mart.SpouseShift(spouse, true))
		{
			Done(spouse, item, Tr.Get("spouse.plan.shopClosed", "店没开，改天再去"));
			return;
		}
		_mindUntil = Math.Min(_mart.CloseAt, Clock.Add(time, SpouseTasks.Minutes(item.Task)));
		item.Did = _mart.Sales.On(Today, StoreSale.Ours).Count();
		if (SpouseWork(spouse.Name) is (string id, int qty) && _mart.StockSpouseWork(spouse.Name, id, qty))
		{
			item.Note = LedgerService.ItemName(id);
		}
	}

	private void EndMinding(NPC spouse, SpousePlanItem item)
	{
		_mart.SpouseShift(spouse, false);
		_mindUntil = -1;
		int sold = _mart.Sales.On(Today, StoreSale.Ours).Count() - item.Did;
		item.Did = Math.Max(0, sold);
		string note = item.Note.Length > 0
			? Tr.Get("spouse.plan.mindedWithWork", "看店卖出 {{n}} 笔，还摆了自己做的{{item}}", ("n", item.Did), ("item", item.Note))
			: Tr.Get("spouse.plan.minded", "看店卖出 {{n}} 笔", ("n", item.Did));
		Done(spouse, item, note);
	}

	/// <summary>配偶自己会做、拿到店里卖的东西（按人设）。</summary>
	private static (string, int)? SpouseWork(string npc) => npc switch
	{
		"Emily" => ("(O)428", 2),
		"Leah" => ("(O)196", 3),
		"Penny" => ("(O)223", 3),
		"Haley" => ("(O)421", 4),
		"Abigail" => ("(O)66", 1),
		"Maru" => ("(O)787", 1),
		"Elliott" => ("(O)797", 1),
		"Sebastian" => ("(O)84", 1),
		"Sam" => ("(O)206", 2),
		"Alex" => ("(O)201", 2),
		"Shane" => ("(O)215", 3),
		"Harvey" => ("(O)395", 4),
		"Krobus" => ("(O)769", 3),
		_ => null
	};

	// ---------------- 带孩子 ----------------

	private void StartKids(NPC spouse, SpousePlanItem item, int time)
	{
		List<Child> kids = Kids.All().ToList();
		if (kids.Count == 0)
		{
			Done(spouse, item);
			return;
		}
		_kidsUntil = Clock.Add(time, SpouseTasks.Minutes(item.Task));
		foreach (Child c in kids)
		{
			KidBook?.Note(Kids.Key(c), Today, Tr.Get("spouse.plan.kidNote", "今天{{parent}}陪我在院子里玩了好久", ("parent", spouse.displayName)));
		}
		Farm farm = Game1.getFarm();
		TeensHelp(spouse, kids, farm);
		if (!CanShowWorker(spouse) || Game1.isRaining || kids.Any(k => k.IsInvisible))
		{
			return;
		}
		Point door = farm.GetMainFarmHouseEntry();
		NPC parent = MartService.Clone(spouse, farm, door + new Point(2, 3), KidsReason);
		parent.walkInSquare(4, 3, 3000);
		_kidClones.Add(parent);
		spouse.IsInvisible = true;
		NpcBusy.Hold(spouse.Name, KidsReason);
		int i = 0;
		foreach (Child c in kids.Where(k => k.Age >= 3))
		{
			NPC kid = MartService.Clone(c, farm, door + new Point(3 + i++, 4), KidsReason);
			kid.walkInSquare(3, 3, 2000);
			kid.Speed = 3;
			_kidClones.Add(kid);
			c.IsInvisible = true;
		}
		Hints.Hud(Tr.Get("spouse.plan.kidsOut", "{{npc}}带着孩子在院子里玩。", ("npc", Name(spouse))), 5000);
	}

	/// <summary>少年在家：跟着配偶浇几块地（每人最多 8 块）。</summary>
	private void TeensHelp(NPC spouse, List<Child> kids, Farm farm)
	{
		List<Child> teens = kids.Where(k => KidGrowthService.Stage(k) == AISV.Core.Family.KidStages.Teen && !k.IsInvisible).ToList();
		if (teens.Count == 0 || Game1.isRaining)
		{
			return;
		}
		int done = 0;
		foreach (KeyValuePair<Vector2, StardewValley.TerrainFeatures.TerrainFeature> pair in farm.terrainFeatures.Pairs.ToList())
		{
			if (done >= 8 * teens.Count)
			{
				break;
			}
			if (pair.Value is StardewValley.TerrainFeatures.HoeDirt dirt && FarmChoreExecutor.NeedsWater(dirt) && FarmChoreExecutor.WaterTile(farm, pair.Key))
			{
				done++;
			}
		}
		if (done == 0)
		{
			return;
		}
		string names = string.Join(Tr.Get("kids.traitJoin", "、"), teens.Select(t => t.displayName));
		foreach (Child t in teens)
		{
			KidBook?.Note(Kids.Key(t), Today, Tr.Get("spouse.plan.teenNote", "今天和{{parent}}一起在地里浇了水", ("parent", spouse.displayName)));
		}
		Hints.Hud(Tr.Get("spouse.plan.teensHelp", "{{kids}}帮{{npc}}浇了 {{count}} 块地。", ("kids", names), ("npc", Name(spouse)), ("count", done)), 5000);
		_log.Info($"配偶 {spouse.Name} 带着少年 {names} 浇了 {done} 块地。");
	}

	private void EndKids()
	{
		_kidsUntil = -1;
		if (_kidClones.Count == 0)
		{
			return;
		}
		foreach (NPC clone in _kidClones)
		{
			clone.currentLocation?.characters.Remove(clone);
			if (clone.modData.TryGetValue("AIStardewValley.Core/Real", out string? real))
			{
				if (Kids.All().FirstOrDefault(k => k.Name == real) is Child c)
				{
					c.IsInvisible = false;
				}
				else if (Game1.getCharacterFromName(real) is NPC n)
				{
					n.IsInvisible = false;
				}
			}
		}
		_kidClones.Clear();
		NpcBusy.ReleaseAll(KidsReason);
	}

	// ---------------- 收获箱和汇总 ----------------

	/// <summary>收获箱（全队共用的箱子，一位农夫一个）。</summary>
	public Inventory Box() => Game1.player.team.GetOrCreateGlobalInventory(BoxPrefix + (Owner ?? Game1.player).UniqueMultiplayerID);

	public int BoxCount => Box().Where(i => i != null).Sum(i => i.Stack);

	/// <summary>放进收获箱（或出货箱）。</summary>
	private void Store(Item item)
	{
		if (Cfg.SpouseHarvestTo == "Bin")
		{
			Game1.getFarm().getShippingBin(Owner ?? Game1.player).Add(item);
			return;
		}
		Inventory box = Box();
		foreach (Item? slot in box)
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
		box.Add(item);
	}

	/// <summary>把收获箱里的东西拿进背包（放不下的留着）；返回拿了几件。</summary>
	public int Claim()
	{
		Inventory box = Box();
		int took = 0;
		foreach (Item? item in box.ToList())
		{
			if (item == null)
			{
				continue;
			}
			int before = item.Stack;
			Item? left = Game1.player.addItemToInventory(item);
			took += before - (left?.Stack ?? 0);
			if (left == null)
			{
				box.Remove(item);
			}
		}
		box.RemoveEmptySlots();
		Version++;
		return took;
	}

	private void Summarize(NPC spouse, SpouseDayLog log)
	{
		log.Summarized = true;
		MarkDirty();
		List<SpousePlanItem> done = log.Items.Where(i => i.State == SpousePlanStates.Done).ToList();
		if (done.Count == 0)
		{
			return;
		}
		string list = string.Join(Tr.Get("list.sep", "、"), done.Select(i => SpouseTasks.IsChore(i.Task) && i.Did > 0 ? $"{TaskName(i.Task)} ×{i.Did}" : i.Note.Length > 0 ? i.Note : TaskName(i.Task)));
		string box = BoxCount > 0 ? Tr.Get("spouse.plan.boxNote", "（收获在手机“家务安排”里领）") : "";
		Tell(Tr.Get("spouse.plan.summary", "{{npc}}今天：{{list}}{{box}}", ("npc", Name(spouse)), ("list", list), ("box", box)), 10000);
	}

	public string PromptBlock(string npc) => Shown && Log is SpouseDayLog log && log.Day == Today ? log.PromptBlock(npc, Game1.player.Name) : "";

	private void MarkDirty()
	{
		_store()?.MarkDirty(_path);
		_pushLog = _owner != 0;
	}

	/// <summary>告诉这份安排的主人（房主自己就弹提示，农场帮手发过去在他那边弹）。</summary>
	private void Tell(string text, int ms)
	{
		if (_owner == 0)
		{
			Hints.Hud(text, ms);
		}
		else
		{
			Net?.ToFarmer(_owner, HudOp, new Hud(text, ms));
		}
	}

	/// <summary>配偶冒一句（头顶的气泡只画在看得见的那位农夫那边）。</summary>
	private void Say(NPC spouse, string text)
	{
		if (_owner == 0)
		{
			NpcSpeech.Say(spouse, text);
		}
		else
		{
			Net?.ToFarmer(_owner, SayOp, new Bubble(spouse.Name, text));
		}
	}

	// ---------------- 联机 ----------------

	public const string LogOp = "spouse.log";

	public const string HudOp = "spouse.hud";

	public const string SayOp = "spouse.say";

	public const string PrefsOp = "spouse.prefs";

	public const string AskOp = "spouse.ask";

	public sealed record Hud(string Text, int Ms);

	public sealed record Bubble(string Npc, string Text);

	public sealed record AskTask(string Task);

	/// <summary>农场帮手关于配偶日程的设置。</summary>
	public sealed record Prefs(bool On, string Level, string HarvestTo, int Spend, bool Roommates, List<string> Tasks);

	public void WireNet(AISV.Game.Net.NetSync net)
	{
		Net = net;
		net.On<Hud>(HudOp, (_, h) => Hints.Hud(h.Text, h.Ms));
		net.On<Bubble>(SayOp, (_, b) =>
		{
			if (Game1.getCharacterFromName(b.Npc) is NPC n && n.currentLocation == Game1.currentLocation)
			{
				NpcSpeech.Say(n, b.Text);
			}
		});
		net.On<string>(LogOp, (_, json) =>
		{
			_store()?.Import(SpouseDayLog.Path, json);
			Version++;
		});
		net.On<Prefs>(PrefsOp, (from, p) =>
		{
			if (!Context.IsMainPlayer)
			{
				return;
			}
			ModConfig c = JsonCopy(_config());
			c.SpousePlanner = p.On;
			c.SpouseHelpLevel = p.Level;
			c.SpouseHarvestTo = p.HarvestTo;
			c.SpouseSpendDaily = p.Spend;
			c.RoommatePlans = p.Roommates;
			c.SpouseWaters = p.Tasks.Contains(SpouseTasks.Water);
			c.SpouseHarvests = p.Tasks.Contains(SpouseTasks.Harvest);
			c.SpouseTendsAnimals = p.Tasks.Contains(SpouseTasks.Animals);
			c.SpouseClears = p.Tasks.Contains(SpouseTasks.Clear);
			c.SpouseGreenhouse = p.Tasks.Contains(SpouseTasks.Greenhouse);
			c.SpouseFishPonds = p.Tasks.Contains(SpouseTasks.Ponds);
			c.SpouseMachines = p.Tasks.Contains(SpouseTasks.Machines);
			c.SpouseGoesOut = p.Tasks.Contains(SpouseTasks.Visit);
			if (_farmhands.TryGetValue(from, out SpousePlanner? other))
			{
				other._prefs = c;
				return;
			}
			other = new SpousePlanner(this, from, c);
			_farmhands[from] = other;
			other.OnDayStarted();
			_log.Info($"配偶日程：也替 {Game1.GetPlayer(from)?.Name} 排（{other.Spouse()?.Name ?? "还没结婚"}）。");
		});
		net.On<AskTask>(AskOp, (from, a) =>
		{
			if (Context.IsMainPlayer && _farmhands.TryGetValue(from, out SpousePlanner? other) && other.Spouse() is NPC spouse && other.RequestTask(a.Task, out bool today))
			{
				other.Tell(today
					? Tr.Get("spouse.plan.okToday", "{{npc}}答应今天就帮你{{task}}。", ("npc", other.Name(spouse)), ("task", TaskName(a.Task)))
					: Tr.Get("spouse.plan.okTomorrow", "{{npc}}答应明天一早帮你{{task}}。", ("npc", other.Name(spouse)), ("task", TaskName(a.Task))), 6000);
			}
		});
	}

	private static ModConfig JsonCopy(ModConfig c) => System.Text.Json.JsonSerializer.Deserialize<ModConfig>(System.Text.Json.JsonSerializer.Serialize(c))!;

	/// <summary>农场帮手：把自己的设置发给房主（连上后、换天、改了开关时）。</summary>
	private void SendPrefs()
	{
		if (_owner != 0 || Net?.Linked != true)
		{
			return;
		}
		ModConfig c = _config();
		List<string> tasks = new[] { SpouseTasks.Water, SpouseTasks.Harvest, SpouseTasks.Animals, SpouseTasks.Clear, SpouseTasks.Greenhouse, SpouseTasks.Ponds, SpouseTasks.Machines, SpouseTasks.Visit }.Where(IsOn).ToList();
		Net.ToHost(PrefsOp, new Prefs(c.SpousePlanner, c.SpouseHelpLevel, c.SpouseHarvestTo, c.SpouseSpendDaily, c.RoommatePlans, tasks));
		_prefsSent = true;
	}

	/// <summary>房主替农场帮手排的那几份：跟着这边一起过每一天；人走了就收起来。</summary>
	private IEnumerable<SpousePlanner> Farmhands()
	{
		foreach (long id in _farmhands.Keys.ToList())
		{
			if (Game1.GetPlayer(id, onlyOnline: true) == null || Net?.IsLinkedFarmhand(id) != true)
			{
				_farmhands[id].Cleanup();
				Planners.Remove(_farmhands[id]);
				_farmhands.Remove(id);
			}
		}
		return _farmhands.Values.ToList();
	}
}
