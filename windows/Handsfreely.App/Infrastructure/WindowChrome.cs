using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace Handsfreely.App.Infrastructure;

public static class WindowChrome
{
    public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcActivate = 0x0086;
    private const int HtClient = 1;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;

    private static readonly List<SubclassProc> Procs = [];

    public static void Apply(Window window, UIElement dragRegion)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(dragRegion);
        if (IsWindows11) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        bool active = true;

        SubclassProc proc = (h, msg, wParam, lParam, id, data) =>
        {
            switch (msg)
            {
                case WmNcCalcSize when wParam != IntPtr.Zero && !IsIconic(h):
                {
                    int top = Marshal.ReadInt32(lParam, 4);
                    var result = DefSubclassProc(h, msg, wParam, lParam);
                    Marshal.WriteInt32(lParam, 4, top + (IsZoomed(h) ? FrameThickness(h) : 0));
                    return result;
                }
                case WmNcActivate:
                {
                    active = wParam != IntPtr.Zero;
                    var result = DefSubclassProc(h, msg, wParam, new IntPtr(-1));
                    InvalidateTopRow(h);
                    return result;
                }
                case WmEraseBkgnd:
                    return new IntPtr(1);
                case WmPaint:
                {
                    var ps = new PaintStruct();
                    var hdc = BeginPaint(h, ref ps);
                    GetClientRect(h, out var client);
                    var row = new Rect { Left = 0, Top = 0, Right = client.Right, Bottom = 1 };
                    var brush = CreateSolidBrush(TopRowColor(h, active));
                    FillRect(hdc, ref row, brush);
                    DeleteObject(brush);
                    EndPaint(h, ref ps);
                    return IntPtr.Zero;
                }
                case WmNcHitTest:
                {
                    var hit = DefSubclassProc(h, msg, wParam, lParam);
                    if (hit.ToInt32() != HtClient || IsZoomed(h)) return hit;
                    var pt = new Point { X = (short)(lParam.ToInt64() & 0xFFFF), Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF) };
                    ScreenToClient(h, ref pt);
                    GetClientRect(h, out var client);
                    int frame = FrameThickness(h);
                    if (pt.Y >= frame) return hit;
                    return new IntPtr(pt.X < frame * 2 ? HtTopLeft : pt.X >= client.Right - frame * 2 ? HtTopRight : HtTop);
                }
            }
            return DefSubclassProc(h, msg, wParam, lParam);
        };
        Procs.Add(proc);
        SetWindowSubclass(hwnd, proc, (UIntPtr)1, IntPtr.Zero);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static void InvalidateTopRow(IntPtr hwnd)
    {
        GetClientRect(hwnd, out var client);
        var row = new Rect { Left = 0, Top = 0, Right = client.Right, Bottom = 1 };
        InvalidateRect(hwnd, ref row, false);
    }

    private static uint TopRowColor(IntPtr hwnd, bool active)
    {
        bool dark = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128;
        int bg = dark ? 44 : 238;
        if (IsZoomed(hwnd)) return Rgb(bg, bg, bg);
        if (active)
        {
            using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("ColorPrevalence") is int prevalence && prevalence == 1
                && DwmGetColorizationColor(out uint argb, out _) >= 0)
                return Rgb((int)(argb >> 16) & 0xFF, (int)(argb >> 8) & 0xFF, (int)argb & 0xFF);
        }
        int alpha = active ? 128 : 85;
        int v = bg * (255 - alpha) / 255;
        return Rgb(v, v, v);
    }

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    private static int FrameThickness(IntPtr hwnd)
    {
        var rc = new Rect();
        uint style = (uint)GetWindowLongPtr(hwnd, -16).ToInt64() & ~WsCaption;
        uint exStyle = (uint)GetWindowLongPtr(hwnd, -20).ToInt64();
        AdjustWindowRectExForDpi(ref rc, style, false, exStyle, GetDpiForWindow(hwnd));
        return -rc.Left;
    }

    private const uint WsCaption = 0x00C00000;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, IntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct ps);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct ps);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, ref Rect rect, bool erase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref Point pt);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint color, out bool opaque);
}
