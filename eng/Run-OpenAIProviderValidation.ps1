[CmdletBinding()]
param(
    [string]$Solution = "Graphiti.Core.CSharp.slnx",
    [string]$TestProject = "tests\Graphiti.Core.Tests\Graphiti.Core.Tests.csproj",
    [string]$SampleProject = "samples\Graphiti.Sample.OpenAI\Graphiti.Sample.OpenAI.csproj",
    [string]$Filter = "FullyQualifiedName~OpenAIProviderIntegrationTests",
    [switch]$SkipSample
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-ProviderValidationStep {
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

# Load a local, gitignored .env if present so validation is one command. Sets every NAME=VALUE pair
# as a process env var; if OPENAI_API_KEY is not provided directly, aliases the first variable whose
# name contains OPENAI_API_KEY (e.g. a project-scoped OPENAI_API_KEY_* name). Secrets are never echoed.
$envFile = Join-Path (Get-Location) ".env"
if (Test-Path $envFile) {
    $apiKeyAlias = $null
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#") -or -not $trimmed.Contains("=")) { continue }
        $idx = $trimmed.IndexOf("=")
        $name = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1).Trim().Trim('"').Trim("'")
        if (-not $name) { continue }
        Set-Item -Path "env:$name" -Value $value
        if ($name -like "*OPENAI_API_KEY*" -and -not $apiKeyAlias) { $apiKeyAlias = $value }
    }
    if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY) -and $apiKeyAlias) {
        $env:OPENAI_API_KEY = $apiKeyAlias
    }
}

if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    [Console]::Error.WriteLine("Set OPENAI_API_KEY (or provide a .env with an OPENAI_API_KEY* entry) before running live OpenAI provider validation.")
    exit 2
}

Write-Host "Using OPENAI_CHAT_MODEL=$($env:OPENAI_CHAT_MODEL)"
Write-Host "Using OPENAI_SMALL_MODEL=$($env:OPENAI_SMALL_MODEL)"
Write-Host "Using OPENAI_RERANKER_MODEL=$($env:OPENAI_RERANKER_MODEL)"
Write-Host "Using OPENAI_EMBEDDING_MODEL=$($env:OPENAI_EMBEDDING_MODEL)"
Write-Host "Using OPENAI_EMBEDDING_DIMENSIONS=$($env:OPENAI_EMBEDDING_DIMENSIONS)"

Invoke-ProviderValidationStep "restore" {
    dotnet restore $Solution --locked-mode
}

Invoke-ProviderValidationStep "build" {
    dotnet build $Solution --no-restore --no-incremental --verbosity minimal
}

Invoke-ProviderValidationStep "OpenAI integration tests" {
    dotnet test $TestProject --no-build --filter $Filter --verbosity minimal
}

if (-not $SkipSample) {
    Invoke-ProviderValidationStep "OpenAI sample" {
        dotnet run --project $SampleProject --no-build --no-restore
    }
}
