using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Companions;
using AISV.Core.Dialogue;
using AISV.Core.Diagnostics;
using AISV.Core.Favors;
using AISV.Core.Life;
using AISV.Core.Persona;
using AISV.Core.Social.Diary;
using AISV.Core.Social.Personality;
using AISV.Core.Social.Web;
using AISV.Core.Storage;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Core.World.Ledger;
using AISV.Game.Companions;
using AISV.Game.Dialogue;
using AISV.Game.Favors;
using AISV.Game.Npcs;
using AISV.Game.Social;
using AISV.Game.Stories;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace AISV.Game.Life;

/// <summary>
/// 村民自己的人生：每周推进一次（只在房主那边）。心愿一步步往前走（走到某一步托你帮忙，达成时演一段、可能换工作），
/// 暗生好感的两人开始交往、约会、订婚、办婚礼，也可能分手。都写进八卦、周报、日记和聊天素材。
/// </summary>
public sealed class LifeService
{
	public const string DateOwner = "life_date";

	public const string PropPrefix = "Mods/AIStardewValley.Core/Life/";

	/// <summary>物品请求的 StageRef 前缀：认出是哪个心愿托的。</summary>
	public const string HelpTag = "life:";

	private static readonly Random Rng = new Random();

	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly IModHelper _helper;

	private readonly SocialSystem _social;

	private readonly NpcDirectory _npcs;

	private readonly ScheduleOverlayService _schedules;

	private readonly FavorService _favors;

	private readonly ILog _log;

	private readonly string _catalogPath;

	private LifeCatalog? _catalog;

	/// <summary>结局戏或婚礼正在演（免得一进门又排一场）。</summary>
	private bool _playing;

	public LifeService(Func<ModConfig> config, Func<DocumentStore?> store, IModHelper helper, SocialSystem social, NpcDirectory npcs, ScheduleOverlayService schedules, FavorService favors, ILog log)
	{
		_config = config;
		_store = store;
		_helper = helper;
		_social = social;
		_npcs = npcs;
		_schedules = schedules;
		_favors = favors;
		_log = log;
		_catalogPath = Path.Combine(helper.DirectoryPath, "assets", "life", "goals.json");
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	public Func<string, string>? Persona { get; set; }

	public Func<string, string> Display { get; set; } = n => n;

	public LifeCatalog Catalog
	{
		get
		{
			if (_catalog == null)
			{
				try
				{
					_catalog = LifeCatalog.Parse(File.ReadAllText(_catalogPath));
					foreach (string p in _catalog.Problems)
					{
						_log.Warn("村民心愿目录：" + p);
					}
				}
				catch (Exception ex)
				{
					_log.Warn("村民心愿目录读不出来：" + ex.Message);
					_catalog = LifeCatalog.Empty;
				}
			}
			return _catalog;
		}
	}

	public LifeBook? Book => _store()?.Get(LifeBook.Path, LifeBook.Doc);

	private void Dirty() => _store()?.MarkDirty(LifeBook.Path);

	private bool On => _config().ModOn && _config().LifeOn;

	private bool CouplesOn => On && _config().VillagerCouplesOn;

	private static int Today => Game1.Date.TotalDays;

	// ---------------- 给别的功能问 ----------------

	public bool IsWed(string npc) => On && Book?.IsWed(npc) == true;

	public bool IsMarried(string a, string b) => On && Book?.Couples.Any(c => c.Phase == CouplePhases.Married && c.Has(a) && c.Has(b)) == true;

	public bool IsCouple(string a, string b) => On && Book?.IsCouple(a, b) == true;

	/// <summary>账本问这位村民现在做什么工作。</summary>
	public Occupation JobFor(string npc, Occupation job) => On && _config().LifeJobs && Book?.JobOf(npc) is JobChange j ? job with { Job = j.Job, Wage = j.Wage > 0 ? j.Wage : job.Wage } : job;

	/// <summary>家谱：订了婚、结了婚的（只是在交往的不进家谱，关系网里看得到）。</summary>
	public IEnumerable<(string, string, string)> Declared() => !On || Book is not LifeBook b ? Enumerable.Empty<(string, string, string)>()
		: b.Couples.Where(c => c.Phase is CouplePhases.Engaged or CouplePhases.Married).Select(c => (c.A, c.B, c.Phase == CouplePhases.Married ? "spouse" : "fiance"));

	/// <summary>心愿达成以后才夹进日记的画（排在这位村民平常那幅前面）。</summary>
	public IReadOnlyList<DiarySketch> Sketches()
	{
		if (!On || Book is not LifeBook b)
		{
			return DiarySketches.All;
		}
		List<DiarySketch> mine = b.Goals.Where(g => g.Outcome == "done").Select(g => (g, def: Catalog.Get(g.Id)))
			.Where(x => x.def != null && x.def.Ending.Art.Length > 0 && File.Exists(Path.Combine(_helper.DirectoryPath, "assets", "diary", "life", x.def.Ending.Art + ".png")))
			.Select(x => new DiarySketch("life/" + x.def!.Ending.Art + ".png", x.g.Npc, x.g.EndedDay)).ToList();
		return mine.Concat(DiarySketches.All).ToList();
	}

	/// <summary>聊天素材：自己的心愿、感情、新工作。</summary>
	public string PromptBlock(string npc)
	{
		if (!On || Book is not LifeBook b)
		{
			return "";
		}
		List<string> lines = new List<string>();
		int today = Today;
		if (b.Active(npc) is LifeGoal g && Catalog.Get(g.Id) is LifeGoalDef def)
		{
			string last = g.Notes.LastOrDefault() ?? "";
			lines.Add(Tr.Get("life.prompt.goal", "你最近在忙一件自己的事：{{goal}}。{{progress}}", ("goal", def.Goal), ("progress", last.Length > 0 ? Tr.Get("life.prompt.progress", "最新进展：{{note}}", ("note", last)) : Tr.Get("life.prompt.justStarted", "刚开始。"))));
			if (g.HelpRequest.Length > 0 && def.Help != null && _favors.Requests.All.Any(r => r.Id == g.HelpRequest && r.IsActive))
			{
				lines.Add(Tr.Get("life.prompt.waiting", "你托了{{farmer}}帮你找{{item}}，还在等。", ("farmer", Game1.player?.Name ?? "Farmer"), ("item", ItemName(def.Help.Item))));
			}
		}
		foreach (LifeGoal done in b.Goals.Where(x => x.Outcome == "done" && string.Equals(x.Npc, npc, StringComparison.OrdinalIgnoreCase) && today - x.EndedDay <= 28))
		{
			if (Catalog.Get(done.Id) is LifeGoalDef d)
			{
				lines.Add(Tr.Get("life.prompt.done", "你前不久做成了一件大事：{{goal}}。{{helped}}", ("goal", d.Goal), ("helped", done.Helped > 0 ? Tr.Get("life.prompt.helped", "{{farmer}}帮过你。", ("farmer", Game1.player?.Name ?? "Farmer")) : "")));
			}
		}
		if (b.Couples.FirstOrDefault(c => c.Has(npc) && (c.Together || today - c.Since <= 28)) is Couple c)
		{
			string other = Display(c.Other(npc));
			lines.Add(c.Phase switch
			{
				CouplePhases.Dating => Tr.Get("life.prompt.dating", "你和{{other}}在交往，镇上的人都知道了。", ("other", other)),
				CouplePhases.Engaged => Tr.Get("life.prompt.engaged", "你和{{other}}订婚了，婚礼在 {{days}} 天后，在镇广场办。", ("other", other), ("days", Math.Max(0, c.WeddingDay - today))),
				CouplePhases.Married => Tr.Get("life.prompt.married", "你和{{other}}结婚了（婚后照旧住在各自原来的地方）。", ("other", other)),
				_ => Tr.Get("life.prompt.brokenUp", "你和{{other}}前不久分手了，提起来还有点难受。", ("other", other))
			});
		}
		if (_config().LifeJobs && b.JobOf(npc) is JobChange j)
		{
			lines.Add(Tr.Get("life.prompt.job", "你现在的工作：{{job}}。", ("job", j.Job)));
		}
		return string.Join("\n", lines.Select(l => "- " + l));
	}

	// ---------------- 生命周期 ----------------

	public void OnSaveLoaded()
	{
		_playing = false;
		if (Book is LifeBook b)
		{
			_log.Debug($"村民人生：心愿 {b.Goals.Count(g => g.Active)} 个在进行、{b.Goals.Count(g => g.Outcome == "done")} 个达成，{b.Couples.Count(c => c.Together)} 对在一起，换了工作的 {b.Jobs.Count} 人。");
		}
	}

	public void OnDayStarted()
	{
		_playing = false;
		if (!On || !Context.IsMainPlayer || Book is not LifeBook b)
		{
			return;
		}
		int today = Today;
		if (CouplesOn)
		{
			ClaimedByFarmers(b, today);
			Weddings(b, today);
			Dates(b, today);
		}
		if (LifeRules.Due(b, today))
		{
			Tick(b, today);
		}
	}

	/// <summary>到了结局戏或婚礼的地方：开演。</summary>
	public void OnWarped(WarpedEventArgs e)
	{
		if (!e.IsLocalPlayer || !On || _playing || Book is not LifeBook b || Game1.eventUp)
		{
			return;
		}
		string here = e.NewLocation?.Name ?? "";
		int today = Today;
		if (CouplesOn && _config().LifeWeddings && string.Equals(here, "Town", StringComparison.OrdinalIgnoreCase) && Game1.timeOfDay >= 900 && Game1.timeOfDay < 1600
			&& b.Couples.FirstOrDefault(c => c.Phase == CouplePhases.Married && c.WeddingDay == today && !c.WeddingShown) is Couple wed)
		{
			PlayWedding(b, wed);
			return;
		}
		foreach (LifeGoal g in b.Goals.Where(g => g.Outcome == "done" && !g.EndingShown))
		{
			if (Catalog.Get(g.Id) is LifeGoalDef def && LifeRules.EndingDue(g, def, here, today, Game1.timeOfDay))
			{
				PlayEnding(g, def);
				return;
			}
		}
	}

	/// <summary>托你找的东西送到了。</summary>
	public void OnRequestCompleted(ItemRequestFavor f)
	{
		if (!f.StageRef.StartsWith(HelpTag, StringComparison.Ordinal) || Book is not LifeBook b)
		{
			return;
		}
		string id = f.StageRef[HelpTag.Length..];
		if (b.Goals.FirstOrDefault(g => g.Id == id && g.HelpRequest == f.Id) is LifeGoal g)
		{
			g.Helped++;
			Dirty();
			_log.Info($"{g.Npc} 的心愿「{id}」：你帮上忙了。");
		}
	}

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		string name = e.NameWithoutLocale.BaseName.Replace('\\', '/');
		if (!name.StartsWith(PropPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string file = Path.Combine("assets", "life", "props", name[PropPrefix.Length..] + ".png");
		if (File.Exists(Path.Combine(_helper.DirectoryPath, file)))
		{
			e.LoadFromModFile<Texture2D>(file, AssetLoadPriority.Exclusive);
		}
	}

	// ---------------- 每周 ----------------

	private void Tick(LifeBook b, int today)
	{
		// 第一次推进时把空位都填上，以后每周最多开一个新的。
		int starts = b.LastTick < 0 ? LifeRules.MaxGoals : 1;
		b.LastTick = today;
		Dirty();
		foreach (LifeGoal g in b.Goals.Where(g => g.Active).ToList())
		{
			Advance(b, g, today);
		}
		for (int i = 0; i < starts && StartGoal(b, today); i++)
		{
		}
		if (CouplesOn)
		{
			Romance(b, today);
		}
	}

	private bool Available(string npc) => _npcs.IsEnabled(npc) && Game1.getCharacterFromName(npc) is NPC n && n.CanSocialize && SocialWeb.PresentInTown(npc, Game1.year);

	private void Advance(LifeBook b, LifeGoal g, int today, bool force = false)
	{
		if (Catalog.Get(g.Id) is not LifeGoalDef def)
		{
			g.Outcome = "dropped";
			g.EndedDay = today;
			return;
		}
		if (!Available(g.Npc))
		{
			return;
		}
		bool waiting = g.HelpRequest.Length > 0 && _favors.Requests.All.Any(r => r.Id == g.HelpRequest && r.IsActive);
		double chance = LifeRules.StepChance(g, _social.Moods.Get(g.Npc)?.Level ?? 0, CloseFriends(g.Npc), waiting);
		if (!force && !LifeRules.Steps(g, chance, Rng.NextDouble(), today))
		{
			return;
		}
		bool done = LifeRules.Advance(g, def, today);
		string step = def.Steps[g.Stage - 1];
		Narrate(g, def, step, done);
		if (done)
		{
			Finished(b, g, def, today);
		}
		else if (LifeRules.WantsHelp(g, def))
		{
			AskForHelp(g, def);
		}
		Dirty();
	}

	private int CloseFriends(string npc) => _social.Web.Edges.Count(e => (string.Equals(e.First, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Second, npc, StringComparison.OrdinalIgnoreCase)) && e.Closeness >= 0.5f && e.Liking >= 0.4f);

	private bool StartGoal(LifeBook b, int today)
	{
		int max = Math.Clamp(_config().LifeGoalsAtOnce, 1, LifeRules.MaxGoals);
		if (b.Goals.Count(g => g.Active) >= max)
		{
			return false;
		}
		List<LifeGoalDef> open = LifeRules.Startable(Catalog, b, Available, Game1.year);
		if (open.Count == 0)
		{
			return false;
		}
		LifeGoalDef def = open[Rng.Next(open.Count)];
		LifeGoal g = new LifeGoal { Id = def.Id, Npc = def.Npc, StartedDay = today, LastStepDay = today };
		b.Goals.Add(g);
		Dirty();
		_log.Info($"村民人生：{def.Npc} 有了新心愿「{def.Goal}」。");
		Gossip(Tr.Get("life.gossip.start", "听说{{name}}想{{goal}}，已经开始张罗了。", ("name", Display(def.Npc)), ("goal", def.Goal)), def.Npc);
		return true;
	}

	private void AskForHelp(LifeGoal g, LifeGoalDef def)
	{
		LifeHelp h = def.Help!;
		ItemRequestFavor? f = _favors.AskFor(g.Npc, h.Item, h.Amount, h.Ask, h.Thanks, HelpTag + g.Id);
		if (f != null)
		{
			g.HelpRequest = f.Id;
		}
	}

	/// <summary>这一步写成一句话：AI 写，没写成就用目录里的那步。</summary>
	private void Narrate(LifeGoal g, LifeGoalDef def, string step, bool done)
	{
		string fallback = Tr.Get("life.note", "{{name}}{{step}}。", ("name", Display(g.Npc)), ("step", step));
		void Record(string note)
		{
			g.Notes.Add(note);
			while (g.Notes.Count > 8)
			{
				g.Notes.RemoveAt(0);
			}
			Dirty();
			Gossip(note, g.Npc);
			_log.Info($"村民人生：{note}");
		}
		if (Ai is not IAiSender ai || !_config().AiOn)
		{
			Record(fallback);
			return;
		}
		string system = Tr.Get("life.notePrompt", "这次只用{{language}}回答。\n\n你在给星露谷小镇的周报写一句简讯。{{name}}的心愿：{{goal}}。\n这一周的进展：{{step}}{{done}}\n{{persona}}\n\n用一句话写出这件事（第三人称，具体、有生活气息、带一点这个人的性格，不超过 40 个字）。只输出这句话，不加引号。",
			("language", Language?.Invoke() ?? "中文"), ("name", Display(g.Npc)), ("goal", def.Goal), ("step", step), ("done", done ? Tr.Get("life.notePromptDone", "（心愿就此达成）") : ""),
			("persona", Persona?.Invoke(g.Npc) is string p && p.Length > 0 ? Tr.Get("life.notePromptPersona", "这个人：{{p}}", ("p", TextTools.Truncate(p, 300))) : ""));
		LifeBook? book = Book;
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("life_step", system, Tr.Get("life.notePromptGo", "写。"), 300, 0.9)).ConfigureAwait(false);
				line = r.Ok ? Clean(r.Text, 60) : null;
			}
			catch (Exception ex)
			{
				_log.Debug("心愿进展没写成：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				if (ReferenceEquals(book, Book))
				{
					Record(line ?? fallback);
				}
			});
		});
	}

	private void Finished(LifeBook b, LifeGoal g, LifeGoalDef def, int today)
	{
		_log.Info($"村民人生：{def.Npc} 的心愿「{def.Goal}」达成了。");
		if (_config().LifeJobs && def.NewJob.Length > 0)
		{
			b.Jobs.RemoveAll(j => string.Equals(j.Npc, def.Npc, StringComparison.OrdinalIgnoreCase));
			b.Jobs.Add(new JobChange { Npc = def.Npc, Job = def.NewJob, Wage = def.NewWage, Since = today });
		}
		_social.Moods.Adjust(def.Npc, 25);
		string place = EndingPlace(def.Ending.Location);
		(int from, int to) = LifeRules.EndingHours(def.Ending.Location);
		_social.PostStoryLetter(def.Npc, HelpTag + def.Id,
			Tr.Get("life.inviteSubject", "{{goal}}", ("goal", def.Goal)),
			Tr.Get("life.inviteBody", "我一直想{{goal}}，现在终于成了！{{thanks}}这一周里，{{from}}到{{to}}之间来{{place}}吧，我想让你也在场。", ("goal", def.Goal), ("thanks", g.Helped > 0 ? Tr.Get("life.inviteThanks", "多亏了你帮忙。") : ""),
				("from", Clock(from)), ("to", Clock(to)), ("place", place)), null);
		WriteEnding(g, def);
	}

	private static string Clock(int t) => $"{t / 100 % 24}:{t % 100:00}";

	/// <summary>结局戏在哪（镇上的都在广场）。</summary>
	private static string EndingPlace(string location) => string.Equals(location, "Town", StringComparison.OrdinalIgnoreCase) ? Tr.Get("life.place.square", "镇广场") : Places.Name(location);

	/// <summary>结局台词：AI 按到场的人写，没写成就用目录里的。</summary>
	private void WriteEnding(LifeGoal g, LifeGoalDef def)
	{
		List<string> cast = def.Ending.Cast.Where(Available).ToList();
		if (Ai is not IAiSender ai || !_config().AiOn || cast.Count == 0)
		{
			return;
		}
		string people = string.Join("\n", cast.Select(n => $"- {n}（{Display(n)}）：{TextTools.Truncate(Persona?.Invoke(n) ?? "", 160)}"));
		string system = Tr.Get("life.endingPrompt", "这次只用{{language}}回答。\n\n星露谷小镇上，{{name}}的心愿达成了：{{goal}}。\n一路走来：{{notes}}\n{{helped}}\n\n在{{place}}演一小段，到场的人：\n{{people}}\n\n写 4～6 句台词，第一句是{{name}}，每人说话符合自己的性格，温暖、具体，每句不超过 40 个字，不写动作。\n只输出 JSON：{\"lines\":[{\"who\":\"英文名\",\"line\":\"说的话\",\"feeling\":\"happy|surprised|sad|angry|neutral\"}]}",
			("language", Language?.Invoke() ?? "中文"), ("name", Display(g.Npc)), ("goal", def.Goal), ("notes", string.Join(" ", g.Notes.TakeLast(4))), ("place", EndingPlace(def.Ending.Location)),
			("helped", g.Helped > 0 ? Tr.Get("life.endingPromptHelped", "农夫{{farmer}}帮过忙，也在场。", ("farmer", Game1.player?.Name ?? "Farmer")) : ""), ("people", people));
		LifeBook? book = Book;
		Task.Run(async () =>
		{
			List<string> lines = new List<string>();
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("life_ending", system, Tr.Get("life.notePromptGo", "写。"), 900, 0.9)).ConfigureAwait(false);
				lines = r.Ok ? SceneLines(r.Text ?? "", cast) : lines;
			}
			catch (Exception ex)
			{
				_log.Debug("心愿结局台词没写成：" + ex.Message);
			}
			MainThread.Run(() =>
			{
				if (ReferenceEquals(book, Book) && lines.Count >= 2)
				{
					g.Scene = lines;
					Dirty();
				}
			});
		});
	}

	private static List<string> SceneLines(string text, IReadOnlyCollection<string> cast)
	{
		int a = text.IndexOf('{'), z = text.LastIndexOf('}');
		if (a < 0 || z <= a)
		{
			return new List<string>();
		}
		using JsonDocument doc = JsonDocument.Parse(text[a..(z + 1)]);
		if (!doc.RootElement.TryGetProperty("lines", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
		{
			return new List<string>();
		}
		List<string> raw = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
			.Select(e => $"{Str(e, "who")}|{Clean(Str(e, "line"), 60)?.Replace("|", " ")}|{Str(e, "feeling")}").Take(6).ToList();
		return LifeRules.Lines(raw, cast).Select(l => $"{l.Who}|{l.Line}|{l.Feeling}").ToList();
	}

	private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static string? Clean(string? text, int max)
	{
		string t = TextTools.CleanForGame(TextTools.Truncate((text ?? "").Trim().Trim('"', '“', '”'), max));
		return string.IsNullOrWhiteSpace(t) ? null : t;
	}

	private void Gossip(string text, params string[] npcs)
	{
		GameDate d = GameState.Today;
		_social.Gossip.AddVillageMoment(d.Day, d.Season, d.Year, text, text, npcs.Where(n => n.Length > 0).ToList());
	}

	private static string ItemName(string id) => ItemRegistry.GetData(id)?.DisplayName ?? id;

	// ---------------- 结局戏 ----------------

	private void PlayEnding(LifeGoal g, LifeGoalDef def)
	{
		List<string> cast = def.Ending.Cast.Where(n => Game1.getCharacterFromName(n) != null).ToList();
		List<ConclusionLine> lines = LifeRules.Lines(g.Scene.Count > 0 ? g.Scene : def.Ending.Lines, cast);
		if (lines.Count == 0)
		{
			g.EndingShown = true;
			Dirty();
			return;
		}
		(int hx, int hy) = def.Ending.X > 0 ? (def.Ending.X, def.Ending.Y) : LifeRules.Stage(def.Ending.Location);
		List<string> speakers = lines.Select(l => l.Who).Distinct(StringComparer.OrdinalIgnoreCase).Take(ConclusionScript.MaxSpeakers).ToList();
		ConclusionScript.Layout layout = LifeRules.EndingLayout(hx, hy, speakers, def.Npc);
		string prelude = "";
		(string Asset, int X, int Y, int W, int H)? prop = def.Ending.Prop.Length == 0 ? null
			: LifeRules.VanillaProp(def.Ending.Prop) ?? (PropSize(def.Ending.Prop) is (int pw, int ph) ? (PropPrefix + def.Ending.Prop, 0, 0, pw, ph) : null);
		if (prop is var (asset, sx, sy, w, h))
		{
			(int px, int py) = LifeRules.PropAt(hx, hy, def.Ending.PropDx, def.Ending.PropDy, w, h);
			prelude = LifeRules.PropCommand(asset, sx, sy, w, h, px, py);
		}
		prelude += "/pause 1500";
		_playing = ConclusionScenePlayer.TryStart(def.Ending.Location, lines, "life_" + def.Id, layout, prelude, () =>
		{
			_playing = false;
			_social.Moods.Adjust(def.Npc, 10);
		}, _log);
		if (_playing)
		{
			g.EndingShown = true;
			Dirty();
			_log.Info($"村民人生：演 {def.Npc} 的心愿结局。");
		}
	}

	/// <summary>道具图的大小（游戏像素）；没有这张图返回 null。</summary>
	private (int, int)? PropSize(string prop)
	{
		try
		{
			Texture2D t = Game1.content.Load<Texture2D>(PropPrefix + prop);
			return (t.Width, t.Height);
		}
		catch (Exception ex)
		{
			_log.Warn($"心愿道具 {prop} 读不出来：{ex.Message}");
			return null;
		}
	}

	// ---------------- 感情 ----------------

	private bool Taken(string npc)
	{
		if (VillageRelations.IsMarriedNpc(npc) || ChildSafety.IsChild(npc, Game1.getCharacterFromName(npc)?.Age))
		{
			return true;
		}
		foreach (Farmer f in Game1.getAllFarmers())
		{
			if (f.friendshipData.TryGetValue(npc, out Friendship fr) && (fr.IsDating() || fr.IsEngaged() || fr.IsMarried() || fr.IsRoommate()
				|| Game1.getCharacterFromName(npc)?.datable.Value == true && fr.Points >= 8 * NPC.friendshipPointsPerHeartLevel))
			{
				return true;
			}
		}
		return false;
	}

	private bool RomanceAllowed(string a, string b) => RelationshipGraph.RomanceAllowed(a, b, _config()) && Available(a) && Available(b);

	private PairState Pair(Couple c)
	{
		RelationshipEdgeState e = _social.Personality.Tie(c.A, c.B);
		return new PairState(c.A, c.B, e.Fondness, e.Spark, e.Strain);
	}

	private void Romance(LifeBook b, int today)
	{
		foreach (Couple c in b.Couples.Where(c => c.Together).ToList())
		{
			PairState now = Pair(c);
			string? next = LifeRules.NextPhase(c, now, today, Rng.NextDouble());
			if (next == CouplePhases.Engaged && !_config().LifeWeddings)
			{
				next = null;
			}
			if (next != null && next != CouplePhases.Married)
			{
				SetPhase(b, c, next, today);
			}
			if (c.Together)
			{
				RelationshipEdgeState e = _social.Personality.Tie(c.A, c.B);
				(e.Fondness, e.Spark) = LifeRules.Grow(now);
				_social.Personality.Changed();
				PlanDate(b, c, today);
			}
		}
		IEnumerable<PairState> crushes = _social.Personality.Edges
			.Where(e => e.A != RelationshipGraph.Farmer && e.B != RelationshipGraph.Farmer && e.Phase is "crush" or "questioning_attraction" or "warming")
			.Select(e => new PairState(e.A, e.B, e.Fondness, e.Spark, e.Strain));
		if (LifeRules.NewCouple(crushes, b, RomanceAllowed, Taken) is PairState p)
		{
			Couple c = new Couple { A = p.A, B = p.B, Phase = CouplePhases.Dating, Since = today };
			b.Couples.Add(c);
			SetPhase(b, c, CouplePhases.Dating, today);
			PlanDate(b, c, today);
		}
	}

	/// <summary>换阶段：关系图、八卦、心情、来信。</summary>
	private void SetPhase(LifeBook b, Couple c, string phase, int today)
	{
		c.Phase = phase;
		string a = Display(c.A), bb = Display(c.B);
		RelationshipEdgeState e = _social.Personality.Tie(c.A, c.B);
		switch (phase)
		{
			case CouplePhases.Dating:
				c.Since = today;
				RelationshipGraph.SetStatus(e, "dating", today, Tr.Get("life.turn.dating", "开始交往"));
				Gossip(Tr.Get("life.gossip.dating", "{{a}}和{{b}}在一起了！好几个人看见他们晚上一起散步。", ("a", a), ("b", bb)), c.A, c.B);
				_social.AddMoment(c.A, c.B, "romance", "warm", Tr.Get("life.moment.dating", "{{a}}和{{b}}开始交往了。", ("a", a), ("b", bb)), false);
				_social.Moods.Adjust(c.A, 15);
				_social.Moods.Adjust(c.B, 15);
				break;
			case CouplePhases.Engaged:
				c.WeddingDay = today + 7;
				RelationshipGraph.SetStatus(e, "engaged", today, Tr.Get("life.turn.engaged", "订婚"));
				Gossip(Tr.Get("life.gossip.engaged", "{{a}}向{{b}}求婚了，{{b}}答应了！婚礼下周在镇广场办。", ("a", a), ("b", bb)), c.A, c.B);
				_social.PostStoryLetter(c.A, $"life:wedding:{c.A}:{c.B}:{today}", Tr.Get("life.weddingSubject", "婚礼的请帖"),
					Tr.Get("life.weddingBody", "我和{{b}}要结婚了！婚礼在 {{date}}，上午九点到下午四点之间，在镇广场。你一定要来。", ("b", bb), ("date", DateText(c.WeddingDay))), null);
				_social.Moods.Adjust(c.A, 20);
				_social.Moods.Adjust(c.B, 20);
				break;
			case CouplePhases.Married:
				RelationshipGraph.SetStatus(e, "married", today, Tr.Get("life.turn.married", "结婚"));
				Gossip(Tr.Get("life.gossip.married", "{{a}}和{{b}}今天在镇广场办了婚礼，刘易斯镇长主持，全镇的人都去了。", ("a", a), ("b", bb)), c.A, c.B);
				_social.AddMoment(c.A, c.B, "wedding", "warm", Tr.Get("life.moment.married", "{{a}}和{{b}}结婚了。", ("a", a), ("b", bb)), false);
				_social.Moods.Adjust(c.A, 30);
				_social.Moods.Adjust(c.B, 30);
				break;
			case CouplePhases.BrokenUp:
				c.Since = today;
				c.NextDate = -1;
				RelationshipGraph.SetStatus(e, "broken_up", today, Tr.Get("life.turn.brokenUp", "分手"));
				e.Strain = Math.Min(1f, e.Strain + 0.2f);
				e.Spark = Math.Max(0f, e.Spark - 0.3f);
				Gossip(Tr.Get("life.gossip.brokenUp", "{{a}}和{{b}}分手了。两个人最近见面都绕着走。", ("a", a), ("b", bb)), c.A, c.B);
				_social.Moods.Adjust(c.A, -25);
				_social.Moods.Adjust(c.B, -25);
				break;
		}
		_social.Personality.Changed();
		Dirty();
		_log.Info($"村民人生：{c.A} 和 {c.B} → {phase}。");
	}

	private static string DateText(int totalDays)
	{
		SDate d = SDate.FromDaysSinceStart(Math.Max(1, totalDays + 1));
		return Tr.Get("life.date", "{{season}} {{day}} 日", ("season", Utility.getSeasonNameFromNumber(d.SeasonIndex)), ("day", d.Day));
	}

	/// <summary>你和其中一位谈起恋爱了：mod 撮合的这对就散了。</summary>
	private void ClaimedByFarmers(LifeBook b, int today)
	{
		foreach (Couple c in b.Couples.Where(c => c.Together && c.Phase != CouplePhases.Married).ToList())
		{
			if (Game1.getAllFarmers().Any(f => new[] { c.A, c.B }.Any(n => f.friendshipData.TryGetValue(n, out Friendship fr) && (fr.IsDating() || fr.IsEngaged() || fr.IsMarried()))))
			{
				SetPhase(b, c, CouplePhases.BrokenUp, today);
			}
		}
	}

	private void Weddings(LifeBook b, int today)
	{
		foreach (Couple c in b.Couples.Where(c => c.Phase == CouplePhases.Engaged && c.WeddingDay >= 0 && today >= c.WeddingDay).ToList())
		{
			c.WeddingDay = today;
			SetPhase(b, c, CouplePhases.Married, today);
			if (_config().LifeWeddings)
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("life.weddingToday", "今天{{a}}和{{b}}在镇广场办婚礼（9:00～16:00）。", ("a", Display(c.A)), ("b", Display(c.B))), HUDMessage.newQuest_type));
			}
		}
	}

	/// <summary>一周约一次：晚上两人去同一个地方见面（和你的约见、帮工撞上时让路）。</summary>
	private void PlanDate(LifeBook b, Couple c, int today)
	{
		if (c.NextDate > today || LifeRules.WorksEvenings(c.A) || LifeRules.WorksEvenings(c.B))
		{
			return;
		}
		int day = today + 1 + Rng.Next(6);
		// 同一晚别的情侣占了的地方不去。
		List<int> free = Enumerable.Range(0, LifeRules.DatePlaces.Count).Where(i => !b.Couples.Any(o => o != c && o.Together && o.NextDate == day && o.DatePlace == i)).ToList();
		int place = free.Count > 0 ? free[Rng.Next(free.Count)] : Rng.Next(LifeRules.DatePlaces.Count);
		DatePlace p = LifeRules.DatePlaces[place];
		ScheduleOverlay? a = _schedules.Plan(c.A, DateOwner, day, LifeRules.DateStart, LifeRules.DateEnd, p.Location, p.AX, p.AY, p.AFacing, out string why);
		ScheduleOverlay? bo = a == null ? null : _schedules.Plan(c.B, DateOwner, day, LifeRules.DateStart, LifeRules.DateEnd, p.Location, p.BX, p.BY, p.BFacing, out why);
		if (a == null || bo == null)
		{
			if (a != null)
			{
				_schedules.Cancel(a.Id, why);
			}
			_log.Debug($"{c.A} 和 {c.B} 的约会排不上：{why}");
			return;
		}
		c.NextDate = day;
		c.DatePlace = place;
		Dirty();
	}

	/// <summary>今天有约会的：记成一次两人之间的来往。</summary>
	private void Dates(LifeBook b, int today)
	{
		foreach (Couple c in b.Couples.Where(c => c.Together && c.NextDate == today))
		{
			DatePlace p = LifeRules.DatePlaces[Math.Clamp(c.DatePlace, 0, LifeRules.DatePlaces.Count - 1)];
			_social.AddMoment(c.A, c.B, "date", "warm", Tr.Get("life.dateMoment", "{{a}}和{{b}}晚上在{{place}}约会。", ("a", Display(c.A)), ("b", Display(c.B)), ("place", p.Name)), true);
		}
	}

	// ---------------- 婚礼 ----------------

	private void PlayWedding(LifeBook b, Couple c)
	{
		const string lewis = "Lewis";
		List<string> guests = new[] { "Gus", "Robin", "Pierre", "Marnie", "Emily", "Pam", "Caroline", "Evelyn" }
			.Where(n => !c.Has(n) && n != lewis && Game1.getCharacterFromName(n) != null && _npcs.IsEnabled(n)).Take(3).ToList();
		string a = Display(c.A), bb = Display(c.B);
		List<ConclusionLine> lines = new List<ConclusionLine>
		{
			new ConclusionLine { Who = lewis, Line = Tr.Get("life.wedding.lewis1", "各位，今天我们聚在这里，见证{{a}}和{{b}}结为夫妻。", ("a", a), ("b", bb)), Feeling = "happy" },
			new ConclusionLine { Who = c.A, Line = Tr.Get("life.wedding.vowA", "{{b}}，以后的日子，不管晴天还是下雨，我都陪着你。", ("b", bb)), Feeling = "love" },
			new ConclusionLine { Who = c.B, Line = Tr.Get("life.wedding.vowB", "我愿意。{{a}}，谢谢你选了我。", ("a", a)), Feeling = "love" },
			new ConclusionLine { Who = lewis, Line = Tr.Get("life.wedding.lewis2", "那么，我宣布你们正式结为夫妻！大家鼓掌！"), Feeling = "happy" }
		};
		if (guests.Count > 0)
		{
			lines.Add(new ConclusionLine { Who = guests[0], Line = Tr.Get("life.wedding.guest", "恭喜你们！今晚酒吧我请客！"), Feeling = "happy" });
		}
		if (Game1.getCharacterFromName(lewis) == null)
		{
			lines.RemoveAll(l => l.Who == lewis);
		}
		List<string> speakers = lines.Select(l => l.Who).Concat(guests).Distinct(StringComparer.OrdinalIgnoreCase).Take(ConclusionScript.MaxSpeakers).ToList();
		// 只站不说的宾客也要上场：给他们一句不出声的表情。
		foreach (string g in guests.Skip(1))
		{
			lines.Add(new ConclusionLine { Who = g, Line = Tr.Get("life.wedding.cheer", "恭喜恭喜！"), Feeling = "happy" });
		}
		ConclusionScript.Layout layout = LifeRules.WeddingLayout(LifeRules.WeddingX, LifeRules.WeddingY, speakers, c.A, c.B, lewis);
		string prelude = "/playMusic wedding" + LifeRules.WeddingDecor() + "/pause 2000";
		_playing = ConclusionScenePlayer.TryStart("Town", lines, $"life_wedding_{c.A}_{c.B}", layout, prelude, () =>
		{
			_playing = false;
			foreach (string n in guests)
			{
				_social.Moods.Adjust(n, 10);
			}
		}, _log);
		if (_playing)
		{
			c.WeddingShown = true;
			Dirty();
			_log.Info($"村民人生：{c.A} 和 {c.B} 的婚礼开演。");
		}
	}

	// ---------------- 调试 ----------------

	public const string Usage = "用法：aisv_life list | tick | advance <村民> | start <心愿id> | couple <村民> <村民> <dating|engaged|married|broken_up> | job <村民> <工作…> | ending <村民> | wedding <村民>";

	public string Command(string[] args)
	{
		if (Book is not LifeBook b)
		{
			return "先进存档。";
		}
		int today = Today;
		string Arg(int i) => i < args.Length ? args[i] : "";
		switch (Arg(0))
		{
			case "tick":
				Tick(b, today);
				return "推进了一周。";
			case "advance" when b.Active(Arg(1)) is LifeGoal g:
				Advance(b, g, today, force: true);
				return $"{g.Npc}：第 {g.Stage} 步{(g.Outcome == "done" ? "，达成" : "")}{(g.HelpRequest.Length > 0 ? "，托你帮忙 " + g.HelpRequest : "")}。";
			case "advance":
				return "这位村民没有进行中的心愿。";
			case "start" when Catalog.Get(Arg(1)) is LifeGoalDef def:
				b.Goals.RemoveAll(x => x.Id == def.Id);
				if (b.Active(def.Npc) is LifeGoal old)
				{
					old.Outcome = "dropped";
					old.EndedDay = today;
				}
				b.Goals.Add(new LifeGoal { Id = def.Id, Npc = def.Npc, StartedDay = today, LastStepDay = today });
				Dirty();
				return $"{def.Npc} 开始「{def.Goal}」。";
			case "couple" when args.Length > 3:
			{
				Couple c = b.Couples.FirstOrDefault(x => x.Has(Arg(1)) && x.Has(Arg(2))) ?? new Couple { A = Arg(1), B = Arg(2), Since = today };
				if (!b.Couples.Contains(c))
				{
					b.Couples.Add(c);
				}
				SetPhase(b, c, Arg(3), today);
				if (c.Phase == CouplePhases.Married)
				{
					c.WeddingDay = today;
					c.WeddingShown = false;
				}
				return $"{c.A} 和 {c.B}：{c.Phase}{(c.WeddingDay >= 0 ? $"，婚礼第 {c.WeddingDay} 天" : "")}。";
			}
			case "job" when args.Length > 2:
				b.Jobs.RemoveAll(j => string.Equals(j.Npc, Arg(1), StringComparison.OrdinalIgnoreCase));
				b.Jobs.Add(new JobChange { Npc = Arg(1), Job = string.Join(" ", args.Skip(2)), Since = today });
				Dirty();
				return "换好了。";
			case "ending" when b.Goals.LastOrDefault(x => x.Outcome == "done" && string.Equals(x.Npc, Arg(1), StringComparison.OrdinalIgnoreCase)) is LifeGoal g && Catalog.Get(g.Id) is LifeGoalDef def:
				g.EndingShown = false;
				_playing = false;
				PlayEnding(g, def);
				return _playing ? "开演。" : "演不了（看日志）。";
			case "wedding" when b.Couples.FirstOrDefault(x => x.Has(Arg(1)) && x.Together) is Couple c:
				_playing = false;
				PlayWedding(b, c);
				return _playing ? "开演。" : "演不了（看日志）。";
			case "dates":
				return string.Join("\n", _schedules.All().Where(o => o.Owner == DateOwner).Select(o => $"{o.Npc} 第 {o.Day} 天 {o.Start}～{o.End} {o.Location}({o.X},{o.Y})") ?? Array.Empty<string>());
			default:
				return $"上次推进第 {b.LastTick} 天（今天 {today}）。\n心愿：\n" + string.Join("\n", b.Goals.Select(g => $"  {g.Npc} {g.Id} 第 {g.Stage} 步 {(g.Active ? "进行中" : g.Outcome)}{(g.Helped > 0 ? $" 帮过 {g.Helped} 次" : "")}{(g.HelpRequest.Length > 0 ? " 托过忙" : "")}{(g.EndingShown ? " 演过" : "")}{(g.Notes.Count > 0 ? "：" + g.Notes[^1] : "")}"))
					+ "\n感情：\n" + string.Join("\n", b.Couples.Select(c => $"  {c.A}+{c.B} {c.Phase} 自第 {c.Since} 天{(c.WeddingDay >= 0 ? $" 婚礼第 {c.WeddingDay} 天{(c.WeddingShown ? "（演过）" : "")}" : "")}{(c.NextDate >= 0 ? $" 约会第 {c.NextDate} 天" : "")}"))
					+ "\n工作：" + string.Join("，", b.Jobs.Select(j => $"{j.Npc}→{j.Job}"));
		}
	}
}
