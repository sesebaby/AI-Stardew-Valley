using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>伴侣：正在进行的小故事走到哪了、下一段的心愿、以前的故事。几位伴侣时顶上切换。</summary>
internal sealed class PartnersPage : ShellPage
{
	private static string? _lastPartner;

	private readonly Scroller _scroll = new Scroller();

	private readonly TextBox _wish;

	private IReadOnlyList<string> _partners = Array.Empty<string>();

	private string? _partner;

	private Rectangle _view;

	public PartnersPage(ShellContext ctx) : base(ctx)
	{
		_wish = new TextBox(null, null, Game1.smallFont, AisvTheme.Text) { textLimit = SpouseStoryRules.MaxThemeLength, limitWidth = false };
	}

	public override bool CapturesKeys => _wish.Selected;

	public override void Reload()
	{
		_partners = Ctx.Stories?.Partners() ?? Array.Empty<string>();
		_partner = _partners.FirstOrDefault(p => string.Equals(p, _partner ?? _lastPartner, StringComparison.OrdinalIgnoreCase)) ?? _partners.FirstOrDefault();
	}

	public override void Closed() => _wish.Selected = false;

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (_wish.Selected)
		{
			if (key == Keys.Escape)
			{
				_wish.Selected = false;
			}
			else if (key == Keys.Enter)
			{
				SaveWish();
			}
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		Kit.Header(b, header, Tr.Get("story.title", "伴侣"), Tr.Get("story.subtitle", "你们之间的小故事"), null, hits, null);
		_view = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		hits.Add(_view, () => _wish.Selected = false);
		SpouseStoryService? stories = Ctx.Stories;
		if (stories == null || _partner == null)
		{
			Kit.Empty(b, new Rectangle(_view.X, _view.Y + 60, _view.Width, 100), Tr.Get("story.noPartner", "你还没有结婚。结婚以后，这里会记下你和伴侣之间的小故事。"));
			return;
		}
		int w = Math.Min(_view.Width - 80, 980);
		int x = _view.X + (_view.Width - w) / 2;
		int y = _view.Y + 16 - _scroll.Offset;
		AisvTheme.BeginClip(b, _view);
		if (_partners.Count > 1)
		{
			float cx = x;
			foreach (string p in _partners)
			{
				string label = Ctx.Display(p);
				Rectangle r = new Rectangle((int)cx, y, (int)AisvTheme.Width(label, 0.85f) + 40, 44);
				AisvTheme.SubTab(b, r, label, p == _partner, AisvTheme.Hover(r));
				string pick = p;
				hits.Add(r, () => Select(pick), _view);
				cx = r.Right + 8;
			}
			y += 60;
		}
		y = DrawNow(b, stories, _partner, x, y, w) + 16;
		y = DrawWish(b, stories, _partner, x, y, w, hits) + 28;
		y = DrawHistory(b, stories, _partner, x, y, w);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y);
	}

	private void Select(string partner)
	{
		_partner = partner;
		_lastPartner = partner;
		_wish.Text = "";
		_wish.Selected = false;
		_scroll.Reset();
		Game1.playSound("smallSelect");
	}

	private int DrawNow(SpriteBatch b, SpouseStoryService stories, string partner, int x, int y, int w)
	{
		StoryPackage? p = stories.ActiveFor(partner);
		StoryStep? step = p?.Current;
		int tx = x + 128;
		int textW = x + w - 24 - tx;
		List<string> lines;
		if (p != null && step != null)
		{
			lines = AisvTheme.WrapLines(ShellText.Clean(stories.StepStatus(p, step)), textW, 0.8f);
		}
		else
		{
			string text = stories.IsGenerating(partner) ? Tr.Get("story.generating", "{{npc}}在想下一段故事……", ("npc", Ctx.Display(partner)))
				: !stories.Enabled ? Tr.Get("story.disabled", "伴侣剧情已关闭，可以在设置里打开。")
				: Math.Max(0, stories.NextStoryDay - (Game1.Date?.TotalDays ?? 0)) is int days && days > 0 ? Tr.Get("story.nextIn", "下一段故事大约在 {{days}} 天后开始。", ("days", days))
				: Tr.Get("story.soon", "下一段故事很快就会开始。");
			lines = AisvTheme.WrapLines(ShellText.Clean(text), textW, 0.8f);
		}
		int lh = AisvTheme.LineHeight(0.8f);
		Rectangle card = new Rectangle(x, y, w, Math.Max(132, 64 + (p != null ? 40 : 0) + lines.Count * lh + 20));
		AisvTheme.DrawCard(b, card);
		AisvTheme.FillRounded(b, new Rectangle(card.X + 2, card.Y + 12, 6, card.Height - 24), AisvTheme.Pink, 3);
		Kit.Avatar(b, new Rectangle(card.X + 28, card.Y + 22, 84, 84), Ctx, partner);
		AisvTheme.Bold(b, Ctx.Display(partner), new Vector2(tx, card.Y + 18), AisvTheme.Text, 0.95f);
		int ty = card.Y + 58;
		if (p != null && step != null)
		{
			Rectangle chip = AisvTheme.Chip(b, new Vector2(tx, ty), Tr.Get("story.inProgress", "正在进行"), AisvTheme.PinkSoft, AisvTheme.Pink, 0.7f);
			AisvTheme.Bold(b, AisvTheme.Truncate(ShellText.Clean("《" + p.Title + "》"), x + w - 24 - chip.Right - 8, 0.85f), new Vector2(chip.Right + 8, ty - 2), AisvTheme.Text, 0.85f);
			string progress = Tr.Get("story.progress", "第 {{n}}/{{total}} 步", ("n", step.Index + 1), ("total", p.Steps.Count));
			AisvTheme.DrawText(b, progress, new Vector2(card.Right - 24 - AisvTheme.Width(progress, 0.72f), card.Y + 22), AisvTheme.TextFaint, 0.72f);
			ty += 40;
		}
		foreach (string line in lines)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.TextMuted, 0.8f);
			ty += lh;
		}
		return card.Bottom;
	}

	private int DrawWish(SpriteBatch b, SpouseStoryService stories, string partner, int x, int y, int w, Hits hits)
	{
		Rectangle card = new Rectangle(x, y, w, 168);
		AisvTheme.DrawCard(b, card);
		AisvTheme.Bold(b, Tr.Get("story.wishTitle", "下一段故事，你想要什么？"), new Vector2(card.X + 24, card.Y + 18), AisvTheme.Text, 0.85f);
		string saved = stories.Theme(partner);
		if (saved.Length > 0)
		{
			string note = AisvTheme.Truncate(ShellText.Clean(Tr.Get("story.wishSaved", "已经告诉TA：") + saved), w / 2f, 0.72f);
			AisvTheme.DrawText(b, note, new Vector2(card.Right - 24 - AisvTheme.Width(note, 0.72f), card.Y + 22), AisvTheme.Gold, 0.72f);
		}
		const int buttonW = 150;
		Rectangle field = new Rectangle(card.X + 24, card.Y + 62, card.Width - 48 - buttonW * 2 - 24, 48);
		Rectangle save = new Rectangle(field.Right + 12, field.Y + 2, buttonW, 44);
		Rectangle start = new Rectangle(save.Right + 12, field.Y + 2, buttonW, 44);
		AisvTheme.Field(b, field, _wish.Selected);
		string text = _wish.Text ?? "";
		Vector2 tp = new Vector2(field.X + 16, field.Center.Y - 14);
		if (text.Length == 0 && !_wish.Selected)
		{
			AisvTheme.DrawText(b, Tr.Get("story.wishPlaceholder", "比如：一起去看海上的日落"), tp, AisvTheme.TextFaint, 0.85f);
		}
		else
		{
			string shown = text;
			while (shown.Length > 1 && AisvTheme.Width(shown, 0.85f) > field.Width - 32)
			{
				shown = shown.Substring(1);
			}
			AisvTheme.DrawText(b, shown, tp, AisvTheme.Text, 0.85f);
			if (_wish.Selected && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 550)
			{
				AisvTheme.Fill(b, new Rectangle((int)tp.X + (int)AisvTheme.Width(shown, 0.85f) + 2, field.Center.Y - 12, 2, 24), AisvTheme.Accent);
			}
		}
		hits.Add(field, () => _wish.Selected = true, _view);
		bool hasText = text.Trim().Length > 0 && StardewModdingAPI.Context.IsMainPlayer;
		AisvTheme.Button(b, save, Tr.Get("story.wishSave", "交给TA"), primary: false, hover: AisvTheme.Hover(save), disabled: !hasText, scale: 0.85f);
		if (hasText)
		{
			hits.Add(save, SaveWish, _view);
		}
		bool canStart = CanStart(stories, partner);
		AisvTheme.Button(b, start, Tr.Get("story.startNow", "现在就开始"), primary: true, hover: AisvTheme.Hover(start), disabled: !canStart, scale: 0.85f);
		if (canStart)
		{
			hits.Add(start, () => StartNow(stories, partner), _view);
		}
		AisvTheme.DrawText(b, AisvTheme.Truncate(Tr.Get("story.wishHint", "写下你想要的，下一段故事会照这个来；也可以让TA现在就开始。"), card.Width - 48, 0.72f), new Vector2(card.X + 24, field.Bottom + 16), AisvTheme.TextFaint, 0.72f);
		return card.Bottom;
	}

	private static bool CanStart(SpouseStoryService stories, string partner) => stories.Enabled && stories.ActiveFor(partner) == null && !stories.IsGenerating(partner) && StardewModdingAPI.Context.IsMainPlayer;

	private void SaveWish()
	{
		if (Ctx.Stories == null || _partner == null || (_wish.Text ?? "").Trim().Length == 0)
		{
			return;
		}
		Ctx.Stories.SetTheme(_partner, _wish.Text ?? "");
		_wish.Text = "";
		_wish.Selected = false;
		Game1.playSound("newArtifact");
		Host.Notice(Tr.Get("story.themeSaved", "记下了，下一段故事会照这个来。"));
	}

	private void StartNow(SpouseStoryService stories, string partner)
	{
		if ((_wish.Text ?? "").Trim().Length > 0)
		{
			stories.SetTheme(partner, _wish.Text ?? "");
			_wish.Text = "";
		}
		_wish.Selected = false;
		if (stories.StartStory(partner, null, out string reason))
		{
			Game1.playSound("newArtifact");
			Host.Notice(Tr.Get("story.starting", "{{npc}}在想下一段故事……", ("npc", Ctx.Display(partner))));
		}
		else
		{
			Game1.playSound("cancel");
			Host.Notice(reason.Contains("AI") ? Tr.Get("story.cantStartAi", "现在联系不上 AI，晚点再试。") : Tr.Get("story.cantStart", "现在还不行。"));
		}
	}

	private int DrawHistory(SpriteBatch b, SpouseStoryService stories, string partner, int x, int y, int w)
	{
		Kit.SectionTitle(b, Tr.Get("story.historyTitle", "以前的故事"), x, y);
		y += 40;
		IReadOnlyList<StoryPackage> history = stories.HistoryFor(partner);
		if (history.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("story.historyEmpty", "还没有。"), new Vector2(x + 4, y), AisvTheme.TextMuted, 0.8f);
			return y + 52;
		}
		int lh = AisvTheme.LineHeight(0.75f);
		foreach (StoryPackage p in history)
		{
			List<string> summary = AisvTheme.WrapLines(ShellText.Clean(p.Summary), w - 48, 0.75f).Take(3).ToList();
			Rectangle card = new Rectangle(x, y, w, 64 + summary.Count * lh + 12);
			AisvTheme.DrawCard(b, card);
			(Color fill, Color ink) = p.State switch
			{
				StoryStates.Done => (AisvTheme.AccentSoft, AisvTheme.AccentDark),
				StoryStates.Failed => (AisvTheme.PinkSoft, AisvTheme.Pink),
				_ => (AisvTheme.Divider, AisvTheme.TextMuted)
			};
			Rectangle chip = AisvTheme.Chip(b, new Vector2(card.X + 24, card.Y + 18), SpouseStoryRules.StateLabel(p.State), fill, ink, 0.68f);
			AisvTheme.Bold(b, AisvTheme.Truncate(ShellText.Clean("《" + p.Title + "》"), w - 220, 0.85f), new Vector2(chip.Right + 10, card.Y + 16), AisvTheme.Text, 0.85f);
			StardewModdingAPI.Utilities.SDate closed = StardewModdingAPI.Utilities.SDate.FromDaysSinceStart(Math.Max(1, p.ClosedDay + 1));
			string when = ShellText.DaysAgo((Game1.Date?.TotalDays ?? 0) - p.ClosedDay, closed.Day, closed.SeasonKey);
			AisvTheme.DrawText(b, when, new Vector2(card.Right - 24 - AisvTheme.Width(when, 0.7f), card.Y + 20), AisvTheme.TextFaint, 0.7f);
			int ty = card.Y + 56;
			foreach (string line in summary)
			{
				AisvTheme.DrawText(b, line, new Vector2(card.X + 24, ty), AisvTheme.TextMuted, 0.75f);
				ty += lh;
			}
			y = card.Bottom + 10;
		}
		return y + 20;
	}
}
