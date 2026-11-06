using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AISV.Core.Diagnostics;

namespace AISV.Core.Ai;

/// <summary>
/// AI星露谷服务器的地址：mod 文件夹里的 assets/connection.dat（打包脚本写入，混淆存放，里面没有任何 key）。
/// 界面和日志里不出现服务器地址。
/// </summary>
public sealed class ServerConnection
{
	public const string RelativePath = "assets/connection.dat";

	private const string PadSeed = "AIStardewValley.Core/connection";

	private readonly string _host;

	private ServerConnection(string root, string host, string label)
	{
		Root = root;
		_host = host;
		Label = label;
	}

	/// <summary>没有 connection.dat 的版本：只能用自己的网关。</summary>
	public static readonly ServerConnection None = new ServerConnection("", "", "");

	public bool IsAvailable => Root.Length > 0;

	/// <summary>服务器根地址（不带 /v1）。</summary>
	public string Root { get; }

	/// <summary>打包时写的包名（可选），只在设置页显示。</summary>
	public string Label { get; }

	public string ChatBaseUrl => Root + "/v1";

	public static ServerConnection Load(string modDirectory, ILog log)
	{
		string path = Path.Combine(modDirectory, RelativePath);
		if (!File.Exists(path))
		{
			return None;
		}
		try
		{
			ServerConnection? c = Parse(Decode(File.ReadAllText(path).Trim()));
			if (c == null)
			{
				log.Warn("服务器连接文件不完整，忽略。");
				return None;
			}
			log.Info("找到 AI星露谷服务器的连接。");
			return c;
		}
		catch (Exception ex) when (ex is FormatException or JsonException or IOException or KeyNotFoundException or InvalidOperationException)
		{
			log.Warn("服务器连接文件读不出来：" + ex.GetType().Name);
			return None;
		}
	}

	internal static ServerConnection? Parse(string json)
	{
		using JsonDocument doc = JsonDocument.Parse(json);
		string url = doc.RootElement.GetProperty("u").GetString()?.Trim().TrimEnd('/') ?? "";
		if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
		{
			url = url.Substring(0, url.Length - 3);
		}
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}
		string label = doc.RootElement.TryGetProperty("n", out JsonElement n) ? n.GetString() ?? "" : "";
		return new ServerConnection(url, uri.Authority, label);
	}

	/// <summary>把文字里的服务器地址换成"服务器"（网络异常的说明里常带 host:port）。</summary>
	public string Mask(string? text)
	{
		if (string.IsNullOrEmpty(text) || !IsAvailable)
		{
			return text ?? "";
		}
		string result = text.Replace(Root, "服务器", StringComparison.OrdinalIgnoreCase);
		if (_host.Length > 0)
		{
			result = result.Replace(_host, "服务器", StringComparison.OrdinalIgnoreCase);
			int colon = _host.LastIndexOf(':');
			if (colon > 0)
			{
				result = result.Replace(_host.Substring(0, colon), "服务器", StringComparison.OrdinalIgnoreCase);
			}
		}
		return result;
	}

	// 混淆：base64( UTF8(json) 的每个字节 异或 SHA256(PadSeed)[i % 32] 异或 (byte)(i * 31) )。异或两次还原，编码解码同一个变换。
	internal static string Decode(string text) => Encoding.UTF8.GetString(Xor(Convert.FromBase64String(text)));

	internal static string Encode(string json) => Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(json)));

	private static byte[] Xor(byte[] data)
	{
		byte[] pad = SHA256.HashData(Encoding.UTF8.GetBytes(PadSeed));
		for (int i = 0; i < data.Length; i++)
		{
			data[i] = (byte)(data[i] ^ pad[i % pad.Length] ^ (byte)(i * 31));
		}
		return data;
	}
}
