using AISV.Core.Text;
using StardewModdingAPI;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>还没填我的设定时提醒一次：新开的农场第一天直接打开，中途装上 mod 的在屏幕上提示一句。</summary>
public sealed class ProfilePrompt
{
	private const string Flag = "AISV_ProfilePrompted";

	private readonly ShellContext _ctx;

	private bool _pending;

	public ProfilePrompt(ShellContext ctx)
	{
		_ctx = ctx;
	}

	public void OnDayStarted() => _pending = Context.IsMainPlayer && !Game1.player.mailReceived.Contains(Flag) && !ProfilePage.Filled(_ctx);

	public void OnUpdateTicked()
	{
		if (!_pending || !Context.IsPlayerFree || Game1.eventUp || Game1.activeClickableMenu != null)
		{
			return;
		}
		_pending = false;
		Game1.player.mailReceived.Add(Flag);
		if (Game1.stats.DaysPlayed <= 1)
		{
			ShellMenu menu = new ShellMenu(_ctx, ShellMenu.Tab.Settings);
			Game1.activeClickableMenu = menu;
			menu.OpenProfile(welcome: true);
		}
		else
		{
			string key = _ctx.Config().KeyAboutMe?.Trim() ?? "";
			string text = key.Length > 0 ? Tr.Get("profile.remind", "还没写我的设定，按 {{key}} 介绍一下自己吧", ("key", key))
				: Tr.Get("profile.remindMenu", "还没写我的设定，在 设置 → 设定 → 我的设定 里介绍一下自己吧");
			Game1.addHUDMessage(new HUDMessage(text) { noIcon = true, timeLeft = 6000 });
		}
	}

	public void Reset() => _pending = false;
}
