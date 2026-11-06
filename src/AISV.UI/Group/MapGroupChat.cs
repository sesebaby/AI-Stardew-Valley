using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Group;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace AISV.UI.Group;

internal interface IGroupView
{
	event Action<string>? Submitted;

	bool IsOpen { get; }

	void ShowLines(IReadOnlyList<GroupLine> lines, int repliesLeft);

	void ShowThinking();

	void Close();
}

/// <summary>
/// 地图上的群聊：村民站着一句句在头顶气泡里说，游戏照常走；你按插话键弹出底部输入栏，打字时游戏暂停。
/// 你一开口，没说完的话就停下，带头的人头上冒"…"等回复。换地图、进剧情时自动散。
/// </summary>
internal sealed class MapGroupChat : IGroupView
{
	private enum Phase
	{
		Speaking,
		Idle,
		Waiting,
		Closed
	}

	private sealed class Bubble
	{
		public NPC? Npc;

		public string Text = "";

		public double Elapsed;

		public double Duration;
	}

	internal readonly record struct LogLine(string Name, string Text, bool Mine);

	private const int MaxLog = 40;

	private const double LingerMs = 2500;

	private readonly List<NPC> _members;

	private readonly GameLocation _location;

	private readonly Func<string> _keyLabel;

	private readonly Func<string, string> _display;

	private readonly Queue<Bubble> _queue = new Queue<Bubble>();

	private readonly List<LogLine> _log = new List<LogLine>();

	private Bubble? _current;

	private Bubble? _last;

	private Bubble? _mine;

	private GroupInputBar? _input;

	private Phase _phase = Phase.Idle;

	private double _gapMs;

	private double _idleMs;

	private double _lingerMs;

	private bool _roundReported = true;

	private bool _reportAfterInput;

	private double _dotMs;

	private int _dots;

	public MapGroupChat(IReadOnlyList<NPC> members, GameLocation location, int repliesLeft, Func<string> keyLabel, Func<string, string> display)
	{
		_members = members.ToList();
		_location = location;
		RepliesLeft = repliesLeft;
		_keyLabel = keyLabel;
		_display = display;
		foreach (NPC npc in _members)
		{
			npc.Halt();
		}
		Pin();
	}

	public event Action<string>? Submitted;

	public event Action? RoundFinished;

	public event Action? InputCancelled;

	public bool IsOpen => _phase != Phase.Closed;

	public bool PlayerJoined { get; private set; }

	/// <summary>这一轮说完后自动弹出输入栏一次。</summary>
	public bool PromptAfterRound { get; set; }

	public int RepliesLeft { get; private set; }

	public IReadOnlyList<NPC> Members => _members;

	internal IReadOnlyList<LogLine> Log => _log;

	public bool IsTyping => _input != null;

	public string PhaseName => _input != null ? "Typing" : _phase.ToString();

	public Vector2 Center => _members.Count == 0 ? Vector2.Zero : new Vector2(_members.Average(n => n.Tile.X), _members.Average(n => n.Tile.Y));

	public float PlayerDistance => Game1.player == null ? float.MaxValue : Vector2.Distance(Center, Game1.player.Tile);

	public bool Includes(NPC npc) => _members.Contains(npc);

	public void AddMember(NPC npc)
	{
		if (_phase == Phase.Closed || _members.Contains(npc))
		{
			return;
		}
		_members.Add(npc);
		npc.Halt();
		Pin();
		Face(_current?.Npc);
	}

	public void ShowLines(IReadOnlyList<GroupLine> lines, int repliesLeft)
	{
		if (_phase == Phase.Closed)
		{
			return;
		}
		RepliesLeft = Math.Max(0, repliesLeft);
		foreach (GroupLine line in lines)
		{
			NPC? npc = _members.FirstOrDefault(n => string.Equals(n.Name, line.Speaker, StringComparison.OrdinalIgnoreCase));
			string text = line.Text.Trim();
			if (npc == null || text.Length == 0)
			{
				continue;
			}
			AddLog(new LogLine(_display(npc.Name), text, false));
			foreach (string part in GroupRules.SplitBubbles(text, Cjk ? 28 : 80))
			{
				_queue.Enqueue(new Bubble { Npc = npc, Text = part, Duration = GroupRules.ReadingMs(part, Cjk) });
			}
		}
		_phase = Phase.Speaking;
		_roundReported = false;
	}

	public void ShowThinking()
	{
		if (_phase is not Phase.Closed and not Phase.Speaking)
		{
			_phase = Phase.Waiting;
		}
	}

	public void Close()
	{
		if (_phase == Phase.Closed)
		{
			return;
		}
		_phase = Phase.Closed;
		_queue.Clear();
		_current = null;
		GroupInputBar? input = _input;
		_input = null;
		input?.CloseFromOwner();
		foreach (NPC npc in _members)
		{
			npc.movementPause = 0;
		}
	}

	/// <summary>弹出输入栏。在等回复、没回合了、或别的窗口开着时不行。</summary>
	public bool RequestInput()
	{
		if (_phase is Phase.Closed or Phase.Waiting || _input != null || RepliesLeft <= 0)
		{
			return false;
		}
		if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp)
		{
			return false;
		}
		_input = new GroupInputBar(this, Send, CancelInput);
		Game1.activeClickableMenu = _input;
		return true;
	}

	public void Update(double elapsedMs)
	{
		if (_phase == Phase.Closed)
		{
			return;
		}
		if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation != _location || Game1.eventUp || _location.currentEvent != null)
		{
			Close();
			return;
		}
		Pin();
		if (_input != null && Game1.activeClickableMenu != _input)
		{
			CancelInput();
		}
		if (Game1.activeClickableMenu != null || Game1.dialogueUp)
		{
			return;
		}
		_dotMs += elapsedMs;
		if (_dotMs >= 400)
		{
			_dotMs = 0;
			_dots = (_dots + 1) % 4;
		}
		if (_mine != null)
		{
			_mine.Elapsed += elapsedMs;
			if (_mine.Elapsed >= _mine.Duration && _phase != Phase.Waiting)
			{
				_mine = null;
			}
		}
		if (_phase == Phase.Speaking)
		{
			TickSpeaking(elapsedMs);
		}
		else if (_phase == Phase.Idle)
		{
			TickIdle(elapsedMs);
		}
	}

	private void TickSpeaking(double elapsedMs)
	{
		if (_current == null)
		{
			if (_gapMs > 0)
			{
				_gapMs -= elapsedMs;
				return;
			}
			if (_queue.Count == 0)
			{
				_phase = Phase.Idle;
				_idleMs = 0;
				_lingerMs = 0;
				return;
			}
			_current = _queue.Dequeue();
			_mine = null;
			Face(_current.Npc);
			return;
		}
		_current.Elapsed += elapsedMs;
		if (_current.Elapsed >= _current.Duration)
		{
			_last = _current;
			_current = null;
			_gapMs = 220;
		}
	}

	private void TickIdle(double elapsedMs)
	{
		_lingerMs += elapsedMs;
		if (_last != null && _lingerMs >= LingerMs)
		{
			_last = null;
		}
		if (_roundReported)
		{
			return;
		}
		_idleMs += elapsedMs;
		if (_idleMs < 900)
		{
			return;
		}
		_roundReported = true;
		if (PromptAfterRound)
		{
			PromptAfterRound = false;
			if (RequestInput())
			{
				_reportAfterInput = true;
				return;
			}
		}
		RoundFinished?.Invoke();
	}

	private void Send(string text)
	{
		_input = null;
		if (_phase == Phase.Closed)
		{
			return;
		}
		string clean = text.Trim();
		_queue.Clear();
		_current = null;
		_last = null;
		AddLog(new LogLine(Game1.player.Name, clean, true));
		string first = GroupRules.SplitBubbles(clean.Replace('\n', ' '), Cjk ? 40 : 110).FirstOrDefault() ?? clean;
		_mine = new Bubble { Text = first, Duration = GroupRules.ReadingMs(clean, Cjk) };
		PlayerJoined = true;
		_phase = Phase.Waiting;
		_roundReported = true;
		_reportAfterInput = false;
		Face(null);
		Game1.playSound("coin");
		Submitted?.Invoke(clean);
	}

	private void CancelInput()
	{
		_input = null;
		if (_phase == Phase.Closed)
		{
			return;
		}
		InputCancelled?.Invoke();
		if (_reportAfterInput)
		{
			_reportAfterInput = false;
			RoundFinished?.Invoke();
		}
	}

	private void Pin()
	{
		foreach (NPC npc in _members)
		{
			if (npc.currentLocation == _location)
			{
				npc.movementPause = Math.Max(npc.movementPause, 400);
			}
		}
	}

	/// <summary>说话的人看着大家（你插过话且离得近就看着你），其他人看着说话的人；null 表示你在说。</summary>
	private void Face(NPC? speaker)
	{
		Vector2 player = Game1.player.getStandingPosition();
		foreach (NPC npc in _members)
		{
			if (npc.currentLocation != _location || npc.Sprite?.CurrentAnimation != null)
			{
				continue;
			}
			Vector2 target;
			if (speaker == null)
			{
				target = player;
			}
			else if (npc != speaker)
			{
				target = speaker.getStandingPosition();
			}
			else if (PlayerJoined && Vector2.Distance(npc.Tile, Game1.player.Tile) <= 5f)
			{
				target = player;
			}
			else
			{
				List<NPC> others = _members.Where(n => n != npc).ToList();
				if (others.Count == 0)
				{
					continue;
				}
				target = new Vector2(others.Average(n => n.getStandingPosition().X), others.Average(n => n.getStandingPosition().Y));
			}
			npc.faceGeneralDirection(target, 0, opposite: false, useTileCalculations: false);
		}
	}

	private void AddLog(LogLine line)
	{
		_log.Add(line);
		if (_log.Count > MaxLog)
		{
			_log.RemoveAt(0);
		}
	}

	internal static bool Cjk => LocalizedContentManager.CurrentLanguageCode is LocalizedContentManager.LanguageCode.zh or LocalizedContentManager.LanguageCode.ja or LocalizedContentManager.LanguageCode.ko;

	internal string MemberNames() => string.Join(Cjk ? "、" : ", ", _members.Select(n => _display(n.Name)));

	/// <summary>头顶气泡和插话提示（UI 坐标）。没有菜单时由 HUD 事件画；输入栏开着时由输入栏先画。</summary>
	public void Draw(SpriteBatch b)
	{
		if (_phase == Phase.Closed)
		{
			return;
		}
		Bubble? said = _phase switch
		{
			Phase.Speaking => _current,
			Phase.Idle => _last,
			_ => null
		};
		if (said?.Npc != null)
		{
			float alpha = _phase == Phase.Speaking ? Fade(said) : (float)Math.Clamp((LingerMs - _lingerMs) / 300.0, 0, 1);
			HeadBubbles.DrawBubble(b, HeadBubbles.Head(said.Npc), _display(said.Npc.Name), said.Text, mine: false, alpha);
		}
		Rectangle mine = _mine == null ? Rectangle.Empty : HeadBubbles.DrawBubble(b, HeadBubbles.Head(null), "", _mine.Text, mine: true, 1f);
		if (_phase == Phase.Waiting && _members.Count > 0)
		{
			DrawDots(b, HeadBubbles.Head(_members[0]), mine);
		}
		if (_input == null && Game1.activeClickableMenu == null && PlayerDistance <= GroupRules.HintTiles && _phase != Phase.Waiting && RepliesLeft > 0)
		{
			DrawHint(b);
		}
	}

	private void DrawHint(SpriteBatch b)
	{
		string key = _keyLabel();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}
		string text = PlayerJoined
			? Tr.Get("group.hintJoined", "继续和{{names}}聊", ("names", MemberNames()))
			: Tr.Get("group.hintListen", "插话 · {{names}}在聊天", ("names", MemberNames()));
		const float scale = 0.8f;
		Vector2 ks = Game1.smallFont.MeasureString(key) * scale;
		Vector2 ts = Game1.smallFont.MeasureString(text) * scale;
		int keyW = (int)ks.X + 20;
		int w = keyW + 12 + (int)ts.X + 24;
		Rectangle r = new Rectangle(Game1.uiViewport.Width / 2 - w / 2, Game1.uiViewport.Height - 168, w, 40);
		AisvTheme.DrawShadow(b, r, 20, 3);
		AisvTheme.Bordered(b, r, AisvTheme.Card * 0.95f, AisvTheme.CardBorder, 20);
		Rectangle k = new Rectangle(r.X + 8, r.Y + 6, keyW, r.Height - 12);
		AisvTheme.FillRounded(b, k, AisvTheme.Accent, 8);
		AisvTheme.DrawText(b, key, new Vector2(k.Center.X - ks.X / 2f, k.Center.Y - ks.Y / 2f + 2f), Color.White, scale);
		AisvTheme.DrawText(b, text, new Vector2(k.Right + 12, r.Center.Y - ts.Y / 2f + 2f), AisvTheme.TextMuted, scale);
	}

	private static float Fade(Bubble bubble) => (float)Math.Clamp(Math.Min(bubble.Elapsed / 150.0, (bubble.Duration - bubble.Elapsed) / 250.0), 0, 1);

	private void DrawDots(SpriteBatch b, Vector2 anchor, Rectangle avoid)
	{
		const int w = 84, h = 40;
		int x = Math.Clamp((int)anchor.X - w / 2, 8, Math.Max(8, Game1.uiViewport.Width - w - 8));
		Rectangle r = new Rectangle(x, Math.Max(8, (int)anchor.Y - h - 14), w, h);
		if (!avoid.IsEmpty && r.Intersects(AisvTheme.Inflate(avoid, 6)))
		{
			r.Y = Math.Max(8, avoid.Y - h - 10);
		}
		AisvTheme.DrawShadow(b, r, 12, 4);
		AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 12);
		for (int i = 0; i < 3; i++)
		{
			AisvTheme.FillRounded(b, new Rectangle(r.X + 18 + i * 18, r.Center.Y - 5, 10, 10), i < _dots ? AisvTheme.Accent : AisvTheme.CardBorder, 5);
		}
	}
}
