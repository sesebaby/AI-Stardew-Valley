using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Social;
using AISV.Core.Social.Web;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>村民：左边是认识的人（可搜索，按交情分组），右边是选中那位的详情。</summary>
internal sealed class VillagersPage : ShellPage
{
	private sealed class Villager
	{
		public string Name = "";

		public string Display = "";

		public NPC? Npc;

		public int Hearts;

		public int MaxHearts = 10;

		public int Group;

		public string Relation = "";

		public string Where = "";

		public string Mood = "";

		public string MoodReason = "";

		public bool Birthday;

		public string BirthdayText = "";

		public int GiftsWeek;

		public bool TalkedToday;

		public bool AiEnabled;
	}

	private sealed class Section
	{
		public string Title = "";

		public List<(string Text, Color Color)> Lines = new List<(string, Color)>();

		public List<(string Label, float Fraction, string Note)> Bars = new List<(string, float, string)>();
	}

	/// <summary>从别的页跳过来时要选中的村民。</summary>
	public static string? Focus;

	private readonly TextBox _search;

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _detailScroll = new Scroller();

	private List<Villager> _all = new List<Villager>();

	private List<Villager> _shown = new List<Villager>();

	private string? _selected;

	private string _query = "";

	private Rectangle _listArea;

	private Rectangle _detail;

	private Rectangle _searchBox;

	public VillagersPage(ShellContext ctx) : base(ctx)
	{
		_search = new TextBox(null, null, Game1.smallFont, Hearth.Quill) { textLimit = 20, limitWidth = false };
	}

	public override bool CapturesKeys => _search.Selected;

	public override void Reload()
	{
		if (Focus != null)
		{
			_selected = Focus;
			Focus = null;
			_detailScroll.Reset();
		}
		Farmer? player = Game1.player;
		List<Villager> list = new List<Villager>();
		if (player != null)
		{
			foreach (string name in player.friendshipData.Keys.ToList())
			{
				NPC? npc = GameState.FindNpc(name);
				if (npc == null || !npc.CanSocialize || Game1.characterData == null || !Game1.characterData.ContainsKey(name))
				{
					continue;
				}
				list.Add(Build(name, npc, player.friendshipData[name]));
			}
		}
		_all = list.OrderBy(v => v.Group).ThenByDescending(v => v.Hearts).ThenBy(v => v.Display, StringComparer.CurrentCulture).ToList();
		ApplySearch();
	}

	private Villager Build(string name, NPC npc, Friendship f)
	{
		bool partner = f.IsMarried() || f.IsRoommate();
		Villager v = new Villager
		{
			Name = name,
			Display = Ctx.Display(name),
			Npc = npc,
			MaxHearts = partner ? 14 : 10,
			Hearts = Math.Min(Ctx.Relationships.EffectiveHearts(name), partner ? 14 : 10),
			GiftsWeek = f.GiftsThisWeek,
			TalkedToday = f.TalkedToToday,
			AiEnabled = Ctx.Npcs.IsEnabled(name),
			Birthday = npc.isBirthday(),
			Where = npc.currentLocation?.DisplayName ?? ""
		};
		(v.Relation, v.Group) = f.IsMarried() ? (Tr.Get("shell.villagers.spouse", "配偶"), 0)
			: f.IsRoommate() ? (Tr.Get("shell.villagers.roommate", "室友"), 0)
			: f.IsEngaged() ? (Tr.Get("shell.villagers.engaged", "订婚了"), 0)
			: f.IsDating() ? (Tr.Get("shell.villagers.dating", "在约会"), 0)
			: v.Hearts >= 8 ? (Tr.Get("shell.villagers.close", "好朋友"), 1)
			: v.Hearts >= 4 ? (Tr.Get("shell.villagers.friend", "朋友"), 2)
			: (Tr.Get("shell.villagers.known", "认识"), 3);
		NpcMood? mood = Ctx.Social.Moods.Get(name);
		if (mood != null && v.AiEnabled)
		{
			v.Mood = MoodBook.MoodWord(mood.Temper);
			v.MoodReason = ShellText.Clean(mood.Because);
		}
		if (!string.IsNullOrEmpty(npc.Birthday_Season) && npc.Birthday_Day > 0)
		{
			v.BirthdayText = Tr.Get("shell.villagers.birthdayDate", "{{season}} {{day}} 日", ("season", ShellText.Season(npc.Birthday_Season)), ("day", npc.Birthday_Day));
		}
		return v;
	}

	private void ApplySearch()
	{
		string q = (_search.Text ?? "").Trim();
		_query = q;
		_shown = q.Length == 0 ? _all.ToList() : _all.Where(v => v.Display.Contains(q, StringComparison.OrdinalIgnoreCase) || v.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
		if (_selected == null || !_all.Any(v => v.Name == _selected))
		{
			_selected = _shown.FirstOrDefault()?.Name;
		}
	}

	private Villager? Selected => _all.FirstOrDefault(v => v.Name == _selected);

	private static string GroupTitle(int group) => group switch
	{
		0 => Tr.Get("shell.villagers.groupPartner", "伴侣"),
		1 => Tr.Get("shell.villagers.groupClose", "好朋友"),
		2 => Tr.Get("shell.villagers.groupFriend", "朋友"),
		_ => Tr.Get("shell.villagers.groupKnown", "认识的人")
	};

	public override void Scroll(int direction)
	{
		if (_listArea.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			_listScroll.Wheel(direction, 68);
		}
		else
		{
			_detailScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		if (_search.Selected)
		{
			if (key == Keys.Escape || key == Keys.Enter)
			{
				_search.Selected = false;
			}
			return true;
		}
		return _detailScroll.PageKeys(key, _detail.Height - 80);
	}

	public override void Closed() => _search.Selected = false;

	private const int NarrowWidth = 760;

	/// <summary>窄屏时先看列表，点进去才看详情。</summary>
	private bool _reading;

	private int _narrowTick = -10;

	public override bool StepBack()
	{
		bool was = _reading && Game1.ticks - _narrowTick <= 2;
		_reading &= !was;
		return was;
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if ((_search.Text ?? "").Trim() != _query)
		{
			ApplySearch();
			_listScroll.Reset();
		}
		bool narrow = area.Width < NarrowWidth;
		if (narrow)
		{
			_narrowTick = Game1.ticks;
		}
		if (narrow && _reading)
		{
			Rectangle back = new Rectangle(area.X + 10, area.Y + 8, 170, 40);
			Hearth.PressPlate(b, back, "‹ " + Tr.Get("shell.villagers.backToList", "村民列表"), hover: Hearth.Hover(back), scale: 0.78f);
			hits.Add(back, () => _reading = false);
			_detail = new Rectangle(area.X, back.Bottom + 4, area.Width, area.Bottom - back.Bottom - 4);
			DrawDetail(b, hits);
			return;
		}
		int listW = narrow ? area.Width : Math.Clamp(area.Width * 30 / 100, 300, 400);
		Rectangle panel = new Rectangle(area.X, area.Y, listW, area.Height);
		_detail = new Rectangle(panel.Right, area.Y, area.Right - panel.Right, area.Height);
		_searchBox = new Rectangle(panel.X + 12, panel.Y + 12, panel.Width - 24, 44);
		_listArea = new Rectangle(panel.X + 6, _searchBox.Bottom + 10, panel.Width - 12, panel.Bottom - _searchBox.Bottom - 16);
		hits.Add(new Rectangle(area.X, area.Y, area.Width, area.Height), () => _search.Selected = false);
		hits.Add(_searchBox, () => _search.Selected = true);
		DrawList(b, panel, hits);
		if (!narrow)
		{
			DrawDetail(b, hits);
		}
	}

	private void DrawList(SpriteBatch b, Rectangle panel, Hits hits)
	{
		Hearth.Fill(b, panel, Hearth.Slip);
		Hearth.VLine(b, panel.Right - 2, panel.Y, panel.Height, Hearth.Seam);
		Hearth.Field(b, _searchBox, _search.Selected);
		Hearth.Magnifier(b, new Vector2(_searchBox.X + 20, _searchBox.Center.Y));
		string text = _search.Text ?? "";
		Vector2 tp = new Vector2(_searchBox.X + 40, _searchBox.Center.Y - 14);
		if (text.Length == 0 && !_search.Selected)
		{
			Hearth.Ink(b, Tr.Get("shell.villagers.search", "搜索村民"), tp, Hearth.QuillPale, 0.85f);
		}
		else
		{
			string shown = Hearth.Truncate(text, _searchBox.Width - 56, 0.85f);
			Hearth.Ink(b, shown, tp, Hearth.Quill, 0.85f);
			if (_search.Selected && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 550)
			{
				Hearth.Fill(b, new Rectangle((int)tp.X + (int)Hearth.Width(shown, 0.85f) + 2, _searchBox.Center.Y - 12, 2, 24), Hearth.Leaf);
			}
		}
		if (_shown.Count == 0)
		{
			Hearth.InkMiddle(b, _all.Count == 0 ? Tr.Get("shell.villagers.none", "你还没认识任何村民。") : Tr.Get("shell.villagers.noMatch", "没有找到这个人。"), new Rectangle(_listArea.X, _listArea.Y + 20, _listArea.Width, 60), Hearth.QuillSoft, 0.85f);
			return;
		}
		Hearth.BeginClip(b, _listArea);
		int y = _listArea.Y - _listScroll.Offset;
		int group = -1;
		const int rowH = 68;
		foreach (Villager v in _shown)
		{
			if (v.Group != group)
			{
				group = v.Group;
				int count = _shown.Count(x => x.Group == v.Group);
				Hearth.Ink(b, GroupTitle(group) + "  " + count, new Vector2(_listArea.X + 12, y + 8), Hearth.QuillPale, 0.72f);
				y += 34;
			}
			Rectangle row = new Rectangle(_listArea.X, y, _listArea.Width, rowH);
			if (row.Bottom >= _listArea.Y && row.Y <= _listArea.Bottom)
			{
				DrawRow(b, v, row);
				string name = v.Name;
				hits.Add(row, () =>
				{
					_reading = true;
					if (_selected != name)
					{
						Game1.playSound("smallSelect");
						_selected = name;
						_detailScroll.Reset();
					}
					_search.Selected = false;
				}, _listArea);
			}
			y += rowH + 2;
		}
		Hearth.EndClip(b);
		_listScroll.Finish(b, _listArea, y, 6);
	}

	private void DrawRow(SpriteBatch b, Villager v, Rectangle row)
	{
		if (v.Name == _selected)
		{
			Hearth.FillRounded(b, row, Hearth.Honey, 10);
			Hearth.Fill(b, new Rectangle(row.X, row.Y + 8, 4, row.Height - 16), Hearth.Leaf);
		}
		else if (Hearth.Hover(row, _listArea))
		{
			Hearth.FillRounded(b, row, Hearth.ParchmentDeep, 10);
		}
		Hearth.Cameo(b, new Rectangle(row.X + 12, row.Center.Y - 24, 48, 48), v.Npc);
		int tx = row.X + 72, right = row.Right - 12;
		string hearts = v.Hearts.ToString();
		float hw = Hearth.Width(hearts, 0.7f);
		Hearth.Ink(b, hearts, new Vector2(right - hw, row.Y + 14), Hearth.QuillSoft, 0.7f);
		Hearth.Heart(b, new Rectangle(right - (int)hw - 22, row.Y + 18, 16, 14));
		string name = Hearth.Truncate(v.Display, right - hw - 30 - tx, 0.95f);
		Hearth.Ink(b, name, new Vector2(tx, row.Y + 10), Hearth.Quill, 0.95f);
		if (v.Birthday)
		{
			Hearth.Tag(b, new Vector2(tx + Hearth.Width(name, 0.95f) + 8, row.Y + 10), Tr.Get("shell.villagers.birthdayChip", "生日"), Hearth.Rose, Color.White, 0.6f);
		}
		string where = v.Where.Length > 0 ? v.Where : Tr.Get("shell.villagers.nowhere", "不知道在哪");
		Hearth.Ink(b, Hearth.Truncate(v.Mood.Length > 0 ? where + " · " + v.Mood : where, right - tx, 0.72f), new Vector2(tx, row.Y + 40), Hearth.QuillPale, 0.72f);
	}

	private void DrawDetail(SpriteBatch b, Hits hits)
	{
		Villager? v = Selected;
		if (v == null)
		{
			Hearth.InkMiddle(b, Tr.Get("shell.villagers.pick", "选一位村民看看。"), _detail, Hearth.QuillSoft);
			return;
		}
		Hearth.BeginClip(b, _detail);
		int cardW = Math.Min(_detail.Width - 56, 760);
		int x0 = _detail.X + (_detail.Width - cardW) / 2;
		int y = _detail.Y + 24 - _detailScroll.Offset;
		y = DrawHead(b, v, x0, y, cardW, hits) + 16;
		y = DrawInfo(b, v, x0, y, cardW, hits) + 16;
		if (v.AiEnabled)
		{
			Rectangle diary = new Rectangle(x0, y, cardW, 52);
			Hearth.PressPlate(b, diary, Tr.Get("shell.villagers.diary", "翻开{{name}}的日记", ("name", v.Display)), hover: Hearth.Hover(diary, _detail), scale: 0.85f);
			string npc = v.Name;
			hits.Add(diary, () =>
			{
				_search.Selected = false;
				Game1.playSound("shwip");
				Host.Push(new VillagerDiaryBook(Ctx, npc));
			}, _detail);
			y = diary.Bottom + 16;
			foreach (Section s in Sections(v.Name))
			{
				y = DrawSection(b, s, x0, y, cardW) + 16;
			}
		}
		else
		{
			Hearth.Ink(b, Tr.Get("shell.villagers.notAi", "这位村民没有交给 AI，没有记忆和心情。"), new Vector2(x0 + 4, y), Hearth.QuillPale, 0.78f);
			y += 40;
		}
		Hearth.EndClip(b);
		_detailScroll.Finish(b, _detail, y + 8);
	}

	private int DrawHead(SpriteBatch b, Villager v, int x, int y, int w, Hits hits)
	{
		Rectangle head = new Rectangle(x, y, w, 168);
		Hearth.CardSlip(b, head);
		Rectangle portrait = new Rectangle(head.X + 20, head.Y + 20, 128, 128);
		Hearth.Cameo(b, portrait, v.Npc);
		int tx = portrait.Right + 24;
		// 名字太长时截短，给关系标签和右上角的铅笔留地方。
		string shownName = Hearth.Truncate(v.Display, Math.Max(80, head.Right - 76 - tx - Hearth.TagWidth(v.Relation, 0.7f) - 14), 1.3f);
		Hearth.InkBold(b, shownName, new Vector2(tx, head.Y + 26), Hearth.Quill, 1.3f);
		float nameW = Hearth.Width(shownName, 1.3f);
		Color rel = v.Group == 0 ? Hearth.Rose : v.Group == 1 ? Hearth.Leaf : Hearth.ParchmentDeep;
		Hearth.Tag(b, new Vector2(tx + nameW + 14, head.Y + 30), v.Relation, rel, v.Group <= 1 ? Color.White : Hearth.QuillSoft, 0.7f);
		int hy = head.Y + 26 + Hearth.LineHeight(1.3f) + 12;
		int size = Math.Min(26, (head.Right - 20 - tx) / (v.MaxHearts > 10 ? 14 : 10) - 4);
		Hearth.Hearts(b, new Vector2(tx, hy), v.Hearts, v.MaxHearts, size);
		string line = Tr.Get("shell.villagers.hearts", "{{n}} 心", ("n", v.Hearts)) + "  ·  " + (v.TalkedToday ? Tr.Get("shell.villagers.talked", "今天聊过了") : Tr.Get("shell.villagers.notTalked", "今天还没聊"));
		Hearth.Ink(b, line, new Vector2(tx, hy + size + 10), Hearth.QuillSoft, 0.78f);
		Rectangle edit = new Rectangle(head.Right - 60, head.Y + 16, 44, 44);
		bool hover = Hearth.Hover(edit, _detail);
		Hearth.Bordered(b, edit, hover ? Hearth.AmberPale : Hearth.Slip, hover ? Hearth.Amber : Hearth.SlipEdge, 12);
		CardEditorPage.Pencil.DrawIn(b, new Rectangle(edit.X + 8, edit.Y + 8, 28, 28));
		if (hover)
		{
			string tip = Tr.Get("shell.villagers.editCard", "改角色卡");
			Hearth.Ink(b, tip, new Vector2(edit.X - 8 - Hearth.Width(tip, 0.68f), edit.Center.Y - Hearth.LineHeight(0.68f) / 2), Hearth.QuillSoft, 0.68f);
		}
		string npc = v.Name;
		hits.Add(edit, () =>
		{
			_search.Selected = false;
			Host.Push(new CardEditorPage(Ctx, npc));
		}, _detail);
		if (Ctx.Errands is { CanPost: true } errands)
		{
			AISV.Core.World.Errands.Errand? open = errands.OpenFor(npc);
			string label = open == null ? Tr.Get("shell.villagers.errand", "托办件事") : Tr.Get("shell.villagers.errandOpen", "托的事：{{what}}", ("what", errands.What(open)));
			int bw = Math.Min((int)Hearth.Width(label, 0.72f) + 32, Math.Max(140, head.Right - 20 - (tx + (int)Hearth.Width(line, 0.78f) + 16)));
			Rectangle ask = new Rectangle(head.Right - 20 - bw, head.Bottom - 20 - 40, bw, 40);
			Hearth.PressPlate(b, ask, Hearth.Truncate(label, bw - 24, 0.72f), hover: Hearth.Hover(ask, _detail), scale: 0.72f);
			hits.Add(ask, () =>
			{
				_search.Selected = false;
				if (open == null)
				{
					Host.Push(new ErrandFormPage(Ctx, npc));
				}
				else
				{
					CommissionsPage.ShowMine = true;
					Host.Open(ShellMenu.Tab.World, world: ShellMenu.WorldTab.Commissions);
				}
			}, _detail);
		}
		return head.Bottom;
	}

	private int DrawInfo(SpriteBatch b, Villager v, int x, int y, int w, Hits hits)
	{
		AISV.Core.World.Ledger.NpcWallet? wallet = Ctx.Ledger is { Enabled: true } ledger && StardewModdingAPI.Context.IsMainPlayer ? ledger.Wallet(v.Name) : null;
		List<(string Label, string Value, Color Color)> rows = new List<(string, string, Color)>
		{
			(Tr.Get("shell.villagers.where", "现在在"), v.Where.Length > 0 ? v.Where : Tr.Get("shell.villagers.nowhere", "不知道在哪"), Hearth.Quill),
			(Tr.Get("shell.villagers.mood", "今天心情"), v.Mood.Length > 0 ? v.Mood + (v.MoodReason.Length > 0 ? "：" + v.MoodReason : "") : Tr.Get("shell.villagers.moodUnknown", "看不出来"), Hearth.Quill),
			(Tr.Get("shell.villagers.birthday", "生日"), v.Birthday ? v.BirthdayText + Tr.Get("shell.villagers.birthdayToday", "（就是今天）") : v.BirthdayText, v.Birthday ? Hearth.Rose : Hearth.Quill),
			(Tr.Get("shell.villagers.gifts", "本周送礼"), $"{v.GiftsWeek} / 2", Hearth.Quill)
		};
		if (wallet != null)
		{
			rows.Add((Tr.Get("shell.villagers.wallet", "钱包"), Tr.Get("shell.villagers.walletValue", "身上 {{gold}}g · 存款 {{savings}}g", ("gold", wallet.Gold.ToString("N0")), ("savings", wallet.Savings.ToString("N0"))), Hearth.Amber));
		}
		const int rowH = 52;
		Rectangle info = new Rectangle(x, y, w, rows.Count * rowH + 12);
		Hearth.CardSlip(b, info);
		for (int i = 0; i < rows.Count; i++)
		{
			int ry = info.Y + 6 + i * rowH;
			if (i > 0)
			{
				Hearth.HLine(b, info.X + 20, ry, info.Width - 40, Hearth.Seam);
			}
			Hearth.Ink(b, rows[i].Label, new Vector2(info.X + 24, ry + 14), Hearth.QuillSoft, 0.82f);
			bool walletRow = wallet != null && i == rows.Count - 1;
			Hearth.Ink(b, Hearth.Truncate(rows[i].Value, info.Width - 190 - (walletRow ? 110 : 0), 0.85f), new Vector2(info.X + 170, ry + 14), rows[i].Color, 0.85f);
			if (walletRow)
			{
				Rectangle open = new Rectangle(info.Right - 20 - 96, ry + 8, 96, 38);
				Hearth.PressPlate(b, open, Tr.Get("shell.villagers.belongings", "家当"), hover: Hearth.Hover(open, _detail), scale: 0.72f);
				string npc = v.Name;
				hits.Add(open, () =>
				{
					_search.Selected = false;
					Host.Push(new LedgerPage(Ctx, npc));
				}, _detail);
			}
		}
		return info.Bottom;
	}

	private List<Section> Sections(string npc)
	{
		GameDate today = GameState.Today;
		List<Section> list = new List<Section>();
		Section feel = new Section { Title = Tr.Get("shell.villagers.feelings", "对你的感觉") };
		EmotionalProfile? p = Ctx.Social.Feelings.Find(npc);
		if (p == null)
		{
			feel.Lines.Add((Tr.Get("shell.villagers.noFeelings", "还说不上来。多聊几次，每个星期天会重新想一想。"), Hearth.QuillPale));
		}
		else
		{
			feel.Bars.Add((Tr.Get("shell.villagers.trust", "信任"), p.FaithInFarmer, $"{p.FaithInFarmer:0.00}"));
			feel.Bars.Add((Tr.Get("shell.villagers.warmth", "感情"), (p.Affection + 1f) / 2f, $"{p.Affection:+0.00;-0.00;0.00}"));
			if (!string.IsNullOrWhiteSpace(p.Impression))
			{
				feel.Lines.Add((Tr.Get("shell.villagers.impression", "在{{npc}}眼里，你是：{{text}}", ("npc", Ctx.Display(npc)), ("text", p.Impression)), Hearth.Quill));
			}
			feel.Lines.AddRange(p.Recollections.Select(m => ("· " + m, Hearth.Quill)));
			feel.Lines.AddRange(p.Sores.Where(m => m.HealsOn >= today.TotalDays).Select(m => ("· " + Tr.Get("shell.villagers.hurt", "心里还有个疙瘩：{{text}}", ("text", m.Text)), Hearth.Rust)));
		}
		list.Add(feel);
		IReadOnlyList<RelationshipMilestone> milestones = Ctx.Social.Milestones.For(npc);
		if (milestones.Count > 0)
		{
			list.Add(new Section
			{
				Title = Tr.Get("shell.villagers.milestones", "一路走来"),
				Lines = milestones.Reverse().Take(8).Select(m => (ShellText.Date(m.Day, m.Season, m.Year) + "　" + MilestoneBook.Describe(m), Hearth.Quill)).ToList()
			});
		}
		IReadOnlyList<GiftRecord> gifts = Ctx.Social.Gifts.For(npc);
		if (gifts.Count > 0)
		{
			list.Add(new Section
			{
				Title = Tr.Get("shell.villagers.giftsGiven", "送过的礼物"),
				Lines = gifts.Reverse().Take(6).Select(g => (ShellText.Date(g.Day, g.Season, g.Year) + "　" + g.What + "（" + GiftBook.TasteWord(g.Liking) + (g.OnBirthday ? Tr.Get("shell.villagers.onBirthday", "，生日那天") : "") + "）", Hearth.Quill)).ToList()
			});
		}
		List<NpcSocialEdge> edges = Ctx.Social.Web.Edges.Where(e => (string.Equals(e.First, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Second, npc, StringComparison.OrdinalIgnoreCase)) && e.Encounters > 0).OrderByDescending(e => e.Closeness).Take(8).ToList();
		if (edges.Count > 0)
		{
			list.Add(new Section
			{
				Title = Tr.Get("shell.villagers.others", "和其他村民"),
				Lines = edges.Select(e =>
				{
					string other = string.Equals(e.First, npc, StringComparison.OrdinalIgnoreCase) ? e.Second : e.First;
					NpcSocialMemory? latest = e.Shared.OrderByDescending(m => m.OnDay).FirstOrDefault();
					return (Ctx.Display(other) + "：" + SocialWeb.StateWord(e.Standing) + (latest == null ? "" : "——" + latest.Text), Hearth.Quill);
				}).ToList()
			});
		}
		IReadOnlyList<ConversationEntry> recent = Ctx.Conversations.Recent(npc, 3);
		if (recent.Count > 0)
		{
			Section talk = new Section { Title = Tr.Get("shell.villagers.recent", "最近的对话") };
			foreach (ConversationEntry e in recent.Reverse())
			{
				talk.Lines.Add((ShellText.Date(e.Day, e.Season, e.Year), Hearth.QuillPale));
				talk.Lines.Add((Ctx.Display(npc) + "：" + GameDialogueText.ForDisplay(e.Opener, Game1.player?.Name ?? ""), Hearth.Quill));
				if (!string.IsNullOrWhiteSpace(e.FarmerReply))
				{
					string said = SkippedReply.Is(e.FarmerReply) ? Tr.Get("shell.villagers.skipped", "（没接话）") : e.FarmerReply;
					talk.Lines.Add((Tr.Get("shell.villagers.you", "你：{{text}}", ("text", said)), Hearth.LeafDeep));
				}
			}
			list.Add(talk);
		}
		return list;
	}

	private int DrawSection(SpriteBatch b, Section s, int x, int y, int w)
	{
		const float scale = 0.85f;
		int lh = Hearth.LineHeight(scale);
		List<(string Text, Color Color)> lines = s.Lines.SelectMany(l => Hearth.WrapLines(ShellText.Clean(l.Text), w - 48, scale).Select(t => (t, l.Color))).ToList();
		int h = 18 + 34 + s.Bars.Count * 36 + lines.Count * lh + 18;
		Rectangle card = new Rectangle(x, y, w, h);
		Hearth.CardSlip(b, card);
		Kit.SectionTitle(b, s.Title, x + 20, y + 18);
		int cy = y + 56;
		foreach ((string label, float fraction, string note) in s.Bars)
		{
			Hearth.Ink(b, label, new Vector2(x + 24, cy), Hearth.QuillSoft, 0.82f);
			Rectangle bar = new Rectangle(x + 110, cy + 9, Math.Min(320, w / 2), 12);
			Hearth.Gauge(b, bar, fraction);
			Hearth.Ink(b, note, new Vector2(bar.Right + 16, cy), Hearth.Quill, 0.82f);
			cy += 36;
		}
		foreach ((string text, Color color) in lines)
		{
			Hearth.Ink(b, text, new Vector2(x + 24, cy), color, scale);
			cy += lh;
		}
		return card.Bottom;
	}
}
