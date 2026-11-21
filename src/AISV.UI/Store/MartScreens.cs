using System;
using AISV.UI.Apps;
using AISV.UI.Dialogue;
using AISV.UI.Shell;
using StardewValley;
using StardewValley.Objects;

namespace AISV.UI.Store;

/// <summary>小超市用到的几个界面入口（给 ModEntry 接线用）。</summary>
public static class MartScreens
{
	public static void OpenApp(ShellContext ctx, bool guide = false)
	{
		ShellMenu menu = new ShellMenu(ctx);
		Game1.activeClickableMenu = menu;
		((IShellHost)menu).Push(new FarmerMartPage(ctx, guide));
	}

	public static void OpenShelf(Furniture shelf) => Game1.activeClickableMenu = new ShelfMenu(shelf);

	public static void OpenCheckout(AISV.Game.Store.CheckoutRequest req) => Game1.activeClickableMenu = new CheckoutMenu(req);

	/// <summary>上门理论时"讲道理"：上面是对方的话，下面写你的回应；关掉或不回就当没说。</summary>
	public static void AskArgument(NPC npc, string line, Action<string?> done)
	{
		bool answered = false;
		ReplyMenu menu = new ReplyMenu(npc, npc.displayName, line, "angry", multiTurn: false, Game1.player.getFriendshipHeartLevelForNPC(npc.Name), kid: true);
		menu.Submitted += (text, _, _) =>
		{
			answered = true;
			menu.CloseMenu();
			done(text);
		};
		menu.Cancelled += () =>
		{
			if (!answered)
			{
				answered = true;
				done(null);
			}
		};
		menu.Skipped += () =>
		{
			if (!answered)
			{
				answered = true;
				done(null);
			}
		};
		Game1.activeClickableMenu = menu;
	}
}
