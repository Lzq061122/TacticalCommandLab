param(
    [string]$DriverPath = (Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) 'tools/run_phase10.ps1'),
    [string]$ChildCase = '',
    [string]$StatsPath = ''
)

$ErrorActionPreference = 'Stop'
$parseTokens = $null
$parseErrors = $null
$driverAst = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $DriverPath).Path, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw "Driver parse failed: $($parseErrors | Out-String)" }

$cleanupTry = $driverAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.TryStatementAst] -and
    $null -ne $node.Finally -and $node.Finally.Extent.Text.Contains('cleanup-result.txt')
}, $true)
if ($null -eq $cleanupTry) { throw 'Could not find the driver cleanup finally block.' }
$finallyCode = ($cleanupTry.Finally.Statements | ForEach-Object { $_.Extent.Text }) -join "`r`n"
$responseCheck = $driverAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-McpCleanupResponse'
}, $true)
if ($null -eq $responseCheck) { throw 'Could not find the cleanup MCP response checker.' }
Invoke-Expression $responseCheck.Extent.Text
$driverFooter = $driverAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
    $node.Extent.Text -match '^if\s*\(\s*\$runFailed\s*\)\s*\{\s*exit\s+1\s*\}$'
}, $true) | Select-Object -Last 1
if ($null -eq $driverFooter) { throw 'Could not find the driver runFailed exit footer.' }
$footerCode = $driverFooter.Extent.Text

function Get-Process {
    [CmdletBinding()]
    param([string[]]$Name, [int]$Id)
    if ($PSBoundParameters.ContainsKey('Id')) {
        return $script:MockProcesses | Where-Object { $_.Id -eq $Id }
    }
    return @($script:MockProcesses | Where-Object { $Name -contains $_.ProcessName })
}

function Stop-Process {
    [CmdletBinding()]
    param([int]$Id, [switch]$Force)
    $script:StopTargets += $Id
    if ($script:CaseName -eq 'stop-process-failure' -and $Id -eq $script:OwnedPid) {
        throw 'MOCK Stop-Process failure'
    }
    if ($script:CaseName -ne 'residual-owned-pid') {
        $process = $script:MockProcesses | Where-Object { $_.Id -eq $Id } | Select-Object -First 1
        if ($process) { $process.HasExited = $true }
    }
}

function Invoke-Tool {
    param([string]$name, [int]$timeout = 30)
    if ($name -eq 'cbath/end_mission' -and $script:CaseName -eq 'end-mission-failure') {
        throw 'MOCK end_mission failure'
    }
    $raw = if ($name -eq 'cbath/end_mission' -and $script:CaseName -eq 'end-mission-error-envelope') {
        '{"jsonrpc":"2.0","id":1,"result":{"isError":true,"content":[{"type":"text","text":"MOCK end_mission tool error"}]}}'
    } else { '{"jsonrpc":"2.0","id":1,"result":{"content":[]}}' }
    $rawDir = Join-Path $script:MockRunDir 'raw-probe'
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $rawDir -Force
    $rawLeaf = ($name -replace '[\\/:*?"<>|]', '_') + '.json'
    [System.IO.File]::WriteAllText((Join-Path $rawDir $rawLeaf), $raw, [System.Text.Encoding]::UTF8)
    return $raw
}

function Invoke-McpHttp {
    param([string]$Tool, $Arguments, [int]$TimeoutSec)
    if ($Tool -eq 'games_stop' -and $script:CaseName -eq 'games-stop-failure') {
        throw 'MOCK games_stop failure'
    }
    if ($Tool -eq 'games_stop' -and $script:CaseName -eq 'games-stop-error-envelope') {
        return '{"jsonrpc":"2.0","id":2,"error":{"code":-32000,"message":"MOCK games_stop JSON-RPC error"}}'
    }
    if ($Tool -eq 'games_stop' -and $script:CaseName -ne 'residual-owned-pid' -and $script:CaseName -ne 'stop-process-failure') {
        $owned = $script:MockProcesses | Where-Object { $_.Id -eq $script:OwnedPid } | Select-Object -First 1
        if ($owned) { $owned.HasExited = $true }
    }
    return '{"jsonrpc":"2.0","id":2,"result":{"structuredContent":{"code":"terminated"}}}'
}

function Close-McpHttp {}
function Start-Sleep { param([int]$Seconds) }
function Say { param([string]$m) }
function Save-Json($value, [string]$path) {
    [System.IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $value -Depth 30), [System.Text.Encoding]::UTF8)
}
function Get-FileRecord([string]$path) {
    $item = Microsoft.PowerShell.Management\Get-Item -LiteralPath $path
    return [pscustomobject]@{ path=$path; sha256=(Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); length=$item.Length; last_write_utc=$item.LastWriteTimeUtc.ToString('o') }
}
function Test-Path {
    [CmdletBinding()]
    param([string]$LiteralPath, [string]$PathType)
    $full = [System.IO.Path]::GetFullPath($LiteralPath)
    if (-not $full.StartsWith($script:MockRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($PathType) { return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
}

function Invoke-MockedCleanup([string]$caseName) {
    $script:CaseName = $caseName
    $script:OwnedPid = 41001
    $script:ExternalPid = 42002
    $script:StopTargets = @()
    $script:MockRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('tcl-phase10-cleanup-' + [guid]::NewGuid().ToString('N'))
    $script:MockRoot = [System.IO.Path]::GetFullPath($script:MockRoot)
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $script:MockRoot -Force
    $RunDir = Join-Path $script:MockRoot 'run'
    $script:MockRunDir = $RunDir
    $ProbeDir = Join-Path $script:MockRoot 'probe'
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $RunDir -Force
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $ProbeDir -Force
    $script:MockProcesses = @(
        [pscustomobject]@{ Id=$script:OwnedPid; ProcessName='Bannerlord'; HasExited=$false },
        [pscustomobject]@{ Id=$script:ExternalPid; ProcessName='Bannerlord'; HasExited=$false }
    )
    $GameId = 'mock-game-id'
    $ScriptName = 'mock-case'
    $EntityId = 'T1F0'
    $startedGameIds = @($script:OwnedPid)
    $missionStarted = $caseName -in @('end-mission-failure','end-mission-error-envelope')
    $missionEnded = -not $missionStarted
    $gabsOpen = $true
    $runFailed = $false
    $probeBefore = @{}

    Invoke-Expression $script:finallyCode

    $result = Get-Content -LiteralPath (Join-Path $RunDir 'run-result.json') -Raw | ConvertFrom-Json
    $cleanup = Get-Content -LiteralPath (Join-Path $RunDir 'cleanup-result.txt') -Raw
    $exception = if (Test-Path (Join-Path $RunDir 'exception.log')) { Get-Content -LiteralPath (Join-Path $RunDir 'exception.log') -Raw } else { '' }
    $output = [pscustomobject]@{
        case=$caseName; status=$result.status; cleanup=$cleanup.Trim()
        run_failed=[bool]$runFailed
        exception=$exception; stop_targets=@($script:StopTargets)
        owned_pid_alive=(-not (($script:MockProcesses | Where-Object { $_.Id -eq $script:OwnedPid }).HasExited))
        external_pid_alive=(-not (($script:MockProcesses | Where-Object { $_.Id -eq $script:ExternalPid }).HasExited))
        external_process_calls=0
        artifacts_exist=((Test-Path (Join-Path $RunDir 'run-result.json')) -and
            (Test-Path (Join-Path $RunDir 'cleanup-result.txt')) -and
            (Test-Path (Join-Path $RunDir 'artifact-manifest.json')))
    }
    $gameStopPath = Join-Path $RunDir 'games-stop.json'
    $rawProbeDir = Join-Path $RunDir 'raw-probe'
    if ($caseName -eq 'games-stop-error-envelope') {
        $output | Add-Member -NotePropertyName error_body_preserved -NotePropertyValue ((Get-Content -LiteralPath $gameStopPath -Raw) -match 'MOCK games_stop JSON-RPC error')
    } elseif ($caseName -eq 'end-mission-error-envelope') {
        $rawBody = Microsoft.PowerShell.Management\Get-ChildItem -LiteralPath $rawProbeDir -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
        $output | Add-Member -NotePropertyName error_body_preserved -NotePropertyValue (([string]::Join("`n", [string[]]$rawBody)) -match 'MOCK end_mission tool error')
    }
    $resolvedRoot = (Resolve-Path -LiteralPath $script:MockRoot).Path
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $resolvedRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the OS temp directory: $resolvedRoot"
    }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    return $output
}

function Assert-Case($actual, [string]$expectedStatus, [int]$expectedExit, [string]$name) {
    if ($actual.status -ne $expectedStatus -or $actual.exit -ne $expectedExit) {
        throw "$name produced $($actual.status)/$($actual.exit), expected $expectedStatus/$expectedExit. Details: $($actual.exception)"
    }
    if (-not $actual.artifacts_exist) { throw "$name did not finalize all required artifacts." }
    Write-Output ("PASS {0}: {1}/{2}; cleanup={3}" -f $name,$actual.status,$actual.exit,$actual.cleanup)
}

function Invoke-ChildCase([string]$caseName) {
    $caseRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('tcl-phase10-child-' + [guid]::NewGuid().ToString('N'))
    $null = Microsoft.PowerShell.Management\New-Item -ItemType Directory -Path $caseRoot -Force
    $statsPath = Join-Path $caseRoot 'stats.json'
    $stdoutPath = Join-Path $caseRoot 'stdout.txt'
    $stderrPath = Join-Path $caseRoot 'stderr.txt'
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $shellPath = Join-Path $PSHOME 'pwsh.exe'
        $arguments = @('-NoLogo','-NoProfile','-File',('"{0}"' -f $PSCommandPath),'-DriverPath',('"{0}"' -f $DriverPath),'-ChildCase',$caseName,'-StatsPath',('"{0}"' -f $statsPath))
    } else {
        $shellPath = Join-Path $PSHOME 'powershell.exe'
        $arguments = @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $PSCommandPath),'-DriverPath',('"{0}"' -f $DriverPath),'-ChildCase',$caseName,'-StatsPath',('"{0}"' -f $statsPath))
    }
    $proc = Start-Process -FilePath $shellPath -ArgumentList $arguments -PassThru -Wait -NoNewWindow `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $statsPath -PathType Leaf)) {
        $stdout = if (Microsoft.PowerShell.Management\Test-Path $stdoutPath) { Get-Content $stdoutPath -Raw } else { '' }
        $stderr = if (Microsoft.PowerShell.Management\Test-Path $stderrPath) { Get-Content $stderrPath -Raw } else { '' }
        throw "Child '$caseName' did not save stats (exit $($proc.ExitCode)). stdout=$stdout stderr=$stderr"
    }
    $output = Get-Content -LiteralPath $statsPath -Raw | ConvertFrom-Json
    $output | Add-Member -NotePropertyName exit -NotePropertyValue $proc.ExitCode
    $resolvedRoot = (Resolve-Path -LiteralPath $caseRoot).Path
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $resolvedRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the OS temp directory: $resolvedRoot"
    }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    return $output
}

if ($ChildCase) {
    if (-not $StatsPath) { throw 'Child mode requires StatsPath.' }
    $childResult = Invoke-MockedCleanup $ChildCase
    $childStats = [ordered]@{
        case = [string]$childResult.case
        status = [string]$childResult.status
        cleanup = [string]$childResult.cleanup
        run_failed = [bool]$childResult.run_failed
        exception = [string]$childResult.exception
        stop_targets = @($childResult.stop_targets)
        owned_pid_alive = [bool]$childResult.owned_pid_alive
        external_pid_alive = [bool]$childResult.external_pid_alive
        external_process_calls = [int]$childResult.external_process_calls
        artifacts_exist = [bool]$childResult.artifacts_exist
        error_body_preserved = [bool]$childResult.error_body_preserved
    }
    [System.IO.File]::WriteAllText($StatsPath, (ConvertTo-Json -InputObject $childStats -Depth 10), [System.Text.Encoding]::UTF8)
    $runFailed = [bool]$childResult.run_failed
    Invoke-Expression $footerCode
    return
}

$normal = Invoke-ChildCase 'normal'
Assert-Case $normal 'COMPLETE' 0 'normal cleanup'

$endMission = Invoke-ChildCase 'end-mission-failure'
Assert-Case $endMission 'FAIL' 1 'end_mission failure'
if ($endMission.exception -notmatch 'MOCK end_mission failure') { throw 'end_mission failure details were not preserved.' }

$endMissionEnvelope = Invoke-ChildCase 'end-mission-error-envelope'
Assert-Case $endMissionEnvelope 'FAIL' 1 'end_mission MCP error envelope'
if (-not $endMissionEnvelope.error_body_preserved -or $endMissionEnvelope.owned_pid_alive) {
    throw "end_mission MCP error body preserved=$($endMissionEnvelope.error_body_preserved), owned_pid_alive=$($endMissionEnvelope.owned_pid_alive)."
}

$gamesStop = Invoke-ChildCase 'games-stop-failure'
Assert-Case $gamesStop 'FAIL' 1 'games_stop failure'
if ($gamesStop.exception -notmatch 'MOCK games_stop failure') { throw 'games_stop failure details were not preserved.' }

$gamesStopEnvelope = Invoke-ChildCase 'games-stop-error-envelope'
Assert-Case $gamesStopEnvelope 'FAIL' 1 'games_stop MCP error envelope'
if (-not $gamesStopEnvelope.error_body_preserved -or $gamesStopEnvelope.owned_pid_alive) {
    throw "games_stop MCP error body preserved=$($gamesStopEnvelope.error_body_preserved), owned_pid_alive=$($gamesStopEnvelope.owned_pid_alive)."
}

$stopFailure = Invoke-ChildCase 'stop-process-failure'
Assert-Case $stopFailure 'FAIL' 1 'Stop-Process failure'
if ($stopFailure.exception -notmatch 'MOCK Stop-Process failure' -or $stopFailure.stop_targets -notcontains 41001) {
    throw 'Stop-Process failure or owned PID was not preserved.'
}

$residual = Invoke-ChildCase 'residual-owned-pid'
Assert-Case $residual 'FAIL' 1 'residual owned PID'
if ($residual.cleanup -ne 'residual_owned_pids=41001' -or $residual.exception -notmatch '41001') {
    throw 'Residual owned PID was not preserved.'
}

$external = Invoke-ChildCase 'external-pid'
Assert-Case $external 'COMPLETE' 0 'external PID isolation'
if (-not $external.external_pid_alive -or $external.stop_targets -contains 42002) {
    throw 'Cleanup affected the unrelated external PID.'
}

Write-Output 'All isolated cleanup AST cases passed; only isolated PowerShell test children ran; mocked Get-Process/Stop-Process and no external MCP, Steam, or game call.'
