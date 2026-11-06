using System;

namespace AISV.Core.Dialogue;

/// <summary>聊天花时间：每聊一轮记几分钟，菜单都关了再一格一格（10 分钟）推钟，最晚推到 25:50（不替游戏让玩家晕倒）。</summary>
public static class TalkTime
{
	public const int Latest = 2550;

	/// <summary>钟一次走 10 分钟；不足 10 分钟的留着，下次聊天凑够了再走。</summary>
	public const int Step = 10;

	/// <summary>攒着没推的最多 3 小时。</summary>
	public const int MaxPending = 180;

	public static readonly int[] Choices = { 0, 5, 10, 15, 20, 30 };

	public static int Accrue(int pending, int minutes) => Math.Clamp(pending + Math.Max(0, minutes), 0, MaxPending);

	/// <summary>游戏时间（如 1350）加 10 分钟。</summary>
	public static int Next(int time) => time % 100 >= 50 ? time - time % 100 + 100 : time + 10;

	public static bool CanStep(int time) => Next(time) <= Latest;

	public static bool Ready(int pending) => pending >= Step;

	public static int AfterStep(int pending) => Math.Max(0, pending - Step);
}
