using System;
using System.Collections.Generic;

namespace AISV.Core.Text;

/// <summary>
/// 给玩家看的文字。代码里写中文原文，i18n 里 zh.json 是同样的中文，default.json 是英文（SMAPI 的英文环境只读它），其他语言各一个文件。
/// 写法：Tr.Get(键, 中文原文)；带参数时原文里写 {{name}}，再传 ("name", 值)。
/// </summary>
public static class Tr
{
	private static Func<string, string?> _lookup = _ => null;

	public static void Use(Func<string, string?> lookup) => _lookup = lookup;

	public static string Get(string key, string fallback, params (string Name, object? Value)[] tokens)
	{
		string text = _lookup(key) is string s && s.Length > 0 ? s : fallback;
		foreach ((string name, object? value) in tokens)
		{
			text = text.Replace("{{" + name + "}}", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", StringComparison.Ordinal);
		}
		return Glyphs.Fix(text);
	}
}
