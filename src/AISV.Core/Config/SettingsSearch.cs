using System;
using System.Linq;

namespace AISV.Core.Config;

/// <summary>设置页的搜索：用空格隔开的每个词都要在某一栏里找到。</summary>
public static class SettingsSearch
{
	public static bool Matches(string query, params string[] fields)
	{
		string[] words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		return words.Length > 0 && words.All(w => fields.Any(f => (f ?? "").Contains(w, StringComparison.OrdinalIgnoreCase)));
	}
}
