using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Text;

/// <summary>
/// 游戏字体画不出的标点换成画得出的。各语言的字体字符不一样：葡语没有弯引号和省略号，俄语没有省略号，
/// 德语没有短破折号，韩语没有间隔点，中文没有波浪号和直角引号；画不出的会显示成一个方块。
/// 没装字体检查（单元测试、游戏还没加载字体）时原样返回。
/// </summary>
public static class Glyphs
{
	private static Func<char, bool>? _has;

	/// <summary>备选从前往后试，第一个整串都画得出的就用；最后一个是 ASCII 或空串，总能用上。</summary>
	private static readonly Dictionary<char, string[]> Fallbacks = new Dictionary<char, string[]>
	{
		['–'] = new[] { "—", "-" },
		['—'] = new[] { "―", "-" },
		['―'] = new[] { "—", "-" },
		['“'] = new[] { "\"" },
		['”'] = new[] { "\"" },
		['„'] = new[] { "“", "\"" },
		['«'] = new[] { "\"" },
		['»'] = new[] { "\"" },
		['‘'] = new[] { "'" },
		['’'] = new[] { "'" },
		['…'] = new[] { "..." },
		['‹'] = new[] { "<" },
		['›'] = new[] { ">" },
		['×'] = new[] { "x" },
		['·'] = new[] { "・", "/" },
		['《'] = new[] { "「", "<" },
		['》'] = new[] { "」", ">" },
		['～'] = new[] { "~" },
		['「'] = new[] { "“", "\"" },
		['」'] = new[] { "”", "\"" },
		['『'] = new[] { "「", "“", "\"" },
		['』'] = new[] { "」", "”", "\"" },
		['★'] = new[] { "*" },
		['☆'] = new[] { "*" },
		['♥'] = new[] { "" },
		['♪'] = new[] { "" },
		// 界面代码里拼的中文标点，到了拉丁字母的字体里换成半角。
		['：'] = new[] { ": " },
		['，'] = new[] { ", " },
		['、'] = new[] { ", " },
		['；'] = new[] { "; " },
		['。'] = new[] { ". " },
		['！'] = new[] { "! " },
		['？'] = new[] { "? " },
		['（'] = new[] { " (" },
		['）'] = new[] { ") " },
		['　'] = new[] { " " }
	};

	public static void Use(Func<char, bool>? has) => _has = has;

	public static string Fix(string text) => _has is Func<char, bool> has ? Fix(text, has) : text;

	public static string Fix(string text, Func<char, bool> has)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}
		StringBuilder? sb = null;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c < 0x80 || !Fallbacks.TryGetValue(c, out string[]? alts) || has(c))
			{
				sb?.Append(c);
				continue;
			}
			sb ??= new StringBuilder(text, 0, i, text.Length + 8);
			sb.Append(alts.FirstOrDefault(a => a.All(x => x < 0x80 || has(x))) ?? alts[^1]);
		}
		return sb?.ToString() ?? text;
	}
}
