[CmdletBinding()]
param(
    [string]$ParentRepository = "..",
    [string]$ParityNote = ".agents\notes\parity.md",
    [string]$TargetRef = "origin/main",
    [string]$LibraryPath = "graphiti_core",
    [string]$Anchor,
    [switch]$Fetch,
    [switch]$FailOnDelta
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git -C $Repository @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Host
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }

    return @($output | ForEach-Object { "$_" })
}

function Get-ParityAnchor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $content = Get-Content -Raw -LiteralPath $Path
    $regexOptions = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $match = [regex]::Match(
        $content,
        '\*\*Python baseline:\*\*.*?HEAD\s+`(?<sha>[0-9a-f]{7,40})`',
        $regexOptions)
    if (-not $match.Success) {
        throw "Could not find the Python baseline HEAD in $Path"
    }

    return $match.Groups["sha"].Value
}

$resolvedParent = (Resolve-Path -LiteralPath $ParentRepository).Path
$resolvedParityNote = (Resolve-Path -LiteralPath $ParityNote).Path
$resolvedAnchor = if ([string]::IsNullOrWhiteSpace($Anchor)) {
    Get-ParityAnchor -Path $resolvedParityNote
}
else {
    $Anchor
}

if ($Fetch) {
    Write-Host "==> fetch origin"
    Invoke-GitOutput -Repository $resolvedParent -Arguments @("fetch", "origin", "--no-tags") | Write-Host
}

$targetSha = @(Invoke-GitOutput -Repository $resolvedParent -Arguments @("rev-parse", $TargetRef))[0]
$revisionRange = "$resolvedAnchor..$TargetRef"

Write-Host "Anchor: $resolvedAnchor"
Write-Host "Target: $targetSha ($TargetRef)"
Write-Host "Library path: $LibraryPath"

$commitLog = @(Invoke-GitOutput -Repository $resolvedParent -Arguments @(
    "log",
    "--oneline",
    $revisionRange,
    "--",
    $LibraryPath))
$diffStat = @(Invoke-GitOutput -Repository $resolvedParent -Arguments @(
    "diff",
    "--stat",
    $revisionRange,
    "--",
    $LibraryPath))
$nameStatus = @(Invoke-GitOutput -Repository $resolvedParent -Arguments @(
    "diff",
    "--name-status",
    $revisionRange,
    "--",
    $LibraryPath))

Write-Host "==> library commits"
if ($commitLog.Count -eq 0) {
    Write-Host "(none)"
}
else {
    $commitLog | Write-Host
}

Write-Host "==> library diff stat"
if ($diffStat.Count -eq 0) {
    Write-Host "(none)"
}
else {
    $diffStat | Write-Host
}

Write-Host "==> library changed files"
if ($nameStatus.Count -eq 0) {
    Write-Host "(none)"
    Write-Host "No $LibraryPath delta from $resolvedAnchor to $targetSha."
    exit 0
}

$nameStatus | Write-Host
Write-Host "$LibraryPath delta detected from $resolvedAnchor to $targetSha."
if ($FailOnDelta) {
    exit 1
}
