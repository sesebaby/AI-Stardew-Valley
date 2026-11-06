using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Festivals;
using AISV.Core.Text;
using AISV.Game.Social;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Festivals;

/// <summary>Beach Episode 的排球队友接口（它提供，我们只调用）。</summary>
public interface IBeachVolleyballApi
{
	bool CanInviteVolleyballPartner(string npcName);

	bool TrySetVolleyballPartner(string npcName);

	bool TryDeclineVolleyballPartner(string npcName);
}

/// <summary>认出正在进行的节日，管这一场的状态：谁聊过、有没有舞伴；散场时写总结。</summary>
public sealed class FestivalSystem
{
	private const int EndAfterAbsentTicks = 60;

	private const string BeachEpisodeId = "Lucas.BeachEpisode";

	private const string VolleyballFestival = "beach_volleyball_championship";

	private const string VolleyballAction = "invite_as_partner";

	private readonly Func<ModConfig> _config;

	private readonly string _directory;

	private readonly DialogueEngine _dialogue;

	private readonly SocialSystem _social;

	private readonly IModRegistry _mods;

	private readonly ILog _log;

	private readonly HashSet<string> _talked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private IReadOnlyList<FestivalPack>? _packs;

	private FestivalPack? _active;

	private string _sessionKey = "";

	private string? _partner;

	private bool _warned;

	private int _absentTicks;

	public FestivalSystem(Func<ModConfig> config, string modDirectory, DialogueEngine dialogue, SocialSystem social, IModRegistry mods, ILog log)
	{
		_config = config;
		_directory = Path.Combine(modDirectory, "assets", "festivals");
		_dialogue = dialogue;
		_social = social;
		_mods = mods;
		_log = log;
	}

	/// <summary>一进节日就开场（一句话没说也会写总结）；切地图的几帧里节日会短暂不可见：连续一秒都不在节日上才算散场。</summary>
	public void OnUpdateTicked()
	{
		if (_active == null)
		{
			if (Game1.currentLocation?.currentEvent?.isFestival == true)
			{
				_ = Active;
			}
			return;
		}
		_absentTicks = Active == null ? _absentTicks + 1 : 0;
		if (_absentTicks >= EndAfterAbsentTicks)
		{
			End();
		}
	}

	/// <summary>天黑了还没散场（比如节日上直接睡觉的情况）：马上结算。</summary>
	public void OnDayEnding()
	{
		if (_active != null)
		{
			End();
		}
	}

	/// <summary>玩家现在所在的节日；不在节日上、功能关着或没有对应的内容包时为 null。</summary>
	public FestivalPack? Active
	{
		get
		{
			Event? e = Game1.currentLocation?.currentEvent;
			if (e == null || !e.isFestival || !Context.IsWorldReady || !_config().FestivalTalkOn || !_config().AiOn)
			{
				return null;
			}
			string key = $"{Constants.SaveFolderName}:{Game1.year}:{Game1.currentSeason}:{Game1.dayOfMonth}";
			if (_active != null && key == _sessionKey)
			{
				return _active;
			}
			if (_ignored.Contains(key))
			{
				return null;
			}
			FestivalPack? pack = Detect();
			if (pack == null)
			{
				_ignored.Add(key);
				_log.Info($"这场节日没有对应的内容包，村民说游戏原来的话（{Game1.currentSeason} {Game1.dayOfMonth}，{Game1.currentLocation!.NameOrUniqueName}）。");
				return null;
			}
			if (_active != null)
			{
				End();
			}
			_active = pack;
			_sessionKey = key;
			_talked.Clear();
			_partner = null;
			_warned = false;
			_absentTicks = 0;
			_dialogue.EnterFestival();
			_log.Info($"节日对话开始：{pack.Id}。");
			return pack;
		}
	}

	private FestivalPack? Detect()
	{
		_packs ??= FestivalPacks.Load(_directory, _log);
		GameLocation here = Game1.currentLocation!;
		IReadOnlyDictionary<string, string>? data = _packs.Any(p => p.Detection.FestivalDataMarkers.Count > 0) ? FestivalData() : null;
		return _packs.FirstOrDefault(p => p.Detection.Matches(Game1.currentSeason, Game1.dayOfMonth, here.NameOrUniqueName, here.Name ?? "", data));
	}

	private static IReadOnlyDictionary<string, string>? FestivalData()
	{
		try
		{
			return Game1.content.Load<Dictionary<string, string>>($"Data/Festivals/{Game1.currentSeason}{Game1.dayOfMonth}");
		}
		catch (Exception)
		{
			return null;
		}
	}

	public bool HasTalked(string npc) => _talked.Contains(npc);

	public void MarkTalked(string npc) => _talked.Add(npc);

	/// <summary>这场节日第一次用不了 AI 时提示一声。</summary>
	public void NotifyUnavailable(string why)
	{
		_log.Warn($"节日台词用不了（{_active?.Id}）：{why}");
		if (_warned)
		{
			return;
		}
		_warned = true;
		string message = Tr.Get("festival.aiFailed", "这次节日的 AI 台词没接上，村民先说游戏原来的话。");
		if (Game1.chatBox != null)
		{
			Game1.chatBox.addMessage(message, new Color(255, 165, 0));
		}
		else
		{
			Game1.addHUDMessage(new HUDMessage(message, 3));
		}
	}

	/// <summary>这位村民能不能被邀请（跳舞、组队）。不能时返回 null。</summary>
	public FestivalAction? OfferFor(string npc)
	{
		FestivalPack? pack = _active;
		FestivalAction? action = pack?.Action;
		if (action == null)
		{
			return null;
		}
		if (action.Kind == FestivalAction.Dance)
		{
			return HasDancePartner ? null : action;
		}
		return Volleyball(pack!, action, api => api.CanInviteVolleyballPartner(npc)) ? action : null;
	}

	private bool HasDancePartner => _partner != null || Game1.player?.dancePartner.Value != null;

	/// <summary>花舞节有人答应当舞伴。</summary>
	public event Action<NPC>? DanceAccepted;

	/// <summary>把答复落到游戏里。邀请方（Beach Episode）记不下来时返回 false，这次当作没发生。</summary>
	public bool ApplyInvite(NPC npc, FestivalInviteDecision decision)
	{
		FestivalPack? pack = _active;
		FestivalAction? action = pack?.Action;
		if (action == null)
		{
			return false;
		}
		if (action.Kind == FestivalAction.Invitation)
		{
			return Volleyball(pack!, action, api => decision.Accepted ? api.TrySetVolleyballPartner(npc.Name) : api.TryDeclineVolleyballPartner(npc.Name));
		}
		if (decision.Accepted)
		{
			_partner = npc.Name;
			Game1.player.dancePartner.Value = npc;
			DanceAccepted?.Invoke(npc);
		}
		return true;
	}

	private bool Volleyball(FestivalPack pack, FestivalAction action, Func<IBeachVolleyballApi, bool> call)
	{
		if (pack.Id != VolleyballFestival || action.Id != VolleyballAction)
		{
			return false;
		}
		try
		{
			IBeachVolleyballApi? api = _mods.GetApi<IBeachVolleyballApi>(BeachEpisodeId);
			return api != null && call(api);
		}
		catch (Exception ex)
		{
			_log.Warn("问 Beach Episode 排球队友时出错：" + ex.Message);
			return false;
		}
	}

	public void Reset()
	{
		if (_active != null)
		{
			_dialogue.LeaveFestival();
		}
		_active = null;
		_sessionKey = "";
		_talked.Clear();
		_ignored.Clear();
		_partner = null;
	}

	private void End()
	{
		FestivalPack pack = _active!;
		List<string> talked = _talked.ToList();
		Reset();
		_log.Info($"节日对话结束：{pack.Id}。");
		try
		{
			_social.OnFestivalEnded(pack, talked);
		}
		catch (Exception ex)
		{
			_log.Warn("写节日总结时出错：" + ex.Message);
		}
	}
}
