# One-shot quality gate: format + build + all tests.
# "Done" for any task means this script exits 0. Used by the pre-commit hook and CI.
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host '== format (verify only) ==' -ForegroundColor Cyan
    dotnet format Zurari.sln --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw 'Formatting issues found. Fix with: dotnet format Zurari.sln' }

    Write-Host '== build (warnings are errors) ==' -ForegroundColor Cyan
    dotnet build Zurari.sln
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    Write-Host '== test ==' -ForegroundColor Cyan
    dotnet test Zurari.sln --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    Write-Host 'ALL CHECKS GREEN' -ForegroundColor Green
}
finally {
    Pop-Location
}
