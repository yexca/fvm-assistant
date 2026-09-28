# FvmAssistant prototype findings

The prototype, originally named FvmSpike, was built to answer three questions: can an embedded Flash browser enter the 4399 game, can it capture moving game frames while hidden or minimized, and can it click the game without moving the real pointer? Tests on 2026-09-25 answered yes for off-screen rendering plus CEF-injected mouse events. The application is now named FvmAssistant.

## Components and operation

| Component | Details |
| --- | --- |
| Browser | CefSharp 84.4.10, based on the last Chromium generation that supports PPAPI Flash |
| Flash | Local `pepflashplayer.dll` 34.0.0.330, copied from CefFlashBrowser into ignored `vendor/pepflash/` |
| `--mode windowed` | Standard CEF child window; useful for comparison |
| `--mode osr` | Off-screen rendering into memory, then painted in the WinForms window; required for the automation panel |
| Local API | `127.0.0.1:18765`, requires `X-Fvm-Assistant` (legacy `X-Fvm-Spike` remains accepted), rejects requests with an `Origin` header |
| Runtime data | `%LOCALAPPDATA%\FvmAssistant\`: browser profiles, captures, logs, learned card icons, and panel settings |

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

On 2026-09-25, a user manually logged into the configured server from the 4399 page. The first game iframe request timed out and a refresh recovered it. The embedded stage was located at page coordinate `(165,0)` with size 950×596 in a 1280×800 viewport. Captures kept updating while minimized, and injected hover/click events opened and closed the in-game inventory without using the real pointer. CefSharp off-screen mode is muted by default.

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
