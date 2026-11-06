using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Dialogue;

namespace AISV.Core.Social.Diary;

/// <summary>一幅夹在日记里的画，图片在 assets/diary。</summary>
public sealed record DiarySketch(string File, string Npc);

public sealed record PinnedSketch(DiarySketch Sketch, DiaryEntry Entry);

/// <summary>
/// 日记写到某位村民时，把他的一幅画夹在那一天。每天的位置只由日记内容决定，不用另外存档。
/// </summary>
public static class DiarySketches
{
	public const string CoverFile = "cover.png";

	/// <summary>同一位村民两幅画至少隔这么多天，免得一下子全用完。</summary>
	public const int RestDays = 14;

	private static readonly string[] Villagers =
	{
		"Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Dwarf", "Elliott", "Emily", "Evelyn", "George", "Gus", "Haley", "Harvey", "Jas", "Jodi", "Kent", "Krobus",
		"Leah", "Leo", "Lewis", "Linus", "Marnie", "Maru", "Pam", "Penny", "Pierre", "Robin", "Sam", "Sandy", "Sebastian", "Shane", "Vincent", "Willy", "Wizard"
	};

	public static readonly IReadOnlyList<DiarySketch> All = Villagers.Select(n => new DiarySketch(n.ToLowerInvariant() + ".png", n)).Append(new DiarySketch("haley-2.png", "Haley")).ToList();

	public static List<PinnedSketch> Pin(IEnumerable<DiaryEntry> entries, Func<string, string> displayName, IReadOnlyList<DiarySketch>? sketches = null)
	{
		Dictionary<string, Queue<DiarySketch>> waiting = (sketches ?? All).GroupBy(s => s.Npc, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => new Queue<DiarySketch>(g), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> lastDay = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		List<PinnedSketch> pinned = new List<PinnedSketch>();
		foreach ((DiaryEntry entry, int day) in entries.Select(e => (e, new GameDate(e.Day, e.Season, e.Year).TotalDays)).OrderBy(x => x.TotalDays))
		{
			string? npc = waiting.Keys.Where(n => waiting[n].Count > 0 && (!lastDay.TryGetValue(n, out int last) || day - last >= RestDays))
				.FirstOrDefault(n => Mentions(entry.Text, n) || Mentions(entry.Text, displayName(n)));
			if (npc != null)
			{
				pinned.Add(new PinnedSketch(waiting[npc].Dequeue(), entry));
				lastDay[npc] = day;
			}
		}
		return pinned;
	}

	/// <summary>写到了这个名字。英文名要是完整的词，same 不算写到 Sam。</summary>
	public static bool Mentions(string text, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		for (int at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase))
		{
			bool joinedBefore = at > 0 && char.IsAscii(text[at - 1]) && char.IsLetter(text[at - 1]) && char.IsAscii(name[0]);
			int end = at + name.Length;
			bool joinedAfter = end < text.Length && char.IsAscii(text[end]) && char.IsLetter(text[end]) && char.IsAscii(name[^1]);
			if (!joinedBefore && !joinedAfter)
			{
				return true;
			}
		}
		return false;
	}
}
