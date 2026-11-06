using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Core.Persona;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Messenger;

/// <summary>
/// 村民主动发消息：每周几条（按设置），每天早上把这周剩下的摊到今天、排好时间；到点挑一位发。
/// 农夫生日那天上午按安排发祝福；看到农夫做的事却走不过去时，改发一条消息。
/// </summary>
public sealed class VillagerTextService
{
	private sealed record InFlight(string Npc, TextKind Kind, Task<string?> Text, DateTime Started, PhotoShot? Photo = null, Action? OnSent = null);

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly MessengerService _messenger;

	private readonly MessengerAi _ai;

	private readonly NpcDirectory _npcs;

	private readonly Relationships _relationships;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private InFlight? _inFlight;

	public VillagerTextService(Func<ModConfig> config, Func<DocumentStore?> store, MessengerService messenger, MessengerAi ai, NpcDirectory npcs, Relationships relationships, ILog log)
	{
		_config = config;
		_store = store;
		_messenger = messenger;
		_ai = ai;
		_npcs = npcs;
		_relationships = relationships;
		_log = log;
	}

	public SocialSystem? Social { get; set; }

	private VillagerTextState? State => _store()?.Get(VillagerTexts.Path, VillagerTexts.Doc);

	private int PerWeek => VillagerTexts.ClampPerWeek(_config().VillagerTextsWeekly);

	private static int Today => GameState.Today.TotalDays;

	public void Reset() => _inFlight = null;

	public void OnDayStarted()
	{
		VillagerTextState? s = State;
		if (s == null)
		{
			return;
		}
		int today = Today;
		int week = VillagerTexts.WeekOf(today);
		if (s.Week != week)
		{
			s.Week = week;
			s.SentCount = 0;
		}
		if (s.PlannedOn != today)
		{
			int count = VillagerTexts.PlanToday(PerWeek, s.SentCount, VillagerTexts.DaysLeftInWeek(today), _random.NextDouble());
			s.PlannedOn = today;
			s.Slots = VillagerTexts.PlanTimes(count, Enumerable.Range(0, count).Select(_ => _random.NextDouble()).ToList()).ToList();
			if (count > 0)
			{
				_log.Info($"信使：今天村民会主动发 {count} 条，时间 {string.Join(", ", s.Slots)}。");
			}
		}
		_store()?.MarkDirty(VillagerTexts.Path);
	}

	/// <summary>每 30 帧一次（主线程）。</summary>
	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || State == null)
		{
			return;
		}
		try
		{
			TakeBirthdayPlan();
			SendBirthdayTexts();
			Collect();
			TryStart();
		}
		catch (Exception ex)
		{
			_inFlight = null;
			_log.Warn("信使：村民主动发消息时出错：" + ex);
		}
	}

	private void TakeBirthdayPlan()
	{
		VillagerTextState s = State!;
		if (s.BirthdayPlannedOn == Today || Social?.TakeBirthdayTexts() is not { } plan)
		{
			return;
		}
		s.BirthdayPlannedOn = Today;
		s.BirthdaySlots = plan.OrderBy(p => p.Time).Select(p => new PlannedBirthdayText { NpcName = p.Npc, Text = p.Text, Time = p.Time }).ToList();
		_store()?.MarkDirty(VillagerTexts.Path);
		_log.Info($"信使：今天有 {plan.Count} 条生日祝福要发。");
	}

	private void SendBirthdayTexts()
	{
		VillagerTextState s = State!;
		if (s.BirthdaySlots.Count == 0)
		{
			return;
		}
		if (s.BirthdayPlannedOn != Today)
		{
			s.BirthdaySlots.Clear();
			_store()?.MarkDirty(VillagerTexts.Path);
			return;
		}
		PlannedBirthdayText next = s.BirthdaySlots[0];
		if (Game1.timeOfDay < next.Time || Blocked)
		{
			return;
		}
		s.BirthdaySlots.RemoveAt(0);
		_store()?.MarkDirty(VillagerTexts.Path);
		string text = VillagerTexts.CleanText(next.Text, next.NpcName, _npcs.Names.Display(next.NpcName));
		if (text.Length == 0 || _messenger.IsBusy(next.NpcName) || !_messenger.RecordTextedFirst(next.NpcName, text, TextKind.Birthday))
		{
			_log.Info($"信使：{next.NpcName} 的生日祝福没发出去。");
			return;
		}
		s.LatestOpener[next.NpcName] = Today;
		Social?.RecordBirthdayTexted(next.NpcName);
		_log.Info($"信使：{next.NpcName} 发来了生日祝福。");
		_messenger.Arrived(next.NpcName, text);
	}

	private static bool Blocked => Game1.eventUp || Game1.isFestival() || Game1.farmEvent != null || Game1.player == null;

	private void TryStart()
	{
		VillagerTextState s = State!;
		if (_inFlight != null || s.Slots.Count == 0 || Game1.timeOfDay < s.Slots[0])
		{
			return;
		}
		if (PerWeek <= 0 || s.SentCount >= PerWeek)
		{
			s.Slots.Clear();
			_store()?.MarkDirty(VillagerTexts.Path);
			return;
		}
		if (Blocked || !_messenger.IsAiAvailable)
		{
			return;
		}
		s.Slots.RemoveAt(0);
		_store()?.MarkDirty(VillagerTexts.Path);
		int today = Today;
		List<TextCandidate> candidates = _messenger.Contacts().Where(c => !c.NoPhone)
			.Select(c => new TextCandidate(c.NpcName, c.Hearts, s.LatestOpener.TryGetValue(c.NpcName, out int last) ? last : null, IsNearFarmer(c.NpcName), _messenger.IsBusy(c.NpcName)))
			.ToList();
		TextCandidate? sender = VillagerTexts.PickSender(candidates, today, _random.NextDouble());
		if (sender != null && Build(sender) is { } built)
		{
			Start(sender.NpcName, built.Request, built.Photo, null);
		}
	}

	/// <summary>看到农夫做的事、走不过去时改发消息。返回开始发了没有。</summary>
	public bool TryRequestText(string npcName, string situation, Action? onSent)
	{
		try
		{
			VillagerTextState? s = State;
			if (s == null || !Context.IsWorldReady || string.IsNullOrWhiteSpace(npcName) || _inFlight != null || !MessengerRules.HasPhone(npcName))
			{
				return false;
			}
			if (PerWeek <= 0 || s.SentCount >= PerWeek || Blocked || !_messenger.IsAiAvailable || _messenger.IsBusy(npcName))
			{
				return false;
			}
			TextCandidate who = new TextCandidate(npcName, _relationships.EffectiveHearts(npcName), null, false, false);
			if (Build(who, TextKind.SeenAndHeard, situation) is not { } built)
			{
				return false;
			}
			_log.Info($"信使：{npcName} 为看到的事给农夫发消息。");
			Start(npcName, built.Request, null, onSent);
			return true;
		}
		catch (Exception ex)
		{
			_log.Warn($"信使：{npcName} 为看到的事发消息没能开始：{ex.Message}");
			return false;
		}
	}

	public string DebugStart(string npcName, TextKind? kind)
	{
		if (_inFlight != null)
		{
			return $"{_inFlight.Npc} 的消息还在写。";
		}
		NPC? npc = GameState.FindNpc(npcName);
		if (npc == null)
		{
			return "找不到村民 " + npcName;
		}
		TextCandidate who = new TextCandidate(npc.Name, _relationships.EffectiveHearts(npc.Name), null, false, false);
		if (Build(who, kind) is not { } built)
		{
			return "准备不出这条消息。";
		}
		Start(npc.Name, built.Request, built.Photo, null);
		return $"{npc.Name} 开始写一条 {VillagerTexts.KindKey(built.Request.Kind)}。";
	}

	public string Describe()
	{
		VillagerTextState? s = State;
		if (s == null)
		{
			return "没有存档数据。";
		}
		return $"本周已主动发 {s.SentCount}/{PerWeek} 条；今天还排着 {string.Join(", ", s.Slots)}；生日祝福还剩 {s.BirthdaySlots.Count} 条；{(_inFlight == null ? "空闲" : _inFlight.Npc + " 正在写")}。";
	}

	private void Start(string npc, TextRequest request, PhotoShot? photo, Action? onSent)
	{
		_inFlight = new InFlight(npc, request.Kind, Task.Run(() => _ai.VillagerTextAsync(request)), DateTime.UtcNow, photo, onSent);
		_log.Info($"信使：{npc} 主动给农夫发消息（{VillagerTexts.KindKey(request.Kind)}）。");
	}

	private (TextRequest Request, PhotoShot? Photo)? Build(TextCandidate sender, TextKind? forced = null, string? situationNote = null)
	{
		Farmer player = Game1.player;
		NPC? npc = GameState.FindNpc(sender.NpcName);
		if (player == null || npc == null)
		{
			return null;
		}
		IReadOnlyList<TextEntry> history = _messenger.History(sender.NpcName);
		player.friendshipData.TryGetValue(sender.NpcName, out Friendship? f);
		bool married = f != null && f.IsMarried() && !f.IsRoommate();
		bool roommate = f?.IsRoommate() ?? false;
		bool engaged = f?.IsEngaged() ?? false;
		bool dating = f?.IsDating() ?? false;
		bool marriedElsewhere = !string.IsNullOrWhiteSpace(player.spouse) && !string.Equals(player.spouse, sender.NpcName, StringComparison.OrdinalIgnoreCase);
		bool canFlirt = VillagerTexts.CanFlirt(married || engaged || dating, npc.datable.Value, sender.Hearts, marriedElsewhere);
		PhotoShot? photo = null;
		string topic = "";
		TextKind kind = forced ?? TextKind.SmallTalk;
		if (kind != TextKind.SeenAndHeard && kind != TextKind.Birthday)
		{
			GossipEntry? gossip = PickGossip(sender.NpcName);
			GameDate today = GameState.Today;
			TextEntry? callback = VillagerTexts.PickCallback(history, player.Name, today.Day, today.Season, today.Year);
			kind = forced ?? VillagerTexts.PickKind(VillagerTexts.Kinds(gossip != null, canFlirt, callback != null, npc.currentLocation != null), _random.NextDouble());
			if (kind == TextKind.Selfie)
			{
				photo = TakeSelfie(npc);
			}
			kind = kind switch
			{
				TextKind.Selfie when photo == null => TextKind.SmallTalk,
				TextKind.Gossip when gossip == null => TextKind.SmallTalk,
				TextKind.Callback when callback == null => TextKind.SmallTalk,
				_ => kind
			};
			topic = kind switch
			{
				TextKind.Gossip => "最近传到你耳朵里的事：" + gossip!.Fact,
				TextKind.Callback => VillagerTexts.DescribeCallback(callback!),
				TextKind.Selfie => $"这张自拍拍的是你自己，地点：{photo!.Record.LocationDisplay}。",
				_ => ""
			};
		}
		string situation = Situation(npc) + (string.IsNullOrWhiteSpace(situationNote) ? "" : "\n" + situationNote.Trim());
		string feelings = "";
		try
		{
			feelings = Social?.Feelings.PromptBlock(sender.NpcName, _npcs.Names.Display(sender.NpcName), sender.Hearts, Today) ?? "";
		}
		catch (Exception ex)
		{
			_log.Info($"信使读不出 {sender.NpcName} 的感受：{ex.Message}");
		}
		NpcPersona persona = _npcs.Persona(sender.NpcName, npc);
		TextRequest request = new TextRequest(kind, sender.NpcName, persona.Personality, player.Name,
			VillagerTexts.Relationship(sender.Hearts, married, roommate, engaged, dating), situation, VillagerTexts.FormatChat(history, player.Name), topic, feelings, persona.Source == PersonaSource.Card);
		return (request, photo);
	}

	private PhotoShot? TakeSelfie(NPC npc)
	{
		GameLocation? place = npc.currentLocation;
		Microsoft.Xna.Framework.Graphics.Texture2D? image = place == null ? null : PhotoCamera.Capture(npc, _log, selfie: true);
		if (image == null)
		{
			return null;
		}
		string placeName = place!.DisplayName ?? place.Name;
		return new PhotoShot(image, new PhotoRecord(Guid.NewGuid().ToString("N"), npc.Name, npc.Name, place.Name, placeName, Game1.dayOfMonth, Game1.currentSeason, Game1.year, Game1.timeOfDay, ""));
	}

	private GossipEntry? PickGossip(string npc)
	{
		try
		{
			List<GossipEntry> known = Social?.Gossip.RecentKnownBy(npc, 3).Where(e => !string.IsNullOrWhiteSpace(e.Fact)).ToList() ?? new List<GossipEntry>();
			return known.Count == 0 ? null : known[_random.Next(known.Count)];
		}
		catch (Exception ex)
		{
			_log.Info($"信使读不出 {npc} 知道的八卦：{ex.Message}");
			return null;
		}
	}

	private static string Situation(NPC npc)
	{
		string weather = Game1.isLightning ? "雷雨" : Game1.isRaining ? "下雨" : Game1.isSnowing ? "下雪" : "晴天";
		string place = npc.currentLocation == null ? "" : $"你在{npc.currentLocation.DisplayName ?? npc.currentLocation.Name}。";
		return $"第 {Game1.year} 年{HistoryFormatter.SeasonName(Game1.currentSeason)} {Game1.dayOfMonth} 日，{Game1.getTimeOfDayString(Game1.timeOfDay)}，{weather}。{place}";
	}

	private static bool IsNearFarmer(string npcName)
	{
		Farmer? player = Game1.player;
		NPC? npc = GameState.FindNpc(npcName);
		return player?.currentLocation != null && npc?.currentLocation == player.currentLocation && Vector2.Distance(player.Tile, npc.Tile) <= VillagerTexts.NearbyTiles;
	}

	private void Collect()
	{
		InFlight? f = _inFlight;
		if (f == null)
		{
			return;
		}
		VillagerTextState s = State!;
		if (!f.Text.IsCompleted)
		{
			if (DateTime.UtcNow - f.Started > VillagerTexts.Timeout)
			{
				_inFlight = null;
				f.Photo?.Image.Dispose();
				_log.Info($"信使：{f.Npc} 的消息太久没写好，不发了。");
			}
			return;
		}
		_inFlight = null;
		string text = VillagerTexts.CleanText(f.Text.Status == TaskStatus.RanToCompletion ? f.Text.Result : null, f.Npc, _npcs.Names.Display(f.Npc));
		if (text.Length == 0 || !_messenger.RecordTextedFirst(f.Npc, text, f.Kind))
		{
			_log.Info($"信使：{f.Npc} 的消息是空的，没发。");
			f.Photo?.Image.Dispose();
			return;
		}
		if (f.Photo != null)
		{
			_messenger.Photos.Save(f.Photo);
		}
		s.SentCount++;
		s.LatestOpener[f.Npc] = Today;
		_store()?.MarkDirty(VillagerTexts.Path);
		_messenger.Arrived(f.Npc, text);
		try
		{
			f.OnSent?.Invoke();
		}
		catch (Exception ex)
		{
			_log.Warn($"信使：{f.Npc} 发完消息之后的处理出错：{ex.Message}");
		}
	}
}
