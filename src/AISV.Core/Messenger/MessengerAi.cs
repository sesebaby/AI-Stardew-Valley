using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Diagnostics;
using AISV.Core.Prompts;

namespace AISV.Core.Messenger;

/// <summary>村民主动发一条消息要的资料。</summary>
public sealed record TextRequest(TextKind Kind, string NpcName, string Personality, string PlayerName, string Relationship, string Situation, string Chat, string Topic, string Feelings, bool CardRewritten = false);

public sealed class MessengerAi
{
	public const string RequestName = "messenger_villager_text";

	private readonly IAiSender _ai;

	private readonly PromptTemplates _templates;

	private readonly Func<string> _language;

	public MessengerAi(IAiSender ai, PromptTemplates templates, Func<string> language)
	{
		_ai = ai;
		_templates = templates;
		_language = language;
	}

	public async Task<string?> VillagerTextAsync(TextRequest r, CancellationToken ct = default)
	{
		string? system = System(r);
		if (system == null)
		{
			return null;
		}
		AiResult result = await _ai.SendAsync(new ChatRequest(RequestName, system, $"{r.NpcName} 拿起手机要给 {r.PlayerName} 发什么？写出这条消息。", 3072, 0.95), ct).ConfigureAwait(false);
		return result.Ok ? result.Text : null;
	}

	public string? System(TextRequest r)
	{
		string? kinds = _templates.Raw("messenger_text_kinds");
		if (_templates.Raw("messenger_text") == null || kinds == null)
		{
			return null;
		}
		Dictionary<string, string?> values = new Dictionary<string, string?>
		{
			["VILLAGER"] = r.NpcName,
			["FARMER_NAME"] = r.PlayerName,
			["NATURE"] = PersonaPrompt.PersonalityBlock(r.NpcName, r.Personality),
			["PLAY_THE_ROLE"] = PersonaPrompt.UsageRules(r.NpcName, r.CardRewritten),
			["BOND"] = r.Relationship,
			["WHATS_UP"] = r.Situation,
			["RECENT_MESSAGES"] = string.IsNullOrWhiteSpace(r.Chat) ? "（信使上还没跟对方聊过。）" : PromptText.Wrap("MESSENGER_CHAT_HISTORY", r.Chat),
			["SUBJECT"] = string.IsNullOrWhiteSpace(r.Topic) ? "" : PromptText.Wrap("TEXT_TOPIC", r.Topic, 800),
			["HOW_THEY_FEEL"] = r.Feelings,
			["LENGTH_RULE"] = r.Kind == TextKind.SeenAndHeard ? "一到三句短句，220 字以内" : "一两句短句，150 字以内",
			["SPEAK_IN"] = _language()
		};
		values["KIND_RULES"] = PromptTemplates.Fill("messenger_text_kinds", KindSection(kinds, VillagerTexts.KindKey(r.Kind)), values, NullLog.Instance);
		return _templates.Fill("messenger_text", values);
	}

	/// <summary>从"## 键"分段的文件里取一段。</summary>
	public static string KindSection(string raw, string key)
	{
		List<string> lines = raw.Replace("\r", "").Split('\n').ToList();
		int start = lines.FindIndex(l => l.Trim() == "## " + key);
		if (start < 0)
		{
			return "";
		}
		int end = lines.FindIndex(start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
		return string.Join("\n", lines.Skip(start + 1).Take((end < 0 ? lines.Count : end) - start - 1)).Trim();
	}

	/// <summary>村民自己过生日那天收到农夫的消息。</summary>
	public static ReplyFrame NpcBirthday(string player) => new ReplyFrame(
		$"【信使·今天你过生日】{player} 在你生日这天发来了消息。\n" +
		"先读一读：对方记得吗？是在祝你生日快乐，还是压根在说别的事？\n" +
		"记得的话，就照你的性子收下这份心意——可能被打动、不好意思、喜出望外、嘴上嫌麻烦心里乐，对人有意思的话还会有点手忙脚乱。" +
		"没提的话，你可以旁敲侧击、开个玩笑、有点小失落，也可以根本不当回事，看你是什么人、和对方多熟；不管怎样，对方说的事还是要接。\n" +
		"交情深浅决定分量：恋人和好朋友记得时会很暖，忘了也更容易失望；不太熟的人记得会让你意外，忘了你也没指望。\n" +
		"可以顺口说说今天怎么过的（有什么安排、收到了什么、谁记得谁忘了），别编大事。要是记录里对方今天已经祝过你了，就别当成头一回。\n" +
		"打分照常：记得一般会让你高兴一点；忘了不算什么大错。",
		"[那天是你的生日，农夫给你发了这条消息。]");

	/// <summary>深夜（凌晨一点以后）收到的消息。</summary>
	public static ReplyFrame LateNight(string player, int time) => new ReplyFrame(
		$"【信使·半夜来的消息】现在是{MessengerRules.ClockText(time)}。除非你本来就是熬夜的人，{player} 这条多半是把你吵醒了，或者正赶上你要睡。\n" +
		"回的时候让人看出你意识到了这个钟点。反应因人而异：有起床气的、迷迷糊糊的、反倒担心起来的、觉得好笑的、夜猫子无所谓的、惦记着明早还要干活的。" +
		"熟人和恋人可能温柔、打趣或者问一句\"出什么事了？\"；不熟的人可能不耐烦、回得特别短。" +
		"要是对方以前也常半夜发，你可能更烦；可要是这条很急、很难过或者很要紧，就先把脾气放一边。\n" +
		"也可以像半睡半醒的人那样，三言两语回一下对方的事。不耐烦可以，打分时也可以扣分，但别凶得不像你。",
		$"[那是半夜{MessengerRules.ClockText(time)}，农夫发来了这条消息。]");
}
