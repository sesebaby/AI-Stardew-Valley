using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.World;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TokenizableStrings;

namespace AISV.Game.World;

/// <summary>扫一遍各地点的 Data/Events，内容包加的心事件也算在内。</summary>
public static class HeartEventSource
{
	public static void Refresh(IModHelper helper, ILog log)
	{
		IEnumerable<string> names = Game1.locations.Select(l => l.Name).Concat(DataLoader.Locations(Game1.content).Keys).Distinct(StringComparer.OrdinalIgnoreCase);
		List<KeyValuePair<string, string>> events = new List<KeyValuePair<string, string>>();
		foreach (string name in names)
		{
			try
			{
				IAssetName asset = helper.GameContent.ParseAssetName("Data/Events/" + name);
				if (helper.GameContent.DoesAssetExist<Dictionary<string, string>>(asset))
				{
					events.AddRange(helper.GameContent.Load<Dictionary<string, string>>(asset)
						.Select(e => new KeyValuePair<string, string>(e.Key, TokenParser.ParseText(e.Value) ?? "")));
				}
			}
			catch (Exception ex)
			{
				log.Warn($"读 {name} 的事件表失败：{ex.Message}");
			}
		}
		WorldData.UseHeartEvents(HeartEventScan.Collect(events));
	}
}
