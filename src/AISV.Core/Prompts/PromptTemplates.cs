using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AISV.Core.Diagnostics;
using AISV.Core.Storage;

namespace AISV.Core.Prompts;

/// <summary>
/// 提示词模板：assets/prompts/&lt;名字&gt;.txt，用到时才读、读过记住（启动时不读）。
/// 模板里用 {{token}} 占位；填的时候没给的占位会留空并记一条调试日志，方便发现模板和代码对不上。
/// </summary>
public sealed class PromptTemplates
{
	private static readonly Regex Token = new Regex(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

	private readonly IStorageBackend _files;

	private readonly ILog _log;

	private readonly Dictionary<string, string?> _cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

	public PromptTemplates(IStorageBackend files, ILog log)
	{
		_files = files;
		_log = log;
	}

	/// <summary>模板原文；文件不存在时返回 null。</summary>
	public string? Raw(string name)
	{
		lock (_cache)
		{
			if (!_cache.TryGetValue(name, out string? text))
			{
				text = _files.Read(name + ".txt")?.Replace("\r\n", "\n");
				_cache[name] = text;
				if (text == null)
				{
					_log.Warn($"缺少提示词模板 {name}.txt。");
				}
			}
			return text;
		}
	}

	public string Fill(string name, IReadOnlyDictionary<string, string?> values) => Fill(name, Raw(name) ?? "", values, _log);

	internal static string Fill(string name, string template, IReadOnlyDictionary<string, string?> values, ILog log)
	{
		return Token.Replace(template, m =>
		{
			if (values.TryGetValue(m.Groups[1].Value, out string? v))
			{
				return v ?? "";
			}
			log.Debug($"提示词模板 {name} 的 {{{{{m.Groups[1].Value}}}}} 没有填。");
			return "";
		});
	}

	/// <summary>改了模板文件后重新读（调试命令用）。</summary>
	public void Reload()
	{
		lock (_cache)
		{
			_cache.Clear();
		}
	}
}
