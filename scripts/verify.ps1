#!/usr/bin/env pwsh
# verify.ps1 - One-command health check.
#
# Usage:
#   .\scripts\verify.ps1          # build + tests
#   .\scripts\verify.ps1 -Fast    # build only
#   .\scripts\verify.ps1 -Files   # also print every source file with size
#
# Exit: 0 = green, 1 = build failed, 2 = tests failed.

param([switch]$Fast, [switch]$Files)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "=== dotnet ===" -ForegroundColor Cyan
    dotnet --version

    if ($Files) {
        Write-Host ""
        Write-Host "=== Source files ===" -ForegroundColor Cyan
        Get-ChildItem -Recurse -File -Include *.cs,*.xaml,*.sql |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
            Sort-Object FullName |
            ForEach-Object {
                $rel = $_.FullName.Substring($root.Length + 1)
                Write-Host ("{0,9}  {1}" -f $_.Length, $rel)
            }
    }

    Write-Host ""
    Write-Host "=== Build ===" -ForegroundColor Cyan
    $buildOutput = dotnet build Pos.slnx --nologo 2>&1
    $buildOutput | Out-Host
    $buildErrors = $buildOutput | Select-String -Pattern "error CS|error MC|error NETSDK|error MSB"

    if ($buildErrors) {
        Write-Host ""
        Write-Host "BUILD FAILED - errors:" -ForegroundColor Red
        $buildErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "BUILD OK" -ForegroundColor Green

    if ($Fast) { exit 0 }

    Write-Host ""
    Write-Host "=== Tests ===" -ForegroundColor Cyan
    $testOutput = dotnet test Pos.slnx --no-build --nologo 2>&1
    $testOutput | Out-Host

    $summary = ($testOutput | Select-String -Pattern "Test summary:").ToString()
    if ($summary -match "failed: ([1-9])") {
        Write-Host ""
        Write-Host "TESTS FAILED" -ForegroundColor Red
        $testOutput | Select-String -Pattern "\[FAIL\]|Expected:|Actual:|Error Message:" | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        exit 2
    }
    Write-Host "TESTS OK" -ForegroundColor Green

    # Runtime errors from the app log
    $logDir = "C:\ProgramData\MyPOS\logs"
    if (Test-Path $logDir) {
        $errs = Get-ChildItem "$logDir\*.log" | Get-Content | Select-String -Pattern "\[ERR\]" | Select-Object -Last 5
        if ($errs) {
            Write-Host ""
            Write-Host "Recent runtime errors (last 5):" -ForegroundColor Yellow
            $errs | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        }
    }

    Write-Host ""
    Write-Host "ALL GREEN" -ForegroundColor Green
    exit 0
} finally {
    Pop-Location
}
