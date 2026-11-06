using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AISV.Core.Diagnostics;
using AISV.Core.Messenger;
using AISV.Core.Social;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.Game.Messenger;

public sealed record PhotoShot(Texture2D Image, PhotoRecord Record);

/// <summary>
/// 信使里的照片：每个存档每位农夫一个文件夹（mod 文件夹下 Data/photo-album/存档ID_农夫ID），PNG 加一份 album.json 索引。
/// 图片按需读，最多缓存 24 张。
/// </summary>
public sealed class PhotoStore
{
	private const string IndexFile = "album.json";

	private const int CachedTextures = 24;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

	private readonly string _root;

	private readonly ILog _log;

	private readonly Dictionary<string, Texture2D?> _textures = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> _order = new List<string>();

	private string? _loadedFolder;

	private List<PhotoRecord> _records = new List<PhotoRecord>();

	public PhotoStore(string modDirectory, ILog log)
	{
		_root = Path.Combine(modDirectory, "Data", "photo-album");
		_log = log;
	}

	private static string Key => $"{Game1.uniqueIDForThisGame}_{Game1.player?.UniqueMultiplayerID ?? 0}";

	private string Folder => Path.Combine(_root, Key);

	public IReadOnlyList<PhotoRecord> From(string sender) => Records().Where(r => Same(r.Sender, sender)).ToList();

	public bool SentToday(string sender, string subject) => Records().Any(r => Same(r.Sender, sender) && Same(r.Subject, subject) && r.Day == Game1.dayOfMonth && r.Year == Game1.year && Same(r.Season, Game1.currentSeason));

	public PhotoRecord? Find(string id) => Records().FirstOrDefault(r => r.Id == id);

	/// <summary>这个存档收到过的所有照片，旧的在前。</summary>
	public IReadOnlyList<PhotoRecord> All() => Records().OrderBy(r => Dates.TotalDays(r.Day, r.Season, r.Year) * 10000 + r.Time).ToList();

	public void Save(PhotoShot shot)
	{
		try
		{
			List<PhotoRecord> records = Records();
			string folder = Folder;
			Directory.CreateDirectory(folder);
			using (FileStream stream = File.Create(Path.Combine(folder, shot.Record.Id + ".png")))
			{
				shot.Image.SaveAsPng(stream, shot.Image.Width, shot.Image.Height);
			}
			records.Add(shot.Record);
			File.WriteAllText(Path.Combine(folder, IndexFile), JsonSerializer.Serialize(records, Json));
			Remember(shot.Record.Id, shot.Image);
		}
		catch (Exception ex)
		{
			_log.Warn("信使没能存下一张照片：" + ex.Message);
			shot.Image.Dispose();
		}
	}

	public Texture2D? Image(string id)
	{
		if (_textures.TryGetValue(id, out Texture2D? cached))
		{
			return cached;
		}
		Texture2D? image = null;
		try
		{
			string path = Path.Combine(Folder, id + ".png");
			if (File.Exists(path))
			{
				using FileStream stream = File.OpenRead(path);
				image = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			}
		}
		catch (Exception ex)
		{
			_log.Info($"信使读不出照片 {id}：{ex.Message}");
		}
		Remember(id, image);
		return image;
	}

	private void Remember(string id, Texture2D? image)
	{
		_textures[id] = image;
		_order.Remove(id);
		_order.Add(id);
		while (_order.Count > CachedTextures)
		{
			string old = _order[0];
			_order.RemoveAt(0);
			if (_textures.Remove(old, out Texture2D? t))
			{
				t?.Dispose();
			}
		}
	}

	private List<PhotoRecord> Records()
	{
		string folder = Folder;
		if (folder == _loadedFolder)
		{
			return _records;
		}
		foreach (Texture2D? t in _textures.Values)
		{
			t?.Dispose();
		}
		_textures.Clear();
		_order.Clear();
		_loadedFolder = folder;
		_records = new List<PhotoRecord>();
		try
		{
			string path = Path.Combine(folder, IndexFile);
			if (File.Exists(path))
			{
				_records = JsonSerializer.Deserialize<List<PhotoRecord>>(File.ReadAllText(path)) ?? new List<PhotoRecord>();
			}
		}
		catch (Exception ex)
		{
			_log.Warn("信使读不出照片列表：" + ex.Message);
		}
		return _records;
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
