[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$CoreProject = "src\Graphiti.Core\Graphiti.Core.csproj",
    [string]$TestProject = "tests\Graphiti.Core.Tests\Graphiti.Core.Tests.csproj",
    [switch]$SkipTests,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-CoreOnlyStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Write-CoreOnlyNuGetConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    Set-Content -Path $Path -Value $content -Encoding UTF8
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "graphiti-core-only-$([Guid]::NewGuid().ToString('N'))"
$previousNuGetPackages = $env:NUGET_PACKAGES
$pushedLocation = $false

try {
    Push-Location $repositoryRoot
    $pushedLocation = $true
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $env:NUGET_PACKAGES = Join-Path $tempRoot ".nuget-packages"
    $nugetConfig = Join-Path $tempRoot "NuGet.config"
    Write-CoreOnlyNuGetConfig -Path $nugetConfig

    $coreOnlyProperty = "/p:GraphitiCoreOnlyTests=true"
    $nonProviderFilter = "FullyQualifiedName!~OpenAIProviderIntegrationTests"

    Invoke-CoreOnlyStep "restore core from nuget.org only" {
        dotnet restore $CoreProject `
            --configfile $nugetConfig `
            --no-cache `
            --verbosity minimal
    }

    Invoke-CoreOnlyStep "restore core-only tests from nuget.org only" {
        dotnet restore $TestProject `
            --configfile $nugetConfig `
            --no-cache `
            --verbosity minimal `
            $coreOnlyProperty
    }

    Invoke-CoreOnlyStep "format core" {
        dotnet format $CoreProject `
            --verify-no-changes `
            --no-restore `
            --verbosity minimal
    }

    Invoke-CoreOnlyStep "build core" {
        dotnet build $CoreProject `
            --configuration $Configuration `
            --no-restore `
            --no-incremental `
            --verbosity minimal
    }

    Invoke-CoreOnlyStep "build core-only tests" {
        dotnet build $TestProject `
            --configuration $Configuration `
            --no-restore `
            --no-incremental `
            --verbosity minimal `
            $coreOnlyProperty
    }

    if (-not $SkipTests) {
        Invoke-CoreOnlyStep "test core-only suite" {
            dotnet test $TestProject `
                --configuration $Configuration `
                --no-build `
                --filter $nonProviderFilter `
                --verbosity minimal `
                $coreOnlyProperty
        }
    }

    if (-not $SkipPack) {
        Invoke-CoreOnlyStep "pack core" {
            dotnet pack $CoreProject `
                --configuration $Configuration `
                --no-restore `
                --verbosity minimal
        }
    }
}
finally {
    if ($pushedLocation) {
        Pop-Location
    }

    $env:NUGET_PACKAGES = $previousNuGetPackages
    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
