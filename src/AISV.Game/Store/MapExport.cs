using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using xTile;
using xTile.Layers;
using xTile.Tiles;

namespace AISV.Game.Store;

/// <summary>调试：把原版地图的图块和贴图导出来，方便挑小超市室内要用的图块。</summary>
public static class MapExport
{
	public static string Run(IModHelper helper, string dir, string[] maps)
	{
		Directory.CreateDirectory(dir);
		StringBuilder log = new StringBuilder();
		foreach (string name in maps)
		{
			Map map = helper.GameContent.Load<Map>("Maps/" + name);
			StringBuilder sb = new StringBuilder();
			foreach (TileSheet ts in map.TileSheets)
			{
				sb.AppendLine($"#sheet {ts.Id} {ts.ImageSource} {ts.SheetWidth}x{ts.SheetHeight}");
				string asset = ts.ImageSource.Replace(".png", "");
				string file = Path.Combine(dir, Path.GetFileName(asset) + ".png");
				if (!File.Exists(file))
				{
					try
					{
						Texture2D tex = helper.GameContent.Load<Texture2D>(asset.StartsWith("Maps") ? asset : "Maps/" + asset);
						using FileStream fs = File.Create(file);
						tex.SaveAsPng(fs, tex.Width, tex.Height);
					}
					catch (Exception ex)
					{
						log.AppendLine($"{asset}: {ex.Message}");
					}
				}
			}
			foreach (var p in map.Properties)
			{
				sb.AppendLine($"#prop {p.Key}={p.Value}");
			}
			foreach (Layer layer in map.Layers)
			{
				sb.AppendLine($"#layer {layer.Id} {layer.LayerWidth}x{layer.LayerHeight}");
				for (int y = 0; y < layer.LayerHeight; y++)
				{
					sb.AppendLine(string.Join(",", Enumerable.Range(0, layer.LayerWidth).Select(x =>
					{
						Tile? t = layer.Tiles[x, y];
						if (t == null)
						{
							return ".";
						}
						string s = $"{map.TileSheets.IndexOf(t.TileSheet)}:{t.TileIndex}";
						if (t is AnimatedTile)
						{
							s += "a";
						}
						if (t.Properties.Count > 0 || t.TileIndexProperties.Count > 0)
						{
							s += "{" + string.Join(";", t.Properties.Select(q => q.Key + "=" + q.Value)) + "}";
						}
						return s;
					})));
				}
			}
			File.WriteAllText(Path.Combine(dir, name + ".txt"), sb.ToString());
			log.AppendLine($"{name}: {map.Layers[0].LayerWidth}x{map.Layers[0].LayerHeight}, sheets {string.Join(" ", map.TileSheets.Select(t => t.ImageSource))}");
		}
		return log.ToString();
	}
}
