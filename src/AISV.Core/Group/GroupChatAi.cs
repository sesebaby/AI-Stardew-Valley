using System;
using System.Threading;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Diagnostics;

namespace AISV.Core.Group;

public sealed class GroupChatAi
{
	public const string RequestName = "group_conversation";

	public const int Tokens = 3072;

	public const double Temperature = 0.9;

	private readonly IAiSender _ai;

	private readonly Func<string> _language;

	private readonly ILog _log;

	public GroupChatAi(IAiSender ai, Func<string> language, ILog log)
	{
		_ai = ai;
		_language = language;
		_log = log;
	}

	public async Task<GroupTurn?> TurnAsync(GroupRequest request, Func<string, string?>? display, CancellationToken ct = default)
	{
		if (request.Members.Count == 0 || (request.Setting != GroupSetting.Phone && request.Members.Count < 2) || ct.IsCancellationRequested)
		{
			return null;
		}
		ChatRequest chat = new ChatRequest(RequestName, GroupPrompts.System(request, _language()), GroupPrompts.User(request), Tokens, Temperature);
		AiResult r = await _ai.SendAsync(chat, ct).ConfigureAwait(false);
		if (!r.Ok || ct.IsCancellationRequested)
		{
			return null;
		}
		GroupTurn? turn = GroupReply.Parse(r.Text, request.Names, display);
		if (turn == null)
		{
			_log.Warn("群聊回复读不出台词：" + Preview(r.Text));
		}
		return turn;
	}

	private static string Preview(string? text)
	{
		string t = (text ?? "").Replace('\n', ' ').Trim();
		return t.Length <= 200 ? t : t.Substring(0, 200) + "…";
	}
}
