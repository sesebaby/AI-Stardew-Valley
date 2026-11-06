using System;
using System.Collections.Generic;
using System.Diagnostics;
using AISV.Core.Diagnostics;
using StardewValley;

namespace AISV.Game.Promo;

public enum LinkResult
{
	Opened,
	Copied,
	Failed
}

/// <summary>用系统浏览器打开链接；打不开时复制到剪贴板。</summary>
public static class ExternalLink
{
	/// <summary>不为空时只记下链接、不打开（调试用）。</summary>
	public static List<string>? Captured { get; set; }

	public static LinkResult Open(string url, ILog log)
	{
		if (Captured != null)
		{
			Captured.Add(url);
			return LinkResult.Opened;
		}
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			return LinkResult.Opened;
		}
		catch (Exception ex)
		{
			log.Debug("链接打不开：" + ex.Message);
			try
			{
				return DesktopClipboard.SetText(url) ? LinkResult.Copied : LinkResult.Failed;
			}
			catch
			{
				return LinkResult.Failed;
			}
		}
	}
}
