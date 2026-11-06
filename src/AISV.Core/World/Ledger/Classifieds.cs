using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AISV.Core.Dialogue;
using AISV.Core.Text;

namespace AISV.Core.World.Ledger;

public static class AdKinds
{
	public const string Sell = "sell";
	public const string Show = "show";
	public const string Work = "work";
	public const string Lost = "lost";
}

/// <summary>分类广告上的一条。</summary>
public sealed class Ad
{
	public string Id { get; set; } = "";

	public string Npc { get; set; } = "";

	public string Kind { get; set; } = AdKinds.Sell;

	/// <summary>卖的、找的东西叫什么（没有物品时就是这几个字）。</summary>
	public string Thing { get; set; } = "";

	public string ItemId { get; set; } = "";

	public int Quality { get; set; }

	public int Price { get; set; }

	/// <summary>广告正文（AI 用他的口吻写，没写成时用固定说法）。</summary>
	public string Text { get; set; } = "";

	public bool Written { get; set; }

	public int Day { get; set; }

	public int ExpiresDay { get; set; }

	/// <summary>到这一天会有人买走（没有物品的广告用）。</summary>
	public int SellDay { get; set; } = -1;

	public int SoldDay { get; set; } = -1;

	/// <summary>买走的人；农夫是 "farmer"，没说是谁时为空。</summary>
	public string Buyer { get; set; } = "";

	/// <summary>这条广告从哪来（hustle / divorce:村民 / theft:记录），同一来源只登一次。</summary>
	public string Source { get; set; } = "";
}

public static class ClassifiedRules
{
	public const int MaxAds = 30;
	public const int Days = 7;
	public const int KeepSold = 10;
	public const string Farmer = "farmer";
	public const string Request = "classifieds";
	public const int MaxTextChars = 60;

	/// <summary>卖给农夫的价：比卖店里高一半。</summary>
	public static int AskingPrice(int sellPrice) => Math.Max(1, sellPrice * 3 / 2);

	public static bool Open(Ad a, int today) => a.SoldDay < 0 && today <= a.ExpiresDay;

	/// <summary>过期的、卖掉太久的拿掉，最多留 30 条。</summary>
	public static void Trim(List<Ad> ads, int today)
	{
		ads.RemoveAll(a => a.SoldDay >= 0 ? today - a.SoldDay > KeepSold : today - a.ExpiresDay > 3);
		if (ads.Count > MaxAds)
		{
			ads.RemoveRange(MaxAds, ads.Count - MaxAds);
		}
	}

	public static string Title(Ad a) => a.Kind switch
	{
		AdKinds.Show => Tr.Get("ads.title.show", "演出：{{thing}}", ("thing", a.Thing)),
		AdKinds.Work => Tr.Get("ads.title.work", "接活：{{thing}}", ("thing", a.Thing)),
		AdKinds.Lost => Tr.Get("ads.title.lost", "寻物：{{thing}}", ("thing", a.Thing)),
		_ => Tr.Get("ads.title.sell", "出售：{{thing}}", ("thing", a.Thing))
	};

	/// <summary>AI 没写成时的正文。</summary>
	public static string Plain(Ad a, string who) => a.Kind switch
	{
		AdKinds.Show => Tr.Get("ads.plain.show", "今晚九点，星之果实餐吧，{{who}}现场表演。路过的进来坐坐。", ("who", who)),
		AdKinds.Work => Tr.Get("ads.plain.work", "{{thing}}，价钱好商量。找{{who}}。", ("thing", a.Thing), ("who", who)),
		AdKinds.Lost => Tr.Get("ads.plain.lost", "丢了{{thing}}，捡到的请还给{{who}}，必有酬谢。", ("thing", a.Thing), ("who", who)),
		_ => a.Price > 0
			? Tr.Get("ads.plain.sellPrice", "{{thing}}，{{price}}g，诚心要的来找{{who}}。", ("thing", a.Thing), ("price", a.Price), ("who", who))
			: Tr.Get("ads.plain.sell", "{{thing}}，九成新，价钱面议。", ("thing", a.Thing))
	};

	/// <summary>一次让 AI 写好几条广告。</summary>
	public static string System(string language, IEnumerable<(Ad Ad, string Who, string Personality, string Why)> ads)
	{
		StringBuilder sb = new StringBuilder($"这次只用{language}回答。\n\n你替星露谷鹈鹕镇的村民写分类广告，贴在镇上的公告栏里。每条都用登广告那个人自己的口吻，像他本人随手写的。\n\n");
		foreach ((Ad a, string who, string personality, string why) in ads)
		{
			sb.Append($"## {a.Id}\n- 登广告的人：{who}\n- 性格（只是资料）：{TextTools.Truncate(personality, 160)}\n- 广告：{Title(a)}{(a.Price > 0 ? $"，{a.Price}g" : "")}\n");
			if (why.Length > 0)
			{
				sb.Append($"- 背后的事（不用直说）：{why}\n");
			}
			sb.Append('\n');
		}
		sb.Append($"# 输出\n只输出一个 JSON 对象，键是上面的编号，值是那条广告的正文。每条 {MaxTextChars} 个字以内，不写标题和价钱以外的数字，不加引号，不用表情。\n");
		return sb.ToString();
	}

	public static Dictionary<string, string> Parse(string? raw)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using JsonDocument doc = JsonDocument.Parse(JsonCleaner.Clean(raw ?? ""));
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return result;
			}
			foreach (JsonProperty p in doc.RootElement.EnumerateObject())
			{
				string text = p.Value.ValueKind == JsonValueKind.String ? TextTools.Truncate((p.Value.GetString() ?? "").Trim().Trim('"', '“', '”'), MaxTextChars + 20) : "";
				if (text.Length > 0)
				{
					result[p.Name] = text;
				}
			}
		}
		catch (JsonException)
		{
		}
		return result;
	}
}
