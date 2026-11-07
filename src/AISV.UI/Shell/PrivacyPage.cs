using System.Collections.Generic;
using System.Linq;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Text;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>数据说明：用服务器时会收到什么、拿来做什么，哪些可以关。</summary>
internal sealed class PrivacyPage : ShellPage
{
	private sealed record Section(string Title, IReadOnlyList<string> Points);

	private readonly Scroller _scroll = new Scroller();

	private readonly List<Section> _sections = new List<Section>
	{
		new Section(Tr.Get("privacy.needed", "服务必需的（一直会有）"), new[]
		{
			Tr.Get("privacy.needed.account", "账号：用户名；密码只存加密后的哈希，谁也看不到原文。"),
			Tr.Get("privacy.needed.login", "登录：登录时的 IP 和电脑名，用来显示你在哪些电脑上登录过，也用来防盗号。"),
			Tr.Get("privacy.needed.usage", "AI 用量：每次 AI 请求的时间、用了多少 token、花了多少额度，用来扣额度和限速。"),
			Tr.Get("privacy.needed.machine", "机器码：这台电脑系统机器 ID 加密后的哈希，看不出原值，只用来防刷号和拉黑恶意账号。")
		}),
		new Section(Tr.Get("privacy.improve", "帮助改进功能的（可以关）"), new[]
		{
			Tr.Get("privacy.improve.time", "游戏时长：存档开着、游戏窗口在前台的时间。"),
			Tr.Get("privacy.improve.counts", "功能次数：打开了哪些页面、应用，用了几次手机和电脑。"),
			Tr.Get("privacy.improve.feature", "每次 AI 请求属于哪个功能，比如和村民聊天、居民群、来信。"),
			Tr.Get("privacy.improve.why", "这些只用来了解大家喜欢哪些功能、改进这个 mod：不卖、不给别人、不做广告。默认开着，关掉后就不再收集，还没发出去的也会丢掉。")
		}),
		new Section(Tr.Get("privacy.never", "不会收集的"), new[]
		{
			Tr.Get("privacy.never.chat", "你和村民聊天的内容、存档内容、你写的任何文字：服务器只把请求转给 AI，不保存。"),
			Tr.Get("privacy.never.device", "系统版本、硬件配置、真实姓名、联系方式。"),
			Tr.Get("privacy.never.keep", "使用记录默认保留 90 天，之后自动删除。用自己的 AI 网关时，以上都不会发给 AI星露谷服务器。")
		})
	};

	private Rectangle _view;

	public PrivacyPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("privacy.title", "数据说明");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		int pad = area.Width < 900 ? 16 : 32;
		int x = area.X + pad, w = area.Width - pad * 2 - 12;
		Hearth.BeginClip(b, area);
		int y = area.Y + 20 - _scroll.Offset;
		foreach (string line in Hearth.WrapLines(Tr.Get("privacy.intro", "用 AI星露谷服务器时，服务器会收到下面这些信息。"), w, 0.8f))
		{
			Hearth.Ink(b, line, new Vector2(x, y), Hearth.QuillSoft, 0.8f);
			y += Hearth.LineHeight(0.8f);
		}
		y += 12;
		foreach (Section section in _sections)
		{
			y = Card(b, section, x, y, w) + 16;
		}
		y = Switch(b, hits, x, y, w);
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + 12);
	}

	private int Switch(SpriteBatch b, Hits hits, int x, int y, int w)
	{
		bool on = Ctx.Config().ShareUsageStats;
		string state = on ? Tr.Get("privacy.stateOn", "帮助改进功能：开着。谢谢你！") : Tr.Get("privacy.stateOff", "帮助改进功能：关着。");
		foreach (string line in Hearth.WrapLines(state, w, 0.8f))
		{
			Hearth.Ink(b, line, new Vector2(x, y), Hearth.Quill, 0.8f);
			y += Hearth.LineHeight(0.8f);
		}
		y += 8;
		Rectangle button = new Rectangle(x, y, 260, 52);
		Hearth.PressPlate(b, button, on ? Tr.Get("privacy.turnOff", "关掉") : Tr.Get("privacy.turnOn", "打开"), primary: !on, hover: Hearth.Hover(button, _view));
		hits.Add(button, Toggle, _view);
		return button.Bottom;
	}

	private void Toggle()
	{
		ModConfig config = Ctx.Config();
		config.ShareUsageStats = !config.ShareUsageStats;
		if (!config.ShareUsageStats)
		{
			UsageReporter.Shared?.Clear();
		}
		Ctx.SaveConfig(config);
		Game1.playSound("drumkit6");
	}

	private static int Card(SpriteBatch b, Section s, int x, int y, int w)
	{
		const int inner = 20;
		int tw = w - inner * 2 - 18;
		List<List<string>> points = s.Points.Select(p => Hearth.WrapLines(p, tw, 0.76f).ToList()).ToList();
		int height = inner * 2 + Hearth.LineHeight(0.95f) + 10 + points.Sum(p => p.Count * Hearth.LineHeight(0.76f) + 8);
		Hearth.CardSlip(b, new Rectangle(x, y, w, height));
		int tx = x + inner, ty = y + inner;
		Hearth.InkBold(b, s.Title, new Vector2(tx, ty), Hearth.LeafDeep, 0.95f);
		ty += Hearth.LineHeight(0.95f) + 10;
		foreach (List<string> point in points)
		{
			Hearth.Ink(b, "·", new Vector2(tx, ty), Hearth.LeafDeep, 0.76f);
			foreach (string line in point)
			{
				Hearth.Ink(b, line, new Vector2(tx + 18, ty), Hearth.QuillSoft, 0.76f);
				ty += Hearth.LineHeight(0.76f);
			}
			ty += 8;
		}
		return y + height;
	}
}
