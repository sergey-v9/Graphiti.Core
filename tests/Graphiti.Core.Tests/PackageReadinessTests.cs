using System.Xml.Linq;

namespace Graphiti.Core.Tests;

public class PackageReadinessTests
{
    [Fact]
    public void CoreProject_HasNuGetMetadataAndCoreDriverDependencies()
    {
        var project = LoadProject("src", "Graphiti.Core", "Graphiti.Core.csproj");
        var properties = GetProperties(project);
        var packageReferences = GetPackageReferences(project);

        AssertShippablePackageMetadata(project, properties, "Graphiti.Core");
        Assert.Contains("temporal-graph", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("neo4j", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("Neo4j.Driver", packageReferences);
        // After the Step E package split, Graphiti.Core is LadybugDB-free so it restores from
        // nuget.org alone; the LadybugDB packages live in Graphiti.Core.Drivers.Ladybug instead.
        Assert.DoesNotContain("LadybugDB", packageReferences);
        Assert.DoesNotContain("LadybugDB.Native", packageReferences);
        Assert.DoesNotContain("OpenTelemetry", packageReferences);
    }

    [Fact]
    public void LadybugDriverProject_OwnsLadybugPackagesAndReferencesCore()
    {
        var project = LoadProject(
            "src",
            "Graphiti.Core.Drivers.Ladybug",
            "Graphiti.Core.Drivers.Ladybug.csproj");
        var properties = GetProperties(project);
        var packageReferences = GetPackageReferences(project);
        var projectReferences = project.Root!
            .Elements("ItemGroup")
            .Elements("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();

        AssertShippablePackageMetadata(project, properties, "Graphiti.Core.Drivers.Ladybug");
        Assert.Contains("temporal-graph", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("ladybugdb", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("kuzu", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("LadybugDB", packageReferences);
        Assert.Contains("LadybugDB.Native", packageReferences);
        Assert.Contains(
            projectReferences,
            value => value!.EndsWith(@"Graphiti.Core\Graphiti.Core.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ShippablePackageProjects_UseSameSemVerVersion()
    {
        var coreProperties = GetProperties(LoadProject("src", "Graphiti.Core", "Graphiti.Core.csproj"));
        var ladybugProperties = GetProperties(LoadProject(
            "src",
            "Graphiti.Core.Drivers.Ladybug",
            "Graphiti.Core.Drivers.Ladybug.csproj"));
        var version = coreProperties["Version"];

        AssertSemVerLike(version);
        Assert.Equal(version, ladybugProperties["Version"]);
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
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var testPackageReferences = testProject.Root!
            .Elements("ItemGroup")
            .Elements("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
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

    [Fact]
    public void VerifyScript_PacksBothShippableProjects()
    {
        var csharpRoot = FindCSharpRoot();
        var verifyScript = File.ReadAllText(Path.Combine(csharpRoot, "eng", "Verify-GraphitiCore.ps1"));

        Assert.Contains(@"src\Graphiti.Core\Graphiti.Core.csproj", verifyScript);
        Assert.Contains(
            @"src\Graphiti.Core.Drivers.Ladybug\Graphiti.Core.Drivers.Ladybug.csproj",
            verifyScript);
        Assert.Contains("dotnet pack $packageProject", verifyScript);
    }

    [Fact]
    public void VerifyScript_BuildsFreshPackageConsumersWithStrictNuGetSources()
    {
        var csharpRoot = FindCSharpRoot();
        var verifyScript = File.ReadAllText(Path.Combine(csharpRoot, "eng", "Verify-GraphitiCore.ps1"));

        Assert.Contains("package consumer smoke", verifyScript);
        Assert.Contains("GraphitiCorePackageSmoke", verifyScript);
        Assert.Contains("GraphitiLadybugPackageSmoke", verifyScript);
        Assert.Contains("Graphiti.Core.Drivers.Ladybug", verifyScript);
        Assert.Contains("Invoke-DotNetCommandOutput", verifyScript);
        Assert.Contains("\"run\"", verifyScript);
        Assert.Contains("BuildIndicesAndConstraintsAsync", verifyScript);
        Assert.Contains("AddTripletAsync", verifyScript);
        Assert.Contains("SearchAsync(\"Alice works on Atlas\"", verifyScript);
        Assert.Contains("smoke-edge", verifyScript);
        Assert.Contains("new Graphiti.Core.Graphiti(graphDriver: driver)", verifyScript);
        Assert.Contains("-ExpectedOutput \"InMemory:smoke-edge\"", verifyScript);
        Assert.Contains("-ExpectedOutput \"LadybugDb:smoke-edge\"", verifyScript);
        Assert.Contains("--configfile", verifyScript);
        Assert.Contains("--no-cache", verifyScript);
        Assert.Contains("<clear />", verifyScript);
        Assert.Contains("graphiti-core-pack", verifyScript);
        Assert.Contains("graphiti-ladybug-pack", verifyScript);
        Assert.Contains("ladybug-local", verifyScript);
        Assert.Contains("NUGET_PACKAGES", verifyScript);
    }

    [Fact]
    public void GeneratedXmlDocs_DoNotDescribeBulkInvalidationBackwards()
    {
        var xmlPath = Path.ChangeExtension(typeof(global::Graphiti.Core.Graphiti).Assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath), $"Expected XML documentation at {xmlPath}.");

        var xml = XDocument.Load(xmlPath);
        var summary = xml
            .Descendants("member")
            .Where(element => (string?)element.Attribute("name") is { } name
                              && name.StartsWith("M:Graphiti.Core.Graphiti.AddEpisodeBulkAsync", StringComparison.Ordinal))
            .Elements("summary")
            .Select(element => string.Join(" ", element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Single();

        Assert.Contains("same batch", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("less aggressively", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument LoadProject(params string[] paths)
    {
        var csharpRoot = FindCSharpRoot();
        var allPaths = new string[paths.Length + 1];
        allPaths[0] = csharpRoot;
        Array.Copy(paths, 0, allPaths, 1, paths.Length);
        return XDocument.Load(Path.Combine(allPaths));
    }

    private static Dictionary<string, string> GetProperties(XDocument project)
    {
        return project.Root!
            .Elements("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
    }

    private static HashSet<string> GetPackageReferences(XDocument project)
    {
        return project.Root!
            .Elements("ItemGroup")
            .Elements("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AssertShippablePackageMetadata(
        XDocument project,
        Dictionary<string, string> properties,
        string packageId)
    {
        Assert.Equal(packageId, properties["PackageId"]);
        Assert.False(string.IsNullOrWhiteSpace(properties["Version"]));
        AssertSemVerLike(properties["Version"]);
        Assert.False(string.IsNullOrWhiteSpace(properties["Title"]));
        Assert.Equal("Zep Software, Inc.; Graphiti Contributors", properties["Authors"]);
        Assert.Equal("Zep Software, Inc.", properties["Company"]);
        Assert.False(string.IsNullOrWhiteSpace(properties["Description"]));
        Assert.Contains("graphiti", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Contains("knowledge-graph", properties["PackageTags"], StringComparison.Ordinal);
        Assert.Equal("Apache-2.0", properties["PackageLicenseExpression"]);
        Assert.Equal("https://github.com/getzep/graphiti", properties["PackageProjectUrl"]);
        Assert.Equal("https://github.com/getzep/graphiti", properties["RepositoryUrl"]);
        Assert.Equal("git", properties["RepositoryType"]);
        Assert.Equal("README.md", properties["PackageReadmeFile"]);
        Assert.Equal("true", properties["PublishRepositoryUrl"]);
        Assert.Equal("true", properties["EmbedUntrackedSources"]);
        Assert.Equal("true", properties["IncludeSymbols"]);
        Assert.Equal("snupkg", properties["SymbolPackageFormat"]);
        Assert.Equal("true", properties["EnablePackageValidation"]);
        Assert.Equal("true", properties["GenerateDocumentationFile"]);
        Assert.Contains(
            project.Root!.Elements("ItemGroup").Elements("None"),
            element => element.Attribute("Pack")?.Value == "true"
                       && element.Attribute("PackagePath")?.Value == "\\"
                       && element.Attribute("Include")?.Value == @"..\..\README.md");
    }

    private static void AssertSemVerLike(string version)
    {
        var buildParts = version.Split('+', 2);
        var prereleaseParts = buildParts[0].Split('-', 2);
        var releaseSegments = prereleaseParts[0].Split('.');

        Assert.Equal(3, releaseSegments.Length);
        foreach (var segment in releaseSegments)
        {
            Assert.False(string.IsNullOrEmpty(segment));
            Assert.True(int.TryParse(segment, out _), $"Version segment '{segment}' must be numeric.");
            Assert.False(segment.Length > 1 && segment[0] == '0');
        }

        if (prereleaseParts.Length == 2)
        {
            AssertVersionIdentifierSet(prereleaseParts[1], "Prerelease");
        }

        if (buildParts.Length == 2)
        {
            AssertVersionIdentifierSet(buildParts[1], "Build metadata");
        }
    }

    private static void AssertVersionIdentifierSet(string value, string section)
    {
        Assert.False(string.IsNullOrWhiteSpace(value), $"{section} identifiers must be present.");
        foreach (var identifier in value.Split('.'))
        {
            Assert.False(string.IsNullOrEmpty(identifier), $"{section} identifier cannot be empty.");
            foreach (var character in identifier)
            {
                var valid =
                    character is >= '0' and <= '9'
                    || character is >= 'A' and <= 'Z'
                    || character is >= 'a' and <= 'z'
                    || character == '-';
                Assert.True(valid, $"{section} identifier '{identifier}' contains invalid character '{character}'.");
            }
        }
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
