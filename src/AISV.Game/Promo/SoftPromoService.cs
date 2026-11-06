using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Group;
using AISV.Core.Persona;
using AISV.Core.Promo;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Group;
using AISV.Game.Social;
using AISV.Game.Stories;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Promo;

/// <summary>
/// 游戏内推广：后台填的新作品偶尔出现在周报"山谷之外"、八卦、居民群、鹈鹕图、小剧场和来信里，
/// 都用镇上的说法，只让成年村民说（优先阿比盖尔、山姆、塞巴斯蒂安）。还有委托板上的支持任务。
/// </summary>
public sealed class SoftPromoService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ServerContentService _content;

	private readonly SocialSystem _social;

	private readonly ResidentsService _residents;

	private readonly StoryArcService _arcs;

	private readonly Func<string, bool> _npcEnabled;

	private readonly Func<string, string> _display;

	private readonly string _modId;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	public SoftPromoService(Func<ModConfig> config, Func<DocumentStore?> store, ServerContentService content, SocialSystem social, ResidentsService residents, StoryArcService arcs, Func<string, bool> npcEnabled, Func<string, string> display, string modId, ILog log)
	{
		_config = config;
		_store = store;
		_content = content;
		_social = social;
		_residents = residents;
		_arcs = arcs;
		_npcEnabled = npcEnabled;
		_display = display;
		_modId = modId;
		_log = log;
	}

	private static int Today => Game1.Date?.TotalDays ?? 0;

	private PromoState? State => _store()?.Get(PromoBook.Path, PromoBook.Doc);

	private PromoCampaign Campaign => _content.Content.Campaign;

	/// <summary>这个版本有内置服务器就推（不能关）。</summary>
	public bool Enabled => _config().ModOn && _content.Available;

	/// <summary>能推新作品：开着、后台填了作品名、进了存档。</summary>
	public bool Active => Enabled && Campaign.HasGame && State != null;

	public ServerContentService Content => _content;

	public void OnSaveLoaded()
	{
		if (State is not PromoState s)
		{
			return;
		}
		s.SupportDone ??= new List<long>();
		s.Mail ??= new Dictionary<string, string>();
		s.Posts ??= new List<PromoPost>();
		s.LetterGames ??= new List<string>();
		if (s.FirstDay < 0)
		{
			s.FirstDay = Today;
			Dirty();
		}
		try
		{
			EditMail(Game1.content.Load<Dictionary<string, string>>("Data\\mail"));
		}
		catch (Exception ex)
		{
			_log.Debug("推广：谢礼信没补回 Data/mail：" + ex.Message);
		}
	}

	/// <summary>每天早上最多出一种（八卦、鹈鹕图、小剧场、来信按这个顺序）。</summary>
	public void OnDayStarted()
	{
		_content.Refresh();
		if (!Active || State is not PromoState s || s.LastAnyDay == Today)
		{
			return;
		}
		PromoCampaign c = Campaign;
		if (c.GossipEnabled && PromoRules.Due(s, s.LastGossipDay, c.GossipEveryDays, PromoRules.GossipQuietDays, Today) && TryGossip())
		{
			return;
		}
		if (c.PelicanEnabled && PromoRules.Due(s, s.LastPelicanDay, c.PelicanEveryDays, PromoRules.PelicanQuietDays, Today) && TryPelican())
		{
			return;
		}
		if (c.ArcEnabled && PromoRules.ArcDue(s, c, Today, SeasonIndex) && TryArc())
		{
			return;
		}
		if (c.LetterEnabled && PromoRules.LetterDue(s, c, Today))
		{
			TryLetter();
		}
	}

	/// <summary>每 10 分钟：中午到晚上 9 点、居民群空闲时，六分之一的机会。</summary>
	public void OnTimeChanged()
	{
		if (!Active || State is not PromoState s || !Campaign.GroupEnabled || Game1.timeOfDay < PromoRules.GroupFrom || Game1.timeOfDay > PromoRules.GroupUntil || Game1.eventUp || Game1.isFestival())
		{
			return;
		}
		if (PromoRules.Due(s, s.LastGroupDay, Campaign.GroupEveryDays, PromoRules.GroupQuietDays, Today) && _random.Next(6) == 0)
		{
			TryGroup();
		}
	}

	private static int SeasonIndex => PromoRules.SeasonIndex(Game1.year, Utility.getSeasonNumber(Game1.currentSeason));

	/// <summary>周报：这期的"山谷之外"。</summary>
	public (string Title, string Text)? PaperColumn(int issue) => Active ? PromoRules.PaperColumn(Campaign, issue) : null;

	private List<NPC> Speakers(IEnumerable<string> allowed, int count)
	{
		HashSet<string> allow = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
		List<NPC> pick = new List<NPC>();
		foreach (string name in PromoRules.Preferred.OrderBy(_ => _random.Next()).Concat(PromoRules.Fallback.OrderBy(_ => _random.Next())))
		{
			if (pick.Count >= count)
			{
				break;
			}
			if (allow.Contains(name) && Game1.getCharacterFromName(name) is NPC npc && !ChildSafety.IsChild(name, npc.Age) && SafeEnabled(name))
			{
				pick.Add(npc);
			}
		}
		return pick;
	}

	private IEnumerable<string> Everyone => PromoRules.Preferred.Concat(PromoRules.Fallback);

	private bool SafeEnabled(string name)
	{
		try
		{
			return _npcEnabled(name);
		}
		catch
		{
			return false;
		}
	}

	public bool TryGossip()
	{
		if (!Active || State is not PromoState s || Speakers(Everyone, 1).FirstOrDefault() is not NPC speaker)
		{
			return false;
		}
		(string memory, string chat) = PromoRules.Gossip(Campaign, _display(speaker.Name));
		if (!_social.Gossip.AddAuthored(memory, chat, speaker.Name, PromoRules.GossipOrigin, Campaign.Game, PromoRules.GossipLifetimeDays, GameState.Today))
		{
			return false;
		}
		s.LastGossipDay = s.LastAnyDay = Today;
		Dirty();
		_log.Debug($"推广：{speaker.Name} 听说了新作品，记进八卦。");
		return true;
	}

	public bool TryGroup()
	{
		if (!Active || State is not PromoState s || _residents.IsBusy)
		{
			return false;
		}
		List<NPC> who = Speakers(_residents.Members().Select(n => n.Name), 2);
		if (who.Count == 0)
		{
			return false;
		}
		List<GroupLine> lines = PromoRules.GroupLines(Campaign, who.Count).Select((l, i) => new GroupLine(who[i].Name, l.Text, l.Emotion)).ToList();
		if (!_residents.PostScripted(lines))
		{
			return false;
		}
		s.LastGroupDay = Today;
		Dirty();
		_log.Debug("推广：居民群里提了一句新作品。");
		return true;
	}

	/// <summary>鹈鹕图上的推广帖（新的在前）。</summary>
	public IReadOnlyList<PromoPost> PelicanPosts() => (State?.Posts ?? new List<PromoPost>()).OrderByDescending(p => p.TotalDay).ToList();

	public bool TryPelican()
	{
		if (!Active || State is not PromoState s || Speakers(Everyone, 1).FirstOrDefault() is not NPC who)
		{
			return false;
		}
		PromoRules.AddPost(s, new PromoPost
		{
			TotalDay = Today,
			Day = Game1.dayOfMonth,
			Season = Game1.currentSeason,
			Year = Game1.year,
			Npc = who.Name,
			Text = PromoRules.PelicanText(Campaign),
			Image = Campaign.Image
		});
		s.LastPelicanDay = s.LastAnyDay = Today;
		Dirty();
		_log.Debug($"推广：{who.Name} 在鹈鹕图上发了海报。");
		return true;
	}

	public bool TryArc()
	{
		if (!Active || State is not PromoState s)
		{
			return false;
		}
		int max = _config().StorylinesAtOnce;
		if (!PromoRules.ArcRoom(_arcs.Arcs.ActiveCount(_arcs.Usable), max))
		{
			return false;
		}
		foreach (string name in PromoRules.ArcPair)
		{
			if (Game1.getCharacterFromName(name) is not NPC npc || ChildSafety.IsChild(name, npc.Age) || !SafeEnabled(name))
			{
				return false;
			}
		}
		if (!_arcs.Arcs.AddExternal(PromoRules.Arc(Campaign, _modId, Game1.year, Game1.currentSeason), max, _arcs.Usable))
		{
			return false;
		}
		s.LastArcSeason = SeasonIndex;
		s.LastAnyDay = Today;
		Dirty();
		_log.Info("推广：开了一段小剧场（山姆和塞巴斯蒂安的游戏之夜）。");
		return true;
	}

	public bool TryLetter()
	{
		Farmer? player = Game1.player;
		if (!Active || State is not PromoState s || player == null)
		{
			return false;
		}
		NPC? who = Speakers(Everyone, 11).FirstOrDefault(n => player.friendshipData.ContainsKey(n.Name) && player.getFriendshipHeartLevelForNPC(n.Name) >= 2);
		if (who == null || !_social.QueueReadyLetter(who.Name, PromoRules.LetterTrigger(Campaign), PromoRules.LetterSubject, PromoRules.LetterBody(Campaign)))
		{
			return false;
		}
		s.LetterGames.Add(Campaign.Game);
		s.LastLetterDay = s.LastAnyDay = Today;
		Dirty();
		_log.Debug($"推广：{who.Name} 明天会寄来一封信。");
		return true;
	}

	// ---------------- 支持任务 ----------------

	public bool IsSupportDone(long id) => State?.SupportDone.Contains(id) == true;

	/// <summary>委托板上的支持任务：开着推广、这个存档没做过的最新两条。</summary>
	public IReadOnlyList<SupportTask> OpenSupport() => Enabled && State is PromoState s ? PromoRules.OpenSupport(_content.Content.Support, s) : new List<SupportTask>();

	private static bool ValidItem(string id)
	{
		try
		{
			return ItemRegistry.GetData(id) is { IsErrorItem: false };
		}
		catch
		{
			return false;
		}
	}

	/// <summary>点了支持任务的链接就算完成：记下、报给服务器、发谢礼（背包满了寄到信箱）。返回给玩家看的一句话。</summary>
	public (bool Ok, string Message) CompleteSupport(SupportTask t)
	{
		if (State is not PromoState s || Game1.player is not Farmer player)
		{
			return (false, "");
		}
		if (s.SupportDone.Contains(t.Id))
		{
			return (false, AISV.Core.Text.Tr.Get("promo.supportDone", "这个任务已经做过了。"));
		}
		s.SupportDone.Add(t.Id);
		Dirty();
		_content.ReportClick(t.Id);
		(string id, int qty) = PromoRules.PickReward(t, ValidItem, _random);
		Item? item = ItemRegistry.Create(id, qty, 0, allowNull: true);
		string author = Campaign.Author;
		if (item == null)
		{
			return (true, AISV.Core.Text.Tr.Get("promo.thanks", "谢谢你的支持！"));
		}
		string name = item.DisplayName + (qty > 1 ? " ×" + qty : "");
		if (player.couldInventoryAcceptThisItem(item) && player.addItemToInventoryBool(item))
		{
			Game1.addHUDMessage(HUDMessage.ForItemGained(item, qty));
			Game1.playSound("reward");
			return (true, AISV.Core.Text.Tr.Get("promo.thanksGift", "谢谢你的支持！{{author}}送你 {{item}}。", ("author", author), ("item", name)));
		}
		string key = PromoRules.SupportMailKey(t.Id);
		s.Mail[key] = PromoRules.SupportMail(author, id, qty);
		Dirty();
		try
		{
			Game1.content.Load<Dictionary<string, string>>("Data\\mail")[key] = s.Mail[key];
		}
		catch (Exception ex)
		{
			_log.Warn("推广：谢礼信没放进 Data/mail：" + ex.Message);
		}
		if (!player.mailbox.Contains(key))
		{
			player.mailbox.Add(key);
		}
		Game1.playSound("reward");
		return (true, AISV.Core.Text.Tr.Get("promo.thanksMailed", "谢谢你的支持！背包满了，{{item}}寄到信箱了。", ("item", name)));
	}

	public void EditMail(IDictionary<string, string> data)
	{
		foreach (KeyValuePair<string, string> pair in State?.Mail ?? new Dictionary<string, string>())
		{
			data[pair.Key] = pair.Value;
		}
	}

	/// <summary>推广状态马上写盘：同一天里退出再进，不会再推一次、重复报点击。</summary>
	private void Dirty()
	{
		_store()?.MarkDirty(PromoBook.Path);
		_store()?.Commit(new[] { PromoBook.Path });
	}

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		PromoState? s = State;
		switch (sub)
		{
			case "refresh":
				_content.Refresh(force: true);
				return "在拉服务器内容（结果看 dump）";
			case "content" when args.Length > 1:
				try
				{
					if (JsonNode.Parse(File.ReadAllText(args[1])) is not JsonObject o)
					{
						return "不是 JSON 对象";
					}
					_content.ApplyForTest(o);
					return $"换上了：推荐 {_content.Content.Promo.Count} 条、支持任务 {_content.Content.Support.Count} 条、作品《{Campaign.Game}》";
				}
				catch (Exception ex)
				{
					return "读不出来：" + ex.Message;
				}
			case "due" when s != null:
				s.FirstDay = Today - 30;
				s.LastGossipDay = s.LastGroupDay = s.LastPelicanDay = s.LastArcSeason = s.LastLetterDay = s.LastAnyDay = -1;
				s.LetterGames.Clear();
				Dirty();
				return "都到期了";
			case "gossip":
				return TryGossip() ? "记进八卦了" : "没成";
			case "group":
				return TryGroup() ? "群里发了" : "没成";
			case "pelican":
				return TryPelican() ? "发了海报" : "没成";
			case "arc":
				return TryArc() ? "开了小剧场" : "没成";
			case "letter":
				return TryLetter() ? "明天到信" : "没成";
			case "support" when args.Length > 1 && long.TryParse(args[1], out long id):
				SupportTask? t = _content.Content.Support.FirstOrDefault(x => x.Id == id);
				return t == null ? "没有这个任务" : CompleteSupport(t).Message;
			case "unsupport" when s != null:
				s.SupportDone.Clear();
				Dirty();
				return "支持任务都清掉了";
			case "capture":
				ExternalLink.Captured = ExternalLink.Captured == null ? new List<string>() : null;
				return ExternalLink.Captured == null ? "链接照常打开" : "只记下链接，不打开";
			default:
				return write("promo.json", new
				{
					enabled = Enabled,
					active = Active,
					available = _content.Available,
					today = Today,
					state = s,
					content = _content.Content,
					openSupport = OpenSupport(),
					captured = ExternalLink.Captured
				});
		}
	}
}
