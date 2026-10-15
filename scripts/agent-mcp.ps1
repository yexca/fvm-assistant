<#
.SYNOPSIS
    Local stdio MCP bridge for the versioned FvmAssistant Agent API. Requires Windows PowerShell 5.1.
.DESCRIPTION
    Only JSON-RPC is written to stdout. Account discovery reads controller metadata, never browser profiles.
    Start the game instances separately and log in before running plans. See docs/agent-api.md.
#>
param([string]$AccountsFile = (Join-Path $PSScriptRoot '..\.fvm-data\controller.json'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$script:json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$script:json.MaxJsonLength = 8388608
$script:json.RecursionLimit = 32
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$stdioReader = New-Object IO.StreamReader([Console]::OpenStandardInput(), $utf8, $true)

function Object-Schema([hashtable]$Properties, [string[]]$Required = @()) {
    return @{ type = 'object'; properties = $Properties; required = @($Required); additionalProperties = $false }
}
function String-Schema([string]$Description) {
    return @{ type = 'string'; minLength = 1; maxLength = 512; description = $Description }
}
function Tool([string]$Name, [string]$Description, [hashtable]$Schema, [bool]$ReadOnly = $true) {
    return @{ name = $Name; description = $Description; inputSchema = $Schema
        annotations = @{ readOnlyHint = $ReadOnly; destructiveHint = (-not $ReadOnly); idempotentHint = $true; openWorldHint = $false } }
}
$profileSchema = String-Schema 'Configured account profile from list_accounts.'
$pageProperties = @{ profile = $profileSchema; offset = @{ type = 'integer'; minimum = 0 }; limit = @{ type = 'integer'; minimum = 1; maximum = 100 } }
$trackProperties = $pageProperties.Clone()
$trackProperties.level = String-Schema 'Optional island/stage ID; filters suggestions, not proven winning tracks.'
$mapping = Object-Schema @{ trackCard = (String-Schema 'Exact effective track card name.'); learnedCard = (String-Schema 'Exact learned icon name for this account.') } @('trackCard', 'learnedCard')
$planSchema = Object-Schema @{
    level = (String-Schema 'Exact island/stage ID from list_levels; required even when already in a room.')
    trackId = (String-Schema 'Track ID from list_tracks; absolute file paths are not accepted.')
    cards = @{ type = 'array'; minItems = 1; maxItems = 11; items = $mapping }
    rounds = @{ type = 'integer'; minimum = 1; maximum = 500; description = 'Default 1.' }
    boss = @{ type = 'boolean'; description = 'Default false. Boss continuation consumes a boss key.' }
    shovel = @{ type = 'boolean'; description = 'Default false.' }
    autoFlame = @{ type = 'boolean'; description = 'Default false. Enable only for an account with automatic flame pickup.' }
} @('level', 'trackId', 'cards')
$validateProperties = @{ profile = $profileSchema; plan = $planSchema }
$startProperties = $validateProperties.Clone()
$startProperties.instanceId = String-Schema 'Current instanceId from get_account_state. Never replace it to replay a request after restart.'
$startProperties.requestId = @{ type = 'string'; pattern = '^[A-Za-z0-9_-]{1,80}$'; description = 'Unique caller ID. Reuse this ID and the identical plan after a timeout; query by requestId first.' }
$script:toolList = @(
    (Tool 'list_accounts' 'List configured profiles and ports. This does not launch instances or inspect login storage.' (Object-Schema @{}))
    (Tool 'get_account_state' 'Inspect browser readiness, fresh recognized screen, instance identity and availability.' (Object-Schema @{ profile = $profileSchema } @('profile')))
    (Tool 'capture_screen' 'Return the current private game-stage image for attended inspection. It may expose account information or room passwords; do not share it. No game input or file reads.' (Object-Schema @{ profile = $profileSchema } @('profile')))
    (Tool 'list_levels' 'List local stage IDs and whether boss continuation exists. Paginated.' (Object-Schema $pageProperties @('profile')))
    (Tool 'list_tracks' 'List preset and private track IDs for this account. Paginated; stage filtering provides suggestions only.' (Object-Schema $trackProperties @('profile')))
    (Tool 'get_track' 'Inspect effective card names, character cells and compatibility diagnostics for a track and stage.' (Object-Schema @{ profile = $profileSchema; trackId = (String-Schema 'Listed track ID.'); level = (String-Schema 'Exact island/stage ID.') } @('profile', 'trackId', 'level')))
    (Tool 'list_cards' 'List learned card icon names for this account. Paginated.' (Object-Schema $pageProperties @('profile')))
    (Tool 'validate_plan' 'Validate a solo plan without game input. Checks track support and learned icons, not the ability to win or current login readiness.' (Object-Schema $validateProperties @('profile', 'plan')))
    (Tool 'start_run' 'Start a validated solo plan asynchronously. Poll get_run; the game process owns execution. Repeated requestId with identical plan returns the same run.' (Object-Schema $startProperties @('profile', 'plan', 'instanceId', 'requestId')) $false)
    (Tool 'get_run' 'Query an accepted run using exactly one runId or requestId, including after a lost start response. History lasts for the game process lifetime.' (Object-Schema @{ profile = $profileSchema; runId = (String-Schema 'Accepted run ID.'); requestId = (String-Schema 'Original start request ID.') } @('profile')))
    (Tool 'stop_run' 'Stop only the identified Agent run. finish-round collects current rewards and returns to town; immediate cancels now. Does not stop a replacement or paired run.' (Object-Schema @{ profile = $profileSchema; runId = (String-Schema 'Accepted run ID.'); mode = @{ type = 'string'; enum = @('finish-round', 'immediate') } } @('profile', 'runId', 'mode')) $false)
)

function Check-Value($Value, $Schema, [string]$Path) {
    switch ($Schema.type) {
        'object' {
            if ($Value -isnot [System.Collections.IDictionary]) { throw "INVALID_ARGUMENT: $Path must be an object." }
            foreach ($key in $Schema.required) { if (-not $Value.ContainsKey($key)) { throw "INVALID_ARGUMENT: $Path needs $key." } }
            foreach ($key in $Value.Keys) {
                if (-not $Schema.properties.ContainsKey($key)) { throw "INVALID_ARGUMENT: Unexpected field at $Path." }
                Check-Value $Value[$key] $Schema.properties[$key] "$Path.$key"
            }
        }
        'array' {
            if ($Value -isnot [System.Collections.IList] -or $Value.Count -lt $Schema.minItems -or $Value.Count -gt $Schema.maxItems) { throw "INVALID_ARGUMENT: Invalid array at $Path." }
            foreach ($item in $Value) { Check-Value $item $Schema.items "$Path[]" }
        }
        'string' {
            if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "INVALID_ARGUMENT: $Path must be a nonempty string." }
            if ($Schema.maxLength -and $Value.Length -gt $Schema.maxLength) { throw "INVALID_ARGUMENT: $Path is too long." }
            if ($Schema.pattern -and $Value -cnotmatch $Schema.pattern) { throw "INVALID_ARGUMENT: Invalid format at $Path." }
            if ($Schema.enum -and $Value -cnotin $Schema.enum) { throw "INVALID_ARGUMENT: Invalid choice at $Path." }
        }
        'integer' {
            if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -lt $Schema.minimum -or ($Schema.ContainsKey('maximum') -and $Value -gt $Schema.maximum)) { throw "INVALID_ARGUMENT: Invalid integer at $Path." }
        }
        'boolean' { if ($Value -isnot [bool]) { throw "INVALID_ARGUMENT: $Path must be a boolean." } }
    }
}

function Read-Accounts {
    try {
        $accounts = @(@{ Profile = 'main'; Port = 18765 })
        if ([IO.File]::Exists($AccountsFile)) {
            $config = $script:json.DeserializeObject([IO.File]::ReadAllText($AccountsFile, [Text.Encoding]::UTF8))
            if ($config -isnot [System.Collections.IDictionary]) { throw 'Invalid metadata.' }
            if ($config.Accounts.Count -gt 0) { $accounts = @($config.Accounts) }
        }
        $profiles = @{}
        $ports = @{}
        foreach ($account in $accounts) {
            if ($account.Profile -isnot [string] -or $account.Profile -cnotmatch '^[A-Za-z0-9_-]{1,32}$' -or
                $account.Port -isnot [int] -or $account.Port -lt 1024 -or $account.Port -gt 65535 -or
                $profiles.ContainsKey($account.Profile) -or $ports.ContainsKey([string]$account.Port)) { throw 'Invalid account metadata.' }
            $profiles[$account.Profile] = $true
            $ports[[string]$account.Port] = $true
            @{ profile = $account.Profile; port = $account.Port }
        }
    }
    catch { throw 'INVALID_ACCOUNT_CONFIG: Review the local controller account metadata.' }
}

function Invoke-AgentTool([string]$Name, $Arguments) {
    $definition = $script:toolList | Where-Object { $_.name -ceq $Name } | Select-Object -First 1
    if (-not $definition) { throw 'UNKNOWN_TOOL: Tool is not available.' }
    Check-Value $Arguments $definition.inputSchema 'arguments'
    $accounts = @(Read-Accounts)
    if ($Name -eq 'list_accounts') { return @{ accounts = $accounts } }
    $account = $accounts | Where-Object { $_.profile -ieq $Arguments.profile } | Select-Object -First 1
    if (-not $account) { throw 'ACCOUNT_NOT_FOUND: Select a configured profile from list_accounts.' }
    if ($Name -eq 'get_run' -and ($Arguments.ContainsKey('runId') -eq $Arguments.ContainsKey('requestId'))) { throw 'INVALID_ARGUMENT: Supply exactly one runId or requestId.' }
    $routes = @{ get_account_state = 'state'; capture_screen = 'capture'; list_levels = 'levels'; list_tracks = 'tracks'; get_track = 'track'; list_cards = 'cards'; validate_plan = 'validate'; start_run = 'start'; get_run = 'run'; stop_run = 'stop' }
    $query = foreach ($key in ($Arguments.Keys | Sort-Object)) {
        $value = if ($key -eq 'plan') { $script:json.Serialize($Arguments[$key]) } else { [string]$Arguments[$key] }
        [Uri]::EscapeDataString($key) + '=' + [Uri]::EscapeDataString($value)
    }
    $uri = 'http://127.0.0.1:' + $account.port + '/agent/v1/' + $routes[$Name] + '?' + ($query -join '&')
    if ([Text.Encoding]::ASCII.GetByteCount($uri) -gt 14000) { throw 'REQUEST_TOO_LARGE: The encoded plan exceeds the local API request limit.' }
    $response = $null
    try {
        $request = [Net.HttpWebRequest]::Create($uri)
        $request.Proxy = $null
        $request.AllowAutoRedirect = $false
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 30000
        $request.Headers.Add('X-Fvm-Assistant', '1')
        $response = $request.GetResponse()
        if ([int]$response.StatusCode -ne 200) { throw 'Unexpected response.' }
        $reader = New-Object IO.StreamReader($response.GetResponseStream(), [Text.Encoding]::UTF8)
        try { $reply = $script:json.DeserializeObject($reader.ReadToEnd()) } finally { $reader.Dispose() }
        if ($reply -isnot [System.Collections.IDictionary] -or $reply.apiVersion -ne 1 -or
            $reply.profile -ine $account.profile -or $reply.ok -isnot [bool]) { throw 'Unexpected API response.' }
        return $reply
    }
    catch { throw 'INSTANCE_UNAVAILABLE: Check the configured game instance and its version. After a start timeout, query the original requestId before retrying.' }
    finally { if ($null -ne $response) { $response.Dispose() } }
}

function Tool-Result($Value, [bool]$Failed) {
    if (-not $Failed -and $Value.ContainsKey('data') -and $Value.data -is [System.Collections.IDictionary] -and $Value.data.ContainsKey('png')) {
        $png = $Value.data.png
        $Value.data.Remove('png') | Out-Null
        return @{ content = @(@{ type = 'text'; text = $script:json.Serialize($Value) }, @{ type = 'image'; mimeType = 'image/png'; data = $png }); structuredContent = $Value; isError = $false }
    }
    return @{ content = @(@{ type = 'text'; text = $script:json.Serialize($Value) }); structuredContent = $Value; isError = $Failed }
}
function Send-Reply($Reply) { [Console]::Out.WriteLine($script:json.Serialize($Reply)); [Console]::Out.Flush() }

$initialized = $false
$ready = $false
while ($null -ne ($line = $stdioReader.ReadLine())) {
    $rpc = $null
    try {
        if ($line.Length -gt 1048576) { throw 'Oversized message.' }
        $rpc = $script:json.DeserializeObject($line)
    }
    catch { Send-Reply @{ jsonrpc = '2.0'; id = $null; error = @{ code = -32700; message = 'Parse error' } }; continue }
    if ($rpc -isnot [System.Collections.IDictionary] -or $rpc.jsonrpc -cne '2.0' -or $rpc.method -isnot [string] -or
        ($rpc.ContainsKey('id') -and $rpc.id -isnot [string] -and $rpc.id -isnot [int] -and $rpc.id -isnot [long])) {
        Send-Reply @{ jsonrpc = '2.0'; id = $null; error = @{ code = -32600; message = 'Invalid request' } }; continue
    }
    if (-not $rpc.ContainsKey('id')) {
        if ($rpc.method -ceq 'notifications/initialized' -and $initialized) { $ready = $true }
        continue
    }
    $result = $null
    $rpcError = $null
    switch -CaseSensitive ($rpc.method) {
        'initialize' {
            if ($initialized -or $rpc.params -isnot [System.Collections.IDictionary] -or $rpc.params.protocolVersion -isnot [string]) {
                $rpcError = @{ code = -32602; message = 'Invalid initialize request' }
            } else {
                $version = if ($rpc.params.protocolVersion -in @('2025-06-18', '2025-11-25')) { $rpc.params.protocolVersion } else { '2025-11-25' }
                $result = @{ protocolVersion = $version; capabilities = @{ tools = @{ listChanged = $false } }; serverInfo = @{ name = 'fvm-assistant'; version = '1.0.0' }
                    instructions = 'Use list_accounts, get_account_state, catalogs and validate_plan before start_run. Retain instanceId, requestId and runId. Poll get_run. Login and unsupported screens require attended handling. Results and card names may be private.' }
                $initialized = $true
            }
        }
        'ping' { $result = @{} }
        'tools/list' {
            if (-not $ready) { $rpcError = @{ code = -32000; message = 'Initialize first' } }
            elseif ($rpc.params.cursor) { $rpcError = @{ code = -32602; message = 'No tool cursor is supported' } }
            else { $result = @{ tools = $script:toolList } }
        }
        'tools/call' {
            if (-not $ready) { $rpcError = @{ code = -32000; message = 'Initialize first' } }
            elseif ($rpc.params -isnot [System.Collections.IDictionary] -or $rpc.params.name -isnot [string]) { $rpcError = @{ code = -32602; message = 'Tool name is required' } }
            else {
                try {
                    $arguments = if ($rpc.params.ContainsKey('arguments')) { $rpc.params.arguments } else { @{} }
                    $value = Invoke-AgentTool $rpc.params.name $arguments
                    $result = Tool-Result $value ($value.ContainsKey('ok') -and -not $value.ok)
                }
                catch {
                    $message = $_.Exception.Message
                    $code = 'TOOL_ERROR'
                    if ($message -match '^([A-Z_]+): (.+)$') { $code = $Matches[1]; $message = $Matches[2] }
                    else { $message = 'The local tool could not complete.' }
                    $result = Tool-Result @{ ok = $false; error = @{ code = $code; message = $message } } $true
                }
            }
        }
        default { $rpcError = @{ code = -32601; message = 'Method not found' } }
    }
    if ($rpcError) { Send-Reply @{ jsonrpc = '2.0'; id = $rpc.id; error = $rpcError } }
    else { Send-Reply @{ jsonrpc = '2.0'; id = $rpc.id; result = $result } }
}
