using System;
using System.Collections.Generic;

namespace AISV.Core.Text;

/// <summary>
/// 给玩家看的文字。代码里写中文原文当默认值，i18n 文件里有同名键就用文件里的（游戏层在启动时接上 SMAPI 的翻译）。
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
		return text;
	}
}
