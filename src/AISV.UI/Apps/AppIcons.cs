using System.Collections.Generic;
using AISV.Core.Apps;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;

namespace AISV.UI.Apps;

/// <summary>应用的 16×16 像素图标和心情预报的天气图。</summary>
internal static class AppIcons
{
	private static readonly Dictionary<char, Color> P = PixelGlyph.Palette(('l', new Color(178, 220, 128)), ('q', new Color(255, 196, 214)), ('h', new Color(150, 200, 240)), ('e', new Color(250, 246, 236)), ('m', new Color(170, 176, 190)), ('z', new Color(120, 126, 140)), ('u', new Color(110, 160, 230)));

	public static readonly PixelGlyph Messenger = new PixelGlyph(P, "................", "..kkkkkkkkkkkk..", ".kGGGGGGGGGGGGk.", "kGllGGGGGGGGGGGk", "kGlGGGGGGGGGGGGk", "kGGGwwGGwwGGwwGk", "kGGGwwGGwwGGwwGk", "kGGGGGGGGGGGGGGk", "kDGGGGGGGGGGGGDk", ".kDDGGGGGGGGDDk.", "..kkkkGGDkkkkk..", ".....kGDk.......", ".....kDk........", ".....kk.........", "................", "................");

	public static readonly PixelGlyph Where = new PixelGlyph(P, "................", "......kkkk......", ".....kRRRRk.....", "....kRRwwRRk....", "....kRRwwRRk....", "....kRRRRRRk....", ".....kRRRRk.....", "..kkkkkRRkkkkk..", ".kGGGGkRRkuuuuk.", ".kGlGGGkkuuuuuk.", ".kGGGlGGuuuuuuk.", ".kGGGGGuuuuGGGk.", ".kuuuuuuuGGlGGk.", ".kuuuuuGGGGGGGk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelGlyph Thanks = new PixelGlyph(P, "................", "..kkkk....kkkk..", ".kqqqqk..kqqqqk.", "kqqwqqqkkqqqqqqk", "kqwqqqqqqqqqqqqk", "kqqqqqqqqqqqqqqk", "kqqqqqqqqqqqqqqk", ".kqqqqqqqqqqqqk.", "..kqqqqqqqqqqk..", "...kqqqqqqqqk...", "....kqqqqqqk....", ".....kqqqqk.....", "......kqqk......", ".......kk.......", "................", "................");

	public static readonly PixelGlyph Year = new PixelGlyph(P, "..kk........kk..", ".kRRk......kRRk.", "kkkkkkkkkkkkkkkk", "krrrrrrrrrrrrrrk", "krrrrrrrrrrrrrrk", "kkkkkkkkkkkkkkkk", "keeeeeeyyeeeeeek", "keeeeeyyyyeeeeek", "keeyyyyYYyyyyeek", "keeeyyyYYyyyeeek", "keeeeyyyyyyeeeek", "keeeyyyeeyyyeeek", "keeeyyeeeeyyeeek", "keeeeeeeeeeeeeek", "kkkkkkkkkkkkkkkk", "................");

	public static readonly PixelGlyph Web = new PixelGlyph(P, "......kkkk......", ".....kppppk.....", ".....kpqqpk.....", ".....kppppk.....", "......kkkk......", "......c..c......", ".....c....c.....", "....c......c....", "...c........c...", ".kkkk......kkkk.", "kGGGGk....kbbbbk", "kGllGkcccckbhhbk", "kGGGGk....kbbbbk", ".kkkk......kkkk.", "................", "................");

	public static readonly PixelGlyph Gossip = new PixelGlyph(P, "......kkkk......", ".....kGllGk.....", ".....kGGGGk.....", "......kkkk......", ".......cc.......", "..cccccccccccc..", ".kkkk......kkkk.", "kyYYyk....kyYYyk", "kyyyyk....kyyyyk", ".kkkk......kkkk.", "..cc........cc..", "..cc........cc..", ".kkkk......kkkk.", "kooooo....krrrrk", "kooook....krrrrk", ".kkkk......kkkk.");

	public static readonly PixelGlyph Reviews = new PixelGlyph(P, ".......kk.......", "......kyyk......", "......kyyk......", ".....kyYyyk.....", "kkkkkyYyyyykkkkk", "kyyyyyYyyyyyyyok", ".kyyyyyyyyyyyok.", "..kyyyyyyyyyok..", "...kyyyyyyyok...", "...kyyyyyyyok...", "..kyyyyyyyyyok..", "..kyyyykkyyyok..", ".kyyyk....kyyok.", ".kyyk......kyok.", ".kkk........kkk.", "................");

	public static readonly PixelGlyph Mood = new PixelGlyph(P, "......y.........", ".y....y....y....", "..y..kkkk.y.....", "....kyyyyk......", "...kyYYyyyk.....", "yykyYyyyykkkk...", "...kyyyykWWWWk..", "...kyyykWWWWWWk.", "..y.kkkWWWWWWWk.", "..kkkWWWWWWWWWWk", ".kWWWWWWWWWWWWWk", "kWWWWWWWWWWWWWWk", "kgWWWWWWWWWWWWgk", ".kggggggggggggk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelGlyph Pelican = new PixelGlyph(P, "................", ".kkkkkkkkkkkkkk.", "kwwwwwwwwwwwwwwk", "kwkkkkkkkkkkkkwk", "kwkhhhhhhhyyhkwk", "kwkhhhhhhhyyhkwk", "kwkhhhhhhhhhhkwk", "kwkhhlhhhhhhhkwk", "kwklllllhhhllkwk", "kwkGGGGlllllGkwk", "kwkGGGGGGGGGGkwk", "kwkkkkkkkkkkkkwk", "kwwwwwwwwwwppwwk", "kwwwwwwwwwppppwk", ".kkkkkkkkkkkkkk.", "................");

	public static readonly PixelGlyph Rainbow = new PixelGlyph(P, "................", "................", "....rrrrrrrr....", "..rroooooooorr..", ".roooyyyyyyooor.", ".royyGGGGGGyyor.", "royGGbbbbbbGGyor", "royGbbPPPPbbGyor", "royGbP....PbGyor", "royGbP....PbGyor", "royGbP....PbGyor", "................", "................", "................", "................", "................");

	public static readonly PixelGlyph Sunny = new PixelGlyph(P, ".......y........", "...y...y...y....", "....y.....y.....", "......kkkk......", ".....kyyyyk.....", "....kyYYyyyk....", "yy.kyYyyyyyyk.yy", "...kyyyyyyyyk...", "...kyyyyyyyok...", "....kyyyyyok....", ".....kyoook.....", "......kkkk......", "....y.....y.....", "...y...y...y....", ".......y........", "................");

	public static readonly PixelGlyph Cloudy = new PixelGlyph(P, "................", "................", "................", "......kkkk......", ".....kWWWWkk....", "...kkWWWWWWWk...", "..kWWWWWWWWWWk..", ".kWWWWWWWWWWWWk.", "kWWWWWWWWWWWWWWk", "kWWWWWWWWWWWWWWk", "kgWWWWWWWWWWWWgk", ".kggggggggggggk.", "..kkkkkkkkkkkk..", "................", "................", "................");

	public static readonly PixelGlyph Foggy = new PixelGlyph(P, "................", "................", "..mmmmmmmmmm....", "................", "....mmmmmmmmmmm.", "................", ".mmmmmmmmmmmm...", "................", "...mmmmmmmmmmmm.", "................", "..mmmmmmmmmm....", "................", "................", "................", "................", "................");

	public static readonly PixelGlyph Rainy = new PixelGlyph(P, "................", "......kkkk......", ".....kmmmmkk....", "...kkmmmmmmmk...", "..kmmmmmmmmmmk..", ".kmmmmmmmmmmmmk.", "kmmmmmmmmmmmmmmk", "kzmmmmmmmmmmmmzk", ".kzzzzzzzzzzzzk.", "..kkkkkkkkkkkk..", "...u...u...u....", "..u...u...u.....", "................", "....u...u...u...", "...u...u...u....", "................");

	public static readonly PixelGlyph Stormy = new PixelGlyph(P, "................", "......kkkk......", ".....kzzzzkk....", "...kkzzzzzzzk...", "..kzzzzzzzzzzk..", ".kzzzzzzzzzzzzk.", "kzzzzzzzzzzzzzzk", "knzzzzzzzzzzzznk", ".knnnnnkknnnnnk.", "..kkkkkyykkkkk..", "......yyk.......", ".....yyyyyk.....", ".......yyk......", "......yyk.......", "......yk........", "................");

	public static readonly PixelGlyph Agenda = new PixelGlyph(P, "................", "....kkkkkkkk....", "...kYYYYYYYYk...", "..kkkkkkkkkkkk..", ".kWWWWWWWWWWWWk.", ".kWbbWkkkkkkWWk.", ".kWbbWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWGGWkkkkkkWWk.", ".kWGGWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWooWkkkkWWWWk.", ".kWooWWWWWWWWWk.", ".kWWWWWWWWWWWWk.", "..kkkkkkkkkkkk..", "................");

	public static readonly PixelGlyph Horoscope = new PixelGlyph(P, "................", "......kkkk......", "....kkPPPPk.....", "...kPPPkk.......", "..kPPPk.....y...", "..kPPk.....yYy..", ".kPPPk......y...", ".kPPPk..........", ".kPPPk..........", ".kPPPPk.....y...", "..kPPPPk...yYy..", "..kPPPPPkk..y...", "...kPPPPPPkkk...", "....kkPPPPPPk...", "......kkkkkk....", "................");

	public static readonly PixelGlyph Telescope = new PixelGlyph(P, "................", "..........kkk...", "........kkWWBk..", "......kkWWWBBk..", "....kkWWWWBBk...", "..kkWWWWWBBk....", ".kBkWWWWBBk.....", ".kBBkWWBBk......", "..kBBkBBk.......", "...kkBkk........", "....kCkk........", "....kCkCk.......", "...kC.kC.k......", "..kC..kC..k.....", ".kk..kkkk..kk...", "................");

	public static readonly PixelGlyph Board = new PixelGlyph(P, "................", ".kkkkkkkkkkkkkk.", ".kccccccccccccck", ".kcWWWWcrcYYYck.", ".kcWkkWcccYkYck.", ".kcWWWWcccYYYck.", ".kcWkkWcccYkYck.", ".kcWWWWcccYYYck.", ".kcccccccccccck.", ".kcccbcccWWWWck.", ".kcsssscccWkkWck", ".kcskkscccWWWck.", ".kcsssscccWkkck.", ".kcccccccccccck.", ".kkkkkkkkkkkkkk.", "................");

	public static readonly PixelGlyph Quote = new PixelGlyph(P, "................", "...kkkkkkkkkk...", "..kWWWWWWWWWWk..", ".kWWWkkkkWWWWWk.", ".kWWkWWWWkWWWWk.", ".kWWWWWWkWWWWWk.", ".kWWWWWkWWWWWWk.", ".kWWWWWkWWWWWWk.", ".kWWWWWWWWWWWWk.", ".kWWWWWkWWWWWWk.", "..kWWWWWWWWWWk..", "...kkkkkWWkkk...", ".......kWk......", "......kWk.......", "......kk........", "................");

	public static readonly PixelGlyph Junimo = new PixelGlyph(P, "................", "......kk........", ".....kGGk.......", "......kk........", "....kkkkkk......", "...kGGGGGGk.....", "..kGGGGGGGGk....", "..kGGwkGwkGk....", "..kGGkkGkkGk....", "..kGGGGGGGGk....", "..kGGGqqGGGk....", "...kGGGGGGk.....", "....kGGGGk......", "...kGk..kGk.....", "...kk....kk.....", "................");

	/// <summary>评价里的空星。</summary>
	public static readonly PixelGlyph StarEmpty = new PixelGlyph(P, ".......kk.......", "......kggk......", "......kggk......", ".....kgWggk.....", "kkkkkgWggggkkkkk", "kgggggWgggggggnk", ".kgggggggggggnk.", "..kgggggggggnk..", "...kgggggggnk...", "...kgggggggnk...", "..kgggggggggnk..", "..kggggkkgggnk..", ".kgggk....kggnk.", ".kggk......kgnk.", ".kkk........kkk.", "................");

	public static PixelGlyph Sky(MoodSky sky) => sky switch
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
