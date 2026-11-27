using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AISV.Core.Social;
using AISV.Core.Social.Gossip;
using AISV.Core.Dialogue;
using AISV.Game.Dialogue;
using AISV.Game.Social;
using StardewModdingAPI;

namespace AISV.Game.Net;

/// <summary>联机消息的名字和内容，以及全村共用的那几样（心情、今天的聊天、小事八卦）怎么接到 <see cref="NetSync"/> 上。</summary>
public static class NetOps
{
	public const string Mood = "mood";

	public const string Talk = "talk";

	public const string LowKey = "lowkey";

	public sealed record MoodChange(string Npc, int Delta);

	public sealed record LowKeyGossip(string Text, List<string> Knowers, string Origin);

	public static void Wire(NetSync net, SocialSystem social)
	{
		// 房主发给农场帮手：今天的心情、今天谁和谁聊过。
		net.Channel("moods", () => JsonSerializer.Serialize(social.Moods.All.OrderBy(m => m.Npc).ToList()), json => social.Moods.ReplaceAll(JsonSerializer.Deserialize<List<NpcMood>>(json) ?? new List<NpcMood>()));
		net.Channel("talks", () => JsonSerializer.Serialize(social.Gossip.TodayInteractions), json => social.Gossip.ReplaceShared(JsonSerializer.Deserialize<List<DailyInteraction>>(json) ?? new List<DailyInteraction>()));

		// 农场帮手这边的改动交给房主（自己这边也先改上，下一次同步时换成房主的）。
		social.Moods.Adjusted += (npc, delta) =>
		{
			if (net.Linked)
			{
				net.ToHost(Mood, new MoodChange(npc, delta));
			}
		};
		social.Gossip.Interacted += i =>
		{
			if (net.Linked)
			{
				net.ToHost(Talk, i);
			}
		};

		net.On<MoodChange>(Mood, (_, m) =>
		{
			if (Context.IsMainPlayer)
			{
				social.Moods.Adjust(m.Npc, m.Delta);
			}
		});
		net.On<DailyInteraction>(Talk, (_, i) =>
		{
			if (Context.IsMainPlayer)
			{
				social.Gossip.AddRemote(i);
			}
		});
		net.On<LowKeyGossip>(LowKey, (_, g) =>
		{
			if (Context.IsMainPlayer)
			{
				GameDate today = GameState.Today;
				social.Gossip.AddLowKey(g.Text, g.Knowers, g.Origin, g.Origin + "-" + today.TotalDays + "-" + g.Text.GetHashCode(), today);
			}
		});
	}
}
