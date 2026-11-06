using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using AISV.Core.World.Market;
using Xunit;

namespace AISV.Core.Tests;

public class MarketTests
{
	private const ulong Game = 123456789012345UL;

	private static readonly string[] Spring = { "spring" };

	private static List<MarketCrop> Crops() => new List<MarketCrop>
	{
		new MarketCrop("(O)24", "防风草", 35, Spring),
		new MarketCrop("(O)190", "花椰菜", 175, Spring),
		new MarketCrop("(O)192", "土豆", 80, Spring),
		new MarketCrop("(O)256", "番茄", 60, new[] { "summer" }),
		new MarketCrop("(O)90", "仙人掌果子", 75, new[] { "spring", "summer", "fall", "winter" }),
		new MarketCrop("(O)613", "苹果", 100, new[] { "fall" })
	};

	[Fact]
	public void BoundsStayWithinTenPercentAndOverallRange()
	{
		Assert.Equal((0.9f, 1.1f), MarketRules.Bounds(1f));
		Assert.Equal((0.8f, 0.935f), MarketRules.Bounds(0.85f));
		(float low, float high) = MarketRules.Bounds(1.35f);
		Assert.Equal(1.215f, low, 3);
		Assert.Equal(1.4f, high, 3);
		Assert.Equal((0.8f, 0.88f), MarketRules.Bounds(0.8f));
		Assert.Equal(1.4f, MarketRules.Bounds(1.4f).High);
		Assert.Equal(0.913f, MarketRules.Bounds(0.83f).High);
		Assert.Equal(0.99f, MarketRules.Bounds(1.1f).Low);
	}

	[Fact]
	public void HashMatchesFnvOfTextGameIdDayAndSalt()
	{
		Assert.Equal(1002405653, MarketRules.Hash("(O)24", 4, 7, 123456789012345UL));
		Assert.Equal(1803766131, MarketRules.Hash("Lewis", 28, 3, 123456789012345UL));
		Assert.Equal(110891547, MarketRules.Hash("(O)190", 0, 11, 987654321UL));
	}

	[Fact]
	public void ShipmentIsRecordedOncePerNight()
	{
		MarketState s = new MarketState();
		ShippedItem[] night = { new ShippedItem("(O)24", false, 50, 35, 44), new ShippedItem("(O)24", false, 2, 70, 88), new ShippedItem("(O)400", true, 3, 360, 340) };
		Assert.True(MarketRules.RecordShipment(s, 5, night));
		Assert.Equal(52, s.PendingPlayerSales["(O)24"]);
		Assert.False(s.PendingPlayerSales.ContainsKey("(O)400"));
		Assert.Equal(50 * 9 + 2 * 18 - 3 * 20, s.LastNightDifference);
		Assert.Equal(5, s.LastNightDay);
		Assert.Equal(426, MarketRules.LastNightGold(s, 5));
		Assert.Equal(0, MarketRules.LastNightGold(s, 6));
		Assert.False(MarketRules.RecordShipment(s, 5, new[] { new ShippedItem("(O)24", false, 999, 35, 44) }));
		Assert.Equal(52, s.PendingPlayerSales["(O)24"]);
		Assert.True(MarketRules.RecordShipment(s, 6, Array.Empty<ShippedItem>()));
		Assert.Empty(s.PendingPlayerSales);
		Assert.Equal(0, s.LastNightDifference);
	}

	[Fact]
	public void FirstDayPricesWithoutVillagerSalesAndPicksTownGrownHotItems()
	{
		MarketState s = new MarketState();
		Dictionary<string, int> npc = MarketRules.PriceDay(s, Crops(), "spring", 0, Game, _ => true);
		Assert.Empty(npc);
		Assert.Empty(s.LastNpcSales);
		Assert.Equal(0, s.LastPricedDay);
		Assert.Equal(6, s.Items.Count);
		Assert.Equal(2, s.HotItems.Count);
		Assert.All(s.HotItems, id => Assert.Contains(id, new[] { "(O)24", "(O)190", "(O)192" }));
		foreach (MarketEntry e in s.Items.Values)
		{
			Assert.Equal(1f, e.Yesterday);
			Assert.InRange(e.Factor, 0.9f, 1.1f);
			Assert.Single(e.History);
		}
		// 反季的番茄、苹果目标里有 +0.10。
		Assert.True(s.Items["(O)256"].Factor >= 1.05f);
	}

	[Fact]
	public void SameSaveSameDayGivesSameNumbers()
	{
		MarketState a = new MarketState(), b = new MarketState();
		for (int day = 0; day < 5; day++)
		{
			MarketRules.PriceDay(a, Crops(), "spring", day, Game, _ => true);
			MarketRules.PriceDay(b, Crops(), "spring", day, Game, _ => true);
		}
		Assert.Equal(a.Items.Values.Select(e => e.Factor), b.Items.Values.Select(e => e.Factor));
		Assert.Equal(a.LastNpcSales.Select(x => (x.Seller, x.ItemId, x.Count)), b.LastNpcSales.Select(x => (x.Seller, x.ItemId, x.Count)));
		MarketState other = new MarketState();
		for (int day = 0; day < 5; day++)
		{
			MarketRules.PriceDay(other, Crops(), "spring", day, Game + 1, _ => true);
		}
		Assert.NotEqual(a.Items.Values.Select(e => e.Factor), other.Items.Values.Select(e => e.Factor));
	}

	[Fact]
	public void VillagersSellInSeasonTownGrownCropsWhenPresent()
	{
		MarketState s = new MarketState();
		MarketRules.PriceDay(s, Crops(), "spring", 0, Game, _ => true);
		Dictionary<string, int> npc = MarketRules.PriceDay(s, Crops(), "spring", 1, Game, n => n != "Linus");
		Assert.NotEmpty(s.LastNpcSales);
		Assert.DoesNotContain(s.LastNpcSales, x => x.Seller == "Linus");
		Assert.All(s.LastNpcSales, x =>
		{
			Assert.Contains(x.ItemId, new[] { "(O)24", "(O)190", "(O)192" });
			Assert.InRange(x.Count, 5, 20);
		});
		Assert.InRange(s.LastNpcSales.GroupBy(x => x.Seller).Max(g => g.Count()), 1, 2);
		Assert.Equal(npc.Values.Sum(), s.LastNpcSales.Sum(x => x.Count));
		foreach ((string id, int n) in npc)
		{
			Assert.Equal(n, s.Items[id].NpcSold);
		}
	}

	[Fact]
	public void SalesDecayAndPushPriceDownWithinDailyCap()
	{
		MarketState s = new MarketState();
		MarketRules.PriceDay(s, Crops(), "spring", 0, Game, _ => false);
		float start = s.Items["(O)24"].Factor;
		s.Items["(O)24"].Volume = 100f;
		s.PendingPlayerSales["(O)24"] = 300;
		MarketRules.PriceDay(s, Crops(), "spring", 3, Game, _ => false);
		MarketEntry e = s.Items["(O)24"];
		Assert.Equal(300, e.PlayerSold);
		Assert.Equal(100f * (float)Math.Pow(MarketRules.Decay, 3) + 300f, e.Volume, 3);
		Assert.Empty(s.PendingPlayerSales);
		Assert.Equal(start, e.Yesterday);
		Assert.True(e.Factor >= MarketRules.Bounds(start).Low - 0.0001f);
		Assert.True(e.Factor < start);
		// 销量压低价格最多 20%，一直卖下去会落到 0.8 附近但不再更低。
		for (int day = 4; day < 30; day++)
		{
			s.PendingPlayerSales["(O)24"] = 2000;
			MarketRules.PriceDay(s, Crops(), "spring", day, Game, _ => false);
			Assert.InRange(s.Items["(O)24"].Factor, MarketRules.MinFactor, MarketRules.MaxFactor);
		}
		float demand = s.HotItems.Contains("(O)24") ? 1f + MarketRules.HotBonus : 1f;
		Assert.True(s.Items["(O)24"].Factor <= demand * (1f - MarketRules.MaxSupplyDrop) * (1f + MarketRules.Noise) + 0.001f);
		Assert.Equal(MarketRules.HistoryDays, s.Items["(O)24"].History.Count);
	}

	[Fact]
	public void HotItemsChangeWeekly()
	{
		MarketState s = new MarketState();
		MarketRules.PriceDay(s, Crops(), "spring", 0, Game, _ => false);
		List<string> week0 = s.HotItems.ToList();
		MarketRules.PriceDay(s, Crops(), "spring", 6, Game, _ => false);
		Assert.Equal(week0, s.HotItems);
		Assert.Equal(0, s.HotWeek);
		MarketRules.PriceDay(s, Crops(), "spring", 7, Game, _ => false);
		Assert.Equal(1, s.HotWeek);
	}

	[Fact]
	public void TargetCombinesSeasonHotSupplyAndNoise()
	{
		MarketEntry e = new MarketEntry { ItemId = "(O)256", Volume = 200f };
		MarketCrop tomato = new MarketCrop("(O)256", "番茄", 60, new[] { "summer" });
		float t = MarketRules.Target(e, tomato, "spring", 4, new[] { "(O)256" }, Game);
		float noise = (MarketRules.Hash("(O)256", 4, 7, Game) % 2001 / 1000f - 1f) * MarketRules.Noise;
		Assert.Equal(1.22f * 0.9f * (1f + noise), t, 4);
	}

	[Fact]
	public void BoardPutsHeldSoldHotAndInSeasonFirst()
	{
		MarketRow Row(string id, bool has = false, int sold = 0, bool hot = false, bool off = false, float change = 0f, float factor = 1f) =>
			new MarketRow(id, id, 50, 50, factor, change, Array.Empty<float>(), hot, off, false, sold, 0, has);
		List<MarketRow> rows = new List<MarketRow>
		{
			Row("b-off", off: true, change: 9f, factor: 1.2f),
			Row("a-plain"),
			Row("held", has: true),
			Row("sold", sold: 4),
			Row("hot", hot: true)
		};
		Assert.Equal(new[] { "held", "sold", "hot", "a-plain", "b-off" }, MarketRules.Board(rows, 10).Select(r => r.ItemId));
		Assert.Equal(2, MarketRules.Board(rows, 2).Count);
		MarketRow made = MarketRules.Row(new MarketCrop("(O)24", "防风草", 35, Spring), new MarketEntry { ItemId = "(O)24", Factor = 1.1f, Yesterday = 1f, Volume = 120f }, "summer", new[] { "(O)24" }, true);
		Assert.Equal(38, made.TodayPrice);
		Assert.Equal(10f, made.ChangePercent, 1);
		Assert.True(made.Hot && made.OutOfSeason && made.Oversupplied && made.PlayerHas);
	}

	[Fact]
	public void ContentOriginTakesLongestModPrefix()
	{
		string[] mods = { "Author", "Author.Pack", "Other.Mod" };
		Assert.Equal("Author.Pack", ContentOrigin.Of(mods, "(O)Author.Pack_Berry"));
		Assert.Equal("Author", ContentOrigin.Of(mods, "Author.Thing"));
		Assert.Equal("Other.Mod", ContentOrigin.Of(mods, "24", "Other.Mod.Seed"));
		Assert.Null(ContentOrigin.Of(mods, "(O)24"));
		Assert.Null(ContentOrigin.Of(mods, "Author.PackBerry".Replace("Author.", "AuthorX.")));
		Assert.Null(ContentOrigin.Of(mods, null, ""));
	}

	[Fact]
	public void ContentModsFollowNpcModSettings()
	{
		MemoryBackend cache = new MemoryBackend();
		OptOutList optOut = new OptOutList(new HttpClient(new FakeHttp()), cache, new NpcOwnershipScanner(cache, NullLog.Instance), NullLog.Instance);
		ModConfig config = new ModConfig();
		NpcEligibility e = new NpcEligibility(() => config, optOut, NullLog.Instance);
		e.SetLoadedMods(new[] { "A.Pack", "B.Pack", "Tools.Items", "FlashShifter.StardewValleyExpandedCP" });
		e.SetOwners(new Dictionary<string, string> { ["Zed"] = "A.Pack", ["Yuki"] = "B.Pack", ["Claire"] = "FlashShifter.StardewValleyExpandedCP" });
		Assert.True(e.IsContentModEnabled(null));
		Assert.True(e.IsContentModEnabled("A.Pack"));
		config.ModVillagerScope = NpcModActivationMode.VanillaOnly;
		Assert.True(e.IsContentModEnabled(""));
		Assert.False(e.IsContentModEnabled("Tools.Items"));
		config.ModVillagerScope = NpcModActivationMode.SelectedMods;
		config.ModVillagerPicks = new List<string> { "A.Pack" };
		Assert.True(e.IsContentModEnabled("A.Pack"));
		Assert.False(e.IsContentModEnabled("B.Pack"));
		Assert.True(e.IsContentModEnabled("Tools.Items"));
		config.ModVillagerScope = NpcModActivationMode.VanillaAndSve;
		Assert.True(e.IsContentModEnabled("FlashShifter.StardewValleyExpandedCP"));
		Assert.False(e.IsContentModEnabled("A.Pack"));
	}

	[Fact]
	public void ModFamiliesComeFromTheDownloadFolder()
	{
		IReadOnlyDictionary<string, string> f = ContentOrigin.Families(new[]
		{
			new ModFolder("A.Npc", "/game/Mods/AliceMod/[CP] Alice"),
			new ModFolder("A.Items", "/game/Mods/AliceMod/[JA] Items/"),
			new ModFolder("B.Pack", "/game/Mods/BobMod")
		});
		Assert.Equal("AliceMod", f["A.Npc"]);
		Assert.Equal("AliceMod", f["A.Items"]);
		Assert.Equal("BobMod", f["B.Pack"]);
		Assert.Equal("Win", ContentOrigin.Families(new[] { new ModFolder("X", "C:\\Mods\\Win\\One"), new ModFolder("Y", "C:\\Mods\\Other") })["X"]);
		Assert.Empty(ContentOrigin.Families(new[] { new ModFolder("Solo", "/game/Mods/Solo") }));
	}

	[Fact]
	public void FamilyAndSveCountEvenBeforeTheScanFinishes()
	{
		MemoryBackend cache = new MemoryBackend();
		OptOutList optOut = new OptOutList(new HttpClient(new FakeHttp()), cache, new NpcOwnershipScanner(cache, NullLog.Instance), NullLog.Instance);
		ModConfig config = new ModConfig { ModVillagerScope = NpcModActivationMode.SelectedMods };
		NpcEligibility e = new NpcEligibility(() => config, optOut, NullLog.Instance);
		e.SetLoadedMods(new[] { "A.Npc", "A.Items", "C.Code", "FlashShifter.StardewValleyExpandedCP" });
		e.SetModFamilies(new[] { new ModFolder("A.Npc", "/m/Alice/Npc"), new ModFolder("A.Items", "/m/Alice/Items"), new ModFolder("C.Code", "/m/Code") });
		// 还没扫完：SVE 照样算带 NPC。
		Assert.False(e.IsContentModEnabled("FlashShifter.StardewValleyExpandedCP"));
		Assert.True(e.IsContentModEnabled("A.Items"));
		// NPC 包和物品包在同一家：没选 NPC 包时物品也不用，选了就都能用。
		e.SetOwners(new Dictionary<string, string> { ["Alice"] = "A.Npc" });
		Assert.False(e.IsContentModEnabled("A.Items"));
		config.ModVillagerPicks = new List<string> { "A.Npc" };
		Assert.True(e.IsContentModEnabled("A.Items"));
		// 扫描不到、名字带 mod 前缀的村民。
		Assert.True(e.IsContentModEnabled("C.Code"));
		e.SetWorld(new NpcEligibility.World(new HashSet<string> { "C.Code_Zoe" }, new HashSet<string>(), new HashSet<string>()));
		Assert.False(e.IsContentModEnabled("C.Code"));
	}
}
