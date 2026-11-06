using System;
using System.Linq;
using AISV.Core.Apps;
using AISV.Core.Text;
using AISV.UI.Shell;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AISV.UI.Apps;

/// <summary>Junimo 宠物：屋里那只小家伙的样子跟着心情变，喂、摸、陪玩各一天一次；右边是它这些天的事。</summary>
internal sealed class JunimoPage : ShellPage
{
	private static readonly Color[] Tints = { new Color(120, 220, 90), new Color(250, 170, 70), new Color(110, 200, 230), new Color(240, 130, 180), new Color(200, 160, 240), new Color(250, 220, 90) };

	private static readonly Color Wall = new Color(236, 222, 196);

	private static readonly Color Floor = new Color(196, 150, 104);

	private Texture2D? _sprite;

	private string _said = "";

	public JunimoPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("junimo.title", "Junimo 宠物");

	public override void Reload()
	{
		try
		{
			_sprite ??= Game1.content.Load<Texture2D>("Characters\\Junimo");
		}
		catch (Exception ex)
		{
			Ctx.Log.Warn("Junimo 宠物：读不到图：" + ex.Message);
		}
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		JunimoState? s = Ctx.Apps?.Junimo;
		if (s == null)
		{
			Kit.Empty(b, new Rectangle(area.X, area.Y + area.Height / 3, area.Width, 80), Tr.Get("junimo.off", "进存档以后才能看。"), "");
			return;
		}
		bool narrow = area.Width < 900;
		int pad = narrow ? 16 : 28;
		Rectangle room = narrow
			? new Rectangle(area.X + pad, area.Y + 16, area.Width - pad * 2, Math.Min(420, area.Height * 3 / 5))
			: new Rectangle(area.X + pad, area.Y + 20, (area.Width - pad * 3) * 3 / 5, area.Height - 40);
		Rectangle side = narrow
			? new Rectangle(area.X + pad, room.Bottom + 16, area.Width - pad * 2, area.Bottom - room.Bottom - 32)
			: new Rectangle(room.Right + pad, area.Y + 20, area.Right - pad - room.Right - pad, area.Height - 40);
		if (!s.Adopted)
		{
			AisvTheme.DrawCard(b, room);
			AisvTheme.TextCentered(b, Tr.Get("junimo.none", "家里还没有 Junimo。"), new Rectangle(room.X, room.Center.Y - 80, room.Width, 40), AisvTheme.Text, 1f);
			AisvTheme.TextCentered(b, Tr.Get("junimo.noneHint", "有了孩子以后会自己跑来一只，也可以现在就领养。"), new Rectangle(room.X + 20, room.Center.Y - 30, room.Width - 40, 30), AisvTheme.TextMuted, 0.74f);
			Rectangle btn = new Rectangle(room.Center.X - 110, room.Center.Y + 30, 220, 54);
			AisvTheme.Button(b, btn, Tr.Get("junimo.adopt", "领养一只"), primary: true, hover: AisvTheme.Hover(btn));
			hits.Add(btn, () =>
			{
				Ctx.Apps?.Adopt();
				Game1.playSound("junimoMeep1");
			});
			return;
		}
		Room(b, room, s);
		Panel(b, side, s, hits);
	}

	private void Room(SpriteBatch b, Rectangle r, JunimoState s)
	{
		AisvTheme.DrawShadow(b, r, 16, 5);
		AisvTheme.FillRounded(b, r, Wall, 16);
		int floorY = r.Y + r.Height * 2 / 3;
		AisvTheme.Fill(b, new Rectangle(r.X + 8, floorY, r.Width - 16, r.Bottom - floorY - 8), Floor);
		for (int i = 0; i < 6; i++)
		{
			AisvTheme.Fill(b, new Rectangle(r.X + 8, floorY + i * 18, r.Width - 16, 2), Color.Black * 0.06f);
		}
		Rectangle window = new Rectangle(r.X + r.Width / 6, r.Y + 36, r.Width / 4, r.Height / 4);
		AisvTheme.Bordered(b, window, Game1.isDarkOut(Game1.currentLocation) ? new Color(40, 46, 90) : new Color(170, 214, 240), new Color(140, 96, 60), 6, 6);
		AisvTheme.VLine(b, window.Center.X - 2, window.Y, window.Height, new Color(140, 96, 60), 4);
		JunimoMood mood = JunimoRules.Mood(s);
		double t = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
		int scale = Math.Max(4, Math.Min(10, r.Height / 50));
		int size = 16 * scale;
		float bob = mood switch
		{
			JunimoMood.Happy => (float)Math.Abs(Math.Sin(t * 5)) * -size * 0.35f,
			JunimoMood.Calm => (float)Math.Sin(t * 2) * scale,
			_ => 0f
		};
		int frame = mood == JunimoMood.Happy ? (int)(t * 8) % 8 : mood == JunimoMood.Calm ? (int)(t * 2) % 2 : 0;
		Color tint = Tints[Math.Abs(s.Tint) % Tints.Length];
		if (mood is JunimoMood.Lonely or JunimoMood.Curled)
		{
			tint = Color.Lerp(tint, new Color(120, 120, 130), mood == JunimoMood.Curled ? 0.55f : 0.3f);
		}
		int x = r.Center.X - size / 2;
		int y = floorY + 10 - size;
		AisvTheme.FillRounded(b, new Rectangle(x + size / 8, floorY + 4, size * 3 / 4, scale * 3), Color.Black * 0.15f, scale * 2);
		if (_sprite != null)
		{
			Rectangle src = new Rectangle(frame * 16, mood == JunimoMood.Lonely ? 32 : 0, 16, 16);
			if (mood == JunimoMood.Curled)
			{
				int h = size * 3 / 5;
				b.Draw(_sprite, new Rectangle(x + size / 10, floorY + 10 - h, size * 4 / 5, h), src, tint);
			}
			else
			{
				b.Draw(_sprite, new Rectangle(x, y + (int)bob, size, size), src, tint);
			}
		}
		if (mood == JunimoMood.Happy && (int)(t * 2) % 3 == 0)
		{
			AisvTheme.Heart(b, new Rectangle(x + size, y - scale * 4, scale * 4, scale * 4));
		}
		AisvTheme.Bold(b, s.Name, new Vector2(r.X + 28, r.Bottom - 66), Color.White, 1f);
		AisvTheme.DrawText(b, JunimoRules.MoodLabel(mood), new Vector2(r.X + 28, r.Bottom - 34), new Color(255, 246, 226), 0.72f);
	}

	private void Panel(SpriteBatch b, Rectangle r, JunimoState s, Hits hits)
	{
		AisvTheme.DrawCard(b, r);
		int x = r.X + 24, w = r.Width - 48;
		int y = r.Y + 22;
		y = Bar(b, Tr.Get("junimo.full", "吃饱"), s.Fullness, AisvTheme.Gold, x, y, w);
		y = Bar(b, Tr.Get("junimo.joy", "开心"), s.Joy, AisvTheme.Pink, x, y, w);
		int today = AppData.Today;
		(string Act, string Label, bool Done)[] acts =
		{
			("feed", Tr.Get("junimo.feed", "喂"), s.FedDay == today),
			("pet", Tr.Get("junimo.pet", "摸摸"), s.PettedDay == today),
			("play", Tr.Get("junimo.play", "陪它玩"), s.PlayedDay == today)
		};
		int bw = (w - 16) / 3;
		for (int i = 0; i < acts.Length; i++)
		{
			Rectangle btn = new Rectangle(x + i * (bw + 8), y, bw, 48);
			(string act, string label, bool done) = acts[i];
			AisvTheme.Button(b, btn, label, primary: !done, hover: !done && AisvTheme.Hover(btn), disabled: done, scale: 0.8f);
			if (!done)
			{
				hits.Add(btn, () =>
				{
					_said = Ctx.Apps?.Act(act) ?? "";
					Game1.playSound("junimoMeep1");
				});
			}
		}
		y += 62;
		if (_said.Length > 0)
		{
			foreach (string line in AisvTheme.WrapLines(_said, w, 0.74f).Take(2))
			{
				AisvTheme.DrawText(b, line, new Vector2(x, y), AisvTheme.AccentDark, 0.74f);
				y += AisvTheme.LineHeight(0.74f);
			}
			y += 8;
		}
		AisvTheme.HLine(b, x, y, w, AisvTheme.Divider, 1);
		y += 14;
		AisvTheme.Bold(b, Tr.Get("junimo.log", "这些天"), new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
		y += 32;
		foreach (JunimoLog e in s.Log)
		{
			int ago = today - e.Day;
			string when = ago <= 0 ? Tr.Get("townLedger.today", "今天") : Tr.Get("townLedger.ago", "{{n}} 天前", ("n", ago));
			var lines = AisvTheme.WrapLines(ShellText.Clean(e.Text), w - 76, 0.7f).Take(2).ToList();
			if (y + lines.Count * AisvTheme.LineHeight(0.7f) > r.Bottom - 16)
			{
				break;
			}
			AisvTheme.DrawText(b, when, new Vector2(x, y), AisvTheme.TextFaint, 0.62f);
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(x + 76, y), AisvTheme.Text, 0.7f);
				y += AisvTheme.LineHeight(0.7f);
			}
			y += 8;
		}
	}

	private static int Bar(SpriteBatch b, string label, int value, Color color, int x, int y, int w)
	{
		AisvTheme.DrawText(b, label, new Vector2(x, y), AisvTheme.TextMuted, 0.74f);
		string v = value.ToString();
		AisvTheme.Bold(b, v, new Vector2(x + w - AisvTheme.Width(v, 0.74f), y), AisvTheme.Text, 0.74f);
		AisvTheme.Progress(b, new Rectangle(x, y + 32, w, 12), value / 100f, color);
		return y + 62;
	}
}
