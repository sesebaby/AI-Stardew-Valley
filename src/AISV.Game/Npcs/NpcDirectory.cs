using System;
using System.Collections.Generic;
using AISV.Core.Compat;
using AISV.Core.Persona;
using StardewValley;
using StardewValley.GameData.Characters;

namespace AISV.Game.Npcs;

/// <summary>
/// 游戏里的村民：人设（角色卡 + 内容包 CustomFields + 内置）、名字、谁由 AI 驱动。
/// 世界快照在主线程上刷新（进存档、每天早上），查询只读内存。
/// </summary>
public sealed class NpcDirectory
{
	private readonly CharacterCardStore _cards;

	private readonly NpcEligibility _eligibility;

	public NpcDirectory(CharacterCardStore cards, NpcEligibility eligibility)
	{
		_cards = cards;
		_eligibility = eligibility;
	}

	public NameBook Names { get; } = new NameBook();

	public NpcEligibility Eligibility => _eligibility;

	/// <summary>主线程：重建名字表（角色卡改了名字也算）。</summary>
	public void RefreshNames() => Names.Rebuild(name => _cards.Cached(name)?.DisplayName?.Trim());

	/// <summary>主线程：重建名字表和世界里的村民快照。</summary>
	public void RefreshWorld()
	{
		RefreshNames();
		HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> sve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool sveInstalled = _eligibility.SveInstalled;
		foreach (GameLocation location in Game1.locations)
		{
			foreach (NPC npc in location.characters)
			{
				string name = npc?.Name?.Trim() ?? "";
				if (name.Length == 0)
				{
					continue;
				}
				if (npc!.IsVillager)
				{
					present.Add(name);
				}
				if (sveInstalled && (BuiltInPersonas.IsKnownSve(name) || SveMods.IsSve(_eligibility.OwnerModId(name) ?? "")))
				{
					sve.Add(name);
				}
				if (IsManaged(name))
				{
					managed.Add(name);
				}
			}
		}
		_eligibility.SetWorld(new NpcEligibility.World(present, sve, managed));
	}

	public bool IsEnabled(NPC npc) => _eligibility.IsEnabled(npc.Name, npc.IsVillager, IsManaged(npc.Name));

	public bool IsEnabled(string name) => _eligibility.IsEnabled(name);

	public NpcPersona Persona(string name, NPC? npc = null)
	{
		return PersonaResolver.Resolve(Input(name, npc), Game1.year);
	}

	/// <summary>换一张角色卡（null 是默认人设）看看算出来是什么，编辑页用。</summary>
	public NpcPersona Preview(string name, CharacterCard? card)
	{
		NPC? npc = Game1.getCharacterFromName(name, mustBeVillager: false);
		PersonaInput input = Input(name, npc);
		return PersonaResolver.Resolve(new PersonaInput
		{
			Name = input.Name,
			DisplayName = input.DisplayName,
			Traits = input.Traits,
			CustomFields = input.CustomFields,
			Card = card == null || card.IsEmpty ? null : card,
			SourceModId = input.SourceModId
		}, Game1.year);
	}

	/// <summary>这位村民的来历块（内容包、SVE、其他 mod）。</summary>
	public string ContextBlock(string name, NPC? npc, string? location) => npc == null ? "" : PersonaResolver.ContextBlock(Input(name, npc), location);

	private PersonaInput Input(string name, NPC? npc)
	{
		CharacterData? data = Game1.characterData != null && Game1.characterData.TryGetValue(name, out CharacterData? d) ? d : null;
		// 只有拿到 NPC 本人时才按性格数值描述，否则用通用的一句。
		NpcTraits? traits = npc != null ? new NpcTraits(npc.Age, npc.Manners, npc.SocialAnxiety, npc.Optimism) : null;
		return new PersonaInput
		{
			Name = name,
			DisplayName = Names.Display(name),
			Traits = traits,
			CustomFields = data?.CustomFields is { } fields ? new Dictionary<string, string>(fields) : null,
			Card = _cards.Get(name),
			SourceModId = _eligibility.OwnerModId(name)
		};
	}

	public CharacterCardStore Cards => _cards;

	private static bool IsManaged(string name)
	{
		if (Game1.characterData == null || !Game1.characterData.TryGetValue(name, out CharacterData? data) || data.CustomFields == null)
		{
			return false;
		}
		string value = data.CustomFields.TryGetValue(PersonaResolver.OwnPrefix + "Managed", out string? own) ? own?.Trim() ?? "" : "";
		return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1";
	}
}
