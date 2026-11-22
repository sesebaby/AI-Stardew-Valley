using System;
using AISV.Core.Text;
using StardewModdingAPI.Events;
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
		Game1.addHUDMessage(new HUDMessage(Fit(text)) { noIcon = true, timeLeft = ms });
	}

	/// <summary>HUD 只放一句短的：太长就截到第一句，还放不下就截断加"…"（详细的放进手机 > 使用帮助）。</summary>
	private static string Fit(string text)
	{
		int max = Math.Max(300, Game1.uiViewport.Width - 260);
		bool fits(string t) => Game1.smallFont.MeasureString(t).X <= max;
		if (fits(text))
		{
			return text;
		}
		int stop = text.IndexOfAny(new[] { '。', '！', '？', '.', '!', '?' });
		if (stop > 0 && fits(text[..(stop + 1)]))
		{
			return text[..(stop + 1)];
		}
		string cut = text;
		while (cut.Length > 1 && !fits(cut + "…"))
		{
			cut = cut[..^1];
		}
		return cut + "…";
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

	/// <summary>新玩法第一次出现：只弹一行，指向手机里的“使用帮助”（详细用法都在那里）。</summary>
	public static string TipText(string topic) => Tr.Get("hint.tip", "新玩法：{{topic}}（用法见 手机 > 使用帮助）", ("topic", topic));

	public static bool Tip(string flag, string topic) => Once(flag, TipText(topic), 6000);

	/// <summary>介绍新玩法的信（读过就不再寄）。</summary>
	public const string GuideMail = "AIStardewValley.Core_Guide";

	public static void EditMail(AssetRequestedEventArgs e)
	{
		if (e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
		{
			e.Edit(asset => asset.AsDictionary<string, string>().Data[GuideMail] = Tr.Get("mail.guide", "@：\nAI星露谷 加了几样新玩法：\n- 点配偶可以选聊天还是亲吻\n- 可以设成由你先开口\n- 宠物会冒心里话\n- 孩子能聊天、有自己的角色卡，偶尔还会跑出去玩\n- 配偶婚后每天有自己的安排\n- 可以在 Robin 那里建“农夫小超市”，和 Pierre、Joja 抢生意\n\n每样怎么用，都写在 手机 > 应用 > 使用帮助 里。不喜欢的都能在设置里关掉。\n   -AI星露谷").Replace("\n", "^") + "[#]" + Tr.Get("mail.guideTitle", "AI星露谷的新玩法"));
		}
	}

	/// <summary>还没收到过就放进信箱（每个存档一次）。</summary>
	public static void SendGuide()
	{
		Farmer? p = Game1.player;
		if (p != null && !p.mailReceived.Contains(GuideMail) && !p.mailbox.Contains(GuideMail))
		{
			p.mailbox.Add(GuideMail);
		}
	}

	/// <summary>这条用法提示是不是第一次（记下来，但不弹提示条，给自己画提示的地方用）。</summary>
	public static bool First(string flag) => Game1.player != null && Game1.player.mailReceived.Add("AISV_" + flag);
}
