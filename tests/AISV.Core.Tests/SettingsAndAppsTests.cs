using AISV.Core.Apps;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class SettingsAndAppsTests
{
	private static GossipBook Book(ModConfig c)
	{
		DocumentStore store = new DocumentStore(new MemoryBackend(), NullLog.Instance);
		return new GossipBook(() => store, () => c, _ => true, _ => true, n => n, NullLog.Instance);
	}

	[Theory]
	[InlineData("八卦", true)]
	[InlineData("AI 八卦", true)]
	[InlineData("ai", true)]
	[InlineData("八卦 信件", false)]
	[InlineData("   ", false)]
	public void SearchNeedsEveryWordInSomeField(string query, bool expected)
	{
		Assert.Equal(expected, SettingsSearch.Matches(query, "八卦和日记", "晚上用 AI 整理"));
	}

	[Theory]
	[InlineData(10, 70)]
	[InlineData(125, 125)]
	[InlineData(400, 150)]
	public void WindowSizeStaysInRange(int value, int expected)
	{
		Assert.Equal(expected, new ModConfig { ShellSizePercent = value }.Normalize().ShellSizePercent);
	}

	[Fact]
	public void HiddenAppsAreCleanedAndCanBeShownAgain()
	{
		ModConfig c = new ModConfig { HiddenApps = null! }.Normalize();
		AppShelf.SetHidden(c, "mood", true);
		AppShelf.SetHidden(c, "mood", true);
		Assert.True(AppShelf.IsHidden(c, "MOOD"));
		Assert.Single(c.HiddenApps);
		AppShelf.SetHidden(c, "Mood", false);
		Assert.False(AppShelf.IsHidden(c, "mood"));
		Assert.Empty(c.HiddenApps);
	}

	[Fact]
	public void ThanksPageCannotBeHidden()
	{
		ModConfig c = new ModConfig();
		AppShelf.SetHidden(c, AppShelf.AlwaysShown, true);
		Assert.False(AppShelf.IsHidden(c, AppShelf.AlwaysShown));
		c.HiddenApps.Add(AppShelf.AlwaysShown);
		Assert.False(AppShelf.IsHidden(c, AppShelf.AlwaysShown));
	}

	[Fact]
	public void OtherModsRumorsObeyTheSwitchAndDailyLimit()
	{
		ModConfig c = new ModConfig { ModRumorsDaily = 2 };
		GossipBook book = Book(c);
		Assert.False(book.AddThirdPartyNote(" ", "some.mod"));
		Assert.False(book.AddThirdPartyNote("有人在河边捡到一封信", ""));
		Assert.True(book.AddThirdPartyNote("有人在河边捡到一封信", "some.mod"));
		Assert.True(book.AddThirdPartyNote("矿洞里传出歌声", "other.mod"));
		Assert.False(book.AddThirdPartyNote("第三条", "some.mod"));
		ExternalNotes notes = book.TakeExternalNotes();
		Assert.Equal(2, notes.Notes.Count);
		c.RumorsFromMods = false;
		Assert.False(book.AddThirdPartyNote("关掉以后", "some.mod"));
	}
}
