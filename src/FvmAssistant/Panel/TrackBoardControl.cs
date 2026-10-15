using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FvmAssistant.Automation;

namespace FvmAssistant.Panel
{
    /// <summary>The same seven-by-nine formation board is used for editing and controller previews.</summary>
    internal sealed class TrackBoardControl : Control
    {
        private readonly ToolTip _tips = new ToolTip();
        private string _hover;
        private Cell? _painted;
        private IReadOnlyList<TrackCard> _cards = Array.Empty<TrackCard>();
        private IReadOnlyList<Cell> _player1 = Array.Empty<Cell>();
        private IReadOnlyList<Cell> _player2 = Array.Empty<Cell>();
        private CarrierMap _carriers;
        private IReadOnlyDictionary<string, Bitmap> _icons;
        public int SelectedLine { get; set; }
        public event Action<Cell, MouseButtons> CellPressed;

        public TrackBoardControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(246, 248, 251);
            MinimumSize = new Size(300, 210);
            AccessibleName = "Formation board, rows 1-7 and columns 1-9";
        }

        public void ShowFormation(TrackFile track, IReadOnlyList<TrackCard> cards, CarrierMap carriers,
            IReadOnlyDictionary<string, Bitmap> icons = null)
        {
            _cards = cards ?? Array.Empty<TrackCard>();
            _player1 = track?.Player1Cells ?? Array.Empty<Cell>();
            _player2 = track != null && track.Header.TryGetValue("人物2位置", out var positions)
                ? positions.Split(',').Where(s => s.Trim().Length == 2 && s.Trim()[0] >= '1' &&
                    s.Trim()[0] <= '7' && s.Trim()[1] >= '1' && s.Trim()[1] <= '9')
                    .Select(s => new Cell(s.Trim()[0] - '0', s.Trim()[1] - '0')).ToList() : (IReadOnlyList<Cell>)Array.Empty<Cell>();
            _carriers = carriers;
            _icons = icons;
            _hover = null;
            _tips.SetToolTip(this, "");
            Invalidate();
        }

        private Rectangle BoardArea => new Rectangle(25, 24, Math.Max(9, ClientSize.Width - 30), Math.Max(7, ClientSize.Height - 29));
        private Rectangle Tile(Cell cell)
        {
            var area = BoardArea;
            int left = area.Left + (cell.Col - 1) * area.Width / 9;
            int top = area.Top + (cell.Row - 1) * area.Height / 7;
            return new Rectangle(left, top, area.Left + cell.Col * area.Width / 9 - left,
                area.Top + cell.Row * area.Height / 7 - top);
        }

        internal Cell? CellAt(Point point)
        {
            var area = BoardArea;
            if (!area.Contains(point)) return null;
            return new Cell(((point.Y - area.Top + 1) * 7 - 1) / area.Height + 1,
                ((point.X - area.Left + 1) * 9 - 1) / area.Width + 1);
        }

        private List<TrackCard> CardsAt(Cell cell) => _cards.Where(c => c.Steps.Any(s =>
            s.HasCell && s.Cell.Row == cell.Row && s.Cell.Col == cell.Col)).OrderBy(c => c.Layer).ToList();
        private static bool Contains(IEnumerable<Cell> cells, Cell cell) => cells.Any(c => c.Row == cell.Row && c.Col == cell.Col);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var area = BoardArea;
            for (int col = 1; col <= 9; col++)
                TextRenderer.DrawText(e.Graphics, col.ToString(), Font,
                    new Rectangle(area.Left + (col - 1) * area.Width / 9, 0, area.Width / 9, 22),
                    Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (int row = 1; row <= 7; row++)
            {
                TextRenderer.DrawText(e.Graphics, row.ToString(), Font,
                    new Rectangle(0, area.Top + (row - 1) * area.Height / 7, 22, area.Height / 7),
                    Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                for (int col = 1; col <= 9; col++)
                {
                    var cell = new Cell(row, col);
                    var tile = Tile(cell);
                    var cards = CardsAt(cell);
                    bool support = _carriers != null && _carriers.Cards.Any(c => Contains(c.FormationCells, cell));
                    bool selected = cards.Any(c => c.SourceLine == SelectedLine);
                    using (var brush = new SolidBrush(selected ? Color.FromArgb(255, 235, 211) :
                        support ? Color.FromArgb(219, 237, 248) : Color.White)) e.Graphics.FillRectangle(brush, tile);
                    using (var pen = new Pen(selected ? Color.FromArgb(221, 102, 45) : Color.FromArgb(210, 218, 227), selected ? 2 : 1))
                        e.Graphics.DrawRectangle(pen, tile.X + 1, tile.Y + 1, tile.Width - 2, tile.Height - 2);
                    bool p1 = Contains(_player1, cell), p2 = Contains(_player2, cell);
                    TextRenderer.DrawText(e.Graphics, cell.ToString(), Font, new Point(tile.X + 3, tile.Y + 2), Color.SlateGray,
                        TextFormatFlags.NoPadding);
                    if (cards.Count == 0)
                    {
                        if (p1 || p2)
                            TextRenderer.DrawText(e.Graphics, p1 && p2 ? "P1/P2" : p1 ? "P1" : "P2", Font,
                                new Rectangle(tile.Left + 3, tile.Bottom - 17, tile.Width - 6, 16), Color.FromArgb(30, 104, 77),
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                        continue;
                    }
                    var displayed = cards.FirstOrDefault(c => c.SourceLine == SelectedLine) ?? cards.Last();
                    if (_icons != null && _icons.TryGetValue(displayed.Name, out var icon))
                    {
                        int height = Math.Min(50, tile.Height - 6);
                        int width = Math.Min(tile.Width - 8, height * 42 / 50);
                        if (width > 0 && height > 0)
                            e.Graphics.DrawImage(icon, new Rectangle(tile.Right - width - 3, tile.Top + (tile.Height - height) / 2, width, height));
                    }
                    else
                        TextRenderer.DrawText(e.Graphics, displayed.Name, Font,
                            new Rectangle(tile.Left + 3, tile.Top + 20, tile.Width - 6, Math.Max(10, tile.Height - 36)),
                            Color.FromArgb(33, 43, 59), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (cards.Count > 1)
                        TextRenderer.DrawText(e.Graphics, "+" + (cards.Count - 1), Font,
                            new Rectangle(tile.Right - 26, tile.Top + 2, 24, 16), Color.DarkOrange,
                            TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    // Compact previews still need a readable cell label over a larger thumbnail.
                    using (var labelBrush = new SolidBrush(selected ? Color.FromArgb(255, 235, 211) :
                        support ? Color.FromArgb(219, 237, 248) : Color.White))
                        e.Graphics.FillRectangle(labelBrush, tile.Left + 3, tile.Top + 2, 20, Font.Height);
                    TextRenderer.DrawText(e.Graphics, cell.ToString(), Font, new Point(tile.Left + 3, tile.Top + 2), Color.SlateGray,
                        TextFormatFlags.NoPadding);
                    if (p1 || p2)
                        TextRenderer.DrawText(e.Graphics, p1 && p2 ? "P1/P2" : p1 ? "P1" : "P2", Font,
                            new Rectangle(tile.Left + 3, tile.Bottom - 17, tile.Width - 6, 16), Color.FromArgb(30, 104, 77),
                            TextFormatFlags.NoPadding);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _painted = null;
            Capture = CellPressed != null && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right);
            Press(e);
        }

        private void Press(MouseEventArgs e)
        {
            var cell = CellAt(e.Location);
            if (!cell.HasValue || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) ||
                (_painted.HasValue && _painted.Value.Equals(cell.Value))) return;
            var previous = _painted;
            _painted = cell;
            // Mouse events can jump across several cells during a fast drag.
            foreach (var crossed in CellsBetween(previous ?? cell.Value, cell.Value))
                if (!previous.HasValue || !crossed.Equals(previous.Value))
                    CellPressed?.Invoke(crossed, e.Button);
        }

        private static IEnumerable<Cell> CellsBetween(Cell from, Cell to)
        {
            int col = from.Col, row = from.Row;
            int dx = Math.Abs(to.Col - col), dy = -Math.Abs(to.Row - row);
            int stepCol = col < to.Col ? 1 : -1, stepRow = row < to.Row ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                yield return new Cell(row, col);
                if (col == to.Col && row == to.Row) yield break;
                int twiceError = 2 * error;
                if (twiceError >= dy) { error += dy; col += stepCol; }
                if (twiceError <= dx) { error += dx; row += stepRow; }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && CellPressed != null) { Press(e); return; }
            var cell = CellAt(e.Location);
            var key = cell?.ToString();
            if (_hover == key) return;
            _hover = key;
            var cards = cell.HasValue ? CardsAt(cell.Value) : new List<TrackCard>();
            _tips.SetToolTip(this, cell.HasValue ? key + ": " + string.Join("; ", cards.Select(c => c.Name + " [" + c.Layer + "] " +
                string.Join(", ", c.Steps.Where(s => s.Cell.Row == cell.Value.Row && s.Cell.Col == cell.Value.Col).Select(s => s.TimeText + "s")))) : "");
        }

        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; _painted = null; base.OnMouseUp(e); }
        protected override void Dispose(bool disposing) { if (disposing) _tips.Dispose(); base.Dispose(disposing); }
    }
}
