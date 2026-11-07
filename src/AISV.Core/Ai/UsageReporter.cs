using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AISV.Core.Diagnostics;

namespace AISV.Core.Ai;

/// <summary>
/// 帮助改进功能的数据：游戏开着玩了多久、各界面功能打开了几次，每隔几分钟随心跳发给服务器。
/// 只有次数和秒数；键都是代码里的固定名字，不含玩家写的字。玩家关掉开关后什么都不记、不发，没发出去的也丢掉。
/// </summary>
public sealed class UsageReporter
{
	public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

	public const int MaxSeconds = 600;

	public const int MaxCount = 500;

	public const int MaxKeys = 50;

	private static readonly Regex KeyPattern = new Regex("^[a-z0-9_.:-]{1,48}$", RegexOptions.Compiled);

	/// <summary>界面层用的入口（游戏层启动时设好）。</summary>
	public static UsageReporter? Shared { get; set; }

	public static void Note(string key) => Shared?.Count(key);

	private readonly ServerAccount _account;

	private readonly Func<bool> _enabled;

	private readonly ILog _log;

	private readonly object _lock = new object();

	private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

	private double _seconds;

	private DateTime _lastSent = DateTime.UtcNow;

	private bool _sending;

	public UsageReporter(ServerAccount account, Func<bool> enabled, ILog log)
	{
		_account = account;
		_enabled = enabled;
		_log = log;
	}

	/// <summary>服务器是老版本、没有心跳接口：这次运行不再发。</summary>
	public bool Unsupported { get; private set; }

	public int PendingSeconds
	{
		get
		{
			lock (_lock)
			{
				return (int)_seconds;
			}
		}
	}

	public IReadOnlyDictionary<string, int> PendingCounts
	{
		get
		{
			lock (_lock)
			{
				return new Dictionary<string, int>(_counts);
			}
		}
	}

	public void Count(string key)
	{
		key = "ui:" + key.ToLowerInvariant();
		if (!_enabled() || !KeyPattern.IsMatch(key))
		{
			return;
		}
		lock (_lock)
		{
			if (_counts.TryGetValue(key, out int n))
			{
				_counts[key] = Math.Min(MaxCount, n + 1);
			}
			else if (_counts.Count < MaxKeys)
			{
				_counts[key] = 1;
			}
		}
	}

	public void AddSeconds(double seconds)
	{
		if (!_enabled() || seconds <= 0)
		{
			return;
		}
		lock (_lock)
		{
			_seconds = Math.Min(MaxSeconds, _seconds + seconds);
		}
	}

	public bool Due(DateTime utcNow) => !Unsupported && !_sending && utcNow - _lastSent >= Interval;

	/// <summary>把攒下的发出去；没发成就留着下次再发（不超过上限）。</summary>
	public async Task FlushAsync()
	{
		if (Unsupported || _sending)
		{
			return;
		}
		_lastSent = DateTime.UtcNow;
		if (!_enabled())
		{
			Clear();
			return;
		}
		if (!_account.IsLoggedIn)
		{
			return;
		}
		int seconds;
		Dictionary<string, int> counts;
		lock (_lock)
		{
			seconds = (int)_seconds;
			counts = new Dictionary<string, int>(_counts);
			_seconds -= seconds;
			_counts.Clear();
			_sending = true;
		}
		try
		{
			(bool ok, bool unsupported) = await _account.PingAsync(seconds, counts).ConfigureAwait(false);
			if (unsupported)
			{
				Unsupported = true;
				Clear();
				_log.Trace("服务器没有心跳接口，不再上报使用情况。");
			}
			else if (!ok)
			{
				Restore(seconds, counts);
			}
		}
		catch (Exception ex)
		{
			Restore(seconds, counts);
			_log.Trace("上报使用情况失败：" + ex.Message);
		}
		finally
		{
			_sending = false;
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_seconds = 0;
			_counts.Clear();
		}
	}

	private void Restore(int seconds, Dictionary<string, int> counts)
	{
		if (!_enabled())
		{
			return;
		}
		lock (_lock)
		{
			_seconds = Math.Min(MaxSeconds, _seconds + seconds);
			foreach (KeyValuePair<string, int> e in counts.Where(e => _counts.ContainsKey(e.Key) || _counts.Count < MaxKeys))
			{
				_counts[e.Key] = Math.Min(MaxCount, (_counts.TryGetValue(e.Key, out int n) ? n : 0) + e.Value);
			}
		}
	}
}
