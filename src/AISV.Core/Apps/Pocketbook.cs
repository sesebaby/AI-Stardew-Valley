using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Storage;

namespace AISV.Core.Apps;

/// <summary>待办本上的一行。</summary>
public sealed class TodoNote
{
	public string Words { get; set; } = "";

	public bool Ticked { get; set; }
}

public enum JotOutcome
{
	Added,
	Blank,
	PadFull
}

/// <summary>待办本：没做完的排前面，每个存档一本。</summary>
public sealed class TodoPad
{
	private const string File = "farmer/todo.json";

	private const int Capacity = 99;

	private static readonly DocumentType<List<TodoNote>> Kind = new DocumentType<List<TodoNote>>("待办", 1, () => new List<TodoNote>());

	private readonly Func<DocumentStore?> _store;

	public TodoPad(Func<DocumentStore?> store)
	{
		_store = store;
	}

	private List<TodoNote> Notes => _store()?.Get(File, Kind) ?? new List<TodoNote>();

	private void Saved() => _store()?.MarkDirty(File);

	public IReadOnlyList<TodoNote> InOrder() => Notes.OrderBy(n => n.Ticked ? 1 : 0).ToList();

	public JotOutcome Jot(string? words)
	{
		string text = (words ?? "").Trim();
		if (text.Length == 0)
		{
			return JotOutcome.Blank;
		}
		List<TodoNote> notes = Notes;
		if (notes.Count >= Capacity)
		{
			return JotOutcome.PadFull;
		}
		notes.Insert(0, new TodoNote { Words = text });
		Saved();
		return JotOutcome.Added;
	}

	public void Tick(TodoNote note)
	{
		note.Ticked = !note.Ticked;
		Saved();
	}

	public void Tear(TodoNote note)
	{
		if (Notes.Remove(note))
		{
			Saved();
		}
	}

	public void SweepTicked()
	{
		if (Notes.RemoveAll(n => n.Ticked) > 0)
		{
			Saved();
		}
	}
}

/// <summary>番茄钟：记这一段已经走了多久。按真实时间算，关掉页面也算数。</summary>
public sealed class FocusClock
{
	public static readonly TimeSpan WorkSpan = TimeSpan.FromMinutes(25);

	public static readonly TimeSpan RestSpan = TimeSpan.FromMinutes(5);

	/// <summary>全局只有一只钟。</summary>
	public static readonly FocusClock Shared = new FocusClock();

	private DateTime? _resumedAt;

	private TimeSpan _walked;

	public bool Resting { get; private set; }

	public int RoundsDone { get; private set; }

	public bool Ticking => _resumedAt != null;

	private TimeSpan Span => Resting ? RestSpan : WorkSpan;

	private TimeSpan Walked(DateTime now) => _walked + (_resumedAt is DateTime since ? now - since : TimeSpan.Zero);

	public TimeSpan Remaining(DateTime now)
	{
		TimeSpan rest = Span - Walked(now);
		return rest < TimeSpan.Zero ? TimeSpan.Zero : rest;
	}

	public double Share(DateTime now) => Math.Min(1, Walked(now) / Span);

	public void StartOrPause(DateTime now)
	{
		_walked = Walked(now);
		_resumedAt = Ticking ? null : now;
	}

	public void Restart()
	{
		_resumedAt = null;
		_walked = TimeSpan.Zero;
		Resting = false;
	}

	/// <summary>这一段走完就翻到下一段并停下，返回 true。</summary>
	public bool RollOver(DateTime now)
	{
		if (!Ticking || Walked(now) < Span)
		{
			return false;
		}
		RoundsDone += Resting ? 0 : 1;
		Resting = !Resting;
		_resumedAt = null;
		_walked = TimeSpan.Zero;
		return true;
	}
}

/// <summary>完美度里的一项：做到了几成，满分几分。</summary>
public sealed record FarmGoal(string Label, double Reached, int FullMarks)
{
	public double Marks => FullMarks * Math.Min(1, Math.Max(0, Reached));

	public bool Finished => Reached >= 1;
}

public static class FarmGoals
{
	/// <summary>没做完的在前，离完成近的更靠前。</summary>
	public static IReadOnlyList<FarmGoal> NextUp(IEnumerable<FarmGoal> goals) => goals.OrderBy(g => g.Finished).ThenByDescending(g => g.Reached).ToList();
}

/// <summary>谁在哪天过生日。</summary>
public sealed record BornOn(string Npc, string Season, int Day);

public sealed record ComingBirthday(BornOn Who, int InDays);

/// <summary>把一年排成一圈 112 天。</summary>
public static class BirthdayWheel
{
	private const int DaysPerSeason = 28;

	private const int DaysPerYear = 4 * DaysPerSeason;

	private static int SeasonSlot(string? season) => (season ?? "").ToLowerInvariant() switch
	{
		"spring" => 0,
		"summer" => 1,
		"fall" => 2,
		"winter" => 3,
		_ => -1
	};

	private static string SeasonAt(int slot) => slot switch
	{
		0 => "spring",
		1 => "summer",
		2 => "fall",
		_ => "winter"
	};

	public static bool IsIn(BornOn mark, string season) => SeasonSlot(mark.Season) == SeasonSlot(season);

	public static string Turn(string season, int steps) => SeasonAt((Math.Max(SeasonSlot(season), 0) + steps + 4) % 4);

	/// <summary>从今天起往后走一圈，碰到的前几个生日。</summary>
	public static IReadOnlyList<ComingBirthday> Soonest(IEnumerable<BornOn> marks, string season, int day, int howMany)
	{
		ILookup<int, BornOn> byDate = marks.Where(m => SeasonSlot(m.Season) >= 0).ToLookup(m => SeasonSlot(m.Season) * DaysPerSeason + m.Day - 1);
		int today = Math.Max(SeasonSlot(season), 0) * DaysPerSeason + day - 1;
		List<ComingBirthday> found = new List<ComingBirthday>();
		for (int ahead = 0; ahead < DaysPerYear && found.Count < howMany; ahead++)
		{
			found.AddRange(byDate[(today + ahead) % DaysPerYear].Select(m => new ComingBirthday(m, ahead)));
		}
		return found.Take(howMany).ToList();
	}
}
