using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace MemoryDesktop
{
    internal static class NativeDesktop
    {
        internal delegate bool EnumWindowProc(IntPtr window, IntPtr state);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        internal static string ClassName(IntPtr window)
        { var name = new StringBuilder(256); GetClassName(window, name, name.Capacity); return name.ToString(); }
        internal static string Describe(IntPtr window)
        {
            Rect rect; GetWindowRect(window, out rect);
            return String.Format("{0:X} class={1} parent={2:X} visible={3} rect={4},{5},{6},{7}", window.ToInt64(), ClassName(window), GetParent(window).ToInt64(), IsWindowVisible(window), rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        internal static IntPtr FindHost(bool create)
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            if (create)
            {
                IntPtr result;
                // Undocumented Explorer message: request a wallpaper WorkerW, never change SPI wallpaper.
                SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 2, 1000, out result);
            }
            // Raised desktops composite the shell wallpaper in WorkerW. Our layered
            // window must be its sibling, between that wallpaper and the icon layer.
            IntPtr modern = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            IntPtr icons = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (UseRaisedDesktopParent(GetWindowLong(progman, -20), icons == IntPtr.Zero ? 0 : GetWindowLong(icons, -20), modern != IntPtr.Zero, icons != IntPtr.Zero))
                return progman;
            IntPtr host = IntPtr.Zero;
            EnumWindows(delegate(IntPtr top, IntPtr state)
            {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                IntPtr next = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                if (next != IntPtr.Zero && FindWindowEx(next, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                { host = next; return false; }
                return true;
            }, IntPtr.Zero);
            return host;
        }

        internal static void Attach(IntPtr window, IntPtr host, System.Drawing.Rectangle bounds)
        {
            if (host == IntPtr.Zero || !IsWindow(host)) throw new InvalidOperationException("Explorer 桌面宿主不可用。");
            ShowWindow(window, 0);
            int style = GetWindowLong(window, -16);
            SetWindowLong(window, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
            int exStyle = GetWindowLong(window, -20);
            bool raised = ClassName(host) == "Progman";
            // The native presenter uses ordinary redirected GDI pixels with opaque alpha.
            if (raised && (exStyle & 0x00080000) == 0) throw new InvalidOperationException("新版桌面需要分层渲染窗口。");
            SetWindowLong(window, -20, exStyle | 0x00000080 | 0x08000000 | 0x00000020);
            if (raised && !SetLayeredWindowAttributes(window, 0, 255, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (GetParent(window) != host) SetParent(window, host);
            if (GetParent(window) != host) throw new InvalidOperationException("无法连接 Explorer 桌面宿主。");
            Position(window, host, bounds);
            ShowWindow(window, 4); // SW_SHOWNOACTIVATE; desktop icons retain focus.
        }

        internal static void Position(IntPtr window, IntPtr host, System.Drawing.Rectangle bounds)
        {
            var origin = new Point { X = bounds.Left, Y = bounds.Top };
            ScreenToClient(host, ref origin);
            bool raised = ClassName(host) == "Progman";
            IntPtr after = raised ? FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null) : new IntPtr(1);
            if (raised && after == IntPtr.Zero) throw new InvalidOperationException("Explorer 图标层不可用。");
            if (!SetWindowPos(window, after, origin.X, origin.Y, bounds.Width, bounds.Height, 0x0010 | 0x0020))
                throw new InvalidOperationException("无法调整桌面壁纸位置。");
            if (raised)
            {
                IntPtr shellWallpaper = FindWindowEx(host, IntPtr.Zero, "WorkerW", null);
                if (shellWallpaper != IntPtr.Zero) SetWindowPos(shellWallpaper, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
        }

        internal static bool IsCovered(Forms.Screen display)
        {
            IntPtr foreground = GetForegroundWindow();
            string cls = ClassName(foreground);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            Rect rect;
            return GetWindowRect(foreground, out rect) && rect.Left <= display.Bounds.Left && rect.Top <= display.Bounds.Top && rect.Right >= display.Bounds.Right && rect.Bottom >= display.Bounds.Bottom;
        }
        internal static bool IsExplorerHost(IntPtr host)
        {
            if (!IsWindow(host)) return false;
            if (host == FindWindow("Progman", null))
            {
                IntPtr icons = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
                return UseRaisedDesktopParent(GetWindowLong(host, -20), icons == IntPtr.Zero ? 0 : GetWindowLong(icons, -20), FindWindowEx(host, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero, icons != IntPtr.Zero);
            }
            if (ClassName(host) != "WorkerW") return false;
            uint hostProcess, shellProcess;
            GetWindowThreadProcessId(host, out hostProcess);
            GetWindowThreadProcessId(FindWindow("Progman", null), out shellProcess);
            return hostProcess != 0 && hostProcess == shellProcess;
        }
        internal static bool UseRaisedDesktopParent(int progmanExStyle, int iconExStyle, bool hasWallpaperChild, bool hasIcons)
        { return hasIcons && hasWallpaperChild && (progmanExStyle & 0x00200000) != 0 && (iconExStyle & 0x00080000) != 0; }
    }

}
