using System;
using System.Linq;
using AISV.Core.Consequences;
using AISV.Core.Store;
using AISV.Core.Text;
using AISV.Game.Awareness;
using AISV.Game.Consequences;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace AISV.Game.Store;

/// <summary>
/// 卖赃物被认出：失主按自己的脾气当场翻脸（AI 按人设说，情绪拉满），东西拿走、镇长罚款；好感、心情、记仇（之后找上门来吵）、
/// 对手和爱看热闹的人取笑、八卦上周报。同一季被认出越多越重：第 2 次停业整顿，第 3 次被警长（装了 Law and Order）或镇长带走，当天结束。
/// 轻重和通用的部分在 <see cref="ConsequenceService"/>，这里是店里特有的：收回东西、停业、口碑、没收货架上的赃物。
/// </summary>
public sealed partial class MartService
{
	/// <summary>记进"看到听到"（失主之后找上门、对手来取笑都从这里来）。</summary>
	public Action<Observation>? Witness { get; set; }

	/// <summary>做错事的后果（ModEntry 接上）。</summary>
	public ConsequenceService? Consequences { get; set; }

	/// <summary>停业整顿中。</summary>
	public bool Suspended => State is MartState s && s.ClosedUntil > Today;

	public int ClosedUntil => State?.ClosedUntil ?? -1;

	private void Caught(StoreSale sale, NPC buyer, NPC? speaker)
	{
		if (State is not MartState s || Consequences is not ConsequenceService c)
		{
			return;
		}
		string owner = sale.StolenFrom.Length > 0 ? sale.StolenFrom : buyer.Name;
		bool own = string.Equals(owner, buyer.Name, StringComparison.OrdinalIgnoreCase);
		_ledger.Reclaim(owner, sale.ItemId, sale.Qty, sale.Quality);
		string farmer = Game1.player.Name;
		string place = Tr.Get("mart.fencePlace", "{{farmer}}的小超市", ("farmer", farmer));
		ConsequenceRecord? record = c.Report(new Misdeed
		{
			Kind = Misdeeds.Fence,
			Farmer = Game1.player.UniqueMultiplayerID,
			FarmerName = farmer,
			Npc = buyer.Name,
			Owner = owner,
			Item = sale.ItemName,
			ItemValue = sale.MarketPrice * sale.Qty,
			Place = place,
			Speaker = speaker,
			Situation = own
				? Tr.Get("mart.caughtOwnPrompt", "你在{{farmer}}开的农夫小超市里，一眼认出货架上卖的{{item}}正是你家前几天被偷的那件！小偷就是眼前这个店主。", ("farmer", farmer), ("item", sale.ItemName))
				: Tr.Get("mart.caughtKinPrompt", "你在{{farmer}}开的农夫小超市里，一眼认出货架上卖的{{item}}是{{owner}}家前几天被偷的那件！小偷就是眼前这个店主。", ("farmer", farmer), ("item", sale.ItemName), ("owner", Display(owner))),
			Fallback = st => FenceLine(st, own, sale.ItemName, Display(owner)),
			Bystanders = speaker == null ? Array.Empty<NPC>() : _visits.Where(v => v.Clone != speaker && v.Clone.currentLocation == speaker.currentLocation).Select(v => v.Clone).ToList(),
			BystanderLine = st => st switch
			{
				ReactionStyle.Snide => Tr.Get("mart.fenceSeeSnide", "噗——卖赃物被抓现行？笑死我了！"),
				ReactionStyle.Hurt => Tr.Get("mart.fenceSeeHurt", "天哪……怎么会这样……"),
				ReactionStyle.Cold => Tr.Get("mart.fenceSeeCold", "……啧。"),
				_ => Tr.Get("mart.fenceSeeHot", "什么？！这店卖偷来的东西？！")
			},
			// 记进"看到听到"：失主那边（之后照脾气找上门来吵、或者来挖苦），皮埃尔那边（对手幸灾乐祸）。
			Observe = st =>
			{
				if (Game1.getCharacterFromName(buyer.Name) is not NPC real || real.currentLocation is not GameLocation at)
				{
					return null;
				}
				NPC? rival = Game1.getCharacterFromName("Pierre");
				string summary = Tr.Get("mart.fenceSummary", "{{farmer}}的小超市在卖{{owner}}家被偷的{{item}}，被{{name}}当场认了出来。", ("farmer", farmer), ("owner", Display(owner)), ("item", sale.ItemName), ("name", Display(buyer.Name)));
				return new Observation("fenced_goods", farmer, buyer.Name, at.NameOrUniqueName, summary, real.Tile,
					Observation.Map(("owners", owner), ("item", sale.ItemName), ("locationDisplay", place)),
					AlsoLocation: rival?.currentLocation?.NameOrUniqueName, AlsoTile: rival?.Tile);
			},
			Immediate = (plan, _) =>
			{
				if (plan.ClosedDays > 0)
				{
					s.ClosedUntil = Math.Max(s.ClosedUntil, Today + plan.ClosedDays);
				}
				s.Reputation = Math.Clamp(s.Reputation - FenceRules.ReputationDrop(plan.Strike), -100, 100);
				Dirty();
			}
		});
		_log.Info($"{buyer.Name} 认出了赃物 {sale.ItemName}（失主 {owner}）{(record == null ? "，后果没开" : $"，这一季第 {record.Strike} 次")}。");
	}

	/// <summary>AI 没写成时失主按脾气说的话。</summary>
	private static string FenceLine(ReactionStyle st, bool own, string item, string owner) => st switch
	{
		ReactionStyle.Cold => own ? Tr.Get("mart.caughtOwnCold", "……这是我家的{{item}}。我记住你了。", ("item", item)) : Tr.Get("mart.caughtKinCold", "……{{owner}}家丢的{{item}}，在你这儿。我会记住的。", ("item", item), ("owner", owner)),
		ReactionStyle.Hurt => own ? Tr.Get("mart.caughtOwnHurt", "这……这是我家丢的{{item}}……我一直把你当朋友的，你怎么能这样……", ("item", item)) : Tr.Get("mart.caughtKinHurt", "这是{{owner}}家丢的{{item}}吧……原来是你……我真的好失望。", ("item", item), ("owner", owner)),
		ReactionStyle.Snide => own ? Tr.Get("mart.caughtOwnSnide", "哟，这不是我家丢的{{item}}吗？进货渠道挺特别啊，老板——偷来的东西卖得还挺贵！", ("item", item)) : Tr.Get("mart.caughtKinSnide", "哈，{{owner}}家丢的{{item}}跑你货架上了？这店的货源可真是“新鲜”啊！", ("item", item), ("owner", owner)),
		_ => own ? Tr.Get("mart.caughtOwn", "这不是我家丢的{{item}}吗？！你偷了我的东西还拿出来卖？我要去找镇长！", ("item", item)) : Tr.Get("mart.caughtKin", "等等，这{{item}}是{{owner}}家丢的那个吧？你居然卖赃物！我这就去告诉镇长！", ("item", item), ("owner", owner))
	};

	/// <summary>调试：让某个村民站到你旁边，当场认出一件赃物（照常记一笔成交、照常算后果，只是不掷骰子）。</summary>
	public string DebugFence(string name, string? owner)
	{
		if (State is not MartState s || Game1.getCharacterFromName(name) is not NPC buyer)
		{
			return "没店，或者没有这个人。";
		}
		NPC? speaker = null;
		if (IsShop(Game1.currentLocation))
		{
			Point at = Game1.player.TilePoint + new Point(0, 1);
			speaker = Clone(buyer, Game1.currentLocation, at, ShopperReason);
			speaker.faceDirection(0);
			NPC c = speaker;
			DelayedAction.functionAfterDelay(() => c.currentLocation?.characters.Remove(c), 15000);
		}
		StoreSale sale = new StoreSale { Source = StoreSale.Ours, TxId = $"{Today}-{s.NextTx++}", Buyer = buyer.Name, ItemId = "(O)24", ItemName = ItemRegistry.Create("(O)24").DisplayName, Qty = 1, UnitPrice = 0, MarketPrice = 35, Stolen = true, StolenFrom = owner ?? buyer.Name, Day = Today, Time = Game1.timeOfDay, Reclaimed = true };
		Sales.Record(sale);
		Caught(sale, buyer, speaker);
		int strikes = Consequences?.State?.StrikesOf(Game1.player.UniqueMultiplayerID, Misdeeds.Fence, "", ConsequenceRules.SeasonKey(Game1.year, Game1.seasonIndex)) ?? 0;
		return $"{name} 认出了赃物，这一季第 {strikes} 次。";
	}

	/// <summary>调试：现在就被带走。</summary>
	public string DebugArrest()
	{
		if (Consequences?.State is not ConsequenceState s)
		{
			return "先进存档。";
		}
		if (!s.ArrestPending.Contains(Game1.player.UniqueMultiplayerID))
		{
			s.ArrestPending.Add(Game1.player.UniqueMultiplayerID);
		}
		return "等你空下来就带走。";
	}

	/// <summary>没收店里（货架和库房）所有赃物，返回件数。</summary>
	public int Confiscate()
	{
		int n = 0;
		bool Stolen(Item? i) => i?.modData.TryGetValue("StardewThief/IsStolen", out string? v) == true && v == "true";
		foreach ((GameLocation _, Furniture f) in AllShelves())
		{
			StardewValley.Inventories.Inventory slots = MartShelves.Slots(f);
			for (int i = 0; i < slots.Count; i++)
			{
				if (Stolen(slots[i]))
				{
					n += slots[i].Stack;
					slots[i] = null;
				}
			}
		}
		StardewValley.Inventories.Inventory room = MartShelves.Stockroom();
		for (int i = 0; i < room.Count; i++)
		{
			if (Stolen(room[i]))
			{
				n += room[i].Stack;
				room[i] = null;
			}
		}
		if (Shops().FirstOrDefault() is GameLocation shop)
		{
			MartShelves.RefreshDisplay(shop);
		}
		return n;
	}
}
