using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Buildings;
using StardewValley.GameData.Shops;

namespace AISV.Game.Store;

/// <summary>
/// 小超市要的游戏数据：Robin 那里能建的"农夫小超市"（照原版大棚屋，室内也是大棚屋，所以卸掉 mod 后屋里的东西还在），
/// 陈列台和收银台两件家具（照原版桌子，放上去的东西游戏自己会画出来），Robin 店里卖这两件，被罚款时 Lewis 的信。
/// 外观在运行时从原版大棚屋改：换个暖色，门上加一条红白条纹的遮阳棚。不随 mod 发原版图片。
/// </summary>
public sealed class MartContent
{
	public const string BuildingId = "AIStardewValley.Core_FarmerMart";

	public const string ShelfId = "AIStardewValley.Core_Shelf";

	public const string RegisterId = "AIStardewValley.Core_Register";

	public const string ShelfQualified = "(F)" + ShelfId;

	public const string RegisterQualified = "(F)" + RegisterId;

	public const string TextureAsset = "AIStardewValley.Core/FarmerMart";

	public const string FineMail = "AIStardewValley.Core_BrawlFine";

	public const int Cost = 15000;

	public const int ShelfPrice = 500;

	public const int RegisterPrice = 800;

	private readonly IModHelper _helper;

	public MartContent(IModHelper helper)
	{
		_helper = helper;
	}

	public bool Enabled { get; set; } = true;

	/// <summary>对手的经营状况变了：Data/Shops 要重新算（ModEntry 换天时让它失效）。</summary>
	public bool ShopsDirty { get; set; }

	/// <summary>最近一次罚款多少（写进信里）。</summary>
	public int LastFine { get; set; }

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (!Enabled)
		{
			return;
		}
		if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
		{
			e.Edit(asset =>
			{
				IDictionary<string, BuildingData> data = asset.AsDictionary<string, BuildingData>().Data;
				if (!data.TryGetValue("Big Shed", out BuildingData? shed))
				{
					return;
				}
				data[BuildingId] = new BuildingData
				{
					Name = Tr.Get("mart.building", "农夫小超市"),
					Description = Tr.Get("mart.buildingDesc", "自己的小超市。摆上陈列台和收银台，把农场里的东西卖给镇上的人。"),
					Texture = TextureAsset,
					Size = shed.Size,
					SourceRect = HasArt ? Rectangle.Empty : shed.SourceRect,
					DrawShadow = shed.DrawShadow,
					CollisionMap = shed.CollisionMap,
					DrawOffset = shed.DrawOffset,
					SortTileOffset = shed.SortTileOffset,
					HumanDoor = shed.HumanDoor,
					IndoorMap = shed.IndoorMap,
					IndoorMapType = shed.IndoorMapType,
					BuildCost = Cost,
					BuildDays = 2,
					Builder = "Robin",
					BuildMaterials = new List<BuildingMaterial>
					{
						new BuildingMaterial { ItemId = "(O)388", Amount = 300 },
						new BuildingMaterial { ItemId = "(O)390", Amount = 200 }
					},
					FadeWhenBehind = shed.FadeWhenBehind,
					AllowsFlooringUnderneath = shed.AllowsFlooringUnderneath,
					ActionTiles = shed.ActionTiles,
					TileProperties = shed.TileProperties,
					DrawLayers = shed.DrawLayers,
					Metadata = new Dictionary<string, string>(shed.Metadata)
				};
			}, AssetEditPriority.Late);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo(TextureAsset))
		{
			e.LoadFrom(Compose, AssetLoadPriority.Exclusive);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Furniture"))
		{
			e.Edit(asset =>
			{
				IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
				data[ShelfId] = Copy(data, new[] { "Oak Table", "Walnut Table", "Birch Table" }, "2 2", Tr.Get("mart.shelf", "陈列台"), ShelfPrice);
				data[RegisterId] = Copy(data, new[] { "Oak End Table", "Walnut End Table", "Birch End Table" }, "1 1", Tr.Get("mart.register", "收银台"), RegisterPrice);
			}, AssetEditPriority.Late);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
		{
			e.Edit(asset =>
			{
				if (asset.AsDictionary<string, ShopData>().Data.TryGetValue("Carpenter", out ShopData? shop))
				{
					foreach ((string id, int price) in new[] { (ShelfQualified, ShelfPrice), (RegisterQualified, RegisterPrice) })
					{
						if (shop.Items.All(i => i.Id != id))
						{
							shop.Items.Add(new ShopItemData { Id = id, ItemId = id, Price = price });
						}
					}
				}
			}, AssetEditPriority.Late);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
		{
			e.Edit(asset => asset.AsDictionary<string, string>().Data[FineMail] = Tr.Get("mart.fineMail", "@：^在店里动手打人是不对的，不管是谁先挑的事。按镇上的规矩，罚款 {{gold}}g，已经从你的账上扣了。^下不为例。^   -刘易斯镇长", ("gold", LastFine)).Replace("\n", "^"), AssetEditPriority.Late);
		}
	}

	/// <summary>照一件原版桌子复制一条家具数据，改名字和价格（找不到就用第一张桌子）。</summary>
	private static string Copy(IDictionary<string, string> data, string[] names, string box, string display, int price)
	{
		string? raw = null;
		foreach (string n in names)
		{
			raw = data.Values.FirstOrDefault(v => v.Split('/')[0] == n);
			if (raw != null)
			{
				break;
			}
		}
		raw ??= data.Values.FirstOrDefault(v => v.Split('/') is { Length: > 3 } f && f[1] == "table" && f[3] == box) ?? data.Values.First(v => v.Split('/')[1] == "table");
		string[] f = raw.Split('/');
		f[0] = display.Replace("/", " ");
		f[5] = price.ToString();
		if (f.Length > 7)
		{
			f[7] = display.Replace("/", " ");
		}
		return string.Join("/", f);
	}

	/// <summary>mod 自带的外观图（112 宽，门在正中，底边对齐地基）。</summary>
	public const string ArtFile = "assets/store/farmer_mart.png";

	private bool HasArt => System.IO.File.Exists(System.IO.Path.Combine(_helper.DirectoryPath, ArtFile));

	/// <summary>优先用自带的外观图；没有时把原版大棚屋的图换成暖色、门上加一条红白遮阳棚。</summary>
	private Texture2D Compose()
	{
		if (HasArt)
		{
			return _helper.ModContent.Load<Texture2D>(ArtFile);
		}
		Texture2D source = _helper.GameContent.Load<Texture2D>("Buildings\\Big Shed");
		Color[] px = new Color[source.Width * source.Height];
		source.GetData(px);
		for (int i = 0; i < px.Length; i++)
		{
			Color c = px[i];
			if (c.A == 0)
			{
				continue;
			}
			px[i] = new Color((byte)Math.Min(255, c.R * 1.08f + 8), (byte)Math.Min(255, c.G * 0.95f), (byte)Math.Min(255, c.B * 0.82f), c.A);
		}
		// 大棚屋的门在下方正中：遮阳棚盖在门上沿。
		int w = source.Width, h = source.Height;
		int awningY = Math.Max(0, h - 30), awningH = 6;
		int x0 = Math.Max(0, w / 2 - 22), x1 = Math.Min(w, w / 2 + 22);
		for (int y = awningY; y < awningY + awningH && y < h; y++)
		{
			for (int x = x0; x < x1; x++)
			{
				bool edge = y == awningY + awningH - 1 && (x / 3) % 2 == 0;
				px[y * w + x] = ((x - x0) / 4) % 2 == 0 ? new Color(196, 52, 46) : new Color(246, 236, 220);
				if (edge)
				{
					px[y * w + x] = new Color(150, 36, 32);
				}
			}
		}
		Texture2D result = new Texture2D(Game1.graphics.GraphicsDevice, w, h);
		result.SetData(px);
		return result;
	}
}
