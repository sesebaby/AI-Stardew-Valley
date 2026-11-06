using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AISV.Core.World;
using Xunit;

namespace AISV.Core.Tests;

internal static class HeartEventFixture
{
	[ModuleInitializer]
	internal static void Load() => WorldData.UseHeartEvents(new[]
	{
		new HeartEvent("Abigail", 2, 1, "一起打游戏"),
		new HeartEvent("Abigail", 4, 2, "雨里听笛子")
	});
}

public class HeartEventScanTests
{
	[Fact]
	public void ReadsHeartsFromFriendshipPrecondition()
	{
		HeartEvent e = HeartEventScan.Parse("7001/f Leah 1000/t 900 1700", "none/-1 -1/farmer 1 1 0 Leah 2 2 0/speak Leah \"你来啦，@！$h#$b#坐吧。\"/pause 100/speak Leah \"下次再聊。^下回见。\"/end").Single();
		Assert.Equal(("Leah", 4, 7001), (e.Npc, e.Hearts, e.EventId));
		Assert.Contains("你来啦，农夫！ 坐吧。", e.Story);
		Assert.Contains("下次再聊。", e.Story);
		Assert.DoesNotContain("下回见", e.Story);
	}

	[Fact]
	public void LongFormAndSeveralVillagers()
	{
		List<HeartEvent> list = HeartEventScan.Parse("7002/Friendship Sam 500 Vincent 250", "none/-1 -1/farmer 1 1 0 Sam 2 2 0 Vincent 3 3 0/end").ToList();
		Assert.Equal(new[] { ("Sam", 2), ("Vincent", 1) }, list.Select(e => (e.Npc, e.Hearts)));
	}

	[Theory]
	[InlineData("7003/f Leah 100")]
	[InlineData("7004/t 600 700")]
	[InlineData("7005/!f Leah 1000")]
	[InlineData("forkName")]
	[InlineData("named.event/f Leah 1000")]
	[InlineData("7006/f Maru 1000")]
	public void SkipsWhatIsNotAHeartEvent(string key) => Assert.Empty(HeartEventScan.Parse(key, "none/-1 -1/farmer 1 1 0 Leah 2 2 0/end"));

	[Fact]
	public void CollectDropsDuplicatesAndSorts()
	{
		List<HeartEvent> list = HeartEventScan.Collect(new Dictionary<string, string>
		{
			["7010/f Leah 2000"] = "Leah",
			["7010/f Leah 2000/t 600 700"] = "Leah",
			["7011/f Leah 500"] = "Leah",
			["7012/f Alex 500"] = "Alex"
		});
		Assert.Equal(new[] { 7012, 7011, 7010 }, list.Select(e => e.EventId));
	}
}
