using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;

namespace AISV.Core.Compat;

/// <summary>
/// 下载并维护社区 AI 排除名单。后台下载，成功后存一份在本地缓存（下次启动先用缓存，连不上网也有效）；
/// 下载失败又没有缓存时"放行"——不排除任何人。
/// </summary>
public sealed class OptOutList
{
	private const int MaxBytes = 1_000_000;

	private const string CacheFile = "ai-opt-out.json";

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

	private readonly HttpClient _http;

	private readonly IStorageBackend _cache;

	private readonly NpcOwnershipScanner _scanner;

	private readonly ILog _log;

	private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

	private OptOutRules? _rules;

	private string[] _loadedModIds = Array.Empty<string>();

	public OptOutList(HttpClient http, IStorageBackend cache, NpcOwnershipScanner scanner, ILog log)
	{
		_http = http;
		_cache = cache;
		_scanner = scanner;
		_log = log;
	}

	/// <summary>empty（关掉了）/ loading / loaded / cached（用的本地缓存）/ failed（放行）。</summary>
	public string Status { get; private set; } = "empty";

	public bool IsAvailable => Volatile.Read(ref _rules) != null;

	public int RuleCount => Volatile.Read(ref _rules)?.RuleCount ?? 0;

	public int DiscoveredNpcCount => Volatile.Read(ref _rules)?.DiscoveredNpcCount ?? 0;

	public void SetLoadedMods(string[] modIds) => Volatile.Write(ref _loadedModIds, modIds);

	public string? MatchNpc(string npcName, IEnumerable<string> sourceModIds) => Volatile.Read(ref _rules)?.MatchNpc(npcName.Trim(), sourceModIds, Volatile.Read(ref _loadedModIds));

	public string? MatchModId(string modId) => Volatile.Read(ref _rules)?.MatchModId(modId.Trim());

	/// <summary>
	/// 重新加载：先用缓存立刻生效，再下载最新的。url 为空表示玩家关掉了名单。
	/// modFolders 用来找出"整个 mod 被排除"的 mod 加了哪些 NPC。
	/// </summary>
	public async Task RefreshAsync(string? url, IReadOnlyList<ModFolder> modFolders, CancellationToken cancellationToken = default)
	{
		await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				Volatile.Write(ref _rules, null);
				Status = "empty";
				return;
			}
			string? cached = ReadCache();
			Dictionary<string, IReadOnlyDictionary<string, string>> scans = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			if (cached != null && TryApply(cached, modFolders, "cached", scans))
			{
				_log.Debug($"先用本地缓存的 AI 排除名单（{RuleCount} 条规则）。");
			}
			else
			{
				Status = "loading";
			}
			string? fresh = await DownloadAsync(url.Trim(), cancellationToken).ConfigureAwait(false);
			if (fresh != null && TryApply(fresh, modFolders, "loaded", scans))
			{
				WriteCache(fresh);
				_log.Info($"社区 AI 排除名单已加载：{RuleCount} 条 mod 规则，找到 {DiscoveredNpcCount} 位已安装的相关 NPC。");
			}
			else if (!IsAvailable)
			{
				Status = "failed";
				_log.Warn("社区 AI 排除名单没加载成功，先不排除任何 NPC。");
			}
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	/// <param name="scans">这次刷新里已经扫过的 mod 和结果：缓存和新下载的名单各套用一次，不重复扫。</param>
	private bool TryApply(string json, IReadOnlyList<ModFolder> modFolders, string status, Dictionary<string, IReadOnlyDictionary<string, string>> scans)
	{
		try
		{
			OptOutRules rules = OptOutRules.Parse(json);
			string[] loaded = Volatile.Read(ref _loadedModIds);
			Dictionary<string, string> patternFor = loaded
				.Where(rules.NeedsOwnershipScan)
				.ToDictionary(id => id, id => rules.MostSpecificPattern(id)!, StringComparer.OrdinalIgnoreCase);
			if (patternFor.Count > 0)
			{
				List<ModFolder> toScan = modFolders.Where(m => patternFor.ContainsKey(m.Id) && !scans.ContainsKey(m.Id)).ToList();
				if (toScan.Count > 0)
				{
					IReadOnlyDictionary<string, string> owners = _scanner.Scan(toScan, out _);
					foreach (ModFolder m in toScan)
					{
						scans[m.Id] = owners.Where(o => string.Equals(o.Value, m.Id, StringComparison.OrdinalIgnoreCase)).ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
					}
				}
				Dictionary<string, string> discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (string modId in patternFor.Keys)
				{
					if (scans.TryGetValue(modId, out IReadOnlyDictionary<string, string>? found))
					{
						foreach (string npc in found.Keys)
						{
							discovered.TryAdd(npc, patternFor[modId]);
						}
					}
				}
				if (discovered.Count > 0)
				{
					rules = rules.WithDiscoveredOwners(discovered);
				}
			}
			Volatile.Write(ref _rules, rules);
			Status = status;
			return true;
		}
		catch (JsonException ex)
		{
			_log.Warn("AI 排除名单格式不对：" + ex.Message);
			return false;
		}
	}

	private string? ReadCache()
	{
		try
		{
			return _cache.Read(CacheFile);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Debug("AI 排除名单缓存读不了：" + ex.Message);
			return null;
		}
	}

	private void WriteCache(string text)
	{
		try
		{
			_cache.Write(CacheFile, text);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_log.Debug("AI 排除名单缓存写不进去：" + ex.Message);
		}
	}

	private async Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
	{
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.UserAgent.ParseAdd("AIStardewValley/AI-Mod-Exclusion");
			request.Headers.Accept.ParseAdd("application/json");
			request.Headers.Accept.ParseAdd("text/plain");
			request.Headers.Accept.ParseAdd("text/x-wiki");
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(RequestTimeout);
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > MaxBytes)
			{
				throw new InvalidDataException("名单超过大小上限。");
			}
			await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[8192];
			int read;
			while ((read = await stream.ReadAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
			{
				if (buffer.Length + read > MaxBytes)
				{
					throw new InvalidDataException("名单超过大小上限。");
				}
				buffer.Write(chunk, 0, read);
			}
			return new UTF8Encoding(false, true).GetString(buffer.ToArray());
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_log.Warn("下载 AI 排除名单超时。");
			return null;
		}
		catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or DecoderFallbackException or IOException)
		{
			_log.Warn("下载 AI 排除名单失败：" + ex.Message);
			return null;
		}
	}
}
