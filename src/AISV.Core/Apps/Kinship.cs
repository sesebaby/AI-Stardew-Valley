using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AISV.Core.Apps;

public enum PairKind
{
	Married,
	Engaged,
	Dating,
	Siblings,
	Roommates
}

public enum ElderRole
{
	Parent,
	Grandparent,
	AuntOrUncle,
	Godparent,
	Stepparent
}

/// <summary>认出来的一句亲属称呼：要么 A、B 是一对（Pair），要么 A 是 B 的长辈（Role）。</summary>
public sealed record KinClaim(string A, string B, PairKind? Pair, ElderRole? Role);

/// <summary>晚辈挂在长辈这一格下面：Person 在 Cell 里，长辈是上一格里的 Elders。</summary>
public sealed record KinBelow(KinCell Cell, string Person, IReadOnlyList<string> Elders, ElderRole Role);

/// <summary>树之外还要交代的长辈关系，比如既是姑姑又不是直接带大的。对方可能在另一棵树上。</summary>
public sealed record KinSide(string Elder, string Younger, ElderRole Role);

/// <summary>树之外还要交代的一对，比如两家的孩子在交往。对方可能在另一棵树上。</summary>
public sealed record KinLink(string A, string B, PairKind Pair);

/// <summary>
/// 家谱上的一格：一个人，或站在一起的几个人。People[0] 和后面每个人的关系写在 Pairs 里。
/// </summary>
public sealed class KinCell
{
	public List<string> People { get; } = new List<string>();

	public List<PairKind> Pairs { get; } = new List<PairKind>();

	public List<KinBelow> Below { get; } = new List<KinBelow>();

	public int Depth { get; internal set; }

	/// <summary>这一格第一个人的横坐标，一个人宽算 1。</summary>
	public float Left { get; internal set; }

	public float XOf(string person) => Left + People.FindIndex(p => Kinship.Same(p, person));

	public float Center => Left + (People.Count - 1) / 2f;

	public IEnumerable<KinCell> SelfAndBelow() => new[] { this }.Concat(Below.SelectMany(b => b.Cell.SelfAndBelow()));
}

public sealed record KinMood(string A, string B, Bond Bond);

/// <summary>一家人：一棵树。</summary>
public sealed class KinTree
{
	public KinCell Root { get; init; } = new KinCell();

	public bool IsFarmers { get; init; }

	public IReadOnlyList<KinSide> Sides { get; init; } = Array.Empty<KinSide>();

	public IReadOnlyList<KinLink> Links { get; init; } = Array.Empty<KinLink>();

	public IReadOnlyList<KinMood> Moods { get; init; } = Array.Empty<KinMood>();

	public IEnumerable<string> Everyone => Root.SelfAndBelow().SelectMany(c => c.People);

	public int Depth => Root.SelfAndBelow().Max(c => c.Depth) + 1;

	public float Width => Root.SelfAndBelow().Max(c => c.Left + c.People.Count);
}

/// <summary>农夫自己的家。不是村民的人（孩子、联机配偶）用 <see cref="Kinship.Outsider"/> 标记。</summary>
public sealed class FarmerHome
{
	public string? Spouse { get; init; }

	public IReadOnlyList<string> OtherSpouses { get; init; } = Array.Empty<string>();

	public string? Roommate { get; init; }

	public string? Fiance { get; init; }

	public IReadOnlyList<string> Sweethearts { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Children { get; init; } = Array.Empty<string>();
}

public sealed class KinSources
{
	public IReadOnlyCollection<string> Residents { get; init; } = Array.Empty<string>();

	public IReadOnlyCollection<string> Elsewhere { get; init; } = Array.Empty<string>();

	/// <summary>角色数据里的亲属：谁、亲属是谁、怎么称呼。</summary>
	public IReadOnlyList<(string Speaker, string Other, string Word)> Declared { get; init; } = Array.Empty<(string, string, string)>();

	public IReadOnlyList<KinMood> Moods { get; init; } = Array.Empty<KinMood>();

	public FarmerHome? Farmer { get; init; }
}

public sealed class KinRegister
{
	public IReadOnlyList<KinTree> Trees { get; init; } = Array.Empty<KinTree>();

	/// <summary>在镇上，但亲戚都不在镇上的人。</summary>
	public IReadOnlyList<string> Solo { get; init; } = Array.Empty<string>();

	public IReadOnlyCollection<string> Elsewhere { get; init; } = Array.Empty<string>();
}

/// <summary>家谱：把称呼变成一棵棵树。排版时先从下往上算每棵子树多宽，再从上往下放。</summary>
public static class Kinship
{
	public const string Me = "@me";

	private const string OutsiderMark = "@:";

	private static readonly string[][] TownCouples =
	{
		new[] { "George", "Evelyn" }, new[] { "Kent", "Jodi" }, new[] { "Pierre", "Caroline" }, new[] { "Robin", "Demetrius" }
	};

	private static readonly string[][] TownSiblings = { new[] { "Emily", "Haley" } };

	/// <summary>按晚辈的名字排。亲兄弟姐妹不用单独写，同一位家长下面的就是。</summary>
	private static readonly (string Younger, ElderRole Role, string[] Elders)[] TownElders =
	{
		("Abigail", ElderRole.Parent, new[] { "Pierre", "Caroline" }),
		("Alex", ElderRole.Grandparent, new[] { "George", "Evelyn" }),
		("Jas", ElderRole.AuntOrUncle, new[] { "Marnie" }),
		("Jas", ElderRole.Godparent, new[] { "Shane" }),
		("Maru", ElderRole.Parent, new[] { "Robin", "Demetrius" }),
		("Penny", ElderRole.Parent, new[] { "Pam" }),
		("Sam", ElderRole.Parent, new[] { "Kent", "Jodi" }),
		("Sebastian", ElderRole.Parent, new[] { "Robin" }),
		("Shane", ElderRole.AuntOrUncle, new[] { "Marnie" }),
		("Vincent", ElderRole.Parent, new[] { "Kent", "Jodi" })
	};

	private static readonly Regex Token = new Regex("[a-z]+|[\\u4e00-\\u9fff]+", RegexOptions.CultureInvariant);

	private static readonly Regex LocalizedKey = new Regex(":([^:\\]]+)\\]\\s*$", RegexOptions.CultureInvariant);

	/// <summary>foster、adoptive 这些前缀不改变辈分，去掉后再查词表。</summary>
	private static readonly Regex KindPrefix = new Regex("^(?:adopt(?:ive|ed)|foster|half)(?=[a-z])", RegexOptions.CultureInvariant);

	/// <summary>出现这些词，后面的亲属词就不是在说这个人本身（前妻、姐夫、朋友的妹妹）。</summary>
	private static readonly HashSet<string> NotKin = new HashSet<string> { "ex", "former", "law", "friend", "friends" };

	/// <summary>词 → 说的是谁。Elder：对方是长辈；Younger：对方是晚辈。</summary>
	private static readonly Dictionary<string, (bool? ElderIsRelative, PairKind? Pair, ElderRole? Role)> Vocabulary = BuildVocabulary();

	private static Dictionary<string, (bool?, PairKind?, ElderRole?)> BuildVocabulary()
	{
		Dictionary<string, (bool?, PairKind?, ElderRole?)> map = new Dictionary<string, (bool?, PairKind?, ElderRole?)>();
		void Pair(PairKind kind, string words)
		{
			foreach (string word in words.Split(' '))
			{
				map[word] = (null, kind, null);
			}
		}
		void Family(ElderRole role, bool relativeIsElder, string words)
		{
			foreach (string word in words.Split(' '))
			{
				map[word] = (relativeIsElder, null, role);
			}
		}
		Pair(PairKind.Married, "wife husband spouse 妻子 丈夫");
		Pair(PairKind.Engaged, "fiancee fiance 未婚妻 未婚夫");
		Pair(PairKind.Dating, "girlfriend boyfriend partner 女朋友 男朋友");
		Pair(PairKind.Siblings, "sister brother sibling twin stepsister stepbrother 姐姐 妹妹 哥哥 弟弟");
		Pair(PairKind.Roommates, "roommate housemate 室友");
		Family(ElderRole.Parent, true, "mom mum mama mother dad papa father parent 妈妈 母亲 爸爸 父亲");
		Family(ElderRole.Parent, false, "daughter son child kid 女儿 儿子 孩子");
		Family(ElderRole.Grandparent, true, "grandmother grandma grandfather grandpa grandparent 奶奶 外婆 爷爷 外公");
		Family(ElderRole.Grandparent, false, "granddaughter grandson grandchild 孙女 孙子");
		Family(ElderRole.AuntOrUncle, true, "aunt auntie uncle 姑姑 阿姨 叔叔 舅舅");
		Family(ElderRole.AuntOrUncle, false, "niece nephew 侄女 侄子 外甥女 外甥");
		Family(ElderRole.Godparent, true, "godmother godfather godparent 教母 教父");
		Family(ElderRole.Godparent, false, "goddaughter godson godchild");
		Family(ElderRole.Stepparent, true, "stepmom stepmum stepmother stepdad stepfather stepparent 继母 继父");
		Family(ElderRole.Stepparent, false, "stepdaughter stepson stepchild 继女 继子");
		return map;
	}

	public static string Outsider(string name) => OutsiderMark + name;

	public static bool IsOutsider(string person) => person.StartsWith(OutsiderMark, StringComparison.Ordinal);

	public static string OutsiderName(string person) => IsOutsider(person) ? person.Substring(OutsiderMark.Length) : person;

	public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>认一个称呼：other 是 speaker 的什么人。认不出返回 null。</summary>
	public static KinClaim? ReadTitle(string speaker, string other, string? word)
	{
		string text = (word ?? "").ToLowerInvariant();
		Match localized = LocalizedKey.Match(text);
		if (localized.Success)
		{
			text = localized.Groups[1].Value;
		}
		string[] tokens = Token.Matches(text).Select(m => m.Value).ToArray();
		if (Same(speaker, other) || tokens.Any(NotKin.Contains))
		{
			return null;
		}
		// 英文的中心词在最后；"grand mother" 这样拆开写的，先和前一个词拼起来查。
		for (int i = tokens.Length - 1; i >= 0; i--)
		{
			string[] tries = i > 0 ? new[] { tokens[i - 1] + tokens[i], tokens[i] } : new[] { tokens[i] };
			foreach (string candidate in tries)
			{
				if (Vocabulary.TryGetValue(KindPrefix.Replace(candidate, ""), out var meaning))
				{
					return meaning.Pair != null ? new KinClaim(speaker, other, meaning.Pair, null)
						: meaning.ElderIsRelative == true ? new KinClaim(other, speaker, null, meaning.Role)
						: new KinClaim(speaker, other, null, meaning.Role);
				}
			}
		}
		return null;
	}

	/// <summary>同一件事可能内置表和角色数据各说一遍。一对只留最亲的那种关系。</summary>
	private static List<KinClaim> Unique(IEnumerable<KinClaim> claims)
	{
		string Key(KinClaim c) => string.Compare(c.A, c.B, StringComparison.OrdinalIgnoreCase) < 0 ? (c.A + "|" + c.B).ToLowerInvariant() : (c.B + "|" + c.A).ToLowerInvariant();
		List<KinClaim> all = claims.ToList();
		IEnumerable<KinClaim> pairs = all.Where(c => c.Pair != null).OrderBy(c => c.Pair).GroupBy(Key).Select(g => g.First());
		IEnumerable<KinClaim> elders = all.Where(c => c.Role != null).GroupBy(c => (c.A.ToLowerInvariant(), c.B.ToLowerInvariant(), c.Role)).Select(g => g.First());
		return pairs.Concat(elders).ToList();
	}

	public static KinRegister Build(KinSources sources)
	{
		HashSet<string> town = new HashSet<string>(sources.Residents, StringComparer.OrdinalIgnoreCase);
		List<KinClaim> claims = Unique(TownClaims().Concat(sources.Declared.Select(d => ReadTitle(d.Speaker, d.Other, d.Word)).OfType<KinClaim>())
			.Where(c => town.Contains(c.A) && town.Contains(c.B)));
		Dictionary<string, KinCell> cellOf = MakeCells(town, claims);
		Dictionary<KinCell, List<KinSide>> sides = new Dictionary<KinCell, List<KinSide>>();
		HashSet<KinCell> hung = Hang(cellOf, claims, sides);
		List<KinLink> links = Unplaced(claims, cellOf);
		bool Related(KinCell c) => c.People.Count > 1 || c.Below.Count > 0 || claims.Any(k => Same(k.A, c.People[0]) || Same(k.B, c.People[0]));
		List<KinTree> trees = cellOf.Values.Distinct().Where(c => !hung.Contains(c) && Related(c)).OrderBy(c => c.People[0], StringComparer.OrdinalIgnoreCase)
			.Select(root => FinishTree(root, false, sides.SelectMany(s => s.Value), links, sources.Moods)).ToList();
		HashSet<string> elsewhere = new HashSet<string>(sources.Elsewhere, StringComparer.OrdinalIgnoreCase);
		HashSet<string> placed = new HashSet<string>(trees.SelectMany(tree => tree.Everyone), StringComparer.OrdinalIgnoreCase);
		IOrderedEnumerable<string> solo = town.Except(placed, StringComparer.OrdinalIgnoreCase).Except(elsewhere, StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
		if (sources.Farmer != null)
		{
			trees.Insert(0, FinishTree(FarmerCell(sources.Farmer), true, Array.Empty<KinSide>(), Array.Empty<KinLink>(), Array.Empty<KinMood>()));
		}
		return new KinRegister { Trees = trees, Solo = solo.ToList(), Elsewhere = elsewhere };
	}

	private static IEnumerable<KinClaim> TownClaims()
	{
		foreach (string[] couple in TownCouples)
		{
			yield return new KinClaim(couple[0], couple[1], PairKind.Married, null);
		}
		foreach (string[] pair in TownSiblings)
		{
			yield return new KinClaim(pair[0], pair[1], PairKind.Siblings, null);
		}
		foreach ((string younger, ElderRole role, string[] elders) in TownElders)
		{
			foreach (string elder in elders)
			{
				yield return new KinClaim(elder, younger, null, role);
			}
		}
	}

	/// <summary>
	/// 一对尽量站一格。只有结婚能把两边的长辈连到一起；两边都有长辈的情侣和兄弟姐妹各归各家，关系另记一条。
	/// </summary>
	private static Dictionary<string, KinCell> MakeCells(IEnumerable<string> town, List<KinClaim> claims)
	{
		Dictionary<string, KinCell> cellOf = new Dictionary<string, KinCell>(StringComparer.OrdinalIgnoreCase);
		bool HasElder(string person) => claims.Any(c => c.Role != null && Same(c.B, person));
		foreach (KinClaim claim in claims.Where(c => c.Pair != null).OrderBy(c => c.Pair))
		{
			PairKind kind = claim.Pair!.Value;
			bool freeA = !cellOf.ContainsKey(claim.A), freeB = !cellOf.ContainsKey(claim.B);
			if (kind != PairKind.Married && HasElder(claim.A) && HasElder(claim.B) || !freeA && !freeB)
			{
				continue;
			}
			if (freeA && freeB)
			{
				KinCell cell = new KinCell();
				cell.People.Add(claim.A);
				cell.People.Add(claim.B);
				cell.Pairs.Add(kind);
				cellOf[claim.A] = cellOf[claim.B] = cell;
				continue;
			}
			string held = freeA ? claim.B : claim.A, joining = freeA ? claim.A : claim.B;
			KinCell holder = cellOf[held];
			if (kind == PairKind.Siblings ? holder.Pairs.All(p => p == PairKind.Siblings) : Same(holder.People[0], held) && holder.Pairs.All(p => p != PairKind.Siblings))
			{
				holder.People.Add(joining);
				holder.Pairs.Add(kind);
				cellOf[joining] = holder;
			}
		}
		foreach (string person in town.Where(p => !cellOf.ContainsKey(p)))
		{
			KinCell single = new KinCell();
			single.People.Add(person);
			cellOf[person] = single;
		}
		return cellOf;
	}

	/// <summary>
	/// 每一格挂到一位长辈下面。好几位长辈时挂到辈分最低的那位（玛妮带大谢恩、谢恩是贾斯的教父，贾斯就在谢恩下面），其余的记成旁支。
	/// </summary>
	private static HashSet<KinCell> Hang(Dictionary<string, KinCell> cellOf, List<KinClaim> claims, Dictionary<KinCell, List<KinSide>> sides)
	{
		Dictionary<string, int> memo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		int Chain(string person, int guard)
		{
			if (memo.TryGetValue(person, out int known))
			{
				return known;
			}
			if (guard > cellOf.Count)
			{
				return 0;
			}
			int longest = claims.Where(c => c.Role != null && Same(c.B, person)).Select(c => Chain(c.A, guard + 1) + 1).DefaultIfEmpty(0).Max();
			return memo[person] = longest;
		}
		Dictionary<KinCell, KinCell> up = new Dictionary<KinCell, KinCell>();
		bool WouldLoop(KinCell above, KinCell below)
		{
			for (KinCell? walk = above; walk != null; walk = up.GetValueOrDefault(walk))
			{
				if (walk == below)
				{
					return true;
				}
			}
			return false;
		}
		foreach (KinCell cell in cellOf.Values.Distinct().OrderBy(c => c.People[0], StringComparer.OrdinalIgnoreCase))
		{
			List<KinSide> notes = new List<KinSide>();
			foreach (string person in cell.People)
			{
				List<KinClaim> mine = claims.Where(c => c.Role != null && Same(c.B, person)).ToList();
				if (mine.Count == 0)
				{
					continue;
				}
				KinClaim best = mine.OrderByDescending(c => Chain(c.A, 0)).ThenBy(c => c.Role).First();
				KinCell above = cellOf[best.A];
				if (!up.ContainsKey(cell) && above != cell && !WouldLoop(above, cell))
				{
					List<string> elders = mine.Where(c => c.Role == best.Role && cellOf[c.A] == above).Select(c => c.A).ToList();
					above.Below.Add(new KinBelow(cell, person, elders, best.Role!.Value));
					up[cell] = above;
					mine = mine.Where(c => !elders.Contains(c.A, StringComparer.OrdinalIgnoreCase)).ToList();
				}
				notes.AddRange(mine.Select(c => new KinSide(c.A, c.B, c.Role!.Value)));
			}
			sides[cell] = notes;
		}
		foreach (KinCell cell in up.Values.Distinct())
		{
			cell.Below.Sort((x, y) => string.Compare(x.Person, y.Person, StringComparison.OrdinalIgnoreCase));
		}
		return new HashSet<KinCell>(up.Keys);
	}

	/// <summary>站不进同一格、也不能从共同的家长看出来的那些关系。</summary>
	private static List<KinLink> Unplaced(List<KinClaim> claims, Dictionary<string, KinCell> cellOf)
	{
		bool Siblings(string a, string b) => claims.Any(x => x.Role == ElderRole.Parent && Same(x.B, a) && claims.Any(y => y.Role == ElderRole.Parent && Same(y.B, b) && Same(x.A, y.A)));
		bool Shown(KinClaim c)
		{
			KinCell cell = cellOf[c.A];
			return cell == cellOf[c.B] && (Same(cell.People[0], c.A) || Same(cell.People[0], c.B) || c.Pair == PairKind.Siblings);
		}
		return claims.Where(c => c.Pair != null && !Shown(c) && !(c.Pair == PairKind.Siblings && Siblings(c.A, c.B))).Select(c => new KinLink(c.A, c.B, c.Pair!.Value)).ToList();
	}

	private static KinCell FarmerCell(FarmerHome home)
	{
		KinCell cell = new KinCell();
		cell.People.Add(Me);
		void Join(string? person, PairKind kind)
		{
			if (person != null)
			{
				cell.People.Add(person);
				cell.Pairs.Add(kind);
			}
		}
		Join(home.Spouse, PairKind.Married);
		foreach (string other in home.OtherSpouses)
		{
			Join(other, PairKind.Married);
		}
		Join(home.Roommate, PairKind.Roommates);
		Join(home.Fiance, PairKind.Engaged);
		foreach (string sweetheart in home.Sweethearts)
		{
			Join(sweetheart, PairKind.Dating);
		}
		string[] parents = home.Spouse == null ? new[] { Me } : new[] { Me, home.Spouse };
		foreach (string child in home.Children)
		{
			KinCell kid = new KinCell();
			kid.People.Add(child);
			cell.Below.Add(new KinBelow(kid, child, parents, ElderRole.Parent));
		}
		return cell;
	}

	private static KinTree FinishTree(KinCell root, bool isFarmers, IEnumerable<KinSide> allSides, IEnumerable<KinLink> allLinks, IEnumerable<KinMood> moods)
	{
		Place(root, 0, 0);
		HashSet<string> people = new HashSet<string>(root.SelfAndBelow().SelectMany(c => c.People), StringComparer.OrdinalIgnoreCase);
		return new KinTree
		{
			Root = root,
			IsFarmers = isFarmers,
			Sides = allSides.Where(s => people.Contains(s.Elder) || people.Contains(s.Younger)).ToList(),
			Links = allLinks.Where(l => people.Contains(l.A) || people.Contains(l.B)).ToList(),
			Moods = moods.Where(m => people.Contains(m.A) && people.Contains(m.B) && m.Bond != Bond.Strangers).ToList()
		};
	}

	private static float Span(KinCell cell) => Math.Max(cell.People.Count, cell.Below.Sum(b => Span(b.Cell)));

	/// <summary>子树从左到右排开，这一格放在它们的正中间。</summary>
	private static void Place(KinCell cell, int depth, float left)
	{
		float span = Span(cell);
		cell.Depth = depth;
		cell.Left = left + (span - cell.People.Count) / 2f;
		float next = left + (span - cell.Below.Sum(b => Span(b.Cell))) / 2f;
		foreach (KinBelow below in cell.Below)
		{
			Place(below.Cell, depth + 1, next);
			next += Span(below.Cell);
		}
	}
}
