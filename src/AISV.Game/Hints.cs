using StardewValley;

namespace AISV.Game;

/// <summary>屏幕左下角的提示条，和"第一次碰到时只说一次"的用法提示。</summary>
public static class Hints
{
	public static void Hud(string text, int ms = 6000)
	{
		if (string.IsNullOrWhiteSpace(text) || Game1.player == null)
		{
			return;
		}
		Game1.addHUDMessage(new HUDMessage(text) { noIcon = true, timeLeft = ms });
	}

	/// <summary>这条用法提示还没给过这位玩家时显示，并记下来（存在存档里，换存档会再提示一次）。</summary>
	public static bool Once(string flag, string text, int ms = 8000)
	{
		if (Game1.player == null || !Game1.player.mailReceived.Add("AISV_" + flag))
		{
			return false;
		}
		Hud(text, ms);
		return true;
	}

	/// <summary>这条用法提示是不是第一次（记下来，但不弹提示条，给自己画提示的地方用）。</summary>
	public static bool First(string flag) => Game1.player != null && Game1.player.mailReceived.Add("AISV_" + flag);
}
