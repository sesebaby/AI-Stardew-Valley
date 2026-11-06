using System;
using System.Collections.Generic;
using StardewValley;

namespace AISV.Integrations.Squad;

/// <summary>The Stardew Squad 的 API（带 API 的版本才有）。SMAPI 按方法名和签名代理，要和那边一致。</summary>
public interface ISquadApi
{
	bool IsRecruited(NPC npc);

	bool IsControlled(NPC npc);

	IReadOnlyList<string> GetMemberNames();

	bool Recruit(NPC npc, Farmer recruiter, bool silent);

	bool Dismiss(NPC npc, bool silent);

	void SetRecruitGate(Func<NPC, Farmer, bool>? gate);

	void SetTaskOverride(Func<NPC, string, bool?>? taskOverride);

	void SetTaskModeOverride(Func<NPC, string, string?>? modeOverride);

	void AddRecruitedHandler(Action<NPC> handler);

	void AddDismissedHandler(Action<NPC> handler);

	bool SetWorkSite(NPC npc, string locationName, int x, int y, int radius);

	void ClearWorkSite(NPC npc);

	bool DismissInPlace(NPC npc);
}

/// <summary>新一点的 Squad 才有：队员说话改用我们的头顶气泡。旧版拿不到就用它自己的。</summary>
public interface ISquadBubbleApi
{
	void SetBubble(Action<NPC, string>? show);
}
