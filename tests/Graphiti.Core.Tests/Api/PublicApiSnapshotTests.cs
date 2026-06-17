using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace Graphiti.Core.Tests.Api;

/// <summary>
/// Locks the public API surface of the shipped assemblies (<c>Graphiti.Core</c> and
/// <c>Graphiti.Core.Drivers.Ladybug</c>) against accidental drift.
///
/// <para>
/// Each test reflects over an assembly with <see cref="ApiGenerator"/> and compares the generated
/// signature dump against a committed baseline fixture (<c>Api/&lt;assembly&gt;.approved.txt</c>). The
/// dump is signatures only — XML documentation is not included — so this snapshot composes with
/// documentation work without conflict.
/// </para>
///
/// <para>
/// When the public API changes on purpose, the test fails and writes the freshly generated surface
/// next to the baseline as <c>Api/&lt;assembly&gt;.received.txt</c>. Review the diff, and if the change
/// is intended, overwrite the baseline with the received contents and commit that as a deliberate,
/// reviewable edit. An unintended change is caught the same way but should be reverted in source.
/// </para>
/// </summary>
public class PublicApiSnapshotTests
{
    [Fact]
    public void GraphitiCore_PublicApi_MatchesApprovedBaseline()
        => AssertPublicApiMatches(typeof(Graphiti).Assembly, "Graphiti.Core");

#if !GRAPHITI_CORE_ONLY_TESTS
    [Fact]
    public void GraphitiCoreDriversLadybug_PublicApi_MatchesApprovedBaseline()
        => AssertPublicApiMatches(
            typeof(global::Graphiti.Core.Configuration.LadybugDbOptions).Assembly,
            "Graphiti.Core.Drivers.Ladybug");
#endif

    private static void AssertPublicApiMatches(Assembly assembly, string baselineName)
    {
        var options = new ApiGeneratorOptions
        {
            // Emit attributes on the public surface so attribute drift (e.g. an obsolete marker being
            // added/removed) is also caught, but keep the dump stable and assembly-attribute noise out.
            IncludeAssemblyAttributes = false,
        };

        var actualApi = Normalize(assembly.GeneratePublicApi(options));

        var apiDirectory = GetApiDirectory();
        var approvedPath = Path.Combine(apiDirectory, baselineName + ".approved.txt");
        var receivedPath = Path.Combine(apiDirectory, baselineName + ".received.txt");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(receivedPath, actualApi);
            Assert.Fail(
                $"Approved public API baseline is missing at '{approvedPath}'. " +
                $"A candidate baseline was written to '{receivedPath}'. " +
                "Review it and rename it to the approved file name to establish the baseline.");
        }

        var approvedApi = Normalize(File.ReadAllText(approvedPath));

        if (!string.Equals(approvedApi, actualApi, StringComparison.Ordinal))
        {
            File.WriteAllText(receivedPath, actualApi);
            Assert.Fail(
                $"The public API surface of {baselineName} has changed and no longer matches the " +
                $"approved baseline ('{approvedPath}').\n" +
                "If this change is INTENTIONAL, review the diff and update the baseline by copying " +
                $"the generated surface ('{receivedPath}') over the approved file, then commit it as a " +
                "deliberate API change.\n" +
                "If this change is UNINTENTIONAL, revert the offending change in src/ — this test exists " +
                "to catch accidental public API drift before a stable release.");
        }

        // Clean up any stale received file from a previous failing run once the API matches again.
        if (File.Exists(receivedPath))
        {
            File.Delete(receivedPath);
        }
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string GetApiDirectory([CallerFilePath] string thisFilePath = "")
    {
        // The baseline lives in source control next to this test file. In local builds, CallerFilePath
        // points there directly; deterministic CI builds can remap it to /_/, so fall back to walking
        // from the test output directory to the repository root.
        var directory = Path.GetDirectoryName(thisFilePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            return directory;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Graphiti.Core.CSharp.slnx")))
            {
                var apiDirectory = Path.Combine(current.FullName, "tests", "Graphiti.Core.Tests", "Api");
                if (Directory.Exists(apiDirectory))
                {
                    return apiDirectory;
                }
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the API fixture directory from source path '{thisFilePath}'.");
    }
}
