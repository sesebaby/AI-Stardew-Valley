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
	Siblings
}

public enum ElderRole
{
	Parent,
	Grandparent,
	AuntOrUncle,
	Godparent
}

/// <summary>认出来的一句亲属称呼：要么 A、B 是一对（Pair），要么 A 是 B 的长辈（Role）。</summary>
public sealed record KinClaim(string A, string B, PairKind? Pair, ElderRole? Role);

/// <summary>晚辈挂在长辈这一格下面：Person 在 Cell 里，长辈是上一格里的 Elders。</summary>
public sealed record KinBelow(KinCell Cell, string Person, IReadOnlyList<string> Elders, ElderRole Role);

/// <summary>树之外还要交代的长辈关系，比如既是姑姑又不是直接带大的。</summary>
public sealed record KinSide(string Elder, string Younger, ElderRole Role);

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

	public IReadOnlyList<KinMood> Moods { get; init; } = Array.Empty<KinMood>();

	public IEnumerable<string> Everyone => Root.SelfAndBelow().SelectMany(c => c.People);

	public int Depth => Root.SelfAndBelow().Max(c => c.Depth) + 1;

	public float Width => Root.SelfAndBelow().Max(c => c.Left + c.People.Count);
}

/// <summary>农夫自己的家。不是村民的人（孩子、联机配偶）用 <see cref="Kinship.Outsider"/> 标记。</summary>
public sealed class FarmerHome
{
	public string? Spouse { get; init; }

	public string? Fiance { get; init; }

	public IReadOnlyList<string> Sweethearts { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> Children { get; init; } = Array.Empty<string>();
}

public sealed class KinSources
{
	public IReadOnlyCollection<string> Villagers { get; init; } = Array.Empty<string>();

	public IReadOnlyCollection<string> Away { get; init; } = Array.Empty<string>();

	/// <summary>角色数据里的亲属：谁、亲属是谁、怎么称呼。</summary>
	public IReadOnlyList<(string Owner, string Relative, string Title)> Declared { get; init; } = Array.Empty<(string, string, string)>();

	public IReadOnlyList<KinMood> Moods { get; init; } = Array.Empty<KinMood>();

	public FarmerHome? Farmer { get; init; }
}

public sealed class KinRegister
{
	public IReadOnlyList<KinTree> Trees { get; init; } = Array.Empty<KinTree>();

	/// <summary>在镇上，但亲戚都不在镇上的人。</summary>
	public IReadOnlyList<string> Alone { get; init; } = Array.Empty<string>();

	public IReadOnlyCollection<string> Away { get; init; } = Array.Empty<string>();
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

	private static readonly Regex NotByBlood = new Regex("^(?:step|half|foster|adopt(?:ive|ed))(?=[a-z])", RegexOptions.CultureInvariant);

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
		Pair(PairKind.Siblings, "sister brother sibling twin 姐姐 妹妹 哥哥 弟弟");
		Family(ElderRole.Parent, true, "mom mum mama mother dad papa father parent 妈妈 母亲 爸爸 父亲");
		Family(ElderRole.Parent, false, "daughter son child kid 女儿 儿子 孩子");
		Family(ElderRole.Grandparent, true, "grandmother grandma grandfather grandpa grandparent 奶奶 外婆 爷爷 外公");
		Family(ElderRole.Grandparent, false, "granddaughter grandson grandchild 孙女 孙子");
		Family(ElderRole.AuntOrUncle, true, "aunt auntie uncle 姑姑 阿姨 叔叔 舅舅");
		Family(ElderRole.AuntOrUncle, false, "niece nephew 侄女 侄子 外甥女 外甥");
		Family(ElderRole.Godparent, true, "godmother godfather godparent 教母 教父");
		Family(ElderRole.Godparent, false, "goddaughter godson godchild");
		return map;
	}

	public static string Outsider(string name) => OutsiderMark + name;

	public static bool IsOutsider(string person) => person.StartsWith(OutsiderMark, StringComparison.Ordinal);

	public static string OutsiderName(string person) => IsOutsider(person) ? person.Substring(OutsiderMark.Length) : person;

	public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	/// <summary>认一个称呼：relative 是 owner 的什么人。认不出返回 null。</summary>
	public static KinClaim? ReadTitle(string owner, string relative, string? title)
	{
		foreach (Match token in Token.Matches((title ?? "").ToLowerInvariant()))
		{
			if (Vocabulary.TryGetValue(NotByBlood.Replace(token.Value, ""), out var meaning))
			{
				return Same(owner, relative) ? null
					: meaning.Pair != null ? new KinClaim(owner, relative, meaning.Pair, null)
					: meaning.ElderIsRelative == true ? new KinClaim(relative, owner, null, meaning.Role)
					: new KinClaim(owner, relative, null, meaning.Role);
			}
		}
		return null;
	}

	public static KinRegister Build(KinSources sources)
	{
		HashSet<string> town = new HashSet<string>(sources.Villagers, StringComparer.OrdinalIgnoreCase);
		List<KinClaim> claims = TownClaims().Concat(sources.Declared.Select(d => ReadTitle(d.Owner, d.Relative, d.Title)).OfType<KinClaim>())
			.Where(c => town.Contains(c.A) && town.Contains(c.B)).ToList();
		Dictionary<string, KinCell> cellOf = MakeCells(town, claims);
		Dictionary<KinCell, List<KinSide>> sides = new Dictionary<KinCell, List<KinSide>>();
		HashSet<KinCell> hung = Hang(cellOf, claims, sides);
		List<KinTree> trees = new List<KinTree>();
		foreach (KinCell root in cellOf.Values.Distinct().Where(c => !hung.Contains(c) && (c.People.Count > 1 || c.Below.Count > 0)).OrderBy(c => c.People[0], StringComparer.OrdinalIgnoreCase))
		{
			trees.Add(FinishTree(root, false, sides.SelectMany(s => s.Value), sources.Moods));
		}
		HashSet<string> away = new HashSet<string>(sources.Away, StringComparer.OrdinalIgnoreCase);
		List<string> alone = town.Where(n => !away.Contains(n) && !trees.Any(t => t.Everyone.Contains(n, StringComparer.OrdinalIgnoreCase))).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		if (sources.Farmer != null)
		{
			trees.Insert(0, FinishTree(FarmerCell(sources.Farmer), true, Array.Empty<KinSide>(), Array.Empty<KinMood>()));
		}
		return new KinRegister { Trees = trees, Alone = alone, Away = away };
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
	/// 夫妻总在一格。订婚、交往和没有长辈的兄弟姐妹，只有都没长辈时才放一格，免得把两家连到一起。
	/// </summary>
	private static Dictionary<string, KinCell> MakeCells(IEnumerable<string> town, List<KinClaim> claims)
	{
		Dictionary<string, KinCell> cellOf = new Dictionary<string, KinCell>(StringComparer.OrdinalIgnoreCase);
		bool HasElder(string person) => claims.Any(c => c.Role != null && Same(c.B, person));
		foreach (KinClaim claim in claims.Where(c => c.Pair != null).OrderBy(c => c.Pair))
		{
			if (claim.Pair != PairKind.Married && (HasElder(claim.A) || HasElder(claim.B)))
			{
				continue;
			}
			if (cellOf.TryGetValue(claim.A, out KinCell? ofA) && !cellOf.ContainsKey(claim.B) && claim.Pair == PairKind.Siblings && ofA.Pairs.All(p => p == PairKind.Siblings))
			{
				ofA.People.Add(claim.B);
				ofA.Pairs.Add(PairKind.Siblings);
				cellOf[claim.B] = ofA;
			}
			else if (ofA == null && !cellOf.ContainsKey(claim.B))
			{
				KinCell cell = new KinCell();
				cell.People.Add(claim.A);
				cell.People.Add(claim.B);
				cell.Pairs.Add(claim.Pair!.Value);
				cellOf[claim.A] = cellOf[claim.B] = cell;
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
				KinClaim best = mine.OrderByDescending(c => Chain(c.A, 0)).First();
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

	private static KinTree FinishTree(KinCell root, bool isFarmers, IEnumerable<KinSide> allSides, IEnumerable<KinMood> moods)
	{
		Place(root, 0, 0);
		HashSet<string> people = new HashSet<string>(root.SelfAndBelow().SelectMany(c => c.People), StringComparer.OrdinalIgnoreCase);
		return new KinTree
		{
			Root = root,
			IsFarmers = isFarmers,
			Sides = allSides.Where(s => people.Contains(s.Elder) && people.Contains(s.Younger)).ToList(),
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
