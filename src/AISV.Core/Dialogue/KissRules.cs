namespace AISV.Core.Dialogue;

/// <summary>
/// 点配偶时要不要先弹"聊天 / 亲吻"圆盘。条件照游戏原版的亲吻（1.6 的 NPC.checkAction）：
/// 结了婚或是室友、10 心以上、昨晚睡在床上、22 点前、不在走路、没在播动作、手里没拿东西、
/// 玩家站在左右两边（站在上下方原版不亲）。今天已经亲过就不弹，直接聊天（原版可以重复亲，但只有第一次加好感）。
/// </summary>
public static class KissRules
{
	public readonly record struct Situation(bool MarriedOrRoommate, int Hearts, bool SleptInBed, int TimeOfDay, bool Moving, bool AnimationPlaying, bool TemporaryMessage, bool HoldingItem, bool KissedToday, float DeltaX, float DeltaY);

	/// <summary>原版亲吻要求玩家站在配偶的左右两边：横向距离不小于纵向距离。</summary>
	public static bool SideBySide(float dx, float dy) => System.Math.Abs(dx) >= System.Math.Abs(dy);

	public static bool CanOffer(Situation s)
	{
		return s.MarriedOrRoommate && s.Hearts > 9 && s.SleptInBed && s.TimeOfDay < 2200 && !s.Moving && !s.AnimationPlaying && !s.TemporaryMessage && !s.HoldingItem && !s.KissedToday && SideBySide(s.DeltaX, s.DeltaY);
	}
}
