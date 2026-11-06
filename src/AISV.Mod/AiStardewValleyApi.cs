using AISV.Core.Social.Gossip;
using StardewModdingAPI;

namespace AISV.Mod;

/// <summary>给其他 mod 用的接口：通过 SMAPI 的 <c>ModRegistry.GetApi</c> 取得。</summary>
public interface IAiStardewValleyApi
{
	/// <summary>
	/// 交一条今天发生的事，当晚整理八卦时一起考虑。只有房主端在存档里才收；玩家关掉“接受其他 mod 的八卦”或今天已满时也不收。
	/// </summary>
	/// <param name="text">发生了什么，一句话。</param>
	/// <param name="ownerModId">交这条素材的 mod 的 UniqueID。</param>
	/// <returns>收下了返回 true。</returns>
	bool AddRumorNote(string text, string ownerModId);
}

public sealed class AiStardewValleyApi : IAiStardewValleyApi
{
	private readonly GossipBook _gossip;

	internal AiStardewValleyApi(GossipBook gossip)
	{
		_gossip = gossip;
	}

	public bool AddRumorNote(string text, string ownerModId) => Context.IsWorldReady && Context.IsMainPlayer && _gossip.AddThirdPartyNote(text, ownerModId);
}
