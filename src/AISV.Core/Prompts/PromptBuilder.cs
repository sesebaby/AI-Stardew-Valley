using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AISV.Core.Prompts;

/// <summary>
/// 按段拼提示词：每段有优先级，超出总字数时先丢优先级低的段（同优先级先丢后加的），保留的段按加入顺序输出。
/// 必需的段（优先级 Required）永远保留。
/// </summary>
public sealed class PromptBuilder
{
	public const int Required = int.MaxValue;

	private readonly List<(string Text, int Priority, int Order)> _sections = new List<(string, int, int)>();

	public PromptBuilder Add(string? text, int priority = Required)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			_sections.Add((text.Trim(), priority, _sections.Count));
		}
		return this;
	}

	public PromptBuilder AddIf(bool condition, Func<string?> text, int priority = Required) => condition ? Add(text(), priority) : this;

	/// <summary>拼起来；maxChars ≤ 0 表示不限。dropped 是丢掉的段数。</summary>
	public string Build(int maxChars, out int dropped)
	{
		List<(string Text, int Priority, int Order)> kept = _sections.ToList();
		dropped = 0;
		if (maxChars > 0)
		{
			foreach ((string Text, int Priority, int Order) s in _sections.Where(s => s.Priority != Required).OrderBy(s => s.Priority).ThenByDescending(s => s.Order))
			{
				if (Length(kept) <= maxChars)
				{
					break;
				}
				kept.Remove(s);
				dropped++;
			}
		}
		return string.Join("\n\n", kept.OrderBy(s => s.Order).Select(s => s.Text));
	}

	public string Build() => Build(0, out _);

	private static int Length(List<(string Text, int Priority, int Order)> sections) => sections.Sum(s => s.Text.Length) + Math.Max(0, sections.Count - 1) * 2;
}
