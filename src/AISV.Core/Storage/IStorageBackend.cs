using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AISV.Core.Storage;

/// <summary>存档数据的落盘位置。路径都是相对路径，用 / 分隔。</summary>
public interface IStorageBackend
{
	/// <summary>文件不存在时返回 null。</summary>
	string? Read(string path);

	/// <summary>整个文件替换（先写临时文件再换名，写到一半断电也不会坏）。</summary>
	void Write(string path, string content);

	void Delete(string path);

	/// <summary>目录下直接包含的文件（相对路径）。目录不存在时返回空。</summary>
	IReadOnlyList<string> List(string directory);
}

/// <summary>落在某个文件夹下。</summary>
public sealed class DirectoryBackend : IStorageBackend
{
	private readonly string _root;

	public DirectoryBackend(string root)
	{
		_root = Path.GetFullPath(root);
	}

	public string Root => _root;

	public string? Read(string path)
	{
		string full = Resolve(path);
		return File.Exists(full) ? File.ReadAllText(full) : null;
	}

	public void Write(string path, string content)
	{
		string full = Resolve(path);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		string temp = full + ".tmp";
		File.WriteAllText(temp, content);
		File.Move(temp, full, overwrite: true);
	}

	public void Delete(string path)
	{
		string full = Resolve(path);
		if (File.Exists(full))
		{
			File.Delete(full);
		}
	}

	public IReadOnlyList<string> List(string directory)
	{
		string full = Resolve(directory);
		if (!Directory.Exists(full))
		{
			return Array.Empty<string>();
		}
		return Directory.GetFiles(full).Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
	}

	private string Resolve(string path)
	{
		string full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
		string rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
		StringComparison cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (!string.Equals(full, _root, cmp) && !full.StartsWith(rootWithSep, cmp))
		{
			throw new ArgumentException("路径跑出了存档数据目录：" + path, nameof(path));
		}
		return full;
	}
}

/// <summary>放在内存里（测试用）。</summary>
public sealed class MemoryBackend : IStorageBackend
{
	private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, string> Files => _files;

	public int Writes { get; private set; }

	public string? Read(string path) => _files.TryGetValue(path, out string? v) ? v : null;

	public void Write(string path, string content)
	{
		_files[path] = content;
		Writes++;
	}

	public void Delete(string path) => _files.Remove(path);

	public IReadOnlyList<string> List(string directory)
	{
		string prefix = directory.TrimEnd('/') + "/";
		return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.IndexOf('/', prefix.Length) < 0).OrderBy(k => k, StringComparer.Ordinal).ToList();
	}
}
