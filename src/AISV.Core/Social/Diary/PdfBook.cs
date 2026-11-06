using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AISV.Core.Social.Diary;

/// <summary>把一页页图片装订成 PDF。图片加进来时就压缩，不用把整本书的像素都留在内存里。</summary>
public sealed class PdfBook
{
	private const int PageWidthPoints = 420;

	private readonly List<(int Width, int Height, byte[] Pixels)> _pages = new List<(int, int, byte[])>();

	public int PageCount => _pages.Count;

	/// <param name="rgb">从上到下、每像素 3 字节。</param>
	public void AddPage(byte[] rgb, int width, int height)
	{
		if (width <= 0 || height <= 0 || rgb.Length != width * height * 3)
		{
			throw new ArgumentException("图片大小不对");
		}
		using MemoryStream packed = new MemoryStream();
		using (ZLibStream zlib = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
		{
			zlib.Write(rgb, 0, rgb.Length);
		}
		_pages.Add((width, height, packed.ToArray()));
	}

	public void Save(Stream output, string title)
	{
		if (_pages.Count == 0)
		{
			throw new InvalidOperationException("没有页面");
		}
		// 编号依次是：目录、页面树、所有页面、所有图片、所有绘制指令，最后是书名。
		int count = _pages.Count;
		int PageId(int i) => 3 + i;
		int ImageId(int i) => 3 + count + i;
		int DrawId(int i) => 3 + count * 2 + i;
		int infoId = 3 + count * 3;
		List<byte[]> objects = new List<byte[]>
		{
			Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
			Ascii($"<< /Type /Pages /Count {count} /Kids [{string.Join(" ", Enumerable.Range(0, count).Select(i => $"{PageId(i)} 0 R"))}] >>")
		};
		long PageHeight(int i) => (long)PageWidthPoints * _pages[i].Height / _pages[i].Width;
		objects.AddRange(Enumerable.Range(0, count).Select(i => Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidthPoints} {PageHeight(i)}] /Resources << /XObject << /Picture {ImageId(i)} 0 R >> >> /Contents {DrawId(i)} 0 R >>")));
		objects.AddRange(_pages.Select(p => StreamObject($"/Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode", p.Pixels)));
		objects.AddRange(Enumerable.Range(0, count).Select(i => StreamObject("", Ascii($"q {PageWidthPoints} 0 0 {PageHeight(i)} 0 0 cm /Picture Do Q"))));
		objects.Add(Ascii($"<< /Title <FEFF{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title))}> >>"));
		Stream sink = output;
		long written = 0;
		void Put(byte[] bytes)
		{
			sink.Write(bytes, 0, bytes.Length);
			written += bytes.Length;
		}
		Put(Ascii("%PDF-1.5\n"));
		List<long> starts = new List<long>();
		for (int i = 0; i < objects.Count; i++)
		{
			starts.Add(written);
			Put(Ascii($"{i + 1} 0 obj\n"));
			Put(objects[i]);
			Put(Ascii("\nendobj\n"));
		}
		long table = written;
		StringBuilder xref = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
		foreach (long start in starts)
		{
			xref.Append(start.ToString("D10")).Append(" 00000 n \n");
		}
		xref.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info {infoId} 0 R >>\nstartxref\n{table}\n%%EOF\n");
		Put(Ascii(xref.ToString()));
	}

	private static byte[] StreamObject(string dictionary, byte[] data)
	{
		using MemoryStream all = new MemoryStream();
		byte[] head = Ascii($"<< {dictionary} /Length {data.Length} >>\nstream\n");
		all.Write(head, 0, head.Length);
		all.Write(data, 0, data.Length);
		byte[] tail = Ascii("\nendstream");
		all.Write(tail, 0, tail.Length);
		return all.ToArray();
	}

	private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
