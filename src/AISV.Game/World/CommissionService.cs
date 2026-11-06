using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Game.Dialogue;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.World;

/// <summary>
/// 委托：村民每天早上在委托板上贴委托，你在世界页的委托板上接下或放弃。
/// 送物品、采集、挖矿：拿着东西对发布者按动作键交付；拜访：白天去他家，他在家就算完成。
/// 板上的话由 AI 用发布者的口吻写，失败用模板。
/// </summary>
public sealed class CommissionService
{
	public const string NoteRequest = "quest_text";

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly Ecosystem _eco;

	private readonly MarketService _market;

	private readonly Func<string, bool> _npcEnabled;

	private readonly ILog _log;

	private readonly HashSet<string> _visitHintShown = new HashSet<string>(StringComparer.Ordinal);

	public CommissionService(Func<ModConfig> config, Func<DocumentStore?> store, Ecosystem eco, MarketService market, Func<string, bool> npcEnabled, ILog log)
	{
		_config = config;
		_store = store;
		_eco = eco;
		_market = market;
		_npcEnabled = npcEnabled;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Personality { get; set; }

	/// <summary>村民说一段不走 AI 流程的话。</summary>
	public Action<NPC, string>? Say { get; set; }

	private void Speak(NPC npc, string text)
	{
		if (Say != null)
		{
			Say(npc, text);
			return;
		}
		npc.CurrentDialogue.Push(new StardewValley.Dialogue(npc, null, text));
		Game1.drawDialogue(npc);
	}

	public int Version { get; private set; }

	/// <summary>一张委托交付了，发布者付了酬劳。</summary>
	public Action<Commission>? Paid { get; set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.WorldModuleOn && c.JobBoardOn;
		}
	}

	private CommissionLog? Log => _store()?.Get(CommissionLog.Path, CommissionLog.Doc);

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public IReadOnlyList<Commission> All() => Log?.Items.ToList() ?? new List<Commission>();

	public IReadOnlyList<Commission> Open() => All().Where(c => c.State == CommissionStates.Open).ToList();

	public IReadOnlyList<Commission> Active() => All().Where(c => c.State == CommissionStates.Active).ToList();

	public Commission? Find(string id) => All().FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

	public static int DaysLeft(Commission c) => c.LastDay - Today;

	// ---------------- 生命周期 ----------------

	/// <summary>物品或发布者没了的（mod 删了）直接下架，不扣好感。</summary>
	public void OnSaveLoaded()
	{
		_visitHintShown.Clear();
		foreach (Commission c in All().Where(c => CommissionStates.IsOpenOrActive(c.State) && !StillValid(c)))
		{
			c.State = CommissionStates.Withdrawn;
			c.ClosedDay = Today;
			MarkDirty();
			_log.Info($"委托 {c.Id} 下架了（{c.Poster} 或 {c.ItemId} 不在了）。");
		}
		Version++;
	}

	public void OnReturnedToTitle() => _visitHintShown.Clear();

	public void OnDayStarted()
	{
		if (Log is not CommissionLog log || !Enabled || !Context.IsMainPlayer)
		{
			return;
		}
		try
		{
			ExpireOverdue(log);
			if (log.LastPostedDay != Today)
			{
				log.LastPostedDay = Today;
				PostNew(Math.Clamp(_config().JobsPostedDaily, 0, CommissionRules.MaxOpen), null, null);
			}
			log.Items.RemoveAll(c => !CommissionStates.IsOpenOrActive(c.State) && c.ClosedDay >= 0 && Today - c.ClosedDay > CommissionRules.ClosedKeepDays);
			MarkDirty();
		}
		catch (Exception ex)
		{
			_log.Warn("委托：早上更新出错：" + ex);
		}
	}

	/// <summary>每 10 分钟：拜访的委托，你在他家里、他也在就完成。</summary>
	public void CheckVisits()
	{
		if (Log == null || !Enabled || Game1.player?.currentLocation is not GameLocation here)
		{
			return;
		}
		foreach (Commission c in Active().Where(c => c.Kind == CommissionKinds.Visit && string.Equals(c.Location, here.NameOrUniqueName, StringComparison.OrdinalIgnoreCase)))
		{
			if (Game1.timeOfDay < CommissionRules.VisitStartTime || Game1.timeOfDay > CommissionRules.VisitEndTime)
			{
				continue;
			}
			NPC? npc = here.characters.FirstOrDefault(n => n.Name == c.Poster);
			if (npc == null)
			{
				if (_visitHintShown.Add(c.Id))
				{
					Game1.addHUDMessage(new HUDMessage(Tr.Get("commission.visitNotHome", "{{npc}}不在家，晚点再来吧。", ("npc", NpcName(c.Poster))), HUDMessage.newQuest_type));
				}
				continue;
			}
			Complete(c);
			npc.doEmote(20);
		}
	}

	// ---------------- 玩家 ----------------

	public (bool Ok, string Message) Accept(string id)
	{
		Commission? c = Find(id);
		if (c == null || c.State != CommissionStates.Open)
		{
			return (false, Tr.Get("commission.gone", "这条委托已经不在了。"));
		}
		if (Active().Count >= CommissionRules.MaxActive)
		{
			return (false, Tr.Get("commission.tooMany", "你手上已经有 {{count}} 条委托了，先做完再接。", ("count", CommissionRules.MaxActive)));
		}
		c.State = CommissionStates.Active;
		c.AcceptedDay = Today;
		Changed();
		_log.Info($"接下委托 {c.Id}（{c.Kind} {c.ItemId}×{c.Count}，{c.Poster}）。");
		return (true, Tr.Get("commission.accepted", "接下了{{npc}}的委托。", ("npc", NpcName(c.Poster))));
	}

	public (bool Ok, string Message) Abandon(string id)
	{
		Commission? c = Find(id);
		if (c == null || c.State != CommissionStates.Active)
		{
			return (false, Tr.Get("commission.gone", "这条委托已经不在了。"));
		}
		c.State = CommissionStates.Abandoned;
		c.ClosedDay = Today;
		c.PenaltyPoints = CommissionRules.AbandonPenalty;
		ChangeFriendship(c.Poster, -CommissionRules.AbandonPenalty);
		Changed();
		_log.Info($"放弃委托 {c.Id}（{c.Poster} -{CommissionRules.AbandonPenalty}）。");
		return (true, Tr.Get("commission.abandoned", "放弃了{{npc}}的委托。{{npc}}有点失望。", ("npc", NpcName(c.Poster))));
	}

	public static int CountInInventory(Farmer player, string itemId) => player.Items.Where(i => i != null && i.QualifiedItemId == itemId && i is not Tool).Sum(i => i.Stack);

	/// <summary>拿着东西对他按动作键：这个人有没有等着这样东西的委托。</summary>
	public Commission? WaitingFor(NPC npc, Item? held) =>
		held == null || !Enabled ? null : Active().FirstOrDefault(c => CommissionKinds.NeedsItems(c.Kind) && c.ItemId == held.QualifiedItemId && string.Equals(c.Poster, npc.Name, StringComparison.OrdinalIgnoreCase));

	/// <summary>交付：先扣品质低的，付钱、加好感，他回一句谢谢。东西不够返回 false。</summary>
	public bool Deliver(Commission c, Farmer player, NPC npc)
	{
		if (c.State != CommissionStates.Active || !CommissionKinds.NeedsItems(c.Kind) || CountInInventory(player, c.ItemId) < c.Count)
		{
			return false;
		}
		int left = c.Count;
		foreach (Item item in player.Items.Where(i => i != null && i.QualifiedItemId == c.ItemId).OrderBy(i => i.Quality).ToList())
		{
			int take = Math.Min(left, item.Stack);
			item.Stack -= take;
			left -= take;
			if (item.Stack <= 0)
			{
				player.removeItemFromInventory(item);
			}
			if (left <= 0)
			{
				break;
			}
		}
		string itemName = ItemName(c);
		Complete(c);
		player.completelyStopAnimatingOrDoingAction();
		npc.currentLocation?.localSound("give_gift");
		npc.faceTowardFarmerForPeriod(3000, 4, faceAway: false, player);
		Speak(npc, TextTools.CleanForGame(c.Thanks.Length > 0 ? c.Thanks : Tr.Get("commission.thanks", "谢谢你！这些{{item}}正是我需要的，说好的酬劳给你。", ("item", itemName))));
		return true;
	}

	private void Complete(Commission c)
	{
		c.State = CommissionStates.Done;
		c.ClosedDay = Today;
		Game1.player.Money += c.RewardGold;
		ChangeFriendship(c.Poster, c.RewardFriendship);
		Paid?.Invoke(c);
		Game1.playSound("reward");
		Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(Tr.Get("commission.done", "{{npc}}的委托完成了：+{{gold}}g", ("npc", NpcName(c.Poster)), ("gold", c.RewardGold))), HUDMessage.achievement_type));
		Changed();
		_log.Info($"完成委托 {c.Id}（{c.Kind}，{c.Poster}）：+{c.RewardGold}g，好感 +{c.RewardFriendship}。");
	}

	private void ExpireOverdue(CommissionLog log)
	{
		foreach (Commission c in log.Items.Where(c => CommissionStates.IsOpenOrActive(c.State) && c.LastDay < Today))
		{
			c.ClosedDay = Today;
			if (c.State == CommissionStates.Open)
			{
				c.State = CommissionStates.Withdrawn;
				continue;
			}
			c.State = CommissionStates.Expired;
			c.PenaltyPoints = CommissionRules.ExpiryPenalty;
			ChangeFriendship(c.Poster, -CommissionRules.ExpiryPenalty);
			Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(Tr.Get("commission.expired", "{{npc}}的委托过期了，{{npc}}有些失望。", ("npc", NpcName(c.Poster)))), HUDMessage.error_type));
			_log.Info($"委托 {c.Id} 过期了（{c.Poster} -{CommissionRules.ExpiryPenalty}）。");
		}
		Version++;
	}

	private static void ChangeFriendship(string npc, int points)
	{
		NPC? n = Game1.getCharacterFromName(npc);
		if (n != null && Game1.player != null)
		{
			Game1.player.changeFriendship(points, n);
		}
	}

	// ---------------- 贴 ----------------

	/// <summary>贴新委托：最多 count 条，板上没人接的不超过 4 条。kind、poster 为空时自动挑。</summary>
	public IReadOnlyList<Commission> PostNew(int count, string? kind, string? poster, string? item = null)
	{
		List<Commission> posted = new List<Commission>();
		if (Log is not CommissionLog log)
		{
			return posted;
		}
		Random rng = new Random(unchecked((int)Game1.uniqueIDForThisGame + Today * 7919 + log.NextNumber * 31));
		for (int i = 0; i < count && Open().Count < CommissionRules.MaxOpen; i++)
		{
			Commission? c = null;
			for (int attempt = 0; attempt < 6 && c == null; attempt++)
			{
				c = TryBuild(log, rng, kind ?? CommissionRules.PickKind(rng), poster, realistic: kind == null, item);
			}
			if (c == null)
			{
				continue;
			}
			log.Items.Add(c);
			posted.Add(c);
			_log.Info($"贴出委托 {c.Id}：{c.Kind} {c.ItemId}×{c.Count} {c.Location}，{c.Poster}，{c.RewardGold}g，到第 {c.LastDay} 天。");
			RequestText(c);
		}
		if (posted.Count > 0)
		{
			Changed();
		}
		return posted;
	}

	/// <param name="realistic">自动贴的为 true：第一年只要到今天已经能收的作物，开档几天不会要花椰菜、樱桃。</param>
	private Commission? TryBuild(CommissionLog log, Random rng, string kind, string? forcedPoster, bool realistic, string? forcedItem = null)
	{
		string season = Game1.currentSeason ?? "spring";
		string itemId = "";
		int count = 1;
		int gold;
		switch (kind)
		{
			case CommissionKinds.Deliver:
			{
				List<string> crops = _market.MarketCrops()
					.Where(c => c.Seasons.Contains(season) && !CommissionRules.NotTownGrown.Contains(c.HarvestItemId) && (!realistic || CommissionRules.RealisticCrop(Game1.year, Game1.dayOfMonth, c.IsFruitTree, c.DaysToGrow)))
					.Select(c => c.HarvestItemId).ToList();
				if (forcedItem != null)
				{
					crops = ItemRegistry.GetData(forcedItem) != null ? new List<string> { ItemRegistry.QualifyItemId(forcedItem) } : new List<string>();
				}
				if (crops.Count == 0)
				{
					return null;
				}
				List<string> hot = (_market.State?.HotItems ?? new List<string>()).Where(crops.Contains).ToList();
				itemId = hot.Count > 0 && rng.Next(100) < 40 ? hot[rng.Next(hot.Count)] : crops[rng.Next(crops.Count)];
				int price = BasePrice(itemId);
				count = CommissionRules.DeliverCount(price);
				gold = CommissionRules.DeliverReward(price);
				break;
			}
			case CommissionKinds.Gather:
			{
				if (rng.Next(100) < 60 && CommissionRules.Forage.TryGetValue(season, out string[]? pool))
				{
					itemId = pool[rng.Next(pool.Length)];
					count = rng.Next(2, 6);
				}
				else
				{
					(string id, int min, int max) = CommissionRules.Materials[rng.Next(CommissionRules.Materials.Count)];
					itemId = id;
					count = CommissionRules.RoundMaterial(rng.Next(min, max + 1), min);
				}
				gold = CommissionRules.GatherReward(BasePrice(itemId), count);
				break;
			}
			case CommissionKinds.Mine:
			{
				int depth = Game1.player?.deepestMineLevel ?? 0;
				List<MineGood> goods = CommissionRules.MineGoods.Where(g => depth >= g.Level).ToList();
				if (goods.Count == 0)
				{
					return null;
				}
				MineGood g = goods[rng.Next(goods.Count)];
				itemId = g.Id;
				count = rng.Next(g.Min, g.Max + 1);
				gold = CommissionRules.MineReward(g.Unit, BasePrice(itemId), count);
				break;
			}
			case CommissionKinds.Visit:
				gold = CommissionRules.VisitReward(rng);
				break;
			default:
				return null;
		}
		string? poster = forcedPoster ?? PickPoster(rng, kind, realistic);
		if (poster == null)
		{
			return null;
		}
		string location = "";
		if (kind == CommissionKinds.Visit)
		{
			location = VisitLocation(poster) ?? "";
			if (location.Length == 0)
			{
				return null;
			}
		}
		if (All().Any(c => CommissionStates.IsOpenOrActive(c.State) && (c.Poster == poster || (itemId.Length > 0 && c.ItemId == itemId))))
		{
			return null;
		}
		int days = rng.Next(2, 5);
		Commission made = new Commission
		{
			Id = "c" + log.NextNumber++,
			Kind = kind,
			Poster = poster,
			ItemId = itemId,
			Count = count,
			Location = location,
			RewardGold = Math.Min(CommissionRules.MaxGold, gold),
			RewardFriendship = kind == CommissionKinds.Visit ? CommissionRules.VisitFriendship : CommissionRules.ItemFriendship,
			PostedDay = Today,
			LastDay = Today + days - 1,
			State = CommissionStates.Open
		};
		made.Text = CommissionRules.TemplateText(kind, ItemName(made));
		return made;
	}

	public static int BasePrice(string itemId) => Game1.objectData != null && Game1.objectData.TryGetValue(itemId.Replace("(O)", ""), out StardewValley.GameData.Objects.ObjectData? data) ? data.Price : 0;

	/// <summary>你进得去他家吗：法师塔要看过初见事件，单独的卧室要 2 心。</summary>
	private static bool CanEnterHome(string npc, string home)
	{
		Farmer? player = Game1.player;
		if (player == null)
		{
			return false;
		}
		if (home.Equals("WizardHouse", StringComparison.OrdinalIgnoreCase) && !player.eventsSeen.Contains("112"))
		{
			return false;
		}
		return !home.EndsWith("Room", StringComparison.OrdinalIgnoreCase) || (player.friendshipData.TryGetValue(npc, out Friendship? f) && f.Points >= 500);
	}

	/// <summary>开启的、能社交的成年人；偏好名单 70%。</summary>
	private string? PickPoster(Random rng, string kind, bool realistic)
	{
		HashSet<string> busy = new HashSet<string>(All().Where(c => CommissionStates.IsOpenOrActive(c.State)).Select(c => c.Poster), StringComparer.OrdinalIgnoreCase);
		List<string> eligible = new List<string>();
		foreach (NPC npc in Utility.getAllVillagers())
		{
			if (npc == null || !npc.CanSocialize || CommissionRules.RemotePosters.Contains(npc.Name) || busy.Contains(npc.Name) || ChildSafety.IsChild(npc.Name, npc.Age) || !_npcEnabled(npc.Name))
			{
				continue;
			}
			if (kind == CommissionKinds.Visit && (VisitLocation(npc.Name) is not string home || (realistic && !CanEnterHome(npc.Name, home))))
			{
				continue;
			}
			eligible.Add(npc.Name);
		}
		if (eligible.Count == 0)
		{
			return null;
		}
		IReadOnlyList<string> preferred = kind switch
		{
			CommissionKinds.Mine => CommissionRules.PreferredMine,
			CommissionKinds.Gather => CommissionRules.PreferredGather,
			CommissionKinds.Deliver => CommissionRules.PreferredDeliver,
			_ => Array.Empty<string>()
		};
		List<string> pref = preferred.Where(p => eligible.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
		return pref.Count > 0 && rng.Next(100) < 70 ? pref[rng.Next(pref.Count)] : eligible[rng.Next(eligible.Count)];
	}

	/// <summary>拜访的地点：发布者的家（室内、存在、不是农舍）。</summary>
	private string? VisitLocation(string npc)
	{
		NpcHomeInfo? home = _eco.Home(npc);
		if (home == null || !home.LocationExists || home.Source == "spouse" || home.Location.Contains("FarmHouse", StringComparison.OrdinalIgnoreCase) || home.Location.Contains("WarpRoom", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		GameLocation? loc = Game1.getLocationFromName(home.Location);
		return loc == null || loc.IsOutdoors ? null : home.Location;
	}

	private static bool StillValid(Commission c)
	{
		if (Game1.getCharacterFromName(c.Poster) == null)
		{
			return false;
		}
		if (CommissionKinds.NeedsItems(c.Kind))
		{
			StardewValley.ItemTypeDefinitions.ParsedItemData? data = ItemRegistry.GetData(c.ItemId);
			return data != null && !data.IsErrorItem;
		}
		return Game1.getLocationFromName(c.Location) != null;
	}

	// ---------------- 文字 ----------------

	public static string NpcName(string npc)
	{
		string name = TextTools.CleanForGame(Game1.getCharacterFromName(npc)?.displayName ?? "");
		return name.Length > 0 ? name : npc;
	}

	public static string ItemName(Commission c) => c.ItemId.Length == 0 ? "" : TextTools.CleanForGame(ItemRegistry.GetData(c.ItemId)?.DisplayName ?? c.ItemId);

	public static string TaskLine(Commission c) => CommissionRules.TaskLine(c, NpcName(c.Poster), ItemName(c));

	/// <summary>后台让 AI 用发布者的口吻写板上的话；不合格就留模板。</summary>
	private void RequestText(Commission c)
	{
		IAiSender? ai = Ai;
		if (ai == null || !_config().AiOn)
		{
			return;
		}
		string id = c.Id;
		int count = c.Count;
		string system = CommissionRules.NoteSystem(Language?.Invoke() ?? "中文", NpcName(c.Poster), Personality?.Invoke(c.Poster) ?? "", Game1.currentSeason ?? "spring", c.Kind, ItemName(c), count);
		string user = CommissionRules.NoteUser(NpcName(c.Poster));
		Task.Run(async () =>
		{
			string? text = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(NoteRequest, system, user, 2048, 0.9)).ConfigureAwait(false);
				text = r.Ok ? r.Text?.Trim() : null;
			}
			catch (Exception ex)
			{
				_log.Debug("委托：AI 没写成板上的话，用模板：" + ex.Message);
			}
			(string? clean, string thanks) = CommissionRules.ParseNote(text, count);
			if (clean == null)
			{
				return;
			}
			MainThread.Run(() =>
			{
				Commission? live = Find(id);
				if (live != null && CommissionStates.IsOpenOrActive(live.State))
				{
					live.Text = clean;
					live.TextByAi = true;
					live.Thanks = thanks;
					Changed();
				}
			});
		});
	}

	public string PromptBlock(string npc)
	{
		if (Log == null || !Enabled)
		{
			return "";
		}
		return CommissionRules.PromptBlock(All().Where(c => string.Equals(c.Poster, npc, StringComparison.OrdinalIgnoreCase) && CommissionRules.InPrompt(c, Today)), Today, ItemName);
	}

	private void Changed()
	{
		Version++;
		MarkDirty();
	}

	private void MarkDirty() => _store()?.MarkDirty(CommissionLog.Path);

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		switch (sub)
		{
			case "post":
			{
				IReadOnlyList<Commission> posted = PostNew(1, args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
				return posted.Count == 0 ? "没贴成（板上满了或者凑不出）" : "贴了 " + string.Join("、", posted.Select(c => $"{c.Id} {c.Kind} {c.Poster} {c.ItemId}×{c.Count} {c.RewardGold}g"));
			}
			case "accept" when args.Length > 1:
				return Accept(args[1]).Message;
			case "abandon" when args.Length > 1:
				return Abandon(args[1]).Message;
			case "expire" when args.Length > 1:
			{
				Commission? c = Find(args[1]);
				if (c == null)
				{
					return "没有这条";
				}
				c.LastDay = Today - 1;
				Changed();
				return "下次换天就过期";
			}
			case "clear":
				Log?.Items.Clear();
				Changed();
				return "清空了";
			default:
				return write("commissions.json", new { enabled = Enabled, today = Today, money = Game1.player?.Money, items = All() });
		}
	}
}
