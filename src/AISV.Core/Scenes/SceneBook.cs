using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AISV.Core.Dialogue;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;

namespace AISV.Core.Scenes;

/// <summary>演完一个场景要记的：谁记得、记什么、当天互动里怎么写。</summary>
public sealed record SceneMemory(string SceneId, string Text, IReadOnlyList<string> Participants, string Interaction)
{
	public const int MaxChars = 240;

	private static readonly Regex NameSlot = new Regex(@"\{([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

	/// <summary>
	/// 场景要记忆时才有：参与者是说话的人；文字用场景写好的（"{Abigail}"这样的占位换成显示名），没有就用第一句"某某说：「…」"。
	/// <paramref name="display"/> 把内部名字换成显示名。
	/// </summary>
	public static SceneMemory? Build(SceneDefinition d, IReadOnlyList<SceneLine>? played, Func<string, string> display)
	{
		List<string> names = d.Speakers.ToList();
		if (names.Count == 0)
		{
			return null;
		}
		string text = SceneCatalog.Cut(NameSlot.Replace(d.Keepsake ?? "", m => display(m.Groups[1].Value)), MaxChars);
		if (text.Length == 0)
		{
			SceneLine? first = (played ?? Array.Empty<SceneLine>()).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Speaker) && !string.IsNullOrWhiteSpace(l.Text));
			if (first == null)
			{
				return null;
			}
			text = SceneCatalog.Cut($"{display(first.Speaker.Trim())}说：「{first.Text.Replace('\n', ' ').Trim()}」", MaxChars);
		}
		string who = names.Count > 1 ? "农夫看到" + string.Join("、", names.Select(display)) + "之间的一幕" : "农夫看到的一幕";
		string head = d.Name.Length > 0 ? $"[{who}：{d.Name}]" : $"[{who}]";
		return new SceneMemory(d.Id, text, names, $"{head} {text}");
	}

	/// <summary>当天互动里的一条：领头的人，农夫只是看着、没接话。</summary>
	public DailyInteraction ToInteraction(string playerName) => new DailyInteraction
	{
		Npc = Participants[0],
		Farmer = playerName,
		Opener = Interaction,
		Reply = SkippedReply.Marker,
		Comeback = "",
		Face = "neutral"
	};
}

/// <summary>
/// 场景记录（town/scene_log.json，每个存档一份）：今天挑了哪些、演过哪些、各演过几次、冷却到哪天。主线程读写。
/// </summary>
public sealed class SceneBook
{
	public const string Path = "town/scene_log.json";

	private static readonly DocumentType<SceneState> Doc = new DocumentType<SceneState>("场景事件", 1, () => new SceneState());

	private readonly Func<DocumentStore?> _store;

	private SceneState? _tidied;

	public SceneBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>当前存档的记录；没有存档时是一份空的（改了也不存）。</summary>
	public SceneState State
	{
		get
		{
			DocumentStore? store = _store();
			if (store == null)
			{
				return new SceneState();
			}
			SceneState state = store.Get(Path, Doc);
			if (!ReferenceEquals(state, _tidied))
			{
				ScenePlanner.Tidy(state);
				_tidied = state;
			}
			return state;
		}
	}

	public void Changed() => _store()?.MarkDirty(Path);

	public bool PlayedToday(string id) => State.PlayedToday.Contains(id, StringComparer.OrdinalIgnoreCase);

	/// <summary>记一次演完：今天演过、次数 +1、有冷却就记冷却到哪天。</summary>
	public void Complete(SceneDefinition d, int today)
	{
		SceneState s = State;
		if (!s.PlayedToday.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
		{
			s.PlayedToday.Add(d.Id);
		}
		s.TimesPlayed[d.Id] = s.TimesPlayed.GetValueOrDefault(d.Id) + 1;
		if (d.RestDays > 0)
		{
			s.RestingUntil[d.Id] = Math.Max(1, today) + d.RestDays;
		}
		Changed();
	}

	/// <summary>今天强制挑上（测试用），清掉"今天演过"。</summary>
	public void Arm(string id)
	{
		SceneState s = State;
		if (!s.Loaded.Contains(id, StringComparer.OrdinalIgnoreCase))
		{
			s.Loaded.Add(id);
		}
		s.PlayedToday.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
		Changed();
	}

	/// <summary>设置关了：换天时清掉今天的挑选。返回原来挑的。</summary>
	public IReadOnlyList<string> ClearDay(string dayKey)
	{
		SceneState s = State;
		if (string.Equals(s.DayKey, dayKey, StringComparison.OrdinalIgnoreCase) && s.Loaded.Count == 0)
		{
			return Array.Empty<string>();
		}
		List<string> before = s.Loaded.ToList();
		s.DayKey = dayKey;
		s.Loaded.Clear();
		s.PlayedToday.Clear();
		Changed();
		return before;
	}
}
