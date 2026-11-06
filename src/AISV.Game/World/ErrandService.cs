using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Core.World.Errands;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Inventories;

namespace AISV.Game.World;

/// <summary>表单里能选的一样东西：单价、是不是材料（数量上限高）、从哪家店买。</summary>
public sealed record ErrandItem(string Id, string Name, int Price, bool Material, string? Shop = null, int Stock = 0, int InventoryIndex = -1);

/// <summary>表单里能选的一位村民；Blocked 不为空时选不了。</summary>
public sealed record ErrandHelper(string Npc, string Display, bool Child, IReadOnlySet<string> Skills, int Hearts, string? Blocked);

/// <summary>一张还没发出去的委托。</summary>
public sealed class ErrandDraft
{
	public string Npc { get; set; } = "";

	public string Kind { get; set; } = ErrandKinds.Fetch;

	public string Source { get; set; } = FetchSources.Forage;

	public ErrandItem? Item { get; set; }

	public int Count { get; set; } = 1;

	public int Days { get; set; } = 3;

	public int Reward { get; set; }

	public string Note { get; set; } = "";

	public string Recipient { get; set; } = "";
}

/// <summary>
/// 玩家给村民发委托：报酬（和代买的货款）先交出去托管，村民回话（AI 按性格决定，没有 AI 时按规则），
/// 接下以后按规则掷一次定结果，到日子早上寄信交货或道歉退款。只在房主（或单人）上算。
/// </summary>
public sealed class ErrandService
{
	public const string ReplyRequest = "errand_reply";

	private static readonly HashSet<string> Legendary = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "159", "160", "163", "682", "775", "898", "899", "900", "901", "902" };

	private static readonly HashSet<string> MineMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "(O)390", "(O)382", "(O)378", "(O)380" };

	private static readonly string[] BuyShops = { "SeedShop", "AnimalShop", "FishShop", "Blacksmith", "Saloon" };

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly Ecosystem _eco;

	private readonly NpcDirectory _npcs;

	private readonly Relationships _relationships;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private readonly HashSet<string> _asking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private const string HoldPrefix = "AIStardewValley.Errand.";

	private int _session;

	public ErrandService(Func<ModConfig> config, Func<DocumentStore?> store, Ecosystem eco, NpcDirectory npcs, Relationships relationships, ILog log)
	{
		_config = config;
		_store = store;
		_eco = eco;
		_npcs = npcs;
		_relationships = relationships;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Mood { get; set; }

	/// <summary>寄一封信（村民、缘由、标题、正文、附带物品、数量）。</summary>
	public Func<string, string, string, string, string?, int, string?>? PostLetter { get; set; }

	/// <summary>村民用信使发来一条（没有手机时返回 false）。</summary>
	public Func<string, string, bool>? Text { get; set; }

	/// <summary>办成了：报酬进帮忙的人的口袋，代买的货款进店主的口袋。</summary>
	public Action<Errand>? Completed { get; set; }

	/// <summary>进行中撤回：留下的辛苦费（委托、金额）进帮忙的人的口袋。</summary>
	public Action<Errand, int>? Kept { get; set; }

	/// <summary>让村民记住一件事。</summary>
	public Action<string, string>? Remember { get; set; }

	public int Version { get; private set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.WorldModuleOn && c.ErrandsOn;
		}
	}

	public bool CanPost => Enabled && Context.IsWorldReady && Context.IsMainPlayer && Book != null;

	private ErrandBook? Book => _store()?.Get(ErrandBook.Path, ErrandBook.Doc);

	private static int Today => (Game1.Date?.TotalDays ?? 0) + 1;

	public IReadOnlyList<Errand> All() => Book?.Items.ToList() ?? new List<Errand>();

	public IReadOnlyList<Errand> Open() => All().Where(e => ErrandStates.IsOpen(e.State)).ToList();

	public Errand? Find(string id) => All().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

	public Errand? OpenFor(string npc) => Open().FirstOrDefault(e => string.Equals(e.Npc, npc, StringComparison.OrdinalIgnoreCase));

	public bool IsAsking(string id) => _asking.Contains(id);

	public string Display(string npc) => _npcs.Names.Display(npc);

	public static string ItemName(string id) => TextTools.CleanForGame(ItemRegistry.GetData(id)?.DisplayName ?? id);

	public string What(Errand e) => ErrandRules.What(e, ItemName(e.ItemId), e.Recipient.Length > 0 ? Display(e.Recipient) : "");

	// ---------------- 表单 ----------------

	public IReadOnlyList<ErrandHelper> Helpers()
	{
		List<ErrandHelper> list = new List<ErrandHelper>();
		if (!Context.IsWorldReady)
		{
			return list;
		}
		int open = Open().Count;
		foreach (string name in Game1.player.friendshipData.Keys.ToList())
		{
			NPC? npc = Game1.getCharacterFromName(name);
			if (npc == null || !npc.IsVillager || !npc.CanSocialize || !_npcs.IsEnabled(name))
			{
				continue;
			}
			string? blocked = CommissionRules.RemotePosters.Contains(name) ? Tr.Get("errand.blocked.remote", "不在镇上")
				: OpenFor(name) != null ? Tr.Get("errand.blocked.busy", "已经托了一件事")
				: open >= ErrandRules.MaxTotal ? Tr.Get("errand.blocked.full", "镇上同时最多 {{n}} 件", ("n", ErrandRules.MaxTotal))
				: null;
			list.Add(new ErrandHelper(name, Display(name), ChildSafety.IsChild(name, npc.Age), Skills(name), _relationships.EffectiveHearts(name), blocked));
		}
		return list.OrderBy(h => h.Blocked != null).ThenByDescending(h => h.Skills.Count).ThenByDescending(h => h.Hearts).ThenBy(h => h.Display).ToList();
	}

	public static IReadOnlySet<string> Skills(string npc)
	{
		string? field = null;
		if (Game1.characterData != null && Game1.characterData.TryGetValue(npc, out var data) && data?.CustomFields != null)
		{
			data.CustomFields.TryGetValue(ErrandSkills.Field, out field);
		}
		return ErrandSkills.Of(npc, field);
	}

	/// <summary>这位村民不能接这一类时的原因。</summary>
	public static string? KindBlocked(ErrandHelper h, string kind)
	{
		if (h.Child && !ErrandRules.ChildCan(kind))
		{
			return Tr.Get("errand.blocked.child", "孩子只能代买和转送");
		}
		if (kind == ErrandKinds.Cook && !h.Skills.Contains(ErrandSkills.Cook))
		{
			return Tr.Get("errand.blocked.cook", "不太会做饭");
		}
		return null;
	}

	public IReadOnlyList<ErrandItem> Items(string kind, string source, ErrandHelper? helper)
	{
		try
		{
			return kind switch
			{
				ErrandKinds.Fetch => source switch
				{
					FetchSources.Fish => FishItems(),
					FetchSources.Mine => MineItems(),
					_ => ForageItems()
				},
				ErrandKinds.Buy => ShopItems(helper?.Child == true),
				ErrandKinds.Cook => CookItems(helper?.Npc),
				_ => InventoryItems()
			};
		}
		catch (Exception ex)
		{
			_log.Warn("委托：列物品出错：" + ex.Message);
			return Array.Empty<ErrandItem>();
		}
	}

	private static ErrandItem? Make(string id, bool material = false, int? price = null, string? shop = null)
	{
		Item? item = ItemRegistry.Create(id, 1, 0, allowNull: true);
		if (item == null)
		{
			return null;
		}
		int unit = price ?? (item is StardewValley.Object o ? o.Price : item.salePrice());
		return new ErrandItem(item.QualifiedItemId, TextTools.CleanForGame(item.DisplayName), Math.Max(0, unit), material, shop);
	}

	private static IReadOnlyList<ErrandItem> ForageItems()
	{
		string season = Game1.currentSeason ?? "spring";
		IEnumerable<ErrandItem?> forage = (CommissionRules.Forage.TryGetValue(season, out string[]? ids) ? ids : Array.Empty<string>()).Select(id => Make(id));
		IEnumerable<ErrandItem?> materials = CommissionRules.Materials.Select(m => Make(m.Id, material: true));
		return forage.Concat(materials).OfType<ErrandItem>().ToList();
	}

	private static IReadOnlyList<ErrandItem> FishItems()
	{
		string season = Game1.currentSeason ?? "spring";
		Dictionary<string, string> fish = DataLoader.Fish(Game1.content);
		return fish.Where(kv => !Legendary.Contains(kv.Key))
			.Select(kv => (kv.Key, Fields: kv.Value.Split('/')))
			.Where(f => f.Fields.Length > 6 && f.Fields[1] != "trap" && f.Fields[6].Split(' ').Contains(season, StringComparer.OrdinalIgnoreCase))
			.Select(f => Make("(O)" + f.Key))
			.OfType<ErrandItem>()
			.OrderBy(i => i.Price)
			.ToList();
	}

	private static IReadOnlyList<ErrandItem> MineItems()
	{
		int deepest = Math.Max(1, Game1.player.deepestMineLevel);
		return CommissionRules.MineGoods.Where(g => g.Level <= deepest)
			.Select(g => Make(g.Id, MineMaterials.Contains(g.Id), Math.Max(g.Unit, ItemRegistry.Create(g.Id) is StardewValley.Object o ? o.Price : 0)))
			.OfType<ErrandItem>()
			.ToList();
	}

	private static IReadOnlyList<ErrandItem> ShopItems(bool child)
	{
		Dictionary<string, ShopData> shops = DataLoader.Shops(Game1.content);
		List<ErrandItem> list = new List<ErrandItem>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string shopId in BuyShops.Where(s => !child || ErrandRules.ChildShops.Contains(s)))
		{
			if (!shops.TryGetValue(shopId, out ShopData? shop) || shop?.Items == null)
			{
				continue;
			}
			foreach (ShopItemData entry in shop.Items)
			{
				string? id = entry.ItemId;
				if (string.IsNullOrWhiteSpace(id) || id.Contains(' ') || !id.StartsWith("(O)", StringComparison.OrdinalIgnoreCase) || !GameStateQuery.CheckConditions(entry.Condition) || !seen.Add(id))
				{
					continue;
				}
				Item? item = ItemRegistry.Create(id, 1, 0, allowNull: true);
				if (item != null && Make(id, price: entry.Price > 0 ? entry.Price : item.salePrice(), shop: shopId) is ErrandItem e && e.Price > 0)
				{
					list.Add(e);
				}
			}
		}
		return list;
	}

	private IReadOnlyList<ErrandItem> CookItems(string? npc)
	{
		HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string recipe in Game1.player.cookingRecipes.Keys)
		{
			try
			{
				CraftingRecipe r = new CraftingRecipe(recipe, isCookingRecipe: true);
				if (r.itemToProduce.Count > 0 && ItemRegistry.QualifyItemId(r.itemToProduce[0]) is string id)
				{
					ids.Add(id);
				}
			}
			catch (Exception)
			{
			}
		}
		if (!string.IsNullOrWhiteSpace(npc))
		{
			foreach (ItemTemplate t in _eco.PersonalTastes(npc, "loved").Concat(_eco.PersonalTastes(npc, "liked")).Where(t => t.Category == StardewValley.Object.CookingCategory))
			{
				ids.Add(t.QualifiedId);
			}
		}
		return ids.Select(id => Make(id)).OfType<ErrandItem>().OrderBy(i => i.Name).ToList();
	}

	private static IReadOnlyList<ErrandItem> InventoryItems()
	{
		List<ErrandItem> list = new List<ErrandItem>();
		for (int i = 0; i < Game1.player.Items.Count; i++)
		{
			if (Game1.player.Items[i] is StardewValley.Object o && o.canBeGivenAsGift() && !o.questItem.Value)
			{
				list.Add(new ErrandItem(o.QualifiedItemId, TextTools.CleanForGame(o.DisplayName), Math.Max(0, o.Price), false, Stock: o.Stack, InventoryIndex: i));
			}
		}
		return list;
	}

	public ErrandQuote Quote(ErrandDraft d)
	{
		bool skilled = ErrandSkills.Needed(d.Kind, d.Source) is string need && Skills(d.Npc).Contains(need);
		int hearts = string.IsNullOrWhiteSpace(d.Npc) ? 0 : _relationships.EffectiveHearts(d.Npc);
		int count = d.Kind == ErrandKinds.Relay ? Math.Max(1, d.Item?.Stock ?? 1) : d.Count;
		return ErrandRules.Quote(d.Kind, d.Source, d.Item?.Price ?? 0, count, d.Item?.Material == true, d.Days, hearts, skilled, Math.Max(1, Game1.player?.deepestMineLevel ?? 1));
	}

	/// <summary>发不出去的原因；能发时为 null。</summary>
	public string? Problem(ErrandDraft d)
	{
		if (!CanPost)
		{
			return Context.IsMainPlayer ? Tr.Get("errand.problem.off", "委托没开") : Tr.Get("errand.problem.host", "联机时只有房主能发委托");
		}
		ErrandHelper? h = Helpers().FirstOrDefault(x => string.Equals(x.Npc, d.Npc, StringComparison.OrdinalIgnoreCase));
		if (h == null)
		{
			return Tr.Get("errand.problem.npc", "先选一位村民");
		}
		if (h.Blocked != null)
		{
			return h.Blocked;
		}
		if (KindBlocked(h, d.Kind) is string k)
		{
			return k;
		}
		if (d.Item == null)
		{
			return Tr.Get("errand.problem.item", "先选一样东西");
		}
		if (d.Kind == ErrandKinds.Relay && (string.IsNullOrWhiteSpace(d.Recipient) || string.Equals(d.Recipient, d.Npc, StringComparison.OrdinalIgnoreCase)))
		{
			return Tr.Get("errand.problem.recipient", "选一位收件人（不能是帮忙的人自己）");
		}
		ErrandQuote q = Quote(d);
		if (d.Reward < q.MinReward || d.Reward > q.MaxReward)
		{
			return Tr.Get("errand.problem.reward", "报酬要在 {{min}}g～{{max}}g 之间", ("min", q.MinReward), ("max", q.MaxReward));
		}
		if (Game1.player.Money < q.Total(d.Reward))
		{
			return Tr.Get("errand.problem.money", "钱不够（要 {{n}}g）", ("n", q.Total(d.Reward)));
		}
		return null;
	}

	/// <summary>发出去：先收钱（转送时交出东西），再问村民。<paramref name="inline"/> 不为空时当面回答。</summary>
	public Errand? Post(ErrandDraft d, Func<Errand, ErrandReply, bool>? inline = null)
	{
		if (Problem(d) != null || Book is not ErrandBook book || d.Item is not ErrandItem item)
		{
			return null;
		}
		ErrandQuote q = Quote(d);
		int count = d.Kind == ErrandKinds.Relay ? item.Stock : Math.Clamp(d.Count, 1, ErrandRules.MaxCount(d.Kind, d.Source, item.Material));
		int quality = 0;
		Item? held = null;
		if (d.Kind == ErrandKinds.Relay)
		{
			held = item.InventoryIndex >= 0 && item.InventoryIndex < Game1.player.Items.Count ? Game1.player.Items[item.InventoryIndex] : null;
			if (held is not StardewValley.Object || held.QualifiedItemId != item.Id)
			{
				return null;
			}
			count = held.Stack;
			quality = held.Quality;
			Game1.player.Items[item.InventoryIndex] = null;
		}
		string id = "e" + book.NextNumber++;
		if (d.Kind == ErrandKinds.Relay)
		{
			Game1.player.team.globalInventories.Remove(HoldPrefix + id);
			Game1.player.team.GetOrCreateGlobalInventory(HoldPrefix + id).Add(held);
		}
		Game1.player.Money -= q.Total(d.Reward);
		Errand e = new Errand
		{
			Id = id,
			Kind = d.Kind,
			Source = d.Kind == ErrandKinds.Fetch ? d.Source : "",
			Npc = d.Npc,
			ItemId = item.Id,
			Count = count,
			Quality = quality,
			Recipient = d.Kind == ErrandKinds.Relay ? d.Recipient : "",
			Shop = d.Kind == ErrandKinds.Buy ? item.Shop ?? "" : "",
			Note = TextTools.Truncate(d.Note ?? "", ErrandRules.MaxNote).Trim(),
			Reward = d.Reward,
			Goods = q.Goods,
			DefaultReward = q.DefaultReward,
			PostedDay = Today,
			LastDay = Today + Math.Clamp(d.Days, ErrandRules.MinDays, ErrandRules.MaxDays),
			DaysNeeded = q.DaysNeeded,
			Chance = q.Chance
		};
		book.Items.Add(e);
		Changed();
		_log.Info($"委托 {e.Id}：托 {e.Npc} {What(e)}，报酬 {e.Reward}g，货款 {e.Goods}g，成功率 {e.Chance}%。");
		if (inline != null || MessengerRules.HasPhone(e.Npc))
		{
			Ask(e, inline);
		}
		else
		{
			e.ReplyDay = Today + 1;
		}
		return e;
	}

	// ---------------- 回话 ----------------

	private void Ask(Errand e, Func<Errand, ErrandReply, bool>? inline)
	{
		IAiSender? ai = Ai;
		if (ai == null || !_config().AiOn)
		{
			Reply(e.Id, null, inline);
			return;
		}
		NPC? npc = Game1.getCharacterFromName(e.Npc);
		bool skilled = ErrandSkills.Needed(e.Kind, e.Source) is string need && Skills(e.Npc).Contains(need);
		string system = ErrandRules.ReplySystem(Language?.Invoke() ?? "中文", Display(e.Npc), _npcs.Persona(e.Npc, npc).Personality, _relationships.EffectiveHearts(e.Npc), Mood?.Invoke(e.Npc) ?? "", What(e), e, skilled, ChildSafety.IsChild(e.Npc, npc?.Age));
		string user = ErrandRules.ReplyUser(Display(e.Npc));
		string id = e.Id;
		int reward = e.Reward, fair = e.DefaultReward, session = _session;
		_asking.Add(id);
		Version++;
		Task.Run(async () =>
		{
			ErrandReply? parsed = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(ReplyRequest, system, user, 1024, 0.8)).ConfigureAwait(false);
				parsed = r.Ok && r.Text != null ? ErrandRules.ParseReply(r.Text, reward, fair) : null;
				if (parsed == null)
				{
					_log.Debug($"委托 {id}：AI 的回话不合格，按规则回：{(r.Ok ? r.Text : r.Message)}");
				}
			}
			catch (Exception ex)
			{
				_log.Debug($"委托 {id}：AI 没回成，按规则回：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (session == _session)
				{
					Reply(id, parsed, inline);
				}
			});
		});
	}

	private void Reply(string id, ErrandReply? ai, Func<Errand, ErrandReply, bool>? inline)
	{
		_asking.Remove(id);
		if (Find(id) is not Errand e || e.State != ErrandStates.Waiting)
		{
			Version++;
			return;
		}
		ErrandReply reply = ai ?? ErrandRules.RuleReply(e.Chance, e.Reward, e.DefaultReward);
		if (reply.Decision == ErrandDecision.Counter && reply.Gold <= e.Reward)
		{
			reply = reply with { Decision = ErrandDecision.Accept, Gold = 0 };
		}
		e.Reply = reply.Line;
		e.ReplyByAi = ai != null;
		e.ReplyDay = -1;
		switch (reply.Decision)
		{
			case ErrandDecision.Accept:
				Accept(e);
				break;
			case ErrandDecision.Counter:
				e.State = ErrandStates.Countered;
				e.Counter = reply.Gold;
				break;
			default:
				Close(e, ErrandStates.Declined, e.Reward + e.Goods);
				if (e.Kind == ErrandKinds.Relay)
				{
					ReturnRelayItem(e, inline != null);
				}
				break;
		}
		Changed();
		_log.Info($"委托 {e.Id}：{e.Npc} {reply.Decision}{(reply.Gold > 0 ? " " + reply.Gold + "g" : "")}（{(ai != null ? "AI" : "规则")}）：{reply.Line}");
		if (inline?.Invoke(e, reply) == true)
		{
			return;
		}
		if (Text?.Invoke(e.Npc, reply.Line) != true)
		{
			PostLetter?.Invoke(e.Npc, "errand_reply_" + e.Id, Tr.Get("errand.letter.replySubject", "关于你托的事"), reply.Line + "^^" + Tr.Get("errand.letter.replyHint", "（{{what}}）", ("what", What(e))), null, 1);
		}
	}

	private void Accept(Errand e)
	{
		e.State = ErrandStates.Active;
		e.AcceptedDay = Today;
		e.WillSucceed = _random.Next(100) < e.Chance;
	}

	/// <summary>还价：同意就补差价开工。</summary>
	public string? AcceptCounter(string id)
	{
		if (Find(id) is not Errand e || e.State != ErrandStates.Countered)
		{
			return Tr.Get("errand.problem.gone", "这件委托已经不在了");
		}
		int extra = Math.Max(0, e.Counter - e.Reward);
		if (Game1.player.Money < extra)
		{
			return Tr.Get("errand.problem.money", "钱不够（要 {{n}}g）", ("n", extra));
		}
		Game1.player.Money -= extra;
		e.Reward = e.Counter;
		Accept(e);
		Changed();
		Text?.Invoke(e.Npc, Tr.Get("errand.text.deal", "好，那就这么说定了。"));
		return null;
	}

	/// <summary>撤回（包括不接受还价）：等回复时全退；进行中退一半报酬，好感 -10。</summary>
	public string? Withdraw(string id)
	{
		if (Find(id) is not Errand e || !ErrandStates.IsOpen(e.State))
		{
			return Tr.Get("errand.problem.gone", "这件委托已经不在了");
		}
		bool started = e.State == ErrandStates.Active;
		int refund = ErrandRules.Refund(e);
		Close(e, ErrandStates.Withdrawn, refund);
		if (e.Kind == ErrandKinds.Relay)
		{
			ReturnRelayItem(e, true);
		}
		if (started && e.Reward + e.Goods - refund > 0)
		{
			Kept?.Invoke(e, e.Reward + e.Goods - refund);
		}
		if (started && Game1.getCharacterFromName(e.Npc) is NPC npc && Game1.player.friendshipData.ContainsKey(e.Npc))
		{
			Game1.player.changeFriendship(ErrandRules.WithdrawFriendship, npc);
		}
		Changed();
		return null;
	}

	private void Close(Errand e, string state, int refund)
	{
		e.State = state;
		e.ClosedDay = Today;
		if (refund > 0)
		{
			Game1.player.Money += refund;
		}
	}

	/// <summary>拿出托管的原物；不在了就照记录重造一份。</summary>
	private static Item TakeHeld(Errand e)
	{
		string key = HoldPrefix + e.Id;
		Item? item = Game1.player.team.globalInventories.TryGetValue(key, out Inventory inv) ? inv.FirstOrDefault(i => i != null) : null;
		Game1.player.team.globalInventories.Remove(key);
		return item ?? ItemRegistry.Create(e.ItemId, e.Count, e.Quality);
	}

	/// <summary>转送的东西原样退回：先放背包；满了时人在跟前就丢在脚下，否则放进失物招领箱。</summary>
	private static void ReturnRelayItem(Errand e, bool here)
	{
		Item item = TakeHeld(e);
		if (Game1.player.addItemToInventoryBool(item))
		{
			return;
		}
		if (here && Game1.player.currentLocation != null)
		{
			Game1.createItemDebris(item, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
			return;
		}
		Game1.player.team.returnedDonations.Add(item);
		Game1.player.team.newLostAndFoundItems.Value = true;
		Game1.addHUDMessage(new HUDMessage(Tr.Get("errand.lostAndFound", "背包满了，{{item}}放进了镇长家的失物招领箱。", ("item", item.DisplayName)), 2));
	}

	// ---------------- 每天 ----------------

	public void OnSaveLoaded()
	{
		_session++;
		_asking.Clear();
		Version++;
	}

	public void OnReturnedToTitle()
	{
		_session++;
		_asking.Clear();
	}

	public void OnDayStarted()
	{
		if (Book is not ErrandBook book || !Context.IsMainPlayer)
		{
			return;
		}
		try
		{
			int today = Today;
			foreach (Errand e in book.Items.ToList())
			{
				if (e.State == ErrandStates.Waiting && !_asking.Contains(e.Id) && (e.ReplyDay < 0 || e.ReplyDay <= today))
				{
					Ask(e, null);
				}
				else if (e.State == ErrandStates.Countered && today > e.LastDay)
				{
					Close(e, ErrandStates.Withdrawn, e.Reward + e.Goods);
					if (e.Kind == ErrandKinds.Relay)
					{
						ReturnRelayItem(e, false);
						PostLetter?.Invoke(e.Npc, "errand_back_" + e.Id, Tr.Get("errand.letter.backSubject", "东西还你"), Tr.Get("errand.letter.back", "你没回我，我就先不帮你送了。东西已经还到你包里，包满了就去镇长家的失物招领箱拿。"), null, 1);
					}
				}
				else if (e.State == ErrandStates.Active && today >= ErrandRules.ResultDay(e))
				{
					if (e.WillSucceed && (e.Kind != ErrandKinds.Relay || Game1.getCharacterFromName(e.Recipient) != null))
					{
						Complete(e);
					}
					else
					{
						Fail(e);
					}
				}
			}
			book.Items.RemoveAll(e => !ErrandStates.IsOpen(e.State) && e.ClosedDay >= 0 && today - e.ClosedDay > ErrandRules.ClosedKeepDays);
			Changed();
		}
		catch (Exception ex)
		{
			_log.Warn("委托：早上结算出错：" + ex);
		}
	}

	private void Complete(Errand e)
	{
		e.State = ErrandStates.Done;
		e.ClosedDay = Today;
		string helper = Display(e.Npc);
		string what = What(e);
		bool skilled = ErrandSkills.Needed(e.Kind, e.Source) is string need && Skills(e.Npc).Contains(need);
		int friendship = e.Kind == ErrandKinds.Relay ? ErrandRules.RelayFriendship : skilled ? ErrandRules.SkilledFriendship : ErrandRules.DoneFriendship;
		if (Game1.getCharacterFromName(e.Npc) is NPC npc && Game1.player.friendshipData.ContainsKey(e.Npc))
		{
			Game1.player.changeFriendship(friendship, npc);
		}
		if (e.Kind == ErrandKinds.Relay)
		{
			string recipient = Display(e.Recipient);
			if (Game1.getCharacterFromName(e.Recipient) is NPC to && TakeHeld(e) is StardewValley.Object gift)
			{
				to.receiveGift(gift, Game1.player, updateGiftLimitInfo: false, showResponse: false);
			}
			Remember?.Invoke(e.Recipient, Tr.Get("errand.memory.got", "{{helper}}替农夫把{{item}}捎给了你。", ("helper", helper), ("item", ItemName(e.ItemId))));
			Text?.Invoke(e.Npc, Tr.Get("errand.text.relayed", "东西已经交到{{who}}手上了，放心。", ("who", recipient)));
		}
		else
		{
			PostLetter?.Invoke(e.Npc, "errand_done_" + e.Id, Tr.Get("errand.letter.doneSubject", "你托的事办好了"), DoneBody(e), e.ItemId, e.Count);
			Text?.Invoke(e.Npc, Tr.Get("errand.text.done", "你要的东西放进信里寄过去了，记得看信箱。"));
		}
		Completed?.Invoke(e);
		Remember?.Invoke(e.Npc, Tr.Get("errand.memory.done", "你替农夫{{what}}，办成了，拿了 {{gold}}g。", ("what", what), ("gold", e.Reward)));
		_log.Info($"委托 {e.Id}：{e.Npc} 办成了。");
	}

	private string DoneBody(Errand e) => e.Kind switch
	{
		ErrandKinds.Buy => Tr.Get("errand.letter.doneBuy", "东西买到了，一共 {{count}} 个，跟这封信一起寄给你。钱正好，没有剩。", ("count", e.Count)),
		ErrandKinds.Cook => Tr.Get("errand.letter.doneCook", "做好了，趁新鲜吃。要是合口味，下次再找我。"),
		_ => Tr.Get("errand.letter.doneFetch", "你要的东西弄齐了，{{count}} 个都在信里。", ("count", e.Count))
	};

	private void Fail(Errand e)
	{
		Close(e, ErrandStates.Failed, e.Reward + e.Goods);
		if (e.Kind == ErrandKinds.Relay)
		{
			ReturnRelayItem(e, false);
		}
		string body = e.Kind == ErrandKinds.Relay
			? Tr.Get("errand.letter.failRelay", "对不起，这几天一直没碰上{{who}}。东西原样还到你包里了（包满了就在镇长家的失物招领箱），钱也退回去了。", ("who", Display(e.Recipient)))
			: Tr.Get("errand.letter.fail", "对不起，你托的事我没办成。钱已经全部退给你了，下次我一定尽力。");
		PostLetter?.Invoke(e.Npc, "errand_fail_" + e.Id, Tr.Get("errand.letter.failSubject", "实在抱歉"), body, null, 1);
		Remember?.Invoke(e.Npc, Tr.Get("errand.memory.fail", "你答应农夫{{what}}，没办成，钱退回去了。", ("what", What(e))));
		_log.Info($"委托 {e.Id}：{e.Npc} 没办成，退款 {e.Reward + e.Goods}g。");
	}

	// ---------------- 给 AI 和界面 ----------------

	public string PromptBlock(string npc)
	{
		if (Book == null || !Enabled)
		{
			return "";
		}
		int today = Today;
		return ErrandRules.PromptBlock(All().Where(e => string.Equals(e.Npc, npc, StringComparison.OrdinalIgnoreCase) && ErrandRules.InPrompt(e, today)), today, What);
	}

	public static int DaysLeft(Errand e) => e.LastDay - Today;

	public static double Progress(Errand e) => ErrandRules.Progress(e, Today);

	private void Changed()
	{
		Version++;
		_store()?.MarkDirty(ErrandBook.Path);
	}

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
		switch (sub)
		{
			case "list":
				return write("errands.json", All()) + "\n" + string.Join("\n", All().Select(e => $"{e.Id} {e.State} {e.Npc} {What(e)} {e.Reward}g 成功率 {e.Chance}% {(e.Reply.Length > 0 ? "「" + e.Reply + "」" : "")}"));
			case "post" when args.Length >= 4:
			{
				string kind = args[2].ToLowerInvariant();
				string source = kind.Contains(':') ? kind.Split(':')[1] : FetchSources.Forage;
				kind = kind.Split(':')[0];
				ErrandHelper? h = Helpers().FirstOrDefault(x => string.Equals(x.Npc, args[1], StringComparison.OrdinalIgnoreCase));
				ErrandItem? item = Items(kind, source, h).FirstOrDefault(i => string.Equals(i.Id, args[3], StringComparison.OrdinalIgnoreCase) || i.Name == args[3]);
				ErrandDraft d = new ErrandDraft { Npc = args[1], Kind = kind, Source = source, Item = item, Count = args.Length > 4 && int.TryParse(args[4], out int n) ? n : 1, Days = args.Length > 5 && int.TryParse(args[5], out int days) ? days : 3, Recipient = args.Length > 6 ? args[6] : "" };
				ErrandQuote q = Quote(d);
				d.Reward = args.Length > 7 && int.TryParse(args[7], out int gold) ? gold : args.Length > 7 && args[7] == "low" ? q.MinReward : q.DefaultReward;
				string? problem = Problem(d);
				if (problem != null)
				{
					return "发不出去：" + problem;
				}
				Errand? e = Post(d);
				return e == null ? "没发成。" : $"发出去了：{e.Id}，{What(e)}，要 {q.DaysNeeded} 天，成功率 {q.Chance}%。";
			}
			case "items" when args.Length >= 2:
			{
				string kind = args[1].Split(':')[0];
				string source = args[1].Contains(':') ? args[1].Split(':')[1] : FetchSources.Forage;
				return string.Join("、", Items(kind, source, null).Select(i => $"{i.Name}({i.Id},{i.Price}g)"));
			}
			case "reply" when args.Length >= 3 && Find(args[1]) is Errand e:
			{
				ErrandDecision decision = args[2].ToLowerInvariant() switch { "decline" => ErrandDecision.Decline, "counter" => ErrandDecision.Counter, _ => ErrandDecision.Accept };
				int gold = args.Length > 3 && int.TryParse(args[3], out int g) ? g : 0;
				_asking.Remove(e.Id);
				Reply(e.Id, new ErrandReply(decision, gold, "（调试）" + decision), null);
				return $"{e.Id}：{e.State}";
			}
			case "finish" when args.Length >= 2 && Find(args[1]) is Errand e && e.State == ErrandStates.Active:
				e.WillSucceed = args.Length < 3 || args[2] != "fail";
				if (e.WillSucceed)
				{
					Complete(e);
				}
				else
				{
					Fail(e);
				}
				Changed();
				return $"{e.Id}：{e.State}";
			case "counter" when args.Length >= 3 && Find(args[1]) is Errand e:
				return (args[2] == "yes" ? AcceptCounter(e.Id) : Withdraw(e.Id)) ?? $"{e.Id}：{Find(e.Id)?.State}";
			case "withdraw" when args.Length >= 2:
				return Withdraw(args[1]) ?? "撤回了。";
			case "prompt" when args.Length >= 2:
				return PromptBlock(args[1]);
			case "clear":
				foreach (Errand e in Open().Where(e => e.Kind == ErrandKinds.Relay))
				{
					ReturnRelayItem(e, true);
				}
				Book?.Items.Clear();
				Changed();
				return "清空了。";
			default:
				return "用法：aisv_errand list | items <类型[:来源]> | post <村民> <类型[:来源]> <物品> [数量] [天数] [收件人] [报酬] | reply <id> accept|decline|counter [金额] | counter <id> yes|no | finish <id> [fail] | withdraw <id> | prompt <村民> | clear";
		}
	}
}
