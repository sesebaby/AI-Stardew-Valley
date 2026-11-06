using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>
/// 给玩家的小提示（八卦出来了、日记更新了、晚上整理没成功……）。
/// 后台结果回到主线程时先排队，下一帧合成一句显示；同样的话只说一次。
/// </summary>
public static class Notices
{
	public static readonly Color Normal = new Color(255, 215, 0);

	public static readonly Color Warning = new Color(255, 165, 0);

	private static readonly List<(string Text, Color Color)> Pending = new List<(string, Color)>();

	public static void Queue(string text) => Queue(text, Normal);

	public static void Queue(string text, Color color)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			Pending.Add((text.Trim(), color));
		}
	}

	/// <summary>马上显示一句（八卦正文这类要单独一行的）。</summary>
	public static void ShowNow(string text, Color? color = null)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			Show(text.Trim(), color ?? Normal);
		}
	}

	/// <summary>每帧调：把排队的按颜色合成一句显示。</summary>
	public static void Flush()
	{
		if (Pending.Count == 0)
		{
			return;
		}
		List<(string Text, Color Color)> list = Pending.DistinctBy(p => (p.Text.ToLowerInvariant(), p.Color.PackedValue)).ToList();
		Pending.Clear();
		foreach (IGrouping<uint, (string Text, Color Color)> group in list.GroupBy(p => p.Color.PackedValue))
		{
			Show("AI星露谷：" + string.Join("；", group.Select(p => p.Text.TrimEnd('。'))) + "。", group.First().Color);
		}
	}

	public static void Clear() => Pending.Clear();

	private static void Show(string text, Color color)
	{
		if (Game1.chatBox != null)
		{
			Game1.chatBox.addMessage(text, color);
		}
		else
		{
			Game1.addHUDMessage(new HUDMessage(text, 3));
		}
	}
}
