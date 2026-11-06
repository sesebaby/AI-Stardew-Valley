using System;
using System.Text.Json;

namespace AISV.Core.Favors;

/// <summary>物品请求由游戏那边提供的几件事。</summary>
public interface IItemRequestDesk
{
	bool CanAsk(string npc, int hearts, bool adultPartner, bool child);

	string PlanningOptions(int hearts, bool adultPartner, bool child);

	ItemRequestCandidate? Candidate(string npc, int hearts, bool adultPartner, bool child, ItemRequestPlan? plan);

	/// <summary>开场白说出口以后登记，加任务。</summary>
	void Activate(ItemRequestCandidate candidate, string requestText, string line1, string line2, string handover = "");
}

/// <summary>解析物品请求的两次 AI 回复。</summary>
public static class ItemRequestAi
{
	public static ItemRequestPlan? ParsePlan(string raw)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(Dialogue.JsonCleaner.Clean(raw));
			JsonElement e = doc.RootElement;
			if (e.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			int gold = e.TryGetProperty("gold", out JsonElement g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out int v) ? v : 0;
			bool monetary = gold > 0;
			return new ItemRequestPlan
			{
				Mood = Cut(Text(e, "scene_kind"), 60),
				StageRef = Cut(Text(e, "scene_id"), 120),
				ItemName = Cut(Text(e, "item"), 80),
				PayKind = monetary ? "monetary" : "none",
				PayGold = monetary ? Math.Clamp(gold, 50, 500) : 0,
				Rationale = Cut(Text(e, "why"), 180)
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static (string, string)? ParseLines(string raw)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(Dialogue.JsonCleaner.Clean(raw));
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			string a = Line(Text(doc.RootElement, "beat_1"));
			string b = Line(Text(doc.RootElement, "beat_2"));
			return a.Length == 0 || b.Length == 0 ? null : (a, b);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>当面接过东西时的那句道谢；没有或写坏了返回空。</summary>
	public static string ParseHandover(string raw)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(Dialogue.JsonCleaner.Clean(raw));
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return "";
			}
			string text = Line(Text(doc.RootElement, "thanks"));
			return text.Length is >= 2 and <= 60 ? text : "";
		}
		catch (JsonException)
		{
			return "";
		}
	}

	private static string Line(string text) => Cut(text.Replace('\r', ' ').Replace('\n', ' ').Trim().Trim('"', '“', '”'), 120);

	private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

	private static string Cut(string text, int max)
	{
		string t = text.Trim();
		return t.Length > max ? t[..max] : t;
	}
}
