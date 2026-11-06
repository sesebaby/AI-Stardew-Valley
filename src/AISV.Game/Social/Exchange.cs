using AISV.Core.Dialogue;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>
/// 聊完的一轮：村民、玩家说的话、显示出来的反应、是否保密、AI 的完整结果、玩家回应的那句、今天的开场白、
/// 附带的提议、这轮之前的有效心数。
/// </summary>
public sealed record Exchange(NPC Npc, string PlayerText, string ShownReaction, bool KeepSecret, NpcReaction Reaction, string AnsweredLine, string OpeningLine, string? ProposedAction, int HeartsBefore, string? Said = null)
{
	/// <summary>玩家的原话（不带记录用的标签）。</summary>
	public string Words => Said ?? PlayerText;
}

/// <summary>节日上的一次邀请：节日、村民、玩家的话、写进日记的那段、AI 的答复、计入的好感、邀请前的有效心数、村民刚说的那句。</summary>
public sealed record FestivalInviteOutcome(AISV.Core.Festivals.FestivalPack Festival, NPC Npc, string PlayerText, string Shown, AISV.Core.Festivals.FestivalInviteDecision Decision, int Points, int HeartsBefore, string AnsweredLine);
