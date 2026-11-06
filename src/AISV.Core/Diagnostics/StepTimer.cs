using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace AISV.Core.Diagnostics;

/// <summary>
/// 分步计时：启动、进存档这些阶段每一步花了多少毫秒，结束时打一行汇总。
/// <code>
/// var timer = new StepTimer("启动");
/// using (timer.Step("读配置")) { ... }
/// log.Info(timer.Summary());   // 启动 共 12ms：读配置 3ms、注册事件 1ms ……
/// </code>
/// </summary>
public sealed class StepTimer
{
	private readonly string _name;

	private readonly Stopwatch _total = Stopwatch.StartNew();

	private readonly List<(string Step, double Ms)> _steps = new List<(string, double)>();

	public StepTimer(string name)
	{
		_name = name;
	}

	public IReadOnlyList<(string Step, double Ms)> Steps => _steps;

	public double TotalMs => _total.Elapsed.TotalMilliseconds;

	public IDisposable Step(string step)
	{
		return new Scope(this, step);
	}

	/// <summary>一行汇总，最慢的排前面。</summary>
	public string Summary()
	{
		string steps = string.Join("、", _steps.OrderByDescending(s => s.Ms).Select(s => s.Step + " " + Format(s.Ms)));
		return _name + " 共 " + Format(TotalMs) + (steps.Length > 0 ? "：" + steps : "");
	}

	private static string Format(double ms)
	{
		return ms.ToString(ms < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + "ms";
	}

	private sealed class Scope : IDisposable
	{
		private readonly StepTimer _owner;

		private readonly string _step;

		private readonly Stopwatch _watch = Stopwatch.StartNew();

		private bool _done;

		public Scope(StepTimer owner, string step)
		{
			_owner = owner;
			_step = step;
		}

		public void Dispose()
		{
			if (_done)
			{
				return;
			}
			_done = true;
			_owner._steps.Add((_step, _watch.Elapsed.TotalMilliseconds));
		}
	}
}
