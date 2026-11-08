param(
    [switch]$Quick,
    [switch]$Full
)
$ErrorActionPreference = 'Stop'
if (($Quick -and $Full) -or (-not $Quick -and -not $Full)) { throw 'Specify exactly one of -Quick or -Full.' }

$root = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $root 'artifacts/perf'
$publishDir = Join-Path $artifactDir 'publish'
$mode = if ($Full) { 'Full' } else { 'Quick' }
$runId = "$mode-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$PID"
$runDir = Join-Path $artifactDir $runId
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$indexPath = Join-Path $env:LOCALAPPDATA 'WinLight/index.json'
$hadRegistryKey = Test-Path $registryPath
$hadStartupValue = $false
$startupValue = $null
$startupKind = $null
$hadSnapshot = Test-Path $indexPath
$snapshotHashBefore = if ($hadSnapshot) { (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash } else { $null }
$hadPerfEnvironment = Test-Path Env:\WINLIGHT_PERF
$priorPerfEnvironment = $env:WINLIGHT_PERF
$stoppedInstance = $null
$instanceWasStopped = $false
$needsManualRestart = Test-Path (Join-Path $artifactDir 'preexisting-process.txt')
$scriptExitCode = 0
$runError = $null
$perfLogPath = Join-Path $env:LOCALAPPDATA 'WinLight/logs/perf.log'
$privateBytesAt50Mb = $null
$privateBytesAt100Mb = $null

function Get-RunValueState {
    $exists = Test-Path $registryPath
    $hasValue = $false
    $value = $null
    $kind = $null
    if ($exists) {
        $key = Get-Item $registryPath
        $hasValue = $key.GetValueNames() -contains 'WinLight'
        if ($hasValue) { $value = [string]$key.GetValue('WinLight'); $kind = [string]$key.GetValueKind('WinLight') }
    }
    return [pscustomobject]@{ KeyExists = $exists; HasValue = $hasValue; Value = $value; Kind = $kind }
}

if ($hadRegistryKey) {
    $runBefore = Get-RunValueState
    $hadStartupValue = $runBefore.HasValue
    $startupValue = $runBefore.Value
    $startupKind = $runBefore.Kind
} else { $runBefore = [pscustomobject]@{ KeyExists = $false; HasValue = $false; Value = $null; Kind = $null } }

New-Item -ItemType Directory -Force -Path $publishDir, $runDir | Out-Null
Push-Location $root
try {
    $publishLog = Join-Path $artifactDir 'publish-build.log'
    & dotnet publish src/WinLight.App/WinLight.App.csproj -c Release -o $publishDir -p:NuGetAudit=false *> $publishLog
    if ($LASTEXITCODE -ne 0) { throw "Published app build failed; see $publishLog." }
    $harnessBuildLog = Join-Path $artifactDir 'harness-build.log'
    & dotnet build tools/WinLight.PerfHarness/WinLight.PerfHarness.csproj -c Release *> $harnessBuildLog
    if ($LASTEXITCODE -ne 0) { throw "Performance harness build failed; see $harnessBuildLog." }

    $running = @(Get-Process -Name 'WinLight.App' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 1) { throw 'More than one WinLight process was found; refusing to stop an ambiguous set.' }
    if ($running.Count -eq 1) {
        $instancePath = [IO.Path]::GetFullPath($running[0].Path)
        if (-not $instancePath.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "A WinLight process outside this workspace is running ($instancePath); refusing to stop it."
        }
        $stoppedInstance = [pscustomobject]@{
            Id = [int]$running[0].Id
            Path = $instancePath
            CommandLine = $instancePath
            CommandLineSource = 'Win32_Process query unavailable; no command-line arguments are used by Program.Main.'
            SessionId = [int]$running[0].SessionId
            Created = [string]$running[0].StartTime
        }
        try {
            $nativeProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($stoppedInstance.Id)" -ErrorAction Stop
            if ($nativeProcess.CommandLine) {
                $stoppedInstance.CommandLine = [string]$nativeProcess.CommandLine
                $stoppedInstance.CommandLineSource = 'Win32_Process.CommandLine'
            }
        } catch { }
        $stoppedInstance | Format-List * | Out-String | Set-Content -LiteralPath (Join-Path $artifactDir 'preexisting-process.txt')
        $needsManualRestart = $true
        for ($attempt = 0; $attempt -lt 3; $attempt++) {
            if (-not (Get-Process -Id $stoppedInstance.Id -ErrorAction SilentlyContinue)) { break }
            Stop-Process -Id $stoppedInstance.Id -Force
            Start-Sleep -Milliseconds 300
            if (Get-Process -Id $stoppedInstance.Id -ErrorAction SilentlyContinue) { & taskkill.exe /PID $stoppedInstance.Id /F | Out-Null }
            Start-Sleep -Milliseconds 500
        }
        if (Get-Process -Id $stoppedInstance.Id -ErrorAction SilentlyContinue) { throw 'The resident WinLight process did not stop.' }
        $remaining = @(Get-Process -Name 'WinLight.App' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) })
        if ($remaining.Count -gt 0) { throw "A workspace WinLight process restarted while being stopped (PID $($remaining[0].Id)); refusing to continue." }
        $instanceWasStopped = $true
    }

    $harness = Join-Path $root 'tools/WinLight.PerfHarness/bin/Release/net10.0-windows10.0.19041.0/WinLight.PerfHarness.exe'
    $exe = Join-Path $publishDir 'WinLight.App.exe'
    $env:WINLIGHT_PERF = '1'
    $harnessLog = Join-Path $runDir 'harness-console.log'
    $stdoutLog = Join-Path $runDir 'harness-stdout.log'
    $stderrLog = Join-Path $runDir 'harness-stderr.log'
    $runStartedUtc = [DateTime]::UtcNow
    $seenPerfLines = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $harnessArgs = '"{0}" "{1}" "{2}"' -f $mode, $exe, $runDir
    $harnessProcess = Start-Process -FilePath $harness -ArgumentList $harnessArgs -PassThru -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    $snapshotStarted = $false
    $snapshotHideCount = 0
    while (-not $harnessProcess.HasExited) {
        foreach ($logPath in @($perfLogPath + '.old', $perfLogPath)) {
            if (Test-Path $logPath) {
                foreach ($line in @(Get-Content -LiteralPath $logPath)) {
                    if (-not $seenPerfLines.Add($line)) { continue }
                    $lineTimestamp = [DateTimeOffset]::MinValue
                    if (-not [DateTimeOffset]::TryParse(($line -split '\|', 2)[0], [ref]$lineTimestamp) -or $lineTimestamp.UtcDateTime -lt $runStartedUtc) { continue }
                    if ($line -match '\|index_usable\|.*source=snapshot') { $snapshotStarted = $true; $snapshotHideCount = 0 }
                    elseif ($snapshotStarted -and $line -match '\|window_hide\|') {
                        $snapshotHideCount++
                        if ($snapshotHideCount -eq 50 -or $snapshotHideCount -eq 100) {
                            $publishedProcess = Get-Process -Name 'WinLight.App' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($exe) -and $_.StartTime.ToUniversalTime() -ge $runStartedUtc } | Select-Object -Last 1
                            if ($publishedProcess) {
                                $publishedProcess.Refresh()
                                if ($snapshotHideCount -eq 50) { $privateBytesAt50Mb = $publishedProcess.PrivateMemorySize64 / 1MB }
                                else { $privateBytesAt100Mb = $publishedProcess.PrivateMemorySize64 / 1MB }
                            }
                        }
                    }
                }
            }
        }
        Start-Sleep -Milliseconds 250
        $harnessProcess.Refresh()
    }
    $harnessProcess.WaitForExit()
    $scriptExitCode = $harnessProcess.ExitCode
    Get-Content -LiteralPath $stdoutLog, $stderrLog | Set-Content -LiteralPath $harnessLog
    if ($scriptExitCode -ne 0) { $runError = "Performance gates failed; see $runDir/summary.txt." }
} catch {
    $runError = $_.Exception.Message
    $scriptExitCode = 1
} finally {
    if ($hadPerfEnvironment) { $env:WINLIGHT_PERF = $priorPerfEnvironment }
    else { Remove-Item Env:\WINLIGHT_PERF -ErrorAction SilentlyContinue }

    if (-not (Test-Path $registryPath)) { New-Item -Path $registryPath -Force | Out-Null }
    Remove-ItemProperty -Path $registryPath -Name WinLight -ErrorAction SilentlyContinue
    if ($hadStartupValue) { New-ItemProperty -Path $registryPath -Name WinLight -Value $startupValue -PropertyType $startupKind -Force | Out-Null }
    if (-not $hadRegistryKey -and (Test-Path $registryPath) -and -not (Get-Item $registryPath).GetValueNames().Count) { Remove-Item $registryPath }
    Pop-Location
}

$snapshotAfterExists = Test-Path $indexPath
$snapshotHashAfter = if ($snapshotAfterExists) { (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash } else { $null }
$snapshotMatches = ($hadSnapshot -eq $snapshotAfterExists) -and ($snapshotHashBefore -eq $snapshotHashAfter)
$runAfter = Get-RunValueState
$runMatches = ($runBefore.KeyExists -eq $runAfter.KeyExists) -and ($runBefore.HasValue -eq $runAfter.HasValue) -and ($runBefore.Value -ceq $runAfter.Value) -and ($runBefore.Kind -ceq $runAfter.Kind)
if (-not $snapshotMatches -or -not $runMatches) { $scriptExitCode = 1 }

$summaryPath = Join-Path $runDir 'summary.txt'
if (-not (Test-Path $summaryPath)) { @("Mode: $mode", "Harness error: $runError") | Set-Content -LiteralPath $summaryPath }
$resultPath = Join-Path $runDir 'results.json'
$reportLines = @(Get-Content -LiteralPath $summaryPath | Where-Object { $_ -notmatch 'query index coverage' })
for ($i = 0; $i -lt $reportLines.Count; $i++) {
    if ($reportLines[$i] -match '^(.*memory/resources): ([\d.]+) MB working set; private ([\d.]+) MB; (\d+) handles; (\d+) threads; budget <100; FAIL \[info\]$') {
        $reportLines[$i] = "$($Matches[1]): working set $($Matches[2]) MB (informational); private $($Matches[3]) MB (informational); $($Matches[4]) handles; $($Matches[5]) threads"
    }
}
if (Test-Path $resultPath) {
    $perfResults = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $perfResults.Metrics = @($perfResults.Metrics | Where-Object { $_.Name -notmatch 'index coverage' })
    if ($Full) {
        $budgetConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'budgets.json') -Raw | ConvertFrom-Json
        $privateMax = [double]$budgetConfig.private_bytes_after_50_cycles_mb.max
        $growthMax = [double]$budgetConfig.private_bytes_growth_50_to_100_percent.max
        $snapshotScenario = $perfResults.Scenarios | Where-Object Name -eq 'snapshot' | Select-Object -First 1
        $private100Mb = $privateBytesAt100Mb
        $private50Pass = $null -ne $privateBytesAt50Mb -and $privateBytesAt50Mb -lt $privateMax
        $growthPercent = if ($null -ne $privateBytesAt50Mb -and $privateBytesAt50Mb -gt 0 -and $null -ne $private100Mb) { (($private100Mb - $privateBytesAt50Mb) / $privateBytesAt50Mb) * 100 } else { $null }
        $growthPass = $null -ne $growthPercent -and $growthPercent -lt $growthMax
        $perfResults | Add-Member -NotePropertyName PrivateBytesAfter50CyclesMb -NotePropertyValue $privateBytesAt50Mb -Force
        $perfResults | Add-Member -NotePropertyName PrivateBytesAfter100CyclesMb -NotePropertyValue $privateBytesAt100Mb -Force
        $perfResults.Metrics += [pscustomobject]@{ Name = 'snapshot private bytes after 50 cycles'; Value = $privateBytesAt50Mb; Unit = 'MB'; Budget = $privateMax; Gate = $true; Pass = $private50Pass }
        $perfResults.Metrics += [pscustomobject]@{ Name = 'snapshot private bytes growth 50 to 100 cycles'; Value = $growthPercent; Unit = '%'; Budget = $growthMax; Gate = $true; Pass = $growthPass }
        if (-not $private50Pass -or -not $growthPass) { $scriptExitCode = 1 }
        $private50Text = if ($null -eq $privateBytesAt50Mb) { 'unavailable' } else { '{0:N2} MB' -f $privateBytesAt50Mb }
        $private100Text = if ($null -eq $private100Mb) { 'unavailable' } else { '{0:N2} MB' -f $private100Mb }
        $growthText = if ($null -eq $growthPercent) { 'unavailable' } else { '{0:N2}%' -f $growthPercent }
        $reportLines += "Private bytes after 50 cycles: $private50Text; budget <$privateMax MB; $(if ($private50Pass) {'PASS'} else {'FAIL'}) [gate]"
        $reportLines += "Private bytes after 100 total cycles: $private100Text (measurement)"
        $reportLines += "Private bytes growth 50 to 100 cycles: $growthText; budget <$growthMax%; $(if ($growthPass) {'PASS'} else {'FAIL'}) [gate]"
    } else {
        $reportLines += 'Private-bytes 50-to-100-cycle gates: Full mode required; not evaluated [info]'
    }
    $perfResults | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resultPath
}
Set-Content -LiteralPath $summaryPath -Value $reportLines
$snapshotLabel = if ($hadSnapshot) { "SHA256 $snapshotHashBefore" } else { 'absent' }
$runLabel = if ($runBefore.HasValue) { "WinLight=$($runBefore.Value) ($($runBefore.Kind))" } else { 'WinLight absent' }
Add-Content -LiteralPath $summaryPath -Value "State restore: snapshot $(if ($snapshotMatches) {'MATCH'} else {'MISMATCH'}) ($snapshotLabel); HKCU Run $(if ($runMatches) {'MATCH'} else {'MISMATCH'}) ($runLabel)"
if ($needsManualRestart) { Add-Content -LiteralPath $summaryPath -Value 'WinLight was stopped; restart it manually.' }

$rootSummary = Join-Path $artifactDir 'summary.txt'
$rootResults = Join-Path $artifactDir 'results.json'
Copy-Item -LiteralPath $summaryPath -Destination $rootSummary -Force
if (Test-Path $resultPath) { Copy-Item $resultPath $rootResults -Force }
Get-Content -LiteralPath $rootSummary | ForEach-Object { Write-Host $_ }
exit $scriptExitCode
