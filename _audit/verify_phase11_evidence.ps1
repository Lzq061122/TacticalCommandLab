param(
  [string]$Root = 'E:\MBmod\TacticalCommandLab\_audit\_local-phase10-20261006',
  [string]$OutDir = 'E:\MBmod\TacticalCommandLab\_audit\2026-10-07-phase11-evidence'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$runs = @(
  'run04-census-final',
  'run05-h2-suppression',
  'run06-h2-coverage',
  'run07-h3-route-b',
  'run08-rel-none',
  'run09-rel-reapply',
  'run10-rel-reapply-resetweights',
  'run11-rel-after-release'
)

function Get-Sha256Lower([string]$p) {
  (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLower()
}

$manifestResults = @()
$assessmentResults = @()

foreach ($r in $runs) {
  $dir = Join-Path $Root $r
  $mp = Join-Path $dir 'artifact-manifest.json'

  # --- manifest check ---
  $entries = Get-Content -LiteralPath $mp -Raw | ConvertFrom-Json
  $arrayName = 'array'

  $missing = 0; $mismatch = 0; $ok = 0; $bad = @()
  foreach ($e in $entries) {
    $rel = $e.relative_path
    $wantHash = $e.sha256
    $wantLen = $e.length
    $full = Join-Path $dir $rel
    if (-not (Test-Path -LiteralPath $full)) { $missing++; $bad += "MISSING:$rel"; continue }
    $len = (Get-Item -LiteralPath $full).Length
    if ($wantLen -ne $null -and $len -ne $wantLen) { $mismatch++; $bad += "LEN:$rel want=$wantLen got=$len"; continue }
    if ($wantHash) {
      $h = Get-Sha256Lower $full
      if ($h -ne $wantHash.ToLower()) { $mismatch++; $bad += "HASH:$rel want=$wantHash got=$h"; continue }
    }
    $ok++
  }
  $manifestResults += [pscustomobject]@{
    run=$r; entriesField=$arrayName; entries=$entries.Count; ok=$ok; missing=$missing; mismatch=$mismatch; bad=($bad -join '; ')
  }

  # --- assessment check ---
  $ap = Join-Path $dir 'assessment.json'
  $a = Get-Content -LiteralPath $ap -Raw | ConvertFrom-Json
  $mName = $a.metrics; $tName = $a.telemetry
  $mPath = Join-Path $dir $mName; $tPath = Join-Path $dir $tName
  $mOk = 'n/a'; $tOk = 'n/a'
  if (Test-Path -LiteralPath $mPath) { $mOk = ((Get-Sha256Lower $mPath) -eq $a.metrics_sha256.ToLower()) }
  else { $mOk = 'MISSING' }
  if (Test-Path -LiteralPath $tPath) { $tOk = ((Get-Sha256Lower $tPath) -eq $a.telemetry_sha256.ToLower()) }
  else { $tOk = 'MISSING' }
  # script hash: assessment.script_sha256 refers to the seeded script
  $sPath = Join-Path $dir 'script-seeded.txt'
  $sOk = 'n/a'
  if ($a.script_sha256 -and (Test-Path -LiteralPath $sPath)) {
    $sOk = ((Get-Sha256Lower $sPath) -eq $a.script_sha256.ToLower())
  }
  $gates = $a.instrument_gates_pass; $cls = $a.classification
  $assessmentResults += [pscustomobject]@{
    run=$r; runtime=$a.runtime; source_head=$a.source_head; classification=$cls; gates=$gates
    metricsMatch=$mOk; telemetryMatch=$tOk; scriptSourceMatch=$sOk
    metrics=$mName; telemetry=$tName
  }
}

$manifestResults | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'manifest-verification.json') -Encoding UTF8
$assessmentResults | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'assessment-verification.json') -Encoding UTF8

Write-Output '=== MANIFEST ==='
$manifestResults | Format-Table -AutoSize | Out-String -Width 250
Write-Output '=== ASSESSMENT ==='
$assessmentResults | Format-Table -AutoSize | Out-String -Width 250
Write-Output '=== BAD DETAIL ==='
$manifestResults | Where-Object { $_.bad } | ForEach-Object { "$($_.run): $($_.bad)" }
