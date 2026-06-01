using System.Text;

namespace Graphiti.Core.Internal.Helpers;

internal static class DeterministicCommunityText
{
    internal static string BuildCommunitySummary(IReadOnlyList<string> summaries)
    {
        var builder = new StringBuilder();
        foreach (var value in summaries)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(value);
        }

        var summary = builder.ToString();
        return TextUtilities.TruncateAtSentence(summary, TextUtilities.MaxSummaryChars) ?? summary;
    }

    internal static string BuildNodeSummary(EntityNode node) =>
        string.IsNullOrWhiteSpace(node.Summary)
            ? node.Name
            : $"{node.Name}: {node.Summary}";

    internal static string BuildCommunityName(IReadOnlyList<EntityNode> cluster)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StringBuilder? builder = null;

        foreach (var node in cluster)
        {
            var name = node.Name;
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                continue;
            }

            if (builder is null)
            {
                builder = new StringBuilder("Community: ");
                builder.Append(name);
            }
            else
            {
                builder.Append(", ");
                builder.Append(name);
            }

            if (names.Count == 3)
            {
                break;
            }
        }

        return builder?.ToString() ?? "Community";
    }
}
