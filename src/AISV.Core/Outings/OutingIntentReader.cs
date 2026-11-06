using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AISV.Core.Outings;

/// <summary>
/// 从玩家的邀请里读出想去的地方和时间，各语言的说法都认；
/// 中日文直接看包含，其他语言按整词（去掉重音、转小写、标点当空格）。
/// </summary>
public static class OutingIntentReader
{
	private const string Hint = "hint", HourLead = "lead", HourTail = "tail", Dusk = "dusk", Dawn = "dawn", Noon = "noon", Night = "night";

	/// <summary>一门语言一行：词条=说法,说法；词条是地点名或上面的时间记号。</summary>
	private static readonly string[] Phrasebook =
	{
		// English
		"BathHouse_Pool=hot spring,hot springs,spa,bathhouse,bath house;CommunityCenter=community center,community centre;ArchaeologyHouse=museum,library;Beach=beach,seaside,shore,pier,dock;Saloon=saloon,pub,bar;Mountain=mountain,mountains,hillside;Forest=forest,woods,cindersap;FarmHouse=farmhouse,farm house,my place,my house,our home;Farm=farm;Town=town,pelican town,plaza,square;Sewer=sewer,sewers;"
		+ "hint=at the,to the,in the,by the,near the,over at;lead=at,around,about,by,after,before;tail=o'clock,oclock,hrs;dusk=late afternoon,dusk,sunset;dawn=morning,dawn,sunrise;noon=afternoon,noon,midday,lunchtime;night=evening,night,tonight",
		// 中文
		"BathHouse_Pool=温泉,澡堂,泡澡,浴池;CommunityCenter=社区中心,活动中心;ArchaeologyHouse=博物馆,图书馆;Beach=海边,海滩,沙滩,码头;Saloon=酒吧,酒馆,餐吧;Mountain=山上,山里,山顶,山区,湖边;Forest=森林,林子,树林;FarmHouse=我家,我那儿,农舍;Farm=农场;Town=镇上,镇里,小镇,广场;Sewer=下水道,污水道;"
		+ "lead=在,约,大概;tail=点,點,时,時;dusk=傍晚,黄昏,日落;dawn=早上,上午,清晨,大清早,一早;noon=下午,午后,中午,正午;night=晚上,今晚,夜里,半夜,入夜",
		// Deutsch
		"BathHouse_Pool=therme,badehaus,thermalbad;CommunityCenter=gemeindezentrum,dorfzentrum;ArchaeologyHouse=museum,bibliothek,bücherei;Beach=strand,ufer,anlegesteg;Saloon=kneipe,wirtshaus,saloon;Mountain=berg,berge,gebirge,bergsee;Forest=wald,waldstück;FarmHouse=bauernhaus,zu mir,bei mir;Farm=hof,bauernhof;Town=stadt,dorf,dorfplatz,marktplatz;Sewer=kanalisation,abwasserkanal;"
		+ "hint=zum,zur,beim,an den,in den,auf den;lead=um,gegen,nach,vor,ab;tail=uhr;dusk=spätnachmittag,später nachmittag,dämmerung;dawn=morgens,vormittag,vormittags,früh am morgen;noon=nachmittag,nachmittags,mittags;night=abend,abends,nachts,heute abend",
		// Español
		"BathHouse_Pool=termas,balneario,aguas termales;CommunityCenter=centro comunitario,centro cívico;ArchaeologyHouse=museo,biblioteca;Beach=playa,costa,muelle;Saloon=taberna,cantina,bar;Mountain=montaña,montañas,sierra;Forest=bosque,arboleda;FarmHouse=mi casa,mi hogar,casa de la granja;Farm=granja,finca;Town=pueblo,plaza,centro del pueblo;Sewer=alcantarilla,alcantarillas,cloacas;"
		+ "hint=en la,en el,a la,al,cerca de;lead=a las,sobre las,hacia las,tras las,antes de las,después de las;tail=horas,hs;dusk=última hora de la tarde,atardecer;dawn=por la mañana,mañanita,madrugada;noon=por la tarde,tarde,mediodía;night=noche,esta noche,anochecer",
		// Français
		"BathHouse_Pool=thermes,source chaude,bains;CommunityCenter=centre communautaire,salle des fêtes;ArchaeologyHouse=musée,bibliothèque;Beach=plage,bord de mer,jetée;Saloon=taverne,bistrot,auberge;Mountain=montagne,montagnes;Forest=forêt,bois;FarmHouse=chez moi,ma maison;Farm=ferme;Town=ville,village,place du village;Sewer=égout,égouts;"
		+ "hint=à la,au,aux,près de,dans le,dans la;lead=à,vers,après,avant;tail=heures;dusk=fin d'après-midi,crépuscule;dawn=matin,matinée;noon=après-midi,midi;night=soir,ce soir,nuit,soirée",
		// Italiano
		"BathHouse_Pool=terme,bagni termali;CommunityCenter=centro civico,centro comunitario;ArchaeologyHouse=museo,biblioteca;Beach=spiaggia,riva,molo;Saloon=osteria,taverna,locanda;Mountain=montagna,montagne,monti;Forest=foresta,bosco;FarmHouse=casa mia,da me;Farm=fattoria,podere;Town=paese,piazza,città;Sewer=fogna,fogne;"
		+ "hint=alla,nel,nella,vicino a;lead=alle,verso le,dopo le,prima delle;tail=ore;dusk=tardo pomeriggio,tramonto;dawn=mattina,mattino,stamattina;noon=pomeriggio,mezzogiorno;night=sera,stasera,notte,stanotte",
		// Português
		"BathHouse_Pool=termas,banhos termais;CommunityCenter=centro comunitário,centro da comunidade;ArchaeologyHouse=museu,biblioteca;Beach=praia,orla,cais;Saloon=boteco,taverna;Mountain=montanha,montanhas,serra;Forest=floresta,mata;FarmHouse=minha casa,lá em casa;Farm=fazenda,sítio;Town=cidade,vila,praça;Sewer=esgoto,esgotos,bueiro;"
		+ "hint=na,nas,ao,para a,perto de;lead=às,pelas,lá pelas,depois das,antes das;dusk=fim da tarde,entardecer;dawn=de manhã,manhã,cedinho;noon=à tarde,meio-dia;night=noite,à noite,hoje à noite",
		// Tiếng Việt
		"BathHouse_Pool=suối nước nóng,nhà tắm;CommunityCenter=trung tâm cộng đồng,nhà văn hóa;ArchaeologyHouse=bảo tàng,thư viện;Beach=bãi biển,bờ biển,bến tàu;Saloon=quán rượu,quán bar;Mountain=núi,trên núi;Forest=rừng,khu rừng;FarmHouse=nhà tôi,nhà mình;Farm=nông trại,trang trại;Town=thị trấn,quảng trường;Sewer=cống,cống ngầm;"
		+ "hint=ở,tại,đến,tới;lead=lúc,khoảng,sau,trước;tail=giờ;dusk=cuối buổi chiều,chiều muộn,hoàng hôn;dawn=buổi sáng,sáng sớm,sáng;noon=buổi chiều,chiều,buổi trưa;night=buổi tối,tối,ban đêm,đêm"
	};

	private static readonly Dictionary<string, string[]> Lexicon = Phrasebook
		.SelectMany(language => language.Split(';'))
		.Select(entry => entry.Split('='))
		.GroupBy(pair => pair[0], pair => pair[1])
		.ToDictionary(g => g.Key, g => g.SelectMany(list => list.Split(',')).ToArray());

	/// <summary>按这个顺序找，先找到的算。</summary>
	private static readonly string[] Places = { "BathHouse_Pool", "CommunityCenter", "ArchaeologyHouse", "Beach", "Saloon", "Mountain", "Forest", "FarmHouse", "Farm", "Town", "Sewer" };

	private static readonly string[] PlaceHints = Lexicon[Hint], HourBefore = Lexicon[HourLead], HourAfter = Lexicon[HourTail];

	private static readonly string[] LateAfternoon = Lexicon[Dusk], Morning = Lexicon[Dawn], Afternoon = Lexicon[Noon], Evening = Lexicon[Night];

	private static readonly Regex AmPm = new Regex(@"\b(?<hour>[1-9]|1[0-2])\s*(?<period>a\.?m\.?|p\.?m\.?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex Clock = new Regex(@"\b(?<hour>[01]?\d|2[0-3])\s*(?:(?:h|:)\s*(?<minute>[0-5]\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex HourBeforeRegex = new Regex(@"\b(?:" + Alternatives(HourBefore) + @")\s+(?<hour>[1-9]|1[0-9]|2[0-3])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex HourAfterRegex = new Regex(@"(?<![0-9])(?<hour>[1-9]|1[0-9]|2[0-3])\s*(?:" + Alternatives(HourAfter) + @")(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>读地点和时间。<paramref name="exists"/>：这个存档里有没有这个地方（读不出来时当有）。</summary>
	public static OutingIntent Read(string? playerText, IEnumerable<string> supported, Func<string, bool>? exists = null)
	{
		OutingIntent intent = new OutingIntent();
		string text = Normalize(playerText);
		if (text.Length > 0)
		{
			HashSet<string> known = supported.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeLocation).ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (string location in Places)
			{
				string? hit = Lexicon[location].FirstOrDefault(w => Contains(text, w));
				if (hit != null)
				{
					intent.RequestedLocation = NormalizeLocation(location);
					intent.RawLocation = hit;
					intent.LocationStatus = known.Contains(intent.RequestedLocation) && Exists(exists, intent.RequestedLocation) ? OutingIntent.Supported : OutingIntent.Unsupported;
					break;
				}
			}
			if (!intent.HasLocation && PlaceHints.Any(h => Contains(text, h)))
			{
				intent.LocationStatus = OutingIntent.Unresolved;
			}
		}
		if (TryReadTime(playerText, out int min, out int max))
		{
			intent.TimeMin = min;
			intent.TimeMax = max;
		}
		return intent;
	}

	/// <summary>
	/// 时间：7pm、19:30 / 19h、"at 7"、"7点"；都没有时认时段（傍晚 1600–1900、早上 600–1200、下午 1200–1800、晚上 1800–2400）。
	/// 具体钟点不带上下午时，话里有下午/傍晚/晚上就当下午的钟点；晚上 12 点是半夜。
	/// </summary>
	public static bool TryReadTime(string? playerText, out int min, out int max)
	{
		min = max = 0;
		string words = Normalize(playerText);
		string clock = NormalizeKeeping(playerText, '.', ':');
		if (words.Length == 0 && clock.Length == 0)
		{
			return false;
		}
		Match m = AmPm.Match(clock);
		if (m.Success && int.TryParse(m.Groups["hour"].Value, out int h))
		{
			bool pm = m.Groups["period"].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase);
			h = pm && h < 12 ? h + 12 : !pm && h == 12 ? 0 : h;
			return Exact(h, 0, out min, out max);
		}
		m = Clock.Match(clock);
		if (m.Success && int.TryParse(m.Groups["hour"].Value, out h))
		{
			return Exact(h, int.TryParse(m.Groups["minute"].Value, out int minute) ? minute : 0, out min, out max);
		}
		bool later = Any(words, LateAfternoon) || Any(words, Afternoon) || Any(words, Evening);
		foreach (Regex r in new[] { HourBeforeRegex, HourAfterRegex })
		{
			m = r.Match(words);
			if (m.Success && int.TryParse(m.Groups["hour"].Value, out h))
			{
				int hour = later && h < 12 ? h + 12 : h == 12 && Any(words, Evening) && !Any(words, Afternoon) ? 24 : h;
				return Exact(hour, 0, out min, out max);
			}
		}
		if (Any(words, LateAfternoon))
		{
			return Window(1600, 1900, out min, out max);
		}
		if (Any(words, Morning))
		{
			return Window(600, 1200, out min, out max);
		}
		if (Any(words, Afternoon))
		{
			return Window(1200, 1800, out min, out max);
		}
		return Any(words, Evening) && Window(1800, 2400, out min, out max);
	}

	/// <summary>地点名的几种写法归到游戏里的名字。</summary>
	public static string NormalizeLocation(string? location)
	{
		string t = (location ?? "").Trim();
		return t switch
		{
			"Community Center" or "CommunityCentre" => "CommunityCenter",
			"Museum" or "Library" => "ArchaeologyHouse",
			"Cindersap Forest" or "CindersapForest" => "Forest",
			"Farm House" => "FarmHouse",
			"Bath House" or "Spa" or "spa" or "BathHouse" or "Bathhouse" => "BathHouse_Pool",
			"PelicanTown" or "Pelican Town" => "Town",
			_ => t
		};
	}

	internal static string Normalize(string? text) => NormalizeKeeping(text);

	private static string NormalizeKeeping(string? text, params char[] keep)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		StringBuilder sb = new StringBuilder(text.Length);
		foreach (char c in text.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
			{
				sb.Append(char.IsLetterOrDigit(c) || keep.Contains(c) ? char.ToLowerInvariant(c) : ' ');
			}
		}
		return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
	}

	internal static bool Contains(string normalizedText, string phrase)
	{
		string p = Normalize(phrase);
		if (p.Length == 0)
		{
			return false;
		}
		return p.Any(c => c >= '⺀') ? normalizedText.Contains(p, StringComparison.Ordinal) : (" " + normalizedText + " ").Contains(" " + p + " ", StringComparison.Ordinal);
	}

	private static bool Any(string text, string[] words) => words.Any(w => Contains(text, w));

	private static bool Exists(Func<string, bool>? exists, string location)
	{
		try
		{
			return exists?.Invoke(location) ?? true;
		}
		catch
		{
			return true;
		}
	}

	private static bool Exact(int hour, int minute, out int min, out int max)
	{
		min = max = Math.Clamp(Math.Clamp(hour, 0, 26) * 100 + Math.Clamp(minute, 0, 59), 600, 2600);
		return true;
	}

	private static bool Window(int start, int end, out int min, out int max)
	{
		min = Math.Clamp(start, 600, 2600);
		max = Math.Clamp(end, min, 2600);
		return true;
	}

	private static string Alternatives(string[] words) => string.Join("|", words.Select(Normalize).Where(w => w.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(w => w.Length).Select(Regex.Escape));
}
