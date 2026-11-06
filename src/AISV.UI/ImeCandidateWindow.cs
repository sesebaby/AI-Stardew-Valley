using System;
using System.Runtime.InteropServices;
using AISV.Core.Config;
using AISV.Core.Diagnostics;
using AISV.Core.Text;
using AISV.UI.Dialogue;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace AISV.UI;

/// <summary>
/// 让输入法自己画候选框（Windows）。游戏的 SDL2 默认接管输入法界面却不画，打拼音时看不到候选字；
/// 环境变量 SDL_IME_SHOW_UI=1 交还给输入法，但只在启动时读，所以写进当前用户的环境变量，下次启动生效。
/// 生效后挂一层窗口过程，把候选框的消息直接交给 Windows；每 10 帧把正在打字的输入框位置告诉 SDL。
/// </summary>
public sealed class ImeCandidateWindow
{
	public const string Variable = "SDL_IME_SHOW_UI";

	[StructLayout(LayoutKind.Sequential)]
	private struct SdlRect
	{
		public int X;

		public int Y;

		public int W;

		public int H;
	}

	[DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
	private static extern void SDL_SetTextInputRect(ref SdlRect rect);

	private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

	[DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
	private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "SendMessageW")]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
	private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	private const int GwlpWndProc = -4;

	private const uint WmImeSetContext = 0x0281;

	private const uint WmImeNotify = 0x0282;

	private const long IscShowUiAll = 0xC000000F;

	private const int ImnChangeCandidate = 0x0003;

	private const int ImnCloseCandidate = 0x0004;

	private const int ImnOpenCandidate = 0x0005;

	// 窗口过程的委托要一直引用着，否则被回收。
	private static WndProc? _hook;

	private static IntPtr _prevProc;

	private readonly Func<ModConfig> _config;

	private readonly Action<ModConfig> _save;

	private readonly ILog _log;

	private Rectangle _lastRect = Rectangle.Empty;

	private bool _broken;

	private bool _hookFailed;

	public ImeCandidateWindow(Func<ModConfig> config, Action<ModConfig> save, ILog log)
	{
		_config = config;
		_save = save;
		_log = log;
	}

	/// <summary>这次启动已经带着开关。</summary>
	public static bool ActiveNow => Environment.GetEnvironmentVariable(Variable) == "1";

	/// <summary>启动时按设置写上或删掉用户环境变量（只删自己写的）。</summary>
	public void Apply()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		ModConfig c = _config();
		bool before = c.ImeVarIsOurs;
		try
		{
			SetVariable(c, c.ImeCandidatesVisible);
			if (c.ImeVarIsOurs != before)
			{
				_save(c);
				_log.Info(c.ImeVarIsOurs
					? $"写上了当前用户的环境变量 {Variable}=1，下次启动游戏时输入法候选框就能显示（从 Steam 启动的话 Steam 也要重开）。"
					: $"输入法候选框关了，删掉了当前用户的环境变量 {Variable}。");
			}
		}
		catch (Exception ex)
		{
			_log.Warn($"改不了当前用户的环境变量 {Variable}：{ex.Message}");
		}
		_log.Debug($"输入法候选框：设置{(c.ImeCandidatesVisible ? "开" : "关")}，这次启动{(ActiveNow ? "已生效" : "未生效")}。");
	}

	/// <summary>写上或删掉用户环境变量（设置页切开关时也调用，下次启动生效）。</summary>
	public static void SetVariable(ModConfig c, bool on)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			string? user = Environment.GetEnvironmentVariable(Variable, EnvironmentVariableTarget.User);
			if (on && user != "1")
			{
				Environment.SetEnvironmentVariable(Variable, "1", EnvironmentVariableTarget.User);
				c.ImeVarIsOurs = true;
			}
			else if (!on && user == "1" && c.ImeVarIsOurs)
			{
				Environment.SetEnvironmentVariable(Variable, null, EnvironmentVariableTarget.User);
				c.ImeVarIsOurs = false;
			}
		}
		catch
		{
		}
	}

	/// <summary>设置页开关下面那句。</summary>
	public static string StatusText(ModConfig c)
	{
		if (!OperatingSystem.IsWindows())
		{
			return "";
		}
		if (c.ImeCandidatesVisible)
		{
			return ActiveNow
				? Tr.Get("settings.imeActive", "已生效：打拼音时候选框会出现在输入框旁边。窗口模式或无边框窗口模式最稳；独占全屏时候选框可能被游戏挡住。")
				: Tr.Get("settings.imeRestart", "重启游戏后生效（从 Steam 启动的话，先完全退出 Steam 再开）。");
		}
		return ActiveNow ? Tr.Get("settings.imeOffRestart", "已关掉，重启游戏后不再显示候选框。") : "";
	}

	/// <summary>SDL 会截下候选框的打开、切换、关闭通知自己画（画不出来），这几条直接交给 Windows，别的照常给 SDL。</summary>
	private void HookWindow()
	{
		if (_hook != null || _hookFailed)
		{
			return;
		}
		try
		{
			IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			_hook = Hook;
			_prevProc = SetWindowLongPtr(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_hook));
			if (_prevProc == IntPtr.Zero)
			{
				_hook = null;
				_hookFailed = true;
				_log.Debug("输入法候选框：没挂上游戏窗口。");
				return;
			}
			SendMessage(hwnd, WmImeSetContext, new IntPtr(1), new IntPtr(IscShowUiAll));
			_log.Debug("输入法候选框：挂上游戏窗口了。");
		}
		catch (Exception ex)
		{
			_hookFailed = true;
			_log.Debug("输入法候选框：没挂上游戏窗口：" + ex.Message);
		}
	}

	private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WmImeSetContext && wParam != IntPtr.Zero)
		{
			return DefWindowProc(hWnd, msg, wParam, new IntPtr(lParam.ToInt64() | IscShowUiAll));
		}
		if (msg == WmImeNotify && wParam.ToInt32() is ImnOpenCandidate or ImnChangeCandidate or ImnCloseCandidate)
		{
			return DefWindowProc(hWnd, msg, wParam, lParam);
		}
		return CallWindowProc(_prevProc, hWnd, msg, wParam, lParam);
	}

	public void OnUpdateTicked(uint ticks)
	{
		if (_broken || ticks % 10 != 0 || !OperatingSystem.IsWindows() || !_config().ImeCandidatesVisible)
		{
			return;
		}
		HookWindow();
		if (CurrentBox() is not Rectangle box)
		{
			return;
		}
		float scale = Game1.options.uiScale;
		Rectangle px = new Rectangle((int)(box.X * scale), (int)(box.Y * scale), Math.Max(1, (int)(box.Width * scale)), Math.Max(1, (int)(box.Height * scale)));
		if (px == _lastRect)
		{
			return;
		}
		_lastRect = px;
		try
		{
			SdlRect r = new SdlRect { X = px.X, Y = px.Y, W = px.Width, H = px.Height };
			SDL_SetTextInputRect(ref r);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			_broken = true;
			_log.Debug("输入法候选框：没法告诉 SDL 输入框在哪：" + ex.Message);
		}
	}

	/// <summary>正在打字的输入框（界面坐标）；认不出的用鼠标位置。</summary>
	private static Rectangle? CurrentBox() => Game1.keyboardDispatcher?.Subscriber switch
	{
		null => null,
		TextArea a => a.Bounds,
		TextBox t => new Rectangle(t.X, t.Y, t.Width, t.Height),
		_ => new Rectangle(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true), 200, 48)
	};
}
