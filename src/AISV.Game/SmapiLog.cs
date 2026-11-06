using AISV.Core.Diagnostics;
using StardewModdingAPI;

namespace AISV.Game;

/// <summary>把 SMAPI 的 Monitor 包成 Core 的日志接口。</summary>
public sealed class SmapiLog : ILog
{
	private readonly IMonitor _monitor;

	public SmapiLog(IMonitor monitor)
	{
		_monitor = monitor;
	}

	public void Trace(string message) => _monitor.Log(message, LogLevel.Trace);

	public void Debug(string message) => _monitor.Log(message, LogLevel.Debug);

	public void Info(string message) => _monitor.Log(message, LogLevel.Info);

	public void Warn(string message) => _monitor.Log(message, LogLevel.Warn);

	public void Error(string message) => _monitor.Log(message, LogLevel.Error);
}
