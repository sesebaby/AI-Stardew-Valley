using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using AISV.Core.World.Market;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.World;

/// <summary>
/// 市场：蔬菜和水果（含 mod 作物）每天一个价格系数，只在出货箱结算时生效；加工品跟着原料走。
/// 每天早上算价后马上写盘（早上的结果属于刚存下的这一天），出货记录跟着当晚的存档写。
/// </summary>
public sealed class MarketService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly Ecosystem _eco;

	private readonly Func<string, bool> _npcEnabled;

	private readonly ILog _log;

	public MarketService(Func<ModConfig> config, Func<DocumentStore?> store, Ecosystem eco, Func<string, bool> npcEnabled, ILog log)
	{
		_config = config;
		_store = store;
		_eco = eco;
		_npcEnabled = npcEnabled;
		_log = log;
		MarketPatches.Factor = SettlementFactor;
		MarketPatches.Shipped = RecordShipment;
	}

	/// <summary>世界模块和市场都开着才改价；关掉后出货立刻回到原价。</summary>
	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.WorldModuleOn && c.MarketOn;
		}
	}

	/// <summary>当前存档的行情；没进存档时为空。</summary>
	public MarketState? State
	{
		get
		{
			DocumentStore? store = _store();
			if (store == null)
			{
				return null;
			}
			MarketState s = store.Get(MarketState.Path, MarketState.Doc);
			MarketRules.Tidy(s);
			return s;
		}
	}

	public void OnSaveLoaded() => MarketPatches.SettlementActive = false;

	public void OnReturnedToTitle() => MarketPatches.SettlementActive = false;

	/// <summary>入夜：从现在到第二天开始，出货结算和出货清单按行情价。</summary>
	public void OnDayEnding() => MarketPatches.SettlementActive = Enabled && Context.IsMainPlayer && _store() != null;

	public void OnDayStarted()
	{
		MarketPatches.SettlementActive = false;
		MarketState? s = State;
		if (s == null || !Enabled || s.LastPricedDay == Game1.Date.TotalDays)
		{
			return;
		}
		try
		{
			Price(s, Game1.Date.TotalDays);
			_store()?.Commit(new[] { MarketState.Path });
		}
		catch (Exception ex)
		{
			_log.Error("算今天的行情出错：" + ex);
		}
	}

	/// <summary>可用的作物和果树里，收获物是蔬菜或水果、原价不低于 5g 的（按收获物去重）。</summary>
	public IReadOnlyList<CropInfo> MarketCrops() =>
		_eco.EnabledCrops().Where(c => IsProduce(c.HarvestCategory) && c.HarvestPrice >= MarketRules.MinBasePrice).GroupBy(c => c.HarvestItemId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

	private void Price(MarketState s, int day)
	{
		List<MarketCrop> crops = MarketCrops().Select(c => new MarketCrop(c.HarvestItemId, c.HarvestDisplayName, c.HarvestPrice, c.Seasons)).ToList();
		Dictionary<string, int> npc = MarketRules.PriceDay(s, crops, Game1.currentSeason ?? "spring", day, Game1.uniqueIDForThisGame, n => Game1.getCharacterFromName(n) != null && _npcEnabled(n));
		Dirty();
		_log.Trace($"第 {day} 天的行情：{crops.Count} 种作物，热门 {string.Join("、", s.HotItems)}，村民卖了 {npc.Values.Sum()} 件。");
	}

	// ---------------- 结算 ----------------

	/// <summary>这件东西的行情系数（作物本身，或加工品的原料）；不在市场里为空。</summary>
	public float? FactorFor(Item? item) => MarketKeyFor(item, out _) is string key && State?.Items.TryGetValue(key, out MarketEntry? e) == true ? e!.Factor : null;

	/// <summary>市场里对应的作物；<paramref name="artisan"/> 表示这是加工品。</summary>
	public string? MarketKeyFor(Item? item, out bool artisan)
	{
		artisan = false;
		if (item is not StardewValley.Object obj || obj.bigCraftable.Value)
		{
			return null;
		}
		string? id;
		if (IsProduce(obj.Category))
		{
			id = obj.QualifiedItemId;
		}
		else if (obj.Category == StardewValley.Object.artisanGoodsCategory && !string.IsNullOrWhiteSpace(obj.preservedParentSheetIndex.Value))
		{
			id = Ecosystem.IngredientId(obj);
			artisan = true;
		}
		else
		{
			return null;
		}
		CropInfo? crop = _eco.CropByProduce(id);
		return crop != null && _eco.IsEnabled(crop) && IsProduce(crop.HarvestCategory) ? crop.HarvestItemId : null;
	}

	private float? SettlementFactor(StardewValley.Object obj) => Enabled ? FactorFor(obj) : null;

	/// <summary>出货清单打开时（每晚一次）：记下卖了哪些作物，按行情比原价多拿了多少。</summary>
	private void RecordShipment(IList<Item> items, Func<Item, int> vanillaPrice, Func<Item, int> marketPrice)
	{
		MarketState? s = State;
		if (s == null || !Enabled)
		{
			return;
		}
		IEnumerable<ShippedItem> shipped = items.Where(item => item != null).Select(item => (Item: item, Key: MarketKeyFor(item, out bool artisan), Artisan: artisan))
			.Where(x => x.Key != null).Select(x => new ShippedItem(x.Key!, x.Artisan, x.Item.Stack, vanillaPrice(x.Item), marketPrice(x.Item)));
		if (MarketRules.RecordShipment(s, Game1.Date.TotalDays, shipped))
		{
			Dirty();
			_log.Trace($"记下出货：{s.PendingPlayerSales.Values.Sum()} 件作物，按行情差 {s.LastNightDifference}g。");
		}
	}

	// ---------------- 看板 ----------------

	public IReadOnlyList<MarketRow> BoardRows(int count = 8) => MarketRules.Board(AllRows(), count);

	public IReadOnlyList<MarketRow> AllRows()
	{
		MarketState? s = State;
		if (s == null || !Enabled)
		{
			return Array.Empty<MarketRow>();
		}
		string season = Game1.currentSeason ?? "spring";
		HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Item? item in Game1.player?.Items ?? (IList<Item?>)Array.Empty<Item?>())
		{
			if (MarketKeyFor(item, out _) is string key)
			{
				held.Add(key);
			}
		}
		List<MarketRow> rows = new List<MarketRow>();
		foreach (CropInfo crop in MarketCrops())
		{
			if (s.Items.TryGetValue(crop.HarvestItemId, out MarketEntry? e))
			{
				rows.Add(MarketRules.Row(new MarketCrop(crop.HarvestItemId, crop.HarvestDisplayName, crop.HarvestPrice, crop.Seasons), e, season, s.HotItems, held.Contains(crop.HarvestItemId)));
			}
		}
		return rows;
	}

	/// <summary>昨天镇上谁卖了什么（"刘易斯 防风草×12"）。</summary>
	/// <summary>出货结算是在新的一天记的账，所以早上看的就是今天这一笔。</summary>
	public int LastNightGold(MarketState s) => MarketRules.LastNightGold(s, Game1.Date.TotalDays);

	public IReadOnlyList<string> NpcSalesLines() =>
		(State?.LastNpcSales ?? new List<MarketSale>()).Select(x => $"{Game1.getCharacterFromName(x.Seller)?.displayName ?? x.Seller} {ItemName(x.ItemId)}×{x.Count}").ToList();

	/// <summary>本周热门的显示名。</summary>
	public IReadOnlyList<string> HotNames() => (State?.HotItems ?? new List<string>()).Select(ItemName).ToList();

	public static string ItemName(string id)
	{
		try
		{
			return ItemRegistry.GetData(id)?.DisplayName ?? id;
		}
		catch
		{
			return id;
		}
	}

	// ---------------- 调试 ----------------

	/// <summary>不睡觉连算几天，每天都当作你卖了 <paramref name="sales"/>；算完明早照常按真实日期再算。</summary>
	public void DebugAdvance(int days, IReadOnlyDictionary<string, int>? sales)
	{
		MarketState s = State ?? throw new InvalidOperationException("先进存档。");
		for (int i = 0; i < Math.Max(1, days); i++)
		{
			foreach ((string id, int n) in sales ?? new Dictionary<string, int>())
			{
				s.PendingPlayerSales[id] = s.PendingPlayerSales.GetValueOrDefault(id) + n;
			}
			Price(s, Math.Max(0, s.LastPricedDay) + 1);
		}
		s.LastPricedDay = Game1.Date.TotalDays;
	}

	public bool DebugSetFactor(string itemId, float factor)
	{
		if (State?.Items.TryGetValue(itemId, out MarketEntry? e) != true)
		{
			return false;
		}
		e!.Factor = Math.Clamp(factor, MarketRules.MinFactor, MarketRules.MaxFactor);
		Dirty();
		return true;
	}

	/// <summary>"(O)24"，或加工品 "pickle:(O)24"、"wine:…"、"jelly:…"、"juice:…"、"dried:…"。</summary>
	public static Item CreateItem(string spec, int count, int quality)
	{
		int colon = spec.IndexOf(':');
		if (colon <= 0)
		{
			return ItemRegistry.Create(spec, count, quality);
		}
		StardewValley.Object ingredient = (StardewValley.Object)ItemRegistry.Create(spec[(colon + 1)..]);
		StardewValley.ItemTypeDefinitions.ObjectDataDefinition def = ItemRegistry.GetObjectTypeDefinition();
		StardewValley.Object made = spec[..colon].ToLowerInvariant() switch
		{
			"wine" => def.CreateFlavoredWine(ingredient),
			"juice" => def.CreateFlavoredJuice(ingredient),
			"jelly" => def.CreateFlavoredJelly(ingredient),
			"pickle" => def.CreateFlavoredPickle(ingredient),
			"dried" => def.CreateFlavoredDriedFruit(ingredient),
			string kind => throw new ArgumentException("认不出的加工品：" + kind)
		};
		made.Stack = count;
		made.Quality = quality;
		return made;
	}

	/// <summary>aisv_market 命令；结果写到 debug/ 下。</summary>
	public string Command(string[] args, Func<string, object, string> write)
	{
		if (State is not MarketState s)
		{
			return "先进存档。";
		}
		string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		try
		{
			switch (mode)
			{
				case "price":
				{
					Item item = CreateItem(args[1], 1, args.Length > 2 ? int.Parse(args[2]) : 0);
					return write("market-price.json", new
					{
						id = item.QualifiedItemId,
						name = item.DisplayName,
						key = MarketKeyFor(item, out bool artisan),
						artisan,
						factor = FactorFor(item),
						shop = item.sellToStorePrice(-1L),
						vanilla = MarketPatches.VanillaPrice(item),
						settlement = MarketPatches.MarketPrice(item),
						settlementActive = MarketPatches.InSettlement
					});
				}
				case "bin":
				{
					int count = int.Parse(args[2]);
					Item item = CreateItem(args[1], count, args.Length > 3 ? int.Parse(args[3]) : 0);
					Game1.getFarm().getShippingBin(Game1.player).Add(item);
					return write("market-bin.json", new { id = item.QualifiedItemId, name = item.DisplayName, count, vanillaEach = MarketPatches.VanillaPrice(item), marketEach = MarketPatches.MarketPrice(item), moneyBefore = Game1.player.Money });
				}
				case "advance":
				{
					int days = int.Parse(args[1]);
					Dictionary<string, int>? sales = null;
					if (args.Length > 2)
					{
						sales = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
						foreach (string part in args[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
						{
							string[] kv = part.Split(':');
							sales[Ecosystem.Qualify(kv[0]) ?? kv[0]] = int.Parse(kv[1]);
						}
					}
					List<object> trace = new List<object>();
					for (int i = 0; i < days; i++)
					{
						DebugAdvance(1, sales);
						trace.Add(sales?.Keys.Select(k => new { id = k, factor = s.Items.TryGetValue(k, out MarketEntry? e) ? e.Factor : 0f, volume = e?.Volume ?? 0f }).ToList() ?? (object)"（没卖东西）");
					}
					return write("market-advance.json", new { days, trace, rows = AllRows() });
				}
				case "set":
				{
					string id = Ecosystem.Qualify(args[1]) ?? args[1];
					return DebugSetFactor(id, float.Parse(args[2], CultureInfo.InvariantCulture)) ? write("market-set.json", new { id, rows = AllRows() }) : $"{id} 不在市场里。";
				}
				default:
					return write("market-dump.json", new
					{
						enabled = Enabled,
						day = Game1.Date.TotalDays,
						s.LastPricedDay,
						crops = MarketCrops().Count,
						hot = s.HotItems,
						npcSales = NpcSalesLines(),
						lastNightDifference = s.LastNightDifference,
						lastNightDay = s.LastNightDay,
						pending = s.PendingPlayerSales,
						board = BoardRows(),
						all = AllRows()
					});
			}
		}
		catch (Exception ex)
		{
			return "aisv_market 出错：" + ex.Message + "\n用法：aisv_market dump | price <id> [品质] | bin <id> <件数> [品质] | advance <天数> [id:件数,...] | set <id> <系数>";
		}
	}

	private static bool IsProduce(int category) => category == StardewValley.Object.VegetableCategory || category == StardewValley.Object.FruitsCategory;

	private void Dirty() => _store()?.MarkDirty(MarketState.Path);
}
