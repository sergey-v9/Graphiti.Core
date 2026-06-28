using System.Buffers;
using System.Text;

namespace Graphiti.Core.Search;

/// <summary>
/// Escapes Lucene query special characters so a raw query can be passed to a full-text index,
/// including the uppercase letters used by Lucene boolean operators (O, R, N, T, A, D).
/// </summary>
internal static class LuceneQueryEscaper
{
    private static readonly SearchValues<char> CharactersToEscape =
        SearchValues.Create("+-&|!(){}[]^\"~*?:\\/ORNTAD");

    /// <summary>Returns the query with every Lucene special character backslash-escaped.</summary>
    public static string Sanitize(string query)
    {
        var source = query ?? string.Empty;
        var firstEscaped = source.AsSpan().IndexOfAny(CharactersToEscape);
        return firstEscaped < 0
            ? source
            : EscapeCharacters(source, firstEscaped);
    }

    private static string EscapeCharacters(string source, int firstEscaped)
    {
        var builder = new StringBuilder(source.Length + 8);
        builder.Append(source.AsSpan(0, firstEscaped));

        for (var i = firstEscaped; i < source.Length; i++)
        {
            var current = source[i];
            if (CharactersToEscape.Contains(current))
            {
                builder.Append('\\');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
