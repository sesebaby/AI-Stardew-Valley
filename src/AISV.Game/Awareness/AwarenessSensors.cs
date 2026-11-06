using System;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AISV.Game.Awareness;

/// <summary>所有传感器和陪钓的总开关：装钩子、分发事件。</summary>
public sealed class AwarenessSensors
{
	private readonly AwarenessSystem _system;

	private readonly PlaceSensors _places;

	private readonly WatchSensors _watch;

	private readonly FishingCompanion _fishing;

	public AwarenessSensors(AwarenessSystem system, Func<ModConfig> config, Func<string, bool> isNpcEnabled, Func<bool> isDayTransition, Func<string, Texture2D?> fishingPose, ILog log)
	{
		_system = system;
		_places = new PlaceSensors(system, () => config().NoticeVisitors, log);
		_watch = new WatchSensors(system, isNpcEnabled, isDayTransition, log);
		_fishing = new FishingCompanion(() => system.Enabled, () => config().FishTogetherPercent, system.IsNpcInApproach, fishingPose, log);
		system.Fishing = _fishing;
		system.IsBusyElsewhere = _fishing.IsNpcBusy;
	}

	/// <summary>本机农夫弄出的爆炸：地点、中心格、半径。</summary>
	public event Action<GameLocation, Microsoft.Xna.Framework.Vector2, int>? Exploded
	{
		add => ActionPatches.Exploded += value;
		remove => ActionPatches.Exploded -= value;
	}

	/// <summary>正被跟着、已经起疑的村民。</summary>
	public string? AttentionNpc => _watch.AttentionNpc;

	public void Apply(Harmony harmony, ILog log)
	{
		ActionPatches.Apply(harmony, _system, log);
		FishingPose.Configure(harmony, log);
	}

	public void Reset()
	{
		ActionPatches.Reset();
		_places.Reset();
		_watch.Reset();
	}

	public void OnSaveLoaded()
	{
		_places.Clear();
		Reset();
		_fishing.Reset("换了存档");
	}

	public void OnDayStarted()
	{
		Reset();
		_fishing.Reset("新的一天");
	}

	public void OnDayEnding()
	{
		Reset();
		_fishing.Reset("天黑了");
	}

	public void OnSaving() => _fishing.Abort("存档");

	public void OnReturnedToTitle()
	{
		_places.Clear();
		Reset();
		_fishing.Reset("回到标题");
	}

	public void OnUpdateTicked()
	{
		_fishing.OnUpdateTicked();
		if (_system.Enabled)
		{
			_watch.OnUpdateTicked();
			_places.OnUpdateTicked();
		}
	}

	public void OnWarped(WarpedEventArgs e) => _places.OnWarped(e);

	public void OnNpcListChanged(NpcListChangedEventArgs e) => _places.OnNpcListChanged(e);

	public void OnMenuChanged(IClickableMenu? oldMenu, IClickableMenu? newMenu) => _places.OnMenuChanged(oldMenu, newMenu);

	public void OnGift(NPC npc, StardewValley.Object item) => _watch.OnGift(npc, item);

	public void Draw(SpriteBatch b) => _fishing.Draw(b);

	public string DescribeRooms() => _places.DescribeRooms(Game1.currentLocation);
}
