using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Config;
using AISV.Core.Text;

namespace AISV.Core.Compat;

public enum NpcCompatKind
{
	Active,
	TurnedOff,
	AuthorOptOut,
	ByScope,
	NotHandled,
	Left
}

/// <summary>把"为什么不让 AI 驱动"翻成玩家看得懂的话；开关一位村民。</summary>
public static class NpcCompat
{
	public const string ExcludedPrefix = "ai-mod-excluded:";

	public static NpcCompatKind Kind(string? reason, bool enabled)
	{
		if (reason == null)
		{
			return enabled ? NpcCompatKind.Active : NpcCompatKind.Left;
		}
		if (reason.StartsWith(ExcludedPrefix, StringComparison.Ordinal))
		{
			return NpcCompatKind.AuthorOptOut;
		}
		return reason switch
		{
			"disabled-by-player" or "disabled-by-config" => NpcCompatKind.TurnedOff,
			"vanilla-only" or "other-npc-mods-disabled" or "unknown-origin-disabled" or "npc-mod-not-selected" => NpcCompatKind.ByScope,
			_ => NpcCompatKind.NotHandled
		};
	}

	public static string KindWord(NpcCompatKind kind) => kind switch
	{
		NpcCompatKind.Active => Tr.Get("compat.kind.active", "AI"),
		NpcCompatKind.TurnedOff => Tr.Get("compat.kind.off", "你关了"),
		NpcCompatKind.AuthorOptOut => Tr.Get("compat.kind.author", "作者不愿意"),
		NpcCompatKind.ByScope => Tr.Get("compat.kind.scope", "范围外"),
		NpcCompatKind.Left => Tr.Get("compat.kind.left", "离开了"),
		_ => Tr.Get("compat.kind.none", "不处理")
	};

	public static string Explain(string? reason, bool enabled, string name)
	{
		NpcCompatKind kind = Kind(reason, enabled);
		switch (kind)
		{
		case NpcCompatKind.Active:
			return Tr.Get("compat.why.active", "{{name}}的对话和记忆都交给 AI。", ("name", name));
		case NpcCompatKind.Left:
			return Tr.Get("compat.why.left", "{{name}}已经离开小镇，回来之前不用 AI。", ("name", name));
		case NpcCompatKind.TurnedOff:
			return Tr.Get("compat.why.off", "你在这里关掉了{{name}}，说的是游戏原来的台词。", ("name", name));
		case NpcCompatKind.AuthorOptOut:
			return Tr.Get("compat.why.author", "{{name}}所在 mod 的作者不希望角色被 AI 驱动（{{mod}}）。", ("name", name), ("mod", reason![ExcludedPrefix.Length..]));
		}
		return reason switch
		{
			"vanilla-only" => Tr.Get("compat.why.vanillaOnly", "现在只用游戏自带的村民，{{name}}来自别的 mod。", ("name", name)),
			"other-npc-mods-disabled" => Tr.Get("compat.why.sveOnly", "现在只用游戏自带和 SVE 的村民。", ("name", name)),
			"npc-mod-not-selected" => Tr.Get("compat.why.notPicked", "{{name}}所在的 mod 没有勾选。", ("name", name)),
			"unknown-origin-disabled" => Tr.Get("compat.why.unknown", "查不到{{name}}来自哪个 mod，来路不明的村民没有打开。", ("name", name)),
			"custom-npc-not-present" => Tr.Get("compat.why.absent", "{{name}}现在不在这个世界里。", ("name", name)),
			_ => Tr.Get("compat.why.other", "{{name}}不是能聊天的村民。", ("name", name))
		};
	}

	/// <summary>打开或关掉一位村民：两份名单都要改，否则关不掉或开不了。</summary>
	public static void SetOff(ModConfig config, string name, bool off)
	{
		config.VillagersOff = Without(config.VillagersOff, name);
		config.OptOutExtras = Without(config.OptOutExtras, name);
		if (off)
		{
			config.VillagersOff = string.IsNullOrWhiteSpace(config.VillagersOff) ? name : config.VillagersOff + "," + name;
		}
	}

	public static void SetPicked(ModConfig config, string modId, bool picked)
	{
		List<string> list = (config.ModVillagerPicks ?? new List<string>()).Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)).ToList();
		if (picked)
		{
			list.Add(modId);
		}
		config.ModVillagerPicks = list;
	}

	private static string Without(string? csv, string name) => string.Join(",", (csv ?? "").Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
		.Select(s => s.Trim()).Where(s => s.Length > 0 && !string.Equals(s, name, StringComparison.OrdinalIgnoreCase)));
}
