using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Ai;

/// <summary>自己的网关可以填好几把密钥。一把用不了就轮到下一把，下次请求从这把接着用。</summary>
public sealed class KeyRing
{
	private static readonly char[] Separators = { ',', ';', '\n', '\r', '\t', ' ', '，', '；', '、', '\u3000' };

	private readonly object _gate = new object();

	private int _turn;

	public static IReadOnlyList<string> Split(string? keys) => (keys ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

	public string Current(string? keys)
	{
		IReadOnlyList<string> all = Split(keys);
		lock (_gate)
		{
			return all.Count == 0 ? "" : all[_turn % all.Count];
		}
	}

	/// <summary>这把用不了，往后挪。别的请求已经挪走了就不再挪，免得转回这把。</summary>
	public void MoveOff(string? keys, string failed)
	{
		lock (_gate)
		{
			IReadOnlyList<string> all = Split(keys);
			if (all.Count > 0 && all[_turn % all.Count] == failed)
			{
				_turn++;
			}
		}
	}

	/// <summary>只有这把密钥本身的问题才值得换：认证、权限、余额、限流。</summary>
	public static bool WorthSwitching(FailureKind failure) => failure is FailureKind.InvalidApiKey or FailureKind.PermissionDenied or FailureKind.PaymentRequired or FailureKind.RateLimit;
}

/// <summary>一类请求这次开游戏以来的情况。</summary>
public sealed record RequestTally(string Name, int Calls, int Failures, double AverageSeconds);
