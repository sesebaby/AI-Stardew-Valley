using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Companions;

public static class StepKinds
{
	/// <summary>伴侣写来的信（可以带一件礼物），读了算完成。</summary>
	public const string Letter = "letter";

	/// <summary>拜托你的事：任务日志里一条，做完找伴侣交付或说一声。</summary>
	public const string Quest = "quest";

	/// <summary>约你在某处见面，你到了演一场戏。</summary>
	public const string Event = "event";

	/// <summary>伴侣有话想跟你说，找他说话时听完。</summary>
	public const string Talk = "talk";
}

public static class StoryStates
{
	public const string Active = "active";
	public const string Done = "done";
	public const string Failed = "failed";
	public const string Abandoned = "abandoned";

	public static bool IsOpen(string s) => s == Active;
}

public static class StepStates
{
	public const string Locked = "locked";
	public const string Ready = "ready";
	public const string Done = "done";
	public const string Failed = "failed";
}

public static class StoryQuestKinds
{
	public const string Deliver = "deliver";
	public const string Fish = "fish";
	public const string Slay = "slay";
}

/// <summary>事件或对话里的一句。Type：say | farmer | narrate | emote | pause | jump | choice。</summary>
public sealed class StoryLine
{
	public string Type { get; set; } = "say";

	public string Text { get; set; } = "";

	/// <summary>neutral | happy | sad | love | angry。</summary>
	public string Mood { get; set; } = "";

	/// <summary>emote：npc | farmer。</summary>
	public string Who { get; set; } = "npc";

	public string Emote { get; set; } = "";

	public List<StoryOption> Options { get; set; } = new List<StoryOption>();
}

public sealed class StoryOption
{
	public string Text { get; set; } = "";

	public string Reply { get; set; } = "";

	public string Mood { get; set; } = "";

	/// <summary>warm | neutral | cold。</summary>
	public string Effect { get; set; } = "neutral";
}

public sealed class StoryQuest
{
	/// <summary>接到任务时打倒过多少只。</summary>
	public int Baseline { get; set; }

	public int Count { get; set; } = 1;

	public string Description { get; set; } = "";

	public string DoneLine { get; set; } = "";

	public string DoneMood { get; set; } = "happy";

	public string Kind { get; set; } = StoryQuestKinds.Deliver;

	/// <summary>最后一天（含）。</summary>
	public int LastDay { get; set; }

	public string QuestId { get; set; } = "";

	public bool ReadyHinted { get; set; }

	/// <summary>物品是带类型的 ID，怪物是 Data/Monsters 里的名字。</summary>
	public string Target { get; set; } = "";

	public string TargetName { get; set; } = "";

	public string Title { get; set; } = "";
}

public sealed class StoryStep
{
	public int Index { get; set; }

	public string Kind { get; set; } = StepKinds.Talk;

	public string State { get; set; } = StepStates.Locked;

	public string LetterText { get; set; } = "";

	public bool Gift { get; set; }

	public string GiftId { get; set; } = "";

	public string MailKey { get; set; } = "";

	/// <summary>哪天早上放进信箱。</summary>
	public int DueDay { get; set; } = -1;

	public bool Delivered { get; set; }

	public string Topic { get; set; } = "";

	public List<StoryLine> Lines { get; set; } = new List<StoryLine>();

	public string Place { get; set; } = "";

	public int Time { get; set; } = 1400;

	/// <summary>约你的消息（日子和时间发的时候补上）。</summary>
	public string Invite { get; set; } = "";

	public string MeetupId { get; set; } = "";

	public int Attempts { get; set; }

	/// <summary>每个选项点选了哪个。</summary>
	public List<int> Choices { get; set; } = new List<int>();

	public StoryQuest? Quest { get; set; }

	public int UnlockedDay { get; set; } = -1;

	public int DoneDay { get; set; } = -1;

	public string Note { get; set; } = "";
}

public sealed class StoryPackage
{
	public int ClosedDay { get; set; } = -1;

	public int Composition { get; set; }

	public int CreatedDay { get; set; }

	public string Id { get; set; } = "";

	public string Outcome { get; set; } = "";

	public string Partner { get; set; } = "";

	public string State { get; set; } = StoryStates.Active;

	public List<StoryStep> Steps { get; set; } = new List<StoryStep>();

	public string Summary { get; set; } = "";

	public string Theme { get; set; } = "";

	public string Title { get; set; } = "";

	public StoryStep? Current => Steps.FirstOrDefault(s => s.State == StepStates.Ready);
}

/// <summary>你某一天做了什么（给编剧的素材）。</summary>
public sealed class StoryActivityDay
{
	public int Day { get; set; }

	public int Fish { get; set; }

	public int Cooked { get; set; }

	public int CropsShipped { get; set; }

	public int Monsters { get; set; }

	public int Foraged { get; set; }

	public int Crafted { get; set; }

	public int Earned { get; set; }

	public List<string> Places { get; set; } = new List<string>();

	public List<string> GiftsTo { get; set; } = new List<string>();

	public bool AwayAtNight { get; set; }
}

public sealed class SpouseStoryLog
{
	public const string Path = "plans/spouse_chapters.json";

	public static readonly DocumentType<SpouseStoryLog> Doc = new DocumentType<SpouseStoryLog>("伴侣剧情", 1, () => new SpouseStoryLog());

	public int NextNumber { get; set; } = 1;

	public List<StoryPackage> Packages { get; set; } = new List<StoryPackage>();

	/// <summary>哪天起可以开始下一段（0 = 还没算过）。</summary>
	public int NextStoryDay { get; set; }

	public string LastPartner { get; set; } = "";

	public Dictionary<string, string> Themes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public List<StoryActivityDay> Activity { get; set; } = new List<StoryActivityDay>();

	public Dictionary<string, long> Snapshot { get; set; } = new Dictionary<string, long>();

	public List<string> TodayPlaces { get; set; } = new List<string>();

	public void Normalize()
	{
		Packages ??= new List<StoryPackage>();
		Dictionary<string, string> themes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> t in Themes ?? new Dictionary<string, string>())
		{
			themes.TryAdd(t.Key, t.Value);
		}
		Themes = themes;
		Activity ??= new List<StoryActivityDay>();
		Snapshot ??= new Dictionary<string, long>();
		TodayPlaces ??= new List<string>();
		foreach (StoryPackage p in Packages)
		{
			p.Steps ??= new List<StoryStep>();
			foreach (StoryStep s in p.Steps)
			{
				s.Lines ??= new List<StoryLine>();
				s.Choices ??= new List<int>();
			}
		}
	}
}

public sealed record StoryCandidate(string Id, string Name, string Note);

/// <summary>任务能挑的东西和见面能去的地方。</summary>
public sealed class StoryCandidates
{
	public List<StoryCandidate> Deliver { get; } = new List<StoryCandidate>();

	public List<StoryCandidate> Fish { get; } = new List<StoryCandidate>();

	public List<StoryCandidate> Slay { get; } = new List<StoryCandidate>();

	public List<MeetupPlace> Places { get; } = new List<MeetupPlace>();

	public IReadOnlyList<StoryCandidate> For(string kind) => kind switch
	{
		StoryQuestKinds.Fish => Fish,
		StoryQuestKinds.Slay => Slay,
		_ => Deliver
	};
}

public sealed record StoryComposition(string[] Steps, string Hint);

/// <summary>编剧资料里农夫和伴侣的情况，游戏那边填好。</summary>
public sealed class StoryContext
{
	public string FarmerName { get; init; } = "";

	public bool FarmerMale { get; init; }

	public bool Roommate { get; init; }

	public int? DaysMarried { get; init; }

	public int Hearts { get; init; }

	public IReadOnlyList<string> Children { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> OtherPartners { get; init; } = Array.Empty<string>();

	public string Today { get; init; } = "";

	public int TodayNumber { get; init; }

	public IReadOnlyList<StoryActivityDay> Activity { get; init; } = Array.Empty<StoryActivityDay>();

	public int? DaysSinceGift { get; init; }

	public string Skills { get; init; } = "";

	public string Feelings { get; init; } = "";

	public IReadOnlyList<StoryPackage> Earlier { get; init; } = Array.Empty<StoryPackage>();

	public string Theme { get; init; } = "";

	public Func<string, string> Display { get; init; } = n => n;
}

public static class SpouseStoryRules
{
	public const string Prefix = "AISV_Story_";
	public const string ChoiceCommand = "AISV_StoryChoice";
	public const string RequestName = "spouse_story";
	public const int MaxOutputTokens = 6000;
	public const double Temperature = 0.85;
	public const int EventFriendship = 30;
	public const int WarmBonus = 25;
	public const int ColdPenalty = -15;
	public const int QuestFriendship = 50;
	public const int TalkFriendship = 10;
	public const int QuestDays = 7;
	public const int TalkDays = 7;
	public const int MaxAttempts = 4;
	public const int MaxThemeLength = 60;
	public const int KeepPackages = 30;
	public const int ActivityDays = 7;
	public const int MaxPlacesPerDay = 8;
	public const int RecentDays = 3;
	public const int MinEventLines = 4;
	public const int MaxEventLines = 20;
	public const int EventEarliest = 900;
	public const int EventLatest = 1900;

	public static readonly IReadOnlyList<StoryComposition> Compositions = new[]
	{
		new StoryComposition(new[] { StepKinds.Letter, StepKinds.Quest, StepKinds.Event }, "你先写信请农夫帮一个忙（信里说清楚要什么）；农夫办好；然后你们在某处见面，这场戏围绕这件事（道谢、你拿它准备的惊喜、它对你意味着什么）。"),
		new StoryComposition(new[] { StepKinds.Event, StepKinds.Talk }, "你约农夫在某处见面，你们之间发生了点什么；过一两天你在家里又提起它，说几句（事后的念头、一句坦白、拿它开个玩笑）。"),
		new StoryComposition(new[] { StepKinds.Event, StepKinds.Talk, StepKinds.Quest, StepKinds.Talk }, "你们在某处见面；之后你跟农夫说了件事，引出一个小请求；农夫办好；办完你再说几句收尾的话。"),
		new StoryComposition(new[] { StepKinds.Event, StepKinds.Quest, StepKinds.Letter }, "你们在某处见面，戏里你请农夫帮你办件事；农夫办好；你写信道谢（常常附一件小礼物）。"),
		new StoryComposition(new[] { StepKinds.Letter, StepKinds.Event }, "你写信邀农夫去一个地方（说清楚是哪、为什么对你重要；别写日子和时间，会替你补上）；然后你们在那里见面。")
	};

	/// <summary>深不可测的、特殊地点才有的、传说中的鱼不拿来当任务。</summary>
	public static readonly IReadOnlySet<string> SpecialFish = new HashSet<string> { "156", "158", "159", "160", "161", "162", "163", "164", "165", "682", "734", "775", "795", "796", "798", "799", "800", "836", "837", "838", "898", "899", "900", "901", "902", "153", "157", "152" };

	/// <summary>怪物：Data/Monsters 的名字、下到第几层才见得到。</summary>
	public static readonly IReadOnlyList<(string Name, int Level)> Monsters = new[]
	{
		("Green Slime", 1), ("Bug", 1), ("Rock Crab", 1), ("Duggy", 6), ("Grub", 15), ("Bat", 31),
		("Dust Spirit", 41), ("Frost Jelly", 41), ("Frost Bat", 41), ("Skeleton", 71), ("Shadow Brute", 81)
	};

	/// <summary>伴侣喜好里能拿来当任务的类别（作物、采集物、料理、矿石等）。</summary>
	public static readonly IReadOnlySet<int> LikedCategories = new HashSet<int> { -75, -79, -81, -80, -7, -26, -2, -12, -4, -6, -5, -27 };

	public static readonly IReadOnlyList<string> Emotes = new[] { "heart", "happy", "sad", "angry", "surprised", "question", "music", "sleepy", "blush" };

	public static string KindName(string kind) => kind switch
	{
		StepKinds.Letter => "信",
		StepKinds.Quest => "任务",
		StepKinds.Event => "见面",
		_ => "有话说"
	};

	public static string Describe(int composition) => string.Join(" → ", Compositions[Math.Clamp(composition, 0, Compositions.Count - 1)].Steps.Select(KindName));

	/// <summary>随机挑一种结构，不和这位上一段一样。</summary>
	public static int PickComposition(int last, Random rng)
	{
		List<int> options = Enumerable.Range(0, Compositions.Count).Where(i => i != last).ToList();
		return options[rng.Next(options.Count)];
	}

	/// <summary>从上一位之后轮到谁（名字已排好序）。</summary>
	public static IEnumerable<string> RotationFrom(IReadOnlyList<string> partners, string last)
	{
		int start = partners.ToList().FindIndex(n => string.Equals(n, last, StringComparison.OrdinalIgnoreCase)) + 1;
		for (int i = 0; i < partners.Count; i++)
		{
			yield return partners[(start + i) % partners.Count];
		}
	}

	public static int DeliverCountLimit(int price) => Math.Max(1, Math.Min(10, 600 / Math.Max(10, price)));

	public static string StyleText(string? style) => (style ?? "").Trim().ToLowerInvariant() switch
	{
		"romantic" or "浪漫" => "浪漫：温柔，带点梦幻",
		"everyday" or "日常" => "日常：小小的、暖暖的、平常的时刻",
		"adventure" or "冒险" => "小冒险：好奇，有点刺激，但没有危险",
		"funny" or "搞笑" => "搞笑：互相逗、出点小岔子、一起笑",
		_ => "这次挑最合适的：浪漫、日常、小冒险或搞笑"
	};

	// ---------------- 给编剧的资料 ----------------

	public static string RelativeDay(int day, int today) => (today - day) switch
	{
		0 => "今天",
		1 => "昨天",
		int d => $"{d} 天前"
	};

	public static string ActivityLine(StoryActivityDay d, int today, Func<string, string> display)
	{
		List<string> bits = new List<string>();
		void Add(int n, string what)
		{
			if (n > 0)
			{
				bits.Add($"{what} {n}");
			}
		}
		Add(d.Fish, "钓到鱼");
		Add(d.Cooked, "做菜");
		Add(d.CropsShipped, "出货作物");
		Add(d.Monsters, "打怪");
		Add(d.Foraged, "采集");
		Add(d.Crafted, "制作");
		if (d.Earned > 0)
		{
			bits.Add($"赚了 {d.Earned}g");
		}
		if (d.Places.Count > 0)
		{
			bits.Add("去了" + string.Join("、", d.Places));
		}
		if (d.GiftsTo.Count > 0)
		{
			bits.Add("给" + string.Join("、", d.GiftsTo.Select(display)) + "送了礼");
		}
		if (d.AwayAtNight)
		{
			bits.Add("那晚没回家");
		}
		return $"- {RelativeDay(d.Day, today)}：{(bits.Count == 0 ? "平静的一天" : string.Join("；", bits))}";
	}

	public static string HowItWent(StoryPackage p) => p.State switch
	{
		StoryStates.Done => "很顺利",
		StoryStates.Failed => "没成：" + p.Outcome,
		_ => "不了了之"
	};

	public static string Context(StoryContext c)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【农夫和你】");
		string bond = c.Roommate ? "是室友" : "结婚了";
		sb.AppendLine($"- 农夫是 {c.FarmerName}（{(c.FarmerMale ? "他" : "她")}）。你们{bond}{(c.DaysMarried is int d ? $"，已经 {d} 天" : "")}。好感：{c.Hearts} 心。");
		sb.AppendLine("- 孩子：" + (c.Children.Count == 0 ? "没有" : string.Join("、", c.Children)) + "。");
		if (c.OtherPartners.Count > 0)
		{
			sb.AppendLine("- 农夫还有别的伴侣：" + string.Join("、", c.OtherPartners.Select(c.Display)) + "。你知道，也接受。");
		}
		sb.AppendLine("- 今天：" + c.Today + "。");
		sb.AppendLine();
		sb.AppendLine("【农夫最近做了什么】");
		List<StoryActivityDay> days = c.Activity.OrderByDescending(x => x.Day).Take(ActivityDays).ToList();
		if (days.Count == 0)
		{
			sb.AppendLine("- （还没记下什么）");
		}
		foreach (StoryActivityDay day in days)
		{
			sb.AppendLine(ActivityLine(day, c.TodayNumber, c.Display));
		}
		sb.AppendLine("- 离农夫上次送你礼物：" + (c.DaysSinceGift is int g ? $"{g} 天" : "很久了或从没送过") + "。");
		if (c.Skills.Length > 0)
		{
			sb.AppendLine("- " + c.Skills);
		}
		if (c.Feelings.Trim().Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine(c.Feelings.Trim());
		}
		sb.AppendLine();
		sb.AppendLine("【以前和农夫的小故事（别重复）】");
		if (c.Earlier.Count == 0)
		{
			sb.AppendLine("- （这是第一段）");
		}
		foreach (StoryPackage p in c.Earlier.Take(5))
		{
			sb.AppendLine($"- 《{p.Title}》：{p.Summary}（{HowItWent(p)}）");
		}
		if (c.Theme.Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine("【农夫的心愿】");
			sb.AppendLine($"农夫希望下一段故事是关于：“{c.Theme}”。合你的性格的话，就围绕它来写。");
		}
		return sb.ToString();
	}

	// ---------------- 编剧要求 ----------------

	public static string Brief(string partner, int composition, string context, StoryCandidates candidates, string style, bool adult, Func<string, string> placeLabel)
	{
		StoryComposition comp = Compositions[Math.Clamp(composition, 0, Compositions.Count - 1)];
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("【怎么写】");
		sb.AppendLine($"- {partner}是个活生生的人，有自己的生活、朋友、家人、习惯和情绪。会别扭、会逗人、会小小地闹脾气、会害羞、会说错话、会改主意。用具体的小细节表现感情。");
		sb.AppendLine("- 说出口的话要短、口语、自然，像真人聊天。不用术语，不写统计和精确数字（物品数量除外），不要机器人或助手的腔调。");
		sb.AppendLine("- 把故事和农夫最近做的事、你们共同的记忆连起来：好多天没送你礼物，你可能有点小委屈；给别人送了礼，你可能有点吃醋；农夫起早贪黑或者夜里没回家，你可能担心或者骄傲；一起做饭、钓鱼、种地会让你心里暖。挑一两样用，别一条条列出来。");
		sb.AppendLine("- 别重复以前的故事。守着游戏的世界，不编大设定（不加新亲戚、不搬走、不生病）。");
		sb.AppendLine("- 绝不提游戏机制：任务、任务日志、菜单、心数、点数、mod、AI 都不提。");
		sb.AppendLine(adult
			? "- 亲密：你们是已婚的成年人。可以暗示亲密（多停留的一眼、一句悄悄的“早点回家吧”、淡出的一幕），但绝不描写性行为和身体，含蓄、有分寸。"
			: "- 亲密：只到温馨家庭游戏里会有的程度：拥抱、亲一下、牵手、靠在一起。任何性的内容都不写，连暗示也不要。");
		sb.AppendLine("- 故事风格：" + StyleText(style) + "。");
		sb.AppendLine();
		sb.AppendLine(context.Trim());
		sb.AppendLine();
		sb.AppendLine("【结构——照着写】");
		sb.AppendLine($"正好写 {comp.Steps.Length} 步，按这个顺序：{string.Join(", ", comp.Steps)}。");
		sb.AppendLine(comp.Hint);
		sb.AppendLine("每一步要等上一步完成才开始，所以后面的步骤可以提到前面的事。");
		sb.AppendLine();
		sb.AppendLine("【每种步骤的格式】");
		if (comp.Steps.Contains(StepKinds.Letter))
		{
			sb.AppendLine("letter: {\"kind\":\"letter\", \"text\":\"写给农夫的信，2 到 6 个短段落，用 \\n 分开，350 字以内，不署名（会替你加），不写日子和时间\", \"gift\": true 或 false（随信附一件小礼物就写 true，礼物由我们挑）}");
		}
		if (comp.Steps.Contains(StepKinds.Quest))
		{
			sb.AppendLine("quest: {\"kind\":\"quest\", \"quest_kind\":\"deliver | fish | slay\", \"target\":\"下面对应清单里的一个 id，照抄\", \"count\": 数字, \"title\":\"12 字以内\", \"description\":\"1 到 3 句，用你的口吻说想要什么、为什么；要说出物品或怪物和数量\", \"done_line\":\"农夫带来东西或告诉你办好了时你说的话\", \"done_mood\":\"happy | love | neutral\"}");
			sb.AppendLine("  数量：deliver 1 到 10（越贵越少），fish 1 到 3，slay 3 到 12。");
			sb.AppendLine("  deliver（农夫现在种得出、采得到或买得到的）：");
			sb.AppendLine(FormatList(candidates.Deliver));
			sb.AppendLine("  fish（这个季节咬钩的）：");
			sb.AppendLine(FormatList(candidates.Fish));
			sb.AppendLine("  slay（农夫在矿洞里已经见过的怪物）：");
			sb.AppendLine(candidates.Slay.Count == 0 ? "  （没有：农夫还没下过矿洞，别用 slay）" : FormatList(candidates.Slay));
		}
		if (comp.Steps.Contains(StepKinds.Event))
		{
			sb.AppendLine("event: {\"kind\":\"event\", \"place\":\"下面清单里的一个地点键\", \"time\":\"HH:MM\", \"invite\":\"约农夫在那里见面的一条短信：一两句，提到地点，不写日子和时间（会补上）\", \"lines\":[...]}");
			sb.AppendLine($"  戏就在你们见面的地方演：农夫走过来站在你旁边，没有别人。角色不走动，地点不变。{MinEventLines + 4} 到 16 行。");
			sb.AppendLine("  每行的类型：");
			sb.AppendLine("    {\"type\":\"say\", \"text\":\"你说的话（120 字以内）\", \"mood\":\"neutral | happy | sad | love | angry\"}");
			sb.AppendLine("    {\"type\":\"farmer\", \"text\":\"农夫在选项之外说的话（少用）\"}");
			sb.AppendLine("    {\"type\":\"narrate\", \"text\":\"一句描写气氛、地点或小动作的旁白（不带 mood）\"}");
			sb.AppendLine("    {\"type\":\"emote\", \"who\":\"npc | farmer\", \"emote\":\"heart | happy | sad | angry | surprised | question | music | sleepy | blush\"}");
			sb.AppendLine("    {\"type\":\"pause\"}   {\"type\":\"jump\"}（你高兴得蹦一下）");
			sb.AppendLine("    {\"type\":\"choice\", \"text\":\"一句简短的问题，或者农夫得回答的那一刻\", \"options\":[{\"text\":\"农夫说的话（30 字以内）\", \"reply\":\"你的回应\", \"mood\":\"...\", \"effect\":\"warm | neutral | cold\"}, {...}]}");
			sb.AppendLine("  每场戏 1 到 2 个选项，每个正好 2 项，两项要真的不一样（不只是好和坏）。effect：warm = 农夫体贴温柔，cold = 敷衍或伤人，neutral = 介于两者之间。选完之后戏照同一条线往下走，所以后面的台词要两种回答都说得通。");
			sb.AppendLine("  地点（键 = 名字，时间）：");
			foreach (MeetupPlace p in candidates.Places)
			{
				int open = Math.Max(p.OpenTime, EventEarliest);
				int close = Math.Min(p.CloseTime, EventLatest + 100);
				sb.AppendLine($"  {p.Key} = {placeLabel(p.Key)}（{(p.Indoors ? "室内" : "室外")}，{Clock.Text(open)}–{Clock.Text(close)}）");
			}
			sb.AppendLine($"  time：{EventEarliest / 100}:00 到 {EventLatest / 100}:00 之间，在地点开放的时间里。");
		}
		if (comp.Steps.Contains(StepKinds.Talk))
		{
			sb.AppendLine("talk: {\"kind\":\"talk\", \"topic\":\"2 到 8 个字：农夫会怎么称呼这件事（比如“昨晚的梦”）\", \"lines\":[{\"text\":\"你说的话\", \"mood\":\"neutral | happy | sad | love | angry\"}, ...]}，2 到 5 句。在家里或者农夫找到你的地方说。");
		}
		sb.AppendLine();
		sb.AppendLine("【输出】");
		sb.AppendLine("只回一个严格的 JSON 对象：前后不加别的字，不用 markdown 或代码块，不写注释。");
		sb.AppendLine("{\"title\":\"这段小故事的标题，12 字以内\", \"summary\":\"一句话说发生了什么，用你的视角（你会记住它）\", \"steps\":[ ……按顺序的各步…… ]}");
		sb.AppendLine("任何文字里都不要用这些字符：/ \\ # $ ^ % ~ @");
		return sb.ToString();
	}

	private static string FormatList(IReadOnlyList<StoryCandidate> list) => list.Count == 0 ? "  （没有）" : string.Join("\n", list.Select(c => $"  {c.Id} — {c.Name} — {c.Note}"));

	public static string System(string language, string partner, string personality, string brief) =>
		$"这次只用{language}写。\n\n你是《星露谷物语》的故事作者，要为农夫和伴侣{partner}写一段小故事。{partner}说的每一句都要像{partner}本人。\n\n# {partner}的性格（只是资料）\n{personality}\n\n{brief}";

	public static string User(string partner, int composition) => $"为{partner}和农夫写下一段小故事：{Describe(composition)}。";

	// ---------------- 解析 ----------------

	/// <summary>把 AI 的回复变成剧情包（没有 Id/Partner）。不合格返回 null 和原因。</summary>
	public static StoryPackage? Parse(string? raw, int composition, StoryCandidates candidates, Func<string, int> price, out string error)
	{
		error = "";
		if (string.IsNullOrWhiteSpace(raw))
		{
			error = "空回复";
			return null;
		}
		StoryComposition comp = Compositions[Math.Clamp(composition, 0, Compositions.Count - 1)];
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(JsonCleaner.Clean(raw), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
		}
		catch (JsonException ex)
		{
			error = "不是 JSON：" + ex.Message;
			return null;
		}
		using (doc)
		{
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				error = "不是对象";
				return null;
			}
			StoryPackage pkg = new StoryPackage
			{
				Title = Clean(Str(root, "title", "Title"), 16),
				Summary = Clean(Str(root, "summary", "Summary", "description"), 200),
				Composition = composition
			};
			if (pkg.Title.Length == 0)
			{
				error = "没有标题";
				return null;
			}
			if (!root.TryGetProperty("steps", out JsonElement steps) && !root.TryGetProperty("Steps", out steps))
			{
				error = "没有步骤";
				return null;
			}
			List<JsonElement> list = steps.ValueKind == JsonValueKind.Array ? steps.EnumerateArray().ToList() : new List<JsonElement>();
			if (list.Count < comp.Steps.Length)
			{
				error = $"写了 {list.Count} 步，要 {comp.Steps.Length} 步";
				return null;
			}
			for (int i = 0; i < comp.Steps.Length; i++)
			{
				StoryStep? step = ParseStep(list[i], comp.Steps[i], candidates, price, out string why);
				if (step == null)
				{
					error = $"第 {i + 1} 步（{comp.Steps[i]}）：{why}";
					return null;
				}
				step.Index = i;
				pkg.Steps.Add(step);
			}
			return pkg;
		}
	}

	private static StoryStep? ParseStep(JsonElement e, string kind, StoryCandidates candidates, Func<string, int> price, out string why)
	{
		why = "";
		StoryStep step = new StoryStep { Kind = kind };
		switch (kind)
		{
			case StepKinds.Letter:
				step.LetterText = CleanLetter(Str(e, "text", "content", "body"), 600);
				step.Gift = Bool(e, "gift", "has_gift", "attachment");
				if (step.LetterText.Length < 10)
				{
					why = "信太短";
					return null;
				}
				return step;
			case StepKinds.Quest:
				step.Quest = ParseQuest(e, candidates, price, out why);
				return step.Quest == null ? null : step;
			case StepKinds.Talk:
				step.Topic = Clean(Str(e, "topic", "title"), 10);
				step.Lines = ParseLines(e, talk: true);
				if (step.Lines.Count == 0)
				{
					why = "没有台词";
					return null;
				}
				if (step.Topic.Length == 0)
				{
					step.Topic = Clean(step.Lines[0].Text, 8);
				}
				step.Lines = step.Lines.Take(6).ToList();
				return step;
			case StepKinds.Event:
			{
				MeetupPlace? place = MeetupPlaces.Parse(Str(e, "place", "location", "where"));
				if (place == null || place.Key == MeetupPlaces.Home || !candidates.Places.Contains(place))
				{
					place = candidates.Places.FirstOrDefault(p => p.Key == MeetupPlaces.TownSquare);
				}
				if (place == null)
				{
					why = "没有地点";
					return null;
				}
				step.Place = place.Key;
				int time = MeetupRules.ParseTime(Str(e, "time")) ?? MeetupRules.DefaultTime;
				int open = Math.Max(place.OpenTime, EventEarliest);
				int latest = Math.Min(EventLatest, place.CloseTime - 100);
				step.Time = Math.Clamp(time, open, Math.Max(open, latest));
				step.Invite = Clean(Str(e, "invite", "invitation", "message"), 140);
				step.Lines = ParseLines(e, talk: false);
				int spoken = step.Lines.Count(l => l.Type is "say" or "choice");
				if (spoken < 2 || step.Lines.Count < MinEventLines)
				{
					why = $"只有 {step.Lines.Count} 行";
					return null;
				}
				int choices = 0;
				step.Lines = step.Lines.Where(l => l.Type != "choice" || ++choices <= 2).Take(MaxEventLines).ToList();
				return step;
			}
			default:
				why = "不认识的步骤";
				return null;
		}
	}

	private static StoryQuest? ParseQuest(JsonElement e, StoryCandidates candidates, Func<string, int> price, out string why)
	{
		why = "";
		string kind = Str(e, "quest_kind", "questKind", "type").Trim().ToLowerInvariant() switch
		{
			"fish" or "fishing" or "钓鱼" => StoryQuestKinds.Fish,
			"slay" or "kill" or "monster" or "击杀" or "打怪" => StoryQuestKinds.Slay,
			_ => StoryQuestKinds.Deliver
		};
		string target = Str(e, "target", "item", "item_id", "monster").Trim();
		StoryCandidate? pick = Match(candidates.For(kind), target);
		if (pick == null)
		{
			foreach (string k in new[] { StoryQuestKinds.Deliver, StoryQuestKinds.Fish, StoryQuestKinds.Slay })
			{
				pick = Match(candidates.For(k), target);
				if (pick != null)
				{
					kind = k;
					break;
				}
			}
		}
		if (pick == null)
		{
			why = $"目标“{target}”不在清单里";
			return null;
		}
		int count = Int(e, "count", "amount", "target_count") ?? 1;
		count = kind switch
		{
			StoryQuestKinds.Fish => Math.Clamp(count, 1, 3),
			StoryQuestKinds.Slay => Math.Clamp(count, 3, 12),
			_ => Math.Clamp(count, 1, DeliverCountLimit(price(pick.Id)))
		};
		StoryQuest q = new StoryQuest
		{
			Kind = kind,
			Target = pick.Id,
			TargetName = pick.Name,
			Count = count,
			Title = Clean(Str(e, "title"), 14),
			Description = Clean(Str(e, "description", "text"), 220),
			DoneLine = Clean(Str(e, "done_line", "doneLine", "completion"), 160),
			DoneMood = NormalizeMood(Str(e, "done_mood", "mood"))
		};
		if (q.Title.Length == 0)
		{
			q.Title = pick.Name;
		}
		q.Description = Regex.Replace(q.Description, "\\d+", count.ToString(CultureInfo.InvariantCulture));
		if (!q.Description.Contains(pick.Name))
		{
			q.Description = (q.Description + $" （{pick.Name} ×{count}）").Trim();
		}
		if (q.DoneLine.Length == 0)
		{
			q.DoneLine = "谢谢你。";
		}
		return q;
	}

	private static StoryCandidate? Match(IReadOnlyList<StoryCandidate> list, string target)
	{
		if (target.Length == 0)
		{
			return null;
		}
		return list.FirstOrDefault(c => string.Equals(c.Id, target, StringComparison.OrdinalIgnoreCase))
			?? list.FirstOrDefault(c => string.Equals(c.Id, "(O)" + target, StringComparison.OrdinalIgnoreCase))
			?? list.FirstOrDefault(c => string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase))
			?? (target.StartsWith("(", StringComparison.Ordinal) ? null : list.FirstOrDefault(c => target.Contains(c.Name)));
	}

	private static List<StoryLine> ParseLines(JsonElement e, bool talk)
	{
		List<StoryLine> lines = new List<StoryLine>();
		if ((!e.TryGetProperty("lines", out JsonElement arr) && !e.TryGetProperty("Lines", out arr)) || arr.ValueKind != JsonValueKind.Array)
		{
			return lines;
		}
		foreach (JsonElement l in arr.EnumerateArray())
		{
			if (l.ValueKind == JsonValueKind.String)
			{
				string text = Clean(l.GetString(), 160);
				if (text.Length > 0)
				{
					lines.Add(new StoryLine { Type = "say", Text = text });
				}
				continue;
			}
			if (l.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			string type = talk ? "say" : NormalizeType(Str(l, "type", "kind"));
			StoryLine line = new StoryLine { Type = type };
			switch (type)
			{
				case "say":
				case "farmer":
				case "narrate":
					line.Text = Clean(Str(l, "text", "line"), type == "narrate" ? 160 : 140);
					line.Mood = type == "say" ? NormalizeMood(Str(l, "mood", "emotion")) : "";
					if (line.Text.Length == 0)
					{
						continue;
					}
					break;
				case "emote":
					line.Who = Str(l, "who").Trim().ToLowerInvariant() == "farmer" ? "farmer" : "npc";
					line.Emote = NormalizeEmote(Str(l, "emote", "text"));
					break;
				case "choice":
					line.Text = Clean(Str(l, "text", "prompt", "question"), 80);
					if (l.TryGetProperty("options", out JsonElement opts) && opts.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement o in opts.EnumerateArray().Take(2))
						{
							StoryOption opt = new StoryOption
							{
								Text = Clean(Str(o, "text", "option"), 30),
								Reply = Clean(Str(o, "reply", "response"), 140),
								Mood = NormalizeMood(Str(o, "mood", "emotion")),
								Effect = NormalizeEffect(Str(o, "effect"))
							};
							if (opt.Text.Length > 0 && opt.Reply.Length > 0)
							{
								line.Options.Add(opt);
							}
						}
					}
					if (line.Options.Count != 2)
					{
						StoryOption? only = line.Options.FirstOrDefault();
						if (only == null)
						{
							continue;
						}
						line = new StoryLine { Type = "say", Text = only.Reply, Mood = only.Mood };
					}
					break;
				case "pause":
				case "jump":
					break;
				default:
					continue;
			}
			lines.Add(line);
		}
		return lines;
	}

	private static string NormalizeType(string t) => t.Trim().ToLowerInvariant() switch
	{
		"say" or "speak" or "npc" or "spouse" or "配偶说" => "say",
		"farmer" or "player" or "玩家说" => "farmer",
		"narrate" or "narration" or "message" or "旁白" or "描述" => "narrate",
		"emote" or "表情" => "emote",
		"choice" or "question" or "选项" => "choice",
		"pause" or "wait" or "等待" => "pause",
		"jump" or "跳" => "jump",
		string other => other
	};

	public static string NormalizeMood(string? m)
	{
		string t = (m ?? "").Trim().ToLowerInvariant();
		if (t.Contains("happy") || t.Contains("开心") || t.Contains("joy") || t.Contains("excited"))
		{
			return "happy";
		}
		if (t.Contains("sad") || t.Contains("悲伤") || t.Contains("worr") || t.Contains("担心"))
		{
			return "sad";
		}
		if (t.Contains("love") || t.Contains("爱") || t.Contains("blush") || t.Contains("害羞") || t.Contains("shy"))
		{
			return "love";
		}
		if (t.Contains("angry") || t.Contains("生气") || t.Contains("annoyed"))
		{
			return "angry";
		}
		return "neutral";
	}

	private static string NormalizeEffect(string? e) => (e ?? "").Trim().ToLowerInvariant() switch
	{
		"warm" or "good" or "positive" => "warm",
		"cold" or "bad" or "negative" => "cold",
		_ => "neutral"
	};

	private static string NormalizeEmote(string? e)
	{
		string t = (e ?? "").Trim().ToLowerInvariant();
		if (Emotes.Contains(t))
		{
			return t;
		}
		return t switch
		{
			"爱心" or "love" => "heart",
			"开心" => "happy",
			"悲伤" => "sad",
			"生气" => "angry",
			"惊讶" or "exclamation" or "surprise" => "surprised",
			"困惑" or "confused" => "question",
			"音乐" => "music",
			_ => "happy"
		};
	}

	/// <summary>去掉事件脚本和对话里的保留字符、表情符号，压成一行，截断。</summary>
	public static string Clean(string? text, int max)
	{
		string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
		t = StripReserved(t);
		t = Regex.Replace(t, "\\s{2,}", " ").Trim().Trim('"', '“', '”', '「', '」');
		return Cut(t, max);
	}

	/// <summary>信：保留换行（最多空一行）。</summary>
	public static string CleanLetter(string? text, int max)
	{
		string t = (text ?? "").Replace("\r", "").Replace("\\n", "\n");
		t = StripReserved(t);
		t = Regex.Replace(t, "[ \t]{2,}", " ");
		t = Regex.Replace(t, "\n{3,}", "\n\n").Trim();
		return Cut(t, max);
	}

	private static string StripReserved(string t)
	{
		StringBuilder sb = new StringBuilder(t.Length);
		foreach (char ch in t)
		{
			if (char.IsSurrogate(ch) || (ch >= '☀' && ch <= '➿') || ch == '️' || ch == '‍')
			{
				continue;
			}
			switch (ch)
			{
				case '/': sb.Append('／'); break;
				case '\\' or '$' or '^' or '@' or '`': break;
				case '#': sb.Append('＃'); break;
				case '%': sb.Append('％'); break;
				case '~': sb.Append('～'); break;
				case '"': sb.Append('“'); break;
				case '|': sb.Append('｜'); break;
				default: sb.Append(ch); break;
			}
		}
		return sb.ToString().Replace("(break)", "");
	}

	private static string Cut(string t, int max)
	{
		if (t.Length <= max)
		{
			return t;
		}
		string cut = t.Substring(0, max);
		int stop = cut.LastIndexOfAny(new[] { '。', '！', '？', '.', '!', '?' });
		return stop >= max / 2 ? cut.Substring(0, stop + 1) : cut + "…";
	}

	private static string Str(JsonElement e, params string[] names)
	{
		foreach (string n in names)
		{
			if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out JsonElement v))
			{
				if (v.ValueKind == JsonValueKind.String)
				{
					return v.GetString() ?? "";
				}
				if (v.ValueKind == JsonValueKind.Number)
				{
					return v.GetRawText();
				}
			}
		}
		return "";
	}

	private static bool Bool(JsonElement e, params string[] names)
	{
		foreach (string n in names)
		{
			if (e.TryGetProperty(n, out JsonElement v))
			{
				if (v.ValueKind == JsonValueKind.True)
				{
					return true;
				}
				if (v.ValueKind == JsonValueKind.String)
				{
					return v.GetString()?.Trim().ToLowerInvariant() is "true" or "yes" or "是";
				}
			}
		}
		return false;
	}

	private static int? Int(JsonElement e, params string[] names)
	{
		foreach (string n in names)
		{
			if (e.TryGetProperty(n, out JsonElement v))
			{
				if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i))
				{
					return i;
				}
				if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int j))
				{
					return j;
				}
			}
		}
		return null;
	}

	// ---------------- 台词和事件脚本 ----------------

	public static string MoodSuffix(string mood) => mood switch
	{
		"happy" => "$h",
		"sad" => "$s",
		"love" => "$l",
		"angry" => "$a",
		_ => ""
	};

	public static int EmoteId(string emote) => emote switch
	{
		"heart" => 20,
		"sad" => 28,
		"angry" => 12,
		"surprised" => 16,
		"question" => 8,
		"music" => 56,
		"sleepy" => 24,
		"blush" => 60,
		_ => 32
	};

	/// <summary>伴侣在对话框里说几句：一句一页。</summary>
	public static string DialogueText(IEnumerable<StoryLine> lines) => string.Join("#$b#", lines.Where(l => l.Text.Length > 0).Select(l => l.Text + MoodSuffix(l.Mood)));

	public static int Facing(int fromX, int fromY, int toX, int toY)
	{
		int dx = toX - fromX, dy = toY - fromY;
		if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0)
		{
			return dx > 0 ? 1 : 3;
		}
		return dy > 0 ? 2 : 0;
	}

	/// <summary>就地演的事件脚本：两人面对面，选项用 quickQuestion，每个分支末尾记下选了哪个。</summary>
	public static string EventScript(string packageId, int stepIndex, string actor, (int X, int Y) npc, (int X, int Y) farmer, IReadOnlyList<StoryLine> lines, string farmerName, string choicePrompt)
	{
		List<string> c = new List<string>
		{
			"none",
			$"{npc.X} {npc.Y}",
			$"farmer {farmer.X} {farmer.Y} {Facing(farmer.X, farmer.Y, npc.X, npc.Y)} {actor} {npc.X} {npc.Y} {Facing(npc.X, npc.Y, farmer.X, farmer.Y)}",
			"skippable",
			"pause 400"
		};
		int point = 0;
		foreach (StoryLine l in lines)
		{
			switch (l.Type)
			{
				case "say":
					c.Add($"speak {actor} \"{l.Text}{MoodSuffix(l.Mood)}\"");
					break;
				case "farmer":
					c.Add($"message \"{farmerName}：{l.Text}\"");
					break;
				case "narrate":
					c.Add($"message \"{l.Text}\"");
					break;
				case "emote":
					c.Add($"emote {(l.Who == "farmer" ? "farmer" : actor)} {EmoteId(l.Emote)}");
					break;
				case "pause":
					c.Add("pause 800");
					break;
				case "jump":
					c.Add($"jump {actor}");
					break;
				case "choice" when l.Options.Count == 2:
				{
					StringBuilder q = new StringBuilder("quickQuestion " + (l.Text.Length > 0 ? l.Text : choicePrompt));
					foreach (StoryOption o in l.Options)
					{
						q.Append('#').Append(o.Text);
					}
					for (int i = 0; i < l.Options.Count; i++)
					{
						StoryOption o = l.Options[i];
						q.Append("(break)").Append($"speak {actor} \"{o.Reply}{MoodSuffix(o.Mood)}\"").Append('\\').Append($"{ChoiceCommand} {packageId} {stepIndex} {point} {i}");
					}
					c.Add(q.ToString());
					point++;
					break;
				}
			}
		}
		c.Add("pause 500");
		c.Add("end");
		return string.Join("/", c);
	}

	/// <summary>见面戏的结果：好感变化、农夫说过的话、伴侣的感受。</summary>
	public static (int Points, List<string> Said, string Feeling) SceneOutcome(StoryStep step)
	{
		List<StoryLine> points = step.Lines.Where(l => l.Type == "choice" && l.Options.Count == 2).ToList();
		int total = EventFriendship, warm = 0, cold = 0;
		List<string> said = new List<string>();
		for (int i = 0; i < points.Count; i++)
		{
			int pick = i < step.Choices.Count ? step.Choices[i] : -1;
			if (pick is < 0 or > 1)
			{
				continue;
			}
			StoryOption o = points[i].Options[pick];
			said.Add("“" + o.Text + "”");
			if (o.Effect == "warm")
			{
				total += WarmBonus;
				warm++;
			}
			else if (o.Effect == "cold")
			{
				total += ColdPenalty;
				cold++;
			}
		}
		string feeling = warm > cold ? "这对你意义很大。" : cold > warm ? "有些话让你有点难受。" : "很愉快。";
		return (total, said, feeling);
	}

	// ---------------- 记忆和提示词 ----------------

	public static string FinishedMemory(StoryPackage p) => $"你和农夫一起经历了一段小故事《{p.Title}》：{p.Summary}";

	public static string QuestExpiredMemory(StoryPackage p, StoryQuest q) => q.Kind == StoryQuestKinds.Slay
		? $"你拜托农夫去矿洞里收拾 {q.Count} 只{q.TargetName}（《{p.Title}》），可农夫一直没顾上，有点失落。"
		: $"你拜托农夫带 {q.Count} 个{q.TargetName}（《{p.Title}》），可农夫一直没顾上，有点失落。";

	public static string QuestDoneMemory(StoryPackage p, StoryQuest q) => q.Kind == StoryQuestKinds.Slay
		? $"农夫为你下矿洞收拾了 {q.Count} 只{q.TargetName}（《{p.Title}》），你松了口气，也有点骄傲。"
		: $"农夫照你说的带来了 {q.Count} 个{q.TargetName}（《{p.Title}》），你很开心。";

	public static string SceneMemory(StoryPackage p, string place, List<string> said, string feeling) =>
		$"你和农夫在{place}见了面（《{p.Title}》）。{(said.Count > 0 ? "农夫说了" + string.Join("和", said) + "。" : "")}{feeling}";

	public static string MissedMemory(StoryPackage p, string place) => $"你在{place}等农夫（《{p.Title}》），农夫却没来，心里有点受伤。";

	public static string StepPromptLine(StoryStep step, bool letterRead, bool meetupArranged) => step.Kind switch
	{
		StepKinds.Letter => step.Delivered ? "你已经给农夫寄了一封信说这件事" + (letterRead ? "。" : "，农夫还没读。") : "你正在给农夫写一封信说这件事，先别提，这是个惊喜。",
		StepKinds.Quest when step.Quest != null => step.Quest.Kind == StoryQuestKinds.Slay
			? $"你拜托农夫去矿洞里收拾 {step.Quest.Count} 只{step.Quest.TargetName}，还没办完。可以轻轻提一次。"
			: $"你拜托农夫带 {step.Quest.Count} 个{step.Quest.TargetName}给你，还没办完。可以轻轻提一次。",
		StepKinds.Event => meetupArranged ? "你约了农夫见面（见你和农夫的约定），很期待，别说出你准备了什么。" : "你想找个时间和农夫见一面。",
		StepKinds.Talk => $"你有话想跟农夫说（{step.Topic}），等农夫问起再说。",
		_ => ""
	};

	public static string PromptBlock(StoryPackage? active, string stepLine, StoryPackage? recent)
	{
		List<string> lines = new List<string>();
		if (active != null)
		{
			lines.Add($"- 你和农夫正在经历一段小故事《{active.Title}》：{active.Summary} 别剧透后面的事。");
			if (stepLine.Length > 0)
			{
				lines.Add("- " + stepLine);
			}
		}
		if (recent != null)
		{
			lines.Add($"- 最近你和农夫有过一段小故事《{recent.Title}》：{recent.Summary}（{HowItWent(recent)}）。可以提起。");
		}
		return lines.Count == 0 ? "" : "【和农夫之间的小故事】\n" + string.Join("\n", lines);
	}

	// ---------------- 界面文字 ----------------

	public static string StateLabel(string state) => state switch
	{
		StoryStates.Done => Tr.Get("story.state.done", "圆满"),
		StoryStates.Failed => Tr.Get("story.state.failed", "错过了"),
		_ => Tr.Get("story.state.dropped", "不了了之")
	};

	public static string TodayWhat(string kind) => kind switch
	{
		StepKinds.Event => Tr.Get("todayPage.storyEvent", "约你见面"),
		StepKinds.Talk => Tr.Get("todayPage.storyTalk", "有话想跟你说"),
		StepKinds.Quest => Tr.Get("todayPage.storyQuest", "拜托你一件事"),
		_ => Tr.Get("todayPage.storyLetter", "给你写了信")
	};
}
