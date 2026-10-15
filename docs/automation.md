# Automation behavior and operating notes

The game-control loop lives in `src/FvmAssistant/Automation/` and runs only in FvmAssistant's off-screen mode. It recognizes screen states from image templates and sends CEF mouse events without taking over the real pointer. The window may be minimized during a run.

## Single-account round flow

```text
美味镇 → world map → island → stage → room list → create a room
room → close the 2P slot → clear and build the deck → start
battle → place the character → place cards from the track → collect drops
victory → collect rewards, or continue into the optional boss stage
loot → settlement → free chest flip → room → next round
final room → leave room → world map → 美味镇
```

The loop closes recognized health/activity popups. If a screen remains unrecognized for 45 seconds, it refreshes; it also refreshes after 30 seconds at `加载中 100%` or immediately on a disconnect. A failed round is abandoned and navigation restarts from 美味镇. Each round allows at most six refreshes. A newly loaded page resets the wait clock so two refresh mechanisms do not interrupt one another. Unhandled situations stop with a screenshot rather than blind clicking.

When a fixed stage support map rejects a carrier cell, that cell is retried after
a longer two-minute backoff. This keeps changing boards retryable while avoiding
rapid repeated card clicks on cells the stage cannot accept.

World-map navigation clicks the text label at `(866,58)` in stage coordinates;
the decorative area at the older `(866,41)` position can ignore island-map input.
After the final reward flow, opening the world map sends at most three clicks
within the existing 15-second transition limit, spaced at least two seconds apart.
Each click requires a freshly recognized lobby map. A disconnect or popup stops
the return, and an unknown screen permits no input. Completed battle counters
remain available if returning to town fails; task completion still needs board evidence.

On 2026-09-25, three consecutive 曲奇岛 normal-stage rounds succeeded from 美味镇, each taking about 2 minutes 45 seconds. This is a manual observation, not an automated regression test. When a target stage is specified but the application starts inside a room, it first leaves the room because it cannot verify that room's stage.

## Operation panel

1. Log in and enter the game manually. Stop at 美味镇 or in a stage room.
2. Select a stage from `普通关卡.txt`, or select the already-in-room option. Selecting a stage filters the available preset tracks; an arbitrary track can also be chosen by file picker. The formation/timeline preview shows all track waves on a 7×9 board, with skipped cards in gray and `00` events in the timeline. The adjacent step editor can edit a copy of the selected track; see [editing a track copy](#editing-a-track-copy).
3. Map each track card to a learned card icon from the account. Select “do not bring” to skip a card. To learn a new icon, open the defense-card page in 背包 and use **Scan 背包...**; room-deck learning also remains available. See [inventory card learning](#inventory-card-learning).
   The card list includes support cards resolved from the selected stage's defaults. The compatibility report updates when the stage, track, mapping, or shovel option changes; errors disable Start.
4. Choose a round count and, if needed, boss continuation, shovel steps, and the account's automatic flame-pickup setting. Start the loop. The game view ignores manual mouse and keyboard input while automation is active; Stop ends it.
5. Use the recorder to capture a manually played round for debugging or track generation. It records mouse actions and frames, not keyboard input.

**Finish current round** requests a solo stop after the current round's reward flow,
including its configured boss continuation and the one free treasure flip. It then
returns to town without starting another battle. The immediate Stop button still
cancels the current action. A finish request made before any battle completes allows
the first planned round to finish; if made while the next deck is being built, the
loop checks again before Start and can return without another battle. Paired runs
must be stopped through their coordinator. The loopback equivalent is
`/grind?state=finish-round`; progress includes `finishAfterRoundRequested`, and an
early return reports `finished-early`. A queue does not advance when fewer than its
requested rounds completed.

The red crosshair is the automation pointer shown only in the UI. During automation or recording, the page keeps its starting size; resizing the window can crop or add blank space but does not shift stage coordinates. For the `main` profile, panel settings are in `.fvm-data\panel.json`; learned icons are in `.fvm-data\cards\` and run logs/captures are in `.fvm-data\captures\grind-*`. Treat all of these as private.

The game can center the stage at a half pixel when page width is odd. Stage positioning rounds that edge upward to match Chromium. Older recordings can be corrected by one pixel during track conversion. If a battle frame stops changing for five seconds, the application asks the browser to redraw; after 20 seconds without placing a card it logs card-slot diagnostics.

Other profiles store their panel settings, learned cards, captures, logs, generated tracks, and edited copies under `.fvm-data/accounts/<profile>/`. Browser cookies and Flash storage are under `.fvm-data/profiles/<profile>/`. A second process cannot open the same profile at the same time.

## Independent controller

Run `FvmAssistant.exe --mode controller` from the Debug or Release output, or open the executable without arguments. This window does not initialize CEF. The `账号` tab stores a profile name, control port, server number or label, and entry URL. On the current 4399 portal, a numeric server such as `<server-number>` is selected by locating the visible number field and `进入` control, then sending CEF mouse and key events. Selection retries while those controls are unavailable; background network loading does not block a ready page. A direct server URL can also be used. If the page layout changes and those controls cannot be found, the controller stops and asks for manual selection. It does not inspect cookies or Flash storage. The account card list displays icons and supports inventory scanning, removal, and legacy room-deck learning. The `轨道` tab filters tracks by stage, previews the selected formation, creates new formations or edited copies, and keeps success markers per account, stage and track.

The controller has four tabs: `账号`, `轨道`, `关卡队列`, and `双人任务`. The account tab separates **Card library** from **Account settings**, keeping scanning and the icon list visible. New workflow controls use English labels alongside existing Chinese labels. Each list sits beside its settings and actions. Choosing `加入关卡队列` on a track carries its path, account, and selected stage to the queue tab. It fills mappings for learned cards with matching names, or remembered account mappings; review any missing mappings before running. The bottom panel shows task output and provides a stop control and a shortcut to the private runtime directory.

The `关卡队列` tab stores several stage runs for one account and uses the same `scripts/queue.ps1` preflight and sequencing as a manual plan. If a queue request starts a closed account window, the controller waits up to 90 seconds for a recognized rendered game screen. Failure asks the user to log in and select a server; it does not try to read or interpret saved login state. Success markers are set after the coordinator exits successfully. The `双人任务` tab runs `scripts/duo.ps1` with each account's track and card mapping, and can synchronize optional boss continuation. Friend invitation pairing uses the visible island area selector and the configured row of the host's online Friends list, then accepts the guest's timed prompt. Both windows must be at a lobby map on the same server before it starts. Manual room mode still requires both windows to be paired on the same server before starting; room-number pairing with a stage is experimental as described below.

Account cleanup is available only after its game window closes. The dialog can delete that account's recordings/captures, logs, generated tracks, and known CEF resource-cache folders independently. It preserves login storage, learned cards, and edited track copies. Legacy generated tracks in `vendor/track/预制轨道/我的轨道/` and legacy edited tracks are not included because their account ownership is unknown.

## Inventory card learning

1. Open the chosen account's game window and manually navigate to the defense-card page in 背包. Stop any active automation first. Use **Scan 背包...** from the controller's **Card library** workspace or the off-screen operation panel, then **Capture page**.
2. Set **First icon origin** and click the top-left of the first card body in the captured image. Crops are native 42×50 pixels, including the star badge, matching the room icon format. Set the visible row and column counts. Use **Next column origin** and **Next row origin** to measure the spacing, or edit the numeric fields. The grid must fit wholly inside the capture without overlapping icons.
3. Choose **Scan grid** and inspect the outlined crops and thumbnail list. The scanner can correct the origin by up to two pixels when at least two distinct known icons agree on a unique offset; otherwise it retains the manual calibration. Any correction is shown in the status text and X/Y fields. Existing icons are suggested by local pixel matching; unfamiliar icons need a manually entered card/variant name. Keep empty slots, unrelated items, and incomplete crops unchecked. Use distinct names for different star variants. A crop from a differently rendered or scaled inventory view may not match the room list; room-deck learning remains the fallback.
4. Check the icons to add and choose **Add selected cards**. Existing names require **Replace existing names**. The entire selection is validated for names, duplicate locations and bounds before saving. A successful add consumes the capture; capture again to continue. Turn pages in the game manually, then repeat the capture/review/add process.

The scanner sends no game input, performs no OCR naming, and does not automatically open or page through the inventory. Calibration is saved per account in the ignored `inventory-grid.json`; card PNGs use that account's existing private `cards/` folder. Captures stay in memory and expire after ten minutes or when a newer inventory capture replaces them, so saving uses the pixels that were reviewed even if the game view has since changed. Inventory learning and run startup share the account operation lock. Existing running processes need a restart with the updated executable to expose `/inventory`.

Room-deck learning also reserves the account operation lock and refuses an active
run. After verifying the room, it moves the CEF pointer away from the cards,
waits 600 ms, and verifies a fresh room frame before cropping icons. This avoids
saving hover shading or a tooltip as part of an icon.

The loopback API offers `/inventory?state=capture`, returning a capture `id` and base64 PNG, and `/inventory?state=learn&id=...&items=...&replace=1`, where `items` is a URL-encoded JSON array of `{ "Name": "card variant", "X": 100, "Y": 120 }`. The controller uses `items64` instead of `items`, encoding the same JSON as UTF-8 base64 to keep full-page Chinese-name batches within the request-header limit. Omit `replace` to refuse existing names. Treat the returned image as private account data and do not paste it into reports. The UI handles this API for controller users.

## Local control API

### Guild publication and mission rewards

The off-screen panel includes **Publish guild-leader tasks** and **Claim selected task reward**.
**Open account task board** and **Open guild task board** navigate from a lobby map,
the guild hall, or either task board and return fresh evidence. The loopback routes
are `/missions?state=open` and `/guild-tasks?state=open`. Opening an already visible
board sends no input. These actions do not select tasks, claim rewards, or draw
guild tasks; unknown screens and popups stop navigation. Close those popups first.
Publication navigates the visible guild activity menu, draws once, and verifies that publication
is disabled afterward. An already-disabled publication button causes no draw. This uses local
image templates and CEF input, never game-protocol requests. Start from a lobby map, the guild
hall, either task board or an already open publication dialog; an unrecognized screen stops
the action. Publication requires the account's guild
permissions and must be done before the game's daily deadline.

Mission handling is attended, not an autonomous task scanner or quest-to-stage planner.
Open the relevant board, inspect its private capture, and use the observed row coordinate.
Always collect completed rewards before accepting newly unlocked tasks. A task is accepted
only after clicking its row and verifying **任务进行中**; a victory alone is not proof that a
mission was completed. Claiming verifies **已领取** or a changed, no-longer-claimable task pane.
Do not repeat an uncertain claim or draw without inspecting the current state.

```powershell
$before = .\scripts\assistant.ps1 '/missions?state=inspect' | ConvertFrom-Json
.\scripts\assistant.ps1 ('/missions?state=select&row=173&listHash=' + $before.ListHash)
$before = .\scripts\assistant.ps1 '/missions?state=inspect' | ConvertFrom-Json
.\scripts\assistant.ps1 ('/missions?state=claim&selectedHash=' + $before.SelectedHash)
$before = .\scripts\assistant.ps1 '/missions?state=inspect' | ConvertFrom-Json
.\scripts\assistant.ps1 ('/missions?state=accept&row=173&listHash=' + $before.ListHash)
.\scripts\assistant.ps1 '/guild-tasks?state=publish'
```

Replace the example row with a row observed in the latest capture. Hashes guard the selected
pane and visible list against reordering or other intervening changes.
The response's `CompletedRewardRows` contains candidate Y coordinates derived
from green completion badges in that captured list. Review the associated capture
to identify each task, and use fresh list evidence for selection. This read-only
field neither scrolls the list nor clicks or claims a task.
`select` returns the selected pane and fresh reward evidence without clicking the reward
button. The game may automatically accept a task when its row is selected; inspect the
returned state before battle. If any visible reward is pending, selection is restricted
to a row with a completed-reward badge, and `accept` remains blocked until rewards are
collected. Publication recognizes both the gold enabled button and the gray disabled
button; the orange draw button retains its separate check and one-draw verification.
The list guard hashes stable white/yellow label ink instead of animated category
backgrounds or status badges. Pending rewards and the accepted row's active badge
remain independent checks; selected reward-pane evidence still uses its full image.
Acceptance refuses visible completed rewards; it does not prove that rewards in collapsed categories or outside
the current viewport have been collected. Review all categories first. Party, guild-member,
ranking, restricted-deck, consumable and specific-card requirements need manual eligibility
review; unsupported preset requirements are not bypassed. Mission actions and grind startup
share an account operation lock, and manual pointer API input is rejected during automation.
The Stop button remains the battle stop control; short mission actions have bounded waits.

Private mission captures stay under the account's ignored captures directory. For local text
diagnostics, `powershell.exe -NoProfile -File scripts/read-game-text.ps1 -Image <private capture> -X <x> -Y <y> -Width <w> -Height <h>`
uses Windows PowerShell and the installed Simplified Chinese Windows OCR language. No cloud
OCR or SDK installation is required. OCR can misread outlined fonts and is not used as
authority for automatic stage selection. `-Ink orange` filters objective text; `-Ink list` and
`-Ink status` are optional diagnostic filters. Keep OCR output private as well.
Use `powershell.exe` explicitly when the default shell is PowerShell 7; its runtime
does not expose the Windows Runtime types used by this diagnostic script.

A compact two-button system notice covering a recognized room is reported as
`RoomNotice`. The run stops with a message identifying the blocked Start/Ready
action, without accepting either button. Inspect the notice and resolve its
requirement before restarting; insufficient item-bag capacity is one observed
cause. Recognition does not interpret the dialog text or manage inventory.

The API is loopback-only and intended for local scripts. Examples (run from the repository root):

`/window?state=onscreen` restores and shows a hidden or minimized game window;
`state=top` shows and activates it. Window status includes `visible` separately
from bounds and minimized state, since a hidden window can still have normal bounds.

For attended inventory transfers, `/drag?fromX=103&fromY=192&toX=737&toY=111`
sends a CEF left-button press, twelve held-button moves, and a release. Coordinates
default to Flash-stage space; use `space=page` for an HTML test page. The action
reserves the account operation lock and refuses active automation. It does not
recognize items, choose a destination, or verify a transfer; review fresh captures
before and after each drag. `/click` also accepts `button=right` with `method=cef`
for attended cancellation of a held selection. The window-message input path
continues to support left clicks only.

If a task reward remains unconfirmed after its bounded wait, diagnostics retain
the frame sampled 500 ms after the claim click and the final board frame. The
error identifies both private capture paths. This preserves short-lived feedback
such as a full equipment bag; it neither interprets that notice nor retries the
claim. Confirm the current reward state before acting again.

```powershell
.\scripts\assistant.ps1 '/cards?learn=日光炉,,,,红柳烤串机'
.\scripts\assistant.ps1 '/grind?state=start&rounds=3&level=美味/曲奇&track=通用轨道/6美味/1曲奇.txt&cards=小火炉:日光炉,海星:红柳烤串机'
.\scripts\assistant.ps1 /grind
.\scripts\assistant.ps1 '/grind?state=stop'
```

`level` is `island/stage` from `普通关卡.txt`; omit it only when already in a suitable room. `track` is relative to `vendor\track\预制轨道` or an absolute path. `cards` maps `track card:learned card` in deck order; unmapped track cards are skipped. `shovel=1` enables shovel steps, `boss=1` requests boss continuation, and `autoflame=1` changes collection sweeps for an account that automatically picks up flames. Never put login/session tokens in these URLs or logs.

`/grind` reports both `roundsDone` and `bossRoundsDone`. A normal-stage victory alone does not increment the completed-round count when the run disconnects before rewards. Verify task progress on the mission board as well.
Transition polling also updates the reported `screen`, so entering a recognized boss
battle replaces the preceding victory-dialog state before the battle runner resumes.

## Two-account rooms

This mode uses two off-screen processes on the **same server**, with different
profiles, ports, and learned card libraries. The supported live workflow is to
pair both accounts in a room manually and then run `scripts/duo.ps1` without
`-Level`. Follow the [manual two-account guide](two-account.md) for separate
process startup, a tested 曲奇 support track, the coordinator command, and
progress/stop commands. Confirm the host and close invitation or other overlays
before starting. The room screen does not expose its stage reliably, so the user
must verify the stage in manual mode.

With `-Level`, the coordinator attempts to create and join a room automatically
from a navigable map. This path is **not yet usable in the tested live setup**:
room-number recognition failed after the host created the room. The command
below describes the implemented interface for further testing, not a recommended
unattended run. Automatic mode rejects an existing room or room list because it
cannot verify that stage:

```powershell
.\scripts\duo.ps1 -HostPort 18765 -GuestPort 18766 -Level '美味/曲奇' `
    -HostTrack '我的轨道\host.txt' -GuestTrack '我的轨道\guest.txt' `
    -HostCards '小火炉:日光炉' -GuestCards '海星:红柳烤串机' -Rounds 3
```

The coordinator checks both tracks before touching the browsers. With `-Level`, it generates a four-digit temporary password, has the host create the selected stage's room, reads the room number, and has the guest locate that number in the visible room list and join. Both clients recheck the room number on later rounds, including the final return to the room. The script keeps the temporary password out of its normal output; it travels through the local control API and may still be visible in local diagnostics. Do not share run logs or captures without review.

Both tracks must specify a single `人物1位置` for their **own** account. Duo plans accept ordinary layer-3 cards, fixed repeated layer-5 bursts, and a matching layer-1 support for one fixed stage layout. Each account placing ordinary cards on water or cloud support cells must carry its own matching support card. Explicit support rows must use a maintained time-zero formation; empty rows repair only that account's ordinary targets, and inherited rows include all resolved targets in the overlap check. Card and support cells must remain disjoint between accounts, and neither formation may target the other character. Character positions retain the game's existing placement rules rather than inheriting card-support requirements. Timed or mixed support layouts and shovel actions remain unsupported. Optional boss continuation requires a usable key on both accounts and is coordinated through both victory prompts. Both profiles need their own learned card icons matching the supplied mappings. Automatic joining searches only the currently visible room list and times out after 60 seconds if the host room is absent; page turning and list refresh are not implemented.

For each round the coordinator waits until both decks are ready, asks the guest to prepare, then asks the host to start. Each browser places its character and captures its opening board before either track begins card placement. Both accounts run their own track and reward flow; the next round starts only after both return to the room. A lost room, disconnect, unrecognized screen, failed battle, or missing coordinator heartbeat stops the affected account, and the script stops its partner. Duo completion is counted separately for each account. The final round leaves both accounts in the room.

`/duo` reports each account's state and, in automatic mode, its recognized room
number; the number remains `0` in manual mode. `/duo?state=plan&track=...&cards=...&level=...`
checks one track without accessing the browser. The coordinator sends
round-specific `room` and `battle` releases with run IDs; use `scripts/duo.ps1`
to manage these gates. `scripts/assistant.ps1 -Port 18766 /duo` reads the second
account's state, and `/duo?state=stop` stops its run. The host's empty/open 2P
seat is detected, but the occupied seat does not yet have a positive template.
Room-number matching adds an automatic check in `-Level` mode but does not prove
the guest occupied the seat; verify each manual pairing before starting.

This implementation has deterministic gate, track-plan, and room-number recognition regression coverage. On 2026-09-28, manually paired 曲奇 accounts completed one coordinated round and then two consecutive rounds. Both accounts reached Victory, collected the free treasure card, and returned to the shared room after each round; the partner placed only 小火炉. Automatic pairing stopped before guest joining because the live room-number font was not recognized by the installed masks. Automatic room password entry and guest joining therefore remain unverified, and the occupied-seat display still has no positive template. Keep automatic pairing attended until these paths pass a live test.

## Single-account task queues

`scripts/queue.ps1` runs a sequence of ordinary stage grinds on one already logged-in off-screen profile. Save a plan outside the repository, for example `my-queue.json`:

```json
{
  "tasks": [
    { "level": "美味/曲奇", "track": "我的轨道/曲奇.txt", "cards": "小火炉:日光炉", "rounds": 3 },
    { "level": "美味/色拉", "track": "我的轨道/色拉.txt", "cards": "海星:红柳烤串机", "rounds": 2 }
  ]
}
```

Run `.\scripts\queue.ps1 -Plan .\my-queue.json -DryRun` to check every track without starting a game run; omit `-DryRun` to execute. Add `-Port 18766` for another profile. Each task may also set `shovel`, `boss`, or `autoflame` to `true`. The queue requires a stage on every task, uses that account's learned card icons, and starts the next task only after the previous one reports all requested rounds finished. A failed, stopped, replaced, or timed-out run stops the queue. This first coordinator does not automatically retry or skip a failed stage because the current game screen may not be a safe navigation boundary. Keep the plan and run diagnostics private if they identify an account.

## Track compatibility checks

The panel and `/grind?state=start` use the same preflight checker. Start reloads the
track and its support layout, checks them before accessing learned icons or the
browser, and passes the checked snapshot to the run. Editing a file after that
does not alter a running track. Refocus the track field to refresh a preview after
editing its file.

Use the read-only endpoint before starting an imported preset:

```powershell
.\scripts\assistant.ps1 '/track?path=通用轨道/6美味/1曲奇.txt&level=美味/曲奇&cards=小火炉:日光炉,海星:红柳烤串机'
```

The response includes `effectiveDeck` and `compatibility` with `CanStart` and
`Diagnostics`. Each diagnostic has a severity, stable code, source line where
available, card name, and explanation. Without `cards` or `map`, it checks all
opening cards; with either mapping it checks the selected subset, as Start does.
Pass `shovel=1` to include shovel semantics. This endpoint does not inspect the
browser or verify whether learned icons exist. An incompatible start returns HTTP
409 with the error descriptions; malformed support layouts return HTTP 400.

Errors include damaged steps, invalid or out-of-range cells, duplicate
headers or card names, invalid character positions, inconsistent declared deck
size, later wave sections, active gem actions, unknown flags/headers, unsupported
special layers, and non-default settings the executor does not implement. `放卡限速`
accepts 0 for the normal runner cadence or an integer from 1 to 5 to limit card
attempts per second. A limited attempt is measured from the start of the previous
card action; shovel actions are unchanged. Selected
`极` cards can use delay, trigger mode, minimum flames, an ordinary-card count,
termination count, and termination time. A card-count condition needs its named
layer-3 card selected in the opening deck. The count uses confirmed board evidence;
an unreadable or unconfirmed card cannot satisfy a positive threshold. A positive
flame threshold requires the locally installed `火苗`
or `火苗_win7` digit masks under the configured track-editor directory; missing
masks block Start. A failed read is unknown, distinct from zero, and never reuses
an old value. A frame that has stopped changing for five seconds also invalidates
the reading. Maintenance mode (`0`) resets a pending delay when the threshold
reading is lost and starts a new delay after each confirmed placement. Trigger mode
(`1`) latches after the first valid observation and delay. Termination count uses
confirmed placements and is rolled back after a late rejection; termination time
uses elapsed battle time. Other active condition fields, including wave, image,
targeting and follow conditions, remain blocked. Inactive condition fields without
`极` produce a warning. Malformed active basic fields also block Start.
Zero/empty/`NULL` defaults, editor metadata, and the editor-only `锁` and `x` flags do not
block a run.

Layer-5 cooldown cards with explicit opening formation cells can target occupied
cells and do not reserve those cells as permanent cards. This covers `冰激凌` in
tracks such as 火山/炭烧日. The editor's `冰沙` fallback-cell behavior, non-shovel
layer 0, layers 4 and 6, and later-wave changes remain blocked. Attended paired
布丁日 and 布丁夜 runs used fixed repeating `酒瓶炸弹` targets; the guest's
mission counter confirmed eight cumulative uses. This verifies that burst
path for the observed card and formation, not every layer-5 card or condition.

Timed rows without `独` use synchronized track time. It starts with the battle clock
and excludes the time spent executing ordinary timed placements (including a
timed shovel action). An `独` row uses elapsed battle time without that offset.
Extreme (`极`) placements do not add to the offset, but their timed start marker
uses the selected row's clock. This follows the track executor's scheduling rule;
the actual CEF input duration is measured rather than copying its input delay.
Whole-second and one-decimal-second steps use the editor's 0.1-second clock. A
nonzero step must be at least one second and no later than 960 seconds;
additional decimal digits are rejected rather than silently rounded. Time-zero
fill formations remain available.
A continuous `极` fill formation may have one timed start marker, such as `035|49`
followed by time-zero cells. Scheduled `补` without `极` uses each positive-time
entry as a fill event; `00` is a timed event without a target cell, while playable
cells on the same row form the placement set. A full formation consumes its event
without clicking. The runner makes one confirmed placement per event and retries a
refusal; it does not copy the editor's multi-click input sequence. Later timed
markers on an `极` formation and unresolved requested support inheritance remain
blocked. These checks expose
existing execution differences; they do not add wave
changes or targeting. Some previously accepted presets now stop before entering
the game instead of silently dropping their requirements.

An intentionally unmapped card or disabled shovel emits a warning. Structural
errors remain blocking even on skipped rows, and later-wave sections remain
blocking for the whole track. Unknown priority queue cards are errors; known but
unmapped queue cards are reported as skipped. There is no bypass flag for errors.
In a solo room, `人物2位置` describes the unused second player and is reported as
a warning even when it names playable cells. Character placement
continues to use `人物1位置`. Selecting “do not bring” for a known card, including
one named in the priority queue, leaves warnings but does not block Start.
Automatic deck building supports up to 11 selected cards; manual slot mapping
retains the existing 18-slot limit. Time-zero formation markers mixed into
ordinary non-`补` schedules are not supported.
The report covers implemented syntax and execution limits, not whether a formation
can win. Diagnostic warnings and support-resolution information are also retained
in the local run log.

## Editing a track copy

Select an account and stage in the controller's `轨道` tab. **New formation...** starts an empty private track; **Visual edit...** opens the selected track. The off-screen panel offers **New formation...** and its existing `编辑轨道副本…` action. **Show all stages** bypasses the controller's stage filter. Controller-created copies retain a stage association in private `controller.json`; copies saved through the off-screen panel use the selected stage in their default filename.

Add a card from the account library, choose its cooldown and stacking layer, and choose **Maintain (补极)** or **Timed (独)**. The initial cooldown is seven seconds and the layer is ordinary (3); adjust these for the actual card. Maintain mode paints time-zero formation cells and keeps them replenished; timed mode uses positive placement times. Select a card on the left, enter the time in seconds below the board, and click or drag across the 7×9 board to add placements. Fast drags fill the cells between successive pointer positions, including diagonal paths. Painting an existing card/time/cell entry leaves it unchanged. Right-click or **Erase selected card** removes that card only at the entered time. Other times and other stacked cards remain intact.

Choose **人物1位置** or **人物2位置** as the board tool and click to set the character cell; right-click removes that cell from the respective header. Solo execution uses 人物1位置. Blue cells mark the selected stage's fixed or timed support locations, and **Add stage support** inserts matching inherited support rows. Supply a learned icon for that support card before running. The board displays all targets, including future placements, rather than simulating the current battle state; layered targets show an additional-card count and details on hover.

The step list and existing header/specification controls remain available for precise edits. Steps can be replaced, appended, deleted, or reordered. Use `035.5` and `49` in the two fields for `035.5|49`; `00` is available for a timed scheduled-fill event. Removing an opening card also updates a declared slot count and its priority-queue references. Undo and redo operate on the in-memory copy. The preview button shows the edited board and timeline, while the diagnostic box reports whether the opening plan can run with the selected stage's support layout. Incompatible drafts can be saved for later editing; run preflight still blocks unsupported instructions.

`另存副本…` writes a new UTF-8 file under the current account's
`.fvm-data/edited-tracks/` or `.fvm-data/accounts/<profile>/edited-tracks/`.
It loads that absolute path in the operation panel. The imported preset is never overwritten. Choose a new filename
for each saved version; an existing file is not replaced. Source lines outside the
edited card rows are preserved, including headers and later waves. The
editor adds cards only to the opening and does not execute later-wave behavior; existing later-wave rows remain editable, and the compatibility
report remains authoritative before Start. Edited tracks are private local state;
review them before sharing.

## Recording a track

In a room, build a deck with learned cards, start recording, play a round manually, then stop recording and use “generate track from latest recording.” It saves under the current account's `.fvm-data/tracks/` or `.fvm-data/accounts/<profile>/tracks/`. The API route `/record-track?save=<name>` also generates a track; without `save`, it returns parsed data and track text only. These generated files can be removed by the per-account cleanup dialog.

`RecordingReader` takes the last room deck before Start and matches learned 42×50 icons. It interprets a card-slot click followed by a grid click as a placement, checks the frame at mouse-down, looks at neighboring cells near grid borders, and confirms visible changes for up to 5 seconds (with a longer fallback window). A repeated grid click within four seconds can be the same card's retry. It deduplicates card/cell pairs and writes a UTF-8 preset track, using known layer/cooldown values where possible. A recording with several rounds uses the round with the most placements.

Generated tracks encode placement order and cells, but not wave-dependent formations or conditions. Temporary manual placements and shovels may be included, so inspect and edit the result before relying on it. Two manually recorded 曲奇 runs were checked against their frames: one included 11 日光炉 cells, 18 红柳烤串机 cells, and 10 shovel actions; the other included 6 and 8 cells with the character at 33.

Newly generated shovel rows include `独` because recording times use elapsed battle
time. Older generated files are unchanged; a timed shovel row without `独` uses
synchronized track time when shovels are enabled.

## Renderer-loss safety

In off-screen mode, a renderer termination invalidates capture and input even
when CEF still exposes an initialized browser and a cached screenshot.
`/status` reports `browserReady=false`, `rendererReady=false`, the termination
reason, and no cached Flash location. The viewer clears the old image; capture,
manual input, and automation fail closed rather than recognizing that image
as a live game screen.

Reload or reopen the page manually after checking the failure and available
memory. Recovery needs a new main-frame navigation after the failure, successful
load completion, and a subsequent viewport paint. A late callback or popup-only
paint cannot restore readiness. This guard does not resume stopped battles,
restart queues, or establish that an out-of-memory root cause has been fixed.

## Track and board rules

The track editor's local data is under `vendor\track`. It supplies `附加程序\文本\普通关卡.txt` (map coordinates and boss-key information), preset tracks, support layouts, and image templates. Locally converted track text is UTF-8 without BOM; the original GBK archive stays local. The application tries UTF-8 first and GBK on decode failure; generated tracks are UTF-8. `附加程序\文本\龙图.txt` is binary, despite its extension.

Water and sky cells may require a support card (`木盘子`, `棉花糖`, or `麦芽糖`). The application reads the matching `承载轨道` layout and waits for its own support before placing another card on that cell. Some water maps start with game-provided support; these cells can be used directly, but a rejected placement causes support to be replenished first. A support layout is matched by selected stage or the track's folder; missing layouts use the older placement rules.

`钢丝球` requires bare water. Its targets do not trigger automatic plate placement or repair. When a stage support layout is known, preflight rejects its land or cloud targets and any selected ordinary card or explicit support sharing those targets. Duo checks also reserve these cells against the other account's formation. An unused matching plate can still be carried to satisfy the game's room warning.

Fixed sky gaps are read from time-zero support entries. Timed-only layouts are also retained. An empty opening support row with `继` imports its matching default support schedule; `棉花糖` and `麦芽糖` are equivalent for this lookup. For example, an empty `麦芽糖` row with spec `7[1]继` uses the selected cloud stage's defaults. When the opening `继` row supplies cells, those locations remain in place while default times, independent/extreme timing flags, and conditions are imported by index. If that produces a timed `00` event, the row needs scheduled `补`; otherwise preflight blocks the unclickable cell. An empty row without `继` retains only the older fixed-cell fallback, and only for a matching support card.

Timed cloud steps activate their support dependency when due, invalidate remembered cards at a new cloud opening, and run ahead of ordinary `极` placements. Both timed and formation-based ordinary cards wait for support on an active gap. A failed support remains pending: its retry interval starts at 500 ms for six seconds after the first attempt, then becomes five seconds. A newly due step takes priority over old repairs, and a completed later repair of the same cell supersedes older ones. Support slots must be visibly ready for one second before an attempt. A late rejection restores the affected timed step without replaying other completed steps. A 2026-09-26 manual test of the earlier implementation placed all six explicit timed supports and won two 棉花日 rounds; that observation does not validate these changes.

The [track executor source review](track-editor-analysis.md) explains the implementation reference and the repaired failure paths. The author's source is cloned locally at `sample/FvmTrack`; it remains ignored and is not a build dependency.

On animated backgrounds, comparison against the initial frame alone cannot determine whether a cell is empty. The application still compares empty-looking cells in an animated column, but no longer permanently excludes an uncertain cell after one attempt. Unknown cells are probed in least-recently-tried order, at most one per card every ten seconds and with at least thirty seconds before retrying the same cell. This also reaches a final empty cell that has no empty neighbor.

For ordinary layer-3 cards, the first successful pick-up attempts learn foreground color features from two held-card frames after removing colors in two background frames. Features stay in memory for that battle and are checked inside each cell's interior to limit interference from adjacent cards. Negative feature evidence is used only after the card was recognized in that cell. Ownership is refreshed independently of target selection, including on full columns; at least three absence observations spanning one second are required to forget a card. If feature learning cannot validate an ordinary card on a cloud or recognized animated board, ownership becomes eligible for re-evaluation after 45 seconds and uses the bounded probe policy. Support and shield recognition retain their separate image comparisons.

Tracks may specify placement time (`035|49` means second 35 at cell 49) or a priority queue such as `优先队列=海星,海星,狮子座`. Both are supported. `绝` cards may bypass the priority queue; a support card can be placed to unblock its first target, and an unplaceable queue head is skipped after 30 seconds. A minimum flame count such as `海星 7[3]补极,0,0,200` is enforced when its digit masks are available. Wave-dependent changes and image conditions are not implemented.

## Recognition and retries

| Signal | Current method |
| --- | --- |
| Screen state | Small templates named `(x,y)name.png`, from the local track editor or `src/FvmAssistant/templates/` |
| Card availability | Brightness of the white cost label below a card (ready about 0.93, unavailable about 0.52); support waits for one second of observed readiness |
| Flame counter | Match locally installed 6×10 masks, then classify the visible top and side strokes when the live Flash font differs; unreadable or stale frames yield unknown |
| Card picked up | Cost label darkens; allow up to 700 ms when the game is busy |
| Placement confirmation | Probe the card slot after clicking the cell: a card still in hand can be put back, while a successfully placed card remains on cooldown; late server rejection is corrected on the next pick-up |
| Empty cell | Compare interior pixels with the start-of-battle frame, with special handling for animated backgrounds and support layers |
| Card still present | Validated foreground color features for ordinary cards, otherwise before/after image comparisons; multiple absence observations are required before replenishment |
| Drops | Sweep mouse points about 20 px apart, prioritizing the last two columns and flame-producing cards; make a full sweep every 45 seconds, or every 90 seconds with automatic flame pickup |

The room password box may be empty after a new login. The loop checks the checkbox and visible asterisks, reuses a remembered password if present, otherwise types a random four-digit room password and confirms it appeared. If entry fails, it creates a room with the 2P slot closed. The generated room password can appear in the local run log; do not share that log unredacted.

Boss continuation occurs in the same battle with the existing board and flames. The victory prompt times out after about eight seconds; the loop retries “continue” up to three times, then falls back to rewards if it does not re-enter battle. A missing boss key can bypass the prompt. A manual 曲奇 boss run on 2026-09-26 took about five minutes including the normal stage. Only the first chest flip is free; the loop uses that flip.

## Loading problems and known limits

The iframe (`td-4399.123u.com`) and resource CDN (`q.ms.huanlecdn.com`) can resolve to unreliable nodes. Startup probes several TLS connections and selects an available iframe IP. A stalled iframe is refreshed after 30 seconds; a stuck `加载中 100%` state is also refreshed after 30 seconds. Outside a grind, the loader watchdog manages refreshes; within a grind, the grind loop does. Repeated failures can require waiting or restarting to probe nodes again. Room card-list scrolling waits up to 1.2 seconds for a rendered change before declaring the list boundary, then allows another 150 ms for scrolling to settle; this prevents one delayed OSR frame from prematurely ending the search. If a remembered deck arrives during selection and introduces extra cards, deck building clears and rebuilds it once. A repeated change or an incorrect final deck stops the action.

The application does not yet recognize defeat reliably or perform wave-dependent or advanced conditional track actions. The restricted two-account mode has been verified for manually paired 曲奇 rooms, but automatic pairing still needs a live room-number recognition fix and is limited to a visible room-list entry. Basic conditions have synthetic regression coverage. On 2026-09-26, one live 曲奇 round with a temporary two-card track completed after a page refresh: the producer card and a card gated at 200 flames both had confirmed placements, and the loop returned to the room with one round completed. The counter was briefly unreadable during that battle, so this observation verifies recovery and a successful gated placement, not continuous reading accuracy or every condition mode. Before the refresh, room card-list loading timed out and a manual-slot retry could not leave the room. A non-victory battle ending stops the current run. These are implementation limits, not promises that a preset track will win a particular stage.
