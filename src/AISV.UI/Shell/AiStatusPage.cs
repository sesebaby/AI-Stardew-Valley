using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>AI 状态：现在好不好用、连的是哪、今天用了多少、最近为什么失败，可以当场测试。</summary>
internal sealed class AiStatusPage : ShellPage
{
	private readonly Scroller _scroll = new Scroller();

	private Rectangle _view;

	private bool _testing;

	private bool? _testOk;

	private string _testText = "";

	public AiStatusPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("shell.settings.aiStatus", "AI 状态");

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key) => _scroll.PageKeys(key, _view.Height - 80);

	private void Test()
	{
		if (_testing || Ctx.Ai == null || Ctx.Ai.AskLoginIfNeeded(playerAsked: true))
		{
			return;
		}
		_testing = true;
		_testText = "";
		ConnectionTest.Run(Ctx.Ai, (ok, text) =>
		{
			_testing = false;
			_testOk = ok;
			_testText = text;
		});
	}

	private bool IsSetUp(ModConfig c, AiService ai) => ai.UsesServer
		? Ctx.Account?.IsLoggedIn == true
		: !string.IsNullOrWhiteSpace(c.CustomLlmBaseUrl) && SlotRouter.Models(c, AiSlot.Dialogue, false).Count > 0;

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_view = area;
		AiService? ai = Ctx.Ai;
		if (ai == null)
		{
			return;
		}
		ModConfig c = Ctx.Config();
		Hearth.BeginClip(b, area);
		int w = Math.Min(area.Width - 64, 820), x = area.X + (area.Width - w) / 2;
		int y = area.Y + 20 - _scroll.Offset;
		y = DrawHealth(b, AiHealthRules.Evaluate(new AiHealthFacts(c.AiOn, IsSetUp(c, ai), ai.IsGatewayOffline, ai.SucceededToday, ai.FailedToday, _testOk)), x, y, w, hits) + 16;
		y = DrawFacts(b, Tr.Get("aistatus.connection", "连接"), Connection(c, ai), x, y, w) + 16;
		(int used, int skipped) = ai.BackgroundUsage;
		string limit = ai.BackgroundDailyLimit == 0 ? Tr.Get("aistatus.noLimit", "不限") : ai.BackgroundDailyLimit.ToString();
		y = DrawFacts(b, Tr.Get("aistatus.today", "今天"), new List<(string, string)>
		{
			(Tr.Get("aistatus.succeeded", "成功"), ai.SucceededToday.ToString()),
			(Tr.Get("aistatus.failed", "失败"), ai.FailedToday.ToString()),
			(Tr.Get("aistatus.background", "后台任务"), skipped > 0 ? Tr.Get("aistatus.backgroundSkipped", "{{used}} / {{limit}}，跳过 {{skipped}} 次", ("used", used), ("limit", limit), ("skipped", skipped)) : $"{used} / {limit}"),
			(Tr.Get("aistatus.tokensToday", "今天用的 token"), ai.TokensToday.ToString("N0")),
			(Tr.Get("aistatus.tokensSession", "这次开游戏以来"), ai.TokensSession.ToString("N0"))
		}, x, y, w) + 16;
		IReadOnlyList<RequestTally> tallies = ai.Tallies;
		if (tallies.Count > 0)
		{
			y = DrawFacts(b, Tr.Get("aistatus.byRequest", "调试统计：各类请求（这次开游戏以来）"), tallies.Take(12).Select(t => (t.Name, Tr.Get("aistatus.tally", "{{calls}} 次，失败 {{failed}} 次，平均 {{sec}} 秒", ("calls", t.Calls), ("failed", t.Failures), ("sec", t.AverageSeconds.ToString("0.0"))))).ToList(), x, y, w) + 16;
		}
		y = DrawBusy(b, x, y, w) + 16;
		y = DrawFailures(b, ai, x, y, w);
		Hearth.EndClip(b);
		_scroll.Finish(b, area, y + 12);
	}

	private List<(string, string)> Connection(ModConfig c, AiService ai)
	{
		List<(string, string)> rows = new List<(string, string)>
		{
			(Tr.Get("aistatus.aiSwitch", "AI 功能"), c.AiOn ? Tr.Get("aistatus.on", "开") : Tr.Get("aistatus.offWord", "关")),
			(Tr.Get("aistatus.mode", "连接方式"), ai.UsesServer ? Tr.Get("aistatus.server", "AI星露谷服务器") : Tr.Get("aistatus.own", "自己的 AI 网关"))
		};
		if (ai.UsesServer)
		{
			rows.Add((Tr.Get("aistatus.account", "账号"), Ctx.Account?.IsLoggedIn == true ? Ctx.Account.Name : Tr.Get("aistatus.notLoggedIn", "还没登录")));
		}
		else
		{
			rows.Add((Tr.Get("aistatus.url", "网关地址"), string.IsNullOrWhiteSpace(c.CustomLlmBaseUrl) ? Tr.Get("aistatus.notFilled", "没填") : c.CustomLlmBaseUrl.Trim()));
			rows.Add((Tr.Get("aistatus.key", "密钥"), KeyRing.Split(c.CustomLlmApiKey).Count switch
			{
				0 => Tr.Get("aistatus.notFilled", "没填"),
				1 => Tr.Get("aistatus.filled", "已填"),
				int n => Tr.Get("aistatus.filledMany", "已填 {{n}} 把，自动轮换", ("n", n))
			}));
		}
		IReadOnlyList<string> models = SlotRouter.Models(c, AiSlot.Dialogue, ai.UsesServer);
		rows.Add((Tr.Get("aistatus.model", "对话模型"), models.Count > 0 ? models[0] : Tr.Get("aistatus.notFilled", "没填")));
		rows.Add((Tr.Get("aistatus.fallback", "AI 没回话时"), c.GameLinesWhenAiDown || ai.UsesServer ? Tr.Get("aistatus.fallbackLines", "说游戏原来的台词") : Tr.Get("aistatus.fallbackQuiet", "不说话，只弹提示")));
		return rows;
	}

	private int DrawHealth(SpriteBatch b, AiHealth health, int x, int y, int w, Hits hits)
	{
		Color fill = health.Level switch
		{
			AiHealthLevel.Good => Hearth.LeafPale,
			AiHealthLevel.Warning => Hearth.AmberPale,
			AiHealthLevel.Bad => new Color(250, 226, 218),
			_ => Hearth.ParchmentDeep
		};
		Color ink = health.Level switch
		{
			AiHealthLevel.Good => Hearth.LeafDeep,
			AiHealthLevel.Warning => Hearth.Amber,
			AiHealthLevel.Bad => Hearth.Ember,
			_ => Hearth.QuillSoft
		};
		List<string> result = _testText.Length > 0 ? Hearth.WrapLines(ShellText.Clean(_testText), w - 48, 0.74f) : new List<string>();
		int lh = Hearth.LineHeight(0.74f);
		Rectangle card = new Rectangle(x, y, w, 100 + result.Count * lh + (result.Count > 0 ? 8 : 0));
		Hearth.Bordered(b, card, fill, ink * 0.5f);
		Hearth.InkBold(b, health.Title, new Vector2(card.X + 24, card.Y + 18), ink, 1.05f);
		Hearth.Ink(b, Hearth.Truncate(health.Hint, w - 250, 0.74f), new Vector2(card.X + 24, card.Y + 58), Hearth.QuillSoft, 0.74f);
		Rectangle test = new Rectangle(card.Right - 20 - 180, card.Y + 26, 180, 48);
		Hearth.PressPlate(b, test, _testing ? Tr.Get("gateway.testing", "正在测试……") : Tr.Get("gateway.test", "测试连接"), primary: true, hover: Hearth.Hover(test, _view), disabled: _testing);
		hits.Add(test, Test, _view);
		int ry = card.Y + 94;
		foreach (string line in result)
		{
			Hearth.Ink(b, line, new Vector2(card.X + 24, ry), _testOk == true ? Hearth.LeafDeep : Hearth.Ember, 0.74f);
			ry += lh;
		}
		return card.Bottom;
	}

	private int DrawFacts(SpriteBatch b, string title, List<(string Label, string Value)> rows, int x, int y, int w)
	{
		const int rowH = 44;
		Rectangle card = new Rectangle(x, y, w, 56 + rows.Count * rowH + 8);
		Hearth.CardSlip(b, card);
		Kit.SectionTitle(b, title, x + 20, y + 18);
		int ry = y + 56;
		foreach ((string label, string value) in rows)
		{
			Hearth.Ink(b, label, new Vector2(x + 24, ry + 8), Hearth.QuillSoft, 0.8f);
			Hearth.Ink(b, Hearth.Truncate(ShellText.Clean(value), w - 240, 0.82f), new Vector2(x + 210, ry + 8), Hearth.Quill, 0.82f);
			ry += rowH;
		}
		return card.Bottom;
	}

	private int DrawBusy(SpriteBatch b, int x, int y, int w)
	{
		List<string> jobs = new List<string>();
		if (GenerationStatus.Current is { } talk)
		{
			jobs.Add(Tr.Get("aistatus.jobTalk", "{{name}}在想怎么回你（{{n}} 秒）", ("name", talk.DisplayName), ("n", (int)talk.Elapsed.TotalSeconds)));
		}
		if (Ctx.Social.IsAnalysisRunning)
		{
			jobs.Add(Tr.Get("aistatus.jobNight", "整理八卦和日记"));
		}
		if (Ctx.Messenger?.PendingCount is > 0 and int pending)
		{
			jobs.Add(Tr.Get("aistatus.jobTexts", "信使有 {{n}} 条在等回复", ("n", pending)));
		}
		return DrawList(b, Tr.Get("aistatus.busy", "正在进行"), jobs, Tr.Get("aistatus.idle", "现在没有在等 AI。"), Hearth.Quill, x, y, w);
	}

	private int DrawFailures(SpriteBatch b, AiService ai, int x, int y, int w)
	{
		List<string> lines = new List<string>();
		foreach (AiFailureNote note in ai.RecentFailures)
		{
			lines.Add(note.When + "  " + ShellText.Clean(FailureText.Describe(note.Kind, note.Message)));
		}
		return DrawList(b, Tr.Get("aistatus.failures", "最近的失败"), lines, Tr.Get("aistatus.noFailures", "这次开游戏以来没有失败。"), Hearth.Rust, x, y, w);
	}

	private static int DrawList(SpriteBatch b, string title, List<string> items, string empty, Color color, int x, int y, int w)
	{
		const float scale = 0.8f;
		int lh = Hearth.LineHeight(scale);
		List<(string Text, Color Color)> rows = new List<(string, Color)>();
		foreach (string item in items)
		{
			foreach (string line in Hearth.WrapLines(item, w - 48, scale))
			{
				rows.Add((line, color));
			}
		}
		if (rows.Count == 0)
		{
			rows.Add((empty, Hearth.QuillPale));
		}
		Rectangle card = new Rectangle(x, y, w, 56 + rows.Count * lh + 16);
		Hearth.CardSlip(b, card);
		Kit.SectionTitle(b, title, x + 20, y + 18);
		int ry = y + 56;
		foreach ((string text, Color ink) in rows)
		{
			Hearth.Ink(b, text, new Vector2(x + 24, ry), ink, scale);
			ry += lh;
		}
		return card.Bottom;
	}
}
