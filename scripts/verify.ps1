param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()
$warningCount = 0
$testPassed = 0
$testFailed = 0

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $env:TEMP "winlight-$Name-$PID.log"
    & dotnet @Arguments *> $log
    $exit = $LASTEXITCODE
    return @{ ExitCode = $exit; Log = $log }
}

Push-Location $root
try {
    $restore = Invoke-LoggedDotnet 'restore' @('restore', 'WinLight.sln', '-p:NuGetAudit=false')
    if ($restore.ExitCode -ne 0) { $failures.Add('restore') }
    $build = Invoke-LoggedDotnet 'build' @('build', 'WinLight.sln', '-c', 'Release', '--no-restore')
    $buildText = Get-Content -LiteralPath $build.Log -Raw
    $warningCount = ([regex]::Matches($buildText, '(?im)\bwarning\s+[A-Z]{1,3}\d+\b')).Count
    if ($build.ExitCode -ne 0) { $failures.Add('build') }
    if ($warningCount -gt 0) { $failures.Add('warnings') }
    $test = Invoke-LoggedDotnet 'test' @('test', 'WinLight.sln', '-c', 'Release', '--no-build', '--logger', 'console;verbosity=minimal')
    if ($test.ExitCode -ne 0) { $failures.Add('tests') }
    $testText = Get-Content -LiteralPath $test.Log -Raw
    $summaryMatch = [regex]::Match($testText, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
    if ($summaryMatch.Success) { $testFailed = [int]$summaryMatch.Groups[1].Value; $testPassed = [int]$summaryMatch.Groups[2].Value }
} finally { Pop-Location }

$status = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
Write-Host "Verification: $status"
Write-Host "Restore: $(if ($restore.ExitCode -eq 0) {'PASS'} else {'FAIL'})"
Write-Host "Release build: $(if ($build.ExitCode -eq 0) {'PASS'} else {'FAIL'})"
Write-Host "Build warnings: $warningCount (budget 0)"
Write-Host "Tests: $testPassed passed, $testFailed failed"
Write-Host "Failed checks: $(if ($failures.Count -eq 0) {'none'} else {$failures -join ', '})"
if ($failures.Count -gt 0) { exit 1 }
