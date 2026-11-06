using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace AISV.Core.Awareness;

/// <summary>村民看到或听到的一件事（存档里的字段名不能改）。</summary>
public sealed class Sighting
{
	/// <summary>大类（Gift、Consumption、Explosion、PassOut、LockedDoor、SocialInteraction、Custom 等）。</summary>
	public string Act { get; set; } = "Custom";

	public int Clock { get; set; }

	public string Doer { get; set; } = "";

	public string EventId { get; set; } = "";

	public Dictionary<string, string> Facts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public bool FirstHand { get; set; } = true;

	public int OnDay { get; set; }

	public string Place { get; set; } = "";

	public List<Witness> SeenBy { get; set; } = new List<Witness>();

	public double Weight { get; set; } = 1.0;

	public int X { get; set; }

	public int Y { get; set; }

	[JsonIgnore]
	public string Type => SightingCatalog.NormalizeType(Detail("actionId", Act));

	public string Detail(string key, string fallback = "") =>
		Facts.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

	public double DetailNumber(string key) =>
		double.TryParse(Detail(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

	public Witness? WitnessNamed(string npc) => SeenBy.FirstOrDefault(w => string.Equals(w.Who, npc, StringComparison.OrdinalIgnoreCase));

	public static string ActionFor(string type) => SightingCatalog.NormalizeType(type) switch
	{
		"gift" => "Gift",
		"consume" => "Consumption",
		"bomb" => "Explosion",
		"sleep_outside" => "PassOut",
		"room_locked" => "LockedDoor",
		"kiss" or "night_walk" or "talk" or "block_path" or "phone_text" or "argue" or "flirt" or "stare" or "dating_proposal" or "marriage_proposal" => "SocialInteraction",
		_ => "Custom"
	};
}

/// <summary>一位目击者看到的情况。</summary>
public sealed class Witness
{
	public bool ClearView { get; set; } = true;

	public List<string> FiredRules { get; set; } = new List<string>();

	public bool FirstHand { get; set; }

	public double Focus { get; set; } = 1.0;

	public Dictionary<string, double> Gauges { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

	/// <summary>target / vendor / relative / bystander。</summary>
	public string Part { get; set; } = "";

	public bool Quiet { get; set; }

	public int Repeats { get; set; } = 1;

	public double SightShare { get; set; } = 1.0;

	/// <summary>记下来的确定度（0 = 没记，按确信度算）。</summary>
	public int Sureness { get; set; }

	public double TilesAway { get; set; }

	public string Who { get; set; } = "";

	[JsonIgnore]
	public double Confidence => Certainty.Confidence(this);

	public Witness Copy() => new Witness
	{
		Who = Who,
		SightShare = SightShare,
		TilesAway = TilesAway,
		FirstHand = FirstHand,
		ClearView = ClearView,
		Focus = Focus,
		Repeats = Repeats,
		Sureness = Sureness,
		Part = Part,
		Gauges = new Dictionary<string, double>(Gauges, StringComparer.OrdinalIgnoreCase),
		FiredRules = FiredRules.ToList(),
		Quiet = Quiet
	};
}

public sealed class GrudgeData
{
	public string NpcName { get; set; } = "";

	public string MeterId { get; set; } = "";

	public double Amount { get; set; }
}

/// <summary>bonds/witnessed.json 的内容。</summary>
public sealed class AwarenessData
{
	public List<Sighting> Events { get; set; } = new List<Sighting>();

	public List<GrudgeData> Grudges { get; set; } = new List<GrudgeData>();
}

/// <summary>确信度、确定度（1～5）和距离可见度。</summary>
public static class Certainty
{
	public static double Confidence(Witness w)
	{
		double c = Math.Clamp(w.SightShare, 0, 1) * Math.Clamp(w.Focus, 0, 1);
		if (!w.ClearView)
		{
			c *= 0.35;
		}
		if (w.FirstHand)
		{
			c *= 1.15;
		}
		return Math.Clamp(c, 0, 1);
	}

	public static int FromConfidence(Witness w)
	{
		if (w.FirstHand)
		{
			return 5;
		}
		double c = w.Confidence;
		return c >= 0.85 ? 5 : c >= 0.65 ? 4 : c >= 0.45 ? 3 : c >= 0.25 ? 2 : 1;
	}

	public static int Level(Witness w, int floor = 1)
	{
		int level = w.Sureness > 0 ? w.Sureness : FromConfidence(w);
		return Math.Clamp(Math.Max(level, floor), 1, 5);
	}

	public static double VisibilityAt(double tiles) => tiles <= 2 ? 1.0 : tiles <= 5 ? 0.85 : tiles <= 8 ? 0.65 : tiles <= 12 ? 0.45 : 0.25;

	public static string Words(int level) => level switch
	{
		5 => "看得清清楚楚",
		4 => "看见了",
		3 => "觉得看见了",
		2 => "不太确定",
		_ => "只瞥了一眼"
	};
}
