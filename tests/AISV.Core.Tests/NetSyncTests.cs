using System.Collections.Generic;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Social;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Core.World.Commissions;
using AISV.Core.World.Ledger;
using Xunit;

namespace AISV.Core.Tests;

/// <summary>联机同步用到的几处：文档整份导出、换成房主那份、改动的版本号；今天的聊天和心情转给房主。</summary>
public class NetSyncTests
{
	private sealed class Notes
	{
		public List<string> Lines { get; set; } = new List<string>();
	}

	private static readonly DocumentType<Notes> Doc = new DocumentType<Notes>("notes", 1, () => new Notes());

	[Fact]
	public void ExportedDocumentImportsIntoAnotherStore()
	{
		DocumentStore host = new DocumentStore(new MemoryBackend());
		host.Get("town/a.json", Doc).Lines.Add("八卦");
		host.MarkDirty("town/a.json");
		DocumentStore farmhand = new DocumentStore(new MemoryBackend());
		Notes before = farmhand.Get("town/a.json", Doc);
		farmhand.Import("town/a.json", host.Export("town/a.json")!);
		Notes after = farmhand.Get("town/a.json", Doc);
		Assert.NotSame(before, after);
		Assert.Equal(new[] { "八卦" }, after.Lines);
		Assert.Equal(host.Export("town/a.json"), farmhand.Export("town/a.json"));
	}

	[Fact]
	public void ImportedDocumentCanBeMarkedDirtyBeforeRead()
	{
		DocumentStore farmhand = new DocumentStore(new MemoryBackend());
		DocumentStore host = new DocumentStore(new MemoryBackend());
		host.Get("town/a.json", Doc);
		farmhand.Import("town/a.json", host.Export("town/a.json")!);
		farmhand.MarkDirty("town/a.json");
		Assert.Throws<System.InvalidOperationException>(() => farmhand.MarkDirty("town/never.json"));
	}

	[Fact]
	public void RevisionChangesOnlyWhenDocumentChanges()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		Assert.Equal(0, store.Revision("a.json"));
		store.Get("a.json", Doc);
		long read = store.Revision("a.json");
		Assert.True(read > 0);
		store.Get("a.json", Doc);
		Assert.Equal(read, store.Revision("a.json"));
		store.MarkDirty("a.json");
		Assert.True(store.Revision("a.json") > read);
	}

	[Fact]
	public void ExportOfMissingDocumentIsNull()
	{
		Assert.Null(new DocumentStore(new MemoryBackend()).Export("nothing.json"));
	}

	[Fact]
	public void OtherFarmersTodayIncludesHostSharedTalksButNotMine()
	{
		DocumentStore store = new DocumentStore(new MemoryBackend());
		ModConfig c = new ModConfig();
		GossipBook book = new GossipBook(() => store, () => c, _ => true, _ => true, n => n, NullLog.Instance);
		List<DailyInteraction> seen = new List<DailyInteraction>();
		book.Interacted += seen.Add;
		DailyInteraction mine = new DailyInteraction { Npc = "Lewis", Farmer = "Bob", Opener = "早", Reply = "去钓鱼", Comeback = "好" };
		book.RecordInteraction(mine);
		Assert.Single(seen);
		book.ReplaceShared(new[] { mine, new DailyInteraction { Npc = "Lewis", Farmer = "Ann", Opener = "嗨", Reply = "要种子", Comeback = "明天来" } });
		string block = book.OtherFarmersToday("Lewis", "Bob");
		Assert.Contains("Ann", block);
		Assert.DoesNotContain("Bob", block);
		Assert.Contains("Bob", book.OtherFarmersToday("Lewis", "Ann"));
		book.AddRemote(new DailyInteraction { Npc = "Pam", Farmer = "Ann", Opener = "a", Reply = "b" });
		Assert.Single(seen);
		Assert.Equal(2, book.TakeTodayInteractions().Count);
		Assert.Equal("", book.OtherFarmersToday("Lewis", "Bob"));
	}

	[Fact]
	public void MoodChangesAreAnnounced()
	{
		MoodBook moods = new MoodBook();
		List<(string, int)> changes = new List<(string, int)>();
		moods.Adjusted += (n, d) => changes.Add((n, d));
		moods.Adjust("Haley", -5);
		Assert.Equal(new[] { ("Haley", -5) }, changes);
	}

	[Fact]
	public void OldSavesBelongToTheHost()
	{
		Assert.Equal(0, new Commission().Taker);
		Assert.Equal(0, new Debt().Farmer);
		Debt d = System.Text.Json.JsonSerializer.Deserialize<Debt>("{\"Id\":\"d1\",\"Npc\":\"Lewis\",\"Amount\":100}")!;
		Assert.Equal(0, d.Farmer);
	}
}
