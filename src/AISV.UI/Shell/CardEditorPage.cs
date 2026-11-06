using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Persona;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>一位村民的角色卡：左边默认人设，中间自己写的，右边合起来 AI 实际看到的。</summary>
internal sealed class CardEditorPage : ShellPage
{
	private sealed record Slot(string Label, string Hint, TextArea Area, int Height);

	public static readonly PixelGlyph Pencil = new PixelGlyph(PixelGlyph.Palette(('e', new Color(250, 196, 170)), ('t', new Color(120, 120, 130))),
		"..........kk",
		".........kpk",
		"........kyyk",
		".......kyyk.",
		"......kyyk..",
		".....kyyk...",
		"....kyyk....",
		"...kyyk.....",
		"..keyk......",
		"..kek.......",
		".ktk........",
		".kk.........");

	private readonly string _npc;

	private readonly FieldSet _fields = new FieldSet();

	private readonly Slot[] _slots;

	private readonly Scroller _left = new Scroller();

	private readonly Scroller _mid = new Scroller();

	private readonly Scroller _right = new Scroller();

	private Rectangle _leftView;

	private Rectangle _midView;

	private Rectangle _rightView;

	private NpcPersona _default = null!;

	private string _saved = "";

	private string _previewKey = "\u0002";

	private NpcPersona _preview = null!;

	private string _warnedAt = "";

	private TextArea? _lastFocused;

	public CardEditorPage(ShellContext ctx, string npc) : base(ctx)
	{
		_npc = npc;
		_slots = new[]
		{
			new Slot(Tr.Get("card.displayName", "显示名"), Tr.Get("card.displayNameHint", "AI 用这个名字称呼这位村民；不填用游戏里的名字。"), _fields.Add(24, singleLine: true), 56),
			new Slot(Tr.Get("card.personality", "性格"), Tr.Get("card.personalityHint", "写了就整个替换默认人设；留空则保留默认，只把下面几项补上去。"), _fields.Add(1500, Tr.Get("card.personalityPlaceholder", "身份、性子、和谁亲近、怕什么、想要什么……")), 220),
			new Slot(Tr.Get("card.appearance", "外貌"), Tr.Get("card.appearanceHint", "长相、穿着、身上的小特征。"), _fields.Add(400), 96),
			new Slot(Tr.Get("card.lore", "背景"), Tr.Get("card.loreHint", "过去的经历、家里的事、没对人说过的秘密。"), _fields.Add(1000), 150),
			new Slot(Tr.Get("card.tags", "社交标签"), Tr.Get("card.tagsHint", "几个词就够，用顿号隔开，比如：爱热闹、记仇、护短。"), _fields.Add(200), 56),
			new Slot(Tr.Get("card.speech", "说话方式"), Tr.Get("card.speechHint", "口头禅、语气、爱用的词。"), _fields.Add(400), 96)
		};
		Fill(Ctx.Npcs.Cards.Get(npc));
		_saved = Snapshot();
	}

	public override string Title => Ctx.Display(_npc);

	public override bool CapturesKeys => _fields.Typing;

	public override void Reload() => _default = Ctx.Npcs.Preview(_npc, null);

	/// <summary>和 <see cref="_slots"/> 同样的顺序。</summary>
	private static string[] Values(CharacterCard c) => new[] { c.DisplayName, c.Personality, c.Appearance, c.Lore, c.SocialTags, c.Speech };

	/// <summary>填进各栏；返回有没有哪栏超长被截掉。</summary>
	private bool Fill(CharacterCard? card)
	{
		string[] values = Values(card ?? new CharacterCard());
		bool clipped = false;
		for (int i = 0; i < _slots.Length; i++)
		{
			string v = (values[i] ?? "").Trim();
			clipped |= v.Length > _slots[i].Area.MaxChars;
			_slots[i].Area.Text = v;
		}
		return clipped;
	}

	private CharacterCard Draft() => new CharacterCard
	{
		DisplayName = _slots[0].Area.Text.Trim(),
		Personality = _slots[1].Area.Text.Trim(),
		Appearance = _slots[2].Area.Text.Trim(),
		Lore = _slots[3].Area.Text.Trim(),
		SocialTags = _slots[4].Area.Text.Trim(),
		Speech = _slots[5].Area.Text.Trim()
	};

	private string Snapshot() => string.Join("\u0001", _slots.Select(s => s.Area.Text.Trim()));

	private bool Dirty => Snapshot() != _saved;

	public override void Scroll(int direction)
	{
		if (AisvTheme.Hover(_leftView))
		{
			_left.Wheel(direction);
		}
		else if (AisvTheme.Hover(_rightView))
		{
			_right.Wheel(direction);
		}
		else
		{
			_mid.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		KeyboardState s = Game1.input.GetKeyboardState();
		if (key == Keys.Enter && (s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl)))
		{
			Save();
			return true;
		}
		if (_fields.KeyPress(key))
		{
			return true;
		}
		return _mid.PageKeys(key, _midView.Height - 80);
	}

	public override bool CanLeave()
	{
		string now = Snapshot();
		if (!Dirty || now == _warnedAt)
		{
			return true;
		}
		_warnedAt = now;
		_fields.BlurAll();
		Host.Notice(Tr.Get("card.unsaved", "角色卡还没保存；再来一次就不要这些改动了"));
		return false;
	}

	public override void Closed() => _fields.BlurAll();

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		const int pad = 24, gap = 16, bottomH = 64;
		int x = area.X + pad, w = area.Width - pad * 2;
		int top = area.Y + 16;
		int bottom = area.Bottom - 16 - bottomH;
		int side = Math.Clamp(w * 28 / 100, 240, 420);
		int midW = w - side * 2 - gap * 2;
		Rectangle left = new Rectangle(x, top, side, bottom - 12 - top);
		Rectangle mid = new Rectangle(left.Right + gap, top, midW, left.Height);
		Rectangle right = new Rectangle(mid.Right + gap, top, side, left.Height);
		DrawDefault(b, left, hits);
		DrawFields(b, mid, hits);
		DrawPreview(b, right);
		Bottom(b, new Rectangle(x, bottom, w, bottomH), hits);
	}

	private static Rectangle Column(SpriteBatch b, Rectangle r, string title, Color band)
	{
		AisvTheme.Bordered(b, r, AisvTheme.Card, AisvTheme.CardBorder, 14);
		Rectangle head = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 48);
		AisvTheme.FillRounded(b, head, band, 12);
		AisvTheme.Bold(b, title, new Vector2(r.X + 20, r.Y + 13), AisvTheme.Text, 0.85f);
		return new Rectangle(r.X + 4, head.Bottom + 4, r.Width - 8, r.Bottom - head.Bottom - 10);
	}

	private void DrawDefault(SpriteBatch b, Rectangle r, Hits hits)
	{
		Rectangle body = Column(b, r, Tr.Get("card.default", "默认设定"), AisvTheme.PageDeep);
		string src = CardsPage.SourceName(_default.Source);
		AisvTheme.Chip(b, new Vector2(r.Right - 16 - AisvTheme.ChipWidth(src, 0.66f), r.Y + 13), src, AisvTheme.Card, AisvTheme.TextMuted, 0.66f);
		Rectangle copy = new Rectangle(body.X + 12, body.Bottom - 48, body.Width - 24, 44);
		_leftView = new Rectangle(body.X, body.Y, body.Width, copy.Y - 8 - body.Y);
		Prose(b, _leftView, _left, _default.Personality, AisvTheme.TextMuted);
		AisvTheme.Button(b, copy, Tr.Get("card.copyDefault", "抄进性格栏再改"), hover: AisvTheme.Hover(copy), scale: 0.75f);
		hits.Add(copy, () =>
		{
			TextArea p = _slots[1].Area;
			if (p.Text.Trim().Length > 0)
			{
				Host.Notice(Tr.Get("card.personalityNotEmpty", "性格栏里已经有字了；先清空再抄"));
				return;
			}
			p.Text = _default.Personality.Length > p.MaxChars ? _default.Personality.Substring(0, p.MaxChars) : _default.Personality;
			_fields.Focus(p);
			Game1.playSound("coin");
		});
	}

	private void DrawPreview(SpriteBatch b, Rectangle r)
	{
		string key = Snapshot();
		if (key != _previewKey)
		{
			_previewKey = key;
			_preview = Ctx.Npcs.Preview(_npc, Draft());
		}
		CharacterCard draft = Draft();
		(string mode, Color fill, Color ink) = draft.IsEmpty ? (Tr.Get("card.modeDefault", "用默认"), AisvTheme.Card, AisvTheme.TextMuted)
			: CardsPage.Badge(draft);
		if (fill == AisvTheme.AccentSoft)
		{
			fill = AisvTheme.Card;
		}
		Rectangle body = Column(b, r, Tr.Get("card.preview", "AI 实际看到的"), AisvTheme.AccentSoft);
		AisvTheme.Chip(b, new Vector2(r.Right - 16 - AisvTheme.ChipWidth(mode, 0.66f), r.Y + 13), mode, fill, ink, 0.66f);
		string name = _preview.DisplayNameOverride.Length > 0 ? _preview.DisplayNameOverride : Ctx.Display(_npc);
		string text = Tr.Get("card.previewName", "称呼：{{name}}", ("name", name)) + "\n\n" + _preview.Personality;
		string count = Tr.Get("card.chars", "{{n}} 字", ("n", _preview.Personality.Length));
		Vector2 cp = new Vector2(body.Right - 12 - AisvTheme.Width(count, 0.66f), body.Bottom - AisvTheme.LineHeight(0.66f) - 6);
		_rightView = new Rectangle(body.X, body.Y, body.Width, (int)cp.Y - 6 - body.Y);
		Prose(b, _rightView, _right, text, AisvTheme.Text);
		AisvTheme.DrawText(b, count, cp, _preview.Personality.Length > 2400 ? AisvTheme.Warn : AisvTheme.TextFaint, 0.66f);
	}

	private static void Prose(SpriteBatch b, Rectangle view, Scroller scroll, string text, Color color)
	{
		const float scale = 0.76f;
		int lh = AisvTheme.LineHeight(scale) + 4;
		AisvTheme.BeginClip(b, view);
		int y = view.Y + 10 - scroll.Offset;
		foreach (string line in AisvTheme.WrapLines(text, view.Width - 36, scale, paragraphGap: true))
		{
			if (y + lh >= view.Y && y <= view.Bottom)
			{
				AisvTheme.DrawText(b, line, new Vector2(view.X + 16, y), color, scale);
			}
			y += lh;
		}
		AisvTheme.EndClip(b);
		scroll.Finish(b, view, y + 10, 6);
	}

	private void DrawFields(SpriteBatch b, Rectangle r, Hits hits)
	{
		Rectangle body = Column(b, r, Tr.Get("card.mine", "我的设定"), AisvTheme.GoldSoft);
		_midView = body;
		AisvTheme.BeginClip(b, body);
		int x = body.X + 16, w = body.Width - 32;
		int y = body.Y + 12 - _mid.Offset;
		foreach (Slot s in _slots)
		{
			AisvTheme.Bold(b, s.Label, new Vector2(x, y), AisvTheme.Text, 0.8f);
			string count = $"{s.Area.Text.Length}/{s.Area.MaxChars}";
			AisvTheme.DrawText(b, count, new Vector2(x + w - AisvTheme.Width(count, 0.62f), y + 4), AisvTheme.TextFaint, 0.62f);
			y += AisvTheme.LineHeight(0.8f) + 2;
			foreach (string line in AisvTheme.WrapLines(s.Hint, w, 0.68f))
			{
				AisvTheme.DrawText(b, line, new Vector2(x, y), AisvTheme.TextMuted, 0.68f);
				y += AisvTheme.LineHeight(0.68f);
			}
			y += 6;
			s.Area.Bounds = new Rectangle(x, y, w, s.Height);
			s.Area.Draw(b);
			TextArea area = s.Area;
			hits.Add(s.Area.Bounds, () => _fields.Focus(area), body);
			y += s.Height + 18;
		}
		AisvTheme.EndClip(b);
		int bottom = y + _mid.Offset;
		_mid.Finish(b, body, y, 6);
		KeepFocusedVisible(body, bottom);
	}

	/// <summary>换到视野外的框时（比如按 Tab），把它滚进来；之后滚轮照常能滚。</summary>
	private void KeepFocusedVisible(Rectangle view, int contentBottom)
	{
		TextArea? f = _fields.Focused;
		if (f == _lastFocused)
		{
			return;
		}
		_lastFocused = f;
		if (f == null || contentBottom <= view.Bottom)
		{
			return;
		}
		if (f.Bounds.Bottom > view.Bottom)
		{
			_mid.By(f.Bounds.Bottom - view.Bottom + 12);
		}
		else if (f.Bounds.Y - 60 < view.Y)
		{
			_mid.By(f.Bounds.Y - 60 - view.Y);
		}
	}

	private void Bottom(SpriteBatch b, Rectangle r, Hits hits)
	{
		int x = r.X;
		x = SmallButton(b, x, r, Tr.Get("card.export", "复制成文字"), hits, Export) + 10;
		x = SmallButton(b, x, r, Tr.Get("card.import", "从剪贴板导入"), hits, Import) + 10;
		x = SmallButton(b, x, r, Tr.Get("card.reset", "恢复默认"), hits, Reset) + 10;
		if (Ctx.TryChat != null)
		{
			x = SmallButton(b, x, r, Tr.Get("card.trial", "试聊"), hits, () => Host.Push(new CardTrialPage(Ctx, _npc, Draft())));
		}
		Rectangle save = new Rectangle(r.Right - 200, r.Center.Y - 26, 200, 52);
		bool dirty = Dirty;
		AisvTheme.Button(b, save, dirty ? Tr.Get("card.save", "保存") : Tr.Get("card.savedShort", "已保存"), primary: true, hover: AisvTheme.Hover(save), disabled: !dirty);
		if (dirty)
		{
			hits.Add(save, Save);
		}
		string tip = Tr.Get("card.keys", "Tab 下一栏 · Ctrl+Enter 保存");
		float tw = AisvTheme.Width(tip, 0.66f);
		if (save.X - 20 - tw > x + 160)
		{
			AisvTheme.DrawText(b, tip, new Vector2(save.X - 20 - tw, r.Center.Y - AisvTheme.LineHeight(0.66f) / 2), AisvTheme.TextFaint, 0.66f);
		}
	}

	private static int SmallButton(SpriteBatch b, int x, Rectangle bar, string label, Hits hits, Action action)
	{
		Rectangle r = new Rectangle(x, bar.Center.Y - 22, (int)AisvTheme.Width(label, 0.75f) + 36, 44);
		AisvTheme.Button(b, r, label, hover: AisvTheme.Hover(r), scale: 0.75f);
		hits.Add(r, action);
		return r.Right;
	}

	private void Save()
	{
		CharacterCard card = Draft();
		if (card.DisplayName.Length > 0 && Ctx.Npcs.Names.TakenByOther(card.DisplayName, _npc))
		{
			Host.Notice(Tr.Get("card.nameTaken", "“{{name}}”已经是别的村民的名字了，换一个吧", ("name", card.DisplayName)));
			_fields.Focus(_slots[0].Area);
			return;
		}
		CharacterCard before = Ctx.Npcs.Cards.Get(_npc) ?? new CharacterCard();
		try
		{
			Ctx.Npcs.Cards.Save(_npc, card);
			Ctx.Npcs.RefreshNames();
		}
		catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
		{
			Host.Notice(Tr.Get("card.saveFailed", "存不了：{{reason}}", ("reason", ex.Message)));
			return;
		}
		_saved = Snapshot();
		_warnedAt = "";
		CardsPage.Changed = true;
		if (Character(before) != Character(card))
		{
			Ctx.Arcs?.OnCardRewritten(_npc);
		}
		_fields.BlurAll();
		Game1.playSound("newRecipe");
		Host.Notice(card.IsEmpty ? Tr.Get("card.resetDone", "{{name}} 用回默认人设了", ("name", Ctx.Display(_npc)))
			: Tr.Get("card.saved", "{{name}} 的角色卡存好了，所有存档通用", ("name", Ctx.Display(_npc))));
	}

	/// <summary>决定这个人是谁的几栏（不算名字和外貌）。</summary>
	private static string Character(CharacterCard c) => string.Join("\u0001", c.Personality.Trim(), c.Lore.Trim(), c.SocialTags.Trim(), c.Speech.Trim());

	private void Reset()
	{
		if (Draft().IsEmpty)
		{
			return;
		}
		Fill(null);
		_fields.BlurAll();
		Game1.playSound("trashcan");
		Host.Notice(Tr.Get("card.resetHint", "清空了；点保存才算数"));
	}

	private void Export()
	{
		if (!DesktopClipboard.SetText(CharacterCard.ToJson(Draft())))
		{
			Host.Notice(Tr.Get("card.exportFailed", "剪贴板用不了，没复制上"));
			return;
		}
		Game1.playSound("coin");
		Host.Notice(Tr.Get("card.exported", "复制好了，可以贴给别人"));
	}

	private void Import()
	{
		string text = "";
		CharacterCard? card = DesktopClipboard.GetText(ref text) ? CharacterCard.FromJson(text) : null;
		if (card == null || card.IsEmpty)
		{
			Host.Notice(Tr.Get("card.importFailed", "剪贴板里不是角色卡"));
			return;
		}
		card.DisplayName = card.DisplayName.Replace('\n', ' ').Replace('\r', ' ');
		bool clipped = Fill(card);
		_fields.BlurAll();
		Game1.playSound("coin");
		Host.Notice(clipped ? Tr.Get("card.importedClipped", "导进来了，有几栏太长截掉了一些；点保存才算数") : Tr.Get("card.imported", "导进来了；点保存才算数"));
	}
}
