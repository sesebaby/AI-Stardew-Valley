using System.Collections.Generic;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Social;
using Xunit;

namespace AISV.Core.Tests;

public class FamilyTests
{
	private static KissRules.Situation Ready() => new KissRules.Situation(true, 10, true, 1200, false, false, false, false, false, 64, 0);

	[Fact]
	public void KissMenuFollowsVanillaKissRules()
	{
		Assert.True(KissRules.CanOffer(Ready()));
		Assert.False(KissRules.CanOffer(Ready() with { Hearts = 9 }));
		Assert.False(KissRules.CanOffer(Ready() with { SleptInBed = false }));
		Assert.False(KissRules.CanOffer(Ready() with { TimeOfDay = 2200 }));
		Assert.False(KissRules.CanOffer(Ready() with { Moving = true }));
		Assert.False(KissRules.CanOffer(Ready() with { KissedToday = true }));
		Assert.False(KissRules.CanOffer(Ready() with { HoldingItem = true }));
		Assert.False(KissRules.CanOffer(Ready() with { MarriedOrRoommate = false }));
		// 站在上下方原版不亲。
		Assert.False(KissRules.CanOffer(Ready() with { DeltaX = 10, DeltaY = 64 }));
		Assert.True(KissRules.CanOffer(Ready() with { DeltaX = -64, DeltaY = 20 }));
	}

	[Fact]
	public void PetThoughtsKeepTheirDistance()
	{
		Assert.True(PetThoughtRules.CanThink(0, 3, -1, 900));
		Assert.False(PetThoughtRules.CanThink(3, 3, 900, 1200));
		Assert.False(PetThoughtRules.CanThink(1, 3, 900, 950));
		Assert.True(PetThoughtRules.CanThink(1, 3, 950, 1050));
		Assert.Equal(24, PetThoughtRules.Emote("sleepy"));
		Assert.True(PetThoughtRules.Rolls(0.1, 25));
		Assert.False(PetThoughtRules.Rolls(0.3, 25));
	}

	[Fact]
	public void PetThoughtParsesJsonOrPlainText()
	{
		Assert.Equal(("hungry", "饭盆是空的……"), PetThoughtRules.Parse("好的 {\"mood\":\"hungry\",\"line\":\"饭盆是空的……\"}"));
		Assert.Equal(("happy", "主人回来啦"), PetThoughtRules.Parse("“主人回来啦”"));
		Assert.Null(PetThoughtRules.Parse("{\"mood\":\"happy\"}"));
		Assert.Null(PetThoughtRules.Parse(""));
		Assert.True(PetThoughtRules.Parse("{\"line\":\"" + new string('喵', 60) + "\"}")!.Value.Line.Length <= PetThoughtRules.MaxChars);
	}

	[Fact]
	public void OwnChildrenAreChildrenWhateverTheirGrowthStage()
	{
		try
		{
			ChildSafety.SetFamily(new[] { "Pip" });
			// 会走路的孩子 Age 是 3、新生儿是 0：都按孩子对待。
			Assert.True(ChildSafety.IsChild("Pip", 3));
			Assert.True(ChildSafety.IsChild("Pip", 0));
			Assert.False(ChildSafety.AgeAllowsRomance("Pip", 0, false));
			Assert.False(ChildSafety.IsChild("Abigail", 1));
		}
		finally
		{
			ChildSafety.SetFamily(new string[0]);
		}
	}

	[Fact]
	public void ChildPromptKeepsItChildlike()
	{
		ChildContext c = new ChildContext { Key = "1_Pip", Name = "Pip", Boy = true, Stage = "刚学会走路", ParentName = "小麦", ParentRole = "妈妈", Home = new[] { "家里只有你和妈妈。" }, Day = 5 };
		string system = ChildPrompts.System(c, new List<ChildTurn> { new ChildTurn { Day = 5, Who = "note", Text = "今天跑到农场上躲猫猫，被妈妈找到了" } }, opening: true);
		Assert.Contains("不谈恋爱", system);
		Assert.Contains("躲猫猫", system);
		Assert.Contains("Pip是妈妈小麦的孩子", system);
		ChildContext carded = new ChildContext { Name = "Pip", ParentRole = "妈妈", Card = new CharacterCard { Personality = "爱吃胡萝卜的小捣蛋", DisplayName = "皮皮" } };
		Assert.Contains("爱吃胡萝卜", ChildPrompts.Persona(carded));
		Assert.Equal("皮皮", ChildPrompts.Called(carded));
		Assert.Null(ChildPrompts.Clean("  \"\" "));
	}
}
