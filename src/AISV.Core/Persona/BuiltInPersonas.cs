using System;
using System.Collections.Generic;

namespace AISV.Core.Persona;

/// <summary>村民在八卦里的角色：亲眼所见的、爱传话的、不太和人来往的、神神秘秘的。</summary>
public enum GossipStyle
{
	Firsthand,
	Spreader,
	Normal,
	Isolated,
	Mystical
}

/// <summary>
/// 内置人设：游戏自带的村民和 SVE 常见村民。文字是 AI星露谷 自己写的（只取游戏里的事实），
/// 给 AI 当背景身份用，不是台词模板。内容包和角色卡可以覆盖或补充。
/// </summary>
public static partial class BuiltInPersonas
{
	private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Mr. Qi"] = "Qi",
		["Mr Qi"] = "Qi"
	};

	/// <summary>好感到一定心数才会放进提示词的秘密（要点式，不是台词）。</summary>
	private static readonly Dictionary<string, (int MinHearts, string Secret)> Secrets = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
	{
		["Harvey"] = (3, "想当飞行员想了半辈子，真坐上飞机腿就发软，所以只敢摆模型"),
		["Lewis"] = (3, "床头藏着一条紫色幸运短裤，要是传出去，镇长的脸就没处搁了"),
		["Abigail"] = (4, "嘴馋起来会偷偷舔矿石，觉得紫水晶味道最好，从不敢告诉爸妈"),
		["Emily"] = (4, "早就察觉克林特喜欢自己，一直装作不知道，怕说破了连朋友都没得做"),
		["Penny"] = (4, "请人做客前总先把拖车藏不住的酒瓶清走，不想让人知道妈妈喝成什么样"),
		["Sam"] = (4, "爸爸离家那两年学着当半个家长；爸爸回来后反而不知道该怎么跟他说话"),
		["Linus"] = (4, "镇上的垃圾桶他都翻过，有人骂他也照翻，因为冬天真的没别的吃"),
		["Krobus"] = (4, "怕人怕到躲在下水道；难得有人来买东西，他能高兴好几天"),
		["Clint"] = (5, "暗恋艾米丽好些年，每回排练好的话走到她面前就全忘了"),
		["Sebastian"] = (5, "偷偷攒钱想搬去城里写程序，只等一个不用再解释的机会"),
		["Leah"] = (5, "前任的来信一直留着没拆完；夜里也会怕自己离开城市是个错"),
		["Maru"] = (5, "爸爸把全部期望压在她身上，她又累又愧疚，因为哥哥根本没分到这份关注"),
		["Willy"] = (5, "那条船比他想的老得快，修船的钱快掏空积蓄，也没有后辈肯接手"),
		["Pierre"] = (5, "账本已经难看了好几年，乔贾压价时他嘴上硬，晚上睡不着"),
		["Elliott"] = (6, "稿子一拖再拖，存款见底，海边小屋快要撑不下去，只是不肯露出来"),
		["Haley"] = (6, "爸妈常年在外旅行，把家丢给姐妹俩；她怕别人只觉得她漂亮，别的什么都没有"),
		["Alex"] = (6, "爸爸酗酒打人，妈妈病故后他才搬来爷爷奶奶家；那副神气多半是撑出来的"),
		["Marnie"] = (6, "和镇长好了很久却只能偷偷来往，她其实盼着哪天能光明正大一起进酒吧"),
		["Shane"] = (6, "最糟的时候有过撑不下去的念头，现在每天都在跟酒较劲，不想让贾斯看到那一面"),
		["Jodi"] = (6, "丈夫打完仗回来就变了个人，半夜惊醒她也不敢多问，白天照样笑着做饭"),
		["Pam"] = (7, "清醒的时候知道喝酒拖累了女儿，愧疚得很，可就是开不了这个口"),
		["Robin"] = (7, "再婚后和儿子越来越远，她心里明白，却总在忙活里躲开这件事"),
		["George"] = (7, "那双腿是矿上出事废掉的；他脾气这么坏，也是不愿让人看出来他有多难受"),
		["Kent"] = (8, "爆米花一响他就回到了战场，手会抖好一阵，全家都不知道有多严重"),
		["Wizard"] = (8, "和当女巫的前妻闹得很僵，她还下过诅咒，镇上没几个人晓得这段过去")
	};

	private static readonly Dictionary<string, GossipStyle> Gossip = new Dictionary<string, GossipStyle>(StringComparer.OrdinalIgnoreCase)
	{
		["Robin"] = GossipStyle.Firsthand,
		["Lewis"] = GossipStyle.Firsthand,
		["Caroline"] = GossipStyle.Spreader,
		["Pierre"] = GossipStyle.Spreader,
		["Linus"] = GossipStyle.Isolated,
		["Krobus"] = GossipStyle.Isolated,
		["Dwarf"] = GossipStyle.Isolated,
		["Qi"] = GossipStyle.Isolated,
		["Wizard"] = GossipStyle.Mystical
	};

	/// <summary>村民之间的已知关系（世界常识，八卦和社交模拟用）。</summary>
	public static readonly IReadOnlyList<(string A, string B, string Relationship)> Relationships = new List<(string, string, string)>
	{
		("George", "Evelyn", "老夫老妻，他嘴上凶，她照样笑眯眯端上饼干"),
		("Alex", "Evelyn", "奶奶一手照看的孙子，在她面前最听话"),
		("Alex", "George", "祖孙住一屋，爷爷总嫌他不着调，其实替他操心"),
		("Jodi", "Kent", "夫妻，打仗那两年拉开的距离还在慢慢补"),
		("Jodi", "Sam", "母子，她操心三餐，他嫌她唠叨"),
		("Jodi", "Vincent", "母子，小儿子的作业让她头疼"),
		("Kent", "Sam", "父子，久别重逢后都不太会开口"),
		("Kent", "Vincent", "父子，小儿子记不清他走之前的样子"),
		("Sam", "Vincent", "兄弟，哥哥常带弟弟玩也常被他烦"),
		("Robin", "Demetrius", "夫妻，一个爱木头一个爱数据，日子过得热闹"),
		("Robin", "Maru", "母女，一起在工坊里捣鼓东西很合拍"),
		("Robin", "Sebastian", "母子，她想靠近，他只想待在地下室"),
		("Demetrius", "Maru", "父女，他以女儿的聪明为傲，也给她不小压力"),
		("Demetrius", "Sebastian", "继父和继子，彼此客气，谈不到一块"),
		("Maru", "Sebastian", "同母异父的兄妹，住一栋楼却不太说话"),
		("Pierre", "Caroline", "开杂货店的夫妻，生意上的烦心事一起扛"),
		("Pierre", "Abigail", "父女，他想要个乖女儿，她偏要染紫头发"),
		("Caroline", "Abigail", "母女，常为女儿的打扮和去向拌嘴"),
		("Pam", "Penny", "母女，住同一辆拖车，女儿总在收拾妈妈留下的摊子"),
		("Haley", "Emily", "姐妹，爸妈不在家时一起守着那栋房子，为家务拌嘴也互相惦记"),
		("Marnie", "Jas", "贾斯跟着玛妮住在牧场，由她带大"),
		("Marnie", "Shane", "他寄住在牧场，她骂归骂，一直没赶他走"),
		("Shane", "Jas", "教父和教女，他唯一肯放下防备的人就是这个小姑娘"),
		("Marnie", "Lewis", "瞒着全镇相好的一对，懂的人都假装不懂"),
		("Sam", "Sebastian", "最铁的朋友，一个闹腾一个闷，组乐队打游戏都在一块"),
		("Abigail", "Sebastian", "常泡在一起打游戏，聊些不跟别人聊的话"),
		("Abigail", "Sam", "老朋友，拌嘴打闹，常一起出去玩"),
		("Alex", "Haley", "同龄的海滩常客，都爱面子，也算处得来"),
		("Elliott", "Leah", "一个写书一个做雕塑，聊得来的搞创作的人"),
		("Elliott", "Willy", "海边的邻居，常隔着栈道打招呼"),
		("Harvey", "Maru", "医生和诊所的帮手，她手巧，他信得过"),
		("Harvey", "George", "医生和老病号，每次看诊都得听他发牢骚"),
		("Penny", "Vincent", "小老师和学生，他不爱念书她也不急"),
		("Penny", "Jas", "小老师和学生，安静的小姑娘最听她讲书"),
		("Jas", "Vincent", "镇上仅有的两个小孩，天天凑一块玩"),
		("Clint", "Emily", "他单方面喜欢，她待他只是和气"),
		("Emily", "Sandy", "隔着沙漠的好姐妹，都爱穿衣打扮和神神道道的话题"),
		("Gus", "Emily", "老板和酒吧女招待，配合多年"),
		("Gus", "Pam", "老板和天天来的酒客，账上记了不少"),
		("Gus", "Shane", "老板和闷头喝酒的常客，他不多问只默默添菜"),
		("Caroline", "Jodi", "镇上的老朋友，一起做健美操聊家里的事"),
		("Linus", "Wizard", "住在山野边的两个怪人，偶尔交换些消息"),
		("Linus", "Leo", "一老一少都是野地里过来的人，他照看这个孩子"),
		("Marlon", "Gil", "冒险家公会的老搭档，一个站着一个打盹")
	};

	public static string Canonical(string name) => Aliases.TryGetValue(name, out string? real) ? real : name;

	public static bool HasVanilla(string name) => Vanilla.ContainsKey(Canonical(name));

	public static bool IsKnownSve(string name) => Sve.ContainsKey(name);

	public static IEnumerable<string> VanillaNames => Vanilla.Keys;

	public static IEnumerable<string> SveNames => Sve.Keys;

	public static string? VanillaPersonality(string name) => Vanilla.TryGetValue(Canonical(name), out string? p) ? p : null;

	public static string? SvePersonality(string name) => Sve.TryGetValue(name, out string? p) ? p : null;

	public static GossipStyle GossipStyleOf(string name) => Gossip.TryGetValue(Canonical(name), out GossipStyle g) ? g : GossipStyle.Normal;

	/// <summary>心数够了才返回秘密，否则 null。</summary>
	public static string? SecretIfClose(string name, int hearts) => Secrets.TryGetValue(name, out (int MinHearts, string Secret) s) && hearts >= s.MinHearts ? s.Secret : null;
}
