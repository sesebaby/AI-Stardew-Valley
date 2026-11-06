using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Messenger;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.Game.Messenger;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>
/// 信使：左边搜索框和联系人（聊天 / 联系人两栏，生日的人在前，没手机的灰着），右边和选中的人的聊天；
/// 底部输入框、"别说出去"开关和发送（回车发送）。照片点开放大。
/// </summary>
internal sealed class MessengerPage : ShellPage
{
	private sealed record Row(Contact Contact, int? LastDay, string Preview);

	private const int ListWidth = 320;

	private const int RowHeight = 76;

	private const int AvatarSize = 48;

	private static string? _selected;

	private static bool _chatsTab = true;

	private static bool _tabChosen;

	private static readonly Color ChatBack = new Color(241, 237, 222);

	private static readonly Color Mine = new Color(214, 236, 190);

	private static readonly Color MineEdge = new Color(139, 186, 102);

	private static readonly Dictionary<string, string> Drafts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static int _drawnTick = -100;

	private static bool _preselected;

	private readonly TextArea _box = new TextArea(MessengerRules.MaxMessageChars) { Placeholder = Tr.Get("messenger.placeholder", "写条消息……回车发送") };

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _chatScroll = new Scroller();

	private List<Row> _rows = new List<Row>();

	private List<Row> _tabRows = new List<Row>();

	private readonly TextBox _search = new TextBox(null, null, Game1.smallFont, AisvTheme.Text) { textLimit = 20, limitWidth = false };

	private string _query = "";

	private IReadOnlyList<string> _tokens = Array.Empty<string>();

	private Rectangle _searchBox;

	private IReadOnlyList<ChatItem> _items = Array.Empty<ChatItem>();

	private int _seenVersion = -1;

	private string? _loadedFor;

	private int _npcItems = -1;

	private int _revealIndex = -1;

	private int _offerVersion = -1;

	private double _revealMs;

	private bool _stick = true;

	private bool _keepSecret;

	private string _status = "";

	private string? _zoomPhoto;

	private Rectangle _listArea;

	private bool _opening = true;

	private bool _backspaceHeld;

	private double _backspaceMs;

	public MessengerPage(ShellContext ctx) : base(ctx)
	{
	}

	/// <summary>外壳开着、正在看的聊天对象。</summary>
	public static string? ViewingNpc => Game1.ticks - _drawnTick <= 2 && Game1.activeClickableMenu is ShellMenu ? _selected : null;

	public static void Preselect(string npc)
	{
		_selected = npc;
		_chatsTab = false;
		_tabChosen = true;
		_preselected = true;
	}

	private MessengerService? Service => Ctx.Messenger;

	public override bool CapturesKeys => _box.Selected || _search.Selected;

	public override void Reload()
	{
		MessengerService? service = Service;
		if (service == null)
		{
			_rows.Clear();
			return;
		}
		IReadOnlyList<Contact> contacts = service.Contacts();
		List<Row> rows = new List<Row>();
		foreach (Contact c in contacts)
		{
			IReadOnlyList<TextEntry> history = c.NoPhone ? Array.Empty<TextEntry>() : service.History(c.NpcName);
			TextEntry? last = history.LastOrDefault(e => MessengerRules.IsOwn(e, Game1.player?.Name));
			string preview = last == null ? "" : MessengerRules.CleanReply(last.NpcReply).Length > 0 ? MessengerRules.CleanReply(last.NpcReply) : MessengerRules.Normalize(last.PlayerMessage);
			rows.Add(new Row(c, last == null ? null : new GameDate(last.Day, last.Season, last.Year).TotalDays, preview));
		}
		if (!_tabChosen)
		{
			_chatsTab = rows.Any(r => r.LastDay != null);
			_tabChosen = true;
		}
		List<int> order = MessengerRules.TabMembers(rows.Select(r => r.LastDay).ToList(), _chatsTab, rows.Select(r => r.Contact.Birthday).ToList());
		_tabRows = order.Select(i => rows[i]).ToList();
		_rows = Filtered();
		Row? unread = _rows.FirstOrDefault(r => service.Unread.Contains(r.Contact.NpcName));
		if (_opening && unread != null && !_preselected)
		{
			_selected = unread.Contact.NpcName;
		}
		_opening = false;
		_preselected = false;
		if (_selected == null || !_rows.Any(r => Same(r.Contact.NpcName, _selected)))
		{
			_selected = (unread ?? _rows.FirstOrDefault(r => !r.Contact.NoPhone))?.Contact.NpcName;
		}
		LoadChat(force: true);
	}

	private List<Row> Filtered() => _tokens.Count == 0 ? _tabRows : _tabRows.Where(r => MessengerRules.MatchesSearch(_tokens, r.Contact.DisplayName, r.Contact.NpcName, r.Preview)).ToList();

	private void ApplySearch()
	{
		_query = (_search.Text ?? "").Trim();
		_tokens = MessengerRules.SearchTokens(_query);
		_rows = Filtered();
		_listScroll.Reset();
	}

	public override void Closed()
	{
		KeepDraft();
		_box.Blur();
		_search.Selected = false;
		_zoomPhoto = null;
	}

	public override bool KeyPress(Keys key)
	{
		if (_zoomPhoto != null && (key == Keys.Escape || key == Keys.Enter))
		{
			_zoomPhoto = null;
			return true;
		}
		if (_search.Selected)
		{
			if (key == Keys.Enter && _rows.FirstOrDefault(r => !r.Contact.NoPhone) is Row first)
			{
				_search.Selected = false;
				Select(first.Contact.NpcName);
				_box.Focus();
				return true;
			}
			if (key == Keys.Escape)
			{
				if ((_search.Text ?? "").Length > 0)
				{
					_search.Text = "";
					ApplySearch();
				}
				else
				{
					_search.Selected = false;
				}
				return true;
			}
			return false;
		}
		if (!_box.Selected)
		{
			return _chatScroll.PageKeys(key, 300);
		}
		switch (key)
		{
		case Keys.Enter:
		{
			KeyboardState s = Keyboard.GetState();
			if (s.IsKeyDown(Keys.LeftShift) || s.IsKeyDown(Keys.RightShift))
			{
				_box.NewLine();
			}
			else
			{
				Send();
			}
			return true;
		}
		case Keys.Back:
			_box.DeleteChar();
			_backspaceHeld = true;
			_backspaceMs = 0;
			return true;
		case Keys.Escape:
			_box.Blur();
			return true;
		default:
			return false;
		}
	}

	public override void Scroll(int direction)
	{
		Point m = new Point(Game1.getMouseX(), Game1.getMouseY());
		if (_listArea.Contains(m))
		{
			_listScroll.Wheel(direction);
			return;
		}
		_chatScroll.Wheel(direction);
		_stick = _chatScroll.Offset >= _chatScroll.Max;
	}

	private void Select(string npc)
	{
		if (Same(npc, _selected))
		{
			return;
		}
		Game1.playSound("smallSelect");
		KeepDraft();
		_selected = npc;
		_status = "";
		_stick = true;
		LoadChat(force: true);
	}

	private static string DraftKey(string npc) => $"{Game1.uniqueIDForThisGame}:{npc}";

	private void KeepDraft()
	{
		if (_selected != null)
		{
			Drafts[DraftKey(_selected)] = _box.Text;
		}
	}

	private void SwitchTab(bool chats)
	{
		if (_chatsTab == chats)
		{
			return;
		}
		Game1.playSound("smallSelect");
		_chatsTab = chats;
		_tabChosen = true;
		_listScroll.Reset();
		Reload();
	}

	private void LoadChat(bool force)
	{
		MessengerService? service = Service;
		if (service == null || _selected == null)
		{
			_items = Array.Empty<ChatItem>();
			_loadedFor = _selected;
			return;
		}
		bool switched = !Same(_loadedFor, _selected);
		if (!force && !switched)
		{
			return;
		}
		IReadOnlyList<TextEntry> history = service.History(_selected);
		List<ChatItem> items = MessengerRules.Transcript(history, Game1.player?.Name, service.Photos.From(_selected)).ToList();
		if (service.PendingMessage(_selected) is string waiting)
		{
			GameDate today = new GameDate(Game1.dayOfMonth, Game1.currentSeason, Game1.year);
			ChatItem? last = items.LastOrDefault();
			if (last == null || !today.SameDay(last.Day, last.Season, last.Year))
			{
				items.Add(new ChatItem(ChatItemKind.Date, "", today.Day, today.Season, today.Year));
			}
			items.Add(new ChatItem(ChatItemKind.Player, waiting, today.Day, today.Season, today.Year));
		}
		_items = items;
		int npcItems = history.Count(e => MessengerRules.IsOwn(e, Game1.player?.Name) && MessengerRules.CleanReply(e.NpcReply).Length > 0);
		if (!switched && _npcItems >= 0 && npcItems > _npcItems)
		{
			_revealIndex = LastIndex(ChatItemKind.Npc);
			_revealMs = 0;
			_stick = true;
		}
		else if (switched)
		{
			_revealIndex = -1;
			_stick = true;
			_box.Text = Drafts.TryGetValue(DraftKey(_selected), out string? draft) ? draft : "";
		}
		_npcItems = npcItems;
		_loadedFor = _selected;
		service.MarkRead(_selected);
	}

	private int LastIndex(ChatItemKind kind)
	{
		for (int i = _items.Count - 1; i >= 0; i--)
		{
			if (_items[i].Kind == kind)
			{
				return i;
			}
		}
		return -1;
	}

	private void Send()
	{
		MessengerService? service = Service;
		if (service == null || _selected == null)
		{
			return;
		}
		SendBlock block = service.Send(_selected, _box.Text, _keepSecret);
		if (block == SendBlock.None)
		{
			_box.Text = "";
			_status = "";
			_stick = true;
			Game1.playSound("coin");
			LoadChat(force: true);
			return;
		}
		_status = BlockText(block, service);
		Game1.playSound("cancel");
	}

	private static string BlockText(SendBlock block, MessengerService service) => block switch
	{
		SendBlock.Empty => Tr.Get("messenger.empty", "先写点什么"),
		SendBlock.TooLong => Tr.Get("messenger.tooLong", "太长了，最多 {{max}} 个字", ("max", MessengerRules.MaxMessageChars)),
		SendBlock.DailyLimitReached => Tr.Get("messenger.limit", "今天给这位发了 {{count}} 条，明天再聊", ("count", service.DailyLimit)),
		SendBlock.Busy => Tr.Get("messenger.busy", "等对方回完再发"),
		SendBlock.AiOff => Tr.Get("messenger.aiOff", "AI 现在用不了"),
		SendBlock.NoContact => Tr.Get("messenger.noContact", "联系不上这位"),
		_ => ""
	};

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_drawnTick = Game1.ticks;
		Tick(Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 16.7);
		MessengerService? service = Service;
		if (service == null)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + 80, area.Width, 80), Tr.Get("messenger.unavailable", "信使还没准备好"));
			return;
		}
		Rectangle list = new Rectangle(area.X, area.Y, Math.Min(ListWidth, area.Width / 3), area.Height);
		AisvTheme.Fill(b, list, AisvTheme.PageDeep);
		AisvTheme.VLine(b, list.Right, list.Y, list.Height, AisvTheme.Divider);
		DrawList(b, list, hits, service);
		Rectangle chat = new Rectangle(list.Right + 2, area.Y, area.Right - list.Right - 2, area.Height);
		Row? row = _rows.FirstOrDefault(r => Same(r.Contact.NpcName, _selected));
		if (row == null)
		{
			Kit.Empty(b, new Rectangle(chat.X, chat.Y + 120, chat.Width, 80), Tr.Get("messenger.pick", "从左边挑个人说话吧"), Tr.Get("messenger.pickHint", "认识的村民都在左边"));
		}
		else if (row.Contact.NoPhone)
		{
			_box.Blur();
			DrawChatHeader(b, new Rectangle(chat.X, chat.Y, chat.Width, 76), row.Contact, service);
			Kit.Empty(b, new Rectangle(chat.X, chat.Y + 180, chat.Width, 80), NoPhoneText(row.Contact), Tr.Get("messenger.noPhoneHint", "想说话就当面去找吧"));
		}
		else
		{
			Rectangle header = new Rectangle(chat.X, chat.Y, chat.Width, 76);
			DrawChatHeader(b, header, row.Contact, service);
			int composeH = 40 + AisvTheme.LineHeight() * 2 + 40;
			Rectangle compose = new Rectangle(chat.X, chat.Bottom - composeH, chat.Width, composeH);
			DrawMessages(b, new Rectangle(chat.X + 12, header.Bottom + 4, chat.Width - 24, compose.Y - header.Bottom - 8), hits, service, row.Contact);
			DrawCompose(b, compose, hits, service, row.Contact);
		}
		if (_zoomPhoto != null)
		{
			DrawZoom(b, area, hits, service);
		}
	}

	private void Tick(double ms)
	{
		if ((_search.Text ?? "").Trim() != _query)
		{
			ApplySearch();
		}
		if (_box.Selected && !_search.Selected && Game1.keyboardDispatcher.Subscriber == null)
		{
			_box.Focus();
		}
		if (_backspaceHeld && Keyboard.GetState().IsKeyDown(Keys.Back) && _box.Selected)
		{
			_backspaceMs += ms;
			if (_backspaceMs >= 400)
			{
				_backspaceMs -= 50;
				_box.DeleteChar();
			}
		}
		else
		{
			_backspaceHeld = false;
		}
		_revealMs += ms;
		if (Service is MessengerService s && s.Version != _seenVersion)
		{
			_seenVersion = s.Version;
			LoadChat(force: true);
			if (s.LastFailure is { } failed && Same(failed.Npc, _selected))
			{
				s.LastFailure = null;
				_status = Tr.Get("messenger.failed", "对方没收到，再发一次试试");
				if (_box.Text.Length == 0)
				{
					_box.Text = failed.Message;
				}
			}
		}
	}

	private void DrawList(SpriteBatch b, Rectangle r, Hits hits, MessengerService service)
	{
		_searchBox = new Rectangle(r.X + 12, r.Y + 12, r.Width - 24, 42);
		DrawSearch(b, hits);
		Rectangle tabs = new Rectangle(r.X + 12, _searchBox.Bottom + 10, r.Width - 24, 40);
		AisvTheme.FillRounded(b, tabs, AisvTheme.Divider * 0.6f, 20);
		Rectangle chats = new Rectangle(tabs.X + 3, tabs.Y + 3, tabs.Width / 2 - 3, tabs.Height - 6);
		Rectangle all = new Rectangle(tabs.Center.X, tabs.Y + 3, tabs.Width / 2 - 3, tabs.Height - 6);
		DrawSegment(b, chats, Tr.Get("messenger.tab.chats", "聊天"), _chatsTab);
		DrawSegment(b, all, Tr.Get("messenger.tab.contacts", "联系人"), !_chatsTab);
		hits.Add(chats, () => SwitchTab(true));
		hits.Add(all, () => SwitchTab(false));
		_listArea = new Rectangle(r.X, tabs.Bottom + 8, r.Width, r.Bottom - tabs.Bottom - 8);
		if (_rows.Count == 0)
		{
			string empty = _tokens.Count > 0 ? Tr.Get("messenger.noMatch", "没有找到“{{query}}”", ("query", _query))
				: _chatsTab ? Tr.Get("messenger.noChats", "还没和谁聊过") : Tr.Get("messenger.noContacts", "还没认识谁");
			AisvTheme.TextCentered(b, empty, new Rectangle(_listArea.X, _listArea.Y + 30, _listArea.Width, 40), AisvTheme.TextFaint, 0.8f);
			return;
		}
		AisvTheme.BeginClip(b, _listArea);
		int y = _listArea.Y - _listScroll.Offset;
		foreach (Row row in _rows)
		{
			Rectangle rr = new Rectangle(_listArea.X + 8, y, _listArea.Width - 24, RowHeight - 6);
			if (rr.Bottom >= _listArea.Y && rr.Y <= _listArea.Bottom)
			{
				DrawRow(b, rr, row, service);
				string npc = row.Contact.NpcName;
				hits.Add(rr, () => Select(npc), _listArea);
			}
			y += RowHeight;
		}
		AisvTheme.EndClip(b);
		_listScroll.Finish(b, _listArea, y, 10);
	}

	private void DrawSearch(SpriteBatch b, Hits hits)
	{
		AisvTheme.Field(b, _searchBox, _search.Selected);
		AisvTheme.Magnifier(b, new Vector2(_searchBox.X + 20, _searchBox.Center.Y));
		string text = _search.Text ?? "";
		Vector2 tp = new Vector2(_searchBox.X + 40, _searchBox.Center.Y - AisvTheme.LineHeight(0.8f) / 2);
		if (text.Length == 0 && !_search.Selected)
		{
			AisvTheme.DrawText(b, Tr.Get("messenger.search", "搜索村民或聊天内容"), tp, AisvTheme.TextFaint, 0.8f);
		}
		else
		{
			string shown = AisvTheme.Truncate(text, _searchBox.Width - 84, 0.8f);
			AisvTheme.DrawText(b, shown, tp, AisvTheme.Text, 0.8f);
			if (_search.Selected && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 550)
			{
				AisvTheme.Fill(b, new Rectangle((int)tp.X + (int)AisvTheme.Width(shown, 0.8f) + 2, _searchBox.Center.Y - 11, 2, 22), AisvTheme.Accent);
			}
		}
		hits.Add(_searchBox, () =>
		{
			_box.Blur();
			_search.Selected = true;
		});
		if (text.Length > 0)
		{
			Rectangle clear = new Rectangle(_searchBox.Right - 36, _searchBox.Center.Y - 14, 28, 28);
			AisvTheme.FillRounded(b, clear, AisvTheme.Hover(clear) ? AisvTheme.Divider : AisvTheme.PageDeep, 14);
			AisvTheme.TextCentered(b, "×", clear, AisvTheme.TextMuted, 0.8f);
			hits.Add(clear, () =>
			{
				Game1.playSound("smallSelect");
				_search.Text = "";
				ApplySearch();
			});
		}
	}

	/// <summary>搜索词在这一行里的位置画成浅绿底和下划线。</summary>
	private void Highlight(SpriteBatch b, string line, Vector2 pos, float scale)
	{
		if (_tokens.Count == 0)
		{
			return;
		}
		int lh = AisvTheme.LineHeight(scale);
		foreach ((int start, int length) in MessengerRules.SearchSpans(line, _tokens))
		{
			if (start >= line.Length)
			{
				continue;
			}
			int x = (int)(pos.X + AisvTheme.Width(line.Substring(0, start), scale));
			int w = (int)Math.Ceiling(AisvTheme.Width(line.Substring(start, Math.Min(length, line.Length - start)), scale));
			AisvTheme.Fill(b, new Rectangle(x - 1, (int)pos.Y, w + 2, lh), AisvTheme.AccentSoft);
			AisvTheme.Fill(b, new Rectangle(x - 1, (int)pos.Y + lh - 2, w + 2, 2), AisvTheme.Accent);
		}
	}

	private static void DrawSegment(SpriteBatch b, Rectangle r, string label, bool selected)
	{
		if (selected)
		{
			AisvTheme.FillRounded(b, r, AisvTheme.Card, r.Height / 2);
		}
		else if (AisvTheme.Hover(r))
		{
			AisvTheme.FillRounded(b, r, AisvTheme.Card * 0.5f, r.Height / 2);
		}
		AisvTheme.TextCentered(b, label, r, selected ? AisvTheme.AccentDark : AisvTheme.TextMuted, 0.8f);
	}

	private void DrawRow(SpriteBatch b, Rectangle r, Row row, MessengerService service)
	{
		Contact c = row.Contact;
		bool selected = Same(c.NpcName, _selected);
		if (selected)
		{
			AisvTheme.FillRounded(b, r, AisvTheme.SelectedRow, 10);
			AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 12, 4, r.Height - 24), AisvTheme.Accent, 2);
		}
		else if (AisvTheme.Hover(r, _listArea))
		{
			AisvTheme.FillRounded(b, r, AisvTheme.Card * 0.7f, 10);
		}
		else if (c.Birthday)
		{
			AisvTheme.Bordered(b, r, AisvTheme.PinkSoft * 0.6f, AisvTheme.Pink * 0.5f, 10);
		}
		Rectangle avatar = new Rectangle(r.X + 10, r.Center.Y - AvatarSize / 2, AvatarSize, AvatarSize);
		AisvTheme.Avatar(b, avatar, ShellText.Npc(c.NpcName), c.DisplayName.Length > 0 ? c.DisplayName.Substring(0, 1) : "?");
		if (c.NoPhone)
		{
			AisvTheme.Fill(b, avatar, AisvTheme.PageDeep * 0.55f);
		}
		int tx = avatar.Right + 12;
		int right = r.Right - 10;
		if (c.Hearts > 0 && !c.NoPhone)
		{
			string hearts = c.Hearts.ToString();
			float hw = AisvTheme.Width(hearts, 0.66f);
			AisvTheme.DrawText(b, hearts, new Vector2(right - hw, r.Y + 13), AisvTheme.TextFaint, 0.66f);
			AisvTheme.Heart(b, new Rectangle((int)(right - hw - 18), r.Y + 15, 14, 12));
			right = (int)(right - hw - 26);
		}
		Color name = c.NoPhone ? AisvTheme.TextFaint : AisvTheme.Text;
		string shownName = AisvTheme.Truncate(c.DisplayName, right - tx - (c.Birthday ? 30 : 0), 0.85f);
		Vector2 np = new Vector2(tx, r.Y + 10);
		Highlight(b, shownName, np, 0.85f);
		AisvTheme.Bold(b, shownName, np, name, 0.85f);
		if (c.Birthday)
		{
			Cake(b, new Rectangle(tx + (int)AisvTheme.Width(shownName, 0.85f) + 6, r.Y + 8, 24, 24));
		}
		bool unread = service.Unread.Contains(c.NpcName) && !selected;
		int subRight = r.Right - 10 - (unread ? 26 : 0);
		string sub;
		Color subColor = AisvTheme.TextFaint;
		bool preview = false;
		if (c.NoPhone)
		{
			sub = Tr.Get("messenger.noPhoneShort", "没有手机");
		}
		else if (service.IsBusy(c.NpcName))
		{
			sub = Tr.Get("messenger.typingShort", "正在输入……");
			subColor = AisvTheme.AccentDark;
		}
		else if (c.Birthday)
		{
			sub = Tr.Get("messenger.birthday", "今天过生日");
			subColor = AisvTheme.Pink;
		}
		else
		{
			preview = row.Preview.Length > 0;
			sub = preview ? row.Preview.Replace('\n', ' ') : Tr.Get("messenger.noPreview", "还没聊过");
		}
		string shownSub = AisvTheme.Truncate(sub, subRight - tx, 0.7f);
		Vector2 sp = new Vector2(tx, r.Y + 40);
		if (preview)
		{
			Highlight(b, shownSub, sp, 0.7f);
		}
		AisvTheme.DrawText(b, shownSub, sp, subColor, 0.7f);
		if (unread)
		{
			Rectangle dot = new Rectangle(r.Right - 26, r.Y + 41, 16, 16);
			AisvTheme.FillRounded(b, dot, AisvTheme.Badge, 8);
			AisvTheme.FillRounded(b, AisvTheme.Inflate(dot, -5), Color.White * 0.9f, 3);
		}
	}

	private static void Cake(SpriteBatch b, Rectangle r)
	{
		try
		{
			StardewValley.ItemTypeDefinitions.ParsedItemData data = ItemRegistry.GetDataOrErrorItem("(O)221");
			b.Draw(data.GetTexture(), r, data.GetSourceRect(), Color.White);
		}
		catch
		{
		}
	}

	/// <summary>气泡底角的小尾巴（我的在右，对方的在左）。</summary>
	private static void Tail(SpriteBatch b, Rectangle r, bool right, Color fill, Color edge)
	{
		int x = right ? r.Right - 2 : r.X - 6;
		int y = r.Bottom - 14;
		for (int i = 0; i < 6; i++)
		{
			int w = 8 - i;
			Rectangle line = right ? new Rectangle(x, y + i, w, 1) : new Rectangle(x + i, y + i, w, 1);
			AisvTheme.Fill(b, line, fill);
			AisvTheme.Fill(b, right ? new Rectangle(line.Right, line.Y, 2, 1) : new Rectangle(line.X - 2, line.Y, 2, 1), edge);
		}
	}

	private void DrawChatHeader(SpriteBatch b, Rectangle r, Contact c, MessengerService service)
	{
		AisvTheme.Fill(b, r, AisvTheme.Card);
		AisvTheme.HLine(b, r.X, r.Bottom - 2, r.Width, AisvTheme.Divider);
		Rectangle avatar = new Rectangle(r.X + 20, r.Y + 12, AvatarSize + 4, AvatarSize + 4);
		AisvTheme.Avatar(b, avatar, ShellText.Npc(c.NpcName), c.DisplayName.Length > 0 ? c.DisplayName.Substring(0, 1) : "?");
		int tx = avatar.Right + 14;
		AisvTheme.Bold(b, c.DisplayName, new Vector2(tx, r.Y + 10), AisvTheme.Text, 1.0f);
		if (c.Birthday)
		{
			AisvTheme.Chip(b, new Vector2(tx + AisvTheme.Width(c.DisplayName) + 12, r.Y + 13), Tr.Get("messenger.birthday", "今天过生日"), AisvTheme.PinkSoft, AisvTheme.Pink, 0.66f);
		}
		bool typing = !c.NoPhone && (service.IsTyping(c.NpcName) || service.IsBusy(c.NpcName));
		if (typing)
		{
			string dots = new string('·', 1 + Game1.ticks / 20 % 3);
			AisvTheme.DrawText(b, Tr.Get("messenger.typing", "{{name}}在打字", ("name", c.DisplayName)) + " " + dots, new Vector2(tx, r.Y + 44), AisvTheme.AccentDark, 0.72f);
		}
		else
		{
			AisvTheme.Hearts(b, new Vector2(tx + 2, r.Y + 48), c.Hearts, 10, 14);
		}
		if (c.NoPhone)
		{
			return;
		}
		int limit = service.DailyLimit, left = service.Remaining(c.NpcName);
		const int dot = 10, gap = 5;
		int dotsW = limit * dot + (limit - 1) * gap;
		string label = Tr.Get("messenger.leftLabel", "今天还能发");
		float lw = AisvTheme.Width(label, 0.7f);
		int x = r.Right - 24 - dotsW;
		int cy = r.Center.Y;
		AisvTheme.DrawText(b, label, new Vector2(x - 10 - lw, cy - AisvTheme.LineHeight(0.7f) / 2), AisvTheme.TextFaint, 0.7f);
		for (int i = 0; i < limit; i++)
		{
			AisvTheme.FillRounded(b, new Rectangle(x + i * (dot + gap), cy - dot / 2, dot, dot), i < left ? AisvTheme.Accent : AisvTheme.Divider, dot / 2);
		}
	}



	private void DrawMessages(SpriteBatch b, Rectangle view, Hits hits, MessengerService service, Contact c)
	{
		int lh = AisvTheme.LineHeight();
		int bubbleMax = Math.Max(240, (int)(view.Width * 0.64f));
		AisvTheme.Fill(b, AisvTheme.Inflate(view, 12), ChatBack);
		AisvTheme.BeginClip(b, view);
		int y = view.Y + 12 - _chatScroll.Offset;
		if (_items.Count == 0 && !service.IsBusy(c.NpcName))
		{
			AisvTheme.TextCentered(b, Tr.Get("messenger.emptyThread", "还没聊过，发条消息打个招呼吧"), new Rectangle(view.X, view.Y + 40, view.Width, 40), AisvTheme.TextFaint, 0.85f);
		}
		for (int i = 0; i < _items.Count; i++)
		{
			ChatItem item = _items[i];
			switch (item.Kind)
			{
			case ChatItemKind.Date:
			{
				string label = Tr.Get("messenger.date", "第 {{year}} 年{{season}} {{day}} 日", ("year", item.Year), ("season", HistoryFormatter.SeasonName(item.Season)), ("day", item.Day));
				int cw = AisvTheme.ChipWidth(label, 0.7f);
				AisvTheme.Chip(b, new Vector2(view.Center.X - cw / 2, y), label, AisvTheme.Divider, AisvTheme.TextMuted, 0.7f);
				y += 40;
				break;
			}
			case ChatItemKind.Photo:
			{
				Texture2D? image = service.Photos.Image(item.Text);
				int w = Math.Min(bubbleMax, 320), h = w * PhotoCamera.Height / PhotoCamera.Width;
				Rectangle r = new Rectangle(view.X + AvatarSize + 16, y, w, h);
				AisvTheme.Bordered(b, AisvTheme.Inflate(r, 4), AisvTheme.Card, AisvTheme.CardBorder, 8);
				if (image != null)
				{
					b.Draw(image, r, Color.White);
					string id = item.Text;
					hits.Add(r, () =>
					{
						Game1.playSound("bigSelect");
						_zoomPhoto = id;
					}, view);
				}
				else
				{
					AisvTheme.TextCentered(b, Tr.Get("messenger.photoMissing", "照片找不到了"), r, AisvTheme.TextFaint, 0.75f);
				}
				y += h + 20;
				break;
			}
			default:
			{
				bool player = item.Kind == ChatItemKind.Player;
				string text = item.Text;
				if (!player && i == _revealIndex)
				{
					int shown = MessengerRules.RevealedChars(_revealMs, text.Length);
					if (shown >= text.Length)
					{
						_revealIndex = -1;
					}
					else
					{
						text = text.Substring(0, shown);
					}
				}
				string wrapped = AisvTheme.Wrap(text, bubbleMax - 32);
				string full = AisvTheme.Wrap(item.Text, bubbleMax - 32);
				Vector2 size = Game1.smallFont.MeasureString(full);
				int w = Math.Max(72, (int)size.X + 32);
				int h = full.Split('\n').Length * lh + 22;
				if (player)
				{
					Rectangle r = new Rectangle(view.Right - 20 - w, y, w, h);
					AisvTheme.Bordered(b, r, Mine, MineEdge, 12);
					Tail(b, r, right: true, Mine, MineEdge);
					AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 11), AisvTheme.Text);
				}
				else
				{
					int ah = AvatarSize - 8, bh = h;
					h = Math.Max(h, ah);
					Rectangle avatar = new Rectangle(view.X + 4, y + h - ah, ah, ah);
					AisvTheme.Avatar(b, avatar, ShellText.Npc(c.NpcName), c.DisplayName.Length > 0 ? c.DisplayName.Substring(0, 1) : "?");
					Rectangle r = new Rectangle(view.X + AvatarSize + 12, y + h - bh, w, bh);
					AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 12);
					Tail(b, r, right: false, AisvTheme.Card, AisvTheme.CardBorder);
					AisvTheme.DrawText(b, wrapped, new Vector2(r.X + 16, r.Y + 11), AisvTheme.Text);
				}
				y += h + 14;
				break;
			}
			}
		}
		if (!service.IsBusy(c.NpcName) && _revealIndex < 0)
		{
			string channel = OfferDesk.MessengerChannel(c.NpcName);
			int offerVersion = Ctx.Offers?.Version ?? 0;
			if (offerVersion != _offerVersion)
			{
				_offerVersion = offerVersion;
				_stick = true;
			}
			int bottom = OfferCard.Draw(b, Ctx, channel, view.X + AvatarSize + 12, y, bubbleMax, hits, view, said => _status = said);
			if (bottom > y)
			{
				y = bottom + 14;
			}
		}
		if (service.IsBusy(c.NpcName))
		{
			int ah = AvatarSize - 8;
			Rectangle avatar = new Rectangle(view.X + 4, y, ah, ah);
			AisvTheme.Avatar(b, avatar, ShellText.Npc(c.NpcName), c.DisplayName.Length > 0 ? c.DisplayName.Substring(0, 1) : "?");
			Rectangle bubble = new Rectangle(view.X + AvatarSize + 12, y + ah - 40, 76, 40);
			AisvTheme.Bordered(b, bubble, AisvTheme.Card, AisvTheme.CardBorder, 12);
			Tail(b, bubble, right: false, AisvTheme.Card, AisvTheme.CardBorder);
			double t = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0;
			for (int k = 0; k < 3; k++)
			{
				int lift = (int)Math.Round(Math.Max(0, Math.Sin(t / 160.0 - k * 0.9)) * 4);
				AisvTheme.FillRounded(b, new Rectangle(bubble.X + 16 + k * 16, bubble.Center.Y - 4 - lift, 9, 9), Color.Lerp(AisvTheme.TextFaint, AisvTheme.Accent, k / 3f), 4);
			}
			y += ah + 14;
		}
		AisvTheme.EndClip(b);
		_chatScroll.Finish(b, view, y + 8);
		if (_stick)
		{
			_chatScroll.By(int.MaxValue / 2);
		}
	}

	private void DrawCompose(SpriteBatch b, Rectangle r, Hits hits, MessengerService service, Contact c)
	{
		AisvTheme.HLine(b, r.X, r.Y, r.Width, AisvTheme.Divider);
		Rectangle toggle = new Rectangle(r.X + 20, r.Y + 8, 52, 26);
		AisvTheme.Toggle(b, toggle, _keepSecret);
		string secret = Tr.Get("messenger.keepSecret", "别说出去（这条不会传成八卦）");
		AisvTheme.DrawText(b, secret, new Vector2(toggle.Right + 10, toggle.Y + 2), _keepSecret ? AisvTheme.AccentDark : AisvTheme.TextMuted, 0.72f);
		Rectangle toggleHit = new Rectangle(toggle.X, toggle.Y - 4, toggle.Width + 14 + (int)AisvTheme.Width(secret, 0.72f), toggle.Height + 8);
		hits.Add(toggleHit, () =>
		{
			Game1.playSound("drumkit6");
			_keepSecret = !_keepSecret;
		});
		if (_status.Length > 0)
		{
			string s = AisvTheme.Truncate(_status, r.Right - 20 - toggleHit.Right - 30, 0.72f);
			Vector2 size = Game1.smallFont.MeasureString(s) * 0.72f;
			AisvTheme.DrawText(b, s, new Vector2(r.Right - 20 - size.X, r.Y + 10), AisvTheme.Warn, 0.72f);
		}
		const int bw = 112;
		Rectangle send = new Rectangle(r.Right - 20 - bw, r.Bottom - 14 - 48, bw, 48);
		_box.Bounds = new Rectangle(r.X + 20, r.Y + 42, send.X - 12 - (r.X + 20), r.Bottom - 14 - (r.Y + 42));
		_box.Draw(b);
		string count = _box.Text.Length + "/" + MessengerRules.MaxMessageChars;
		Vector2 cs = Game1.smallFont.MeasureString(count) * 0.7f;
		AisvTheme.DrawText(b, count, new Vector2(_box.Bounds.Right - cs.X - 12, _box.Bounds.Bottom - cs.Y - 6), AisvTheme.TextFaint, 0.7f);
		bool canSend = service.Check(c.NpcName, _box.Text) == SendBlock.None;
		AisvTheme.Button(b, send, Tr.Get("reply.send", "发送"), primary: true, hover: AisvTheme.Hover(send), disabled: !canSend);
		hits.Add(_box.Bounds, () => _box.Focus());
		hits.Add(send, Send);
	}

	private void DrawZoom(SpriteBatch b, Rectangle area, Hits hits, MessengerService service)
	{
		AisvTheme.Fill(b, area, Color.Black * 0.7f);
		Texture2D? image = _zoomPhoto == null ? null : service.Photos.Image(_zoomPhoto);
		PhotoRecord? record = _zoomPhoto == null ? null : service.Photos.Find(_zoomPhoto);
		if (image != null)
		{
			float scale = Math.Min((area.Width - 80f) / image.Width, (area.Height - 120f) / image.Height);
			int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
			Rectangle r = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2 - 16, w, h);
			AisvTheme.Fill(b, AisvTheme.Inflate(r, 8), Color.White);
			b.Draw(image, r, Color.White);
			if (record != null)
			{
				string caption = Tr.Get("messenger.photoCaption", "{{place}} · 第 {{year}} 年{{season}} {{day}} 日 {{time}}",
					("place", record.LocationDisplay), ("year", record.Year), ("season", HistoryFormatter.SeasonName(record.Season)), ("day", record.Day), ("time", MessengerRules.ClockText(record.Time)));
				AisvTheme.TextCentered(b, caption, new Rectangle(area.X, r.Bottom + 16, area.Width, 32), Color.White, 0.8f);
			}
		}
		hits.Add(area, () => _zoomPhoto = null);
	}

	private static string NoPhoneText(Contact c) => c.NpcName switch
	{
		"Linus" => Tr.Get("messenger.noPhone.linus", "莱纳斯住在山边的帐篷里，用不上手机。"),
		"Krobus" => Tr.Get("messenger.noPhone.krobus", "科罗布斯不用人类的手机，想找他得亲自去下水道。"),
		"Wizard" => Tr.Get("messenger.noPhone.wizard", "法师有自己的联络办法，从来不碰手机。"),
		"Leo" => Tr.Get("messenger.noPhone.leo", "里奥在姜岛的林子里长大，还没学会用手机。"),
		"Birdie" => Tr.Get("messenger.noPhone.birdie", "伯蒂在岛上过着简单的日子，没有手机。"),
		"Vincent" or "Jas" => Tr.Get("messenger.noPhone.kid", "{{name}}还小，没有自己的手机。", ("name", c.DisplayName)),
		_ => Tr.Get("messenger.noPhone.other", "{{name}}没有手机。", ("name", c.DisplayName))
	};

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
