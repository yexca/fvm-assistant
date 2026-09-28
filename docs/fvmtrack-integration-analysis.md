# FvmTrack integration opportunities

Reviewed on 2026-09-26 against the local `sample/FvmTrack` checkout at
`f651e905fb935a222c870c687adbebfbfe57d353` (v6.7.2). This is a source review and
an implementation proposal, not a claim of live-game compatibility. No application
behavior or third-party files were changed during that review. The subsequent P0
implementation is described under [Implementation follow-up](#implementation-follow-up).
The gap descriptions below reflect the original review; the follow-up sections
record which gaps have since been addressed.

## Current implementation status (2026-09-28)

The priority table and gap analysis below are the historical starting point.
For the exact accepted syntax and restrictions, use the current
[automation behavior](automation.md#track-compatibility-checks). The following
summary separates implemented paths from proposals still awaiting work:

| Area | Current state and evidence |
| --- | --- |
| Track preflight and effective deck | Shared panel/API/start diagnostics, support resolution, and a checked run snapshot; deterministic regressions and a live read-only 曲奇 check |
| Conditions and timing | Typed basic `极` conditions, fresh flame reads, tenth-second steps, synchronized/`独` clocks, scheduled `补`/`00` events, and initial `继` inheritance; synthetic regressions cover the semantics, while individual advanced paths still need live tests |
| Other opening actions | Explicit layer-5 cooldown placements and `放卡限速` are implemented and preflighted; live verification is pending |
| Queue and editor | Single-account queue `-DryRun` passed; the preview and copy editor build and pass deterministic construction/edit tests, but their layout and live interactions have not been visually checked |
| Two accounts | Manually paired 曲奇 accounts completed one round and then two consecutive rounds on both sides; [manual operation](two-account.md) is documented. Automatic room pairing stopped at live room-number recognition |
| Still unsupported | Later-wave execution, image/target/follow conditions, physically moving boards, resource-based auto scheduling, and loot-item accounting |

The [live test record](prototype-findings.md#2026-09-28-follow-up-test) distinguishes build and
synthetic checks from observed gameplay. These additions keep the off-screen
capture and CEF input boundary described in the original review.

## Recommendation

Prioritize track compatibility checks, flame conditions, timing semantics, and
wave transitions. Add a single-account task queue and an editable formation preview
after that foundation. These features extend the existing off-screen capture and
CEF input architecture without requiring the original executor as a dependency.

The earlier [cloud-stage analysis](track-editor-analysis.md) covers recognition and
support repairs already implemented. Those repairs should be retained; they do not
constitute full compatibility with FvmTrack.

## Review scope and evidence

The review covered relevant functions in these four programs and their shared
`track.h`, alongside FvmAssistant's parser, scheduler, battle runner, panel, controller,
screen detector, deck builder, and project documentation:

| Local upstream source | Relevant responsibilities |
| --- | --- |
| `sample/FvmTrack/轨道执行器/轨道执行器.cpp` | Track validation, recognition, scheduling, wave changes, task lists, navigation, and loot accounting |
| `sample/FvmTrack/轨道编辑器/轨道编辑器.cpp` | Formation/timeline editing, undo, recording, production calculations, and automatic scheduling |
| `sample/FvmTrack/综合截图工具/综合截图工具.cpp` | Card/image capture, recognition parameter adjustment, and template management |
| `sample/FvmTrack/轨道模拟器/轨道模拟器.cpp` | Card crafting and upgrade simulation; this is not a battle simulator |

Source references below use function names and one-based line numbers at the pinned
revision. The simulator source requires GBK decoding; the reviewed executor and
editor sources are readable as UTF-8.

A read-only text scan of `sample/FvmTrack/轨道执行器/预制轨道` found 482 `.txt`
files containing card specifications. Counts below mean files containing a feature
anywhere, including later waves. They overlap, include support and companion tracks,
and do not represent distinct stages, usage frequency, or a measured failure rate.
The scan used strict UTF-8 with GBK fallback and excluded text files without card rows.

| Feature present | Files |
| --- | ---: |
| Non-default comma-separated conditions | 192 |
| Positive flame threshold | 169 |
| Later wave sections | 144 |
| `独` flag | 202 |
| `继` flag | 138 |
| Fractional placement times | 14 |
| `00` position entries | 93 |
| `索` flag | 3 |

Conditions were counted as non-default when at least one field was neither empty,
`NULL`, nor numeric zero. These are syntax counts, not execution of the tracks.

## Existing capabilities to preserve

FvmAssistant already implements single-stage repetition, navigation and deck selection,
timed placements, a priority queue with `绝` bypass, support dependencies, bounded
retries, drop collection, boss continuation, and recording-to-track conversion.
Ordinary layer-3 cards already use held-card foreground color learning and repeated
absence observations. See [automation behavior](automation.md).

There are deliberate differences from upstream: queue heads can be skipped after a
timeout, placement success is checked through the CEF capture path, and support
attempts are not treated as guaranteed success. Preserve these choices explicitly
when extending compatibility.

## Features worth integrating

Effort is relative to this prototype: small means a localized change; medium means
several cooperating components; large means a new subsystem and substantial manual
verification. It is not a delivery estimate.

| Priority | Feature and benefit | Upstream evidence | Suggested integration and effort |
| --- | --- | --- | --- |
| P0 | Compatibility report before starting: identify unsupported semantics before consuming a run | Executor `LoadSingleCard` at 15248 and `CheckList` at 30619 validate inputs and dependencies | Add semantic validation after `TrackFile` parsing and support resolution; show diagnostics in the panel and API. Small to medium for a first version |
| P1 | Flame recognition and basic conditions: reserve flames for expensive cards, delay actions, stop after a count/time | Executor `GetEnergyDigit` at 10850, `IsConditionMet` at 17166, `GetPlantableTick` at 17255 | Add a small digit recognizer and typed conditions; evaluate them before scheduling an action. Medium |
| P1 | Correct timing and inheritance: preserve the intended ordering of existing tracks | Executor `LoadSingleCard` at 15449, opening `继` branch at 15822, `CheckNormalSlot` at 17099 | Separate formation cells from timed events; support deciseconds and independent/synchronized clocks. Medium to large |
| P1 | Wave changes: support early production formations and later attack formations | Executor `GetWave` at 14684, `ReloadWave` at 15946, `CheckPlant` at 17505 | Recognize major waves first, then add minor-wave progress. Switch plans while retaining the correct board and inherited state. Large |
| P1 | Single-account task queue: run several stage/track/deck combinations in sequence | Executor `Advance` at 164, `StartQuest` at 30954, `FinishAdvance` at 31171 | Add a coordinator above `GrindController`, with per-task counts and explicit retry/skip/stop outcomes. Medium |
| P2 | Formation/timeline editor: inspect generated tracks and correct temporary placements or shovels | Editor `ViewTrack` at 4709, `EditMap` at 7554, `EditTrack` at 8246, `SaveBackup` at 3600, `Undo` at 3651 | Start with a 7×9 layered preview and editable action list, then drag/drop and undo. Medium to large |
| P2 | Recognition inspector and template calibration: explain why a cell is waiting or being repaired | Executor `PrintCombatInfo` at 14834; screenshot tool `AdjustSlotParam` at 1780 and `CatchSlotTemplate` at 1857 | Show per-layer occupancy, evidence scores, pending support, and retry reasons in a UI overlay; add optional per-card calibration. Medium |
| P2 | Production/cooldown preflight and automatic scheduling: detect a plan that spends flames too early | Editor `CountEnergyNormal` at 790, `IsCold` at 801, `AutoTrack` at 1212, `ConstrainedAutoTrack` at 6019 | Add an offline estimator using explicit account/card metadata, then suggest schedules. Large; results are estimates |
| P2 | Run and loot statistics: compare stage yield and find expensive failure patterns | Executor `State` at 1300, `GetLootCodeAndQuantity` at 12757, `SaveLoot` at 13250 | Start with local duration/result/retry summaries; add item recognition and quantity aggregation later. Small to medium for summaries, larger for loot recognition |
| P3 | Image-triggered placement, targeting, and follow actions: react to a recognized target | Executor `IsConditionMet` at 17166, `GetHunterLocation` at 17200, `CheckFollowSlot` at 17442 | Requires stable target templates, locations, counts, and action confirmation. Large |
| P3 | Moving-board support: follow physical cell movement in relevant stages | Executor `GetGridLocation` at 15130 and `TranslateMobile` at 16688 | Introduce frame-dependent cell transforms and route both recognition and input through them. Large |
| P3 | Two-account cooperation and additional stage families | Executor `Advance` at 164, `GetCombatInfo` at 16852, `EnterTowerLevel` at 28459 | Requires independent browser contexts, room coordination, start barriers, and recovery for either player. Large |

## Compatibility gaps that should be addressed first

### Parsed conditions currently do not constrain execution

[TrackFile.cs](../src/FvmAssistant/Automation/TrackFile.cs) stores `Conditions` as text.
[BattleRunner.cs](../src/FvmAssistant/Automation/BattleRunner.cs) does not evaluate it.
For example, the actual track syntax `7[3]补极,0,0,200` includes a flame threshold
of 200. The current scheduler can select that card whenever its slot is ready and
its other implemented rules allow it, without enforcing that threshold.

The upstream condition structure has 13 fields: delay, trigger mode, flames, wave,
card name/count, image name/count, target row/column expressions, follow card,
termination count, and termination time. Implementing only the flame field must
not make the other fields appear supported. The upstream conditional path is tied
to `极`; compatibility checks should distinguish effective conditions from inert
fields and preserve the source's delay/trigger behavior deliberately.

An initial validator should classify supported, partially supported, unsupported,
and malformed inputs. Unsupported behavior-changing features should prevent an
unqualified normal start; any deliberate partial execution should clearly identify
what will be omitted. The panel and API should use the same validation result.

### Time and formation data need separate representations

Upstream accepts fractional seconds and `00` positions, and handles the last timed
entry of an `极` row as the transition into limit placement. See `LoadSingleCard`
at 15449–15504 and `GetPlantableTick` at 17269.

The current parser only accepts integer seconds and represents every entry as a
`TrackStep` containing both a time and a `Cell`. Consequently:

- A fractional entry such as `035.5|49` becomes a warning and is omitted.
- `035|00` passes the parser but is not a playable cell. If it reaches
  [BattleLayout.CellCenter](../src/FvmAssistant/Automation/BattleLayout.cs), it throws
  an out-of-range exception. Upstream treats zero positions separately from real
  formation cells; they must not be passed to the current geometry methods.
- `补` plans currently begin from the first step time and choose from the formation;
  they do not reproduce an upstream timed prefix followed by conditional filling.
- `独` is exposed as a property, but the current executor uses one elapsed battle
  clock. Upstream synchronized timing adds accumulated placement overhead, while
  independent timing does not.

Use separate formation and event collections with integer decisecond or millisecond
timestamps. Preserve event completion and late-rejection recovery from
[PlacementSchedule.cs](../src/FvmAssistant/Automation/PlacementSchedule.cs). Do not
reuse upstream repeated-click timing: CEF input needs its own measured timings.

### Resolve effective tracks before offering card mappings

[CarrierMap.Resolve](../src/FvmAssistant/Automation/CarrierMap.cs) imports a default
support schedule only for an empty opening `继` row. Upstream can also retain an
explicit formation while importing the default timing and conditions. That needs
the separate representations described above.

There is also a panel integration gap: [GrindPanel.cs](../src/FvmAssistant/Panel/GrindPanel.cs)
builds its card mapping rows from opening cards with `Steps.Count > 0` at line 316.
An empty support row that becomes usable through inheritance therefore has no
mapping row in that panel. Resolve support defaults before deciding which cards
need mappings. When wave execution is added, derive the required deck from all
effective waves, including cards unused in the opening.

### Wave execution needs more than a wave-number detector

The parser preserves later sections, but the runner constructs plans from
`track.Opening` only. The panel and log already warn about this limitation.

Upstream preserves inherited slot state and resets non-inherited slot clocks to
the new wave start (`LoadSingleCard` at 15271). Its `CheckPlant` also chooses a
relevant track section when recognition skips intermediate wave observations.
Implement these rules explicitly, including what happens to pending support repairs,
queue progress, card ownership, and conditions at a transition. A stable major-wave
detector is a useful first milestone; minor waves derived from the progress bar
require additional validation.

Other stored headers, including `退出时间`, `放卡限速`, `移动板块`, and
`刷技能模式`, also need a capability decision rather than silent acceptance.
`人物2位置` is now an explicit non-blocking warning in solo rooms; the runner
uses `人物1位置` for its character.
`宝石` is excluded from deck cards and has no dedicated execution path today.
Zero/default values and editor-only metadata should not be reported as active
unsupported behavior merely because a field is present.

## Suggested implementation boundaries

Keep [IGameIo](../src/FvmAssistant/Automation/GameIo.cs), `StageFrame`, and CEF input
as the platform boundary. New components can be ordinary C# classes within the
existing .NET Framework 4.8 project:

1. A track resolver/validator produces an effective deck, formations, typed
   conditions, timed actions, and precise diagnostics before the game is touched.
2. A battle observer produces timestamped flame/wave/occupancy observations with
   an explicit unknown state. A failed digit read must not become a confident zero
   or use stale evidence indefinitely.
3. The scheduler consumes observations and proposes one action; the runner keeps
   ownership of CEF input, placement confirmation, cancellation, and recovery.
4. A task coordinator starts one grind at a time and persists only task settings
   and completion summaries under `%LOCALAPPDATA%\FvmAssistant\`.
5. The panel presents the same effective track and observations used by execution,
   so its preview, mapping choices, and status cannot silently disagree.

For queue recovery, borrow upstream's retry/skip/stop distinction, not its full
task system. Current refresh handling already exists. Reliable defeat recognition
would be an additional FvmAssistant feature requiring its own evidence; the upstream
end-state helpers alone do not establish an explicit defeat detector. Resume a
task at a confirmed room/lobby boundary, not halfway through an unverified battle.

For recognition, add calibrated support/shield masks and temporal evidence where
needed. Upstream's `LoadMaltoseCoverage` at 14910 intersects support coverage masks;
its scores are raw pixel/color counts and are not interchangeable with FvmAssistant's
normalized signature scores. Do not adopt the upstream assumption that three
support placement attempts prove occupancy. Moving backgrounds and physically
moving cells are also distinct problems; current background handling does not
implement the latter.

## Scope to defer

The simulator's `mode` at line 9 selects crafting or upgrading; `Upgrade` at 1348
implements a local probability model. Integrating it would not provide battle
simulation or validate a formation's ability to win. Resource estimation and
automatic track scheduling belong to the editor functions listed above. Their
accuracy depends on card star levels, production, collection, and special-card
effects that the current learned-icon library does not model.

Defer inventory cleanup, actual upgrading, purchases, and mail/trade operations.
They expand the product beyond repeated stages and introduce resource-changing
actions with separate configuration and verification requirements. Defer the
upstream telemetry/update infrastructure, native window binding/input, and broad
multi-account task machinery as well. None is required to gain track compatibility.

Keep `sample/` and `vendor/` local and excluded from builds and staging. Implement
the required behavior in the existing codebase without copying third-party source
or embedding its assets into application source. New learned templates and run
statistics belong in the private runtime directory; exported reports require the
review described in [security.md](security.md).

## Proposed delivery and verification order

1. Compatibility diagnostics, including conditions, fractional times, `00`, timing
   flags, later waves, special actions, and effective support/deck mapping.
2. Flame observations and a documented subset of typed conditions, followed by
   timing/inheritance semantics and major-wave transitions.
3. A single-account task queue, local run summaries, and an editable formation
   preview. This queue can proceed independently of advanced battle recognition.
4. Minor waves, template calibration, production estimation, loot recognition,
   image targeting, moving cells, and two-account support as separate increments.

For future code changes, add focused synthetic regressions for semantics and
state transitions through the existing fake `IGameIo` harness, then run `make check`.
Cases should include an unreadable flame total, delayed conditions, inherited
versus reset clocks, a skipped observed wave, zero-position markers, and a queue
item failing after completed rounds. Keep fixtures source-owned.

Capture/input changes additionally require the [local Flash-page procedure](prototype-findings.md#local-input-and-capture-test).
Any claim about game success requires an explicit live manual test, independently
of build or synthetic test results.

This review used source inspection and a read-only preset syntax census. No
third-party executable, runtime profile, account log, or live game was opened.
Documentation verification consists of `make secrets` and checking referenced
local files, function locations, and documented commands. Application build and
gameplay tests are not part of this documentation-only review.

## Implementation follow-up

The first implementation adds `TrackPreflight`, shared by the panel, the read-only
`/track` API, the start controller, and the grind loop. It resolves default support
before presenting deck mappings, rejects unsupported execution requirements before
browser/card access, preserves source locations for diagnostics, and executes the
same loaded snapshot that passed validation. The preview is refreshed even when
the path stays the same, so a stage change or an edited file cannot reuse stale
support information. Newly generated shovel rows explicitly use elapsed-time `独`.

This implements the compatibility and effective-deck work, not flame recognition,
new timing modes, wave transitions, or a task queue. See the exact supported subset
and API contract in [automation behavior](automation.md#track-compatibility-checks).

Verification: sensitive-data scanning and scanner tests passed; Visual Studio
MSBuild produced a Debug build and all 20 synthetic automation regressions passed,
including eight new preflight/export groups. `make check` initially could not copy
the executable because the existing FvmAssistant process held the default Debug output
open. The same project was then built with
`-p:Configuration=Debug -p:Platform=x64 -p:OutputPath=bin\x64\PreflightCheck\`, and
the regression executable was compiled against and run beside that fresh build.
The running game instance was left active. No live gameplay or manual panel test
was performed; capture and CEF input implementations were not changed.

## Basic condition follow-up

The next increment parses the 13 condition fields into typed basic values and
retains unsupported fields for preflight diagnostics. Active `极` cards now support
delay, trigger mode, a minimum flame threshold, termination count, and termination
time. Flame digits are read from the locally installed track-editor masks at run
time; the third-party images are not copied into the application. A missing mask
set blocks a flame-dependent start. Each battle frame produces a fresh reading or
an explicit unknown result; a frozen frame is invalidated after five seconds.
Only confirmed placements increase the termination
count, and the existing late-rejection recovery reduces it again. Unsupported
wave, card/image, targeting, and follow conditions remain blocked.

The reader uses the current card-bar position, including the shift at 13 or more
slots. It first tries the installed masks, then a conservative stroke classifier
for the wider live Flash font. Synthetic regressions cover zero versus unreadable
digits, both font layouts, several counter values, maintenance and trigger delays,
termination limits, and preflight behavior. `make check` passed with 22 automation
regressions. In the first live attempt, the original masks could not read the
counter, so the flame-gated card never placed; the run was stopped. A tightly
cropped live counter was then read offline as 3531 with the corrected reader. The
diagnostic crop was deleted after inspection. On 2026-09-26, a full one-round
live rerun on 曲奇 completed after a page refresh. The temporary track's producer
card and its card gated at 200 flames both had confirmed placements; the loop
returned to the room with one round completed. The counter was briefly
unreadable, so this confirms fail-safe recovery and a successful gated placement
but not uninterrupted recognition or the other condition modes. Before the
refresh, room card-list loading timed out and a manual-slot attempt could not
leave the room. The manual panel test remains pending.

## Cooldown-card and completion-navigation follow-up

The current source adds the editor-only `x` lock marker, a positive count of a
selected layer-3 card as an active condition, and explicit layer-5 placements on
occupied formation cells. Layer-5 actions do not claim lasting board occupancy.
The final completed round now leaves the room through the world map and selects
美味镇. The town point follows the pinned executor's `EnterTown` route; the local
screen detector checks each transition.

The working-tree edits passed `make check` on 2026-09-28: the sensitive-data scan,
scanner tests, Debug build, and 25 synthetic automation regressions. The rebuilt
application's read-only `/track` preflight accepted 火山/炭烧日 with only the solo
second-player warning. No live battle or return-to-town result has been verified.
At that point, the pending compatibility gaps were later waves, non-shovel layer 0, layers 4
and 6, the editor's `冰沙` fallback cells, image and targeting conditions,
fractional timing, and zero-position markers. Preflight blocked those
requirements.

## Fractional timing follow-up

The executor now preserves track times at the editor's 0.1-second resolution.
The parser accepts `ttt.t|xy` with one decimal digit, and the same value reaches
timed placement, support activation, shovel actions, track preview, and generated
track serialization. Preflight rejects nonzero times below one second, values
above 960 seconds, and extra decimal digits. Locally resolved support schedules
are checked with the same limits. This removes the fractional-timing gap in the
earlier review; `00` position markers and scheduled `补` remain unsupported.
The synthetic regressions cover parser round-tripping, deadline boundaries for
both timing modes, support inheritance, and invalid values. No live gameplay
result has been verified for fractional timing.

## Scheduled fill and task queue follow-up

The executor now accepts positive-time `补` events without `极`, including `00`
entries. Playable cells form the placement set; a `00` event triggers a sweep but
is never sent to board geometry. Each event is consumed after one confirmed
placement or when the formation has no available target, and refused placements
remain retryable. This follows the editor's separation of timing entries from
formation cells while retaining FvmAssistant's one-click confirmation model. Synthetic
regressions cover placement and a full formation. Live behavior is still pending.

For task sequencing, the loopback coordinator `scripts/queue.ps1` validates each
track before starting any task, then runs stages in order and stops on the first
non-success outcome. It deliberately requires an explicit stage and does not
retry or skip a failed run from an uncertain game screen. This is the useful
single-account queue subset of the editor's broader task system; no live queue
run has been verified.

## Formation preview follow-up

The operation panel now opens a read-only 7×9 board and a time-sorted action
list from the same loaded track snapshot used by preflight. It distinguishes
playable formation cells from `00` events, shows all stored waves for inspection,
and grays out unmapped opening cards. Later waves remain preview-only until the
battle observer can identify their transitions reliably. The dialog has compiled
but has not had visual or live-game verification.

## Editable track copy and timing follow-up

The preview now has a companion step editor for every stored wave. It changes
selected headers, card specifications, and time/cell fields in memory, supports reordering and
undo/redo, previews the edited snapshot, and
reports compatibility before saving a new UTF-8 copy under the application's
private edited-tracks directory. It preserves untouched source lines and does not
replace imported presets. This supplies the useful correction workflow for
recorded tracks, though drag-and-drop board editing and later-wave execution are
still absent.

Opening `继` support cards now import the matching default timing, independent and
extreme flags, and conditions while retaining explicit formation cells. A missing
matching layout or an inherited timed `00` without scheduled `补` blocks Start.
`放卡限速` accepts the editor's 0–5 range and caps card attempts when positive.
Synthetic regressions cover the merge, invalid inherited cells, rate boundaries,
step editing, undo/redo, and copy saving. The Debug build and regressions pass.
The editor layout and interaction, inherited support timing, and rate limiting
have not been individually verified in live gameplay. Later manual solo and
two-account 曲奇 tests are recorded in [prototype findings](prototype-findings.md); they do not
exercise every editor or track-syntax feature.
