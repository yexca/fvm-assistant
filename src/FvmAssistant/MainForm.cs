using System;
using System.Drawing;
using System.Windows.Forms;
using CefSharp;

namespace FvmAssistant
{
    internal sealed class MainForm : Form
    {
        private readonly AssistantOptions _options;
        private readonly string _flashVersion;
        private readonly ToolStripTextBox _address;
        private readonly ToolStripStatusLabel _status;
        private readonly ToolStripButton _recordButton;
        private readonly ToolStripButton _stopGrindButton;
        private readonly ToolStrip _toolbar;
        private Panel.GrindPanel _panel;
        private Point? _onscreenLocation;
        private bool _recording;
        private bool _grinding;

        public MainForm(AssistantOptions options, string flashVersion)
        {
            _options = options;
            _flashVersion = flashVersion;
            AutoScaleMode = AutoScaleMode.None; // the process is DPI aware; keep sizes in real pixels
            Font = new Font("Microsoft YaHei UI", 9f);

            Hooks = new BrowserHooks(autoDialogs: options.Mode == ViewMode.Offscreen);
            View = options.Mode == ViewMode.Offscreen
                ? (IGameView)new OsrView(options.Url, options.Viewport)
                : new WindowedView(options.Url);
            Hooks.Attach(View);

            var toolbar = _toolbar = new ClickThroughToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            toolbar.Items.Add(new ToolStripButton("←", null, (s, e) => View.WebBrowser.Back()) { ToolTipText = "后退" });
            toolbar.Items.Add(new ToolStripButton("→", null, (s, e) => View.WebBrowser.Forward()) { ToolTipText = "前进" });
            toolbar.Items.Add(new ToolStripButton("刷新", null, (s, e) => View.WebBrowser.Reload()));
            toolbar.Items.Add(new ToolStripButton("首页", null, (s, e) => View.WebBrowser.Load(_options.Url)));
            _address = new ToolStripTextBox { AutoSize = false, Width = 640 };
            _address.KeyDown += OnAddressKeyDown;
            toolbar.Items.Add(_address);
            toolbar.Items.Add(new ToolStripButton("DevTools", null, (s, e) => View.WebBrowser.ShowDevTools()));
            _recordButton = new ToolStripButton("● 录制", null, OnRecordClick)
            {
                Enabled = options.Mode == ViewMode.Offscreen,
                ToolTipText = "记录你的鼠标点击和画面（不记录键盘输入），仅离屏模式",
            };
            toolbar.Items.Add(_recordButton);
            _stopGrindButton = new ToolStripButton("■ 停止刷图", null, (s, e) => StopGrind?.Invoke())
            {
                Enabled = false,
                ForeColor = Color.Red,
                ToolTipText = "刷图期间游戏画面不响应你的鼠标键盘，用这个按钮停止",
            };
            toolbar.Items.Add(_stopGrindButton);

            var statusStrip = new StatusStrip();
            _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.Add(_status);

            // Docking runs in reverse z-order: add the fill control first so the bars claim their edges.
            View.Control.Dock = DockStyle.Fill;
            Controls.Add(View.Control);
            Controls.Add(toolbar);
            Controls.Add(statusStrip);
            ClientSize = new Size(options.Viewport.Width,
                options.Viewport.Height + toolbar.PreferredSize.Height + statusStrip.PreferredSize.Height);

            View.AddressChanged += (s, e) => OnUi(() => _address.Text = e.Address);
            View.TitleChanged += (s, e) => OnUi(UpdateTitle);
            SetStatus("就绪");
            UpdateTitle();
        }

        public IGameView View { get; }
        public BrowserHooks Hooks { get; }

        /// <summary>Starts or stops recording; returns a status line. Supplied by the control API.</summary>
        public Func<System.Threading.Tasks.Task<string>> ToggleRecording { get; set; }

        /// <summary>Stops a running grind.</summary>
        public Action StopGrind { get; set; }

        /// <summary>Adds the operation panel (off-screen mode only) and a toolbar toggle for it.</summary>
        public void AttachGrind(GrindController grind, string panelSettingsPath)
        {
            StopGrind = grind.Stop;
            if (_options.Mode != ViewMode.Offscreen) return;

            _panel = new Panel.GrindPanel(grind, panelSettingsPath, () => ToggleRecording?.Invoke() ?? System.Threading.Tasks.Task.FromResult("录制不可用"))
            {
                Dock = DockStyle.Right,
                Width = 340,
            };
            RecordingChanged += _panel.ShowRecording;
            Controls.Add(_panel); // added last, so it is docked first and gets the full right edge
            ClientSize = new Size(ClientSize.Width + _panel.Width, ClientSize.Height);

            var toggle = new ToolStripButton("面板", null, (s, e) =>
            {
                _panel.Visible = !_panel.Visible;
                Width += _panel.Visible ? _panel.Width : -_panel.Width;
            })
            { CheckOnClick = true, Checked = true, ToolTipText = "显示/隐藏自动刷图面板" };
            _toolbar.Items.Insert(0, toggle);
            _toolbar.Items.Insert(1, new ToolStripSeparator());
        }

        public void SetGrinding(bool grinding) => OnUi(() =>
        {
            _grinding = grinding;
            _stopGrindButton.Enabled = grinding;
            if (View is OsrView osr) osr.SuppressUserInput = grinding;
            HoldPageSize();
            UpdateTitle();
        });

        /// <summary>Grinding and recording use stage coordinates found at their start: keep the page size fixed meanwhile.</summary>
        private void HoldPageSize()
        {
            if (View is OsrView osr) osr.HoldPageSize = _grinding || _recording;
        }

        /// <summary>Raised on the UI thread when recording starts (true) or stops (false).</summary>
        public event Action<bool> RecordingChanged;

        public void SetRecording(bool recording) => OnUi(() =>
        {
            _recording = recording;
            HoldPageSize();
            RecordingChanged?.Invoke(recording);
            _recordButton.Text = recording ? "■ 停止录制" : "● 录制";
            _recordButton.ForeColor = recording ? Color.Red : SystemColors.ControlText;
            UpdateTitle();
        });

        public void SetStatus(string text) =>
            OnUi(() => _status.Text = $"{DateTime.Now:HH:mm:ss}  {text}   |   控制端口 127.0.0.1:{_options.Port} · Flash {_flashVersion}");

        public void MoveOffscreen()
        {
            if (_onscreenLocation == null) _onscreenLocation = Location;
            var virtualScreen = SystemInformation.VirtualScreen;
            Location = new Point(virtualScreen.Left - Width - 200, virtualScreen.Top);
        }

        public void MoveOnscreen()
        {
            if (_onscreenLocation == null) return;
            Location = _onscreenLocation.Value;
            _onscreenLocation = null;
        }

        private void UpdateTitle()
        {
            var mode = _options.Mode == ViewMode.Offscreen ? "离屏" : "窗口";
            Text = $"{(_grinding ? "▶ 刷图中 · " : "")}{(_recording ? "● 录制中 · " : "")}FvmAssistant [{mode}] {_options.Profile} — {Hooks.Title}";
        }

        private async void OnRecordClick(object sender, EventArgs e)
        {
            if (ToggleRecording == null) return;
            _recordButton.Enabled = false;
            try
            {
                SetStatus(await ToggleRecording());
            }
            catch (Exception ex)
            {
                SetStatus("录制失败：" + ex.Message);
            }
            finally
            {
                _recordButton.Enabled = true;
            }
        }

        private void OnAddressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var url = _address.Text.Trim();
            if (!url.Contains("://")) url = "https://" + url;
            if (UrlText.IsWeb(url)) View.WebBrowser.Load(url);
        }

        private void OnUi(Action action)
        {
            if (IsDisposed) return;
            if (!IsHandleCreated || !InvokeRequired)
            {
                action();
                return;
            }
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) View.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A ToolStrip ignores the click that activates its window, so pressing a toolbar button while
    /// another app is in front only brings this window forward (that is how a 停止录制 click got lost).
    /// Turn the activating click into a normal one.
    /// </summary>
    internal sealed class ClickThroughToolStrip : ToolStrip
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_ACTIVATE = 1;
        private const int MA_ACTIVATEANDEAT = 2;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT)
                m.Result = (IntPtr)MA_ACTIVATE;
        }
    }
}
