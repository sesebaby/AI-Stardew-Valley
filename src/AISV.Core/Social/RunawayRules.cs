using System;

namespace AISV.Core.Social;

/// <summary>孩子离家出走的规则：什么时候会跑、藏在哪儿好、提示往哪个方向、找得快慢加多少好感。</summary>
public static class RunawayRules
{
	/// <summary>上午什么时候检查（只检查一次）。</summary>
	public const int CheckFrom = 900;

	public const int CheckUntil = 1200;

	/// <summary>到这个时间还没找到，配偶自己把孩子找回来。</summary>
	public const int GiveUpAt = 1800;

	/// <summary>同一个孩子两次出走至少隔几天。</summary>
	public const int RestDays = 14;

	/// <summary>藏身点离家门至少几格。</summary>
	public const int MinTilesFromHome = 15;

	/// <summary>多近能听到笑声、多近笑声更明显。</summary>
	public const int HearTiles = 10;

	public const int CloseTiles = 4;

	/// <summary>每过多少分钟配偶给一次方向提示。</summary>
	public const int HintEveryMinutes = 120;

	/// <summary>藏了这么久还没被找到就换个地方。</summary>
	public const int MoveAfterMinutes = 240;

	public readonly record struct Day(bool Raining, bool Festival, bool PlayerHome, bool HasWalkingChild, int DaysSinceLast, int Time);

	public static bool MayRun(Day d, bool enabled, int percent, double roll)
	{
		return enabled && !d.Raining && !d.Festival && d.PlayerHome && d.HasWalkingChild && d.DaysSinceLast >= RestDays
			&& d.Time >= CheckFrom && d.Time < CheckUntil && roll * 100 < Math.Clamp(percent, 0, 100);
	}

	/// <summary>藏身点打分：离家够远、挨着能挡住的东西（树、灌木、大石头、机器）越多越好；太远的扣一点，免得要跑遍整个农场。</summary>
	public static double HideScore(double tilesFromHome, int coverAround)
	{
		if (tilesFromHome < MinTilesFromHome || coverAround <= 0)
		{
			return 0;
		}
		return coverAround * 10 - Math.Max(0, tilesFromHome - 45) * 0.5;
	}

	/// <summary>藏身点相对家门的大方向（地图上 y 往下是南）。</summary>
	public static string Direction(double dx, double dy)
	{
		double angle = Math.Atan2(-dy, dx) * 180 / Math.PI;
		angle = (angle + 360) % 360;
		string[] names = { "东", "东北", "北", "西北", "西", "西南", "南", "东南" };
		return names[(int)Math.Round(angle / 45) % 8];
	}

	public static int Minutes(int time) => time / 100 * 60 + time % 100;

	/// <summary>找到时配偶加多少好感：两小时内 60，四小时内 40，再晚 20。</summary>
	public static int FoundPoints(int startTime, int now)
	{
		int took = Minutes(now) - Minutes(startTime);
		return took <= 120 ? 60 : took <= 240 ? 40 : 20;
	}
}
