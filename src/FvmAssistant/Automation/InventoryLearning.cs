using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>A calibrated grid of native 42x50 card bodies on the visible inventory page.</summary>
    internal sealed class InventoryGrid
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
        public int PitchX { get; set; } = 49;
        public int PitchY { get; set; } = 57;

        public IReadOnlyList<Rectangle> Regions(Size frameSize)
        {
            if (Columns < 1 || Columns > 12 || Rows < 1 || Rows > 8 ||
                PitchX < DeckBuilder.CardSize.Width || PitchX > frameSize.Width ||
                PitchY < DeckBuilder.CardSize.Height || PitchY > frameSize.Height)
                throw new ArgumentException("Use 1-12 columns, 1-8 rows and non-overlapping card spacing.");
            if (X < 0 || Y < 0 || (long)X + (Columns - 1L) * PitchX + DeckBuilder.CardSize.Width > frameSize.Width ||
                (long)Y + (Rows - 1L) * PitchY + DeckBuilder.CardSize.Height > frameSize.Height)
                throw new ArgumentException("The card grid extends outside the captured page. Adjust its origin, spacing or size.");
            var bounds = new Rectangle(Point.Empty, frameSize);
            var regions = new List<Rectangle>();
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Columns; col++)
                {
                    var region = new Rectangle(X + col * PitchX, Y + row * PitchY,
                        DeckBuilder.CardSize.Width, DeckBuilder.CardSize.Height);
                    if (!bounds.Contains(region))
                        throw new ArgumentException("The card grid extends outside the captured page. Adjust its origin, spacing or size.");
                    regions.Add(region);
                }
            return regions;
        }

        /// <summary>Correct a small calibration error only when multiple known icons agree on one offset.</summary>
        public InventoryGrid AlignToKnown(StageFrame frame, IReadOnlyList<StageFrame> icons)
        {
            var size = new Size(frame.Width, frame.Height);
            Regions(size);
            var known = icons.Where(icon => icon.Width == DeckBuilder.CardSize.Width &&
                icon.Height == DeckBuilder.CardSize.Height).ToList();
            if (known.Count < 2) return this;
            int originalMatches = AlignmentMatches(frame, Regions(size), known);
            int bestMatches = originalMatches, winners = 0;
            InventoryGrid best = this;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var candidate = new InventoryGrid { X = X + dx, Y = Y + dy, Columns = Columns,
                        Rows = Rows, PitchX = PitchX, PitchY = PitchY };
                    IReadOnlyList<Rectangle> regions;
                    try { regions = candidate.Regions(size); }
                    catch (ArgumentException) { continue; }
                    int matches = AlignmentMatches(frame, regions, known);
                    if (matches > bestMatches) { bestMatches = matches; best = candidate; winners = 1; }
                    else if (matches == bestMatches) winners++;
                }
            return bestMatches >= 2 && bestMatches > originalMatches && winners == 1 ? best : this;
        }

        private static int AlignmentMatches(StageFrame frame, IReadOnlyList<Rectangle> regions,
            IReadOnlyList<StageFrame> known)
        {
            var matched = new HashSet<int>();
            foreach (var region in regions)
            {
                var crop = frame.Crop(region);
                int index = -1;
                double best = 6;
                for (int i = 0; i < known.Count; i++)
                {
                    double diff = crop.MeanAbsDiff(known[i]);
                    if (diff < best) { best = diff; index = i; }
                }
                if (index >= 0) matched.Add(index);
            }
            return matched.Count;
        }
    }

    internal sealed class InventorySelection
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class InventorySnapshot
    {
        public string Id { get; set; }
        public StageFrame Frame { get; set; }
    }

    /// <summary>Learning uses the reviewed snapshot, never a new frame after the inventory has moved.</summary>
    internal sealed class InventoryLearningSession
    {
        private InventorySnapshot _snapshot;
        private DateTime _expires;

        public InventorySnapshot Capture(StageFrame frame, DateTime now)
        {
            _snapshot = new InventorySnapshot { Id = Guid.NewGuid().ToString("N"), Frame = frame };
            _expires = now.AddMinutes(10);
            return _snapshot;
        }

        public IReadOnlyList<string> Save(string snapshotId, IReadOnlyList<InventorySelection> selections,
            CardLibrary library, bool replace, DateTime now)
        {
            if (_snapshot == null || snapshotId != _snapshot.Id || now >= _expires)
                throw new InvalidOperationException("This inventory capture has expired or was replaced. Capture the page again.");
            if (selections == null || selections.Count < 1 || selections.Count > 96)
                throw new ArgumentException("Select and name between 1 and 96 card icons.");
            var icons = new List<(string name, StageFrame icon)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regions = new HashSet<Point>();
            var bounds = new Rectangle(0, 0, _snapshot.Frame.Width, _snapshot.Frame.Height);
            var existing = new HashSet<string>(library.Names, StringComparer.OrdinalIgnoreCase);
            // Validate every item before writing any icon, including existing-name collisions.
            foreach (var item in selections)
            {
                if (item == null) throw new ArgumentException("A card selection is missing.");
                var name = (item.Name ?? "").Trim();
                CardLibrary.ValidateName(name);
                if (name.IndexOfAny(new[] { ',', '，', '=', ';' }) >= 0)
                    throw new ArgumentException("Card names cannot contain track or mapping separators. Use a space or suffix for variants.");
                var at = new Point(item.X, item.Y);
                var region = new Rectangle(at, DeckBuilder.CardSize);
                if (item.X < 0 || item.Y < 0 || (long)item.X + region.Width > bounds.Width ||
                    (long)item.Y + region.Height > bounds.Height)
                    throw new ArgumentException("A selected icon is outside the captured page.");
                if (!names.Add(name)) throw new ArgumentException("Use a different name for each selected card variant.");
                if (!regions.Add(at)) throw new ArgumentException("The same inventory icon was selected twice.");
                if (!replace && existing.Contains(name))
                    throw new InvalidOperationException("An icon named " + name + " already exists. Enable replacement or use a different name.");
                icons.Add((name, _snapshot.Frame.Crop(region)));
            }
            foreach (var item in icons) library.Save(item.name, item.icon);
            _snapshot = null;
            return icons.Select(i => i.name).ToList();
        }
    }
}
