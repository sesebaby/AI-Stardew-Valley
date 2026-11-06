using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Messenger;
using AISV.Core.Text;
using AISV.Game.Messenger;
using AISV.Game.Npcs;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>屏幕下方滑出的小手机：点一下进那条聊天，右键收起。菜单和过场时先攒着。</summary>
public sealed class PhonePeek
{
	public const string ResidentsThread = "residents-group";

	private const int Width = 216;

	private const int Height = 176;

	private static readonly Color Body = new Color(58, 50, 66);

	private readonly MessagePeek _peek = new MessagePeek();

	private readonly MessengerService _messenger;

	private readonly AISV.Game.Group.ResidentsService _residents;

	private string? _lastThread;

	private readonly NpcDirectory _npcs;

	private readonly Action<string?> _open;

	private IInputHelper? _input;

	public PhonePeek(MessengerService messenger, AISV.Game.Group.ResidentsService residents, NpcDirectory npcs, Action<string?> open)
	{
		_messenger = messenger;
		_npcs = npcs;
		_open = open;
		_residents = residents;
		messenger.ShowArrival = (npc, text) => Post(new PeekNote(npc, MessengerRules.Preview(text, 60), npc));
		residents.ShowArrival = (speaker, text) => Post(new PeekNote(ResidentsThread, MessengerRules.Preview(text, 60), speaker));
	}

	public void Attach(IModHelper helper)
	{
		_input = helper.Input;
		helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
		helper.Events.Display.RenderedHud += OnRenderedHud;
		helper.Events.Input.ButtonPressed += OnButtonPressed;
		helper.Events.GameLoop.ReturnedToTitle += (_, _) => _peek.Clear();
		helper.Events.GameLoop.SaveLoaded += (_, _) => _peek.Clear();
	}

	private void Post(PeekNote note)
	{
		_lastThread = note.Thread;
		_peek.Post(note);
	}

	private bool IsUnread(string thread) => thread == ResidentsThread ? _residents.Unread > 0 : _messenger.Unread.Contains(thread);

	private int UnreadChats => _messenger.Unread.Count + (_residents.Unread > 0 ? 1 : 0);

	private static bool CanShow => Context.IsWorldReady && Game1.activeClickableMenu == null && !Game1.eventUp && !Game1.dialogueUp && Game1.displayHUD;

	private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
	{
		if (!CanShow)
		{
			return;
		}
		if (_peek.Update(Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16, IsUnread, UnreadChats > 0))
		{
			Game1.playSound("crystal");
		}
	}

	private Rectangle Bounds() => new Rectangle(16, Game1.uiViewport.Height - (int)(Height * _peek.Shown), Width, Height);

	private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
	{
		if (!CanShow || !_peek.IsVisible || e.Button is not (SButton.MouseLeft or SButton.MouseRight) || !Bounds().Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			return;
		}
		_input?.Suppress(e.Button);
		if (e.Button == SButton.MouseRight)
		{
			_peek.Dismiss();
			return;
		}
		OpenCurrent();
	}

	/// <summary>进正在提示的那条聊天。</summary>
	public void OpenCurrent()
	{
		string? thread = _peek.Current?.Thread ?? (_lastThread != null && IsUnread(_lastThread) ? _lastThread : _messenger.LatestUnread ?? (_residents.Unread > 0 ? ResidentsThread : null));
		_peek.Dismiss();
		Game1.playSound("smallSelect");
		_open(thread);
	}

	public void Dismiss() => _peek.Dismiss();

	public string DebugState => $"{_peek.Phase} {_peek.Current?.Thread}";

	private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
	{
		if (!CanShow || !_peek.IsVisible)
		{
			return;
		}
		SpriteBatch b = e.SpriteBatch;
		Rectangle r = Bounds();
		bool hover = r.Contains(Game1.getMouseX(), Game1.getMouseY());
		AisvTheme.DrawShadow(b, r, 18);
		AisvTheme.FillRounded(b, r, hover ? new Color(76, 66, 86) : Body, 18);
		AisvTheme.FillRounded(b, new Rectangle(r.Center.X - 22, r.Y + 8, 44, 6), Color.Black * 0.45f, 3);
		Rectangle screen = new Rectangle(r.X + 10, r.Y + 20, r.Width - 20, r.Height - 30);
		AisvTheme.FillRounded(b, screen, AisvTheme.Page, 10);
		if (_peek.Current is PeekNote note)
		{
			DrawBanner(b, screen, note);
		}
		else
		{
			string count = Tr.Get("peek.unread", "{{n}} 个聊天有新消息", ("n", UnreadChats));
			AisvTheme.DrawText(b, AisvTheme.Truncate(count, screen.Width - 16, 0.68f), new Vector2(screen.X + 10, screen.Y + 4), AisvTheme.AccentDark, 0.68f);
		}
		if (UnreadChats > 0)
		{
			Rectangle dot = new Rectangle(r.Right - 18, r.Y - 6, 22, 22);
			AisvTheme.FillRounded(b, dot, AisvTheme.Badge, 11);
			AisvTheme.TextCentered(b, Math.Min(UnreadChats, 9).ToString(), dot, Color.White, 0.62f);
		}
	}

	private void DrawBanner(SpriteBatch b, Rectangle screen, PeekNote note)
	{
		string name = _npcs.Names.Display(note.Speaker);
		AisvTheme.Avatar(b, new Rectangle(screen.X + 8, screen.Y + 8, 44, 44), ShellText.Npc(note.Speaker), name.Length > 0 ? name[..1] : "?");
		int tx = screen.X + 60, w = screen.Right - 8 - tx;
		AisvTheme.Bold(b, AisvTheme.Truncate(name, w, 0.82f), new Vector2(tx, screen.Y + 6), AisvTheme.Text, 0.82f);
		AisvTheme.DrawText(b, note.Thread == ResidentsThread ? Tr.Get("peek.residents", "居民群") : Tr.Get("peek.app", "信使"), new Vector2(tx, screen.Y + 32), AisvTheme.TextFaint, 0.62f);
		int y = screen.Y + 58;
		List<string> lines = AisvTheme.WrapLines(ShellText.Clean(note.Text), screen.Width - 16, 0.72f);
		for (int i = 0; i < lines.Count && i < 2; i++)
		{
			string line = i == 1 && lines.Count > 2 ? AisvTheme.Truncate(lines[i] + "……", screen.Width - 16, 0.72f) : lines[i];
			AisvTheme.DrawText(b, line, new Vector2(screen.X + 8, y), AisvTheme.Text, 0.72f);
			y += AisvTheme.LineHeight(0.72f);
		}
	}
}
