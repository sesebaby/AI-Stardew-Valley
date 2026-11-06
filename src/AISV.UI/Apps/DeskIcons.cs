using System.Collections.Generic;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;

namespace AISV.UI.Apps;

/// <summary>手机和电脑桌面上，各个页面的图标。</summary>
internal static class DeskIcons
{
	private static readonly Dictionary<char, Color> P = PixelArt.Inks(('l', new Color(178, 220, 128)), ('q', new Color(255, 196, 214)), ('h', new Color(150, 200, 240)), ('e', new Color(250, 246, 236)), ('m', new Color(170, 176, 190)));

	private static PixelArt G(params string[] rows) => PixelArt.Of(P, rows);

	public static readonly PixelArt Today = G("................", "...y....y....y..", "....y...y...y...", ".......kkk......", ".....kkyyykk....", "...y.kyyyyyk.y..", "....kyyyyyyyk...", "....kyyYYYyyk...", "kkkkkkkkkkkkkkkk", "GGGGlGGGGGGlGGGG", "GGlGGGGGlGGGGGGG", "DDDDDDDDDDDDDDDD", "................", "................", "................", "................");

	public static readonly PixelArt Letter = G("................", "................", ".kkkkkkkkkkkkkk.", ".kekeeeeeeeekek.", ".keekeeeeeekeek.", ".keeekeeeekeeek.", ".keeeekrrkeeeek.", ".keeeekrrkeeeek.", ".keekeeeeeekeek.", ".kekeeeeeeeekek.", ".keeeeeeeeeeeek.", ".kkkkkkkkkkkkkk.", "................", "................", "................", "................");

	public static readonly PixelArt Villagers = G("................", ".....kkkk.......", "....kssssk......", "....kskskk.kkk..", "....kssssk.ksskk", ".....kkkk.kskskk", "...kkbbbbkkksssk", "..kbbbbbbbkkkkk.", "..kbbbbbbbkGGGGk", "..kbkbbbbkbkGGGk", "..kbkbbbbkbkGGGk", "...kkkkkkkkkkkk.", "................", "................", "................", "................");

	public static readonly PixelArt Residents = G("................", "..kkkkkkkkk.....", ".khhhhhhhhhk....", ".khkkhkkhkkhk...", ".khhhhhhhhhk....", "..kkkhkkkkkkkkk.", "....kklllllllllk", "....klkklkklkkll", "....kllllllllllk", ".....kkkkklkkkk.", ".........kk.....", "................", "................", "................", "................", "................");

	public static readonly PixelArt Diary = G("................", "...kkkkkkkkkkk..", "..kCCCCCCCCCCCk.", "..kCsssssssssCk.", "..kCsPPPPPPPsCk.", "..kCsPqqPqqPsCk.", "..kCsPqqqqqPsCk.", "..kCsPPqqqPPsCk.", "..kCsPPPqPPPsCk.", "..kCsPPPPPPPsCk.", "..kCsssssssssCk.", "..kCCCCCCCCCCCk.", "...kkkkkkkrrkk..", "..........rr....", "..........r.....", "................");

	public static readonly PixelArt Gossip = G("................", "...kkkkkkkk.....", "..kqqqqqqqqk....", ".kqqkqqkqqkqk...", ".kqqqqqqqqqqk...", "..kkkqqqqkkk....", "....kqkkkk.kkkk.", "....kk...kYYYYk.", "........kYkYkYYk", "........kYYYYYYk", ".........kkkYkk.", "...........kk...", "................", "................", "................", "................");

	public static readonly PixelArt Arcs = G("................", ".kkkkkkkkkkkkkk.", ".krrrrkyykrrrrk.", ".krRrrkkkkrrRrk.", ".krRrrk..krrRrk.", ".krRrk....krRrk.", ".krRk..yy..kRrk.", ".krk...yy...krk.", ".kk..yyyyyy..kk.", ".k....yyyy....k.", ".kkkkkkkkkkkkkk.", ".kCCCCCCCCCCCCk.", ".kkkkkkkkkkkkkk.", "................", "................", "................");

	public static readonly PixelArt Outings = G("................", "....kkkkkk......", "...krrrrrrk.....", "..krrkkkkrrk....", "..krk....krk....", "..krrk..krrk....", "...krrkkrrk.....", "....krrrrk......", ".....krrk.......", "......kk..GG....", "...GG.....GGGG..", "..GGGGGGGGGGGGG.", ".DDDDDDDDDDDDDDD", "................", "................", "................");

	public static readonly PixelArt Partners = G("................", "...kkk...kkk....", "..krrrk.krrrk...", ".krrwrrkrrrrrk..", ".krwrrrrrrrrrk..", ".krrrrrrrrrrrk..", "..krrrrrrrrrk...", "...krrrrrrrk....", "....krrrrrk.....", ".....krrrk......", "......krk.......", ".......k........", "................", "................", "................", "................");

	public static readonly PixelArt Village = G("................", "..kkkk....kkkk..", ".kqqqqk..khhhhk.", ".kqkqkkkkkhkhhk.", ".kqqqqk..khhhhk.", "..kkkk....kkkk..", "...kk......kk...", "....kk....kk....", ".....kkkkkk.....", "......klllk.....", ".....kllkllk....", ".....kllllllk...", "......kkkkk.....", "................", "................", "................");

	public static readonly PixelArt Market = G("................", "..kkkkkkkkkkkk..", ".krrwwrrwwrrwwk.", ".krrwwrrwwrrwwk.", ".kkkkkkkkkkkkkk.", "..keeeeeeeeeek..", "..keeGGeeyyeek..", "..keeGGeeyyeek..", "..kCCCCCCCCCCk..", "..kCeeCCCCCCCk..", "..kCeeCCCCCCCk..", "..kkkkkkkkkkkk..", "................", "................", "................", "................");

	public static readonly PixelArt Commissions = G("................", ".kkkkkkkkkkkkkk.", ".kCCCCCCCCCCCCk.", ".kCeeeeeekCCCCk.", ".kCekkkeekCeeCk.", ".kCeeeeeekCekCk.", ".kCekkkeekCeeCk.", ".kCeeeeeekCCCCk.", ".kCkkkkkkkCCCCk.", ".kCCCCCCCCCCCCk.", ".kkkkkkkkkkkkkk.", "..kk........kk..", "................", "................", "................", "................");

	public static readonly PixelArt Ledger = G("................", "...kkkkkkkkkk...", "..kGGGGGGGGGGk..", "..kGkkkkkkkkGk..", "..kGkeeeeeekGk..", "..kGkekkkkekGk..", "..kGkeeeeeekGk..", "..kGkekkkkekGk..", "..kGkeeeeeekGk..", "..kGkkkkkkkkGk..", "..kGGGGyyGGGGk..", "...kkkkkkkkkk...", "................", "................", "................", "................");

	public static readonly PixelArt Help = G("................", "...kkk..........", "..k...k...kkkk..", "..k....kkkbbbbk.", ".....kkbbbbbbbk.", "k...kbbbbbbbbbk.", "kk.kbbbbbbbbbbk.", "kbkkbbbWbbbbbbk.", "kbbbbbbWbbbbbbk.", ".kkbbbbbbbbbbk..", "...kbbbbbbbbk...", "....kkkkkkkk....", "................", "................", "................", "................");

	public static readonly PixelArt News = G("................", ".kkkkkkkkkkkkk..", ".keeeeeeeeeeekk.", ".kekkkkkkkkkekek", ".keeeeeeeeeeekek", ".kekkkkekkkkekek", ".kekkkkeeeeeekek", ".kekkkkekkkkekek", ".keeeeeeeeeeekek", ".kekkkkkkkkkekek", ".keeeeeeeeeeekk.", ".kkkkkkkkkkkkk..", "................", "................", "................", "................");

	public static readonly PixelArt Settings = G("................", "..........kkk...", ".........kmmk...", ".........kmk..k.", ".........kmmkkmk", "........kmmmmmmk", ".......kmmmkkkk.", "......kmmmk.....", ".....kmmmk......", "....kmmmk.......", "...kmmmk........", "..kmmmk.........", "..kmmk..........", "..kkk...........", "................", "................");

	public static readonly PixelArt Look = G("................", ".....kkkkkk.....", "...kkeeeeeekk...", "..keerreeGGeek..", ".keeerreeGGeeek.", ".keeeeeeeeeeeek.", ".kyyeeeeeeeeeek.", ".kyyeeeeeekkkk..", ".keeeeeeek......", ".kePPeebbek.....", "..kPPeebbek.....", "...kkeeeekk.....", ".....kkkk.......", "................", "................", "................");

	public static PixelArt Of(ShellMenu.SocialTab tab) => tab switch
	{
		ShellMenu.SocialTab.Messenger => AppIcons.Messenger,
		ShellMenu.SocialTab.Contacts => Villagers,
		ShellMenu.SocialTab.Residents => Residents,
		ShellMenu.SocialTab.Diary => Diary,
		ShellMenu.SocialTab.Gossip => Gossip,
		ShellMenu.SocialTab.Arcs => Arcs,
		ShellMenu.SocialTab.Outings => Outings,
		ShellMenu.SocialTab.Partners => Partners,
		_ => Letter
	};

	public static PixelArt Of(ShellMenu.WorldTab tab) => tab switch
	{
		ShellMenu.WorldTab.Market => Market,
		ShellMenu.WorldTab.Commissions => Commissions,
		ShellMenu.WorldTab.Ledger => Ledger,
		ShellMenu.WorldTab.Help => Help,
		ShellMenu.WorldTab.News => News,
		_ => Village
	};

	public static Color TintOf(ShellMenu.SocialTab tab) => tab switch
	{
		ShellMenu.SocialTab.Messenger => new Color(226, 240, 214),
		ShellMenu.SocialTab.Contacts => new Color(226, 234, 250),
		ShellMenu.SocialTab.Residents => new Color(222, 240, 234),
		ShellMenu.SocialTab.Diary => new Color(240, 230, 250),
		ShellMenu.SocialTab.Gossip => new Color(252, 236, 226),
		ShellMenu.SocialTab.Arcs => new Color(255, 236, 200),
		ShellMenu.SocialTab.Outings => new Color(226, 244, 226),
		ShellMenu.SocialTab.Partners => new Color(252, 226, 232),
		_ => new Color(240, 234, 214)
	};

	public static Color TintOf(ShellMenu.WorldTab tab) => tab switch
	{
		ShellMenu.WorldTab.Market => new Color(250, 230, 206),
		ShellMenu.WorldTab.Commissions => new Color(240, 226, 200),
		ShellMenu.WorldTab.Ledger => new Color(226, 240, 208),
		ShellMenu.WorldTab.Help => new Color(226, 232, 240),
		ShellMenu.WorldTab.News => new Color(240, 240, 232),
		_ => new Color(252, 232, 238)
	};
}
