using System.Xml.Linq;

namespace Graphiti.Core.Tests;

public class PackageReadinessTests
{
    [Fact]
    public void CoreProject_HasNuGetMetadataAndNoUnusedOpenTelemetryDependency()
    {
        var csharpRoot = FindCSharpRoot();
        var project = XDocument.Load(Path.Combine(
            csharpRoot,
            "src",
            "Graphiti.Core",
            "Graphiti.Core.csproj"));
        var properties = project.Root!
            .Elements("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
        var packageReferences = project.Root!
            .Elements("ItemGroup")
            .Elements("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal("Graphiti.Core", properties["PackageId"]);
        Assert.Equal("Apache-2.0", properties["PackageLicenseExpression"]);
        Assert.Equal("README.md", properties["PackageReadmeFile"]);
        Assert.Equal("https://github.com/getzep/graphiti", properties["RepositoryUrl"]);
        Assert.Equal("true", properties["EnablePackageValidation"]);
        Assert.Contains("temporal-graph", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("Neo4j.Driver", packageReferences);
        Assert.DoesNotContain("OpenTelemetry", packageReferences);
        Assert.Contains(
            project.Root.Elements("ItemGroup").Elements("None"),
            element => element.Attribute("Pack")?.Value == "true"
                       && element.Attribute("PackagePath")?.Value == "\\"
                       && element.Attribute("Include")?.Value == @"..\..\README.md");
    }

    [Fact]
    public void TestDependencies_UseCurrentPackagesAndDoNotCarryUnusedVersions()
    {
        var csharpRoot = FindCSharpRoot();
        var props = XDocument.Load(Path.Combine(csharpRoot, "Directory.Packages.props"));
        var testProject = XDocument.Load(Path.Combine(
            csharpRoot,
            "tests",
            "Graphiti.Core.Tests",
            "Graphiti.Core.Tests.csproj"));
        var packageVersions = props.Root!
            .Elements("ItemGroup")
            .Elements("PackageVersion")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);
        var testPackageReferences = testProject.Root!
            .Elements("ItemGroup")
            .Elements("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("OpenTelemetry", packageVersions);
        Assert.DoesNotContain("Microsoft.Extensions.Resilience", packageVersions);
        Assert.Contains("Polly.Core", packageVersions);
        Assert.DoesNotContain("xunit", packageVersions);
        Assert.Contains("xunit.v3", packageVersions);
        Assert.DoesNotContain("xunit", testPackageReferences);
        Assert.Contains("xunit.v3", testPackageReferences);
    }

    [Fact]
    public void CSharpGitIgnore_ExcludesBuildAndPackageOutputs()
    {
        var csharpRoot = FindCSharpRoot();
        var gitIgnore = File.ReadAllLines(Path.Combine(csharpRoot, ".gitignore"));

        Assert.Contains("bin/", gitIgnore);
        Assert.Contains("obj/", gitIgnore);
        Assert.Contains("*.nupkg", gitIgnore);
        Assert.Contains("*.snupkg", gitIgnore);
    }

    private static string FindCSharpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Graphiti.Core.CSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the csharp solution root.");
    }
}
