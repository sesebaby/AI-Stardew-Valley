using System;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Dialogue;
using AISV.Core.Group;
using Microsoft.Xna.Framework;
using StardewValley;

namespace AISV.Game.Companions;

/// <summary>aisv_schedule、aisv_meetup、aisv_farmhelp 三个调试命令。详细结果写到 debug/。</summary>
public sealed class CompanionCommands
{
	private readonly ScheduleOverlayService _schedules;

	private readonly MeetupService _meetups;

	private readonly FarmHelpService _help;

	private readonly OfferDesk _desk;

	private readonly Func<string, object, string> _write;

	public CompanionCommands(ScheduleOverlayService schedules, MeetupService meetups, FarmHelpService help, OfferDesk desk, Func<string, object, string> write)
	{
		_schedules = schedules;
		_meetups = meetups;
		_help = help;
		_desk = desk;
		_write = write;
	}

	private static int Today => Game1.Date?.TotalDays ?? 0;

	private static string Arg(string[] a, int i, string fallback = "") => a.Length > i ? a[i] : fallback;

	private static int Int(string[] a, int i, int fallback) => a.Length > i && int.TryParse(a[i], out int v) ? v : fallback;

	public string Schedule(string[] args)
	{
		switch (Arg(args, 0, "dump").ToLowerInvariant())
		{
			case "plan" when args.Length >= 5:
			{
				// plan <村民> <地图> <x> <y> [开始] [结束] [天数偏移]
				int start = Int(args, 5, Clock.Add(Game1.timeOfDay, 60) / 10 * 10);
				ScheduleOverlay? o = _schedules.Plan(args[1], "debug", Today + Int(args, 7, 0), start, Int(args, 6, Clock.Add(start, 120)), args[2], Int(args, 3, 0), Int(args, 4, 0), 2, out string why);
				return o == null ? "排不上：" + why : $"排好了 {o.Id}：{o.Location}({o.X},{o.Y}) {o.Start}～{o.End}";
			}
			case "cancel" when args.Length >= 2:
				return _schedules.Cancel(args[1], PlanReasons.Cleared) ? "取消了" : "没有这段或者已经结束";
			case "clear":
				_schedules.DebugClear();
				return "清空了";
			case "npc" when args.Length >= 2:
			{
				NPC? npc = Game1.getCharacterFromName(args[1]);
				return npc == null ? "找不到村民 " + args[1] : $"{npc.Name}：{npc.currentLocation?.NameOrUniqueName}({npc.TilePoint.X},{npc.TilePoint.Y}) 接管 {_schedules.IsHolding(npc.Name)} 忽略日程 {npc.ignoreScheduleToday} 控制器 {npc.controller?.GetType().Name ?? "-"}";
			}
			default:
				return _write("schedule.json", new { today = Today, now = Game1.timeOfDay, overlays = _schedules.All() });
		}
	}

	public string Meetup(string[] args)
	{
		switch (Arg(args, 0, "dump").ToLowerInvariant())
		{
			case "arrange" when args.Length >= 4:
			{
				// arrange <村民> <地点> <时间> [天数偏移]
				Meetup? m = _meetups.Arrange(args[1], Today + Int(args, 4, 0), Int(args, 3, 1400), args[2], "调试", out string why);
				return m == null ? "排不上：" + why : $"约好了 {m.Id}（{m.OverlayId}）";
			}
			case "offer" when args.Length >= 2:
				// offer <村民> [when] [time] [where]：像当面答应一样聊完后问你
				return Offer(args[1], new NpcProposal { Type = OfferLedger.Meetup, Summary = "一起见个面吧。", When = Arg(args, 2, "today"), Time = Arg(args, 3), Where = Arg(args, 4, MeetupPlaces.TownSquare) }, chat: null);
			case "chat" when args.Length >= 3:
				// chat <村民|group> <村民> [when] [time] [where] [task]
				return Offer(args[2], new NpcProposal { Type = Arg(args, 6).Length > 0 ? OfferLedger.Work : OfferLedger.Meetup, Task = Arg(args, 6), Summary = "我过来找你。", When = Arg(args, 3, "today"), Time = Arg(args, 4, "now"), Where = Arg(args, 5, MeetupPlaces.Farm) }, chat: args[1]);
			case "answer" when args.Length >= 3:
			{
				string channel = args[1] == OfferDesk.GroupChannel ? OfferDesk.GroupChannel : OfferDesk.MessengerChannel(args[1]);
				return args[2] == "yes" ? _desk.Confirm(channel, out _) : _desk.Decline(channel);
			}
			case "cancel" when args.Length >= 2:
				return _meetups.CancelByPlayer(args[1]) ? "取消了" : "没有这个约或者已经结束";
			case "prompt" when args.Length >= 2:
				return _meetups.PromptBlock(args[1]) + "\n" + _help.PromptBlock(args[1]);
			default:
				return _write("meetups.json", _meetups.DumpData());
		}
	}

	/// <summary>chat 为空是当面；否则是信使（村民名）或居民群。</summary>
	private string Offer(string npcName, NpcProposal p, string? chat)
	{
		NPC? npc = Game1.getCharacterFromName(npcName);
		if (npc == null)
		{
			return "找不到村民 " + npcName;
		}
		if (chat == null)
		{
			PendingOffer? offer = _desk.Ledger.Review(npc.Name, p, Today, _desk.Hearts(npc.Name), false, false, out string reason);
			if (offer == null)
			{
				return "没收下：" + reason;
			}
			_meetups.Enqueue(offer);
			return "收下了，空下来就问你";
		}
		if (chat == OfferDesk.GroupChannel)
		{
			GroupLine[] lines = { new GroupLine(npc.Name, "好啊，我过去。") };
			return "群里那句：" + _desk.ReviewGroup(new GroupMeetup(npc.Name, p.When, p.Time, p.Where, p.Summary, p.Task), lines)[0].Text + " 卡片：" + (_desk.For(OfferDesk.GroupChannel) is ChatOffer g ? OfferDesk.OfferLabel(g) : "无");
		}
		NpcReaction reaction = new NpcReaction { Reaction = "好啊，我过去。", Proposal = p };
		_desk.ReviewText(npc, reaction, _desk.Hearts(npc.Name));
		return "回复：" + reaction.Reaction + " 卡片：" + (_desk.For(OfferDesk.MessengerChannel(npc.Name)) is ChatOffer o ? OfferDesk.OfferLabel(o) : "无");
	}

	public string FarmHelp(string[] args)
	{
		switch (Arg(args, 0, "dump").ToLowerInvariant())
		{
			case "arrange" when args.Length >= 3:
			{
				// arrange <村民> <活> [时间] [天数偏移] [价钱]
				FarmHelpJob? j = _help.Arrange(args[1], args[2], Today + Int(args, 4, 0), Int(args, 3, Clock.Add(Game1.timeOfDay, 70) / 10 * 10), Int(args, 5, 0), "调试", out string why);
				return j == null ? "排不上：" + why : $"排好了 {j.Id}（{j.OverlayId}）{j.Start}～{j.End}";
			}
			case "offer" when args.Length >= 3:
			{
				// offer <村民> <活> <when> [time]：像当面答应一样聊完后问你
				NPC? npc = Game1.getCharacterFromName(args[1]);
				if (npc == null)
				{
					return "找不到村民 " + args[1];
				}
				NpcProposal p = new NpcProposal { Type = OfferLedger.Work, Task = args[2], Summary = "我来帮你" + FarmHelpRules.TaskLabel(args[2]) + "。", When = Arg(args, 3, "tomorrow"), Time = Arg(args, 4) };
				PendingOffer? offer = _desk.Ledger.Review(npc.Name, p, Today, _desk.Hearts(npc.Name), false, false, out string reason);
				if (offer == null)
				{
					return "没收下：" + reason;
				}
				if (_help.Crew == null || !_help.Crew.IsAvailable)
				{
					return "Squad 不可用";
				}
				_help.Crew.Enqueue(offer);
				return $"收下了（{offer.Price}g），空下来就问你";
			}
			case "start" when args.Length >= 2:
				return _help.DebugStart(args[1]) ? "开工了" : "开不了工";
			case "end" when args.Length >= 2:
			{
				FarmHelpJob? j = _help.Find(args[1]);
				if (j == null || j.State != FarmHelpStates.Working)
				{
					return "没在干";
				}
				_help.Finish(j, "debug");
				return $"结算了：{j.Progress}/{j.Target}，退 {j.Refund}g";
			}
			case "cancel" when args.Length >= 2:
				return _help.CancelByPlayer(args[1]) ? "取消了，钱退了" : "取消不了";
			case "shop" when args.Length >= 3:
				return FarmHelpRules.ShopConflict(args[1], Today + Int(args, 3, 0), Int(args, 2, 900), FarmHelpRules.EndOf("water", Int(args, 2, 900))) ?? "不冲突";
			case "prompt" when args.Length >= 2:
				return _help.PromptBlock(args[1]);
			case "clear":
				_help.DebugClear();
				return "清空了";
			default:
				Farm? farm = Game1.getFarm();
				return _write("farmhelp.json", new { today = Today, now = Game1.timeOfDay, money = Game1.player?.Money, crew = _help.Crew?.Members(), jobs = _help.All(), done = _help.All().Select(j => new { j.Id, done = FarmHelpService.CountDone(j) }), farm = farm?.Name });
		}
	}
}
