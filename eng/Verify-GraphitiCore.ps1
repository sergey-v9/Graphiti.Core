[CmdletBinding()]
param(
    [string]$Solution = "Graphiti.Core.CSharp.slnx",
    [string]$TestProject = "tests\Graphiti.Core.Tests\Graphiti.Core.Tests.csproj",
    [string]$FocusedFilter,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-VerifyStep {
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

Invoke-VerifyStep "restore" {
    dotnet restore $Solution --locked-mode
}

if ($FocusedFilter) {
    Invoke-VerifyStep "focused test" {
        dotnet test $TestProject --no-restore --filter $FocusedFilter --verbosity minimal
    }
}

Invoke-VerifyStep "format" {
    dotnet format $Solution --verify-no-changes --verbosity minimal
}

Invoke-VerifyStep "build" {
    dotnet build $Solution --no-restore --no-incremental --verbosity minimal
}

Invoke-VerifyStep "test" {
    dotnet test $Solution --no-build --verbosity minimal
}

if (-not $SkipPack) {
    Invoke-VerifyStep "pack core" {
        dotnet pack "src\Graphiti.Core\Graphiti.Core.csproj" --configuration Release --no-restore --verbosity minimal
    }
}
