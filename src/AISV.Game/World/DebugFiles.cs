using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AISV.Game.World;

/// <summary>调试命令的结果写到 mod 目录的 debug/ 下。</summary>
public static class DebugFiles
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static Func<string, object, string> Writer(string modDirectory) => (file, value) =>
	{
		string dir = Path.Combine(modDirectory, "debug");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, file), JsonSerializer.Serialize(value, value.GetType(), Json), new UTF8Encoding(false));
		return "写到了 debug/" + file;
	};
}
