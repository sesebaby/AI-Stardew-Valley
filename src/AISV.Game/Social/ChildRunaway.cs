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
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace AISV.Game.Social;

/// <summary>
/// 孩子离家出走：上午有一定机会，会走路的孩子跑到农场上躲起来玩。真孩子留在屋里藏起来，农场上放一个替身；
/// 配偶来报信（不在身边就发消息），靠近能听到笑声，路上有小脚印，配偶每两小时提示一次方向。
/// 点到替身就算找到；到傍晚还没找到，配偶自己把孩子领回来。换天、存档、回标题时一定收回替身。
/// 只在单人或主机上。
/// </summary>
public sealed class ChildRunaway
{
	public const string HoldReason = "child_runaway";

	private const string LastPath = "family/runaway.json";

	private static readonly DocumentType<Dictionary<string, int>> LastDoc = new DocumentType<Dictionary<string, int>>("孩子出走记录", 1, () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ChildTalk _talk;

	private readonly ChildBook _book;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private sealed class Run
	{
		public Child Child = null!;

		public NPC StandIn = null!;

		public string Key = "";

		public Point Home;

		public Point Hide;

		public Point Clue;

		public int Started;

		public int LastHint;

		public bool Moved;

		public int GiggleTicks;

		public bool ClueSeen;

		public bool Found;
	}

	private Run? _run;

	private int _checkedDay = -1;

	public ChildRunaway(Func<ModConfig> config, Func<DocumentStore?> store, ChildTalk talk, ChildBook book, ILog log)
	{
		_config = config;
		_store = store;
		_talk = talk;
		_book = book;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Persona { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	/// <summary>养育记录：跑出去、找得快慢。</summary>
	public AISV.Core.Family.GrowthBook? Growth { get; set; }

	public Action<string, int>? AdjustMood { get; set; }

	/// <summary>记一件事给配偶（进八卦和聊天素材）：配偶名、这件事。</summary>
	public Action<string, string>? Remember { get; set; }

	public bool Active => _run != null;

	public void OnTimeChanged()
	{
		if (_run != null)
		{
			Tick(Game1.timeOfDay);
			return;
		}
		int day = Game1.Date.TotalDays;
		if (_checkedDay == day || Game1.timeOfDay < RunawayRules.CheckFrom || !Context.IsMainPlayer)
		{
			return;
		}
		_checkedDay = day;
		Child? child = Candidate();
		RunawayRules.Day d = new RunawayRules.Day(Game1.isRaining || Game1.isGreenRain || Game1.isLightning, Game1.isFestival() || Game1.eventUp,
			Game1.currentLocation is Farm or FarmHouse, child != null, child == null ? 0 : DaysSince(Kids.Key(child), day), Game1.timeOfDay);
		if (child != null && RunawayRules.MayRun(d, _config().ChildRunaway, _config().ChildRunawayPercent, _random.NextDouble()))
		{
			Start(child);
		}
	}

	private static Child? Candidate()
	{
		// 少年不玩躲猫猫了。
		List<Child> kids = Game1.player.getChildren().Where(c => Kids.CanTalk(c) && c.currentLocation is FarmHouse && !c.IsInvisible && KidGrowthService.Stage(c) != AISV.Core.Family.KidStages.Teen).ToList();
		return kids.Count == 0 ? null : kids[Game1.random.Next(kids.Count)];
	}

	private int DaysSince(string key, int today)
	{
		Dictionary<string, int>? last = _store()?.Get(LastPath, LastDoc);
		return last != null && last.TryGetValue(key, out int day) ? today - day : 999;
	}

	/// <summary>调试：现在就让一个孩子跑出去（返回结果说明）。</summary>
	public string Force()
	{
		if (_run != null)
		{
			return "已经有孩子在外面了。";
		}
		Child? child = Game1.player.getChildren().FirstOrDefault(c => Kids.CanTalk(c) && !c.IsInvisible);
		if (child == null)
		{
			return "没有会走路的孩子。";
		}
		return Start(child) ? $"{child.Name} 跑出去了，藏在 {_run!.Hide}。" : "没找到能藏的地方。";
	}

	private bool Start(Child child)
	{
		Farm farm = Game1.getFarm();
		Point home = farm.GetMainFarmHouseEntry();
		Point? hide = PickHide(farm, home);
		if (hide == null)
		{
			_log.Debug($"{child.Name} 想跑出去玩，但农场上没找到能藏的地方。");
			return false;
		}
		NPC standIn = new NPC(new AnimatedSprite(child.Sprite.textureName.Value, 0, child.Sprite.SpriteWidth, child.Sprite.SpriteHeight), new Vector2(hide.Value.X, hide.Value.Y) * 64f, "Farm", 2, "AISV_Runaway_" + child.Name, NpcBusy.BlankPortrait, eventActor: true)
		{
			displayName = child.displayName
		};
		standIn.AllowDynamicAppearance = false;
		standIn.modData[NpcBusy.StandInKey] = HoldReason;
		KidGrowthService.Dress(standIn, child);
		farm.characters.Add(standIn);
		standIn.currentLocation = farm;
		Wander(standIn, hide.Value);
		child.IsInvisible = true;
		_run = new Run
		{
			Child = child,
			StandIn = standIn,
			Key = Kids.Key(child),
			Home = home,
			Hide = hide.Value,
			Clue = Between(home, hide.Value),
			Started = Game1.timeOfDay,
			LastHint = Game1.timeOfDay
		};
		NpcBusy.Hold(child.Name, HoldReason);
		Growth?.Note(Kids.Key(child), "runaway", Game1.Date.TotalDays);
		Dictionary<string, int>? last = _store()?.Get(LastPath, LastDoc);
		if (last != null)
		{
			last[_run.Key] = Game1.Date.TotalDays;
			_store()!.MarkDirty(LastPath);
		}
		_log.Info($"{child.Name} 跑到农场上玩去了（藏在 {hide.Value}）。");
		Announce(child);
		return true;
	}

	/// <summary>在藏身点附近转悠。按事件演员造出来的 NPC 没有原地中心点（默认在地图左上角），要自己设上，不然会一路走远。</summary>
	private static void Wander(NPC standIn, Point at)
	{
		standIn.lastCrossroad = new Rectangle(at.X * 64, at.Y * 64, 64, 64);
		standIn.walkInSquare(3, 3, 4000);
	}

	private void Announce(Child child)
	{
		NPC? spouse = Spouse();
		string kid = child.displayName;
		string fallback = Tr.Get("runaway.spouseWorried", "{{child}}不见了！刚才还在屋里……你快去农场上找找！", ("child", kid));
		Hints.Hud(Tr.Get("runaway.hud", "{{child}}跑出去玩了，去农场上找找。靠近时能听到笑声，地上有小脚印。", ("child", kid)), 10000);
		if (spouse == null)
		{
			Game1.playSound("cancel");
			Hints.Hud(Tr.Get("runaway.alone", "屋里静悄悄的，{{child}}的小鞋子也不见了。", ("child", kid)), 9000);
		}
		else
		{
			Say(spouse, Tr.Get("runaway.worriedPrompt", "你的孩子{{child}}刚才还在屋里，现在不见了，大概跑到农场上去玩了。你很着急，跟{{farmer}}说一句，催对方去找。", ("child", kid), ("farmer", Game1.player.Name)), fallback, worried: true);
		}
		Hints.Tip("RunawayHint", Tr.Get("help.runaway.title", "孩子离家出走"));
	}

	/// <summary>配偶说一句：在身边就冒在头顶，不在身边就当成发来的消息。</summary>
	private void Say(NPC spouse, string situation, string fallback, bool worried)
	{
		void Show(string line)
		{
			if (spouse.currentLocation == Game1.currentLocation && !spouse.IsInvisible)
			{
				spouse.doEmote(worried ? 16 : 20);
				NpcSpeech.Say(spouse, line);
			}
			else
			{
				Game1.playSound("phone");
				Hints.Hud(Tr.Get("runaway.text", "{{spouse}}发来消息：{{line}}", ("spouse", Display(spouse.Name)), ("line", line)), 9000);
			}
		}
		if (Ai is not IAiSender ai || !_config().AiOn)
		{
			Show(fallback);
			return;
		}
		string system = $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是{Display(spouse.Name)}。\n{Persona?.Invoke(spouse.Name) ?? ""}\n\n# 现在\n{situation}\n\n# 输出\n只写你说出口的一句话，30 个字以内，不加引号，不写动作。";
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("child_runaway", system, "说一句。", 300, 0.9)).ConfigureAwait(false);
				line = r.Ok ? ChildPrompts.Clean(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug("配偶的话没生成：" + ex.Message);
			}
			MainThread.Run(() => Show(line ?? fallback));
		});
	}

	private static NPC? Spouse()
	{
		string? name = Game1.player.spouse;
		return string.IsNullOrWhiteSpace(name) || Game1.player.friendshipData.TryGetValue(name, out Friendship? f) && !(f.IsMarried() || f.IsRoommate()) ? null : Game1.getCharacterFromName(name);
	}

	private static Point Between(Point a, Point b) => new Point(a.X + (b.X - a.X) / 2, a.Y + (b.Y - a.Y) / 2);

	/// <summary>挑藏身点：随机看几百个格子，能走、空着、离家够远、旁边有东西挡着，再确认从家门走得过去。</summary>
	private Point? PickHide(Farm farm, Point home)
	{
		int w = farm.Map.Layers[0].LayerWidth, h = farm.Map.Layers[0].LayerHeight;
		List<(Point P, double Score)> picks = new List<(Point, double)>();
		for (int i = 0; i < 600; i++)
		{
			Point p = new Point(_random.Next(2, w - 2), _random.Next(2, h - 2));
			if (!Free(farm, p))
			{
				continue;
			}
			double score = RunawayRules.HideScore(Vector2.Distance(p.ToVector2(), home.ToVector2()), Cover(farm, p));
			if (score > 0)
			{
				picks.Add((p, score + _random.NextDouble() * 5));
			}
		}
		foreach ((Point p, double _) in picks.OrderByDescending(x => x.Score).Take(12))
		{
			try
			{
				if (PathFindController.findPathForNPCSchedules(home, p, farm, 30000) != null)
				{
					return p;
				}
			}
			catch
			{
				// 寻路出错就换下一个。
			}
		}
		return null;
	}

	private static bool Free(GameLocation l, Point p)
	{
		Vector2 t = p.ToVector2();
		return l.isTileOnMap(t) && l.isTilePassable(t) && !l.isWaterTile(p.X, p.Y) && !l.IsTileOccupiedBy(t) && !(l.terrainFeatures.TryGetValue(t, out TerrainFeature? f) && f is HoeDirt or Flooring);
	}

	/// <summary>周围一格内能挡住孩子的东西有几样。</summary>
	private static int Cover(GameLocation l, Point p)
	{
		int n = 0;
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -1; dy <= 1; dy++)
			{
				Vector2 t = new Vector2(p.X + dx, p.Y + dy);
				if (l.terrainFeatures.TryGetValue(t, out TerrainFeature? f) && f is Tree or FruitTree or Bush)
				{
					n++;
				}
				else if (l.objects.TryGetValue(t, out StardewValley.Object? o) && o.bigCraftable.Value)
				{
					n++;
				}
			}
		}
		n += l.resourceClumps.Count(c => c.getBoundingBox().Intersects(new Rectangle((p.X - 1) * 64, (p.Y - 1) * 64, 192, 192)));
		n += l.largeTerrainFeatures.Count(f => f.getBoundingBox().Intersects(new Rectangle((p.X - 1) * 64, (p.Y - 1) * 64, 192, 192)));
		return n;
	}

	/// <summary>每 10 分钟：方向提示、太久就换地方、到傍晚配偶领回来。</summary>
	private void Tick(int now)
	{
		Run r = _run!;
		if (r.Found)
		{
			return;
		}
		if (now >= RunawayRules.GiveUpAt)
		{
			Growth?.Note(r.Key, "slow", Game1.Date.TotalDays);
			GiveUp();
			return;
		}
		int elapsed = RunawayRules.Minutes(now) - RunawayRules.Minutes(r.Started);
		if (!r.Moved && elapsed >= RunawayRules.MoveAfterMinutes)
		{
			Farm farm = Game1.getFarm();
			if (PickHide(farm, r.Home) is Point p)
			{
				r.Moved = true;
				r.Hide = p;
				r.Clue = Between(r.Home, p);
				r.ClueSeen = false;
				Game1.warpCharacter(r.StandIn, farm, p.ToVector2());
				Wander(r.StandIn, p);
				_log.Debug($"{r.Child.Name} 换了个地方藏：{p}。");
			}
		}
		if (RunawayRules.Minutes(now) - RunawayRules.Minutes(r.LastHint) >= RunawayRules.HintEveryMinutes)
		{
			r.LastHint = now;
			string dir = RunawayRules.Direction(r.Hide.X - r.Home.X, r.Hide.Y - r.Home.Y);
			NPC? spouse = Spouse();
			string hint = Tr.Get("runaway.direction", "好像看见{{child}}往{{dir}}边跑了。", ("child", r.Child.displayName), ("dir", dir));
			if (spouse != null)
			{
				Hints.Hud(Tr.Get("runaway.text", "{{spouse}}发来消息：{{line}}", ("spouse", Display(spouse.Name)), ("line", hint)), 9000);
			}
			else
			{
				Hints.Hud(hint, 9000);
			}
		}
	}

	/// <summary>每帧：靠近时的笑声、脚印线索。</summary>
	public void OnUpdateTicked()
	{
		if (_run is not Run r || r.Found || Game1.currentLocation is not Farm || !Context.IsPlayerFree || !Game1.game1.IsActive)
		{
			return;
		}
		if (++r.GiggleTicks % 30 != 0)
		{
			return;
		}
		float d = Vector2.Distance(Game1.player.Tile, r.StandIn.Tile);
		if (d <= RunawayRules.HearTiles && r.GiggleTicks % 360 == 0)
		{
			bool close = d <= RunawayRules.CloseTiles;
			r.StandIn.showTextAboveHead(close ? Tr.Get("runaway.giggleClose", "嘻嘻……找不到我~") : Tr.Get("runaway.giggle", "嘻嘻"), duration: 2500);
			Game1.playSound(close ? "dwop" : "shwip");
		}
		if (!r.ClueSeen)
		{
			if (r.GiggleTicks % 120 == 0)
			{
				Game1.getFarm().temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(346, 400, 8, 8), 2000f, 1, 0, r.Clue.ToVector2() * 64f + new Vector2(24f, 24f), flicker: false, flipped: false, 1f, 0.01f, Color.White, 4f, 0f, 0f, 0f));
			}
			if (Vector2.Distance(Game1.player.Tile, r.Clue.ToVector2()) <= 1.5f)
			{
				r.ClueSeen = true;
				string dir = RunawayRules.Direction(r.Hide.X - r.Clue.X, r.Hide.Y - r.Clue.Y);
				Hints.Hud(Tr.Get("runaway.clue", "地上有{{child}}的小脚印，往{{dir}}边去了。", ("child", r.Child.displayName), ("dir", dir)), 8000);
			}
		}
	}

	/// <summary>点到替身：找到了。返回 true 表示接管了这次点击。</summary>
	public bool TryFound(NPC npc, Farmer who)
	{
		if (_run is not Run r || r.Found || npc != r.StandIn)
		{
			return false;
		}
		r.Found = true;
		int points = RunawayRules.FoundPoints(r.Started, Game1.timeOfDay);
		Growth?.Note(r.Key, Clock.Minutes(Game1.timeOfDay) - Clock.Minutes(r.Started) <= 120 ? "quick" : "slow", Game1.Date.TotalDays);
		r.StandIn.Halt();
		r.StandIn.faceGeneralDirection(who.getStandingPosition(), 0, opposite: false, useTileCalculations: false);
		r.StandIn.doEmote(32);
		Game1.playSound("yoba");
		string kid = r.Child.displayName;
		_book.Note(r.Key, Game1.Date.TotalDays, Tr.Get("runaway.noteFound", "今天跑到农场上躲起来玩，被{{parent}}找到了", ("parent", who.IsMale ? "爸爸" : "妈妈")));
		ChildContext ctx = _talk.Context(r.Child);
		string fallback = Tr.Get("runaway.kidFound", "被你找到啦！嘻嘻，再玩一次好不好？");
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				if (Ai != null && _config().AiOn)
				{
					line = await new ChildDialogue(Ai, _book, _log).OpeningAsync(ctx).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				_log.Debug("孩子被找到时的话没生成：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				// 用真孩子当说话人：原版孩子没有头像，对话框就不画空的头像框（和孩子聊天时一样）。
				Game1.DrawDialogue(new StardewValley.Dialogue(r.Child, null, line ?? fallback));
				Game1.afterDialogues = () => Finish(points, found: true);
			});
		});
		return true;
	}

	private void GiveUp()
	{
		Run r = _run!;
		r.Found = true;
		NPC? spouse = Spouse();
		string kid = r.Child.displayName;
		if (spouse != null)
		{
			Hints.Hud(Tr.Get("runaway.spouseFound", "{{spouse}}把{{child}}领回家了。", ("spouse", Display(spouse.Name)), ("child", kid)), 9000);
			Say(spouse, Tr.Get("runaway.blamePrompt", "孩子{{child}}跑出去玩了一整天，{{farmer}}一直没去找，最后是你自己把孩子领回家的。你有点生气，也有点累，跟{{farmer}}说一句。", ("child", kid), ("farmer", Game1.player.Name)),
				Tr.Get("runaway.spouseBlame", "孩子是我领回来的……你今天一直在忙什么？"), worried: true);
			AdjustMood?.Invoke(spouse.Name, -10);
			Remember?.Invoke(spouse.Name, Tr.Get("runaway.memoryLost", "{{child}}跑出去玩了一天，{{farmer}}没去找，是{{spouse}}领回来的", ("child", kid), ("farmer", Game1.player.Name), ("spouse", Display(spouse.Name))));
		}
		else
		{
			Hints.Hud(Tr.Get("runaway.cameHome", "天快黑了，{{child}}自己跑回家了。", ("child", kid)), 9000);
		}
		_book.Note(r.Key, Game1.Date.TotalDays, Tr.Get("runaway.noteLost", "今天跑到农场上玩了一整天，没人来找"));
		Cleanup();
	}

	private void Finish(int points, bool found)
	{
		Run? r = _run;
		if (r == null)
		{
			return;
		}
		Game1.globalFadeToBlack(() =>
		{
			Cleanup();
			Game1.globalFadeToClear();
			NPC? spouse = Spouse();
			if (spouse == null || !found)
			{
				return;
			}
			string kid = r.Child.displayName;
			Game1.player.changeFriendship(points, spouse);
			AdjustMood?.Invoke(spouse.Name, 8);
			Remember?.Invoke(spouse.Name, Tr.Get("runaway.memoryFound", "{{child}}跑到农场上躲起来，{{farmer}}把孩子找回来了", ("child", kid), ("farmer", Game1.player.Name), ("spouse", Display(spouse.Name))));
			Say(spouse, Tr.Get("runaway.reliefPrompt", "孩子{{child}}刚才跑到农场上躲起来玩，{{farmer}}把孩子找回来了。你松了一口气，跟{{farmer}}说一句。", ("child", kid), ("farmer", Game1.player.Name)),
				Tr.Get("runaway.spouseRelief", "找到了就好……吓死我了。谢谢你。"), worried: false);
			_log.Info($"找到了 {r.Child.Name}，{spouse.Name} 好感 +{points}。");
		});
	}

	/// <summary>收回替身、放出真孩子（换天、存档、回标题时也走这里）。</summary>
	public void Cleanup()
	{
		Run? r = _run;
		_run = null;
		NpcBusy.ReleaseAll(HoldReason);
		if (r == null)
		{
			return;
		}
		try
		{
			r.StandIn.currentLocation?.characters.Remove(r.StandIn);
			Game1.getFarm().characters.Remove(r.StandIn);
			r.Child.IsInvisible = false;
		}
		catch (Exception ex)
		{
			_log.Warn("收回离家出走的替身时出错：" + ex.Message);
		}
	}

	/// <summary>万一替身被存进了存档（比如上次游戏崩了），读档时清掉。</summary>
	public static void Sweep()
	{
		foreach (GameLocation l in Game1.locations)
		{
			l.characters.RemoveWhere(c => c != null && c.modData.TryGetValue(NpcBusy.StandInKey, out string? why) && why == HoldReason);
		}
		foreach (Child c in Kids.All())
		{
			if (c.IsInvisible && c.currentLocation is FarmHouse)
			{
				c.IsInvisible = false;
			}
		}
	}
}
