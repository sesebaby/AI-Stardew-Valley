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
		string text = AisvTheme.Wrap(ShellText.Clean(OfferDesk.OfferLabel(offer)), w - 32);
		int lh = AisvTheme.LineHeight();
		Rectangle r = new Rectangle(x, y, w, text.Split('\n').Length * lh + 36 + ButtonH);
		AisvTheme.Bordered(b, r, AisvTheme.GoldSoft, AisvTheme.Gold, 12);
		AisvTheme.DrawText(b, text, new Vector2(r.X + 16, r.Y + 12), AisvTheme.Text);
		int bw = (w - 32 - 10) / 2;
		Rectangle yes = new Rectangle(r.X + 16, r.Bottom - 12 - ButtonH, bw, ButtonH);
		Rectangle no = new Rectangle(yes.Right + 10, yes.Y, bw, ButtonH);
		AisvTheme.Button(b, yes, OfferDesk.OfferYesText(offer), primary: true, hover: AisvTheme.Hover(yes, clip), scale: 0.8f);
		AisvTheme.Button(b, no, MeetupService.NoText, hover: AisvTheme.Hover(no, clip), scale: 0.8f);
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
