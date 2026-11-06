using System;

namespace AISV.Core.Awareness;

/// <summary>两格之间看不看得见：在范围内，沿直线每格四次采样，路过的格子（不算两端）都要通，斜着跨格时拐角两侧也要通。</summary>
public static class SightLine
{
	public static bool Clear(int fromX, int fromY, int toX, int toY, double range, Func<int, int, bool> open)
	{
		int dx = toX - fromX, dy = toY - fromY;
		if ((double)dx * dx + (double)dy * dy > range * range)
		{
			return false;
		}
		int steps = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 4;
		int px = fromX, py = fromY;
		for (int i = 1; i <= steps; i++)
		{
			int x = (int)Math.Floor(fromX + 0.5 + (double)dx * i / steps);
			int y = (int)Math.Floor(fromY + 0.5 + (double)dy * i / steps);
			if (x != px && y != py && (!open(px, y) || !open(x, py)))
			{
				return false;
			}
			bool endpoint = (x == fromX && y == fromY) || (x == toX && y == toY);
			if (!endpoint && !open(x, y))
			{
				return false;
			}
			px = x;
			py = y;
		}
		return true;
	}
}
