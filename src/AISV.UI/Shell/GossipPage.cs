using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Social.Gossip;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>八卦：镇上传开的事，新的在前；可以只看今天或这七天。</summary>
internal sealed class GossipPage : ShellPage
{
	private sealed class Item
	{
		public GossipView View = null!;

		public List<string> Lines = new List<string>();

		public string When = "";

		public bool Today;

		public bool Festival;

		public List<string> Aware = new List<string>();

		public int AwareCount;

		public int Height;
	}

	private readonly Scroller _scroll = new Scroller();

	private List<GossipEntry> _entries = new List<GossipEntry>();

	private List<Item> _items = new List<Item>();

	private int _filter;

	private int _builtWidth = -1;

	private Rectangle _feed;

	public GossipPage(ShellContext ctx) : base(ctx)
	{
	}

	private static string[] Filters => new[] { Tr.Get("shell.gossip.all", "全部"), Tr.Get("shell.gossip.today", "今天"), Tr.Get("shell.gossip.week", "这七天") };

	public override void Reload()
	{
		_entries = Ctx.Social.Gossip.Feed().ToList();
		_builtWidth = -1;
	}

	private void Build(int width)
	{
		_builtWidth = width;
		int today = GameState.Today.TotalDays;
		int textW = width - 40 - 64;
		int lh = Hearth.LineHeight(0.9f);
		_items = new List<Item>();
		foreach (GossipEntry e in _entries)
		{
			int day = e.BornOn > 0 ? e.BornOn : e.TotalDay;
			if (_filter == 1 && day != today || _filter == 2 && today - day > 6)
			{
				continue;
			}
			GossipView view = GossipView.Resolve(e, Ctx.Display);
			Item item = new Item
			{
				View = view,
				Today = day == today,
				Festival = e.IsFestival,
				When = ShellText.DaysAgo(today - day, e.Day, e.Season),
				Aware = e.KnownTo.Where(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, view.Speaker, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
				AwareCount = e.KnownTo.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
				Lines = Hearth.WrapLines(view.Text, textW, 0.9f)
			};
			bool meta = view.Origin != null || item.AwareCount > 0;
			item.Height = 20 + 30 + 8 + item.Lines.Count * lh + 12 + (meta ? 40 : 0) + 16;
			_items.Add(item);
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _feed.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle header = new Rectangle(area.X, area.Y, area.Width, 64);
		_feed = new Rectangle(area.X, header.Bottom, area.Width, area.Bottom - header.Bottom);
		int colW = Math.Min(_feed.Width - 80, 900);
		Rectangle column = new Rectangle(_feed.X + (_feed.Width - colW) / 2, _feed.Y + 12, colW, _feed.Height - 12);
		if (_builtWidth != colW)
		{
			Build(colW);
		}
		DrawHeader(b, header, hits);
		Hearth.BeginClip(b, _feed);
		int x = column.X, w = column.Width;
		int y = column.Y - _scroll.Offset;
		y = Kit.RetryCard(b, Ctx, gossip: true, x, y, w, _feed, hits, Host);
		if (_items.Count == 0)
		{
			Kit.Empty(b, new Rectangle(x, y + 20, w, 80), _entries.Count == 0 ? Tr.Get("shell.gossip.none", "还没有八卦，镇上很安静。") : Tr.Get("shell.gossip.noneHere", "这段时间没有新的八卦。"), _entries.Count == 0 ? Tr.Get("shell.gossip.noneHint", "和村民多聊聊，晚上睡觉时会整理出来。") : "");
			y += 120;
		}
		else
		{
			Rectangle list = new Rectangle(x, y, w, _items.Sum(i => i.Height) + 8);
			Hearth.CardSlip(b, list);
			int iy = y + 4;
			for (int i = 0; i < _items.Count; i++)
			{
				Item it = _items[i];
				if (iy + it.Height >= _feed.Y && iy <= _feed.Bottom)
				{
					DrawItem(b, it, new Rectangle(x, iy, w, it.Height), i > 0);
				}
				iy += it.Height;
			}
			y = list.Bottom + 16;
		}
		Hearth.EndClip(b);
		_scroll.Finish(b, _feed, y);
	}

	private void DrawHeader(SpriteBatch b, Rectangle header, Hits hits)
	{
		Kit.Header(b, header, Tr.Get("shell.gossip.title", "八卦"), "", null, hits, null);
		int fx = header.X + 20 + (int)Hearth.Width(Tr.Get("shell.gossip.title", "八卦"), 1.15f) + 28;
		string[] labels = Filters;
		for (int i = 0; i < labels.Length; i++)
		{
			int w = (int)Hearth.Width(labels[i], 0.85f) + 32;
			Rectangle r = new Rectangle(fx, header.Y + 10, w, 40);
			bool sel = i == _filter;
			Hearth.Bordered(b, r, sel ? Hearth.LeafPale : Hearth.Hover(r) ? Hearth.Slip : Hearth.Parchment, sel ? Hearth.Leaf : Hearth.SlipEdge, r.Height / 2);
			Hearth.InkMiddle(b, labels[i], r, sel ? Hearth.LeafDeep : Hearth.QuillSoft, 0.85f);
			int index = i;
			hits.Add(r, () =>
			{
				if (_filter != index)
				{
					Game1.playSound("smallSelect");
					_filter = index;
					_scroll.Reset();
					_builtWidth = -1;
				}
			});
			fx = r.Right + 8;
		}
	}

	private void DrawItem(SpriteBatch b, Item it, Rectangle r, bool divider)
	{
		int lh = Hearth.LineHeight(0.9f);
		if (divider)
		{
			Hearth.HLine(b, r.X + 88, r.Y, r.Width - 108, Hearth.Seam);
		}
		Rectangle face = new Rectangle(r.X + 20, r.Y + 20, 52, 52);
		if (it.View.Speaker != null)
		{
			Kit.Avatar(b, face, Ctx, it.View.Speaker);
		}
		else
		{
			Hearth.Glyph(b, face, Hearth.QuillPale, "?");
		}
		int tx = face.Right + 16;
		string name = it.View.Speaker == null ? Tr.Get("shell.gossip.someone", "镇上有人说") : Ctx.Display(it.View.Speaker);
		Hearth.InkBold(b, name, new Vector2(tx, r.Y + 20), Hearth.LeafDeep, 0.95f);
		float cx = tx + Hearth.Width(name, 0.95f) + 14;
		Hearth.Ink(b, it.When, new Vector2(cx, r.Y + 24), Hearth.QuillPale, 0.72f);
		cx += Hearth.Width(it.When, 0.72f) + 10;
		if (it.Today)
		{
			cx = Hearth.Tag(b, new Vector2(cx, r.Y + 20), Tr.Get("shell.gossip.fresh", "新"), Hearth.Rose, Color.White, 0.6f).Right + 8;
		}
		if (it.Festival)
		{
			Hearth.Tag(b, new Vector2(cx, r.Y + 20), Tr.Get("shell.gossip.festival", "节日"), Hearth.AmberPale, Hearth.Amber, 0.6f);
		}
		int ty = r.Y + 58;
		foreach (string line in it.Lines)
		{
			Hearth.Ink(b, line, new Vector2(tx, ty), Hearth.Quill, 0.9f);
			ty += lh;
		}
		if (it.View.Origin == null && it.AwareCount == 0)
		{
			return;
		}
		ty += 12;
		List<string> parts = new List<string>();
		if (it.View.Origin != null)
		{
			parts.Add(Tr.Get("shell.gossip.origin", "最早是{{name}}说起的", ("name", Ctx.Display(it.View.Origin))));
		}
		if (it.AwareCount > 0)
		{
			parts.Add(Tr.Get("shell.gossip.aware", "{{n}} 个人知道了", ("n", it.AwareCount)));
		}
		int faces = Math.Min(8, it.Aware.Count);
		int facesW = faces * 30 + (it.Aware.Count > faces ? 40 : 0);
		Hearth.Ink(b, Hearth.Truncate(string.Join(" · ", parts), r.Right - 20 - facesW - 16 - tx, 0.75f), new Vector2(tx, ty + 6), Hearth.QuillSoft, 0.75f);
		int fx = r.Right - 20 - facesW;
		for (int i = 0; i < faces; i++)
		{
			Kit.Avatar(b, new Rectangle(fx + i * 30, ty, 28, 28), Ctx, it.Aware[i]);
		}
		if (it.Aware.Count > faces)
		{
			Hearth.Ink(b, "+" + (it.Aware.Count - faces), new Vector2(fx + faces * 30 + 6, ty + 4), Hearth.QuillSoft, 0.75f);
		}
	}
}
