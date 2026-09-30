using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>Step editor that writes a new private copy and leaves the imported preset untouched.</summary>
    internal sealed class TrackEditorDialog : Form
    {
        private sealed class CardItem
        {
            public TrackCard Card;
            public string Wave;
            public override string ToString() => (Wave == "0" ? "开局" : "第 " + Wave + " 波") +
                "  " + Card.Name + " [" + Card.Layer + "]";
        }

        private readonly TrackEditDocument _document;
        private readonly CarrierMap _carriers;
        private readonly ListBox _cards = new ListBox();
        private readonly ListView _steps = new ListView();
        private readonly TextBox _time = new TextBox();
        private readonly TextBox _cell = new TextBox();
        private readonly TextBox _spec = new TextBox();
        private readonly ComboBox _headerKey = new ComboBox();
        private readonly TextBox _headerValue = new TextBox();
        private readonly TextBox _diagnostics = new TextBox();
        private readonly Button _undo = new Button();
        private readonly Button _redo = new Button();
        private readonly string _saveRoot;

        public TrackEditorDialog(string sourcePath, CarrierMap carriers, string saveRoot = null)
        {
            _saveRoot = saveRoot ?? Path.Combine(AppPaths.DataRoot(), "edited-tracks");
            _document = new TrackEditDocument(sourcePath, EditorText.ReadAllText(sourcePath));
            _carriers = carriers;
            Text = "轨道步骤编辑";
            Font = new Font("Microsoft YaHei UI", 9f);
            Size = new Size(920, 640);
            MinimumSize = new Size(720, 510);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (ActiveControl is TextBox || !e.Control ||
                    (e.KeyCode != Keys.Z && e.KeyCode != Keys.Y)) return;
                if (e.KeyCode == Keys.Z) _document.Undo(); else _document.Redo();
                RefreshView();
                e.SuppressKeyPress = true;
            };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = "选择卡片，再修改时间和格位。撤销与重做只影响当前副本；保存会生成新文件。",
                AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 0);

            var headers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                Margin = new Padding(0, 6, 0, 6) };
            headers.Controls.Add(new Label { Text = "轨道头", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _headerKey.DropDownStyle = ComboBoxStyle.DropDownList;
            _headerKey.Width = 105;
            _headerKey.Items.AddRange(new object[] { "人物1位置", "人物2位置", "优先队列", "放卡限速", "注释" });
            _headerKey.SelectedIndexChanged += (s, e) =>
            {
                var track = _document.Track;
                _headerValue.Text = track.Header.TryGetValue((string)_headerKey.SelectedItem, out var value)
                    ? value : "";
            };
            headers.Controls.Add(_headerKey);
            _headerValue.Width = 250;
            headers.Controls.Add(_headerValue);
            AddButton(headers, "修改轨道头", ChangeHeader);
            root.Controls.Add(headers, 0, 1);
            _headerKey.SelectedIndex = 0;

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 310 };
            _cards.Dock = DockStyle.Fill;
            _cards.SelectedIndexChanged += (s, e) => RefreshSteps();
            split.Panel1.Controls.Add(_cards);
            _steps.Dock = DockStyle.Fill;
            _steps.View = View.Details;
            _steps.FullRowSelect = true;
            _steps.GridLines = true;
            _steps.HideSelection = false;
            _steps.Columns.Add("时间", 100);
            _steps.Columns.Add("格位", 80);
            _steps.Columns.Add("类型", 160);
            _steps.SelectedIndexChanged += (s, e) =>
            {
                if (_steps.SelectedIndices.Count == 0) return;
                var step = SelectedCard?.Steps[_steps.SelectedIndices[0]];
                if (step == null) return;
                _time.Text = step.TimeText;
                _cell.Text = step.Cell.ToString();
            };
            split.Panel2.Controls.Add(_steps);
            root.Controls.Add(split, 0, 2);

            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.ReadOnly = true;
            _diagnostics.Multiline = true;
            _diagnostics.ScrollBars = ScrollBars.Vertical;
            root.Controls.Add(_diagnostics, 0, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            buttons.Controls.Add(new Label { Text = "规格", AutoSize = true, Margin = new Padding(0, 7, 3, 0) });
            _spec.Width = 165;
            buttons.Controls.Add(_spec);
            AddButton(buttons, "修改规格", ChangeSpec);
            buttons.Controls.Add(new Label { Text = "秒", AutoSize = true, Margin = new Padding(0, 7, 3, 0) });
            _time.Width = 55;
            buttons.Controls.Add(_time);
            buttons.Controls.Add(new Label { Text = "格位", AutoSize = true, Margin = new Padding(7, 7, 3, 0) });
            _cell.Width = 40;
            buttons.Controls.Add(_cell);
            AddButton(buttons, "替换", () => ChangeStep(false));
            AddButton(buttons, "追加", () => ChangeStep(true));
            AddButton(buttons, "删除", DeleteStep);
            AddButton(buttons, "上移", () => MoveStep(-1));
            AddButton(buttons, "下移", () => MoveStep(1));
            _undo.Text = "撤销";
            _undo.AutoSize = true;
            _undo.Click += (s, e) => { _document.Undo(); RefreshView(); };
            buttons.Controls.Add(_undo);
            _redo.Text = "重做";
            _redo.AutoSize = true;
            _redo.Click += (s, e) => { _document.Redo(); RefreshView(); };
            buttons.Controls.Add(_redo);
            AddButton(buttons, "预览阵型…", Preview);
            AddButton(buttons, "另存副本…", SaveCopy);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);
            RefreshView();
        }

        public string SavedPath { get; private set; }
        private TrackCard SelectedCard => (_cards.SelectedItem as CardItem)?.Card;

        private static void AddButton(FlowLayoutPanel parent, string label, Action action)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (s, e) => action();
            parent.Controls.Add(button);
        }

        private void RefreshView()
        {
            int previous = SelectedCard?.SourceLine ?? 0;
            var track = _document.Track;
            _cards.BeginUpdate();
            _cards.Items.Clear();
            foreach (var wave in track.Waves)
                foreach (var card in wave.Cards)
                    _cards.Items.Add(new CardItem { Card = card, Wave = wave.Label });
            _cards.SelectedIndex = Enumerable.Range(0, _cards.Items.Count)
                .FirstOrDefault(i => ((CardItem)_cards.Items[i]).Card.SourceLine == previous);
            if (_cards.Items.Count == 0) _cards.SelectedIndex = -1;
            _cards.EndUpdate();
            RefreshSteps();
            _undo.Enabled = _document.CanUndo;
            _redo.Enabled = _document.CanRedo;
            if (_headerKey.SelectedItem is string key && !(_headerValue.Focused))
                _headerValue.Text = track.Header.TryGetValue(key, out var value) ? value : "";
            var report = new TrackPreflight(track, _carriers).Inspect();
            _diagnostics.Text = (report.CanStart ? "Opening track compatible." : "Opening track cannot run yet.") +
                Environment.NewLine + string.Join(Environment.NewLine, report.Diagnostics.Take(10));
        }

        private void ChangeHeader()
        {
            if (!(_headerKey.SelectedItem is string key)) return;
            try { _document.SetHeader(key, _headerValue.Text); RefreshView(); }
            catch (FormatException error)
            { MessageBox.Show(this, error.Message, "轨道头", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RefreshSteps()
        {
            _steps.BeginUpdate();
            _steps.Items.Clear();
            var card = SelectedCard;
            _spec.Text = card == null ? "" : _document.GetSpec(card.SourceLine);
            if (card != null)
                foreach (var step in card.Steps)
                    _steps.Items.Add(new ListViewItem(new[] { step.TimeText + " s", step.Cell.ToString(),
                        step.IsMarker ? "00 定时触发" : step.TimeDeciseconds == 0 ? "阵型" : "定时放置" }));
            _steps.EndUpdate();
        }

        private void ChangeSpec()
        {
            var card = SelectedCard;
            if (card == null) return;
            try { _document.SetSpec(card.SourceLine, _spec.Text.Trim()); RefreshView(); }
            catch (Exception error) when (error is FormatException || error is ArgumentException ||
                error is InvalidOperationException)
            { MessageBox.Show(this, error.Message, "卡片规格", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void MoveStep(int direction)
        {
            var card = SelectedCard;
            if (card == null || _steps.SelectedIndices.Count == 0) return;
            int index = _steps.SelectedIndices[0];
            if (index + direction < 0 || index + direction >= card.Steps.Count) return;
            _document.MoveStep(card.SourceLine, index, direction);
            RefreshView();
        }

        private void ChangeStep(bool append)
        {
            var card = SelectedCard;
            if (card == null) return;
            if (!append && _steps.SelectedIndices.Count == 0) return;
            try
            {
                _document.SetStep(card.SourceLine, append ? card.Steps.Count : _steps.SelectedIndices[0],
                    _time.Text.Trim() + "|" + _cell.Text.Trim());
                RefreshView();
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidOperationException)
            {
                MessageBox.Show(this, error.Message, "轨道步骤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteStep()
        {
            var card = SelectedCard;
            if (card == null || _steps.SelectedIndices.Count == 0) return;
            _document.RemoveStep(card.SourceLine, _steps.SelectedIndices[0]);
            RefreshView();
        }

        private void Preview()
        {
            var track = _document.Track;
            var selected = track.Waves.SelectMany(w => w.Cards).Select(c => c.Name).Distinct();
            using (var preview = new TrackPreviewDialog(new TrackPreflight(track, _carriers), selected))
                preview.ShowDialog(this);
        }

        private void SaveCopy()
        {
            try
            {
                Directory.CreateDirectory(_saveRoot);
                using (var dialog = new SaveFileDialog { Filter = "轨道文件 (*.txt)|*.txt",
                    InitialDirectory = _saveRoot,
                    FileName = Path.GetFileNameWithoutExtension(_document.SourcePath) + "-edited.txt",
                    OverwritePrompt = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    _document.SaveCopy(dialog.FileName, _saveRoot);
                    SavedPath = dialog.FileName;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is InvalidOperationException || error is ArgumentException)
            {
                MessageBox.Show(this, error.Message, "保存轨道副本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
