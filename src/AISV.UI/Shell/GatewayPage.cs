using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

/// <summary>AI 连接：选服务器或自己的网关。服务器只挑模型和思考档位；自己的网关填地址、密钥、模型，能当场测一下通不通。</summary>
internal sealed class GatewayPage : ShellPage
{
	private static readonly int[] ReplyLengths = { 100, 150, 200, 240, 275, 300, 400, 500, 750, 1000, 1500, 2000 };

	private static readonly int[] Memories = { 5, 10, 15, 20, 30, 40, 60, 100, 200 };

	private static readonly int[] TokenCaps = { 4096, 8192, 12000, 16000, 20000 };

	private static readonly int[] Waits = { 15, 30, 45, 60, 90, 120, 180, 240, 300 };

	private static readonly int[] DailyCalls = { 0, 50, 100, 150, 200, 300, 500 };

	private readonly FieldSet _fields = new FieldSet();

	private readonly TextArea _url;

	private readonly TextArea _key;

	private readonly TextArea _model;

	private readonly TextArea _backups;

	private readonly TextArea _nightModel;

	private readonly TextArea _nightBackups;

	private readonly Scroller _scroll = new Scroller();

	private Rectangle _view;

	private bool _testing;

	private string _result = "";

	private bool _resultOk;

	public GatewayPage(ShellContext ctx) : base(ctx)
	{
		ModConfig c = ctx.Config();
		_url = Field(c.CustomLlmBaseUrl, 300, "https://example.com/v1");
		_key = Field(c.CustomLlmApiKey, 300, Tr.Get("gateway.keyHint", "本地模型不需要可以留空"));
		_key.Masked = true;
		_model = Field(c.CustomLlmModel, 120, Tr.Get("gateway.modelHint", "比如 deepseek-chat"));
		_backups = Field(c.ChatBackupModels, 300, Tr.Get("gateway.backupsHint", "主模型失败时依次换，用逗号隔开，可以留空"));
		_nightModel = Field(c.NightlyModel, 120, Tr.Get("gateway.nightHint", "留空就和对话模型一样"));
		_nightBackups = Field(c.NightlyBackupModels, 300, Tr.Get("gateway.backupsHint", "主模型失败时依次换，用逗号隔开，可以留空"));
	}

	public override string Title => Tr.Get("gateway.title", "AI 连接");

	public override bool CapturesKeys => _fields.Typing;

	private TextArea Field(string? value, int limit, string hint)
	{
		TextArea area = _fields.Add(limit, hint, singleLine: true);
		area.Text = value ?? "";
		return area;
	}

	public override void Scroll(int direction) => _scroll.Wheel(direction);

	public override bool KeyPress(Keys key)
	{
		if (_fields.KeyPress(key))
		{
			return true;
		}
		return _scroll.PageKeys(key, _view.Height - 80);
	}

	public override void Closed()
	{
		_fields.BlurAll();
		SaveTyped();
	}

	private void SaveTyped()
	{
		ModConfig c = Ctx.Config();
		string url = _url.Text.Trim(), key = _key.Text.Trim(), model = _model.Text.Trim();
		string backups = _backups.Text.Trim(), night = _nightModel.Text.Trim(), nightBackups = _nightBackups.Text.Trim();
		if (c.CustomLlmBaseUrl == url && c.CustomLlmApiKey == key && c.CustomLlmModel == model
			&& (c.ChatBackupModels ?? "") == backups && (c.NightlyModel ?? "") == night && (c.NightlyBackupModels ?? "") == nightBackups)
		{
			return;
		}
		c.CustomLlmBaseUrl = url;
		c.CustomLlmApiKey = key;
		c.CustomLlmModel = model;
		c.ChatBackupModels = backups;
		c.NightlyModel = night;
		c.NightlyBackupModels = nightBackups;
		Ctx.SaveConfig(c.Normalize());
	}

	private void Change(Action<ModConfig> edit)
	{
		Game1.playSound("drumkit6");
		SaveTyped();
		ModConfig c = Ctx.Config();
		edit(c);
		Ctx.SaveConfig(c.Normalize());
		_result = "";
	}

	private void Test()
	{
		AiService? ai = Ctx.Ai;
		if (_testing || ai == null)
		{
			return;
		}
		_fields.BlurAll();
		SaveTyped();
		_testing = true;
		_result = "";
		Task.Run(async () =>
		{
			string text;
			bool ok;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("connection_test", "这是一次连通测试。", "只回一个字：好", 64, 0.1));
				ok = r.Ok;
				text = ok ? Tr.Get("gateway.testOk", "通了，模型回了：{{text}}", ("text", (r.Text ?? "").Trim())) : FailureText.Describe(r.Failure, r.Message);
			}
			catch (Exception ex)
			{
				ok = false;
				text = ex.Message;
			}
			MainThread.Run(() =>
			{
				_testing = false;
				_resultOk = ok;
				_result = text;
				Game1.playSound(ok ? "newArtifact" : "cancel");
			});
		});
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		_view = area;
		ModConfig c = Ctx.Config();
		bool server = Ctx.UsesServer();
		AisvTheme.BeginClip(b, area);
		int w = Math.Min(area.Width - 64, 820);
		int x = area.X + (area.Width - w) / 2;
		int y = area.Y + 18 - _scroll.Offset;
		if (Ctx.HasServer())
		{
			y = Choice(b, hits, Tr.Get("gateway.mode", "连接方式"), Tr.Get("gateway.modeNote", "默认用 AI星露谷服务器；也可以换成自己的 AI 服务"), new[] { "server", "own" }, server ? "server" : "own",
				v => v == "own" ? Tr.Get("gateway.modeOwn", "我自己的 AI 网关") : Tr.Get("gateway.modeServer", "AI星露谷服务器"), (m, v) => m.AiConnectionMode = v, x, y, w);
		}
		y = server ? DrawServer(b, hits, c, x, y, w) : DrawOwn(b, hits, c, x, y, w);
		y = Section(b, Tr.Get("gateway.replies", "回复"), x, y);
		y = Number(b, hits, Tr.Get("gateway.replyLength", "村民每句话最多几个字"), ReplyLengths, c.ReplyLength, (m, v) => m.ReplyLength = v, "{0}", x, y, w);
		y = Number(b, hits, Tr.Get("gateway.memory", "带给 AI 的对话记录条数"), Memories, c.RecentTurnsInPrompt, (m, v) => m.RecentTurnsInPrompt = v, "{0}", x, y, w);
		AisvTheme.EndClip(b);
		_scroll.Finish(b, area, y + 20);
	}

	private int DrawServer(SpriteBatch b, Hits hits, ModConfig c, int x, int y, int w)
	{
		ServerAccount? account = Ctx.Account;
		IReadOnlyList<PlayerModel> models = account?.PlayerModels ?? Array.Empty<PlayerModel>();
		string[] ids = new[] { "" }.Concat(models.Select(m => m.Id)).ToArray();
		string Model(string id)
		{
			if (id.Length == 0)
			{
				return Tr.Get("gateway.modelDefault", "默认（服务器推荐）");
			}
			PlayerModel? m = models.FirstOrDefault(p => p.Id == id);
			string label = m?.Label is { Length: > 0 } l ? l : id;
			return m?.Factor is double f && f > 0 ? label + Tr.Get("gateway.modelFactor", "（约 {{n}} 倍价钱）", ("n", f.ToString("0.#", CultureInfo.InvariantCulture))) : label;
		}
		y = Section(b, Tr.Get("gateway.serverGroup", "服务器"), x, y);
		y = Choice(b, hits, Tr.Get("gateway.serverModel", "对话模型"), Tr.Get("gateway.serverModelNote", "贵的模型扣得快"), ids, c.ServerChatModel ?? "", Model, (m, v) => m.ServerChatModel = v, x, y, w);
		string[] levels = { "", "off", "low", "medium", "high" };
		int top = Math.Max(1, Array.IndexOf(levels, account?.ThinkingMax ?? "medium"));
		return Choice(b, hits, Tr.Get("gateway.serverThinking", "思考档位"), Tr.Get("gateway.thinkingNote", "想得越多越慢，也越贵"), levels.Take(top + 1).ToArray(), c.ServerThinkingLevel ?? "",
			v => v.Length == 0 ? Tr.Get("gateway.thinkingDefault", "默认（服务器设的）") : Thinking(v), (m, v) => m.ServerThinkingLevel = v, x, y, w);
	}

	private int DrawOwn(SpriteBatch b, Hits hits, ModConfig c, int x, int y, int w)
	{
		y = Section(b, Tr.Get("gateway.ownGroup", "我的网关（OpenAI 兼容接口）"), x, y);
		y = Input(b, hits, Tr.Get("gateway.url", "网关地址"), _url, x, y, w);
		y = Input(b, hits, Tr.Get("gateway.key", "密钥"), _key, x, y, w);
		y = Input(b, hits, Tr.Get("gateway.model", "对话模型"), _model, x, y, w);
		Rectangle test = new Rectangle(x, y, 200, 48);
		AisvTheme.Button(b, test, _testing ? Tr.Get("gateway.testing", "正在测试……") : Tr.Get("gateway.test", "测试连接"), primary: true, hover: AisvTheme.Hover(test, _view), disabled: _testing);
		hits.Add(test, Test, _view);
		y = test.Bottom + 10;
		if (_result.Length > 0)
		{
			string wrapped = AisvTheme.Wrap(_result, w);
			b.DrawString(Game1.smallFont, wrapped, new Vector2(x, y), _resultOk ? AisvTheme.AccentDark : AisvTheme.Danger);
			y += wrapped.Split('\n').Length * AisvTheme.LineHeight() + 8;
		}
		y += 10;
		y = Input(b, hits, Tr.Get("gateway.backups", "对话备用模型"), _backups, x, y, w);
		y = Choice(b, hits, Tr.Get("gateway.thinking", "对话思考档位"), Tr.Get("gateway.thinkingNote", "想得越多越慢，也越贵"), SlotRouter.ReasoningEfforts, SlotRouter.NormalizeEffort(c.ChatThinking), Thinking, (m, v) => m.ChatThinking = v, x, y, w);
		y = Number(b, hits, Tr.Get("gateway.wait", "对话超时"), Waits, c.ChatWaitSeconds, (m, v) => m.ChatWaitSeconds = v, Tr.Get("gateway.seconds", "{0} 秒"), x, y, w);
		y = Section(b, Tr.Get("gateway.nightGroup", "后台（八卦、信件、晚上整理）"), x, y);
		y = Input(b, hits, Tr.Get("gateway.nightModel", "后台模型"), _nightModel, x, y, w);
		y = Input(b, hits, Tr.Get("gateway.nightBackups", "后台备用模型"), _nightBackups, x, y, w);
		y = Choice(b, hits, Tr.Get("gateway.nightThinking", "后台思考档位"), "", SlotRouter.ReasoningEfforts, SlotRouter.NormalizeEffort(c.NightlyThinking), Thinking, (m, v) => m.NightlyThinking = v, x, y, w);
		y = Number(b, hits, Tr.Get("gateway.nightWait", "后台超时"), Waits, c.NightlyWaitSeconds, (m, v) => m.NightlyWaitSeconds = v, Tr.Get("gateway.seconds", "{0} 秒"), x, y, w);
		y = Number(b, hits, Tr.Get("gateway.nightDaily", "每天后台请求上限（0 = 不限）"), DailyCalls, c.NightlyCallsPerDay, (m, v) => m.NightlyCallsPerDay = v, "{0}", x, y, w);
		y = Section(b, Tr.Get("gateway.advanced", "进阶"), x, y);
		y = Number(b, hits, Tr.Get("gateway.tokens", "回复的输出 token 上限"), TokenCaps, c.ReplyTokenCap, (m, v) => m.ReplyTokenCap = v, "{0}", x, y, w);
		return Toggle(b, hits, Tr.Get("gateway.log", "把 AI 收发的全文写进日志"), c.LogAiTraffic, (m, v) => m.LogAiTraffic = v, x, y, w);
	}

	private static string Thinking(string v) => v switch
	{
		"auto" => Tr.Get("gateway.level.auto", "自动"),
		"off" => Tr.Get("gateway.level.off", "不思考"),
		"low" => Tr.Get("gateway.level.low", "低"),
		"medium" => Tr.Get("gateway.level.medium", "中"),
		"high" => Tr.Get("gateway.level.high", "高"),
		_ => v
	};

	private static int Section(SpriteBatch b, string title, int x, int y)
	{
		AisvTheme.DrawText(b, title, new Vector2(x + 6, y + 8), AisvTheme.TextFaint, 0.75f);
		return y + 38;
	}

	private int Input(SpriteBatch b, Hits hits, string label, TextArea input, int x, int y, int w)
	{
		AisvTheme.DrawText(b, label, new Vector2(x + 2, y), AisvTheme.TextMuted, 0.8f);
		input.Bounds = new Rectangle(x, y + 28, w, AisvTheme.LineHeight() + 24);
		input.Draw(b);
		hits.Add(input.Bounds, () => _fields.Focus(input), _view);
		return input.Bounds.Bottom + 12;
	}

	private int Choice(SpriteBatch b, Hits hits, string label, string note, string[] values, string current, Func<string, string> format, Action<ModConfig, string> set, int x, int y, int w)
	{
		int index = Math.Max(0, Array.IndexOf(values, current));
		string text = format(values[index]);
		int bw = (int)AisvTheme.Width(text, 0.8f) + 56;
		Rectangle btn = new Rectangle(x + w - bw, y + 8, bw, 44);
		Label(b, label, note, x, y, w - bw - 16);
		AisvTheme.Button(b, btn, text, hover: AisvTheme.Hover(btn, _view), scale: 0.8f);
		AisvTheme.Chevron(b, new Vector2(btn.Right - 18, btn.Center.Y), left: false, AisvTheme.TextFaint);
		string next = values[(index + 1) % values.Length];
		hits.Add(btn, () => Change(c => set(c, next)), _view);
		return y + 62;
	}

	private int Number(SpriteBatch b, Hits hits, string label, int[] stops, int current, Action<ModConfig, int> set, string format, int x, int y, int w)
	{
		int index = 0;
		for (int i = 1; i < stops.Length; i++)
		{
			if (Math.Abs(stops[i] - current) < Math.Abs(stops[index] - current))
			{
				index = i;
			}
		}
		string[] values = stops.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray();
		return Choice(b, hits, label, "", values, values[index], v => string.Format(CultureInfo.InvariantCulture, format, v), (c, v) => set(c, int.Parse(v, CultureInfo.InvariantCulture)), x, y, w);
	}

	private int Toggle(SpriteBatch b, Hits hits, string label, bool on, Action<ModConfig, bool> set, int x, int y, int w)
	{
		Rectangle sw = new Rectangle(x + w - 64, y + 12, 64, 36);
		Label(b, label, "", x, y, w - 80);
		AisvTheme.Toggle(b, sw, on);
		hits.Add(sw, () => Change(c => set(c, !on)), _view);
		return y + 62;
	}

	private static void Label(SpriteBatch b, string label, string note, int x, int y, int w)
	{
		AisvTheme.DrawText(b, label, new Vector2(x + 2, note.Length > 0 ? y + 2 : y + 16), AisvTheme.Text, 0.85f);
		if (note.Length > 0)
		{
			AisvTheme.DrawText(b, AisvTheme.Truncate(note, w, 0.7f), new Vector2(x + 2, y + 30), AisvTheme.TextFaint, 0.7f);
		}
	}
}
