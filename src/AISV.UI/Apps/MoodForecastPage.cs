using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Social;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Apps;

internal static class MoodText
{
	public static string SkyName(MoodSky s) => s switch
	{
		MoodSky.Rainbow => Tr.Get("mood.sky.rainbow", "彩虹"),
		MoodSky.Sunny => Tr.Get("mood.sky.sunny", "晴"),
		MoodSky.PartlyCloudy => Tr.Get("mood.sky.partly", "晴转多云"),
		MoodSky.Foggy => Tr.Get("mood.sky.foggy", "起雾"),
		MoodSky.Rainy => Tr.Get("mood.sky.rainy", "下雨"),
		MoodSky.Stormy => Tr.Get("mood.sky.stormy", "雷阵雨"),
		_ => Tr.Get("mood.sky.cloudy", "多云")
	};

	public static string SkyLine(MoodSky s) => s switch
	{
		MoodSky.Rainbow => Tr.Get("mood.line.rainbow", "好得不能再好，今天说什么都顺耳。"),
		MoodSky.Sunny => Tr.Get("mood.line.sunny", "心里亮堂堂的，正适合去说说话。"),
		MoodSky.PartlyCloudy => Tr.Get("mood.line.partly", "大体不错，偶尔飘过一点小心事。"),
		MoodSky.Foggy => Tr.Get("mood.line.foggy", "心里有点乱，说话慢一点。"),
		MoodSky.Rainy => Tr.Get("mood.line.rainy", "情绪低落，需要一点安慰。"),
		MoodSky.Stormy => Tr.Get("mood.line.stormy", "火气不小，最好别去招惹。"),
		_ => Tr.Get("mood.line.cloudy", "不好不坏，平平淡淡的一天。")
	};

	public static string VillageLine(MoodSky s) => s switch
	{
		MoodSky.Sunny or MoodSky.Rainbow => Tr.Get("mood.village.sunny", "镇上一片晴朗，大家脸上都带着笑。"),
		MoodSky.PartlyCloudy => Tr.Get("mood.village.partly", "镇上气氛不错，只有零星几朵云。"),
		MoodSky.Foggy => Tr.Get("mood.village.foggy", "镇上有点闷，大家都心不在焉。"),
		MoodSky.Rainy => Tr.Get("mood.village.rainy", "镇上灰蒙蒙的，好几个人提不起精神。"),
		MoodSky.Stormy => Tr.Get("mood.village.stormy", "镇上火药味有点重，说话当心。"),
		_ => Tr.Get("mood.village.cloudy", "镇上平平静静，和往常差不多。")
	};

	public static string Advice(MoodAdvice a) => a switch
	{
		MoodAdvice.BirthdayGift => Tr.Get("mood.tip.birthday", "今天过生日！这时送礼效果是平常的八倍，挑一件最爱的东西吧。"),
		MoodAdvice.GoodDayToTalk => Tr.Get("mood.tip.good", "心情好的时候，聊几句、送点小东西都特别加分。"),
		MoodAdvice.ChatOrGift => Tr.Get("mood.tip.chat", "没什么起伏，打个招呼或者送件小东西，就能让对方开心一点。"),
		MoodAdvice.GentleTalk => Tr.Get("mood.tip.gentle", "说话温和些，别催，也别开过头的玩笑。"),
		MoodAdvice.LovedGift => Tr.Get("mood.tip.loved", "送一件对方最喜欢的东西，比说什么都管用。"),
		MoodAdvice.TreadLightly => Tr.Get("mood.tip.light", "今天少惹为妙；真要去，话说得软一点。"),
		MoodAdvice.WeatherWillPass => Tr.Get("mood.tip.weather", "让对方不舒服的是天气，天一变，心情也就好了。"),
		_ => Tr.Get("mood.tip.tomorrow", "是这个日子让对方提不起劲，明天就好了。")
	};

	/// <summary>原因前面的小圆点颜色。</summary>
	public static Color CauseColor(MoodCause c) => c switch
	{
		MoodCause.Birthday => AisvTheme.Pink,
		MoodCause.LikesWeather or MoodCause.LovesSeason or MoodCause.LikesDay or MoodCause.GoodEvent => AisvTheme.Accent,
		MoodCause.DislikesWeather or MoodCause.DislikesSeason or MoodCause.DislikesDay or MoodCause.BadEvent => AisvTheme.Warn,
		_ => AisvTheme.TextFaint
	};

	/// <summary>天气的底色（横幅和卡片角上用）。</summary>
	public static (Color Top, Color Bottom) SkyColors(MoodSky s) => s switch
	{
		MoodSky.Rainbow => (new Color(255, 236, 214), new Color(232, 240, 255)),
		MoodSky.Sunny => (new Color(255, 240, 196), new Color(220, 238, 255)),
		MoodSky.PartlyCloudy => (new Color(246, 240, 214), new Color(222, 234, 248)),
		MoodSky.Foggy => (new Color(232, 232, 228), new Color(222, 224, 226)),
		MoodSky.Rainy => (new Color(214, 224, 238), new Color(200, 212, 230)),
		MoodSky.Stormy => (new Color(206, 206, 220), new Color(184, 186, 204)),
		_ => (new Color(236, 240, 244), new Color(222, 230, 238))
	};

	/// <summary>分数条颜色：低是灰蓝，高是金色。</summary>
	public static Color ScoreColor(int score)
	{
		float t = MoodForecast.Fraction(score);
		return t < 0.5f ? Color.Lerp(new Color(120, 140, 186), new Color(196, 186, 150), t * 2f) : Color.Lerp(new Color(196, 186, 150), AisvTheme.Gold, (t - 0.5f) * 2f);
	}
}

/// <summary>心情预报：上面全村的天气，左边每位村民一张天气卡，右边选中的人为什么是这个心情、该怎么相处。</summary>
internal sealed class MoodForecastPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private readonly Scroller _detailScroll = new Scroller();

	private Rectangle _detail;

	private List<MoodEntry> _entries = new List<MoodEntry>();

	private string? _selected;

	private Rectangle _grid;

	public MoodForecastPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("mood.title", "心情预报");

	public override void Reload()
	{
		_entries = AppData.Moods(Ctx);
		if (_selected == null || _entries.All(e => !Same(e.Npc, _selected)))
		{
			_selected = _entries.FirstOrDefault()?.Npc;
		}
	}

	public override void Scroll(int direction)
	{
		if (AisvTheme.Hover(_detail))
		{
			_detailScroll.Wheel(direction);
		}
		else
		{
			_scroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key)
	{
		int i = _entries.FindIndex(e => Same(e.Npc, _selected));
		int delta = key switch { Keys.Left => -1, Keys.Right => 1, _ => 0 };
		if (delta != 0 && _entries.Count > 0)
		{
			Select(_entries[Math.Clamp(i + delta, 0, _entries.Count - 1)].Npc);
			return true;
		}
		return _scroll.PageKeys(key, _grid.Height - 80);
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		if (_entries.Count == 0)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("mood.empty", "还没有今天的预报"), Tr.Get("mood.emptyHint", "每天早上村民醒来时算心情，过一会儿再来看看。"));
			return;
		}
		Rectangle banner = new Rectangle(area.X + 20, area.Y + 18, area.Width - 40, 140);
		Banner(b, banner);
		int top = banner.Bottom + 20;
		int detailW = Math.Clamp(area.Width * 2 / 5, 380, 560);
		Rectangle detail = _detail = new Rectangle(area.Right - 20 - detailW, top, detailW, area.Bottom - 18 - top);
		_grid = new Rectangle(area.X + 20, top, detail.X - 20 - (area.X + 20), area.Bottom - 10 - top);
		Grid(b, hits);
		MoodEntry? sel = _entries.FirstOrDefault(e => Same(e.Npc, _selected));
		if (sel != null)
		{
			Detail(b, detail, sel);
		}
	}

	private void Banner(SpriteBatch b, Rectangle r)
	{
		MoodSky village = MoodForecast.Village(_entries.Select(e => e.Mood.Level));
		(Color topColor, Color bottomColor) = MoodText.SkyColors(village);
		AisvTheme.FillRounded(b, r, bottomColor, 16);
		AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y, r.Width, r.Height / 2 + 8), topColor, 16);
		AisvTheme.Fill(b, new Rectangle(r.X, r.Y + r.Height / 2 - 8, r.Width, 16), Color.Lerp(topColor, bottomColor, 0.5f));
		AppIcons.Sky(village).Draw(b, r.X + 28, r.Center.Y - 48, 6);
		int tx = r.X + 28 + 96 + 28;
		AisvTheme.DrawText(b, Tr.Get("mood.villageLabel", "全村今天"), new Vector2(tx, r.Y + 22), AisvTheme.TextMuted, 0.78f);
		AisvTheme.Bold(b, MoodText.SkyName(village), new Vector2(tx, r.Y + 48), AisvTheme.Text, 1.5f);
		AisvTheme.DrawText(b, MoodText.VillageLine(village), new Vector2(tx, r.Y + 104), AisvTheme.TextMuted, 0.78f);
		// 右边：每种天气几个人。
		int x = r.Right - 24;
		foreach (IGrouping<MoodSky, MoodEntry> g in _entries.GroupBy(e => e.Sky).OrderByDescending(g => g.Key))
		{
			string count = g.Count().ToString();
			int pw = 40 + 8 + (int)AisvTheme.Width(count, 0.85f) + 16;
			Rectangle pill = new Rectangle(x - pw, r.Center.Y - 26, pw, 52);
			if (pill.X < tx + 420)
			{
				break;
			}
			AisvTheme.FillRounded(b, pill, Color.White * 0.6f, 26);
			AppIcons.Sky(g.Key).Draw(b, pill.X + 10, pill.Center.Y - 16, 2);
			AisvTheme.Bold(b, count, new Vector2(pill.X + 50, pill.Center.Y - 14), AisvTheme.Text, 0.85f);
			x = pill.X - 10;
		}
	}

	private void Grid(SpriteBatch b, Hits hits)
	{
		const int gap = 14, h = 118;
		int cols = Math.Max(1, (_grid.Width - 12 + gap) / (250 + gap));
		int cw = (_grid.Width - 12 - gap * (cols - 1)) / cols;
		AisvTheme.BeginClip(b, _grid);
		int y0 = _grid.Y - _scroll.Offset;
		for (int i = 0; i < _entries.Count; i++)
		{
			Rectangle r = new Rectangle(_grid.X + i % cols * (cw + gap), y0 + i / cols * (h + gap), cw, h);
			if (r.Bottom < _grid.Y || r.Y > _grid.Bottom)
			{
				continue;
			}
			MoodEntry e = _entries[i];
			Card(b, r, e, Same(e.Npc, _selected), AisvTheme.Hover(r, _grid));
			hits.Add(r, () => Select(e.Npc), _grid);
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, _grid, y0 + (_entries.Count + cols - 1) / cols * (h + gap), 4);
	}

	private void Card(SpriteBatch b, Rectangle r, MoodEntry e, bool selected, bool hover)
	{
		Color border = selected ? AisvTheme.Accent : e.Birthday ? AisvTheme.Pink : hover ? AisvTheme.TextFaint : AisvTheme.CardBorder;
		AisvTheme.Bordered(b, r, selected ? AisvTheme.SelectedRow : AisvTheme.Card, border, 12, selected ? 3 : 2);
		(Color skyTop, _) = MoodText.SkyColors(e.Sky);
		Rectangle corner = new Rectangle(r.Right - 66, r.Y + 10, 56, 56);
		AisvTheme.FillRounded(b, corner, skyTop, 14);
		AppIcons.Sky(e.Sky).Draw(b, corner.X + 4, corner.Y + 4, 3);
		Rectangle avatar = new Rectangle(r.X + 14, r.Y + 14, 56, 56);
		Kit.Avatar(b, avatar, Ctx, e.Npc);
		int tx = avatar.Right + 12;
		string name = AisvTheme.Truncate(e.Name, corner.X - tx - 6, 0.85f);
		AisvTheme.Bold(b, name, new Vector2(tx, r.Y + 16), AisvTheme.Text, 0.85f);
		string word = e.Birthday ? Tr.Get("mood.birthday", "过生日") : MoodBook.MoodWord(e.Mood.Temper);
		AisvTheme.DrawText(b, word + " · " + MoodText.SkyName(e.Sky), new Vector2(tx, r.Y + 46), e.Birthday ? AisvTheme.Pink : AisvTheme.TextMuted, 0.7f);
		ScoreBar(b, new Rectangle(r.X + 14, r.Bottom - 30, r.Width - 28, 12), e.Mood.Level);
	}

	private static void ScoreBar(SpriteBatch b, Rectangle r, int score)
	{
		AisvTheme.FillRounded(b, r, AisvTheme.Divider, r.Height / 2);
		int fw = Math.Max(r.Height, (int)(r.Width * MoodForecast.Fraction(score)));
		AisvTheme.FillRounded(b, new Rectangle(r.X, r.Y, fw, r.Height), MoodText.ScoreColor(score), r.Height / 2);
		AisvTheme.Fill(b, new Rectangle(r.Center.X - 1, r.Y - 3, 2, r.Height + 6), AisvTheme.TextFaint * 0.5f);
	}

	private void Detail(SpriteBatch b, Rectangle r, MoodEntry e)
	{
		AisvTheme.DrawCard(b, r);
		(Color skyTop, _) = MoodText.SkyColors(e.Sky);
		Rectangle head = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 124);
		AisvTheme.FillRounded(b, head, skyTop, 12);
		AisvTheme.Fill(b, new Rectangle(head.X, head.Bottom - 14, head.Width, 14), skyTop);
		AppIcons.Sky(e.Sky).Draw(b, head.Right - 24 - 80, head.Y + 26, 5);
		Rectangle avatar = new Rectangle(r.X + 24, r.Y + 26, 80, 80);
		Kit.Avatar(b, avatar, Ctx, e.Npc);
		int tx = avatar.Right + 18;
		AisvTheme.Bold(b, AisvTheme.Truncate(e.Name, head.Right - 120 - tx, 1.1f), new Vector2(tx, r.Y + 30), AisvTheme.Text, 1.1f);
		AisvTheme.Bold(b, MoodText.SkyName(e.Sky), new Vector2(tx, r.Y + 72), AisvTheme.AccentDark, 0.85f);
		AisvTheme.DrawText(b, MoodBook.MoodWord(e.Mood.Temper), new Vector2(tx + AisvTheme.Width(MoodText.SkyName(e.Sky), 0.85f) + 14, r.Y + 74), AisvTheme.TextMuted, 0.75f);
		int x = r.X + 24, w = r.Width - 48;
		Rectangle content = new Rectangle(r.X, head.Bottom, r.Width, r.Bottom - head.Bottom - 4);
		int y = head.Bottom + 16 - _detailScroll.Offset;
		AisvTheme.BeginClip(b, content);
		foreach (string line in AisvTheme.WrapLines(MoodText.SkyLine(e.Sky), w, 0.8f))
		{
			AisvTheme.DrawText(b, line, new Vector2(x, y), AisvTheme.Text, 0.8f);
			y += AisvTheme.LineHeight(0.8f);
		}
		y += 14;
		ScoreBar(b, new Rectangle(x, y, w, 14), e.Mood.Level);
		y += 22;
		AisvTheme.DrawText(b, Tr.Get("mood.low", "闷闷不乐"), new Vector2(x, y), AisvTheme.TextFaint, 0.62f);
		string high = Tr.Get("mood.high", "神采飞扬");
		AisvTheme.DrawText(b, high, new Vector2(x + w - AisvTheme.Width(high, 0.62f), y), AisvTheme.TextFaint, 0.62f);
		y += 36;
		Kit.SectionTitle(b, Tr.Get("mood.why", "为什么"), x, y);
		y += 36;
		foreach (MoodCauseItem c in e.Causes)
		{
			AisvTheme.FillRounded(b, new Rectangle(x + 2, y + 9, 10, 10), MoodText.CauseColor(c.Kind), 5);
			foreach (string line in AisvTheme.WrapLines(c.Text, w - 24, 0.75f))
			{
				AisvTheme.DrawText(b, line, new Vector2(x + 22, y), AisvTheme.Text, 0.75f);
				y += AisvTheme.LineHeight(0.75f);
			}
			y += 4;
		}
		y += 14;
		Kit.SectionTitle(b, Tr.Get("mood.tips", "怎么相处"), x, y);
		y += 36;
		foreach (MoodAdvice a in e.Advice)
		{
			List<string> lines = AisvTheme.WrapLines(MoodText.Advice(a), w - 52, 0.72f);
			Rectangle tip = new Rectangle(x, y, w, lines.Count * AisvTheme.LineHeight(0.72f) + 22);
			AisvTheme.FillRounded(b, tip, a == MoodAdvice.BirthdayGift ? AisvTheme.PinkSoft : AisvTheme.GoldSoft, 10);
			AisvTheme.FillRounded(b, new Rectangle(tip.X + 14, tip.Y + 14, 14, 14), a == MoodAdvice.BirthdayGift ? AisvTheme.Pink : AisvTheme.Gold, 7);
			int ly = tip.Y + 11;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(tip.X + 40, ly), AisvTheme.Text, 0.72f);
				ly += AisvTheme.LineHeight(0.72f);
			}
			y = tip.Bottom + 10;
		}
		AisvTheme.EndClip(b);
		_detailScroll.Finish(b, content, y + 10, 8);
	}

	private void Select(string npc)
	{
		Game1.playSound("shiny4");
		_selected = npc;
		_detailScroll.Reset();
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
