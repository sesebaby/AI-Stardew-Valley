using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>村庄关系网：左边村民名单（最上面是"焦点"），右边选中的人在中间、和谁有关系连一圈线，点一条线看两人之间的事。</summary>
internal sealed class VillageMapPage : ShellPage
{
	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _overviewScroll = new Scroller();

	private MapModel _model = new MapModel();

	/// <summary>为空时显示焦点。</summary>
	private string? _person;

	private string? _other;

	private Rectangle _list;

	private Rectangle _right;

	public VillageMapPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("web.title", "村庄关系网");

	public override void Reload()
	{
		_model = AppData.Map(Ctx);
		if (_person != null && !_model.People.Contains(_person, StringComparer.OrdinalIgnoreCase))
		{
			_person = null;
		}
	}

	public override void Scroll(int direction)
	{
		if (AisvTheme.Hover(_list))
		{
			_listScroll.Wheel(direction);
		}
		else if (_person == null)
		{
			_overviewScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		if (key is Keys.Up or Keys.Down && _model.People.Count > 0)
		{
			int i = _person == null ? -1 : _model.People.ToList().FindIndex(p => Same(p, _person));
			i = Math.Clamp(i + (key == Keys.Down ? 1 : -1), -1, _model.People.Count - 1);
			Pick(i < 0 ? null : _model.People[i]);
			return true;
		}
		return false;
	}

	private void Pick(string? person)
	{
		_person = person;
		_other = person == null ? null : VillageMap.TiesOf(_model, person).FirstOrDefault()?.Other(person);
		Game1.playSound("smallSelect");
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_model.IsEmpty)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("web.empty", "村民之间还没什么来往"), Tr.Get("web.emptyHint", "在镇上过几天，等他们碰过几次面再来看。"));
			return;
		}
		_list = new Rectangle(area.X + 12, area.Y + 12, 290, area.Height - 24);
		_right = new Rectangle(_list.Right + 16, area.Y + 12, area.Right - 12 - _list.Right - 16, area.Height - 24);
		List(b, hits);
		if (_person == null)
		{
			Overview(b, hits);
		}
		else
		{
			Person(b, _person, hits);
		}
	}

	private void List(SpriteBatch b, Hits hits)
	{
		AisvTheme.Bordered(b, _list, AisvTheme.PageDeep, AisvTheme.Divider, 12);
		Rectangle view = AisvTheme.Inflate(_list, -6);
		AisvTheme.BeginClip(b, view);
		int y = view.Y + 4 - _listScroll.Offset;
		Rectangle focus = new Rectangle(view.X + 4, y, view.Width - 18, 60);
		Row(b, focus, null, Tr.Get("web.focus", "焦点"), Tr.Get("web.focusSub", "最亲和最僵的几对"), hits, view);
		y = focus.Bottom + 10;
		AisvTheme.HLine(b, view.X + 14, y, view.Width - 40, AisvTheme.Divider);
		y += 12;
		foreach (string p in _model.People)
		{
			Rectangle r = new Rectangle(view.X + 4, y, view.Width - 18, 60);
			if (r.Bottom >= view.Y && r.Y <= view.Bottom)
			{
				int n = _model.Ties.Count(t => t.Involves(p));
				Row(b, r, p, Ctx.Display(p), Tr.Get("web.ties", "牵着 {{count}} 个人", ("count", n)), hits, view);
			}
			y += 64;
		}
		AisvTheme.EndClip(b);
		_listScroll.Finish(b, view, y, 6);
	}

	private void Row(SpriteBatch b, Rectangle r, string? npc, string name, string sub, Hits hits, Rectangle clip)
	{
		bool selected = Same(npc, _person);
		if (selected)
		{
			AisvTheme.FillRounded(b, r, AisvTheme.Card, 10);
			AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y + 12, 4, r.Height - 24), AisvTheme.Accent, 2);
		}
		else if (AisvTheme.Hover(r, clip))
		{
			AisvTheme.FillRounded(b, r, AisvTheme.Card * 0.6f, 10);
		}
		Rectangle icon = new Rectangle(r.X + 12, r.Y + 8, 44, 44);
		if (npc == null)
		{
			AisvTheme.FillRounded(b, icon, new Color(252, 230, 238), 10);
			AppIcons.Web.Draw(b, icon.X + 6, icon.Y + 6, 2);
		}
		else
		{
			Kit.Avatar(b, icon, Ctx, npc);
		}
		AisvTheme.Bold(b, AisvTheme.Truncate(name, r.Right - icon.Right - 22, 0.82f), new Vector2(icon.Right + 12, r.Y + 8), AisvTheme.Text, 0.82f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(sub, r.Right - icon.Right - 22, 0.66f), new Vector2(icon.Right + 12, r.Y + 34), AisvTheme.TextFaint, 0.66f);
		hits.Add(r, () => Pick(npc), clip);
	}

	private void Overview(SpriteBatch b, Hits hits)
	{
		AisvTheme.BeginClip(b, _right);
		int x = _right.X + 12, w = _right.Width - 36;
		int y = _right.Y + 8 - _overviewScroll.Offset;
		AisvTheme.DrawText(b, Tr.Get("web.caption", "亲疏远近、恩怨暗恋，都画在这张图上。"), new Vector2(x, y), AisvTheme.TextMuted, 0.78f);
		y += 44;
		y = Section(b, Tr.Get("web.topBonds", "最要好的"), _model.TopBonds, Tr.Get("web.noBonds", "还没有特别要好的朋友或情侣。"), x, y, w, hits);
		y += 18;
		y = Section(b, Tr.Get("web.topFeuds", "最僵的"), _model.TopFeuds, Tr.Get("web.noFeuds", "镇上一团和气，眼下没人闹别扭。"), x, y, w, hits);
		AisvTheme.EndClip(b);
		_overviewScroll.Finish(b, _right, y, 4);
	}

	private int Section(SpriteBatch b, string title, IReadOnlyList<Tie> ties, string empty, int x, int y, int w, Hits hits)
	{
		Kit.SectionTitle(b, title, x, y);
		y += 40;
		if (ties.Count == 0)
		{
			Rectangle r = new Rectangle(x, y, w, 64);
			AisvTheme.FillRounded(b, r, AisvTheme.PageDeep, 12);
			AisvTheme.TextCentered(b, empty, r, AisvTheme.TextFaint, 0.76f);
			return r.Bottom;
		}
		foreach (Tie t in ties)
		{
			Rectangle r = new Rectangle(x, y, w, 112);
			PairCard(b, r, t, hits);
			y = r.Bottom + 12;
		}
		return y;
	}

	private void PairCard(SpriteBatch b, Rectangle r, Tie t, Hits hits)
	{
		Color c = BondColor(t.Bond);
		bool hover = AisvTheme.Hover(r, _right);
		AisvTheme.Bordered(b, r, AisvTheme.Card, hover ? c : AisvTheme.CardBorder, 14);
		Rectangle a = new Rectangle(r.X + 20, r.Y + 20, 72, 72), bb = new Rectangle(r.X + 20 + 72 + 80, r.Y + 20, 72, 72);
		AisvTheme.Line(b, new Vector2(a.Right + 4, a.Center.Y), new Vector2(bb.X - 4, bb.Center.Y), c, 4f);
		Rectangle knot = new Rectangle((a.Right + bb.X) / 2 - 12, a.Center.Y - 12, 24, 24);
		AisvTheme.FillRounded(b, knot, c, 12);
		if (VillageMap.IsRomantic(t.Bond))
		{
			AisvTheme.Heart(b, new Rectangle(knot.X + 5, knot.Y + 6, 14, 12));
		}
		Kit.Avatar(b, a, Ctx, t.A);
		Kit.Avatar(b, bb, Ctx, t.B);
		int tx = bb.Right + 28;
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(t.A) + " · " + Ctx.Display(t.B), r.Right - tx - 160, 0.88f), new Vector2(tx, r.Y + 18), AisvTheme.Text, 0.88f);
		AisvTheme.Chip(b, new Vector2(tx, r.Y + 54), BondName(t.Bond), Color.Lerp(c, Color.White, 0.8f), Color.Lerp(c, AisvTheme.Text, 0.4f), 0.7f);
		string memory = t.Memories.FirstOrDefault()?.Summary ?? BondHint(t.Bond);
		int chipW = AisvTheme.ChipWidth(BondName(t.Bond), 0.7f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(ShellText.Clean(memory), r.Right - tx - chipW - 30, 0.68f), new Vector2(tx + chipW + 12, r.Y + 58), AisvTheme.TextMuted, 0.68f);
		string pct = VillageMap.IsFeud(t.Bond) ? Tr.Get("web.tensionPct", "紧张 {{value}}%", ("value", (int)Math.Round(t.Tension * 100))) : Tr.Get("web.warmthPct", "亲近 {{value}}%", ("value", (int)Math.Round(t.Warmth * 100)));
		AisvTheme.DrawText(b, pct, new Vector2(r.Right - 24 - AisvTheme.Width(pct, 0.72f), r.Y + 20), c, 0.72f);
		hits.Add(r, () =>
		{
			Game1.playSound("smallSelect");
			_person = t.A;
			_other = t.B;
		}, _right);
	}

	private void Person(SpriteBatch b, string npc, Hits hits)
	{
		IReadOnlyList<Tie> ties = VillageMap.TiesOf(_model, npc);
		int detailW = Math.Clamp(_right.Width * 2 / 5, 340, 460);
		Rectangle graph = new Rectangle(_right.X, _right.Y, _right.Width - detailW - 16, _right.Height);
		Rectangle detail = new Rectangle(graph.Right + 16, _right.Y, detailW, _right.Height);
		AisvTheme.Bordered(b, graph, new Color(250, 247, 238), AisvTheme.Divider, 14);
		Graph(b, graph, npc, ties, hits);
		Tie? sel = ties.FirstOrDefault(t => Same(t.Other(npc), _other)) ?? ties.FirstOrDefault();
		if (sel != null)
		{
			Detail(b, detail, npc, sel);
		}
		else
		{
			AisvTheme.DrawCard(b, detail);
			AisvTheme.TextCentered(b, Tr.Get("web.noTies", "还没和谁有来往。"), detail, AisvTheme.TextFaint, 0.8f);
		}
	}

	private void Graph(SpriteBatch b, Rectangle r, string npc, IReadOnlyList<Tie> ties, Hits hits)
	{
		Point c = r.Center;
		int n = ties.Count;
		int node = n <= 10 ? 60 : n <= 18 ? 48 : 38;
		int radius = Math.Max(80, Math.Min(r.Width, r.Height) / 2 - node / 2 - 40);
		List<(Tie Tie, Rectangle Box)> nodes = new List<(Tie, Rectangle)>();
		for (int i = 0; i < n; i++)
		{
			double angle = -Math.PI / 2 + i * 2 * Math.PI / Math.Max(1, n);
			int nx = c.X + (int)(Math.Cos(angle) * radius), ny = c.Y + (int)(Math.Sin(angle) * radius);
			nodes.Add((ties[i], new Rectangle(nx - node / 2, ny - node / 2, node, node)));
		}
		foreach ((Tie t, Rectangle box) in nodes)
		{
			bool sel = Same(t.Other(npc), _other);
			Color color = BondColor(t.Bond) * (sel ? 1f : 0.7f);
			float thick = 2f + t.Intensity * 5f + (sel ? 2f : 0f);
			AisvTheme.Line(b, new Vector2(c.X, c.Y), new Vector2(box.Center.X, box.Center.Y), color, thick);
		}
		Rectangle center = new Rectangle(c.X - 50, c.Y - 50, 100, 100);
		AisvTheme.FillRounded(b, AisvTheme.Inflate(center, 6), AisvTheme.Card, 16);
		Kit.Avatar(b, center, Ctx, npc);
		string cn = Ctx.Display(npc);
		Rectangle tag = new Rectangle(c.X - (int)AisvTheme.Width(cn, 0.8f) / 2 - 12, center.Bottom + 8, (int)AisvTheme.Width(cn, 0.8f) + 24, 32);
		AisvTheme.FillRounded(b, tag, AisvTheme.Text * 0.85f, 16);
		AisvTheme.TextCentered(b, cn, tag, Color.White, 0.8f);
		foreach ((Tie t, Rectangle box) in nodes)
		{
			string other = t.Other(npc);
			bool sel = Same(other, _other);
			Color color = BondColor(t.Bond);
			AisvTheme.FillRounded(b, AisvTheme.Inflate(box, sel ? 5 : 3), sel ? color : AisvTheme.Card, 12);
			Kit.Avatar(b, box, Ctx, other);
			string name = AisvTheme.Truncate(Ctx.Display(other), node + 50, 0.62f);
			float nw = AisvTheme.Width(name, 0.62f);
			AisvTheme.DrawText(b, name, new Vector2(box.Center.X - nw / 2f, box.Bottom + 6), sel ? AisvTheme.Text : AisvTheme.TextMuted, 0.62f);
			hits.Add(AisvTheme.Inflate(box, 6), () =>
			{
				Game1.playSound("smallSelect");
				_other = other;
			});
		}
		// 图例。
		int lx = r.X + 16, ly = r.Bottom - 34;
		foreach (Bond bond in ties.Select(t => t.Bond).Distinct().OrderBy(x => x))
		{
			string label = BondName(bond);
			AisvTheme.FillRounded(b, new Rectangle(lx, ly + 7, 12, 12), BondColor(bond), 6);
			AisvTheme.DrawText(b, label, new Vector2(lx + 18, ly), AisvTheme.TextMuted, 0.62f);
			lx += 18 + (int)AisvTheme.Width(label, 0.62f) + 18;
			if (lx > r.Right - 80)
			{
				break;
			}
		}
	}

	private void Detail(SpriteBatch b, Rectangle r, string npc, Tie t)
	{
		Color c = BondColor(t.Bond);
		AisvTheme.DrawCard(b, r);
		Rectangle head = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 120);
		AisvTheme.FillRounded(b, head, Color.Lerp(c, Color.White, 0.84f), 12);
		AisvTheme.Fill(b, new Rectangle(head.X, head.Bottom - 12, head.Width, 12), Color.Lerp(c, Color.White, 0.84f));
		string other = t.Other(npc);
		Rectangle a = new Rectangle(r.X + 22, r.Y + 22, 64, 64), o = new Rectangle(a.Right + 40, r.Y + 22, 64, 64);
		AisvTheme.Line(b, new Vector2(a.Right, a.Center.Y), new Vector2(o.X, o.Center.Y), c, 4f);
		Kit.Avatar(b, a, Ctx, npc);
		Kit.Avatar(b, o, Ctx, other);
		int tx = o.Right + 18;
		AisvTheme.Bold(b, AisvTheme.Truncate(Ctx.Display(other), r.Right - tx - 16, 0.95f), new Vector2(tx, r.Y + 24), AisvTheme.Text, 0.95f);
		AisvTheme.Chip(b, new Vector2(tx, r.Y + 60), BondName(t.Bond), c, Color.White, 0.7f);
		int x = r.X + 24, w = r.Width - 48;
		AisvTheme.BeginClip(b, new Rectangle(r.X, head.Bottom, r.Width, r.Bottom - head.Bottom - 4));
		int y = head.Bottom + 16;
		foreach (string line in AisvTheme.WrapLines(BondHint(t.Bond), w, 0.78f))
		{
			AisvTheme.DrawText(b, line, new Vector2(x, y), AisvTheme.TextMuted, 0.78f);
			y += AisvTheme.LineHeight(0.78f);
		}
		y += 14;
		Meter(b, x, ref y, w, Tr.Get("web.affinity", "好感"), t.Affinity, new Color(214, 104, 140));
		Meter(b, x, ref y, w, Tr.Get("web.trust", "信任"), t.Trust, new Color(54, 150, 140));
		Meter(b, x, ref y, w, Tr.Get("web.tension", "紧张"), t.Tension, AisvTheme.Danger);
		string meta = Tr.Get("web.encounters", "碰过 {{count}} 次面", ("count", t.Encounters));
		if (t.LastTotalDay > 0)
		{
			int ago = Math.Max(0, AppData.Today - t.LastTotalDay);
			meta += " · " + (ago == 0 ? Tr.Get("web.today", "今天") : ago == 1 ? Tr.Get("web.yesterday", "昨天") : Tr.Get("web.daysAgo", "{{count}} 天前", ("count", ago)));
		}
		AisvTheme.DrawText(b, meta, new Vector2(x, y + 2), AisvTheme.TextFaint, 0.68f);
		y += 40;
		Kit.SectionTitle(b, Tr.Get("web.memories", "一起经历的事"), x, y);
		y += 36;
		if (t.Memories.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("web.noMemories", "还没有一起经历过什么。"), new Vector2(x, y), AisvTheme.TextFaint, 0.72f);
		}
		foreach (PairMemory m in t.Memories)
		{
			Color tone = VillageMap.ToneOf(m.Tone) switch
			{
				MemoryTone.Warm => new Color(214, 104, 140),
				MemoryTone.Playful => AisvTheme.Gold,
				MemoryTone.Tense => AisvTheme.Danger,
				_ => AisvTheme.TextFaint
			};
			List<string> lines = AisvTheme.WrapLines(ShellText.Clean(m.Summary), w - 24, 0.72f);
			AisvTheme.FillRounded(b, new Rectangle(x, y + 2, 4, lines.Count * AisvTheme.LineHeight(0.72f) - 4), tone, 2);
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(x + 16, y), AisvTheme.Text, 0.72f);
				y += AisvTheme.LineHeight(0.72f);
			}
			y += 10;
		}
		AisvTheme.EndClip(b);
	}

	private static void Meter(SpriteBatch b, int x, ref int y, int w, string label, float value, Color color)
	{
		AisvTheme.DrawText(b, label, new Vector2(x, y), AisvTheme.Text, 0.72f);
		string pct = (int)Math.Round(value * 100) + "%";
		AisvTheme.DrawText(b, pct, new Vector2(x + w - AisvTheme.Width(pct, 0.68f), y), AisvTheme.TextMuted, 0.68f);
		Rectangle track = new Rectangle(x + 70, y + 9, w - 70 - 60, 12);
		AisvTheme.FillRounded(b, track, AisvTheme.Divider, 6);
		AisvTheme.FillRounded(b, new Rectangle(track.X, track.Y, Math.Max(12, (int)(track.Width * value)), track.Height), color, 6);
		y += 36;
	}

	public static Color BondColor(Bond b) => b switch
	{
		Bond.Couple => new Color(214, 84, 130),
		Bond.Crush => new Color(236, 140, 176),
		Bond.BestFriends => AisvTheme.AccentDark,
		Bond.OddFriends => new Color(146, 100, 200),
		Bond.Friends => new Color(112, 172, 80),
		Bond.Acquaintances => new Color(172, 160, 140),
		Bond.Awkward => new Color(222, 166, 70),
		Bond.Tense => AisvTheme.Warn,
		Bond.Rivals => AisvTheme.Danger,
		_ => AisvTheme.TextFaint
	};

		private static readonly Dictionary<Bond, (Func<string> Name, Func<string> Hint)> BondWords = new Dictionary<Bond, (Func<string>, Func<string>)>
	{
		[Bond.Couple] = (() => Tr.Get("web.bond.couple", "情侣"), () => Tr.Get("web.hint.couple", "一对恋人，全镇都知道。")),
		[Bond.Crush] = (() => Tr.Get("web.bond.crush", "暗生情愫"), () => Tr.Get("web.hint.crush", "两人之间有点说不清的意思……")),
		[Bond.BestFriends] = (() => Tr.Get("web.bond.best", "至交"), () => Tr.Get("web.hint.best", "又亲近又信得过，有事一定互相帮。")),
		[Bond.OddFriends] = (() => Tr.Get("web.bond.odd", "奇妙的友谊"), () => Tr.Get("web.hint.odd", "谁都没想到这两人会走到一块。")),
		[Bond.Friends] = (() => Tr.Get("web.bond.friends", "朋友"), () => Tr.Get("web.hint.friends", "处得挺好。")),
		[Bond.Acquaintances] = (() => Tr.Get("web.bond.acquaintances", "点头之交"), () => Tr.Get("web.hint.acquaintances", "见面打个招呼，也就这样。")),
		[Bond.Awkward] = (() => Tr.Get("web.bond.awkward", "有点别扭"), () => Tr.Get("web.hint.awkward", "最近见面有点不自在。")),
		[Bond.Tense] = (() => Tr.Get("web.bond.tense", "关系紧张"), () => Tr.Get("web.hint.tense", "两人眼下有点疙瘩。")),
		[Bond.Rivals] = (() => Tr.Get("web.bond.rivals", "死对头"), () => Tr.Get("web.hint.rivals", "较劲较到全镇都看得出来。")),
		[Bond.Strangers] = (() => Tr.Get("web.bond.strangers", "不太熟"), () => Tr.Get("web.hint.strangers", "几乎没打过交道。"))
	};

	public static string BondName(Bond b) => BondWords.TryGetValue(b, out var words) ? words.Name() : BondWords[Bond.Strangers].Name();

	private static string BondHint(Bond b) => BondWords.TryGetValue(b, out var words) ? words.Hint() : BondWords[Bond.Strangers].Hint();

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
