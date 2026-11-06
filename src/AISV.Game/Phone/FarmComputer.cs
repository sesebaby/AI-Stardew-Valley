using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Objects;

namespace AISV.Game.Phone;

/// <summary>罗宾店里卖的农场电脑：摆在屋里，右键开机。</summary>
public sealed class FarmComputer
{
	public const string ItemId = "AIStardewValley.Core_FarmComputer";

	public const string QualifiedItemId = "(F)" + ItemId;

	private const string TextureAsset = "Mods/AIStardewValley.Core/FarmComputer";

	private const int Price = 2500;

	private readonly string _texturePath;

	public FarmComputer(string modDirectory)
	{
		_texturePath = System.IO.Path.Combine(modDirectory, "assets", "world", "farm_computer.png");
	}

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (e.NameWithoutLocale.IsEquivalentTo("Data/Furniture"))
		{
			e.Edit(asset => asset.AsDictionary<string, string>().Data[ItemId] = $"AIStardewValley Computer/decor/2 3/2 1/1/{Price}/-1/{Tr.Get("computer.name", "农场电脑").Replace("/", " ")}/0/{TextureAsset.Replace('/', '\\')}/true");
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
		{
			e.Edit(asset =>
			{
				if (asset.AsDictionary<string, ShopData>().Data.TryGetValue("Carpenter", out ShopData? shop) && shop.Items.All(i => i.Id != ItemId))
				{
					shop.Items.Add(new ShopItemData { Id = ItemId, ItemId = QualifiedItemId, Price = Price });
				}
			});
		}
		else if (e.NameWithoutLocale.IsEquivalentTo(TextureAsset))
		{
			e.LoadFrom(() =>
			{
				using System.IO.FileStream stream = System.IO.File.OpenRead(_texturePath);
				return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			}, AssetLoadPriority.Exclusive);
		}
	}

	/// <summary>调试用：在脚边摆一台。</summary>
	public static string PlaceHere()
	{
		Furniture piece = ItemRegistry.Create<Furniture>(QualifiedItemId);
		piece.TileLocation = Game1.player.Tile + new Vector2(1f, -1f);
		piece.boundingBox.Value = new Rectangle((int)piece.TileLocation.X * 64, (int)piece.TileLocation.Y * 64, piece.boundingBox.Width, piece.boundingBox.Height);
		Game1.currentLocation.furniture.Add(piece);
		return $"摆在 {Game1.currentLocation.NameOrUniqueName} {piece.TileLocation}";
	}

	/// <summary>鼠标指的这一格是不是摆着的电脑（连上半截画面也算），人要站得够近。</summary>
	public static bool IsAt(Vector2 tile)
	{
		Point at = new Point((int)tile.X * 64 + 32, (int)tile.Y * 64 + 32);
		foreach (Furniture piece in Game1.currentLocation?.furniture ?? Enumerable.Empty<Furniture>())
		{
			if (piece.ItemId != ItemId)
			{
				continue;
			}
			Rectangle box = piece.GetBoundingBox();
			Rectangle whole = new Rectangle(box.X, box.Y - 128, box.Width, box.Height + 128);
			if (whole.Contains(at) && Vector2.Distance(Game1.player.Tile, new Vector2(box.Center.X / 64, box.Center.Y / 64)) <= 3f)
			{
				return true;
			}
		}
		return false;
	}
}
