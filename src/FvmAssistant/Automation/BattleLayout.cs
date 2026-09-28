using System;
using System.Drawing;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// Battle screen geometry in stage pixels, measured from a recorded 曲奇岛 run (land map).
    /// The card bar is left-aligned, so slot positions only depend on the slot index.
    /// </summary>
    internal sealed class BattleLayout
    {
        // Cell borders of the 9×7 grid (the 7th row is unused on 6-row land maps like 曲奇岛).
        private static readonly int[] ColEdges = { 304, 364, 423, 481, 540, 600, 661, 721, 783, 841 };
        private static readonly int[] RowEdges = { 110, 174, 236, 299, 364, 428, 492, 556 };

        private const double SlotPitch = 52.8;
        private const int FirstSlotCenterX = 241;
        private const int SlotClickY = 45;

        /// <summary>
        /// A slot is ready (cooled down and affordable) when its white cost label is lit (≈0.93).
        /// Greyed out (cooling, unaffordable, or the card is in hand) it reads ≈0.52.
        /// </summary>
        public const double ReadyValue = 0.88;

        public BattleLayout(int slotCount, Point? shovel = null)
        {
            if (slotCount < 1 || slotCount > 18) throw new ArgumentOutOfRangeException(nameof(slotCount));
            SlotCount = slotCount;
            // With 5 slots the shovel sits at (515,45); it follows the last slot.
            Shovel = shovel ?? new Point((int)Math.Round(251 + SlotPitch * slotCount), SlotClickY);
        }

        public int SlotCount { get; }
        public Point Shovel { get; }

        /// <summary>
        /// Off-grid spot left of the field where the cursor parks: away from the cells, and a card in
        /// hand (drawn at the cursor) stays off the card bar's cost labels. What lies here depends on
        /// the map (曲奇's shelves, 深渊's animated water), so nothing is read from it.
        /// </summary>
        public Point Neutral => new Point(130, 250);

        /// <summary>Held-card capture around the neutral pointer, away from the board and card bar.</summary>
        public Rectangle HeldCardRegion => new Rectangle(Neutral.X - 60, Neutral.Y - 65, 120, 90);

        /// <summary>Keep color matching inside the cell so an adjacent copy cannot mask a missing card.</summary>
        public Rectangle CardRegion(Cell cell) => CellInterior(cell);

        public Point CellCenter(Cell cell)
        {
            if (cell.Row < 1 || cell.Row > 7 || cell.Col < 1 || cell.Col > 9)
                throw new ArgumentOutOfRangeException(nameof(cell), cell.ToString());
            return new Point((ColEdges[cell.Col - 1] + ColEdges[cell.Col]) / 2, (RowEdges[cell.Row - 1] + RowEdges[cell.Row]) / 2);
        }

        /// <summary>A cell without its border strip, where neighbouring cards and the grid lines do not reach.</summary>
        public Rectangle CellInterior(Cell cell)
        {
            var c = CellCenter(cell);
            int w = ColEdges[cell.Col] - ColEdges[cell.Col - 1] - 24, h = RowEdges[cell.Row] - RowEdges[cell.Row - 1] - 24;
            return new Rectangle(c.X - w / 2, c.Y - h / 2, w, h);
        }

        /// <summary>
        /// Where flames and dropped items can lie: the grid plus the strip right of column 9 where
        /// mice are often defeated.
        /// </summary>
        public Rectangle Pickups => new Rectangle(ColEdges[0], RowEdges[0], 880 - ColEdges[0], RowEdges[RowEdges.Length - 1] - RowEdges[0]);

        /// <summary>Left and right edge of a grid column (1-9).</summary>
        public static (int left, int right) ColumnSpan(int col) => (ColEdges[col - 1], ColEdges[col]);

        /// <summary>The grid cell under a stage point, or null off the grid.</summary>
        public static Cell? CellAt(Point p)
        {
            int col = Array.FindLastIndex(ColEdges, e => e <= p.X) + 1;
            int row = Array.FindLastIndex(RowEdges, e => e <= p.Y) + 1;
            return col >= 1 && col <= 9 && row >= 1 && row <= 7 ? new Cell(row, col) : (Cell?)null;
        }

        /// <summary>The card slot (1..<see cref="SlotCount"/>) whose icon is under a stage point, 0 for none.</summary>
        public int SlotAt(Point p)
        {
            if (p.Y < 10 || p.Y > 90) return 0;
            int slot = (int)Math.Round((p.X - FirstSlotCenterX) / SlotPitch) + 1;
            return slot >= 1 && slot <= SlotCount && Math.Abs(p.X - SlotCenter(slot).X) <= 26 ? slot : 0;
        }

        /// <summary>Whether a stage point is on the shovel button (its box is wider than a card).</summary>
        public bool OnShovel(Point p) => p.Y >= 10 && p.Y <= 90 && Math.Abs(p.X - Shovel.X) <= 35;

        public Point SlotCenter(int slot)
        {
            CheckSlot(slot);
            return new Point((int)Math.Round(FirstSlotCenterX + SlotPitch * (slot - 1)), SlotClickY);
        }

        public Rectangle SlotLabel(int slot)
        {
            CheckSlot(slot);
            return new Rectangle(220 + (int)(SlotPitch * (slot - 1)), 65, 38, 9);
        }

        public double SlotValue(StageFrame frame, int slot) => frame.MeanValue(SlotLabel(slot));

        public bool SlotReady(StageFrame frame, int slot) => SlotValue(frame, slot) >= ReadyValue;

        private void CheckSlot(int slot)
        {
            if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} of {SlotCount}");
        }
    }
}
