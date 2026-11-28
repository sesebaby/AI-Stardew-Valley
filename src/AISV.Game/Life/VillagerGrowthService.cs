using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Life;
using AISV.Core.Persona;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Life;

/// <summary>
/// 村民的成长：每天早上（房主）看哪几位的经历攒够了——交情上了一档、做成心愿、交往结婚、换了工作、性格有了转折、又过了一年——
/// 请 AI 在原本角色卡的底色上重写一段"现在的他"，接在人设后面给所有聊天用。玩家整个改写过性格的村民不动。
/// </summary>
public sealed class VillagerGrowthService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly SocialSystem _social;

	private readonly NpcDirectory _npcs;

	private readonly LifeService _life;

	private readonly ILog _log;

	/// <summary>正在写的（免得同一天排两次）。</summary>
	private readonly HashSet<string> _writing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public VillagerGrowthService(Func<ModConfig> config, Func<DocumentStore?> store, SocialSystem social, NpcDirectory npcs, LifeService life, ILog log)
	{
		_config = config;
		_store = store;
		_social = social;
		_npcs = npcs;
		_life = life;
		_log = log;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public VillagerGrowthBook? Book => _store()?.Get(VillagerGrowthBook.Path, VillagerGrowthBook.Doc);

	private bool On => _config().ModOn && _config().VillagerGrowth;

	private static int Today => Game1.Date.TotalDays;

	/// <summary>接在人设后面的一段（NpcDirectory.Persona 调）；玩家整个改写过性格的不接。</summary>
	public string Tail(string npc, CharacterCard? card)
	{
		if (!On || card?.Replaces == true || Book?.Find(npc)?.Current is not VillagerGrowthEntry e)
		{
			return "";
		}
		return VillagerGrowthRules.PersonaTail(e, !string.IsNullOrWhiteSpace(card?.Speech));
	}

	/// <summary>角色卡页显示：最近一次写的样子。</summary>
	public VillagerGrowthEntry? Current(string npc) => On ? Book?.Find(npc)?.Current : null;

	public void OnSaveLoaded()
	{
		lock (_writing)
		{
			_writing.Clear();
		}
	}

	public void OnDayStarted()
	{
		if (!On || !Context.IsMainPlayer || Book is not VillagerGrowthBook b || Ai == null || !_config().AiOn)
		{
			return;
		}
		int today = Today;
		List<(VillagerGrowthFacts Facts, bool Major)> due = new List<(VillagerGrowthFacts, bool)>();
		foreach (NPC npc in Utility.getAllVillagers())
		{
			string name = npc.Name;
			if (!_npcs.IsEnabled(npc) || !npc.CanSocialize || _npcs.Cards.Get(name)?.Replaces == true)
			{
				continue;
			}
			VillagerGrowthFacts f = Facts(name);
			VillagerGrowth? g = b.Find(name);
			if (VillagerGrowthRules.Due(g, f, today))
			{
				due.Add((f, g == null || VillagerGrowthRules.Major(g.Mark, VillagerGrowthRules.Mark(f))));
			}
		}
		foreach ((VillagerGrowthFacts f, _) in due.OrderByDescending(d => d.Major).ThenByDescending(d => d.Facts.Hearts).Take(VillagerGrowthRules.PerDay))
		{
			Write(f, today);
		}
	}

	private VillagerGrowthFacts Facts(string npc)
	{
		int hearts = 0;
		string spouse = "";
		foreach (Farmer farmer in Game1.getAllFarmers())
		{
			if (farmer.friendshipData.TryGetValue(npc, out Friendship? fr))
			{
				hearts = Math.Max(hearts, fr.Points / NPC.friendshipPointsPerHeartLevel);
			}
			if (string.Equals(farmer.spouse, npc, StringComparison.OrdinalIgnoreCase))
			{
				spouse = farmer.Name;
			}
		}
		LifeBook? life = _life.Book;
		int goals = life?.Goals.Count(g => g.Outcome == "done" && string.Equals(g.Npc, npc, StringComparison.OrdinalIgnoreCase)) ?? 0;
		Couple? c = life?.Couples.LastOrDefault(x => x.Has(npc));
		string couple = c == null ? "" : c.Phase + ":" + c.Other(npc);
		string job = life?.JobOf(npc)?.Job ?? "";
		int turns = _social.Personality.Find(npc)?.TurningPoints.Count ?? 0;
		return new VillagerGrowthFacts(npc, hearts, spouse, goals, couple, job, turns, Game1.year);
	}

	/// <summary>给 AI 看的经历。</summary>
	private List<string> Story(VillagerGrowthFacts f)
	{
		List<string> lines = new List<string> { $"现在是第 {f.Year} 年{Game1.CurrentSeasonDisplayName}。" };
		foreach (Farmer farmer in Game1.getAllFarmers())
		{
			if (!farmer.friendshipData.TryGetValue(f.Npc, out Friendship? fr) || fr.Points < NPC.friendshipPointsPerHeartLevel)
			{
				continue;
			}
			int h = fr.Points / NPC.friendshipPointsPerHeartLevel;
			string status = fr.IsMarried() ? "，你们结婚了" : fr.IsEngaged() ? "，你们订婚了" : fr.IsDating() ? "，你们在交往" : fr.IsDivorced() ? "，你们离了婚" : "";
			lines.Add($"和农夫{farmer.Name}有 {h} 心的交情{status}。");
		}
		if (_life.Book is LifeBook life)
		{
			foreach (LifeGoal g in life.Goals.Where(g => string.Equals(g.Npc, f.Npc, StringComparison.OrdinalIgnoreCase)))
			{
				string goal = _life.Catalog.Get(g.Id)?.Goal ?? g.Id;
				lines.Add(g.Outcome == "done" ? $"做成了自己的心愿：{goal}{(g.Helped > 0 ? "，农夫帮过忙" : "")}。" : g.Active ? $"正在忙自己的心愿：{goal}。{g.Notes.LastOrDefault() ?? ""}" : $"放下了一个心愿：{goal}。");
			}
			if (life.Couples.LastOrDefault(x => x.Has(f.Npc)) is Couple c)
			{
				string other = _npcs.Names.Display(c.Other(f.Npc));
				lines.Add(c.Phase switch
				{
					CouplePhases.Dating => $"和{other}在交往。",
					CouplePhases.Engaged => $"和{other}订了婚。",
					CouplePhases.Married => $"和{other}结了婚。",
					_ => $"和{other}分手了。"
				});
			}
			if (life.JobOf(f.Npc) is JobChange j)
			{
				lines.Add($"换了工作，现在是：{j.Job}。");
			}
		}
		string evolution = _social.Personality.PromptBlock(f.Npc);
		if (evolution.Length > 0)
		{
			lines.Add("性格上慢慢起的变化：" + evolution);
		}
		return lines;
	}

	private void Write(VillagerGrowthFacts f, int today)
	{
		string npc = f.Npc;
		lock (_writing)
		{
			if (!_writing.Add(npc))
			{
				return;
			}
		}
		VillagerGrowthBook? book = Book;
		VillagerGrowth? old = book?.Find(npc);
		string card = _npcs.BasePersona(npc, Game1.getCharacterFromName(npc)).Personality;
		string ask = VillagerGrowthRules.Ask(_npcs.Names.Display(npc), card, old?.Current, Story(f), Language?.Invoke() ?? "中文");
		string mark = VillagerGrowthRules.Mark(f);
		IAiSender ai = Ai!;
		Task.Run(async () =>
		{
			VillagerGrowthEntry? entry = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("villager_growth", VillagerGrowthRules.System, ask, 2500, 0.8)).ConfigureAwait(false);
				entry = r.Ok ? VillagerGrowthRules.Parse(r.Text, today) : null;
			}
			catch (Exception ex)
			{
				_log.Debug($"{npc} 的成长没写成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				lock (_writing)
				{
					_writing.Remove(npc);
				}
				if (entry == null || !ReferenceEquals(book, Book) || book == null)
				{
					return;
				}
				VillagerGrowthRules.Add(book.Get(npc), entry, mark);
				_store()?.MarkDirty(VillagerGrowthBook.Path);
				_log.Info($"{npc} 长了一点：{entry.Now}（{entry.Why}）");
			});
		});
	}

	public const string Usage = "用法：aisv_growth list | show <村民> | run <村民> | clear <村民>";

	public string Command(string[] args)
	{
		if (Book is not VillagerGrowthBook b)
		{
			return "先进存档。";
		}
		string op = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
		string who = args.Length > 1 ? args[1] : "";
		switch (op)
		{
			case "list":
				return b.Villagers.Count == 0 ? "还没有村民长过。" : string.Join("\n", b.Villagers.Select(v => $"{v.Npc}：{v.History.Count} 次，最近第 {v.Current?.Day} 天 [{v.Mark}]"));
			case "show" when who.Length > 0:
				return b.Find(who) is VillagerGrowth g ? string.Join("\n", g.History.Select(e => $"第 {e.Day} 天：{e.Now}\n  说话：{e.Voice}\n  因为：{e.Why}")) : $"{who} 还没长过。";
			case "run" when who.Length > 0:
				if (Ai == null || !_config().AiOn)
				{
					return "AI 没开。";
				}
				VillagerGrowthFacts f = Facts(who);
				Write(f, Today);
				return $"在给 {who} 写（{VillagerGrowthRules.Mark(f)}），稍后用 show 看。";
			case "clear" when who.Length > 0:
				b.Villagers.RemoveAll(v => string.Equals(v.Npc, who, StringComparison.OrdinalIgnoreCase));
				_store()?.MarkDirty(VillagerGrowthBook.Path);
				return $"清掉了 {who} 的成长。";
			default:
				return Usage;
		}
	}
}
