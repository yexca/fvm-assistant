using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using CefSharp.OffScreen;

namespace FvmAssistant
{
    internal interface IGameView : IDisposable
    {
        Control Control { get; }
        IWebBrowser WebBrowser { get; }
        IBrowser BrowserOrNull { get; }
        bool IsOffscreen { get; }

        // Declared on the concrete browsers, not on IWebBrowser.
        event EventHandler<AddressChangedEventArgs> AddressChanged;
        event EventHandler<TitleChangedEventArgs> TitleChanged;

        /// <summary>Latest rendered viewport frame. Off-screen mode only.</summary>
        Bitmap CaptureFrame();
    }

    internal sealed class WindowedView : IGameView
    {
        private readonly CefSharp.WinForms.ChromiumWebBrowser _browser;

        public WindowedView(string url)
        {
            _browser = new CefSharp.WinForms.ChromiumWebBrowser(url, null);
        }

        public Control Control => _browser;
        public IWebBrowser WebBrowser => _browser;
        public IBrowser BrowserOrNull => _browser.IsBrowserInitialized ? _browser.GetBrowser() : null;
        public bool IsOffscreen => false;

        public event EventHandler<AddressChangedEventArgs> AddressChanged
        {
            add => _browser.AddressChanged += value;
            remove => _browser.AddressChanged -= value;
        }

        public event EventHandler<TitleChangedEventArgs> TitleChanged
        {
            add => _browser.TitleChanged += value;
            remove => _browser.TitleChanged -= value;
        }

        public Bitmap CaptureFrame() => throw new NotSupportedException("windowed mode has no off-screen frame buffer");

        public void Dispose() => _browser.Dispose();
    }

    /// <summary>
    /// Hosts a windowless CEF browser: frames are painted into memory and drawn by this control,
    /// and user input is forwarded to the browser. Rendering does not depend on this window being visible.
    /// </summary>
    internal sealed class OsrView : Control, IGameView
    {
        private readonly ChromiumWebBrowser _browser;
        private int _repaintQueued;
        private readonly RendererFrameHealth _frameHealth = new RendererFrameHealth();

        // Where automation last put the injected cursor, which the page itself never draws.
        private const int TrailLength = 28;
        private static readonly TimeSpan MarkerVisible = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MarkerFade = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ClickRipple = TimeSpan.FromMilliseconds(450);
        private readonly object _markerLock = new object();
        private readonly Queue<Point> _trail = new Queue<Point>();
        private Point? _marker;
        private DateTime _markerAt;
        private Point _clickAt;
        private DateTime _clickTime = DateTime.MinValue;
        private readonly System.Windows.Forms.Timer _markerTimer = new System.Windows.Forms.Timer { Interval = 50 };

        public OsrView(string url, Size initialSize)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable | ControlStyles.Opaque, true);
            TabStop = true;
            BackColor = Color.Black;
            _browser = new ChromiumWebBrowser(url, new BrowserSettings { WindowlessFrameRate = 30 }, null, true) { Size = initialSize };
            _browser.Paint += OnBrowserPaint;
            _markerTimer.Tick += (s, e) => Invalidate();
            _browser.FrameLoadStart += (s, e) =>
            {
                if (e.Frame.IsMain)
                {
                    LastPageLoadUtc = DateTime.UtcNow;
                    _frameHealth.MainFrameStarted();
                }
            };
            _browser.FrameLoadEnd += (s, e) =>
            {
                if (e.Frame.IsMain) _frameHealth.MainFrameCompleted(e.HttpStatusCode == 0 ||
                    (e.HttpStatusCode >= 200 && e.HttpStatusCode < 400));
            };
        }

        /// <summary>When the page last started loading, whoever reloaded it.</summary>
        public DateTime LastPageLoadUtc { get; private set; } = DateTime.UtcNow;

        public Control Control => this;
        public IWebBrowser WebBrowser => _browser;
        public IBrowser BrowserOrNull => RendererAvailable && _browser.IsBrowserInitialized ? _browser.GetBrowser() : null;
        public bool IsOffscreen => true;

        public event EventHandler<AddressChangedEventArgs> AddressChanged
        {
            add => _browser.AddressChanged += value;
            remove => _browser.AddressChanged -= value;
        }

        public event EventHandler<TitleChangedEventArgs> TitleChanged
        {
            add => _browser.TitleChanged += value;
            remove => _browser.TitleChanged -= value;
        }

        private IBrowserHost Host => BrowserOrNull?.GetHost();

        /// <summary>Raised on the UI thread for the user's own mouse input ("down", "up", "move"), in page coordinates.</summary>
        public event Action<string, MouseEventArgs> UserMouse;

        /// <summary>While automation drives the page, the user's mouse and keyboard are not forwarded.</summary>
        public bool SuppressUserInput { get; set; }

        internal bool RendererAvailable => _frameHealth.Available;
        internal string RendererUnavailableReason => _frameHealth.FailureReason;
        internal void RequireRenderer() => _frameHealth.RequireAvailable();

        internal void RendererTerminated(string reason)
        {
            _frameHealth.Terminated(reason);
            QueueRepaint();
        }

        public Bitmap CaptureFrame()
        {
            RequireRenderer();
            var frame = _browser.ScreenshotOrNull(PopupBlending.Blend);
            try { RequireRenderer(); return frame; }
            catch { frame?.Dispose(); throw; }
        }

        /// <summary>
        /// Records an injected move or click (page pixels) so the view can show where automation is
        /// working. Only drawn on screen: <see cref="CaptureFrame"/> and recognition see the page as is.
        /// </summary>
        public void MarkAutomationCursor(Point page, bool click)
        {
            lock (_markerLock)
            {
                _marker = page;
                _markerAt = DateTime.UtcNow;
                _trail.Enqueue(page);
                while (_trail.Count > TrailLength) _trail.Dequeue();
                if (click)
                {
                    _clickAt = page;
                    _clickTime = DateTime.UtcNow;
                }
            }
            QueueRepaint();
        }

        private void OnBrowserPaint(object sender, OnPaintEventArgs e)
        {
            _frameHealth.Painted(e.IsPopup);
            QueueRepaint();
        }

        private void QueueRepaint()
        {
            // CEF paints up to 30 times a second and automation moves every few ms; coalesce into one pending repaint.
            if (!IsHandleCreated || Interlocked.Exchange(ref _repaintQueued, 1) == 1) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    Interlocked.Exchange(ref _repaintQueued, 0);
                    Invalidate();
                }));
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref _repaintQueued, 0);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!RendererAvailable)
            {
                e.Graphics.Clear(BackColor);
                return;
            }
            using (var frame = _browser.ScreenshotOrNull(PopupBlending.Blend))
            {
                if (frame == null)
                {
                    e.Graphics.Clear(BackColor);
                    return;
                }
                e.Graphics.DrawImageUnscaled(frame, 0, 0);
                using (var brush = new SolidBrush(BackColor))
                {
                    if (frame.Width < ClientSize.Width) e.Graphics.FillRectangle(brush, frame.Width, 0, ClientSize.Width - frame.Width, ClientSize.Height);
                    if (frame.Height < ClientSize.Height) e.Graphics.FillRectangle(brush, 0, frame.Height, frame.Width, ClientSize.Height - frame.Height);
                }
            }
            DrawAutomationCursor(e.Graphics);
        }

        /// <summary>A fading trail of recent moves, a ring with a crosshair at the cursor, and a ripple on clicks.</summary>
        private void DrawAutomationCursor(Graphics g)
        {
            Point[] trail;
            Point marker, clickAt;
            DateTime markerAt, clickTime;
            lock (_markerLock)
            {
                if (_marker == null) return;
                marker = _marker.Value;
                markerAt = _markerAt;
                clickAt = _clickAt;
                clickTime = _clickTime;
                trail = _trail.ToArray();
            }

            var now = DateTime.UtcNow;
            var age = now - markerAt;
            if (age > MarkerVisible)
            {
                _markerTimer.Stop();
                return;
            }
            float fade = age < MarkerVisible - MarkerFade ? 1f : (float)((MarkerVisible - age).TotalMilliseconds / MarkerFade.TotalMilliseconds);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < trail.Length; i++)
            {
                int alpha = (int)(170 * fade * (i + 1) / trail.Length);
                using (var dot = new SolidBrush(Color.FromArgb(alpha, 255, 214, 0)))
                    g.FillEllipse(dot, trail[i].X - 3, trail[i].Y - 3, 6, 6);
            }

            using (var shadow = new Pen(Color.FromArgb((int)(200 * fade), 0, 0, 0), 5))
            using (var ring = new Pen(Color.FromArgb((int)(255 * fade), 255, 60, 60), 2.5f))
            {
                foreach (var pen in new[] { shadow, ring })
                {
                    g.DrawEllipse(pen, marker.X - 11, marker.Y - 11, 22, 22);
                    g.DrawLine(pen, marker.X - 18, marker.Y, marker.X - 6, marker.Y);
                    g.DrawLine(pen, marker.X + 6, marker.Y, marker.X + 18, marker.Y);
                    g.DrawLine(pen, marker.X, marker.Y - 18, marker.X, marker.Y - 6);
                    g.DrawLine(pen, marker.X, marker.Y + 6, marker.X, marker.Y + 18);
                }
            }

            var sinceClick = now - clickTime;
            if (sinceClick < ClickRipple)
            {
                float t = (float)(sinceClick.TotalMilliseconds / ClickRipple.TotalMilliseconds);
                float r = 10 + 24 * t;
                using (var ripple = new Pen(Color.FromArgb((int)(255 * (1 - t)), 80, 220, 255), 3))
                    g.DrawEllipse(ripple, clickAt.X - r, clickAt.Y - r, 2 * r, 2 * r);
            }

            // Keep the fade-out and the ripple going even when the page itself does not repaint.
            if (!_markerTimer.Enabled) _markerTimer.Start();
        }

        /// <summary>
        /// Keeps the page at its current size while the window is resized. Automation and recording
        /// work in stage coordinates found once at their start, and the page centres the game, so a
        /// wider or narrower page would move the stage under them. The view just shows the page
        /// clipped or with a margin until this is released.
        /// </summary>
        public bool HoldPageSize
        {
            get => _holdPageSize;
            set
            {
                _holdPageSize = value;
                if (!value) FitPageToView();
            }
        }

        private bool _holdPageSize;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitPageToView();
            Invalidate();
        }

        private void FitPageToView()
        {
            if (_holdPageSize) return;
            // Minimizing collapses the client area; keep the page at its last real size.
            var form = FindForm();
            if (form != null && form.WindowState == FormWindowState.Minimized) return;
            if (ClientSize.Width < 100 || ClientSize.Height < 100) return;
            if (_browser.Size != ClientSize) _browser.Size = ClientSize;
        }

        private static CefEventFlags Flags(MouseButtons buttons)
        {
            var flags = CefEventFlags.None;
            if ((buttons & MouseButtons.Left) != 0) flags |= CefEventFlags.LeftMouseButton;
            if ((buttons & MouseButtons.Right) != 0) flags |= CefEventFlags.RightMouseButton;
            if ((buttons & MouseButtons.Middle) != 0) flags |= CefEventFlags.MiddleMouseButton;
            var keys = ModifierKeys;
            if ((keys & Keys.Shift) != 0) flags |= CefEventFlags.ShiftDown;
            if ((keys & Keys.Control) != 0) flags |= CefEventFlags.ControlDown;
            if ((keys & Keys.Alt) != 0) flags |= CefEventFlags.AltDown;
            return flags;
        }

        private static MouseButtonType? ToCef(MouseButtons button)
        {
            switch (button)
            {
                case MouseButtons.Left: return MouseButtonType.Left;
                case MouseButtons.Right: return MouseButtonType.Right;
                case MouseButtons.Middle: return MouseButtonType.Middle;
                default: return null;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (SuppressUserInput) return;
            UserMouse?.Invoke("move", e);
            Host?.SendMouseMoveEvent(e.X, e.Y, false, Flags(e.Button));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (SuppressUserInput) return;
            Host?.SendMouseMoveEvent(0, 0, true, Flags(MouseButtons.None));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (SuppressUserInput) return;
            UserMouse?.Invoke("down", e); // before forwarding, so a recorder sees the pre-click frame
            var button = ToCef(e.Button);
            if (button != null) Host?.SendMouseClickEvent(e.X, e.Y, button.Value, false, Math.Max(1, e.Clicks), Flags(e.Button));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (SuppressUserInput) return;
            UserMouse?.Invoke("up", e);
            var button = ToCef(e.Button);
            if (button != null) Host?.SendMouseClickEvent(e.X, e.Y, button.Value, true, 1, Flags(MouseButtons.None));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (SuppressUserInput) return;
            Host?.SendMouseWheelEvent(e.X, e.Y, 0, e.Delta, Flags(MouseButtons.None));
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Host?.SendFocusEvent(true);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Host?.SendFocusEvent(false);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_KEYDOWN:
                case Native.WM_KEYUP:
                case Native.WM_CHAR:
                    if (SuppressUserInput) return;
                    var host = Host;
                    if (host != null)
                    {
                        host.SendKeyEvent(m.Msg, (int)m.WParam.ToInt64(), (int)m.LParam.ToInt64());
                        return;
                    }
                    break;
                case Native.WM_SYSKEYDOWN:
                case Native.WM_SYSKEYUP:
                case Native.WM_SYSCHAR:
                    // Forward, but still let Windows see them so Alt+F4 etc. keep working.
                    Host?.SendKeyEvent(m.Msg, (int)m.WParam.ToInt64(), (int)m.LParam.ToInt64());
                    break;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _markerTimer.Dispose();
                _browser.Paint -= OnBrowserPaint;
                _browser.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
