<#
.SYNOPSIS
  Builds and tests the whole solution.
.PARAMETER Configuration
  Debug or Release. Default Release.
.PARAMETER SkipTests
  Skip the test step (for speed during iterative work).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    Write-Host "==> Restore" -ForegroundColor Cyan
    dotnet restore Pos.slnx
    if ($LASTEXITCODE -ne 0) { throw "restore failed" }

    Write-Host "==> Build ($Configuration)" -ForegroundColor Cyan
    dotnet build Pos.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "build failed" }

    if (-not $SkipTests) {
        Write-Host "==> Test" -ForegroundColor Cyan
        dotnet test Pos.slnx -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "tests failed" }
    }

    Write-Host "==> OK" -ForegroundColor Green
} finally {
    Pop-Location
}
