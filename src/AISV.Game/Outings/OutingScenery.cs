using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Outings;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace AISV.Game.Outings;

/// <summary>出游场景的布景：用临时精灵把家具摆在两人中间，晚上或室内的灯会亮，事件结束时清掉。</summary>
internal static class OutingScenery
{
	public static void Place(Event scene, GameLocation location, string npcName, IReadOnlyList<OutingProp> props)
	{
		NPC? npc = scene.getActorByName(npcName);
		if (npc == null)
		{
			return;
		}
		Point farmer = Game1.player.TilePoint, other = npc.TilePoint;
		int x = (farmer.X + other.X) / 2, y = Math.Max(farmer.Y, other.Y);
		HashSet<(int X, int Y)> actors = scene.actors.Select(a => (a.TilePoint.X, a.TilePoint.Y)).ToHashSet();
		actors.Add((farmer.X, farmer.Y));
		IReadOnlyList<OutingProp> fit = OutingLayout.FittingProps(props, x, y, actors, (tx, ty) => OutingService.Clear(location, tx, ty));
		List<TemporaryAnimatedSprite> sprites = new List<TemporaryAnimatedSprite>();
		void Cleanup()
		{
			scene.onEventFinished = (Action?)Delegate.Remove(scene.onEventFinished, (Action)Cleanup);
			foreach (TemporaryAnimatedSprite s in sprites)
			{
				location.TemporarySprites.Remove(s);
				if (s.lightId != null)
				{
					Game1.currentLightSources.Remove(s.lightId);
				}
			}
		}
		scene.onEventFinished = (Action?)Delegate.Combine(scene.onEventFinished, (Action)Cleanup);
		try
		{
			foreach (OutingProp p in fit)
			{
				ParsedItemData? data = ItemRegistry.GetData(p.ItemId);
				if (data == null)
				{
					continue;
				}
				Rectangle source = data.GetSourceRect();
				int px = (x + p.X) * 64;
				int bottom = (y + p.Y + p.Height) * 64;
				float depth = p.Floor ? 0.0001f : bottom / 10000f;
				TemporaryAnimatedSprite sprite = new TemporaryAnimatedSprite(data.TextureName, source, new Vector2(px, bottom - source.Height * 4), false, 0f, Color.White)
				{
					scale = 4f,
					layerDepth = depth,
					interval = 1000f,
					animationLength = 1,
					totalNumberOfLoops = int.MaxValue,
					Parent = location
				};
				if (p.LightRadius > 0f && (!location.IsOutdoors || Game1.timeOfDay >= 1800))
				{
					sprite.lightId = scene.GenerateLightSourceId("AisvOutingScenery_" + sprites.Count);
					sprite.lightRadius = p.LightRadius;
					sprite.lightcolor = new Color(0, 55, 110);
				}
				sprites.Add(sprite);
				location.TemporarySprites.Add(sprite);
				if (p.Flame && sprite.lightId != null)
				{
					TemporaryAnimatedSprite flame = new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(276, 1985, 12, 11), new Vector2(px + 8, (y + p.Y) * 64 - 56), false, 0f, Color.White)
					{
						scale = 4f,
						layerDepth = depth + 0.0001f,
						interval = 100f,
						animationLength = 4,
						totalNumberOfLoops = int.MaxValue,
						Parent = location
					};
					sprites.Add(flame);
					location.TemporarySprites.Add(flame);
				}
			}
		}
		catch
		{
			Cleanup();
			throw;
		}
	}
}
