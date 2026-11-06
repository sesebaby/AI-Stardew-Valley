using AISV.Core.Ai;
using StardewModdingAPI;

namespace AISV.Game;

/// <summary>账号存在 SMAPI 的全局数据里（%appdata%\StardewValley\.smapi\mod-data\…），不进存档、不进 mod 文件夹。</summary>
public sealed class GlobalAccountStore : IAccountStore
{
	private const string Key = "aisv-account";

	private readonly IDataHelper _data;

	public GlobalAccountStore(IDataHelper data)
	{
		_data = data;
	}

	public AccountState? Load() => _data.ReadGlobalData<AccountState>(Key);

	public void Save(AccountState state) => _data.WriteGlobalData(Key, state);
}
