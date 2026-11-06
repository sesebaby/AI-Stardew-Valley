using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AISV.Core.Text;

public enum LanguageMode
{
	GameLanguage,
	Vietnamese,
	Custom
}

/// <summary>
/// 提示词里要求 AI 说的语言：默认跟游戏语言；也可以固定越南语，或自己填一个语言名（只允许字母、空格、括号、连字符，不能夹带指令词）。
/// </summary>
public static class PromptLanguage
{
	public const int MaxCustomLength = 64;

	/// <summary>词头，碰到就不当语言名：命令、角色和"回答"一类的词，各语言变形都拦住。</summary>
	private static readonly string[] CommandStems =
	{
		"ignor", "disregard", "forget", "overrid", "pretend", "instruct", "instruc", "assistant", "developer", "prompt", "system", "sistem",
		"respon", "repl", "answer", "output", "transl", "tradu", "write", "speak", "always", "never", "follow", "please",
		"desconsider", "esquec", "esqueç", "finj", "obedec", "obedeç", "regra", "retorn", "saída",
		"hướng", "phản", "viết"
	};

	/// <summary>太短、当词头会误伤的词，整词比较。</summary>
	private static readonly HashSet<string> CommandWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"act", "all", "use", "say", "must", "only", "rule", "rules", "diga", "fale", "usar", "bỏ", "chỉ", "dẫn", "hãy", "lời", "luôn", "nói", "qua", "tắc", "trả", "change", "return", "previous"
	};

	public static string Resolve(LanguageMode mode, string? custom, string? gameLanguageCode) => mode switch
	{
		LanguageMode.Vietnamese => "Vietnamese (Tiếng Việt)",
		LanguageMode.Custom when TryNormalizeCustom(custom, out string name) => name,
		_ => FromGame(gameLanguageCode)
	};

	/// <summary>游戏语言代码（如 zh-CN、pt-BR）→ 语言名；不认识的用英语。</summary>
	public static string FromGame(string? code) => (code ?? "").Trim().Replace('_', '-').ToLowerInvariant().Split('-', 2)[0] switch
	{
		"de" => "German (Deutsch)",
		"es" => "Spanish (Español)",
		"fr" => "French (Français)",
		"it" => "Italian (Italiano)",
		"pt" => "Brazilian Portuguese (Português Brasileiro)",
		"ja" => "Japanese (日本語)",
		"ko" => "Korean (한국어)",
		"zh" => "Chinese (中文)",
		"vi" => "Vietnamese (Tiếng Việt)",
		"ru" => "Russian (Русский)",
		"tr" => "Turkish (Türkçe)",
		"hu" => "Hungarian (Magyar)",
		_ => "English"
	};

	private static bool LooksLikeCommand(string word) =>
		CommandWords.Contains(word) || CommandStems.Any(stem => word.StartsWith(stem, StringComparison.OrdinalIgnoreCase));

	public static bool TryNormalizeCustom(string? value, out string normalized)
	{
		normalized = "";
		if (string.IsNullOrEmpty(value) || value.Any(char.IsControl) || value.IndexOfAny(new[] { (char)0x2028, (char)0x2029 }) >= 0)
		{
			return false;
		}
		string text = value.Trim();
		int runes = text.EnumerateRunes().Count();
		if (runes < 1 || runes > MaxCustomLength)
		{
			return false;
		}
		bool hasLetter = false;
		foreach (Rune r in text.EnumerateRunes())
		{
			if (r.Value is ' ' or '(' or ')' or '-')
			{
				continue;
			}
			UnicodeCategory cat = Rune.GetUnicodeCategory(r);
			if ((int)cat > (int)UnicodeCategory.EnclosingMark)
			{
				return false;
			}
			hasLetter |= (int)cat <= (int)UnicodeCategory.OtherLetter;
		}
		if (!hasLetter || text.Split(new[] { ' ', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(LooksLikeCommand))
		{
			return false;
		}
		normalized = text;
		return true;
	}
}
