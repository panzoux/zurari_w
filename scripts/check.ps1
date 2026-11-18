# One-shot quality gate: format + build + all tests.
# "Done" for any task means this script exits 0. Used by the pre-commit hook and CI.
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # A C0 control byte in a source or plan file is almost always an escape that a tool turned into
    # a literal (a path written as C:<BEL>). The compiler accepts it, so it is checked here.
    Write-Host '== control characters ==' -ForegroundColor Cyan
    $badBytes = @()
    $scan = @(
        @{ Dir = 'src'; Filter = '*.cs' }, @{ Dir = 'tests'; Filter = '*.cs' },
        @{ Dir = 'plan'; Filter = '*.md' }, @{ Dir = 'docs'; Filter = '*.md' })
    foreach ($s in $scan) {
        if (-not (Test-Path $s.Dir)) { continue }
        foreach ($f in Get-ChildItem $s.Dir -Recurse -File -Filter $s.Filter) {
            if ($f.FullName -match '[\\/](bin|obj)[\\/]') { continue }
            $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
            $line = 1
            foreach ($b in $bytes) {
                if ($b -eq 10) { $line++ }
                elseif ($b -lt 32 -and $b -ne 9 -and $b -ne 13) {
                    $badBytes += ('{0}:{1} (byte 0x{2:X2})' -f $f.FullName, $line, $b)
                }
            }
        }
    }
    if ($badBytes.Count -gt 0) {
        $badBytes | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw 'Control characters found in source files (see above).'
    }

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
