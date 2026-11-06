using System;
using System.Threading.Tasks;
using AISV.Core.Ai;
using AISV.Core.Text;
using AISV.Game.Dialogue;
using AISV.UI.Dialogue;
using StardewValley;

namespace AISV.UI.Shell;

/// <summary>发一句很短的话试试 AI 通不通；结果回到主线程。</summary>
internal static class ConnectionTest
{
	public static void Run(AiService ai, Action<bool, string> done)
	{
		Task.Run(async () =>
		{
			string text;
			bool ok;
			try
			{
				AiResult r = await ai.SendAsync(new ChatRequest("connection_test", "这是一次连通测试。", "只回一个字：好", 64, 0.1));
				ok = r.Ok;
				text = ok ? Tr.Get("gateway.testOk", "通了，模型回了：{{text}}", ("text", (r.Text ?? "").Trim())) : FailureText.Describe(r.Failure, r.Message);
			}
			catch (Exception ex)
			{
				ok = false;
				text = ex.Message;
			}
			MainThread.Run(() =>
			{
				done(ok, text);
				Game1.playSound(ok ? "newArtifact" : "cancel");
			});
		});
	}
}
