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
    internal sealed class GrindSettings
    {
        public string TrackPath { get; set; }
        public TrackFile Track { get; set; }
        public FlameReader FlameReader { get; set; }
        public int Rounds { get; set; } = 1;

        /// <summary>Track card name → slot number in the deck actually carried (e.g. 海星 → the slot holding 红柳烤串机).</summary>
        public Dictionary<string, int> SlotOfCard { get; set; } = new Dictionary<string, int>();

        public int SlotCount { get; set; }
        public bool UseShovel { get; set; }

        /// <summary>Enter the boss stage (继续作战) after the normal stage. Consumes a key each time.</summary>
        public bool Boss { get; set; }

        /// <summary>The account picks up flames by itself: sweep only where items drop.</summary>
        public bool AutoFlame { get; set; }

        /// <summary>Level to walk to from 美味镇 / an island map when not already in its room; null = start in the room.</summary>
        public LevelEntry Level { get; set; }

        /// <summary>Where the level's map needs a carrier under a card (water, clouds); null when unknown.</summary>
        public CarrierMap CarrierMap { get; set; }

        /// <summary>Deck to carry, in slot order; null keeps whatever the room has.</summary>
        public List<(string name, StageFrame icon)> Deck { get; set; }

        /// <summary>Set for a two-player run controlled by the local coordinator.</summary>
        public DuoSession Duo { get; set; }
        public RoomNumberReader RoomNumbers { get; set; }
        public string DuoPassword { get; set; }
    }

    /// <summary>
    /// Repeats one stage from its room: close the 2P seat, 开始, fight with the track, either take the
    /// reward or (when enabled) go on to the boss stage in the same field, flip the one free treasure
    /// card, back to the room; after the final round, return to 美味镇.
    /// Stops on anything it does not recognise instead of guessing.
    /// </summary>
    internal sealed class GrindLoop
    {
        // Stage coordinates from the recorded 曲奇岛 run.
        private static readonly Point SecondSeat = new Point(248, 182);
        private static readonly Point StartButton = new Point(857, 456);
        private static readonly Point ContinueToBoss = new Point(468, 345);
        private static readonly Point TakeReward = new Point(568, 345);
        private static readonly Point Shuffle = new Point(708, 504);
        private static readonly Point FreeCard = new Point(709, 367);
        private static readonly Point Finish = new Point(709, 505);
        private static readonly Point Park = new Point(940, 300);
        private static readonly Rectangle TreasureCards = new Rectangle(482, 82, 456, 390);
        private static readonly Point WorldMapButton = new Point(866, 41);
        private static readonly Point TownOnWorldMap = new Point(500, 187);
        // Room list positions on 曲奇岛; other levels can shift the dialog right (ScreenDetector.RoomListOffset).
        // Upper half of 创建: the "任务完成了" bubble of the 任务 button can cover its bottom edge.
        private static readonly Point CreateRoom = new Point(587, 490);
        private static readonly Point PasswordBox = new Point(490, 454);
        private static readonly Point PasswordField = new Point(622, 454);

        /// <summary>
        /// Columns of the password field that differ from the empty field when it holds a password: four
        /// asterisks change 16, a blinking caret 1-2. (A mean over the field, used before, put a caret
        /// at ≈1.5 against ≈3.4 for asterisks, and took an empty focused field for a remembered password.)
        /// </summary>
        private const int PasswordTextColumns = 6;

        /// <summary>Green tick pixels in the 密码 checkbox: 100 when ticked, none otherwise.</summary>
        private const int PasswordTickPixels = 30;
        private static readonly Point LeaveRoom = new Point(915, 568);
        private static readonly TimeSpan NavigationStepLimit = TimeSpan.FromSeconds(15);
        internal static readonly Point HealthNoticeOk = new Point(425, 369);
        internal static readonly Point EventPopupClose = new Point(922, 112);

        /// <summary>
        /// The activity bar along the top of 美味镇 and island maps. Its ▶ button cycles: one row of
        /// icons → hidden → two rows → one row. After login it shows two rows, and the second row
        /// (down to y≈112) covers levels high up on their island: 深渊 at (356,77) sits under 马年活动,
        /// which opens the 金马盛典 page instead of the room list.
        /// </summary>
        private static readonly Point ActivityBarNext = new Point(787, 25);
        private static readonly Rectangle ActivityBarReach = new Rectangle(255, 0, 560, 130);
        private static readonly Rectangle ActivityBarRow2 = new Rectangle(270, 60, 490, 52);
        private const int ActivityBarSettleMs = 700;

        /// <summary>Second-row difference between two states that both show the map there (0.4; icons ≈65).</summary>
        private const double ActivityBarSameRow = 12;

        private static readonly TimeSpan UnknownLimit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ReloadAfter = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan StuckLoaderReloadAfter = TimeSpan.FromSeconds(30);
        /// <summary>Per round: the path to the servers can stay bad for minutes, and a run is meant to be unattended.</summary>
        private const int MaxReloads = 6;
        private static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(60);

        private readonly IGameIo _io;
        private readonly ScreenDetector _detector;
        private readonly GrindSettings _settings;
        private readonly TrackFile _track;
        private readonly BattleLayout _layout;
        private readonly string _runDirectory;
        private readonly LinkedList<string> _messages = new LinkedList<string>();
        private readonly object _gate = new object();

        public GrindLoop(IGameIo io, ScreenDetector detector, GrindSettings settings, string runDirectory)
        {
            _io = io;
            _detector = detector;
            _settings = settings;
            _runDirectory = runDirectory;
            _track = settings.Track ?? TrackFile.Load(settings.TrackPath);
            var compatibility = new TrackPreflight(_track, settings.CarrierMap, settings.FlameReader).Inspect(settings.SlotOfCard.Keys, settings.UseShovel,
                settings.Deck != null ? DeckBuilder.DeckSlots : 18, settings.Duo != null);
            compatibility.RequireCompatible();
            _layout = new BattleLayout(settings.SlotCount);
            Directory.CreateDirectory(runDirectory);
            foreach (var diagnostic in compatibility.Diagnostics) Say(diagnostic.ToString());
        }

        public int RoundsDone { get; private set; }

        /// <summary>Rounds whose boss stage was won.</summary>
        public int BossRoundsDone { get; private set; }
        public GameScreen LastScreen { get; private set; }
        public string State { get; private set; } = "starting";
        public DateTime StartedAt { get; } = DateTime.Now;
        public string RunDirectory => _runDirectory;

        /// <summary>Every progress line as it is written (raised on the loop's worker thread).</summary>
        public event Action<string> MessageLogged;

        public IReadOnlyList<string> Messages
        {
            get { lock (_gate) return _messages.ToList(); }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Say($"开始：{_settings.Level?.ToString() ?? "当前房间"}，{Path.GetFileName(_settings.TrackPath)} × {_settings.Rounds} 局，" +
                (_settings.Deck != null ? "卡组 " + string.Join("、", _settings.Deck.Select(c => c.name)) + "，" : "") +
                $"卡槽 {_settings.SlotCount} 个，映射 " + string.Join("、", _settings.SlotOfCard.Select(p => $"{p.Key}→{p.Value}")));
            try
            {
                await LoopAsync(ct).ConfigureAwait(false);
                State = "finished";
                Say($"完成 {RoundsDone} 局");
            }
            catch (OperationCanceledException)
            {
                State = "stopped";
                Say($"已停止（完成 {RoundsDone} 局）");
            }
            catch (Exception e)
            {
                State = "failed";
                Say("出错停止：" + e.Message);
                Snapshot("error");
                Log.Error("grind loop failed", e);
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            bool inRound = false, fought = false, victory = false;
            bool bossTried = false, bossWon = false, bossUnavailable = false;
            bool ownRoom = false; // this run created or verified the room for the requested level
            BattleRunner battle = null;
            int pickAttempts = 0, treasureStill = 0;
            StageFrame treasurePrevious = null;
            DateTime? unknownSince = null, loaderSince = null;
            bool onIslandMap = false; // set right after picking the island on the world map
            bool duoSelectedRoomList = false; // set only after clicking the requested stage
            int navigations = 0, popups = 0, reloads = 0;
            int duoDeckPreparedRound = 0, duoRoomActionRound = 0, duoVerifiedRoomRound = 0;
            DateTime duoRoomActionAt = DateTime.MinValue;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var frame = _io.Grab();
                var screen = _detector.Detect(frame);
                if (screen != LastScreen) Log.Info($"grind: screen {LastScreen} → {screen}");
                LastScreen = screen;
                bool lost = screen == GameScreen.Unknown || screen == GameScreen.Loading100;
                unknownSince = lost ? unknownSince ?? DateTime.UtcNow : (DateTime?)null;
                loaderSince = screen == GameScreen.Loading100 ? loaderSince ?? DateTime.UtcNow : (DateTime?)null;

                switch (screen)
                {
                    case GameScreen.Room:
                        if (inRound)
                        {
                            if (!victory) throw new InvalidOperationException("这一局没有看到胜利弹窗就回到了房间");
                            RoundsDone++;
                            if (bossWon) BossRoundsDone++;
                            inRound = false;
                            reloads = popups = 0; // budgets are per round, so long runs survive the odd disconnect
                            Say($"第 {RoundsDone} 局完成" + (bossWon ? "（含 boss）" : ""));
                        }
                        if (RoundsDone >= _settings.Rounds && (_settings.Duo == null || _settings.RoomNumbers == null))
                        {
                            if (_settings.Duo == null) await ReturnToTownAsync(ct).ConfigureAwait(false);
                            return;
                        }

                        if (_settings.Duo != null)
                        {
                            if (_settings.Level != null && !ownRoom)
                            {
                                throw new InvalidOperationException(
                                    "Automatic duo pairing must start from a map; the current room's stage cannot be verified.");
                            }
                            int round = RoundsDone + 1;
                            if (_settings.RoomNumbers != null && duoVerifiedRoomRound != round)
                            {
                                var roomNumber = await ReadStableRoomNumberAsync(ct).ConfigureAwait(false);
                                _settings.Duo.SetRoomNumber(roomNumber);
                                duoVerifiedRoomRound = round;
                            }
                            if (RoundsDone >= _settings.Rounds) return;
                            if (duoRoomActionRound == round)
                            {
                                State = "duo-awaiting-battle";
                                if (DateTime.UtcNow - duoRoomActionAt > StartLimit)
                                    throw new TimeoutException("The paired room did not enter battle after Start/Ready.");
                                await Task.Delay(300, ct).ConfigureAwait(false);
                                break;
                            }

                            // An open empty or closed 2P seat is not an acceptable host room. The
                            // occupied seat has no positive template yet. A stable room ID and the
                            // coordinator's room gate provide the available automatic checks.
                            if (_settings.Duo.Role == DuoRole.Host)
                            {
                                var partnerSeat = _detector.SecondSeat(frame);
                                if (partnerSeat != RoomSeat.Unknown)
                                {
                                    State = "duo-waiting-partner";
                                    await Task.Delay(300, ct).ConfigureAwait(false);
                                    break;
                                }
                            }
                            if (duoDeckPreparedRound != round && _settings.Deck != null)
                            {
                                try
                                {
                                    await new DeckBuilder(_io, Say, f => _detector.Detect(f) == GameScreen.Room)
                                        .EnsureAsync(_settings.Deck, ct).ConfigureAwait(false);
                                }
                                catch (RoomCoveredException)
                                {
                                    Say("选卡被弹窗打断，先关掉弹窗再选");
                                    break;
                                }
                                duoDeckPreparedRound = round;
                            }
                            State = "duo-room-ready";
                            if (!_settings.Duo.RoomReleased(round))
                            {
                                await Task.Delay(300, ct).ConfigureAwait(false);
                                break;
                            }
                            duoRoomActionRound = round;
                            duoRoomActionAt = DateTime.UtcNow;
                            Say($"第 {round} 局：{(_settings.Duo.Role == DuoRole.Host ? "房主开始" : "队员准备")}");
                            await ClickAndParkAsync(StartButton, 500, ct).ConfigureAwait(false);
                            State = "duo-awaiting-battle";
                            break;
                        }

                        if (_settings.Level != null && !ownRoom)
                        {
                            // A room that was already open may belong to another level; nothing on this
                            // screen says which reliably, so leave it and create one.
                            if (++navigations > 20) throw new InvalidOperationException("走到关卡房间的步骤太多，停下检查");
                            Say($"已经在一个房间里，先离开，再去 {_settings.Level} 建房间");
                            await ClickAndParkAsync(LeaveRoom, 300, ct).ConfigureAwait(false);
                            await WaitForAsync(s => s != GameScreen.Room, NavigationStepLimit, ct).ConfigureAwait(false);
                            break;
                        }

                        State = $"room (round {RoundsDone + 1}/{_settings.Rounds})";
                        // Lock the room first, before anything that takes a while. A new room draws its
                        // seats a moment after it appears.
                        var seat = _detector.SecondSeat(frame);
                        for (var until = DateTime.UtcNow.AddSeconds(4); seat == RoomSeat.Unknown && DateTime.UtcNow < until;)
                        {
                            await Task.Delay(300, ct).ConfigureAwait(false);
                            seat = _detector.SecondSeat(_io.Grab());
                        }
                        if (seat == RoomSeat.Waiting)
                        {
                            Say("关闭 2P 位置");
                            await ClickAndParkAsync(SecondSeat, 300, ct).ConfigureAwait(false);
                            // The seat toggles: wait for the change before looking again, or a second click reopens it.
                            for (var until = DateTime.UtcNow.AddSeconds(2); DateTime.UtcNow < until && _detector.SecondSeat(_io.Grab()) == RoomSeat.Waiting;)
                                await Task.Delay(200, ct).ConfigureAwait(false);
                            continue;
                        }
                        if (seat == RoomSeat.Unknown) Say("认不出 2P 位置的状态，照常开始");

                        if (_settings.Deck != null)
                        {
                            try
                            {
                                await new DeckBuilder(_io, Say, f => _detector.Detect(f) == GameScreen.Room).EnsureAsync(_settings.Deck, ct).ConfigureAwait(false);
                            }
                            catch (RoomCoveredException)
                            {
                                Say("选卡被弹窗打断，先关掉弹窗再选");
                                continue;
                            }
                        }

                        Say($"第 {RoundsDone + 1} 局：开始");
                        await ClickAndParkAsync(StartButton, 500, ct).ConfigureAwait(false);
                        await WaitUntilAsync(s => s != GameScreen.Room, StartLimit, "点了开始但没有离开房间", ct).ConfigureAwait(false);
                        inRound = true;
                        fought = victory = bossTried = bossWon = false;
                        battle = null;
                        pickAttempts = treasureStill = 0;
                        treasurePrevious = null;
                        break;

                    case GameScreen.Battle:
                        if (!inRound)
                        {
                            inRound = true;
                            fought = victory = bossTried = bossWon = false;
                            battle = null;
                            pickAttempts = treasureStill = 0;
                            treasurePrevious = null;
                            Say("接管进行中的战斗");
                        }
                        if (fought)
                        {
                            await Task.Delay(300, ct).ConfigureAwait(false);
                            break;
                        }
                        fought = true;
                        State = $"battle (round {RoundsDone + 1})";
                        var runner = battle = new BattleRunner(_io, _detector, _layout, _track, _settings.SlotOfCard, _settings.UseShovel, _settings.AutoFlame, _settings.CarrierMap, Say, _settings.FlameReader);
                        var outcome = await runner.RunAsync(ct, _settings.Duo == null ? null : WaitForDuoBattleAsync).ConfigureAwait(false);
                        victory = outcome == BattleOutcome.Victory;
                        Say($"战斗结束：{outcome}，放卡 {runner.Placements} 次，未放进 {runner.FailedAttempts} 次（其中被丢掉 {runner.Drops} 次），没拿起来 {runner.PickUpFailures} 次，放卡后又回到手上 {runner.LateRefusals} 次，重点扫 {runner.Sweeps} 遍、全场扫 {runner.FullSweeps} 遍");
                        if (runner.RefusalSummary.Length > 0) Say("放不进的格子：" + runner.RefusalSummary);
                        if (outcome == BattleOutcome.Disconnected) break; // handled as its own screen on the next pass
                        if (outcome != BattleOutcome.Victory)
                        {
                            Snapshot("battle-" + outcome);
                            throw new InvalidOperationException("战斗没有以胜利弹窗结束：" + outcome);
                        }
                        break;

                    case GameScreen.VictoryDialog:
                        victory = true;
                        if (_settings.Boss && !bossUnavailable && !bossTried && battle != null)
                        {
                            if (_settings.Duo != null && !_settings.Duo.BossReleased(RoundsDone + 1))
                            {
                                State = "duo-boss-ready";
                                await Task.Delay(150, ct).ConfigureAwait(false);
                                break;
                            }
                            bossTried = true;
                            Snapshot($"round{RoundsDone + 1}-victory");
                            Say($"通关战胜利：继续作战，进 boss 关（消耗 {_settings.Level?.Key ?? "钥匙"}）");
                            // The dialog fades out and the same battle goes on; start playing only once the
                            // dialog is gone, or the runner would take it for the end of the boss stage.
                            // A click right after the dialog appeared was once ignored, so try again while the
                            // ≈8 s countdown lasts, keeping a picture of what each click led to.
                            bool entered = false;
                            if (_settings.Duo?.Role == DuoRole.Guest)
                            {
                                Say("Waiting for the host to continue into the boss stage");
                                entered = await WaitForAsync(s => s == GameScreen.Battle, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                            }
                            else for (int attempt = 1; attempt <= 3 && !entered; attempt++)
                            {
                                await ClickAndParkAsync(ContinueToBoss, 300, ct).ConfigureAwait(false);
                                entered = await WaitForAsync(s => s == GameScreen.Battle, TimeSpan.FromSeconds(2.2), ct).ConfigureAwait(false);
                                if (!entered)
                                {
                                    Snapshot($"boss-click{attempt}");
                                    if (_detector.Detect(_io.Grab()) != GameScreen.VictoryDialog) break;
                                    Say($"点了继续作战，第 {attempt} 次没有反应");
                                }
                            }
                            if (!entered && !await WaitForAsync(s => s == GameScreen.Battle, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false))
                            {
                                Snapshot("boss-refused");
                                bossUnavailable = true;
                                Say("点了继续作战但没有进入 boss 关（钥匙用完了？），这局领取奖品，之后不再进 boss 关");
                                break;
                            }
                            State = $"boss (round {RoundsDone + 1})";
                            var bossOutcome = await battle.ContinueAsync(ct).ConfigureAwait(false);
                            Say($"boss 战结束：{bossOutcome}，累计放卡 {battle.Placements} 次，重点扫 {battle.Sweeps} 遍、全场扫 {battle.FullSweeps} 遍");
                            if (bossOutcome == BattleOutcome.Disconnected) break;
                            if (bossOutcome != BattleOutcome.Victory)
                            {
                                Snapshot("boss-" + bossOutcome);
                                throw new InvalidOperationException("boss 战没有以胜利结束：" + bossOutcome);
                            }
                            bossWon = true;
                            Snapshot($"round{RoundsDone + 1}-boss-victory");
                            break;
                        }
                        // Also reached after a boss stage, should the game show this dialog again.
                        Snapshot($"round{RoundsDone + 1}-victory");
                        Say("胜利：领取奖品" + (_settings.Boss ? "" : "（不进 boss 关）"));
                        await ClickAndParkAsync(TakeReward, 1200, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.AwaitHostDecision:
                        if (_settings.Duo?.Role != DuoRole.Guest)
                            throw new InvalidOperationException("A host-choice dialog appeared outside the guest duo flow.");
                        victory = true;
                        if (_settings.Boss && !bossTried && battle != null)
                        {
                            State = "duo-boss-ready";
                            if (!_settings.Duo.BossReleased(RoundsDone + 1))
                            {
                                await Task.Delay(150, ct).ConfigureAwait(false);
                                break;
                            }
                            bossTried = true;
                            Say("Waiting for the host to continue into the boss stage");
                            if (!await WaitForAsync(s => s == GameScreen.Battle, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false))
                            {
                                bossUnavailable = true;
                                Say("The host did not enter the boss stage.");
                                break;
                            }
                            State = $"boss (round {RoundsDone + 1})";
                            var guestBossOutcome = await battle.ContinueAsync(ct).ConfigureAwait(false);
                            if (guestBossOutcome != BattleOutcome.Victory)
                            {
                                Snapshot("boss-" + guestBossOutcome);
                                throw new InvalidOperationException("Guest boss battle did not end in victory: " + guestBossOutcome);
                            }
                            bossWon = true;
                            Snapshot($"round{RoundsDone + 1}-boss-victory");
                        }
                        else await Task.Delay(250, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.TreasureShuffle:
                        Say("翻宝箱：开始洗牌");
                        await ClickAndParkAsync(Shuffle, 2500, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.TreasurePick:
                        // Only the first flip is free; any later card click costs 500 gold. So click a card
                        // once per round, and only when the 完成 button is clearly absent (a hovered or
                        // half-drawn 完成 button would otherwise make a paid flip look like a free one).
                        // Recorded diffs: absent ≈72, present-but-hovered ≈39, present 0.
                        // Clicks during the shuffle animation are ignored, so also wait until the cards stop moving.
                        var cards = frame.Crop(TreasureCards);
                        treasureStill = treasurePrevious != null && cards.MeanAbsDiff(treasurePrevious) < 2 ? treasureStill + 1 : 0;
                        treasurePrevious = cards;
                        if (pickAttempts == 0 && treasureStill >= 2 && _detector.Diff("翻宝箱_完成", frame) > 55)
                        {
                            pickAttempts++;
                            Say("翻宝箱：翻免费的一张");
                            await ClickAndParkAsync(FreeCard, 1500, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(300, ct).ConfigureAwait(false);
                        }
                        break;

                    case GameScreen.TreasureDone:
                        // Further flips cost gold; never click another card here.
                        Snapshot($"round{RoundsDone + 1}-treasure");
                        Say("翻宝箱：完成");
                        await ClickAndParkAsync(Finish, 1500, ct).ConfigureAwait(false);
                        // A step that did not arrive (e.g. a login popup covered the map) is simply
                        // re-evaluated from whatever screen is showing now.
                        break;

                    case GameScreen.Disconnected:
                        if (_settings.Duo != null)
                            throw new InvalidOperationException("A duo account disconnected; stop both runs and pair the room again.");
                        if (++reloads > MaxReloads)
                        {
                            Snapshot("disconnected");
                            throw new InvalidOperationException($"和游戏服务器断开连接，已经重新登录 {MaxReloads} 次");
                        }
                        Snapshot("disconnected");
                        Say($"和游戏服务器断开连接，刷新页面重新登录（第 {reloads} 次）" + (inRound ? "，这一局作废" : ""));
                        inRound = ownRoom = false;
                        onIslandMap = false;
                        navigations = popups = 0;
                        _io.Reload();
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.HealthNotice:
                    case GameScreen.EventPopup:
                        if (++popups > 10) throw new InvalidOperationException("弹窗关不掉");
                        Say(screen == GameScreen.HealthNotice ? "关闭健康提示" : "关闭活动弹窗");
                        await ClickAndParkAsync(screen == GameScreen.HealthNotice ? HealthNoticeOk : EventPopupClose, 1000, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.Loot:
                    case GameScreen.Settlement:
                        await Task.Delay(300, ct).ConfigureAwait(false);
                        break;

                    case GameScreen.Lobby:
                    case GameScreen.WorldMap:
                    case GameScreen.RoomList:
                        if (_settings.Duo != null && (_settings.Level == null || ownRoom || inRound))
                            throw new InvalidOperationException("The paired room was lost; stop both runs and pair again.");
                        if (inRound)
                        {
                            Snapshot("unexpected-" + screen);
                            throw new InvalidOperationException($"一局还没结束就回到了 {screen}");
                        }
                        var level = _settings.Level ?? throw new InvalidOperationException($"当前在 {screen}，请先进入关卡房间并选好卡组（或指定关卡让程序自己走过去）");
                        if (++navigations > 20) throw new InvalidOperationException("走到关卡房间的步骤太多，停下检查");
                        State = "navigating to " + level;
                        if (screen == GameScreen.WorldMap)
                        {
                            duoSelectedRoomList = false;
                            Say($"世界地图 → {level.Island}");
                            await ClickAndParkAsync(level.IslandAt, 300, ct).ConfigureAwait(false);
                            onIslandMap = await WaitForAsync(s => s == GameScreen.Lobby, NavigationStepLimit, ct).ConfigureAwait(false);
                        }
                        else if (screen == GameScreen.RoomList)
                        {
                            if (_settings.Duo != null && !duoSelectedRoomList)
                                throw new InvalidOperationException(
                                    "Automatic duo pairing must start from a map; this room list's stage cannot be verified.");
                            int dx = _detector.RoomListOffset(frame) ?? 0;
                            if (_settings.Duo?.Role == DuoRole.Guest)
                            {
                                State = "duo-finding-room";
                                var target = await FindDuoRoomAsync(_settings.Duo.TargetRoom, dx, ct).ConfigureAwait(false);
                                Say("Found the host room in the list; entering with the temporary password");
                                await ClickAndParkAsync(target, 400, ct).ConfigureAwait(false);
                                await _io.TypeAsync(new string('\b', 8) + _settings.DuoPassword).ConfigureAwait(false);
                                await ClickAndParkAsync(new Point(475 + dx, 335), 400, ct).ConfigureAwait(false);
                                await WaitUntilAsync(s => s == GameScreen.Room, NavigationStepLimit,
                                    "The guest did not enter the host room.", ct).ConfigureAwait(false);
                                var number = await ReadStableRoomNumberAsync(ct).ConfigureAwait(false);
                                _settings.Duo.SetRoomNumber(number);
                                ownRoom = true; // verified against the room number supplied by the host
                                Say("Guest entered the verified host room");
                            }
                            else if (_settings.Duo?.Role == DuoRole.Host)
                            {
                                await SetDuoRoomPasswordAsync(dx, ct).ConfigureAwait(false);
                                Say("Creating the temporary-password duo room");
                                await ClickAndParkAsync(new Point(CreateRoom.X + dx, CreateRoom.Y), 300, ct).ConfigureAwait(false);
                                await WaitUntilAsync(s => s == GameScreen.Room, NavigationStepLimit,
                                    "The host did not enter the new room.", ct).ConfigureAwait(false);
                                var number = await ReadStableRoomNumberAsync(ct).ConfigureAwait(false);
                                _settings.Duo.SetRoomNumber(number);
                                ownRoom = true;
                                Say("Host room number recognized; waiting for the guest");
                            }
                            else
                            {
                                Say("创建房间（" + await SetRoomPasswordAsync(dx, ct).ConfigureAwait(false) + "）");
                                await ClickAndParkAsync(new Point(CreateRoom.X + dx, CreateRoom.Y), 300, ct).ConfigureAwait(false);
                                ownRoom = await WaitForAsync(s => s == GameScreen.Room, NavigationStepLimit, ct).ConfigureAwait(false);
                            }
                        }
                        else if (onIslandMap)
                        {
                            // 美味镇 and island maps look alike to the detector, so only click a level
                            // right after arriving here from the world map.
                            onIslandMap = false;
                            if (ActivityBarReach.Contains(level.At)) await HideActivityBarAsync(ct).ConfigureAwait(false);
                            Say($"{level.Island} → {level.Name}");
                            await ClickAndParkAsync(level.At, 300, ct).ConfigureAwait(false);
                            duoSelectedRoomList = _settings.Duo != null;
                            await WaitForAsync(s => s == GameScreen.RoomList, NavigationStepLimit, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            duoSelectedRoomList = false;
                            Say("打开世界地图");
                            await ClickAndParkAsync(WorldMapButton, 300, ct).ConfigureAwait(false);
                            await WaitForAsync(s => s == GameScreen.WorldMap, NavigationStepLimit, ct).ConfigureAwait(false);
                        }
                        break;

                    default:
                        if (_settings.Duo != null)
                        {
                            var duoUnknownLimit = screen == GameScreen.Loading100 ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(45);
                            if (DateTime.UtcNow - Later(unknownSince.Value, _io.LastPageLoadUtc) > duoUnknownLimit)
                            {
                                Snapshot("duo-unknown");
                                throw new TimeoutException("The paired account's screen did not recover; stop both runs.");
                            }
                            await Task.Delay(400, ct).ConfigureAwait(false);
                            break;
                        }
                        // A loader stuck at 100% or a dropped connection: reload once in a while.
                        // Count from the latest page load too: the browser reloads a stalled game frame by itself.
                        var reloadAfter = screen == GameScreen.Loading100 ? StuckLoaderReloadAfter : ReloadAfter;
                        var waitingSince = Later(screen == GameScreen.Loading100 ? loaderSince.Value : unknownSince.Value, _io.LastPageLoadUtc);
                        if (DateTime.UtcNow - waitingSince > reloadAfter && reloads < MaxReloads)
                        {
                            reloads++;
                            Snapshot("reload");
                            Say(screen == GameScreen.Loading100
                                ? $"停在“加载中 100%”{reloadAfter.TotalSeconds:0} 秒（登录服务器没响应），刷新页面（第 {reloads} 次）"
                                : $"{reloadAfter.TotalSeconds:0} 秒认不出画面，刷新页面（第 {reloads} 次）");
                            if (inRound) Say("这一局作废");
                            inRound = ownRoom = false;
                            onIslandMap = false;
                            navigations = popups = 0;
                            _io.Reload();
                            unknownSince = DateTime.UtcNow;
                            await Task.Delay(5000, ct).ConfigureAwait(false);
                            break;
                        }
                        if (DateTime.UtcNow - Later(unknownSince.Value, _io.LastPageLoadUtc) > UnknownLimit)
                        {
                            Snapshot("stuck");
                            throw new TimeoutException($"{UnknownLimit.TotalSeconds:0} 秒内认不出当前画面");
                        }
                        await Task.Delay(400, ct).ConfigureAwait(false);
                        break;
                }
            }
        }

        private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

        private async Task<int> ReadStableRoomNumberAsync(CancellationToken ct)
        {
            if (_settings.RoomNumbers == null) throw new InvalidOperationException("Room digit masks are unavailable.");
            int last = 0;
            var until = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                var value = _settings.RoomNumbers.ReadRoom(_io.Grab()) ?? 0;
                if (value > 0 && value == last) return value;
                last = value;
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            Snapshot("room-number-unreadable");
            throw new InvalidOperationException("The room number could not be verified from two frames.");
        }

        private async Task<Point> FindDuoRoomAsync(int number, int dx, CancellationToken ct)
        {
            if (_settings.RoomNumbers == null || number <= 0)
                throw new InvalidOperationException("A target room number and local digit masks are required.");
            Point? last = null;
            var until = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                var frame = _io.Grab();
                if (_detector.Detect(frame) != GameScreen.RoomList)
                    throw new InvalidOperationException("The room list changed while locating the host room.");
                var match = _settings.RoomNumbers.FindListRoom(frame, number, dx);
                if (match.HasValue && last.HasValue && Math.Abs(match.Value.Y - last.Value.Y) <= 2)
                    return match.Value;
                last = match;
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            Snapshot("room-not-found");
            throw new TimeoutException("The host room number was not visible in the room list.");
        }

        private async Task SetDuoRoomPasswordAsync(int dx, CancellationToken ct)
        {
            var box = new Point(PasswordBox.X + dx, PasswordBox.Y);
            if (!PasswordTicked(_io.Grab(), dx)) await ClickAndParkAsync(box, 350, ct).ConfigureAwait(false);
            if (!PasswordTicked(_io.Grab(), dx))
                throw new InvalidOperationException("The room password checkbox could not be enabled.");
            await ClickAndParkAsync(new Point(PasswordField.X + dx, PasswordField.Y), 250, ct).ConfigureAwait(false);
            await _io.TypeAsync(new string('\b', 8) + _settings.DuoPassword).ConfigureAwait(false);
            await Task.Delay(300, ct).ConfigureAwait(false);
            int changed = _detector.ChangedColumns("密码框_空", _io.Grab(), dx);
            if (changed < 10 || changed > 24)
                throw new InvalidOperationException("The temporary room password was not confirmed in the field.");
        }

        private async Task WaitForDuoBattleAsync(CancellationToken ct)
        {
            int round = RoundsDone + 1;
            State = "duo-battle-ready";
            Say($"第 {round} 局：人物已放置，等待另一账户的基准帧");
            var until = DateTime.UtcNow.AddSeconds(45);
            while (!_settings.Duo.BattleReleased(round))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > until) throw new TimeoutException("The second account did not reach the battle gate.");
                await Task.Delay(150, ct).ConfigureAwait(false);
            }
            State = $"battle (round {round})";
        }

        /// <summary>Leave the completed room and use the world map's 美味镇 destination.</summary>
        private async Task ReturnToTownAsync(CancellationToken ct)
        {
            State = "returning to town";
            Say("全部战斗完成，离开房间返回美味镇");
            await ClickAndParkAsync(LeaveRoom, 300, ct).ConfigureAwait(false);
            await WaitUntilAsync(s => s == GameScreen.Lobby, NavigationStepLimit,
                "离开房间后没有到达关卡地图", ct).ConfigureAwait(false);
            await ClickAndParkAsync(WorldMapButton, 300, ct).ConfigureAwait(false);
            await WaitUntilAsync(s => s == GameScreen.WorldMap, NavigationStepLimit,
                "关卡地图没有打开世界地图", ct).ConfigureAwait(false);
            await ClickAndParkAsync(TownOnWorldMap, 300, ct).ConfigureAwait(false);
            await WaitUntilAsync(s => s == GameScreen.Lobby, NavigationStepLimit,
                "世界地图没有返回美味镇", ct).ConfigureAwait(false);
            LastScreen = GameScreen.Lobby;
            Say("已返回美味镇");
        }

        /// <summary>
        /// Ticks 密码 on the create-room panel so passers-by cannot join. The game fills in the password
        /// used last time; only an empty field gets a new random 4-digit one (typing into a filled
        /// field would append to it). Returns a description for the log.
        /// </summary>
        /// <summary>
        /// Ticks 密码 and makes sure the field holds a password. Within one login the game refills the last
        /// password when the box is ticked; after a new login the field stays empty, and 创建 then does
        /// nothing. The checkbox toggles, so its tick is read rather than assumed; a field that stays empty
        /// is unticked again so the room can still be created (the closed 2P seat keeps others out).
        /// </summary>
        private async Task<string> SetRoomPasswordAsync(int dx, CancellationToken ct)
        {
            var box = new Point(PasswordBox.X + dx, PasswordBox.Y);
            if (!PasswordTicked(_io.Grab(), dx)) await ClickAndParkAsync(box, 500, ct).ConfigureAwait(false);
            if (!PasswordTicked(_io.Grab(), dx))
            {
                Snapshot("password-not-ticked");
                return "勾不上“密码”，只靠关闭 2P 位置";
            }
            if (PasswordTyped(_io.Grab(), dx)) return "密码：沿用游戏记住的上次密码";

            var password = new Random().Next(1000, 10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await ClickAndParkAsync(new Point(PasswordField.X + dx, PasswordField.Y), 300, ct).ConfigureAwait(false);
            await _io.TypeAsync(password).ConfigureAwait(false);
            await Task.Delay(300, ct).ConfigureAwait(false);
            if (PasswordTyped(_io.Grab(), dx)) return "密码 " + password;

            Snapshot("password-not-typed");
            await ClickAndParkAsync(box, 300, ct).ConfigureAwait(false);
            return PasswordTicked(_io.Grab(), dx)
                ? "密码没输进去，“密码”也取消不了勾选"
                : "密码没输进去，已取消勾选“密码”，只靠关闭 2P 位置";
        }

        private bool PasswordTicked(StageFrame frame, int dx)
        {
            var r = new Rectangle(PasswordBox.X - 14 + dx, PasswordBox.Y - 14, 30, 28);
            int green = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = (y * frame.Width + x) * 4;
                    int b = frame.Bgra[i], g = frame.Bgra[i + 1], red = frame.Bgra[i + 2];
                    if (g >= 150 && g - red >= 60 && g - b >= 60) green++;
                }
            return green >= PasswordTickPixels;
        }

        private bool PasswordTyped(StageFrame frame, int dx) => _detector.ChangedColumns("密码框_空", frame, dx) >= PasswordTextColumns;

        private async Task ClickAndParkAsync(Point at, int settleMs, CancellationToken ct)
        {
            await _io.ClickAsync(at).ConfigureAwait(false);
            _io.Move(Park); // hovered buttons change colour and would confuse detection
            await Task.Delay(settleMs, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Puts the activity bar into its hidden state. Its state is not visible from the ▶ button, so
        /// press it twice and compare the three states seen: the one-row and hidden states show the same
        /// map under the second row, and hidden comes right after one-row.
        /// </summary>
        private async Task HideActivityBarAsync(CancellationToken ct)
        {
            var seen = new List<StageFrame> { _io.Grab() };
            for (int i = 0; i < 2; i++)
            {
                await ClickAndParkAsync(ActivityBarNext, ActivityBarSettleMs, ct).ConfigureAwait(false);
                seen.Add(_io.Grab());
            }
            // seen[k] is the bar after k presses; three presses bring it back to seen[0].
            var row2 = Enumerable.Range(0, 3)
                .Select(k => seen[k].Crop(ActivityBarRow2).MeanAbsDiff(seen[(k + 1) % 3].Crop(ActivityBarRow2)))
                .ToList();
            var oneRow = Enumerable.Range(0, 3).Where(k => row2[k] < ActivityBarSameRow).ToList();
            if (oneRow.Count != 1)
            {
                Snapshot("activity-bar");
                Say($"认不出顶部活动栏的状态（第二排差值 {string.Join(" / ", row2.Select(d => d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))}），直接点关卡");
                return;
            }
            int hidden = (oneRow[0] + 1) % 3;
            int presses = (hidden + 1) % 3; // from seen[2] to seen[hidden]
            for (int i = 0; i < presses; i++)
                await ClickAndParkAsync(ActivityBarNext, ActivityBarSettleMs, ct).ConfigureAwait(false);
            Say("收起顶部活动栏");
        }

        /// <summary>Polls until the screen satisfies <paramref name="done"/>; false on timeout.</summary>
        private async Task<bool> WaitForAsync(Func<GameScreen, bool> done, TimeSpan limit, CancellationToken ct)
        {
            var until = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < until)
            {
                var screen = _detector.Detect(_io.Grab());
                if (done(screen)) return true;
                if (screen == GameScreen.HealthNotice || screen == GameScreen.EventPopup) return false; // let the loop close it
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            return false;
        }

        private async Task WaitUntilAsync(Func<GameScreen, bool> done, TimeSpan limit, string failure, CancellationToken ct)
        {
            var until = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < until)
            {
                if (done(_detector.Detect(_io.Grab()))) return;
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            Snapshot("timeout");
            throw new TimeoutException(failure);
        }

        private void Snapshot(string label)
        {
            try
            {
                var path = Path.Combine(_runDirectory, $"{DateTime.Now:HHmmss-fff}_{label}.png");
                using (var bmp = _io.Grab().ToBitmap())
                    bmp.Save(path, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Log.Warn("snapshot failed: " + e.Message);
            }
        }

        private void Say(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {message}";
            Log.Info("grind: " + message);
            lock (_gate)
            {
                _messages.AddLast(line);
                while (_messages.Count > 200) _messages.RemoveFirst();
            }
            MessageLogged?.Invoke(line);
            try
            {
                File.AppendAllText(Path.Combine(_runDirectory, "run.log"), line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}
