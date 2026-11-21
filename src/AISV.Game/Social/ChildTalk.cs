using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using HarmonyLib;
using StardewValley;
using StardewValley.Characters;

namespace AISV.Game.Social;

/// <summary>
/// 你自己的孩子：会走路以后，每天第一次搭话时（游戏本来只冒个心）按设置说一句，或者交给界面层开一场来回几句的聊天。
/// 拿着帽子给孩子戴、过场里，都交给游戏。
/// </summary>
public sealed class ChildTalk
{
	private static ChildTalk? _instance;

	private readonly Func<ModConfig> _config;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	private readonly HashSet<string> _today = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private int _day = -1;

	public ChildTalk(Func<ModConfig> config, NpcDirectory npcs, ILog log)
	{
		_config = config;
		_npcs = npcs;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	/// <summary>这位村民有没有被角色卡改过性格。</summary>
	public Func<string, bool>? Rewritten { get; set; }

	public ChildCards? Cards { get; set; }

	public ChildBook? Book { get; set; }

	/// <summary>设置是"能来回聊"时，界面层接手开一场聊天。</summary>
	public Action<Child>? ChatRequested { get; set; }

	public void Apply(Harmony harmony)
	{
		_instance = this;
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.checkAction)), postfix: new HarmonyMethod(typeof(ChildTalk), nameof(Postfix)));
	}

	private static void Postfix(Child __instance, Farmer who, bool __result)
	{
		try
		{
			// 游戏只在每天第一次搭话时返回 true（戴帽子返回 false），这里正好一天一次。
			if (__result && who.IsLocalPlayer && Kids.CanTalk(__instance) && who.CurrentItem is not StardewValley.Objects.Hat && !NpcBusy.IsStandIn(__instance))
			{
				_instance?.Talk(__instance);
			}
		}
		catch (Exception ex)
		{
			_instance?._log.Warn("孩子说话出错：" + ex.Message);
		}
	}

	public ChildContext Context(Child child) => Kids.Context(child, Cards, _npcs.Names.Display, Rewritten, Language?.Invoke() ?? "中文");

	private void Talk(Child child)
	{
		ModConfig c = _config();
		if (c.ChildTalkMode == "Off" || !c.AiOn || Game1.activeClickableMenu != null)
		{
			return;
		}
		int day = Game1.Date.TotalDays;
		if (day != _day)
		{
			_day = day;
			_today.Clear();
		}
		if (!_today.Add(child.Name))
		{
			return;
		}
		if (c.ChildTalkMode == "Chat" && ChatRequested != null)
		{
			ChatRequested(child);
			return;
		}
		if (Ai is not IAiSender ai)
		{
			return;
		}
		ChildContext ctx = Context(child);
		string system = ChildPrompts.System(ctx, Book?.Recent(ctx.Key, 8) ?? Array.Empty<ChildTurn>(), opening: true);
		string name = child.Name;
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(ChildPrompts.OpeningRequest, system, ChildPrompts.OpeningTurn(), 400, 0.9)).ConfigureAwait(false);
				line = r.Ok ? ChildPrompts.Clean(r.Text) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"孩子 {name} 的话没生成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (string.IsNullOrWhiteSpace(line) || Game1.activeClickableMenu != null || Game1.getCharacterFromName(name, mustBeVillager: false) is not Child kid || kid.currentLocation != Game1.currentLocation)
				{
					return;
				}
				Book?.Add(ctx.Key, ctx.Day, "child", line!);
				Game1.DrawDialogue(new StardewValley.Dialogue(kid, null, line));
				_log.Info($"孩子 {name}：{line}");
			});
		});
	}
}
