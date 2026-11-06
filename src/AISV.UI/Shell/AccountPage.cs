using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>AI星露谷账号：用户名和密码同一个表单，新名字就注册，已有的就登录；登录后看额度、改密码、退出。</summary>
internal sealed class AccountPage : ShellPage
{
	private enum View
	{
		Enter,
		Status,
		Password
	}

	private const int NameLimit = 20;

	private const int PasswordLimit = 64;

	private readonly bool _prompted;

	private FieldSet _fields = new FieldSet();

	private TextArea[] _inputs = Array.Empty<TextArea>();

	private View _view;

	private bool _revealed;

	private bool _busy;

	private bool _finished;

	private string _error = "";

	private string _lastName = "";

	public AccountPage(ShellContext ctx, bool prompted = false) : base(ctx)
	{
		_prompted = prompted;
		Show(ctx.Account?.IsLoggedIn == true ? View.Status : View.Enter);
		if (_view == View.Status)
		{
			Refresh();
		}
	}

	public override string Title => Tr.Get("account.title", "AI星露谷账号");

	public override bool CapturesKeys => _fields.Typing;

	private void Show(View view)
	{
		_fields.BlurAll();
		_fields = new FieldSet();
		_view = view;
		_error = "";
		_revealed = false;
		_inputs = view switch
		{
			View.Enter => new[]
			{
				Field(NameLimit, Tr.Get("account.nameHint", "字母、数字、下划线，3 到 20 位"), false),
				Field(PasswordLimit, Tr.Get("account.passwordHint", "至少 6 位"), true)
			},
			View.Password => new[]
			{
				Field(PasswordLimit, Tr.Get("account.oldHint", "现在的密码"), true),
				Field(PasswordLimit, Tr.Get("account.newHint", "新密码，至少 6 位"), true),
				Field(PasswordLimit, Tr.Get("account.againHint", "再输一遍新密码"), true)
			},
			_ => Array.Empty<TextArea>()
		};
		if (view == View.Enter)
		{
			_inputs[0].Text = Ctx.Account?.Name is { Length: > 0 } name ? name : _lastName;
		}
		if (_inputs.Length > 0)
		{
			_fields.Focus(_inputs.FirstOrDefault(i => i.Text.Length == 0) ?? _inputs[0]);
		}
	}

	private TextArea Field(int limit, string hint, bool masked)
	{
		TextArea area = _fields.Add(limit, hint, singleLine: true);
		area.Masked = masked;
		return area;
	}

	private void Refresh()
	{
		ServerAccount? account = Ctx.Account;
		if (account == null)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await account.RefreshMeAsync();
			}
			catch (Exception)
			{
				// 查不到就显示上次的数字。
			}
		});
	}

	public override bool KeyPress(Keys key)
	{
		if (_busy)
		{
			return true;
		}
		if (key == Keys.Enter && _inputs.Length > 0 && (_fields.Focused == null || _fields.Focused == _inputs[^1]))
		{
			Submit();
			return true;
		}
		return _fields.KeyPress(key);
	}

	public override void Closed()
	{
		_fields.BlurAll();
		ServerAccount? account = Ctx.Account;
		if (_prompted && !_finished && account != null && !account.IsLoggedIn)
		{
			account.DeferLoginToday();
			Notify(Tr.Get("account.later", "好的，今天村民先说游戏原来的台词。想用 AI 时在 设置 > 常用 > 账号 登录。"));
		}
	}

	private static void Notify(string text) => Game1.addHUDMessage(new HUDMessage(text) { noIcon = true, timeLeft = 7000 });

	private void Submit()
	{
		ServerAccount? account = Ctx.Account;
		if (_busy || account == null)
		{
			return;
		}
		string problem = _view == View.Enter ? EnterProblem() : PasswordProblem();
		if (problem.Length > 0)
		{
			_error = problem;
			Game1.playSound("cancel");
			return;
		}
		View view = _view;
		string[] values = _inputs.Select(i => i.Text).ToArray();
		_busy = true;
		_error = "";
		_fields.BlurAll();
		Task.Run(async () =>
		{
			(bool ok, string message) result;
			try
			{
				result = view == View.Enter
					? await account.EnterAsync(values[0].Trim(), values[1])
					: await account.ChangePasswordAsync(values[0], values[1]);
			}
			catch (Exception ex)
			{
				result = (false, ex.Message);
			}
			MainThread.Run(() => Done(view, result.ok, result.message));
		});
	}

	private string EnterProblem()
	{
		if (!Regex.IsMatch(_inputs[0].Text.Trim(), "^[A-Za-z0-9_]{3,20}$"))
		{
			return Tr.Get("account.badUser", "用户名只能用字母、数字和下划线，3 到 20 位。");
		}
		return _inputs[1].Text.Length < 6 ? Tr.Get("account.badPassword", "密码至少 6 位。") : "";
	}

	private string PasswordProblem()
	{
		if (_inputs[1].Text.Length < 6)
		{
			return Tr.Get("account.badPassword", "密码至少 6 位。");
		}
		return _inputs[1].Text != _inputs[2].Text ? Tr.Get("account.mismatch", "两次输的新密码不一样。") : "";
	}

	private void Done(View view, bool ok, string message)
	{
		_busy = false;
		if (!ok)
		{
			_error = message;
			Game1.playSound("cancel");
			if (_inputs.Length > 0)
			{
				_fields.Focus(_inputs[^1]);
			}
			return;
		}
		Game1.playSound("newArtifact");
		if (view == View.Password)
		{
			Notify(message);
			Show(View.Status);
			return;
		}
		string name = Ctx.Account?.Name ?? "";
		Notify(message == "created"
			? Tr.Get("account.registered", "注册好了，欢迎 {{name}}！记住用户名和密码，换电脑时用它们登录。", ("name", name))
			: Tr.Get("account.loggedIn", "已登录 {{name}}。", ("name", name)));
		_finished = true;
		if (_prompted)
		{
			Game1.activeClickableMenu?.exitThisMenu();
			return;
		}
		Show(View.Status);
		Refresh();
	}

	private void Logout()
	{
		ServerAccount? account = Ctx.Account;
		if (account == null || _busy)
		{
			return;
		}
		_busy = true;
		_lastName = account.Name;
		Task.Run(async () =>
		{
			try
			{
				await account.LogoutAsync();
			}
			catch (Exception)
			{
				// 服务器没回也算退出：本机的登录已经清掉。
			}
			MainThread.Run(() =>
			{
				_busy = false;
				Notify(Tr.Get("account.loggedOut", "已退出登录。"));
				Show(View.Enter);
			});
		});
	}

	private void Later()
	{
		if (_busy)
		{
			return;
		}
		if (_prompted)
		{
			Game1.activeClickableMenu?.exitThisMenu();
		}
		else
		{
			Host.Back();
		}
	}

	private void ToggleReveal()
	{
		_revealed = !_revealed;
		foreach (TextArea area in _inputs.Skip(1))
		{
			area.Masked = !_revealed;
		}
		Game1.playSound("smallSelect");
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		const int pad = 28;
		int w = Math.Min(area.Width - pad * 2, 640);
		int x = area.X + (area.Width - w) / 2;
		int y = area.Y + 20;
		if (Ctx.Account == null || !Ctx.UsesServer())
		{
			Paragraph(b, Tr.Get("account.ownGateway", "现在用的是自己的 AI 网关，不需要账号。"), x, y, w, AisvTheme.TextMuted);
			return;
		}
		switch (_view)
		{
		case View.Enter:
			DrawEnter(b, hits, x, y, w);
			break;
		case View.Password:
			DrawPassword(b, hits, x, y, w);
			break;
		default:
			DrawStatus(b, hits, x, y, w);
			break;
		}
	}

	private void DrawEnter(SpriteBatch b, Hits hits, int x, int y, int w)
	{
		y = Paragraph(b, Tr.Get("account.subEnter", "第一次来：起个用户名、设个密码，点“开始”就注册好了（免费）。\n已经有账号：填同样的用户名和密码就是登录。"), x, y, w, AisvTheme.TextMuted) + 16;
		y = Input(b, hits, Tr.Get("account.userName", "用户名"), _inputs[0], x, y, w);
		y = Input(b, hits, Tr.Get("account.password", "密码"), _inputs[1], x, y, w, reveal: true);
		y = Message(b, x, y, w);
		Buttons(b, hits, x, y, Tr.Get("account.doEnter", "开始"), Submit, Tr.Get("account.doLater", "稍后"), Later);
	}

	private void DrawPassword(SpriteBatch b, Hits hits, int x, int y, int w)
	{
		y = Paragraph(b, Tr.Get("account.subPassword", "改完以后，其他电脑上的登录会退出。"), x, y, w, AisvTheme.TextMuted) + 16;
		y = Input(b, hits, Tr.Get("account.oldPassword", "原密码"), _inputs[0], x, y, w);
		y = Input(b, hits, Tr.Get("account.newPassword", "新密码"), _inputs[1], x, y, w, reveal: true);
		y = Input(b, hits, Tr.Get("account.confirmPassword", "再输一遍新密码"), _inputs[2], x, y, w);
		y = Message(b, x, y, w);
		Buttons(b, hits, x, y, Tr.Get("account.doPassword", "改密码"), Submit, Tr.Get("account.cancel", "取消"), () => Show(View.Status));
	}

	private void DrawStatus(SpriteBatch b, Hits hits, int x, int y, int w)
	{
		ServerAccount account = Ctx.Account!;
		string text = account.StatusText("");
		int lines = AisvTheme.Wrap(text, w - 40).Split('\n').Length;
		Rectangle card = new Rectangle(x, y, w, lines * AisvTheme.LineHeight() + 36);
		AisvTheme.DrawCard(b, card);
		Paragraph(b, text, x + 20, y + 18, w - 40, AisvTheme.Text);
		y = card.Bottom + 12;
		y = Message(b, x, y, w);
		Buttons(b, hits, x, y, Tr.Get("account.changePassword", "改密码"), () => Show(View.Password), Tr.Get("account.logout", "退出登录"), Logout);
	}

	private int Input(SpriteBatch b, Hits hits, string label, TextArea input, int x, int y, int w, bool reveal = false)
	{
		AisvTheme.DrawText(b, label, new Vector2(x + 2, y), AisvTheme.TextMuted, 0.8f);
		if (reveal)
		{
			string text = _revealed ? Tr.Get("account.hide", "隐藏密码") : Tr.Get("account.reveal", "显示密码");
			Rectangle r = new Rectangle(x + w - 130, y - 6, 130, 34);
			AisvTheme.Button(b, r, text, hover: AisvTheme.Hover(r));
			hits.Add(r, ToggleReveal);
		}
		input.Bounds = new Rectangle(x, y + 30, w, AisvTheme.LineHeight() + 24);
		input.Locked = _busy;
		input.Draw(b);
		hits.Add(input.Bounds, () =>
		{
			if (!_busy)
			{
				_fields.Focus(input);
			}
		});
		return input.Bounds.Bottom + 14;
	}

	private int Message(SpriteBatch b, int x, int y, int w)
	{
		if (_busy)
		{
			return Paragraph(b, Tr.Get("account.connecting", "正在连接服务器……"), x, y, w, AisvTheme.TextMuted) + 10;
		}
		return _error.Length > 0 ? Paragraph(b, _error, x, y, w, AisvTheme.Danger) + 10 : y;
	}

	private void Buttons(SpriteBatch b, Hits hits, int x, int y, string primary, Action onPrimary, string secondary, Action onSecondary)
	{
		Rectangle first = new Rectangle(x, y, 200, 52);
		Rectangle second = new Rectangle(first.Right + 16, y, 200, 52);
		AisvTheme.Button(b, first, primary, primary: true, hover: AisvTheme.Hover(first), disabled: _busy);
		AisvTheme.Button(b, second, secondary, hover: AisvTheme.Hover(second), disabled: _busy);
		if (!_busy)
		{
			hits.Add(first, onPrimary);
			hits.Add(second, onSecondary);
		}
	}

	private static int Paragraph(SpriteBatch b, string text, int x, int y, int w, Color color)
	{
		string wrapped = AisvTheme.Wrap(text, w);
		b.DrawString(Game1.smallFont, wrapped, new Vector2(x, y), color);
		return y + wrapped.Split('\n').Length * AisvTheme.LineHeight();
	}
}
