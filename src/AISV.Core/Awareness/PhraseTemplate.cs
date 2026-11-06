using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AISV.Core.Awareness;

/// <summary>
/// 短语模板：<c>{名字}</c> 取值；<c>{名字?单数|复数}</c> 按数值选，<c>#</c> 换成数；
/// <c>[内容]</c> 里有取不到的值时整段不出现，<c>[内容|备选]</c> 取不到时用备选；<c>[[ ]] {{ }}</c> 转义。
/// </summary>
public sealed class PhraseTemplate
{
	private abstract record Node;

	private sealed record Text(string Value) : Node;

	private sealed record Group(List<Node> Body, List<Node>? Fallback) : Node;

	private sealed record Token(string Name, string? One, string? Many) : Node;

	private readonly List<Node> _nodes;

	private PhraseTemplate(List<Node> nodes, string source)
	{
		_nodes = nodes;
		Source = source;
	}

	public string Source { get; }

	public static readonly PhraseTemplate Empty = Parse("");

	public static PhraseTemplate Parse(string? text)
	{
		int i = 0;
		return new PhraseTemplate(Sequence(text ?? "", ref i, false), text ?? "");
	}

	public string Render(Func<string, string?> resolve)
	{
		StringBuilder sb = new StringBuilder();
		Write(_nodes, resolve, sb);
		return Tidy(sb.ToString());
	}

	public override string ToString() => Source;

	private static List<Node> Sequence(string s, ref int i, bool inGroup)
	{
		List<Node> nodes = new List<Node>();
		StringBuilder lit = new StringBuilder();
		void Flush()
		{
			if (lit.Length > 0)
			{
				nodes.Add(new Text(lit.ToString()));
				lit.Clear();
			}
		}
		while (i < s.Length)
		{
			char c = s[i];
			if (inGroup && (c == ']' || c == '|'))
			{
				break;
			}
			if ((c == '[' || c == ']' || c == '{' || c == '}') && i + 1 < s.Length && s[i + 1] == c)
			{
				lit.Append(c);
				i += 2;
				continue;
			}
			if (c == '{')
			{
				int end = s.IndexOf('}', i + 1);
				if (end < 0)
				{
					lit.Append(c);
					i++;
					continue;
				}
				Flush();
				nodes.Add(ParseToken(s.Substring(i + 1, end - i - 1)));
				i = end + 1;
			}
			else if (c == '[')
			{
				Flush();
				i++;
				List<Node> body = Sequence(s, ref i, true);
				List<Node>? fallback = null;
				if (i < s.Length && s[i] == '|')
				{
					i++;
					fallback = Sequence(s, ref i, true);
				}
				if (i < s.Length && s[i] == ']')
				{
					i++;
				}
				nodes.Add(new Group(body, fallback));
			}
			else
			{
				lit.Append(c);
				i++;
			}
		}
		Flush();
		return nodes;
	}

	private static Token ParseToken(string inner)
	{
		int q = inner.IndexOf('?');
		if (q >= 0)
		{
			string rest = inner.Substring(q + 1);
			int bar = rest.IndexOf('|');
			if (bar >= 0)
			{
				return new Token(inner.Substring(0, q).Trim(), rest.Substring(0, bar), rest.Substring(bar + 1));
			}
		}
		return new Token(inner.Trim(), null, null);
	}

	/// <summary>返回这一段有没有取不到的值。</summary>
	private static bool Write(List<Node> nodes, Func<string, string?> resolve, StringBuilder sb)
	{
		bool missing = false;
		foreach (Node node in nodes)
		{
			switch (node)
			{
			case Text t:
				sb.Append(t.Value);
				break;
			case Token t:
			{
				string value = resolve(t.Name) ?? "";
				if (t.One == null)
				{
					missing |= value.Length == 0;
					sb.Append(value);
					break;
				}
				int n = int.TryParse(value, out int parsed) ? parsed : 1;
				string pick = n == 1 ? t.One : t.Many ?? t.One;
				missing |= pick.Length == 0;
				sb.Append(pick.Replace("#", n.ToString(CultureInfo.InvariantCulture)));
				break;
			}
			case Group g:
			{
				StringBuilder part = new StringBuilder();
				if (!Write(g.Body, resolve, part))
				{
					sb.Append(part);
				}
				else if (g.Fallback != null)
				{
					StringBuilder alt = new StringBuilder();
					Write(g.Fallback, resolve, alt);
					sb.Append(alt);
				}
				break;
			}
			}
		}
		return missing;
	}

	private static string Tidy(string text)
	{
		StringBuilder sb = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (c == ' ' && sb.Length > 0 && sb[^1] == ' ')
			{
				continue;
			}
			if (IsClosing(c) && sb.Length > 0 && sb[^1] == ' ')
			{
				sb.Length--;
			}
			sb.Append(c);
		}
		return sb.ToString().Trim();
	}

	private static bool IsClosing(char c) => c is '!' or ')' or ',' or '.' or ':' or ';' or '?' or '，' or '。' or '！' or '？' or '：' or '；' or '）' or '、';
}
