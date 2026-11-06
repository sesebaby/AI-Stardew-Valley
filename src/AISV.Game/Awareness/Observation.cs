using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AISV.Game.Awareness;

/// <summary>传感器看到的一件事，交给 <see cref="AwarenessSystem.Record"/>。</summary>
/// <param name="Type">事件类型（catalog.json 里的键）。</param>
/// <param name="Actor">做事的人（通常是农夫）。</param>
/// <param name="Subject">被做的村民（收礼人、被浇水的人……）。</param>
/// <param name="Location">地图名。</param>
/// <param name="Summary">一句话摘要（没有写法时的后备，也是显式反应的描述）。</param>
/// <param name="AlsoLocation">事件同时发生在另一张图上（比如门口两侧）。</param>
/// <param name="Exclude">不算目击者的村民。</param>
public sealed record Observation(
	string Type,
	string Actor,
	string? Subject,
	string Location,
	string Summary,
	Vector2? Tile = null,
	IReadOnlyDictionary<string, string>? Details = null,
	string? AlsoLocation = null,
	Vector2? AlsoTile = null,
	IReadOnlyList<string>? Exclude = null)
{
	public string Detail(string key) => Details != null && Details.TryGetValue(key, out string? v) ? v : "";

	public bool Flag(string key) => string.Equals(Detail(key), "true", StringComparison.OrdinalIgnoreCase);

	public static Dictionary<string, string> Map(params (string Key, string Value)[] pairs)
	{
		Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach ((string k, string v) in pairs)
		{
			d[k] = v;
		}
		return d;
	}
}
