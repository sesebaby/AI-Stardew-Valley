using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Awareness;

/// <summary>朝向判断。方向：0 上、1 右、2 下、3 左；坐标单位是格。</summary>
public static class Sightlines
{
	public static bool IsFacing(float fromX, float fromY, int facing, float toX, float toY, float maxDistance)
	{
		float dx = toX - fromX;
		float dy = toY - fromY;
		if (dx == 0f && dy == 0f)
		{
			return false;
		}
		(float ahead, float side) = facing switch
		{
			0 => (-dy, Math.Abs(dx)),
			1 => (dx, Math.Abs(dy)),
			2 => (dy, Math.Abs(dx)),
			3 => (-dx, Math.Abs(dy)),
			_ => (-1f, 0f)
		};
		return ahead >= 0.5f && ahead <= maxDistance && side <= 1f;
	}

	/// <summary>玩家是否贴在村民身后：越远，容许偏离的宽度越大（最多 3 格）。</summary>
	public static bool IsBehind(float npcX, float npcY, int npcFacing, float playerX, float playerY, float maxDistance = 6f)
	{
		float dx = playerX - npcX;
		float dy = playerY - npcY;
		float squared = dx * dx + dy * dy;
		if (squared < 0.16f || squared > maxDistance * maxDistance)
		{
			return false;
		}
		(float backX, float backY) = npcFacing switch
		{
			0 => (0f, 1f),
			1 => (-1f, 0f),
			2 => (0f, -1f),
			3 => (1f, 0f),
			_ => (0f, 0f)
		};
		if (backX == 0f && backY == 0f)
		{
			return false;
		}
		float behind = dx * backX + dy * backY;
		float side = Math.Abs(dx * backY - dy * backX);
		return behind >= 0.45f && side <= Math.Min(3f, 1.5f + behind * 0.25f);
	}
}

public enum FollowSignal
{
	None,
	Warning,
	Confront
}

/// <summary>跟在别人身后多久：3 秒起疑，6 秒当面说；中间断 1.5 秒以内不算断。</summary>
public sealed class FollowTracker
{
	private readonly int _warningTicks;

	private readonly int _confrontTicks;

	private readonly int _graceTicks;

	private string? _target;

	private int _ticks;

	private int _gap;

	public FollowTracker(int warningTicks = 180, int confrontTicks = 360, int graceTicks = 90)
	{
		_warningTicks = Math.Max(1, warningTicks);
		_confrontTicks = Math.Max(_warningTicks + 1, confrontTicks);
		_graceTicks = Math.Max(0, graceTicks);
	}

	public bool IsTracking => _target != null;

	public bool HasWarned { get; private set; }

	public FollowSignal Observe(string? npc, bool qualifies)
	{
		if (string.IsNullOrWhiteSpace(npc))
		{
			Reset();
			return FollowSignal.None;
		}
		if (!string.Equals(_target, npc, StringComparison.OrdinalIgnoreCase))
		{
			Reset();
			if (!qualifies)
			{
				return FollowSignal.None;
			}
			_target = npc;
		}
		if (!qualifies)
		{
			if (++_gap > _graceTicks)
			{
				Reset();
			}
			return FollowSignal.None;
		}
		_gap = 0;
		_ticks++;
		if (!HasWarned)
		{
			HasWarned = _ticks >= _warningTicks;
			return HasWarned ? FollowSignal.Warning : FollowSignal.None;
		}
		if (_ticks < _confrontTicks)
		{
			return FollowSignal.None;
		}
		Reset();
		return FollowSignal.Confront;
	}

	public void Reset()
	{
		_target = null;
		_ticks = 0;
		_gap = 0;
		HasWarned = false;
	}
}

public enum HurtCondition
{
	None,
	Hurt,
	Exhausted
}

public enum HurtResponse
{
	None,
	Comment,
	Help
}

/// <summary>玩家伤得重或累坏时，附近的人怎么反应、递什么吃的。</summary>
public static class HurtPolicy
{
	public const string DefaultItem = "(O)211";

	private const double Threshold = 0.25;

	private static readonly IReadOnlyDictionary<string, string> Items =
		("Abigail=236 Alex=212 Caroline=618 Clint=210 Demetrius=606 Dwarf=281 Elliott=727 Emily=609 Evelyn=222 George=20 Gus=728 Haley=610 Harvey=200 " +
		"Jas=233 Jodi=195 Kent=229 Krobus=404 Leah=648 Leo=88 Lewis=199 Linus=296 Marnie=608 Maru=400 Pam=303 Penny=218 Pierre=270 Qi=268 Robin=636 " +
		"Sam=90 Sandy=834 Sebastian=228 Shane=206 Vincent=223 Willy=198 Wizard=257")
		.Split(' ').Select(pair => pair.Split('=')).ToDictionary(p => p[0], p => "(O)" + p[1], StringComparer.OrdinalIgnoreCase);

	public static HurtCondition ConditionOf(int health, int maxHealth, float stamina, int maxStamina)
	{
		static bool Low(double value, int max) => max > 0 && value <= max * Threshold;
		if (health > 0 && Low(health, maxHealth))
		{
			return HurtCondition.Hurt;
		}
		return Low(stamina, maxStamina) ? HurtCondition.Exhausted : HurtCondition.None;
	}

	public static HurtResponse ResponseOf(int hearts, bool helpedToday, bool commentedToday)
	{
		if (helpedToday)
		{
			return HurtResponse.None;
		}
		if (hearts >= 4)
		{
			return HurtResponse.Help;
		}
		return hearts >= 2 && !commentedToday ? HurtResponse.Comment : HurtResponse.None;
	}

	public static string ItemFor(string? npc) => npc != null && Items.TryGetValue(npc, out string? item) ? item : DefaultItem;
}

/// <summary>一次抛竿里，每个人只掷一次要不要来陪钓；掷中了也隔一秒才重试。</summary>
public sealed class FishingInvitations
{
	private const int RetryTicks = 60;

	private readonly Dictionary<string, bool> _decisions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _retryAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private string _session = "";

	public bool CanTry(string session, string npc, int tick, int percent, Func<int> roll)
	{
		if (_session != session)
		{
			Reset();
			_session = session;
		}
		if (!_decisions.TryGetValue(npc, out bool yes))
		{
			percent = Math.Clamp(percent, 0, 100);
			yes = percent > 0 && (percent == 100 || roll() < percent);
			_decisions[npc] = yes;
		}
		if (!yes || _retryAt.GetValueOrDefault(npc) > tick)
		{
			return false;
		}
		_retryAt[npc] = tick + RetryTicks;
		return true;
	}

	public void Reset()
	{
		_session = "";
		_decisions.Clear();
		_retryAt.Clear();
	}
}

/// <summary>一片连起来的水：岸边的格子、从岸边往水里抛多远最深。</summary>
public sealed class WaterBody
{
	private static readonly (int X, int Y)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

	private readonly HashSet<(int X, int Y)> _water;

	private readonly int _width;

	private readonly int _height;

	private WaterBody(HashSet<(int X, int Y)> water, IReadOnlyList<(int X, int Y)> shore, int width, int height)
	{
		_water = water;
		Shore = shore;
		_width = width;
		_height = height;
	}

	public IReadOnlyList<(int X, int Y)> Shore { get; }

	public static WaterBody? Discover((int X, int Y) start, int width, int height, Func<int, int, bool> isWater, int maxTiles)
	{
		bool Inside((int X, int Y) t) => t.X >= 0 && t.Y >= 0 && t.X < width && t.Y < height;
		if (maxTiles <= 0 || !Inside(start) || !isWater(start.X, start.Y))
		{
			return null;
		}
		HashSet<(int X, int Y)> water = new HashSet<(int, int)> { start };
		Queue<(int X, int Y)> queue = new Queue<(int, int)>();
		queue.Enqueue(start);
		HashSet<(int X, int Y)> shore = new HashSet<(int, int)>();
		while (queue.Count > 0)
		{
			(int X, int Y) t = queue.Dequeue();
			foreach ((int dx, int dy) in Directions)
			{
				(int X, int Y) n = (t.X + dx, t.Y + dy);
				if (!Inside(n) || water.Contains(n))
				{
					continue;
				}
				if (!isWater(n.X, n.Y))
				{
					shore.Add(n);
					continue;
				}
				if (water.Count >= maxTiles)
				{
					return null;
				}
				water.Add(n);
				queue.Enqueue(n);
			}
		}
		return new WaterBody(water, shore.OrderBy(t => t.Y).ThenBy(t => t.X).ToList(), width, height);
	}

	/// <summary>从站的地方沿四个方向最远抛 <paramref name="maxDistance"/> 格，挑最深的（一样深挑远的）。</summary>
	public bool TryCast((int X, int Y) stand, int maxDistance, out (int X, int Y) cast, out int depth)
	{
		cast = default;
		depth = 0;
		int bestDistance = 0;
		bool found = false;
		foreach ((int dx, int dy) in Directions)
		{
			for (int d = 1; d <= maxDistance; d++)
			{
				(int X, int Y) t = (stand.X + dx * d, stand.Y + dy * d);
				if (!_water.Contains(t))
				{
					break;
				}
				int here = DepthAt(t);
				if (!found || here > depth || (here == depth && d > bestDistance))
				{
					cast = t;
					depth = here;
					bestDistance = d;
					found = true;
				}
			}
		}
		return found;
	}

	private int DepthAt((int X, int Y) t)
	{
		int depth = Math.Min(Math.Min(t.X + 1, _width - t.X), Math.Min(t.Y + 1, _height - t.Y));
		foreach ((int x, int y) in Shore)
		{
			depth = Math.Min(depth, Math.Abs(t.X - x) + Math.Abs(t.Y - y));
		}
		return Math.Max(1, depth);
	}
}
