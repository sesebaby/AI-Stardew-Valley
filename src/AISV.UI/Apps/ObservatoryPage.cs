using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>天文台：画出今晚的天空（月相、星星、流星），旁边写今晚能不能看、去哪看、下一场流星雨在哪天。</summary>
internal sealed class ObservatoryPage : ShellPage
{
	private static readonly Color Sky = new Color(24, 30, 66);

	private static readonly Color SkyLow = new Color(52, 52, 102);

	private static readonly Color MoonLight = new Color(252, 244, 206);

	private NightSky? _sky;

	private int _next = -1;

	public ObservatoryPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("sky.title", "天文台");

	public override void Reload()
	{
		_sky = Ctx.Apps?.Tonight();
		_next = Ctx.Apps?.NextMeteor() ?? -1;
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_sky is not NightSky sky)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("sky.off", "天文台还没开。"), "");
			return;
		}
		bool narrow = area.Width < 900;
		int pad = narrow ? 16 : 28;
		Rectangle canvas = narrow
			? new Rectangle(area.X + pad, area.Y + 16, area.Width - pad * 2, Math.Min(320, area.Height / 2))
			: new Rectangle(area.X + pad, area.Y + 20, (area.Width - pad * 3) * 3 / 5, area.Height - 40);
		DrawSky(b, canvas, sky);
		Rectangle side = narrow
			? new Rectangle(area.X + pad, canvas.Bottom + 16, area.Width - pad * 2, area.Bottom - canvas.Bottom - 32)
			: new Rectangle(canvas.Right + pad, area.Y + 20, area.Right - pad - canvas.Right - pad, area.Height - 40);
		Info(b, side, sky, hits);
	}

	private static void DrawSky(SpriteBatch b, Rectangle r, NightSky sky)
	{
		AisvTheme.DrawShadow(b, r, 16, 5);
		AisvTheme.FillRounded(b, r, Sky, 16);
		for (int i = 0; i < 6; i++)
		{
			int h = r.Height / 3 / 6;
			AisvTheme.Fill(b, new Rectangle(r.X + 8, r.Bottom - r.Height / 3 + i * h, r.Width - 16, h), Color.Lerp(Sky, SkyLow, i / 6f) * 0.9f);
		}
		ulong seed = Game1.uniqueIDForThisGame;
		int day = Game1.Date.TotalDays;
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		int stars = sky.Clear ? sky.Stars : 6;
		for (int i = 0; i < stars; i++)
		{
			int sx = r.X + 14 + Horoscope.Roll(seed, day, "star", i * 3, r.Width - 28);
			int sy = r.Y + 14 + Horoscope.Roll(seed, day, "star", i * 3 + 1, r.Height * 4 / 5);
			int size = 1 + Horoscope.Roll(seed, day, "star", i * 3 + 2, 3);
			float twinkle = 0.55f + 0.45f * (float)Math.Abs(Math.Sin(t * 1.3 + i));
			AisvTheme.Fill(b, new Rectangle(sx, sy, size * 2, size * 2), Color.White * (sky.Clear ? twinkle : 0.15f));
		}
		int d = Math.Min(110, r.Width / 5);
		Rectangle moon = new Rectangle(r.Right - d - 46, r.Y + 40, d, d);
		if (sky.Phase != 0)
		{
			AisvTheme.FillRounded(b, AisvTheme.Inflate(moon, 10), MoonLight * 0.12f, d / 2 + 10);
			AisvTheme.FillRounded(b, moon, MoonLight, d / 2);
			float light = Observatory.Light(sky.Phase);
			int shift = (int)(light * d * (sky.Phase < 4 ? -1 : 1));
			if (sky.Phase != 4)
			{
				AisvTheme.FillRounded(b, new Rectangle(moon.X + shift, moon.Y, d, d), Sky, d / 2);
			}
		}
		else
		{
			AisvTheme.Bordered(b, moon, Sky, MoonLight * 0.25f, d / 2);
		}
		if (sky.Meteors)
		{
			for (int i = 0; i < 4; i++)
			{
				double phase = (t * 0.45 + i * 0.27) % 1.0;
				if (phase > 0.35)
				{
					continue;
				}
				float p = (float)(phase / 0.35);
				int sx = r.X + 60 + Horoscope.Roll(seed, day + (int)(t * 0.45 + i * 0.27), "meteor", i, r.Width - 200);
				int sy = r.Y + 30 + Horoscope.Roll(seed, day + (int)(t * 0.45 + i * 0.27), "meteorY", i, r.Height / 3);
				Vector2 head = new Vector2(sx + p * 160, sy + p * 90);
				AisvTheme.Line(b, head - new Vector2(70, 40), head, Color.White * (1 - p) * 0.8f, 2f);
				AisvTheme.Fill(b, new Rectangle((int)head.X - 2, (int)head.Y - 2, 4, 4), Color.White * (1 - p));
			}
		}
		if (!sky.Clear)
		{
			for (int i = 0; i < 5; i++)
			{
				Rectangle cloud = new Rectangle(r.X + 20 + i * (r.Width - 40) / 5, r.Y + 50 + (i % 2) * 60, (r.Width - 40) / 3, 70);
				AisvTheme.FillRounded(b, cloud, new Color(90, 96, 130) * 0.85f, 35);
			}
		}
		AisvTheme.FillRounded(b, new Rectangle(r.X, r.Bottom - 46, r.Width, 46), new Color(28, 40, 34), 16);
		AisvTheme.Fill(b, new Rectangle(r.X, r.Bottom - 46, r.Width, 20), new Color(28, 40, 34));
	}

	private void Info(SpriteBatch b, Rectangle r, NightSky sky, Hits hits)
	{
		AisvTheme.DrawCard(b, r);
		int x = r.X + 24, w = r.Width - 48;
		int y = r.Y + 22;
		AisvTheme.Bold(b, Tr.Get("sky.tonight", "今晚"), new Vector2(x, y), AisvTheme.TextMuted, 0.78f);
		y += 34;
		foreach (string line in AisvTheme.WrapLines(sky.Summary, w, 1f).Take(3))
		{
			AisvTheme.Bold(b, line, new Vector2(x, y), sky.Meteors ? AisvTheme.Accent : AisvTheme.Text, 1f);
			y += AisvTheme.LineHeight(1f);
		}
		y += 14;
		y = Row(b, Tr.Get("sky.moon", "月相"), sky.Moon, x, y, w);
		y = Row(b, Tr.Get("sky.stars", "能看到的星星"), sky.Clear ? Tr.Get("sky.starsN", "大约 {{n}} 颗", ("n", sky.Stars)) : Tr.Get("sky.starsNone", "看不到"), x, y, w);
		string next = sky.Meteors ? Tr.Get("sky.nextTonight", "就在今晚") : _next > 0 ? Tr.Get("sky.nextDay", "这一季的 {{day}} 日", ("day", _next)) : Tr.Get("sky.nextNone", "这一季没有了");
		y = Row(b, Tr.Get("sky.next", "下一场流星雨"), next, x, y, w);
		if (sky.Spots.Count > 0)
		{
			y += 10;
			AisvTheme.Bold(b, Tr.Get("sky.spots", "适合看星星的地方"), new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
			y += 32;
			int cx = x;
			foreach (string spot in sky.Spots)
			{
				int cw = AisvTheme.ChipWidth(spot, 0.7f);
				if (cx + cw > x + w)
				{
					cx = x;
					y += 34;
				}
				AisvTheme.Chip(b, new Vector2(cx, y), spot, AisvTheme.AccentSoft, AisvTheme.AccentDark, 0.7f);
				cx += cw + 8;
			}
			y += 46;
		}
		if (sky.Clear && Ctx.Outings != null && y + 52 < r.Bottom)
		{
			string label = sky.Meteors ? Tr.Get("sky.invite", "约人一起看流星") : Tr.Get("sky.inviteStars", "约人一起看星星");
			Rectangle btn = new Rectangle(x, Math.Min(y + 6, r.Bottom - 64), Math.Min(w, (int)AisvTheme.Width(label, 0.85f) + 60), 48);
			AisvTheme.Button(b, btn, label, primary: true, hover: AisvTheme.Hover(btn));
			hits.Add(btn, () => Host.Open(ShellMenu.Tab.Social, ShellMenu.SocialTab.Outings));
		}
	}

	private static int Row(SpriteBatch b, string label, string value, int x, int y, int w)
	{
		AisvTheme.DrawText(b, label, new Vector2(x, y), AisvTheme.TextMuted, 0.76f);
		float vw = AisvTheme.Width(value, 0.8f);
		AisvTheme.Bold(b, value, new Vector2(x + w - vw, y), AisvTheme.Text, 0.8f);
		AisvTheme.HLine(b, x, y + 36, w, AisvTheme.Divider, 1);
		return y + 46;
	}
}
