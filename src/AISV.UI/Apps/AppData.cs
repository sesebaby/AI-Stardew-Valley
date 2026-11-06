using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Dialogue;
using AISV.Core.Favors;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Social.Gossip;
using AISV.Core.Social.Drama;
using AISV.Core.Social.Personality;
using AISV.Core.Stories;
using AISV.Core.Social.Web;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Shell;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.WorldMaps;

namespace AISV.UI.Apps;

/// <summary>一位村民今天的心情预报。</summary>
internal sealed record MoodEntry(string Npc, string Name, NpcMood Mood, MoodSky Sky, IReadOnlyList<MoodCauseItem> Causes, IReadOnlyList<MoodAdvice> Advice)
{
	public bool Birthday => Causes.Any(c => c.Kind == MoodCause.Birthday);
}

/// <summary>一条八卦和推出来的八卦树。</summary>
internal sealed record GossipCase(GossipEntry Entry, string Fact, string Told, Trail Trail);

internal sealed record ReviewEntry(string Npc, string Name, ReviewInput Input, Review Review);

/// <summary>从存档数据里读出各应用要用的东西（只读，读不到就当没有）。</summary>
internal static class AppData
{
	public static int Today => Game1.Date.TotalDays + 1;

	public static string Player => Game1.player?.Name ?? "";

	public static List<MoodEntry> Moods(ShellContext ctx) => Safe(ctx, "今天的心情", () =>
	{
		List<MoodEntry> list = new List<MoodEntry>();
		foreach (NpcMood m in ctx.Social.Moods.All.Where(m => ctx.Npcs.IsEnabled(m.Npc) && Game1.getCharacterFromName(m.Npc, mustBeVillager: false) != null))
		{
			IReadOnlyList<MoodCauseItem> causes = MoodForecast.Causes(m.Because);
			bool birthday = causes.Any(c => c.Kind == MoodCause.Birthday);
			MoodSky sky = MoodForecast.Sky(m.Temper, m.Level, birthday);
			list.Add(new MoodEntry(m.Npc, ctx.Display(m.Npc), m, sky, causes, MoodForecast.Advice(sky, causes)));
		}
		return list.OrderBy(e => e.Name, StringComparer.CurrentCulture).ToList();
	});

	/// <summary>年度回顾的事实：每种数据单独读，一种读不出来不影响别的。</summary>
	public static YearFacts Year(ShellContext ctx)
	{
		string player = Player;
		List<TalkFact> talks = Safe(ctx, "聊天记录", () =>
		{
			List<TalkFact> list = new List<TalkFact>();
			foreach (string npc in ctx.Conversations.Tracked().Where(ctx.Npcs.IsEnabled))
			{
				foreach ((int year, string who, int points) in ctx.Conversations.AllTalks(npc))
				{
					if (string.IsNullOrWhiteSpace(who) || string.Equals(who, player, StringComparison.OrdinalIgnoreCase))
					{
						list.Add(new TalkFact(npc, year, points));
					}
				}
			}
			return list;
		});
		List<GiftFact> gifts = Safe(ctx, "送礼记录", () => ctx.Social.Gifts.Everything().Where(g => ctx.Npcs.IsEnabled(g.Recipient)).Select(g => new GiftFact(g.Recipient, ShellText.Clean(g.What), g.Liking, g.Year)).ToList());
		List<GossipFact> gossip = Safe(ctx, "八卦", () => ctx.Social.Gossip.Feed().Select(e => new GossipFact(ShellText.Clean(string.IsNullOrWhiteSpace(e.Rumor) ? e.Fact : e.Rumor), e.StartedBy ?? "",
			e.KnownTo.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Count(), e.Year, e.TotalDay)).ToList());
		List<ArcFact> arcs = Safe(ctx, "小剧场", () => (ctx.Arcs?.Arcs.All ?? Array.Empty<StoryArc>()).Where(a => a.State == StoryArc.Resolved).Select(a =>
		{
			int day = a.EndedOn ?? 0;
			return new ArcFact(ShellText.Clean(a.Title), a.Cast.ToList(), day > 0 ? YearReview.YearOf(day) : a.BornYear, day);
		}).ToList());
		List<MilestoneFact> milestones = Safe(ctx, "里程碑", () => ctx.Social.Milestones.Everything().Where(p => ctx.Npcs.IsEnabled(p.Npc))
			.Select(p => new MilestoneFact(p.Npc, p.Milestone.Kind, p.Milestone.Year, Dates.TotalDays(p.Milestone.Day, p.Milestone.Season, p.Milestone.Year), ShellText.Clean(MilestoneBook.Describe(p.Milestone)))).ToList());
		List<LetterFact> letters = Safe(ctx, "来信", () => ctx.Social.Letters.All().Where(l => l.Sent).Select(l => new LetterFact(l.From, l.Year)).ToList());
		return new YearFacts(talks, gifts, gossip, arcs, milestones, letters);
	}

	/// <summary>日程：镇上的村民（按交情排）、各自现在在哪，今天的一站站和你的约见。</summary>
	public static List<(string Npc, string Name, string Now)> Agenda(ShellContext ctx, Dictionary<string, List<AgendaStop>> stops) => Safe(ctx, "日程", () =>
	{
		stops.Clear();
		List<(string, string, string, int)> list = new List<(string, string, string, int)>();
		foreach (NPC n in Utility.getAllVillagers().Where(n => n != null && n.IsVillager && n.CanSocialize && ctx.Npcs.IsEnabled(n.Name)))
		{
			List<AgendaStop> day = ctx.Eco?.TodaySchedule(n.Name) is { } info
				? Core.Apps.Agenda.Build(info.Stops.Select(s => (s.Time, s.Location, s.EndBehavior)))
				: new List<AgendaStop>();
			foreach (var m in ctx.Meetups?.Upcoming().Where(m => m.Day == Game1.Date.TotalDays && m.Npc.Equals(n.Name, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<Core.Companions.Meetup>())
			{
				day.Add(new AgendaStop(m.Time, AISV.Game.Companions.MeetupService.PlaceLabel(m), Tr.Get("agenda.meetup", "和你约好见面"), WithYou: true, Note: m.Summary));
			}
			stops[n.Name] = day.OrderBy(s => s.Time).ToList();
			string now = Tr.Get("agenda.at", "现在在{{place}}", ("place", Places.Name(n.currentLocation?.Name)));
			list.Add((n.Name, ctx.Display(n.Name), now, ctx.Relationships.EffectiveHearts(n.Name)));
		}
		return list.OrderByDescending(x => x.Item4).ThenBy(x => x.Item2, StringComparer.CurrentCulture).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
	});

	/// <summary>村民日记要用的记录：每种单独读，一种读不出来不影响别的。</summary>
	public static VillagerFacts VillagerFacts(ShellContext ctx, string npc)
	{
		int today = GameState.Today.TotalDays;
		return new VillagerFacts
		{
			Npc = npc,
			Name = ctx.Display(npc),
			Today = today,
			AfterwordDays = ctx.Config().AfterwordDays,
			Profile = One(ctx, "情感档案", () => ctx.Social.Feelings.Find(npc)),
			Noticing = One(ctx, "今天的心事", () => ctx.Awareness?.DiaryView(npc)) ?? NoticingView.Off,
			Favors = Safe(ctx, "托付", () => ctx.Favors?.Favors.All.ToList() ?? new List<NpcFavor>()),
			ItemRequests = Safe(ctx, "物品请求", () => ctx.Favors?.Requests.For(npc).ToList() ?? new List<ItemRequestFavor>()),
			Arcs = Safe(ctx, "小剧场", () => ctx.Arcs?.Arcs.All.ToList() ?? new List<StoryArc>()),
			Edges = Safe(ctx, "村民关系", () => ctx.Social.Web.Edges.ToList()),
			Personality = One(ctx, "性格变化", () => ctx.Social.Personality.Find(npc)),
			Rumors = Safe(ctx, "传闻", () => ctx.Social.Drama.RumorsKnownBy(npc, today).ToList()),
			Opinions = Safe(ctx, "看法", () => ctx.Social.Drama.OpinionsHeldBy(npc).ToList()),
			Display = n => string.Equals(n, RumorRules.Farmer, StringComparison.OrdinalIgnoreCase) ? Tr.Get("vdiary.you", "你") : ctx.Display(n)
		};
	}

	private static T? One<T>(ShellContext ctx, string what, Func<T?> read) where T : class
	{
		try
		{
			return read();
		}
		catch (Exception ex)
		{
			ctx.Log.Warn($"读不出{what}：{ex.Message}");
			return null;
		}
	}

	/// <summary>大家都在哪：每位村民现在的位置和今天的路线。</summary>
	public static List<WhereInput> WhereInputs(ShellContext ctx) => Safe(ctx, "村民的位置", () =>
	{
		List<WhereInput> list = new List<WhereInput>();
		foreach (NPC n in Utility.getAllVillagers().Where(n => n != null && n.CanSocialize).GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
		{
			bool hasRoute = n.Schedule != null && !n.ignoreScheduleToday && n.followSchedule;
			List<WhereStop> stops = hasRoute
				? n.Schedule!.Where(p => !string.IsNullOrWhiteSpace(p.Value?.targetLocationName)).Select(p => new WhereStop(p.Key, p.Value.targetLocationName, PlaceName(p.Value.targetLocationName))).ToList()
				: new List<WhereStop>();
			string? place = n.currentLocation?.Name;
			string? heading = n.controller?.pathToEndPoint?.Count > 0 ? n.DirectionsToNewLocation?.targetLocationName : null;
			list.Add(new WhereInput(n.Name, ctx.Display(n.Name), place, PlaceName(place), n.DefaultMap, heading, PlaceName(heading), n.IsInvisible || place == null, hasRoute, stops));
		}
		return list;
	});

	/// <summary>村民在世界地图上的位置（像素，1 倍）。地图上找不到的不在里面。</summary>
	public static Dictionary<string, (string Region, float X, float Y)> WherePoints(ShellContext ctx)
	{
		Dictionary<string, (string, float, float)> points = new Dictionary<string, (string, float, float)>(StringComparer.OrdinalIgnoreCase);
		foreach (NPC n in Utility.getAllVillagers().Where(n => n?.currentLocation != null && n.CanSocialize && !n.IsInvisible))
		{
			try
			{
				if (WorldMapManager.GetPositionData(n.currentLocation, n.TilePoint) is { } at)
				{
					Vector2 pixel = at.GetMapPixelPosition();
					points[n.Name] = (at.Data.Region.Id, pixel.X / 4f, pixel.Y / 4f);
				}
			}
			catch (Exception ex)
			{
				ctx.Log.Debug($"大家都在哪：{n.Name} 放不到地图上：{ex.Message}");
			}
		}
		return points;
	}

	public static string HomeRegion(ShellContext ctx)
	{
		try
		{
			return WorldMapManager.GetPositionData(Game1.getFarm(), Point.Zero)?.Data.Region.Id ?? "";
		}
		catch (Exception ex)
		{
			ctx.Log.Debug($"大家都在哪：农场不在地图上：{ex.Message}");
			return "";
		}
	}

	/// <summary>地点叫什么：先用自己的说法，再用游戏的名字；都没有时用出口通向的地方（mod 的小房间）。</summary>
	public static string PlaceName(string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return Tr.Get("where.somewhere", "某个地方");
		}
		if (Places.IsKnown(id))
		{
			return Places.Name(id);
		}
		GameLocation? location = Game1.getLocationFromName(id);
		if (location == null)
		{
			return id;
		}
		if (HasOwnName(location))
		{
			return location.DisplayName;
		}
		foreach (Warp warp in location.warps)
		{
			if (Places.IsKnown(warp.TargetName))
			{
				return Places.Name(warp.TargetName);
			}
			if (Game1.getLocationFromName(warp.TargetName) is GameLocation target && !string.Equals(target.Name, location.Name, StringComparison.OrdinalIgnoreCase) && HasOwnName(target))
			{
				return target.DisplayName;
			}
		}
		return id;
	}

	private static bool HasOwnName(GameLocation location) => !string.IsNullOrWhiteSpace(location.DisplayName) && !string.Equals(location.DisplayName, location.Name, StringComparison.OrdinalIgnoreCase);

	/// <summary>最先看谁：伴侣，其次交情最深的。</summary>
	public static string? Closest(ShellContext ctx, IEnumerable<string> npcs)
	{
		string? spouse = Game1.player?.spouse;
		List<string> list = npcs.ToList();
		return list.FirstOrDefault(n => n.Equals(spouse, StringComparison.OrdinalIgnoreCase)) ?? list.OrderByDescending(ctx.Relationships.EffectiveHearts).FirstOrDefault();
	}

	/// <summary>星座：每位村民今天的运势（宜送 / 忌送挑和全村比最特别的一类）。</summary>
	public static List<Fortune> Fortunes(ShellContext ctx) => Safe(ctx, "星座", () =>
	{
		ulong id = Game1.uniqueIDForThisGame;
		List<(NPC Npc, int Sign, Dictionary<string, double> Loved, Dictionary<string, double> Hated)> people = new List<(NPC, int, Dictionary<string, double>, Dictionary<string, double>)>();
		foreach (NPC n in Utility.getAllVillagers().Where(n => n != null && n.IsVillager && n.CanSocialize && ctx.Npcs.IsEnabled(n.Name)))
		{
			int sign = Horoscope.SignOf(n.Birthday_Season, n.Birthday_Day);
			if (sign < 0)
			{
				continue;
			}
			IEnumerable<TasteItem> Tastes(string taste) => ctx.Eco?.PersonalTastes(n.Name, taste).Select(t => new TasteItem(t.Category, t.Price)) ?? Enumerable.Empty<TasteItem>();
			people.Add((n, sign, Horoscope.Weights(Tastes("loved"), Tastes("liked")), Horoscope.Weights(Tastes("hated"), Tastes("disliked"))));
		}
		Dictionary<string, double> avgLoved = Horoscope.Average(people.Select(p => p.Loved).ToList());
		Dictionary<string, double> avgHated = Horoscope.Average(people.Select(p => p.Hated).ToList());
		return people.Select(p =>
		{
			(string good, string bad) = Horoscope.Picks(p.Loved, p.Hated, avgLoved, avgHated);
			return Horoscope.For(p.Npc.Name, p.Sign, ctx.Social.Moods.Get(p.Npc.Name)?.Level ?? 0, good, bad, id, Today);
		}).ToList();
	});

	/// <summary>谁说的：八卦的原话、居民群、信使和当面说过的话。</summary>
	public static List<Quote> Quotes(ShellContext ctx) => Safe(ctx, "说过的话", () =>
	{
		List<Quote> raw = new List<Quote>();
		foreach (GossipEntry e in ctx.Social.Gossip.Feed().Where(e => !string.IsNullOrWhiteSpace(e.StartedBy) && !string.IsNullOrWhiteSpace(e.Rumor)))
		{
			raw.Add(new Quote(ShellText.Clean(e.Rumor), e.StartedBy, WhoSaidIt.Gossip, e.TotalDay));
		}
		foreach (var m in ctx.Residents?.Messages.Where(m => !m.IsPlayer) ?? Enumerable.Empty<Core.Group.ResidentsMessage>())
		{
			raw.Add(new Quote(ShellText.Clean(m.Text), m.Speaker, WhoSaidIt.Chat, Dates.TotalDays(m.Day, m.Season, m.Year)));
		}
		foreach (string npc in ctx.Conversations.Tracked().Where(ctx.Npcs.IsEnabled))
		{
			foreach (ConversationEntry e in ctx.Conversations.Recent(npc, 6))
			{
				string line = string.IsNullOrWhiteSpace(e.Comeback) ? e.Opener : e.Comeback;
				raw.Add(new Quote(ShellText.Clean(line), npc, WhoSaidIt.Talk, Dates.TotalDays(e.Day, e.Season, e.Year)));
			}
			foreach (var t in ctx.Messenger?.History(npc).TakeLast(4) ?? Enumerable.Empty<Core.Messenger.TextEntry>())
			{
				raw.Add(new Quote(ShellText.Clean(t.NpcReply), npc, WhoSaidIt.Text, Dates.TotalDays(t.Day, t.Season, t.Year)));
			}
		}
		return WhoSaidIt.Clean(raw.Where(q => ctx.Npcs.IsEnabled(q.Speaker)), ctx.Display);
	});

	private static List<T> Safe<T>(ShellContext ctx, string what, Func<List<T>> read)
	{
		try
		{
			return read();
		}
		catch (Exception ex)
		{
			ctx.Log.Warn($"应用读{what}出错：{ex.Message}");
			return new List<T>();
		}
	}

	public static MapModel Map(ShellContext ctx)
	{
		try
		{
			return BuildMap(ctx);
		}
		catch (Exception ex)
		{
			ctx.Log.Warn("应用读村民关系出错：" + ex.Message);
			return new MapModel();
		}
	}

	private static MapModel BuildMap(ShellContext ctx)
	{
		var c = ctx.Config();
		MapPolicy policy = new MapPolicy(VillageRelations.IsMarriage,
			(a, b) => VillageMap.CanShowRomance(c.VillagerCouplesOn, RelationshipGraph.RomanceAllowed(a, b, c), VillageRelations.IsMarriedNpc(a) || VillageRelations.IsMarriedNpc(b), c.AdultDramaOn(), c.InfidelityOn()),
			c.AdultDramaOn());
		return VillageMap.Build(ctx.Social.Web.Edges.Where(e => ctx.Npcs.IsEnabled(e.First) && ctx.Npcs.IsEnabled(e.Second)), Today, policy);
	}

	public const int MaxStories = 60;

	public static List<GossipCase> Gossip(ShellContext ctx)
	{
		GossipWorld world = new GossipWorld(BuiltInPersonas.VanillaNames.ToList(), BuiltInPersonas.Relationships.Select(r => (r.A, r.B)).ToList(), GossipSpread.Isolated, GossipTrail.DefaultFastSpreaders, ctx.Display);
		string intensity = ctx.Config().DramaHeat;
		List<GossipCase> list = new List<GossipCase>();
		foreach (GossipEntry e in ctx.Social.Gossip.Feed().Take(MaxStories))
		{
			try
			{
				GossipStory story = new GossipStory(e.StartedBy ?? "", e.Involved.ToList(), e.Fact ?? "", e.KnownTo.ToList(), e.ToldBy ?? "",
					e.BornOn > 0 ? e.BornOn : e.TotalDay, e.LastSpreadOn, e.Unsourced);
				string fact = ShellText.Clean(e.Fact);
				string told = ShellText.Clean(e.Rumor);
				list.Add(new GossipCase(e, fact, SameStory(fact, told) ? "" : told, GossipTrail.Build(story, world, intensity)));
			}
			catch (Exception ex)
			{
				ctx.Log.Warn("八卦树跳过了一条读不懂的八卦：" + ex.Message);
			}
		}
		return list;
	}

	/// <summary>说法和事实一样，或者事实只是在说法前面加了"谁和谁（在哪）："。</summary>
	private static bool SameStory(string fact, string told) =>
		string.Equals(fact, told, StringComparison.OrdinalIgnoreCase) || told.Length > 0 && fact.EndsWith("：" + told, StringComparison.OrdinalIgnoreCase);

	public static List<ReviewEntry> Reviews(ShellContext ctx)
	{
		Farmer? player = Game1.player;
		List<ReviewEntry> list = new List<ReviewEntry>();
		if (player == null)
		{
			return list;
		}
		int today = Today;
		foreach (string npc in player.friendshipData.Keys.ToList())
		{
			try
			{
				if (!ctx.Npcs.IsEnabled(npc) || Game1.getCharacterFromName(npc, mustBeVillager: false) == null)
				{
					continue;
				}
				ReviewInput input = ReviewInputFor(ctx, npc, player.friendshipData[npc], today);
				list.Add(new ReviewEntry(npc, ctx.Display(npc), input, FarmerReviews.Build(input)));
			}
			catch (Exception ex)
			{
				ctx.Log.Warn($"评价跳过了 {npc}：{ex.Message}");
			}
		}
		return list.OrderByDescending(r => r.Review.Stars).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	private static ReviewInput ReviewInputFor(ShellContext ctx, string npc, Friendship f, int today)
	{
		EmotionalProfile? profile = ctx.Social.Feelings.Find(npc);
		List<NegativeMemory> hurts = profile?.Sores.Where(m => m.HealsOn <= 0 || m.HealsOn >= today).ToList() ?? new List<NegativeMemory>();
		IReadOnlyList<GiftRecord> gifts = ctx.Social.Gifts.For(npc);
		int Count(string taste) => gifts.Count(g => string.Equals(g.Liking, taste, StringComparison.OrdinalIgnoreCase));
		string? Last(params string[] tastes)
		{
			foreach (string taste in tastes)
			{
				GiftRecord? g = gifts.LastOrDefault(x => string.Equals(x.Liking, taste, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.What));
				if (g != null)
				{
					return ShellText.Clean(g.What);
				}
			}
			return null;
		}
		int talks = ctx.Conversations.Count(npc);
		string player = Player;
		List<int> warm = talks == 0 ? new List<int>() : ctx.Conversations.Recent(npc, int.MaxValue)
			.Where(e => string.IsNullOrWhiteSpace(e.Farmer) || string.Equals(e.Farmer, player, StringComparison.OrdinalIgnoreCase))
			.TakeLast(20).Where(e => e.HeartPoints > 0).Select(e => e.HeartPoints).ToList();
		return new ReviewInput
		{
			Npc = npc,
			Hearts = Math.Max(0, ctx.Relationships.EffectiveHearts(npc)),
			Bound = f.Status switch
			{
				FriendshipStatus.Married => Bound.Married,
				FriendshipStatus.Engaged => Bound.Engaged,
				FriendshipStatus.Dating => Bound.Dating,
				FriendshipStatus.Divorced => Bound.Divorced,
				_ => Bound.None
			},
			HasProfile = profile != null,
			Trust = profile?.FaithInFarmer ?? 0.5f,
			Warmth = profile?.Affection ?? 0f,
			Loved = Count("loved"),
			Liked = Count("liked"),
			Neutral = Count("neutral"),
			Disliked = Count("disliked"),
			Hated = Count("hated"),
			BirthdayGifts = gifts.Count(g => g.OnBirthday),
			BestGift = Last("loved"),
			WorstGift = Last("hated", "disliked"),
			Talks = talks,
			PositivePoints = warm,
			BrokenPromises = hurts.Count(m => string.Equals(m.Cause, "promise", StringComparison.OrdinalIgnoreCase)),
			HurtfulTalks = hurts.Count(m => string.Equals(m.Cause, "conversation", StringComparison.OrdinalIgnoreCase)),
			Memory = profile?.Recollections.Select(ShellText.Clean).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
		};
	}

	public static IReadOnlyList<PelicanPost> Posts(ShellContext ctx)
	{
		Func<string, string> display = ctx.Display;
		List<PostMemory> gossip = Safe(ctx, "八卦", () => ctx.Social.Gossip.Feed().Take(48).Select(e =>
		{
			GossipView view = GossipView.Resolve(e, display);
			return new PostMemory(e.TotalDay, e.Day, e.Season, e.Year, view.Text, Poster(view.Speaker));
		}).ToList());
		List<PostMemory> diary = Safe(ctx, "日记", () => ctx.Social.Diary.All().Take(24).Select(d => new PostMemory(Dates.TotalDays(d.Day, d.Season, d.Year), d.Day, d.Season, d.Year, ShellText.Clean(d.Text))).ToList());
		List<PostMemory> promo = Safe(ctx, "推广帖", () => ctx.Promo?.Enabled == true
			? ctx.Promo.PelicanPosts().Select(p => new PostMemory(Dates.TotalDays(p.Day, p.Season, p.Year), p.Day, p.Season, p.Year, p.Text, Poster(p.Npc), p.Image)).ToList()
			: new List<PostMemory>());
		return PelicanFeed.Build(gossip, diary, promo, PostText);
	}

	private static string Poster(string? npc) => string.IsNullOrWhiteSpace(npc) || Game1.getCharacterFromName(npc, mustBeVillager: false) == null ? "" : npc;

	private static string PostText(string key, int k) => key switch
	{
		"diary" => k switch
		{
			0 => AISV.Core.Text.Tr.Get("pelican.diary.0", "路过农场，顺手拍一张。"),
			1 => AISV.Core.Text.Tr.Get("pelican.diary.1", "农场日记更新啦——"),
			_ => AISV.Core.Text.Tr.Get("pelican.diary.2", "偷偷翻了农夫的日记本："),
		},
		"tag" => k switch
		{
			0 => AISV.Core.Text.Tr.Get("pelican.tag.0", "#鹈鹕镇日常"),
			1 => AISV.Core.Text.Tr.Get("pelican.tag.1", "#原图直出"),
			2 => AISV.Core.Text.Tr.Get("pelican.tag.2", "#镇上新鲜事"),
			3 => AISV.Core.Text.Tr.Get("pelican.tag.3", "#山谷慢生活"),
			4 => AISV.Core.Text.Tr.Get("pelican.tag.4", "#今天也很好"),
			_ => AISV.Core.Text.Tr.Get("pelican.tag.5", "#比城里有意思"),
		},
		"comment" => k switch
		{
			0 => AISV.Core.Text.Tr.Get("pelican.comment.0", "真的假的？！"),
			1 => AISV.Core.Text.Tr.Get("pelican.comment.1", "先存图。"),
			2 => AISV.Core.Text.Tr.Get("pelican.comment.2", "谁拍的，角度绝了。"),
			3 => AISV.Core.Text.Tr.Get("pelican.comment.3", "这镇子每天都有新故事。"),
			4 => AISV.Core.Text.Tr.Get("pelican.comment.4", "早就猜到了。"),
			5 => AISV.Core.Text.Tr.Get("pelican.comment.5", "光线好好看。"),
			6 => AISV.Core.Text.Tr.Get("pelican.comment.6", "细说！"),
			_ => AISV.Core.Text.Tr.Get("pelican.comment.7", "哈，果然。"),
		},
		"promo.comment" => k == 0 ? AISV.Core.Text.Tr.Get("pelican.promo.comment0", "有人玩过吗？看着不错。") : AISV.Core.Text.Tr.Get("pelican.promo.comment1", "城里人的花样真多。"),
		"promo.tag" => AISV.Core.Text.Tr.Get("pelican.promo.tag", "#祖祖城来的消息"),
		_ => ""
	};
}
