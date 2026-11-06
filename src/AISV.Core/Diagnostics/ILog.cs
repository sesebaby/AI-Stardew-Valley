namespace AISV.Core.Diagnostics;

/// <summary>日志。Core 不认识 SMAPI，游戏层把 SMAPI 的 Monitor 包成这个接口传进来。</summary>
public interface ILog
{
	void Trace(string message);

	void Debug(string message);

	void Info(string message);

	void Warn(string message);

	void Error(string message);
}

/// <summary>什么也不记的日志（测试和还没接上日志时用）。</summary>
public sealed class NullLog : ILog
{
	public static readonly NullLog Instance = new NullLog();

	public void Trace(string message) { }

	public void Debug(string message) { }

	public void Info(string message) { }

	public void Warn(string message) { }

	public void Error(string message) { }
}
