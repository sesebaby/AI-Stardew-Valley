using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Scenes;

namespace AISV.Core.Outings;

/// <summary>出游场景的站位：农舍按入口对齐，农场按区域找空地，布景挑放得下的摆。</summary>
public static class OutingLayout
{
	/// <summary>农舍的模板按 (3, 11) 是入口写的：整体挪到这个存档农舍的入口。</summary>
	public static string AnchorFarmHouse(string script, int entryX, int entryY)
	{
		if (!EventPlacement.TryRead(script, out EventPlacement.Actor farmer, out List<EventPlacement.Actor> npcs))
		{
			return script;
		}
		int dx = entryX - 3, dy = entryY - 11;
		return EventPlacement.Replace(script, farmer with { X = farmer.X + dx, Y = farmer.Y + dy }, npcs.Select(n => n with { X = n.X + dx, Y = n.Y + dy }).ToList());
	}

	/// <summary>农场区域的起点（门口按农舍门算，其余按地图比例）。</summary>
	public static (int X, int Y) Anchor(string area, int width, int height, (int X, int Y) door) => area switch
	{
		"doorstep" => (door.X + 1, door.Y + 2),
		"picnic" => ((int)(width * 0.65), (int)(height * 0.38)),
		"north_entrance" => ((int)(width * 0.5), 8),
		"east_entrance" => (width - 9, (int)(height * 0.27)),
		"south_entrance" => ((int)(width * 0.5), height - 9),
		"west_corner" => ((int)(width * 0.18), (int)(height * 0.48)),
		"pond" => ((int)(width * 0.35), (int)(height * 0.65)),
		_ => throw new ArgumentException("没有这个农场区域。", nameof(area))
	};

	/// <summary>入口区域对应的出口地图（有这个出口时从出口往里 7 格）。</summary>
	public static string ExitFor(string area) => area switch
	{
		"north_entrance" => "Backwoods",
		"east_entrance" => "BusStop",
		"south_entrance" => "Forest",
		_ => ""
	};

	public static (int X, int Y) InsideExit(string area, int warpX, int warpY) => area switch
	{
		"north_entrance" => (warpX, warpY + 7),
		"east_entrance" => (warpX - 7, warpY),
		_ => (warpX, warpY - 7)
	};

	/// <summary>
	/// 在起点 radius 格内找左中右三格都空的中心，离起点越近越好；布景每放不下一件扣 9 分。
	/// <paramref name="suitable"/> 额外的要求（比如靠水），可以为空。
	/// </summary>
	public static bool TryFindCenter((int X, int Y) anchor, int width, int height, int radius, Func<int, int, bool> clear, IReadOnlyList<OutingProp>? props, Func<int, int, bool>? suitable, out (int X, int Y) center)
	{
		center = default;
		int best = int.MaxValue;
		bool Ground(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && clear(x, y);
		for (int y = Math.Max(0, anchor.Y - radius); y <= Math.Min(height - 1, anchor.Y + radius); y++)
		{
			for (int x = Math.Max(1, anchor.X - radius); x <= Math.Min(width - 2, anchor.X + radius); x++)
			{
				int distance = (x - anchor.X) * (x - anchor.X) + (y - anchor.Y) * (y - anchor.Y);
				if (distance >= best || !Ground(x - 1, y) || !Ground(x, y) || !Ground(x + 1, y) || suitable != null && !suitable(x, y))
				{
					continue;
				}
				int missing = props == null ? 0 : props.Count - FittingProps(props, x, y, new HashSet<(int, int)> { (x - 1, y), (x + 1, y) }, Ground).Count;
				int score = distance + missing * 9;
				if (score < best)
				{
					center = (x, y);
					best = score;
				}
			}
		}
		return best != int.MaxValue;
	}

	/// <summary>农场上的站位：镜头在中心，农夫在左朝右，村民在右朝左。</summary>
	public static string PlaceOnFarm(string script, int x, int y)
	{
		List<string> commands = script.Split('/').ToList();
		string[] actors = commands.Count > 2 ? commands[2].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
		if (actors.Length != 8 || actors[0] != "farmer")
		{
			throw new InvalidOperationException("农场出游的脚本只能有农夫和一位村民。");
		}
		commands[1] = $"{x} {y}";
		commands[2] = $"farmer {x - 1} {y} 1 {actors[4]} {x + 1} {y} 3";
		commands.RemoveAll(c => c == "ignoreEventTileOffset");
		commands.Insert(3, "ignoreEventTileOffset");
		return string.Join("/", commands);
	}

	/// <summary>以 (x, y) 为原点挑出放得下的布景：地毯只要地面能用，其余还不能压到人或别的家具。</summary>
	public static IReadOnlyList<OutingProp> FittingProps(IReadOnlyList<OutingProp> props, int x, int y, IReadOnlySet<(int X, int Y)> actors, Func<int, int, bool> usable)
	{
		HashSet<(int, int)> taken = new HashSet<(int, int)>();
		List<OutingProp> fit = new List<OutingProp>();
		foreach (OutingProp p in props)
		{
			(int, int)[] tiles = Enumerable.Range(0, p.Width).SelectMany(dx => Enumerable.Range(0, p.Height).Select(dy => (x + p.X + dx, y + p.Y + dy))).ToArray();
			if (tiles.Any(t => !usable(t.Item1, t.Item2) || !p.Floor && (actors.Contains(t) || taken.Contains(t))))
			{
				continue;
			}
			fit.Add(p);
			if (!p.Floor)
			{
				taken.UnionWith(tiles);
			}
		}
		return fit;
	}

	/// <summary>在脚本的 skippable 后面加上摆布景的命令（已经有就不加）。</summary>
	public static string WithScenery(string script, string id) =>
		script.Contains(OutingCatalog.SceneryCommand + " ", StringComparison.Ordinal) ? script : script.Replace("/skippable/", $"/skippable/{OutingCatalog.SceneryCommand} {id}/", StringComparison.Ordinal);
}
