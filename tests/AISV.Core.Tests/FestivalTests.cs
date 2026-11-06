using System.IO;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Festivals;
using Xunit;

namespace AISV.Core.Tests;

public class FestivalTests
{
	private static string PacksDir()
	{
		string d = System.AppContext.BaseDirectory;
		while (!Directory.Exists(Path.Combine(d, "src", "AISV.Mod")))
		{
			d = Path.GetDirectoryName(d)!;
		}
		return Path.Combine(d, "src", "AISV.Mod", "assets", "festivals");
	}

	[Fact]
	public void AllPacksLoadAndCoverTheCalendar()
	{
		var packs = FestivalPacks.Load(PacksDir(), NullLog.Instance);
		Assert.Equal(10, packs.Count);
		Assert.All(packs, p =>
		{
			Assert.False(string.IsNullOrWhiteSpace(p.Name));
			Assert.False(string.IsNullOrWhiteSpace(p.Direction));
			Assert.False(string.IsNullOrWhiteSpace(p.Gossip.Summary));
		});
		Assert.Equal("beach_volleyball_championship", packs[0].Id.Length > 0 ? packs.First(p => p.Priority == 20 && p.Action != null).Id : "");
		FestivalPack dance = packs.Single(p => p.Id == "flower_dance");
		Assert.Equal(FestivalAction.Dance, dance.Action!.Kind);
		Assert.Contains("{{VILLAGER}}", dance.Action.Prompt);
		foreach ((string season, int day, string id) in new[] { ("spring", 13, "egg_festival"), ("spring", 24, "flower_dance"), ("summer", 11, "luau"), ("summer", 28, "moonlight_jellies"), ("fall", 16, "stardew_valley_fair"), ("fall", 27, "spirits_eve"), ("winter", 8, "festival_of_ice"), ("winter", 25, "feast_of_winter_star") })
		{
			Assert.Equal(id, packs.First(p => p.Detection.Matches(season, day, "Temp", "Temp", null)).Id);
		}
		Assert.DoesNotContain(packs, p => p.Detection.Matches("spring", 2, "Temp", "Temp", null));
	}

	[Fact]
	public void DetectionNeedsDateLocationAndMarkers()
	{
		FestivalDetection d = new FestivalDetection { Season = "summer", Day = 9, Locations = { "IslandSouth" }, FestivalDataMarkers = { ["k"] = "v" } };
		Assert.True(d.Matches("Summer", 9, "IslandSouth", "IslandSouth", new System.Collections.Generic.Dictionary<string, string> { ["k"] = "v" }));
		Assert.False(d.Matches("summer", 9, "IslandSouth", "IslandSouth", null));
		Assert.False(d.Matches("summer", 9, "IslandSouth", "IslandSouth", new System.Collections.Generic.Dictionary<string, string> { ["k"] = "x" }));
		Assert.False(d.Matches("summer", 10, "IslandSouth", "IslandSouth", new System.Collections.Generic.Dictionary<string, string> { ["k"] = "v" }));
		FestivalDetection any = new FestivalDetection { MatchAnyDate = true, FestivalDataMarkers = { ["k"] = "v" } };
		Assert.True(any.Matches("fall", 1, "Anywhere", "Anywhere", new System.Collections.Generic.Dictionary<string, string> { ["k"] = "v" }));
	}

	[Fact]
	public void InviteDecisionIsClampedAndCleaned()
	{
		FestivalInviteDecision? d = FestivalInviteDecision.Parse("```json\n{\"agrees\":true,\"code\":\"too_distant\",\"says\":\"好呀，跳吧。\",\"feeling\":\"开心\",\"warmth\":-5}\n```", 100, NullLog.Instance);
		Assert.NotNull(d);
		Assert.Equal("unclear", d!.ReasonCode);
		Assert.Equal(0, d.FriendshipPoints);
		Assert.Equal("happy", d.Emotion);
		FestivalInviteDecision? no = FestivalInviteDecision.Parse("{\"agrees\":false,\"code\":\"nonsense\",\"says\":\"算了。\",\"warmth\":99}", 100, NullLog.Instance);
		Assert.Equal("not_ready", no!.ReasonCode);
		Assert.Equal(40, no.FriendshipPoints);
		FestivalInviteDecision? empty = FestivalInviteDecision.Parse("{\"agrees\":true,\"says\":\"\"}", 100, NullLog.Instance);
		Assert.NotEqual("", empty!.Reaction);
		Assert.Null(FestivalInviteDecision.Parse("{\"code\":\"close_enough\",\"says\":\"好\"}", 100, NullLog.Instance));
		Assert.Null(FestivalInviteDecision.Parse("{\"agrees\":\"yes\",\"says\":\"好\"}", 100, NullLog.Instance));
		Assert.Null(FestivalInviteDecision.Parse("不是 JSON", 100, NullLog.Instance));
	}

	[Fact]
	public void TeamInvitationUsesItsOwnProtocol()
	{
		FestivalInviteDecision? d = FestivalInviteDecision.Parse("{\"agrees\":true,\"code\":\"close_enough\",\"says\":\"走，组队！\",\"warmth\":15,\"exception\":true}", 100, NullLog.Instance, InviteProtocol.Invitation);
		Assert.Equal("close_enough", d!.ReasonCode);
		Assert.True(d.BelowGateOverride);
		Assert.Equal("unclear", FestivalInviteDecision.Parse("{\"agrees\":true,\"code\":\"too_distant\",\"says\":\"好\"}", 100, NullLog.Instance, InviteProtocol.Invitation)!.ReasonCode);
		Assert.Equal("unclear", FestivalInviteDecision.Parse("{\"agrees\":true,\"code\":\"our_bond\",\"says\":\"好\"}", 100, NullLog.Instance)!.ReasonCode);
		FestivalInviteDecision? no = FestivalInviteDecision.Parse("{\"agrees\":false,\"code\":\"too_distant\",\"says\":\"不了\",\"exception\":true}", 100, NullLog.Instance, InviteProtocol.Invitation);
		Assert.Equal("too_distant", no!.ReasonCode);
		Assert.False(no.BelowGateOverride);
	}

	[Fact]
	public void OnlyPlainChatIsReplaced()
	{
		Assert.True(FestivalLine.IsPlainChat("今天的花真好看。"));
		Assert.False(FestivalLine.IsPlainChat("要跳舞吗？$q 123 foo"));
		Assert.False(FestivalLine.IsPlainChat("$r 1 2 ok"));
		Assert.False(FestivalLine.IsPlainChat(""));
		Assert.Equal(260, FestivalLine.Normalize(new string('字', 400)).Length);
	}
}
