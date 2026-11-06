using System;
using System.Reflection;
using AISV.Core.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.Game.Awareness;

/// <summary>陪钓的人正面握竿的样子。NPC.draw 的钩子只在有人钓鱼时挂上，收竿就摘掉。</summary>
internal static class FishingPose
{
	private const float Height = 128f;

	private const float AnchorY = 0.75f;

	private static readonly MethodInfo? Target = AccessTools.Method(typeof(NPC), nameof(NPC.draw), new[] { typeof(SpriteBatch), typeof(float) });

	private static Harmony? _harmony;

	private static ILog _log = NullLog.Instance;

	private static NPC? _npc;

	private static Texture2D? _texture;

	private static bool _patched;

	public static void Configure(Harmony harmony, ILog log)
	{
		_harmony = harmony;
		_log = log;
	}

	public static bool IsShowing(NPC npc) => _patched && _npc == npc && _texture is { IsDisposed: false };

	public static void Show(NPC npc, Texture2D texture)
	{
		_npc = npc;
		_texture = texture;
		if (_patched || _harmony == null || Target == null)
		{
			return;
		}
		try
		{
			_harmony.Patch(Target, prefix: new HarmonyMethod(typeof(FishingPose), nameof(Prefix)));
			_patched = true;
		}
		catch (Exception ex)
		{
			_log.Warn("陪钓姿势画不了，用平常的样子：" + ex.Message);
		}
	}

	public static void Hide()
	{
		_npc = null;
		_texture = null;
		if (!_patched || _harmony == null || Target == null)
		{
			return;
		}
		try
		{
			_harmony.Unpatch(Target, AccessTools.Method(typeof(FishingPose), nameof(Prefix)));
		}
		catch (Exception ex)
		{
			_log.Warn("摘陪钓姿势的钩子失败：" + ex.Message);
		}
		_patched = false;
	}

	private static bool Prefix(NPC __instance, SpriteBatch __0, float __1)
	{
		if (__instance != _npc || _texture is not { IsDisposed: false } texture || __instance.IsInvisible)
		{
			return true;
		}
		try
		{
			Vector2 position = __instance.getLocalPosition(Game1.viewport) + new Vector2(__instance.GetSpriteWidthForPositioning() * 4 / 2, __instance.GetBoundingBox().Height / 2);
			if (__instance.shakeTimer > 0)
			{
				position += new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2));
			}
			float depth = Math.Max(0f, __instance.drawOnTop ? 0.991f : __instance.StandingPixel.Y / 10000f);
			__0.Draw(texture, position, null, Color.White * __1, 0f, new Vector2(texture.Width / 2f, texture.Height * AnchorY), Height / texture.Height, SpriteEffects.None, depth);
			if (!Game1.eventUp)
			{
				__instance.DrawEmote(__0);
			}
			return false;
		}
		catch (Exception ex)
		{
			_npc = null;
			_log.Warn("陪钓姿势画失败，换回平常的样子：" + ex.Message);
			return true;
		}
	}
}
