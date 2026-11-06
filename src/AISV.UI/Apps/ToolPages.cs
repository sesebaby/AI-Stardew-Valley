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
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;

namespace AISV.UI.Apps;

/// <summary>小工具页的共同骨架：一列能滚动的内容。</summary>
internal abstract class ToolPage : ShellPage
{
	protected readonly Scroller Scroller = new Scroller();

	protected Rectangle View;

	protected ToolPage(ShellContext ctx) : base(ctx)
	{
	}

	public override void Scroll(int direction) => Scroller.Wheel(direction);

	public override bool KeyPress(Keys key) => Scroller.PageKeys(key, View.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		View = area;
		Hearth.BeginClip(b, area);
		int bottom = Column(b, area.X + 40, area.Y + 22 - Scroller.Offset, area.Width - 96, hits);
		Hearth.EndClip(b);
		Scroller.Finish(b, area, bottom + 16);
		Topmost(b);
	}

	protected abstract int Column(SpriteBatch b, int x, int y, int w, Hits hits);

	protected virtual void Topmost(SpriteBatch b)
	{
	}

	protected static int Lead(SpriteBatch b, string line, int x, int y, int w)
	{
		List<string> rows = Hearth.WrapLines(line, w, 0.8f);
		for (int i = 0; i < rows.Count; i++)
		{
			Hearth.Ink(b, rows[i], new Vector2(x, y + i * Hearth.LineHeight(0.8f)), Hearth.QuillSoft, 0.8f);
		}
		return y + rows.Count * Hearth.LineHeight(0.8f) + 18;
	}
}

internal sealed class PerfectionPage : ToolPage
{
	private List<FarmGoal> _goals = new List<FarmGoal>();

	private double _score;

	private DateTime _readAt = DateTime.MinValue;

	public PerfectionPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("tools.perfection", "完美度");

	/// <summary>和游戏算的一样：全农场最好的成绩，再加上买的豁免。</summary>
	public static double Score() => Math.Min(100, Utility.percentGameComplete() * 100 + Game1.netWorldState.Value.PerfectionWaivers);

	private static double BestOnFarm(Func<Farmer, float> reached) => Utility.GetFarmCompletion(reached).Value;

	private static double AnyoneOnFarm(Func<Farmer, bool> done) => Utility.GetFarmCompletion(done).Value ? 1 : 0;

	private static List<FarmGoal> ReadGoals() => new List<FarmGoal>
	{
		new FarmGoal(Tr.Get("tools.perf.shipped", "出货过的东西"), BestOnFarm(Utility.getFarmerItemsShippedPercent), 15),
		new FarmGoal(Tr.Get("tools.perf.friends", "满心的朋友"), BestOnFarm(Utility.getMaxedFriendshipPercent), 11),
		new FarmGoal(Tr.Get("tools.perf.cooked", "做过的菜"), BestOnFarm(Utility.getCookedRecipesPercent), 10),
		new FarmGoal(Tr.Get("tools.perf.crafted", "做过的物品"), BestOnFarm(Utility.getCraftedRecipesPercent), 10),
		new FarmGoal(Tr.Get("tools.perf.fish", "钓过的鱼"), BestOnFarm(Utility.getFishCaughtPercent), 10),
		new FarmGoal(Tr.Get("tools.perf.obelisks", "方尖塔"), Utility.GetObeliskTypesBuilt() / 4f, 4),
		new FarmGoal(Tr.Get("tools.perf.clock", "黄金时钟"), Game1.IsBuildingConstructed("Gold Clock") ? 1 : 0, 10),
		new FarmGoal(Tr.Get("tools.perf.monsters", "冒险家公会的目标"), AnyoneOnFarm(f => f.hasCompletedAllMonsterSlayerQuests.Value), 10),
		new FarmGoal(Tr.Get("tools.perf.level", "农夫等级"), BestOnFarm(f => Math.Min(f.Level, 25) / 25f), 5),
		new FarmGoal(Tr.Get("tools.perf.stardrops", "星之果实"), AnyoneOnFarm(Utility.foundAllStardrops), 10),
		new FarmGoal(Tr.Get("tools.perf.walnuts", "金核桃"), Game1.netWorldState.Value.GoldenWalnutsFound / 130f, 5)
	};

	public override void Reload()
	{
		if ((DateTime.UtcNow - _readAt).TotalSeconds >= 10)
		{
			_readAt = DateTime.UtcNow;
			_goals = FarmGoals.NextUp(ReadGoals()).ToList();
			_score = Score();
		}
	}

	protected override int Column(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		Rectangle banner = new Rectangle(x, y, w, 120);
		Hearth.CardSlip(b, banner);
		Hearth.InkBold(b, _score.ToString("0.0") + "%", new Vector2(banner.X + 28, banner.Y + 24), Hearth.Quill, 1.6f);
		Hearth.Ink(b, Hearth.Truncate(Tr.Get("tools.perf.lead", "到 100% 就能登上山顶。"), w - 56, 0.76f), new Vector2(banner.X + 28, banner.Y + 76), Hearth.QuillSoft, 0.76f);
		y = banner.Bottom + 16;
		bool tight = w < 560;
		foreach (FarmGoal goal in _goals)
		{
			Rectangle row = new Rectangle(x, y, w, tight ? 88 : 64);
			Hearth.CardSlip(b, row);
			Hearth.Ink(b, goal.Label, new Vector2(row.X + 20, row.Y + 18), Hearth.Quill, 0.84f);
			Rectangle bar = tight ? new Rectangle(row.X + 20, row.Y + 56, row.Width - 40, 16) : new Rectangle(row.X + 300, row.Center.Y - 8, row.Width - 460, 16);
			Hearth.Gauge(b, bar, (float)goal.Reached, goal.Finished ? Hearth.Amber : Hearth.Leaf);
			string marks = goal.Marks.ToString("0.0") + " / " + goal.FullMarks;
			Hearth.Ink(b, marks, new Vector2(row.Right - 20 - Hearth.Width(marks, 0.76f), row.Y + 20), Hearth.QuillSoft, 0.76f);
			y = row.Bottom + 10;
		}
		return y;
	}
}

internal sealed class TodoPage : ToolPage
{
	private readonly TextBox _box = new TextBox(null, null, Game1.smallFont, Hearth.Quill) { textLimit = 80, limitWidth = false };

	public TodoPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("tools.todo", "待办");

	public override bool CapturesKeys => _box.Selected;

	private TodoPad Pad => new TodoPad(Ctx.Store);

	public override void Closed() => _box.Selected = false;

	public override bool KeyPress(Keys key)
	{
		if (!_box.Selected)
		{
			return base.KeyPress(key);
		}
		if (key == Keys.Enter)
		{
			Jot();
		}
		else if (key == Keys.Escape)
		{
			_box.Selected = false;
		}
		return true;
	}

	private void Jot()
	{
		switch (Pad.Jot(_box.Text))
		{
			case JotOutcome.Added:
				_box.Text = "";
				Game1.playSound("coin");
				break;
			case JotOutcome.PadFull:
				Host.Notice(Tr.Get("tools.todo.full", "本子写满了，先划掉几件吧。"));
				break;
		}
	}

	protected override int Column(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		Rectangle field = new Rectangle(x, y, w - 130, 52);
		Hearth.Field(b, field, _box.Selected);
		string typed = Hearth.Truncate(_box.Text ?? "", field.Width - 32, 0.84f);
		bool blank = typed.Length == 0 && !_box.Selected;
		Hearth.Ink(b, blank ? Hearth.Truncate(Tr.Get("tools.todo.hint", "写下要做的事，回车记下来"), field.Width - 32, 0.84f) : typed, new Vector2(field.X + 16, field.Y + 14), blank ? Hearth.QuillPale : Hearth.Quill, 0.84f);
		if (_box.Selected && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 550)
		{
			Hearth.Fill(b, new Rectangle(field.X + 18 + (int)Hearth.Width(typed, 0.84f), field.Y + 12, 2, 28), Hearth.Leaf);
		}
		hits.Add(field, () => _box.Selected = true, View);
		Rectangle jot = new Rectangle(field.Right + 12, y, 118, 52);
		Hearth.PressPlate(b, jot, Tr.Get("tools.todo.add", "记下"), primary: true, hover: Hearth.Hover(jot, View), scale: 0.86f);
		hits.Add(jot, Jot, View);
		y += 68;
		TodoPad pad = Pad;
		IReadOnlyList<TodoNote> notes = pad.InOrder();
		if (notes.Count == 0)
		{
			Kit.Empty(b, new Rectangle(x, y + 30, w, 80), Tr.Get("tools.todo.none", "今天没什么要记的。"));
			return y + 160;
		}
		foreach (TodoNote note in notes)
		{
			List<string> lines = Hearth.WrapLines(note.Words, w - 120, 0.84f);
			Rectangle row = new Rectangle(x, y, w, Math.Max(60, 28 + lines.Count * Hearth.LineHeight(0.84f)));
			Hearth.CardSlip(b, row, fill: note.Ticked ? Hearth.ParchmentDeep : null);
			Rectangle tick = new Rectangle(row.X + 16, row.Center.Y - 16, 32, 32);
			b.Draw(Game1.mouseCursors, tick, note.Ticked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked, Color.White);
			hits.Add(tick, () =>
			{
				Game1.playSound("drumkit6");
				pad.Tick(note);
			}, View);
			for (int i = 0; i < lines.Count; i++)
			{
				Vector2 at = new Vector2(row.X + 64, row.Y + 14 + i * Hearth.LineHeight(0.84f));
				Hearth.Ink(b, lines[i], at, note.Ticked ? Hearth.QuillPale : Hearth.Quill, 0.84f);
				if (note.Ticked)
				{
					Hearth.Fill(b, new Rectangle((int)at.X, (int)at.Y + 14, (int)Hearth.Width(lines[i], 0.84f), 2), Hearth.QuillPale);
				}
			}
			Rectangle tear = new Rectangle(row.Right - 44, row.Center.Y - 16, 32, 32);
			Hearth.CrossPlate(b, tear, Hearth.Hover(tear, View));
			hits.Add(tear, () =>
			{
				Game1.playSound("trashcan");
				pad.Tear(note);
			}, View);
			y = row.Bottom + 10;
		}
		if (notes.Any(n => n.Ticked))
		{
			Rectangle sweep = new Rectangle(x, y + 6, 190, 46);
			Hearth.PressPlate(b, sweep, Tr.Get("tools.todo.sweep", "清掉做完的"), hover: Hearth.Hover(sweep, View), scale: 0.8f);
			hits.Add(sweep, () =>
			{
				Game1.playSound("trashcan");
				pad.SweepTicked();
			}, View);
			y = sweep.Bottom + 8;
		}
		return y;
	}
}

internal sealed class FocusPage : ToolPage
{
	private static FocusClock Clock => FocusClock.Shared;

	public FocusPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("tools.focus", "番茄钟");

	protected override int Column(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		DateTime now = DateTime.UtcNow;
		y = Lead(b, Tr.Get("tools.focus.lead", "专心 25 分钟，歇 5 分钟。按现实时间走，关掉页面也算。"), x, y, w);
		int cardW = Math.Min(w, 520);
		Rectangle card = new Rectangle(x + (w - cardW) / 2, y, cardW, 400);
		Hearth.CardSlip(b, card);
		Vector2 c = new Vector2(card.Center.X, card.Y + 150);
		FrameDecor.RuneCircle(b, c, 120f, Hearth.Sapling * 0.3f, (float)(now.TimeOfDay.TotalSeconds * (Clock.Ticking ? 0.15 : 0)), heart: false);
		const int beads = 60;
		int lit = (int)(Clock.Share(now) * beads);
		for (int i = 0; i < beads; i++)
		{
			double a = -Math.PI / 2 + i * Math.PI * 2 / beads;
			Hearth.Fill(b, new Rectangle((int)(c.X + Math.Cos(a) * 136) - 3, (int)(c.Y + Math.Sin(a) * 136) - 3, 6, 6), i < lit ? (Clock.Resting ? Hearth.Leaf : Hearth.Berry) : Hearth.Seam);
		}
		TimeSpan left = Clock.Remaining(now);
		string face = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
		Hearth.InkBold(b, face, new Vector2(c.X - Hearth.Width(face, 1.6f) / 2f, c.Y - 30), Hearth.Quill, 1.6f);
		string state = Clock.Resting ? Tr.Get("tools.focus.rest", "歇一会儿") : Tr.Get("tools.focus.work", "专心");
		Hearth.Ink(b, state, new Vector2(c.X - Hearth.Width(state, 0.8f) / 2f, c.Y + 26), Hearth.QuillSoft, 0.8f);
		Rectangle go = new Rectangle(card.Center.X - 170, card.Bottom - 84, 160, 56), again = new Rectangle(card.Center.X + 10, card.Bottom - 84, 160, 56);
		Hearth.PressPlate(b, go, Clock.Ticking ? Tr.Get("tools.focus.pause", "暂停") : Tr.Get("tools.focus.start", "开始"), primary: true, hover: Hearth.Hover(go, View), scale: 0.9f);
		Hearth.PressPlate(b, again, Tr.Get("tools.focus.reset", "重来"), hover: Hearth.Hover(again, View), scale: 0.9f);
		hits.Add(go, () =>
		{
			Game1.playSound("smallSelect");
			Clock.StartOrPause(DateTime.UtcNow);
		}, View);
		hits.Add(again, () =>
		{
			Game1.playSound("shwip");
			Clock.Restart();
		}, View);
		string rounds = Hearth.Truncate(Tr.Get("tools.focus.rounds", "这次开游戏以来完成了 {{n}} 轮", ("n", Clock.RoundsDone)), w, 0.76f);
		Hearth.Ink(b, rounds, new Vector2(card.Center.X - Hearth.Width(rounds, 0.76f) / 2f, card.Bottom + 14), Hearth.QuillSoft, 0.76f);
		return card.Bottom + 50;
	}
}

internal sealed class BirthdaysPage : ToolPage
{
	private string _season = Game1.currentSeason ?? "spring";

	private List<BornOn> _marks = new List<BornOn>();

	public BirthdaysPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("tools.birthdays", "生日");

	public override void Reload()
	{
		if (_marks.Count == 0)
		{
			_marks = Utility.getAllVillagers().Where(n => n is not StardewValley.Characters.Child && n.CanSocialize && !string.IsNullOrEmpty(n.Birthday_Season) && n.Birthday_Day > 0)
				.Select(n => new BornOn(n.Name, n.Birthday_Season, n.Birthday_Day)).ToList();
		}
	}

	protected override int Column(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		Rectangle back = new Rectangle(x, y, 56, 48), forward = new Rectangle(x + w - 56, y, 56, 48);
		Hearth.PressPlate(b, back, "<", hover: Hearth.Hover(back, View));
		Hearth.PressPlate(b, forward, ">", hover: Hearth.Hover(forward, View));
		hits.Add(back, () => _season = BirthdayWheel.Turn(_season, -1), View);
		hits.Add(forward, () => _season = BirthdayWheel.Turn(_season, 1), View);
		string title = ShellText.Season(_season);
		Hearth.InkBold(b, title, new Vector2(x + w / 2f - Hearth.Width(title, 1.1f) / 2f, y + 8), Hearth.Quill, 1.1f);
		y += 64;
		const int cellH = 88, gap = 8;
		int cellW = Math.Min((w - 6 * gap) / 7, 118), left = x + (w - cellW * 7 - 6 * gap) / 2;
		bool thisSeason = string.Equals(_season, Game1.currentSeason, StringComparison.OrdinalIgnoreCase);
		ILookup<int, BornOn> byDay = _marks.Where(m => BirthdayWheel.IsIn(m, _season)).ToLookup(m => m.Day);
		for (int day = 1; day <= 28; day++)
		{
			Rectangle r = new Rectangle(left + (day - 1) % 7 * (cellW + gap), y + (day - 1) / 7 * (cellH + gap), cellW, cellH);
			Hearth.CardSlip(b, r, thisSeason && day == Game1.dayOfMonth);
			Hearth.Ink(b, day.ToString(), new Vector2(r.X + 10, r.Y + 8), Hearth.QuillSoft, 0.72f);
			int shift = 0;
			foreach (BornOn mark in byDay[day])
			{
				Kit.Avatar(b, new Rectangle(r.Right - 48 - shift * 26, r.Y + 34, 44, 44), Ctx, mark.Npc);
				shift++;
			}
		}
		y += 4 * (cellH + gap) + 12;
		Hearth.Ribbon(b, Tr.Get("tools.birthdays.next", "快到了"), x + 12, y);
		y += 46;
		foreach (ComingBirthday coming in BirthdayWheel.Soonest(_marks, Game1.currentSeason ?? "spring", Game1.dayOfMonth, 5))
		{
			Rectangle row = new Rectangle(x, y, w, 64);
			Hearth.CardSlip(b, row);
			Kit.Avatar(b, new Rectangle(row.X + 14, row.Y + 10, 44, 44), Ctx, coming.Who.Npc);
			Hearth.Ink(b, Ctx.Display(coming.Who.Npc), new Vector2(row.X + 72, row.Y + 18), Hearth.Quill, 0.84f);
			string when = coming.InDays == 0 ? Tr.Get("tools.birthdays.today", "就是今天") : Tr.Get("tools.birthdays.in", "还有 {{n}} 天", ("n", coming.InDays));
			Hearth.Ink(b, when, new Vector2(row.Right - 20 - Hearth.Width(when, 0.78f), row.Y + 20), coming.InDays == 0 ? Hearth.Berry : Hearth.QuillSoft, 0.78f);
			y = row.Bottom + 10;
		}
		return y;
	}
}

/// <summary>图鉴：鱼、菜谱、博物馆共用一张格子，没得到的显示剪影。</summary>
internal sealed class CollectionPage : ToolPage
{
	public sealed record Entry(ParsedItemData Art, bool Known, bool Got, string Note);

	private readonly string _title;

	private readonly string _lead;

	private readonly Func<List<Entry>> _read;

	private readonly bool _tank;

	private List<Entry> _entries = new List<Entry>();

	private Entry? _hover;

	private DateTime _readAt = DateTime.MinValue;

	public CollectionPage(ShellContext ctx, string title, string lead, Func<List<Entry>> read, bool tank = false) : base(ctx)
	{
		_title = title;
		_lead = lead;
		_read = read;
		_tank = tank;
	}

	public override string Title => _title;

	public override void Reload()
	{
		if ((DateTime.UtcNow - _readAt).TotalSeconds >= 30)
		{
			_readAt = DateTime.UtcNow;
			_entries = _read();
		}
	}

	public static List<Entry> Fish() => Game1.objectData.Where(o => o.Value.Type == "Fish" && !o.Value.ExcludeFromFishingCollection)
		.Select(o => FishEntry("(O)" + o.Key)).ToList();

	private static Entry FishEntry(string id)
	{
		bool caught = Game1.player.fishCaught.TryGetValue(id, out int[] counts);
		return new Entry(ItemRegistry.GetDataOrErrorItem(id), caught, caught, caught ? Tr.Get("tools.fish.count", "已经上钩 {{n}} 回", ("n", counts[0])) : Tr.Get("tools.notYet", "还没有"));
	}

	public static List<Entry> Recipes() => CraftingRecipe.cookingRecipes.Keys.Select(RecipeEntry).OfType<Entry>().ToList();

	private static Entry? RecipeEntry(string key)
	{
		CraftingRecipe recipe = new CraftingRecipe(key, isCookingRecipe: true);
		if (recipe.createItem() is not Item dish)
		{
			return null;
		}
		var cooked = Game1.player.recipesCooked;
		bool done = cooked.ContainsKey(dish.ItemId) || cooked.ContainsKey(dish.QualifiedItemId) || cooked.ContainsKey(recipe.getIndexOfMenuView());
		bool known = Game1.player.cookingRecipes.ContainsKey(key);
		string note = done ? Tr.Get("tools.recipe.cooked", "做过了") : known ? Tr.Get("tools.recipe.known", "学会了，还没做过") : Tr.Get("tools.recipe.unknown", "还没学会");
		return new Entry(ItemRegistry.GetDataOrErrorItem(dish.QualifiedItemId), known || done, done, note);
	}

	public static List<Entry> Museum()
	{
		HashSet<string> given = new HashSet<string>((Game1.getLocationFromName("ArchaeologyHouse") as LibraryMuseum)?.museumPieces.Values ?? Enumerable.Empty<string>());
		return Game1.objectData.Where(o => LibraryMuseum.IsItemSuitableForDonation("(O)" + o.Key, checkDonatedItems: false))
			.Select(o => new Entry(ItemRegistry.GetDataOrErrorItem("(O)" + o.Key), given.Contains(o.Key), given.Contains(o.Key), given.Contains(o.Key) ? Tr.Get("tools.museum.given", "已经捐给博物馆") : Tr.Get("tools.notYet", "还没有"))).ToList();
	}

	protected override int Column(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		y = Lead(b, _lead + " " + Tr.Get("tools.collect.count", "（{{a}} / {{b}}）", ("a", _entries.Count(e => e.Got)), ("b", _entries.Count)), x, y, w);
		if (_tank)
		{
			y = DrawTank(b, x, y, w);
		}
		const int gap = 8;
		_hover = null;
		int size = Math.Clamp(w / 10, 64, 88);
		int cols = Math.Max(1, w / (size + gap));
		for (int i = 0; i < _entries.Count; i++)
		{
			Entry e = _entries[i];
			Rectangle r = new Rectangle(x + i % cols * (size + gap), y + i / cols * (size + gap), size, size);
			if (r.Bottom < View.Y || r.Y > View.Bottom)
			{
				continue;
			}
			Hearth.CardSlip(b, r, fill: e.Got ? null : Hearth.ParchmentDeep);
			if (Hearth.Hover(r, View))
			{
				_hover = e;
			}
			b.Draw(e.Art.GetTexture(), Hearth.Inflate(r, -8), e.Art.GetSourceRect(), e.Got ? Color.White : Color.Black * 0.32f);
		}
		return y + (_entries.Count + cols - 1) / cols * (size + gap) + 8;
	}

	protected override void Topmost(SpriteBatch b)
	{
		if (_hover != null)
		{
			IClickableMenu.drawHoverText(b, _hover.Note, Game1.smallFont, boldTitleText: _hover.Known ? _hover.Art.DisplayName : "???");
		}
	}

	/// <summary>鱼缸：钓到过的鱼在水里慢慢游。</summary>
	private int DrawTank(SpriteBatch b, int x, int y, int w)
	{
		Rectangle tank = new Rectangle(x, y, w, 190);
		Hearth.Bordered(b, tank, new Color(74, 150, 196), Hearth.Bark, 8, 4);
		Rectangle water = Hearth.Inflate(tank, -4);
		const int bands = 6;
		for (int i = 0; i < bands; i++)
		{
			Hearth.Fill(b, new Rectangle(water.X, water.Y + i * water.Height / bands, water.Width, water.Height / bands + 1), Color.Lerp(new Color(120, 196, 226), new Color(40, 104, 164), i / (float)bands));
		}
		Hearth.Fill(b, new Rectangle(water.X, water.Bottom - 20, water.Width, 20), new Color(208, 176, 112));
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		List<Entry> swimmers = _entries.Where(e => e.Got).Take(14).ToList();
		for (int i = 0; i < swimmers.Count; i++)
		{
			double pace = 0.25 + i % 5 * 0.07, wave = t * pace + i * 1.7, span = Math.Max(1, water.Width - 56);
			bool turning = Math.Cos(wave) < 0;
			Vector2 at = new Vector2(water.X + (int)((Math.Sin(wave) + 1) / 2 * span), water.Y + 12 + i * 37 % (water.Height - 76) + (int)(Math.Sin(t + i) * 5));
			b.Draw(swimmers[i].Art.GetTexture(), at, swimmers[i].Art.GetSourceRect(), Color.White, 0f, Vector2.Zero, 3f, turning ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 1f);
		}
		if (swimmers.Count == 0)
		{
			Hearth.InkMiddle(b, Tr.Get("tools.fish.empty", "鱼缸还空着，去钓一条吧。"), water, Color.White, 0.84f);
		}
		return tank.Bottom + 14;
	}
}
