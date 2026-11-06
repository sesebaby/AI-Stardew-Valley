using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.Core.World.Errands;
using AISV.Game.World;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace AISV.UI.Shell;

/// <summary>发布委托：托谁、做什么、要什么、数量期限报酬；右边一直是规则预估和要交的钱。</summary>
internal sealed class ErrandFormPage : ShellPage
{
	private static readonly Color Blue = new Color(70, 130, 190);

	private static readonly Color BlueSoft = new Color(222, 236, 250);

	private readonly ErrandDraft _draft = new ErrandDraft();

	private readonly FieldSet _fields = new FieldSet();

	private readonly TextArea _search;

	private readonly TextArea _note;

	private readonly Scroller _scroll = new Scroller();

	private readonly Func<Errand, ErrandReply, bool>? _inline;

	private IReadOnlyList<ErrandHelper> _helpers = Array.Empty<ErrandHelper>();

	private IReadOnlyList<ErrandItem> _items = Array.Empty<ErrandItem>();

	private string _itemsKey = "";

	private bool _rewardTouched;

	private Rectangle _view;

	private (Rectangle Bounds, string Text)? _tip;

	private string? _awaiting;

	private bool _posting;

	private int _problemTick = -1;

	private string? _problem;

	/// <param name="inline">从对话里打开时：发出去后村民当面回答。</param>
	public ErrandFormPage(ShellContext ctx, string? npc = null, Func<Errand, ErrandReply, bool>? inline = null) : base(ctx)
	{
		_inline = inline;
		_search = _fields.Add(20, Tr.Get("errandForm.search", "搜索名字"), singleLine: true);
		_note = _fields.Add(ErrandRules.MaxNote, Tr.Get("errandForm.notePlaceholder", "想对他说的话（可以不填）"), singleLine: true);
		_draft.Npc = npc ?? "";
	}

	private ErrandService? Service => Ctx.Errands;

	public override string Title => Tr.Get("errandForm.title", "发布委托");

	public override bool CapturesKeys => _fields.Typing;

	private ErrandHelper? Helper => _helpers.FirstOrDefault(h => string.Equals(h.Npc, _draft.Npc, StringComparison.OrdinalIgnoreCase));

	public override void Reload()
	{
		if (Service == null)
		{
			return;
		}
		_helpers = Service.Helpers();
		if (_draft.Npc.Length > 0 && Helper == null)
		{
			_draft.Npc = "";
		}
		if (Helper is ErrandHelper h && ErrandService.KindBlocked(h, _draft.Kind) != null)
		{
			_draft.Kind = ErrandKinds.All.FirstOrDefault(k => ErrandService.KindBlocked(h, k) == null) ?? ErrandKinds.Buy;
		}
		_itemsKey = "";
		if (!_sourcePicked)
		{
			_sourcePicked = true;
			PreferSkilledSource();
		}
	}

	private bool _sourcePicked;

	/// <summary>找东西时先切到这位村民擅长的那一种。</summary>
	private void PreferSkilledSource()
	{
		if (_draft.Kind == ErrandKinds.Fetch && Helper is ErrandHelper h && FetchSources.All.FirstOrDefault(s => h.Skills.Contains(ErrandSkills.Needed(ErrandKinds.Fetch, s)!)) is string source && source != _draft.Source)
		{
			_draft.Source = source;
			_draft.Count = 1;
			_draft.Item = null;
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (_fields.KeyPress(key))
		{
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	public override void Closed()
	{
		_fields.BlurAll();
		_awaiting = null;
	}

	/// <summary>这张表单还在等这件委托的当面回话。</summary>
	public bool Awaits(string id) => _posting || _awaiting == id;

	private void RefreshItems()
	{
		string key = $"{_draft.Npc}|{_draft.Kind}|{_draft.Source}";
		if (key == _itemsKey || Service == null)
		{
			return;
		}
		_itemsKey = key;
		_items = Service.Items(_draft.Kind, _draft.Source, Helper);
		if (_draft.Item != null && !_items.Any(i => i.Id == _draft.Item.Id && i.InventoryIndex == _draft.Item.InventoryIndex))
		{
			_draft.Item = null;
		}
		ResetReward();
	}

	private ErrandQuote Quote() => Service!.Quote(_draft);

	private void ResetReward()
	{
		_rewardTouched = false;
		_draft.Reward = Service == null ? 0 : Quote().DefaultReward;
	}

	/// <summary>改了会影响默认报酬的东西：没手动调过报酬就跟着变，调过的保持在范围里。</summary>
	private void Changed()
	{
		if (Service == null)
		{
			return;
		}
		ErrandQuote q = Quote();
		_draft.Reward = _rewardTouched ? Math.Clamp(_draft.Reward, q.MinReward, q.MaxReward) : q.DefaultReward;
	}

	// ---------------- 画 ----------------

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		_tip = null;
		if (Service == null || !Service.Enabled)
		{
			Kit.Empty(b, area, Tr.Get("errandForm.off", "给村民发委托没开。"), Tr.Get("errandForm.offHint", "在设置 > 世界里打开。"));
			return;
		}
		RefreshItems();
		bool narrow = area.Width < 980;
		int pad = narrow ? 16 : 24;
		int sideW = narrow ? 0 : Math.Clamp(area.Width * 30 / 100, 300, 380);
		Rectangle side = narrow ? Rectangle.Empty : new Rectangle(area.Right - pad - sideW, area.Y + 16, sideW, area.Height - 32);
		_view = narrow ? new Rectangle(area.X, area.Y, area.Width, area.Height - 150) : new Rectangle(area.X, area.Y, side.X - 8 - area.X, area.Height);
		Hearth.BeginClip(b, _view);
		int x = _view.X + pad, w = _view.Width - pad * 2 - 12;
		int y = _view.Y + 16 - _scroll.Offset;
		y = Section(b, Tr.Get("errandForm.who", "托谁"), x, y);
		y = DrawHelpers(b, x, y, w, hits) + 22;
		y = Section(b, Tr.Get("errandForm.what", "做什么"), x, y);
		y = DrawKinds(b, x, y, w, hits) + 22;
		y = Section(b, _draft.Kind == ErrandKinds.Relay ? Tr.Get("errandForm.giveWhat", "捎什么") : Tr.Get("errandForm.item", "要什么"), x, y);
		y = DrawItems(b, x, y, w, hits) + 22;
		if (_draft.Kind == ErrandKinds.Relay)
		{
			y = Section(b, Tr.Get("errandForm.recipient", "捎给谁"), x, y);
			y = DrawRecipients(b, x, y, w, hits) + 22;
		}
		y = Section(b, Tr.Get("errandForm.terms", "数量、期限和报酬"), x, y);
		y = DrawTerms(b, x, y, w, hits) + 24;
		Hearth.EndClip(b);
		_scroll.Finish(b, _view, y);
		if (narrow)
		{
			DrawSummary(b, new Rectangle(area.X + pad, _view.Bottom + 6, area.Width - pad * 2, area.Bottom - _view.Bottom - 12), hits, compact: true);
		}
		else
		{
			DrawSummary(b, side, hits, compact: false);
		}
		if (_tip is var (bounds, text))
		{
			Tooltip(b, bounds, text);
		}
		if (_awaiting != null)
		{
			b.Draw(Game1.fadeToBlackRect, area, Color.Black * 0.3f);
			hits.Add(area, () => { });
			Rectangle card = new Rectangle(area.Center.X - 200, area.Center.Y - 50, 400, 100);
			Hearth.CastShadow(b, card);
			Hearth.CardSlip(b, card);
			string dots = new string('.', 1 + Game1.ticks / 20 % 3);
			Hearth.InkMiddle(b, Tr.Get("errandForm.waiting", "等{{name}}回话", ("name", Service.Display(_draft.Npc))) + dots, card, Hearth.Quill, 0.85f);
		}
	}

	private static int Section(SpriteBatch b, string title, int x, int y)
	{
		Kit.SectionTitle(b, title, x, y);
		return y + Hearth.LineHeight(0.85f) + 10;
	}

	private int DrawHelpers(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		if (_helpers.Count == 0)
		{
			Hearth.Ink(b, Tr.Get("errandForm.noHelpers", "还不认识能托的人。"), new Vector2(x, y), Hearth.QuillSoft, 0.78f);
			return y + 30;
		}
		const int tile = 92, gap = 10;
		int cols = Math.Max(1, (w + gap) / (tile + gap));
		for (int i = 0; i < _helpers.Count; i++)
		{
			ErrandHelper h = _helpers[i];
			Rectangle r = new Rectangle(x + i % cols * (tile + gap), y + i / cols * (tile + 30 + gap), tile, tile + 30);
			if (r.Bottom < _view.Y || r.Y > _view.Bottom)
			{
				continue;
			}
			bool sel = h.Npc == _draft.Npc;
			bool blocked = h.Blocked != null;
			bool hover = Hearth.Hover(r, _view);
			Hearth.Bordered(b, r, sel ? Hearth.LeafPale : hover && !blocked ? Hearth.Slip : Hearth.Parchment, sel ? Hearth.Leaf : Hearth.SlipEdge, 12);
			Rectangle face = new Rectangle(r.X + (tile - 60) / 2, r.Y + 8, 60, 60);
			Kit.Avatar(b, face, Ctx, h.Npc);
			if (blocked)
			{
				Hearth.FillRounded(b, r, Hearth.Parchment * 0.6f, 12);
			}
			string name = Hearth.Truncate(h.Display, tile - 10, 0.7f);
			Hearth.InkMiddle(b, name, new Rectangle(r.X, face.Bottom + 4, tile, 24), blocked ? Hearth.QuillPale : Hearth.Quill, 0.7f);
			string skills = string.Join("", h.Skills.OrderBy(s => s).Select(SkillGlyph));
			if (skills.Length > 0)
			{
				Hearth.Tag(b, new Vector2(r.Right - 6 - Hearth.TagWidth(skills, 0.55f), r.Y + 4), skills, Hearth.AmberPale, Hearth.Amber, 0.55f);
			}
			if (h.Child)
			{
				Hearth.Tag(b, new Vector2(r.X + 4, r.Y + 4), Tr.Get("errandForm.child", "小"), BlueSoft, Blue, 0.55f);
			}
			string tip = h.Blocked ?? (h.Skills.Count > 0 ? Tr.Get("errandForm.good", "擅长{{what}}", ("what", string.Join("、", h.Skills.Select(ErrandSkills.Label)))) : "") + (h.Skills.Count > 0 ? " · " : "") + Tr.Get("errandForm.hearts", "{{n}} 心", ("n", h.Hearts));
			if (hover)
			{
				_tip = (r, tip);
			}
			if (!blocked)
			{
				string npc = h.Npc;
				hits.Add(r, () =>
				{
					Game1.playSound("smallSelect");
					_draft.Npc = npc;
					if (_draft.Recipient == npc)
					{
						_draft.Recipient = "";
					}
					if (Helper is ErrandHelper picked && ErrandService.KindBlocked(picked, _draft.Kind) != null)
					{
						_draft.Kind = ErrandKinds.All.FirstOrDefault(k => ErrandService.KindBlocked(picked, k) == null) ?? ErrandKinds.Buy;
					}
					PreferSkilledSource();
					_itemsKey = "";
				}, _view);
			}
		}
		int rows = (_helpers.Count + cols - 1) / cols;
		return y + rows * (tile + 30 + gap) - gap;
	}

	private static string SkillGlyph(string skill) => skill switch
	{
		ErrandSkills.Fish => Tr.Get("errandForm.glyph.fish", "钓"),
		ErrandSkills.Mine => Tr.Get("errandForm.glyph.mine", "矿"),
		ErrandSkills.Forage => Tr.Get("errandForm.glyph.forage", "采"),
		_ => Tr.Get("errandForm.glyph.cook", "厨")
	};

	private static string KindLabel(string kind) => kind switch
	{
		ErrandKinds.Fetch => Tr.Get("errandForm.kind.fetch", "找东西"),
		ErrandKinds.Buy => Tr.Get("errandForm.kind.buy", "代买"),
		ErrandKinds.Cook => Tr.Get("errandForm.kind.cook", "做菜"),
		_ => Tr.Get("errandForm.kind.relay", "转送")
	};

	private static string KindHint(string kind) => kind switch
	{
		ErrandKinds.Fetch => Tr.Get("errandForm.kindHint.fetch", "采集、钓鱼、挖矿"),
		ErrandKinds.Buy => Tr.Get("errandForm.kindHint.buy", "去店里替你买"),
		ErrandKinds.Cook => Tr.Get("errandForm.kindHint.cook", "用自己的材料做"),
		_ => Tr.Get("errandForm.kindHint.relay", "把东西捎给别人")
	};

	private int DrawKinds(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		const int gap = 10;
		int count = ErrandKinds.All.Count;
		int bw = (w - gap * (count - 1)) / count;
		ErrandHelper? h = Helper;
		for (int i = 0; i < count; i++)
		{
			string kind = ErrandKinds.All[i];
			Rectangle r = new Rectangle(x + i * (bw + gap), y, bw, 70);
			string? blocked = h == null ? null : ErrandService.KindBlocked(h, kind);
			bool sel = kind == _draft.Kind;
			bool hover = Hearth.Hover(r, _view);
			Hearth.Bordered(b, r, sel ? Hearth.LeafPale : hover && blocked == null ? Hearth.Slip : Hearth.Parchment, sel ? Hearth.Leaf : Hearth.SlipEdge, 12);
			Color ink = blocked != null ? Hearth.QuillPale : sel ? Hearth.LeafDeep : Hearth.Quill;
			Hearth.InkMiddle(b, KindLabel(kind), new Rectangle(r.X, r.Y + 8, r.Width, 30), ink, 0.9f);
			Hearth.InkMiddle(b, Hearth.Truncate(blocked ?? KindHint(kind), r.Width - 12, 0.62f), new Rectangle(r.X, r.Y + 40, r.Width, 22), blocked != null ? Hearth.Rust : Hearth.QuillSoft, 0.62f);
			if (blocked == null)
			{
				hits.Add(r, () =>
				{
					if (_draft.Kind != kind)
					{
						Game1.playSound("smallSelect");
						_draft.Kind = kind;
						_draft.Count = 1;
						_draft.Item = null;
					}
				}, _view);
			}
		}
		y += 70;
		if (_draft.Kind == ErrandKinds.Fetch)
		{
			y += 12;
			int px = x;
			foreach (string source in FetchSources.All)
			{
				string label = ErrandSkills.Label(ErrandSkills.Needed(ErrandKinds.Fetch, source)!);
				bool skilled = h != null && h.Skills.Contains(ErrandSkills.Needed(ErrandKinds.Fetch, source)!);
				string text = skilled ? Tr.Get("errandForm.skilledSource", "{{what}}（擅长）", ("what", label)) : label;
				int pw = (int)Hearth.Width(text, 0.8f) + 36;
				if (px > x && px + pw > x + w)
				{
					px = x;
					y += 46;
				}
				Rectangle r = new Rectangle(px, y, pw, 38);
				bool sel = source == _draft.Source;
				Hearth.Bordered(b, r, sel ? Hearth.AmberPale : Hearth.Hover(r, _view) ? Hearth.Slip : Hearth.Parchment, sel ? Hearth.Amber : Hearth.SlipEdge, 19);
				Hearth.InkMiddle(b, text, r, sel ? Hearth.Amber : Hearth.QuillSoft, 0.8f);
				hits.Add(r, () =>
				{
					if (_draft.Source != source)
					{
						Game1.playSound("smallSelect");
						_draft.Source = source;
						_draft.Count = 1;
						_draft.Item = null;
					}
				}, _view);
				px = r.Right + 8;
			}
			y += 38;
		}
		return y;
	}

	private int DrawItems(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		_search.Bounds = new Rectangle(x, y, Math.Min(320, w), 44);
		_search.Draw(b);
		hits.Add(_search.Bounds, () => _fields.Focus(_search), _view);
		if (_draft.Item is ErrandItem chosen)
		{
			string picked = Tr.Get("errandForm.picked", "已选：{{item}}（{{price}}g）", ("item", chosen.Name), ("price", chosen.Price));
			Hearth.Ink(b, Hearth.Truncate(picked, w - _search.Bounds.Width - 20, 0.75f), new Vector2(_search.Bounds.Right + 16, y + 12), Hearth.LeafDeep, 0.75f);
		}
		y += 56;
		string q = _search.Text.Trim();
		List<ErrandItem> shown = _items.Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
		if (shown.Count == 0)
		{
			string empty = _items.Count == 0
				? _draft.Kind == ErrandKinds.Relay ? Tr.Get("errandForm.noInventory", "包里没有能送人的东西。") : Tr.Get("errandForm.noItems", "现在没有能托的东西。")
				: Tr.Get("errandForm.noMatch", "没找到。");
			Hearth.Ink(b, empty, new Vector2(x, y), Hearth.QuillSoft, 0.78f);
			return y + 30;
		}
		const int cell = 64, gap = 8;
		int cols = Math.Max(1, (w + gap) / (cell + gap));
		for (int i = 0; i < shown.Count; i++)
		{
			ErrandItem item = shown[i];
			Rectangle r = new Rectangle(x + i % cols * (cell + gap), y + i / cols * (cell + gap), cell, cell);
			if (r.Bottom < _view.Y || r.Y > _view.Bottom)
			{
				continue;
			}
			bool sel = _draft.Item?.Id == item.Id && _draft.Item.InventoryIndex == item.InventoryIndex;
			bool hover = Hearth.Hover(r, _view);
			Hearth.Bordered(b, r, sel ? Hearth.LeafPale : hover ? Hearth.Slip : Hearth.ParchmentDeep, sel ? Hearth.Leaf : hover ? Hearth.Amber : Hearth.SlipEdge, 10);
			ParsedItemData? data = ItemRegistry.GetData(item.Id);
			if (data != null && !data.IsErrorItem)
			{
				b.Draw(data.GetTexture(), Hearth.Inflate(r, -10), data.GetSourceRect(), Color.White);
			}
			if (item.Stock > 1)
			{
				string n = item.Stock.ToString();
				Hearth.InkBold(b, n, new Vector2(r.Right - 6 - Hearth.Width(n, 0.6f), r.Bottom - 6 - Hearth.LineHeight(0.6f)), Hearth.Quill, 0.6f);
			}
			if (hover)
			{
				_tip = (r, $"{item.Name} · {item.Price}g{(item.Stock > 1 ? " ×" + item.Stock : "")}");
			}
			hits.Add(r, () =>
			{
				Game1.playSound("coin");
				_draft.Item = item;
				_draft.Count = Math.Clamp(_draft.Count, 1, ErrandRules.MaxCount(_draft.Kind, _draft.Source, item.Material));
				Changed();
			}, _view);
		}
		int rows = (shown.Count + cols - 1) / cols;
		return y + rows * (cell + gap) - gap;
	}

	private int DrawRecipients(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		IReadOnlyList<ErrandHelper> all = _helpers.Where(h => h.Npc != _draft.Npc).ToList();
		const int tile = 64, gap = 8;
		int cols = Math.Max(1, (w + gap) / (tile + gap));
		for (int i = 0; i < all.Count; i++)
		{
			ErrandHelper h = all[i];
			Rectangle r = new Rectangle(x + i % cols * (tile + gap), y + i / cols * (tile + gap), tile, tile);
			if (r.Bottom < _view.Y || r.Y > _view.Bottom)
			{
				continue;
			}
			bool sel = h.Npc == _draft.Recipient;
			bool hover = Hearth.Hover(r, _view);
			Hearth.Bordered(b, r, sel ? Hearth.RosePale : Hearth.Parchment, sel ? Hearth.Rose : Hearth.SlipEdge, 12);
			Kit.Avatar(b, Hearth.Inflate(r, -6), Ctx, h.Npc);
			if (hover)
			{
				_tip = (r, h.Display);
			}
			string npc = h.Npc;
			hits.Add(r, () =>
			{
				Game1.playSound("smallSelect");
				_draft.Recipient = npc;
			}, _view);
		}
		return y + (all.Count + cols - 1) / cols * (tile + gap) - gap;
	}

	private int DrawTerms(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		ErrandQuote q = Quote();
		int labelW = w < 440 ? 64 : 96;
		int cy = y + 22;
		if (_draft.Kind != ErrandKinds.Relay)
		{
			int max = ErrandRules.MaxCount(_draft.Kind, _draft.Source, _draft.Item?.Material == true);
			Hearth.Ink(b, Tr.Get("errandForm.count", "数量"), new Vector2(x, cy - 12), Hearth.QuillSoft, 0.8f);
			int end = Stepper(b, x + labelW, cy, Tr.Get("errandForm.countValue", "{{n}} 个", ("n", _draft.Count)), 120, hits, () => StepCount(-StepSize(1), max), () => StepCount(StepSize(1), max));
			cy += SideNote(b, Tr.Get("errandForm.max", "最多 {{n}}", ("n", max)), end, cy, x + labelW, x + w, Hearth.QuillPale);
			cy += 62;
		}
		Hearth.Ink(b, Tr.Get("errandForm.days", "期限"), new Vector2(x, cy - 12), Hearth.QuillSoft, 0.8f);
		int dEnd = Stepper(b, x + labelW, cy, Tr.Get("errandForm.daysValue", "{{n}} 天内", ("n", _draft.Days)), 120, hits, () => StepDays(-1), () => StepDays(1));
		if (q.DaysNeeded > _draft.Days)
		{
			cy += SideNote(b, Tr.Get("errandForm.tooShort", "大概要 {{n}} 天，可能来不及", ("n", q.DaysNeeded)), dEnd, cy, x + labelW, x + w, Hearth.Rust);
		}
		cy += 62;
		Hearth.Ink(b, Tr.Get("errandForm.reward", "报酬"), new Vector2(x, cy - 12), Hearth.QuillSoft, 0.8f);
		int rEnd = Stepper(b, x + labelW, cy, $"{_draft.Reward}g", 120, hits, () => StepReward(-1), () => StepReward(1));
		string fair = Tr.Get("errandForm.fair", "公道价 {{n}}g", ("n", q.DefaultReward));
		int fw = (int)Hearth.Width(fair, 0.66f) + 28;
		Rectangle reset = rEnd + 12 + fw <= x + w ? new Rectangle(rEnd + 12, cy - 18, fw, 36) : new Rectangle(x + labelW, cy + 28, fw, 36);
		if (reset.Y > cy)
		{
			cy += 46;
		}
		bool off = _draft.Reward != q.DefaultReward;
		Hearth.Bordered(b, reset, off && Hearth.Hover(reset, _view) ? Hearth.AmberPale : Hearth.Parchment, off ? Hearth.Amber : Hearth.SlipEdge, 18);
		Hearth.InkMiddle(b, fair, reset, off ? Hearth.Amber : Hearth.QuillPale, 0.66f);
		if (off)
		{
			hits.Add(reset, () =>
			{
				Game1.playSound("shwip");
				ResetReward();
			}, _view);
		}
		cy += 44;
		Hearth.Ink(b, Tr.Get("errandForm.stepTip", "按住 Shift 点，一次多调一些。"), new Vector2(x + labelW, cy - 12), Hearth.QuillPale, 0.62f);
		cy += 26;
		Hearth.Ink(b, Tr.Get("errandForm.note", "留言"), new Vector2(x, cy + 10), Hearth.QuillSoft, 0.8f);
		_note.Bounds = new Rectangle(x + labelW, cy, Math.Min(w - labelW, 560), 44);
		_note.Draw(b);
		hits.Add(_note.Bounds, () => _fields.Focus(_note), _view);
		return cy + 44;
	}

	private static int StepSize(int small) => Game1.input.GetKeyboardState().IsKeyDown(Keys.LeftShift) || Game1.input.GetKeyboardState().IsKeyDown(Keys.RightShift) ? small * 10 : small;

	private static int Step(int value, int delta, int min, int max)
	{
		int next = Math.Clamp(value + delta, min, max);
		Game1.playSound(next == value ? "cancel" : "shwip");
		return next;
	}

	private void StepCount(int delta, int max)
	{
		_draft.Count = Step(_draft.Count, delta, 1, max);
		Changed();
	}

	private void StepDays(int delta)
	{
		_draft.Days = Step(_draft.Days, delta, ErrandRules.MinDays, ErrandRules.MaxDays);
		Changed();
	}

	private void StepReward(int delta)
	{
		ErrandQuote q = Quote();
		int next = Math.Clamp(_draft.Reward + delta * StepSize(10), q.MinReward, q.MaxReward);
		Game1.playSound(next == _draft.Reward ? "cancel" : "shwip");
		_draft.Reward = next;
		_rewardTouched = true;
	}

	/// <summary>步进器右边的小字；放不下就挪到下一行，返回多占的高度。</summary>
	private static int SideNote(SpriteBatch b, string text, int end, int cy, int wrapX, int right, Color color)
	{
		if (end + 12 + Hearth.Width(text, 0.66f) <= right)
		{
			Hearth.Ink(b, text, new Vector2(end + 12, cy - 10), color, 0.66f);
			return 0;
		}
		Hearth.Ink(b, Hearth.Truncate(text, right - wrapX, 0.66f), new Vector2(wrapX, cy + 26), color, 0.66f);
		return 26;
	}

	private int Stepper(SpriteBatch b, int x, int cy, string label, int w, Hits hits, Action prev, Action next)
	{
		Rectangle left = new Rectangle(x, cy - 20, 40, 40);
		Rectangle mid = new Rectangle(left.Right + 4, cy - 22, w, 44);
		Rectangle right = new Rectangle(mid.Right + 4, cy - 20, 40, 40);
		Hearth.FillRounded(b, left, Hearth.Hover(left, _view) ? Hearth.LeafPale : Hearth.ParchmentDeep, 20);
		Hearth.FillRounded(b, right, Hearth.Hover(right, _view) ? Hearth.LeafPale : Hearth.ParchmentDeep, 20);
		Hearth.Chevron(b, new Vector2(left.Center.X + 2, cy), left: true, Hearth.LeafDeep);
		Hearth.Chevron(b, new Vector2(right.Center.X - 2, cy), left: false, Hearth.LeafDeep);
		Hearth.Bordered(b, mid, Hearth.Slip, Hearth.SlipEdge, 10);
		Hearth.InkMiddle(b, label, mid, Hearth.Quill, 0.85f);
		hits.Add(left, prev, _view);
		hits.Add(right, next, _view);
		return right.Right;
	}

	private void DrawSummary(SpriteBatch b, Rectangle r, Hits hits, bool compact)
	{
		ErrandService service = Service!;
		ErrandQuote q = Quote();
		// 检查要扫一遍村民，几帧算一次就够。
		if (Game1.ticks / 15 != _problemTick)
		{
			_problemTick = Game1.ticks / 15;
			_problem = service.Problem(_draft);
		}
		string? problem = _problem;
		Hearth.CardSlip(b, r);
		int x = r.X + 20, w = r.Width - 40;
		Rectangle submit = new Rectangle(r.Right - 20 - (compact ? 180 : w), r.Bottom - 20 - 52, compact ? 180 : w, 52);
		Hearth.PressPlate(b, submit, _inline != null ? Tr.Get("errandForm.ask", "问问看") : Tr.Get("errandForm.submit", "发出委托"), primary: true, hover: Hearth.Hover(submit), disabled: problem != null);
		if (problem == null)
		{
			hits.Add(submit, Submit);
		}
		if (compact)
		{
			string line = problem ?? Tr.Get("errandForm.compact", "{{chance}} · 大概 {{days}} 天 · 要交 {{total}}g", ("chance", ErrandRules.ChanceLabel(q.Chance)), ("days", q.DaysNeeded), ("total", q.Total(_draft.Reward)));
			int lh = Hearth.LineHeight(0.75f);
			List<string> lines = Hearth.WrapLines(line, submit.X - x - 12, 0.75f).Take(3).ToList();
			int ly = r.Center.Y - lines.Count * lh / 2;
			foreach (string l in lines)
			{
				Hearth.Ink(b, l, new Vector2(x, ly), problem != null ? Hearth.Rust : Hearth.Quill, 0.75f);
				ly += lh;
			}
			return;
		}
		int y = r.Y + 20;
		Rectangle face = new Rectangle(x, y, 64, 64);
		Kit.Avatar(b, face, Ctx, _draft.Npc.Length > 0 ? _draft.Npc : null);
		string who = _draft.Npc.Length > 0 ? service.Display(_draft.Npc) : Tr.Get("errandForm.nobody", "还没选人");
		Hearth.InkBold(b, Hearth.Truncate(who, w - 80, 0.95f), new Vector2(face.Right + 14, y + 6), Hearth.Quill, 0.95f);
		Hearth.Ink(b, KindLabel(_draft.Kind), new Vector2(face.Right + 14, y + 38), Hearth.QuillSoft, 0.72f);
		y = face.Bottom + 18;
		if (_draft.Item is ErrandItem item)
		{
			string what = _draft.Kind == ErrandKinds.Relay
				? Tr.Get("errandForm.sumRelay", "把{{item}}捎给{{who}}", ("item", item.Stock > 1 ? $"{item.Name} ×{item.Stock}" : item.Name), ("who", _draft.Recipient.Length > 0 ? service.Display(_draft.Recipient) : "……"))
				: $"{item.Name} ×{_draft.Count}";
			foreach (string line in Hearth.WrapLines(what, w, 0.85f).Take(2))
			{
				Hearth.InkBold(b, line, new Vector2(x, y), Hearth.Quill, 0.85f);
				y += Hearth.LineHeight(0.85f);
			}
			y += 10;
		}
		Hearth.HLine(b, x, y, w, Hearth.Seam);
		y += 14;
		Hearth.Ink(b, Tr.Get("errandForm.chance", "把握"), new Vector2(x, y), Hearth.QuillSoft, 0.75f);
		Color chanceColor = q.Chance >= 65 ? Hearth.Leaf : q.Chance >= 40 ? Hearth.Amber : Hearth.Berry;
		string label = ErrandRules.ChanceLabel(q.Chance);
		Hearth.InkBold(b, label, new Vector2(x + w - Hearth.Width(label, 0.8f), y), chanceColor, 0.8f);
		y += Hearth.LineHeight(0.8f) + 6;
		Hearth.Gauge(b, new Rectangle(x, y, w, 10), q.Chance / 100f, chanceColor);
		y += 24;
		y = Row(b, x, y, w, Tr.Get("errandForm.daysNeeded", "大概要"), Tr.Get("errandForm.daysN", "{{n}} 天", ("n", q.DaysNeeded)), q.DaysNeeded > _draft.Days ? Hearth.Rust : Hearth.Quill);
		y = Row(b, x, y, w, Tr.Get("errandForm.rewardRow", "报酬"), $"{_draft.Reward}g", Hearth.Quill);
		if (q.Goods > 0)
		{
			y = Row(b, x, y, w, Tr.Get("errandForm.goods", "货款"), $"{q.Goods}g", Hearth.Quill);
		}
		Hearth.HLine(b, x, y + 2, w, Hearth.Seam);
		y += 12;
		int total = q.Total(_draft.Reward);
		y = Row(b, x, y, w, Tr.Get("errandForm.total", "现在要交"), $"{total}g", Game1.player.Money >= total ? Hearth.Amber : Hearth.Berry, bold: true);
		y = Row(b, x, y, w, Tr.Get("errandForm.wallet", "你有"), $"{Game1.player.Money}g", Hearth.QuillSoft);
		y += 8;
		string hint = problem ?? (_inline != null ? Tr.Get("errandForm.inlineHint", "他会当面回答你。") : Tr.Get("errandForm.hint", "钱先交出去；拒绝或没办成会全额退回。"));
		foreach (string line in Hearth.WrapLines(hint, w, 0.68f))
		{
			if (y + Hearth.LineHeight(0.68f) > submit.Y - 8)
			{
				break;
			}
			Hearth.Ink(b, line, new Vector2(x, y), problem != null ? Hearth.Rust : Hearth.QuillSoft, 0.68f);
			y += Hearth.LineHeight(0.68f);
		}
	}

	private static int Row(SpriteBatch b, int x, int y, int w, string label, string value, Color color, bool bold = false)
	{
		Hearth.Ink(b, label, new Vector2(x, y), Hearth.QuillSoft, 0.75f);
		float vw = Hearth.Width(value, 0.8f);
		if (bold)
		{
			Hearth.InkBold(b, value, new Vector2(x + w - vw, y), color, 0.8f);
		}
		else
		{
			Hearth.Ink(b, value, new Vector2(x + w - vw, y), color, 0.8f);
		}
		return y + Hearth.LineHeight(0.8f) + 8;
	}

	private static void Tooltip(SpriteBatch b, Rectangle anchor, string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		int w = (int)Hearth.Width(text, 0.7f) + 24;
		Rectangle r = new Rectangle(Math.Min(anchor.X, Game1.uiViewport.Width - w - 8), anchor.Bottom + 6, w, 34);
		if (r.Bottom > Game1.uiViewport.Height - 8)
		{
			r.Y = anchor.Y - 40;
		}
		Hearth.CastShadow(b, r, 8, 3);
		Hearth.Bordered(b, r, Hearth.Quill, Hearth.Quill, 8);
		Hearth.InkMiddle(b, text, r, Color.White, 0.7f);
	}

	private void Submit()
	{
		ErrandService? service = Service;
		if (service == null)
		{
			return;
		}
		_draft.Note = _note.Text;
		_fields.BlurAll();
		string name = service.Display(_draft.Npc);
		_posting = true;
		Errand? e;
		try
		{
			e = service.Post(_draft, _inline);
		}
		finally
		{
			_posting = false;
		}
		if (e == null)
		{
			Game1.playSound("cancel");
			Host.Notice(service.Problem(_draft) ?? Tr.Get("errandForm.failed", "没发出去"));
			return;
		}
		Game1.playSound("purchase");
		if (_inline != null)
		{
			_awaiting = e.State == ErrandStates.Waiting ? e.Id : null;
			return;
		}
		CommissionsPage.ShowMine = true;
		Host.Back();
		Host.Notice(e.State != ErrandStates.Waiting && e.Reply.Length > 0
			? Tr.Get("errandForm.answered", "{{name}}：{{line}}", ("name", name), ("line", e.Reply))
			: e.ReplyDay > 0
			? Tr.Get("errandForm.sentLater", "{{name}}没有手机，托人带了话，明天早上回你", ("name", name))
			: Tr.Get("errandForm.sent", "发给{{name}}了，等他回话", ("name", name)));
	}
}
