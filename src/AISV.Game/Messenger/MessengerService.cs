using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Messenger;

public sealed record Contact(string NpcName, string DisplayName, int Hearts, bool Birthday, bool NoPhone);

/// <summary>
/// 信使：给村民发消息、等回复（回复到了先"正在输入"一会儿再出现）、记进对话记录、交给八卦或日记；
/// 农夫站在村民身边却发消息时，周围的人会注意到；要照片时村民拍一张随回复发来。
/// </summary>
public sealed class MessengerService
{
	private sealed class Pending
	{
		public Pending(string npc, string message, Task<ReplyResult?> reply, DialogueMaterial material, string line, ReplyFrame? frame, PhotoShot? photo, bool keepSecret, int heartsBefore)
		{
			Npc = npc;
			Message = message;
			Reply = reply;
			Material = material;
			Line = line;
			Frame = frame;
			Photo = photo;
			KeepSecret = keepSecret;
			HeartsBefore = heartsBefore;
		}

		public string Npc { get; }

		public string Message { get; }

		public Task<ReplyResult?> Reply { get; }

		public DialogueMaterial Material { get; }

		public string Line { get; }

		public ReplyFrame? Frame { get; }

		public PhotoShot? Photo { get; }

		public bool KeepSecret { get; }

		public int HeartsBefore { get; }

		public DateTime Started { get; } = DateTime.UtcNow;

		public DateTime? ShowAt { get; set; }

		public string Shown { get; set; } = "";
	}

	private readonly Func<ModConfig> _config;

	private readonly AiService _ai;

	private readonly DialogueEngine _engine;

	private readonly MaterialFactory _materials;

	private readonly Relationships _relationships;

	private readonly ConversationLog _log;

	private readonly NpcDirectory _npcs;

	private readonly ILog _diag;

	private readonly List<Pending> _outbox = new List<Pending>();

	private readonly HashSet<string> _unread = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> _arrivals = new List<string>();

	public MessengerService(Func<ModConfig> config, AiService ai, DialogueEngine engine, MaterialFactory materials, Relationships relationships, ConversationLog log, NpcDirectory npcs, PhotoStore photos, ILog diag)
	{
		_config = config;
		_ai = ai;
		_engine = engine;
		_materials = materials;
		_relationships = relationships;
		_log = log;
		_npcs = npcs;
		Photos = photos;
		_diag = diag;
	}

	public PhotoStore Photos { get; }

	public SocialSystem? Social { get; set; }

	/// <summary>农夫站在村民身边却发了消息：(村民, 设备)。</summary>
	public Action<NPC, string>? TextedBeside { get; set; }

	/// <summary>正在看哪位村民的聊天（看着的人新消息不算未读、不弹提示）。</summary>
	public Func<string?>? Viewing { get; set; }

	/// <summary>回复写进记录之前审一下提议（排不上会把回复换成婉拒）。</summary>
	public Action<NPC, NpcReaction, int>? ReviewProposal { get; set; }

	/// <summary>回复出现或失败、主动来消息时加一，界面据此刷新。</summary>
	public int Version { get; private set; }

	/// <summary>最近一次没等到回复的：(村民, 原话)。界面显示一次后清掉。</summary>
	public (string Npc, string Message)? LastFailure { get; set; }

	public int DailyLimit => MessengerRules.ClampDailyLimit(_config().TextsDaily);

	public bool IsAiAvailable => _config().AiOn && !_ai.IsDialogueUnavailable();

	public int PendingCount => _outbox.Count;

	public IReadOnlyCollection<string> Unread => _unread;

	/// <summary>最近一条没读的消息是谁发的。</summary>
	public string? LatestUnread => _arrivals.LastOrDefault(_unread.Contains);

	/// <summary>有人接手提示新消息（屏幕下方的小手机）；没有时用左下角提示。</summary>
	public Action<string, string>? ShowArrival { get; set; }

	public void MarkRead(string npc)
	{
		if (_unread.Remove(npc))
		{
			Version++;
		}
	}

	public void Reset()
	{
		foreach (Pending p in _outbox)
		{
			p.Photo?.Image.Dispose();
		}
		_outbox.Clear();
		_unread.Clear();
		LastFailure = null;
		Version++;
	}

	/// <summary>认识的村民：有手机的要启用了 AI 才列出；没手机的也列出（灰的）。有手机的在前，按心数排。</summary>
	public IReadOnlyList<Contact> Contacts()
	{
		Farmer? player = Game1.player;
		if (player == null)
		{
			return Array.Empty<Contact>();
		}
		Dictionary<string, Contact> found = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
		foreach (NPC n in Utility.getAllCharacters())
		{
			if (n == null || !n.IsVillager || string.IsNullOrWhiteSpace(n.Name) || found.ContainsKey(n.Name) || !player.friendshipData.ContainsKey(n.Name))
			{
				continue;
			}
			bool phone = MessengerRules.HasPhone(n.Name);
			if (phone && !_npcs.IsEnabled(n.Name))
			{
				continue;
			}
			found[n.Name] = new Contact(n.Name, _npcs.Names.Display(n.Name), player.getFriendshipHeartLevelForNPC(n.Name), phone && n.isBirthday(), !phone);
		}
		return found.Values.OrderBy(c => c.NoPhone).ThenByDescending(c => c.Hearts).ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public IReadOnlyList<TextEntry> History(string npc)
	{
		if (string.IsNullOrWhiteSpace(npc) || !_log.Tracked().Contains(npc, StringComparer.OrdinalIgnoreCase))
		{
			return Array.Empty<TextEntry>();
		}
		List<TextEntry> list = _log.Recent(npc, int.MaxValue).Select(TextEntry.From).ToList();
		Pending? p = PendingFor(npc);
		return p != null && p.ShowAt != null ? MessengerRules.WithoutHeld(list, PlayerName, p.Message) : list;
	}

	public int SentToday(string npc) => MessengerRules.SentToday(History(npc), PlayerName, GameState.Today);

	public int Remaining(string? npc) => string.IsNullOrWhiteSpace(npc) ? DailyLimit : MessengerRules.Remaining(DailyLimit, SentToday(npc), PendingFor(npc) != null);

	public bool IsBusy(string npc) => PendingFor(npc) != null || _engine.IsGeneratingFor(npc);

	/// <summary>回复已经到了、正在"输入"的村民。</summary>
	public bool IsTyping(string npc) => PendingFor(npc) != null;

	/// <summary>发出去、还没等到回复的那条。</summary>
	public string? PendingMessage(string npc) => PendingFor(npc)?.Message;

	public SendBlock Check(string? npc, string? text) => MessengerRules.Check(npc != null, text, Remaining(npc), npc != null && IsBusy(npc), IsAiAvailable);

	public SendBlock Send(string npcName, string text, bool keepSecret, bool onPhone = false)
	{
		SendBlock block = Check(npcName, text);
		if (block != SendBlock.None)
		{
			return block;
		}
		NPC? npc = GameState.FindNpc(npcName);
		if (npc == null || !MessengerRules.HasPhone(npcName))
		{
			return SendBlock.NoContact;
		}
		string message = MessengerRules.Normalize(text);
		(PhotoShot? photo, ReplyFrame? photoFrame) = TakePhoto(npc, message);
		ReplyFrame? frame = photoFrame ?? (Social?.IsPlayerBirthday() ?? false ? null : npc.isBirthday() ? MessengerAi.NpcBirthday(PlayerName) : MessengerRules.IsLateNight(Game1.timeOfDay) ? MessengerAi.LateNight(PlayerName, Game1.timeOfDay) : null);
		string line = MessengerRules.NpcLine(MessengerRules.LastReply(History(npcName), PlayerName), onPhone);
		if (IsBeside(npc))
		{
			line += " " + MessengerRules.BesideHint(PlayerName);
			try
			{
				TextedBeside?.Invoke(npc, onPhone ? "手机" : "电脑");
			}
			catch (Exception ex)
			{
				_diag.Info($"记录「给身边的 {npcName} 发消息」时出错：{ex.Message}");
			}
		}
		string section = "【信使】" + PlayerName + " 在" + (onPhone ? "手机" : "农场的电脑") + "上给你发消息，你们不是当面说话。你的回复就是一条手机消息：短，像平常发消息那样。" + (frame == null ? "" : "\n" + frame.Section);
		DialogueMaterial material = _materials.Build(npc, messengerSection: section);
		GameDate today = GameState.Today;
		Task<ReplyResult?> reply = Task.Run(() => _engine.ReplyAsync(material, message, today, line));
		_outbox.Add(new Pending(npcName, message, reply, material, line, frame, photo, keepSecret, _relationships.EffectiveHearts(npcName)));
		Version++;
		_diag.Debug($"信使：给 {npcName} 发了一条（{message.Length} 字{(keepSecret ? "，保密" : "")}{(photo != null ? "，有照片" : "")}）。");
		return SendBlock.None;
	}

	/// <summary>每帧（主线程）：回复到了先记下来、"正在输入"一会儿，到点再出现。</summary>
	public void OnUpdateTicked()
	{
		if (_outbox.Count == 0 || !Context.IsWorldReady)
		{
			return;
		}
		for (int i = _outbox.Count - 1; i >= 0; i--)
		{
			Pending p = _outbox[i];
			if (!p.Reply.IsCompleted)
			{
				if (DateTime.UtcNow - p.Started > MessengerRules.ReplyTimeout(AISV.Core.Ai.SlotRouter.DialogueWatchdogSeconds(_config())))
				{
					Fail(i, p, "等太久");
				}
				continue;
			}
			ReplyResult? reply = p.Reply.Status == TaskStatus.RanToCompletion ? p.Reply.Result : null;
			if (reply == null)
			{
				Fail(i, p, "没有回复");
				continue;
			}
			if (p.ShowAt == null)
			{
				Commit(p, reply);
				p.ShowAt = DateTime.UtcNow + MessengerRules.TypingDelay(p.Shown.Length);
				Version++;
			}
			if (DateTime.UtcNow < p.ShowAt)
			{
				continue;
			}
			_outbox.RemoveAt(i);
			if (p.Photo != null)
			{
				Photos.Save(p.Photo);
			}
			Arrived(p.Npc, p.Shown);
		}
	}

	private void Commit(Pending p, ReplyResult reply)
	{
		GameDate today = GameState.Today;
		if (reply.Reaction.Proposal != null && GameState.FindNpc(p.Npc) is NPC asked)
		{
			try
			{
				ReviewProposal?.Invoke(asked, reply.Reaction, p.HeartsBefore);
			}
			catch (Exception ex)
			{
				_diag.Warn($"信使：{p.Npc} 的提议没审成：{ex.Message}");
				reply.Reaction.Proposal = null;
			}
			reply = reply with { DisplayText = ReactionText.ForDisplay(reply.Reaction) };
		}
		_engine.CommitReply(p.Material, p.Message, reply, today, p.Frame?.HistoryNote ?? p.Material.ReplyHistoryNote);
		p.Shown = MessengerRules.CleanReply(reply.DisplayText);
		if (p.Shown.Length == 0)
		{
			p.Shown = MessengerRules.CleanReply(reply.Reaction.Reaction);
		}
		NPC? npc = GameState.FindNpc(p.Npc);
		if (npc != null)
		{
			try
			{
				Social?.OnTexted(npc, p.Message, p.Line, reply.Reaction, p.Shown, p.KeepSecret, p.HeartsBefore);
			}
			catch (Exception ex)
			{
				_diag.Warn($"信使：和 {p.Npc} 的这轮没能交给八卦或日记：{ex.Message}");
			}
		}
		_diag.Debug($"信使：{p.Npc} 回了（{reply.Reaction.Emotion}，{reply.Reaction.FriendshipPoints} 分）。");
	}

	private void Fail(int index, Pending p, string why)
	{
		_outbox.RemoveAt(index);
		p.Photo?.Image.Dispose();
		LastFailure = (p.Npc, p.Message);
		Version++;
		_diag.Info($"信使：{p.Npc} 没回（{why}）。");
		if (!string.Equals(Viewing?.Invoke(), p.Npc, StringComparison.OrdinalIgnoreCase))
		{
			Game1.addHUDMessage(new HUDMessage(AISV.Core.Text.Tr.Get("messenger.notDelivered", "{{name}}没收到：{{message}}", ("name", _npcs.Names.Display(p.Npc)), ("message", MessengerRules.Preview(p.Message, 30))), 3));
		}
	}

	/// <summary>一条新消息出现了：看着这个聊天时只响一声，否则记未读并在左下角提示。</summary>
	public void Arrived(string npc, string text)
	{
		Version++;
		if (string.Equals(Viewing?.Invoke(), npc, StringComparison.OrdinalIgnoreCase))
		{
			Game1.playSound("dwop");
			return;
		}
		_unread.Add(npc);
		_arrivals.RemoveAll(n => string.Equals(n, npc, StringComparison.OrdinalIgnoreCase));
		_arrivals.Add(npc);
		if (ShowArrival != null)
		{
			ShowArrival(npc, text);
			return;
		}
		Game1.addHUDMessage(new HUDMessage($"{AISV.Core.Text.Tr.Get("messenger.title", "信使")} · {_npcs.Names.Display(npc)}：{MessengerRules.Preview(text, 40)}", 2) { noIcon = true });
	}

	/// <summary>村民主动发来的一条，记进对话记录（农夫的话为空）。</summary>
	public bool RecordTextedFirst(string npc, string text, TextKind kind)
	{
		if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		GameDate today = GameState.Today;
		_log.Add(npc, new ConversationEntry
		{
			Day = today.Day,
			Season = today.Season,
			Year = today.Year,
			Setting = MessengerRules.ContextType,
			SettingRef = MessengerRules.ContextId,
			SettingName = "信使",
			Opener = VillagerTexts.TextedFirstLine(kind, Game1.timeOfDay),
			FarmerReply = "",
			Comeback = text,
			Face = "neutral",
			Farmer = PlayerName
		});
		return true;
	}

	private (PhotoShot? Photo, ReplyFrame? Frame) TakePhoto(NPC npc, string message)
	{
		try
		{
			GameLocation? place = npc.currentLocation;
			if (place == null)
			{
				return (null, null);
			}
			List<(string, string)> candidates = Utility.getAllCharacters().Where(n => n != null && n.IsVillager && !string.IsNullOrWhiteSpace(n.Name)).Select(n => (n.Name, _npcs.Names.Display(n.Name))).ToList();
			PhotoRequest request = PhotoRules.Plan(message, npc.Name, candidates, name => GameState.FindNpc(name)?.currentLocation == place, name => Photos.SentToday(npc.Name, name));
			if (request.Kind == PhotoKind.None)
			{
				return (null, null);
			}
			NPC? subject = request.Kind == PhotoKind.Selfie ? npc : GameState.FindNpc(request.Subject);
			string subjectName = subject != null ? _npcs.Names.Display(subject.Name) : request.Subject;
			string placeName = place.DisplayName ?? place.Name;
			ReplyFrame? frame = PhotoRules.Frame(request, subjectName, placeName, PlayerName);
			if (request.Kind == PhotoKind.Absent)
			{
				return (null, frame);
			}
			Texture2D? image = subject == null ? null : PhotoCamera.Capture(subject, _diag, request.Kind == PhotoKind.Selfie);
			if (image == null)
			{
				return (null, null);
			}
			PhotoRecord record = new PhotoRecord(Guid.NewGuid().ToString("N"), npc.Name, subject!.Name, place.Name, placeName, Game1.dayOfMonth, Game1.currentSeason, Game1.year, Game1.timeOfDay, message);
			return (new PhotoShot(image, record), frame);
		}
		catch (Exception ex)
		{
			_diag.Info($"信使没能给 {npc.Name} 拍照：{ex.Message}");
			return (null, null);
		}
	}

	private static bool IsBeside(NPC npc)
	{
		Farmer? player = Game1.player;
		bool same = player?.currentLocation != null && player.currentLocation == npc.currentLocation;
		return MessengerRules.IsBeside(same, same ? Vector2.Distance(player!.Tile, npc.Tile) : float.MaxValue);
	}

	private Pending? PendingFor(string npc) => _outbox.FirstOrDefault(p => string.Equals(p.Npc, npc, StringComparison.OrdinalIgnoreCase));

	private static string PlayerName => Game1.player?.Name ?? "";
}
