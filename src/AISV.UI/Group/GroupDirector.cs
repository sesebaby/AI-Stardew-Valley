using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Approach;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Social.Gossip;
using AISV.Core.Text;
using AISV.Game.Approach;
using AISV.Game.Dialogue;
using AISV.Game.Group;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.UI.Group;

/// <summary>
/// 群聊的流程：
/// 地图模式下，你附近站在一起的村民按概率自己聊起来，路过的人会加入，你按插话键（或点他们）加入；也可以按插话键和身边两位以上的村民直接聊。
/// 窗口模式下，进门时按概率由一位村民走过来，把你拉进他们的话题，在聊天窗口里聊。
/// 每位成员各记一笔对话记录，你开口的那几轮按 AI 给的分数加减好感。
/// </summary>
public sealed class GroupDirector
{
	private sealed class Pending
	{
		public Pending(string id, string lead, IReadOnlyList<string> others, string location, string shared)
		{
			Id = id;
			Lead = lead;
			Others = others;
			Location = location;
			Shared = shared;
		}

		public string Id { get; }

		public string Lead { get; }

		public IReadOnlyList<string> Others { get; }

		public string Location { get; }

		public string Shared { get; }

		public GroupTurn? Opening { get; set; }

		public CancellationTokenSource Cancel { get; } = new CancellationTokenSource();

		public bool Includes(string npc) => Same(Lead, npc) || Others.Any(o => Same(o, npc));
	}

	private sealed class Session
	{
		private readonly Dictionary<string, List<string>> _saidBefore = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		public Session(IReadOnlyList<NPC> members, GameLocation location, IGroupView view, int replies, string shared)
		{
			Npcs = members.ToList();
			Location = location;
			View = view;
			RepliesLeft = replies;
			Shared = shared;
		}

		public List<NPC> Npcs { get; }

		public NPC Lead => Npcs[0];

		public GameLocation Location { get; }

		public IGroupView View { get; }

		public MapGroupChat? Map => View as MapGroupChat;

		public int RepliesLeft { get; set; }

		public string Shared { get; }

		public bool AmongThemselves { get; set; }

		public bool PlayerStarted { get; set; }

		public int RoundsLeft { get; set; }

		public bool LastRoundFinal { get; set; }

		public bool RoundInFlight { get; set; }

		public int RoundId { get; set; }

		public int NextRoundTick { get; set; } = -1;

		public CancellationTokenSource? RoundCancel { get; set; }

		public List<string> Joiners { get; } = new List<string>();

		public List<GroupLine> Transcript { get; } = new List<GroupLine>();

		public Dictionary<string, string> LastEmotion { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public CancellationTokenSource Cancel { get; } = new CancellationTokenSource();

		public string? Unanswered { get; set; }

		public bool PlayerSpoke { get; set; }

		public bool Includes(string npc) => Npcs.Any(n => Same(n.Name, npc));

		public IReadOnlyList<NPC> CompanionsOf(NPC npc) => Npcs.Where(n => n != npc).ToList();

		public string Names => string.Join(", ", Npcs.Select(n => n.Name));

		public IReadOnlyList<string> SaidBefore(NPC npc) => _saidBefore.TryGetValue(npc.Name, out List<string>? said) ? said : Array.Empty<string>();

		public void Absorb(GroupTurn turn)
		{
			Transcript.AddRange(turn.Lines);
			_saidBefore.Clear();
			foreach (GroupLine line in turn.Lines)
			{
				if (!_saidBefore.TryGetValue(line.Speaker, out List<string>? said))
				{
					_saidBefore[line.Speaker] = said = new List<string>();
				}
				said.Add(line.Text);
			}
		}
	}

	private readonly Func<ModConfig> _config;

	private readonly AiService _ai;

	private readonly GroupChatAi _chat;

	private readonly GroupMaterial _material;

	private readonly ApproachCoordinator _approaches;

	private readonly DialogueEngine _dialogue;

	private readonly IModHelper _helper;

	private readonly ILog _log;

	private readonly HashSet<string> _rolledToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _groupedToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _cooldownUntil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Random _random = new Random();

	private int _tick;

	private string? _checkLocation;

	private int _checkTick;

	private Pending? _pending;

	private Session? _session;

	private int _ambientToday;

	private int _lastAmbientSlot = -1;

	private bool _forceAmbient;

	/// <summary>玩家在群聊里说了一句。</summary>
	public event Action? PlayerSpoke;

	public GroupDirector(Func<ModConfig> config, AiService ai, GroupChatAi chat, GroupMaterial material, ApproachCoordinator approaches, DialogueEngine dialogue, IModHelper helper, ILog log)
	{
		_config = config;
		_ai = ai;
		_chat = chat;
		_material = material;
		_approaches = approaches;
		_dialogue = dialogue;
		_helper = helper;
		_log = log;
	}

	public SocialSystem? Social { get; set; }

	/// <summary>村民被别的事占着（陪钓、走过来说话……）。</summary>
	public Func<string, bool>? IsNpcOccupied { get; set; }

	public Func<bool>? InDayTransition { get; set; }

	public bool IsNpcInvolved(string npc) => (_pending?.Includes(npc) ?? false) || (_session?.Includes(npc) ?? false);

	public bool IsActive => _session != null || _pending != null;

	private static int NowMinutes => Game1.timeOfDay / 100 * 60 + Game1.timeOfDay % 100;

	private string KeyLabel() => _config().KeyGroupTalk ?? "";

	public void OnWarped(WarpedEventArgs e)
	{
		if (e.IsLocalPlayer && e.NewLocation != null)
		{
			_checkLocation = e.NewLocation.NameOrUniqueName;
			_checkTick = _tick + GroupRules.EntryDelayTicks;
		}
	}

	public void OnUpdateTicked()
	{
		if (!Context.IsMainPlayer)
		{
			return;
		}
		_tick++;
		if (_session?.Map is MapGroupChat map)
		{
			map.Update(Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16.7);
			TickAmbient(_session, map);
		}
		if (_session != null && !_session.View.IsOpen)
		{
			Finish(_session);
		}
		if (_checkLocation != null && _tick >= _checkTick)
		{
			string location = _checkLocation;
			_checkLocation = null;
			if (!_config().WorldGroupTalkOn)
			{
				TryStart(location, forced: false, out _);
			}
		}
		if (_tick % 60 == 0)
		{
			TryAmbientStart();
		}
	}

	public void OnDayStarted()
	{
		Reset();
		_rolledToday.Clear();
		_groupedToday.Clear();
		_cooldownUntil.Clear();
		_ambientToday = 0;
		_lastAmbientSlot = -1;
	}

	public void Reset()
	{
		_checkLocation = null;
		_pending?.Cancel.Cancel();
		_pending = null;
		Session? session = _session;
		_session = null;
		if (session != null)
		{
			session.Cancel.Cancel();
			session.RoundCancel?.Cancel();
			session.View.Close();
		}
	}

	public void OnRenderedHud(SpriteBatch b)
	{
		if (_session?.Map is MapGroupChat map && Game1.activeClickableMenu == null)
		{
			map.Draw(b);
		}
	}

	public void OnButtonPressed(ButtonPressedEventArgs e)
	{
		if (!Context.IsWorldReady || !Context.IsPlayerFree)
		{
			return;
		}
		string key = KeyLabel().Trim();
		if (key.Length > 0 && Enum.TryParse(key, true, out SButton button) && button == e.Button)
		{
			OnGroupKey();
		}
		else if (e.Button.IsActionButton() && TryInterject(e.Cursor.AbsolutePixels))
		{
			_helper.Input.Suppress(e.Button);
		}
	}

	// ---------------- 地图上自己聊起来 ----------------

	private bool OnCooldown(NPC npc) => _cooldownUntil.TryGetValue(npc.Name, out int until) && NowMinutes < until;

	/// <summary>每过游戏里 10 分钟看一次：你附近有没有两位以上村民站在一起，有就按概率让他们聊起来。</summary>
	private void TryAmbientStart()
	{
		ModConfig c = _config();
		if (!c.WorldGroupTalkOn || _session != null || _pending != null || !Context.IsPlayerFree || Game1.activeClickableMenu != null)
		{
			return;
		}
		int chance = _forceAmbient ? 100 : c.PassingTalkPercent;
		int perDay = _forceAmbient ? Math.Max(_ambientToday + 1, c.PassingTalksDaily) : c.PassingTalksDaily;
		if (_ambientToday >= perDay || chance <= 0 || Game1.timeOfDay == _lastAmbientSlot || !CanStart(out GameLocation location, out Farmer player, out _))
		{
			return;
		}
		_lastAmbientSlot = Game1.timeOfDay;
		List<NPC> standing = location.characters.Where(n => IsEligible(n, forced: true) && !OnCooldown(n) && !n.isMoving() && Vector2.Distance(n.Tile, player.Tile) <= GroupRules.HearTiles).ToList();
		List<(NPC A, NPC B)> pairs = new List<(NPC, NPC)>();
		for (int i = 0; i < standing.Count; i++)
		{
			for (int j = i + 1; j < standing.Count; j++)
			{
				if (Vector2.Distance(standing[i].Tile, standing[j].Tile) <= GroupRules.TogetherTiles)
				{
					pairs.Add((standing[i], standing[j]));
				}
			}
		}
		if (pairs.Count == 0 || _random.Next(100) >= chance)
		{
			return;
		}
		(NPC a, NPC b) = pairs[_random.Next(pairs.Count)];
		Vector2 center = (a.Tile + b.Tile) / 2f;
		int max = GroupRules.MaxMembers(c.GroupTalkSize);
		List<NPC> members = new List<NPC> { a, b };
		members.AddRange(standing.Where(n => n != a && n != b && Vector2.Distance(n.Tile, center) <= GroupRules.TogetherTiles).OrderBy(n => Vector2.Distance(n.Tile, center)).Take(max - 2));
		StartOnMap(location, members, playerStarted: false, aiOpening: true);
	}

	/// <summary>开一场地图群聊。aiOpening = 先让 AI 写开场；否则等你先开口。</summary>
	private MapGroupChat? StartOnMap(GameLocation location, List<NPC> members, bool playerStarted, bool aiOpening)
	{
		if (members.Count < 2 || _session != null)
		{
			return null;
		}
		ModConfig c = _config();
		int replies = GroupRules.Replies(c.RepliesPerTalk);
		MapGroupChat view = new MapGroupChat(members, location, replies, KeyLabel, _material.Display);
		Session session = new Session(members, location, view, replies, _material.Shared())
		{
			AmongThemselves = true,
			PlayerStarted = playerStarted,
			RoundsLeft = GroupRules.AmbientRounds(c.PassingTalkRounds)
		};
		view.Submitted += text => OnPlayerText(session, text);
		view.RoundFinished += () => OnRoundFinished(session);
		view.InputCancelled += () => OnInputCancelled(session);
		_session = session;
		if (!playerStarted)
		{
			_ambientToday++;
		}
		if (aiOpening)
		{
			view.PromptAfterRound = playerStarted;
			if (playerStarted)
			{
				view.ShowThinking();
			}
			RequestRound(session);
		}
		_log.Debug($"群聊：{(playerStarted ? "你和" : "")}{session.Names} 在地图上聊起来了。");
		return view;
	}

	private void RequestRound(Session session)
	{
		MapGroupChat? view = session.Map;
		if (view == null || !view.IsOpen)
		{
			return;
		}
		session.RoundCancel?.Cancel();
		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cancel.Token);
		session.RoundCancel = cts;
		int round = ++session.RoundId;
		bool final = session.RoundsLeft <= 1;
		List<string> joined = session.Joiners.ToList();
		session.Joiners.Clear();
		GroupRequest request = _material.Request(session.Npcs, session.Location, GroupSetting.OnMap, session.Transcript, null, final, session.Shared) with
		{
			AmongThemselves = !(session.PlayerStarted && session.Transcript.Count == 0),
			FarmerDistance = (int)Math.Round(view.PlayerDistance),
			FarmerJoined = view.PlayerJoined,
			JustJoined = joined
		};
		if (request.AmongThemselves && session.Transcript.Count == 0 && request.FarmerDistance > 7)
		{
			string topic = _material.FarmerTopic(session.Npcs.Select(n => n.Name), "map");
			if (topic.Length > 0)
			{
				request = request with { FarmerTopic = topic };
				_log.Debug($"群聊：背着农夫聊起他的事：{topic}");
			}
		}
		session.RoundInFlight = true;
		session.NextRoundTick = -1;
		Run(request, session.Npcs, cts.Token, turn => OnRoundReady(session, round, turn, final));
	}

	private void OnRoundReady(Session session, int round, GroupTurn? turn, bool final)
	{
		if (_session != session || round != session.RoundId)
		{
			return;
		}
		session.RoundInFlight = false;
		MapGroupChat? view = session.Map;
		if (view == null || !view.IsOpen)
		{
			return;
		}
		if (turn == null)
		{
			if (session.PlayerStarted && session.Transcript.Count == 0)
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("group.failed", "大家一时不知道说什么"), 3));
			}
			view.Close();
			return;
		}
		session.Absorb(turn);
		session.RoundsLeft--;
		session.LastRoundFinal = final;
		view.ShowLines(turn.Lines, session.RepliesLeft);
	}

	/// <summary>一轮说完：说好是最后一轮、没轮次了或你走远了就散；否则过一会儿接着聊（你插过话就多等一会儿）。</summary>
	private void OnRoundFinished(Session session)
	{
		MapGroupChat? view = session.Map;
		if (_session != session || view == null)
		{
			return;
		}
		if (session.LastRoundFinal || session.RoundsLeft <= 0 || view.PlayerDistance > GroupRules.HearTiles)
		{
			view.Close();
			return;
		}
		session.NextRoundTick = _tick + (view.PlayerJoined ? GroupRules.NextRoundAfterFarmerTicks : GroupRules.NextRoundTicks);
	}

	private void OnInputCancelled(Session session)
	{
		if (_session == session && session.Transcript.Count == 0 && !session.RoundInFlight)
		{
			session.Map?.Close();
		}
	}

	private void TickAmbient(Session session, MapGroupChat view)
	{
		if (!session.AmongThemselves || !view.IsOpen)
		{
			return;
		}
		if (session.NextRoundTick >= 0 && _tick >= session.NextRoundTick && !view.IsTyping && Game1.activeClickableMenu == null)
		{
			session.NextRoundTick = -1;
			if (view.PlayerDistance > GroupRules.HearTiles)
			{
				view.Close();
				return;
			}
			RequestRound(session);
		}
		if (_tick % 30 != 0 || session.Npcs.Count >= GroupRules.MaxMembers(_config().GroupTalkSize))
		{
			return;
		}
		Vector2 center = view.Center;
		NPC? joiner = session.Location.characters
			.Where(n => !session.Includes(n.Name) && IsEligible(n, forced: true) && !OnCooldown(n) && Vector2.Distance(n.Tile, center) <= GroupRules.JoinTiles)
			.OrderBy(n => Vector2.Distance(n.Tile, center))
			.FirstOrDefault();
		if (joiner != null)
		{
			session.Npcs.Add(joiner);
			view.AddMember(joiner);
			session.Joiners.Add(joiner.Name);
			_log.Debug($"群聊：{joiner.Name} 走过来加入了 {session.Names}。");
		}
	}

	/// <summary>插话键：附近有人在聊就弹出输入栏；没有的话，身边 4 格内站着两位以上村民就直接聊起来（你先开口）。</summary>
	public void OnGroupKey()
	{
		if (!_config().WorldGroupTalkOn || !Context.IsPlayerFree)
		{
			return;
		}
		if (_session?.Map is MapGroupChat map && map.IsOpen)
		{
			if (map.PlayerDistance <= GroupRules.HintTiles)
			{
				map.RequestInput();
			}
			return;
		}
		if (_session != null || _pending != null || !CanStart(out GameLocation location, out Farmer player, out _))
		{
			return;
		}
		List<NPC> near = location.characters
			.Where(n => IsEligible(n, forced: true) && Vector2.Distance(n.Tile, player.Tile) <= GroupRules.KeyStartTiles)
			.OrderBy(n => Vector2.Distance(n.Tile, player.Tile))
			.Take(GroupRules.MaxMembers(_config().GroupTalkSize))
			.ToList();
		if (near.Count >= 2)
		{
			StartOnMap(location, near, playerStarted: true, aiOpening: false)?.RequestInput();
		}
	}

	/// <summary>点了正在群聊的村民：当作插话，不打开一对一对话。返回 true 表示吞掉这次点击。</summary>
	public bool TryInterject(Vector2 cursor)
	{
		if (_session?.Map is not MapGroupChat map || !map.IsOpen || !Context.IsPlayerFree)
		{
			return false;
		}
		Point p = new Point((int)cursor.X, (int)cursor.Y);
		NPC? hit = map.Members.FirstOrDefault(n =>
		{
			Rectangle box = n.GetBoundingBox();
			return new Rectangle(box.X - 8, box.Y - 96, box.Width + 16, box.Height + 104).Contains(p);
		});
		if (hit == null || Vector2.Distance(hit.Tile, Game1.player.Tile) > GroupRules.ClickTiles)
		{
			return false;
		}
		if (!map.RequestInput())
		{
			_log.Trace($"群聊：点了 {hit.Name}，但现在开不了输入栏（{map.PhaseName}）。");
		}
		return true;
	}

	// ---------------- 你点"一起聊" ----------------

	/// <summary>离这位村民 6 格内、最近的另一位合适村民。</summary>
	public NPC? FindCompanion(NPC lead)
	{
		if (lead.currentLocation == null || lead.currentLocation != Game1.currentLocation)
		{
			return null;
		}
		return lead.currentLocation.characters
			.Where(n => n != lead && !Same(n.Name, lead.Name) && IsEligible(n, forced: true) && Vector2.Distance(n.Tile, lead.Tile) <= GroupRules.PairTiles)
			.OrderBy(n => Vector2.Distance(n.Tile, lead.Tile))
			.FirstOrDefault();
	}

	public bool CanStartWith(NPC lead, NPC other, out string reason)
	{
		if (!CanStart(out GameLocation location, out _, out reason))
		{
			return false;
		}
		if (lead.currentLocation != location || other.currentLocation != location)
		{
			return Fail(out reason, "村民不在这张地图上");
		}
		if (!IsEligible(lead, forced: true) || !IsEligible(other, forced: true))
		{
			return Fail(out reason, "有村民正忙、没启用或正在想台词");
		}
		if (_groupedToday.Contains(lead.Name) || _groupedToday.Contains(other.Name))
		{
			return Fail(out reason, "今天已经一起聊过了");
		}
		if (Vector2.Distance(lead.Tile, other.Tile) > GroupRules.PairTiles)
		{
			return Fail(out reason, "两人离得太远");
		}
		reason = "";
		return true;
	}

	/// <summary>地图模式下原地聊起来（AI 先写一段把你拉进来的开场，说完弹出输入栏）；窗口模式下走过来拉你聊。</summary>
	public bool TryStartWith(NPC lead, NPC other, out string detail)
	{
		if (!CanStartWith(lead, other, out detail))
		{
			return false;
		}
		GameLocation here = Game1.currentLocation;
		if (!_config().WorldGroupTalkOn)
		{
			return StartPending(here, Game1.player, lead.Name, other.Name, lead.Tile, playerStarted: true, out detail);
		}
		List<NPC> members = new List<NPC> { lead };
		members.AddRange(Gather(lead, other).Select(n => Find(here, n)).OfType<NPC>());
		if (StartOnMap(here, members, playerStarted: true, aiOpening: true) == null)
		{
			return Fail(out detail, "没开起来");
		}
		detail = "和 " + string.Join(", ", members.Select(n => n.Name)) + " 聊起来";
		return true;
	}

	// ---------------- 进门时被拉进去（窗口模式） ----------------

	private bool TryStart(string locationName, bool forced, out string detail)
	{
		if (!CanStart(out GameLocation location, out Farmer player, out detail))
		{
			return false;
		}
		if (!Same(location.NameOrUniqueName, locationName))
		{
			return Fail(out detail, "已经离开那里了");
		}
		int chance = _config().NoticedGroupTalkPercent;
		if (!forced && (chance <= 0 || _rolledToday.Contains(location.NameOrUniqueName)))
		{
			return Fail(out detail, "机会是 0% 或今天这里已经掷过");
		}
		List<GroupSpot> spots = location.characters.Where(n => IsEligible(n, forced)).Select(n => new GroupSpot(n.Name, n.Tile.X, n.Tile.Y)).ToList();
		(GroupSpot Lead, GroupSpot Other)? pair = GroupRules.PickPair(spots, player.Tile.X, player.Tile.Y);
		if (pair == null)
		{
			return Fail(out detail, $"这里没有两位站在一起的合适村民（合适的 {spots.Count} 位）");
		}
		if (!forced)
		{
			_rolledToday.Add(location.NameOrUniqueName);
			if (_random.Next(100) >= chance)
			{
				return Fail(out detail, "没掷中");
			}
		}
		return StartPending(location, player, pair.Value.Lead.Name, pair.Value.Other.Name, new Vector2(pair.Value.Lead.X, pair.Value.Lead.Y), playerStarted: false, out detail);
	}

	/// <summary>成员：带头的人和另一位，再加上离带头的人 6 格内的其他合适村民（近的优先），不超过上限。</summary>
	private List<string> Gather(NPC lead, NPC other)
	{
		List<string> list = new List<string> { other.Name };
		int max = GroupRules.MaxMembers(_config().GroupTalkSize);
		foreach (NPC n in (lead.currentLocation?.characters ?? Enumerable.Empty<NPC>())
			.Where(n => n != lead && n != other && IsEligible(n, forced: true) && !_groupedToday.Contains(n.Name) && Vector2.Distance(n.Tile, lead.Tile) <= GroupRules.PairTiles)
			.OrderBy(n => Vector2.Distance(n.Tile, lead.Tile)))
		{
			if (list.Count + 1 >= max)
			{
				break;
			}
			list.Add(n.Name);
		}
		return list;
	}

	private bool StartPending(GameLocation location, Farmer player, string lead, string other, Vector2 leadTile, bool playerStarted, out string detail)
	{
		NPC? leadNpc = Find(location, lead);
		NPC? otherNpc = Find(location, other);
		IReadOnlyList<string> others = leadNpc != null && otherNpc != null ? Gather(leadNpc, otherNpc) : new List<string> { other };
		Pending pending = new Pending($"group:{Guid.NewGuid():N}", lead, others, location.NameOrUniqueName, _material.Shared());
		List<NPC> members = new[] { lead }.Concat(others).Select(n => Find(location, n)).OfType<NPC>().ToList();
		GroupRequest request = _material.Request(members, location, GroupSetting.FaceToFace, Array.Empty<GroupLine>(), null, final: false, pending.Shared);
		Dictionary<string, string> display = members.ToDictionary(n => n.Name, n => _material.Display(n.Name), StringComparer.OrdinalIgnoreCase);
		_pending = pending;
		EnqueueResult result = _approaches.Enqueue(new ApproachIntent("seen-and-heard", pending.Id, lead, $"{Game1.Date.TotalDays}:group:{location.NameOrUniqueName}", location.NameOrUniqueName, Game1.timeOfDay)
		{
			Relevance = 0.6,
			DistanceTiles = Vector2.Distance(leadTile, player.Tile),
			Priority = 40,
			TtlTicks = 3600,
			PrepareDialogue = async ct =>
			{
				using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, pending.Cancel.Token);
				GroupTurn? turn = await _chat.TurnAsync(request, n => display.TryGetValue(n, out string? d) ? d : null, linked.Token).ConfigureAwait(false);
				pending.Opening = turn;
				return turn?.Lines[0].Text;
			},
			StartWhilePreparing = true,
			ShowAttentionMarker = !playerStarted,
			AnnounceText = playerStarted ? null : Tr.Get("group.callout", "{{player}}！过来一起聊聊？", ("player", player.Name)),
			AnnounceTextNpcName = playerStarted ? null : other,
			CanStartNow = () => OtherStillAround(pending),
			CanPresent = _ => OtherStillAround(pending),
			Present = () => Present(pending),
			OnFinished = outcome =>
			{
				if (_pending == pending)
				{
					_pending = null;
					pending.Cancel.Cancel();
					_log.Debug($"群聊：和 {pending.Lead}、{string.Join(", ", pending.Others)} 的群聊没开起来（{outcome}）。");
				}
			}
		});
		if (result is not (EnqueueResult.Accepted or EnqueueResult.Replaced))
		{
			if (_pending == pending)
			{
				_pending = null;
			}
			return Fail(out detail, $"走过来的请求没被接受（{result}）");
		}
		detail = $"{lead} 走过来，{string.Join(", ", others)} 一起聊";
		_log.Debug($"群聊：{lead} 和 {string.Join(", ", others)} 要把你拉进话题（{location.NameOrUniqueName}）。");
		return true;
	}

	private bool OtherStillAround(Pending pending)
	{
		GameLocation? here = Game1.currentLocation;
		if (here == null || !Same(here.NameOrUniqueName, pending.Location))
		{
			return false;
		}
		NPC? other = Find(here, pending.Others[0]);
		return other != null && !(IsNpcOccupied?.Invoke(other.Name) ?? false) && Vector2.Distance(other.Tile, Game1.player.Tile) <= GroupRules.OtherStaysTiles;
	}

	private bool Present(Pending pending)
	{
		GameLocation? here = Game1.currentLocation;
		GroupTurn? opening = pending.Opening;
		if (opening == null || here == null || Game1.activeClickableMenu != null || Game1.dialogueUp || _session != null)
		{
			return false;
		}
		NPC? lead = Find(here, pending.Lead);
		List<NPC> others = pending.Others.Select(n => Find(here, n)).OfType<NPC>().ToList();
		if (lead == null || others.Count == 0)
		{
			return false;
		}
		int replies = GroupRules.Replies(_config().RepliesPerTalk);
		List<NPC> members = new[] { lead }.Concat(others).ToList();
		GroupChatMenu window = new GroupChatMenu(members, opening.Lines, replies, _material.Display);
		Session session = new Session(members, here, window, replies, pending.Shared);
		session.Absorb(opening);
		window.Submitted += text => OnPlayerText(session, text);
		foreach (NPC o in others)
		{
			o.Halt();
			o.faceGeneralDirection(Game1.player.getStandingPosition(), 0, opposite: false, useTileCalculations: false);
		}
		_session = session;
		if (_pending == pending)
		{
			_pending = null;
		}
		Game1.activeClickableMenu = window;
		Game1.playSound("bigSelect");
		_log.Debug($"群聊：打开了和 {session.Names} 的聊天窗口。");
		return true;
	}

	// ---------------- 你说话之后 ----------------

	private void OnPlayerText(Session session, string text)
	{
		if (_session != session || string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		PlayerSpoke?.Invoke();
		bool final = session.RepliesLeft <= 1;
		session.Transcript.Add(new GroupLine(Game1.player.Name, text));
		session.Unanswered = text;
		GroupSetting setting = session.Map != null ? GroupSetting.OnMap : GroupSetting.FaceToFace;
		GroupRequest request = _material.Request(session.Npcs, session.Location, setting, session.Transcript, text, final, session.Shared) with
		{
			Addressed = GroupRules.Addressed(text, session.Npcs.Select(n => (n.Name, _material.Display(n.Name))))
		};
		if (session.AmongThemselves && session.Map is MapGroupChat map)
		{
			session.RoundCancel?.Cancel();
			session.RoundId++;
			session.RoundInFlight = false;
			session.NextRoundTick = -1;
			session.RoundsLeft = Math.Max(session.RoundsLeft, 1);
			List<string> joined = session.Joiners.ToList();
			session.Joiners.Clear();
			request = request with
			{
				AmongThemselves = true,
				FarmerDistance = (int)Math.Round(map.PlayerDistance),
				FarmerJoined = true,
				JustJoined = joined
			};
		}
		Run(request, session.Npcs, session.Cancel.Token, turn => OnReplyReady(session, text, turn));
	}

	private void OnReplyReady(Session session, string text, GroupTurn? turn)
	{
		if (_session != session)
		{
			return;
		}
		session.Unanswered = null;
		session.PlayerSpoke = true;
		if (turn == null)
		{
			Record(session, text, GroupTurn.Empty);
			Game1.addHUDMessage(new HUDMessage(Tr.Get("group.failed", "大家一时不知道说什么"), 3));
			session.View.Close();
			return;
		}
		Record(session, text, turn);
		session.Absorb(turn);
		session.RepliesLeft--;
		if (session.AmongThemselves && session.RepliesLeft <= 0)
		{
			session.LastRoundFinal = true;
		}
		session.View.ShowLines(turn.Lines, session.RepliesLeft);
	}

	/// <summary>每位成员记一笔：自己说的、别人说的、你说的；你开口时按分数加好感。</summary>
	private void Record(Session session, string text, GroupTurn turn)
	{
		ModConfig c = _config();
		GameDate today = GameState.Today;
		string player = Game1.player.Name;
		foreach (NPC npc in session.Npcs)
		{
			IReadOnlyList<NPC> companions = session.CompanionsOf(npc);
			List<GroupLine> own = turn.Lines.Where(l => Same(l.Speaker, npc.Name)).ToList();
			IEnumerable<string> othersSaid = companions.SelectMany(o => session.SaidBefore(o).Concat(turn.Lines.Where(l => Same(l.Speaker, o.Name)).Select(l => l.Text))
				.Select(t => companions.Count > 1 ? o.Name + ": " + t : t));
			string emotion = own.LastOrDefault()?.Emotion ?? "neutral";
			turn.Friendship.TryGetValue(npc.Name, out int raw);
			int points = _material.Relationships.ClampInteraction(raw);
			_material.Log.Add(npc.Name, new ConversationEntry
			{
				Day = today.Day,
				Season = today.Season,
				Year = today.Year,
				Farmer = player,
				Setting = "group",
				Opener = string.Join(" ", session.SaidBefore(npc)),
				FarmerReply = text,
				Comeback = string.Join(" ", own.Select(l => l.Text)),
				Face = emotion,
				HeartPoints = points,
				Company = companions.Select(o => o.Name).ToList(),
				CompanySaid = string.Join(" ", othersSaid)
			});
			if (points != 0)
			{
				_material.Relationships.Add(npc.Name, points);
			}
			if (own.Count > 0)
			{
				session.LastEmotion[npc.Name] = emotion;
			}
		}
		if (session.PlayerSpoke && Social != null)
		{
			Social.Gossip.RecordInteraction(new DailyInteraction
			{
				Npc = session.Lead.Name,
				Farmer = player,
				Opener = $"[和农夫、{string.Join("、", session.Npcs.Skip(1).Select(n => n.Name))}一起聊天] " + string.Join(" ", session.Npcs.SelectMany(n => session.SaidBefore(n).Select(t => n.Name + ": " + t))),
				Reply = text,
				Comeback = string.Join(" ", turn.Lines.Select(l => l.Speaker + ": " + l.Text)),
				Face = turn.Lines.LastOrDefault(l => Same(l.Speaker, session.Lead.Name))?.Emotion ?? "neutral"
			});
		}
	}

	private void Finish(Session session)
	{
		if (_session != session)
		{
			return;
		}
		_session = null;
		session.Cancel.Cancel();
		session.RoundCancel?.Cancel();
		try
		{
			if (session.Unanswered is string text)
			{
				session.PlayerSpoke = true;
				Record(session, text, GroupTurn.Empty);
			}
			else if (!session.PlayerSpoke && (!session.AmongThemselves || (session.PlayerStarted && session.Transcript.Count > 0)))
			{
				Record(session, GroupRules.ListenedOnly, GroupTurn.Empty);
			}
			foreach (NPC npc in session.Npcs)
			{
				if (session.AmongThemselves)
				{
					_cooldownUntil[npc.Name] = NowMinutes + GroupRules.AmbientCooldownMinutes;
				}
				else
				{
					_groupedToday.Add(npc.Name);
				}
				if (session.LastEmotion.TryGetValue(npc.Name, out string? emotion) && emotion != "neutral" && AISV.Core.Dialogue.ReactionEffects.PlainEmote(emotion) is int emote)
				{
					npc.doEmote(emote);
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("群聊收尾时出错：" + ex.Message);
		}
		_approaches.OnConversationEnded(session.Lead.Name);
		_log.Debug($"群聊：和 {session.Names} 的群聊结束了。");
	}

	// ---------------- 公共 ----------------

	private void Run(GroupRequest request, IReadOnlyList<NPC> members, CancellationToken ct, Action<GroupTurn?> done)
	{
		Dictionary<string, string> display = members.ToDictionary(n => n.Name, n => _material.Display(n.Name), StringComparer.OrdinalIgnoreCase);
		Task.Run(async () =>
		{
			GroupTurn? turn = null;
			try
			{
				turn = await _chat.TurnAsync(request, n => display.TryGetValue(n, out string? d) ? d : null, ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_log.Warn("群聊生成台词时出错：" + ex.Message);
			}
			MainThread.Run(() => done(turn));
		});
	}

	private bool CanStart(out GameLocation location, out Farmer player, out string detail)
	{
		ModConfig c = _config();
		location = Game1.currentLocation;
		player = Game1.player;
		if (!Context.IsWorldReady || !Context.IsMainPlayer || location == null || player == null)
		{
			return Fail(out detail, "存档没准备好或不是房主");
		}
		if (!c.NoticingOn || !c.AiOn)
		{
			return Fail(out detail, "看到听到或 AI 功能关着");
		}
		if (Game1.eventUp || location.currentEvent != null || Game1.isFestival() || (InDayTransition?.Invoke() ?? false))
		{
			return Fail(out detail, "正在过剧情、节日或换天");
		}
		if (_session != null || _pending != null || _approaches.IsBusy)
		{
			return Fail(out detail, "另一场群聊或走过来说话正在进行");
		}
		if (_ai.IsDialogueUnavailable())
		{
			return Fail(out detail, "AI 现在用不了");
		}
		detail = "";
		return true;
	}

	private bool IsEligible(NPC npc, bool forced)
	{
		if (!ApproachCoordinator.IsVillager(npc) || npc.isSleeping.Value || !_material.Npcs.IsEnabled(npc.Name) || (IsNpcOccupied?.Invoke(npc.Name) ?? false) || _dialogue.IsGeneratingFor(npc.Name))
		{
			return false;
		}
		return forced || !_groupedToday.Contains(npc.Name);
	}

	private static NPC? Find(GameLocation location, string name) => location.characters.FirstOrDefault(n => ApproachCoordinator.IsVillager(n) && Same(n.Name, name));

	private static bool Fail(out string detail, string reason)
	{
		detail = reason;
		return false;
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	// ---------------- 调试 ----------------

	public string DebugStart()
	{
		if (!Context.IsWorldReady || Game1.currentLocation == null)
		{
			return "先进存档。";
		}
		return TryStart(Game1.currentLocation.NameOrUniqueName, forced: true, out string detail) ? "群聊开始：" + detail : "没开起来：" + detail;
	}

	public string DebugAmbient()
	{
		_lastAmbientSlot = -1;
		_forceAmbient = true;
		try
		{
			TryAmbientStart();
		}
		finally
		{
			_forceAmbient = false;
		}
		return _session != null ? "聊起来了：" + _session.Names : "你附近没有站在一起的村民";
	}

	public bool DebugClick(string npcName)
	{
		NPC? npc = Game1.currentLocation?.characters.FirstOrDefault(n => Same(n.Name, npcName));
		if (npc == null)
		{
			return false;
		}
		Rectangle box = npc.GetBoundingBox();
		return TryInterject(new Vector2(box.Center.X, box.Y - 32));
	}

	public string Describe()
	{
		if (_pending != null)
		{
			return $"等 {_pending.Lead} 走过来（{string.Join(", ", _pending.Others)}）";
		}
		if (_session == null)
		{
			return "没有群聊";
		}
		string phase = _session.Map?.PhaseName ?? "窗口";
		return $"{_session.Names} · {phase} · 还能说 {_session.RepliesLeft} 句 · 剩 {_session.RoundsLeft} 轮 · 记录 {_session.Transcript.Count} 句";
	}
}
