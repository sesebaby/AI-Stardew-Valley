using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Storage;
using Xunit;

namespace AISV.Core.Tests;

public class CharacterCardTests
{
	[Fact]
	public void CardSurvivesTextRoundTripWithoutComputedFields()
	{
		CharacterCard card = new CharacterCard { DisplayName = "阿山", Personality = "爱弹吉他的\"年轻人\"", Speech = "哟" };
		string json = CharacterCard.ToJson(card);
		Assert.Contains("阿山", json);
		Assert.DoesNotContain("IsEmpty", json);
		Assert.DoesNotContain("Replaces", json);
		CharacterCard? back = CharacterCard.FromJson(json);
		Assert.NotNull(back);
		Assert.Equal(card.Personality, back!.Personality);
		Assert.Equal("哟", back.Speech);
	}

	[Fact]
	public void ImportToleratesNullsCaseAndRejectsJunk()
	{
		CharacterCard? card = CharacterCard.FromJson("{\"displayname\":\"小山\",\"Personality\":null,\"Lore\":null}");
		Assert.NotNull(card);
		Assert.Equal("小山", card!.DisplayName);
		Assert.Equal("", card.Personality);
		Assert.False(card.Replaces);
		Assert.True(CharacterCard.FromJson("{}")!.IsEmpty);
		Assert.Null(CharacterCard.FromJson("[1,2]"));
		Assert.Null(CharacterCard.FromJson("不是 JSON"));
		Assert.Null(CharacterCard.FromJson("  "));
	}

	[Fact]
	public void StoreNormalizesNullFieldsAndCachedNeverReadsDisk()
	{
		MemoryBackend backend = new MemoryBackend();
		backend.Write("Sam.json", "{\"DisplayName\":null,\"Appearance\":\"戴帽子\",\"Speech\":null}");
		CharacterCardStore store = new CharacterCardStore(backend, NullLog.Instance);
		Assert.Null(store.Cached("Sam"));
		CharacterCard? card = store.Get("Sam");
		Assert.NotNull(card);
		Assert.Equal("", card!.DisplayName);
		Assert.Same(card, store.Cached("Sam"));
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Sam", Card = card }, 2);
		Assert.Contains("戴帽子", p.Personality);
		Assert.Equal("", p.DisplayNameOverride);
	}

	[Fact]
	public void NameOnlyCardKeepsDefaultPersonaAndRenames()
	{
		NpcPersona p = PersonaResolver.Resolve(new PersonaInput { Name = "Sam", Card = new CharacterCard { DisplayName = "阿山" } }, 2);
		Assert.Equal("阿山", p.DisplayNameOverride);
		Assert.Contains("乔迪和肯特的大儿子", p.Personality);
	}
}
