# TCL Phase 10 single-run CBATH/GABS driver. Run once per seeded script.
param(
    [int]$McpPort = 19580,
    [int]$CombatHoldSec = 55,
    [int]$PollTimeoutSec = 180,
    [string]$GameId = 'bannerlord148-nocb',
    [ValidateSet('census-baseline','h1-external-write','h2-suppression','h2-coverage','h3-route-b','rel-none','rel-reapply','rel-reapply-resetweights','rel-after-release')]
    [string]$ScriptName = 'census-baseline',
    [string]$EntityId = '',
    [string]$SeededScriptPath = '',
    [string]$RunDir = ''
)

$ErrorActionPreference = 'Stop'
$RepoDir = 'E:\MBmod\TacticalCommandLab'
$Tools = 'E:\MBmod\CombatBrain Autonomous test harness\tools'
$ProbeDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CommandLabProbe'
$ScriptDir = Join-Path $RepoDir 'deploy\CommandLabProbe\scripts'
$ExpectedModules = @('Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen','Native','SandBoxCore','Sandbox','StoryMode','CustomBattle','CombatBrainAutonomousTestHarness','CommandLabProbe')

if (-not $RunDir) { $RunDir = Join-Path $RepoDir ('_audit\_local-phase10-20261006\' + $ScriptName + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if (-not (Test-Path -LiteralPath $RepoDir -PathType Container)) { throw "Repository path missing: $RepoDir" }
if ($GameId -ne 'bannerlord148-nocb') { throw "Only configured no-CombatBrain experiment entry is allowed; got GameId=$GameId" }
$scriptSource = Join-Path $ScriptDir ($ScriptName + '.txt')
if (-not (Test-Path -LiteralPath $scriptSource -PathType Leaf)) { throw "Experiment script missing: $scriptSource" }
if ($ScriptName -ne 'census-baseline' -and -not $EntityId -and -not $SeededScriptPath) { throw 'ABORT: non-census scripts require -EntityId T{team}F{formation} or an explicit -SeededScriptPath.' }
if ($EntityId -and $EntityId -notmatch '^T\d+F\d+$') { throw "ABORT: EntityId must be a full census identity such as T1F3; got '$EntityId'." }
if ($SeededScriptPath -and -not (Test-Path -LiteralPath $SeededScriptPath -PathType Leaf)) { throw "SeededScriptPath missing: $SeededScriptPath" }

function Say([string]$m) { Write-Host "[$(Get-Date -Format HH:mm:ss)] $m" }
function Unescape([string]$s) { return $s -replace '\\"', '"' -replace '\\u003c', '<' -replace '\\u003e', '>' -replace '\\\\', '\' }
function AsObject([string]$json) {
    if (-not $json) { return $null }
    try {
        $outer = $json | ConvertFrom-Json
        $sc = $outer.result.structuredContent
        if ($sc -and ($sc.psobject.Properties.Count -gt 0)) { return $sc }
    } catch { }
    $m = [regex]::Match($json, '\\"text\\":\\"(?<p>\{.*?\})\\"')
    if (-not $m.Success) { $m = [regex]::Match($json, '"text":"(?<p>\{.*?\})"') }
    if (-not $m.Success) { return $null }
    try { return (Unescape $m.Groups['p'].Value) | ConvertFrom-Json } catch { return $null }
}
function Save-Json($value, [string]$path) {
    $json = ConvertTo-Json -InputObject $value -Depth 30
    [System.IO.File]::WriteAllText($path, $json, [System.Text.Encoding]::UTF8)
}
function Get-GameProcesses {
    return @(Get-Process -Name 'Bannerlord','Bannerlord.Native' -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited })
}
function Get-FileRecord([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return [pscustomobject]@{ path=$path; sha256=$hash; length=$item.Length; last_write_utc=$item.LastWriteTimeUtc.ToString('o') }
}
function Write-SourceManifest {
    $head = (& git -C $RepoDir rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to read source HEAD: $head" }
    Set-Content -LiteralPath (Join-Path $RunDir 'source-head.txt') -Value $head -Encoding ASCII
    $diff = (& git -C $RepoDir diff --no-ext-diff 2>&1 | Out-String)
    $cached = (& git -C $RepoDir diff --cached --no-ext-diff 2>&1 | Out-String)
    @("--- unstaged ---",$diff,"--- staged ---",$cached) | Set-Content -LiteralPath (Join-Path $RunDir 'git-diff.txt') -Encoding UTF8
    (& git -C $RepoDir status --short --branch --untracked-files=all 2>&1 | Out-String) | Set-Content -LiteralPath (Join-Path $RunDir 'git-status.txt') -Encoding UTF8
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $RunDir 'driver-script.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $RepoDir '_audit\_local-phase10-20261006\driver-acceptance.md') -Destination (Join-Path $RunDir 'driver-acceptance.md') -Force
    $dlls = @(Get-ChildItem -LiteralPath (Join-Path $RepoDir 'deploy\CommandLabProbe') -Filter '*.dll' -Recurse -File -ErrorAction SilentlyContinue)
    $records = @()
    foreach ($dll in $dlls) { try { $records += Get-FileRecord $dll.FullName } catch { $records += [pscustomobject]@{ path=$dll.FullName; error=$_.Exception.Message } } }
    Save-Json $records (Join-Path $RunDir 'source-dll-hashes.json')
}
function Capture-LoadedModules([int[]]$processIds) {
    $rows = @()
    foreach ($id in $processIds) {
        try {
            $proc = Get-Process -Id $id -ErrorAction Stop
            $start = $proc.StartTime.ToUniversalTime().ToString('o')
            $mods = @($proc.Modules | Where-Object { $_.ModuleName -match '^CommandLab\.(BannerlordProbe|Core|Telemetry)\.dll$' })
            foreach ($mod in $mods) {
                $record = [ordered]@{ pid=$id; process=$proc.ProcessName; process_start_utc=$start; module=$mod.ModuleName; path=$mod.FileName; sha256=$null }
                try { $record.sha256 = (Get-FileHash -LiteralPath $mod.FileName -Algorithm SHA256).Hash.ToLowerInvariant() } catch { $record.hash_error=$_.Exception.Message }
                $rows += [pscustomobject]$record
            }
        } catch { $rows += [pscustomobject]@{ pid=$id; inspection_error=$_.Exception.Message } }
    }
    if ($rows.Count -eq 0) { $rows = @([pscustomobject]@{ status='unknown'; reason='Get-Process.Modules exposed none of the managed CommandLab assemblies'; inspected_pids=$processIds }) }
    Save-Json $rows (Join-Path $RunDir 'loaded-modules.json')
}
function Invoke-Tool([string]$name, [int]$timeout = 30) {
    try {
        $raw = Invoke-McpHttp -Tool 'games_call_tool' -Arguments @{ gameId = $GameId; tool = $name; timeout = $timeout } -TimeoutSec ($timeout + 40)
    $safe = [regex]::Replace($name, '[\\/:*?"<>|]', '_')
        $script:RawSequence++
        $leaf = ('{0:D3}-{1}.json' -f $script:RawSequence,$safe)
        Set-Content -LiteralPath (Join-Path $RunDir ('raw-probe\' + $leaf)) -Value $raw -Encoding UTF8
        return $raw
    } catch {
        $script:RawSequence++
        $err = "tool=$name`ntime=$(Get-Date -Format o)`n$($_ | Out-String)"
        Add-Content -LiteralPath (Join-Path $RunDir 'exception.log') -Value $err -Encoding UTF8
        throw
    }
}
function Wait-For([string]$label, [scriptblock]$cond, [int]$seconds, [scriptblock]$poll) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $r = & $poll
        if ($r -and (& $cond $r)) { return $r }
        Start-Sleep -Seconds 3
    }
    Say "TIMEOUT waiting for $label"
    return $null
}

if (Test-Path -LiteralPath $RunDir -PathType Container) {
    if (@(Get-ChildItem -LiteralPath $RunDir -Force -ErrorAction SilentlyContinue).Count -gt 0) { throw "ABORT: refusing to overwrite non-empty evidence directory: $RunDir" }
}
elseif (Test-Path -LiteralPath $RunDir) { throw "ABORT: evidence path exists and is not a directory: $RunDir" }
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $RunDir 'raw-probe') -Force | Out-Null
New-Item -ItemType Directory -Path $ProbeDir -Force | Out-Null
Write-SourceManifest
$sourceToSeed = if ($SeededScriptPath) { (Resolve-Path -LiteralPath $SeededScriptPath).Path } else { $scriptSource }
Copy-Item -LiteralPath $sourceToSeed -Destination (Join-Path $RunDir 'script-source.txt') -Force
$scriptText = Get-Content -LiteralPath $sourceToSeed -Raw
$entityMap = [ordered]@{ script_name=$ScriptName; source_path=$sourceToSeed; target_entity=$(if ($EntityId) { $EntityId } else { $null }); replaced_tokens=@() }
if ($EntityId) {
    $scriptText = [regex]::Replace($scriptText, '(?m)^(\s*t=[^\r\n]*?\s+(?:acquire|cmd|release|renew)\s+)(?:N0|T\d+F\d+)(?=\s)', {
        param($match)
        $entityMap.replaced_tokens += $match.Value.Substring($match.Value.LastIndexOf(' ') + 1)
        return $match.Groups[1].Value + $EntityId
    })
}
Set-Content -LiteralPath (Join-Path $RunDir 'script-seeded.txt') -Value $scriptText -Encoding UTF8
Set-Content -LiteralPath (Join-Path $ProbeDir 'script.txt') -Value $scriptText -Encoding UTF8
Save-Json $entityMap (Join-Path $RunDir 'entity-mapping.json')
@("script=$ScriptName", "game_id=$GameId", "hold_seconds=$CombatHoldSec", "poll_timeout_seconds=$PollTimeoutSec", "seeded_utc=$((Get-Date).ToUniversalTime().ToString('o'))") | Set-Content -LiteralPath (Join-Path $RunDir 'run-params.txt') -Encoding UTF8
$probeBefore = @{}
Get-ChildItem -LiteralPath $ProbeDir -File -ErrorAction SilentlyContinue | ForEach-Object { $probeBefore[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }

$script:RawSequence = 0
$script:GabsPid = $null
$startedGameIds = @()
$missionStarted = $false
$missionEnded = $false
$gabsOpen = $false
$runFailed = $false
$baselineIds = @((Get-GameProcesses | ForEach-Object { $_.Id }))
. (Join-Path $Tools 'mcp-http.ps1')
try {
    # Refuse any pre-existing game before touching Steam or game state.
    $preexisting = Get-GameProcesses
    if ($preexisting.Count -gt 0) {
        $detail = ($preexisting | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ', '
        throw "ABORT: Bannerlord is already running ($detail); close it manually and rerun. No process was stopped."
    }
    $steam = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -ceq 'steam' } | Select-Object -First 1
    if (-not $steam) {
        $steamExe = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamExe
        if (-not $steamExe -and (Test-Path -LiteralPath 'D:\Program Files (x86)\Steam\Steam.exe')) { $steamExe = 'D:\Program Files (x86)\Steam\Steam.exe' }
        if (-not $steamExe) { throw 'ABORT: Steam is not running and SteamExe could not be resolved.' }
        Start-Process -FilePath $steamExe -ArgumentList '-silent' -WindowStyle Hidden | Out-Null
        Start-Sleep -Seconds 25
        $steam = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -ceq 'steam' } | Select-Object -First 1
        if (-not $steam) { throw 'ABORT: Steam did not start.' }
    }
    $script:GabsPid = Open-McpHttp -ListenPort $McpPort
    $gabsOpen = $true
    Say "gabs pid=$script:GabsPid"

    $baselineIds = @((Get-GameProcesses | ForEach-Object { $_.Id }))
    $startRaw = Invoke-McpHttp -Tool 'games_start' -Arguments @{ gameId = $GameId; timeout = 150 } -TimeoutSec 240
    Set-Content -LiteralPath (Join-Path $RunDir 'games-start.json') -Value $startRaw -Encoding UTF8
    if ($startRaw -match 'already_running|already running') { throw 'ABORT: games_start reported already_running.' }
    if (-not ($startRaw -match 'started_connected' -or $startRaw -match 'processStarted\\?":true')) { throw "ABORT: games_start did not confirm a started game: $startRaw" }
    $launchDeadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $launchDeadline) {
        $startedGameIds = @((Get-GameProcesses | Where-Object { $baselineIds -notcontains $_.Id } | ForEach-Object { $_.Id }))
        if ($startedGameIds.Count -gt 0) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($startedGameIds.Count -eq 0) { throw 'ABORT: could not identify a new Bannerlord PID started by this run.' }
    $gameProcessRows = @()
    foreach ($pidValue in $startedGameIds) {
        try { $p=Get-Process -Id $pidValue -ErrorAction Stop; $gameProcessRows += [pscustomobject]@{ pid=$pidValue; process=$p.ProcessName; start_utc=$p.StartTime.ToUniversalTime().ToString('o') } } catch { }
    }
    Save-Json $gameProcessRows (Join-Path $RunDir 'game-processes.json')
    $connRaw = Invoke-McpHttp -Tool 'games_connect' -Arguments @{ gameId = $GameId; timeout = 40; forceTakeover = $true } -TimeoutSec 100
    Set-Content -LiteralPath (Join-Path $RunDir 'games-connect.json') -Value $connRaw -Encoding UTF8

    $ping = Wait-For 'CBATH bridge' { param($r) $r.pong -eq $true } $PollTimeoutSec { AsObject (Invoke-Tool 'cbath/ping' 20) }
    if (-not $ping) { throw 'ABORT: CBATH bridge did not answer.' }
    $ident = AsObject (Invoke-Tool 'cbath/get_build_identity' 25)
    if (-not $ident -or -not $ident.active_modules -or -not $ident.game_version -or -not $ident.pid) { throw 'ABORT: runtime identity is missing required pid, game_version, or active_modules.' }
    if ($startedGameIds -notcontains [int]$ident.pid) { throw "ABORT: runtime identity PID $($ident.pid) is not the game process started by this run." }
    $startedGameIds = @([int]$ident.pid)
    $actualModules = @("$($ident.active_modules)".Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $moduleDiff = @(Compare-Object -ReferenceObject @($ExpectedModules | Sort-Object) -DifferenceObject @($actualModules | Sort-Object))
    if ($moduleDiff.Count -gt 0 -or $actualModules.Count -ne $ExpectedModules.Count) {
        Save-Json $ident (Join-Path $RunDir 'runtime-identity.json')
        throw "ABORT: active_modules failed exact 11-module whitelist: $($ident.active_modules)"
    }
    Save-Json $ident (Join-Path $RunDir 'runtime-identity.json')
    Set-Content -LiteralPath (Join-Path $RunDir 'game-version.txt') -Value $ident.game_version -Encoding ASCII
    Capture-LoadedModules $startedGameIds

    $gs = Wait-For 'main menu (InitialState)' { param($r) $r.activeGameState -match 'InitialState' } $PollTimeoutSec { AsObject (Invoke-Tool 'cbath/get_game_state' 25) }
    if (-not $gs) { throw 'ABORT: game never reached the main menu.' }
    $prov = AsObject (Invoke-Tool 'cbath/h3_provider_start' 40)
    if (-not $prov -or $prov.status -ne 'ok') { throw "ABORT: h3_provider_start failed: $($prov | ConvertTo-Json -Compress)" }
    $onScreen = Wait-For 'CustomBattleScreen' { param($r) $r.topScreenType -match 'CustomBattleScreen' } $PollTimeoutSec { AsObject (Invoke-Tool 'cbath/get_game_state' 25) }
    if (-not $onScreen) { throw 'ABORT: game never reached CustomBattleScreen.' }
    $st = AsObject (Invoke-Tool 'cbath/h3_start_from_screen' 40)
    if (-not $st -or $st.status -ne 'ok') { throw "ABORT: h3_start_from_screen failed: $($st | ConvertTo-Json -Compress)" }
    $ms = Wait-For 'mission_running' { param($r) $r.mission_running -eq $true } $PollTimeoutSec { AsObject (Invoke-Tool 'cbath/get_mission_state' 25) }
    if (-not $ms) { throw 'ABORT: mission never became active.' }
    $missionStarted = $true
    Say "mission running: $($ms.mission_name)"

    $af = $null
    $deadline = (Get-Date).AddSeconds($PollTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $af = AsObject (Invoke-Tool 'cbath/auto_finish_deployment' 30)
        if ($af -and $af.status -eq 'ok') { break }
        Start-Sleep -Seconds 2
    }
    if (-not $af -or $af.status -ne 'ok') { throw 'ABORT: deployment did not finish.' }
    $null = AsObject (Invoke-Tool 'cbath/delegate_team_to_ai' 30)
    $earlyRaw = Invoke-Tool 'cbath/get_all_formations' 25
    Set-Content -LiteralPath (Join-Path $RunDir 'early-formations.json') -Value $earlyRaw -Encoding UTF8

    Say "holding combat for $CombatHoldSec seconds"
    Start-Sleep -Seconds $CombatHoldSec
    $lateRaw = Invoke-Tool 'cbath/get_all_formations' 25
    Set-Content -LiteralPath (Join-Path $RunDir 'late-formations.json') -Value $lateRaw -Encoding UTF8

    $null = Invoke-Tool 'cbath/end_mission' 40
    $missionEnded = $true
    $run = $null
    $runDeadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $runDeadline) {
        $runRaw = Invoke-Tool 'cbath/get_last_run' 30
        $run = AsObject $runRaw
        if ($run -and $run.status -eq 'ok' -and $run.sample_count) { break }
        Start-Sleep -Seconds 2
    }
    if (-not $run -or $run.status -ne 'ok') { throw 'ABORT: get_last_run did not return a completed mission timeline.' }
    Save-Json $run (Join-Path $RunDir 'get-last-run.json')
    Capture-LoadedModules $startedGameIds
    Rename-Item -LiteralPath (Join-Path $RunDir 'loaded-modules.json') -NewName 'loaded-modules-after-mission.json' -Force
}
catch {
    $runFailed = $true
    $failure = "time=$(Get-Date -Format o)`n$($_ | Out-String)"
    Add-Content -LiteralPath (Join-Path $RunDir 'exception.log') -Value $failure -Encoding UTF8
    Say "RUN ERROR: $($_.Exception.Message)"
}
finally {
    if ($missionStarted -and -not $missionEnded -and $gabsOpen) {
        try { $null = Invoke-Tool 'cbath/end_mission' 40; $missionEnded = $true } catch { Add-Content -LiteralPath (Join-Path $RunDir 'exception.log') -Value ("end_mission cleanup`n" + ($_ | Out-String)) -Encoding UTF8 }
        if ($missionEnded) {
            try {
                $runRaw = Invoke-Tool 'cbath/get_last_run' 30
                Set-Content -LiteralPath (Join-Path $RunDir 'get-last-run.json') -Value $runRaw -Encoding UTF8
            } catch { }
        }
    }
    if ($startedGameIds.Count -gt 0 -and $gabsOpen) {
        try { $stopRaw = Invoke-McpHttp -Tool 'games_stop' -Arguments @{ gameId = $GameId } -TimeoutSec 180; Set-Content -LiteralPath (Join-Path $RunDir 'games-stop.json') -Value $stopRaw -Encoding UTF8 } catch { Add-Content -LiteralPath (Join-Path $RunDir 'exception.log') -Value ("games_stop`n" + ($_ | Out-String)) -Encoding UTF8 }
        Start-Sleep -Seconds 8
        foreach ($pidValue in $startedGameIds) {
            $p = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if ($p -and -not $p.HasExited) { Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($gabsOpen) { Close-McpHttp }

    $gameLogDir = 'C:\ProgramData\Mount and Blade II Bannerlord\logs'
    New-Item -ItemType Directory -Path (Join-Path $RunDir 'game-logs') -Force | Out-Null
    foreach ($pidValue in $startedGameIds) {
        if (Test-Path -LiteralPath $gameLogDir) {
            $pidPattern = '^(rgl_log|rgl_log_errors|watchdog_log)_' + [regex]::Escape([string]$pidValue) + '\.txt$'
            $pidLogs = Get-ChildItem -LiteralPath $gameLogDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $pidPattern }
            foreach ($log in $pidLogs) { Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $RunDir 'game-logs') -Force }
        }
    }

    $changedProbe = @()
    Get-ChildItem -LiteralPath $ProbeDir -File -ErrorAction SilentlyContinue | ForEach-Object {
        $h = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        if (-not $probeBefore.ContainsKey($_.FullName) -or $probeBefore[$_.FullName] -ne $h) { $changedProbe += $_ }
    }
    foreach ($file in $changedProbe) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $RunDir $file.Name) -Force }
    $residual = @(Get-Process -Name 'Bannerlord','Bannerlord.Native' -ErrorAction SilentlyContinue | Where-Object { $startedGameIds -contains $_.Id -and -not $_.HasExited } | ForEach-Object { $_.Id })
    Set-Content -LiteralPath (Join-Path $RunDir 'cleanup-result.txt') -Value $(if ($residual.Count -eq 0) { 'clean' } else { 'residual_owned_pids=' + ($residual -join ',') }) -Encoding ASCII
    $runEnd = Get-Date
    Save-Json ([pscustomobject]@{ status=$(if ($runFailed) { 'FAIL' } else { 'COMPLETE' }); script=$ScriptName; entity_id=$EntityId; game_id=$GameId; started_pids=$startedGameIds; ended_utc=$runEnd.ToUniversalTime().ToString('o') }) (Join-Path $RunDir 'run-result.json')
    $manifest = @()
    Get-ChildItem -LiteralPath $RunDir -File -Recurse | Where-Object { $_.Name -ne 'artifact-manifest.json' } | ForEach-Object {
        try { $manifest += Get-FileRecord $_.FullName } catch { $manifest += [pscustomobject]@{path=$_.FullName; error=$_.Exception.Message} }
    }
    Save-Json $manifest (Join-Path $RunDir 'artifact-manifest.json')
    Say "artifacts saved to $RunDir; status=$(if ($runFailed) { 'FAIL' } else { 'COMPLETE' })"
}
if ($runFailed) { exit 1 }
