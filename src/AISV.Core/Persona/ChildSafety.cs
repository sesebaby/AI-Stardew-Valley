using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AISV.Core.Persona;

/// <summary>
/// 谁算孩子：名单里的（贾斯、文森特、利奥、摩根 + 设置里补充的）、游戏年龄是 Child 的、人设里写明是小孩的。
/// 游戏里阿比盖尔、玛鲁、潘妮、山姆、塞巴斯蒂安是 Teen 且可恋爱，所以 Teen 只要可约会就按成年人对待。
/// </summary>
public static class ChildSafety
{
	public const int AgeAdult = 0;

	public const int AgeTeen = 1;

	public const int AgeChild = 2;

	private static readonly string[] BuiltIn = { "Jas", "Vincent", "Leo", "Morgan" };

	private static readonly Regex ChildCodedText = new Regex("\\b(child|kid|young\\s+girl|young\\s+boy|little\\s+(?:boy|girl|brother|sister))\\b|还是个孩子|是个小孩", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static HashSet<string> _listed = new HashSet<string>(BuiltIn, StringComparer.OrdinalIgnoreCase);

	/// <summary>设置里的补充名单（逗号分隔，中英文逗号、分号都行）。</summary>
	public static void Configure(string? extraCsv)
	{
		HashSet<string> set = new HashSet<string>(BuiltIn, StringComparer.OrdinalIgnoreCase);
		foreach (string part in (extraCsv ?? "").Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			set.Add(part);
		}
		_listed = set;
	}

	private static HashSet<string> _family = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 农夫自己家的孩子（游戏里 Child 的 Age 是成长阶段 0～3，和村民的年龄档不是一回事：会走路的孩子 Age 是 3，
	/// 刚出生的是 0，按年龄判断会把他们当成大人）。游戏层每天按名字登记一遍，和村民重名的不登记。
	/// </summary>
	public static void SetFamily(IEnumerable<string> names)
	{
		HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string n in names)
		{
			if (!string.IsNullOrWhiteSpace(n))
			{
				set.Add(n.Trim());
			}
		}
		_family = set;
	}

	public static bool IsFamilyChild(string? name) => !string.IsNullOrWhiteSpace(name) && _family.Contains(name.Trim());

	public static bool IsListed(string? name) => !string.IsNullOrWhiteSpace(name) && (_listed.Contains(name.Trim()) || _family.Contains(name.Trim()));

	public static bool IsChild(string? name, int? age) => IsListed(name) || age == AgeChild;

	public static bool IsChildCoded(string? name, int? age, string? personality) => IsChild(name, age) || (!string.IsNullOrWhiteSpace(personality) && ChildCodedText.IsMatch(personality));

	/// <summary>年龄上允许恋爱内容：成年人，或可约会的 Teen；名单里的孩子永远不行。</summary>
	public static bool AgeAllowsRomance(string? name, int age, bool datable) => !IsListed(name) && (age == AgeAdult || (age == AgeTeen && datable));
}
