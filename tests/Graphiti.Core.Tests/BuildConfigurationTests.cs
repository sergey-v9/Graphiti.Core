using System.Xml.Linq;

namespace Graphiti.Core.Tests;

public class BuildConfigurationTests
{
    [Fact]
    public void DirectoryBuildProps_EnforcesModernCompilerAndAnalyzerDefaults()
    {
        var csharpRoot = FindCSharpRoot();
        var props = XDocument.Load(Path.Combine(csharpRoot, "Directory.Build.props"));
        var properties = props.Root!
            .Elements("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);

        Assert.Equal("net10.0", properties["TargetFramework"]);
        Assert.Equal("enable", properties["ImplicitUsings"]);
        Assert.Equal("enable", properties["Nullable"]);
        Assert.Equal("latest", properties["LangVersion"]);
        Assert.Equal("latest", properties["AnalysisLevel"]);
        Assert.Equal("Recommended", properties["AnalysisMode"]);
        Assert.Equal("true", properties["EnableNETAnalyzers"]);
        Assert.Equal("true", properties["EnforceCodeStyleInBuild"]);
        Assert.Equal("true", properties["TreatWarningsAsErrors"]);
        Assert.Equal("true", properties["Deterministic"]);
        Assert.Equal("true", properties["NuGetAudit"]);
        Assert.Equal("all", properties["NuGetAuditMode"]);
        Assert.Equal("low", properties["NuGetAuditLevel"]);
    }

    [Fact]
    public void ProjectFiles_DoNotDuplicateCentralBuildDefaults()
    {
        var csharpRoot = FindCSharpRoot();
        var centralizedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "TargetFramework",
            "ImplicitUsings",
            "Nullable",
            "LangVersion",
            "AnalysisLevel",
            "AnalysisMode"
        };

        foreach (var projectPath in Directory.EnumerateFiles(csharpRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var project = XDocument.Load(projectPath);
            var duplicated = project.Root!
                .Elements("PropertyGroup")
                .Elements()
                .Select(element => element.Name.LocalName)
                .Where(centralizedProperties.Contains)
                .ToList();

            Assert.Empty(duplicated);
        }
    }

    [Fact]
    public void SourceFiles_UseFolderAlignedNamespaces()
    {
        var sourceRoot = Path.Combine(FindCSharpRoot(), "src", "Graphiti.Core");
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var directory = Path.GetDirectoryName(relativePath);
            if (string.IsNullOrEmpty(directory) || directory.Equals("Properties", StringComparison.Ordinal))
            {
                continue;
            }

            var firstSegment = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)[0];
            if (firstSegment is "bin" or "obj")
            {
                continue;
            }

            var expectedNamespace = "Graphiti.Core." + directory.Replace(
                Path.DirectorySeparatorChar,
                '.');
            expectedNamespace = expectedNamespace.Replace(Path.AltDirectorySeparatorChar, '.');
            var namespaceDeclaration = File.ReadLines(sourcePath)
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal));

            Assert.Equal($"namespace {expectedNamespace};", namespaceDeclaration);
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
