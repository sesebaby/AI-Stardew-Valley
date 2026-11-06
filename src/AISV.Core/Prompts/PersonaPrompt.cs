using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AISV.Core.Prompts;

/// <summary>
/// 人设怎么放进提示词：人设只当背景身份，不当话题清单。每位村民有几样"招牌话题"（阿比盖尔的游戏和矿井、山姆的音乐……），
/// AI 很容易每句都往上扯，所以提示词里点名提醒，记忆里只讲招牌话题、和玩家无关的条目也不放进去。
/// </summary>
public static class PersonaPrompt
{
	public const int PersonalityMaxChars = 1200;

	private sealed record Motif(string Npc, string Tag, string[] Terms);

	// 词表包含英文和中文；标签是给 AI 看的简短说明。
	private static readonly Motif[] Motifs =
	{
		new("Abigail", "游戏、矿井、长笛、神秘学", new[] { "video game", "videogame", "games", "mines", "flute", "occult", "supernatural", "purple hair", "amethyst", "adventure", "游戏", "矿井", "长笛", "神秘学", "超自然", "紫发", "紫水晶", "冒险" }),
		new("Sam", "音乐、滑板、披萨", new[] { "musician", "guitar", "skateboard", "pizza", "band", "joja", "音乐", "吉他", "滑板", "披萨", "乐队" }),
		new("Sebastian", "编程、摩托、青蛙、雨天", new[] { "programmer", "programming", "code", "basement", "motorcycle", "frog", "rainy day", "编程", "程序", "代码", "地下室", "摩托", "青蛙", "雨天" }),
		new("Haley", "阳光、时尚、摄影", new[] { "photography", "photo", "camera", "fashion", "outfit", "clothes", "makeup", "nail polish", "sunflower", "sunny", "sun", "摄影", "照片", "相机", "时尚", "衣服", "化妆", "指甲油", "向日葵", "阳光", "晒太阳" }),
		new("Emily", "水晶、缝纫、灵性", new[] { "crystal", "sewing", "meditation", "spiritual", "barmaid", "saloon", "水晶", "缝纫", "冥想", "灵性", "酒吧" }),
		new("Alex", "格子球、训练", new[] { "gridball", "athlete", "athletic", "sports", "training", "workout", "professional player", "格子球", "运动员", "运动", "训练", "健身", "职业球员" }),
		new("Harvey", "医疗、健康", new[] { "doctor", "clinic", "medical", "medicine", "patient", "health", "checkup", "appointment", "blood pressure", "symptom", "diagnos", "医生", "诊所", "医疗", "药", "病人", "健康", "体检", "预约", "血压", "症状" }),
		new("Harvey", "咖啡、爵士、眼镜", new[] { "coffee", "jazz", "glasses", "咖啡", "爵士", "眼镜" }),
		new("Maru", "科学、发明", new[] { "science", "scientist", "experiment", "gadget", "robot", "telescope", "technology", "invention", "inventor", "lab", "data", "科学", "实验", "小发明", "机器人", "望远镜", "技术", "发明", "实验室", "数据" }),
		new("Penny", "上课、帕姆、拖车", new[] { "tutor", "teaching", "library", "pam", "drinking", "trailer", "上课", "教书", "图书馆", "帕姆", "喝酒", "拖车" }),
		new("Leah", "艺术、自然、城市", new[] { "artist", "sculptor", "sculpture", "foraging", "salad", "simple life", "city", "艺术家", "雕塑", "采集", "沙拉", "简单的生活", "城里" }),
		new("Elliott", "小说、诗、海边", new[] { "novelist", "novel", "poetry", "poetic", "piano", "pomegranate", "beach cabin", "beach", "小说", "诗", "钢琴", "石榴", "海边小屋", "海边" }),
		new("Shane", "酒、鸡、Joja", new[] { "depression", "depressed", "alcoholism", "alcohol", "beer", "chicken", "joja", "jas", "抑郁", "酗酒", "酒", "啤酒", "鸡" }),
		new("Caroline", "茶园、瑜伽、秘密", new[] { "tea garden", "green tea", "yoga", "abigail's real father", "abigail real father", "茶园", "绿茶", "瑜伽", "亲生父亲" }),
		new("Pierre", "商店、Joja、生意", new[] { "shop", "store", "seeds", "sale", "joja", "business", "customer", "coupon", "商店", "店里", "种子", "打折", "生意", "顾客", "优惠券" }),
		new("Gus", "酒吧、厨艺、账目", new[] { "bartender", "chef", "saloon", "cooking", "cook", "finances", "酒保", "厨师", "酒吧", "做饭", "账" }),
		new("Lewis", "镇长、玛妮、秘密恋情", new[] { "mayor", "marnie", "secret romance", "secret relationship", "pelican town", "镇长", "玛妮", "秘密恋情", "鹈鹕镇" }),
		new("Robin", "木工、家人", new[] { "carpenter", "carpentry", "woodworking", "wood", "木匠", "木工", "木头" }),
		new("Demetrius", "科学、生态、玛鲁", new[] { "scientist", "ecosystem", "analytical", "maru", "research", "data", "科学家", "生态", "研究", "数据", "玛鲁" }),
		new("Linus", "帐篷、采集、山", new[] { "hermit", "tent", "mountain", "forager", "foraging", "nature", "隐士", "帐篷", "山上", "采集", "大自然" }),
		new("Willy", "钓鱼、大海", new[] { "fisherman", "fishing", "fish shop", "ocean", "fish", "beach", "渔夫", "钓鱼", "鱼店", "大海", "鱼", "海边" }),
		new("Clint", "打铁、艾米丽", new[] { "blacksmith", "forge", "anvil", "emily", "crush", "shy", "awkward", "铁匠", "打铁", "铁砧", "艾米丽", "暗恋", "害羞", "笨拙" }),
		new("Marnie", "牧场、刘易斯、家人", new[] { "ranch", "animal rancher", "animals", "lewis", "secret romance", "jas", "shane", "牧场", "动物", "刘易斯", "秘密恋情" }),
		new("Jodi", "做饭、家务、肯特", new[] { "mother", "cooking", "house", "kent", "war", "family", "妈妈", "做饭", "家务", "肯特", "战争", "家人" }),
		new("Kent", "战争、创伤、家人", new[] { "war", "veteran", "ptsd", "flashback", "loud sound", "soldier", "civilian life", "战争", "老兵", "创伤", "闪回", "巨响", "士兵", "平民生活" }),
		new("Vincent", "画画、出去玩", new[] { "little brother", "child", "drawing", "playing outside", "jas", "弟弟", "画画", "出去玩", "贾斯" }),
		new("Jas", "童话、野花", new[] { "orphan", "fairy tale", "wildflower", "vincent", "孤儿", "童话", "野花", "文森特" }),
		new("Leo", "鹦鹉、海岛、自然", new[] { "parrot", "ginger island", "island", "nature", "鹦鹉", "姜岛", "海岛", "大自然" }),
		new("Evelyn", "园艺、饼干", new[] { "gardening", "garden", "baking", "cookies", "george", "园艺", "花园", "烘焙", "饼干", "乔治" }),
		new("George", "轮椅、坏脾气", new[] { "wheelchair", "grumpy", "tough exterior", "soft heart", "轮椅", "脾气", "嘴硬", "心软" }),
		new("Pam", "酒、巴士、拖车", new[] { "drinking", "alcohol", "bus", "calico desert", "trailer", "penny", "喝酒", "酒", "巴士", "卡利科沙漠", "拖车", "潘妮" }),
		new("Sandy", "绿洲、沙漠、孤单", new[] { "oasis", "desert", "calico", "lonely", "emily", "绿洲", "沙漠", "孤单", "艾米丽" }),
		new("Qi", "神秘、挑战、奖励", new[] { "mystery", "mysterious", "challenge", "prize", "secretive", "神秘", "挑战", "奖励", "秘密" }),
		new("Krobus", "影子人、下水道、信任", new[] { "shadow creature", "shadow people", "sewer", "rare items", "roommate", "影子人", "下水道", "稀有物品", "室友" }),
		new("Dwarf", "矿井、买卖、地面人", new[] { "dwarf merchant", "merchant", "mines", "translation guide", "surface-dweller", "surface culture", "商人", "矿井", "翻译", "地面人", "地面" }),
		new("Wizard", "魔法、法师塔", new[] { "wizard", "magic", "magical", "arcane", "tower", "valley secrets", "法师", "魔法", "奥术", "法师塔", "山谷的秘密" })
	};

	// 一条记忆里出现这些词，说明它和玩家或关系有关，即使提到招牌话题也保留。
	private static readonly string[] RelationshipSignals =
	{
		"farmer", "player", "you", "your", "friend", "trust", "help", "support", "gift", "promise", "apolog", "argu", "relationship", "conversation", "shared", "felt", "together",
		"农夫", "玩家", "朋友", "信任", "帮忙", "帮了", "支持", "礼物", "答应", "承诺", "道歉", "吵架", "关系", "聊天", "陪"
	};

	/// <summary>人设资料块：说明这只是背景，附上这位村民的招牌话题提醒。</summary>
	public static string PersonalityBlock(string npcName, string? personality)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【你是谁】下面的资料决定你的底色。别把它念出来，也别当话题表逐条去聊。");
		string text = PromptText.Sanitize(personality, PersonalityMaxChars);
		sb.AppendLine(text.Length == 0 ? "（没有资料）" : text);
		List<string> tags = TagsFor(npcName).ToList();
		if (tags.Count > 0)
		{
			sb.AppendLine("老生常谈警告（AI 最爱拿来开口的几样）：" + string.Join("；", tags) + "。对方先提起或眼下正好相关再说。");
		}
		return PromptText.Wrap("NPC_PERSONALITY", sb.ToString(), 1600);
	}

	/// <param name="rewritten">玩家改写过这位村民的角色卡。</param>
	public static string UsageRules(string npcName, bool rewritten = false)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("怎么演：");
		if (rewritten)
		{
			sb.AppendLine("- 玩家改写过你的设定：设定里写的性子和对农夫的态度为准。后面的情感档案、性格变化、对农夫的心思是改写前留下的旧印象，和设定冲突时照设定演；发生过的事还记得，只是不再按旧印象去看。");
		}
		sb.AppendLine("- 让性格藏在说话方式里——口气、关心什么、冷热、会不会开玩笑——而不是靠报出自己的爱好和身份。");
		sb.AppendLine("- 资料块里的句子一律换成自己的话；一句台词里最多带一样个人标签（爱好、职业、口头禅、随身东西），能不带就不带。");
		List<string> tags = TagsFor(npcName).ToList();
		if (tags.Count > 0)
		{
			sb.AppendLine("- 本人的老生常谈：" + string.Join("；", tags) + "，少碰。");
		}
		return sb.ToString().Trim();
	}

	/// <summary>一行记忆/档案：清理、限长；只讲招牌话题、和玩家无关的返回空。</summary>
	public static string ProfileLine(string npcName, string? text, int maxChars = 180)
	{
		string line = PromptText.Sanitize(text, maxChars).Replace('\n', ' ').Trim();
		return Suppress(npcName, line) ? "" : line;
	}

	public static List<string> FilterMemories(string npcName, IEnumerable<string>? memories, int maxCount)
	{
		if (memories == null)
		{
			return new List<string>();
		}
		return memories.Select(m => ProfileLine(npcName, m, 220))
			.Where(m => m.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, maxCount))
			.ToList();
	}

	public static bool Suppress(string npcName, string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		if (TopicTags(npcName, text).Count == 0)
		{
			return false;
		}
		string n = Normalize(text);
		return !RelationshipSignals.Any(s => n.Contains(Normalize(s), StringComparison.Ordinal));
	}

	/// <summary>这段文字碰到了这位村民的哪些招牌话题。</summary>
	public static IReadOnlyList<string> TopicTags(string? npcName, string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(npcName))
		{
			return Array.Empty<string>();
		}
		string n = Normalize(text);
		string who = npcName.Trim();
		return Motifs.Where(m => string.Equals(m.Npc, who, StringComparison.OrdinalIgnoreCase))
			.Where(m => m.Terms.Any(t => n.Contains(Normalize(t), StringComparison.Ordinal)))
			.Select(m => m.Tag)
			.Distinct()
			.ToList();
	}

	private static IEnumerable<string> TagsFor(string npcName) => Motifs.Where(m => string.Equals(m.Npc, npcName, StringComparison.OrdinalIgnoreCase)).Select(m => m.Tag).Distinct();

	/// <summary>小写、去掉重音符号，控制字符变空格。</summary>
	private static string Normalize(string value)
	{
		string d = value.Normalize(NormalizationForm.FormD);
		StringBuilder sb = new StringBuilder(d.Length);
		foreach (char c in d)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.IsControl(c) ? ' ' : char.ToLowerInvariant(c));
			}
		}
		return sb.ToString().Normalize(NormalizationForm.FormC);
	}
}
