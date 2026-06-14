[CmdletBinding()]
param(
    [string]$Solution = "Graphiti.Core.CSharp.slnx",
    [string]$TestProject = "tests\Graphiti.Core.Tests\Graphiti.Core.Tests.csproj",
    [string]$FocusedFilter,
    [switch]$SkipPack,
    [switch]$SkipPackageSmoke
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

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Invoke-DotNetCommandOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & dotnet @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Host
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }

    return $output
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $project = [xml](Get-Content -Raw $ProjectPath)
    foreach ($propertyGroup in $project.Project.PropertyGroup) {
        $property = $propertyGroup.SelectSingleNode($PropertyName)
        if ($property) {
            return $property.InnerText
        }
    }

    throw "Could not find property $PropertyName in $ProjectPath"
}

function Add-PackageReference {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $project = [xml](Get-Content -Raw $ProjectPath)
    $itemGroup = $project.CreateElement("ItemGroup")
    $packageReference = $project.CreateElement("PackageReference")
    $packageReference.SetAttribute("Include", $PackageId)
    $packageReference.SetAttribute("Version", $Version)
    [void]$itemGroup.AppendChild($packageReference)
    [void]$project.Project.AppendChild($itemGroup)
    $project.Save($ProjectPath)
}

function Write-SmokeNuGetConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [hashtable]$PackageSources
    )

    $sourceEntries = foreach ($source in $PackageSources.GetEnumerator()) {
        $key = [System.Security.SecurityElement]::Escape($source.Key)
        $value = [System.Security.SecurityElement]::Escape($source.Value)
        "    <add key=`"$key`" value=`"$value`" />"
    }

    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
$($sourceEntries -join [Environment]::NewLine)
  </packageSources>
</configuration>
"@

    Set-Content -Path (Join-Path $Directory "NuGet.config") -Value $content -Encoding UTF8
}

function Invoke-PackageConsumerSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [hashtable]$PackageSources,

        [Parameter(Mandatory = $true)]
        [string]$ProgramSource,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedOutput
    )

    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "graphiti-package-smoke-$([Guid]::NewGuid().ToString('N'))"
    $previousNuGetPackages = $env:NUGET_PACKAGES

    try {
        New-Item -ItemType Directory -Path $smokeRoot | Out-Null
        $env:NUGET_PACKAGES = Join-Path $smokeRoot ".nuget-packages"
        $projectDirectory = Join-Path $smokeRoot $Name
        $projectPath = Join-Path $projectDirectory "$Name.csproj"
        $configPath = Join-Path $projectDirectory "NuGet.config"

        Invoke-DotNetCommand -Arguments @(
            "new",
            "console",
            "--framework",
            "net10.0",
            "--name",
            $Name,
            "--output",
            $projectDirectory,
            "--no-restore")
        Add-PackageReference -ProjectPath $projectPath -PackageId $PackageId -Version $Version
        Write-SmokeNuGetConfig -Directory $projectDirectory -PackageSources $PackageSources
        Set-Content -Path (Join-Path $projectDirectory "Program.cs") -Value $ProgramSource -Encoding UTF8

        Invoke-DotNetCommand -Arguments @(
            "restore",
            $projectPath,
            "--configfile",
            $configPath,
            "--no-cache",
            "--verbosity",
            "minimal")
        Invoke-DotNetCommand -Arguments @(
            "build",
            $projectPath,
            "--no-restore",
            "--verbosity",
            "minimal")
        $output = Invoke-DotNetCommandOutput -Arguments @(
            "run",
            "--project",
            $projectPath,
            "--no-restore",
            "--no-build",
            "--verbosity",
            "minimal")
        $actualLines = @($output | ForEach-Object { "$_" } | Where-Object { $_.Trim().Length -gt 0 })
        $actualOutput = if ($actualLines.Count -eq 0) { "" } else { $actualLines[-1].Trim() }
        if ($actualOutput -ne $ExpectedOutput) {
            throw "$Name produced final output '$actualOutput'; expected '$ExpectedOutput'"
        }
    }
    finally {
        $env:NUGET_PACKAGES = $previousNuGetPackages
        if (Test-Path $smokeRoot) {
            Remove-Item -LiteralPath $smokeRoot -Recurse -Force
        }
    }
}

function Invoke-PackageConsumerSmokes {
    $version = Get-ProjectProperty -ProjectPath "src\Graphiti.Core\Graphiti.Core.csproj" -PropertyName "Version"
    $corePackageSource = (Resolve-Path "src\Graphiti.Core\bin\Release").Path
    $ladybugPackageSource = (Resolve-Path "src\Graphiti.Core.Drivers.Ladybug\bin\Release").Path
    $ladybugLocalSource = (Resolve-Path "..\..\ladybug\tools\csharp_api\artifacts").Path
    $nugetSource = "https://api.nuget.org/v3/index.json"

    Invoke-PackageConsumerSmoke `
        -Name "GraphitiCorePackageSmoke" `
        -PackageId "Graphiti.Core" `
        -Version $version `
        -PackageSources @{
            "graphiti-core-pack" = $corePackageSource
            "nuget.org" = $nugetSource
        } `
        -ProgramSource @'
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;

await using var graphiti = new Graphiti.Core.Graphiti();
await graphiti.BuildIndicesAndConstraintsAsync();
await graphiti.AddTripletAsync(
    new EntityNode { Uuid = "smoke-alice", Name = "Alice", GroupId = "smoke" },
    new EntityEdge
    {
        Uuid = "smoke-edge",
        Name = "WORKS_ON",
        Fact = "Alice works on Atlas",
        GroupId = "smoke",
        ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    },
    new EntityNode { Uuid = "smoke-atlas", Name = "Atlas", GroupId = "smoke" });
var hits = await graphiti.SearchAsync("Alice works on Atlas", groupIds: new[] { "smoke" }, numResults: 1);
Console.WriteLine($"{graphiti.Driver.Provider}:{hits.Single().Uuid}");
'@ `
        -ExpectedOutput "InMemory:smoke-edge"

    Invoke-PackageConsumerSmoke `
        -Name "GraphitiLadybugPackageSmoke" `
        -PackageId "Graphiti.Core.Drivers.Ladybug" `
        -Version $version `
        -PackageSources @{
            "graphiti-core-pack" = $corePackageSource
            "graphiti-ladybug-pack" = $ladybugPackageSource
            "ladybug-local" = $ladybugLocalSource
            "nuget.org" = $nugetSource
        } `
        -ProgramSource @'
using Graphiti.Core.Drivers.Ladybug;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;

await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
await using var graphiti = new Graphiti.Core.Graphiti(graphDriver: driver);
await graphiti.BuildIndicesAndConstraintsAsync();
await graphiti.AddTripletAsync(
    new EntityNode { Uuid = "smoke-alice", Name = "Alice", GroupId = "smoke" },
    new EntityEdge
    {
        Uuid = "smoke-edge",
        Name = "WORKS_ON",
        Fact = "Alice works on Atlas",
        GroupId = "smoke",
        ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    },
    new EntityNode { Uuid = "smoke-atlas", Name = "Atlas", GroupId = "smoke" });
var hits = await graphiti.SearchAsync("Alice works on Atlas", groupIds: new[] { "smoke" }, numResults: 1);
Console.WriteLine($"{graphiti.Driver.Provider}:{hits.Single().Uuid}");
'@ `
        -ExpectedOutput "LadybugDb:smoke-edge"
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
    $packageProjects = @(
        "src\Graphiti.Core\Graphiti.Core.csproj",
        "src\Graphiti.Core.Drivers.Ladybug\Graphiti.Core.Drivers.Ladybug.csproj"
    )

    foreach ($packageProject in $packageProjects) {
        Invoke-VerifyStep "pack $packageProject" {
            dotnet pack $packageProject --configuration Release --no-restore --verbosity minimal
        }
    }

    if (-not $SkipPackageSmoke) {
        Invoke-VerifyStep "package consumer smoke" {
            Invoke-PackageConsumerSmokes
        }
    }
}
