using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AISV.Core.Social.Drama;

/// <summary>社交剧情的存档：看法、片段、农夫的影响、传闻、成人关系紧张。</summary>
public sealed class DramaState
{
	[JsonPropertyName("strains")]
	public List<AdultStrain> Adult { get; set; } = new List<AdultStrain>();

	[JsonPropertyName("beats")]
	public List<DramaBeat> Beats { get; set; } = new List<DramaBeat>();

	[JsonPropertyName("influences")]
	public List<Influence> Influences { get; set; } = new List<Influence>();

	[JsonPropertyName("opinions")]
	public List<Opinion> Opinions { get; set; } = new List<Opinion>();

	[JsonPropertyName("rumors")]
	public List<Rumor> Rumors { get; set; } = new List<Rumor>();
}

/// <summary>一个村民（持有人）对另一个人的看法，各项 0～1。</summary>
public sealed class Opinion
{
	[JsonPropertyName("about")]
	public string About { get; set; } = "";

	[JsonPropertyName("annoyance")]
	public float Annoyance { get; set; }

	[JsonPropertyName("attitude")]
	public string Attitude { get; set; } = Stances.Neutral;

	[JsonPropertyName("doubt")]
	public float Doubt { get; set; }

	[JsonPropertyName("envy")]
	public float Envy { get; set; }

	[JsonPropertyName("esteem")]
	public float Esteem { get; set; } = 0.5f;

	[JsonPropertyName("trust")]
	public float Faith { get; set; } = 0.5f;

	[JsonPropertyName("fealty")]
	public float Fealty { get; set; } = 0.45f;

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("moved_on")]
	public int MovedOn { get; set; }

	[JsonPropertyName("origins")]
	public List<string> Origins { get; set; } = new List<string>();

	[JsonPropertyName("who")]
	public string Who { get; set; } = "";

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

/// <summary>一个剧情片段（谁对谁说了重话、背后抱怨……），只记不显示。</summary>
public sealed class DramaBeat
{
	[JsonPropertyName("doer")]
	public string Doer { get; set; } = "";

	[JsonPropertyName("force")]
	public float Force { get; set; }

	[JsonPropertyName("gist")]
	public string Gist { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = "";

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("day")]
	public int OnDay { get; set; }

	[JsonPropertyName("receiver")]
	public string Receiver { get; set; } = "";
}

/// <summary>农夫在某个村民面前说起另一个人（维护、透露、提醒、挑拨）。</summary>
public sealed class Influence
{
	[JsonPropertyName("aim")]
	public string Aim { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("move")]
	public string Move { get; set; } = "";

	[JsonPropertyName("day")]
	public int OnDay { get; set; }

	[JsonPropertyName("origins")]
	public List<string> Origins { get; set; } = new List<string>();

	[JsonPropertyName("slant")]
	public int Slant { get; set; }

	[JsonPropertyName("subject")]
	public string Subject { get; set; } = "";

	[JsonPropertyName("speaker")]
	public string Talker { get; set; } = "";

	[JsonPropertyName("upshot")]
	public string Upshot { get; set; } = "";

	[JsonPropertyName("uptake")]
	public float Uptake { get; set; }
}

/// <summary>一条在村里传的话：谁知道、信不信、传歪了多少。</summary>
public sealed class Rumor
{
	[JsonPropertyName("about")]
	public List<string> About { get; set; } = new List<string>();

	[JsonPropertyName("believability")]
	public float Believability { get; set; }

	[JsonPropertyName("born_day")]
	public int BornDay { get; set; }

	[JsonPropertyName("carrier")]
	public string Carrier { get; set; } = "";

	[JsonPropertyName("comes_from")]
	public string ComesFrom { get; set; } = "";

	[JsonPropertyName("convinced")]
	public List<string> Convinced { get; set; } = new List<string>();

	[JsonPropertyName("dies_day")]
	public int DiesDay { get; set; }

	[JsonPropertyName("doubters")]
	public List<string> Doubters { get; set; } = new List<string>();

	[JsonPropertyName("harm")]
	public float Harm { get; set; }

	[JsonPropertyName("heard")]
	public List<string> Heard { get; set; } = new List<string>();

	[JsonPropertyName("heat")]
	public float Heat { get; set; }

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("moved_day")]
	public int MovedDay { get; set; }

	[JsonPropertyName("origins")]
	public List<string> Origins { get; set; } = new List<string>();

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";

	[JsonPropertyName("spread")]
	public string Spread { get; set; } = RumorRules.Private;

	[JsonPropertyName("subject")]
	public string Subject { get; set; } = "";

	[JsonPropertyName("veracity")]
	public string Veracity { get; set; } = "unknown";

	[JsonPropertyName("warp")]
	public float Warp { get; set; }
}

/// <summary>成年村民和原作配偶之间的紧张（成人开关开着才有）。</summary>
public sealed class AdultStrain
{
	[JsonPropertyName("born_day")]
	public int BornDay { get; set; }

	[JsonPropertyName("dies_day")]
	public int DiesDay { get; set; }

	[JsonPropertyName("force")]
	public float Force { get; set; }

	[JsonPropertyName("gist")]
	public string Gist { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("labels")]
	public List<string> Labels { get; set; } = new List<string>();

	[JsonPropertyName("limit")]
	public float Limit { get; set; } = 0.75f;

	[JsonPropertyName("moved_day")]
	public int MovedDay { get; set; }

	[JsonPropertyName("origins")]
	public List<string> Origins { get; set; } = new List<string>();

	[JsonPropertyName("phase")]
	public string Phase { get; set; } = "none";

	[JsonPropertyName("pull")]
	public float Pull { get; set; }

	[JsonPropertyName("remorse")]
	public float Remorse { get; set; }

	[JsonPropertyName("spouse")]
	public string Spouse { get; set; } = "";

	[JsonPropertyName("third")]
	public string Third { get; set; } = "";

	[JsonPropertyName("villager")]
	public string Villager { get; set; } = "";

	[JsonPropertyName("why")]
	public string Why { get; set; } = "";
}

public static class Stances
{
	public const string Neutral = "neutral";
	public const string Admiring = "admiring";
	public const string Protective = "protective";
	public const string Annoyed = "annoyed";
	public const string Suspicious = "suspicious";
	public const string Cold = "cold";
	public const string Badmouthing = "badmouthing";
	public const string Betrayed = "betrayed";
}
