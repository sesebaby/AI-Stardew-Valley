using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Group;

/// <summary>
/// 居民群：你发一条，几位候选村民里 AI 挑 1～3 位回，按打字速度一条条冒出来；村民每天也会按计划自己聊几次。
/// 记录存在 town/group_chat.json，回话的村民各自记一笔对话记忆。
/// </summary>
public sealed class ResidentsService
{
	private sealed record InFlight(string Post, Task<GroupTurn?> Turn, DateTime Started)
	{
		public bool ByVillagers => Post.Length == 0;
	}

	private sealed record Scheduled(GroupLine Line, DateTime ShowAt);

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly AiService _ai;

	private readonly GroupChatAi _chat;

	private readonly GroupMaterial _material;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private readonly Queue<Scheduled> _scheduled = new Queue<Scheduled>();

	private InFlight? _inFlight;

	public ResidentsService(Func<ModConfig> config, Func<DocumentStore?> store, AiService ai, GroupChatAi chat, GroupMaterial material, ILog log)
	{
		_config = config;
		_store = store;
		_ai = ai;
		_chat = chat;
		_material = material;
		_log = log;
	}

	/// <summary>群里每多一条消息、或"正在输入"变了就加一，界面据此刷新。</summary>
	public int Version { get; private set; }

	public bool LastPostFailed { get; set; }

	/// <summary>页面开着时只响一声，不弹提示。</summary>
	public Func<bool>? IsViewing { get; set; }

	/// <summary>你发言的那一轮给 AI 的约见说明；空表示不能约。</summary>
	public Func<string>? MeetupRule { get; set; }

	/// <summary>有人答应过来：审一下，排不上把他那句换成婉拒。</summary>
	public Func<GroupMeetup, IReadOnlyList<GroupLine>, IReadOnlyList<GroupLine>>? ReviewMeetup { get; set; }

	private ResidentsState? State => _store()?.Get(ResidentsGroup.Path, ResidentsGroup.Doc);

	public IReadOnlyList<ResidentsMessage> Messages => State?.Messages ?? new List<ResidentsMessage>();

	public bool IsBusy => _inFlight != null || _scheduled.Count > 0;

	/// <summary>正在输入的人；"" = AI 还在想（只在回你时显示）；null = 没人。</summary>
	public string? TypingSpeaker => _scheduled.Count > 0 ? _scheduled.Peek().Line.Speaker : _inFlight != null && !_inFlight.ByVillagers ? "" : null;

	public int DailyLimit => ResidentsGroup.DailyLimit(_config().TownChatPostsDaily);

	public int PostsLeft => State is ResidentsState s ? ResidentsGroup.PostsLeft(s, GameState.Today.TotalDays, DailyLimit) : 0;

	public bool IsAiAvailable => _config().AiOn && !_ai.IsDialogueUnavailable();

	public void Reset()
	{
		_inFlight = null;
		_scheduled.Clear();
		LastPostFailed = false;
		Version++;
	}

	/// <summary>认识的、有手机、启用了 AI 的大人，按心数从高到低。</summary>
	public IReadOnlyList<NPC> Members()
	{
		Farmer? player = Game1.player;
		if (player == null)
		{
			return Array.Empty<NPC>();
		}
		Dictionary<string, NPC> found = new Dictionary<string, NPC>(StringComparer.OrdinalIgnoreCase);
		foreach (NPC npc in Utility.getAllCharacters())
		{
			if (npc == null || !npc.IsVillager || string.IsNullOrWhiteSpace(npc.Name) || found.ContainsKey(npc.Name))
			{
				continue;
			}
			if (!player.friendshipData.ContainsKey(npc.Name) || !ResidentsGroup.HasPhone(npc.Name) || ChildSafety.IsChild(npc.Name, npc.Age) || !_material.Npcs.IsEnabled(npc.Name))
			{
				continue;
			}
			found[npc.Name] = npc;
		}
		return found.Values
			.OrderByDescending(n => player.getFriendshipHeartLevelForNPC(n.Name))
			.ThenBy(n => n.displayName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	public ResidentsBlock Check(string? text) => ResidentsGroup.Check(text, IsAiAvailable, IsBusy, PostsLeft, Members().Count);

	public ResidentsBlock Send(string text)
	{
		ResidentsBlock block = Check(text);
		ResidentsState? s = State;
		if (block != ResidentsBlock.None || s == null)
		{
			return block == ResidentsBlock.None ? ResidentsBlock.AiOff : block;
		}
		string post = ResidentsGroup.Normalize(text);
		IReadOnlyList<NPC> members = Members();
		Farmer player = Game1.player;
		List<string> names = ResidentsGroup.Candidates(post, members.Select(n => (n.Name, _material.Display(n.Name), player.getFriendshipHeartLevelForNPC(n.Name))).ToList(), s.Messages, _random);
		List<NPC> pool = names.Select(n => members.First(m => m.Name == n)).ToList();
		GroupRequest request = Request(pool, post) with
		{
			Addressed = GroupRules.Addressed(post, pool.Select(n => (n.Name, _material.Display(n.Name))))
		};
		Append(s, new ResidentsMessage { Speaker = player.Name, IsPlayer = true, Text = post });
		ResidentsGroup.CountPost(s, GameState.Today.TotalDays);
		_store()?.MarkDirty(ResidentsGroup.Path);
		LastPostFailed = false;
		Start(post, request, pool);
		_log.Debug("居民群：你发了一条，候选 " + string.Join(", ", names));
		return ResidentsBlock.None;
	}

	/// <summary>村民自己在群里聊起来。</summary>
	public bool StartVillagerChat()
	{
		if (IsBusy || !IsAiAvailable || State == null)
		{
			return false;
		}
		IReadOnlyList<NPC> members = Members();
		if (members.Count < ResidentsGroup.VillagerChatMinMembers)
		{
			return false;
		}
		List<string> names = ResidentsGroup.VillagerPool(members.Select(n => n.Name).ToList(), (a, b) => GroupPrompts.Relation(a, b).Length > 0, _random);
		List<NPC> pool = names.Select(n => members.First(m => m.Name == n)).ToList();
		string topic = _material.FarmerTopic(new[] { names[0] }, "phone");
		Start("", Request(pool, null) with { FarmerTopic = topic }, pool);
		_log.Debug($"居民群：村民自己聊起来，{names[0]} 起头，还有 {string.Join(", ", names.Skip(1))}" + (topic.Length > 0 ? $"；从农夫的事聊起：{topic}" : ""));
		return true;
	}

	/// <summary>写好的几句，照打字的速度一句句发出来；说话的人都得在群里。</summary>
	public bool PostScripted(IReadOnlyList<GroupLine> lines)
	{
		if (State == null || IsBusy || lines.Count == 0)
		{
			return false;
		}
		HashSet<string> members = new HashSet<string>(Members().Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
		if (lines.Any(l => !members.Contains(l.Speaker) || string.IsNullOrWhiteSpace(l.Text)))
		{
			return false;
		}
		DateTime at = DateTime.UtcNow;
		foreach (GroupLine line in lines)
		{
			at += GroupRules.TypingDelay(line.Text.Length);
			_scheduled.Enqueue(new Scheduled(line, at));
		}
		Version++;
		return true;
	}

	/// <summary>每帧（主线程）。</summary>
	public void OnUpdateTicked()
	{
		if (!Context.IsWorldReady || State == null)
		{
			return;
		}
		try
		{
			PlanVillagerChats();
			Collect();
			Deliver();
			TryVillagerChat();
		}
		catch (Exception ex)
		{
			_inFlight = null;
			_scheduled.Clear();
			_log.Warn("居民群出错：" + ex.Message);
		}
	}

	private GroupRequest Request(IReadOnlyList<NPC> pool, string? post)
	{
		GameLocation town = Game1.getLocationFromName("Town") ?? Game1.currentLocation;
		List<GroupLine> transcript = Messages.TakeLast(GroupPrompts.PhoneTranscriptLines).Select(m => new GroupLine(m.Speaker, m.Text, m.Emotion)).ToList();
		string rule = string.IsNullOrEmpty(post) ? "" : MeetupRule?.Invoke() ?? "";
		return _material.Request(pool, town, GroupSetting.Phone, transcript, post, final: false, _material.Shared()) with { Location = "Town", MeetupRule = rule };
	}

	private void Start(string post, GroupRequest request, IReadOnlyList<NPC> pool)
	{
		Dictionary<string, string> display = pool.ToDictionary(n => n.Name, n => _material.Display(n.Name), StringComparer.OrdinalIgnoreCase);
		_inFlight = new InFlight(post, Task.Run(() => _chat.TurnAsync(request, n => display.TryGetValue(n, out string? d) ? d : null)), DateTime.UtcNow);
		Version++;
	}

	private void Collect()
	{
		InFlight? f = _inFlight;
		if (f == null)
		{
			return;
		}
		if (!f.Turn.IsCompleted)
		{
			if (DateTime.UtcNow - f.Started > ResidentsGroup.Timeout)
			{
				_inFlight = null;
				LastPostFailed = !f.ByVillagers;
				Version++;
				_log.Info("居民群：等太久没人回。");
			}
			return;
		}
		_inFlight = null;
		GroupTurn? turn = f.Turn.Status == TaskStatus.RanToCompletion ? f.Turn.Result : null;
		List<GroupLine> lines = (turn?.Lines ?? Array.Empty<GroupLine>()).Where(l => !string.IsNullOrWhiteSpace(l.Text)).Take(f.ByVillagers ? ResidentsGroup.MaxVillagerLines : ResidentsGroup.MaxReplies).ToList();
		if (lines.Count == 0)
		{
			LastPostFailed = !f.ByVillagers;
			Version++;
			_log.Info("居民群：这次没有回复。");
			return;
		}
		if (!f.ByVillagers && turn?.Meetup != null && ReviewMeetup != null)
		{
			try
			{
				lines = ReviewMeetup(turn.Meetup, lines).ToList();
			}
			catch (Exception ex)
			{
				_log.Warn("居民群：约见没审成：" + ex.Message);
			}
		}
		DateTime at = DateTime.UtcNow;
		foreach (GroupLine line in lines)
		{
			at += GroupRules.TypingDelay(line.Text.Length);
			_scheduled.Enqueue(new Scheduled(line, at));
		}
		if (!f.ByVillagers)
		{
			RecordMemories(f.Post, lines);
		}
		Version++;
	}

	private void Deliver()
	{
		ResidentsState? s = State;
		bool any = false;
		while (s != null && _scheduled.Count > 0 && DateTime.UtcNow >= _scheduled.Peek().ShowAt)
		{
			GroupLine line = _scheduled.Dequeue().Line;
			Append(s, new ResidentsMessage { Speaker = line.Speaker, Text = line.Text.Trim(), Emotion = line.Emotion });
			any = true;
			Notify(line);
		}
		if (any)
		{
			_store()?.MarkDirty(ResidentsGroup.Path);
			Version++;
		}
	}

	private void Notify(GroupLine line)
	{
		if (IsViewing?.Invoke() ?? false)
		{
			Game1.playSound("dwop");
			return;
		}
		string text = line.Text.Length <= 40 ? line.Text : line.Text.Substring(0, 40) + "…";
		Game1.addHUDMessage(new HUDMessage($"{Tr("residents.title", "居民群")} · {_material.Display(line.Speaker)}：{text}", 2) { noIcon = true });
	}

	private void RecordMemories(string post, IReadOnlyList<GroupLine> lines)
	{
		GameDate today = GameState.Today;
		string player = Game1.player.Name;
		foreach (IGrouping<string, GroupLine> group in lines.GroupBy(l => l.Speaker, StringComparer.OrdinalIgnoreCase))
		{
			List<GroupLine> others = lines.Where(l => !string.Equals(l.Speaker, group.Key, StringComparison.OrdinalIgnoreCase)).ToList();
			_material.Log.Add(group.Key, new ConversationEntry
			{
				Day = today.Day,
				Season = today.Season,
				Year = today.Year,
				Farmer = player,
				Setting = ResidentsGroup.ContextType,
				SettingRef = "phone",
				SettingName = "居民群",
				Opener = "[农夫在手机上的鹈鹕镇居民群里发了一条]",
				FarmerReply = post,
				Comeback = string.Join(" ", group.Select(l => l.Text)),
				Face = group.Last().Emotion,
				Company = others.Select(l => l.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
				CompanySaid = string.Join(" ", others.Select(l => l.Speaker + ": " + l.Text))
			});
		}
	}

	private void PlanVillagerChats()
	{
		ResidentsState s = State!;
		int today = GameState.Today.TotalDays;
		if (s.NpcPlanDay == today)
		{
			return;
		}
		s.NpcPlanDay = today;
		s.NpcPlannedTimes = ResidentsGroup.PlanTimes(ResidentsGroup.VillagerChatsPerDay(_config().TownChatTalksDaily), Game1.timeOfDay, _random);
		_store()?.MarkDirty(ResidentsGroup.Path);
		if (s.NpcPlannedTimes.Count > 0)
		{
			_log.Debug("居民群：今天村民自己聊的时间 " + string.Join(", ", s.NpcPlannedTimes));
		}
	}

	private void TryVillagerChat()
	{
		ResidentsState s = State!;
		if (IsBusy || s.NpcPlannedTimes.Count == 0 || Game1.timeOfDay < s.NpcPlannedTimes[0])
		{
			return;
		}
		if (Game1.eventUp || Game1.isFestival() || Game1.farmEvent != null || !IsAiAvailable)
		{
			return;
		}
		s.NpcPlannedTimes.RemoveAt(0);
		_store()?.MarkDirty(ResidentsGroup.Path);
		StartVillagerChat();
	}

	private static void Append(ResidentsState s, ResidentsMessage m)
	{
		m.Day = Game1.dayOfMonth;
		m.Season = Game1.currentSeason;
		m.Year = Game1.year;
		m.Time = Game1.timeOfDay;
		ResidentsGroup.Append(s, m);
	}

	private static string Tr(string key, string fallback) => AISV.Core.Text.Tr.Get(key, fallback);
}
