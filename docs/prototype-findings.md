# FvmAssistant prototype findings

These records retain technical verification outcomes; account identifiers,
server choices, balances, progression values, and unrelated activity ledgers
are omitted. Raw evidence remains private.

The prototype, originally named FvmSpike, was built to answer three questions: can an embedded Flash browser enter the 4399 game, can it capture moving game frames while hidden or minimized, and can it click the game without moving the real pointer? Tests on 2026-09-25 answered yes for off-screen rendering plus CEF-injected mouse events. The application is now named FvmAssistant.

## 2026-10-08 attended main-account tasks and activity rewards

An attended single-account check verified guild publication and guarded reward
claims from fresh selected-pane evidence.

An attended solo 美味/深渊 run completed normal combat and boss continuation,
reporting one completed normal round and one boss round. The festival board
confirmed its SS clear requirement. The online-duration and sign-in conditions
also showed complete; the daily challenge bundle displayed success and 已领取.

An attended solo 火山/港口 run completed one normal round with no separate
boss continuation. The 每天游港口 mission remained in progress, so the normal
clear was not treated as completion of its three-boss objective. Neither run
required coupon purchases or item deletion.

Five attended solo 美味/咖喱夜 normal clears completed with no boss
continuation. The first settlement showed A. The 扰民的垃圾车 guild objective
reached 5/5, and its guarded reward claim displayed 已领取. The session total
was seven completed normal rounds and one completed boss round. The main
profile was left idle in the town with no renderer or plugin termination
reported by the running instance.

Mission observations now include read-only `CompletedRewardRows` candidates
from the visible green completion badges. Scrolling, selection, and reward
claims still require attended review and fresh guards. Synthetic regressions
cover shifted badges, unrelated colored marks, small green noise, and an empty
board. Offline checks against three private game captures returned one pending
row before each claim and none after collection. The restarted Release API
exposed the field. `make check` passed with 58 automation regressions.

Live inspection of the completed garbage-truck task returned row 205 before
its reward claim and an empty candidate list after the verified claim. The
selected task and its 5/5 objective were reviewed before collection.

The previous pointer-parking change in card learning was verified live in a
port room: learning the selected 日光炉, 红柳烤串机, and 瓜皮 while initially
hovering over a deck slot succeeded, and ensuring the same deck required no
selection changes. This verifies the learning path for those cards; 油灯 was
not available in the inspected usable card list.

## 2026-10-07 attended missions and navigation repair

An attended single-account check used OSR captures and CEF input to review
mission publication and guarded reward claims.

One 美味/可可日 round completed normal and boss stages and their rewards, with both round
counters at one. The 融化冰渣 pane independently showed its boss objective at 1/1;
its reward was claimed and verified 已领取. The run then failed while returning
to town, with a private timeout capture still showing the 美味 island map.
A fresh CEF click at the old world-map position `(866,41)` left the map unchanged;
clicking its label at `(866,58)` immediately opened the world map. Navigation now
uses that label. Returning after rewards also allows at most three clicks based
on fresh lobby observations within the existing transition limit; unknown screens, disconnects and
popups do not authorize further clicks. Synthetic checks cover a lost first
click, the retry bound and rejection of those intervening screens.

One 火山/果酱夜 normal round completed its reward flow and returned to town, but
the SS通关果酱部落 objective remained at 0/1. A subsequent round completed both
normal and boss stages and returned to town with both counters at one. The
selected task pane then independently confirmed the SS-or-higher objective at
1/1; its reward was claimed and verified 已领取. This establishes the task result
after the boss round; the first normal victory did not satisfy its rating requirement.

One 火山/城堡 round completed normal and boss stages on the rebuilt Release instance,
with both counters at one and a finished state in town. This live return also
verified the corrected world-map label position. The 佳节打卡 board independently
showed its A+ castle, online for 30 minutes and daily sign-in objectives complete;
claiming displayed 领取奖励成功 and 已领取. The daily free-wish control
disabled after a successful claim.

One 火山/薄荷夜 normal round completed and returned to town. The selected
穿游薄荷海滩 pane independently showed 1/1 for its objective to win with at least
600 flames remaining. Its reward was claimed and verified 已领取. The event
board separately confirmed a completed timed-boss objective, and a guarded
claim returned success.

The new task-board open actions were checked in a restarted Release instance:
`/missions?state=open` opened the account board from town;
`/guild-tasks?state=open` switched from that board to the guild board; and the
account route returned through the guild hall to the account board again.
Each action returned fresh board evidence. No task was selected, no reward was
claimed and no publication draw was made by these navigation checks. Synthetic
regressions also verify that an already open board sends no input and that a
failed transition stops before the next click.

`make check` passed with 53 automation regressions, scanner tests and area-menu
checks. Debug and Release MSBuild builds passed. An isolated local Flash check
located the 950-by-600 cross-origin embed at `(59,87)` without a guessed offset.
Four captures spaced 230 ms apart were distinct both normally and while
minimized. Normal hover changed the button from gray to light gray, and a click
changed its indicator from red to green. The first click after minimizing did
not advance the indicator; after a fresh hover observation, the next click
changed it from green to blue. No plugin crash or renderer termination was
reported. The isolated app and local server were stopped. These local input
checks do not establish live combat outcomes.

## 2026-10-02 attended missions and activity checks

The attended check used OSR captures and CEF input. Reward claims were reviewed
against fresh selected-pane evidence.

The enabled gold 发布任务 button contained exactly 100 pixels satisfying the old
orange test, which required more than 100. The action therefore returned
unavailable although the task list said no leader tasks had been published.
An attended CEF click opened the publication dialog; the existing one-draw
action then published tasks and the publication button became disabled. The
button check now includes its gold gradient in the same bounded region; gray buttons and unrelated gold pixels
remain rejected by deterministic checks.

One 美味/可可日 normal-and-boss round completed and returned to town with both
round counters at one. The 融化冰渣 pane independently showed its boss objective
complete, and its reward was claimed and verified 已领取. Bringing the learned
seven-star 小笼包 did not satisfy 小笼包逆袭可可岛, whose pane requires 双层小笼包;
that task remained in progress. A 火山/城堡 normal round also completed and
returned to town with an A rating, which did not meet the event's A+ objective.
A subsequent normal-and-boss castle round completed, returned to town with both
counters at one, and its private treasure capture showed SS. The event independently
showed all three daily objectives complete after the free daily sign-in. Claiming
then displayed 领取奖励成功 and 已领取.
An event reward claim displayed success and disabled the eligible free-reward
control. Paid activation and reward tiers were not selected.

Two 港口 normal rounds completed without a boss continuation being observed.
每天游港口 remained in progress and no task reward was claimed. The third repeat
had already started when the run was stopped; that opening battle was exited and
was not counted as a completed round. The absence of a boss continuation does not
establish whether keys, timing, or another game condition caused it. Further port
attempts were stopped rather than treating normal wins as proof of the boss objective.

During boss continuation, the progress endpoint retained VictoryDialog although
a private capture showed a live battle. Transition polling now updates LastScreen
from its existing observations before the runner resumes. The new attended
`/missions?state=select` action guards the observed list, returns fresh selected
evidence, and allows a completed row to be selected while blocking unrelated
rows when rewards remain pending. It sends no reward click; selecting an eligible
row can still cause the game to accept it automatically.

Replaying the private enabled and disabled publication captures against the new
Release evidence method returned the expected true and false values. This verifies
the repaired classification on observed pixels without making a second publication.

The lack of a safe stop boundary also exposed unnecessary repeat rounds while
checking task progress. A new solo Finish current round control completes the
configured boss and free reward flow, returns to town, and reports finished-early
when the original round target was not reached. Synthetic full-loop checks cover
normal and boss endings, one free flip, no second battle, repeated finish requests,
and rejection for paired runs.

The sensitive-data scan, scanner tests, area-menu regressions, Release MSBuild
build, and 52 automation regressions against that new Release binary passed.
The ongoing game session uses the prior Debug executable. These checks do not
establish live verification of the repaired publication check, selection route,
transition reporting, or the finish control in a restarted instance.

## 2026-10-01 inventory and formation editor checks

The inventory learner and visual track editor passed synthetic regression checks for grid calibration, reviewed-frame saving, stale captures, duplicate names, exact-time cell erasing, character headers, card-count updates, and undo. The controller, inventory scanner, and formation editor were rendered with synthetic card icons for layout inspection. Scanning controls and all seven board rows fit the controller's default window; the scanner and editor were also inspected at their minimum sizes. QA output remains in the ignored runtime directory.

An attended live check followed server maintenance. The defense-card page had nine columns, six visible rows, and a 49-by-57-pixel pitch. Calibrating the native origin at (470,92) matched eight existing icons; a one-pixel error in each axis matched none. The scanner saved a reviewed seven-star 小笼包 icon, and room deck building successfully selected it with 日光炉, 红柳烤串机 and 棉花糖. This verifies learning from that inventory page and using its crop in room selection. Inventory paging still requires separate verification.

The attended run also exposed undersized thumbnails in the controller's compact formation preview; thumbnails now use the cell height while keeping cell labels visible. The scanner can align a slightly inaccurate origin using multiple distinct known icons, with bounded offsets and ambiguity rejection covered by synthetic regressions. During a 雪顶火山 run, the game disconnected before victory; automatic re-entry stopped when it could not find the learned 红柳烤串机 icon. Manual scrolling later exposed the matching icon and room deck building succeeded. Scroll observation now tolerates delayed frames, with a synthetic regression for delayed changes and a bounded wait at the actual list boundary. This does not establish the cause of the game-server disconnect or a successful 雪顶火山 clear.

A second attended inventory capture tested the updated alignment from an intentionally inaccurate (471,93) origin. Scanning corrected both fields to (470,92), displayed the -1,-1 offset, and recognized the learned 日光炉 and 小笼包 variants in their expected grid positions. No additional icons were saved during this check.

A fast vertical drag in the live formation editor painted only its endpoints. The board now interpolates crossed cells between mouse events. A WinForms regression sends one distant mouse move across seven rows, checks the complete column, erases it in reverse while preserving a later placement, and checks a continuous diagonal path. The updated editor was reopened and an attended drag filled all seven rows. It saved a new private 浮空/花园 formation with two producer columns and three attacker columns, which passed preflight and won the normal stage using 日光炉 and 红柳烤串机. Moving tiles caused many rejected placements; the retries completed this observed normal battle without establishing that every planned cell was filled.

The 花园 boss continuation disconnected before victory. The run re-entered the game but stopped when 日光炉 appeared in the second deck slot beside an unexpected remembered card. Deck building now rebuilds once if extra cards arrive during selection and verifies the final deck; regressions cover both one successful recovery and repeated changes that must stop. The actual reconnect race has not been replayed with this repair. No complete reward round was counted, and 每天闯花园 remained in progress with its reward unavailable. 天天制卡 was verified complete and its reward was claimed earlier in the session. Combat attempts were stopped after the repeated disconnects; renderer diagnostics reported no plugin crash or renderer termination.

The updated capture/input build was checked against `tests/testpage` in an isolated off-screen profile. Four successive captures changed both normally and while minimized. CEF hover changed the button highlight, and clicks advanced its color indicator, including after reacquiring hover in the minimized window. The first click immediately after minimizing did not advance the indicator. No plugin crash or renderer termination was reported. These checks do not establish live battle stability.

The controller had also timed out waiting for the portal's global loading flag, although its visible server controls could already be used. Selection now retries the existing visible-control check once the browser and renderer are ready. After the final restart, one missing-control observation was followed by automatic entry into the configured server without manual selection. The final Debug build, sensitive-data scan and scanner tests, area-menu tests, 48 automation regressions, documentation link-path check, and `git diff --check` passed. The final restart verified portal entry, not a complete gameplay round with the last reconnect repair.

## Components and operation

| Component | Details |
| --- | --- |
| Browser | CefSharp 84.4.10, based on the last Chromium generation that supports PPAPI Flash |
| Flash | Local `pepflashplayer.dll` 34.0.0.330, copied from CefFlashBrowser into ignored `vendor/pepflash/` |
| `--mode windowed` | Standard CEF child window; useful for comparison |
| `--mode osr` | Off-screen rendering into memory, then painted in the WinForms window; required for the automation panel |
| Local API | `127.0.0.1:18765`, requires `X-Fvm-Assistant` (legacy `X-Fvm-Spike` remains accepted), rejects requests with an `Origin` header |
| Runtime data | `.fvm-data/`: browser profiles, captures, logs, learned card icons, and panel settings (earlier observations used `%LOCALAPPDATA%\FvmAssistant\`) |

The process is DPI-unaware by default and forces browser scale 1, so one page pixel equals one Flash stage pixel, even on a scaled monitor. `--dpi-aware` opts into per-monitor rendering and changes the pixel mapping. The default viewport is 1280×800; the Flash stage is approximately 950×600.

Build and start from the repository root:

```powershell
.\scripts\fetch-flash-plugin.ps1 -CefFlashBrowserDir "<path to CefFlashBrowser x64>"
.\scripts\build.ps1
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode osr --profile selftest
.\scripts\assistant.ps1 /help
```

Useful routes include `/status`, `/locate`, `/capture?method=osr|devtools&crop=flash`, `/sample?method=…&count=4`, `/move`, `/click?x=&y=&method=cef`, and `/window?state=normal|bottom|offscreen|minimize`. Coordinates are stage-relative unless the route says otherwise. Keep the API bound to loopback; the header is a browser-origin guard, not a user authentication scheme.

## Local input and capture test

`tests/testpage/` contains an AS2 SWF with a hover/press button, a click counter, and a moving square. `frame.html` embeds it through a cross-origin iframe to resemble the game page. Start a local server and point FvmAssistant at it:

```powershell
python .\tests\testpage\make_test_swf.py
python -m http.server 8765 --bind 127.0.0.1 --directory .\tests\testpage
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode osr --profile selftest --url http://127.0.0.1:8765/frame.html
```

Run the server and application in separate terminals. Use `/locate`, `/sample`, `/window`, and `/click` through `scripts/assistant.ps1` to compare captured frames and input. The 2026-09-25 test machine was Windows 11 with 150% display scaling and GPU enabled.

| Window state | Windowed + DevTools capture | Off-screen capture |
| --- | --- | --- |
| Visible | Updating, about 220 ms/frame | Updating, about 1 ms/frame |
| Covered by another window | Updating | Updating |
| Moved off screen | Updating | Updating |
| Minimized | Timed out | Updating |

`PrintWindow` produced a frozen, offset image. CEF-injected hover and click worked in off-screen mode even while minimized; posting Windows messages to the rendering window did not. In the local test, a minimized off-screen process tree used roughly 24% of one CPU core.

On 2026-09-26, the cloud-stage repair build passed a fresh local Flash check with `--mode osr --profile cloud-selftest --port 18766`. The user launched the test instance after automatic approval review blocked the agent's launch attempts. The cross-origin embed was located at `(59,87)` with size 950×600 and no guessed offset. Both normal and minimized windows produced four distinct frames in four samples spaced 230 ms apart. CEF hover changed the button from RGB `(128,128,128)` to `(205,205,205)` in both states; clicks advanced the indicator from red to green, then from green to blue while minimized. The instance reported zero plugin crashes and render terminations, and its window was restored afterward. This verifies the local capture and input path, not cloud-stage gameplay.

## Real-game observation

On 2026-09-25, a user manually logged into the game from the 4399 page. The first game iframe request timed out and a refresh recovered it. The embedded stage was located at page coordinate `(165,0)` with size 950×596 in a 1280×800 viewport. Captures kept updating while minimized, and injected hover/click events opened and closed the in-game inventory without using the real pointer. CefSharp off-screen mode is muted by default.

Game login may ask to allow Flash local storage; that preference is stored in the browser profile. The iframe, CDN, and login infrastructure are external and may be slow or unavailable. See [automation behavior](automation.md) for refresh handling and [security](security.md) for the privacy of profiles and captures.

The prototype's conclusion is limited to these local and live observations: off-screen capture plus CEF input is suitable for this automation. Gameplay outcomes require separate manual verification. Per-profile browser and application-state isolation is implemented for the two-account mode. Manually paired 曲奇 runs have been verified; the automatic pairing attempt stopped at live room-number recognition.

## 2026-09-28 follow-up test

The rebuilt off-screen app loaded the local cross-origin Flash test page in an
isolated profile. `/locate` found its 950×600 embed at `(59,87)` without a guessed
offset. Four captures taken 230 ms apart were distinct both normally and while
minimized. CEF hover changed the test button from RGB `(128,128,128)` to
`(205,205,205)`; a click changed the indicator from red to green. A second click
while minimized changed it from green to blue. The API reported no plugin crashes
or render terminations. The test server and isolated app instance were stopped.

The read-only `/track` endpoint accepted a normal-stage preset and rejected an
incompatible later-wave preset with explicit diagnostic codes. The single-account
queue accepted one local task through `-DryRun` without starting a battle.
`make check` passed with 33 deterministic regressions, including construction of
the track editor and preview dialogs on an STA WinForms thread. Dialog layout has
not been visually inspected.

The main profile initially showed no game Flash object. After the user manually
logged in, `/locate` found the live 950×596 Flash embed and the detector recognized
the health notice. A one-round 美味/曲奇 run with the selected two-card mapping
passed preflight, closed the notice, entered battle, and reported Victory. It
finished the reward/treasure flow, counted one completed round, left the room,
and reported a return to 美味镇. The battle logged 20 confirmed placements, 11
refused attempts, one pickup failure, and one late card return; the loop recovered
and completed. This solo run alone does not verify duo operation or the new
track-editor interactions. A separate partner profile was opened for the
same-server login and duo test.

Two accounts were then manually paired in a 曲奇 room. The main account used
the same two-card output track, while the partner used only 小火炉 on a disjoint
support track in column 6. One coordinated round finished with Victory on both
accounts, including reward collection and a return to the shared room. The
main account logged 22 confirmed placements; the partner logged 6. A further
two-round run also completed on both accounts, with both victories and both
reward flows recorded in each round. In its second round, the partner confirmed
all seven 小火炉 positions. Both `/duo` endpoints reported `finished`, two rounds
done, and `Room` afterward. This verifies manual-room pairing, deck rebuilding,
round gates, simultaneous battle execution, and repeat-round coordination for
this stage and account setup; other stages and cards remain untested.

The first automatic pairing attempt created a room but stopped before guest
joining because the live room-number font did not match the installed digit
masks closely enough for two-frame verification. A first manual-room attempt
also stopped safely when an invitation overlay covered the host's deck bar;
after the user closed it, the same setup succeeded. Automatic room-number
recognition and invitation-overlay detection remain open limitations. Local
captures and run logs were kept outside the repository.

On 2026-09-28, friend invitation pairing was checked live for 美味/神殿.
Both accounts navigated to the same island area; the host created the 神殿 room,
invited the guest from the first online Friends row, and the guest accepted the
timed prompt. The coordinator compared room headers before starting. The first
battle reached normal victory, but the guest stopped because its “等待房主选择”
screen was not recognized. After adding that screen state and the paired boss
gate, a second run completed normal and boss combat. Both `/duo` endpoints
reported `finished`, `roundsDone=1`, `bossRoundsDone=1`, and `Room`. The host
used the 神殿 preset with 日光炉 and 红柳烤串机; the guest used a separate 小火炉
support track. These are observations for this account pair and stage, not a
claim that other friend rows, areas, or stages have been verified.

## 2026-09-30 attended port preparation

The visible 火山 area menu offered area 1 and area 11 as two rows. The
friend-pair script had incorrectly used the area number as a consecutive row
index, putting area 11 outside that menu. Its sparse mapping now selects the
second row for area 11 and rejects unobserved area numbers before input.
Both accounts were manually switched to the requested area using the corrected row;
this verifies the selection coordinate, not the full automated pairing path.
`scripts/test-friend-pair.ps1` exercises the mapping without game input.

Two private opening-only plans passed duo preflight: the host used 日光炉
and 红柳烤串机, while the guest used only 小火炉 on disjoint cells. These
plans were not played and do not establish victory or an A+ result. The
host created a 玛奇朵港 room, but the guest saw a level-locked area on its map
and did not enter after an invitation. Direct stage selection also left the guest on that map. This does not establish whether
an invitation can bypass the level requirement. The attended attempt stopped
before combat, and the host left the empty room. The 佳节打卡 challenge
requires an A+ port clear; no activity completion or reward was claimed.

## 2026-09-30 renderer-loss observation

Before an attended 曲奇 carry run started, both accounts logged renderer
termination with `OutOfMemory`. CEF browser objects
still appeared initialized, cached Flash information remained in `/status`,
and OSR captures returned the old game screens, while fresh frame discovery
returned no frames. This prevented pairing; no carry battle was started and
no completed round was reported.

Renderer-loss guards now invalidate OSR captures, browser input, the displayed
frame, and cached Flash status until navigation and fresh viewport paint prove
recovery. A deterministic regression covers the failure/recovery state machine.
The memory exhaustion cause and live recovery with this guard remain unverified;
the change is a safety fix, not evidence of a repaired memory leak or completed
gameplay.

### Updated-build restart and invitation timeout

After both old processes were closed, `make check` passed with 39 deterministic
regressions, and both accounts were reopened from the updated Debug output.
Both services reported `rendererReady=true` and no renderer terminations.
Fresh live captures and CEF input worked during task inspection and room
preparation; this does not validate recovery after an actual renderer failure.

The host created a 曲奇 room and invited the confirmed guest from the Friends
tab. The guest displayed the expected host and stage, but the attended capture
and subsequent acceptance attempt were separated by roughly 37 seconds,
exceeding the visible 15-second response window. The guest did not join and
the host reported that the invitation could not be accepted. No battle was
started or quest reward claimed in this attempt. This timeout does not prove
a guest level restriction; a later retry needs a bounded, immediate invitation
response rather than a model inspection round trip within that window.

## 2026-09-30 to 2026-10-01 attended port and carry runs

The primary account completed one 火山/港口 round with boss continuation,
using the existing recorded three-card solo track. The run reported boss
Victory and one completed round, performed the free chest flip, and returned
to town. Its private treasure capture showed an S rating. This establishes a
solo clear, not the guest's ability to enter the port.

In one island area, immediate invitation acceptance after about 0.52 seconds still
did not put the guest in the host's room. After both accounts were visibly
switched to another shared island area, the guest could see and join the host's
room directly from its list. The observed 美味 area menu had all eleven rows, unlike the
previous two-row observation. The pairing helper now counts visible gold
row markers, accepts only the observed two-row and eleven-row layouts,
and explicitly selects the Friends tab. Synthetic regressions cover the
layout mapping; the modified helper's full live pairing flow remains unverified.

One coordinated 曲奇 boss round and one coordinated 慕斯 boss round finished
on both accounts with `roundsDone=1` and `bossRoundsDone=1`. The primary
account supplied 日光炉 and 红柳烤串机; the guest brought only 小火炉.
Guarded task claims were verified after the coordinated rounds.

Repeated mission inspections without input produced different list hashes
while the selected-pane hash stayed unchanged. Comparing private captures
localized the changing pixels to the flashing category header. The white/yellow
label-ink masks were identical. The list guard now uses that stable label
mask, excluding status badges, while pending-reward and active-row checks
remain separate. A deterministic regression verifies that header animation
does not invalidate the guard but moved labels still do. Further battles were
stopped for this repair. The updated guard has not yet been loaded into the
two live instances or verified by a live accept action.
The services remained idle with zero renderer terminations in this restarted
session.

The sensitive-data scan, scanner tests, area-menu regressions, independent
Debug build, 40 automation regressions against that independent binary, and
`git diff --check` passed. The default `make check` test step still references
the older in-use Debug binary even when MSBuild uses an independent `OutDir`,
so that combined invocation did not complete; its regression step was rerun
explicitly against the new output. This is build and deterministic coverage,
not live verification of the repaired mission guard.

## 2026-10-07 attended guest task carry

Both accounts completed coordinated 美味/可可日 and 美味/可可夜 rounds with
boss continuation, each reporting `roundsDone=1` and `bossRoundsDone=1`. The
guest placed 小火炉 and, for the night stage, 香肠. The day-stage settlement
showed S with no damaged cards. An unused 木盘子 deck entry was needed to
avoid the game's water-stage ready warning; the two ordinary formations
remained disjoint. Mission acceptance and reward claims used fresh task-board
evidence, including the repaired stable list guard.

A guarded guild-task reward claim was verified after an API timeout.

火山/芥末日 normal combat ended in Victory. The guest's 钢丝球 was visibly
placed on bare water without a plate. During the host's outcome fade, the
generic guest-wait pixel heuristic temporarily misclassified the host screen.
Both runs stopped; an attended reward click then returned both accounts to
the shared room, and 火山岛首战 was verified claimed. This was a recovered
normal clear, not a completed automated boss round.

Positive victory and notice templates now precede the guest-wait heuristic,
and a host which just won briefly resamples a transient guest-wait screen.
Fixed support in paired tracks is limited to each account's own cells.
钢丝球 never requests automatic support and preflight rejects land targets
or selected plates/cards sharing its target. `make check` passed with 56
automation regressions, and the Release build was loaded into both accounts.

For 美味/深渊, the friend-pair helper now compares the three activity-bar
states and hides the bar before clicking a covered stage pin. The repaired
path paired both accounts in the same room during the attended run.

The complete 深渊 round then finished on both accounts with normal and boss
counters at one. Guarded reward claims were verified afterward. The host also
passed the normal victory-to-boss transition with the outcome recognition fix
loaded.

The first 城堡 preparation was blocked by the guest's full 道具 bag. Crafting
material stacks were moved intact into existing 储藏室 space to free item slots.
A subsequent battle disconnected during wave two and was not counted as a
clear. The guest's original eighth-column targets also
refused cards; the attended replacement reserved column two for the guest
and kept all host targets disjoint. Both accounts then completed the normal
and boss round. The festival's castle A+ objective showed complete, and the
guest's daily challenge reward displayed success and 已领取. Task claims were
reviewed against fresh evidence.

Subsequent paired 火山/芥末日, 火山/芥末夜, 美味/咖喱日, 美味/咖喱夜,
美味/布丁日, and 美味/布丁夜 runs completed normal and boss combat on both
accounts. The 咖喱夜 boss settlement showed C and 22 damaged cards; completion
does not establish the higher grade required by other tasks. Its guest fan
placements did satisfy 风扇转转, whose reward was verified claimed.

The fixed layer-5 burst path was exercised by the guest's own 酒瓶炸弹 in the
two pudding stages. A task objective independently advanced after both runs.

Inventory handling recovered guest space by merging exact duplicate stacks
between the item bag and storage, then storing selected recipes and books.
No item was deleted or consumed in those transfers. The primary account also
merged a duplicate skill book to recover an item slot before the night run.

Room-header comparison previously included pixels from the adjacent adventure
button and the scrolling announcement strip. Both could differ while the
visible room numbers matched. The helper now compares every pixel in the
room-label region below the announcement, requires two consecutive matching
capture pairs, and retains rejection of changed digit strokes. Synthetic
regressions cover both overlays and changed digits. An attended 可可夜
invitation passed the repaired guard and reached coordinated battle startup;
this does not establish unattended pairing reliability. `make check` passed
with 57 automation regressions after the repair.

A later 可可日 preparation showed the guest's item-bag warning while the
underlying screen still matched `Room`. Both accounts were stopped and the
inventory was handled through the visible game controls. The detector now
recognizes the observed compact two-button notice as `RoomNotice`, and the
loop stops without accepting it. Synthetic tests verify notice precedence,
unchanged room recognition, and a failed run with no clicks or completed
rounds. The restarted Release binary classified a previously captured warning
as `RoomNotice` and an unobstructed room as `Room`; this is captured-frame
verification, not a fresh live warning run. `make check` passed with 57
automation regressions.

The later attended carry session completed both normal and boss stages of
火山/薄荷日 and 火山/薄荷夜 on both accounts. The guest's 横扫一切鼠患 task
reached 15/15 and its reward was verified claimed. Five card/map information
tasks were completed through the visible 情报岛 lists and verified claimed.
A subsequent 炭烧日 attempt stopped at an item-bag warning before combat. The
updated `RoomNotice` handling was exercised by fresh live warnings on both
accounts. Remaining stage tasks were not treated as completed.

Learning a room icon while hovering over its deck slot produced an icon which
deck selection rejected as gray. Moving the pointer away and relearning the
same card allowed selection. The learning method now parks the CEF pointer
before capturing and rejects active runs. `make check` passed with 57
regressions after this change. An isolated Debug instance located the local
950×600 cross-origin Flash test page, but the user requested a stop before
the hover/click checks or a live test of the new learning method. Both game
coordinators were confirmed idle and the isolated test app/server were closed.

## 2026-10-09 attended main tasks and inventory input

An attended single-account check encountered a full equipment bag blocking
reward collection. Visible inventory transfers and item use recovered capacity;
no item was deleted and no paid purchase was made.

The local API now supports held-button CEF drag input and right clicks. An
isolated instance on the [local HTML pointer page](../tests/testpage/drag.html) verified one press, twelve
held moves, one release, delivery to the target, and right-button release.
The existing cross-origin Flash test page also verified hover feedback and
release after a drag. Live storage transfers subsequently exercised the same
drag endpoint. These observations verify input behavior, not unattended
inventory recognition or management.

An unconfirmed mission claim now retains the first post-click feedback frame
as well as the final task-board capture. A synthetic regression verified that
a transient notice survives in the private attempt image after later frames
remove it, while the reward remains unclaimed and no second click is sent.
This diagnostic change has synthetic coverage; a fresh live failed claim is
not claimed as verification. `make check` passed with 59 automation
regressions, and the Release build was loaded into the main instance.

An attended solo 美味/神殿 round completed normal and boss combat, collected
rewards, and returned to town with both counters at one. 神殿中的法老 then
showed its objective at 1/1 and its guarded reward claim was verified claimed.

Two solo 浮空/月桂日 normal rounds completed with grade A. The first formation
repeatedly tried unavailable cells on the moving upper and lower platforms.
The second concentrated producers in the fixed middle rows and gave the moving
lanes earlier attacker targets. Its requested boss continuation did not enter
a separate boss stage. A+通关月桂天空 remained at 0/1, so neither clear was
reported as completion of that mission.

The first 布丁日 preparation stopped on a freshly observed `RoomNotice`: the
full item bag would prevent obtaining loot. The notice was canceled before
combat. Attended inventory handling recovered an item slot. This live notice
exercised the existing startup guard in the new Release instance.

The retried 美味/布丁日 normal round completed with five scheduled 日光炉
placements and no producer replenishment. The selected 卡片限制! LV.3 pane
then showed complete, and its reward was verified claimed. 月桂日 had also
completed the growth task 灰机你别跑啊!, whose reward was verified claimed
separately. The session completed four normal rounds and one boss round;
five task rewards were verified claimed, including the two login tasks.

Daily claim controls disabled after collection. Screenshots, account activity
records, learned icons, and edited formations remain in the ignored runtime
directory.

## 2026-10-11 attended main tasks and support retry repair

An attended single-account check verified guild publication and task-board
navigation. Two solo 火山/芥末日 normal rounds ended in Victory and returned
to town, but did not satisfy the selected boss objective. Requested boss
continuation did not enter a separate stage. A separate map-information task
was completed through 情报岛, and its guarded reward claim was verified.

The two 芥末日 rounds exposed a support-map inefficiency: the fixed 木盘子
layout retried many cells the stage rejected (126 and 119 refusal attempts in
the two runs). Fixed support cells now use a two-minute retry backoff while
remaining retryable, instead of the ordinary 30-second backoff. This limits
repeated input on known-invalid cells without disabling recovery after a board
change. The change passed the 59 automation regressions and the Release build;
the rebuilt Release process was loaded after the live session and left idle in
美味镇. The new backoff itself has not yet been exercised in a subsequent
live battle.

## 2026-10-14 attended main login and window visibility repair

The saved main session had expired, so server selection initially stopped on
the login page. The user completed login manually after the game window was
restarted visibly. No password or verification code was read or entered by the
assistant. After login, the configured server loaded and guild publication
was verified by its disabled control.

An attended solo 美味/神殿 run completed normal and boss combat and returned
to town with both counters at one. 神殿中的法老 independently showed 1/1,
and its guarded reward claim was verified claimed.

A separate solo 美味/咖喱日 run completed its normal stage and returned to
town. 咖喱岛节约之旅 independently showed 1/1 before its guarded claim;
this verifies the task's victory and remaining-flame requirement. The already
completed 飞船秘密钥匙LV1, 每天领取勇士令牌, and 常回家看看 rewards were
also claimed. No additional Laurel battle is attributed to this session.

The hidden-started process had normal window bounds but no visible main
window. Previously, `onscreen` only restored a saved location and `top` only
activated the form. Those commands now also show the form; `onscreen` restores
a minimized form. Window status reports `visible` separately from bounds and
minimized state. `make check` passed with 59 automation regressions. A separate
diagnostic-window launch was blocked before execution; it did not operate
another game account.

After the attended gameplay, the Release build succeeded and the main profile
was reopened with the rebuilt executable. Its `onscreen` response reported a
visible, normal window with a nonzero main-window handle. A subsequent manual
API check changed the window to minimized and verified `onscreen` restored it
to normal with `visible=true` and `iconic=false`; `top` verified foreground
activation. This is a window-state check, separate from the live battles above.

## 2026-10-16 attended main MCP check and cloud recognition repair

An attended single-account check exercised the bridge and cloud-stage recovery.

An attended client exercised the actual PowerShell stdio MCP bridge, negotiating
2025-11-25 and discovering all 11 tools. Account state, complete catalog
pagination, effective track inspection, validation, native image content and
identified background startup were exercised. Separate bridge processes could
query an accepted run; repeating its identical request returned the original
run ID rather than starting another run. This checks the bridge protocol,
not registration or rendering in a particular MCP client's interface.

The first 火山/棉花日 attempt stopped without a completed round because blue
clouds and a white cell matched the three-pixel guest host-decision heuristic.
The live frame was still a battle, while the executor had reported Victory and
then stopped at AwaitHostDecision. MCP reported a failed run with zero completed
rounds and required attention; it did not report the mission completed.

Screen detection now opts into that guest-only heuristic only for a duo guest
execution context. Solo and host loops use a separate detector context with the
heuristic disabled; ordinary state inspection also defaults to disabled. The
guest context retains recognized victory/notice precedence. Synthetic coverage
checks cloud-like pixels with a battle marker in both contexts. `make check`
passed all 64 regressions, including MCP subprocess tests. A subsequent live
solo attempt continued placing cards through the cloud scene that had stopped
the first attempt.

The retry completed one normal and one boss round and returned to town. The
棉花日中尉 guild task independently displayed 1/1 before its guarded claim
was verified claimed. The MCP run ended `completed`, with both completion
counters at one, screen `Lobby`, and no error or attention flag.

A second MCP plan used the existing private Laurel-day formation for
浮空/月桂夜, with effective night support inherited and validation passing.
It completed the normal stage and returned to town; 空袭来了! independently
showed completion and its reward was verified claimed. A `stop_run` request in
`finish-round` mode during this one-round run set the flag, preserved combat
and rewards, and ended `completed` with one normal round and no boss round.
All 11 exposed MCP tools were called during this attended session; immediate
cancellation was not exercised.

The final Release build and sensitive-data scan passed. The rebuilt main
process reopened with its saved session after the attended gameplay.
