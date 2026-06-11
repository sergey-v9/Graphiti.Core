using System.Text;

namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugFulltextQuery
{
    internal static string Build(string? query, IReadOnlyList<string>? groupIds)
    {
        GraphitiHelpers.ValidateGroupIds(groupIds);

        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var source = query.AsSpan();
        var builder = new StringBuilder(query.Length);
        var termCount = 0;
        var index = 0;
        while (index < source.Length && termCount < SearchUtilities.MaxQueryLength)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index >= source.Length)
            {
                break;
            }

            var start = index;
            while (index < source.Length && !char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (termCount > 0)
            {
                builder.Append(' ');
            }

            builder.Append(source[start..index]);
            termCount++;
        }

        return builder.ToString();
    }
}
