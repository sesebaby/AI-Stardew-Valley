using AISV.Core.Text;

namespace AISV.Core.Ai;

public enum AiHealthLevel
{
	Good,
	Warning,
	Bad,
	Off
}

public sealed record AiHealthFacts(bool AiOn, bool SetUp, bool Offline, int Succeeded, int Failed, bool? TestOk);

public sealed record AiHealth(AiHealthLevel Level, string Title, string Hint);

/// <summary>现在 AI 好不好用：连接测试的结果优先于今天的统计。</summary>
public static class AiHealthRules
{
	public static AiHealth Evaluate(AiHealthFacts f)
	{
		if (!f.AiOn)
		{
			return new AiHealth(AiHealthLevel.Off, Tr.Get("aistatus.off", "AI 功能关着"), Tr.Get("aistatus.offHint", "村民都说游戏原来的台词。可以在设置的“常用”里打开。"));
		}
		if (!f.SetUp)
		{
			return new AiHealth(AiHealthLevel.Bad, Tr.Get("aistatus.notSetUp", "还没设置好"), Tr.Get("aistatus.notSetUpHint", "用服务器要先登录账号；用自己的网关要填好地址和模型。"));
		}
		if (f.TestOk == false)
		{
			return new AiHealth(AiHealthLevel.Bad, Tr.Get("aistatus.testFailed", "连接测试没通过"), Tr.Get("aistatus.seeFailures", "看看下面最近的失败是什么原因。"));
		}
		if (f.Offline)
		{
			return new AiHealth(AiHealthLevel.Bad, Tr.Get("aistatus.offline", "暂时连不上"), Tr.Get("aistatus.offlineHint", "连续几次都没连上，这会儿村民先说原来的台词，过一分钟会再试。"));
		}
		if (f.Failed > 0 && f.TestOk != true)
		{
			return f.Succeeded > 0
				? new AiHealth(AiHealthLevel.Warning, Tr.Get("aistatus.shaky", "偶尔出错"), Tr.Get("aistatus.seeFailures", "看看下面最近的失败是什么原因。"))
				: new AiHealth(AiHealthLevel.Bad, Tr.Get("aistatus.failing", "今天还没成功过"), Tr.Get("aistatus.failingHint", "点“测试连接”试试，再看看下面的原因。"));
		}
		if (f.Succeeded > 0 || f.TestOk == true)
		{
			return new AiHealth(AiHealthLevel.Good, Tr.Get("aistatus.good", "一切正常"), Tr.Get("aistatus.goodHint", "村民正在用 AI 说话。"));
		}
		return new AiHealth(AiHealthLevel.Good, Tr.Get("aistatus.ready", "准备好了"), Tr.Get("aistatus.readyHint", "今天还没用过。和村民说句话，或者点“测试连接”。"));
	}
}
