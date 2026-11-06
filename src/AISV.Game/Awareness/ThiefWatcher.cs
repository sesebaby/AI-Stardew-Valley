using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.Game.Awareness;

/// <summary>
/// Stardew Thief 兼容：在它的扒窃、入室和抓人方法后面只读地看一眼（不改它的行为），知道偷了谁、拿了什么、被谁撞见。
/// 认出来以后记一笔，交给"看到听到"（目击、找人理论），失主没看见时让他发现少了东西、在镇上传开。
/// </summary>
public sealed class ThiefWatcher
{
	public const string ModId = "bubuge.StardewThief";

	private const string Prefix = "StardewThief/";

	/// <summary>认出扒窃以后再等这么多帧，让偷来的东西和钱都进包；骑马时东西来得慢一点。</summary>
	private const int SettleTicks = 30;

	private const int HorseSettleTicks = 90;

	/// <summary>背包满了会弹出拿东西的菜单，最多等这么久。</summary>
	private const int MaxWaitTicks = 1200;

	private static ThiefWatcher? _current;

	private readonly Func<ModConfig> _config;

	private readonly AwarenessSystem _awareness;

	private readonly TheftLog _log;

	private readonly ILog _smapiLog;

	private NPC? _attempt;

	private int _attemptMoney;

	private TheftRecord? _pending;

	private bool _pendingHorse;

	private int _pendingMoney;

	private int _pendingStart;

	private readonly Dictionary<string, (TheftRecord Record, List<Item> Taken)> _burglaries = new Dictionary<string, (TheftRecord, List<Item>)>(StringComparer.OrdinalIgnoreCase);

	private (GameLocation Where, NPC? Spotter, Vector2 Tile)? _caught;

	public ThiefWatcher(Func<ModConfig> config, AwarenessSystem awareness, TheftLog log, ILog smapiLog)
	{
		_config = config;
		_awareness = awareness;
		_log = log;
		_smapiLog = smapiLog;
	}

	public bool Installed { get; private set; }

	public TheftLog Log => _log;

	/// <summary>失主发现少了东西（不知道是谁）：改心情、在镇上传开。</summary>
	public Action<TheftRecord, string>? NoticedLoss { get; set; }

	/// <summary>记下了一笔（以后从失主的钱和家当里扣）。</summary>
	public Action<TheftRecord>? Recorded { get; set; }

	public void Attach(IModHelper helper, Harmony harmony)
	{
		Installed = helper.ModRegistry.IsLoaded(ModId);
		if (!Installed)
		{
			return;
		}
		_current = this;
		helper.Events.GameLoop.GameLaunched += (_, _) => Patch(harmony);
		helper.Events.GameLoop.UpdateTicked += (_, _) => OnUpdateTicked();
		helper.Events.Player.InventoryChanged += (_, e) => OnInventoryChanged(e);
		helper.Events.Player.Warped += (_, e) => OnWarped(e);
		helper.Events.GameLoop.DayEnding += (_, _) => FlushAll();
		helper.Events.GameLoop.DayStarted += (_, _) => Reset();
		helper.Events.GameLoop.ReturnedToTitle += (_, _) => Reset();
	}

	private bool Active => Installed && _config().NoticeTheft && Context.IsWorldReady && Context.IsMainPlayer;

	public void Reset()
	{
		_attempt = null;
		_pending = null;
		_burglaries.Clear();
		_caught = null;
	}

	// ---------------- 接到 Stardew Thief 上 ----------------

	/// <summary>它的程序集要等所有 mod 都加载完才找得到。</summary>
	private void Patch(Harmony harmony)
	{
		const string pick = "StardewThief.Mechanics.PickpocketHandler:";
		const string jail = "StardewThief.Mechanics.JailHandler:";
		bool ok = Hook(harmony, pick + "TryPickpocket", prefix: nameof(BeforeTry))
			& Hook(harmony, pick + "OnPickpocketSuccess", postfix: nameof(AfterSuccess))
			& Hook(harmony, pick + "OnPickpocketFail", postfix: nameof(AfterFail))
			& Hook(harmony, pick + "TryHorsePickpocket", prefix: nameof(BeforeTry), postfix: nameof(AfterHorse))
			& Hook(harmony, "StardewThief.Mechanics.BurglaryHandler:OnSearchComplete", postfix: nameof(AfterSearch))
			& Hook(harmony, "StardewThief.Mechanics.MaskBurglaryHandler:OnSearchComplete", postfix: nameof(AfterSearch))
			& Hook(harmony, jail + "TriggerCaught", prefix: nameof(BeforeSpotted), postfix: nameof(AfterCaught))
			& Hook(harmony, jail + "TriggerAlertFull", prefix: nameof(BeforeFled), postfix: nameof(AfterCaught))
			& Hook(harmony, jail + "TriggerMaskCaught", prefix: nameof(BeforeFled), postfix: nameof(AfterCaught));
		_smapiLog.Info(ok ? "接上了 Stardew Thief：村民会对偷窃有反应。" : "Stardew Thief 有些地方没认出来（可能更新过），偷窃反应可能不全。");
	}

	private bool Hook(Harmony harmony, string method, string? prefix = null, string? postfix = null)
	{
		try
		{
			MethodInfo? target = AccessTools.Method(method);
			if (target == null)
			{
				_smapiLog.Warn("Stardew Thief：找不到 " + method);
				return false;
			}
			harmony.Patch(target,
				prefix: prefix == null ? null : new HarmonyMethod(typeof(ThiefWatcher), prefix),
				postfix: postfix == null ? null : new HarmonyMethod(typeof(ThiefWatcher), postfix));
			return true;
		}
		catch (Exception ex)
		{
			_smapiLog.Warn($"Stardew Thief：接 {method} 出错：{ex.Message}");
			return false;
		}
	}

	private static void Run(Action<ThiefWatcher> action)
	{
		if (_current is not ThiefWatcher w || !w.Active)
		{
			return;
		}
		try
		{
			action(w);
		}
		catch (Exception ex)
		{
			w._smapiLog.Warn("Stardew Thief 兼容出错：" + ex);
		}
	}

	private static void BeforeTry(NPC __1) => Run(w =>
	{
		w._attempt = __1;
		w._attemptMoney = Game1.player.Money;
	});

	private static void AfterSuccess(NPC __1) => Run(w => w.StartPickpocket(__1, caught: false, horse: false));

	/// <summary>失手一定是被对方发现了（白天心虚失手不走这里）。</summary>
	private static void AfterFail() => Run(w =>
	{
		if (w._attempt != null)
		{
			w.StartPickpocket(w._attempt, caught: true, horse: false);
		}
	});

	/// <summary>骑马扒窃认不出失手，先当得手，最后没拿到东西就不记。</summary>
	private static void AfterHorse(NPC __1) => Run(w => w.StartPickpocket(__1, caught: false, horse: true));

	private static void AfterSearch(List<Item> __0) => Run(w => w.AddLoot(__0));

	private static void BeforeSpotted(string __1) => Run(w => w.BeginCaught(__1));

	/// <summary>警戒值满了、或者被别的玩家发现：没有村民看见。</summary>
	private static void BeforeFled() => Run(w => w.BeginCaught(null));

	private static void AfterCaught() => Run(w => w.EndCaught());

	// ---------------- 扒窃 ----------------

	private void StartPickpocket(NPC npc, bool caught, bool horse)
	{
		if (_pending != null)
		{
			Finish(_pending, _pendingHorse);
		}
		GameLocation? here = npc.currentLocation ?? Game1.player.currentLocation;
		_pending = new TheftRecord
		{
			Kind = TheftRecord.Pickpocket,
			Day = Today,
			Time = Game1.timeOfDay,
			Location = here?.NameOrUniqueName ?? "",
			Victims = new List<string> { npc.Name },
			Caught = caught,
			CaughtBy = caught ? npc.Name : ""
		};
		_pendingHorse = horse;
		_pendingMoney = _attempt == npc ? _attemptMoney : Game1.player.Money;
		_pendingStart = Game1.ticks;
		_attempt = null;
	}

	private void OnUpdateTicked()
	{
		if (_pending == null || !Active)
		{
			return;
		}
		int waited = Game1.ticks - _pendingStart;
		if (waited < (_pendingHorse ? HorseSettleTicks : SettleTicks) || Game1.activeClickableMenu != null && waited < MaxWaitTicks)
		{
			return;
		}
		TheftRecord done = _pending;
		_pending = null;
		if (!done.Caught)
		{
			done.Coins = Math.Max(0, Game1.player.Money - _pendingMoney);
		}
		Finish(done, _pendingHorse);
	}

	private void OnInventoryChanged(InventoryChangedEventArgs e)
	{
		if (!Active || !e.IsLocalPlayer || _pending is not { Caught: false } p)
		{
			return;
		}
		foreach (Item item in e.Added)
		{
			if (StolenFrom(item, p.Victims[0]))
			{
				p.Items.Add(Describe(item, item.Stack));
			}
		}
		// 赃物之间能叠在一起，叠上去的只是数量变了。
		foreach (ItemStackSizeChange c in e.QuantityChanged)
		{
			if (c.NewSize > c.OldSize && StolenFrom(c.Item, p.Victims[0]))
			{
				p.Items.Add(Describe(c.Item, c.NewSize - c.OldSize));
			}
		}
	}

	private static bool StolenFrom(Item item, string npc) =>
		Flag(item, "IsStolen") && !Flag(item, "BurglaryLoot")
		&& (!item.modData.TryGetValue(Prefix + "StolenFrom", out string? from) || string.Equals(from, npc, StringComparison.OrdinalIgnoreCase));

	// ---------------- 入室 ----------------

	private void AddLoot(List<Item> items)
	{
		if (Game1.player.currentLocation is not GameLocation here || items.Count == 0)
		{
			return;
		}
		(TheftRecord record, List<Item> taken) = Burglary(here);
		foreach (Item item in items.Where(i => i != null))
		{
			record.Items.Add(Describe(item, item.Stack));
			taken.Add(item);
		}
	}

	private (TheftRecord, List<Item>) Burglary(GameLocation here)
	{
		string key = here.NameOrUniqueName;
		if (!_burglaries.TryGetValue(key, out var b))
		{
			b = _burglaries[key] = (new TheftRecord
			{
				Kind = TheftRecord.Burglary,
				Day = Today,
				Time = Game1.timeOfDay,
				Location = here.Name,
				Victims = TheftRules.BurglaryVictims(here.Name, Homes.OwnersOf(here.Name))
			}, new List<Item>());
		}
		return b;
	}

	/// <summary>它会在送人回农场之前调用；这时人还在屋里，记下是谁撞见的。</summary>
	private void BeginCaught(string? spotter)
	{
		if (Game1.player.currentLocation is not GameLocation here)
		{
			return;
		}
		Vector2 at = Game1.player.Tile;
		NPC? npc = spotter == null ? null : here.characters
			.Where(n => n.IsVillager && (string.Equals(n.Name, spotter, StringComparison.OrdinalIgnoreCase) || n.displayName == spotter))
			.OrderBy(n => Vector2.Distance(n.Tile, at))
			.FirstOrDefault();
		_caught = (here, npc, npc?.Tile ?? at);
	}

	/// <summary>罚完以后：被收走的赃物不算偷走（吃了核桃面包时只收走一半）。</summary>
	private void EndCaught()
	{
		if (_caught is not { } caught)
		{
			return;
		}
		_caught = null;
		(GameLocation here, NPC? spotter, Vector2 tile) = caught;
		(TheftRecord record, List<Item> taken) = Burglary(here);
		_burglaries.Remove(here.NameOrUniqueName);
		record.Caught = true;
		record.CaughtBy = spotter?.Name ?? "";
		record.Items = taken.Where(i => Game1.player.Items.Contains(i)).Select(i => Describe(i, i.Stack)).ToList();
		Finish(record, false, here, tile);
	}

	private void OnWarped(WarpedEventArgs e)
	{
		if (!Active || !e.IsLocalPlayer || e.OldLocation == null || !_burglaries.Remove(e.OldLocation.NameOrUniqueName, out var b))
		{
			return;
		}
		Finish(b.Record, false, e.OldLocation, Game1.player.Tile);
	}

	private void FlushAll()
	{
		if (_pending != null)
		{
			Finish(_pending, _pendingHorse);
			_pending = null;
		}
		foreach ((TheftRecord r, _) in _burglaries.Values.ToList())
		{
			Finish(r, false);
		}
		_burglaries.Clear();
	}

	// ---------------- 结算 ----------------

	private void Finish(TheftRecord r, bool horse, GameLocation? where = null, Vector2? tile = null)
	{
		if (!TheftRules.WorthRecording(r, horse))
		{
			return;
		}
		where ??= Game1.getLocationFromName(r.Location);
		_log.Add(r);
		Recorded?.Invoke(r);
		_smapiLog.Debug($"偷窃：{r.Kind} {string.Join("、", r.Victims)} 在 {r.Location}，{TheftText.Loot(r.Items, r.Coins)}，{(r.Caught ? "被 " + (r.CaughtBy.Length > 0 ? r.CaughtBy : "警戒") + " 抓住" : "没被发现")}。");
		if (where != null)
		{
			Report(r, where, tile ?? Game1.player.Tile);
		}
	}

	private void Report(TheftRecord r, GameLocation where, Vector2 tile)
	{
		string farmer = ActionPatches.Name(Game1.player);
		string loot = TheftText.Loot(r.Items, r.Coins);
		Dictionary<string, string> d = Observation.Map(("caught", r.Caught ? "true" : "false"), ("loot", loot), ("value", r.Value.ToString()));
		if (r.Kind == TheftRecord.Pickpocket)
		{
			string victim = r.Victims[0];
			NPC? npc = where.getCharacterFromName(victim);
			string summary = r.Caught ? $"{farmer}想偷{victim}身上的东西，被{victim}当场发现了。" : $"{farmer}从{victim}身上偷走了{(loot.Length > 0 ? loot : "东西")}。";
			_awareness.Record(new Observation("pickpocket", farmer, victim, where.NameOrUniqueName, summary, npc?.Tile ?? tile, d, Exclude: r.Caught ? null : new[] { victim }));
		}
		else if (r.Caught && where.getCharacterFromName(r.CaughtBy) is NPC spotter)
		{
			// 没被村民撞见说明屋里没人看见，主人只能事后发现少了东西（见下面的 NoticedLoss）。
			d["owners"] = string.Join(",", r.Victims);
			d["place"] = where.Name;
			string owners = r.Victims.Count > 0 ? string.Join("、", r.Victims) + "家" : "这户人家";
			_awareness.Record(new Observation("burglary", farmer, spotter.Name, where.NameOrUniqueName, $"{farmer}溜进{owners}翻东西，被{spotter.Name}撞了个正着。", spotter.Tile, d));
		}
		LoseFriendship(r);
		if (TheftRules.LossNoticed(r))
		{
			NoticedLoss?.Invoke(r, AwarenessSystem.PlaceName(where));
		}
	}

	/// <summary>当场被抓：Stardew Thief 自己不动好感，这里扣一次。</summary>
	private void LoseFriendship(TheftRecord r)
	{
		if (Game1.player is not Farmer player)
		{
			return;
		}
		foreach ((string name, int amount) in TheftRules.FriendshipHits(r, Math.Clamp(_config().CaughtStealingPenalty, 0, 250)))
		{
			if (player.friendshipData.ContainsKey(name) && Game1.getCharacterFromName(name) is NPC npc)
			{
				player.changeFriendship(-amount, npc);
			}
		}
	}

	/// <summary>调试：假装刚偷了一次（不装 Stardew Thief 也能用）。</summary>
	public string Simulate(string kind, string? npcName, bool caught)
	{
		if (!Context.IsWorldReady || Game1.player?.currentLocation is not GameLocation here)
		{
			return "进了存档才能用。";
		}
		TheftRecord r = new TheftRecord { Kind = kind == "burglary" ? TheftRecord.Burglary : TheftRecord.Pickpocket, Day = Today, Time = Game1.timeOfDay, Location = here.NameOrUniqueName, Caught = caught };
		if (r.Kind == TheftRecord.Pickpocket)
		{
			NPC? npc = string.IsNullOrWhiteSpace(npcName) ? here.characters.FirstOrDefault(n => n.IsVillager) : Game1.getCharacterFromName(npcName);
			if (npc == null)
			{
				return "附近没有村民。";
			}
			r.Victims.Add(npc.Name);
			r.Location = npc.currentLocation?.NameOrUniqueName ?? r.Location;
			r.CaughtBy = caught ? npc.Name : "";
			r.Coins = caught ? 0 : 120;
			here = npc.currentLocation ?? here;
		}
		else
		{
			r.Location = here.Name;
			r.Victims = TheftRules.BurglaryVictims(here.Name, Homes.OwnersOf(here.Name));
			r.CaughtBy = caught ? here.characters.FirstOrDefault(n => n.IsVillager)?.Name ?? "" : "";
			if (!caught)
			{
				r.Items.Add(new StolenItem { Id = "(O)72", Name = ItemRegistry.Create("(O)72").DisplayName, Price = 250 });
			}
		}
		Finish(r, false, here, Game1.player.Tile);
		return $"记下了：{r.Kind}，失主 {string.Join("、", r.Victims)}，{(caught ? "被抓" : "没被发现")}。";
	}

	private static bool Flag(Item? item, string key) => item != null && item.modData.TryGetValue(Prefix + key, out string? v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

	/// <summary>Stardew Thief 会给赃物的名字加前缀，记原来的名字。</summary>
	private static StolenItem Describe(Item item, int stack) => new StolenItem
	{
		Id = item.QualifiedItemId ?? item.ItemId ?? "",
		Name = ItemRegistry.GetData(item.QualifiedItemId)?.DisplayName is { Length: > 0 } plain ? plain : item.DisplayName ?? "",
		Stack = Math.Max(1, stack),
		Quality = item.Quality,
		Price = Math.Max(0, item.salePrice())
	};

	private static int Today => Math.Max(1, Game1.Date.TotalDays + 1);
}
