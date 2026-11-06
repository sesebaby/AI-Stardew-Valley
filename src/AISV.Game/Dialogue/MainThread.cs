using System;
using System.Collections.Concurrent;
using AISV.Core.Diagnostics;

namespace AISV.Game.Dialogue;

/// <summary>后台线程把要碰游戏状态的事交回主线程：排进队列，每帧（UpdateTicked）执行。</summary>
public static class MainThread
{
	private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

	public static ILog Log { get; set; } = NullLog.Instance;

	public static void Run(Action action) => Queue.Enqueue(action);

	/// <summary>主线程每帧调用。</summary>
	public static void Drain()
	{
		while (Queue.TryDequeue(out Action? action))
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Log.Error("主线程上执行排队的操作时出错：" + ex);
			}
		}
	}

	public static void Clear()
	{
		while (Queue.TryDequeue(out _))
		{
		}
	}
}
