using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AISV.Core.Text;
using Xunit;

namespace AISV.Core.Tests;

public class I18nTests
{
	private static string RepoRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AISV.sln")))
		{
			dir = dir.Parent;
		}
		return dir!.FullName;
	}

	[Fact]
	public void EveryKeyInCodeIsInChineseFileWithSameText()
	{
		string root = RepoRoot();
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/AISV.Mod/i18n/zh.json")));
		Regex call = new Regex("Tr\\.Get\\(\"([^\"]+)\",\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
		foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/")))
		{
			foreach (Match m in call.Matches(File.ReadAllText(file)))
			{
				Assert.True(doc.RootElement.TryGetProperty(m.Groups[1].Value, out JsonElement v), $"zh.json 缺少 {m.Groups[1].Value}（{Path.GetFileName(file)}）");
				Assert.Equal(Regex.Unescape(m.Groups[2].Value), v.GetString());
			}
		}
	}

	[Fact]
	public void OtherLanguagesCoverEveryKeyAndKeepPlaceholders()
	{
		string folder = Path.Combine(RepoRoot(), "src/AISV.Mod/i18n");
		Regex token = new Regex("\\{\\{\\w+\\}\\}");
		using JsonDocument zh = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "zh.json")));
		foreach (string file in Directory.EnumerateFiles(folder, "*.json").Where(f => Path.GetFileName(f) != "zh.json"))
		{
			using JsonDocument other = JsonDocument.Parse(File.ReadAllText(file));
			foreach (JsonProperty source in zh.RootElement.EnumerateObject())
			{
				Assert.True(other.RootElement.TryGetProperty(source.Name, out JsonElement v), $"{Path.GetFileName(file)} 缺少 {source.Name}");
				string[] want = token.Matches(source.Value.GetString()!).Select(m => m.Value).OrderBy(x => x, System.StringComparer.Ordinal).ToArray();
				string[] got = token.Matches(v.GetString()!).Select(m => m.Value).OrderBy(x => x, System.StringComparer.Ordinal).ToArray();
				Assert.True(want.SequenceEqual(got), $"{Path.GetFileName(file)} 的 {source.Name} 占位符不对");
			}
		}
	}

	[Fact]
	public void TrUsesLookupAndReplacesTokens()
	{
		try
		{
			Tr.Use(k => k == "k" ? "你好 {{name}}" : null);
			Assert.Equal("你好 阿哲", Tr.Get("k", "x", ("name", "阿哲")));
			Assert.Equal("默认 1", Tr.Get("missing", "默认 {{n}}", ("n", 1)));
		}
		finally
		{
			Tr.Use(_ => null);
		}
	}
}
