using System.Linq;
using Microsoft.Xna.Framework;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using StardewModdingAPI;
using StardewValley;
using xTile;
using Size = xTile.Dimensions.Size;
using xTile.Layers;
using xTile.Tiles;

namespace AISV.Game.Store;

/// <summary>
/// 小超市的室内：照皮埃尔杂货店的样子，在运行时用原版图块拼一间 18×16 的店——上面是皮埃尔店的墙（窗户、海报、告示板），
/// 木地板，左上角是皮埃尔的白柜台和收银机，门在下边正中。不随 mod 发原版图片。
/// </summary>
public static class MartInterior
{
	public const string MapName = "AIStardewValley.Core_FarmerMart";

	public const string Asset = "Maps/" + MapName;

	public const int Width = 18;

	public const int Height = 16;

	/// <summary>门在下边第几列（回农场的传送点在 (DoorX, 15)，进门站在 (DoorX, 14)）。</summary>
	public const int DoorX = 8;

	/// <summary>柜台占的格子（x 2～7，y 4～5）。</summary>
	public static readonly Rectangle Counter = new Rectangle(2, 4, 6, 2);

	/// <summary>收银员站的地方（柜台后面，收银机旁边）。</summary>
	public static readonly Point Cashier = new Point(3, 4);

	/// <summary>排队结账：第一位站在收银机前，后面往下排。</summary>
	public static readonly Point[] Queue = { new Point(3, 6), new Point(3, 7), new Point(3, 8), new Point(2, 8), new Point(2, 9) };

	/// <summary>库房箱子放的地方。</summary>
	public static readonly Point Stockroom = new Point(16, 11);

	/// <summary>迎宾站的地方（门口旁边）。</summary>
	public static readonly Point Greeter = new Point(7, 11);

	/// <summary>海报贴在墙上哪里（贴图左上角，格子坐标）。</summary>
	public static readonly Point Poster = new Point(12, 2);

	public static bool IsCounter(int x, int y) => Counter.Contains(x, y);

	public static Map Build(IModHelper helper)
	{
		Map seed = VanillaSeedShop() ?? helper.GameContent.Load<Map>("Maps/SeedShop");
		Map shed = helper.GameContent.Load<Map>("Maps/Shed2");
		Map map = new Map(MapName);
		TileSheet main = AddSheet(map, seed, "townInterior", "untitled tile sheet");
		AddSheet(map, seed, "townInterior_2", "z_townInterior_2");
		foreach (string id in new[] { "Back", "Buildings", "Front", "AlwaysFront" })
		{
			// 图块大小照原版地图（游戏按它算地图有多大、镜头怎么居中）。
			map.AddLayer(new Layer(id, map, new Size(Width, Height), seed.GetLayer("Back").TileSize));
		}
		Layer back = map.GetLayer("Back"), buildings = map.GetLayer("Buildings"), front = map.GetLayer("Front");
		void Put(Layer layer, int x, int y, int index)
		{
			layer.Tiles[x, y] = index < 0 ? null : new StaticTile(layer, main, BlendMode.Alpha, index);
		}
		// 黑底。
		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				Put(back, x, y, 0);
				Put(buildings, x, y, 0);
			}
		}
		// 墙：皮埃尔店第 13～16 行。门那几列（通往他家）换成普通墙，右边框照抄。
		Copy(seed, map, 0, 13, 13, 4, 0, 0);
		Copy(seed, map, 3, 13, 4, 4, 13, 0);
		Copy(seed, map, 16, 13, 2, 4, 16, 0);
		// 木地板（皮埃尔家客厅那种，错开拼）和左右墙。
		for (int y = 4; y <= 12; y++)
		{
			for (int x = 1; x <= 16; x++)
			{
				Put(back, x, y, ((y + x / 2) % 2 == 0 ? 606 : 638) + x % 2);
				Put(buildings, x, y, -1);
				Put(front, x, y, -1);
			}
			Put(buildings, 0, y, 64);
			Put(front, 0, y, y % 3 == 0 ? 2152 : 64);
			Put(buildings, 17, y, 0);
			Put(front, 17, y, y % 3 == 0 ? 2153 : 68);
		}
		// 底边，门在 DoorX。
		for (int x = 0; x < Width; x++)
		{
			Put(front, x, 12, x % 4 == 0 ? 2120 : 165);
		}
		Put(front, 0, 12, 160);
		Put(front, 1, 12, 161);
		Put(front, DoorX - 1, 12, 162);
		Put(front, DoorX, 12, -1);
		Put(front, DoorX + 1, 12, 163);
		Put(front, 16, 12, 166);
		Put(front, 17, 12, 167);
		for (int y = 13; y <= 14; y++)
		{
			Put(back, DoorX, y, y == 13 ? 292 : 2085);
			Put(buildings, DoorX, y, -1);
			Put(front, DoorX - 1, y, y == 13 ? 64 : 96);
			Put(front, DoorX + 1, y, y == 13 ? 68 : 130);
		}
		// 皮埃尔的柜台：上沿一行在 Front（人能站在后面），柜身一行挡路。
		int[] top = { 979, 980, 981, 982, 983, 334 };
		int[] body = { 1011, 1012, 1013, 1014, 1015, 1016 };
		for (int i = 0; i < 6; i++)
		{
			Put(front, Counter.X + i, Counter.Y, top[i]);
			Put(buildings, Counter.X + i, Counter.Y + 1, body[i]);
		}
		map.Properties["Warp"] = $"{DoorX} 15 Farm 0 0";
		// 夜里店里点灯（原版地图的 Light 属性：x y 种类，1 是灯笼那种暖光），不然天一黑整间店漆黑。
		map.Properties["Light"] = "4 6 1 9 6 1 14 6 1 4 10 1 9 10 1 14 10 1";
		foreach (string p in new[] { "AmbientLight", "AmbientNightLight" })
		{
			if (shed.Properties.TryGetValue(p, out xTile.ObjectModel.PropertyValue? v))
			{
				map.Properties[p] = v;
			}
		}
		return map;
	}

	/// <summary>原版的皮埃尔店地图：SVE 这类 mod 会整张换掉，图块和位置都对不上，所以绕过它们直接读游戏文件。</summary>
	private static Map? VanillaSeedShop()
	{
		try
		{
			using var raw = new Microsoft.Xna.Framework.Content.ContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
			return raw.Load<Map>("Maps/SeedShop");
		}
		catch
		{
			return null;
		}
	}

	private static TileSheet AddSheet(Map map, Map from, string image, string id)
	{
		TileSheet? source = from.TileSheets.FirstOrDefault(t => t.ImageSource.EndsWith(image));
		TileSheet sheet = source != null
			? new TileSheet(id, map, source.ImageSource, source.SheetSize, source.TileSize)
			: new TileSheet(id, map, "Maps/" + image, new Size(32, image == "townInterior" ? 70 : 64), new Size(16, 16));
		map.AddTileSheet(sheet);
		return sheet;
	}

	/// <summary>照抄皮埃尔店的一块（三层），图块表按图片对上，格子上的属性（开店、门）不抄。</summary>
	private static void Copy(Map from, Map to, int sx, int sy, int w, int h, int dx, int dy)
	{
		foreach (string id in new[] { "Back", "Buildings", "Front" })
		{
			Layer src = from.GetLayer(id), dst = to.GetLayer(id);
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					Tile? t = src.Tiles[sx + x, sy + y];
					if (t == null)
					{
						dst.Tiles[dx + x, dy + y] = null;
						continue;
					}
					TileSheet sheet = to.TileSheets.First(s => s.ImageSource == t.TileSheet.ImageSource);
					dst.Tiles[dx + x, dy + y] = new StaticTile(dst, sheet, BlendMode.Alpha, t.TileIndex);
				}
			}
		}
	}
}
