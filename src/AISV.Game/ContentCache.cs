using System;
using System.Collections.Generic;
using System.IO;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace AISV.Game;

public static class ContentCache
{
	private const string EventsPrefix = "Data/Events/";

	/// <summary>把一个资源连同各语言版本（比如 Data/Events/Town.zh-CN）一起从缓存里清掉。</summary>
	internal static void InvalidateAllLocales(this IGameContentHelper content, string assetName) =>
		content.InvalidateCache(a => a.NameWithoutLocale.IsEquivalentTo(assetName));

	/// <summary>游戏没有事件表的地点（JojaMart、铁匠铺等）先给一张空表，之后才能往里加事件。</summary>
	public static void ProvideMissingEventTable(AssetRequestedEventArgs e)
	{
		string name = e.NameWithoutLocale.BaseName;
		if (name.StartsWith(EventsPrefix, StringComparison.OrdinalIgnoreCase)
			&& !File.Exists(Path.Combine(Constants.GamePath, "Content", "Data", "Events", name[EventsPrefix.Length..] + ".xnb")))
		{
			e.LoadFrom(() => new Dictionary<string, string>(), AssetLoadPriority.Low);
		}
	}
}
