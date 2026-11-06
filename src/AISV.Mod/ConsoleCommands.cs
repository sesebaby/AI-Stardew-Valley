using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AISV.Core.Ai;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Game.Npcs;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Mod;

/// <summary>SMAPI 控制台命令：登录、退出、查状态、试发一句。登录窗口做好之前先用这些。</summary>
internal sealed class ConsoleCommands
{
	private readonly ServerAccount _account;

	private readonly AiService _ai;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	public ConsoleCommands(ServerAccount account, AiService ai, NpcDirectory npcs, ILog log)
	{
		_account = account;
		_ai = ai;
		_npcs = npcs;
		_log = log;
	}

	public void Register(ICommandHelper commands)
	{
		commands.Add("aisv_login", "登录 AI星露谷服务器（名字没人用就注册）。\n用法：aisv_login <用户名> <密码>", (_, args) => Login(args));
		commands.Add("aisv_logout", "退出 AI星露谷服务器的登录。", (_, _) => Run(async () =>
		{
			await _account.LogoutAsync();
			_log.Info("已退出登录。");
		}));
		commands.Add("aisv_status", "显示 AI 连接和账号状态。", (_, _) => Run(async () =>
		{
			if (_account.IsLoggedIn)
			{
				await _account.RefreshMeAsync();
			}
			(int used, int skipped) = _ai.BackgroundUsage;
			_log.Info($"连接：{(_ai.UsesServer ? "AI星露谷服务器" : "自己的网关")}\n{_account.StatusText("")}\n今天：成功 {_ai.SucceededToday}，失败 {_ai.FailedToday}，后台 {used}/{(_ai.BackgroundDailyLimit == 0 ? "不限" : _ai.BackgroundDailyLimit.ToString())}（跳过 {skipped}）"
				+ (_ai.LastFailure == FailureKind.None ? "" : $"\n上次失败：{_ai.LastFailure} {_ai.LastFailureMessage}"));
		}));
		commands.Add("aisv_npcs", "列出村民：是否由 AI 驱动、人设来源、来自哪个 mod。", (_, _) => ListNpcs());
		commands.Add("aisv_persona", "显示一位村民给 AI 的人设。\n用法：aisv_persona <内部名>", (_, args) =>
		{
			if (args.Length == 0 || !Context.IsWorldReady)
			{
				_log.Info("进存档以后用：aisv_persona <内部名>");
				return;
			}
			NpcPersona p = _npcs.Persona(args[0], Game1.getCharacterFromName(args[0]));
			_log.Info($"{p.Name}（来源 {p.Source}）：\n{p.Personality}");
		});
		commands.Add("aisv_ask", "直接发一句给 AI，看连接是否正常。\n用法：aisv_ask <一句话>", (_, args) => Run(async () =>
		{
			string text = string.Join(" ", args).Trim();
			if (text.Length == 0)
			{
				_log.Info("用法：aisv_ask <一句话>");
				return;
			}
			AiResult r = await _ai.SendAsync(new ChatRequest("console_ask", "你是星露谷物语里鹈鹕镇的一位居民，用一两句话回答。", text, 512, 0.8));
			_log.Info(r.Ok ? "AI：" + r.Text : $"没拿到回复：{r.Failure} {r.Message}");
		}));
	}

	private void ListNpcs()
	{
		if (!Context.IsWorldReady)
		{
			_log.Info("进存档以后再用。");
			return;
		}
		List<string> lines = new List<string>();
		Utility.ForEachVillager(npc =>
		{
			string? reason = _npcs.Eligibility.RejectionReason(npc.Name, npc.IsVillager, null, present: true);
			NpcPersona persona = _npcs.Persona(npc.Name, npc);
			lines.Add($"{npc.Name}（{_npcs.Names.Display(npc.Name)}）：{(reason == null ? "AI" : "不用 AI：" + reason)}，人设来源 {persona.Source}" + (_npcs.Eligibility.OwnerModId(npc.Name) is string mod ? "，来自 " + mod : ""));
			return true;
		});
		_log.Info($"{lines.Count} 位村民：\n" + string.Join("\n", lines.OrderBy(l => l)));
	}

	private void Login(string[] args)
	{
		if (args.Length < 2)
		{
			_log.Info("用法：aisv_login <用户名> <密码>");
			return;
		}
		Run(async () =>
		{
			(bool ok, string message) = await _account.EnterAsync(args[0], args[1]);
			_log.Info(ok ? (message == "created" ? "注册并登录成功。" : "登录成功。") + "\n" + _account.StatusText("") : "登录失败：" + message);
		});
	}

	/// <summary>命令在主线程上触发；网络请求放到后台，结果写日志。</summary>
	private void Run(Func<Task> work)
	{
		Task.Run(async () =>
		{
			try
			{
				await work();
			}
			catch (Exception ex)
			{
				_log.Error("命令出错：" + ex.Message);
			}
		});
	}
}
