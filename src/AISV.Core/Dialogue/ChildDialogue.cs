using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Core.Prompts;

namespace AISV.Core.Dialogue;

/// <summary>和孩子聊天要的材料（游戏层在主线程取好）。</summary>
public sealed class ChildContext
{
	/// <summary>记录用的键：父母 id + 净化过的名字。</summary>
	public string Key { get; init; } = "";

	public string Name { get; init; } = "";

	public bool Boy { get; init; }

	/// <summary>成长阶段的说法，比如"刚学会走路"。</summary>
	public string Stage { get; init; } = "";

	public string ParentName { get; init; } = "";

	/// <summary>爸爸 / 妈妈。</summary>
	public string ParentRole { get; init; } = "";

	/// <summary>家里的情况，一条一句。</summary>
	public IReadOnlyList<string> Home { get; init; } = Array.Empty<string>();

	/// <summary>此刻的情况（季节、天气、地点、时间）。</summary>
	public string Now { get; init; } = "";

	/// <summary>孩子的角色卡（没有为 null）。</summary>
	public CharacterCard? Card { get; init; }

	public string Language { get; init; } = "中文";

	public int Day { get; init; }
}

public sealed class ChildTurn
{
	public int Day { get; set; }

	/// <summary>child / parent / note（发生过的事）。</summary>
	public string Who { get; set; } = "";

	public string Text { get; set; } = "";
}

/// <summary>每个孩子的聊天记录和发生过的事，跟着存档走（family/children.json）。</summary>
public sealed class ChildBook
{
	public const string Path = "family/children.json";

	public const int MaxTurns = 60;

	public static readonly DocumentType<Dictionary<string, List<ChildTurn>>> Doc = new DocumentType<Dictionary<string, List<ChildTurn>>>("孩子的聊天记录", 1, () => new Dictionary<string, List<ChildTurn>>(StringComparer.OrdinalIgnoreCase));

	private readonly Func<DocumentStore?> _store;

	public ChildBook(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public IReadOnlyList<ChildTurn> Recent(string key, int count)
	{
		List<ChildTurn>? list = Data()?.GetValueOrDefault(key);
		return list == null ? Array.Empty<ChildTurn>() : list.Skip(Math.Max(0, list.Count - count)).ToList();
	}

	public void Add(string key, int day, string who, string text)
	{
		DocumentStore? store = _store();
		Dictionary<string, List<ChildTurn>>? data = Data();
		if (store == null || data == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (!data.TryGetValue(key, out List<ChildTurn>? list))
		{
			data[key] = list = new List<ChildTurn>();
		}
		list.Add(new ChildTurn { Day = day, Who = who, Text = text.Trim() });
		if (list.Count > MaxTurns)
		{
			list.RemoveRange(0, list.Count - MaxTurns);
		}
		store.MarkDirty(Path);
	}

	/// <summary>记一件发生过的事（离家出走被找到、和爸爸妈妈在院子里玩），聊天时孩子会记得。</summary>
	public void Note(string key, int day, string text) => Add(key, day, "note", text);

	/// <summary>不在家里了的孩子（比如被变成了鸽子）：记录一起清掉。</summary>
	public void Forget(IEnumerable<string> keep)
	{
		DocumentStore? store = _store();
		Dictionary<string, List<ChildTurn>>? data = Data();
		if (store == null || data == null)
		{
			return;
		}
		HashSet<string> alive = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
		if (data.Keys.Where(k => !alive.Contains(k)).ToList() is { Count: > 0 } gone)
		{
			foreach (string k in gone)
			{
				data.Remove(k);
			}
			store.MarkDirty(Path);
		}
	}

	private Dictionary<string, List<ChildTurn>>? Data() => _store()?.Get(Path, Doc);
}

/// <summary>孩子的角色卡：跟着存档走（family/child_cards.json），不放全局，免得和同名村民的卡混在一起。</summary>
public sealed class ChildCards
{
	public const string Path = "family/child_cards.json";

	public static readonly DocumentType<Dictionary<string, CharacterCard>> Doc = new DocumentType<Dictionary<string, CharacterCard>>("孩子的角色卡", 1, () => new Dictionary<string, CharacterCard>(StringComparer.OrdinalIgnoreCase));

	private readonly Func<DocumentStore?> _store;

	public ChildCards(Func<DocumentStore?> store)
	{
		_store = store;
	}

	public CharacterCard? Get(string key) => _store()?.Get(Path, Doc).GetValueOrDefault(key);

	public void Save(string key, CharacterCard card)
	{
		DocumentStore? store = _store();
		if (store == null || string.IsNullOrWhiteSpace(key))
		{
			return;
		}
		Dictionary<string, CharacterCard> all = store.Get(Path, Doc);
		if (card.IsEmpty)
		{
			all.Remove(key);
		}
		else
		{
			all[key] = card;
		}
		store.MarkDirty(Path);
	}
}

/// <summary>孩子的提示词：像小孩一样说话；不谈恋爱、成人话题和钱。</summary>
public static class ChildPrompts
{
	public const string OpeningRequest = "child_opening";

	public const string ReplyRequest = "child_reply";

	public const int MaxChars = 40;

	/// <summary>没有角色卡时的默认性格。</summary>
	public static string DefaultPersona(ChildContext c)
	{
		string kid = c.Boy ? "男孩" : "女孩";
		return $"{c.Name}是{c.ParentRole}{c.ParentName}的孩子，{kid}，{c.Stage}。好奇心很重，什么都想摸一摸、问一问；最喜欢{c.ParentRole}陪着玩，高兴了会咯咯笑，困了会揉眼睛。";
	}

	public static string Persona(ChildContext c)
	{
		CharacterCard? card = c.Card;
		StringBuilder sb = new StringBuilder();
		sb.AppendLine(card?.Replaces == true ? card.Personality.Trim() : DefaultPersona(c));
		if (!string.IsNullOrWhiteSpace(card?.Appearance))
		{
			sb.AppendLine("长相：" + card!.Appearance.Trim());
		}
		if (!string.IsNullOrWhiteSpace(card?.Lore))
		{
			sb.AppendLine("经历：" + card!.Lore.Trim());
		}
		if (!string.IsNullOrWhiteSpace(card?.Speech))
		{
			sb.AppendLine("说话的样子：" + card!.Speech.Trim());
		}
		return sb.ToString().Trim();
	}

	public static string Called(ChildContext c) => string.IsNullOrWhiteSpace(c.Card?.DisplayName) ? c.Name : c.Card!.DisplayName.Trim();

	public static string System(ChildContext c, IReadOnlyList<ChildTurn> recent, bool opening)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"这次只用{c.Language}回答。");
		sb.AppendLine();
		sb.AppendLine($"你是星露谷一座农场上的孩子{Called(c)}。{c.ParentRole}（{c.ParentName}）{(opening ? "刚走过来" : "正在和你说话")}。");
		sb.AppendLine();
		sb.AppendLine("# 你是谁");
		sb.AppendLine(Persona(c));
		sb.AppendLine();
		sb.AppendLine("# 家里的情况（只是资料）");
		foreach (string h in c.Home)
		{
			sb.AppendLine("- " + h);
		}
		if (c.Now.Length > 0)
		{
			sb.AppendLine("- 现在：" + c.Now);
		}
		if (recent.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("# 最近（你记得的）");
			foreach (ChildTurn t in recent)
			{
				string who = t.Who switch
				{
					"child" => "你",
					"parent" => c.ParentRole,
					_ => "发生过"
				};
				sb.AppendLine($"- {(t.Day == c.Day ? "今天" : "以前")}·{who}：{t.Text}");
			}
		}
		sb.AppendLine();
		sb.AppendLine("# 规矩（一定要守）");
		sb.AppendLine("- 你是小孩：词很简单，句子短，可以重复、可以说错字、可以奶声奶气；只懂小孩懂的事（玩具、吃的、小动物、天气、爸爸妈妈、睡觉、讲故事）。");
		sb.AppendLine("- 不谈恋爱、不谈成人话题、不谈钱和工作；大人问到这些就像小孩一样岔开，或者说听不懂。");
		sb.AppendLine("- 不要像大人一样讲道理，也不要突然很懂事。");
		sb.AppendLine($"- 只写你说出口的话，{MaxChars} 个字以内，不加引号，不写动作描写。");
		return sb.ToString();
	}

	public static string OpeningTurn() => "说一句（你先开口）。";

	public static string ReplyTurn(string parentRole, string lastLine, string playerText)
	{
		StringBuilder sb = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(lastLine))
		{
			sb.AppendLine($"你刚才说：“{lastLine.Trim()}”");
		}
		sb.AppendLine($"{parentRole}说：{PromptText.Wrap("PARENT", playerText, 300)}");
		sb.AppendLine("接着说一句。");
		return sb.ToString().Trim();
	}

	public static string? Clean(string? text)
	{
		string t = TextTools.CleanForGame(TextTools.Truncate((text ?? "").Trim().Trim('"', '“', '”'), MaxChars));
		return string.IsNullOrWhiteSpace(t) ? null : t;
	}
}

/// <summary>和孩子聊天：开场一句，之后一来一回；不打分、不加好感、不进八卦和日记，只记在孩子自己的本子里。</summary>
public sealed class ChildDialogue
{
	private readonly IAiSender _ai;

	private readonly ChildBook _book;

	private readonly ILog _log;

	public ChildDialogue(IAiSender ai, ChildBook book, ILog log)
	{
		_ai = ai;
		_book = book;
		_log = log;
	}

	public ChildBook Book => _book;

	public async Task<string?> OpeningAsync(ChildContext c, CancellationToken ct = default)
	{
		string system = ChildPrompts.System(c, _book.Recent(c.Key, 12), opening: true);
		AiResult r = await _ai.SendAsync(new ChatRequest(ChildPrompts.OpeningRequest, system, ChildPrompts.OpeningTurn(), 400, 0.9), ct).ConfigureAwait(false);
		string? line = r.Ok ? ChildPrompts.Clean(r.Text) : null;
		if (line == null)
		{
			_log.Debug($"孩子 {c.Name} 的开场没生成出来。");
		}
		return line;
	}

	public async Task<string?> ReplyAsync(ChildContext c, string lastLine, string playerText, CancellationToken ct = default)
	{
		string system = ChildPrompts.System(c, _book.Recent(c.Key, 12), opening: false);
		AiResult r = await _ai.SendAsync(new ChatRequest(ChildPrompts.ReplyRequest, system, ChildPrompts.ReplyTurn(c.ParentRole, lastLine, playerText), 400, 0.9), ct).ConfigureAwait(false);
		return r.Ok ? ChildPrompts.Clean(r.Text) : null;
	}
}
