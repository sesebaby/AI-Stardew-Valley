using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Dialogue;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Objects;

namespace AISV.Game.Store;

/// <summary>
/// 和 Pierre、Joja 抢生意：每天按三家店的吸引力分镇上的购物，一周份额掉得多了对手就吃紧 → 亏损 → 停业整顿。
/// 只改 AI星露谷 自己的经济：营业时间变短、少几样杂货、门口贴告示、心情和话题变；原版剧情标记不动，种子一样不少。
/// 亏损以后对手会找上门理论：讲道理 / 让一步 / 动手（像原版过场那样一拳，不伤人，但要罚款、上周报）。
/// </summary>
public sealed partial class MartService
{
	/// <summary>停业整顿时 Pierre 店里先不卖的杂货（种子一样不动）。</summary>
	private static readonly string[] PierreExtras = { "(O)245", "(O)246", "(O)247", "(O)419", "(O)423" };

	private static readonly string[] JojaExtras = { "(O)245", "(O)246", "(O)247", "(O)419", "(O)423", "(O)167" };

	private int _rivalNoticeDay = -1;

	private bool _confronting;

	public static bool JojaOpen => !Game1.MasterPlayer.mailReceived.Contains("ccIsComplete") && !Game1.MasterPlayer.mailReceived.Contains("abandonedJojaMartAccessible") && !Game1.MasterPlayer.mailReceived.Contains("ccMovieTheater");

	public int Health(string rival) => _config().MartCompetition && State?.Rivals.TryGetValue(rival, out RivalState? r) == true ? r!.Health : Competition.Normal;

	/// <summary>给账本：这家店还剩几成生意（最近一周的份额 ÷ 没有我们时的份额）。</summary>
	public double ShareLeft(string rival)
	{
		MartState? s = State;
		if (!_config().MartCompetition || s == null || s.Shares.Count == 0 || s.Rivals.TryGetValue(rival, out RivalState? r) && r.Partner)
		{
			return 1;
		}
		(double np, double nj, double _) = Competition.Shares(0, 1, 0, JojaOpen);
		List<DayShare> week = s.Shares.Where(d => Today - d.Day < 7).ToList();
		if (week.Count == 0)
		{
			return 1;
		}
		double share = rival == Competition.Pierre ? week.Average(d => d.Pierre) : week.Average(d => d.Joja);
		double normal = rival == Competition.Pierre ? np : nj;
		return normal <= 0 ? 1 : Math.Clamp(share / normal, 0, 1);
	}

	private void UpdateCompetition(MartState s)
	{
		if (!_config().MartCompetition)
		{
			return;
		}
		(int kinds, double ratio) = Offerings();
		bool joja = JojaOpen;
		// 从 Pierre 进货等于照顾他的生意：最近一周每进 3000g 的货，他的份额底子多 1（最多 0.6）。
		double support = Math.Min(0.6, s.Delivered.Where(d => d.Supplier == "Pierre" && Today - d.Day < 7).Sum(d => d.Cost) / 3000.0);
		(double p, double j, double o) = Competition.Shares(kinds, ratio, s.Reputation, joja, pierreBoost: support);
		s.Shares.Add(new DayShare { Day = Today, Pierre = p, Joja = j, Ours = o });
		s.Shares.RemoveAll(d => Today - d.Day > 28);
		foreach (string rival in new[] { Competition.Pierre, Competition.Joja })
		{
			if (!s.Rivals.TryGetValue(rival, out RivalState? r))
			{
				s.Rivals[rival] = r = new RivalState();
			}
			if (rival == Competition.Joja && !joja)
			{
				r.Health = Competition.Normal;
				continue;
			}
			int before = r.Health;
			r.Health = r.Partner ? Competition.Normal : Competition.Health(ShareLeft(rival), 1, r.Health);
			if (r.Health != before)
			{
				Announce(rival, before, r.Health);
			}
			if (r.Health >= Competition.Losing)
			{
				AdjustMood?.Invoke(Owner(rival), -6);
			}
		}
		_store()!.MarkDirty(MartState.Path);
		Content.ShopsDirty = true;
	}

	private static string Owner(string rival) => rival == Competition.Pierre ? "Pierre" : "Morris";

	private string ShopName(string rival) => rival == Competition.Pierre ? Tr.Get("mart.pierreShop", "皮埃尔的杂货店") : Tr.Get("mart.jojaShop", "Joja 超市");

	public static string HealthName(int h) => h switch
	{
		Competition.Tight => Tr.Get("mart.health.tight", "吃紧"),
		Competition.Losing => Tr.Get("mart.health.losing", "亏损"),
		Competition.Closed => Tr.Get("mart.health.closed", "停业整顿"),
		_ => Tr.Get("mart.health.normal", "正常")
	};

	/// <summary>经营状况变了：镇上传开，对手反击（打折），停业整顿时贴告示。</summary>
	private void Announce(string rival, int before, int now)
	{
		string shop = ShopName(rival);
		string owner = Owner(rival);
		string text = now > before
			? now switch
			{
				Competition.Tight => Tr.Get("mart.newsTight", "{{shop}}最近客人少了，听说是被{{farmer}}的小超市抢了生意。", ("shop", shop), ("farmer", Game1.player.Name)),
				Competition.Losing => Tr.Get("mart.newsLosing", "{{shop}}这个月在亏钱，{{owner}}愁得睡不着。", ("shop", shop), ("owner", Display(owner))),
				_ => Tr.Get("mart.newsClosed", "{{shop}}贴出告示：停业整顿，缩短营业时间。", ("shop", shop))
			}
			: Tr.Get("mart.newsBetter", "{{shop}}的生意慢慢回来了。", ("shop", shop));
		Gossip?.Invoke(text, new[] { owner });
		Hints.Hud(text, 9000);
		_log.Info($"{rival} 的经营状况：{HealthName(before)} → {HealthName(now)}。");
	}

	/// <summary>换了地方：走进对手的店（或镇上）时，有告示就提一句（每天一次）。</summary>
	public void OnWarped(WarpedEventArgs e)
	{
		if (!_config().FarmerMart || !_config().MartCompetition || !e.IsLocalPlayer || _rivalNoticeDay == Today)
		{
			return;
		}
		string? rival = e.NewLocation.Name switch
		{
			"SeedShop" => Competition.Pierre,
			"JojaMart" => Competition.Joja,
			_ => null
		};
		if (rival != null && Health(rival) >= Competition.Losing)
		{
			_rivalNoticeDay = Today;
			(int open, int close) = Competition.Hours(900, rival == Competition.Pierre ? 1700 : 2300, Health(rival));
			Hints.Hud(Tr.Get("mart.notice", "门口贴着告示：“{{state}}，营业时间 {{open}} - {{close}}，部分商品暂停供应。”", ("state", HealthName(Health(rival))), ("open", Clock(open)), ("close", Clock(close))), 9000);
		}
	}

	private static string Clock(int t) => $"{t / 100}:{t % 100:00}";

	/// <summary>Data/Shops：亏损以后少卖几样杂货（种子一样不少）。</summary>
	public void EditShops(AssetRequestedEventArgs e)
	{
		if (!e.NameWithoutLocale.IsEquivalentTo("Data/Shops") || !_config().FarmerMart || !_config().MartCompetition || !Context.IsWorldReady)
		{
			return;
		}
		int pierre = Health(Competition.Pierre), joja = Health(Competition.Joja);
		e.Edit(asset =>
		{
			IDictionary<string, ShopData> data = asset.AsDictionary<string, ShopData>().Data;
			if (pierre >= Competition.Losing && data.TryGetValue("SeedShop", out ShopData? seed))
			{
				seed.Items.RemoveAll(i => PierreExtras.Contains(i.ItemId));
			}
			if (joja >= Competition.Losing && data.TryGetValue("Joja", out ShopData? jm))
			{
				jm.Items.RemoveAll(i => JojaExtras.Contains(i.ItemId));
			}
		}, StardewModdingAPI.Events.AssetEditPriority.Late);
	}

	/// <summary>对手店的营业时间：停业整顿时开得晚、关得早（打开店之前问一下）。返回 false 表示这次不开。</summary>
	public bool AllowShop(string shopId)
	{
		string? rival = shopId switch
		{
			"SeedShop" => Competition.Pierre,
			"Joja" => Competition.Joja,
			_ => null
		};
		if (rival == null || Health(rival) < Competition.Losing)
		{
			return true;
		}
		(int open, int close) = Competition.Hours(900, rival == Competition.Pierre ? 1700 : 2300, Health(rival));
		if (Game1.timeOfDay >= open && Game1.timeOfDay < close)
		{
			return true;
		}
		Game1.drawObjectDialogue(Tr.Get("mart.closedNow", "{{state}}中，营业时间 {{open}} - {{close}}。", ("state", HealthName(Health(rival))), ("open", Clock(open)), ("close", Clock(close))));
		return false;
	}

	/// <summary>投资入股：花一笔钱让对手缓过来，以后不再抢它的生意。</summary>
	public bool Invest(string rival, int gold)
	{
		if (State is not MartState s || Game1.player.Money < gold)
		{
			return false;
		}
		Game1.player.Money -= gold;
		if (!s.Rivals.TryGetValue(rival, out RivalState? r))
		{
			s.Rivals[rival] = r = new RivalState();
		}
		r.Partner = true;
		r.Health = Competition.Normal;
		_ledger.Pay(Owner(rival), gold, Tr.Get("mart.investNote", "{{farmer}}投资入股", ("farmer", Game1.player.Name)));
		Game1.player.changeFriendship(120, Game1.getCharacterFromName(Owner(rival)));
		_store()!.MarkDirty(MartState.Path);
		Content.ShopsDirty = true;
		Gossip?.Invoke(Tr.Get("mart.investGossip", "{{farmer}}给{{shop}}投了钱，两家店不再抢生意了。", ("farmer", Game1.player.Name), ("shop", ShopName(rival))), new[] { Owner(rival) });
		return true;
	}

	// ---------------- 上门理论 ----------------

	private void TryConfront(int time)
	{
		MartState? s = State;
		if (_confronting || s == null || !_config().MartCompetition || !IsShop(Game1.currentLocation) || !Context.IsPlayerFree || time % 100 != 0)
		{
			return;
		}
		int season = Game1.year * 4 + Game1.seasonIndex;
		foreach (string rival in new[] { Competition.Pierre, Competition.Joja })
		{
			if (s.Rivals.TryGetValue(rival, out RivalState? r) && !r.Partner && Competition.MayConfront(r.Health, r.ConfrontedSeason, season, _random.NextDouble())
				&& Game1.getCharacterFromName(Owner(rival)) is NPC real && !real.IsInvisible && !NpcBusy.Is(real.Name))
			{
				r.ConfrontedSeason = season;
				_store()!.MarkDirty(MartState.Path);
				Confront(rival, real);
				return;
			}
		}
	}

	/// <summary>调试：叫对手现在就来理论。</summary>
	public string ForceConfront(string rival)
	{
		rival = rival.Equals("Morris", StringComparison.OrdinalIgnoreCase) || rival.Equals("Joja", StringComparison.OrdinalIgnoreCase) ? Competition.Joja : Competition.Pierre;
		if (!IsShop(Game1.currentLocation) || Game1.getCharacterFromName(Owner(rival)) is not NPC real)
		{
			return "先走进农夫小超市。";
		}
		Confront(rival, real);
		return $"{real.Name} 来了。";
	}

	private void Confront(string rival, NPC real)
	{
		_confronting = true;
		GameLocation shop = Game1.currentLocation;
		NPC clone = Clone(real, shop, Entry(shop), RivalReason);
		real.IsInvisible = true;
		NpcBusy.Hold(real.Name, RivalReason);
		Game1.playSound("doorClose");
		Point near = Game1.player.TilePoint + new Point(0, 1);
		clone.controller = new StardewValley.Pathfinding.PathFindController(clone, shop, near, 0, (c, l) => Arrive(rival, real, clone));
		if (clone.controller.pathToEndPoint == null)
		{
			clone.controller = null;
			Arrive(rival, real, clone);
		}
	}

	private void Arrive(string rival, NPC real, NPC clone)
	{
		clone.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
		clone.doEmote(12);
		Game1.playSound("angryDuck");
		string fallback = rival == Competition.Pierre
			? Tr.Get("mart.pierreAngry", "你开这个店，是要把我的杂货店挤垮吗？！我一家老小还要吃饭的！")
			: Tr.Get("mart.morrisAngry", "你的小店正在影响 Joja 的季度业绩。我们得好好谈谈。");
		Line(real.Name, Tr.Get("mart.angryPrompt", "你是{{shop}}的店主。{{farmer}}开的农夫小超市抢走了你很多生意，你的店现在{{state}}。你冲进了{{farmer}}的店里，要当面质问。说一句。", ("shop", ShopName(rival)), ("farmer", Game1.player.Name), ("state", HealthName(Health(rival)))), fallback, line =>
		{
			Game1.DrawDialogue(new StardewValley.Dialogue(clone, null, line));
			Game1.afterDialogues = () => Choices(rival, real, clone, line);
		});
	}

	private void Choices(string rival, NPC real, NPC clone, string said)
	{
		List<Response> answers = new List<Response>
		{
			new Response("argue", Tr.Get("mart.choiceArgue", "讲道理")),
			new Response("concede", Tr.Get("mart.choiceConcede", "让一步（一周内涨价两成）"))
		};
		if (_config().AllowBrawl)
		{
			answers.Add(new Response("brawl", Tr.Get("mart.choiceBrawl", "动手（会被罚款、上周报）")));
		}
		// 不另写提示语：对方刚吼完那句就是问题，下面直接给三个选项。
		Game1.currentLocation.createQuestionDialogue("", answers.ToArray(), (who, answer) =>
		{
			switch (answer)
			{
				case "argue":
					Argue(rival, real, clone, said);
					break;
				case "concede":
					Concede(rival, real, clone);
					break;
				case "brawl":
					Brawl(rival, real, clone);
					break;
				default:
					// 没选就关掉了：对方气呼呼地走了。
					Leave(real, clone, angry: true);
					break;
			}
		});
	}

	private void Argue(string rival, NPC real, NPC clone, string said)
	{
		if (AskArgument == null || Ai == null)
		{
			Concede(rival, real, clone);
			return;
		}
		AskArgument(clone, said, text =>
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				Leave(real, clone, angry: true);
				return;
			}
			string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(real.Name)}，{ShopName(rival)}的店主。\n{Persona?.Invoke(real.Name) ?? ""}\n\n# 现在\n{Game1.player.Name}开的农夫小超市抢了你的生意，你找上门理论，刚说了：“{said}”。\n\n# 输出\n只输出 JSON：{{\"says\":\"你的回答，40 字以内\",\"convinced\":true 或 false}}。对方说得在理、态度诚恳就 convinced=true。";
			string user = "对方说：" + AISV.Core.Prompts.PromptText.Wrap("PLAYER", text, 300);
			IAiSender ai = Ai;
			Task.Run(async () =>
			{
				(string Says, bool Convinced)? result = null;
				try
				{
					AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, user, 400, 0.8)).ConfigureAwait(false);
					result = r.Ok ? ParseArgue(r.Text) : null;
				}
				catch (Exception ex)
				{
					_log.Debug("理论的回答没生成：" + ex.Message);
				}
				MainThread.Run(() =>
				{
					(string says, bool convinced) = result ?? (Tr.Get("mart.argueFallback", "……哼，我回去想想。"), false);
					Game1.DrawDialogue(new StardewValley.Dialogue(clone, null, says));
					Game1.afterDialogues = () =>
					{
						Game1.player.changeFriendship(convinced ? 30 : -60, real);
						AdjustMood?.Invoke(real.Name, convinced ? 5 : -8);
						Leave(real, clone, angry: !convinced);
					};
				});
			});
		});
	}

	private static (string, bool)? ParseArgue(string? text)
	{
		string raw = text ?? "";
		int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
		if (a < 0 || b <= a)
		{
			return null;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(raw.Substring(a, b - a + 1));
			string says = doc.RootElement.TryGetProperty("says", out JsonElement s) ? s.GetString() ?? "" : "";
			bool ok = doc.RootElement.TryGetProperty("convinced", out JsonElement c) && c.ValueKind == JsonValueKind.True;
			return says.Length == 0 ? null : (says, ok);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private void Concede(string rival, NPC real, NPC clone)
	{
		if (State is MartState s)
		{
			s.ConcedeUntil = Today + 7;
			_store()!.MarkDirty(MartState.Path);
		}
		Game1.player.changeFriendship(40, real);
		Hints.Hud(Tr.Get("mart.conceded", "你答应一周内涨价两成。{{name}}的脸色好看多了。", ("name", Display(real.Name))), 8000);
		Leave(real, clone, angry: false);
	}

	/// <summary>一拳的过程：照原版莉亚 14 心事件里打凯尔那一拳的节奏，按帧推进。</summary>
	private sealed class Punch
	{
		public NPC Clone = null!;

		/// <summary>从玩家指向对手的方向（单位向量），对手往这边退、玩家往这边探身。</summary>
		public Vector2 Away;

		public int Ticks;

		public Action Done = () => { };
	}

	private Punch? _punch;

	// 原版：闪一下屏、玩家出拳（第 15 帧 650 毫秒、往前探 8 像素）、clubhit、对手跳起并每 25 毫秒退 4 像素共 8 次，
	// 停 200 毫秒、clubSmash、对手抖 1 秒。下面按 60 帧/秒换算。
	private const int PushTicks = 12, SmashTick = 24, DoneTick = 84;

	/// <summary>动手：玩家一拳，对手被打得往后退、跳起、抖一下，然后气呼呼地走出门。后果：掉一颗心、镇长罚款、上周报、镇上传开。</summary>
	private void Brawl(string rival, NPC real, NPC clone)
	{
		Farmer me = Game1.player;
		me.faceGeneralDirection(clone.getStandingPosition(), 0, false, false);
		clone.controller = null;
		clone.Halt();
		clone.faceGeneralDirection(me.getStandingPosition(), 0, false, false);
		Vector2 away = clone.getStandingPosition() - me.getStandingPosition();
		away = away == Vector2.Zero ? new Vector2(0, 1) : Vector2.Normalize(away);
		Game1.flashAlpha = 0.25f;
		me.FarmerSprite.animateOnce(new[] { new FarmerSprite.AnimationFrame(15, 650, secondaryArm: false, flip: false) });
		me.Position += away * 8f;
		Game1.playSound("clubhit");
		clone.jump();
		_punch = new Punch
		{
			Clone = clone,
			Away = away,
			Done = () =>
			{
				int fine = Math.Min(Game1.player.Money, Math.Clamp(Game1.player.Money / 10, 200, 1000));
				Game1.player.Money -= fine;
				Content.LastFine = fine;
				Game1.player.mailForTomorrow.Add(MartContent.FineMail);
				Game1.player.changeFriendship(-250, real);
				AdjustMood?.Invoke(real.Name, -20);
				if (State is MartState s)
				{
					s.Reputation = Math.Clamp(s.Reputation - 15, -100, 100);
					if (s.Rivals.TryGetValue(rival, out RivalState? r))
					{
						r.Brawls++;
					}
					_store()!.MarkDirty(MartState.Path);
				}
				Gossip?.Invoke(Tr.Get("mart.brawlGossip", "{{farmer}}在自己店里和{{name}}动了手，一拳把{{name}}打得连退好几步，镇长罚了 {{gold}}g。", ("farmer", Game1.player.Name), ("name", Display(real.Name)), ("gold", fine)), new[] { real.Name, "Lewis" });
				Hints.Hud(Tr.Get("mart.brawlHud", "{{name}}挨了一拳，晕乎乎地走了。罚款 {{gold}}g，明天会收到镇长的信。", ("name", Display(real.Name)), ("gold", fine)), 10000);
				Leave(real, clone, angry: true);
			}
		};
	}

	/// <summary>每帧推进这一拳（ModEntry 的 UpdateTicked 调）。</summary>
	public void TickFly()
	{
		if (_punch is not Punch p)
		{
			return;
		}
		p.Ticks++;
		if (p.Ticks <= PushTicks && p.Ticks % 3 != 0)
		{
			// 每 25 毫秒退 4 像素：60 帧里大约每帧 2.7 像素，三帧里走两帧，正好两格出头不到。
			p.Clone.Position += p.Away * 4f;
		}
		if (p.Ticks == 4)
		{
			Game1.player.Position -= p.Away * 8f;
		}
		if (p.Ticks == SmashTick)
		{
			Game1.playSound("clubSmash");
			p.Clone.shake(1000);
		}
		if (p.Ticks >= DoneTick)
		{
			_punch = null;
			p.Done();
		}
	}

	private void Leave(NPC real, NPC clone, bool angry)
	{
		if (angry)
		{
			clone.doEmote(12);
		}
		void Gone()
		{
			clone.currentLocation?.characters.Remove(clone);
			real.IsInvisible = false;
			NpcBusy.Release(real.Name);
			_confronting = false;
		}
		// 自己走出门；走不过去就在门口消失。
		GameLocation? shop = clone.currentLocation;
		DelayedAction.functionAfterDelay(() =>
		{
			if (shop == null)
			{
				Gone();
				return;
			}
			clone.controller = new StardewValley.Pathfinding.PathFindController(clone, shop, Entry(shop), 2, (c, l) =>
			{
				Game1.playSound("doorClose");
				Gone();
			});
			if (clone.controller.pathToEndPoint == null)
			{
				clone.controller = null;
				Gone();
			}
		}, angry ? 900 : 300);
	}

	/// <summary>让对手说一句（AI 用不了时用固定的话）。</summary>
	private void Line(string npc, string situation, string fallback, Action<string> show)
	{
		if (Ai is not IAiSender ai || !_config().AiOn)
		{
			show(fallback);
			return;
		}
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(npc)}。\n{Persona?.Invoke(npc) ?? ""}\n\n# 现在\n{situation}\n\n# 输出\n只写你说出口的话，40 个字以内，不加引号，不写动作。";
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("brawl_line", system, "说。", 300, 0.9)).ConfigureAwait(false);
				line = r.Ok ? ChildPrompts.Clean(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug("对手的话没生成：" + ex.Message);
			}
			MainThread.Run(() => show(line ?? fallback));
		});
	}
}
