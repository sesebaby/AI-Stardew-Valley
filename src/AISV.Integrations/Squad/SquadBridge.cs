using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Companions;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using AISV.Core.Text;
using AISV.Game.Companions;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewValley;
using Microsoft.Xna.Framework;
using StardewValley.Characters;

namespace AISV.Integrations.Squad;

/// <summary>
/// 接 The Stardew Squad：招募键的闸门（孩子、有事的不跟，4～7 心先付钱）；AI 提议的"跟你去""帮你干活"聊完确认后招募；
/// 请来干活的人主动干约好的活；上门帮工时只在一片地里干。Squad 没装或同伴模块关着时什么都不做。
/// </summary>
public sealed class SquadBridge : ISquadCrew
{
	public const string SquadModId = "ThaliaFawnheart.TheStardewSquad";

	private const int FreeTicksBeforeAsking = 20;

	private const int MaxMembers = 3;

	private readonly IModHelper _helper;

	private readonly ILog _log;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, bool> _isBusy;

	private readonly Func<string, int> _hearts;

	private readonly CompanionRoster _roster = new CompanionRoster();

	private readonly Queue<PendingOffer> _toConfirm = new Queue<PendingOffer>();

	private ISquadApi? _api;

	private int _freeTicks;

	public SquadBridge(IModHelper helper, ILog log, Func<ModConfig> config, Func<string, bool> isBusy, Func<string, int> hearts)
	{
		_helper = helper;
		_log = log;
		_config = config;
		_isBusy = isBusy;
		_hearts = hearts;
	}

	/// <summary>和约见共用的提议账本。</summary>
	public OfferLedger Ledger { get; set; } = new OfferLedger();

	/// <summary>约好时间上门的帮工由这里接手（返回 true 就不再问招募）。</summary>
	public Func<PendingOffer, NPC, bool>? FarmVisit { get; set; }

	public bool IsAvailable => _api != null && _config().CompanionModuleOn;

	private static int Today => Game1.Date?.TotalDays ?? 0;

	public void OnGameLaunched()
	{
		if (!_helper.ModRegistry.IsLoaded(SquadModId))
		{
			_log.Info("没装 The Stardew Squad：村民跟随和帮工不可用。");
			return;
		}
		try
		{
			_api = _helper.ModRegistry.GetApi<ISquadApi>(SquadModId);
		}
		catch (Exception ex)
		{
			_log.Warn("读不了 The Stardew Squad 的 API（要装带 API 的版本）：" + ex.Message);
		}
		if (_api == null)
		{
			_log.Warn("这个版本的 The Stardew Squad 没有 API（要装带 API 的版本）：村民跟随和帮工不可用。");
			return;
		}
		_api.SetRecruitGate(Gate);
		_api.SetTaskOverride((npc, task) => _roster.TaskOverride(npc.Name, task, Today));
		_api.SetTaskModeOverride((npc, task) => _roster.ModeOverride(npc.Name, task, Today));
		try
		{
			_helper.ModRegistry.GetApi<ISquadBubbleApi>(SquadModId)?.SetBubble((npc, text) => NpcSpeech.Say(npc, text));
		}
		catch (Exception ex)
		{
			_log.Debug("这个版本的 The Stardew Squad 不能换气泡，用它自己的：" + ex.Message);
		}
		_api.AddRecruitedHandler(OnRecruited);
		_api.AddDismissedHandler(OnLeft);
		_log.Info("接上了 The Stardew Squad。");
	}

	public void OnSaveLoaded() => ResetDay();

	public void OnReturnedToTitle() => OnSaveLoaded();

	public void OnDayStarted() => ResetDay();

	private void ResetDay()
	{
		_roster.Clear();
		_toConfirm.Clear();
		_freeTicks = 0;
	}

	/// <summary>跟着农夫的村民：世界信息里多一行。</summary>
	public string PromptLine(string npc) => SquadMark.IsControlled(npc) ? _roster.PromptLine(npc, Today) : "";

	// ---------------- 招募键 ----------------

	private bool Gate(NPC npc, Farmer player)
	{
		if (!_config().CompanionModuleOn || npc is Pet || !Context.IsMainPlayer)
		{
			return true;
		}
		// 原地等着的队员：交给 Squad 叫回来。
		if (_api != null && _api.IsControlled(npc) && !_api.IsRecruited(npc))
		{
			return true;
		}
		string name = Display(npc);
		switch (_roster.Gate(npc.Name, IsChild(npc), _isBusy(npc.Name), _hearts(npc.Name), IsPartner(npc.Name, roommate: true), Today, out int price))
		{
			case GateDecision.RefuseChild:
				_log.Debug($"不让招募 {npc.Name}：孩子。");
				Game1.addHUDMessage(new HUDMessage(Tr.Get("squad.childRefused", "{{name}}还小，不能跟你出去。", ("name", name)), HUDMessage.error_type));
				return false;
			case GateDecision.RefuseBusy:
				_log.Debug($"不让招募 {npc.Name}：正忙。");
				Game1.addHUDMessage(new HUDMessage(Tr.Get("squad.busy", "{{name}}现在有事，走不开。", ("name", name)), HUDMessage.error_type));
				return false;
			case GateDecision.AskToPay:
				AskToPay(npc, Tr.Get("squad.hireAsk", "{{name}}愿意今天跟着你帮忙干活，要收 {{price}}g。", ("name", name), ("price", price)), price, () =>
				{
					_roster.MarkPaid(npc.Name, Today);
					if (!_api!.Recruit(npc, player, silent: false))
					{
						Refund(npc, price);
					}
				});
				return false;
			default:
				return true;
		}
	}

	// ---------------- AI 提议 ----------------

	public void Enqueue(PendingOffer offer)
	{
		if (offer.Type is OfferLedger.Follow or OfferLedger.Work && !_toConfirm.Contains(offer))
		{
			_toConfirm.Enqueue(offer);
		}
	}

	public void OnUpdateTicked()
	{
		if (_toConfirm.Count == 0)
		{
			return;
		}
		if (!Context.IsPlayerFree || Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp)
		{
			_freeTicks = 0;
			return;
		}
		if (++_freeTicks >= FreeTicksBeforeAsking)
		{
			_freeTicks = 0;
			Ask(_toConfirm.Dequeue());
		}
	}

	private void Ask(PendingOffer p)
	{
		NPC? npc = Game1.getCharacterFromName(p.Npc);
		if (npc == null || _api == null || !IsAvailable || p.ExpiresDay < Today)
		{
			_log.Debug($"{p.Npc} 的提议问之前就作废了。");
			Ledger.Resolve(p.Id);
			return;
		}
		if (FarmVisit?.Invoke(p, npc) == true)
		{
			return;
		}
		if (_api.IsRecruited(npc))
		{
			Ledger.Resolve(p.Id);
			if (p.Type == OfferLedger.Work)
			{
				_roster.SetJob(npc.Name, p.Task, Today, 0);
			}
			return;
		}
		string name = Display(npc);
		string summary = TextTools.CleanForGame(p.Summary);
		string question = p.Type == OfferLedger.Follow
			? Tr.Get("squad.proposalFollow", "{{name}}想跟你一起去：{{summary}}", ("name", name), ("summary", summary))
			: p.Price > 0
				? Tr.Get("squad.proposalWorkPaid", "{{name}}说要帮你干活：{{summary}}（今天收 {{price}}g）", ("name", name), ("summary", summary), ("price", p.Price))
				: Tr.Get("squad.proposalWork", "{{name}}说要帮你干活：{{summary}}", ("name", name), ("summary", summary));
		AskToPay(npc, question, p.Price, () =>
		{
			Ledger.Resolve(p.Id);
			if (p.Price > 0)
			{
				_roster.MarkPaid(npc.Name, Today);
			}
			_roster.SetJob(npc.Name, p.Type == OfferLedger.Work ? p.Task : OfferLedger.Follow, Today, p.Price);
			// 村民在对话里已经说好了，不用再说一遍招募台词。
			if (_api.Recruit(npc, Game1.player, silent: true))
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("squad.joined", "{{name}}跟着你了。", ("name", name)), HUDMessage.newQuest_type));
			}
			else
			{
				_roster.RemoveJob(npc.Name);
				Refund(npc, p.Price);
			}
		}, () => Ledger.Resolve(p.Id));
	}

	/// <summary>"好（价格）" / "不用了"。钱不够算不答应。</summary>
	private void AskToPay(NPC npc, string question, int price, Action accepted, Action? declined = null)
	{
		Response[] answers =
		{
			new Response("yes", price > 0 ? Tr.Get("squad.yesPaid", "好（{{price}}g）", ("price", price)) : Tr.Get("squad.yes", "好啊")),
			new Response("no", Tr.Get("squad.no", "不用了"))
		};
		Game1.currentLocation.createQuestionDialogue(question, answers, (who, answer) =>
		{
			if (answer != "yes")
			{
				declined?.Invoke();
				return;
			}
			if (price > 0)
			{
				if (who.Money < price)
				{
					Game1.addHUDMessage(new HUDMessage(Tr.Get("squad.notEnoughMoney", "钱不够：要 {{price}}g。", ("price", price)), HUDMessage.error_type));
					declined?.Invoke();
					return;
				}
				who.Money -= price;
				Game1.playSound("purchaseClick");
				_log.Debug($"付给 {npc.Name} {price}g，今天帮忙。");
			}
			accepted();
		});
	}

	private void Refund(NPC npc, int price)
	{
		if (price > 0)
		{
			Game1.player.Money += price;
			_roster.ClearPaid(npc.Name);
		}
		Game1.addHUDMessage(new HUDMessage(Tr.Get("squad.full", "你的小队已经满了。"), HUDMessage.error_type));
	}

	private void OnLeft(NPC npc)
	{
		_roster.OnLeft(npc.Name);
		_roster.RemoveSite(npc.Name);
	}

	private void OnRecruited(NPC npc)
	{
		_roster.OnJoined(npc.Name, Game1.timeOfDay);
		_log.Debug($"{npc.Name} 跟着农夫了{(_roster.JobTask(npc.Name, Today) is string task ? "（" + task + "）" : "")}。");
	}

	private static bool IsChild(NPC npc) => ChildSafety.IsChild(npc.Name, npc.Age);

	private static bool IsPartner(string npc, bool roommate) =>
		Game1.player != null && Game1.player.friendshipData.TryGetValue(npc, out Friendship? f) && (f.IsMarried() || f.IsDating() || f.IsEngaged() || (roommate && f.IsRoommate()));

	private static string Display(NPC npc)
	{
		string text = TextTools.CleanForGame(npc.displayName ?? "");
		return text.Length > 0 ? text : npc.Name;
	}

	// ---------------- 上门帮工 ----------------

	public bool StartSiteWork(NPC npc, string task, string location, Point center, int radius)
	{
		if (!IsAvailable || _api == null || !Context.IsMainPlayer)
		{
			return false;
		}
		if (_api.IsControlled(npc) || _api.GetMemberNames().Count >= MaxMembers)
		{
			_log.Debug($"{npc.Name} 没法用 Squad 在农场干活（已经被带着或者小队满了）。");
			return false;
		}
		_roster.SetJob(npc.Name, task, Today, 0);
		_roster.AddSite(npc.Name);
		if (_api.SetWorkSite(npc, location, center.X, center.Y, radius))
		{
			if (_api.Recruit(npc, Game1.player, silent: true))
			{
				return true;
			}
			_api.ClearWorkSite(npc);
		}
		_roster.RemoveJob(npc.Name);
		_roster.RemoveSite(npc.Name);
		return false;
	}

	/// <summary>离开工作地点、原地离队（之后由日程改写送回去）。</summary>
	public void EndSiteWork(NPC npc)
	{
		_roster.RemoveSite(npc.Name);
		if (_api == null)
		{
			return;
		}
		_api.ClearWorkSite(npc);
		if (_api.IsRecruited(npc))
		{
			_api.DismissInPlace(npc);
		}
		_roster.RemoveJob(npc.Name);
	}

	public IReadOnlyList<(string Npc, string Task, int Price, int? JoinedAt, bool Site)> Members()
	{
		if (_api == null)
		{
			return Array.Empty<(string, string, int, int?, bool)>();
		}
		try
		{
			return _api.GetMemberNames().Select(n => (n, _roster.JobTask(n, Today) ?? "", _roster.JobPrice(n, Today) ?? 0, _roster.JoinedAt(n), _roster.IsSiteWorker(n))).ToList();
		}
		catch (Exception ex)
		{
			_log.Debug("列不出队员：" + ex.Message);
			return Array.Empty<(string, string, int, int?, bool)>();
		}
	}

	// ---------------- 调试 ----------------

	/// <summary>aisv_squad 命令。</summary>
	public string Command(string[] args)
	{
		string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
		if (mode == "offer" && args.Length >= 3)
		{
			NPC? npc = Game1.getCharacterFromName(args[1]);
			if (npc == null)
			{
				return "找不到村民 " + args[1];
			}
			NpcProposal proposal = new NpcProposal { Type = args[2], Task = args.Length > 3 ? args[3] : "", Summary = args.Length > 4 ? string.Join(" ", args.Skip(4)) : "今天我陪你。" };
			PendingOffer? offer = Ledger.Review(npc.Name, proposal, Today, _hearts(npc.Name), IsPartner(npc.Name, roommate: false), IsChild(npc), out string reason);
			if (offer == null)
			{
				return "没收下：" + reason;
			}
			Enqueue(offer);
			return $"收下了：{offer.Price}g{(IsAvailable ? "" : "（Squad 不可用）")}";
		}
		if (mode == "gate" && args.Length >= 2)
		{
			NPC? npc = Game1.getCharacterFromName(args[1]);
			return npc == null ? "找不到村民 " + args[1] : Gate(npc, Game1.player) ? "放行（交给 Squad）" : "挡下了";
		}
		if (mode == "check" && args.Length >= 2)
		{
			NPC? npc = Game1.getCharacterFromName(args[1]);
			return npc == null ? "找不到村民 " + args[1] : $"{args[1]}：标记 {SquadMark.IsControlled(npc)}，有事 {_isBusy(npc.Name)}；提示：{PromptLine(npc.Name)}";
		}
		IEnumerable<string> members = _api?.GetMemberNames() ?? Array.Empty<string>();
		return $"Squad {(IsAvailable ? "可用" : "不可用")}，提议{(_config().VillagersOfferHelp ? "开" : "关")}；队员：{string.Join("、", members.Select(n => n + "(" + (_roster.JobTask(n, Today) ?? "-") + ")"))}；待确认 {_toConfirm.Count}，账本待办 {Ledger.Pending(Today).Count}，收 {Ledger.AcceptedCount} 拒 {Ledger.RejectedCount}。";
	}
}
