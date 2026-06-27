namespace Graphiti.Core.Tests;

public class UpstreamSyncProcedureTests
{
    [Fact]
    public void UpstreamSyncProcedure_ReferencesExecutableLibraryDeltaCheck()
    {
        var csharpRoot = FindCSharpRoot();
        var scriptPath = Path.Combine(csharpRoot, "eng", "Check-PythonUpstreamDelta.ps1");
        var reminderPath = Path.Combine(csharpRoot, "eng", "Invoke-UpstreamDeltaReminder.ps1");
        var procedurePath = Path.Combine(csharpRoot, ".agents", "notes", "upstream-sync-procedure.md");

        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(reminderPath));

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("graphiti_core", script, StringComparison.Ordinal);
        Assert.Contains("git", script, StringComparison.Ordinal);
        Assert.Contains("fetch", script, StringComparison.Ordinal);
        Assert.Contains("log", script, StringComparison.Ordinal);
        Assert.Contains("diff", script, StringComparison.Ordinal);
        Assert.Contains("name-status", script, StringComparison.Ordinal);
        Assert.Contains("FailOnDelta", script, StringComparison.Ordinal);
        Assert.Contains("Python baseline", script, StringComparison.Ordinal);

        var reminder = File.ReadAllText(reminderPath);
        Assert.Contains("Check-PythonUpstreamDelta.ps1", reminder, StringComparison.Ordinal);
        Assert.Contains("non-blocking", reminder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 0", reminder, StringComparison.Ordinal);

        var procedure = File.ReadAllText(procedurePath);
        Assert.Contains(@".\eng\Check-PythonUpstreamDelta.ps1 -Fetch", procedure, StringComparison.Ordinal);
        Assert.Contains(@".\eng\Invoke-UpstreamDeltaReminder.ps1", procedure, StringComparison.Ordinal);
        Assert.Contains("non-blocking", procedure, StringComparison.OrdinalIgnoreCase);
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
