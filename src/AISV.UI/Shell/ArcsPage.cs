using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Game.Stories;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>
/// 社交 → 小剧场：左边"进行中 / 已结束"，右边详情：涉及的人、经过、现在到哪一步、目标；
/// 目标都完成时选结局、结束小剧场；结束了的看结局、重温场景；可以删除（要确认）。
/// </summary>
internal sealed class ArcsPage : ShellPage
{
	private static readonly Color Purple = new Color(140, 100, 190);

	private static readonly Color PurpleSoft = new Color(236, 226, 248);

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _detailScroll = new Scroller();

	private List<StoryArc> _active = new List<StoryArc>();

	private List<StoryArc> _closed = new List<StoryArc>();

	private string? _selected;

	private StoryArc? _confirmDelete;

	private Rectangle _listArea;

	private Rectangle _detail;

	public ArcsPage(ShellContext ctx) : base(ctx)
	{
	}

	private StoryArcService? Arcs => Ctx.Arcs;

	private StoryArc? Selected => _active.Concat(_closed).FirstOrDefault(a => a.Id == _selected);

	public override void Reload()
	{
		IReadOnlyList<StoryArc> all = Arcs?.Arcs.All ?? new List<StoryArc>();
		_active = all.Where(a => a.State == StoryArc.Completable).Concat(all.Where(a => a.State == StoryArc.Active)).ToList();
		_closed = all.Where(a => a.State is StoryArc.Resolved or StoryArc.Fizzled or "expired").Reverse().Take(12).ToList();
		foreach (StoryArc a in _active.Where(a => a.State == StoryArc.Completable))
		{
			StoryArcBook.EnsureChoices(a);
		}
		if (Selected == null)
		{
			_selected = (_active.FirstOrDefault() ?? _closed.FirstOrDefault())?.Id;
		}
	}

	public override void Scroll(int direction)
	{
		if (_confirmDelete != null)
		{
			return;
		}
		if (_listArea.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			_listScroll.Wheel(direction, 72);
		}
		else
		{
			_detailScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		if (_confirmDelete != null)
		{
			if (key == Keys.Escape)
			{
				_confirmDelete = null;
			}
			return true;
		}
		return _detailScroll.PageKeys(key, _detail.Height - 80);
	}

	private readonly Flip _flip = new Flip();

	public override bool StepBack() => _flip.StepBack();

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (Flip.Applies(area))
		{
			_flip.Draw(b, area, hits, r =>
			{
				_detail = Rectangle.Empty;
				_listArea = new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12);
				DrawList(b, r, hits);
			}, r =>
			{
				_listArea = Rectangle.Empty;
				_detail = r;
				DrawDetail(b, hits);
			});
			if (_confirmDelete != null)
			{
				DrawConfirm(b, area, hits);
			}
			return;
		}
		int listW = Math.Clamp(area.Width * 32 / 100, 300, 400);
		Rectangle panel = new Rectangle(area.X, area.Y, listW, area.Height);
		_detail = new Rectangle(panel.Right, area.Y, area.Right - panel.Right, area.Height);
		_listArea = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
		DrawList(b, panel, hits);
		DrawDetail(b, hits);
		if (_confirmDelete != null)
		{
			DrawConfirm(b, area, hits);
		}
	}

	// ---- 左边 ----

	private void DrawList(SpriteBatch b, Rectangle panel, Hits hits)
	{
		Hearth.Fill(b, panel, Hearth.Slip);
		Hearth.VLine(b, panel.Right - 2, panel.Y, panel.Height, Hearth.Seam);
		if (_active.Count == 0 && _closed.Count == 0)
		{
			List<string> lines = Hearth.WrapLines(Tr.Get("arcs.page.none", "还没有小剧场。多和村民来往，镇上的事会慢慢展开。"), panel.Width - 48, 0.8f);
			int ly = panel.Y + 40;
			foreach (string line in lines)
			{
				Hearth.InkMiddle(b, line, new Rectangle(panel.X, ly, panel.Width, 28), Hearth.QuillSoft, 0.8f);
				ly += Hearth.LineHeight(0.8f);
			}
			return;
		}
		Hearth.BeginClip(b, _listArea);
		int y = _listArea.Y + 6 - _listScroll.Offset;
		y = DrawGroup(b, Tr.Get("arcs.page.groupActive", "进行中"), _active, y, hits);
		if (_closed.Count > 0)
		{
			y = DrawGroup(b, Tr.Get("arcs.page.groupClosed", "已结束"), _closed, y + 10, hits);
		}
		Hearth.EndClip(b);
		_listScroll.Finish(b, _listArea, y, 6);
	}

	private int DrawGroup(SpriteBatch b, string title, List<StoryArc> arcs, int y, Hits hits)
	{
		Hearth.Ink(b, title + "  " + arcs.Count, new Vector2(_listArea.X + 14, y), Hearth.QuillPale, 0.72f);
		y += 32;
		const int rowH = 86;
		foreach (StoryArc arc in arcs)
		{
			Rectangle row = new Rectangle(_listArea.X + 2, y, _listArea.Width - 12, rowH);
			bool sel = arc.Id == _selected;
			if (sel || Hearth.Hover(row, _listArea))
			{
				Hearth.FillRounded(b, row, sel ? Hearth.Honey : Hearth.ParchmentDeep, 10);
			}
			if (sel)
			{
				Hearth.FillRounded(b, new Rectangle(row.X, row.Y + 14, 4, row.Height - 28), Hearth.Leaf, 2);
			}
			Kit.Avatar(b, new Rectangle(row.X + 14, row.Y + 16, 52, 52), Ctx, arc.Cast.FirstOrDefault());
			int tx = row.X + 80, tw = row.Right - 12 - tx;
			string created = Created(arc);
			float cw = created.Length > 0 ? Hearth.Width(created, 0.62f) + 8 : 0;
			Hearth.InkBold(b, Hearth.Truncate(ShellText.Clean(arc.Title), tw - cw, 0.88f), new Vector2(tx, row.Y + 12), Hearth.Quill, 0.88f);
			if (created.Length > 0)
			{
				Hearth.Ink(b, created, new Vector2(row.Right - 12 - cw + 8, row.Y + 16), Hearth.QuillPale, 0.62f);
			}
			if (arc.State == StoryArc.Active && arc.Goals.Count > 0)
			{
				Hearth.Gauge(b, new Rectangle(tx, row.Y + 52, Math.Max(40, tw - 56), 10), arc.DoneCount / (float)arc.Goals.Count);
				Hearth.Ink(b, $"{arc.DoneCount}/{arc.Goals.Count}", new Vector2(row.Right - 12 - 44, row.Y + 44), Hearth.QuillSoft, 0.72f);
			}
			else
			{
				(string text, Color fill, Color ink) = Chip(arc);
				Hearth.Tag(b, new Vector2(tx, row.Y + 46), text, fill, ink, 0.66f);
			}
			string id = arc.Id;
			hits.Add(row, () =>
			{
				if (_selected != id)
				{
					Game1.playSound("smallSelect");
					_selected = id;
					_detailScroll.Reset();
				}
			}, _listArea);
			y += rowH + 4;
		}
		return y;
	}

	// ---- 右边 ----

	private void DrawDetail(SpriteBatch b, Hits hits)
	{
		StoryArc? arc = Selected;
		if (arc == null)
		{
			if (_active.Count + _closed.Count > 0)
			{
				Hearth.InkMiddle(b, Tr.Get("arcs.page.pick", "选一出小剧场看看。"), _detail, Hearth.QuillSoft);
			}
			return;
		}
		Hearth.BeginClip(b, _detail);
		int w = Math.Min(_detail.Width - 56, 820);
		int x = _detail.X + (_detail.Width - w) / 2;
		int y = _detail.Y + 20 - _detailScroll.Offset;
		y = DrawHead(b, arc, x, y, w);
		if (arc.State == StoryArc.Completable)
		{
			y = DrawChoices(b, arc, x, y + 16, w, hits);
		}
		if (arc.State == StoryArc.Resolved)
		{
			y = DrawOutcome(b, arc, x, y + 16, w, hits);
		}
		y = DrawStory(b, arc, x, y + 16, w);
		if (arc.Goals.Count > 0)
		{
			y = DrawObjectives(b, arc, x, y + 16, w);
		}
		if (Arcs?.IsConcluding(arc.Id) != true)
		{
			Rectangle del = new Rectangle(x, y + 20, 180, 44);
			Hearth.PressPlate(b, del, Tr.Get("arcs.page.delete", "删除小剧场"), hover: Hearth.Hover(del, _detail), scale: 0.85f);
			hits.Add(del, () =>
			{
				Game1.playSound("smallSelect");
				_confirmDelete = arc;
			}, _detail);
			y = del.Bottom;
		}
		y += 24;
		Hearth.EndClip(b);
		_detailScroll.Finish(b, _detail, y);
	}

	private int DrawHead(SpriteBatch b, StoryArc arc, int x, int y, int w)
	{
		List<string> title = Hearth.WrapLines(ShellText.Clean(arc.Title), w - 48, 1.15f);
		int people = Math.Min(6, arc.Cast.Count);
		int total = arc.Goals.Count;
		int h = 24 + (people > 0 ? 64 : 0) + title.Count * Hearth.LineHeight(1.15f) + 12 + 34 + 16 + (total > 0 ? 30 : 0) + 12;
		Rectangle card = new Rectangle(x, y, w, h);
		Hearth.CardSlip(b, card);
		int cy = y + 24;
		if (people > 0)
		{
			int px = x + 24;
			for (int i = 0; i < people; i++)
			{
				Kit.Avatar(b, new Rectangle(px, cy, 48, 48), Ctx, arc.Cast[i]);
				px += 56;
			}
			string names = string.Join("、", arc.Cast.Take(6).Select(Ctx.Display));
			Hearth.Ink(b, Hearth.Truncate(names, card.Right - 24 - px - 8, 0.75f), new Vector2(px + 8, cy + 12), Hearth.QuillSoft, 0.75f);
			cy += 64;
		}
		foreach (string line in title)
		{
			Hearth.InkBold(b, line, new Vector2(x + 24, cy), Hearth.Quill, 1.15f);
			cy += Hearth.LineHeight(1.15f);
		}
		cy += 12;
		(string text, Color fill, Color ink) = Chip(arc);
		Rectangle chip = Hearth.Tag(b, new Vector2(x + 24, cy), text, fill, ink, 0.72f);
		string created = Created(arc);
		if (created.Length > 0)
		{
			Hearth.Ink(b, Tr.Get("arcs.page.since", "开始于 {{when}}", ("when", created)), new Vector2(chip.Right + 12, cy + 4), Hearth.QuillPale, 0.72f);
		}
		cy += 34 + 16;
		if (total > 0)
		{
			Hearth.Gauge(b, new Rectangle(x + 24, cy + 6, w - 48 - 140, 12), arc.DoneCount / (float)total, arc.State == StoryArc.Completable ? Hearth.Rust : null);
			Hearth.Ink(b, Tr.Get("arcs.page.progress", "进度 {{done}}/{{total}}", ("done", arc.DoneCount), ("total", total)), new Vector2(card.Right - 24 - 120, cy), Hearth.QuillSoft, 0.75f);
		}
		return card.Bottom;
	}

	private int DrawChoices(SpriteBatch b, StoryArc arc, int x, int y, int w, Hits hits)
	{
		List<ArcChoiceOption> choices = arc.Endings ?? new List<ArcChoiceOption>();
		int inner = w - 48;
		string hintText = Tr.Get("arcs.page.readyHint", "目标都完成了。选一个收尾的方式，然后结束这出小剧场。");
		List<string> hint = Hearth.WrapLines(hintText, inner, 0.8f);
		List<(ArcChoiceOption Choice, List<string> Desc)> rows = choices.Select(c => (c, Hearth.WrapLines(ChoiceDescription(c), inner - 56, 0.75f))).ToList();
		int h = 20 + 34 + hint.Count * Hearth.LineHeight(0.8f) + 12 + rows.Sum(r => 42 + r.Desc.Count * Hearth.LineHeight(0.75f) + 10 + 10) + 6 + 50 + 20;
		Rectangle card = new Rectangle(x, y, w, h);
		Hearth.Bordered(b, card, Hearth.AmberPale, Hearth.Rust * 0.6f);
		int cy = y + 20;
		Kit.SectionTitle(b, Tr.Get("arcs.page.chooseEnding", "怎么收尾"), x + 24, cy);
		cy += 34;
		foreach (string line in hint)
		{
			Hearth.Ink(b, line, new Vector2(x + 24, cy), Hearth.QuillSoft, 0.8f);
			cy += Hearth.LineHeight(0.8f);
		}
		cy += 12;
		bool busy = Arcs?.IsConcluding(arc.Id) == true;
		foreach ((ArcChoiceOption choice, List<string> desc) in rows)
		{
			Rectangle r = new Rectangle(x + 24, cy, inner, 42 + desc.Count * Hearth.LineHeight(0.75f) + 10);
			bool sel = string.Equals(arc.ChosenEnding, choice.Id, StringComparison.OrdinalIgnoreCase);
			Hearth.Bordered(b, r, sel || Hearth.Hover(r, _detail) ? Hearth.Slip : Hearth.Parchment, sel ? Hearth.Rust : Hearth.SlipEdge, 10);
			Rectangle dot = new Rectangle(r.X + 16, r.Y + 14, 22, 22);
			Hearth.Bordered(b, dot, Hearth.Slip, sel ? Hearth.Rust : Hearth.SlipEdge, 11);
			if (sel)
			{
				Hearth.FillRounded(b, Hearth.Inflate(dot, -6), Hearth.Rust, 5);
			}
			Hearth.InkBold(b, ChoiceLabel(choice), new Vector2(r.X + 52, r.Y + 10), Hearth.Quill, 0.88f);
			int dy = r.Y + 42;
			foreach (string line in desc)
			{
				Hearth.Ink(b, line, new Vector2(r.X + 52, dy), Hearth.QuillSoft, 0.75f);
				dy += Hearth.LineHeight(0.75f);
			}
			if (!busy)
			{
				string arcId = arc.Id, choiceId = choice.Id;
				hits.Add(r, () =>
				{
					Game1.playSound("smallSelect");
					Arcs?.SelectChoice(arcId, choiceId);
				}, _detail);
			}
			cy = r.Bottom + 10;
		}
		cy += 6;
		Rectangle go = new Rectangle(x + 24, cy, 220, 50);
		Hearth.PressPlate(b, go, busy ? Tr.Get("arcs.page.writing", "正在写结局…") : Tr.Get("arcs.page.conclude", "结束小剧场"), primary: true, hover: !busy && Hearth.Hover(go, _detail), disabled: busy);
		if (!busy)
		{
			string arcId = arc.Id;
			hits.Add(go, () => Conclude(arcId), _detail);
		}
		return card.Bottom;
	}

	private void Conclude(string arcId)
	{
		StoryArcService? arcs = Arcs;
		if (arcs == null)
		{
			return;
		}
		Game1.playSound("bigSelect");
		string? why = ArcScenes.Conclude(arcs, arcId, Ctx.Config().FinaleScenesOn);
		if (why != null)
		{
			Host.Notice(why);
		}
	}

	private int DrawOutcome(SpriteBatch b, StoryArc arc, int x, int y, int w, Hits hits)
	{
		string summary = ShellText.Clean(arc.Closure ?? arc.Outcome ?? "");
		ArcChoiceOption? chosen = arc.Endings?.FirstOrDefault(c => string.Equals(c.Id, arc.ChosenEnding, StringComparison.OrdinalIgnoreCase));
		int inner = w - 48;
		List<string> lines = summary.Length == 0 ? new List<string>() : Hearth.WrapLines(summary, inner, 0.85f);
		bool replay = ConclusionScript.Playable(arc, Game1.player?.Name ?? "").Count > 0;
		int h = 20 + 34 + (chosen != null ? 40 : 0) + lines.Count * Hearth.LineHeight(0.85f) + (replay ? 70 : 0) + 20;
		Rectangle card = new Rectangle(x, y, w, h);
		Hearth.Bordered(b, card, PurpleSoft, Purple * 0.5f);
		int cy = y + 20;
		Kit.SectionTitle(b, Tr.Get("arcs.page.outcome", "结局"), x + 24, cy);
		cy += 34;
		if (chosen != null)
		{
			Hearth.Tag(b, new Vector2(x + 24, cy), Tr.Get("arcs.page.youChose", "你选了：") + ChoiceLabel(chosen), Hearth.Slip, Purple, 0.72f);
			cy += 40;
		}
		foreach (string line in lines)
		{
			Hearth.Ink(b, line, new Vector2(x + 24, cy), Hearth.Quill, 0.85f);
			cy += Hearth.LineHeight(0.85f);
		}
		if (replay)
		{
			Rectangle r = new Rectangle(x + 24, cy + 14, 200, 46);
			Hearth.PressPlate(b, r, Tr.Get("arcs.page.replay", "重温结局场景"), hover: Hearth.Hover(r, _detail), scale: 0.85f);
			hits.Add(r, () =>
			{
				Game1.playSound("bigSelect");
				Game1.exitActiveMenu();
				ArcScenes.Play(arc);
			}, _detail);
		}
		return card.Bottom;
	}

	private int DrawStory(SpriteBatch b, StoryArc arc, int x, int y, int w)
	{
		string desc = ShellText.Clean(arc.Premise);
		string stage = ShellText.Clean(arc.Stage);
		int inner = w - 48;
		List<string> descLines = desc.Length == 0 ? new List<string>() : Hearth.WrapLines(desc, inner, 0.85f);
		List<string> stageLines = stage.Length == 0 ? new List<string>() : Hearth.WrapLines(stage, inner - 24, 0.8f);
		int stageH = stageLines.Count == 0 ? 0 : 18 + 30 + stageLines.Count * Hearth.LineHeight(0.8f) + 14;
		if (descLines.Count == 0 && stageH == 0)
		{
			return y - 16;
		}
		Rectangle card = new Rectangle(x, y, w, 20 + 34 + descLines.Count * Hearth.LineHeight(0.85f) + stageH + 20);
		Hearth.CardSlip(b, card);
		int cy = y + 20;
		Kit.SectionTitle(b, Tr.Get("arcs.page.story", "经过"), x + 24, cy);
		cy += 34;
		foreach (string line in descLines)
		{
			Hearth.Ink(b, line, new Vector2(x + 24, cy), Hearth.Quill, 0.85f);
			cy += Hearth.LineHeight(0.85f);
		}
		if (stageH > 0)
		{
			cy += 18;
			Rectangle box = new Rectangle(x + 24, cy, inner, 30 + stageLines.Count * Hearth.LineHeight(0.8f) + 10);
			Hearth.FillRounded(b, box, Hearth.ParchmentDeep, 8);
			Hearth.Fill(b, new Rectangle(box.X, box.Y + 8, 4, box.Height - 16), Hearth.Leaf);
			Hearth.InkBold(b, Tr.Get("arcs.page.now", "现在"), new Vector2(box.X + 16, box.Y + 6), Hearth.LeafDeep, 0.75f);
			int ly = box.Y + 32;
			foreach (string line in stageLines)
			{
				Hearth.Ink(b, line, new Vector2(box.X + 16, ly), Hearth.Quill, 0.8f);
				ly += Hearth.LineHeight(0.8f);
			}
		}
		return card.Bottom;
	}

	private int DrawObjectives(SpriteBatch b, StoryArc arc, int x, int y, int w)
	{
		int inner = w - 48 - 40;
		List<(ArcObjective Objective, List<string> Lines)> rows = arc.Goals.Select(o => (o, Hearth.WrapLines(ObjectiveText(o), inner, 0.82f))).ToList();
		int h = 20 + 34 + rows.Sum(r => Math.Max(32, r.Lines.Count * Hearth.LineHeight(0.82f)) + 12) + 12;
		Rectangle card = new Rectangle(x, y, w, h);
		Hearth.CardSlip(b, card);
		int cy = y + 20;
		Kit.SectionTitle(b, Tr.Get("arcs.page.objectives", "目标"), x + 24, cy);
		cy += 34;
		foreach ((ArcObjective o, List<string> lines) in rows)
		{
			Rectangle box = new Rectangle(x + 24, cy + 2, 26, 26);
			Hearth.Bordered(b, box, o.Done ? Hearth.Leaf : Hearth.Slip, o.Done ? Hearth.Leaf : Hearth.SlipEdge, 6);
			if (o.Done)
			{
				Hearth.Line(b, new Vector2(box.X + 6, box.Y + 13), new Vector2(box.X + 11, box.Y + 19), Color.White, 3);
				Hearth.Line(b, new Vector2(box.X + 11, box.Y + 19), new Vector2(box.X + 20, box.Y + 7), Color.White, 3);
			}
			int ly = cy;
			foreach (string line in lines)
			{
				Hearth.Ink(b, line, new Vector2(x + 24 + 40, ly), o.Done ? Hearth.QuillPale : Hearth.Quill, 0.82f);
				ly += Hearth.LineHeight(0.82f);
			}
			cy += Math.Max(32, lines.Count * Hearth.LineHeight(0.82f)) + 12;
		}
		return card.Bottom;
	}

	private string ObjectiveText(ArcObjective o)
	{
		string text = ShellText.Clean(o.Text);
		if (!string.IsNullOrWhiteSpace(o.Npc))
		{
			string who = Ctx.Display(o.Npc);
			if (who.Length > 0 && !text.Contains(who))
			{
				text += "（" + who + "）";
			}
		}
		return text;
	}

	private void DrawConfirm(SpriteBatch b, Rectangle area, Hits hits)
	{
		b.Draw(Game1.fadeToBlackRect, area, Color.Black * 0.35f);
		hits.Add(area, () =>
		{
			Game1.playSound("bigDeSelect");
			_confirmDelete = null;
		});
		int w = Math.Min(560, area.Width - 48);
		string msg = Tr.Get("arcs.page.deleteMessage", "要把「{{title}}」从小剧场里删掉吗？删了就找不回来了。", ("title", ShellText.Clean(_confirmDelete!.Title)));
		List<string> lines = Hearth.WrapLines(msg, w - 56, 0.85f);
		int h = 28 + 40 + lines.Count * Hearth.LineHeight(0.85f) + 24 + 50 + 28;
		Rectangle card = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
		hits.Add(card, () => { });
		Hearth.CastShadow(b, card);
		Hearth.CardSlip(b, card);
		Hearth.InkBold(b, Tr.Get("arcs.page.deleteTitle", "删除小剧场？"), new Vector2(card.X + 28, card.Y + 28), Hearth.Quill);
		int y = card.Y + 72;
		foreach (string line in lines)
		{
			Hearth.Ink(b, line, new Vector2(card.X + 28, y), Hearth.QuillSoft, 0.85f);
			y += Hearth.LineHeight(0.85f);
		}
		Rectangle no = new Rectangle(card.Right - 28 - 140 - 12 - 140, y + 24, 140, 50);
		Rectangle yes = new Rectangle(card.Right - 28 - 140, y + 24, 140, 50);
		Hearth.PressPlate(b, no, Tr.Get("arcs.page.cancel", "取消"), hover: Hearth.Hover(no));
		Hearth.PressPlate(b, yes, Tr.Get("arcs.page.deleteYes", "删除"), primary: true, hover: Hearth.Hover(yes));
		hits.Add(no, () =>
		{
			Game1.playSound("bigDeSelect");
			_confirmDelete = null;
		});
		hits.Add(yes, Delete);
	}

	private void Delete()
	{
		StoryArc? arc = _confirmDelete;
		_confirmDelete = null;
		if (arc != null && Arcs?.Delete(arc.Id) == true)
		{
			Game1.playSound("trashcan");
			_selected = null;
			Reload();
			_detailScroll.Reset();
			Host.Notice(Tr.Get("arcs.deleted", "小剧场删掉了。"));
		}
		else
		{
			Game1.playSound("cancel");
		}
	}

	// ---- 文字 ----

	private static (string Text, Color Fill, Color Ink) Chip(StoryArc arc) => arc.State switch
	{
		StoryArc.Completable => (Tr.Get("arcs.page.ready", "可以收尾了"), Hearth.AmberPale, Hearth.Rust),
		StoryArc.Resolved => (Tr.Get("arcs.page.resolved", "已有结局"), PurpleSoft, Purple),
		StoryArc.Fizzled or "expired" => (Tr.Get("arcs.page.fizzled", "不了了之"), Hearth.Seam, Hearth.QuillSoft),
		_ => (Tr.Get("arcs.page.active", "进行中"), Hearth.LeafPale, Hearth.LeafDeep)
	};

	private static string Created(StoryArc arc) => string.IsNullOrWhiteSpace(arc.BornSeason) ? ""
		: Tr.Get("arcs.page.created", "第 {{year}} 年{{season}}", ("year", Math.Max(1, arc.BornYear)), ("season", ShellText.Season(arc.BornSeason)));

	public static string ChoiceLabel(ArcChoiceOption c) => ShellText.Clean(c.Label);

	public static string ChoiceDescription(ArcChoiceOption c) => ShellText.Clean(c.Text);
}
