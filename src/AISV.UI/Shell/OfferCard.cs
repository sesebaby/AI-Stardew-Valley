using System;
using AISV.Game.Companions;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>聊天里村民约你、说来帮工的那张卡片："好 / 改天吧"。</summary>
internal static class OfferCard
{
	private const int ButtonH = 40;

	/// <summary>画在 (x, y)，返回卡片底边。</summary>
	public static int Draw(SpriteBatch b, ShellContext ctx, string channel, int x, int y, int maxWidth, Hits hits, Rectangle clip, Action<string> notice)
	{
		OfferDesk? desk = ctx.Offers;
		if (desk?.For(channel) is not ChatOffer offer)
		{
			return y;
		}
		int w = Math.Min(maxWidth, 460);
		string text = Hearth.Wrap(ShellText.Clean(OfferDesk.OfferLabel(offer)), w - 32);
		int lh = Hearth.LineHeight();
		Rectangle r = new Rectangle(x, y, w, text.Split('\n').Length * lh + 36 + ButtonH);
		Hearth.Bordered(b, r, Hearth.AmberPale, Hearth.Amber, 12);
		Hearth.Ink(b, text, new Vector2(r.X + 16, r.Y + 12), Hearth.Quill);
		int bw = (w - 32 - 10) / 2;
		Rectangle yes = new Rectangle(r.X + 16, r.Bottom - 12 - ButtonH, bw, ButtonH);
		Rectangle no = new Rectangle(yes.Right + 10, yes.Y, bw, ButtonH);
		Hearth.PressPlate(b, yes, OfferDesk.OfferYesText(offer), primary: true, hover: Hearth.Hover(yes, clip), scale: 0.8f);
		Hearth.PressPlate(b, no, MeetupService.NoText, hover: Hearth.Hover(no, clip), scale: 0.8f);
		hits.Add(yes, () =>
		{
			string said = desk.Confirm(channel, out bool arranged);
			Game1.playSound(arranged ? "newArtifact" : "cancel");
			notice(said);
		}, clip);
		hits.Add(no, () =>
		{
			Game1.playSound("smallSelect");
			notice(desk.Decline(channel));
		}, clip);
		return r.Bottom;
	}
}
