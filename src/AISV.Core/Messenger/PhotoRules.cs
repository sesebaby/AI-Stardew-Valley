using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AISV.Core.Messenger;

public enum PhotoKind
{
	None,
	Subject,
	Selfie,
	Absent
}

public sealed record PhotoRequest(PhotoKind Kind, string Subject)
{
	public static readonly PhotoRequest None = new PhotoRequest(PhotoKind.None, "");
}

/// <summary>一张照片：谁发的、拍的谁、在哪、什么时候、回的是农夫哪句话。</summary>
public sealed record PhotoRecord(string Id, string Sender, string Subject, string LocationName, string LocationDisplay, int Day, string Season, int Year, int Time, string PlayerMessage);

/// <summary>回复要附带的情况说明：放进回复提示词的一段，以及记进对话记录的一句。</summary>
public sealed record ReplyFrame(string Section, string HistoryNote);

/// <summary>农夫在信使里要照片 / 问某人在哪时，村民拍什么。</summary>
public static class PhotoRules
{
	private static readonly HashSet<string> PhotoWords = Vocabulary(
		"photo photos photograph picture pictures pic pics selfie selfies snapshot shot",
		"foto fotos bild bilder schnappschuss aufnahme knipsen zeig zeigen",
		"fotografia retrato imagen muestrame ensename",
		"cliche montremoi",
		"fotina scatto mostrami immagine",
		"fotinha fotinho imagem mostra mostre manda");

	private static readonly HashSet<string> WhereWords = Vocabulary("where wheres whereabouts", "wo wohin", "donde", "dove dov", "onde cade");

	private static readonly string[] WherePhrases = { " ou es ", " ou est ", " o dau ", " o đau " };

	private static readonly string[] CjkPhotoWords = { "拍照", "合影", "合照", "自拍", "相片", "照片", "来一张", "拍张", "拍个", "图片" };

	/// <param name="candidates">村民的内部名和显示名。</param>
	/// <param name="isWithSender">这个人现在和发消息的村民在同一个地方。</param>
	/// <param name="photographedToday">今天已经拍过这个人。</param>
	public static PhotoRequest Plan(string? message, string sender, IEnumerable<(string Name, string Display)> candidates, Func<string, bool> isWithSender, Func<string, bool> photographedToday)
	{
		string text = MessengerRules.Normalize(message);
		if (text.Length == 0)
		{
			return PhotoRequest.None;
		}
		List<string> words = Words(text);
		string joined = " " + string.Join(" ", words) + " ";
		bool photo = words.Any(PhotoWords.Contains) || CjkPhotoWords.Any(text.Contains);
		bool where = words.Any(WhereWords.Contains) || WherePhrases.Any(joined.Contains) || text.Contains('哪');
		string? named = Mentioned(text, candidates, sender).FirstOrDefault();
		if (named != null)
		{
			if (isWithSender(named))
			{
				return !(photo || where) && photographedToday(named) ? PhotoRequest.None : new PhotoRequest(PhotoKind.Subject, named);
			}
			return photo || where ? new PhotoRequest(PhotoKind.Absent, named) : PhotoRequest.None;
		}
		return photo ? new PhotoRequest(PhotoKind.Selfie, sender) : PhotoRequest.None;
	}

	private static HashSet<string> Vocabulary(params string[] languages) =>
		languages.SelectMany(l => l.Split(' ')).ToHashSet(StringComparer.Ordinal);

	/// <summary>消息里提到的其他村民，按出现的先后。</summary>
	public static List<string> Mentioned(string text, IEnumerable<(string Name, string Display)> candidates, string? exclude)
	{
		string folded = Fold(text);
		List<(int At, string Name)> hits = new List<(int, string)>();
		foreach ((string name, string display) in candidates)
		{
			if (string.IsNullOrWhiteSpace(name) || string.Equals(name, exclude, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			int at = Find(folded, Fold(name));
			if (at < 0 && !string.IsNullOrWhiteSpace(display))
			{
				at = Find(folded, Fold(display));
			}
			if (at >= 0)
			{
				hits.Add((at, name));
			}
		}
		return hits.OrderBy(h => h.At).Select(h => h.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>附照片、没法拍时放进回复提示词的说明。</summary>
	public static ReplyFrame? Frame(PhotoRequest request, string subjectName, string place, string player)
	{
		string where = string.IsNullOrWhiteSpace(place) ? "你现在待的地方" : place.Trim();
		string at = string.IsNullOrWhiteSpace(place) ? "" : "（在" + place.Trim() + "）";
		return request.Kind switch
		{
			PhotoKind.Subject => new ReplyFrame(
				$"【信使·附照片】{player} 问到了 {subjectName}。他这会儿正好和你在一起，{where}。你顺手给 {subjectName} 拍了一张，跟这条回复一起发过去。\n" +
				$"配的这句话要先回答农夫问的，再自然地扯到照片上：{subjectName} 在干嘛、这是什么地方、你怎么看他，用你自己的口气。" +
				"农夫看得见照片，所以别写成图片说明，也别说自己发不了照片。" +
				$"不要编大事，只写 {subjectName} 这时候待在{where}说得通的样子。",
				$"[你随这条回复给农夫发了一张 {subjectName} 的照片{at}。]"),
			PhotoKind.Selfie => new ReplyFrame(
				$"【信使·附照片】{player} 想看看你。你刚在{where}拍了张自拍，跟这条回复一起发过去。\n" +
				"配的这句话先回答农夫，再顺嘴提提这张自拍：你在哪儿、在忙什么，带上你的性子——害羞、得意或者打趣；关系够近才可以带点暧昧。" +
				"农夫看得见照片，所以别写成图片说明，也别说自己发不了照片。",
				$"[你随这条回复给农夫发了一张自拍{at}。]"),
			PhotoKind.Absent => new ReplyFrame(
				$"【信使·拍不了】{player} 问起 {subjectName}（人在哪，或者想看他的照片），可 {subjectName} 不在你身边——你在{where}，他不在这儿，所以这次没有照片。\n" +
				$"自然地告诉农夫 {subjectName} 不在你这边、拍不了。除非上面的资料写明了，别装作知道他到底在哪。" +
				"无论如何都不能说你附上了或者发了照片。",
				$"[农夫问起 {subjectName}，他不在你身边，所以你没发照片。]"),
			_ => null
		};
	}

	/// <summary>把照片对到当天、回的是同一句话的那条记录上。</summary>
	public static Dictionary<TextEntry, PhotoRecord> Match(IEnumerable<TextEntry> entries, IEnumerable<PhotoRecord> photos)
	{
		List<PhotoRecord> left = photos.ToList();
		Dictionary<TextEntry, PhotoRecord> result = new Dictionary<TextEntry, PhotoRecord>(ReferenceEqualityComparer.Instance);
		foreach (TextEntry e in entries)
		{
			string said = MessengerRules.Normalize(e.PlayerMessage);
			PhotoRecord? p = left.FirstOrDefault(p => p.Day == e.Day && p.Year == e.Year && string.Equals(p.Season, e.Season, StringComparison.OrdinalIgnoreCase) && string.Equals(MessengerRules.Normalize(p.PlayerMessage), said, StringComparison.Ordinal));
			if (p != null)
			{
				result[e] = p;
				left.Remove(p);
			}
		}
		return result;
	}

	public static string Fold(string text)
	{
		string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
		StringBuilder sb = new StringBuilder(decomposed.Length);
		foreach (char c in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(c);
			}
		}
		return sb.ToString().Normalize(NormalizationForm.FormC);
	}

	/// <summary>拉丁字母的名字按整词找、至少 3 个字母；中日韩的名字直接找。</summary>
	private static int Find(string text, string name)
	{
		name = name.Trim();
		if (name.Length == 0)
		{
			return -1;
		}
		if (name.All(IsLatin))
		{
			return name.Count(char.IsLetter) >= 3 ? WordIndex(text, name) : -1;
		}
		return text.IndexOf(name, StringComparison.Ordinal);
	}

	private static int WordIndex(string text, string word)
	{
		if (word.Length == 0)
		{
			return -1;
		}
		for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
		{
			bool startOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]) || !IsLatin(text[at - 1]);
			int end = at + word.Length;
			bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]) || !IsLatin(text[end]);
			if (startOk && endOk)
			{
				return at;
			}
		}
		return -1;
	}

	private static bool IsLatin(char c) => c < 0x2E80;

	private static List<string> Words(string text)
	{
		List<string> words = new List<string>();
		StringBuilder sb = new StringBuilder();
		foreach (char c in Fold(text))
		{
			if (char.IsLetterOrDigit(c))
			{
				sb.Append(c);
				continue;
			}
			if (sb.Length > 0)
			{
				words.Add(sb.ToString());
			}
			sb.Clear();
		}
		if (sb.Length > 0)
		{
			words.Add(sb.ToString());
		}
		return words;
	}
}
