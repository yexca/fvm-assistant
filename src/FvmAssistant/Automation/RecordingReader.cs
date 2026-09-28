using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FvmAssistant.Automation
{
    /// <summary>A deck card of a recorded battle and the cells it was placed on, first placement first.</summary>
    internal sealed class RecordedCard
    {
        public int Slot { get; set; }
        public string Name { get; set; }

        /// <summary>False when the deck icon matched no learned card; <see cref="Name"/> is then "卡N".</summary>
        public bool Learned { get; set; }

        public int CooldownSec { get; set; } = 7;
        public int Layer { get; set; } = 3;
        public List<Cell> Cells { get; } = new List<Cell>();
    }

    /// <summary>One battle of a recording, read back as a fill formation.</summary>
    internal sealed class RecordedBattle
    {
        /// <summary>Recording time of the first click on the battle screen.</summary>
        public double StartSec { get; set; }

        /// <summary>Every deck slot in order, including cards that were never placed.</summary>
        public List<RecordedCard> Cards { get; } = new List<RecordedCard>();

        public Cell? Character { get; set; }
        public List<TrackStep> Shovel { get; } = new List<TrackStep>();

        /// <summary>Clicks on a cell with a card in hand whose card never showed up there.</summary>
        public int NotPlaced { get; set; }

        /// <summary>Placements kept without proof because no later frame showed the cell.</summary>
        public int Unverified { get; set; }

        /// <summary>One line per click, for checking the reading against the frames.</summary>
        public List<string> Log { get; } = new List<string>();

        public IEnumerable<RecordedCard> Used => Cards.Where(c => c.Cells.Count > 0);
        public int Placed => Cards.Sum(c => c.Cells.Count);

        /// <summary>
        /// 轨道编辑器 track text: each used card fills its cells in the order they were first placed
        /// (补极, the same mode as the editor's formation tracks); shovel clicks keep their time.
        /// </summary>
        public string ToTrackText(string comment)
        {
            var used = Used.ToList();
            var text = new StringBuilder();
            void Line(string s) => text.Append(s).Append("\r\n");

            Line("轨道版本号=v6.2");
            Line("注释=" + (comment ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
            Line("人物1位置=" + (Character ?? new Cell(1, 1)));
            Line("魔塔层数=0");
            Line("退出时间=0");
            Line("放卡限速=0");
            Line("刷技能模式=0");
            Line("移动板块=0");
            Line("优先队列=");
            Line("卡槽数=" + used.Count.ToString(CultureInfo.InvariantCulture));
            Line("宝石\t120[0]");
            Line("铲子\t0[0]独" + string.Concat(Shovel.Select(s => $"\t{s.EditorTimeText}|{s.Cell}")));
            foreach (var card in used)
                Line($"{card.Name}\t{card.CooldownSec}[{card.Layer}]补极" + string.Concat(card.Cells.Select(c => $"\t000|{c}")));
            return text.ToString();
        }
    }

    /// <summary>
    /// Reads a manual-play recording (<see cref="FvmAssistant.Recorder"/>: events.jsonl plus a stage frame
    /// at every press) back as tracks, one per battle.
    /// <list type="bullet">
    /// <item>The deck comes from the last room frame before the battle (the 开始 click), matched
    /// against the learned card icons; its size fixes where the shovel sits.</item>
    /// <item>A click on card slot k followed by a click on a cell is a placement of card k. The
    /// first cell clicked before any card was taken places the character.</item>
    /// <item>A placement counts only if the cell then looks different from the frame taken when the
    /// card was picked up. The card can show up a few seconds late on a laggy connection, so frames
    /// up to 5 s later are checked, skipping those where the cursor (and a card still in hand) is
    /// over the cell. Measured on two 曲奇 recordings: placed ≥ 41, not placed ≤ 4.4.</item>
    /// <item>A card stays in hand when its placement fails, so a click on another cell soon after
    /// (without taking a card) is read as a retry of the same card, and is checked the same way.</item>
    /// </list>
    /// </summary>
    internal sealed class RecordingReader
    {
        private const double PlacedChange = 20;
        private const double IconMatch = 8;
        private const long VerifyFrom = 300, VerifyWithin = 5000, VerifyLast = 15000, RetryWithin = 4000;

        /// <summary>The game snaps a card to a neighbouring cell when the click is this close to its edge.</summary>
        private const int SnapMargin = 16;

        /// <summary>A pick-up can show on the slot a little after the click; a cell clicked sooner counts as carrying it.</summary>
        private const long PickUpShowsWithin = 250;
        private const int FrameCache = 24;

        private readonly string _directory;
        private readonly ScreenDetector _detector;
        private readonly IReadOnlyList<(string name, StageFrame icon)> _icons;
        private readonly Func<string, (int cooldown, int layer)?> _cardDefaults;

        private readonly Dictionary<string, StageFrame> _frames = new Dictionary<string, StageFrame>();
        private readonly Queue<string> _frameOrder = new Queue<string>();
        private readonly Dictionary<string, GameScreen> _screens = new Dictionary<string, GameScreen>();
        private List<Event> _framed;
        private List<(long t, Point at)> _cursor;

        /// <summary>
        /// Correction for recordings cropped a pixel off the stage (before PluginInfo.StageRect, an odd
        /// page width did that): corrected pixel (x, y) is recorded pixel (x + dx, y + dy).
        /// </summary>
        private int _dx, _dy;
        private string _alignNote;

        private sealed class Event
        {
            public long T;
            public string Kind;
            public string Button;
            public Point? Stage;
            public string Frame;
        }

        private sealed class Candidate
        {
            public long T;
            public int Slot;
            public Point At;
            public Cell Cell;
            public string Before;
            public bool Retry;
        }

        /// <param name="cardDefaults">Cooldown and layer for a card name, when known (from the preset tracks).</param>
        public RecordingReader(string directory, ScreenDetector detector, CardLibrary cards, Func<string, (int cooldown, int layer)?> cardDefaults)
        {
            _directory = directory;
            _detector = detector;
            _icons = cards.Names.Select(n => (n, cards.Get(n))).ToList();
            _cardDefaults = cardDefaults;
        }

        public List<RecordedBattle> Read()
        {
            var path = Path.Combine(_directory, "events.jsonl");
            if (!File.Exists(path)) throw new FileNotFoundException("录制文件夹里没有 events.jsonl：" + _directory);
            var events = LoadEvents(path);
            Align(events);
            _framed = events.Where(e => e.Frame != null).OrderBy(e => e.T).ToList();
            _cursor = events.Where(e => e.Stage != null && (e.Kind == "move" || e.Kind == "down" || e.Kind == "up"))
                .Select(e => (e.T, e.Stage.Value)).OrderBy(c => c.T).ToList();

            var battles = new List<RecordedBattle>();
            string room = null;
            BattleState battle = null;
            bool battleWithoutRoom = false;
            // Only presses are classified: the last press in the room is the 开始 click, which shows the final deck.
            foreach (var e in events.Where(e => e.Kind == "down" && e.Frame != null && e.Stage != null))
            {
                var screen = Screen(e.Frame);
                if (screen == GameScreen.Room)
                {
                    if (battle != null) battles.Add(Finish(battle));
                    battle = null;
                    room = e.Frame;
                    continue;
                }
                if (screen != GameScreen.Battle) continue;
                if (battle == null)
                {
                    if (room == null)
                    {
                        battleWithoutRoom = true;
                        continue;
                    }
                    battle = Start(room, e.T);
                    room = null;
                }
                Press(battle, e);
            }
            if (battle != null) battles.Add(Finish(battle));
            if (battles.Count == 0)
                throw new InvalidOperationException(battleWithoutRoom
                    ? "录制是从战斗中途开始的：要从房间（选卡界面）开始录制，才知道每个卡槽是什么卡"
                    : "录制里没有找到战斗画面");
            return battles;
        }

        // ---------- one battle ----------

        private sealed class BattleState
        {
            public RecordedBattle Result;
            public BattleLayout Layout;
            public long FirstPress;
            public long T0 = -1;
            public int Pending;          // slot clicked since the last cell click, 0 none, -1 the shovel
            public string PendingFrame;  // frame at the first of those clicks: the field as it was, hand empty
            public long PendingAt;       // time of the latest of those clicks
            public Candidate Last;       // latest placement click, for retries
            public bool TookCard;
            public readonly List<Candidate> Candidates = new List<Candidate>();
        }

        private BattleState Start(string roomFrame, long t)
        {
            var frame = Frame(roomFrame) ?? throw new InvalidOperationException("房间画面读不出来：" + roomFrame);
            var filled = DeckBuilder.FilledSlots(frame);
            if (filled.Count == 0) throw new InvalidOperationException("开战前的房间画面里卡组是空的");
            var result = new RecordedBattle { StartSec = t / 1000.0 };
            if (_alignNote != null) result.Log.Add(_alignNote);
            for (int i = 0; i < filled.Count; i++)
            {
                var match = _icons.Select(c => (c.name, diff: DeckBuilder.SlotDiff(frame, filled[i], c.icon)))
                    .OrderBy(m => m.diff).FirstOrDefault();
                bool learned = match.name != null && match.diff < IconMatch;
                var card = new RecordedCard { Slot = i + 1, Name = learned ? match.name : $"卡{i + 1}", Learned = learned };
                var known = learned ? _cardDefaults(card.Name) : null;
                if (known.HasValue) (card.CooldownSec, card.Layer) = known.Value;
                result.Cards.Add(card);
            }
            return new BattleState { Result = result, Layout = new BattleLayout(filled.Count), FirstPress = t };
        }

        private void Press(BattleState b, Event e)
        {
            var at = e.Stage.Value;
            var log = b.Result.Log;
            string when = $"{e.T / 1000.0,7:0.0}s";
            if (e.Button != "Left")
            {
                b.Pending = 0;
                b.Last = null;
                return;
            }

            // Which card a slot click leaves in hand is not decided here: a click can miss the card
            // (e.g. just below it), and a second click on the same slot then picks it up rather than
            // putting it back. The slot's cost label at the next cell click tells (dark = in hand).
            int slot = b.Layout.SlotAt(at);
            if (slot > 0 || b.Layout.OnShovel(at))
            {
                int pending = slot > 0 ? slot : -1;
                if (b.Pending != pending) b.PendingFrame = e.Frame;
                b.Pending = pending;
                b.PendingAt = e.T;
                b.Last = null;
                b.TookCard |= slot > 0;
                return;
            }

            var cell = BattleLayout.CellAt(at);
            if (cell == null)
            {
                b.Pending = 0; // clicking beside the field puts the card back
                b.Last = null;
                return;
            }

            int held = b.Pending;
            b.Pending = 0;
            if (held == -1)
            {
                if (b.T0 < 0) b.T0 = b.FirstPress;
                b.Result.Shovel.Add(new TrackStep((int)Math.Max(0, Math.Round((e.T - b.T0) / 1000.0)), cell.Value));
                log.Add($"{when} 铲子 → {cell}");
                return;
            }
            if (held > 0 && Carrying(b, held, e))
            {
                b.Last = new Candidate { T = e.T, Slot = held, At = at, Cell = cell.Value, Before = b.PendingFrame };
                b.Candidates.Add(b.Last);
                return;
            }
            if (held > 0) log.Add($"{when} 点了 {b.Result.Cards[held - 1].Name} 的卡槽，但没有拿起来（卡槽还亮着），忽略点 {cell}");
            if (b.Last != null && e.T - b.Last.T <= RetryWithin && !Same(b.Last.Cell, cell.Value))
            {
                b.Last = new Candidate { T = e.T, Slot = b.Last.Slot, At = at, Cell = cell.Value, Before = b.Last.Before, Retry = true };
                b.Candidates.Add(b.Last);
                return;
            }
            if (b.Result.Character == null && !b.TookCard)
            {
                b.Result.Character = cell;
                b.T0 = e.T;
                log.Add($"{when} 放置人物 → {cell}");
            }
        }

        /// <summary>
        /// Whether the card of <paramref name="slot"/> is in hand at this cell click: its cost label is dark
        /// in the frame taken as the button went down (before the game handled it), after being lit when
        /// its slot was clicked. A cell clicked right after the slot counts too, the label may lag.
        /// </summary>
        private bool Carrying(BattleState b, int slot, Event cellClick)
        {
            var atCell = Frame(cellClick.Frame);
            if (atCell == null || cellClick.T - b.PendingAt < PickUpShowsWithin) return true;
            var atSlot = Frame(b.PendingFrame);
            bool couldTake = atSlot == null || b.Layout.SlotReady(atSlot, slot);
            return couldTake && !b.Layout.SlotReady(atCell, slot);
        }

        /// <summary>The clicked cell and the neighbours whose edge is within <see cref="SnapMargin"/> of the click.</summary>
        private static List<Cell> SnapCells(Point at)
        {
            var cells = new List<Cell>();
            for (int dy = -SnapMargin; dy <= SnapMargin; dy += SnapMargin)
                for (int dx = -SnapMargin; dx <= SnapMargin; dx += SnapMargin)
                {
                    var c = BattleLayout.CellAt(new Point(at.X + dx, at.Y + dy));
                    if (c != null && !cells.Any(x => Same(x, c.Value))) cells.Add(c.Value);
                }
            return cells;
        }

        private RecordedBattle Finish(BattleState b)
        {
            var result = b.Result;
            foreach (var c in b.Candidates)
            {
                var card = result.Cards[c.Slot - 1];
                var clicked = c.Cell;
                var neighbours = SnapCells(c.At).Where(n => !Same(n, clicked)).ToList();
                var change = CellChange(b.Layout, c.Before, c.T, c.Cell, full: neighbours.Count > 0);
                // A click near an edge may have put the card on the neighbour (the game snaps it):
                // take the cell that changed clearly more.
                foreach (var near in neighbours)
                {
                    var other = CellChange(b.Layout, c.Before, c.T, near, full: true);
                    if (other >= PlacedChange && other > (change ?? 0) + 10)
                    {
                        change = other;
                        c.Cell = near;
                    }
                }
                string where = Same(clicked, c.Cell) ? $"{c.Cell}" : $"{c.Cell}（点在 {clicked} 靠边处，卡吸附到了 {c.Cell}）";
                string what = $"{c.T / 1000.0,7:0.0}s {card.Name} → {where}{(c.Retry ? "（没拿卡，当作重试）" : "")}";
                if (change == null)
                {
                    result.Unverified++;
                    result.Log.Add($"{what}：之后没有能看到这一格的画面，按放上了算");
                }
                else if (change < PlacedChange)
                {
                    result.NotPlaced++;
                    result.Log.Add($"{what}：格子没有变化（{change:0.0}），没放上");
                    continue;
                }
                else result.Log.Add($"{what}：放上了（变化 {change:0.0}）");
                if (!card.Cells.Any(x => Same(x, c.Cell))) card.Cells.Add(c.Cell);
            }
            return result;
        }

        /// <summary>
        /// Largest difference of the cell against <paramref name="before"/> in battle frames after the
        /// click; null when no frame could show the cell.
        /// </summary>
        private double? CellChange(BattleLayout layout, string before, long t, Cell cell, bool full = false)
        {
            var region = layout.CellInterior(cell);
            var reference = Frame(before)?.Crop(region);
            if (reference == null) return null;
            double? best = null;
            foreach (var e in _framed)
            {
                if (e.T < t + VerifyFrom) continue;
                if (e.T > t + VerifyLast || (e.T > t + VerifyWithin && best != null)) break;
                var under = BattleLayout.CellAt(CursorAt(e.T));
                if (under != null && Same(under.Value, cell)) continue;
                if (Screen(e.Frame) != GameScreen.Battle) continue;
                var frame = Frame(e.Frame);
                if (frame == null) continue;
                best = Math.Max(best ?? 0, frame.Crop(region).MeanAbsDiff(reference));
                if (!full && best >= PlacedChange) break;
            }
            return best;
        }

        // ---------- recording files ----------

        private static bool Same(Cell a, Cell b) => a.Row == b.Row && a.Col == b.Col;

        private Point CursorAt(long t)
        {
            int lo = 0, hi = _cursor.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_cursor[mid].t <= t) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found < 0 ? new Point(-1, -1) : _cursor[found].at;
        }

        /// <summary>
        /// Finds the pixel offset at which the pressed frames are recognised. Most recordings need none;
        /// one made when the page width was odd is off by one pixel, and then no template matches.
        /// </summary>
        private void Align(List<Event> events)
        {
            var sample = events.Where(e => e.Kind == "down" && e.Frame != null).ToList();
            if (sample.Count > 16) sample = Enumerable.Range(0, 16).Select(i => sample[i * sample.Count / 16]).ToList();
            var frames = sample.Select(e => LoadFrame(e.Frame)).Where(f => f != null).ToList();
            int Known(int dx, int dy) => frames.Count(f => _detector.Detect(f.Shifted(dx, dy)) != GameScreen.Unknown);

            int best = Known(0, 0);
            if (best * 2 >= frames.Count) return;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                int known = Known(dx, dy);
                if (known <= best) continue;
                best = known;
                (_dx, _dy) = (dx, dy);
            }
            if (_dx == 0 && _dy == 0) return;
            foreach (var e in events.Where(e => e.Stage != null))
                e.Stage = new Point(e.Stage.Value.X - _dx, e.Stage.Value.Y - _dy);
            _alignNote = $"录制的画面错开了 {Math.Abs(_dx) + Math.Abs(_dy)} 像素（录制时网页宽度是奇数），已按 ({_dx},{_dy}) 校正画面和点击位置";
            Log.Info("recording " + Path.GetFileName(_directory) + ": " + _alignNote);
        }

        private StageFrame Frame(string name)
        {
            if (name == null) return null;
            if (_frames.TryGetValue(name, out var cached)) return cached;
            var frame = LoadFrame(name)?.Shifted(_dx, _dy);
            _frames[name] = frame;
            _frameOrder.Enqueue(name);
            while (_frameOrder.Count > FrameCache) _frames.Remove(_frameOrder.Dequeue());
            return frame;
        }

        private StageFrame LoadFrame(string name)
        {
            var path = Path.Combine(_directory, name);
            if (!File.Exists(path)) return null;
            try
            {
                using (var bmp = new Bitmap(path))
                    return StageFrame.FromBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            }
            catch (ArgumentException)
            {
                return null; // a frame the recorder was still writing when the app closed
            }
        }

        private GameScreen Screen(string name)
        {
            if (_screens.TryGetValue(name, out var screen)) return screen;
            var frame = Frame(name);
            return _screens[name] = frame == null ? GameScreen.Unknown : _detector.Detect(frame);
        }

        private static List<Event> LoadEvents(string path)
        {
            var events = new List<Event>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                Dictionary<string, object> e;
                try
                {
                    e = Json.Parse(line);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    continue; // the last line can be cut off
                }
                var ev = new Event
                {
                    T = e.TryGetValue("t", out var t) ? Convert.ToInt64(t, CultureInfo.InvariantCulture) : 0,
                    Kind = e.TryGetValue("kind", out var kind) ? kind as string : null,
                    Button = e.TryGetValue("button", out var button) ? button as string : null,
                    Frame = e.TryGetValue("frame", out var frame) ? frame as string : null,
                };
                if (e.TryGetValue("stage", out var s) && s is Dictionary<string, object> stage && stage.ContainsKey("x") && stage.ContainsKey("y"))
                    ev.Stage = new Point(Convert.ToInt32(stage["x"], CultureInfo.InvariantCulture), Convert.ToInt32(stage["y"], CultureInfo.InvariantCulture));
                events.Add(ev);
            }
            return events;
        }
    }
}
