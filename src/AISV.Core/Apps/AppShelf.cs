using System;
using System.Linq;
using AISV.Core.Config;

namespace AISV.Core.Apps;

/// <summary>应用页显示哪些应用。致谢页总是留着。</summary>
public static class AppShelf
{
	public const string AlwaysShown = "thanks";

	public static bool CanHide(string id) => !string.Equals(id, AlwaysShown, StringComparison.OrdinalIgnoreCase);

	public static bool IsHidden(ModConfig config, string id) => CanHide(id) && config.HiddenApps.Contains(id, StringComparer.OrdinalIgnoreCase);

	public static void SetHidden(ModConfig config, string id, bool hidden)
	{
		config.HiddenApps.RemoveAll(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase));
		if (hidden && CanHide(id))
		{
			config.HiddenApps.Add(id);
		}
	}
}
