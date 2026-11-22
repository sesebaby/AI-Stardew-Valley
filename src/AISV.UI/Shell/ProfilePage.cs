using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>我的设定：四个问题（你是谁、为什么搬来、还有什么、一眼看上去）和生日。村民按交情知道多少。</summary>
internal sealed class ProfilePage : ShellPage
{
	public const int MaxChars = 500;

	private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

	private readonly FieldSet _fields = new FieldSet();

	private readonly TextArea[] _areas;

	private readonly bool _welcome;

	private string _season = "";

	private int _day;

	private string _saved = "";

	private string _warnedAt = "";

	/// <summary>当前页签：0～3 是四个问题，4 是生日。</summary>
	private int _tab;

	public ProfilePage(ShellContext ctx, bool welcome = false) : base(ctx)
	{
		_welcome = welcome;
		_areas = new[]
		{
			_fields.Add(MaxChars, Tr.Get("profile.placeholder1", "比如：在城里写了十年代码，越来越不知道自己在忙什么……")),
			_fields.Add(MaxChars, Tr.Get("profile.placeholder2", "比如：爷爷留下了这块地，我想换一种活法……")),
			_fields.Add(MaxChars, Tr.Get("profile.placeholder3", "比如：会弹吉他、做饭还行，就是一打雷就睡不着……")),
			_fields.Add(MaxChars, Tr.Get("profile.placeholder4", "比如：短短的紫头发，总穿一件绿外套，称呼我用“她”……"))
		};
		PlayerProfile? p = Profile();
		if (p != null)
		{
			_areas[0].Text = p.Intro;
			_areas[1].Text = p.CameBecause;
			_areas[2].Text = p.Notes;
			_areas[3].Text = p.QuickFacts;
			_season = p.HasBirthday ? p.BornSeason.ToLowerInvariant() : "";
			_day = p.HasBirthday ? p.BornDay : 0;
		}
		_saved = Snapshot();
		_fields.Focus(_areas[0]);
	}

	public override string Title => Tr.Get("profile.title", "我的设定");

	public override bool CapturesKeys => _fields.Typing;

	/// <summary>存过、而且至少写了一项或设了生日（存档没打开时当作写过，不去提醒）。</summary>
	public static bool Filled(ShellContext ctx)
	{
		PlayerProfile? p = ctx.Store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);
		return p == null || p.Filled && (p.HasBirthday || new[] { p.Intro, p.CameBecause, p.Notes, p.QuickFacts }.Any(t => !string.IsNullOrWhiteSpace(t)));
	}

	private PlayerProfile? Profile() => Ctx.Store()?.Get(PlayerProfile.Path, PlayerProfile.Doc);

	private string Snapshot() => string.Join("\u0001", _areas.Select(a => a.Text.Trim())) + "\u0001" + _season + _day;

	private bool Dirty => Snapshot() != _saved;

	public override bool KeyPress(Keys key)
	{
		KeyboardState s = Game1.input.GetKeyboardState();
		if (key == Keys.Enter && (s.IsKeyDown(Keys.LeftControl) || s.IsKeyDown(Keys.RightControl)))
		{
			Save();
			return true;
		}
		return _fields.KeyPress(key);
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
		Host.Notice(Tr.Get("profile.unsaved", "还没保存；再来一次就不要这些改动了"));
		return false;
	}

	public override void Closed() => _fields.BlurAll();

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		const int pad = 28;
		int x = area.X + pad, w = area.Width - pad * 2;
		int y = area.Y + 18;
		string key = Ctx.Config().KeyAboutMe?.Trim() ?? "";
		string intro = !_welcome ? Tr.Get("profile.intro", "村民会照着这些认识你：交情浅的只听说过一点，熟了才知道得多。")
			: key.Length > 0 ? Tr.Get("profile.welcome", "欢迎来到山谷！先介绍一下自己吧，村民会照着这些认识你。不想写可以先返回，以后按 {{key}} 再来。", ("key", key))
			: Tr.Get("profile.welcomeMenu", "欢迎来到山谷！先介绍一下自己吧，村民会照着这些认识你。不想写可以先返回，以后在设置里再来。");
		Hearth.Ink(b, Hearth.Truncate(intro, w - 200, 0.8f), new Vector2(x, y), Hearth.QuillSoft, 0.8f);
		int filled = _areas.Count(a => a.Text.Trim().Length > 0);
		string progress = Tr.Get("profile.progress", "答了 {{filled}}/{{total}}", ("filled", filled), ("total", _areas.Length));
		Hearth.Tag(b, new Vector2(x + w - Hearth.TagWidth(progress, 0.75f), y - 2), progress, filled == _areas.Length ? Hearth.LeafPale : Hearth.ParchmentDeep, Hearth.LeafDeep, 0.75f);
		y += 48;
		// 按 Tab 换到下一个输入框时，页签跟着走。
		int focused = Array.IndexOf(_areas, _fields.Focused);
		if (focused >= 0)
		{
			_tab = focused;
		}
		y = Tabs(b, x, y, w, hits) + 14;
		const int bottomH = 64;
		Rectangle card = new Rectangle(x, y, w, area.Bottom - 16 - bottomH - 12 - y);
		if (_tab < _areas.Length)
		{
			Field(b, card, Label(_tab).Title, Label(_tab).Hint, _areas[_tab], hits);
		}
		else
		{
			Birthday(b, card, hits);
		}
		Bottom(b, new Rectangle(x, area.Bottom - 16 - bottomH, w, bottomH), hits);
	}

	private static (string Title, string Hint) Label(int i) => i switch
	{
		0 => (Tr.Get("profile.q1", "你是谁"), Tr.Get("profile.h1", "名字、年纪、以前做什么、什么性子。")),
		1 => (Tr.Get("profile.q2", "为什么搬来"), Tr.Get("profile.h2", "一个念想、一次出走、一份遗产……")),
		2 => (Tr.Get("profile.q3", "还想让大家知道的"), Tr.Get("profile.h3", "爱好、怕什么、小毛病、秘密、家里人。")),
		_ => (Tr.Get("profile.q4", "一眼看上去"), Tr.Get("profile.h4", "选填：长相、穿着、希望别人怎么称呼你。"))
	};

	/// <summary>五个页签：四个问题和生日，答过的带一个绿点。</summary>
	private int Tabs(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		const int h = 52, gap = 10;
		int count = _areas.Length + 1;
		int tw = (w - gap * (count - 1)) / count;
		for (int i = 0; i < count; i++)
		{
			Rectangle r = new Rectangle(x + i * (tw + gap), y, tw, h);
			bool sel = i == _tab;
			bool done = i < _areas.Length ? _areas[i].Text.Trim().Length > 0 : _day > 0;
			string label = i < _areas.Length ? Label(i).Title : Tr.Get("profile.birthday", "生日");
			Color fill = sel ? Hearth.Leaf : Hearth.Hover(r) ? Hearth.Honey : Hearth.ParchmentDeep;
			Hearth.FillRounded(b, r, fill, 14);
			string shown = Hearth.Truncate(label, tw - 44, 0.82f);
			float lw = Hearth.Width(shown, 0.82f);
			int lx = r.Center.X - (int)(lw + (done ? 18 : 0)) / 2;
			if (done)
			{
				Hearth.FillRounded(b, new Rectangle(lx, r.Center.Y - 5, 10, 10), sel ? Color.White : Hearth.Leaf, 5);
				lx += 18;
			}
			if (sel)
			{
				Hearth.InkBold(b, shown, new Vector2(lx, r.Center.Y - 13), Color.White, 0.82f);
			}
			else
			{
				Hearth.Ink(b, shown, new Vector2(lx, r.Center.Y - 13), Hearth.Quill, 0.82f);
			}
			int k = i;
			hits.Add(r, () => Pick(k));
		}
		return y + h;
	}

	private void Pick(int tab)
	{
		if (tab == _tab)
		{
			return;
		}
		Game1.playSound("smallSelect");
		_tab = tab;
		if (tab < _areas.Length)
		{
			_fields.Focus(_areas[tab]);
		}
		else
		{
			_fields.BlurAll();
		}
	}

	private void Field(SpriteBatch b, Rectangle card, string title, string hint, TextArea text, Hits hits)
	{
		Hearth.Bordered(b, card, Hearth.Slip, text.Selected ? Hearth.Leaf : Hearth.SlipEdge, 14);
		Hearth.InkBold(b, title, new Vector2(card.X + 28, card.Y + 22), Hearth.Quill, 1.05f);
		Hearth.Ink(b, hint, new Vector2(card.X + 28, card.Y + 62), Hearth.QuillSoft, 0.75f);
		string count = $"{text.Text.Length}/{text.MaxChars}";
		Hearth.Ink(b, count, new Vector2(card.Right - 28 - Hearth.Width(count, 0.68f), card.Y + 28), text.Text.Length >= text.MaxChars ? Hearth.Rust : Hearth.QuillPale, 0.68f);
		text.Bounds = new Rectangle(card.X + 20, card.Y + 100, card.Width - 40, card.Height - 120);
		text.Draw(b);
		hits.Add(card, () => _fields.Focus(text));
	}

	private void Birthday(SpriteBatch b, Rectangle r, Hits hits)
	{
		Hearth.Bordered(b, r, Hearth.Slip, Hearth.SlipEdge, 14);
		Hearth.InkBold(b, Tr.Get("profile.birthday", "生日"), new Vector2(r.X + 28, r.Y + 22), Hearth.Quill, 1.05f);
		Hearth.Ink(b, Tr.Get("profile.birthdayHint", "那天村民会给你发消息、寄礼物；节日那几天不能选。"), new Vector2(r.X + 28, r.Y + 62), Hearth.QuillSoft, 0.75f);
		Rectangle cake = new Rectangle(r.Center.X - 160, r.Center.Y - 84, 320, 90);
		Hearth.FillRounded(b, cake, Hearth.RosePale, 20);
		bool set = _day > 0;
		string date = set ? ShellText.Season(_season) + " " + Tr.Get("profile.day", "{{day}} 日", ("day", _day)) : Tr.Get("profile.notSet", "还没设");
		Vector2 dateSize = Hearth.Measure(date, 1f, Game1.dialogueFont);
		Hearth.Ink(b, date, new Vector2(cake.Center.X - dateSize.X / 2f, cake.Center.Y - dateSize.Y / 2f), set ? Hearth.Rose : Hearth.QuillPale, 1f, Game1.dialogueFont);
		int cy = cake.Bottom + 44;
		const int stepW = 40 + 4 + 120 + 4 + 40;
		int sx = r.Center.X - (stepW * 2 + 24) / 2;
		string season = set ? ShellText.Season(_season) : "—";
		int end = Stepper(b, sx, cy, season, 120, hits, () => Shift(-1, 0), () => Shift(1, 0));
		Stepper(b, end + 24, cy, set ? Tr.Get("profile.day", "{{day}} 日", ("day", _day)) : "—", 120, hits, () => Shift(0, -1), () => Shift(0, 1));
		if (set)
		{
			string label = Tr.Get("profile.clearBirthday", "不设生日");
			Rectangle clear = new Rectangle(r.Center.X - 70, cy + 44, 140, 44);
			Hearth.PressPlate(b, clear, label, hover: Hearth.Hover(clear), scale: 0.75f);
			hits.Add(clear, () =>
			{
				_season = "";
				_day = 0;
			});
		}
	}

	private void Bottom(SpriteBatch b, Rectangle r, Hits hits)
	{
		string tip = Tr.Get("profile.keys", "Tab 换下一个问题 · Ctrl+Enter 保存 · Esc 返回");
		Hearth.Ink(b, tip, new Vector2(r.X + 4, r.Center.Y - Hearth.LineHeight(0.7f) / 2), Hearth.QuillPale, 0.7f);
		Rectangle save = new Rectangle(r.Right - 200, r.Center.Y - 26, 200, 52);
		bool dirty = Dirty;
		Hearth.PressPlate(b, save, dirty ? Tr.Get("profile.save", "保存") : Tr.Get("profile.savedShort", "已保存"), primary: true, hover: Hearth.Hover(save), disabled: !dirty);
		if (dirty)
		{
			hits.Add(save, Save);
		}
	}

	private static int Stepper(SpriteBatch b, int x, int cy, string label, int w, Hits hits, Action prev, Action next)
	{
		Rectangle left = new Rectangle(x, cy - 20, 40, 40);
		Rectangle mid = new Rectangle(left.Right + 4, cy - 22, w, 44);
		Rectangle right = new Rectangle(mid.Right + 4, cy - 20, 40, 40);
		Hearth.FillRounded(b, left, Hearth.Hover(left) ? Hearth.RosePale : Hearth.ParchmentDeep, 20);
		Hearth.FillRounded(b, right, Hearth.Hover(right) ? Hearth.RosePale : Hearth.ParchmentDeep, 20);
		Hearth.Chevron(b, new Vector2(left.Center.X + 2, cy), left: true, Hearth.Rose);
		Hearth.Chevron(b, new Vector2(right.Center.X - 2, cy), left: false, Hearth.Rose);
		Hearth.Bordered(b, mid, Hearth.Slip, Hearth.SlipEdge, 10);
		Hearth.InkMiddle(b, label, mid, Hearth.Quill, 0.85f);
		hits.Add(left, prev);
		hits.Add(right, next);
		return right.Right;
	}

	/// <summary>换季节或日子：日子在"不设"和 1～28 之间转，跳过节日；没设时从春天开始。</summary>
	private void Shift(int seasons, int days)
	{
		Game1.playSound("shwip");
		if (_day <= 0)
		{
			_season = "spring";
			_day = days < 0 ? BirthdayRules.StepDay(0, -1, d => IsFestival(_season, d)) : BirthdayRules.SettleDay(1, d => IsFestival(_season, d));
			return;
		}
		int si = Math.Max(0, Array.IndexOf(Seasons, _season));
		_season = Seasons[(si + seasons + 4) % 4];
		if (days != 0)
		{
			_day = BirthdayRules.StepDay(_day, days, d => IsFestival(_season, d));
		}
		else
		{
			_day = BirthdayRules.SettleDay(Math.Max(1, _day), d => IsFestival(_season, d));
		}
		if (_day == 0)
		{
			_season = "";
		}
	}

	private static bool IsFestival(string season, int day) => Enum.TryParse(season, true, out Season s) && Utility.isFestivalDay(day, s);

	private void Save()
	{
		DocumentStore? store = Ctx.Store();
		if (store == null)
		{
			Host.Notice(Tr.Get("profile.noSave", "进了存档才能保存"));
			return;
		}
		PlayerProfile p = store.Get(PlayerProfile.Path, PlayerProfile.Doc);
		p.Intro = _areas[0].Text.Trim();
		p.CameBecause = _areas[1].Text.Trim();
		p.Notes = _areas[2].Text.Trim();
		p.QuickFacts = _areas[3].Text.Trim();
		p.BornSeason = _day > 0 ? _season : "";
		p.BornDay = Math.Max(0, _day);
		p.Filled = true;
		p.EditedOn = Dates.Label(new GameDate(Game1.dayOfMonth, Game1.currentSeason, Game1.year));
		store.MarkDirty(PlayerProfile.Path);
		if (store.Commit(new[] { PlayerProfile.Path }) == 0)
		{
			Host.Notice(Tr.Get("profile.saveFailed", "没存上，日志里有原因；过一会儿再试试"));
			return;
		}
		_saved = Snapshot();
		_warnedAt = "";
		_fields.BlurAll();
		Game1.playSound("newRecipe");
		Host.Notice(Tr.Get("profile.saved", "我的设定存好了"));
	}
}
