using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AISV.Core.Prompts;

namespace AISV.Core.Dialogue;

/// <summary>
/// 防重复：记住每位村民最近 3 句开场白、这场对话里最近 3 句反应和最近 3 个话题类别，下次生成时告诉 AI 别再用。
/// 只在内存里（回标题清空）；反应在新开场时清掉。
/// </summary>
public sealed class RecentLines
{
	private const int Keep = 3;

	// 命中最多的类别算这句话的话题；先看这位村民的招牌话题。
	private static readonly (string Label, string[] Words)[] Topics =
	{
		("天色", W("rainy downpour sunshine thunder snowfall breeze chilly humid forecast 雨 放晴 打雷 飘雪 起风 天冷 闷热 天阴 太阳")),
		("地里的活", W("sprout soil fertilizer watering pasture hay livestock greenhouse 浇水 施肥 锄地 发芽 牧草 牲口 大棚 地里")),
		("亲人", W("parents daughter son uncle aunt relatives hometown household 爹 娘 爸 妈 儿子 女儿 亲戚 老家 兄弟 姐妹")),
		("心动", W("sweetheart crush flirt darling propose bouquet anniversary 心动 喜欢你 暗恋 表白 花束 求婚 对象 想你")),
		("心事", W("stress panic dread uneasy overthinking insomnia 发愁 睡不着 慌 心烦 忐忑 压力 怕")),
		("节庆", W("celebration parade bonfire lantern fair moonlight 集市 灯笼 过节 游行 篝火 月光 舞伴")),
		("闲话", W("whisper apparently neighbors tattle grapevine 嚼舌根 据说 悄悄话 背后说 风声 小道消息"))
	};

	private readonly object _sync = new object();

	private readonly Dictionary<string, List<string>> _openings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<string>> _reactions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<string>> _topics = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	public void RecordOpening(string npc, string line)
	{
		lock (_sync)
		{
			_reactions.Remove(npc);
		}
		Record(_openings, npc, line);
	}

	public void RecordReaction(string npc, string line) => Record(_reactions, npc, line);

	public void Clear()
	{
		lock (_sync)
		{
			_openings.Clear();
			_reactions.Clear();
			_topics.Clear();
		}
	}

	/// <summary>开场白用：最近的开场白（每句截 60 字）+ 最近的话题类别。</summary>
	public string OpeningBlock(string npc)
	{
		lock (_sync)
		{
			StringBuilder sb = new StringBuilder();
			if (_openings.TryGetValue(npc, out List<string>? lines) && lines.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("最近用过的开场白（这次换个新的，别重复也别换汤不换药）：");
				foreach (string l in lines)
				{
					sb.AppendLine("- \"" + Cut(l, 60) + "\"");
				}
			}
			if (_topics.TryGetValue(npc, out List<string>? tags) && tags.Count > 0)
			{
				if (sb.Length > 0)
				{
					sb.AppendLine();
				}
				sb.AppendLine("最近聊过的话题类别：" + string.Join("、", tags) + "。除非眼下正好相关，这次挑个别的、更个人的话题。");
			}
			return sb.ToString();
		}
	}

	/// <summary>回复用：这场对话里最近的反应（每句截 80 字）。</summary>
	public string ReactionBlock(string npc)
	{
		lock (_sync)
		{
			if (!_reactions.TryGetValue(npc, out List<string>? lines) || lines.Count == 0)
			{
				return "";
			}
			StringBuilder sb = new StringBuilder();
			sb.AppendLine();
			sb.AppendLine("这场对话里你刚说过这些，别重复也别换个说法再说一遍：");
			foreach (string l in lines)
			{
				sb.AppendLine("- \"" + Cut(l, 80) + "\"");
			}
			sb.AppendLine("接着话头说点不一样的。");
			return sb.ToString();
		}
	}

	internal string? Classify(string npc, string text)
	{
		IReadOnlyList<string> motif = PersonaPrompt.TopicTags(npc, text);
		if (motif.Count > 0)
		{
			return motif[0];
		}
		string n = Normalize(text);
		string? best = null;
		int bestCount = 0;
		foreach ((string label, string[] words) in Topics)
		{
			int count = words.Count(w => n.Contains(w, StringComparison.Ordinal));
			if (count > bestCount)
			{
				best = label;
				bestCount = count;
			}
		}
		return best;
	}

	private static string[] W(string list) => list.Split(' ');

	private void Record(Dictionary<string, List<string>> map, string npc, string line)
	{
		if (string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(line))
		{
			return;
		}
		lock (_sync)
		{
			Push(map, npc, line, dedupe: false);
			if (Classify(npc, line) is string tag)
			{
				Push(_topics, npc, tag, dedupe: true);
			}
		}
	}

	private static void Push(Dictionary<string, List<string>> map, string npc, string value, bool dedupe)
	{
		if (!map.TryGetValue(npc, out List<string>? list))
		{
			list = map[npc] = new List<string>();
		}
		if (dedupe)
		{
			list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
		}
		list.Add(value);
		while (list.Count > Keep)
		{
			list.RemoveAt(0);
		}
	}

	private static string Cut(string s, int max) => s.Length > max ? s.Substring(0, max) + "…" : s;

	private static string Normalize(string text)
	{
		string d = text.Normalize(NormalizationForm.FormD);
		StringBuilder sb = new StringBuilder(d.Length);
		foreach (char c in d)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.IsControl(c) ? ' ' : char.ToLowerInvariant(c));
			}
		}
		return sb.ToString();
	}
}
