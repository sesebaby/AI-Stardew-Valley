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
public sealed class ShellMenu : IClickableMenu, IShellHost
{
	public enum Tab
	{
		Today,
		Social,
		World,
		Apps,
		Settings
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

	private readonly List<Rectangle> _tabRects = new List<Rectangle>();

	private Tab _tab;

	private SocialTab _social;

	private WorldTab _world;

	private Rectangle _outer;

	private Rectangle _page;

	private Rectangle _header;

	private Rectangle _back;

	private Rectangle _close;

	private string _notice = "";

	private int _noticeUntil;

	public ShellMenu(ShellContext ctx, Tab? tab = null, SocialTab? social = null, WorldTab? world = null)
	{
		_ctx = ctx;
		_tab = tab ?? _lastTab;
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
		Page.Reload();
	}

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

	private bool HasSubBar => !Pushed && SubTabs().Count > 1;

	private Rectangle Content => HasSubBar || Pushed
		? new Rectangle(_page.X + 12, _header.Bottom + 6, _page.Width - 24, _page.Bottom - 12 - (_header.Bottom + 6))
		: new Rectangle(_page.X + 12, _page.Y + 92, _page.Width - 24, _page.Height - 104);

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
		Tab.Settings => new SettingsPage(_ctx),
		_ => new TodayPage(_ctx)
	};

	private void Layout()
	{
		int vw = Game1.uiViewport.Width, vh = Game1.uiViewport.Height;
		xPositionOnScreen = 0;
		yPositionOnScreen = 0;
		width = vw;
		height = vh;
		int w = Math.Min(vw - 48, 1440), h = Math.Min(vh - 48, 940);
		_outer = new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
		_page = AisvTheme.Inflate(_outer, -AisvTheme.WindowBorder);
		_tabRects.Clear();
		// 两边留出日期和金钱的位置，窄的时候页签跟着收窄。
		int tw = Math.Clamp((_page.Width - 2 * 250) / Tabs.Length, 88, 132);
		int startX = _page.Center.X - tw * Tabs.Length / 2;
		for (int i = 0; i < Tabs.Length; i++)
		{
			_tabRects.Add(new Rectangle(startX + i * tw + 8, _page.Y + 18, tw - 16, 50));
		}
		_close = new Rectangle(_page.Right - 64, _page.Y + 22, 44, 44);
		_header = new Rectangle(_page.X + 12, _page.Y + 92, _page.Width - 24, 52);
		_back = new Rectangle(_header.X + 4, _header.Y + 6, 150, _header.Height - 12);
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
		int x = _header.X + 8;
		foreach ((string label, bool selected, Action select) in items)
		{
			Rectangle r = new Rectangle(x, _header.Y + 2, (int)AisvTheme.Width(label) + 44, _header.Height - 4);
			result.Add((r, label, selected, select));
			x = r.Right + 4;
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
		Page.Tick();
	}

	public override void draw(SpriteBatch b)
	{
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
		AisvTheme.Window(b, _outer);
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
		AisvTheme.Fill(b, content, AisvTheme.Page);
		_hits.Clear();
		Page.Draw(b, content, _hits);
		if (_notice.Length > 0 && Game1.ticks < _noticeUntil)
		{
			string text = AisvTheme.Truncate(_notice, content.Width - 80, 0.85f);
			int tw = (int)AisvTheme.Width(text, 0.85f);
			Rectangle r = new Rectangle(_page.Center.X - tw / 2 - 24, content.Bottom - 70, tw + 48, 50);
			AisvTheme.FillRounded(b, r, AisvTheme.Text * 0.88f, 25);
			AisvTheme.TextCentered(b, text, r, Color.White, 0.85f);
		}
		drawMouse(b);
	}

	private void DrawTopBar(SpriteBatch b)
	{
		Rectangle top = new Rectangle(_page.X, _page.Y, _page.Width, 84);
		AisvTheme.FillRounded(b, new Rectangle(top.X, top.Y, top.Width, top.Height + 12), AisvTheme.PageDeep, 12);
		AisvTheme.Fill(b, new Rectangle(top.X, top.Bottom, top.Width, 12), AisvTheme.Page);
		AisvTheme.HLine(b, top.X, top.Bottom, top.Width, AisvTheme.Divider);
		string left = AisvTheme.Truncate(ShellText.TopLine(), _tabRects[0].X - top.X - 40, 0.9f);
		AisvTheme.DrawText(b, left, new Vector2(top.X + 28, top.Center.Y - 14), AisvTheme.TextMuted, 0.9f);
		for (int i = 0; i < Tabs.Length; i++)
		{
			AisvTheme.Tab(b, _tabRects[i], TabLabel(Tabs[i]), Tabs[i] == _tab, AisvTheme.Hover(_tabRects[i]));
		}
		string money = Utility.getNumberWithCommas(Game1.player?.Money ?? 0) + "G";
		Vector2 ms = Game1.smallFont.MeasureString(money);
		AisvTheme.Bold(b, money, new Vector2(_close.X - 24 - ms.X, top.Center.Y - ms.Y / 2f + 2f), AisvTheme.Text);
		AisvTheme.CloseButton(b, _close, AisvTheme.Hover(_close));
	}

	private void DrawSubBar(SpriteBatch b)
	{
		foreach ((Rectangle r, string label, bool selected, _) in SubTabs())
		{
			AisvTheme.SubTab(b, r, label, selected, AisvTheme.Hover(r));
		}
		AisvTheme.HLine(b, _header.X, _header.Bottom, _header.Width, AisvTheme.Divider);
	}

	private void DrawPageHeader(SpriteBatch b)
	{
		string backLabel = _tab switch
		{
			Tab.Social => SubLabel(_social),
			Tab.World when WorldTabs().Length > 1 => WorldLabel(_world),
			_ => TabLabel(_tab)
		};
		int lw = (int)AisvTheme.Width(backLabel, 0.9f);
		Rectangle pill = new Rectangle(_back.X, _back.Y, Math.Min(_back.Width, lw + 44), _back.Height);
		if (AisvTheme.Hover(pill))
		{
			AisvTheme.FillRounded(b, pill, AisvTheme.SelectedRow, pill.Height / 2);
		}
		AisvTheme.Chevron(b, new Vector2(pill.X + 16, pill.Center.Y), left: true, AisvTheme.AccentDark);
		AisvTheme.DrawText(b, backLabel, new Vector2(pill.X + 30, pill.Center.Y - 12), AisvTheme.AccentDark, 0.9f);
		string title = AisvTheme.Truncate(Page.Title, _header.Width - 2 * (pill.Width + 24), 1.05f);
		float tw = AisvTheme.Width(title, 1.05f);
		AisvTheme.Bold(b, title, new Vector2(_header.Center.X - tw / 2f, _header.Center.Y - 12), AisvTheme.Text, 1.05f);
		AisvTheme.HLine(b, _header.X, _header.Bottom, _header.Width, AisvTheme.Divider);
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
		if (_close.Contains(x, y))
		{
			if (!Page.CanLeave())
			{
				return;
			}
			Game1.playSound("bigDeSelect");
			exitThisMenu(playSound: false);
			return;
		}
		for (int i = 0; i < Tabs.Length; i++)
		{
			if (_tabRects[i].Contains(x, y))
			{
				if (Page.CanLeave())
				{
					Switch(Tabs[i], _social, _world);
				}
				return;
			}
		}
		if (Pushed && _back.Contains(x, y))
		{
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
		if (Pushed && Page.CanLeave())
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
		if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key))
		{
			if (!Page.CanLeave())
			{
				return;
			}
			if (Pushed && key == Keys.Escape)
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
		else if ((b == Buttons.LeftShoulder || b == Buttons.RightShoulder) && Page.CanLeave())
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
		_lastTab = tab;
		_lastSocial = social;
		_lastWorld = world;
		Game1.playSound("smallSelect");
		_hits.Clear();
		Page.Reload();
	}

	void IShellHost.Push(ShellPage page)
	{
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
