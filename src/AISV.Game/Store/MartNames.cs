using AISV.Core.Store;
using AISV.Core.Text;

namespace AISV.Game.Store;

/// <summary>小超市里各种东西给玩家看的名字（货的大类、新鲜度、温度、店型、顾客类型）。</summary>
public static class MartNames
{
	public static string Group(string g) => g switch
	{
		Goods.Vegetable => Tr.Get("mart.group.vegetable", "蔬菜"),
		Goods.Fruit => Tr.Get("mart.group.fruit", "水果"),
		Goods.Flower => Tr.Get("mart.group.flower", "花"),
		Goods.Fish => Tr.Get("mart.group.fish", "鱼"),
		Goods.Animal => Tr.Get("mart.group.animal", "蛋奶"),
		Goods.Artisan => Tr.Get("mart.group.artisan", "手工品"),
		Goods.Food => Tr.Get("mart.group.food", "熟食"),
		Goods.Forage => Tr.Get("mart.group.forage", "野货"),
		Goods.Mineral => Tr.Get("mart.group.mineral", "矿石"),
		Goods.Artifact => Tr.Get("mart.group.artifact", "文物"),
		_ => Tr.Get("mart.group.general", "杂货")
	};

	public static string Stage(string s) => s switch
	{
		Freshness.SameDay => Tr.Get("mart.fresh.sameDay", "当日上架"),
		Freshness.Fresh => Tr.Get("mart.fresh.fresh", "新鲜"),
		Freshness.Near => Tr.Get("mart.fresh.near", "临期"),
		Freshness.Expired => Tr.Get("mart.fresh.expired", "过期"),
		_ => Tr.Get("mart.fresh.normal", "正常")
	};

	public static string Temp(string t) => t switch
	{
		Temps.Chilled => Tr.Get("mart.temp.chilled", "冷藏"),
		Temps.Warm => Tr.Get("mart.temp.warm", "保温"),
		_ => Tr.Get("mart.temp.ambient", "常温")
	};

	public static string Origin(string o) => o switch
	{
		Origins.Supplier => Tr.Get("mart.origin.supplier", "进的货"),
		Origins.Spouse => Tr.Get("mart.origin.spouse", "配偶做的"),
		_ => Tr.Get("mart.origin.farm", "自家产")
	};

	public static string ShopType(string id) => id switch
	{
		"greengrocer" => Tr.Get("mart.type.greengrocer", "蔬果店"),
		"fishmonger" => Tr.Get("mart.type.fishmonger", "鱼铺"),
		"florist" => Tr.Get("mart.type.florist", "花店"),
		"deli" => Tr.Get("mart.type.deli", "熟食店"),
		"workshop" => Tr.Get("mart.type.workshop", "工坊店"),
		"curio" => Tr.Get("mart.type.curio", "珍宝店"),
		_ => Tr.Get("mart.type.general", "杂货店")
	};

	/// <summary>店型说明：主打什么、5 级的本事。</summary>
	public static string ShopTypeNote(string id) => id switch
	{
		"greengrocer" => Tr.Get("mart.typeNote.greengrocer", "主打蔬菜、水果、野货，买菜的人来得多。5 级：自家种的顾客多给一成价。"),
		"fishmonger" => Tr.Get("mart.typeNote.fishmonger", "主打鱼，买鱼的人来得多。5 级：冷柜里的鱼多放一半天数。"),
		"florist" => Tr.Get("mart.typeNote.florist", "主打花，送礼的人来得多。5 级：周末客人多两成。"),
		"deli" => Tr.Get("mart.typeNote.deli", "主打熟食，饭点饿了的人来得多。5 级：午饭、晚饭时客人多两成。"),
		"workshop" => Tr.Get("mart.typeNote.workshop", "主打手工品（酒、果酱、奶酪……），识货的人来得多。5 级：金星以上多给一成价。"),
		"curio" => Tr.Get("mart.typeNote.curio", "主打矿石和文物，收藏的人来得多。5 级：主打的货多给一成价。"),
		_ => Tr.Get("mart.typeNote.general", "什么都卖，不偏哪类，随便逛逛的人多。")
	};

	public static string Archetype(string id) => id switch
	{
		Archetypes.Cook => Tr.Get("mart.arch.cook", "买菜做饭的"),
		Archetypes.FruitLover => Tr.Get("mart.arch.fruit", "买水果的"),
		Archetypes.FishBuyer => Tr.Get("mart.arch.fish", "买鱼的"),
		Archetypes.Gifter => Tr.Get("mart.arch.gift", "挑礼物的"),
		Archetypes.Hungry => Tr.Get("mart.arch.hungry", "饿了的"),
		Archetypes.Connoisseur => Tr.Get("mart.arch.connoisseur", "识货的"),
		Archetypes.Collector => Tr.Get("mart.arch.collector", "收藏的"),
		_ => Tr.Get("mart.arch.browser", "随便逛逛的")
	};
}
