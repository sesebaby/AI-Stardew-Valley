using System.Collections.Generic;
using System.Linq;
using AISV.Core.Compat;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class TheftTests
{
	[Fact]
	public void LootReadsAsAShortList()
	{
		Assert.Equal("", TheftText.Loot(new List<StolenItem>(), 0));
		Assert.Equal("120g", TheftText.Loot(new List<StolenItem>(), 120));
		List<StolenItem> items = new List<StolenItem>
		{
			new StolenItem { Name = "破旧钱包", Price = 50 },
			new StolenItem { Name = "苹果", Stack = 2, Price = 100 },
			new StolenItem { Name = "苹果", Stack = 1, Price = 100 }
		};
		Assert.Equal("破旧钱包、苹果 ×3和80g", TheftText.Loot(items, 80));
	}

	[Fact]
	public void ValueAndMoodDropScaleWithWhatWasTaken()
	{
		TheftRecord r = new TheftRecord { Coins = 120, Items = { new StolenItem { Price = 100, Stack = 2 } } };
		Assert.Equal(320, r.Value);
		Assert.Equal(-10, TheftText.MoodDrop(r.Value));
		Assert.Equal(-6, TheftText.MoodDrop(50));
		Assert.Equal(-15, TheftText.MoodDrop(1500));
	}

	[Fact]
	public void LossGossipNeverNamesTheThief()
	{
		TheftRecord pick = new TheftRecord { Victims = { "Pierre" }, Coins = 120 };
		string text = TheftText.LossGossip(pick, n => n == "Pierre" ? "皮埃尔" : n, "镇上");
		Assert.Equal("皮埃尔在镇上一转身，身上的120g就不见了，到现在也不知道是被谁摸走的。", text);
		TheftRecord home = new TheftRecord { Kind = TheftRecord.Burglary, Victims = { "Pierre", "Caroline" }, Items = { new StolenItem { Name = "钻石", Price = 750 } } };
		Assert.Equal("Pierre、Caroline家里被人翻过，少了钻石，没人看见是谁干的。", TheftText.LossGossip(home, n => n, "杂货店"));
		home.Location = "SeedShop";
		Assert.StartsWith("Pierre、Caroline店里被人翻过", TheftText.LossGossip(home, n => n, "杂货店"));
	}

	[Fact]
	public void ShopkeepersCountAsVictimsBeforeResidents()
	{
		Assert.Equal(new[] { "Pierre", "Caroline", "Abigail" }, TheftRules.BurglaryVictims("SeedShop", new[] { "Caroline", "Pierre", "Abigail" }));
		Assert.Equal(new[] { "Harvey" }, TheftRules.BurglaryVictims("Hospital", new string[0]));
		Assert.Equal(new[] { "Emily", "Haley" }, TheftRules.BurglaryVictims("HaleyHouse", new[] { "Emily", "Haley" }));
	}

	[Fact]
	public void OnlySomeoneWhoActuallyCaughtYouCostsFriendship()
	{
		TheftRecord pick = new TheftRecord { Victims = { "Pierre" }, Caught = true, CaughtBy = "Pierre" };
		Assert.Equal(new[] { ("Pierre", 60) }, TheftRules.FriendshipHits(pick, 60));
		TheftRecord home = new TheftRecord { Kind = TheftRecord.Burglary, Victims = { "Pierre", "Caroline", "Abigail" }, Caught = true, CaughtBy = "Abigail" };
		Assert.Equal(new[] { ("Abigail", 60), ("Pierre", 30), ("Caroline", 30) }, TheftRules.FriendshipHits(home, 60));
		TheftRecord alarm = new TheftRecord { Kind = TheftRecord.Burglary, Victims = { "Pierre" }, Caught = true };
		Assert.Empty(TheftRules.FriendshipHits(alarm, 60));
		Assert.Empty(TheftRules.FriendshipHits(new TheftRecord { Victims = { "Pierre" } }, 60));
		Assert.Empty(TheftRules.FriendshipHits(pick, 0));
	}

	[Fact]
	public void WhatGetsRecordedAndNoticed()
	{
		TheftRecord empty = new TheftRecord { Kind = TheftRecord.Burglary, Victims = { "Pierre" } };
		Assert.False(TheftRules.WorthRecording(empty, false));
		empty.Caught = true;
		Assert.False(TheftRules.WorthRecording(empty, false));
		empty.CaughtBy = "Pierre";
		Assert.True(TheftRules.WorthRecording(empty, false));
		Assert.False(TheftRules.WorthRecording(new TheftRecord { Victims = { "Pierre" } }, horse: true));
		Assert.True(TheftRules.WorthRecording(new TheftRecord { Victims = { "Pierre" }, Coins = 30 }, horse: true));
		Assert.True(TheftRules.WorthRecording(new TheftRecord { Victims = { "Pierre" }, Caught = true, CaughtBy = "Pierre" }, false));

		StolenItem gem = new StolenItem { Name = "钻石", Price = 750 };
		Assert.True(TheftRules.LossNoticed(new TheftRecord { Items = { gem } }));
		Assert.False(TheftRules.LossNoticed(new TheftRecord { Items = { gem }, Caught = true, CaughtBy = "Pierre" }));
		Assert.True(TheftRules.LossNoticed(new TheftRecord { Kind = TheftRecord.Burglary, Items = { gem }, Caught = true }));
		Assert.False(TheftRules.LossNoticed(new TheftRecord()));
	}

	[Fact]
	public void LogKeepsTheLatestRecords()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), AISV.Core.Diagnostics.NullLog.Instance);
		TheftLog log = new TheftLog(() => store);
		for (int i = 0; i < TheftLog.MaxRecords + 5; i++)
		{
			log.Add(new TheftRecord { Day = i + 1, Time = 900 });
		}
		Assert.Equal(TheftLog.MaxRecords, log.All.Count);
		Assert.Equal(6, log.All[0].Day);
		Assert.Single(log.On(10));
		Assert.False(string.IsNullOrEmpty(log.All[^1].Id));
		log.Add(new TheftRecord { Day = 300, Time = 900 });
		log.Add(new TheftRecord { Day = 300, Time = 900 });
		Assert.Equal(log.All.Count, log.All.Select(r => r.Id).Distinct().Count());
	}
}
