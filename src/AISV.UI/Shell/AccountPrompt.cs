using AISV.Core.Ai;
using StardewModdingAPI;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>有 AI 请求因为没登录发不出去：外壳开着就在里面打开账号页，否则等玩家能行动时打开。</summary>
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
		if (Game1.activeClickableMenu is ShellMenu open)
		{
			account.ConsumeLoginRequest();
			open.PromptAccount();
			return;
		}
		if (!Context.IsPlayerFree)
		{
			return;
		}
		account.ConsumeLoginRequest();
		ShellMenu menu = new ShellMenu(_ctx, ShellMenu.Tab.Settings);
		Game1.activeClickableMenu = menu;
		menu.OpenAccount(prompted: true);
	}
}
