using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace AISV.UI.Apps;

/// <summary>致谢：帮过这个 mod 的人，按贡献分成几张卡片。</summary>
internal sealed class ThanksPage : ShellPage
{
	private sealed record Credit(string Name, string Detail);

	private sealed record Section(string Title, string Intro, IReadOnlyList<Credit> Credits);

	private readonly Scroller _scroll = new Scroller();

	private readonly List<Section> _sections = new List<Section>
	{
		new Section(Tr.Get("thanks.special", "特别致谢"), "", new[]
		{
			new Credit("坐忘道", Tr.Get("thanks.special.zuowangdao", "好友。自本项目重构之初便持续关注，一年以来不断催更、提出建议，并在开发期间捐赠了大量免费 API 额度，让这个 mod 得以持续免费提供给所有人。没有他，就没有今天这个版本。"))
		}),
		new Section(Tr.Get("thanks.ideas", "创意与功能共创"), Tr.Get("thanks.ideas.intro", "以下功能的创意来自群友的提议："), new[]
		{
			new Credit("鹿鸣即盈", Tr.Get("thanks.ideas.1", "向 NPC 赊账、在任务板向村民发布委托")),
			new Credit("~", Tr.Get("thanks.ideas.2", "配偶自主行为、玩家主动发起对话、NPC 独立完成农活")),
			new Credit("予你碎碎念", Tr.Get("thanks.ideas.3", "赊账期限与按性格差异化的催债方式、对话时游戏时间缓慢流逝、NPC 资产分级")),
			new Credit("坐忘道", Tr.Get("thanks.ideas.4", "对话消耗游戏时间、SVE 兼容建议")),
			new Credit("千钟", Tr.Get("thanks.ideas.5", "NPC 商业竞争（把皮埃尔挤兑破产）")),
			new Credit("sea", Tr.Get("thanks.ideas.6", "NPC 自主发展与经济系统")),
			new Credit("Sans.", Tr.Get("thanks.ideas.7", "修改 NPC 昵称")),
			new Credit("孤影中偏执的悲伤", Tr.Get("thanks.ideas.8", "NPC 心情与骂人反应、宠物对话设想"))
		}),
		new Section(Tr.Get("thanks.testing", "测试与问题反馈"), "", new[]
		{
			new Credit("千钟", Tr.Get("thanks.testing.1", "录屏定位 UI 按钮点击错位与分辨率问题")),
			new Credit("恰似暖骄阳", Tr.Get("thanks.testing.2", "反馈行为类 mod 冲突、高心值拥抱与亲吻被覆盖")),
			new Credit("~", Tr.Get("thanks.testing.3", "地图 mod 兼容性、界面按钮偏移")),
			new Credit("KANA", Tr.Get("thanks.testing.4", "平板端运行测试")),
			new Credit("Sans.", Tr.Get("thanks.testing.5", "按钮偏移、一键下载内容异常")),
			new Credit("吃墨之4092", Tr.Get("thanks.testing.6", "游戏内输入法无法输入中文")),
			new Credit("gjjvvjj", Tr.Get("thanks.testing.7", "SVE 与里奇赛德村兼容性确认")),
			new Credit("郜", Tr.Get("thanks.testing.8", "小偷 mod 冲突反馈"))
		}),
		new Section(Tr.Get("thanks.along", "一路相伴"), Tr.Get("thanks.along.intro", "感谢一路陪伴、鼓励并帮助解答新人问题的老乡们："), new[]
		{
			new Credit("KANA、吃墨之4092、小马、文能、今天，也要开开心心的哦！、sea、u2、顾辰、清阳", Tr.Get("thanks.along.all", "以及所有下载、试玩、提出过意见的老乡。"))
		})
	};

	private Rectangle _view;

	public ThanksPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("thanks.title", "致谢");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		AisvTheme.BeginClip(b, area);
		int y = area.Y + 20 - _scroll.Offset;
		foreach (Section section in _sections)
		{
			y = Card(b, section, x, y, w) + 18;
		}
		foreach (string line in AisvTheme.WrapLines(Tr.Get("thanks.footer", "由于记录所限，难免有遗漏。如有帮助过我但未被列出的老乡，请通过群内或 N 网私信告知，我会在后续版本补上。"), w, 0.74f))
		{
			AisvTheme.DrawText(b, line, new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
			y += AisvTheme.LineHeight(0.74f);
		}
		AisvTheme.EndClip(b);
		_scroll.Finish(b, area, y + 12);
	}

	private static int Card(SpriteBatch b, Section s, int x, int y, int w)
	{
		const int inner = 20;
		int tw = w - inner * 2;
		List<string> intro = s.Intro.Length > 0 ? AisvTheme.WrapLines(s.Intro, tw, 0.78f).ToList() : new List<string>();
		List<(List<string> Name, List<string> Detail)> credits = s.Credits
			.Select(c => (AisvTheme.WrapLines(c.Name, tw, 0.8f).ToList(), AisvTheme.WrapLines(c.Detail, tw, 0.76f).ToList()))
			.ToList();
		int height = inner * 2 + AisvTheme.LineHeight(0.95f) + 10 + intro.Count * AisvTheme.LineHeight(0.78f) + (intro.Count > 0 ? 10 : 0)
			+ credits.Sum(c => c.Name.Count * AisvTheme.LineHeight(0.8f) + c.Detail.Count * AisvTheme.LineHeight(0.76f) + 10);
		AisvTheme.DrawCard(b, new Rectangle(x, y, w, height));
		int tx = x + inner, ty = y + inner;
		AisvTheme.Bold(b, s.Title, new Vector2(tx, ty), AisvTheme.AccentDark, 0.95f);
		ty += AisvTheme.LineHeight(0.95f) + 10;
		foreach (string line in intro)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.TextMuted, 0.78f);
			ty += AisvTheme.LineHeight(0.78f);
		}
		ty += intro.Count > 0 ? 10 : 0;
		foreach ((List<string> name, List<string> detail) in credits)
		{
			foreach (string line in name)
			{
				AisvTheme.Bold(b, line, new Vector2(tx, ty), AisvTheme.Text, 0.8f);
				ty += AisvTheme.LineHeight(0.8f);
			}
			foreach (string line in detail)
			{
				AisvTheme.DrawText(b, line, new Vector2(tx, ty), AisvTheme.TextMuted, 0.76f);
				ty += AisvTheme.LineHeight(0.76f);
			}
			ty += 10;
		}
		return y + height;
	}
}
