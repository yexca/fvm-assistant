using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>Formation and step editor that saves a new private track without replacing its source.</summary>
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
        private readonly string _level;
        private readonly TrackBoardControl _board = new TrackBoardControl();
        private readonly Dictionary<string, Bitmap> _icons = new Dictionary<string, Bitmap>();
        private readonly ComboBox _tool = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
        private readonly ComboBox _newCard = new ComboBox { Width = 155 };
        private readonly NumericUpDown _cooldown = new NumericUpDown { Minimum = 0, Maximum = 960, Value = 7, Width = 50 };
        private readonly ComboBox _layer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
        private readonly ComboBox _schedule = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private bool _refreshing;
        private string _lastBoardError;

        public TrackEditorDialog(string sourcePath, CarrierMap carriers, string saveRoot = null,
            CardLibrary library = null, string level = null)
        {
            _saveRoot = saveRoot ?? Path.Combine(AppPaths.DataRoot(), "edited-tracks");
            _document = sourcePath == null ? TrackEditDocument.CreateNew(level) : new TrackEditDocument(sourcePath, EditorText.ReadAllText(sourcePath));
            _carriers = carriers;
            _level = level;
            if (library != null)
                foreach (var name in library.Names)
                {
                    _newCard.Items.Add(name);
                    _icons[name] = library.Get(name).ToBitmap();
                }
            Text = sourcePath == null ? "Create a stage formation" : "Edit track formation";
            Font = new Font("Microsoft YaHei UI", 9f);
            Size = new Size(1240, 800);
            MinimumSize = new Size(1000, 680);
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = (string.IsNullOrWhiteSpace(level) ? "Stage: not selected" : "Stage: " + level) +
                "  |  Select a card, then click or drag across the board. Right-click erases at the current time.\n" +
                "Blue cells show fixed or timed support locations. The board shows all targets, including future placements.",
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

            var add = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            Field(add, "Card", _newCard);
            Field(add, "Cooldown (s)", _cooldown);
            _layer.Items.AddRange(new object[] { "1 - 承载", "2 - 护罩", "3 - 普通", "5 - 冷却", "0 - 铲子" });
            _layer.SelectedIndex = 2;
            add.Controls.Add(_layer);
            _schedule.Items.AddRange(new object[] { "Maintain (补极)", "Timed (独)" });
            _schedule.SelectedIndex = 0;
            add.Controls.Add(_schedule);
            AddButton(add, "Add card", AddCard);
            AddButton(add, "Remove card", RemoveCard);
            if (_carriers != null) AddButton(add, "Add stage support", AddStageSupport);
            _tool.Items.AddRange(new object[] { "Place selected card", "Erase selected card", "人物1位置", "人物2位置" });
            _tool.SelectedIndex = 0;
            add.Controls.Add(_tool);
            root.Controls.Add(add, 0, 2);
            if (_newCard.Items.Count > 0) _newCard.SelectedIndex = 0;

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
            _cards.Dock = DockStyle.Fill;
            _cards.DrawMode = DrawMode.OwnerDrawFixed;
            _cards.ItemHeight = 58;
            _cards.DrawItem += DrawCard;
            _cards.SelectedIndexChanged += (s, e) => { if (!_refreshing) RefreshSteps(); };
            split.Controls.Add(_cards, 0, 0);
            _board.Dock = DockStyle.Fill;
            _board.CellPressed += PaintBoard;
            split.Controls.Add(_board, 1, 0);
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
            split.Controls.Add(_steps, 2, 0);
            root.Controls.Add(split, 0, 3);

            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.ReadOnly = true;
            _diagnostics.Multiline = true;
            _diagnostics.ScrollBars = ScrollBars.Vertical;
            root.Controls.Add(_diagnostics, 0, 4);

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
            root.Controls.Add(buttons, 0, 5);
            Controls.Add(root);
            _time.Text = "000";
            _cell.Text = "11";
            FormClosing += (s, e) =>
            {
                if (_document.CanUndo && SavedPath == null && MessageBox.Show(this, "Discard the unsaved formation edits?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
            };
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

        private void RefreshView(int selectedLine = 0)
        {
            int previous = selectedLine != 0 ? selectedLine : SelectedCard?.SourceLine ?? 0;
            var track = _document.Track;
            _refreshing = true;
            _cards.BeginUpdate();
            _cards.Items.Clear();
            foreach (var wave in track.Waves)
                foreach (var card in wave.Cards)
                    _cards.Items.Add(new CardItem { Card = card, Wave = wave.Label });
            _cards.SelectedIndex = _cards.Items.Count == 0 ? -1 : Enumerable.Range(0, _cards.Items.Count)
                .FirstOrDefault(i => ((CardItem)_cards.Items[i]).Card.SourceLine == previous);
            _cards.EndUpdate();
            _refreshing = false;
            RefreshSteps();
            _undo.Enabled = _document.CanUndo;
            _redo.Enabled = _document.CanRedo;
            if (_headerKey.SelectedItem is string key && !(_headerValue.Focused))
                _headerValue.Text = track.Header.TryGetValue(key, out var value) ? value : "";
            var report = new TrackPreflight(track, _carriers).Inspect(useShovel: true, deckLimit: DeckBuilder.DeckSlots);
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
            var track = _document.Track;
            var wave = track.Waves.FirstOrDefault(w => w.Cards.Any(c => c.SourceLine == card?.SourceLine)) ?? track.Opening;
            _board.SelectedLine = card?.SourceLine ?? 0;
            var shown = wave == track.Opening ? wave.Cards.Select(c => _carriers?.Resolve(c) ?? c).ToList() : wave.Cards;
            _board.ShowFormation(track, shown, _carriers, _icons);
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

        private static void Field(FlowLayoutPanel row, string label, Control control)
        {
            row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 7, 3, 0) });
            row.Controls.Add(control);
        }

        private void DrawCard(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var item = (CardItem)_cards.Items[e.Index];
            int left = e.Bounds.Left + 5;
            if (_icons.TryGetValue(item.Card.Name, out var icon))
            {
                e.Graphics.DrawImage(icon, new Rectangle(left, e.Bounds.Top + 4, 38, 46));
                left += 43;
            }
            var color = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Color.FromArgb(33, 43, 59);
            TextRenderer.DrawText(e.Graphics, item.Card.Name, Font,
                new Rectangle(left, e.Bounds.Top + 7, e.Bounds.Right - left - 4, 23), color,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, (item.Wave == "0" ? "Opening" : "Wave " + item.Wave) +
                " | Layer " + item.Card.Layer + " | " + item.Card.Steps.Count + " steps", Font,
                new Rectangle(left, e.Bounds.Top + 31, e.Bounds.Right - left - 4, 19), color,
                TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private void PaintBoard(Cell cell, MouseButtons button)
        {
            try
            {
                if (_tool.SelectedIndex >= 2)
                {
                    var key = _tool.SelectedIndex == 2 ? "人物1位置" : "人物2位置";
                    var current = _document.Track.Header.TryGetValue(key, out var value) ? value : "";
                    if (button == MouseButtons.Right)
                        _document.SetHeader(key, string.Join(",", current.Split(',').Where(s => s.Trim() != cell.ToString())));
                    else _document.SetHeader(key, cell.ToString());
                }
                else
                {
                    var card = SelectedCard;
                    if (card == null) return;
                    _document.PaintCell(card.SourceLine, cell, _time.Text.Trim(), button == MouseButtons.Right || _tool.SelectedIndex == 1);
                }
                _cell.Text = cell.ToString();
                RefreshView();
                _lastBoardError = null;
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidOperationException)
            {
                if (_lastBoardError != error.Message)
                    MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _lastBoardError = error.Message;
            }
        }

        private void AddCard()
        {
            try
            {
                int layer = new[] { 1, 2, 3, 5, 0 }[_layer.SelectedIndex];
                var name = _newCard.Text.Trim();
                if (layer == 0 && name != "铲子") throw new ArgumentException("Layer 0 is available only for 铲子 in this editor.");
                if (name == "铲子" && layer != 0) throw new ArgumentException("Use layer 0 for 铲子.");
                var spec = _cooldown.Value + "[" + layer + "]" + (_schedule.SelectedIndex == 0 && layer != 0 ? "补极" : "独");
                var line = _document.AddCard(name, spec);
                _time.Text = _schedule.SelectedIndex == 0 && layer != 0 ? "000" : "001";
                RefreshView(line);
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidOperationException)
            { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RemoveCard()
        {
            if (SelectedCard == null) return;
            _document.RemoveCard(SelectedCard.SourceLine);
            RefreshView();
        }

        private void Preview()
        {
            var track = _document.Track;
            var selected = track.Waves.SelectMany(w => w.Cards).Select(c => c.Name).Distinct();
            using (var preview = new TrackPreviewDialog(new TrackPreflight(track, _carriers), selected))
                preview.ShowDialog(this);
        }

        private void AddStageSupport()
        {
            try
            {
                foreach (var support in _carriers.Cards)
                    if (!_document.Track.Opening.Cards.Any(c => c.Layer == 1 && (c.Name == support.Name ||
                        (CarrierMap.IsCloudCarrier(c.Name) && CarrierMap.IsCloudCarrier(support.Name)))))
                        _document.AddCard(support.Name, support.CooldownSec + "[1]继");
                RefreshView();
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidOperationException)
            { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void SaveCopy()
        {
            try
            {
                Directory.CreateDirectory(_saveRoot);
                using (var dialog = new SaveFileDialog { Filter = "轨道文件 (*.txt)|*.txt",
                    InitialDirectory = _saveRoot,
                    FileName = _document.SourcePath == null ? ((_level ?? "formation").Replace('/', '-').Replace('\\', '-') + ".txt") :
                        Path.GetFileNameWithoutExtension(_document.SourcePath) + "-edited.txt",
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var icon in _icons.Values) icon.Dispose();
            base.Dispose(disposing);
        }
    }
}
