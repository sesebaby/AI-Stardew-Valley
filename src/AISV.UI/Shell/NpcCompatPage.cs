using System;
using System.Collections.Generic;
using System.Linq;
using AISV.Core.Compat;
using AISV.Core.Config;
using AISV.Core.Persona;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Theme;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>村民和 NPC mod：左边每位村民用不用 AI，右边看原因、开关这一位，下面选用哪些 mod 的村民。</summary>
internal sealed class NpcCompatPage : ShellPage
{
	private sealed record Entry(string Name, string Display, NPC Npc, string Group, NpcCompatKind Kind, string? Reason, bool Enabled);

	private static readonly NpcModActivationMode[] Scopes = { NpcModActivationMode.VanillaOnly, NpcModActivationMode.VanillaAndSve, NpcModActivationMode.SelectedMods, NpcModActivationMode.AllNpcMods };

	private readonly Scroller _listScroll = new Scroller();

	private readonly Scroller _detailScroll = new Scroller();

	private List<Entry> _entries = new List<Entry>();

	private string? _selected;

	private Rectangle _listView, _detail;

	public NpcCompatPage(ShellContext ctx) : base(ctx)
	{
	}

	public override string Title => Tr.Get("shell.settings.npcCompat", "村民和 NPC mod");

	public override void Reload()
	{
		List<Entry> list = new List<Entry>();
		foreach (NPC npc in Utility.getAllVillagers().Where(n => n != null && n.CanSocialize).GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
		{
			string? reason = Ctx.Npcs.Eligibility.RejectionReason(npc.Name, npc.IsVillager, null, present: true);
			bool enabled = Ctx.Npcs.IsEnabled(npc);
			list.Add(new Entry(npc.Name, Ctx.Display(npc.Name), npc, GroupOf(npc.Name), NpcCompat.Kind(reason, enabled), reason, enabled));
		}
		string builtIn = Tr.Get("compat.group.builtIn", "游戏自带");
		_entries = list.OrderBy(e => e.Group == builtIn ? 0 : 1).ThenBy(e => e.Group, StringComparer.CurrentCulture).ThenBy(e => e.Display, StringComparer.CurrentCulture).ToList();
		if (_selected == null || !_entries.Any(e => e.Name == _selected))
		{
			_selected = _entries.FirstOrDefault()?.Name;
		}
	}

	private string GroupOf(string name)
	{
		if (BuiltInPersonas.HasVanilla(name))
		{
			return Tr.Get("compat.group.builtIn", "游戏自带");
		}
		if (BuiltInPersonas.IsKnownSve(name))
		{
			return "Stardew Valley Expanded";
		}
		string? mod = Ctx.Npcs.Eligibility.OwnerModId(name);
		return mod != null ? Ctx.ModName(mod) : Tr.Get("compat.group.unknown", "来路不明");
	}

	public override void Scroll(int direction)
	{
		if (_listView.Contains(Game1.getMouseX(), Game1.getMouseY()))
		{
			_listScroll.Wheel(direction, 64);
		}
		else
		{
			_detailScroll.Wheel(direction);
		}
	}

	public override bool KeyPress(Keys key) => _detailScroll.PageKeys(key, _detail.Height - 80);

	private void Change(Action<ModConfig> edit)
	{
		Game1.playSound("drumkit6");
		ModConfig c = Ctx.Config();
		edit(c);
		Ctx.SaveConfig(c.Normalize());
		Reload();
	}

	public override void Draw(SpriteBatch b, Rectangle area, Hits hits)
	{
		int listW = Math.Clamp(area.Width * 34 / 100, 320, 440);
		Rectangle panel = new Rectangle(area.X, area.Y, listW, area.Height);
		_detail = new Rectangle(panel.Right, area.Y, area.Right - panel.Right, area.Height);
		DrawList(b, panel, hits);
		DrawDetail(b, hits);
	}

	private void DrawList(SpriteBatch b, Rectangle panel, Hits hits)
	{
		AisvTheme.Fill(b, panel, AisvTheme.Card);
		AisvTheme.VLine(b, panel.Right - 2, panel.Y, panel.Height, AisvTheme.Divider);
		int active = _entries.Count(e => e.Kind == NpcCompatKind.Active);
		AisvTheme.DrawText(b, Tr.Get("compat.summary", "{{total}} 位村民，{{active}} 位用 AI", ("total", _entries.Count), ("active", active)), new Vector2(panel.X + 18, panel.Y + 16), AisvTheme.TextMuted, 0.78f);
		_listView = new Rectangle(panel.X + 6, panel.Y + 50, panel.Width - 12, panel.Height - 56);
		AisvTheme.BeginClip(b, _listView);
		int y = _listView.Y - _listScroll.Offset;
		string group = "";
		const int rowH = 62;
		foreach (Entry e in _entries)
		{
			if (e.Group != group)
			{
				group = e.Group;
				AisvTheme.DrawText(b, AisvTheme.Truncate(group, _listView.Width - 24, 0.72f), new Vector2(_listView.X + 12, y + 8), AisvTheme.TextFaint, 0.72f);
				y += 34;
			}
			Rectangle row = new Rectangle(_listView.X, y, _listView.Width, rowH);
			if (row.Bottom >= _listView.Y && row.Y <= _listView.Bottom)
			{
				DrawRow(b, e, row);
				string name = e.Name;
				hits.Add(row, () =>
				{
					if (_selected != name)
					{
						Game1.playSound("smallSelect");
						_selected = name;
						_detailScroll.Reset();
					}
				}, _listView);
			}
			y += rowH + 2;
		}
		AisvTheme.EndClip(b);
		_listScroll.Finish(b, _listView, y, 6);
	}

	private void DrawRow(SpriteBatch b, Entry e, Rectangle row)
	{
		if (e.Name == _selected)
		{
			AisvTheme.FillRounded(b, row, AisvTheme.SelectedRow, 10);
			AisvTheme.Fill(b, new Rectangle(row.X, row.Y + 8, 4, row.Height - 16), AisvTheme.Accent);
		}
		else if (AisvTheme.Hover(row, _listView))
		{
			AisvTheme.FillRounded(b, row, AisvTheme.PageDeep, 10);
		}
		AisvTheme.Avatar(b, new Rectangle(row.X + 12, row.Center.Y - 22, 44, 44), e.Npc);
		string chip = NpcCompat.KindWord(e.Kind);
		int cw = AisvTheme.ChipWidth(chip, 0.66f);
		AisvTheme.Chip(b, new Vector2(row.Right - 12 - cw, row.Center.Y - 14), chip, ChipFill(e.Kind), e.Kind == NpcCompatKind.Active ? Color.White : AisvTheme.TextMuted, 0.66f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(e.Display, row.Width - 84 - cw - 16, 0.9f), new Vector2(row.X + 68, row.Center.Y - AisvTheme.LineHeight(0.9f) / 2), AisvTheme.Text, 0.9f);
	}

	private static Color ChipFill(NpcCompatKind kind) => kind switch
	{
		NpcCompatKind.Active => AisvTheme.Accent,
		NpcCompatKind.AuthorOptOut => AisvTheme.GoldSoft,
		_ => AisvTheme.PageDeep
	};

	private void DrawDetail(SpriteBatch b, Hits hits)
	{
		AisvTheme.BeginClip(b, _detail);
		int w = Math.Min(_detail.Width - 56, 760), x = _detail.X + (_detail.Width - w) / 2;
		int y = _detail.Y + 20 - _detailScroll.Offset;
		if (_entries.FirstOrDefault(e => e.Name == _selected) is Entry entry)
		{
			y = DrawEntry(b, entry, x, y, w, hits) + 16;
		}
		y = DrawScope(b, x, y, w, hits);
		AisvTheme.EndClip(b);
		_detailScroll.Finish(b, _detail, y + 12);
	}

	private int DrawEntry(SpriteBatch b, Entry e, int x, int y, int w, Hits hits)
	{
		const float scale = 0.8f;
		List<string> why = AisvTheme.WrapLines(NpcCompat.Explain(e.Reason, e.Enabled, e.Display), w - 170, scale);
		string source = Tr.Get("compat.persona", "人设来自：{{where}}", ("where", PersonaWord(Ctx.Npcs.Persona(e.Name, e.Npc).Source)));
		Rectangle card = new Rectangle(x, y, w, Math.Max(148, 60 + why.Count * AisvTheme.LineHeight(scale) + 34) + 76);
		AisvTheme.DrawCard(b, card);
		AisvTheme.Avatar(b, new Rectangle(card.X + 20, card.Y + 20, 108, 108), e.Npc);
		int tx = card.X + 148;
		AisvTheme.Bold(b, AisvTheme.Truncate(e.Display, card.Right - 20 - tx, 1.15f), new Vector2(tx, card.Y + 20), AisvTheme.Text, 1.15f);
		int ty = card.Y + 60;
		foreach (string line in why)
		{
			AisvTheme.DrawText(b, line, new Vector2(tx, ty), e.Kind == NpcCompatKind.Active ? AisvTheme.AccentDark : AisvTheme.TextMuted, scale);
			ty += AisvTheme.LineHeight(scale);
		}
		AisvTheme.DrawText(b, AisvTheme.Truncate(source, card.Right - 20 - tx, 0.72f), new Vector2(tx, ty + 4), AisvTheme.TextFaint, 0.72f);
		Rectangle row = new Rectangle(card.X + 4, card.Bottom - 72, card.Width - 8, 68);
		AisvTheme.HLine(b, row.X + 16, row.Y, row.Width - 32, AisvTheme.Divider);
		bool off = e.Kind == NpcCompatKind.TurnedOff;
		SwitchRow(b, row, Tr.Get("compat.useAi", "让 AI 驱动这位村民"), Tr.Get("compat.useAiNote", "关掉后说游戏原来的台词，也不记忆、不传八卦"), !off, hits, () => Change(c => NpcCompat.SetOff(c, e.Name, !off)));
		return card.Bottom;
	}

	private static string PersonaWord(PersonaSource source) => source switch
	{
		PersonaSource.BuiltIn => Tr.Get("compat.source.builtIn", "内置"),
		PersonaSource.Sve => "Stardew Valley Expanded",
		PersonaSource.ContentPack => Tr.Get("compat.source.pack", "内容包"),
		PersonaSource.Card => Tr.Get("compat.source.card", "你写的角色卡"),
		_ => Tr.Get("compat.source.generated", "按游戏数据生成")
	};

	private int DrawScope(SpriteBatch b, int x, int y, int w, Hits hits)
	{
		ModConfig config = Ctx.Config();
		bool picking = config.ModVillagerScope == NpcModActivationMode.SelectedMods;
		List<string> mods = picking ? Ctx.Npcs.Eligibility.NpcModIds.ToList() : new List<string>();
		const int rowH = 68;
		int rows = 2 + mods.Count + (picking ? 1 : 0);
		Rectangle card = new Rectangle(x, y, w, 56 + rows * rowH + 8);
		AisvTheme.DrawCard(b, card);
		Kit.SectionTitle(b, Tr.Get("compat.scopeTitle", "其他 NPC mod"), x + 20, y + 18);
		int ry = y + 52;
		int index = Math.Max(0, Array.IndexOf(Scopes, config.ModVillagerScope));
		string label = ScopeWord(Scopes[index]);
		AisvTheme.DrawText(b, Tr.Get("compat.scope", "用哪些村民"), new Vector2(x + 24, ry + 20), AisvTheme.Text, 0.86f);
		int bw = (int)AisvTheme.Width(label, 0.8f) + 56;
		Rectangle btn = new Rectangle(card.Right - 20 - bw, ry + 12, bw, 44);
		AisvTheme.Button(b, btn, label, hover: AisvTheme.Hover(btn, _detail), scale: 0.8f);
		AisvTheme.Chevron(b, new Vector2(btn.Right - 18, btn.Center.Y), left: false, AisvTheme.TextFaint);
		hits.Add(btn, () => Change(c => c.ModVillagerScope = Scopes[(index + 1) % Scopes.Length]), _detail);
		ry += rowH;
		foreach (string mod in mods)
		{
			string id = mod;
			bool on = config.ModVillagerPicks?.Contains(id, StringComparer.OrdinalIgnoreCase) == true;
			SwitchRow(b, new Rectangle(x + 4, ry, w - 8, rowH), Ctx.ModName(id), id, on, hits, () => Change(c => NpcCompat.SetPicked(c, id, !on)));
			ry += rowH;
		}
		if (picking)
		{
			bool on = config.UnknownVillagersOn;
			SwitchRow(b, new Rectangle(x + 4, ry, w - 8, rowH), Tr.Get("compat.unknown", "来路不明的村民"), Tr.Get("compat.unknownNote", "查不到来自哪个 mod 的，也用 AI"), on, hits, () => Change(c => c.UnknownVillagersOn = !on));
			ry += rowH;
		}
		bool optOut = config.OptOutListOn;
		SwitchRow(b, new Rectangle(x + 4, ry, w - 8, rowH), Tr.Get("compat.optOut", "遵守 AI 排除名单"), Tr.Get("compat.optOutNote", "社区名单上的 NPC，作者不希望被 AI 驱动"), optOut, hits, () => Change(c => c.OptOutListOn = !optOut));
		return card.Bottom;
	}

	private static string ScopeWord(NpcModActivationMode scope) => scope switch
	{
		NpcModActivationMode.VanillaOnly => Tr.Get("compat.scope.vanilla", "只用游戏自带的"),
		NpcModActivationMode.VanillaAndSve => Tr.Get("compat.scope.sve", "游戏自带和 SVE"),
		NpcModActivationMode.SelectedMods => Tr.Get("compat.scope.picked", "我勾选的 mod"),
		_ => Tr.Get("compat.scope.all", "所有 NPC mod")
	};

	private void SwitchRow(SpriteBatch b, Rectangle r, string label, string note, bool on, Hits hits, Action toggle)
	{
		if (AisvTheme.Hover(r, _detail))
		{
			AisvTheme.FillRounded(b, AisvTheme.Inflate(r, -2), AisvTheme.SelectedRow, 10);
		}
		AisvTheme.DrawText(b, AisvTheme.Truncate(label, r.Width - 120, 0.86f), new Vector2(r.X + 20, r.Y + 10), AisvTheme.Text, 0.86f);
		AisvTheme.DrawText(b, AisvTheme.Truncate(note, r.Width - 120, 0.68f), new Vector2(r.X + 20, r.Y + 38), AisvTheme.TextFaint, 0.68f);
		AisvTheme.Toggle(b, new Rectangle(r.Right - 20 - 64, r.Center.Y - 18, 64, 36), on);
		hits.Add(r, toggle, _detail);
	}
}
