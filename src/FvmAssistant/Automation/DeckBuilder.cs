using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// Card icons as they appear in the room (42×50, star badge included so a specific copy is
    /// chosen), stored as "name.png". The deck bar and the 我的防御卡 list draw cards identically.
    /// </summary>
    internal sealed class CardLibrary
    {
        private readonly string _directory;

        public CardLibrary(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        public IReadOnlyList<string> Names =>
            Directory.GetFiles(_directory, "*.png").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();

        public StageFrame Get(string name)
        {
            ValidateName(name);
            var path = Path.Combine(_directory, name + ".png");
            if (!File.Exists(path)) throw new KeyNotFoundException("Card icon not learned: " + name + ". Scan it from 背包 or learn it from a room deck first.");
            using (var bmp = new Bitmap(path))
                return StageFrame.FromBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        }

        public void Save(string name, StageFrame icon)
        {
            ValidateName(name);
            using (var bmp = icon.ToBitmap())
                bmp.Save(Path.Combine(_directory, name + ".png"), ImageFormat.Png);
        }

        public static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith(".") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl) ||
                System.Text.RegularExpressions.Regex.IsMatch(name,
                    @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("Use a non-empty card name that is valid as a Windows file name.");
        }
    }

    /// <summary>
    /// Room card selection: empty the 战斗卡片包 bar, then add cards from 我的防御卡 in the wanted
    /// order. Clicking a deck card removes it and leaves a gap; clicking a list card fills the first gap.
    /// List cards that clash with the deck (same family) or are already in it are drawn grey.
    /// </summary>
    /// <summary>The room was covered (a late login popup, a notice) while the deck was being built.</summary>
    internal sealed class RoomCoveredException : Exception
    {
        public RoomCoveredException() : base("选卡时房间被弹窗挡住了") { }
    }

    internal sealed class DeckBuilder
    {
        public static readonly Size CardSize = new Size(42, 50);

        // Geometry measured on the room screen: card bodies start at x = 391, 439, 487, 535, 583.
        private const int DeckLeft = 388, DeckTop = 47, DeckPitch = 48;
        private const int ListLeft = 386, ListPitch = 49, ListColumns = 11;
        private const int ListTop = 178, ListBottom = 408, ListRight = 916;
        private static readonly Point ListUp = new Point(929, 187);
        private static readonly Point ListDown = new Point(929, 400);
        private static readonly Point Park = new Point(940, 300);

        private const double FilledValue = 0.5;     // deck slot brightness: card ≈0.63-0.75, empty ≈0.39
        private const double AvailableDiff = 6;     // colour icon match ≈0
        private const double PresentDiff = 25;      // same card greyed out ≈12-20; other cards ≥29

        private readonly IGameIo _io;
        private readonly Action<string> _log;
        private readonly Func<StageFrame, bool> _inRoom;

        /// <param name="inRoom">Checked on every frame before acting; a popup covering the room stops the build.</param>
        public DeckBuilder(IGameIo io, Action<string> log, Func<StageFrame, bool> inRoom)
        {
            _io = io;
            _log = log;
            _inRoom = inRoom;
        }

        private StageFrame Grab()
        {
            var frame = _io.Grab();
            if (!_inRoom(frame)) throw new RoomCoveredException();
            return frame;
        }

        public static Rectangle DeckSlot(int slot) =>
            new Rectangle(DeckLeft + DeckPitch * (slot - 1), DeckTop, CardSize.Width, CardSize.Height);

        private static Point Center(Rectangle r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);

        public static bool SlotFilled(StageFrame frame, int slot) => frame.MeanValue(DeckSlot(slot)) > FilledValue;

        /// <summary>Visible deck slots. Removing a card leaves a gap (nothing shifts); a new card fills the first gap.</summary>
        public const int DeckSlots = 11;

        public static List<int> FilledSlots(StageFrame frame) =>
            Enumerable.Range(1, DeckSlots).Where(s => SlotFilled(frame, s)).ToList();

        public static bool DeckMatches(StageFrame frame, IReadOnlyList<StageFrame> icons) =>
            FilledSlots(frame).SequenceEqual(Enumerable.Range(1, icons.Count)) &&
            icons.Select((icon, i) => SlotDiff(frame, i + 1, icon)).All(d => d < AvailableDiff);

        /// <summary>Icon difference at a deck slot, allowing ±2 px (one pixel off costs ≈50).</summary>
        public static double SlotDiff(StageFrame frame, int slot, StageFrame icon)
        {
            var r = DeckSlot(slot);
            double best = double.PositiveInfinity;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    best = Math.Min(best, frame.Crop(new Rectangle(r.X + dx, r.Y + dy, icon.Width, icon.Height)).MeanAbsDiff(icon));
            return best;
        }

        public async Task EnsureAsync(IReadOnlyList<(string name, StageFrame icon)> cards, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
                try
                {
                    await EnsureLoadedDeckAsync(cards, ct).ConfigureAwait(false);
                    return;
                }
                catch (DeckChangedException) when (attempt == 0)
                {
                    _log("Room deck changed while selecting cards; rebuilding once.");
                }
        }

        private sealed class DeckChangedException : InvalidOperationException
        {
            public DeckChangedException() : base("The room loaded extra deck cards during selection.") { }
        }

        private async Task EnsureLoadedDeckAsync(IReadOnlyList<(string name, StageFrame icon)> cards, CancellationToken ct)
        {
            await WaitUntilLoadedAsync(ct).ConfigureAwait(false);
            var icons = cards.Select(c => c.icon).ToList();
            if (DeckMatches(Grab(), icons)) return;

            var filled = FilledSlots(Grab());
            _log($"选卡：清空卡组（第 {string.Join("、", filled)} 槽有卡）");
            for (int attempts = 0; filled.Count > 0; attempts++)
            {
                if (attempts > 15) throw new InvalidOperationException($"清空卡组失败，第 {string.Join("、", filled)} 槽还有卡");
                await _io.ClickAsync(Center(DeckSlot(filled[0]))).ConfigureAwait(false);
                _io.Move(Park);
                await Task.Delay(400, ct).ConfigureAwait(false);
                filled = FilledSlots(Grab());
            }

            for (int i = 0; i < cards.Count; i++)
            {
                var (name, icon) = cards[i];
                var at = await FindInListAsync(name, icon, ct).ConfigureAwait(false);
                await _io.ClickAsync(at).ConfigureAwait(false);
                _io.Move(Park);
                // The card flies from the list into the bar; give the animation time to land.
                double diff = double.PositiveInfinity;
                for (var until = DateTime.UtcNow.AddSeconds(3); DateTime.UtcNow < until;)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                    diff = SlotDiff(Grab(), i + 1, icon);
                    if (diff < AvailableDiff) break;
                }
                // A remembered deck can arrive after an initially empty, stable card bar.
                if (FilledSlots(Grab()).Any(slot => slot > i + 1)) throw new DeckChangedException();
                if (diff >= AvailableDiff)
                    throw new InvalidOperationException($"{name} 没有加进卡组第 {i + 1} 槽（差值 {diff:0.0}）");
                _log($"选卡：{name} → 第 {i + 1} 槽");
            }
            if (!DeckMatches(Grab(), icons)) throw new DeckChangedException();
        }

        /// <summary>
        /// A freshly created room fills its deck bar and card list a moment after it appears; acting
        /// earlier sees an empty deck and an empty list. Wait for the list (scrolled to the top) to
        /// show cards and for both areas to stop changing.
        /// </summary>
        private async Task WaitUntilLoadedAsync(CancellationToken ct)
        {
            var deckBar = new Rectangle(DeckLeft - 5, DeckTop - 5, 540, CardSize.Height + 10);
            var firstRow = new Rectangle(ListLeft, ListTop + 3, ListRight - ListLeft, CardSize.Height);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            StageFrame previous = null;
            int stable = 0;
            bool scrolled = false;
            while (DateTime.UtcNow < until)
            {
                var frame = Grab();
                bool hasCards = frame.MeanValue(firstRow) > 0.5; // cards ≈0.62-0.66, empty cells ≈0.38
                if (!hasCards && !scrolled)
                {
                    // The list may simply be scrolled down to its empty tail.
                    scrolled = true;
                    await ScrollListAsync(ListUp, 40, ct).ConfigureAwait(false);
                    continue;
                }
                var watched = frame.Crop(deckBar);
                stable = previous != null && watched.MeanAbsDiff(previous) < 1 ? stable + 1 : 0;
                previous = watched;
                if (hasCards && stable >= 4) return;
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            throw new TimeoutException("房间的卡组和卡片列表没有加载出来");
        }

        private async Task<Point> FindInListAsync(string name, StageFrame icon, CancellationToken ct)
        {
            await ScrollListAsync(ListUp, 40, ct).ConfigureAwait(false);
            for (int page = 0; page < 40; page++)
            {
                var frame = Grab();
                var (diff, at) = Search(frame, icon);
                if (diff < AvailableDiff) return at;
                if (diff < PresentDiff)
                    throw new InvalidOperationException($"{name} 在列表里是灰的（已在卡组里或与卡组里的卡同系列）");
                if (!await ScrollListAsync(ListDown, 2, ct).ConfigureAwait(false))
                    break; // bottom of the list
            }
            throw new InvalidOperationException($"我的防御卡里找不到 {name}");
        }

        /// <summary>Clicks a scroll arrow up to <paramref name="clicks"/> times; false if the list did not move at all.</summary>
        private async Task<bool> ScrollListAsync(Point arrow, int clicks, CancellationToken ct)
        {
            var view = new Rectangle(ListLeft, ListTop, ListRight - ListLeft, ListBottom - ListTop);
            var before = Grab().Crop(view);
            bool moved = false;
            var last = before;
            for (int i = 0; i < clicks; i++)
            {
                await _io.ClickAsync(arrow).ConfigureAwait(false);
                _io.Move(Park);
                var now = last;
                // After reconnecting, a cached OSR frame can survive the first scroll poll.
                // Wait for the rendered list before treating an unchanged frame as its end.
                for (int poll = 0; poll < 8; poll++)
                {
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    now = Grab().Crop(view);
                    if (now.MeanAbsDiff(last) >= 0.5) break;
                }
                if (now.MeanAbsDiff(last) < 0.5) break; // reached the end
                await Task.Delay(150, ct).ConfigureAwait(false);
                moved = true;
                last = Grab().Crop(view);
            }
            _io.Move(Park);
            return moved;
        }

        /// <summary>Best match of the icon in the visible list: columns are fixed, rows scroll freely.</summary>
        private static (double diff, Point center) Search(StageFrame frame, StageFrame icon)
        {
            double best = double.PositiveInfinity;
            var bestAt = Point.Empty;
            for (int col = 0; col < ListColumns; col++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = ListLeft + ListPitch * col + dx;
                    if (x < 0 || x + CardSize.Width > ListRight) continue;
                    for (int y = ListTop; y + CardSize.Height <= ListBottom; y++)
                    {
                        double d = SampledDiff(frame, icon, x, y, best);
                        if (d < best)
                        {
                            best = d;
                            bestAt = new Point(x + CardSize.Width / 2, y + CardSize.Height / 2);
                        }
                    }
                }
            }
            return (best, bestAt);
        }

        /// <summary>Mean RGB difference on every other pixel, giving up once it cannot beat <paramref name="limit"/>.</summary>
        private static double SampledDiff(StageFrame frame, StageFrame icon, int x0, int y0, double limit)
        {
            const int step = 2;
            int samples = ((icon.Height + 1) / step) * ((icon.Width + 1) / step);
            double budget = limit * 3 * samples;
            long sum = 0;
            var f = frame.Bgra;
            var t = icon.Bgra;
            for (int y = 0; y < icon.Height; y += step)
            {
                int fi = ((y0 + y) * frame.Width + x0) * 4;
                int ti = y * icon.Width * 4;
                for (int x = 0; x < icon.Width; x += step, fi += 4 * step, ti += 4 * step)
                    sum += Math.Abs(f[fi] - t[ti]) + Math.Abs(f[fi + 1] - t[ti + 1]) + Math.Abs(f[fi + 2] - t[ti + 2]);
                if (sum > budget) return double.PositiveInfinity;
            }
            return sum / (3.0 * samples);
        }
    }
}
