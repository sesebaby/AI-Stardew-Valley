using System;
using System.Collections.Generic;
using System.Linq;

namespace AISV.Core.Awareness;

public sealed record BubbleCandidate(string Npc, string Role, int Certainty, double DistanceTiles, string Bucket, bool Busy);

public sealed record BubbleChoice(string Npc, string Text, int? Emote);

/// <summary>
/// 小事冒一句：每件事最多一个人说；同一人两句之间隔 900 tick；每人每类每天有上限；不重复上一句。
/// 优先牵涉者（target、vendor、relative），再按关系（配偶、恋人、孩子、朋友）和距离。
/// </summary>
public sealed class Bubbles
{
	public const long CooldownTicks = 900;

	private readonly Random _random;

	private readonly Dictionary<string, int> _shownToday = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, long> _lastTick = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> _lastLine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public Bubbles(Random? random = null)
	{
		_random = random ?? new Random();
	}

	public void Reset()
	{
		_shownToday.Clear();
		_lastTick.Clear();
		_lastLine.Clear();
	}

	public int ShownToday(string npc, string type) => _shownToday.GetValueOrDefault(npc + "|" + type);

	/// <param name="resolve">(说话人, 变量名) → 值。</param>
	public BubbleChoice? Choose(string type, BubbleDefinition bubble, IReadOnlyList<BubbleCandidate> candidates, long nowTick, Func<string, string> translate, Func<string, string, string?> resolve)
	{
		if (candidates.Count == 0 || (bubble.EffectiveChance < 1.0 && _random.NextDouble() >= bubble.EffectiveChance))
		{
			return null;
		}
		BubbleCandidate? speaker = candidates
			.Where(c => !c.Busy && !string.IsNullOrWhiteSpace(c.Npc))
			.Where(c => c.Certainty >= bubble.EffectiveMinCertainty)
			.Where(c => bubble.AllowsRole(string.IsNullOrWhiteSpace(c.Role) ? "bystander" : c.Role))
			.Where(c => ShownToday(c.Npc, type) < bubble.EffectiveMaxPerNpcPerDay)
			.Where(c => !_lastTick.TryGetValue(c.Npc, out long t) || nowTick - t >= CooldownTicks)
			.Where(c => bubble.LinesFor(c.Bucket).Count > 0)
			.OrderBy(c => RoleRank(c.Role))
			.ThenBy(c => BucketRank(c.Bucket))
			.ThenBy(c => c.DistanceTiles)
			.FirstOrDefault();
		if (speaker == null)
		{
			return null;
		}
		IReadOnlyList<string> lines = bubble.LinesFor(speaker.Bucket);
		string key = speaker.Npc + "|" + type;
		List<string> pool = lines.Count > 1 && _lastLine.TryGetValue(key, out string? last) ? lines.Where(l => l != last).ToList() : lines.ToList();
		if (pool.Count == 0)
		{
			pool = lines.ToList();
		}
		string raw = pool[_random.Next(pool.Count)];
		string text = PhraseTemplate.Parse(translate(raw)).Render(token => resolve(speaker.Npc, token)).Trim();
		if (text.Length == 0)
		{
			return null;
		}
		_lastLine[key] = raw;
		_shownToday[key] = ShownToday(speaker.Npc, type) + 1;
		_lastTick[speaker.Npc] = nowTick;
		return new BubbleChoice(speaker.Npc, text, bubble.EmoteIndex);
	}

	private static int RoleRank(string role) => role?.ToLowerInvariant() switch
	{
		"target" => 0,
		"vendor" => 1,
		"relative" => 2,
		_ => 3
	};

	private static int BucketRank(string bucket) => bucket?.ToLowerInvariant() switch
	{
		"spouse" => 0,
		"partner" => 1,
		"child" => 2,
		"friend" => 3,
		_ => 4
	};
}
