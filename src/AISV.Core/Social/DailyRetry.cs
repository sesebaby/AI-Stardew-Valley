using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AISV.Core.Dialogue;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;

namespace AISV.Core.Social;

/// <summary>排上晚上整理的那一天：素材都留着，失败了第二天可以原样重试（town/nightly_retry.json）。</summary>
public sealed class PendingDailyAnalysis
{
	public List<DailyInteraction> Chats { get; set; } = new List<DailyInteraction>();

	public int Day { get; set; }

	public string Facts { get; set; } = "";

	public List<HeartEventCandidate> HeartScenes { get; set; } = new List<HeartEventCandidate>();

	public List<string> JournalNotes { get; set; } = new List<string>();

	public ExternalNotes OutsideNotes { get; set; } = ExternalNotes.None;

	public string PlainJournal { get; set; } = "";

	public string Season { get; set; } = "";

	public string Ticket { get; set; } = "";

	public List<string> TownEvents { get; set; } = new List<string>();

	public string Weather { get; set; } = "";

	/// <summary>false = 联机农场工只写日记。</summary>
	public bool WithRumors { get; set; }

	public int Year { get; set; }

	[JsonIgnore]
	public GameDate Date => new GameDate(Day, Season, Year);

	[JsonIgnore]
	public int TotalDays => Dates.TotalDays(Day, Season, Year);
}

/// <summary>
/// 晚上整理的重试：天黑排上整理时记下素材；成功了清掉；下一个天黑前作废。
/// 只有记下的那天已经过去、而且没在跑时才能重试。只在主线程用。
/// </summary>
public sealed class DailyRetry
{
	public const string Path = "town/nightly_retry.json";

	private static readonly DocumentType<PendingDailyAnalysis> Doc = new DocumentType<PendingDailyAnalysis>("晚上整理重试", 1, () => new PendingDailyAnalysis());

	private readonly Func<DocumentStore?> _store;

	public DailyRetry(Func<DocumentStore?> store)
	{
		_store = store;
	}

	/// <summary>记下的那一天（没有时为 null）。</summary>
	public PendingDailyAnalysis? Pending
	{
		get
		{
			DocumentStore? s = _store();
			if (s == null)
			{
				return null;
			}
			PendingDailyAnalysis p = s.Get(Path, Doc);
			return p.Year <= 0 || string.IsNullOrWhiteSpace(p.Season) ? null : p;
		}
	}

	/// <summary>今天能不能重试：记下的那天已经过去（<paramref name="today"/> 之前）。</summary>
	public PendingDailyAnalysis? RetryableOn(GameDate today)
	{
		PendingDailyAnalysis? p = Pending;
		return p != null && p.TotalDays < today.TotalDays ? p : null;
	}

	public void Begin(PendingDailyAnalysis p) => _store()?.Put(Path, Doc, p);

	/// <summary>那天整理成功了：是同一天就清掉。</summary>
	public void Complete(GameDate date)
	{
		PendingDailyAnalysis? p = Pending;
		if (p != null && date.SameDay(p.Day, p.Season, p.Year))
		{
			Discard();
		}
	}

	public void Discard()
	{
		DocumentStore? s = _store();
		if (s != null && Pending != null)
		{
			s.Put(Path, Doc, new PendingDailyAnalysis());
		}
	}
}
