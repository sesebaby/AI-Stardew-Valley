using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class CompatTests
{
	private const string List = "{\"INSTRUCTIONS\":\"x\",\"a\":{\"ModId\":\"Author.Whole*\"},\"b\":{\"ModId\":\"Author.Some\",\"NPCs\":[\"Bob\",\"Al?ce\"]},\"c\":{\"modid\":\"Author.Some\",\"npc\":\"Carl\"}}";

	[Theory]
	[InlineData("Author.*", "author.mod", true)]
	[InlineData("A?C", "abc", true)]
	[InlineData("A?C", "abbc", false)]
	[InlineData("*.Mod", "x.y.Mod", true)]
	[InlineData("Exact", "Exactly", false)]
	public void GlobMatches(string pattern, string value, bool expected) => Assert.Equal(expected, OptOutRules.Glob(pattern, value));

	[Fact]
	public void ParsesAndMatchesRules()
	{
		OptOutRules rules = OptOutRules.Parse(List);
		Assert.Equal(2, rules.RuleCount);
		string[] loaded = { "Author.Some", "Author.WholeThing" };
		Assert.Equal("Author.Whole*", rules.MatchNpc("Zed", new[] { "Author.WholeThing" }, loaded));
		Assert.Equal("Author.Whole*", rules.MatchNpc("Author.WholeThing_Zed", Array.Empty<string>(), loaded));
		Assert.Equal("Author.Some", rules.MatchNpc("alice", Array.Empty<string>(), loaded));
		Assert.Equal("Author.Some", rules.MatchNpc("Carl", Array.Empty<string>(), loaded));
		Assert.Null(rules.MatchNpc("Carl", Array.Empty<string>(), new[] { "Other" }));
		Assert.True(rules.NeedsOwnershipScan("Author.WholeThing"));
		Assert.False(rules.NeedsOwnershipScan("Author.Some"));
		Assert.Throws<System.Text.Json.JsonException>(() => OptOutRules.Parse("{\"a\":{\"NPC\":\"x\"}}"));
		Assert.Throws<System.Text.Json.JsonException>(() => OptOutRules.Parse("[]"));
	}

	[Fact]
	public void ScannerFindsNewNpcsAndUsesCache()
	{
		string dir = Path.Combine(Path.GetTempPath(), "aisv-scan-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(dir, "data"));
		try
		{
			File.WriteAllText(Path.Combine(dir, "content.json"), "{ // 注释\n\"Changes\":[{\"Action\":\"EditData\",\"Target\":\"Data/Characters, Data/Other\",\"Entries\":{\"Zed\":{\"Home\":[]},\"Abigail\":{\"Portrait\":\"x\"},\"{{Token}}\":{\"Home\":[]},\"Yan\":{\"DisplayName\":\"Y\",\"Age\":\"Adult\"},\"Wil\":{\"SpawnIfMissing\":\"{{x}}\"}}},{\"Action\":\"EditData\",\"Target\":\"Data/Characters\",\"TargetField\":[\"Abigail\"],\"Entries\":{\"Fake\":{\"Home\":[]}}},],}");
			File.WriteAllText(Path.Combine(dir, "data", "other.json"), "{\"Unrelated\":1}");
			MemoryBackend cache = new MemoryBackend();
			NpcOwnershipScanner scanner = new NpcOwnershipScanner(cache, NullLog.Instance);
			IReadOnlyDictionary<string, string> owners = scanner.Scan(new[] { new ModFolder("A.Pack", dir), new ModFolder("Missing", "/nope") }, out int rescanned);
			Assert.Equal(new[] { "Wil", "Yan", "Zed" }, owners.Keys.OrderBy(k => k));
			Assert.All(owners.Values, v => Assert.Equal("A.Pack", v));
			Assert.Equal(1, rescanned);
			scanner.Scan(new[] { new ModFolder("A.Pack", dir) }, out rescanned);
			Assert.Equal(0, rescanned);
			File.WriteAllText(Path.Combine(dir, "data", "more.json"), "{}");
			scanner.Scan(new[] { new ModFolder("A.Pack", dir) }, out rescanned);
			Assert.Equal(1, rescanned);
			// 另一组 mod 的扫描不会把这一组的缓存冲掉。
			scanner.Scan(new[] { new ModFolder("Other", Path.GetTempPath() + "aisv-none") }, out _);
			scanner.Scan(new[] { new ModFolder("A.Pack", dir) }, out rescanned);
			Assert.Equal(0, rescanned);
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	private static NpcFacts Facts(string name = "Zed", bool builtIn = false, bool sve = false, bool managed = false, bool present = true, string[]? sources = null, string? excluded = null)
		=> new NpcFacts(name, true, builtIn, sve, managed, present, sources ?? Array.Empty<string>(), excluded);

	[Fact]
	public void PolicyOrderMatchesOriginal()
	{
		ModConfig all = new ModConfig();
		Assert.Null(EligibilityPolicy.RejectionReason(Facts("Abigail", builtIn: true, excluded: "x"), all));
		Assert.Equal("disabled-by-player", EligibilityPolicy.RejectionReason(Facts("Abigail", builtIn: true), new ModConfig { OptOutExtras = "Sam\nabigail" }));
		Assert.Equal("not-villager", EligibilityPolicy.RejectionReason(Facts() with { IsVillager = false }, all));
		Assert.Equal("custom-npc-not-present", EligibilityPolicy.RejectionReason(Facts(present: false), all));
		Assert.Null(EligibilityPolicy.RejectionReason(Facts(managed: true, excluded: "x"), all));
		Assert.Equal("ai-mod-excluded:Author.*", EligibilityPolicy.RejectionReason(Facts(excluded: "Author.*"), all));
		Assert.Equal("vanilla-only", EligibilityPolicy.RejectionReason(Facts(), new ModConfig { ModVillagerScope = NpcModActivationMode.VanillaOnly }));
		Assert.Null(EligibilityPolicy.RejectionReason(Facts(sve: true), new ModConfig { ModVillagerScope = NpcModActivationMode.VanillaAndSve }));
		ModConfig selected = new ModConfig { ModVillagerScope = NpcModActivationMode.SelectedMods, ModVillagerPicks = new List<string> { "A.Pack" } };
		Assert.Null(EligibilityPolicy.RejectionReason(Facts(sources: new[] { "a.pack" }), selected));
		Assert.Equal("npc-mod-not-selected", EligibilityPolicy.RejectionReason(Facts(sources: new[] { "B" }), selected));
		Assert.Equal("unknown-origin-disabled", EligibilityPolicy.RejectionReason(Facts(), selected));
		selected.UnknownVillagersOn = true;
		Assert.Null(EligibilityPolicy.RejectionReason(Facts(), selected));
	}

	[Fact]
	public async Task EligibilityUsesOwnersOptOutAndExpulsion()
	{
		FakeHttp fake = new FakeHttp().Reply(HttpStatusCode.OK, List);
		MemoryBackend cache = new MemoryBackend();
		OptOutList optOut = new OptOutList(new HttpClient(fake), cache, new NpcOwnershipScanner(cache, NullLog.Instance), NullLog.Instance);
		ModConfig config = new ModConfig();
		NpcEligibility e = new NpcEligibility(() => config, optOut, NullLog.Instance);
		e.SetLoadedMods(new[] { "Author.Some", "Author.WholeThing", "A.Pack" });
		e.SetOwners(new Dictionary<string, string> { ["Zed"] = "A.Pack", ["Quinn"] = "Author.WholeThing" });
		e.SetWorld(new NpcEligibility.World(new HashSet<string> { "Zed", "Bob", "Quinn" }, new HashSet<string>(), new HashSet<string>()));
		Assert.True(e.IsEnabled("Bob"));
		await optOut.RefreshAsync("http://wiki.test/list", Array.Empty<ModFolder>());
		Assert.Equal("loaded", optOut.Status);
		Assert.NotNull(cache.Read("ai-opt-out.json"));
		Assert.False(e.IsEnabled("Bob"));
		Assert.False(e.IsEnabled("Quinn"));
		Assert.True(e.IsEnabled("Zed"));
		Assert.False(e.IsEnabled("NotHere"));
		Assert.True(e.IsEnabled("Abigail"));
		Assert.Equal("A.Pack", e.OwnerModId("Zed"));
		Assert.Equal("A.Pack", e.OwnerModId("A.Pack_Someone"));
		Assert.False(e.IsModAllowed("Author.WholeThing"));
		Assert.False(e.SveContentEnabled);
		e.SetLoadedMods(new[] { "FlashShifter.StardewValleyExpandedCP", "FlashShifter.SVECode" });
		Assert.True(e.SveInstalled);
		Assert.True(e.SveContentEnabled);
		e.SetLoadedMods(new[] { "Author.Some", "Author.WholeThing", "A.Pack" });
		e.SetExpelledCheck(n => n == "Abigail");
		Assert.False(e.IsEnabled("Abigail"));

		// 下次启动连不上网：先用缓存。
		OptOutList offline = new OptOutList(new HttpClient(new FakeHttp().Throw(new HttpRequestException("down"))), cache, new NpcOwnershipScanner(cache, NullLog.Instance), NullLog.Instance);
		offline.SetLoadedMods(new[] { "Author.Some" });
		await offline.RefreshAsync("http://wiki.test/list", Array.Empty<ModFolder>());
		Assert.Equal("cached", offline.Status);
		Assert.Equal("Author.Some", offline.MatchNpc("Bob", Array.Empty<string>()));

		await offline.RefreshAsync("", Array.Empty<ModFolder>());
		Assert.Equal("empty", offline.Status);
		Assert.Null(offline.MatchNpc("Bob", Array.Empty<string>()));
	}
}
