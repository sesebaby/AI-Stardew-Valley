using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Looks;
using AISV.Core.Text;
using AISV.Game.Npcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AISV.Game.Looks;

/// <summary>
/// 村民临时换的样子：花舞节答应当舞伴的换上舞裙；被炸到的脸上身上沾一阵烟灰（半分钟后自己拍干净）。
/// 都只改贴图，天黑或回标题就换回来。
/// </summary>
public sealed class NpcLooks
{
	private const int SootTicks = 1800;

	private const string DanceFolder = "assets/looks/flower-dance";

	private const string FishingFolder = "assets/looks/fishing";

	private const int DanceTop = 320;

	private readonly IModHelper _helper;

	private readonly ILog _log;

	private readonly HashSet<string> _dancers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _soot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, Texture2D?> _images = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

	public NpcLooks(IModHelper helper, ILog log)
	{
		_helper = helper;
		_log = log;
	}

	public bool IsSooty(string npc) => _soot.ContainsKey(npc);

	/// <summary>正面握竿的姿势图，没有的返回 null。</summary>
	public Texture2D? FishingPose(string npc) => Image(FishingFolder, npc);

	public bool HasDanceOutfit(string npc) => !string.IsNullOrWhiteSpace(npc) && File.Exists(Path.Combine(_helper.DirectoryPath, DanceFolder, npc + ".png"));

	public void WearDanceOutfit(NPC npc)
	{
		if (!HasDanceOutfit(npc.Name) || !_dancers.Add(npc.Name))
		{
			return;
		}
		Refresh(npc.Name);
		_log.Debug(npc.Name + " 换上了花舞节的舞裙。");
	}

	/// <summary>爆炸范围外再多一格内的村民都会沾上。</summary>
	public static float SootReach(int radius) => Math.Max(radius, 1) + 1;

	public void OnExplosion(GameLocation location, Vector2 tile, int radius)
	{
		float reach = SootReach(radius);
		List<string> fresh = new List<string>();
		foreach (NPC npc in location.characters.Where(n => n is { IsVillager: true } && Vector2.Distance(n.Tile, tile) <= reach))
		{
			if (!_soot.ContainsKey(npc.Name))
			{
				fresh.Add(npc.Name);
			}
			_soot[npc.Name] = SootTicks;
		}
		foreach (string name in fresh)
		{
			Refresh(name);
		}
	}

	public void OnUpdateTicked()
	{
		if (_soot.Count == 0 || !Context.IsWorldReady || Game1.paused)
		{
			return;
		}
		foreach (string name in _soot.Keys.ToList())
		{
			if (--_soot[name] > 0)
			{
				continue;
			}
			_soot.Remove(name);
			Refresh(name);
			if (Game1.getCharacterFromName(name) is NPC said)
			{
				NpcSpeech.Say(said, Tr.Get("looks.soot.relief", "呼——干净了。"));
			}
		}
	}

	public void Reset()
	{
		List<string> names = _dancers.Concat(_soot.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		_dancers.Clear();
		_soot.Clear();
		foreach (string name in names)
		{
			Refresh(name);
		}
	}

	public void OnAssetRequested(AssetRequestedEventArgs e)
	{
		if (_dancers.Count == 0 && _soot.Count == 0 || !TryParse(e.NameWithoutLocale.BaseName, out bool portrait, out string npc, out bool variant))
		{
			return;
		}
		bool dance = !portrait && !variant && _dancers.Contains(npc);
		bool soot = _soot.ContainsKey(npc);
		if (!dance && !soot)
		{
			return;
		}
		e.Edit(asset =>
		{
			IAssetDataForImage image = asset.AsImage();
			if (dance && Image(DanceFolder, npc) is Texture2D outfit)
			{
				if (image.Data.Height < DanceTop + outfit.Height || image.Data.Width < outfit.Width)
				{
					image.ExtendImage(Math.Max(image.Data.Width, outfit.Width), DanceTop + outfit.Height);
				}
				image.PatchImage(outfit, targetArea: new Rectangle(0, DanceTop, outfit.Width, outfit.Height), patchMode: PatchMode.Replace);
			}
			if (soot)
			{
				(int w, int h) = portrait ? (64, 64) : FrameSize(npc);
				Blacken(image, w, h, Soot.Seed(npc));
			}
		}, AssetEditPriority.Late + 100);
	}

	private Texture2D? Image(string folder, string npc)
	{
		string path = Path.Combine(_helper.DirectoryPath, folder, npc + ".png");
		if (_images.TryGetValue(path, out Texture2D? cached) && cached is not { IsDisposed: true })
		{
			return cached;
		}
		Texture2D? image = null;
		if (File.Exists(path))
		{
			try
			{
				using FileStream stream = File.OpenRead(path);
				image = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			}
			catch (Exception ex)
			{
				_log.Warn($"读不了 {folder}/{npc}.png：{ex.Message}");
			}
		}
		return _images[path] = image;
	}

	private static void Blacken(IAssetDataForImage image, int frameWidth, int frameHeight, int seed)
	{
		var data = image.Data;
		int width = data.Width;
		int height = data.Height;
		float[] mask = Soot.Mask(width, height, frameWidth, frameHeight, seed);
		Color[] pixels = new Color[width * height];
		data.GetData(pixels);
		for (int i = 0; i < pixels.Length; i++)
		{
			Color c = pixels[i];
			if (c.A == 0)
			{
				continue;
			}
			(byte r, byte g, byte b) = Soot.Apply(c.R, c.G, c.B, mask[i]);
			pixels[i] = new Color(r, g, b, c.A);
		}
		data.SetData(pixels);
	}

	private static (int W, int H) FrameSize(string npc)
	{
		NPC? n = Game1.getCharacterFromName(npc);
		return n?.Sprite is { SpriteWidth: > 0, SpriteHeight: > 0 } s ? (s.SpriteWidth, s.SpriteHeight) : (16, 32);
	}

	private void Refresh(string npc)
	{
		_helper.GameContent.InvalidateCache(a => TryParse(a.NameWithoutLocale.BaseName, out _, out string name, out _) && string.Equals(name, npc, StringComparison.OrdinalIgnoreCase));
		foreach (NPC n in Copies(npc))
		{
			if (n.Sprite != null)
			{
				n.Sprite.loadedTexture = null;
				_ = n.Sprite.Texture;
			}
		}
	}

	private static IEnumerable<NPC> Copies(string npc)
	{
		NPC? world = Game1.getCharacterFromName(npc);
		if (world != null)
		{
			yield return world;
		}
		NPC? actor = Game1.CurrentEvent?.getActorByName(npc);
		if (actor != null && actor != world)
		{
			yield return actor;
		}
	}

	/// <summary>Characters/名字 或 Portraits/名字，带下划线的是季节或场合的变体（Abigail_Beach）。</summary>
	private static bool TryParse(string baseName, out bool portrait, out string npc, out bool variant)
	{
		portrait = false;
		npc = "";
		variant = false;
		string rest;
		if (baseName.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase))
		{
			rest = baseName["Characters/".Length..];
		}
		else if (baseName.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase))
		{
			portrait = true;
			rest = baseName["Portraits/".Length..];
		}
		else
		{
			return false;
		}
		if (rest.Length == 0 || rest.Contains('/'))
		{
			return false;
		}
		int cut = rest.IndexOf('_');
		variant = cut > 0;
		npc = variant ? rest[..cut] : rest;
		return npc.Length > 0;
	}
}
