using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using AISV.Core.Storage;

namespace AISV.Core.Social.Letters;

/// <summary>一封村民写给农夫的信（mail/letters.json）。</summary>
public sealed class QueuedLetter
{
	/// <summary>hearts_6、miss_you_spring_1、thanks_birthday_gift_1、after_festival_…… 同一位村民同一个触发只寄一次。</summary>
	[JsonPropertyName("cause")]
	public string Cause { get; set; } = "";

	[JsonPropertyName("day")]
	public int Day { get; set; }

	/// <summary>附带的任意物品（带前缀的 ID）和数量，委托交货用。</summary>
	[JsonPropertyName("item")]
	public string? Enclosed { get; set; }

	[JsonPropertyName("enclosed_count")]
	public int EnclosedCount { get; set; } = 1;

	/// <summary>附带的物品（游戏物品 ID），-1 = 没有。</summary>
	[JsonPropertyName("enclosed_id")]
	public int EnclosedId { get; set; } = -1;

	[JsonPropertyName("from")]
	public string From { get; set; } = "";

	/// <summary>附的 AI 礼物；读信时放进信里，不用游戏的物品标记。</summary>
	[JsonPropertyName("gift_ref")]
	public string? GiftRef { get; set; }

	/// <summary>游戏邮件里的键。</summary>
	[JsonPropertyName("mailbox_key")]
	public string MailboxKey { get; set; } = "";

	[JsonPropertyName("season")]
	public string Season { get; set; } = "";

	[JsonPropertyName("sent")]
	public bool Sent { get; set; }

	[JsonPropertyName("sent_on")]
	public int SentOn { get; set; }

	[JsonPropertyName("body")]
	public string Text { get; set; } = "";

	[JsonPropertyName("subject")]
	public string Title { get; set; } = "";

	[JsonPropertyName("year")]
	public int Year { get; set; }

	[JsonIgnore]
	public int TotalDay => Dates.TotalDays(Day, Season, Year);

	[JsonIgnore]
	public string Key => MailboxKey;
}

/// <summary>还没寄出的"事件信"（节日后、出游后、故事结局后……）。</summary>
public sealed class PendingEventLetter
{
	public string NpcName { get; set; } = "";

	public string Trigger { get; set; } = "";

	public string Context { get; set; } = "";

	public int DueTotalDay { get; set; }
}

/// <summary>信件的存档数据。</summary>
public sealed class LetterData
{
	public List<QueuedLetter> Letters { get; set; } = new List<QueuedLetter>();

	public Dictionary<string, int> LastLetterDay { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public List<string> HandledEventKeys { get; set; } = new List<string>();

	public List<PendingEventLetter> PendingEventLetters { get; set; } = new List<PendingEventLetter>();
}

/// <summary>今天要写的一封信：谁、什么触发、给 AI 的情境说明。</summary>
public sealed record LetterTrigger(string Npc, string Trigger, string Context);

/// <summary>判断"今天该不该给这位村民写信"需要的情况。</summary>
public readonly record struct LetterCandidate(string Npc, int Hearts, int ConversationCount, int? LastTalkTotalDay);

/// <summary>信件。存在 mail/letters.json。只在主线程用。</summary>
public sealed class LetterBook
{
	public const string Path = "mail/letters.json";

	/// <summary>伴侣剧情的信的触发前缀：不算进每天的信数。</summary>
	public const string StoryPrefix = "story:";

	public const int PendingExpiryDays = 14;

	public const int RedeliveryDays = 7;

	private static readonly DocumentType<LetterData> Doc = new DocumentType<LetterData>("信件", 1, () => new LetterData());

	/// <summary>到心数里程碑时附的东西：物品 ID → 爱送它的人。</summary>
	private static readonly Dictionary<string, int> UsualGifts = new (int Item, string Senders)[]
	{
		(276, "Abigail"), (90, "Sam Linus"), (227, "Sebastian"), (88, "Haley"), (62, "Emily"), (212, "Alex"),
		(190, "Maru"), (254, "Penny"), (426, "Leah"), (637, "Elliott"), (260, "Shane"), (342, "Harvey"),
		(614, "Caroline Lewis"), (636, "Robin"), (608, "Marnie"), (635, "Gus"), (336, "Clint"), (769, "Wizard")
	}.SelectMany(g => g.Senders.Split(' ').Select(who => (Who: who, g.Item))).ToDictionary(p => p.Who, p => p.Item, StringComparer.OrdinalIgnoreCase);

	private readonly Func<DocumentStore?> _store;

	private readonly Func<ModConfig> _config;

	private readonly Func<string, bool> _npcEnabled;

	public LetterBook(Func<DocumentStore?> store, Func<ModConfig> config, Func<string, bool> npcEnabled)
	{
		_store = store;
		_config = config;
		_npcEnabled = npcEnabled;
	}

	/// <summary>新的在前（关掉 AI 的村民的信不列出）。</summary>
	public List<QueuedLetter> All() => Data().Letters.Where(l => _npcEnabled(l.From)).OrderByDescending(l => l.Year).ThenByDescending(l => GameDate.SeasonIndex(l.Season)).ThenByDescending(l => l.Day).ToList();

	public IEnumerable<QueuedLetter> Queued => Data().Letters;

	public bool HasSent(string npc, string trigger) => Data().Letters.Any(l => string.Equals(l.From, npc, StringComparison.OrdinalIgnoreCase) && l.Cause == trigger);

	public QueuedLetter? Find(string npc, string trigger) => Data().Letters.FirstOrDefault(l => string.Equals(l.From, npc, StringComparison.OrdinalIgnoreCase) && string.Equals(l.Cause, trigger, StringComparison.OrdinalIgnoreCase));

	public IReadOnlyList<QueuedLetter> DeliveredOn(int totalDay) => Data().Letters.Where(l => l.Sent && l.SentOn == totalDay).ToList();

	/// <summary>
	/// 今天要写哪些信。先看排队到期的事件信，再按村民名字顺序看心数和"想你了"。
	/// <paramref name="candidates"/> 不用排序。
	/// </summary>
	public List<LetterTrigger> DueToday(GameDate today, IEnumerable<LetterCandidate> candidates)
	{
		ModConfig c = _config();
		List<LetterTrigger> due = new List<LetterTrigger>();
		if (!c.MailOn)
		{
			return due;
		}
		int now = today.TotalDays;
		CollectDueEvents(now, due);
		List<int> milestones = Milestones(c.MailHeartSteps);
		foreach (LetterCandidate who in candidates.Where(x => !string.IsNullOrWhiteSpace(x.Npc)).GroupBy(x => x.Npc, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Npc, StringComparer.OrdinalIgnoreCase))
		{
			if (due.Count >= c.MailDaily)
			{
				break;
			}
			if (InCooldown(who.Npc, now) || !_npcEnabled(who.Npc))
			{
				continue;
			}
			foreach (int h in milestones)
			{
				if (who.Hearts >= h && !HasSent(who.Npc, $"hearts_{h}"))
				{
					due.Add(new LetterTrigger(who.Npc, $"hearts_{h}", $"农夫和你的交情到了 {h} 心。"));
					break;
				}
			}
			if (who.Hearts >= 6 && who.ConversationCount > 5 && who.LastTalkTotalDay is int last && !due.Any(t => t.Npc == who.Npc))
			{
				string trigger = $"miss_you_{today.Season}_{today.Year}";
				if (now - last >= c.MailMissingDays && !HasSent(who.Npc, trigger))
				{
					due.Add(new LetterTrigger(who.Npc, trigger, "农夫有一阵子没来找你聊天了，你有点想念。"));
				}
			}
			if (due.Count >= c.MailDaily)
			{
				break;
			}
		}
		return due;
	}

	/// <summary>
	/// 请求一封事件信：信件和事件信都开着、这个事件没处理过、也没寄过。能马上寄就返回 true（由调用方去写信），否则排进队列。
	/// </summary>
	public bool RequestEvent(string npc, string trigger, string context, GameDate today, int delayDays = 0)
	{
		ModConfig c = _config();
		if (!c.MailOn || !c.MailAfterHeartScene || string.IsNullOrWhiteSpace(npc) || string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(context))
		{
			return false;
		}
		LetterData d = Data();
		string key = npc + "|" + trigger;
		if (d.HandledEventKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || HasSent(npc, trigger))
		{
			return false;
		}
		d.HandledEventKeys.Add(key);
		Dirty();
		int now = today.TotalDays;
		if (delayDays <= 0 && CanSend(npc, trigger, now) && CountQueuedOn(today) < c.MailDaily)
		{
			return true;
		}
		d.PendingEventLetters.Add(new PendingEventLetter { NpcName = npc, Trigger = trigger, Context = context, DueTotalDay = now + Math.Max(1, delayDays) });
		return false;
	}

	public bool HasHandledEvent(string npc, string trigger) => Data().HandledEventKeys.Contains(npc + "|" + trigger, StringComparer.OrdinalIgnoreCase) || HasSent(npc, trigger);

	/// <summary>写好了：放进待寄（同一触发已经寄过就不放），记下冷却。</summary>
	public QueuedLetter? Add(string npc, string trigger, string subject, string body, int attachedItemId, GameDate date, bool cooldown = true)
	{
		if (string.IsNullOrWhiteSpace(body) || HasSent(npc, trigger))
		{
			return null;
		}
		LetterData d = Data();
		QueuedLetter letter = new QueuedLetter
		{
			From = npc,
			Cause = trigger,
			Title = subject ?? "",
			Text = body.Trim(),
			EnclosedId = attachedItemId,
			Day = date.Day,
			Season = date.Season,
			Year = date.Year,
			MailboxKey = $"AISV_{npc}_{date.Day}_{date.Season}_{date.Year}_{d.Letters.Count}"
		};
		d.Letters.Add(letter);
		if (cooldown)
		{
			d.LastLetterDay[npc] = date.TotalDays;
		}
		Dirty();
		return letter;
	}

	/// <summary>里程碑信附的东西：常送的那件（不在黑名单、没超过价格上限才附），否则不附。</summary>
	public static int UsualGift(string npc, Func<int, bool> allowed) => UsualGifts.TryGetValue(npc, out int id) && allowed(id) ? id : -1;

	/// <summary>天亮时投递：没寄的放进信箱；寄过但当天没保存（信箱和已读里都没有）的，7 天内再放一次。</summary>
	public void Deliver(int today, Func<string, bool> inMailOrRead, Action<QueuedLetter> putInMailbox, Action<string>? log = null)
	{
		if (!_config().MailOn)
		{
			return;
		}
		bool changed = false;
		foreach (QueuedLetter l in Data().Letters)
		{
			if (l.Sent)
			{
				int age = today - l.TotalDay;
				if (age >= 0 && age <= RedeliveryDays && !inMailOrRead(l.Key))
				{
					putInMailbox(l);
					log?.Invoke($"{l.From} 的信（{l.Title}）寄到那天没有保存，重新放回信箱。");
				}
				continue;
			}
			if (!_npcEnabled(l.From))
			{
				continue;
			}
			try
			{
				putInMailbox(l);
				l.SentOn = today;
				log?.Invoke($"{l.From} 的信到了：{l.Title}");
			}
			catch (Exception ex)
			{
				log?.Invoke($"{l.From} 的信没放进信箱：{ex.Message}");
			}
			l.Sent = true;
			changed = true;
		}
		if (changed)
		{
			Dirty();
		}
	}

	/// <summary>游戏邮件内容：正文 + 署名，附了东西就加上物品标记。</summary>
	public static string MailText(QueuedLetter l, string signature)
	{
		string text = l.Text + "^^   — " + signature;
		if (!string.IsNullOrWhiteSpace(l.Enclosed))
		{
			return text + $"%item id {l.Enclosed} {Math.Max(1, l.EnclosedCount)} %%";
		}
		return l.EnclosedId > 0 ? text + $"%item object {l.EnclosedId} 1 %%" : text;
	}

	/// <summary>心数里程碑：只留 1～14、去重排序；一个都不剩时用默认的 3/6/8/10。</summary>
	public static List<int> Milestones(IEnumerable<int>? values)
	{
		List<int> list = (values ?? Array.Empty<int>()).Where(v => v >= 1 && v <= 14).Distinct().OrderBy(v => v).ToList();
		return list.Count > 0 ? list : new List<int> { 3, 6, 8, 10 };
	}

	private void CollectDueEvents(int now, List<LetterTrigger> due)
	{
		LetterData d = Data();
		ModConfig c = _config();
		bool changed = false;
		foreach (PendingEventLetter p in d.PendingEventLetters.ToList())
		{
			if (p.DueTotalDay > now)
			{
				continue;
			}
			if (!c.MailAfterHeartScene || now - p.DueTotalDay > PendingExpiryDays || HasSent(p.NpcName, p.Trigger))
			{
				d.PendingEventLetters.Remove(p);
				changed = true;
				continue;
			}
			if (due.Count >= c.MailDaily)
			{
				break;
			}
			if (!due.Any(t => string.Equals(t.Npc, p.NpcName, StringComparison.OrdinalIgnoreCase)) && CanSend(p.NpcName, p.Trigger, now))
			{
				d.PendingEventLetters.Remove(p);
				changed = true;
				due.Add(new LetterTrigger(p.NpcName, p.Trigger, p.Context));
			}
		}
		if (changed)
		{
			Dirty();
		}
	}

	private bool InCooldown(string npc, int now) => LastLetter(npc) is int last && now - last < _config().MailRestDays;

	private int? LastLetter(string npc)
	{
		foreach ((string key, int day) in Data().LastLetterDay)
		{
			if (string.Equals(key, npc, StringComparison.OrdinalIgnoreCase))
			{
				return day;
			}
		}
		return null;
	}

	private bool CanSend(string npc, string trigger, int now) => !InCooldown(npc, now) && _npcEnabled(npc) && !HasSent(npc, trigger);

	private int CountQueuedOn(GameDate d) => Data().Letters.Count(l => d.SameDay(l.Day, l.Season, l.Year) && !l.Cause.StartsWith(StoryPrefix, StringComparison.Ordinal));

	private LetterData Data() => _store()?.Get(Path, Doc) ?? new LetterData();

	private void Dirty() => _store()?.MarkDirty(Path);

	public void Changed() => Dirty();
}
