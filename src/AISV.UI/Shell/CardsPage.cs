using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Persona;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>村民角色卡：所有能聊天的村民排成格子，标出谁的人设被改写或补充过，点进去编辑。</summary>
internal sealed class CardsPage : ShellPage
{
	private sealed record Entry(string Npc, string Name, PersonaSource Source, CharacterCard? Card);

	private readonly Scroller _scroll = new Scroller();

	private readonly TextBox _search = new TextBox(null, null, Game1.smallFont, AisvTheme.Text) { textLimit = 20, limitWidth = false };

	private List<Entry> _all = new List<Entry>();

	/// <summary>有角色卡改过了，下次回到列表时重算。</summary>
	public static bool Changed { get; set; }

	private Rectangle _searchBox;

	private Rectangle _view;

	public CardsPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("cards.title", "村民角色卡");

	public override bool CapturesKeys => _search.Selected;

	public override void Reload()
	{
		if (_all.Count > 0 && !Changed)
		{
			return;
		}
		Changed = false;
		HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (NPC npc in Utility.getAllVillagers().Where(n => n.CanSocialize))
		{
			names.Add(npc.Name);
		}
		_all = names.Select(n =>
		{
			NpcPersona p = Ctx.Npcs.Preview(n, null);
			return new Entry(n, Ctx.Display(n), p.Source, Ctx.Npcs.Cards.Get(n));
		}).OrderByDescending(e => e.Card != null).ThenBy(e => e.Name, StringComparer.CurrentCulture).ToList();
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (_search.Selected)
		{
			if (key == Keys.Escape)
			{
				if ((_search.Text ?? "").Length > 0)
				{
					_search.Text = "";
				}
				else
				{
					_search.Selected = false;
				}
			}
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	public override void Closed() => _search.Selected = false;

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		const int pad = 28;
		int x = area.X + pad, w = area.Width - pad * 2;
		AisvTheme.DrawText(b, Tr.Get("cards.intro", "改写或补充村民的人设，所有存档通用；没改过的用默认人设。"), new Vector2(x, area.Y + 22), AisvTheme.TextMuted, 0.78f);
		_searchBox = new Rectangle(x + w - 320, area.Y + 12, 320, 44);
		AisvTheme.Field(b, _searchBox, _search.Selected);
		AisvTheme.Magnifier(b, new Vector2(_searchBox.X + 20, _searchBox.Center.Y));
		string q = (_search.Text ?? "").Trim();
		Vector2 tp = new Vector2(_searchBox.X + 40, _searchBox.Center.Y - AisvTheme.LineHeight(0.8f) / 2);
		AisvTheme.DrawText(b, q.Length == 0 && !_search.Selected ? Tr.Get("cards.search", "搜索村民") : q, tp, q.Length == 0 ? AisvTheme.TextFaint : AisvTheme.Text, 0.8f);
		hits.Add(_searchBox, () => _search.Selected = true);
		_view = new Rectangle(area.X, area.Y + 70, area.Width, area.Height - 70);
		List<Entry> shown = _all.Where(e => q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Npc.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
		AisvTheme.BeginClip(b, _view);
		const int gap = 16, h = 96;
		int cols = Math.Max(1, (w + gap) / (300 + gap));
		int cw = (w - gap * (cols - 1)) / cols;
		int y0 = _view.Y + 8 - _scroll.Offset;
		for (int i = 0; i < shown.Count; i++)
		{
			Rectangle r = new Rectangle(x + i % cols * (cw + gap), y0 + i / cols * (h + gap), cw, h);
			if (r.Bottom < _view.Y || r.Y > _view.Bottom)
			{
				continue;
			}
			Entry e = shown[i];
			Tile(b, r, e);
			hits.Add(r, () =>
			{
				_search.Selected = false;
				Host.Push(new CardEditorPage(Ctx, e.Npc));
			}, _view);
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y0 + (shown.Count + cols - 1) / cols * (h + gap), 12);
	}

	private void Tile(SpriteBatch b, Rectangle r, Entry e)
	{
		bool hover = AisvTheme.Hover(r, _view);
		AisvTheme.Bordered(b, r, AisvTheme.Card, hover ? AisvTheme.Accent : e.Card != null ? AisvTheme.Gold * 0.7f : AisvTheme.CardBorder, 12);
		Rectangle av = new Rectangle(r.X + 14, r.Y + 14, 68, 68);
		Kit.Avatar(b, av, Ctx, e.Npc);
		int tx = av.Right + 14;
		AisvTheme.Bold(b, AisvTheme.Truncate(e.Name, r.Right - tx - 16, 0.88f), new Vector2(tx, r.Y + 16), AisvTheme.Text, 0.88f);
		(string label, Color fill, Color ink) = e.Card == null ? (SourceName(e.Source), AisvTheme.PageDeep, AisvTheme.TextMuted) : Badge(e.Card);
		AisvTheme.Chip(b, new Vector2(tx, r.Y + 52), label, fill, ink, 0.66f);
	}

	/// <summary>改过的卡的标签：写了性格是"已改写"，只改了名字是"已改名"，其余是"已补充"。</summary>
	public static (string Label, Color Fill, Color Ink) Badge(CharacterCard card)
	{
		if (card.Replaces)
		{
			return (Tr.Get("cards.replaced", "已改写"), AisvTheme.GoldSoft, AisvTheme.Gold);
		}
		bool onlyName = new[] { card.Appearance, card.Lore, card.SocialTags, card.Speech }.All(string.IsNullOrWhiteSpace);
		return onlyName ? (Tr.Get("cards.renamed", "已改名"), AisvTheme.PinkSoft, AisvTheme.Pink) : (Tr.Get("cards.supplemented", "已补充"), AisvTheme.AccentSoft, AisvTheme.AccentDark);
	}

	public static string SourceName(PersonaSource s) => s switch
	{
		PersonaSource.BuiltIn => Tr.Get("cards.src.builtin", "内置人设"),
		PersonaSource.Sve => Tr.Get("cards.src.sve", "SVE 人设"),
		PersonaSource.ContentPack => Tr.Get("cards.src.pack", "内容包人设"),
		PersonaSource.Card => Tr.Get("cards.src.card", "角色卡"),
		_ => Tr.Get("cards.src.generated", "按性格数值生成")
	};
}
