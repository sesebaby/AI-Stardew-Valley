using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Ai;
using AISV.Core.Apps;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Favors;
using AISV.Core.Messenger;
using AISV.Core.Social;
using AISV.Core.Social.Drama;
using AISV.Core.Social.Personality;
using AISV.Core.Social.Web;
using AISV.Core.Stories;
using Xunit;

namespace AISV.Core.Tests;

public class RestoredFeatureTests
{
	private static List<string> Text(VillagerChapter chapter, VillagerFacts facts) => VillagerDiary.Write(chapter, facts).Select(l => l.Text).ToList();

	// ---- 村民日记 ----

	[Fact]
	public void DiaryEveryChapterSaysSomethingWhenEmpty()
	{
		VillagerFacts facts = new VillagerFacts { Npc = "Abigail", Name = "阿比盖尔", Today = 10 };
		foreach (VillagerChapter chapter in Enum.GetValues<VillagerChapter>())
		{
			List<DiaryLine> lines = VillagerDiary.Write(chapter, facts);
			Assert.NotEmpty(lines);
			Assert.All(lines, l => Assert.Equal(DiaryInk.Faint, l.Ink));
		}
	}

	[Fact]
	public void DiaryHealedSoresAndPrivateTurnsStayHidden()
	{
		VillagerFacts facts = new VillagerFacts
		{
			Npc = "Abigail",
			Name = "阿比盖尔",
			Today = 10,
			Profile = new EmotionalProfile { Impression = "怪人", FaithInFarmer = 0.9f, Affection = -0.9f, Sores = new List<NegativeMemory> { new NegativeMemory { Text = "旧伤", HealsOn = 5 }, new NegativeMemory { Text = "新伤", HealsOn = 12 } } },
			Personality = new NpcPersonalityState
			{
				Traits = new Dictionary<string, PersonalityTraitState> { ["kindness"] = new PersonalityTraitState { Level = 0.5f }, ["honesty"] = new PersonalityTraitState { Level = 0.01f } },
				TurningPoints = new List<PersonalityMilestone> { new PersonalityMilestone { Text = "公开的", Private = false }, new PersonalityMilestone { Text = "私下的", Private = true } }
			}
		};
		List<string> heart = Text(VillagerChapter.Heart, facts);
		Assert.Contains(heart, l => l.Contains("新伤") && l.Contains("2 天"));
		Assert.DoesNotContain(heart, l => l.Contains("旧伤"));
		Assert.Contains(heart, l => l.Contains(VillagerDiary.TrustWord(0.9f)));
		List<string> changes = Text(VillagerChapter.Changes, facts);
		Assert.Contains(changes, l => l.Contains(PersonalityEvolution.TraitWord("kindness", 0.5f)));
		Assert.DoesNotContain(changes, l => l.Contains(PersonalityEvolution.TraitWord("honesty", 0.01f)));
		Assert.Contains(changes, l => l.Contains("公开的"));
		Assert.DoesNotContain(changes, l => l.Contains("私下的"));
	}

	[Fact]
	public void DiaryOthersLimitAndHideRomanceAroundChildren()
	{
		List<NpcSocialEdge> edges = new List<NpcSocialEdge>
		{
			new NpcSocialEdge { First = "Jas", Second = "Vincent", Standing = "friendly", Encounters = 3, LastMetDay = 9, Shared = new List<NpcSocialMemory> { new NpcSocialMemory { Text = "一起约会", OnDay = 9 }, new NpcSocialMemory { Text = "一起抓虫", OnDay = 8 } } },
			new NpcSocialEdge { First = "Jas", Second = "Marnie", Standing = "unknown", Encounters = 1 }
		};
		List<string> lines = Text(VillagerChapter.Others, new VillagerFacts { Npc = "Jas", Name = "贾斯", Today = 10, Edges = edges, Display = n => n });
		Assert.Contains(lines, l => l.Contains("一起抓虫"));
		Assert.DoesNotContain(lines, l => l.Contains("约会"));
		Assert.DoesNotContain(lines, l => l.Contains("Marnie"));
		List<NpcSocialEdge> many = Enumerable.Range(0, 12).Select(i => new NpcSocialEdge { First = "Sam", Second = "N" + i, Standing = "friendly", Encounters = i }).ToList();
		Assert.Equal(VillagerDiary.MaxOthers, VillagerDiary.Write(VillagerChapter.Others, new VillagerFacts { Npc = "Sam", Today = 10, Edges = many }).Count(l => l.Ink == DiaryInk.Heading));
	}

	[Fact]
	public void DiaryAsksOpenFirstAndKeepsFewClosed()
	{
		List<ItemRequestFavor> requests = Enumerable.Range(0, 6).Select(i => new ItemRequestFavor { Asker = "Sam", WantedItemName = "旧" + i, Phase = "completed", AskedOn = i }).ToList();
		requests.Add(new ItemRequestFavor { Asker = "Sam", WantedItemName = "新要的", Phase = "active", AskedOn = 9, LapsesOn = 12, PayGold = 300 });
		List<DiaryLine> lines = VillagerDiary.Write(VillagerChapter.Asks, new VillagerFacts { Npc = "Sam", Today = 10, ItemRequests = requests });
		Assert.Contains("新要的", lines[0].Text);
		Assert.Contains("还剩 2 天", lines[1].Text);
		Assert.Contains("300", lines[1].Text);
		Assert.Equal(1 + VillagerDiary.MaxClosedAsks, lines.Count / 2);
	}

	[Fact]
	public void DiaryStoriesShowWhatWaitsOnYou()
	{
		StoryArc arc = new StoryArc { Title = "风波", Stage = "正吵着", State = StoryArc.Completable, Cast = new List<string> { "Sam", "Haley" }, Goals = new List<ArcObjective> { new ArcObjective { Npc = "Sam", Text = "找山姆谈谈" }, new ArcObjective { Npc = "Haley", Text = "找海莉", Done = true } }, Endings = new List<ArcChoiceOption> { new ArcChoiceOption() } };
		List<string> lines = Text(VillagerChapter.Stories, new VillagerFacts { Npc = "Sam", Today = 10, Arcs = new[] { arc }, Display = n => n });
		Assert.Contains(lines, l => l.Contains("目标 1/2"));
		Assert.Contains(lines, l => l.Contains("找山姆谈谈"));
		Assert.DoesNotContain(lines, l => l.Contains("找海莉"));
		Assert.Contains(lines, l => l.Contains("Haley"));
		Assert.Contains(lines, l => l.Contains("选择"));
		Assert.Empty(VillagerDiary.Write(VillagerChapter.Stories, new VillagerFacts { Npc = "Elliott", Today = 10, Arcs = new[] { arc } }).Where(l => l.Ink != DiaryInk.Faint));
	}

	[Fact]
	public void DiaryBeliefsIgnoreWeakOpinionsAndTheFarmer()
	{
		List<Opinion> opinions = new List<Opinion>
		{
			new Opinion { About = "Sam", Attitude = Stances.Annoyed, Annoyance = 0.8f },
			new Opinion { About = "Alex", Attitude = Stances.Cold },
			new Opinion { About = RumorRules.Farmer, Attitude = Stances.Betrayed, Annoyance = 0.9f }
		};
		List<Rumor> rumors = new List<Rumor> { new Rumor { Subject = "塌方是假的", Convinced = new List<string> { "Abigail" } } };
		List<string> lines = Text(VillagerChapter.Beliefs, new VillagerFacts { Npc = "Abigail", Today = 10, Opinions = opinions, Rumors = rumors, Display = n => n });
		Assert.Contains(lines, l => l.Contains("信了") && l.Contains("塌方"));
		Assert.Contains(lines, l => l.Contains("Sam"));
		Assert.DoesNotContain(lines, l => l.Contains("Alex"));
		Assert.DoesNotContain(lines, l => l.Contains(RumorRules.Farmer));
	}

	[Fact]
	public void DiaryChildOpinionIgnoresEnvy()
	{
		Opinion o = new Opinion { About = "Vincent", Envy = 0.9f };
		Assert.Equal(0f, VillagerDiary.OpinionStrength(o, childInvolved: true));
		Assert.Equal(0.9f, VillagerDiary.OpinionStrength(o, childInvolved: false));
	}

	// ---- 大家都在哪 ----

	private static WhereInput Who(string? place, string? heading = null, bool hidden = false, bool route = true, params (int Time, string Place)[] stops) =>
		new WhereInput("Sam", "山姆", place, place ?? "", "SamHouse", heading, heading ?? "", hidden, route, stops.Select(s => new WhereStop(s.Time, s.Place, s.Place)).ToList());

	[Fact]
	public void WhereStatesFollowPlaceAndHeading()
	{
		Assert.Equal(WhereState.Hidden, Whereabouts.Plan(Who("Town", hidden: true), 900).State);
		Assert.Equal(WhereState.Walking, Whereabouts.Plan(Who("Town", heading: "Saloon"), 900).State);
		Assert.Equal(WhereState.Home, Whereabouts.Plan(Who("SamHouse"), 900).State);
		Assert.Equal(WhereState.Out, Whereabouts.Plan(Who("Town", heading: "Town"), 900).State);
	}

	[Fact]
	public void WhereWorksOutWhenHome()
	{
		WherePlan plan = Whereabouts.Plan(Who("Town", null, false, true, (800, "Town"), (1200, "Saloon"), (1800, "SamHouse"), (2000, "SamHouse")), 1000);
		Assert.Equal(1200, plan.Next!.Time);
		Assert.Equal(HomeReturn.At, plan.Home);
		Assert.Equal(1800, plan.HomeTime);
		Assert.Equal(0, plan.CurrentStop);
		Assert.Equal(HomeReturn.StaysHome, Whereabouts.Plan(Who("SamHouse", null, false, true, (1800, "SamHouse")), 1000).Home);
		Assert.Equal(HomeReturn.EndOfDay, Whereabouts.Plan(Who("Town", null, false, true, (800, "Town")), 1000).Home);
		Assert.Equal(HomeReturn.EndOfDay, Whereabouts.Plan(Who("Town", null, false, true, (1200, "Saloon")), 1000).Home);
		Assert.Equal(HomeReturn.OnTheWay, Whereabouts.Plan(Who("Town", "SamHouse", false, true, (1800, "SamHouse")), 1000).Home);
		Assert.Equal(HomeReturn.Unknown, Whereabouts.Plan(Who("Town", route: false), 1000).Home);
	}

	[Fact]
	public void WhereGroupsBiggestPlaceFirstAndHiddenLast()
	{
		WherePlan a = Whereabouts.Plan(Who("Town"), 900), b = Whereabouts.Plan(Who("town"), 900), c = Whereabouts.Plan(Who("Beach"), 900), d = Whereabouts.Plan(Who("Mine", hidden: true), 900);
		IReadOnlyList<WhereGroup> groups = Whereabouts.Group(new[] { d, c, a, b }, "看不到");
		Assert.Equal(new[] { 2, 1, 1 }, groups.Select(g => g.People.Count));
		Assert.Null(groups[^1].Place);
		Assert.Equal("看不到", groups[^1].Name);
	}

	[Fact]
	public void WhereSoonOnlyWithinHalfAnHour()
	{
		Assert.True(Whereabouts.IsSoon(950, 1020));
		Assert.False(Whereabouts.IsSoon(950, 1030));
		Assert.False(Whereabouts.IsSoon(1000, 1000));
	}

	// ---- 兼容页 ----

	[Theory]
	[InlineData(null, true, NpcCompatKind.Active)]
	[InlineData(null, false, NpcCompatKind.Left)]
	[InlineData("disabled-by-player", false, NpcCompatKind.TurnedOff)]
	[InlineData("disabled-by-config", false, NpcCompatKind.TurnedOff)]
	[InlineData("ai-mod-excluded:Some.Mod", false, NpcCompatKind.AuthorOptOut)]
	[InlineData("vanilla-only", false, NpcCompatKind.ByScope)]
	[InlineData("npc-mod-not-selected", false, NpcCompatKind.ByScope)]
	[InlineData("not-villager", false, NpcCompatKind.NotHandled)]
	public void CompatKinds(string? reason, bool enabled, NpcCompatKind kind)
	{
		Assert.Equal(kind, NpcCompat.Kind(reason, enabled));
		Assert.NotEmpty(NpcCompat.Explain(reason, enabled, "山姆"));
		Assert.DoesNotContain("{{", NpcCompat.Explain(reason, enabled, "山姆"));
	}

	[Fact]
	public void CompatTurningOnClearsBothLists()
	{
		ModConfig c = new ModConfig { VillagersOff = "Sam, Alex", OptOutExtras = "sam" };
		NpcCompat.SetOff(c, "Sam", false);
		Assert.Equal("Alex", c.VillagersOff);
		Assert.Equal("", c.OptOutExtras);
		Assert.False(EligibilityPolicy.ListContains(c.VillagersOff, "Sam"));
		NpcCompat.SetOff(c, "Sam", true);
		NpcCompat.SetOff(c, "Sam", true);
		Assert.Equal("Alex,Sam", c.VillagersOff);
		Assert.True(EligibilityPolicy.ListContains(c.VillagersOff, "sam"));
	}

	[Fact]
	public void CompatPickedModsToggle()
	{
		ModConfig c = new ModConfig();
		NpcCompat.SetPicked(c, "A.B", true);
		NpcCompat.SetPicked(c, "a.b", true);
		Assert.Single(c.ModVillagerPicks);
		NpcCompat.SetPicked(c, "A.B", false);
		Assert.Empty(c.ModVillagerPicks);
	}

	// ---- AI 状态 ----

	[Theory]
	[InlineData(false, true, false, 0, 0, null, AiHealthLevel.Off)]
	[InlineData(true, false, false, 5, 0, null, AiHealthLevel.Bad)]
	[InlineData(true, true, false, 5, 0, false, AiHealthLevel.Bad)]
	[InlineData(true, true, true, 5, 0, null, AiHealthLevel.Bad)]
	[InlineData(true, true, false, 5, 2, null, AiHealthLevel.Warning)]
	[InlineData(true, true, false, 0, 2, null, AiHealthLevel.Bad)]
	[InlineData(true, true, false, 0, 2, true, AiHealthLevel.Good)]
	[InlineData(true, true, false, 3, 0, null, AiHealthLevel.Good)]
	[InlineData(true, true, false, 0, 0, null, AiHealthLevel.Good)]
	public void HealthLevels(bool on, bool setUp, bool offline, int ok, int failed, bool? test, AiHealthLevel level)
	{
		Assert.Equal(level, AiHealthRules.Evaluate(new AiHealthFacts(on, setUp, offline, ok, failed, test)).Level);
	}

	// ---- 小手机 ----

	private static void Run(MessagePeek peek, double ms, bool unread = true)
	{
		peek.Update(ms, _ => unread, unread);
	}

	[Fact]
	public void PeekRisesShowsTucksAndLeaves()
	{
		MessagePeek peek = new MessagePeek();
		peek.Post(new PeekNote("Sam", "在吗"));
		Assert.True(peek.Update(16, _ => true, true));
		Assert.Equal(PeekPhase.Rising, peek.Phase);
		Run(peek, MessagePeek.SlideMs);
		Assert.Equal(PeekPhase.Banner, peek.Phase);
		Assert.Equal(1f, peek.Shown);
		Run(peek, MessagePeek.BannerMs);
		Assert.Equal(PeekPhase.Tucking, peek.Phase);
		Run(peek, MessagePeek.SlideMs);
		Assert.Equal(PeekPhase.Tab, peek.Phase);
		Assert.Null(peek.Current);
		Assert.Equal(MessagePeek.TabShare, peek.Shown);
		Run(peek, 16, unread: false);
		Assert.Equal(PeekPhase.Leaving, peek.Phase);
		Run(peek, MessagePeek.SlideMs, unread: false);
		Assert.False(peek.IsVisible);
	}

	[Fact]
	public void PeekReadWhileShowingGoesAway()
	{
		MessagePeek peek = new MessagePeek();
		peek.Post(new PeekNote("Sam", "在吗"));
		Run(peek, 16);
		Run(peek, MessagePeek.SlideMs);
		Run(peek, 16, unread: false);
		Assert.Equal(PeekPhase.Leaving, peek.Phase);
	}

	[Fact]
	public void PeekShowsNextAndSkipsAlreadyRead()
	{
		MessagePeek peek = new MessagePeek();
		peek.Post(new PeekNote("Sam", "一"));
		peek.Post(new PeekNote("Alex", "二"));
		peek.Post(new PeekNote("Haley", "三"));
		Func<string, bool> unread = n => n != "Alex";
		peek.Update(16, unread, true);
		peek.Update(MessagePeek.SlideMs, unread, true);
		Assert.True(peek.Update(MessagePeek.BannerMs, unread, true));
		Assert.Equal("Haley", peek.Current!.Thread);
		Assert.Equal(PeekPhase.Banner, peek.Phase);
	}

	[Fact]
	public void PeekDismissDropsEverything()
	{
		MessagePeek peek = new MessagePeek();
		peek.Post(new PeekNote("Sam", "一"));
		peek.Post(new PeekNote("Alex", "二"));
		Run(peek, 16);
		peek.Dismiss();
		Assert.Equal(PeekPhase.Leaving, peek.Phase);
		Run(peek, MessagePeek.SlideMs);
		Assert.False(peek.IsVisible);
		Assert.False(peek.Update(16, _ => true, true));
		Assert.False(peek.IsVisible);
	}

	[Fact]
	public void PeekSameChatKeepsOnlyTheNewest()
	{
		MessagePeek peek = new MessagePeek();
		peek.Post(new PeekNote("group", "一", "Sam"));
		peek.Post(new PeekNote("group", "二", "Alex"));
		Assert.True(peek.Update(16, _ => true, true));
		Assert.Equal("二", peek.Current!.Text);
		peek.Update(MessagePeek.SlideMs, _ => true, true);
		peek.Update(MessagePeek.BannerMs - 100, _ => true, true);
		peek.Post(new PeekNote("group", "三", "Haley"));
		Assert.Equal("三", peek.Current!.Text);
		peek.Update(200, _ => true, true);
		Assert.Equal(PeekPhase.Banner, peek.Phase);
	}

	[Fact]
	public void PeekQueueIsBounded()
	{
		MessagePeek peek = new MessagePeek();
		for (int i = 0; i < MessagePeek.MaxWaiting + 3; i++)
		{
			peek.Post(new PeekNote("N" + i, "x"));
		}
		peek.Update(16, _ => true, true);
		Assert.Equal("N3", peek.Current!.Thread);
	}
}
