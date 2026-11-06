using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Social;

/// <summary>应用里要存档或每天结算的部分：Junimo 宠物、天文台。</summary>
public sealed class AppsService
{
	private readonly Func<DocumentStore?> _store;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	private readonly Random _random = new Random();

	private int _forcedMeteorDay = -1;

	public AppsService(Func<DocumentStore?> store, NpcDirectory npcs, ILog log)
	{
		_store = store;
		_npcs = npcs;
		_log = log;
	}

	private static int Today => (Game1.Date?.TotalDays ?? 0) + 1;

	public JunimoState? Junimo => _store()?.Get(JunimoState.Path, JunimoState.Doc);

	public void OnDayStarted()
	{
		if (!Context.IsWorldReady || Junimo is not JunimoState s)
		{
			return;
		}
		try
		{
			int kids = Game1.player.getChildrenCount();
			if (!s.Adopted && kids > 0)
			{
				JunimoRules.Adopt(s, Today, _random);
				JunimoRules.Add(s, Today, Core.Text.Tr.Get("junimo.log.baby", "家里添了个小宝宝，{{name}}守在摇篮边上不肯走。", ("name", s.Name)));
				_log.Info($"Junimo 宠物：家里有了孩子，{s.Name} 搬了进来。");
			}
			JunimoRules.NewDay(s, Today, Home(), _npcs.Names.Display);
			Dirty();
		}
		catch (Exception ex)
		{
			_log.Warn("Junimo 宠物：早上结算出错：" + ex.Message);
		}
	}

	private static JunimoHome Home()
	{
		Farmer p = Game1.player;
		string? spouse = string.IsNullOrWhiteSpace(p.spouse) ? null : p.spouse;
		int hearts = spouse != null && p.friendshipData.TryGetValue(spouse, out Friendship? f) ? f.Points / 250 : 0;
		string? ex = p.friendshipData.Pairs.Where(kv => kv.Value.IsDivorced()).Select(kv => kv.Key).FirstOrDefault();
		return new JunimoHome(spouse, hearts, ex != null && spouse == null, p.getChildrenCount(), ex);
	}

	public string? Act(string act)
	{
		if (Junimo is not JunimoState s || !s.Adopted)
		{
			return null;
		}
		string? said = JunimoRules.Act(s, act, Today);
		if (said != null)
		{
			JunimoRules.Add(s, Today, said);
			Dirty();
		}
		return said;
	}

	public void Adopt(string? name = null)
	{
		if (Junimo is JunimoState s && !s.Adopted)
		{
			JunimoRules.Adopt(s, Today, _random, name);
			Dirty();
		}
	}

	private void Dirty() => _store()?.MarkDirty(JunimoState.Path);

	// ---------------- 天文台 ----------------

	public NightSky Tonight()
	{
		bool summit = Game1.MasterPlayer?.mailReceived.Contains("Farm_Eternal") == true || Game1.player?.mailReceived.Contains("Summit_event") == true;
		return Observatory.Tonight(Game1.uniqueIDForThisGame, Game1.currentSeason, Game1.dayOfMonth, Game1.year, Game1.isRaining || Game1.isLightning, Game1.isSnowing, _forcedMeteorDay == Today, summit);
	}

	public int NextMeteor() => Observatory.NextMeteor(Game1.uniqueIDForThisGame, Game1.currentSeason, Game1.year, Game1.dayOfMonth);

	public string Command(string[] args)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
		switch (sub)
		{
			case "meteor":
				_forcedMeteorDay = Today;
				return "今晚改成有流星雨（下雨时还是看不到）：" + Tonight().Summary;
			case "sky":
				return $"{Tonight().Summary} 这一季的流星雨：{string.Join("、", Observatory.MeteorNights(Game1.uniqueIDForThisGame, Game1.currentSeason, Game1.year))} 日。";
			case "adopt":
				Adopt(args.Length > 1 ? args[1] : null);
				return Junimo is JunimoState a && a.Adopted ? $"领养了 {a.Name}。" : "没领养成。";
			case "junimo" when Junimo is JunimoState s:
				if (args.Length >= 3 && int.TryParse(args[2], out int v))
				{
					if (args[1] == "joy")
					{
						s.Joy = Math.Clamp(v, 0, 100);
					}
					else if (args[1] == "full")
					{
						s.Fullness = Math.Clamp(v, 0, 100);
					}
					Dirty();
				}
				else if (args.Length >= 2 && args[1] == "day")
				{
					s.LastDay = Today - 1;
					JunimoRules.NewDay(s, Today, Home(), _npcs.Names.Display);
					Dirty();
				}
				return $"{s.Name}：吃饱 {s.Fullness}，开心 {s.Joy}，{JunimoRules.MoodLabel(JunimoRules.Mood(s))}。";
			default:
				return "用法：aisv_apps meteor | sky | adopt [名字] | junimo [joy|full <数>] | junimo day";
		}
	}
}
