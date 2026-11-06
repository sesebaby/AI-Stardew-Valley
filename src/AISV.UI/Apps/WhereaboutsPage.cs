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
using StardewValley.WorldMaps;

namespace AISV.UI.Apps;

/// <summary>大家都在哪：按地点列出村民，或者摆在世界地图上；选一位看今天的路线和几点回家。</summary>
internal sealed class WhereaboutsPage : ShellPage
{
	private sealed class MapView
	{
		public string Region = "";

		public List<MapAreaTexture> Textures = new List<MapAreaTexture>();

		public int Width;

		public int Height;

		public Rectangle Bounds;

		public float Scale = 1f;

		public static MapView? Load(string region)
		{
			MapRegion? found = WorldMapManager.GetMapRegions().FirstOrDefault(r => string.Equals(r.Id, region, StringComparison.OrdinalIgnoreCase));
			if (found == null)
			{
				return null;
			}
			MapView view = new MapView { Region = found.Id };
			if (found.GetBaseTexture() is { Texture: not null } baseTexture)
			{
				view.Textures.Add(baseTexture);
			}
			view.Textures.AddRange(found.GetAreas().SelectMany(a => a.GetTextures()).Where(t => t.Texture != null));
			view.Width = view.Textures.Select(t => t.MapPixelArea.Right).DefaultIfEmpty(0).Max() / 4;
			view.Height = view.Textures.Select(t => t.MapPixelArea.Bottom).DefaultIfEmpty(0).Max() / 4;
			return view.Width > 0 && view.Height > 0 ? view : null;
		}

		public void Fit(Rectangle area)
		{
			Scale = Math.Min(area.Width / (float)Width, area.Height / (float)Height);
			int w = (int)(Width * Scale), h = (int)(Height * Scale);
			Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
		}

		public Vector2 ToScreen(float x, float y) => new Vector2(Bounds.X + x * Scale, Bounds.Y + y * Scale);

		public void Draw(SpriteBatch b)
		{
			foreach (MapAreaTexture t in Textures.Where(t => !t.Texture.IsDisposed))
			{
				Vector2 a = ToScreen(t.MapPixelArea.X / 4f, t.MapPixelArea.Y / 4f), z = ToScreen(t.MapPixelArea.Right / 4f, t.MapPixelArea.Bottom / 4f);
				b.Draw(t.Texture, new Rectangle((int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(z.X - a.X), (int)Math.Round(z.Y - a.Y)), t.SourceRect, Color.White);
			}
		}
	}

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _detailScroll = new Scroller();

	private IReadOnlyList<WhereGroup> _groups = Array.Empty<WhereGroup>();

	private Dictionary<string, (string Region, float X, float Y)> _points = new Dictionary<string, (string, float, float)>(StringComparer.OrdinalIgnoreCase);

	private string _homeRegion = "";

	private MapView? _map;

	private bool _mapFailed;

	private bool _mapMode;

	private string? _selected;

	private Rectangle _listView, _detail;

	public WhereaboutsPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("apps.where", "大家都在哪");

	public override void Reload()
	{
		int now = Game1.timeOfDay;
		List<WherePlan> plans = AppData.WhereInputs(Ctx).Select(w => Whereabouts.Plan(w, now)).ToList();
		_groups = Whereabouts.Group(plans, Tr.Get("where.hidden", "看不到的人"));
		_points = AppData.WherePoints(Ctx);
		_homeRegion = AppData.HomeRegion(Ctx);
		if (_selected == null || !plans.Any(p => p.Who.Npc == _selected))
		{
			_selected = AppData.Closest(Ctx, plans.Select(p => p.Who.Npc));
		}
	}

	public override void Scroll(int direction)
	{
		if (!_mapMode && _listView.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			_listScroll.Wheel(direction, 64);
		}
		else
		{
			_detailScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key) => _detailScroll.PageKeys(key, _detail.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		Rectangle bar = new Rectangle(area.X, area.Y, area.Width, 60);
		AisvTheme.Fill(b, bar, AisvTheme.PageDeep);
		AisvTheme.HLine(b, bar.X, bar.Bottom - 2, bar.Width, AisvTheme.Divider);
		Rectangle list = new Rectangle(bar.X + 16, bar.Y + 10, 96, 40), map = new Rectangle(list.Right + 8, list.Y, 96, 40);
		AisvTheme.Button(b, list, Tr.Get("where.tabList", "列表"), primary: !_mapMode, hover: AisvTheme.Hover(list), scale: 0.8f);
		AisvTheme.Button(b, map, Tr.Get("where.tabMap", "地图"), primary: _mapMode, hover: AisvTheme.Hover(map), scale: 0.8f);
		hits.Add(list, () => _mapMode = false);
		hits.Add(map, () => _mapMode = true);
		string clock = Tr.Get("where.now", "现在 {{time}}", ("time", Game1.getTimeOfDayString(Game1.timeOfDay)));
		AisvTheme.DrawText(b, clock, new Vector2(bar.Right - 20 - AisvTheme.Width(clock, 0.8f), bar.Y + 18), AisvTheme.TextMuted, 0.8f);
		Rectangle body = new Rectangle(area.X, bar.Bottom, area.Width, area.Bottom - bar.Bottom);
		if (_mapMode)
		{
			DrawMap(b, body, hits);
			return;
		}
		int listW = Math.Clamp(body.Width * 44 / 100, 380, 560);
		Rectangle panel = new Rectangle(body.X, body.Y, listW, body.Height);
		_detail = new Rectangle(panel.Right, body.Y, body.Right - panel.Right, body.Height);
		DrawGroups(b, panel, hits);
		DrawDetail(b);
	}

	private void DrawGroups(SpriteBatch b, Rectangle panel, Hits hits)
	{
		AisvTheme.Fill(b, panel, AisvTheme.Card);
		AisvTheme.VLine(b, panel.Right - 2, panel.Y, panel.Height, AisvTheme.Divider);
		_listView = AisvTheme.Inflate(panel, -6);
		AisvTheme.BeginClip(b, _listView);
		int y = _listView.Y - _listScroll.Offset;
		const int rowH = 66;
		foreach (WhereGroup group in _groups)
		{
			AisvTheme.Bold(b, AisvTheme.Truncate(ShellText.Clean(group.Name), _listView.Width - 80, 0.82f), new Vector2(_listView.X + 12, y + 8), AisvTheme.AccentDark, 0.82f);
			string count = group.People.Count.ToString();
			AisvTheme.DrawText(b, count, new Vector2(_listView.Right - 24 - AisvTheme.Width(count, 0.72f), y + 10), AisvTheme.TextFaint, 0.72f);
			y += 40;
			foreach (WherePlan plan in group.People)
			{
				Rectangle row = new Rectangle(_listView.X, y, _listView.Width, rowH);
				if (row.Bottom >= _listView.Y && row.Y <= _listView.Bottom)
				{
					DrawRow(b, plan, row);
					string npc = plan.Who.Npc;
					hits.Add(row, () => Select(npc), _listView);
				}
				y += rowH + 2;
			}
			y += 6;
		}
		AisvTheme.EndClip(b);
		_listScroll.Finish(b, _listView, y, 6);
	}

	private void Select(string npc)
	{
		if (_selected != npc)
		{
			Game1.playSound("smallSelect");
			_selected = npc;
			_detailScroll.Reset();
		}
	}

	private void DrawRow(SpriteBatch b, WherePlan plan, Rectangle row)
	{
		if (plan.Who.Npc == _selected)
		{
			AisvTheme.FillRounded(b, row, AisvTheme.SelectedRow, 10);
			AisvTheme.Fill(b, new Rectangle(row.X, row.Y + 8, 4, row.Height - 16), AisvTheme.Accent);
		}
		else if (AisvTheme.Hover(row, _listView))
		{
			AisvTheme.FillRounded(b, row, AisvTheme.PageDeep, 10);
		}
		Kit.Avatar(b, new Rectangle(row.X + 12, row.Center.Y - 22, 44, 44), Ctx, plan.Who.Npc);
		string state = StateWord(plan);
		int cw = AisvTheme.ChipWidth(state, 0.62f);
		AisvTheme.Chip(b, new Vector2(row.Right - 12 - cw, row.Y + 10), state, plan.State == WhereState.Walking ? AisvTheme.GoldSoft : AisvTheme.PageDeep, AisvTheme.TextMuted, 0.62f);
		int tx = row.X + 68, right = row.Right - 12;
		AisvTheme.DrawText(b, AisvTheme.Truncate(plan.Who.Name, right - cw - 8 - tx, 0.9f), new Vector2(tx, row.Y + 8), AisvTheme.Text, 0.9f);
		string next = NextLine(plan);
		bool soon = plan.Next != null && Whereabouts.IsSoon(Game1.timeOfDay, plan.Next.Time);
		AisvTheme.DrawText(b, AisvTheme.Truncate(next, right - tx, 0.7f), new Vector2(tx, row.Y + 38), soon ? AisvTheme.AccentDark : AisvTheme.TextFaint, 0.7f);
	}

	private static string StateWord(WherePlan plan) => plan.State switch
	{
		WhereState.Home => Tr.Get("where.state.home", "在家"),
		WhereState.Walking => Tr.Get("where.state.walking", "在路上"),
		WhereState.Hidden => Tr.Get("where.state.hidden", "看不到"),
		_ => Tr.Get("where.state.out", "在外面")
	};

	private static string NextLine(WherePlan plan)
	{
		if (plan.State == WhereState.Hidden)
		{
			return Tr.Get("where.next.hidden", "不知道去哪了");
		}
		if (plan.State == WhereState.Walking)
		{
			return Tr.Get("where.next.walking", "正去{{place}}", ("place", ShellText.Clean(plan.Who.HeadingName)));
		}
		return plan.Next != null
			? Tr.Get("where.next.stop", "{{time}} 去{{place}}", ("time", Game1.getTimeOfDayString(plan.Next.Time)), ("place", ShellText.Clean(plan.Next.Name)))
			: Tr.Get("where.next.none", "今天没有别的安排");
	}

	private static string HomeLine(WherePlan plan) => plan.Home switch
	{
		HomeReturn.StaysHome => Tr.Get("where.home.stays", "今天不出门了"),
		HomeReturn.OnTheWay => Tr.Get("where.home.onWay", "正在回家路上"),
		HomeReturn.At => Tr.Get("where.home.at", "{{time}} 回家", ("time", Game1.getTimeOfDayString(plan.HomeTime))),
		HomeReturn.EndOfDay => Tr.Get("where.home.late", "要到晚上才回家"),
		_ => Tr.Get("where.home.unknown", "不知道几点回家")
	};

	private void DrawDetail(SpriteBatch b)
	{
		WherePlan? plan = _groups.SelectMany(g => g.People).FirstOrDefault(p => p.Who.Npc == _selected);
		if (plan == null)
		{
			AisvTheme.TextCentered(b, Tr.Get("where.pick", "选一位村民看看。"), _detail, AisvTheme.TextMuted);
			return;
		}
		AisvTheme.BeginClip(b, _detail);
		int w = Math.Min(_detail.Width - 48, 640), x = _detail.X + (_detail.Width - w) / 2;
		int y = _detail.Y + 18 - _detailScroll.Offset;
		Rectangle head = new Rectangle(x, y, w, 148);
		AisvTheme.DrawCard(b, head);
		Kit.Avatar(b, new Rectangle(head.X + 18, head.Y + 20, 108, 108), Ctx, plan.Who.Npc);
		int tx = head.X + 144, tw = head.Right - 16 - tx;
		AisvTheme.Bold(b, AisvTheme.Truncate(plan.Who.Name, tw, 1.15f), new Vector2(tx, head.Y + 18), AisvTheme.Text, 1.15f);
		string now = plan.State == WhereState.Hidden ? Tr.Get("where.next.hidden", "不知道去哪了") : Tr.Get("agenda.at", "现在在{{place}}", ("place", ShellText.Clean(plan.Who.PlaceName)));
		AisvTheme.DrawText(b, AisvTheme.Truncate(now, tw, 0.8f), new Vector2(tx, head.Y + 58), AisvTheme.AccentDark, 0.8f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(HomeLine(plan), tw, 0.76f), new Vector2(tx, head.Y + 92), AisvTheme.TextMuted, 0.76f);
		y = head.Bottom + 14;
		y = DrawRoute(b, plan, x, y, w);
		AisvTheme.EndClip(b);
		_detailScroll.Finish(b, _detail, y + 12);
	}

	private int DrawRoute(SpriteBatch b, WherePlan plan, int x, int y, int w)
	{
		List<WhereStop> stops = plan.Who.Stops.OrderBy(s => s.Time).ToList();
		const int rowH = 46;
		Rectangle card = new Rectangle(x, y, w, 56 + Math.Max(1, stops.Count) * rowH + 8);
		AisvTheme.DrawCard(b, card);
		Kit.SectionTitle(b, Tr.Get("where.route", "今天的路线"), x + 20, y + 18);
		int ry = y + 54;
		if (stops.Count == 0)
		{
			AisvTheme.DrawText(b, Tr.Get("where.noRoute", "今天没有固定的路线。"), new Vector2(x + 24, ry + 8), AisvTheme.TextFaint, 0.8f);
		}
		for (int i = 0; i < stops.Count; i++)
		{
			bool current = i == plan.CurrentStop, past = i < plan.CurrentStop;
			Color ink = current ? AisvTheme.AccentDark : past ? AisvTheme.TextFaint : AisvTheme.Text;
			if (current)
			{
				AisvTheme.FillRounded(b, new Rectangle(x + 8, ry, w - 16, rowH - 4), AisvTheme.SelectedRow, 8);
			}
			AisvTheme.DrawText(b, Game1.getTimeOfDayString(stops[i].Time), new Vector2(x + 24, ry + 8), ink, 0.8f);
			AisvTheme.DrawText(b, AisvTheme.Truncate(ShellText.Clean(stops[i].Name), w - 190, 0.82f), new Vector2(x + 150, ry + 8), ink, 0.82f);
			ry += rowH;
		}
		return card.Bottom;
	}

	private void DrawMap(SpriteBatch b, Rectangle body, Hits hits)
	{
		if (!_mapFailed && (_map == null || !string.Equals(_map.Region, _homeRegion, StringComparison.OrdinalIgnoreCase)) && _homeRegion.Length > 0)
		{
			try
			{
				_map = MapView.Load(_homeRegion);
				_mapFailed = _map == null;
			}
			catch (Exception ex)
			{
				_mapFailed = true;
				Ctx.Log.Warn($"大家都在哪：读不出地图：{ex.Message}");
			}
		}
		if (_map == null)
		{
			AisvTheme.TextCentered(b, Tr.Get("where.noMap", "这里看不了地图，用列表吧。"), body, AisvTheme.TextMuted);
			return;
		}
		Rectangle off = new Rectangle(body.X + 16, body.Bottom - 62, body.Width - 32, 54);
		_map.Fit(new Rectangle(body.X + 12, body.Y + 10, body.Width - 24, off.Y - body.Y - 18));
		_map.Draw(b);
		List<WherePlan> everyone = _groups.SelectMany(g => g.People).ToList();
		int size = Math.Clamp((int)Math.Round(_map.Scale * 11f), 30, 44);
		Dictionary<(int, int), int> used = new Dictionary<(int, int), int>();
		string hover = "";
		foreach (WherePlan plan in everyone)
		{
			if (plan.State == WhereState.Hidden || !_points.TryGetValue(plan.Who.Npc, out var at) || !string.Equals(at.Region, _map.Region, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			Vector2 p = _map.ToScreen(at.X, at.Y);
			(int, int) cell = ((int)(p.X / size), (int)(p.Y / size));
			used.TryGetValue(cell, out int nth);
			used[cell] = nth + 1;
			Rectangle face = new Rectangle((int)p.X - size / 2 + nth % 4 * (size * 2 / 3), (int)p.Y - size / 2 + nth / 4 * (size * 2 / 3), size, size);
			hover = DrawFace(b, plan, face, hits, _map.Bounds) ?? hover;
		}
		List<WherePlan> away = everyone.Where(p => p.State != WhereState.Hidden && (!_points.TryGetValue(p.Who.Npc, out var at) || !string.Equals(at.Region, _map.Region, StringComparison.OrdinalIgnoreCase))).ToList();
		string label = away.Count > 0 ? Tr.Get("where.offMap", "不在这张地图上：") : "";
		AisvTheme.DrawText(b, label, new Vector2(off.X, off.Y + 14), AisvTheme.TextMuted, 0.76f);
		int fx = off.X + (int)AisvTheme.Width(label, 0.76f) + 8;
		foreach (WherePlan plan in away)
		{
			if (fx + 44 > off.Right - 260)
			{
				break;
			}
			hover = DrawFace(b, plan, new Rectangle(fx, off.Y + 4, 44, 44), hits, null) ?? hover;
			fx += 48;
		}
		if (hover.Length > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate(hover, 250, 0.76f), new Vector2(off.Right - 250, off.Y + 14), AisvTheme.Text, 0.76f);
		}
	}

	/// <summary>地图上的一张脸，鼠标放上去时返回说明，点一下回列表看详情。</summary>
	private string? DrawFace(SpriteBatch b, WherePlan plan, Rectangle face, Hits hits, Rectangle? clip)
	{
		Kit.Avatar(b, face, Ctx, plan.Who.Npc);
		string npc = plan.Who.Npc;
		hits.Add(face, () =>
		{
			Select(npc);
			_mapMode = false;
		}, clip);
		return AisvTheme.Hover(face, clip) ? plan.Who.Name + " · " + ShellText.Clean(plan.Who.PlaceName) : null;
	}
}
