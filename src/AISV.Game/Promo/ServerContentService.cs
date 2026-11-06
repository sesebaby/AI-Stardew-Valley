using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Diagnostics;
using AISV.Core.Promo;
using AISV.Game.Dialogue;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game.Promo;

/// <summary>
/// 服务器后台的内容：启动时和每天早上拉一次 /aisv/content（6 小时内拉过就不拉），
/// JSON 和图片缓存在 SMAPI 全局数据目录，连不上用上次的。没有内置服务器时什么都不做。
/// </summary>
public sealed class ServerContentService
{
	private const string DataKey = "aisv-content";

	private static readonly TimeSpan Throttle = TimeSpan.FromHours(6);

	private readonly ServerConnection _connection;

	private readonly IDataHelper _data;

	private readonly HttpClient _http;

	private readonly ILog _log;

	private readonly string _mediaDir;

	private readonly Dictionary<string, Texture2D?> _textures = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);

	private DateTime _lastFetch = DateTime.MinValue;

	private volatile bool _fetching;

	public ServerContentService(ServerConnection connection, IDataHelper data, HttpClient http, string modId, ILog log)
	{
		_connection = connection;
		_data = data;
		_http = http;
		_log = log;
		_mediaDir = Path.Combine(Constants.DataPath, ".smapi", "mod-data", modId.ToLowerInvariant(), "media");
	}

	public bool Available => _connection.IsAvailable;

	public ServerContent Content { get; private set; } = ServerContent.Empty;

	/// <summary>内容变了加一。</summary>
	public int Version { get; private set; }

	/// <summary>读上次缓存的内容。</summary>
	public void LoadCached()
	{
		if (!Available)
		{
			return;
		}
		try
		{
			string? cached = _data.ReadGlobalData<Cached>(DataKey)?.Json;
			if (!string.IsNullOrEmpty(cached) && JsonNode.Parse(cached) is JsonObject o)
			{
				Apply(o);
			}
		}
		catch (Exception ex)
		{
			_log.Debug("推荐内容的缓存读不出来：" + ex.GetType().Name);
		}
	}

	public void Refresh(bool force = false)
	{
		if (!Available || _fetching || (!force && DateTime.UtcNow - _lastFetch < Throttle))
		{
			return;
		}
		_fetching = true;
		Task.Run(async () =>
		{
			try
			{
				await FetchAsync().ConfigureAwait(false);
			}
			finally
			{
				_fetching = false;
			}
		});
	}

	private async Task FetchAsync()
	{
		try
		{
			string text = await _http.GetStringAsync(_connection.Root + "/aisv/content").ConfigureAwait(false);
			_lastFetch = DateTime.UtcNow;
			if (JsonNode.Parse(text) is not JsonObject o)
			{
				return;
			}
			await DownloadImagesAsync(o).ConfigureAwait(false);
			string version = o["version"]?.ToString() ?? "";
			if (version.Length > 0 && version == Content.Version)
			{
				return;
			}
			MainThread.Run(() => Apply(o));
			try
			{
				_data.WriteGlobalData(DataKey, new Cached { Json = o.ToJsonString() });
			}
			catch (Exception ex)
			{
				_log.Debug("推荐内容的缓存存不下来：" + ex.GetType().Name);
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
		{
			_log.Debug("推荐内容没拉到（用上次的）：" + _connection.Mask(ex.Message));
		}
	}

	private void Apply(JsonObject o)
	{
		Content = ServerContent.Parse(o);
		Version++;
	}

	/// <summary>测试用：直接换上一份内容（不缓存）。</summary>
	public void ApplyForTest(JsonObject o)
	{
		Apply(o);
		Task.Run(() => DownloadImagesAsync(o));
	}

	private async Task DownloadImagesAsync(JsonObject o)
	{
		foreach (string image in ServerContent.Images(o))
		{
			string? url = ServerContent.ImageUrl(image, _connection.Root);
			if (url == null)
			{
				continue;
			}
			string path = Path.Combine(_mediaDir, ServerContent.CacheName(url));
			if (File.Exists(path))
			{
				continue;
			}
			try
			{
				using HttpResponseMessage resp = await _http.GetAsync(url).ConfigureAwait(false);
				if (!resp.IsSuccessStatusCode || (resp.Content.Headers.ContentLength ?? 0) > ServerContent.MaxImageBytes)
				{
					continue;
				}
				byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
				if (bytes.Length > ServerContent.MaxImageBytes)
				{
					continue;
				}
				Directory.CreateDirectory(_mediaDir);
				await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
			{
				_log.Debug("推荐图片没下载到：" + ex.GetType().Name);
			}
		}
	}

	/// <summary>画图用（主线程）。还没下载好或读不出来时为空。</summary>
	public Texture2D? Texture(string image)
	{
		string? url = ServerContent.ImageUrl(image, _connection.Root);
		if (url == null)
		{
			return null;
		}
		if (_textures.TryGetValue(url, out Texture2D? t))
		{
			return t;
		}
		string path = Path.Combine(_mediaDir, ServerContent.CacheName(url));
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			using FileStream fs = File.OpenRead(path);
			t = Texture2D.FromStream(Game1.graphics.GraphicsDevice, fs);
		}
		catch (Exception ex)
		{
			_log.Debug("推荐图片读不出来：" + ex.GetType().Name);
			t = null;
		}
		_textures[url] = t;
		return t;
	}

	/// <summary>点了支持任务：告诉服务器一声（只计数）。</summary>
	public void ReportClick(long id)
	{
		if (!Available || id <= 0)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				using HttpResponseMessage _ = await _http.PostAsync(_connection.Root + "/aisv/support/" + id + "/click", new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				_log.Debug("支持任务的点击没报上去：" + ex.GetType().Name);
			}
		});
	}

	private sealed class Cached
	{
		public string Json { get; set; } = "";
	}
}
