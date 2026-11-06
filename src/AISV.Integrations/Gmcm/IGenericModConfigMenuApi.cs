using System;
using StardewModdingAPI;

namespace AISV.Integrations.Gmcm;

/// <summary>Generic Mod Config Menu 的 API（只列用到的方法，签名照 GMCM 公开的接口）。</summary>
public interface IGenericModConfigMenuApi
{
	void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

	void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

	void AddParagraph(IManifest mod, Func<string> text);

	void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);

	void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);

	void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
}
