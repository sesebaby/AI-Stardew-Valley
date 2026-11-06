using System.IO;
using System.Linq;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;
using StardewModdingAPI;
using StardewValley;

namespace AISV.Game;

/// <summary>
/// 当前存档的数据文档：进存档时打开（用到才读），游戏保存时写盘，不保存就回标题时丢弃。
/// 位置：mod 文件夹/Data/&lt;玩家名_存档ID&gt;/…。联机的农场工只写自己的数据，全村的数据由房主写。
/// </summary>
public sealed class SaveData
{
	private readonly string _dataRoot;

	private readonly ILog _log;

	public SaveData(string modDirectory, ILog log)
	{
		_dataRoot = Path.Combine(modDirectory, "Data");
		_log = log;
	}

	/// <summary>没进存档时为 null。</summary>
	public DocumentStore? Current { get; private set; }

	public void Open()
	{
		Current = new DocumentStore(new DirectoryBackend(Path.Combine(_dataRoot, FolderName())), _log);
	}

	/// <summary>存档文件夹名：玩家名（去掉文件名里不能用的字符）+ "_" + 存档 ID。</summary>
	public static string FolderName()
	{
		string name = Game1.player?.Name ?? "Unknown";
		char[] invalid = Path.GetInvalidFileNameChars();
		return string.Concat(name.Where(c => !invalid.Contains(c))) + "_" + Game1.uniqueIDForThisGame;
	}

	public int Commit()
	{
		if (Current == null)
		{
			return 0;
		}
		return Current.Commit(skip: Context.IsMainPlayer ? null : Social.SocialSystem.VillageDocuments);
	}

	public void Close()
	{
		Current?.Discard();
		Current = null;
	}
}
