using System.Threading;
using AISV.Core.Diagnostics;
using Xunit;

namespace AISV.Core.Tests;

public class StepTimerTests
{
	[Fact]
	public void SummaryListsSlowestFirst()
	{
		StepTimer timer = new StepTimer("启动");
		using (timer.Step("快"))
		{
		}
		using (timer.Step("慢"))
		{
			Thread.Sleep(20);
		}
		string summary = timer.Summary();
		Assert.StartsWith("启动 共 ", summary);
		Assert.True(summary.IndexOf("慢") < summary.IndexOf("快"), summary);
		Assert.Equal(2, timer.Steps.Count);
	}
}
