using System;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Dialogue;
using AISV.Core.Text;
using AISV.Game;
using AISV.Game.Dialogue;
using AISV.Game.Social;
using StardewValley;
using StardewValley.Characters;

namespace AISV.UI.Dialogue;

/// <summary>
/// 和自己的孩子聊几句：孩子先说一句 → 回复框（没有保密、表白、约出去这些）→ 孩子接一句 → 还有轮数就再开回复框。
/// 不走村民的那套：不打分、不加好感、不进八卦和日记，只记在孩子自己的本子里。
/// </summary>
public sealed class ChildConversation
{
	private readonly Func<ModConfig> _config;

	private readonly ChildDialogue _engine;

	private readonly ChildTalk _talk;

	private readonly ILog _log;

	private int _remaining;

	private CancellationTokenSource? _cts;

	public ChildConversation(Func<ModConfig> config, ChildDialogue engine, ChildTalk talk, ILog log)
	{
		_config = config;
		_engine = engine;
		_talk = talk;
		_log = log;
	}

	public void Start(Child child)
	{
		ChildContext ctx = _talk.Context(child);
		child.doEmote(40);
		_remaining = Math.Max(1, _config().RepliesPerTalk);
		Task.Run(async () =>
		{
			string? line = null;
			try
			{
				line = await _engine.OpeningAsync(ctx).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_log.Debug($"孩子 {child.Name} 的开场没生成：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (string.IsNullOrWhiteSpace(line) || Game1.activeClickableMenu != null || child.currentLocation != Game1.currentLocation)
				{
					return;
				}
				_engine.Book.Add(ctx.Key, ctx.Day, "child", line!);
				if (Hints.Tip("ChildCardHint", Tr.Get("help.kids.title", "和孩子聊天")))
				{
					_log.Debug("第一次和孩子聊天，提示过角色卡。");
				}
				Open(child, ctx, line!);
			});
		});
	}

	private void Open(Child child, ChildContext ctx, string line)
	{
		int hearts = Game1.player.friendshipData.TryGetValue(child.Name, out Friendship? f) ? f.Points / 250 : 0;
		ReplyMenu menu = new ReplyMenu(child, ChildPrompts.Called(ctx), line, null, multiTurn: _remaining < _config().RepliesPerTalk, hearts, kid: true);
		menu.Submitted += (text, _, _) => Submit(child, ctx, menu, line, text);
		menu.ProcessingCancelled += () => _cts?.Cancel();
		Game1.activeClickableMenu = menu;
	}

	private void Submit(Child child, ChildContext ctx, ReplyMenu menu, string lastLine, string text)
	{
		_cts?.Dispose();
		CancellationTokenSource cts = _cts = new CancellationTokenSource();
		CancellationToken token = cts.Token;
		Task.Run(async () =>
		{
			string? reply = null;
			try
			{
				reply = await _engine.ReplyAsync(ctx, lastLine, text, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_log.Debug($"孩子 {child.Name} 没接上话：{ex.Message}");
			}
			MainThread.Run(() =>
			{
				if (token.IsCancellationRequested)
				{
					return;
				}
				if (string.IsNullOrWhiteSpace(reply))
				{
					if (Game1.activeClickableMenu == menu)
					{
						menu.ShowFailure(Tr.Get("child.noReply", "孩子歪着头看你，好像没听懂。"));
					}
					return;
				}
				_engine.Book.Add(ctx.Key, ctx.Day, "parent", text);
				_engine.Book.Add(ctx.Key, ctx.Day, "child", reply!);
				_remaining--;
				if (Game1.activeClickableMenu == menu)
				{
					menu.CloseMenu();
				}
				if (_remaining > 0)
				{
					Open(child, ctx, reply!);
				}
				else
				{
					Game1.DrawDialogue(new StardewValley.Dialogue(child, null, reply));
				}
			});
		});
	}
}
