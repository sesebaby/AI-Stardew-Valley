using System;
using System.Collections.Generic;

namespace AISV.Core.Messenger;

public enum PeekPhase
{
	Hidden,
	Rising,
	Banner,
	Tucking,
	Tab,
	Leaving
}

/// <summary>一条提示：哪个聊天（村民名或群）、谁说的、说了什么。</summary>
public sealed record PeekNote(string Thread, string Text, string Speaker = "");

/// <summary>来消息时屏幕下方滑出的小手机：升起、亮一会儿横幅、缩成露头的一小截，消息读了或被收起就退下去。</summary>
public sealed class MessagePeek
{
	public const double SlideMs = 280;

	public const double BannerMs = 5500;

	public const int MaxWaiting = 4;

	public const float TabShare = 0.28f;

	private readonly List<PeekNote> _waiting = new List<PeekNote>();

	private double _elapsed;

	private float _from;

	public PeekPhase Phase { get; private set; }

	public PeekNote? Current { get; private set; }

	public bool IsVisible => Phase != PeekPhase.Hidden;

	/// <summary>露出来的比例，0～1。</summary>
	public float Shown
	{
		get
		{
			float t = (float)Math.Clamp(_elapsed / SlideMs, 0, 1);
			t = 1f - (1f - t) * (1f - t);
			return Phase switch
			{
				PeekPhase.Rising => _from + (1f - _from) * t,
				PeekPhase.Banner => 1f,
				PeekPhase.Tucking => 1f + (TabShare - 1f) * t,
				PeekPhase.Tab => TabShare,
				PeekPhase.Leaving => _from * (1f - t),
				_ => 0f
			};
		}
	}

	/// <summary>同一个聊天的新消息顶掉旧的，一口气来好几条也只亮最新的。</summary>
	public void Post(PeekNote note)
	{
		if (Current != null && Current.Thread == note.Thread && Phase is PeekPhase.Rising or PeekPhase.Banner)
		{
			Current = note;
			if (Phase == PeekPhase.Banner)
			{
				_elapsed = 0;
			}
			return;
		}
		_waiting.RemoveAll(w => w.Thread == note.Thread);
		_waiting.Add(note);
		while (_waiting.Count > MaxWaiting)
		{
			_waiting.RemoveAt(0);
		}
	}

	/// <summary>右键收起：连同还没轮到的一起。</summary>
	public void Dismiss()
	{
		_waiting.Clear();
		if (Phase != PeekPhase.Hidden)
		{
			Start(PeekPhase.Leaving, Shown);
		}
	}

	public void Clear()
	{
		_waiting.Clear();
		Phase = PeekPhase.Hidden;
		Current = null;
		_elapsed = 0;
	}

	/// <summary>走一帧。返回 true 表示刚换上一条新横幅（该响一声）。</summary>
	public bool Update(double elapsedMs, Func<string, bool> isUnread, bool anyUnread)
	{
		_elapsed += Math.Max(0, elapsedMs);
		if (Phase is PeekPhase.Rising or PeekPhase.Banner && Current != null && !isUnread(Current.Thread))
		{
			Start(PeekPhase.Leaving, Shown);
			return false;
		}
		switch (Phase)
		{
		case PeekPhase.Rising when _elapsed >= SlideMs:
			Start(PeekPhase.Banner, 1f);
			break;
		case PeekPhase.Banner when _elapsed >= BannerMs:
			if (NextNote(isUnread) is PeekNote next)
			{
				Current = next;
				Start(PeekPhase.Banner, 1f);
				return true;
			}
			Start(anyUnread ? PeekPhase.Tucking : PeekPhase.Leaving, 1f);
			break;
		case PeekPhase.Tucking when _elapsed >= SlideMs:
			Current = null;
			Start(PeekPhase.Tab, TabShare);
			break;
		case PeekPhase.Tab:
			if (NextNote(isUnread) is PeekNote fresh)
			{
				Current = fresh;
				Start(PeekPhase.Rising, TabShare);
				return true;
			}
			if (!anyUnread)
			{
				Start(PeekPhase.Leaving, TabShare);
			}
			break;
		case PeekPhase.Leaving when _elapsed >= SlideMs:
			Phase = PeekPhase.Hidden;
			Current = null;
			_elapsed = 0;
			break;
		case PeekPhase.Hidden:
			if (NextNote(isUnread) is PeekNote note)
			{
				Current = note;
				Start(PeekPhase.Rising, 0f);
				return true;
			}
			break;
		}
		return false;
	}

	private PeekNote? NextNote(Func<string, bool> isUnread)
	{
		while (_waiting.Count > 0)
		{
			PeekNote note = _waiting[0];
			_waiting.RemoveAt(0);
			if (isUnread(note.Thread))
			{
				return note;
			}
		}
		return null;
	}

	private void Start(PeekPhase phase, float from)
	{
		Phase = phase;
		_from = from;
		_elapsed = 0;
	}
}
