using AISV.Core.Looks;
using Xunit;

namespace AISV.Core.Tests;

public class SootTests
{
	[Fact]
	public void EveryFrameGetsTheSamePattern()
	{
		float[] mask = Soot.Mask(64, 64, 16, 32, Soot.Seed("Abigail"));
		for (int y = 0; y < 32; y++)
		{
			for (int x = 0; x < 16; x++)
			{
				Assert.Equal(mask[y * 64 + x], mask[(y + 32) * 64 + x + 48]);
			}
		}
		Assert.Contains(mask, v => v > 0f);
	}

	[Fact]
	public void SameNameSameSmudges()
	{
		Assert.Equal(Soot.Mask(64, 64, 64, 64, Soot.Seed("Sam")), Soot.Mask(64, 64, 64, 64, Soot.Seed("sam")));
		Assert.NotEqual(Soot.Mask(64, 64, 64, 64, Soot.Seed("Sam")), Soot.Mask(64, 64, 64, 64, Soot.Seed("Leah")));
	}

	[Fact]
	public void OutlinesStayDarkAndSkinTurnsGrey()
	{
		(byte r, byte g, byte b) = Soot.Apply(20, 10, 10, 1f);
		Assert.True(r <= 20 && g <= 10 && b <= 10);
		(byte sr, byte sg, byte sb) = Soot.Apply(250, 210, 180, 1f);
		Assert.True(sr < 180 && sg < 160 && sb < 150);
		Assert.True(sr - sb < 40);
	}

	[Fact]
	public void BadSizesGiveAnEmptyMask()
	{
		Assert.Empty(Soot.Mask(0, 10, 16, 32, 1));
		Assert.All(Soot.Mask(4, 4, 0, 0, 1), v => Assert.Equal(0f, v));
	}
}
