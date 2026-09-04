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
    $trxDir = Join-Path $root 'artifacts/testresults'
    if (Test-Path $trxDir) { Remove-Item $trxDir -Recurse -Force }
    dotnet test Zurari.sln --no-build --logger trx --results-directory $trxDir
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    # The plan documents restate the test total. A number kept by hand goes stale by omission, and
    # it did, repeatedly - so it is checked here rather than remembered. The .trx files the test run
    # just wrote are the source, so this costs nothing.
    Write-Host '== plan status ==' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'status.ps1') -Verify -NoBuild

    Write-Host 'ALL CHECKS GREEN' -ForegroundColor Green
}
finally {
    Pop-Location
}
