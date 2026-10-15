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

        public static async Task ClickCefAsync(IBrowserHost host, int x, int y, MouseButtonType button = MouseButtonType.Left)
        {
            MoveCef(host, x, y);
            await Task.Delay(HoverDelayMs).ConfigureAwait(false);
            var pressed = button == MouseButtonType.Right ? CefEventFlags.RightMouseButton : CefEventFlags.LeftMouseButton;
            host.SendMouseClickEvent(x, y, button, false, 1, pressed);
            await Task.Delay(PressDelayMs).ConfigureAwait(false);
            host.SendMouseClickEvent(x, y, button, true, 1, CefEventFlags.None);
        }

        public static async Task DragCefAsync(IBrowserHost host, int fromX, int fromY, int toX, int toY)
        {
            MoveCef(host, fromX, fromY);
            await Task.Delay(HoverDelayMs).ConfigureAwait(false);
            host.SendMouseClickEvent(fromX, fromY, MouseButtonType.Left, false, 1, CefEventFlags.LeftMouseButton);
            int x = fromX, y = fromY;
            try
            {
                await Task.Delay(PressDelayMs).ConfigureAwait(false);
                for (int step = 1; step <= 12; step++)
                {
                    x = (int)Math.Round(fromX + (toX - fromX) * step / 12.0);
                    y = (int)Math.Round(fromY + (toY - fromY) * step / 12.0);
                    host.SendMouseMoveEvent(x, y, false, CefEventFlags.LeftMouseButton);
                    await Task.Delay(25).ConfigureAwait(false);
                }
            }
            finally
            {
                host.SendMouseClickEvent(x, y, MouseButtonType.Left, true, 1, CefEventFlags.None);
            }
        }

        /// <summary>Replace a focused HTML text field using browser key events, without system focus.</summary>
        public static async Task ReplaceCefTextAsync(IBrowserHost host, string text)
        {
            host.SendFocusEvent(true);
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 17, Modifiers = CefEventFlags.ControlDown });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 65, Modifiers = CefEventFlags.ControlDown });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 65, Modifiers = CefEventFlags.ControlDown });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 17 });
            foreach (char ch in text)
            {
                int vk = char.ToUpperInvariant(ch);
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = vk });
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.Char, WindowsKeyCode = ch });
                await Task.Delay(40).ConfigureAwait(false);
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = vk });
            }
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
