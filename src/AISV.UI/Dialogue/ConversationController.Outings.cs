using System;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Dialogue;
using AISV.Core.Outings;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.Game.Outings;
using AISV.Game.Social;
using StardewValley;

namespace AISV.UI.Dialogue;

/// <summary>约出去、约会、反提议、配偶主动邀约。</summary>
public sealed partial class ConversationController
{
	private sealed record PendingCounter(NPC Npc, string Text, bool KeepSecret, OutingDecision Decision, string? CurrentLine, OutingKind Kind, DialogueMaterial Material, string Opening);

	/// <summary>村民改提了去处，等它的反应框关掉后问农夫。</summary>
	private PendingCounter? _counter;

	public OutingService? Outings { get; set; }

	/// <summary>邀约这一轮结束。</summary>
	public event Action<Exchange, OutingTurn>? Outed;

	private void SubmitOuting(NPC npc, ReplyMenu menu, DialogueMaterial material, OutingKind kind, string text, bool keepSecret, string? currentLine, GameDate today, DateTime started, CancellationToken token)
	{
		OutingService outings = Outings!;
		(OutingContext Context, OutingIntent Intent)? prepared = outings.Prepare(npc, kind, text);
		if (prepared == null)
		{
			_log.Info($"约 {npc.Name} 出去：现在没有能演的出游。");
			HandleFailedReply(npc, menu, started);
			return;
		}
		(OutingContext context, OutingIntent intent) = prepared.Value;
		Task.Run(async () =>
		{
			try
			{
				OutingDecision? d = await _engine.OutingAsync(material, context, text, currentLine, today, token).ConfigureAwait(false);
				MainThread.Run(() =>
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (d == null)
					{
						HandleFailedReply(npc, menu, started);
						return;
					}
					if (d.Instead != null && !outings.KeepCounter(npc.Name, d.Instead))
					{
						d.Instead = null;
					}
					if (d.Instead != null)
					{
						OfferCounter(npc, menu, material, kind, text, keepSecret, currentLine, d);
					}
					else
					{
						ApplyOuting(npc, menu, material, kind, text, keepSecret, currentLine, d, intent);
					}
				});
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				_log.Debug($"玩家不等 {npc.Name} 的答复了。");
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					_log.Error($"处理邀约时出错：{ex.Message}");
					MainThread.Run(() => HandleFailedReply(npc, menu, started));
				}
			}
		});
	}

	private void ApplyOuting(NPC npc, ReplyMenu menu, DialogueMaterial material, OutingKind kind, string text, bool keepSecret, string? currentLine, OutingDecision d, OutingIntent intent)
	{
		GameDate today = GameState.Today;
		string opening = _engine.State(npc.Name)?.Line ?? "";
		_engine.CommitOuting(material, kind, text, currentLine, d, today);
		_aiNpcs.Remove(npc.Name);
		OutingRecord r = Outings!.Record(npc, kind, text, d, intent);
		_relationships.React(npc, d.Feeling, d.Warmth);
		menu.ReleaseKeyboard();
		ShowReaction(npc, d.Says, d.Feeling);
		int points = _relationships.ClampInteraction(d.Warmth);
		_relationships.Add(npc.Name, points);
		Notify(npc, kind, text, keepSecret, d, r, currentLine, opening, material.Hearts, Math.Max(3, points * 2), favor: true);
		_engine.CompleteDaily(npc.Name, today);
		ResetReplyState();
		CloseIfActive(menu);
	}

	/// <summary>先显示村民的话（里面已经说了改去哪），反应框关掉后再问农夫。</summary>
	private void OfferCounter(NPC npc, ReplyMenu menu, DialogueMaterial material, OutingKind kind, string text, bool keepSecret, string? currentLine, OutingDecision d)
	{
		_counter = new PendingCounter(npc, text, keepSecret, d, currentLine, kind, material, _engine.State(npc.Name)?.Line ?? "");
		_aiNpcs.Remove(npc.Name);
		_relationships.React(npc, d.Feeling, d.Warmth);
		menu.ReleaseKeyboard();
		ShowReaction(npc, d.Says, d.Feeling);
		ResetReplyState();
		CloseIfActive(menu);
	}

	/// <summary>反应框关掉了：有等着回答的反提议就问农夫。</summary>
	private bool TryAskCounter(NPC? npc)
	{
		PendingCounter? p = _counter;
		if (p == null || npc != null && !string.Equals(npc.Name, p.Npc.Name, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		OutingCounter c = p.Decision.Instead!;
		string place = OutingService.PlaceName(c.Place);
		string question = string.IsNullOrWhiteSpace(c.AskText) ? Tr.Get("outing.counterAsk", "{{npc}}提议改去{{location}}，好吗？", ("npc", _npcs.Names.Display(p.Npc.Name)), ("location", place)) : c.AskText;
		string yes = string.IsNullOrWhiteSpace(c.YesLabel) ? Tr.Get("outing.counterYes", "好，就去{{location}}", ("location", place)) : c.YesLabel;
		string no = string.IsNullOrWhiteSpace(c.NoLabel) ? Tr.Get("outing.counterNo", "这次先不了") : c.NoLabel;
		GameLocation? here = Game1.currentLocation ?? p.Npc.currentLocation;
		if (here == null)
		{
			DeclineCounter(p);
			return true;
		}
		here.createQuestionDialogue(question, new[] { new Response("yes", yes), new Response("no", no) }, (_, answer) =>
		{
			if (answer == "yes")
			{
				AcceptCounter(p);
			}
			else
			{
				DeclineCounter(p);
			}
		});
		return true;
	}

	private void AcceptCounter(PendingCounter p)
	{
		if (_counter != p)
		{
			return;
		}
		_counter = null;
		OutingCounter c = p.Decision.Instead!;
		string memory = Tr.Get("outing.counterMemory", "{{npc}}提议改去{{location}}，农夫答应了。", ("npc", _npcs.Names.Display(p.Npc.Name)), ("location", OutingService.PlaceName(c.Place)));
		RecordCounter(p, OutingAi.AcceptCounter(p.Decision, memory), OutingIntent.At(c.Place));
	}

	private void DeclineCounter(PendingCounter p)
	{
		if (_counter != p)
		{
			return;
		}
		_counter = null;
		OutingDecision d = p.Decision;
		if (!string.IsNullOrWhiteSpace(d.Instead?.IfNo))
		{
			d.Says = d.Instead.IfNo;
		}
		d.Instead = null;
		RecordCounter(p, d, null);
	}

	private void RecordCounter(PendingCounter p, OutingDecision d, OutingIntent? intent)
	{
		GameDate today = GameState.Today;
		_engine.CommitOuting(p.Material, p.Kind, p.Text, p.CurrentLine, d, today);
		OutingRecord r = Outings!.Record(p.Npc, p.Kind, p.Text, d, intent);
		int points = _relationships.ClampInteraction(d.Warmth);
		_relationships.Add(p.Npc.Name, points);
		Notify(p.Npc, p.Kind, p.Text, p.KeepSecret, d, r, p.CurrentLine, p.Opening, p.Material.Hearts, 0, favor: intent != null);
		_engine.CompleteDaily(p.Npc.Name, today);
	}

	/// <summary>开场白是配偶的邀请：不开回复框，直接问去不去。</summary>
	private bool TryAskOffer(NPC npc)
	{
		TodayDialogue? state = _engine.State(npc.Name);
		OutingDecision? offer = state?.OpenOffer;
		if (state == null || offer == null || Outings == null || Game1.currentLocation == null)
		{
			return false;
		}
		_aiNpcs.Remove(npc.Name);
		string yes = Tr.Get("outing.offerYes", "好啊，我很想去！"), no = Tr.Get("outing.offerNo", "今天就不了，谢谢。");
		Game1.currentLocation.createQuestionDialogue(Tr.Get("outing.offerAsk", "{{npc}}约你一起出去，去吗？", ("npc", _npcs.Names.Display(npc.Name))), new[] { new Response("yes", yes), new Response("no", no) },
			(_, answer) => AnswerOffer(npc, state, offer, answer == "yes", answer == "yes" ? yes : no));
		return true;
	}

	private void AnswerOffer(NPC npc, TodayDialogue state, OutingDecision offer, bool yes, string text)
	{
		if (_dayTransition || state.OpenOffer != offer || _engine.State(npc.Name) != state)
		{
			return;
		}
		bool accepted = yes && Outings!.CanAcceptOffer(npc, offer);
		string display = _npcs.Names.Display(npc.Name);
		offer.Agrees = accepted;
		offer.Says = accepted ? Tr.Get("outing.offerAccepted", "太好了！我已经开始盼着那会儿了。") : yes ? Tr.Get("outing.offerUnavailable", "计划有变……这次的出去就改天吧。") : Tr.Get("outing.offerDeclined", "没关系，我们改天再找时间。");
		offer.Feeling = accepted ? "happy" : "neutral";
		offer.Warmth = 0;
		offer.BackOffDays = accepted ? _config().OutingRestAfterYes : _config().OutingRestAfterNo;
		offer.ToRemember = accepted ? Tr.Get("outing.offerAcceptedMemory", "{{npc}}主动约农夫出去，农夫答应了；还没去。", ("npc", display))
			: yes ? Tr.Get("outing.offerUnavailableMemory", "{{npc}}约农夫出去，可还没定下来计划就变了。", ("npc", display))
			: Tr.Get("outing.offerDeclinedMemory", "{{npc}}约农夫出去，农夫客气地推掉了。", ("npc", display));
		if (!accepted)
		{
			offer.WorthTelling = false;
		}
		GameDate today = GameState.Today;
		DialogueMaterial material = _materials.Build(npc);
		_engine.CommitOuting(material, OutingKind.SpouseDate, text, state.Line, offer, today);
		OutingRecord r = Outings!.Record(npc, OutingKind.SpouseDate, text, offer, new OutingIntent());
		_engine.CompleteDaily(npc.Name, today);
		Notify(npc, OutingKind.SpouseDate, text, false, offer, r, state.Line, state.Line, material.Hearts, 0, favor: false, milestones: false);
		ResetReplyState();
		DelayedAction.functionAfterDelay(() => ShowReaction(npc, offer.Says, offer.Feeling), 100);
	}

	private void Notify(NPC npc, OutingKind kind, string text, bool keepSecret, OutingDecision d, OutingRecord r, string? currentLine, string opening, int hearts, int mood, bool favor, bool milestones = true)
	{
		string tag = kind == OutingKind.Invite ? "约出去" : "约会";
		string shown = r.Agreed && r.Place.Length > 0 ? $"{d.Says}（答应了，去{OutingService.PlaceName(r.Place)}：{r.OutingId}。）"
			: !r.Agreed && !string.IsNullOrWhiteSpace(r.Why) ? $"{d.Says}（没答应：{r.Why}）" : d.Says;
		NpcReaction reaction = new NpcReaction { Reaction = d.Says, Emotion = d.Feeling, FriendshipPoints = d.Warmth };
		try
		{
			Outed?.Invoke(new Exchange(npc, $"[{tag}] {text}", shown, keepSecret, reaction, currentLine ?? "", opening, kind == OutingKind.Invite ? "core:invite_out" : "core:spouse_date", hearts, text), new OutingTurn(mood, d.Agrees || d.WorthTelling, favor, milestones));
		}
		catch (Exception ex)
		{
			_log.Warn($"邀约之后的处理出错（{npc.Name}）：{ex.Message}");
		}
	}
}
