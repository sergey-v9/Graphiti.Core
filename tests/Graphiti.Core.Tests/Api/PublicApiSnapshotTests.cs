using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace Graphiti.Core.Tests.Api;

/// <summary>
/// Locks the public API surface of the <c>Graphiti.Core</c> assembly against accidental drift.
///
/// <para>
/// The test reflects over the shipped assembly with <see cref="ApiGenerator"/> and compares the
/// generated signature dump against a committed baseline fixture
/// (<c>Api/Graphiti.Core.approved.txt</c>). The dump is signatures only — XML documentation is not
/// included — so this snapshot composes with documentation work without conflict.
/// </para>
///
/// <para>
/// When the public API changes on purpose, the test fails and writes the freshly generated surface
/// next to the baseline as <c>Api/Graphiti.Core.received.txt</c>. Review the diff, and if the change
/// is intended, overwrite the baseline with the received file (or copy the received contents over the
/// approved file) and commit that as a deliberate, reviewable edit. An unintended change is caught the
/// same way but should be reverted in source instead.
/// </para>
/// </summary>
public class PublicApiSnapshotTests
{
    private const string ApprovedFileName = "Graphiti.Core.approved.txt";
    private const string ReceivedFileName = "Graphiti.Core.received.txt";

    [Fact]
    public void PublicApi_MatchesApprovedBaseline()
    {
        var options = new ApiGeneratorOptions
        {
            // Emit attributes on the public surface so attribute drift (e.g. an obsolete marker being
            // added/removed) is also caught, but keep the dump stable and assembly-attribute noise out.
            IncludeAssemblyAttributes = false,
        };

        var actualApi = typeof(Graphiti).Assembly.GeneratePublicApi(options);
        actualApi = Normalize(actualApi);

        var apiDirectory = GetApiDirectory();
        var approvedPath = Path.Combine(apiDirectory, ApprovedFileName);
        var receivedPath = Path.Combine(apiDirectory, ReceivedFileName);

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
                "The public API surface of Graphiti.Core has changed and no longer matches the " +
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
        // The baseline lives in source control next to this test file, not in the build output, so
        // anchor to the compiler-provided path of this source file rather than AppContext.BaseDirectory.
        var directory = Path.GetDirectoryName(thisFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Could not locate the API fixture directory from source path '{thisFilePath}'.");
        }

        return directory;
    }
}
