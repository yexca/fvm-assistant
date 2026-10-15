using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>An attended inventory page scanner. It captures pixels without clicking the game.</summary>
    internal sealed class InventoryScanDialog : Form
    {
        private readonly Func<Task<InventorySnapshot>> _capture;
        private readonly Func<string, IReadOnlyList<InventorySelection>, bool, Task> _save;
        private readonly CardLibrary _library;
        private readonly string _gridPath;
        private readonly FrameView _view = new FrameView();
        private readonly DataGridView _candidates = new DataGridView();
        private readonly NumericUpDown _x = Number(0, 4096, 0);
        private readonly NumericUpDown _y = Number(0, 4096, 0);
        private readonly NumericUpDown _columns = Number(1, 12, 1);
        private readonly NumericUpDown _rows = Number(1, 8, 1);
        private readonly NumericUpDown _pitchX = Number(42, 4096, 49);
        private readonly NumericUpDown _pitchY = Number(50, 4096, 57);
        private readonly ComboBox _pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
        private readonly CheckBox _replace = new CheckBox { Text = "Replace existing names", AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly Button _captureButton = new Button { Text = "Capture page", AutoSize = true };
        private readonly Button _scanButton = new Button { Text = "Scan grid", AutoSize = true, Enabled = false };
        private readonly Button _saveButton = new Button { Text = "Add selected cards", AutoSize = true, Enabled = false };
        private InventorySnapshot _snapshot;
        private bool _busy;

        public InventoryScanDialog(Func<Task<InventorySnapshot>> capture,
            Func<string, IReadOnlyList<InventorySelection>, bool, Task> save, CardLibrary library, string gridPath)
        {
            _capture = capture;
            _save = save;
            _library = library;
            _gridPath = gridPath;
            Text = "Inventory card library";
            Font = new Font("Microsoft YaHei UI", 9f);
            Size = new Size(1160, 750);
            MinimumSize = new Size(900, 640);
            StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top,
                Text = "Open 背包 and its defense-card page in the game, then capture it here.\n" +
                       "Click the top-left of a card body (42 x 50 pixels, including its star badge). Review each crop before adding it." }, 0, 0);

            var calibration = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            _pick.Items.AddRange(new object[] { "First icon origin", "Next column origin", "Next row origin" });
            _pick.SelectedIndex = 0;
            calibration.Controls.Add(_pick);
            AddField(calibration, "X", _x);
            AddField(calibration, "Y", _y);
            AddField(calibration, "Columns", _columns);
            AddField(calibration, "Rows", _rows);
            AddField(calibration, "Column spacing", _pitchX);
            AddField(calibration, "Row spacing", _pitchY);
            root.Controls.Add(calibration, 0, 1);

            var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            _view.Dock = DockStyle.Fill;
            _view.Picked += PickOrigin;
            workspace.Controls.Add(_view, 0, 0);
            _candidates.Dock = DockStyle.Fill;
            _candidates.AllowUserToAddRows = _candidates.AllowUserToDeleteRows = false;
            _candidates.AutoGenerateColumns = false;
            _candidates.RowHeadersVisible = false;
            _candidates.BackgroundColor = Color.White;
            _candidates.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _candidates.MultiSelect = false;
            _candidates.RowTemplate.Height = 58;
            _candidates.Columns.Add(new DataGridViewCheckBoxColumn { Name = "include", HeaderText = "Add", Width = 38 });
            _candidates.Columns.Add(new DataGridViewImageColumn { Name = "icon", HeaderText = "Icon", Width = 50,
                ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true });
            _candidates.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Card / variant name",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _candidates.Columns.Add(new DataGridViewTextBoxColumn { Name = "match", HeaderText = "Review", Width = 105, ReadOnly = true });
            _candidates.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 2)
                    _candidates.Rows[e.RowIndex].Cells[0].Value =
                        !string.IsNullOrWhiteSpace(Convert.ToString(_candidates.Rows[e.RowIndex].Cells[2].Value));
            };
            _candidates.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_candidates.CurrentCell is DataGridViewCheckBoxCell && _candidates.IsCurrentCellDirty)
                    _candidates.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _candidates.SelectionChanged += (s, e) =>
            {
                _view.Highlight = _candidates.SelectedRows.Count > 0 ? (Rectangle?)_candidates.SelectedRows[0].Tag : null;
                _view.Invalidate();
            };
            workspace.Controls.Add(_candidates, 1, 0);
            root.Controls.Add(workspace, 0, 2);
            root.Controls.Add(_status, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _captureButton.Click += async (s, e) => await RunAsync(CapturePageAsync);
            _scanButton.Click += async (s, e) => await RunAsync(() => { ScanGrid(); return Task.CompletedTask; });
            _saveButton.Click += async (s, e) => await RunAsync(SaveCardsAsync);
            actions.Controls.AddRange(new Control[] { _captureButton, _scanButton, _saveButton, _replace,
                new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel } });
            root.Controls.Add(actions, 0, 4);
            Controls.Add(root);
            foreach (var number in new[] { _x, _y, _columns, _rows, _pitchX, _pitchY })
                number.ValueChanged += (s, e) => InvalidateScan();
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
            _status.Text = "Capture one page at a time. New card names and different star variants need manual review.";
            LoadGrid();
        }

        private static NumericUpDown Number(int min, int max, int value) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 64 };

        private static void AddField(FlowLayoutPanel row, string text, Control field)
        {
            row.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(8, 6, 2, 0) });
            row.Controls.Add(field);
        }

        private InventoryGrid Grid => new InventoryGrid { X = (int)_x.Value, Y = (int)_y.Value,
            Columns = (int)_columns.Value, Rows = (int)_rows.Value, PitchX = (int)_pitchX.Value, PitchY = (int)_pitchY.Value };

        private void LoadGrid()
        {
            if (!File.Exists(_gridPath)) return;
            try
            {
                var grid = Json.Deserialize<InventoryGrid>(File.ReadAllText(_gridPath));
                if (grid == null) return;
                var values = new[] { grid.X, grid.Y, grid.Columns, grid.Rows, grid.PitchX, grid.PitchY };
                var fields = new[] { _x, _y, _columns, _rows, _pitchX, _pitchY };
                for (int i = 0; i < fields.Length; i++) fields[i].Value = Math.Min(fields[i].Maximum, Math.Max(fields[i].Minimum, values[i]));
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException)
            { _status.Text = "Saved calibration could not be loaded. Calibrate this page again."; }
        }

        private void PickOrigin(Point at)
        {
            if (_busy || _snapshot == null) return;
            if (_pick.SelectedIndex == 1)
            {
                if (at.X - _x.Value >= _pitchX.Minimum) _pitchX.Value = at.X - _x.Value;
            }
            else if (_pick.SelectedIndex == 2)
            {
                if (at.Y - _y.Value >= _pitchY.Minimum) _pitchY.Value = at.Y - _y.Value;
            }
            else { _x.Value = at.X; _y.Value = at.Y; }
            _view.Regions = new[] { new Rectangle((int)_x.Value, (int)_y.Value, 42, 50) };
            _view.Invalidate();
        }

        private void InvalidateScan()
        {
            ClearCandidates();
            _view.Regions = Array.Empty<Rectangle>();
            _view.Highlight = null;
            _view.Invalidate();
            _saveButton.Enabled = false;
        }

        private async Task CapturePageAsync()
        {
            _snapshot = null;
            InvalidateScan();
            _view.SetFrame(null);
            var snapshot = await _capture();
            _snapshot = snapshot;
            _view.SetFrame(snapshot.Frame);
            _status.Text = "Capture ready. Set the first icon, spacing and grid size, then scan. Empty cells should stay unchecked.";
        }

        private void ScanGrid()
        {
            if (_snapshot == null) return;
            var known = _library.Names.Select(n => (name: n, icon: _library.Get(n)))
                .Where(k => k.icon.Width == 42 && k.icon.Height == 50).ToList();
            var original = Grid;
            var grid = original.AlignToKnown(_snapshot.Frame, known.Select(k => k.icon).ToList());
            _x.Value = grid.X;
            _y.Value = grid.Y;
            var regions = grid.Regions(new Size(_snapshot.Frame.Width, _snapshot.Frame.Height));
            ClearCandidates();
            foreach (var region in regions)
            {
                var icon = _snapshot.Frame.Crop(region);
                var matches = known.Select(k => (k.name, diff: icon.MeanAbsDiff(k.icon))).OrderBy(k => k.diff).ToList();
                var matched = matches.Count > 0 && matches[0].diff < 6 ? matches[0].name : "";
                int index = _candidates.Rows.Add(false, icon.ToBitmap(), matched,
                    matched.Length > 0 ? "Known icon" : "Review and name");
                _candidates.Rows[index].Tag = region;
            }
            _view.Regions = regions;
            _view.Invalidate();
            Directory.CreateDirectory(Path.GetDirectoryName(_gridPath));
            File.WriteAllText(_gridPath, Json.Serialize(Grid));
            string alignment = grid.X != original.X || grid.Y != original.Y
                ? "Grid aligned by " + (grid.X - original.X) + ", " + (grid.Y - original.Y) + " pixels using known icons. " : "";
            _status.Text = alignment + regions.Count + " candidate crops. Check and name the icons to add. No OCR names are inferred.";
        }

        private async Task SaveCardsAsync()
        {
            _candidates.EndEdit();
            var items = new List<InventorySelection>();
            foreach (DataGridViewRow row in _candidates.Rows)
            {
                if (!Convert.ToBoolean(row.Cells[0].Value)) continue;
                var region = (Rectangle)row.Tag;
                items.Add(new InventorySelection { Name = Convert.ToString(row.Cells[2].Value).Trim(), X = region.X, Y = region.Y });
            }
            if (items.Count == 0) throw new InvalidOperationException("Check and name at least one card to add.");
            await _save(_snapshot.Id, items, _replace.Checked);
            _snapshot = null;
            InvalidateScan();
            _status.Text = "Added " + items.Count + " card icons. Turn the inventory page manually and capture again to add more.";
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_busy) return;
            _busy = true;
            _captureButton.Enabled = _scanButton.Enabled = _saveButton.Enabled = false;
            try { await action(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally
            {
                _busy = false;
                _captureButton.Enabled = true;
                _scanButton.Enabled = _snapshot != null;
                _saveButton.Enabled = _snapshot != null && _candidates.Rows.Count > 0;
            }
        }

        private void ClearCandidates()
        {
            foreach (DataGridViewRow row in _candidates.Rows) (row.Cells[1].Value as Image)?.Dispose();
            _candidates.Rows.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearCandidates();
            base.Dispose(disposing);
        }

        private sealed class FrameView : Control
        {
            private Bitmap _image;
            public IReadOnlyList<Rectangle> Regions { get; set; } = Array.Empty<Rectangle>();
            public Rectangle? Highlight { get; set; }
            public event Action<Point> Picked;
            public FrameView() { DoubleBuffered = true; BackColor = Color.FromArgb(33, 43, 59); Cursor = Cursors.Cross; }
            public void SetFrame(StageFrame frame) { _image?.Dispose(); _image = frame?.ToBitmap(); Invalidate(); }
            private RectangleF Viewport()
            {
                if (_image == null || ClientSize.Width == 0 || ClientSize.Height == 0) return RectangleF.Empty;
                float scale = Math.Min(ClientSize.Width / (float)_image.Width, ClientSize.Height / (float)_image.Height);
                return new RectangleF((ClientSize.Width - _image.Width * scale) / 2,
                    (ClientSize.Height - _image.Height * scale) / 2, _image.Width * scale, _image.Height * scale);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var area = Viewport();
                if (area.IsEmpty) return;
                e.Graphics.DrawImage(_image, area);
                using (var pen = new Pen(Color.Orange, 2))
                    foreach (var region in Regions)
                        e.Graphics.DrawRectangle(pen, area.Left + region.X * area.Width / _image.Width,
                            area.Top + region.Y * area.Height / _image.Height,
                            region.Width * area.Width / _image.Width, region.Height * area.Height / _image.Height);
                if (Highlight.HasValue)
                {
                    var region = Highlight.Value;
                    using (var pen = new Pen(Color.Lime, 3))
                        e.Graphics.DrawRectangle(pen, area.Left + region.X * area.Width / _image.Width,
                            area.Top + region.Y * area.Height / _image.Height,
                            region.Width * area.Width / _image.Width, region.Height * area.Height / _image.Height);
                }
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                var area = Viewport();
                if (_image == null || !area.Contains(e.Location) || e.Button != MouseButtons.Left) return;
                Picked?.Invoke(new Point((int)((e.X - area.Left) * _image.Width / area.Width),
                    (int)((e.Y - area.Top) * _image.Height / area.Height)));
            }
            protected override void Dispose(bool disposing) { if (disposing) _image?.Dispose(); base.Dispose(disposing); }
        }
    }
}
