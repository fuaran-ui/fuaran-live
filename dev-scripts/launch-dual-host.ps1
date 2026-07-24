#Requires -Version 7.0
<#
.SYNOPSIS
    Phase 85 — launch fuaran-live in dual render-host mode: the Fable watcher
    (F# host) + the Vite dev server with VITE_DUAL_HOST=1, so the preview pane
    renders the operator's tree through BOTH the TS and F# (Fable) hosts with a
    live toggle.

.DESCRIPTION
    Steps:
      1. pnpm install (unless -SkipInstall).
      2. dotnet tool restore (Fable 5 + Fantomas).
      3. Start the Fable watcher (`dotnet fable watch fable-host --outDir
         fable-host/output`) as a background process; Vite reads the emitted JS.
      4. Wait for fable-host/output/Host.js so Vite doesn't load the F# host
         page before Fable's first compile finishes.
      5. Serve the Vite dev server on port 24040 with VITE_DUAL_HOST=1.

    Per the workspace ../CLAUDE.md "Sibling launcher conventions", every pnpm
    invocation routes through the Invoke-Pnpm named-helper wrapper. The Fable
    watcher is left running; the script prints its PID so it can be stopped.

.PARAMETER SkipInstall
    Skip `pnpm install`.

.PARAMETER NoBrowser
    Serve without opening a browser tab.
#>
[CmdletBinding()]
param(
    [switch] $SkipInstall,
    [switch] $NoBrowser,
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# Per workspace ../CLAUDE.md "Sibling launcher conventions": route every pnpm
# call through the .cmd binary. The ${LASTEXITCODE} brace form is required —
# `$LASTEXITCODE:` parses as a drive-qualified variable reference (parse error).
function Invoke-Pnpm {
    # `Get-Command pnpm.cmd` returns EVERY pnpm.cmd on PATH — potentially several. `$cmd.Source`
    # would then be an array and `& $cmd.Source` concatenates the paths into one bogus string.
    # Pin to the first match (workspace CLAUDE.md "Sibling launcher conventions (mandate)").
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    $cmd = Get-Command pnpm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $cmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm exited with code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

Push-Location $repoRoot
try {
    if (-not $SkipInstall) {
        Write-Host "=== pnpm install ===" -ForegroundColor Cyan
        Invoke-Pnpm install
    }

    Write-Host "=== dotnet tool restore (Fable) ===" -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit ${LASTEXITCODE})" }

    Write-Host "=== dotnet fable watch fable-host ===" -ForegroundColor Cyan
    $fableLog = Join-Path $repoRoot 'fable-host/fable-watch.log'
    $fableProc = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('fable', 'watch', 'fable-host', '--outDir', 'fable-host/output') `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $fableLog `
        -RedirectStandardError "$fableLog.err" `
        -NoNewWindow -PassThru

    $expectedJs = Join-Path $repoRoot 'fable-host/output/Host.js'
    Write-Host "Waiting for Fable first compile -> $expectedJs"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path $expectedJs) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ($fableProc.HasExited) {
            throw "Fable watcher exited before first compile (exit $($fableProc.ExitCode)); see $fableLog"
        }
    }
    if (-not (Test-Path $expectedJs)) {
        throw "Fable did not produce $expectedJs within $TimeoutSeconds seconds; see $fableLog"
    }

    $url = 'http://localhost:24040/'
    if (-not $NoBrowser) {
        Write-Host "Opening $url" -ForegroundColor Green
        Start-Process $url | Out-Null
    }

    Write-Host ""
    Write-Host "=== serving fuaran-live (dual-host) on $url (Ctrl+C to stop) ===" -ForegroundColor Cyan
    Write-Host "  Fable watcher: PID $($fableProc.Id) -> $fableLog (stop with: Stop-Process -Id $($fableProc.Id))" -ForegroundColor DarkGray
    $env:VITE_DUAL_HOST = '1'
    Invoke-Pnpm run dev
}
finally {
    Pop-Location
}
