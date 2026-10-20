#!/usr/bin/env pwsh
param([switch]$Fast, [switch]$Files)

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "=== dotnet ===" -ForegroundColor Cyan
    & dotnet --version

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
    $buildOutput = (& dotnet build Pos.slnx --nologo 2>&1) | Out-String
    $buildExit = $LASTEXITCODE
    Write-Host $buildOutput
    if ($buildExit -ne 0) {
        Write-Host "BUILD FAILED" -ForegroundColor Red
        return 1
    }
    Write-Host "BUILD OK" -ForegroundColor Green

    if ($Fast) { return 0 }

    Write-Host ""
    Write-Host "=== Tests ===" -ForegroundColor Cyan
    $testOutput = (& dotnet test Pos.slnx --no-build --nologo 2>&1) | Out-String
    $testExit = $LASTEXITCODE
    Write-Host $testOutput

    if ($testExit -ne 0) {
        Write-Host "TESTS FAILED" -ForegroundColor Red
        $testOutput -split "`n" |
            Where-Object { $_ -match "\[FAIL\]|Expected:|Actual:|Error Message:|Assert\." } |
            ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        return 2
    }
    Write-Host "TESTS OK" -ForegroundColor Green

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
    return 0
} finally {
    Pop-Location
}
