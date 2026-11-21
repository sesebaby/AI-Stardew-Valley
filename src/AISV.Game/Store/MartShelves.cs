using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Store;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace AISV.Game.Store;

/// <summary>
/// 货架：每张货架有几个格子，格子里的货存在存档的共享库存里（卸掉 mod 货也还在）；
/// 每样货记着哪天上架、从哪来、标价几成。店里的货用临时贴图画在货架上（跟着游戏的前后遮挡）。
/// </summary>
public static class MartShelves
{
	public const string GuidKey = "AIStardewValley.Core/ShelfId";

	public const string StockedKey = "AIStardewValley.Core/Stocked";

	public const string OriginKey = "AIStardewValley.Core/Origin";

	public const string PriceKey = "AIStardewValley.Core/PricePct";

	public const string StockroomId = "AIStardewValley.Core/MartStock";

	private const int DisplayId = 77_310_000;

	/// <summary>家具 ID → 货架种类。</summary>
	public static readonly IReadOnlyDictionary<string, ShelfKind> Kinds = new Dictionary<string, ShelfKind>
	{
		[MartContent.ShelfQualified] = ShelfKinds.Shelf,
		["(F)" + MartContent.SmallId] = ShelfKinds.Small,
		["(F)" + MartContent.ColdId] = ShelfKinds.Cold,
		["(F)" + MartContent.WarmId] = ShelfKinds.Warm,
		["(F)" + MartContent.BinId] = ShelfKinds.Bin,
		["(F)" + MartContent.CabinetId] = ShelfKinds.Cabinet
	};

	public static ShelfKind? Kind(Furniture f) => Kinds.TryGetValue(f.QualifiedItemId, out ShelfKind? k) ? k : null;

	public static bool IsShelf(Furniture f) => Kinds.ContainsKey(f.QualifiedItemId);

	private static int Today => Game1.Date.TotalDays;

	/// <summary>这张货架的格子（长度固定为种类的格子数，空格是 null）。</summary>
	public static Inventory Slots(Furniture f)
	{
		if (!f.modData.TryGetValue(GuidKey, out string? id))
		{
			id = Guid.NewGuid().ToString("N");
			f.modData[GuidKey] = id;
		}
		Inventory inv = Game1.player.team.GetOrCreateGlobalInventory("AIStardewValley.Core/Shelf/" + id);
		int want = Kind(f)?.Slots ?? 1;
		while (inv.Count < want)
		{
			inv.Add(null);
		}
		// 旧版陈列台是桌子：放在上面的一样货搬进第一个格子。
		if (f.heldObject.Value is SObject old)
		{
			f.heldObject.Value = null;
			Tag(old, Origins.Farm);
			int free = Enumerable.Range(0, inv.Count).FirstOrDefault(i => inv[i] == null, -1);
			if (free >= 0)
			{
				inv[free] = old;
			}
			else
			{
				Stockroom().Add(old);
			}
		}
		return inv;
	}

	public static Inventory Stockroom() => Game1.player.team.GetOrCreateGlobalInventory(StockroomId);

	public static IEnumerable<SObject> Goods(Furniture f) => Slots(f).OfType<SObject>().Where(o => o.Stack > 0);

	/// <summary>这样货能不能当商品：能出货的普通物品，或者熟食、手工品、文物、矿石。不收家具、工具、大件机器。</summary>
	public static bool Sellable(Item? item) => item is SObject o && item is not Furniture && !o.bigCraftable.Value && (o.canBeShipped() || o.Category is SObject.CookingCategory or SObject.artisanGoodsCategory || o.Type == "Arch" || o.Type == "Minerals");

	public static string Group(SObject o) => AISV.Core.Store.Goods.Of(o.Category, o.Type == "Arch");

	/// <summary>记上：哪天上架、从哪来。已经有的不改（进货的货在库房里就带着来路）。</summary>
	public static void Tag(SObject o, string origin)
	{
		if (!o.modData.ContainsKey(StockedKey))
		{
			o.modData[StockedKey] = Today.ToString();
		}
		if (!o.modData.ContainsKey(OriginKey))
		{
			o.modData[OriginKey] = origin;
		}
	}

	/// <summary>拿回背包前去掉我们的标记，好和身上的同样东西叠在一起。</summary>
	public static void Untag(Item o)
	{
		o.modData.Remove(StockedKey);
		o.modData.Remove(OriginKey);
		o.modData.Remove(PriceKey);
	}

	public static int StockedDay(SObject o) => o.modData.TryGetValue(StockedKey, out string? v) && int.TryParse(v, out int d) ? d : Today;

	public static string Origin(SObject o) => o.modData.TryGetValue(OriginKey, out string? v) ? v : Origins.Farm;

	public static int Percent(SObject o) => o.modData.TryGetValue(PriceKey, out string? v) && int.TryParse(v, out int p) ? Math.Clamp(p, 50, 200) : 100;

	public static void SetPercent(SObject o, int pct) => o.modData[PriceKey] = Math.Clamp(pct, 50, 200).ToString();

	/// <summary>放在这张货架上的这样货现在是哪个新鲜度。</summary>
	public static string Stage(Furniture f, SObject o, bool longerChilled = false)
	{
		int life = Freshness.Life(Group(o), Kind(f)?.Temp ?? Temps.Ambient, longerChilled);
		return Freshness.Stage(life, Today - StockedDay(o));
	}

	/// <summary>温度不对：该冷藏的放常温、熟食不在保温柜。</summary>
	public static bool WrongTemp(Furniture f, SObject o)
	{
		string want = AISV.Core.Store.Goods.Prefers(Group(o));
		return want != Temps.Ambient && (Kind(f)?.Temp ?? Temps.Ambient) != want;
	}

	/// <summary>
	/// 把一样货放上货架：先叠到同一天上架、同样东西的格子，再放空格；货架不收这类货返回 -1。返回放上去几件。
	/// </summary>
	public static int Add(Furniture f, SObject item, int qty, string origin)
	{
		ShelfKind? kind = Kind(f);
		if (kind == null || !Sellable(item))
		{
			return 0;
		}
		if (!kind.Allows(Group(item)))
		{
			return -1;
		}
		Inventory slots = Slots(f);
		SObject one = (SObject)item.getOne();
		Tag(one, item.modData.TryGetValue(OriginKey, out string? o) ? o : origin);
		if (item.modData.TryGetValue(StockedKey, out string? stocked))
		{
			one.modData[StockedKey] = stocked;
		}
		if (item.modData.TryGetValue(PriceKey, out string? pct))
		{
			one.modData[PriceKey] = pct;
		}
		int left = qty;
		for (int i = 0; i < slots.Count && left > 0; i++)
		{
			if (slots[i] is SObject s && s.canStackWith(one) && StockedDay(s) == StockedDay(one) && s.Stack < kind.PerSlot)
			{
				int n = Math.Min(left, kind.PerSlot - s.Stack);
				s.Stack += n;
				left -= n;
			}
		}
		for (int i = 0; i < slots.Count && left > 0; i++)
		{
			if (slots[i] == null)
			{
				SObject s = (SObject)one.getOne();
				foreach (var kv in one.modData.Pairs)
				{
					s.modData[kv.Key] = kv.Value;
				}
				s.Stack = Math.Min(left, kind.PerSlot);
				if (!s.modData.ContainsKey(PriceKey))
				{
					SetPercent(s, 100);
				}
				slots[i] = s;
				left -= s.Stack;
			}
		}
		return qty - left;
	}

	/// <summary>从格子里拿走几件（卖掉或取下），返回拿下来的那一份。</summary>
	public static SObject? Take(Furniture f, int slot, int qty)
	{
		Inventory slots = Slots(f);
		if (slot < 0 || slot >= slots.Count || slots[slot] is not SObject s || qty <= 0)
		{
			return null;
		}
		SObject taken = (SObject)s.getOne();
		foreach (var kv in s.modData.Pairs)
		{
			taken.modData[kv.Key] = kv.Value;
		}
		taken.Stack = Math.Min(qty, s.Stack);
		s.Stack -= taken.Stack;
		if (s.Stack <= 0)
		{
			slots[slot] = null;
		}
		return taken;
	}

	/// <summary>标价：原价 × 标价几成。</summary>
	public static int UnitPrice(SObject o) => Math.Max(1, MartService.BasePrice(o) * Percent(o) / 100);

	// ---------------- 画在货架上 ----------------

	/// <summary>每种货架上，每个格子摆在哪（贴图像素坐标，左上角）、画多大（像素）、摆几个。</summary>
	private static IEnumerable<(Vector2 At, int Size, int Count, int Step)> Layout(string kind, int slot) => (kind, slot) switch
	{
		("shelf", < 3) => new[] { (new Vector2(2 + slot * 16, 10), 12, 1, 0) },
		("shelf", 3) => new[] { (new Vector2(4, 22), 8, 5, 8) },
		("shelf", 4) => new[] { (new Vector2(4, 30), 8, 5, 8) },
		("shelf", 5) => new[] { (new Vector2(4, 37), 8, 5, 8) },
		("shelf_small", < 2) => new[] { (new Vector2(2 + slot * 16, 10), 12, 1, 0) },
		("shelf_small", 2) => new[] { (new Vector2(4, 22), 8, 3, 8) },
		("shelf_small", 3) => new[] { (new Vector2(4, 30), 8, 3, 8) },
		("cold_case", _) or ("warm_case", _) => new[] { (new Vector2(6 + slot % 2 * 32, kind == "warm_case" ? 20 + slot / 2 * 11 : 18 + slot / 2 * 8), kind == "warm_case" ? 10 : 8, 3, kind == "warm_case" ? 9 : 7) },
		("promo_bin", _) => new[] { (new Vector2(7, 3), 10, 3, 6), (new Vector2(10, 8), 10, 2, 6) },
		("cabinet", _) => new[] { (new Vector2(5 + slot % 2 * 20, 15 + slot / 2 * 11), 10, 2, 8) },
		_ => Array.Empty<(Vector2, int, int, int)>()
	};

	private static string _signature = "";

	/// <summary>
	/// 你在店里时每半秒看一眼：货变了（上下架、卖掉、过了一天）或者贴图被游戏清掉了（换地图、重进），就重画。
	/// 返回 true 表示重画了。
	/// </summary>
	public static bool EnsureDisplay(GameLocation shop)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (Furniture f in shop.furniture.Where(IsShelf))
		{
			sb.Append(f.TileLocation).Append(':');
			foreach (Item? i in Slots(f))
			{
				sb.Append(i is SObject o ? $"{o.QualifiedItemId}x{o.Stack}/{Stage(f, o)}," : "-,");
			}
		}
		string sig = sb.ToString();
		bool missing = sig.Contains('x') && !shop.temporarySprites.Any(t => t.id == DisplayId);
		if (sig == _signature && !missing)
		{
			return false;
		}
		_signature = sig;
		RefreshDisplay(shop);
		return true;
	}

	/// <summary>店里所有货架重新画一遍货（上下架、卖掉、进店时调用）。</summary>
	public static void RefreshDisplay(GameLocation shop)
	{
		shop.temporarySprites.AnimatedSprites.RemoveAll(t => t.id == DisplayId);
		foreach (Furniture f in shop.furniture.Where(IsShelf))
		{
			ShelfKind kind = Kind(f)!;
			Rectangle box = f.GetBoundingBox();
			Vector2 origin = new Vector2(box.X, box.Bottom - f.sourceRect.Height * 4);
			float depth = (box.Bottom - 8) / 10000f;
			Inventory slots = Slots(f);
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] is not SObject o || o.Stack <= 0)
				{
					continue;
				}
				ParsedItemData data = ItemRegistry.GetDataOrErrorItem(o.QualifiedItemId);
				Texture2D tex = data.GetTexture();
				Rectangle src = data.GetSourceRect(0, o.ParentSheetIndex);
				string stage = Stage(f, o);
				Color tint = stage == Freshness.Expired ? new Color(150, 140, 120) : stage == Freshness.Near ? new Color(225, 215, 190) : Color.White;
				int per = Math.Max(1, (o.Stack + kind.PerSlot / 5) / Math.Max(1, kind.PerSlot / 5));
				foreach ((Vector2 at, int size, int count, int step) in Layout(kind.Id, i))
				{
					int n = Math.Min(count, per);
					for (int k = 0; k < n; k++)
					{
						shop.temporarySprites.Add(new TemporaryAnimatedSprite
						{
							texture = tex,
							sourceRect = src,
							sourceRectStartingPos = new Vector2(src.X, src.Y),
							position = origin + (at + new Vector2(k * step, 0)) * 4,
							scale = size * 4f / src.Width,
							interval = 999_999f,
							animationLength = 1,
							totalNumberOfLoops = 999_999,
							layerDepth = depth + 0.00001f * (i * 4 + k + 1),
							color = tint,
							id = DisplayId
						});
					}
				}
			}
		}
	}

	// ---------------- 贴图 ----------------

	/// <summary>拼货架贴图：从原版室内图块里裁皮埃尔的白货架、Joja 的冷柜和促销桶、酒馆的木柜，把上面画好的货抹掉。</summary>
	public static Texture2D Compose(IModHelper helper)
	{
		Texture2D src = helper.GameContent.Load<Texture2D>("Maps/townInterior");
		Color[] all = new Color[src.Width * src.Height];
		src.GetData(all);
		Color[] Crop(int tx, int ty, int tw, int th)
		{
			Color[] px = new Color[tw * 16 * th * 16];
			for (int y = 0; y < th * 16; y++)
			{
				Array.Copy(all, (ty * 16 + y) * src.Width + tx * 16, px, y * tw * 16, tw * 16);
			}
			return px;
		}
		const int W = 256, H = 112;
		Color[] sheet = new Color[W * H];
		void Paste(Color[] px, int w, int h, int tx, int ty)
		{
			for (int y = 0; y < h; y++)
			{
				Array.Copy(px, y * w, sheet, (ty * 16 + y) * W + tx * 16, w);
			}
		}
		// 皮埃尔的白货架（6 格宽）：抹掉货，顶面铺成浅色，再截成 3 格、2 格两种宽度。
		Color[] shelf = Crop(26, 29, 6, 3);
		HashSet<Color> lilac = new HashSet<Color> { Hex(0x67529e), Hex(0x8975b9), Hex(0x9792c9), Hex(0xbcbee6), Hex(0xe0e1ff), Hex(0xf1f2ff), Hex(0x49387a), Hex(0x7c3928) };
		Empty(shelf, 96, 48, lilac, 3, clearAbove: 9);
		for (int y = 10; y <= 22; y++)
		{
			for (int x = 3; x < 93; x++)
			{
				shelf[y * 96 + x] = shelf[y * 96 + 93];
			}
		}
		Paste(Narrow(shelf, 96, 48, 24, 24), 48, 48, 0, 0);
		Paste(Narrow(shelf, 96, 48, 16, 16), 32, 48, 3, 0);
		// Joja 的冷柜（6 格宽，三扇门）截成两扇门；保温柜是同一个柜子换成暖色。
		Color[] cold = Narrow(Crop(14, 61, 6, 3), 96, 48, 32, 32);
		Paste(cold, 64, 48, 5, 0);
		Color[] warm = (Color[])cold.Clone();
		for (int i = 0; i < warm.Length; i++)
		{
			Color c = warm[i];
			if (c.A > 0 && c.B > c.R)
			{
				warm[i] = new Color(c.B, (int)(c.G * 0.85f), (int)(c.R * 0.8f), c.A);
			}
		}
		Paste(warm, 64, 48, 9, 0);
		// Joja 的空促销桶。
		Paste(Crop(8, 64, 2, 2), 32, 32, 13, 0);
		// 酒馆的木柜（8 格宽 4 格高）：抹掉酒瓶，截成 3 格宽。
		Color[] cab = Crop(13, 26, 8, 4);
		HashSet<Color> wood = new HashSet<Color>();
		for (int y = 0; y < 64; y++)
		{
			for (int x = 0; x < 6; x++)
			{
				if (cab[y * 128 + x].A > 0)
				{
					wood.Add(cab[y * 128 + x]);
				}
			}
		}
		Empty(cab, 128, 64, wood, 4, clearAbove: 0);
		Paste(Narrow(cab, 128, 64, 24, 24), 48, 64, 0, 3);
		Texture2D tex = new Texture2D(Game1.graphics.GraphicsDevice, W, H);
		tex.SetData(sheet);
		return tex;
	}

	private static Color Hex(int rgb) => new Color((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);

	/// <summary>每一行：在架子本身的颜色里取出现最多的那个铺满（左右边框留着），画在上面的货就没了。</summary>
	private static void Empty(Color[] px, int w, int h, HashSet<Color> palette, int edge, int clearAbove)
	{
		for (int y = 0; y < h; y++)
		{
			if (y < clearAbove)
			{
				for (int x = 0; x < w; x++)
				{
					px[y * w + x] = Color.Transparent;
				}
				continue;
			}
			Dictionary<Color, int> count = new Dictionary<Color, int>();
			for (int x = edge; x < w - edge; x++)
			{
				Color c = px[y * w + x];
				if (c.A == 0 || palette.Contains(c))
				{
					count[c] = count.TryGetValue(c, out int n) ? n + 1 : 1;
				}
			}
			if (count.Count == 0)
			{
				continue;
			}
			Color fill = count.OrderByDescending(p => p.Value).First().Key;
			for (int x = edge; x < w - edge; x++)
			{
				px[y * w + x] = fill;
			}
		}
	}

	/// <summary>留左边几列、右边几列，中间截掉（货架变窄）。</summary>
	private static Color[] Narrow(Color[] px, int w, int h, int left, int right)
	{
		int nw = left + right;
		Color[] result = new Color[nw * h];
		for (int y = 0; y < h; y++)
		{
			Array.Copy(px, y * w, result, y * nw, left);
			Array.Copy(px, y * w + w - right, result, y * nw + left, right);
		}
		return result;
	}
}
