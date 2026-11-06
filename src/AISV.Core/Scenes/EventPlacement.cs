using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Scenes;

/// <summary>
/// 事件脚本里人物站的格子：从 "farmer x y 朝向 名字 x y 朝向 …" 读出来，站不住的就近（6 格内一圈圈往外）换成能站的，
/// 第二段的镜头位置跟着农夫一起挪。
/// </summary>
public static class EventPlacement
{
	public readonly record struct Actor(string Name, int X, int Y, int Facing);

	private const int SearchRadius = 6;

	/// <summary>把脚本里的站位都换成能站的格子；有人找不到地方就返回 false。</summary>
	public static bool TrySafe(string script, Func<int, int, bool> usable, out string safe)
	{
		safe = script;
		if (string.IsNullOrWhiteSpace(script) || !TryRead(script, out Actor farmer, out List<Actor> npcs))
		{
			return false;
		}
		HashSet<(int, int)> used = new HashSet<(int, int)>();
		if (!TryTile(usable, farmer.X, farmer.Y, used, out (int X, int Y) f))
		{
			return false;
		}
		used.Add(f);
		List<Actor> placed = new List<Actor>();
		foreach (Actor n in npcs)
		{
			if (!TryTile(usable, n.X, n.Y, used, out (int X, int Y) t))
			{
				return false;
			}
			used.Add(t);
			placed.Add(n with { X = t.X, Y = t.Y });
		}
		safe = Replace(script, farmer with { X = f.X, Y = f.Y }, placed);
		return true;
	}

	public static bool TryTile(Func<int, int, bool> usable, int x, int y, ISet<(int, int)> used, out (int X, int Y) tile)
	{
		if (!used.Contains((x, y)) && usable(x, y))
		{
			tile = (x, y);
			return true;
		}
		for (int r = 1; r <= SearchRadius; r++)
		{
			for (int dy = -r; dy <= r; dy++)
			{
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r)
					{
						continue;
					}
					(int, int) t = (x + dx, y + dy);
					if (!used.Contains(t) && usable(t.Item1, t.Item2))
					{
						tile = t;
						return true;
					}
				}
			}
		}
		tile = default;
		return false;
	}

	public static bool TryRead(string script, out Actor farmer, out List<Actor> npcs)
	{
		farmer = default;
		npcs = new List<Actor>();
		string? line = script.Split('/').FirstOrDefault(c => c.StartsWith("farmer ", StringComparison.OrdinalIgnoreCase));
		string[] parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
		if (parts.Length < 4 || !int.TryParse(parts[1], out int fx) || !int.TryParse(parts[2], out int fy) || !int.TryParse(parts[3], out int ff))
		{
			return false;
		}
		farmer = new Actor("farmer", fx, fy, ff);
		for (int i = 4; i + 3 < parts.Length; i += 4)
		{
			if (int.TryParse(parts[i + 1], out int x) && int.TryParse(parts[i + 2], out int y) && int.TryParse(parts[i + 3], out int f))
			{
				npcs.Add(new Actor(parts[i], x, y, f));
			}
		}
		return true;
	}

	public static string Replace(string script, Actor farmer, IReadOnlyList<Actor> npcs)
	{
		string[] commands = script.Split('/');
		for (int i = 0; i < commands.Length; i++)
		{
			if (!commands[i].StartsWith("farmer ", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string[] old = commands[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string[] view = i == 2 ? commands[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
			if (view.Length == 2 && int.TryParse(view[0], out int vx) && int.TryParse(view[1], out int vy) && vx >= 0 && vy >= 0
				&& int.TryParse(old[1], out int ox) && int.TryParse(old[2], out int oy))
			{
				commands[1] = $"{vx + farmer.X - ox} {vy + farmer.Y - oy}";
			}
			commands[i] = $"farmer {farmer.X} {farmer.Y} {farmer.Facing}" + string.Concat(npcs.Select(n => $" {n.Name} {n.X} {n.Y} {n.Facing}"));
			break;
		}
		return string.Join("/", commands);
	}
}
