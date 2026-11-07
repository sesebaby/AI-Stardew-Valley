using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>外壳：今日 / 社交 / 世界 / 设置。社交下面分信件、信使、村民、居民群、日记、八卦、小剧场、出游，世界下面分村民之间、市场；推进来的页面上面是"‹ 返回"。</summary>
public sealed partial class ShellMenu : IClickableMenu, IShellHost
{
	public enum Tab
	{
		Today,
		Social,
		World,
		Apps,
		Settings,
		Desk
	}

	public enum SocialTab
	{
		Letters,
		Messenger,
		Contacts,
		Residents,
		Diary,
		Gossip,
		Arcs,
		Outings,
		Partners
	}

	public enum WorldTab
	{
		Village,
		Market,
		Commissions,
		Ledger,
		Help,
		News
	}

	private static readonly Tab[] Tabs = { Tab.Today, Tab.Social, Tab.World, Tab.Apps, Tab.Settings };

	private static readonly SocialTab[] SocialTabs = { SocialTab.Letters, SocialTab.Messenger, SocialTab.Contacts, SocialTab.Residents, SocialTab.Diary, SocialTab.Gossip, SocialTab.Arcs, SocialTab.Outings };

	public static bool ViewingResidents => ResidentsPage.Viewing;

	public static string? ViewingMessenger => MessengerPage.ViewingNpc;

	public static void PreselectChat(string npc) => MessengerPage.Preselect(npc);

	private static Tab _lastTab;

	private static SocialTab _lastSocial;

	private static WorldTab _lastWorld;

	private readonly ShellContext _ctx;

	private readonly Dictionary<(Tab, int), Stack<ShellPage>> _stacks = new Dictionary<(Tab, int), Stack<ShellPage>>();

	private readonly Hits _hits = new Hits();

	private readonly List<Rectangle> _signs = new List<Rectangle>();

	private Tab _tab;

	private SocialTab _social;

	private WorldTab _world;

	private Rectangle _frame;

	private Rectangle _paper;

	private Rectangle _strip;

	private Rectangle _returnBox;

	private Rectangle _crossBox;

	private string _notice = "";

	private int _noticeUntil;

	private int _sizePercent;

	private float _subScale = 1f;

	public ShellMenu(ShellContext ctx, Tab? tab = null, SocialTab? social = null, WorldTab? world = null, Device device = Device.Window)
	{
		_ctx = ctx;
		_device = device;
		Hearth.UseSkin(ctx.Config().ShellSkin);
		_tab = tab ?? (OnDevice ? Tab.Desk : _lastTab);
		_social = social ?? _lastSocial;
		_world = world ?? _lastWorld;
		if (!WorldTabs().Contains(_world))
		{
			_world = WorldTab.Village;
		}
		if (!SocialTabsNow().Contains(_social))
		{
			_social = SocialTab.Letters;
		}
		Layout();
		StartBoot();
		Page.Reload();
		AISV.Core.Ai.UsageReporter.Note("device." + (device == Device.Window ? "shell" : device.ToString()));
		NoteTab();
	}

	private void NoteTab() => AISV.Core.Ai.UsageReporter.Note("tab." + (_tab switch { Tab.Social => "social." + _social, Tab.World => "world." + _world, _ => _tab.ToString() }));

	private Stack<ShellPage> Stack
	{
		get
		{
			(Tab, int) key = (_tab, _tab switch { Tab.Social => (int)_social, Tab.World => (int)_world, _ => 0 });
			if (!_stacks.TryGetValue(key, out Stack<ShellPage>? s))
			{
				s = new Stack<ShellPage>();
				ShellPage root = Root();
				root.Host = this;
				s.Push(root);
				_stacks[key] = s;
			}
			return s;
		}
	}

	private ShellPage Page => Stack.Peek();

	private bool Pushed => Stack.Count > 1;

	private bool HasSubBar => !OnDevice && !Pushed && SubTabs().Count > 1;

	private Rectangle Content => OnDevice ? DeviceContent : HasSubBar || Pushed
		? new Rectangle(_strip.X, _strip.Bottom + 6, _strip.Width, _paper.Bottom - 14 - (_strip.Bottom + 6))
		: new Rectangle(_strip.X, _strip.Y, _strip.Width, _paper.Bottom - 14 - _strip.Y);

	private ShellPage Root() => _tab switch
	{
		Tab.Social => _social switch
		{
			SocialTab.Messenger => new MessengerPage(_ctx),
			SocialTab.Contacts => new VillagersPage(_ctx),
			SocialTab.Residents => new ResidentsPage(_ctx),
			SocialTab.Diary => new DiaryPage(_ctx),
			SocialTab.Gossip => new GossipPage(_ctx),
			SocialTab.Arcs => new ArcsPage(_ctx),
			SocialTab.Outings => new OutingsPage(_ctx),
			SocialTab.Partners => new PartnersPage(_ctx),
			_ => new LettersPage(_ctx)
		},
		Tab.World => _world switch
		{
			WorldTab.Market => new MarketPage(_ctx),
			WorldTab.Commissions => new CommissionsPage(_ctx),
			WorldTab.Ledger => new TownLedgerPage(_ctx),
			WorldTab.Help => new FarmHelpPage(_ctx),
			WorldTab.News => new NewsPage(_ctx),
			_ => new WorldPage(_ctx)
		},
		Tab.Apps => new AISV.UI.Apps.AppsPage(_ctx),
		Tab.Desk => new AISV.UI.Apps.DeskPage(_ctx, DeskSections),
		Tab.Settings => new SettingsPage(_ctx),
		_ => new TodayPage(_ctx)
	};

	private const int BoardH = 86;

	private int _dateRoom;

	private bool _signIcons;

	private void Layout()
	{
		int vw = Game1.uiViewport.Width, vh = Game1.uiViewport.Height;
		xPositionOnScreen = 0;
		yPositionOnScreen = 0;
		width = vw;
		height = vh;
		_sizePercent = _ctx.Config().WindowPercent;
		if (OnDevice)
		{
			LayoutDevice(vw, vh);
			return;
		}
		Point size = new Point(Math.Min(vw - 40, 1480 * _sizePercent / 100), Math.Min(vh - 40, 960 * _sizePercent / 100));
		_frame = new Rectangle(new Point((vw - size.X) / 2, (vh - size.Y) / 2), size);
		_paper = Hearth.Inflate(_frame, -Hearth.WindowBorder);
		Rectangle board = new Rectangle(_paper.X, _paper.Y, _paper.Width, BoardH);
		_crossBox = new Rectangle(board.Right - 62, board.Center.Y - 23, 46, 46);
		_strip = new Rectangle(board.X + 10, board.Bottom + 8, board.Width - 20, 50);
		HangSigns(board);
	}

	/// <summary>页签按字数排开，夹在日期牌和金币牌之间；放不下就不要图标。</summary>
	private void HangSigns(Rectangle board)
	{
		_dateRoom = Math.Clamp(board.Width / 4, 170, 320);
		int from = board.X + 16 + _dateRoom + 8, to = _crossBox.X - 12 - (int)Hearth.Width("9,999,999") - 62 - 8;
		int[] words = Tabs.Select(t => (int)Hearth.Width(TabLabel(t))).ToArray();
		int bare = words.Sum() + Tabs.Length * 30;
		_signIcons = bare + Tabs.Length * 38 <= to - from;
		int total = bare + (_signIcons ? Tabs.Length * 38 : 0);
		int gap = Math.Clamp((to - from - total) / (Tabs.Length + 1), 2, 22);
		int x = from + Math.Max(0, (to - from - total - gap * (Tabs.Length - 1)) / 2);
		_signs.Clear();
		foreach (int word in words)
		{
			int wide = word + 30 + (_signIcons ? 38 : 0);
			_signs.Add(new Rectangle(x, board.Y + 17, wide, 52));
			x += wide + gap;
		}
	}

	/// <summary>有伴侣才显示"伴侣"。</summary>
	private SocialTab[] SocialTabsNow() => _ctx.Stories?.Partners().Count > 0 ? SocialTabs.Append(SocialTab.Partners).ToArray() : SocialTabs;

	/// <summary>市场、委托、同伴模块关着时对应的子页签不显示。</summary>
	private WorldTab[] WorldTabs()
	{
		List<WorldTab> tabs = new List<WorldTab> { WorldTab.Village };
		if (_ctx.Market?.Enabled == true)
		{
			tabs.Add(WorldTab.Market);
		}
		if ((_ctx.Commissions?.Enabled == true || _ctx.Errands?.Enabled == true) && StardewModdingAPI.Context.IsMainPlayer)
		{
			tabs.Add(WorldTab.Commissions);
		}
		if (_ctx.Ledger?.Enabled == true && StardewModdingAPI.Context.IsMainPlayer)
		{
			tabs.Add(WorldTab.Ledger);
		}
		if (_ctx.FarmHelp?.Enabled == true)
		{
			tabs.Add(WorldTab.Help);
		}
		if (_ctx.News?.Enabled == true)
		{
			tabs.Add(WorldTab.News);
		}
		return tabs.ToArray();
	}

	/// <summary>当前大页签下面的子页签：位置、名字、选没选中、切过去。</summary>
	private List<(Rectangle Bounds, string Label, bool Selected, Action Select)> SubTabs()
	{
		List<(string Label, bool Selected, Action Select)> items = new List<(string, bool, Action)>();
		if (_tab == Tab.Social)
		{
			foreach (SocialTab t in SocialTabsNow())
			{
				items.Add((SubLabel(t), t == _social, () => Switch(Tab.Social, t, _world)));
			}
		}
		else if (_tab == Tab.World)
		{
			foreach (WorldTab t in WorldTabs())
			{
				items.Add((WorldLabel(t), t == _world, () => Switch(Tab.World, _social, t)));
			}
		}
		List<(Rectangle, string, bool, Action)> result = new List<(Rectangle, string, bool, Action)>();
		// 放不下时（比如英文）先缩留白，再缩字。整排居中。
		const int gap = 6;
		float textW = items.Sum(i => Hearth.Width(i.Label));
		int room = _strip.Width - 12 - items.Count * gap;
		int pad = items.Count == 0 ? 40 : Math.Clamp((room - (int)textW) / items.Count, 16, 40);
		_subScale = Math.Clamp((room - pad * items.Count) / Math.Max(1f, textW), 0.6f, 1f);
		int[] widths = items.Select(i => (int)(Hearth.Width(i.Label) * _subScale) + pad).ToArray();
		int x = _strip.X + (_strip.Width - widths.Sum() - gap * Math.Max(0, widths.Length - 1)) / 2;
		for (int i = 0; i < items.Count; i++)
		{
			result.Add((new Rectangle(x, _strip.Y, widths[i], _strip.Height - 2), items[i].Label, items[i].Selected, items[i].Select));
			x += widths[i] + gap;
		}
		return result;
	}

	public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
	{
		base.gameWindowSizeChanged(oldBounds, newBounds);
		Layout();
	}

	public override void update(GameTime time)
	{
		base.update(time);
		Hearth.UseSkin(_ctx.Config().ShellSkin);
		if (_bootLeft > 0)
		{
			_bootLeft -= time.ElapsedGameTime.TotalSeconds;
			if (_bootLeft <= 0)
			{
				Game1.playSound("crystal");
			}
			return;
		}
		if (_ctx.Config().WindowPercent != _sizePercent)
		{
			Layout();
		}
		Page.Tick();
	}

	public override void draw(SpriteBatch b)
	{
		if (OnDevice)
		{
			DrawDevice(b);
			return;
		}
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
		FrameDecor.DrawDrift(b, Game1.uiViewport.Width, Game1.uiViewport.Height);
		Hearth.WoodFrame(b, _frame);
		DrawTopBar(b);
		if (Pushed)
		{
			DrawPageHeader(b);
		}
		else if (HasSubBar)
		{
			DrawSubBar(b);
		}
		Rectangle content = Content;
		Hearth.Paper(b, content);
		FrameDecor.PaperMarks(b, content);
		_hits.Clear();
		Page.Draw(b, content, _hits);
		FrameDecor.DrawFrame(b, _frame);
		DrawNotice(b, content);
		drawMouse(b);
	}

	/// <summary>提示挂在纸面上方，像一块临时的小木牌。</summary>
	private void DrawNotice(SpriteBatch b, Rectangle content)
	{
		if (_notice.Length == 0 || Game1.ticks >= _noticeUntil)
		{
			return;
		}
		string text = Hearth.Truncate(_notice, content.Width - 70, 0.82f);
		int wide = (int)Hearth.Width(text, 0.82f) + 40;
		Rectangle sign = new Rectangle(content.Center.X - wide / 2, content.Y + 14, wide, 46);
		foreach (int x in new[] { sign.X + 12, sign.Right - 16 })
		{
			Hearth.Fill(b, new Rectangle(x, content.Y, 4, 16), Hearth.Bark);
		}
		Hearth.Plate(b, sign, Hearth.AmberPale);
		Hearth.InkMiddle(b, text, sign, Hearth.Quill, 0.82f);
	}

	/// <summary>顶上是一条木板：左边日期牌，中间挂着的页签，右边金币牌和关闭按钮。</summary>
	private void DrawTopBar(SpriteBatch b)
	{
		Rectangle top = new Rectangle(_paper.X, _paper.Y, _paper.Width, BoardH);
		Hearth.Fill(b, top, Hearth.Timber);
		for (int y = top.Y, row = 0; y < top.Bottom; y += 21, row++)
		{
			Hearth.Fill(b, new Rectangle(top.X, y, top.Width, 2), Hearth.Sapling * 0.6f);
			Hearth.Fill(b, new Rectangle(top.X, y + 19, top.Width, 2), Hearth.Bark * 0.7f);
			for (int x = top.X + 40 + row * 137 % 180; x < top.Right; x += 260)
			{
				Hearth.Fill(b, new Rectangle(x, y + 2, 2, 17), Hearth.Bark * 0.5f);
			}
		}
		Hearth.Fill(b, new Rectangle(top.X, top.Bottom, top.Width, 4), Hearth.Bark);
		Hearth.Fill(b, new Rectangle(top.X, top.Bottom + 4, top.Width, 6), Hearth.Bark * 0.22f);
		FrameDecor.Lantern(b, top.X + 6, top.Bottom - 2);
		FrameDecor.Lantern(b, top.Right - 26, top.Bottom - 2);
		string left = Hearth.Truncate(ShellText.TopLine(), _dateRoom - 32, 0.82f);
		Plaque(b, new Rectangle(top.X + 16, top.Y + 18, (int)Hearth.Width(left, 0.82f) + 32, 48), left, 0.82f);
		for (int i = 0; i < Tabs.Length; i++)
		{
			Hearth.HangingSign(b, _signs[i], TabLabel(Tabs[i]), Tabs[i] == _tab, Hearth.Hover(_signs[i]), _signIcons ? TabIcon(Tabs[i]) : null);
		}
		string money = Utility.getNumberWithCommas(Game1.player?.Money ?? 0);
		int moneyW = (int)Hearth.Width(money) + 62;
		Rectangle purse = new Rectangle(_crossBox.X - 12 - moneyW, top.Y + 18, moneyW, 48);
		Plaque(b, purse, "", 1f);
		FrameDecor.Coin(b, purse.X + 14, purse.Center.Y - 15, 3);
		Hearth.Ink(b, money, new Vector2(purse.X + 46, purse.Center.Y - Hearth.LineHeight() / 2 + 2), new Color(120, 44, 10));
		Hearth.CrossPlate(b, _crossBox, Hearth.Hover(_crossBox));
	}

	private static PixelArt TabIcon(Tab t) => t switch
	{
		Tab.Social => AISV.UI.Apps.DeskIcons.Villagers,
		Tab.World => AISV.UI.Apps.DeskIcons.Market,
		Tab.Apps => AISV.UI.Apps.AppIcons.Junimo,
		Tab.Settings => AISV.UI.Apps.DeskIcons.Settings,
		_ => AISV.UI.Apps.DeskIcons.Today
	};

	private static void Plaque(SpriteBatch b, Rectangle r, string text, float scale)
	{
		Hearth.Plate(b, r, new Color(255, 234, 186));
		Hearth.InkMiddle(b, text, r, Hearth.Quill, scale);
	}

	private void DrawSubBar(SpriteBatch b)
	{
		foreach ((Rectangle r, string label, bool selected, _) in SubTabs())
		{
			Hearth.PaperTab(b, r, label, selected, Hearth.Hover(r), _subScale);
		}
		Hearth.HLine(b, _strip.X, _strip.Bottom, _strip.Width, Hearth.Seam);
	}

	/// <summary>页头是一条面包屑：回去的木牌，后面跟着这一页的名字。</summary>
	private void DrawPageHeader(SpriteBatch b)
	{
		string crumb = OnDevice && !Pushed ? Tr.Get("desk.start", "主屏") : _tab switch
		{
			Tab.Social => SubLabel(_social),
			Tab.World when WorldTabs().Length > 1 => WorldLabel(_world),
			Tab.Desk => Tr.Get("desk.start", "主屏"),
			_ => TabLabel(_tab)
		};
		if (OnDevice)
		{
			Hearth.Fill(b, _strip, Hearth.ParchmentDeep);
		}
		_returnBox = new Rectangle(_strip.X + 4, _strip.Y + 3, (int)Hearth.Width(crumb, 0.8f) + 50, _strip.Height - 10);
		Hearth.PressPlate(b, _returnBox, "‹ " + crumb, hover: Hearth.Hover(_returnBox), scale: 0.8f);
		int titleX = _returnBox.Right + 18;
		Hearth.InkBold(b, Hearth.Truncate(TitleNow, _strip.Right - titleX - 12, 1f), new Vector2(titleX, _strip.Center.Y - Hearth.LineHeight(1f) / 2), Hearth.Quill, 1f);
		Hearth.Dotted(b, _strip.X, _strip.Bottom - 2, _strip.Width);
	}

	private static string TabLabel(Tab t) => t switch
	{
		Tab.Social => Tr.Get("shell.tab.social", "社交"),
		Tab.World => Tr.Get("shell.tab.world", "世界"),
		Tab.Apps => Tr.Get("shell.tab.apps", "应用"),
		Tab.Settings => Tr.Get("shell.tab.settings", "设置"),
		_ => Tr.Get("shell.tab.today", "今日")
	};

	private static string SubLabel(SocialTab t) => t switch
	{
		SocialTab.Messenger => Tr.Get("shell.sub.messenger", "信使"),
		SocialTab.Contacts => Tr.Get("shell.sub.villagers", "村民"),
		SocialTab.Residents => Tr.Get("shell.sub.residents", "居民群"),
		SocialTab.Diary => Tr.Get("shell.sub.diary", "日记"),
		SocialTab.Gossip => Tr.Get("shell.sub.gossip", "八卦"),
		SocialTab.Arcs => Tr.Get("shell.sub.arcs", "小剧场"),
		SocialTab.Outings => Tr.Get("shell.sub.outings", "出游"),
		SocialTab.Partners => Tr.Get("shell.sub.partners", "伴侣"),
		_ => Tr.Get("shell.sub.letters", "信件")
	};

	private static string WorldLabel(WorldTab t) => t switch
	{
		WorldTab.Market => Tr.Get("shell.sub.market", "市场"),
		WorldTab.Commissions => Tr.Get("shell.sub.commissions", "委托"),
		WorldTab.Ledger => Tr.Get("shell.sub.ledger", "账本"),
		WorldTab.Help => Tr.Get("shell.sub.help", "帮工"),
		WorldTab.News => Tr.Get("shell.sub.news", "周报"),
		_ => Tr.Get("shell.sub.village", "村民之间")
	};

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_bootLeft > 0)
		{
			_bootLeft = 0;
			Game1.playSound("crystal");
			return;
		}
		if (OnDevice && _home.Contains(x, y))
		{
			Page.StepBack();
			if (Page.CanLeave() && !OnDesk)
			{
				Open(Tab.Desk);
			}
			return;
		}
		if (_crossBox.Contains(x, y))
		{
			if (!Page.CanLeave())
			{
				return;
			}
			Game1.playSound("bigDeSelect");
			exitThisMenu(playSound: false);
			return;
		}
		for (int i = 0; i < _signs.Count; i++)
		{
			if (_signs[i].Contains(x, y))
			{
				if (Page.CanLeave())
				{
					Switch(Tabs[i], _social, _world);
				}
				return;
			}
		}
		if (CanGoBack && _returnBox.Contains(x, y))
		{
			if (Page.StepBack())
			{
				return;
			}
			if (Page.CanLeave())
			{
				Back();
			}
			return;
		}
		if (HasSubBar)
		{
			foreach ((Rectangle r, _, _, Action select) in SubTabs())
			{
				if (r.Contains(x, y))
				{
					select();
					return;
				}
			}
		}
		_hits.Click(x, y);
	}

	public override bool overrideSnappyMenuCursorMovementBan() => true;

	public override void receiveRightClick(int x, int y, bool playSound = true)
	{
		if (!Page.StepBack() && CanGoBack && Page.CanLeave())
		{
			Back();
		}
	}

	public override void receiveScrollWheelAction(int direction) => Page.Scroll(direction);

	public override void receiveKeyPress(Keys key)
	{
		if (Page.KeyPress(key))
		{
			return;
		}
		if (Page.CapturesKeys && key != Keys.Escape)
		{
			return;
		}
		if (key == Keys.Escape && Page.StepBack())
		{
			return;
		}
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			if (!Page.CanLeave())
			{
				return;
			}
			if (CanGoBack && key == Keys.Escape)
			{
				Back();
				return;
			}
			exitThisMenu();
		}
	}

	public override void receiveGamePadButton(Buttons b)
	{
		if (b == Buttons.B)
		{
			receiveKeyPress(Keys.Escape);
		}
		else if (!OnDevice && (b == Buttons.LeftShoulder || b == Buttons.RightShoulder) && Page.CanLeave())
		{
			int i = Array.IndexOf(Tabs, _tab) + (b == Buttons.RightShoulder ? 1 : -1);
			Switch(Tabs[(i + Tabs.Length) % Tabs.Length], _social, _world);
		}
	}

	protected override void cleanupBeforeExit()
	{
		foreach (Stack<ShellPage> s in _stacks.Values)
		{
			foreach (ShellPage p in s)
			{
				p.Closed();
			}
		}
		base.cleanupBeforeExit();
	}

	private void Switch(Tab tab, SocialTab social, WorldTab world)
	{
		if (!WorldTabs().Contains(world))
		{
			world = WorldTab.Village;
		}
		if (tab == _tab && social == _social && world == _world)
		{
			return;
		}
		Page.Closed();
		_tab = tab;
		_social = social;
		_world = world;
		if (!OnDevice)
		{
			_lastTab = tab;
			_lastSocial = social;
			_lastWorld = world;
		}
		Game1.playSound("smallSelect");
		_hits.Clear();
		Page.Reload();
		NoteTab();
	}

	void IShellHost.Push(ShellPage page)
	{
		AISV.Core.Ai.UsageReporter.Note("page." + page.GetType().Name);
		page.Host = this;
		Stack.Push(page);
		_hits.Clear();
		Game1.playSound("bigSelect");
		page.Reload();
	}

	public void Back()
	{
		if (!Pushed)
		{
			if (OnDevice && _tab != Tab.Desk)
			{
				Switch(Tab.Desk, _social, _world);
			}
			return;
		}
		Stack.Pop().Closed();
		_hits.Clear();
		Game1.playSound("shwip");
		Page.Reload();
	}

	/// <summary>调试用：在"应用"里打开一个应用。</summary>
	public void OpenApp(string id)
	{
		Open(Tab.Apps);
		if (Page is AISV.UI.Apps.AppsPage apps)
		{
			apps.OpenById(id);
		}
	}

	public void Notice(string text)
	{
		_notice = ShellText.Clean(text);
		_noticeUntil = Game1.ticks + 240;
	}

	/// <summary>打开设置下的我的设定。</summary>
	public void OpenProfile(bool welcome = false)
	{
		Open(Tab.Settings);
		((IShellHost)this).Push(new ProfilePage(_ctx, welcome));
	}

	public void OpenAccount(bool prompted = false)
	{
		Open(Tab.Settings);
		((IShellHost)this).Push(new AccountPage(_ctx, prompted));
	}

	/// <summary>正在用外壳里的功能时要登录：账号页盖在当前页上，办完回到这一页。</summary>
	public void PromptAccount()
	{
		if (Page is not AccountPage)
		{
			((IShellHost)this).Push(new AccountPage(_ctx, prompted: true, inPlace: true));
		}
	}

	public void OpenAiStatus()
	{
		Open(Tab.Settings);
		((IShellHost)this).Push(new AiStatusPage(_ctx));
	}

	public void OpenNpcCompat()
	{
		Open(Tab.Settings);
		((IShellHost)this).Push(new NpcCompatPage(_ctx));
	}

	public void OpenVillagerDiary(string npc)
	{
		Open(Tab.Social, SocialTab.Contacts);
		((IShellHost)this).Push(new VillagerDiaryBook(_ctx, npc));
	}

	/// <summary>打开一位村民的家当；loan 为真时直接到借钱。</summary>
	public void OpenLedger(string npc, bool loan = false)
	{
		Open(Tab.World, world: WorldTab.Ledger);
		((IShellHost)this).Push(new LedgerPage(_ctx, npc, loan ? LedgerPage.View.Loan : LedgerPage.View.Carried));
	}

	/// <summary>下次打开委托页时直接看"我发的"。</summary>
	public static void ShowMyErrands() => CommissionsPage.ShowMine = true;

	/// <summary>托一位村民办事；<paramref name="inline"/> 不为空时他当面回答。</summary>
	public void OpenErrand(string npc, Func<AISV.Core.World.Errands.Errand, AISV.Core.World.Errands.ErrandReply, bool>? inline = null)
	{
		CommissionsPage.ShowMine = true;
		Open(Tab.World, world: WorldTab.Commissions);
		((IShellHost)this).Push(new ErrandFormPage(_ctx, npc, inline));
	}

	/// <summary>最上面的委托表单还在等这件委托的当面回话。</summary>
	public bool IsAwaiting(string errandId) => Page is ErrandFormPage f && f.Awaits(errandId);

	/// <summary>调试用：打开村民角色卡。</summary>
	public void OpenCards()
	{
		Open(Tab.Settings);
		((IShellHost)this).Push(new CardsPage(_ctx));
	}

	public void Open(Tab tab, SocialTab social = SocialTab.Letters, WorldTab? world = null)
	{
		while (Pushed)
		{
			Stack.Pop().Closed();
		}
		Switch(tab, social, world ?? _world);
	}


}
