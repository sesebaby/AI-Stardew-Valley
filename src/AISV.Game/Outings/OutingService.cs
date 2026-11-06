using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Outings;
using AISV.Core.Persona;
using AISV.Core.Scenes;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace AISV.Game.Outings;

/// <summary>回复框里能开哪个邀约开关。</summary>
public sealed record OutingOptions(bool Invite, bool Date)
{
	public static readonly OutingOptions None = new OutingOptions(false, false);

	public bool Any => Invite || Date;
}

/// <summary>
/// 约村民出去：判断能不能约、给 AI 准备可选的出游、记下结果，答应了就把场景排进游戏事件（或马上开演），
/// 演完记记忆、对话、八卦，过几天来信。
/// </summary>
public sealed class OutingService : IOutingDesk
{
	private const string EventsPrefix = "Data/Events/";

	private static readonly TimeSpan LinesWait = TimeSpan.FromSeconds(30);

	private readonly Func<ModConfig> _config;

	private readonly IModHelper _helper;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private readonly Dictionary<string, DateTime> _linesInFlight = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private OutingCatalog? _catalog;

	private readonly object _catalogLock = new object();

	private bool _eventHooks;

	/// <summary>调试：下次配偶开口一定主动约。</summary>
	private bool _forceOffer;

	public OutingService(Func<ModConfig> config, Func<DocumentStore?> store, IModHelper helper, Relationships relationships, NpcDirectory npcs, ILog log)
	{
		_config = config;
		_helper = helper;
		_relationships = relationships;
		_npcs = npcs;
		_log = log;
		Book = new OutingBook(store);
		RegisterEventHooks();
	}

	public OutingBook Book { get; }

	public SocialSystem? Social { get; set; }

	public ConversationLog? Conversations { get; set; }

	public DialogueEngine? Engine { get; set; }

	public MaterialFactory? Materials { get; set; }

	public ProposalService? Proposals { get; set; }

	public OutingCatalog Catalog
	{
		get
		{
			lock (_catalogLock)
			{
				if (_catalog != null)
				{
					return _catalog;
				}
				string dir = Path.Combine(_helper.DirectoryPath, "assets", "outings");
				_catalog = OutingCatalog.Load(Read(Path.Combine(dir, "spots.json")), Read(Path.Combine(dir, "outings.json")), _log);
				_catalog.MinHeartsFloor = () => _config().OutingHearts;
				_catalog.LocationAvailable = l => !l.StartsWith("BathHouse", StringComparison.OrdinalIgnoreCase) || Context.IsWorldReady && Game1.isLocationAccessible("Railroad");
				_log.Debug($"读了 {_catalog.Count} 种出游。");
				return _catalog;
			}
		}
	}

	private static int Today => GameState.Today.TotalDays;

	// ---------------- 能不能约 ----------------

	public OutingOptions Options(NPC npc, bool multiTurn)
	{
		try
		{
			ModConfig c = _config();
			string name = npc.Name;
			if (multiTurn || !c.AiOn || !c.OutingsOn || Game1.eventUp || Game1.currentLocation?.currentEvent != null || string.IsNullOrWhiteSpace(name))
			{
				return OutingOptions.None;
			}
			OutingAsk ask = Ask(npc, spouse: false);
			if (ask.Child)
			{
				return OutingOptions.None;
			}
			int min = Math.Clamp(c.OutingHearts, 0, 14);
			string? why = ask.Hearts < min ? $"心数不够（{ask.Hearts}/{min}）" : Book.HasWaiting(name) ? "还有一次没去" : Book.OnCooldown(name, Today) ? "还在冷却" : null;
			if (why != null)
			{
				_log.Debug($"约 {name} 出去的开关不出现：{why}。");
				return OutingOptions.None;
			}
			bool invite = Catalog.Eligible(ask).Count > 0;
			bool date = GameState.IsSpouse(name) && ask.Romantic && Catalog.Eligible(ask with { Spouse = true }).Count > 0;
			return new OutingOptions(invite, date);
		}
		catch (Exception ex)
		{
			_log.Warn($"判断能不能约 {npc.Name} 出去时出错：{ex.Message}");
			return OutingOptions.None;
		}
	}

	private OutingAsk Ask(NPC npc, bool spouse)
	{
		string personality = _npcs.Persona(npc.Name, npc).Personality;
		return new OutingAsk(_relationships.EffectiveHearts(npc.Name), MaterialFactory.IsAdultRomanticPartner(npc, personality), ChildSafety.IsChildCoded(npc.Name, npc.Age, personality), spouse, _config().HotSpouseScenesOn());
	}

	/// <summary>准备一次判定：可选出游、之前的邀约、关系状态。约会但已经不是配偶、或者没有能用的出游时返回 null。</summary>
	public (OutingContext Context, OutingIntent Intent)? Prepare(NPC npc, OutingKind kind, string playerText)
	{
		bool spouse = kind != OutingKind.Invite;
		OutingAsk ask = Ask(npc, spouse);
		if (spouse && (!GameState.IsSpouse(npc.Name) || !ask.Romantic || ask.Child))
		{
			return null;
		}
		OutingIntent intent = OutingIntentReader.Read(playerText, Catalog.Locations, Exists);
		string options = Catalog.OptionsBlock(ask, intent);
		if (options.Length == 0)
		{
			return null;
		}
		return (BuildContext(npc.Name, kind, options), intent);
	}

	private OutingContext BuildContext(string npc, OutingKind kind, string options) =>
		new OutingContext(kind, options, Book.PromptBlock(npc, Today), Proposals?.Status(npc) ?? "unknown", Proposals?.Narrative(npc) ?? "unknown", Proposals?.VanillaBlock(npc) ?? "", _config().HotSpouseScenesOn());

	/// <summary>配偶主动邀约（开场白之前，在后台线程上调）。</summary>
	public OutingOffer? SpouseOffer(DialogueMaterial m)
	{
		try
		{
			ModConfig c = _config();
			if (c.SpouseAsksOutPercent <= 0 && !_forceOffer || !c.AiOn || !c.OutingsOn || !m.IsSpouse || !m.IsAdultRomanticPartner || m.IsChildCoded
				|| m.Hearts < Math.Clamp(c.OutingHearts, 0, 14) || Book.HasWaiting(m.NpcName) || Book.OnCooldown(m.NpcName, Today))
			{
				return null;
			}
			OutingPlan? plan;
			OutingAsk ask = new OutingAsk(m.Hearts, true, false, true, c.HotSpouseScenesOn());
			lock (_random)
			{
				plan = _forceOffer ? Catalog.Eligible(ask).FirstOrDefault(p => p.TimeMax >= Game1.timeOfDay) : Catalog.PickSpouseOffer(ask, Game1.timeOfDay, c.SpouseAsksOutPercent, _random);
				_forceOffer = false;
			}
			return plan == null ? null : new OutingOffer(plan, BuildContext(m.NpcName, OutingKind.SpouseOffer, OutingCatalog.SingleOption(plan)));
		}
		catch (Exception ex)
		{
			_log.Warn($"准备 {m.NpcName} 的主动邀约时出错：{ex.Message}");
			return null;
		}
	}

	/// <summary>农夫答应配偶的邀约时再查一次：还能约、那个出游还在、时间没过。</summary>
	public bool CanAcceptOffer(NPC npc, OutingDecision offer)
	{
		if (_config().SpouseAsksOutPercent <= 0 || !_npcs.IsEnabled(npc.Name) || !Options(npc, false).Date)
		{
			return false;
		}
		return Catalog.Eligible(Ask(npc, spouse: true)).Any(p => string.Equals(p.Id, offer.OutingId, StringComparison.OrdinalIgnoreCase) && p.TimeMax >= Game1.timeOfDay);
	}

	/// <summary>反提议以选中的出游为准：地点换成出游的地点；出游不存在就不要这个反提议。</summary>
	public bool KeepCounter(string npc, OutingCounter counter)
	{
		OutingPlan? plan = Catalog.Find(counter.OutingId);
		if (plan == null)
		{
			_log.Info($"{npc} 改提的出游 {counter.OutingId} 不存在，不要这个反提议。");
			return false;
		}
		if (!string.Equals(counter.Place, plan.Location, StringComparison.OrdinalIgnoreCase))
		{
			_log.Debug($"{npc} 改提的地点 {counter.Place} 换成出游的地点 {plan.Location}。");
			counter.Place = plan.Location;
		}
		return true;
	}

	// ---------------- 记下结果 ----------------

	/// <summary>记下答复：关键记忆、里程碑、邀约记录；答应了就准备场景。</summary>
	public OutingRecord Record(NPC npc, OutingKind kind, string playerText, OutingDecision d, OutingIntent? intent = null)
	{
		ModConfig c = _config();
		string name = npc.Name;
		GameDate today = GameState.Today;
		OutingIntent wanted = intent ?? OutingIntentReader.Read(playerText, Catalog.Locations, Exists);
		if (Social != null)
		{
			if (!string.IsNullOrWhiteSpace(d.ToRemember))
			{
				Social.Feelings.AddKeyMemory(name, d.ToRemember, today);
			}
			string fallback = d.Agrees ? "农夫约我一起待一会儿，我答应了。" : "农夫约我一起待一会儿，我没答应。";
			Social.Milestones.AddCustom(name, $"invite_out_{(d.Agrees ? "accepted" : "declined")}:{today.TotalDays}", string.IsNullOrWhiteSpace(d.ToRemember) ? fallback : d.ToRemember, today);
		}
		bool awaitLines = d.Agrees && string.IsNullOrWhiteSpace(d.Beat1) && c.AiOn;
		OutingRecord r;
		lock (_random)
		{
			r = Book.Record(name, playerText, d, Ask(npc, kind != OutingKind.Invite), wanted, Catalog, d.Agrees ? c.OutingRestAfterYes : c.OutingRestAfterNo, today.TotalDays, _random);
		}
		_log.Debug($"邀约 {name}：{(r.Agreed ? "答应" : "没答应")}，{OutingBook.StateName(r.State)}，冷却到第 {r.RestUntil} 天，出游 {(r.OutingId.Length > 0 ? r.OutingId : "无")}，{r.Id}");
		if (r.Waiting)
		{
			_log.Info($"{name} 答应出去：{r.Place}，{Clock(r.FromTime)}-{Clock(r.ToTime)}。");
			if (awaitLines)
			{
				_linesInFlight[r.Id] = DateTime.UtcNow;
				WriteLines(npc, r);
			}
			StartOrQueue(r);
		}
		return r;
	}

	/// <summary>后台写台词，写好了重拼脚本（失败就用内置的几句）。</summary>
	private void WriteLines(NPC npc, OutingRecord r)
	{
		OutingPlan? plan = Catalog.Find(r.OutingId);
		DialogueEngine? engine = Engine;
		if (plan == null || engine == null || Materials == null)
		{
			ApplyLines(r.Id, null);
			return;
		}
		DialogueMaterial material = Materials.Build(npc);
		string scenery = SceneryText(plan);
		GameDate today = GameState.Today;
		string id = r.Id;
		Task.Run(async () =>
		{
			IReadOnlyList<string>? lines = null;
			try
			{
				lines = await engine.OutingLinesAsync(material, plan, scenery, r, today).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_log.Warn($"{r.Npc} 的出游台词没写成，用内置的：{ex.Message}");
			}
			MainThread.Run(() => DelayedAction.functionAfterDelay(() => ApplyLines(id, lines), 100));
		});
	}

	private void ApplyLines(string id, IReadOnlyList<string>? lines)
	{
		_linesInFlight.Remove(id);
		OutingRecord? r = Book.ApplyLines(id, lines, Catalog);
		if (r == null)
		{
			return;
		}
		_log.Debug(lines != null ? $"{r.Npc} 的出游台词写好了。" : $"{r.Npc} 的出游用内置台词。");
		Invalidate(r.Place);
		TryStartHere();
	}

	/// <summary>布景的东西叫什么（给写台词的 AI 看）。</summary>
	private static string SceneryText(OutingPlan plan) => string.Join("、", plan.Props.Select(p => ItemRegistry.GetData(p.ItemId)?.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());

	// ---------------- 场景 ----------------

	private bool AwaitingLines(OutingRecord r) => _linesInFlight.TryGetValue(r.Id, out DateTime since) && DateTime.UtcNow - since < LinesWait;

	private static bool InWindow(OutingRecord r) => Game1.timeOfDay >= r.FromTime && Game1.timeOfDay <= r.ToTime;

	private void StartOrQueue(OutingRecord r)
	{
		if (string.IsNullOrWhiteSpace(r.Script) || TryStartNow(r) || Book.Find(r.Id)?.Waiting != true)
		{
			return;
		}
		Invalidate(r.Place);
		_log.Info($"{r.Npc} 的出游排上了：{Clock(r.FromTime)}-{Clock(r.ToTime)} 去 {r.Place}。");
		Game1.addHUDMessage(new HUDMessage(Tr.Get("outing.queued", "{{npc}}答应了，{{time}}在{{location}}等你。", ("npc", _npcs.Names.Display(r.Npc)), ("location", PlaceName(r.Place)), ("time", $"{Clock(r.FromTime)}-{Clock(r.ToTime)}")), HUDMessage.newQuest_type));
	}

	/// <summary>人在场景地点、时间在窗口里、没有菜单和事件时，开演等着的出游。</summary>
	public bool TryStartHere()
	{
		GameLocation? here = Game1.currentLocation;
		if (here == null || here.currentEvent != null || Game1.activeClickableMenu != null || Game1.eventUp)
		{
			return false;
		}
		OutingRecord? r = Book.Waiting.FirstOrDefault(x => string.Equals(here.NameOrUniqueName, x.Place, StringComparison.OrdinalIgnoreCase) && InWindow(x));
		return r != null && TryStartNow(r);
	}

	private bool TryStartNow(OutingRecord r)
	{
		GameLocation? here = Game1.currentLocation;
		if (!r.Waiting || AwaitingLines(r) || here == null || !string.Equals(here.NameOrUniqueName, r.Place, StringComparison.OrdinalIgnoreCase) || !InWindow(r)
			|| here.currentEvent != null || Game1.activeClickableMenu != null || Game1.eventUp)
		{
			return false;
		}
		try
		{
			if (!TryLayout(here, r, out string script))
			{
				CancelUnsafe(r);
				return false;
			}
			_log.Info($"{r.Npc} 的出游在 {r.Place} 开演。");
			here.startEvent(new Event(script, Game1.player));
			return true;
		}
		catch (Exception ex)
		{
			_log.Warn($"{r.Npc} 的出游没能马上开演：{ex.Message}");
			return false;
		}
	}

	public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
	{
		string name = e.NameWithoutLocale.BaseName;
		if (!name.StartsWith(EventsPrefix, StringComparison.OrdinalIgnoreCase) || !Context.IsWorldReady)
		{
			return;
		}
		string location = name[EventsPrefix.Length..];
		List<OutingRecord> waiting = Book.Waiting.Where(r => !AwaitingLines(r) && string.Equals(r.Place, location, StringComparison.OrdinalIgnoreCase)).ToList();
		if (waiting.Count == 0)
		{
			return;
		}
		GameLocation? place = Game1.getLocationFromName(location) ?? (string.Equals(Game1.currentLocation?.NameOrUniqueName, location, StringComparison.OrdinalIgnoreCase) ? Game1.currentLocation : null);
		List<(string Key, string Script)> entries = new List<(string, string)>();
		foreach (OutingRecord r in waiting)
		{
			if (place == null || !TryLayout(place, r, out string script))
			{
				CancelUnsafe(r);
				continue;
			}
			entries.Add(($"{EventId(r.Id)}/{OutingCatalog.WaitingCondition} {r.Id}/{OutingCatalog.TimeCondition} {r.FromTime} {r.ToTime}", script));
		}
		if (entries.Count > 0)
		{
			e.Edit(asset =>
			{
				IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
				foreach ((string key, string script) in entries)
				{
					data[key] = script;
				}
			}, AssetEditPriority.Late);
		}
	}

	/// <summary>事件 ID：9731000 + 邀约 ID 的稳定哈希。</summary>
	private static string EventId(string id)
	{
		uint hash = 2166136261;
		foreach (char c in id.ToLowerInvariant())
		{
			hash = (hash ^ c) * 16777619;
		}
		return (9731000 + hash % 10000).ToString();
	}

	private bool TryLayout(GameLocation location, OutingRecord r, out string script)
	{
		script = OutingLayout.WithScenery(r.Script, r.Id);
		OutingPlan? plan = Catalog.Find(r.OutingId);
		if (location is Farm farm && !string.IsNullOrEmpty(plan?.FarmArea))
		{
			return TryFarmLayout(farm, plan, ref script);
		}
		if (location is FarmHouse house)
		{
			Point entry = house.getEntryLocation();
			script = OutingLayout.AnchorFarmHouse(script, entry.X, entry.Y);
		}
		return EventPlacement.TrySafe(script, (x, y) => Walkable(location, x, y), out script);
	}

	private bool TryFarmLayout(Farm farm, OutingPlan plan, ref string script)
	{
		xTile.Layers.Layer? back = farm.Map.GetLayer("Back");
		if (back == null || !OutingCatalog.FarmAreas.Contains(plan.FarmArea))
		{
			return false;
		}
		Point door = farm.GetMainFarmHouseEntry();
		(int X, int Y) anchor = OutingLayout.Anchor(plan.FarmArea, back.LayerWidth, back.LayerHeight, (door.X, door.Y));
		string exit = OutingLayout.ExitFor(plan.FarmArea);
		Warp? warp = exit.Length == 0 ? null : farm.warps.FirstOrDefault(w => string.Equals(w.TargetName, exit, StringComparison.OrdinalIgnoreCase));
		if (warp != null)
		{
			anchor = OutingLayout.InsideExit(plan.FarmArea, warp.X, warp.Y);
		}
		bool pond = plan.FarmArea == "pond";
		int radius = pond ? Math.Max(back.LayerWidth, back.LayerHeight) : 12;
		Func<int, int, bool>? nearWater = pond ? (x, y) => NearWater(farm, x, y) : null;
		if (!OutingLayout.TryFindCenter(anchor, back.LayerWidth, back.LayerHeight, radius, (x, y) => Clear(farm, x, y), plan.Props, nearWater, out (int X, int Y) center))
		{
			return false;
		}
		script = OutingLayout.PlaceOnFarm(script, center.X, center.Y);
		return true;
	}

	private static bool NearWater(Farm farm, int x, int y)
	{
		for (int dx = -4; dx <= 4; dx++)
		{
			for (int dy = -4; dy <= 4; dy++)
			{
				if (farm.isTileOnMap(new Vector2(x + dx, y + dy)) && farm.doesTileHaveProperty(x + dx, y + dy, "Water", "Back") != null)
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>能站人：在地图上、有地面、不是水、能走。</summary>
	private static bool Walkable(GameLocation l, int x, int y)
	{
		Vector2 t = new Vector2(x, y);
		try
		{
			return x >= 0 && y >= 0 && l.isTileOnMap(t) && l.Map.GetLayer("Back")?.Tiles[x, y] != null && l.doesTileHaveProperty(x, y, "Water", "Back") == null && l.isTilePassable(t);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>能站人，而且上面没有东西（只允许铺地板）。</summary>
	internal static bool Clear(GameLocation l, int x, int y)
	{
		if (!Walkable(l, x, y))
		{
			return false;
		}
		Vector2 t = new Vector2(x, y);
		Rectangle box = new Rectangle(x * 64, y * 64, 64, 64);
		return !l.objects.ContainsKey(t) && l.getBuildingAt(t) == null && (!l.terrainFeatures.TryGetValue(t, out TerrainFeature? f) || f is Flooring)
			&& !l.largeTerrainFeatures.Any(f => f.getBoundingBox().Intersects(box)) && !l.resourceClumps.Any(c => c.getBoundingBox().Intersects(box))
			&& !l.furniture.Any(f => f.boundingBox.Value.Intersects(box));
	}

	private void CancelUnsafe(OutingRecord r)
	{
		if (Book.Cancel(r.Id) == null)
		{
			return;
		}
		_log.Warn($"{r.Npc} 的出游在 {r.Place} 找不到安全的站位，取消了。");
		Game1.addHUDMessage(new HUDMessage(Tr.Get("outing.cancelled", "和{{npc}}约在{{location}}的事黄了。", ("npc", _npcs.Names.Display(r.Npc)), ("location", PlaceName(r.Place))), HUDMessage.error_type));
	}

	private void Invalidate(string location)
	{
		if (!string.IsNullOrWhiteSpace(location))
		{
			_helper.GameContent.InvalidateAllLocales(EventsPrefix + location);
		}
	}

	private void RegisterEventHooks()
	{
		if (_eventHooks)
		{
			return;
		}
		_eventHooks = true;
		Event.RegisterPrecondition(OutingCatalog.WaitingCondition, (_, _, args) => Book.Find(Arg(args, OutingCatalog.WaitingCondition, 0))?.Waiting == true);
		Event.RegisterPrecondition(OutingCatalog.TimeCondition, (_, _, args) =>
			int.TryParse(Arg(args, OutingCatalog.TimeCondition, 0), out int min) && int.TryParse(Arg(args, OutingCatalog.TimeCondition, 1), out int max) && Game1.timeOfDay >= min && Game1.timeOfDay <= max);
		Event.RegisterCommand(OutingCatalog.DoneCommand, (Event ev, string[] args, EventContext _) =>
		{
			try
			{
				Complete(Arg(args, OutingCatalog.DoneCommand, 0));
			}
			catch (Exception ex)
			{
				_log.Warn("出游演完后的记录出错：" + ex.Message);
			}
			ev.CurrentCommand++;
		});
		Event.RegisterCommand(OutingCatalog.SceneryCommand, (Event ev, string[] args, EventContext ctx) =>
		{
			try
			{
				OutingRecord? r = Book.Find(Arg(args, OutingCatalog.SceneryCommand, 0));
				OutingPlan? plan = r == null ? null : Catalog.Find(r.OutingId);
				if (r != null && plan != null && plan.Props.Count > 0)
				{
					OutingScenery.Place(ev, ctx.Location, r.Npc, plan.Props);
				}
			}
			catch (Exception ex)
			{
				_log.Warn("出游布景摆不上，不带布景接着演：" + ex.Message);
			}
			ev.CurrentCommand++;
		});
	}

	// ---------------- 演完 ----------------

	/// <summary>开演第一条命令：标记完成，加好感，记记忆、对话、八卦，排一封事后来信。</summary>
	private void Complete(string id)
	{
		GameDate today = GameState.Today;
		OutingRecord? r = Book.Complete(id, today.TotalDays);
		if (r == null)
		{
			return;
		}
		Game1.player?.eventsSeen.Remove(EventId(id));
		string npc = r.Npc, display = _npcs.Names.Display(npc);
		int points = Math.Clamp(_config().OutingPoints, 0, 100);
		if (points > 0)
		{
			_relationships.Add(npc, points);
		}
		OutingPlan? plan = Catalog.Find(r.OutingId);
		string fallback = string.IsNullOrWhiteSpace(r.ToRemember) ? Tr.Get("outing.memory", "{{npc}}答应了邀请，和农夫一起待了一会儿。", ("npc", display)) : r.ToRemember;
		OutingMemory memory = OutingAi.Remember(r, plan?.Title, OutingAi.SpokenLines(r.Script, npc), Weather(), display, fallback);
		string text = string.IsNullOrWhiteSpace(memory.Text) ? fallback : memory.Text;
		Book.SetMemory(r.Id, text);
		Social?.Feelings.AddKeyMemory(npc, text, today);
		Social?.Milestones.AddCustom(npc, "invite_out_scene:" + r.OutingId, text, today);
		string player = Game1.player?.Name ?? "";
		Conversations?.Add(npc, new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			SettingRef = r.Id,
			SettingName = plan?.Title ?? "",
			Farmer = player,
			Opener = memory.FirstLine,
			FarmerReply = $"[一起出去：{memory.Place}] {r.Asked}".TrimEnd(),
			Comeback = memory.LastLine,
			Face = string.IsNullOrWhiteSpace(r.Feeling) ? "neutral" : r.Feeling,
			HeartPoints = points
		});
		if (Social != null)
		{
			Social.Gossip.RecordInteraction(new DailyInteraction
			{
				Npc = npc,
				Farmer = player,
				Opener = $"[农夫和{display}一起出去了：{memory.Place}] {text}",
				Reply = $"[一起出去] {r.Asked}".TrimEnd(),
				Comeback = memory.LastLine.Length > 0 ? memory.LastLine : memory.FirstLine,
				Face = string.IsNullOrWhiteSpace(r.Feeling) ? "neutral" : r.Feeling
			});
			if (r.WorthTelling)
			{
				Social.Gossip.AddLowKey(text, new[] { npc }, "invite_out", r.Id, today);
			}
			Social.RequestEventLetter(npc, "after_outing_" + r.Id, $"回想你和农夫那次一起出去（{Shorten(text, 300)}），从那天最让你记得的一个细节写起，说说你现在怎么看那次，以后还想不想再约。");
		}
		Invalidate(r.Place);
		_log.Debug($"{npc} 的出游演完了（{r.OutingId}）。");
	}

	private static string Weather() => Game1.isSnowing ? "下着雪" : Game1.isRaining ? "下着雨" : Game1.isDebrisWeather ? "刮着风" : "天气晴朗";

	// ---------------- 每天、计划页、日记 ----------------

	public void OnDayStarted()
	{
		_linesInFlight.Clear();
		IReadOnlyList<OutingRecord> expired = Book.Expire(Today);
		foreach (string location in expired.Concat(Book.Waiting).Select(r => r.Place).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			Invalidate(location);
		}
		if (expired.Count > 0)
		{
			_log.Debug($"{expired.Count} 次出游过期了：{string.Join("、", expired.Select(r => r.Npc))}。");
		}
	}

	public void OnTimeChanged() => TryStartHere();

	/// <summary>当天事实：今天一起出去过的人。</summary>
	public IReadOnlyList<string> CompletedToday() => Book.CompletedOn(Today).Where(r => _npcs.IsEnabled(r.Npc))
		.Select(r => string.IsNullOrWhiteSpace(r.ToRemember) ? $"和{_npcs.Names.Display(r.Npc)}在{PlaceName(r.Place)}" : $"和{_npcs.Names.Display(r.Npc)}在{PlaceName(r.Place)}：{r.ToRemember.Trim()}").ToList();

	/// <summary>出游计划页：普通的能去哪（3 心、非恋人），约会能去哪（14 心、配偶）。</summary>
	public IReadOnlyList<OutingDestination> Destinations(bool spouse) => Catalog.Destinations(spouse ? new OutingAsk(14, true, false, true, _config().HotSpouseScenesOn()) : new OutingAsk(3, false, false));

	public static string PlaceName(string location)
	{
		string key = "outing.place." + location;
		string fallback = OutingCatalog.PlaceName(location);
		try
		{
			return Tr.Get(key, fallback);
		}
		catch
		{
			return fallback;
		}
	}

	public static string Clock(int time) => $"{Math.Clamp(time / 100, 0, 26) % 24:D2}:{Math.Clamp(time % 100, 0, 59):D2}";

	private static bool Exists(string location)
	{
		try
		{
			if (Game1.locations == null || Game1.locations.Count == 0 || string.Equals(Game1.currentLocation?.NameOrUniqueName, location, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return Game1.locations.Any(l => string.Equals(l.NameOrUniqueName, location, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Name, location, StringComparison.OrdinalIgnoreCase)) || Game1.getLocationFromName(location) != null;
		}
		catch
		{
			return true;
		}
	}

	private static string Shorten(string text, int max) => text.Length > max ? text[..max].TrimEnd() + "…" : text;

	private static string Arg(string[] args, string command, int index)
	{
		int skip = args.Length > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
		return skip + index < args.Length ? args[skip + index] : "";
	}

	private string? Read(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (Exception ex)
		{
			_log.Warn($"读不了 {Path.GetFileName(path)}：{ex.Message}");
			return null;
		}
	}

	// ---------------- 调试 ----------------

	public string Command(string[] args)
	{
		if (!Context.IsWorldReady)
		{
			return "先进存档。";
		}
		string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "state";
		switch (verb)
		{
			case "state":
				return string.Join("\n", Book.All.OrderByDescending(r => r.AskedOn).Take(12).Select(r => $"{r.Id} {r.Npc} {OutingBook.StateName(r.State)} {r.OutingId} {r.Place} {r.FromTime}-{r.ToTime} 冷却到 {r.RestUntil}")) is { Length: > 0 } s ? s : "没有邀约记录。";
			case "options" when args.Length > 1 && GameState.FindNpc(args[1]) is NPC n:
				OutingOptions o = Options(n, false);
				return $"约出去：{o.Invite}，约会：{o.Date}";
			case "accept" when args.Length > 1 && GameState.FindNpc(args[1]) is NPC n:
			{
				OutingPlan? plan = args.Length > 2 ? Catalog.Find(args[2]) : Catalog.Eligible(Ask(n, GameState.IsSpouse(n.Name) && args.Length > 3)).FirstOrDefault();
				if (plan == null)
				{
					return "没有能用的出游。";
				}
				OutingDecision d = new OutingDecision { Agrees = true, Says = "好啊，走吧。", Feeling = "happy", Warmth = 10, Tone = plan.Type, OutingId = plan.Id, ToRemember = "调试：答应了出游。", WorthTelling = true };
				OutingRecord r = Record(n, plan.IsSpouseType || plan.Type == "date_light" && GameState.IsSpouse(n.Name) ? OutingKind.SpouseDate : OutingKind.Invite, "调试邀约", d, OutingIntent.At(plan.Location));
				return $"{r.Id} {OutingBook.StateName(r.State)} {r.OutingId} {r.Place} {r.FromTime}-{r.ToTime}";
			}
			case "offer":
				_forceOffer = true;
				return "下次配偶开口会主动约你。";
			case "start":
				return TryStartHere() ? "开演了。" : "这里现在没有能演的出游。";
			case "expire":
				OnDayStarted();
				return "检查过期了。";
			default:
				return "用法：aisv_outing state | options <村民> | accept <村民> [出游 id] | offer | start | expire";
		}
	}
}
