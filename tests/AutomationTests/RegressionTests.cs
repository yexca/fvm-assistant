using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using FvmAssistant.Automation;
using FvmAssistant.Panel;
using FvmAssistant.Controller;

namespace FvmAssistant.Tests
{
    internal static class RegressionTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BattleLayout Layout = new BattleLayout(1);
        private static int _passed;
        private static string _scratch;

        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Members).SetValue(target, value);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
        private static object Plan(BattleRunner runner) => ((IList)Get(runner, "_plans"))[0];
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); _passed++; }

        private static int Main()
        {
            _scratch = Path.Combine(Path.GetTempPath(), "fvm-automation-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratch);
            try
            {
                Test("Agent plans reject malformed mappings and preserve execution order", AgentTests.Plans);
                Test("Agent request identity survives retries and rejects stale instances", AgentTests.RequestIdentity);
                Test("Agent stops cannot affect a replacement run", AgentTests.StaleStop);
                Test("Agent catalogs and preflight use synthetic account resources without browser input", () => AgentTests.Catalogs(_scratch));
                Test("MCP stdio negotiates, validates and routes only to the configured local account", () => AgentTests.Mcp(_scratch));
                Test("timed-only maps, aliases, explicit overrides and fixed support", CarrierDefaults);
                Test("renderer failures block old frames until a new navigation and viewport paint", RendererRecovery);
                Test("mission state distinguishes active, complete, claimed and unknown", MissionStates);
                Test("mission list identity survives header animation but rejects moved labels", MissionListAnimation);
                Test("mission actions require fresh evidence and verify claim and acceptance", MissionGuards);
                Test("failed claims retain transient feedback and current evidence without another click", MissionClaimFeedback);
                Test("mission row selection reaches pending rewards without bypassing fresh evidence", MissionSelection);
                Test("completed reward rows follow badge positions and reject unrelated colors and noise", MissionRewardRows);
                Test("task board navigation validates each transition and never selects, draws or claims", MissionNavigation);
                Test("guild publication recognizes the gold button while rejecting gray and unrelated colors", GuildButtonColors);
                Test("transition polling updates the reported screen before resuming a boss", TransitionScreenStatus);
                Test("recognized victory and notices outrank the guest-choice pixel heuristic", GuestChoicePrecedence);
                Test("finish after round preserves boss continuation and free rewards without starting another battle", FinishRoundRewards);
                Test("guild publication retains its transient submenu and draws only once", GuildPublication);
                Test("late first attempt gets retries and future events remain reachable", TimedRetries);
                Test("completed events stay complete when a late rejection is restored", LateSchedule);
                Test("last empty cell can be probed again after a transient failure", LastEmptyCell);
                Test("unknown-cell probes rotate instead of starving later cells", ProbeFairness);
                Test("card colors survive moving backgrounds and short occlusions", Presence);
                Test("ownership refresh detects loss in a fully occupied column", RefreshOwnership);
                Test("unverified animated ownership eventually returns to probing", UnverifiedOwnership);
                Test("placement learns the held card through the existing IO interface", HeldSignatureLearning);
                Test("real placement confirmation restores a late-rejected timed step", PlacementRollback);
                Test("refused timed support stays pending", RefusedSupport);
                Test("timed cloud activation blocks ordinary cards until support exists", SupportDependency);
                Test("preflight reports damaged input without silently accepting omitted steps", InvalidTracks);
                Test("fractional track steps retain tenths through scheduling and support", FractionalSteps);
                Test("preflight distinguishes active conditions, skipped cards and defaults", SelectedCompatibility);
                Test("flame digits distinguish zero from unreadable frames", FlameDigits);
                Test("condition clocks, triggers and termination use confirmed placements", ConditionSemantics);
                Test("card-count conditions use observed ordinary cards", CardCountConditions);
                Test("layer-5 targets occupied cells without claiming board ownership", SpecialCardTargets);
                Test("preflight blocks unsupported timing, waves, flags and headers", UnsupportedTracks);
                Test("scheduled fill uses timed 00 events without clicking a zero cell", ScheduledFill);
                Test("synchronized timing pauses for ordinary placements", SynchronizedTiming);
                Test("track card rate limit accepts only the declared attempt cadence", PlantSpeed);
                Test("step editor preserves source lines and supports undo, redo and copy save", TrackEditing);
                Test("inventory grid rejects overlap and clipping before any learning", InventoryCalibration);
                Test("inventory alignment needs distinct known icons and an unambiguous offset", InventoryAlignment);
                Test("room scrolling waits for a delayed rendered list before declaring its end", DeckScrollDelay);
                Test("late remembered room decks rebuild once and never start with extra cards", DelayedRoomDeck);
                Test("inventory learning saves reviewed pixels and rejects stale or invalid batches", InventoryLearning);
                Test("new formations paint and erase exact times with coherent card counts and undo", FormationEditing);
                Test("track editor and board preview construct on a WinForms UI thread", TrackEditorUi);
                Test("formation board and inventory scanner support attended editing without game input", FormationWorkflowUi);
                Test("fast formation drags paint crossed cells and erase only the selected time", FormationDragUi);
                Test("effective deck includes inherited and fixed support across stage changes", EffectiveSupportDeck);
                Test("malformed support data cannot bypass preflight", InvalidSupportData);
                Test("controller rejects incompatible tracks before browser or card access", StartPreflight);
                Test("loop uses the checked snapshot and rejects invalid data before creating a run", PreflightSnapshot);
                Test("generated tracks retain elapsed shovel timing and pass preflight", GeneratedCompatibility);
                Test("duo commands reject stale runs and out-of-order gates", DuoGates);
                Test("duo preflight enforces disjoint ordinary formations", DuoPlans);
                Test("duo fixed bursts retain target guards and reject timed or unsupported actions", DuoFixedBursts);
                Test("duo fixed support stays within each account's cells and rejects timed layouts", DuoFixedCarriers);
                Test("bare water attacks never request a plate and reject covered or land targets", BareWaterAttacks);
                Test("local room masks read a stable room ID and find only its list entry", RoomNumbers);
                Test("controller keeps account track markers separate and runtime local", ControllerIsolation);
                Test("portal selection pairs the visible server field and Enter control", PortalControls);
                Console.WriteLine("Automation regressions passed: " + _passed);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                var full = Path.GetFullPath(_scratch);
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("fvm-automation-tests-"))
                    Directory.Delete(full, true);
            }
        }

        private static TrackCard Card(string line) => TrackFile.Parse(line).Opening.Cards[0];

        private static void RendererRecovery()
        {
            var health = new RendererFrameHealth();
            Check(health.Available, "Initial frame access remains supported");
            health.Terminated("OutOfMemory");
            bool rejected = false;
            try { health.RequireAvailable(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && health.FailureReason == "OutOfMemory", "Failed rendering must block cached frame access");
            health.Painted(false);
            health.MainFrameCompleted(true);
            health.Painted(false);
            Check(!health.Available, "Late paint or load callbacks cannot revive a dead renderer");
            health.MainFrameStarted();
            health.Painted(false);
            Check(!health.Available, "Navigation alone does not authorize old frames");
            health.MainFrameCompleted(false);
            health.Painted(false);
            Check(!health.Available, "Failed navigation must stay blocked");
            health.MainFrameStarted();
            health.MainFrameCompleted(true);
            health.Painted(true);
            Check(!health.Available, "Popup paint cannot prove viewport recovery");
            health.Painted(false);
            health.RequireAvailable();
            Check(health.Available && health.FailureReason == null, "Fresh viewport paint after navigation enables recovery");
            health.MainFrameStarted();
            health.Terminated("Crashed");
            health.MainFrameCompleted(true);
            health.Painted(false);
            Check(!health.Available, "Another failure discards earlier recovery evidence");
        }

        private static void ControllerIsolation()
        {
            var root = AppPaths.DataRoot();
            Check(Path.GetFileName(root) == ".fvm-data" &&
                  File.Exists(Path.Combine(Path.GetDirectoryName(root), "AGENTS.md")),
                "Runtime state must resolve to the ignored project directory.");
            var track = Path.Combine(_scratch, "track.txt");
            var main = ControllerState.SuccessKey("main", "美味/曲奇", track);
            Check(main != ControllerState.SuccessKey("partner", "美味/曲奇", track) &&
                  main != ControllerState.SuccessKey("main", "美味/色拉", track),
                "A success marker must not leak between accounts or stages.");
            try
            {
                ControllerState.Validate(new AccountEntry { Profile = "../other" });
                throw new Exception("Unsafe profile accepted");
            }
            catch (ArgumentException) { }
        }

        private static void PortalControls()
        {
            var scan = new PortalScan
            {
                inputs = new List<PortalElement> { new PortalElement { x = 1036, y = 392, w = 32, h = 18 } },
                entries = new List<PortalElement> { new PortalElement { x = 1092, y = 390, w = 44, h = 21 } }
            };
            var pair = PortalSelection.FindPair(scan);
            Check(pair.input.CenterX == 1052 && pair.entry.CenterX == 1114, "Expected the adjacent server controls.");
            scan.entries[0].y = 100;
            try
            {
                PortalSelection.FindPair(scan);
                throw new Exception("Unrelated Enter control accepted");
            }
            catch (InvalidOperationException) { }
        }

        private static TrackCompatibilityReport Inspect(string text, string[] selected = null, bool shovel = false) =>
            new TrackPreflight(TrackFile.Parse(text), null).Inspect(selected, shovel);

        private static bool Has(TrackCompatibilityReport report, string code, string severity = "error") =>
            report.Diagnostics.Any(d => d.Code == code && d.Severity == severity);

        private static void InvalidTracks()
        {
            var malformed = Inspect("Card\t7[3]独\t035.55|49");
            Check(!malformed.CanStart && Has(malformed, "bad-step"), "Unsupported precision must block a truncated run");
            Check(malformed.Diagnostics[0].Line == 1 && malformed.Diagnostics[0].Card == "Card", "Diagnostic must identify source location");
            foreach (var cell in new[] { "00", "80", "19x" })
                Check(!Inspect("Card\t7[3]独\t010|" + cell).CanStart, "Invalid cell must not reach geometry: " + cell);
            Check(Has(Inspect("Card\t999999999999[3]\t000|11"), "bad-card-spec"), "Numeric overflow must become a diagnostic");
            Check(Has(Inspect("Card\t7[3]独\t999999999999|11"), "bad-step"), "Time overflow must become a diagnostic");
            Check(Has(Inspect("Card\t\nOther\t7[3]补极\t000|11"), "bad-card-spec"), "Empty specification must not disappear");
            Check(Has(Inspect("\t7[3]补极\t000|11"), "empty-card"), "Blank names are invalid");
            Check(Has(Inspect("Card\t7[3]补极\t000|11\nCard\t7[3]补极\t000|12"), "duplicate-card"), "Duplicate names must block ambiguous mappings");
        }

        private static void FractionalSteps()
        {
            var track = TrackFile.Parse("Card\t7[3]独\t035.5|49\t042|39");
            Check(new TrackPreflight(track, null).Inspect().CanStart, "A one-decimal timed card should pass preflight");
            Check(track.Opening.Cards[0].Steps[0].TimeDeciseconds == 355 &&
                track.Opening.Cards[0].Steps[0].TimeText == "35.5" &&
                track.Opening.Cards[0].Steps[0].EditorTimeText == "035.5", "Tenths must survive parsing and formatting");
            var schedule = new PlacementSchedule(track.Opening.Cards[0].Steps);
            var now = DateTime.UtcNow;
            Check(schedule.Next(35.49, now, false) == null && schedule.Next(35.5, now, false) == 0,
                "A fractional step must not fire at the previous whole second");
            Check(Has(Inspect("Card\t7[3]独\t000.5|49"), "invalid-time"), "Nonzero times below one second are invalid");
            Check(Has(Inspect("Card\t7[3]独\t960.1|49"), "invalid-time"), "The 960-second limit also applies to tenths");

            var runner = Runner("Sync\t7[3]\t010.5|11\nIndependent\t7[3]独\t010.5|12", Frame(Color.Black));
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Set(runner, "_start", start);
            Set(runner, "_synchronizedPlacementTime", TimeSpan.FromSeconds(4));
            var plans = (IList)Get(runner, "_plans");
            Check(!(bool)Call(runner, "IsDue", plans[0], start.AddMilliseconds(14499)) &&
                (bool)Call(runner, "IsDue", plans[0], start.AddMilliseconds(14500)),
                "A synchronized fractional deadline must include placement time");
            Check(!(bool)Call(runner, "IsDue", plans[1], start.AddMilliseconds(10499)) &&
                (bool)Call(runner, "IsDue", plans[1], start.AddMilliseconds(10500)),
                "An independent fractional deadline must use real battle time");

            var folder = Path.Combine(_scratch, "fractional-support", "承载轨道", "1Test");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1Cloud.txt"), "棉花糖\t7[1]独\t010.5|12");
            var support = CarrierMap.Find(Path.Combine(_scratch, "fractional-support"),
                new LevelEntry { Island = "Test", Name = "Cloud" }, null);
            Check(support != null && support.Resolve(Card("麦芽糖\t7[1]继")).Steps[0].TimeDeciseconds == 105,
                "Inherited support must retain fractional timing");
        }

        private static void SelectedCompatibility()
        {
            const string plain = "\uFEFF人物1位置=11\n退出时间=0\n魔塔层数=0\n移动板块=0\n放卡限速=0\n刷技能模式=0\n卡槽数=2\n" +
                "宝石\t120[0],0,0,0\n铲子\t0[0]独\t010|21\n" +
                "Basic\t7[3]补极锁,0,0,0,0,NULL,0,NULL,0,0,0,NULL,0,0\t000|11\n" +
                "Expensive\t7[3]补极,0,0,200\t000|12";
            var selected = Inspect(plain, new[] { "Basic" });
            Check(selected.CanStart && Has(selected, "card-skipped", "warning"), "An intentionally skipped conditional card must not block execution");
            Check(Has(selected, "shovel-skipped", "warning") && !Has(selected, "gem-action"), "Default gem fields are inert; disabled shovel is visible");
            var all = Inspect(plain);
            Check(!all.CanStart && Has(all, "flame-assets-missing"), "A conditional card requires readable digit masks");
            Check(all.Diagnostics.Any(d => d.Card == "Expensive" && d.Message.Contains("Flame")), "Report must identify the missing flame input");
            Check(new TrackPreflight(TrackFile.Parse(plain), null, TestFlameReader()).Inspect().CanStart,
                "A selected basic flame condition can run with installed masks");
            Check(Inspect(plain, new[] { "Basic" }, true).CanStart, "Independent timed shovel is supported");
            Check(!Inspect(plain, new string[0]).CanStart, "An empty selection is not a runnable deck");
            Check(Has(Inspect(plain, new[] { "Missing" }), "unknown-mapping"), "A stale mapping must fail before input");
            Check(Inspect("Basic\t7[3]独,0,0,200\t010|11").CanStart, "Conditions without 极 are inactive in this track mode");
            Check(Has(Inspect("Card\t7[3]补极,0,0,0,1\t000|11"), "conditions-unsupported"), "Wave conditions remain blocked");
            Check(Has(Inspect("Card\t7[3]补极,-1,0,0\t000|11"), "condition-format"), "Malformed basic conditions must not become zero");
        }

        private static bool[] DigitMask(int digit)
        {
            string segments = new[] { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgecd", "abc", "abcdefg", "abcdfg" }[digit];
            var mask = new bool[60];
            void Line(char segment, int x1, int y1, int x2, int y2)
            {
                if (!segments.Contains(segment)) return;
                for (int y = y1; y <= y2; y++)
                    for (int x = x1; x <= x2; x++) mask[y * 6 + x] = true;
            }
            Line('a', 1, 0, 4, 0); Line('b', 5, 1, 5, 4); Line('c', 5, 5, 5, 8);
            Line('d', 1, 9, 4, 9); Line('e', 0, 5, 0, 8); Line('f', 0, 1, 0, 4);
            Line('g', 1, 5, 4, 5);
            return mask;
        }

        private static FlameReader TestFlameReader()
        {
            var folder = Path.Combine(_scratch, "附加程序", "数字", "火苗");
            Directory.CreateDirectory(folder);
            for (int digit = 0; digit < 10; digit++)
            {
                using (var bitmap = new Bitmap(6, 10))
                {
                    var mask = DigitMask(digit);
                    for (int y = 0; y < 10; y++)
                        for (int x = 0; x < 6; x++)
                            bitmap.SetPixel(x, y, mask[y * 6 + x] ? Color.White : Color.Black);
                    bitmap.Save(Path.Combine(folder, digit + ".png"), ImageFormat.Png);
                }
            }
            return FlameReader.Load(_scratch);
        }

        private static StageFrame FlameFrame(int value, int slotCount = 1)
        {
            string digits = value.ToString();
            int[] offset = { 0, 0, 4, 7, 11, 14 };
            return Frame(Color.White, bitmap =>
            {
                for (int position = 0; position < digits.Length; position++)
                {
                    int x0 = (slotCount > 12 ? 151 : 172) + position * 7 - offset[digits.Length];
                    var mask = DigitMask(digits[position] - '0');
                    for (int y = 0; y < 10; y++)
                        for (int x = 0; x < 6; x++)
                            if (mask[y * 6 + x]) bitmap.SetPixel(x0 + x, 75 + y, Color.Black);
                }
            });
        }

        private static StageFrame StrokeFlameFrame(int value)
        {
            string digits = value.ToString();
            return Frame(Color.White, bitmap =>
            {
                int first = 172 - (digits.Length - 1) * 4;
                for (int position = 0; position < digits.Length; position++)
                {
                    int digit = digits[position] - '0';
                    int x0 = first + position * 9;
                    if (digit == 1)
                    {
                        for (int y = 0; y < 10; y++)
                            for (int x = 2; x <= 3; x++) bitmap.SetPixel(x0 + x, 77 + y, Color.Black);
                        continue;
                    }
                    var mask = DigitMask(digit);
                    for (int y = 0; y < 9; y++)
                        for (int x = 0; x < 6; x++)
                            if (mask[y * 6 + x]) bitmap.SetPixel(x0 + x, 77 + y, Color.Black);
                }
            });
        }

        private static void FlameDigits()
        {
            var reader = TestFlameReader();
            Check(reader != null, "Synthetic digit masks must load");
            foreach (int value in new[] { 0, 5, 42, 200, 15000 })
                Check(reader.Read(FlameFrame(value)) == value, "Read flame value " + value);
            Check(reader.Read(FlameFrame(200, 13), 13) == 200, "Counter must follow the shifted 13-slot bar");
            Check(reader.Read(FlameFrame(15001)) == null, "An out-of-range counter must not become a shorter partial value");
            foreach (int value in new[] { 0, 200, 3531, 9876, 15000 })
            {
                int? observed = reader.Read(StrokeFlameFrame(value));
                Check(observed == value, "Wider stroke font must read " + value + "; got " + (observed?.ToString() ?? "unknown"));
            }
            Check(reader.Read(StrokeFlameFrame(15001)) == null, "Wider font must reject out-of-range totals");
            Check(reader.Read(Frame(Color.White)) == null, "An unreadable counter is unknown, not zero");
            Check(reader.Read(FlameFrame(200)) == 200 && reader.Read(Frame(Color.White)) == null,
                "A prior valid reading must not survive an unreadable frame");
        }

        private static void ConditionSemantics()
        {
            var parsed = TrackConditions.Parse("2,0,200,0,NULL,0,NULL,0,0,0,NULL,2,10");
            Check(parsed.Errors.Count == 0 && parsed.MinimumFlames == 200 && parsed.TerminationCount == 2 && parsed.TerminationSeconds == 10,
                "Condition fields must retain their typed values");
            var start = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
            var gate = new ConditionGate(parsed);
            Check(!gate.Allows(start, start, null) && !gate.Allows(start, start, 0), "Unknown and zero cannot meet a positive threshold");
            Check(!gate.Allows(start, start, 200), "Delay starts when the threshold is observed");
            Check(!gate.Allows(start.AddSeconds(1), start, null), "A lost reading resets maintenance delay");
            Check(!gate.Allows(start.AddSeconds(2), start, 200) && gate.Allows(start.AddSeconds(4), start, 200),
                "Maintenance delay must restart after unknown input");
            gate.Placed();
            Check(!gate.Allows(start.AddSeconds(5), start, 200) && gate.Allows(start.AddSeconds(7), start, 200),
                "A confirmed placement restarts maintenance delay");
            gate.Placed();
            Check(gate.Stopped(start.AddSeconds(8), start) && !gate.Allows(start.AddSeconds(8), start, 200),
                "Termination count blocks additional placements");
            gate.RollBack();
            Check(gate.SuccessfulPlacements == 1 && !gate.Allows(start.AddSeconds(11), start, 200),
                "Late rejection restores the count but cannot extend the termination time");

            var trigger = new ConditionGate(TrackConditions.Parse("1,1,200"));
            Check(!trigger.Allows(start, start, 200) && trigger.Allows(start.AddSeconds(1), start, null),
                "Trigger mode latches after its delay despite a later unreadable frame");
            trigger.Placed();
            Check(trigger.Allows(start.AddSeconds(2), start, null), "A triggered card stays enabled for later attempts");
        }

        private static void CardCountConditions()
        {
            const string track = "狮子座\t35[3]补极\t000|12\n冰激凌\t42[5]x补极,0,0,0,,狮子座,1\t000|12";
            Check(Inspect(track).CanStart, "Editor lock, layer-5 placement and a selected ordinary-card count should be accepted");
            Check(Has(Inspect(track, new[] { "冰激凌" }), "condition-card-missing"),
                "The card named by a count condition must be selected");
            Check(Has(Inspect("冰激凌\t42[5]x补极,0,0,0,,冰激凌,1\t000|12"), "condition-card-layer"),
                "A transient special card must not be counted as board occupancy");
            Check(Has(Inspect("冰激凌\t42[5]x补极,0,0,0,,狮子座\t000|12"), "condition-format"),
                "A count condition without a positive count must fail preflight");

            var now = DateTime.UtcNow;
            var opening = Frame(Color.Black);
            var visible = Frame(Color.Black, b => PaintCard(b, Layout.CardRegion(new Cell(1, 2))));
            var runner = Runner("狮子座\t35[3]补极\t000|12", opening);
            var cell = new Cell(1, 2);
            var own = Own(runner, cell, opening, visible, now);
            Set(own, "After", null);
            Check((int)Call(runner, "ObservedCardCount", "狮子座", visible, now) == 0,
                "A click without positive board evidence must not satisfy the condition");
            Set(own, "After", visible.Crop(Layout.CellInterior(cell)));
            Check((int)Call(runner, "ObservedCardCount", "狮子座", visible, now.AddSeconds(6)) == 1,
                "A visibly present ordinary card must satisfy a one-card condition");
            var gate = new ConditionGate(TrackConditions.Parse("0,0,0,0,狮子座,1"));
            Check(!gate.Allows(now, now, null, 0) && gate.Allows(now, now, null, 1),
                "Only the observed count opens the condition gate");
        }

        private static void SpecialCardTargets()
        {
            var now = DateTime.UtcNow;
            var cell = new Cell(1, 2);
            var io = new FakeIo { RenderCard = true };
            var runner = Runner("冰激凌\t42[5]x补极\t000|12", io.Grab(), io);
            var plan = Plan(runner);
            var occupied = Frame(Color.Black, b => PaintCard(b, Layout.CardRegion(cell)));
            var targets = (List<Cell>)Call(runner, "SpecialTargets", plan, now);
            Check(targets.Count == 1 && targets[0].Equals(cell), "A layer-5 target may already hold another card");
            Check(((Task<bool>)Call(runner, "PlaceAsync", plan, occupied, CancellationToken.None, null)).GetAwaiter().GetResult(),
                "A ready layer-5 card should use its formation cell");
            Check(((IDictionary)Get(plan, "Own")).Count == 0 && runner.Placements == 1,
                "A transient special card must not claim ordinary-card ownership");
        }

        private static void UnsupportedTracks()
        {
            const string card = "Card\t7[3]补极\t000|11";
            Check(Has(Inspect(card + "\n--第1波--\nCard\t7[3]继"), "later-waves"), "Later waves cannot silently become opening-only execution");
            Check(Has(Inspect("人物1位置=89\n" + card), "header-unsupported"), "Invalid character cells must not be silently discarded");
            foreach (var header in new[] { "退出时间=15", "放卡限速=6", "移动板块=1", "Unknown=1" })
                Check(Has(Inspect(header + "\n" + card), "header-unsupported"), "Unsupported header: " + header);
            Check(Inspect("放卡限速=5\n" + card).CanStart, "Valid card rate limit must pass preflight");
            var solo = Inspect("人物1位置=62,52\n人物2位置=22,32\n优先队列=Other\n" +
                "Card\t7[3]补极\t000|11\nOther\t7[3]补极\t000|12", new[] { "Card" });
            Check(solo.CanStart && Has(solo, "second-player-ignored", "warning") &&
                Has(solo, "card-skipped", "warning") && Has(solo, "queue-card-skipped", "warning"),
                "A solo track must run with its second-player layout and an intentionally omitted queue card");
            Check(Has(Inspect("退出时间=15\n退出时间=0\n" + card), "duplicate-header"), "Repeated headers must not erase unsupported behavior");
            Check(Inspect("Card\t7[3]\t010|11").CanStart, "Synchronized ordinary timing must pass preflight");
            Check(Inspect("瓜皮\t15[2]补极\t035|49\t000|59").CanStart,
                "A synchronized extreme formation with a timed start must pass preflight");
            Check(Inspect("Card\t7[3]独\t010|11\t020|12").CanStart, "Independent ordinary timing remains available");
            Check(Has(Inspect("Card\t7[3]补极独\t005|11\t010|12"), "timed-prefix"), "Mixed timing and filling need dedicated semantics");
            Check(Has(Inspect("Card\t7[3]补极索\t000|11"), "flag-unsupported"), "Targeting flag must not disappear");
            Check(Has(Inspect("Card\t7[3]补极\t000|11\n宝石\t120[0]极"), "gem-action"), "Active gem rows require a dedicated executor");
            Check(Has(Inspect("优先队列=Absent\n" + card), "queue-card-missing"), "Unknown queue entries must not disappear");
            Check(Inspect("Card\t7[3]补独\t010|11\t020|00").CanStart,
                "Scheduled fill with a playable formation and 00 timer should pass preflight");
            Check(Has(Inspect("Card\t7[3]补独\t010|00"), "scheduled-fill-cells"),
                "A marker-only fill row has no safe target");
            Check(Has(Inspect("Card\t7[3]独\t010|00"), "invalid-cell"),
                "00 must not reach geometry on an ordinary timed row");
            Check(Has(Inspect("Card\t7[3]独\t000|11\t010|12"), "zero-time-schedule"), "Formation markers must not become timed placements");
            var deck = TrackFile.Parse(string.Join("\n", Enumerable.Range(1, 12).Select(i => "Card" + i + "\t7[3]补极\t000|11")));
            Check(Has(new TrackPreflight(deck, null).Inspect(null, false, DeckBuilder.DeckSlots), "deck-capacity"), "Automatic deck building must respect room capacity");
        }

        private static void ScheduledFill()
        {
            const string text = "Card\t7[3]补独\t001|12\t002|00";
            var io = new FakeIo();
            var runner = Runner(text, io.Grab(), io);
            var plan = Plan(runner);
            var schedule = (PlacementSchedule)Get(plan, "Schedule");
            Check(schedule.Pending == 2, "Both timed entries must become schedule events");
            schedule.Complete(0);
            Set(runner, "_start", DateTime.UtcNow.AddSeconds(-3));
            Check((bool)Call(runner, "IsDue", plan, DateTime.UtcNow), "The 00 entry must activate a timed fill sweep");
            Check(((Task<bool>)Call(runner, "PlaceAsync", plan, io.Grab(), CancellationToken.None, null))
                .GetAwaiter().GetResult(), "A timed marker should place into its playable formation");
            Check(schedule.Done && io.CellClicks.SequenceEqual(new[] { new Cell(1, 2) }),
                "The marker event must complete without a click at 00");

            var opening = Frame(Color.Black);
            var full = Runner(text, opening);
            var fullPlan = Plan(full);
            var fullSchedule = (PlacementSchedule)Get(fullPlan, "Schedule");
            fullSchedule.Complete(0);
            Set(full, "_start", DateTime.UtcNow.AddSeconds(-3));
            Own(full, new Cell(1, 2), opening, opening, DateTime.UtcNow);
            Check(!((Task<bool>)Call(full, "PlaceAsync", fullPlan, opening, CancellationToken.None, null))
                .GetAwaiter().GetResult() && fullSchedule.Done && (bool)Get(fullPlan, "HandledEvent"),
                "A full formation consumes its timed sweep without attempting invalid input");
        }

        private static void SynchronizedTiming()
        {
            var runner = Runner("Sync\t7[3]\t010|11\nIndependent\t7[3]独\t010|12\n" +
                "瓜皮\t15[2]补极\t035|49\t000|59", Frame(Color.Black));
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Set(runner, "_start", start);
            Set(runner, "_synchronizedPlacementTime", TimeSpan.FromSeconds(4));
            var plans = (IList)Get(runner, "_plans");
            Check(!(bool)Call(runner, "IsDue", plans[0], start.AddSeconds(11)),
                "A synchronized step waits for time spent placing another ordinary card");
            Check((bool)Call(runner, "IsDue", plans[1], start.AddSeconds(11)),
                "An independent step keeps its real-time deadline");
            Check((bool)Call(runner, "IsDue", plans[0], start.AddSeconds(14)),
                "The synchronized step becomes due after the offset");
            Check(!(bool)Call(runner, "IsDue", plans[2], start.AddSeconds(36)),
                "A synchronized extreme card uses the same delayed start clock");
            Check((bool)Call(runner, "IsDue", plans[2], start.AddSeconds(39)),
                "The extreme card starts when synchronized time reaches its first marker");
        }

        private static void PlantSpeed()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var runner = Runner("放卡限速=2\nCard\t7[3]补极\t000|11", Frame(Color.Black));
            Set(runner, "_lastCardActionAt", start);
            Check(!(bool)Call(runner, "CardRateReady", start.AddMilliseconds(499)) &&
                  (bool)Call(runner, "CardRateReady", start.AddMilliseconds(500)),
                "Two cards per second requires at least 500 ms between attempts");
            var unlimited = Runner("放卡限速=0\nCard\t7[3]补极\t000|11", Frame(Color.Black));
            Set(unlimited, "_lastCardActionAt", start);
            Check((bool)Call(unlimited, "CardRateReady", start), "Zero retains the normal runner cadence");
        }

        private static void TrackEditing()
        {
            var source = Path.Combine(_scratch, "source-track.txt");
            const string original = "注释=keep this header\nCard\t7[3]补极\t000|11\t035.5|49\n--第1波--\nOther\t8[3]独\t010|12";
            File.WriteAllText(source, original);
            var document = new TrackEditDocument(source, original);
            document.SetStep(2, 1, "040.5|39");
            document.SetStep(4, 1, "020|22");
            document.MoveStep(4, 1, -1);
            document.SetSpec(4, "9[3]补独");
            document.SetHeader("放卡限速", "2");
            Check(document.Track.Waves[1].Cards[0].Steps.Count == 2 &&
                  document.Text.Contains("注释=keep this header"),
                "Editing a later-wave row must preserve all other lines and headers");
            Check(document.Track.Waves[1].Cards[0].Steps[0].TimeDeciseconds == 200 &&
                  document.Track.Waves[1].Cards[0].CooldownSec == 9 &&
                  document.Track.Waves[1].Cards[0].Fill && document.Track.PlantSpeed == 2,
                "Step order, card specification, and header edits must survive reparsing");
            document.RemoveStep(3, 0);
            document.Undo();
            Check(document.Track.Opening.Cards[0].Steps.Count == 2, "Undo must restore the removed step");
            document.Redo();
            Check(document.Track.Opening.Cards[0].Steps.Count == 1, "Redo must remove it again");
            var folder = Path.Combine(_scratch, "edited-tracks");
            var saved = Path.Combine(folder, "copy.txt");
            document.SaveCopy(saved, folder);
            Check(File.ReadAllText(source) == original && File.ReadAllText(saved) == document.Text,
                "Saving an edit must write only a new copy");
            bool rejected = false;
            try { document.SaveCopy(source, folder); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "The editor cannot replace a source outside its private output folder");
        }

        private static void InventoryCalibration()
        {
            var grid = new InventoryGrid { X = 4, Y = 5, Columns = 2, Rows = 2, PitchX = 49, PitchY = 57 };
            var regions = grid.Regions(new Size(100, 115));
            Check(regions.Count == 4 && regions[3] == new Rectangle(53, 62, 42, 50),
                "Inventory crops must retain native room-icon dimensions and row-major order");
            foreach (var invalid in new[] {
                new InventoryGrid { Columns = 0 }, new InventoryGrid { PitchX = 41 },
                new InventoryGrid { PitchY = 49 }, new InventoryGrid { X = -1 },
                new InventoryGrid { X = 60, Columns = 2 }, new InventoryGrid { Rows = int.MaxValue },
                new InventoryGrid { X = int.MaxValue } })
            {
                bool refused = false;
                try { invalid.Regions(new Size(100, 115)); } catch (ArgumentException) { refused = true; }
                Check(refused, "Invalid inventory grids must fail instead of cropping clipped or overlapping images");
            }
        }

        private static void InventoryAlignment()
        {
            var exact = new InventoryGrid { X = 40, Y = 60, Columns = 2, Rows = 1 };
            var frame = Frame(Color.Black, bitmap =>
            {
                var random = new Random(271);
                foreach (var region in exact.Regions(bitmap.Size))
                    for (int y = region.Top; y < region.Bottom; y++)
                        for (int x = region.Left; x < region.Right; x++)
                            bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            });
            var known = exact.Regions(new Size(frame.Width, frame.Height)).Select(frame.Crop).ToList();
            var approximate = new InventoryGrid { X = 41, Y = 61, Columns = 2, Rows = 1 };
            var aligned = approximate.AlignToKnown(frame, known);
            Check(aligned.X == 40 && aligned.Y == 60 && approximate.X == 41 && approximate.Y == 61,
                "Two independent icon anchors must correct a one-pixel offset without mutating the supplied calibration");
            Check(ReferenceEquals(exact, exact.AlignToKnown(frame, known)), "Correct calibration must remain unchanged");
            Check(ReferenceEquals(approximate, approximate.AlignToKnown(frame, known.Take(1).ToList())),
                "A single known icon cannot justify moving the whole grid");
            Check(ReferenceEquals(approximate, approximate.AlignToKnown(Frame(Color.White), known)),
                "A page with no known anchors must retain manual calibration");
            var ambiguous = Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.FillRectangle(Brushes.Red, 38, 58, 50, 56);
                    graphics.FillRectangle(Brushes.Blue, 88, 58, 52, 56);
                }
            });
            var flatIcons = exact.Regions(new Size(frame.Width, frame.Height)).Select(ambiguous.Crop).ToList();
            Check(ReferenceEquals(approximate, approximate.AlignToKnown(ambiguous, flatIcons)),
                "Several equally supported offsets must not silently change calibration");
            var edge = new InventoryGrid { X = 0, Y = 0, Columns = 2 };
            Check(ReferenceEquals(edge, edge.AlignToKnown(frame, known)),
                "Searching near a frame boundary must skip invalid offsets without clipped crops");
        }

        private static void DeckScrollDelay()
        {
            var io = new DelayedScrollIo
            {
                Before = Frame(Color.Black),
                After = Frame(Color.Black, bitmap =>
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.White, 400, 190, 42, 50);
                })
            };
            var builder = new DeckBuilder(io, _ => { }, _ => true);
            bool moved = ((Task<bool>)Call(builder, "ScrollListAsync", new Point(929, 400), 1, CancellationToken.None))
                .GetAwaiter().GetResult();
            Check(moved && io.Polls >= 3 && io.Clicks == 1,
                "Delayed frames must not be mistaken for a list boundary or cause repeated unobserved clicks");
            var still = new MissionIo { Current = io.Before };
            builder = new DeckBuilder(still, _ => { }, _ => true);
            Check(!((Task<bool>)Call(builder, "ScrollListAsync", new Point(929, 400), 2, CancellationToken.None))
                .GetAwaiter().GetResult() && still.Clicks == 1,
                "A genuinely unchanged list must stop after a bounded observation window");
        }

        private static void DelayedRoomDeck()
        {
            var icon = Frame(Color.Cyan).Crop(new Rectangle(0, 0, 42, 50));
            var cards = new List<(string name, StageFrame icon)> { ("Card", icon) };
            var messages = new List<string>();
            var io = new RememberedDeckIo();
            new DeckBuilder(io, messages.Add, _ => true).EnsureAsync(cards, CancellationToken.None).GetAwaiter().GetResult();
            Check(DeckBuilder.DeckMatches(io.Grab(), new[] { icon }) && io.Selections == 2 && io.Removals == 2,
                "Late existing cards must be cleared before rebuilding the exact requested deck");
            Check(messages.Count(m => m.Contains("rebuilding once")) == 1, "A delayed room deck must use only one rebuild");
            io = new RememberedDeckIo { RepeatRace = true };
            bool refused = false;
            try { new DeckBuilder(io, _ => { }, _ => true).EnsureAsync(cards, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && io.Selections == 2 && !DeckBuilder.DeckMatches(io.Grab(), new[] { icon }),
                "Repeated unexpected deck changes must stop after one retry, preserving the exact-deck guard");
        }

        private sealed class RememberedDeckIo : IGameIo
        {
            private readonly Color?[] _slots = new Color?[DeckBuilder.DeckSlots];
            internal int Selections, Removals;
            internal bool RepeatRace;
            public StageFrame Grab() => Frame(Color.FromArgb(30, 55, 90), bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    for (int i = 0; i < 11; i++)
                        graphics.FillRectangle(i == 0 ? Brushes.Cyan : Brushes.Orange, 386 + 49 * i, 178, 42, 50);
                    for (int i = 0; i < _slots.Length; i++)
                        if (_slots[i].HasValue)
                            using (var brush = new SolidBrush(_slots[i].Value))
                                graphics.FillRectangle(brush, DeckBuilder.DeckSlot(i + 1));
                }
            });
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            {
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i].HasValue && DeckBuilder.DeckSlot(i + 1).Contains(point))
                    { _slots[i] = null; Removals++; return Task.CompletedTask; }
                if (new Rectangle(386, 178, 42, 50).Contains(point))
                {
                    Selections++;
                    if (Selections == 1 || RepeatRace) { _slots[0] = Color.Magenta; _slots[1] = Color.Cyan; }
                    else _slots[0] = Color.Cyan;
                }
                return Task.CompletedTask;
            }
            public void Move(Point point) { }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private sealed class DelayedScrollIo : IGameIo
        {
            internal StageFrame Before, After;
            internal int Clicks, Polls;
            public StageFrame Grab() => Clicks > 0 && ++Polls >= 3 ? After : Before;
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            { Clicks++; return Task.CompletedTask; }
            public void Move(Point point) { }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private static void InventoryLearning()
        {
            var library = new CardLibrary(Path.Combine(_scratch, "inventory-cards"));
            var session = new InventoryLearningSession();
            var now = DateTime.UtcNow;
            var original = Frame(Color.DarkRed);
            var captured = session.Capture(original, now);
            var firstId = captured.Id;
            captured = session.Capture(Frame(Color.DarkBlue), now.AddSeconds(1));
            var items = new[] { new InventorySelection { Name = "Card", X = 0, Y = 0 } };
            bool refused = false;
            try { session.Save(firstId, items, library, false, now.AddSeconds(2)); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && library.Names.Count == 0, "A newer capture must invalidate the previous review token");
            foreach (var invalid in new[] {
                new InventorySelection { Name = "../outside", X = 50, Y = 0 },
                new InventorySelection { Name = "Other", X = 940, Y = 0 },
                new InventorySelection { Name = "card", X = 50, Y = 0 },
                new InventorySelection { Name = "CON", X = 50, Y = 0 },
                new InventorySelection { Name = "Bad,variant", X = 50, Y = 0 },
                new InventorySelection { Name = "Outside", X = int.MaxValue, Y = 0 },
                new InventorySelection { Name = "Other", X = 0, Y = 0 } })
            {
                refused = false;
                try { session.Save(captured.Id, new[] { items[0], invalid }, library, false, now.AddSeconds(2)); }
                catch (ArgumentException) { refused = true; }
                Check(refused && library.Names.Count == 0, "An invalid later selection must not save earlier icons");
            }
            refused = false;
            try { session.Save(captured.Id, items, library, false, now.AddMinutes(11)); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "Old capture tokens must expire even without a newer capture");
            session.Save(captured.Id, items, library, false, now.AddSeconds(2));
            Check(library.Get("Card").MeanAbsDiff(captured.Frame.Crop(new Rectangle(0, 0, 42, 50))) == 0,
                "Saved inventory icons must come from the reviewed snapshot without scaling");
            captured = session.Capture(original, now.AddSeconds(3));
            refused = false;
            try { session.Save(captured.Id, new[] { new InventorySelection { Name = "New", X = 50, Y = 0 }, items[0] },
                library, false, now.AddSeconds(4)); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && library.Names.Count == 1 && library.Get("Card").MeanAbsDiff(original.Crop(new Rectangle(0, 0, 42, 50))) > 0,
                "Name collisions must not overwrite or partially add a batch without explicit replacement");
            session.Save(captured.Id, items, library, true, now.AddSeconds(4));
            Check(library.Get("Card").MeanAbsDiff(original.Crop(new Rectangle(0, 0, 42, 50))) == 0,
                "Explicit replacement must update only the chosen account's card icon");
        }

        private static void FormationEditing()
        {
            var document = TrackEditDocument.CreateNew("美味/曲奇");
            var initial = document.Text;
            int line = document.AddCard("Producer", "7[3]补极");
            document.PaintCell(line, new Cell(2, 3), "000", false);
            document.PaintCell(line, new Cell(2, 3), "0", false);
            document.PaintCell(line, new Cell(2, 4), "000", false);
            Check(document.Track.Opening.Cards[0].Steps.Count == 2 &&
                new TrackPreflight(document.Track, null).Inspect().CanStart,
                "Painting the same formation cell twice must be idempotent and create an executable track");
            document.PaintCell(line, new Cell(2, 3), "000", true);
            document.Undo();
            Check(document.Track.Opening.Cards[0].Steps.Count == 2, "Undo must restore an erased board cell");
            document.SetHeader("优先队列", "Producer,Producer");
            line = document.Track.Opening.Cards[0].SourceLine;
            document.RemoveCard(line);
            Check(document.Track.Header["卡槽数"] == "0" && document.Track.PriorityQueue.Count == 0,
                "Removing a card must atomically update its deck count and priority references");
            document.Undo();
            Check(document.Track.Header["卡槽数"] == "1" && document.Track.PriorityQueue.Count == 2,
                "One undo must restore the entire removed card and its metadata");
            while (document.CanUndo) document.Undo();
            Check(document.Text == initial, "A new formation must undo back to its empty seed");

            const string source = "注释=preserve\n卡槽数=1\nTimed\t7[3]补\t010|00\t000|11\t035.5|11\tbad-field\n--第1波--\nLater\t7[3]独\t020|22";
            document = new TrackEditDocument(null, source);
            document.PaintCell(3, new Cell(1, 1), "035.5", true);
            Check(document.Text.Contains("010|00\t000|11\tbad-field") && document.Text.EndsWith("Later\t7[3]独\t020|22"),
                "Erasing one timed placement must preserve time-zero cells, 00 events, damaged fields and later waves");
            int added = document.AddCard("New", "7[3]补极");
            Check(document.Track.Opening.Cards.Last().SourceLine == added && document.Track.Header["卡槽数"] == "2",
                "Added cards must enter the opening before a later-wave section");
            bool refused = false;
            try { document.AddCard("new", "7[3]补极"); } catch (ArgumentException) { refused = true; }
            Check(refused, "A duplicate card row must not be created by the visual editor");
        }

        private static void FormationWorkflowUi()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var library = new CardLibrary(Path.Combine(_scratch, "ui-inventory-cards"));
                    var frame = Frame(Color.DarkCyan);
                    library.Save("日光炉", frame.Crop(new Rectangle(4, 5, 42, 50)));
                    var session = new InventoryLearningSession();
                    using (var scanner = new InventoryScanDialog(() => Task.FromResult(session.Capture(frame, DateTime.UtcNow)),
                        (id, items, replace) => { session.Save(id, items, library, replace, DateTime.UtcNow); return Task.CompletedTask; },
                        library, Path.Combine(_scratch, "ui-inventory-grid.json")))
                    {
                        Check(scanner.Handle != IntPtr.Zero, "Inventory review must create a WinForms handle");
                        ((Task)Call(scanner, "CapturePageAsync")).GetAwaiter().GetResult();
                        ((System.Windows.Forms.NumericUpDown)Get(scanner, "_x")).Value = 4;
                        ((System.Windows.Forms.NumericUpDown)Get(scanner, "_y")).Value = 5;
                        Call(scanner, "ScanGrid");
                        var rows = ((System.Windows.Forms.DataGridView)Get(scanner, "_candidates")).Rows;
                        Check(rows.Count == 1 && Convert.ToString(rows[0].Cells[2].Value) == "日光炉",
                            "Known inventory icons must be suggested using the local library");
                        Check(!Convert.ToBoolean(rows[0].Cells[0].Value), "Known icon suggestions must still require the user to choose Add");
                        rows[0].Cells[2].Value = "日光炉-test";
                        ((Task)Call(scanner, "SaveCardsAsync")).GetAwaiter().GetResult();
                        Check(library.Names.Contains("日光炉-test"), "Reviewed inventory names must save through the shared learning session");
                    }
                    using (var editor = new TrackEditorDialog(null, null, Path.Combine(_scratch, "ui-formations"), library, "美味/曲奇"))
                    {
                        Check(editor.Handle != IntPtr.Zero, "New formations must create a WinForms handle");
                        ((System.Windows.Forms.ComboBox)Get(editor, "_newCard")).Text = "日光炉";
                        Call(editor, "AddCard");
                        Call(editor, "PaintBoard", new Cell(4, 5), System.Windows.Forms.MouseButtons.Left);
                        var document = (TrackEditDocument)Get(editor, "_document");
                        Check(document.Track.Opening.Cards[0].FormationCells.Single().ToString() == "45",
                            "The selected learned card must paint the requested board cell");
                        ((System.Windows.Forms.ComboBox)Get(editor, "_tool")).SelectedIndex = 2;
                        Call(editor, "PaintBoard", new Cell(3, 2), System.Windows.Forms.MouseButtons.Left);
                        Check(document.Track.Player1Cells.Single().ToString() == "32", "The character tool must update the actual track header");
                        var board = (TrackBoardControl)Get(editor, "_board");
                        board.Size = new Size(587, 431);
                        for (int row = 1; row <= 7; row++)
                            for (int col = 1; col <= 9; col++)
                            {
                                var tile = (Rectangle)Call(board, "Tile", new Cell(row, col));
                                Check(board.CellAt(tile.Location)?.ToString() == row.ToString() + col,
                                    "Every tile's top-left pixel must resolve to that cell at fractional board sizes");
                            }
                        Check(!board.CellAt(Point.Empty).HasValue && !board.CellAt(new Point(board.Width, board.Height)).HasValue,
                            "Board labels and outer margins must never produce playable cells");
                        Check(new TrackPreflight(document.Track, null).Inspect().CanStart, "A basic visual formation must pass the existing run preflight");
                    }
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Check(thread.Join(15000), "Attended editor workflow timed out");
            if (failure != null) throw failure;
        }

        private static void FormationDragUi()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var editor = new TrackEditorDialog(null, null))
                    {
                        Check(editor.Handle != IntPtr.Zero, "The drag editor must have a native handle");
                        ((System.Windows.Forms.ComboBox)Get(editor, "_newCard")).Text = "Card";
                        Call(editor, "AddCard");
                        var board = (TrackBoardControl)Get(editor, "_board");
                        board.Size = new Size(587, 431);
                        Check(board.Handle != IntPtr.Zero, "The painting board must have a native handle");
                        Action<Cell, Cell, System.Windows.Forms.MouseButtons> drag = (from, to, button) =>
                        {
                            var start = (Rectangle)Call(board, "Tile", from);
                            var end = (Rectangle)Call(board, "Tile", to);
                            Call(board, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(button, 1,
                                start.Left + start.Width / 2, start.Top + start.Height / 2, 0));
                            var move = new System.Windows.Forms.MouseEventArgs(button, 0,
                                end.Left + end.Width / 2, end.Top + end.Height / 2, 0);
                            Call(board, "OnMouseMove", move);
                            Call(board, "OnMouseMove", move);
                            Call(board, "OnMouseUp", move);
                        };
                        drag(new Cell(1, 2), new Cell(7, 2), System.Windows.Forms.MouseButtons.Left);
                        var document = (TrackEditDocument)Get(editor, "_document");
                        Check(document.Track.Opening.Cards[0].FormationCells.Select(c => c.ToString())
                            .SequenceEqual(Enumerable.Range(1, 7).Select(r => r + "2")),
                            "A single distant mouse move must paint all seven crossed cells without duplicates");
                        ((System.Windows.Forms.TextBox)Get(editor, "_time")).Text = "035.5";
                        Call(editor, "PaintBoard", new Cell(4, 2), System.Windows.Forms.MouseButtons.Left);
                        ((System.Windows.Forms.TextBox)Get(editor, "_time")).Text = "000";
                        drag(new Cell(7, 2), new Cell(1, 2), System.Windows.Forms.MouseButtons.Right);
                        var timed = document.Track.Opening.Cards[0].Steps.Single();
                        Check(timed.Cell.ToString() == "42" && timed.TimeDeciseconds == 355,
                            "Reverse erasing must remove the complete opening column and preserve a later placement");
                        drag(new Cell(1, 1), new Cell(7, 9), System.Windows.Forms.MouseButtons.Left);
                        var diagonal = document.Track.Opening.Cards[0].Steps.Where(s => s.TimeDeciseconds == 0)
                            .Select(s => s.Cell).ToList();
                        Check(diagonal.Count == 9 && diagonal.First().ToString() == "11" && diagonal.Last().ToString() == "79",
                            "A fast diagonal drag must include both endpoints and a continuous path");
                        for (int i = 1; i < diagonal.Count; i++)
                            Check(Math.Abs(diagonal[i].Row - diagonal[i - 1].Row) <= 1 &&
                                Math.Abs(diagonal[i].Col - diagonal[i - 1].Col) <= 1,
                                "Diagonal painting must not leave gaps between cells");
                    }
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Check(thread.Join(15000), "Formation drag workflow timed out");
            if (failure != null) throw failure;
        }

        private static void TrackEditorUi()
        {
            var source = Path.Combine(_scratch, "ui-track.txt");
            File.WriteAllText(source, "Card\t7[3]补极\t000|11\t035.5|49");
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var editor = new TrackEditorDialog(source, null))
                    using (var preview = new TrackPreviewDialog(
                        new TrackPreflight(TrackFile.Load(source), null), new[] { "Card" }))
                    {
                        Check(editor.Handle != IntPtr.Zero && preview.Handle != IntPtr.Zero,
                            "Both dialogs must create a native WinForms handle");
                        editor.PerformLayout();
                        preview.PerformLayout();
                    }
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Check(thread.Join(10000), "UI dialog construction timed out");
            if (failure != null) throw failure;
        }

        private static void EffectiveSupportDeck()
        {
            var folder = Path.Combine(_scratch, "effective", "承载轨道", "1Test");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1Cloud.txt"), "棉花糖\t7[1]独\t010|12\t020|22");
            File.WriteAllText(Path.Combine(folder, "2Water.txt"), "木盘子\t7[1]\t000|13\t000|23");
            var root = Path.Combine(_scratch, "effective");
            var track = TrackFile.Parse("麦芽糖\t7[1]继绝\nCard\t7[3]补极\t000|12");
            var cloud = CarrierMap.Find(root, new LevelEntry { Island = "Test", Name = "Cloud" }, null);
            var prepared = new TrackPreflight(track, cloud);
            Check(prepared.DeckCards.Count == 2 && prepared.DeckCards[0].Steps.Count == 2, "Empty inherited row must be available to the panel before mapping");
            Check(prepared.Inspect().CanStart && Has(prepared.Inspect(), "support-inherited", "info"), "Resolved support must validate and explain its origin");
            Check(track.Opening.Cards[0].Steps.Count == 0, "Preview must not mutate the source track");
            var water = CarrierMap.Find(root, new LevelEntry { Island = "Test", Name = "Water" }, null);
            Check(!new TrackPreflight(track, water).Inspect().CanStart, "Changing stages must recompute support even for the same track");
            var fallback = new TrackPreflight(TrackFile.Parse("木盘子\t7[1]"), water);
            Check(fallback.DeckCards.Count == 1 && fallback.Inspect().CanStart, "Fixed-cell legacy fallback must remain mappable");
            var explicitTrack = TrackFile.Parse("麦芽糖\t7[1]继\t000|11\t000|21");
            var explicitPlan = new TrackPreflight(explicitTrack, cloud);
            Check(explicitPlan.Inspect().CanStart && Has(explicitPlan.Inspect(), "support-inherited", "info"),
                "An explicit support formation must import default timing and validate");
            Check(explicitPlan.DeckCards[0].Steps[0].Cell.Equals(new Cell(1, 1)) &&
                  explicitPlan.DeckCards[0].Steps[1].Cell.Equals(new Cell(2, 1)) &&
                  explicitPlan.DeckCards[0].Steps[0].TimeDeciseconds == 100 &&
                  explicitPlan.DeckCards[0].Steps[1].TimeDeciseconds == 200,
                "Inherited times must follow explicit cells by index");
            Check(Has(new TrackPreflight(explicitTrack, null).Inspect(), "missing-inheritance"),
                "An explicit 继 formation needs a matching default support layout");
            Check(Has(new TrackPreflight(TrackFile.Parse("麦芽糖\t7[1]继\t000|11"), cloud).Inspect(),
                "invalid-inherited-cell"), "Default timing cannot create a clickable 00 on a non-fill row");
        }

        private static void InvalidSupportData()
        {
            var folder = Path.Combine(_scratch, "bad-support", "承载轨道", "1Test");
            Directory.CreateDirectory(folder);
            foreach (var steps in new[] { "000.5|12", "010|00", "9999|12" })
            {
                File.WriteAllText(Path.Combine(folder, "1Cloud.txt"), "棉花糖\t7[1]独\t" + steps);
                bool failed = false;
                try { CarrierMap.Find(Path.Combine(_scratch, "bad-support"), new LevelEntry { Island = "Test", Name = "Cloud" }, null); }
                catch (FormatException) { failed = true; }
                Check(failed, "Invalid inherited input must be rejected before it loses its diagnostics");
            }
        }

        private static void StartPreflight()
        {
            var root = Path.Combine(_scratch, "controller");
            Directory.CreateDirectory(Path.Combine(root, "预制轨道"));
            File.WriteAllText(Path.Combine(root, "预制轨道", "Test.txt"), "Card\t7[3]补极,0,0,200,1\t000|11");
            var controller = new GrindController(null, null, AssistantOptions.Parse(new[] { "--track-dir", root }));
            bool rejected = false;
            try
            {
                controller.StartAsync(new GrindRequest { Track = "Test.txt", Cards = new List<(string, string)> { ("Card", "Mine") } }).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("conditions-unsupported"); }
            Check(rejected && !controller.Running && controller.Current == null, "Incompatible requests must stop before accessing null browser/card dependencies");
        }

        private static void PreflightSnapshot()
        {
            var path = Path.Combine(_scratch, "snapshot.txt");
            File.WriteAllText(path, "Card\t7[3]补极\t000|11");
            var snapshot = TrackFile.Load(path);
            Check(new TrackPreflight(snapshot, null).Inspect().CanStart, "Initial snapshot must be valid");
            File.WriteAllText(path, "Card\t7[3]补极\t000|00");
            var settings = new GrindSettings { TrackPath = path, Track = snapshot, SlotCount = 1,
                SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 } };
            var loop = new GrindLoop(null, null, settings, Path.Combine(_scratch, "snapshot-run"));
            Check(ReferenceEquals(Get(loop, "_track"), snapshot), "Execution must use the checked snapshot rather than reread changed input");
            settings.Track = TrackFile.Load(path);
            var rejectedDir = Path.Combine(_scratch, "must-not-exist");
            bool rejected = false;
            try { new GrindLoop(null, null, settings, rejectedDir); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !Directory.Exists(rejectedDir), "Invalid geometry must fail before a run directory is created");
        }
        private static void GeneratedCompatibility()
        {
            var battle = new RecordedBattle { Character = new Cell(2, 3) };
            var card = new RecordedCard { Name = "TestCard", Slot = 1 };
            card.Cells.Add(new Cell(1, 2));
            battle.Cards.Add(card);
            battle.Shovel.Add(new TrackStep(12, new Cell(1, 2)));
            battle.Shovel.Add(TrackStep.AtDeciseconds(125, new Cell(1, 2)));
            var exported = battle.ToTrackText("Synthetic recording");
            Check(exported.Contains("\t012|12\t012.5|12"), "Recorded shovel times must keep whole and fractional values");
            var track = TrackFile.Parse(exported);
            Check(track.Opening.Cards.Single(c => c.IsShovel).Independent, "Recorded clicks use elapsed battle time");
            Check(new TrackPreflight(track, null).Inspect(null, true).CanStart, "Generated formation and shovel must be accepted together");
        }

        private static void DuoGates()
        {
            var session = new DuoSession(DuoRole.Host);
            Check(!session.RoomReleased(1) && !session.BattleReleased(1) && !session.BossReleased(1), "A new duo run must wait for all gates");
            bool rejected = false;
            try { session.ReleaseRoom("stale", 1, 1, "duo-room-ready"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !session.RoomReleased(1), "A stale run ID must not release a room");
            rejected = false;
            try { session.ReleaseRoom(session.RunId, 2, 1, "duo-room-ready"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !session.RoomReleased(2), "A future round must not release a room");
            session.ReleaseRoom(session.RunId, 1, 1, "duo-room-ready");
            Check(session.RoomReleased(1) && !session.RoomReleased(2), "Only the current room round may advance");
            rejected = false;
            try { session.ReleaseBattle(session.RunId, 1, 1, "duo-awaiting-battle"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !session.BattleReleased(1), "Battle cannot start before both opening frames are ready");
            session.ReleaseBattle(session.RunId, 1, 1, "duo-battle-ready");
            Check(session.BattleReleased(1), "The matching battle gate should release");
            rejected = false;
            try { session.ReleaseBoss(session.RunId, 1, 1, "duo-battle-ready"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !session.BossReleased(1), "Boss cannot start before the victory prompt");
            session.ReleaseBoss(session.RunId, 1, 1, "duo-boss-ready");
            Check(session.BossReleased(1) && !session.BossReleased(2), "Only the current boss round may advance");
            var guest = new DuoSession(DuoRole.Guest, 2345);
            rejected = false;
            try { guest.SetRoomNumber(6789); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && guest.RoomNumber == 0, "The guest must reject a different room ID");
            guest.SetRoomNumber(2345);
            Check(guest.RoomNumber == 2345, "The joined room ID should be retained for the run");
        }

        private static void RoomNumbers()
        {
            var root = Path.Combine(_scratch, "room-digits");
            void WriteMasks(string name, int width)
            {
                var folder = Path.Combine(root, "附加程序", "数字", name);
                Directory.CreateDirectory(folder);
                for (int digit = 0; digit < 10; digit++)
                    using (var bmp = new Bitmap(width, 8))
                    {
                        for (int y = 0; y < 8; y++)
                            for (int x = 0; x < width; x++)
                                bmp.SetPixel(x, y, MaskPixel(digit, width, x, y) ? Color.White : Color.Black);
                        bmp.Save(Path.Combine(folder, digit + ".png"));
                    }
            }
            WriteMasks("房间内房间号", 6);
            WriteMasks("列表房间号", 5);
            var reader = RoomNumberReader.Load(root);
            Check(reader != null, "Complete local digit sets should load");
            using (var bitmap = new Bitmap(950, 600))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(20, 20, 20));
                void PaintNumber(string value, int width, int pitch, int left, int top, Color color)
                {
                    for (int i = 0; i < value.Length; i++)
                        for (int y = 0; y < 8; y++)
                            for (int x = 0; x < width; x++)
                                if (MaskPixel(value[i] - '0', width, x, y))
                                    bitmap.SetPixel(left + i * pitch + x, top + y, color);
                }
                PaintNumber("2345", 6, 7, 164 - 12, 28, Color.White);
                PaintNumber("2345", 5, 6, 150 + 20 - 12, 200, Color.FromArgb(198, 255, 248));
                var frame = StageFrame.FromBitmap(bitmap, new Rectangle(0, 0, 950, 600));
                Check(reader.ReadRoom(frame) == 2345, "The room header should yield the four-digit ID");
                Check(reader.FindListRoom(frame, 2345, 20) == new Point(158, 204),
                    "The shifted room list should yield the intended click point");
                Check(reader.FindListRoom(frame, 9999, 20) == null, "An absent room must never be selected");
                using (var blank = new Bitmap(950, 600))
                {
                    using (var graphics = Graphics.FromImage(blank)) graphics.Clear(Color.Black);
                    Check(reader.ReadRoom(StageFrame.FromBitmap(blank, new Rectangle(0, 0, 950, 600))) == null,
                        "An unreadable room number must stay unknown");
                }
            }
        }

        private static bool MaskPixel(int digit, int width, int x, int y)
        {
            if (x == width - 1) return true;
            for (int bit = 0; bit < 4; bit++)
                if ((digit & (1 << bit)) != 0 && x >= (bit % 2) * 2 && x < (bit % 2) * 2 + 2 &&
                    y >= (bit / 2) * 4 && y < (bit / 2) * 4 + 2)
                    return true;
            return false;
        }

        private static void DuoFixedBursts()
        {
            DuoTrackPlan Plan(string text) => DuoTrackPlan.Build(new TrackPreflight(TrackFile.Parse(text), null),
                new[] { "Card", "Burst" }.Where(name => text.Contains(name + "\t")), false);
            void Reject(Action action, string message)
            {
                bool rejected = false;
                try { action(); }
                catch (Exception error) when (error is NotSupportedException || error is InvalidOperationException) { rejected = true; }
                Check(rejected, message);
            }
            var guest = Plan("人物1位置=71\nCard\t7[3]补极\t000|35\nBurst\t30[5]补极\t000|35\t000|36");
            Check(guest.Cells.Count == 2 && guest.Cells.Contains(new Cell(3, 5)) && guest.Cells.Contains(new Cell(3, 6)),
                "Every burst target must be guarded, including a target shared with the account's own ordinary card");
            var host = Plan("人物1位置=11\nCard\t7[3]补极\t000|34");
            DuoTrackPlan.RequireDisjoint(host, guest);
            bool rejected = false;
            try { DuoTrackPlan.RequireDisjoint(host, Plan("人物1位置=71\nBurst\t30[5]补极\t000|34")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A burst must not bypass the other account's formation guard");
            rejected = false;
            try { DuoTrackPlan.RequireDisjoint(host, Plan("人物1位置=71\nBurst\t30[5]补极\t000|11")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A burst must not target the other account's character");
            Reject(() => Plan("人物1位置=71\nBurst\t30[5]补极\t010|35"), "Timed bursts remain unsupported in duo mode");
            Reject(() => Plan("人物1位置=71\nBurst\t30[5]\t000|35"), "A nonrepeating burst must not enter the fixed-burst path");
            Reject(() => Plan("人物1位置=71\nBurst\t30[2]补极\t000|35"), "Shield actions remain unsupported in duo mode");
            Reject(() => Plan("人物1位置=71\nBurst\t30[6]补极\t000|35"), "Unknown layer-six actions remain unsupported");
        }

        private static void DuoPlans()
        {
            DuoTrackPlan Plan(string player, string cardCell, int layer = 3) =>
                DuoTrackPlan.Build(new TrackPreflight(TrackFile.Parse(
                    "人物1位置=" + player + "\nCard\t7[" + layer + "]补极\t000|" + cardCell), null),
                    new[] { "Card" }, false);
            var host = Plan("11", "12");
            var guest = Plan("21", "22");
            DuoTrackPlan.RequireDisjoint(host, guest);
            var timed = DuoTrackPlan.Build(new TrackPreflight(
                TrackFile.Parse("人物1位置=11\nCard\t7[3]补独\t010|12\t020|00"), null),
                new[] { "Card" }, false);
            Check(timed.Cells.Count == 1 && timed.Cells[0].Equals(new Cell(1, 2)),
                "A timed 00 marker must not become a shared duo board cell");
            bool rejected = false;
            try { DuoTrackPlan.RequireDisjoint(host, Plan("21", "12")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Shared card cells must stop a duo plan");
            rejected = false;
            try { DuoTrackPlan.RequireDisjoint(host, Plan("11", "22")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Characters must not share a cell");
            rejected = false;
            try { DuoTrackPlan.RequireDisjoint(host, Plan("12", "22")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A card must not target the other account's character");
            rejected = false;
            try { Plan("21", "22", 1); }
            catch (NotSupportedException) { rejected = true; }
            Check(rejected, "A support card without a matching stage layout must stop a duo plan");
            rejected = false;
            try { DuoTrackPlan.Build(new TrackPreflight(TrackFile.Parse("Card\t7[3]补极\t000|12"), null), new[] { "Card" }, false); }
            catch (NotSupportedException) { rejected = true; }
            Check(rejected, "An account must have an explicit own-character cell");
        }

        private static void BareWaterAttacks()
        {
            var folder = Path.Combine(_scratch, "承载轨道", "95BareWater");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1Water.txt"), "木盘子\t7[1]补极\t000|12\t000|22");
            var water = CarrierMap.Find(_scratch, new LevelEntry { Island = "BareWater", Name = "Water" }, null);
            var text = "人物1位置=11\n钢丝球\t7[3]补极\t000|12\n木盘子\t7[1]";
            var track = TrackFile.Parse(text);
            var preflight = new TrackPreflight(track, water);
            Check(preflight.Inspect(new[] { "钢丝球", "木盘子" }).CanStart, "An unused plate must not cover the water attack");
            var duo = DuoTrackPlan.Build(preflight, new[] { "钢丝球" }, false);
            Check(duo.Cells.Count == 1, "Bare water attacks do not require a support mapping");
            var runner = new BattleRunner(null, null, new BattleLayout(2), track,
                new Dictionary<string, int> { ["钢丝球"] = 1, ["木盘子"] = 2 }, false, false, water, _ => { });
            Check(((HashSet<Cell>)Get(runner, "_carrierCells")).Count == 0 &&
                ((HashSet<Cell>)Get(runner, "_mapCarrierCells")).Count == 0,
                "The runner must not remember or repair a plate below the water attack");
            Check(!new TrackPreflight(TrackFile.Parse(text.Replace("000|12", "000|13")), water)
                .Inspect(new[] { "钢丝球", "木盘子" }).CanStart, "Known land targets must be refused");
            Check(!new TrackPreflight(TrackFile.Parse(text.Replace("木盘子\t7[1]", "木盘子\t7[1]补极\t000|12")), water)
                .Inspect(new[] { "钢丝球", "木盘子" }).CanStart, "An explicit plate must not cover the water attack");
            Check(!new TrackPreflight(TrackFile.Parse(text + "\nCard\t7[3]补极\t000|12"), water)
                .Inspect(new[] { "钢丝球", "Card" }).CanStart, "An ordinary formation must not cover the water attack");
            var ordinary = TrackFile.Parse("Card\t7[3]补极\t000|12\n木盘子\t7[1]");
            var ordinaryRunner = new BattleRunner(null, null, new BattleLayout(2), ordinary,
                new Dictionary<string, int> { ["Card"] = 1, ["木盘子"] = 2 }, false, false, water, _ => { });
            Check(((HashSet<Cell>)Get(ordinaryRunner, "_carrierCells")).Contains(new Cell(1, 2)),
                "Ordinary cards must retain their water support dependency");
        }

        private static void DuoFixedCarriers()
        {
            var folder = Path.Combine(_scratch, "承载轨道", "94DuoFixed");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1Water.txt"), "木盘子\t7[1]补极\t000|12\t000|22");
            File.WriteAllText(Path.Combine(folder, "2Timed.txt"), "木盘子\t7[1]独\t010|12");
            File.WriteAllText(Path.Combine(folder, "3Mixed.txt"), "木盘子\t7[1]补极\t000|12\n棉花糖\t7[1]补极\t000|22");
            var water = CarrierMap.Find(_scratch, new LevelEntry { Island = "DuoFixed", Name = "Water" }, null);
            var timed = CarrierMap.Find(_scratch, new LevelEntry { Island = "DuoFixed", Name = "Timed" }, null);
            var mixed = CarrierMap.Find(_scratch, new LevelEntry { Island = "DuoFixed", Name = "Mixed" }, null);
            DuoTrackPlan Plan(string player, string target, string support, CarrierMap map)
            {
                var text = "人物1位置=" + player + "\nCard\t7[3]补极\t000|" + target;
                if (support != null) text += "\n木盘子\t" + support;
                return DuoTrackPlan.Build(new TrackPreflight(TrackFile.Parse(text), map),
                    support == null ? new[] { "Card" } : new[] { "Card", "木盘子" }, false);
            }
            void Reject(Action action, string message)
            {
                bool refused = false;
                try { action(); } catch (NotSupportedException) { refused = true; }
                Check(refused, message);
            }
            var inherited = Plan("11", "12", "7[1]继", water);
            Check(inherited.Cells.Select(c => c.ToString()).OrderBy(c => c).SequenceEqual(new[] { "12", "22" }),
                "Inherited support cells must be included even where the account has no ordinary card");
            var landGuest = Plan("71", "23", null, water);
            DuoTrackPlan.RequireDisjoint(inherited, landGuest);
            var localHost = Plan("11", "12", "7[1]", water);
            var localGuest = Plan("21", "22", "7[1]", water);
            Check(localHost.Cells.Count == 1 && localHost.Cells[0].ToString() == "12",
                "Empty support rows repair only this account's ordinary targets");
            DuoTrackPlan.RequireDisjoint(localHost, localGuest);
            bool shared = false;
            try { DuoTrackPlan.RequireDisjoint(inherited, localGuest); }
            catch (InvalidOperationException) { shared = true; }
            Check(shared, "A support formation must not overlap the other account's card cells");
            shared = false;
            try { DuoTrackPlan.RequireDisjoint(inherited, Plan("22", "23", null, null)); }
            catch (InvalidOperationException) { shared = true; }
            Check(shared, "A support formation must not target the other character");
            Reject(() => Plan("11", "12", null, water), "Water targets must have their own mapped support card");
            var waterCharacter = Plan("12", "23", null, water);
            Check(waterCharacter.Player.ToString() == "12", "Character positioning must remain independent of card-support requirements");
            Reject(() => Plan("11", "23", null, timed), "Timed stage supports need further coordination");
            Reject(() => Plan("11", "23", null, mixed), "Mixed support types need further coordination");
            Reject(() => Plan("11", "12", "7[1]补独\t010|12", water), "A timed support card must not enter fixed duo mode");
            Reject(() => Plan("11", "12", "7[1]极\t000|12", water), "Explicit supports must maintain their formation");
        }

        private static void CarrierDefaults()
        {
            var folder = Path.Combine(_scratch, "承载轨道", "1Test");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "1Cloud.txt"), "棉花糖\t7[1]独\t010|12\t020|22");
            var map = CarrierMap.Find(_scratch, new LevelEntry { Island = "Test", Name = "Cloud" }, null);
            Check(map != null && map.Clouds && map.Cells.Count == 0, "Timed-only support must survive lookup without becoming fixed support");
            var empty = Card("麦芽糖\t7[1]继绝");
            var inherited = map.Resolve(empty);
            Check(inherited.Name == "麦芽糖" && inherited.Steps.Count == 2 && inherited.Independent && inherited.Absolute, "Cloud alias must inherit default timing");
            Check(empty.Steps.Count == 0, "Resolving defaults must not mutate the loaded track");
            var explicitCard = Card("麦芽糖\t7[1]独继\t025|32\t035|42");
            var explicitResolved = map.Resolve(explicitCard);
            Check(!ReferenceEquals(explicitCard, explicitResolved) && explicitResolved.Independent &&
                  explicitResolved.Steps[0].Cell.Equals(new Cell(3, 2)) &&
                  explicitResolved.Steps[1].Cell.Equals(new Cell(4, 2)) &&
                  explicitResolved.Steps[0].TimeDeciseconds == 100 &&
                  explicitResolved.Steps[1].TimeDeciseconds == 200,
                "Explicit support cells retain their locations and inherit timing");
            Check(explicitCard.Steps[0].TimeDeciseconds == 250,
                "Resolving explicit inheritance must not mutate the source track");
            var disabled = Card("麦芽糖\t7[1]");
            Check(ReferenceEquals(disabled, map.Resolve(disabled)), "Timed defaults need opening inherit opt-in");
            Check(!map.Matches(Card("木盘子\t7[1]")), "A water carrier must not fill cloud defaults");
            File.WriteAllText(Path.Combine(folder, "2Water.txt"), "木盘子\t7[1]\t000|13\t000|23");
            var water = CarrierMap.Find(_scratch, new LevelEntry { Island = "Test", Name = "Water" }, null);
            var fixedCard = water.Resolve(Card("木盘子\t7[1]继"));
            Check(water.Cells.Count == 2 && fixedCard.Fill && fixedCard.Extreme, "Fixed support must replenish");
        }

        private static void TimedRetries()
        {
            var now = DateTime.UtcNow;
            var schedule = new PlacementSchedule(Card("Support\t7[1]独\t010|12\t020|22").Steps);
            Check(schedule.Next(9, now, false) == null, "Must not place early");
            schedule.Attempt(0, now);
            schedule.Retry(0, now);
            Check(schedule.Next(19, now.AddMilliseconds(499), false) == null, "Rapid retry must wait");
            Check(schedule.Next(19, now.AddMilliseconds(500), false) == 0, "A late first attempt must get a full retry window");
            schedule.Retry(0, now.AddSeconds(7));
            Check(schedule.Next(19, now.AddSeconds(8), false) == null, "Old failures need slower retries");
            Check(schedule.Next(21, now.AddSeconds(8), false) == 1, "Old failure must not block a newly due event");
            schedule.Attempt(1, now.AddSeconds(8));
            schedule.Complete(1);
            Check(schedule.Next(25, now.AddSeconds(12), false) == 0, "Old failure must remain repairable");
            schedule.Complete(0);
            Check(schedule.Done, "All events should complete");
        }

        private static void LateSchedule()
        {
            var now = DateTime.UtcNow;
            var schedule = new PlacementSchedule(Card("Support\t7[1]独\t010|12\t020|22\t030|12").Steps);
            schedule.Attempt(0, now); schedule.Complete(0);
            schedule.Attempt(1, now); schedule.Complete(1);
            schedule.Retry(1, now);
            Check(schedule.Next(25, now.AddSeconds(1), false) == 1, "Late rejection should restore only the rejected event");
            schedule.Complete(1);
            Check(schedule.Next(25, now.AddSeconds(2), false) == null, "Completed steps must not replay");
            schedule.Retry(0, now);
            schedule.Complete(2, true);
            Check(schedule.Done, "A later repair of the same cell supersedes the older failed repair");
        }

        private static void MissionStates()
        {
            Check(MissionEvidence.State(Frame(Color.White)) == MissionState.Unknown, "Unknown is not accepted");
            var active = MissionFrame(Color.Cyan, false, false);
            Check(MissionEvidence.State(active) == MissionState.InProgress && !MissionEvidence.CanClaim(active), "An active task cannot be claimed");
            var complete = MissionFrame(Color.Red, true, false);
            Check(MissionEvidence.State(complete) == MissionState.Complete && MissionEvidence.CanClaim(complete), "Complete requires an enabled reward button");
            var claimed = MissionFrame(Color.Red, false, true);
            Check(MissionEvidence.State(claimed) == MissionState.Claimed && !MissionEvidence.CanClaim(claimed), "The claimed stamp prevents a second claim");
            Check(!MissionEvidence.CanClaim(MissionFrame(Color.Red, false, false)), "A disabled button is not claimable");
            bool rejected = false;
            try { MissionEvidence.RequireGuard(null, "abc"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Missing hashes must not authorize input");
            rejected = false;
            try { MissionEvidence.RequireGuard("old", "new"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Changed hashes must not authorize input");
        }

        private static void MissionListAnimation()
        {
            StageFrame ListFrame(Color header, bool moved, bool pending) => Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(header))
                {
                    graphics.FillRectangle(brush, 100, 132, 302, 25);
                    graphics.FillRectangle(Brushes.White, 142, 140, 40, 7);
                    graphics.FillRectangle(Brushes.DarkOrange, 142, moved ? 204 : 174, 60, 8);
                    graphics.FillRectangle(pending ? Brushes.LimeGreen : Brushes.Orange, 370, 170, 20, 15);
                }
            });
            var first = ListFrame(Color.DarkBlue, false, false);
            var animated = ListFrame(Color.Cyan, false, true);
            Check(MissionEvidence.Hash(first, MissionEvidence.List) != MissionEvidence.Hash(animated, MissionEvidence.List),
                "The former raw guard must reproduce the animated-header mismatch");
            Check(MissionEvidence.ListHash(first) == MissionEvidence.ListHash(animated),
                "Header glow and status badges must not change label identity");
            Check(MissionEvidence.Pending(animated), "Reward status remains checked separately from identity");
            bool rejected = false;
            try { MissionEvidence.RequireGuard(MissionEvidence.ListHash(first), MissionEvidence.ListHash(ListFrame(Color.DarkBlue, true, false))); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Moving a visible task label must still invalidate the guard");
        }

        private static StageFrame MissionFrame(Color banner, bool button, bool claimed, bool pending = false, bool disconnected = false)
        {
            return Frame(Color.White, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.FillRectangle(Brushes.Magenta, 10, 10, 8, 8);
                    graphics.FillRectangle(disconnected ? Brushes.White : Brushes.Black, 0, 0, 3, 3);
                    using (var brush = new SolidBrush(banner)) graphics.FillRectangle(brush, MissionEvidence.Banner);
                    if (banner == Color.Cyan) graphics.FillRectangle(Brushes.Cyan, 370, 170, 20, 15);
                    if (button) graphics.FillRectangle(Brushes.DarkOrange, MissionEvidence.RewardButton);
                    if (claimed) graphics.FillRectangle(Brushes.Red, MissionEvidence.RewardStamp);
                    if (pending) graphics.FillRectangle(Brushes.LimeGreen, 370, 170, 20, 15);
                }
            });
        }

        private static void MissionGuards()
        {
            var directory = Path.Combine(_scratch, "mission-templates");
            Directory.CreateDirectory(directory);
            using (var bitmap = new Bitmap(8, 8))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Magenta);
                bitmap.Save(Path.Combine(directory, "(10,10)任务看板.png"));
            }
            var library = new TemplateLibrary();
            library.LoadDirectory(directory);
            var active = MissionFrame(Color.Cyan, false, false);
            var io = new MissionIo { Current = active };
            var actions = new MissionActions(io, Detector(), library, _scratch);
            var observation = actions.ExecuteAsync("inspect", 0, null, null).GetAwaiter().GetResult();
            Check(io.Clicks == 0 && observation.SelectedState == "InProgress", "Inspect sends no clicks");
            bool rejected = false;
            try { actions.ExecuteAsync("accept", 173, "stale", null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 0, "A stale list must not be clicked");
            var accepted = actions.ExecuteAsync("accept", 173, observation.ListHash, null).GetAwaiter().GetResult();
            Check(io.Clicks == 1 && accepted.SelectedState == "InProgress", "Acceptance must verify the active banner");
            rejected = false;
            try { actions.ExecuteAsync("accept", 143, accepted.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 2, "A category header must not count as an accepted task");
            io.Current = MissionFrame(Color.Cyan, false, false, true);
            observation = actions.ExecuteAsync("inspect", 0, null, null).GetAwaiter().GetResult();
            rejected = false;
            try { actions.ExecuteAsync("accept", 173, observation.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 2, "Pending rewards prevent new task acceptance");
            io.Current = MissionFrame(Color.Red, true, false);
            io.AfterClick = MissionFrame(Color.Red, false, true);
            observation = actions.ExecuteAsync("inspect", 0, null, null).GetAwaiter().GetResult();
            var result = actions.ExecuteAsync("claim", 0, null, observation.SelectedHash).GetAwaiter().GetResult();
            Check(io.Clicks == 3 && result.SelectedState == "Claimed", "A claim must verify its postcondition");
            rejected = false;
            try { actions.ExecuteAsync("claim", 0, null, result.SelectedHash).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 3, "Claimed rewards cannot be claimed again");
            io.Current = MissionFrame(Color.Cyan, false, false, disconnected: true);
            observation = actions.ExecuteAsync("inspect", 0, null, null).GetAwaiter().GetResult();
            Check(observation.View == "Unknown" && observation.SelectedState == "Unknown", "Disconnect overlays hide the underlying mission evidence");
            rejected = false;
            try { actions.ExecuteAsync("accept", 173, result.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 3, "Disconnects must not accept the task visible underneath");
        }

        private static void MissionClaimFeedback()
        {
            var library = new TemplateLibrary();
            library.LoadDirectory(Path.Combine(_scratch, "mission-templates"));
            var pending = MissionFrame(Color.Red, true, false);
            var feedback = Frame(Color.White, bitmap =>
            {
                using (var source = pending.ToBitmap())
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImageUnscaled(source, 0, 0);
                    graphics.FillRectangle(Brushes.Lime, 310, 290, 325, 28);
                }
            });
            var io = new MissionIo { Current = pending };
            int postClickGrabs = 0;
            io.FrameSource = () => io.Clicks > 0 && ++postClickGrabs == 1 ? feedback : pending;
            var actions = new MissionActions(io, Detector(), library, _scratch);
            string message = null;
            try { actions.ExecuteAsync("claim", 0, null, MissionEvidence.Hash(pending, MissionEvidence.Selected)).GetAwaiter().GetResult(); }
            catch (InvalidOperationException error) { message = error.Message; }
            var captures = Directory.GetFiles(_scratch, "missions-attempt-*.png");
            Check(message != null && io.Clicks == 1 && captures.Length == 1 && message.Contains(captures[0]),
                "An unconfirmed claim must fail once and identify its private feedback capture");
            using (var bitmap = new Bitmap(captures[0]))
                Check(bitmap.GetPixel(315, 295).ToArgb() == Color.Lime.ToArgb(),
                    "The failed attempt capture must retain the transient notice after later frames remove it");
            Check(MissionEvidence.CanClaim(io.Grab()), "Saving diagnostics must not mark the unclaimed reward as collected");
        }

        private static void MissionRewardRows()
        {
            StageFrame Badges(int shift) => Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.FillRectangle(Brushes.Lime, 360, 166 + shift, 28, 15);
                    graphics.FillRectangle(Brushes.Lime, 360, 388 + shift, 28, 15);
                    graphics.FillRectangle(Brushes.Cyan, 360, 250 + shift, 28, 15);
                    graphics.FillRectangle(Brushes.Magenta, 360, 280 + shift, 28, 15);
                    graphics.FillRectangle(Brushes.Lime, 360, 320 + shift, 3, 3);
                }
            });
            Check(MissionEvidence.CompletedRewardRows(Badges(0)).SequenceEqual(new[] { 173, 395 }),
                "Only complete badges should produce ordered row centers");
            Check(MissionEvidence.CompletedRewardRows(Badges(29)).SequenceEqual(new[] { 202, 424 }),
                "Scrolling must change the suggested row coordinates");
            Check(MissionEvidence.CompletedRewardRows(Frame(Color.Black)).Count == 0,
                "A frame without completed badges must return no candidate rows");
        }

        private static void MissionSelection()
        {
            var library = new TemplateLibrary();
            library.LoadDirectory(Path.Combine(_scratch, "mission-templates"));
            var io = new MissionIo { Current = MissionFrame(Color.Red, true, false, true) };
            var actions = new MissionActions(io, Detector(), library, _scratch);
            var before = actions.ExecuteAsync("inspect", 0, null, null).GetAwaiter().GetResult();
            bool rejected = false;
            try { actions.ExecuteAsync("select", 177, "stale", null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 0, "Selection must reject stale list evidence");
            rejected = false;
            try { actions.ExecuteAsync("select", 220, before.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 0, "Pending rewards prevent selecting an unrelated row");
            var selected = actions.ExecuteAsync("select", 177, before.ListHash, null).GetAwaiter().GetResult();
            Check(io.Clicks == 1 && selected.CanClaim && selected.SelectedState == "Complete",
                "Selecting a completed row returns fresh claim evidence without collecting its reward");
            rejected = false;
            try { actions.ExecuteAsync("accept", 177, selected.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 1, "Selection must not relax the acceptance guard");
            io.Current = MissionFrame(Color.Cyan, false, false, disconnected: true);
            rejected = false;
            try { actions.ExecuteAsync("select", 177, selected.ListHash, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && io.Clicks == 1, "A disconnect hides the task board during selection");
        }

        private static void MissionNavigation()
        {
            var directory = Path.Combine(_scratch, "mission-navigation-templates");
            Directory.CreateDirectory(directory);
            var rules = (IEnumerable)typeof(ScreenDetector).GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var names = new HashSet<string>(new[] { "房间_2P等待中", "房间_2P已关闭", "任务看板", "公会", "公会任务", "发布任务" });
            foreach (var rule in rules)
                foreach (var name in (string[])rule.GetType().GetField("Item2").GetValue(rule)) names.Add(name);
            var markers = new Dictionary<string, Point>();
            foreach (var name in names)
            {
                var point = new Point(100 + markers.Count * 25, 2);
                markers.Add(name, point);
                using (var bitmap = new Bitmap(1, 1))
                {
                    bitmap.SetPixel(0, 0, Color.White);
                    bitmap.Save(Path.Combine(directory, "(" + point.X + "," + point.Y + ")" + name + ".png"));
                }
            }
            StageFrame Mark(params string[] shown) => Frame(Color.Black, bitmap =>
            {
                foreach (var name in shown) bitmap.SetPixel(markers[name].X, markers[name].Y, Color.White);
            });
            var library = new TemplateLibrary();
            library.LoadDirectory(directory);
            var menu = Mark("公会");
            using (var bitmap = menu.ToBitmap())
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.GreenYellow, 646, 266, 205, 86);
                menu = StageFrame.FromBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            var io = new MissionNavigationIo { Lobby = Mark("排行", "跳转按钮"), Account = Mark("任务看板"),
                Guild = Mark("公会"), GuildTasks = Mark("公会任务"), Menu = menu };
            io.Current = io.Lobby;
            var actions = new MissionActions(io, new ScreenDetector(library), library, _scratch);
            var opened = actions.ExecuteAsync("open", 0, null, null).GetAwaiter().GetResult();
            Check(opened.View == "AccountTasks" && io.Clicks.Count == 1, "Opening account tasks must reach the verified board");
            actions.ExecuteAsync("open", 0, null, null).GetAwaiter().GetResult();
            Check(io.Clicks.Count == 1, "An already open account board sends no input");
            opened = actions.ExecuteAsync("open-guild", 0, null, null).GetAwaiter().GetResult();
            Check(opened.View == "GuildTasks" && io.Clicks.Count == 5 && io.MenuClosedByMove == 0,
                "Switching boards must verify the hall and retain its transient menu");
            actions.ExecuteAsync("open-guild", 0, null, null).GetAwaiter().GetResult();
            Check(io.Clicks.Count == 5, "An already open guild board sends no input");
            opened = actions.ExecuteAsync("open", 0, null, null).GetAwaiter().GetResult();
            Check(opened.View == "AccountTasks" && io.Clicks.Count == 8, "Account navigation must return through the hall and lobby");
            Check(!io.Clicks.Contains(new Point(125, 57)) && !io.Clicks.Contains(new Point(484, 440)) &&
                !io.Clicks.Contains(new Point(641, 531)) && io.Clicks.All(point => point.X > 402 || point.Y < 140 || point.Y > 526),
                "Opening boards must never publish, draw, select a task or claim rewards");
            foreach (var blocked in new[] { Mark("任务看板", "断开连接"), Mark("任务看板", "健康提示"),
                Mark("任务看板", "活动弹窗_关闭"), Mark("选卡界面"), Frame(Color.Black) })
                foreach (var action in new[] { "open", "open-guild" })
                {
                    io.Current = blocked;
                    bool rejected = false;
                    try { actions.ExecuteAsync(action, 0, null, null).GetAwaiter().GetResult(); }
                    catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && io.Clicks.Count == 8, "A popup or unknown screen must stop before any navigation click");
                }
            io.Current = io.GuildTasks;
            io.HoldTransitions = true;
            bool stalled = false;
            try { actions.ExecuteAsync("open", 0, null, null).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { stalled = true; }
            Check(stalled && io.Clicks.Count == 9, "A failed board close must stop before the next navigation click");
        }

        private sealed class MissionNavigationIo : IGameIo
        {
            internal StageFrame Current, Lobby, Account, Guild, GuildTasks, Menu;
            internal readonly List<Point> Clicks = new List<Point>();
            internal bool HoldTransitions;
            internal int MenuClosedByMove;
            public StageFrame Grab() => Current;
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            {
                Clicks.Add(point);
                if (HoldTransitions) return Task.CompletedTask;
                if (Current == GuildTasks && point == new Point(853, 56)) Current = Guild;
                else if (Current == Account && point == new Point(853, 53) || Current == Guild && point == new Point(904, 57)) Current = Lobby;
                else if (Current == Lobby && point == new Point(641, 559)) Current = Account;
                else if (Current == Lobby && point == new Point(778, 555)) Current = Guild;
                else if (Current == Guild && point == new Point(748, 419)) Current = Menu;
                else if (Current == Menu && point == new Point(696, 310)) Current = GuildTasks;
                return Task.CompletedTask;
            }
            public void Move(Point point) { if (Current == Menu) { MenuClosedByMove++; Current = Guild; } }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private static void GuildButtonColors()
        {
            var gold = Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(Color.FromArgb(235, 210, 100)))
                {
                    graphics.FillRectangle(brush, 88, 44, 74, 20);
                    graphics.FillRectangle(Brushes.DarkOrange, 88, 44, 10, 10);
                }
            });
            Check(MissionEvidence.Count(gold, new Rectangle(88, 44, 74, 20), MissionEvidence.Orange) == 100,
                "The formerly rejected gold button has only 100 orange pixels");
            Check(MissionEvidence.GuildPublishEnabled(gold), "Gold gradients must enable publication");
            Check(!MissionEvidence.GuildPublishEnabled(Frame(Color.Gray)), "A gray button cannot authorize a draw");
            var unrelated = Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.FillRectangle(Brushes.Gold, 436, 425, 96, 24);
            });
            Check(!MissionEvidence.GuildPublishEnabled(unrelated), "Gold outside the publication button must be ignored");
        }

        private static void TransitionScreenStatus()
        {
            var io = new MissionIo { Current = Frame(Color.Black, bitmap => bitmap.SetPixel(10, 10, Color.White)) };
            var settings = new GrindSettings { Track = TrackFile.Parse("人物1位置=11\nCard\t7[3]\t001|12"),
                SlotCount = 1, SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 } };
            var loop = new GrindLoop(io, Detector(), settings, Path.Combine(_scratch, "transition-screen"));
            Set(loop, "<LastScreen>k__BackingField", GameScreen.VictoryDialog);
            var found = ((Task<bool>)Call(loop, "WaitForAsync", new Func<GameScreen, bool>(screen => screen == GameScreen.Battle),
                TimeSpan.FromSeconds(1), CancellationToken.None)).GetAwaiter().GetResult();
            Check(found && loop.LastScreen == GameScreen.Battle, "A recognized boss transition must replace the old victory screen");
            io.Current = Frame(Color.White);
            ((Task)Call(loop, "WaitUntilAsync", new Func<GameScreen, bool>(screen => screen == GameScreen.Disconnected),
                TimeSpan.FromSeconds(1), "test transition failed", CancellationToken.None)).GetAwaiter().GetResult();
            Check(loop.LastScreen == GameScreen.Disconnected && io.Clicks == 0,
                "Transition polling reports observed failures without sending extra input");
        }

        private static void FinishRoundRewards()
        {
            var directory = Path.Combine(_scratch, "finish-round-templates");
            Directory.CreateDirectory(directory);
            var rules = (IEnumerable)typeof(ScreenDetector).GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var markers = new Dictionary<string, Point>();
            foreach (var rule in rules)
                foreach (var name in (string[])rule.GetType().GetField("Item2").GetValue(rule))
                    if (!markers.ContainsKey(name)) markers[name] = new Point(20 + markers.Count * 8, 10);
            foreach (var name in new[] { "房间_2P等待中", "房间_2P已关闭" }) markers[name] = new Point(20 + markers.Count * 8, 10);
            foreach (var marker in markers)
                using (var bitmap = new Bitmap(1, 1))
                {
                    bitmap.SetPixel(0, 0, Color.White);
                    bitmap.Save(Path.Combine(directory, "(" + marker.Value.X + ",10)" + marker.Key + ".png"));
                }
            StageFrame Marked(params string[] names) => Frame(Color.Black, bitmap =>
            {
                foreach (var name in names) bitmap.SetPixel(markers[name].X, markers[name].Y, Color.White);
            });
            var library = new TemplateLibrary();
            library.LoadDirectory(directory);
            foreach (bool boss in new[] { false, true })
            {
                var io = new FinishRoundIo
                {
                    Room = Marked("选卡界面", "房间_2P已关闭"), Battle = Marked("退出按钮"), Victory = Marked("胜利弹窗"),
                    Shuffle = Marked("翻宝箱", "翻宝箱_开始洗牌"), Pick = Marked("翻宝箱"), Done = Marked("翻宝箱", "翻宝箱_完成"),
                    Lobby = Marked("排行", "跳转按钮"), World = Marked("世界地图"), IgnoredWorldMapClicks = boss ? 1 : 0
                };
                io.Current = io.Room;
                var settings = new GrindSettings { Track = TrackFile.Parse("人物1位置=11\nCard\t7[3]\t001|12"),
                    Rounds = 3, Boss = boss, SlotCount = 1, SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 } };
                var loop = new GrindLoop(io, new ScreenDetector(library), settings, Path.Combine(_scratch, "finish-round-" + boss));
                io.OnCharacter = () => { loop.FinishAfterRound(); loop.FinishAfterRound(); };
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20))) loop.RunAsync(timeout.Token).GetAwaiter().GetResult();
                Check(loop.State == "finished-early" && loop.RoundsDone == 1 && loop.LastScreen == GameScreen.Lobby,
                    "Finish must collect the current round and return to town instead of launching round two");
                Check(io.Starts == 1 && io.FreeFlips == 1 && io.Finishes == 1 && io.Characters == 1,
                    "A finish request must not abort rewards, replay a paid flip, or place a second character");
                Check(loop.BossRoundsDone == (boss ? 1 : 0) && io.BossEntries == (boss ? 1 : 0),
                    "A requested boss continuation belongs to the current round and must finish before stopping");
                Check(io.WorldMapClicks == (boss ? 2 : 1), "A lost first map input must retry once without replaying the reward flow");
            }
            var returnSettings = new GrindSettings { Track = TrackFile.Parse("Card\t7[3]\t001|12"),
                SlotCount = 1, SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 } };
            foreach (var blocked in new[] { Marked("断开连接"), Marked("健康提示"), Marked("活动弹窗_关闭"), Frame(Color.Black) })
            {
                var io = new FinishRoundIo { Current = blocked };
                var loop = new GrindLoop(io, new ScreenDetector(library), returnSettings, Path.Combine(_scratch, "return-map-guard"));
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                {
                    bool stopped = false;
                    try { ((Task)Call(loop, "OpenWorldMapAsync", timeout.Token)).GetAwaiter().GetResult(); }
                    catch (TimeoutException) { stopped = true; }
                    catch (OperationCanceledException) { stopped = true; }
                    Check(stopped && io.WorldMapClicks == 0, "A popup or unknown screen cannot authorize a map click");
                }
            }
            var stalledIo = new FinishRoundIo { Current = Marked("排行", "跳转按钮"), IgnoredWorldMapClicks = int.MaxValue };
            var stalledLoop = new GrindLoop(stalledIo, new ScreenDetector(library), returnSettings, Path.Combine(_scratch, "return-map-stalled"));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9)))
            {
                bool stopped = false;
                try { ((Task)Call(stalledLoop, "OpenWorldMapAsync", timeout.Token)).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { stopped = true; }
                Check(stopped && stalledIo.WorldMapClicks == 3, "An unresponsive visible lobby gets at most three map clicks");
            }
            var duoSettings = new GrindSettings { Track = TrackFile.Parse("人物1位置=11\nCard\t7[3]\t001|12"),
                SlotCount = 1, SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 }, Duo = new DuoSession(DuoRole.Host, 0) };
            var duoLoop = new GrindLoop(null, null, duoSettings, Path.Combine(_scratch, "finish-round-duo"));
            bool rejected = false;
            try { duoLoop.FinishAfterRound(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !duoLoop.FinishAfterRoundRequested, "A solo finish request cannot split a paired run");
        }

        private sealed class FinishRoundIo : IGameIo
        {
            internal StageFrame Room, Battle, Victory, Shuffle, Pick, Done, Lobby, World, Current;
            internal Action OnCharacter;
            internal int Starts, FreeFlips, Finishes, Characters, BossEntries;
            internal int WorldMapClicks, IgnoredWorldMapClicks;
            private bool _bossTransition;
            private int _bossGrabs;
            public StageFrame Grab()
            {
                if (_bossTransition && ++_bossGrabs > 1) { _bossTransition = false; Current = Victory; }
                return Current;
            }
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            {
                if (point == new Point(857, 456)) { Starts++; Current = Battle; }
                else if (Current == Battle && point == new BattleLayout(1).CellCenter(new Cell(1, 1)))
                { Characters++; OnCharacter?.Invoke(); Current = Victory; }
                else if (point == new Point(468, 345)) { BossEntries++; Current = Battle; _bossTransition = true; _bossGrabs = 0; }
                else if (point == new Point(568, 345)) Current = Shuffle;
                else if (point == new Point(708, 504)) Current = Pick;
                else if (point == new Point(709, 367)) { FreeFlips++; Current = Done; }
                else if (point == new Point(709, 505)) { Finishes++; Current = Room; }
                else if (point == new Point(915, 568)) Current = Lobby;
                else if (point == new Point(866, 58)) { WorldMapClicks++; if (WorldMapClicks > IgnoredWorldMapClicks) Current = World; }
                else if (point == new Point(500, 187)) Current = Lobby;
                return Task.CompletedTask;
            }
            public void Move(Point point) { }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private static void GuildPublication()
        {
            var directory = Path.Combine(_scratch, "guild-templates");
            Directory.CreateDirectory(directory);
            var names = new[] { "公会", "公会任务", "发布任务" };
            var colors = new[] { Color.Magenta, Color.Yellow, Color.Blue };
            for (int index = 0; index < names.Length; index++)
                using (var bitmap = new Bitmap(8, 8))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(colors[index]);
                    bitmap.Save(Path.Combine(directory, "(" + (10 + index * 20) + ",10)" + names[index] + ".png"));
                }
            StageFrame GuildFrame(int marker, bool menu, bool enabled) => Frame(Color.Black, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(colors[marker]))
                {
                    graphics.FillRectangle(brush, 10 + marker * 20, 10, 8, 8);
                    if (menu) graphics.FillRectangle(Brushes.GreenYellow, 634, 254, 232, 108);
                    if (enabled) graphics.FillRectangle(Brushes.DarkOrange, marker == 1
                        ? new Rectangle(88, 44, 74, 20) : new Rectangle(436, 425, 96, 24));
                }
            });
            var library = new TemplateLibrary();
            library.LoadDirectory(directory);
            var io = new GuildIo { Guild = GuildFrame(0, false, false), Menu = GuildFrame(0, true, false),
                Tasks = GuildFrame(1, false, true), Publish = GuildFrame(2, false, true),
                Drawn = GuildFrame(2, false, false), Published = GuildFrame(1, false, false) };
            io.Current = io.Guild;
            var actions = new MissionActions(io, Detector(), library, _scratch);
            var result = actions.ExecuteAsync("publish", 0, null, null).GetAwaiter().GetResult();
            Check(io.Draws == 1 && io.MenuClosedByMove == 0 && !result.CanPublish, "The hover menu must survive until its task entry is clicked");
            actions.ExecuteAsync("publish", 0, null, null).GetAwaiter().GetResult();
            Check(io.Draws == 1, "Already-published tasks must not be redrawn");
            io.Current = io.Menu;
            actions.ExecuteAsync("publish", 0, null, null).GetAwaiter().GetResult();
            Check(io.MenuClosedByMove == 0, "An already-open activities menu must not be toggled closed");
        }

        private sealed class GuildIo : IGameIo
        {
            internal StageFrame Guild, Menu, Tasks, Publish, Drawn, Published, Current;
            internal int Draws, MenuClosedByMove;
            public StageFrame Grab() => Current;
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            {
                if (point == new Point(748, 419)) Current = Menu;
                else if (point == new Point(696, 310) && Current == Menu) Current = Tasks;
                else if (point == new Point(125, 57)) Current = Publish;
                else if (point == new Point(484, 440)) { Draws++; Current = Drawn; }
                else if (point == new Point(635, 200)) Current = Published;
                return Task.CompletedTask;
            }
            public void Move(Point point) { if (Current == Menu) { MenuClosedByMove++; Current = Guild; } }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private sealed class MissionIo : IGameIo
        {
            internal StageFrame Current;
            internal StageFrame AfterClick;
            internal int Clicks;
            internal Func<StageFrame> FrameSource;
            public StageFrame Grab() => FrameSource == null ? Current : FrameSource();
            public Task ClickAsync(Point point, MouseButtonType button = MouseButtonType.Left)
            { Clicks++; if (AfterClick != null) Current = AfterClick; return Task.CompletedTask; }
            public void Move(Point point) { }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public void Repaint() { }
        }

        private static StageFrame Frame(Color background, Action<Bitmap> paint = null)
        {
            using (var bitmap = new Bitmap(950, 600, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap)) g.Clear(background);
                paint?.Invoke(bitmap);
                return StageFrame.FromBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
        }

        private static BattleRunner Runner(string trackText, StageFrame opening, FakeIo io = null)
        {
            var track = TrackFile.Parse(trackText);
            var slots = track.Opening.DeckCards.Select((c, i) => new { c.Name, Slot = i + 1 }).ToDictionary(c => c.Name, c => c.Slot);
            var runner = new BattleRunner(io, io == null ? null : Detector(), new BattleLayout(slots.Count), track, slots, false, false, null, _ => { });
            Set(runner, "_openingField", opening);
            Set(runner, "_start", DateTime.UtcNow.AddSeconds(-40));
            return runner;
        }

        private static object Own(BattleRunner runner, Cell cell, StageFrame before, StageFrame after, DateTime at)
        {
            var own = Activator.CreateInstance(typeof(BattleRunner).GetNestedType("OwnCard", BindingFlags.NonPublic), true);
            Set(own, "Before", before.Crop(Layout.CellInterior(cell)));
            Set(own, "After", after.Crop(Layout.CellInterior(cell)));
            Set(own, "PlacedAt", at);
            ((IDictionary)Get(Plan(runner), "Own"))[cell] = own;
            return own;
        }

        private static List<Cell> Targets(BattleRunner runner, StageFrame frame, DateTime at) =>
            (List<Cell>)Call(runner, "FormationTargets", Plan(runner), frame, at, null);

        private static void LastEmptyCell()
        {
            var now = DateTime.UtcNow;
            var opening = Frame(Color.Black);
            var frame = Frame(Color.Navy, b =>
            {
                using (var g = Graphics.FromImage(b))
                    for (int row = 1; row <= 6; row++) g.FillRectangle(Brushes.Red, Layout.CellInterior(new Cell(row, 3)));
            });
            var runner = Runner("Card\t7[3]补极\t000|13\t000|23\t000|33\t000|43\t000|53\t000|63\t000|73", opening);
            ((int[])Get(runner, "_movingSightings"))[3] = 3;
            for (int row = 1; row <= 6; row++) Own(runner, new Cell(row, 3), opening, frame, now.AddSeconds(-20));
            Check(Targets(runner, frame, now).Single().Equals(new Cell(7, 3)), "Must reach the last unowned cell without an empty neighbor");
            Check(Targets(runner, frame, now.AddSeconds(11)).Count == 0, "Must back off after a probe");
            Check(Targets(runner, frame, now.AddSeconds(31)).Single().Equals(new Cell(7, 3)), "A failed probe must not permanently exclude the last cell");
        }

        private static void ProbeFairness()
        {
            var now = DateTime.UtcNow;
            var runner = Runner("Card\t7[3]补极\t000|12\t000|22", Frame(Color.Black));
            var unknown = Frame(Color.Blue);
            Check(Targets(runner, unknown, now)[0].Equals(new Cell(1, 2)), "First probe order");
            Check(Targets(runner, unknown, now.AddSeconds(11))[0].Equals(new Cell(2, 2)), "Untried cells should get a turn");
            Check(Targets(runner, unknown, now.AddSeconds(42))[0].Equals(new Cell(1, 2)), "Least recently tried cell should be revisited first");
        }

        private static void PaintCard(Bitmap bitmap, Rectangle region)
        {
            for (int y = region.Top; y < region.Bottom; y++)
                for (int x = region.Left; x < region.Right; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(240, 16 + (x - region.Left) % 24 * 8, 16));
        }

        private static CardSignature Signature()
        {
            var background = Frame(Color.Black).Crop(Layout.HeldCardRegion);
            var held = Frame(Color.Black, b => PaintCard(b, Layout.HeldCardRegion)).Crop(Layout.HeldCardRegion);
            Check(CardSignature.Learn(background, background, background, background) == null, "Unchanged capture must not learn a card");
            return CardSignature.Learn(background, background, held, held);
        }

        private static void Presence()
        {
            var now = DateTime.UtcNow;
            var cell = new Cell(4, 4);
            var opening = Frame(Color.Black);
            var visible = Frame(Color.Navy, b => PaintCard(b, Layout.CardRegion(cell)));
            var absent = Frame(Color.DarkCyan);
            var runner = Runner("Card\t7[3]补极\t000|44", opening);
            Set(Plan(runner), "Signature", Signature());
            var own = Own(runner, cell, opening, visible, now.AddSeconds(-20));
            Check((bool)Call(runner, "Present", Plan(runner), cell, visible, now), "Card should be visible against a changed background");
            Check((bool)Get(own, "SignatureSeen"), "Signature must first be validated on the board");
            Check((bool)Call(runner, "Present", Plan(runner), cell, absent, now.AddSeconds(1)), "One occluded sample must not remove the card");
            Check((bool)Call(runner, "Present", Plan(runner), cell, visible, now.AddSeconds(1.5)), "Visible card must reset absence evidence");
            foreach (var offset in new[] { 2.0, 2.5 })
                Check((bool)Call(runner, "Present", Plan(runner), cell, absent, now.AddSeconds(offset)), "Wait for multiple absence samples");
            Check(!(bool)Call(runner, "Present", Plan(runner), cell, absent, now.AddSeconds(3)), "Persistent loss must clear ownership without an empty neighbor");
        }

        private static void RefreshOwnership()
        {
            var now = DateTime.UtcNow;
            var opening = Frame(Color.Black);
            var visible = Frame(Color.Navy, b =>
            {
                for (int row = 1; row <= 7; row++) PaintCard(b, Layout.CardRegion(new Cell(row, 4)));
            });
            var runner = Runner("Card\t7[3]补极\t000|14\t000|24\t000|34\t000|44\t000|54\t000|64\t000|74", opening);
            Set(Plan(runner), "Signature", Signature());
            for (int row = 1; row <= 7; row++) Own(runner, new Cell(row, 4), opening, visible, now.AddSeconds(-20));
            Call(runner, "RefreshBoard", visible, now);
            var gone = Frame(Color.DarkCyan, b =>
            {
                for (int row = 1; row <= 7; row++)
                    if (row != 4) PaintCard(b, Layout.CardRegion(new Cell(row, 4)));
            });
            foreach (var offset in new[] { 1.0, 1.5, 2.0 }) Call(runner, "RefreshBoard", gone, now.AddSeconds(offset));
            var owned = (IDictionary)Get(Plan(runner), "Own");
            Check(owned.Count == 6 && !owned.Contains(new Cell(4, 4)), "A hole surrounded by the same card must be detected without losing adjacent cards");
        }

        private static void UnverifiedOwnership()
        {
            var now = DateTime.UtcNow;
            var opening = Frame(Color.Black);
            var ambiguous = Frame(Color.Navy);
            var runner = Runner("Card\t7[3]补极\t000|44", opening);
            Set(runner, "_cardsFall", true);
            Own(runner, new Cell(4, 4), opening, ambiguous, now.AddSeconds(-44));
            Check((bool)Call(runner, "Present", Plan(runner), new Cell(4, 4), ambiguous, now), "Unverified occupancy must retain its grace period");
            foreach (var offset in new[] { 1.0, 1.5, 2.0 }) Call(runner, "Present", Plan(runner), new Cell(4, 4), ambiguous, now.AddSeconds(offset));
            Check(((IDictionary)Get(Plan(runner), "Own")).Count == 0, "Unverified occupancy should expire into an uncertain cell");
        }

        private static void HeldSignatureLearning()
        {
            var io = new FakeIo { RenderCard = true };
            var runner = Runner("Card\t7[3]补极\t000|12", io.Grab(), io);
            var plan = Plan(runner);
            Check(((Task<bool>)Call(runner, "PlaceAsync", plan, io.Grab(), CancellationToken.None, null)).GetAwaiter().GetResult(), "Ordinary card should place");
            var signature = (CardSignature)Get(plan, "Signature");
            Check(signature != null && signature.Score(io.Grab(), Layout.CardRegion(new Cell(1, 2))) >= 0.3,
                "Held-card learning must produce usable on-board evidence");
        }

        private static void GuestChoicePrecedence()
        {
            var path = Path.Combine(_scratch, "choice-templates");
            Directory.CreateDirectory(path);
            var rules = (IEnumerable)typeof(ScreenDetector).GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var names = new HashSet<string>(new[] { "房间_2P等待中", "房间_2P已关闭" });
            foreach (var rule in rules)
                foreach (var name in (string[])rule.GetType().GetField("Item2").GetValue(rule)) names.Add(name);
            var points = new Dictionary<string, Point>();
            int index = 0;
            foreach (var name in names)
                using (var bitmap = new Bitmap(1, 1))
                {
                    var point = new Point(10 + index++ * 10, 10);
                    points[name] = point;
                    bitmap.SetPixel(0, 0, Color.White);
                    bitmap.Save(Path.Combine(path, "(" + point.X + ",10)" + name + ".png"));
                }
            var library = new TemplateLibrary();
            library.LoadDirectory(path);
            var detector = new ScreenDetector(library, allowGuestDecision: true);
            StageFrame Choice(string marker) => Frame(Color.Black, bitmap =>
            {
                bitmap.SetPixel(475, 290, Color.FromArgb(130, 180, 230));
                bitmap.SetPixel(630, 225, Color.FromArgb(130, 180, 230));
                bitmap.SetPixel(500, 350, Color.White);
                if (marker != null) bitmap.SetPixel(points[marker].X, points[marker].Y, Color.White);
            });
            Check(detector.Detect(Choice(null)) == GameScreen.AwaitHostDecision, "The guest overlay must remain recognized");
            Check(detector.Detect(Choice("胜利弹窗")) == GameScreen.VictoryDialog, "A host victory must outrank the guest heuristic");
            Check(detector.Detect(Choice("健康提示")) == GameScreen.HealthNotice, "A recognized notice must outrank the guest heuristic");
            var cloudBattle = Choice("退出按钮");
            Check(detector.ForExecution(false).Detect(cloudBattle) == GameScreen.Battle,
                "Blue clouds and a white board cell must not end a solo or host battle");
            Check(new ScreenDetector(library).Detect(cloudBattle) == GameScreen.Battle,
                "Unscoped state inspection must not infer a guest-only overlay");
            Check(detector.ForExecution(true).Detect(cloudBattle) == GameScreen.AwaitHostDecision,
                "The guest execution context must retain its host-decision observation");
            StageFrame Room(bool notice) => Frame(Color.Black, bitmap =>
            {
                bitmap.SetPixel(points["选卡界面"].X, points["选卡界面"].Y, Color.White);
                if (!notice) return;
                foreach (var point in new[] { new Point(360, 264), new Point(594, 264), new Point(360, 327), new Point(594, 327) })
                    bitmap.SetPixel(point.X, point.Y, Color.FromArgb(126, 200, 245));
                foreach (int x in new[] { 394, 451, 494, 551 })
                    bitmap.SetPixel(x, 347, Color.FromArgb(245, 170, 40));
            });
            Check(detector.Detect(Room(false)) == GameScreen.Room, "An unobstructed room must remain recognized");
            Check(detector.Detect(Room(true)) == GameScreen.RoomNotice, "A compact system notice must outrank its underlying room");
            var noticeIo = new MissionIo { Current = Room(true) };
            var blocked = new GrindLoop(noticeIo, detector, new GrindSettings
            {
                TrackPath = "notice.txt", Track = TrackFile.Parse("人物1位置=11\nCard\t7[3]补极\t000|12"),
                SlotOfCard = new Dictionary<string, int> { ["Card"] = 1 }, SlotCount = 1, Rounds = 1,
            }, Path.Combine(_scratch, "room-notice"));
            blocked.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(blocked.State == "failed" && blocked.Messages.Any(message => message.Contains("system notice")) &&
                noticeIo.Clicks == 0 && blocked.RoundsDone == 0,
                "A blocking room notice must fail with an actionable message without accepting or starting it");
        }

        private static ScreenDetector Detector()
        {
            var path = Path.Combine(_scratch, "templates");
            Directory.CreateDirectory(path);
            var rules = (IEnumerable)typeof(ScreenDetector).GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var names = new HashSet<string>(new[] { "房间_2P等待中", "房间_2P已关闭" });
            foreach (var rule in rules)
                foreach (var name in (string[])rule.GetType().GetField("Item2").GetValue(rule)) names.Add(name);
            foreach (var name in names)
                using (var b = new Bitmap(1, 1))
                {
                    b.SetPixel(0, 0, Color.White);
                    b.Save(Path.Combine(path, (name == "退出按钮" ? "(10,10)" : "(0,0)") + name + ".png"));
                }
            var library = new TemplateLibrary();
            library.LoadDirectory(path);
            return new ScreenDetector(library);
        }

        private static void PlacementRollback()
        {
            var io = new FakeIo();
            var detector = Detector();
            Check(detector.Detect(io.Grab()) == GameScreen.Battle,
                "Synthetic detector expected Battle, got " + detector.Detect(io.Grab()) + "; loot diff " + detector.Diff("战利品", io.Grab()));
            var runner = Runner("Support\t7[1]独\t010|12", io.Grab(), io);
            var plan = Plan(runner);
            Check(((Task<bool>)Call(runner, "PlaceAsync", plan, io.Grab(), CancellationToken.None, null)).GetAwaiter().GetResult(), "Fake slot cooldown should confirm placement");
            Check(((PlacementSchedule)Get(plan, "Schedule")).Done && runner.Placements == 1, "Successful timed placement should complete once");
            // Ownership can disappear before the late-refusal signal; the event ledger must still recover.
            ((IDictionary)Get(plan, "Own")).Clear();
            Call(runner, "ForgetLatePlacement", 1);
            Check(!((PlacementSchedule)Get(plan, "Schedule")).Done && runner.Placements == 0, "Late rejection must restore a consumed event even after ownership was cleared");
            Call(runner, "ForgetLatePlacement", 1);
            Check(runner.Placements == 0, "Repeated rollback must not decrement twice");
        }

        private static void RefusedSupport()
        {
            var io = new FakeIo { Reject = true };
            var runner = Runner("Support\t7[1]独\t001|12", io.Grab(), io);
            Check(!((Task<bool>)Call(runner, "PlaceAsync", Plan(runner), io.Grab(), CancellationToken.None, null)).GetAwaiter().GetResult(), "Held card should be recognized as refused");
            Check(!((PlacementSchedule)Get(Plan(runner), "Schedule")).Done, "A refused late support must remain pending");
        }

        private static void SupportDependency()
        {
            var now = DateTime.UtcNow;
            var opening = Frame(Color.Black);
            var runner = Runner("棉花糖\t7[1]独\t010|12\t020|12\nCard\t7[3]独\t011|12", opening);
            var plans = (IList)Get(runner, "_plans");
            Set(runner, "_start", now);
            Call(runner, "RefreshBoard", opening, now.AddSeconds(9));
            Check(!((HashSet<Cell>)Get(runner, "_carrierCells")).Contains(new Cell(1, 2)), "Future cloud event must not block a solid cell at startup");
            Call(runner, "RefreshBoard", opening, now.AddSeconds(10));
            Check(((HashSet<Cell>)Get(runner, "_carrierCells")).Contains(new Cell(1, 2)), "Due cloud event must activate support dependency");
            Set(runner, "_start", now.AddSeconds(-15));
            Check(!((Task<bool>)Call(runner, "PlaceAsync", plans[1], opening, CancellationToken.None, null)).GetAwaiter().GetResult(), "Timed ordinary card must wait without using IO on an unsupported cloud");
        }

        private sealed class FakeIo : IGameIo
        {
            private int _state; // 0 ready, 1 held, 2 cooling
            public bool Reject;
            public bool RenderCard;
            public readonly List<Cell> CellClicks = new List<Cell>();
            public DateTime LastPageLoadUtc => DateTime.MinValue;
            public StageFrame Grab() => Frame(Color.Black, b =>
            {
                b.SetPixel(10, 10, Color.White);
                if (RenderCard && _state == 1) PaintCard(b, Layout.HeldCardRegion);
                if (RenderCard && _state == 2) PaintCard(b, Layout.CardRegion(new Cell(1, 2)));
                if (_state == 0)
                    using (var g = Graphics.FromImage(b)) g.FillRectangle(Brushes.White, Layout.SlotLabel(1));
            });
            public Task ClickAsync(Point at, MouseButtonType button = MouseButtonType.Left)
            {
                var cell = BattleLayout.CellAt(at);
                if (cell.HasValue) CellClicks.Add(cell.Value);
                if (at == Layout.SlotCenter(1)) { if (_state == 0) _state = 1; else if (_state == 1) _state = 0; }
                else if (_state == 1 && !Reject && cell.HasValue) _state = 2;
                return Task.CompletedTask;
            }
            public void Move(Point at) { }
            public Task TypeAsync(string text) => Task.CompletedTask;
            public void Reload() { }
            public void Repaint() { }
        }
    }
}
