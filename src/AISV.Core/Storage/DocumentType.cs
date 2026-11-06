using System;
using System.Text.Json.Nodes;

namespace AISV.Core.Storage;

/// <summary>
/// 一种存档文档：当前格式版本号、空文档怎么建、低版本文件怎么升上来。
/// 改了数据结构就把 <see cref="Version"/> 加一，并在 <see cref="Upgrade"/> 里把上一版的 JSON 改成新版。
/// </summary>
public sealed class DocumentType<T> where T : class
{
	public DocumentType(string name, int version, Func<T> createDefault, Func<JsonNode, int, JsonNode>? upgrade = null)
	{
		if (version < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(version), "版本号从 1 开始。");
		}
		Name = name;
		Version = version;
		CreateDefault = createDefault;
		Upgrade = upgrade;
	}

	/// <summary>日志里用的名字。</summary>
	public string Name { get; }

	public int Version { get; }

	public Func<T> CreateDefault { get; }

	/// <summary>(文件里的数据, 文件的版本号) → 升到 <see cref="Version"/> 的数据。为 null 时直接按当前结构读。</summary>
	public Func<JsonNode, int, JsonNode>? Upgrade { get; }
}
