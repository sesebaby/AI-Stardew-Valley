using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AISV.Core.Ai;

/// <summary>
/// 这台电脑的机器码：系统自带的机器 ID 加盐后取 SHA256 的前 32 位，原值不离开本机。
/// 服务器只拿它防刷号、拉黑。读不到就是空。
/// </summary>
public static class MachineId
{
	private const string Salt = "aisv-machine-v1:";

	private static string? _hash;

	public static string Hash()
	{
		if (_hash == null)
		{
			string raw;
			try
			{
				raw = Read().Trim();
			}
			catch (Exception)
			{
				raw = "";
			}
			_hash = Of(raw);
		}
		return _hash;
	}

	public static string Of(string raw)
	{
		if (raw.Length == 0)
		{
			return "";
		}
		byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + raw.ToLowerInvariant()));
		return Convert.ToHexString(digest)[..32].ToLowerInvariant();
	}

	private static string Read()
	{
		if (OperatingSystem.IsWindows())
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using RegistryKey? key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
			return key?.GetValue("MachineGuid") as string ?? "";
		}
		if (OperatingSystem.IsMacOS())
		{
			using Process? p = Process.Start(new ProcessStartInfo("ioreg", "-rd1 -c IOPlatformExpertDevice") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
			if (p == null)
			{
				return "";
			}
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit(3000);
			Match m = Regex.Match(output, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
			return m.Success ? m.Groups[1].Value : "";
		}
		foreach (string path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
		{
			if (File.Exists(path))
			{
				return File.ReadAllText(path);
			}
		}
		return "";
	}
}
