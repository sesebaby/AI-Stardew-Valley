using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Dialogue;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Awareness;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>
/// 卖赃物被认出：失主按自己的脾气当场翻脸（AI 按人设说，情绪拉满），东西拿走、镇长罚款；好感、心情、记仇（之后找上门来吵）、
/// 对手和爱看热闹的人取笑、八卦上周报。同一季被认出越多越重：第 2 次停业整顿，第 3 次被警长（装了 Law and Order）或镇长带走，当天结束。
/// </summary>
public sealed partial class MartService
{
	private const string Jail = "sdvhead.LawAndOrderSV_Jail";

	private static readonly string[] Officers = { "sdvhead.LawAndOrderSV_Russell", "sdvhead.LawAndOrderSV_Shannon" };

	/// <summary>其他 mod 的 NPC 让 AI 判断过的脾气（这一局记着）。</summary>
	private readonly Dictionary<string, FenceStyle> _styles = new Dictionary<string, FenceStyle>(StringComparer.OrdinalIgnoreCase);

	/// <summary>失主还在当场发作（AI 的话还没回来）：这时不抓人。</summary>
	private bool _reacting;

	private bool _arresting;

	/// <summary>记进"看到听到"（失主之后找上门、对手来取笑都从这里来）。</summary>
	public Action<Observation>? Witness { get; set; }

	/// <summary>停业整顿中。</summary>
	public bool Suspended => State is MartState s && s.ClosedUntil > Today;

	public int ClosedUntil => State?.ClosedUntil ?? -1;

	private static int Season => Game1.year * 4 + Game1.seasonIndex;

	private void Caught(StoreSale sale, NPC buyer, NPC? speaker)
	{
		if (State is not MartState s)
		{
			return;
		}
		string owner = sale.StolenFrom.Length > 0 ? sale.StolenFrom : buyer.Name;
		bool own = string.Equals(owner, buyer.Name, StringComparison.OrdinalIgnoreCase);
		_ledger.Reclaim(owner, sale.ItemId, sale.Qty, sale.Quality);

		int strike = FenceRules.Strike(s.FenceSeason, s.FenceStrikes, Season);
		s.FenceSeason = Season;
		s.FenceStrikes = strike;
		int fine = Math.Min(Game1.player.Money, FenceRules.Fine(sale.MarketPrice * sale.Qty, strike));
		Game1.player.Money -= fine;
		int closed = FenceRules.ClosedDays(strike);
		if (closed > 0)
		{
			s.ClosedUntil = Math.Max(s.ClosedUntil, Today + closed);
		}
		bool arrest = FenceRules.Arrest(strike, _config().FenceArrest);
		if (arrest)
		{
			s.ArrestPending = true;
		}
		s.Reputation = Math.Clamp(s.Reputation - FenceRules.ReputationDrop(strike), -100, 100);
		Content.LastFenceFine = fine;
		Content.LastFenceOwner = Display(owner);
		Content.LastFenceItem = sale.ItemName;
		Content.LastFenceStrike = strike;
		Content.LastFenceClosed = closed;
		Game1.player.mailForTomorrow.Add(MartContent.FenceFineMail);
		Dirty();

		FenceStyle? known = FenceRules.StyleOf(buyer.Name) ?? (_styles.TryGetValue(buyer.Name, out FenceStyle cached) ? cached : null);
		if (speaker != null)
		{
			_reacting = true;
			React(buyer, speaker, sale, owner, own, strike, known, style => Aftermath(sale, buyer, owner, own, strike, style, fine, closed, arrest, speaker));
			Bystanders(speaker, sale.ItemName);
		}
		else
		{
			Aftermath(sale, buyer, owner, own, strike, known ?? FenceStyle.Hot, fine, closed, arrest, null);
		}
		_log.Info($"{buyer.Name} 认出了赃物 {sale.ItemName}（失主 {owner}），这一季第 {strike} 次：罚 {fine}g，停业 {closed} 天{(arrest ? "，要被带走" : "")}。");
	}

	/// <summary>好感、心情、记仇和取笑、八卦、提示。</summary>
	private void Aftermath(StoreSale sale, NPC buyer, string owner, bool own, int strike, FenceStyle style, int fine, int closed, bool arrest, NPC? speaker)
	{
		int penalty = (int)Math.Round(_config().CaughtStealingPenalty * FenceRules.FriendshipFactor(style) * (strike >= 2 ? 1.5 : 1));
		Game1.player.changeFriendship(-penalty, buyer);
		if (!own && Game1.getCharacterFromName(owner) is NPC victim)
		{
			Game1.player.changeFriendship(-penalty / 2, victim);
			AdjustMood?.Invoke(owner, FenceRules.MoodDrop(FenceStyle.Hurt));
		}
		AdjustMood?.Invoke(buyer.Name, FenceRules.MoodDrop(style));

		// 记进"看到听到"：失主那边（之后照脾气找上门来吵、或者来挖苦），皮埃尔那边（对手幸灾乐祸）。
		if (Witness != null && Game1.getCharacterFromName(buyer.Name) is NPC real && real.currentLocation is GameLocation at)
		{
			NPC? rival = Game1.getCharacterFromName("Pierre");
			string summary = Tr.Get("mart.fenceSummary", "{{farmer}}的小超市在卖{{owner}}家被偷的{{item}}，被{{name}}当场认了出来。", ("farmer", Game1.player.Name), ("owner", Display(owner)), ("item", sale.ItemName), ("name", Display(buyer.Name)));
			Witness(new Observation("fenced_goods", Game1.player.Name, buyer.Name, at.NameOrUniqueName, summary, real.Tile,
				Observation.Map(("owners", owner), ("item", sale.ItemName), ("style", style.ToString().ToLowerInvariant()), ("strike", strike.ToString())),
				AlsoLocation: rival?.currentLocation?.NameOrUniqueName, AlsoTile: rival?.Tile));
		}

		string tail = arrest ? Tr.Get("mart.fenceTailArrest", "，要被带走") : closed > 0 ? Tr.Get("mart.fenceTailClosed", "，停业 {{days}} 天", ("days", closed)) : "";
		Hints.Hud(Tr.Get("mart.caughtHud", "{{name}}认出了赃物：罚款 {{gold}}g", ("name", Display(buyer.Name)), ("gold", fine)) + tail, 8000);
		string gossip = strike >= 3
			? Tr.Get("mart.caughtGossipArrest", "{{farmer}}又在自己店里卖赃物，这回是{{owner}}家的{{item}}，被{{name}}认了出来——这一季都第三回了，听说直接被带走了，店也封了。", ("farmer", Game1.player.Name), ("owner", Display(owner)), ("item", sale.ItemName), ("name", Display(buyer.Name)))
			: strike == 2
				? Tr.Get("mart.caughtGossipAgain", "{{farmer}}的店又被认出卖赃物了，{{owner}}家的{{item}}，{{name}}当场闹了起来。镇长罚了 {{gold}}g，还让店停业整顿。", ("farmer", Game1.player.Name), ("owner", Display(owner)), ("item", sale.ItemName), ("name", Display(buyer.Name)), ("gold", fine))
				: Tr.Get("mart.caughtGossip", "{{name}}在{{farmer}}的店里认出了{{owner}}家被偷的{{item}}，当场吵了起来，镇长罚了{{farmer}} {{gold}}g。", ("name", Display(buyer.Name)), ("farmer", Game1.player.Name), ("owner", Display(owner)), ("item", sale.ItemName), ("gold", fine));
		Gossip?.Invoke(gossip, new[] { buyer.Name, owner, "Lewis" }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
		_reacting = false;
	}

	// ---------------- 当场发作 ----------------

	private static string StyleHint(FenceStyle s) => s switch
	{
		FenceStyle.Cold => Tr.Get("mart.fenceStyleCold", "你的脸一下子冷到了冰点。你不吼不叫，只盯着对方，一字一句说一句又冷又狠、让人后背发凉的话，说完就当这个人不存在了。"),
		FenceStyle.Hurt => Tr.Get("mart.fenceStyleHurt", "你眼眶一下就红了，声音发抖，又委屈又心寒：你一直把对方当朋友，没想到会这样。说着说着快哭出来了。"),
		FenceStyle.Snide => Tr.Get("mart.fenceStyleSnide", "你先是冷笑一声，然后阴阳怪气、往死里损对方：挖苦这家店、挖苦对方的人品，句句带刺，还故意说得让旁边的人都听见。"),
		_ => Tr.Get("mart.fenceStyleHot", "你当场气炸了：拍柜台、跳脚、大声嚷嚷，让整个店的人都回头看。骂得很冲（不说脏话），扬言要去找镇长、要让全镇都知道。")
	};

	private static string Portrait(FenceStyle s) => s switch
	{
		FenceStyle.Hurt => "$s",
		FenceStyle.Snide => "$u",
		FenceStyle.Cold => "",
		_ => "$a"
	};

	/// <summary>失主当场的反应：先按脾气做动作，再说一句（AI 按人设、按脾气；不认识的 NPC 让 AI 顺便判断脾气）。</summary>
	private void React(NPC buyer, NPC speaker, StoreSale sale, string owner, bool own, int strike, FenceStyle? known, Action<FenceStyle> done)
	{
		string fallbackFor(FenceStyle st) => st switch
		{
			FenceStyle.Cold => own ? Tr.Get("mart.caughtOwnCold", "……这是我家的{{item}}。我记住你了。", ("item", sale.ItemName)) : Tr.Get("mart.caughtKinCold", "……{{owner}}家丢的{{item}}，在你这儿。我会记住的。", ("item", sale.ItemName), ("owner", Display(owner))),
			FenceStyle.Hurt => own ? Tr.Get("mart.caughtOwnHurt", "这……这是我家丢的{{item}}……我一直把你当朋友的，你怎么能这样……", ("item", sale.ItemName)) : Tr.Get("mart.caughtKinHurt", "这是{{owner}}家丢的{{item}}吧……原来是你……我真的好失望。", ("item", sale.ItemName), ("owner", Display(owner))),
			FenceStyle.Snide => own ? Tr.Get("mart.caughtOwnSnide", "哟，这不是我家丢的{{item}}吗？进货渠道挺特别啊，老板——偷来的东西卖得还挺贵！", ("item", sale.ItemName)) : Tr.Get("mart.caughtKinSnide", "哈，{{owner}}家丢的{{item}}跑你货架上了？这店的货源可真是“新鲜”啊！", ("item", sale.ItemName), ("owner", Display(owner))),
			_ => own ? Tr.Get("mart.caughtOwn", "这不是我家丢的{{item}}吗？！你偷了我的东西还拿出来卖？我要去找镇长！", ("item", sale.ItemName)) : Tr.Get("mart.caughtKin", "等等，这{{item}}是{{owner}}家丢的那个吧？你居然卖赃物！我这就去告诉镇长！", ("item", sale.ItemName), ("owner", Display(owner)))
		};
		void Show(FenceStyle st, string line)
		{
			Act(speaker, st);
			if (Game1.currentLocation == speaker.currentLocation && Game1.activeClickableMenu == null && !Game1.eventUp)
			{
				Game1.DrawDialogue(new StardewValley.Dialogue(speaker, null, line + Portrait(st)));
			}
			else
			{
				NpcSpeech.Say(speaker, line, 6000);
			}
			done(st);
		}
		if (Ai is not IAiSender ai || !_config().AiOn)
		{
			FenceStyle st = known ?? FenceStyle.Hot;
			Show(st, fallbackFor(st));
			return;
		}
		string situation = own
			? Tr.Get("mart.caughtOwnPrompt", "你在{{farmer}}开的农夫小超市里，一眼认出货架上卖的{{item}}正是你家前几天被偷的那件！小偷就是眼前这个店主。", ("farmer", Game1.player.Name), ("item", sale.ItemName))
			: Tr.Get("mart.caughtKinPrompt", "你在{{farmer}}开的农夫小超市里，一眼认出货架上卖的{{item}}是{{owner}}家前几天被偷的那件！小偷就是眼前这个店主。", ("farmer", Game1.player.Name), ("item", sale.ItemName), ("owner", Display(owner)));
		if (strike >= 2)
		{
			situation += Tr.Get("mart.fenceAgainPrompt", "而且这已经是这一季第 {{n}} 次有人在这家店认出赃物了，全镇都在传。", ("n", strike));
		}
		string styleBlock = known is FenceStyle k
			? Tr.Get("mart.fencePromptStyle", "# 你现在的反应\n{{hint}}", ("hint", StyleHint(k)))
			: Tr.Get("mart.fencePromptPick", "# 你现在的反应\n按你的性格，从下面四种里挑最像你的一种，照它演：\n- hot：{{hot}}\n- cold：{{cold}}\n- hurt：{{hurt}}\n- snide：{{snide}}",
				("hot", StyleHint(FenceStyle.Hot)), ("cold", StyleHint(FenceStyle.Cold)), ("hurt", StyleHint(FenceStyle.Hurt)), ("snide", StyleHint(FenceStyle.Snide)));
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(buyer.Name)}。\n{Persona?.Invoke(buyer.Name) ?? ""}\n\n# 现在\n{situation}\n\n{styleBlock}\n\n"
			+ Tr.Get("mart.fencePromptOut", "# 怎么说\n这是游戏里情绪最激烈的一幕，反应一定要大、要有戏，像电视剧里的名场面：不能平淡、不能客气、不能讲道理、不能一句带过。用你自己的说话习惯说两三句（至少两句，不少于 30 个字），点明是什么东西、是谁家的，再狠狠表态（要找镇长、要让全镇知道、再也不来了、记你一辈子……），30～80 个字，可以用感叹号、问号和省略号。不写动作、不加引号。\n\n# 输出\n只输出 JSON：{\"style\":\"hot|cold|hurt|snide\",\"line\":\"你说的话\"}");
		Task.Run(async () =>
		{
			FenceStyle st = known ?? FenceStyle.Hot;
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, "说。", 700, 0.95)).ConfigureAwait(false);
				if (r.Ok && JsonSlice(r.Text ?? "") is string json)
				{
					using JsonDocument doc = JsonDocument.Parse(json);
					if (known == null && doc.RootElement.TryGetProperty("style", out JsonElement se))
					{
						st = FenceRules.Parse(se.GetString());
					}
					line = doc.RootElement.TryGetProperty("line", out JsonElement le) ? Said(le.GetString()) : null;
				}
			}
			catch (Exception ex)
			{
				_log.Debug("失主的话没生成：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				if (known == null)
				{
					_styles[buyer.Name] = st;
				}
				Show(st, string.IsNullOrWhiteSpace(line) ? fallbackFor(st) : line!);
			});
		});
	}

	/// <summary>整理 AI 说的话（比孩子、顾客的短句长，最多 120 字）。</summary>
	private static string? Said(string? text)
	{
		string t = AISV.Core.Dialogue.TextTools.CleanForGame(AISV.Core.Dialogue.TextTools.Truncate((text ?? "").Trim().Trim('"', '“', '”'), 120));
		return string.IsNullOrWhiteSpace(t) ? null : t;
	}

	private static string? JsonSlice(string text)
	{
		int a = text.IndexOf('{'), b = text.LastIndexOf('}');
		return a >= 0 && b > a ? text.Substring(a, b - a + 1) : null;
	}

	/// <summary>按脾气做动作：气得跳脚 / 冷冷地"……" / 难过 / 冷笑。</summary>
	private static void Act(NPC n, FenceStyle s)
	{
		switch (s)
		{
			case FenceStyle.Hot:
				n.doEmote(12);
				n.jump();
				n.shake(800);
				Game1.playSound("clubhit");
				break;
			case FenceStyle.Cold:
				n.doEmote(40);
				Game1.playSound("breathout");
				break;
			case FenceStyle.Hurt:
				n.doEmote(28);
				Game1.playSound("ow");
				break;
			default:
				n.doEmote(56);
				Game1.playSound("dwop");
				break;
		}
	}

	/// <summary>店里其他顾客的反应：爱看热闹的笑出声，正派的倒吸一口气，心软的叹气。</summary>
	private void Bystanders(NPC speaker, string item)
	{
		int i = 0;
		foreach (Visit v in _visits.Where(v => v.Clone != speaker && v.Clone.currentLocation == speaker.currentLocation).Take(3))
		{
			FenceStyle st = FenceRules.StyleOf(v.Real.Name) ?? FenceStyle.Hot;
			NPC c = v.Clone;
			string line = st switch
			{
				FenceStyle.Snide => Tr.Get("mart.fenceSeeSnide", "噗——卖赃物被抓现行？笑死我了！"),
				FenceStyle.Hurt => Tr.Get("mart.fenceSeeHurt", "天哪……怎么会这样……"),
				FenceStyle.Cold => Tr.Get("mart.fenceSeeCold", "……啧。"),
				_ => Tr.Get("mart.fenceSeeHot", "什么？！这店卖偷来的东西？！")
			};
			DelayedAction.functionAfterDelay(() =>
			{
				c.doEmote(st == FenceStyle.Snide ? 56 : 16);
				NpcSpeech.Say(c, line, 4000);
			}, 1200 + 900 * i++);
		}
	}

	// ---------------- 带走 ----------------

	/// <summary>每帧看一眼：有要带走的、你又空着手（不在过场、菜单、节日里），就来了。</summary>
	private void TickArrest()
	{
		if (_arresting || _reacting || State is not MartState s || !s.ArrestPending || !Context.IsPlayerFree || Game1.eventUp || Game1.isFestival() || Game1.timeOfDay >= 2400 || Game1.ticks % 30 != 0)
		{
			return;
		}
		s.ArrestPending = false;
		Dirty();
		Arrest();
	}

	/// <summary>调试：让某个村民站到你旁边，当场认出一件赃物（照常记一笔成交、照常算后果，只是不掷骰子）。</summary>
	public string DebugFence(string name, string? owner)
	{
		if (State is not MartState s || Game1.getCharacterFromName(name) is not NPC buyer)
		{
			return "没店，或者没有这个人。";
		}
		NPC? speaker = null;
		if (IsShop(Game1.currentLocation))
		{
			Point at = Game1.player.TilePoint + new Point(0, 1);
			speaker = Clone(buyer, Game1.currentLocation, at, ShopperReason);
			speaker.faceDirection(0);
			NPC c = speaker;
			DelayedAction.functionAfterDelay(() => c.currentLocation?.characters.Remove(c), 15000);
		}
		StoreSale sale = new StoreSale { Source = StoreSale.Ours, TxId = $"{Today}-{s.NextTx++}", Buyer = buyer.Name, ItemId = "(O)24", ItemName = ItemRegistry.Create("(O)24").DisplayName, Qty = 1, UnitPrice = 0, MarketPrice = 35, Stolen = true, StolenFrom = owner ?? buyer.Name, Day = Today, Time = Game1.timeOfDay, Reclaimed = true };
		Sales.Record(sale);
		Caught(sale, buyer, speaker);
		return $"{name} 认出了赃物，这一季第 {s.FenceStrikes} 次。";
	}

	/// <summary>调试：现在就被带走。</summary>
	public string DebugArrest()
	{
		if (State is not MartState s)
		{
			return "没店。";
		}
		s.ArrestPending = true;
		return "等你空下来就带走。";
	}

	private void Arrest()
	{
		_arresting = true;
		NPC? officer = Officers.Select(n => Game1.getCharacterFromName(n)).FirstOrDefault(n => n != null);
		// 装了 Law and Order 就关进它的牢里；警长、副警长还没在剧情里登场时由镇长押过去。
		bool jail = Game1.getLocationFromName(Jail) != null;
		officer ??= Game1.getCharacterFromName("Lewis");
		int taken = Confiscate();
		string fallback = jail
			? Tr.Get("mart.arrestOfficer", "{{farmer}}，这一季你店里已经第三回被认出卖赃物了。东西我们全扣了——跟我回局里，今晚就在牢里反省吧！", ("farmer", Game1.player.Name))
			: Tr.Get("mart.arrestLewis", "{{farmer}}！这一季第三回了！你把镇上的脸都丢尽了！店里的赃物全部没收，今天你哪儿也别想去，跟我走！", ("farmer", Game1.player.Name));
		void Speak(string line)
		{
			if (officer == null)
			{
				Lockup(jail, taken);
				return;
			}
			Game1.playSound("doorClose");
			Game1.DrawDialogue(new StardewValley.Dialogue(officer, null, line + "$a"));
			Game1.afterDialogues = () => Lockup(jail, taken);
		}
		if (officer == null || Ai is not IAiSender ai || !_config().AiOn)
		{
			Speak(fallback);
			return;
		}
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(officer.Name)}。\n{Persona?.Invoke(officer.Name) ?? ""}\n\n"
			+ Tr.Get("mart.arrestPrompt", "# 现在\n{{farmer}}开的农夫小超市这一季已经第三次被人认出在卖偷来的东西，全镇都在议论。你是来把{{farmer}}带走的，店里的赃物已经全部没收，店也封了。\n\n# 怎么说\n这是游戏里最严重的一幕，要有压迫感和戏剧性：严厉、愤怒、失望，宣布要带走对方{{where}}。60 个字以内，不写动作，不加引号，只写说出口的话。",
				("farmer", Game1.player.Name), ("where", jail ? Tr.Get("mart.arrestWhereJail", "关进牢里过一夜") : Tr.Get("mart.arrestWhereHome", "押回家闭门思过，今天哪儿也不许去")));
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, "说。", 300, 0.9)).ConfigureAwait(false);
				line = r.Ok ? Said(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug("带走的话没生成：" + ex.Message);
			}
			MainThread.Run(() => Speak(string.IsNullOrWhiteSpace(line) ? fallback : line!));
		});
	}

	/// <summary>关进牢里待一会儿（装了 Law and Order），再押回家，第二天在家醒来。传送本身会黑屏过渡，这里不再另外淡入淡出。</summary>
	private void Lockup(bool jail, int taken)
	{
		if (!jail)
		{
			SendHome();
			return;
		}
		LocationRequest req = Game1.getLocationRequest(Jail);
		req.OnWarp += () =>
		{
			Game1.player.faceDirection(0);
			Hints.Hud(Tr.Get("mart.arrestJailHud", "你被关进了牢里。"), 6000);
			DelayedAction.functionAfterDelay(SendHome, 6000);
		};
		Game1.warpFarmer(req, 10, 22, 0);
	}

	private void SendHome()
	{
		FarmHouse home = Utility.getHomeOfFarmer(Game1.player);
		Point bed = home.GetPlayerBedSpot();
		LocationRequest req = Game1.getLocationRequest(home.NameOrUniqueName);
		req.OnWarp += () =>
		{
			Hints.Hud(Tr.Get("mart.arrestHomeHud", "你被押回了家，今天就到这儿了。"), 6000);
			_arresting = false;
			DelayedAction.functionAfterDelay(() =>
			{
				// 当天结束：直接睡下（像晕倒那样第二天在家醒来，但不扣晕倒的钱）。
				if (AccessTools.Method(typeof(GameLocation), "startSleep") is { } sleep && Game1.currentLocation is FarmHouse here)
				{
					Game1.player.isInBed.Value = true;
					sleep.Invoke(here, null);
				}
			}, 2500);
		};
		Game1.warpFarmer(req, bed.X, bed.Y, 2);
	}

	/// <summary>没收店里（货架和库房）所有赃物，返回件数。</summary>
	private int Confiscate()
	{
		int n = 0;
		bool Stolen(Item? i) => i?.modData.TryGetValue("StardewThief/IsStolen", out string? v) == true && v == "true";
		foreach ((GameLocation _, Furniture f) in AllShelves())
		{
			StardewValley.Inventories.Inventory slots = MartShelves.Slots(f);
			for (int i = 0; i < slots.Count; i++)
			{
				if (Stolen(slots[i]))
				{
					n += slots[i].Stack;
					slots[i] = null;
				}
			}
		}
		StardewValley.Inventories.Inventory room = MartShelves.Stockroom();
		for (int i = 0; i < room.Count; i++)
		{
			if (Stolen(room[i]))
			{
				n += room[i].Stack;
				room[i] = null;
			}
		}
		if (Shops().FirstOrDefault() is GameLocation shop)
		{
			MartShelves.RefreshDisplay(shop);
		}
		return n;
	}
}
