using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>Read-only opening and wave preview from the same track snapshot used by preflight.</summary>
    internal sealed class TrackPreviewDialog : Form
    {
        private readonly TrackPreflight _preflight;
        private readonly HashSet<string> _selected;
        private readonly ComboBox _waves;
        private readonly TableLayoutPanel _board;
        private readonly ListView _timeline;
        private readonly TextBox _details;
        private readonly ToolTip _tips = new ToolTip();

        public TrackPreviewDialog(TrackPreflight preflight, IEnumerable<string> selected)
        {
            _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
            _selected = new HashSet<string>(selected ?? Enumerable.Empty<string>());
            Text = "轨道阵型与时间线";
            Font = new Font("Microsoft YaHei UI", 9f);
            Size = new Size(1080, 650);
            MinimumSize = new Size(850, 530);
            StartPosition = FormStartPosition.CenterParent;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            top.Controls.Add(new Label { Text = "波次", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            _waves = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            foreach (var wave in preflight.Track.Waves)
                _waves.Items.Add(wave.Label == "0" ? "开局" : "第 " + wave.Label + " 波");
            _waves.SelectedIndexChanged += (s, e) => Rebuild();
            top.Controls.Add(_waves);
            top.Controls.Add(new Label
            {
                Text = "格子显示目标阵型；时间线保留 00 定时触发项。灰色表示当前未带的卡。",
                AutoSize = true, Margin = new Padding(15, 6, 0, 0), ForeColor = SystemColors.GrayText
            });
            root.Controls.Add(top, 0, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1000, 480), SplitterDistance = 590 };
            _board = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, RowCount = 8, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            _board.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
            for (int col = 1; col <= 9; col++) _board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 9));
            _board.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            for (int row = 1; row <= 7; row++) _board.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 7));
            split.Panel1.Controls.Add(_board);

            _timeline = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                GridLines = true, HideSelection = false };
            _timeline.Columns.Add("时间", 72);
            _timeline.Columns.Add("卡片", 120);
            _timeline.Columns.Add("位置", 52);
            _timeline.Columns.Add("含义", 140);
            split.Panel2.Controls.Add(_timeline);
            root.Controls.Add(split, 0, 1);

            _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
            root.Controls.Add(_details, 0, 2);
            Controls.Add(root);
            if (_waves.Items.Count > 0) _waves.SelectedIndex = 0;
        }

        private IReadOnlyList<TrackCard> CurrentCards()
        {
            if (_waves.SelectedIndex == 0)
                return _preflight.DeckCards.Concat(_preflight.Track.Opening.Cards.Where(c => c.IsShovel || c.IsGem)).ToList();
            return _preflight.Track.Waves[_waves.SelectedIndex].Cards;
        }

        private void Rebuild()
        {
            if (_waves.SelectedIndex < 0) return;
            var cards = CurrentCards();
            var cells = cards.SelectMany(c => c.FormationCells.Distinct().Select(cell => (cell, card: c)))
                .GroupBy(pair => pair.cell.ToString()).ToDictionary(g => g.Key, g => g.Select(p => p.card).ToList());

            _board.SuspendLayout();
            foreach (Control old in _board.Controls.Cast<Control>().ToList()) old.Dispose();
            _board.Controls.Clear();
            _board.Controls.Add(new Label { Text = "", Dock = DockStyle.Fill }, 0, 0);
            for (int col = 1; col <= 9; col++)
                _board.Controls.Add(new Label { Text = col.ToString(), Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter }, col, 0);
            for (int row = 1; row <= 7; row++)
            {
                _board.Controls.Add(new Label { Text = row.ToString(), Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter }, 0, row);
                for (int col = 1; col <= 9; col++)
                {
                    var cell = new Cell(row, col);
                    var at = cell.ToString();
                    var names = cells.TryGetValue(at, out var found) ? found : new List<TrackCard>();
                    var active = names.Where(c => _selected.Contains(c.Name)).ToList();
                    var shown = active.Count > 0 ? active : names;
                    var tile = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                        Margin = Padding.Empty, AutoEllipsis = true, BorderStyle = BorderStyle.FixedSingle,
                        BackColor = active.Count > 1 ? Color.Khaki : active.Count == 1 ? LayerColor(active[0].Layer)
                            : names.Count > 0 ? Color.Gainsboro : SystemColors.Window,
                        Text = names.Count == 0 ? at : at + "\n" +
                            string.Join("/", shown.Take(2).Select(c => c.Name.Length > 3 ? c.Name.Substring(0, 3) : c.Name)) +
                            (shown.Count > 2 ? "+" + (shown.Count - 2) : "") };
                    _tips.SetToolTip(tile, names.Count == 0 ? at : at + ": " +
                        string.Join("; ", names.Select(c => c.Name + " [" + c.Layer + "]" +
                            (_selected.Contains(c.Name) ? "" : " (skipped)"))));
                    _board.Controls.Add(tile, col, row);
                }
            }
            _board.ResumeLayout();

            _timeline.BeginUpdate();
            _timeline.Items.Clear();
            foreach (var item in cards.SelectMany(c => c.Steps.Select(s => (card: c, step: s)))
                .OrderBy(p => p.step.TimeDeciseconds).ThenBy(p => p.card.SourceLine))
            {
                var meaning = item.step.IsMarker ? "定时补阵触发" : item.step.TimeDeciseconds == 0 ? "阵型" :
                    item.card.Extreme ? "开始 / 阵型" : item.card.Fill ? "定时补阵" : "定时放置";
                var line = new ListViewItem(new[] { item.step.TimeText + "s", item.card.Name,
                    item.step.Cell.ToString(), meaning });
                if (!_selected.Contains(item.card.Name) && !item.card.IsShovel) line.ForeColor = SystemColors.GrayText;
                _timeline.Items.Add(line);
            }
            _timeline.EndUpdate();

            var wave = _preflight.Track.Waves[_waves.SelectedIndex];
            _details.Text = (wave.Label == "0" ? "Opening" : "Wave " + wave.Label + " (execution currently unsupported)") +
                ": " + cards.Count + " cards, " + cells.Count + " formation cells, " +
                cards.Sum(c => c.Steps.Count) + " timeline entries." + Environment.NewLine +
                "The preview shows track instructions, not whether the selected deck can win or whether a card was placed.";
        }

        private static Color LayerColor(int layer)
        {
            switch (layer)
            {
                case 1: return Color.LightSkyBlue;
                case 2: return Color.PaleGreen;
                case 3: return Color.PeachPuff;
                case 5: return Color.Plum;
                default: return Color.LightGray;
            }
        }
    }
}
