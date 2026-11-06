using System;

namespace AISV.Core.Looks;

/// <summary>被炸到以后脸上身上的烟灰：按帧生成同一组不规则斑块，每帧位置一致，表情和走路帧切换时不会跳。</summary>
public static class Soot
{
	public const byte Red = 104;

	public const byte Green = 96;

	public const byte Blue = 90;

	/// <summary>整张图每个像素的烟灰浓度（0～1）。frame 是一帧的宽高（立绘 64×64，行走图通常 16×32）。</summary>
	public static float[] Mask(int width, int height, int frameWidth, int frameHeight, int seed)
	{
		float[] mask = new float[Math.Max(0, width * height)];
		if (width <= 0 || height <= 0 || frameWidth <= 0 || frameHeight <= 0)
		{
			return mask;
		}
		(float X, float Y, float R)[] blobs = Blobs(frameWidth, frameHeight, seed);
		float[] frame = new float[frameWidth * frameHeight];
		for (int y = 0; y < frameHeight; y++)
		{
			for (int x = 0; x < frameWidth; x++)
			{
				float best = 0f;
				foreach ((float bx, float by, float r) in blobs)
				{
					float d = MathF.Sqrt((x + 0.5f - bx) * (x + 0.5f - bx) + (y + 0.5f - by) * (y + 0.5f - by));
					float edge = r * (0.7f + 0.6f * Hash(x, y, seed));
					if (d < edge)
					{
						best = MathF.Max(best, d < edge * 0.55f ? 1f : 0.6f);
					}
				}
				if (best == 0f && Hash(x * 7 + 3, y * 5 + 1, seed) > 0.965f)
				{
					best = 0.5f;
				}
				frame[y * frameWidth + x] = best;
			}
		}
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				mask[y * width + x] = frame[(y % frameHeight) * frameWidth + x % frameWidth];
			}
		}
		return mask;
	}

	/// <summary>把一个像素按浓度熏黑。深色的描边不动，整体再暗一点。</summary>
	public static (byte R, byte G, byte B) Apply(byte r, byte g, byte b, float strength)
	{
		float dim = 0.88f;
		float rr = r * dim;
		float gg = g * dim;
		float bb = b * dim;
		float lum = (r * 3 + g * 6 + b) / 10f;
		if (strength > 0f && lum > 70f)
		{
			float t = strength * 0.7f;
			rr += (Red - rr) * t;
			gg += (Green - gg) * t;
			bb += (Blue - bb) * t;
		}
		return (Clamp(rr), Clamp(gg), Clamp(bb));
	}

	public static int Seed(string name)
	{
		unchecked
		{
			int h = 17;
			foreach (char c in name.ToLowerInvariant())
			{
				h = h * 31 + c;
			}
			return h;
		}
	}

	private static (float X, float Y, float R)[] Blobs(int w, int h, int seed)
	{
		bool portrait = w >= 32;
		int count = portrait ? 5 : 3;
		float minR = portrait ? w * 0.06f : 1.4f;
		float maxR = portrait ? w * 0.13f : 2.4f;
		(float, float, float)[] blobs = new (float, float, float)[count];
		for (int i = 0; i < count; i++)
		{
			float x = w * (0.25f + 0.5f * Hash(i, 11, seed));
			float y = portrait ? h * (0.2f + 0.6f * Hash(i, 23, seed)) : h * (0.25f + 0.5f * Hash(i, 23, seed));
			float r = minR + (maxR - minR) * Hash(i, 37, seed);
			blobs[i] = (x, y, r);
		}
		return blobs;
	}

	private static float Hash(int x, int y, int seed)
	{
		unchecked
		{
			uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
			h = (h ^ (h >> 13)) * 1274126177u;
			h ^= h >> 16;
			return (h & 0xFFFFFF) / (float)0x1000000;
		}
	}

	private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
}
