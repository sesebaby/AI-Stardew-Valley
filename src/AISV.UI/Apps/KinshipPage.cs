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

/// <summary>家谱：左边列出每一家，右边画这一家的树；点头像只看这个人的亲属。</summary>
internal sealed class KinshipPage : ShellPage
{
	private const int Face = 72;

	private const int Storey = 144;

	private readonly Scroller _scroll = new Scroller();

	private KinRegister _register = new KinRegister();

	private DateTime _readAt = DateTime.MinValue;

	private int _shown;

	private string? _focus;

	private Rectangle _canvas;

	public KinshipPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("kin.title", "家谱");

	public override void Reload()
	{
		if ((DateTime.UtcNow - _readAt).TotalSeconds < 30)
		{
			return;
		}
		_readAt = DateTime.UtcNow;
		_register = AppData.Kin(Ctx);
		_shown = Math.Clamp(_shown, 0, _register.Trees.Count);
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _canvas.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		int listW = Math.Clamp(area.Width / 4, 220, 300);
		_canvas = new Rectangle(area.X + listW, area.Y, area.Width - listW, area.Height);
		Rectangle list = new Rectangle(area.X, area.Y, listW, area.Height);
		AisvTheme.Fill(b, list, AisvTheme.Card);
		AisvTheme.VLine(b, list.Right - 2, list.Y, list.Height, AisvTheme.Divider);
		int rows = _register.Trees.Count + 1, rowH = Math.Min(58, (list.Height - 24) / Math.Max(1, rows));
		for (int i = 0; i < _register.Trees.Count; i++)
		{
			KinTree tree = _register.Trees[i];
			ListRow(b, new Rectangle(list.X + 8, list.Y + 12 + i * rowH, listW - 20, rowH - 2), i, TreeName(tree), tree.Everyone.Count(), hits);
		}
		ListRow(b, new Rectangle(list.X + 8, list.Y + 12 + _register.Trees.Count * rowH, listW - 20, rowH - 2), _register.Trees.Count, Tr.Get("kin.alone", "独自生活"), _register.Alone.Count, hits);
		AisvTheme.BeginClip(b, _canvas);
		int end = _shown < _register.Trees.Count ? DrawTree(b, _register.Trees[_shown], hits) : DrawLoners(b);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _canvas, end + 16);
	}

	private void ListRow(SpriteBatch b, Rectangle r, int index, string label, int count, Hits hits)
	{
		bool active = index == _shown;
		if (active || AisvTheme.Hover(r))
		{
			AisvTheme.FillRounded(b, r, active ? AisvTheme.SelectedRow : AisvTheme.PageDeep, 10);
		}
		string number = count.ToString();
		float numberW = AisvTheme.Width(number, 0.72f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(label, r.Width - 44 - (int)numberW, 0.84f), new Vector2(r.X + 14, r.Center.Y - AisvTheme.LineHeight(0.84f) / 2), active ? AisvTheme.AccentDark : AisvTheme.Text, 0.84f);
		AisvTheme.DrawText(b, number, new Vector2(r.Right - 14 - numberW, r.Center.Y - AisvTheme.LineHeight(0.72f) / 2), AisvTheme.TextFaint, 0.72f);
		hits.Add(r, () =>
		{
			if (!active)
			{
				Game1.playSound("smallSelect");
				_shown = index;
				_focus = null;
				_scroll.Reset();
			}
		});
	}

	private string TreeName(KinTree tree) => tree.IsFarmers ? Tr.Get("kin.myFamily", "你的家") : Tr.Get("kin.family", "{{name}}一家", ("name", Who(tree.Root.People[0])));

	private int DrawTree(SpriteBatch b, KinTree tree, Hits hits)
	{
		int left = _canvas.X + 32;
		int y = _canvas.Y + 22 - _scroll.Offset;
		AisvTheme.Bold(b, TreeName(tree), new Vector2(left, y), AisvTheme.Text, 1.15f);
		y += AisvTheme.LineHeight(1.15f) + 4;
		AisvTheme.DrawText(b, Tr.Get("kin.hint", "点头像，只看这个人的亲属；再点一下看全家。"), new Vector2(left, y), AisvTheme.TextMuted, 0.76f);
		y += 46;
		int step = (int)Math.Clamp((_canvas.Width - 80) / Math.Max(1f, tree.Width), Face + 24, 170);
		int originX = _canvas.Center.X - (int)(step * (tree.Width - 1) / 2);
		int top = y;
		Vector2 At(KinCell cell, string person) => new Vector2(originX + cell.XOf(person) * step, top + Face / 2 + cell.Depth * Storey);
		List<KinCell> cells = tree.Root.SelfAndBelow().ToList();
		foreach (KinCell cell in cells)
		{
			DrawPairs(b, cell, At);
			foreach (KinBelow below in cell.Below)
			{
				float fromX = below.Elders.Average(e => At(cell, e).X);
				Bracket(b, new Vector2(fromX, At(cell, below.Elders[0]).Y), At(below.Cell, below.Person), RoleColor(below.Role));
			}
		}
		foreach (KinSide side in tree.Sides)
		{
			KinCell elder = cells.First(c => c.People.Contains(side.Elder, StringComparer.OrdinalIgnoreCase));
			KinCell younger = cells.First(c => c.People.Contains(side.Younger, StringComparer.OrdinalIgnoreCase));
			AisvTheme.Line(b, At(elder, side.Elder), At(younger, side.Younger), RoleColor(side.Role) * 0.55f, 2f);
		}
		foreach (KinCell cell in cells)
		{
			foreach (string person in cell.People)
			{
				DrawFace(b, person, At(cell, person), step, hits);
			}
		}
		y += tree.Depth * Storey + 4;
		int w = Math.Min(_canvas.Width - 64, 820), x = _canvas.X + (_canvas.Width - w) / 2;
		bool focused = _focus != null && tree.Everyone.Contains(_focus, StringComparer.OrdinalIgnoreCase);
		y = TextCard(b, focused ? Tr.Get("kin.ofPerson", "{{name}}的亲属", ("name", Who(_focus!))) : Tr.Get("kin.ties", "这一家"), Sentences(tree, focused ? _focus : null), x, y, w) + 16;
		if (tree.Moods.Count > 0)
		{
			y = TextCard(b, Tr.Get("kin.moods", "最近相处得怎么样"), tree.Moods.Select(m => Tr.Get("kin.moodLine", "{{a}}和{{b}}：{{mood}}", ("a", Who(m.A)), ("b", Who(m.B)), ("mood", VillageMapPage.BondName(m.Bond)))).ToList(), x, y, w) + 16;
		}
		return y;
	}

	private static void DrawPairs(SpriteBatch b, KinCell cell, Func<KinCell, string, Vector2> at)
	{
		for (int i = 0; i < cell.Pairs.Count; i++)
		{
			Color color = cell.Pairs[i] switch
			{
				PairKind.Married => new Color(214, 84, 130),
				PairKind.Siblings => AisvTheme.TextFaint,
				_ => new Color(236, 140, 176)
			};
			AisvTheme.Line(b, at(cell, cell.People[0]), at(cell, cell.People[i + 1]), color, cell.Pairs[i] == PairKind.Married ? 4f : 2f);
		}
	}

	private static Color RoleColor(ElderRole role) => role switch
	{
		ElderRole.Parent => AisvTheme.Accent,
		ElderRole.Godparent => new Color(146, 100, 200),
		_ => AisvTheme.Gold
	};

	/// <summary>从长辈往下，横着走到晚辈头上，再落下去。</summary>
	private static void Bracket(SpriteBatch b, Vector2 elder, Vector2 younger, Color color)
	{
		float bend = younger.Y - Face / 2f - 22;
		AisvTheme.Line(b, elder, new Vector2(elder.X, bend), color, 3f);
		AisvTheme.Line(b, new Vector2(elder.X, bend), new Vector2(younger.X, bend), color, 3f);
		AisvTheme.Line(b, new Vector2(younger.X, bend), younger, color, 3f);
	}

	private void DrawFace(SpriteBatch b, string person, Vector2 center, int step, Hits hits)
	{
		Rectangle face = new Rectangle((int)center.X - Face / 2, (int)center.Y - Face / 2, Face, Face);
		bool focused = Kinship.Same(person, _focus);
		if (focused || AisvTheme.Hover(face, _canvas))
		{
			AisvTheme.FillRounded(b, AisvTheme.Inflate(face, 5), focused ? AisvTheme.Accent : AisvTheme.AccentSoft, 12);
		}
		string name = Who(person);
		if (person == Kinship.Me || Kinship.IsOutsider(person))
		{
			AisvTheme.Avatar(b, face, null, name.Length > 0 ? name.Substring(0, 1) : "?");
		}
		else
		{
			Kit.Avatar(b, face, Ctx, person);
		}
		bool away = _register.Away.Contains(person, StringComparer.OrdinalIgnoreCase);
		string label = AisvTheme.Truncate(away ? Tr.Get("kin.awayName", "{{name}}（不在镇上）", ("name", name)) : name, Math.Max(Face, step - 10), 0.74f);
		float labelW = AisvTheme.Width(label, 0.74f);
		AisvTheme.Fill(b, new Rectangle((int)(center.X - labelW / 2f) - 4, face.Bottom + 5, (int)labelW + 8, AisvTheme.LineHeight(0.74f)), AisvTheme.Page);
		AisvTheme.DrawText(b, label, new Vector2(center.X - labelW / 2f, face.Bottom + 5), away ? AisvTheme.TextFaint : AisvTheme.Text, 0.74f);
		hits.Add(face, () =>
		{
			Game1.playSound("smallSelect");
			_focus = focused ? null : person;
		}, _canvas);
	}

	private int DrawLoners(SpriteBatch b)
	{
		int x = _canvas.X + 32, y = _canvas.Y + 22 - _scroll.Offset;
		AisvTheme.Bold(b, Tr.Get("kin.alone", "独自生活"), new Vector2(x, y), AisvTheme.Text, 1.15f);
		y += AisvTheme.LineHeight(1.15f) + 4;
		AisvTheme.DrawText(b, Tr.Get("kin.aloneHint", "这些人的亲戚都不住在这里。"), new Vector2(x, y), AisvTheme.TextMuted, 0.76f);
		y += 46;
		const int cell = 132;
		int perRow = Math.Max(1, (_canvas.Width - 64) / cell);
		for (int i = 0; i < _register.Alone.Count; i++)
		{
			Rectangle face = new Rectangle(x + i % perRow * cell, y + i / perRow * Storey, Face, Face);
			Kit.Avatar(b, face, Ctx, _register.Alone[i]);
			string label = AisvTheme.Truncate(Who(_register.Alone[i]), cell - 8, 0.74f);
			AisvTheme.DrawText(b, label, new Vector2(face.Center.X - AisvTheme.Width(label, 0.74f) / 2f, face.Bottom + 6), AisvTheme.Text, 0.74f);
		}
		return y + (_register.Alone.Count + perRow - 1) / perRow * Storey;
	}

	private static int TextCard(SpriteBatch b, string title, List<string> lines, int x, int y, int w)
	{
		const float scale = 0.8f;
		int lh = AisvTheme.LineHeight(scale);
		List<string> rows = lines.SelectMany(l => AisvTheme.WrapLines(l, w - 48, scale)).ToList();
		Rectangle card = new Rectangle(x, y, w, 72 + rows.Count * lh);
		AisvTheme.DrawCard(b, card);
		Kit.SectionTitle(b, title, x + 20, y + 18);
		for (int i = 0; i < rows.Count; i++)
		{
			AisvTheme.DrawText(b, rows[i], new Vector2(x + 24, y + 56 + i * lh), AisvTheme.Text, scale);
		}
		return card.Bottom;
	}

	/// <summary>按树往下读出每句关系；focus 不为空时只留和这个人有关的。</summary>
	private List<string> Sentences(KinTree tree, string? focus)
	{
		List<(string Text, string A, string B)> all = new List<(string, string, string)>();
		foreach (KinCell cell in tree.Root.SelfAndBelow())
		{
			for (int i = 0; i < cell.Pairs.Count; i++)
			{
				all.Add((PairSentence(cell.People[0], cell.People[i + 1], cell.Pairs[i]), cell.People[0], cell.People[i + 1]));
			}
			foreach (KinBelow below in cell.Below)
			{
				all.AddRange(below.Elders.Select(e => (ElderSentence(e, below.Person, below.Role), e, below.Person)));
			}
			foreach ((KinBelow first, KinBelow second) in cell.Below.SelectMany((x, i) => cell.Below.Skip(i + 1).Select(y => (x, y))).Where(p => p.x.Role == ElderRole.Parent && p.y.Role == ElderRole.Parent && p.x.Elders.Intersect(p.y.Elders, StringComparer.OrdinalIgnoreCase).Any()))
			{
				all.Add((PairSentence(first.Person, second.Person, PairKind.Siblings), first.Person, second.Person));
			}
		}
		all.AddRange(tree.Sides.Select(s => (ElderSentence(s.Elder, s.Younger, s.Role), s.Elder, s.Younger)));
		return all.Where(s => focus == null || Kinship.Same(s.A, focus) || Kinship.Same(s.B, focus)).Select(s => s.Text).ToList();
	}

	private string PairSentence(string a, string b, PairKind kind) => kind switch
	{
		PairKind.Married => Tr.Get("kin.tie.spouse", "{{a}}和{{b}}结婚了", ("a", Who(a)), ("b", Who(b))),
		PairKind.Engaged => Tr.Get("kin.tie.engaged", "{{a}}和{{b}}订婚了", ("a", Who(a)), ("b", Who(b))),
		PairKind.Dating => Tr.Get("kin.tie.dating", "{{a}}和{{b}}在交往", ("a", Who(a)), ("b", Who(b))),
		_ => Tr.Get("kin.tie.sibling", "{{a}}和{{b}}是兄弟姐妹", ("a", Who(a)), ("b", Who(b)))
	};

	private string ElderSentence(string elder, string younger, ElderRole role)
	{
		bool? male = elder == Kinship.Me ? Game1.player?.IsMale : ShellText.Npc(elder)?.Gender switch
		{
			Gender.Male => true,
			Gender.Female => false,
			_ => null
		};
		string word = (role, male) switch
		{
			(ElderRole.Parent, true) => Tr.Get("kin.role.father", "父亲"),
			(ElderRole.Parent, false) => Tr.Get("kin.role.mother", "母亲"),
			(ElderRole.Parent, _) => Tr.Get("kin.role.parent", "家长"),
			(ElderRole.Grandparent, true) => Tr.Get("kin.role.grandfather", "祖父"),
			(ElderRole.Grandparent, false) => Tr.Get("kin.role.grandmother", "祖母"),
			(ElderRole.Grandparent, _) => Tr.Get("kin.role.grand", "祖辈"),
			(ElderRole.AuntOrUncle, true) => Tr.Get("kin.role.uncle", "叔叔"),
			(ElderRole.AuntOrUncle, false) => Tr.Get("kin.role.aunt", "姑姑"),
			(ElderRole.AuntOrUncle, _) => Tr.Get("kin.role.elder", "长辈"),
			(_, true) => Tr.Get("kin.role.godfather", "教父"),
			(_, false) => Tr.Get("kin.role.godmother", "教母"),
			_ => Tr.Get("kin.role.god", "教父母")
		};
		return Tr.Get("kin.tie.elder", "{{a}}是{{b}}的{{role}}", ("a", Who(elder)), ("b", Who(younger)), ("role", word));
	}

	private string Who(string person)
	{
		if (person == Kinship.Me)
		{
			return Tr.Get("kin.me", "你");
		}
		return Kinship.IsOutsider(person) ? ShellText.Clean(Kinship.OutsiderName(person)) : Ctx.Display(person);
	}
}
