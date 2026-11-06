using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Favors;
using AISV.Core.Scenes;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Quests;
using SObject = StardewValley.Object;

namespace AISV.Game.Favors;

/// <summary>
/// 托付和物品请求在游戏里的部分：登记、任务日志、送礼或交付完成、完成后的小场景。
/// 只在主线程用；物品请求的登记从后台线程过来时排到主线程。
/// </summary>
public sealed class FavorService : IItemRequestDesk
{
	private const string QuestsAsset = "Data/Quests";

	private const string EventsPrefix = "Data/Events/";

	private static bool _eventHooks;

	private readonly Func<ModConfig> _config;

	private readonly IModHelper _helper;

	private readonly Relationships _relationships;

	private readonly ILog _log;

	private readonly ItemRequestScenes _scenes;

	private List<(string Id, string? Display, string? Internal)>? _objects;

	public FavorService(Func<ModConfig> config, Func<DocumentStore?> store, IModHelper helper, Relationships relationships, ILog log)
	{
		_config = config;
		_helper = helper;
		_relationships = relationships;
		_log = log;
		Favors = new FavorBook(store);
		Requests = new ItemRequestBook(store, () => _config().WishRestDays);
		_scenes = new ItemRequestScenes(Path.Combine(helper.DirectoryPath, "assets", "item_request_scenes"));
		RegisterEventHooks();
	}

	public FavorBook Favors { get; }

	public ItemRequestBook Requests { get; }

	/// <summary>托付办成或请求完成后，这位村民今天的对话要重新生成。</summary>
	public Action<string>? Changed { get; set; }

	private static int Today => GameState.Today.TotalDays;

	// ---- 托付 ----

	public void OnReply(NPC npc, NpcReaction reaction)
	{
		Favors.CompleteByTalk(npc.Name, Today);
		RequestedFavor? r = reaction.RequestedFavor;
		if (r == null || string.IsNullOrWhiteSpace(r.Description))
		{
			return;
		}
		string itemId = FavorBook.NormalizeType(r.Type) == FavorBook.BringItem ? ItemMatcher.ExactId(r.Target, Objects()) ?? "" : "";
		string? why = Favors.Register(npc.Name, r.Type, r.Description, r.Target, itemId, GameState.Today);
		_log.Debug(why == null ? $"{npc.Name} 托农夫：{r.Description}（{r.Target}）。" : $"{npc.Name} 托的事没记：{why}。");
	}

	/// <summary>收下礼物以后：办成托付补好感，送对了请求的东西就算完成。</summary>
	public void OnGift(NPC npc, SObject item, int gain)
	{
		NpcFavor? done = Favors.CompleteByGift(npc.Name, item.QualifiedItemId, item.DisplayName, item.Name, Today);
		if (done != null)
		{
			int bonus = FavorBook.GiftBonus(gain);
			if (bonus > 0)
			{
				_relationships.Add(npc.Name, bonus);
			}
			_log.Debug($"{npc.Name} 托的「{done.Aim}」送到了（补好感 {bonus}）。");
		}
		CompleteRequest(f => Same(f.Asker, npc.Name) && ItemRequestBook.Matches(f, item.QualifiedItemId, item.ItemId));
	}

	public void OnItemOffered(NPC npc, Item item) => CompleteRequest(f => Same(f.Asker, npc.Name) && ItemRequestBook.Matches(f, item.QualifiedItemId, item.ItemId));

	public void OnQuestDelivered(Quest quest) => CompleteRequest(f => Same(f.JournalId, quest.id.Value));

	/// <summary>信的世界信息：还没办完的托付和请求，最多 3 条。</summary>
	public List<string> OpenFor(string npc) => Favors.OpenDescriptions(npc)
		.Concat(Requests.For(npc).Where(f => f.IsActive).Select(f => $"{f.Amount}×{f.WantedItemName}"))
		.Take(3).ToList();

	public string FavorBlock(string npc) => Favors.PromptBlock(npc, Today);

	public string RequestBlock(string npc) => Requests.PromptBlock(npc, Today);

	public void OnDayStarted()
	{
		Favors.Expire(Today);
		if (Requests.Expire(Today).Count > 0)
		{
			_helper.GameContent.InvalidateAllLocales(QuestsAsset);
		}
	}

	/// <summary>进存档：任务日志里缺的补上，游戏已经记着交付了的改成完成。</summary>
	public void OnSaveLoaded()
	{
		_objects = null;
		if (Game1.player == null)
		{
			return;
		}
		foreach (ItemRequestFavor f in Requests.All.Where(f => f.IsActive).ToList())
		{
			string key = "questComplete_" + f.JournalId;
			if (Game1.player.activeDialogueEvents.ContainsKey(key) || Game1.player.previousActiveDialogueEvents.ContainsKey(key))
			{
				CompleteRequest(x => x == f);
			}
			else if (!Game1.player.hasQuest(f.JournalId))
			{
				AddQuest(f, hud: false);
			}
		}
	}

	/// <summary>调试：state | favor 村民 东西 | request 村民 [模板] | complete 村民。</summary>
	public string Command(string[] args)
	{
		string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "state";
		NPC? npc = args.Length > 1 ? Game1.getCharacterFromName(args[1]) : null;
		switch (verb)
		{
			case "favor" when npc != null && args.Length > 2:
			{
				string target = string.Join(" ", args.Skip(2));
				return Favors.Register(npc.Name, FavorBook.BringItem, $"帮忙带{target}", target, ItemMatcher.ExactId(target, Objects()) ?? "", GameState.Today) ?? "记下了。";
			}
			case "request" when npc != null:
			{
				int hearts = _relationships.EffectiveHearts(npc.Name);
				ItemRequestPlan? plan = args.Length > 2 ? new ItemRequestPlan { StageRef = args[2] } : null;
				ItemRequestCandidate? c = Candidate(npc.Name, hearts, true, false, plan);
				if (c == null)
				{
					return "现在托不了（已经有一件、在冷却，或者没有能用的模板）。";
				}
				Activate(c, $"能帮我找{c.Amount}个{c.WantedItemName}吗？", "", "");
				return $"{c.Scene.TemplateId} → {c.Amount}×{c.WantedItemName}（{c.JournalId}）";
			}
			case "complete" when npc != null:
			{
				int before = Requests.All.Count(f => f.IsCompleted);
				CompleteRequest(f => f.IsActive && Same(f.Asker, npc.Name));
				return Requests.All.Count(f => f.IsCompleted) > before ? "完成了。" : "这位没有进行中的请求。";
			}
			default:
				return string.Join("\n", Favors.All.Select(f => $"托付 {f.Asker}：{f.Ask}（{f.Aim}，{f.Phase}）")
					.Concat(Requests.All.Select(f => $"请求 {f.Asker}：{f.Amount}×{f.WantedItemName} {f.Phase} 场景 {f.StageRef}@{f.StagePlace}{(f.StagePlayed ? " 已播" : "")}"))
					.Concat(Game1.player.questLog.Where(q => q.id.Value?.StartsWith("Aisv.ItemRequest.", StringComparison.Ordinal) == true)
						.Select(q => $"任务 {q.id.Value}「{q.GetName()}」：{(q is ItemDeliveryQuest d ? $"{d.target.Value} {d.ItemId.Value}×{d.number.Value}" : q.GetType().Name)}{(q.completed.Value ? " 已完成" : "")}"))
					.DefaultIfEmpty("没有。"));
		}
	}

	// ---- 物品请求 ----

	public bool CanAsk(string npc, int hearts, bool adultPartner, bool child) => Requests.CanAsk(npc, Today) && _scenes.Eligible(hearts, adultPartner, child).Count > 0;

	public string PlanningOptions(int hearts, bool adultPartner, bool child) => _scenes.PlanningOptions(hearts, adultPartner, child);

	public ItemRequestCandidate? Candidate(string npc, int hearts, bool adultPartner, bool child, ItemRequestPlan? plan) =>
		Requests.Candidate(_scenes, npc, hearts, adultPartner, child, plan, SaveData.FolderName(), Today, DisplayName);

	public void Activate(ItemRequestCandidate candidate, string requestText, string line1, string line2, string handover = "") => MainThread.Run(() =>
	{
		ItemRequestFavor? f = Requests.Activate(candidate, requestText, line1, line2, Today, handover);
		if (f != null)
		{
			AddQuest(f, hud: true);
			_log.Debug($"{f.Asker} 托农夫找 {f.WantedItemName}（{f.JournalId}）。");
		}
	});

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (e.NameWithoutLocale.IsEquivalentTo(QuestsAsset))
		{
			List<ItemRequestFavor> quests = Requests.All.Where(f => !string.IsNullOrWhiteSpace(f.JournalId) && !string.Equals(f.Phase, "cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
			if (quests.Count > 0)
			{
				e.Edit(a =>
				{
					IDictionary<string, string> data = a.AsDictionary<string, string>().Data;
					quests.ForEach(f => data[f.JournalId] = ItemRequestBook.QuestData(f, NpcDisplay(f.Asker)));
				}, AssetEditPriority.Late);
			}
			return;
		}
		string name = e.NameWithoutLocale.BaseName;
		if (!name.StartsWith(EventsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string location = name[EventsPrefix.Length..];
		List<(string Key, string Script)> scenes = WaitingScenes(location);
		if (scenes.Count > 0)
		{
			e.Edit(a =>
			{
				IDictionary<string, string> data = a.AsDictionary<string, string>().Data;
				scenes.ForEach(s => data[s.Key] = s.Script);
			}, AssetEditPriority.Late);
		}
	}

	private void AddQuest(ItemRequestFavor f, bool hud)
	{
		try
		{
			if (Game1.player.hasQuest(f.JournalId))
			{
				return;
			}
			PrimeQuestData(f);
			Game1.player.addQuest(f.JournalId);
			if (hud && Game1.player.hasQuest(f.JournalId))
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("itemRequest.questAdded", "{{npc}}托你找{{item}}，记进任务日志了。", ("npc", NpcDisplay(f.Asker)), ("item", f.WantedItemName)), HUDMessage.newQuest_type));
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"{f.Asker} 的请求加不进任务日志：{ex.Message}");
		}
	}

	/// <summary>任务数据直接写进已经读好的 Data/Quests（游戏可能不会马上重新读这个资源）。</summary>
	private void PrimeQuestData(ItemRequestFavor f)
	{
		string data = ItemRequestBook.QuestData(f, NpcDisplay(f.Asker));
		_helper.GameContent.InvalidateAllLocales(QuestsAsset);
		try
		{
			_helper.GameContent.Load<Dictionary<string, string>>(QuestsAsset)[f.JournalId] = data;
		}
		catch (Exception ex)
		{
			_log.Warn($"写不进任务数据：{ex.Message}");
		}
		try
		{
			Game1.content.Load<Dictionary<string, string>>("Data\\Quests")[f.JournalId] = data;
		}
		catch (Exception ex)
		{
			_log.Warn($"写不进游戏的任务数据：{ex.Message}");
		}
	}

	private void CompleteRequest(Func<ItemRequestFavor, bool> match)
	{
		ItemRequestFavor? f = Requests.Complete(match, Today);
		if (f == null)
		{
			return;
		}
		_helper.GameContent.InvalidateAllLocales(QuestsAsset);
		_log.Debug($"{f.Asker} 要的 {f.WantedItemName} 送到了。");
		Changed?.Invoke(f.Asker);
		StartOrQueueScene(f);
	}

	private void StartOrQueueScene(ItemRequestFavor f)
	{
		if (string.IsNullOrWhiteSpace(f.StageScript))
		{
			return;
		}
		GameLocation? here = Game1.currentLocation;
		if (here != null && Same(here.NameOrUniqueName, f.StagePlace) && here.currentEvent == null && Game1.activeClickableMenu == null && !Game1.eventUp)
		{
			if (!EventPlacement.TrySafe(f.StageScript, (x, y) => Usable(here, x, y), out string script))
			{
				Requests.MarkScenePlayed(f.Id);
				_log.Warn($"{f.Asker} 的完成场景在 {f.StagePlace} 找不到站的地方，跳过。");
				return;
			}
			try
			{
				here.startEvent(new Event(script, Game1.player));
				return;
			}
			catch (Exception ex)
			{
				_log.Warn($"{f.Asker} 的完成场景放不了：{ex.Message}");
			}
		}
		_helper.GameContent.InvalidateAllLocales(EventsPrefix + f.StagePlace);
		Game1.addHUDMessage(new HUDMessage(Tr.Get("itemRequest.sceneQueued", "{{npc}}好像在{{location}}等你。", ("npc", NpcDisplay(f.Asker)), ("location", PlaceDisplay(f.StagePlace))), HUDMessage.newQuest_type));
	}

	private List<(string, string)> WaitingScenes(string location)
	{
		List<(string, string)> list = new List<(string, string)>();
		GameLocation? place = Game1.getLocationFromName(location);
		foreach (ItemRequestFavor f in Requests.All.Where(f => f.SceneWaiting && Same(f.StagePlace, location)).ToList())
		{
			if (place == null || !EventPlacement.TrySafe(f.StageScript, (x, y) => Usable(place, x, y), out string script))
			{
				Requests.MarkScenePlayed(f.Id);
				_log.Warn($"{f.Asker} 的完成场景在 {location} 找不到站的地方，跳过。");
				continue;
			}
			list.Add((ItemRequestBook.EventKey(f), script));
		}
		return list;
	}

	private static bool Usable(GameLocation l, int x, int y)
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

	private void RegisterEventHooks()
	{
		if (_eventHooks)
		{
			return;
		}
		_eventHooks = true;
		Event.RegisterPrecondition(ItemRequestBook.SceneArmed, (_, _, args) => Requests.All.Any(f => Same(f.Id, Arg(args, ItemRequestBook.SceneArmed, 0)) && f.SceneWaiting));
		Event.RegisterPrecondition(ItemRequestBook.SceneTime, (_, _, args) =>
			int.TryParse(Arg(args, ItemRequestBook.SceneTime, 0), out int min) && int.TryParse(Arg(args, ItemRequestBook.SceneTime, 1), out int max) && Game1.timeOfDay >= min && Game1.timeOfDay <= max);
		Event.RegisterCommand(ItemRequestBook.SceneComplete, (Event ev, string[] args, EventContext _) =>
		{
			ItemRequestFavor? f = Requests.All.FirstOrDefault(x => Same(x.Id, Arg(args, ItemRequestBook.SceneComplete, 0)));
			if (f != null)
			{
				Requests.MarkScenePlayed(f.Id);
				_helper.GameContent.InvalidateAllLocales(EventsPrefix + f.StagePlace);
			}
			ev.CurrentCommand++;
		});
	}

	private static string Arg(string[] args, string command, int index)
	{
		int skip = args.Length > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
		return skip + index < args.Length ? args[skip + index] : "";
	}

	private List<(string Id, string? Display, string? Internal)> Objects()
	{
		if (_objects != null)
		{
			return _objects;
		}
		List<(string, string?, string?)> list = new List<(string, string?, string?)>();
		ObjectDataDefinition objects = ItemRegistry.GetObjectTypeDefinition();
		foreach (string id in objects.GetAllIds())
		{
			try
			{
				ParsedItemData? data = objects.GetData(id);
				if (data is { IsErrorItem: false })
				{
					list.Add((data.QualifiedItemId, data.DisplayName, data.InternalName));
				}
			}
			catch
			{
			}
		}
		return _objects = list;
	}

	private static string DisplayName(string qualifiedId, string fallback)
	{
		ParsedItemData? data = ItemRegistry.GetData(qualifiedId);
		return data is { IsErrorItem: false } && !string.IsNullOrWhiteSpace(data.DisplayName) ? data.DisplayName.Trim() : fallback;
	}

	private static string PlaceDisplay(string location)
	{
		string? name = Game1.getLocationFromName(location)?.DisplayName;
		return string.IsNullOrWhiteSpace(name) ? location : name;
	}

	private static string NpcDisplay(string npc) => Game1.getCharacterFromName(npc)?.displayName ?? npc;

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
