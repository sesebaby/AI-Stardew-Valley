using System;
using System.Collections.Generic;

namespace AISV.Core.Compat;

/// <summary>物品、作物是哪个 mod 加的：看键或 ID 的前缀。</summary>
public static class ContentOrigin
{
	/// <summary>依次看每个键或 ID（去掉 "(O)" 这类前缀），以哪个已装 mod 的 UniqueID 开头、后面紧跟 "_" 或 "."（取最长的）；游戏自带或认不出为空。</summary>
	public static string? Of(IReadOnlyList<string> mods, params string?[] ids)
	{
		foreach (string? raw in ids)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				continue;
			}
			string id = raw.Trim();
			int close = id.IndexOf(')');
			if (id.StartsWith('(') && close > 0)
			{
				id = id[(close + 1)..];
			}
			string? best = null;
			foreach (string mod in mods)
			{
				if (id.Length > mod.Length && (id[mod.Length] == '_' || id[mod.Length] == '.') && mod.Length > (best?.Length ?? 0) && id.StartsWith(mod, StringComparison.OrdinalIgnoreCase))
				{
					best = mod;
				}
			}
			if (best != null)
			{
				return best;
			}
		}
		return null;
	}

	/// <summary>同一个下载文件夹里的包算一家：所有 mod 目录的共同上级下面第一层文件夹名就是这家的键（mod ID → 键）。</summary>
	public static IReadOnlyDictionary<string, string> Families(IReadOnlyList<ModFolder> folders)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (folders.Count < 2)
		{
			return result;
		}
		static string[] Split(string dir) => dir.TrimEnd('\\', '/').Split('\\', '/');
		string[] first = Split(folders[0].Directory);
		int common = first.Length - 1;
		foreach (ModFolder f in folders)
		{
			string[] parts = Split(f.Directory);
			int i = 0;
			while (i < common && i < parts.Length - 1 && string.Equals(parts[i], first[i], StringComparison.OrdinalIgnoreCase))
			{
				i++;
			}
			common = i;
		}
		foreach (ModFolder f in folders)
		{
			string[] parts = Split(f.Directory);
			if (parts.Length > common)
			{
				result[f.Id] = parts[common];
			}
		}
		return result;
	}
}
