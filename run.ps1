<#
.SYNOPSIS
    Stage-1 entry point for the fuaran-live sibling — launch the playground.
.DESCRIPTION
    fuaran-live is a serverless, client-only BYOK browser playground for the
    Fuaran UI language. This launcher installs dependencies and serves the Vite
    dev server on the workspace-reserved port 24040 (per the workspace CLAUDE.md
    "Port allocation" table: fuaran-live -> Vite 24040–24049, no server port —
    the app is static).

    Per the workspace "Sibling launcher conventions" in ../CLAUDE.md, every pnpm
    invocation routes through the `Invoke-Pnpm` named-helper wrapper to avoid the
    Node `pnpm.ps1` substring-slice argument-corruption bug. Never call `& pnpm`
    directly from a launcher.

.PARAMETER SkipInstall
    Skip `pnpm install`. Use when node_modules is already in sync.

.PARAMETER NoBrowser
    Serve without opening a browser tab.

.PARAMETER Build
    Produce a static production build in dist/ instead of serving the dev server.

.EXAMPLE
    .\run.ps1
    Install + serve the playground + open a browser tab.

.EXAMPLE
    .\run.ps1 -Build
    Install + produce a static dist/ (no server).
#>

[CmdletBinding()]
param(
    [switch] $SkipInstall,
    [switch] $NoBrowser,
    [switch] $Build
)

$ErrorActionPreference = 'Stop'

# Per workspace ../CLAUDE.md "Sibling launcher conventions": Node's *.ps1 shims
# have a substring-slice argument-corruption bug when invoked from inside another
# .ps1. Route every pnpm call through the .cmd binary via this wrapper. Note the
# ${LASTEXITCODE} brace form — `$LASTEXITCODE:` parses as a drive-qualified
# variable reference (a hard parse error).
function Invoke-Pnpm {
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments = $true)] $Arguments)
    # `Get-Command pnpm.cmd` returns EVERY pnpm.cmd on PATH — here two (the
    # Program Files Node shim + the %LOCALAPPDATA%\pnpm shim). $cmd.Source would
    # then be an array and `& $cmd.Source` concatenates the paths into one bogus
    # string. Pin to the first match (the `Select-Object -First 1` guard is
    # load-bearing — see the workspace CLAUDE.md "Sibling launcher conventions").
    $cmd = Get-Command pnpm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1
    & $cmd.Source @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm exited with code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

$pnpm = Get-Command pnpm.cmd -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pnpm) {
    Write-Error @"
pnpm was not found on PATH.

Install Node.js 22+ (https://nodejs.org/) and pnpm 9+ via:
    corepack enable
    corepack prepare pnpm@9 --activate

Then re-run this script.
"@
    exit 1
}

Push-Location $PSScriptRoot
try {
    # fuaran-live is now built entirely in F#/Fable (Phase 326): `pnpm run dev` /
    # `build` compile the app via `dotnet fable`, so the local Fable tool must be
    # restored. Cheap + idempotent once the manifest is in place.
    Write-Host "=== dotnet tool restore (Fable) ===" -ForegroundColor Cyan
    dotnet tool restore

    if (-not $SkipInstall) {
        Write-Host "=== pnpm install ===" -ForegroundColor Cyan
        Invoke-Pnpm install
    }

    if ($Build) {
        Write-Host ""
        Write-Host "=== building static dist/ ===" -ForegroundColor Cyan
        Invoke-Pnpm run build
        Write-Host ""
        Write-Host "Static build written to dist/. Serve it from any static host, or open dist/index.html directly." -ForegroundColor Green
        return
    }

    # Compile the F#/Fable app FIRST (a multi-minute step), THEN open the browser
    # and start Vite. The `dev` script chains `fable:app && vite`, so Vite does not
    # bind the port until Fable finishes — opening the tab before that left the
    # browser hitting a dead port for the whole compile, which reads as "not
    # running". Splitting the chain lets us defer the browser until Vite is next.
    Write-Host ""
    Write-Host "=== compiling Fuaran app (dotnet fable) ===" -ForegroundColor Cyan
    Invoke-Pnpm run fable:app

    $url = 'http://localhost:24040/'
    if (-not $NoBrowser) {
        # Fable is done; Vite binds within ~1s and then blocks, so open the browser
        # just before launch. strictPort means the server holds 24040 or fails loudly.
        Write-Host ""
        Write-Host "Opening $url" -ForegroundColor Green
        Start-Process $url
    }

    Write-Host ""
    Write-Host "=== serving fuaran-live on $url (Ctrl+C to stop) ===" -ForegroundColor Cyan
    Invoke-Pnpm exec vite
} finally {
    Pop-Location
}
