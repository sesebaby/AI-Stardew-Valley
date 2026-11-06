using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Adult;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;
using AISV.Core.Social;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Adult;

/// <summary>
/// 成人事件记忆：装着的成人包里清单上的事件演完，给另一半记下"那天在哪里和农夫有过私密时刻"，以后聊天可以含蓄提起。
/// 只记识别信息，从不读原文。成人模块开着才记、才进提示词；孩子什么都不记，有孩子在场的只记"更亲近了"。
/// </summary>
public sealed class AdultMemoryService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly ILog _log;

	private AdultEvent? _playing;

	private string _playingLocation = "";

	private bool _playingWithChildren;

	public AdultMemoryService(Func<ModConfig> config, Func<DocumentStore?> store, ILog log)
	{
		_config = config;
		_store = store;
		_log = log;
	}

	public AdultCatalog Catalog { get; } = new AdultCatalog();

	public Func<string, string>? Display { get; set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.AdultModuleOn;
		}
	}

	private AdultMemoryLog? Log => _store()?.Get(AdultMemoryLog.Path, AdultMemoryLog.Doc);

	public IReadOnlyList<AdultMemory> Recorded => Log?.Recorded.ToList() ?? new List<AdultMemory>();

	/// <summary>游戏启动后读清单，按装了的 mod 过滤。</summary>
	public void LoadCatalog(string modDirectory, IModRegistry registry)
	{
		string path = Path.Combine(modDirectory, "assets", "adult", "adult_events.json");
		try
		{
			Catalog.Load(File.Exists(path) ? File.ReadAllText(path) : null);
		}
		catch (Exception ex)
		{
			_log.Warn("成人事件清单读不了：" + ex.Message);
			Catalog.Load(null);
		}
		Catalog.Activate(registry.IsLoaded);
		_log.Debug(Catalog.AnyActive ? $"成人事件清单：{string.Join("、", Catalog.ActivePacks)} 共 {Catalog.ActiveEvents.Count} 个事件。" : $"成人事件清单：{Catalog.Known.Count} 个包都没装。");
	}

	public void OnSaveLoaded() => _playing = null;

	public void OnReturnedToTitle() => _playing = null;

	/// <summary>每帧：清单上的事件开演时记下，演完（或跳过）时记录。</summary>
	public void OnUpdateTicked()
	{
		if (!Catalog.AnyActive || !Context.IsWorldReady)
		{
			return;
		}
		Event? ev = Game1.CurrentEvent;
		if (_playing == null)
		{
			if (!Game1.eventUp || ev == null || Catalog.Find(ev.id, Game1.currentLocation?.Name) is not AdultEvent hit)
			{
				return;
			}
			_playing = hit;
			_playingLocation = Game1.currentLocation?.Name ?? hit.Location;
			_playingWithChildren = hit.HasChildren || HasChildActor(ev);
			_log.Debug($"清单上的事件 {hit.Id}（{hit.Pack}）在 {_playingLocation} 开演。");
			return;
		}
		if (ev != null && Game1.eventUp && ev.id == _playing.Id)
		{
			_playingWithChildren |= HasChildActor(ev);
			return;
		}
		AdultEvent done = _playing;
		_playing = null;
		Remember(done, _playingLocation, _playingWithChildren);
	}

	private static bool HasChildActor(Event ev) => ev.actors?.Any(a => a != null && ChildSafety.IsChild(a.Name, a.Age)) ?? false;

	/// <summary>记给另一半。模块关着或记过就不记。返回记了几个人。</summary>
	public int Remember(AdultEvent e, string location, bool withChildren)
	{
		if (Log is not AdultMemoryLog log)
		{
			return 0;
		}
		if (!Enabled)
		{
			_log.Debug($"清单上的事件 {e.Id} 演完了；成人模块关着，不记。");
			return 0;
		}
		if (log.Recorded.Any(r => r.EventId == e.Id && r.Pack == e.Pack))
		{
			return 0;
		}
		List<string> partners = (e.Partners ?? new List<string>()).Where(IsAdultPartner).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (partners.Count == 0)
		{
			_log.Debug($"清单上的事件 {e.Id} 演完了；没有成年的另一半可记。");
			return 0;
		}
		log.Recorded.Add(new AdultMemory { EventId = e.Id, Pack = e.Pack, Location = location, Day = Game1.Date?.TotalDays ?? 0, WithChildren = withChildren, Partners = partners });
		_store()?.MarkDirty(AdultMemoryLog.Path);
		_log.Info($"清单上的事件 {e.Id}（{e.Pack}）演完了，给 {string.Join("、", partners)} 记下一段私密记忆。");
		return partners.Count;
	}

	private static bool IsAdultPartner(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || ChildSafety.IsListed(name) || Game1.characterData == null || !Game1.characterData.ContainsKey(name))
		{
			return false;
		}
		NPC? npc = Game1.getCharacterFromName(name);
		return npc != null && !ChildSafety.IsChild(name, npc.Age) && ChildSafety.AgeAllowsRomance(name, npc.Age, npc.datable.Value);
	}

	public string PromptBlock(string npc)
	{
		if (Log is not AdultMemoryLog log || !Enabled || string.IsNullOrEmpty(npc) || ChildSafety.IsListed(npc))
		{
			return "";
		}
		Func<string, string> display = Display ?? (n => n);
		return AdultMemoryRules.PromptBlock(log.Recorded.Where(r => r.Partners.Contains(npc, StringComparer.OrdinalIgnoreCase)).OrderByDescending(r => r.Day).Take(AdultMemoryRules.PromptLimit)
			.Select(r => AdultMemoryRules.Neutral(npc, r, DateLabel(r.Day), display)));
	}

	private static string DateLabel(int totalDays)
	{
		StardewModdingAPI.Utilities.SDate d = StardewModdingAPI.Utilities.SDate.FromDaysSinceStart(Math.Max(1, totalDays + 1));
		return Dates.Label(new AISV.Core.Dialogue.GameDate(d.Day, d.SeasonKey, d.Year));
	}

	public string Command(string[] args)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		switch (sub)
		{
			case "activate" when args.Length > 1:
			{
				string which = args[1].ToLowerInvariant();
				HashSet<string> ids = new HashSet<string>(args.Skip(1), StringComparer.OrdinalIgnoreCase);
				Catalog.Activate(id => which == "all" || (which != "none" && ids.Contains(id)));
				return Catalog.AnyActive ? $"生效：{string.Join("、", Catalog.ActivePacks)}（{Catalog.ActiveEvents.Count} 个事件）" : "一个包都没生效";
			}
			case "prompt" when args.Length > 1:
			{
				string block = PromptBlock(args[1]);
				return block.Length > 0 ? block : "（空）";
			}
			case "remember" when args.Length > 1:
			{
				AdultEvent? e = Catalog.ActiveEvents.FirstOrDefault(x => x.Id == args[1]);
				return e == null ? "清单上没有这个事件（先 activate）" : $"记了 {Remember(e, e.Location, e.HasChildren)} 个人";
			}
			case "clear":
				Log?.Recorded.Clear();
				_store()?.MarkDirty(AdultMemoryLog.Path);
				return "清空了";
			default:
				return $"模块：{(Enabled ? "开" : "关")}；清单 {Catalog.Known.Count} 个包，生效：{(Catalog.AnyActive ? string.Join("、", Catalog.ActivePacks) : "无")}；被换掉的心事件：{string.Join(",", Catalog.ActiveEvents.Where(e => e.ReplacesVanilla).Select(e => e.Id))}；记录 {Recorded.Count} 条：\n"
					+ string.Join("\n", Recorded.Select(r => $"  {r.EventId}（{r.Pack}）第 {r.Day} 天 {r.Location} {string.Join("、", r.Partners)}{(r.WithChildren ? " 有孩子" : "")}"));
		}
	}
}
