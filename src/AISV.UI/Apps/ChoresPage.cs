using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>家务安排：配偶今天要做的事和进度、收获箱、每样事开关、让 TA 帮忙做什么。</summary>
internal sealed class ChoresPage : ShellPage
{
	private static readonly string[] Toggles =
	{
		SpouseTasks.Water, SpouseTasks.Harvest, SpouseTasks.Animals, SpouseTasks.Clear, SpouseTasks.Greenhouse, SpouseTasks.Ponds,
		SpouseTasks.Machines, SpouseTasks.Visit, SpouseTasks.MindShop, SpouseTasks.Kids
	};

	private static readonly string[] Askable =
	{
		SpouseTasks.Water, SpouseTasks.Harvest, SpouseTasks.Animals, SpouseTasks.Hay, SpouseTasks.Clear, SpouseTasks.Greenhouse, SpouseTasks.Kids
	};

	private readonly Scroller _scroll = new Scroller();

	private Rectangle _view;

	public ChoresPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("apps.chores", "家务安排");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	private SpousePlanner? Planner => Ctx.Spouse;

	public static string Summary(ShellContext ctx)
	{
		if (ctx.Spouse is not SpousePlanner p || p.Spouse() == null)
		{
			return Tr.Get("chores.summaryNone", "结婚以后才有");
		}
		SpouseDayLog? log = p.Log;
		int done = log?.Items.Count(i => i.State == SpousePlanStates.Done) ?? 0;
		int all = log?.Items.Count(i => i.State != SpousePlanStates.Skipped) ?? 0;
		return all == 0 ? Tr.Get("chores.summaryEmpty", "今天没有安排") : Tr.Get("chores.summary", "今天 {{done}}/{{all}} 件做完", ("done", done), ("all", all));
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		Hearth.BeginClip(b, area);
		int y = area.Y + 18 - _scroll.Offset;
		if (Planner is not SpousePlanner p || p.Spouse() is not NPC spouse)
		{
			y = Text(b, Tr.Get("chores.notMarried", "结婚（或有室友）以后，配偶每天会自己安排家务：浇水、收菜、照顾动物、清理农场、打理温室和鱼塘，也会去镇上找人聊天、买东西、去小超市看店、带孩子玩。"), x, y, w, Hearth.QuillSoft);
		}
		else if (!Ctx.Config().SpousePlanner)
		{
			y = Text(b, Tr.Get("chores.off", "配偶日程已在设置里关掉（设置 → 家里 → 配偶日程）。"), x, y, w, Hearth.QuillSoft);
		}
		else
		{
			y = Today(b, p, spouse, x, y, w) + 18;
			y = BoxPart(b, hits, p, x, y, w) + 18;
			y = AskPart(b, hits, p, spouse, x, y, w) + 18;
			y = TogglePart(b, hits, p, x, y, w) + 18;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + _scroll.Offset, 12);
	}

	private static int Text(SpriteBatch b, string text, int x, int y, int w, Color color, float scale = 0.8f)
	{
		foreach (string line in Hearth.WrapLines(text, w, scale))
		{
			Hearth.Ink(b, line, new Vector2(x, y), color, scale);
			y += Hearth.LineHeight(scale);
		}
		return y;
	}

	private int Today(SpriteBatch b, SpousePlanner p, NPC spouse, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("chores.today", "{{npc}}今天的安排", ("npc", Ctx.Display(spouse.Name))), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 38;
		SpouseDayLog? log = p.Log;
		if (log == null || log.Items.Count == 0)
		{
			string why = Game1.timeOfDay < 610 ? Tr.Get("chores.notYet", "早上 6:10 排好。") : Tr.Get("chores.nothing", "今天没什么要做的（下雨、节日、心情不好，或者活都干完了）。");
			return Text(b, why, x, y, w, Hearth.QuillSoft);
		}
		if (log.Say.Length > 0)
		{
			y = Text(b, "「" + log.Say + "」", x, y, w, Hearth.LeafDeep, 0.78f) + 6;
		}
		foreach (SpousePlanItem i in log.Items)
		{
			string state = i.State switch
			{
				SpousePlanStates.Done => Tr.Get("chores.done", "做完了"),
				SpousePlanStates.Working => Tr.Get("chores.working", "正在做"),
				SpousePlanStates.Skipped => Tr.Get("chores.skipped", "不做了"),
				_ => Tr.Get("chores.pending", "还没开始")
			};
			string detail = SpouseTasks.IsChore(i.Task) && i.Did > 0 ? " ×" + i.Did : i.Note.Length > 0 && i.State == SpousePlanStates.Done ? " · " + i.Note : "";
			string mark = i.Requested ? Tr.Get("chores.asked", "（你要的）") : "";
			Color c = i.State == SpousePlanStates.Done ? Hearth.LeafDeep : i.State == SpousePlanStates.Skipped ? Hearth.QuillPale : Hearth.Quill;
			Hearth.Ink(b, $"{Clock.Text(i.Time)}  {SpousePlanner.TaskName(i.Task)}{mark} — {state}{detail}", new Vector2(x, y), c, 0.8f);
			y += Hearth.LineHeight(0.8f) + 4;
		}
		if (log.Yesterday.Length > 0)
		{
			y = Text(b, Tr.Get("chores.yesterday", "昨天：{{what}}", ("what", log.Yesterday)), x, y + 6, w, Hearth.QuillSoft, 0.72f);
		}
		return y;
	}

	private int BoxPart(SpriteBatch b, Hits hits, SpousePlanner p, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("chores.box", "收获箱"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 36;
		if (Ctx.Config().SpouseHarvestTo == "Bin")
		{
			return Text(b, Tr.Get("chores.toBin", "设置成了直接放进出货箱，晚上一起卖掉。"), x, y, w, Hearth.QuillSoft, 0.78f);
		}
		List<Item> items = p.Box().Where(i => i != null).ToList();
		if (items.Count == 0)
		{
			return Text(b, Tr.Get("chores.boxEmpty", "空的。配偶收的菜、蛋、鱼塘和机器里的东西会放在这里。"), x, y, w, Hearth.QuillSoft, 0.78f);
		}
		string list = string.Join(Tr.Get("list.sep", "、"), items.Take(12).Select(i => $"{i.DisplayName} ×{i.Stack}")) + (items.Count > 12 ? "……" : "");
		y = Text(b, list, x, y, w - 200, Hearth.Quill, 0.78f);
		Rectangle claim = new Rectangle(x + w - 180, y - 40, 180, 44);
		Hearth.PressPlate(b, claim, Tr.Get("chores.claim", "全部领取"), hover: Hearth.Hover(claim, _view), scale: 0.8f);
		hits.Add(claim, () =>
		{
			int n = p.Claim();
			Host.Notice(n > 0 ? Tr.Get("chores.claimed", "放进背包 {{n}} 件。", ("n", n)) : Tr.Get("chores.bagFull", "背包满了。"));
		}, _view);
		return y + 10;
	}

	private int AskPart(SpriteBatch b, Hits hits, SpousePlanner p, NPC spouse, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("chores.ask", "让 TA 帮忙"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 34;
		y = Text(b, Tr.Get("chores.askNote", "今天还来得及就马上去做，不然明天一早做。聊天时说「帮我浇水」也一样。"), x, y, w, Hearth.QuillSoft, 0.72f) + 4;
		int bx = x;
		foreach (string t in Askable)
		{
			string label = SpousePlanner.TaskName(t);
			int bw = (int)Hearth.Width(label, 0.75f) + 36;
			if (bx + bw > x + w)
			{
				bx = x;
				y += 50;
			}
			Rectangle btn = new Rectangle(bx, y, bw, 42);
			Hearth.PressPlate(b, btn, label, hover: Hearth.Hover(btn, _view), scale: 0.75f);
			string task = t;
			hits.Add(btn, () =>
			{
				if (p.RequestTask(task, out bool today))
				{
					Host.Notice(today
						? Tr.Get("spouse.plan.okToday", "{{npc}}答应今天就帮你{{task}}。", ("npc", Ctx.Display(spouse.Name)), ("task", SpousePlanner.TaskName(task)))
						: Tr.Get("spouse.plan.okTomorrow", "{{npc}}答应明天一早帮你{{task}}。", ("npc", Ctx.Display(spouse.Name)), ("task", SpousePlanner.TaskName(task))));
				}
			}, _view);
			bx += bw + 10;
		}
		return y + 50;
	}

	private int TogglePart(SpriteBatch b, Hits hits, SpousePlanner p, int x, int y, int w)
	{
		Hearth.InkBold(b, Tr.Get("chores.toggles", "TA 会主动做的事"), new Vector2(x, y), Hearth.Quill, 0.9f);
		y += 36;
		foreach (string t in Toggles)
		{
			bool on = p.IsOn(t);
			Rectangle row = new Rectangle(x, y, w, 40);
			Hearth.Ink(b, SpousePlanner.TaskName(t), new Vector2(x, y + 8), Hearth.Quill, 0.8f);
			Rectangle sw = new Rectangle(x + w - 120, y, 120, 40);
			Hearth.PressPlate(b, sw, on ? Tr.Get("chores.on", "会做") : Tr.Get("chores.offBtn", "不做"), hover: Hearth.Hover(sw, _view), scale: 0.75f, disabled: !on);
			string task = t;
			hits.Add(sw, () =>
			{
				p.Toggle(task, !on);
				Ctx.SaveConfig(Ctx.Config());
			}, _view);
			y += 46;
		}
		return Text(b, Tr.Get("chores.moreSettings", "勤快程度、收获放哪、每天花多少钱：设置 → 家里 → 配偶日程。"), x, y + 4, w, Hearth.QuillSoft, 0.72f);
	}
}
