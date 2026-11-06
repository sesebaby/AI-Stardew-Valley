using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using HarmonyLib;
using StardewValley;
using StardewValley.Characters;

namespace AISV.Game.Social;

/// <summary>你自己的孩子：会走路以后，每天第一次搭话时按家里的情况说一句（游戏本来只冒个心）。</summary>
public sealed class ChildTalk
{
	public const string Request = "child_line";

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

	public void Apply(Harmony harmony)
	{
		_instance = this;
		harmony.Patch(AccessTools.Method(typeof(Child), nameof(Child.checkAction)), postfix: new HarmonyMethod(typeof(ChildTalk), nameof(Postfix)));
	}

	private static void Postfix(Child __instance, Farmer who, bool __result)
	{
		try
		{
			if (__result && who.IsLocalPlayer && __instance.Age >= 3)
			{
				_instance?.Talk(__instance);
			}
		}
		catch (Exception ex)
		{
			_instance?._log.Warn("孩子说话出错：" + ex.Message);
		}
	}

	private void Talk(Child child)
	{
		ModConfig c = _config();
		if (Ai is not IAiSender ai || !c.AiOn || Game1.activeClickableMenu != null)
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
		string system = Prompt(child);
		string name = child.Name;
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest(Request, system, "说一句。", 400, 0.9)).ConfigureAwait(false);
				line = r.Ok ? TextTools.CleanForGame(TextTools.Truncate((r.Text ?? "").Trim().Trim('"', '“', '”'), 40)) : null;
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
				Game1.DrawDialogue(new StardewValley.Dialogue(kid, null, line));
				_log.Info($"孩子 {name}：{line}");
			});
		});
	}

	private string Prompt(Child child)
	{
		Farmer p = Game1.player;
		string me = p.IsMale ? "爸爸" : "妈妈";
		string? spouse = string.IsNullOrWhiteSpace(p.spouse) ? null : p.spouse;
		string? partner = spouse ?? p.friendshipData.Pairs.FirstOrDefault(kv => kv.Value.IsDivorced()).Key;
		bool partnerMale = partner != null && Game1.getCharacterFromName(partner) is NPC pn ? pn.Gender == Gender.Male : !p.IsMale;
		string other = partnerMale ? (p.IsMale ? "另一个爸爸" : "爸爸") : (p.IsMale ? "妈妈" : "另一个妈妈");
		List<string> home = new List<string>();
		if (spouse != null)
		{
			int hearts = p.friendshipData.TryGetValue(spouse, out Friendship? f) ? f.Points / 250 : 0;
			home.Add($"{other}是{_npcs.Names.Display(spouse)}，住在家里。");
			if (hearts < 6)
			{
				home.Add($"{me}和{other}最近不怎么说话，家里冷冷清清的。");
			}
			if (Rewritten?.Invoke(spouse) == true)
			{
				home.Add($"{other}最近好像变了个人：笑得很完美，说话的样子和以前不一样，你说不上来哪里不对，有点怕。");
			}
		}
		else
		{
			foreach ((string npc, Friendship f) in p.friendshipData.Pairs)
			{
				if (f.IsDivorced())
				{
					home.Add($"{other}（{_npcs.Names.Display(npc)}）已经搬走，不住在家里了，你很想{other}。");
					break;
				}
			}
		}
		if (home.Count == 0)
		{
			home.Add($"家里只有你和{me}。");
		}
		return $"这次只用{Language?.Invoke() ?? "中文"}回答。\n\n你是星露谷农场上{me}（{p.Name}）的孩子{child.Name}，{(child.Gender == Gender.Male ? "男孩" : "女孩")}，刚学会走路、会说简单的话。{me}刚走过来和你说话。\n\n# 家里的情况（只是资料）\n- "
			+ string.Join("\n- ", home)
			+ $"\n\n# 输出\n只写你说出口的一句话，像三四岁的小孩：词很简单，可以重复，可以奶声奶气。15 个字以内，不加引号，不写动作描写。\n";
	}
}
