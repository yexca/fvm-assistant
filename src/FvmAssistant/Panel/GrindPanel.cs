using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>
    /// Operation panel beside the game: pick a level and a preset track, map the track's cards to
    /// the user's learned cards, set the number of rounds, start / stop, and watch the log.
    /// </summary>
    internal sealed class GrindPanel : UserControl
    {
        private const string NoCard = "（不带）";

        /// <summary>The log takes the height left over; below this the whole panel scrolls instead.</summary>
        private const int LogMinHeight = 160;

        private readonly GrindController _grind;
        private readonly TableLayoutPanel _layout;
        private bool _scrollQueued;
        private readonly string _settingsPath;
        private readonly PanelSettings _settings;

        private readonly ComboBox _level;
        private readonly ComboBox _track;
        private readonly TableLayoutPanel _cards;
        private readonly Label _cardsHint;
        private readonly TextBox _compatibility;
        private readonly Button _preview;
        private readonly Button _editTrack;
        private readonly NumericUpDown _rounds;
        private readonly CheckBox _boss;
        private readonly CheckBox _shovel;
        private readonly CheckBox _autoFlame;
        private readonly Button _start;
        private readonly Button _stop;
        private readonly Button _finishRound;
        private readonly Button _learn;
        private readonly Label _state;
        private readonly Button _record;
        private readonly Label _recordState;
        private readonly Button _fromRecording;
        private bool _recording;
        private readonly Func<System.Threading.Tasks.Task<string>> _toggleRecording;
        private readonly ListBox _log;
        private readonly LinkLabel _openRun;
        private readonly Timer _refresh = new Timer { Interval = 1000 };
        private readonly List<(TrackCard card, ComboBox mine)> _cardRows = new List<(TrackCard, ComboBox)>();
        private readonly List<Control> _inputs = new List<Control>();
        private TrackFile _loadedTrack;
        private TrackPreflight _preflight;
        private bool _compatible;
        private bool _populating;

        private sealed class LevelItem
        {
            public LevelEntry Entry;

            public override string ToString() =>
                Entry == null ? "（已在关卡房间里）" : $"{Entry.Island} / {Entry.Name}" + (Entry.HasBoss ? $"　boss：{Entry.Key}" : "");
        }

        public GrindPanel(GrindController grind, string settingsPath, Func<System.Threading.Tasks.Task<string>> toggleRecording)
        {
            _grind = grind;
            _toggleRecording = toggleRecording;
            _settingsPath = settingsPath;
            _settings = PanelSettings.Load(settingsPath);
            // The system UI font on a Japanese Windows draws "\" as "¥"; use a Chinese UI font.
            Font = new Font("Microsoft YaHei UI", 9f);
            Padding = new Padding(8);
            BackColor = SystemColors.Control;

            // The layout fills the panel's scrollable area; AutoScrollMinSize (see UpdateScrollRange) keeps
            // that area tall enough for every control plus a usable log, so a short window scrolls.
            AutoScroll = true;
            var layout = _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = false };
            layout.Layout += (s, e) => QueueScrollUpdate();
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void Add(Control c, bool fill = false)
            {
                layout.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
                c.Dock = fill ? DockStyle.Fill : DockStyle.Top;
                layout.Controls.Add(c, 0, layout.RowStyles.Count - 1);
            }

            Label Heading(string text) => new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 3) };

            Add(new Label { Text = "自动刷图", AutoSize = true, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });

            Add(Heading("关卡"));
            _level = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 25 };
            WheelScrollsPanel(_level);
            _level.SelectedIndexChanged += (s, e) => OnLevelChanged();
            Add(_level);

            Add(Heading("轨道（预制轨道里的文件）"));
            var trackRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            trackRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            trackRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _track = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
            WheelScrollsPanel(_track);
            _track.SelectedIndexChanged += (s, e) => OnTrackChanged();
            _track.Leave += (s, e) => OnTrackChanged();
            var browse = new Button { Text = "…", Width = 32, Height = _track.Height + 2 };
            browse.Click += (s, e) => BrowseTrack();
            trackRow.Controls.Add(_track, 0, 0);
            trackRow.Controls.Add(browse, 1, 0);
            Add(trackRow);

            Add(Heading("卡片：轨道里的卡 → 我的卡"));
            _cards = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            _cards.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(_cards);
            _cardsHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(310, 0), Margin = new Padding(0, 2, 0, 4) };
            Add(_cardsHint);
            _compatibility = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Height = 100, Text = "Select a track to check compatibility.", TabStop = true };
            Add(_compatibility);
            var trackTools = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = true };
            _preview = new Button { Text = "预览阵型与时间线…", AutoSize = true, Enabled = false };
            _preview.Click += (s, e) => PreviewTrack();
            trackTools.Controls.Add(_preview);
            _editTrack = new Button { Text = "编辑轨道副本…", AutoSize = true, Enabled = false };
            _editTrack.Click += (s, e) => EditTrack();
            trackTools.Controls.Add(_editTrack);
            var createTrack = new Button { Text = "New formation...", AutoSize = true };
            createTrack.Click += (s, e) => CreateTrack();
            trackTools.Controls.Add(createTrack);
            _inputs.Add(createTrack);
            Add(trackTools);

            var learnRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = true };
            var inventory = new Button { Text = "Scan 背包...", AutoSize = true };
            inventory.Click += async (s, e) => await LearnInventoryAsync();
            learnRow.Controls.Add(inventory);
            _inputs.Add(inventory);
            _learn = new Button { Text = "学习当前卡组…", AutoSize = true };
            _learn.Click += async (s, e) => await LearnDeckAsync();
            var cardsFolder = new LinkLabel { Text = "图标文件夹", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            cardsFolder.LinkClicked += (s, e) => OpenFolder(_grind.CardsDirectory);
            learnRow.Controls.Add(_learn);
            learnRow.Controls.Add(cardsFolder);
            Add(learnRow);

            Add(Heading("设置"));
            var roundsRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            roundsRow.Controls.Add(new Label { Text = "局数", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            _rounds = new NumericUpDown { Minimum = 1, Maximum = 500, Width = 70, Value = Math.Max(1, Math.Min(500, _settings.Rounds)) };
            WheelScrollsPanel(_rounds);
            roundsRow.Controls.Add(_rounds);
            Add(roundsRow);
            _boss = new CheckBox { Text = "通关后继续打 boss 关（每局消耗钥匙）", AutoSize = true, Checked = _settings.Boss };
            Add(_boss);
            _shovel = new CheckBox { Text = "执行轨道里的铲子步骤", AutoSize = true, Checked = _settings.Shovel };
            _shovel.CheckedChanged += (s, e) => UpdateCompatibility();
            Add(_shovel);
            _autoFlame = new CheckBox { Text = "账号有自动拾取火苗（只重点扫最后两列）", AutoSize = true, Checked = _settings.AutoFlame };
            Add(_autoFlame);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0), WrapContents = true };
            _start = new Button { Text = "▶ 开始刷图", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _start.Click += async (s, e) => await StartAsync();
            _stop = new Button { Text = "■ 停止", AutoSize = true, Enabled = false };
            _stop.Click += (s, e) => _grind.Stop();
            _finishRound = new Button { Text = "Finish current round", AutoSize = true, Enabled = false };
            _finishRound.Click += (s, e) =>
            {
                try { _grind.FinishAfterRound(); UpdateState(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Finish current round"); }
            };
            buttons.Controls.Add(_start);
            buttons.Controls.Add(_stop);
            buttons.Controls.Add(_finishRound);
            Add(buttons);

            Add(Heading("Missions"));
            var missionButtons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = true };
            foreach (var board in new[] { (label: "Open account task board", action: "open"), (label: "Open guild task board", action: "open-guild") })
            {
                var openBoard = new Button { Text = board.label, AutoSize = true };
                openBoard.Click += async (s, e) =>
                {
                    try
                    {
                        var result = await _grind.MissionActionAsync(board.action);
                        AppendLog(result.Result ?? result.View);
                    }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "Mission board"); }
                };
                missionButtons.Controls.Add(openBoard);
                _inputs.Add(openBoard);
            }
            var publishTasks = new Button { Text = "Publish guild-leader tasks", AutoSize = true };
            publishTasks.Click += async (s, e) =>
            {
                try
                {
                    var result = await _grind.MissionActionAsync("publish");
                    AppendLog(result.Result ?? result.View);
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Guild tasks"); }
            };
            var claimTask = new Button { Text = "Claim selected task reward", AutoSize = true };
            claimTask.Click += async (s, e) =>
            {
                try
                {
                    var before = await _grind.MissionActionAsync("inspect");
                    var result = await _grind.MissionActionAsync("claim", selectedHash: before.SelectedHash);
                    AppendLog(result.Result);
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Mission rewards"); }
            };
            missionButtons.Controls.Add(publishTasks);
            missionButtons.Controls.Add(claimTask);
            Add(missionButtons);
            _inputs.Add(publishTasks);
            _inputs.Add(claimTask);
            Add(new Label { Text = "Claim rewards first. Click a new task and verify 任务进行中 before its battle. Task scanning and stage selection remain attended.",
                AutoSize = true, MaximumSize = new Size(310, 0), ForeColor = SystemColors.GrayText });

            _state = new Label { AutoSize = true, MaximumSize = new Size(310, 0), Margin = new Padding(0, 6, 0, 4) };
            Add(_state);

            _log = new ListBox { IntegralHeight = false, HorizontalScrollbar = true, Font = new Font("Microsoft YaHei UI", 8.5f) };
            Add(_log, fill: true);

            _openRun = new LinkLabel { Text = "打开本次记录文件夹", AutoSize = true, Enabled = false };
            _openRun.LinkClicked += (s, e) => { if (_grind.Current != null) OpenFolder(_grind.Current.RunDirectory); };
            Add(_openRun);

            Add(Heading("录制（手动打一局，记录点击和画面）"));
            var recordRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            _record = new Button { Text = "● 开始录制", AutoSize = true };
            _record.Click += async (s, e) => await ToggleRecordingAsync();
            _recordState = new Label { AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(6, 6, 0, 0), ForeColor = SystemColors.GrayText, Text = "不记录键盘输入" };
            recordRow.Controls.Add(_record);
            recordRow.Controls.Add(_recordState);
            Add(recordRow);
            var fromRecordingRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            _fromRecording = new Button { Text = "用最近的录制生成轨道…", AutoSize = true };
            _fromRecording.Click += async (s, e) => await TrackFromRecordingAsync();
            fromRecordingRow.Controls.Add(_fromRecording);
            Add(fromRecordingRow);
            Add(new Label
            {
                Text = "从房间里开始录制，手动打完一局后停止录制，再点这里：按你放卡的格子和顺序生成补阵轨道，存到当前账户的 tracks 文件夹。卡组里的卡要先学习过。",
                AutoSize = true,
                MaximumSize = new Size(310, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 2, 0, 4),
            });

            Controls.Add(layout);
            _inputs.AddRange(new Control[] { _level, _track, browse, _cards, _learn, _rounds, _shovel, _autoFlame, _boss, _fromRecording });

            _grind.Message += line => OnUi(() => AppendLog(line));
            _grind.RunningChanged += running => OnUi(() => SetRunning(running));
            _refresh.Tick += (s, e) => UpdateState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PopulateLevels();
            SetRunning(_grind.Running);
            QueueScrollUpdate();
            // Save while the controls still exist; by Dispose time their text is gone.
            var form = FindForm();
            if (form != null) form.FormClosing += (s, args) => SaveSettings();
        }

        private void SaveSettings()
        {
            foreach (var (card, mine) in _cardRows) RememberChoice(card.Name, mine);
            _settings.Level = SelectedLevel?.ToString();
            _settings.Track = _track.Text;
            _settings.Rounds = (int)_rounds.Value;
            _settings.Shovel = _shovel.Checked;
            _settings.AutoFlame = _autoFlame.Checked;
            _settings.Boss = _boss.Checked;
            _settings.Save(_settingsPath);
        }

        // ---------- level / track / cards ----------

        private void PopulateLevels()
        {
            _populating = true;
            try
            {
                _level.Items.Clear();
                _level.Items.Add(new LevelItem());
                foreach (var entry in _grind.Levels()) _level.Items.Add(new LevelItem { Entry = entry });
                var saved = _level.Items.Cast<LevelItem>().FirstOrDefault(i => i.Entry?.ToString() == _settings.Level);
                _level.SelectedItem = saved ?? _level.Items[0];
            }
            catch (Exception ex)
            {
                _state.Text = "读取关卡列表失败：" + ex.Message;
            }
            finally
            {
                _populating = false;
            }
            OnLevelChanged(keepSavedTrack: true);
        }

        private LevelEntry SelectedLevel => (_level.SelectedItem as LevelItem)?.Entry;

        private void OnLevelChanged(bool keepSavedTrack = false)
        {
            if (_populating) return;
            var suggestions = _grind.SuggestTracks(SelectedLevel);
            _populating = true;
            try
            {
                _track.Items.Clear();
                _track.Items.AddRange(suggestions.Cast<object>().ToArray());
                if (keepSavedTrack && !string.IsNullOrEmpty(_settings.Track)) _track.Text = _settings.Track;
                else if (suggestions.Count > 0) _track.SelectedIndex = 0;
                else if (SelectedLevel != null) _track.Text = "";
            }
            finally
            {
                _populating = false;
            }
            UpdateBossOption();
            OnTrackChanged();
        }

        private void BrowseTrack()
        {
            using (var dialog = new OpenFileDialog { Filter = "轨道文件 (*.txt)|*.txt", InitialDirectory = SafePresetDir() })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var root = SafePresetDir();
                _track.Text = root != null && dialog.FileName.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                    ? dialog.FileName.Substring(root.Length + 1)
                    : dialog.FileName;
                OnTrackChanged();
            }
        }

        private string SafePresetDir()
        {
            try { return _grind.PresetTrackDir; }
            catch (InvalidOperationException) { return null; }
        }

        private void OnTrackChanged()
        {
            if (_populating) return;
            TrackFile track = null;
            _preflight = null;
            string problem = null;
            try
            {
                _preflight = _grind.PrepareTrack(_track.Text, SelectedLevel);
                track = _preflight.Track;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
            _loadedTrack = track;
            _preview.Enabled = _preflight != null;
            _editTrack.Enabled = _loadedTrack?.Path != null;
            RebuildCardRows(problem);
        }

        private void PreviewTrack()
        {
            if (_preflight == null) return;
            var selected = _cardRows.Where(r => r.mine.SelectedItem is string name && name != NoCard)
                .Select(r => r.card.Name).ToList();
            using (var preview = new TrackPreviewDialog(_preflight, selected)) preview.ShowDialog(this);
        }

        private void EditTrack()
        {
            if (_loadedTrack?.Path == null) return;
            using (var editor = new TrackEditorDialog(_loadedTrack.Path, _preflight?.Carriers, _grind.EditedTrackDir,
                _grind.Cards, (_level.SelectedItem as LevelItem)?.Entry?.ToString()))
            {
                editor.ShowDialog(this);
                if (editor.SavedPath == null) return;
                _track.Text = editor.SavedPath;
                OnTrackChanged();
            }
        }

        private void CreateTrack()
        {
            try
            {
                var level = (_level.SelectedItem as LevelItem)?.Entry;
                if (level == null) throw new InvalidOperationException("Choose a stage before creating a formation.");
                var carriers = CarrierMap.Find(_grind.PresetTrackDir, level, null);
                using (var editor = new TrackEditorDialog(null, carriers, _grind.EditedTrackDir, _grind.Cards, level.ToString()))
                {
                    editor.ShowDialog(this);
                    if (editor.SavedPath == null) return;
                    _track.Text = editor.SavedPath;
                    OnTrackChanged();
                }
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Create a stage formation"); }
        }

        private System.Threading.Tasks.Task LearnInventoryAsync()
        {
            try
            {
                using (var dialog = new InventoryScanDialog(_grind.CaptureInventoryAsync,
                    async (id, items, replace) => { await _grind.LearnInventoryAsync(id, items, replace); },
                    _grind.Cards, _grind.InventoryGridPath)) dialog.ShowDialog(this);
                _loadedTrack = null;
                OnTrackChanged();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Inventory card library"); }
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void RebuildCardRows(string problem = null)
        {
            // Remember what the user picked before throwing the rows away.
            foreach (var (card, mine) in _cardRows) RememberChoice(card.Name, mine);

            _cards.SuspendLayout();
            _cards.Controls.Clear();
            _cards.RowStyles.Clear();
            _cardRows.Clear();

            if (_loadedTrack == null)
            {
                _cardsHint.Text = string.IsNullOrEmpty(_track.Text) ? "先选择轨道" : "轨道读不出来：" + problem;
                _compatible = false;
                _compatibility.Text = problem ?? "Select a track to check compatibility.";
                _start.Enabled = false;
                _cards.ResumeLayout();
                return;
            }

            var learned = _grind.Cards.Names;
            var used = _preflight.DeckCards.GroupBy(c => c.Name).Select(g => g.First()).ToList();
            int row = 0;
            foreach (var card in used)
            {
                var mode = (card.Extreme ? "极" : "") + (card.Fill ? "补" : "");
                _cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _cards.Controls.Add(new Label
                {
                    Text = $"{card.Name}（{card.FormationCells.Distinct().Count()} 格{(mode.Length > 0 ? "·" + mode : "")}）",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 4, 6, 0),
                }, 0, row);
                var mine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                WheelScrollsPanel(mine);
                mine.Items.Add(NoCard);
                mine.Items.AddRange(learned.Cast<object>().ToArray());
                var preferred = _settings.CardMap.TryGetValue(card.Name, out var saved) && learned.Contains(saved) ? saved
                    : learned.Contains(card.Name) ? card.Name
                    : NoCard;
                mine.SelectedItem = preferred;
                mine.SelectedIndexChanged += (s, e) => UpdateCompatibility();
                _cards.Controls.Add(mine, 1, row);
                _cardRows.Add((card, mine));
                row++;
            }
            _cards.ResumeLayout();

            var shovel = _loadedTrack.Opening.Cards.FirstOrDefault(c => c.IsShovel);
            _shovel.Text = shovel != null && shovel.Steps.Count > 0 ? $"执行轨道里的铲子步骤（{shovel.Steps.Count} 处）" : "执行轨道里的铲子步骤（这条轨道没有）";
            var notes = new List<string>();
            if (used.Count == 0) notes.Add("这条轨道没有放卡步骤");
            if (_loadedTrack.Waves.Count > 1) notes.Add($"有 {_loadedTrack.Waves.Count - 1} 个按波变阵段落，目前只执行开局段");
            if (learned.Count == 0) notes.Add("No learned cards yet. Open 背包 and use Scan 背包, or learn a room deck.");
            notes.Add("卡组按上面的顺序组成，选“不带”的卡跳过");
            _cardsHint.Text = string.Join("\n", notes);
            UpdateCompatibility();
        }

        private void UpdateCompatibility()
        {
            if (_preflight == null || _start == null) return;
            var selected = _cardRows.Where(r => r.mine.SelectedItem is string name && name != NoCard).Select(r => r.card.Name);
            var report = _preflight.Inspect(selected, _shovel.Checked, DeckBuilder.DeckSlots);
            _compatible = report.CanStart;
            _compatibility.Text = (_compatible ? "Ready for the supported track subset." : "Cannot start this track.") +
                Environment.NewLine + string.Join(Environment.NewLine, report.Diagnostics.Select(d => d.ToString()));
            _start.Enabled = _compatible && !_grind.Running;
        }

        private void RememberChoice(string trackCard, ComboBox mine)
        {
            if (mine.SelectedItem is string name && name != NoCard) _settings.CardMap[trackCard] = name;
            else _settings.CardMap.Remove(trackCard);
        }

        private async System.Threading.Tasks.Task LearnDeckAsync()
        {
            try
            {
                var (frame, filled) = await _grind.ReadDeckAsync();
                if (filled.Count == 0)
                {
                    MessageBox.Show(this, "卡组是空的。先在房间里手动把要学习的卡放进卡组。", "学习当前卡组");
                    return;
                }
                using (var dialog = new LearnDeckDialog(frame, filled, _grind.Cards.Names))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    foreach (var (slot, name) in dialog.Named) _grind.LearnCard(name, frame, slot);
                    AppendLog($"{DateTime.Now:HH:mm:ss} 学习了 {string.Join("、", dialog.Named.Select(n => n.name))}");
                }
                _loadedTrack = null;
                OnTrackChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "学习当前卡组");
            }
        }

        // ---------- run ----------

        private async System.Threading.Tasks.Task StartAsync()
        {
            SaveSettings();
            var cards = _cardRows
                .Where(r => r.mine.SelectedItem is string name && name != NoCard)
                .Select(r => (track: r.card.Name, mine: (string)r.mine.SelectedItem))
                .ToList();

            if (cards.Count == 0)
            {
                MessageBox.Show(this, "至少给一张轨道卡选上“我的卡”。", "开始刷图");
                return;
            }

            try
            {
                _start.Enabled = false;
                _log.Items.Clear();
                await _grind.StartAsync(new GrindRequest
                {
                    Level = SelectedLevel?.ToString(),
                    Track = _track.Text,
                    Cards = cards,
                    Rounds = (int)_rounds.Value,
                    Shovel = _shovel.Checked,
                    AutoFlame = _autoFlame.Checked,
                    Boss = _boss.Checked,
                });
            }
            catch (Exception ex)
            {
                SetRunning(_grind.Running);
                MessageBox.Show(this, ex.Message, "无法开始");
            }
        }

        private async System.Threading.Tasks.Task ToggleRecordingAsync()
        {
            _record.Enabled = false;
            try
            {
                _recordState.Text = await _toggleRecording();
            }
            catch (Exception ex)
            {
                _recordState.Text = "录制失败：" + ex.Message;
            }
            finally
            {
                _record.Enabled = true;
            }
        }

        /// <summary>Keeps the record button in step with recording started from anywhere (toolbar, API).</summary>
        public void ShowRecording(bool recording)
        {
            _recording = recording;
            _record.Text = recording ? "■ 停止录制" : "● 开始录制";
            _record.ForeColor = recording ? Color.Red : SystemColors.ControlText;
        }

        private async System.Threading.Tasks.Task TrackFromRecordingAsync()
        {
            const string Title = "录制生成轨道";
            if (_recording)
            {
                MessageBox.Show(this, "先停止录制。", Title);
                return;
            }
            var dir = _grind.LatestRecording();
            if (dir == null)
            {
                MessageBox.Show(this, "还没有录制。进房间后点“开始录制”，手动打完一局再停止。", Title);
                return;
            }

            // A learned card that stands in for a preset card (日光炉 for 小火炉) takes that card's layer.
            var standsFor = _settings.CardMap.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);
            (int, int)? Defaults(string name) =>
                _grind.CardDefaults(name) ?? (standsFor.TryGetValue(name, out var preset) ? _grind.CardDefaults(preset) : null);

            var status = _recordState.Text;
            _fromRecording.Enabled = false;
            _recordState.Text = "正在分析录制…";
            try
            {
                var battles = await System.Threading.Tasks.Task.Run(() => _grind.ReadRecording(dir, Defaults));
                var battle = battles.OrderByDescending(b => b.Placed).First();
                if (battle.Placed == 0)
                {
                    MessageBox.Show(this, "录制里没有找到放上的卡。", Title);
                    return;
                }

                var recording = Path.GetFileName(dir);
                var summary = DescribeRecorded(battle, recording, battles.Count, battles.IndexOf(battle) + 1);
                if (MessageBox.Show(this, summary + "\n\n保存为轨道？", Title, MessageBoxButtons.OKCancel) != DialogResult.OK) return;

                var level = SelectedLevel;
                var stamp = recording.StartsWith("rec-") ? recording.Substring(4) : recording;
                Directory.CreateDirectory(_grind.UserTrackDir);
                using (var dialog = new SaveFileDialog
                {
                    Filter = "轨道文件 (*.txt)|*.txt",
                    InitialDirectory = _grind.UserTrackDir,
                    FileName = $"{level?.Name ?? "录制"}-{stamp}.txt",
                    OverwritePrompt = true,
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var comment = $"由录制 {recording} 生成" + (level != null ? $"（{level.Island}/{level.Name}）" : "");
                    EditorText.WriteAllText(dialog.FileName, battle.ToTrackText(comment));
                    var root = SafePresetDir();
                    _track.Text = root != null && dialog.FileName.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                        ? dialog.FileName.Substring(root.Length + 1)
                        : dialog.FileName;
                    _loadedTrack = null;
                    OnTrackChanged();
                    AppendLog($"{DateTime.Now:HH:mm:ss} 生成轨道 {_track.Text}：{string.Join("、", battle.Used.Select(c => $"{c.Name} {c.Cells.Count} 格"))}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title);
            }
            finally
            {
                _recordState.Text = status;
                _fromRecording.Enabled = !_grind.Running;
            }
        }

        private static string DescribeRecorded(RecordedBattle battle, string recording, int battles, int index)
        {
            var lines = new List<string>
            {
                battles > 1 ? $"录制 {recording} 里有 {battles} 局，用放卡最多的第 {index} 局。" : $"录制 {recording}",
                "卡组：" + string.Join("、", battle.Cards.Select(c => c.Name)),
                "人物：" + (battle.Character?.ToString() ?? "没找到放人物的点击，用 11"),
            };
            foreach (var card in battle.Used)
                lines.Add($"{card.Name}：{card.Cells.Count} 格　{string.Join(" ", card.Cells)}");
            if (battle.Shovel.Count > 0) lines.Add($"铲子：{battle.Shovel.Count} 次（按录制时的时间，勾选“执行铲子步骤”才会铲）");
            if (battle.NotPlaced > 0) lines.Add($"没放上的点击 {battle.NotPlaced} 次，已忽略");
            if (battle.Unverified > 0) lines.Add($"{battle.Unverified} 次放卡之后看不到那一格，按放上了算");
            var unlearned = battle.Used.Where(c => !c.Learned).Select(c => c.Name).ToList();
            if (unlearned.Count > 0)
                lines.Add($"{string.Join("、", unlearned)} 没有学习过：生成后在“卡片”里给它选我的卡，或先学习再重新生成");
            lines.Add("格子写法：前一位是行（1-7，从上往下），后一位是列（1-9，从左往右）。");
            return string.Join("\n", lines);
        }

        /// <summary>The boss option names the selected level's key, and is off for levels without a boss.</summary>
        private void UpdateBossOption()
        {
            var level = SelectedLevel;
            bool possible = level == null || level.HasBoss;
            _boss.Text = level == null ? "通关后继续打 boss 关（每局消耗钥匙）"
                : level.HasBoss ? $"通关后继续打 boss 关（每局消耗 1 把{level.Key}）"
                : "这一关没有 boss 关";
            _boss.Enabled = possible && !_grind.Running;
            if (!possible) _boss.Checked = false;
        }

        private void SetRunning(bool running)
        {
            foreach (var c in _inputs) c.Enabled = !running;
            if (!running) UpdateBossOption();
            _start.Enabled = !running && _compatible;
            _stop.Enabled = running;
            _openRun.Enabled = _grind.Current != null;
            if (running) _refresh.Start();
            else _refresh.Stop();
            UpdateState();
        }

        private void UpdateState()
        {
            var loop = _grind.Current;
            _finishRound.Enabled = _grind.Running && _grind.Duo == null && loop != null && !loop.FinishAfterRoundRequested;
            if (loop == null)
            {
                _state.Text = "未开始。刷图期间游戏画面不响应鼠标键盘，可以最小化窗口。";
                return;
            }
            string target = $"{loop.RoundsDone}/{_rounds.Value}";
            string ended;
            switch (loop.State)
            {
                case "finished": ended = "全部完成"; break;
                case "finished-early": ended = "Finished after the current round"; break;
                case "stopped": ended = "已停止"; break;
                case "failed": ended = "出错停止，见日志和记录文件夹里的截图"; break;
                default: ended = loop.State; break;
            }
            _state.Text = _grind.Running
                ? $"刷图中：已完成 {target} 局 · {Describe(loop.LastScreen)}" + (loop.FinishAfterRoundRequested ? " · finishing this round" : "")
                : $"{ended}：完成 {loop.RoundsDone} 局";
        }

        private static string Describe(GameScreen screen)
        {
            switch (screen)
            {
                case GameScreen.Lobby: return "城镇/岛屿地图";
                case GameScreen.WorldMap: return "世界地图";
                case GameScreen.RoomList: return "房间列表";
                case GameScreen.Room: return "房间";
                case GameScreen.Battle: return "战斗中";
                case GameScreen.VictoryDialog: return "胜利";
                case GameScreen.Loot:
                case GameScreen.Settlement: return "结算";
                case GameScreen.TreasureShuffle:
                case GameScreen.TreasurePick:
                case GameScreen.TreasureDone: return "翻宝箱";
                case GameScreen.HealthNotice:
                case GameScreen.EventPopup: return "关闭弹窗";
                default: return "识别中";
            }
        }

        /// <summary>
        /// The wheel over a closed list or the rounds box scrolls the panel, instead of silently
        /// changing the level, track, card or rounds while the user scrolls down.
        /// </summary>
        private void WheelScrollsPanel(Control control)
        {
            control.MouseWheel += (s, e) =>
            {
                if (control is ComboBox box && box.DroppedDown) return;
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
                if (VerticalScroll.Visible)
                    AutoScrollPosition = new Point(0, Math.Max(0, -AutoScrollPosition.Y - e.Delta));
            };
        }

        private void QueueScrollUpdate()
        {
            if (_scrollQueued || !IsHandleCreated) return;
            _scrollQueued = true;
            BeginInvoke((Action)UpdateScrollRange);
        }

        /// <summary>Runs after a layout pass, when the rows have their final heights.</summary>
        private void UpdateScrollRange()
        {
            _scrollQueued = false;
            int needed = Padding.Vertical + _layout.Padding.Vertical;
            foreach (Control c in _layout.Controls)
                needed += (c == _log ? LogMinHeight : c.Height) + c.Margin.Vertical;
            if (AutoScrollMinSize.Height != needed) AutoScrollMinSize = new Size(0, needed);
        }

        private void AppendLog(string line)
        {
            _log.Items.Add(line);
            while (_log.Items.Count > 500) _log.Items.RemoveAt(0);
            _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        }

        private static void OpenFolder(string path)
        {
            if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
        }

        private void OnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refresh.Dispose();
            base.Dispose(disposing);
        }
    }
}
