using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FvmAssistant.Automation;
using FvmAssistant.Panel;

namespace FvmAssistant.Controller
{
    /// <summary>A separate control window that never initializes CEF or examines saved cookies.</summary>
    internal sealed class ControllerForm : Form
    {
        private readonly ControllerState _state;
        private readonly ListBox _accounts = new ListBox();
        private readonly TextBox _profile = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown { Minimum = 1024, Maximum = 65535 };
        private readonly TextBox _server = new TextBox();
        private readonly TextBox _entryUrl = new TextBox();
        private readonly ListBox _cards = new ListBox();
        private readonly TabControl _accountWorkspace = new TabControl { Dock = DockStyle.Fill };
        private readonly TextBox _learn = new TextBox();
        private readonly ListBox _tracks = new ListBox();
        private readonly ComboBox _trackAccount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _trackLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        private readonly CheckBox _showAllTracks = new CheckBox { Text = "Show all stages", AutoSize = true };
        private readonly TrackBoardControl _trackBoard = new TrackBoardControl();
        private readonly Label _trackSummary = new Label { AutoSize = true, ForeColor = Muted };
        private readonly Dictionary<string, Bitmap> _cardIcons = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Bitmap> _trackIcons = new Dictionary<string, Bitmap>();
        private readonly CheckBox _success = new CheckBox { Text = "此轨道已在当前账号和关卡通关", AutoSize = true };
        private readonly ComboBox _queueAccount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ListBox _queue = new ListBox();
        private readonly TextBox _queueLevel = new TextBox();
        private readonly TextBox _queueTrack = new TextBox();
        private readonly TextBox _queueCards = new TextBox();
        private readonly NumericUpDown _queueRounds = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 1 };
        private readonly CheckBox _queueShovel = new CheckBox { Text = "执行铲除", AutoSize = true };
        private readonly CheckBox _queueBoss = new CheckBox { Text = "继续挑战 Boss", AutoSize = true };
        private readonly CheckBox _queueAutoFlame = new CheckBox { Text = "自动拾火", AutoSize = true };
        private readonly ComboBox _host = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _guest = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _duoLevel = new TextBox();
        private readonly TextBox _hostTrack = new TextBox();
        private readonly TextBox _guestTrack = new TextBox();
        private readonly TextBox _hostCards = new TextBox();
        private readonly TextBox _guestCards = new TextBox();
        private readonly NumericUpDown _duoRounds = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 1 };
        private readonly CheckBox _duoBoss = new CheckBox { Text = "继续挑战 Boss", AutoSize = true };
        private readonly CheckBox _duoFriendInvite = new CheckBox { Text = "通过好友邀请组队", AutoSize = true };
        private readonly NumericUpDown _duoArea = new NumericUpDown { Minimum = 1, Maximum = 11, Value = 11 };
        private readonly NumericUpDown _duoFriendRow = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 1 };
        private readonly CheckBox _hostShovel = new CheckBox { Text = "房主铲除", AutoSize = true };
        private readonly CheckBox _guestShovel = new CheckBox { Text = "队友铲除", AutoSize = true };
        private readonly CheckBox _hostAutoFlame = new CheckBox { Text = "房主自动拾火", AutoSize = true };
        private readonly CheckBox _guestAutoFlame = new CheckBox { Text = "队友自动拾火", AutoSize = true };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button _stop = new Button { Text = "停止任务", AutoSize = true, Enabled = false };
        private readonly TabControl _tabs = new TabControl();
        private static readonly Color Ink = Color.FromArgb(33, 43, 59);
        private static readonly Color Muted = Color.FromArgb(103, 115, 132);
        private static readonly Color Surface = Color.White;
        private static readonly Color Canvas = Color.FromArgb(246, 248, 251);
        private static readonly Color Accent = Color.FromArgb(221, 102, 45);
        private Process _coordinator;
        private AccountEntry _activeHost;
        private AccountEntry _activeGuest;
        private bool _loading;
        private bool _refreshingTracks;

        public ControllerForm()
        {
            _state = ControllerState.Load();
            Text = "FvmAssistant · 控制台";
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ForeColor = Ink;
            BackColor = Canvas;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(940, 660);
            Size = new Size(1180, 800);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Canvas };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            var header = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(24, 14, 20, 10) };
            header.Controls.Add(new Label { Text = "美食大战老鼠  /  FvmAssistant", Dock = DockStyle.Top, Height = 30,
                ForeColor = Color.White, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = "Account cards  /  Stage formations  /  Task execution", Dock = DockStyle.Bottom, Height = 25,
                ForeColor = Color.FromArgb(194, 205, 220), Font = new Font(Font.FontFamily, 9f) });
            root.Controls.Add(header, 0, 0);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Margin = new Padding(18, 15, 18, 8);
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.ItemSize = new Size(116, 38);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.DrawItem += DrawTab;
            _tabs.TabPages.Add(Page("账号", BuildAccounts()));
            _tabs.TabPages.Add(Page("轨道", BuildTracks()));
            _tabs.TabPages.Add(Page("关卡队列", BuildQueue()));
            _tabs.TabPages.Add(Page("双人任务", BuildDuo()));
            root.Controls.Add(_tabs, 0, 1);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Margin = new Padding(18, 0, 18, 14), BackColor = Surface, Padding = new Padding(14, 8, 14, 8) };
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Controls.Add(new Label { Text = "运行记录", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            bar.Controls.Add(Button("打开数据目录", () => OpenFolder(AppPaths.DataRoot())), 1, 0);
            StyleButton(_stop, true);
            _stop.EnabledChanged += (s, e) => _stop.BackColor = _stop.Enabled ? Accent : Color.FromArgb(213, 219, 227);
            _stop.BackColor = Color.FromArgb(213, 219, 227);
            _stop.Margin = new Padding(8, 0, 0, 0);
            bar.Controls.Add(_stop, 2, 0);
            _stop.Click += async (s, e) => await StopRunAsync();
            bottom.Controls.Add(bar, 0, 0);
            _log.Dock = DockStyle.Fill;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Color.FromArgb(250, 251, 253);
            _log.Font = new Font("Consolas", 9f);
            bottom.Controls.Add(_log, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);
            RefreshAccounts();
            RefreshTracks();
            FormClosing += (s, e) => { if (_coordinator != null && !_coordinator.HasExited) e.Cancel = true; };
            Log("控制台已就绪。打开游戏窗口时由浏览器读取账号登录状态。");
        }

        private static TabPage Page(string title, Control contents)
        {
            var page = new TabPage(title) { BackColor = Surface, Padding = new Padding(8) };
            contents.Dock = DockStyle.Fill;
            page.Controls.Add(contents);
            return page;
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            bool selected = e.Index == _tabs.SelectedIndex;
            using (var background = new SolidBrush(selected ? Surface : Canvas)) e.Graphics.FillRectangle(background, e.Bounds);
            if (selected)
                using (var underline = new SolidBrush(Accent))
                    e.Graphics.FillRectangle(underline, e.Bounds.Left + 12, e.Bounds.Bottom - 4, e.Bounds.Width - 24, 3);
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Font, e.Bounds,
                selected ? Ink : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static TableLayoutPanel Grid(int columns)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, AutoScroll = true,
                Padding = new Padding(18, 16, 18, 18), BackColor = Surface, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            if (columns == 1) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            else
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            }
            return grid;
        }

        private static void Row(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted }, 0, row);
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 6, 0, 5);
            if (control is TextBox input) input.BorderStyle = BorderStyle.FixedSingle;
            grid.Controls.Add(control, Math.Min(1, grid.ColumnCount - 1), row);
        }

        private static void AddWide(TableLayoutPanel grid, Control control, int height = 0)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(height == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, height));
            control.Margin = new Padding(0, 8, 0, 8);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, grid.ColumnCount);
        }

        private static Label Section(string title) => new Label { Text = title, AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ForeColor = Ink };

        private static Label Hint(string text) => new Label { Text = text, AutoSize = true,
            MaximumSize = new Size(480, 0), ForeColor = Muted, Font = new Font("Microsoft YaHei UI", 8.5f) };

        private static FlowLayoutPanel Actions(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
            panel.Controls.AddRange(controls);
            return panel;
        }

        private static Control WithFooter(Control content, Control action)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Surface };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            content.Dock = DockStyle.Fill;
            layout.Controls.Add(content, 0, 0);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 0, 0) };
            footer.Controls.Add(action);
            layout.Controls.Add(footer, 0, 1);
            return layout;
        }

        private static void StyleButton(Button button, bool primary = false)
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(82, 34);
            button.Padding = new Padding(9, 3, 9, 3);
            button.Margin = new Padding(0, 0, 8, 5);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = primary ? Accent : Color.FromArgb(238, 241, 246);
            button.ForeColor = primary ? Color.White : Ink;
            button.UseVisualStyleBackColor = false;
        }

        private static Button Button(string title, Action action, bool primary = false)
        {
            var button = new Button { Text = title };
            StyleButton(button, primary);
            button.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception error) { MessageBox.Show(button.FindForm(), error.Message, "FvmAssistant 控制台"); }
            };
            return button;
        }

        private static Button AsyncButton(string title, Func<Task> action, bool primary = false)
        {
            var button = new Button { Text = title };
            StyleButton(button, primary);
            button.Click += async (s, e) =>
            {
                button.Enabled = false;
                try { await action(); }
                catch (Exception error) { MessageBox.Show(button.FindForm(), error.Message, "FvmAssistant 控制台"); }
                finally { button.Enabled = true; }
            };
            return button;
        }

        private Control BuildAccounts()
        {
            var split = WorkspaceSplit(260);
            var left = Grid(1);
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Section("我的账号"), 0, 0);
            _accounts.Dock = DockStyle.Fill;
            _accounts.BorderStyle = BorderStyle.None;
            _accounts.BackColor = Color.FromArgb(250, 251, 253);
            _accounts.ItemHeight = 30;
            _accounts.SelectedIndexChanged += (s, e) => ShowAccount();
            left.Controls.Add(_accounts, 0, 1);
            left.Controls.Add(Actions(Button("添加账号", AddAccount, true), Button("移除", RemoveAccount)), 0, 2);
            split.Panel1.Controls.Add(left);

            var settings = Grid(2);
            AddWide(settings, Section("账号设置"));
            Row(settings, "档案名称", _profile);
            Row(settings, "控制端口", _port);
            Row(settings, "服务器", _server);
            Row(settings, "游戏入口", _entryUrl);
            AddWide(settings, Actions(Button("保存设置", SaveAccount, true), AsyncButton("打开游戏", OpenSelectedGameAsync),
                Button("账号目录", () => OpenFolder(SelectedAccount == null ? null : new AppPaths(SelectedAccount.Profile).AccountRoot))));
            AddWide(settings, Hint("数字区服会在打开游戏时自动选择；其他区服可填写直达网址，或在游戏窗口手动选择。"));
            AddWide(settings, Actions(AsyncButton("清理账号数据…", CleanAccountAsync)));

            var library = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(12), BackColor = Surface };
            library.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            library.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            library.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            library.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            library.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            library.Controls.Add(Section("Card library"), 0, 0);
            library.Controls.Add(Actions(AsyncButton("Scan 背包...", ScanInventoryAsync, true),
                AsyncButton("打开游戏", OpenSelectedGameAsync), Button("刷新卡片", RefreshCards), Button("删除所选图标", DeleteCard)), 0, 1);
            _cards.Dock = DockStyle.Fill;
            _cards.BorderStyle = BorderStyle.FixedSingle;
            _cards.DrawMode = DrawMode.OwnerDrawFixed;
            _cards.ItemHeight = 54;
            _cards.DrawItem += DrawLibraryCard;
            library.Controls.Add(_cards, 0, 2);
            library.Controls.Add(Hint("Open the defense-card page in 背包, then scan and review its icons. Name different star variants separately.\n" +
                "Optional room learning: enter comma-separated slot names; empty entries skip slots."), 0, 3);
            _learn.Width = 250;
            library.Controls.Add(Actions(new Label { Text = "Room slots (optional)", AutoSize = true, Margin = new Padding(0, 8, 5, 0) },
                _learn, AsyncButton("从当前房间学习", LearnCardsAsync)), 0, 4);
            _accountWorkspace.TabPages.Add(Page("Card library", library));
            _accountWorkspace.TabPages.Add(Page("Account settings", settings));
            split.Panel2.Controls.Add(_accountWorkspace);
            return split;
        }

        private Control BuildTracks()
        {
            var split = WorkspaceSplit(390);
            var left = Grid(1);
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Section("可用轨道"), 0, 0);
            _tracks.Dock = DockStyle.Fill;
            _tracks.BorderStyle = BorderStyle.None;
            _tracks.BackColor = Color.FromArgb(250, 251, 253);
            _tracks.DrawMode = DrawMode.OwnerDrawFixed;
            _tracks.ItemHeight = 30;
            _tracks.DrawItem += DrawTrack;
            left.Controls.Add(_tracks, 0, 1);
            left.Controls.Add(Actions(Button("刷新", RefreshTracks), Button("预览", PreviewTrack),
                Button("Visual edit...", EditTrack), Button("New formation...", CreateTrack, true),
                Button("加入关卡队列", UseTrackInQueue)), 0, 2);
            split.Panel1.Controls.Add(left);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(16), BackColor = Surface, AutoScroll = true };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filters = Grid(2);
            filters.Padding = Padding.Empty;
            filters.Margin = Padding.Empty;
            filters.AutoScroll = false;
            _trackAccount.SelectedIndexChanged += (s, e) => { if (!_loading) { RefreshTracks(); ShowTrackSuccess(); } };
            Row(filters, "账号", _trackAccount);
            Row(filters, "关卡", _trackLevel);
            right.Controls.Add(filters, 0, 0);
            var trackDirectory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
            if (trackDirectory != null)
                _trackLevel.Items.AddRange(LevelCatalog.Load(Path.Combine(trackDirectory, "附加程序", "文本", "普通关卡.txt"))
                    .Select(l => (object)l.ToString()).ToArray());
            _trackLevel.TextChanged += (s, e) => { if (!_loading) { RefreshTracks(); ShowTrackSuccess(); } };
            _showAllTracks.CheckedChanged += (s, e) => RefreshTracks();
            _tracks.SelectedIndexChanged += (s, e) =>
            {
                if (_refreshingTracks) return;
                if (_tracks.SelectedItem is string path && _state.TrackLevels.TryGetValue(Path.GetFullPath(path), out var target))
                {
                    _loading = true;
                    _trackLevel.Text = target;
                    _loading = false;
                }
                ShowTrackSuccess();
                RefreshTrackBoard();
            };
            _success.CheckedChanged += (s, e) => SaveTrackSuccess();
            right.Controls.Add(Actions(_showAllTracks, _success), 0, 1);
            _trackBoard.Dock = DockStyle.Fill;
            right.Controls.Add(_trackBoard, 0, 2);
            _trackSummary.Margin = new Padding(0, 8, 0, 0);
            right.Controls.Add(_trackSummary, 0, 3);
            split.Panel2.Controls.Add(right);
            return split;
        }

        private Control BuildQueue()
        {
            var split = WorkspaceSplit(355);
            var left = Grid(1);
            left.RowCount = 3;
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Section("执行顺序"), 0, 0);
            _queue.Dock = DockStyle.Fill;
            _queue.BorderStyle = BorderStyle.None;
            _queue.BackColor = Color.FromArgb(250, 251, 253);
            _queue.DrawMode = DrawMode.OwnerDrawFixed;
            _queue.ItemHeight = 42;
            _queue.DrawItem += DrawQueue;
            _queue.SelectedIndexChanged += (s, e) => ShowQueueItem();
            left.Controls.Add(_queue, 0, 1);
            left.Controls.Add(Actions(Button("添加 / 更新", SaveQueueItem, true), Button("移除", RemoveQueueItem),
                Button("上移", () => MoveQueueItem(-1)), Button("下移", () => MoveQueueItem(1))), 0, 2);
            split.Panel1.Controls.Add(left);
            var right = Grid(2);
            _queueAccount.SelectedIndexChanged += (s, e) => RefreshQueue();
            AddWide(right, Section("关卡任务"));
            Row(right, "账号", _queueAccount);
            Row(right, "关卡", _queueLevel);
            Row(right, "轨道", _queueTrack);
            Row(right, "卡片映射", _queueCards);
            AddWide(right, Hint("格式：轨道卡片:已学习卡片，多个映射用逗号分隔。"));
            Row(right, "循环次数", _queueRounds);
            AddWide(right, Actions(_queueShovel, _queueBoss, _queueAutoFlame));
            AddWide(right, Hint("队列按左侧顺序逐项执行。启动前会检查轨道；若游戏尚未打开，会等待进入游戏画面。"));
            split.Panel2.Controls.Add(WithFooter(right, AsyncButton("开始队列", StartQueueAsync, true)));
            return split;
        }

        private Control BuildDuo()
        {
            var grid = Grid(2);
            AddWide(grid, Section("选择账号"));
            Row(grid, "房主", _host);
            Row(grid, "队友", _guest);
            Row(grid, "关卡", _duoLevel);
            AddWide(grid, Hint("手动组队并进入同一房间时可留空关卡。通过好友邀请组队时必须填写关卡。"));
            AddWide(grid, Section("各自轨道"));
            Row(grid, "房主轨道", _hostTrack);
            Row(grid, "房主卡片", _hostCards);
            Row(grid, "队友轨道", _guestTrack);
            Row(grid, "队友卡片", _guestCards);
            AddWide(grid, Hint("卡片映射格式：轨道卡片:已学习卡片，多个映射用逗号分隔。"));
            AddWide(grid, Section("执行方式"));
            Row(grid, "循环次数", _duoRounds);
            AddWide(grid, Actions(_duoFriendInvite, _duoBoss, _hostShovel, _guestShovel,
                _hostAutoFlame, _guestAutoFlame));
            Row(grid, "所在分区", _duoArea);
            Row(grid, "队友所在行", _duoFriendRow);
            _duoArea.Enabled = _duoFriendRow.Enabled = _duoFriendInvite.Checked;
            _duoFriendInvite.CheckedChanged += (s, e) =>
                _duoArea.Enabled = _duoFriendRow.Enabled = _duoFriendInvite.Checked;
            AddWide(grid, Hint("好友邀请要求两个账号在同一服务器的大厅地图。选择房主好友列表中队友所在行。自动房号组队仍处于实验阶段。"));
            return WithFooter(grid, AsyncButton("开始双人任务", StartDuoAsync, true));
        }

        private static SplitContainer WorkspaceSplit(int leftWidth)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 10, BackColor = Canvas };
            bool initialized = false;
            split.SizeChanged += (s, e) =>
            {
                if (initialized || split.Width < leftWidth + 450) return;
                initialized = true;
                split.SplitterDistance = leftWidth;
                split.Panel1MinSize = 220;
                split.Panel2MinSize = 400;
            };
            return split;
        }

        private void DrawTrack(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var path = (string)_tracks.Items[e.Index];
            var name = Path.GetFileName(Path.GetDirectoryName(path)) + "  /  " + Path.GetFileNameWithoutExtension(path);
            var bounds = new Rectangle(e.Bounds.Left + 9, e.Bounds.Top, e.Bounds.Width - 18, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, name, Font, bounds,
                (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private void DrawQueue(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var task = (QueueEntry)_queue.Items[e.Index];
            var color = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Ink;
            var title = (e.Index + 1) + ".  " + task.Level + "   ·   " + task.Rounds + " 局";
            TextRenderer.DrawText(e.Graphics, title, Font,
                new Rectangle(e.Bounds.Left + 9, e.Bounds.Top + 2, e.Bounds.Width - 18, 21), color,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, Path.GetFileName(task.Track) ?? "", Font,
                new Rectangle(e.Bounds.Left + 25, e.Bounds.Top + 22, e.Bounds.Width - 34, 18),
                (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private void UseTrackInQueue()
        {
            if (!(_tracks.SelectedItem is string path)) return;
            if (_trackAccount.SelectedItem is AccountEntry account) _queueAccount.SelectedItem = account;
            _queueTrack.Text = path;
            if (!string.IsNullOrWhiteSpace(_trackLevel.Text)) _queueLevel.Text = _trackLevel.Text;
            if (_trackAccount.SelectedItem is AccountEntry selected)
            {
                var known = new HashSet<string>(new CardLibrary(new AppPaths(selected.Profile).Cards).Names);
                var remembered = PanelSettings.Load(new AppPaths(selected.Profile).PanelSettings).CardMap;
                _queueCards.Text = string.Join(",", TrackContext(path).DeckCards.Select(c =>
                    (track: c.Name, mine: known.Contains(c.Name) ? c.Name :
                        remembered.TryGetValue(c.Name, out var name) && known.Contains(name) ? name : null))
                    .Where(c => c.mine != null).Select(c => c.track + ":" + c.mine));
            }
            _tabs.SelectedIndex = 2;
            _queueCards.Focus();
        }

        private AccountEntry SelectedAccount => _accounts.SelectedItem as AccountEntry;

        private void RefreshAccounts()
        {
            var selected = SelectedAccount?.Profile;
            _loading = true;
            _accounts.Items.Clear();
            foreach (var account in _state.Accounts) _accounts.Items.Add(account);
            _accounts.SelectedIndex = Math.Max(0, _state.Accounts.FindIndex(a => a.Profile == selected));
            foreach (var combo in new[] { _queueAccount, _trackAccount, _host, _guest })
            {
                var old = (combo.SelectedItem as AccountEntry)?.Profile;
                combo.Items.Clear();
                combo.Items.AddRange(_state.Accounts.Cast<object>().ToArray());
                combo.SelectedIndex = Math.Max(0, _state.Accounts.FindIndex(a => a.Profile == old));
            }
            if (_guest.Items.Count > 1 && _guest.SelectedIndex == _host.SelectedIndex) _guest.SelectedIndex = 1;
            _loading = false;
            ShowAccount();
            RefreshTracks();
            RefreshQueue();
        }

        private void ShowAccount()
        {
            var account = SelectedAccount;
            if (account == null) return;
            _profile.Text = account.Profile;
            _port.Value = account.Port;
            _server.Text = account.Server;
            _entryUrl.Text = account.EntryUrl;
            RefreshCards();
        }

        private void AddAccount()
        {
            int n = 2;
            while (_state.Accounts.Any(a => a.Profile == "account" + n)) n++;
            int port = 18765;
            while (_state.Accounts.Any(a => a.Port == port)) port++;
            var account = new AccountEntry { Profile = "account" + n, Port = port };
            _state.Accounts.Add(account);
            _state.Save();
            RefreshAccounts();
            _accounts.SelectedItem = account;
            _accountWorkspace.SelectedIndex = 1;
            _profile.Focus();
            _profile.SelectAll();
        }

        private void RemoveAccount()
        {
            var account = SelectedAccount;
            if (account == null || _state.Accounts.Count == 1) return;
            if (MessageBox.Show(this, "从控制台移除账号 " + account.Profile + "？浏览器和卡片文件会保留在数据目录。", "移除账号", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            _state.Accounts.Remove(account);
            _state.Save();
            RefreshAccounts();
        }

        private void SaveAccount()
        {
            var account = SelectedAccount;
            if (account == null) return;
            try
            {
                var replacement = new AccountEntry { Profile = _profile.Text.Trim(), Port = (int)_port.Value,
                    Server = _server.Text.Trim(), EntryUrl = _entryUrl.Text.Trim() };
                ControllerState.Validate(replacement);
                var oldProfileDir = Path.Combine(AppPaths.DataRoot(), "profiles", account.Profile);
                if (replacement.Profile != account.Profile && Directory.Exists(oldProfileDir) &&
                    Directory.EnumerateFileSystemEntries(oldProfileDir).Any())
                    throw new InvalidOperationException("已有数据的档案不能在这里改名。请添加新账号后手动迁移文件。");
                if (_state.Accounts.Any(a => a != account && (a.Profile.Equals(replacement.Profile, StringComparison.OrdinalIgnoreCase) || a.Port == replacement.Port)))
                    throw new InvalidOperationException("档案名称和控制端口不能重复。");
                if (replacement.Profile != account.Profile)
                {
                    foreach (var task in _state.Queue.Where(t => t.Profile == account.Profile)) task.Profile = replacement.Profile;
                    var oldPrefix = account.Profile.ToLowerInvariant() + "\n";
                    foreach (var key in _state.SuccessfulTracks.Keys.Where(k => k.StartsWith(oldPrefix, StringComparison.Ordinal)).ToList())
                    {
                        var marked = _state.SuccessfulTracks[key];
                        _state.SuccessfulTracks.Remove(key);
                        _state.SuccessfulTracks[replacement.Profile.ToLowerInvariant() + key.Substring(oldPrefix.Length)] = marked;
                    }
                }
                account.Profile = replacement.Profile;
                account.Port = replacement.Port;
                account.Server = replacement.Server;
                account.EntryUrl = replacement.EntryUrl;
                _state.Save();
                RefreshAccounts();
                Log("已保存账号 " + account);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "账号设置"); }
        }

        private async Task OpenSelectedGameAsync()
        {
            var account = SelectedAccount ?? throw new InvalidOperationException("请先选择账号。");
            await EnsureGameAsync(account, false);
            Log(account.Profile + " 的游戏窗口已就绪。");
        }

        private async Task<bool> IsGameOpenAsync(AccountEntry account)
        {
            try
            {
                var status = await LocalClient.GetAsync(account.Port, "/status", timeoutMs: 1200);
                if (LocalClient.Value(status, "profile") != account.Profile)
                    throw new InvalidOperationException("端口 " + account.Port + " 已被其他档案使用。");
                return true;
            }
            catch (WebException) { return false; }
        }

        private async Task EnsureGameAsync(AccountEntry account, bool authenticateForRun)
        {
            ControllerState.Validate(account);
            bool wasOpen = await IsGameOpenAsync(account);
            if (!wasOpen)
            {
                if (ProfileBusy(account.Profile))
                    throw new InvalidOperationException("该档案已在其他控制端口打开。请关闭窗口或修改保存的端口。");
                var start = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--mode osr --profile " + Quote(account.Profile) + " --port " + account.Port + " --url " + Quote(account.EntryUrl),
                    WorkingDirectory = ProjectRoot(),
                    UseShellExecute = false,
                };
                Process.Start(start);
                Log("正在打开 " + account.Profile + "（" + account.Server + "），等待游戏窗口启动。");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline && !await IsGameOpenAsync(account)) await Task.Delay(500);
                if (!await IsGameOpenAsync(account)) throw new InvalidOperationException("游戏窗口未能打开。请检查窗口报错和账号端口。");
                if (Regex.IsMatch(account.Server, "^[1-9][0-9]{0,3}$"))
                    await EnterServerAsync(account);
            }
            if (authenticateForRun && !wasOpen)
            {
                Log("正在识别 " + account.Profile + " 的游戏画面。");
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var detection = await LocalClient.GetAsync(account.Port, "/detect", timeoutMs: 5000);
                        var screen = LocalClient.Value(detection, "screen");
                        if (screen != "Unknown" && screen != "Loading100" && screen != "Disconnected")
                        {
                            Log(account.Profile + " 已进入游戏：" + screen);
                            return;
                        }
                    }
                    catch (InvalidOperationException) { }
                    await Task.Delay(1500);
                }
                throw new InvalidOperationException("90 秒内未识别到游戏画面。请在打开的窗口中登录并选择服务器，然后重新启动任务。");
            }
        }

        private async Task EnterServerAsync(AccountEntry account)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                var status = await LocalClient.GetAsync(account.Port, "/status");
                var url = LocalClient.Value(status, "url");
                if (url.IndexOf("tpl-sid-" + account.Server + "-", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                    parsed.Host == "my.4399.com" && parsed.AbsolutePath.EndsWith("/wd-air", StringComparison.OrdinalIgnoreCase) &&
                    LocalClient.Flag(status, "browserReady") && LocalClient.Flag(status, "rendererReady"))
                {
                    try
                    {
                        // Background page requests can keep loading=true after the visible controls are ready.
                        await LocalClient.GetAsync(account.Port, "/portal",
                            new Dictionary<string, string> { ["state"] = "select", ["server"] = account.Server }, 20000);
                        Log(account.Profile + " 已进入服务器 " + account.Server + "。");
                        return;
                    }
                    catch (InvalidOperationException error)
                    {
                        Log("服务器入口仍在加载：" + error.Message);
                    }
                }
                await Task.Delay(1000);
            }
            throw new InvalidOperationException("无法进入服务器 " + account.Server + "。请在游戏窗口登录或手动选区。");
        }

        private void RefreshCards()
        {
            foreach (var icon in _cardIcons.Values) icon.Dispose();
            _cardIcons.Clear();
            _cards.Items.Clear();
            if (SelectedAccount == null) return;
            var library = new CardLibrary(new AppPaths(SelectedAccount.Profile).Cards);
            foreach (var name in library.Names)
            {
                _cards.Items.Add(name);
                _cardIcons[name] = library.Get(name).ToBitmap();
            }
        }

        private void DrawLibraryCard(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var name = (string)_cards.Items[e.Index];
            if (_cardIcons.TryGetValue(name, out var icon))
                e.Graphics.DrawImage(icon, new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + 2, 42, 50));
            TextRenderer.DrawText(e.Graphics, name, Font,
                new Rectangle(e.Bounds.Left + 57, e.Bounds.Top, e.Bounds.Width - 60, e.Bounds.Height),
                (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private async Task ScanInventoryAsync()
        {
            var account = SelectedAccount ?? throw new InvalidOperationException("Select an account first.");
            if (!await IsGameOpenAsync(account)) throw new InvalidOperationException("Open the account's game window and its 背包 defense-card page first.");
            var paths = new AppPaths(account.Profile);
            using (var dialog = new InventoryScanDialog(async () =>
            {
                var response = await LocalClient.GetAsync(account.Port, "/inventory", new Dictionary<string, string> { ["state"] = "capture" });
                using (var stream = new MemoryStream(Convert.FromBase64String(LocalClient.Value(response, "png"))))
                using (var bitmap = new Bitmap(stream))
                    return new InventorySnapshot { Id = LocalClient.Value(response, "id"),
                        Frame = StageFrame.FromBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)) };
            }, async (id, items, replace) =>
            {
                await LocalClient.GetAsync(account.Port, "/inventory", new Dictionary<string, string>
                { ["state"] = "learn", ["id"] = id, ["items64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Serialize(items))),
                    ["replace"] = replace ? "1" : "0" });
            }, new CardLibrary(paths.Cards), Path.Combine(paths.AccountRoot, "inventory-grid.json")))
                dialog.ShowDialog(this);
            RefreshCards();
        }

        private void DeleteCard()
        {
            if (SelectedAccount == null || !(_cards.SelectedItem is string name)) return;
            if (MessageBox.Show(this, "删除 " + SelectedAccount.Profile + " 的卡片图标“" + name + "”？", "删除卡片", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var folder = new AppPaths(SelectedAccount.Profile).Cards;
            var path = Path.GetFullPath(Path.Combine(folder, name + ".png"));
            if (!path.StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("卡片路径不在账号目录中。");
            File.Delete(path);
            RefreshCards();
        }

        private async Task LearnCardsAsync()
        {
            var account = SelectedAccount ?? throw new InvalidOperationException("请先选择账号。");
            if (!await IsGameOpenAsync(account)) throw new InvalidOperationException("请先打开账号的游戏窗口，并在房间里准备卡组。");
            await LocalClient.GetAsync(account.Port, "/cards", new Dictionary<string, string> { ["learn"] = _learn.Text });
            RefreshCards();
            Log("已学习 " + account.Profile + " 的卡片。");
        }

        private void RefreshTracks()
        {
            if (_refreshingTracks) return;
            _refreshingTracks = true;
            try
            {
                var old = _tracks.SelectedItem as string;
                _tracks.Items.Clear();
                var directory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
                var preset = directory == null ? null : Path.Combine(directory, "预制轨道");
                var level = SelectedTrackLevel();
                var trackAccount = _trackAccount.SelectedItem as AccountEntry;
                if (trackAccount != null)
                {
                    var paths = new AppPaths(trackAccount.Profile);
                    foreach (var personal in new[] { paths.EditedTracks, paths.Tracks })
                        foreach (var file in Directory.GetFiles(personal, "*.txt", SearchOption.AllDirectories).OrderBy(p => p)
                            .Where(p => _showAllTracks.Checked || level == null ||
                                (_state.TrackLevels.TryGetValue(Path.GetFullPath(p), out var target)
                                    ? target == level.ToString() : Path.GetFileNameWithoutExtension(p).Contains(level.Name))))
                            _tracks.Items.Add(file);
                }
                if (preset != null && Directory.Exists(preset))
                    foreach (var file in Directory.GetFiles(preset, "*.txt", SearchOption.AllDirectories).OrderBy(p => p)
                        .Where(p => _showAllTracks.Checked || level == null ||
                            (Path.GetFileNameWithoutExtension(p).Contains(level.Name) &&
                             Path.GetFileName(Path.GetDirectoryName(p)).TrimStart("0123456789".ToCharArray()) == level.Island)))
                        _tracks.Items.Add(file);
                if (old != null) _tracks.SelectedItem = old;
                if (_tracks.SelectedIndex < 0 && _tracks.Items.Count > 0) _tracks.SelectedIndex = 0;
            }
            finally { _refreshingTracks = false; }
            ShowTrackSuccess();
            RefreshTrackBoard();
        }

        private LevelEntry SelectedTrackLevel()
        {
            var directory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
            if (directory == null || string.IsNullOrWhiteSpace(_trackLevel.Text)) return null;
            return LevelCatalog.Find(LevelCatalog.Load(Path.Combine(directory, "附加程序", "文本", "普通关卡.txt")), _trackLevel.Text);
        }

        private TrackPreflight TrackContext(string path)
        {
            var directory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
            var preset = directory == null ? null : Path.Combine(directory, "预制轨道");
            return new TrackPreflight(TrackFile.Load(path), preset == null ? null : CarrierMap.Find(preset, SelectedTrackLevel(), path),
                directory == null ? null : FlameReader.Load(directory));
        }

        private void RefreshTrackBoard()
        {
            foreach (var icon in _trackIcons.Values) icon.Dispose();
            _trackIcons.Clear();
            if (!(_tracks.SelectedItem is string path))
            {
                _trackBoard.ShowFormation(null, null, null);
                _trackSummary.Text = "Choose a stage and select a track, or create a new formation.";
                return;
            }
            try
            {
                var plan = TrackContext(path);
                if (_trackAccount.SelectedItem is AccountEntry account)
                {
                    var library = new CardLibrary(new AppPaths(account.Profile).Cards);
                    var known = new HashSet<string>(library.Names);
                    foreach (var name in plan.DeckCards.Select(c => c.Name).Distinct().Where(known.Contains))
                        _trackIcons[name] = library.Get(name).ToBitmap();
                }
                _trackBoard.ShowFormation(plan.Track, plan.DeckCards, plan.Carriers, _trackIcons);
                var report = plan.Inspect(deckLimit: DeckBuilder.DeckSlots);
                _trackSummary.Text = Path.GetFileNameWithoutExtension(path) + "  |  " + plan.DeckCards.Count + " cards" +
                    Environment.NewLine + (report.CanStart ? "Opening formation compatible." : "Open the editor to review compatibility errors.");
            }
            catch (Exception error) when (error is IOException || error is FormatException || error is ArgumentException || error is InvalidOperationException)
            {
                _trackBoard.ShowFormation(null, null, null);
                _trackSummary.Text = error.Message;
            }
        }

        private void PreviewTrack()
        {
            if (!(_tracks.SelectedItem is string path)) return;
            try
            {
                var preflight = TrackContext(path);
                using (var preview = new TrackPreviewDialog(preflight, preflight.DeckCards.Select(c => c.Name))) preview.ShowDialog(this);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "轨道预览"); }
        }

        private void EditTrack()
        {
            if (!(_tracks.SelectedItem is string path)) return;
            OpenTrackEditor(path);
        }

        private void CreateTrack()
        {
            if (SelectedTrackLevel() == null) throw new InvalidOperationException("Choose a valid stage before creating a formation.");
            OpenTrackEditor(null);
        }

        private void OpenTrackEditor(string path)
        {
            try
            {
                var account = _trackAccount.SelectedItem as AccountEntry ?? throw new InvalidOperationException("Select an account first.");
                var paths = new AppPaths(account.Profile);
                var directory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
                var carriers = directory == null ? null : CarrierMap.Find(Path.Combine(directory, "预制轨道"), SelectedTrackLevel(), path);
                using (var editor = new TrackEditorDialog(path, carriers, paths.EditedTracks, new CardLibrary(paths.Cards), _trackLevel.Text))
                {
                    editor.ShowDialog(this);
                    if (editor.SavedPath == null) return;
                    if (SelectedTrackLevel() is LevelEntry level) _state.TrackLevels[Path.GetFullPath(editor.SavedPath)] = level.ToString();
                    _state.Save();
                    RefreshTracks();
                    _tracks.SelectedItem = editor.SavedPath;
                }
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "轨道编辑"); }
        }

        private void ShowTrackSuccess()
        {
            _loading = true;
            var account = _trackAccount.SelectedItem as AccountEntry;
            var path = _tracks.SelectedItem as string;
            _success.Enabled = account != null && path != null && !string.IsNullOrWhiteSpace(_trackLevel.Text);
            _success.Checked = _success.Enabled && _state.SuccessfulTracks.TryGetValue(ControllerState.SuccessKey(account.Profile, _trackLevel.Text, path), out var marked) && marked;
            _loading = false;
        }

        private void SaveTrackSuccess()
        {
            if (_loading || !_success.Enabled || !(_tracks.SelectedItem is string path)) return;
            var account = _trackAccount.SelectedItem as AccountEntry;
            if (account == null) return;
            _state.SuccessfulTracks[ControllerState.SuccessKey(account.Profile, _trackLevel.Text, path)] = _success.Checked;
            _state.Save();
        }

        private void RefreshQueue()
        {
            if (_loading) return;
            _queue.Items.Clear();
            var account = _queueAccount.SelectedItem as AccountEntry;
            if (account == null) return;
            foreach (var task in _state.Queue.Where(t => t.Profile == account.Profile))
                _queue.Items.Add(task);
            _queue.Invalidate();
        }

        private void ShowQueueItem()
        {
            if (!(_queue.SelectedItem is QueueEntry entry)) return;
            _queueLevel.Text = entry.Level;
            _queueTrack.Text = entry.Track;
            _queueCards.Text = entry.Cards;
            _queueRounds.Value = Math.Max(1, Math.Min(500, entry.Rounds));
            _queueShovel.Checked = entry.Shovel;
            _queueBoss.Checked = entry.Boss;
            _queueAutoFlame.Checked = entry.AutoFlame;
        }

        private void SaveQueueItem()
        {
            var account = _queueAccount.SelectedItem as AccountEntry;
            if (account == null) return;
            if (new[] { _queueLevel.Text, _queueTrack.Text, _queueCards.Text }.Any(string.IsNullOrWhiteSpace))
            {
                MessageBox.Show(this, "请填写关卡、轨道和卡片映射。", "关卡任务");
                return;
            }
            var task = _queue.SelectedItem as QueueEntry;
            if (task == null) { task = new QueueEntry(); _state.Queue.Add(task); }
            task.Profile = account.Profile;
            task.Level = _queueLevel.Text.Trim();
            task.Track = _queueTrack.Text.Trim();
            task.Cards = _queueCards.Text.Trim();
            task.Rounds = (int)_queueRounds.Value;
            task.Shovel = _queueShovel.Checked;
            task.Boss = _queueBoss.Checked;
            task.AutoFlame = _queueAutoFlame.Checked;
            _state.Save();
            RefreshQueue();
            _queue.SelectedItem = task;
        }

        private void RemoveQueueItem()
        {
            if (!(_queue.SelectedItem is QueueEntry task)) return;
            _state.Queue.Remove(task);
            _state.Save();
            RefreshQueue();
        }

        private void MoveQueueItem(int delta)
        {
            if (!(_queue.SelectedItem is QueueEntry task)) return;
            int old = _state.Queue.IndexOf(task), next = old + delta;
            if (next < 0 || next >= _state.Queue.Count || _state.Queue[next].Profile != task.Profile) return;
            _state.Queue.RemoveAt(old);
            _state.Queue.Insert(next, task);
            _state.Save();
            RefreshQueue();
            _queue.SelectedItem = task;
        }

        private async Task StartQueueAsync()
        {
            if (_coordinator != null && !_coordinator.HasExited) throw new InvalidOperationException("已有任务正在运行。");
            var account = _queueAccount.SelectedItem as AccountEntry ?? throw new InvalidOperationException("请选择账号。");
            var tasks = _state.Queue.Where(t => t.Profile == account.Profile).ToList();
            if (tasks.Count == 0) throw new InvalidOperationException("请先添加至少一个关卡任务。");
            await EnsureGameAsync(account, true);
            var queueDir = Path.Combine(AppPaths.DataRoot(), "queues");
            Directory.CreateDirectory(queueDir);
            var plan = Path.Combine(queueDir, "active-" + account.Profile + ".json");
            File.WriteAllText(plan, Json.Serialize(new { tasks = tasks.Select(t => new
            {
                level = t.Level, track = t.Track, cards = t.Cards, rounds = t.Rounds,
                shovel = t.Shovel, boss = t.Boss, autoflame = t.AutoFlame
            }).ToList() }), new UTF8Encoding(false));
            _activeHost = account;
            _activeGuest = null;
            try
            {
                int code = await RunScriptAsync("queue.ps1", "-Plan", plan, "-Port", account.Port.ToString());
                if (code == 0)
                {
                    foreach (var task in tasks)
                    {
                        var path = ResolveTrack(task.Track);
                        _state.SuccessfulTracks[ControllerState.SuccessKey(account.Profile, task.Level, path)] = true;
                    }
                    _state.Save();
                    ShowTrackSuccess();
                    Log("关卡队列已完成。");
                }
                else Log("关卡队列已停止或执行失败。详情请查看账号运行日志。");
            }
            finally
            {
                _activeHost = null;
                _activeGuest = null;
                if (File.Exists(plan)) File.Delete(plan);
            }
        }

        private async Task StartDuoAsync()
        {
            if (_coordinator != null && !_coordinator.HasExited) throw new InvalidOperationException("已有任务正在运行。");
            var host = _host.SelectedItem as AccountEntry;
            var guest = _guest.SelectedItem as AccountEntry;
            if (host == null || guest == null || host.Profile == guest.Profile) throw new InvalidOperationException("请选择两个不同的账号。");
            if (new[] { _hostTrack.Text, _guestTrack.Text, _hostCards.Text, _guestCards.Text }.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("两个账号都需要填写轨道和卡片映射。");
            if (host.Server != guest.Server) throw new InvalidOperationException("两个账号必须选择同一服务器。");
            if (_duoFriendInvite.Checked && string.IsNullOrWhiteSpace(_duoLevel.Text))
                throw new InvalidOperationException("好友邀请组队需要填写关卡。");
            bool hostOpen = await IsGameOpenAsync(host), guestOpen = await IsGameOpenAsync(guest);
            if (!hostOpen || !guestOpen)
            {
                await EnsureGameAsync(host, true);
                await EnsureGameAsync(guest, true);
                if (!_duoFriendInvite.Checked)
                    throw new InvalidOperationException("两个游戏窗口已打开。请先让账号进入同一房间，然后重新启动双人任务。");
            }
            _activeHost = host;
            _activeGuest = guest;
            var args = new List<string> { "-HostPort", host.Port.ToString(), "-GuestPort", guest.Port.ToString(),
                "-HostTrack", _hostTrack.Text.Trim(), "-GuestTrack", _guestTrack.Text.Trim(),
                "-HostCards", _hostCards.Text.Trim(), "-GuestCards", _guestCards.Text.Trim(),
                "-Rounds", ((int)_duoRounds.Value).ToString() };
            if (!string.IsNullOrWhiteSpace(_duoLevel.Text)) { args.Add("-Level"); args.Add(_duoLevel.Text.Trim()); }
            if (_hostShovel.Checked) args.Add("-HostShovel");
            if (_guestShovel.Checked) args.Add("-GuestShovel");
            if (_duoBoss.Checked) args.Add("-Boss");
            if (_duoFriendInvite.Checked)
            {
                args.Add("-FriendInvite");
                args.Add("-Area"); args.Add(((int)_duoArea.Value).ToString());
                args.Add("-FriendRow"); args.Add(((int)_duoFriendRow.Value).ToString());
            }
            if (_hostAutoFlame.Checked) args.Add("-HostAutoFlame");
            if (_guestAutoFlame.Checked) args.Add("-GuestAutoFlame");
            try
            {
                int code = await RunScriptAsync("duo.ps1", args.ToArray());
                if (code == 0)
                {
                    if (!string.IsNullOrWhiteSpace(_duoLevel.Text))
                    {
                        _state.SuccessfulTracks[ControllerState.SuccessKey(host.Profile, _duoLevel.Text, ResolveTrack(_hostTrack.Text))] = true;
                        _state.SuccessfulTracks[ControllerState.SuccessKey(guest.Profile, _duoLevel.Text, ResolveTrack(_guestTrack.Text))] = true;
                        _state.Save();
                    }
                    Log("双人任务已完成。");
                }
                else Log("双人任务已停止或执行失败。详情请查看两个账号的运行日志。");
            }
            finally { _activeHost = null; _activeGuest = null; }
        }

        private async Task<int> RunScriptAsync(string script, params string[] arguments)
        {
            var path = Path.Combine(ProjectRoot(), "scripts", script);
            if (!File.Exists(path)) throw new FileNotFoundException("任务脚本不存在。", path);
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(path) + " " + string.Join(" ", arguments.Select(Quote)),
                WorkingDirectory = ProjectRoot(), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                if (!process.Start()) throw new InvalidOperationException("无法启动任务脚本。");
                _coordinator = process;
                _stop.Enabled = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());
                int code = process.ExitCode;
                _coordinator = null;
                _stop.Enabled = false;
                return code;
            }
        }

        private async Task StopRunAsync()
        {
            var process = _coordinator;
            if (process == null) return;
            foreach (var account in new[] { _activeHost, _activeGuest }.Where(a => a != null))
            {
                try { await LocalClient.GetAsync(account.Port, _activeGuest == null ? "/grind" : "/duo",
                    new Dictionary<string, string> { ["state"] = "stop" }); }
                catch (Exception error) { Log("停止 " + account.Profile + " 任务失败：" + error.Message); }
            }
            if (!process.HasExited) process.Kill();
            Log("任务已停止。");
        }

        private async Task CleanAccountAsync()
        {
            var account = SelectedAccount;
            if (account == null) return;
            if (await IsGameOpenAsync(account) || ProfileBusy(account.Profile))
            {
                MessageBox.Show(this, "请先关闭该账号的游戏窗口，再清理数据。", "清理账号");
                return;
            }
            bool captures, logs, tracks, browserCache;
            using (var dialog = new Form { Text = "清理账号 · " + account.Profile, Font = Font, Size = new Size(490, 320),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
                choices.Items.Add("录像、截图和运行捕获", true);
                choices.Items.Add("应用与 CEF 日志", true);
                choices.Items.Add("从录像生成的轨道", false);
                choices.Items.Add("CEF 资源缓存（保留登录状态和 Flash 数据）", false);
                var note = new Label { Text = "只清理当前账号勾选的数据；登录状态、已学习卡片和编辑副本会保留。",
                    Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42 };
                var ok = new Button { Text = "删除所选数据", DialogResult = DialogResult.OK, AutoSize = true };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
                dialog.Controls.Add(choices);
                dialog.Controls.Add(note);
                dialog.Controls.Add(buttons);
                dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                captures = choices.GetItemChecked(0);
                logs = choices.GetItemChecked(1);
                tracks = choices.GetItemChecked(2);
                browserCache = choices.GetItemChecked(3);
            }
            var paths = new AppPaths(account.Profile);
            if (captures) DeleteContents(paths.Captures);
            if (logs) DeleteContents(paths.Logs);
            if (tracks) DeleteContents(paths.Tracks);
            if (browserCache)
                foreach (var name in new[] { "Cache", "Code Cache", "GPUCache" })
                    DeleteContents(Path.Combine(paths.ProfileCache, name));
            if (tracks) RefreshTracks();
            Log("已清理 " + account.Profile + " 的所选数据。");
        }

        private static bool ProfileBusy(string profile)
        {
            using (var mutex = new Mutex(false, @"Local\FvmAssistant.Profile." + profile.ToLowerInvariant()))
            {
                try
                {
                    if (!mutex.WaitOne(0)) return true;
                    mutex.ReleaseMutex();
                    return false;
                }
                catch (AbandonedMutexException) { mutex.ReleaseMutex(); return false; }
            }
        }

        private static void DeleteContents(string folder)
        {
            var root = Path.GetFullPath(AppPaths.DataRoot()).TrimEnd('\\') + "\\";
            var full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("清理路径不在数据目录中。");
            if (!Directory.Exists(full)) return;
            if ((File.GetAttributes(full.TrimEnd('\\')) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("不能清理链接的账号目录。");
            foreach (var file in Directory.GetFiles(full)) File.Delete(file);
            foreach (var directory in Directory.GetDirectories(full)) DeleteTree(directory, root);
        }

        private static void DeleteTree(string directory, string root)
        {
            var full = Path.GetFullPath(directory).TrimEnd('\\');
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("清理路径不在数据目录中。");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(full);
                return;
            }
            foreach (var file in Directory.GetFiles(full)) File.Delete(file);
            foreach (var child in Directory.GetDirectories(full)) DeleteTree(child, root);
            Directory.Delete(full);
        }

        private static string ResolveTrack(string track)
        {
            if (Path.IsPathRooted(track)) return Path.GetFullPath(track);
            var directory = AssistantOptions.Parse(new string[0]).ResolveTrackDir();
            if (directory == null) throw new InvalidOperationException("缺少轨道编辑器数据。");
            return Path.GetFullPath(Path.Combine(directory, "预制轨道", track.Replace('/', '\\')));
        }

        private static string ProjectRoot()
        {
            for (var dir = new DirectoryInfo(Application.ExecutablePath).Parent; dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(dir.FullName, "scripts")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("控制台需要仓库中的 scripts 目录。");
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"')
                {
                    result.Append('\\', slashes * 2 + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', slashes);
                    result.Append(c);
                }
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static void OpenFolder(string folder)
        {
            if (folder != null && Directory.Exists(folder)) Process.Start("explorer.exe", Quote(folder));
        }

        private void Log(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var icon in _cardIcons.Values) icon.Dispose();
                foreach (var icon in _trackIcons.Values) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
