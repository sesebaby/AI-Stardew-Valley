using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Diagnostics;
using AISV.Core.Social.Drama;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class DramaTests
{
	private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Abigail"] = "阿比盖尔", ["Sam"] = "山姆", ["Sebastian"] = "塞巴斯蒂安", ["Pierre"] = "皮埃尔", ["Caroline"] = "卡洛琳", ["Haley"] = "海莉", ["Vincent"] = "文森特", ["Jodi"] = "乔迪", ["Kent"] = "肯特"
	};

	private static string Display(string n) => Zh.TryGetValue(n, out string? v) ? v : n;

	private static (SocialDrama Drama, ModConfig Config, DocumentStore Store) Make(Action<ModConfig>? tweak = null, Func<string, bool>? enabled = null, MemoryBackend? backend = null)
	{
		ModConfig c = new ModConfig { DramaHeat = "Dramatic" }.Normalize();
		tweak?.Invoke(c);
		DocumentStore store = new DocumentStore(backend ?? new MemoryBackend(), NullLog.Instance);
		return (new SocialDrama(() => store, () => c, enabled ?? (_ => true), _ => true, Display, NullLog.Instance), c, store);
	}

	private static GossipEntry Gossip(string text, string origin = "", params string[] aware) => new GossipEntry
	{
		Day = 5, Season = "spring", Year = 1, Fact = text, Rumor = text, StartedBy = origin, KnownTo = aware.ToList(), BornOn = 5
	};

	// ---- 规则 ----

	[Theory]
	[InlineData(0.8f, 0.2f, 0f, 0.5f, Stances.Betrayed)]
	[InlineData(0.5f, 0.5f, 0f, 0.5f, Stances.Badmouthing)]
	[InlineData(0.1f, 0.28f, 0f, 0.5f, Stances.Cold)]
	[InlineData(0.36f, 0.5f, 0f, 0.5f, Stances.Cold)]
	[InlineData(0.1f, 0.5f, 0.32f, 0.5f, Stances.Suspicious)]
	[InlineData(0.18f, 0.5f, 0f, 0.5f, Stances.Annoyed)]
	[InlineData(0f, 0.52f, 0f, 0.65f, Stances.Protective)]
	[InlineData(0f, 0.58f, 0f, 0.45f, Stances.Neutral)]
	public void StanceFollowsThresholds(float irritation, float trust, float suspicion, float loyalty, string stance)
	{
		Assert.Equal(stance, OpinionRules.Classify(new Opinion { Annoyance = irritation, Faith = trust, Doubt = suspicion, Fealty = loyalty }));
		Assert.Equal(Stances.Admiring, OpinionRules.Classify(new Opinion { Esteem = 0.64f, Faith = 0.58f }));
	}

	[Fact]
	public void FreshOpinionsStartWarmerForRelatedPeople()
	{
		Opinion close = OpinionRules.Fresh("Sam", "Sebastian");
		Assert.Equal((0.6f, 0.55f, 0.7f, Stances.Protective), (close.Faith, close.Esteem, close.Fealty, close.Attitude));
		Opinion stranger = OpinionRules.Fresh("Pierre", "Haley");
		Assert.Equal((0.5f, 0.5f, 0.45f, Stances.Neutral), (stranger.Faith, stranger.Esteem, stranger.Fealty, stranger.Attitude));
		Assert.Equal("Caroline", DramaRules.CanonicalSpouse("Pierre"));
		Assert.Equal("Evelyn", DramaRules.CanonicalSpouse("George"));
		Assert.Equal("", DramaRules.CanonicalSpouse("Lewis"));
	}

	[Fact]
	public void TextFindsNamesSentimentAndAction()
	{
		Assert.Equal(new[] { "Abigail", "Sam" }, DramaText.Mentioned("I saw sam with 阿比盖尔 yesterday", Display));
		Assert.Empty(DramaText.Mentioned("Samson and Pierrette", Display));
		Assert.Equal(new[] { "Abigail" }, DramaText.Mentioned("阿比盖尔说的", null).Concat(DramaText.Mentioned("Abigail!", null)).Distinct());
		Assert.Equal(-1, DramaText.Sentiment("我听说塞巴斯蒂安撒谎了"));
		Assert.Equal(1, DramaText.Sentiment("阿比盖尔很善良，我支持她"));
		Assert.Equal(0, DramaText.Sentiment("今天天气不错"));
		Assert.Equal(DramaText.Leak, DramaText.Action("我听说塞巴斯蒂安撒谎了", -1));
		Assert.Equal(DramaText.Warn, DramaText.Action("watch out, he is a liar", -1));
		Assert.Equal(DramaText.Warn, DramaText.Action("他很粗鲁", -1));
		Assert.Equal(DramaText.Instigate, DramaText.Action("他让我很失望", -1));
		Assert.Equal(DramaText.Defend, DramaText.Action("x", 1));
		Assert.Equal("", DramaText.Action("x", 0));
	}

	[Fact]
	public void AcceptanceUsesHeartsPointsAndMood()
	{
		Assert.True(InfluenceRules.Acceptance(8, null, null) > InfluenceRules.Acceptance(3, null, null));
		Assert.InRange(InfluenceRules.Acceptance(0, null, null), 0.01f, 0.99f);
		Assert.InRange(InfluenceRules.Acceptance(14, null, null), 0.01f, 0.99f);
		Assert.Equal(InfluenceRules.Acceptance(5, null, null), InfluenceRules.Acceptance(5, null, "angry"));
		Assert.True(InfluenceRules.Acceptance(8, 20, null) > InfluenceRules.Acceptance(8, -20, null));
		Assert.True(InfluenceRules.Acceptance(8, 20, "happy") > InfluenceRules.Acceptance(8, 20, "neutral"));
		Assert.True(InfluenceRules.Acceptance(8, 20, "LOVE") > InfluenceRules.Acceptance(8, 20, ""));
		Assert.True(InfluenceRules.Acceptance(8, 20, "angry") < InfluenceRules.Acceptance(8, 20, "whatever"));
		float gain = InfluenceRules.Acceptance(5, 10, "happy") - InfluenceRules.Acceptance(5, 10, null);
		float loss = InfluenceRules.Acceptance(5, 10, null) - InfluenceRules.Acceptance(5, 10, "sad");
		Assert.True(loss > gain);
		Assert.Equal(InfluenceRules.Acceptance(5, 100, null), InfluenceRules.Acceptance(5, 900, null));
		Assert.Equal(0f, InfluenceRules.Acceptance(0, -100, "angry"));
		Assert.InRange(InfluenceRules.Acceptance(10, 100, "love"), 0.9f, 1f);
		Assert.Equal(InfluenceRules.Accepted, InfluenceRules.Result(InfluenceRules.Acceptance(8, 20, "happy")));
		Assert.Equal(InfluenceRules.Rejected, InfluenceRules.Result(InfluenceRules.Acceptance(0, 0, "angry")));
		Assert.Equal(InfluenceRules.Rejected, InfluenceRules.Result(0.24f));
		Assert.Equal(InfluenceRules.Suspected, InfluenceRules.Result(0.44f));
		Assert.Equal(InfluenceRules.Accepted, InfluenceRules.Result(0.45f));
	}

	// ---- 传闻 ----

	[Fact]
	public void RumorFromGossipCarriesNumbersAndSpreads()
	{
		Rumor r = RumorRules.FromGossip("海莉说阿比盖尔撒谎", "spring", 5, 1, "Haley", "Emily", new[] { "Emily", "Alex" }, null, -1, new[] { "Abigail" }, 5, DramaRules.Dramatic);
		Assert.Equal(new[] { "Haley", "Alex", "Emily" }, r.Heard);
		Assert.Equal((0.76f, 0.34f, 0.67f, 0.57f), (r.Believability, r.Warp, r.Heat, r.Harm));
		Assert.Equal(("partial", 31, RumorRules.Private), (r.Veracity, r.DiesDay, r.Spread));
		Assert.Equal(new[] { "Alex", "Emily", "Haley" }, r.Convinced);
		Assert.Equal(20, r.Id.Length);
		Assert.Equal(r.Id, RumorRules.FromGossip("海莉说阿比盖尔撒谎", "spring", 5, 1, "x", "", null, null, 0, Array.Empty<string>(), 5, DramaRules.Cozy).Id);

		Assert.False(RumorRules.SpreadForDay(r, 5, DramaRules.Dramatic, new[] { "Sam" }));
		Assert.True(RumorRules.SpreadForDay(r, 6, DramaRules.Dramatic, new[] { "Abigail", "Sam", "Robin", "Gus", "Pam" }));
		Assert.Equal(7, r.Heard.Count);
		Assert.Contains("Evelyn", r.Heard);
		Assert.Equal(RumorRules.Circle, r.Spread);
		Assert.Equal("Emily", r.Carrier);
		Assert.Equal(0.395f, r.Warp, 3);
		Assert.False(RumorRules.SpreadForDay(r, 32, DramaRules.Dramatic, new[] { "Sam" }));

		Rumor cozy = RumorRules.FromGossip("一件好事", "spring", 5, 1, "", "", new[] { "Sam" }, null, 1, new[] { "Sam" }, 5, DramaRules.Cozy);
		Assert.Equal((0.6f, 11, "partial"), (cozy.Believability, cozy.DiesDay, cozy.Veracity));
		Assert.Equal(0.07f, cozy.Warp, 4);
	}

	[Fact]
	public void RumorFromInfluenceStartsWithTheListener()
	{
		Influence a = new Influence { Id = "abc", Move = DramaText.Leak, Talker = "Sam", Aim = "Abigail", Subject = "听说她撒谎", Slant = -1, Uptake = 0.4f, Upshot = InfluenceRules.Suspected, OnDay = 10 };
		Rumor r = RumorRules.FromInfluence(a, DramaRules.Balanced);
		Assert.Equal((RumorRules.Farmer, "Sam", "abc"), (r.Source, r.Carrier, r.ComesFrom));
		Assert.Equal(new[] { "Sam" }, r.Heard);
		Assert.Equal((0.51f, 0.5f, 0.55f, 0.52f, 28), (r.Believability, r.Warp, r.Heat, r.Harm, r.DiesDay));
		Assert.Equal(new[] { "Sam" }, r.Doubters);
	}

	// ---- 新八卦 ----

	[Fact]
	public void BadGossipCoolsListenersTowardThePersonMentioned()
	{
		(SocialDrama drama, _, _) = Make();
		int changed = drama.ApplyGossip(Gossip("听说阿比盖尔撒谎了", "Haley", "Gus", "Sam"));
		Assert.Equal(2, changed);
		Opinion pierre = drama.Data.Opinions.Single(o => o.Who == "Gus");
		Assert.Equal((0.14f, 0.112f, 0.437f, 0.451f), (pierre.Annoyance, pierre.Doubt, pierre.Faith, pierre.Esteem));
		Assert.StartsWith("听到一条和海莉有关的八卦", pierre.Why);
		Opinion sam = drama.Data.Opinions.Single(o => o.Who == "Sam");
		Assert.Equal(0.049f, sam.Annoyance, 3);
		Assert.Empty(drama.Data.Beats);
		Rumor r = Assert.Single(drama.Data.Rumors);
		Assert.Equal(new[] { "Gus", "Haley", "Sam" }, r.Heard);

		Assert.Equal(2, drama.ApplyGossip(Gossip("又听说阿比盖尔撒谎", "Haley", "Gus", "Sam")));
		Assert.Single(drama.Data.Beats);
		Assert.Equal("sharp_comment", drama.Data.Beats[0].Kind);
	}

	[Fact]
	public void GossipRespectsSwitchesAndDisabledVillagers()
	{
		(SocialDrama off, ModConfig c, _) = Make(c => c.RumorsMoveOpinions = false);
		Assert.Equal(0, off.ApplyGossip(Gossip("听说阿比盖尔撒谎了", "Haley", "Pierre")));
		Assert.Single(off.Data.Rumors);
		Assert.Empty(off.Data.Opinions);
		c.DramaOn = false;
		Assert.Equal(0, off.ApplyGossip(Gossip("另一件：阿比盖尔撒谎了", "Haley", "Pierre")));
		Assert.Single(off.Data.Rumors);

		(SocialDrama blocked, _, _) = Make(enabled: n => n != "Haley");
		Assert.Equal(0, blocked.ApplyGossip(Gossip("听说阿比盖尔撒谎了", "Haley", "Pierre")));
		Assert.Empty(blocked.Data.Rumors);

		(SocialDrama nobody, _, _) = Make();
		Assert.Equal(0, nobody.ApplyGossip(Gossip("天气很糟糕")));
		Assert.Equal(1, nobody.ApplyGossip(Gossip("天气很糟糕", "Pierre", "Sam")));
		Assert.Equal("Pierre", nobody.Data.Opinions.Single().About);
	}

	[Fact]
	public void DailyBeatCapApplies()
	{
		(SocialDrama drama, _, _) = Make(c => c.DramaBeatsDaily = 1);
		for (int i = 0; i < 3; i++)
		{
			drama.ApplyGossip(Gossip($"第{i}次听说阿比盖尔撒谎", "Haley", "Pierre", "Gus"));
		}
		Assert.Single(drama.Data.Beats);
	}

	// ---- 农夫的话 ----

	[Fact]
	public void FarmerTalkChangesTheListenersView()
	{
		(SocialDrama drama, _, _) = Make();
		int changed = drama.RecordInfluence("Sam", "我听说阿比盖尔撒谎了", 8, new NpcReaction { FriendshipPoints = 20, Emotion = "happy" }, 10);
		Assert.Equal(1, changed);
		Influence a = Assert.Single(drama.Data.Influences);
		Assert.Equal((DramaText.Leak, InfluenceRules.Accepted, "Abigail"), (a.Move, a.Upshot, a.Aim));
		Opinion o = drama.Data.Opinions.Single();
		float d = 0.14f * (0.35f + InfluenceRules.Acceptance(8, 20, "happy")) * 0.45f;
		Assert.Equal(d, o.Annoyance, 3);
		Assert.Equal(d * 1.05f, o.Doubt, 3);
		Assert.StartsWith("农夫最近跟你透露过一件关于阿比盖尔的事。", o.Why);
		Assert.Single(drama.Data.Rumors);
		Assert.Equal("leaked_claim", drama.Data.Beats.Single().Kind);

		Assert.Equal(0, drama.RecordInfluence("Pierre", "阿比盖尔撒谎", 0, new NpcReaction { FriendshipPoints = 0, Emotion = "angry" }, 10));
		Assert.Equal(InfluenceRules.Rejected, drama.Data.Influences[0].Upshot);
		Assert.DoesNotContain(drama.Data.Opinions, x => x.Who == "Pierre");

		Assert.Equal(0, drama.RecordInfluence("Sam", "今天天气不错，阿比盖尔", 8, null, 10));
		Assert.Equal(0, drama.RecordInfluence("Sam", "山姆你撒谎", 8, null, 10));
		Assert.Equal(1, drama.RecordInfluence("Gus", "我支持阿比盖尔，她很善良", 8, null, 10));
		Assert.Contains("defended", drama.Data.Opinions.Single(x => x.Who == "Gus").Labels);
	}

	[Fact]
	public void FarmerTalkNeedsItsSwitch()
	{
		(SocialDrama drama, _, _) = Make(c => c.FarmerCanSway = false);
		Assert.Equal(0, drama.RecordInfluence("Sam", "我听说阿比盖尔撒谎了", 8, null, 10));
		Assert.Empty(drama.Data.Influences);
	}

	// ---- 成人 ----

	[Fact]
	public void AdultStrainOnlyForCanonicalSpousesWhenEnabled()
	{
		AdultRules.Gates on = new AdultRules.Gates(true, false, false, DramaRules.Balanced);
		List<AdultStrain> list = new List<AdultStrain>();
		Opinion pierre = new Opinion { Who = "Pierre", About = "Caroline", Annoyance = 0.2f, Why = "吵了一架" };
		Assert.True(AdultRules.Apply(list, pierre, DramaSources.Gossip, 10, on));
		AdultStrain s = Assert.Single(list);
		Assert.Equal((AdultRules.Strained, 0.2f, 0.52f, 0.75f, 31), (s.Phase, s.Force, s.Remorse, s.Limit, s.DiesDay));
		Assert.False(AdultRules.Apply(list, new Opinion { Who = "Pierre", About = "Haley", Annoyance = 0.9f }, DramaSources.Gossip, 10, on));
		Assert.False(AdultRules.Apply(new List<AdultStrain>(), new Opinion { Who = "Kent", About = "Jodi", Annoyance = 0.15f }, DramaSources.Gossip, 10, on));
		Assert.True(AdultRules.Apply(new List<AdultStrain>(), new Opinion { Who = "Kent", About = "Jodi", Annoyance = 0.15f }, DramaSources.Gossip, 10, on with { Intensity = DramaRules.Dramatic }));
		Assert.False(AdultRules.Apply(new List<AdultStrain>(), pierre, DramaSources.Gossip, 10, on with { Enabled = false }));

		List<AdultStrain> risky = new List<AdultStrain>();
		AdultRules.Apply(risky, new Opinion { Who = "Pierre", About = "Caroline", Annoyance = 0.5f }, DramaSources.Player, 10, on with { AllowInfidelity = true });
		Assert.Equal((AdultRules.Contemplating, RumorRules.Farmer, 0.45f, 0.575f), (risky[0].Phase, risky[0].Third, risky[0].Pull, risky[0].Limit));
		List<AdultStrain> guarded = new List<AdultStrain>();
		AdultRules.Apply(guarded, new Opinion { Who = "Pierre", About = "Caroline", Annoyance = 0.5f }, DramaSources.Player, 10, on with { AllowInfidelity = true, ProtectMarriages = true });
		Assert.Equal(AdultRules.Strained, guarded[0].Phase);
	}

	// ---- 天亮 ----

	[Fact]
	public void DayStartDecaysAndTrims()
	{
		(SocialDrama drama, _, _) = Make();
		DramaState d = drama.Data;
		d.Opinions.Add(new Opinion { Who = "Pierre", About = "Haley", Annoyance = 0.2f, Doubt = 0.005f, Faith = 0.45f, Attitude = Stances.Annoyed });
		d.Opinions.Add(new Opinion { Who = "Gus", About = "Haley", Faith = 0.52f });
		d.Opinions.Add(new Opinion { Who = "Sam", About = "Sebastian", Faith = 0.5f });
		d.Beats.Add(new DramaBeat { OnDay = 1 });
		d.Beats.Add(new DramaBeat { OnDay = 30 });
		d.Rumors.Add(new Rumor { Id = "old", Subject = "x", Heard = new List<string> { "Sam" }, BornDay = 1, MovedDay = 1, DiesDay = 20 });
		d.Adult.Add(new AdultStrain { Villager = "Pierre", Spouse = "Caroline", DiesDay = 40 });
		drama.OnDayStarted(30);
		Opinion pierre = d.Opinions.Single(o => o.Who == "Pierre");
		Assert.Equal((0.185f, 0f, 0.454f, Stances.Annoyed), (pierre.Annoyance, pierre.Doubt, pierre.Faith, pierre.Attitude));
		Assert.DoesNotContain(d.Opinions, o => o.Who == "Gus");
		Assert.Contains(d.Opinions, o => o.Who == "Sam");
		Assert.Equal(0.6f - 0.096f, d.Opinions.Single(o => o.Who == "Sam").Faith, 3);
		Assert.Single(d.Beats);
		Assert.Empty(d.Rumors);
		Assert.Single(d.Adult);
	}

	// ---- 提示词 ----

	[Fact]
	public void PromptBlockListsWhatTheVillagerKnows()
	{
		(SocialDrama drama, ModConfig c, _) = Make();
		Assert.Equal("", drama.PromptBlock("Gus"));
		drama.ApplyGossip(Gossip("听说阿比盖尔撒谎了", "Haley", "Gus"));
		drama.ApplyGossip(Gossip("又听说阿比盖尔撒谎", "Haley", "Gus"));
		drama.RecordInfluence("Gus", "我支持海莉，她很善良", 8, null, 6);
		string block = drama.PromptBlock("Gus");
		Assert.Contains("【村民之间的事】", block);
		Assert.Contains("这一档可以闹得开", block);
		Assert.Contains("- 你对阿比盖尔：有点烦。起因：听到一条和海莉有关的八卦", block);
		Assert.Contains("农夫最近在你面前替海莉说过好话。这是农夫的说法，不一定是真的。原话：我支持海莉", block);
		Assert.Contains("你听过一个传闻（说的是阿比盖尔）", block);
		Assert.Contains("终究是传闻", block);
		Assert.Contains("没发生的事不要编", block);
		Assert.Contains("不知道的八卦不要抖出来", block);
		Assert.DoesNotContain("Abigail", block);
		Assert.DoesNotContain("。。", block);
		Assert.DoesNotContain(" 原话", block);
		Assert.Equal(("一句话。", "问吗？", "没标点。", ""), (DramaPrompt.Sentence("一句话。"), DramaPrompt.Sentence("问吗？"), DramaPrompt.Sentence("没标点"), DramaPrompt.Sentence(" ")));

		c.DramaHeat = "Cozy";
		Assert.DoesNotContain("这一档可以闹得开", drama.PromptBlock("Gus"));
		c.DramaOn = false;
		Assert.Equal("", drama.PromptBlock("Gus"));
	}

	[Fact]
	public void AdultLinesNameTheSpouse()
	{
		Assert.Contains("你和卡洛琳最近不太顺", DramaPrompt.AdultLine(new AdultStrain { Phase = AdultRules.Strained, Spouse = "Caroline" }, Display));
		Assert.Contains("眼下农夫对你的关注", DramaPrompt.AdultLine(new AdultStrain { Phase = AdultRules.Contemplating, Third = "农夫" }, Display));
		Assert.Contains("守住底线", DramaPrompt.AdultLine(new AdultStrain { Phase = AdultRules.Tempted, Spouse = "Caroline", Third = "农夫" }, Display));
	}

	[Fact]
	public void SameGossipMergesIntoOneRumor()
	{
		(SocialDrama drama, _, _) = Make(c => c.RumorsMoveOpinions = false);
		drama.ApplyGossip(Gossip("听说阿比盖尔撒谎了", "Haley", "Pierre"));
		GossipEntry again = Gossip("听说阿比盖尔撒谎了", "Emily", "Gus");
		again.ToldBy = "Gus";
		drama.ApplyGossip(again);
		Rumor r = Assert.Single(drama.Data.Rumors);
		Assert.Equal(new[] { "Emily", "Gus", "Haley", "Pierre" }, r.Heard);
		Assert.Equal(("Emily", "Gus"), (r.Source, r.Carrier));
		Assert.Equal(new[] { "Abigail" }, r.About);
	}

	[Fact]
	public void GossipyVillagersDistortFaster()
	{
		Rumor r = RumorRules.FromGossip("一件事", "spring", 5, 1, "", "Haley", new[] { "Haley" }, null, 1, new[] { "Sam" }, 5, DramaRules.Balanced);
		float before = r.Warp;
		Assert.True(RumorRules.SpreadForDay(r, 6, DramaRules.Balanced, new[] { "Sam", "Gus" }));
		Assert.Equal(before + 0.035f * 1.35f, r.Warp, 4);
		Assert.Equal("Haley", r.Carrier);
	}

	[Fact]
	public void InfluenceBeatsFollowTheAction()
	{
		(SocialDrama drama, _, _) = Make();
		drama.RecordInfluence("Gus", "我支持阿比盖尔，她很善良", 8, null, 10);
		drama.RecordInfluence("Pierre", "海莉让我很失望", 8, null, 10);
		Assert.Equal(new[] { "instigated_suspicion", "defense" }, drama.Data.Beats.Select(b => b.Kind));
		drama.RecordInfluence("Pierre", "海莉让我很失望，真糟糕", 8, null, 10);
		Assert.Equal(2, drama.Data.Beats.Count);
	}

	[Fact]
	public void PromptBlockFiltersAndCaps()
	{
		(SocialDrama drama, _, _) = Make(enabled: n => n != "Haley");
		DramaState d = drama.Data;
		foreach (string t in new[] { "Abigail", "Sam", "Gus", "Pam", "Haley" })
		{
			d.Opinions.Add(new Opinion { Who = "Pierre", About = t, Annoyance = t == "Pam" ? 0.6f : 0.3f, Attitude = t == "Pam" ? Stances.Badmouthing : Stances.Annoyed, Why = "因为" + t });
		}
		d.Influences.Add(new Influence { Talker = "Pierre", Aim = "Gus", Move = DramaText.Defend, Upshot = InfluenceRules.Rejected, OnDay = 9, Subject = "被拒的话" });
		d.Influences.Add(new Influence { Talker = "Pierre", Aim = "Haley", Move = DramaText.Defend, Upshot = InfluenceRules.Accepted, OnDay = 9, Subject = "关了的人" });
		for (int i = 0; i < 3; i++)
		{
			d.Influences.Add(new Influence { Talker = "Pierre", Aim = "Sam", Move = DramaText.Warn, Upshot = InfluenceRules.Accepted, OnDay = i, Subject = "第" + i + "次" });
		}
		d.Rumors.Add(new Rumor { Subject = "牵扯到关了的人", Heard = new List<string> { "Pierre", "Haley" }, Heat = 1f });
		for (int i = 0; i < 4; i++)
		{
			d.Rumors.Add(new Rumor { Subject = "传闻" + i, Heard = new List<string> { "Pierre" }, Heat = i / 10f });
		}
		string block = drama.PromptBlock("Pierre");
		string[] lines = block.Split('\n');
		Assert.Equal(3, lines.Count(l => l.StartsWith("- 你对")));
		Assert.StartsWith("- 你对Pam", lines.First(l => l.StartsWith("- 你对")));
		Assert.DoesNotContain("因为Haley", block);
		Assert.DoesNotContain("被拒的话", block);
		Assert.DoesNotContain("关了的人", block);
		Assert.Contains("第2次", block);
		Assert.Contains("第1次", block);
		Assert.DoesNotContain("第0次", block);
		Assert.Equal(3, lines.Count(l => l.StartsWith("- 你听过一个传闻")));
		Assert.DoesNotContain("传闻0", block);
		Assert.Equal("", drama.PromptBlock("Haley"));
	}

	[Fact]
	public void DayStartKeepsTheCaps()
	{
		(SocialDrama drama, _, _) = Make();
		DramaState d = drama.Data;
		for (int i = 0; i < 210; i++)
		{
			d.Opinions.Add(new Opinion { Who = "H" + i, About = "Sam", Annoyance = 0.5f });
		}
		for (int i = 0; i < 85; i++)
		{
			d.Rumors.Add(new Rumor { Id = "r" + i, Subject = "t", Heat = i / 100f, BornDay = 1, MovedDay = 99, DiesDay = 200 });
		}
		for (int i = 0; i < 45; i++)
		{
			d.Adult.Add(new AdultStrain { Villager = "N" + i, Force = i / 100f, DiesDay = 200 });
		}
		drama.OnDayStarted(100);
		Assert.Equal((200, 80, 40), (d.Opinions.Count, d.Rumors.Count, d.Adult.Count));
		Assert.Equal("r84", d.Rumors[0].Id);
		Assert.DoesNotContain(d.Rumors, r => r.Id == "r4");
		Assert.Equal("N44", d.Adult[0].Villager);
	}

	[Fact]
	public void CommonWordNamesNeedTheirInternalName()
	{
		Func<string, string> zh = n => n switch { "Wizard" => "法师", "Dwarf" => "矮人", _ => Display(n) };
		Assert.Empty(DramaText.Mentioned("魔法师和矮人的宝石", zh));
		Assert.Equal(new[] { "Wizard" }, DramaText.Mentioned("the Wizard said so", zh));
		Assert.Equal(new[] { "Sam" }, DramaText.Mentioned("山姆说的", zh));
	}

	[Fact]
	public void AdultSummaryUsesDisplayNamesAndBeatsAreClamped()
	{
		List<AdultStrain> list = new List<AdultStrain>();
		AdultRules.Apply(list, new Opinion { Who = "Pierre", About = "Caroline", Annoyance = 0.3f }, DramaSources.Gossip, 1, new AdultRules.Gates(true, false, false, DramaRules.Balanced), null, Display);
		Assert.StartsWith("皮埃尔和卡洛琳之间", list[0].Gist);
		Assert.Equal(2, new ModConfig { DramaBeatsDaily = 0 }.Normalize().DramaBeatsDaily);
		Assert.Equal(5, new ModConfig { DramaBeatsDaily = 10 }.Normalize().DramaBeatsDaily);
	}

	// ---- 敌意排名和存档 ----

	[Fact]
	public void HostilityNeedsEnoughHolders()
	{
		(SocialDrama drama, _, _) = Make();
		DramaState d = drama.Data;
		foreach (string h in new[] { "Pierre", "Gus", "Sam" })
		{
			d.Opinions.Add(new Opinion { Who = h, About = "Haley", Annoyance = 0.6f, Doubt = 0.4f, Esteem = 0.2f, Faith = 0.2f });
		}
		d.Opinions.Add(new Opinion { Who = "Gus", About = "Abigail", Annoyance = 1f, Doubt = 1f, Esteem = 0f, Faith = 0f });
		(string npc, float h2) = Assert.Single(drama.Hostility());
		Assert.Equal(("Haley", 0.65f), (npc, MathF.Round(h2, 3)));
		Assert.Equal("Abigail", drama.Hostility(1)[0].Npc);
	}

	[Fact]
	public void LoadedStateIsTidied()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write(SocialDrama.Path, "{\"schema\":1,\"data\":{\"opinions\":[{\"who\":\"Pierre\",\"about\":\"Haley\",\"annoyance\":2,\"trust\":0.2,\"attitude\":\"x\",\"why\":\"heard gossip\"}],\"rumors\":[{\"id\":\"r\",\"subject\":\"t\",\"heard\":[\"Sam\",\"sam\",\" \"],\"believability\":0.9,\"born_day\":3}],\"strains\":[{\"villager\":\"Pierre\",\"limit\":0}]}}");
		DocumentStore store = new DocumentStore(backend, NullLog.Instance);
		ModConfig c = new ModConfig().Normalize();
		SocialDrama drama = new SocialDrama(() => store, () => c, _ => true, _ => true, Display, NullLog.Instance);
		Opinion o = drama.Data.Opinions.Single();
		Assert.Equal((1f, Stances.Betrayed), (o.Annoyance, o.Attitude));
		Rumor r = drama.Data.Rumors.Single();
		Assert.Equal(new[] { "Sam" }, r.Heard);
		Assert.Equal(new[] { "Sam" }, r.Convinced);
		Assert.Equal(13, r.DiesDay);
		Assert.Equal(0.75f, drama.Data.Adult.Single().Limit);
	}
}
