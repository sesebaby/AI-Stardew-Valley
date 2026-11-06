using System;
using System.Diagnostics;
using System.Threading;

namespace AISV.Game.Dialogue;

/// <summary>
/// 正在等哪位村民的开场白（屏幕上"××在想……"、按 ESC 取消、单人时暂停游戏时间都看它），
/// 以及刚失败时要显示几秒的那句说明。
/// </summary>
public static class GenerationStatus
{
	/// <summary>刚开始的 1 秒内不让取消（避免点村民的那一下同时被当成取消）。</summary>
	private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan FailureShown = TimeSpan.FromSeconds(8);

	private static readonly object Sync = new object();

	private static Active? _active;

	private static string _failure = "";

	private static readonly Stopwatch FailureClock = new Stopwatch();

	public sealed record Snapshot(string NpcName, string DisplayName, TimeSpan Elapsed, TimeSpan Budget);

	private sealed class Active
	{
		public string NpcName = "";

		public string DisplayName = "";

		public CancellationTokenSource Cts = null!;

		public Stopwatch Clock = null!;

		public TimeSpan Budget;

		public bool CancelledByPlayer;
	}

	public static bool IsActive
	{
		get
		{
			lock (Sync)
			{
				return _active != null;
			}
		}
	}

	public static Snapshot? Current
	{
		get
		{
			lock (Sync)
			{
				return _active == null ? null : new Snapshot(_active.NpcName, _active.DisplayName, _active.Clock.Elapsed, _active.Budget);
			}
		}
	}

	/// <summary>还在显示时间内的失败说明；没有为 null。</summary>
	public static string? Failure
	{
		get
		{
			lock (Sync)
			{
				return _failure.Length > 0 && FailureClock.IsRunning && FailureClock.Elapsed < FailureShown ? _failure : null;
			}
		}
	}

	public static void Begin(string npcName, string displayName, CancellationTokenSource cts, TimeSpan budget)
	{
		lock (Sync)
		{
			_active = new Active { NpcName = npcName, DisplayName = string.IsNullOrWhiteSpace(displayName) ? npcName : displayName, Cts = cts, Clock = Stopwatch.StartNew(), Budget = budget };
			_failure = "";
			FailureClock.Reset();
		}
	}

	public static void End(string npcName)
	{
		lock (Sync)
		{
			if (_active != null && string.Equals(_active.NpcName, npcName, StringComparison.OrdinalIgnoreCase))
			{
				_active = null;
			}
		}
	}

	public static bool CancelledByPlayer(string npcName)
	{
		lock (Sync)
		{
			return _active != null && string.Equals(_active.NpcName, npcName, StringComparison.OrdinalIgnoreCase) && _active.CancelledByPlayer;
		}
	}

	/// <summary>玩家按 ESC：取消正在等的生成。</summary>
	public static bool Cancel()
	{
		Active? active;
		lock (Sync)
		{
			if (_active == null || _active.Clock.Elapsed < CancelGrace)
			{
				return false;
			}
			active = _active;
			active.CancelledByPlayer = true;
		}
		try
		{
			active.Cts.Cancel();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	/// <summary>换天、回标题：不管宽限，直接取消。</summary>
	public static void Abort()
	{
		Active? active;
		lock (Sync)
		{
			active = _active;
		}
		try
		{
			active?.Cts.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public static void ReportFailure(string message)
	{
		lock (Sync)
		{
			_failure = message ?? "";
			FailureClock.Restart();
		}
	}

	public static void Reset()
	{
		lock (Sync)
		{
			_active = null;
			_failure = "";
			FailureClock.Reset();
		}
	}
}
