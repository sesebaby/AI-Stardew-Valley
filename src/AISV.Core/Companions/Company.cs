using System;
using System.Linq;

namespace AISV.Core.Companions;

/// <summary>
/// 村民找你、陪你走的规矩：哪些地方不跟进去，谁的家要够熟才进，陪多久，路上说几句。
/// </summary>
public static class CompanyRules
{
	/// <summary>见到以后陪你走多久（分钟）。</summary>
	public const int CompanyMinutes = 120;

	/// <summary>陪到最晚几点。</summary>
	public const int LatestEnd = 2200;

	/// <summary>到点过了这么久你还没来，就去附近的屋里找你。</summary>
	public const int SeekAfterMinutes = 20;

	/// <summary>同一张地图上，离你这么近（格）就看见你、迎上来。</summary>
	public const int SeeTiles = 24;

	/// <summary>进你家（农舍、小屋）要几心；伴侣、恋人不用。</summary>
	public const int HomeHearts = 4;

	/// <summary>陪着走时一次约见最多冒几句。</summary>
	public const int MaxRemarks = 3;

	/// <summary>两句之间至少隔几分钟（游戏时间）。</summary>
	public const int RemarkGapMinutes = 30;

	/// <summary>欠债的人最晚几点还找不到就照老办法收（钱直接扣）。</summary>
	public const int CollectGiveUpTime = 2000;

	/// <summary>上门帮工到了以后，等几分钟没见到你就先干活。</summary>
	public const int HelperGreetMinutes = 60;

	private static readonly string[] NoFollowPrefixes =
	{
		"UndergroundMine", "SkullCave", "VolcanoDungeon", "Caldera", "Club", "BathHouse_MensLocker", "BathHouse_WomensLocker",
		"Sewer", "BugLand", "WitchSwamp", "WitchHut", "WitchWarpCave", "WizardHouseBasement", "QiNutRoom", "MovieTheater", "Island"
	};

	/// <summary>这些地方不跟进去（矿洞、赌场、更衣室、下水道、女巫那边、岛上……）。</summary>
	public static bool NoFollow(string location) =>
		string.IsNullOrEmpty(location) || NoFollowPrefixes.Any(p => location.StartsWith(p, StringComparison.OrdinalIgnoreCase));

	/// <summary>农夫自己的家：够熟（或是伴侣、恋人）才进门，不然在门口等。</summary>
	public static bool MayEnterHome(int hearts, bool partner) => partner || hearts >= HomeHearts;

	/// <summary>陪到几点：见到后两小时，最晚 22 点，不早于现在。</summary>
	public static int End(int now) => Math.Max(now, Math.Min(Clock.Add(now, CompanyMinutes), LatestEnd));

	/// <summary>该不该去屋里找：到点过了 <see cref="SeekAfterMinutes"/> 分钟。</summary>
	public static bool TimeToSeek(int meetupTime, int now) => Clock.Minutes(now) >= Clock.Minutes(meetupTime) + SeekAfterMinutes;

	/// <summary>能不能再冒一句：没超过次数，离上一句够久（last &lt; 0 表示还没说过）。</summary>
	public static bool MayRemark(int said, int last, int now) => said < MaxRemarks && (last < 0 || Clock.Minutes(now) - Clock.Minutes(last) >= RemarkGapMinutes);
}
