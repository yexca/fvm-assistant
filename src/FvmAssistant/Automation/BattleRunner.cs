using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant.Automation
{
    internal enum BattleOutcome
    {
        /// <summary>The post-stage dialog (继续作战 / 领取奖品) appeared.</summary>
        Victory,
        /// <summary>The battle screen went away without that dialog (defeat, …).</summary>
        EndedWithoutVictory,
        TimedOut,
        /// <summary>The game lost its server connection; the battle is frozen behind the notice.</summary>
        Disconnected,
    }

    /// <summary>
    /// Plays the opening wave of a track: places the character, then places cards from the
    /// track as their slots light up, sweeping the cursor over the field to collect flames
    /// while idle. Preflight limits execution to supported timing, conditions and layers.
    /// </summary>
    internal sealed class BattleRunner
    {
        private static readonly TimeSpan MaxBattle = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan FullFormationBackoff = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan OffBattleGrace = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan OwnCardMemory = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RefusedCellMemory = TimeSpan.FromSeconds(30);
        // Fixed support maps can contain cells that look valid but reject this account's
        // carrier on the current stage. Give those cells time to change before probing again;
        // they remain retryable rather than being disabled for the whole battle.
        private static readonly TimeSpan RefusedSupportCellMemory = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ProbeRetry = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PresenceInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan AbsenceWindow = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan UnverifiedOwnershipLimit = TimeSpan.FromSeconds(45);

        /// <summary>A shield stays until mice break it; do not try to cover the same card again before this.</summary>
        private static readonly TimeSpan ShieldMemory = TimeSpan.FromSeconds(60);

        /// <summary>How late the game can still refuse a placement that looked done.</summary>
        private static readonly TimeSpan LateRefusalWindow = TimeSpan.FromSeconds(10);

        /// <summary>A cell within this of how it looked before our card went down has lost the card.</summary>
        private const double GoneDiff = 8;

        /// <summary>
        /// An empty cell renders exactly as in the opening frame (difference 0.0); cards read 20-90, and
        /// anything moving (mice, flames, the bobbing bread) is above this too, which only delays a placement.
        /// </summary>
        private const double EmptyCellDiff = 6;

        /// <summary>
        /// On a map whose background moves (棉花日's clouds sway on a 12 s loop) an empty cell hardly ever
        /// looks as in the opening frame, but it does look like the other empty cells of its column at the
        /// same moment: 1-4 apart (up to ≈10 a few rows away), where a card makes 30-110.
        /// </summary>
        private const double SameColumnEmptyDiff = 10;

        /// <summary>
        /// A column's background moves once most of its cells without a card of ours differ from the opening
        /// frame by more than this, on <see cref="MovingSightings"/> looks at least a second apart (flames or
        /// mice cover a cell or two, not a whole column).
        /// </summary>
        private const double MovingBackgroundDiff = 12;
        private const int MovingSightings = 3;
        private readonly int[] _movingSightings = new int[10];
        private readonly DateTime[] _movingSeenAt = new DateTime[10];
        private bool _movingLogged;

        /// <summary>Formation cells tried per pick-up; each miss is a click with a card in hand.</summary>
        private const int TargetsPerPick = 3;

        /// <summary>Cursor moves per sweep slice (≈0.5 s), between which placements are decided.</summary>
        private const int SweepSlice = 40;
        private const int SettleMs = 140;

        /// <summary>Cost labels react within a frame or two of a click; read them after this, then poll.</summary>
        private const int LabelSettleMs = 110;

        /// <summary>
        /// Generous: a busy field (深渊 full of 海星 stars) slows the game down, and a second click after a
        /// pick-up that only showed late puts the card back again.
        /// </summary>
        private static readonly TimeSpan PickUpLimit = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan PutBackLimit = TimeSpan.FromMilliseconds(350);

        private readonly IGameIo _io;
        private readonly ScreenDetector _detector;
        private readonly BattleLayout _layout;
        private readonly TrackFile _track;
        private readonly Action<string> _log;
        private readonly FlameReader _flameReader;
        private readonly List<Plan> _plans = new List<Plan>();
        private readonly Queue<TrackStep> _shovelSteps;
        private readonly bool _shovelIndependent;
        // Sweeps: mostly the focus path, and now and then the whole field (less often when flames pick themselves up).
        private readonly TimeSpan _fullSweepEvery;
        private readonly bool _autoFlame;
        private readonly List<Point> _focusPath;
        private readonly List<Point> _fullPath;
        private List<Point> _sweepPath;
        private DateTime _fullSweepAt = DateTime.UtcNow;

        /// <summary>
        /// Cells where a card needs a carrier (承载) under it: the track's carrier formation, and the water
        /// (or clouds) of the level's <see cref="CarrierMap"/> where the track puts other cards.
        /// </summary>
        private readonly HashSet<Cell> _carrierCells;

        /// <summary>Map carrier cells the track puts cards on without laying a carrier there itself.</summary>
        private readonly HashSet<Cell> _mapCarrierCells;

        /// <summary>
        /// On water, those of <see cref="_mapCarrierCells"/> are taken to hold a carrier of the game's own at
        /// the start (城堡, 深渊 have them). Fixed for the battle; the opening frame shows a carrier there.
        /// Empty on clouds: the game lays no 棉花糖.
        /// </summary>
        private readonly HashSet<Cell> _openingCarriers;

        /// <summary>
        /// Those of <see cref="_openingCarriers"/> whose game carrier is still believed there: cards go straight
        /// on. A refused card there means mice ate it, and a carrier of ours goes down first from then on.
        /// </summary>
        private readonly HashSet<Cell> _gameCarriers;

        /// <summary>
        /// The carriers are 棉花糖 or 麦芽糖: a card put on an open cloud falls through and is lost instead of
        /// being refused, so a carrier cell without our carrier is never tried blind.
        /// </summary>
        private readonly bool _cardsFall;

        /// <summary>Whether the deck has a carrier to put down where the game's is gone.</summary>
        private readonly bool _repairsCarriers;

        /// <summary>The track's 优先队列 as plans, and how far it has got.</summary>
        private readonly List<Plan> _queue = new List<Plan>();
        private int _queueIndex;
        private DateTime? _queueStuckSince;

        /// <summary>A queue head that cannot be placed for this long (its cells all taken or refusing) is skipped.</summary>
        private static readonly TimeSpan QueueStuckLimit = TimeSpan.FromSeconds(30);
        private int _sweepIndex;
        private StageFrame _openingField;
        private DateTime _start;
        // The upstream synchronized clock excludes time spent executing ordinary timed steps.
        // Independent steps always use wall-clock battle time.
        private TimeSpan _synchronizedPlacementTime;
        private readonly TimeSpan _cardActionInterval;
        private DateTime _lastCardActionAt = DateTime.MinValue;
        private DateTime _lastPresenceCheck;

        // Diagnostics for a battle that stops placing: an unchanged frame means the page stopped repainting.
        private static readonly TimeSpan FrozenAfter = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan IdleReport = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan IdleRepeat = TimeSpan.FromSeconds(60);
        private long _frameSignature;
        private DateTime _frameChangedAt = DateTime.UtcNow;
        private bool _frozenLogged;
        private DateTime _repaintAskedAt = DateTime.MinValue;
        private DateTime _lastPlacedAt = DateTime.UtcNow;
        private DateTime _idleLoggedAt = DateTime.MinValue;
        private DateTime _flameUnreadableLoggedAt = DateTime.MinValue;

        private sealed class Plan
        {
            public TrackCard Card;
            public int Slot;
            public PlacementSchedule Schedule;
            public ConditionGate Conditions;
            public DateTime RetryAt;
            public DateTime LastProbe;
            public DateTime? ReadySince;
            public int Placed;
            public CardSignature Signature;
            public int SignatureAttempts;
            public Cell LastCell;
            public OwnCard LastPlacement;
            public readonly HashSet<int> ActivatedSupports = new HashSet<int>();
            public bool Fill => Card.Fill || Card.Steps.Count == 0;
            public bool ScheduledFill => Card.Fill && !Card.Extreme;
            public bool HandledEvent;

            /// <summary>Whether the last <see cref="PlaceAsync"/> clicked the slot (false: no target, nothing done).</summary>
            public bool PickedUp;

            /// <summary>Cells where this card was placed, until they look as they did before it.</summary>
            public readonly Dictionary<Cell, OwnCard> Own = new Dictionary<Cell, OwnCard>();

            /// <summary>Last uncertain-cell attempt; temporary obstructions must not exclude a cell forever.</summary>
            public readonly Dictionary<Cell, DateTime> Probed = new Dictionary<Cell, DateTime>();

            /// <summary>Cells to skip until the given time: our own placements, or cells that refused the card.</summary>
            public readonly Dictionary<Cell, DateTime> BusyUntil = new Dictionary<Cell, DateTime>();

            /// <summary>Last layer-5 attempt per target; these cards can act on occupied cells.</summary>
            public readonly Dictionary<Cell, DateTime> SpecialTriedAt = new Dictionary<Cell, DateTime>();
        }

        /// <summary>One of our cards on the field.</summary>
        private sealed class OwnCard
        {
            /// <summary>The cell interior just before the card went down: bare ground, or our carrier.</summary>
            public StageFrame Before;

            /// <summary>The cell with the card on it, taken at the first look after <see cref="OwnCardMemory"/>.</summary>
            public StageFrame After;

            public DateTime PlacedAt;
            public int StepIndex = -1;
            public bool SignatureSeen;
            public DateTime LastObservedAt;
            public DateTime? AbsentSince;
            public int AbsentSightings;
        }

        private enum Placement
        {
            Placed,
            Refused,
            Dropped,
        }

        /// <summary>Thrown out of a placement when the battle screen gave way to its end.</summary>
        private sealed class BattleOverException : Exception
        {
            public BattleOverException(BattleOutcome outcome)
            {
                Outcome = outcome;
            }

            public BattleOutcome Outcome { get; }
        }

        /// <summary>Thrown out of a placement when a notice covered the field; the main loop closes it.</summary>
        private sealed class CoveredException : Exception
        {
        }

        public BattleRunner(IGameIo io, ScreenDetector detector, BattleLayout layout, TrackFile track,
            IReadOnlyDictionary<string, int> slotOfCard, bool useShovel, bool autoFlame, CarrierMap carrierMap, Action<string> log,
            FlameReader flameReader = null)
        {
            _autoFlame = autoFlame;
            _fullSweepEvery = TimeSpan.FromSeconds(autoFlame ? 90 : 45);
            _io = io;
            _detector = detector;
            _layout = layout;
            _track = track;
            _log = log;
            _flameReader = flameReader;
            _cardActionInterval = track.PlantSpeed > 0
                ? TimeSpan.FromSeconds(1.0 / track.PlantSpeed) : TimeSpan.Zero;

            var wave = track.Opening;
            foreach (var original in wave.DeckCards)
            {
                var card = carrierMap?.Resolve(original) ?? original;
                // Preserve the legacy empty-row fallback for fixed support, but only for the right card.
                if (card.Steps.Count == 0 && !(carrierMap?.Matches(card) == true && carrierMap.Cells.Count > 0))
                    continue;
                if (slotOfCard.TryGetValue(card.Name, out var slot))
                {
                    _plans.Add(new Plan { Card = card, Slot = slot,
                        Conditions = card.Extreme ? new ConditionGate(card.Conditions) : null,
                        Schedule = card.Fill && !card.Extreme
                            ? new PlacementSchedule(card.Steps.Where(s => s.TimeDeciseconds > 0).ToList())
                            : card.Fill || card.Steps.Count == 0 ? null : new PlacementSchedule(card.Steps) });
                    if (card != original) _log($"Inherited {card.Steps.Count} support steps for {card.Name} from {carrierMap.Source}");
                }
                else if (card.Steps.Count > 0)
                    _log($"轨道里的 {card.Name} 没有对应卡槽，跳过它的 {card.Steps.Count} 个放卡点");
            }

            var shovel = wave.Cards.FirstOrDefault(c => c.IsShovel);
            _shovelSteps = new Queue<TrackStep>(useShovel && shovel != null ? shovel.Steps.OrderBy(s => s.TimeSec) : Enumerable.Empty<TrackStep>());
            _shovelIndependent = shovel?.Independent == true;
            if (!useShovel && shovel != null && shovel.Steps.Count > 0)
                _log($"未启用铲子：跳过 {shovel.Steps.Count} 个铲除点");
            if (track.Waves.Count > 1)
                _log($"轨道有 {track.Waves.Count - 1} 个按波变阵段落，当前版本只执行开局段");

            // Stacking on a carrier (承载) means a normal card's cell is not empty, so only check for
            // empty cells where no carrier goes.
            var carrierFormation = new HashSet<Cell>(_plans.Where(p => p.Card.Layer == 1)
                .SelectMany(p => p.Card.Steps.Where(s => s.HasCell && (p.Fill || p.Card.Extreme || s.TimeSec == 0)).Select(s => s.Cell)));
            var cardFormation = new HashSet<Cell>(_plans.Where(p => p.Card.Layer != 1 && p.Card.Layer != 2 && !p.Card.RequiresBareWater)
                .SelectMany(p => p.Card.FormationCells));
            _cardsFall = carrierMap?.Clouds ?? _plans.Any(p => p.Card.Layer == 1 && CarrierMap.IsCloudCarrier(p.Card.Name));
            _mapCarrierCells = new HashSet<Cell>(carrierMap?.Cells.Where(c => cardFormation.Contains(c) && !carrierFormation.Contains(c)) ?? Enumerable.Empty<Cell>());
            _openingCarriers = new HashSet<Cell>(_cardsFall ? Enumerable.Empty<Cell>() : _mapCarrierCells);
            _gameCarriers = new HashSet<Cell>(_openingCarriers);
            _carrierCells = new HashSet<Cell>(carrierFormation.Concat(_mapCarrierCells));
            _repairsCarriers = _plans.Any(p => p.Card.Layer == 1 && (p.Card.Fill || p.Card.Steps.Count == 0));
            if (carrierMap != null && _carrierCells.Count > 0)
            {
                var carrier = _plans.FirstOrDefault(p => p.Card.Layer == 1 && (p.Card.Fill || p.Card.Steps.Count == 0))?.Card.Name;
                string missing = _mapCarrierCells.Count == 0 ? ""
                    : _cardsFall ? $"，其中 {_mapCarrierCells.Count} 格轨道没放{carrierMap.Carrier}" +
                                   (_repairsCarriers ? $"，由 {carrier} 先补上再放卡" : "（卡组里没有能补的承载卡，这些格子不放卡）")
                    : $"，其中 {_mapCarrierCells.Count} 格轨道没放承载，当作开局有游戏自带的" +
                      (_repairsCarriers ? $"，被吃掉后先放 {carrier} 再放卡" : "（卡组里没有承载卡，被吃掉后补不了）");
                _log($"{(_cardsFall ? "云层缺口" : "水路")}（承载轨道\\{carrierMap.Source}）：阵型里 {_carrierCells.Count} 格要先放承载卡{missing}");
            }

            foreach (var name in track.PriorityQueue)
            {
                var plan = _plans.FirstOrDefault(p => p.Card.Name == name);
                if (plan != null) _queue.Add(plan);
                else _log($"优先队列里的 {name} 不在卡组里，跳过");
            }
            if (_queue.Count > 0)
            {
                var absolute = _plans.Where(p => p.Card.Absolute).Select(p => p.Card.Name).ToList();
                _log("优先队列：" + string.Join(" → ", _queue.Select(p => p.Card.Name)) + "，放完之前只放" +
                     (absolute.Count > 0 ? "绝对优先的 " + string.Join("、", absolute) + " 和" : "") + "队首要用的承载");
            }
            _fullPath = SweepPath(new[] { (_layout.Pickups.Left, _layout.Pickups.Right) });
            _focusPath = SweepPath(FocusSpans());
            _sweepPath = _focusPath;
        }

        public int Placements { get; private set; }
        public int FailedAttempts { get; private set; }

        private readonly Dictionary<Cell, int> _refused = new Dictionary<Cell, int>();
        private int _refusalLogs;

        /// <summary>Cells that refused a card, most often first ("61×4 72×2").</summary>
        public string RefusalSummary => string.Join(" ", _refused.OrderByDescending(p => p.Value).Select(p => $"{p.Key}×{p.Value}"));

        /// <summary>Clicks that made the game drop the card instead of placing it.</summary>
        public int Drops { get; private set; }
        /// <summary>Completed passes over the focus columns (last two columns, flame producers).</summary>
        public int Sweeps { get; private set; }

        /// <summary>Completed passes over the whole field.</summary>
        public int FullSweeps { get; private set; }

        /// <summary>Clicks on a lit slot that did not pick the card up.</summary>
        public int PickUpFailures { get; private set; }

        /// <summary>Cards found back in hand after their placement had looked done (refused late by the game).</summary>
        public int LateRefusals { get; private set; }

        public async Task<BattleOutcome> RunAsync(CancellationToken ct, Func<CancellationToken, Task> afterOpeningFrame = null)
        {
            await PlaceCharacterAsync(ct).ConfigureAwait(false);
            _start = DateTime.UtcNow;
            _synchronizedPlacementTime = TimeSpan.Zero;
            if (afterOpeningFrame != null) await afterOpeningFrame(ct).ConfigureAwait(false);
            return await PlayAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The boss stage (after 继续作战) goes on in the same field with the same cards and flames, so
        /// it is played by the same runner: no character to place, and the track's formation (补) keeps
        /// being filled. Call once the battle screen is back.
        /// </summary>
        public Task<BattleOutcome> ContinueAsync(CancellationToken ct)
        {
            if (_openingField == null) throw new InvalidOperationException("ContinueAsync needs a battle played by RunAsync first");
            return PlayAsync(ct);
        }

        private async Task<BattleOutcome> PlayAsync(CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + MaxBattle;
            DateTime? offBattleSince = null;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var now = DateTime.UtcNow;
                if (now > deadline) return BattleOutcome.TimedOut;

                var frame = _io.Grab();
                var screen = _detector.Detect(frame);
                // The 胜利 banner with the loot bar also proves the win, in case the dialog was missed.
                if (screen == GameScreen.VictoryDialog || screen == GameScreen.AwaitHostDecision || screen == GameScreen.Loot) return BattleOutcome.Victory;
                if (screen == GameScreen.Disconnected) return BattleOutcome.Disconnected;
                if (screen == GameScreen.HealthNotice || screen == GameScreen.EventPopup)
                {
                    // A notice over the battle (e.g. the periodic health reminder): close it and play on.
                    _log(screen == GameScreen.HealthNotice ? "战斗中出现健康提示，关掉" : "战斗中出现活动弹窗，关掉");
                    await _io.ClickAsync(screen == GameScreen.HealthNotice ? GrindLoop.HealthNoticeOk : GrindLoop.EventPopupClose).ConfigureAwait(false);
                    _io.Move(_layout.Neutral);
                    await Task.Delay(600, ct).ConfigureAwait(false);
                    offBattleSince = null;
                    continue;
                }
                if (screen != GameScreen.Battle)
                {
                    // Banners and effects can briefly hide the exit button; only give up after a grace period.
                    offBattleSince = offBattleSince ?? now;
                    if (now - offBattleSince > OffBattleGrace) return BattleOutcome.EndedWithoutVictory;
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    continue;
                }
                offBattleSince = null;
                WatchFrame(frame, now);
                RefreshBoard(frame, now);
                // First at 20 s, then every minute: a full formation leaves nothing to place for long stretches.
                if (now - _lastPlacedAt > IdleReport && now - _idleLoggedAt > (_idleLoggedAt > _lastPlacedAt ? IdleRepeat : TimeSpan.Zero))
                {
                    _idleLoggedAt = now;
                    var waiting = QueueHead(now);
                    _log($"{(int)(now - _lastPlacedAt).TotalSeconds} 秒没有放卡：" + (waiting != null ? $"优先队列等 {waiting.Card.Name}；" : "") +
                         string.Join("；", _plans.Select(p => DescribeIdle(p, frame, now))));
                }

                bool needsFlames = _plans.Any(p => p.Card.Extreme && p.Card.Conditions.MinimumFlames > 0);
                int? flames = needsFlames && now - _frameChangedAt <= FrozenAfter
                    ? _flameReader?.Read(frame, _layout.SlotCount) : null;
                if (needsFlames && !flames.HasValue && now - _flameUnreadableLoggedAt > TimeSpan.FromSeconds(15))
                {
                    _flameUnreadableLoggedAt = now;
                    _log("Flame counter unreadable or frame stale; flame-gated cards are waiting for a fresh reading.");
                }
                bool acted = false;

                if (_shovelSteps.Count > 0 && TrackSeconds(_shovelIndependent, now) >= _shovelSteps.Peek().TimeSec)
                {
                    var actionStart = DateTime.UtcNow;
                    try { await ShovelAsync(_shovelSteps.Dequeue().Cell, ct).ConfigureAwait(false); }
                    finally { if (!_shovelIndependent) CountSynchronizedPlacement(actionStart); }
                    acted = true;
                }
                else
                {
                    var head = QueueHead(now);
                    // Timed steps before "as soon as ready" ones: a cloud hole is patched on time.
                    foreach (var plan in _plans.OrderBy(p => p.Card.Fill || p.Card.Extreme || p.Card.Steps.Count == 0 ? 1 : 0))
                    {
                        // The queue saves the flames for its head: meanwhile only 绝 cards, and carriers for the head's cells.
                        bool forHead = head != null && plan != head && !plan.Card.Absolute;
                        if (forHead && plan.Card.Layer != 1) continue;
                        if (!IsDue(plan, now)) continue;
                        int? cardCount = plan.Card.Conditions.MinimumCardCount > 0
                            ? ObservedCardCount(plan.Card.Conditions.CardName, frame, now) : null;
                        if (plan.Conditions != null && !plan.Conditions.Allows(now, _start, flames, cardCount))
                        {
                            if (plan == head) AdvanceQueue(false);
                            continue;
                        }
                        if (!_layout.SlotReady(frame, plan.Slot)) continue;
                        if (plan.Card.Layer == 1 && (!plan.ReadySince.HasValue || now - plan.ReadySince.Value < TimeSpan.FromSeconds(1))) continue;
                        if (!CardRateReady(now)) continue;
                        try
                        {
                            var actionStart = DateTime.UtcNow;
                            bool placed;
                            try
                            {
                                placed = await PlaceAsync(plan, frame, ct, forHead ? head.Card.FormationCells.Distinct().ToList() : null).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (plan.PickedUp) _lastCardActionAt = actionStart;
                                // CheckNormalSlot charges ordinary synchronized placements, including failed attempts.
                                // Extreme placements do not pause the shared clock in the track executor.
                                if ((!plan.Fill || plan.ScheduledFill) && !plan.Card.Extreme && !plan.Card.Independent && plan.PickedUp)
                                    CountSynchronizedPlacement(actionStart);
                            }
                            if (plan == head) AdvanceQueue(placed || plan.HandledEvent);
                            if (!plan.PickedUp) continue; // no free cell for it yet; the hand stayed empty
                        }
                        catch (BattleOverException over)
                        {
                            return over.Outcome;
                        }
                        catch (CoveredException)
                        {
                            // The loop closes the notice on its next pass.
                        }
                        acted = true;
                        break; // flames and slots changed; decide again on a fresh frame
                    }
                }

                // A placement always ends with the hand empty (see SettleAsync), so the sweep collects.
                if (!acted) await SweepSliceAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>Logs when the page stops repainting during a battle (something always moves there) and when it resumes.</summary>
        private void WatchFrame(StageFrame frame, DateTime now)
        {
            long signature = 0;
            var bgra = frame.Bgra;
            for (int i = 0; i < bgra.Length; i += 4 * 97) signature = signature * 31 + bgra[i] + (bgra[i + 1] << 8) + (bgra[i + 2] << 16);
            if (signature != _frameSignature)
            {
                if (_frozenLogged) _log($"画面恢复更新（停了 {(now - _frameChangedAt).TotalSeconds:0} 秒）");
                _frameSignature = signature;
                _frameChangedAt = now;
                _frozenLogged = false;
            }
            else if (now - _frameChangedAt > FrozenAfter && now - _repaintAskedAt > FrozenAfter)
            {
                // Something always moves in a battle: the page stopped repainting. Ask for a frame.
                _repaintAskedAt = now;
                _io.Repaint();
                if (!_frozenLogged) _log($"战斗画面 {FrozenAfter.TotalSeconds:0} 秒没有任何变化：浏览器可能停止了重绘，请它重绘");
                _frozenLogged = true;
            }
        }

        /// <summary>Why a plan is not placing: slot state, back-off, and how its formation cells look.</summary>
        private string DescribeIdle(Plan plan, StageFrame frame, DateTime now)
        {
            var cells = plan.Card.FormationCells.Distinct().ToList();
            int busy = cells.Count(c => plan.BusyUntil.TryGetValue(c, out var until) && until > now);
            int own = cells.Count(c => plan.Own.ContainsKey(c));
            int empty = cells.Count(c => !plan.Own.ContainsKey(c) && LooksEmpty(frame, c));
            string slot = _layout.SlotReady(frame, plan.Slot) ? "亮" : $"暗 {_layout.SlotValue(frame, plan.Slot):0.00}";
            string wait = now < plan.RetryAt ? $"，等 {(plan.RetryAt - now).TotalSeconds:0.0} 秒" : "";
            int pending = plan.Schedule?.Pending ?? 0;
            return $"{plan.Card.Name} slot {slot}{wait}: {cells.Count} cells, owned {own}, waiting {busy}, empty {empty}, probed {plan.Probed.Count}, pending steps {pending}";
        }

        /// <summary>Battle time for 独, or track time with ordinary synchronized placement time removed.</summary>
        private double TrackSeconds(bool independent, DateTime now) => Math.Max(0,
            (now - _start - (independent ? TimeSpan.Zero : _synchronizedPlacementTime)).TotalSeconds);

        private bool CardRateReady(DateTime now) => _cardActionInterval == TimeSpan.Zero ||
            now - _lastCardActionAt >= _cardActionInterval;

        private void CountSynchronizedPlacement(DateTime actionStart)
        {
            var duration = DateTime.UtcNow - actionStart;
            if (duration > TimeSpan.Zero) _synchronizedPlacementTime += duration;
        }

        /// <summary>
        /// The 优先队列 entry to place next, once its time has come (before that the others go on as usual).
        /// Entries whose card has no step left are passed over.
        /// </summary>
        private Plan QueueHead(DateTime now)
        {
            while (_queueIndex < _queue.Count)
            {
                var head = _queue[_queueIndex];
                var steps = head.Card.Steps;
                if (steps.Count == 0 || head.Schedule?.Done == true || head.Conditions?.Stopped(DateTime.UtcNow, _start) == true)
                {
                    _queueIndex++;
                    continue;
                }
                double t = TrackSeconds(head.Card.Independent, now);
                return head.Fill && !head.ScheduledFill ? (t >= steps[0].TimeSec ? head : null)
                    : (head.Schedule.Next(t, now, head.Card.Extreme).HasValue ? head : null);
            }
            return null;
        }

        /// <summary>After the queue head had its turn: next entry once it is down, or when it stayed stuck too long.</summary>
        private void AdvanceQueue(bool placed)
        {
            var head = _queue[_queueIndex];
            var now = DateTime.UtcNow;
            if (placed)
            {
                _queueIndex++;
                _queueStuckSince = null;
                if (_queueIndex == _queue.Count) _log("优先队列放完，其他卡照常放");
                return;
            }
            _queueStuckSince = _queueStuckSince ?? now;
            if (now - _queueStuckSince < QueueStuckLimit) return;
            _log($"优先队列：{head.Card.Name} {QueueStuckLimit.TotalSeconds:0} 秒放不下，跳过它");
            _queueIndex++;
            _queueStuckSince = null;
        }

        private bool IsDue(Plan plan, DateTime now)
        {
            var steps = plan.Card.Steps;
            if (now < plan.RetryAt) return false;
            if (steps.Count == 0) return true; // a carrier that only goes where the map needs one
            double t = TrackSeconds(plan.Card.Independent, now);
            if (plan.Fill && !plan.ScheduledFill) return t >= steps[0].TimeSec;
            return plan.Schedule.Next(t, now, plan.Card.Extreme).HasValue;
        }

        /// <summary>Recheck ownership independently of target selection, including a fully occupied column.</summary>
        private void RefreshBoard(StageFrame frame, DateTime now)
        {
            foreach (var plan in _plans)
            {
                if (_layout.SlotReady(frame, plan.Slot)) plan.ReadySince = plan.ReadySince ?? now;
                else plan.ReadySince = null;

                if (plan.Card.Layer != 1 || plan.Fill || plan.Card.Extreme) continue;
                for (int i = 0; i < plan.Card.Steps.Count; i++)
                {
                    var step = plan.Card.Steps[i];
                    if (TrackSeconds(plan.Card.Independent, now) < step.TimeSec || !plan.ActivatedSupports.Add(i)) continue;
                    _carrierCells.Add(step.Cell);
                    // Each timed cloud opening invalidates the old support, including repeat cells.
                    if (_cardsFall)
                        foreach (var other in _plans)
                        {
                            other.Own.Remove(step.Cell);
                            other.BusyUntil.Remove(step.Cell);
                            other.Probed.Remove(step.Cell);
                        }
                }
            }
            if (now - _lastPresenceCheck < PresenceInterval) return;
            _lastPresenceCheck = now;
            foreach (var plan in _plans)
                foreach (var cell in plan.Own.Keys.ToList()) Present(plan, cell, frame, now);
        }

        private async Task PlaceCharacterAsync(CancellationToken ct)
        {
            var cells = _track.Player1Cells;
            var cell = cells.Count > 0 ? cells[0] : new Cell(1, 1);
            var at = _layout.CellCenter(cell);
            // "请放置您的角色" appears ≈1.2 s after the battle screen with a 3 s countdown (the game
            // places the character itself if nobody does). Click once only: clicking the placed
            // character again could pick it back up.
            await Task.Delay(1000, ct).ConfigureAwait(false);
            _log($"放置角色 → {cell}");
            await _io.ClickAsync(at).ConfigureAwait(false);
            _io.Move(_layout.Neutral);
            await Task.Delay(800, ct).ConfigureAwait(false);
            _openingField = _io.Grab();
        }

        private bool LooksEmpty(StageFrame frame, Cell cell)
        {
            var r = _layout.CellInterior(cell);
            if (frame.Crop(r).MeanAbsDiff(_openingField.Crop(r)) < EmptyCellDiff) return true;
            return BackgroundMoves(frame, cell.Col) && LikeAnEmptyNeighbour(frame, cell);
        }

        /// <summary>No card of ours is known to be there.</summary>
        private bool Unowned(Cell cell) => !_plans.Any(p => p.Own.ContainsKey(cell));

        private IEnumerable<Cell> Column(int col) => Enumerable.Range(1, 7).Select(row => new Cell(row, col));

        /// <summary>Whether the background of the column moves (see <see cref="SameColumnEmptyDiff"/>); once seen, for the rest of the battle.</summary>
        private bool BackgroundMoves(StageFrame frame, int col)
        {
            if (_movingSightings[col] >= MovingSightings) return true;
            var now = DateTime.UtcNow;
            if (now - _movingSeenAt[col] < TimeSpan.FromSeconds(1)) return false;
            var diffs = Column(col).Where(Unowned).Select(c =>
            {
                var r = _layout.CellInterior(c);
                return frame.Crop(r).MeanAbsDiff(_openingField.Crop(r));
            }).ToList();
            if (diffs.Count < 4 || diffs.Count(d => d > MovingBackgroundDiff) * 4 < diffs.Count * 3) return false;
            _movingSeenAt[col] = now;
            if (++_movingSightings[col] < MovingSightings) return false;
            if (!_movingLogged)
                _log($"第 {col} 列的背景在动（空格和开局差 {diffs.OrderBy(d => d).ElementAt(diffs.Count / 2):0}）：这种列的空格改为和同一列别的空格比较");
            _movingLogged = true;
            return true;
        }

        /// <summary>Whether the cell looks like another cell of its column without a card of ours, give or take a pixel.</summary>
        private bool LikeAnEmptyNeighbour(StageFrame frame, Cell cell)
        {
            var mine = frame.Crop(Core(cell));
            foreach (var other in Column(cell.Col).Where(c => c.Row != cell.Row && Unowned(c)))
            {
                var at = Core(other);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (mine.MeanAbsDiff(frame.Crop(new Rectangle(at.X + dx, at.Y + dy, at.Width, at.Height))) < SameColumnEmptyDiff)
                            return true;
            }
            return false;
        }

        /// <summary>The middle of a cell, the same size in every row of its column (rows are 62-65 px tall).</summary>
        private Rectangle Core(Cell cell)
        {
            var r = _layout.CellInterior(cell);
            return new Rectangle(r.X, r.Y + (r.Height - 36) / 2, r.Width, 36);
        }

        /// <summary>
        /// Formation cells to try, chosen before the card is picked up so it is carried as briefly as
        /// possible.
        /// <list type="bullet">
        /// <item>Carriers: cells whose game carrier was eaten under a card of ours (that card is waiting),
        /// then their formation cells that look empty.</item>
        /// <item>Other cards: bare cells that look empty, carrier cells once our carrier is on them, and
        /// cells with the game's own carrier (as bare cells) until a refusal there shows it is gone;
        /// never a cell where a card of ours still is.</item>
        /// <item>Shields: cells where a card of ours (not just a carrier) still is.</item>
        /// </list>
        /// Unknown cells are probed at a bounded rate and revisited after transient failures.
        /// <paramref name="only"/> limits (and orders) the cells, for a carrier laid ahead of the queue head.
        /// </summary>
        private List<Cell> FormationTargets(Plan plan, StageFrame frame, DateTime now, IList<Cell> only = null)
        {
            var free = plan.Card.FormationCells.Distinct().Where(c => !Busy(plan, c, now)).ToList();
            if (only != null) free = only.Where(free.Contains).ToList();

            // A shield (护罩, e.g. 瓜皮) only goes onto a card: cover cells where another card of ours still is.
            if (plan.Card.Layer == 2)
                return free.Where(c => OursStillThere(c, frame, now)).Take(TargetsPerPick).ToList();

            var ready = new List<Cell>();
            var unknown = new List<Cell>();
            if (plan.Card.Layer == 1)
            {
                var wanted = (only ?? _mapCarrierCells.ToList()).Where(c => _mapCarrierCells.Contains(c) && !_gameCarriers.Contains(c) && !Busy(plan, c, now) &&
                                                                            !CarriedByUs(c, frame, now) && !OursStillThere(c, frame, now));
                foreach (var c in wanted)
                {
                    // Water: the game's carrier was there at the start, so the cell never looks as it did then.
                    if (_openingCarriers.Contains(c) || LooksEmpty(frame, c)) ready.Add(c);
                    else unknown.Add(c);
                }
                foreach (var c in free)
                {
                    if (CarriedByUs(c, frame, now) || OursStillThere(c, frame, now)) continue;
                    if (LooksEmpty(frame, c)) ready.Add(c);
                    else if (!plan.Own.ContainsKey(c)) unknown.Add(c);
                }
            }
            else if (_carrierCells.Count == 0)
            {
                foreach (var c in free)
                {
                    if (OursStillThere(c, frame, now)) continue;
                    if (LooksEmpty(frame, c)) ready.Add(c);
                    else if (!plan.Own.ContainsKey(c)) unknown.Add(c);
                }
            }
            else
            {
                foreach (var c in free)
                {
                    if (OursStillThere(c, frame, now)) continue;
                    bool empty = LooksEmpty(frame, c);
                    if (_carrierCells.Contains(c))
                    {
                        if (CarriedByUs(c, frame, now)) ready.Add(c);
                        else if (_gameCarriers.Contains(c))
                        {
                            if (empty) ready.Add(c);
                            else unknown.Add(c);
                        }
                        // Empty ones wait for our carrier; on clouds a card tried blind would fall through.
                        else if (!empty && !_openingCarriers.Contains(c) && !_cardsFall) unknown.Add(c);
                    }
                    else if (empty) ready.Add(c);
                    else unknown.Add(c);
                }
            }

            if (ready.Count > 0 || now - plan.LastProbe < ProbeInterval) return ready.Take(TargetsPerPick).ToList();
            var probe = unknown.Distinct().Where(c => !plan.Probed.TryGetValue(c, out var last) || now - last >= ProbeRetry)
                .OrderBy(c => plan.Probed.TryGetValue(c, out var last) ? last : DateTime.MinValue).Take(1).ToList();
            if (probe.Count > 0)
            {
                plan.LastProbe = now;
                plan.Probed[probe[0]] = now;
            }
            return probe;
        }

        /// <summary>Cooldown cards act on a formation cell even when another card occupies it.</summary>
        private List<Cell> SpecialTargets(Plan plan, DateTime now)
        {
            return plan.Card.FormationCells.Distinct()
                .Where(c => !Busy(plan, c, now))
                .OrderBy(c => plan.SpecialTriedAt.TryGetValue(c, out var last) ? last : DateTime.MinValue)
                .Take(TargetsPerPick).ToList();
        }

        /// <summary>Count only cards with positive board evidence; an unconfirmed click is not a card.</summary>
        private int? ObservedCardCount(string name, StageFrame frame, DateTime now)
        {
            var plan = _plans.FirstOrDefault(p => p.Card.Name == name && p.Card.Layer == 3);
            if (plan == null) return null;
            int count = 0;
            foreach (var cell in plan.Own.Keys.ToList())
            {
                if (!Present(plan, cell, frame, now)) continue;
                var own = plan.Own[cell];
                if (own.SignatureSeen || (own.After != null && own.After.MeanAbsDiff(own.Before) >= GoneDiff)) count++;
            }
            return count;
        }

        /// <summary>Whether a normal card of ours (not a carrier or shield) holds the cell.</summary>
        private bool OursStillThere(Cell cell, StageFrame frame, DateTime now) =>
            _plans.Any(p => p.Card.Layer == 3 && Present(p, cell, frame, now));

        private bool CarriedByUs(Cell cell, StageFrame frame, DateTime now) =>
            _plans.Any(p => p.Card.Layer == 1 && Present(p, cell, frame, now));

        private static bool Busy(Plan plan, Cell cell, DateTime now) => plan.BusyUntil.TryGetValue(cell, out var until) && until > now;

        /// <summary>
        /// Whether the card <paramref name="plan"/> put on the cell is still there. It is gone once the cell
        /// looks as in the opening frame, or as it did before the card went down, or clearly more like that
        /// than like the cell with the card on it. The last test copes with a "before" that caught a flame
        /// or a bullet passing through. Forgets the card once gone.
        /// </summary>
        private bool Present(Plan plan, Cell cell, StageFrame frame, DateTime now)
        {
            if (!plan.Own.TryGetValue(cell, out var own)) return false;
            if (now - own.PlacedAt < OwnCardMemory)
            {
                // Remember positive sprite evidence even if the card disappears during the grace period.
                if (plan.Signature != null && plan.Signature.Score(frame, _layout.CardRegion(cell)) >= 0.3)
                    own.SignatureSeen = true;
                return true; // a laggy connection can draw it late
            }
            if (now - own.LastObservedAt < PresenceInterval) return true;
            own.LastObservedAt = now;
            var r = _layout.CellInterior(cell);
            var current = frame.Crop(r);
            if (own.After == null)
            {
                own.After = current;
                return true;
            }
            double toBefore = current.MeanAbsDiff(own.Before);
            // Our carrier where the game had one at the start looks just like the opening frame.
            bool likeOpeningMeansGone = !(plan.Card.Layer == 1 && _openingCarriers.Contains(cell));
            // "Clearly" closer: a banner or effect over the cell (最后一波啦) changes both comparisons alike.
            // Where the background moves, a card gone leaves the cell like the empty ones of its column; not
            // for carriers, whose 棉花糖 looks just like a cloud.
            bool missing = toBefore < GoneDiff || toBefore < current.MeanAbsDiff(own.After) * 0.5 ||
                (likeOpeningMeansGone && current.MeanAbsDiff(_openingField.Crop(r)) < EmptyCellDiff) ||
                (plan.Card.Layer != 1 && BackgroundMoves(frame, cell.Col) && LikeAnEmptyNeighbour(frame, cell));
            if (plan.Signature != null)
            {
                double score = plan.Signature.Score(frame, _layout.CardRegion(cell));
                if (score >= 0.3) own.SignatureSeen = true;
                // Use negative template evidence only after this cell has actually shown the card.
                if (own.SignatureSeen) missing = score < 0.1;
            }
            // Failed template learning must not leave a phantom card on an animated board forever.
            // Expiry only makes the cell unknown again; the usual slow probe and placement confirmation apply.
            if (plan.Card.Layer == 3 && !own.SignatureSeen && now - own.PlacedAt >= UnverifiedOwnershipLimit &&
                (_cardsFall || BackgroundMoves(frame, cell.Col))) missing = true;
            if (!missing)
            {
                own.AbsentSince = null;
                own.AbsentSightings = 0;
            }
            else
            {
                own.AbsentSince = own.AbsentSince ?? now;
                if (++own.AbsentSightings >= 3 && now - own.AbsentSince.Value >= AbsenceWindow)
                {
                    plan.Own.Remove(cell);
                    plan.Probed.Remove(cell);
                    if (plan.Card.Layer == 1 && !plan.ScheduledFill && own.StepIndex >= 0)
                        plan.Schedule.Retry(own.StepIndex, now);
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> PlaceAsync(Plan plan, StageFrame frame, CancellationToken ct, IList<Cell> only = null)
        {
            var card = plan.Card;
            var now = DateTime.UtcNow;
            plan.PickedUp = false;
            plan.HandledEvent = false;
            bool fill = plan.Fill;
            int? stepIndex = fill && !plan.ScheduledFill ? null :
                plan.Schedule.Next(TrackSeconds(card.Independent, now), now, card.Extreme);
            var cells = fill ? card.Layer == 5 ? SpecialTargets(plan, now) : FormationTargets(plan, frame, now, only)
                : stepIndex.HasValue ? new List<Cell> { card.Steps[stepIndex.Value].Cell } : new List<Cell>();
            if (!fill && card.Layer != 1 && card.Layer != 5)
                cells = cells.Where(c => !_carrierCells.Contains(c) || CarriedByUs(c, frame, now) || _gameCarriers.Contains(c)).ToList();
            if (cells.Count == 0)
            {
                if (plan.ScheduledFill && stepIndex.HasValue)
                {
                    plan.Schedule.Complete(stepIndex.Value);
                    plan.HandledEvent = true;
                }
                else plan.RetryAt = now + FullFormationBackoff;
                return false;
            }
            plan.PickedUp = true;

            foreach (var cell in cells)
            {
                StageFrame background1 = null, background2 = null;
                if (card.Layer == 3 && plan.Signature == null && plan.SignatureAttempts < 3)
                {
                    plan.SignatureAttempts++;
                    _io.Move(_layout.Neutral);
                    await Task.Delay(LabelSettleMs, ct).ConfigureAwait(false);
                    background1 = CheckedGrab().Crop(_layout.HeldCardRegion);
                    await Task.Delay(LabelSettleMs, ct).ConfigureAwait(false);
                    background2 = CheckedGrab().Crop(_layout.HeldCardRegion);
                }
                var bar = _io.Grab();
                await _io.ClickAsync(_layout.SlotCenter(plan.Slot)).ConfigureAwait(false);
                bool picked = await PickedUpAsync(plan.Slot, ct).ConfigureAwait(false);
                int returned = picked ? 0 : ReturnedSlot(bar, _io.Grab(), plan.Slot);
                if (returned > 0)
                {
                    // That click only put back a card still in hand; take this one now.
                    ForgetLatePlacement(returned);
                    await _io.ClickAsync(_layout.SlotCenter(plan.Slot)).ConfigureAwait(false);
                    picked = await PickedUpAsync(plan.Slot, ct).ConfigureAwait(false);
                }
                if (!picked)
                {
                    PickUpFailures++;
                    _log($"{card.Name}（{plan.Slot} 槽）没有拿起来（费用标签亮度 {_layout.SlotValue(bar, plan.Slot):0.00} → {_layout.SlotValue(_io.Grab(), plan.Slot):0.00}）");
                    plan.RetryAt = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    return false;
                }

                if (background1 != null)
                {
                    var held1 = CheckedGrab().Crop(_layout.HeldCardRegion);
                    await Task.Delay(LabelSettleMs, ct).ConfigureAwait(false);
                    var held2 = CheckedGrab().Crop(_layout.HeldCardRegion);
                    plan.Signature = CardSignature.Learn(background1, background2, held1, held2);
                }
                var before = CheckedGrab().Crop(_layout.CellInterior(cell));
                if (stepIndex.HasValue) plan.Schedule.Attempt(stepIndex.Value, DateTime.UtcNow);
                if (card.Layer == 5) plan.SpecialTriedAt[cell] = DateTime.UtcNow;
                plan.ReadySince = null;
                await _io.ClickAsync(_layout.CellCenter(cell)).ConfigureAwait(false);
                var outcome = await SettleAsync(plan.Slot, ct).ConfigureAwait(false);
                if (outcome == Placement.Placed)
                {
                    var placedAt = DateTime.UtcNow;
                    var own = new OwnCard { Before = before, PlacedAt = placedAt, StepIndex = stepIndex ?? -1 };
                    if (card.Layer != 5) plan.Own[cell] = own;
                    plan.LastCell = cell;
                    plan.LastPlacement = own;
                    plan.Placed++;
                    plan.Conditions?.Placed();
                    Placements++;
                    if (stepIndex.HasValue) plan.Schedule.Complete(stepIndex.Value, card.Layer == 1 && !plan.ScheduledFill);
                    plan.HandledEvent = stepIndex.HasValue;
                    plan.BusyUntil[cell] = placedAt + (card.Layer == 2 ? ShieldMemory : OwnCardMemory);
                    // A carrier is down: cards refused here a moment ago can go on now.
                    if (card.Layer == 1)
                        foreach (var p in _plans.Where(p => p.Card.Layer != 1))
                            p.BusyUntil.Remove(cell);
                    _log($"放 {card.Name}（{plan.Slot} 槽）→ {cell}");
                    _lastPlacedAt = placedAt;
                    return true;
                }

                FailedAttempts++;
                plan.BusyUntil[cell] = DateTime.UtcNow +
                    (card.Layer == 1 ? RefusedSupportCellMemory : RefusedCellMemory);
                if (outcome == Placement.Dropped)
                {
                    Drops++;
                }
                else
                {
                    _refused[cell] = _refused.TryGetValue(cell, out var n) ? n + 1 : 1;
                    // Refused on a carrier cell: the carrier there (ours or the game's) is most likely gone
                    // (eaten), and a busy field (flying stars, mice) can hide that. Wait until a carrier of
                    // ours is placed there again.
                    if (card.Layer != 1 && card.Layer != 5 && _carrierCells.Contains(cell))
                    {
                        foreach (var carrier in _plans.Where(p => p.Card.Layer == 1))
                            carrier.Own.Remove(cell);
                        if (_repairsCarriers && _gameCarriers.Remove(cell))
                            _log($"{cell} 游戏自带的承载没了：先放承载卡再放 {card.Name}");
                    }
                    // A carrier refused where the game had one: it is still there after all, cards may go straight on.
                    else if (card.Layer == 1 && _openingCarriers.Contains(cell))
                    {
                        _gameCarriers.Add(cell);
                    }
                    if (_refusalLogs++ < 6)
                    {
                        var r = _layout.CellInterior(cell);
                        _log($"{card.Name} 放不进 {cell}，点卡槽放回（格子与开局差值 {_io.Grab().Crop(r).MeanAbsDiff(_openingField.Crop(r)):0.0}）");
                    }
                }
                if (!fill) break;
            }

            if (plan.ScheduledFill)
            {
                if (stepIndex.HasValue) plan.Schedule.Retry(stepIndex.Value, DateTime.UtcNow);
            }
            else if (fill)
            {
                plan.RetryAt = DateTime.UtcNow + FullFormationBackoff;
            }
            else if (card.Layer == 1)
            {
                plan.Schedule.Retry(stepIndex.Value, DateTime.UtcNow);
            }
            else
            {
                _log($"{card.Name} 放不进 {cells[0]}，跳过这一步");
                plan.Schedule.Complete(stepIndex.Value);
            }
            return false;
        }

        /// <summary>
        /// After clicking a cell with a card in hand: whether the card went down. A card still in hand and
        /// one that went down (now cooling) both keep the slot dark, and a card drawn at a parked cursor
        /// can hide in an animated background (深渊's water). Clicking the card bar tells them apart on any
        /// map: it puts a card in hand back, which lights the slot at once, and does nothing to a cooling
        /// card. Some occupied cells make the game drop the card instead, which lights the slot by itself.
        /// </summary>
        private async Task<Placement> SettleAsync(int slot, CancellationToken ct)
        {
            _io.Move(_layout.Neutral);
            await Task.Delay(SettleMs, ct).ConfigureAwait(false);
            if (_layout.SlotReady(CheckedGrab(), slot)) return Placement.Dropped;
            await _io.ClickAsync(_layout.SlotCenter(slot)).ConfigureAwait(false);
            return await SlotLabelAsync(slot, lit: true, PutBackLimit, ct).ConfigureAwait(false) ? Placement.Refused : Placement.Placed;
        }

        /// <summary>
        /// Whether a click on <paramref name="slot"/> lit up another slot instead: it put back a card that
        /// was still in hand. That happens when a placement looked done but the game refused it only after
        /// our check (the server's answer can take a second or more).
        /// </summary>
        private int ReturnedSlot(StageFrame before, StageFrame after, int slot) =>
            Enumerable.Range(1, _layout.SlotCount).FirstOrDefault(k => k != slot && !_layout.SlotReady(before, k) && _layout.SlotReady(after, k));

        /// <summary>The card of <paramref name="slot"/> came back to hand: its latest placement did not happen.</summary>
        private void ForgetLatePlacement(int slot)
        {
            LateRefusals++;
            var plan = _plans.FirstOrDefault(p => p.Slot == slot);
            var latest = plan?.LastPlacement;
            if (latest == null || DateTime.UtcNow - latest.PlacedAt > LateRefusalWindow) return;
            if (plan.Own.TryGetValue(plan.LastCell, out var current) && current == latest) plan.Own.Remove(plan.LastCell);
            plan.BusyUntil.Remove(plan.LastCell);
            plan.Probed.Remove(plan.LastCell);
            if (latest.StepIndex >= 0) plan.Schedule.Retry(latest.StepIndex, DateTime.UtcNow);
            plan.RetryAt = DateTime.MinValue;
            plan.LastPlacement = null;
            plan.Placed--;
            plan.Conditions?.RollBack();
            Placements--;
            if (LateRefusals <= 3) _log($"Late rejection: {plan.Card.Name} at {plan.LastCell}; placement rolled back and timed step restored");
        }

        /// <summary>After clicking a ready slot: its cost label goes dark at once when the card comes off.</summary>
        private Task<bool> PickedUpAsync(int slot, CancellationToken ct) => SlotLabelAsync(slot, lit: false, PickUpLimit, ct);

        /// <summary>
        /// Parks the cursor off the card bar (a card in hand is drawn at the cursor and would cover the
        /// labels) and waits up to <paramref name="limit"/> for the slot's cost label to be lit or dark.
        /// </summary>
        private async Task<bool> SlotLabelAsync(int slot, bool lit, TimeSpan limit, CancellationToken ct)
        {
            _io.Move(_layout.Neutral);
            await Task.Delay(LabelSettleMs, ct).ConfigureAwait(false);
            var until = DateTime.UtcNow + limit;
            while (true)
            {
                if (_layout.SlotReady(CheckedGrab(), slot) == lit) return true;
                if (DateTime.UtcNow >= until) return false;
                await Task.Delay(40, ct).ConfigureAwait(false);
            }
        }

        /// <summary>A frame, after making sure the battle is still on: long formation walks must not sleep through the victory dialog's 8 s countdown.</summary>
        private StageFrame CheckedGrab()
        {
            var frame = _io.Grab();
            var screen = _detector.Detect(frame);
            if (screen == GameScreen.VictoryDialog || screen == GameScreen.AwaitHostDecision || screen == GameScreen.Loot) throw new BattleOverException(BattleOutcome.Victory);
            if (screen == GameScreen.Disconnected) throw new BattleOverException(BattleOutcome.Disconnected);
            if (screen == GameScreen.HealthNotice || screen == GameScreen.EventPopup) throw new CoveredException();
            return frame;
        }

        private async Task ShovelAsync(Cell cell, CancellationToken ct)
        {
            await _io.ClickAsync(_layout.Shovel).ConfigureAwait(false);
            await Task.Delay(60, ct).ConfigureAwait(false);
            await _io.ClickAsync(_layout.CellCenter(cell)).ConfigureAwait(false);
            await Task.Delay(SettleMs, ct).ConfigureAwait(false);
            // If the cell was empty the shovel stays in hand; a right-click puts it back.
            await _io.ClickAsync(_layout.Neutral, MouseButtonType.Right).ConfigureAwait(false);
            _log($"铲 {cell}");
        }

        /// <summary>Flame producers (小火炉, 日光炉, 酒杯灯…): their flames land in their own columns.</summary>
        private static bool IsFlameProducer(TrackCard card) => card.Name.Contains("炉") || card.Name.Contains("酒杯灯");

        /// <summary>
        /// Where pickups mostly lie: the last two columns and the strip right of them, where mice die
        /// as they arrive and drop their items, and (unless the account picks up flames by itself) the
        /// columns of flame producers. Adjacent columns merge into one span.
        /// </summary>
        private List<(int left, int right)> FocusSpans()
        {
            var spans = _plans.Where(p => !_autoFlame && IsFlameProducer(p.Card))
                .SelectMany(p => p.Card.Steps.Select(s => s.Cell.Col))
                .Distinct()
                .Select(BattleLayout.ColumnSpan)
                .ToList();
            spans.Add((BattleLayout.ColumnSpan(8).left, _layout.Pickups.Right));
            var merged = new List<(int left, int right)>();
            foreach (var span in spans.OrderBy(s => s.left))
            {
                if (merged.Count > 0 && span.left <= merged[merged.Count - 1].right)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].left, Math.Max(merged[merged.Count - 1].right, span.right));
                else
                    merged.Add(span);
            }
            return merged;
        }

        /// <summary>
        /// Up and down over the given column spans: vertical lines 30 px apart and a move every 20 px,
        /// so a ≈40 px pickup is always crossed (the cursor jumps between moves, so sparser paths skip
        /// items lying between the points).
        /// </summary>
        private List<Point> SweepPath(IEnumerable<(int left, int right)> spans)
        {
            var area = _layout.Pickups;
            var path = new List<Point>();
            bool downward = true;
            foreach (var (left, right) in spans)
                for (int x = left + 15; x < right; x += 30)
                {
                    var ys = Enumerable.Range(0, area.Height / 20 + 1).Select(i => area.Top + i * 20).ToList();
                    if (!downward) ys.Reverse();
                    path.AddRange(ys.Select(y => new Point(x, y)));
                    downward = !downward;
                }
            return path;
        }

        /// <summary>Continues the sweep for one slice: the focus path, with a pass over the whole field now and then.</summary>
        private async Task SweepSliceAsync(CancellationToken ct)
        {
            for (int i = 0; i < SweepSlice; i++)
            {
                _io.Move(_sweepPath[_sweepIndex]);
                await Task.Delay(12, ct).ConfigureAwait(false);
                if (++_sweepIndex < _sweepPath.Count) continue;
                _sweepIndex = 0;
                if (_sweepPath == _fullPath)
                {
                    FullSweeps++;
                    _sweepPath = _focusPath;
                }
                else
                {
                    Sweeps++;
                    if (DateTime.UtcNow - _fullSweepAt > _fullSweepEvery)
                    {
                        _fullSweepAt = DateTime.UtcNow;
                        _sweepPath = _fullPath;
                    }
                }
            }
            // Park off the field so hover effects stay off the cells the loop is about to look at.
            _io.Move(_layout.Neutral);
        }
    }
}
