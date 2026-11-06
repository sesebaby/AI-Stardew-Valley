using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.News;
using AISV.Core.Social.Gossip;
using AISV.Core.Storage;
using AISV.Core.Text;
using AISV.Core.World.Commissions;
using AISV.Core.World.Market;
using AISV.Game.Dialogue;
using AISV.Game.World;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TokenizableStrings;

namespace AISV.Game.Social;

/// <summary>
/// 《鹈鹕镇周报》：每月 7、14、21、28 日晚上出刊。行情和委托栏当晚抄下；新闻栏等晚上的八卦整理完再让 AI 写，
/// 出刊前按句子做事实校验。第二天早上把摘要寄到信箱，全文在"世界 → 周报"。只在主机上。
/// </summary>
public sealed class WeeklyNewsService
{
	private readonly Func<ModConfig> _config;

	private readonly Func<DocumentStore?> _store;

	private readonly SocialSystem _social;

	private readonly MarketService _market;

	private readonly CommissionService _commissions;

	private readonly ILog _log;

	private (GameDate Day, WeeklyColumns Columns)? _pending;

	public WeeklyNewsService(Func<ModConfig> config, Func<DocumentStore?> store, SocialSystem social, MarketService market, CommissionService commissions, ILog log)
	{
		_config = config;
		_store = store;
		_social = social;
		_market = market;
		_commissions = commissions;
		_log = log;
		_social.NightSettled += OnNightSettled;
	}

	public IAiSender? Ai { get; set; }

	public Func<string>? Language { get; set; }

	/// <summary>第几期 → 这期"山谷之外"的标题和正文（不登时为空）。</summary>
	public Func<int, (string Title, string Text)?>? Beyond { get; set; }

	/// <summary>"本周钱事"的几句。</summary>
	public Func<IReadOnlyList<string>>? Money { get; set; }

	/// <summary>界面上显不显示"山谷之外"（关掉推广后旧报纸里的也不显示）。</summary>
	public Func<bool>? ShowBeyond { get; set; }

	public int Version { get; private set; }

	public bool Enabled
	{
		get
		{
			ModConfig c = _config();
			return c.ModOn && c.GazetteOn;
		}
	}

	private List<WeeklyNews>? Book => _store()?.Get(WeeklyNewsBook.Path, WeeklyNewsBook.Doc);

	public IReadOnlyList<WeeklyNews> Editions() => WeeklyNewsRules.Newest(Book ?? new List<WeeklyNews>());

	public WeeklyNews? Latest => Editions().FirstOrDefault();

	public void OnSaveLoaded() => _pending = null;

	public void OnReturnedToTitle() => _pending = null;

	/// <summary>天黑：今天出刊就把行情和委托抄下来，等晚上的八卦整理完。</summary>
	public void OnDayEnding()
	{
		_pending = null;
		ModConfig c = _config();
		if (!Enabled || !c.AiOn || !c.RumorsOn || !Context.IsMainPlayer || _store() == null || !WeeklyNewsRules.IsIssueDay(Game1.dayOfMonth))
		{
			return;
		}
		_pending = (GameState.Today, CaptureColumns());
	}

	private void OnNightSettled(GameDate day)
	{
		if (_pending is not { } p || !p.Day.SameDay(day.Day, day.Season, day.Year))
		{
			return;
		}
		_pending = null;
		Write(p.Day, p.Columns);
	}

	/// <summary>让 AI 写新闻栏（没八卦时不问 AI），回来后出刊。</summary>
	private void Write(GameDate day, WeeklyColumns columns)
	{
		List<GossipEntry> entries = _social.Gossip.FeedForAi();
		string facts = WeeklyNewsRules.Facts(entries);
		IAiSender? ai = entries.Count > 0 ? Ai : null;
		string system = WeeklyNewsRules.System(Language?.Invoke() ?? "中文", facts);
		if (ai != null)
		{
			_log.Info("周报：在写镇上新闻……");
		}
		_social.Enqueue("写周报", async ct =>
		{
			List<WeeklyNewsEntry>? articles = null;
			if (ai != null)
			{
				AiResult r = await ai.SendAsync(new ChatRequest(WeeklyNewsRules.RequestName, system, WeeklyNewsRules.User, WeeklyNewsRules.MaxOutputTokens, WeeklyNewsRules.Temperature), ct).ConfigureAwait(false);
				articles = r.Ok ? WeeklyNewsRules.Parse(r.Text) : null;
				if (articles == null)
				{
					_log.Warn("周报：AI 没写出能用的新闻，这期只登行情和委托。");
				}
			}
			return () => Publish(articles, facts, columns, day, entries);
		}, onFailed: () => Publish(null, facts, columns, day, entries));
	}

	private bool Publish(IReadOnlyList<WeeklyNewsEntry>? articles, string facts, WeeklyColumns columns, GameDate day, List<GossipEntry> entries)
	{
		if (Book is not List<WeeklyNews> book)
		{
			return false;
		}
		string checkFacts = facts + "\n" + (Game1.player?.Name ?? "") + " " + (Game1.player?.farmName.Value ?? "");
		List<string> removed = new List<string>();
		IReadOnlyList<string[]> names = KnownNameGroups();
		WeeklyNews? issue = WeeklyNewsRules.Publish(articles, checkFacts, names, columns, day.Day, day.Season, day.Year, null, removed, WeeklyNewsRules.Rumors(entries, names.Append(new[] { Game1.player?.Name ?? "", "农夫" }.Where(WeeklyNewsRules.UsableName).ToArray()).ToList()));
		foreach (string s in removed)
		{
			_log.Debug("周报：删掉一句和八卦对不上的：" + s);
		}
		if (issue == null)
		{
			_log.Info("周报：这期没有能登的内容，不出刊。");
			return false;
		}
		if (Beyond?.Invoke(issue.Edition) is { } beyond && beyond.Text.Trim().Length > 0)
		{
			issue.FarTitle = WeeklyNewsRules.CleanForPaper(beyond.Title);
			issue.FarBody = WeeklyNewsRules.CleanForPaper(beyond.Text);
		}
		issue.Purse = columns.Money.ToList();
		WeeklyNewsRules.Store(book, issue);
		_store()?.MarkDirty(WeeklyNewsBook.Path);
		_store()?.Commit(new[] { WeeklyNewsBook.Path });
		Version++;
		_log.Info($"周报第 {issue.Edition} 期出刊：新闻 {issue.Stories.Count} 条、行情 {issue.Prices.Count} 行、委托 {issue.Jobs.Count} 条，事实校验删了 {issue.CutLines} 句。");
		Notices.Queue(Tr.Get("news.generated", "新一期《鹈鹕镇周报》出刊了，去信箱看看吧。"));
		TryDeliverMail();
		return true;
	}

	/// <summary>出刊那晚的行情和委托板。</summary>
	public WeeklyColumns CaptureColumns()
	{
		WeeklyColumns columns = new WeeklyColumns();
		try
		{
			columns.Money.AddRange((Money?.Invoke() ?? Array.Empty<string>()).Select(WeeklyNewsRules.CleanForPaper).Where(s => s.Trim().Length > 0).Take(4));
		}
		catch (Exception ex)
		{
			_log.Warn("周报：钱事栏没抄成：" + ex.Message);
		}
		try
		{
			if (_market.Enabled)
			{
				IEnumerable<MarketRow> rows = _market.AllRows().Where(r => !r.OutOfSeason && !CommissionRules.NotTownGrown.Contains(r.ItemId));
				foreach (MarketRow r in rows.OrderByDescending(r => r.Hot).ThenByDescending(r => Math.Abs(WeeklyNewsRules.WeekChange(r.History))).ThenBy(r => r.DisplayName, StringComparer.Ordinal).Take(WeeklyNewsRules.MarketLines))
				{
					columns.Market.Add(new WeeklyMarketLine { ItemId = r.ItemId, Name = r.DisplayName, Price = r.TodayPrice, BasePrice = r.BasePrice, WeekChange = WeeklyNewsRules.WeekChange(r.History), Hot = r.Hot });
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("周报：行情栏没抄成：" + ex.Message);
		}
		try
		{
			if (_commissions.Enabled)
			{
				foreach (Commission c in _commissions.Open().Where(c => CommissionService.DaysLeft(c) >= 1).OrderByDescending(c => c.RewardGold).Take(WeeklyNewsRules.CommissionLines))
				{
					columns.Commissions.Add(new WeeklyCommissionLine
					{
						Id = c.Id,
						Poster = c.Poster,
						PosterName = CommissionService.NpcName(c.Poster),
						Headline = CommissionRules.Headline(c.Kind),
						Task = CommissionService.TaskLine(c),
						RewardGold = c.RewardGold,
						LastDay = c.LastDay
					});
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warn("周报：委托栏没抄成：" + ex.Message);
		}
		return columns;
	}

	/// <summary>所有村民的名字，每组是同一个人的内部名和显示名。</summary>
	public static IReadOnlyList<string[]> KnownNameGroups()
	{
		List<string[]> groups = new List<string[]>();
		if (Game1.characterData == null)
		{
			return groups;
		}
		foreach (KeyValuePair<string, StardewValley.GameData.Characters.CharacterData> pair in Game1.characterData)
		{
			HashSet<string> names = new HashSet<string>(StringComparer.Ordinal) { pair.Key };
			try
			{
				string? display = TokenParser.ParseText(pair.Value?.DisplayName ?? "");
				if (!string.IsNullOrWhiteSpace(display))
				{
					names.Add(display.Trim());
				}
			}
			catch
			{
			}
			string[] usable = names.Where(WeeklyNewsRules.UsableName).ToArray();
			if (usable.Length > 0)
			{
				groups.Add(usable);
			}
		}
		return groups;
	}

	// ---------------- 信 ----------------

	/// <summary>出刊后第 1～2 天放进信箱；同一期只寄一次。</summary>
	public void TryDeliverMail()
	{
		Farmer? player = Game1.player;
		if (player == null || !Context.IsWorldReady || Latest is not WeeklyNews latest || !WeeklyNewsRules.ShouldMail(latest, GameState.Today.TotalDays))
		{
			return;
		}
		string key = WeeklyNewsRules.MailKey(latest);
		if (player.mailReceived.Contains(key) || player.mailbox.Contains(key))
		{
			return;
		}
		player.mailbox.Add(key);
		try
		{
			Game1.content.Load<Dictionary<string, string>>("Data\\mail")[key] = WeeklyNewsRules.MailContent(latest);
		}
		catch (Exception ex)
		{
			_log.Debug("周报：信没补进 Data/mail：" + ex.Message);
		}
		_log.Info($"周报第 {latest.Edition} 期放进信箱了。");
	}

	public void EditMail(IDictionary<string, string> data)
	{
		foreach (WeeklyNews n in Book ?? new List<WeeklyNews>())
		{
			data[WeeklyNewsRules.MailKey(n)] = WeeklyNewsRules.MailContent(n);
		}
	}

	public bool BeyondShown => ShowBeyond?.Invoke() ?? false;

	// ---------------- 调试 ----------------

	public string Command(string[] args, Func<string, object, string> write)
	{
		string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";
		switch (sub)
		{
			case "publish":
				Write(GameState.Today, CaptureColumns());
				return "在写这期（结果看日志）";
			case "deliver":
				TryDeliverMail();
				return "信箱：" + string.Join(",", Game1.player.mailbox.Where(m => m.Contains("WeeklyNews")));
			case "facts":
				return write("news-facts.txt", WeeklyNewsRules.Facts(_social.Gossip.FeedForAi()));
			case "clear":
				Book?.Clear();
				_store()?.MarkDirty(WeeklyNewsBook.Path);
				Version++;
				return "清空了";
			default:
				return write("weekly-news.json", new { enabled = Enabled, today = GameState.Today.TotalDays, pending = _pending?.Day.TotalDays, mail = Latest is { } n ? new { key = WeeklyNewsRules.MailKey(n), text = WeeklyNewsRules.MailContent(n) } : null, editions = Editions() });
		}
	}
}
