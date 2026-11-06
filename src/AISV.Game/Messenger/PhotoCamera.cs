using System;
using AISV.Core.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using xTile;
using xTile.Layers;

namespace AISV.Game.Messenger;

/// <summary>
/// 给村民拍照：把他所在地图以他为中心的一块（640×448）画到离屏纹理上；自拍时再在下方叠上他的头像。
/// 只在主线程调用，画完把视口和渲染目标还原。
/// </summary>
public static class PhotoCamera
{
	public const int Width = 640;

	public const int Height = 448;

	private static readonly Color NightTint = new Color(18, 22, 58);

	private static readonly Color RainTint = new Color(70, 96, 150);

	private const float MaxNightDarkness = 0.6f;

	private const float RainStrength = 0.18f;

	private const float IndoorDarkness = 0.7f;

	private const int PortraitCell = 64;

	private static SpriteBatch? _batch;

	public static Texture2D? Capture(NPC npc, ILog log, bool selfie)
	{
		GameLocation? place = npc.currentLocation;
		Map? map = place?.Map;
		GraphicsDevice? device = Game1.graphics?.GraphicsDevice;
		if (place == null || map == null || device == null || Game1.mapDisplayDevice == null)
		{
			return null;
		}
		Layer? back = map.GetLayer("Back");
		int mapW = (back?.LayerWidth ?? 0) * 64, mapH = (back?.LayerHeight ?? 0) * 64;
		Rectangle box = npc.GetBoundingBox();
		Point focus = new Point(box.Center.X, box.Center.Y - 48);
		xTile.Dimensions.Rectangle view = new xTile.Dimensions.Rectangle(Math.Max(0, Math.Min(focus.X - Width / 2, mapW - Width)), Math.Max(0, Math.Min(focus.Y - Height / 2, mapH - Height)), Width, Height);
		RenderTargetBinding[] previousTargets = device.GetRenderTargets();
		xTile.Dimensions.Rectangle previousView = Game1.viewport;
		RenderTarget2D? target = null;
		try
		{
			map.LoadTileSheets(Game1.mapDisplayDevice);
			target = new RenderTarget2D(device, Width, Height, false, SurfaceFormat.Color, DepthFormat.None);
			device.SetRenderTarget(target);
			device.Clear(Color.Black);
			Game1.viewport = view;
			SpriteBatch b = _batch ??= new SpriteBatch(device);
			Game1.mapDisplayDevice.BeginScene(b);
			DrawPlace(b, place, view, log);
			if (selfie)
			{
				Pass(b, SpriteSortMode.Deferred, log, () => DrawSelfie(b, npc));
			}
			Color[] pixels = new Color[Width * Height];
			device.SetRenderTargets(previousTargets);
			target.GetData(pixels);
			Texture2D photo = new Texture2D(device, Width, Height);
			photo.SetData(pixels);
			return photo;
		}
		catch (Exception ex)
		{
			log.Info($"信使没能在 {place.Name} 给 {npc.Name} 拍照：{ex.Message}");
			return null;
		}
		finally
		{
			Game1.viewport = previousView;
			if (Game1.spriteBatch != null)
			{
				Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
			}
			device.SetRenderTargets(previousTargets);
			target?.Dispose();
		}
	}

	private static void DrawPlace(SpriteBatch b, GameLocation place, xTile.Dimensions.Rectangle view, ILog log)
	{
		Pass(b, SpriteSortMode.Deferred, log, () => place.drawBackground(b));
		foreach (var layer in place.backgroundLayers)
		{
			Pass(b, SpriteSortMode.Texture, log, () => DrawLayer(layer.Key, view, -1f));
		}
		Pass(b, SpriteSortMode.Deferred, log, () => place.drawWater(b));
		Pass(b, SpriteSortMode.FrontToBack, log, () => place.drawFloorDecorations(b));
		Pass(b, SpriteSortMode.Deferred, log, () =>
		{
			if (!place.shouldHideCharacters())
			{
				foreach (NPC c in place.characters)
				{
					if (c != null && !c.swimming.Value && !c.HideShadow && !c.IsInvisible)
					{
						c.DrawShadow(b);
					}
				}
			}
			for (int i = 0; i < place.buildingLayers.Count; i++)
			{
				DrawLayer(place.buildingLayers[i].Key, view, place.buildingLayers.Count > 1 ? 0.1f * i / (place.buildingLayers.Count - 1) : 0f);
			}
		});
		Pass(b, SpriteSortMode.FrontToBack, log, () =>
		{
			place.draw(b);
			for (int i = 0; i < place.frontLayers.Count; i++)
			{
				DrawLayer(place.frontLayers[i].Key, view, 64f + (place.frontLayers.Count > 1 ? 0.1f * i / (place.frontLayers.Count - 1) : 0f));
			}
			place.drawAboveFrontLayer(b);
		});
		foreach (var layer in place.alwaysFrontLayers)
		{
			Pass(b, SpriteSortMode.Texture, log, () => DrawLayer(layer.Key, view, -1f));
		}
		Pass(b, SpriteSortMode.Deferred, log, () =>
		{
			Rectangle frame = new Rectangle(0, 0, Width, Height);
			int now = Utility.ConvertTimeToMinutes(Game1.timeOfDay);
			int dusk = Utility.ConvertTimeToMinutes(Game1.getStartingToGetDarkTime(place));
			int night = Utility.ConvertTimeToMinutes(Game1.getTrulyDarkTime(place));
			if (place.IsOutdoors)
			{
				float depth = Math.Clamp((now - dusk) / (float)Math.Max(1, night - dusk), 0f, 1f);
				if (depth > 0f)
				{
					b.Draw(Game1.staminaRect, frame, NightTint * (depth * MaxNightDarkness));
				}
				if (place.IsRainingHere())
				{
					b.Draw(Game1.staminaRect, frame, RainTint * RainStrength);
				}
			}
			else if (now < night)
			{
				float gloom = RoomGloom(place);
				if (gloom > 0f)
				{
					b.Draw(Game1.staminaRect, frame, Color.Black * (gloom * IndoorDarkness));
				}
			}
		});
	}

	private static void DrawSelfie(SpriteBatch b, NPC npc)
	{
		Texture2D? face = npc.Portrait;
		if (face == null || face.IsDisposed || face.Width < PortraitCell || face.Height < PortraitCell)
		{
			return;
		}
		Rectangle cell = new Rectangle(face.Width >= PortraitCell * 2 ? PortraitCell : 0, 0, PortraitCell, PortraitCell);
		const int side = PortraitCell * 5;
		Rectangle spot = new Rectangle((Width - side) / 2, Height - side, side, side);
		b.Draw(Game1.staminaRect, new Rectangle(0, Height - side / 3, Width, side / 3), Color.Black * 0.28f);
		b.Draw(face, new Rectangle(spot.X + 8, spot.Y + 8, side, side), cell, Color.Black * 0.35f);
		b.Draw(face, spot, cell, Color.White);
	}

	private static float RoomGloom(GameLocation place)
	{
		string[] channels = (place.getMapProperty("AmbientLight") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		float sum = 0f;
		foreach (string channel in channels)
		{
			sum += int.TryParse(channel, out int value) ? Math.Clamp(value, 0, 255) : 0;
		}
		return channels.Length == 0 ? 0f : sum / (channels.Length * 255f);
	}

	private static void DrawLayer(Layer layer, xTile.Dimensions.Rectangle view, float depth) => layer.Draw(Game1.mapDisplayDevice, view, xTile.Dimensions.Location.Origin, false, 4, depth);

	private static void Pass(SpriteBatch b, SpriteSortMode mode, ILog log, Action draw)
	{
		b.Begin(mode, BlendState.AlphaBlend, SamplerState.PointClamp);
		try
		{
			draw();
		}
		catch (Exception ex)
		{
			log.Debug("拍照时跳过了一部分：" + ex.Message);
		}
		finally
		{
			b.End();
		}
	}
}
