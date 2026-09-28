# Track executor analysis: cloud stages

Initially reviewed on 2026-09-26. The analysis below records the pre-fix behavior. A subsequent implementation addresses the identified failure paths; see [Implementation follow-up](#implementation-follow-up). Neither source review nor synthetic regression checks establish the cause or outcome of a particular live run.

## Source availability and binary inspection

The [official manual](https://mstrack.cn/Track-Web/guide/) links to the author's public [FvmTrack repository](https://github.com/YunTaoXiaoWu/FvmTrack). Its execution logic is available as C++ source, including names and comments. Decompilation is unnecessary for this review.

The reviewed revision is `f651e905fb935a222c870c687adbebfbfe57d353`, whose commit message and `track.h` identify v6.7.2. The local installation archive is also labeled v6.7.2. Matching version labels do not prove that this source reproduces the local executables byte for byte.

The installer was inspected and unpacked as an archive without running it. The package contains separate editor, simulator, executor, and screenshot-tool executables, plus a PDF manual. Both the editor and executor have PE32 x86 headers, linker version 11.0, and zero CLR-directory RVA/size. No PDB files were found in the package listing. These observations fit the published native C++ projects and their `v110` toolset settings.

If a future binary has no corresponding source, a native decompiler such as [Ghidra](https://github.com/NationalSecurityAgency/ghidra) can help recover control flow and C-like pseudocode. It does not promise the original comments, local variable names, project structure, or a directly rebuildable source tree. The [Microsoft PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) documents the headers used for this inspection. A .NET decompiler is not the appropriate first tool for these executables.

Package extracts and the initial upstream checkout were kept in a separate temporary analysis directory outside this repository. At the user's subsequent request, the author repository was also cloned to ignored `sample/FvmTrack`, at the same revision. No third-party implementation or data was copied into application source, and no runtime profiles, screenshots, or account logs were used.

## What the executor actually does

The references below point to the reviewed revision of `轨道执行器/轨道执行器.cpp`.

| Mechanism | Source evidence | Implication for FvmAssistant |
| --- | --- | --- |
| Learn the held card's appearance | [`CatchTemplate`, line 14081](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14081) captures the card at a designated position and removes pixels matching a previously captured background. It supports custom templates and adjusts some card shapes. | Room-deck icons and battlefield sprites serve different purposes. A battlefield template can identify a card even when the ground moves. |
| Recognize card presence | [`GetSimilarityXY`, line 14348](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14348) counts distinct matching template colors in a cell region. [`GetSimilarity`, line 14430](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14430) aggregates the maximum score over ten observations, with a nominal 100 ms recognition interval. | Recognition does not require another empty cell in the same column. Temporal aggregation helps retain evidence through animation and brief occlusion. Actual sampling can be slower when execution blocks. |
| Keep occupancy by layer | [`GetOccupied`, line 14402](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14402) records recognized cards by layer. [`SmartMovePlant`, line 16901](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L16901) visits formation positions whose relevant layer is unoccupied. | A support, a shield, and an ordinary card should have separate observations; one changed cell image cannot reliably represent all three. |
| Special handling for 棉花糖 / 麦芽糖 | [`LoadMaltoseCoverage`, line 14910](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14910) intersects five coverage masks. [`GetInnateGridSimilarity`, line 14307](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14307) counts unchanged opening-map pixels only inside that intersection. | Support recognition needs a dedicated region and state. The upstream threshold of 20 is a raw matching-pixel count, not a percentage or FvmAssistant's mean color difference. |
| Remember support attempts | The cloud branch of [`GetOccupied`](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L14409) combines the masked score with a per-cell placement counter. After three attempts it assumes support is present. | This is a bounded heuristic, not proof of successful placement. FvmAssistant should retain confirmation and recovery rather than adopting the three-attempt assumption unchanged. |
| Load complete default support tracks | [`LoadDefaultTrackFrom`, line 15566](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L15566) loads full card schedules. [`IsSameSlot`, line 15665](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L15665) treats 棉花糖 and 麦芽糖 as equivalent. The [opening-wave `继` branch, line 15822](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L15822) imports defaults when enabled, preserving a supplied formation while taking the default schedule. | Default support behavior is opt-in track semantics. An empty support row alone is not the complete upstream rule. Do not unconditionally merge all default events into an explicit track. |
| Separate timing and readiness | [`CheckNormalSlot`, line 17099](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L17099) distinguishes independent and synchronized timing. [`CheckLimitPlant`, line 17345](https://github.com/YunTaoXiaoWu/FvmTrack/blob/f651e905fb935a222c870c687adbebfbfe57d353/轨道执行器/轨道执行器.cpp#L17345) requires support slots to have been available for at least one second on its limit/condition path. | A lit slot is not always sufficient evidence to place support immediately. The one-second guard is not applied universally to every timed placement. |

The executor also repeats ordinary pick/place/release sequences. Those timings and its window-message input implementation should not be transferred mechanically to FvmAssistant's CEF input. The useful reference is the recognition and scheduling design.

## Concrete gaps in the pre-fix implementation

### 1. The final empty cell can lose its comparison reference

In [BattleRunner.cs](../src/FvmAssistant/Automation/BattleRunner.cs), `LooksEmpty` first compares the cell to the opening frame, then calls `LikeAnEmptyNeighbour` for an animated column. The latter only considers other cells that have no remembered owned card.

Once six cells in a column are remembered as occupied, the seventh has no eligible comparison neighbor. If its animated background differs from the opening frame, this method cannot recognize it as empty. Separately, `BackgroundMoves` needs at least four unowned cells to establish that a column moves, so late detection can also become impossible.

`FormationTargets` can still probe an unknown cell, but it adds that cell to `Plan.Probed` before the attempt, and that set is never expired or cleared during the round. If the one probe fails during a transient obstruction, the cell can remain untried while it continues to look unknown. Expiring `BusyUntil` does not remove this exclusion.

This is a credible code path for persistent late-formation gaps. A fresh live trace is still needed to establish whether it caused the reported gaps.

### 2. Timed-only support maps disappear during lookup

[CarrierMap.cs](../src/FvmAssistant/Automation/CarrierMap.cs) selects only a layer-1 card with at least one time-zero step and stores only its time-zero cells. The local 棉花日 and 棉花夜 support layouts contain timed cloud events without time-zero support cells, so `CarrierMap.Find` returns null for both.

A probe using the actual current parser and lookup sources reproduced:

| Selected stage | Lookup result |
| --- | --- |
| 火山 / 棉花日 | null |
| 火山 / 棉花夜 | null |
| 火山 / 城堡 | non-null, 54 fixed cells |

This prevents those cloud schedules from supplying automatic support. It does not disable an explicitly written schedule: the current general 棉花日 preset parses six independent 麦芽糖 steps with zero warnings. Therefore this lookup gap alone does not explain missing ordinary cards in a run that already executed all six explicit supports.

### 3. A timed support can be forgotten permanently

In `PlaceAsync`, a refused timed support is retried only until its scheduled time plus six seconds; another failure then advances `Plan.Next`. This deadline is measured from the scheduled time, not from the first attempt, so a late first attempt may have little or no retry allowance.

A second path exists after apparent success: `Plan.Next` advances immediately, but `ForgetLatePlacement` only removes ownership and decrements placement counts when a late rejection is noticed. It does not restore the consumed timed step. A non-fill support plan therefore has no ordinary mechanism to retry that lost event.

These paths require separate recovery from replenishing an ordinary `补` formation.

### 4. Parsed flags do not imply complete execution semantics

[TrackFile.cs](../src/FvmAssistant/Automation/TrackFile.cs) exposes `Independent` and `Inherit`. The battle runner uses a single elapsed-time clock and does not implement the upstream opening-wave `继` default-import behavior. Flame conditions are also not executed, as already documented in [automation.md](automation.md).

These compatibility differences should be explicit when borrowing a preset or comparing outcomes with the original executor.

## Suggested implementation order

1. Address persistent unknown cells: replace the lifetime probe exclusion with a bounded retry policy, and make stale ownership re-evaluable. Record a compact per-cell reason for waiting so an unattempted cell can be distinguished from a rejected placement.
2. Represent fixed support requirements and timed support events separately. Preserve explicit track schedules; activate defaults through documented settings or the opening-wave `继` semantics. Track event status so a late rejection can return an event to pending without replaying completed events.
3. Add battlefield-card templates and temporal presence scoring through the existing off-screen capture and CEF input interfaces. Learn actual mapped cards, including transformed skins, and keep support recognition separate from ordinary-card recognition. Calibrate scores against this capture pipeline.
4. Add a readiness stability check for support placement and evaluate it alongside event deadlines. Preserve successful-placement evidence rather than interpreting attempt counts as guaranteed occupancy.

Validation should cover a column with one remaining gap, temporary effects during its first probe, a vanished card on a fully occupied column, delayed cloud opening, late support rejection, and an explicit support schedule coexisting with available defaults. Recognition/input changes also require the local test-page checks in [prototype-findings.md](prototype-findings.md), followed by explicit live-game testing on cloud and static stages.

## Initial review verification and limits

- Read the project documentation, current automation code, local support/preset files, and the relevant upstream execution functions.
- Inspected package contents and executable headers without launching third-party programs.
- Compiled four existing parser/lookup source files into a temporary PowerShell process and reproduced the lookup results above. This was a focused diagnostic probe, not a build of FvmAssistant.
- Ran `make secrets` successfully (55 text files scanned), checked local document links, and checked upstream source anchors against the pinned checkout.
- During the initial review, no application code, third-party files in `vendor/` or `sample/`, or runtime account data were changed. No live game was run.

## Implementation follow-up

The repair adds a per-event placement ledger, timed-only support lookup, default import for empty opening `继` rows, expiring and fair unknown-cell probes, periodic ownership checks, held-card foreground color learning, and a support readiness guard. It preserves explicit support steps and the off-screen capture / CEF input interfaces. At the time of that repair, full upstream synchronized timing, default timing over an explicit formation, and coverage-mask-based support recognition remained outside its scope.

## Synchronized timing follow-up

The runner now tracks the duration of ordinary timed placements and timed shovel actions without `独`. It subtracts that accumulated duration from the battle clock for synchronized rows. Independent rows retain elapsed battle time, and `极` placements do not add to the synchronized offset. The start marker of an `极` row still uses its selected clock. This follows the distinction in the upstream executor's `CheckNormalSlot` and `GetPlantableTick` paths without copying its input method or fixed input delay. After the user finished the existing run, `make check` passed: the Debug build, sensitive-data checks, and all 23 automation regressions, including the new timing case. Live gameplay with synchronized timing has not been verified.

The deterministic tests in `tests/AutomationTests/RegressionTests.cs` exercise the actual automation classes using generated images, temporary track files, and a fake `IGameIo`. They cover delayed and late-rejected timed events, event supersession, final empty cells, probe fairness, transient occlusion, disappearance between adjacent identical cards, unverified ownership expiry, held-card learning, and timed support dependencies. They do not use third-party fixtures or login data.

The Debug build, sensitive-data checks, scanner tests, and all 12 automation regressions passed through `make check`. Automatic approval review initially rejected launching the dedicated local test instance, returning only `blocked by policy`. After the user launched it with `--mode osr`, the [local Flash-page check](prototype-findings.md#local-input-and-capture-test) passed: cross-origin stage location was exact, captures kept updating in normal and minimized windows, and CEF hover and clicks worked in both states without reported plugin crashes or render terminations. Cloud-stage gameplay remains unverified for this implementation.
