using System;
using System.Text;
using AISV.Core.Dialogue;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using StardewValley;

namespace AISV.UI.Shell;

internal static class ShellText
{
	/// <summary>去掉游戏字体画不出的表情符号。</summary>
	public static string Clean(string? text) => TextTools.CleanForGame(text);

	public static string Season(string? season) => HistoryFormatter.SeasonName(season);

	public static string Date(int day, string? season, int year) => Tr.Get("shell.date", "第 {{year}} 年{{season}} {{day}} 日", ("year", year), ("season", Season(season)), ("day", day));

	public static string SeasonGroup(int year, string? season) => Tr.Get("shell.seasonGroup", "第 {{year}} 年 · {{season}}", ("year", year), ("season", Season(season)));

	public static string Weather() => Game1.isLightning ? Tr.Get("shell.weather.storm", "雷雨")
		: Game1.isRaining ? Tr.Get("shell.weather.rain", "下雨")
		: Game1.isSnowing ? Tr.Get("shell.weather.snow", "下雪")
		: Game1.isDebrisWeather ? Tr.Get("shell.weather.wind", "刮风")
		: Tr.Get("shell.weather.sun", "晴");

	public static string Greeting()
	{
		int t = Game1.timeOfDay;
		return t < 1200 ? Tr.Get("shell.greet.morning", "早上好") : t < 1800 ? Tr.Get("shell.greet.afternoon", "下午好") : Tr.Get("shell.greet.evening", "晚上好");
	}

	public static string TopLine() => Clean(Utility.getDateString()) + " · " + Weather() + " · " + Game1.getTimeOfDayString(Game1.timeOfDay);

	public static string DaysAgo(int days, int day, string? season) => days <= 0 ? Tr.Get("shell.when.today", "今天")
		: days == 1 ? Tr.Get("shell.when.yesterday", "昨天")
		: days < 7 ? Tr.Get("shell.when.days", "{{n}} 天前", ("n", days))
		: Season(season) + " " + day;

	public static string DiaryWeather(string? weather) => (weather ?? "").Trim().ToLowerInvariant() switch
	{
		"sunny" => Tr.Get("shell.diaryWeather.sunny", "晴"),
		"rainy" => Tr.Get("shell.diaryWeather.rainy", "雨"),
		"stormy" => Tr.Get("shell.diaryWeather.stormy", "雷雨"),
		"snowy" => Tr.Get("shell.diaryWeather.snowy", "雪"),
		"windy" => Tr.Get("shell.diaryWeather.windy", "风"),
		_ => ""
	};

	public static bool IsToday(int day, string? season, int year) => GameState.Today.SameDay(day, season ?? "", year);

	public static NPC? Npc(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		try
		{
			return GameState.FindNpc(name);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
