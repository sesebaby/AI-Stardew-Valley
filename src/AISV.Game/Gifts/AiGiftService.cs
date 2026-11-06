using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Gifts;
using AISV.Core.Social;
using AISV.Core.Social.Letters;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Social;
using AISV.Game.World;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Gifts;

/// <summary>
/// AI 礼物：村民送你一件游戏里的模板物品，名字和描述用他的口吻写。
/// 当面聊天时回复里给他几样能送的东西，由 AI 决定送不送，聊完交到你手上；心数信和生日信必带一件，读信时放在信底下。
/// </summary>
public sealed class AiGiftService
{
	public const string NamingRequest = "gift_naming";

	public const string KeyId = "AIStardewValley/GiftId";

	public const string KeyFrom = "AIStardewValley/GiftFrom";

	public const string KeyName = "AIStardewValley/GiftName";

	public const string KeyDescription = "AIStardewValley/GiftDescription";

	public const string KeyDay = "AIStardewValley/GiftDay";

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly Ecosystem _eco;

	private readonly Func<string, bool> _npcEnabled;

	private readonly ILog _log;

	/// <summary>上次给这位村民的回复里列了哪些（AI 只能从里面挑）。</summary>
	private readonly Dictionary<string, List<GiftCandidate>> _offered = new Dictionary<string, List<GiftCandidate>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, AiGiftRecord> _pending = new Dictionary<string, AiGiftRecord>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> _handOver = new List<string>();

	private int _freeTicks;

	public AiGiftService(Func<ModConfig> config, Func<DocumentStore?> store, Ecosystem eco, Func<string, bool> npcEnabled, ILog log)
	{
		_config = config;
		_store = store;
		_eco = eco;
		_npcEnabled = npcEnabled;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	/// <summary>村民自己的家当（账本给的），送礼时优先从里面挑。</summary>
	public Func<string, IReadOnlyList<string>>? Owned { get; set; }

	/// <summary>送出去以后从家当里拿掉：村民、物品、送给谁。</summary>
	public Action<string, string, string>? GaveAway { get; set; }

	public Func<string, string>? Personality { get; set; }

	public Func<EmotionalMemory?>? Feelings { get; set; }

	public bool Enabled => _config().ModOn && _config().VillagerGiftsOn && _config().AiOn;

	private AiGiftLog? Log => _store()?.Get(AiGiftLog.Path, AiGiftLog.Doc);

	public IReadOnlyList<AiGiftRecord> Gifts => Log?.Gifts.ToList() ?? new List<AiGiftRecord>();

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public void OnSaveLoaded() => Clear();

	public void OnReturnedToTitle() => Clear();

	private void Clear()
	{
		_offered.Clear();
		_pending.Clear();
		_handOver.Clear();
		_freeTicks = 0;
	}

	/// <summary>聊完、玩家空下来 20 帧再把礼物交到手上。</summary>
	public void OnUpdateTicked()
	{
		if (_handOver.Count == 0 || !Context.IsWorldReady)
		{
			return;
		}
		if (!Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.eventUp || Game1.dialogueUp)
		{
			_freeTicks = 0;
			return;
		}
		if (++_freeTicks < AiGiftRules.HandOverFreeTicks)
		{
			return;
		}
		_freeTicks = 0;
		string npc = _handOver[0];
		_handOver.RemoveAt(0);
		if (_pending.Remove(npc, out AiGiftRecord? record))
		{
			HandOver(record);
		}
	}

	// ---------------- 规则 ----------------

	private string DialogueBlock(string npc, int hearts)
	{
		ModConfig c = _config();
		List<AiGiftRecord> gifts = Gifts.ToList();
		int givenToday = gifts.Count(g => g.Source == AiGiftSources.Dialogue && g.CreatedDay == Today) + _pending.Count;
		return AiGiftRules.DialogueBlock(Enabled && Log != null, _npcEnabled(npc), hearts, c.GiftHearts, AiGiftRules.OnCooldown(gifts, npc, Today, c.GiftRestDays), c.GiftRestDays, givenToday, c.GiftsDaily, _pending.ContainsKey(npc));
	}

	/// <summary>招牌物品、他自己最爱和喜欢的、当季作物随机 4 件；能送、有价格、不是古物和书、所属 mod 开着、不超过信件礼物上限。</summary>
	public List<GiftCandidate> Candidates(string npc)
	{
		ModConfig c = _config();
		List<(string Id, string Note, bool Loved)> raw = new List<(string, string, bool)>();
		raw.AddRange((Owned?.Invoke(npc) ?? Array.Empty<string>()).Take(4).Select(id => (id, AiGiftRules.NoteOwned, false)));
		int usual = LetterBook.UsualGift(npc, _ => true);
		if (usual > 0)
		{
			raw.Add(("(O)" + usual, AiGiftRules.NoteUsual, false));
		}
		raw.AddRange(_eco.PersonalTastes(npc, "loved").Select(t => (t.QualifiedId, AiGiftRules.NoteLoved, true)));
		raw.AddRange(_eco.PersonalTastes(npc, "liked").Select(t => (t.QualifiedId, AiGiftRules.NoteLiked, false)));
		Random random = new Random(Today * 31 + StableHash(npc));
		raw.AddRange(_eco.FindItems(new ItemQuery { GiftableOnly = true, CropProduceOnly = true, InSeason = Game1.currentSeason ?? "spring", Limit = 60 }).OrderBy(_ => random.Next()).Take(4).Select(t => (t.QualifiedId, AiGiftRules.NoteSeason, false)));
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		IEnumerable<GiftCandidate> ok = raw.Where(r => seen.Add(r.Id) && Giftable(r.Id, r.Loved, c)).Select(r => new GiftCandidate(r.Id, ShellName(r.Id), r.Note));
		return AiGiftRules.Order(ok, Gifts, npc);
	}

	private static int StableHash(string s)
	{
		unchecked
		{
			int h = 17;
			foreach (char ch in s)
			{
				h = h * 31 + ch;
			}
			return h;
		}
	}

	private bool Giftable(string id, bool loved, ModConfig c)
	{
		try
		{
			ItemTemplate? t = _eco.Item(id);
			if (t == null || !t.CanBeGiven || t.Price <= 0 || t.Category == -999 || !_eco.IsEnabled(t))
			{
				return false;
			}
			if (string.Equals(t.Type, "Arch", StringComparison.OrdinalIgnoreCase) || t.Category == StardewValley.Object.booksCategory || t.Category == StardewValley.Object.skillBooksCategory)
			{
				return false;
			}
			return ItemRegistry.Create(id, 1, 0, allowNull: true) is StardewValley.Object obj && LetterGifts.Allowed(obj, loved, c);
		}
		catch
		{
			return false;
		}
	}

	private static string ShellName(string id) => TextTools.CleanForGame(ItemRegistry.GetDataOrErrorItem(id).DisplayName);

	private static string Who(string npc)
	{
		try
		{
			return TextTools.CleanForGame(Game1.getCharacterFromName(npc, mustBeVillager: false)?.displayName ?? NPC.GetDisplayName(npc) ?? npc);
		}
		catch
		{
			return npc;
		}
	}

	// ---------------- 当面聊天 ----------------

	/// <summary>回复 JSON 里的 gift 字段；不能送时为空串（AI 就看不到这个字段）。</summary>
	public string DialogueJson(string npc, int hearts)
	{
		try
		{
			if (DialogueBlock(npc, hearts).Length > 0)
			{
				return "";
			}
			List<GiftCandidate> list = Candidates(npc);
			if (list.Count == 0)
			{
				return "";
			}
			_offered[npc] = list;
			return AiGiftRules.DialogueJson(Who(npc), list);
		}
		catch (Exception ex)
		{
			_log.Trace($"AI 礼物：没列出 {npc} 能送的东西：{ex.Message}");
			return "";
		}
	}

	/// <summary>规则复核 AI 在回复里决定送的礼物；合规就等聊完交给你。</summary>
	public void OnTalked(string npc, int hearts, NpcReaction reaction)
	{
		NpcGift? g = reaction.Gift;
		if (g == null)
		{
			return;
		}
		string block = DialogueBlock(npc, hearts);
		if (block.Length > 0 || !_offered.TryGetValue(npc, out List<GiftCandidate>? offered))
		{
			_log.Info($"{npc} 想送的礼物没送成：{(block.Length > 0 ? block : "这次没给他列能送的东西")}。");
			reaction.Gift = null;
			return;
		}
		GiftCandidate? pick = AiGiftRules.Match(offered, g.ItemId, Ecosystem.Qualify);
		if (pick == null)
		{
			_log.Info($"{npc} 想送的“{g.ItemId}”不在清单里，不送。");
			reaction.Gift = null;
			return;
		}
		AiGiftRecord record = NewRecord(npc, pick.Id, AiGiftSources.Dialogue, g.Reason);
		ApplyNaming(record, g.Name, g.Description, named: g.Name.Length > 0);
		_pending[npc] = record;
		if (!_handOver.Contains(npc, StringComparer.OrdinalIgnoreCase))
		{
			_handOver.Add(npc);
		}
		_log.Info($"{npc} 聊完会送“{record.Name}”（{record.TemplateId}）。");
	}

	// ---------------- 信 ----------------

	/// <summary>给一封信配礼物：先定下模板和兜底名字，后台让 AI 挑选、起名。没东西可送返回 null（信里留原来的附件）。</summary>
	public string? PrepareLetterGift(string npc, string mailKey, string reason)
	{
		if (!Enabled || Log is not AiGiftLog log)
		{
			return null;
		}
		List<GiftCandidate> list = Candidates(npc);
		if (list.Count == 0)
		{
			return null;
		}
		AiGiftRecord record = NewRecord(npc, list[0].Id, AiGiftSources.Letter, reason);
		record.MailKey = mailKey;
		ApplyNaming(record, null, null, named: false);
		log.Gifts.Add(record);
		MarkDirty();
		NameInBackground(record, list, reason, () => { });
		return record.Id;
	}

	private void NameInBackground(AiGiftRecord record, List<GiftCandidate> list, string reason, Action after)
	{
		if (Ai == null)
		{
			after();
			return;
		}
		(string system, string user) = NamingPrompt(record.Npc, list, reason);
		IAiSender ai = Ai;
		Task.Run(async () =>
		{
			NamedGift? named = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(NamingRequest, system, user, 2048, 0.9)).ConfigureAwait(false);
				named = r.Ok ? AiGiftRules.ParseNaming(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Warn($"给 {record.Npc} 的礼物起名出错：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (named != null && !record.Delivered)
				{
					GiftCandidate? pick = AiGiftRules.Match(list, named.ItemId, Ecosystem.Qualify);
					if (pick != null)
					{
						record.TemplateId = pick.Id;
					}
					ApplyNaming(record, named.Name, named.Description, named: true);
					MarkDirty();
					_log.Info($"{record.Npc} 的礼物起名叫“{record.Name}”（{record.TemplateId}）。");
				}
				after();
			});
		});
	}

	private (string System, string User) NamingPrompt(string npc, List<GiftCandidate> list, string reason)
	{
		string player = Game1.player?.Name ?? "农夫";
		string memories = "";
		EmotionalMemory? feelings = Feelings?.Invoke();
		if (feelings != null)
		{
			memories = string.Join("\n", feelings.GetOrCreate(npc).Recollections.Take(3).Select(m => "- " + m));
		}
		Farmer? p = Game1.player;
		bool known = p != null && p.friendshipData.TryGetValue(npc, out Friendship _);
		Friendship? f = known ? p!.friendshipData[npc] : null;
		string relationship = AiGiftRules.Relationship(f?.IsMarried() == true, f?.IsEngaged() == true, f?.IsDating() == true, known ? p!.getFriendshipHeartLevelForNPC(npc) : 0, known);
		string system = AiGiftRules.NamingSystem(Language?.Invoke() ?? "中文", npc, Personality?.Invoke(npc) ?? "", player, relationship, Game1.currentSeason ?? "spring", reason, memories, list, Who(npc));
		return (system, AiGiftRules.NamingUser(npc, player));
	}

	/// <summary>读信时：做出礼物放进信里，每件只给一次。</summary>
	public Item? TakeLetterGift(string mailKey)
	{
		AiGiftRecord? record = Log?.Gifts.FirstOrDefault(g => !g.Delivered && string.Equals(g.MailKey, mailKey, StringComparison.Ordinal));
		if (record == null || BuildItem(record) is not Item item)
		{
			return null;
		}
		MarkDelivered(record);
		return item;
	}

	public Item? Preview(string? giftId) => Find(giftId) is AiGiftRecord r ? BuildItem(r) : null;

	/// <summary>给提示词和记忆用的名字：还没起好名时用物品本名，不用"某某送的"兜底名。</summary>
	public string? NameOf(string? giftId) => Find(giftId) is AiGiftRecord r ? (r.Named ? r.Name : ShellName(r.TemplateId)) : null;

	private AiGiftRecord? Find(string? giftId) => string.IsNullOrWhiteSpace(giftId) ? null : Log?.Gifts.FirstOrDefault(g => g.Id == giftId);

	// ---------------- 共用 ----------------

	private static AiGiftRecord NewRecord(string npc, string templateId, string source, string? reason) => new AiGiftRecord
	{
		Id = Guid.NewGuid().ToString("N").Substring(0, 12),
		Npc = npc,
		TemplateId = templateId,
		Source = source,
		Reason = (reason ?? "").Trim(),
		CreatedDay = Today
	};

	private static void ApplyNaming(AiGiftRecord record, string? name, string? description, bool named)
	{
		string clean = AiGiftRules.CleanName(name);
		record.Named = named && clean.Length > 0;
		record.Name = record.Named ? clean : AiGiftRules.FallbackName(Who(record.Npc), ShellName(record.TemplateId));
		record.Description = AiGiftRules.CleanDescription(record.Named ? description : null, Who(record.Npc), TemplateDescription(record.TemplateId));
	}

	private static string TemplateDescription(string id)
	{
		try
		{
			StardewValley.ItemTypeDefinitions.ParsedItemData data = ItemRegistry.GetDataOrErrorItem(id);
			return TextTools.CleanForGame(data.Description ?? data.DisplayName);
		}
		catch
		{
			return "";
		}
	}

	/// <summary>模板物品：名字写进 displayNameFormat，描述和送礼人写进 modData。只做普通物品。</summary>
	public static StardewValley.Object? BuildItem(AiGiftRecord r)
	{
		if (ItemRegistry.Create(r.TemplateId, 1, 0, allowNull: true) is not StardewValley.Object obj || (obj.GetType() != typeof(StardewValley.Object) && obj.GetType() != typeof(StardewValley.Objects.ColoredObject)))
		{
			return null;
		}
		string name = AiGiftRules.CleanName(r.Name);
		if (name.Length == 0)
		{
			name = AiGiftRules.FallbackName(Who(r.Npc), obj.DisplayName);
		}
		obj.displayNameFormat = name;
		obj.displayName = null;
		obj.modData[KeyId] = r.Id;
		obj.modData[KeyFrom] = r.Npc;
		obj.modData[KeyName] = name;
		obj.modData[KeyDescription] = AiGiftRules.CleanDescription(r.Description, Who(r.Npc), TemplateDescription(r.TemplateId));
		obj.modData[KeyDay] = r.CreatedDay.ToString();
		return obj;
	}

	public static string? GiftIdOf(Item? item) => item != null && item.modData.TryGetValue(KeyId, out string id) ? id : null;

	public static string? DescriptionOf(Item? item) => item != null && item.modData.TryGetValue(KeyDescription, out string text) && !string.IsNullOrWhiteSpace(text) ? text : null;

	/// <summary>像游戏举起物品那样交给你，记进他的记忆。</summary>
	private void HandOver(AiGiftRecord record)
	{
		if (BuildItem(record) is not Item item || Game1.player == null || Log is not AiGiftLog log)
		{
			return;
		}
		log.Gifts.Add(record);
		try
		{
			Game1.player.addItemByMenuIfNecessaryElseHoldUp(item);
		}
		catch
		{
			Item? left = Game1.player.addItemToInventory(item);
			if (left != null)
			{
				Game1.createItemDebris(left, Game1.player.Position, Game1.player.FacingDirection, Game1.player.currentLocation);
			}
		}
		MarkDelivered(record);
		_log.Info($"{record.Npc} 送给农夫“{record.Name}”（{record.TemplateId}）。");
	}

	private void MarkDelivered(AiGiftRecord record)
	{
		record.Delivered = true;
		record.DeliveredDay = Today;
		MarkDirty();
		GaveAway?.Invoke(record.Npc, record.TemplateId, Game1.player?.Name ?? "农夫");
		try
		{
			GameDate today = GameState.Today;
			Feelings?.Invoke()?.AddKeyMemory(record.Npc, AiGiftRules.KeyMemory(Dates.Label(today), Game1.player?.Name ?? "农夫", record, ShellName(record.TemplateId)), today);
		}
		catch (Exception ex)
		{
			_log.Trace($"礼物没记进 {record.Npc} 的记忆：{ex.Message}");
		}
	}

	public string PromptBlock(string npc) => Log == null ? "" : AiGiftRules.PromptBlock(Gifts, npc, Today, ShellName);

	private void MarkDirty() => _store()?.MarkDirty(AiGiftLog.Path);

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		switch (sub)
		{
			case "candidates" when args.Length > 1:
			{
				string text = AiGiftRules.Candidates(Candidates(args[1]));
				return text.Length > 0 ? text : "没有能送的东西";
			}
			case "json" when args.Length > 1:
			{
				string text = DialogueJson(args[1], args.Length > 2 && int.TryParse(args[2], out int h) ? h : 10);
				return text.Length > 0 ? text : "（这次不给 gift 字段：" + DialogueBlock(args[1], args.Length > 2 && int.TryParse(args[2], out int h2) ? h2 : 10) + "）";
			}
			case "make" when args.Length > 1:
			{
				string npc = args[1];
				List<GiftCandidate> list = Candidates(npc);
				if (args.Length > 2 && args[2] != "ai")
				{
					string? id = Ecosystem.Qualify(args[2]);
					list = id == null ? new List<GiftCandidate>() : new List<GiftCandidate> { new GiftCandidate(id, ShellName(id), "test") };
				}
				if (list.Count == 0 || Log is not AiGiftLog log)
				{
					return "做不出来";
				}
				AiGiftRecord record = NewRecord(npc, list[0].Id, AiGiftSources.Debug, "随手想送点东西");
				ApplyNaming(record, null, null, named: false);
				void Give()
				{
					if (BuildItem(record) is not Item item)
					{
						_log.Info("这件做不成礼物");
						return;
					}
					log.Gifts.Add(record);
					Item? left = Game1.player.addItemToInventory(item);
					if (left != null)
					{
						Game1.createItemDebris(left, Game1.player.Position, Game1.player.FacingDirection, Game1.player.currentLocation);
					}
					MarkDelivered(record);
					_log.Info($"放进背包：“{record.Name}”（{record.TemplateId}）\n{record.Description}");
				}
				if (args.Contains("ai"))
				{
					NameInBackground(record, list, record.Reason, Give);
					return "让 AI 起名，好了放进背包";
				}
				Give();
				return "好了";
			}
			case "letter" when args.Length > 1:
			{
				string? id = PrepareLetterGift(args[1], "AISV_test_" + Today, "测试");
				return id == null ? "没备上" : "备好了礼物 " + id;
			}
			case "clear":
				Log?.Gifts.Clear();
				MarkDirty();
				Clear();
				return "清空了";
			default:
				return write("ai-gifts.json", new { enabled = Enabled, today = Today, pending = _pending.Values, offered = _offered, gifts = Gifts });
		}
	}
}
