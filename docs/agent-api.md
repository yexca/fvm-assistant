# Local Agent interface

The Agent interface adds resource discovery, solo-plan validation and identified
background runs to the existing off-screen automation. It uses the same track
preflight, card library, account operation lock, image recognition and CEF input
as the operation panel. No game-network protocol or login-storage inspection is
involved.

## Start the MCP bridge

Build with `scripts/build.ps1` or `make check`. Restart game instances to load the
new routes. Start each account in off-screen mode with the profile and port saved
in the controller, then log in and select its server. The controller window alone
does not run a control endpoint.

Configure a local MCP client to launch this stdio command:

```text
command: powershell.exe
arguments:
  -NoLogo
  -NoProfile
  -NonInteractive
  -ExecutionPolicy
  Bypass
  -File
  <absolute-repository-path>\scripts\agent-mcp.ps1
```

Use the actual absolute script path. The bridge requires Windows PowerShell 5.1
and .NET Framework already present on the supported Windows setup; no Node,
Python, SDK installation or extra listening port is required. It resolves the
default account metadata relative to its script, so the client's working
directory does not matter. It reads `.fvm-data/controller.json`, returning only
profile names and control ports. When no accounts are saved, it uses `main` on
port `18765`, matching the controller. It does not launch or log in to accounts.

For an instance launched outside the controller, pass `-AccountsFile` followed
by an absolute path to private local JSON with this shape:

```json
{
  "Accounts": [
    { "Profile": "main", "Port": 18765 },
    { "Profile": "secondary", "Port": 18766 }
  ]
}
```

Keep real account metadata out of Git. A configured port pointing at a different
profile is rejected by the game process before executing an Agent action.
The bridge never accepts a tool-supplied host, URL or port and does not follow
HTTP redirects.

The bridge implements MCP stdio initialization, ping, tools/list and tools/call,
negotiating protocol `2025-06-18` or `2025-11-25`. Tool schemas reject unknown
fields and wrong types. Only JSON-RPC is written to stdout. Closing stdin exits
the bridge; an accepted game run continues in its owning game process. Run
cancellation is an explicit `stop_run` call, not an MCP connection shutdown.
Subscriptions, MCP task extensions and server-initiated requests are not exposed.

## Tools and workflow

| Tool | Purpose |
| --- | --- |
| `list_accounts` | Configured profiles and ports; does not assert that they are running |
| `get_account_state` | Instance ID, browser/renderer readiness, fresh recognized screen, busy state, active Agent run ID and startup availability |
| `list_levels` | Stage IDs and boss availability |
| `list_tracks` | Preset, generated and edited track IDs for this account; optional stage suggestions |
| `get_track` | Effective track card names, character cells and compatibility diagnostics for a stage |
| `list_cards` | This account's learned card icon names |
| `validate_plan` | Track, mapping, learned-image and boss compatibility without game input |
| `start_run` | Accept an identified solo plan and start it in the game process |
| `get_run` | Query by exactly one `runId` or original `requestId` |
| `stop_run` | Stop that run immediately or after its current round |
| `capture_screen` | Return a private current game-stage PNG as native MCP image content |

All tools except `list_accounts` require `profile`. Catalog tools accept `offset`
and `limit` (default 50, maximum 100), returning `items`, `total` and `nextOffset`.
Follow `nextOffset` until it is null. Track filtering is based on existing stage
suggestions and saved controller stage associations; suggestions are not a
guarantee of compatibility or victory. Omit `level` on `list_tracks` to see other
tracks, then inspect and validate the intended combination.

A typical sequence is:

1. `list_accounts`, then `get_account_state` for the selected profile.
2. Discover the exact stage, track and learned-card names; use `get_track` to
   inspect the effective deck, including inherited supports.
3. Call `validate_plan` with an explicit stage and ordered card mappings.
4. If validation passes and account state permits startup, call `start_run` with
   that plan, the current `instanceId`, and a new caller-chosen `requestId`.
5. Retain both IDs and poll `get_run`, for example every two seconds. The game
   process continues running between calls.
6. On an unexpected result, inspect state and optionally request a private
   capture before deciding the next action.

Example `start_run` arguments, with discovered values substituted:

```json
{
  "profile": "main",
  "instanceId": "<instance ID from get_account_state>",
  "requestId": "solo-cookie-001",
  "plan": {
    "level": "美味/曲奇",
    "trackId": "preset:通用轨道/6美味/1曲奇.txt",
    "cards": [
      { "trackCard": "小火炉", "learnedCard": "日光炉" },
      { "trackCard": "海星", "learnedCard": "红柳烤串机" }
    ],
    "rounds": 3,
    "boss": false,
    "shovel": false,
    "autoFlame": false
  }
}
```

`rounds` defaults to 1 and accepts integers from 1 to 500. The boolean options
default to false. Boss continuation consumes a key. Supply 1-11 unique track
cards and unique learned icons in deck order. Unmapped track cards retain the
existing preflight warnings. `trackId` must come from this account's catalog;
the Agent interface does not accept arbitrary file paths. Stage IDs always use
the full `island/stage` form. This first version deliberately requires a stage
even when starting in a room, letting the existing loop leave and navigate to
the requested stage.

Validation is read-only and does not prove login, account eligibility, possession
of boss keys or that a formation will win. Starting revalidates the current local
data and requires a recognized `Lobby`, `WorldMap` or `Room` screen. Other screens
need attended handling. Actual navigation and input retain the executor's own
screen guards. A validation result is not a frozen copy of track/card files;
changing them requires another validation.

## Run identity and outcomes

Each game process has an `instanceId`; each accepted request has a `runId`.
The same `requestId` and equivalent plan return the original run, including when
startup failed. Changing the plan under an existing request ID returns
`REQUEST_CONFLICT`. After a lost start response, query `get_run` by the original
`requestId`; retry the identical start only if necessary. Do not generate a new
request ID merely because an HTTP call timed out.

Deduplication and history live in the game process, independent of the bridge.
Restarting the bridge preserves them. Restarting the game process loses them and
changes `instanceId`; an old start request receives `INSTANCE_CHANGED`. Inspect
the account before submitting new work on a replacement instance. Up to 64
accepted requests are retained without eviction. At that limit new requests
receive `RUN_HISTORY_FULL`, while old queries and retries still work. Finish
work before restarting the game process to reset this history.

Run results contain `status`, executor `phase`, requested/completed normal and
boss round counts, last recognized screen, the finish-round flag and an
`errorCode`. `phase` and screen describe the executor's latest observation;
`get_account_state` samples a new screen separately.

| Status | Meaning |
| --- | --- |
| `starting` | Accepted; startup validation and readiness checks are pending |
| `running` | The executor is navigating, fighting or collecting rewards |
| `completed` | The executor finished its configured flow |
| `stopped` | Immediate cancellation or an early finish after rewards; inspect completed counts |
| `failed` | Startup or execution failed; inspect state before taking another action |

Startup errors include `PLAN_INVALID`, `ACCOUNT_BUSY`, `SCREEN_BLOCKED` and
`START_FAILED`. Executor failure reports `RUN_FAILED`; raw private log text is
not forwarded. `needsAttention` is true for failures. Completed battles do not
prove a game mission has been completed; mission-board evidence remains required.

`stop_run` requires a specific `runId` and mode `immediate` or `finish-round`.
The latter preserves the current round's configured boss/reward flow and returns
to town. The account operation lock and exact loop identity prevent a delayed
stop from cancelling a newer UI, legacy API or Agent run. A replacement returns
`RUN_REPLACED`. If startup has not attached an executor yet, query again after
`RUN_NOT_STARTED`; the stop has not been queued. Immediate stop may take time to
settle, so poll until the run is terminal.

The MCP tool list covers solo execution and observation. Existing task-board,
queue and paired-account scripts remain available through their documented
interfaces; this version does not add autonomous mission planning or expose
duo coordination gates to the model.

## Direct local HTTP access

The same contract is available under `/agent/v1/` on each off-screen process.
It retains the existing GET-only transport and `X-Fvm-Assistant: 1` header.
Requests with an Origin header are refused. Keep the endpoint on loopback and
do not expose or forward its port. The header is a local-script guard, not a
credential authenticating other local processes.

| Route | Query fields in addition to required `profile` |
| --- | --- |
| `state`, `capture` | None |
| `levels`, `cards` | Optional `offset`, `limit` |
| `tracks` | Optional `level`, `offset`, `limit` |
| `track` | `trackId`, `level` |
| `validate` | URL-encoded JSON `plan` |
| `start` | URL-encoded JSON `plan`, `instanceId`, `requestId` |
| `run` | Exactly one `runId` or `requestId` |
| `stop` | `runId`, `mode` |

```powershell
.\scripts\assistant.ps1 '/agent/v1/state?profile=main'
.\scripts\assistant.ps1 '/agent/v1/levels?profile=main&limit=20'
```

Responses use `{ ok, apiVersion, profile, instanceId, data }` or
`{ ok: false, apiVersion, profile, instanceId, error: { code, message } }`.
Application errors use this envelope even with HTTP 200; always inspect `ok`.
Transport failures still use the legacy HTTP error response. The bridge converts
application errors into MCP `isError` tool results and returns both text and
structured content. Captures use a separate image block instead of duplicating
base64 in the text result.

GET requests retain the server's 16 KiB request-header limit. The bridge rejects
encoded request URLs over 14,000 bytes before sending them. Long Unicode paths
and card names can reach that limit; the first version does not support large
queue documents or POST bodies.

## Privacy and verification

Ordinary responses omit entry URLs, cookies, browser storage, absolute runtime
paths and log messages. Resource names and plans may still identify an account.
`capture_screen` explicitly returns private game pixels, potentially including
account names or room passwords, to the requesting MCP client. Captures are
encoded in memory and are not saved by this interface. Review/redact any content
before sharing it, and follow [the security policy](security.md).

`make check` includes synthetic Agent plan, catalog, preflight, request-identity,
stale-stop and MCP subprocess/loopback tests. The MCP fixture uses temporary
account metadata and a synthetic local service, not a real game account. These
checks do not verify live gameplay, image correctness, mission completion or a
particular MCP client's UI integration.
