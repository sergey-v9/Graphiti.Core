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

if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    [Console]::Error.WriteLine("Set OPENAI_API_KEY before running live OpenAI provider validation.")
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
