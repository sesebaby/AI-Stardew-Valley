using System;
using System.Collections.Generic;
using System.IO;
using AISV.Core.Dialogue;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Dialogue;

/// <summary>个别村民某种情绪的专用立绘（单格 64×64），用到时才读。</summary>
public static class PortraitOverrides
{
	private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);

	public static string Directory { get; set; } = "";

	public static Texture2D? For(string npc, string? emotion)
	{
		string? name = Emotion.PortraitOverride(npc, emotion);
		if (name == null || Directory.Length == 0)
		{
			return null;
		}
		if (Cache.TryGetValue(name, out Texture2D? cached) && cached is not { IsDisposed: true })
		{
			return cached;
		}
		Texture2D? image = null;
		string path = Path.Combine(Directory, "assets", "portraits", name + ".png");
		try
		{
			if (File.Exists(path))
			{
				using FileStream stream = File.OpenRead(path);
				image = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
			}
		}
		catch
		{
			image = null;
		}
		return Cache[name] = image;
	}
}
