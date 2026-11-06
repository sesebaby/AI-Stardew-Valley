using System.Collections.Generic;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Prompts;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class PersonaTests
{
	[Fact]
	public void BuiltInVillagerUsesOwnPersona()
	{
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Abigail" }, year: 2);
		Assert.Equal(PersonaSource.BuiltIn, p.Source);
		Assert.Contains("杂货店老板家的独女", p.Personality);
		Assert.Equal(PersonaSource.BuiltIn, PersonaResolver.Resolve(new PersonaInput { Name = "Mr. Qi" }, 2).Source);
	}

	[Fact]
	public void ManagedContentPackBeatsBuiltIn()
	{
		Dictionary<string, string> fields = new Dictionary<string, string>
		{
			["AIStardewValley/Managed"] = "true",
			["AIStardewValley/Personality"] = "新写法的人设",
			["AIStardewValley/Lore"] = "来自远方",
			["AIStardewValley/Credit"] = "某作者"
		};
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Abigail", CustomFields = fields }, 2);
		Assert.Equal(PersonaSource.ContentPack, p.Source);
		Assert.StartsWith("新写法的人设", p.Personality);
		Assert.Contains("原创角色背景：来自远方。", p.Personality);
		Assert.Contains("作者：某作者。", p.Personality);
	}

	[Fact]
	public void UnmanagedCustomFieldsDoNotReplaceBuiltIn()
	{
		Dictionary<string, string> fields = new Dictionary<string, string> { ["AIStardewValley/Personality"] = "x" };
		Assert.Equal(PersonaSource.BuiltIn, PersonaResolver.Resolve(new PersonaInput { Name = "Sam", CustomFields = fields }, 2).Source);
	}

	[Fact]
	public void CardReplacesOrSupplements()
	{
		NpcPersona replaced = PersonaResolver.Resolve(new PersonaInput { Name = "Sam", Card = new CharacterCard { Personality = "完全不同的山姆", Speech = "爱说“哟”" } }, 2);
		Assert.Equal(PersonaSource.Card, replaced.Source);
		Assert.Equal("完全不同的山姆 说话方式：爱说“哟”。", replaced.Personality);

		NpcPersona supplemented = PersonaResolver.Resolve(new PersonaInput { Name = "Sam", Card = new CharacterCard { Lore = "会弹钢琴", DisplayName = "山姆哥" } }, 2);
		Assert.Contains("乔迪和肯特的大儿子", supplemented.Personality);
		Assert.EndsWith("补充背景：会弹钢琴。", supplemented.Personality);
		Assert.Equal("山姆哥", supplemented.DisplayNameOverride);
	}

	[Fact]
	public void GeneratedPersonaFromTraitsAndModContext()
	{
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Zed", Traits = new NpcTraits(2, 1, 2, 2), SourceModId = "Someone.Mod" }, 2);
		Assert.Equal(PersonaSource.Generated, p.Source);
		Assert.Contains("小孩", p.Personality);
		Assert.Contains("Someone.Mod", PersonaResolver.ContextBlock(new PersonaInput { Name = p.Name, SourceModId = "Someone.Mod" }, "Town"));
		Assert.Equal(PersonaSource.Sve, PersonaResolver.Resolve(new PersonaInput { Name = "Sophia" }, 2).Source);
	}

	[Fact]
	public void KentIsAwayInYearOne()
	{
		Assert.False(PersonaResolver.IsInTown("Kent", 1));
		Assert.True(PersonaResolver.CanMention("Sam", "Kent", 1));
		Assert.False(PersonaResolver.CanMention("Abigail", "Kent", 1));
		Assert.True(PersonaResolver.CanMention("Abigail", "Kent", 2));
	}

	[Fact]
	public void SecretsNeedEnoughHearts()
	{
		Assert.Null(BuiltInPersonas.SecretIfClose("Clint", 4));
		Assert.NotNull(BuiltInPersonas.SecretIfClose("Clint", 5));
		Assert.Equal(GossipStyle.Spreader, BuiltInPersonas.GossipStyleOf("Caroline"));
		Assert.Equal(GossipStyle.Isolated, BuiltInPersonas.GossipStyleOf("Mr. Qi"));
	}

	[Fact]
	public void CardStoreReadsCachesAndDeletesEmpty()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("Sam.json", "{\"personality\":\"卡片\"}");
		backend.Write("Bad.json", "{oops");
		CharacterCardStore store = new CharacterCardStore(backend, NullLog.Instance);
		Assert.Equal("卡片", store.Get("Sam")!.Personality);
		Assert.Null(store.Get("Bad"));
		Assert.Null(store.Get("Nobody"));
		store.Save("Sam", new CharacterCard());
		Assert.Null(backend.Read("Sam.json"));
		Assert.Null(store.Get("Sam"));
	}

	[Fact]
	public void PromptTextSanitizesAndWraps()
	{
		Assert.Equal("ab\ncd", PromptText.Sanitize(" a\u0007b\ncd "));
		Assert.EndsWith("[已截断]", PromptText.Sanitize(new string('x', 50), 10));
		Assert.Equal("a", PromptText.Truncate("a\U0001F600", 2));
		string block = PromptText.Wrap("npc personality!", "内容");
		Assert.Contains("NPCPERSONALITY_BEGIN\n内容\nNPCPERSONALITY_END", block);
	}

	[Fact]
	public void MotifMemoriesAreSuppressedUnlessAboutTheFarmer()
	{
		Assert.True(PersonaPrompt.Suppress("Abigail", "Abigail went to the mines alone."));
		Assert.False(PersonaPrompt.Suppress("Abigail", "Abigail went to the mines with the farmer."));
		Assert.False(PersonaPrompt.Suppress("Abigail", "Abigail baked bread."));
		Assert.Equal(new[] { "游戏、矿井、长笛、神秘学" }, PersonaPrompt.TopicTags("abigail", "她又去矿井了"));
		List<string> kept = PersonaPrompt.FilterMemories("Sam", new[] { "Sam played guitar.", "Sam thanked the farmer for the gift.", "Sam thanked the farmer for the gift.", "" }, 5);
		Assert.Equal(new[] { "Sam thanked the farmer for the gift." }, kept);
		Assert.Contains("老生常谈", PersonaPrompt.PersonalityBlock("Sam", "开朗"));
		Assert.DoesNotContain("老生常谈", PersonaPrompt.PersonalityBlock("Nobody", "x"));
	}

	[Fact]
	public void TemplatesFillTokensAndBuilderDropsLowPriorityFirst()
	{
		MemoryBackend files = new MemoryBackend();
		files.Write("hello.txt", "你好 {{name}}，{{missing}}再见");
		PromptTemplates templates = new PromptTemplates(files, NullLog.Instance);
		Assert.Equal("你好 阿哲，再见", templates.Fill("hello", new Dictionary<string, string?> { ["name"] = "阿哲" }));
		Assert.Null(templates.Raw("nope"));

		PromptBuilder b = new PromptBuilder().Add("必须").Add("低优先级的很长一段", 1).Add("中优先级", 5).Add("  ");
		Assert.Equal("必须\n\n低优先级的很长一段\n\n中优先级", b.Build());
		Assert.Equal("必须\n\n中优先级", b.Build(12, out int dropped));
		Assert.Equal(1, dropped);
	}

	[Fact]
	public void ManagedPackWithSupplementCardReplacesSameFields()
	{
		Dictionary<string, string> fields = new Dictionary<string, string>
		{
			["AIStardewValley/Managed"] = "true",
			["AIStardewValley/Personality"] = "原创",
			["AIStardewValley/Lore"] = "旧背景",
			["AIStardewValley/Appearance"] = "红头发"
		};
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Oc", CustomFields = fields, Card = new CharacterCard { Lore = "新背景" } }, 2);
		Assert.Equal(PersonaSource.Card, p.Source);
		Assert.Contains("新背景", p.Personality);
		Assert.DoesNotContain("旧背景", p.Personality);
		Assert.Contains("红头发", p.Personality);
	}
}
