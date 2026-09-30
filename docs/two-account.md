# Two-account 曲奇 guide

The verified workflow starts with both accounts **already in the same room**. One
account owns the room and supplies the main attack formation; the other can carry
only a support card. `scripts/duo.ps1` then rebuilds each deck, coordinates Ready
and Start, runs both tracks, handles both rewards, and repeats for the requested
number of rounds. A one-round run and a later two-round run completed on both
accounts on 2026-09-28. See [the test record](prototype-findings.md#2026-09-28-follow-up-test).

Automatic room pairing with `-Level` is **not ready for unattended use**. In the
live test, the host created a room, but the installed digit masks could not read
its room number in two frames, so the guest was never asked to join. Use the
manual-room workflow below until that recognition path is repaired and tested.

## Prepare the two profiles

Build the project as described in the [README](../README.md#build-and-run). Run
the following commands from the repository root, using separate PowerShell
windows to keep the two launches clear, and keep both FvmAssistant windows open:

```powershell
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode osr --profile main --port 18765
```

```powershell
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode osr --profile partner --port 18766
```

`partner` is only an example profile name. If the second account already uses a
different profile, restart it with that **same** profile name to retain its login
and learned cards. Never open the same profile in two processes. Log in manually,
select the same game server on both accounts, and enter the game. New profiles keep
cookies and learned card images separately under `.fvm-data/`;
see [security](security.md) before sharing anything from there.

Each profile must learn the card images that its own deck mapping names. In a
room, place a card in a known deck slot and use the panel's learn-current-deck
action. For example, when the partner's first slot is 小火炉, this command learns
it for port 18766:

```powershell
.\scripts\assistant.ps1 '/cards?learn=小火炉' -Port 18766
.\scripts\assistant.ps1 /cards -Port 18766
```

The main profile also needs learned images for the cards named on the right side
of `-HostCards` below. `/cards` lists what each profile has learned; learning an
image in one profile does not make it available in the other.

## Make a support track

This example puts the partner character at `71` and fills column 6 with 小火炉.
It was used in the live 曲奇 test and does not overlap the main preset's selected
card cells. Create the file under the ignored project runtime directory from a third
PowerShell window:

```powershell
$guestTrack = Join-Path (Get-Location) '.fvm-data\accounts\partner\edited-tracks\曲奇-小火炉辅助.txt'
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $guestTrack)) | Out-Null
$trackText = "人物1位置=71`n小火炉`t7[3]补极`t000|16`t000|26`t000|36`t000|46`t000|56`t000|66`t000|76`n"
if ([System.IO.File]::Exists($guestTrack)) { throw 'The support track already exists; inspect it before reusing it.' }
[System.IO.File]::WriteAllText($guestTrack, $trackText, [System.Text.UTF8Encoding]::new($false))
```

If the file already exists, set `$guestTrack` to that path and inspect its
contents; skip the creation block rather than overwriting a track you edited.
The track is a plan for this stage, not a guarantee of victory on another board.
If either account uses another track or character cell, inspect both formations
and let the coordinator's preflight reject overlaps before starting.

## Pair manually and run

The friend invitation path can pair two logged-in accounts from a lobby map on
the same server. The host and guest are sent to the requested island and area;
the host creates the stage room, invites the guest from a configured row of the
online Friends list, and the guest accepts the prompt. The guest row is
one-based and must identify the intended account. For 美味/神殿, area 11 and row 1
were checked live.

Two area-menu layouts have been observed: a sparse menu containing area 1
followed by area 11, and a contiguous menu containing all eleven areas. The
pairing script counts the visible gold row markers before resolving the
requested area's row. It rejects other layouts; keep this attended and inspect
the requested area afterward. `scripts/test-friend-pair.ps1` checks both layouts
with synthetic images without opening the game. The host explicitly selects
the Friends tab before using the configured friend row.

To pair and then run one boss round:

```powershell
$guestTrack = Join-Path (Get-Location) '.fvm-data\accounts\<guest-profile>\tracks\神殿-小火炉-第5列.txt'
.\scripts\duo.ps1 -HostPort 18765 -GuestPort 18766 -Level '美味/神殿' `
    -FriendInvite -Area 11 -FriendRow 1 -Boss -Rounds 1 `
    -HostTrack '通用轨道/6美味/7神殿.txt' -GuestTrack $guestTrack `
    -HostCards '小火炉:日光炉,海星:红柳烤串机' -GuestCards '小火炉:小火炉'
```

This uses visible CEF clicks and captures. The invitation prompt is accepted
only after the host sends it during this run. Room headers are compared before
the coordinator starts either battle. A changed game layout or a friend who
moves to another list row can stop pairing; inspect the two windows and update
the area or row before retrying.

### Already paired room

With both accounts on the same server, have `main` create the 曲奇 room and
`partner` join it manually. Confirm which account is the host. Leave both
characters in the room; do not press Ready or Start. Close invitation dialogs and
other overlays that cover the deck or room controls. A leftover invitation panel
blocked deck clearing in the first live attempt even though the screen detector
still recognized `Room`.

Run this from the repository root in the third PowerShell window. Change only
`-Rounds` to choose the number of consecutive rounds. The tested main mapping
uses 日光炉 for the preset's 小火炉 and 红柳烤串机 for 海星; the partner uses its own
learned 小火炉 icon.

```powershell
$guestTrack = Join-Path (Get-Location) '.fvm-data\accounts\partner\edited-tracks\曲奇-小火炉辅助.txt'
.\scripts\duo.ps1 -HostPort 18765 -GuestPort 18766 `
    -HostTrack '通用轨道/6美味/1曲奇.txt' -GuestTrack $guestTrack `
    -HostCards '小火炉:日光炉,海星:红柳烤串机' `
    -GuestCards '小火炉:小火炉' -Rounds 2
```

The script checks both tracks and selected card cells before it touches either
browser. It stops on incompatible tracks or missing learned card images. Once a
run starts, leave both game windows alone. The script prints each completed
round, and both accounts remain in the room after the final round. Do **not** add
`-Level` to this manually paired command: that selects automatic room pairing.

## Check progress or stop

The coordinator stops its partner when either account fails or times out. To
inspect the current state, use:

```powershell
.\scripts\assistant.ps1 /duo -Port 18765
.\scripts\assistant.ps1 /duo -Port 18766
```

To stop an active run on both accounts explicitly, use:

```powershell
.\scripts\assistant.ps1 '/duo?state=stop' -Port 18765
.\scripts\assistant.ps1 '/duo?state=stop' -Port 18766
```

`finished` with the requested `roundsDone` on **both** ports confirms the
coordinated run completed. With `-Boss`, require `bossRoundsDone` on both ports
as well. The coordinator waits for both victory prompts before releasing boss
continuation. `Room` alone does not confirm a win. If a run stops,
inspect the messages from each `/duo` response before retrying. Captures and
logs may contain account names or room passwords; keep them outside the
repository and redact them before sharing.

The current two-account plan accepts ordinary layer-3 cards on disjoint cells,
one distinct `人物1位置` per account, and a stage without a support-card layout.
It rejects shovel actions. Boss continuation can be requested with `-Boss`;
it requires a usable key for the selected stage. The room screen cannot prove
which stage was selected in manual mode, so verify the room yourself. Further
execution details and the automatic-pairing design are in
[automation behavior](automation.md#two-account-rooms).
