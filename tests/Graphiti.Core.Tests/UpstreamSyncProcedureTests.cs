namespace Graphiti.Core.Tests;

public class UpstreamSyncProcedureTests
{
    [Fact]
    public void UpstreamSyncProcedure_ReferencesExecutableLibraryDeltaCheck()
    {
        var csharpRoot = FindCSharpRoot();
        var scriptPath = Path.Combine(csharpRoot, "eng", "Check-PythonUpstreamDelta.ps1");
        var procedurePath = Path.Combine(csharpRoot, ".agents", "notes", "upstream-sync-procedure.md");

        Assert.True(File.Exists(scriptPath));

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("graphiti_core", script, StringComparison.Ordinal);
        Assert.Contains("git", script, StringComparison.Ordinal);
        Assert.Contains("fetch", script, StringComparison.Ordinal);
        Assert.Contains("log", script, StringComparison.Ordinal);
        Assert.Contains("diff", script, StringComparison.Ordinal);
        Assert.Contains("name-status", script, StringComparison.Ordinal);
        Assert.Contains("FailOnDelta", script, StringComparison.Ordinal);
        Assert.Contains("Python baseline", script, StringComparison.Ordinal);

        var procedure = File.ReadAllText(procedurePath);
        Assert.Contains(@".\eng\Check-PythonUpstreamDelta.ps1 -Fetch", procedure, StringComparison.Ordinal);
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
