using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Game;
using AISV.Game.Dialogue;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Mod;

/// <summary>
/// 自检：AI 能不能来回一次、存档数据、素材、Harmony 补丁、兼容 mod、配置、各功能的状态、联机对方的版本。
/// 结果写到 debug/selfcheck.txt，玩家报问题时把这个文件发过来就够了。
/// </summary>
internal sealed class SelfCheck
{
	public enum Level
	{
		Ok,
		Warn,
		Fail
	}

	public sealed record Section(string Title, Func<(Level Level, string Text)> Run);

	/// <summary>认识的兼容 mod：id → 名字。没装的不算问题，只列出来。</summary>
	private static readonly (string Id, string Name)[] KnownMods =
	{
		("FlashShifter.StardewValleyExpandedCP", "Stardew Valley Expanded（CP）"),
		("FlashShifter.SVECode", "Stardew Valley Expanded（代码）"),
		("sdvhead.LawAndOrderSV.cs", "Law and Order SV"),
		("bubuge.StardewThief", "Stardew Thief"),
		("bubuge.CommercializationOfStardewValley", "Commercialization of Stardew Valley"),
		("ThaliaFawnheart.TheStardewSquad", "The Stardew Squad"),
		("spacechase0.GenericModConfigMenu", "Generic Mod Config Menu"),
		("Pathoschild.ContentPatcher", "Content Patcher"),
		("CJBok.CheatsMenu", "CJB Cheats Menu")
	};

	/// <summary>少了就有功能不能用的补丁："类型.方法"。</summary>
	private static readonly string[] CorePatches = { "NPC.checkAction", "Child.checkAction", "Child.reloadSprite" };

	private static bool IsSecret(string name) => name.EndsWith("ApiKey", StringComparison.Ordinal) || name.EndsWith("Token", StringComparison.Ordinal) || name.Contains("Password", StringComparison.Ordinal) || name.Contains("Secret", StringComparison.Ordinal);

	private readonly IModHelper _helper;

	private readonly IManifest _manifest;

	private readonly Harmony _harmony;

	private readonly AiService _ai;

	private readonly Func<ModConfig> _config;

	private readonly ILog _log;

	private bool _running;

	public SelfCheck(IModHelper helper, IManifest manifest, Harmony harmony, AiService ai, Func<ModConfig> config, ILog log)
	{
		_helper = helper;
		_manifest = manifest;
		_harmony = harmony;
		_ai = ai;
		_config = config;
		_log = log;
	}

	/// <summary>各功能的状态，由 ModEntry 按进没进存档给出。</summary>
	public Func<IEnumerable<Section>> Features { get; set; } = Enumerable.Empty<Section>;

	public string FilePath => Path.Combine(_helper.DirectoryPath, "debug", "selfcheck.txt");

	/// <summary>跑一遍。AI 那一项要等网络，回来以后在主线程调 done（正常几项、要看的几项）。</summary>
	public void Run(Action<int, int>? done = null)
	{
		if (_running)
		{
			return;
		}
		_running = true;
		ModConfig config = _config();
		Stopwatch clock = Stopwatch.StartNew();
		Task.Run(async () =>
		{
			(Level, string) ai;
			if (!config.AiOn)
			{
				ai = (Level.Warn, "AI 功能关着，没测。");
			}
			else
			{
				try
				{
					AiResult r = await _ai.SendAsync(new ChatRequest("connection_test", "这是一次连通测试。", "只回一个字：好", 64, 0.1)).ConfigureAwait(false);
					ai = r.Ok
						? (Level.Ok, $"通了，{clock.ElapsedMilliseconds} 毫秒，模型回了：{(r.Text ?? "").Trim()}")
						: (Level.Fail, $"没通（{r.Failure}）：{r.Message}");
				}
				catch (Exception ex)
				{
					ai = (Level.Fail, "出错：" + ex.Message);
				}
			}
			MainThread.Run(() =>
			{
				_running = false;
				(int ok, int bad) = Write(ai);
				done?.Invoke(ok, bad);
			});
		});
	}

	private (int Ok, int Bad) Write((Level, string) ai)
	{
		List<Section> sections = new List<Section>
		{
			new("版本", Versions),
			new("AI 来回一次", () => ai),
			new("配置", Config),
			new("素材文件", Assets),
			new("多语言文件", Translations),
			new("Harmony 补丁", HarmonyPatches),
			new("兼容 mod", Mods)
		};
		sections.AddRange(Features());
		StringBuilder text = new StringBuilder();
		text.AppendLine($"AI星露谷 自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		int ok = 0, bad = 0;
		foreach (Section s in sections)
		{
			(Level level, string body) result;
			try
			{
				result = s.Run();
			}
			catch (Exception ex)
			{
				result = (Level.Fail, "检查时出错：" + ex.GetType().Name + "：" + ex.Message);
			}
			if (result.level == Level.Ok)
			{
				ok++;
			}
			else
			{
				bad++;
			}
			text.AppendLine();
			text.AppendLine($"== {s.Title}：{Word(result.level)} ==");
			text.AppendLine(result.body.TrimEnd());
		}
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, text.ToString(), new UTF8Encoding(false));
		_log.Info($"自检完了：{ok} 项正常，{bad} 项要看看，写到了 debug/selfcheck.txt");
		return (ok, bad);
	}

	private static string Word(Level level) => level switch
	{
		Level.Ok => "正常",
		Level.Warn => "注意",
		_ => "出错"
	};

	private (Level, string) Versions()
	{
		string lang = LocalizedContentManager.CurrentLanguageCode.ToString();
		return (Level.Ok, $"AI星露谷 {_manifest.Version}，SMAPI {Constants.ApiVersion}，星露谷 {Game1.version}，{Constants.TargetPlatform}，游戏语言 {lang}"
			+ (Context.IsWorldReady ? $"\n存档：{SaveData.FolderName()}，{(Context.IsMainPlayer ? "房主" : "农场帮手")}，{(Context.IsMultiplayer ? "联机" : "单人")}，第 {Game1.year} 年 {Game1.currentSeason} {Game1.dayOfMonth} 日" : "\n还没进存档（存档数据和各功能的状态进存档后再查一次）。"));
	}

	/// <summary>所有配置项；密钥、口令只说填没填。</summary>
	private (Level, string) Config()
	{
		ModConfig c = _config();
		List<string> lines = new List<string>();
		foreach (PropertyInfo p in typeof(ModConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
		{
			object? value = p.GetValue(c);
			string shown = IsSecret(p.Name) && value is string s
				? (string.IsNullOrWhiteSpace(s) ? "（没填）" : "（已填）")
				: value is string url && p.Name.EndsWith("Url", StringComparison.Ordinal) && url.Contains('?') ? url[..url.IndexOf('?')] + "?…"
				: value is string or null ? $"{value}" : value is System.Collections.IEnumerable e ? "[" + string.Join(", ", e.Cast<object>()) + "]" : value.ToString() ?? "";
			lines.Add($"{p.Name} = {shown}");
		}
		bool gateway = !c.AiOn || _ai.UsesServer || !string.IsNullOrWhiteSpace(c.CustomLlmBaseUrl);
		return (gateway ? Level.Ok : Level.Warn, (gateway ? "" : "用自己的 AI 网关但没填地址。\n") + string.Join("\n", lines));
	}

	/// <summary>assets 下每个 json 能读、每张 png 是真图片。</summary>
	private (Level, string) Assets()
	{
		string root = Path.Combine(_helper.DirectoryPath, "assets");
		if (!Directory.Exists(root))
		{
			return (Level.Fail, "没有 assets 文件夹，mod 没装完整。");
		}
		List<string> broken = new List<string>();
		int json = 0, png = 0;
		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
			if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				json++;
				if (!ParsesAsJson(file, out string? error))
				{
					broken.Add($"{rel}：{error}");
				}
			}
			else if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				png++;
				if (!IsPng(file))
				{
					broken.Add(rel + "：不是 png 图片");
				}
			}
		}
		List<string> dirs = Directory.GetDirectories(root).Select(Path.GetFileName).Where(d => d != null).Select(d => d!).OrderBy(d => d).ToList();
		string summary = $"{dirs.Count} 个文件夹（{string.Join("、", dirs)}），json {json} 个，png {png} 张。";
		return broken.Count == 0 ? (Level.Ok, summary) : (Level.Fail, summary + "\n读不了的：\n" + string.Join("\n", broken.Take(40)));
	}

	/// <summary>每种语言的键和中文一样多；还没翻译（和中文一字不差）的数一下。</summary>
	private (Level, string) Translations()
	{
		string dir = Path.Combine(_helper.DirectoryPath, "i18n");
		Dictionary<string, string>? zh = ReadStrings(Path.Combine(dir, "zh.json"));
		if (zh == null)
		{
			return (Level.Fail, "zh.json 读不了。");
		}
		List<string> lines = new List<string>();
		Level level = Level.Ok;
		foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			if (name == "zh")
			{
				continue;
			}
			if (ReadStrings(file) is not { } other)
			{
				lines.Add(name + "：读不了");
				level = Level.Fail;
				continue;
			}
			int missing = zh.Keys.Count(k => !other.ContainsKey(k));
			int same = zh.Count(kv => other.TryGetValue(kv.Key, out string? v) && v == kv.Value && kv.Value.Any(ch => ch >= '一' && ch <= '鿿'));
			int limit = name == "ja" ? 80 : 0;
			if (missing > 0 || same > limit)
			{
				level = level == Level.Fail ? level : Level.Warn;
			}
			lines.Add($"{name}：{other.Count} 条，缺 {missing}，还是中文 {same}");
		}
		return (level, $"zh：{zh.Count} 条\n" + string.Join("\n", lines));
	}

	/// <summary>我们打上的补丁，和同一处还有哪些 mod 也打了（排查冲突用）。</summary>
	private (Level, string) HarmonyPatches()
	{
		List<MethodBase> methods = _harmony.GetPatchedMethods().ToList();
		List<string> lines = new List<string>();
		HashSet<string> names = new HashSet<string>();
		foreach (MethodBase m in methods.OrderBy(m => m.DeclaringType?.Name).ThenBy(m => m.Name))
		{
			string name = $"{m.DeclaringType?.Name}.{m.Name}";
			names.Add(name);
			Patches? info = Harmony.GetPatchInfo(m);
			List<string> others = info?.Owners.Where(o => o != _harmony.Id).ToList() ?? new List<string>();
			lines.Add(name + (others.Count > 0 ? "（也被 " + string.Join("、", others) + " 改了）" : ""));
		}
		List<string> missing = CorePatches.Where(p => !names.Contains(p)).ToList();
		string head = $"打上了 {methods.Count} 处。" + (missing.Count > 0 ? "\n少了：" + string.Join("、", missing) : "");
		return (missing.Count > 0 ? Level.Fail : Level.Ok, head + "\n" + string.Join("\n", lines));
	}

	private (Level, string) Mods()
	{
		List<string> lines = new List<string>();
		foreach ((string id, string name) in KnownMods)
		{
			IModInfo? mod = _helper.ModRegistry.Get(id);
			lines.Add($"{name}：{(mod == null ? "没装" : mod.Manifest.Version.ToString())}");
		}
		List<IModInfo> all = _helper.ModRegistry.GetAll().OrderBy(m => m.Manifest.Name).ToList();
		lines.Add($"\n一共装了 {all.Count} 个 mod：");
		lines.AddRange(all.Select(m => $"  {m.Manifest.Name} {m.Manifest.Version}（{m.Manifest.UniqueID}）"));
		return (Level.Ok, string.Join("\n", lines));
	}

	/// <summary>当前存档的数据文件都能读。</summary>
	public (Level, string) SaveFiles()
	{
		string dir = Path.Combine(_helper.DirectoryPath, "Data", SaveData.FolderName());
		if (!Directory.Exists(dir))
		{
			return (Level.Ok, "这个存档还没存过 mod 数据（第一次保存后才有）。");
		}
		List<string> broken = new List<string>();
		int count = 0;
		long bytes = 0;
		foreach (string file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
		{
			count++;
			bytes += new FileInfo(file).Length;
			if (!ParsesAsJson(file, out string? error))
			{
				broken.Add($"{Path.GetRelativePath(dir, file)}：{error}");
			}
		}
		string summary = $"Data/{SaveData.FolderName()}：{count} 个文件，{bytes / 1024} KB。";
		return broken.Count == 0 ? (Level.Ok, summary) : (Level.Fail, summary + "\n读不了的：\n" + string.Join("\n", broken));
	}

	/// <summary>联机时每位对方装的 AI星露谷 版本。</summary>
	public (Level, string) Peers(string sync)
	{
		if (!Context.IsMultiplayer)
		{
			return (Level.Ok, "单人游戏。");
		}
		List<string> lines = new List<string>();
		Level level = Level.Ok;
		foreach (IMultiplayerPeer peer in _helper.Multiplayer.GetConnectedPlayers())
		{
			string who = Game1.GetPlayer(peer.PlayerID)?.Name ?? peer.PlayerID.ToString();
			IMultiplayerPeerMod? mod = peer.GetMod(_manifest.UniqueID);
			string version = !peer.HasSmapi ? "没装 SMAPI" : mod == null ? "没装 AI星露谷" : mod.Version.ToString();
			if (mod == null || !mod.Version.Equals(_manifest.Version))
			{
				level = Level.Warn;
			}
			lines.Add($"{who}{(peer.IsHost ? "（房主）" : "")}：{version}");
		}
		return (level, (lines.Count == 0 ? "现在没有别的玩家连着。" : string.Join("\n", lines)) + "\n\n" + sync);
	}

	private static bool ParsesAsJson(string file, out string? error)
	{
		try
		{
			using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool IsPng(string file)
	{
		byte[] head = new byte[8];
		using FileStream s = File.OpenRead(file);
		return s.Read(head, 0, 8) == 8 && head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G';
	}

	private static Dictionary<string, string>? ReadStrings(string file)
	{
		try
		{
			string text = File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿');
			return JsonSerializer.Deserialize<Dictionary<string, string>>(text, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		}
		catch
		{
			return null;
		}
	}
}
