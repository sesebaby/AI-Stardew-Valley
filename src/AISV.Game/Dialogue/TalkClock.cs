using System;
using AISV.Core.Config;
using AISV.Core.Dialogue;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Dialogue;

/// <summary>聊天花掉的时间：聊的时候先记着，菜单都关了、玩家能动时每隔一会儿推 10 分钟。只在单人游戏里算，节日和过场里不算。</summary>
public sealed class TalkClock
{
	private const int TicksPerStep = 20;

	private readonly Func<ModConfig> _config;

	private int _pending;

	private int _ticks;

	public TalkClock(Func<ModConfig> config)
	{
		_config = config;
	}

	public int Pending => _pending;

	/// <summary>聊完一轮（当面一轮回复、群聊里说一句）。</summary>
	public void Talked()
	{
		int minutes = _config().MinutesPerReply;
		if (minutes <= 0 || !Context.IsWorldReady || Context.IsMultiplayer || Game1.isFestival() || Game1.eventUp)
		{
			return;
		}
		_pending = TalkTime.Accrue(_pending, minutes);
	}

	public void OnUpdateTicked()
	{
		if (!TalkTime.Ready(_pending))
		{
			return;
		}
		if (!Context.IsWorldReady || Context.IsMultiplayer)
		{
			_pending = 0;
			return;
		}
		if (Game1.activeClickableMenu != null || !Context.IsPlayerFree || Game1.eventUp || Game1.isFestival() || Game1.dialogueUp)
		{
			_ticks = 0;
			return;
		}
		if (++_ticks < TicksPerStep)
		{
			return;
		}
		_ticks = 0;
		if (!TalkTime.CanStep(Game1.timeOfDay))
		{
			_pending = 0;
			return;
		}
		Game1.performTenMinuteClockUpdate();
		_pending = TalkTime.AfterStep(_pending);
	}

	/// <summary>新的一天、换存档时清掉没推完的。</summary>
	public void Reset()
	{
		_pending = 0;
		_ticks = 0;
	}
}
