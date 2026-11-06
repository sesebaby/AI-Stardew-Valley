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

/// <summary>左栏列出八卦，右栏是原话、传开后的版本和变味多少，下面画传播路线；点人名高亮他听到这话的那条路。</summary>
internal sealed class GossipTreePage : ShellPage
{
	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _treeScroll = new Scroller();

	private List<GossipCase> _cases = new List<GossipCase>();

	private int _index;

	private string? _focus;

	private bool _loaded;

	private Rectangle _list;

	private Rectangle _tree;

	public GossipTreePage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("gtree.title", "八卦树");

	public override void Reload()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		_cases = AppData.Gossip(Ctx);
		_index = 0;
	}

	public override void Scroll(int direction)
	{
		if (Hearth.Hover(_list))
		{
			_listScroll.Wheel(direction);
		}
		else
		{
			_treeScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		if (key is Keys.Up or Keys.Down && _cases.Count > 0)
		{
			Select(Math.Clamp(_index + (key == Keys.Down ? 1 : -1), 0, _cases.Count - 1));
			return true;
		}
		return _treeScroll.PageKeys(key, _tree.Height - 60);
	}

	private void Select(int i)
	{
		if (i != _index)
		{
			_index = i;
			_focus = null;
			_treeScroll.Reset();
			Game1.playSound("smallSelect");
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_cases.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("gtree.empty", "山谷里还没人说八卦"), Tr.Get("gtree.emptyHint", "过一两天，等村里有了新鲜事再来看。"));
			return;
		}
		if (Flip.Applies(area))
		{
			_flip.Draw(b, area, hits, r =>
			{
				_list = Hearth.Inflate(r, -12);
				List(b, hits);
			}, r =>
			{
				_list = Rectangle.Empty;
				Story(b, Hearth.Inflate(r, -12), _cases[_index], hits);
			});
			return;
		}
		_list = new Rectangle(area.X + 12, area.Y + 12, Math.Clamp(area.Width / 3, 300, 400), area.Height - 24);
		List(b, hits);
		Rectangle right = new Rectangle(_list.Right + 18, area.Y + 12, area.Right - 12 - _list.Right - 18, area.Height - 24);
		Story(b, right, _cases[_index], hits);
	}

	private readonly Flip _flip = new Flip();

	public override bool StepBack() => _flip.StepBack();

	private void List(SpriteBatch b, Hits hits)
	{
		Hearth.Bordered(b, _list, Hearth.ParchmentDeep, Hearth.Seam, 12);
		Rectangle view = Hearth.Inflate(_list, -6);
		Hearth.BeginClip(b, view);
		int y = view.Y + 4 - _listScroll.Offset;
		for (int i = 0; i < _cases.Count; i++)
		{
			GossipCase g = _cases[i];
			string headline = g.Told.Length > 0 ? g.Told : g.Fact;
			List<string> lines = Hearth.WrapLines(headline, view.Width - 46, 0.74f).Take(2).ToList();
			int h = 44 + lines.Count * Hearth.LineHeight(0.74f) + 10;
			Rectangle r = new Rectangle(view.X + 4, y, view.Width - 18, h);
			if (r.Bottom >= view.Y && r.Y <= view.Bottom)
			{
				bool sel = i == _index;
				if (sel)
				{
					Hearth.FillRounded(b, r, Hearth.Slip, 10);
					Hearth.FillRounded(b, new Rectangle(r.X, r.Y + 10, 4, r.Height - 20), Hearth.Leaf, 2);
				}
				else if (Hearth.Hover(r, view))
				{
					Hearth.FillRounded(b, r, Hearth.Slip * 0.6f, 10);
				}
				string date = ShellText.Date(g.Entry.Day, g.Entry.Season, g.Entry.Year);
				Hearth.Ink(b, date, new Vector2(r.X + 16, r.Y + 10), Hearth.QuillPale, 0.62f);
				string known = Tr.Get("gtree.knownBy", "传到了 {{count}} 人耳朵里", ("count", g.Trail.KnownBy));
				Color pip = FidelityColor(GossipTrail.FidelityOf(g.Trail.MaxDrift));
				float kw = Hearth.Width(known, 0.62f);
				Hearth.Ink(b, known, new Vector2(r.Right - 14 - kw, r.Y + 10), Hearth.QuillSoft, 0.62f);
				Hearth.FillRounded(b, new Rectangle((int)(r.Right - 14 - kw - 16), r.Y + 15, 10, 10), pip, 5);
				int ly = r.Y + 36;
				foreach (string line in lines)
				{
					Hearth.Ink(b, line, new Vector2(r.X + 16, ly), sel ? Hearth.Quill : Hearth.QuillSoft, 0.74f);
					ly += Hearth.LineHeight(0.74f);
				}
				int k = i;
				hits.Add(r, () => Select(k), view);
			}
			y += h + 6;
		}
		Hearth.EndClip(b);
		_listScroll.Finish(b, view, y, 6);
	}

	private void Story(SpriteBatch b, Rectangle r, GossipCase g, Hits hits)
	{
		int x = r.X, w = r.Width;
		int y = r.Y;
		// 事实（像一张便签）。
		List<string> fact = Hearth.WrapLines(g.Fact, w - 60, 0.8f).Take(4).ToList();
		Rectangle note = new Rectangle(x, y, w, 44 + fact.Count * Hearth.LineHeight(0.8f) + 16);
		Hearth.Bordered(b, note, new Color(255, 250, 230), new Color(232, 214, 160), 12);
		Hearth.InkBold(b, Tr.Get("gtree.fact", "发生了什么"), new Vector2(note.X + 24, note.Y + 14), Hearth.Amber, 0.75f);
		int ly = note.Y + 44;
		foreach (string line in fact)
		{
			Hearth.Ink(b, line, new Vector2(note.X + 24, ly), Hearth.Quill, 0.8f);
			ly += Hearth.LineHeight(0.8f);
		}
		y = note.Bottom + 12;
		if (g.Told.Length > 0)
		{
			List<string> told = Hearth.WrapLines("“" + g.Told + "”", w - 60, 0.76f).Take(3).ToList();
			Rectangle bubble = new Rectangle(x, y, w, 40 + told.Count * Hearth.LineHeight(0.76f) + 14);
			Hearth.Bordered(b, bubble, Hearth.RosePale, Hearth.Rose * 0.5f, 12);
			Hearth.InkBold(b, Tr.Get("gtree.told", "镇上是这么传的"), new Vector2(bubble.X + 24, bubble.Y + 12), Hearth.Rose, 0.72f);
			ly = bubble.Y + 40;
			foreach (string line in told)
			{
				Hearth.Ink(b, line, new Vector2(bubble.X + 24, ly), Hearth.Quill, 0.76f);
				ly += Hearth.LineHeight(0.76f);
			}
			y = bubble.Bottom + 12;
		}
		y = Meter(b, g.Trail, x, y, w) + 10;
		_tree = new Rectangle(x, y, w, r.Bottom - y);
		Tree(b, g.Trail, hits);
	}

	/// <summary>走样表：绿→金→红的条，标出传得最走样的那个版本。</summary>
	private int Meter(SpriteBatch b, Trail t, int x, int y, int w)
	{
		string spread = t.Steps == 0 ? Tr.Get("gtree.sameDay", "{{count}} 人知道，都是当天听说的", ("count", t.KnownBy)) : Tr.Get("gtree.days", "{{count}} 人听过，走了 {{days}} 天", ("count", t.KnownBy), ("days", t.Steps));
		Hearth.Ink(b, spread, new Vector2(x + 4, y), Hearth.QuillSoft, 0.72f);
		string hint = Tr.Get("gtree.meterHint", "最走样的版本离事实有多远");
		Hearth.Ink(b, hint, new Vector2(x + w - Hearth.Width(hint, 0.62f) - 4, y + 4), Hearth.QuillPale, 0.62f);
		Rectangle bar = new Rectangle(x + 4, y + 34, w - 8, 14);
		const int steps = 48;
		for (int i = 0; i < steps; i++)
		{
			float f = i / (float)(steps - 1);
			Color c = f < 0.5f ? Color.Lerp(FidelityColor(Fidelity.Faithful), FidelityColor(Fidelity.Stretched), f * 2) : Color.Lerp(FidelityColor(Fidelity.Stretched), FidelityColor(Fidelity.Distorted), (f - 0.5f) * 2);
			Hearth.Fill(b, new Rectangle(bar.X + bar.Width * i / steps, bar.Y, bar.Width / steps + 1, bar.Height), c * 0.85f);
		}
		int mx = bar.X + (int)(bar.Width * Math.Clamp(t.MaxDrift, 0f, 1f));
		Hearth.Bordered(b, new Rectangle(mx - 9, bar.Center.Y - 13, 18, 26), Hearth.Slip, Hearth.Quill, 6, 3);
		string[] labels = { FidelityName(Fidelity.Faithful), FidelityName(Fidelity.Stretched), FidelityName(Fidelity.Distorted) };
		Hearth.Ink(b, labels[0], new Vector2(bar.X, bar.Bottom + 6), Hearth.QuillPale, 0.6f);
		Hearth.Ink(b, labels[1], new Vector2(bar.Center.X - Hearth.Width(labels[1], 0.6f) / 2f, bar.Bottom + 6), Hearth.QuillPale, 0.6f);
		Hearth.Ink(b, labels[2], new Vector2(bar.Right - Hearth.Width(labels[2], 0.6f), bar.Bottom + 6), Hearth.QuillPale, 0.6f);
		return bar.Bottom + 34;
	}

	private void Tree(SpriteBatch b, Trail t, Hits hits)
	{
		Hearth.Bordered(b, _tree, Hearth.Slip, Hearth.SlipEdge, 12);
		Rectangle view = new Rectangle(_tree.X + 4, _tree.Y + 4, _tree.Width - 8, _tree.Height - 8);
		TrailNode? focus = _focus == null ? null : t.Find(_focus);
		HashSet<TrailNode> path = new HashSet<TrailNode>(focus?.PathFromRoot() ?? Array.Empty<TrailNode>());
		Hearth.BeginClip(b, view);
		int y = view.Y + 12 - _treeScroll.Offset;
		Hearth.InkBold(b, Tr.Get("gtree.tree", "谁告诉了谁"), new Vector2(view.X + 20, y), Hearth.LeafDeep, 0.8f);
		y += 34;
		if (focus != null && focus.Teller != null)
		{
			string chain = string.Join(" › ", focus.PathFromRoot().Select(n => n.Npc.Length > 0 ? Ctx.Display(n.Npc) : Tr.Get("gtree.village", "全村")));
			foreach (string line in Hearth.WrapLines(Tr.Get("gtree.chain", "传话的路：{{chain}}", ("chain", chain)), view.Width - 40, 0.7f))
			{
				Hearth.Ink(b, line, new Vector2(view.X + 20, y), Hearth.Leaf, 0.7f);
				y += Hearth.LineHeight(0.7f);
			}
			y += 6;
		}
		const int rowH = 60, indent = 46, av = 40;
		Dictionary<TrailNode, Point> anchors = new Dictionary<TrailNode, Point>();
		int top = y;
		foreach (TrailNode n in t.Rows)
		{
			int nx = view.X + 20 + n.Depth * indent;
			anchors[n] = new Point(nx + av / 2, y + 8 + av);
			y += rowH;
		}
		// 先画连线。
		y = top;
		foreach (TrailNode n in t.Rows)
		{
			if (n.Teller != null && anchors.TryGetValue(n.Teller, out Point from))
			{
				int nx = view.X + 20 + n.Depth * indent;
				int cy = y + 8 + av / 2;
				Color line = path.Contains(n) ? Hearth.Leaf : Hearth.Seam;
				int thick = path.Contains(n) ? 3 : 2;
				Hearth.Fill(b, new Rectangle(from.X - thick / 2, from.Y, thick, cy - from.Y), line);
				Hearth.Fill(b, new Rectangle(from.X - thick / 2, cy - thick / 2, nx - from.X, thick), line);
			}
			y += rowH;
		}
		y = top;
		foreach (TrailNode n in t.Rows)
		{
			int nx = view.X + 20 + n.Depth * indent;
			Rectangle row = new Rectangle(nx - 6, y + 2, view.Right - 14 - (nx - 6), rowH - 4);
			bool on = path.Contains(n);
			if (on)
			{
				Hearth.FillRounded(b, row, Hearth.Honey, 10);
			}
			else if (Hearth.Hover(row, view))
			{
				Hearth.FillRounded(b, row, Hearth.ParchmentDeep, 10);
			}
			Rectangle a = new Rectangle(nx, y + 8, av, av);
			if (n.Npc.Length > 0)
			{
				Kit.Avatar(b, a, Ctx, n.Npc);
			}
			else
			{
				Hearth.Glyph(b, a, Hearth.QuillPale, "?");
			}
			string name = n.Npc.Length > 0 ? Ctx.Display(n.Npc) : Tr.Get("gtree.village", "全村");
			int tx = a.Right + 12;
			Hearth.InkBold(b, name, new Vector2(tx, y + 8), Hearth.Quill, 0.76f);
			Fidelity fid = n.Fidelity;
			bool firstHand = n.How is HeardHow.Origin or HeardHow.Witness;
			string chip = firstHand ? Tr.Get("gtree.firsthand", "第一手") : FidelityName(fid);
			Color cc = firstHand ? Hearth.LeafDeep : FidelityColor(fid);
			int cw = Hearth.TagWidth(chip, 0.62f);
			Hearth.Tag(b, new Vector2(row.Right - 10 - cw, y + 16), chip, Color.Lerp(cc, Color.White, 0.8f), Color.Lerp(cc, Hearth.Quill, 0.3f), 0.62f);
			string how = HowText(n);
			Hearth.Ink(b, Hearth.Truncate(how, row.Right - 24 - cw - tx, 0.64f), new Vector2(tx, y + 32), Hearth.QuillSoft, 0.64f);
			if (n.Npc.Length > 0)
			{
				string npc = n.Npc;
				hits.Add(row, () =>
				{
					Game1.playSound("smallSelect");
					_focus = Same(_focus, npc) ? null : npc;
				}, view);
			}
			y += rowH;
		}
		y += 10;
		if (t.Approximate)
		{
			foreach (string line in Hearth.WrapLines(Tr.Get("gtree.approx", "有几步是推出来的：旧八卦、合并过的八卦，村里记不清每一步是谁传给谁。"), view.Width - 40, 0.64f))
			{
				Hearth.Ink(b, line, new Vector2(view.X + 20, y), Hearth.QuillPale, 0.64f);
				y += Hearth.LineHeight(0.64f);
			}
		}
		Hearth.EndClip(b);
		_treeScroll.Finish(b, view, y + 12, 8);
	}

	private string HowText(TrailNode n)
	{
		string teller = n.Teller == null || n.Teller.Npc.Length == 0 ? Tr.Get("gtree.someone", "有人") : Ctx.Display(n.Teller.Npc);
		string how = n.How switch
		{
			HeardHow.Origin => n.Npc.Length > 0 ? Tr.Get("gtree.how.origin", "最早说起的人") : Tr.Get("gtree.how.originUnknown", "不知道是谁先说的"),
			HeardHow.Witness => Tr.Get("gtree.how.witness", "亲眼看见的"),
			HeardHow.Mentioned => Tr.Get("gtree.how.mentioned", "这话本来就在说他"),
			HeardHow.Close => Tr.Get("gtree.how.close", "最先听说，是{{name}}讲的", ("name", teller)),
			HeardHow.Spread => n.Step == 1 ? Tr.Get("gtree.how.nextDay", "隔天从{{name}}那儿得知", ("name", teller)) : Tr.Get("gtree.how.later", "过了 {{days}} 天，{{name}}才提起", ("days", n.Step), ("name", teller)),
			_ => Tr.Get("gtree.how.unclear", "知道这事，不过没人看见是谁讲的")
		};
		if (n.How is HeardHow.Origin or HeardHow.Witness)
		{
			return how;
		}
		int pct = (int)Math.Round(n.Drift * 100);
		string drift = n.Fidelity switch
		{
			Fidelity.Faithful => Tr.Get("gtree.hint.faithful", "和事实差不多（走样 {{percent}}%）", ("percent", pct)),
			Fidelity.Stretched => Tr.Get("gtree.hint.stretched", "大体是真的，路上多了点戏（走样 {{percent}}%）", ("percent", pct)),
			_ => Tr.Get("gtree.hint.distorted", "转了太多手，早不是那么回事了（走样 {{percent}}%）", ("percent", pct))
		};
		return how + " · " + drift;
	}

	private static string FidelityName(Fidelity f) => f switch
	{
		Fidelity.Faithful => Tr.Get("gtree.ver.faithful", "八九不离十"),
		Fidelity.Stretched => Tr.Get("gtree.ver.stretched", "添油加醋"),
		_ => Tr.Get("gtree.ver.distorted", "传走样了")
	};

	private static Color FidelityColor(Fidelity f) => f switch
	{
		Fidelity.Faithful => new Color(96, 168, 64),
		Fidelity.Stretched => new Color(222, 166, 50),
		_ => new Color(214, 76, 60)
	};

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
