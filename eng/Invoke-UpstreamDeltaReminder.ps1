[CmdletBinding()]
param(
    [string]$ParentRepository,
    [string]$ParityNote,
    [string]$TargetRef = "origin/main",
    [string]$LibraryPath = "graphiti_core",
    [string]$Anchor,
    [switch]$NoFetch
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath {
    param(
        [string]$Path,
        [string]$DefaultPath,
        [string]$BasePath
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Path)) {
        $DefaultPath
    }
    elseif ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $BasePath $Path
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

$scriptDirectory = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    $scriptDirectory = Split-Path -Parent $PSCommandPath
}

$csharpRoot = Split-Path -Parent $scriptDirectory
$resolvedParentRepository = Resolve-RepositoryPath `
    -Path $ParentRepository `
    -DefaultPath (Join-Path $csharpRoot "..") `
    -BasePath $csharpRoot
$resolvedParityNote = Resolve-RepositoryPath `
    -Path $ParityNote `
    -DefaultPath (Join-Path $csharpRoot ".agents\notes\parity.md") `
    -BasePath $csharpRoot

$checkScript = Join-Path $scriptDirectory "Check-PythonUpstreamDelta.ps1"

$arguments = @(
    "-NoLogo",
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    $checkScript,
    "-ParentRepository",
    $resolvedParentRepository,
    "-ParityNote",
    $resolvedParityNote,
    "-TargetRef",
    $TargetRef,
    "-LibraryPath",
    $LibraryPath
)

if (-not [string]::IsNullOrWhiteSpace($Anchor)) {
    $arguments += @("-Anchor", $Anchor)
}

if (-not $NoFetch) {
    $arguments += "-Fetch"
}

Write-Host "==> upstream library delta reminder"
$powerShell = (Get-Process -Id $PID).Path
$output = @(& $powerShell @arguments 2>&1)
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host "$_" }

if ($exitCode -ne 0) {
    throw "Check-PythonUpstreamDelta.ps1 failed with exit code $exitCode"
}

$deltaLine = "$LibraryPath delta detected"
$hasDelta = $output | Where-Object { "$_".IndexOf($deltaLine, [StringComparison]::Ordinal) -ge 0 }
if ($hasDelta) {
    Write-Warning (
        "$LibraryPath has upstream changes. This reminder is non-blocking; " +
        "follow .agents\notes\upstream-sync-procedure.md to classify and reconcile them.")
}
else {
    Write-Host "No upstream $LibraryPath delta detected."
}

exit 0
