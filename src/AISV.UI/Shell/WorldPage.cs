using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Web;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AISV.UI.Shell;

/// <summary>世界：村民之间最近发生的事，和谁跟谁走得近。</summary>
internal sealed class WorldPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private List<NpcSocialMoment> _moments = new List<NpcSocialMoment>();

	private List<NpcSocialEdge> _edges = new List<NpcSocialEdge>();

	private Rectangle _feed;

	public WorldPage(ShellContext ctx) : base(ctx)
	{
	}

	public static string ToneWord(string tone) => (tone ?? "").Trim().ToLowerInvariant() switch
	{
		"sweet" => Tr.Get("shell.tone.sweet", "温馨"),
		"funny" => Tr.Get("shell.tone.funny", "好笑"),
		"awkward" => Tr.Get("shell.tone.awkward", "尴尬"),
		"tense" => Tr.Get("shell.tone.tense", "紧张"),
		"strange" => Tr.Get("shell.tone.strange", "古怪"),
		_ => Tr.Get("shell.tone.everyday", "日常")
	};

	public override void Reload()
	{
		int today = GameState.Today.TotalDays;
		_moments = Ctx.Social.Web.ForFeed(today, 30, 30).Where(m => Ctx.Npcs.IsEnabled(m.First) && Ctx.Npcs.IsEnabled(m.Second)).ToList();
		_edges = Ctx.Social.Web.Edges.Where(e => e.Encounters > 0 && Ctx.Npcs.IsEnabled(e.First) && Ctx.Npcs.IsEnabled(e.Second)).OrderByDescending(e => e.Closeness + e.Friction).Take(24).ToList();
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _feed.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		_feed = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		Kit.Header(b, header, Tr.Get("shell.world.title", "村民之间"), _moments.Count > 0 ? Tr.Get("shell.world.count", "最近 {{n}} 件事", ("n", _moments.Count)) : "", null, hits, null);
		AisvTheme.BeginClip(b, _feed);
		const int pad = 28;
		int x = _feed.X + pad, w = _feed.Width - pad * 2;
		int y = _feed.Y + 16 - _scroll.Offset;
		if (w >= 1000)
		{
			const int gap = 24;
			int lw = (w - gap) * 60 / 100, rw = w - gap - lw;
			int ly = DrawMoments(b, x, y, lw);
			int ry = DrawEdges(b, x + lw + gap, y, rw);
			y = Math.Max(ly, ry) + 20;
		}
		else
		{
			y = DrawMoments(b, x, y, w) + 16;
			y = DrawEdges(b, x, y, w) + 20;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _feed, y);
	}

	private int DrawMoments(SpriteBatch b, int x, int y, int w)
	{
		const float scale = 0.85f;
		int lh = AisvTheme.LineHeight(scale);
		int today = GameState.Today.TotalDays;
		List<(NpcSocialMoment M, List<string> Lines)> items = _moments.Select(m => (m, AisvTheme.WrapLines(ShellText.Clean(m.WhatHappened) + Quote(m), w - 40 - 96, scale))).ToList();
		int h = 18 + 34 + (items.Count == 0 ? 56 : items.Sum(i => 76 + i.Lines.Count * lh)) + 8;
		Rectangle card = new Rectangle(x, y, w, h);
		AisvTheme.DrawCard(b, card);
		Kit.SectionTitle(b, Tr.Get("shell.world.moments", "最近的事"), x + 20, y + 18);
		int cy = y + 52;
		if (items.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("shell.world.noMoments", "还没什么动静。每天晚上村民之间会有一两件小事。"), new Vector2(x + 20, cy + 14), AisvTheme.TextFaint, 0.8f);
			return card.Bottom;
		}
		for (int i = 0; i < items.Count; i++)
		{
			(NpcSocialMoment m, List<string> lines) = items[i];
			if (i > 0)
			{
				AisvTheme.HLine(b, x + 96, cy, w - 116, AisvTheme.Divider);
			}
			Kit.Avatar(b, new Rectangle(x + 20, cy + 16, 44, 44), Ctx, m.First);
			Kit.Avatar(b, new Rectangle(x + 44, cy + 36, 34, 34), Ctx, m.Second);
			int tx = x + 96;
			string pair = Tr.Get("shell.today.pair", "{{a}}和{{b}}", ("a", Ctx.Display(m.First)), ("b", Ctx.Display(m.Second)));
			AisvTheme.Bold(b, pair, new Vector2(tx, cy + 14), AisvTheme.AccentDark, 0.9f);
			string meta = Places.Name(m.Place) + " · " + ShellText.DaysAgo(today - m.OnDay, m.Day, m.Season);
			float px = tx + AisvTheme.Width(pair, 0.9f) + 12;
			AisvTheme.DrawText(b, meta, new Vector2(px, cy + 18), AisvTheme.TextFaint, 0.7f);
			string tone = ToneWord(m.Vibe);
			AisvTheme.Chip(b, new Vector2(x + w - 20 - AisvTheme.ChipWidth(tone, 0.62f), cy + 14), tone, AisvTheme.PageDeep, AisvTheme.TextMuted, 0.62f);
			int ty = cy + 50;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.Text, scale);
				ty += lh;
			}
			cy += 76 + lines.Count * lh;
		}
		return card.Bottom;
	}

	private string Quote(NpcSocialMoment m)
	{
		List<string> said = new List<string>();
		if (!string.IsNullOrWhiteSpace(m.LineA))
		{
			said.Add(Ctx.Display(m.First) + "：“" + m.LineA!.Trim() + "”");
		}
		if (!string.IsNullOrWhiteSpace(m.LineB))
		{
			said.Add(Ctx.Display(m.Second) + "：“" + m.LineB!.Trim() + "”");
		}
		return said.Count == 0 ? "" : "\n" + string.Join("\n", said);
	}

	private int DrawEdges(SpriteBatch b, int x, int y, int w)
	{
		const int rowH = 62;
		Rectangle card = new Rectangle(x, y, w, 18 + 34 + Math.Max(1, _edges.Count) * rowH + 12);
		AisvTheme.DrawCard(b, card);
		Kit.SectionTitle(b, Tr.Get("shell.world.edges", "谁和谁"), x + 20, y + 18);
		int ry = y + 52;
		if (_edges.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("shell.world.noEdges", "还看不出谁和谁走得近。"), new Vector2(x + 20, ry + 16), AisvTheme.TextFaint, 0.8f);
			return card.Bottom;
		}
		for (int i = 0; i < _edges.Count; i++)
		{
			NpcSocialEdge e = _edges[i];
			if (i > 0)
			{
				AisvTheme.HLine(b, x + 20, ry, w - 40, AisvTheme.Divider);
			}
			Kit.Avatar(b, new Rectangle(x + 20, ry + 11, 40, 40), Ctx, e.First);
			Kit.Avatar(b, new Rectangle(x + 64, ry + 11, 40, 40), Ctx, e.Second);
			string state = SocialWeb.StateWord(e.Standing);
			bool bad = e.Standing is "rivalry" or "tense" or "awkward";
			int cw = AisvTheme.ChipWidth(state, 0.62f);
			AisvTheme.Chip(b, new Vector2(x + w - 20 - cw, ry + 10), state, bad ? AisvTheme.PinkSoft : AisvTheme.AccentSoft, bad ? AisvTheme.Pink : AisvTheme.AccentDark, 0.62f);
			int tx = x + 118, right = x + w - 30 - cw;
			AisvTheme.DrawText(b, AisvTheme.Truncate(Tr.Get("shell.today.pair", "{{a}}和{{b}}", ("a", Ctx.Display(e.First)), ("b", Ctx.Display(e.Second))), right - tx, 0.82f), new Vector2(tx, ry + 8), AisvTheme.Text, 0.82f);
			AisvTheme.DrawText(b, Tr.Get("shell.world.times", "来往 {{n}} 次", ("n", e.Encounters)), new Vector2(tx, ry + 34), AisvTheme.TextFaint, 0.68f);
			ry += rowH;
		}
		return card.Bottom;
	}
}
