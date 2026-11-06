using AISV.Core.Ai;
using StardewModdingAPI;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>有 AI 请求因为没登录发不出去：玩家能行动时打开账号页。点“稍后”当天不再弹。</summary>
public sealed class AccountPrompt
{
	private readonly ShellContext _ctx;

	public AccountPrompt(ShellContext ctx)
	{
		_ctx = ctx;
	}

	public void OnUpdateTicked()
	{
		ServerAccount? account = _ctx.Account;
		if (account?.LoginRequested != true || !Context.IsWorldReady)
		{
			return;
		}
		if (!_ctx.UsesServer())
		{
			account.ConsumeLoginRequest();
			return;
		}
		if (!Context.IsPlayerFree || Game1.eventUp || Game1.activeClickableMenu != null)
		{
			return;
		}
		account.ConsumeLoginRequest();
		ShellMenu menu = new ShellMenu(_ctx, ShellMenu.Tab.Settings);
		Game1.activeClickableMenu = menu;
		menu.OpenAccount(prompted: true);
	}
}
