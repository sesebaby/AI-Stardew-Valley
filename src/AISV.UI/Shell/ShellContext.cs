using System;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;

namespace AISV.UI.Shell;

/// <summary>外壳各页要用的东西：社交模拟的数据、村民目录、好感、对话记录、设置（改了马上存）。</summary>
public sealed class ShellContext
{
	public ShellContext(SocialSystem social, NpcDirectory npcs, Relationships relationships, ConversationLog conversations, Func<ModConfig> config, Action<ModConfig> saveConfig)
	{
		Social = social;
		Npcs = npcs;
		Relationships = relationships;
		Conversations = conversations;
		Config = config;
		SaveConfig = saveConfig;
	}

	public SocialSystem Social { get; }

	public NpcDirectory Npcs { get; }

	public Relationships Relationships { get; }

	public ConversationLog Conversations { get; }

	public Func<ModConfig> Config { get; }

	public Action<ModConfig> SaveConfig { get; }

	public AISV.Core.Ai.ServerAccount? Account { get; set; }

	public AISV.Core.Ai.AiService? Ai { get; set; }

	/// <summary>这个版本带不带内置服务器。</summary>
	public Func<bool> HasServer { get; set; } = () => false;

	/// <summary>现在是不是连 AI星露谷服务器（否则是玩家自己的网关）。</summary>
	public Func<bool> UsesServer { get; set; } = () => false;

	public AISV.Game.Group.ResidentsService? Residents { get; set; }

	public AISV.Game.Messenger.MessengerService? Messenger { get; set; }

	public AISV.Game.Stories.StoryArcService? Arcs { get; set; }

	public AISV.Game.Outings.OutingService? Outings { get; set; }

	public AISV.Game.World.MarketService? Market { get; set; }

	public AISV.Game.Companions.MeetupService? Meetups { get; set; }

	public AISV.Game.Companions.FarmHelpService? FarmHelp { get; set; }

	public AISV.Game.Companions.OfferDesk? Offers { get; set; }

	public AISV.Game.World.CommissionService? Commissions { get; set; }

	public AISV.Game.World.ErrandService? Errands { get; set; }

	public AISV.Game.World.LedgerService? Ledger { get; set; }

	public AISV.Game.World.Ecosystem? Eco { get; set; }

	public AISV.Game.Social.AppsService? Apps { get; set; }

	/// <summary>角色卡试聊：村民、草稿、你说的话、回来的一句（没成时为 null）。不记进对话。</summary>
	public Action<string, AISV.Core.Persona.CharacterCard, string, Action<string?>>? TryChat { get; set; }

	public AISV.Game.Favors.FavorService? Favors { get; set; }

	public AISV.Game.Gifts.AiGiftService? Gifts { get; set; }

	public AISV.Game.Companions.SpouseStoryService? Stories { get; set; }

	public AISV.Game.Social.WeeklyNewsService? News { get; set; }

	public AISV.Game.Promo.SoftPromoService? Promo { get; set; }

	public AISV.Core.Diagnostics.ILog Log { get; set; } = AISV.Core.Diagnostics.NullLog.Instance;

	/// <summary>当前存档的数据（没进存档时为空）。</summary>
	public Func<AISV.Core.Storage.DocumentStore?> Store { get; set; } = () => null;

	public string Display(string npc) => Npcs.Names.Display(npc);
}
