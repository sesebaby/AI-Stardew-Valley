using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Scenes;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Game.Dialogue;
using AISV.Game.Npcs;
using AISV.Game.Social;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.Game.Scenes;

/// <summary>
/// 场景事件：每天挑场景；过场风格下把场景注入事件表，其余交给偶遇（<see cref="MomentDirector"/>）；演完记录、记忆、八卦。
/// </summary>
public sealed class SceneService
{
	private const string EventsPrefix = "Data/Events/";

	private readonly Func<ModConfig> _config;

	private readonly IModHelper _helper;

	private readonly Relationships _relationships;

	private readonly NpcDirectory _npcs;

	private readonly ILog _log;

	private Dictionary<string, string> _starts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _forced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly object _catalogLock = new object();

	private SceneCatalog? _catalog;

	private bool _eventHooks;

	private bool? _plannedWithAi;

	public SceneService(Func<ModConfig> config, Func<DocumentStore?> store, IModHelper helper, Relationships relationships, NpcDirectory npcs, ILog log)
	{
		_config = config;
		_helper = helper;
		_relationships = relationships;
		_npcs = npcs;
		_log = log;
		Book = new SceneBook(store);
		RegisterEventHooks();
	}

	public SceneBook Book { get; }

	public SocialSystem? Social { get; set; }

	/// <summary>现在能不能请 AI 写台词。</summary>
	public Func<bool> AiAvailable { get; set; } = () => false;

	/// <summary>只有主机（或分屏的主屏）演偶遇。</summary>
	public static bool DeliversMomentsHere => Context.IsMainPlayer || Context.IsSplitScreen;

	public SceneCatalog Catalog
	{
		get
		{
			lock (_catalogLock)
			{
				if (_catalog == null)
				{
					string path = Path.Combine(_helper.DirectoryPath, "assets", "scenes", "scenes.json");
					_catalog = SceneCatalog.Load(File.Exists(path) ? File.ReadAllText(path) : null, _log);
					_log.Debug($"读了 {_catalog.Count} 个场景。");
				}
				return _catalog;
			}
		}
	}

	public string Style => SceneStyle.Normalize(_config().EncounterStyle);

	public SceneDelivery DeliveryOf(SceneDefinition d) => SceneCatalog.Delivery(d, Style);

	// ---------------- 每天挑场景 ----------------

	public void OnDayStarted()
	{
		_forced.Clear();
		ModConfig c = _config();
		SceneDay day = Today();
		if (!c.EncountersOn)
		{
			Invalidate(Book.ClearDay(day.Key));
			return;
		}
		SceneState state = Book.State;
		List<string> before = state.Loaded.ToList();
		bool ai = AiAvailable();
		if (!string.Equals(state.DayKey, day.Key, StringComparison.OrdinalIgnoreCase))
		{
			_plannedWithAi = ai;
		}
		Dictionary<string, (bool Found, bool Readable, string? Now, IReadOnlyList<ScheduleStop> Stops)> schedules = new Dictionary<string, (bool, bool, string?, IReadOnlyList<ScheduleStop>)>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> starts = StartLocations();
		_starts = starts;
		IReadOnlyList<string> armed = ScenePlanner.Plan(Catalog.All, state, day, Math.Clamp(c.EncountersDaily, 0, 5), d => Deliverable(d, ai, schedules, starts));
		Book.Changed();
		Invalidate(before.Concat(armed));
		_log.Debug($"今天挑的场景：{(armed.Count == 0 ? "没有" : string.Join("、", armed))}。");
	}

	/// <summary>今天演不演得出来：过场总可以；偶遇看说话的人今天会不会在那里。</summary>
	private bool Deliverable(SceneDefinition d, bool ai, Dictionary<string, (bool Found, bool Readable, string? Now, IReadOnlyList<ScheduleStop> Stops)> schedules, IReadOnlyDictionary<string, string> starts)
	{
		try
		{
			if (DeliveryOf(d) == SceneDelivery.Cinematic)
			{
				return true;
			}
			if (!DeliversMomentsHere)
			{
				return false;
			}
			(IReadOnlyList<string> speakers, _) = Scope(d, ai);
			foreach (string speaker in speakers)
			{
				if (!schedules.TryGetValue(speaker, out var reading))
				{
					reading = schedules[speaker] = ScheduleReader.Read(speaker);
				}
				if (!Expected(d, reading, starts.GetValueOrDefault(speaker)))
				{
					return false;
				}
			}
			return speakers.Count > 0;
		}
		catch (Exception ex)
		{
			_log.Debug($"场景 {d.Id} 的日程看不了，先当能演：{ex.Message}");
			return true;
		}
	}

	private (IReadOnlyList<string> Speakers, string Reason) Scope(SceneDefinition d, bool ai)
	{
		bool leadEnabled = d.Speakers.Count == 0 || _npcs.IsEnabled(d.Lead);
		return MomentRules.ForecastScope(d.Speakers, d.Lines, ai && leadEnabled);
	}

	private static bool Expected(SceneDefinition d, (bool Found, bool Readable, string? Now, IReadOnlyList<ScheduleStop> Stops) reading, string? start)
	{
		if (!reading.Found)
		{
			return false;
		}
		return !reading.Readable || MomentRules.ExpectedAt(start ?? reading.Now, reading.Stops, d.Place, d.WindowMin, d.WindowMax);
	}

	private static Dictionary<string, string> StartLocations()
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (GameLocation location in Game1.locations)
		{
			foreach (NPC npc in location.characters)
			{
				if (npc != null)
				{
					map[npc.Name] = location.Name;
				}
			}
		}
		return map;
	}

	private SceneDay Today()
	{
		Dictionary<string, int> hearts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (string npc in Catalog.All.SelectMany(d => d.Needs.HeartsAtLeast.Keys.Concat(d.Needs.HeartsAtMost.Keys)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (Game1.player?.friendshipData.ContainsKey(npc) == true)
			{
				hearts[npc] = _relationships.EffectiveHearts(npc);
			}
		}
		GameDate today = GameState.Today;
		string weather = Game1.isLightning ? "Stormy" : Game1.isRaining ? "Rainy" : Game1.isSnowing ? "Snowy" : "Sunny";
		return new SceneDay(today.Year, today.Season, today.Day, today.TotalDays, weather, Game1.uniqueIDForThisGame.ToString(), Constants.SaveFolderName ?? "", hearts);
	}

	// ---------------- 挑中的场景 ----------------

	public bool IsArmed(string id)
	{
		if (!_config().EncountersOn)
		{
			return false;
		}
		SceneState s = Book.State;
		if (s.PlayedToday.Contains(id, StringComparer.OrdinalIgnoreCase) || Catalog.Find(id) is not SceneDefinition d)
		{
			return false;
		}
		if (!_forced.Contains(id) && d.Repeats > 0 && s.TimesPlayed.GetValueOrDefault(id) >= d.Repeats)
		{
			return false;
		}
		return s.Loaded.Contains(id, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>这张地图上今天挑中、要按偶遇演的场景。只在主机上有。</summary>
	public IReadOnlyList<(SceneDefinition Scene, SceneDelivery Delivery)> ArmedMoments(string? location)
	{
		if (!Context.IsMainPlayer || string.IsNullOrWhiteSpace(location))
		{
			return Array.Empty<(SceneDefinition, SceneDelivery)>();
		}
		return Catalog.At(location).Where(d => IsArmed(d.Id)).Select(d => (d, DeliveryOf(d))).Where(x => x.Item2 != SceneDelivery.Cinematic).ToList();
	}

	public void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
	{
		string name = e.NameWithoutLocale.BaseName;
		if (!name.StartsWith(EventsPrefix, StringComparison.OrdinalIgnoreCase) || Style != SceneStyle.Cinematic)
		{
			return;
		}
		string location = name[EventsPrefix.Length..];
		IReadOnlyList<SceneDefinition> here = Catalog.At(location);
		if (here.Count == 0)
		{
			return;
		}
		e.Edit(asset =>
		{
			IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
			foreach (SceneDefinition d in here)
			{
				data[SceneCatalog.EventKey(d)] = SceneCatalog.Script(d);
			}
		}, AssetEditPriority.Late);
	}

	/// <summary>风格变了：所有有场景的地图的事件表重新加载。</summary>
	public void OnStyleChanged()
	{
		foreach (string location in Catalog.Locations)
		{
			InvalidateLocation(location);
		}
	}

	// ---------------- 演完 ----------------

	/// <summary>记一次演完：今天演过、次数、冷却；场景要记忆时每个说话的人记一条，加一条当天互动和低调八卦。</summary>
	public void Complete(string id, IReadOnlyList<SceneLine>? played = null)
	{
		SceneDefinition? d = Catalog.Find(id);
		if (d == null)
		{
			return;
		}
		Book.Complete(d, GameState.Today.TotalDays);
		InvalidateLocation(d.Place);
		_log.Debug($"场景演完了：{d.Id}。");
		if (!d.RecordsMemory || !_config().EncountersRemembered || Social == null)
		{
			return;
		}
		try
		{
			SceneMemory? m = SceneMemory.Build(d, played ?? d.Lines, n => _npcs.Names.Display(n));
			if (m == null)
			{
				return;
			}
			GameDate today = GameState.Today;
			foreach (string npc in m.Participants)
			{
				Social.Feelings.AddKeyMemory(npc, m.Text, today);
			}
			Social.Gossip.RecordInteraction(m.ToInteraction(Game1.player?.Name ?? ""));
			Social.Gossip.AddLowKey(m.Text, m.Participants, "scene_event", d.Id, today);
			_log.Debug($"场景 {d.Id} 记进了 {string.Join("、", m.Participants)} 的记忆：{m.Text}");
		}
		catch (Exception ex)
		{
			_log.Warn($"场景 {d.Id} 的记忆没记上：{ex.Message}");
		}
	}

	// ---------------- 调试 ----------------

	public string Arm(string id, out bool ok)
	{
		SceneDefinition? d = Catalog.Find(id);
		ok = d != null;
		if (d == null)
		{
			return $"没有叫 {id} 的场景。";
		}
		Book.Arm(d.Id);
		_forced.Add(d.Id);
		InvalidateLocation(d.Place);
		SceneDelivery delivery = DeliveryOf(d);
		string text = $"今天挑上了 {d.Id}（{d.Name}）：{SceneCatalog.DeliveryName(delivery)}，{d.Place}，{d.WindowMin}-{d.WindowMax}。";
		if (!_config().EncountersOn)
		{
			text += "偶遇在设置里关着，不会演。";
		}
		else if (delivery != SceneDelivery.Cinematic && !DeliversMomentsHere)
		{
			text += "走过来和旁听只在主机上演，这里不会演。";
		}
		return text;
	}

	public IReadOnlyList<string> Describe()
	{
		SceneState s = Book.State;
		string style = Style;
		List<string> lines = new List<string> { $"偶遇{(_config().EncountersOn ? "开着" : "关着")}，风格 {style}，今天 {(s.DayKey.Length == 0 ? "还没挑" : s.DayKey)}，挑了 {s.Loaded.Count} 个。" };
		if (!DeliversMomentsHere && style != SceneStyle.Cinematic)
		{
			lines.Add("走过来和旁听只在主机上演：这里既不挑也不演。");
		}
		bool ai = AiAvailable();
		bool planned = _plannedWithAi ?? ai;
		if (planned != ai)
		{
			lines.Add($"挑场景时{(planned ? "能" : "不能")}请 AI 写台词，现在{(ai ? "能" : "不能")}。");
		}
		foreach (string id in s.Loaded)
		{
			SceneDefinition? d = Catalog.Find(id);
			if (d == null)
			{
				lines.Add($"- {id}：目录里已经没有了。");
				continue;
			}
			SceneDelivery delivery = DeliveryOf(d);
			bool hostOnly = delivery != SceneDelivery.Cinematic && !DeliversMomentsHere;
			string status = s.PlayedToday.Contains(id, StringComparer.OrdinalIgnoreCase) ? "今天演过了" : hostOnly ? "只在主机上演" : IsArmed(id) ? "等着" : "挡住了（关了或次数满了）";
			lines.Add($"- {id}（{d.Name}）：{SceneCatalog.DeliveryName(delivery)}，{d.Place} {d.WindowMin}-{d.WindowMax}，{status}，说话的人 {string.Join("、", d.Speakers)}。");
			if (delivery != SceneDelivery.Cinematic && !hostOnly)
			{
				lines.AddRange(Forecast(d, planned).Select(l => "  " + l));
			}
		}
		return lines;
	}

	private IEnumerable<string> Forecast(SceneDefinition d, bool ai)
	{
		(IReadOnlyList<string> speakers, string reason) = Scope(d, ai);
		if (speakers.Count == 0)
		{
			yield return $"预测：没人能演（{reason}）。";
			yield break;
		}
		yield return $"预测要看 {string.Join("、", speakers)}：{reason}。";
		foreach (string speaker in speakers)
		{
			var r = ScheduleReader.Read(speaker);
			if (!r.Found)
			{
				yield return $"{speaker} 今天不在世界里。";
				continue;
			}
			if (!r.Readable)
			{
				yield return $"{speaker} 的日程读不出来，当会来（现在在 {r.Now ?? "?"}）。";
				continue;
			}
			string start = _starts.GetValueOrDefault(speaker) ?? r.Now ?? "?";
			string route = string.Join("，", new[] { $"6:00 {start}" }.Concat(r.Stops.Select(x => $"{x.Time} {x.Location}")));
			bool expected = Expected(d, r, start);
			yield return $"{speaker} {(expected ? "会" : "不会")}在时间窗口里到 {d.Place}（路线：{route}；现在在 {r.Now ?? "?"}）。";
		}
	}

	// ---------------- 事件条件和命令 ----------------

	private void RegisterEventHooks()
	{
		if (_eventHooks)
		{
			return;
		}
		_eventHooks = true;
		Event.RegisterPrecondition(SceneCatalog.ArmedCondition, (_, _, args) =>
		{
			string id = Arg(args, SceneCatalog.ArmedCondition, 0);
			bool armed = id.Length > 0 && IsArmed(id);
			_log.Trace($"场景条件：{id} 今天挑中 {armed}。");
			return armed;
		});
		Event.RegisterPrecondition(SceneCatalog.TimeCondition, (_, _, args) =>
		{
			bool inWindow = int.TryParse(Arg(args, SceneCatalog.TimeCondition, 0), out int min) && int.TryParse(Arg(args, SceneCatalog.TimeCondition, 1), out int max) && Game1.timeOfDay >= min && Game1.timeOfDay <= max;
			_log.Trace($"场景条件：时间窗口 {string.Join(" ", args)}，现在 {Game1.timeOfDay}，{inWindow}。");
			return inWindow;
		});
		Event.RegisterCommand(SceneCatalog.DoneCommand, (Event ev, string[] args, EventContext _) =>
		{
			try
			{
				string id = Arg(args, SceneCatalog.DoneCommand, 0);
				if (id.Length > 0)
				{
					Complete(id);
				}
			}
			catch (Exception ex)
			{
				_log.Warn("场景演完后的记录出错：" + ex.Message);
			}
			ev.CurrentCommand++;
		});
	}

	private static string Arg(string[] args, string command, int index)
	{
		int skip = args.Length > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
		return skip + index < args.Length ? args[skip + index] : "";
	}

	private void Invalidate(IEnumerable<string> ids)
	{
		foreach (string location in ids.Select(id => Catalog.Find(id)?.Place ?? "").Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			InvalidateLocation(location);
		}
	}

	private void InvalidateLocation(string location)
	{
		try
		{
			_helper.GameContent.InvalidateAllLocales(EventsPrefix + location);
		}
		catch (Exception ex)
		{
			_log.Warn($"{location} 的事件表没能重新加载：{ex.Message}");
		}
	}
}
