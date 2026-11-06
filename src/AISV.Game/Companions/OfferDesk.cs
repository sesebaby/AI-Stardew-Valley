using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using AISV.Core.Text;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Companions;

/// <summary>聊天里等你回答的一条：日子、时间、地点都算好并检查过。Task 不空就是上门帮工。</summary>
public sealed record ChatOffer(string Channel, string Npc, int Day, int Time, string Place, string Summary, string ProposalId, bool Shifted, string Task = "", int Price = 0)
{
	public bool IsFarmHelp => Task.Length > 0;
}

/// <summary>
/// 提议的入口：当面说的过规则层后分给 Squad 或约见；信使和居民群里说的，排得上就在聊天里放一张卡片，
/// 排不上就把村民那句换成婉拒，免得嘴上答应了人却不来。
/// </summary>
public sealed class OfferDesk
{
	public const string GroupChannel = "group";

	private const int MaxTextSummary = 100;

	private readonly Func<ModConfig> _config;

	private readonly MeetupService _meetups;

	private readonly FarmHelpService _help;

	private readonly ILog _log;

	private readonly Dictionary<string, ChatOffer> _offers = new Dictionary<string, ChatOffer>(StringComparer.OrdinalIgnoreCase);

	public OfferDesk(Func<ModConfig> config, MeetupService meetups, FarmHelpService help, Func<string, int> hearts, ILog log)
	{
		_config = config;
		_meetups = meetups;
		_help = help;
		Hearts = hearts;
		_log = log;
		_meetups.ResolveOffer = Ledger.Resolve;
		_help.ResolveOffer = Ledger.Resolve;
	}

	public OfferLedger Ledger { get; } = new OfferLedger();

	public Func<string, int> Hearts { get; }

	public int Version { get; private set; }

	private ISquadCrew? Crew => _help.Crew;

	private bool SquadReady => Crew?.IsAvailable == true;

	private bool ProposalsOn => _config().VillagersOfferHelp && _meetups.Enabled && Context.IsMainPlayer;

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public static string MessengerChannel(string npc) => "messenger:" + npc;

	public void OnSaveLoaded() => Clear();

	public void OnReturnedToTitle() => Clear();

	public void OnDayStarted() => _offers.Clear();

	private void Clear()
	{
		Ledger.Clear();
		_offers.Clear();
	}

	/// <summary>回复 JSON 里的提议字段：只列这位村民眼下的交情够得着的。</summary>
	public string Json(bool texting, string npc, int hearts)
	{
		if (!ProposalsOn)
		{
			return "";
		}
		NPC? character = Game1.getCharacterFromName(npc);
		if (character != null && ChildSafety.IsChild(npc, character.Age))
		{
			return OfferPrompts.None;
		}
		if (Ledger.ProposedToday(npc, Today))
		{
			return OfferPrompts.AlreadyToday;
		}
		return OfferPrompts.Json(SquadReady, texting, hearts, IsPartner(npc));
	}

	/// <summary>居民群这一轮的约见说明。</summary>
	public string GroupRule() => ProposalsOn ? OfferPrompts.GroupRule(SquadReady) : "";

	// ---------------- 当面 ----------------

	/// <summary>当面那句显示之前：提议过规则层，约见再看排不排得上；不行就把那句换成婉拒，行就等聊完问你。</summary>
	public void ReviewFace(NPC npc, NpcReaction reaction, int hearts)
	{
		NpcProposal? proposal = reaction.Proposal;
		if (proposal == null)
		{
			return;
		}
		reaction.Proposal = null;
		string type = (proposal.Type ?? "").Trim().ToLowerInvariant();
		bool squadType = type is OfferLedger.Follow or OfferLedger.Work;
		if (!ProposalsOn || !(type == OfferLedger.Meetup || (squadType && SquadReady)))
		{
			return;
		}
		if (type == OfferLedger.Follow && Crew!.Members().Any(m => string.Equals(m.Npc, npc.Name, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(proposal.Summary))
		{
			proposal.Summary = PromptSafe((reaction.Reaction ?? "").Trim(), MaxTextSummary);
		}
		PendingOffer? offer = Ledger.Review(npc.Name, proposal, Today, hearts, IsPartner(npc.Name), ChildSafety.IsChild(npc.Name, npc.Age), out string reason);
		if (offer != null && type == OfferLedger.Meetup && !_meetups.TryResolve(offer, npc, out _, out _, out _, out _, out reason))
		{
			Ledger.Resolve(offer.Id);
			offer = null;
		}
		if (offer == null)
		{
			_log.Info($"{npc.Name} 的提议（{type}）不成：{reason}，那句换成婉拒。");
			reaction.Reaction = MeetupRules.DeclineLine(reason);
			reaction.ActionResponse = null;
			return;
		}
		_log.Info($"收下 {npc.Name} 的提议：{offer.Type} {offer.Task}，{offer.Price}g，第 {offer.ExpiresDay} 天前有效。");
		reaction.Proposal = proposal;
		if (type == OfferLedger.Meetup)
		{
			_meetups.Enqueue(offer);
		}
		else
		{
			Crew!.Enqueue(offer);
		}
	}

	// ---------------- 发消息 ----------------

	/// <summary>信使的回复写进记录之前：提议过规则层，排不上就换成婉拒。</summary>
	public void ReviewText(NPC npc, NpcReaction reaction, int hearts)
	{
		NpcProposal? p = reaction.Proposal;
		if (p == null)
		{
			return;
		}
		reaction.Proposal = null;
		if (!_config().VillagersOfferHelp)
		{
			return;
		}
		string type = (p.Type ?? "").Trim().ToLowerInvariant();
		bool farmTask = type == OfferLedger.Work && FarmHelpRules.Tasks.Contains((p.Task ?? "").Trim().ToLowerInvariant());
		if (type != OfferLedger.Meetup && !farmTask)
		{
			// 说"我跟你去""帮你钓鱼"：人不在身边，按约在你农场见算。
			if (type is not (OfferLedger.Follow or OfferLedger.Work))
			{
				return;
			}
			ToFarmMeetup(p);
		}
		if (string.IsNullOrWhiteSpace(p.Summary))
		{
			p.Summary = PromptSafe((reaction.Reaction ?? "").Trim(), MaxTextSummary);
		}
		PendingOffer? accepted = Ledger.Review(npc.Name, p, Today, hearts, IsPartner(npc.Name), ChildSafety.IsChild(npc.Name, npc.Age), out string reason);
		if (accepted == null && farmTask && reason.Contains(OfferLedger.HeartsWord))
		{
			// 还不够请来帮工，但答应过来：按约在你农场见。
			ToFarmMeetup(p);
			accepted = Ledger.Review(npc.Name, p, Today, hearts, IsPartner(npc.Name), ChildSafety.IsChild(npc.Name, npc.Age), out reason);
		}
		string channel = MessengerChannel(npc.Name);
		string? no = ReviewOffer(channel, npc.Name, accepted, reason);
		if (no != null)
		{
			reaction.Reaction = no;
			reaction.ActionResponse = null;
			return;
		}
		if (For(channel) is ChatOffer offer && offer.Shifted && offer.Day == Today)
		{
			reaction.Reaction = (reaction.Reaction ?? "").TrimEnd() + ArrivalLine(offer);
		}
	}

	private static void ToFarmMeetup(NpcProposal p)
	{
		p.Type = OfferLedger.Meetup;
		p.Task = "";
		if (string.IsNullOrWhiteSpace(p.Where))
		{
			p.Where = MeetupPlaces.Farm;
		}
	}

	/// <summary>居民群有人答应过来：先过规则层；排得上放卡片，排不上把他那句换成婉拒。</summary>
	public IReadOnlyList<GroupLine> ReviewGroup(GroupMeetup meetup, IReadOnlyList<GroupLine> lines)
	{
		int last = -1;
		for (int i = 0; i < lines.Count; i++)
		{
			if (string.Equals(lines[i].Speaker, meetup.Speaker, StringComparison.OrdinalIgnoreCase))
			{
				last = i;
			}
		}
		if (last < 0)
		{
			return lines;
		}
		string npc = lines[last].Speaker;
		NPC? character = Game1.getCharacterFromName(npc);
		bool work = FarmHelpRules.Tasks.Contains(meetup.Task);
		NpcProposal p = new NpcProposal
		{
			Type = work ? OfferLedger.Work : OfferLedger.Meetup,
			Task = work ? meetup.Task : "",
			Summary = meetup.Summary.Length > 0 ? meetup.Summary : lines[last].Text,
			When = meetup.When,
			Time = meetup.Time,
			Where = meetup.Where
		};
		string reason = "";
		PendingOffer? accepted = character == null ? null : Ledger.Review(npc, p, Today, Hearts(npc), IsPartner(npc), ChildSafety.IsChild(npc, character.Age), out reason);
		List<GroupLine> result = lines.ToList();
		string? no = ReviewOffer(GroupChannel, npc, accepted, reason);
		if (no != null)
		{
			result[last] = result[last] with { Text = no };
			return result;
		}
		if (For(GroupChannel) is ChatOffer offer && offer.Shifted && offer.Day == Today)
		{
			result[last] = result[last] with { Text = result[last].Text.TrimEnd() + ArrivalLine(offer) };
		}
		return result;
	}

	private string? ReviewOffer(string channel, string npcName, PendingOffer? accepted, string ledgerReason)
	{
		NPC? npc = Game1.getCharacterFromName(npcName);
		string why;
		if (!_meetups.Enabled || !Context.IsMainPlayer || npc == null)
		{
			why = MeetupRules.OffReason;
		}
		else if (accepted == null)
		{
			why = ledgerReason;
		}
		else if (accepted.Type == OfferLedger.Work)
		{
			if (_help.TryResolveChat(accepted, npc, out int hDay, out int hTime, out bool hShifted, out why))
			{
				_offers[channel] = new ChatOffer(channel, npc.Name, hDay, hTime, MeetupPlaces.Farm, accepted.Summary, accepted.Id, hShifted, accepted.Task, accepted.Price);
				_log.Info($"{npc.Name} 在 {channel} 说来帮工 {accepted.Task}：第 {hDay} 天 {hTime}（{accepted.Price}g），等你点。");
				Version++;
				return null;
			}
		}
		else if (_meetups.TryResolve(accepted, npc, out int day, out int time, out MeetupPlace place, out bool shifted, out why))
		{
			_offers[channel] = new ChatOffer(channel, npc.Name, day, time, place.Key, accepted.Summary, accepted.Id, shifted);
			_log.Info($"{npc.Name} 在 {channel} 约见：第 {day} 天 {time} 在 {place.Key}，等你点。");
			Version++;
			return null;
		}
		if (accepted != null)
		{
			Ledger.Resolve(accepted.Id);
		}
		_log.Info($"{npcName} 在 {channel} 的提议成不了（{why}），回复换成婉拒。");
		return MeetupRules.DeclineLine(why);
	}

	public ChatOffer? For(string channel) => _offers.TryGetValue(channel, out ChatOffer? o) ? o : null;

	public static string OfferYesText(ChatOffer offer) => offer.IsFarmHelp && offer.Price > 0 ? Tr.Get("meetup.yesPaid", "好（{{price}}g）", ("price", offer.Price)) : MeetupService.YesText;

	/// <summary>卡片上的整句。</summary>
	public static string OfferLabel(ChatOffer offer)
	{
		string name = MeetupService.NpcName(offer.Npc);
		string head = MeetupService.WhenText(offer.Day) + " " + Clock.Text(offer.Time);
		if (offer.IsFarmHelp)
		{
			string price = offer.Price > 0 ? Tr.Get("meetup.helpPrice", "（{{price}}g，干得少会退一部分）", ("price", offer.Price)) : Tr.Get("meetup.helpFree", "（不收钱）");
			return Tr.Get("messenger.helpOffer", "{{name}}来帮工：{{when}}", ("name", name), ("when", head + Tr.Get("meetup.helpTask", " 来农场帮你{{task}}", ("task", FarmHelpRules.TaskLabel(offer.Task))) + price));
		}
		string note = offer.Shifted ? Tr.Get("meetup.earliestNote", "（走过去最快这个点到）") : "";
		return Tr.Get("messenger.meetupOffer", "{{name}}约你见面：{{when}}", ("name", name), ("when", head + note + " · " + MeetupPlaces.Label(offer.Place, name)));
	}

	/// <summary>你点了"好"。返回说给聊天看的一句。</summary>
	public string Confirm(string channel, out bool arranged)
	{
		arranged = false;
		if (!_offers.Remove(channel, out ChatOffer? offer))
		{
			return "";
		}
		Ledger.Resolve(offer.ProposalId);
		Version++;
		NPC? npc = Game1.getCharacterFromName(offer.Npc);
		bool poor = offer.IsFarmHelp && offer.Price > 0 && Game1.player.Money < offer.Price;
		string text;
		if (offer.IsFarmHelp)
		{
			text = npc == null ? _meetups.CantArrangeText(offer.Npc, "other") : _help.PayAndArrange(Game1.player, npc.Name, offer.Task, offer.Day, offer.Time, offer.Price, offer.Summary, out arranged);
		}
		else
		{
			int time = offer.Time;
			MeetupPlace? place = MeetupPlaces.Get(offer.Place);
			// 犹豫的这会儿时间过去了：今天的约往后推。
			if (npc != null && place != null && offer.Day == Today)
			{
				int earliest = _meetups.EarliestToday(npc, place);
				if (Clock.Minutes(time) < Clock.Minutes(earliest))
				{
					time = Math.Max(earliest, place.OpenTime);
				}
			}
			Meetup? m = null;
			string why = PlanReasons.NoVillager;
			if (npc != null && place != null)
			{
				m = _meetups.Arrange(offer.Npc, offer.Day, time, offer.Place, offer.Summary, out why);
			}
			arranged = m != null;
			text = m == null
				? _meetups.CantArrangeText(offer.Npc, why)
				: Tr.Get("meetup.arrangedWith", "和{{npc}}约好了：{{when}}，{{place}}。", ("npc", MeetupService.NpcName(offer.Npc)), ("when", MeetupService.WhenText(m.Day) + " " + Clock.Text(m.Time)), ("place", MeetupService.PlaceLabel(m)));
		}
		Game1.addHUDMessage(new HUDMessage(TextTools.CleanForGame(text), poor ? HUDMessage.error_type : HUDMessage.newQuest_type));
		return text;
	}

	/// <summary>你点了"改天吧"。</summary>
	public string Decline(string channel)
	{
		if (!_offers.Remove(channel, out ChatOffer? offer))
		{
			return "";
		}
		Ledger.Resolve(offer.ProposalId);
		Version++;
		return Tr.Get("meetup.declinedInChat", "你跟{{npc}}说改天再约。", ("npc", MeetupService.NpcName(offer.Npc)));
	}

	private static string ArrivalLine(ChatOffer offer) => Tr.Get("meetup.arrivalLine", "（看了眼时间）路上要走一阵，大概{{time}}到。", ("time", Clock.Text(offer.Time)));

	private static string PromptSafe(string text, int max) => text.Length <= max ? text : text.Substring(0, char.IsHighSurrogate(text[max - 1]) ? max - 1 : max);

	private static bool IsPartner(string npc) => Game1.player != null && Game1.player.friendshipData.TryGetValue(npc, out Friendship? f) && (f.IsMarried() || f.IsDating() || f.IsEngaged());
}
