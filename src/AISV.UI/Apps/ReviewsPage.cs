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

namespace AISV.UI.Apps;

/// <summary>评价：左边总评（平均星数、各星级多少条，点一行只看这一档），右边每位村民写给农夫的一条评价，点开看四项声望。</summary>
internal sealed class ReviewsPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private readonly HashSet<string> _open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private List<ReviewEntry> _all = new List<ReviewEntry>();

	private int _filter;

	private bool _loaded;

	private Rectangle _view;

	public ReviewsPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("review.title", "评价");

	public override void Reload()
	{
		if (!_loaded)
		{
			_loaded = true;
			_all = AppData.Reviews(Ctx);
		}
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_all.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("review.empty", "还没有人写评价"), Tr.Get("review.emptyHint", "先去和镇上的人打打招呼吧。"));
			return;
		}
		Rectangle side = new Rectangle(area.X + 16, area.Y + 16, 340, area.Height - 32);
		Summary(b, side, hits);
		_view = new Rectangle(side.Right + 20, area.Y + 8, area.Right - 8 - side.Right - 20, area.Height - 16);
		Cards(b, hits);
	}

	private void Summary(SpriteBatch b, Rectangle r, Hits hits)
	{
		AisvTheme.DrawCard(b, r);
		int x = r.X + 26, w = r.Width - 52;
		int y = r.Y + 24;
		string farmer = ShellText.Clean(Game1.player?.Name ?? "");
		foreach (string line in AisvTheme.WrapLines(Tr.Get("review.sub", "山谷怎么看{{farmer}}", ("farmer", farmer)), w, 0.82f))
		{
			AisvTheme.Bold(b, line, new Vector2(x, y), AisvTheme.Text, 0.82f);
			y += AisvTheme.LineHeight(0.82f);
		}
		y += 14;
		float avg = FarmerReviews.Average(_all.Select(e => e.Review.Stars));
		string big = avg.ToString("0.0");
		b.DrawString(Game1.dialogueFont, big, new Vector2(x, y), AisvTheme.Text, 0f, Vector2.Zero, 1.6f, SpriteEffects.None, 1f);
		int bw = (int)(Game1.dialogueFont.MeasureString(big).X * 1.6f);
		AisvTheme.DrawText(b, "/ 5", new Vector2(x + bw + 10, y + 42), AisvTheme.TextFaint, 0.8f);
		y += 92;
		Stars(b, x, y, FarmerReviews.StarFill(avg), 2);
		y += 44;
		AisvTheme.DrawText(b, Tr.Get("review.count", "收到 {{count}} 句评语", ("count", _all.Count)), new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
		y += 46;
		int[] dist = FarmerReviews.Distribution(_all.Select(e => e.Review.Stars));
		int max = Math.Max(1, dist.Max());
		for (int s = 5; s >= 1; s--)
		{
			Rectangle row = new Rectangle(x - 10, y - 6, w + 20, 40);
			bool on = _filter == s;
			if (on)
			{
				AisvTheme.FillRounded(b, row, AisvTheme.GoldSoft, 10);
			}
			else if (AisvTheme.Hover(row))
			{
				AisvTheme.FillRounded(b, row, AisvTheme.PageDeep, 10);
			}
			AisvTheme.DrawText(b, Tr.Get("review.starN", "{{n}} 星", ("n", s)), new Vector2(x, y), AisvTheme.Text, 0.72f);
			Rectangle track = new Rectangle(x + 60, y + 9, w - 60 - 40, 12);
			AisvTheme.FillRounded(b, track, AisvTheme.Divider, 6);
			if (dist[s - 1] > 0)
			{
				AisvTheme.FillRounded(b, new Rectangle(track.X, track.Y, Math.Max(12, track.Width * dist[s - 1] / max), track.Height), AisvTheme.Gold, 6);
			}
			string n = dist[s - 1].ToString();
			AisvTheme.DrawText(b, n, new Vector2(x + w - AisvTheme.Width(n, 0.7f), y), AisvTheme.TextMuted, 0.7f);
			int k = s;
			hits.Add(row, () =>
			{
				Game1.playSound("smallSelect");
				_filter = _filter == k ? 0 : k;
				_scroll.Reset();
			});
			y += 44;
		}
		if (_filter != 0)
		{
			y += 6;
			string all = Tr.Get("review.showAll", "看全部");
			Rectangle btn = new Rectangle(x, y, (int)AisvTheme.Width(all, 0.8f) + 40, 42);
			AisvTheme.Button(b, btn, all, hover: AisvTheme.Hover(btn), scale: 0.8f);
			hits.Add(btn, () =>
			{
				_filter = 0;
				_scroll.Reset();
			});
		}
	}

	private static void Stars(SpriteBatch b, int x, int y, int[] fill, int scale)
	{
		int step = 16 * scale + 4 * scale;
		for (int i = 0; i < 5; i++)
		{
			AppIcons.StarEmpty.Draw(b, x + i * step, y, scale);
			if (fill[i] > 0)
			{
				AppIcons.Reviews.Draw(b, x + i * step, y, scale, columns: fill[i] == 2 ? 16 : 8);
			}
		}
	}

	private void Cards(SpriteBatch b, Hits hits)
	{
		AisvTheme.BeginClip(b, _view);
		int x = _view.X + 8, w = _view.Width - 32;
		int y = _view.Y + 8 - _scroll.Offset;
		foreach (ReviewEntry e in _all.Where(e => _filter == 0 || e.Review.Stars == _filter))
		{
			y = Card(b, e, x, y, w, hits) + 14;
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _view, y, 8);
	}

	private int Card(SpriteBatch b, ReviewEntry e, int x, int y, int w, Hits hits)
	{
		Review rv = e.Review;
		bool open = _open.Contains(e.Npc);
		int tx = x + 24 + 64 + 18, tw = x + w - 24 - tx;
		List<string> body = rv.Body.Select(Line).SelectMany(t => AisvTheme.WrapLines(t, tw, 0.76f)).ToList();
		string? memory = rv.Memory == null ? null : Line(rv.Memory);
		List<string> memLines = memory == null ? new List<string>() : AisvTheme.WrapLines(memory, tw - 36, 0.7f);
		int h = 24 + 34 + 36 + body.Count * AisvTheme.LineHeight(0.76f) + (memLines.Count > 0 ? 16 + memLines.Count * AisvTheme.LineHeight(0.7f) + 20 : 0) + 18 + 30 + (open ? 150 : 0) + 16;
		Rectangle r = new Rectangle(x, y, w, h);
		if (r.Bottom < _view.Y || r.Y > _view.Bottom)
		{
			return r.Bottom;
		}
		bool hover = AisvTheme.Hover(r, _view);
		AisvTheme.Bordered(b, r, AisvTheme.Card, hover ? AisvTheme.Gold : AisvTheme.CardBorder, 14);
		Kit.Avatar(b, new Rectangle(x + 24, y + 24, 64, 64), Ctx, e.Npc);
		AisvTheme.Bold(b, e.Name, new Vector2(tx, y + 22), AisvTheme.Text, 0.88f);
		int[] fill = new int[5];
		for (int i = 0; i < 5; i++)
		{
			fill[i] = i < rv.Stars ? 2 : 0;
		}
		Stars(b, (int)(tx + AisvTheme.Width(e.Name, 0.88f) + 16), y + 26, fill, 1);
		int ly = y + 58;
		AisvTheme.Bold(b, AisvTheme.Truncate(Line(rv.Title), tw, 0.84f), new Vector2(tx, ly), AisvTheme.Text, 0.84f);
		ly += 36;
		foreach (string line in body)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ly), AisvTheme.TextMuted, 0.76f);
			ly += AisvTheme.LineHeight(0.76f);
		}
		if (memLines.Count > 0)
		{
			ly += 16;
			Rectangle q = new Rectangle(tx, ly - 8, tw, memLines.Count * AisvTheme.LineHeight(0.7f) + 16);
			AisvTheme.FillRounded(b, q, AisvTheme.PageDeep, 10);
			AisvTheme.FillRounded(b, new Rectangle(q.X, q.Y, 4, q.Height), AisvTheme.Gold, 2);
			foreach (string line in memLines)
			{
				AisvTheme.DrawText(b, line, new Vector2(q.X + 20, ly), AisvTheme.Text, 0.7f);
				ly += AisvTheme.LineHeight(0.7f);
			}
			ly += 20;
		}
		ly += 18;
		AisvTheme.HLine(b, tx, ly - 8, tw, AisvTheme.Divider);
		string verified = e.Input.IsStranger ? Tr.Get("review.unverified", "还没打过交道") : Tr.Get("review.verified", "认证邻居 · 聊了 {{talks}} 次 · 送了 {{gifts}} 份礼", ("talks", e.Input.Talks), ("gifts", e.Input.Gifts));
		AisvTheme.DrawText(b, verified, new Vector2(tx, ly), e.Input.IsStranger ? AisvTheme.TextFaint : AisvTheme.AccentDark, 0.64f);
		string helpful = rv.Helpful == 0 ? Tr.Get("review.helpful.none", "还没人觉得有用") : Tr.Get("review.helpful", "{{count}} 位村民点头", ("count", rv.Helpful));
		string more = open ? Tr.Get("review.less", "收起") : Tr.Get("review.more", "看声望");
		float mw = AisvTheme.Width(more, 0.64f);
		AisvTheme.DrawText(b, more, new Vector2(tx + tw - mw, ly), AisvTheme.Accent, 0.64f);
		AisvTheme.DrawText(b, helpful, new Vector2(tx + tw - mw - 24 - AisvTheme.Width(helpful, 0.64f), ly), AisvTheme.TextFaint, 0.64f);
		if (open)
		{
			Axes(b, rv.Axes, e.Name, tx, ly + 36, tw);
		}
		string npc = e.Npc;
		hits.Add(r, () =>
		{
			Game1.playSound("smallSelect");
			if (!_open.Remove(npc))
			{
				_open.Add(npc);
			}
		}, _view);
		return r.Bottom;
	}

	private static void Axes(SpriteBatch b, ReviewAxes a, string name, int x, int y, int w)
	{
		AisvTheme.Bold(b, Tr.Get("review.axes", "在{{name}}眼里", ("name", name)), new Vector2(x, y), AisvTheme.Text, 0.72f);
		y += 32;
		(string Label, float? Value, Color Tint)[] bars =
		{
			(Tr.Get("review.axis.generosity", "大方"), a.Generosity, new Color(214, 104, 140)),
			(Tr.Get("review.axis.reliability", "靠得住"), a.Reliability, new Color(54, 150, 140)),
			(Tr.Get("review.axis.charisma", "讨人喜欢"), a.Charisma, new Color(84, 140, 214)),
			(Tr.Get("review.axis.mystery", "神秘"), a.Mystery, new Color(146, 100, 200))
		};
		int colW = (w - 20) / 2;
		for (int i = 0; i < bars.Length; i++)
		{
			int bx = x + i % 2 * (colW + 20), by = y + i / 2 * 50;
			AisvTheme.DrawText(b, bars[i].Label, new Vector2(bx, by), AisvTheme.TextMuted, 0.66f);
			Rectangle track = new Rectangle(bx, by + 26, colW, 10);
			AisvTheme.FillRounded(b, track, AisvTheme.Divider, 5);
			if (bars[i].Value is float v)
			{
				AisvTheme.FillRounded(b, new Rectangle(track.X, track.Y, Math.Max(10, (int)(track.Width * v / 100f)), track.Height), bars[i].Tint, 5);
			}
			else
			{
				string none = Tr.Get("review.axis.none", "没数据");
				AisvTheme.DrawText(b, none, new Vector2(bx + colW - AisvTheme.Width(none, 0.6f), by), AisvTheme.TextFaint, 0.6f);
			}
		}
	}

	/// <summary>评价里一句话的文字（值填进 {{…}}）。</summary>
	private static string Line(ReviewLine line)
	{
		string text = Template(line.Key);
		foreach ((string name, string value) in line.Values)
		{
			text = text.Replace("{{" + name + "}}", value);
		}
		return text;
	}

	private static string Template(string key) => key switch
	{
		"stranger.title" => Tr.Get("review.stranger.title", "好像没见过？"),
		"stranger.body" => Tr.Get("review.stranger.body", "只远远见过一回，扛着锄头经过。先给三星，暂时挑不出毛病。"),
		"title.5a" => Tr.Get("review.title.5a", "山谷第一好农夫！"),
		"title.5b" => Tr.Get("review.title.5b", "五星不够，想给十星"),
		"title.4a" => Tr.Get("review.title.4a", "挺好，差一点就完美"),
		"title.4b" => Tr.Get("review.title.4b", "靠得住的邻居"),
		"title.3a" => Tr.Get("review.title.3a", "还凑合"),
		"title.3b" => Tr.Get("review.title.3b", "中规中矩的农夫"),
		"title.2a" => Tr.Get("review.title.2a", "还有很大进步空间"),
		"title.2b" => Tr.Get("review.title.2b", "不太行"),
		"title.1a" => Tr.Get("review.title.1a", "见了绕道走"),
		"title.1b" => Tr.Get("review.title.1b", "能打零星就好了"),
		"open.5" => Tr.Get("review.open.5", "全镇我都会推荐。"),
		"open.4" => Tr.Get("review.open.4", "常来看我，人也好，就一点小意见。"),
		"open.3" => Tr.Get("review.open.3", "没什么特别的，也不算差。"),
		"open.2" => Tr.Get("review.open.2", "原本以为这位农夫会更好些。"),
		"open.1" => Tr.Get("review.open.1", "写这条，是为了让后来的人少受点罪。"),
		"rel.married" => Tr.Get("review.rel.married", "我们结婚了，照规矩我得在这儿说好话。"),
		"rel.engaged" => Tr.Get("review.rel.engaged", "我们订婚了！退货期还没过呢。"),
		"rel.dating" => Tr.Get("review.rel.dating", "我们在交往，难免有点偏心。"),
		"rel.divorced" => Tr.Get("review.rel.divorced", "我们离过婚。别问了。"),
		"promise.broken" => Tr.Get("review.promise.broken", "答应我的事没做到，老毛病了。"),
		"talk.hurt" => Tr.Get("review.talk.hurt", "前阵子说了句挺伤人的话。"),
		"gift.hated" => Tr.Get("review.gift.hated", "送过我{{item}}。我实在想不通。"),
		"gift.many" => Tr.Get("review.gift.many", "送的礼多到架子都摆不下了。"),
		"gift.loved" => Tr.Get("review.gift.loved", "送过我{{item}}，太懂我了。"),
		"gift.none" => Tr.Get("review.gift.none", "连一朵野花都没送过我。"),
		"gift.birthday" => Tr.Get("review.gift.birthday", "记得我的生日，比好些人强。"),
		"talk.great" => Tr.Get("review.talk.great", "和这人说上几句，一整天都轻快。"),
		"talk.awkward" => Tr.Get("review.talk.awkward", "我们聊天嘛……不太好形容。"),
		"talk.often" => Tr.Get("review.talk.often", "我们聊了 {{talks}} 次，对方的事我差不多都知道了。"),
		"talk.rare" => Tr.Get("review.talk.rare", "话少得可怜，怕是记不住我的名字。"),
		"trust.high" => Tr.Get("review.trust.high", "家里钥匙交给对方我都放心。"),
		"trust.low" => Tr.Get("review.trust.low", "每回来过，我都得清点一下东西。"),
		"memory" => Tr.Get("review.memory", "还记得：“{{memory}}”"),
		_ => key
	};
}
