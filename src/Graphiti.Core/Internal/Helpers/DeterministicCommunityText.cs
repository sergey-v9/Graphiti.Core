namespace Graphiti.Core.Internal.Helpers;

internal static class DeterministicCommunityText
{
    internal static string BuildCommunitySummary(IReadOnlyList<string> summaries)
    {
        var summary = string.Join("; ", summaries.Where(summary => !string.IsNullOrWhiteSpace(summary)));
        return TextUtilities.TruncateAtSentence(summary, TextUtilities.MaxSummaryChars) ?? summary;
    }

    internal static string BuildNodeSummary(EntityNode node) =>
        string.IsNullOrWhiteSpace(node.Summary)
            ? node.Name
            : $"{node.Name}: {node.Summary}";

    internal static string BuildCommunityName(IReadOnlyList<EntityNode> cluster)
    {
        var names = cluster
            .Select(node => node.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        return names.Count == 0
            ? "Community"
            : "Community: " + string.Join(", ", names);
    }
}
