using System.Collections.Generic;
using AISV.Core.Apps;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;

namespace AISV.UI.Apps;

/// <summary>应用的 16×16 像素图标和心情预报的天气图。</summary>
internal static class AppIcons
{
	private static readonly Dictionary<char, Color> P = PixelArt.Inks(('l', new Color(178, 220, 128)), ('q', new Color(255, 196, 214)), ('h', new Color(150, 200, 240)), ('e', new Color(250, 246, 236)), ('m', new Color(170, 176, 190)), ('z', new Color(120, 126, 140)), ('u', new Color(110, 160, 230)));

	public static readonly PixelArt Family = PixelArt.Of(P, "................", ".....kkkkkk.....", "....kGGGGGGk....", "...kGGlGGlGGk...", "..kGGGGGGGGGGk..", "..kGlGGGGGGlGk..", "..kGGGGGGGGGGk..", "...kDGGGGGGDk...", "....kkkkkkkk....", ".......kk.......", "...kkkkkkkkkk...", "...k...kk...k...", "..kqk.kuuk.kqk..", "..kkk.kkkk.kkk..", "................", "................");

	public static readonly PixelArt Perfection = PixelArt.Of(P, "................", ".......kk.......", "......kYYk......", "......kYYk......", "..kkkkYYYYkkkk..", "..kYYYYYYYYYYk..", "...kYYYYYYYYk...", "....kYYYYYYk....", "....kYYYYYYk....", "...kYYYkkYYYk...", "...kYkk..kkYk...", "...kk......kk...", "................", "................", "................", "................");

	public static readonly PixelArt Todo = PixelArt.Of(P, "................", "...kkkkkkkkkk...", "...keeeeeeeek...", "...kekkeeeeek...", "...keeekkkkek...", "...keeeeeeeek...", "...kekkeeeeek...", "...keeekkkkek...", "...keeeeeeeek...", "...kekkeeeeek...", "...keeekkkkek...", "...keeeeeeeek...", "...kkkkkkkkkk...", "................", "................", "................");

	public static readonly PixelArt Focus = PixelArt.Of(P, "................", ".......kk.......", "......kGGk......", "....kkkGGkkk....", "...kRRRRRRRRk...", "..kRRRRRRRRRRk..", "..kRRRRRRRRRRk..", "..kRRRRRRRRRRk..", "..kRRRRRRRRRRk..", "..kRRRRRRRRRRk..", "...kRRRRRRRRk...", "....kkkkkkkk....", "................", "................", "................", "................");

	public static readonly PixelArt Birthdays = PixelArt.Of(P, "................", "......Y..Y......", "......k..k......", "....kkkkkkkk....", "...kqqqqqqqqk...", "...kqqqqqqqqk...", "..kkkkkkkkkkkk..", "..keeqeeqeeqek..", "..keeeeeeeeeek..", "..kqqqqqqqqqqk..", "..kqqqqqqqqqqk..", "..kkkkkkkkkkkk..", "................", "................", "................", "................");

	public static readonly PixelArt Aquarium = PixelArt.Of(P, "................", "..kkkkkkkkkkkk..", "..khhhhhhhhhhk..", "..khhhhhhhhhhk..", "..khhkkkhhhkhk..", "..khkYYYkhkYkk..", "..kkYkYYYkYYkk..", "..khkYYYkhkYkk..", "..khhkkkhhhkhk..", "..khhhhhhhhhhk..", "..kuuuuuuuuuuk..", "..kkkkkkkkkkkk..", "................", "................", "................", "................");

	public static readonly PixelArt Cookbook = PixelArt.Of(P, "................", "..kkkkkk.kkkkk..", ".keeeeeekeeeeek.", ".keRReeekeGGeek.", ".keeeeeekeeeeek.", ".kekkkeekekkkek.", ".keeeeeekeeeeek.", ".kekkkeekekkkek.", ".keeeeeekeeeeek.", ".keeeeeekeeeeek.", "..kkkkkkkkkkkk..", "................", "................", "................", "................", "................");

	public static readonly PixelArt Museum = PixelArt.Of(P, "................", ".......kk.......", ".....kkmmkk.....", "...kkmmmmmmkk...", "..kkkkkkkkkkkk..", "...kek.kek.kek..", "...kek.kek.kek..", "...kek.kek.kek..", "...kek.kek.kek..", "..kkkkkkkkkkkk..", ".kmmmmmmmmmmmmk.", ".kkkkkkkkkkkkkk.", "................", "................", "................", "................");

	public static readonly PixelArt Messenger = PixelArt.Of(P, "................", "...kkkkkkkkkk...", "..kGGGGGGGGGGk..", ".kGGGGGGGGGGGGk.", ".kGGGrrGGrrGGGk.", ".kGGrrrrrrrrGGk.", ".kGGrrrrrrrrGGk.", ".kGGGrrrrrrGGGk.", ".kGGGGrrrrGGGGk.", ".kGGGGGrrGGGGGk.", "..kGGGGGGGGGGk..", "...kkkkkkGGkkk..", ".........kGGk...", "..........kGk...", "...........kk...", "................");

	public static readonly PixelArt Where = PixelArt.Of(P, "................", "......kkkk......", ".....kRRRRk.....", "....kRRwwRRk....", "....kRRwwRRk....", "....kRRRRRRk....", ".....kRRRRk.....", "..kkkkkRRkkkkk..", ".kGGGGkRRkuuuuk.", ".kGlGGGkkuuuuuk.", ".kGGGlGGuuuuuuk.", ".kGGGGGuuuuGGGk.", ".kuuuuuuuGGlGGk.", ".kuuuuuGGGGGGGk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelArt Thanks = PixelArt.Of(P, "................", "..kkkk....kkkk..", ".kqqqqk..kqqqqk.", "kqqwqqqkkqqqqqqk", "kqwqqqqqqqqqqqqk", "kqqqqqqqqqqqqqqk", "kqqqqqqqqqqqqqqk", ".kqqqqqqqqqqqqk.", "..kqqqqqqqqqqk..", "...kqqqqqqqqk...", "....kqqqqqqk....", ".....kqqqqk.....", "......kqqk......", ".......kk.......", "................", "................");

	public static readonly PixelArt Year = PixelArt.Of(P, "..kk........kk..", ".kRRk......kRRk.", "kkkkkkkkkkkkkkkk", "krrrrrrrrrrrrrrk", "krrrrrrrrrrrrrrk", "kkkkkkkkkkkkkkkk", "keeeeeeyyeeeeeek", "keeeeeyyyyeeeeek", "keeyyyyYYyyyyeek", "keeeyyyYYyyyeeek", "keeeeyyyyyyeeeek", "keeeyyyeeyyyeeek", "keeeyyeeeeyyeeek", "keeeeeeeeeeeeeek", "kkkkkkkkkkkkkkkk", "................");

	public static readonly PixelArt Web = PixelArt.Of(P, "......kkkk......", ".....kppppk.....", ".....kpqqpk.....", ".....kppppk.....", "......kkkk......", "......c..c......", ".....c....c.....", "....c......c....", "...c........c...", ".kkkk......kkkk.", "kGGGGk....kbbbbk", "kGllGkcccckbhhbk", "kGGGGk....kbbbbk", ".kkkk......kkkk.", "................", "................");

	public static readonly PixelArt Gossip = PixelArt.Of(P, "......kkkk......", ".....kGllGk.....", ".....kGGGGk.....", "......kkkk......", ".......cc.......", "..cccccccccccc..", ".kkkk......kkkk.", "kyYYyk....kyYYyk", "kyyyyk....kyyyyk", ".kkkk......kkkk.", "..cc........cc..", "..cc........cc..", ".kkkk......kkkk.", "kooooo....krrrrk", "kooook....krrrrk", ".kkkk......kkkk.");

	public static readonly PixelArt Reviews = PixelArt.Of(P, "................", "........k.......", ".......kYk......", ".......kYk......", "......kYyyk.....", "..kkkkkYyyykkkk.", "..kYYYYyyyyyyok.", "...kYyyyyyyyok..", "....kyyyyyyok...", "....kyyyyyyok...", "...kyyyokyyyok..", "...kyokk.kkyok..", "..kkk.....kkk...", "................");

	public static readonly PixelArt Mood = PixelArt.Of(P, "......y.........", ".y....y....y....", "..y..kkkk.y.....", "....kyyyyk......", "...kyYYyyyk.....", "yykyYyyyykkkk...", "...kyyyykWWWWk..", "...kyyykWWWWWWk.", "..y.kkkWWWWWWWk.", "..kkkWWWWWWWWWWk", ".kWWWWWWWWWWWWWk", "kWWWWWWWWWWWWWWk", "kgWWWWWWWWWWWWgk", ".kggggggggggggk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelArt Pelican = PixelArt.Of(P, "................", ".kkkkkkkkkkkkkk.", "kwwwwwwwwwwwwwwk", "kwkkkkkkkkkkkkwk", "kwkhhhhhhhyyhkwk", "kwkhhhhhhhyyhkwk", "kwkhhhhhhhhhhkwk", "kwkhhlhhhhhhhkwk", "kwklllllhhhllkwk", "kwkGGGGlllllGkwk", "kwkGGGGGGGGGGkwk", "kwkkkkkkkkkkkkwk", "kwwwwwwwwwwppwwk", "kwwwwwwwwwppppwk", ".kkkkkkkkkkkkkk.", "................");

	public static readonly PixelArt Rainbow = PixelArt.Of(P, "................", "................", "....rrrrrrrr....", "..rroooooooorr..", ".roooyyyyyyooor.", ".royyGGGGGGyyor.", "royGGbbbbbbGGyor", "royGbbPPPPbbGyor", "royGbP....PbGyor", "royGbP....PbGyor", "royGbP....PbGyor", "................", "................", "................", "................", "................");

	public static readonly PixelArt Sunny = PixelArt.Of(P, ".......y........", "...y...y...y....", "....y.....y.....", "......kkkk......", ".....kyyyyk.....", "....kyYYyyyk....", "yy.kyYyyyyyyk.yy", "...kyyyyyyyyk...", "...kyyyyyyyok...", "....kyyyyyok....", ".....kyoook.....", "......kkkk......", "....y.....y.....", "...y...y...y....", ".......y........", "................");

	public static readonly PixelArt Cloudy = PixelArt.Of(P, "................", "................", "..........kkk...", ".........kWWWk..", "...kkkk.kWWWWWk.", "..kWWWWkWWWWWWk.", ".kWWWWWWWWWWWWk.", "kWWWWWWWWWWWWWWk", "kWWWWWWWWWWWggWk", "kggWWWWWWggggggk", ".kkkkkkkkkkkkkk.", "................", "................", "................", "................", "................");

	public static readonly PixelArt Foggy = PixelArt.Of(P, "................", "................", "..mmmmmmmmmm....", "................", "....mmmmmmmmmmm.", "................", ".mmmmmmmmmmmm...", "................", "...mmmmmmmmmmmm.", "................", "..mmmmmmmmmm....", "................", "................", "................", "................", "................");

	public static readonly PixelArt Rainy = PixelArt.Of(P, "................", "......kkkk......", ".....kmmmmkk....", "...kkmmmmmmmk...", "..kmmmmmmmmmmk..", ".kmmmmmmmmmmmmk.", "kmmmmmmmmmmmmmmk", "kzmmmmmmmmmmmmzk", ".kzzzzzzzzzzzzk.", "..kkkkkkkkkkkk..", "...u...u...u....", "..u...u...u.....", "................", "....u...u...u...", "...u...u...u....", "................");

	public static readonly PixelArt Stormy = PixelArt.Of(P, "................", "......kkkk......", ".....kzzzzkk....", "...kkzzzzzzzk...", "..kzzzzzzzzzzk..", ".kzzzzzzzzzzzzk.", "kzzzzzzzzzzzzzzk", "knzzzzzzzzzzzznk", ".knnnnnkknnnnnk.", "..kkkkkyykkkkk..", "......yyk.......", ".....yyyyyk.....", ".......yyk......", "......yyk.......", "......yk........", "................");

	public static readonly PixelArt Agenda = PixelArt.Of(P, "................", "....kkkkkkkk....", "...kYYYYYYYYk...", "..kkkkkkkkkkkk..", ".kWWWWWWWWWWWWk.", ".kWbbWkkkkkkWWk.", ".kWbbWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWGGWkkkkkkWWk.", ".kWGGWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWooWkkkkWWWWk.", ".kWooWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelArt Horoscope = PixelArt.Of(P, "................", "......kkkk......", "....kkPPPPk.....", "...kPPPkk.......", "..kPPPk.....y...", "..kPPk.....yYy..", ".kPPPk......y...", ".kPPPk..........", ".kPPPk..........", ".kPPPPk.....y...", "..kPPPPk...yYy..", "..kPPPPPkk..y...", "...kPPPPPPkkk...", "....kkPPPPPPk...", "......kkkkkk....", "................");

	public static readonly PixelArt Telescope = PixelArt.Of(P, "................", "..........kkk...", "........kkWWBk..", "......kkWWWBBk..", "....kkWWWWBBk...", "..kkWWWWWBBk....", ".kBkWWWWBBk.....", ".kBBkWWBBk......", "..kBBkBBk.......", "...kkBkk........", "....kCkk........", "....kCkCk.......", "...kC.kC.k......", "..kC..kC..k.....", ".kk..kkkk..kk...", "................");

	public static readonly PixelArt Board = PixelArt.Of(P, "................", ".kkkkkkkkkkkkkk.", ".kccccccccccccck", ".kcWWWWcrcYYYck.", ".kcWkkWcccYkYck.", ".kcWWWWcccYYYck.", ".kcWkkWcccYkYck.", ".kcWWWWcccYYYck.", ".kcccccccccccck.", ".kcccbcccWWWWck.", ".kcsssscccWkkWck", ".kcskkscccWWWck.", ".kcsssscccWkkck.", ".kcccccccccccck.", ".kkkkkkkkkkkkkk.", "................");

	public static readonly PixelArt Quote = PixelArt.Of(P, "................", "...kkkkkkkkkk...", "..kWWWWWWWWWWk..", ".kWWWkkkkWWWWWk.", ".kWWkWWWWkWWWWk.", ".kWWWWWWkWWWWWk.", ".kWWWWWkWWWWWWk.", ".kWWWWWkWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWWWWWkWWWWWWk.", "..kWWWWWWWWWWk..", "...kkkkkWWkkk...", ".......kWk......", "......kWk.......", "......kk........", "................");

	public static readonly PixelArt Junimo = PixelArt.Of(P, "................", "......kk........", ".....kGGk.......", "......kk........", "....kkkkkk......", "...kGGGGGGk.....", "..kGGGGGGGGk....", "..kGGwkGwkGk....", "..kGGkkGkkGk....", "..kGGGGGGGGk....", "..kGGGqqGGGk....", "...kGGGGGGk.....", "....kGGGGk......", "...kGk..kGk.....", "...kk....kk.....", "................");

	/// <summary>评价里的空星。</summary>
	public static readonly PixelArt StarEmpty = PixelArt.Of(P, "................", "........k.......", ".......kWk......", ".......kWk......", "......kWWWk.....", "..kkkkkWWWWkkkk.", "..kWWWWWWWWWWgk.", "...kWWWWWWWWgk..", "....kWWWWWWgk...", "....kWWWWWWgk...", "...kWWWgkWWWgk..", "...kWgkk.kkWgk..", "..kkk.....kkk...", "................");

	public static PixelArt Sky(MoodSky sky) => sky switch
	{
		MoodSky.Rainbow => Rainbow,
		MoodSky.Sunny => Sunny,
		MoodSky.PartlyCloudy => Mood,
		MoodSky.Foggy => Foggy,
		MoodSky.Rainy => Rainy,
		MoodSky.Stormy => Stormy,
		_ => Cloudy
	};
}
