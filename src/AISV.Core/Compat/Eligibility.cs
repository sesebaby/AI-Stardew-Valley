using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Persona;

namespace AISV.Core.Compat;

/// <summary>其他 NPC mod 的村民怎么处理：只要游戏自带的 / 游戏自带的和 SVE / 选定的 mod / 全部。</summary>
public enum NpcModActivationMode
{
	VanillaOnly,
	VanillaAndSve,
	SelectedMods,
	AllNpcMods
}

public static class SveMods
{
	public static readonly IReadOnlyList<string> Ids = new[] { "FlashShifter.StardewValleyExpandedCP", "FlashShifter.SVECode" };

	public static bool IsSve(string modId) => Ids.Contains(modId, StringComparer.OrdinalIgnoreCase);
}

/// <summary>判断一位 NPC 能不能由 AI 驱动要知道的事实。</summary>
public sealed record NpcFacts(
	string Name,
	bool IsVillager,
	bool IsBuiltIn,
	bool IsSve,
	bool IsManaged,
	bool PresentInWorld,
	IReadOnlyCollection<string> SourceModIds,
	string? ExcludedBy);

/// <summary>
/// 规则（顺序有意义）：不是村民 → 否；玩家关掉的 → 否；游戏自带的村民 → 是；不在世界里的 mod 村民 → 否；
/// 内容包托管的原创角色 → 是；按"其他 NPC mod"的模式筛；最后看 AI 排除名单。
/// 返回拒绝理由（日志和兼容页显示），可以用返回 null。
/// </summary>
public static class EligibilityPolicy
{
	public static string? RejectionReason(NpcFacts f, ModConfig config)
	{
		if (!f.IsVillager)
		{
			return "not-villager";
		}
		if (string.IsNullOrWhiteSpace(f.Name))
		{
			return "missing-name";
		}
		string name = f.Name.Trim();
		if (ListContains(config.OptOutExtras, name) || ListContains(config.VillagersOff, name))
		{
			return "disabled-by-player";
		}
		if (f.IsBuiltIn)
		{
			return null;
		}
		if (!f.PresentInWorld)
		{
			return "custom-npc-not-present";
		}
		if (f.IsManaged)
		{
			return null;
		}
		string[] sources = f.SourceModIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToArray();
		bool unknownOrigin = sources.Length == 0;
		HashSet<string> selected = (config.ModVillagerPicks ?? new List<string>()).Concat(config.ModVillagerExtras ?? new List<string>())
			.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		string? byMode = config.ModVillagerScope switch
		{
			NpcModActivationMode.VanillaOnly => "vanilla-only",
			NpcModActivationMode.VanillaAndSve => f.IsSve ? null : "other-npc-mods-disabled",
			NpcModActivationMode.SelectedMods when sources.Any(selected.Contains) => null,
			NpcModActivationMode.SelectedMods when f.IsSve && selected.Any(SveMods.IsSve) => null,
			NpcModActivationMode.SelectedMods when unknownOrigin && config.UnknownVillagersOn => null,
			NpcModActivationMode.SelectedMods => unknownOrigin ? "unknown-origin-disabled" : "npc-mod-not-selected",
			_ => null
		};
		if (byMode != null)
		{
			return byMode;
		}
		return string.IsNullOrWhiteSpace(f.ExcludedBy) ? null : "ai-mod-excluded:" + f.ExcludedBy.Trim();
	}

	internal static bool ListContains(string? csv, string name) => !string.IsNullOrWhiteSpace(csv)
		&& csv.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Any(s => string.Equals(s.Trim(), name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 哪些 NPC 由 AI 驱动。游戏层喂进来：已加载的 mod、后台扫出的 NPC 归属、世界里有哪些村民、被逐出小镇的名单；
/// 查询时只做内存里的判断，不碰磁盘。
/// </summary>
public sealed class NpcEligibility
{
	private readonly Func<ModConfig> _config;

	private readonly OptOutList _optOut;

	private readonly ILog _log;

	private readonly ConcurrentDictionary<string, byte> _loggedToday = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

	private string[] _loadedModIds = Array.Empty<string>();

	private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private IReadOnlyDictionary<string, string> _families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private World _world = World.Empty;

	private Func<string, bool> _isExpelled = _ => false;

	public NpcEligibility(Func<ModConfig> config, OptOutList optOut, ILog log)
	{
		_config = config;
		_optOut = optOut;
		_log = log;
	}

	/// <summary>世界里的村民快照：哪些在场、哪些是 SVE 的、哪些是内容包托管的原创角色。</summary>
	public sealed record World(IReadOnlySet<string> PresentVillagers, IReadOnlySet<string> SveNpcs, IReadOnlySet<string> ManagedNpcs)
	{
		public static readonly World Empty = new World(new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
	}

	public void SetModFamilies(IReadOnlyList<ModFolder> folders) => Volatile.Write(ref _families, ContentOrigin.Families(folders));

	public IReadOnlyList<string> LoadedModIds => Volatile.Read(ref _loadedModIds);

	public bool SveInstalled => LoadedModIds.Any(SveMods.IsSve);

	/// <summary>装了 SVE，而且它的每个部分都没被 AI 排除名单拦下，才把 SVE 的世界观告诉 AI。</summary>
	public bool SveContentEnabled => SveInstalled && LoadedModIds.Where(SveMods.IsSve).All(IsModAllowed);

	public void SetLoadedMods(IEnumerable<string> modIds)
	{
		string[] ids = modIds.Select(id => id?.Trim() ?? "").Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(id => id.Length).ToArray();
		Volatile.Write(ref _loadedModIds, ids);
		_optOut.SetLoadedMods(ids);
	}

	public void SetOwners(IReadOnlyDictionary<string, string> owners) => Volatile.Write(ref _owners, owners);

	public void SetWorld(World world) => Volatile.Write(ref _world, world);

	public void SetExpelledCheck(Func<string, bool> isExpelled) => _isExpelled = isExpelled;

	public void StartNewDay() => _loggedToday.Clear();

	/// <summary>这位村民现在由 AI 驱动吗？（世界里的 NPC 用 isVillager 和是否托管的实时值。）</summary>
	public bool IsEnabled(string npcName, bool? isVillager = null, bool? isManaged = null)
	{
		if (string.IsNullOrWhiteSpace(npcName))
		{
			return false;
		}
		if (_isExpelled(npcName))
		{
			LogOnce(npcName, "expelled-from-village");
			return false;
		}
		string? reason = RejectionReason(npcName, isVillager, isManaged, present: null);
		if (reason == null && EligibilityPolicy.ListContains(_config().VillagersOff, npcName))
		{
			reason = "disabled-by-config";
		}
		if (reason != null)
		{
			LogOnce(npcName, reason);
			return false;
		}
		return true;
	}

	public string? RejectionReason(string npcName, bool? isVillager, bool? isManaged, bool? present)
	{
		World world = Volatile.Read(ref _world);
		bool builtIn = BuiltInPersonas.HasVanilla(npcName);
		bool sve = world.SveNpcs.Contains(npcName) || (SveInstalled && BuiltInPersonas.IsKnownSve(npcName)) || (SveInstalled && DirectSources(npcName).Any(SveMods.IsSve));
		IReadOnlyCollection<string> sources = Sources(npcName, sve);
		bool managed = isManaged ?? world.ManagedNpcs.Contains(npcName);
		NpcFacts facts = new NpcFacts(npcName, isVillager ?? true, builtIn, sve, managed, present ?? (builtIn || isVillager.HasValue || world.PresentVillagers.Contains(npcName)), sources, _optOut.MatchNpc(npcName, sources));
		return EligibilityPolicy.RejectionReason(facts, _config());
	}

	/// <summary>这个 mod 的内容（物品、作物）能不能给 AI 功能用：排除名单里的 mod 不行。名单不可用时放行。</summary>
	public bool IsModAllowed(string modId)
	{
		if (string.IsNullOrWhiteSpace(modId))
		{
			return false;
		}
		string? excluded = _optOut.MatchModId(modId);
		if (excluded == null)
		{
			return true;
		}
		LogOnce("mod:" + modId, "ai-mod-excluded:" + excluded);
		return false;
	}

	/// <summary>
	/// 这个 mod 加的物品和作物能不能给市场、委托、礼物这些功能用：跟着"其他 NPC mod"的模式走（关掉一个 mod 的 NPC，它的东西也不用）。
	/// 游戏自带的都能用；同一个下载文件夹里的包算一家，一家里有被排除的就都不用；不带 NPC 的只有"只用游戏自带村民"时才不用。
	/// </summary>
	public bool IsContentModEnabled(string? modId)
	{
		if (string.IsNullOrWhiteSpace(modId))
		{
			return true;
		}
		string id = modId.Trim();
		ModConfig config = _config();
		if (config.ModVillagerScope == NpcModActivationMode.VanillaOnly)
		{
			return false;
		}
		HashSet<string> family = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
		IReadOnlyDictionary<string, string> families = Volatile.Read(ref _families);
		if (families.TryGetValue(id, out string? key))
		{
			family.UnionWith(families.Where(p => string.Equals(p.Value, key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key));
		}
		if (!family.All(IsModAllowed))
		{
			return false;
		}
		if (config.ModVillagerScope == NpcModActivationMode.AllNpcMods)
		{
			return true;
		}
		List<string> npcMods = NpcMods().Where(family.Contains).ToList();
		if (npcMods.Count == 0)
		{
			return true;
		}
		if (config.ModVillagerScope == NpcModActivationMode.VanillaAndSve)
		{
			return npcMods.Any(SveMods.IsSve);
		}
		HashSet<string> selected = (config.ModVillagerPicks ?? new List<string>()).Concat(config.ModVillagerExtras ?? new List<string>())
			.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return npcMods.Any(selected.Contains) || (npcMods.Any(SveMods.IsSve) && selected.Any(SveMods.IsSve));
	}

	/// <summary>带 NPC 的 mod：扫描结果、世界里的村民名字前缀，加上 SVE（不等扫描）。</summary>
	private IEnumerable<string> NpcMods()
	{
		IReadOnlyList<string> loaded = LoadedModIds;
		return Volatile.Read(ref _owners).Values
			.Concat(Volatile.Read(ref _world).PresentVillagers.Select(n => NamespacedOwner(n, loaded)).Where(o => o != null)!)
			.Concat(loaded.Where(SveMods.IsSve))
			.Distinct(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>加这位 NPC 的 mod（名字前缀或扫描结果）；游戏自带或查不到返回 null。</summary>
	public string? OwnerModId(string npcName) => string.IsNullOrWhiteSpace(npcName) ? null : DirectSources(npcName.Trim()).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

	private IReadOnlyCollection<string> DirectSources(string npcName)
	{
		HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (NamespacedOwner(npcName, LoadedModIds) is string prefix)
		{
			set.Add(prefix);
		}
		if (Volatile.Read(ref _owners).TryGetValue(npcName, out string? owner))
		{
			set.Add(owner);
		}
		return set;
	}

	private IReadOnlyCollection<string> Sources(string npcName, bool sve)
	{
		HashSet<string> set = new HashSet<string>(DirectSources(npcName), StringComparer.OrdinalIgnoreCase);
		if (sve)
		{
			set.UnionWith(LoadedModIds.Where(SveMods.IsSve));
		}
		return set;
	}

	/// <summary>"模组ID_名字" 写法的 NPC：取最长的、后面紧跟下划线的已加载 mod ID。</summary>
	internal static string? NamespacedOwner(string npcName, IEnumerable<string> loadedModIds)
	{
		string name = npcName.Trim();
		if (name.Length < 2 || name.IndexOf('_', 1) < 0)
		{
			return null;
		}
		string? best = null;
		foreach (string id in loadedModIds)
		{
			if (id.Length > (best?.Length ?? 0) && name.Length > id.Length && name[id.Length] == '_' && name.StartsWith(id, StringComparison.OrdinalIgnoreCase))
			{
				best = id;
			}
		}
		return best;
	}

	private void LogOnce(string name, string reason)
	{
		if (_loggedToday.TryAdd(name + ":" + reason, 0))
		{
			_log.Debug($"不让 AI 驱动 {name}（{reason}）。");
		}
	}
}
