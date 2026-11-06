using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.World;

/// <summary>"在哪、几点、旁边有谁"块和天气说法。</summary>
public static class LocationFacts
{
	private static readonly Dictionary<string, string> Places = new[]
	{
		"Town|镇子正中，喷泉和长椅都在这儿。", "BusStop|农场和镇子之间的路口，停着一辆旧巴士。", "Farm|农夫自己打理的那片地。", "FarmHouse|农夫住的屋子。",
		"Beach|镇南的沙滩，尽头有条栈桥。", "Mountain|镇北的山路，湖、矿洞和木匠铺都挨着。", "Forest|镇子西南的大林子，牧场和法师塔藏在里面。", "Mine|山上矿道的入口。",
		"Railroad|山后头的铁轨和小站。", "Woods|林子深处被大木头挡住的一块空地。", "Desert|坐巴士才到得了的沙漠。", "Sewer|镇子底下的暗渠，影子人在那儿摆摊。",
		"Saloon|古斯掌勺的酒馆，晚上全镇都爱往这儿凑。", "SeedShop|皮埃尔卖种子杂货的铺子，他一家住楼上。", "Hospital|哈维看病的地方。", "HarveyRoom|诊所二楼，哈维的住处。",
		"Blacksmith|克林特打铁的铺子，工具和晶球都归这儿。", "AnimalShop|玛妮家，买鸡买牛、买干草都来这儿。", "ArchaeologyHouse|冈瑟照看的藏书和展品都在这栋楼里。", "JojaMart|莫里斯当经理的连锁大超市。",
		"CommunityCenter|镇上那栋老活动中心。", "ScienceHouse|山脚的木匠铺，罗宾和德米特里厄斯一家住这儿。", "WizardHouse|林子里那座高塔，拉斯莫迪斯的家。", "Club|沙漠里只有熟人才进得去的赌场。",
		"Trailer|潘妮和帕姆的家，一辆拖车。", "HaleyHouse|柳巷那栋彩色的房子，姐妹俩的家。", "SamHouse|柳巷上乔迪、肯特和两个儿子的家。", "JoshHouse|河边路上亚历克斯和爷爷奶奶的家。",
		"ManorHouse|刘易斯的宅子。", "ElliottHouse|沙滩边的小木屋，艾利欧特在这儿写书。", "LeahHouse|林边的小屋，莉亚在这儿住也在这儿创作。",
		"BathHouse_Entry|温泉的门厅和更衣间。", "BathHouse_Pool|冒着热气的温泉池。",
		"BlueMoonVineyard|蓝月葡萄园，SVE 加的地方。", "ShearwaterBridge|剪水桥，SVE 加的地方。", "AuroraVineyard|极光葡萄园，SVE 加的地方。", "CastleVillageOutpost|城堡村前哨，SVE 加的地方。",
		"GrampletonFields|格兰普顿原野，SVE 加的地方。", "Highlands|高地，SVE 加的地方。", "JunimoWoods|祝尼魔森林，SVE 加的地方。"
	}.Select(row => row.Split('|')).ToDictionary(c => c[0], c => c[1], StringComparer.OrdinalIgnoreCase);

	public static string Describe(string location) => Places.TryGetValue(location, out string? text) ? text : "在" + location + "。";

	/// <summary>早上 9 点前清早、12 点前上午、14 点前中午、18 点前下午、21 点前傍晚、之后深夜。</summary>
	public static string TimeOfDay(int time) => time switch
	{
		< 900 => "清早",
		< 1200 => "上午",
		< 1400 => "中午",
		< 1800 => "下午",
		< 2100 => "傍晚",
		_ => "深夜"
	};

	/// <param name="others">同一地点的其他 AI 村民（游戏层取，不含本人）；null = 取不到，不写这一行。</param>
	public static string Build(string location, int time, IReadOnlyList<string>? others)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine();
		sb.Append("【地点】").Append(Describe(location)).Append($" 时间 {time / 100:D2}:{time % 100:D2}（{TimeOfDay(time)}）。").AppendLine();
		if (others != null)
		{
			sb.AppendLine(others.Count > 0 ? "旁边还有：" + string.Join("、", others) + "。" : "这里只有你们两个。");
		}
		return sb.ToString();
	}

	/// <summary>天气：暴风雨 &gt; 下雨 &gt; 下雪 &gt; 晴；在屋里时说"在屋里，外面……"。</summary>
	public static string Weather(bool lightning, bool raining, bool snowing, bool indoors)
	{
		string w = lightning ? "暴风雨" : raining ? "下雨" : snowing ? "下雪" : "晴";
		return indoors ? "在屋里，外面" + w : w;
	}
}
