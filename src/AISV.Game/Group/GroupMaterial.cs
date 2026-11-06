using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Storage;
using AISV.Core.World;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewValley;

namespace AISV.Game.Group;

/// <summary>主线程：把群聊成员和大家共有的背景整理成提示词资料。</summary>
public sealed class GroupMaterial
{
	private readonly NpcDirectory _npcs;

	private readonly Relationships _relationships;

	private readonly ConversationLog _log;

	private readonly Func<DocumentStore?> _store;

	public SocialSystem? Social { get; set; }

	public Func<string, bool, string>? Awareness { get; set; }

	/// <summary>某位村民最近亲眼看见农夫做的事（id、他眼里的说法）。</summary>
	public Func<string, int, IReadOnlyList<(string Id, string Text)>>? FarmerSightings { get; set; }

	public FarmerTopics Topics { get; } = new FarmerTopics();

	/// <summary>闲聊要不要从农夫最近的事聊起：从这几位知道的新鲜事里挑，渠道（phone / map / scene）各自冷却。</summary>
	public string FarmerTopic(IEnumerable<string> members, string channel)
	{
		string player = Game1.player?.Name ?? "";
		int today = GameState.Today.TotalDays;
		List<(string Id, string Text)> candidates = new List<(string, string)>();
		foreach (string name in members)
		{
			try
			{
				string who = Display(name);
				candidates.AddRange((FarmerSightings?.Invoke(name, FarmerTopics.FreshDays) ?? Array.Empty<(string, string)>())
					.Select(x => (x.Id, $"{who}亲眼看见的（下面的“你”就是{who}）：{x.Text}")));
				if (Social != null && player.Length > 0)
				{
					candidates.AddRange(Social.Gossip.Feed()
						.Where(e => today - e.TotalDay <= FarmerTopics.FreshDays && e.Fact.Contains(player, StringComparison.OrdinalIgnoreCase)
							&& (e.KnownTo.Contains(name, StringComparer.OrdinalIgnoreCase) || string.Equals(e.StartedBy, name, StringComparison.OrdinalIgnoreCase)))
						.Select(e => ("gossip:" + (e.Key.Length > 0 ? e.Key : e.Fact), "听说" + e.Fact)));
				}
			}
			catch (Exception)
			{
			}
		}
		// 同一件事几个人都知道时合成一条，各人的说法都带上，免得当时在场的人被写成头一回听说。
		List<(string Id, string Text)> merged = candidates
			.GroupBy(c => c.Id)
			.Select(g => (g.Key, string.Join("；", g.Select(c => c.Text).Distinct())))
			.ToList();
		return Topics.Pick(channel, today, merged, Random.Shared.NextDouble());
	}

	public GroupMaterial(NpcDirectory npcs, Relationships relationships, ConversationLog log, Func<DocumentStore?> store)
	{
		_npcs = npcs;
		_relationships = relationships;
		_log = log;
		_store = store;
	}

	public NpcDirectory Npcs => _npcs;

	public Relationships Relationships => _relationships;

	public ConversationLog Log => _log;

	public GroupMember Member(string name, NPC? npc, bool phone = false)
	{
		string player = Game1.player?.Name ?? "";
		int hearts = _relationships.EffectiveHearts(name);
		GameDate today = GameState.Today;
		SocialSystem? social = Social;
		List<string> talks = _log.Recent(name, 40)
			.Where(e => string.IsNullOrWhiteSpace(e.Farmer) || string.Equals(e.Farmer, player, StringComparison.OrdinalIgnoreCase))
			.TakeLast(GroupRules.RecentTalks)
			.Select(GroupRules.DescribeTalk)
			.Where(t => t.Length > 0)
			.ToList();
		return new GroupMember(
			name,
			Safe(() => _npcs.Persona(name, npc).Personality),
			hearts,
			talks,
			Safe(() => social?.Moods.PromptBlock(name)),
			Safe(() => social?.Feelings.PromptBlock(name, _npcs.Names.Display(name), hearts, today.TotalDays)),
			_log.Count(name) > 0 ? Safe(() => social?.Gossip.MemoryFor(name, player, today.TotalDays, GroupRules.GossipPerMember)) : "",
			phone ? "" : Safe(() => Awareness?.Invoke(name, false)));
	}

	public string Shared()
	{
		string player = Game1.player?.Name ?? "";
		string profile = Safe(() => PlayerProfileBlock.Shared(player, _store()?.Get(PlayerProfile.Path, PlayerProfile.Doc)));
		string world = Safe(() => WorldFacts.Build(GameState.World(), null, _npcs.IsEnabled, display: _npcs.Names.Display));
		string sve = _npcs.Eligibility.SveContentEnabled ? DialoguePrompts.SveWorld : "";
		return DialoguePrompts.Join(profile, world, sve);
	}

	public GroupRequest Request(IReadOnlyList<NPC> members, GameLocation location, GroupSetting setting, IReadOnlyList<GroupLine> transcript, string? playerText, bool final, string shared)
	{
		List<string> names = members.Select(n => n.Name).ToList();
		return new GroupRequest
		{
			PlayerName = Game1.player?.Name ?? "",
			Members = members.Select(n => Member(n.Name, n, setting == GroupSetting.Phone)).ToList(),
			Display = names.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(n => n, n => _npcs.Names.Display(n), StringComparer.OrdinalIgnoreCase),
			Setting = setting,
			Location = location.NameOrUniqueName,
			Time = Game1.timeOfDay,
			Season = Game1.currentSeason ?? "",
			Day = Game1.dayOfMonth,
			Year = Game1.year,
			Weather = Weather(location),
			Shared = shared,
			Relations = GroupPrompts.Relations(names),
			Transcript = transcript.ToList(),
			PlayerText = playerText,
			Final = final
		};
	}

	public static string Weather(GameLocation location)
	{
		if (!location.IsOutdoors)
		{
			return "室内";
		}
		if (Game1.IsSnowingHere(location))
		{
			return "下雪";
		}
		if (Game1.IsLightningHere(location))
		{
			return "雷雨";
		}
		return Game1.IsRainingHere(location) ? "下雨" : "晴";
	}

	public string Display(string name) => _npcs.Names.Display(name);

	private static string Safe(Func<string?> build)
	{
		try
		{
			return (build() ?? "").Trim();
		}
		catch (Exception)
		{
			return "";
		}
	}
}
