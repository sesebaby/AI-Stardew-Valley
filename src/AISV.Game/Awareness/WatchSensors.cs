using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AISV.Core.Awareness;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Text;
using AISV.Game.Approach;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace AISV.Game.Awareness;

/// <summary>靠定时查看才知道的事：盯着人看、挡路、跟踪、当众亲吻、游泳、受伤、抛竿。</summary>
internal sealed class WatchSensors
{
	private const int SlowCheckTicks = 30;

	private const int FastCheckTicks = 10;

	private const int StareTicks = 180;

	private const long StareCooldown = 3600;

	private const float StareRange = 2.2f;

	private const int BlockedMilliseconds = 2500;

	private const long BlockCooldown = 3600;

	private const float BlockRange = 2f;

	private const int FollowCooldown = 3600;

	private const float NoticeRange = 8f;

	private const int HurtRetryTicks = 3600;

	private const int HurtAttempts = 3;

	private const int FishingFirstCheck = 24;

	private const int FishingCheckEvery = 30;

	private readonly AwarenessSystem _system;

	private readonly Func<string, bool> _isNpcEnabled;

	private readonly Func<bool> _isDayTransition;

	private readonly ILog _log;

	private readonly FollowTracker _follow = new FollowTracker();

	private readonly Dictionary<string, int> _lastConfront = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private string? _staring;

	private int _stareTicks;

	private string? _following;

	private bool _kissedBefore;

	private bool _kissDone;

	private int _nextHurtTick;

	private int _hurtAttempts;

	private bool _helpedToday;

	private bool _commentedToday;

	private int _castTicks;

	private int _nextCastCheck;

	private int _castSession;

	private bool _casting;

	private string? _walker;

	private int _walkTicks;

	private readonly HashSet<string> _walkedTonight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private const int WalkTicks = 480;

	private const float WalkRange = 2.5f;

	private const float WalkTiles = 4f;

	private Vector2 _walkFrom;

	private Vector2 _walkNpcFrom;

	public WatchSensors(AwarenessSystem system, Func<string, bool> isNpcEnabled, Func<bool> isDayTransition, ILog log)
	{
		_system = system;
		_isNpcEnabled = isNpcEnabled;
		_isDayTransition = isDayTransition;
		_log = log;
		_system.ReactionFinished += OnReactionFinished;
	}

	/// <summary>正被跟着、已经起疑的村民（头上冒感叹号）。</summary>
	public string? AttentionNpc => _follow.HasWarned ? _following : null;

	public void Reset()
	{
		StopStaring();
		ResetFollow();
		_lastConfront.Clear();
		_kissedBefore = false;
		_kissDone = false;
		_nextHurtTick = 0;
		_hurtAttempts = 0;
		_helpedToday = false;
		_commentedToday = false;
		ResetCast();
		_castSession = 0;
		_walker = null;
		_walkTicks = 0;
		_walkedTonight.Clear();
	}

	public void OnUpdateTicked()
	{
		if (!_system.Enabled || !Context.IsWorldReady || !Context.IsMainPlayer || _isDayTransition())
		{
			StopStaring();
			ResetFollow();
			ResetCast();
			return;
		}
		try
		{
			int tick = Game1.ticks;
			Fishing();
			Following(tick);
			if (tick % FastCheckTicks == 0)
			{
				Staring(tick);
				Blocking(tick);
			}
			if (tick % SlowCheckTicks == 0)
			{
				Kissing();
				Swimming(tick);
				Hurting(tick);
				WalkingTogether();
			}
		}
		catch (Exception ex)
		{
			_log.Warn("看到听到（查看）出错：" + ex.Message);
		}
	}

	private static bool InEvent => Game1.eventUp || Game1.currentLocation?.currentEvent != null;

	private static bool Unhindered => Context.IsPlayerFree && !InEvent && !Game1.dialogueUp && Game1.activeClickableMenu == null;

	private bool Available(NPC? npc) => ApproachCoordinator.IsVillager(npc) && !npc!.isSleeping.Value && _isNpcEnabled(npc.Name);

	private bool Free(NPC? npc) => Available(npc) && !_system.IsNpcOccupied(npc!.Name);

	private void Staring(int tick)
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (!Unhindered || player == null || here == null || player.isMoving() || player.UsingTool || player.isRidingHorse())
		{
			StopStaring();
			return;
		}
		NPC? npc = here.characters.Where(Free)
			.Where(n => Sightlines.IsFacing(player.Tile.X, player.Tile.Y, player.FacingDirection, n.Tile.X, n.Tile.Y, StareRange))
			.OrderBy(n => Vector2.DistanceSquared(n.Tile, player.Tile)).FirstOrDefault();
		if (npc == null)
		{
			StopStaring();
			return;
		}
		if (!Same(_staring, npc.Name))
		{
			_staring = npc.Name;
			_stareTicks = 0;
		}
		_stareTicks += FastCheckTicks;
		if (_stareTicks < StareTicks)
		{
			return;
		}
		_stareTicks = 0;
		if (_system.Pass("stare:" + npc.Name, tick, StareCooldown))
		{
			string farmer = ActionPatches.Name(player);
			bool facing = Sightlines.IsFacing(npc.Tile.X, npc.Tile.Y, npc.FacingDirection, player.Tile.X, player.Tile.Y, StareRange);
			_system.Record(new Observation("stare", farmer, npc.Name, here.NameOrUniqueName, $"{farmer}站在那儿盯着{npc.Name}看了好几秒。", player.Tile,
				Observation.Map(("seconds", "3"), ("npcFacing", facing ? "true" : "false"))));
		}
	}

	private void StopStaring()
	{
		_staring = null;
		_stareTicks = 0;
	}

	private void Blocking(int tick)
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (!Unhindered || player == null || here == null || player.isMoving() || player.isRidingHorse())
		{
			return;
		}
		foreach (NPC npc in here.characters.Where(Free))
		{
			if (npc.blockedInterval >= BlockedMilliseconds && Sightlines.IsFacing(npc.Tile.X, npc.Tile.Y, npc.FacingDirection, player.Tile.X, player.Tile.Y, BlockRange) && _system.Pass("block:" + npc.Name, tick, BlockCooldown))
			{
				string farmer = ActionPatches.Name(player);
				_system.Record(new Observation("block_path", farmer, npc.Name, here.NameOrUniqueName, $"{farmer}挡在{npc.Name}的路上不肯让开。", player.Tile,
					Observation.Map(("seconds", Math.Max(1, npc.blockedInterval / 1000).ToString(CultureInfo.InvariantCulture)), ("indoors", here.IsOutdoors ? "false" : "true"))));
				return;
			}
		}
	}

	private void Following(int tick)
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (InEvent || Game1.activeClickableMenu != null || Game1.dialogueUp || player == null || here == null)
		{
			ResetFollow();
			return;
		}
		if (!player.isMoving() && !_follow.IsTracking)
		{
			return;
		}
		List<NPC> behind = here.characters
			.Where(n => n != null && Sightlines.IsBehind(n.Tile.X, n.Tile.Y, n.FacingDirection, player.Tile.X, player.Tile.Y))
			.Where(n => CanBeFollowed(n, tick))
			.OrderBy(n => Same(n.Name, _following) ? 0 : 1).ThenBy(n => Vector2.DistanceSquared(n.Tile, player.Tile))
			.ToList();
		NPC? npc = string.IsNullOrWhiteSpace(_following) ? behind.FirstOrDefault() : behind.FirstOrDefault(n => Same(n.Name, _following));
		if (npc == null)
		{
			if (!string.IsNullOrWhiteSpace(_following))
			{
				_follow.Observe(_following, false);
				if (!_follow.IsTracking)
				{
					_following = null;
				}
			}
			return;
		}
		_following = npc.Name;
		if (_follow.Observe(npc.Name, npc.isMoving() && player.isMoving()) != FollowSignal.Confront)
		{
			return;
		}
		_lastConfront[npc.Name] = tick;
		npc.Halt();
		npc.jumpWithoutSound(3f);
		npc.faceTowardFarmerForPeriod(900, 3, false, player);
		string farmer = ActionPatches.Name(player);
		_system.Record(new Observation("follow_npc", farmer, npc.Name, here.NameOrUniqueName, $"{farmer}贴在{npc.Name}身后跟了整整六秒。", player.Tile,
			Observation.Map(("durationSeconds", "6"), ("sh.explicitReaction", "true"), ("preparationTimeoutSeconds", "10"),
				("fallbackDialogue", Tr.Get("awareness.follow.fallback", "喂！你跟着我干什么？别再这样了，离我远点！")),
				("reactionDirective", "当场转身把话撂下：不许再跟在你后面。口气要冲、要明显不高兴，按你的脾气甩一句短短的呛人的话；用不着讲礼貌，更不用说对不起。"))));
		ResetFollow();
	}

	private bool CanBeFollowed(NPC npc, int tick) =>
		Free(npc) && !npc.ignoreScheduleToday && (!_lastConfront.TryGetValue(npc.Name, out int last) || tick - last >= FollowCooldown);

	private void ResetFollow()
	{
		_follow.Reset();
		_following = null;
	}

	private void Kissing()
	{
		if (_kissDone)
		{
			return;
		}
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		string? spouse = player?.spouse;
		if (player == null || here == null || string.IsNullOrWhiteSpace(spouse) || (player.friendshipData.TryGetValue(spouse, out Friendship? f) && f.IsRoommate()))
		{
			return;
		}
		NPC? npc = here.characters.FirstOrDefault(n => Same(n.Name, spouse));
		bool kissed = npc?.hasBeenKissedToday.Value ?? false;
		bool before = _kissedBefore;
		_kissedBefore = kissed;
		if (npc == null || !kissed || before)
		{
			return;
		}
		_kissDone = true;
		if (Game1.eventUp || here is FarmHouse || here.IsFarm || !_isNpcEnabled(npc.Name))
		{
			return;
		}
		string farmer = ActionPatches.Name(player);
		_system.Record(new Observation("kiss", farmer, npc.Name, here.NameOrUniqueName, $"{farmer}当众亲了{npc.Name}。", npc.Tile,
			Observation.Map(("spouse", npc.Name), ("location", here.NameOrUniqueName), ("indoors", here.IsOutdoors ? "false" : "true")), Exclude: new[] { npc.Name }));
	}

	/// <summary>晚上八点以后，有村民一直贴着你走在外面（比如陪你走回家）：八秒里一起走出四格以上，旁人看见就是一件事。一晚上同一个人只算一次。</summary>
	private void WalkingTogether()
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (Game1.timeOfDay < 2000 || !Unhindered || player == null || here == null || !here.IsOutdoors || here.IsFarm)
		{
			_walker = null;
			return;
		}
		NPC? npc = here.characters.Where(Available).Where(n => Vector2.Distance(n.Tile, player.Tile) <= WalkRange && !_walkedTonight.Contains(n.Name))
			.OrderBy(n => Vector2.DistanceSquared(n.Tile, player.Tile)).FirstOrDefault();
		if (npc == null)
		{
			_walker = null;
			return;
		}
		if (!Same(_walker, npc.Name))
		{
			_walker = npc.Name;
			_walkTicks = 0;
			_walkFrom = player.Tile;
			_walkNpcFrom = npc.Tile;
		}
		_walkTicks += SlowCheckTicks;
		if (_walkTicks < WalkTicks)
		{
			return;
		}
		bool moved = Vector2.Distance(_walkFrom, player.Tile) >= WalkTiles && Vector2.Distance(_walkNpcFrom, npc.Tile) >= WalkTiles - 1;
		_walker = null;
		if (!moved)
		{
			return;
		}
		_walkedTonight.Add(npc.Name);
		string farmer = ActionPatches.Name(player);
		bool partner = Same(player.spouse, npc.Name) || player.friendshipData.TryGetValue(npc.Name, out Friendship? fs) && fs.IsDating();
		List<string> report = new List<string>();
		_system.Record(new Observation("night_walk", farmer, npc.Name, here.NameOrUniqueName, $"{farmer}和{npc.Name}深夜一起走在外面，挨得很近。", player.Tile,
			Observation.Map(("location", here.NameOrUniqueName), ("partner", partner ? "true" : "false"), ("time", Game1.timeOfDay.ToString())), Exclude: new[] { npc.Name }), report);
		_log.Info($"看到听到：深夜和 {npc.Name} 一起走，{string.Join("；", report)}");
	}

	private void Swimming(int tick)
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (InEvent || player == null || here == null || !player.swimming.Value)
		{
			return;
		}
		List<NPC> nearby = here.characters.Where(Available).Where(n => Vector2.Distance(n.Tile, player.Tile) <= NoticeRange).ToList();
		if (nearby.Count == 0 || !_system.Pass("swim:" + here.NameOrUniqueName, tick, long.MaxValue))
		{
			return;
		}
		string farmer = ActionPatches.Name(player);
		List<string> swimmers = nearby.Where(n => n.swimming.Value).Select(n => n.Name).ToList();
		string summary = swimmers.Count > 0 ? $"{farmer}和{SightingText.JoinAnd(swimmers)}一起游泳。" : $"{farmer}当着大家的面下水游泳。";
		_system.Record(new Observation("swim", farmer, null, here.NameOrUniqueName, summary, player.Tile,
			Observation.Map(("location", here.NameOrUniqueName), ("withNpcs", string.Join(",", swimmers)), ("count", nearby.Count.ToString(CultureInfo.InvariantCulture)))));
	}

	private void Hurting(int tick)
	{
		Farmer player = Game1.player;
		GameLocation here = Game1.currentLocation;
		if (tick < _nextHurtTick || _hurtAttempts >= HurtAttempts || _helpedToday || !Context.IsPlayerFree || InEvent || player == null || here == null)
		{
			return;
		}
		HurtCondition condition = HurtPolicy.ConditionOf(player.health, player.maxHealth, player.Stamina, player.MaxStamina);
		if (condition == HurtCondition.None)
		{
			return;
		}
		var pick = here.characters
			.Where(n => n != null && Vector2.Distance(n.Tile, player.Tile) <= NoticeRange && ApproachCoordinator.IsVillager(n) && _isNpcEnabled(n.Name) && !_system.IsNpcOccupied(n.Name))
			.Select(n => (Npc: n, Hearts: player.getFriendshipHeartLevelForNPC(n.Name)))
			.Select(x => (x.Npc, x.Hearts, Response: HurtPolicy.ResponseOf(x.Hearts, _helpedToday, _commentedToday)))
			.Where(x => x.Response != HurtResponse.None)
			.OrderByDescending(x => x.Response).ThenByDescending(x => x.Hearts).ThenBy(x => Vector2.DistanceSquared(x.Npc.Tile, player.Tile))
			.FirstOrDefault();
		if (pick.Npc == null)
		{
			return;
		}
		_hurtAttempts++;
		_nextHurtTick = tick + HurtRetryTicks;
		string farmer = ActionPatches.Name(player);
		string state = condition == HurtCondition.Hurt ? "伤得很重" : "累得快站不住了";
		Dictionary<string, string> d = Observation.Map(("condition", condition == HurtCondition.Hurt ? "hurt" : "exhausted"), ("hurtReaction", "comment"));
		SObject? food = pick.Response == HurtResponse.Help ? CreateFood(pick.Npc.Name) : null;
		if (food != null)
		{
			d["hurtReaction"] = "help";
			d["rewardItem"] = food.QualifiedItemId;
			d["reactionDirective"] = $"{farmer}一副{state}的样子，你看着放心不下，于是把{food.DisplayName}塞给对方垫垫肚子、缓口气。话里要用你自己的口气讲明你在给这个。";
		}
		else
		{
			d["reactionDirective"] = $"{farmer}看起来{state}，被你看在眼里。照你们的交情关心一句，别过头也别太冷淡。注意：你手上什么都没给对方。";
		}
		_system.Record(new Observation("hurt", farmer, pick.Npc.Name, here.NameOrUniqueName, $"{farmer}从旁边走过，看上去{state}。", player.Tile, d));
	}

	private static SObject? CreateFood(string npc)
	{
		foreach (string id in new[] { HurtPolicy.ItemFor(npc), HurtPolicy.DefaultItem })
		{
			if (ItemRegistry.GetData(id) != null && ItemRegistry.Create(id) is SObject { Edibility: > 0 } food)
			{
				return food;
			}
		}
		return null;
	}

	private void OnReactionFinished(Sighting e, bool spoken)
	{
		if (!spoken || e.Type != "hurt")
		{
			return;
		}
		if (e.Detail("hurtReaction") == "help")
		{
			_helpedToday = true;
		}
		else
		{
			_commentedToday = true;
		}
	}

	private void Fishing()
	{
		Farmer player = Game1.player;
		if (InEvent || player?.CurrentTool is not FishingRod || !player.UsingTool)
		{
			ResetCast();
			return;
		}
		if (!_casting)
		{
			_casting = true;
			_castSession++;
			_castTicks = 0;
			_nextCastCheck = FishingFirstCheck;
		}
		if (++_castTicks < _nextCastCheck)
		{
			return;
		}
		_nextCastCheck = _castTicks + FishingCheckEvery;
		string farmer = ActionPatches.Name(player);
		_system.Record(new Observation("fishing", farmer, null, Game1.currentLocation?.NameOrUniqueName ?? "", $"{farmer}在附近开始钓鱼。", player.Tile,
			Observation.Map(("activity", "fishing"), ("fishingSession", _castSession.ToString(CultureInfo.InvariantCulture)))));
	}

	private void ResetCast()
	{
		_casting = false;
		_castTicks = 0;
		_nextCastCheck = 0;
	}

	/// <summary>村民收下礼物。</summary>
	public void OnGift(NPC npc, SObject item)
	{
		string farmer = ActionPatches.Name(Game1.player);
		string name = ActionPatches.ItemName(item);
		_system.Record(new Observation("gift", farmer, npc.Name, npc.currentLocation?.NameOrUniqueName ?? "", $"{farmer}把{name}送给了{npc.Name}。", null,
			Observation.Map(("item", name), ("itemId", item.QualifiedItemId ?? ""), ("taste", Taste(npc, item)))));
	}

	private static string Taste(NPC npc, Item item)
	{
		try
		{
			return npc.getGiftTasteForThisItem(item) switch
			{
				0 or 7 => "loved",
				2 => "liked",
				4 => "disliked",
				6 => "hated",
				_ => "neutral"
			};
		}
		catch (Exception)
		{
			return "neutral";
		}
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
