using System.Collections.Generic;
using System.Linq;
using AISV.Core.Persona;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>角色卡试聊：用还没保存的设定和村民说几句，听听像不像；说过的话不进记忆，也不加好感。</summary>
internal sealed class CardTrialPage : ShellPage
{
	private readonly string _npc;

	private readonly CharacterCard _draft;

	private readonly FieldSet _fields = new FieldSet();

	private readonly TextArea _input;

	private readonly List<(bool Mine, string Text)> _lines = new List<(bool, string)>();

	private bool _waiting;

	public CardTrialPage(ShellContext ctx, string npc, CharacterCard draft) : base(ctx)
	{
		_npc = npc;
		_draft = draft;
		_input = _fields.Add(200, Tr.Get("card.trialPlaceholder", "想对TA说什么……"), singleLine: true);
		_fields.Focus(_input);
	}

	public override string Title => Tr.Get("card.trialTitle", "试聊：{{name}}", ("name", _draft.DisplayName.Length > 0 ? _draft.DisplayName : Ctx.Display(_npc)));

	public override bool CapturesKeys => _fields.Typing;

	public override void Closed() => _fields.BlurAll();

	public override bool KeyPress(Keys key)
	{
		if (key == Keys.Enter && _fields.Typing)
		{
			Send();
			return true;
		}
		return _fields.KeyPress(key);
	}

	private void Send()
	{
		string text = _input.Text.Trim();
		if (text.Length == 0 || _waiting || Ctx.TryChat == null || Ctx.Ai?.AskLoginIfNeeded(playerAsked: true) == true)
		{
			return;
		}
		_lines.Add((true, text));
		_input.Text = "";
		_waiting = true;
		Game1.playSound("smallSelect");
		Ctx.TryChat(_npc, _draft, text, said =>
		{
			_waiting = false;
			_lines.Add((false, said ?? Tr.Get("card.trialFailed", "（AI 没回话，换句话再试试。）")));
		});
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		_fields.Update();
		int w = System.Math.Min(900, area.Width - 48);
		int x = area.Center.X - w / 2;
		Rectangle note = new Rectangle(x, area.Y + 16, w, 52);
		AisvTheme.FillRounded(b, note, AisvTheme.GoldSoft, 12);
		AisvTheme.DrawText(b, AisvTheme.Truncate(Tr.Get("card.trialNote", "用还没保存的设定试几句。这里说的话不会被记住，也不影响好感。"), w - 40, 0.72f), new Vector2(note.X + 20, note.Y + 14), AisvTheme.TextMuted, 0.72f);
		Rectangle bar = new Rectangle(x, area.Bottom - 76, w, 60);
		Rectangle chat = new Rectangle(x, note.Bottom + 14, w, bar.Y - note.Bottom - 28);
		AisvTheme.DrawCard(b, chat);
		AisvTheme.BeginClip(b, chat);
		List<(bool Mine, List<string> Lines)> bubbles = _lines.Select(l => (l.Mine, AisvTheme.WrapLines(ShellText.Clean(l.Text), w * 3 / 5, 0.82f))).ToList();
		if (_waiting)
		{
			bubbles.Add((false, new List<string> { Tr.Get("card.trialWaiting", "……") }));
		}
		int lh = AisvTheme.LineHeight(0.82f);
		int y = chat.Bottom - 16;
		for (int i = bubbles.Count - 1; i >= 0 && y > chat.Y; i--)
		{
			(bool mine, List<string> lines) = bubbles[i];
			int bw = (int)lines.Max(l => AisvTheme.Width(l, 0.82f)) + 32;
			int bh = lines.Count * lh + 22;
			y -= bh;
			Rectangle bubble = mine ? new Rectangle(chat.Right - 20 - bw, y, bw, bh) : new Rectangle(chat.X + 76, y, bw, bh);
			if (!mine)
			{
				Kit.Avatar(b, new Rectangle(chat.X + 16, y, 48, 48), Ctx, _npc);
			}
			AisvTheme.FillRounded(b, bubble, mine ? AisvTheme.AccentSoft : AisvTheme.PageDeep, 14);
			int ty = bubble.Y + 11;
			foreach (string line in lines)
			{
				AisvTheme.DrawText(b, line, new Vector2(bubble.X + 16, ty), AisvTheme.Text, 0.82f);
				ty += lh;
			}
			y -= 14;
		}
		AisvTheme.EndClip(b);
		Rectangle field = new Rectangle(bar.X, bar.Y + 4, bar.Width - 140, 52);
		_input.Bounds = field;
		_input.Draw(b);
		hits.Add(field, () => _fields.Focus(_input));
		Rectangle send = new Rectangle(bar.Right - 124, bar.Y + 4, 124, 52);
		AisvTheme.Button(b, send, Tr.Get("card.trialSend", "说"), primary: true, hover: AisvTheme.Hover(send), disabled: _waiting);
		hits.Add(send, Send);
	}
}
