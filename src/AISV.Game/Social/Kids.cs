using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AISV.Core.Dialogue;
using AISV.Core.Persona;
using StardewValley;
using StardewValley.Characters;

namespace AISV.Game.Social;

/// <summary>农夫自己家的孩子：成长阶段、记录用的键、聊天要的家里情况。</summary>
public static class Kids
{
	/// <summary>游戏 Child.Age：0 新生儿、1 婴儿、2 会爬、3 会走。</summary>
	public static string StageName(int age) => age switch
	{
		0 => "刚出生",
		1 => "还是婴儿",
		2 => "会爬了",
		_ => "会走了"
	};

	/// <summary>说话用的阶段描述。</summary>
	public static string StageText(int age) => age >= 3 ? "刚学会走路、会说简单的话，大概三四岁" : StageName(age);

	public static bool CanTalk(Child child) => child.Age >= 3;

	/// <summary>长大以后的说话材料（阶段、阶段说法、长大那天写下的样子、性子），由 <see cref="KidGrowthService"/> 提供。</summary>
	public static Func<Child, (string StageId, string StageText, string Grown, IReadOnlyList<string> Traits)>? Growth { get; set; }

	/// <summary>家里情况里怎么说这个孩子多大。</summary>
	public static string Describe(Child child) => KidGrowthService.Grown(child) ? AISV.Core.Family.KidStages.Label(KidGrowthService.Stage(child)) : StageName(child.Age);

	/// <summary>是不是孩子：自己家的孩子（游戏的 Child），或者名单 / 年龄上算孩子的村民。</summary>
	public static bool IsChild(NPC? npc) => npc is Child || (npc != null && ChildSafety.IsChild(npc.Name, npc.Age));

	/// <summary>所有农夫家里的孩子。</summary>
	public static IEnumerable<Child> All()
	{
		foreach (Farmer f in Game1.getAllFarmers())
		{
			foreach (Child c in f.getChildren())
			{
				yield return c;
			}
		}
	}

	/// <summary>每天登记一遍孩子的名字给安全规则用（和村民重名的不登记，免得把村民当成孩子）。</summary>
	public static void RegisterFamily()
	{
		try
		{
			ChildSafety.SetFamily(All().Select(c => c.Name).Where(n => !Game1.characterData.ContainsKey(n ?? "")));
		}
		catch
		{
			ChildSafety.SetFamily(Array.Empty<string>());
		}
	}

	/// <summary>记录和角色卡用的键：父母 id + 去掉怪字符的名字。</summary>
	public static string Key(Child child)
	{
		StringBuilder sb = new StringBuilder();
		foreach (char ch in child.Name ?? "")
		{
			sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
		}
		return child.idOfParent.Value + "_" + sb;
	}

	public static Child? Find(string key) => All().FirstOrDefault(c => Key(c) == key);

	public static Farmer Parent(Child child) => Game1.GetPlayer(child.idOfParent.Value) ?? Game1.player;

	/// <summary>家里的情况，一条一句（和原来 ChildTalk 写的一样）。</summary>
	public static List<string> Home(Farmer p, Func<string, string> display, Func<string, bool>? rewritten)
	{
		string me = p.IsMale ? "爸爸" : "妈妈";
		string? spouse = string.IsNullOrWhiteSpace(p.spouse) ? null : p.spouse;
		string? partner = spouse ?? p.friendshipData.Pairs.FirstOrDefault(kv => kv.Value.IsDivorced()).Key;
		bool partnerMale = partner != null && Game1.getCharacterFromName(partner) is NPC pn ? pn.Gender == Gender.Male : !p.IsMale;
		string other = partnerMale ? (p.IsMale ? "另一个爸爸" : "爸爸") : (p.IsMale ? "妈妈" : "另一个妈妈");
		List<string> home = new List<string>();
		if (spouse != null)
		{
			int hearts = p.friendshipData.TryGetValue(spouse, out Friendship? f) ? f.Points / 250 : 0;
			home.Add($"{other}是{display(spouse)}，住在家里。");
			if (hearts < 6)
			{
				home.Add($"{me}和{other}最近不怎么说话，家里冷冷清清的。");
			}
			if (rewritten?.Invoke(spouse) == true)
			{
				home.Add($"{other}最近好像变了个人：笑得很完美，说话的样子和以前不一样，你说不上来哪里不对，有点怕。");
			}
		}
		else
		{
			foreach ((string npc, Friendship f) in p.friendshipData.Pairs)
			{
				if (f.IsDivorced())
				{
					home.Add($"{other}（{display(npc)}）已经搬走，不住在家里了，你很想{other}。");
					break;
				}
			}
		}
		foreach (Child sib in p.getChildren().Where(c => c.Name != null))
		{
			home.Add($"家里的孩子：{sib.Name}（{Describe(sib)}，{(sib.Gender == Gender.Male ? "男孩" : "女孩")}）。");
		}
		if (home.Count == 0)
		{
			home.Add($"家里只有你和{me}。");
		}
		return home;
	}

	public static ChildContext Context(Child child, ChildCards? cards, Func<string, string> display, Func<string, bool>? rewritten, string language)
	{
		Farmer p = Parent(child);
		string place = child.currentLocation?.DisplayName ?? "";
		string weather = Game1.isRaining ? "下雨" : Game1.isSnowing ? "下雪" : "晴";
		(string stageId, string stageText, string grown, IReadOnlyList<string> traits) = Growth?.Invoke(child) ?? (AISV.Core.Family.KidStages.Toddler, StageText(child.Age), "", Array.Empty<string>());
		return new ChildContext
		{
			Key = Key(child),
			Name = child.Name ?? "",
			Boy = child.Gender == Gender.Male,
			Stage = stageText,
			StageId = stageId,
			Grown = grown,
			Traits = traits,
			ParentName = p.Name,
			ParentRole = p.IsMale ? "爸爸" : "妈妈",
			Home = Home(p, display, rewritten).Where(h => !h.Contains("家里的孩子：" + child.Name + "（")).ToList(),
			Now = $"{HistoryFormatter.SeasonName(Game1.currentSeason)}，{Game1.timeOfDay / 100}点，{weather}，在{place}",
			Card = cards?.Get(Key(child)),
			Language = language,
			Day = Game1.Date.TotalDays
		};
	}
}
