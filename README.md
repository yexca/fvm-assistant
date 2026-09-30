# fvm-assistant

A Windows prototype for automating repeated stages in **美食大战老鼠** (the 4399 edition). It embeds a Flash-capable CefSharp browser, recognizes the game screen from images, and sends mouse events through CEF. It does not modify the game client or implement the game's network protocol.

The current application is **FvmAssistant**, a working prototype rather than a packaged release. Its off-screen mode can navigate from 美味镇 to a stage, build a deck, play a track, collect rewards, and repeat. See [automation behavior](docs/automation.md) and the [prototype findings](docs/prototype-findings.md) for tested behavior and known limits.

| Workflow | Current verification |
| --- | --- |
| Single-account 曲奇 stage loop | One live round completed with Victory, rewards, and return to 美味镇 |
| Two accounts already paired in one 曲奇 room | One live round and then two consecutive rounds completed on both accounts; see the [manual two-account guide](docs/two-account.md) |
| Friend invitation pairing and two-account 美味/神殿 boss | One live round completed by both accounts in a shared island area, including boss and room return; see [two-account guide](docs/two-account.md) |
| Automatic two-account room pairing with `-Level` | The host created a room, but live room-number recognition failed before the guest joined |
| Single-account task queue | Read-only `-DryRun` checked; a live queue sequence has not been run |
| Track preview and copy editor | Built and covered by deterministic tests; visual layout and live use have not been checked |

## Requirements

- Windows x64 and Visual Studio 2022 or Build Tools with MSBuild and .NET Framework 4.8 targeting support. The build script finds MSBuild through `vswhere.exe`; a standalone .NET SDK is not required.
- A locally obtained x64 CefFlashBrowser installation containing `Assets\Plugins\pepflashplayer.dll`. The plugin is intentionally excluded from Git.
- The track editor data under `vendor\track` for stage lists, templates, and preset tracks. This is local third-party data and is not part of the repository.
- GNU Make is optional; every task also has a PowerShell command.

## Build and run

From the repository root in PowerShell:

```powershell
.\scripts\fetch-flash-plugin.ps1 -CefFlashBrowserDir "<path to CefFlashBrowser x64>"
.\scripts\build.ps1
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode osr --profile main
```

To use the Makefile, run `make help`, `make check`, or `make release`. `make check` scans repository candidates for sensitive material, tests the scanner, builds the Debug configuration, and runs deterministic automation regressions with synthetic frames and input. `make test-automation` builds and runs those regressions alone. Without Make, run `scripts/build.ps1` followed by `scripts/test-automation.ps1`. There is no automated live-game test suite; the local Flash test procedure is in [docs/prototype-findings.md](docs/prototype-findings.md).

At first launch, log in manually and select a server. In off-screen mode, the right-hand automation panel can then select a stage, track, learned cards, and round count. The default control endpoint is on `127.0.0.1:18765`; `scripts\assistant.ps1 /help` lists its routes. Do not expose or forward that port.

The separate controller window starts without a browser and is also the default when the executable is opened without arguments:

```powershell
.\src\FvmAssistant\bin\x64\Debug\FvmAssistant.exe --mode controller
```

It manages account profiles and server choices, learned card icons, private tracks and success markers, stage queues, two-account runs, and per-account cleanup. A numeric server such as `<server-number>` is entered through the visible 4399 portal controls with CEF input; a saved direct server URL is also supported. The controller never inspects saved cookies. When a queue request opens a closed game window, it waits for a recognized game screen; if login has not completed, it asks you to log in. Two-account manual-room runs still require both accounts to be paired before starting.

For several stages in sequence, `scripts/queue.ps1` validates a local JSON plan and runs each stage only after the previous one finishes. See [task queues](docs/automation.md#single-account-task-queues). The panel can preview a track's formations and timeline and [edit a separate copy](docs/automation.md#editing-a-track-copy) without overwriting the imported preset.

The two-account runner uses two off-screen instances with different `--profile` and `--port` values. The verified path is to pair the accounts in one room manually, then run `scripts/duo.ps1`; follow the [manual two-account guide](docs/two-account.md). The `-Level` path is implemented but currently stops when it cannot read the live room number. See [two-account behavior](docs/automation.md#two-account-rooms) for plan restrictions and recovery.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/FvmAssistant/` | WinForms / .NET Framework 4.8 x64 application |
| `tests/AutomationTests/` | Deterministic C# regression harness |
| `tests/testpage/` | Local Flash input and capture test page |
| `scripts/` | Build, plugin copy, control API, and sensitive-data checks |
| `docs/` | Prototype, automation, and security notes |
| `vendor/` | Local Flash plugin and track editor data; ignored by Git |
| `sample/` | Local third-party reference material; ignored by Git |

New application state lives in the ignored `.fvm-data\` folder at the repository root, including login profiles, screenshots, recordings, card icons, tracks, controller settings, and logs. Those files may contain account information. Keep them out of Git and review any excerpt before sharing it. Read [the security policy](docs/security.md) before staging files or sharing diagnostics. The old `%LOCALAPPDATA%\FvmAssistant\` folder is not migrated or deleted automatically; close the application and copy the data you want to retain into `.fvm-data\` before removing the old folder.

The application was previously named FvmSpike. The old `scripts/spike.ps1` command remains as a compatibility entry point. New logs use the `assistant-YYYYMMDD.log` name.

## Development

Read [AGENTS.md](AGENTS.md) before changing the project. Run `make check` (or the equivalent PowerShell commands) after code changes. Before any future commit, run `make secrets` and review `git status --short` plus `git diff --cached --stat`. The optional local pre-commit hook can be activated with `make install-hooks`; it checks the exact staged content.

This repository is initialized locally. No commit is required to build or run the prototype.
