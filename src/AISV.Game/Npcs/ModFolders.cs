using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AISV.Core.Compat;
using StardewModdingAPI;

namespace AISV.Game.Npcs;

/// <summary>已加载 mod 的文件夹。SMAPI 的 IModInfo 实现有公开的 DirectoryPath（接口上没有），用反射读，不用去扫整个 Mods 文件夹找 manifest。</summary>
public static class ModFolders
{
	public const string ContentPatcherId = "Pathoschild.ContentPatcher";

	/// <summary>一个文件夹都没读出来时（SMAPI 改了内部实现）记一条警告：归属扫描和排除名单会因此失效。</summary>
	public static Action<string>? Warn { get; set; }

	public static IReadOnlyList<ModFolder> All(IModRegistry registry) => Collect(registry.GetAll());

	/// <summary>Content Patcher 的内容包（会往 Data/Characters 加 NPC 的就是它们）。</summary>
	public static IReadOnlyList<ModFolder> ContentPatcherPacks(IModRegistry registry) =>
		Collect(registry.GetAll().Where(m => string.Equals(m.Manifest.ContentPackFor?.UniqueID, ContentPatcherId, StringComparison.OrdinalIgnoreCase)));

	private static IReadOnlyList<ModFolder> Collect(IEnumerable<IModInfo> mods)
	{
		List<ModFolder> list = new List<ModFolder>();
		int seen = 0;
		foreach (IModInfo mod in mods)
		{
			seen++;
			string id = mod.Manifest.UniqueID?.Trim() ?? "";
			if (id.Length > 0 && mod.GetType().GetProperty("DirectoryPath")?.GetValue(mod) is string dir && dir.Length > 0)
			{
				list.Add(new ModFolder(id, Path.GetFullPath(dir)));
			}
		}
		if (seen > 0 && list.Count == 0)
		{
			Warn?.Invoke("读不到任何 mod 的文件夹位置，mod 村民的来源和 AI 排除名单的整 mod 规则暂时无效。");
		}
		return list;
	}
}
