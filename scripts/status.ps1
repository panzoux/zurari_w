# Writes the facts the plan documents restate from elsewhere, so they cannot go stale by omission.
#
# Two things drift, both because they are copies of something the repository already knows:
#   - the test total recorded in the phase plan
#   - "(this commit)" placeholders left in a Status table, waiting for the hash of the commit that
#     had not been made yet when the row was written
#
# check.ps1 verifies the first and refuses to pass while it is wrong. Run this to fix it, the same
# way `dotnet format` fixes what check.ps1's format step rejects.
#Requires -Version 5.1
[CmdletBinding()]
param(
    # Report what would change and exit non-zero if anything would, without writing. Used by check.ps1.
    [switch]$Verify,
    # Skip running the tests and take the total from a previous run's .trx files.
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $trxDir = Join-Path $root 'artifacts/testresults'

    if (-not $NoBuild) {
        if (Test-Path $trxDir) { Remove-Item $trxDir -Recurse -Force }
        dotnet test Zurari.sln --logger trx --results-directory $trxDir | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; the status is only worth recording when they pass.' }
    }

    if (-not (Test-Path $trxDir)) { throw "No test results in $trxDir. Run without -NoBuild." }

    # From the .trx rather than the console summary: the console output is localized, and parsing
    # "合格:" would work here and nowhere else.
    $total = 0
    foreach ($trx in Get-ChildItem $trxDir -Filter *.trx) {
        $total += [int]([xml](Get-Content -LiteralPath $trx.FullName)).TestRun.ResultSummary.Counters.total
    }
    if ($total -le 0) { throw "Read a test total of $total, which cannot be right." }

    $head = (git rev-parse --short HEAD).Trim()
    $changes = @()

    foreach ($doc in Get-ChildItem (Join-Path $root 'plan') -Filter *.md) {
        $text = Get-Content -LiteralPath $doc.FullName -Raw
        $updated = $text

        # "640 tests, `scripts/check.ps1` green." and "640 テスト green"
        $updated = [regex]::Replace($updated, '\d+(?= tests, `scripts/check\.ps1` green)', $total)
        $updated = [regex]::Replace($updated, '\d+(?= テスト green)', $total)

        # A Status row written before the commit it describes existed.
        $updated = $updated.Replace('(this commit)', "``$head``")

        if ($updated -ne $text) {
            $changes += $doc.Name
            if (-not $Verify) {
                [System.IO.File]::WriteAllText($doc.FullName, $updated)
            }
        }
    }

    if ($changes.Count -eq 0) {
        Write-Host "status current ($total tests)" -ForegroundColor Green
        exit 0
    }

    if ($Verify) {
        throw "Plan status is stale in: $($changes -join ', '). Fix with: scripts/status.ps1 -NoBuild"
    }

    Write-Host "status updated ($total tests): $($changes -join ', ')" -ForegroundColor Yellow
}
finally {
    Pop-Location
}
