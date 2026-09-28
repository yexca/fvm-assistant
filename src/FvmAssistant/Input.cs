using System;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant
{
    /// <summary>
    /// Two ways to deliver a click without touching the real cursor:
    /// "cef" injects events through the browser host (works for both view modes),
    /// "post" posts window messages to the render widget (windowed mode only; what 轨道 does to IE).
    /// </summary>
    internal static class Input
    {
        private const int HoverDelayMs = 40;
        private const int PressDelayMs = 80;

        public static void MoveCef(IBrowserHost host, int x, int y) =>
            host.SendMouseMoveEvent(x, y, false, CefEventFlags.None);

        public static async Task ClickCefAsync(IBrowserHost host, int x, int y)
        {
            MoveCef(host, x, y);
            await Task.Delay(HoverDelayMs).ConfigureAwait(false);
            host.SendMouseClickEvent(x, y, MouseButtonType.Left, false, 1, CefEventFlags.LeftMouseButton);
            await Task.Delay(PressDelayMs).ConfigureAwait(false);
            host.SendMouseClickEvent(x, y, MouseButtonType.Left, true, 1, CefEventFlags.None);
        }

        public static void MovePost(IntPtr hwnd, int x, int y) =>
            Native.PostMessage(hwnd, Native.WM_MOUSEMOVE, IntPtr.Zero, Native.MakeLParam(x, y));

        public static async Task ClickPostAsync(IntPtr hwnd, int x, int y)
        {
            var lParam = Native.MakeLParam(x, y);
            MovePost(hwnd, x, y);
            await Task.Delay(HoverDelayMs).ConfigureAwait(false);
            Native.PostMessage(hwnd, Native.WM_LBUTTONDOWN, (IntPtr)Native.MK_LBUTTON, lParam);
            await Task.Delay(PressDelayMs).ConfigureAwait(false);
            Native.PostMessage(hwnd, Native.WM_LBUTTONUP, IntPtr.Zero, lParam);
        }
    }
}
