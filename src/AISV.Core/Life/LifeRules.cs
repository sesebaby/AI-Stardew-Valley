using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Stories;

namespace AISV.Core.Life;

/// <summary>一对村民的现况（从关系图读出来）。</summary>
public sealed record PairState(string A, string B, float Fondness, float Spark, float Strain);

/// <summary>每周推进一次：心愿往前走一步、感情往前走一步。</summary>
public static class LifeRules
{
	public const int TickDays = 7;

	/// <summary>同时最多几个心愿在推进。</summary>
	public const int MaxGoals = 6;

	/// <summary>同时最多几对在交往（含订婚、新婚）。</summary>
	public const int MaxNewCouples = 3;

	/// <summary>心愿卡了这么多周就硬往前推一步，免得永远不动。</summary>
	public const int StuckWeeks = 3;

	public static bool Due(LifeBook b, int today) => b.LastTick < 0 || today - b.LastTick >= TickDays;

	/// <summary>能开始的心愿：这位村民在镇上、没有进行中的心愿、这个心愿没做过、到年份了。</summary>
	public static List<LifeGoalDef> Startable(LifeCatalog c, LifeBook b, Func<string, bool> available, int year) =>
		c.Goals.Where(g => year >= g.FromYear && available(g.Npc) && b.Active(g.Npc) == null && b.Goals.All(x => !Same(x.Id, g.Id))).ToList();

	/// <summary>这周往前走一步的机会：心情好、你帮过、和人处得好都更容易；托你帮忙还没回音时慢一点。</summary>
	public static double StepChance(LifeGoal g, int mood, int closeFriends, bool waitingForHelp)
	{
		double p = 0.5 + Math.Min(g.Helped, 2) * 0.15 + Math.Clamp(mood, -100, 100) / 400.0 + Math.Min(closeFriends, 3) * 0.03;
		if (waitingForHelp)
		{
			p -= 0.2;
		}
		return Math.Clamp(p, 0.15, 0.95);
	}

	/// <summary>这周走不走一步（<paramref name="roll"/> 是 0～1 的随机数）。</summary>
	public static bool Steps(LifeGoal g, double chance, double roll, int today) => roll < chance || today - g.LastStepDay >= StuckWeeks * TickDays;

	/// <summary>往前走一步；走到最后一步就达成。返回是不是刚达成。</summary>
	public static bool Advance(LifeGoal g, LifeGoalDef def, int today)
	{
		if (!g.Active)
		{
			return false;
		}
		g.Stage = Math.Min(def.Steps.Count, g.Stage + 1);
		g.LastStepDay = today;
		if (g.Stage >= def.Steps.Count)
		{
			g.Outcome = "done";
			g.EndedDay = today;
			return true;
		}
		return false;
	}

	/// <summary>到了要托你帮忙的那一步（还没开口）。</summary>
	public static bool WantsHelp(LifeGoal g, LifeGoalDef def) => def.Help != null && g.Active && g.Stage == def.Help.Step && g.HelpRequest.Length == 0;

	// ---------------- 感情 ----------------

	/// <summary>两个人能不能开始交往：都单身、不是你的恋人、都同意谈恋爱（规则由外面判断）。</summary>
	public static bool CanPair(LifeBook b, string a, string bName, Func<string, string, bool> romanceAllowed, Func<string, bool> taken) =>
		!Same(a, bName) && b.CoupleOf(a) == null && b.CoupleOf(bName) == null && !taken(a) && !taken(bName) && romanceAllowed(a, bName)
		&& !b.Couples.Any(c => c.Phase == CouplePhases.BrokenUp && c.Has(a) && c.Has(bName));

	/// <summary>暗生好感的一对里挑一对开始交往（火花最大的）。</summary>
	public static PairState? NewCouple(IEnumerable<PairState> crushes, LifeBook b, Func<string, string, bool> romanceAllowed, Func<string, bool> taken)
	{
		if (b.Couples.Count(c => c.Together) >= MaxNewCouples)
		{
			return null;
		}
		return crushes.Where(p => p.Spark >= 0.3f && p.Strain < 0.4f && CanPair(b, p.A, p.B, romanceAllowed, taken)).OrderByDescending(p => p.Spark + p.Fondness).FirstOrDefault();
	}

	/// <summary>
	/// 一对这周怎么走：交往满三周且感情好 → 订婚；订婚后一周办婚礼 → 结婚；紧张太多 → 分手。返回新的阶段（不变返回 null）。
	/// </summary>
	public static string? NextPhase(Couple c, PairState now, int today, double roll)
	{
		switch (c.Phase)
		{
			case CouplePhases.Dating:
				if (now.Strain >= 0.5f && roll < 0.5)
				{
					return CouplePhases.BrokenUp;
				}
				if (today - c.Since >= 21 && now.Fondness >= 0.45f && now.Strain < 0.35f && roll < 0.35)
				{
					return CouplePhases.Engaged;
				}
				return null;
			case CouplePhases.Engaged:
				if (now.Strain >= 0.6f && roll < 0.3)
				{
					return CouplePhases.BrokenUp;
				}
				return c.WeddingDay >= 0 && today >= c.WeddingDay ? CouplePhases.Married : null;
			default:
				return null;
		}
	}

	/// <summary>在一起的这一周：感情慢慢变深。</summary>
	public static (float Fondness, float Spark) Grow(PairState p) => (Math.Min(1f, p.Fondness + 0.04f), Math.Min(1f, p.Spark + 0.02f));

	// ---------------- 约会 ----------------

	/// <summary>晚上约会的地方：两人各站一格、面对面。</summary>
	public static readonly IReadOnlyList<DatePlace> DatePlaces = new[]
	{
		new DatePlace("星之果实酒吧", "Saloon", 15, 21, 1, 16, 21, 3),
		new DatePlace("镇上的河边", "Town", 68, 58, 1, 69, 58, 3),
		new DatePlace("海滩", "Beach", 40, 22, 1, 41, 22, 3),
		new DatePlace("山里的湖边", "Mountain", 47, 25, 1, 48, 25, 3)
	};

	public const int DateStart = 1900;

	public const int DateEnd = 2130;

	/// <summary>晚上要在酒吧干活的，不约出去。</summary>
	public static bool WorksEvenings(string npc) => npc is "Gus" or "Emily";

	// ---------------- 结局戏 ----------------

	/// <summary>达成后这么多天内去那里都能看到。</summary>
	public const int EndingDays = 7;

	/// <summary>几点到几点去能碰上：酒吧是晚上，别的地方是白天。</summary>
	public static (int From, int To) EndingHours(string location) => string.Equals(location, "Saloon", StringComparison.OrdinalIgnoreCase) ? (1700, 2400) : (900, 1800);

	/// <summary>每个地方演结局戏的空地（主角站的那一格）。</summary>
	public static (int X, int Y) Stage(string location) => location switch
	{
		"Saloon" => (21, 20),
		"Beach" => (36, 21),
		"ArchaeologyHouse" => (14, 11),
		"Mountain" => (16, 28),
		"Forest" => (104, 33),
		_ => (30, 64)
	};

	/// <summary>
	/// 结局戏的站位：主角在 (x, y) 面朝大家，两边的人侧身看着主角，后排的人面朝主角，农夫站在最前面；镜头对着主角下面一格。
	/// <paramref name="speakers"/> 是按开口顺序排好的人（和 <see cref="ConclusionScript.Build"/> 一样），主角不管第几个开口都站中间。
	/// </summary>
	public static ConclusionScript.Layout EndingLayout(int x, int y, IReadOnlyList<string> speakers, string hero)
	{
		Queue<ConclusionScript.Spot> around = new Queue<ConclusionScript.Spot>(new[]
		{
			new ConclusionScript.Spot(x - 2, y + 1, 1), new ConclusionScript.Spot(x + 2, y + 1, 3), new ConclusionScript.Spot(x - 1, y + 2, 0),
			new ConclusionScript.Spot(x + 1, y + 2, 0), new ConclusionScript.Spot(x - 3, y + 2, 1), new ConclusionScript.Spot(x + 3, y + 2, 3)
		});
		List<ConclusionScript.Spot> spots = speakers.Select(s => Same(s, hero) ? new ConclusionScript.Spot(x, y, 2) : around.Count > 0 ? around.Dequeue() : new ConclusionScript.Spot(x + 4, y + 1, 3)).ToList();
		return new ConclusionScript.Layout(x, y + 1, new ConclusionScript.Spot(x, y + 3, 0), spots.Count > 0 ? spots : new[] { new ConclusionScript.Spot(x, y, 2) });
	}

	/// <summary>道具左上角在哪一格：底边正中对着主角往右 dx、往下 dy 格。</summary>
	public static (int X, int Y) PropAt(int heroX, int heroY, int dx, int dy, int width, int height)
	{
		int cols = Math.Max(1, (width + 15) / 16), rows = Math.Max(1, (height + 15) / 16);
		return (heroX + dx - (cols - 1) / 2, heroY + dy - rows + 1);
	}

	public static bool EndingDue(LifeGoal g, LifeGoalDef def, string here, int today, int time)
	{
		(int from, int to) = EndingHours(def.Ending.Location);
		return g.Outcome == "done" && !g.EndingShown && today - g.EndedDay <= EndingDays
			&& string.Equals(here, def.Ending.Location, StringComparison.OrdinalIgnoreCase) && time >= from && time < to;
	}

	/// <summary>"谁|说什么|心情" → 台词；只留到场的人说的。</summary>
	public static List<ConclusionLine> Lines(IEnumerable<string> raw, IReadOnlyCollection<string> cast) => raw
		.Select(l => l.Split('|'))
		.Where(p => p.Length >= 2 && p[1].Trim().Length > 0 && cast.Any(c => Same(c, p[0].Trim())))
		.Select(p => new ConclusionLine { Who = cast.First(c => Same(c, p[0].Trim())), Line = p[1].Trim(), Feeling = p.Length > 2 ? p[2].Trim() : "neutral" })
		.ToList();

	/// <summary>
	/// 事件里摆一张静止的道具图（每格 16 像素，按游戏的 4 倍画）：贴图里 (sx, sy) 起 width x height 的一块，左上角在 (x, y) 格，前后层次按最下面那一排算。
	/// 贴图名里的 / 换成 \，免得被当成命令分隔符。
	/// </summary>
	public static string PropCommand(string asset, int sx, int sy, int width, int height, int x, int y)
	{
		int rows = Math.Max(1, (height + 15) / 16);
		return $"/temporaryAnimatedSprite {asset.Replace('/', '\\')} {sx} {sy} {width} {height} 999999 1 0 {x} {y} false false {y + rows - 1} 0 1 0 0 0";
	}

	/// <summary>
	/// 道具写成"原版贴图@x,y,宽,高"时用原版素材的那一块（比如 TileSheets/Craftables@64,448,16,32）；不是这种写法返回 null。
	/// </summary>
	public static (string Asset, int X, int Y, int W, int H)? VanillaProp(string prop)
	{
		int at = prop.IndexOf('@');
		if (at <= 0)
		{
			return null;
		}
		int[] n = prop[(at + 1)..].Split(',').Select(p => int.TryParse(p.Trim(), out int v) ? v : -1).ToArray();
		return n.Length == 4 && n.All(v => v >= 0) && n[2] > 0 && n[3] > 0 ? (prop[..at].Trim(), n[0], n[1], n[2], n[3]) : null;
	}

	/// <summary>原版婚礼的位置（镇上，新人站的那一格）。</summary>
	public const int WeddingX = 27;

	public const int WeddingY = 63;

	/// <summary>
	/// 原版婚礼的布置（LooseSprites/Cursors 里的花拱门和四根花柱，位置照原版）：拱门上半截挡在人前面，下半截和前排花柱铺在地上。
	/// </summary>
	public static string WeddingDecor() =>
		"/temporaryAnimatedSprite LooseSprites\\Cursors 540 1196 98 48 99999 1 99999 25 59 false false 150 0 1 0 0 0"
		+ "/temporaryAnimatedSprite LooseSprites\\Cursors 540 1244 98 31 99999 1 99999 25 62 false false 0 0 1 0 0 0"
		+ "/temporaryAnimatedSprite LooseSprites\\Cursors 527 1249 12 25 99999 1 99999 24 62 false false 0 0 1 0 0 0"
		+ "/temporaryAnimatedSprite LooseSprites\\Cursors 527 1249 12 25 99999 1 99999 32 62 false false 0 0 1 0 0 0"
		+ "/temporaryAnimatedSprite LooseSprites\\Cursors 527 1249 12 25 99999 1 99999 24 69 false false 150 0 1 0 0 0"
		+ "/temporaryAnimatedSprite LooseSprites\\Cursors 527 1249 12 25 99999 1 99999 32 69 false false 150 0 1 0 0 0";

	/// <summary>
	/// 婚礼的站位，照原版：新人在 (x, y) 并排面朝大家，主婚人在他们前面一格面朝新人，宾客在后面几排，农夫站在宾客中间。
	/// <paramref name="speakers"/> 是按开口顺序排好的人（和 <see cref="ConclusionScript.Build"/> 一样），返回每人的位置。
	/// </summary>
	public static ConclusionScript.Layout WeddingLayout(int x, int y, IReadOnlyList<string> speakers, string a, string b, string officiant)
	{
		Queue<ConclusionScript.Spot> guests = new Queue<ConclusionScript.Spot>(new[]
		{
			new ConclusionScript.Spot(x - 1, y + 2, 0), new ConclusionScript.Spot(x + 2, y + 2, 0), new ConclusionScript.Spot(x - 2, y + 3, 0),
			new ConclusionScript.Spot(x + 3, y + 3, 0), new ConclusionScript.Spot(x - 1, y + 4, 0), new ConclusionScript.Spot(x + 2, y + 4, 0)
		});
		List<ConclusionScript.Spot> spots = speakers.Select(s =>
			Same(s, officiant) ? new ConclusionScript.Spot(x, y + 1, 0)
			: Same(s, a) ? new ConclusionScript.Spot(x, y, 2)
			: Same(s, b) ? new ConclusionScript.Spot(x + 1, y, 2)
			: guests.Count > 0 ? guests.Dequeue() : new ConclusionScript.Spot(x + 4, y + 2, 0)).ToList();
		return new ConclusionScript.Layout(x, y + 1, new ConclusionScript.Spot(x + 1, y + 3, 0), spots.Count > 0 ? spots : new[] { new ConclusionScript.Spot(x, y + 1, 0) });
	}

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>约会的地方：两人的位置和朝向。</summary>
public sealed record DatePlace(string Name, string Location, int AX, int AY, int AFacing, int BX, int BY, int BFacing);
