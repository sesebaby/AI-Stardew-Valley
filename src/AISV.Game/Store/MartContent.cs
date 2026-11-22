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
/// 室内照皮埃尔杂货店用原版图块拼（见 <see cref="MartInterior"/>），六种货架家具的贴图从原版皮埃尔店、Joja、酒馆的图块裁出来、抹掉画好的货
/// （见 <see cref="MartShelves.Compose"/>），Robin 店里卖这些货架，被罚款时 Lewis 的信。外观用 mod 自带的图，没有时把原版大棚屋换个暖色。
/// 不随 mod 发原版图片。
/// </summary>
public sealed class MartContent
{
	public const string BuildingId = "AIStardewValley.Core_FarmerMart";

	public const string ShelfId = "AIStardewValley.Core_Shelf";

	public const string RegisterId = "AIStardewValley.Core_Register";

	public const string SmallId = "AIStardewValley.Core_ShelfSmall";

	public const string ColdId = "AIStardewValley.Core_ColdCase";

	public const string WarmId = "AIStardewValley.Core_WarmCase";

	public const string BinId = "AIStardewValley.Core_PromoBin";

	public const string CabinetId = "AIStardewValley.Core_Cabinet";

	/// <summary>家具数据用 / 分段，所以贴图名里只能用反斜杠。</summary>
	public const string FurnitureTexture = "Mods\\AIStardewValley.Core\\MartFurniture";

	public const string ShelfQualified = "(F)" + ShelfId;

	public const string RegisterQualified = "(F)" + RegisterId;

	public const string TextureAsset = "AIStardewValley.Core/FarmerMart";

	public const string FineMail = "AIStardewValley.Core_BrawlFine";

	public const string FenceFineMail = "AIStardewValley.Core_FenceFine";

	public const int Cost = 15000;

	public const int ShelfPrice = 500;

	public const int RegisterPrice = 800;

	private readonly IModHelper _helper;

	/// <summary>店里的货架家具：ID、种类、贴图大小、占地、贴图序号（拼好的贴图每行 16 格）。</summary>
	public sealed record Fixture(string Id, AISV.Core.Store.ShelfKind Kind, string Size, string Box, int Index, Func<string> Name);

	public static readonly Fixture[] Fixtures =
	{
		new Fixture(ShelfId, AISV.Core.Store.ShelfKinds.Shelf, "3 3", "3 2", 0, () => Tr.Get("mart.kind.shelf", "白货架")),
		new Fixture(SmallId, AISV.Core.Store.ShelfKinds.Small, "2 3", "2 2", 3, () => Tr.Get("mart.kind.shelfSmall", "窄货架")),
		new Fixture(ColdId, AISV.Core.Store.ShelfKinds.Cold, "4 3", "4 2", 5, () => Tr.Get("mart.kind.cold", "玻璃冷柜")),
		new Fixture(WarmId, AISV.Core.Store.ShelfKinds.Warm, "4 3", "4 2", 9, () => Tr.Get("mart.kind.warm", "保温柜")),
		new Fixture(BinId, AISV.Core.Store.ShelfKinds.Bin, "2 2", "2 1", 13, () => Tr.Get("mart.kind.bin", "促销桶")),
		new Fixture(CabinetId, AISV.Core.Store.ShelfKinds.Cabinet, "3 4", "3 2", 48, () => Tr.Get("mart.kind.cabinet", "木柜"))
	};

	public static Fixture? FixtureOf(AISV.Core.Store.ShelfKind kind) => Fixtures.FirstOrDefault(f => f.Kind == kind);

	public MartContent(IModHelper helper)
	{
		_helper = helper;
	}

	public bool Enabled { get; set; } = true;

	/// <summary>对手的经营状况变了：Data/Shops 要重新算（ModEntry 换天时让它失效）。</summary>
	public bool ShopsDirty { get; set; }

	/// <summary>最近一次罚款多少（写进信里）。</summary>
	public int LastFine { get; set; }

	public int LastFenceFine { get; set; }

	public string LastFenceOwner { get; set; } = "";

	public string LastFenceItem { get; set; } = "";

	public int LastFenceStrike { get; set; }

	public int LastFenceClosed { get; set; }

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
					Description = Tr.Get("mart.buildingDesc", "皮埃尔杂货店那样的小店，货架、冷柜和收银台都摆好了。把收成和进来的货卖给镇上的人。"),
					Texture = TextureAsset,
					Size = shed.Size,
					SourceRect = HasArt ? Rectangle.Empty : shed.SourceRect,
					DrawShadow = shed.DrawShadow,
					CollisionMap = shed.CollisionMap,
					DrawOffset = shed.DrawOffset,
					SortTileOffset = shed.SortTileOffset,
					HumanDoor = shed.HumanDoor,
					IndoorMap = MartInterior.MapName,
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
		else if (e.NameWithoutLocale.IsEquivalentTo(FurnitureTexture))
		{
			e.LoadFrom(() => MartShelves.Compose(_helper), AssetLoadPriority.Exclusive);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo(MartInterior.Asset))
		{
			e.LoadFrom(() => MartInterior.Build(_helper), AssetLoadPriority.Exclusive);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Furniture"))
		{
			e.Edit(asset =>
			{
				IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
				foreach (Fixture f in Fixtures)
				{
					data[f.Id] = $"{f.Id}/other/{f.Size}/{f.Box}/1/{f.Kind.Price}/-1/{f.Name().Replace("/", " ")}/{f.Index}/{FurnitureTexture}/true";
				}
				data[RegisterId] = Copy(data, new[] { "Oak End Table", "Walnut End Table", "Birch End Table" }, "1 1", Tr.Get("mart.register", "收银台"), RegisterPrice);
			}, AssetEditPriority.Late);
		}
		else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
		{
			e.Edit(asset =>
			{
				if (asset.AsDictionary<string, ShopData>().Data.TryGetValue("Carpenter", out ShopData? shop))
				{
					foreach ((string id, int price) in Fixtures.Select(f => ("(F)" + f.Id, f.Kind.Price)))
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
			e.Edit(asset => asset.AsDictionary<string, string>().Data[FenceFineMail] = FenceMailText().Replace("\n", "^"), AssetEditPriority.Late);
		}
	}

	/// <summary>卖赃物被认出后镇长的信：第 1 次罚款，第 2 次加停业整顿，第 3 次被带走。</summary>
	private string FenceMailText()
	{
		(string Name, object? Value)[] args = { ("owner", LastFenceOwner), ("item", LastFenceItem), ("gold", LastFenceFine), ("days", LastFenceClosed) };
		return LastFenceStrike >= 3
			? Tr.Get("mart.fenceArrestMail", "@：^这一季第三回了。{{owner}}家的{{item}}又出现在你的货架上。^店里的赃物已经全部没收，罚款 {{gold}}g，小超市封店 {{days}} 天。^我在这个镇上当了这么多年镇长，从没这么丢脸过。好好想想你要做个什么样的人。^   -刘易斯镇长", args)
			: LastFenceStrike == 2
				? Tr.Get("mart.fenceAgainMail", "@：^又是你。{{owner}}来找我，说你店里卖的{{item}}是从他们家偷的——这一季已经是第二回了。^东西还给失主，罚款加倍：{{gold}}g，小超市停业整顿 {{days}} 天。^再有第三回，就不是罚款能了事的了。^   -刘易斯镇长", args)
				: Tr.Get("mart.fenceFineMail", "@：^{{owner}}来找我了：你店里卖的{{item}}是从{{owner}}家偷的。东西已经还给失主，按镇上的规矩，窝赃销赃罚款 {{gold}}g，已经从你的账上扣了。^再有下次，我就不只是写信了。^   -刘易斯镇长", args);
	}

	/// <summary>照一件原版桌子复制一条家具数据，改名字和价格（找不到就用第一张桌子）。</summary>
	/// <summary>
	/// 照一件原版桌子复制一条家具数据。原版很多条目不写贴图序号（第 9 段），用的是自己的数字 ID；
	/// 我们的 ID 不是数字，所以要把原来的 ID 填进贴图序号，不然会变成"错误物品"。
	/// </summary>
	private static string Copy(IDictionary<string, string> data, string[] names, string box, string display, int price)
	{
		KeyValuePair<string, string>? pick = null;
		foreach (string n in names)
		{
			pick = data.FirstOrDefault(p => p.Value.Split('/')[0] == n) is { Value: not null } hit ? hit : null;
			if (pick != null)
			{
				break;
			}
		}
		pick ??= data.FirstOrDefault(p => p.Value.Split('/') is { Length: > 3 } f && f[1] == "table" && f[3] == box && int.TryParse(p.Key, out _));
		string key = pick.Value.Key;
		List<string> f = pick.Value.Value.Split('/').ToList();
		while (f.Count < 9)
		{
			f.Add("");
		}
		f[0] = display.Replace("/", " ");
		f[5] = price.ToString();
		f[6] = string.IsNullOrEmpty(f[6]) ? "-1" : f[6];
		f[7] = display.Replace("/", " ");
		if (string.IsNullOrEmpty(f[8]))
		{
			f[8] = key;
		}
		return string.Join("/", f);
	}

	/// <summary>mod 自带的外观图（112 宽，门在正中，底边对齐地基）。</summary>
	public const string ArtFile = "assets/store/farmer_mart.png";

	private Texture2D? LoadArt() => LoadPng(ArtFile);

	private readonly Dictionary<string, Texture2D?> _brawlArt = new Dictionary<string, Texture2D?>();

	/// <summary>动手时对手的两帧（32×32 一格：挨拳后仰、坐在地上冒星星）；没有这张图返回 null，照常用村民自己的贴图。</summary>
	public Texture2D? BrawlArt(string npc)
	{
		if (!_brawlArt.TryGetValue(npc, out Texture2D? tex))
		{
			string file = $"assets/store/brawl_{npc.ToLowerInvariant()}.png";
			_brawlArt[npc] = tex = System.IO.File.Exists(System.IO.Path.Combine(_helper.DirectoryPath, file)) ? LoadPng(file) : null;
		}
		return tex;
	}

	/// <summary>直接用 MonoGame 读 png（不经过 SMAPI 的图片解码），再预乘透明度；读不了返回 null。</summary>
	private Texture2D? LoadPng(string file)
	{
		try
		{
			using System.IO.FileStream stream = System.IO.File.OpenRead(System.IO.Path.Combine(_helper.DirectoryPath, file));
			Texture2D tex = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			Color[] px = new Color[tex.Width * tex.Height];
			tex.GetData(px);
			for (int i = 0; i < px.Length; i++)
			{
				px[i] = px[i].A == 255 ? px[i] : Color.FromNonPremultiplied(px[i].R, px[i].G, px[i].B, px[i].A);
			}
			tex.SetData(px);
			return tex;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private bool HasArt => System.IO.File.Exists(System.IO.Path.Combine(_helper.DirectoryPath, ArtFile));

	/// <summary>优先用自带的外观图；没有时把原版大棚屋的图换成暖色、门上加一条红白遮阳棚。</summary>
	private Texture2D Compose()
	{
		if (HasArt && LoadArt() is Texture2D art)
		{
			return art;
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
