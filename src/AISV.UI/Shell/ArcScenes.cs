using System;
using AISV.Core.Stories;
using AISV.Core.Text;
using AISV.Game.Stories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI.Shell;

/// <summary>小剧场收尾：写结局，然后黑屏标题 → 游戏里演一段 → 黑屏"完"。</summary>
public static class ArcScenes
{
	public static AISV.Core.Diagnostics.ILog Log { get; set; } = AISV.Core.Diagnostics.NullLog.Instance;

	/// <summary>开始写结局。返回为什么没开始（null = 开始了）。</summary>
	internal static string? Conclude(StoryArcService arcs, string arcId, bool playScene)
	{
		string? why = arcs.Conclude(arcId, arc =>
		{
			if (arc == null)
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.failed", "结局没写成（AI 没回话），等会儿再点一次“结束小剧场”。"), HUDMessage.error_type));
			}
			else if (playScene && ConclusionScript.Playable(arc, Game1.player?.Name ?? "").Count > 0)
			{
				Game1.hudMessages.RemoveAll(m => m.message == Writing);
				Play(arc);
			}
			else
			{
				Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.concluded", "小剧场「{{title}}」落幕了。", ("title", arc.Title)), HUDMessage.newQuest_type));
			}
		});
		if (why == null && playScene)
		{
			Game1.exitActiveMenu();
			Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.writing", "正在写结局，写好了就开演。"), HUDMessage.newQuest_type));
		}
		return why;
	}

	private static string Writing => Tr.Get("arcs.writing", "正在写结局，写好了就开演。");

	internal static void Play(StoryArc arc)
	{
		WhenFree(() => Game1.activeClickableMenu = TitleCard.Intro(arc.Title, () =>
		{
			if (!ConclusionScenePlayer.TryStart(arc, () => Outro(arc), Log))
			{
				Outro(arc);
			}
		}));
	}

	private static void Outro(StoryArc arc) => WhenFree(() => Game1.activeClickableMenu = TitleCard.Outro(arc.Title, () =>
		Game1.addHUDMessage(new HUDMessage(Tr.Get("arcs.concluded", "小剧场「{{title}}」落幕了。", ("title", arc.Title)), HUDMessage.newQuest_type))));

	/// <summary>等菜单、事件都没了再做（最多等 24 次）。</summary>
	private static void WhenFree(Action action, int attempts = 0)
	{
		DelayedAction.functionAfterDelay(() =>
		{
			if ((Game1.eventUp || Game1.currentLocation?.currentEvent != null || Game1.activeClickableMenu != null || Game1.dialogueUp) && attempts < 24)
			{
				WhenFree(action, attempts + 1);
			}
			else
			{
				action();
			}
		}, 150);
	}
}

/// <summary>黑底金字的标题卡（开场 1.2 秒、结尾 1.6 秒，过一小会儿可以点掉）。</summary>
internal sealed class TitleCard : IClickableMenu
{
	private const double SkipAfterMs = 350;

	private readonly string _title;

	private readonly string? _subtitle;

	private readonly double _duration;

	private readonly Action? _done;

	private double _elapsed;

	private bool _finished;

	private TitleCard(string title, string? subtitle, double duration, Action? done) : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height)
	{
		_title = string.IsNullOrWhiteSpace(title) ? Tr.Get("arcs.title", "小剧场") : ShellText.Clean(title);
		_subtitle = subtitle;
		_duration = Math.Max(500, duration);
		_done = done;
	}

	public static TitleCard Intro(string title, Action? done) => new TitleCard(title, null, 1200, done);

	public static TitleCard Outro(string title, Action? done) => new TitleCard(title, Tr.Get("arcs.end", "— 完 —"), 1600, done);

	public override void update(GameTime time)
	{
		base.update(time);
		_elapsed += time.ElapsedGameTime.TotalMilliseconds;
		if (_elapsed >= _duration)
		{
			Finish();
		}
	}

	public override void receiveLeftClick(int x, int y, bool playSound = true)
	{
		if (_elapsed >= SkipAfterMs)
		{
			Finish();
		}
	}

	public override void receiveKeyPress(Keys key)
	{
		if (_elapsed >= SkipAfterMs && key is Keys.Enter or Keys.Escape or Keys.Space)
		{
			Finish();
		}
	}

	private void Finish()
	{
		if (_finished)
		{
			return;
		}
		_finished = true;
		if (Game1.activeClickableMenu == this)
		{
			Game1.activeClickableMenu = null;
		}
		else
		{
			exitThisMenu(playSound: false);
		}
		DelayedAction.functionAfterDelay(() => _done?.Invoke(), 50);
	}

	public override void draw(SpriteBatch b)
	{
		int vw = Game1.uiViewport.Width, vh = Game1.uiViewport.Height;
		b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, vw, vh), Color.Black);
		float scale = vw < 900 ? 0.85f : 1f;
		Vector2 size = Game1.dialogueFont.MeasureString(_title) * scale;
		Vector2 pos = new Vector2((vw - size.X) / 2f, (vh - size.Y) / 2f - (_subtitle != null ? 34 : 0));
		b.DrawString(Game1.dialogueFont, _title, pos + new Vector2(3f, 3f), Color.Black * 0.85f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
		b.DrawString(Game1.dialogueFont, _title, pos, Color.Gold, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
		if (_subtitle != null)
		{
			Vector2 sub = Game1.smallFont.MeasureString(_subtitle);
			b.DrawString(Game1.smallFont, _subtitle, new Vector2((vw - sub.X) / 2f, pos.Y + size.Y + 24f), Color.White * 0.8f);
		}
		drawMouse(b);
	}
}
