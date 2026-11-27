using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Consequences;
using AISV.Core.Store;
using Xunit;

namespace AISV.Core.Tests;

public sealed class MartTests
{
	[Fact]
	public void FishKeepsLongerInTheColdCaseAndSpoilsOnAmbientShelves()
	{
		Assert.Equal(4, Freshness.Life(Goods.Fish, Temps.Chilled));
		Assert.Equal(1, Freshness.Life(Goods.Fish, Temps.Ambient));
		Assert.Equal(6, Freshness.Life(Goods.Fish, Temps.Chilled, longerChilled: true));
		Assert.Equal(0, Freshness.Life(Goods.Mineral, Temps.Ambient));
		Assert.Equal(Freshness.SameDay, Freshness.Stage(6, 0));
		Assert.Equal(Freshness.Fresh, Freshness.Stage(6, 2));
		Assert.Equal(Freshness.Normal, Freshness.Stage(6, 3));
		Assert.Equal(Freshness.Near, Freshness.Stage(6, 5));
		Assert.Equal(Freshness.Expired, Freshness.Stage(6, 7));
		Assert.Equal(Freshness.Normal, Freshness.Stage(0, 40));
		Assert.True(Freshness.Appeal(Freshness.Expired) < Freshness.Appeal(Freshness.Near));
	}

	[Fact]
	public void GoodsGroupsFollowGameCategories()
	{
		Assert.Equal(Goods.Vegetable, Goods.Of(-75));
		Assert.Equal(Goods.Animal, Goods.Of(-5));
		Assert.Equal(Goods.Artifact, Goods.Of(0, artifact: true));
		Assert.Equal(Goods.General, Goods.Of(-999));
		Assert.False(ShelfKinds.Warm.Allows(Goods.Fish));
		Assert.True(ShelfKinds.Cold.Allows(Goods.Fish));
		Assert.Equal(ShelfKinds.Bin, ShelfKinds.Get("promo_bin"));
	}

	[Fact]
	public void MarketIsStablePerDayWithOneHotAndOneColdGroup()
	{
		MarketDay a = Market.For(42, 1, false), b = Market.For(42, 1, false);
		Assert.Equal(a.Hot, b.Hot);
		Assert.NotEqual(a.Hot, a.Cold);
		Assert.True(a.Of(a.Hot) > 1.3 && a.Of(a.Cold) < 0.8);
		Assert.Equal(1.0, a.Of(Goods.General));
	}

	[Fact]
	public void ShopTypeFavoursItsOwnGoodsAndLevelsUp()
	{
		Assert.Equal(1, ShopTypes.Level(0));
		Assert.Equal(5, ShopTypes.Level(9000));
		Assert.Equal(10, ShopTypes.Level(999999));
		Assert.True(ShopTypes.Demand(ShopTypes.Fishmonger, Goods.Fish, 1) > 1.2);
		Assert.True(ShopTypes.Demand(ShopTypes.Fishmonger, Goods.Fruit, 1) < 1);
		Assert.Equal(1.0, ShopTypes.Demand(ShopTypes.General, Goods.Fruit, 9));
		Assert.Equal(0, ShopTypes.Tolerance(ShopTypes.General, Goods.Fruit, 9));
	}

	[Fact]
	public void ArchetypesOnlyWantWhatTheShopHas()
	{
		HashSet<string> stocked = new HashSet<string> { Goods.Fish };
		for (double roll = 0; roll < 1; roll += 0.05)
		{
			Archetype a = Archetypes.Pick("Abigail", ShopTypes.Fishmonger, 1000, stocked, roll);
			Assert.True(a.Id is Archetypes.Browser or Archetypes.FishBuyer, a.Id + " " + roll);
		}
		// Gus 习惯买菜：杂货店里随便逛逛的人多，但他多半还是来买菜的。
		int cook = Enumerable.Range(0, 20).Count(i => Archetypes.Pick("Gus", ShopTypes.General, 1000, new HashSet<string> { Goods.Vegetable }, i / 20.0).Id == Archetypes.Cook);
		Assert.True(cook >= 10);
	}

	[Fact]
	public void BasketsStayWithinBudgetAndWishList()
	{
		List<Offer> offers = new List<Offer>
		{
			new Offer("(O)24", 70, 70, 0, 10, 8, false, Goods.Vegetable),
			new Offer("(O)145", 60, 60, 0, 10, 8, false, Goods.Fish),
			new Offer("(O)176", 100, 100, 0, 10, 8, false, Goods.Animal),
			new Offer("(O)348", 900, 900, 0, 10, 8, false, Goods.Artisan)
		};
		Archetype cook = Archetypes.Get(Archetypes.Cook);
		for (int seed = 0; seed < 30; seed++)
		{
			List<(int Index, int Qty)> basket = CustomerDemand.Basket(offers, cook, 300, new Random(seed));
			Assert.All(basket, p => Assert.Contains(offers[p.Index].Group, cook.Groups));
			Assert.True(basket.Sum(p => offers[p.Index].Price * p.Qty) <= 300);
		}
		Assert.Empty(CustomerDemand.Basket(offers, Archetypes.Get(Archetypes.Collector), 300, new Random(1)));
	}

	[Fact]
	public void ReviewsRewardFreshFairShopsAndPunishWaits()
	{
		(int good, string _) = ReviewRules.Score(new ReviewInput(true, 0.85, 0.8, 2, false, 4, 12, true, ReviewRules.CheckoutPerfect));
		(int bad, string why) = ReviewRules.Score(new ReviewInput(true, 1.9, -2, 0, true, 50, 2, false, ReviewRules.CheckoutWrong));
		Assert.Equal(5, good);
		Assert.Equal(1, bad);
		Assert.True(why is "pricey" or "stale" or "slow");
		Assert.Equal("notfound", ReviewRules.Score(new ReviewInput(false, 1, 0, 0, false, 0, 5, false, 0)).Reason);
		Assert.Equal(3, ReviewRules.Average(Array.Empty<Review>()));
		Assert.Equal(100, ReviewRules.Reputation(5));
	}

	[Fact]
	public void TrafficPeaksAtLunchAndEveningAndGrowsWithStars()
	{
		double noon = Traffic.Expected(6, 1, 0.3, 1230, false, false, false, 3, 0);
		double morning = Traffic.Expected(6, 1, 0.3, 930, false, false, false, 3, 0);
		Assert.True(noon > morning);
		Assert.True(Traffic.Expected(6, 1, 0.3, 1230, false, false, false, 5, 0) > noon);
		Assert.True(Traffic.Expected(6, 1, 0.3, 1230, false, true, false, 3, 0) < noon);
		Assert.Equal(0, Traffic.Expected(0, 1, 0.3, 1230, true, false, true, 5, 1));
		Assert.Equal(4, Traffic.Arrivals(9, 0));
	}

	[Fact]
	public void CheckoutNeedsExactChangeAndTipsFastCashiers()
	{
		for (double roll = 0; roll < 1; roll += 0.07)
		{
			int paid = Checkout.Paid(337, roll);
			Assert.True(paid >= 337);
		}
		Assert.Equal((true, 33), Checkout.Result(337, 500, 163, 5));
		Assert.Equal((true, 16), Checkout.Result(337, 500, 163, 15));
		Assert.Equal((true, 0), Checkout.Result(337, 500, 163, 30));
		Assert.False(Checkout.Result(337, 500, 160, 5).Ok);
	}

	[Fact]
	public void PromotionsBoostOnlyWhileRunning()
	{
		List<Promotion> promos = new List<Promotion> { new Promotion { Kind = Promotion.Poster, Target = Goods.Fish, From = 10, Until = 16 }, new Promotion { Kind = Promotion.Window, Target = "(O)24", From = 10, Until = 99 } };
		Assert.Equal(1.4, Promotions.PosterBoost(promos, Goods.Fish, 12));
		Assert.Equal(1.0, Promotions.PosterBoost(promos, Goods.Fish, 17));
		Assert.Equal(1.8, Promotions.WindowBoost(promos, "(O)24", 50));
		Assert.Equal(0.15, Promotions.TrafficBonus(promos, 12), 3);
	}

	[Fact]
	public void SuppliersSellAboveShippingPriceAndStaffWagesDropWithFriendship()
	{
		Assert.Equal(49, Suppliers.Cost(35));
		Assert.All(Suppliers.All, s => Assert.Equal(4, s.Seasons.Length));
		Assert.Equal(200, Staff.Wage(Staff.Cashier, 4, false, "Emily", 7));
		Assert.Equal(160, Staff.Wage(Staff.Cashier, 8, false, "Emily", 7));
		Assert.True(Staff.Wage(Staff.Cashier, 4, true, "Sam", 7) < 200);
		Assert.True(Staff.Wage(Staff.Cashier, 4, false, "Emily", 28) < 200);
	}

	[Fact]
	public void FenceStrikesEscalateAndResetEachSeason()
	{
		Assert.Equal(1, FenceRules.Strike(5, 2, 6));
		Assert.Equal(3, FenceRules.Strike(6, 2, 6));
		Assert.Equal(300, FenceRules.Fine(35, 1));
		Assert.Equal(600, FenceRules.Fine(35, 2));
		Assert.Equal(9000, FenceRules.Fine(2000, 3));
		Assert.Equal(0, FenceRules.ClosedDays(1));
		Assert.Equal(3, FenceRules.ClosedDays(2));
		Assert.Equal(7, FenceRules.ClosedDays(4));
		Assert.False(FenceRules.Arrest(2, true));
		Assert.True(FenceRules.Arrest(3, true));
		Assert.False(FenceRules.Arrest(3, false));
	}

	[Fact]
	public void FenceStylesDifferByPersonality()
	{
		Assert.Equal(ReactionStyle.Hot, FenceRules.StyleOf("Shane"));
		Assert.Equal(ReactionStyle.Cold, FenceRules.StyleOf("Sebastian"));
		Assert.Equal(ReactionStyle.Hurt, FenceRules.StyleOf("penny"));
		Assert.Equal(ReactionStyle.Snide, FenceRules.StyleOf("Alex"));
		Assert.Null(FenceRules.StyleOf("Ripley"));
		Assert.Equal(ReactionStyle.Snide, FenceRules.Parse(" Snide "));
		Assert.Equal(ReactionStyle.Hot, FenceRules.Parse("???"));
		Assert.True(FenceRules.FriendshipFactor(ReactionStyle.Cold) > FenceRules.FriendshipFactor(ReactionStyle.Snide));
		Assert.True(FenceRules.MoodDrop(ReactionStyle.Hurt) < FenceRules.MoodDrop(ReactionStyle.Snide));
	}
}
