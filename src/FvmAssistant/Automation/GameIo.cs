using System;
using System.Drawing;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant.Automation
{
    /// <summary>Everything the automation needs from the game, in stage coordinates.</summary>
    internal interface IGameIo
    {
        StageFrame Grab();
        Task ClickAsync(Point stage, MouseButtonType button = MouseButtonType.Left);
        void Move(Point stage);

        /// <summary>Types digits/ASCII letters or Backspace into the focused game field.</summary>
        Task TypeAsync(string text);

        /// <summary>Reloads the game page (recovers a stuck loader or a lost connection).</summary>
        void Reload();

        /// <summary>When the page last started loading — also after reloads the browser made on its own.</summary>
        DateTime LastPageLoadUtc { get; }

        /// <summary>Asks the browser for a fresh frame (after the captured page stopped changing).</summary>
        void Repaint();
    }

    /// <summary>Reads frames from the off-screen buffer and injects mouse events through CEF.</summary>
    internal sealed class OsrGameIo : IGameIo
    {
        private readonly OsrView _view;
        private readonly Rectangle _stage;

        public OsrGameIo(OsrView view, Rectangle stage)
        {
            _view = view;
            _stage = stage;
        }

        private IBrowserHost Host => _view.BrowserOrNull?.GetHost() ?? throw new InvalidOperationException("browser is not ready");

        public StageFrame Grab()
        {
            using (var full = _view.CaptureFrame() ?? throw new InvalidOperationException("no frame rendered yet"))
                return StageFrame.FromBitmap(full, _stage);
        }

        public async Task ClickAsync(Point stage, MouseButtonType button = MouseButtonType.Left)
        {
            int x = _stage.X + stage.X, y = _stage.Y + stage.Y;
            var host = Host;
            _view.MarkAutomationCursor(new Point(x, y), click: true);
            var pressed = button == MouseButtonType.Right ? CefEventFlags.RightMouseButton : CefEventFlags.LeftMouseButton;
            host.SendMouseMoveEvent(x, y, false, CefEventFlags.None);
            await Task.Delay(30).ConfigureAwait(false);
            host.SendMouseClickEvent(x, y, button, false, 1, pressed);
            await Task.Delay(60).ConfigureAwait(false);
            host.SendMouseClickEvent(x, y, button, true, 1, CefEventFlags.None);
        }

        public void Move(Point stage)
        {
            Host.SendMouseMoveEvent(_stage.X + stage.X, _stage.Y + stage.Y, false, CefEventFlags.None);
            _view.MarkAutomationCursor(new Point(_stage.X + stage.X, _stage.Y + stage.Y), click: false);
        }

        public async Task TypeAsync(string text)
        {
            var host = Host;
            // Key events only reach the page while the browser believes it has focus; in off-screen
            // mode that is CEF's own flag and does not take the system focus from anyone.
            host.SendFocusEvent(true);
            foreach (char ch in text)
            {
                int vk = ch == '\b' ? 8 : char.ToUpperInvariant(ch);
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = vk });
                if (ch != '\b') host.SendKeyEvent(new KeyEvent { Type = KeyEventType.Char, WindowsKeyCode = ch });
                await Task.Delay(40).ConfigureAwait(false);
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = vk });
                await Task.Delay(40).ConfigureAwait(false);
            }
        }

        public void Reload() => _view.WebBrowser.Reload();

        public DateTime LastPageLoadUtc => _view.LastPageLoadUtc;

        public void Repaint() => Host?.Invalidate(PaintElementType.View);
    }
}
