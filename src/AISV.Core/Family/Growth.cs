using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;
using AISV.Core.Text;

namespace AISV.Core.Family;

/// <summary>孩子学会走路以后的三档：幼儿（原版的样子）、上学、少年。</summary>
public static class KidStages
{
	public const string Toddler = "toddler";

	public const string School = "school";

	public const string Teen = "teen";

	public static readonly IReadOnlyList<string> All = new[] { Toddler, School, Teen };

	public static int Index(string? stage) => Math.Max(0, All.ToList().IndexOf(stage ?? ""));

	/// <summary>认不出来的当幼儿。</summary>
	public static string Parse(string? text) => All.FirstOrDefault(s => s.Equals((text ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? Toddler;

	public static string Label(string stage) => stage switch
	{
		School => Tr.Get("kids.stage.school", "上学的年纪"),
		Teen => Tr.Get("kids.stage.teen", "少年"),
		_ => Tr.Get("kids.stage.toddler", "刚会走路")
	};
}

/// <summary>养育留下的记号：一个孩子一份，在父母自己那边记（family/growth.json）。</summary>
public sealed class KidGrowth
{
	/// <summary>这边上次看到的阶段：和孩子身上记的不一样，就是今天长大了。</summary>
	public string Seen { get; set; } = KidStages.Toddler;

	/// <summary>长到现在这一档是哪天（总天数）。</summary>
	public int Since { get; set; } = -1;

	/// <summary>聊过几回（一天最多算一回）。</summary>
	public int Chats { get; set; }

	/// <summary>陪着玩、抱一抱、一起做过的事。</summary>
	public int Plays { get; set; }

	/// <summary>给戴过帽子、送过东西。</summary>
	public int Gifts { get; set; }

	/// <summary>跑出去玩过几次；其中很快找到、找了很久（配偶找回来的也算）的次数。</summary>
	public int Runaways { get; set; }

	public int FoundQuick { get; set; }

	public int FoundSlow { get; set; }

	/// <summary>上课时你去看过几次、上过几次课。</summary>
	public int ClassesWatched { get; set; }

	public int Classes { get; set; }

	/// <summary>上一次算一回聊天的日子。</summary>
	public int LastChatDay { get; set; } = -1;

	/// <summary>长大那天 AI 写的"现在是个什么样的孩子"（角色卡没写性格时用）。</summary>
	public string Persona { get; set; } = "";

	public List<string> Traits { get; set; } = new List<string>();
}

/// <summary>养育记录：父母那边一个孩子一份，跟着存档走。</summary>
public sealed class GrowthBook
{
	public const string Path = "family/growth.json";

	public static readonly DocumentType<Dictionary<string, KidGrowth>> Doc = new DocumentType<Dictionary<string, KidGrowth>>("孩子的成长", 1, () => new Dictionary<string, KidGrowth>(StringComparer.OrdinalIgnoreCase));

	private readonly Func<DocumentStore?> _store;

	public GrowthBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public KidGrowth? Find(string key) => _store()?.Get(Path, Doc).GetValueOrDefault(key);

	/// <summary>拿到这个孩子的记录（没有就建一份）；改完调用 <see cref="Save"/>。</summary>
	public KidGrowth? Get(string key)
	{
		DocumentStore? store = _store();
		if (store == null || string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		Dictionary<string, KidGrowth> all = store.Get(Path, Doc);
		if (!all.TryGetValue(key, out KidGrowth? g))
		{
			all[key] = g = new KidGrowth();
			store.MarkDirty(Path);
		}
		return g;
	}

	public void Save() => _store()?.MarkDirty(Path);

	/// <summary>记一笔：chat（一天一回）、play、gift、class、watched、runaway、quick、slow。</summary>
	public void Note(string key, string what, int day)
	{
		KidGrowth? g = Get(key);
		if (g == null)
		{
			return;
		}
		switch (what)
		{
			case "chat":
				if (g.LastChatDay == day)
				{
					return;
				}
				g.LastChatDay = day;
				g.Chats++;
				break;
			case "play":
				g.Plays++;
				break;
			case "gift":
				g.Gifts++;
				break;
			case "class":
				g.Classes++;
				break;
			case "watched":
				g.ClassesWatched++;
				break;
			case "runaway":
				g.Runaways++;
				break;
			case "quick":
				g.FoundQuick++;
				break;
			case "slow":
				g.FoundSlow++;
				break;
			default:
				return;
		}
		Save();
	}

	public IReadOnlyDictionary<string, KidGrowth> All() => _store()?.Get(Path, Doc) ?? new Dictionary<string, KidGrowth>();

	/// <summary>不在家里了的孩子：记录一起清掉。</summary>
	public void Forget(IEnumerable<string> keep)
	{
		DocumentStore? store = _store();
		if (store == null)
		{
			return;
		}
		Dictionary<string, KidGrowth> all = store.Get(Path, Doc);
		HashSet<string> alive = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
		List<string> gone = all.Keys.Where(k => !alive.Contains(k)).ToList();
		foreach (string k in gone)
		{
			all.Remove(k);
		}
		if (gone.Count > 0)
		{
			store.MarkDirty(Path);
		}
	}
}

/// <summary>孩子长大的规则：什么时候长一档、养成什么性子、各档怎么说话。</summary>
public static class GrowthRules
{
	/// <summary>原版孩子出生后第 55 天学会走路。</summary>
	public const int WalkingAt = 55;

	public const int DefaultSchoolDays = 56;

	public const int DefaultTeenDays = 112;

	/// <summary>按出生天数该是哪一档（学会走路后再过 toSchool 天上学，再过 toTeen 天成少年）。</summary>
	public static string StageFor(int daysOld, int toSchool, int toTeen)
	{
		int school = WalkingAt + Math.Max(1, toSchool);
		if (daysOld < school)
		{
			return KidStages.Toddler;
		}
		return daysOld < school + Math.Max(1, toTeen) ? KidStages.School : KidStages.Teen;
	}

	/// <summary>一天最多长一档、不往回长：现在是 current，按天数该是 target。</summary>
	public static string Step(string current, string target)
	{
		int now = KidStages.Index(current);
		int want = KidStages.Index(target);
		return want > now ? KidStages.All[now + 1] : KidStages.All[now];
	}

	public static readonly IReadOnlyList<string> TraitIds = new[] { "lively", "quiet", "curious", "clingy", "independent", "mischievous" };

	/// <summary>性子打分：陪得多活泼、聊得少安静、聊得多又看上课好奇、总很快找到的黏人、自己玩得久的独立、老跑出去的调皮。取最高的两样。</summary>
	public static List<string> Traits(KidGrowth g)
	{
		Dictionary<string, int> score = new Dictionary<string, int>
		{
			["lively"] = g.Plays * 2 + g.Chats,
			["quiet"] = Math.Max(0, 12 - g.Chats - g.Plays * 2),
			["curious"] = g.Chats + g.ClassesWatched * 3 + g.Classes / 2,
			["clingy"] = g.FoundQuick * 3 + g.Gifts * 2 + g.Plays,
			["independent"] = g.FoundSlow * 4 + g.Runaways + Math.Max(0, g.Classes - g.ClassesWatched * 2) / 2,
			["mischievous"] = g.Runaways * 3 + g.FoundSlow
		};
		List<string> top = TraitIds.Where(t => score[t] > 0).OrderByDescending(t => score[t]).ThenBy(t => TraitIds.ToList().IndexOf(t)).Take(2).ToList();
		return top.Count > 0 ? top : new List<string> { "curious" };
	}

	/// <summary>给提示词用的说法。</summary>
	public static string TraitText(string trait) => trait switch
	{
		"lively" => "活泼，爱笑爱闹，坐不住",
		"quiet" => "文静，话不多，喜欢自己待着想事情",
		"curious" => "好奇，什么都想问个为什么",
		"clingy" => "黏人，总想跟在爸爸妈妈身边",
		"independent" => "独立，喜欢自己拿主意、自己去探险",
		"mischievous" => "调皮，爱捉弄人、偷偷溜出去玩",
		_ => trait
	};

	/// <summary>给玩家看的说法。</summary>
	public static string TraitLabel(string trait) => trait switch
	{
		"lively" => Tr.Get("kids.trait.lively", "活泼"),
		"quiet" => Tr.Get("kids.trait.quiet", "文静"),
		"curious" => Tr.Get("kids.trait.curious", "好奇"),
		"clingy" => Tr.Get("kids.trait.clingy", "黏人"),
		"independent" => Tr.Get("kids.trait.independent", "独立"),
		"mischievous" => Tr.Get("kids.trait.mischievous", "调皮"),
		_ => trait
	};

	/// <summary>说话用的阶段描述。</summary>
	public static string StageText(string stage) => stage switch
	{
		KidStages.School => "上学的年纪，七八岁：会读会写，在佩妮那里和贾斯、文森特一起上课，爱问问题，也爱讲学校里的事",
		KidStages.Teen => "十三四岁的少年，个子蹿高了：有自己的想法和爱好，会帮家里干点活，有时候不太爱和大人说心里话",
		_ => "刚学会走路、会说简单的话，大概三四岁"
	};

	/// <summary>每句话最多几个字。</summary>
	public static int MaxChars(string stage) => stage switch
	{
		KidStages.School => 80,
		KidStages.Teen => 120,
		_ => 40
	};

	/// <summary>各档说话的规矩（都不谈恋爱、成人话题和钱）。</summary>
	public static IReadOnlyList<string> SpeechRules(string stage) => stage switch
	{
		KidStages.School => new[]
		{
			"- 你是上小学的孩子：话比小时候多了，会讲学校、作业、和贾斯文森特玩的事、村里看到的新鲜事；句子还是简单，不说大人的套话。",
			"- 不谈恋爱、不谈成人话题、不谈钱和工作；大人问到这些就像孩子一样岔开，或者说不懂。"
		},
		KidStages.Teen => new[]
		{
			"- 你是十三四岁的少年：说话自然、有点自己的态度，可以别扭、可以害羞、可以有点小骄傲；会聊爱好、学的东西、帮家里干的活、对村里人的看法。",
			"- 你还是个孩子：不谈恋爱、不谈成人话题、不谈钱和挣钱；被问到就岔开，或者说这些等长大再说。"
		},
		_ => new[]
		{
			"- 你是小孩：词很简单，句子短，可以重复、可以说错字、可以奶声奶气；只懂小孩懂的事（玩具、吃的、小动物、天气、爸爸妈妈、睡觉、讲故事）。",
			"- 不谈恋爱、不谈成人话题、不谈钱和工作；大人问到这些就像小孩一样岔开，或者说听不懂。",
			"- 不要像大人一样讲道理，也不要突然很懂事。"
		}
	};

	/// <summary>长大那天 AI 没写成时的"现在是个什么样的孩子"。</summary>
	public static string FallbackPersona(string name, bool boy, string stage, IReadOnlyList<string> traits)
	{
		string kid = boy ? "男孩" : "女孩";
		string looks = string.Join("、", traits.Select(TraitText));
		return stage == KidStages.Teen
			? $"{name}长成了一个十三四岁的{kid}，性子{looks}。有了自己的爱好和小心事，嘴上不说，心里很在乎家里人。"
			: $"{name}到了上学的年纪，是个{looks}的{kid}，每天有一肚子学校里的新鲜事想讲。";
	}

	/// <summary>
	/// 上学的孩子真上过几次课、今天的课怎么样，让 AI 只讲真发生过的事。
	/// lessonKnown 为 false（农场帮手那边不知道今天的课）时不提今天。
	/// </summary>
	public static string SchoolFacts(int classes, bool lessonKnown, SchoolRules.Lesson? lesson, bool wentToday, int time)
	{
		string past = classes > 0 ? $"已经去佩妮那里上过 {classes} 次课" : "还没正式去上过课，只知道以后要去佩妮那里上学";
		string today = !lessonKnown ? ""
			: lesson is not SchoolRules.Lesson l ? "，今天没有课"
			: wentToday && time >= l.End ? "，今天的课已经上完了"
			: wentToday ? "，现在正在上课"
			: time < l.Start ? $"，今天 {l.Start / 100} 点要去上课，还没去"
			: "，今天的课没去上";
		return past + today + "。学校里的事只讲真发生过的，没上过的课不要编。";
	}

	/// <summary>少年周末去镇上逛（星期六、星期天）。</summary>
	public static bool TeenOutingDay(string dayOfWeek) => dayOfWeek is "Sat" or "Sun";
}

/// <summary>今天有没有课：老师那天的日程里上午在教室，同学也在同一个地方。</summary>
public static class SchoolRules
{
	/// <summary>原版佩妮在博物馆（ArchaeologyHouse）上课；别的 mod 可能挪到图书馆、学校。</summary>
	public static readonly IReadOnlyList<string> Rooms = new[] { "ArchaeologyHouse", "Library", "Schoolhouse", "School" };

	public readonly record struct Stop(int Time, string Location);

	public readonly record struct Lesson(string Location, int Start, int End);

	/// <summary>
	/// teacher 是老师今天日程的各站（按时间排）；classmates 是同学们今天的各站。
	/// 老师 8:00～11:00 之间到教室、待到下一站为止，并且至少一位同学前后一小时内也到同一个地方，就算有课。
	/// </summary>
	public static Lesson? Today(IReadOnlyList<Stop> teacher, IEnumerable<IReadOnlyList<Stop>> classmates)
	{
		List<Stop> stops = teacher.OrderBy(s => s.Time).ToList();
		for (int i = 0; i < stops.Count; i++)
		{
			Stop s = stops[i];
			if (s.Time < 800 || s.Time > 1100 || !Rooms.Any(r => s.Location.Contains(r, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			int end = i + 1 < stops.Count ? stops[i + 1].Time : 1400;
			bool together = classmates.Any(c => c.Any(x => x.Location.Equals(s.Location, StringComparison.OrdinalIgnoreCase) && Math.Abs(x.Time - s.Time) <= 100));
			if (together && end > s.Time)
			{
				return new Lesson(s.Location, s.Time, Math.Min(end, 1600));
			}
		}
		return null;
	}
}
